using Courier.International.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.International.Presentation;

public static class CarrierAccountEndpoints
{
    public static void MapCarrierAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/international/carrier-accounts")
            .WithTags("International Carrier Accounts")
            .RequireAuthorization();

        group.MapGet("/", async (CarrierAccountService service, HttpContext httpContext) =>
            Results.Ok(await service.ListAsync(httpContext.RequestAborted)))
            .RequirePermission("international.carrier.manage");

        group.MapGet("/{id:guid}", async (Guid id, CarrierAccountService service, HttpContext httpContext) =>
        {
            var result = await service.GetAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("international.carrier.manage");

        group.MapPost("/", (CreateCarrierAccountRequest request, CarrierAccountService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.CreateAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/international/carrier-accounts/{result.Account.Id}", result);
            })).RequirePermission("international.carrier.manage");

        group.MapPut("/{id:guid}", (Guid id, UpdateCarrierAccountRequest request, CarrierAccountService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.UpdateAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.carrier.manage");

        group.MapPost("/{id:guid}/credentials", (Guid id, ReplaceCarrierCredentialsRequest request, CarrierAccountService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.ReplaceCredentialsAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.carrier.manage");

        group.MapPost("/{id:guid}/webhook-secret/rotate", (Guid id, CarrierAccountService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.RotateWebhookSecretAsync(id, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.carrier.manage");
    }
}
