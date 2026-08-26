namespace Courier.Merchants.Application;

// Separate from IMerchantLookupService (ExistsAsync) deliberately — IMerchantLookupService is
// implemented as a fake in many files across several test projects; extending it for a need only
// Notifications has would force every one of those fakes to grow a member they'd never exercise.
// MerchantLookupService implements both interfaces (see MerchantLookupService.cs) — the same
// "one class, two narrow interfaces" shape CodService already uses for
// ICodCollectionCreationService/ICodCollectionService.
public interface IMerchantContactLookupService
{
    // organizationId is a required, explicit parameter (not resolved from ambient tenant
    // context) because this method's real caller (Notifications.Application.
    // NotificationDispatchService) runs from a background service with no
    // ICurrentTenantProvider context at all — the implementation must filter by it directly
    // rather than relying on AppDbContext's automatic query filter, which would otherwise
    // silently hide every Merchant row when no tenant context is present. Null if the merchant
    // has no ContactEmail on file (a normal, expected state — Merchant.ContactEmail is nullable)
    // or doesn't exist in the given organization.
    Task<string?> GetContactEmailAsync(Guid organizationId, Guid merchantId, CancellationToken cancellationToken = default);
}
