namespace Courier.Merchants.Application;

public interface IMerchantLookupService
{
    // True iff a Merchant with this id exists — scoped to the current tenant automatically via
    // AppDbContext's query filter on Merchant (no explicit organizationId parameter needed).
    Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default);
}
