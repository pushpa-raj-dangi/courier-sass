using Courier.Geography.Infrastructure;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Geography.Presentation;

public static class GeographyEndpoints
{
    public static void MapGeographyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/geography");

        group.MapGet("/provinces", async (GeographyQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListProvincesAsync(cancellationToken))).RequirePermission("geography.read");

        group.MapGet("/districts", async (Guid provinceId, GeographyQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListDistrictsAsync(provinceId, cancellationToken))).RequirePermission("geography.read");

        group.MapGet("/local-levels", async (Guid districtId, GeographyQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListLocalLevelsAsync(districtId, cancellationToken))).RequirePermission("geography.read");

        group.MapGet("/wards", async (Guid localLevelId, GeographyQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListWardsAsync(localLevelId, cancellationToken))).RequirePermission("geography.read");

        group.MapGet("/areas", async (Guid wardId, GeographyQueryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAreasAsync(wardId, cancellationToken))).RequirePermission("geography.read");
    }
}
