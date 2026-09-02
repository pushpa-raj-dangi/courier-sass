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

        group.MapGet("/roles", async (RoleService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListRolesAsync(cancellationToken)))
            .RequirePermission("role.manage");

        group.MapPost("/roles", async (CreateRoleRequest request, RoleService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CreateRoleAsync(request, httpContext.User.GetUserId()!.Value, httpContext.RequestAborted);
                return Results.Ok(result);
            }
            catch (RoleValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "ROLE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("role.manage");

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
