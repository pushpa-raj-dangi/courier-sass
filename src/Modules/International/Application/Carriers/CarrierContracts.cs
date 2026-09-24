namespace Courier.International.Application.Carriers;

public record CarrierCapabilities(bool SupportsWebhooks, bool SupportsPolling, bool RequiresManualAwb, bool SupportsLabels);

// Decrypted carrier credentials. Held only for the duration of one provider call; ToString never
// prints values (records print every member by default), so accidental logging cannot leak them.
public sealed record CarrierCredentials(IReadOnlyDictionary<string, string> Values)
{
    public static CarrierCredentials Empty { get; } = new(new Dictionary<string, string>());

    // Values long enough to be worth masking in stored carrier payloads.
    public IEnumerable<string> SecretValues => Values.Values.Where(v => v.Length >= 4);

    public override string ToString() => $"CarrierCredentials({Values.Count} values)";
}

// Platform-side booking input. I6 (DHL) extends this with the addresses/customs it needs.
public record CarrierBookingRequest(Guid ShipmentId, string PlatformAwb, string? ServiceLevel, string? ManualCarrierAwb, decimal WeightKg);

public record CarrierLabel(byte[] Content, string ContentType);

public record CarrierBookingResult(string CarrierAwb, string? ServiceLevel, CarrierLabel? Label, string? RawResponse);

public record CarrierTrackingUpdate(
    string CarrierAwb,
    string CarrierEventId,
    string CarrierStatusCode,
    string? Description,
    string? Location,
    DateTimeOffset OccurredAt,
    string? RawPayload);

// The carrier understood the request and refused it (bad data, unknown AWB, account problem).
public class CarrierBookingException : Exception
{
    public CarrierBookingException(string message) : base(message) { }
}

// A webhook body the provider could not parse (after its signature verified).
public class CarrierWebhookPayloadException : Exception
{
    public CarrierWebhookPayloadException(string message) : base(message) { }
}
