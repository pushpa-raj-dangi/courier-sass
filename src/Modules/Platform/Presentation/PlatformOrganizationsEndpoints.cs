using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformOrganizationsEndpoints
{
    public static void MapPlatformOrganizationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/organizations").WithTags("Platform Organizations");

        group.MapGet("/", async (OrganizationLifecycleService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListOrganizationsAsync(cancellationToken)))
            .RequireSuperAdmin();

        group.MapPost("/{id:guid}/suspend", async (Guid id, OrganizationLifecycleService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            await service.SuspendAsync(id, httpContext.User.GetUserId()!.Value, cancellationToken);
            return Results.NoContent();
        }).RequireSuperAdmin();

        group.MapPost("/{id:guid}/reactivate", async (Guid id, OrganizationLifecycleService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            await service.ReactivateAsync(id, httpContext.User.GetUserId()!.Value, cancellationToken);
            return Results.NoContent();
        }).RequireSuperAdmin();

        group.MapPost("/{id:guid}/delete", async (Guid id, OrganizationLifecycleService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            await service.SoftDeleteAsync(id, httpContext.User.GetUserId()!.Value, cancellationToken);
            return Results.NoContent();
        }).RequireSuperAdmin();

        group.MapPost("/{id:guid}/restore", async (Guid id, OrganizationLifecycleService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            await service.RestoreAsync(id, httpContext.User.GetUserId()!.Value, cancellationToken);
            return Results.NoContent();
        }).RequireSuperAdmin();
    }
}
