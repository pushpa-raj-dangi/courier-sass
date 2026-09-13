using Courier.Audit.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class OrganizationSettingsService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;

    public OrganizationSettingsService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
    }

    public async Task<OrganizationSettingsDto> GetAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateAsync(organizationId, cancellationToken);
        return ToDto(settings);
    }

    // Guid-keyed, no ClaimsPrincipal: called from AuthService's login/refresh/invite-accept flows
    // (Task 4), which run before or entirely without a resolved acting-staff-user principal.
    public async Task<SecurityPolicyDto> GetSecurityPolicyAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateAsync(organizationId, cancellationToken);
        return new SecurityPolicyDto(settings.RefreshTokenLifetimeDays, settings.MinPasswordLength);
    }

    // IgnoreQueryFilters(): every caller of this method either already holds a resolved ambient
    // tenant (staff Settings endpoints, Task 2/3/5) or explicitly runs with no ambient tenant at
    // all (AuthService's pre-authentication flows, Task 4) — the explicit organizationId
    // predicate is the real scope in both cases, matching OrganizationAdminService.IsActiveAsync.
    private async Task<OrganizationSettings> GetOrCreateAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.Set<OrganizationSettings>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = new OrganizationSettings { OrganizationId = organizationId };
        _dbContext.Set<OrganizationSettings>().Add(settings);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private static OrganizationSettingsDto ToDto(OrganizationSettings s) => new(
        new OrganizationProfileDto(s.SupportEmail, s.SupportPhone, s.AddressLine, s.LogoUrl, s.TimeZoneId, s.BusinessHours),
        new SecurityPolicyDto(s.RefreshTokenLifetimeDays, s.MinPasswordLength),
        new OperationalDefaultsDto(s.DefaultServiceTypeId, s.DefaultOriginBranchId));
}
