using Courier.Audit.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class OrganizationAdminService : IOrganizationLookupService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;

    public OrganizationAdminService(DbContext dbContext, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
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
    public async Task<PagedResult<OrganizationRecord>> ListAsync(string? search, bool? isActive, bool? isDeleted, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Organization>().IgnoreQueryFilters().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Case-insensitive via ToLower() on both sides rather than EF.Functions.ILike — this
            // module has no reference to Npgsql.EntityFrameworkCore.PostgreSQL (only the
            // provider-agnostic Microsoft.EntityFrameworkCore package), and adding it solely for
            // this one search feature isn't justified. Matches name/slug directly, or any user's
            // email within the org via a correlated Any() subquery — Users also carries a tenant
            // query filter, so this subquery must IgnoreQueryFilters() too or it would silently
            // match nothing under Platform's always-null ambient tenant.
            var term = search.ToLower();
            query = query.Where(o =>
                o.Name.ToLower().Contains(term) ||
                o.Slug.ToLower().Contains(term) ||
                _dbContext.Set<User>().IgnoreQueryFilters().Any(u => u.OrganizationId == o.Id && u.Email.ToLower().Contains(term)));
        }
        if (isActive is bool activeFilter)
        {
            query = query.Where(o => o.IsActive == activeFilter);
        }
        if (isDeleted is bool deletedFilter)
        {
            query = deletedFilter ? query.Where(o => o.DeletedAt != null) : query.Where(o => o.DeletedAt == null);
        }

        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var organizations = await query
            .OrderBy(o => o.Name)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(o => new OrganizationRecord(o.Id, o.Name, o.Slug, o.IsActive, o.SuspendedBySystem, o.DeletedAt, o.CreatedAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<OrganizationRecord>(organizations, normalizedPage, normalizedPageSize, totalCount);
    }

    public async Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrThrowAsync(organizationId, cancellationToken);
        organization.IsActive = false;
        organization.SuspendedBySystem = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Distinct from SuspendAsync (Task 9, a SuperAdmin's manual action, audited with the acting
    // user's UserId) — this is the system's own action, audited with a null UserId to make "the
    // system, not a person, did this" legible in the audit trail per CLAUDE.md §39.
    public async Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrThrowAsync(organizationId, cancellationToken);
        if (!organization.IsActive)
        {
            // Already suspended (by this same job on a prior run, or manually) — avoid
            // re-writing an identical audit row every time the job re-scans an already-overdue
            // invoice it can't yet mark Paid.
            return;
        }
        organization.IsActive = false;
        organization.SuspendedBySystem = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, null, "organization.suspended_for_nonpayment", "Organization", organizationId.ToString(), IsCrossTenantAction: true), cancellationToken);
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

    // IgnoreQueryFilters(): called only from Platform's PlatformDashboardService.GetOrganizationDetailAsync
    // (SuperAdmin, cross-tenant, ICurrentTenantProvider.OrganizationId == null) — without this the
    // ambient Organizations query filter fails closed and this would always return null.
    public async Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => await _dbContext.Set<Organization>().IgnoreQueryFilters()
            .Where(o => o.Id == organizationId)
            .Select(o => new OrganizationRecord(o.Id, o.Name, o.Slug, o.IsActive, o.SuspendedBySystem, o.DeletedAt, o.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<User>().IgnoreQueryFilters().Where(u => u.OrganizationId == organizationId);
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(u => u.Email)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(u => new OrganizationUserRecord(u.Id, u.Email, u.FullName, u.IsActive))
            .ToListAsync(cancellationToken);
        return new PagedResult<OrganizationUserRecord>(items, normalizedPage, normalizedPageSize, totalCount);
    }

    // IgnoreQueryFilters(): called only from Platform's ImpersonationService (SuperAdmin,
    // cross-tenant, ICurrentTenantProvider.OrganizationId == null) — same reasoning as
    // GetAsync/ListUsersAsync above. The explicit organizationId equality check (rather than just
    // looking up by userId) is what stops a SuperAdmin from impersonating a user under the wrong
    // organizationId path parameter.
    public async Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Set<User>().IgnoreQueryFilters()
            .Where(u => u.Id == userId && u.OrganizationId == organizationId)
            .Select(u => new OrganizationUserRecord(u.Id, u.Email, u.FullName, u.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    // IgnoreQueryFilters(): shared helper for the four lifecycle mutations above, all called only
    // from Platform's OrganizationLifecycleService (SuperAdmin, cross-tenant,
    // ICurrentTenantProvider.OrganizationId == null) — without this the ambient Organizations
    // query filter fails closed and GetOrThrowAsync would always throw "not found."
    private async Task<Organization> GetOrThrowAsync(Guid organizationId, CancellationToken cancellationToken)
        => await _dbContext.Set<Organization>().IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw new InvalidOperationException($"Organization {organizationId} was not found.");
}
