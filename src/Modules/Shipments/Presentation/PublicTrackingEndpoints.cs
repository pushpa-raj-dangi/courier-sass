using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Shipments.Presentation;

public static class PublicTrackingEndpoints
{
    public static void MapPublicTrackingEndpoints(this IEndpointRouteBuilder app)
    {
        // No RequireAuthorization anywhere on this route — genuinely anonymous, per CLAUDE.md §24.
        app.MapGet("/track/{awb}", async (string awb, PublicTrackingService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetByAwbAsync(awb, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .RequireIpRateLimit()
        .WithTags("Public Tracking");
    }
}
