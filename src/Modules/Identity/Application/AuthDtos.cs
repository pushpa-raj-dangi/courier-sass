namespace Courier.Identity.Application;

public record RegisterOrganizationRequest(string OrganizationName, string OrganizationSlug, string OwnerEmail, string OwnerPassword, string OwnerFullName);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record AcceptInviteRequest(string Token, string Password);
public record AuthResult(string AccessToken, string RefreshToken, Guid UserId, Guid OrganizationId);
public record MeBranchGrant(Guid BranchId, string BranchName, DateTimeOffset? ExpiresAt);
public record MeResponse(Guid UserId, Guid OrganizationId, string Email, string FullName, IReadOnlyList<string> Permissions, bool HasAllBranchesScope, IReadOnlyList<MeBranchGrant> Branches);

public class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException(string message) : base(message) { }
}
