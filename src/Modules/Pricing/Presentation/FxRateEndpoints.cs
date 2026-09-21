using Courier.Pricing.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Pricing.Presentation;

public static class FxRateEndpoints
{
    public static void MapFxRateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/fx-rates").WithTags("FX Rates");

        group.MapGet("/", async (string? from, string? to, int? page, int? pageSize, FxRateService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(from, to, page, pageSize, cancellationToken)))
            .RequireSuperAdmin();

        group.MapPost("/", async (RecordFxRateRequest request, FxRateService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await service.RecordAsync(request, httpContext.User.GetUserId()!.Value, cancellationToken);
                return Results.Created($"/api/v1/platform/fx-rates?from={created.FromCurrency}&to={created.ToCurrency}", created);
            }
            catch (FxRateValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "FX_RATE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();
    }
}
