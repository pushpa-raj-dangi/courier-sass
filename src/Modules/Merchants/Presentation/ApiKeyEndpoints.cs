using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Merchants.Presentation;

public static class ApiKeyEndpoints
{
    public static void MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchant/api-keys").WithTags("Merchant API Keys");

        // JWT-only by construction: RequireMerchantPermission (unlike the new
        // RequireMerchantPermissionAnyCredential added in Task 5) only ever accepts the default
        // "Bearer" scheme. An API key can never reach these three routes, so it can never create,
        // list, or revoke another API key — the self-escalation carve-out from Global Constraints.
        group.MapPost("/", async (CreateApiKeyRequest request, ApiKeyService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CreateApiKeyAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/merchant/api-keys/{result.Id}", result);
            }
            catch (MerchantValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "MERCHANT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireMerchantPermission(MerchantPermissionCatalog.ApiKeysManage);

        group.MapGet("/", async (ApiKeyService service, HttpContext httpContext, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListApiKeysAsync(httpContext.User, cancellationToken)))
            .RequireMerchantPermission(MerchantPermissionCatalog.ApiKeysManage);

        group.MapDelete("/{id:guid}", async (Guid id, ApiKeyService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            var result = await service.RevokeApiKeyAsync(id, httpContext.User, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireMerchantPermission(MerchantPermissionCatalog.ApiKeysManage);
    }
}
