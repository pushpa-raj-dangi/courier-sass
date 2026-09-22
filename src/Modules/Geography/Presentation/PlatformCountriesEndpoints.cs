using Courier.Geography.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Geography.Presentation;

public static class PlatformCountriesEndpoints
{
    public static void MapPlatformCountriesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/countries").WithTags("Platform Countries");

        group.MapGet("/", async (string? search, int? page, int? pageSize, CountryQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListForAdminAsync(search, page, pageSize, cancellationToken)))
            .RequireSuperAdmin();

        group.MapPatch("/{id:guid}", async (Guid id, UpdateCountryRequest request, CountryAdminService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.UpdateAsync(id, request, httpContext.User.GetUserId()!.Value, cancellationToken);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (CountryValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "COUNTRY_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();
    }
}
