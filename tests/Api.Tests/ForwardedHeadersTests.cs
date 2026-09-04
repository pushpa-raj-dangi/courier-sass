using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

// Covers Frontend Phase F11's design spec §4.2: once public tracking traffic routes through the
// Next.js BFF, IpRateLimitFilter must key on the real visitor's IP (from X-Forwarded-For), not
// the BFF's own connection IP — but ONLY when the immediate connection is a configured trusted
// proxy, never unconditionally (an untrusted caller could otherwise spoof X-Forwarded-For to
// bypass the per-IP limit entirely).
public class ForwardedHeadersTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ForwardedHeadersTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // Same idiom as PublicTrackingEndpointTests.FixedRemoteIpStartupFilter — fakes the raw
    // socket-level RemoteIpAddress TestServer would otherwise leave unset, so this test can
    // control which address ForwardedHeadersMiddleware evaluates against KnownNetworks.
    private sealed class FixedRemoteIpStartupFilter : IStartupFilter
    {
        private readonly IPAddress _ip;
        public FixedRemoteIpStartupFilter(IPAddress ip) => _ip = ip;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = _ip;
                await nextMiddleware();
            });
            next(app);
        };
    }

    private WebApplicationFactory<Program> CreateFactory(int limit, string knownNetwork, IPAddress immediateIp) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:PublicTrackingRequestsPerMinute"] = limit.ToString(),
                ["ForwardedHeaders:KnownNetworks:0"] = knownNetwork
            }));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new FixedRemoteIpStartupFilter(immediateIp)));
        });

    [Fact]
    public async Task TrustedProxyImmediateIp_XForwardedForHeader_RateLimitsOnForwardedIp_NotImmediateIp()
    {
        // The BFF's own address, inside the configured trusted network.
        var bffIp = IPAddress.Parse("10.5.5.5");
        using var scopedFactory = CreateFactory(limit: 2, knownNetwork: "10.0.0.0/8", immediateIp: bffIp);

        var visitorA = $"203.0.113.{Random.Shared.Next(1, 254)}";
        var clientA = scopedFactory.CreateClient();
        clientA.DefaultRequestHeaders.Add("X-Forwarded-For", visitorA);
        var awb = $"FWD-A-{Guid.NewGuid():N}";

        // Two requests from "visitor A" (as seen via X-Forwarded-For) exhaust visitor A's own
        // bucket...
        await clientA.GetAsync($"/track/{awb}");
        await clientA.GetAsync($"/track/{awb}");
        var thirdFromA = await clientA.GetAsync($"/track/{awb}");
        Assert.Equal(HttpStatusCode.TooManyRequests, thirdFromA.StatusCode);

        // ...but a DIFFERENT visitor, forwarded through the SAME BFF connection IP, gets its own
        // fresh bucket — proving the limiter partitioned on the forwarded IP, not the shared BFF
        // connection IP (the exact bug this fix closes).
        var visitorB = $"203.0.113.{Random.Shared.Next(1, 254)}";
        var clientB = scopedFactory.CreateClient();
        clientB.DefaultRequestHeaders.Add("X-Forwarded-For", visitorB);
        var freshForB = await clientB.GetAsync($"/track/{awb}");
        Assert.Equal(HttpStatusCode.NotFound, freshForB.StatusCode);
    }

    [Fact]
    public async Task UntrustedImmediateIp_XForwardedForHeader_IsIgnored_CannotBypassLimit()
    {
        // NOT inside the configured trusted network — simulates a caller hitting the backend
        // directly, bypassing the BFF entirely.
        var untrustedIp = IPAddress.Parse($"198.51.100.{Random.Shared.Next(1, 254)}");
        using var scopedFactory = CreateFactory(limit: 2, knownNetwork: "10.0.0.0/8", immediateIp: untrustedIp);
        var awb = $"FWD-UNTRUSTED-{Guid.NewGuid():N}";

        // Each request claims to be a DIFFERENT visitor via a spoofed X-Forwarded-For. If the
        // untrusted immediate connection were (incorrectly) allowed to set the trusted IP via
        // this header, each request would land in its own fresh bucket and none would be
        // limited. Since the header must be ignored for an untrusted immediate IP, all three
        // still share the same real (untrusted) IP's bucket and the limit trips on request 3.
        for (var i = 0; i < 2; i++)
        {
            var client = scopedFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"203.0.113.{i}");
            var response = await client.GetAsync($"/track/{awb}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var thirdClient = scopedFactory.CreateClient();
        thirdClient.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.99");
        var third = await thirdClient.GetAsync($"/track/{awb}");
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }
}
