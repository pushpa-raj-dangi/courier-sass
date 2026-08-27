using System.Diagnostics;

namespace Courier.Api.Middleware;

public class ApiKeyRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyRequestLoggingMiddleware> _logger;

    public ApiKeyRequestLoggingMiddleware(RequestDelegate next, ILogger<ApiKeyRequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.FindFirst("api_key_scopes") is null)
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        await _next(context);
        stopwatch.Stop();

        _logger.LogInformation(
            "ApiKey request {ApiKeyId} org={OrganizationId} merchant={MerchantId} {Method} {Path} -> {StatusCode} in {ElapsedMs}ms (trace {TraceId})",
            context.User.FindFirst("sub")?.Value,
            context.User.FindFirst("org_id")?.Value,
            context.User.FindFirst("merchant_id")?.Value,
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            context.TraceIdentifier);
    }
}
