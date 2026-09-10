using Courier.Billing.Application;
using Courier.Identity.Application;

namespace Courier.Api;

// Bridges Identity.Application.IOrganizationSubscriptionStarter (consumed by
// AuthService.RegisterOrganizationAsync) to Billing.Application.IBillingLookupService.
// StartTrialAsync without either module taking a project reference to the other — see
// IOrganizationSubscriptionStarter.cs for why a direct Identity -> Billing reference would
// create a project-reference cycle. The Api project already references both modules
// (transitively, via Persistence) and is referenced by neither, so it is the one place that can
// wire this adapter — same shape as BranchPlanLimitAdapter/MerchantOrganizationLookupAdapter.
public class OrganizationSubscriptionStarterAdapter : IOrganizationSubscriptionStarter
{
    private readonly IBillingLookupService _billingLookupService;

    public OrganizationSubscriptionStarterAdapter(IBillingLookupService billingLookupService)
    {
        _billingLookupService = billingLookupService;
    }

    public Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default) =>
        _billingLookupService.StartTrialAsync(organizationId, actingUserId, cancellationToken);
}
