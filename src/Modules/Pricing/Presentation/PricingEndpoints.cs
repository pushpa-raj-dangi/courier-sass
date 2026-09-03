using Courier.Merchants.Application;
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
        // Dual-auth for the two routes a merchant caller needs to build a booking-form quote
        // preview (service-types below, and /quote further down) — staff via "pricing.read", or
        // merchant JWT/API-key via MerchantPermissionCatalog.PricingRead. Same manual
        // per-credential-type branching ShipmentsEndpoints already establishes, since no
        // existing filter covers "either token kind, different permission string per kind."
        var referenceGroup = app.MapGroup("/api/v1")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Bearer, AuthSchemes.ApiKey)
                .RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true))
            .RequireApiKeyRateLimit();

        // ServiceType has no OrganizationId at all (global reference data, confirmed in
        // ReferenceDataService.ListServiceTypesAsync) — no tenant-isolation risk in exposing it
        // further, only an authorization-scheme change.
        referenceGroup.MapGet("/service-types", async (ReferenceDataService referenceDataService, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker, CancellationToken cancellationToken) =>
        {
            if (!await HasPricingReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker))
            {
                return httpContext.User.GetMerchantId() is null && httpContext.User.GetUserId() is null ? Results.Unauthorized() : Results.Forbid();
            }
            return Results.Ok(await referenceDataService.ListServiceTypesAsync(cancellationToken));
        }).WithTags("Price Management");

        referenceGroup.MapGet("/weight-slabs", async (ReferenceDataService referenceDataService, CancellationToken cancellationToken) =>
            Results.Ok(await referenceDataService.ListWeightSlabsAsync(cancellationToken))).RequirePermission("pricing.read").WithTags("Price Management");

        var rateCardsGroup = app.MapGroup("/api/v1/rate-cards").WithTags("Rate Card Management");

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
        {
            var zoneRates = await rateCardService.ListZoneRatesAsync(id, cancellationToken);
            return zoneRates is null ? Results.NotFound() : Results.Ok(zoneRates);
        }).RequirePermission("pricing.read");

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
        {
            var charges = await rateCardService.ListAdditionalChargesAsync(id, cancellationToken);
            return charges is null ? Results.NotFound() : Results.Ok(charges);
        }).RequirePermission("pricing.read");

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

        var merchantRateCardGroup = app.MapGroup("/api/v1/merchants")
        .WithTags("Merchant Rate Card")
        ;

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

        var quoteGroup = app.MapGroup("/api/v1/pricing")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Bearer, AuthSchemes.ApiKey)
                .RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true))
            .RequireApiKeyRateLimit()
            .WithTags("Price Quote");

        quoteGroup.MapPost("/quote", async (PriceQuoteRequest request, PricingService pricingService, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (!await HasPricingReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker))
            {
                return httpContext.User.GetMerchantId() is null && httpContext.User.GetUserId() is null ? Results.Unauthorized() : Results.Forbid();
            }

            // A merchant caller's client-supplied MerchantId is never trusted — forced to the
            // caller's own id regardless of what the request body said, same principle
            // CreateShipmentRequest's own doc comment establishes for booking. Without this, a
            // merchant JWT could pass another merchant's id and read their MerchantRateCard
            // discount through the quote response.
            if (httpContext.User.GetMerchantId() is Guid callerMerchantId)
            {
                request = request with { MerchantId = callerMerchantId };
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
        });
    }

    // Shared by /service-types (reference data a quote preview needs) and /quote itself — staff
    // via "pricing.read", merchant JWT/API-key via MerchantPermissionCatalog.PricingRead.
    private static async Task<bool> HasPricingReadAccessAsync(HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker)
    {
        if (httpContext.User.GetMerchantId() is not null)
        {
            return await merchantAuthorizationChecker.HasPermissionAsync(httpContext.User, MerchantPermissionCatalog.PricingRead, httpContext.RequestAborted);
        }
        if (httpContext.User.GetUserId() is not null)
        {
            return await permissionChecker.HasPermissionAsync(httpContext.User, "pricing.read", httpContext.RequestAborted);
        }
        return false;
    }
}
