using System.Security.Claims;
using Courier.Audit.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Application;

// Staff-entered carrier events (e.g. from a phone call or the carrier's own portal) — the manual
// carrier's only tracking source, available for any carrier as a fallback (I4 spec §5.5).
public class ManualCarrierEventService
{
    private const int MaxLocationLength = 300;
    private const int MaxDescriptionLength = 1000;
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly ICarrierProviderRegistry _registry;
    private readonly CarrierTrackingEventWriter _writer;
    private readonly ICarrierJobScheduler _scheduler;

    public ManualCarrierEventService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger,
        IShipmentTransitionService shipmentTransitionService, IBranchAuthorizationChecker branchAuthorizationChecker,
        ICarrierProviderRegistry registry, CarrierTrackingEventWriter writer, ICarrierJobScheduler scheduler)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _shipmentTransitionService = shipmentTransitionService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _registry = registry;
        _writer = writer;
        _scheduler = scheduler;
    }

    public async Task<CarrierTrackingEventResponse?> RecordAsync(Guid shipmentId, ManualCarrierEventRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new InternationalShipmentValidationException("No active organization context.");

        var leg = await _dbContext.Set<InternationalLeg>().FirstOrDefaultAsync(l => l.ShipmentId == shipmentId, cancellationToken);
        if (leg is null)
        {
            return null;
        }
        var scan = await _shipmentTransitionService.GetScanInfoAsync(shipmentId, cancellationToken);
        if (scan is null || !await InternationalShipmentAccess.IsVisibleAsync(_branchAuthorizationChecker, actingUser, "international.tracking.manage",
                scan.OriginBranchId, scan.DestinationBranchId, scan.CurrentBranchId, cancellationToken))
        {
            return null;
        }

        var booking = await _dbContext.Set<CarrierBooking>().FirstOrDefaultAsync(b => b.InternationalLegId == leg.Id && b.Status == CarrierBookingStatus.Booked, cancellationToken)
            ?? throw new InternationalShipmentValidationException("A booked carrier booking is required before recording carrier events.");
        var account = await _dbContext.Set<CarrierAccount>().FirstAsync(a => a.Id == booking.CarrierAccountId, cancellationToken);
        var provider = _registry.Find(account.CarrierCode)
            ?? throw new InternationalShipmentValidationException($"Carrier '{account.CarrierCode}' is not registered.");

        var statusCode = request.StatusCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (statusCode.Length == 0 || !provider.IsKnownStatusCode(statusCode))
        {
            throw new InternationalShipmentValidationException($"Unknown status code '{request.StatusCode}' for carrier '{account.CarrierCode}'.");
        }
        if (request.OccurredAt > DateTimeOffset.UtcNow + AllowedClockSkew)
        {
            throw new InternationalShipmentValidationException("OccurredAt cannot be in the future.");
        }
        // Outbound: the carrier cannot have scanned the parcel before we handed it over. Inbound has
        // no such floor — the foreign carrier's scans genuinely precede our booking/shipment rows.
        if (booking.Direction == CarrierBookingDirection.Outbound && booking.CustodyStartedAt is { } handedOverAt
            && request.OccurredAt < handedOverAt - AllowedClockSkew)
        {
            throw new InternationalShipmentValidationException("OccurredAt cannot be before the parcel was handed to the carrier.");
        }
        var location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (location?.Length > MaxLocationLength || description?.Length > MaxDescriptionLength)
        {
            throw new InternationalShipmentValidationException($"Location must be at most {MaxLocationLength} and description at most {MaxDescriptionLength} characters.");
        }

        var eventId = $"manual-{Guid.NewGuid():N}";
        await _writer.InsertAsync(account,
            new[] { new CarrierTrackingUpdate(booking.CarrierAwb!, eventId, statusCode, description, location, request.OccurredAt, null) },
            CarrierEventSource.Manual, actingUser.GetUserId(), Array.Empty<string>(), cancellationToken);
        var stored = await _dbContext.Set<CarrierTrackingEvent>().FirstAsync(e => e.CarrierAccountId == account.Id && e.CarrierEventId == eventId, cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "carrier_event.manual_recorded", "CarrierTrackingEvent", stored.Id.ToString(),
            After: statusCode), cancellationToken);
        _scheduler.EnqueueProcessing();
        return CarrierTrackingEventResponse.From(stored);
    }
}
