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
        // A malformed request body (invalid GUID/enum in a JSON property, truncated JSON, etc.)
        // surfaces as JsonException/BadHttpRequestException from minimal API model binding, not
        // as a domain exception — the framework does not convert it to 400 on its own once it
        // reaches this middleware, so without this branch every bad request body a client sends
        // came back as a 500 INTERNAL_SERVER_ERROR instead of a 400 validation error.
        catch (Exception ex) when (ex is JsonException or BadHttpRequestException)
        {
            _logger.LogWarning(ex, "Malformed request body for {Path}", context.Request.Path);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

            var envelope = new
            {
                error = new
                {
                    code = "INVALID_REQUEST_BODY",
                    message = "The request body could not be parsed.",
                    details = (object?)null,
                    traceId = context.TraceIdentifier
                }
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(envelope));
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
