using Courier.Platform.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformAuthEndpoints
{
    public static void MapPlatformAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/auth").WithTags("Platform Auth");

        group.MapPost("/login", async (PlatformLoginRequest request, PlatformAuthService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.LoginAsync(request, cancellationToken);
                return Results.Ok(result);
            }
            catch (PlatformAuthenticationFailedException ex)
            {
                return Results.Json(new { error = new { code = "LOGIN_FAILED", message = ex.Message } }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        group.MapPost("/refresh", async (PlatformRefreshRequest request, PlatformAuthService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.RefreshTokenAsync(request, cancellationToken);
                return Results.Ok(result);
            }
            catch (PlatformAuthenticationFailedException ex)
            {
                return Results.Json(new { error = new { code = "REFRESH_FAILED", message = ex.Message } }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });
    }
}
