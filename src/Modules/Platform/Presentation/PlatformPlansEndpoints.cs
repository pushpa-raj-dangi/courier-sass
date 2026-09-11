using Courier.Billing.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

// Plan CRUD is Platform's own admin surface over a module it already fully references (Billing),
// the same way Platform calls OrganizationLifecycleService's own concrete methods directly rather
// than hiding them behind an interface nothing else needs — see IBillingLookupService for the
// narrow, read-only interface that exists specifically for OTHER modules' cross-module lookups.
public static class PlatformPlansEndpoints
{
    public static void MapPlatformPlansEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/plans").WithTags("Platform Plans");

        group.MapGet("/", async (string? search, int? page, int? pageSize, PlanService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListPlansAsync(search, page, pageSize, cancellationToken)))
            .RequireSuperAdmin();

        group.MapPost("/", async (CreatePlanRequest request, PlanService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.CreatePlanAsync(request, httpContext.User.GetUserId()!.Value, cancellationToken);
                return Results.Created($"/api/v1/platform/plans/{result.Id}", result);
            }
            catch (BillingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BILLING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();

        group.MapPost("/{id:guid}/retire", async (Guid id, PlanService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                await service.RetirePlanAsync(id, httpContext.User.GetUserId()!.Value, cancellationToken);
                return Results.NoContent();
            }
            catch (BillingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BILLING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();
    }
}
