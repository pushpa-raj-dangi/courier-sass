using System.Security.Cryptography;
using System.Text;

namespace Courier.Webhooks.Application;

// X-Webhook-Signature: sha256=<HMAC> per API-SPEC.md §5 — computed over the exact raw outgoing
// payload bytes using the subscription's decrypted secret. A static pure function (no DbContext,
// no I/O) so both the consumer (Task 11) and the retry job (Task 12) call the identical
// computation, and so it's trivially unit-testable without any infrastructure.
public static class WebhookSignature
{
    public static string Compute(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return $"sha256={Convert.ToHexStringLower(hash)}";
    }
}
