namespace Courier.Billing.Application;

public interface IBillingLookupService
{
    // Null means "no limit" (unlimited) OR "this org has no Subscription yet" — both cases the
    // caller (BranchService, Task 12) must treat identically as "do not block."
    Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
