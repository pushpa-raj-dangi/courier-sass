using Courier.International.Application.Carriers;
using Courier.Shipments.Domain;

namespace Courier.International.Infrastructure.Carriers;

// Staff record the carrier's AWB at booking and enter tracking events by hand (I4 spec §5.2).
// No network calls, no labels, no webhooks, no polling.
public sealed class ManualCarrierProvider : ICarrierProvider
{
    public const string Code = "MANUAL";

    // (outbound target, inbound target); null = informational for that direction.
    private static readonly Dictionary<string, (ShipmentStatus? Outbound, ShipmentStatus? Inbound)> Vocabulary =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["IN_TRANSIT"] = (ShipmentStatus.InInternationalTransit, ShipmentStatus.InInternationalTransit),
            ["IN_CUSTOMS"] = (null, ShipmentStatus.CustomsClearance),
            ["CLEARED"] = (ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsClearance),
            ["CUSTOMS_HOLD"] = (ShipmentStatus.CustomsHold, ShipmentStatus.CustomsHold),
            ["EXCEPTION"] = (ShipmentStatus.InternationalException, ShipmentStatus.InternationalException),
            // Inbound delivery is our own last mile, not a carrier event.
            ["DELIVERED"] = (ShipmentStatus.DeliveredAbroad, null),
            // Arrival at our gateway is the receive scan, not a carrier event.
            ["ARRIVED"] = (null, null)
        };

    public string CarrierCode => Code;

    public CarrierCapabilities Capabilities { get; } = new(SupportsWebhooks: false, SupportsPolling: false, RequiresManualAwb: true, SupportsLabels: false);

    public Task<CarrierBookingResult> BookAsync(CarrierBookingRequest request, CarrierCredentials credentials, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ManualCarrierAwb))
        {
            throw new CarrierBookingException("A carrier AWB is required for the manual carrier.");
        }
        return Task.FromResult(new CarrierBookingResult(request.ManualCarrierAwb.Trim(), request.ServiceLevel, null, null));
    }

    public Task CancelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<CarrierLabel?> GetLabelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken) =>
        Task.FromResult<CarrierLabel?>(null);

    public Task<IReadOnlyList<CarrierTrackingUpdate>> GetTrackingAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The manual carrier does not support polling.");

    public bool VerifyWebhook(IReadOnlyDictionary<string, string> headers, byte[] rawBody, string webhookSecret) => false;

    public IReadOnlyList<CarrierTrackingUpdate> ParseWebhook(byte[] rawBody) =>
        throw new NotSupportedException("The manual carrier does not support webhooks.");

    public bool IsKnownStatusCode(string carrierStatusCode) => Vocabulary.ContainsKey(carrierStatusCode);

    public ShipmentStatus? MapStatus(string carrierStatusCode, ShipmentDirection direction)
    {
        if (!Vocabulary.TryGetValue(carrierStatusCode, out var targets))
        {
            return null;
        }
        return direction == ShipmentDirection.Outbound ? targets.Outbound : targets.Inbound;
    }
}
