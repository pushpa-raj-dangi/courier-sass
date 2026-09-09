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

    // IgnoreQueryFilters(): called only from Platform's OrganizationLifecycleService, which always
    // runs with ICurrentTenantProvider.OrganizationId == null (SuperAdmin, cross-tenant) — the
    // ambient Organizations query filter fails closed in that situation and would otherwise
    // silently return an empty list.
    public async Task<List<OrganizationRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Organization>().IgnoreQueryFilters()
            .OrderBy(o => o.Name)
            .Select(o => new OrganizationRecord(o.Id, o.Name, o.Slug, o.IsActive, o.SuspendedBySystem, o.DeletedAt, o.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrThrowAsync(organizationId, cancellationToken);
        organization.IsActive = false;
        organization.SuspendedBySystem = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrThrowAsync(organizationId, cancellationToken);
        organization.IsActive = true;
        organization.SuspendedBySystem = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrThrowAsync(organizationId, cancellationToken);
        organization.DeletedAt = DateTimeOffset.UtcNow;
        organization.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrThrowAsync(organizationId, cancellationToken);
        // Deliberately does NOT set IsActive = true — a separate explicit Reactivate is required
        // (spec's "two-step, no accidental instant-back-online" rule).
        organization.DeletedAt = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var userIds = await _dbContext.Set<User>().IgnoreQueryFilters()
            .Where(u => u.OrganizationId == organizationId)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        var tokens = await _dbContext.Set<RefreshToken>()
            .Where(rt => userIds.Contains(rt.UserId) && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // IgnoreQueryFilters(): shared helper for the four lifecycle mutations above, all called only
    // from Platform's OrganizationLifecycleService (SuperAdmin, cross-tenant,
    // ICurrentTenantProvider.OrganizationId == null) — without this the ambient Organizations
    // query filter fails closed and GetOrThrowAsync would always throw "not found."
    private async Task<Organization> GetOrThrowAsync(Guid organizationId, CancellationToken cancellationToken)
        => await _dbContext.Set<Organization>().IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw new InvalidOperationException($"Organization {organizationId} was not found.");
}
