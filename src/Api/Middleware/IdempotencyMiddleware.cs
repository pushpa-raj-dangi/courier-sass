using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Courier.SharedKernel;

namespace Courier.Api.Middleware;

public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly Regex ShipmentCancelPattern = new(@"^/api/v1/shipments/[0-9a-fA-F-]{36}/cancel$", RegexOptions.Compiled);
    private static readonly Regex RiderScanPickupPattern = new(@"^/api/v1/riders/[0-9a-fA-F-]{36}/scan/pickup$", RegexOptions.Compiled);
    private static readonly Regex RiderDeliveryFailPattern = new(@"^/api/v1/riders/[0-9a-fA-F-]{36}/delivery-attempts/fail$", RegexOptions.Compiled);
    private static readonly Regex RiderDeliveryCompletePattern = new(@"^/api/v1/riders/[0-9a-fA-F-]{36}/delivery-attempts/complete$", RegexOptions.Compiled);
    private static readonly Regex RiderPodUploadsPattern = new(@"^/api/v1/riders/[0-9a-fA-F-]{36}/pod-uploads$", RegexOptions.Compiled);

    private enum RouteKind { None, MerchantWrite, RiderAction }

    public IdempotencyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IIdempotencyStore idempotencyStore)
    {
        var routeKind = ClassifyRoute(context.Request);
        if (routeKind == RouteKind.None)
        {
            await _next(context);
            return;
        }

        string partitionKey;
        if (routeKind == RouteKind.MerchantWrite)
        {
            var merchantId = context.User.GetMerchantId();
            if (merchantId is null)
            {
                // Staff-JWT (or unauthenticated) callers on these same routes are completely
                // untouched — unchanged from the original merchant-only design, and the reason
                // every existing ShipmentsEndpointsTests/PickupsEndpointsTests case (all
                // staff-JWT-driven, none sending Idempotency-Key) still passes unmodified.
                await _next(context);
                return;
            }
            partitionKey = merchantId.Value.ToString();
        }
        else
        {
            var userId = context.User.GetUserId();
            if (userId is null)
            {
                await _next(context);
                return;
            }
            partitionKey = $"staff:{userId}";
        }

        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keyHeader) || string.IsNullOrWhiteSpace(keyHeader))
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key header is required for this request.");
            return;
        }
        var idempotencyKey = keyHeader.ToString();

        context.Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync();
        }
        context.Request.Body.Position = 0;
        var bodyHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody)));

        var storeKey = $"{partitionKey}:{idempotencyKey}";
        var existing = await idempotencyStore.GetAsync(storeKey, context.RequestAborted);
        if (existing is not null)
        {
            if (existing.RequestBodyHash != bodyHash)
            {
                await WriteErrorAsync(context, StatusCodes.Status409Conflict, "IDEMPOTENCY_KEY_REUSED", "This Idempotency-Key was already used with a different request body.");
                return;
            }

            context.Response.StatusCode = existing.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(existing.ResponseBody);
            return;
        }

        var originalBody = context.Response.Body;
        await using var capturedBody = new MemoryStream();
        context.Response.Body = capturedBody;

        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        capturedBody.Position = 0;
        var responseText = await new StreamReader(capturedBody).ReadToEndAsync();
        capturedBody.Position = 0;
        await capturedBody.CopyToAsync(originalBody);

        if (context.Response.StatusCode is >= 200 and < 300)
        {
            await idempotencyStore.StoreAsync(storeKey, new IdempotencyRecord(bodyHash, context.Response.StatusCode, responseText), TimeSpan.FromHours(24), context.RequestAborted);
        }
    }

    private static RouteKind ClassifyRoute(HttpRequest request)
    {
        if (request.Method != HttpMethods.Post)
        {
            return RouteKind.None;
        }
        var path = request.Path.Value ?? string.Empty;
        if (path == "/api/v1/shipments" || path == "/api/v1/pickups" || ShipmentCancelPattern.IsMatch(path))
        {
            return RouteKind.MerchantWrite;
        }
        if (RiderScanPickupPattern.IsMatch(path) || RiderDeliveryFailPattern.IsMatch(path) ||
            RiderDeliveryCompletePattern.IsMatch(path) || RiderPodUploadsPattern.IsMatch(path))
        {
            return RouteKind.RiderAction;
        }
        return RouteKind.None;
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = new { code, message } }));
    }
}
