using Courier.Audit.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Audit.Presentation;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/audit-logs")
            .WithTags("Audit")
            .RequireAuthorization();

        group.MapGet("/", async (
            string? search, string? action, string? entityType, string? entityId, Guid? userId,
            DateTimeOffset? dateFrom, DateTimeOffset? dateTo, int? page, int? pageSize,
            IAuditLogReader reader, ICurrentTenantProvider tenant, HttpContext httpContext) =>
        {
            // Defense in depth: RequirePermission below already requires audit.read, which no
            // SuperAdmin-only caller (no Role, hence no permissions at all) can ever hold — but
            // if OrganizationId were ever null here regardless, passing it through unchanged
            // would turn SearchAsync into an unfiltered cross-tenant query. Fail closed instead.
            if (tenant.OrganizationId is null)
            {
                return Results.Forbid();
            }

            var filter = new AuditLogSearchFilter(search, action, entityType, entityId, userId, dateFrom, dateTo);
            var result = await reader.SearchAsync(tenant.OrganizationId, filter, page, pageSize, httpContext.RequestAborted);
            return Results.Ok(result);
        }).RequirePermission("audit.read");
    }
}
