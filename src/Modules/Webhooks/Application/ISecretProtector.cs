namespace Courier.Webhooks.Application;

// Reversible encryption for WebhookSubscription.Secret (spec §5 — HMAC-signing an outgoing
// payload needs the plaintext at send time, so this must be encryption, not a one-way hash,
// despite DOMAIN-MODEL.md's original "hashed at rest" wording; Task 15 corrects that doc).
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
