using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformJobsEndpoints
{
    public static void MapPlatformJobsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/platform/jobs", async (PlatformJobsService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetOverviewAsync(cancellationToken)))
            .WithTags("Platform Jobs")
            .RequireSuperAdmin();
    }
}
