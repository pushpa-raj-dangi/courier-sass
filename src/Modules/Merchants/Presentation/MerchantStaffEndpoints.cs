using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Merchants.Presentation;

public static class MerchantStaffEndpoints
{
    public static void MapMerchantStaffEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchant/staff").WithTags("Merchant Staff Management");

        group.MapPost("/", async (InviteMerchantStaffRequest request, MerchantStaffService staffService, HttpContext httpContext) =>
        {
            try
            {
                var result = await staffService.InviteStaffAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/merchant/staff/{result.Id}", result);
            }
            catch (MerchantValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "MERCHANT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireMerchantPermission("merchant.staff.manage");

        group.MapGet("/", async (MerchantStaffService staffService, HttpContext httpContext, CancellationToken cancellationToken) =>
            Results.Ok(await staffService.ListStaffAsync(httpContext.User, cancellationToken))).RequireMerchantPermission("merchant.staff.manage");
    }
}
