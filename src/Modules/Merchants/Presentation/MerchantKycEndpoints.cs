using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Merchants.Presentation;

public static class MerchantKycEndpoints
{
    // Allowlisted content types for KYC document uploads (citizenship documents, PAN cards, etc.)
    // — scans/photos are typically PDF or JPEG/PNG. Anything else is rejected before it ever
    // reaches storage.
    private static readonly string[] AllowedContentTypes = ["application/pdf", "image/jpeg", "image/png"];

    // Explicit cap on KYC upload size. Kestrel's global default max request body size (30MB)
    // still applies underneath this, but 10MB is a tighter, purpose-specific limit for a single
    // scanned document/photo. Requests without a Content-Length header (e.g. chunked transfer)
    // skip this pre-check and fall through to Kestrel's global limit — acceptable for this phase
    // since minimal API delegates have no equivalent of [RequestSizeLimit] to attach per-route.
    private const long MaxUploadSizeBytes = 10 * 1024 * 1024;

    public static void MapMerchantKycEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchant/kyc-documents");

        // Raw request body upload (not multipart/form-data): the client sends the file's bytes
        // directly as the request body with documentType on the query string. This sidesteps
        // IFormFile/antiforgery concerns entirely for a single-file upload, which is all this
        // phase needs.
        group.MapPost("/", async (KycDocumentType documentType, HttpContext httpContext, MerchantKycService kycService) =>
        {
            var contentType = httpContext.Request.ContentType;
            if (contentType is null || !AllowedContentTypes.Any(allowed => contentType.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.BadRequest(new { error = new { code = "UNSUPPORTED_CONTENT_TYPE", message = "Only application/pdf, image/jpeg, and image/png are supported." } });
            }

            if (httpContext.Request.ContentLength is long contentLength && contentLength > MaxUploadSizeBytes)
            {
                return Results.BadRequest(new { error = new { code = "FILE_TOO_LARGE", message = "KYC documents must be 10MB or smaller." } });
            }

            try
            {
                var result = await kycService.UploadDocumentAsync(
                    documentType, httpContext.Request.Body, contentType,
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
            try
            {
                return Results.Ok(await kycService.ListDocumentsAsync(httpContext.User, cancellationToken));
            }
            catch (MerchantValidationException)
            {
                // RequireMerchantPermission already guarantees an authenticated, active
                // MerchantUser reached this point, so the acting merchant should always resolve
                // — this catch is a defensive fallback only, not a reachable path in practice.
                return Results.Unauthorized();
            }
        }).RequireMerchantPermission("merchant.kyc.upload");
    }
}
