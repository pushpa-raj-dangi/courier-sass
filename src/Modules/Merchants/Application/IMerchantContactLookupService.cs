namespace Courier.Merchants.Application;

// Separate from IMerchantLookupService (ExistsAsync) deliberately — IMerchantLookupService is
// implemented as a fake in many files across several test projects; extending it for a need only
// Notifications has would force every one of those fakes to grow a member they'd never exercise.
// MerchantLookupService implements both interfaces (see MerchantLookupService.cs) — the same
// "one class, two narrow interfaces" shape CodService already uses for
// ICodCollectionCreationService/ICodCollectionService.
public interface IMerchantContactLookupService
{
    // Null if the merchant has no ContactEmail on file (a normal, expected state — Merchant.
    // ContactEmail is nullable) or the merchant doesn't exist in this organization.
    Task<string?> GetContactEmailAsync(Guid merchantId, CancellationToken cancellationToken = default);
}
