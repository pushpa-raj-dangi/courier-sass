using System.Text.Json;

namespace Courier.International.Application.Carriers;

// Carrier responses/payloads are stored (jsonb) for support and audit, with any known secret value
// replaced — a carrier echoing our API key must never land in the database in plaintext.
public static class CarrierPayloadSanitizer
{
    public const string Mask = "***";

    public static string MaskSecrets(string text, IEnumerable<string> secretValues)
    {
        foreach (var secret in secretValues.Where(s => !string.IsNullOrEmpty(s)).OrderByDescending(s => s.Length))
        {
            text = text.Replace(secret, Mask, StringComparison.Ordinal);
        }
        return text;
    }

    // Always valid JSON (the columns are jsonb): JSON input is kept as JSON, anything else is
    // stored as a JSON string.
    public static string? ToStoredJson(string? raw, IEnumerable<string> secretValues)
    {
        if (raw is null)
        {
            return null;
        }
        var masked = MaskSecrets(raw, secretValues);
        try
        {
            using var _ = JsonDocument.Parse(masked);
            return masked;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(masked);
        }
    }
}
