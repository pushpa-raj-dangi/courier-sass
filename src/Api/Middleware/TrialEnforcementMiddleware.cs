using Courier.Billing.Application;
using Courier.SharedKernel;

namespace Courier.Api.Middleware;

// Backend enforcement of trial expiry (CLAUDE.md §33: never trust the frontend as the
// enforcement boundary). Blocks non-GET requests for an org whose trial lapsed without a chosen
// Plan (Subscription.Status == TrialExpired, set by TrialExpiryJob). Placed after
// UseAuthentication()/UseAuthorization() (tenant identity is already resolved regardless of auth
// scheme — both the staff JWT and the merchant ApiKey scheme set an "org_id" claim, which
// ICurrentTenantProvider reads) and before IdempotencyMiddleware, so a request that is going to
// be rejected anyway never touches the idempotency store.
public class TrialEnforcementMiddleware
{
    private static readonly string[] ExemptPathPrefixes =
    [
        "/api/v1/auth",
        "/api/v1/billing",
        "/api/v1/platform",
        "/track",
        "/health",
        "/swagger",
        "/hangfire"
    ];

    private readonly RequestDelegate _next;

    public TrialEnforcementMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IBillingLookupService billingLookupService, ICurrentTenantProvider currentTenantProvider)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (ExemptPathPrefixes.Any(prefix =>
                path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var organizationId = currentTenantProvider.OrganizationId;
        if (organizationId is null)
        {
            await _next(context);
            return;
        }

        if (await billingLookupService.IsWriteBlockedAsync(organizationId.Value, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"error":{"code":"TRIAL_EXPIRED","message":"Your trial has ended. Choose a plan to continue."}}""");
            return;
        }

        await _next(context);
    }
}
