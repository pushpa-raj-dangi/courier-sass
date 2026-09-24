using Microsoft.AspNetCore.Http;

namespace Courier.SharedKernel;

public class HttpContextCurrentTenantProvider : ICurrentTenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TenantScopeOverride _tenantScopeOverride;

    public HttpContextCurrentTenantProvider(IHttpContextAccessor httpContextAccessor, TenantScopeOverride tenantScopeOverride)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantScopeOverride = tenantScopeOverride;
    }

    public Guid? OrganizationId
    {
        get
        {
            // A background tenant scope (TenantScopeFactory) wins; it is never set on a request path.
            if (_tenantScopeOverride.OrganizationId is Guid overridden)
            {
                return overridden;
            }
            var claim = _httpContextAccessor.HttpContext?.User.FindFirst("org_id")?.Value;
            return Guid.TryParse(claim, out var id) ? id : null;
        }
    }
}
