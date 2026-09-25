using System.Security.Claims;
using Courier.Audit.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courier.International.Application;

public class CarrierBookingService
{
    private const int MaxIdempotencyKeyLength = 200;
    private const int MaxCarrierAwbLength = 100;
    private const int MaxServiceLevelLength = 100;
    private const int MaxFailureReasonLength = 1000;
    // Fix round 1, finding 1: how old an unresolved Pending booking must be before a later /book
    // call is allowed to resume it, instead of reporting "still in progress" forever.
    private static readonly TimeSpan StalePendingThreshold = TimeSpan.FromMinutes(2);

    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly ICarrierProviderRegistry _registry;
    private readonly ICarrierSecretProtector _secretProtector;
    private readonly IObjectStorage _objectStorage;
    private readonly ICarrierJobScheduler _scheduler;
    private readonly ILogger<CarrierBookingService> _logger;

    public CarrierBookingService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger,
        IShipmentTransitionService shipmentTransitionService, IBranchAuthorizationChecker branchAuthorizationChecker,
        ICarrierProviderRegistry registry, ICarrierSecretProtector secretProtector, IObjectStorage objectStorage,
        ICarrierJobScheduler scheduler, ILogger<CarrierBookingService> logger)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _shipmentTransitionService = shipmentTransitionService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _registry = registry;
        _secretProtector = secretProtector;
        _objectStorage = objectStorage;
        _scheduler = scheduler;
        _logger = logger;
    }

    // Fix round 1, finding 1: cancellationToken is deliberately unused past the trivial header
    // check below. Once a booking attempt begins it must always run to completion — a client
    // disconnect (ASP.NET Core keeps the request executing unless code observes the token) must
    // never leave a booking stuck Pending, and neither must an already-cancelled token passed in.
    // The carrier call's own resilience pipeline enforces its own timeout independently.
    public async Task<CarrierBookingResponse?> BookAsync(Guid shipmentId, BookCarrierRequest request, string? idempotencyKey, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganization();
        var key = idempotencyKey?.Trim() ?? string.Empty;
        if (key.Length == 0 || key.Length > MaxIdempotencyKeyLength)
        {
            throw new InternationalShipmentValidationException($"An Idempotency-Key header of at most {MaxIdempotencyKeyLength} characters is required.");
        }

        var target = await LoadVisibleAsync(shipmentId, actingUser, "international.book", CancellationToken.None);
        if (target is null)
        {
            return null;
        }
        var (leg, scan) = target.Value;

        // Idempotent replay: same key -> same outcome, the carrier is never called twice.
        var replay = await _dbContext.Set<CarrierBooking>().FirstOrDefaultAsync(b => b.IdempotencyKey == key, CancellationToken.None);
        if (replay is not null)
        {
            if (replay.InternationalLegId != leg.Id || replay.CarrierAccountId != request.CarrierAccountId)
            {
                throw new InternationalShipmentValidationException("This Idempotency-Key was already used for a different booking.");
            }
            // Fix round 2, finding i: a same-key retry of a booking that is Pending but STALE (the
            // original attempt was interrupted, e.g. by a crash) must not be blocked forever either
            // — fall through so the live-booking lookup below finds and resumes this exact row,
            // instead of reporting "still in progress" indefinitely.
            var replayIsStalePending = replay.Status == CarrierBookingStatus.Pending && DateTimeOffset.UtcNow - replay.UpdatedAt >= StalePendingThreshold;
            if (!replayIsStalePending)
            {
                return replay.Status switch
                {
                    CarrierBookingStatus.Failed => throw new CarrierBookingFailedException(replay.FailureReason ?? "Carrier booking failed."),
                    CarrierBookingStatus.Pending => throw new InternationalShipmentValidationException("This booking is still in progress."),
                    _ => await ToResponseAsync(replay, CancellationToken.None)
                };
            }
        }

        if (scan.Direction != ShipmentDirection.Outbound)
        {
            throw new InternationalShipmentValidationException("Only outbound shipments are booked with a carrier; link an inbound carrier booking instead.");
        }
        if (scan.CurrentStatus != ShipmentStatus.CustomsPrepared)
        {
            throw new InternationalShipmentValidationException($"Shipment must be CustomsPrepared to book a carrier, was {scan.CurrentStatus}.");
        }
        var (account, provider) = await RequireActiveAccountAsync(request.CarrierAccountId, CancellationToken.None);

        // Fix round 1, finding 1: look for any still-live booking for this leg — a fresh/older
        // Pending row from an attempt that never resolved, or an already-Booked one — before
        // creating a new one under a different Idempotency-Key.
        var live = await _dbContext.Set<CarrierBooking>().FirstOrDefaultAsync(b => b.InternationalLegId == leg.Id
            && (b.Status == CarrierBookingStatus.Pending || b.Status == CarrierBookingStatus.Booked), CancellationToken.None);
        if (live is not null)
        {
            if (live.Status == CarrierBookingStatus.Booked)
            {
                throw new InternationalShipmentValidationException("This shipment already has a carrier booking. Cancel it first.");
            }
            if (DateTimeOffset.UtcNow - live.UpdatedAt < StalePendingThreshold)
            {
                throw new InternationalShipmentValidationException("This booking is still in progress.");
            }
            // Stale Pending: a previous attempt was interrupted between committing the Pending row
            // and completing the carrier call (e.g. a process crash) — resume it with its own
            // stored key rather than leaving it stuck forever.
            if (live.CarrierAccountId != account.Id)
            {
                throw new InternationalShipmentValidationException(
                    "This shipment has a stale, unresolved carrier booking on a different carrier account. Cancel or resolve it before booking again.");
            }
            var resumeManualAwb = ValidateManualAwb(request, account, provider);
            if (resumeManualAwb is not null)
            {
                await EnsureCarrierAwbFreeAsync(account.Id, resumeManualAwb, CancellationToken.None);
            }
            var resumeShipmentDetail = await _shipmentTransitionService.GetResponseAsync(shipmentId, CancellationToken.None)
                ?? throw new InternationalShipmentValidationException("Shipment not found.");
            // Fix round 2, finding ii: claim the row atomically right before calling the carrier, so
            // two concurrent requests resuming the same stale booking can never both proceed.
            await ClaimStalePendingBookingAsync(live);
            // Fix round 2, finding v: resume with the ORIGINAL booking's own stored service level,
            // not whatever the new (retry) request happens to send.
            return await CompleteBookingAsync(live, account, provider, scan.Awb, live.ServiceLevel, resumeManualAwb,
                resumeShipmentDetail.WeightKg, shipmentId, organizationId, actingUser, live.IdempotencyKey);
        }

        var (serviceLevel, manualAwb) = ValidateBookingInputs(request, account, provider);
        if (manualAwb is not null)
        {
            await EnsureCarrierAwbFreeAsync(account.Id, manualAwb, CancellationToken.None);
        }

        var shipmentDetail = await _shipmentTransitionService.GetResponseAsync(shipmentId, CancellationToken.None)
            ?? throw new InternationalShipmentValidationException("Shipment not found.");

        var now = DateTimeOffset.UtcNow;
        var booking = new CarrierBooking
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            InternationalLegId = leg.Id,
            CarrierAccountId = account.Id,
            Direction = CarrierBookingDirection.Outbound,
            ServiceLevel = serviceLevel,
            Status = CarrierBookingStatus.Pending,
            IdempotencyKey = key,
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.Set<CarrierBooking>().Add(booking);
        try
        {
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException)
        {
            // Lost a race on the one-live-booking-per-leg or idempotency-key unique index.
            throw new InternationalShipmentValidationException("A booking for this shipment is already in progress or booked.");
        }

        return await CompleteBookingAsync(booking, account, provider, scan.Awb, serviceLevel, manualAwb, shipmentDetail.WeightKg,
            shipmentId, organizationId, actingUser, key);
    }

    public async Task<CarrierBookingResponse?> CancelAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganization();
        var target = await LoadVisibleAsync(shipmentId, actingUser, "international.book", cancellationToken);
        if (target is null)
        {
            return null;
        }
        var (leg, scan) = target.Value;
        var booking = await _dbContext.Set<CarrierBooking>().FirstOrDefaultAsync(b => b.InternationalLegId == leg.Id && b.Status == CarrierBookingStatus.Booked, cancellationToken)
            ?? throw new InternationalShipmentValidationException("There is no booked carrier booking to cancel.");
        if (booking.Direction != CarrierBookingDirection.Outbound || scan.CurrentStatus != ShipmentStatus.CustomsPrepared)
        {
            throw new InternationalShipmentValidationException("A carrier booking can only be cancelled before the parcel is handed to the carrier.");
        }
        var account = await _dbContext.Set<CarrierAccount>().FirstAsync(a => a.Id == booking.CarrierAccountId, cancellationToken);
        var provider = _registry.Find(account.CarrierCode)
            ?? throw new InternationalShipmentValidationException($"Carrier '{account.CarrierCode}' is no longer registered.");

        var credentials = _secretProtector.UnprotectCredentials(account.EncryptedCredentials);
        try
        {
            await provider.CancelAsync(booking.CarrierAwb!, credentials, cancellationToken);
        }
        catch (Exception ex) when (CarrierCallFailures.IsCarrierUnavailable(ex))
        {
            throw new CarrierUnavailableException($"Carrier '{account.CarrierCode}' is temporarily unavailable. Try again later.");
        }
        catch (Exception ex) when (CarrierCallFailures.IsCarrierFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            throw new CarrierBookingFailedException(Truncate(CarrierPayloadSanitizer.MaskSecrets(ex.Message, credentials.SecretValues), MaxFailureReasonLength));
        }

        booking.Status = CarrierBookingStatus.Cancelled;
        booking.CancelledAt = DateTimeOffset.UtcNow;
        booking.UpdatedAt = booking.CancelledAt.Value;
        try
        {
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Fix round 1, finding 4: the carrier already cancelled the booking on its side — if we
            // fail to record that locally, this is the only trace of it for reconciliation.
            _logger.LogError(ex, "Failed to persist cancellation of carrier booking {CarrierBookingId} (carrier AWB {CarrierAwb}) after carrier {CarrierCode} confirmed the cancellation.",
                booking.Id, booking.CarrierAwb, account.CarrierCode);
            throw;
        }
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "carrier_booking.cancelled", "CarrierBooking", booking.Id.ToString()), CancellationToken.None);
        return ToResponse(booking, account);
    }

    public async Task<CarrierBookingResponse?> LinkInboundAsync(Guid shipmentId, LinkInboundBookingRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganization();
        var target = await LoadVisibleAsync(shipmentId, actingUser, "international.book", cancellationToken);
        if (target is null)
        {
            return null;
        }
        var (leg, scan) = target.Value;
        if (scan.Direction != ShipmentDirection.Inbound)
        {
            throw new InternationalShipmentValidationException("Only inbound shipments can be linked to an existing carrier booking.");
        }
        if (scan.CurrentStatus != ShipmentStatus.Created)
        {
            throw new InternationalShipmentValidationException($"An inbound carrier booking can only be linked while the shipment is Created, was {scan.CurrentStatus}.");
        }
        await EnsureNoLiveBookingAsync(leg.Id, cancellationToken);
        var (account, provider) = await RequireActiveAccountAsync(request.CarrierAccountId, cancellationToken);
        var carrierAwb = NormalizeRequired(request.CarrierAwb, MaxCarrierAwbLength, "Carrier AWB");
        await EnsureCarrierAwbFreeAsync(account.Id, carrierAwb, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var booking = new CarrierBooking
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            InternationalLegId = leg.Id,
            CarrierAccountId = account.Id,
            Direction = CarrierBookingDirection.Inbound,
            CarrierAwb = carrierAwb,
            ServiceLevel = NormalizeOptional(request.ServiceLevel, MaxServiceLevelLength, "Service level"),
            Status = CarrierBookingStatus.Booked,
            IdempotencyKey = $"inbound-link:{Guid.NewGuid():N}",
            BookedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.Set<CarrierBooking>().Add(booking);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new InternationalShipmentValidationException("This shipment already has a carrier booking, or this carrier AWB is already linked.");
        }
        // Fix round 1, finding 3: past the commit, match BookAsync — a client disconnect must never
        // stop the requeue/audit side effects of a booking that has already happened.
        await RequeueUnmatchedAsync(account.Id, carrierAwb, CancellationToken.None);
        if (account.PollingEnabled && provider.Capabilities.SupportsPolling)
        {
            _scheduler.EnqueuePoll(organizationId, booking.Id);
        }

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "carrier_booking.inbound_linked", "CarrierBooking", booking.Id.ToString(),
            After: carrierAwb), CancellationToken.None);
        return ToResponse(booking, account);
    }

    public static CarrierBookingResponse ToResponse(CarrierBooking booking, CarrierAccount account) => new(
        booking.Id, account.Id, account.CarrierCode, account.DisplayName, booking.Direction.ToString(), booking.Status.ToString(),
        booking.CarrierAwb, booking.ServiceLevel, booking.LabelStorageKey is not null, booking.FailureReason,
        booking.BookedAt, booking.CancelledAt, booking.CustodyStartedAt, booking.CreatedAt);

    private async Task<CarrierBookingResponse> ToResponseAsync(CarrierBooking booking, CancellationToken cancellationToken) =>
        ToResponse(booking, await _dbContext.Set<CarrierAccount>().FirstAsync(a => a.Id == booking.CarrierAccountId, cancellationToken));

    private Guid RequireOrganization() =>
        _currentTenantProvider.OrganizationId ?? throw new InternationalShipmentValidationException("No active organization context.");

    // null = not found / not in tenant / not visible to the caller (-> 404).
    private async Task<(InternationalLeg Leg, ShipmentScanInfo Scan)?> LoadVisibleAsync(Guid shipmentId, ClaimsPrincipal actingUser, string permission, CancellationToken cancellationToken)
    {
        var leg = await _dbContext.Set<InternationalLeg>().FirstOrDefaultAsync(l => l.ShipmentId == shipmentId, cancellationToken);
        if (leg is null)
        {
            return null;
        }
        var scan = await _shipmentTransitionService.GetScanInfoAsync(shipmentId, cancellationToken);
        if (scan is null || !await InternationalShipmentAccess.IsVisibleAsync(_branchAuthorizationChecker, actingUser, permission,
                scan.OriginBranchId, scan.DestinationBranchId, scan.CurrentBranchId, cancellationToken))
        {
            return null;
        }
        return (leg, scan);
    }

    private async Task EnsureNoLiveBookingAsync(Guid legId, CancellationToken cancellationToken)
    {
        if (await _dbContext.Set<CarrierBooking>().AnyAsync(b => b.InternationalLegId == legId
                && (b.Status == CarrierBookingStatus.Pending || b.Status == CarrierBookingStatus.Booked), cancellationToken))
        {
            throw new InternationalShipmentValidationException("This shipment already has a carrier booking. Cancel it first.");
        }
    }

    private async Task EnsureCarrierAwbFreeAsync(Guid accountId, string carrierAwb, CancellationToken cancellationToken)
    {
        if (await _dbContext.Set<CarrierBooking>().AnyAsync(b => b.CarrierAccountId == accountId && b.CarrierAwb == carrierAwb
                && b.Status == CarrierBookingStatus.Booked, cancellationToken))
        {
            throw new InternationalShipmentValidationException("This carrier AWB is already linked to another shipment on this carrier account.");
        }
    }

    private async Task<(CarrierAccount Account, ICarrierProvider Provider)> RequireActiveAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _dbContext.Set<CarrierAccount>().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null || !account.IsActive)
        {
            throw new InternationalShipmentValidationException("Carrier account not found or inactive.");
        }
        var provider = _registry.Find(account.CarrierCode)
            ?? throw new InternationalShipmentValidationException($"Carrier '{account.CarrierCode}' is not registered.");
        return (account, provider);
    }

    private string? ValidateManualAwb(BookCarrierRequest request, CarrierAccount account, ICarrierProvider provider)
    {
        if (provider.Capabilities.RequiresManualAwb)
        {
            return NormalizeRequired(request.CarrierAwb, MaxCarrierAwbLength, "Carrier AWB");
        }
        if (!string.IsNullOrWhiteSpace(request.CarrierAwb))
        {
            throw new InternationalShipmentValidationException($"Carrier '{account.CarrierCode}' assigns its own AWB; do not send one.");
        }
        return null;
    }

    // Shared by the fresh-Pending path of BookAsync only — the stale-Pending-resume path validates
    // just the manual AWB (fix round 2, finding v: it reuses the stored ServiceLevel instead).
    private (string? ServiceLevel, string? ManualAwb) ValidateBookingInputs(BookCarrierRequest request, CarrierAccount account, ICarrierProvider provider) =>
        (NormalizeOptional(request.ServiceLevel, MaxServiceLevelLength, "Service level"), ValidateManualAwb(request, account, provider));

    // Fix round 2, finding ii: atomically claims a stale Pending row (compare-and-set on its
    // UpdatedAt) right before the carrier is called, so two concurrent requests resuming the same
    // stale booking can never both proceed — the loser throws instead of calling the carrier.
    private async Task ClaimStalePendingBookingAsync(CarrierBooking live)
    {
        var observedUpdatedAt = live.UpdatedAt;
        var claimedAt = DateTimeOffset.UtcNow;
        if (_dbContext.Database.IsRelational())
        {
            var affected = await _dbContext.Set<CarrierBooking>()
                .Where(b => b.Id == live.Id && b.Status == CarrierBookingStatus.Pending && b.UpdatedAt == observedUpdatedAt)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.UpdatedAt, claimedAt), CancellationToken.None);
            if (affected == 0)
            {
                throw new InternationalShipmentValidationException("This booking is still in progress.");
            }
            // Fix round 3, finding 1: accept the value the ExecuteUpdate above just committed into
            // the tracker WITHOUT reverting CurrentValues. `live` stays tracked (it is about to be
            // mutated further by CompleteBookingAsync), so it must end this method Unchanged, not
            // Modified — otherwise a later, unrelated SaveChangesAsync on this same scoped DbContext
            // (the label save, RequeueUnmatchedAsync, the audit logger) would resend this stale
            // UpdatedAt value and could silently undo a concurrent Cancel.
            live.UpdatedAt = claimedAt;
            AcceptTrackedChanges(live);
        }
        else
        {
            // EF Core's InMemory provider (unit tests) cannot run ExecuteUpdate — an equivalent
            // tracked compare-and-set: re-read fresh (untracked) to check the currently-committed
            // state, then save through the already-tracked `live` only if it still matches. A real
            // SaveChangesAsync naturally leaves the entity Unchanged afterward.
            var fresh = await _dbContext.Set<CarrierBooking>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == live.Id, CancellationToken.None);
            var claimed = fresh is not null && fresh.Status == CarrierBookingStatus.Pending && fresh.UpdatedAt == observedUpdatedAt;
            if (!claimed)
            {
                throw new InternationalShipmentValidationException("This booking is still in progress.");
            }
            live.UpdatedAt = claimedAt;
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }
    }

    // Fix round 2, finding iii / round 3, finding 1: persists `booking`'s current in-memory Booked
    // fields only if its row is still Pending in the store — a late failure (from a different,
    // losing attempt) can never overwrite a real success recorded here.
    //
    // On success, the entity is accepted into the tracker as Unchanged (see AcceptTrackedChanges).
    // On the lost-race path it is detached instead: leaving it tracked and Modified would let some
    // later, unrelated SaveChangesAsync on this same scoped DbContext (the label save,
    // RequeueUnmatchedAsync, the audit logger — anything that runs on every successful booking)
    // blindly resend these losing column values and silently revert a concurrent state change (e.g.
    // a Cancel) made through a completely different code path.
    private async Task<bool> TryMarkBookedIfPendingAsync(CarrierBooking booking)
    {
        if (_dbContext.Database.IsRelational())
        {
            var id = booking.Id;
            var status = booking.Status;
            var carrierAwb = booking.CarrierAwb;
            var serviceLevel = booking.ServiceLevel;
            var rawResponse = booking.RawResponse;
            var bookedAt = booking.BookedAt;
            var updatedAt = booking.UpdatedAt;
            var affected = await _dbContext.Set<CarrierBooking>()
                .Where(b => b.Id == id && b.Status == CarrierBookingStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Status, status)
                    .SetProperty(b => b.CarrierAwb, carrierAwb)
                    .SetProperty(b => b.ServiceLevel, serviceLevel)
                    .SetProperty(b => b.RawResponse, rawResponse)
                    .SetProperty(b => b.BookedAt, bookedAt)
                    .SetProperty(b => b.UpdatedAt, updatedAt), CancellationToken.None);
            if (affected > 0)
            {
                AcceptTrackedChanges(booking);
                return true;
            }
            DetachLosingEntity(booking);
            return false;
        }
        return await TryPersistIfStillPendingNonRelationalAsync(booking);
    }

    // Fix round 2, finding iii / round 3, finding 1: same guard for the failure path — a failure
    // must never overwrite an already-Booked (or otherwise resolved) row. See
    // TryMarkBookedIfPendingAsync's comment for why the accept-or-detach handling matters.
    private async Task<bool> TryMarkFailedIfPendingAsync(CarrierBooking booking)
    {
        if (_dbContext.Database.IsRelational())
        {
            var id = booking.Id;
            var status = booking.Status;
            var failureReason = booking.FailureReason;
            var updatedAt = booking.UpdatedAt;
            var affected = await _dbContext.Set<CarrierBooking>()
                .Where(b => b.Id == id && b.Status == CarrierBookingStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Status, status)
                    .SetProperty(b => b.FailureReason, failureReason)
                    .SetProperty(b => b.UpdatedAt, updatedAt), CancellationToken.None);
            if (affected > 0)
            {
                AcceptTrackedChanges(booking);
                return true;
            }
            DetachLosingEntity(booking);
            return false;
        }
        return await TryPersistIfStillPendingNonRelationalAsync(booking);
    }

    // Fix round 3, finding 1: accepts an entity's CURRENT (already-mutated) in-memory values as the
    // change tracker's new baseline, after those exact values were just committed via a raw
    // ExecuteUpdate that bypassed the tracker entirely. Setting OriginalValues to match CurrentValues
    // BEFORE transitioning to Unchanged is required: assigning `.State = EntityState.Unchanged`
    // directly on a Modified entity does not accept its current values — it REJECTS them and reverts
    // CurrentValues back to the stale pre-mutation snapshot (the exact regression fix round 2
    // introduced and then had to fix). The entity ends Unchanged either way, but only this order
    // preserves the values that were actually written to the database.
    private void AcceptTrackedChanges(CarrierBooking entity)
    {
        var entry = _dbContext.Entry(entity);
        entry.OriginalValues.SetValues(entry.CurrentValues);
        entry.State = EntityState.Unchanged;
    }

    // Fix round 3, finding 1: detaches an entity that lost a conditional (Pending-guarded) write —
    // it never became Unchanged, so it is still tracked as Modified with the losing values. Without
    // detaching it, a later, unrelated SaveChangesAsync on this same scoped DbContext would flush
    // those stale values and could silently overwrite whatever the winning attempt (or an unrelated
    // concurrent operation, like a Cancel) legitimately wrote.
    private void DetachLosingEntity(CarrierBooking entity) => _dbContext.Entry(entity).State = EntityState.Detached;

    // EF Core's InMemory provider (unit tests) cannot run ExecuteUpdate — an equivalent tracked
    // compare-and-set: re-read fresh (untracked) to check the currently-committed status, then save
    // through the already-tracked, already-mutated `booking` only if it is still Pending.
    private async Task<bool> TryPersistIfStillPendingNonRelationalAsync(CarrierBooking booking)
    {
        var fresh = await _dbContext.Set<CarrierBooking>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == booking.Id, CancellationToken.None);
        if (fresh is null || fresh.Status != CarrierBookingStatus.Pending)
        {
            return false;
        }
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    // Calls the carrier, then persists the outcome onto `booking` (a freshly-created Pending row,
    // or a resumed stale one) and returns its response. Used by both of BookAsync's paths.
    private async Task<CarrierBookingResponse> CompleteBookingAsync(CarrierBooking booking, CarrierAccount account, ICarrierProvider provider,
        string platformAwb, string? serviceLevel, string? manualAwb, decimal weightKg, Guid shipmentId, Guid organizationId,
        ClaimsPrincipal actingUser, string carrierIdempotencyKey)
    {
        var credentials = _secretProtector.UnprotectCredentials(account.EncryptedCredentials);
        CarrierBookingResult result;
        try
        {
            // Fix round 1, finding 1: always CancellationToken.None. Once the Pending row is
            // committed, the carrier call must run to completion regardless of the caller
            // disconnecting — the resilience pipeline enforces its own timeout.
            result = await provider.BookAsync(new CarrierBookingRequest(shipmentId, platformAwb, serviceLevel, manualAwb, weightKg),
                credentials, carrierIdempotencyKey, CancellationToken.None);
        }
        catch (Exception ex) when (CarrierCallFailures.IsCarrierUnavailable(ex))
        {
            await MarkFailedAsync(booking, "Carrier is temporarily unavailable.", actingUser);
            throw new CarrierUnavailableException($"Carrier '{account.CarrierCode}' is temporarily unavailable. Try again later.");
        }
        catch (Exception ex) when (CarrierCallFailures.IsCarrierFailure(ex))
        {
            var reason = CarrierPayloadSanitizer.MaskSecrets(ex.Message, credentials.SecretValues);
            await MarkFailedAsync(booking, reason, actingUser);
            throw new CarrierBookingFailedException(Truncate(reason, MaxFailureReasonLength));
        }

        // Fix round 1, finding 2 / round 2, finding iii: persist the carrier's own AWB first,
        // conditional on the row still being Pending, so a later failure (label upload, a transient
        // DB error, a losing concurrent attempt) can never lose track of — or overwrite — a booking
        // that has already happened at the carrier.
        booking.Status = CarrierBookingStatus.Booked;
        booking.CarrierAwb = Truncate(result.CarrierAwb.Trim(), MaxCarrierAwbLength);
        booking.ServiceLevel = NormalizeOptionalOrNull(result.ServiceLevel, MaxServiceLevelLength) ?? serviceLevel;
        booking.RawResponse = CarrierPayloadSanitizer.ToStoredJson(result.RawResponse, credentials.SecretValues);
        booking.BookedAt = DateTimeOffset.UtcNow;
        booking.UpdatedAt = booking.BookedAt.Value;

        bool persisted;
        try
        {
            persisted = await TryMarkBookedIfPendingAsync(booking);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist carrier booking {CarrierBookingId} after carrier {CarrierCode} assigned AWB {CarrierAwb} — reconcile manually.",
                booking.Id, account.CarrierCode, booking.CarrierAwb);
            throw;
        }
        if (!persisted)
        {
            // Fix round 2, finding iii: the row moved on before we could persist the carrier's own
            // result — this can never be silently dropped, and it must never be reported as this
            // request's own success either, so the actual current state is returned instead.
            _logger.LogError(
                "Carrier {CarrierCode} assigned AWB {CarrierAwb} to booking {CarrierBookingId}, but its row was no longer Pending when persisting — reconcile manually.",
                account.CarrierCode, booking.CarrierAwb, booking.Id);
            var current = await _dbContext.Set<CarrierBooking>().AsNoTracking().FirstAsync(b => b.Id == booking.Id, CancellationToken.None);
            return ToResponse(current, account);
        }

        if (result.Label is not null)
        {
            // Fix round 3, finding 2: a failed (or unpersisted) label save must never leave label
            // changes dirty on `booking` — `booking` is Unchanged at this point (TryMarkBookedIfPendingAsync
            // just accepted it), and it must still be Unchanged when this block ends, whatever
            // happens, so RequeueUnmatchedAsync/the audit logger below (or anything later on this
            // scoped DbContext) can never resurface a label failure as an unrelated exception, and
            // can never blindly resend a stale label value.
            if (_dbContext.Database.IsRelational())
            {
                try
                {
                    using var content = new MemoryStream(result.Label.Content);
                    var storageKey = await _objectStorage.UploadAsync($"carrier-labels/{organizationId}/{booking.Id}", content, result.Label.ContentType, CancellationToken.None);
                    var labelUpdatedAt = DateTimeOffset.UtcNow;
                    var affected = await _dbContext.Set<CarrierBooking>()
                        .Where(b => b.Id == booking.Id && b.Status == CarrierBookingStatus.Booked)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(b => b.LabelStorageKey, storageKey)
                            .SetProperty(b => b.LabelContentType, result.Label.ContentType)
                            .SetProperty(b => b.UpdatedAt, labelUpdatedAt), CancellationToken.None);
                    if (affected > 0)
                    {
                        booking.LabelStorageKey = storageKey;
                        booking.LabelContentType = result.Label.ContentType;
                        booking.UpdatedAt = labelUpdatedAt;
                        AcceptTrackedChanges(booking);
                    }
                    else
                    {
                        _logger.LogError(
                            "Could not persist the carrier label for booking {CarrierBookingId} (carrier AWB {CarrierAwb}) — its row was no longer Booked. The booking stands without a stored label.",
                            booking.Id, booking.CarrierAwb);
                    }
                }
                catch (Exception ex)
                {
                    // Leave `booking` untouched: it is still the Unchanged, correctly-Booked entity
                    // from TryMarkBookedIfPendingAsync — nothing about this failure may become dirty.
                    _logger.LogError(ex, "Failed to upload the carrier label for booking {CarrierBookingId} (carrier AWB {CarrierAwb}) — the booking stands without a stored label.",
                        booking.Id, booking.CarrierAwb);
                }
            }
            else
            {
                // EF Core's InMemory provider (unit tests) cannot run ExecuteUpdate — keep the
                // tracked save, but on failure revert the label properties and accept them, so
                // nothing stays dirty for a later, unrelated SaveChangesAsync to resurface.
                try
                {
                    using var content = new MemoryStream(result.Label.Content);
                    booking.LabelStorageKey = await _objectStorage.UploadAsync($"carrier-labels/{organizationId}/{booking.Id}", content, result.Label.ContentType, CancellationToken.None);
                    booking.LabelContentType = result.Label.ContentType;
                    await _dbContext.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload the carrier label for booking {CarrierBookingId} (carrier AWB {CarrierAwb}) — the booking stands without a stored label.",
                        booking.Id, booking.CarrierAwb);
                    booking.LabelStorageKey = null;
                    booking.LabelContentType = null;
                    AcceptTrackedChanges(booking);
                }
            }
        }

        await RequeueUnmatchedAsync(account.Id, booking.CarrierAwb, CancellationToken.None);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "carrier_booking.booked", "CarrierBooking", booking.Id.ToString(),
            After: booking.CarrierAwb), CancellationToken.None);
        return ToResponse(booking, account);
    }

    // Events that arrived before this booking existed (e.g. an early webhook) get another chance.
    private async Task RequeueUnmatchedAsync(Guid accountId, string carrierAwb, CancellationToken cancellationToken)
    {
        var unmatched = await _dbContext.Set<CarrierTrackingEvent>()
            .Where(e => e.CarrierAccountId == accountId && e.CarrierAwb == carrierAwb && e.ProcessingStatus == CarrierEventProcessingStatus.Unmatched)
            .ToListAsync(cancellationToken);
        if (unmatched.Count == 0)
        {
            return;
        }
        foreach (var evt in unmatched)
        {
            evt.ProcessingStatus = CarrierEventProcessingStatus.Pending;
            evt.ProcessingNote = null;
            evt.ProcessedAt = null;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        _scheduler.EnqueueProcessing();
    }

    private async Task MarkFailedAsync(CarrierBooking booking, string reason, ClaimsPrincipal actingUser)
    {
        booking.Status = CarrierBookingStatus.Failed;
        booking.FailureReason = Truncate(reason, MaxFailureReasonLength);
        booking.UpdatedAt = DateTimeOffset.UtcNow;

        // Fix round 2, finding iii: conditional on the row still being Pending — a late failure
        // must never overwrite an already-Booked (or otherwise resolved) row.
        if (!await TryMarkFailedIfPendingAsync(booking))
        {
            _logger.LogWarning(
                "Carrier booking {CarrierBookingId} failed, but its row was no longer Pending — the failure was not recorded because a concurrent attempt already resolved it.",
                booking.Id);
            return;
        }
        _logger.LogWarning("Carrier booking {CarrierBookingId} failed.", booking.Id);
        await _auditLogger.LogAsync(new AuditEntry(booking.OrganizationId, actingUser.GetUserId(), "carrier_booking.failed", "CarrierBooking", booking.Id.ToString(),
            After: booking.FailureReason), CancellationToken.None);
    }

    private static string NormalizeRequired(string? value, int maxLength, string field)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
        {
            throw new InternationalShipmentValidationException($"{field} is required and must be at most {maxLength} characters.");
        }
        return trimmed;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }
        if (trimmed.Length > maxLength)
        {
            throw new InternationalShipmentValidationException($"{field} must be at most {maxLength} characters.");
        }
        return trimmed;
    }

    private static string? NormalizeOptionalOrNull(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), maxLength);

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
