using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Merchants.Presentation;

public static class MerchantProfileEndpoints
{
    public static void MapMerchantProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchant/profile");

        group.MapGet("/", async (HttpContext httpContext, MerchantOnboardingService onboardingService, CancellationToken cancellationToken) =>
        {
            var merchantId = httpContext.User.GetMerchantId();
            if (merchantId is null)
            {
                return Results.Unauthorized();
            }
            var merchant = await onboardingService.GetMerchantAsync(merchantId.Value, cancellationToken);
            return merchant is null ? Results.NotFound() : Results.Ok(merchant);
        }).RequireMerchantPermission("merchant.profile.read");
    }
}
