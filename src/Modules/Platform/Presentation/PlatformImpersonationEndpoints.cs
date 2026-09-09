using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformImpersonationEndpoints
{
    public static void MapPlatformImpersonationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/platform/organizations/{organizationId:guid}/users/{userId:guid}/impersonate",
            async (Guid organizationId, Guid userId, ImpersonationService service, HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                try
                {
                    var result = await service.IssueAsync(organizationId, userId, httpContext.User.GetUserId()!.Value, cancellationToken);
                    return Results.Ok(result);
                }
                catch (PlatformValidationException ex)
                {
                    return Results.NotFound(new { error = new { code = "IMPERSONATION_TARGET_NOT_FOUND", message = ex.Message } });
                }
            })
            .WithTags("Platform Impersonation")
            .RequireSuperAdmin();
    }
}
