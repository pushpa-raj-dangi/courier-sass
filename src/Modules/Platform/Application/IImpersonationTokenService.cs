namespace Courier.Platform.Application;

public interface IImpersonationTokenService
{
    string IssueImpersonationAccessToken(Guid targetUserId, Guid organizationId, string email, Guid impersonatingSuperAdminId);
}
