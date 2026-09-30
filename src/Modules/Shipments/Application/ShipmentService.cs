using System.Security.Claims;
using System.Text.RegularExpressions;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Events.Application;
using Courier.Geography.Application;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public partial class ShipmentService : IShipmentTransitionService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IGeographyLookupService _geographyLookupService;
    private readonly IMerchantLookupService _merchantLookupService;
    private readonly PricingService _pricingService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly IOrganizationLookupService _organizationLookupService;
    private readonly ICodCollectionCreationService _codCollectionCreationService;
    private readonly IOutboxWriter _outboxWriter;

    public ShipmentService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IBranchLookupService branchLookupService,
        IGeographyLookupService geographyLookupService,
        IMerchantLookupService merchantLookupService,
        PricingService pricingService,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IOrganizationLookupService organizationLookupService,
        ICodCollectionCreationService codCollectionCreationService,
        IOutboxWriter outboxWriter)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchLookupService = branchLookupService;
        _geographyLookupService = geographyLookupService;
        _merchantLookupService = merchantLookupService;
        _pricingService = pricingService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _organizationLookupService = organizationLookupService;
        _codCollectionCreationService = codCollectionCreationService;
        _outboxWriter = outboxWriter;
    }

    public async Task<ShipmentResponse> CreateShipmentAsync(CreateShipmentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        // Merchant-JWT callers get their own id forced regardless of what the body says — never
        // trust a client-supplied MerchantId on a merchant token (CLAUDE.md §33).
        Guid merchantId;
        var callerMerchantId = actingUser.GetMerchantId();
        if (callerMerchantId is Guid selfMerchantId)
        {
            merchantId = selfMerchantId;
        }
        else
        {
            var actingUserId = actingUser.GetUserId()
                ?? throw new ShipmentValidationException("Acting user could not be resolved.");
            if (request.MerchantId is not Guid staffSuppliedMerchantId)
            {
                throw new ShipmentValidationException("MerchantId is required.");
            }
            if (!await _merchantLookupService.ExistsAsync(staffSuppliedMerchantId, cancellationToken))
            {
                throw new ShipmentValidationException("Merchant was not found in this organization.");
            }
            merchantId = staffSuppliedMerchantId;

            var accessibleForCreate = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "shipment.create", cancellationToken);
            if (accessibleForCreate is not null && !accessibleForCreate.Contains(request.OriginBranchId))
            {
                throw new ShipmentValidationException("Origin branch is outside your branch access.");
            }
        }

        // Origin/destination branch existence is validated inside PricingService.QuoteAsync below
        // (via the same IBranchLookupService.GetZoneInfoAsync call) — no separate pre-check here,
        // that would just be a redundant duplicate lookup with an equivalent error message.

        if (!await _geographyLookupService.IsValidHierarchyAsync(request.RecipientProvinceId, request.RecipientDistrictId, request.RecipientLocalLevelId, request.RecipientWardNumber, cancellationToken))
        {
            throw new ShipmentValidationException("Recipient province/district/local level/ward do not form a valid geography chain.");
        }
        if (!await _geographyLookupService.IsValidHierarchyAsync(request.SenderProvinceId, request.SenderDistrictId, request.SenderLocalLevelId, request.SenderWardNumber, cancellationToken))
        {
            throw new ShipmentValidationException("Sender province/district/local level/ward do not form a valid geography chain.");
        }

        if (request.Items.Count == 0)
        {
            throw new ShipmentValidationException("A shipment must have at least one item.");
        }

        var declaredValue = request.Items.Sum(i => i.Quantity * i.UnitValue);

        PriceQuoteResponse quote;
        try
        {
            quote = await _pricingService.QuoteAsync(
                new PriceQuoteRequest(request.OriginBranchId, request.DestinationBranchId, request.WeightKg, request.ServiceTypeId, request.CodAmount, declaredValue, request.WantsInsurance, merchantId),
                cancellationToken);
        }
        catch (PricingValidationException ex)
        {
            throw new ShipmentValidationException(ex.Message);
        }

        var insuranceCharge = quote.AppliedCharges.Where(c => c.Type == AdditionalChargeType.Insurance).Sum(c => c.Amount);
        // COD-type charges have no dedicated Shipment column in this phase's domain model, so they
        // fold into OtherCharges alongside Fuel/Other — otherwise a COD-handling charge would vanish
        // from every per-component field even though it's still counted in quote.Total/TotalCharge,
        // breaking DeliveryCharge + InsuranceCharge + OtherCharges == TotalCharge reconciliation
        // (CLAUDE.md §23: "all calculations must be traceable").
        var otherCharges = quote.AppliedCharges.Where(c => c.Type is AdditionalChargeType.Fuel or AdditionalChargeType.Other or AdditionalChargeType.COD).Sum(c => c.Amount);
        var deliveryCharge = quote.BaseRate;

        // Insert-and-catch-DbUpdateException-retry on the unique (OrganizationId, Awb) index —
        // same race-handling shape as Phase 5's RateCard versioning fix (commit a83c320). A bare
        // pre-check (SELECT then INSERT) is a TOCTOU race under concurrent requests for the same
        // org on the same day; only a caught constraint violation on the actual insert is safe.
        // maxAwbAttempts=3 measured as too low under real concurrent load (a 16-way-concurrent
        // burst of same-org creates exhausted it and surfaced as raw 500s); 8 attempts plus a
        // jittered backoff between them — so racing losers don't immediately recompute the same
        // "current max" against each other again — verified safe under the same burst size below.
        // Allocation is also serialized per organization by a Postgres advisory lock
        // (BeginAwbAllocationAsync): domestic create has no ambient transaction, so it gets a short
        // one owning the lock just for the AWB read + Shipment insert, committed right after. The
        // retry loop stays as defense in depth.
        await using var awbTransaction = await BeginAwbAllocationAsync(organizationId, cancellationToken);
        const int maxAwbAttempts = 8;
        Shipment shipment = null!;
        for (var attempt = 1; attempt <= maxAwbAttempts; attempt++)
        {
            var awb = await GenerateAwbAsync(organizationId, cancellationToken);

            shipment = new Shipment
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                OrganizationId = organizationId,
                Awb = awb,
                MerchantId = merchantId,
                OriginBranchId = request.OriginBranchId,
                DestinationBranchId = request.DestinationBranchId,
                CurrentBranchId = request.OriginBranchId,
                CurrentStatus = ShipmentStatus.Created,
                ServiceTypeId = request.ServiceTypeId,
                WeightKg = request.WeightKg,
                LengthCm = request.LengthCm,
                WidthCm = request.WidthCm,
                HeightCm = request.HeightCm,
                DeclaredValue = declaredValue,
                CodAmount = request.CodAmount,
                WantsInsurance = request.WantsInsurance,
                DeliveryCharge = deliveryCharge,
                InsuranceCharge = insuranceCharge,
                OtherCharges = otherCharges,
                TotalCharge = quote.Total,
                RecipientName = request.RecipientName,
                RecipientPhone = request.RecipientPhone,
                RecipientProvinceId = request.RecipientProvinceId,
                RecipientDistrictId = request.RecipientDistrictId,
                RecipientLocalLevelId = request.RecipientLocalLevelId,
                RecipientWardNumber = request.RecipientWardNumber,
                RecipientAddressLine = request.RecipientAddressLine,
                SenderName = request.SenderName,
                SenderPhone = request.SenderPhone,
                SenderProvinceId = request.SenderProvinceId,
                SenderDistrictId = request.SenderDistrictId,
                SenderLocalLevelId = request.SenderLocalLevelId,
                SenderWardNumber = request.SenderWardNumber,
                SenderAddressLine = request.SenderAddressLine
            };
            _dbContext.Set<Shipment>().Add(shipment);

            foreach (var item in request.Items)
            {
                _dbContext.Set<ShipmentItem>().Add(new ShipmentItem
                {
                    Id = Guid.NewGuid(),
                    ShipmentId = shipment.Id,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitValue = item.UnitValue
                });
            }

            _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                EventType = ShipmentEventType.Created,
                PreviousStatus = null,
                NewStatus = ShipmentStatus.Created,
                BranchId = request.OriginBranchId,
                UserId = actingUser.GetUserId(),
                Timestamp = DateTimeOffset.UtcNow
            });

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException) when (attempt < maxAwbAttempts)
            {
                // Another concurrent request committed the same (OrganizationId, Awb) pair first.
                // Detach this attempt's tracked entities so the next loop iteration starts clean —
                // the retry's GenerateAwbAsync call will see the winner's committed row and compute
                // a fresh, non-colliding sequence.
                foreach (var entry in _dbContext.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                {
                    entry.State = EntityState.Detached;
                }

                // Jittered backoff, growing with attempt number: without it, every losing racer
                // recomputes "current max" again in lockstep and can keep colliding with each
                // other, not just with the original winner.
                await Task.Delay(Random.Shared.Next(10, 40) * attempt, cancellationToken);
            }
        }
        if (awbTransaction is not null)
        {
            // Releases the AWB allocation lock as soon as the Shipment row is committed.
            await awbTransaction.CommitAsync(cancellationToken);
        }

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "shipment.created", "Shipment", shipment.Id.ToString()), cancellationToken);

        if (shipment.CodAmount > 0)
        {
            await _codCollectionCreationService.CreateExpectedAsync(
                shipment.Id, organizationId, merchantId, request.DestinationBranchId, shipment.CodAmount, shipment.TotalCharge, actingUser, cancellationToken);
        }

        await _outboxWriter.WriteAsync(organizationId, "shipment.created", new { ShipmentId = shipment.Id, MerchantId = merchantId, Awb = shipment.Awb }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await LoadResponseAsync(shipment.Id, cancellationToken);
    }

    // Advisory-lock namespace for per-organization AWB allocation. Uses Postgres's two-int
    // pg_advisory_xact_lock(classid, objid) form: classid = this constant ("AWB1" in ASCII), objid =
    // a stable 32-bit key folded from the OrganizationId. The two-int key space is disjoint from the
    // single-bigint one, and the constant namespaces it from any other two-int advisory-lock user.
    // A 32-bit fold can make two orgs share a key; that only serializes them against each other
    // (never a correctness issue).
    private const int AwbAllocationLockNamespace = 0x41574231;

    // Serializes AWB allocation per organization (GenerateAwbAsync's max+1 read through the
    // Shipment insert). Without it, a racer can't see a winner's AWB until the winner's transaction
    // commits — under International's create transaction that is only after the leg insert — so
    // racers recompute the same number, collide on IX_Shipments_OrganizationId_Awb and exhaust the
    // retry budget. The xact lock is held until the enclosing transaction ends, so the next
    // allocator for the org always reads the previous one's committed AWB.
    // - Ambient transaction (International create): the lock joins it; returns null (the caller of
    //   that transaction commits, which releases the lock).
    // - No ambient transaction (domestic create): opens and returns a short transaction owned by
    //   the caller, which must commit it right after the Shipment insert's SaveChangesAsync.
    // InMemory provider (unit tests): no transactions or advisory locks; returns null, no lock.
    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAwbAllocationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return null;
        }

        var ownedTransaction = _dbContext.Database.CurrentTransaction is null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var bytes = organizationId.ToByteArray();
        var organizationKey = BitConverter.ToInt32(bytes, 0) ^ BitConverter.ToInt32(bytes, 4)
            ^ BitConverter.ToInt32(bytes, 8) ^ BitConverter.ToInt32(bytes, 12);
        try
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AwbAllocationLockNamespace}, {organizationKey})", cancellationToken);
        }
        catch
        {
            if (ownedTransaction is not null)
            {
                await ownedTransaction.DisposeAsync();
            }
            throw;
        }

        return ownedTransaction;
    }

    // Format: {AwbPrefix}{yyyyMMdd}{6-digit sequence}, e.g. ACME20260820000001. The prefix is the
    // organization's stored AwbPrefix (Identity.Domain.AwbPrefix) — unique across organizations,
    // so AWBs never collide between tenants (public /track/{awb} has no tenant scope). The
    // org-id-derived fallback only covers an organization row that no longer exists. Sequence:
    // current max existing suffix for (OrganizationId, today's date-prefix) + 1. This method does NOT itself guarantee uniqueness under
    // concurrency — two callers racing on the same org/day can compute the same candidate. Its
    // callers make it safe: on Postgres they first take BeginAwbAllocationAsync's per-organization
    // advisory lock, which serializes allocators so each reads the previous one's committed AWB;
    // and they wrap the insert in a catch-DbUpdateException-retry loop that calls this method
    // again on each retry, as a fallback.
    private async Task<string> GenerateAwbAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var storedPrefix = await _organizationLookupService.GetAwbPrefixAsync(organizationId, cancellationToken);
        var prefix = string.IsNullOrEmpty(storedPrefix) ? Identity.Domain.AwbPrefix.Derive(null, organizationId) : storedPrefix;
        var datePart = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var awbPrefix = $"{prefix}{datePart}";

        var existingSuffixes = await _dbContext.Set<Shipment>()
            .Where(s => s.OrganizationId == organizationId && s.Awb.StartsWith(awbPrefix))
            .Select(s => s.Awb)
            .ToListAsync(cancellationToken);
        var nextSequence = existingSuffixes.Count == 0
            ? 1
            : existingSuffixes.Select(a => int.Parse(a[^6..])).Max() + 1;

        return $"{awbPrefix}{nextSequence:D6}";
    }

    private async Task<ShipmentResponse> LoadResponseAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstAsync(s => s.Id == shipmentId, cancellationToken);
        var items = await _dbContext.Set<ShipmentItem>().Where(i => i.ShipmentId == shipmentId).ToListAsync(cancellationToken);
        return ToResponse(shipment, items);
    }

    private static ShipmentResponse ToResponse(Shipment shipment, List<ShipmentItem> items) => new(
        shipment.Id, shipment.Awb, shipment.MerchantId, shipment.OriginBranchId, shipment.DestinationBranchId,
        shipment.CurrentBranchId, shipment.CurrentCustodianUserId, shipment.CurrentCustodianRiderId, shipment.CurrentStatus, shipment.ServiceTypeId,
        shipment.WeightKg, shipment.LengthCm, shipment.WidthCm, shipment.HeightCm,
        shipment.DeclaredValue, shipment.CodAmount, shipment.WantsInsurance,
        shipment.DeliveryCharge, shipment.InsuranceCharge, shipment.OtherCharges, shipment.TotalCharge,
        shipment.RecipientName, shipment.RecipientPhone, shipment.RecipientAddressLine,
        shipment.SenderName, shipment.SenderPhone, shipment.SenderAddressLine,
        items.Select(i => new ShipmentItemResponse(i.Id, i.Description, i.Quantity, i.UnitValue)).ToList(),
        shipment.CreatedAt);
}
