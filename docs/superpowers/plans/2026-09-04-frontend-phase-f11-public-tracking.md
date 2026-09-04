# Frontend Phase F11 — Public Tracking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a standalone, unauthenticated `/track` (AWB search) + `/track/[awb]` (results timeline) surface over the existing backend `GET /track/{awb}` endpoint, after closing two real backend gaps found while inspecting it: `PublicTrackingStatus` serializing as a raw integer, and the IP rate limiter having no reverse-proxy awareness (which would collapse all public-tracking traffic into one shared bucket once it routes through the Next.js BFF).

**Architecture:** Two backend fixes land first (enum serialization, `ForwardedHeadersMiddleware` wired from configuration), then a thin unauthenticated BFF route (`app/api/track/[awb]/route.ts`) proxies to the backend, then the frontend feature (`features/tracking/`) adds a dedicated fetcher/hook, a search page, and a results page built entirely from existing shadcn/ui primitives — no new dependencies anywhere.

**Tech Stack:** ASP.NET Core minimal APIs, xUnit, `WebApplicationFactory` integration tests (backend); Next.js App Router, TanStack Query, react-hook-form + Zod, shadcn/ui, Vitest + Testing Library (frontend).

**Spec:** `docs/superpowers/specs/2026-09-04-frontend-phase-f11-public-tracking-design.md`

## Global Constraints

- Every new frontend control must be built from shadcn/ui primitives already present in `src/web/src/components/ui/` — never a raw HTML form/interaction primitive (`STATUS: standing project convention`).
- This phase's pages are standalone public routes, not a Sheet-driven detail/create view — the "detail views are always a Sheet" convention does not apply here (there is no authenticated app-shell to open a Sheet inside).
- Never hardcode a trusted-proxy list in code — `ForwardedHeaders:KnownProxies`/`KnownNetworks` must be configuration-driven (spec §4.2), and `appsettings.json` (the production baseline) intentionally gets no entry, matching this file's existing pattern of omitting `RateLimiting` entirely and relying on code defaults.
- `PublicTrackingStatus` serializes as its member name (string), not an integer, matching every other API-facing enum fixed in prior phases.
- No new npm or NuGet package references anywhere in this plan.

---

### Task 1: Backend — `PublicTrackingStatus` JSON serialization fix

**Files:**
- Modify: `src/Modules/Shipments/Application/PublicTrackingDtos.cs`
- Test: Create `tests/Shipments.Tests/PublicTrackingStatusSerializationTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `PublicTrackingStatus` now serializes/deserializes as its member name string (e.g. `"InTransit"`), consumed by Task 3's BFF route and Task 4's frontend types as the wire contract.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Shipments.Tests/PublicTrackingStatusSerializationTests.cs
using System.Text.Json;
using Courier.Shipments.Application;
using Xunit;

namespace Courier.Shipments.Tests;

public class PublicTrackingStatusSerializationTests
{
    [Fact]
    public void SerializesAsMemberName_NotRawInteger()
    {
        var json = JsonSerializer.Serialize(PublicTrackingStatus.InTransit);

        Assert.Equal("\"InTransit\"", json);
    }

    [Fact]
    public void RoundTrips_FromMemberNameString()
    {
        var value = JsonSerializer.Deserialize<PublicTrackingStatus>("\"Delivered\"");

        Assert.Equal(PublicTrackingStatus.Delivered, value);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter "FullyQualifiedName~PublicTrackingStatusSerializationTests"`
Expected: FAIL — `SerializesAsMemberName_NotRawInteger` fails because the actual JSON is `"2"` (raw integer), not `"InTransit"`.

- [ ] **Step 3: Add the converter attribute**

In `src/Modules/Shipments/Application/PublicTrackingDtos.cs`, add the missing `using` and attribute:

```csharp
using System.Text.Json.Serialization;

namespace Courier.Shipments.Application;

// CLAUDE.md §24's safe subset only: a status label, a timestamp, and an optional city/hub-level
// location. No branch ID, address, coordinates, staff/rider identity, notes, financial field, or
// recipient PII beyond what the requester (holder of the AWB) already possesses.
// [JsonConverter(typeof(JsonStringEnumConverter))] — same recurring fix as ShipmentStatus,
// BranchType, ManifestStatus/ManifestPurpose, the three Ndr enums, and the three Cod enums: no
// global JsonStringEnumConverter is registered for the API, so every enum serializes as a raw
// integer unless individually opted in.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PublicTrackingStatus
{
    Received,
    PickedUp,
    InTransit,
    AtDestinationBranch,
    OutForDelivery,
    Delivered,
    DeliveryAttemptUnsuccessful,
    Cancelled
}

public record PublicTrackingEvent(PublicTrackingStatus Status, DateTime Timestamp, string? Location);

public record PublicTrackingResponse(string Awb, PublicTrackingStatus CurrentStatus, List<PublicTrackingEvent> Events);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter "FullyQualifiedName~PublicTrackingStatusSerializationTests"`
Expected: PASS (both tests).

- [ ] **Step 5: Run the full Shipments and Api test suites to check for regressions**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj && dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~PublicTracking"`
Expected: PASS — `PublicTrackingEndpointTests` already asserts on `PublicTrackingStatus` enum values via `JsonSerializer.Deserialize<PublicTrackingResponse>` with `PropertyNameCaseInsensitive = true`, which works identically whether the enum is int- or string-encoded, so no existing test breaks.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Shipments/Application/PublicTrackingDtos.cs tests/Shipments.Tests/PublicTrackingStatusSerializationTests.cs
git commit -m "fix(shipments): serialize PublicTrackingStatus as a string, not a raw integer"
```

---

### Task 2: Backend — `ForwardedHeaders` trusted-proxy configuration

**Files:**
- Create: `src/Api/ForwardedHeadersConfiguration.cs`
- Modify: `src/Api/Program.cs`
- Modify: `src/Api/appsettings.Development.json`
- Test: Create `tests/Api.Tests/ForwardedHeadersTests.cs`

**Interfaces:**
- Consumes: `IConfiguration` sections `ForwardedHeaders:KnownProxies` (string array of IPs) and `ForwardedHeaders:KnownNetworks` (string array of `"ip/prefixLength"` CIDR strings).
- Produces: `Courier.Api.ForwardedHeadersConfiguration.Build(IConfiguration configuration) -> Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersOptions`, wired into the pipeline via `app.UseForwardedHeaders(...)` before any other request-handling middleware. After this task, `IpRateLimitFilter` (unchanged) sees the real visitor IP whenever the immediate connection is a configured trusted proxy and the request carries `X-Forwarded-For`; Task 3's BFF route is the intended source of that header in production.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Api.Tests/ForwardedHeadersTests.cs
using System.Net;
using Microsoft.AspNetCore.Builder;
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~ForwardedHeadersTests"`
Expected: FAIL — `ForwardedHeadersMiddleware` isn't registered yet, so `X-Forwarded-For` is never honored; `TrustedProxyImmediateIp_...` fails because visitor B's request also lands in the (still-shared) BFF-IP bucket and returns 429 instead of 404.

- [ ] **Step 3: Create the configuration helper**

```csharp
// src/Api/ForwardedHeadersConfiguration.cs
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

        foreach (var network in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
        {
            var parts = network.Split('/');
            options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse(parts[0]), int.Parse(parts[1])));
        }

        return options;
    }
}
```

- [ ] **Step 4: Wire it into `Program.cs`**

In `src/Api/Program.cs`, right before the existing `app.UseSerilogRequestLogging(...)` call (so it runs before anything else that might care about `RemoteIpAddress`), insert:

```csharp
// Must run before every other request-handling middleware — IpRateLimitFilter (on
// /track/{awb}) reads Connection.RemoteIpAddress, which this middleware corrects from
// X-Forwarded-For only when the immediate connection is a configured trusted proxy. See
// ForwardedHeadersConfiguration.cs.
app.UseForwardedHeaders(Courier.Api.ForwardedHeadersConfiguration.Build(app.Configuration));

```

The surrounding code becomes:

```csharp
app.UseForwardedHeaders(Courier.Api.ForwardedHeadersConfiguration.Build(app.Configuration));

// Explicit Logger, not the default (options.Logger left null, which falls back to the static
// Serilog.Log.Logger): UseSerilog(...) above is configured with preserveStaticLogger: true, so it
// no longer assigns Log.Logger as a side effect — that static field stays at Serilog's silent
// no-op default. Resolving this host's own Serilog.ILogger from DI (registered by the 3-arg
// UseSerilog(...) overload's internal AddSerilog call) and passing it explicitly keeps
// UseSerilogRequestLogging's per-request completion log wired to the real configured logger
// regardless of the static field's state.
app.UseSerilogRequestLogging(options =>
{
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
});
```

- [ ] **Step 5: Add the local dev default to `appsettings.Development.json`**

In `src/Api/appsettings.Development.json`, add a `ForwardedHeaders` section right after `RateLimiting` (production's `appsettings.json` deliberately gets no entry here, matching its existing omission of `RateLimiting` entirely):

```json
  "RateLimiting": {
    "ApiKeyRequestsPerMinute": 60,
    "PublicTrackingRequestsPerMinute": 30
  },
  "ForwardedHeaders": {
    "KnownNetworks": ["172.16.0.0/12", "192.168.0.0/16"]
  },
  "Frontend": {
    "BaseUrl": "http://localhost:3000"
  }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~ForwardedHeadersTests"`
Expected: PASS (both tests).

- [ ] **Step 7: Run the full Api test suite to check for regressions**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: PASS — in particular `PublicTrackingEndpointTests.GetTrack_ExceedsConfiguredIpLimit_ThirdRequestReturns429WithRetryAfter` and `RateLimitingTests` send no `X-Forwarded-For` header and configure no trusted network, so `ForwardedHeadersMiddleware` finds nothing to act on and `RemoteIpAddress` stays exactly as each test's own `FixedRemoteIpStartupFilter` set it — unchanged behavior.

- [ ] **Step 8: Commit**

```bash
git add src/Api/ForwardedHeadersConfiguration.cs src/Api/Program.cs src/Api/appsettings.Development.json tests/Api.Tests/ForwardedHeadersTests.cs
git commit -m "fix(api): trust X-Forwarded-For only from a configured proxy, closing the public-tracking shared-IP rate-limit gap"
```

---

### Task 3: Frontend — public tracking BFF route

**Files:**
- Create: `src/web/src/app/api/track/[awb]/route.ts`
- Test: Create `src/web/src/app/api/track/[awb]/route.test.ts`

**Interfaces:**
- Consumes: `getUpstreamApiUrl()` from `src/web/src/lib/env.ts`.
- Produces: `GET /api/track/{awb}` — pass-through of the backend's `/track/{awb}` status/body, forwarding an inbound `x-forwarded-for` header verbatim when present. Consumed by Task 4's `useTrackShipment` hook.

- [ ] **Step 1: Write the failing tests**

```ts
// src/web/src/app/api/track/[awb]/route.test.ts
import { describe, expect, it, vi, beforeEach } from "vitest";

vi.mock("@/lib/env", () => ({ getUpstreamApiUrl: () => "http://api.internal" }));

import { GET } from "./route";

function makeRequest(headers: Record<string, string> = {}) {
  return { headers: new Headers(headers) } as unknown as Request;
}

describe("GET /api/track/[awb]", () => {
  beforeEach(() => {
    global.fetch = vi.fn();
  });

  it("proxies the backend response without any auth header", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => ({ awb: "AWB1", currentStatus: "Delivered", events: [] }),
    });

    const response = await GET(makeRequest() as never, { params: { awb: "AWB1" } });

    expect(global.fetch).toHaveBeenCalledWith(
      "http://api.internal/track/AWB1",
      expect.objectContaining({ headers: {}, cache: "no-store" })
    );
    expect(response.status).toBe(200);
  });

  it("forwards an inbound X-Forwarded-For header to the backend", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => ({ awb: "AWB1", currentStatus: "Delivered", events: [] }),
    });

    await GET(makeRequest({ "x-forwarded-for": "203.0.113.5" }) as never, { params: { awb: "AWB1" } });

    expect(global.fetch).toHaveBeenCalledWith(
      "http://api.internal/track/AWB1",
      expect.objectContaining({ headers: { "X-Forwarded-For": "203.0.113.5" } })
    );
  });

  it("passes through a 404 with no body", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 404,
      headers: new Headers(),
      json: async () => {
        throw new Error("no body");
      },
    });

    const response = await GET(makeRequest() as never, { params: { awb: "UNKNOWN" } });

    expect(response.status).toBe(404);
  });

  it("passes through a 429 with its Retry-After header", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 429,
      headers: new Headers({ "Retry-After": "30" }),
      json: async () => ({ error: { code: "RATE_LIMIT_EXCEEDED" } }),
    });

    const response = await GET(makeRequest() as never, { params: { awb: "AWB1" } });

    expect(response.status).toBe(429);
    expect(response.headers.get("Retry-After")).toBe("30");
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd src/web && npx vitest run src/app/api/track/\[awb\]/route.test.ts`
Expected: FAIL — `./route` does not exist yet.

- [ ] **Step 3: Write the route**

```ts
// src/web/src/app/api/track/[awb]/route.ts
import { NextResponse } from "next/server";
import { getUpstreamApiUrl } from "@/lib/env";

// Genuinely anonymous — unlike every other BFF route in this codebase, this does NOT go through
// apiFetch (lib/auth/apiFetch.ts), which requires a valid session and throws otherwise. Forwards
// the inbound X-Forwarded-For verbatim (never appended to) when present, so the backend's
// IpRateLimitFilter can key on the real visitor's IP once ForwardedHeadersMiddleware trusts this
// server as a known proxy (see
// docs/superpowers/specs/2026-09-04-frontend-phase-f11-public-tracking-design.md §4.2) — absent
// in local dev with no upstream proxy, which the backend already handles safely.
export async function GET(request: Request, { params }: { params: { awb: string } }) {
  const forwardedFor = request.headers.get("x-forwarded-for");
  const upstreamHeaders: Record<string, string> = {};
  if (forwardedFor) {
    upstreamHeaders["X-Forwarded-For"] = forwardedFor;
  }

  const response = await fetch(`${getUpstreamApiUrl()}/track/${params.awb}`, {
    headers: upstreamHeaders,
    cache: "no-store",
  });
  const body = await response.json().catch(() => null);

  const retryAfter = response.headers.get("Retry-After");
  return NextResponse.json(body, {
    status: response.status,
    headers: retryAfter ? { "Retry-After": retryAfter } : undefined,
  });
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd src/web && npx vitest run src/app/api/track/\[awb\]/route.test.ts`
Expected: PASS (all 4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/app/api/track/\[awb\]/route.ts src/web/src/app/api/track/\[awb\]/route.test.ts
git commit -m "feat(web): add unauthenticated public tracking BFF route"
```

---

### Task 4: Frontend — tracking types + `useTrackShipment` hook

**Files:**
- Create: `src/web/src/features/tracking/types.ts`
- Create: `src/web/src/features/tracking/hooks/useTrackShipment.ts`
- Test: Create `src/web/src/features/tracking/hooks/useTrackShipment.test.tsx`

**Interfaces:**
- Consumes: `GET /api/track/{awb}` from Task 3.
- Produces: `PublicTrackingStatus`, `PublicTrackingEvent`, `PublicTrackingResult` types; `useTrackShipment(awb: string)` (a TanStack Query `useQuery` result over `PublicTrackingResult`); `TrackingNotFoundError`, `TrackingRateLimitedError` (with a `retryAfterSeconds: number` field) — all consumed by Task 7's results page.

- [ ] **Step 1: Write the failing tests**

```tsx
// src/web/src/features/tracking/hooks/useTrackShipment.test.tsx
import { describe, expect, it, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useTrackShipment, TrackingNotFoundError, TrackingRateLimitedError } from "./useTrackShipment";

function wrapper({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useTrackShipment", () => {
  beforeEach(() => {
    global.fetch = vi.fn();
  });

  it("does not fetch when awb is empty", () => {
    renderHook(() => useTrackShipment(""), { wrapper });
    expect(global.fetch).not.toHaveBeenCalled();
  });

  it("returns the tracking result on success", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => ({ awb: "AWB1", currentStatus: "InTransit", events: [] }),
    });

    const { result } = renderHook(() => useTrackShipment("AWB1"), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.currentStatus).toBe("InTransit");
    expect(global.fetch).toHaveBeenCalledWith("/api/track/AWB1");
  });

  it("surfaces a TrackingNotFoundError on 404", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 404,
      headers: new Headers(),
      json: async () => null,
    });

    const { result } = renderHook(() => useTrackShipment("UNKNOWN"), { wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error).toBeInstanceOf(TrackingNotFoundError);
  });

  it("surfaces a TrackingRateLimitedError with the Retry-After value on 429", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 429,
      headers: new Headers({ "Retry-After": "42" }),
      json: async () => ({ error: { code: "RATE_LIMIT_EXCEEDED" } }),
    });

    const { result } = renderHook(() => useTrackShipment("AWB1"), { wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error).toBeInstanceOf(TrackingRateLimitedError);
    expect((result.current.error as TrackingRateLimitedError).retryAfterSeconds).toBe(42);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd src/web && npx vitest run src/features/tracking/hooks/useTrackShipment.test.tsx`
Expected: FAIL — neither `./useTrackShipment` nor `../types` exist yet.

- [ ] **Step 3: Write the types**

```ts
// src/web/src/features/tracking/types.ts
export type PublicTrackingStatus =
  | "Received"
  | "PickedUp"
  | "InTransit"
  | "AtDestinationBranch"
  | "OutForDelivery"
  | "Delivered"
  | "DeliveryAttemptUnsuccessful"
  | "Cancelled";

export interface PublicTrackingEvent {
  status: PublicTrackingStatus;
  timestamp: string;
  location: string | null;
}

export interface PublicTrackingResult {
  awb: string;
  currentStatus: PublicTrackingStatus;
  events: PublicTrackingEvent[];
}
```

- [ ] **Step 4: Write the hook**

```ts
// src/web/src/features/tracking/hooks/useTrackShipment.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import type { PublicTrackingResult } from "@/features/tracking/types";

// A dedicated fetcher, not the shared clientFetch (lib/api/clientFetch.ts): clientFetch
// collapses every non-OK response into a single Error carrying only a stringified message,
// discarding both the HTTP status and any response headers. Public tracking genuinely needs
// both — a distinct 404 vs. 429 UI state, and the 429's real Retry-After header value (design
// spec §5.4) — so this hook talks to the BFF route directly instead.
export class TrackingNotFoundError extends Error {
  constructor() {
    super("No shipment found for this AWB.");
    this.name = "TrackingNotFoundError";
  }
}

export class TrackingRateLimitedError extends Error {
  constructor(public readonly retryAfterSeconds: number) {
    super("Too many lookups.");
    this.name = "TrackingRateLimitedError";
  }
}

async function fetchTracking(awb: string): Promise<PublicTrackingResult> {
  const response = await fetch(`/api/track/${encodeURIComponent(awb)}`);
  if (response.status === 404) {
    throw new TrackingNotFoundError();
  }
  if (response.status === 429) {
    const retryAfter = Number(response.headers.get("Retry-After") ?? "60");
    throw new TrackingRateLimitedError(Number.isFinite(retryAfter) ? retryAfter : 60);
  }
  if (!response.ok) {
    throw new Error("TRACKING_REQUEST_FAILED");
  }
  return response.json();
}

const TERMINAL_STATUSES = new Set<string>(["Delivered", "Cancelled"]);

export function useTrackShipment(awb: string) {
  return useQuery({
    queryKey: ["track", awb],
    queryFn: () => fetchTracking(awb),
    enabled: awb !== "",
    retry: false,
    refetchInterval: (query) => {
      const data = query.state.data;
      if (!data || TERMINAL_STATUSES.has(data.currentStatus)) {
        return false;
      }
      return 15000;
    },
  });
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd src/web && npx vitest run src/features/tracking/hooks/useTrackShipment.test.tsx`
Expected: PASS (all 4 tests).

- [ ] **Step 6: Commit**

```bash
git add src/web/src/features/tracking/types.ts src/web/src/features/tracking/hooks/useTrackShipment.ts src/web/src/features/tracking/hooks/useTrackShipment.test.tsx
git commit -m "feat(web): add tracking types and useTrackShipment hook"
```

---

### Task 5: Frontend — `statusConfig.ts` Public namespace entries

**Files:**
- Modify: `src/web/src/components/status-badge/statusConfig.ts`
- Test: Create `src/web/src/components/status-badge/statusConfig.test.ts`

**Interfaces:**
- Consumes: nothing new.
- Produces: `resolveStatusConfig(status, "Public")` now resolves every `PublicTrackingStatus` member to its own label/token, consumed by Task 7's `TrackingTimeline` and results page via the existing `StatusBadge` component.

- [ ] **Step 1: Write the failing test**

```ts
// src/web/src/components/status-badge/statusConfig.test.ts
import { describe, expect, it } from "vitest";
import { resolveStatusConfig } from "./statusConfig";

describe("resolveStatusConfig — Public namespace", () => {
  it("resolves PublicTrackingStatus members to their own label, not ShipmentStatus's", () => {
    expect(resolveStatusConfig("AtDestinationBranch", "Public").label).toBe("At Destination Hub");
    expect(resolveStatusConfig("AtDestinationBranch").label).toBe("In Transit");
  });

  it("resolves every PublicTrackingStatus member to a real config entry", () => {
    const members = [
      "Received",
      "PickedUp",
      "InTransit",
      "AtDestinationBranch",
      "OutForDelivery",
      "Delivered",
      "DeliveryAttemptUnsuccessful",
      "Cancelled",
    ];
    for (const member of members) {
      const config = resolveStatusConfig(member, "Public");
      expect(config.label).not.toBe(member);
    }
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npx vitest run src/components/status-badge/statusConfig.test.ts`
Expected: FAIL — no `"Public.*"` entries exist yet, so `resolveStatusConfig` falls back to `{ label: status, token: "status-cancelled" }`, making `config.label === member` for every case.

- [ ] **Step 3: Add the Public namespace entries**

In `src/web/src/components/status-badge/statusConfig.ts`, add this block immediately before the closing `};` of `STATUS_CONFIG` (after the existing `Kyc.*` entries):

```ts
  // PublicTrackingStatus — the anonymous /track/{awb} surface (Frontend Phase F11). Namespaced
  // ("Public.X") on the same principle as every other multi-enum status set in this file, even
  // though most member names would otherwise collide outright with ShipmentStatus's own keys
  // above (e.g. "PickedUp", "AtDestinationBranch") and would silently inherit that internal-
  // facing enum's mapping instead of the public-facing one intended here.
  "Public.Received": { label: "Received", token: "status-created" },
  "Public.PickedUp": { label: "Picked Up", token: "status-in-transit" },
  "Public.InTransit": { label: "In Transit", token: "status-in-transit" },
  "Public.AtDestinationBranch": { label: "At Destination Hub", token: "status-in-transit" },
  "Public.OutForDelivery": { label: "Out for Delivery", token: "status-out-for-delivery" },
  "Public.Delivered": { label: "Delivered", token: "status-delivered" },
  "Public.DeliveryAttemptUnsuccessful": { label: "Delivery Attempt Unsuccessful", token: "status-ndr" },
  "Public.Cancelled": { label: "Cancelled", token: "status-cancelled" },
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/web && npx vitest run src/components/status-badge/statusConfig.test.ts`
Expected: PASS (both tests).

- [ ] **Step 5: Run the StatusBadge suite to check for regressions**

Run: `cd src/web && npx vitest run src/components/status-badge/StatusBadge.test.tsx`
Expected: PASS — unchanged, since no existing key was modified, only new ones added.

- [ ] **Step 6: Commit**

```bash
git add src/web/src/components/status-badge/statusConfig.ts src/web/src/components/status-badge/statusConfig.test.ts
git commit -m "feat(web): add Public.* status config entries for public tracking"
```

---

### Task 6: Frontend — `/track` search page

**Files:**
- Create: `src/web/src/features/tracking/schema.ts`
- Create: `src/web/src/features/tracking/TrackingSearchForm.tsx`
- Create: `src/web/src/features/tracking/TrackingSearchForm.test.tsx`
- Create: `src/web/src/app/track/page.tsx`

**Interfaces:**
- Consumes: nothing new (pure client-side form + navigation).
- Produces: `/track` route rendering `TrackingSearchForm`, which navigates to `/track/{awb}` on submit — consumed by Task 7's results page as its entry point.

- [ ] **Step 1: Write the failing test**

```tsx
// src/web/src/features/tracking/TrackingSearchForm.test.tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { TrackingSearchForm } from "./TrackingSearchForm";

const { pushMock } = vi.hoisted(() => ({ pushMock: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: pushMock }) }));

describe("TrackingSearchForm", () => {
  it("shows a validation error for an empty submit", async () => {
    render(<TrackingSearchForm />);

    fireEvent.click(screen.getByRole("button", { name: /track/i }));

    expect(await screen.findByText(/enter an awb number/i)).toBeInTheDocument();
  });

  it("navigates to the AWB's results page on submit", async () => {
    render(<TrackingSearchForm />);

    fireEvent.change(screen.getByLabelText(/awb number/i), { target: { value: "AWB123" } });
    fireEvent.click(screen.getByRole("button", { name: /track/i }));

    await waitFor(() => expect(pushMock).toHaveBeenCalledWith("/track/AWB123"));
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npx vitest run src/features/tracking/TrackingSearchForm.test.tsx`
Expected: FAIL — neither `./TrackingSearchForm` nor `./schema` exist yet.

- [ ] **Step 3: Write the schema**

```ts
// src/web/src/features/tracking/schema.ts
import { z } from "zod";

export const trackingSearchSchema = z.object({
  awb: z.string().trim().min(1, "Enter an AWB number."),
});

export type TrackingSearchFormValues = z.infer<typeof trackingSearchSchema>;
```

- [ ] **Step 4: Write the form**

```tsx
// src/web/src/features/tracking/TrackingSearchForm.tsx
"use client";

import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { AlertCircle, ArrowRight, PackageSearch } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FieldError } from "@/components/ui/field-error";
import { trackingSearchSchema, type TrackingSearchFormValues } from "@/features/tracking/schema";

export function TrackingSearchForm() {
  const router = useRouter();
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<TrackingSearchFormValues>({ resolver: zodResolver(trackingSearchSchema) });

  function onSubmit(values: TrackingSearchFormValues) {
    router.push(`/track/${encodeURIComponent(values.awb.trim())}`);
  }

  return (
    <Card className="w-full max-w-sm shadow-xl shadow-primary/5">
      <CardHeader>
        <CardTitle className="font-display text-xl">Track your shipment</CardTitle>
        <CardDescription>Enter your AWB number to see its current status.</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="awb">AWB Number</Label>
            <div className="relative">
              <PackageSearch
                className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                strokeWidth={2}
              />
              <Input
                id="awb"
                type="text"
                autoComplete="off"
                className="pl-9"
                aria-invalid={!!errors.awb}
                aria-describedby={errors.awb ? "awb-error" : undefined}
                {...register("awb")}
              />
            </div>
            {errors.awb && (
              <FieldError id="awb-error" className="flex items-center gap-1.5">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.awb.message}
              </FieldError>
            )}
          </div>
          <Button type="submit" className="group w-full" disabled={isSubmitting}>
            Track
            <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" strokeWidth={2} />
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 5: Write the page**

```tsx
// src/web/src/app/track/page.tsx
import { Package } from "lucide-react";
import { TrackingSearchForm } from "@/features/tracking/TrackingSearchForm";

export default function TrackPage() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 bg-background p-4">
      <div className="flex flex-col items-center gap-2">
        <span className="flex h-11 w-11 items-center justify-center rounded-lg bg-primary">
          <Package className="h-5 w-5 text-primary-foreground" strokeWidth={2} />
        </span>
        <span className="font-display text-lg font-semibold tracking-tight">Courier Platform</span>
      </div>
      <TrackingSearchForm />
    </main>
  );
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `cd src/web && npx vitest run src/features/tracking/TrackingSearchForm.test.tsx`
Expected: PASS (both tests).

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/tracking/schema.ts src/web/src/features/tracking/TrackingSearchForm.tsx src/web/src/features/tracking/TrackingSearchForm.test.tsx src/web/src/app/track/page.tsx
git commit -m "feat(web): add /track public AWB search page"
```

---

### Task 7: Frontend — `/track/[awb]` results page

**Files:**
- Create: `src/web/src/features/tracking/components/TrackingTimeline.tsx`
- Create: `src/web/src/features/tracking/components/TrackingTimeline.test.tsx`
- Create: `src/web/src/app/track/[awb]/page.tsx`
- Create: `src/web/src/app/track/[awb]/page.test.tsx`

**Interfaces:**
- Consumes: `useTrackShipment` (Task 4), `resolveStatusConfig`/`StatusBadge` with the `"Public"` namespace (Task 5).
- Produces: the `/track/[awb]` route — this phase's final user-facing deliverable, no further tasks consume it.

- [ ] **Step 1: Write the failing `TrackingTimeline` test**

```tsx
// src/web/src/features/tracking/components/TrackingTimeline.test.tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { TrackingTimeline } from "./TrackingTimeline";
import type { PublicTrackingEvent } from "@/features/tracking/types";

describe("TrackingTimeline", () => {
  it("renders one entry per event with its label and location", () => {
    const events: PublicTrackingEvent[] = [
      { status: "Received", timestamp: "2026-01-01T10:00:00Z", location: "Kathmandu" },
      { status: "InTransit", timestamp: "2026-01-02T10:00:00Z", location: null },
    ];

    render(<TrackingTimeline events={events} />);

    expect(screen.getByText("Received")).toBeInTheDocument();
    expect(screen.getByText("Kathmandu")).toBeInTheDocument();
    expect(screen.getByText("In Transit")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npx vitest run src/features/tracking/components/TrackingTimeline.test.tsx`
Expected: FAIL — `./TrackingTimeline` does not exist yet.

- [ ] **Step 3: Write `TrackingTimeline`**

```tsx
// src/web/src/features/tracking/components/TrackingTimeline.tsx
import { format, parseISO } from "date-fns";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import type { PublicTrackingEvent } from "@/features/tracking/types";

// Events arrive oldest-first from the backend (PublicTrackingService orders by Timestamp
// ascending) — kept in that order here so the list reads top-to-bottom like a journey, ending
// at the current status closest to the summary badge above it.
export function TrackingTimeline({ events }: { events: PublicTrackingEvent[] }) {
  return (
    <ol className="space-y-0">
      {events.map((event, index) => (
        <li key={`${event.status}-${event.timestamp}`} className="flex gap-4">
          <div className="flex flex-col items-center">
            <span className="mt-1 h-2.5 w-2.5 shrink-0 rounded-full bg-primary" aria-hidden="true" />
            {index < events.length - 1 && <span className="w-px flex-1 bg-border" aria-hidden="true" />}
          </div>
          <div className="pb-6">
            <div className="flex flex-wrap items-center gap-2">
              <StatusBadge status={event.status} namespace="Public" />
              {event.location && <span className="text-sm text-muted-foreground">{event.location}</span>}
            </div>
            <p className="mt-1 text-xs text-muted-foreground">
              {format(parseISO(event.timestamp), "d MMM yyyy, h:mm a")}
            </p>
          </div>
        </li>
      ))}
    </ol>
  );
}
```

- [ ] **Step 4: Run `TrackingTimeline` test to verify it passes**

Run: `cd src/web && npx vitest run src/features/tracking/components/TrackingTimeline.test.tsx`
Expected: PASS.

- [ ] **Step 5: Write the failing results-page test**

```tsx
// src/web/src/app/track/[awb]/page.test.tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import TrackingResultPage from "./page";
import {
  useTrackShipment,
  TrackingNotFoundError,
  TrackingRateLimitedError,
} from "@/features/tracking/hooks/useTrackShipment";

vi.mock("next/navigation", () => ({
  useParams: () => ({ awb: "AWB123" }),
}));

vi.mock("@/features/tracking/hooks/useTrackShipment", async () => {
  const actual = await vi.importActual<typeof import("@/features/tracking/hooks/useTrackShipment")>(
    "@/features/tracking/hooks/useTrackShipment"
  );
  return { ...actual, useTrackShipment: vi.fn() };
});

describe("TrackingResultPage", () => {
  it("shows a loading skeleton while fetching", () => {
    vi.mocked(useTrackShipment).mockReturnValue({
      isLoading: true,
      isError: false,
      data: undefined,
      error: null,
    } as never);

    render(<TrackingResultPage />);

    expect(screen.getByText(/AWB123/)).toBeInTheDocument();
  });

  it("renders the current status and timeline on success", () => {
    vi.mocked(useTrackShipment).mockReturnValue({
      isLoading: false,
      isError: false,
      data: {
        awb: "AWB123",
        currentStatus: "InTransit",
        events: [
          { status: "Received", timestamp: "2026-01-01T10:00:00Z", location: "Kathmandu" },
          { status: "InTransit", timestamp: "2026-01-02T10:00:00Z", location: null },
        ],
      },
      error: null,
    } as never);

    render(<TrackingResultPage />);

    expect(screen.getAllByText("In Transit").length).toBeGreaterThan(0);
    expect(screen.getByText("Kathmandu")).toBeInTheDocument();
  });

  it("shows a not-found message for an unknown AWB", () => {
    vi.mocked(useTrackShipment).mockReturnValue({
      isLoading: false,
      isError: true,
      data: undefined,
      error: new TrackingNotFoundError(),
    } as never);

    render(<TrackingResultPage />);

    expect(screen.getByText(/no shipment found/i)).toBeInTheDocument();
  });

  it("shows a rate-limited message with the retry time", () => {
    vi.mocked(useTrackShipment).mockReturnValue({
      isLoading: false,
      isError: true,
      data: undefined,
      error: new TrackingRateLimitedError(42),
    } as never);

    render(<TrackingResultPage />);

    expect(screen.getByText(/try again in 42 seconds/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 6: Run test to verify it fails**

Run: `cd src/web && npx vitest run src/app/track/\[awb\]/page.test.tsx`
Expected: FAIL — `./page` does not exist yet.

- [ ] **Step 7: Write the results page**

```tsx
// src/web/src/app/track/[awb]/page.tsx
"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { AlertCircle, PackageSearch, Search } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Separator } from "@/components/ui/separator";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { TrackingTimeline } from "@/features/tracking/components/TrackingTimeline";
import {
  useTrackShipment,
  TrackingNotFoundError,
  TrackingRateLimitedError,
} from "@/features/tracking/hooks/useTrackShipment";

export default function TrackingResultPage() {
  const params = useParams<{ awb: string }>();
  const awb = params.awb;
  const { data, isLoading, isError, error } = useTrackShipment(awb);

  return (
    <main className="flex min-h-screen flex-col items-center gap-6 bg-background p-4 py-12">
      <Link href="/track" className="flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground">
        <Search className="h-4 w-4" strokeWidth={2} />
        Track another shipment
      </Link>
      <Card className="w-full max-w-lg shadow-xl shadow-primary/5">
        <CardHeader>
          <CardTitle className="font-display text-xl">AWB {awb}</CardTitle>
        </CardHeader>
        <CardContent>
          {isLoading && (
            <div className="space-y-3">
              <Skeleton className="h-6 w-32" />
              <Skeleton className="h-16 w-full" />
              <Skeleton className="h-16 w-full" />
            </div>
          )}

          {isError && error instanceof TrackingNotFoundError && (
            <div className="flex flex-col items-center gap-2 py-6 text-center">
              <PackageSearch className="h-8 w-8 text-muted-foreground" strokeWidth={1.5} />
              <p className="text-sm text-muted-foreground">No shipment found for this AWB.</p>
            </div>
          )}

          {isError && error instanceof TrackingRateLimitedError && (
            <div className="flex flex-col items-center gap-2 py-6 text-center">
              <AlertCircle className="h-8 w-8 text-muted-foreground" strokeWidth={1.5} />
              <p className="text-sm text-muted-foreground">
                Too many lookups. Try again in {error.retryAfterSeconds} seconds.
              </p>
            </div>
          )}

          {isError && !(error instanceof TrackingNotFoundError) && !(error instanceof TrackingRateLimitedError) && (
            <div className="flex flex-col items-center gap-2 py-6 text-center">
              <AlertCircle className="h-8 w-8 text-muted-foreground" strokeWidth={1.5} />
              <p className="text-sm text-muted-foreground">Something went wrong. Please try again.</p>
            </div>
          )}

          {data && (
            <div className="space-y-4">
              <StatusBadge status={data.currentStatus} namespace="Public" />
              <Separator />
              <TrackingTimeline events={data.events} />
            </div>
          )}
        </CardContent>
      </Card>
    </main>
  );
}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `cd src/web && npx vitest run src/app/track/\[awb\]/page.test.tsx`
Expected: PASS (all 4 tests).

- [ ] **Step 9: Run a visual design pass**

Per design spec §5.5, this is the first genuinely public/customer-facing surface in the product. Invoke the `frontend-design` (or `impeccable`) skill against the running `/track` and `/track/[awb]` pages (`npm run dev` in `src/web`, visit both routes in a browser) and apply its recommendations directly to `TrackingSearchForm.tsx`, `TrackingTimeline.tsx`, and `app/track/[awb]/page.tsx` — keep every underlying shadcn/ui primitive and shared component (`StatusBadge`, `Card`, etc.), adjusting only spacing/typography/visual-treatment details. Re-run the tests from Steps 4 and 8 after any change to confirm they still pass.

- [ ] **Step 10: Run the full frontend suite, lint, and typecheck**

Run: `cd src/web && npm test && npx tsc --noEmit && npm run lint`
Expected: PASS — no regressions in any existing test, no type errors, no lint errors.

- [ ] **Step 11: Commit**

```bash
git add src/web/src/features/tracking/components/TrackingTimeline.tsx src/web/src/features/tracking/components/TrackingTimeline.test.tsx src/web/src/app/track/\[awb\]/page.tsx src/web/src/app/track/\[awb\]/page.test.tsx
git commit -m "feat(web): add /track/[awb] public tracking results page"
```

---

### Task 8: Manual E2E verification + roadmap update

**Files:**
- Modify: `docs/FRONTEND-ROADMAP.md`

**Interfaces:**
- Consumes: the full stack built in Tasks 1-7.
- Produces: a verified, working public tracking surface and an updated roadmap record — the terminal task of this plan.

- [ ] **Step 1: Start the stack**

Run: `docker start courier-postgres-1 courier-redis-1 courier-rabbitmq-1` (reuse the existing shared containers — do not `docker compose up` a second stack), then in separate terminals: `dotnet run --project src/Api` and `cd src/web && npm run dev`.

- [ ] **Step 2: Seed a real shipment through several real status transitions**

Using the running API (via `curl` or the Swagger UI at `http://localhost:5001/swagger`), reuse the exact seeding shape `tests/Api.Tests/PublicTrackingEndpointTests.cs` already establishes: register an organization, seed two branches + a delivery zone pair + a rate card + a merchant + a pickup rider, book a shipment, then drive it through pickup → origin-receive → manifest scan/seal/dispatch/receive (mirroring that test file's `BookAndMoveToDestinationAsync` sequence) so the shipment reaches `AtDestinationBranch`. Note the shipment's AWB from the booking response.

- [ ] **Step 3: Verify the public page in a real browser**

Open `http://localhost:3000/track`, submit the AWB from Step 2, and confirm:
- The results page at `/track/{awb}` shows `AtDestinationBranch`'s public label ("In Transit" for the current-status badge, consistent with `statusConfig.ts`).
- The timeline shows the collapsed sequence (Received, Picked Up, In Transit) with real timestamps, no duplicate consecutive entries.
- View the page source / network response for `/api/track/{awb}` and confirm no `branchId`, `userId`, `riderId`, `notes`, `codAmount`, `deliveryCharge`, or `declaredValue` field appears anywhere in the payload.
- Enter a nonexistent AWB (e.g. `NOPE-DOES-NOT-EXIST`) on `/track` and confirm the results page shows the "No shipment found" state, not a crash.

- [ ] **Step 4: Fix any real bug found in Step 3**

If Step 3 surfaces a genuine defect (backend or frontend), fix it with its own test following this plan's established TDD pattern, then re-verify Step 3, then commit the fix separately with a `fix:` commit message describing exactly what was wrong — do not silently patch without a commit, matching this project's established practice of recording E2E-discovered bugs.

- [ ] **Step 5: Update `docs/FRONTEND-ROADMAP.md`**

Move the Phase F11 entry out of the "Later (not yet planned in detail)" list into its own numbered section, in the same position and following the same structure (Goal, what shipped, any bugs found and fixed, deferred items if any) as the existing `## Phase F10 — Merchant Portal` section immediately above it. Remove the now-completed bullet from the "Later" list, leaving only Phase F12.

- [ ] **Step 6: Commit the roadmap update**

```bash
git add docs/FRONTEND-ROADMAP.md
git commit -m "docs: mark Frontend Phase F11 (Public Tracking) complete"
```
