using Courier.Geography.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Geography.Presentation;

public static class DeliveryZonesEndpoints
{
    public static void MapDeliveryZonesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/delivery-zones");

        group.MapPost("/", async (CreateDeliveryZoneRequest request, DeliveryZoneService zoneService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await zoneService.CreateAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/delivery-zones/{result.Id}", result);
            }
            catch (DeliveryZoneValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "DELIVERY_ZONE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("zone.create");

        group.MapGet("/", async (DeliveryZoneService zoneService, CancellationToken cancellationToken) =>
            Results.Ok(await zoneService.ListAsync(cancellationToken))).RequirePermission("zone.read");

        group.MapGet("/{id:guid}", async (Guid id, DeliveryZoneService zoneService, CancellationToken cancellationToken) =>
        {
            var zone = await zoneService.GetAsync(id, cancellationToken);
            return zone is null ? Results.NotFound() : Results.Ok(zone);
        }).RequirePermission("zone.read");

        group.MapPut("/{id:guid}", async (Guid id, UpdateDeliveryZoneRequest request, DeliveryZoneService zoneService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await zoneService.UpdateAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (DeliveryZoneValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "DELIVERY_ZONE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("zone.update");
    }
}
