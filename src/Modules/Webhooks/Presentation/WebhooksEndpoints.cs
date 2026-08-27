using Courier.Merchants.Application;
using Courier.SharedKernel;
using Courier.Webhooks.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Webhooks.Presentation;

public static class WebhooksEndpoints
{
    public static void MapWebhooksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/webhooks").RequireApiKeyRateLimit().WithTags("Webhooks");

        group.MapPost("/", async (CreateWebhookSubscriptionRequest request, WebhookService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var (organizationId, merchantId, unauthorized) = ResolveCaller(tenantProvider, httpContext);
            if (unauthorized is not null) return unauthorized;
            return await HandleAsync(() => service.CreateSubscriptionAsync(organizationId, merchantId, request, httpContext.RequestAborted));
        }).RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.WebhooksManage);

        group.MapGet("/", async (WebhookService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var (organizationId, merchantId, unauthorized) = ResolveCaller(tenantProvider, httpContext);
            if (unauthorized is not null) return unauthorized;
            return Results.Ok(await service.ListAsync(organizationId, merchantId, httpContext.RequestAborted));
        }).RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.WebhooksManage);

        group.MapDelete("/{id:guid}", async (Guid id, WebhookService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var (organizationId, merchantId, unauthorized) = ResolveCaller(tenantProvider, httpContext);
            if (unauthorized is not null) return unauthorized;
            var deleted = await service.DeleteAsync(organizationId, merchantId, id, httpContext.RequestAborted);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.WebhooksManage);

        group.MapGet("/{id:guid}/deliveries", async (Guid id, WebhookService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var (organizationId, merchantId, unauthorized) = ResolveCaller(tenantProvider, httpContext);
            if (unauthorized is not null) return unauthorized;
            var deliveries = await service.ListDeliveriesAsync(organizationId, merchantId, id, httpContext.RequestAborted);
            return deliveries is null ? Results.NotFound() : Results.Ok(deliveries);
        }).RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.WebhooksManage);

        group.MapPost("/deliveries/{id:guid}/replay", async (Guid id, WebhookService service, IWebhookDeliveryAttemptService attemptService, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var (organizationId, merchantId, unauthorized) = ResolveCaller(tenantProvider, httpContext);
            if (unauthorized is not null) return unauthorized;
            var result = await service.ReplayAsync(organizationId, merchantId, id, attemptService, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.WebhooksManage);
    }

    private static (Guid OrganizationId, Guid MerchantId, IResult? Unauthorized) ResolveCaller(ICurrentTenantProvider tenantProvider, HttpContext httpContext)
    {
        var merchantId = httpContext.User.GetMerchantId();
        if (merchantId is null)
        {
            return (default, default, Results.Unauthorized());
        }
        if (tenantProvider.OrganizationId is not Guid organizationId)
        {
            return (default, default, Results.Unauthorized());
        }
        return (organizationId, merchantId.Value, null);
    }

    private static async Task<IResult> HandleAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            var result = await operation();
            return Results.Ok(result);
        }
        catch (WebhookValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "WEBHOOK_VALIDATION_FAILED", message = ex.Message } });
        }
    }
}
