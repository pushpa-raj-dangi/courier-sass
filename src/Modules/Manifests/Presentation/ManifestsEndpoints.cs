using Courier.Manifests.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Manifests.Presentation;

public static class ManifestsEndpoints
{
    public static void MapManifestsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/manifests")
        .WithTags("Manifests Management")
        .RequireAuthorization();

        group.MapPost("/", async (CreateManifestRequest request, ManifestService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CreateManifestAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/manifests/{result.Id}", result);
            }
            catch (ManifestValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "MANIFEST_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("manifest.create");

        group.MapGet("/{id:guid}", async (Guid id, ManifestService service, HttpContext httpContext) =>
        {
            var result = await service.GetByIdAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("manifest.read");

        group.MapGet("/{id:guid}/missing-shipments", async (Guid id, ManifestService service, HttpContext httpContext) =>
        {
            var result = await service.GetMissingShipmentsAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("manifest.read");

        group.MapPost("/{id:guid}/scan", async (Guid id, ScanShipmentRequest request, ManifestService service, HttpContext httpContext) =>
            await HandleScanAsync(() => service.ScanAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("manifest.create");

        group.MapPost("/{id:guid}/rto-scan", async (Guid id, ScanShipmentRequest request, ManifestService service, HttpContext httpContext) =>
            await HandleScanAsync(() => service.ScanRtoAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("manifest.create");

        group.MapPost("/{id:guid}/seal", async (Guid id, ManifestService service, HttpContext httpContext) =>
            await HandleManifestAsync(() => service.SealAsync(id, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("manifest.seal");

        group.MapPost("/{id:guid}/dispatch", async (Guid id, DispatchManifestRequest request, ManifestService service, HttpContext httpContext) =>
            await HandleManifestAsync(() => service.DispatchAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("manifest.dispatch");

        group.MapPost("/{id:guid}/receive-scan", async (Guid id, ScanShipmentRequest request, ManifestService service, HttpContext httpContext) =>
            await HandleScanAsync(() => service.ReceiveScanAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("manifest.receive");

        group.MapPost("/{id:guid}/close", async (Guid id, ManifestService service, HttpContext httpContext) =>
            await HandleManifestAsync(() => service.CloseAsync(id, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("manifest.close");
    }

    private static async Task<IResult> HandleManifestAsync(Func<Task<ManifestResponse>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (ManifestNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ManifestValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "MANIFEST_VALIDATION_FAILED", message = ex.Message } });
        }
    }

    private static async Task<IResult> HandleScanAsync(Func<Task<ManifestShipmentResponse>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (ManifestNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ManifestScanRejectedException ex)
        {
            return Results.BadRequest(new { error = new { code = "MANIFEST_SCAN_REJECTED", reason = ex.ReasonCode, message = ex.Message } });
        }
        catch (ManifestValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "MANIFEST_VALIDATION_FAILED", message = ex.Message } });
        }
    }
}
