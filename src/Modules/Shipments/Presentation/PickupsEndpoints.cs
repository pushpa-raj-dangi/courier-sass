using Courier.Merchants.Application;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Shipments.Presentation;

public static class PickupsEndpoints
{
    public static void MapPickupsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pickups")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Bearer, AuthSchemes.ApiKey)
                .RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true))
            .RequireApiKeyRateLimit()
            .WithTags("Pickups");

        group.MapPost("/", async (CreatePickupRequestRequest request, PickupRequestService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CreatePickupRequestAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/pickups/{result.Id}", result);
            }
            catch (ShipmentValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "SHIPMENT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.PickupsCreate);

        // Dual-purpose read, same manual-branching reasoning as ShipmentsEndpoints'
        // HasReadAccessAsync: merchant-track callers (PickupsRead) see their own requests; staff
        // callers (pickup.read) see requests for their accessible branches. No existing filter
        // covers "either token kind, different permission string per kind."
        group.MapGet("/", async (PickupRequestService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (httpContext.User.GetMerchantId() is not null)
            {
                if (!await merchantAuthorizationChecker.HasPermissionAsync(httpContext.User, MerchantPermissionCatalog.PickupsRead, httpContext.RequestAborted))
                {
                    return Results.Forbid();
                }
            }
            else if (httpContext.User.GetUserId() is not null)
            {
                if (!await permissionChecker.HasPermissionAsync(httpContext.User, "pickup.read", httpContext.RequestAborted))
                {
                    return Results.Forbid();
                }
            }
            else
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await service.ListAsync(httpContext.User, httpContext.RequestAborted));
        });

        group.MapPost("/{id:guid}/cancel", async (Guid id, PickupRequestService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CancelAsync(id, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (ShipmentValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "SHIPMENT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.PickupsCancel);
    }
}
