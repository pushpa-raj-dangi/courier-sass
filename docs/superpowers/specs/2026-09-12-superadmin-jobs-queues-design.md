# Design: SuperAdmin Jobs & Queues Monitoring

**Date:** 2026-09-12
**Status:** Approved for planning
**Track:** Platform module (SuperAdmin operational visibility)

## 1. Problem

The platform already runs two pieces of async infrastructure that SuperAdmin has no visibility
into today:

* **Hangfire** (`src/Api/Program.cs:119-124`) — backed by the same Postgres database as
  `AppDbContext` (`Hangfire.PostgreSql`), running five recurring jobs (`outbox-publisher`,
  `webhook-retry`, `billing-invoice-generation`, `billing-overdue-invoice-suspension`,
  `billing-trial-expiry`). Its built-in dashboard is mounted at `/hangfire`
  (`Program.cs:287`) but only inside the `app.Environment.IsDevelopment()` block, and it carries
  no authorization check even there — it is not available in any deployed environment and would
  be unauthenticated if it were.
* **RabbitMQ** (`docker-compose.yml:28-37`) — a single shared `RabbitMQ.Client.IConnection`
  (`Program.cs:104-108`), topology defined in `src/SharedKernel/RabbitMqTopology.cs`
  (`courier.notifications`, `courier.webhooks`, plus the `courier.domain-events` exchange). The
  management plugin's HTTP API (port 15672) is already exposed by the `rabbitmq:3-management`
  image but nothing in the codebase talks to it — there is no queue-depth or consumer-count
  visibility anywhere.

If a recurring job starts failing, or a consumer stops draining a queue, there is currently no
way to see that short of `docker exec`-ing into the containers.

## 2. Goals

* A `SuperAdmin` gets a **Jobs** page under `/platform/jobs`: live counts (enqueued / scheduled /
  processing / succeeded / failed / deleted), the state of all five recurring jobs (last run,
  next run, last state), and a combined recent-activity list of the last 20 failed + last 20
  succeeded jobs, with the failure reason visible on drill-down for failed jobs.
* A `SuperAdmin` gets a **Queues** page under `/platform/queues`: every RabbitMQ queue's ready /
  unacknowledged message counts and consumer count, with an at-a-glance unhealthy flag (messages
  piling up with zero consumers).
* Both pages auto-refresh (this is live operational data, not a historical record) and each
  fails independently — Hangfire being unreachable must not blank the Queues page and vice
  versa.

## 3. Non-Goals

* **Any mutating action** — no retry-failed-job, no requeue, no purge-queue. This is a read-only
  monitoring surface. Actions would need their own audit-logging design (CLAUDE.md §28) and are
  a deliberate fast-follow, not part of this spec.
* **Securing or exposing the built-in `/hangfire` dashboard.** Decided against in brainstorming:
  a custom page matches the rest of the Platform section's UI (DataTable/StatTile conventions)
  and lets us control exactly what's exposed, rather than reusing a third-party admin UI that
  looks and behaves nothing like the rest of `/platform/*`. The dashboard's current dev-only,
  unauthenticated mount is a pre-existing latent gap, unrelated to this feature — worth its own
  follow-up, not fixed here.
* **New RabbitMQ or Hangfire config/credentials.** The management API client reuses the existing
  `ConnectionStrings:RabbitMq` value (host + credentials) rather than adding a parallel
  `RabbitMqManagement` config section — one connection string stays the single source of truth
  for how to reach RabbitMQ.
* **Server-side pagination** for recurring jobs / recent jobs / queues. All three lists are
  small by construction (5 recurring jobs; top-20+20 recent jobs; one row per queue, realistically
  under a dozen) — rendered as a single page, no `page`/`pageSize` query params.

## 4. Backend — Jobs

New records + interface + service in `src/Modules/Platform/Application/`:

```csharp
// IHangfireMonitor.cs
namespace Courier.Platform.Application;

public interface IHangfireMonitor
{
    JobsSummary GetSummary();
    IReadOnlyList<RecurringJobSummary> GetRecurringJobs();
    IReadOnlyList<RecentJobSummary> GetRecentJobs(int failedCount, int succeededCount);
}
```

```csharp
// PlatformJobsService.cs
namespace Courier.Platform.Application;

public sealed record JobsSummary(long Enqueued, long Scheduled, long Processing, long Succeeded, long Failed, long Deleted);
public sealed record RecurringJobSummary(string Id, string? Cron, DateTime? NextExecution, DateTime? LastExecution, string? LastJobState);
public sealed record RecentJobSummary(string JobId, string JobName, string State, DateTimeOffset At, string? ExceptionMessage);
public sealed record JobsOverview(JobsSummary Summary, IReadOnlyList<RecurringJobSummary> RecurringJobs, IReadOnlyList<RecentJobSummary> RecentJobs);

public sealed class PlatformJobsService
{
    private readonly IHangfireMonitor _monitor;
    public PlatformJobsService(IHangfireMonitor monitor) => _monitor = monitor;

    public Task<JobsOverview> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new JobsOverview(_monitor.GetSummary(), _monitor.GetRecurringJobs(), _monitor.GetRecentJobs(20, 20)));
}
```

`IHangfireMonitor` is the seam that keeps every Hangfire type (`JobStorage`, `StatisticsDto`,
`RecurringJobDto`, `FailedJobDto`, `SucceededJobDto`, the `Job.Type` load-exception risk) out of
`Application` and lets `PlatformJobsService` be unit-tested against a hand-written fake, the same
way `PlatformAuditService` is tested against fakes of `IAuditLogReader`/`IOrganizationLookupService`.

Implementation, `src/Modules/Platform/Infrastructure/HangfireMonitor.cs`, wraps
`JobStorage.Current`:

```csharp
namespace Courier.Platform.Infrastructure;

using Courier.Platform.Application;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;

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

Endpoint, `src/Modules/Platform/Presentation/PlatformJobsEndpoints.cs`, following the
`PlatformInvoicesEndpoints` shape exactly:

```csharp
app.MapGet("/api/v1/platform/jobs", async (PlatformJobsService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.GetOverviewAsync(cancellationToken)))
    .WithTags("Platform Jobs")
    .RequireSuperAdmin();
```

`Program.cs` registers `IHangfireMonitor` as a singleton (it wraps a process-wide static, holds
no per-request state) and `PlatformJobsService` as scoped, matching the other Platform services.

## 5. Backend — Queues

```csharp
// IRabbitMqManagementClient.cs
namespace Courier.Platform.Application;

public interface IRabbitMqManagementClient
{
    Task<IReadOnlyList<QueueSummary>> GetQueuesAsync(CancellationToken cancellationToken = default);
}
```

```csharp
// PlatformQueuesService.cs
namespace Courier.Platform.Application;

public sealed record QueueSummary(string Name, long Messages, long MessagesReady, long MessagesUnacknowledged, int Consumers, string State);

public sealed class PlatformQueuesService
{
    private readonly IRabbitMqManagementClient _client;
    public PlatformQueuesService(IRabbitMqManagementClient client) => _client = client;

    public Task<IReadOnlyList<QueueSummary>> GetQueuesAsync(CancellationToken cancellationToken = default) =>
        _client.GetQueuesAsync(cancellationToken);
}
```

Implementation, `src/Modules/Platform/Infrastructure/RabbitMqManagementClient.cs`, is a typed
`HttpClient` hitting the management API's default vhost (`/`, URL-encoded `%2f`):

```csharp
namespace Courier.Platform.Infrastructure;

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Courier.Platform.Application;

public sealed class RabbitMqManagementClient : IRabbitMqManagementClient
{
    private readonly HttpClient _httpClient;
    public RabbitMqManagementClient(HttpClient httpClient) => _httpClient = httpClient;

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
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("messages")] public long Messages { get; set; }
        [JsonPropertyName("messages_ready")] public long MessagesReady { get; set; }
        [JsonPropertyName("messages_unacknowledged")] public long MessagesUnacknowledged { get; set; }
        [JsonPropertyName("consumers")] public int Consumers { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
    }
}
```

`Program.cs` registers it as a typed client whose base address and Basic-auth header are derived
from the existing `ConnectionStrings:RabbitMq` value (`amqp://user:pass@host:5672` →
`http://host:15672/`), not a new config section:

```csharp
builder.Services.AddHttpClient<IRabbitMqManagementClient, RabbitMqManagementClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var rabbitUri = new Uri(config.GetConnectionString("RabbitMq")!);
    client.BaseAddress = new Uri($"http://{rabbitUri.Host}:15672/");
    var userInfo = rabbitUri.UserInfo.Split(':', 2);
    var basicAuth = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{userInfo[0]}:{userInfo[1]}"));
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basicAuth);
});
```

Endpoint, `src/Modules/Platform/Presentation/PlatformQueuesEndpoints.cs`:

```csharp
app.MapGet("/api/v1/platform/queues", async (PlatformQueuesService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.GetQueuesAsync(cancellationToken)))
    .WithTags("Platform Queues")
    .RequireSuperAdmin();
```

## 6. Frontend — Jobs

* `PlatformNav.tsx`: add `{ href: "/platform/jobs", label: "Jobs" }`.
* `src/features/platform-jobs/`:
  * `types.ts` — `JobsSummary`, `RecurringJobSummary`, `RecentJobSummary`, `JobsOverview`
    (camelCase mirrors of the backend records; System.Text.Json's default camelCase output means
    no explicit `[JsonPropertyName]` is needed backend-side).
  * `hooks/usePlatformJobs.ts` — `useQuery({ queryKey: ["platform-jobs", "overview"], queryFn: () => clientFetch<JobsOverview>("/api/platform/jobs"), refetchInterval: 10000 })`.
  * `store/platformJobPanelStore.ts` — Zustand, `{mode:"closed"}|{mode:"detail",job:RecentJobSummary}`, same shape as `platformAuditLogPanelStore`.
  * `PlatformJobDetailSheet.tsx` — right-side Sheet showing `jobName`, `state`, `at`, and `exceptionMessage` (rendered in a `<pre>` block, same as the audit log Before/After blocks).
* `src/app/api/platform/jobs/route.ts` — thin `GET` proxy, no query string, same shape as the audit-logs route but without `request.nextUrl.search`.
* `src/app/platform/jobs/page.tsx`:
  * A local `StatTile` row (same inline component pattern as `platform/dashboard/page.tsx`) for the 6 summary counts.
  * A `DataTable` of recurring jobs (columns: Job, Cron, Last Run, Next Run, Last State), built from `recurringJobs` wrapped in a single-page `PagedResult` shape (`{ items, page: 1, pageSize: items.length, totalCount: items.length, totalPages: 1 }`) since `DataTable` expects that shape and these lists never need real pagination (§3).
  * A second `DataTable` of recent jobs (columns: Time, Job, State — a `Badge` colored by state, Exception) — a Failed row's Exception cell is a truncated preview whose click opens `PlatformJobDetailSheet` via the panel store's `openDetail`; Succeeded rows aren't clickable (no exception to show).

## 7. Frontend — Queues

* `PlatformNav.tsx`: add `{ href: "/platform/queues", label: "Queues" }`.
* `src/features/platform-queues/`:
  * `types.ts` — `QueueSummary`.
  * `hooks/usePlatformQueues.ts` — `useQuery({ queryKey: ["platform-queues", "list"], queryFn: () => clientFetch<QueueSummary[]>("/api/platform/queues"), refetchInterval: 10000 })`.
* `src/app/api/platform/queues/route.ts` — same thin proxy shape as Jobs.
* `src/app/platform/queues/page.tsx` — a single `DataTable` (columns: Queue, Ready, Unacked, Consumers, Status), wrapped in the same single-page `PagedResult` shape. Status is a `Badge`: `destructive` when `messagesReady > 0 && consumers === 0` (messages piling up, nothing draining them), `secondary` otherwise. No row click / no Sheet — nothing further to drill into for a queue in v1.

## 8. Testing

* **Unit** (`Platform.Tests`): `PlatformJobsServiceTests` against a hand-written `FakeHangfireMonitor` (verifies `GetOverviewAsync` assembles `JobsOverview` from the monitor's three calls, and that `GetRecentJobs` is called with `(20, 20)`); `PlatformQueuesServiceTests` against a hand-written `FakeRabbitMqManagementClient` (verifies the service is a pure passthrough).
* **Integration** (`Api.Tests`, `WebApplicationFactory<Program>`, mirroring `PlatformInvoicesEndpointsTests`): `PlatformJobsEndpointsTests` — 401 without auth, 200 with a SuperAdmin token, response shape has `summary`/`recurringJobs`/`recentJobs`, and the five known recurring job IDs (`outbox-publisher`, `webhook-retry`, `billing-invoice-generation`, `billing-overdue-invoice-suspension`, `billing-trial-expiry`) are present since `Program.cs` registers them unconditionally at startup; `PlatformQueuesEndpointsTests` — 401 without auth, 200 with a SuperAdmin token, response is a JSON array (queues are only guaranteed to exist once a publisher/consumer has run at least once in the test environment, so the test only asserts the shape, not specific queue names).
* **Frontend**: hook tests mocking `clientFetch` (Jobs, Queues); proxy route tests mocking `platformApiFetch` (Jobs, Queues); page tests mocking the hooks — Jobs page renders stat tiles + both tables and opens the detail Sheet on a failed-job click; Queues page renders the table and shows the unhealthy badge when `consumers === 0` and `messagesReady > 0`.
