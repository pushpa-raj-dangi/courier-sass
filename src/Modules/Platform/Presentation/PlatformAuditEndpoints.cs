using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformAuditEndpoints
{
    public static void MapPlatformAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1/platform/audit-logs").WithTags("Platform Audit")
            .MapGet("/", async (
                Guid? organizationId, string? organizationSearch, string? search, string? action, string? entityType,
                string? entityId, Guid? userId, DateTimeOffset? dateFrom, DateTimeOffset? dateTo, int? page, int? pageSize,
                PlatformAuditService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.SearchAsync(organizationId, organizationSearch, search, action, entityType, entityId, userId, dateFrom, dateTo, page, pageSize, cancellationToken)))
            .RequireSuperAdmin();
    }
}
