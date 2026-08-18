using System.Net;
using System.Text.Json;
using Courier.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courier.Api.Tests;

public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenNextThrows_Writes500ErrorEnvelope()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(
            next: _ => throw new InvalidOperationException("boom"),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var json = JsonDocument.Parse(body);
        var error = json.RootElement.GetProperty("error");

        Assert.Equal("INTERNAL_SERVER_ERROR", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(error.GetProperty("traceId").GetString()));
    }
}
