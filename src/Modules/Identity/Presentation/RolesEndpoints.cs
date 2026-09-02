using Courier.Identity.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Identity.Presentation;

public record PermissionItem(string Key, string Description);
public record PermissionGroupResponse(string Group, IReadOnlyList<PermissionItem> Permissions);

public static class RolesEndpoints
{
    public static void MapRolesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Roles Management");

        group.MapGet("/permissions", () =>
            Results.Ok(GroupPermissions(PermissionCatalog.All)))
            .RequirePermission("role.manage");
    }

    // Public and static: no DbContext dependency (the catalog is a static in-process list, not a
    // table any caller filters), which makes this trivially unit-testable without a database.
    public static List<PermissionGroupResponse> GroupPermissions(IReadOnlyList<(string Key, string Description)> all)
    {
        return all
            .GroupBy(p => p.Key.Split('.', 2)[0])
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PermissionGroupResponse(g.Key, g.Select(p => new PermissionItem(p.Key, p.Description)).ToList()))
            .ToList();
    }
}
