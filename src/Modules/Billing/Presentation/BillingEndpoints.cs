using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Billing.Presentation;

public record PayInvoiceRequest(PaymentGateway Gateway, string ReturnBaseUrl);
public record AssignPlanRequest(Guid PlanId);
public record BillingStatusResponse(SubscriptionStatus Status, DateTimeOffset? TrialEndsAt);

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

        // Unlike GET /plan above (404s whenever no Plan is chosen — e.g. during a trial), this
        // always reflects the Subscription's real state, so the staff Billing page can render a
        // trial countdown or a "trial expired" prompt instead of treating a 404 as an error.
        group.MapGet("/subscription", async (SubscriptionService service, ICurrentTenantProvider tenantProvider, CancellationToken cancellationToken) =>
        {
            var status = await service.GetSubscriptionStatusAsync(tenantProvider.OrganizationId!.Value, cancellationToken);
            return status is null ? Results.NotFound() : Results.Ok(status);
        }).RequirePermission("org.billing.manage");

        // Deliberately NOT permission-gated beyond authentication: every staff member (not just
        // the OrganizationOwner who holds org.billing.manage) needs to know their org's trial/
        // subscription state to understand why writes might be blocked
        // (TrialEnforcementMiddleware). This is a narrower projection of GET /subscription above
        // (Status/TrialEndsAt only, no PlanName/MonthlyPriceNpr) so it never leaks pricing to
        // non-owner staff.
        group.MapGet("/status", async (SubscriptionService service, ICurrentTenantProvider tenantProvider, CancellationToken cancellationToken) =>
        {
            var status = await service.GetSubscriptionStatusAsync(tenantProvider.OrganizationId!.Value, cancellationToken);
            return status is null
                ? Results.NotFound()
                : Results.Ok(new BillingStatusResponse(status.Status, status.TrialEndsAt));
        }).RequireAuthorization();

        group.MapGet("/plans", async (PlanService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListActivePlansAsync(cancellationToken)))
            .RequirePermission("org.billing.manage");

        // The one call this entire feature is about — finally reachable from a real request.
        // Reuses AssignPlanAsync unmodified: it creates-or-updates the org's Subscription, sets
        // Status = Active, and (since a Trial subscription row already exists from
        // registration) takes the "existing is not null" branch, leaving CurrentCycleStart/End
        // as originally set at trial start rather than resetting the billing cycle clock.
        group.MapPost("/plan", async (AssignPlanRequest request, SubscriptionService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.AssignPlanAsync(tenantProvider.OrganizationId!.Value, request.PlanId, httpContext.User.GetUserId()!.Value, cancellationToken);
                return Results.Ok(result);
            }
            catch (BillingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BILLING_VALIDATION_FAILED", message = ex.Message } });
            }
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
