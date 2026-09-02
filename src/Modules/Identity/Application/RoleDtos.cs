namespace Courier.Identity.Application;

public record RoleResponse(Guid Id, string Name, bool IsSystemDefault, IReadOnlyList<string> PermissionKeys);
public record CreateRoleRequest(string Name, IReadOnlyList<string> PermissionKeys);
public record UpdateRoleRequest(IReadOnlyList<string> PermissionKeys);

public class RoleValidationException : Exception
{
    public RoleValidationException(string message) : base(message) { }
}
