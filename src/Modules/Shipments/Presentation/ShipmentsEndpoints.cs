using Courier.Merchants.Application;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Shipments.Presentation;

public static class ShipmentsEndpoints
{
    public static void MapShipmentsEndpoints(this IEndpointRouteBuilder app)
    {
        // Multi-scheme so API-key merchant callers (Task 5) reach the same manual dual-auth
        // branching below as merchant JWT callers already did — per-route permission checks are
        // unchanged and still fail closed for any credential that resolves neither GetMerchantId()
        // nor GetUserId() (e.g. a staff-only route reached by an API key, which has no User row).
        var group = app.MapGroup("/api/v1/shipments")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Bearer, AuthSchemes.ApiKey)
                .RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true))
            .RequireApiKeyRateLimit()
            .WithTags("Shipments Management");

        // POST / and /{id}/cancel accept EITHER a staff JWT (shipment.create/shipment.cancel via
        // IPermissionChecker) OR a merchant JWT (MerchantPermissionCatalog.ShipmentCreate/
        // ShipmentCancel via IMerchantAuthorizationChecker) — no existing single filter in this
        // codebase covers "either token kind", so both checkers are resolved and checked manually
        // here rather than chaining .RequirePermission()/.RequireMerchantPermission() (which each
        // assume exactly one token kind, per SharedKernel's existing filters).
        group.MapPost("/", async (CreateShipmentRequest request, ShipmentService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (httpContext.User.GetMerchantId() is not null)
            {
                if (!await merchantAuthorizationChecker.HasPermissionAsync(httpContext.User, MerchantPermissionCatalog.ShipmentCreate, httpContext.RequestAborted))
                {
                    return Results.Forbid();
                }
            }
            else if (httpContext.User.GetUserId() is not null)
            {
                if (!await permissionChecker.HasPermissionAsync(httpContext.User, "shipment.create", httpContext.RequestAborted))
                {
                    return Results.Forbid();
                }
            }
            else
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await service.CreateShipmentAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/shipments/{result.Awb}", result);
            }
            catch (ShipmentValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "SHIPMENT_VALIDATION_FAILED", message = ex.Message } });
            }
        });

        // Literal segment "/summary" is resolved ahead of the unconstrained "/{awb}" parameter
        // route below by ASP.NET Core's endpoint-routing precedence (literal beats parameter,
        // regardless of registration order), so this is never swallowed as an AWB lookup for
        // "summary" -- confirmed by GetShipmentsSummary_ReturnsStatusCounts_NotSwallowedByAwbRoute.
        group.MapGet("/summary", async (IShipmentReportingLookupService lookupService, IBranchAuthorizationChecker branchAuthorizationChecker, HttpContext httpContext, IPermissionChecker permissionChecker) =>
        {
            if (!await permissionChecker.HasPermissionAsync(httpContext.User, "shipment.read", httpContext.RequestAborted))
            {
                return Results.Forbid();
            }
            var accessibleBranchIds = await branchAuthorizationChecker.GetAccessibleBranchIdsAsync(httpContext.User, "shipment.read", httpContext.RequestAborted);
            var counts = await lookupService.GetCurrentStatusCountsAsync(accessibleBranchIds, httpContext.RequestAborted);
            return Results.Ok(counts.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));
        });

        group.MapGet("/{awb}", async (string awb, ShipmentService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (!await HasReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker))
            {
                return httpContext.User.GetMerchantId() is null && httpContext.User.GetUserId() is null ? Results.Unauthorized() : Results.Forbid();
            }

            var result = await service.GetByAwbAsync(awb, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/", async (int? page, int? pageSize, ShipmentService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (!await HasReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker))
            {
                return httpContext.User.GetMerchantId() is null && httpContext.User.GetUserId() is null ? Results.Unauthorized() : Results.Forbid();
            }

            return Results.Ok(await service.ListAsync(page, pageSize, httpContext.User, httpContext.RequestAborted));
        });

        group.MapPost("/{id:guid}/receive-origin", async (Guid id, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.ReceiveAtOriginAsync(id, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("shipment.scan");

        group.MapPost("/{id:guid}/cancel", async (Guid id, ShipmentService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (httpContext.User.GetMerchantId() is not null)
            {
                if (!await merchantAuthorizationChecker.HasPermissionAsync(httpContext.User, MerchantPermissionCatalog.ShipmentCancel, httpContext.RequestAborted))
                {
                    return Results.Forbid();
                }
            }
            else if (httpContext.User.GetUserId() is not null)
            {
                if (!await permissionChecker.HasPermissionAsync(httpContext.User, "shipment.cancel", httpContext.RequestAborted))
                {
                    return Results.Forbid();
                }
            }
            else
            {
                return Results.Unauthorized();
            }

            return await HandleTransitionAsync(() => service.CancelAsync(id, httpContext.User, httpContext.RequestAborted), id);
        });
    }

    // Same dual-auth reasoning as POST / and /{id}/cancel above: shipment.read (staff) or
    // MerchantPermissionCatalog.ShipmentRead (merchant) — checked manually since no existing
    // filter in this codebase covers "either token kind".
    private static async Task<bool> HasReadAccessAsync(HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker)
    {
        if (httpContext.User.GetMerchantId() is not null)
        {
            return await merchantAuthorizationChecker.HasPermissionAsync(httpContext.User, MerchantPermissionCatalog.ShipmentRead, httpContext.RequestAborted);
        }
        if (httpContext.User.GetUserId() is not null)
        {
            return await permissionChecker.HasPermissionAsync(httpContext.User, "shipment.read", httpContext.RequestAborted);
        }
        return false;
    }

    private static async Task<IResult> HandleTransitionAsync(Func<Task<ShipmentResponse?>> operation, Guid shipmentId)
    {
        try
        {
            var result = await operation();
            return result is null ? Results.NotFound() : Results.Ok(result);
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
