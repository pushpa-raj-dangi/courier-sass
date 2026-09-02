using Courier.Identity.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Identity.Presentation;

public static class UsersEndpoints
{
    public static void MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/users", async (int? page, int? pageSize, UserManagementService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListUsersAsync(page, pageSize, cancellationToken)))
        .RequirePermission("user.read")
        .WithTags("Users Management");

        app.MapGet("/api/v1/users/{id:guid}/branches", async (Guid id, UserManagementService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListUserBranchGrantsAsync(id, cancellationToken)))
            .RequirePermission("user.read")
            .WithTags("Users Management");

        app.MapPost("/api/v1/users/{id:guid}/branches", async (Guid id, GrantBranchRequest request, UserManagementService service, HttpContext httpContext) =>
        {
            try
            {
                return Results.Ok(await service.GrantBranchAsync(id, request, httpContext.User.GetUserId()!.Value, httpContext.RequestAborted));
            }
            catch (UserManagementException ex)
            {
                return Results.BadRequest(new { error = new { code = "USER_MANAGEMENT_FAILED", message = ex.Message } });
            }
        }).RequirePermission("user.update")
        .WithTags("Users Management");

        app.MapDelete("/api/v1/users/{id:guid}/branches/{branchId:guid}", async (Guid id, Guid branchId, UserManagementService service, HttpContext httpContext) =>
        {
            try
            {
                await service.RevokeBranchAsync(id, branchId, httpContext.User.GetUserId()!.Value, httpContext.RequestAborted);
                return Results.NoContent();
            }
            catch (UserManagementException ex)
            {
                return Results.BadRequest(new { error = new { code = "USER_MANAGEMENT_FAILED", message = ex.Message } });
            }
        }).RequirePermission("user.update")
        .WithTags("Users Management");

        app.MapPatch("/api/v1/users/{id:guid}/deactivate", async (Guid id, UserManagementService service, HttpContext httpContext) =>
        {
            try
            {
                await service.DeactivateUserAsync(id, httpContext.User.GetUserId()!.Value, httpContext.RequestAborted);
                return Results.NoContent();
            }
            catch (UserManagementException ex)
            {
                return Results.BadRequest(new { error = new { code = "USER_MANAGEMENT_FAILED", message = ex.Message } });
            }
        }).RequirePermission("user.update")
        .WithTags("Users Management");

        app.MapPatch("/api/v1/users/{id:guid}/reactivate", async (Guid id, UserManagementService service, HttpContext httpContext) =>
        {
            try
            {
                await service.ReactivateUserAsync(id, httpContext.User.GetUserId()!.Value, httpContext.RequestAborted);
                return Results.NoContent();
            }
            catch (UserManagementException ex)
            {
                return Results.BadRequest(new { error = new { code = "USER_MANAGEMENT_FAILED", message = ex.Message } });
            }
        }).RequirePermission("user.update")
        .WithTags("Users Management");
    }
}
