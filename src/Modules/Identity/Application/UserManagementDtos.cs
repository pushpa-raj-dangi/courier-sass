namespace Courier.Identity.Application;

public record UserBranchGrantResponse(Guid BranchId, string BranchName, DateTimeOffset GrantedAt, DateTimeOffset? ExpiresAt);
public record GrantBranchRequest(Guid BranchId, DateTimeOffset? ExpiresAt);
public record UserListItem(Guid Id, string Email, string FullName, bool IsActive, string? RoleName, int BranchCount);
public record InviteUserRequest(string Email, string FullName, string? Phone, Guid RoleId, Guid BranchId, DateTimeOffset? ExpiresAt);
public record InviteUserResponse(Guid UserId, string Email);

public class UserManagementException : Exception
{
    public UserManagementException(string message) : base(message) { }
}
