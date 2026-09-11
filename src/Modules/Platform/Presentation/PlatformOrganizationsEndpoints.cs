using Courier.Billing.Application;
using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public record AssignOrganizationPlanRequest(Guid PlanId);

public static class PlatformOrganizationsEndpoints
{
    public static void MapPlatformOrganizationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/organizations").WithTags("Platform Organizations");

        group.MapGet("/", async (string? search, bool? isActive, bool? isDeleted, int? page, int? pageSize, OrganizationLifecycleService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListOrganizationsAsync(search, isActive, isDeleted, page, pageSize, cancellationToken)))
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

        // Platform already fully references Billing directly (see PlatformPlansEndpoints's own
        // comment) — no adapter/interface needed here, unlike Identity's registration-time wiring.
        // This is what fixes an already-registered organization sitting with no Subscription at
        // all (pre-dating this feature) or one whose trial expired without converting.
        group.MapPost("/{id:guid}/plan", async (Guid id, AssignOrganizationPlanRequest request, SubscriptionService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.AssignPlanAsync(id, request.PlanId, httpContext.User.GetUserId()!.Value, cancellationToken);
                return Results.Ok(result);
            }
            catch (BillingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BILLING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();
    }
}
