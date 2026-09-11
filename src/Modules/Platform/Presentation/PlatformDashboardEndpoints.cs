using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformDashboardEndpoints
{
    public static void MapPlatformDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform").WithTags("Platform Dashboard");

        group.MapGet("/dashboard", async (PlatformDashboardService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetMetricsAsync(cancellationToken)))
            .RequireSuperAdmin();

        group.MapGet("/organizations/{id:guid}", async (Guid id, PlatformDashboardService service, CancellationToken cancellationToken) =>
        {
            var detail = await service.GetOrganizationDetailAsync(id, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        }).RequireSuperAdmin();

        // The org detail Sheet's three nested sections, each independently paginated — see
        // OrganizationDetail's own doc comment for why these aren't embedded in the detail
        // response above.
        group.MapGet("/organizations/{id:guid}/users", async (Guid id, int? page, int? pageSize, PlatformDashboardService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListOrganizationUsersAsync(id, page, pageSize, cancellationToken)))
            .RequireSuperAdmin();

        group.MapGet("/organizations/{id:guid}/branches", async (Guid id, int? page, int? pageSize, PlatformDashboardService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListOrganizationBranchesAsync(id, page, pageSize, cancellationToken)))
            .RequireSuperAdmin();

        group.MapGet("/organizations/{id:guid}/invoices", async (Guid id, int? page, int? pageSize, PlatformDashboardService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListOrganizationInvoicesAsync(id, page, pageSize, cancellationToken)))
            .RequireSuperAdmin();
    }
}
