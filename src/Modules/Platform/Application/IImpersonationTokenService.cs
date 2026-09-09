namespace Courier.Platform.Application;

// The token's real expiry is returned alongside the token itself (rather than the caller
// independently recomputing "now + lifetime") so ImpersonationResult.ExpiresAt can never drift
// from the exp actually encoded in the JWT.
public record ImpersonationTokenIssued(string AccessToken, DateTimeOffset ExpiresAt);

public interface IImpersonationTokenService
{
    ImpersonationTokenIssued IssueImpersonationAccessToken(Guid targetUserId, Guid organizationId, string email, Guid impersonatingSuperAdminId);
}
