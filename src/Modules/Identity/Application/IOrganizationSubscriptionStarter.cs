namespace Courier.Identity.Application;

// Local port for starting a new organization's trial subscription, consumed by
// AuthService.RegisterOrganizationAsync. Defined here — rather than AuthService taking a direct
// dependency on Courier.Billing.Application.IBillingLookupService — to avoid a circular project
// reference: Billing already references Identity (for Organization), so a reverse
// Identity -> Billing reference would create a cycle. The Api project already references both
// modules and is referenced by neither, so it is the one place that can bridge this (see
// Api/OrganizationSubscriptionStarterAdapter.cs), matching the same shape already used for
// Courier.Branches.Application.IBranchPlanLimitProvider / BranchPlanLimitAdapter.
public interface IOrganizationSubscriptionStarter
{
    Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default);
}
