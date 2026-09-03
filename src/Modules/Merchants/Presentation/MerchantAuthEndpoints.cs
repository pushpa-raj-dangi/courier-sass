using Courier.Merchants.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Merchants.Presentation;

public static class MerchantAuthEndpoints
{
    public static void MapMerchantAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchant/auth").WithTags("Merchant Auth Management");

        group.MapPost("/login", async (MerchantLoginRequest request, MerchantAuthService authService) =>
        {
            try
            {
                var result = await authService.LoginAsync(request);
                return Results.Ok(result);
            }
            catch (MerchantAuthenticationFailedException ex)
            {
                return Results.Json(new { error = new { code = "LOGIN_FAILED", message = ex.Message } }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        group.MapPost("/refresh", async (MerchantRefreshRequest request, MerchantAuthService authService) =>
        {
            try
            {
                var result = await authService.RefreshTokenAsync(request);
                return Results.Ok(result);
            }
            catch (MerchantAuthenticationFailedException ex)
            {
                return Results.Json(new { error = new { code = "REFRESH_FAILED", message = ex.Message } }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        // Bare RequireAuthorization() defaults to the app's default scheme ("Bearer" —
        // Program.cs's AddAuthentication(JwtBearerDefaults.AuthenticationScheme)), which both
        // staff and merchant JWTs validate under (shared signing config) but an ApiKey-scheme
        // credential never does — this alone is the JWT-only self-escalation carve-out, same
        // bar ApiKeyEndpoints' three routes already enforce, no separate filter needed. No
        // MerchantPermissionCatalog check either: /me only requires being a valid, active
        // merchant user, not any specific permission.
        group.MapGet("/me", async (HttpContext httpContext, MerchantAuthService authService, CancellationToken cancellationToken) =>
        {
            var result = await authService.GetMeAsync(httpContext.User, cancellationToken);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        }).RequireAuthorization();
    }
}
