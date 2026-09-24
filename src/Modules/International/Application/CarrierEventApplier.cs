using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Application;

// Applies one claimed (Processing) carrier tracking event. Must run inside a tenant scope for the
// event's organization (CarrierEventProcessorJob creates it) — the gateway's Shipment lookup and
// every query here are tenant-filtered.
public class CarrierEventApplier
{
    public const int MaxAttempts = 5;
    private const int MaxNoteLength = 1000;

    private readonly DbContext _dbContext;
    private readonly ICarrierProviderRegistry _registry;
    private readonly IShipmentInternationalGateway _gateway;

    public CarrierEventApplier(DbContext dbContext, ICarrierProviderRegistry registry, IShipmentInternationalGateway gateway)
    {
        _dbContext = dbContext;
        _registry = registry;
        _gateway = gateway;
    }

    private sealed record Outcome(CarrierEventProcessingStatus Status, string? Note, Guid? BookingId = null, string? MappedStatus = null);

    public async Task ApplyAsync(Guid eventId, CancellationToken cancellationToken)
    {
        // A previous call in this scope may have failed its final save and left a dirty row
        // tracked (e.g. a partially-applied Shipment from a thrown gateway call) — never let that
        // poison this event's own save.
        _dbContext.ChangeTracker.Clear();
        var evt = await _dbContext.Set<CarrierTrackingEvent>().FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        if (evt is null || evt.ProcessingStatus != CarrierEventProcessingStatus.Processing)
        {
            return;
        }

        Outcome outcome;
        try
        {
            outcome = await EvaluateAndApplyAsync(evt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unexpected (not a state-machine rejection) — retried by a later run up to MaxAttempts.
            outcome = new Outcome(CarrierEventProcessingStatus.Pending, $"{ex.GetType().Name}: {ex.Message}");
        }

        // A failed gateway save leaves its modified Shipment tracked; never let it be re-saved.
        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.Set<CarrierTrackingEvent>().FirstAsync(e => e.Id == eventId, cancellationToken);
        stored.ProcessingAttempts += 1;
        stored.CarrierBookingId = outcome.BookingId ?? stored.CarrierBookingId;
        stored.MappedShipmentStatus = outcome.MappedStatus ?? stored.MappedShipmentStatus;
        stored.ClaimedAt = null;

        var status = outcome.Status;
        var note = outcome.Note;
        if (status == CarrierEventProcessingStatus.Pending && stored.ProcessingAttempts >= MaxAttempts)
        {
            status = CarrierEventProcessingStatus.Rejected;
            note = $"Gave up after {stored.ProcessingAttempts} attempts. Last error: {note}";
        }
        stored.ProcessingStatus = status;
        stored.ProcessingNote = note is null ? null : note.Length <= MaxNoteLength ? note : note[..MaxNoteLength];
        stored.ProcessedAt = status == CarrierEventProcessingStatus.Pending ? null : DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Outcome> EvaluateAndApplyAsync(CarrierTrackingEvent evt, CancellationToken cancellationToken)
    {
        var account = await _dbContext.Set<CarrierAccount>().FirstOrDefaultAsync(a => a.Id == evt.CarrierAccountId, cancellationToken);
        if (account is null)
        {
            return new Outcome(CarrierEventProcessingStatus.Rejected, "Carrier account not found.");
        }
        var provider = _registry.Find(account.CarrierCode);
        if (provider is null)
        {
            return new Outcome(CarrierEventProcessingStatus.Rejected, $"No carrier provider is registered for '{account.CarrierCode}'.");
        }

        // Matched by account AND carrier AWB, only against a live Booked booking — never by AWB alone.
        var booking = await _dbContext.Set<CarrierBooking>().FirstOrDefaultAsync(b =>
            b.CarrierAccountId == evt.CarrierAccountId && b.CarrierAwb == evt.CarrierAwb && b.Status == CarrierBookingStatus.Booked, cancellationToken);
        if (booking is null)
        {
            return new Outcome(CarrierEventProcessingStatus.Unmatched, "No booked carrier booking matches this carrier AWB.");
        }
        var leg = await _dbContext.Set<InternationalLeg>().FirstOrDefaultAsync(l => l.Id == booking.InternationalLegId, cancellationToken);
        if (leg is null)
        {
            return new Outcome(CarrierEventProcessingStatus.Rejected, "International leg not found.", booking.Id);
        }

        var direction = booking.Direction == CarrierBookingDirection.Outbound ? ShipmentDirection.Outbound : ShipmentDirection.Inbound;
        var target = provider.MapStatus(evt.CarrierStatusCode, direction);
        if (target is null)
        {
            return new Outcome(CarrierEventProcessingStatus.Ignored, $"Informational carrier status '{evt.CarrierStatusCode}'.", booking.Id);
        }
        var mapped = target.Value.ToString();

        try
        {
            var result = await _gateway.ApplyCarrierStatusAsync(leg.ShipmentId, target.Value, evt.OccurredAt,
                new CarrierEventReference(account.Id, evt.CarrierAwb, evt.CarrierEventId, evt.CarrierStatusCode, evt.Location), cancellationToken);
            return result switch
            {
                CarrierStatusApplyResult.Applied => new Outcome(CarrierEventProcessingStatus.Applied, null, booking.Id, mapped),
                CarrierStatusApplyResult.AlreadyAtStatus => new Outcome(CarrierEventProcessingStatus.Ignored, $"Shipment was already {mapped}.", booking.Id, mapped),
                _ => new Outcome(CarrierEventProcessingStatus.Rejected, "Shipment not found.", booking.Id, mapped)
            };
        }
        catch (ShipmentConcurrencyException ex)
        {
            // A concurrent update raced this save (xmin mismatch) — not a real business-rule
            // rejection, just a lost race. Retryable: goes back to Pending and counts toward
            // MaxAttempts like any other transient failure, rather than being rejected forever.
            // Must be caught before ShipmentValidationException, since it derives from
            // ShipmentInvalidTransitionException which derives from it.
            return new Outcome(CarrierEventProcessingStatus.Pending, ex.Message, booking.Id, mapped);
        }
        catch (ShipmentValidationException ex)
        {
            // Includes ShipmentInvalidTransitionException: a stale/out-of-order/impossible event.
            return new Outcome(CarrierEventProcessingStatus.Rejected, ex.Message, booking.Id, mapped);
        }
    }
}
