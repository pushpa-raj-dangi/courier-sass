using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Merchants.Presentation;

public static class MerchantsEndpoints
{
    public static void MapMerchantsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchants");

        group.MapPost("/", async (CreateMerchantRequest request, MerchantOnboardingService onboardingService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await onboardingService.CreateMerchantAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/merchants/{result.Id}", result);
            }
            catch (MerchantValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "MERCHANT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("merchant.create");

        group.MapGet("/", async (MerchantOnboardingService onboardingService, CancellationToken cancellationToken) =>
            Results.Ok(await onboardingService.ListMerchantsAsync(cancellationToken))).RequirePermission("merchant.read");

        group.MapGet("/{id:guid}", async (Guid id, MerchantOnboardingService onboardingService, CancellationToken cancellationToken) =>
        {
            var merchant = await onboardingService.GetMerchantAsync(id, cancellationToken);
            return merchant is null ? Results.NotFound() : Results.Ok(merchant);
        }).RequirePermission("merchant.read");

        group.MapPost("/{merchantId:guid}/kyc-documents/{documentId:guid}/review", async (
            Guid merchantId, Guid documentId, ReviewKycDocumentRequest request, MerchantKycService kycService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await kycService.ReviewDocumentAsync(merchantId, documentId, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (MerchantValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "MERCHANT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("merchant.kyc.review");
    }
}
