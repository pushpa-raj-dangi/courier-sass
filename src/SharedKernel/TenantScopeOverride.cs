namespace Courier.SharedKernel;

// Scoped. Set only by TenantScopeFactory.CreateScope, for background jobs (no HttpContext) that
// must run tenant-filtered application services as one specific organization. No request path
// ever sets it, so HTTP requests keep resolving the organization from the caller's own JWT.
public sealed class TenantScopeOverride
{
    public Guid? OrganizationId { get; set; }
}
