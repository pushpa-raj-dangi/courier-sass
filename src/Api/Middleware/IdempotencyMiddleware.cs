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

    public IdempotencyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IIdempotencyStore idempotencyStore)
    {
        if (!IsIdempotentRoute(context.Request))
        {
            await _next(context);
            return;
        }

        var merchantId = context.User.GetMerchantId();
        if (merchantId is null)
        {
            // Staff-JWT (or unauthenticated) callers on these same routes are completely
            // untouched — Global Constraints' explicit scope decision, avoids retrofitting every
            // existing staff-facing test from prior phases.
            await _next(context);
            return;
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

        var storeKey = $"{merchantId}:{idempotencyKey}";
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
            // Runs even if _next(context) throws, so a downstream exception never leaves
            // context.Response.Body pointing at capturedBody after it's disposed by the
            // `await using` unwind — ExceptionHandlingMiddleware (earlier in the pipeline) needs
            // the real response stream restored before it can write its JSON error envelope.
            context.Response.Body = originalBody;
        }

        // Only reached if _next completed without throwing — a failed request is correctly never
        // cached as a stored idempotent response.
        capturedBody.Position = 0;
        var responseText = await new StreamReader(capturedBody).ReadToEndAsync();
        capturedBody.Position = 0;
        await capturedBody.CopyToAsync(originalBody);

        await idempotencyStore.StoreAsync(storeKey, new IdempotencyRecord(bodyHash, context.Response.StatusCode, responseText), TimeSpan.FromHours(24), context.RequestAborted);
    }

    private static bool IsIdempotentRoute(HttpRequest request)
    {
        if (request.Method != HttpMethods.Post)
        {
            return false;
        }
        var path = request.Path.Value ?? string.Empty;
        return path == "/api/v1/shipments" || path == "/api/v1/pickups" || ShipmentCancelPattern.IsMatch(path);
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = new { code, message } }));
    }
}
