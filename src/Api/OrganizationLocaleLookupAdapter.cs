using Courier.Identity.Application;
using Courier.Notifications.Application;

namespace Courier.Api;

// Bridges Notifications.Application.IOrganizationLocaleLookup to Identity's
// OrganizationSettingsService.GetLocaleAsync (Guid-keyed, permission-free, ignores the ambient
// tenant filter — safe from NotificationsConsumer's no-tenant-context scope). The Api project is
// the one place that references both modules.
public class OrganizationLocaleLookupAdapter : IOrganizationLocaleLookup
{
    private readonly OrganizationSettingsService _settingsService;

    public OrganizationLocaleLookupAdapter(OrganizationSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<string> GetDefaultLocaleAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        (await _settingsService.GetLocaleAsync(organizationId, cancellationToken)).DefaultLocale;
}
