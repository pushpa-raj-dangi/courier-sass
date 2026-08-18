using System.Net;
using System.Text.Json;

namespace Courier.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing {Path}", context.Request.Path);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var envelope = new
            {
                error = new
                {
                    code = "INTERNAL_SERVER_ERROR",
                    message = "An unexpected error occurred.",
                    details = (object?)null,
                    traceId = context.TraceIdentifier
                }
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(envelope));
        }
    }
}
