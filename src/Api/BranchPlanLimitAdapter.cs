using Courier.Billing.Application;
using Courier.Branches.Application;

namespace Courier.Api;

// Bridges Branches.Application.IBranchPlanLimitProvider (consumed by
// BranchService.CreateBranchAsync, Task 12) to Billing.Application.IBillingLookupService.
// GetMaxBranchesAsync without either module taking a project reference to the other — see
// IBranchPlanLimitProvider.cs for why a direct Branches -> Billing reference would create a
// project-reference cycle (Identity -> Branches, Billing -> Identity). The Api project already
// references both modules (transitively, via Persistence) and is referenced by neither, so it is
// the one place that can wire this adapter — same shape as MerchantOrganizationLookupAdapter.
public class BranchPlanLimitAdapter : IBranchPlanLimitProvider
{
    private readonly IBillingLookupService _billingLookupService;

    public BranchPlanLimitAdapter(IBillingLookupService billingLookupService)
    {
        _billingLookupService = billingLookupService;
    }

    public Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        _billingLookupService.GetMaxBranchesAsync(organizationId, cancellationToken);
}
