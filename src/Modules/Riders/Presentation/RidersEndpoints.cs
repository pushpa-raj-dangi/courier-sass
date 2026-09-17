using Courier.Cod.Application;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Riders.Presentation;

public static class RidersEndpoints
{
    // Allowlisted content types for POD signature/photo uploads — same rationale and limit as
    // Merchants' KYC upload (Phase 4): a scanned signature or a delivery photo is typically JPEG/
    // PNG (signatures may also be uploaded as PDF from some capture devices).
    private static readonly string[] AllowedContentTypes = ["application/pdf", "image/jpeg", "image/png"];
    private const long MaxUploadSizeBytes = 10 * 1024 * 1024;

    public static void MapRidersEndpoints(this IEndpointRouteBuilder app)
    {
        var riders = app.MapGroup("/api/v1/riders").RequireAuthorization()
        .WithTags("Rider Management");
        var vehicles = app.MapGroup("/api/v1/vehicles")
        .WithTags("Vehicles Management")
        .RequireAuthorization();

        riders.MapPost("/", async (CreateRiderRequest request, RiderService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CreateRiderAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/riders/{result.Id}", result);
            }
            catch (RiderValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "RIDER_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("rider.create");

        riders.MapGet("/", async (RiderService service, HttpContext httpContext) =>
            Results.Ok(await service.ListAsync(httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("rider.read");

        riders.MapGet("/summary", async (IRiderReportingLookupService lookupService, IBranchAuthorizationChecker branchAuthorizationChecker, HttpContext httpContext) =>
        {
            var accessibleBranchIds = await branchAuthorizationChecker.GetAccessibleBranchIdsAsync(httpContext.User, "rider.read", httpContext.RequestAborted);
            var summary = await lookupService.GetSummaryAsync(accessibleBranchIds, httpContext.RequestAborted);
            return Results.Ok(summary);
        }).RequirePermission("rider.read");

        riders.MapGet("/me", async (RiderService service, HttpContext httpContext) =>
        {
            var result = await service.GetMyRiderAsync(httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        riders.MapGet("/me/tasks/today", async (RiderService service, HttpContext httpContext) =>
            Results.Ok(await service.GetMyTasksTodayAsync(httpContext.User, httpContext.RequestAborted)));

        riders.MapPost("/me/location", async (LocationPingRequest request, RiderService service, HttpContext httpContext) =>
        {
            try
            {
                var recorded = await service.RecordLocationPingAsync(request, httpContext.User, httpContext.RequestAborted);
                return recorded ? Results.NoContent() : Results.NotFound();
            }
            catch (RiderValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "RIDER_VALIDATION_FAILED", message = ex.Message } });
            }
        });

        riders.MapGet("/branches/{branchId:guid}/locations", async (Guid branchId, RiderService service, HttpContext httpContext) =>
            await HandleAsync(() => service.GetBranchLocationsAsync(branchId, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("rider.location.read");

        // Wrapped in HandleAsync (same as GetBranchLocationsAsync's mapping above) so a
        // RiderValidationException thrown by CheckBranchAccessAsync inside GetRunBreadcrumbsAsync
        // (a branch-scoped caller denied access to run.BranchId) produces the standard 400
        // RIDER_VALIDATION_FAILED shape instead of an uncaught 500. The "run not found"/
        // "cross-tenant run" null case is preserved as 404 by translating it into
        // RiderNotFoundException, which HandleAsync's existing catch maps to Results.NotFound().
        riders.MapGet("/runs/{runId:guid}/breadcrumbs", async (Guid runId, RiderService service, HttpContext httpContext) =>
            await HandleAsync(async () =>
                await service.GetRunBreadcrumbsAsync(runId, httpContext.User, httpContext.RequestAborted)
                ?? throw new RiderNotFoundException(runId)))
            .RequirePermission("rider.location.read");

        riders.MapGet("/{id:guid}", async (Guid id, RiderService service, HttpContext httpContext) =>
        {
            var result = await service.GetByIdAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("rider.read");

        vehicles.MapPost("/", async (CreateVehicleRequest request, RiderService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CreateVehicleAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/vehicles/{result.Id}", result);
            }
            catch (RiderValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "RIDER_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("rider.create");

        vehicles.MapGet("/", async (RiderService service, HttpContext httpContext) =>
            Results.Ok(await service.ListVehiclesAsync(httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("rider.read");

        riders.MapPost("/tasks", async (AssignDeliveryTaskRequest request, RiderService service, HttpContext httpContext) =>
            await HandleAsync(() => service.AssignTaskAsync(request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("delivery.assign");

        riders.MapPost("/{id:guid}/scan/pickup", async (Guid id, RiderScanRequest request, RiderService service, HttpContext httpContext) =>
            await HandleAsync(() => service.RiderPickupScanAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("shipment.scan");

        riders.MapPost("/{id:guid}/delivery-attempts/fail", async (Guid id, RecordFailedAttemptRequest request, RiderService service, HttpContext httpContext) =>
            await HandleAsync(() => service.RecordFailedAttemptAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("delivery.attempt");

        // Raw request body upload (not multipart/form-data) — same shape as
        // Merchants' KYC document upload (Phase 4): kind on the query string, bytes as the body.
        // Called once per file (signature, then separately photo) before delivery-attempts/complete,
        // whose request body references the returned StorageKey.
        riders.MapPost("/{id:guid}/pod-uploads", async (Guid id, PodFileKind kind, HttpContext httpContext, RiderService service) =>
        {
            var contentType = httpContext.Request.ContentType;
            if (contentType is null || !AllowedContentTypes.Any(allowed => contentType.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.BadRequest(new { error = new { code = "UNSUPPORTED_CONTENT_TYPE", message = "Only application/pdf, image/jpeg, and image/png are supported." } });
            }
            if (httpContext.Request.ContentLength is long contentLength && contentLength > MaxUploadSizeBytes)
            {
                return Results.BadRequest(new { error = new { code = "FILE_TOO_LARGE", message = "POD files must be 10MB or smaller." } });
            }

            try
            {
                var result = await service.UploadPodFileAsync(id, kind, httpContext.Request.Body, contentType, httpContext.User, httpContext.RequestAborted);
                return Results.Ok(result);
            }
            catch (RiderNotFoundException)
            {
                return Results.NotFound();
            }
        }).RequirePermission("delivery.complete");

        riders.MapPost("/{id:guid}/delivery-attempts/complete", async (Guid id, CompleteDeliveryRequest request, RiderService service, HttpContext httpContext) =>
            await HandleAsync(() => service.CompleteDeliveryAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("delivery.complete");
    }

    private static async Task<IResult> HandleAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (RiderNotFoundException)
        {
            return Results.NotFound();
        }
        catch (RiderValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "RIDER_VALIDATION_FAILED", message = ex.Message } });
        }
        catch (ShipmentInvalidTransitionException ex)
        {
            return Results.BadRequest(new
            {
                error = new
                {
                    code = "SHIPMENT_INVALID_TRANSITION",
                    message = ex.Message,
                    details = new { shipmentId = ex.ShipmentId, from = ex.From.ToString(), to = ex.To.ToString() }
                }
            });
        }
        catch (ShipmentValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "SHIPMENT_VALIDATION_FAILED", message = ex.Message } });
        }
        catch (CodValidationException ex)
        {
            // The shipment (and its DeliveryProof/DeliveryAttempt) is already committed as Delivered
            // by the time CompleteDeliveryAsync calls ICodCollectionService.MarkCollectedAsync — this
            // is a known, deliberately-deferred design gap (not fixed here), but a COD failure at
            // that point must still surface as an actionable 400, not an opaque 500.
            return Results.BadRequest(new { error = new { code = "COD_VALIDATION_FAILED", message = ex.Message } });
        }
    }
}
