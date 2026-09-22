using Courier.Geography.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Geography.Presentation;

public static class PlatformCountryZonesEndpoints
{
    public static void MapPlatformCountryZonesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/country-zones").WithTags("Platform Country Zones");

        group.MapGet("/", async (CountryZoneService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)))
            .RequireSuperAdmin();

        group.MapPost("/", async (CreateCountryZoneRequest request, CountryZoneService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.CreateAsync(request, httpContext.User.GetUserId()!.Value, cancellationToken);
                return Results.Created($"/api/v1/platform/country-zones/{result.Id}", result);
            }
            catch (CountryZoneValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "COUNTRY_ZONE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();

        group.MapPatch("/{id:guid}", async (Guid id, UpdateCountryZoneRequest request, CountryZoneService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.UpdateAsync(id, request, httpContext.User.GetUserId()!.Value, cancellationToken);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (CountryZoneValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "COUNTRY_ZONE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();
    }
}
