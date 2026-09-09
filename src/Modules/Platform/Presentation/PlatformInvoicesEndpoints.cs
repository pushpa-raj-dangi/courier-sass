using Courier.Billing.Domain;
using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformInvoicesEndpoints
{
    public static void MapPlatformInvoicesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/platform/invoices", async (InvoiceStatus? status, PlatformDashboardService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.ListAllInvoicesAsync(status, cancellationToken)))
            .WithTags("Platform Invoices")
            .RequireSuperAdmin();
    }
}
