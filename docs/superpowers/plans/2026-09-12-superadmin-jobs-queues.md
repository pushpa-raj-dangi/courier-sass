# SuperAdmin Jobs & Queues Monitoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give SuperAdmin two new read-only Platform pages — `/platform/jobs` (Hangfire recurring-job state, live counts, recent failed/succeeded jobs) and `/platform/queues` (RabbitMQ queue depth and consumer health) — with no mutating actions in v1.

**Architecture:** Two independent vertical slices, both following the existing `PlatformInvoicesEndpoints`/`platform-invoices` pattern exactly: an `Application` service behind a small interface (`IHangfireMonitor`, `IRabbitMqManagementClient`) that keeps the real infrastructure (Hangfire's static `JobStorage`, a RabbitMQ management-API `HttpClient`) out of anything unit-testable, a `RequireSuperAdmin()`-gated minimal API endpoint, and a Next.js feature folder (types + TanStack Query hook + BFF proxy route + page) registered in `PlatformNav`.

**Tech Stack:** ASP.NET Core minimal APIs, Hangfire.Core (monitoring API only, no new job types), a typed `HttpClient` against the RabbitMQ management plugin's REST API, xUnit (`Platform.Tests` for services, `Api.Tests`/`WebApplicationFactory<Program>` for endpoints); Next.js, TypeScript, TanStack Query, shadcn/ui `DataTable`/`Badge`, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-09-12-superadmin-jobs-queues-design.md`

## Global Constraints

* Read-only for v1 — no retry/requeue/purge endpoints or UI controls. Do not add any mutating action to either page.
* No new RabbitMQ config section. The management-API client derives its base URL and Basic-auth credentials from the existing `ConnectionStrings:RabbitMq` value (`amqp://user:pass@host:5672` → `http://host:15672/`) — never add a parallel `RabbitMqManagement` settings block.
* Recurring jobs / recent jobs / queues are all rendered as a single page each — no `page`/`pageSize` query params on either new endpoint.
* Both pages auto-refresh independently (`refetchInterval: 10000` on each TanStack Query hook) and each must render `ErrorState` on its own failure without affecting the other page.
* Follow existing conventions exactly: `RequireSuperAdmin()` on every new endpoint, the shared `DataTable`/`ColumnDef`/`PagedResult` component for every table, a Zustand store + right-side `Sheet` for the one drill-down this plan has (a failed job's exception), and the inline `StatTile` pattern already used on `/platform/dashboard` for the summary counts — never a new card/table primitive.

---

### Task 1: Jobs Application layer — `IHangfireMonitor` and `PlatformJobsService`

**Files:**
- Create: `src/Modules/Platform/Application/IHangfireMonitor.cs`
- Create: `src/Modules/Platform/Application/PlatformJobsService.cs`
- Test: `tests/Platform.Tests/PlatformJobsServiceTests.cs`

**Interfaces:**
- Produces: `JobsSummary(long Enqueued, long Scheduled, long Processing, long Succeeded, long Failed, long Deleted)`, `RecurringJobSummary(string Id, string? Cron, DateTime? NextExecution, DateTime? LastExecution, string? LastJobState)`, `RecentJobSummary(string JobId, string JobName, string State, DateTimeOffset At, string? ExceptionMessage)`, `JobsOverview(JobsSummary Summary, IReadOnlyList<RecurringJobSummary> RecurringJobs, IReadOnlyList<RecentJobSummary> RecentJobs)`, `IHangfireMonitor` (`GetSummary()`, `GetRecurringJobs()`, `GetRecentJobs(int failedCount, int succeededCount)`), `PlatformJobsService.GetOverviewAsync(CancellationToken)` — consumed by Task 2's endpoint.

- [ ] **Step 1: Write the failing tests**

Create `tests/Platform.Tests/PlatformJobsServiceTests.cs`:

```csharp
using Courier.Platform.Application;
using Xunit;

namespace Courier.Platform.Tests;

public class PlatformJobsServiceTests
{
    private sealed class FakeHangfireMonitor : IHangfireMonitor
    {
        public JobsSummary Summary { get; set; } = new(0, 0, 0, 0, 0, 0);
        public List<RecurringJobSummary> RecurringJobs { get; set; } = new();
        public List<RecentJobSummary> RecentJobs { get; set; } = new();
        public (int FailedCount, int SucceededCount)? LastRecentJobsCall { get; private set; }

        public JobsSummary GetSummary() => Summary;
        public IReadOnlyList<RecurringJobSummary> GetRecurringJobs() => RecurringJobs;
        public IReadOnlyList<RecentJobSummary> GetRecentJobs(int failedCount, int succeededCount)
        {
            LastRecentJobsCall = (failedCount, succeededCount);
            return RecentJobs;
        }
    }

    [Fact]
    public async Task GetOverviewAsync_AssemblesSummaryRecurringAndRecentJobsFromTheMonitor()
    {
        var monitor = new FakeHangfireMonitor
        {
            Summary = new JobsSummary(Enqueued: 3, Scheduled: 1, Processing: 0, Succeeded: 100, Failed: 2, Deleted: 5),
            RecurringJobs = new List<RecurringJobSummary>
            {
                new("outbox-publisher", "*/10 * * * * *", DateTime.UtcNow.AddSeconds(5), DateTime.UtcNow.AddSeconds(-5), "Succeeded"),
            },
            RecentJobs = new List<RecentJobSummary>
            {
                new("job-1", "OutboxPublisherJob", "Succeeded", DateTimeOffset.UtcNow, null),
            },
        };
        var service = new PlatformJobsService(monitor);

        var overview = await service.GetOverviewAsync();

        Assert.Equal(monitor.Summary, overview.Summary);
        Assert.Equal(monitor.RecurringJobs, overview.RecurringJobs);
        Assert.Equal(monitor.RecentJobs, overview.RecentJobs);
    }

    [Fact]
    public async Task GetOverviewAsync_RequestsTop20FailedAndTop20SucceededRecentJobs()
    {
        var monitor = new FakeHangfireMonitor();
        var service = new PlatformJobsService(monitor);

        await service.GetOverviewAsync();

        Assert.Equal((20, 20), monitor.LastRecentJobsCall);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Platform.Tests/Platform.Tests.csproj --filter PlatformJobsServiceTests`
Expected: FAIL — `IHangfireMonitor`, `PlatformJobsService`, `JobsSummary`, `RecurringJobSummary`, `RecentJobSummary`, `JobsOverview` do not exist yet.

- [ ] **Step 3: Create the interface**

Create `src/Modules/Platform/Application/IHangfireMonitor.cs`:

```csharp
namespace Courier.Platform.Application;

public interface IHangfireMonitor
{
    JobsSummary GetSummary();
    IReadOnlyList<RecurringJobSummary> GetRecurringJobs();
    IReadOnlyList<RecentJobSummary> GetRecentJobs(int failedCount, int succeededCount);
}
```

- [ ] **Step 4: Create the records and service**

Create `src/Modules/Platform/Application/PlatformJobsService.cs`:

```csharp
namespace Courier.Platform.Application;

public sealed record JobsSummary(long Enqueued, long Scheduled, long Processing, long Succeeded, long Failed, long Deleted);

public sealed record RecurringJobSummary(string Id, string? Cron, DateTime? NextExecution, DateTime? LastExecution, string? LastJobState);

public sealed record RecentJobSummary(string JobId, string JobName, string State, DateTimeOffset At, string? ExceptionMessage);

public sealed record JobsOverview(JobsSummary Summary, IReadOnlyList<RecurringJobSummary> RecurringJobs, IReadOnlyList<RecentJobSummary> RecentJobs);

public sealed class PlatformJobsService
{
    private readonly IHangfireMonitor _monitor;

    public PlatformJobsService(IHangfireMonitor monitor)
    {
        _monitor = monitor;
    }

    public Task<JobsOverview> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new JobsOverview(_monitor.GetSummary(), _monitor.GetRecurringJobs(), _monitor.GetRecentJobs(20, 20)));
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Platform.Tests/Platform.Tests.csproj --filter PlatformJobsServiceTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Platform/Application/IHangfireMonitor.cs src/Modules/Platform/Application/PlatformJobsService.cs tests/Platform.Tests/PlatformJobsServiceTests.cs
git commit -m "$(cat <<'EOF'
feat(platform): add PlatformJobsService behind an IHangfireMonitor seam

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Rf7EtY8QJ2sgEus5qeEgTh
EOF
)"
```

---

### Task 2: `HangfireMonitor` infrastructure + `GET /api/v1/platform/jobs`

**Files:**
- Modify: `src/Modules/Platform/Platform.csproj`
- Create: `src/Modules/Platform/Infrastructure/HangfireMonitor.cs`
- Create: `src/Modules/Platform/Presentation/PlatformJobsEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Api.Tests/PlatformJobsEndpointsTests.cs`

**Interfaces:**
- Consumes: `IHangfireMonitor`, `JobsOverview`, `PlatformJobsService` (Task 1), `RequireSuperAdmin` (existing, `Courier.SharedKernel`).
- Produces: `GET /api/v1/platform/jobs` — consumed by Task 5's Next.js proxy route.

- [ ] **Step 1: Add the Hangfire.Core package reference**

`Platform.csproj` needs direct access to `Hangfire`/`Hangfire.Common` types for `HangfireMonitor` (project references don't propagate NuGet package references from `Api` down into `Platform` — the same reason `Events.csproj` has its own `RabbitMQ.Client` reference even though `Api` also references it).

```diff
   <ItemGroup>
     <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.11" />
     <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.11" />
     <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.22.0" />
+    <PackageReference Include="Hangfire.Core" Version="1.8.24" />
   </ItemGroup>
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Api.Tests/PlatformJobsEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Platform.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class PlatformJobsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformJobsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> LoginAsSuperAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task GetJobs_WithoutAuth_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/platform/jobs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetJobs_AsSuperAdmin_ReturnsOverviewWithTheKnownRecurringJobs()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsSuperAdminAsync(client));

        var response = await client.GetAsync("/api/v1/platform/jobs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var overview = await response.Content.ReadFromJsonAsync<JobsOverview>();
        Assert.NotNull(overview);
        var recurringIds = overview!.RecurringJobs.Select(r => r.Id).ToList();
        Assert.Contains("outbox-publisher", recurringIds);
        Assert.Contains("webhook-retry", recurringIds);
        Assert.Contains("billing-invoice-generation", recurringIds);
        Assert.Contains("billing-overdue-invoice-suspension", recurringIds);
        Assert.Contains("billing-trial-expiry", recurringIds);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter PlatformJobsEndpointsTests`
Expected: FAIL — 404, since `/api/v1/platform/jobs` doesn't exist yet.

- [ ] **Step 4: Implement `HangfireMonitor`**

Create `src/Modules/Platform/Infrastructure/HangfireMonitor.cs`:

```csharp
using Courier.Platform.Application;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;

namespace Courier.Platform.Infrastructure;

public sealed class HangfireMonitor : IHangfireMonitor
{
    public JobsSummary GetSummary()
    {
        var stats = JobStorage.Current.GetMonitoringApi().GetStatistics();
        return new JobsSummary(stats.Enqueued, stats.Scheduled, stats.Processing, stats.Succeeded, stats.Failed, stats.Deleted);
    }

    public IReadOnlyList<RecurringJobSummary> GetRecurringJobs()
    {
        using var connection = JobStorage.Current.GetConnection();
        return connection.GetRecurringJobs()
            .Select(r => new RecurringJobSummary(r.Id, r.Cron, r.NextExecution, r.LastExecution, r.LastJobState))
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<RecentJobSummary> GetRecentJobs(int failedCount, int succeededCount)
    {
        var monitor = JobStorage.Current.GetMonitoringApi();

        var failed = monitor.FailedJobs(0, failedCount)
            .Select(kv => new RecentJobSummary(kv.Key, JobName(kv.Value.Job), "Failed",
                new DateTimeOffset(kv.Value.FailedAt ?? DateTime.UtcNow, TimeSpan.Zero), kv.Value.ExceptionMessage));

        var succeeded = monitor.SucceededJobs(0, succeededCount)
            .Select(kv => new RecentJobSummary(kv.Key, JobName(kv.Value.Job), "Succeeded",
                new DateTimeOffset(kv.Value.SucceededAt ?? DateTime.UtcNow, TimeSpan.Zero), null));

        return failed.Concat(succeeded).OrderByDescending(j => j.At).ToList();
    }

    // Job.Type can throw JobLoadException when the stored job's assembly-qualified type name can
    // no longer be resolved (e.g. a renamed/removed job class still referenced by old job data)
    // — the Hangfire dashboard itself guards this same access for the same reason.
    private static string JobName(Job? job)
    {
        try
        {
            return job?.Type.Name ?? "Unknown";
        }
        catch (JobLoadException)
        {
            return "Unknown";
        }
    }
}
```

- [ ] **Step 5: Create the endpoint**

Create `src/Modules/Platform/Presentation/PlatformJobsEndpoints.cs`:

```csharp
using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformJobsEndpoints
{
    public static void MapPlatformJobsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/platform/jobs", async (PlatformJobsService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetOverviewAsync(cancellationToken)))
            .WithTags("Platform Jobs")
            .RequireSuperAdmin();
    }
}
```

- [ ] **Step 6: Wire it up in `Program.cs`**

Register `IHangfireMonitor` right after the existing Hangfire server registration:

```diff
 builder.Services.AddHangfireServer();
+builder.Services.AddSingleton<IHangfireMonitor, HangfireMonitor>();
```

Register `PlatformJobsService` alongside the other Platform services:

```diff
 builder.Services.AddScoped<PlatformAuditService>();
+builder.Services.AddScoped<PlatformJobsService>();
```

Map the endpoint near the other Platform maps:

```diff
 app.MapPlatformAuditEndpoints();
+app.MapPlatformJobsEndpoints();
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter PlatformJobsEndpointsTests`
Expected: PASS (requires the dev Postgres container — the same one every other `Api.Tests` integration test already needs — to be running, since Hangfire's storage lives there).

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Platform/Platform.csproj src/Modules/Platform/Infrastructure/HangfireMonitor.cs src/Modules/Platform/Presentation/PlatformJobsEndpoints.cs src/Api/Program.cs tests/Api.Tests/PlatformJobsEndpointsTests.cs
git commit -m "$(cat <<'EOF'
feat(platform): add GET /api/v1/platform/jobs

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Rf7EtY8QJ2sgEus5qeEgTh
EOF
)"
```

---

### Task 3: Queues Application layer — `IRabbitMqManagementClient` and `PlatformQueuesService`

**Files:**
- Create: `src/Modules/Platform/Application/IRabbitMqManagementClient.cs`
- Create: `src/Modules/Platform/Application/PlatformQueuesService.cs`
- Test: `tests/Platform.Tests/PlatformQueuesServiceTests.cs`

**Interfaces:**
- Produces: `QueueSummary(string Name, long Messages, long MessagesReady, long MessagesUnacknowledged, int Consumers, string State)`, `IRabbitMqManagementClient.GetQueuesAsync(CancellationToken)`, `PlatformQueuesService.GetQueuesAsync(CancellationToken)` — consumed by Task 4's endpoint.

- [ ] **Step 1: Write the failing tests**

Create `tests/Platform.Tests/PlatformQueuesServiceTests.cs`:

```csharp
using Courier.Platform.Application;
using Xunit;

namespace Courier.Platform.Tests;

public class PlatformQueuesServiceTests
{
    private sealed class FakeRabbitMqManagementClient : IRabbitMqManagementClient
    {
        public List<QueueSummary> Queues { get; set; } = new();

        public Task<IReadOnlyList<QueueSummary>> GetQueuesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QueueSummary>>(Queues);
    }

    [Fact]
    public async Task GetQueuesAsync_ReturnsExactlyWhatTheManagementClientReturns()
    {
        var client = new FakeRabbitMqManagementClient
        {
            Queues = new List<QueueSummary>
            {
                new("courier.notifications", Messages: 5, MessagesReady: 5, MessagesUnacknowledged: 0, Consumers: 1, State: "running"),
                new("courier.webhooks", Messages: 0, MessagesReady: 0, MessagesUnacknowledged: 0, Consumers: 0, State: "idle"),
            },
        };
        var service = new PlatformQueuesService(client);

        var result = await service.GetQueuesAsync();

        Assert.Equal(client.Queues, result);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Platform.Tests/Platform.Tests.csproj --filter PlatformQueuesServiceTests`
Expected: FAIL — `IRabbitMqManagementClient`, `QueueSummary`, `PlatformQueuesService` do not exist yet.

- [ ] **Step 3: Create the interface**

Create `src/Modules/Platform/Application/IRabbitMqManagementClient.cs`:

```csharp
namespace Courier.Platform.Application;

public interface IRabbitMqManagementClient
{
    Task<IReadOnlyList<QueueSummary>> GetQueuesAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Create the record and service**

Create `src/Modules/Platform/Application/PlatformQueuesService.cs`:

```csharp
namespace Courier.Platform.Application;

public sealed record QueueSummary(string Name, long Messages, long MessagesReady, long MessagesUnacknowledged, int Consumers, string State);

public sealed class PlatformQueuesService
{
    private readonly IRabbitMqManagementClient _client;

    public PlatformQueuesService(IRabbitMqManagementClient client)
    {
        _client = client;
    }

    public Task<IReadOnlyList<QueueSummary>> GetQueuesAsync(CancellationToken cancellationToken = default) =>
        _client.GetQueuesAsync(cancellationToken);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Platform.Tests/Platform.Tests.csproj --filter PlatformQueuesServiceTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Platform/Application/IRabbitMqManagementClient.cs src/Modules/Platform/Application/PlatformQueuesService.cs tests/Platform.Tests/PlatformQueuesServiceTests.cs
git commit -m "$(cat <<'EOF'
feat(platform): add PlatformQueuesService behind an IRabbitMqManagementClient seam

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Rf7EtY8QJ2sgEus5qeEgTh
EOF
)"
```

---

### Task 4: `RabbitMqManagementClient` infrastructure + `GET /api/v1/platform/queues`

**Files:**
- Create: `src/Modules/Platform/Infrastructure/RabbitMqManagementClient.cs`
- Create: `src/Modules/Platform/Presentation/PlatformQueuesEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Api.Tests/PlatformQueuesEndpointsTests.cs`

**Interfaces:**
- Consumes: `IRabbitMqManagementClient`, `QueueSummary`, `PlatformQueuesService` (Task 3), `RequireSuperAdmin` (existing, `Courier.SharedKernel`).
- Produces: `GET /api/v1/platform/queues` — consumed by Task 8's Next.js proxy route.

- [ ] **Step 1: Write the failing tests**

Create `tests/Api.Tests/PlatformQueuesEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Platform.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class PlatformQueuesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformQueuesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> LoginAsSuperAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task GetQueues_WithoutAuth_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/platform/queues");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetQueues_AsSuperAdmin_ReturnsAQueueList()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsSuperAdminAsync(client));

        var response = await client.GetAsync("/api/v1/platform/queues");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var queues = await response.Content.ReadFromJsonAsync<List<QueueSummary>>();
        Assert.NotNull(queues);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter PlatformQueuesEndpointsTests`
Expected: FAIL — 404, since `/api/v1/platform/queues` doesn't exist yet.

- [ ] **Step 3: Implement `RabbitMqManagementClient`**

Create `src/Modules/Platform/Infrastructure/RabbitMqManagementClient.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Courier.Platform.Application;

namespace Courier.Platform.Infrastructure;

public sealed class RabbitMqManagementClient : IRabbitMqManagementClient
{
    private readonly HttpClient _httpClient;

    public RabbitMqManagementClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<QueueSummary>> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        var queues = await _httpClient.GetFromJsonAsync<List<RabbitMqQueueResponse>>("api/queues/%2f", cancellationToken)
            ?? new List<RabbitMqQueueResponse>();

        return queues
            .Select(q => new QueueSummary(q.Name, q.Messages, q.MessagesReady, q.MessagesUnacknowledged, q.Consumers, q.State ?? "unknown"))
            .OrderBy(q => q.Name, StringComparer.Ordinal)
            .ToList();
    }

    private sealed class RabbitMqQueueResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("messages")]
        public long Messages { get; set; }

        [JsonPropertyName("messages_ready")]
        public long MessagesReady { get; set; }

        [JsonPropertyName("messages_unacknowledged")]
        public long MessagesUnacknowledged { get; set; }

        [JsonPropertyName("consumers")]
        public int Consumers { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }
    }
}
```

- [ ] **Step 4: Create the endpoint**

Create `src/Modules/Platform/Presentation/PlatformQueuesEndpoints.cs`:

```csharp
using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformQueuesEndpoints
{
    public static void MapPlatformQueuesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/platform/queues", async (PlatformQueuesService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetQueuesAsync(cancellationToken)))
            .WithTags("Platform Queues")
            .RequireSuperAdmin();
    }
}
```

- [ ] **Step 5: Wire it up in `Program.cs`**

Register the typed HTTP client right after the existing shared `RabbitMQ.Client.IConnection` registration, deriving its base address and Basic-auth header from the existing `ConnectionStrings:RabbitMq` value rather than any new config:

```diff
 builder.Services.AddSingleton<RabbitMQ.Client.IConnection>(sp =>
 {
     var factory = new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(builder.Configuration.GetConnectionString("RabbitMq")!) };
     return factory.CreateConnectionAsync().GetAwaiter().GetResult();
 });
+
+builder.Services.AddHttpClient<IRabbitMqManagementClient, RabbitMqManagementClient>((sp, client) =>
+{
+    var config = sp.GetRequiredService<IConfiguration>();
+    var rabbitUri = new Uri(config.GetConnectionString("RabbitMq")!);
+    client.BaseAddress = new Uri($"http://{rabbitUri.Host}:15672/");
+    var userInfo = rabbitUri.UserInfo.Split(':', 2);
+    var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{userInfo[0]}:{userInfo[1]}"));
+    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basicAuth);
+});
```

Register `PlatformQueuesService` alongside `PlatformJobsService`:

```diff
 builder.Services.AddScoped<PlatformJobsService>();
+builder.Services.AddScoped<PlatformQueuesService>();
```

Map the endpoint near the other Platform maps:

```diff
 app.MapPlatformJobsEndpoints();
+app.MapPlatformQueuesEndpoints();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter PlatformQueuesEndpointsTests`
Expected: PASS (requires the dev RabbitMQ container — `docker-compose up rabbitmq`, management API on port 15672 — to be running).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Platform/Infrastructure/RabbitMqManagementClient.cs src/Modules/Platform/Presentation/PlatformQueuesEndpoints.cs src/Api/Program.cs tests/Api.Tests/PlatformQueuesEndpointsTests.cs
git commit -m "$(cat <<'EOF'
feat(platform): add GET /api/v1/platform/queues

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Rf7EtY8QJ2sgEus5qeEgTh
EOF
)"
```

---

### Task 5: Jobs frontend — types, `usePlatformJobs`, and proxy route

**Files:**
- Create: `src/web/src/features/platform-jobs/types.ts`
- Create: `src/web/src/features/platform-jobs/hooks/usePlatformJobs.ts`
- Create: `src/web/src/features/platform-jobs/hooks/usePlatformJobs.test.tsx`
- Create: `src/web/src/app/api/platform/jobs/route.ts`
- Create: `src/web/src/app/api/platform/jobs/route.test.ts`

**Interfaces:**
- Consumes: `clientFetch` (existing, `@/lib/api/clientFetch`), `platformApiFetch` (existing, `@/lib/auth/platformApiFetch`), the `GET /api/v1/platform/jobs` response shape from Task 2 (`{ summary, recurringJobs, recentJobs }`).
- Produces: `JobsSummary`, `RecurringJobSummary`, `RecentJobSummary`, `JobsOverview` (TS types), `usePlatformJobs()` — consumed by Task 6 and Task 7.

- [ ] **Step 1: Write the failing hook test**

Create `src/web/src/features/platform-jobs/hooks/usePlatformJobs.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { usePlatformJobs } from "./usePlatformJobs";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const overview = {
  summary: { enqueued: 0, scheduled: 0, processing: 0, succeeded: 10, failed: 1, deleted: 0 },
  recurringJobs: [],
  recentJobs: [],
};

describe("usePlatformJobs", () => {
  it("fetches the jobs overview from the proxy route", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => overview });

    const { result } = renderHook(() => usePlatformJobs(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/jobs", undefined);
    expect(result.current.data).toEqual(overview);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/features/platform-jobs/hooks/usePlatformJobs.test.tsx`
Expected: FAIL — cannot find module `./usePlatformJobs`.

- [ ] **Step 3: Create the types**

Create `src/web/src/features/platform-jobs/types.ts`:

```ts
export interface JobsSummary {
  enqueued: number;
  scheduled: number;
  processing: number;
  succeeded: number;
  failed: number;
  deleted: number;
}

export interface RecurringJobSummary {
  id: string;
  cron: string | null;
  nextExecution: string | null;
  lastExecution: string | null;
  lastJobState: string | null;
}

export interface RecentJobSummary {
  jobId: string;
  jobName: string;
  state: string;
  at: string;
  exceptionMessage: string | null;
}

export interface JobsOverview {
  summary: JobsSummary;
  recurringJobs: RecurringJobSummary[];
  recentJobs: RecentJobSummary[];
}
```

- [ ] **Step 4: Create the hook**

Create `src/web/src/features/platform-jobs/hooks/usePlatformJobs.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { JobsOverview } from "@/features/platform-jobs/types";

export function usePlatformJobs() {
  return useQuery({
    queryKey: ["platform-jobs", "overview"],
    queryFn: () => clientFetch<JobsOverview>("/api/platform/jobs"),
    refetchInterval: 10000,
  });
}
```

- [ ] **Step 5: Run the hook test to verify it passes**

Run: `cd src/web && npx vitest run src/features/platform-jobs/hooks/usePlatformJobs.test.tsx`
Expected: PASS

- [ ] **Step 6: Write the failing proxy route test**

Create `src/web/src/app/api/platform/jobs/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/platformApiFetch", () => ({
  platformApiFetch: vi.fn(),
}));

import { GET } from "./route";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

describe("GET /api/platform/jobs", () => {
  it("proxies to the backend jobs overview endpoint", async () => {
    vi.mocked(platformApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ summary: {}, recurringJobs: [], recentJobs: [] }),
    } as Response);

    const response = await GET();

    expect(platformApiFetch).toHaveBeenCalledWith("/api/v1/platform/jobs");
    expect(response.status).toBe(200);
  });

  it("returns 401 when there is no valid platform session", async () => {
    vi.mocked(platformApiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET();

    expect(response.status).toBe(401);
  });
});
```

- [ ] **Step 7: Run the route test to verify it fails**

Run: `cd src/web && npx vitest run src/app/api/platform/jobs/route.test.ts`
Expected: FAIL — cannot find module `./route`.

- [ ] **Step 8: Create the proxy route**

Create `src/web/src/app/api/platform/jobs/route.ts`:

```ts
import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function GET() {
  try {
    const response = await platformApiFetch("/api/v1/platform/jobs");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 9: Run the route test to verify it passes**

Run: `cd src/web && npx vitest run src/app/api/platform/jobs/route.test.ts`
Expected: PASS

- [ ] **Step 10: Commit**

```bash
git add src/web/src/features/platform-jobs/types.ts src/web/src/features/platform-jobs/hooks/usePlatformJobs.ts src/web/src/features/platform-jobs/hooks/usePlatformJobs.test.tsx src/web/src/app/api/platform/jobs/route.ts src/web/src/app/api/platform/jobs/route.test.ts
git commit -m "$(cat <<'EOF'
feat(web): add platform jobs types, query hook, and proxy route

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Rf7EtY8QJ2sgEus5qeEgTh
EOF
)"
```

---

### Task 6: Jobs frontend — panel store and detail Sheet

**Files:**
- Create: `src/web/src/features/platform-jobs/store/platformJobPanelStore.ts`
- Create: `src/web/src/features/platform-jobs/PlatformJobDetailSheet.tsx`
- Create: `src/web/src/features/platform-jobs/PlatformJobDetailSheet.test.tsx`

**Interfaces:**
- Consumes: `RecentJobSummary` (Task 5).
- Produces: `usePlatformJobPanelStore` (`panel`, `openDetail(job)`, `close()`), `<PlatformJobDetailSheet />` — consumed by Task 7's page.

- [ ] **Step 1: Write the failing Sheet test**

Create `src/web/src/features/platform-jobs/PlatformJobDetailSheet.test.tsx`:

```tsx
import { describe, expect, it, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { usePlatformJobPanelStore } from "./store/platformJobPanelStore";
import { PlatformJobDetailSheet } from "./PlatformJobDetailSheet";

const failedJob = {
  jobId: "job-1",
  jobName: "WebhookRetryJob",
  state: "Failed",
  at: "2026-09-01T10:00:00Z",
  exceptionMessage: "Connection refused",
};

describe("PlatformJobDetailSheet", () => {
  beforeEach(() => {
    usePlatformJobPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("renders nothing (closed) until a job is opened", () => {
    render(<PlatformJobDetailSheet />);

    expect(screen.queryByText("WebhookRetryJob")).not.toBeInTheDocument();
  });

  it("shows the job name and exception message once opened", () => {
    usePlatformJobPanelStore.getState().openDetail(failedJob);
    render(<PlatformJobDetailSheet />);

    expect(screen.getByText("WebhookRetryJob")).toBeInTheDocument();
    expect(screen.getByText("Connection refused")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/features/platform-jobs/PlatformJobDetailSheet.test.tsx`
Expected: FAIL — cannot find modules `./store/platformJobPanelStore` and `./PlatformJobDetailSheet`.

- [ ] **Step 3: Create the panel store**

Create `src/web/src/features/platform-jobs/store/platformJobPanelStore.ts`:

```ts
import { create } from "zustand";
import type { RecentJobSummary } from "@/features/platform-jobs/types";

type PlatformJobPanelState = { mode: "closed" } | { mode: "detail"; job: RecentJobSummary };

interface PlatformJobPanelStore {
  panel: PlatformJobPanelState;
  openDetail: (job: RecentJobSummary) => void;
  close: () => void;
}

export const usePlatformJobPanelStore = create<PlatformJobPanelStore>((set) => ({
  panel: { mode: "closed" },
  openDetail: (job) => set({ panel: { mode: "detail", job } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

- [ ] **Step 4: Create the detail Sheet**

Create `src/web/src/features/platform-jobs/PlatformJobDetailSheet.tsx`:

```tsx
"use client";

import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Badge } from "@/components/ui/badge";
import { usePlatformJobPanelStore } from "@/features/platform-jobs/store/platformJobPanelStore";

export function PlatformJobDetailSheet() {
  const panel = usePlatformJobPanelStore((s) => s.panel);
  const close = usePlatformJobPanelStore((s) => s.close);
  const job = panel.mode === "detail" ? panel.job : null;

  return (
    <Sheet open={panel.mode === "detail"} onOpenChange={(open) => !open && close()}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{job?.jobName ?? "Job"}</SheetTitle>
        </SheetHeader>
        {job && (
          <div className="space-y-4 p-4 text-sm">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={job.state === "Failed" ? "destructive" : "secondary"}>{job.state}</Badge>
              <span className="text-muted-foreground">{job.jobId}</span>
            </div>
            <dl className="space-y-2">
              <div>
                <dt className="text-muted-foreground">Time</dt>
                <dd>{new Date(job.at).toLocaleString()}</dd>
              </div>
            </dl>
            {job.exceptionMessage && (
              <div>
                <span className="mb-1 text-muted-foreground">Exception</span>
                <pre className="overflow-x-auto rounded-md bg-muted p-2 text-xs">{job.exceptionMessage}</pre>
              </div>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/web && npx vitest run src/features/platform-jobs/PlatformJobDetailSheet.test.tsx`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/web/src/features/platform-jobs/store/platformJobPanelStore.ts src/web/src/features/platform-jobs/PlatformJobDetailSheet.tsx src/web/src/features/platform-jobs/PlatformJobDetailSheet.test.tsx
git commit -m "$(cat <<'EOF'
feat(web): add platform job detail panel store and Sheet

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Rf7EtY8QJ2sgEus5qeEgTh
EOF
)"
```

---

### Task 7: Jobs frontend — `/platform/jobs` page and nav entry

**Files:**
- Create: `src/web/src/app/platform/jobs/page.tsx`
- Create: `src/web/src/app/platform/jobs/page.test.tsx`
- Modify: `src/web/src/features/platform/PlatformNav.tsx`
- Modify: `src/web/src/features/platform/PlatformNav.test.tsx`

**Interfaces:**
- Consumes: `usePlatformJobs` (Task 5), `usePlatformJobPanelStore`/`PlatformJobDetailSheet` (Task 6), `DataTable`/`ColumnDef`/`PagedResult` (existing, `@/components/data-table/DataTable`), `ErrorState` (existing, `@/components/states/ErrorState`), `Spinner` (existing, `@/components/ui/spinner`), `Badge` (existing, `@/components/ui/badge`).

- [ ] **Step 1: Write the failing page test**

Create `src/web/src/app/platform/jobs/page.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, within } from "@testing-library/react";

const overview = {
  summary: { enqueued: 1, scheduled: 2, processing: 3, succeeded: 50, failed: 4, deleted: 0 },
  recurringJobs: [
    { id: "outbox-publisher", cron: "*/10 * * * * *", nextExecution: "2026-09-12T10:00:10Z", lastExecution: "2026-09-12T10:00:00Z", lastJobState: "Succeeded" },
  ],
  recentJobs: [
    { jobId: "job-1", jobName: "WebhookRetryJob", state: "Failed", at: "2026-09-12T09:59:00Z", exceptionMessage: "Connection refused" },
  ],
};

const usePlatformJobs = vi.fn(() => ({ data: overview, isLoading: false, isError: false, refetch: vi.fn() }));
vi.mock("@/features/platform-jobs/hooks/usePlatformJobs", () => ({
  usePlatformJobs: () => usePlatformJobs(),
}));

import PlatformJobsPage from "./page";

describe("PlatformJobsPage", () => {
  it("renders the summary counts, the recurring job, and the recent job", () => {
    render(<PlatformJobsPage />);

    expect(screen.getByText("50")).toBeInTheDocument();
    expect(screen.getByText("outbox-publisher")).toBeInTheDocument();
    expect(screen.getByText("WebhookRetryJob")).toBeInTheDocument();
  });

  it("opens the detail Sheet with the exception message when a failed job is clicked", () => {
    render(<PlatformJobsPage />);

    fireEvent.click(screen.getByText("WebhookRetryJob"));

    expect(within(screen.getByRole("dialog")).getByText("Connection refused")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/app/platform/jobs/page.test.tsx`
Expected: FAIL — cannot find module `./page`.

- [ ] **Step 3: Create the page**

Create `src/web/src/app/platform/jobs/page.tsx`:

```tsx
"use client";

import { Spinner } from "@/components/ui/spinner";
import { Badge } from "@/components/ui/badge";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { usePlatformJobs } from "@/features/platform-jobs/hooks/usePlatformJobs";
import { usePlatformJobPanelStore } from "@/features/platform-jobs/store/platformJobPanelStore";
import { PlatformJobDetailSheet } from "@/features/platform-jobs/PlatformJobDetailSheet";
import type { RecurringJobSummary, RecentJobSummary } from "@/features/platform-jobs/types";

function StatTile({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="rounded-lg border bg-card p-4">
      <div className="text-sm text-muted-foreground">{label}</div>
      <div className="mt-1 text-2xl font-semibold">{value}</div>
    </div>
  );
}

function toSinglePage<T>(items: T[]): PagedResult<T> {
  return { items, page: 1, pageSize: Math.max(items.length, 1), totalCount: items.length, totalPages: 1 };
}

export default function PlatformJobsPage() {
  const { data, isLoading, isError, refetch } = usePlatformJobs();
  const openDetail = usePlatformJobPanelStore((s) => s.openDetail);

  const recurringColumns: ColumnDef<RecurringJobSummary>[] = [
    { key: "id", header: "Job", render: (r) => r.id },
    { key: "cron", header: "Cron", render: (r) => r.cron ?? "—" },
    { key: "lastExecution", header: "Last Run", render: (r) => (r.lastExecution ? new Date(r.lastExecution).toLocaleString() : "—") },
    { key: "nextExecution", header: "Next Run", render: (r) => (r.nextExecution ? new Date(r.nextExecution).toLocaleString() : "—") },
    { key: "lastJobState", header: "Last State", render: (r) => (r.lastJobState ? <Badge variant={r.lastJobState === "Failed" ? "destructive" : "secondary"}>{r.lastJobState}</Badge> : "—") },
  ];

  const recentColumns: ColumnDef<RecentJobSummary>[] = [
    { key: "at", header: "Time", render: (r) => new Date(r.at).toLocaleString() },
    {
      key: "jobName",
      header: "Job",
      render: (r) =>
        r.state === "Failed" ? (
          <button type="button" onClick={() => openDetail(r)} className="font-medium text-foreground hover:underline">
            {r.jobName}
          </button>
        ) : (
          r.jobName
        ),
    },
    { key: "state", header: "State", render: (r) => <Badge variant={r.state === "Failed" ? "destructive" : "secondary"}>{r.state}</Badge> },
    { key: "exceptionMessage", header: "Exception", render: (r) => r.exceptionMessage ?? "—" },
  ];

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }
  if (isError || !data) {
    return (
      <ErrorState
        title="Couldn't load jobs"
        description="Something went wrong fetching the background job status."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-6 p-6">
      <h1 className="font-display text-xl font-semibold">Jobs</h1>
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
        <StatTile label="Enqueued" value={data.summary.enqueued} />
        <StatTile label="Scheduled" value={data.summary.scheduled} />
        <StatTile label="Processing" value={data.summary.processing} />
        <StatTile label="Succeeded" value={data.summary.succeeded} />
        <StatTile label="Failed" value={data.summary.failed} />
        <StatTile label="Deleted" value={data.summary.deleted} />
      </div>
      <div className="space-y-2">
        <h2 className="text-sm font-medium text-muted-foreground">Recurring Jobs</h2>
        <DataTable
          columns={recurringColumns}
          data={toSinglePage(data.recurringJobs)}
          isLoading={false}
          emptyTitle="No recurring jobs registered"
          onPageChange={() => {}}
          rowKey={(r) => r.id}
        />
      </div>
      <div className="space-y-2">
        <h2 className="text-sm font-medium text-muted-foreground">Recent Activity</h2>
        <DataTable
          columns={recentColumns}
          data={toSinglePage(data.recentJobs)}
          isLoading={false}
          emptyTitle="No recent job activity"
          onPageChange={() => {}}
          rowKey={(r) => r.jobId}
        />
      </div>
      <PlatformJobDetailSheet />
    </div>
  );
}
```

- [ ] **Step 4: Add the nav entry**

```diff
 const links = [
   { href: "/platform/dashboard", label: "Dashboard" },
   { href: "/platform/organizations", label: "Organizations" },
   { href: "/platform/plans", label: "Plans" },
   { href: "/platform/invoices", label: "Invoices" },
   { href: "/platform/audit-logs", label: "Audit Logs" },
+  { href: "/platform/jobs", label: "Jobs" },
 ];
```

Update the nav test's count and assertion accordingly in `src/web/src/features/platform/PlatformNav.test.tsx`:

```diff
-  it("renders all five platform nav links", () => {
+  it("renders all six platform nav links", () => {
     render(<PlatformNav />);

     expect(screen.getByText("Dashboard")).toBeInTheDocument();
     expect(screen.getByText("Organizations")).toBeInTheDocument();
     expect(screen.getByText("Plans")).toBeInTheDocument();
     expect(screen.getByText("Invoices")).toBeInTheDocument();
     expect(screen.getByText("Audit Logs")).toBeInTheDocument();
+    expect(screen.getByText("Jobs")).toBeInTheDocument();
   });
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd src/web && npx vitest run src/app/platform/jobs/page.test.tsx src/features/platform/PlatformNav.test.tsx`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/web/src/app/platform/jobs/page.tsx src/web/src/app/platform/jobs/page.test.tsx src/web/src/features/platform/PlatformNav.tsx src/web/src/features/platform/PlatformNav.test.tsx
git commit -m "$(cat <<'EOF'
feat(web): add /platform/jobs page and nav link

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Rf7EtY8QJ2sgEus5qeEgTh
EOF
)"
```

---

### Task 8: Queues frontend — types, `usePlatformQueues`, and proxy route

**Files:**
- Create: `src/web/src/features/platform-queues/types.ts`
- Create: `src/web/src/features/platform-queues/hooks/usePlatformQueues.ts`
- Create: `src/web/src/features/platform-queues/hooks/usePlatformQueues.test.tsx`
- Create: `src/web/src/app/api/platform/queues/route.ts`
- Create: `src/web/src/app/api/platform/queues/route.test.ts`

**Interfaces:**
- Consumes: `clientFetch`, `platformApiFetch` (existing), the `GET /api/v1/platform/queues` response shape from Task 4 (a `QueueSummary[]`).
- Produces: `QueueSummary` (TS type), `usePlatformQueues()` — consumed by Task 9's page.

- [ ] **Step 1: Write the failing hook test**

Create `src/web/src/features/platform-queues/hooks/usePlatformQueues.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { usePlatformQueues } from "./usePlatformQueues";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const queues = [{ name: "courier.notifications", messages: 0, messagesReady: 0, messagesUnacknowledged: 0, consumers: 1, state: "running" }];

describe("usePlatformQueues", () => {
  it("fetches the queue list from the proxy route", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => queues });

    const { result } = renderHook(() => usePlatformQueues(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/queues", undefined);
    expect(result.current.data).toEqual(queues);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/features/platform-queues/hooks/usePlatformQueues.test.tsx`
Expected: FAIL — cannot find module `./usePlatformQueues`.

- [ ] **Step 3: Create the types**

Create `src/web/src/features/platform-queues/types.ts`:

```ts
export interface QueueSummary {
  name: string;
  messages: number;
  messagesReady: number;
  messagesUnacknowledged: number;
  consumers: number;
  state: string;
}
```

- [ ] **Step 4: Create the hook**

Create `src/web/src/features/platform-queues/hooks/usePlatformQueues.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { QueueSummary } from "@/features/platform-queues/types";

export function usePlatformQueues() {
  return useQuery({
    queryKey: ["platform-queues", "list"],
    queryFn: () => clientFetch<QueueSummary[]>("/api/platform/queues"),
    refetchInterval: 10000,
  });
}
```

- [ ] **Step 5: Run the hook test to verify it passes**

Run: `cd src/web && npx vitest run src/features/platform-queues/hooks/usePlatformQueues.test.tsx`
Expected: PASS

- [ ] **Step 6: Write the failing proxy route test**

Create `src/web/src/app/api/platform/queues/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/platformApiFetch", () => ({
  platformApiFetch: vi.fn(),
}));

import { GET } from "./route";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

describe("GET /api/platform/queues", () => {
  it("proxies to the backend queues endpoint", async () => {
    vi.mocked(platformApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    } as Response);

    const response = await GET();

    expect(platformApiFetch).toHaveBeenCalledWith("/api/v1/platform/queues");
    expect(response.status).toBe(200);
  });

  it("returns 401 when there is no valid platform session", async () => {
    vi.mocked(platformApiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET();

    expect(response.status).toBe(401);
  });
});
```

- [ ] **Step 7: Run the route test to verify it fails**

Run: `cd src/web && npx vitest run src/app/api/platform/queues/route.test.ts`
Expected: FAIL — cannot find module `./route`.

- [ ] **Step 8: Create the proxy route**

Create `src/web/src/app/api/platform/queues/route.ts`:

```ts
import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function GET() {
  try {
    const response = await platformApiFetch("/api/v1/platform/queues");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 9: Run the route test to verify it passes**

Run: `cd src/web && npx vitest run src/app/api/platform/queues/route.test.ts`
Expected: PASS

- [ ] **Step 10: Commit**

```bash
git add src/web/src/features/platform-queues/types.ts src/web/src/features/platform-queues/hooks/usePlatformQueues.ts src/web/src/features/platform-queues/hooks/usePlatformQueues.test.tsx src/web/src/app/api/platform/queues/route.ts src/web/src/app/api/platform/queues/route.test.ts
git commit -m "$(cat <<'EOF'
feat(web): add platform queues types, query hook, and proxy route

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Rf7EtY8QJ2sgEus5qeEgTh
EOF
)"
```

---

### Task 9: Queues frontend — `/platform/queues` page and nav entry

**Files:**
- Create: `src/web/src/app/platform/queues/page.tsx`
- Create: `src/web/src/app/platform/queues/page.test.tsx`
- Modify: `src/web/src/features/platform/PlatformNav.tsx`
- Modify: `src/web/src/features/platform/PlatformNav.test.tsx`

**Interfaces:**
- Consumes: `usePlatformQueues` (Task 8), `DataTable`/`ColumnDef`/`PagedResult`, `ErrorState`, `Spinner`, `Badge` (existing).

- [ ] **Step 1: Write the failing page test**

Create `src/web/src/app/platform/queues/page.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const queues = [
  { name: "courier.notifications", messages: 5, messagesReady: 5, messagesUnacknowledged: 0, consumers: 1, state: "running" },
  { name: "courier.webhooks", messages: 12, messagesReady: 12, messagesUnacknowledged: 0, consumers: 0, state: "idle" },
];

const usePlatformQueues = vi.fn(() => ({ data: queues, isLoading: false, isError: false, refetch: vi.fn() }));
vi.mock("@/features/platform-queues/hooks/usePlatformQueues", () => ({
  usePlatformQueues: () => usePlatformQueues(),
}));

import PlatformQueuesPage from "./page";

describe("PlatformQueuesPage", () => {
  it("renders every queue with its ready/unacked/consumer counts", () => {
    render(<PlatformQueuesPage />);

    expect(screen.getByText("courier.notifications")).toBeInTheDocument();
    expect(screen.getByText("courier.webhooks")).toBeInTheDocument();
  });

  it("flags a queue with messages piling up and zero consumers as unhealthy", () => {
    render(<PlatformQueuesPage />);

    expect(screen.getByText("Unhealthy")).toBeInTheDocument();
    expect(screen.getByText("Healthy")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/app/platform/queues/page.test.tsx`
Expected: FAIL — cannot find module `./page`.

- [ ] **Step 3: Create the page**

Create `src/web/src/app/platform/queues/page.tsx`:

```tsx
"use client";

import { Spinner } from "@/components/ui/spinner";
import { Badge } from "@/components/ui/badge";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { usePlatformQueues } from "@/features/platform-queues/hooks/usePlatformQueues";
import type { QueueSummary } from "@/features/platform-queues/types";

function toSinglePage<T>(items: T[]): PagedResult<T> {
  return { items, page: 1, pageSize: Math.max(items.length, 1), totalCount: items.length, totalPages: 1 };
}

function isUnhealthy(queue: QueueSummary): boolean {
  return queue.messagesReady > 0 && queue.consumers === 0;
}

export default function PlatformQueuesPage() {
  const { data, isLoading, isError, refetch } = usePlatformQueues();

  const columns: ColumnDef<QueueSummary>[] = [
    { key: "name", header: "Queue", render: (q) => q.name },
    { key: "messagesReady", header: "Ready", render: (q) => q.messagesReady },
    { key: "messagesUnacknowledged", header: "Unacked", render: (q) => q.messagesUnacknowledged },
    { key: "consumers", header: "Consumers", render: (q) => q.consumers },
    {
      key: "status",
      header: "Status",
      render: (q) => <Badge variant={isUnhealthy(q) ? "destructive" : "secondary"}>{isUnhealthy(q) ? "Unhealthy" : "Healthy"}</Badge>,
    },
  ];

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }
  if (isError || !data) {
    return (
      <ErrorState
        title="Couldn't load queues"
        description="Something went wrong fetching RabbitMQ queue status."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4 p-6">
      <h1 className="font-display text-xl font-semibold">Queues</h1>
      <DataTable
        columns={columns}
        data={toSinglePage(data)}
        isLoading={false}
        emptyTitle="No queues found"
        onPageChange={() => {}}
        rowKey={(q) => q.name}
      />
    </div>
  );
}
```

- [ ] **Step 4: Add the nav entry**

```diff
 const links = [
   { href: "/platform/dashboard", label: "Dashboard" },
   { href: "/platform/organizations", label: "Organizations" },
   { href: "/platform/plans", label: "Plans" },
   { href: "/platform/invoices", label: "Invoices" },
   { href: "/platform/audit-logs", label: "Audit Logs" },
   { href: "/platform/jobs", label: "Jobs" },
+  { href: "/platform/queues", label: "Queues" },
 ];
```

Update the nav test's count and assertion accordingly in `src/web/src/features/platform/PlatformNav.test.tsx`:

```diff
-  it("renders all six platform nav links", () => {
+  it("renders all seven platform nav links", () => {
     render(<PlatformNav />);

     expect(screen.getByText("Dashboard")).toBeInTheDocument();
     expect(screen.getByText("Organizations")).toBeInTheDocument();
     expect(screen.getByText("Plans")).toBeInTheDocument();
     expect(screen.getByText("Invoices")).toBeInTheDocument();
     expect(screen.getByText("Audit Logs")).toBeInTheDocument();
     expect(screen.getByText("Jobs")).toBeInTheDocument();
+    expect(screen.getByText("Queues")).toBeInTheDocument();
   });
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd src/web && npx vitest run src/app/platform/queues/page.test.tsx src/features/platform/PlatformNav.test.tsx`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/web/src/app/platform/queues/page.tsx src/web/src/app/platform/queues/page.test.tsx src/web/src/features/platform/PlatformNav.tsx src/web/src/features/platform/PlatformNav.test.tsx
git commit -m "$(cat <<'EOF'
feat(web): add /platform/queues page and nav link

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Rf7EtY8QJ2sgEus5qeEgTh
EOF
)"
```
