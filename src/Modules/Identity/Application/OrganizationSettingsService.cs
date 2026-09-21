using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Domain;
using Courier.Pricing.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class OrganizationSettingsService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IOrganizationLookupService _organizationLookupService;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IServiceTypeLookupService _serviceTypeLookupService;

    public OrganizationSettingsService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger, IOrganizationLookupService organizationLookupService, IBranchLookupService branchLookupService, IServiceTypeLookupService serviceTypeLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _organizationLookupService = organizationLookupService;
        _branchLookupService = branchLookupService;
        _serviceTypeLookupService = serviceTypeLookupService;
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

    public async Task<OrganizationProfileDto> UpdateProfileAsync(Guid organizationId, Guid actingUserId, OrganizationProfileDto profile, CancellationToken cancellationToken = default)
    {
        if (profile.DefaultLocale is not null && !OrganizationLocaleOptions.SupportedLocales.Contains(profile.DefaultLocale))
        {
            throw new OrganizationSettingsValidationException($"Locale must be one of: {string.Join(", ", OrganizationLocaleOptions.SupportedLocales)}.");
        }
        if (profile.CalendarPreference is not null && !OrganizationLocaleOptions.SupportedCalendars.Contains(profile.CalendarPreference))
        {
            throw new OrganizationSettingsValidationException($"Calendar must be one of: {string.Join(", ", OrganizationLocaleOptions.SupportedCalendars)}.");
        }

        var settings = await GetOrCreateAsync(organizationId, cancellationToken);
        var before = new OrganizationProfileDto(settings.SupportEmail, settings.SupportPhone, settings.AddressLine, settings.LogoUrl, settings.TimeZoneId, settings.BusinessHours, settings.DefaultLocale, settings.CalendarPreference);

        settings.SupportEmail = profile.SupportEmail;
        settings.SupportPhone = profile.SupportPhone;
        settings.AddressLine = profile.AddressLine;
        settings.LogoUrl = profile.LogoUrl;
        settings.TimeZoneId = string.IsNullOrWhiteSpace(profile.TimeZoneId) ? "Asia/Kathmandu" : profile.TimeZoneId;
        settings.BusinessHours = profile.BusinessHours;
        settings.DefaultLocale = profile.DefaultLocale ?? settings.DefaultLocale;
        settings.CalendarPreference = profile.CalendarPreference ?? settings.CalendarPreference;
        settings.ProfileUpdatedAt = DateTimeOffset.UtcNow;
        settings.ProfileUpdatedByUserId = actingUserId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Audit the resulting state, not the request: null locale/calendar mean "unchanged".
        var after = new OrganizationProfileDto(settings.SupportEmail, settings.SupportPhone, settings.AddressLine, settings.LogoUrl, settings.TimeZoneId, settings.BusinessHours, settings.DefaultLocale, settings.CalendarPreference);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "organization.profile_updated", "OrganizationSettings", organizationId.ToString(),
            Before: System.Text.Json.JsonSerializer.Serialize(before), After: System.Text.Json.JsonSerializer.Serialize(after)), cancellationToken);

        return after;
    }

    public async Task<SecurityPolicyDto> UpdateSecurityAsync(Guid organizationId, Guid actingUserId, SecurityPolicyDto policy, CancellationToken cancellationToken = default)
    {
        if (policy.RefreshTokenLifetimeDays is < 1 or > 30)
        {
            throw new OrganizationSettingsValidationException("Refresh token lifetime must be between 1 and 30 days.");
        }
        if (policy.MinPasswordLength is < 8 or > 64)
        {
            throw new OrganizationSettingsValidationException("Minimum password length must be between 8 and 64 characters.");
        }

        var settings = await GetOrCreateAsync(organizationId, cancellationToken);
        var before = new SecurityPolicyDto(settings.RefreshTokenLifetimeDays, settings.MinPasswordLength);

        settings.RefreshTokenLifetimeDays = policy.RefreshTokenLifetimeDays;
        settings.MinPasswordLength = policy.MinPasswordLength;
        settings.SecurityUpdatedAt = DateTimeOffset.UtcNow;
        settings.SecurityUpdatedByUserId = actingUserId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "organization.security_updated", "OrganizationSettings", organizationId.ToString(),
            Before: System.Text.Json.JsonSerializer.Serialize(before), After: System.Text.Json.JsonSerializer.Serialize(policy)), cancellationToken);

        return new SecurityPolicyDto(settings.RefreshTokenLifetimeDays, settings.MinPasswordLength);
    }

    public async Task RevokeAllSessionsAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        await _organizationLookupService.RevokeStaffRefreshTokensAsync(organizationId, cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "organization.sessions_revoked", "OrganizationSettings", organizationId.ToString()), cancellationToken);
    }

    public async Task<OperationalDefaultsDto> UpdateDefaultsAsync(Guid organizationId, Guid actingUserId, OperationalDefaultsDto defaults, CancellationToken cancellationToken = default)
    {
        if (defaults.DefaultOriginBranchId is Guid branchId && await _branchLookupService.GetZoneInfoAsync(branchId, cancellationToken) is null)
        {
            throw new OrganizationSettingsValidationException("Default origin branch does not exist.");
        }
        if (defaults.DefaultServiceTypeId is Guid serviceTypeId && !await _serviceTypeLookupService.ExistsAsync(serviceTypeId, cancellationToken))
        {
            throw new OrganizationSettingsValidationException("Default service type does not exist.");
        }

        var settings = await GetOrCreateAsync(organizationId, cancellationToken);
        var before = new OperationalDefaultsDto(settings.DefaultServiceTypeId, settings.DefaultOriginBranchId);

        settings.DefaultServiceTypeId = defaults.DefaultServiceTypeId;
        settings.DefaultOriginBranchId = defaults.DefaultOriginBranchId;
        settings.DefaultsUpdatedAt = DateTimeOffset.UtcNow;
        settings.DefaultsUpdatedByUserId = actingUserId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "organization.defaults_updated", "OrganizationSettings", organizationId.ToString(),
            Before: System.Text.Json.JsonSerializer.Serialize(before), After: System.Text.Json.JsonSerializer.Serialize(defaults)), cancellationToken);

        return new OperationalDefaultsDto(settings.DefaultServiceTypeId, settings.DefaultOriginBranchId);
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
        new OrganizationProfileDto(s.SupportEmail, s.SupportPhone, s.AddressLine, s.LogoUrl, s.TimeZoneId, s.BusinessHours, s.DefaultLocale, s.CalendarPreference),
        new SecurityPolicyDto(s.RefreshTokenLifetimeDays, s.MinPasswordLength),
        new OperationalDefaultsDto(s.DefaultServiceTypeId, s.DefaultOriginBranchId));
}
