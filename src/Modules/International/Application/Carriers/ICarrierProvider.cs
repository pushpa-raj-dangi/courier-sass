using Courier.Shipments.Domain;

namespace Courier.International.Application.Carriers;

// The carrier port (I4 spec §5.1). Adapters live in International.Infrastructure and use platform
// types only; network adapters are registered through AddCarrierHttpClient so they get the shared
// resilience pipeline. GetRatesAsync is added in I5.
public interface ICarrierProvider
{
    string CarrierCode { get; }
    CarrierCapabilities Capabilities { get; }

    Task<CarrierBookingResult> BookAsync(CarrierBookingRequest request, CarrierCredentials credentials, string idempotencyKey, CancellationToken cancellationToken);
    Task CancelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken);
    Task<CarrierLabel?> GetLabelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken);
    Task<IReadOnlyList<CarrierTrackingUpdate>> GetTrackingAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken);

    // headers are case-insensitive. Must compare signatures in constant time.
    bool VerifyWebhook(IReadOnlyDictionary<string, string> headers, byte[] rawBody, string webhookSecret);
    // Throws CarrierWebhookPayloadException for an unparseable body.
    IReadOnlyList<CarrierTrackingUpdate> ParseWebhook(byte[] rawBody);

    // Whether staff may enter this code manually (manual carrier events, I4 spec §5.5).
    bool IsKnownStatusCode(string carrierStatusCode);
    // null = informational only (the event is kept, shown on the timeline, never applied).
    ShipmentStatus? MapStatus(string carrierStatusCode, ShipmentDirection direction);
}
