using Courier.International.Application;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.International.Presentation;

public static class InternationalEndpoints
{
    public static void MapInternationalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/international/shipments")
            .WithTags("International Shipments")
            .RequireAuthorization();

        group.MapPost("/", async (CreateInternationalShipmentRequest request, InternationalShipmentService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CreateAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/international/shipments/{result.Shipment.Id}", result);
            }
            catch (InternationalShipmentValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "INTERNATIONAL_SHIPMENT_VALIDATION_FAILED", message = ex.Message } });
            }
            catch (ShipmentValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "SHIPMENT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("international.create");

        group.MapGet("/{id:guid}", async (Guid id, InternationalShipmentQueryService queryService, HttpContext httpContext) =>
        {
            var result = await queryService.GetAsync(id, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("international.read");

        group.MapPatch("/{id:guid}/customs", async (Guid id, UpdateCustomsRequest request, InternationalShipmentService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.UpdateCustomsAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (InternationalShipmentValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "INTERNATIONAL_SHIPMENT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("international.manage_customs");

        group.MapPost("/{id:guid}/prepare-customs", async (Guid id, InternationalShipmentService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.PrepareCustomsAsync(id, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (InternationalShipmentValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "INTERNATIONAL_SHIPMENT_VALIDATION_FAILED", message = ex.Message } });
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
        }).RequirePermission("international.manage_customs");

        group.MapPost("/{id:guid}/book", (Guid id, BookCarrierRequest request, CarrierBookingService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.BookAsync(id, request, httpContext.Request.Headers["Idempotency-Key"].ToString(), httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.book");

        group.MapPost("/{id:guid}/booking/cancel", (Guid id, CarrierBookingService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.CancelAsync(id, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.book");

        group.MapPost("/{id:guid}/inbound-booking", (Guid id, LinkInboundBookingRequest request, CarrierBookingService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.LinkInboundAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.book");

        group.MapPost("/{id:guid}/carrier-events", (Guid id, ManualCarrierEventRequest request, ManualCarrierEventService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.RecordAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Accepted($"/api/v1/international/shipments/{id}", result);
            })).RequirePermission("international.tracking.manage");

        group.MapPost("/{id:guid}/handoff", (Guid id, InternationalCarrierLifecycleService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.HandoffAsync(id, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.handoff");

        group.MapPost("/{id:guid}/receive", (Guid id, InternationalCarrierLifecycleService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.ReceiveAsync(id, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.receive");

        group.MapPost("/{id:guid}/status-override", (Guid id, StatusOverrideRequest request, InternationalCarrierLifecycleService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.OverrideStatusAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.tracking.manage");

        group.MapGet("/{id:guid}/booking/label", async (Guid id, CarrierBookingService service, HttpContext httpContext) =>
        {
            var label = await service.GetLabelAsync(id, httpContext.User, httpContext.RequestAborted);
            return label is null ? Results.NotFound() : Results.File(label.Value.Content, label.Value.ContentType);
        }).RequirePermission("international.read");
    }
}
