using Courier.Reports.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Reports.Presentation;

public static class ReportsEndpoints
{
    public static void MapReportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reports")
            .WithTags("Reports")
            .RequireAuthorization();

        group.MapGet("/dashboard/organization", async (DateTimeOffset? from, DateTimeOffset? to, OrgDashboardService service, HttpContext httpContext) =>
            await HandleAsync(() => service.GetAsync(from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.read");

        group.MapGet("/dashboard/branch/{branchId:guid}", async (Guid branchId, BranchDashboardService service, HttpContext httpContext) =>
            await HandleAsync(() => service.GetAsync(branchId, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.read");

        group.MapGet("/export/shipments", async (Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ReportExportService service, HttpContext httpContext) =>
            await HandleCsvAsync(() => service.GetShipmentsCsvAsync(branchId, from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.export");

        group.MapGet("/export/cod-summary", async (Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ReportExportService service, HttpContext httpContext) =>
            await HandleCsvAsync(() => service.GetCodSummaryCsvAsync(branchId, from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.export");
    }

    private static async Task<IResult> HandleAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (ReportsValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "REPORTS_VALIDATION_FAILED", message = ex.Message } });
        }
        catch (InvalidDateRangeException ex)
        {
            return Results.BadRequest(new { error = new { code = "INVALID_DATE_RANGE", message = ex.Message } });
        }
    }

    private static async Task<IResult> HandleCsvAsync(Func<Task<string>> operation)
    {
        try
        {
            var csv = await operation();
            return Results.Text(csv, "text/csv");
        }
        catch (ReportsValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "REPORTS_VALIDATION_FAILED", message = ex.Message } });
        }
        catch (InvalidDateRangeException ex)
        {
            return Results.BadRequest(new { error = new { code = "INVALID_DATE_RANGE", message = ex.Message } });
        }
    }
}
