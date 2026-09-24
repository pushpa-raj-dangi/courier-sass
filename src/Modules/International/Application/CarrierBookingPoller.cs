using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courier.International.Application;

public record CarrierPollResult(bool Polled, int Inserted);

// Polls one booking. Runs inside a tenant scope (CarrierTrackingPollJob).
public class CarrierBookingPoller
{
    private readonly DbContext _dbContext;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly ICarrierProviderRegistry _registry;
    private readonly ICarrierSecretProtector _secretProtector;
    private readonly CarrierTrackingEventWriter _writer;
    private readonly ILogger<CarrierBookingPoller> _logger;

    public CarrierBookingPoller(DbContext dbContext, IShipmentTransitionService shipmentTransitionService, ICarrierProviderRegistry registry,
        ICarrierSecretProtector secretProtector, CarrierTrackingEventWriter writer, ILogger<CarrierBookingPoller> logger)
    {
        _dbContext = dbContext;
        _shipmentTransitionService = shipmentTransitionService;
        _registry = registry;
        _secretProtector = secretProtector;
        _writer = writer;
        _logger = logger;
    }

    public async Task<CarrierPollResult> PollAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await _dbContext.Set<CarrierBooking>().FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
        if (booking is null || booking.Status != CarrierBookingStatus.Booked || booking.CarrierAwb is null)
        {
            return new CarrierPollResult(false, 0);
        }
        var account = await _dbContext.Set<CarrierAccount>().FirstOrDefaultAsync(a => a.Id == booking.CarrierAccountId, cancellationToken);
        var provider = account is null ? null : _registry.Find(account.CarrierCode);
        if (account is null || !account.IsActive || !account.PollingEnabled || provider is null || !provider.Capabilities.SupportsPolling)
        {
            return new CarrierPollResult(false, 0);
        }
        var leg = await _dbContext.Set<InternationalLeg>().FirstOrDefaultAsync(l => l.Id == booking.InternationalLegId, cancellationToken);
        var scan = leg is null ? null : await _shipmentTransitionService.GetScanInfoAsync(leg.ShipmentId, cancellationToken);
        if (scan is null || !CarrierTrackingWindow.IsTracked(scan.Direction, scan.CurrentStatus))
        {
            return new CarrierPollResult(false, 0);
        }

        var credentials = _secretProtector.UnprotectCredentials(account.EncryptedCredentials);
        IReadOnlyList<CarrierTrackingUpdate> updates;
        try
        {
            updates = await provider.GetTrackingAsync(booking.CarrierAwb, credentials, cancellationToken);
        }
        catch (Exception ex) when ((CarrierCallFailures.IsCarrierUnavailable(ex) || CarrierCallFailures.IsCarrierFailure(ex)) && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Polling carrier booking {CarrierBookingId} failed: {Reason}", booking.Id, ex.GetType().Name);
            return new CarrierPollResult(true, 0);
        }
        var inserted = await _writer.InsertAsync(account, updates, CarrierEventSource.Poll, null, credentials.SecretValues, cancellationToken);
        return new CarrierPollResult(true, inserted);
    }
}
