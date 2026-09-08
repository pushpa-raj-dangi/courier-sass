using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class OrganizationAdminService : IOrganizationLookupService
{
    private readonly DbContext _dbContext;

    public OrganizationAdminService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
        (await _dbContext.Set<Organization>().FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken))?.Slug;

    // IgnoreQueryFilters(): this is called from pre-tenant-context surfaces (staff login/refresh,
    // merchant login, API-key authentication all run before any tenant is resolved) as well as
    // background jobs with no ambient HTTP request at all — AppDbContext's Organization query
    // filter fails closed (matches nothing) whenever ICurrentTenantProvider.OrganizationId is
    // null, which is exactly the situation in every one of those callers.
    public async Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await _dbContext.Set<Organization>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);
        return organization is { IsActive: true, DeletedAt: null };
    }

    public async Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await _dbContext.Set<Organization>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);
        if (organization is { SuspendedBySystem: true })
        {
            organization.IsActive = true;
            organization.SuspendedBySystem = false;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
