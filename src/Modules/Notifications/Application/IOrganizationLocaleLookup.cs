namespace Courier.Notifications.Application;

// Implemented by Courier.Api (OrganizationLocaleLookupAdapter) against Identity's
// OrganizationSettingsService so Notifications never takes a project reference to Identity —
// same shape as Branches' IBranchPlanLimitProvider.
public interface IOrganizationLocaleLookup
{
    Task<string> GetDefaultLocaleAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
