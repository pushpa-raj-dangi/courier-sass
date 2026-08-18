namespace Courier.Identity.Application;

public interface IJwtTokenService
{
    string IssueAccessToken(Guid userId, Guid organizationId, string email);
}
