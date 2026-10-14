using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Merchants.Application;
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

        group.MapGet("/collections", async (CodCollectionStatus? status, Guid? branchId, Guid? merchantId, Guid? riderCashSettlementId, int? page, int? pageSize, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListCollectionsAsync(status, branchId, merchantId, riderCashSettlementId, page, pageSize, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");

        group.MapGet("/summary", async (ICodReportingLookupService lookupService, IBranchAuthorizationChecker branchAuthorizationChecker, HttpContext httpContext) =>
        {
            var accessibleBranchIds = await branchAuthorizationChecker.GetAccessibleBranchIdsAsync(httpContext.User, "cod.read", httpContext.RequestAborted);
            var summary = await lookupService.GetOrgFinancialSummaryAsync(accessibleBranchIds, httpContext.RequestAborted);
            return Results.Ok(summary);
        }).RequirePermission("cod.read");

        group.MapGet("/collections/{id:guid}", async (Guid id, CodService service, HttpContext httpContext) =>
        {
            var result = await service.GetCollectionByIdAsync(id, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("cod.read");

        group.MapGet("/reconciliation", async (Guid? branchId, Guid? merchantId, DateTimeOffset? from, DateTimeOffset? to, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.GetReconciliationSummaryAsync(branchId, merchantId, from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");

        // F15 R3a staff-only reads — merchant callers are rejected outright (403), never scoped.
        group.MapGet("/flow", async (Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, CodService service, HttpContext httpContext) =>
        {
            if (httpContext.User.GetMerchantId() is not null)
            {
                return Results.Forbid();
            }
            return await HandleAsync(() => service.GetFlowAsync(branchId, from, to, httpContext.User, httpContext.RequestAborted));
        }).RequirePermission("cod.read");

        group.MapGet("/rider-cash", async (Guid? branchId, CodService service, HttpContext httpContext) =>
        {
            if (httpContext.User.GetMerchantId() is not null)
            {
                return Results.Forbid();
            }
            return await HandleAsync(() => service.GetRiderCashAsync(branchId, httpContext.User, httpContext.RequestAborted));
        }).RequirePermission("cod.read");

        group.MapGet("/rider-settlements", async (Guid? branchId, Guid? riderId, bool? unreconciledOnly, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.ListRiderSettlementsAsync(branchId, riderId, unreconciledOnly, from, to, page, pageSize, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");

        group.MapPost("/rider-settlements", async (CreateRiderSettlementRequest request, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.CreateRiderSettlementAsync(request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.reconcile");

        group.MapGet("/branch-settlements", async (Guid? branchId, bool? unverifiedOnly, int? page, int? pageSize, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListBranchSettlementsAsync(branchId, unverifiedOnly, page, pageSize, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");

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

        group.MapGet("/adjustments", async (bool? applied, int? page, int? pageSize, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListAdjustmentsAsync(applied, page, pageSize, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");

        group.MapPost("/adjustments", async (RequestAdjustmentRequest request, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.RequestAdjustmentAsync(request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.adjust");

        group.MapPost("/adjustments/{id:guid}/approve", async (Guid id, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.ApproveAdjustmentAsync(id, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.adjust");

        // Additive merchant-facing read path — the existing /merchant-settlements routes above
        // stay staff-only and untouched. Reuses the exact same CodService methods, which already
        // self-scope a merchant-JWT-or-API-key caller to GetMerchantId() (confirmed in Task 11's
        // exploration) — no service change needed, only these new route registrations.
        var merchantGroup = app.MapGroup("/api/v1/cod/settlements")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Bearer, AuthSchemes.ApiKey)
                .RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true))
            .RequireApiKeyRateLimit()
            .WithTags("COD Management");

        merchantGroup.MapGet("/", async (CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListMerchantSettlementsAsync(null, httpContext.User, httpContext.RequestAborted)))
            .RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.CodSettlementsRead);

        merchantGroup.MapGet("/{id:guid}", async (Guid id, CodService service, HttpContext httpContext) =>
        {
            var result = await service.GetMerchantSettlementByIdAsync(id, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.CodSettlementsRead);
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
