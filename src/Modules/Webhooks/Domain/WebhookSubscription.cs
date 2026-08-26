namespace Courier.Webhooks.Domain;

// Secret is ciphertext (ASP.NET Core Data Protection, Task 10) — never the plaintext signing
// key. The plaintext is shown exactly once, in the POST /api/v1/webhooks response body, and
// never persisted anywhere in plaintext form. Events lists the CLAUDE.md §26 event-type strings
// this subscription wants delivered ("shipment.created", "cod.collected", etc.) — a merchant may
// list any of the 10 documented types even though this phase only ever publishes 4 of them
// (spec §2); an unpublished type simply never fires, which is honest, not a bug.
public class WebhookSubscription
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Guid OrganizationId { get; set; }
    public required string TargetUrl { get; set; }
    public required string Secret { get; set; }
    public required string[] Events { get; set; }
    public bool IsActive { get; set; }
}
