using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace Courier.Api;

// Builds ForwardedHeadersOptions from configuration rather than hardcoding a trusted-proxy list
// — see docs/superpowers/specs/2026-09-04-frontend-phase-f11-public-tracking-design.md §4.2.
// KnownProxies/KnownNetworks default to empty (ForwardedHeadersOptions already trusts loopback
// out of the box; nothing extra is added), so a production deployment must explicitly configure
// its real load balancer/ingress network — leaving it unconfigured is a safe (if incomplete)
// default, never a silent trust-everyone hole.
public static class ForwardedHeadersConfiguration
{
    public static ForwardedHeadersOptions Build(IConfiguration configuration)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
        };

        foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        // KnownIPNetworks (System.Net.IPNetwork, .NET 8+) rather than the obsolete KnownNetworks
        // (Microsoft.AspNetCore.HttpOverrides.IPNetwork) — the newer type parses "ip/prefixLength"
        // CIDR notation directly, no manual splitting needed.
        foreach (var network in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }

        return options;
    }
}
