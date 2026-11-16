# F15 R3b-2 — People & Places Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the staff Riders page (Riders / Map / Fleet tabs, rider Sheet) and Branches page (type strip, live custody counts, branch Sheet) around the approved "Network: people and places" artboards, backed by a small read-only backend slice and one branch-scope security fix on `GET /riders/{id}`.

**Architecture:** Backend adds a read-only Identity name lookup (`IUserDirectoryLookupService`), a batched rider board and a rider "today" read inside `RiderService`, three grouped lookup methods on the existing reporting lookup interfaces, and a Reports-owned `BranchActivityService` behind `GET /branches/activity`. Web adds pure model files (`riderModel.ts`, `branchModel.ts`), thin TanStack hooks and Next proxy routes, restyles the Leaflet map, and rewrites the two pages and their Sheets. `/riders/live-map` becomes a redirect to `/riders?tab=map`.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core (InMemory unit tests + Postgres lookup/API tests), xUnit; Next.js 14 App Router, TypeScript, TanStack Query v5, Zustand, shadcn/ui, react-leaflet 4, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-10-15-f15-r3b2-people-and-places-design.md`

## Global Constraints

- No migrations, no new entities, no new write endpoints, no change to existing write behaviour, no new npm or NuGet dependencies.
- Tenant isolation stays on the existing global query filters. Branch scope comes from `IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, "<permission>")` (null = all branches). A `branchId` filter only narrows within scope.
- Every new read is batched: a fixed number of queries per request, never one per row.
- Out-of-scope single reads return **404**, never 403 and never data (`GET /riders/{id}`, `GET /riders/{id}/today`).
- "Today" is the UTC date: `DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date)`, the same expression `GetMyTasksTodayAsync` uses. Not changed here (follow-up 1).
- Every number shown is server data; loading and error states never render `0` — skeletons, `ErrorState`, or "—".
- Data the API does not serve renders `ComingSoonSlot` (`@/components/page/ComingSoonSlot`) or plain "Needs backend" note text. Never mock values.
- Detail views are right-side Sheets driven by Zustand panel stores (`useRiderPanelStore`, `useBranchPanelStore`, `useShipmentPanelStore`) — never routed pages.
- shadcn/ui components from `@/components/ui/*` only. On-scale type classes only (`text-2xs`, `text-xs`, `text-sm`, `text-base`, `text-lg`, `text-xl`, `text-2xl`) — no arbitrary `text-[13px]`.
- Status never by colour alone: stage badges carry a label, stale pings carry the word "stale", exception counts carry "open".
- Permissions (never role names): Riders page `rider.read`; Map tab and location cards `rider.location.read`; Cash column and cash card `cod.read`; Assign task `delivery.assign`; Add rider / Add vehicle `rider.create`; Branches page `branch.read`; Add branch `branch.create`; branch Sheet Shipments/Exceptions sections `report.read`; Change delivery zone `branch.update` (existing). Hidden when missing.
- Web checks (run in `src/Web`): `npm run build`, `npm run lint`, `npm test` pass; `npx tsc --noEmit` reports **0 errors** (delete a stale `tsconfig.tsbuildinfo` if old errors persist). Backend: `dotnet build` and the listed `dotnet test` projects pass. Lookup and API tests need `courier-postgres-1` (port 5434), `courier-redis-1`, `courier-rabbitmq-1` running — `docker start` them; never `docker compose up` a second Postgres. Run Api.Tests with the `--filter` shown in each task.
- Never assert on ICU month text in tests; compute expected strings with the same formatter or assert on non-date text.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` verbatim.

### Spec reconciliations (code wins; already folded into the tasks)

1. **Custody buckets.** `CurrentBranchId` is set to `null` when a parcel goes `OutForDelivery` and stays null through `DeliveryFailed` and `NdrCreated` (`ShipmentService.Transitions.cs`). So **On hand** = status in {`AtOriginBranch`, `Sorted`, `AtDestinationBranch`, `RtoInitiated`, `RtoAtOrigin`} grouped by `CurrentBranchId` (`RtoInitiated` sets `CurrentBranchId = DestinationBranchId`), and **Out for delivery** = status `OutForDelivery` grouped by **`DestinationBranchId`**. `DeliveryFailed`/`NdrCreated` parcels are still in rider custody and are counted by the Exceptions column (open NDRs), not On hand.
2. **Sparse activity rows.** `IBranchLookupService` has no "all branch ids of the tenant" read and 44 test fakes implement it, so `GET /branches/activity` returns one row per branch **in scope** for a scoped user (zero rows included) and, for an all-branches user, one row per branch **with any activity**. A branch missing from a loaded response has all zeros; the web renders `0` for it only once activity has loaded.
3. **Last ping.** `lastPingAt` is the latest `ReceivedAt` (server clock — the domain comment on `RiderLocationPing` says it anchors "last seen") among **today's** pings (`ReceivedAt >= today 00:00 UTC`). No ping today → `null` → "No ping today".
4. **Rider today read** returns the rider's board row plus the tasks: `RiderTodayResponse(RiderBoardRow Rider, IReadOnlyList<RiderTodayTask> Tasks)`, so the Sheet needs no second board fetch for name, plate, counts or last ping. `GET /riders/{id}` still supplies the licence.
5. **Run done.** No code path ever sets `DeliveryRunStatus.Completed`. The strip keeps its three stages, and "Run done" truthfully reads 0 until a close-run action exists (follow-up 7).
6. **District names.** No district lookup exists. A new `useDistrictNames(provinceIds)` hook reads the existing `/api/geography/districts?provinceId=` per distinct province (shared cache with `useDistricts`).
7. **Proxy for `GET /riders/{id}`** did not exist; it is added.
8. **`GET /vehicles` is not branch-scoped.** With a branch picked, the Fleet tab hides vehicles assigned to riders that are not on the board; unassigned vehicles always show. Logged as follow-up 8.
9. **Map branch.** Users with exactly one scoped branch (no picker) get that branch on the Map tab without picking.
10. **Strip counts exclude inactive riders**; inactive riders appear in the table only with "Show inactive".
11. **`BranchRecord`** is replaced by the restyled Sheet; its delivery-zone `Select` moves to `BranchZoneControl` in the footer.
12. **Add vehicle / Add rider / Assign task Sheets** are already shadcn Sheets and keep their fields, validation and requests; this slice only changes where they are opened from.

## Review Focus

1. **A branch-scoped user pasting another branch's rider id** → `404` from `GET /riders/{id}` and `/riders/{id}/today`, no data, no 403. Pinned in Task 3 (unit) and Task 6 (API).
2. **A rider whose only pings are from yesterday** → `lastPingAt` null, "No ping today", and an On run rider like that counts as stale in the strip. Pinned in Task 2 (unit) and Task 7 (model).
3. **A run with zero tasks, or a rider with no run** → "0 of 0" / "No run", progress share 0, never `NaN`. Pinned in Task 7 (model) and Task 8 (page).
4. **A parcel out for delivery, failed, or in NDR** → counted at its destination branch as Out for delivery / not On hand, never dropped by a null `CurrentBranchId`. Pinned in Task 4 (lookup).
5. **Activity read failing while the branch list loads** → table renders with "—" in the four live columns plus an inline retry, never `0`. Pinned in Task 12.

## File Map

**Backend**
- `src/Modules/Identity/Application/IUserDirectoryLookupService.cs` (new), `UserDirectoryLookupService.cs` (new); `src/Api/Program.cs` (DI).
- `src/Modules/Riders/Application/RiderService.cs` (two ctor deps), `RiderDtos.cs` (board/today records), `RiderService.Board.cs` (new: board, today), `RiderService.Reads.cs` (scoped `GetByIdAsync`, `FindReadableRiderAsync`), `IRiderReportingLookupService.cs` + `RiderReportingLookupService.cs` (`GetRidersOutCountsAsync`), `src/Modules/Riders/Presentation/RidersEndpoints.cs`.
- `src/Modules/Shipments/Application/IShipmentReportingLookupService.cs` + `ShipmentReportingLookupService.cs` (`GetBranchCustodyCountsAsync`).
- `src/Modules/Ndr/Application/INdrReportingLookupService.cs` + `NdrReportingLookupService.cs` (`GetOpenNdrCountsByBranchAsync`).
- `src/Modules/Reports/Application/ReportsDtos.cs`, `BranchActivityService.cs` (new), `src/Modules/Reports/Presentation/ReportsEndpoints.cs`.
- Tests: `tests/Identity.Tests/UserDirectoryLookupServiceTests.cs` (new); `tests/Riders.Tests/{RiderReadTestSupport.cs (new), RiderServiceBoardTests.cs (new), RiderServiceRiderReadsTests.cs (new), RiderServiceTasksTests.cs, RiderServiceReadsTests.cs, RiderServiceDeliveryAttemptReadsTests.cs, RiderServiceCreationTests.cs, RiderServiceAttemptsTests.cs, RiderReportingLookupServiceTests.cs}`; `tests/Shipments.Tests/ShipmentReportingLookupServiceTests.cs`; `tests/Ndr.Tests/NdrReportingLookupServiceTests.cs`; `tests/Manifests.Tests/FakeShipmentReportingLookup.cs`; `tests/Reports.Tests/{Fakes.cs, BranchActivityServiceTests.cs (new)}`; `tests/Api.Tests/RidersEndpointsTests.cs`.

**Web (`src/Web/src`)**
- Data: `features/riders/types.ts`, `features/branches/types.ts`; hooks `features/riders/hooks/{useRiderBoard,useRider,useRiderToday}.ts`, `features/rider-tracking/hooks/useRunBreadcrumbs.ts`, `features/branches/hooks/useBranchActivity.ts`, `features/geography/hooks/useDistrictNames.ts`; proxies `app/api/riders/board/route.ts`, `app/api/riders/[id]/route.ts`, `app/api/riders/[id]/today/route.ts`, `app/api/branches/activity/route.ts`; models `features/riders/lib/riderModel.ts`, `features/branches/lib/branchModel.ts`; `features/riders/store/riderPanelStore.ts`.
- Riders: `app/(staff)/riders/page.tsx`; components `features/riders/components/{RiderStageBadge,RidersBoardTab,RidersMapTab,RidersFleetTab,RiderDetailSheet,RiderTodayCard,RiderLocationCard,RiderCashCard}.tsx`; `features/rider-tracking/components/{LiveMap,RouteMiniMap}.tsx`; `app/(staff)/riders/live-map/page.tsx` (redirect); `app/(staff)/layout.tsx`; `lib/navigation/navItems.ts`; `messages/{en,ne}.json`.
- Branches: `app/(staff)/branches/page.tsx`; `features/branches/components/{BranchDetailSheet,BranchRightNow,BranchLocationCard,BranchZoneControl}.tsx`; delete `features/branches/BranchRecord.tsx` (+ test).

**Docs:** `docs/API-SPEC.md` (§6.6–§6.7), `docs/FRONTEND-ROADMAP.md` (R3b-2 row + follow-ups).

---

### Task 1: Identity — read-only user name lookup

**Files:**
- Create: `src/Modules/Identity/Application/IUserDirectoryLookupService.cs`
- Create: `src/Modules/Identity/Application/UserDirectoryLookupService.cs`
- Modify: `src/Api/Program.cs:168` (DI line after `IUserProvisioningService`)
- Test: `tests/Identity.Tests/UserDirectoryLookupServiceTests.cs`

**Interfaces:**
- Produces: `IUserDirectoryLookupService.GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) → Task<IReadOnlyDictionary<Guid, string>>` (Task 2 consumes it).

- [ ] **Step 1: Write the failing tests**

Create `tests/Identity.Tests/UserDirectoryLookupServiceTests.cs`:

```csharp
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Identity.Tests;

public class UserDirectoryLookupServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId { get; set; }
    }

    private static Organization BuildOrganization(Guid id) => new() { Id = id, Name = $"Org {id}", Slug = $"org-{id:N}" };

    private static User BuildUser(Guid organizationId, string fullName) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = organizationId, Email = $"user-{Guid.NewGuid():N}@example.com",
        PasswordHash = "not-a-real-hash", FullName = fullName
    };

    [Fact]
    public async Task GetFullNamesAsync_ReturnsNamesOfThisTenantsUsersOnly()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        dbContext.Add(BuildOrganization(orgA));
        dbContext.Add(BuildOrganization(orgB));
        var asha = BuildUser(orgA, "Asha Gurung");
        var bikash = BuildUser(orgA, "Bikash Rai");
        var otherTenant = BuildUser(orgB, "Other Tenant");
        dbContext.AddRange(asha, bikash, otherTenant);
        await dbContext.SaveChangesAsync();
        tenantProvider.OrganizationId = orgA;
        var service = new UserDirectoryLookupService(dbContext);

        var names = await service.GetFullNamesAsync(new[] { asha.Id, bikash.Id, otherTenant.Id, Guid.NewGuid(), asha.Id });

        Assert.Equal(2, names.Count);
        Assert.Equal("Asha Gurung", names[asha.Id]);
        Assert.Equal("Bikash Rai", names[bikash.Id]);
        Assert.False(names.ContainsKey(otherTenant.Id));
    }

    [Fact]
    public async Task GetFullNamesAsync_EmptyInput_RunsNoQuery()
    {
        // A disposed context throws on any query, so an empty result proves no query ran.
        var dbContext = TestDbContextFactory.Create(new FixedTenantProvider { OrganizationId = Guid.NewGuid() });
        await dbContext.DisposeAsync();
        var service = new UserDirectoryLookupService(dbContext);

        var names = await service.GetFullNamesAsync(Array.Empty<Guid>());

        Assert.Empty(names);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter "FullyQualifiedName~UserDirectoryLookupServiceTests"`
Expected: build FAIL — `UserDirectoryLookupService` does not exist.

- [ ] **Step 3: Write the interface and implementation**

Create `src/Modules/Identity/Application/IUserDirectoryLookupService.cs`:

```csharp
namespace Courier.Identity.Application;

// Read-only display-name lookup for other modules (F15 R3b-2: the rider board). Operations roles
// lack user.read, so rider names are resolved server-side here instead of through /users. Tenant
// scope is the User query filter: ids from another organization are simply absent from the result.
public interface IUserDirectoryLookupService
{
    Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
}
```

Create `src/Modules/Identity/Application/UserDirectoryLookupService.cs`:

```csharp
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class UserDirectoryLookupService : IUserDirectoryLookupService
{
    private readonly DbContext _dbContext;

    public UserDirectoryLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }
        var idArray = userIds.Distinct().ToArray();
        return await _dbContext.Set<User>().AsNoTracking()
            .Where(u => idArray.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
    }
}
```

In `src/Api/Program.cs`, directly after `builder.Services.AddScoped<IUserProvisioningService, UserProvisioningService>();` add:

```csharp
builder.Services.AddScoped<IUserDirectoryLookupService, UserDirectoryLookupService>();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter "FullyQualifiedName~UserDirectoryLookupServiceTests"`
Expected: PASS (2 tests). Then `dotnet build` → 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Identity/Application/IUserDirectoryLookupService.cs src/Modules/Identity/Application/UserDirectoryLookupService.cs src/Api/Program.cs tests/Identity.Tests/UserDirectoryLookupServiceTests.cs
git commit -m "$(cat <<'EOF'
feat(identity): read-only user full-name lookup for other modules

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Riders — board read (`GET /riders/board`)

**Files:**
- Modify: `src/Modules/Riders/Application/RiderService.cs` (ctor: two new dependencies)
- Modify: `src/Modules/Riders/Application/RiderDtos.cs` (append records)
- Create: `src/Modules/Riders/Application/RiderService.Board.cs`
- Modify: `src/Modules/Riders/Presentation/RidersEndpoints.cs`
- Create: `tests/Riders.Tests/RiderReadTestSupport.cs`
- Create: `tests/Riders.Tests/RiderServiceBoardTests.cs`
- Modify (ctor call sites): `tests/Riders.Tests/RiderServiceTasksTests.cs:171`, `RiderServiceReadsTests.cs:140`, `RiderServiceDeliveryAttemptReadsTests.cs:134`, `RiderServiceCreationTests.cs:151`, `RiderServiceAttemptsTests.cs:203`

**Interfaces:**
- Consumes: `IUserDirectoryLookupService.GetFullNamesAsync` (Task 1); existing `IShipmentReportingLookupService` (`Courier.Shipments.Application`).
- Produces:
  - `RiderService` ctor gains, appended after `IShipmentTaskLookupService shipmentTaskLookupService`: `IShipmentReportingLookupService shipmentReportingLookupService, IUserDirectoryLookupService userDirectoryLookupService`.
  - Records `RiderBoardRunSummary(Guid Id, DeliveryRunStatus Status)`, `RiderBoardRow(Guid RiderId, Guid UserId, string FullName, Guid BaseBranchId, RiderRoleType RoleType, bool IsActive, string? VehiclePlate, RiderBoardRunSummary? Run, int TasksAssigned, int TasksCompleted, int TasksFailed, DateTimeOffset? LastPingAt)`, `RiderBoardResponse(IReadOnlyList<RiderBoardRow> Items)`.
  - `RiderService.GetBoardAsync(Guid? branchId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) → Task<RiderBoardResponse>`.
  - Private `BuildBoardRowsAsync(IReadOnlyCollection<Rider> riders, CancellationToken)` (Task 3 reuses it).
  - Test support: `RiderReadTestSupport.CreateContext()`, `RiderReadTestSupport.ActingUser()`, `RiderReadTestSupport.CreateService(DbContext, IReadOnlySet<Guid>? accessible, StubShipmentReportingLookup? shipments = null, StubUserDirectoryLookup? names = null)`, top-level `StubShipmentReportingLookup` (`Awbs`, `AwbLookupCalls`) and `StubUserDirectoryLookup` (`Names`).
  - JSON: `GET /api/v1/riders/board?branchId=` → `{ "items": [{ "riderId", "userId", "fullName", "baseBranchId", "roleType": "DeliveryRider", "isActive", "vehiclePlate", "run": { "id", "status": "Active" } | null, "tasksAssigned", "tasksCompleted", "tasksFailed", "lastPingAt" }] }`.

- [ ] **Step 1: Add the records**

Append to `src/Modules/Riders/Application/RiderDtos.cs`:

```csharp
// F15 R3b-2 riders board and rider Sheet. Counts are today's run only (UTC date). LastPingAt is the
// latest server-received ping today, null when the rider has not pinged today.
public record RiderBoardRunSummary(Guid Id, DeliveryRunStatus Status);

public record RiderBoardRow(
    Guid RiderId, Guid UserId, string FullName, Guid BaseBranchId, RiderRoleType RoleType, bool IsActive,
    string? VehiclePlate, RiderBoardRunSummary? Run,
    int TasksAssigned, int TasksCompleted, int TasksFailed, DateTimeOffset? LastPingAt);

public record RiderBoardResponse(IReadOnlyList<RiderBoardRow> Items);

public record RiderTodayTask(Guid TaskId, Guid ShipmentId, string Awb, DeliveryTaskType Type, int Sequence, DeliveryTaskStatus Status);

public record RiderTodayResponse(RiderBoardRow Rider, IReadOnlyList<RiderTodayTask> Tasks);
```

(`RiderTodayTask`/`RiderTodayResponse` are used in Task 3; declaring them here keeps the DTO edit in one place.)

- [ ] **Step 2: Add the constructor dependencies**

In `src/Modules/Riders/Application/RiderService.cs`:

Add two fields after `private readonly IShipmentTaskLookupService _shipmentTaskLookupService;`:

```csharp
    private readonly IShipmentReportingLookupService _shipmentReportingLookupService;
    private readonly IUserDirectoryLookupService _userDirectoryLookupService;
```

Change the end of the constructor parameter list from

```csharp
        IOutboxWriter outboxWriter,
        IShipmentTaskLookupService shipmentTaskLookupService)
```

to

```csharp
        IOutboxWriter outboxWriter,
        IShipmentTaskLookupService shipmentTaskLookupService,
        IShipmentReportingLookupService shipmentReportingLookupService,
        IUserDirectoryLookupService userDirectoryLookupService)
```

and after `_shipmentTaskLookupService = shipmentTaskLookupService;` add:

```csharp
        _shipmentReportingLookupService = shipmentReportingLookupService;
        _userDirectoryLookupService = userDirectoryLookupService;
```

(`Courier.Shipments.Application` and `Courier.Identity.Application` are already imported. `Program.cs` registers both interfaces already — `IUserDirectoryLookupService` since Task 1.)

- [ ] **Step 3: Create the shared test support**

Create `tests/Riders.Tests/RiderReadTestSupport.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Events.Application;
using Courier.Identity.Application;
using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.Riders.Application;
using Courier.Riders.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Tests;

// Shared by every RiderService construction in this project: RiderService needs both lookups,
// and only the R3b-2 reads call them. Unused reporting reads throw so an accidental call fails loudly.
internal sealed class StubShipmentReportingLookup : IShipmentReportingLookupService
{
    public Dictionary<Guid, string> Awbs { get; } = new();
    public int AwbLookupCalls { get; private set; }

    public Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default)
    {
        AwbLookupCalls++;
        IReadOnlyDictionary<Guid, string> result = shipmentIds.Where(Awbs.ContainsKey).ToDictionary(id => id, id => Awbs[id]);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCohortStatusCountsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchShipmentCounts> GetBranchShipmentCountsAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TimeSpan?> GetAverageDeliveryTimeAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetShipmentIdsByDestinationBranchAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ShipmentExportRow>> GetShipmentExportRowsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCurrentStatusCountsAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class StubUserDirectoryLookup : IUserDirectoryLookupService
{
    public Dictionary<Guid, string> Names { get; } = new();

    public Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<Guid, string> result = userIds.Distinct().Where(Names.ContainsKey).ToDictionary(id => id, id => Names[id]);
        return Task.FromResult(result);
    }
}

// InMemory context + RiderService factory for the R3b-2 read tests (board, today, scoped single
// read). The InMemory context has no tenant query filters; tenant isolation is pinned by Api.Tests.
internal static class RiderReadTestSupport
{
    public sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RiderConfiguration());
            modelBuilder.ApplyConfiguration(new VehicleConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryRunConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryTaskConfiguration());
            modelBuilder.ApplyConfiguration(new RiderLocationPingConfiguration());
        }
    }

    public static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    public static ClaimsPrincipal ActingUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    public static RiderService CreateService(
        DbContext dbContext, IReadOnlySet<Guid>? accessible,
        StubShipmentReportingLookup? shipments = null, StubUserDirectoryLookup? names = null) =>
        new(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), new ScopedBranchesChecker(accessible), new NotSupportedShipmentTransitionService(),
            new NotSupportedObjectStorage(), new NotSupportedNdrCreationService(), new NotSupportedCodCollectionService(),
            new AlwaysTruePermissionChecker(), new NoopOutboxWriter(), new NotSupportedShipmentTaskLookupService(),
            shipments ?? new StubShipmentReportingLookup(), names ?? new StubUserDirectoryLookup());

    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
        public Guid? OrganizationId { get; }
    }

    private sealed class NoopAuditLogger : IAuditLogger
    {
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ScopedBranchesChecker : IBranchAuthorizationChecker
    {
        private readonly IReadOnlySet<Guid>? _accessible;
        public ScopedBranchesChecker(IReadOnlySet<Guid>? accessible) => _accessible = accessible;
        public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(_accessible);
        public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_accessible is null || _accessible.Contains(branchId));
    }

    private sealed class NotSupportedUserProvisioningService : IUserProvisioningService
    {
        public Task<Guid> ProvisionStaffUserAsync(ProvisionStaffUserRequest request, Guid organizationId, Guid grantedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NotSupportedBranchLookup : IBranchLookupService
    {
        public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NotSupportedShipmentTransitionService : IShipmentTransitionService
    {
        public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> GetResponseAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NotSupportedObjectStorage : IObjectStorage
    {
        public Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NotSupportedNdrCreationService : INdrCreationService
    {
        public Task CreateFromFailedAttemptAsync(Guid shipmentId, Guid organizationId, NdrReason reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NotSupportedCodCollectionService : ICodCollectionService
    {
        public Task MarkCollectedAsync(Guid shipmentId, decimal collectedAmount, Guid riderId, Guid deliveryProofId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class AlwaysTruePermissionChecker : IPermissionChecker
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class NoopOutboxWriter : IOutboxWriter
    {
        public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NotSupportedShipmentTaskLookupService : IShipmentTaskLookupService
    {
        public Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
```

If the build reports `IShipmentTransitionService` members not implemented (the interface grew since this plan was written), copy the missing signatures from `ScanInfoShipmentTransitionService` in `tests/Riders.Tests/RiderServiceDeliveryAttemptReadsTests.cs` with a `throw new NotSupportedException()` body.

- [ ] **Step 4: Update the five existing constructor call sites**

In each of these files, replace the single occurrence of the text on the left with the text on the right (the construction gains the two new arguments at the end):

| File | Replace | With |
|---|---|---|
| `tests/Riders.Tests/RiderServiceTasksTests.cs` (line ~171) | `new NoopOutboxWriter(), new NotSupportedShipmentTaskLookupService());` | `new NoopOutboxWriter(), new NotSupportedShipmentTaskLookupService(), new StubShipmentReportingLookup(), new StubUserDirectoryLookup());` |
| `tests/Riders.Tests/RiderServiceReadsTests.cs` (line ~140) | `shipmentTaskLookup ?? new NotSupportedShipmentTaskLookupService());` | `shipmentTaskLookup ?? new NotSupportedShipmentTaskLookupService(), new StubShipmentReportingLookup(), new StubUserDirectoryLookup());` |
| `tests/Riders.Tests/RiderServiceDeliveryAttemptReadsTests.cs` (line ~134) | `new NoopOutboxWriter(), new NotSupportedShipmentTaskLookupService());` | `new NoopOutboxWriter(), new NotSupportedShipmentTaskLookupService(), new StubShipmentReportingLookup(), new StubUserDirectoryLookup());` |
| `tests/Riders.Tests/RiderServiceCreationTests.cs` (line ~151) | `new NoopOutboxWriter(), new NotSupportedShipmentTaskLookupService());` | `new NoopOutboxWriter(), new NotSupportedShipmentTaskLookupService(), new StubShipmentReportingLookup(), new StubUserDirectoryLookup());` |
| `tests/Riders.Tests/RiderServiceAttemptsTests.cs` (line ~203) | `outboxWriter, new NotSupportedShipmentTaskLookupService());` | `outboxWriter, new NotSupportedShipmentTaskLookupService(), new StubShipmentReportingLookup(), new StubUserDirectoryLookup());` |

Run: `dotnet build tests/Riders.Tests/Riders.Tests.csproj`
Expected: build succeeds (0 errors). If any other file constructs `RiderService` (`grep -rn "RiderService(" tests src --include=*.cs`), give it the same two arguments.

- [ ] **Step 5: Write the failing board tests**

Create `tests/Riders.Tests/RiderServiceBoardTests.cs`:

```csharp
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Xunit;
using static Courier.Riders.Tests.RiderReadTestSupport;

namespace Courier.Riders.Tests;

public class RiderServiceBoardTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

    private static Rider AddRider(TestDbContext dbContext, Guid branchId, bool isActive = true, RiderRoleType roleType = RiderRoleType.DeliveryRider)
    {
        var rider = new Rider
        {
            Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), UserId = Guid.NewGuid(), BaseBranchId = branchId,
            LicenseNumber = $"LIC-{Guid.NewGuid():N}"[..12], RoleType = roleType, IsActive = isActive
        };
        dbContext.Set<Rider>().Add(rider);
        return rider;
    }

    private static DeliveryRun AddRun(TestDbContext dbContext, Rider rider, DeliveryRunStatus status, DateOnly? date = null)
    {
        var run = new DeliveryRun { Id = Guid.NewGuid(), RiderId = rider.Id, BranchId = rider.BaseBranchId, Date = date ?? Today, Status = status };
        dbContext.Set<DeliveryRun>().Add(run);
        return run;
    }

    private static void AddTask(TestDbContext dbContext, DeliveryRun run, int sequence, DeliveryTaskStatus status) =>
        dbContext.Set<DeliveryTask>().Add(new DeliveryTask
        {
            Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = Guid.NewGuid(), Type = DeliveryTaskType.Delivery,
            Sequence = sequence, Status = status
        });

    private static void AddPing(TestDbContext dbContext, Rider rider, DateTimeOffset receivedAt) =>
        dbContext.Set<RiderLocationPing>().Add(new RiderLocationPing
        {
            Id = Guid.NewGuid(), RiderId = rider.Id, Latitude = 27.7m, Longitude = 85.3m, AccuracyMeters = 8m,
            RecordedAt = receivedAt, ReceivedAt = receivedAt
        });

    [Fact]
    public async Task GetBoardAsync_ScopedUser_ReturnsOnlyRidersBasedInScope()
    {
        await using var dbContext = CreateContext();
        var visibleBranch = Guid.NewGuid();
        var visible = AddRider(dbContext, visibleBranch);
        AddRider(dbContext, Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new HashSet<Guid> { visibleBranch });

        var board = await service.GetBoardAsync(null, ActingUser());

        Assert.Equal(visible.Id, Assert.Single(board.Items).RiderId);
    }

    [Fact]
    public async Task GetBoardAsync_BranchIdOutsideScope_ReturnsEmpty()
    {
        await using var dbContext = CreateContext();
        var visibleBranch = Guid.NewGuid();
        var hiddenBranch = Guid.NewGuid();
        AddRider(dbContext, visibleBranch);
        AddRider(dbContext, hiddenBranch);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new HashSet<Guid> { visibleBranch });

        var board = await service.GetBoardAsync(hiddenBranch, ActingUser());

        Assert.Empty(board.Items);
    }

    [Fact]
    public async Task GetBoardAsync_BranchIdNarrowsAllBranchesScope()
    {
        await using var dbContext = CreateContext();
        var branchA = Guid.NewGuid();
        var riderA = AddRider(dbContext, branchA);
        AddRider(dbContext, Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, null);

        var board = await service.GetBoardAsync(branchA, ActingUser());

        Assert.Equal(riderA.Id, Assert.Single(board.Items).RiderId);
    }

    [Fact]
    public async Task GetBoardAsync_NoRunToday_RowHasNoRunAndZeroCounts()
    {
        await using var dbContext = CreateContext();
        var rider = AddRider(dbContext, Guid.NewGuid());
        var yesterdaysRun = AddRun(dbContext, rider, DeliveryRunStatus.Active, Today.AddDays(-1));
        AddTask(dbContext, yesterdaysRun, 1, DeliveryTaskStatus.Pending);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, null);

        var row = Assert.Single((await service.GetBoardAsync(null, ActingUser())).Items);

        Assert.Null(row.Run);
        Assert.Equal(0, row.TasksAssigned);
        Assert.Equal(0, row.TasksCompleted);
        Assert.Equal(0, row.TasksFailed);
    }

    [Fact]
    public async Task GetBoardAsync_ActiveRun_CountsTodaysTasksByStatus()
    {
        await using var dbContext = CreateContext();
        var rider = AddRider(dbContext, Guid.NewGuid());
        var run = AddRun(dbContext, rider, DeliveryRunStatus.Active);
        AddTask(dbContext, run, 1, DeliveryTaskStatus.Completed);
        AddTask(dbContext, run, 2, DeliveryTaskStatus.Completed);
        AddTask(dbContext, run, 3, DeliveryTaskStatus.Failed);
        AddTask(dbContext, run, 4, DeliveryTaskStatus.Pending);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, null);

        var row = Assert.Single((await service.GetBoardAsync(null, ActingUser())).Items);

        Assert.Equal(new RiderBoardRunSummary(run.Id, DeliveryRunStatus.Active), row.Run);
        Assert.Equal(4, row.TasksAssigned);
        Assert.Equal(2, row.TasksCompleted);
        Assert.Equal(1, row.TasksFailed);
    }

    [Fact]
    public async Task GetBoardAsync_CompletedAndActiveRunsToday_PrefersActive()
    {
        await using var dbContext = CreateContext();
        var onlyCompleted = AddRider(dbContext, Guid.NewGuid());
        var completedRun = AddRun(dbContext, onlyCompleted, DeliveryRunStatus.Completed);
        var both = AddRider(dbContext, Guid.NewGuid());
        AddRun(dbContext, both, DeliveryRunStatus.Completed);
        var activeRun = AddRun(dbContext, both, DeliveryRunStatus.Active);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, null);

        var items = (await service.GetBoardAsync(null, ActingUser())).Items;

        Assert.Equal(new RiderBoardRunSummary(completedRun.Id, DeliveryRunStatus.Completed), items.Single(r => r.RiderId == onlyCompleted.Id).Run);
        Assert.Equal(new RiderBoardRunSummary(activeRun.Id, DeliveryRunStatus.Active), items.Single(r => r.RiderId == both.Id).Run);
    }

    [Fact]
    public async Task GetBoardAsync_ReportsLatestPingToday_AndNullWhenOnlyYesterday()
    {
        await using var dbContext = CreateContext();
        var pinged = AddRider(dbContext, Guid.NewGuid());
        var latest = DateTimeOffset.UtcNow;
        AddPing(dbContext, pinged, latest.AddSeconds(-1));
        AddPing(dbContext, pinged, latest);
        var yesterdayOnly = AddRider(dbContext, Guid.NewGuid());
        AddPing(dbContext, yesterdayOnly, DateTimeOffset.UtcNow.AddDays(-1));
        var never = AddRider(dbContext, Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, null);

        var items = (await service.GetBoardAsync(null, ActingUser())).Items;

        Assert.Equal(latest, items.Single(r => r.RiderId == pinged.Id).LastPingAt);
        Assert.Null(items.Single(r => r.RiderId == yesterdayOnly.Id).LastPingAt);
        Assert.Null(items.Single(r => r.RiderId == never.Id).LastPingAt);
    }

    [Fact]
    public async Task GetBoardAsync_VehiclePlate_FirstByPlateNumber_NullWhenNone()
    {
        await using var dbContext = CreateContext();
        var withTwo = AddRider(dbContext, Guid.NewGuid());
        dbContext.Set<Vehicle>().Add(new Vehicle { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), RiderId = withTwo.Id, Type = "Bike", PlateNumber = "BA 3 PA 9999" });
        dbContext.Set<Vehicle>().Add(new Vehicle { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), RiderId = withTwo.Id, Type = "Bike", PlateNumber = "BA 2 PA 1111" });
        var without = AddRider(dbContext, Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, null);

        var items = (await service.GetBoardAsync(null, ActingUser())).Items;

        Assert.Equal("BA 2 PA 1111", items.Single(r => r.RiderId == withTwo.Id).VehiclePlate);
        Assert.Null(items.Single(r => r.RiderId == without.Id).VehiclePlate);
    }

    [Fact]
    public async Task GetBoardAsync_NamesFromDirectory_FallBackToRider()
    {
        await using var dbContext = CreateContext();
        var named = AddRider(dbContext, Guid.NewGuid());
        var unnamed = AddRider(dbContext, Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var names = new StubUserDirectoryLookup();
        names.Names[named.UserId] = "Asha Gurung";
        var service = CreateService(dbContext, null, names: names);

        var items = (await service.GetBoardAsync(null, ActingUser())).Items;

        Assert.Equal("Asha Gurung", items.Single(r => r.RiderId == named.Id).FullName);
        Assert.Equal("Rider", items.Single(r => r.RiderId == unnamed.Id).FullName);
    }

    [Fact]
    public async Task GetBoardAsync_IncludesInactiveRiders()
    {
        await using var dbContext = CreateContext();
        var inactive = AddRider(dbContext, Guid.NewGuid(), isActive: false);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, null);

        var row = Assert.Single((await service.GetBoardAsync(null, ActingUser())).Items);

        Assert.Equal(inactive.Id, row.RiderId);
        Assert.False(row.IsActive);
    }

    [Fact]
    public async Task GetBoardAsync_OrdersOnRunThenRunDoneThenOffDuty_ThenByName()
    {
        await using var dbContext = CreateContext();
        var branch = Guid.NewGuid();
        var offDuty = AddRider(dbContext, branch);
        var done = AddRider(dbContext, branch);
        AddRun(dbContext, done, DeliveryRunStatus.Completed);
        var onRunZed = AddRider(dbContext, branch);
        AddRun(dbContext, onRunZed, DeliveryRunStatus.Active);
        var onRunAsha = AddRider(dbContext, branch);
        AddRun(dbContext, onRunAsha, DeliveryRunStatus.Active);
        await dbContext.SaveChangesAsync();
        var names = new StubUserDirectoryLookup();
        names.Names[offDuty.UserId] = "Aaron";
        names.Names[done.UserId] = "Bina";
        names.Names[onRunZed.UserId] = "Zed";
        names.Names[onRunAsha.UserId] = "asha";
        var service = CreateService(dbContext, null, names: names);

        var items = (await service.GetBoardAsync(null, ActingUser())).Items;

        Assert.Equal(new[] { "asha", "Zed", "Bina", "Aaron" }, items.Select(r => r.FullName).ToArray());
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj --filter "FullyQualifiedName~RiderServiceBoardTests"`
Expected: build FAIL — `RiderService` has no `GetBoardAsync`.

- [ ] **Step 7: Implement the board**

Create `src/Modules/Riders/Application/RiderService.Board.cs`:

```csharp
using System.Security.Claims;
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
    // F15 R3b-2 riders board: one row per rider based in the caller's rider.read scope (inactive
    // riders included, flagged), narrowed by branchId. A branchId outside scope narrows to nothing.
    // "Today" is the UTC date, the same as GetMyTasksTodayAsync and RecordLocationPingAsync.
    public async Task<RiderBoardResponse> GetBoardAsync(Guid? branchId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Rider>().AsNoTracking().AsQueryable();
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "rider.read", cancellationToken);
        if (accessibleBranchIds is not null)
        {
            var accessibleBranchIdsArray = accessibleBranchIds.ToArray();
            query = query.Where(r => accessibleBranchIdsArray.Contains(r.BaseBranchId));
        }
        if (branchId is Guid filterBranchId)
        {
            query = query.Where(r => r.BaseBranchId == filterBranchId);
        }

        var riders = await query.ToListAsync(cancellationToken);
        var rows = await BuildBoardRowsAsync(riders, cancellationToken);
        return new RiderBoardResponse(rows
            .OrderBy(BoardStageRank)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private static int BoardStageRank(RiderBoardRow row) => row.Run?.Status switch
    {
        DeliveryRunStatus.Active => 0,
        DeliveryRunStatus.Completed => 1,
        _ => 2
    };

    // The domain should allow one run per rider per day; if there are more, the Active one wins,
    // then the highest Id, so the choice is deterministic.
    private static DeliveryRun PickTodayRun(IEnumerable<DeliveryRun> runs) =>
        runs.OrderBy(r => r.Status == DeliveryRunStatus.Active ? 0 : 1).ThenByDescending(r => r.Id).First();

    // A fixed number of queries whatever the rider count: runs, grouped task counts, latest ping,
    // vehicles, names. DeliveryRun/DeliveryTask/RiderLocationPing carry no OrganizationId; every
    // query here is keyed by rider ids that came from the tenant-filtered Rider set.
    private async Task<List<RiderBoardRow>> BuildBoardRowsAsync(IReadOnlyCollection<Rider> riders, CancellationToken cancellationToken)
    {
        if (riders.Count == 0)
        {
            return new List<RiderBoardRow>();
        }

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var todayStart = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var riderIds = riders.Select(r => r.Id).ToArray();

        var runs = await _dbContext.Set<DeliveryRun>().AsNoTracking()
            .Where(r => riderIds.Contains(r.RiderId) && r.Date == today)
            .ToListAsync(cancellationToken);
        var runByRider = runs.GroupBy(r => r.RiderId).ToDictionary(g => g.Key, g => PickTodayRun(g));
        var runIds = runByRider.Values.Select(r => r.Id).ToArray();

        var taskCounts = await _dbContext.Set<DeliveryTask>().AsNoTracking()
            .Where(t => runIds.Contains(t.DeliveryRunId))
            .GroupBy(t => new { t.DeliveryRunId, t.Status })
            .Select(g => new { g.Key.DeliveryRunId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var lastPings = await _dbContext.Set<RiderLocationPing>().AsNoTracking()
            .Where(p => riderIds.Contains(p.RiderId) && p.ReceivedAt >= todayStart)
            .GroupBy(p => p.RiderId)
            .Select(g => new { RiderId = g.Key, LastPingAt = g.Max(p => p.ReceivedAt) })
            .ToListAsync(cancellationToken);
        var lastPingByRider = lastPings.ToDictionary(p => p.RiderId, p => p.LastPingAt);

        var vehicles = await _dbContext.Set<Vehicle>().AsNoTracking()
            .Where(v => v.RiderId != null && riderIds.Contains(v.RiderId.Value))
            .Select(v => new { RiderId = v.RiderId!.Value, v.PlateNumber })
            .ToListAsync(cancellationToken);
        var plateByRider = vehicles
            .GroupBy(v => v.RiderId)
            .ToDictionary(g => g.Key, g => g.Select(v => v.PlateNumber).OrderBy(p => p, StringComparer.Ordinal).First());

        var names = await _userDirectoryLookupService.GetFullNamesAsync(riders.Select(r => r.UserId).Distinct().ToArray(), cancellationToken);

        int CountTasks(Guid runId, DeliveryTaskStatus? status) =>
            taskCounts.Where(c => c.DeliveryRunId == runId && (status == null || c.Status == status)).Sum(c => c.Count);

        return riders.Select(rider =>
        {
            runByRider.TryGetValue(rider.Id, out var run);
            return new RiderBoardRow(
                rider.Id,
                rider.UserId,
                names.TryGetValue(rider.UserId, out var fullName) ? fullName : "Rider",
                rider.BaseBranchId,
                rider.RoleType,
                rider.IsActive,
                plateByRider.TryGetValue(rider.Id, out var plate) ? plate : null,
                run is null ? null : new RiderBoardRunSummary(run.Id, run.Status),
                run is null ? 0 : CountTasks(run.Id, null),
                run is null ? 0 : CountTasks(run.Id, DeliveryTaskStatus.Completed),
                run is null ? 0 : CountTasks(run.Id, DeliveryTaskStatus.Failed),
                lastPingByRider.TryGetValue(rider.Id, out var pingAt) ? pingAt : null);
        }).ToList();
    }
}
```

In `src/Modules/Riders/Presentation/RidersEndpoints.cs`, directly after the `riders.MapGet("/summary", ...)` mapping (before `riders.MapGet("/me", ...)`), add:

```csharp
        riders.MapGet("/board", async (Guid? branchId, RiderService service, HttpContext httpContext) =>
            Results.Ok(await service.GetBoardAsync(branchId, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("rider.read");
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj`
Expected: PASS — the 11 new board tests and every existing Riders test (the Postgres-backed `RiderReportingLookupServiceTests` need `courier-postgres-1` running).

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Riders tests/Riders.Tests
git commit -m "$(cat <<'EOF'
feat(riders): batched branch-scoped rider board read

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Riders — scoped single read (security fix) and `GET /riders/{id}/today`

**Files:**
- Modify: `src/Modules/Riders/Application/RiderService.Reads.cs` (`GetByIdAsync`, new `FindReadableRiderAsync`)
- Modify: `src/Modules/Riders/Application/RiderService.Board.cs` (add `GetTodayAsync`)
- Modify: `src/Modules/Riders/Presentation/RidersEndpoints.cs`
- Create: `tests/Riders.Tests/RiderServiceRiderReadsTests.cs`
- Modify: `tests/Riders.Tests/RiderServiceReadsTests.cs` (existing `GetByIdAsync_UnknownId_ReturnsNull`)

**Interfaces:**
- Consumes: `BuildBoardRowsAsync`, `RiderTodayTask`, `RiderTodayResponse`, test support (Task 2).
- Produces:
  - `RiderService.GetByIdAsync(Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) → Task<RiderResponse?>` (null = unknown or out of `rider.read` scope → 404).
  - `RiderService.GetTodayAsync(Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) → Task<RiderTodayResponse?>`.
  - JSON: `GET /api/v1/riders/{id}/today` → `{ "rider": <board row>, "tasks": [{ "taskId", "shipmentId", "awb", "type": "Delivery", "sequence", "status": "Pending" }] }`, tasks ordered by `sequence`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Riders.Tests/RiderServiceRiderReadsTests.cs`:

```csharp
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Xunit;
using static Courier.Riders.Tests.RiderReadTestSupport;

namespace Courier.Riders.Tests;

public class RiderServiceRiderReadsTests
{
    private static Rider AddRider(TestDbContext dbContext, Guid branchId)
    {
        var rider = new Rider
        {
            Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), UserId = Guid.NewGuid(), BaseBranchId = branchId,
            LicenseNumber = "LIC-7", RoleType = RiderRoleType.DeliveryRider, IsActive = true
        };
        dbContext.Set<Rider>().Add(rider);
        return rider;
    }

    private static DeliveryRun AddActiveRun(TestDbContext dbContext, Rider rider)
    {
        var run = new DeliveryRun
        {
            Id = Guid.NewGuid(), RiderId = rider.Id, BranchId = rider.BaseBranchId,
            Date = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date), Status = DeliveryRunStatus.Active
        };
        dbContext.Set<DeliveryRun>().Add(run);
        return run;
    }

    private static DeliveryTask AddTask(TestDbContext dbContext, DeliveryRun run, int sequence, DeliveryTaskStatus status, DeliveryTaskType type = DeliveryTaskType.Delivery)
    {
        var task = new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = Guid.NewGuid(), Type = type, Sequence = sequence, Status = status };
        dbContext.Set<DeliveryTask>().Add(task);
        return task;
    }

    [Fact]
    public async Task GetByIdAsync_InScope_ReturnsRider()
    {
        await using var dbContext = CreateContext();
        var branch = Guid.NewGuid();
        var rider = AddRider(dbContext, branch);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new HashSet<Guid> { branch });

        var result = await service.GetByIdAsync(rider.Id, ActingUser());

        Assert.Equal("LIC-7", result!.LicenseNumber);
    }

    [Fact]
    public async Task GetByIdAsync_OutOfScope_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var rider = AddRider(dbContext, Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new HashSet<Guid> { Guid.NewGuid() });

        Assert.Null(await service.GetByIdAsync(rider.Id, ActingUser()));
    }

    [Fact]
    public async Task GetTodayAsync_UnknownRider_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, null);

        Assert.Null(await service.GetTodayAsync(Guid.NewGuid(), ActingUser()));
    }

    [Fact]
    public async Task GetTodayAsync_OutOfScope_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var rider = AddRider(dbContext, Guid.NewGuid());
        AddTask(dbContext, AddActiveRun(dbContext, rider), 1, DeliveryTaskStatus.Pending);
        await dbContext.SaveChangesAsync();
        var shipments = new StubShipmentReportingLookup();
        var service = CreateService(dbContext, new HashSet<Guid> { Guid.NewGuid() }, shipments);

        Assert.Null(await service.GetTodayAsync(rider.Id, ActingUser()));
        Assert.Equal(0, shipments.AwbLookupCalls);
    }

    [Fact]
    public async Task GetTodayAsync_ReturnsTasksBySequenceWithOneAwbLookup()
    {
        await using var dbContext = CreateContext();
        var rider = AddRider(dbContext, Guid.NewGuid());
        var run = AddActiveRun(dbContext, rider);
        var third = AddTask(dbContext, run, 3, DeliveryTaskStatus.Pending);
        var first = AddTask(dbContext, run, 1, DeliveryTaskStatus.Completed, DeliveryTaskType.Pickup);
        var second = AddTask(dbContext, run, 2, DeliveryTaskStatus.Failed);
        await dbContext.SaveChangesAsync();
        var shipments = new StubShipmentReportingLookup();
        shipments.Awbs[first.ShipmentId] = "AWB-001";
        shipments.Awbs[third.ShipmentId] = "AWB-003";
        var service = CreateService(dbContext, null, shipments);

        var today = await service.GetTodayAsync(rider.Id, ActingUser());

        Assert.NotNull(today);
        Assert.Equal(rider.Id, today!.Rider.RiderId);
        Assert.Equal(3, today.Rider.TasksAssigned);
        Assert.Equal(new[] { 1, 2, 3 }, today.Tasks.Select(t => t.Sequence).ToArray());
        Assert.Equal(new[] { "AWB-001", "", "AWB-003" }, today.Tasks.Select(t => t.Awb).ToArray());
        Assert.Equal(DeliveryTaskType.Pickup, today.Tasks[0].Type);
        Assert.Equal(DeliveryTaskStatus.Failed, today.Tasks[1].Status);
        Assert.Equal(second.Id, today.Tasks[1].TaskId);
        Assert.Equal(1, shipments.AwbLookupCalls);
    }

    [Fact]
    public async Task GetTodayAsync_NoRun_ReturnsRowAndNoTasksWithoutAwbLookup()
    {
        await using var dbContext = CreateContext();
        var rider = AddRider(dbContext, Guid.NewGuid());
        await dbContext.SaveChangesAsync();
        var shipments = new StubShipmentReportingLookup();
        var service = CreateService(dbContext, null, shipments);

        var today = await service.GetTodayAsync(rider.Id, ActingUser());

        Assert.Null(today!.Rider.Run);
        Assert.Empty(today.Tasks);
        Assert.Equal(0, shipments.AwbLookupCalls);
    }
}
```

In `tests/Riders.Tests/RiderServiceReadsTests.cs`, in `GetByIdAsync_UnknownId_ReturnsNull`, replace

```csharp
        var result = await service.GetByIdAsync(Guid.NewGuid());
```

with

```csharp
        var result = await service.GetByIdAsync(Guid.NewGuid(), ActingUser(Guid.NewGuid()));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj --filter "FullyQualifiedName~RiderServiceRiderReadsTests|FullyQualifiedName~RiderServiceReadsTests"`
Expected: build FAIL — `GetByIdAsync` takes no acting user; `GetTodayAsync` does not exist.

- [ ] **Step 3: Implement the scoped single read**

In `src/Modules/Riders/Application/RiderService.Reads.cs`, replace

```csharp
    public async Task<RiderResponse?> GetByIdAsync(Guid riderId, CancellationToken cancellationToken = default)
    {
        var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken);
        return rider is null ? null : ToResponse(rider);
    }
```

with

```csharp
    // F15 R3b-2 security fix: branch-scoped like ListAsync. A rider based outside the caller's
    // rider.read scope reads as not found (404), the same as an unknown id, so a pasted id from
    // another branch confirms nothing.
    public async Task<RiderResponse?> GetByIdAsync(Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var rider = await FindReadableRiderAsync(riderId, actingUser, cancellationToken);
        return rider is null ? null : ToResponse(rider);
    }

    private async Task<Rider?> FindReadableRiderAsync(Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken)
    {
        var rider = await _dbContext.Set<Rider>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken);
        if (rider is null)
        {
            return null;
        }
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "rider.read", cancellationToken);
        return accessibleBranchIds is null || accessibleBranchIds.Contains(rider.BaseBranchId) ? rider : null;
    }
```

- [ ] **Step 4: Implement the today read**

In `src/Modules/Riders/Application/RiderService.Board.cs`, add after `GetBoardAsync`:

```csharp
    // Rider Sheet: the rider's board row plus today's run tasks in sequence order, with AWBs from
    // one batched lookup. Unknown or out-of-scope riders read as not found (404), never 403.
    public async Task<RiderTodayResponse?> GetTodayAsync(Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var rider = await FindReadableRiderAsync(riderId, actingUser, cancellationToken);
        if (rider is null)
        {
            return null;
        }

        var row = (await BuildBoardRowsAsync(new[] { rider }, cancellationToken))[0];
        if (row.Run is null)
        {
            return new RiderTodayResponse(row, new List<RiderTodayTask>());
        }

        var tasks = await _dbContext.Set<DeliveryTask>().AsNoTracking()
            .Where(t => t.DeliveryRunId == row.Run.Id)
            .OrderBy(t => t.Sequence)
            .ToListAsync(cancellationToken);
        if (tasks.Count == 0)
        {
            return new RiderTodayResponse(row, new List<RiderTodayTask>());
        }

        var awbs = await _shipmentReportingLookupService.GetAwbsByShipmentIdsAsync(tasks.Select(t => t.ShipmentId).ToHashSet(), cancellationToken);
        return new RiderTodayResponse(row, tasks
            .Select(t => new RiderTodayTask(t.Id, t.ShipmentId, awbs.TryGetValue(t.ShipmentId, out var awb) ? awb : "", t.Type, t.Sequence, t.Status))
            .ToList());
    }
```

- [ ] **Step 5: Wire the endpoints**

In `src/Modules/Riders/Presentation/RidersEndpoints.cs`, replace

```csharp
        riders.MapGet("/{id:guid}", async (Guid id, RiderService service, HttpContext httpContext) =>
        {
            var result = await service.GetByIdAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("rider.read");
```

with

```csharp
        riders.MapGet("/{id:guid}", async (Guid id, RiderService service, HttpContext httpContext) =>
        {
            var result = await service.GetByIdAsync(id, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("rider.read");

        riders.MapGet("/{id:guid}/today", async (Guid id, RiderService service, HttpContext httpContext) =>
        {
            var result = await service.GetTodayAsync(id, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("rider.read");
```

Then check no other caller still uses the old signature: `grep -rn "GetByIdAsync(" src/Modules/Riders src/Api --include=*.cs` must show only the endpoint and `RiderService.Reads.cs`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj`
Expected: PASS (all Riders tests, including the 6 new ones). `dotnet build` → 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Riders tests/Riders.Tests
git commit -m "$(cat <<'EOF'
fix(riders): branch-scope the rider single read; add rider today read

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Batched branch lookups — custody, riders out, open NDRs

**Files:**
- Modify: `src/Modules/Shipments/Application/IShipmentReportingLookupService.cs`, `ShipmentReportingLookupService.cs`
- Modify: `src/Modules/Riders/Application/IRiderReportingLookupService.cs`, `RiderReportingLookupService.cs`
- Modify: `src/Modules/Ndr/Application/INdrReportingLookupService.cs`, `NdrReportingLookupService.cs`
- Modify (fakes): `tests/Reports.Tests/Fakes.cs`, `tests/Manifests.Tests/FakeShipmentReportingLookup.cs`, `tests/Riders.Tests/RiderReadTestSupport.cs`
- Test: `tests/Shipments.Tests/ShipmentReportingLookupServiceTests.cs`, `tests/Riders.Tests/RiderReportingLookupServiceTests.cs`, `tests/Ndr.Tests/NdrReportingLookupServiceTests.cs`

**Interfaces:**
- Produces (Task 5 consumes all three; every `branchIds` null = no filter; each is one grouped query per bucket, all in SQL):
  - `record BranchCustodyCounts(int OnHand, int OutForDelivery)` in `Courier.Shipments.Application`.
  - `IShipmentReportingLookupService.GetBranchCustodyCountsAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default) → Task<IReadOnlyDictionary<Guid, BranchCustodyCounts>>` — only branches with a non-zero bucket appear.
  - `IRiderReportingLookupService.GetRidersOutCountsAsync(IReadOnlySet<Guid>? branchIds, DateOnly today, CancellationToken cancellationToken = default) → Task<IReadOnlyDictionary<Guid, int>>` — distinct riders with an `Active` run dated `today`, keyed by `Rider.BaseBranchId`.
  - `INdrReportingLookupService.GetOpenNdrCountsByBranchAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default) → Task<IReadOnlyDictionary<Guid, int>>` — NDRs not `Resolved`, keyed by the shipment's `DestinationBranchId`.
  - Fakes (Reports.Tests): `FakeShipmentReportingLookupService.CustodyCounts` / `LastCustodyBranchIds`; `FakeRiderReportingLookupService.RidersOutCounts` / `LastRidersOutBranchIds` / `LastRidersOutToday`; `FakeNdrReportingLookupService.OpenNdrCountsByBranch` / `LastOpenNdrBranchIds`.

- [ ] **Step 1: Write the failing Shipments lookup tests**

Append inside `ShipmentReportingLookupServiceTests` (`tests/Shipments.Tests/ShipmentReportingLookupServiceTests.cs`), reusing its existing `SeedOrgAsync`/`BuildShipment` helpers:

```csharp
    [Fact]
    public async Task GetBranchCustodyCountsAsync_OnHandByCurrentBranch_OutForDeliveryByDestination()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        Shipment At(ShipmentStatus status, Guid? current) => BuildShipment(orgId, originBranchId, destBranchId, merchantId, status, current);
        dbContext.AddRange(
            At(ShipmentStatus.AtOriginBranch, originBranchId),
            At(ShipmentStatus.Sorted, originBranchId),
            At(ShipmentStatus.RtoAtOrigin, originBranchId),
            At(ShipmentStatus.AtDestinationBranch, destBranchId),
            At(ShipmentStatus.RtoInitiated, destBranchId),
            At(ShipmentStatus.OutForDelivery, null),
            At(ShipmentStatus.OutForDelivery, null),
            // Not physically at a branch, or not on hand: none of these may be counted.
            At(ShipmentStatus.Created, originBranchId),
            At(ShipmentStatus.PickedUp, originBranchId),
            At(ShipmentStatus.InTransit, null),
            At(ShipmentStatus.DeliveryFailed, null),
            At(ShipmentStatus.NdrCreated, null),
            At(ShipmentStatus.Delivered, null));
        await dbContext.SaveChangesAsync();
        var service = new ShipmentReportingLookupService(dbContext);

        var counts = await service.GetBranchCustodyCountsAsync(null);

        Assert.Equal(new BranchCustodyCounts(3, 0), counts[originBranchId]);
        Assert.Equal(new BranchCustodyCounts(2, 2), counts[destBranchId]);
        Assert.Equal(2, counts.Count);
    }

    [Fact]
    public async Task GetBranchCustodyCountsAsync_BranchFilter_KeepsOnlyThoseBranches()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;
        dbContext.AddRange(
            BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.AtOriginBranch, originBranchId),
            BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.OutForDelivery, null));
        await dbContext.SaveChangesAsync();
        var service = new ShipmentReportingLookupService(dbContext);

        var counts = await service.GetBranchCustodyCountsAsync(new HashSet<Guid> { destBranchId });

        Assert.Equal(new BranchCustodyCounts(0, 1), Assert.Single(counts).Value);
        Assert.False(counts.ContainsKey(originBranchId));
    }

    [Fact]
    public async Task GetBranchCustodyCountsAsync_IgnoresOtherTenants()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        var (otherOrgId, otherOriginBranchId, otherDestBranchId, otherMerchantId) = await SeedOrgAsync(dbContext);
        dbContext.AddRange(
            BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.AtOriginBranch, originBranchId),
            BuildShipment(otherOrgId, otherOriginBranchId, otherDestBranchId, otherMerchantId, ShipmentStatus.AtOriginBranch, otherOriginBranchId));
        await dbContext.SaveChangesAsync();
        tenantProvider.OrganizationId = orgId;
        var service = new ShipmentReportingLookupService(dbContext);

        var counts = await service.GetBranchCustodyCountsAsync(null);

        Assert.Equal(originBranchId, Assert.Single(counts).Key);
    }
```

- [ ] **Step 2: Write the failing Riders lookup test**

Append inside `RiderReportingLookupServiceTests` (`tests/Riders.Tests/RiderReportingLookupServiceTests.cs`):

```csharp
    [Fact]
    public async Task GetRidersOutCountsAsync_CountsRidersWithAnActiveRunToday_ByBaseBranch()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId) = await SeedOrgAsync(dbContext);
        var otherBranchId = Guid.NewGuid();
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = otherBranchId, OrganizationId = orgId, Code = "BR2", Name = "Branch Two",
            Type = Courier.Branches.Domain.BranchType.Branch, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        await dbContext.SaveChangesAsync();
        tenantProvider.OrganizationId = orgId;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        DeliveryRun Run(Rider rider, DateOnly date, DeliveryRunStatus status) =>
            new() { Id = Guid.NewGuid(), RiderId = rider.Id, BranchId = rider.BaseBranchId, Date = date, Status = status };

        var onRun = BuildRider(orgId, branchId);
        var alsoOnRun = BuildRider(orgId, branchId);
        var finished = BuildRider(orgId, branchId);
        var yesterdayOnly = BuildRider(orgId, branchId);
        var otherBranchRider = BuildRider(orgId, otherBranchId);
        dbContext.AddRange(onRun, alsoOnRun, finished, yesterdayOnly, otherBranchRider);
        dbContext.AddRange(
            Run(onRun, today, DeliveryRunStatus.Active),
            Run(alsoOnRun, today, DeliveryRunStatus.Active),
            Run(finished, today, DeliveryRunStatus.Completed),
            Run(yesterdayOnly, today.AddDays(-1), DeliveryRunStatus.Active),
            Run(otherBranchRider, today, DeliveryRunStatus.Active));
        await dbContext.SaveChangesAsync();
        var service = new RiderReportingLookupService(dbContext);

        var all = await service.GetRidersOutCountsAsync(null, today);
        var filtered = await service.GetRidersOutCountsAsync(new HashSet<Guid> { otherBranchId }, today);

        Assert.Equal(2, all[branchId]);
        Assert.Equal(1, all[otherBranchId]);
        Assert.Equal(otherBranchId, Assert.Single(filtered).Key);
    }
```

- [ ] **Step 3: Write the failing NDR lookup test**

Append inside `NdrReportingLookupServiceTests` (`tests/Ndr.Tests/NdrReportingLookupServiceTests.cs`):

```csharp
    [Fact]
    public async Task GetOpenNdrCountsByBranchAsync_CountsUnresolvedNdrsByDestinationBranch()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        var otherBranchId = Guid.NewGuid();
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = otherBranchId, OrganizationId = orgId, Code = "BR2", Name = "Branch Two",
            Type = Courier.Branches.Domain.BranchType.Branch, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        await dbContext.SaveChangesAsync();
        tenantProvider.OrganizationId = orgId;

        void AddNdr(Guid destinationBranchId, NdrStatus status)
        {
            var shipment = BuildShipment(orgId, destinationBranchId, merchantId);
            dbContext.Add(shipment);
            dbContext.Add(new NDR
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = shipment.Id, Reason = NdrReason.CustomerUnavailable, Status = status,
                Resolution = status == NdrStatus.Resolved ? NdrResolution.Reattempt : null,
                ResolvedAt = status == NdrStatus.Resolved ? DateTimeOffset.UtcNow : null
            });
        }
        AddNdr(branchId, NdrStatus.Open);
        AddNdr(branchId, NdrStatus.Contacting);
        AddNdr(branchId, NdrStatus.Resolved);
        AddNdr(otherBranchId, NdrStatus.RtoRequested);
        await dbContext.SaveChangesAsync();
        var service = new NdrReportingLookupService(dbContext);

        var all = await service.GetOpenNdrCountsByBranchAsync(null);
        var filtered = await service.GetOpenNdrCountsByBranchAsync(new HashSet<Guid> { otherBranchId });

        Assert.Equal(2, all[branchId]);
        Assert.Equal(1, all[otherBranchId]);
        Assert.Equal(otherBranchId, Assert.Single(filtered).Key);
    }
```

(`BuildShipment(orgId, branchId, merchantId)` in this file sets origin = destination = the given branch.)

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet build`
Expected: FAIL — the three lookup methods and `BranchCustodyCounts` do not exist.

- [ ] **Step 5: Add the Shipments lookup**

In `src/Modules/Shipments/Application/IShipmentReportingLookupService.cs`, after `public record BranchShipmentCounts(...)` add:

```csharp
public record BranchCustodyCounts(int OnHand, int OutForDelivery);
```

and add to the interface, after `GetCurrentStatusCountsAsync`:

```csharp
    // F15 R3b-2 branches page. OnHand groups by CurrentBranchId over the statuses where a parcel
    // physically sits at a branch; OutForDelivery groups by DestinationBranchId, because
    // CurrentBranchId is null while a rider holds the parcel. Only branches with a non-zero
    // bucket appear. branchIds null = no filter.
    Task<IReadOnlyDictionary<Guid, BranchCustodyCounts>> GetBranchCustodyCountsAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
```

In `src/Modules/Shipments/Application/ShipmentReportingLookupService.cs`, add before the closing brace of the class:

```csharp
    // RtoInitiated sets CurrentBranchId to the destination (the parcel is back at that branch);
    // DeliveryFailed/NdrCreated keep CurrentBranchId null (still with the rider), so they are
    // counted by the branch's open NDRs, not here.
    private static readonly ShipmentStatus[] OnHandStatuses =
    {
        ShipmentStatus.AtOriginBranch, ShipmentStatus.Sorted, ShipmentStatus.AtDestinationBranch,
        ShipmentStatus.RtoInitiated, ShipmentStatus.RtoAtOrigin
    };

    public async Task<IReadOnlyDictionary<Guid, BranchCustodyCounts>> GetBranchCustodyCountsAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default)
    {
        var onHandStatuses = OnHandStatuses;
        var onHandQuery = _dbContext.Set<Shipment>().AsNoTracking()
            .Where(s => s.CurrentBranchId != null && onHandStatuses.Contains(s.CurrentStatus));
        var outForDeliveryQuery = _dbContext.Set<Shipment>().AsNoTracking()
            .Where(s => s.CurrentStatus == ShipmentStatus.OutForDelivery);
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            onHandQuery = onHandQuery.Where(s => branchIdArray.Contains(s.CurrentBranchId!.Value));
            outForDeliveryQuery = outForDeliveryQuery.Where(s => branchIdArray.Contains(s.DestinationBranchId));
        }

        var onHand = await onHandQuery
            .GroupBy(s => s.CurrentBranchId!.Value)
            .Select(g => new { BranchId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var outForDelivery = await outForDeliveryQuery
            .GroupBy(s => s.DestinationBranchId)
            .Select(g => new { BranchId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var onHandByBranch = onHand.ToDictionary(x => x.BranchId, x => x.Count);
        var outForDeliveryByBranch = outForDelivery.ToDictionary(x => x.BranchId, x => x.Count);
        return onHandByBranch.Keys.Union(outForDeliveryByBranch.Keys).ToDictionary(
            branchId => branchId,
            branchId => new BranchCustodyCounts(onHandByBranch.GetValueOrDefault(branchId), outForDeliveryByBranch.GetValueOrDefault(branchId)));
    }
```

- [ ] **Step 6: Add the Riders lookup**

In `src/Modules/Riders/Application/IRiderReportingLookupService.cs`, add to the interface after `GetSummaryAsync`:

```csharp
    // F15 R3b-2 branches page: distinct riders with an Active run dated `today`, keyed by
    // Rider.BaseBranchId (the "riders at a branch" convention). Only branches with riders out appear.
    Task<IReadOnlyDictionary<Guid, int>> GetRidersOutCountsAsync(IReadOnlySet<Guid>? branchIds, DateOnly today, CancellationToken cancellationToken = default);
```

In `src/Modules/Riders/Application/RiderReportingLookupService.cs`, add before the closing brace of the class:

```csharp
    public async Task<IReadOnlyDictionary<Guid, int>> GetRidersOutCountsAsync(IReadOnlySet<Guid>? branchIds, DateOnly today, CancellationToken cancellationToken = default)
    {
        // DeliveryRun has no OrganizationId: the join to the tenant-filtered Rider set scopes it.
        var riders = _dbContext.Set<Rider>().AsNoTracking().AsQueryable();
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            riders = riders.Where(r => branchIdArray.Contains(r.BaseBranchId));
        }

        var ridersOut = await _dbContext.Set<DeliveryRun>().AsNoTracking()
            .Where(run => run.Date == today && run.Status == DeliveryRunStatus.Active)
            .Join(riders, run => run.RiderId, rider => rider.Id, (run, rider) => new { rider.BaseBranchId, RiderId = rider.Id })
            .Distinct()
            .ToListAsync(cancellationToken);

        return ridersOut.GroupBy(x => x.BaseBranchId).ToDictionary(g => g.Key, g => g.Count());
    }
```

- [ ] **Step 7: Add the NDR lookup**

In `src/Modules/Ndr/Application/INdrReportingLookupService.cs`, add to the interface after `GetStatusCountsAsync`:

```csharp
    // F15 R3b-2 branches page: unresolved NDRs keyed by the shipment's DestinationBranchId — the
    // same join and rule as GetOpenNdrCountForBranchAsync, so the number matches the NDR queue.
    Task<IReadOnlyDictionary<Guid, int>> GetOpenNdrCountsByBranchAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
```

In `src/Modules/Ndr/Application/NdrReportingLookupService.cs`, add before the closing brace of the class:

```csharp
    public async Task<IReadOnlyDictionary<Guid, int>> GetOpenNdrCountsByBranchAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default)
    {
        var destinations = _dbContext.Set<NDR>().AsNoTracking()
            .Where(n => n.Status != NdrStatus.Resolved)
            .Join(_dbContext.Set<Shipment>().AsNoTracking(), n => n.ShipmentId, s => s.Id, (n, s) => s.DestinationBranchId);
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            destinations = destinations.Where(b => branchIdArray.Contains(b));
        }

        var counts = await destinations
            .GroupBy(b => b)
            .Select(g => new { BranchId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(x => x.BranchId, x => x.Count);
    }
```

- [ ] **Step 8: Extend the fakes**

`tests/Reports.Tests/Fakes.cs` — inside `FakeShipmentReportingLookupService` add:

```csharp
    public IReadOnlyDictionary<Guid, BranchCustodyCounts> CustodyCounts { get; set; } = new Dictionary<Guid, BranchCustodyCounts>();
    public IReadOnlySet<Guid>? LastCustodyBranchIds { get; private set; }
    public Task<IReadOnlyDictionary<Guid, BranchCustodyCounts>> GetBranchCustodyCountsAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default)
    {
        LastCustodyBranchIds = branchIds;
        return Task.FromResult(CustodyCounts);
    }
```

inside `FakeRiderReportingLookupService` add:

```csharp
    public IReadOnlyDictionary<Guid, int> RidersOutCounts { get; set; } = new Dictionary<Guid, int>();
    public IReadOnlySet<Guid>? LastRidersOutBranchIds { get; private set; }
    public DateOnly? LastRidersOutToday { get; private set; }
    public Task<IReadOnlyDictionary<Guid, int>> GetRidersOutCountsAsync(IReadOnlySet<Guid>? branchIds, DateOnly today, CancellationToken cancellationToken = default)
    {
        LastRidersOutBranchIds = branchIds;
        LastRidersOutToday = today;
        return Task.FromResult(RidersOutCounts);
    }
```

inside `FakeNdrReportingLookupService` add:

```csharp
    public IReadOnlyDictionary<Guid, int> OpenNdrCountsByBranch { get; set; } = new Dictionary<Guid, int>();
    public IReadOnlySet<Guid>? LastOpenNdrBranchIds { get; private set; }
    public Task<IReadOnlyDictionary<Guid, int>> GetOpenNdrCountsByBranchAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default)
    {
        LastOpenNdrBranchIds = branchIds;
        return Task.FromResult(OpenNdrCountsByBranch);
    }
```

`tests/Manifests.Tests/FakeShipmentReportingLookup.cs` — add after the `GetCurrentStatusCountsAsync` line:

```csharp
    public Task<IReadOnlyDictionary<Guid, BranchCustodyCounts>> GetBranchCustodyCountsAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
```

`tests/Riders.Tests/RiderReadTestSupport.cs` — inside `StubShipmentReportingLookup`, add the same line after its `GetCurrentStatusCountsAsync` line.

If `dotnet build` reports any other implementer of the three interfaces (`grep -rn ": IShipmentReportingLookupService\|: IRiderReportingLookupService\|: INdrReportingLookupService" src tests --include=*.cs`), add a `throw new NotSupportedException()` member to it the same way.

- [ ] **Step 9: Run the tests to verify they pass**

Run:
```bash
dotnet build
dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter "FullyQualifiedName~ShipmentReportingLookupServiceTests"
dotnet test tests/Riders.Tests/Riders.Tests.csproj --filter "FullyQualifiedName~RiderReportingLookupServiceTests"
dotnet test tests/Ndr.Tests/Ndr.Tests.csproj --filter "FullyQualifiedName~NdrReportingLookupServiceTests"
dotnet test tests/Reports.Tests/Reports.Tests.csproj
dotnet test tests/Manifests.Tests/Manifests.Tests.csproj
```
Expected: 0 build errors; all PASS.

- [ ] **Step 10: Commit**

```bash
git add src/Modules/Shipments src/Modules/Riders src/Modules/Ndr tests/Shipments.Tests tests/Riders.Tests tests/Ndr.Tests tests/Reports.Tests tests/Manifests.Tests
git commit -m "$(cat <<'EOF'
feat(reports): batched per-branch custody, riders-out and open NDR lookups

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Reports — `GET /api/v1/branches/activity`

**Files:**
- Modify: `src/Modules/Reports/Application/ReportsDtos.cs`
- Create: `src/Modules/Reports/Application/BranchActivityService.cs`
- Modify: `src/Modules/Reports/Presentation/ReportsEndpoints.cs`
- Modify: `src/Api/Program.cs` (after `builder.Services.AddScoped<BranchDashboardService>();`)
- Test: `tests/Reports.Tests/BranchActivityServiceTests.cs`

**Interfaces:**
- Consumes: the three lookups and fakes from Task 4.
- Produces:
  - `record BranchActivityRow(Guid BranchId, int OnHand, int OutForDelivery, int RidersOut, int OpenExceptions)`, `record BranchActivityResponse(IReadOnlyList<BranchActivityRow> Items)` in `Courier.Reports.Application`.
  - `BranchActivityService.GetAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) → Task<BranchActivityResponse>`.
  - JSON: `GET /api/v1/branches/activity` (`branch.read`) → `{ "items": [{ "branchId", "onHand", "outForDelivery", "ridersOut", "openExceptions" }] }`, ordered by `branchId`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Reports.Tests/BranchActivityServiceTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Reports.Application;
using Courier.Shipments.Application;

namespace Courier.Reports.Tests;

public class BranchActivityServiceTests
{
    private static ClaimsPrincipal AnyUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    private readonly FakeShipmentReportingLookupService _shipments = new();
    private readonly FakeRiderReportingLookupService _riders = new();
    private readonly FakeNdrReportingLookupService _ndrs = new();
    private readonly FakeBranchAuthorizationChecker _checker = new();

    private BranchActivityService CreateService() => new(_shipments, _riders, _ndrs, _checker);

    [Fact]
    public async Task GetAsync_AllBranchesScope_MergesSparseRowsFromEveryLookup()
    {
        var busy = Guid.NewGuid();
        var ridersOnly = Guid.NewGuid();
        var ndrOnly = Guid.NewGuid();
        _shipments.CustodyCounts = new Dictionary<Guid, BranchCustodyCounts> { [busy] = new(12, 4) };
        _riders.RidersOutCounts = new Dictionary<Guid, int> { [busy] = 3, [ridersOnly] = 1 };
        _ndrs.OpenNdrCountsByBranch = new Dictionary<Guid, int> { [busy] = 2, [ndrOnly] = 5 };

        var result = await CreateService().GetAsync(AnyUser());

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(new BranchActivityRow(busy, 12, 4, 3, 2), result.Items.Single(r => r.BranchId == busy));
        Assert.Equal(new BranchActivityRow(ridersOnly, 0, 0, 1, 0), result.Items.Single(r => r.BranchId == ridersOnly));
        Assert.Equal(new BranchActivityRow(ndrOnly, 0, 0, 0, 5), result.Items.Single(r => r.BranchId == ndrOnly));
        Assert.Equal(result.Items.Select(r => r.BranchId).OrderBy(id => id), result.Items.Select(r => r.BranchId));
        Assert.Null(_shipments.LastCustodyBranchIds);
    }

    [Fact]
    public async Task GetAsync_ScopedUser_OneRowPerBranchInScopeIncludingZeros_AndScopeForwarded()
    {
        var quiet = Guid.NewGuid();
        var active = Guid.NewGuid();
        var scope = new HashSet<Guid> { quiet, active };
        _checker.AccessibleBranchIds = scope;
        _riders.RidersOutCounts = new Dictionary<Guid, int> { [active] = 2 };

        var result = await CreateService().GetAsync(AnyUser());

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(new BranchActivityRow(quiet, 0, 0, 0, 0), result.Items.Single(r => r.BranchId == quiet));
        Assert.Equal(2, result.Items.Single(r => r.BranchId == active).RidersOut);
        Assert.Same(scope, _shipments.LastCustodyBranchIds);
        Assert.Same(scope, _riders.LastRidersOutBranchIds);
        Assert.Same(scope, _ndrs.LastOpenNdrBranchIds);
    }

    [Fact]
    public async Task GetAsync_PassesTodaysUtcDateToRidersOut()
    {
        var before = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        await CreateService().GetAsync(AnyUser());

        var after = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        Assert.InRange(_riders.LastRidersOutToday!.Value, before, after);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Reports.Tests/Reports.Tests.csproj --filter "FullyQualifiedName~BranchActivityServiceTests"`
Expected: build FAIL — `BranchActivityService` does not exist.

- [ ] **Step 3: Implement the DTOs and service**

Append to `src/Modules/Reports/Application/ReportsDtos.cs`:

```csharp
// F15 R3b-2 branches page live counts. A scoped caller gets one row per branch in scope (zero
// rows included); an all-branches caller gets one row per branch with any activity, and a branch
// missing from Items has all zeros. OpenExceptions = unresolved NDRs by destination branch.
public record BranchActivityRow(Guid BranchId, int OnHand, int OutForDelivery, int RidersOut, int OpenExceptions);

public record BranchActivityResponse(IReadOnlyList<BranchActivityRow> Items);
```

Create `src/Modules/Reports/Application/BranchActivityService.cs`:

```csharp
using System.Security.Claims;
using Courier.Branches.Application;
using Courier.Ndr.Application;
using Courier.Riders.Application;
using Courier.SharedKernel;
using Courier.Shipments.Application;

namespace Courier.Reports.Application;

public class BranchActivityService
{
    private readonly IShipmentReportingLookupService _shipmentLookup;
    private readonly IRiderReportingLookupService _riderLookup;
    private readonly INdrReportingLookupService _ndrLookup;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public BranchActivityService(
        IShipmentReportingLookupService shipmentLookup,
        IRiderReportingLookupService riderLookup,
        INdrReportingLookupService ndrLookup,
        IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _shipmentLookup = shipmentLookup;
        _riderLookup = riderLookup;
        _ndrLookup = ndrLookup;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<BranchActivityResponse> GetAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var branchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "branch.read", cancellationToken);
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        // Sequential awaits: every lookup shares one scoped DbContext (see BranchDashboardService).
        var custody = await _shipmentLookup.GetBranchCustodyCountsAsync(branchIds, cancellationToken);
        var ridersOut = await _riderLookup.GetRidersOutCountsAsync(branchIds, today, cancellationToken);
        var openNdrs = await _ndrLookup.GetOpenNdrCountsByBranchAsync(branchIds, cancellationToken);

        // The lookups already narrow to branchIds, so a scoped caller's rows are exactly its scope.
        var rowBranchIds = branchIds is not null
            ? new HashSet<Guid>(branchIds)
            : new HashSet<Guid>(custody.Keys.Concat(ridersOut.Keys).Concat(openNdrs.Keys));

        var rows = rowBranchIds
            .OrderBy(id => id)
            .Select(id =>
            {
                custody.TryGetValue(id, out var counts);
                return new BranchActivityRow(
                    id, counts?.OnHand ?? 0, counts?.OutForDelivery ?? 0,
                    ridersOut.GetValueOrDefault(id), openNdrs.GetValueOrDefault(id));
            })
            .ToList();
        return new BranchActivityResponse(rows);
    }
}
```

(If the compiler reports an unused `using`, drop whichever of `Courier.Branches.Application` / `Courier.SharedKernel` does not hold `IBranchAuthorizationChecker`.)

- [ ] **Step 4: Map the endpoint and register the service**

In `src/Modules/Reports/Presentation/ReportsEndpoints.cs`, at the end of `MapReportsEndpoints` (after the `/export/cod-summary` mapping), add:

```csharp
        // F15 R3b-2: live per-branch counts for the branches page. Mapped in Reports (not Branches)
        // because Reports already composes the cross-module reporting lookups. The Branches group's
        // single read is "/{id:guid}", so this static segment never collides with it.
        app.MapGet("/api/v1/branches/activity", async (BranchActivityService service, HttpContext httpContext) =>
                Results.Ok(await service.GetAsync(httpContext.User, httpContext.RequestAborted)))
            .WithTags("Reports")
            .RequireAuthorization()
            .RequirePermission("branch.read");
```

In `src/Api/Program.cs`, directly after `builder.Services.AddScoped<BranchDashboardService>();` add:

```csharp
builder.Services.AddScoped<BranchActivityService>();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Reports.Tests/Reports.Tests.csproj` then `dotnet build`
Expected: PASS; 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Reports src/Api/Program.cs tests/Reports.Tests
git commit -m "$(cat <<'EOF'
feat(reports): branch activity read for the branches page

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: API tests — board, today, scoped rider read, branch activity

**Files:**
- Modify: `tests/Api.Tests/RidersEndpointsTests.cs`

**Interfaces:**
- Consumes: endpoints from Tasks 2, 3 and 5; existing helpers in this class: `RegisterOrgAsync`, `SeedBranchesAsync`, `CreateMerchantAsync`, `CreateRiderAsync` (rider full name "Test Rider"), `BookAndMoveToDestinationAsync`, `InviteAcceptAndLoginForBranchAsync`. Roles used: `BranchManager` (has `rider.read`, `branch.read`, branch-scoped to the invited branch), `SortingStaff` (has neither `rider.read` nor `branch.read`).

- [ ] **Step 1: Add the tests**

Add `using Courier.Reports.Application;` to the usings of `tests/Api.Tests/RidersEndpointsTests.cs`, then add these members to the class (before the private `InviteAcceptAndLoginForBranchAsync` helper). The branch-activity tests live here because they need this class's booking helpers.

```csharp
    // ---- F15 R3b-2 reads: rider board, rider today, scoped rider single read, branch activity ----

    private async Task<(HttpClient Owner, Guid Origin, Guid Destination, Guid PickupRider, Guid DeliveryRider, ShipmentResponse Shipment)> SeedOutForDeliveryAsync(string label, bool assignDelivery = true)
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, label);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndMoveToDestinationAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId);
        if (assignDelivery)
        {
            var assign = await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));
            assign.EnsureSuccessStatusCode();
        }
        return (client, originBranchId, destinationBranchId, pickupRiderId, deliveryRiderId, shipment);
    }

    private HttpClient ClientWithToken(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task RiderBoard_Owner_ReturnsTodaysRunCountsAndNames()
    {
        var (owner, origin, destination, pickupRiderId, deliveryRiderId, _) = await SeedOutForDeliveryAsync("board");

        var response = await owner.GetAsync($"/api/v1/riders/board?branchId={destination}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Assert.Single((await response.Content.ReadFromJsonAsync<RiderBoardResponse>())!.Items);
        Assert.Equal(deliveryRiderId, row.RiderId);
        Assert.Equal("Test Rider", row.FullName);
        Assert.Equal(destination, row.BaseBranchId);
        Assert.Equal(DeliveryRunStatus.Active, row.Run!.Status);
        Assert.Equal(1, row.TasksAssigned);
        Assert.Equal(0, row.TasksCompleted);
        Assert.Null(row.LastPingAt);

        var all = (await owner.GetFromJsonAsync<RiderBoardResponse>("/api/v1/riders/board"))!.Items;
        Assert.Equal(2, all.Count);
        var pickupRow = all.Single(r => r.RiderId == pickupRiderId);
        Assert.Equal(origin, pickupRow.BaseBranchId);
        Assert.Equal(1, pickupRow.TasksCompleted);
    }

    [Fact]
    public async Task RiderBoardAndToday_WithoutRiderRead_Return403_AndAnonymous401()
    {
        var (owner, origin, _, _, deliveryRiderId, _) = await SeedOutForDeliveryAsync("board-perm", assignDelivery: false);
        var sortingStaff = ClientWithToken(await InviteAcceptAndLoginForBranchAsync(owner, "SortingStaff", origin));

        Assert.Equal(HttpStatusCode.Forbidden, (await sortingStaff.GetAsync("/api/v1/riders/board")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sortingStaff.GetAsync($"/api/v1/riders/{deliveryRiderId}/today")).StatusCode);

        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/riders/board")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/riders/{deliveryRiderId}/today")).StatusCode);
    }

    [Fact]
    public async Task RiderToday_InScope_ReturnsTasksWithAwbs()
    {
        var (owner, _, _, _, deliveryRiderId, shipment) = await SeedOutForDeliveryAsync("today");

        var response = await owner.GetAsync($"/api/v1/riders/{deliveryRiderId}/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var today = await response.Content.ReadFromJsonAsync<RiderTodayResponse>();
        Assert.Equal(deliveryRiderId, today!.Rider.RiderId);
        var task = Assert.Single(today.Tasks);
        Assert.Equal(shipment.Awb, task.Awb);
        Assert.Equal(DeliveryTaskType.Delivery, task.Type);
        Assert.Equal(DeliveryTaskStatus.Pending, task.Status);
    }

    [Fact]
    public async Task BranchScopedUser_SeesOnlyOwnBranchRiders_AndOtherBranchRiderReadsAre404()
    {
        var (owner, origin, destination, pickupRiderId, deliveryRiderId, _) = await SeedOutForDeliveryAsync("board-scope");
        var manager = ClientWithToken(await InviteAcceptAndLoginForBranchAsync(owner, "BranchManager", origin));

        var board = (await manager.GetFromJsonAsync<RiderBoardResponse>("/api/v1/riders/board"))!.Items;
        Assert.Equal(pickupRiderId, Assert.Single(board).RiderId);
        Assert.Empty((await manager.GetFromJsonAsync<RiderBoardResponse>($"/api/v1/riders/board?branchId={destination}"))!.Items);

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/v1/riders/{pickupRiderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/v1/riders/{deliveryRiderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/v1/riders/{deliveryRiderId}/today")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/v1/riders/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/v1/riders/{Guid.NewGuid()}/today")).StatusCode);
    }

    [Fact]
    public async Task BranchActivity_Owner_CountsCustodyRidersOutAndExceptions()
    {
        var (owner, origin, destination, _, deliveryRiderId, shipment) = await SeedOutForDeliveryAsync("activity", assignDelivery: false);

        var before = (await owner.GetFromJsonAsync<BranchActivityResponse>("/api/v1/branches/activity"))!.Items;
        Assert.Equal(1, before.Single(r => r.BranchId == destination).OnHand);

        var assign = await owner.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));
        assign.EnsureSuccessStatusCode();

        var response = await owner.GetAsync("/api/v1/branches/activity");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = (await response.Content.ReadFromJsonAsync<BranchActivityResponse>())!.Items;
        var destinationRow = after.Single(r => r.BranchId == destination);
        Assert.Equal(0, destinationRow.OnHand);
        Assert.Equal(1, destinationRow.OutForDelivery);
        Assert.Equal(1, destinationRow.RidersOut);
        Assert.Equal(0, destinationRow.OpenExceptions);
        Assert.Equal(1, after.Single(r => r.BranchId == origin).RidersOut);
    }

    [Fact]
    public async Task BranchActivity_ScopedUserGetsOwnBranchRow_WithoutBranchRead403_Anonymous401()
    {
        var (owner, origin, _, _, _, _) = await SeedOutForDeliveryAsync("activity-scope");
        var manager = ClientWithToken(await InviteAcceptAndLoginForBranchAsync(owner, "BranchManager", origin));
        var sortingStaff = ClientWithToken(await InviteAcceptAndLoginForBranchAsync(owner, "SortingStaff", origin));

        var items = (await manager.GetFromJsonAsync<BranchActivityResponse>("/api/v1/branches/activity"))!.Items;
        Assert.Equal(origin, Assert.Single(items).BranchId);

        Assert.Equal(HttpStatusCode.Forbidden, (await sortingStaff.GetAsync("/api/v1/branches/activity")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/v1/branches/activity")).StatusCode);
    }

    [Fact]
    public async Task R3b2Reads_NeverExposeAnotherTenant()
    {
        var (_, originA, destinationA, pickupRiderA, deliveryRiderA, _) = await SeedOutForDeliveryAsync("tenant-a");
        var ownerB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(ownerB, "tenant-b");
        ownerB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var board = (await ownerB.GetFromJsonAsync<RiderBoardResponse>("/api/v1/riders/board"))!.Items;
        Assert.DoesNotContain(board, r => r.RiderId == pickupRiderA || r.RiderId == deliveryRiderA);
        Assert.Empty((await ownerB.GetFromJsonAsync<RiderBoardResponse>($"/api/v1/riders/board?branchId={originA}"))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync($"/api/v1/riders/{deliveryRiderA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.GetAsync($"/api/v1/riders/{deliveryRiderA}/today")).StatusCode);
        var activity = (await ownerB.GetFromJsonAsync<BranchActivityResponse>("/api/v1/branches/activity"))!.Items;
        Assert.DoesNotContain(activity, r => r.BranchId == originA || r.BranchId == destinationA);
    }
```

- [ ] **Step 2: Run the API tests**

Start the containers if needed: `docker start courier-postgres-1 courier-redis-1 courier-rabbitmq-1`.

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~RidersEndpointsTests|FullyQualifiedName~ReportsEndpointsTests|FullyQualifiedName~TenantIsolationTests"`
Expected: PASS (7 new tests and the existing ones). A failure here is a backend bug from Tasks 2–5: fix it in the owning file, not by loosening the assertion.

- [ ] **Step 3: Commit**

```bash
git add tests/Api.Tests/RidersEndpointsTests.cs
git commit -m "$(cat <<'EOF'
test(api): rider board, rider today, scoped rider read and branch activity

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Web data layer — types, proxies, hooks, models, panel store

All paths in Tasks 7–13 are relative to `src/Web/` unless they start with `docs/`. Run `npm`/`npx` commands from `src/Web`; run `git` commands from the repo root.

**Files:**
- Modify: `src/features/riders/types.ts`, `src/features/branches/types.ts`
- Create: `src/app/api/riders/board/route.ts` (+ `route.test.ts`), `src/app/api/riders/[id]/route.ts` (+ test), `src/app/api/riders/[id]/today/route.ts` (+ test), `src/app/api/branches/activity/route.ts` (+ test)
- Create: `src/features/riders/hooks/useRiderBoard.ts`, `useRider.ts`, `useRiderToday.ts` (+ `riderReadHooks.test.tsx`)
- Create: `src/features/rider-tracking/hooks/useRunBreadcrumbs.ts` (+ test)
- Create: `src/features/branches/hooks/useBranchActivity.ts` (+ test)
- Create: `src/features/geography/hooks/useDistrictNames.ts` (+ test)
- Create: `src/features/riders/lib/riderModel.ts` (+ test), `src/features/branches/lib/branchModel.ts` (+ test)
- Modify: `src/features/riders/store/riderPanelStore.ts` (+ test)

**Interfaces:**
- Consumes: backend JSON from Tasks 2, 3, 5; existing `RiderLocation` (`features/rider-tracking/hooks/useRiderLocations`), `RiderCashHolding` (`features/cod/types`), `District` (`features/geography/types`), `Branch`/`BranchType` (`features/branches/types`), `clientFetch`, `apiFetch`.
- Produces (every later web task uses these exact names):
  - Types: `DeliveryRunStatus`, `DeliveryTaskStatus`, `RiderBoardRun`, `RiderBoardRow`, `RiderBoard`, `RiderTodayTask`, `RiderToday` (riders); `BranchActivityRow`, `BranchActivity` (branches).
  - Hooks: `useRiderBoard(branchId?: string, options?: { enabled?: boolean })` key `["riders","board",branchId ?? null]`; `useRider(id: string)` key `["riders", id]`; `useRiderToday(riderId: string, enabled: boolean)` key `["riders", riderId, "today"]`; `useRunBreadcrumbs(runId: string | null, enabled: boolean)` key `["riders","runs",runId,"breadcrumbs"]` → `RiderLocation[]`; `useBranchActivity(options?: { enabled?: boolean })` key `["branches","activity"]`; `useDistrictNames(provinceIds: string[]) → Map<string, string>` (district id → name).
  - `riderModel.ts`: `RiderStage`, `RIDER_STAGES`, `STALE_LOCATION_MINUTES`, `riderDayStage`, `riderStageLabel`, `riderStageToken`, `minutesSince`, `isLocationStale`, `formatMinutesAgo`, `lastSeenText`, `riderStageCounts`, `staleOnRunCount`, `RidersTab`, `parseRidersTab`, `parseRiderStage`, `RiderRoleFilter`, `parseRiderRole`, `roleLabel`, `filterBoardRows`, `progressShare`, `riderInitials`, `RiderCashTotal`, `cashByRider`, `FleetFilter`, `parseFleetFilter`, `fleetVehicles`.
  - `branchModel.ts`: `BRANCH_TYPES`, `parseBranchType`, `branchTypeLabel`, `matchesBranchQuery`, `filterBranches`, `ON_HAND_STATUSES`, `onHandShipmentsHref`, `outForDeliveryHref`, `ridersOutHref`, `zeroActivity`.
  - `useRiderPanelStore`: new panel `{ mode: "detail"; riderId: string }` and `openDetail(riderId: string)`.

- [ ] **Step 1: Types**

Append to `src/features/riders/types.ts`:

```ts
// F15 R3b-2 rider board and rider Sheet (GET /api/v1/riders/board, /riders/{id}/today).
export type DeliveryRunStatus = "Active" | "Completed";
export type DeliveryTaskStatus = "Pending" | "Completed" | "Failed";

export interface RiderBoardRun {
  id: string;
  status: DeliveryRunStatus;
}

/** Counts are today's run only (UTC date). `lastPingAt` is null when the rider has not pinged today. */
export interface RiderBoardRow {
  riderId: string;
  userId: string;
  fullName: string;
  baseBranchId: string;
  roleType: RiderRoleType;
  isActive: boolean;
  vehiclePlate: string | null;
  run: RiderBoardRun | null;
  tasksAssigned: number;
  tasksCompleted: number;
  tasksFailed: number;
  lastPingAt: string | null;
}

export interface RiderBoard {
  items: RiderBoardRow[];
}

export interface RiderTodayTask {
  taskId: string;
  shipmentId: string;
  /** "" when the AWB lookup missed. */
  awb: string;
  type: DeliveryTaskType;
  sequence: number;
  status: DeliveryTaskStatus;
}

export interface RiderToday {
  rider: RiderBoardRow;
  tasks: RiderTodayTask[];
}
```

Append to `src/features/branches/types.ts`:

```ts
/** GET /api/v1/branches/activity row. A branch missing from a loaded response has all zeros. */
export interface BranchActivityRow {
  branchId: string;
  onHand: number;
  outForDelivery: number;
  ridersOut: number;
  openExceptions: number;
}

export interface BranchActivity {
  items: BranchActivityRow[];
}
```

- [ ] **Step 2: Write the failing proxy tests**

Create `src/app/api/riders/board/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/riders/board", () => {
  it("forwards the query string to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => ({ items: [] }) } as Response);
    const response = await GET(new NextRequest("http://localhost/api/riders/board?branchId=b1"));
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/board?branchId=b1");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new NextRequest("http://localhost/api/riders/board"));
    expect(response.status).toBe(401);
  });
});
```

Create `src/app/api/riders/[id]/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/riders/[id]", () => {
  it("proxies the rider single read and keeps a 404", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: false, status: 404, json: async () => null } as Response);
    const response = await GET(new Request("http://localhost/api/riders/r1"), { params: { id: "r1" } });
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/r1");
    expect(response.status).toBe(404);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new Request("http://localhost/api/riders/r1"), { params: { id: "r1" } });
    expect(response.status).toBe(401);
  });
});
```

Create `src/app/api/riders/[id]/today/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/riders/[id]/today", () => {
  it("proxies the rider today read", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => ({ rider: {}, tasks: [] }) } as Response);
    const response = await GET(new Request("http://localhost/api/riders/r1/today"), { params: { id: "r1" } });
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/r1/today");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new Request("http://localhost/api/riders/r1/today"), { params: { id: "r1" } });
    expect(response.status).toBe(401);
  });
});
```

Create `src/app/api/branches/activity/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/branches/activity", () => {
  it("proxies the branch activity read", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => ({ items: [] }) } as Response);
    const response = await GET();
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/branches/activity");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET();
    expect(response.status).toBe(401);
  });
});
```

- [ ] **Step 3: Write the proxies**

`src/app/api/riders/board/route.ts`:

```ts
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/riders/board${query}`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/app/api/riders/[id]/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/riders/${params.id}`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/app/api/riders/[id]/today/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/riders/${params.id}/today`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/app/api/branches/activity/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/branches/activity");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

(Next resolves the static `board`/`activity` segments before the dynamic `[id]` siblings.)

- [ ] **Step 4: Write the failing hook tests**

Create `src/features/riders/hooks/riderReadHooks.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useRiderBoard } from "./useRiderBoard";
import { useRider } from "./useRider";
import { useRiderToday } from "./useRiderToday";

let queryClient: QueryClient;
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

beforeEach(() => {
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ items: [] }) });
});

describe("rider read hooks", () => {
  it("useRiderBoard keys by branch and sends branchId only when set", async () => {
    const { result } = renderHook(() => useRiderBoard("b 1"), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/riders/board?branchId=b%201", undefined);
    expect(queryClient.getQueryCache().find({ queryKey: ["riders", "board", "b 1"] })).toBeDefined();

    const all = renderHook(() => useRiderBoard(), { wrapper });
    await waitFor(() => expect(all.result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/riders/board", undefined);
    expect(queryClient.getQueryCache().find({ queryKey: ["riders", "board", null] })).toBeDefined();
  });

  it("useRider and useRiderToday stay idle without an id and key by rider", async () => {
    renderHook(() => useRider(""), { wrapper });
    renderHook(() => useRiderToday("r1", false), { wrapper });
    expect(global.fetch).not.toHaveBeenCalled();

    const rider = renderHook(() => useRider("r1"), { wrapper });
    const today = renderHook(() => useRiderToday("r1", true), { wrapper });
    await waitFor(() => expect(rider.result.current.isSuccess && today.result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/riders/r1", undefined);
    expect(global.fetch).toHaveBeenCalledWith("/api/riders/r1/today", undefined);
    expect(queryClient.getQueryCache().find({ queryKey: ["riders", "r1", "today"] })).toBeDefined();
  });
});
```

Create `src/features/rider-tracking/hooks/useRunBreadcrumbs.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useRunBreadcrumbs } from "./useRunBreadcrumbs";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useRunBreadcrumbs", () => {
  it("fetches only with a run id and when enabled", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] });
    renderHook(() => useRunBreadcrumbs(null, true), { wrapper });
    renderHook(() => useRunBreadcrumbs("run1", false), { wrapper });
    expect(global.fetch).not.toHaveBeenCalled();

    const { result } = renderHook(() => useRunBreadcrumbs("run1", true), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/riders/runs/run1/breadcrumbs", undefined);
  });
});
```

Create `src/features/branches/hooks/useBranchActivity.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useBranchActivity } from "./useBranchActivity";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useBranchActivity", () => {
  it("fetches the activity proxy, and not when disabled", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ items: [] }) });
    renderHook(() => useBranchActivity({ enabled: false }), { wrapper });
    expect(global.fetch).not.toHaveBeenCalled();

    const { result } = renderHook(() => useBranchActivity(), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/branches/activity", undefined);
  });
});
```

Create `src/features/geography/hooks/useDistrictNames.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useDistrictNames } from "./useDistrictNames";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useDistrictNames", () => {
  it("reads each distinct province once and maps district ids to names", async () => {
    global.fetch = vi.fn().mockImplementation(async (url: string) => ({
      ok: true,
      status: 200,
      json: async () =>
        url.endsWith("p1")
          ? [{ id: "d1", provinceId: "p1", name: "Kathmandu" }]
          : [{ id: "d2", provinceId: "p2", name: "Kaski" }],
    }));

    const { result } = renderHook(() => useDistrictNames(["p1", "p2", "p1"]), { wrapper });

    await waitFor(() => expect(result.current.get("d2")).toBe("Kaski"));
    expect(result.current.get("d1")).toBe("Kathmandu");
    expect(global.fetch).toHaveBeenCalledTimes(2);
  });
});
```

Run: `npx vitest run src/app/api/riders/board src/app/api/riders/[id] src/app/api/branches/activity src/features/riders/hooks/riderReadHooks.test.tsx src/features/rider-tracking/hooks/useRunBreadcrumbs.test.tsx src/features/branches/hooks/useBranchActivity.test.tsx src/features/geography/hooks/useDistrictNames.test.tsx`
Expected: proxy tests PASS (written in Step 3); hook tests FAIL — modules not found.

- [ ] **Step 5: Write the hooks**

`src/features/riders/hooks/useRiderBoard.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { RiderBoard } from "@/features/riders/types";

export function useRiderBoard(branchId?: string, options: { enabled?: boolean } = {}) {
  const search = branchId ? `?branchId=${encodeURIComponent(branchId)}` : "";
  return useQuery({
    queryKey: ["riders", "board", branchId ?? null],
    queryFn: () => clientFetch<RiderBoard>(`/api/riders/board${search}`),
    enabled: options.enabled ?? true,
  });
}
```

`src/features/riders/hooks/useRider.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Rider } from "@/features/riders/types";

export function useRider(id: string) {
  return useQuery({
    queryKey: ["riders", id],
    queryFn: () => clientFetch<Rider>(`/api/riders/${id}`),
    enabled: id !== "",
  });
}
```

`src/features/riders/hooks/useRiderToday.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { RiderToday } from "@/features/riders/types";

export function useRiderToday(riderId: string, enabled: boolean) {
  return useQuery({
    queryKey: ["riders", riderId, "today"],
    queryFn: () => clientFetch<RiderToday>(`/api/riders/${riderId}/today`),
    enabled: enabled && riderId !== "",
  });
}
```

`src/features/rider-tracking/hooks/useRunBreadcrumbs.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { RiderLocation } from "@/features/rider-tracking/hooks/useRiderLocations";

/** Today's pings on one delivery run, oldest first (GET /riders/runs/{runId}/breadcrumbs). */
export function useRunBreadcrumbs(runId: string | null, enabled: boolean) {
  return useQuery({
    queryKey: ["riders", "runs", runId, "breadcrumbs"],
    queryFn: () => clientFetch<RiderLocation[]>(`/api/riders/runs/${runId}/breadcrumbs`),
    enabled: enabled && runId !== null,
  });
}
```

`src/features/branches/hooks/useBranchActivity.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { BranchActivity } from "@/features/branches/types";

export function useBranchActivity(options: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: ["branches", "activity"],
    queryFn: () => clientFetch<BranchActivity>("/api/branches/activity"),
    enabled: options.enabled ?? true,
  });
}
```

`src/features/geography/hooks/useDistrictNames.ts`:

```ts
"use client";

import { useQueries } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { District } from "@/features/geography/types";

/**
 * District id → name for every distinct province given. Shares `useDistricts`' query key, so a
 * province already read by a picker is not fetched again. Unknown ids are simply absent.
 */
export function useDistrictNames(provinceIds: string[]): Map<string, string> {
  const unique = Array.from(new Set(provinceIds)).sort();
  return useQueries({
    queries: unique.map((provinceId) => ({
      queryKey: ["geography", "districts", provinceId],
      queryFn: () => clientFetch<District[]>(`/api/geography/districts?provinceId=${provinceId}`),
      staleTime: Infinity,
    })),
    combine: (results) => {
      const names = new Map<string, string>();
      for (const result of results) {
        for (const district of result.data ?? []) names.set(district.id, district.name);
      }
      return names;
    },
  });
}
```

Run the Step 4 command again. Expected: PASS.

- [ ] **Step 6: Write the failing model tests**

Create `src/features/riders/lib/riderModel.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import type { RiderBoardRow, Vehicle } from "@/features/riders/types";
import {
  RIDER_STAGES, cashByRider, filterBoardRows, fleetVehicles, formatMinutesAgo, isLocationStale, lastSeenText,
  parseFleetFilter, parseRiderRole, parseRiderStage, parseRidersTab, progressShare, riderDayStage, riderInitials,
  riderStageCounts, roleLabel, staleOnRunCount,
} from "./riderModel";

const NOW = new Date("2026-10-16T08:00:00Z");
const minutesAgo = (m: number) => new Date(NOW.getTime() - m * 60_000).toISOString();

function row(overrides: Partial<RiderBoardRow>): RiderBoardRow {
  return {
    riderId: "r1", userId: "u1", fullName: "Asha Gurung", baseBranchId: "b1", roleType: "DeliveryRider", isActive: true,
    vehiclePlate: null, run: null, tasksAssigned: 0, tasksCompleted: 0, tasksFailed: 0, lastPingAt: null, ...overrides,
  };
}

describe("riderModel", () => {
  it("maps runs to the three day stages, in strip order", () => {
    expect(RIDER_STAGES.map((s) => s.label)).toEqual(["Off duty", "On run", "Run done"]);
    expect(riderDayStage(row({}))).toBe("off");
    expect(riderDayStage(row({ run: { id: "x", status: "Active" } }))).toBe("on");
    expect(riderDayStage(row({ run: { id: "x", status: "Completed" } }))).toBe("done");
  });

  it("treats no ping today and pings older than 15 minutes as stale", () => {
    expect(isLocationStale(null, NOW)).toBe(true);
    expect(isLocationStale(minutesAgo(15), NOW)).toBe(false);
    expect(isLocationStale(minutesAgo(16), NOW)).toBe(true);
  });

  it("words last-seen times and marks stale ones with text", () => {
    expect(formatMinutesAgo(minutesAgo(0), NOW)).toBe("just now");
    expect(formatMinutesAgo(minutesAgo(4), NOW)).toBe("4 min ago");
    expect(formatMinutesAgo(minutesAgo(125), NOW)).toBe("2 h 5 min ago");
    expect(formatMinutesAgo(minutesAgo(120), NOW)).toBe("2 h ago");
    expect(lastSeenText(null, NOW)).toEqual({ text: "No ping today", stale: true });
    expect(lastSeenText(minutesAgo(4), NOW)).toEqual({ text: "4 min ago", stale: false });
    expect(lastSeenText(minutesAgo(18), NOW)).toEqual({ text: "18 min ago · stale", stale: true });
  });

  it("counts stages over active riders only, and stale on-run riders", () => {
    const rows = [
      row({ riderId: "a" }),
      row({ riderId: "b", run: { id: "1", status: "Active" }, lastPingAt: minutesAgo(2) }),
      row({ riderId: "c", run: { id: "2", status: "Active" }, lastPingAt: null }),
      row({ riderId: "d", run: { id: "3", status: "Active" }, lastPingAt: minutesAgo(40) }),
      row({ riderId: "e", run: { id: "4", status: "Completed" } }),
      row({ riderId: "f", isActive: false }),
    ];
    expect(riderStageCounts(rows)).toEqual({ off: 1, on: 3, done: 1 });
    expect(staleOnRunCount(rows, NOW)).toBe(2);
  });

  it("parses URL values with safe defaults", () => {
    expect(parseRidersTab(null)).toBe("riders");
    expect(parseRidersTab("map")).toBe("map");
    expect(parseRidersTab("bogus")).toBe("riders");
    expect(parseRiderStage("on")).toBe("on");
    expect(parseRiderStage("planned")).toBeUndefined();
    expect(parseRiderRole("Pickup")).toBe("Pickup");
    expect(parseRiderRole("PickupRider")).toBeUndefined();
    expect(parseFleetFilter("unassigned")).toBe("unassigned");
    expect(parseFleetFilter(null)).toBe("all");
  });

  it("filters rows by stage, role and the inactive toggle", () => {
    const rows = [
      row({ riderId: "a", roleType: "PickupRider" }),
      row({ riderId: "b", run: { id: "1", status: "Active" } }),
      row({ riderId: "c", isActive: false }),
    ];
    expect(filterBoardRows(rows, { showInactive: false }).map((r) => r.riderId)).toEqual(["a", "b"]);
    expect(filterBoardRows(rows, { showInactive: true }).map((r) => r.riderId)).toEqual(["a", "b", "c"]);
    expect(filterBoardRows(rows, { stage: "on", showInactive: false }).map((r) => r.riderId)).toEqual(["b"]);
    expect(filterBoardRows(rows, { role: "Pickup", showInactive: false }).map((r) => r.riderId)).toEqual(["a"]);
    expect(roleLabel("PickupRider")).toBe("Pickup rider");
  });

  it("never produces NaN progress and builds initials", () => {
    expect(progressShare(0, 0)).toBe(0);
    expect(progressShare(3, 4)).toBe(0.75);
    expect(progressShare(5, 4)).toBe(1);
    expect(riderInitials("Asha Kumari Gurung")).toBe("AG");
    expect(riderInitials("bikash")).toBe("B");
    expect(riderInitials("  ")).toBe("?");
  });

  it("sums cash per rider across branches and keeps the oldest collection", () => {
    const totals = cashByRider([
      { riderId: "r1", branchId: "b1", parcelCount: 2, amount: 1500, oldestCollectedAt: "2026-10-15T10:00:00Z", latestCollectedAt: "2026-10-15T12:00:00Z" },
      { riderId: "r1", branchId: "b2", parcelCount: 1, amount: 500, oldestCollectedAt: "2026-10-14T09:00:00Z", latestCollectedAt: "2026-10-14T09:00:00Z" },
      { riderId: "r2", branchId: "b1", parcelCount: 1, amount: 250, oldestCollectedAt: "2026-10-16T07:00:00Z", latestCollectedAt: "2026-10-16T07:00:00Z" },
    ]);
    expect(totals.get("r1")).toEqual({ amount: 2000, parcelCount: 3, oldestCollectedAt: "2026-10-14T09:00:00Z" });
    expect(totals.get("r2")?.amount).toBe(250);
  });

  it("shows unassigned vehicles always and assigned ones only for board riders when a branch is picked", () => {
    const vehicles: Vehicle[] = [
      { id: "v1", riderId: null, type: "Bike", plateNumber: "BA 1", capacity: null },
      { id: "v2", riderId: "r1", type: "Bike", plateNumber: "BA 2", capacity: 20 },
      { id: "v3", riderId: "elsewhere", type: "Van", plateNumber: "BA 3", capacity: 500 },
    ];
    const ids = new Set(["r1"]);
    expect(fleetVehicles(vehicles, ids, true).map((v) => v.id)).toEqual(["v1", "v2"]);
    expect(fleetVehicles(vehicles, ids, false).map((v) => v.id)).toEqual(["v1", "v2", "v3"]);
  });
});
```

Create `src/features/branches/lib/branchModel.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import type { Branch } from "@/features/branches/types";
import {
  BRANCH_TYPES, branchTypeLabel, filterBranches, matchesBranchQuery, onHandShipmentsHref, outForDeliveryHref,
  parseBranchType, ridersOutHref, zeroActivity,
} from "./branchModel";

function branch(overrides: Partial<Branch>): Branch {
  return {
    id: "b1", code: "KTM-01", name: "Kathmandu Hub", type: "Hub", provinceId: "p1", districtId: "d1", localLevelId: "l1",
    wardNumber: 1, addressLine: "New Road", phone: null, operatingHours: null, latitude: null, longitude: null,
    parentBranchId: null, deliveryZoneId: null, isActive: true, ...overrides,
  };
}

describe("branchModel", () => {
  it("lists the five branch types in strip order with labels", () => {
    expect(BRANCH_TYPES.map((t) => t.type)).toEqual(["HeadOffice", "Hub", "Branch", "PickupPoint", "Warehouse"]);
    expect(branchTypeLabel("PickupPoint")).toBe("Pickup point");
    expect(parseBranchType("Warehouse")).toBe("Warehouse");
    expect(parseBranchType("warehouse")).toBeUndefined();
  });

  it("matches code or name case-insensitively and ignores blank queries", () => {
    const b = branch({});
    expect(matchesBranchQuery(b, "ktm")).toBe(true);
    expect(matchesBranchQuery(b, " HUB ")).toBe(true);
    expect(matchesBranchQuery(b, "pokhara")).toBe(false);
    expect(matchesBranchQuery(b, "   ")).toBe(true);
  });

  it("filters by type, query and the active-only default", () => {
    const branches = [
      branch({ id: "a" }),
      branch({ id: "b", type: "Branch", code: "PKR", name: "Pokhara" }),
      branch({ id: "c", isActive: false }),
    ];
    expect(filterBranches(branches, { q: "", showInactive: false }).map((b) => b.id)).toEqual(["a", "b"]);
    expect(filterBranches(branches, { q: "", showInactive: true }).map((b) => b.id)).toEqual(["a", "b", "c"]);
    expect(filterBranches(branches, { type: "Branch", q: "", showInactive: false }).map((b) => b.id)).toEqual(["b"]);
    expect(filterBranches(branches, { q: "pkr", showInactive: false }).map((b) => b.id)).toEqual(["b"]);
  });

  it("links the right-now cards to filtered lists", () => {
    expect(onHandShipmentsHref("b1")).toBe(
      "/shipments?branchId=b1&status=AtOriginBranch&status=Sorted&status=AtDestinationBranch&status=RtoInitiated&status=RtoAtOrigin",
    );
    expect(outForDeliveryHref("b1")).toBe("/shipments?branchId=b1&status=OutForDelivery");
    expect(ridersOutHref("b1")).toBe("/riders?branch=b1&stage=on");
    expect(zeroActivity("b1")).toEqual({ branchId: "b1", onHand: 0, outForDelivery: 0, ridersOut: 0, openExceptions: 0 });
  });
});
```

Run: `npx vitest run src/features/riders/lib src/features/branches/lib/branchModel.test.ts`
Expected: FAIL — modules not found.

- [ ] **Step 7: Write the models**

Create `src/features/riders/lib/riderModel.ts`:

```ts
import type { RiderCashHolding } from "@/features/cod/types";
import type { RiderBoardRow, RiderRoleType, Vehicle } from "@/features/riders/types";

export type RiderStage = "off" | "on" | "done";

/** The backend has two run states (Active, Completed); no run today is "off". */
export const RIDER_STAGES: { key: RiderStage; label: string; token: string }[] = [
  { key: "off", label: "Off duty", token: "status-created" },
  { key: "on", label: "On run", token: "status-out-for-delivery" },
  { key: "done", label: "Run done", token: "status-delivered" },
];

export const STALE_LOCATION_MINUTES = 15;

export function riderDayStage(row: Pick<RiderBoardRow, "run">): RiderStage {
  if (!row.run) return "off";
  return row.run.status === "Active" ? "on" : "done";
}

export function riderStageLabel(stage: RiderStage): string {
  return RIDER_STAGES.find((s) => s.key === stage)?.label ?? stage;
}

export function riderStageToken(stage: RiderStage): string {
  return RIDER_STAGES.find((s) => s.key === stage)?.token ?? "status-created";
}

export function minutesSince(iso: string, now: Date = new Date()): number {
  return Math.max(0, Math.floor((now.getTime() - Date.parse(iso)) / 60_000));
}

/** No ping today counts as stale: the rider cannot be placed. */
export function isLocationStale(lastPingAt: string | null, now: Date = new Date()): boolean {
  return lastPingAt === null || minutesSince(lastPingAt, now) > STALE_LOCATION_MINUTES;
}

/** "just now", "4 min ago", "2 h 5 min ago". */
export function formatMinutesAgo(iso: string, now: Date = new Date()): string {
  const minutes = minutesSince(iso, now);
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest === 0 ? `${hours} h ago` : `${hours} h ${rest} min ago`;
}

/** Last-seen column text; a stale ping says so in words, not only by colour. */
export function lastSeenText(lastPingAt: string | null, now: Date = new Date()): { text: string; stale: boolean } {
  if (lastPingAt === null) return { text: "No ping today", stale: true };
  const stale = isLocationStale(lastPingAt, now);
  const ago = formatMinutesAgo(lastPingAt, now);
  return { text: stale ? `${ago} · stale` : ago, stale };
}

/** Strip counts: inactive riders are not part of today's operation. */
export function riderStageCounts(rows: RiderBoardRow[]): Record<RiderStage, number> {
  const counts: Record<RiderStage, number> = { off: 0, on: 0, done: 0 };
  for (const row of rows) {
    if (row.isActive) counts[riderDayStage(row)] += 1;
  }
  return counts;
}

export function staleOnRunCount(rows: RiderBoardRow[], now: Date = new Date()): number {
  return rows.filter((r) => r.isActive && riderDayStage(r) === "on" && isLocationStale(r.lastPingAt, now)).length;
}

export type RidersTab = "riders" | "map" | "fleet";

export function parseRidersTab(value: string | null): RidersTab {
  return value === "map" || value === "fleet" ? value : "riders";
}

export function parseRiderStage(value: string | null): RiderStage | undefined {
  return RIDER_STAGES.find((s) => s.key === value)?.key;
}

export type RiderRoleFilter = "Pickup" | "Delivery";

export function parseRiderRole(value: string | null): RiderRoleFilter | undefined {
  return value === "Pickup" || value === "Delivery" ? value : undefined;
}

export function roleLabel(roleType: RiderRoleType): string {
  return roleType === "PickupRider" ? "Pickup rider" : "Delivery rider";
}

export function filterBoardRows(
  rows: RiderBoardRow[],
  filters: { stage?: RiderStage; role?: RiderRoleFilter; showInactive: boolean },
): RiderBoardRow[] {
  return rows.filter((row) => {
    if (!filters.showInactive && !row.isActive) return false;
    if (filters.stage && riderDayStage(row) !== filters.stage) return false;
    if (filters.role && row.roleType !== `${filters.role}Rider`) return false;
    return true;
  });
}

/** Share of a run done, 0–1. A run with no tasks is 0, never NaN. */
export function progressShare(done: number, total: number): number {
  return total > 0 ? Math.min(1, done / total) : 0;
}

export function riderInitials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "?";
  const first = parts[0][0];
  const last = parts.length > 1 ? parts[parts.length - 1][0] : "";
  return `${first}${last}`.toUpperCase();
}

export interface RiderCashTotal {
  amount: number;
  parcelCount: number;
  oldestCollectedAt: string;
}

/** One total per rider: a rider can hold cash collected for more than one branch. */
export function cashByRider(items: RiderCashHolding[]): Map<string, RiderCashTotal> {
  const totals = new Map<string, RiderCashTotal>();
  for (const item of items) {
    const current = totals.get(item.riderId);
    if (!current) {
      totals.set(item.riderId, { amount: item.amount, parcelCount: item.parcelCount, oldestCollectedAt: item.oldestCollectedAt });
      continue;
    }
    totals.set(item.riderId, {
      amount: current.amount + item.amount,
      parcelCount: current.parcelCount + item.parcelCount,
      oldestCollectedAt:
        Date.parse(item.oldestCollectedAt) < Date.parse(current.oldestCollectedAt) ? item.oldestCollectedAt : current.oldestCollectedAt,
    });
  }
  return totals;
}

export type FleetFilter = "all" | "unassigned";

export function parseFleetFilter(value: string | null): FleetFilter {
  return value === "unassigned" ? "unassigned" : "all";
}

/**
 * GET /vehicles is not branch-scoped (backend follow-up). With a branch picked, keep unassigned
 * vehicles and those assigned to riders on the (branch-filtered) board.
 */
export function fleetVehicles(vehicles: Vehicle[], boardRiderIds: Set<string>, branchPicked: boolean): Vehicle[] {
  if (!branchPicked) return vehicles;
  return vehicles.filter((v) => v.riderId === null || boardRiderIds.has(v.riderId));
}
```

Create `src/features/branches/lib/branchModel.ts`:

```ts
import type { Branch, BranchActivityRow, BranchType } from "@/features/branches/types";

export const BRANCH_TYPES: { type: BranchType; label: string }[] = [
  { type: "HeadOffice", label: "Head office" },
  { type: "Hub", label: "Hub" },
  { type: "Branch", label: "Branch" },
  { type: "PickupPoint", label: "Pickup point" },
  { type: "Warehouse", label: "Warehouse" },
];

export function parseBranchType(value: string | null): BranchType | undefined {
  return BRANCH_TYPES.find((t) => t.type === value)?.type;
}

export function branchTypeLabel(type: BranchType): string {
  return BRANCH_TYPES.find((t) => t.type === type)?.label ?? type;
}

export function matchesBranchQuery(branch: Pick<Branch, "code" | "name">, q: string): boolean {
  const needle = q.trim().toLowerCase();
  if (!needle) return true;
  return branch.code.toLowerCase().includes(needle) || branch.name.toLowerCase().includes(needle);
}

export function filterBranches(branches: Branch[], filters: { type?: BranchType; q: string; showInactive: boolean }): Branch[] {
  return branches.filter(
    (b) =>
      (filters.showInactive || b.isActive) &&
      (!filters.type || b.type === filters.type) &&
      matchesBranchQuery(b, filters.q),
  );
}

/** The statuses GET /branches/activity counts as "On hand" — kept in step with the backend. */
export const ON_HAND_STATUSES = ["AtOriginBranch", "Sorted", "AtDestinationBranch", "RtoInitiated", "RtoAtOrigin"] as const;

// The shipments list's branchId filter matches origin, destination or current branch, so these
// lists can be longer than the custody count — link text says "View shipments", never a number.
export function onHandShipmentsHref(branchId: string): string {
  const params = new URLSearchParams({ branchId });
  ON_HAND_STATUSES.forEach((s) => params.append("status", s));
  return `/shipments?${params.toString()}`;
}

export function outForDeliveryHref(branchId: string): string {
  return `/shipments?${new URLSearchParams({ branchId, status: "OutForDelivery" }).toString()}`;
}

export function ridersOutHref(branchId: string): string {
  return `/riders?${new URLSearchParams({ branch: branchId, stage: "on" }).toString()}`;
}

/** A branch missing from a loaded activity response has no activity. */
export function zeroActivity(branchId: string): BranchActivityRow {
  return { branchId, onHand: 0, outForDelivery: 0, ridersOut: 0, openExceptions: 0 };
}
```

Run: `npx vitest run src/features/riders/lib src/features/branches/lib/branchModel.test.ts`
Expected: PASS.

- [ ] **Step 8: Panel store**

Replace `src/features/riders/store/riderPanelStore.ts` with:

```ts
import { create } from "zustand";

type RiderPanelState =
  | { mode: "closed" }
  | { mode: "create-rider" }
  | { mode: "create-vehicle" }
  | { mode: "assign-task" }
  | { mode: "detail"; riderId: string };

interface RiderPanelStore {
  panel: RiderPanelState;
  openCreateRider: () => void;
  openCreateVehicle: () => void;
  openAssignTask: () => void;
  openDetail: (riderId: string) => void;
  close: () => void;
}

export const useRiderPanelStore = create<RiderPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreateRider: () => set({ panel: { mode: "create-rider" } }),
  openCreateVehicle: () => set({ panel: { mode: "create-vehicle" } }),
  openAssignTask: () => set({ panel: { mode: "assign-task" } }),
  openDetail: (riderId) => set({ panel: { mode: "detail", riderId } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

Add to `src/features/riders/store/riderPanelStore.test.ts` inside the `describe`:

```ts
  it("openDetail switches to the rider detail panel", () => {
    useRiderPanelStore.getState().openDetail("r1");
    expect(useRiderPanelStore.getState().panel).toEqual({ mode: "detail", riderId: "r1" });
  });
```

- [ ] **Step 9: Run everything for this task**

Run: `npx vitest run src/app/api/riders src/app/api/branches src/features/riders src/features/rider-tracking src/features/branches src/features/geography` then `npx tsc --noEmit`
Expected: PASS; 0 type errors.

- [ ] **Step 10: Commit**

```bash
git add src/Web/src
git commit -m "$(cat <<'EOF'
feat(web): rider board, rider today and branch activity data layer and models

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Map tab — restyled Leaflet map, pins, "not on the map" list

**Files:**
- Create: `src/features/rider-tracking/lib/mapPins.ts` (+ `mapPins.test.ts`)
- Modify: `src/features/rider-tracking/components/LiveMap.tsx` (rewrite)
- Create: `src/features/riders/components/RidersMapTab.tsx` (+ `RidersMapTab.test.tsx`)

**Interfaces:**
- Consumes: `useRiderLocations(branchId?: string)` (existing; polls every 20 s, idle without a branch), `riderModel` (Task 7), `formatRs` (`features/cod/lib/codFormat`).
- Produces:
  - `mapPins.ts`: `interface MapRiderPin { riderId; latitude; longitude; initials; label; stale; minutesAgo; popup: ReactNode }`, `interface MapBranchPin { latitude; longitude; label }`, `riderPinHtml(pin)`, `branchPinHtml(label)`, `escapeHtml(text)`.
  - `LiveMap({ pins: MapRiderPin[]; branch?: MapBranchPin; className?: string })` (named export, Leaflet; always loaded through `next/dynamic` with `ssr: false`).
  - `RidersMapTab({ branchId?: string; branch?: Branch; rows?: RiderBoardRow[]; cash?: Map<string, RiderCashTotal>; showCash: boolean; onOpenRider: (riderId: string) => void; now: Date })`.

- [ ] **Step 1: Write the failing pin tests**

Create `src/features/rider-tracking/lib/mapPins.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { branchPinHtml, escapeHtml, riderPinHtml } from "./mapPins";

const base = { riderId: "r1", latitude: 27.7, longitude: 85.3, initials: "AG", label: "Asha Gurung", minutesAgo: 3, popup: null };

describe("map pins", () => {
  it("draws a fresh ping as a solid disc without an age label", () => {
    const html = riderPinHtml({ ...base, stale: false });
    expect(html).toContain("bg-primary");
    expect(html).toContain(">AG<");
    expect(html).not.toContain("min<");
  });

  it("draws a stale ping dashed with its age in words", () => {
    const html = riderPinHtml({ ...base, stale: true, minutesAgo: 42 });
    expect(html).toContain("border-dashed");
    expect(html).toContain(">42 min<");
  });

  it("escapes text placed into marker HTML", () => {
    expect(escapeHtml(`<b>"x" & 'y'</b>`)).toBe("&lt;b&gt;&quot;x&quot; &amp; &#39;y&#39;&lt;/b&gt;");
    expect(branchPinHtml("KTM<1>")).toContain("KTM&lt;1&gt;");
  });
});
```

Run: `npx vitest run src/features/rider-tracking/lib/mapPins.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 2: Write the pin builders and the map**

Create `src/features/rider-tracking/lib/mapPins.ts`:

```ts
import type { ReactNode } from "react";

export interface MapRiderPin {
  riderId: string;
  latitude: number;
  longitude: number;
  initials: string;
  /** Full name: the marker's hover title. */
  label: string;
  /** Older than STALE_LOCATION_MINUTES: dashed outline plus an "N min" label. */
  stale: boolean;
  minutesAgo: number;
  popup: ReactNode;
}

export interface MapBranchPin {
  latitude: number;
  longitude: number;
  label: string;
}

const ENTITIES: Record<string, string> = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };

export function escapeHtml(text: string): string {
  return text.replace(/[&<>"']/g, (c) => ENTITIES[c]);
}

/** Leaflet divIcon HTML (Leaflet renders this string, not React). Tailwind sees these classes here. */
export function riderPinHtml(pin: Pick<MapRiderPin, "initials" | "stale" | "minutesAgo">): string {
  const disc = pin.stale
    ? "border-2 border-dashed border-muted-foreground bg-background text-foreground"
    : "bg-primary text-primary-foreground";
  const age = pin.stale
    ? `<span class="mt-0.5 rounded bg-background px-1 text-2xs text-muted-foreground shadow-sm">${pin.minutesAgo} min</span>`
    : "";
  return `<span class="flex flex-col items-center"><span class="flex h-8 w-8 items-center justify-center rounded-full text-2xs font-semibold ring-2 ring-background ${disc}">${escapeHtml(pin.initials)}</span>${age}</span>`;
}

export function branchPinHtml(label: string): string {
  return `<span class="flex h-6 items-center justify-center rounded-md border border-border bg-card px-1.5 font-mono text-2xs font-semibold text-foreground shadow-sm">${escapeHtml(label)}</span>`;
}
```

Replace `src/features/rider-tracking/components/LiveMap.tsx` with:

```tsx
"use client";

import { useEffect } from "react";
import { MapContainer, Marker, Popup, TileLayer } from "react-leaflet";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import { cn } from "@/lib/utils";
import { branchPinHtml, riderPinHtml, type MapBranchPin, type MapRiderPin } from "@/features/rider-tracking/lib/mapPins";

const DEFAULT_CENTER: [number, number] = [27.7172, 85.324]; // Kathmandu — only when nothing has a position.

export function LiveMap({ pins, branch, className }: { pins: MapRiderPin[]; branch?: MapBranchPin; className?: string }) {
  useEffect(() => {
    // Leaflet's default marker icon references image paths relative to the installed package,
    // which bundlers rewrite incorrectly — fixed once here with CDN URLs, the standard workaround.
    L.Icon.Default.mergeOptions({
      iconRetinaUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png",
      iconUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png",
      shadowUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png",
    });
  }, []);

  const center: [number, number] = branch
    ? [branch.latitude, branch.longitude]
    : pins.length > 0
      ? [pins[0].latitude, pins[0].longitude]
      : DEFAULT_CENTER;

  return (
    <MapContainer center={center} zoom={13} className={cn("h-[480px] w-full rounded-lg", className)}>
      <TileLayer
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
      />
      {branch && (
        <Marker
          position={[branch.latitude, branch.longitude]}
          title={branch.label}
          icon={L.divIcon({ className: "", iconSize: [64, 24], iconAnchor: [32, 12], html: branchPinHtml(branch.label) })}
        />
      )}
      {pins.map((pin) => (
        <Marker
          key={pin.riderId}
          position={[pin.latitude, pin.longitude]}
          title={pin.label}
          icon={L.divIcon({ className: "", iconSize: [40, 52], iconAnchor: [20, 16], html: riderPinHtml(pin) })}
        >
          <Popup>{pin.popup}</Popup>
        </Marker>
      ))}
    </MapContainer>
  );
}
```

Run: `npx vitest run src/features/rider-tracking/lib/mapPins.test.ts`
Expected: PASS.

- [ ] **Step 3: Write the failing Map tab tests**

Create `src/features/riders/components/RidersMapTab.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { MapBranchPin, MapRiderPin } from "@/features/rider-tracking/lib/mapPins";
import type { RiderBoardRow } from "@/features/riders/types";
import type { Branch } from "@/features/branches/types";

vi.mock("next/dynamic", () => ({
  default: () =>
    function MapStub({ pins, branch }: { pins: MapRiderPin[]; branch?: MapBranchPin }) {
      return (
        <div data-testid="map">
          {branch && <span>{`branch pin ${branch.label}`}</span>}
          {pins.map((p) => (
            <div key={p.riderId} data-testid={`pin-${p.riderId}`} data-stale={String(p.stale)}>
              {p.popup}
            </div>
          ))}
        </div>
      );
    },
}));

const useRiderLocations = vi.fn();
vi.mock("@/features/rider-tracking/hooks/useRiderLocations", () => ({
  useRiderLocations: (...args: unknown[]) => useRiderLocations(...args),
}));

import { RidersMapTab } from "./RidersMapTab";

const NOW = new Date("2026-10-16T08:00:00Z");
const minutesAgo = (m: number) => new Date(NOW.getTime() - m * 60_000).toISOString();

function row(overrides: Partial<RiderBoardRow>): RiderBoardRow {
  return {
    riderId: "r1", userId: "u1", fullName: "Asha Gurung", baseBranchId: "b1", roleType: "DeliveryRider", isActive: true,
    vehiclePlate: "BA 2 PA 1111", run: { id: "run1", status: "Active" }, tasksAssigned: 5, tasksCompleted: 2, tasksFailed: 0,
    lastPingAt: minutesAgo(2), ...overrides,
  };
}

const branch = { id: "b1", code: "KTM", name: "Kathmandu Hub", latitude: 27.7, longitude: 85.32 } as Branch;
const onOpenRider = vi.fn();

beforeEach(() => {
  onOpenRider.mockReset();
  useRiderLocations.mockReset();
  useRiderLocations.mockReturnValue({
    data: [
      { riderId: "r1", latitude: 27.71, longitude: 85.31, accuracyMeters: 8.4, recordedAt: minutesAgo(2), receivedAt: minutesAgo(2) },
      { riderId: "r2", latitude: 27.72, longitude: 85.33, accuracyMeters: null, recordedAt: minutesAgo(40), receivedAt: minutesAgo(40) },
    ],
    dataUpdatedAt: NOW.getTime(),
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  });
});

describe("RidersMapTab", () => {
  it("asks for a branch and fetches nothing without one", () => {
    useRiderLocations.mockReturnValue({ data: undefined, isLoading: false, isError: false, refetch: vi.fn() });
    render(<RidersMapTab showCash={false} onOpenRider={onOpenRider} now={NOW} />);
    expect(screen.getByText("Pick a branch to see its riders on the map")).toBeInTheDocument();
    expect(useRiderLocations).toHaveBeenCalledWith(undefined);
  });

  it("shows a skeleton while loading and an error with retry", () => {
    useRiderLocations.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn() });
    const { unmount } = render(<RidersMapTab branchId="b1" showCash={false} onOpenRider={onOpenRider} now={NOW} />);
    expect(screen.getByRole("status", { name: "Loading rider map" })).toBeInTheDocument();
    unmount();

    useRiderLocations.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    render(<RidersMapTab branchId="b1" showCash={false} onOpenRider={onOpenRider} now={NOW} />);
    expect(screen.getByText("Couldn't load rider locations")).toBeInTheDocument();
  });

  it("pins riders with board names, marks stale pings and shows the branch pin", () => {
    render(
      <RidersMapTab
        branchId="b1" branch={branch} rows={[row({}), row({ riderId: "r2", fullName: "Bikash Rai", run: null })]}
        showCash={false} onOpenRider={onOpenRider} now={NOW}
      />,
    );
    expect(screen.getByText("branch pin KTM")).toBeInTheDocument();
    expect(screen.getByTestId("pin-r1")).toHaveAttribute("data-stale", "false");
    expect(screen.getByTestId("pin-r2")).toHaveAttribute("data-stale", "true");
    const asha = within(screen.getByTestId("pin-r1"));
    expect(asha.getByText("Asha Gurung")).toBeInTheDocument();
    expect(asha.getByText("Delivery rider · BA 2 PA 1111")).toBeInTheDocument();
    expect(asha.getByText("2 of 5 done")).toBeInTheDocument();
    expect(asha.getByText("Last ping 2 min ago · ±8 m")).toBeInTheDocument();
    expect(asha.queryByText(/Cash held/)).toBeNull();
    expect(within(screen.getByTestId("pin-r2")).getByText("No run today")).toBeInTheDocument();
    fireEvent.click(asha.getByRole("button", { name: "Open rider" }));
    expect(onOpenRider).toHaveBeenCalledWith("r1");
    expect(screen.getByText(/refreshes every 20 s/)).toBeInTheDocument();
  });

  it("shows cash in the popup only with cash access", () => {
    render(
      <RidersMapTab
        branchId="b1" rows={[row({})]} cash={new Map([["r1", { amount: 2350, parcelCount: 2, oldestCollectedAt: minutesAgo(90) }]])}
        showCash onOpenRider={onOpenRider} now={NOW}
      />,
    );
    expect(within(screen.getByTestId("pin-r1")).getByText("Cash held Rs 2,350")).toBeInTheDocument();
  });

  it("lists on-run riders at this branch that have no position, never on the map", () => {
    render(
      <RidersMapTab
        branchId="b1"
        rows={[
          row({}),
          row({ riderId: "r3", fullName: "Chandra Thapa" }),
          row({ riderId: "r4", fullName: "Off Duty Rider", run: null }),
          row({ riderId: "r5", fullName: "Other Branch", baseBranchId: "b9" }),
        ]}
        showCash={false} onOpenRider={onOpenRider} now={NOW}
      />,
    );
    const list = within(screen.getByRole("region", { name: "On run, not on the map" }));
    expect(list.getByText("Chandra Thapa")).toBeInTheDocument();
    expect(list.queryByText("Off Duty Rider")).toBeNull();
    expect(list.queryByText("Other Branch")).toBeNull();
    expect(screen.queryByTestId("pin-r3")).toBeNull();
    fireEvent.click(list.getByRole("button", { name: "Open Chandra Thapa" }));
    expect(onOpenRider).toHaveBeenCalledWith("r3");
  });
});
```

Run: `npx vitest run src/features/riders/components/RidersMapTab.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 4: Write the Map tab**

Create `src/features/riders/components/RidersMapTab.tsx`:

```tsx
"use client";

import dynamic from "next/dynamic";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/states/EmptyState";
import { ErrorState } from "@/components/states/ErrorState";
import { formatRs } from "@/features/cod/lib/codFormat";
import { useRiderLocations, type RiderLocation } from "@/features/rider-tracking/hooks/useRiderLocations";
import type { MapBranchPin, MapRiderPin } from "@/features/rider-tracking/lib/mapPins";
import type { Branch } from "@/features/branches/types";
import type { RiderBoardRow } from "@/features/riders/types";
import {
  STALE_LOCATION_MINUTES, formatMinutesAgo, isLocationStale, minutesSince, riderDayStage, riderInitials, roleLabel,
  type RiderCashTotal,
} from "@/features/riders/lib/riderModel";

const LiveMap = dynamic(() => import("@/features/rider-tracking/components/LiveMap").then((mod) => mod.LiveMap), {
  ssr: false,
  loading: () => <Skeleton className="h-[480px] w-full rounded-lg" />,
});

const updatedFormat = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit" });

function PinPopup({
  location, row, cash, showCash, onOpenRider, now,
}: {
  location: RiderLocation;
  row?: RiderBoardRow;
  cash?: RiderCashTotal;
  showCash: boolean;
  onOpenRider: (riderId: string) => void;
  now: Date;
}) {
  const accuracy = location.accuracyMeters === null ? "" : ` · ±${Math.round(location.accuracyMeters)} m`;
  return (
    <div className="min-w-44 space-y-1">
      <p className="text-sm font-medium text-foreground">{row?.fullName ?? "Rider"}</p>
      {row && <p className="text-xs text-muted-foreground">{`${roleLabel(row.roleType)} · ${row.vehiclePlate ?? "No vehicle"}`}</p>}
      <p className="text-xs text-foreground">{row?.run ? `${row.tasksCompleted} of ${row.tasksAssigned} done` : "No run today"}</p>
      <p className="text-xs text-muted-foreground">{`Last ping ${formatMinutesAgo(location.receivedAt, now)}${accuracy}`}</p>
      {showCash && <p className="text-xs text-foreground">{`Cash held ${cash ? formatRs(cash.amount) : "—"}`}</p>}
      <Button type="button" size="sm" variant="outline" className="mt-1 h-7" onClick={() => onOpenRider(location.riderId)}>
        Open rider
      </Button>
    </div>
  );
}

export function RidersMapTab({
  branchId, branch, rows, cash, showCash, onOpenRider, now,
}: {
  branchId?: string;
  branch?: Branch;
  rows?: RiderBoardRow[];
  cash?: Map<string, RiderCashTotal>;
  showCash: boolean;
  onOpenRider: (riderId: string) => void;
  now: Date;
}) {
  // Mounted only while the Map tab is open, so the 20 s polling stops on the other tabs.
  const locations = useRiderLocations(branchId);

  if (!branchId) {
    return <EmptyState title="Pick a branch to see its riders on the map" description="The map shows one branch at a time. Choose a branch above." />;
  }
  if (locations.isError) {
    return <ErrorState title="Couldn't load rider locations" description="Something went wrong fetching live positions." onRetry={() => locations.refetch()} />;
  }
  if (locations.isLoading || !locations.data) {
    return <Skeleton role="status" aria-label="Loading rider map" className="h-[480px] w-full rounded-lg" />;
  }

  const rowsById = new Map((rows ?? []).map((r) => [r.riderId, r]));
  const located = new Set(locations.data.map((l) => l.riderId));
  const pins: MapRiderPin[] = locations.data.map((location) => {
    const row = rowsById.get(location.riderId);
    const name = row?.fullName ?? "Rider";
    return {
      riderId: location.riderId,
      latitude: location.latitude,
      longitude: location.longitude,
      initials: riderInitials(name),
      label: name,
      stale: isLocationStale(location.receivedAt, now),
      minutesAgo: minutesSince(location.receivedAt, now),
      popup: <PinPopup location={location} row={row} cash={cash?.get(location.riderId)} showCash={showCash} onOpenRider={onOpenRider} now={now} />,
    };
  });
  const branchPin: MapBranchPin | undefined =
    branch && branch.latitude !== null && branch.longitude !== null
      ? { latitude: branch.latitude, longitude: branch.longitude, label: branch.code?.trim() ? branch.code : branch.name }
      : undefined;
  const notOnMap = (rows ?? []).filter(
    (r) => r.isActive && r.baseBranchId === branchId && riderDayStage(r) === "on" && !located.has(r.riderId),
  );

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-4 text-xs text-muted-foreground">
        <span className="flex items-center gap-1.5">
          <span className="h-3 w-3 rounded-full bg-primary" aria-hidden="true" />
          Fresh ping
        </span>
        <span className="flex items-center gap-1.5">
          <span className="h-3 w-3 rounded-full border-2 border-dashed border-muted-foreground" aria-hidden="true" />
          {`No ping for over ${STALE_LOCATION_MINUTES} min`}
        </span>
        <span className="ml-auto">{`Updated ${updatedFormat.format(new Date(locations.dataUpdatedAt))} · refreshes every 20 s`}</span>
      </div>
      {pins.length === 0 && <p className="text-sm text-muted-foreground">No rider at this branch has sent a position yet.</p>}
      <LiveMap pins={pins} branch={branchPin} />
      {notOnMap.length > 0 && (
        <section aria-label="On run, not on the map" className="rounded-lg border border-border bg-card p-4">
          <h3 className="text-sm font-semibold text-foreground">On run, not on the map</h3>
          <p className="mt-0.5 text-xs text-muted-foreground">These riders have not sent a position from the rider app.</p>
          <ul className="mt-3 space-y-1.5">
            {notOnMap.map((r) => (
              <li key={r.riderId} className="flex items-center justify-between gap-3 text-sm">
                <span>{r.fullName}</span>
                <Button type="button" variant="link" size="sm" className="h-auto p-0" aria-label={`Open ${r.fullName}`} onClick={() => onOpenRider(r.riderId)}>
                  Open rider
                </Button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
```

- [ ] **Step 5: Run the tests**

Run: `npx vitest run src/features/riders/components/RidersMapTab.test.tsx src/features/rider-tracking` then `npx tsc --noEmit`
Expected: PASS; 0 type errors. (The old `app/(staff)/riders/live-map/page.tsx` still compiles against the old `LiveMap` props until Task 11 replaces it — if `tsc` flags it here, replace that file now with the Task 11 Step 5 redirect page and its test.)

- [ ] **Step 6: Commit**

```bash
git add src/Web/src
git commit -m "$(cat <<'EOF'
feat(web): riders map tab with initials pins, stale pings and not-on-map list

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: Fleet tab and the rider stage badge

**Files:**
- Create: `src/features/riders/components/RiderStageBadge.tsx`
- Create: `src/features/riders/components/RidersFleetTab.tsx` (+ `RidersFleetTab.test.tsx`)

**Interfaces:**
- Consumes: `riderModel` (`riderDayStage`, `riderStageLabel`, `riderStageToken`, `FleetFilter`), `toClientPage`, `useFilterPage`, `DataTable`.
- Produces:
  - `RiderStageBadge({ stage: RiderStage })`, `RiderInactiveBadge()`.
  - `RidersFleetTab({ vehicles?: Vehicle[]; isLoading: boolean; isError: boolean; onRetry: () => void; rows?: RiderBoardRow[]; filter: FleetFilter; onFilterChange: (f: FleetFilter) => void })` — `vehicles` is already narrowed with `fleetVehicles` by the page.

- [ ] **Step 1: Write the failing test**

Create `src/features/riders/components/RidersFleetTab.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import type { RiderBoardRow, Vehicle } from "@/features/riders/types";
import { RidersFleetTab } from "./RidersFleetTab";

const vehicles: Vehicle[] = [
  { id: "v1", riderId: null, type: "Bike", plateNumber: "BA 1 PA 0001", capacity: null },
  { id: "v2", riderId: "r1", type: "Van", plateNumber: "BA 2 KHA 1234", capacity: 500 },
  { id: "v3", riderId: "gone", type: "Bike", plateNumber: "BA 3 PA 0003", capacity: 20 },
];
const rows: RiderBoardRow[] = [
  {
    riderId: "r1", userId: "u1", fullName: "Asha Gurung", baseBranchId: "b1", roleType: "DeliveryRider", isActive: true,
    vehiclePlate: "BA 2 KHA 1234", run: { id: "run1", status: "Active" }, tasksAssigned: 6, tasksCompleted: 4, tasksFailed: 1, lastPingAt: null,
  },
];

describe("RidersFleetTab", () => {
  it("lists vehicles with capacity, rider and the rider's day", () => {
    render(<RidersFleetTab vehicles={vehicles} isLoading={false} isError={false} onRetry={vi.fn()} rows={rows} filter="all" onFilterChange={vi.fn()} />);
    expect(screen.getByText("BA 2 KHA 1234")).toBeInTheDocument();
    expect(screen.getByText("500 kg")).toBeInTheDocument();
    expect(screen.getByText("Asha Gurung")).toBeInTheDocument();
    expect(screen.getByText("On run")).toBeInTheDocument();
    expect(screen.getByText("4 of 6")).toBeInTheDocument();
    expect(screen.getByText("Unassigned")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "All 3" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByText("Reassigning a vehicle or marking it off the road needs backend support.")).toBeInTheDocument();
  });

  it("filters to unassigned vehicles and reports chip clicks", () => {
    const onFilterChange = vi.fn();
    render(<RidersFleetTab vehicles={vehicles} isLoading={false} isError={false} onRetry={vi.fn()} rows={rows} filter="unassigned" onFilterChange={onFilterChange} />);
    expect(screen.getByText("BA 1 PA 0001")).toBeInTheDocument();
    expect(screen.queryByText("BA 2 KHA 1234")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "All 3" }));
    expect(onFilterChange).toHaveBeenCalledWith("all");
    expect(screen.getByRole("button", { name: "Unassigned 1" })).toHaveAttribute("aria-pressed", "true");
  });

  it("shows an error with retry", () => {
    const onRetry = vi.fn();
    render(<RidersFleetTab vehicles={undefined} isLoading={false} isError onRetry={onRetry} rows={rows} filter="all" onFilterChange={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /retry/i }));
    expect(onRetry).toHaveBeenCalled();
  });
});
```

Run: `npx vitest run src/features/riders/components/RidersFleetTab.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 2: Write the badge and the tab**

Create `src/features/riders/components/RiderStageBadge.tsx`:

```tsx
import { Badge } from "@/components/ui/badge";
import { riderStageLabel, riderStageToken, type RiderStage } from "@/features/riders/lib/riderModel";

/** Label + dot: the stage is never conveyed by colour alone. */
export function RiderStageBadge({ stage }: { stage: RiderStage }) {
  return (
    <Badge variant="outline" className="gap-1.5 whitespace-nowrap font-normal">
      <span className="h-1.5 w-1.5 shrink-0 rounded-full" style={{ backgroundColor: `hsl(var(--${riderStageToken(stage)}))` }} aria-hidden="true" />
      {riderStageLabel(stage)}
    </Badge>
  );
}

export function RiderInactiveBadge() {
  return (
    <Badge variant="secondary" className="font-normal">
      Inactive
    </Badge>
  );
}
```

Create `src/features/riders/components/RidersFleetTab.tsx`:

```tsx
"use client";

import { Button } from "@/components/ui/button";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { ErrorState } from "@/components/states/ErrorState";
import { RiderStageBadge } from "@/features/riders/components/RiderStageBadge";
import { riderDayStage, type FleetFilter } from "@/features/riders/lib/riderModel";
import type { RiderBoardRow, Vehicle } from "@/features/riders/types";
import { useFilterPage } from "@/lib/hooks/useFilterPage";
import { toClientPage } from "@/lib/paging/clientPage";

const PAGE_SIZE = 20;

export function RidersFleetTab({
  vehicles, isLoading, isError, onRetry, rows, filter, onFilterChange,
}: {
  vehicles?: Vehicle[];
  isLoading: boolean;
  isError: boolean;
  onRetry: () => void;
  rows?: RiderBoardRow[];
  filter: FleetFilter;
  onFilterChange: (filter: FleetFilter) => void;
}) {
  const [page, setPage] = useFilterPage(filter);
  const rowsById = new Map((rows ?? []).map((r) => [r.riderId, r]));
  const unassigned = (vehicles ?? []).filter((v) => v.riderId === null);
  const shown = filter === "unassigned" ? unassigned : vehicles ?? [];

  const columns: ColumnDef<Vehicle>[] = [
    { key: "plate", header: "Plate", render: (v) => <span className="font-mono text-sm">{v.plateNumber}</span> },
    { key: "type", header: "Type", render: (v) => <span className="text-sm">{v.type}</span> },
    { key: "capacity", header: "Capacity", render: (v) => <span className="text-sm tabular-nums">{v.capacity === null ? "—" : `${v.capacity} kg`}</span> },
    {
      key: "rider",
      header: "Assigned rider",
      render: (v) =>
        v.riderId === null ? (
          <span className="text-sm text-muted-foreground">Unassigned</span>
        ) : (
          <span className="text-sm">{rowsById.get(v.riderId)?.fullName ?? "—"}</span>
        ),
    },
    {
      key: "today",
      header: "Rider today",
      render: (v) => {
        const row = v.riderId ? rowsById.get(v.riderId) : undefined;
        if (!row) return <span className="text-sm text-muted-foreground">—</span>;
        return (
          <span className="flex items-center gap-2">
            <RiderStageBadge stage={riderDayStage(row)} />
            {row.run && <span className="text-xs tabular-nums text-muted-foreground">{`${row.tasksCompleted} of ${row.tasksAssigned}`}</span>}
          </span>
        );
      },
    },
  ];

  return (
    <div className="space-y-3">
      <div role="group" aria-label="Vehicle filter" className="flex flex-wrap items-center gap-2">
        <Button type="button" size="sm" variant={filter === "all" ? "secondary" : "outline"} aria-pressed={filter === "all"} onClick={() => onFilterChange("all")}>
          {vehicles ? `All ${vehicles.length}` : "All"}
        </Button>
        <Button type="button" size="sm" variant={filter === "unassigned" ? "secondary" : "outline"} aria-pressed={filter === "unassigned"} onClick={() => onFilterChange("unassigned")}>
          {vehicles ? `Unassigned ${unassigned.length}` : "Unassigned"}
        </Button>
      </div>
      {isError ? (
        <ErrorState title="Couldn't load vehicles" description="Something went wrong fetching the fleet." onRetry={onRetry} />
      ) : (
        <DataTable
          columns={columns}
          data={vehicles && toClientPage(shown, page, PAGE_SIZE)}
          isLoading={isLoading}
          emptyTitle={filter === "unassigned" ? "Every vehicle has a rider" : "No vehicles yet"}
          emptyDescription="Add a vehicle to assign it to a rider."
          onPageChange={setPage}
          rowKey={(v) => v.id}
        />
      )}
      <p className="text-xs text-muted-foreground">Reassigning a vehicle or marking it off the road needs backend support.</p>
    </div>
  );
}
```

- [ ] **Step 3: Run the tests**

Run: `npx vitest run src/features/riders/components/RidersFleetTab.test.tsx` then `npx tsc --noEmit`
Expected: PASS; 0 type errors. (If `ErrorState`'s retry button has a different accessible name, read `src/components/states/ErrorState.tsx` and match it in the test.)

- [ ] **Step 4: Commit**

```bash
git add src/Web/src
git commit -m "$(cat <<'EOF'
feat(web): riders fleet tab and rider stage badge

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 10: Rider Sheet — header, today card, location card, cash card

**Files:**
- Create: `src/features/rider-tracking/components/RouteMiniMap.tsx`
- Create: `src/features/riders/components/RiderTodayCard.tsx`, `RiderLocationCard.tsx`, `RiderCashCard.tsx`, `RiderDetailSheet.tsx` (+ `RiderDetailSheet.test.tsx`)
- Modify: `src/app/(staff)/layout.tsx` (mount the Sheet)

**Interfaces:**
- Consumes: `useRider`, `useRiderToday`, `useRunBreadcrumbs`, `riderModel`, `useRiderPanelStore.openDetail/close/openAssignTask` (Task 7); `RiderStageBadge`, `RiderInactiveBadge` (Task 9); existing `useRiderCash(params, options)`, `useShipmentPanelStore.openDetail(awb)`, `useBranches`, `branchCode`, `SheetHeaderBlock`, `ComingSoonSlot`, `formatRs`, `formatAge`.
- Produces: `RiderDetailSheet()` (no props; opens on `useRiderPanelStore` panel `{ mode: "detail", riderId }`), `RouteMiniMap({ points: RiderLocation[] })`.

- [ ] **Step 1: Write the failing Sheet tests**

Create `src/features/riders/components/RiderDetailSheet.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";
import type { RiderBoardRow, RiderToday } from "@/features/riders/types";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";

vi.mock("next/dynamic", () => ({
  default: () => function RouteStub({ points }: { points: unknown[] }) { return <div>{`route map ${points.length} points`}</div>; },
}));
let riderQuery: { data?: unknown; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/riders/hooks/useRider", () => ({ useRider: () => riderQuery }));
let todayQuery: { data?: RiderToday; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/riders/hooks/useRiderToday", () => ({ useRiderToday: () => todayQuery }));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", code: "KTM", name: "Kathmandu Hub" }] }),
}));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
let cashQuery: { data?: unknown; isLoading: boolean; isError: boolean; refetch: () => void };
const useRiderCash = vi.fn();
vi.mock("@/features/cod/hooks/useRiderCash", () => ({ useRiderCash: (...args: unknown[]) => useRiderCash(...args) }));
let crumbs: { data?: unknown[]; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/rider-tracking/hooks/useRunBreadcrumbs", () => ({ useRunBreadcrumbs: () => crumbs }));

import { RiderDetailSheet } from "./RiderDetailSheet";

const ago = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

function boardRow(overrides: Partial<RiderBoardRow> = {}): RiderBoardRow {
  return {
    riderId: "r1", userId: "u1", fullName: "Asha Gurung", baseBranchId: "b1", roleType: "DeliveryRider", isActive: true,
    vehiclePlate: "BA 2 PA 1111", run: { id: "run1", status: "Active" }, tasksAssigned: 9, tasksCompleted: 4, tasksFailed: 1,
    lastPingAt: ago(3), ...overrides,
  };
}

function tasks(count: number) {
  return Array.from({ length: count }, (_, i) => ({
    taskId: `t${i + 1}`, shipmentId: `s${i + 1}`, awb: i === 1 ? "" : `AWB-${i + 1}`, type: "Delivery" as const,
    sequence: i + 1, status: i === 0 ? ("Completed" as const) : ("Pending" as const),
  }));
}

beforeEach(() => {
  granted = ["delivery.assign"];
  riderQuery = { data: { id: "r1", userId: "u1", baseBranchId: "b1", licenseNumber: "LIC-7", roleType: "DeliveryRider", isActive: true }, isLoading: false, isError: false, refetch: vi.fn() };
  todayQuery = { data: { rider: boardRow(), tasks: tasks(9) }, isLoading: false, isError: false, refetch: vi.fn() };
  cashQuery = { data: { items: [] }, isLoading: false, isError: false, refetch: vi.fn() };
  useRiderCash.mockReset();
  useRiderCash.mockImplementation(() => cashQuery);
  crumbs = { data: [], isLoading: false, isError: false, refetch: vi.fn() };
  useShipmentPanelStore.setState({ panel: { mode: "closed" } });
  useRiderPanelStore.setState({ panel: { mode: "detail", riderId: "r1" } });
});

describe("RiderDetailSheet", () => {
  it("renders nothing while closed", () => {
    useRiderPanelStore.setState({ panel: { mode: "closed" } });
    renderWithIntl(<RiderDetailSheet />);
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("shows a skeleton while loading and an error with retry", () => {
    todayQuery = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    const { unmount } = renderWithIntl(<RiderDetailSheet />);
    expect(screen.getByText("Loading")).toBeInTheDocument();
    unmount();

    todayQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    renderWithIntl(<RiderDetailSheet />);
    expect(screen.getByText("Couldn't load this rider")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(todayQuery.refetch).toHaveBeenCalled();
  });

  it("shows the header, today's progress and the first eight tasks", () => {
    renderWithIntl(<RiderDetailSheet />);
    expect(screen.getByText("RIDER · DELIVERY · KTM")).toBeInTheDocument();
    expect(screen.getByText("Asha Gurung")).toBeInTheDocument();
    expect(screen.getByText("On run")).toBeInTheDocument();
    expect(screen.getByText("LIC-7")).toBeInTheDocument();
    expect(screen.getByText("BA 2 PA 1111")).toBeInTheDocument();
    expect(screen.getByText("Kathmandu Hub")).toBeInTheDocument();
    expect(screen.getByText("4 of 9 done · 1 failed")).toBeInTheDocument();
    expect(screen.getByText("AWB-8")).toBeInTheDocument();
    expect(screen.queryByText("AWB-9")).toBeNull();
    expect(screen.getByText("+ 1 more tasks")).toBeInTheDocument();
    expect(screen.getByText("Counts are today's run only (date in UTC).")).toBeInTheDocument();
    expect(screen.getByText("Attendance and delivery history")).toBeInTheDocument();
  });

  it("opens the shipment Sheet from an AWB and closes the rider Sheet", () => {
    renderWithIntl(<RiderDetailSheet />);
    fireEvent.click(screen.getByRole("button", { name: "AWB-1" }));
    expect(useShipmentPanelStore.getState().panel).toEqual({ mode: "detail", awb: "AWB-1" });
    expect(useRiderPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("says No run today and offers Assign task only with delivery.assign", () => {
    todayQuery = { data: { rider: boardRow({ run: null, tasksAssigned: 0, tasksCompleted: 0, tasksFailed: 0 }), tasks: [] }, isLoading: false, isError: false, refetch: vi.fn() };
    granted = ["rider.location.read"];
    renderWithIntl(<RiderDetailSheet />);
    expect(screen.getByText("No run today")).toBeInTheDocument();
    expect(screen.getByText("Off duty")).toBeInTheDocument();
    expect(screen.getByText("No location today")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Assign task" })).toBeNull();
  });

  it("hides the location and cash cards without their permissions", () => {
    granted = [];
    renderWithIntl(<RiderDetailSheet />);
    expect(screen.queryByText("Location")).toBeNull();
    expect(screen.queryByText("Cash held")).toBeNull();
    expect(useRiderCash).toHaveBeenCalledWith({}, { enabled: false });
  });

  it("shows today's route and last ping with location access", () => {
    granted = ["rider.location.read"];
    crumbs = {
      data: [
        { riderId: "r1", latitude: 27.7, longitude: 85.3, accuracyMeters: 12, recordedAt: ago(30), receivedAt: ago(30) },
        { riderId: "r1", latitude: 27.71, longitude: 85.31, accuracyMeters: 8.2, recordedAt: ago(3), receivedAt: ago(3) },
      ],
      isLoading: false, isError: false, refetch: vi.fn(),
    };
    renderWithIntl(<RiderDetailSheet />);
    expect(screen.getByText("route map 2 points")).toBeInTheDocument();
    expect(screen.getByText("Last ping 3 min ago · ±8 m · 2 pings today")).toBeInTheDocument();
  });

  it("shows cash held with cod.read, or says none is held", () => {
    granted = ["cod.read"];
    const { unmount } = renderWithIntl(<RiderDetailSheet />);
    expect(screen.getByText("No cash held")).toBeInTheDocument();
    unmount();

    cashQuery = {
      data: { items: [{ riderId: "r1", branchId: "b1", parcelCount: 2, amount: 2350, oldestCollectedAt: ago(90), latestCollectedAt: ago(20) }] },
      isLoading: false, isError: false, refetch: vi.fn(),
    };
    renderWithIntl(<RiderDetailSheet />);
    expect(screen.getByText("Rs 2,350")).toBeInTheDocument();
    expect(screen.getByText("From 2 COD parcels · oldest collected 1 h ago")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open in COD" })).toHaveAttribute("href", "/cod?branch=b1");
  });
});
```

Run: `npx vitest run src/features/riders/components/RiderDetailSheet.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 2: Write the route mini-map**

Create `src/features/rider-tracking/components/RouteMiniMap.tsx`:

```tsx
"use client";

import { CircleMarker, MapContainer, Polyline, TileLayer } from "react-leaflet";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import type { RiderLocation } from "@/features/rider-tracking/hooks/useRiderLocations";

/** Today's breadcrumb line: hollow start point, solid latest point. Needs at least one point. */
export function RouteMiniMap({ points }: { points: RiderLocation[] }) {
  const path = points.map((p) => [p.latitude, p.longitude] as [number, number]);
  const start = path[0];
  const latest = path[path.length - 1];
  const bounds = path.length > 1 ? L.latLngBounds(path) : undefined;

  return (
    <MapContainer
      center={bounds ? undefined : latest}
      zoom={15}
      bounds={bounds}
      boundsOptions={{ padding: [16, 16] }}
      scrollWheelZoom={false}
      className="h-48 w-full rounded-lg"
    >
      <TileLayer
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
      />
      {path.length > 1 && <Polyline positions={path} pathOptions={{ className: "stroke-primary", weight: 3 }} />}
      <CircleMarker center={start} radius={5} pathOptions={{ className: "fill-background stroke-primary", fillOpacity: 1, weight: 2 }} />
      <CircleMarker center={latest} radius={6} pathOptions={{ className: "fill-primary stroke-background", fillOpacity: 1, weight: 2 }} />
    </MapContainer>
  );
}
```

- [ ] **Step 3: Write the three cards**

Create `src/features/riders/components/RiderTodayCard.tsx`:

```tsx
"use client";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { progressShare } from "@/features/riders/lib/riderModel";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import type { DeliveryTaskStatus, RiderToday } from "@/features/riders/types";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";

const VISIBLE_TASKS = 8;
const STATUS_TEXT: Record<DeliveryTaskStatus, string> = { Pending: "Pending", Completed: "Done", Failed: "Failed" };

function percent(part: number, total: number): string {
  return `${Math.round(progressShare(part, total) * 100)}%`;
}

export function RiderTodayCard({ today }: { today: RiderToday }) {
  const { rider, tasks } = today;
  const openShipment = useShipmentPanelStore((s) => s.openDetail);
  const closeRider = useRiderPanelStore((s) => s.close);

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-semibold">Today</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        {!rider.run ? (
          <p className="text-sm text-muted-foreground">No run today</p>
        ) : (
          <>
            <p className="text-sm tabular-nums text-foreground">{`${rider.tasksCompleted} of ${rider.tasksAssigned} done · ${rider.tasksFailed} failed`}</p>
            <div
              role="img"
              aria-label={`${rider.tasksCompleted} completed and ${rider.tasksFailed} failed of ${rider.tasksAssigned} tasks`}
              className="flex h-1.5 overflow-hidden rounded-full bg-muted"
            >
              <span className="h-full bg-status-delivered" style={{ width: percent(rider.tasksCompleted, rider.tasksAssigned) }} />
              <span className="h-full bg-destructive" style={{ width: percent(rider.tasksFailed, rider.tasksAssigned) }} />
            </div>
            {tasks.length === 0 ? (
              <p className="text-sm text-muted-foreground">No tasks on this run yet.</p>
            ) : (
              <ol className="divide-y divide-border rounded-md border border-border">
                {tasks.slice(0, VISIBLE_TASKS).map((task) => (
                  <li key={task.taskId} className="flex items-center gap-3 px-3 py-2">
                    <span className="w-5 shrink-0 text-xs tabular-nums text-muted-foreground">{task.sequence}</span>
                    {task.awb ? (
                      <Button
                        type="button"
                        variant="link"
                        className="h-auto p-0 font-mono text-sm"
                        onClick={() => {
                          closeRider();
                          openShipment(task.awb);
                        }}
                      >
                        {task.awb}
                      </Button>
                    ) : (
                      <span className="font-mono text-sm text-muted-foreground">—</span>
                    )}
                    <span className="text-xs text-muted-foreground">{task.type}</span>
                    <span className="ml-auto text-xs text-foreground">{STATUS_TEXT[task.status]}</span>
                  </li>
                ))}
              </ol>
            )}
            {tasks.length > VISIBLE_TASKS && (
              <p className="text-xs text-muted-foreground">{`+ ${tasks.length - VISIBLE_TASKS} more tasks`}</p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
```

Create `src/features/riders/components/RiderLocationCard.tsx`:

```tsx
"use client";

import dynamic from "next/dynamic";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useRunBreadcrumbs } from "@/features/rider-tracking/hooks/useRunBreadcrumbs";
import { formatMinutesAgo } from "@/features/riders/lib/riderModel";
import type { RiderBoardRow } from "@/features/riders/types";

const RouteMiniMap = dynamic(() => import("@/features/rider-tracking/components/RouteMiniMap").then((mod) => mod.RouteMiniMap), {
  ssr: false,
  loading: () => <Skeleton className="h-48 w-full rounded-lg" />,
});

export function RiderLocationCard({ row }: { row: RiderBoardRow }) {
  const canSee = usePermission("rider.location.read");
  const crumbs = useRunBreadcrumbs(row.run?.id ?? null, canSee);
  if (!canSee) return null;

  const points = crumbs.data ?? [];
  const latest = points[points.length - 1];

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-semibold">Location</CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        {!row.run ? (
          <p className="text-sm text-muted-foreground">No location today</p>
        ) : crumbs.isLoading ? (
          <Skeleton className="h-48 w-full rounded-lg" />
        ) : crumbs.isError ? (
          <ErrorState title="Couldn't load today's route" description="Live positions are unavailable right now." onRetry={() => crumbs.refetch()} />
        ) : !latest ? (
          <p className="text-sm text-muted-foreground">No location today</p>
        ) : (
          <>
            <RouteMiniMap points={points} />
            <p className="text-xs text-muted-foreground">
              {`Last ping ${formatMinutesAgo(latest.receivedAt)}${latest.accuracyMeters === null ? "" : ` · ±${Math.round(latest.accuracyMeters)} m`} · ${points.length} ${points.length === 1 ? "ping" : "pings"} today`}
            </p>
          </>
        )}
      </CardContent>
    </Card>
  );
}
```

Create `src/features/riders/components/RiderCashCard.tsx`:

```tsx
"use client";

import Link from "next/link";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useRiderCash } from "@/features/cod/hooks/useRiderCash";
import { formatRs } from "@/features/cod/lib/codFormat";
import { cashByRider } from "@/features/riders/lib/riderModel";
import { formatAge } from "@/lib/dates/age";

/** Cash the rider holds across every branch in the caller's COD scope (not only the page filter). */
export function RiderCashCard({ riderId, baseBranchId }: { riderId: string; baseBranchId: string }) {
  const canSee = usePermission("cod.read");
  const cash = useRiderCash({}, { enabled: canSee });
  if (!canSee) return null;

  const holding = cash.data ? cashByRider(cash.data.items).get(riderId) : undefined;

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-semibold">Cash held</CardTitle>
      </CardHeader>
      <CardContent className="space-y-1.5">
        {cash.isLoading ? (
          <Skeleton className="h-10 w-40" />
        ) : cash.isError ? (
          <ErrorState title="Couldn't load cash held" description="COD holdings are unavailable right now." onRetry={() => cash.refetch()} />
        ) : !holding ? (
          <p className="text-sm text-muted-foreground">No cash held</p>
        ) : (
          <>
            <p className="text-2xl font-semibold tabular-nums tracking-tight text-foreground">{formatRs(holding.amount)}</p>
            <p className="text-xs text-muted-foreground">
              {`From ${holding.parcelCount} COD ${holding.parcelCount === 1 ? "parcel" : "parcels"} · oldest collected ${formatAge(holding.oldestCollectedAt)} ago`}
            </p>
            <Link href={`/cod?branch=${encodeURIComponent(baseBranchId)}`} className="text-sm font-medium text-primary hover:underline">
              Open in COD
            </Link>
          </>
        )}
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 4: Write the Sheet and mount it**

Create `src/features/riders/components/RiderDetailSheet.tsx`:

```tsx
"use client";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { branchCode } from "@/features/branches/lib/branchCode";
import { RiderCashCard } from "@/features/riders/components/RiderCashCard";
import { RiderLocationCard } from "@/features/riders/components/RiderLocationCard";
import { RiderInactiveBadge, RiderStageBadge } from "@/features/riders/components/RiderStageBadge";
import { RiderTodayCard } from "@/features/riders/components/RiderTodayCard";
import { useRider } from "@/features/riders/hooks/useRider";
import { useRiderToday } from "@/features/riders/hooks/useRiderToday";
import { riderDayStage, riderInitials } from "@/features/riders/lib/riderModel";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";

export function RiderDetailSheet() {
  const panel = useRiderPanelStore((s) => s.panel);
  const close = useRiderPanelStore((s) => s.close);
  const openAssignTask = useRiderPanelStore((s) => s.openAssignTask);
  const isOpen = panel.mode === "detail";
  const riderId = panel.mode === "detail" ? panel.riderId : "";

  const rider = useRider(riderId);
  const today = useRiderToday(riderId, isOpen);
  const { data: branches } = useBranches();
  const canAssign = usePermission("delivery.assign");

  const row = today.data?.rider;
  const isError = rider.isError || today.isError;
  const isLoading = !isError && (rider.isLoading || today.isLoading);
  const ready = Boolean(row && rider.data && today.data) && !isError;
  const baseBranchName = row ? branches?.find((b) => b.id === row.baseBranchId)?.name ?? "—" : "—";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <div className="flex flex-col gap-6">
          {!ready && <SheetTitle className="sr-only">Rider</SheetTitle>}
          {isLoading && <DetailSkeleton fieldRows={2} />}
          {isError && (
            <ErrorState
              title="Couldn't load this rider"
              description="They may not exist, or may be based outside your branches."
              onRetry={() => {
                rider.refetch();
                today.refetch();
              }}
            />
          )}
          {ready && row && rider.data && today.data && (
            <>
              <SheetHeaderBlock
                eyebrow={`RIDER · ${row.roleType === "PickupRider" ? "PICKUP" : "DELIVERY"} · ${branchCode(branches, row.baseBranchId)}`}
                title={
                  <span className="flex items-center gap-3">
                    <Avatar className="h-9 w-9" aria-hidden="true">
                      <AvatarFallback className="text-xs font-medium">{riderInitials(row.fullName)}</AvatarFallback>
                    </Avatar>
                    <span>{row.fullName}</span>
                  </span>
                }
                status={row.isActive ? <RiderStageBadge stage={riderDayStage(row)} /> : <RiderInactiveBadge />}
                facts={[
                  { label: "Base branch", value: baseBranchName },
                  { label: "Licence", value: rider.data.licenseNumber },
                  { label: "Vehicle", value: row.vehiclePlate ?? "Unassigned" },
                  { label: "Account", value: row.isActive ? "Active" : "Inactive" },
                ]}
              />
              <RiderTodayCard today={today.data} />
              <RiderLocationCard row={row} />
              <RiderCashCard riderId={row.riderId} baseBranchId={row.baseBranchId} />
              <ComingSoonSlot
                title="Attendance and delivery history"
                description="Shift check-ins and the last 30 days of deliveries will show here."
              />
              <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
                <p className="text-2xs text-muted-foreground">Counts are today&apos;s run only (date in UTC).</p>
                {canAssign && (
                  <Button type="button" onClick={() => openAssignTask()}>
                    Assign task
                  </Button>
                )}
              </div>
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

In `src/app/(staff)/layout.tsx`, add the import next to the other rider Sheets:

```tsx
import { RiderDetailSheet } from "@/features/riders/components/RiderDetailSheet";
```

and render `<RiderDetailSheet />` on the line after `<AssignTaskSheet />`.

- [ ] **Step 5: Run the tests**

Run: `npx vitest run src/features/riders/components/RiderDetailSheet.test.tsx` then `npx tsc --noEmit`
Expected: PASS; 0 type errors.

- [ ] **Step 6: Commit**

```bash
git add src/Web/src
git commit -m "$(cat <<'EOF'
feat(web): rider Sheet with today's tasks, route and cash held

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 11: Riders page — strip, tabs, Riders table; live-map redirect and nav

**Files:**
- Create: `src/features/riders/components/RidersBoardTab.tsx`
- Modify: `src/app/(staff)/riders/page.tsx` (rewrite), `src/app/(staff)/riders/page.test.tsx` (rewrite)
- Modify: `src/app/(staff)/riders/live-map/page.tsx` (redirect), `src/app/(staff)/riders/live-map/page.test.tsx` (rewrite)
- Modify: `src/lib/navigation/navItems.ts`, `messages/en.json`, `messages/ne.json`
- Create: `src/lib/navigation/navItems.test.ts`

**Interfaces:**
- Consumes: everything from Tasks 7–10; `PageHeader`, `PipelineStrip` (select + highlight + caption), `useSearchParamState`, `useFilterPage`, `toClientPage`, `useSession` (`hasAllBranchesScope`, `branches[].branchId`), `useRiderCash`, `useVehicles`.
- Produces: the `/riders` page reading `?tab=riders|map|fleet`, `?stage=off|on|done`, `?branch=`, `?role=Pickup|Delivery`, `?inactive=1`, `?fleet=unassigned`; `RidersBoardTab` props `{ rows?: RiderBoardRow[]; isLoading: boolean; isError: boolean; onRetry: () => void; cash?: Map<string, RiderCashTotal>; showCash: boolean; selectedRiderId?: string; onOpenRider: (riderId: string) => void; filterKey: string; now: Date }`.

- [ ] **Step 1: Write the failing page tests**

Replace `src/app/(staff)/riders/page.test.tsx` with:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { RiderBoardRow } from "@/features/riders/types";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/riders",
  useSearchParams: () => new URLSearchParams(search),
}));

let board: { data?: { items: RiderBoardRow[] }; isLoading: boolean; isError: boolean; refetch: () => void };
const useRiderBoard = vi.fn();
vi.mock("@/features/riders/hooks/useRiderBoard", () => ({ useRiderBoard: (...args: unknown[]) => useRiderBoard(...args) }));
const useRiderCash = vi.fn();
vi.mock("@/features/cod/hooks/useRiderCash", () => ({ useRiderCash: (...args: unknown[]) => useRiderCash(...args) }));
vi.mock("@/features/riders/hooks/useVehicles", () => ({
  useVehicles: () => ({ data: [{ id: "v1", riderId: null, type: "Bike", plateNumber: "BA 1", capacity: null }], isLoading: false, isError: false, refetch: vi.fn() }),
}));
let session: { hasAllBranchesScope: boolean; branches: { branchId: string }[] };
vi.mock("@/features/auth/hooks/useSession", () => ({ useSession: () => ({ data: { userId: "u1", ...session } }) }));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", code: "KTM", name: "Kathmandu Hub" }, { id: "b2", code: "PKR", name: "Pokhara Hub" }] }),
}));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
const openDetail = vi.fn();
vi.mock("@/features/riders/store/riderPanelStore", () => ({
  useRiderPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: "closed" }, openDetail, openCreateRider: vi.fn(), openCreateVehicle: vi.fn(), openAssignTask: vi.fn() }),
}));
vi.mock("@/features/riders/components/RidersMapTab", () => ({
  RidersMapTab: ({ branchId }: { branchId?: string }) => <div data-testid="map-tab">{`map for ${branchId ?? "none"}`}</div>,
}));
vi.mock("@/features/riders/components/RidersFleetTab", () => ({
  RidersFleetTab: ({ vehicles }: { vehicles?: unknown[] }) => <div data-testid="fleet-tab">{`fleet ${vehicles?.length ?? "—"}`}</div>,
}));

import RidersPage from "./page";

const ago = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

function row(overrides: Partial<RiderBoardRow>): RiderBoardRow {
  return {
    riderId: "r1", userId: "u1", fullName: "Asha Gurung", baseBranchId: "b1", roleType: "DeliveryRider", isActive: true,
    vehiclePlate: "BA 2 PA 1111", run: { id: "run1", status: "Active" }, tasksAssigned: 6, tasksCompleted: 4, tasksFailed: 1,
    lastPingAt: ago(3), ...overrides,
  };
}

const ROWS = [
  row({}),
  row({ riderId: "r2", fullName: "Bikash Rai", lastPingAt: ago(40), vehiclePlate: null }),
  row({ riderId: "r3", fullName: "Chandra Thapa", roleType: "PickupRider", run: null, tasksAssigned: 0, tasksCompleted: 0, tasksFailed: 0, lastPingAt: null }),
  row({ riderId: "r4", fullName: "Dipa Karki", run: { id: "run4", status: "Active" }, tasksAssigned: 0, tasksCompleted: 0, tasksFailed: 0 }),
  row({ riderId: "r5", fullName: "Old Rider", isActive: false, run: null }),
];

beforeEach(() => {
  replace.mockReset();
  openDetail.mockReset();
  search = "";
  granted = ["rider.location.read", "cod.read", "delivery.assign", "rider.create"];
  session = { hasAllBranchesScope: true, branches: [] };
  board = { data: { items: ROWS }, isLoading: false, isError: false, refetch: vi.fn() };
  useRiderBoard.mockReset();
  useRiderBoard.mockImplementation(() => board);
  useRiderCash.mockReset();
  useRiderCash.mockReturnValue({
    data: { items: [{ riderId: "r1", branchId: "b1", parcelCount: 2, amount: 2350, oldestCollectedAt: ago(60), latestCollectedAt: ago(10) }] },
    isLoading: false, isError: false, refetch: vi.fn(),
  });
});

describe("RidersPage", () => {
  it("counts active riders by stage and highlights stale on-run locations", () => {
    render(<RidersPage />);
    const strip = screen.getByRole("list", { name: "Rider day" });
    expect(within(strip).getByRole("button", { name: /Off duty/ })).toHaveTextContent("1");
    const onRun = within(strip).getByRole("button", { name: /On run/ });
    expect(onRun).toHaveTextContent("3");
    expect(onRun).toHaveAttribute("aria-current", "step");
    expect(onRun).toHaveTextContent("1 with stale location");
    expect(screen.getByText("All branches · 3 out today")).toBeInTheDocument();
  });

  it("never shows zeros while the board loads or fails", () => {
    board = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    const { unmount } = render(<RidersPage />);
    expect(screen.getByRole("status", { name: "Loading rider counts" })).toBeInTheDocument();
    expect(screen.getByText("All branches · —")).toBeInTheDocument();
    unmount();

    board = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<RidersPage />);
    expect(screen.getByText("Couldn't load rider counts")).toBeInTheDocument();
  });

  it("renders the rider table with progress, last seen, cash and status", () => {
    render(<RidersPage />);
    expect(screen.getByRole("button", { name: /Asha Gurung/ })).toBeInTheDocument();
    expect(screen.getByText("4 of 6")).toBeInTheDocument();
    expect(screen.getByText("1 failed")).toBeInTheDocument();
    expect(screen.getByText("0 of 0")).toBeInTheDocument();
    expect(screen.getByText("No run")).toBeInTheDocument();
    expect(screen.getByText("3 min ago")).toBeInTheDocument();
    expect(screen.getByText("40 min ago · stale")).toBeInTheDocument();
    expect(screen.getByText("No ping today")).toBeInTheDocument();
    expect(screen.getByText("Unassigned")).toBeInTheDocument();
    expect(screen.getByText("Rs 2,350")).toBeInTheDocument();
    expect(screen.queryByText("Old Rider")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: /Asha Gurung/ }));
    expect(openDetail).toHaveBeenCalledWith("r1");
  });

  it("hides the cash column without cod.read and the Map tab without rider.location.read", () => {
    granted = [];
    render(<RidersPage />);
    expect(screen.queryByText("Cash held")).toBeNull();
    expect(screen.queryByRole("tab", { name: "Map" })).toBeNull();
    expect(useRiderCash).toHaveBeenCalledWith({ branchId: undefined }, { enabled: false });
    expect(screen.queryByRole("button", { name: /Add rider/ })).toBeNull();
  });

  it("filters by stage from the strip and clears it from the chip", () => {
    const { unmount } = render(<RidersPage />);
    fireEvent.click(screen.getByRole("button", { name: /Off duty/ }));
    expect(replace).toHaveBeenCalledWith("/riders?stage=off", { scroll: false });
    unmount();

    search = "stage=off";
    render(<RidersPage />);
    expect(screen.getByRole("button", { name: /Chandra Thapa/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Asha Gurung/ })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Clear stage filter Off duty" }));
    expect(replace).toHaveBeenLastCalledWith("/riders", { scroll: false });
  });

  it("shows inactive riders and filters by role from the URL", () => {
    search = "inactive=1&role=Delivery";
    render(<RidersPage />);
    expect(screen.getByText("Old Rider")).toBeInTheDocument();
    expect(screen.queryByText("Chandra Thapa")).toBeNull();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  it("switches tabs through the URL and passes the picked branch", () => {
    render(<RidersPage />);
    fireEvent.mouseDown(screen.getByRole("tab", { name: /Fleet/ }));
    expect(replace).toHaveBeenCalledWith("/riders?tab=fleet", { scroll: false });

    search = "tab=map&branch=b2";
    render(<RidersPage />);
    expect(screen.getByText("map for b2")).toBeInTheDocument();
    expect(useRiderBoard).toHaveBeenLastCalledWith("b2");
  });

  it("gives a one-branch user's map their branch without a picker", () => {
    session = { hasAllBranchesScope: false, branches: [{ branchId: "b1" }] };
    search = "tab=map";
    render(<RidersPage />);
    expect(screen.getByText("map for b1")).toBeInTheDocument();
    expect(screen.queryByRole("combobox", { name: "Branch" })).toBeNull();
  });

  it("falls back to the Riders tab when the Map tab is not allowed", () => {
    granted = [];
    search = "tab=map";
    render(<RidersPage />);
    expect(screen.queryByTestId("map-tab")).toBeNull();
    expect(screen.getByRole("button", { name: /Asha Gurung/ })).toBeInTheDocument();
  });
});
```

Replace `src/app/(staff)/riders/live-map/page.test.tsx` with:

```tsx
import { describe, expect, it, vi } from "vitest";

vi.mock("next/navigation", () => ({ redirect: vi.fn() }));

import { redirect } from "next/navigation";
import LiveMapRedirect from "./page";

describe("/riders/live-map", () => {
  it("redirects to the Map tab", () => {
    LiveMapRedirect({ searchParams: {} });
    expect(redirect).toHaveBeenCalledWith("/riders?tab=map");
  });

  it("keeps the branch when one is given", () => {
    LiveMapRedirect({ searchParams: { branch: "b 1" } });
    expect(redirect).toHaveBeenLastCalledWith("/riders?tab=map&branch=b+1");
  });
});
```

Create `src/lib/navigation/navItems.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { NAV_ITEMS } from "./navItems";

describe("NAV_ITEMS", () => {
  it("has no separate live map entry; the map is a tab on /riders", () => {
    expect(NAV_ITEMS.some((item) => item.href === "/riders/live-map")).toBe(false);
    expect(NAV_ITEMS.some((item) => item.href === "/riders")).toBe(true);
  });
});
```

Run: `npx vitest run "src/app/(staff)/riders" src/lib/navigation`
Expected: FAIL.

- [ ] **Step 2: Write the Riders table**

Create `src/features/riders/components/RidersBoardTab.tsx`:

```tsx
"use client";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { ErrorState } from "@/components/states/ErrorState";
import { formatRs } from "@/features/cod/lib/codFormat";
import { RiderInactiveBadge, RiderStageBadge } from "@/features/riders/components/RiderStageBadge";
import {
  lastSeenText, progressShare, riderDayStage, riderInitials, roleLabel, type RiderCashTotal,
} from "@/features/riders/lib/riderModel";
import type { RiderBoardRow } from "@/features/riders/types";
import { useFilterPage } from "@/lib/hooks/useFilterPage";
import { toClientPage } from "@/lib/paging/clientPage";
import { cn } from "@/lib/utils";

const PAGE_SIZE = 20;

export function RidersBoardTab({
  rows, isLoading, isError, onRetry, cash, showCash, selectedRiderId, onOpenRider, filterKey, now,
}: {
  rows?: RiderBoardRow[];
  isLoading: boolean;
  isError: boolean;
  onRetry: () => void;
  cash?: Map<string, RiderCashTotal>;
  showCash: boolean;
  selectedRiderId?: string;
  onOpenRider: (riderId: string) => void;
  filterKey: string;
  now: Date;
}) {
  const [page, setPage] = useFilterPage(filterKey);

  const columns: ColumnDef<RiderBoardRow>[] = [
    {
      key: "rider",
      header: "Rider",
      render: (r) => (
        <button type="button" onClick={() => onOpenRider(r.riderId)} className="flex items-center gap-2.5 text-left">
          <Avatar className="h-8 w-8" aria-hidden="true">
            <AvatarFallback className="text-2xs font-medium">{riderInitials(r.fullName)}</AvatarFallback>
          </Avatar>
          <span className="flex flex-col">
            <span className="text-sm font-medium text-foreground hover:underline">{r.fullName}</span>
            <span className="text-xs text-muted-foreground">{roleLabel(r.roleType)}</span>
          </span>
        </button>
      ),
    },
    {
      key: "vehicle",
      header: "Vehicle",
      render: (r) =>
        r.vehiclePlate ? <span className="font-mono text-sm">{r.vehiclePlate}</span> : <span className="text-sm text-muted-foreground">Unassigned</span>,
    },
    {
      key: "today",
      header: "Today",
      render: (r) =>
        !r.run ? (
          <span className="text-sm text-muted-foreground">No run</span>
        ) : (
          <div className="flex min-w-28 flex-col gap-1">
            <span className="text-sm tabular-nums">{`${r.tasksCompleted} of ${r.tasksAssigned}`}</span>
            <span className="block h-1 rounded-full bg-muted" aria-hidden="true">
              <span
                className="block h-1 rounded-full bg-status-delivered"
                style={{ width: `${Math.round(progressShare(r.tasksCompleted, r.tasksAssigned) * 100)}%` }}
              />
            </span>
            {r.tasksFailed > 0 && <span className="text-xs font-medium text-destructive">{`${r.tasksFailed} failed`}</span>}
          </div>
        ),
    },
    {
      key: "lastSeen",
      header: "Last seen",
      render: (r) => {
        const seen = lastSeenText(r.lastPingAt, now);
        return <span className={cn("whitespace-nowrap text-sm", seen.stale ? "text-muted-foreground" : "text-foreground")}>{seen.text}</span>;
      },
    },
    ...(showCash
      ? [
          {
            key: "cash",
            header: "Cash held",
            render: (r: RiderBoardRow) => {
              const holding = cash?.get(r.riderId);
              return <span className="text-sm tabular-nums">{holding ? formatRs(holding.amount) : "—"}</span>;
            },
          },
        ]
      : []),
    {
      key: "status",
      header: "Status",
      render: (r) => (r.isActive ? <RiderStageBadge stage={riderDayStage(r)} /> : <RiderInactiveBadge />),
    },
  ];

  if (isError) {
    return <ErrorState title="Couldn't load riders" description="Something went wrong fetching today's rider board." onRetry={onRetry} />;
  }

  return (
    <DataTable
      columns={columns}
      data={rows && toClientPage(rows, page, PAGE_SIZE)}
      isLoading={isLoading}
      emptyTitle="No riders here"
      emptyDescription="No rider matches these filters. Clear a filter or add a rider."
      onPageChange={setPage}
      rowKey={(r) => r.riderId}
      selectedRowId={selectedRiderId}
    />
  );
}
```

- [ ] **Step 3: Write the page**

Replace `src/app/(staff)/riders/page.tsx` with:

```tsx
"use client";

import { Suspense } from "react";
import { Plus, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { PageHeader } from "@/components/page/PageHeader";
import { PipelineStrip, type PipelineStage } from "@/components/page/PipelineStrip";
import { ErrorState } from "@/components/states/ErrorState";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useSession } from "@/features/auth/hooks/useSession";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useRiderCash } from "@/features/cod/hooks/useRiderCash";
import { RidersBoardTab } from "@/features/riders/components/RidersBoardTab";
import { RidersFleetTab } from "@/features/riders/components/RidersFleetTab";
import { RidersMapTab } from "@/features/riders/components/RidersMapTab";
import { useRiderBoard } from "@/features/riders/hooks/useRiderBoard";
import { useVehicles } from "@/features/riders/hooks/useVehicles";
import {
  RIDER_STAGES, cashByRider, filterBoardRows, fleetVehicles, parseFleetFilter, parseRiderRole, parseRiderStage,
  parseRidersTab, riderStageCounts, riderStageLabel, staleOnRunCount,
} from "@/features/riders/lib/riderModel";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import { useSearchParamState } from "@/lib/hooks/useSearchParamState";

const ALL = "all";

function RidersPageContent() {
  const { params, update } = useSearchParamState();
  const tab = parseRidersTab(params.get("tab"));
  const stage = parseRiderStage(params.get("stage"));
  const role = parseRiderRole(params.get("role"));
  const showInactive = params.get("inactive") === "1";
  const fleetFilter = parseFleetFilter(params.get("fleet"));
  const branchId = params.get("branch") ?? undefined;

  const { data: session } = useSession();
  const { data: branches } = useBranches();
  const scopedBranches = (branches ?? []).filter(
    (b) => session?.hasAllBranchesScope || session?.branches.some((s) => s.branchId === b.id),
  );
  const showBranchPicker = Boolean(session?.hasAllBranchesScope) || scopedBranches.length > 1;
  // A one-branch user has no picker; the map shows one branch, so it uses theirs.
  const mapBranchId = branchId ?? (!session?.hasAllBranchesScope && scopedBranches.length === 1 ? scopedBranches[0].id : undefined);
  const branchLabel = branchId ? scopedBranches.find((b) => b.id === branchId)?.name ?? "Selected branch" : "All branches";

  const canMap = usePermission("rider.location.read");
  const canCash = usePermission("cod.read");
  const canAssign = usePermission("delivery.assign");
  const canCreate = usePermission("rider.create");
  const activeTab = tab === "map" && !canMap ? "riders" : tab;

  const board = useRiderBoard(branchId);
  const riderCash = useRiderCash({ branchId }, { enabled: canCash });
  const vehicles = useVehicles();
  const panel = useRiderPanelStore((s) => s.panel);
  const openDetail = useRiderPanelStore((s) => s.openDetail);
  const openCreateRider = useRiderPanelStore((s) => s.openCreateRider);
  const openCreateVehicle = useRiderPanelStore((s) => s.openCreateVehicle);
  const openAssignTask = useRiderPanelStore((s) => s.openAssignTask);

  const now = new Date();
  const rows = board.data?.items;
  const counts = rows && riderStageCounts(rows);
  const staleCount = rows ? staleOnRunCount(rows, now) : 0;
  const shownRows = rows && filterBoardRows(rows, { stage, role, showInactive });
  const cash = riderCash.data ? cashByRider(riderCash.data.items) : undefined;
  const branchPicked = branchId !== undefined;
  const fleet =
    vehicles.data && (!branchPicked || rows)
      ? fleetVehicles(vehicles.data, new Set((rows ?? []).map((r) => r.riderId)), branchPicked)
      : undefined;

  const stages: PipelineStage[] = RIDER_STAGES.map((s) => ({
    key: s.key,
    label: s.label,
    token: s.token,
    count: counts ? counts[s.key] : null,
    caption: s.key === "on" && staleCount > 0 ? `${staleCount} with stale location` : undefined,
  }));

  return (
    <div className="space-y-5">
      <PageHeader
        eyebrow="Operations"
        title="Riders"
        description={`${branchLabel} · ${counts ? `${counts.on} out today` : "—"}`}
        actions={
          <>
            {showBranchPicker && (
              <Select value={branchId ?? ALL} onValueChange={(v) => update({ branch: v === ALL ? undefined : v })}>
                <SelectTrigger className="w-48" aria-label="Branch">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={ALL}>All branches</SelectItem>
                  {scopedBranches.map((b) => (
                    <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
            {canAssign && (
              <Button type="button" variant="outline" onClick={() => openAssignTask()}>
                Assign task
              </Button>
            )}
            {canCreate && activeTab === "fleet" && (
              <Button type="button" variant="outline" className="gap-1.5" onClick={() => openCreateVehicle()}>
                <Plus className="h-4 w-4" aria-hidden="true" />
                Add vehicle
              </Button>
            )}
            {canCreate && (
              <Button type="button" className="gap-1.5" onClick={() => openCreateRider()}>
                <Plus className="h-4 w-4" aria-hidden="true" />
                Add rider
              </Button>
            )}
          </>
        }
      />

      <section aria-label="Rider day summary" className="space-y-2">
        {board.isLoading && (
          <div role="status" aria-label="Loading rider counts" className="grid grid-cols-3 gap-2.5">
            {RIDER_STAGES.map((s) => (
              <Skeleton key={s.key} className="h-20 rounded-lg" />
            ))}
          </div>
        )}
        {board.isError && (
          <ErrorState title="Couldn't load rider counts" description="Today's rider summary is unavailable right now." onRetry={() => board.refetch()} />
        )}
        {counts && (
          <>
            <PipelineStrip
              ariaLabel="Rider day"
              stages={stages}
              highlightKey={staleCount > 0 ? "on" : undefined}
              selectedKey={stage}
              onSelect={(key) => update({ stage: key === stage ? undefined : key, tab: undefined })}
            />
            <p className="text-2xs text-muted-foreground">Today&apos;s runs (UTC date) · active riders only</p>
          </>
        )}
      </section>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <Tabs value={activeTab} onValueChange={(v) => update({ tab: v === "riders" ? undefined : v })}>
          <TabsList>
            <TabsTrigger value="riders">
              Riders
              {shownRows && <span className="ml-1.5 tabular-nums text-muted-foreground">{shownRows.length}</span>}
            </TabsTrigger>
            {canMap && <TabsTrigger value="map">Map</TabsTrigger>}
            <TabsTrigger value="fleet">
              Fleet
              {fleet && <span className="ml-1.5 tabular-nums text-muted-foreground">{fleet.length}</span>}
            </TabsTrigger>
          </TabsList>
        </Tabs>
        {activeTab === "riders" && (
          <div className="flex flex-wrap items-center gap-3">
            <Select value={role ?? ALL} onValueChange={(v) => update({ role: v === ALL ? undefined : v })}>
              <SelectTrigger className="w-40" aria-label="Role">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL}>All roles</SelectItem>
                <SelectItem value="Pickup">Pickup riders</SelectItem>
                <SelectItem value="Delivery">Delivery riders</SelectItem>
              </SelectContent>
            </Select>
            <div className="flex items-center gap-2">
              <Checkbox
                id="riders-show-inactive"
                checked={showInactive}
                onCheckedChange={(checked) => update({ inactive: checked === true ? "1" : undefined })}
              />
              <Label htmlFor="riders-show-inactive" className="text-sm font-normal">Show inactive</Label>
            </div>
            {stage && (
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-8 gap-1.5"
                aria-label={`Clear stage filter ${riderStageLabel(stage)}`}
                onClick={() => update({ stage: undefined })}
              >
                {`Stage: ${riderStageLabel(stage)}`}
                <X className="h-3.5 w-3.5" aria-hidden="true" />
              </Button>
            )}
          </div>
        )}
      </div>

      {activeTab === "riders" && (
        <RidersBoardTab
          rows={shownRows}
          isLoading={board.isLoading}
          isError={board.isError}
          onRetry={() => board.refetch()}
          cash={cash}
          showCash={canCash}
          selectedRiderId={panel.mode === "detail" ? panel.riderId : undefined}
          onOpenRider={openDetail}
          filterKey={`${stage ?? ""}|${role ?? ""}|${showInactive ? 1 : 0}|${branchId ?? ""}`}
          now={now}
        />
      )}
      {activeTab === "map" && (
        <RidersMapTab
          branchId={mapBranchId}
          branch={branches?.find((b) => b.id === mapBranchId)}
          rows={rows}
          cash={cash}
          showCash={canCash}
          onOpenRider={openDetail}
          now={now}
        />
      )}
      {activeTab === "fleet" && (
        <RidersFleetTab
          vehicles={fleet}
          isLoading={vehicles.isLoading || (branchPicked && board.isLoading)}
          isError={vehicles.isError}
          onRetry={() => vehicles.refetch()}
          rows={rows}
          filter={fleetFilter}
          onFilterChange={(f) => update({ fleet: f === "all" ? undefined : f })}
        />
      )}
    </div>
  );
}

export default function RidersPage() {
  return (
    <Suspense>
      <RidersPageContent />
    </Suspense>
  );
}
```

- [ ] **Step 4: Run the page tests**

Run: `npx vitest run "src/app/(staff)/riders/page.test.tsx"`
Expected: PASS. Notes if a case fails: the strip's buttons carry their count and caption in their accessible name, so match with a regex; `useSearchParamState` writes a merged patch, so a stage click from `""` produces exactly `/riders?stage=off`.

- [ ] **Step 5: Redirect the old live-map route and drop its nav item**

Replace `src/app/(staff)/riders/live-map/page.tsx` with:

```tsx
import { redirect } from "next/navigation";

// F15 R3b-2: the live map is now the Map tab on /riders. Old links and bookmarks land there.
export default function LiveMapRedirect({ searchParams }: { searchParams: { branch?: string | string[] } }) {
  const branch = typeof searchParams.branch === "string" ? searchParams.branch : undefined;
  const params = new URLSearchParams({ tab: "map" });
  if (branch) params.set("branch", branch);
  redirect(`/riders?${params.toString()}`);
}
```

In `src/lib/navigation/navItems.ts`:
- delete the line `{ href: "/riders/live-map", labelKey: "liveMap", permission: "rider.location.read", icon: MapPin, group: "operations" },`
- change `| "riders" | "liveMap" | "myDeliveries" | "cod" | "billing" | "auditLogs" | "admin";` to `| "riders" | "myDeliveries" | "cod" | "billing" | "auditLogs" | "admin";`
- remove `MapPin,` from the `lucide-react` import.

In `messages/en.json` delete the line `"liveMap": "Live Map",`; in `messages/ne.json` delete the line `"liveMap": "लाइभ नक्सा",`. Then confirm nothing else references them: `grep -rn "liveMap\|riders/live-map" src --include=*.ts --include=*.tsx` must show only `navModel.ts`/`navModel.test.ts`/`ConsoleBreadcrumb.test.tsx` (they use their own sample hrefs, unaffected) and the redirect page.

- [ ] **Step 6: Run the tests and type check**

Run: `npx vitest run "src/app/(staff)/riders" src/lib/navigation src/components/console src/features/riders` then `npx tsc --noEmit` and `npm run lint`
Expected: PASS; 0 type errors; lint clean.

- [ ] **Step 7: Commit**

```bash
git add src/Web
git commit -m "$(cat <<'EOF'
feat(web): riders page with day strip, Riders/Map/Fleet tabs; live map redirects to the Map tab

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 12: Branches page — type strip, search, live counts table

**Files:**
- Modify: `src/app/(staff)/branches/page.tsx` (rewrite), `src/app/(staff)/branches/page.test.tsx` (rewrite)

**Interfaces:**
- Consumes: `useBranches`, `useBranchesSummary` (`{ total, active, inactive, byType }`), `useBranchActivity`, `useDistrictNames`, `branchModel` (Task 7), `useBranchPanelStore.openCreate/openDetail`, `PipelineStrip`, `useSearchParamState`, `useFilterPage`, `toClientPage`.
- Produces: the `/branches` page reading `?type=<BranchType>`, `?q=`, `?inactive=1`.

- [ ] **Step 1: Write the failing page tests**

Replace `src/app/(staff)/branches/page.test.tsx` with:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { Branch, BranchActivity } from "@/features/branches/types";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/branches",
  useSearchParams: () => new URLSearchParams(search),
}));

function branch(overrides: Partial<Branch>): Branch {
  return {
    id: "b1", code: "KTM", name: "Kathmandu Hub", type: "Hub", provinceId: "p1", districtId: "d1", localLevelId: "l1",
    wardNumber: 1, addressLine: "New Road", phone: null, operatingHours: null, latitude: null, longitude: null,
    parentBranchId: null, deliveryZoneId: null, isActive: true, ...overrides,
  };
}

let list: { data?: Branch[]; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/branches/hooks/useBranches", () => ({ useBranches: () => list }));
let summary: { data?: unknown; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/branches/hooks/useBranchesSummary", () => ({ useBranchesSummary: () => summary }));
let activity: { data?: BranchActivity; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/branches/hooks/useBranchActivity", () => ({ useBranchActivity: () => activity }));
vi.mock("@/features/geography/hooks/useDistrictNames", () => ({
  useDistrictNames: () => new Map([["d1", "Kathmandu"], ["d2", "Kaski"]]),
}));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
const openDetail = vi.fn();
const openCreate = vi.fn();
vi.mock("@/features/branches/store/branchPanelStore", () => ({
  useBranchPanelStore: (selector: (s: unknown) => unknown) => selector({ panel: { mode: "closed" }, openCreate, openDetail }),
}));

import BranchesPage from "./page";

beforeEach(() => {
  replace.mockReset();
  openDetail.mockReset();
  search = "";
  granted = ["branch.create"];
  list = {
    data: [
      branch({}),
      branch({ id: "b2", code: "PKR", name: "Pokhara Branch", type: "Branch", provinceId: "p2", districtId: "d2" }),
      branch({ id: "b3", code: "OLD", name: "Old Depot", type: "Warehouse", isActive: false }),
    ],
    isLoading: false, isError: false, refetch: vi.fn(),
  };
  summary = {
    data: { total: 3, active: 2, inactive: 1, byType: { HeadOffice: 0, Hub: 1, Branch: 1, PickupPoint: 0, Warehouse: 1 } },
    isLoading: false, isError: false, refetch: vi.fn(),
  };
  activity = {
    data: { items: [{ branchId: "b1", onHand: 12, outForDelivery: 4, ridersOut: 3, openExceptions: 2 }] },
    isLoading: false, isError: false, refetch: vi.fn(),
  };
});

function rowFor(name: RegExp) {
  return screen.getByRole("button", { name }).closest("tr")!;
}

describe("BranchesPage", () => {
  it("shows the subtitle and the five-type strip from the summary", () => {
    render(<BranchesPage />);
    expect(screen.getByText("2 active · 1 inactive")).toBeInTheDocument();
    const strip = screen.getByRole("list", { name: "Branch types" });
    expect(within(strip).getByRole("button", { name: /Hub/ })).toHaveTextContent("1");
    expect(within(strip).getAllByRole("button")).toHaveLength(5);
  });

  it("filters by type from the strip", () => {
    const { unmount } = render(<BranchesPage />);
    fireEvent.click(within(screen.getByRole("list", { name: "Branch types" })).getByRole("button", { name: /^Branch/ }));
    expect(replace).toHaveBeenCalledWith("/branches?type=Branch", { scroll: false });
    unmount();

    search = "type=Branch";
    render(<BranchesPage />);
    expect(screen.getByRole("button", { name: /Pokhara Branch/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Kathmandu Hub/ })).toBeNull();
  });

  it("searches by code and shows inactive branches only when asked", () => {
    search = "q=pkr";
    const { unmount } = render(<BranchesPage />);
    expect(screen.queryByRole("button", { name: /Kathmandu Hub/ })).toBeNull();
    expect(screen.getByRole("button", { name: /Pokhara Branch/ })).toBeInTheDocument();
    unmount();

    search = "";
    const second = render(<BranchesPage />);
    expect(screen.queryByRole("button", { name: /Old Depot/ })).toBeNull();
    fireEvent.click(screen.getByRole("checkbox", { name: "Active only" }));
    expect(replace).toHaveBeenCalledWith("/branches?inactive=1", { scroll: false });
    second.unmount();

    search = "inactive=1";
    render(<BranchesPage />);
    expect(screen.getByRole("button", { name: /Old Depot/ })).toBeInTheDocument();
  });

  it("shows live counts, zeros for quiet branches, district names and worded exceptions", () => {
    render(<BranchesPage />);
    const ktm = within(rowFor(/Kathmandu Hub/));
    expect(ktm.getByText("12")).toBeInTheDocument();
    expect(ktm.getByText("4")).toBeInTheDocument();
    expect(ktm.getByText("3")).toBeInTheDocument();
    expect(ktm.getByText("2 open")).toBeInTheDocument();
    expect(ktm.getByText("Kathmandu")).toBeInTheDocument();
    const pkr = within(rowFor(/Pokhara Branch/));
    expect(pkr.getByText("None")).toBeInTheDocument();
    expect(pkr.getAllByText("0")).toHaveLength(3);
    expect(pkr.getByText("Kaski")).toBeInTheDocument();
  });

  it("shows dashes, never zeros, while activity loads, and a retry when it fails", () => {
    activity = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    const { unmount } = render(<BranchesPage />);
    const loading = within(rowFor(/Kathmandu Hub/));
    expect(loading.getAllByText("—")).toHaveLength(4);
    expect(loading.queryByText("0")).toBeNull();
    unmount();

    activity = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<BranchesPage />);
    expect(screen.getByText("Couldn't load live counts")).toBeInTheDocument();
    expect(within(rowFor(/Kathmandu Hub/)).getAllByText("—")).toHaveLength(4);
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(activity.refetch).toHaveBeenCalled();
  });

  it("opens the Sheet from a row and gates Add branch", () => {
    const { unmount } = render(<BranchesPage />);
    fireEvent.click(screen.getByRole("button", { name: /Kathmandu Hub/ }));
    expect(openDetail).toHaveBeenCalledWith("b1");
    fireEvent.click(screen.getByRole("button", { name: /Add branch/ }));
    expect(openCreate).toHaveBeenCalled();
    unmount();

    granted = [];
    render(<BranchesPage />);
    expect(screen.queryByRole("button", { name: /Add branch/ })).toBeNull();
  });

  it("never shows zeros in the strip or subtitle while the summary loads", () => {
    summary = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    render(<BranchesPage />);
    expect(screen.getByRole("status", { name: "Loading branch types" })).toBeInTheDocument();
    expect(screen.getByText("—")).toBeInTheDocument();
  });
});
```

Run: `npx vitest run "src/app/(staff)/branches/page.test.tsx"`
Expected: FAIL.

- [ ] **Step 2: Write the page**

Replace `src/app/(staff)/branches/page.tsx` with:

```tsx
"use client";

import { Suspense, type ReactNode } from "react";
import { Plus } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { PageHeader } from "@/components/page/PageHeader";
import { PipelineStrip, type PipelineStage } from "@/components/page/PipelineStrip";
import { ErrorState } from "@/components/states/ErrorState";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useBranchActivity } from "@/features/branches/hooks/useBranchActivity";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useBranchesSummary } from "@/features/branches/hooks/useBranchesSummary";
import { BRANCH_TYPES, branchTypeLabel, filterBranches, parseBranchType, zeroActivity } from "@/features/branches/lib/branchModel";
import { useBranchPanelStore } from "@/features/branches/store/branchPanelStore";
import type { Branch, BranchActivityRow } from "@/features/branches/types";
import { useDistrictNames } from "@/features/geography/hooks/useDistrictNames";
import { useFilterPage } from "@/lib/hooks/useFilterPage";
import { useSearchParamState } from "@/lib/hooks/useSearchParamState";
import { toClientPage } from "@/lib/paging/clientPage";

const PAGE_SIZE = 20;

function BranchesPageContent() {
  const { params, update } = useSearchParamState();
  const type = parseBranchType(params.get("type"));
  const q = params.get("q") ?? "";
  const showInactive = params.get("inactive") === "1";

  const list = useBranches();
  const summary = useBranchesSummary();
  const activity = useBranchActivity();
  const districtNames = useDistrictNames((list.data ?? []).map((b) => b.provinceId));
  const canCreate = usePermission("branch.create");
  const panel = useBranchPanelStore((s) => s.panel);
  const openCreate = useBranchPanelStore((s) => s.openCreate);
  const openDetail = useBranchPanelStore((s) => s.openDetail);
  const [page, setPage] = useFilterPage(`${type ?? ""}|${q}|${showInactive ? 1 : 0}`);

  // Activity rows are sparse for all-branches users: once loaded, a missing branch is all zeros.
  const activityById = activity.data ? new Map(activity.data.items.map((r) => [r.branchId, r])) : undefined;
  const shown = list.data && filterBranches(list.data, { type, q, showInactive });

  function live(branchId: string, pick: (row: BranchActivityRow) => number): ReactNode {
    if (!activityById) return <span className="text-sm text-muted-foreground">—</span>;
    return <span className="text-sm tabular-nums">{pick(activityById.get(branchId) ?? zeroActivity(branchId)).toLocaleString("en-US")}</span>;
  }

  const stages: PipelineStage[] = BRANCH_TYPES.map((t) => ({
    key: t.type,
    label: t.label,
    token: "status-created",
    count: summary.data ? summary.data.byType[t.type] ?? 0 : null,
  }));

  const columns: ColumnDef<Branch>[] = [
    {
      key: "branch",
      header: "Branch",
      render: (b) => (
        <button type="button" onClick={() => openDetail(b.id)} className="flex flex-col items-start gap-0.5 text-left">
          <span className="text-sm font-medium text-foreground hover:underline">{b.name}</span>
          <span className="font-mono text-xs text-muted-foreground">{b.code}</span>
        </button>
      ),
    },
    {
      key: "type",
      header: "Type",
      render: (b) => (
        <Badge variant="outline" className="whitespace-nowrap font-normal">
          {branchTypeLabel(b.type)}
        </Badge>
      ),
    },
    { key: "district", header: "District", render: (b) => <span className="text-sm">{districtNames.get(b.districtId) ?? "—"}</span> },
    { key: "onHand", header: "On hand", render: (b) => live(b.id, (r) => r.onHand) },
    { key: "outForDelivery", header: "Out for delivery", render: (b) => live(b.id, (r) => r.outForDelivery) },
    { key: "ridersOut", header: "Riders out", render: (b) => live(b.id, (r) => r.ridersOut) },
    {
      key: "exceptions",
      header: "Exceptions",
      render: (b) => {
        if (!activityById) return <span className="text-sm text-muted-foreground">—</span>;
        const open = activityById.get(b.id)?.openExceptions ?? 0;
        return open === 0 ? (
          <span className="text-sm text-muted-foreground">None</span>
        ) : (
          <span className="text-sm font-medium text-status-ndr">{`${open} open`}</span>
        );
      },
    },
  ];

  return (
    <div className="space-y-5">
      <PageHeader
        eyebrow="Network"
        title="Branches"
        description={summary.data ? `${summary.data.active} active · ${summary.data.inactive} inactive` : "—"}
        actions={
          canCreate && (
            <Button type="button" className="gap-1.5" onClick={() => openCreate()}>
              <Plus className="h-4 w-4" aria-hidden="true" />
              Add branch
            </Button>
          )
        }
      />

      <section aria-label="Branch type summary">
        {summary.isLoading && (
          <div role="status" aria-label="Loading branch types" className="grid grid-cols-2 gap-2.5 sm:grid-cols-5">
            {BRANCH_TYPES.map((t) => (
              <Skeleton key={t.type} className="h-20 rounded-lg" />
            ))}
          </div>
        )}
        {summary.isError && (
          <ErrorState title="Couldn't load branch counts" description="The type summary is unavailable right now." onRetry={() => summary.refetch()} />
        )}
        {summary.data && (
          <PipelineStrip
            ariaLabel="Branch types"
            stages={stages}
            selectedKey={type}
            onSelect={(key) => update({ type: key === type ? undefined : key })}
          />
        )}
      </section>

      <div className="flex flex-wrap items-center gap-3">
        <Input
          value={q}
          onChange={(e) => update({ q: e.target.value || undefined })}
          placeholder="Search code or name"
          aria-label="Search branches"
          className="w-64"
        />
        <div className="flex items-center gap-2">
          <Checkbox
            id="branches-active-only"
            checked={!showInactive}
            onCheckedChange={(checked) => update({ inactive: checked === true ? undefined : "1" })}
          />
          <Label htmlFor="branches-active-only" className="text-sm font-normal">Active only</Label>
        </div>
      </div>

      {activity.isError && (
        <ErrorState
          title="Couldn't load live counts"
          description="On hand, out for delivery, riders out and exceptions are unavailable right now."
          onRetry={() => activity.refetch()}
        />
      )}

      {list.isError ? (
        <ErrorState title="Couldn't load branches" description="Something went wrong fetching the branch list." onRetry={() => list.refetch()} />
      ) : (
        <DataTable
          columns={columns}
          data={shown && toClientPage(shown, page, PAGE_SIZE)}
          isLoading={list.isLoading}
          emptyTitle="No branches here"
          emptyDescription="No branch matches these filters. Clear a filter or add a branch."
          onPageChange={setPage}
          rowKey={(b) => b.id}
          selectedRowId={panel.mode === "detail" ? panel.branchId : undefined}
        />
      )}
    </div>
  );
}

export default function BranchesPage() {
  return (
    <Suspense>
      <BranchesPageContent />
    </Suspense>
  );
}
```

- [ ] **Step 3: Run the tests**

Run: `npx vitest run "src/app/(staff)/branches/page.test.tsx"` then `npx tsc --noEmit`
Expected: PASS; 0 type errors. (If `DataTable` renders rows without `<tr>`, change the test helper `rowFor` to the row element DataTable uses — read `src/components/data-table/DataTable.tsx`.)

- [ ] **Step 4: Commit**

```bash
git add src/Web/src
git commit -m "$(cat <<'EOF'
feat(web): branches page with type strip, search and live custody counts

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 13: Branch Sheet — header, right-now cards, location, zone footer

**Files:**
- Create: `src/features/branches/components/BranchRightNow.tsx`, `BranchLocationCard.tsx`, `BranchZoneControl.tsx` (+ `BranchZoneControl.test.tsx`)
- Modify: `src/features/branches/components/BranchDetailSheet.tsx` (rewrite) (+ new `BranchDetailSheet.test.tsx`)
- Delete: `src/features/branches/BranchRecord.tsx`, `src/features/branches/BranchRecord.test.tsx`

**Interfaces:**
- Consumes: `useBranch`, `useBranches`, `useBranchActivity({ enabled })`, `branchModel` hrefs and `zeroActivity`, `branchTypeLabel`, `branchCode` (Task 7 / existing); `useDistricts(provinceId, enabled)`, `useLocalLevels(districtId, enabled)`, `useDeliveryZones`, `useUpdateDeliveryZone`; `useBranchDashboard`, `BranchShipments`, `BranchExceptions` (kept sections); `useRiderLocations`, `useRiderBoard(branchId, { enabled })`, `LiveMap` (Task 8), `riderModel`.
- Produces: `BranchDetailSheet()` (no props; existing panel store), `BranchRightNow({ branchId: string; row?: BranchActivityRow; isError: boolean; onRetry: () => void })`, `BranchLocationCard({ branch: Branch; address: string })`, `BranchZoneControl({ branch: Branch })`.

- [ ] **Step 1: Write the failing tests**

Create `src/features/branches/components/BranchZoneControl.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import type { Branch } from "@/features/branches/types";

let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
vi.mock("@/features/geography/hooks/useDeliveryZones", () => ({ useDeliveryZones: () => ({ data: [{ id: "z1", name: "Valley" }] }) }));
vi.mock("@/features/branches/hooks/useUpdateDeliveryZone", () => ({
  useUpdateDeliveryZone: () => ({ mutate: vi.fn(), isPending: false, isError: false }),
}));

import { BranchZoneControl } from "./BranchZoneControl";

const branch = { id: "b1", deliveryZoneId: "z1" } as Branch;

describe("BranchZoneControl", () => {
  it("offers the zone picker with branch.update", () => {
    granted = ["branch.update"];
    render(<BranchZoneControl branch={branch} />);
    expect(screen.getByRole("combobox", { name: "Delivery zone" })).toBeEnabled();
  });

  it("is hidden without branch.update", () => {
    granted = [];
    render(<BranchZoneControl branch={branch} />);
    expect(screen.queryByRole("combobox", { name: "Delivery zone" })).toBeNull();
  });
});
```

Create `src/features/branches/components/BranchDetailSheet.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, within } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";
import type { Branch, BranchActivity } from "@/features/branches/types";
import { useBranchPanelStore } from "@/features/branches/store/branchPanelStore";

vi.mock("next/dynamic", () => ({
  default: () =>
    function MapStub({ pins, branch }: { pins: unknown[]; branch?: { label: string } }) {
      return <div>{`mini map ${branch?.label ?? ""} with ${pins.length} riders`}</div>;
    },
}));

const BRANCH: Branch = {
  id: "b1", code: "KTM", name: "Kathmandu Hub", type: "Hub", provinceId: "p1", districtId: "d1", localLevelId: "l1",
  wardNumber: 5, addressLine: "New Road", phone: "01-4444444", operatingHours: "9–6", latitude: 27.7, longitude: 85.32,
  parentBranchId: "b0", deliveryZoneId: "z1", isActive: true,
};

let branchQuery: { data?: Branch; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/branches/hooks/useBranch", () => ({ useBranch: () => branchQuery }));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b0", code: "HQ", name: "Head Office" }, BRANCH] }),
}));
let activity: { data?: BranchActivity; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/branches/hooks/useBranchActivity", () => ({ useBranchActivity: () => activity }));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
const useBranchDashboard = vi.fn();
vi.mock("@/features/reports/hooks/useBranchDashboard", () => ({ useBranchDashboard: (...a: unknown[]) => useBranchDashboard(...a) }));
vi.mock("@/features/geography/hooks/useGeography", () => ({
  useDistricts: () => ({ data: [{ id: "d1", provinceId: "p1", name: "Kathmandu" }] }),
  useLocalLevels: () => ({ data: [{ id: "l1", districtId: "d1", name: "Kathmandu Metropolitan City" }] }),
}));
vi.mock("@/features/geography/hooks/useDeliveryZones", () => ({ useDeliveryZones: () => ({ data: [{ id: "z1", name: "Valley" }] }) }));
vi.mock("@/features/branches/hooks/useUpdateDeliveryZone", () => ({
  useUpdateDeliveryZone: () => ({ mutate: vi.fn(), isPending: false, isError: false }),
}));
const useRiderLocations = vi.fn();
vi.mock("@/features/rider-tracking/hooks/useRiderLocations", () => ({ useRiderLocations: (...a: unknown[]) => useRiderLocations(...a) }));
vi.mock("@/features/riders/hooks/useRiderBoard", () => ({ useRiderBoard: () => ({ data: { items: [] } }) }));

import { BranchDetailSheet } from "./BranchDetailSheet";

beforeEach(() => {
  granted = [];
  branchQuery = { data: BRANCH, isLoading: false, isError: false, refetch: vi.fn() };
  activity = { data: { items: [{ branchId: "b1", onHand: 12, outForDelivery: 4, ridersOut: 3, openExceptions: 2 }] }, isLoading: false, isError: false, refetch: vi.fn() };
  useBranchDashboard.mockReset();
  useBranchDashboard.mockReturnValue({ data: undefined, isLoading: false, isError: false, refetch: vi.fn() });
  useRiderLocations.mockReset();
  useRiderLocations.mockReturnValue({
    data: [{ riderId: "r1", latitude: 27.71, longitude: 85.31, accuracyMeters: 5, recordedAt: new Date().toISOString(), receivedAt: new Date().toISOString() }],
  });
  useBranchPanelStore.setState({ panel: { mode: "detail", branchId: "b1" } });
});

describe("BranchDetailSheet", () => {
  it("shows the header with type, parent, code, status and facts", () => {
    renderWithIntl(<BranchDetailSheet />);
    expect(screen.getByText("BRANCH · HUB · PARENT HQ")).toBeInTheDocument();
    expect(screen.getByText("Kathmandu Hub")).toBeInTheDocument();
    expect(screen.getAllByText("KTM").length).toBeGreaterThan(0);
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("New Road, Ward 5, Kathmandu Metropolitan City, Kathmandu")).toBeInTheDocument();
    expect(screen.getByText("01-4444444")).toBeInTheDocument();
    expect(screen.getByText("Valley")).toBeInTheDocument();
    expect(screen.getByText("27.70000, 85.32000")).toBeInTheDocument();
    expect(screen.getByText("Edit details, staff roster, weekly hours")).toBeInTheDocument();
  });

  it("links the right-now cards to filtered lists, never claiming a list length", () => {
    renderWithIntl(<BranchDetailSheet />);
    const cards = within(screen.getByRole("region", { name: "Right now" }));
    expect(cards.getByRole("link", { name: /On hands*12/ })).toHaveAttribute(
      "href",
      "/shipments?branchId=b1&status=AtOriginBranch&status=Sorted&status=AtDestinationBranch&status=RtoInitiated&status=RtoAtOrigin",
    );
    expect(cards.getByRole("link", { name: /Out for deliverys*4/ })).toHaveAttribute("href", "/shipments?branchId=b1&status=OutForDelivery");
    expect(cards.getByRole("link", { name: /Riders outs*3/ })).toHaveAttribute("href", "/riders?branch=b1&stage=on");
    expect(cards.getByRole("link", { name: /Open exceptionss*2/ })).toHaveAttribute("href", "/ndrs");
    expect(cards.getAllByText("View shipments")).toHaveLength(2);
  });

  it("shows dashes while activity loads and zeros for a branch missing from loaded activity", () => {
    activity = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    const { unmount } = renderWithIntl(<BranchDetailSheet />);
    expect(within(screen.getByRole("region", { name: "Right now" })).getAllByText("—")).toHaveLength(4);
    unmount();

    activity = { data: { items: [] }, isLoading: false, isError: false, refetch: vi.fn() };
    renderWithIntl(<BranchDetailSheet />);
    expect(within(screen.getByRole("region", { name: "Right now" })).getAllByText("0")).toHaveLength(4);
  });

  it("shows the location map only with rider.location.read and coordinates", () => {
    const { unmount } = renderWithIntl(<BranchDetailSheet />);
    expect(screen.queryByText(/mini map/)).toBeNull();
    expect(useRiderLocations).toHaveBeenLastCalledWith(undefined);
    unmount();

    granted = ["rider.location.read"];
    renderWithIntl(<BranchDetailSheet />);
    expect(screen.getByText("mini map KTM with 1 riders")).toBeInTheDocument();
    expect(useRiderLocations).toHaveBeenLastCalledWith("b1");
  });

  it("loads the shipments and exceptions sections only with report.read", () => {
    const { unmount } = renderWithIntl(<BranchDetailSheet />);
    expect(useBranchDashboard).toHaveBeenLastCalledWith("");
    unmount();

    granted = ["report.read"];
    renderWithIntl(<BranchDetailSheet />);
    expect(useBranchDashboard).toHaveBeenLastCalledWith("b1");
  });

  it("shows an error state when the branch cannot be read", () => {
    branchQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    renderWithIntl(<BranchDetailSheet />);
    expect(screen.getByText("Couldn't load this branch")).toBeInTheDocument();
  });
});
```

Run: `npx vitest run src/features/branches/components/BranchZoneControl.test.tsx src/features/branches/components/BranchDetailSheet.test.tsx`
Expected: FAIL.

- [ ] **Step 2: Write the three components**

Create `src/features/branches/components/BranchZoneControl.tsx`:

```tsx
"use client";

import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useUpdateDeliveryZone } from "@/features/branches/hooks/useUpdateDeliveryZone";
import type { Branch } from "@/features/branches/types";
import { useDeliveryZones } from "@/features/geography/hooks/useDeliveryZones";

/** Branch Sheet footer: the existing delivery-zone action (PUT /branches/{id}/delivery-zone). */
export function BranchZoneControl({ branch }: { branch: Branch }) {
  const { data: zones } = useDeliveryZones();
  const updateZone = useUpdateDeliveryZone(branch.id);
  const canUpdate = usePermission("branch.update", { branchId: branch.id });
  if (!canUpdate) return null;

  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor="branch-delivery-zone" className="text-xs text-muted-foreground">Change delivery zone</Label>
      <Select
        value={branch.deliveryZoneId ?? undefined}
        onValueChange={(zoneId) => updateZone.mutate(zoneId)}
        disabled={updateZone.isPending}
      >
        <SelectTrigger id="branch-delivery-zone" aria-label="Delivery zone" className="w-56">
          <SelectValue placeholder="Not assigned" />
        </SelectTrigger>
        <SelectContent>
          {zones?.map((z) => (
            <SelectItem key={z.id} value={z.id}>{z.name}</SelectItem>
          ))}
        </SelectContent>
      </Select>
      {updateZone.isError && <p role="alert" className="text-xs text-destructive">Couldn&apos;t change the delivery zone. Try again.</p>}
    </div>
  );
}
```

Create `src/features/branches/components/BranchRightNow.tsx`:

```tsx
import Link from "next/link";
import { ErrorState } from "@/components/states/ErrorState";
import { onHandShipmentsHref, outForDeliveryHref, ridersOutHref } from "@/features/branches/lib/branchModel";
import type { BranchActivityRow } from "@/features/branches/types";

const CARDS: { label: string; pick: (r: BranchActivityRow) => number; href: (id: string) => string; linkText: string }[] = [
  { label: "On hand", pick: (r) => r.onHand, href: onHandShipmentsHref, linkText: "View shipments" },
  { label: "Out for delivery", pick: (r) => r.outForDelivery, href: outForDeliveryHref, linkText: "View shipments" },
  { label: "Riders out", pick: (r) => r.ridersOut, href: ridersOutHref, linkText: "View riders" },
  { label: "Open exceptions", pick: (r) => r.openExceptions, href: () => "/ndrs", linkText: "View NDR queue" },
];

/** `row` undefined = activity not loaded (values "—"); a loaded branch with no row passes zeroActivity. */
export function BranchRightNow({ branchId, row, isError, onRetry }: { branchId: string; row?: BranchActivityRow; isError: boolean; onRetry: () => void }) {
  return (
    <section aria-label="Right now" className="space-y-2">
      <h3 className="text-sm font-semibold text-foreground">Right now</h3>
      {isError ? (
        <ErrorState title="Couldn't load live counts" description="This branch's live counts are unavailable right now." onRetry={onRetry} />
      ) : (
        <div className="grid grid-cols-2 gap-2.5">
          {CARDS.map((card) => (
            <Link
              key={card.label}
              href={card.href(branchId)}
              className="flex flex-col gap-1 rounded-lg border border-border bg-background px-3.5 py-3 transition-colors hover:bg-accent"
            >
              <span className="text-xs text-muted-foreground">{card.label}</span>
              <span className="text-2xl font-semibold tabular-nums tracking-tight text-foreground">
                {row ? card.pick(row).toLocaleString("en-US") : "—"}
              </span>
              <span className="text-xs font-medium text-primary">{card.linkText}</span>
            </Link>
          ))}
        </div>
      )}
    </section>
  );
}
```

Create `src/features/branches/components/BranchLocationCard.tsx`:

```tsx
"use client";

import dynamic from "next/dynamic";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { usePermission } from "@/features/auth/hooks/usePermission";
import type { Branch } from "@/features/branches/types";
import { useRiderLocations } from "@/features/rider-tracking/hooks/useRiderLocations";
import type { MapRiderPin } from "@/features/rider-tracking/lib/mapPins";
import { useRiderBoard } from "@/features/riders/hooks/useRiderBoard";
import { isLocationStale, minutesSince, riderInitials } from "@/features/riders/lib/riderModel";

const LiveMap = dynamic(() => import("@/features/rider-tracking/components/LiveMap").then((mod) => mod.LiveMap), {
  ssr: false,
  loading: () => <Skeleton className="h-48 w-full rounded-lg" />,
});

export function BranchLocationCard({ branch, address }: { branch: Branch; address: string }) {
  const canSee = usePermission("rider.location.read");
  const hasCoords = branch.latitude !== null && branch.longitude !== null;
  const showMap = canSee && hasCoords;
  const locations = useRiderLocations(showMap ? branch.id : undefined);
  const board = useRiderBoard(branch.id, { enabled: showMap });

  const now = new Date();
  const names = new Map((board.data?.items ?? []).map((r) => [r.riderId, r.fullName]));
  const pins: MapRiderPin[] = (locations.data ?? []).map((l) => {
    const name = names.get(l.riderId) ?? "Rider";
    return {
      riderId: l.riderId,
      latitude: l.latitude,
      longitude: l.longitude,
      initials: riderInitials(name),
      label: name,
      stale: isLocationStale(l.receivedAt, now),
      minutesAgo: minutesSince(l.receivedAt, now),
      popup: <span className="text-sm">{name}</span>,
    };
  });

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-semibold">Location</CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        {showMap && (
          <LiveMap
            className="h-48"
            pins={pins}
            branch={{ latitude: branch.latitude as number, longitude: branch.longitude as number, label: branch.code?.trim() ? branch.code : branch.name }}
          />
        )}
        <p className="text-sm text-muted-foreground">{address}</p>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 3: Rewrite the Sheet**

Replace `src/features/branches/components/BranchDetailSheet.tsx` with:

```tsx
"use client";

import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { BranchExceptions } from "@/features/branches/components/BranchExceptions";
import { BranchLocationCard } from "@/features/branches/components/BranchLocationCard";
import { BranchRightNow } from "@/features/branches/components/BranchRightNow";
import { BranchShipments } from "@/features/branches/components/BranchShipments";
import { BranchZoneControl } from "@/features/branches/components/BranchZoneControl";
import { useBranch } from "@/features/branches/hooks/useBranch";
import { useBranchActivity } from "@/features/branches/hooks/useBranchActivity";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { branchCode } from "@/features/branches/lib/branchCode";
import { branchTypeLabel, zeroActivity } from "@/features/branches/lib/branchModel";
import { useBranchPanelStore } from "@/features/branches/store/branchPanelStore";
import { useDeliveryZones } from "@/features/geography/hooks/useDeliveryZones";
import { useDistricts, useLocalLevels } from "@/features/geography/hooks/useGeography";
import { useBranchDashboard } from "@/features/reports/hooks/useBranchDashboard";

export function BranchDetailSheet() {
  const panel = useBranchPanelStore((s) => s.panel);
  const close = useBranchPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const branchId = panel.mode === "detail" ? panel.branchId : "";

  const { data: branch, isLoading, isError, refetch } = useBranch(branchId);
  const { data: branches } = useBranches();
  const activity = useBranchActivity({ enabled: isOpen });
  const canReports = usePermission("report.read");
  // Shipments/Exceptions sections read the branch dashboard, which needs report.read.
  const dashboard = useBranchDashboard(canReports ? branchId : "");
  const districts = useDistricts(branch?.provinceId ?? null, isOpen);
  const localLevels = useLocalLevels(branch?.districtId ?? null, isOpen);
  const { data: zones } = useDeliveryZones();

  const districtName = branch ? districts.data?.find((d) => d.id === branch.districtId)?.name : undefined;
  const localLevelName = branch ? localLevels.data?.find((l) => l.id === branch.localLevelId)?.name : undefined;
  const address = branch
    ? [branch.addressLine, `Ward ${branch.wardNumber}`, localLevelName, districtName].filter(Boolean).join(", ")
    : "";
  const zoneName = branch?.deliveryZoneId ? zones?.find((z) => z.id === branch.deliveryZoneId)?.name ?? "—" : "Not assigned";
  const coordinates =
    branch && branch.latitude !== null && branch.longitude !== null ? `${branch.latitude.toFixed(5)}, ${branch.longitude.toFixed(5)}` : "—";
  const activityRow = activity.data ? activity.data.items.find((r) => r.branchId === branchId) ?? zeroActivity(branchId) : undefined;

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <div className="flex flex-col gap-6">
          {!branch && <SheetTitle className="sr-only">Branch</SheetTitle>}
          {isLoading && <DetailSkeleton fieldRows={2} />}
          {isError && (
            <ErrorState title="Couldn't load this branch" description="It may not exist, or you may not have access to it." onRetry={() => refetch()} />
          )}
          {branch && (
            <>
              <SheetHeaderBlock
                eyebrow={["BRANCH", branchTypeLabel(branch.type).toUpperCase(), branch.parentBranchId ? `PARENT ${branchCode(branches, branch.parentBranchId)}` : null]
                  .filter(Boolean)
                  .join(" · ")}
                title={branch.name}
                status={
                  <span className="flex items-center gap-2">
                    <span className="font-mono text-sm text-muted-foreground">{branch.code}</span>
                    <Badge variant={branch.isActive ? "outline" : "secondary"} className="font-normal">
                      {branch.isActive ? "Active" : "Inactive"}
                    </Badge>
                  </span>
                }
                facts={[
                  { label: "Address", value: address },
                  { label: "Phone", value: branch.phone ?? "—" },
                  { label: "Hours", value: branch.operatingHours ?? "—" },
                  { label: "Delivery zone", value: zoneName },
                  { label: "Coordinates", value: coordinates },
                ]}
              />
              <BranchRightNow branchId={branch.id} row={activityRow} isError={activity.isError} onRetry={() => activity.refetch()} />
              <BranchLocationCard branch={branch} address={address} />
              {canReports && dashboard.isLoading && (
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-4" role="status" aria-label="Loading branch dashboard">
                  {Array.from({ length: 4 }).map((_, i) => (
                    <Skeleton key={i} className="h-16 w-full rounded-lg" />
                  ))}
                </div>
              )}
              {canReports && dashboard.isError && (
                <ErrorState title="Couldn't load this branch's dashboard" description="Something went wrong fetching today's activity." onRetry={() => dashboard.refetch()} />
              )}
              {canReports && dashboard.data && (
                <>
                  <BranchShipments
                    data={{
                      shipmentCounts: dashboard.data.shipmentCounts,
                      manifestCounts: dashboard.data.manifestCounts,
                      todaysDeliveries: dashboard.data.todaysDeliveries,
                    }}
                  />
                  <BranchExceptions
                    data={{
                      missingShipmentExceptions: dashboard.data.missingShipmentExceptions,
                      openNdrCount: dashboard.data.openNdrCount,
                      riders: dashboard.data.riders,
                      codPendingFinanceVerification: dashboard.data.codPendingFinanceVerification,
                    }}
                  />
                </>
              )}
              <ComingSoonSlot
                title="Edit details, staff roster, weekly hours"
                description="Editing a branch, its staff roster and weekly opening hours will be available here."
              />
              <div className="border-t border-border pt-4">
                <BranchZoneControl branch={branch} />
              </div>
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 4: Delete `BranchRecord`**

Run: `grep -rn "BranchRecord" src` — it must list only `src/features/branches/BranchRecord.tsx` and `BranchRecord.test.tsx`. Then delete both files (`git rm src/Web/src/features/branches/BranchRecord.tsx src/Web/src/features/branches/BranchRecord.test.tsx` from the repo root). Its delivery-zone `Select` now lives in `BranchZoneControl`; its read-only fields are in the Sheet header facts.

- [ ] **Step 5: Run the tests**

Run: `npx vitest run src/features/branches "src/app/(staff)/branches"` then `npx tsc --noEmit` and `npm run lint`
Expected: PASS; 0 type errors; lint clean.

- [ ] **Step 6: Commit**

```bash
git add -A src/Web/src/features/branches
git commit -m "$(cat <<'EOF'
feat(web): branch Sheet with right-now counts, location map and zone footer

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 14: Documentation, full verification, live check

**Files:**
- Modify: `docs/API-SPEC.md` (new §6.6–§6.7 after §6.5)
- Modify: `docs/FRONTEND-ROADMAP.md` (R3b-2 row + follow-ups)

- [ ] **Step 1: API-SPEC**

Insert after the end of §6.5 (directly before `## 7. International Shipments API (I3, extended I4)`):

````markdown
### 6.6 Riders board and rider reads (staff)

```http
GET /api/v1/riders/board?branchId=   # one row per rider for today
GET /api/v1/riders/{id}              # single rider
GET /api/v1/riders/{id}/today        # rider Sheet: board row + today's tasks
```

All require `rider.read` and are scoped to riders whose `BaseBranchId` is in the caller's
`rider.read` branch scope. **`GET /riders/{id}` is branch-scoped** (F15 R3b-2 fix): a rider outside
scope returns `404`, the same as an unknown id. `/today` follows the same rule.

- `board` takes an optional `branchId` that narrows within scope (outside scope → empty `items`).
  Inactive riders are included with `isActive: false`. Rows are ordered on run, run done, off duty,
  then by name. A fixed number of queries per request.
- Row: `{ riderId, userId, fullName, baseBranchId, roleType, isActive, vehiclePlate, run: { id,
  status: "Active"|"Completed" } | null, tasksAssigned, tasksCompleted, tasksFailed, lastPingAt }`.
  Counts are today's run only, where today is the **UTC** date. `lastPingAt` is the latest
  server-received ping today, else `null`. `fullName` falls back to `"Rider"`.
- `/today`: `{ rider: <row>, tasks: [{ taskId, shipmentId, awb, type, sequence, status }] }`,
  ordered by `sequence`; AWBs come from one batched lookup (`""` if missing).

### 6.7 Branch activity (staff)

`GET /api/v1/branches/activity` — `branch.read`. Live per-branch counts for the branches page:
`{ items: [{ branchId, onHand, outForDelivery, ridersOut, openExceptions }] }` ordered by `branchId`.

- `onHand`: parcels whose `CurrentBranchId` is the branch and status is `AtOriginBranch`, `Sorted`,
  `AtDestinationBranch`, `RtoInitiated` or `RtoAtOrigin`.
- `outForDelivery`: parcels `OutForDelivery` by **destination** branch (`CurrentBranchId` is null
  while a rider holds a parcel).
- `ridersOut`: riders (by base branch) with an `Active` run dated today (UTC).
- `openExceptions`: NDRs not `Resolved`, by the shipment's destination branch (same rule as the NDR
  queue and the branch dashboard).
- A branch-scoped caller gets one row per branch in scope, zeros included. An all-branches caller
  gets rows only for branches with activity; a missing branch has all zeros.
````

- [ ] **Step 2: FRONTEND-ROADMAP**

Replace the row

```markdown
| R3b-2 — People & places | Riders + vehicles, live map, branches (artboards first) | Planned |
```

with

```markdown
| R3b-2 — People & places | Riders (day strip, Riders/Map/Fleet tabs, rider Sheet), branches (type strip, live counts, branch Sheet) | Done — spec `docs/superpowers/specs/2026-10-15-f15-r3b2-people-and-places-design.md` |
```

After the "Backend follow-ups from R3b-1" list (before `## Later`), add:

```markdown
**R3b-2 (2026-11-16):** `GET /riders/{id}` is now branch-scoped (404 out of scope); new
`GET /riders/board`, `GET /riders/{id}/today` and `GET /branches/activity`; rider names resolved
server-side through a read-only Identity lookup. The live map became the Map tab on `/riders`
(`/riders/live-map` redirects) and vehicles the Fleet tab. Pages follow the code where the spec
differed: out-for-delivery counts by destination branch (a rider-held parcel has no current
branch), `lastPingAt` is today's server-received time, and activity rows are sparse for
all-branches users.

**Backend follow-ups from R3b-2:**
1. Organisation-local "today" (Nepal time) for runs, pings, the board and riders out.
2. Reassign or retire a vehicle; edit rider details; activate or deactivate a rider.
3. Edit branch details, staff roster and weekly operating hours.
4. Rider attendance and 30-day delivery history.
5. Rider phone on the rider read, for click-to-call from the Sheet.
6. Batch the per-rider latest-ping loop in `GetBranchLocationsAsync`.
7. Close a delivery run: nothing sets `DeliveryRunStatus.Completed`, so "Run done" always reads 0.
8. Branch-scope `GET /vehicles` and `GET /branches/{id}` (both return any row in the tenant).
9. A tenant-wide branch id list for `GET /branches/activity`, so all-branches callers get zero rows too.
```

- [ ] **Step 3: Full verification**

Run each from the repo root unless noted, and confirm the expected result:

```bash
dotnet build                                                            # 0 errors
dotnet test tests/Identity.Tests/Identity.Tests.csproj                  # PASS
dotnet test tests/Riders.Tests/Riders.Tests.csproj                      # PASS
dotnet test tests/Shipments.Tests/Shipments.Tests.csproj                # PASS
dotnet test tests/Ndr.Tests/Ndr.Tests.csproj                            # PASS
dotnet test tests/Reports.Tests/Reports.Tests.csproj                    # PASS
dotnet test tests/Manifests.Tests/Manifests.Tests.csproj                # PASS
dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~RidersEndpointsTests|FullyQualifiedName~ReportsEndpointsTests|FullyQualifiedName~BranchesEndpointsTests|FullyQualifiedName~TenantIsolationTests|FullyQualifiedName~BranchAuthorizationEndpointTests"   # PASS
cd src/Web
npx tsc --noEmit        # 0 errors
npm run lint            # clean
npm test                # all pass
npm run build           # succeeds (retry once if next/font fails on a Google Fonts network flake)
```

If any fails, fix it in the task that owns the code before continuing.

- [ ] **Step 4: Live check in Chrome**

Start the API (`dotnet run --project src/Api`) and web (`npm run dev` in `src/Web`; first make sure no stale `next dev` holds :3000 — `netstat -ano | grep :3000`) against `courier-postgres-1`. The user signs in to a demo org; then check:
1. `/riders`: strip counts match the table; a rider with no ping today shows "No ping today"; stage click filters and writes `?stage=`; Cash held column present for a finance-capable user only.
2. Map tab with a branch picked: initials pins, a stale pin is dashed with "N min", the popup's "Open rider" opens the rider Sheet; `/riders/live-map` lands on the Map tab.
3. Fleet tab: Unassigned chip filters; Add vehicle opens the existing Sheet.
4. Rider Sheet: "‹done› of ‹assigned› done", task AWB opens the shipment Sheet, location and cash cards follow permissions; a branch-scoped user cannot open another branch's rider by id (error state, not data).
5. `/branches`: type strip filters; search by code; live columns show "—" while loading then numbers; the branch Sheet's right-now links land on filtered lists.
6. Light and dark mode, and a 390 px wide window (no horizontal page scroll).

Note anything off and fix it before the final commit.

- [ ] **Step 5: Commit**

```bash
git add docs/API-SPEC.md docs/FRONTEND-ROADMAP.md
git commit -m "$(cat <<'EOF'
docs: R3b-2 rider board, rider today, branch activity reads; roadmap row and follow-ups

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```
