namespace Courier.Branches.Application;

// Local port for reading the tenant's plan branch-count limit, consumed by
// BranchService.CreateBranchAsync (Task 12). Defined here — rather than BranchService taking a
// direct dependency on Courier.Billing.Application.IBillingLookupService — to avoid a circular
// project reference: Identity -> Branches, and Billing -> Identity, so a Branches -> Billing
// reference would create a cycle. The Api project already references both modules and is
// referenced by neither, so it is the one place that can bridge this (see
// Api/BranchPlanLimitAdapter.cs), matching the same shape already used for
// Courier.Merchants.Application.IMerchantOrganizationLookupService / MerchantOrganizationLookupAdapter.
public interface IBranchPlanLimitProvider
{
    // Null means "no limit" (unlimited) OR "this org has no Subscription yet" — the caller
    // (BranchService.CreateBranchAsync) must treat both identically as "do not block."
    Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
