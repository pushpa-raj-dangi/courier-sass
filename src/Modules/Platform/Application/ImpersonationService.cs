using Courier.Audit.Application;
using Courier.Identity.Application;

namespace Courier.Platform.Application;

public record ImpersonationResult(string AccessToken, DateTimeOffset ExpiresAt, string ImpersonatedUserName, string OrganizationName);

public class ImpersonationService
{
    private readonly IOrganizationLookupService _organizationLookupService;
    private readonly IImpersonationTokenService _tokenService;
    private readonly IAuditLogger _auditLogger;

    public ImpersonationService(IOrganizationLookupService organizationLookupService, IImpersonationTokenService tokenService, IAuditLogger auditLogger)
    {
        _organizationLookupService = organizationLookupService;
        _tokenService = tokenService;
        _auditLogger = auditLogger;
    }

    public async Task<ImpersonationResult> IssueAsync(Guid organizationId, Guid targetUserId, Guid actingSuperAdminId, CancellationToken cancellationToken = default)
    {
        // Deliberately the same generic message for "org not found" and "user not found /
        // not in this org / inactive" — matches this codebase's established convention of never
        // disclosing which specific check failed on an auth-adjacent lookup.
        var organization = await _organizationLookupService.GetAsync(organizationId, cancellationToken)
            ?? throw new PlatformValidationException("Organization or user was not found.");
        var user = await _organizationLookupService.GetUserAsync(organizationId, targetUserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new PlatformValidationException("Organization or user was not found.");
        }

        var issuedToken = _tokenService.IssueImpersonationAccessToken(user.Id, organizationId, user.Email, actingSuperAdminId);

        // This audits the GRANT itself (a cross-tenant SuperAdmin action). Every action the
        // SuperAdmin subsequently takes WITH the issued token is separately audited as a normal
        // action against that org, with ImpersonatedByUserId populated automatically by
        // AuditLogger.LogAsync reading the token's "impersonated_by" claim (Task 5) — no code
        // outside this one grant needs to know impersonation is happening.
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingSuperAdminId, "platform.impersonation_started", "User", targetUserId.ToString(), IsCrossTenantAction: true), cancellationToken);

        return new ImpersonationResult(issuedToken.AccessToken, issuedToken.ExpiresAt, user.FullName, organization.Name);
    }
}
