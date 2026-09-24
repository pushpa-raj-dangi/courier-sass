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
