namespace Courier.Platform.Application;

public interface IPlatformJwtTokenService
{
    string IssueAccessToken(Guid userId, string email);
}
