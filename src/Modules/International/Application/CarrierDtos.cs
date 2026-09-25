namespace Courier.International.Application;

public class CarrierAccountValidationException : Exception
{
    public CarrierAccountValidationException(string message) : base(message) { }
}

// The carrier refused, errored or timed out on a booking/cancel call (502). The message is the
// sanitized reason, safe to return to staff.
public class CarrierBookingFailedException : Exception
{
    public CarrierBookingFailedException(string message) : base(message) { }
}

// The carrier's circuit breaker is open (503) — try again later.
public class CarrierUnavailableException : Exception
{
    public CarrierUnavailableException(string message) : base(message) { }
}

public record CreateCarrierAccountRequest(string CarrierCode, string DisplayName, Dictionary<string, string>? Credentials, bool PollingEnabled, bool GenerateWebhookSecret);

public record UpdateCarrierAccountRequest(string DisplayName, bool IsActive, bool PollingEnabled);

public record ReplaceCarrierCredentialsRequest(Dictionary<string, string> Credentials);

// Never carries secret values — only whether they are configured.
public record CarrierAccountResponse(
    Guid Id, string CarrierCode, string DisplayName, bool IsActive, bool PollingEnabled,
    bool HasCredentials, bool HasWebhookSecret, string WebhookPath, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

// WebhookSecret is present only in the create/rotate response that generated it — shown once.
public record CarrierAccountSecretResponse(CarrierAccountResponse Account, string? WebhookSecret);

public record BookCarrierRequest(Guid CarrierAccountId, string? ServiceLevel, string? CarrierAwb);

public record LinkInboundBookingRequest(Guid CarrierAccountId, string CarrierAwb, string? ServiceLevel);

public record CarrierBookingResponse(
    Guid Id, Guid CarrierAccountId, string CarrierCode, string CarrierAccountName, string Direction, string Status,
    string? CarrierAwb, string? ServiceLevel, bool HasLabel, string? FailureReason,
    DateTimeOffset? BookedAt, DateTimeOffset? CancelledAt, DateTimeOffset? CustodyStartedAt, DateTimeOffset CreatedAt);

public record ManualCarrierEventRequest(string StatusCode, DateTimeOffset OccurredAt, string? Location, string? Description);

// RawPayload is deliberately not exposed.
public record CarrierTrackingEventResponse(
    Guid Id, string Source, string CarrierAwb, string CarrierStatusCode, string? Description, string? Location,
    DateTimeOffset OccurredAt, DateTimeOffset ReceivedAt, string ProcessingStatus, string? ProcessingNote, string? MappedShipmentStatus)
{
    public static CarrierTrackingEventResponse From(Domain.CarrierTrackingEvent e) => new(
        e.Id, e.Source.ToString(), e.CarrierAwb, e.CarrierStatusCode, e.Description, e.Location,
        e.OccurredAt, e.ReceivedAt, e.ProcessingStatus.ToString(), e.ProcessingNote, e.MappedShipmentStatus);
}
