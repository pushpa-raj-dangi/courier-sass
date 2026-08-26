using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Cod.Presentation;

public static class CodEndpoints
{
    public static void MapCodEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/cod")
        .WithTags("COD Management")
        .RequireAuthorization();

        group.MapGet("/collections", async (CodCollectionStatus? status, Guid? branchId, Guid? merchantId, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListCollectionsAsync(status, branchId, merchantId, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");

        group.MapGet("/collections/{id:guid}", async (Guid id, CodService service, HttpContext httpContext) =>
        {
            var result = await service.GetCollectionByIdAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("cod.read");

        group.MapGet("/reconciliation", async (Guid? branchId, Guid? merchantId, DateTimeOffset? from, DateTimeOffset? to, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.GetReconciliationSummaryAsync(branchId, merchantId, from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");

        group.MapPost("/rider-settlements", async (CreateRiderSettlementRequest request, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.CreateRiderSettlementAsync(request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.reconcile");

        group.MapPost("/branch-settlements", async (CreateBranchSettlementRequest request, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.CreateBranchSettlementAsync(request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.reconcile");

        group.MapPost("/finance-verifications", async (CreateFinanceVerificationRequest request, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.CreateFinanceVerificationAsync(request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.settle");

        group.MapPost("/merchant-settlements", async (CreateMerchantSettlementRequest request, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.CreateMerchantSettlementAsync(request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.settle");

        group.MapGet("/merchant-settlements", async (Guid? merchantId, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListMerchantSettlementsAsync(merchantId, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");

        group.MapGet("/merchant-settlements/{id:guid}", async (Guid id, CodService service, HttpContext httpContext) =>
        {
            var result = await service.GetMerchantSettlementByIdAsync(id, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("cod.read");

        group.MapPost("/merchant-settlements/{id:guid}/settle", async (Guid id, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.SettleMerchantSettlementAsync(id, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.settle");

        group.MapPost("/adjustments", async (RequestAdjustmentRequest request, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.RequestAdjustmentAsync(request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.adjust");

        group.MapPost("/adjustments/{id:guid}/approve", async (Guid id, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.ApproveAdjustmentAsync(id, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.adjust");
    }

    private static async Task<IResult> HandleAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            var result = await operation();
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (CodValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "COD_VALIDATION_FAILED", message = ex.Message } });
        }
    }
}
