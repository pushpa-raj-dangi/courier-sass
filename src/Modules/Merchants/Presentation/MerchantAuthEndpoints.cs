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
    }
}
