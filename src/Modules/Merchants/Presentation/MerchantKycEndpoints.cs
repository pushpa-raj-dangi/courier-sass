using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Merchants.Presentation;

public static class MerchantKycEndpoints
{
    public static void MapMerchantKycEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchant/kyc-documents");

        // Raw request body upload (not multipart/form-data): the client sends the file's bytes
        // directly as the request body with documentType on the query string. This sidesteps
        // IFormFile/antiforgery concerns entirely for a single-file upload, which is all this
        // phase needs.
        group.MapPost("/", async (KycDocumentType documentType, HttpContext httpContext, MerchantKycService kycService) =>
        {
            try
            {
                var result = await kycService.UploadDocumentAsync(
                    documentType, httpContext.Request.Body, httpContext.Request.ContentType ?? "application/octet-stream",
                    httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/merchant/kyc-documents/{result.Id}", result);
            }
            catch (MerchantValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "MERCHANT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireMerchantPermission("merchant.kyc.upload");

        group.MapGet("/", async (HttpContext httpContext, MerchantKycService kycService, CancellationToken cancellationToken) =>
        {
            var merchantId = httpContext.User.GetMerchantId();
            if (merchantId is null)
            {
                return Results.Unauthorized();
            }
            return Results.Ok(await kycService.ListDocumentsAsync(merchantId.Value, cancellationToken));
        }).RequireMerchantPermission("merchant.kyc.upload");
    }
}
