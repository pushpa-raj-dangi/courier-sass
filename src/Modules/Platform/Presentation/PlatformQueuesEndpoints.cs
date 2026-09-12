using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformQueuesEndpoints
{
    public static void MapPlatformQueuesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/platform/queues", async (PlatformQueuesService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetQueuesAsync(cancellationToken)))
            .WithTags("Platform Queues")
            .RequireSuperAdmin();
    }
}
