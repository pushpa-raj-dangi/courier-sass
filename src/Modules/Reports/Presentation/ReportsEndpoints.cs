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
            await HandleCsvAsync(httpContext, "shipments", () => service.GetShipmentsCsvAsync(branchId, from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.export");

        group.MapGet("/export/cod-summary", async (Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ReportExportService service, HttpContext httpContext) =>
            await HandleCsvAsync(httpContext, "cod-summary", () => service.GetCodSummaryCsvAsync(branchId, from, to, httpContext.User, httpContext.RequestAborted)))
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

    private static async Task<IResult> HandleCsvAsync(HttpContext httpContext, string fileNamePrefix, Func<Task<IEnumerable<string>>> operation)
    {
        IEnumerable<string> lines;
        try
        {
            lines = await operation();
        }
        catch (ReportsValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "REPORTS_VALIDATION_FAILED", message = ex.Message } });
        }
        catch (InvalidDateRangeException ex)
        {
            return Results.BadRequest(new { error = new { code = "INVALID_DATE_RANGE", message = ex.Message } });
        }

        httpContext.Response.ContentType = "text/csv";
        httpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileNamePrefix}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv\"";
        await using var writer = new StreamWriter(httpContext.Response.Body, leaveOpen: true);
        foreach (var line in lines)
        {
            await writer.WriteAsync(line);
        }
        await writer.FlushAsync();
        return Results.Empty;
    }
}
