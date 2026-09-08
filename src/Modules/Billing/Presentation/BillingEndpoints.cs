using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Billing.Presentation;

public record PayInvoiceRequest(PaymentGateway Gateway, string ReturnBaseUrl);

public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/billing").WithTags("Billing");

        group.MapGet("/plan", async (SubscriptionService service, ICurrentTenantProvider tenantProvider, CancellationToken cancellationToken) =>
        {
            var plan = await service.GetPlanSummaryAsync(tenantProvider.OrganizationId!.Value, cancellationToken);
            return plan is null ? Results.NotFound() : Results.Ok(plan);
        }).RequirePermission("org.billing.manage");

        group.MapGet("/invoices", async (SubscriptionService service, ICurrentTenantProvider tenantProvider, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListInvoicesAsync(tenantProvider.OrganizationId!.Value, cancellationToken)))
            .RequirePermission("org.billing.manage");

        group.MapPost("/invoices/{id:guid}/pay", async (Guid id, PayInvoiceRequest request, PaymentService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var checkoutInfo = await service.InitiatePaymentAsync(id, request.Gateway, request.ReturnBaseUrl, cancellationToken);
                return Results.Ok(checkoutInfo);
            }
            catch (BillingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BILLING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("org.billing.manage");

        group.MapPost("/payments/{id:guid}/callback", async (Guid id, Dictionary<string, string> callbackParameters, PaymentService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.CompletePaymentAsync(id, callbackParameters, cancellationToken);
                return result.Succeeded
                    ? Results.Ok(result)
                    : Results.BadRequest(new { error = new { code = "PAYMENT_NOT_CONFIRMED", message = result.Message } });
            }
            catch (BillingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BILLING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("org.billing.manage");
    }
}
