using System.Security.Claims;
using System.Text;
using Courier.Api.Middleware;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Courier.Api.Tests;

// Unit-level (not WebApplicationFactory-based) test targeting IdempotencyMiddleware.InvokeAsync
// directly, mirroring ExceptionHandlingMiddlewareTests.cs's convention — constructs a
// DefaultHttpContext by hand and calls InvokeAsync with a RequestDelegate that throws, so it can
// assert on stream identity/disposal state that a full HTTP round-trip through
// WebApplicationFactory can't observe once ExceptionHandlingMiddleware has already turned the
// exception into a 500 response.
public class IdempotencyMiddlewareTests
{
    private class NullIdempotencyStore : IIdempotencyStore
    {
        public Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<IdempotencyRecord?>(null);

        public Task StoreAsync(string key, IdempotencyRecord record, TimeSpan ttl, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("StoreAsync must not be called on the exception path — a failed request must never be cached as a stored idempotent response.");
    }

    // Proves the fix: if the downstream handler (_next) throws, IdempotencyMiddleware must still
    // restore context.Response.Body to the original stream before the exception propagates.
    // Without the try/finally, the response-capturing `await using var capturedBody = new
    // MemoryStream();` gets disposed by the async state machine's unwind while context.Response.Body
    // is still pointing at it, so ExceptionHandlingMiddleware (earlier in the real pipeline) then
    // hits ObjectDisposedException trying to write its JSON error envelope instead of producing the
    // intended 500 response.
    [Fact]
    public async Task InvokeAsync_WhenNextThrows_RestoresOriginalResponseBodyStreamBeforePropagating()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/shipments";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        context.Request.Headers["Idempotency-Key"] = "exception-safety-key";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("merchant_id", Guid.NewGuid().ToString()) }, "TestAuth"));

        var originalResponseBody = new MemoryStream();
        context.Response.Body = originalResponseBody;

        var middleware = new IdempotencyMiddleware(_ => throw new InvalidOperationException("boom"));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context, new NullIdempotencyStore()));
        Assert.Equal("boom", thrown.Message);

        // The core assertion: Response.Body must be the original stream reference again, not the
        // (now-disposed) captured buffer.
        Assert.Same(originalResponseBody, context.Response.Body);

        // And the restored stream must actually still be usable — this is what would throw
        // ObjectDisposedException in production if the restore hadn't happened.
        var probe = Encoding.UTF8.GetBytes("still writable");
        await context.Response.Body.WriteAsync(probe);
        Assert.Equal(probe.Length, originalResponseBody.Length);
    }
}
