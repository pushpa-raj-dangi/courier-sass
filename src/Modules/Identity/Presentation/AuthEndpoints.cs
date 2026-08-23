using Courier.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Identity.Presentation;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth Management");

        group.MapPost("/register", async (RegisterOrganizationRequest request, AuthService authService) =>
        {
            try
            {
                var result = await authService.RegisterOrganizationAsync(request);
                return Results.Ok(result);
            }
            catch (AuthenticationFailedException ex)
            {
                return Results.BadRequest(new { error = new { code = "REGISTRATION_FAILED", message = ex.Message } });
            }
        });

        group.MapPost("/login", async (LoginRequest request, AuthService authService) =>
        {
            try
            {
                var result = await authService.LoginAsync(request);
                return Results.Ok(result);
            }
            catch (AuthenticationFailedException ex)
            {
                return Results.Json(new { error = new { code = "LOGIN_FAILED", message = ex.Message } }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        group.MapPost("/refresh", async (RefreshRequest request, AuthService authService) =>
        {
            try
            {
                var result = await authService.RefreshTokenAsync(request);
                return Results.Ok(result);
            }
            catch (AuthenticationFailedException ex)
            {
                return Results.Json(new { error = new { code = "REFRESH_FAILED", message = ex.Message } }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });
    }
}
