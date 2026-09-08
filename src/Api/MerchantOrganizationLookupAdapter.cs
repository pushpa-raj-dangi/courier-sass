using Courier.Identity.Application;
using Courier.Merchants.Application;

namespace Courier.Api;

// Bridges Merchants.Application.IMerchantOrganizationLookupService (consumed by
// MerchantAuthService/ApiKeyAuthenticationHandler) to Identity.Application.
// IOrganizationLookupService.IsActiveAsync (the real suspension check, Task 5) without either
// module taking a project reference to the other — see IMerchantOrganizationLookupService.cs
// for why a direct Merchants -> Identity reference would create a project-reference cycle
// (Identity -> Branches -> Merchants -> Identity). The Api project already references both
// modules and is referenced by neither, so it is the one place that can wire this adapter.
public class MerchantOrganizationLookupAdapter : IMerchantOrganizationLookupService
{
    private readonly IOrganizationLookupService _organizationLookupService;

    public MerchantOrganizationLookupAdapter(IOrganizationLookupService organizationLookupService)
    {
        _organizationLookupService = organizationLookupService;
    }

    public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        _organizationLookupService.IsActiveAsync(organizationId, cancellationToken);
}
