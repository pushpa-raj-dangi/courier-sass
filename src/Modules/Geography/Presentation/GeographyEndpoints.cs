using Courier.Geography.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Geography.Presentation;

public static class GeographyEndpoints
{
    public static void MapGeographyEndpoints(this IEndpointRouteBuilder app)
    {
        // Dual-auth: staff via "geography.read", or merchant JWT/API-key via
        // MerchantPermissionCatalog.GeographyRead ("merchant.geography.read") — the merchant
        // booking form's Recipient/Sender GeographyPicker needs the same province/district/
        // local-level reads the staff Branch/Shipment forms already have (Frontend Phase F10
        // Task 16 E2E fix). Same manual per-credential-type branching PricingEndpoints already
        // establishes for /service-types and /quote, since no existing filter covers "either
        // token kind, different permission string per kind." Geography reference data has no
        // OrganizationId/MerchantId at all (confirmed in GeographyQueryService) — no
        // tenant-isolation risk in exposing it further, only an authorization-scheme change.
        //
        // The merchant permission is checked by string literal rather than
        // MerchantPermissionCatalog.GeographyRead: Merchants.Application already depends on this
        // Geography module (for address fields), so a reference back from Geography to Merchants
        // would cycle. Keep this literal in sync with that constant.
        var group = app.MapGroup("/api/v1/geography")
            .WithTags("Geography Management")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Bearer, AuthSchemes.ApiKey)
                .RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true))
            .RequireApiKeyRateLimit();

        group.MapGet("/provinces", async (GeographyQueryService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker, CancellationToken cancellationToken) =>
        {
            if (!await HasGeographyReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker, cancellationToken))
            {
                return Results.Forbid();
            }
            return Results.Ok(await service.ListProvincesAsync(cancellationToken));
        });

        group.MapGet("/districts", async (Guid provinceId, GeographyQueryService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker, CancellationToken cancellationToken) =>
        {
            if (!await HasGeographyReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker, cancellationToken))
            {
                return Results.Forbid();
            }
            return Results.Ok(await service.ListDistrictsAsync(provinceId, cancellationToken));
        });

        group.MapGet("/local-levels", async (Guid districtId, GeographyQueryService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker, CancellationToken cancellationToken) =>
        {
            if (!await HasGeographyReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker, cancellationToken))
            {
                return Results.Forbid();
            }
            return Results.Ok(await service.ListLocalLevelsAsync(districtId, cancellationToken));
        });

        group.MapGet("/wards", async (Guid localLevelId, GeographyQueryService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker, CancellationToken cancellationToken) =>
        {
            if (!await HasGeographyReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker, cancellationToken))
            {
                return Results.Forbid();
            }
            return Results.Ok(await service.ListWardsAsync(localLevelId, cancellationToken));
        });

        group.MapGet("/areas", async (Guid wardId, GeographyQueryService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker, CancellationToken cancellationToken) =>
        {
            if (!await HasGeographyReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker, cancellationToken))
            {
                return Results.Forbid();
            }
            return Results.Ok(await service.ListAreasAsync(wardId, cancellationToken));
        });

        group.MapGet("/countries", async (CountryQueryService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker, CancellationToken cancellationToken) =>
        {
            if (!await HasGeographyReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker, cancellationToken))
            {
                return Results.Forbid();
            }
            return Results.Ok(await service.ListServicedAsync(cancellationToken));
        });
    }

    private static async Task<bool> HasGeographyReadAccessAsync(HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker, CancellationToken cancellationToken)
    {
        if (httpContext.User.GetMerchantId() is not null)
        {
            return await merchantAuthorizationChecker.HasPermissionAsync(httpContext.User, "merchant.geography.read", cancellationToken);
        }
        if (httpContext.User.GetUserId() is not null)
        {
            return await permissionChecker.HasPermissionAsync(httpContext.User, "geography.read", cancellationToken);
        }
        return false;
    }
}
