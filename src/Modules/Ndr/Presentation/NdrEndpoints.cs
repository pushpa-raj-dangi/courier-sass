using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Ndr.Presentation;

public static class NdrEndpoints
{
    public static void MapNdrEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ndrs")
        .WithTags("NDRS Management")
        .RequireAuthorization();

        group.MapGet("/", async (NdrStatus? status, int? page, int? pageSize, NdrService service, HttpContext httpContext) =>
            Results.Ok(await service.ListAsync(status, page, pageSize, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("ndr.read");

        group.MapGet("/{id:guid}", async (Guid id, NdrService service, HttpContext httpContext) =>
        {
            var result = await service.GetByIdAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("ndr.read");

        group.MapPost("/{id:guid}/update", async (Guid id, UpdateNdrRequest request, NdrService service, HttpContext httpContext) =>
            await HandleAsync(() => service.UpdateAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("ndr.manage");

        group.MapPost("/{id:guid}/resolve", async (Guid id, ResolveNdrRequest request, NdrService service, HttpContext httpContext) =>
            await HandleAsync(() => service.ResolveAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("ndr.resolve");

        var returnShipments = app.MapGroup("/api/v1/return-shipments")
        .WithTags("Shipment Management")
        .RequireAuthorization();

        returnShipments.MapGet("/", async (NdrService service, HttpContext httpContext) =>
            Results.Ok(await service.ListReturnShipmentsAsync(httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("rto.read");

        returnShipments.MapGet("/{id:guid}", async (Guid id, NdrService service, HttpContext httpContext) =>
        {
            var result = await service.GetReturnShipmentByIdAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("rto.read");

        returnShipments.MapPost("/{id:guid}/confirm", async (Guid id, NdrService service, HttpContext httpContext) =>
            await HandleAsync(() => service.ConfirmReturnShipmentAsync(id, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("rto.manage");
    }

    private static async Task<IResult> HandleAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            var result = await operation();
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (NdrValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "NDR_VALIDATION_FAILED", message = ex.Message } });
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
    }
}
