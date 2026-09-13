using Courier.Branches.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Branches.Presentation;

public static class BranchesEndpoints
{
    public static void MapBranchesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/branches").WithTags("Branch Management");

        group.MapPost("/", async (CreateBranchRequest request, BranchService branchService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await branchService.CreateBranchAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/branches/{result.Id}", result);
            }
            catch (BranchLimitExceededException ex)
            {
                return Results.BadRequest(new { error = new { code = "BRANCH_LIMIT_REACHED", message = ex.Message } });
            }
            catch (BranchValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BRANCH_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("branch.create");

        group.MapGet("/", async (BranchService branchService, HttpContext httpContext) =>
        {
            var branches = await branchService.ListBranchesAsync(httpContext.User, httpContext.RequestAborted);
            return Results.Ok(branches);
        }).RequirePermission("branch.read");

        group.MapGet("/summary", async (BranchService branchService, HttpContext httpContext) =>
        {
            var summary = await branchService.GetSummaryAsync(httpContext.User, httpContext.RequestAborted);
            return Results.Ok(summary);
        }).RequirePermission("branch.read");

        // Branch-scoped, not just permission-scoped: RequireBranchScopedPermission checks both
        // "does this user hold branch.read" and "is {id} within this user's branch grants" in
        // one filter, so no separate .RequirePermission(...) is chained here.
        group.MapGet("/{id:guid}", async (Guid id, BranchService branchService, CancellationToken cancellationToken) =>
        {
            var branch = await branchService.GetBranchAsync(id, cancellationToken);
            return branch is null ? Results.NotFound() : Results.Ok(branch);
        }).RequireBranchScopedPermission("branch.read");

        group.MapPut("/{id:guid}/delivery-zone", async (Guid id, AssignDeliveryZoneRequest request, BranchService branchService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await branchService.AssignDeliveryZoneAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (BranchValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BRANCH_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireBranchScopedPermission("branch.update");
    }
}
