using Courier.Pricing.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Pricing.Presentation;

public static class PricingEndpoints
{
    public static void MapPricingEndpoints(this IEndpointRouteBuilder app)
    {
        var referenceGroup = app.MapGroup("/api/v1");

        referenceGroup.MapGet("/service-types", async (ReferenceDataService referenceDataService, CancellationToken cancellationToken) =>
            Results.Ok(await referenceDataService.ListServiceTypesAsync(cancellationToken))).RequirePermission("pricing.read");

        referenceGroup.MapGet("/weight-slabs", async (ReferenceDataService referenceDataService, CancellationToken cancellationToken) =>
            Results.Ok(await referenceDataService.ListWeightSlabsAsync(cancellationToken))).RequirePermission("pricing.read");

        var rateCardsGroup = app.MapGroup("/api/v1/rate-cards");

        rateCardsGroup.MapGet("/", async (RateCardService rateCardService, CancellationToken cancellationToken) =>
            Results.Ok(await rateCardService.ListRateCardsAsync(cancellationToken))).RequirePermission("pricing.read");

        rateCardsGroup.MapGet("/{id:guid}", async (Guid id, RateCardService rateCardService, CancellationToken cancellationToken) =>
        {
            var rateCard = await rateCardService.GetRateCardAsync(id, cancellationToken);
            return rateCard is null ? Results.NotFound() : Results.Ok(rateCard);
        }).RequirePermission("pricing.read");

        rateCardsGroup.MapPost("/", async (CreateRateCardRequest request, RateCardService rateCardService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await rateCardService.CreateRateCardAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/rate-cards/{result.Id}", result);
            }
            catch (PricingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "PRICING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("pricing.manage");

        rateCardsGroup.MapGet("/{id:guid}/zone-rates", async (Guid id, RateCardService rateCardService, CancellationToken cancellationToken) =>
            Results.Ok(await rateCardService.ListZoneRatesAsync(id, cancellationToken))).RequirePermission("pricing.read");

        rateCardsGroup.MapPost("/{id:guid}/zone-rates", async (Guid id, CreateZoneRateRequest request, RateCardService rateCardService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await rateCardService.AddZoneRateAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/rate-cards/{id}/zone-rates/{result.Id}", result);
            }
            catch (PricingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "PRICING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("pricing.manage");

        rateCardsGroup.MapGet("/{id:guid}/additional-charges", async (Guid id, RateCardService rateCardService, CancellationToken cancellationToken) =>
            Results.Ok(await rateCardService.ListAdditionalChargesAsync(id, cancellationToken))).RequirePermission("pricing.read");

        rateCardsGroup.MapPost("/{id:guid}/additional-charges", async (Guid id, CreateAdditionalChargeRequest request, RateCardService rateCardService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await rateCardService.AddAdditionalChargeAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/rate-cards/{id}/additional-charges/{result.Id}", result);
            }
            catch (PricingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "PRICING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("pricing.manage");

        var merchantRateCardGroup = app.MapGroup("/api/v1/merchants");

        merchantRateCardGroup.MapGet("/{merchantId:guid}/rate-card", async (Guid merchantId, RateCardService rateCardService, CancellationToken cancellationToken) =>
        {
            var merchantRateCard = await rateCardService.GetMerchantRateCardAsync(merchantId, cancellationToken);
            return merchantRateCard is null ? Results.NotFound() : Results.Ok(merchantRateCard);
        }).RequirePermission("pricing.read");

        merchantRateCardGroup.MapPut("/{merchantId:guid}/rate-card", async (Guid merchantId, UpsertMerchantRateCardRequest request, RateCardService rateCardService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await rateCardService.UpsertMerchantRateCardAsync(merchantId, request, httpContext.User, httpContext.RequestAborted);
                return Results.Ok(result);
            }
            catch (PricingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "PRICING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("pricing.manage");

        var quoteGroup = app.MapGroup("/api/v1/pricing");

        quoteGroup.MapPost("/quote", async (PriceQuoteRequest request, PricingService pricingService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await pricingService.QuoteAsync(request, httpContext.RequestAborted);
                return Results.Ok(result);
            }
            catch (PricingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "PRICING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("pricing.read");
    }
}
