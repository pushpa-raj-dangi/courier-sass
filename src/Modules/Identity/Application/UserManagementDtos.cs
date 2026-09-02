namespace Courier.Identity.Application;

public record UserBranchGrantResponse(Guid BranchId, string BranchName, DateTimeOffset GrantedAt, DateTimeOffset? ExpiresAt);
public record GrantBranchRequest(Guid BranchId, DateTimeOffset? ExpiresAt);

public class UserManagementException : Exception
{
    public UserManagementException(string message) : base(message) { }
}
