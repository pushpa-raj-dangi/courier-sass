using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public record UpdatePlatformSettingRequest(string Value, string? ConfirmationPhrase);

public static class PlatformSettingsEndpoints
{
    public static void MapPlatformSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/settings").WithTags("Platform Settings");

        group.MapGet("/", async (PlatformSettingsService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAllAsync(cancellationToken)))
            .RequireSuperAdmin();

        group.MapPut("/{key}", async (string key, UpdatePlatformSettingRequest request, PlatformSettingsService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                await service.UpdateAsync(key, request.Value, request.ConfirmationPhrase, httpContext.User.GetUserId()!.Value, cancellationToken);
                return Results.NoContent();
            }
            catch (PlatformSettingsValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "PLATFORM_SETTING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();

        group.MapPost("/{key}/reset", async (string key, PlatformSettingsService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            await service.ResetAsync(key, httpContext.User.GetUserId()!.Value, cancellationToken);
            return Results.NoContent();
        }).RequireSuperAdmin();
    }
}
