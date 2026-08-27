using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Courier.SharedKernel;

// Only enforces a limit for an ApiKey-authenticated caller (presence of the api_key_scopes
// claim, set only by ApiKeyAuthenticationHandler, Task 5) — a staff or merchant JWT caller
// reaching the same route group passes through unlimited. Partitioned per ApiKey.Id, so one
// merchant's key exhausting its quota never affects another key or a JWT caller.
public class ApiKeyRateLimitFilter : IEndpointFilter
{
    private readonly string _configKey;
    private readonly int _defaultLimit;
    private readonly TimeSpan _window;

    public ApiKeyRateLimitFilter(string configKey, int defaultLimit, TimeSpan window)
    {
        _configKey = configKey;
        _defaultLimit = defaultLimit;
        _window = window;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (httpContext.User.FindFirst("api_key_scopes") is null)
        {
            return await next(context);
        }
        var apiKeyId = httpContext.User.GetUserId();
        if (apiKeyId is null)
        {
            return await next(context);
        }

        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var limit = configuration.GetValue<int?>(_configKey) ?? _defaultLimit;
        var limiter = httpContext.RequestServices.GetRequiredService<IRateLimiter>();
        var result = await limiter.TryAcquireAsync($"apikey:{apiKeyId}", limit, _window, httpContext.RequestAborted);
        if (!result.Allowed)
        {
            return RateLimitResultHelper.RateLimitExceededResult(httpContext, result.RetryAfter);
        }

        return await next(context);
    }
}

// Partitioned per client IP — the only credential-free signal available on an anonymous
// endpoint (public tracking, Task 12).
public class IpRateLimitFilter : IEndpointFilter
{
    private readonly string _configKey;
    private readonly int _defaultLimit;
    private readonly TimeSpan _window;

    public IpRateLimitFilter(string configKey, int defaultLimit, TimeSpan window)
    {
        _configKey = configKey;
        _defaultLimit = defaultLimit;
        _window = window;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var limit = configuration.GetValue<int?>(_configKey) ?? _defaultLimit;
        var limiter = httpContext.RequestServices.GetRequiredService<IRateLimiter>();
        var result = await limiter.TryAcquireAsync($"ip:{ip}", limit, _window, httpContext.RequestAborted);
        if (!result.Allowed)
        {
            return RateLimitResultHelper.RateLimitExceededResult(httpContext, result.RetryAfter);
        }

        return await next(context);
    }
}

internal static class RateLimitResultHelper
{
    public static IResult RateLimitExceededResult(HttpContext httpContext, TimeSpan retryAfter)
    {
        httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        return Results.Json(
            new { error = new { code = "RATE_LIMIT_EXCEEDED", message = "Too many requests." } },
            statusCode: StatusCodes.Status429TooManyRequests);
    }
}

public static class RateLimitEndpointExtensions
{
    public static RouteGroupBuilder RequireApiKeyRateLimit(this RouteGroupBuilder builder)
        => builder.AddEndpointFilter(new ApiKeyRateLimitFilter("RateLimiting:ApiKeyRequestsPerMinute", 60, TimeSpan.FromMinutes(1)));

    public static RouteHandlerBuilder RequireIpRateLimit(this RouteHandlerBuilder builder)
        => builder.AddEndpointFilter(new IpRateLimitFilter("RateLimiting:PublicTrackingRequestsPerMinute", 30, TimeSpan.FromMinutes(1)));
}
