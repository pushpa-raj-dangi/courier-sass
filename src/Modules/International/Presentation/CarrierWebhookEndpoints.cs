using Courier.International.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.International.Presentation;

public static class CarrierWebhookEndpoints
{
    public const int MaxBodyBytes = 1_048_576;

    public static void MapCarrierWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous by design — authenticated by the carrier account's webhook signature only.
        app.MapPost("/api/v1/carrier-webhooks/{webhookPublicId}", async (string webhookPublicId, HttpContext httpContext, CarrierWebhookService service) =>
        {
            var body = await ReadCappedBodyAsync(httpContext.Request, httpContext.RequestAborted);
            if (body is null)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }
            var headers = httpContext.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            var outcome = await service.ReceiveAsync(webhookPublicId, headers, body, httpContext.RequestAborted);
            return outcome switch
            {
                CarrierWebhookOutcome.Accepted => Results.Accepted(),
                CarrierWebhookOutcome.InvalidSignature => Results.Unauthorized(),
                CarrierWebhookOutcome.InvalidPayload => InternationalEndpointErrors.Error(StatusCodes.Status400BadRequest,
                    "CARRIER_WEBHOOK_INVALID_PAYLOAD", "The webhook body could not be parsed."),
                _ => Results.NotFound()
            };
        })
        .AllowAnonymous()
        .RequireIpRateLimit()
        .WithTags("Carrier Webhooks");
    }

    // null = larger than MaxBodyBytes (declared or actual).
    private static async Task<byte[]?> ReadCappedBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxBodyBytes)
        {
            return null;
        }
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
