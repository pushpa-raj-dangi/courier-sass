# F15 R3b-1 — Parcel Movement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the staff Manifests, NDR queue and Returns pages (and their Sheets) around the approved "Network: parcel movement" artboards, backed by a small read-only backend slice and one branch-scope security fix.

**Architecture:** Backend adds reads and filters inside the existing module services — `ManifestService` (branch-scoped single reads, `direction`, `receivedCount`, `/shipments`, batched AWB lookup through `IShipmentReportingLookupService`), `RiderService` (delivery attempts read) and `NdrService` (`assigned`/`reason` filters). Web adds pure model files (`features/*/lib/*Model.ts`, `lib/dates/age.ts`, `lib/paging/clientPage.ts`), thin TanStack hooks and Next proxy routes, extends `PipelineStrip` with select/highlight, and rewrites the three pages and Sheets.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core (InMemory unit tests + Postgres API tests), xUnit; Next.js 14 App Router, TypeScript, TanStack Query, Zustand, React Hook Form + Zod 4, shadcn/ui, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-10-14-f15-r3b1-parcel-movement-design.md`

## Global Constraints

- No migrations, no new entities, no new write endpoints, no change to existing write behaviour, no new npm or NuGet dependencies.
- Tenant isolation stays on the existing global query filters. Branch scope comes from `IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, "<permission>")` (null = all branches). Filters only narrow within scope.
- Out-of-scope single reads return **404**, never 403 and never data (manifest single/missing/shipments, delivery attempts).
- `GET /api/v1/shipments/{shipmentId}/delivery-attempts` is staff-only: merchant callers get **403**.
- Validation errors use each module's existing 400 contract: `MANIFEST_VALIDATION_FAILED`, `NDR_VALIDATION_FAILED`.
- Every number shown is server data; loading and error states never render `0` — skeletons, `ErrorState`, or "—".
- Data the API does not serve renders `ComingSoonSlot` (`@/components/page/ComingSoonSlot`) or a disabled control with a "Needs backend" tooltip. Never mock values.
- Detail views are right-side Sheets driven by the existing Zustand panel stores (`useManifestPanelStore`, `useNdrPanelStore`, `useReturnShipmentPanelStore`, `useShipmentPanelStore`) — never routed pages.
- shadcn/ui components from `@/components/ui/*` only. On-scale type classes only (`text-2xs`, `text-xs`, `text-sm`, `text-base`, `text-lg`, `text-xl`, `text-2xl`) — no arbitrary `text-[13px]`.
- Status never by colour alone: badges carry labels, stepper states carry icon + text, overdue ages carry their own text.
- Permissions (never role names): manifests page `manifest.read`; New manifest `manifest.create`; Receive link `manifest.receive`; Seal `manifest.seal`; Dispatch `manifest.dispatch`; Close `manifest.close`; NDR page `ndr.read`; follow-up `ndr.manage`; resolve `ndr.resolve`; Returns `rto.read`; hand back `rto.manage`. Hidden when missing.
- Web checks (run in `src/web`): `npm run build`, `npm run lint`, `npm test` pass; `npx tsc --noEmit` reports **0 errors** (delete a stale `tsconfig.tsbuildinfo` if old errors persist). Backend: `dotnet build` and the listed `dotnet test` projects pass. API tests need `courier-postgres-1` (port 5434), `courier-redis-1`, `courier-rabbitmq-1` running — `docker start` them; never `docker compose up` a second Postgres. `courier-mailhog-1` no longer exists, so run Api.Tests with the `--filter` shown in each task.
- Never assert on ICU month text in tests; compute expected strings with the same formatter or assert on non-date text.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` verbatim.

### Spec reconciliations (code wins; already folded into the tasks)

These are places where the spec read differently from the code. The plan follows the code because the slice may not change write behaviour.

1. **NDR follow-up status.** `NdrService.UpdateAsync` accepts only one legal forward move (`Open→Contacting`, `Contacting→CustomerConfirmed`, `CustomerConfirmed→ReattemptScheduled|RtoRequested`), with no same-status save. The follow-up form's Status `Select` therefore lists only the legal next statuses, and the form is shown only for `Open`, `Contacting` and `CustomerConfirmed`. In other states the recorded follow-up shows read-only.
2. **NDR resolve choices.** `ResolveAsync` allows `Reattempt` only from `ReattemptScheduled`, `Rto` only from `RtoRequested`, and `Other` from any open state. Unavailable radio cards are disabled and say why.
3. **Return hand-back.** `ConfirmReturnShipmentAsync` requires the original shipment to be **`RtoAtOrigin`** (it moves it to `RtoDelivered`). The hand-back card shows for `RtoAtOrigin` + not confirmed, not for `RtoDelivered`.
4. **No toast library exists.** Success shows an inline `role="status"` line in the Sheet.
5. **Seal and Dispatch stay inside their sections.** They submit form fields (scan list, plate, driver), so they gain permission props instead of moving into the footer. The footer carries Close (InTransit/Received).
6. **Driver ids are `Rider.Id`.** Names use `usePeopleNames().riderName`. Delivery-attempt `riderId` is also `Rider.Id`.
7. **Missing-parcel time text** reads "Scanned onto this manifest ‹time›" (the row's `scannedAt` is the scan-on time, not seal time).
8. **"‹N› open" in the Manifests subtitle** counts non-Closed manifests in the loaded list (the current tab/branch view). The summary endpoint is not branch-filtered (follow-up 8).

## Review Focus

1. **A guessed manifest id from another branch** → 404 on `/manifests/{id}`, `/missing-shipments` and `/shipments` for a branch-scoped user (no data, no 403 that confirms existence). Pinned in Task 1 (unit + API).
2. **`direction` with a `branchId` outside the caller's scope** → empty list, never manifests from other branches. Pinned in Task 2 (unit).
3. **NDR "Assigned to me" when the caller owns none** → empty page with `totalCount` 0, not the unfiltered queue. Pinned in Task 4 (unit).
4. **A manifest whose parcel AWB lookup misses, or with 0 parcels** → reconciliation shows the short shipment id / "0 of 0 verified" with a 0 % bar, no `NaN`, no crash. Pinned in Task 8.
5. **An `Open` NDR** → Reattempt and Return-to-origin choices are disabled with a reason; only "Resolve other" submits. Pinned in Task 10.

## File Map

**Backend**
- `src/Modules/Manifests/Application/ManifestDtos.cs` — `ManifestResponse.ReceivedCount`.
- `src/Modules/Manifests/Application/ManifestService.cs` — new ctor dependency `IShipmentReportingLookupService`; `ToResponse(manifest, receivedCount)`, `ToResponseAsync`.
- `src/Modules/Manifests/Application/ManifestService.Reads.cs` — scoped `GetByIdAsync`/`GetMissingShipmentsAsync`, new `ListShipmentsAsync`, `direction` + grouped `receivedCount` in `ListAsync`, `FindReadableAsync`.
- `src/Modules/Manifests/Application/ManifestService.Scanning.cs`, `ManifestService.Transitions.cs` — return `ToResponseAsync`.
- `src/Modules/Manifests/Presentation/ManifestsEndpoints.cs` — acting user on single reads, `/shipments`, `direction`, list validation.
- `src/Modules/Riders/Application/RiderDtos.cs`, new `RiderService.AttemptReads.cs`, `src/Modules/Riders/Presentation/RidersEndpoints.cs`.
- `src/Modules/Ndr/Application/NdrService.Reads.cs`, `src/Modules/Ndr/Presentation/NdrEndpoints.cs`.
- Tests: `tests/Manifests.Tests/{FakeShipmentReportingLookup.cs (new), ManifestServiceReadsTests.cs (new), ManifestServiceListTests.cs, ManifestServicePurposeTests.cs, ManifestServiceScanningTests.cs, ManifestServiceTransitionsTests.cs}`, `tests/Riders.Tests/RiderServiceDeliveryAttemptReadsTests.cs` (new), `tests/Ndr.Tests/NdrServiceListTests.cs`, `tests/Api.Tests/{ManifestsEndpointsTests.cs, NdrEndpointsTests.cs}`.

**Web (`src/web/src`)**
- Shared: `lib/dates/age.ts` (new), `lib/hooks/useFilterPage.ts` (moved from `features/cod/hooks`), `lib/hooks/useSearchParamState.ts` (new), `lib/paging/clientPage.ts` (new), `features/branches/lib/branchCode.ts` (new), `components/page/PipelineStrip.tsx` (select + highlight), `features/ndr/components/NdrRtoSwitch.tsx` (new).
- Manifests: `features/manifests/{types.ts, lib/manifestModel.ts (new), hooks/useManifests.ts, hooks/useManifestShipments.ts (new), store/manifestPanelStore.ts}`, components `ManifestStepper.tsx`, `ManifestReconciliation.tsx`, `ManifestCloseAction.tsx` (new), `ManifestDetailSheet.tsx`, `ManifestDraftSection.tsx`, `ManifestSealedSection.tsx`; delete `ManifestReceivedSection.tsx` (+ test); `app/(staff)/manifests/page.tsx`; proxy `app/api/manifests/[id]/shipments/route.ts`.
- NDR: `features/ndr/{types.ts, schema.ts (new), lib/ndrModel.ts (new), lib/returnModel.ts (new), hooks/useNdrs.ts, hooks/useDeliveryAttempts.ts (new)}`, components `NdrAttemptsCard.tsx`, `NdrFollowUpCard.tsx`, `NdrResolveCard.tsx` (new), `NdrDetailSheet.tsx`, `ReturnShipmentDetailSheet.tsx`; delete `NdrUpdateSection.tsx` (+ test), `NdrReattemptSection.tsx`, `NdrRtoSection.tsx` (+ test), `NdrResolveOtherAction.tsx`; `app/(staff)/ndrs/page.tsx`, `app/(staff)/return-shipments/page.tsx`; proxy `app/api/shipments/[awb]/delivery-attempts/route.ts`.
- COD (import moves only): `features/cod/lib/codFormat.ts`, `features/cod/components/{RiderDepositsTab,CollectionsTab,BranchSettlementsTab,AdjustmentsTab}.tsx`, `features/reports/components/dashboard/NeedsActionCard.tsx`.

**Docs:** `docs/API-SPEC.md` §6.3–6.5, `docs/FRONTEND-ROADMAP.md` (R3b row split + R3b-1 follow-ups), `DESIGN.md` (PipelineStrip highlight note).

---

### Task 1: Manifests — branch-scoped single reads, batched AWB lookup, parcel list (backend)

**Files:**
- Create: `tests/Manifests.Tests/FakeShipmentReportingLookup.cs`
- Create: `tests/Manifests.Tests/ManifestServiceReadsTests.cs`
- Modify: `src/Modules/Manifests/Application/ManifestService.cs`
- Modify: `src/Modules/Manifests/Application/ManifestService.Reads.cs`
- Modify: `src/Modules/Manifests/Presentation/ManifestsEndpoints.cs`
- Modify: `tests/Manifests.Tests/ManifestServiceListTests.cs`, `ManifestServicePurposeTests.cs`, `ManifestServiceScanningTests.cs`, `ManifestServiceTransitionsTests.cs` (constructor call sites)
- Modify: `tests/Api.Tests/ManifestsEndpointsTests.cs`

**Interfaces:**
- Produces: `ManifestService(DbContext, ICurrentTenantProvider, IAuditLogger, IShipmentTransitionService, IBranchAuthorizationChecker, IBranchLookupService, IShipmentReportingLookupService)`; `Task<ManifestResponse?> GetByIdAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken)`; `Task<List<MissingShipmentResponse>?> GetMissingShipmentsAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken)`; `Task<List<ManifestShipmentResponse>?> ListShipmentsAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken)`; private `Task<Manifest?> FindReadableAsync(Guid, ClaimsPrincipal, CancellationToken)`. HTTP: `GET /api/v1/manifests/{id}/shipments` → `ManifestShipmentResponse[]` (`id, shipmentId, awb, scannedAt, receivedAt`), ordered `scannedAt` ascending. Test helper `FakeShipmentReportingLookup` with `Dictionary<Guid,string> Awbs` and `int AwbLookupCalls`.

- [ ] **Step 1: Add the shared test fake**

Create `tests/Manifests.Tests/FakeShipmentReportingLookup.cs`:

```csharp
using Courier.Shipments.Application;
using Courier.Shipments.Domain;

namespace Courier.Manifests.Tests;

// Shared, not file-local: every ManifestService construction in this project needs one. Manifests
// only calls the AWB lookup; the reporting reads throw so an accidental call fails loudly.
class FakeShipmentReportingLookup : IShipmentReportingLookupService
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
```

If `IShipmentReportingLookupService` has gained members since this plan was written, the build will say so; implement each extra member as `=> throw new NotSupportedException();`.

- [ ] **Step 2: Write the failing unit tests**

Create `tests/Manifests.Tests/ManifestServiceReadsTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Manifests.Application;
using Courier.Manifests.Domain;
using Courier.Manifests.Infrastructure;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Manifests.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class NotSupportedBranchLookup : IBranchLookupService
{
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class ScopedBranchesChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid>? _accessible;
    public ScopedBranchesChecker(IReadOnlySet<Guid>? accessible) => _accessible = accessible;
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessible);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessible is null || _accessible.Contains(branchId));
}

// Reads no longer touch the transition service (AWBs come from the batched reporting lookup), so
// every member throws.
file class NotSupportedShipmentTransitionService : IShipmentTransitionService
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

public class ManifestServiceReadsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ManifestConfiguration());
            modelBuilder.ApplyConfiguration(new ManifestShipmentConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentTransferConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    private static ManifestService CreateService(TestDbContext dbContext, Guid organizationId, IReadOnlySet<Guid>? accessible, FakeShipmentReportingLookup lookup) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedShipmentTransitionService(),
            new ScopedBranchesChecker(accessible), new NotSupportedBranchLookup(), lookup);

    private static async Task<Manifest> SeedManifestAsync(TestDbContext dbContext, Guid organizationId, Guid fromBranchId, Guid toBranchId, ManifestStatus status = ManifestStatus.InTransit)
    {
        var manifest = new Manifest
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Code = $"MNF-20261015-{Random.Shared.Next(100, 999)}",
            FromBranchId = fromBranchId, ToBranchId = toBranchId, Status = status, ShipmentCount = 0,
            CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<Manifest>().Add(manifest);
        await dbContext.SaveChangesAsync();
        return manifest;
    }

    private static async Task<ManifestShipment> SeedParcelAsync(TestDbContext dbContext, Guid manifestId, DateTimeOffset scannedAt, DateTimeOffset? receivedAt = null)
    {
        var parcel = new ManifestShipment { Id = Guid.NewGuid(), ManifestId = manifestId, ShipmentId = Guid.NewGuid(), ScannedAt = scannedAt, ReceivedAt = receivedAt };
        dbContext.Set<ManifestShipment>().Add(parcel);
        await dbContext.SaveChangesAsync();
        return parcel;
    }

    [Fact]
    public async Task GetByIdAsync_ManifestOutsideReadScope_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new HashSet<Guid> { Guid.NewGuid() }, new FakeShipmentReportingLookup());

        Assert.Null(await service.GetByIdAsync(manifest.Id, ActingUser()));
    }

    [Fact]
    public async Task GetByIdAsync_ManifestToScopedBranch_ReturnsIt()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), toBranchId);
        var service = CreateService(dbContext, organizationId, new HashSet<Guid> { toBranchId }, new FakeShipmentReportingLookup());

        var result = await service.GetByIdAsync(manifest.Id, ActingUser());

        Assert.NotNull(result);
        Assert.Equal(manifest.Id, result!.Id);
    }

    [Fact]
    public async Task GetMissingShipmentsAsync_ManifestOutsideReadScope_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new HashSet<Guid> { Guid.NewGuid() }, new FakeShipmentReportingLookup());

        Assert.Null(await service.GetMissingShipmentsAsync(manifest.Id, ActingUser()));
    }

    [Fact]
    public async Task GetMissingShipmentsAsync_ResolvesEveryAwbInOneBatchedLookup()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid(), ManifestStatus.Closed);
        var start = DateTimeOffset.UtcNow.AddHours(-5);
        var first = await SeedParcelAsync(dbContext, manifest.Id, start);
        var second = await SeedParcelAsync(dbContext, manifest.Id, start.AddMinutes(1));
        var third = await SeedParcelAsync(dbContext, manifest.Id, start.AddMinutes(2));
        await SeedParcelAsync(dbContext, manifest.Id, start.AddMinutes(3), receivedAt: DateTimeOffset.UtcNow);
        var lookup = new FakeShipmentReportingLookup();
        lookup.Awbs[first.ShipmentId] = "AWB-1";
        lookup.Awbs[second.ShipmentId] = "AWB-2";
        // third deliberately has no AWB: the response keeps the row with an empty Awb.
        var service = CreateService(dbContext, organizationId, null, lookup);

        var missing = await service.GetMissingShipmentsAsync(manifest.Id, ActingUser());

        Assert.NotNull(missing);
        Assert.Equal(new[] { "AWB-1", "AWB-2", string.Empty }, missing!.Select(m => m.Awb).ToArray());
        Assert.Equal(third.ShipmentId, missing[2].ShipmentId);
        Assert.Equal(1, lookup.AwbLookupCalls);
    }

    [Fact]
    public async Task ListShipmentsAsync_ReturnsEveryParcelOldestScanFirstWithAwbs()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid(), ManifestStatus.Received);
        var start = DateTimeOffset.UtcNow.AddHours(-3);
        var later = await SeedParcelAsync(dbContext, manifest.Id, start.AddMinutes(10), receivedAt: DateTimeOffset.UtcNow);
        var earlier = await SeedParcelAsync(dbContext, manifest.Id, start);
        var otherManifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        await SeedParcelAsync(dbContext, otherManifest.Id, start);
        var lookup = new FakeShipmentReportingLookup();
        lookup.Awbs[later.ShipmentId] = "AWB-LATER";
        lookup.Awbs[earlier.ShipmentId] = "AWB-EARLIER";
        var service = CreateService(dbContext, organizationId, null, lookup);

        var parcels = await service.ListShipmentsAsync(manifest.Id, ActingUser());

        Assert.NotNull(parcels);
        Assert.Equal(new[] { "AWB-EARLIER", "AWB-LATER" }, parcels!.Select(p => p.Awb).ToArray());
        Assert.Null(parcels[0].ReceivedAt);
        Assert.NotNull(parcels[1].ReceivedAt);
        Assert.Equal(1, lookup.AwbLookupCalls);
    }

    [Fact]
    public async Task ListShipmentsAsync_ManifestOutsideReadScope_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new HashSet<Guid> { Guid.NewGuid() }, new FakeShipmentReportingLookup());

        Assert.Null(await service.ListShipmentsAsync(manifest.Id, ActingUser()));
    }

    [Fact]
    public async Task ListShipmentsAsync_UnknownManifest_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), null, new FakeShipmentReportingLookup());

        Assert.Null(await service.ListShipmentsAsync(Guid.NewGuid(), ActingUser()));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build tests/Manifests.Tests/Manifests.Tests.csproj`
Expected: FAIL to compile — `ManifestService` has no 7-argument constructor and no `ListShipmentsAsync`; `GetByIdAsync`/`GetMissingShipmentsAsync` take no `ClaimsPrincipal`.

- [ ] **Step 4: Add the constructor dependency**

In `src/Modules/Manifests/Application/ManifestService.cs`, add the field and the trailing constructor parameter (keep the existing six):

```csharp
    private readonly IBranchLookupService _branchLookupService;
    private readonly IShipmentReportingLookupService _shipmentReportingLookupService;

    public ManifestService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IShipmentTransitionService shipmentTransitionService,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IBranchLookupService branchLookupService,
        IShipmentReportingLookupService shipmentReportingLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _shipmentTransitionService = shipmentTransitionService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _branchLookupService = branchLookupService;
        _shipmentReportingLookupService = shipmentReportingLookupService;
    }
```

`using Courier.Shipments.Application;` is already present. DI needs no change: `Program.cs` registers `IShipmentReportingLookupService` and `AddScoped<ManifestService>()` resolves the new parameter.

- [ ] **Step 5: Implement the scoped reads**

In `src/Modules/Manifests/Application/ManifestService.Reads.cs`, replace `GetByIdAsync` and `GetMissingShipmentsAsync` with the following, and add `ListShipmentsAsync` and `FindReadableAsync` (keep `ListAsync` unchanged in this task):

```csharp
    public async Task<ManifestResponse?> GetByIdAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var manifest = await FindReadableAsync(manifestId, actingUser, cancellationToken);
        return manifest is null ? null : ToResponse(manifest);
    }

    // Meaningful once the manifest is Closed (a null ReceivedAt then IS the missing-shipment
    // exception per this plan's resolved design decision #4) — also usable while a manifest is
    // still InTransit/Received to show which shipments haven't been scanned in yet. AWBs come from
    // one batched lookup (F15 R3b-1), not a per-row scan-info call.
    public async Task<List<MissingShipmentResponse>?> GetMissingShipmentsAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var manifest = await FindReadableAsync(manifestId, actingUser, cancellationToken);
        if (manifest is null)
        {
            return null;
        }

        var unreceived = await _dbContext.Set<ManifestShipment>()
            .Where(ms => ms.ManifestId == manifestId && ms.ReceivedAt == null)
            .OrderBy(ms => ms.ScannedAt).ThenBy(ms => ms.Id)
            .ToListAsync(cancellationToken);
        var awbs = await _shipmentReportingLookupService.GetAwbsByShipmentIdsAsync(
            unreceived.Select(ms => ms.ShipmentId).ToHashSet(), cancellationToken);

        return unreceived
            .Select(ms => new MissingShipmentResponse(ms.ShipmentId, awbs.GetValueOrDefault(ms.ShipmentId, string.Empty), ms.ScannedAt))
            .ToList();
    }

    // Every parcel on the manifest (F15 R3b-1). Not paginated: bounded by one vehicle load.
    public async Task<List<ManifestShipmentResponse>?> ListShipmentsAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var manifest = await FindReadableAsync(manifestId, actingUser, cancellationToken);
        if (manifest is null)
        {
            return null;
        }

        var parcels = await _dbContext.Set<ManifestShipment>()
            .Where(ms => ms.ManifestId == manifestId)
            .OrderBy(ms => ms.ScannedAt).ThenBy(ms => ms.Id)
            .ToListAsync(cancellationToken);
        var awbs = await _shipmentReportingLookupService.GetAwbsByShipmentIdsAsync(
            parcels.Select(ms => ms.ShipmentId).ToHashSet(), cancellationToken);

        return parcels
            .Select(ms => new ManifestShipmentResponse(ms.Id, ms.ShipmentId, awbs.GetValueOrDefault(ms.ShipmentId, string.Empty), ms.ScannedAt, ms.ReceivedAt))
            .ToList();
    }

    // Single reads use the list's rule: readable when FromBranchId or ToBranchId is in the
    // caller's manifest.read scope (null = all branches). Out of scope reads as not found, so a
    // guessed id never confirms that a manifest exists.
    private async Task<Manifest?> FindReadableAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken cancellationToken)
    {
        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken);
        if (manifest is null)
        {
            return null;
        }

        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "manifest.read", cancellationToken);
        if (accessibleBranchIds is not null &&
            !accessibleBranchIds.Contains(manifest.FromBranchId) &&
            !accessibleBranchIds.Contains(manifest.ToBranchId))
        {
            return null;
        }
        return manifest;
    }
```

- [ ] **Step 6: Wire the endpoints**

In `src/Modules/Manifests/Presentation/ManifestsEndpoints.cs`, replace the two single-read routes and add `/shipments` right after them:

```csharp
        group.MapGet("/{id:guid}", async (Guid id, ManifestService service, HttpContext httpContext) =>
        {
            var result = await service.GetByIdAsync(id, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("manifest.read");

        group.MapGet("/{id:guid}/missing-shipments", async (Guid id, ManifestService service, HttpContext httpContext) =>
        {
            var result = await service.GetMissingShipmentsAsync(id, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("manifest.read");

        group.MapGet("/{id:guid}/shipments", async (Guid id, ManifestService service, HttpContext httpContext) =>
        {
            var result = await service.ListShipmentsAsync(id, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("manifest.read");
```

- [ ] **Step 7: Update the existing unit-test call sites**

1. Append `, new FakeShipmentReportingLookup()` as the last argument of every `new ManifestService(` call in `ManifestServiceListTests.cs` (4 sites), `ManifestServicePurposeTests.cs` (3), `ManifestServiceScanningTests.cs` (2) and `ManifestServiceTransitionsTests.cs` (4) — 13 in total (`grep -rn "new ManifestService(" tests/Manifests.Tests`).
2. In `ManifestServiceTransitionsTests.CloseAsync_ReceivedManifestWithUnscannedShipment_ClosesAndSurfacesMissingShipment`, construct a named lookup and register the missing AWB, then pass an acting user:

```csharp
        var lookup = new FakeShipmentReportingLookup();
        var service = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments,
            new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId), lookup);
```

and, after `var missingShipmentId = Guid.NewGuid();`, add `lookup.Awbs[missingShipmentId] = "AWB-MISSING";`. Change the read to `var missing = await service.GetMissingShipmentsAsync(manifest.Id, ActingUser(Guid.NewGuid()));`.
3. In `GetByIdAsync_UnknownManifest_ReturnsNull`, change the call to `await service.GetByIdAsync(Guid.NewGuid(), ActingUser(Guid.NewGuid()))`.

- [ ] **Step 8: Run the Manifests unit tests**

Run: `dotnet test tests/Manifests.Tests/Manifests.Tests.csproj`
Expected: PASS (all, including the 7 new `ManifestServiceReadsTests`).

- [ ] **Step 9: Write the API tests**

Append to `tests/Api.Tests/ManifestsEndpointsTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task ManifestSingleReads_UserWithNoAccessToEitherBranch_Return404()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client, "single-scope");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var (fromBranchId, toBranchId, _) = await SeedBranchesAsync(client);
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId)))
            .Content.ReadFromJsonAsync<ManifestResponse>();
        var (unrelatedBranchId, _, _) = await SeedBranchesAsync(client);

        var scopedToken = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "manifest.read", unrelatedBranchId);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopedToken);

        foreach (var path in new[] { "", "/missing-shipments", "/shipments" })
        {
            var response = await scopedClient.GetAsync($"/api/v1/manifests/{manifest!.Id}{path}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task ManifestSingleReads_UserScopedToDestinationBranch_Return200()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client, "single-dest");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var (fromBranchId, toBranchId, _) = await SeedBranchesAsync(client);
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId)))
            .Content.ReadFromJsonAsync<ManifestResponse>();

        var scopedToken = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "manifest.read", toBranchId);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopedToken);

        var response = await scopedClient.GetAsync($"/api/v1/manifests/{manifest!.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetManifestShipments_ReturnsEveryScannedParcelWithItsAwb()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "parcels");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (fromBranchId, toBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var first = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);
        var second = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId)))
            .Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(first.Awb));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/scan", new ScanShipmentRequest(second.Awb));

        var response = await client.GetAsync($"/api/v1/manifests/{manifest.Id}/shipments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parcels = await response.Content.ReadFromJsonAsync<List<ManifestShipmentResponse>>();
        Assert.Equal(new[] { first.Awb, second.Awb }, parcels!.Select(p => p.Awb).ToArray());
        Assert.All(parcels, p => Assert.Null(p.ReceivedAt));
    }

    [Fact]
    public async Task GetManifestShipments_WithoutManifestRead_Returns403()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client, "parcels-403");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var (fromBranchId, toBranchId, _) = await SeedBranchesAsync(client);
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId)))
            .Content.ReadFromJsonAsync<ManifestResponse>();
        var token = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "shipment.read", fromBranchId);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await scopedClient.GetAsync($"/api/v1/manifests/{manifest!.Id}/shipments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetManifestShipments_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/manifests/{Guid.NewGuid()}/shipments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
```

- [ ] **Step 10: Run the Manifests API tests**

Run: `docker start courier-postgres-1 courier-redis-1 courier-rabbitmq-1` then `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~ManifestsEndpointsTests"`
Expected: PASS (existing + 5 new).

- [ ] **Step 11: Commit**

```bash
git add src/Modules/Manifests tests/Manifests.Tests tests/Api.Tests/ManifestsEndpointsTests.cs
git commit -m "$(cat <<'EOF'
fix(manifests): branch-scope single reads, batch AWB lookup, add parcel list

GET /manifests/{id} and /missing-shipments returned any manifest in the
tenant regardless of branch scope; they now 404 outside manifest.read
scope. New GET /manifests/{id}/shipments lists every parcel. Missing and
parcel AWBs come from one batched IShipmentReportingLookupService call.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Manifests — `receivedCount` and the `direction` filter (backend)

**Files:**
- Modify: `src/Modules/Manifests/Application/ManifestDtos.cs`
- Modify: `src/Modules/Manifests/Application/ManifestService.cs`
- Modify: `src/Modules/Manifests/Application/ManifestService.Reads.cs`
- Modify: `src/Modules/Manifests/Application/ManifestService.Scanning.cs`
- Modify: `src/Modules/Manifests/Application/ManifestService.Transitions.cs`
- Modify: `src/Modules/Manifests/Presentation/ManifestsEndpoints.cs`
- Modify: `tests/Manifests.Tests/ManifestServiceListTests.cs`, `tests/Manifests.Tests/ManifestServiceReadsTests.cs`
- Modify: `tests/Api.Tests/ManifestsEndpointsTests.cs`

**Interfaces:**
- Consumes: `FindReadableAsync`, `FakeShipmentReportingLookup` (Task 1).
- Produces: `ManifestResponse(Guid Id, string Code, Guid FromBranchId, Guid ToBranchId, ManifestPurpose Purpose, string? VehiclePlate, Guid? DriverId, ManifestStatus Status, int ShipmentCount, DateTimeOffset CreatedAt, int ReceivedCount)`; `Task<List<ManifestResponse>> ListAsync(Guid? branchId, ManifestStatus? status, string? direction, ClaimsPrincipal actingUser, CancellationToken)`; private `ToResponse(Manifest, int receivedCount)`, `Task<ManifestResponse> ToResponseAsync(Manifest, CancellationToken)`. HTTP: `GET /api/v1/manifests?direction=incoming|outgoing` (invalid → 400 `MANIFEST_VALIDATION_FAILED`); every manifest response has `receivedCount`.

- [ ] **Step 1: Write the failing unit tests**

Append to `ManifestServiceListTests` (inside the class; it already has `CreateManifestAsync`, `RestrictedBranchesAuthorizationChecker`, `AllBranchesAuthorizationChecker`):

```csharp
    private static ManifestService ListService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker checker) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new StubShipmentTransitionService(), checker,
            new FakeBranchLookup(), new FakeShipmentReportingLookup());

    [Fact]
    public async Task ListAsync_DirectionWithBranch_SplitsIncomingFromOutgoing()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var intoB = await CreateManifestAsync(dbContext, organizationId, a, b);
        var outOfB = await CreateManifestAsync(dbContext, organizationId, b, c);
        var service = ListService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var incoming = await service.ListAsync(b, null, "incoming", ActingUser(Guid.NewGuid()));
        var outgoing = await service.ListAsync(b, null, "outgoing", ActingUser(Guid.NewGuid()));
        var either = await service.ListAsync(b, null, null, ActingUser(Guid.NewGuid()));

        Assert.Equal(intoB.Id, Assert.Single(incoming).Id);
        Assert.Equal(outOfB.Id, Assert.Single(outgoing).Id);
        Assert.Equal(2, either.Count);
    }

    [Fact]
    public async Task ListAsync_DirectionWithoutBranch_UsesCallerScope()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var intoB = await CreateManifestAsync(dbContext, organizationId, a, b);
        var outOfB = await CreateManifestAsync(dbContext, organizationId, b, c);
        var service = ListService(dbContext, organizationId, new RestrictedBranchesAuthorizationChecker(b));

        var incoming = await service.ListAsync(null, null, "incoming", ActingUser(Guid.NewGuid()));
        var outgoing = await service.ListAsync(null, null, "OUTGOING", ActingUser(Guid.NewGuid()));

        Assert.Equal(intoB.Id, Assert.Single(incoming).Id);
        Assert.Equal(outOfB.Id, Assert.Single(outgoing).Id);
    }

    [Fact]
    public async Task ListAsync_DirectionAllBranchesScopeNoBranch_DoesNotRestrict()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        await CreateManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        await CreateManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        var service = ListService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var incoming = await service.ListAsync(null, null, "incoming", ActingUser(Guid.NewGuid()));

        Assert.Equal(2, incoming.Count);
    }

    [Fact]
    public async Task ListAsync_DirectionWithBranchOutsideScope_ReturnsEmpty()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await CreateManifestAsync(dbContext, organizationId, b, c);
        var service = ListService(dbContext, organizationId, new RestrictedBranchesAuthorizationChecker(a));

        var incoming = await service.ListAsync(c, null, "incoming", ActingUser(Guid.NewGuid()));

        Assert.Empty(incoming);
    }

    [Fact]
    public async Task ListAsync_InvalidDirection_Throws()
    {
        await using var dbContext = CreateContext();
        var service = ListService(dbContext, Guid.NewGuid(), new AllBranchesAuthorizationChecker());

        await Assert.ThrowsAsync<ManifestValidationException>(() => service.ListAsync(null, null, "sideways", ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ListAsync_ReceivedCount_CountsReceivedParcelsPerManifest()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var withParcels = await CreateManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        var empty = await CreateManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        dbContext.Set<ManifestShipment>().AddRange(
            new ManifestShipment { Id = Guid.NewGuid(), ManifestId = withParcels.Id, ShipmentId = Guid.NewGuid(), ScannedAt = now, ReceivedAt = now },
            new ManifestShipment { Id = Guid.NewGuid(), ManifestId = withParcels.Id, ShipmentId = Guid.NewGuid(), ScannedAt = now, ReceivedAt = now },
            new ManifestShipment { Id = Guid.NewGuid(), ManifestId = withParcels.Id, ShipmentId = Guid.NewGuid(), ScannedAt = now, ReceivedAt = null });
        await dbContext.SaveChangesAsync();
        var service = ListService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListAsync(null, null, null, ActingUser(Guid.NewGuid()));

        Assert.Equal(2, result.Single(m => m.Id == withParcels.Id).ReceivedCount);
        Assert.Equal(0, result.Single(m => m.Id == empty.Id).ReceivedCount);
    }
```

Change the three existing `ListAsync(` calls in this file to pass `null` as the new third argument — `ListAsync(null, null, null, ActingUser(...))`, `ListAsync(null, ManifestStatus.Sealed, null, ActingUser(...))`, `ListAsync(branchA, null, null, ActingUser(...))`.

Append to `ManifestServiceReadsTests`:

```csharp
    [Fact]
    public async Task GetByIdAsync_ReturnsReceivedCount()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var manifest = await SeedManifestAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid(), ManifestStatus.Received);
        await SeedParcelAsync(dbContext, manifest.Id, DateTimeOffset.UtcNow, receivedAt: DateTimeOffset.UtcNow);
        await SeedParcelAsync(dbContext, manifest.Id, DateTimeOffset.UtcNow);
        var service = CreateService(dbContext, organizationId, null, new FakeShipmentReportingLookup());

        var result = await service.GetByIdAsync(manifest.Id, ActingUser());

        Assert.Equal(1, result!.ReceivedCount);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Manifests.Tests/Manifests.Tests.csproj`
Expected: FAIL to compile — `ListAsync` has no `direction` parameter; `ManifestResponse` has no `ReceivedCount`.

- [ ] **Step 3: Add `ReceivedCount` to the response**

In `ManifestDtos.cs`, add a trailing member (trailing so no positional caller shifts):

```csharp
public record ManifestResponse(
    Guid Id,
    string Code,
    Guid FromBranchId,
    Guid ToBranchId,
    ManifestPurpose Purpose,
    string? VehiclePlate,
    Guid? DriverId,
    ManifestStatus Status,
    int ShipmentCount,
    DateTimeOffset CreatedAt,
    // F15 R3b-1: parcels receive-scanned at the destination (ManifestShipment.ReceivedAt != null).
    int ReceivedCount);
```

In `ManifestService.cs`, replace `LoadResponseAsync` and `ToResponse` with:

```csharp
    private async Task<ManifestResponse> LoadResponseAsync(Guid manifestId, CancellationToken cancellationToken)
    {
        var manifest = await _dbContext.Set<Manifest>().FirstAsync(m => m.Id == manifestId, cancellationToken);
        return await ToResponseAsync(manifest, cancellationToken);
    }

    private async Task<ManifestResponse> ToResponseAsync(Manifest manifest, CancellationToken cancellationToken)
    {
        var receivedCount = await _dbContext.Set<ManifestShipment>()
            .CountAsync(ms => ms.ManifestId == manifest.Id && ms.ReceivedAt != null, cancellationToken);
        return ToResponse(manifest, receivedCount);
    }

    private static ManifestResponse ToResponse(Manifest manifest, int receivedCount) => new(
        manifest.Id, manifest.Code, manifest.FromBranchId, manifest.ToBranchId, manifest.Purpose,
        manifest.VehiclePlate, manifest.DriverId, manifest.Status, manifest.ShipmentCount, manifest.CreatedAt, receivedCount);
```

Replace each `return ToResponse(manifest);` in `ManifestService.Scanning.cs` (Seal) and `ManifestService.Transitions.cs` (Dispatch, Close) with `return await ToResponseAsync(manifest, cancellationToken);`. In `ManifestService.Reads.cs`, `GetByIdAsync` becomes `return manifest is null ? null : await ToResponseAsync(manifest, cancellationToken);`.

- [ ] **Step 4: Implement `direction` and the grouped count in `ListAsync`**

Replace `ListAsync` in `ManifestService.Reads.cs`:

```csharp
    // direction (F15 R3b-1): "incoming" keeps manifests headed TO the branch (the requested
    // branchId, else any branch in the caller's scope); "outgoing" the same on FromBranchId;
    // omitted keeps today's "touches the branch either way". It only narrows the scope filter.
    public async Task<List<ManifestResponse>> ListAsync(Guid? branchId, ManifestStatus? status, string? direction, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var normalizedDirection = string.IsNullOrWhiteSpace(direction) ? null : direction.Trim().ToLowerInvariant();
        if (normalizedDirection is not (null or "incoming" or "outgoing"))
        {
            throw new ManifestValidationException("direction must be 'incoming' or 'outgoing'.");
        }

        var query = _dbContext.Set<Manifest>().AsQueryable();

        // Scope to branches the caller can access for manifest.read. null means "no filter
        // applies" (BranchScope.AllBranches) per IBranchAuthorizationChecker's documented
        // contract — a non-null set restricts to manifests touching any of those branches as
        // sender or receiver, mirroring ShipmentService.Reads.cs's ListAsync's established shape.
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "manifest.read", cancellationToken);
        var accessibleBranchIdsArray = accessibleBranchIds?.ToArray();
        if (accessibleBranchIdsArray is not null)
        {
            query = query.Where(m =>
                accessibleBranchIdsArray.Contains(m.FromBranchId) ||
                accessibleBranchIdsArray.Contains(m.ToBranchId));
        }

        if (branchId is Guid requestedBranchId)
        {
            query = normalizedDirection switch
            {
                "incoming" => query.Where(m => m.ToBranchId == requestedBranchId),
                "outgoing" => query.Where(m => m.FromBranchId == requestedBranchId),
                _ => query.Where(m => m.FromBranchId == requestedBranchId || m.ToBranchId == requestedBranchId)
            };
        }
        else if (accessibleBranchIdsArray is not null)
        {
            if (normalizedDirection == "incoming")
            {
                query = query.Where(m => accessibleBranchIdsArray.Contains(m.ToBranchId));
            }
            else if (normalizedDirection == "outgoing")
            {
                query = query.Where(m => accessibleBranchIdsArray.Contains(m.FromBranchId));
            }
        }

        if (status is ManifestStatus requestedStatus)
        {
            query = query.Where(m => m.Status == requestedStatus);
        }

        var manifests = await query.OrderByDescending(m => m.CreatedAt).ToListAsync(cancellationToken);

        // One grouped query for every listed manifest's received count — no per-row CountAsync.
        var manifestIds = manifests.Select(m => m.Id).ToArray();
        var receivedCounts = await _dbContext.Set<ManifestShipment>()
            .Where(ms => manifestIds.Contains(ms.ManifestId) && ms.ReceivedAt != null)
            .GroupBy(ms => ms.ManifestId)
            .Select(g => new { ManifestId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ManifestId, x => x.Count, cancellationToken);

        return manifests.Select(m => ToResponse(m, receivedCounts.GetValueOrDefault(m.Id))).ToList();
    }
```

- [ ] **Step 5: Route the list through validation handling**

In `ManifestsEndpoints.cs`, replace the list route:

```csharp
        group.MapGet("/", async (Guid? branchId, ManifestStatus? status, string? direction, ManifestService service, HttpContext httpContext) =>
        {
            try
            {
                return Results.Ok(await service.ListAsync(branchId, status, direction, httpContext.User, httpContext.RequestAborted));
            }
            catch (ManifestValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "MANIFEST_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("manifest.read");
```

- [ ] **Step 6: Run the Manifests unit tests**

Run: `dotnet test tests/Manifests.Tests/Manifests.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Write the API tests**

In `FullManifestLifecycle_DraftToClosed_MovesShipmentThroughCustodyAndSurfacesMissingShipment`, after `Assert.Equal(ManifestStatus.Closed, closed!.Status);` add:

```csharp
        Assert.Equal(1, closed.ReceivedCount);
        Assert.Equal(2, closed.ShipmentCount);
```

Append to the class:

```csharp
    [Fact]
    public async Task GetManifests_DirectionFilter_SplitsIncomingAndOutgoingForABranch()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "direction");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (fromBranchId, toBranchId, _) = await SeedBranchesAsync(client);
        var forward = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId)))
            .Content.ReadFromJsonAsync<ManifestResponse>();
        var back = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(toBranchId, fromBranchId)))
            .Content.ReadFromJsonAsync<ManifestResponse>();

        var incoming = await (await client.GetAsync($"/api/v1/manifests?branchId={toBranchId}&direction=incoming"))
            .Content.ReadFromJsonAsync<List<ManifestResponse>>();
        var outgoing = await (await client.GetAsync($"/api/v1/manifests?branchId={toBranchId}&direction=outgoing"))
            .Content.ReadFromJsonAsync<List<ManifestResponse>>();

        Assert.Contains(incoming!, m => m.Id == forward!.Id);
        Assert.DoesNotContain(incoming!, m => m.Id == back!.Id);
        Assert.Contains(outgoing!, m => m.Id == back!.Id);
        Assert.DoesNotContain(outgoing!, m => m.Id == forward!.Id);
        Assert.All(incoming!, m => Assert.Equal(0, m.ReceivedCount));
    }

    [Fact]
    public async Task GetManifests_InvalidDirection_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "direction-400");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/manifests?direction=sideways");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("MANIFEST_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }
```

- [ ] **Step 8: Run the Manifests API tests**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~ManifestsEndpointsTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Manifests tests/Manifests.Tests tests/Api.Tests/ManifestsEndpointsTests.cs
git commit -m "$(cat <<'EOF'
feat(manifests): receivedCount on responses and incoming/outgoing direction filter

Every ManifestResponse carries receivedCount (one grouped query for list
pages, one count for single responses). GET /manifests takes
direction=incoming|outgoing, narrowing within branch scope; an invalid
value is a 400 MANIFEST_VALIDATION_FAILED instead of a 500.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---
### Task 3: Riders — `GET /shipments/{shipmentId}/delivery-attempts` (backend)

**Files:**
- Modify: `src/Modules/Riders/Application/RiderDtos.cs`
- Create: `src/Modules/Riders/Application/RiderService.AttemptReads.cs`
- Modify: `src/Modules/Riders/Presentation/RidersEndpoints.cs`
- Create: `tests/Riders.Tests/RiderServiceDeliveryAttemptReadsTests.cs`
- Modify: `tests/Api.Tests/NdrEndpointsTests.cs`

**Interfaces:**
- Produces: `DeliveryAttemptResponse(int AttemptNumber, DateTimeOffset AttemptedAt, bool Succeeded, DeliveryAttemptReason? Reason, string? Notes, bool CustomerContacted, string? NextAction, Guid RiderId)`; `DeliveryAttemptListResponse(IReadOnlyList<DeliveryAttemptResponse> Items)`; `Task<DeliveryAttemptListResponse?> RiderService.ListDeliveryAttemptsAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken)` (null = unknown or out of scope). HTTP: `GET /api/v1/shipments/{shipmentId:guid}/delivery-attempts` → `{ items: [...] }`, newest first. `RiderId` is `Rider.Id` (web resolves it with `usePeopleNames().riderName`). API test helpers in `NdrEndpointsTests`: `SeedScopedUserTokenAsync`, `CreateMerchantClientAsync`.

- [ ] **Step 1: Write the failing unit tests**

Create `tests/Riders.Tests/RiderServiceDeliveryAttemptReadsTests.cs`:

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
using Courier.Riders.Domain;
using Courier.Riders.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Riders.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class ScopedBranchesChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid>? _accessible;
    public ScopedBranchesChecker(IReadOnlySet<Guid>? accessible) => _accessible = accessible;
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessible);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessible is null || _accessible.Contains(branchId));
}

file class NotSupportedUserProvisioningService : IUserProvisioningService
{
    public Task<Guid> ProvisionStaffUserAsync(ProvisionStaffUserRequest request, Guid organizationId, Guid grantedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NotSupportedBranchLookup : IBranchLookupService
{
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class ScanInfoShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);
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

file class NotSupportedObjectStorage : IObjectStorage
{
    public Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NotSupportedNdrCreationService : INdrCreationService
{
    public Task CreateFromFailedAttemptAsync(Guid shipmentId, Guid organizationId, NdrReason reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NoopCodCollectionService : ICodCollectionService
{
    public Task MarkCollectedAsync(Guid shipmentId, decimal collectedAmount, Guid riderId, Guid deliveryProofId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class AlwaysTruePermissionChecker : IPermissionChecker
{
    public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class NotSupportedShipmentTaskLookupService : IShipmentTaskLookupService
{
    public Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class RiderServiceDeliveryAttemptReadsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RiderConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryAttemptConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    private static RiderService CreateService(TestDbContext dbContext, Guid organizationId, IReadOnlySet<Guid>? accessible, ScanInfoShipmentTransitionService shipments) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), new ScopedBranchesChecker(accessible), shipments, new NotSupportedObjectStorage(),
            new NotSupportedNdrCreationService(), new NoopCodCollectionService(), new AlwaysTruePermissionChecker(),
            new NoopOutboxWriter(), new NotSupportedShipmentTaskLookupService());

    private static ShipmentScanInfo ScanInfo(Guid shipmentId, Guid organizationId, Guid origin, Guid destination, Guid? current) =>
        new(shipmentId, organizationId, "AWB-1", ShipmentStatus.NdrCreated, current, origin, destination, null);

    private static DeliveryAttempt Attempt(Guid organizationId, Guid shipmentId, int number, DateTimeOffset at, bool succeeded = false) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = shipmentId, RiderId = Guid.NewGuid(),
        AttemptNumber = number, AttemptedAt = at, Succeeded = succeeded,
        Reason = succeeded ? null : DeliveryAttemptReason.CustomerUnavailable,
        Notes = $"attempt {number}", CustomerContacted = true, NextAction = "Reattempt", Latitude = 27.7m, Longitude = 85.3m
    };

    [Fact]
    public async Task ListDeliveryAttemptsAsync_ReturnsThisShipmentsAttemptsNewestFirst()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow.AddDays(-1);
        dbContext.Set<DeliveryAttempt>().AddRange(
            Attempt(organizationId, shipmentId, 1, at),
            Attempt(organizationId, shipmentId, 3, at.AddHours(5)),
            Attempt(organizationId, shipmentId, 2, at.AddHours(5)),
            Attempt(organizationId, Guid.NewGuid(), 1, at.AddHours(9)));
        await dbContext.SaveChangesAsync();
        var shipments = new ScanInfoShipmentTransitionService();
        shipments.ShipmentsById[shipmentId] = ScanInfo(shipmentId, organizationId, Guid.NewGuid(), Guid.NewGuid(), null);
        var service = CreateService(dbContext, organizationId, null, shipments);

        var result = await service.ListDeliveryAttemptsAsync(shipmentId, ActingUser());

        Assert.NotNull(result);
        Assert.Equal(new[] { 3, 2, 1 }, result!.Items.Select(a => a.AttemptNumber).ToArray());
        var newest = result.Items[0];
        Assert.False(newest.Succeeded);
        Assert.Equal(DeliveryAttemptReason.CustomerUnavailable, newest.Reason);
        Assert.Equal("attempt 3", newest.Notes);
        Assert.True(newest.CustomerContacted);
        Assert.Equal("Reattempt", newest.NextAction);
    }

    [Fact]
    public async Task ListDeliveryAttemptsAsync_ShipmentOutsideNdrReadScope_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var shipments = new ScanInfoShipmentTransitionService();
        shipments.ShipmentsById[shipmentId] = ScanInfo(shipmentId, organizationId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new HashSet<Guid> { Guid.NewGuid() }, shipments);

        Assert.Null(await service.ListDeliveryAttemptsAsync(shipmentId, ActingUser()));
    }

    [Fact]
    public async Task ListDeliveryAttemptsAsync_InScopeThroughCurrentBranchOnly_ReturnsItems()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var currentBranchId = Guid.NewGuid();
        dbContext.Set<DeliveryAttempt>().Add(Attempt(organizationId, shipmentId, 1, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();
        var shipments = new ScanInfoShipmentTransitionService();
        shipments.ShipmentsById[shipmentId] = ScanInfo(shipmentId, organizationId, Guid.NewGuid(), Guid.NewGuid(), currentBranchId);
        var service = CreateService(dbContext, organizationId, new HashSet<Guid> { currentBranchId }, shipments);

        var result = await service.ListDeliveryAttemptsAsync(shipmentId, ActingUser());

        Assert.Single(result!.Items);
    }

    [Fact]
    public async Task ListDeliveryAttemptsAsync_UnknownShipment_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), null, new ScanInfoShipmentTransitionService());

        Assert.Null(await service.ListDeliveryAttemptsAsync(Guid.NewGuid(), ActingUser()));
    }

    [Fact]
    public async Task ListDeliveryAttemptsAsync_NoAttemptsYet_ReturnsEmptyItems()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var shipments = new ScanInfoShipmentTransitionService();
        shipments.ShipmentsById[shipmentId] = ScanInfo(shipmentId, organizationId, Guid.NewGuid(), Guid.NewGuid(), null);
        var service = CreateService(dbContext, organizationId, null, shipments);

        var result = await service.ListDeliveryAttemptsAsync(shipmentId, ActingUser());

        Assert.NotNull(result);
        Assert.Empty(result!.Items);
    }
}
```

The fake interfaces mirror `RiderServiceReadsTests.cs`; if a signature there differs (for example `IObjectStorage` gained a member), copy that file's version.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Riders.Tests/Riders.Tests.csproj`
Expected: FAIL to compile — `RiderService` has no `ListDeliveryAttemptsAsync`.

- [ ] **Step 3: Add the DTOs**

Append to `src/Modules/Riders/Application/RiderDtos.cs`:

```csharp
// F15 R3b-1 staff read of a shipment's attempt history (NDR Sheet). GPS is deliberately not
// exposed — nothing on screen needs it. RiderId is Rider.Id, not a user id.
public record DeliveryAttemptResponse(
    int AttemptNumber, DateTimeOffset AttemptedAt, bool Succeeded, DeliveryAttemptReason? Reason,
    string? Notes, bool CustomerContacted, string? NextAction, Guid RiderId);

public record DeliveryAttemptListResponse(IReadOnlyList<DeliveryAttemptResponse> Items);
```

- [ ] **Step 4: Implement the read**

Create `src/Modules/Riders/Application/RiderService.AttemptReads.cs`:

```csharp
using System.Security.Claims;
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
    // Lives in Riders (the DeliveryAttempt owner) because Ndr cannot reference Riders — Riders
    // already references Ndr. Scope: the shipment's current, origin or destination branch must be
    // in the caller's ndr.read scope, the same rule as the staff shipment single read; out of
    // scope reads as not found. Newest first; not paginated (attempts per shipment are few).
    public async Task<DeliveryAttemptListResponse?> ListDeliveryAttemptsAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(shipmentId, cancellationToken);
        if (scanInfo is null)
        {
            return null;
        }

        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "ndr.read", cancellationToken);
        if (accessibleBranchIds is not null)
        {
            var inScope = accessibleBranchIds.Contains(scanInfo.OriginBranchId) ||
                          accessibleBranchIds.Contains(scanInfo.DestinationBranchId) ||
                          (scanInfo.CurrentBranchId is Guid currentBranchId && accessibleBranchIds.Contains(currentBranchId));
            if (!inScope)
            {
                return null;
            }
        }

        var items = await _dbContext.Set<DeliveryAttempt>().AsNoTracking()
            .Where(a => a.ShipmentId == shipmentId)
            .OrderByDescending(a => a.AttemptedAt).ThenByDescending(a => a.AttemptNumber)
            .Select(a => new DeliveryAttemptResponse(
                a.AttemptNumber, a.AttemptedAt, a.Succeeded, a.Reason, a.Notes, a.CustomerContacted, a.NextAction, a.RiderId))
            .ToListAsync(cancellationToken);
        return new DeliveryAttemptListResponse(items);
    }
}
```

- [ ] **Step 5: Map the endpoint**

In `src/Modules/Riders/Presentation/RidersEndpoints.cs`, inside `MapRidersEndpoints` right after the `vehicles` group declaration, add:

```csharp
        // F15 R3b-1: staff read of one shipment's attempt history, under the shipment URL the NDR
        // Sheet already knows. Staff only — merchants follow their parcels through /track.
        var shipmentAttempts = app.MapGroup("/api/v1/shipments")
        .WithTags("Rider Management")
        .RequireAuthorization();

        shipmentAttempts.MapGet("/{shipmentId:guid}/delivery-attempts", async (Guid shipmentId, RiderService service, HttpContext httpContext) =>
        {
            if (httpContext.User.GetMerchantId() is not null)
            {
                return Results.Forbid();
            }
            var result = await service.ListDeliveryAttemptsAsync(shipmentId, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("ndr.read");
```

(`GetMerchantId` is `Courier.SharedKernel.ClaimsPrincipalExtensions`; the file already imports `Courier.SharedKernel`.)

- [ ] **Step 6: Run the Riders unit tests**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj`
Expected: PASS (all, including 5 new).

- [ ] **Step 7: Write the API tests**

In `tests/Api.Tests/NdrEndpointsTests.cs`, add these usings next to the existing ones:

```csharp
using Courier.Identity.Domain;
using Courier.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
```

Add two helpers inside the class. The first is the same helper `ManifestsEndpointsTests` uses:

```csharp
    private async Task<string> SeedScopedUserTokenAsync(
        Guid organizationId, Guid ownerUserId, string permissionKey, params Guid[] grantedBranchIds)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var permission = await dbContext.Set<Permission>().FirstAsync(p => p.Key == permissionKey);
        var role = new Role
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = $"ScopedRole-{permissionKey}-{Guid.NewGuid():N}",
            IsSystemDefault = false
        };
        role.Permissions.Add(permission);
        dbContext.Set<Role>().Add(role);

        var scopedUserId = Guid.NewGuid();
        var email = $"scoped-{scopedUserId:N}@example.com";
        dbContext.Set<User>().Add(new User
        {
            Id = scopedUserId,
            OrganizationId = organizationId,
            Email = email,
            PasswordHash = "not-used-in-this-test",
            FullName = "Scoped User"
        });
        dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = scopedUserId,
            RoleId = role.Id,
            OrganizationId = organizationId,
            BranchScope = BranchScope.SpecificBranches,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = ownerUserId
        });
        foreach (var branchId in grantedBranchIds)
        {
            dbContext.Set<UserBranch>().Add(new UserBranch
            {
                Id = Guid.NewGuid(),
                UserId = scopedUserId,
                BranchId = branchId,
                OrganizationId = organizationId,
                GrantedAt = DateTimeOffset.UtcNow,
                GrantedByUserId = ownerUserId
            });
        }
        await dbContext.SaveChangesAsync();

        return jwtTokenService.IssueAccessToken(scopedUserId, organizationId, email);
    }

    private async Task<HttpClient> CreateMerchantClientAsync(HttpClient staffClient)
    {
        var ownerEmail = $"ndr-merchant-{Guid.NewGuid():N}@example.com";
        var createResponse = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Attempts Shipper Pvt. Ltd.", "Attempts Shipper", "01-4444444", "attempts-shipper@example.com",
            ownerEmail, "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        createResponse.EnsureSuccessStatusCode();

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, "P@ssw0rd123!"));
        loginResponse.EnsureSuccessStatusCode();
        var auth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return merchantClient;
    }
```

`ManifestsEndpointsTests.cs` compiles the same helper with `Courier.Branches.Domain` + `Courier.Identity.Domain` (+ `Courier.Identity.Application` for `IJwtTokenService`); all are now imported here.

Append the tests:

```csharp
    [Fact]
    public async Task GetDeliveryAttempts_AfterAFailedAttempt_ReturnsIt()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "attempts");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndFailDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);

        var response = await client.GetAsync($"/api/v1/shipments/{shipment.Id}/delivery-attempts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeliveryAttemptListResponse>();
        var attempt = Assert.Single(body!.Items);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.False(attempt.Succeeded);
        Assert.Equal(DeliveryAttemptReason.CustomerUnavailable, attempt.Reason);
        Assert.Equal("No one home", attempt.Notes);
        Assert.True(attempt.CustomerContacted);
        Assert.Equal("Try tomorrow", attempt.NextAction);
        Assert.Equal(deliveryRiderId, attempt.RiderId);
        Assert.DoesNotContain("latitude", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDeliveryAttempts_UserOutsideBranchScope_Returns404()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client, "attempts-scope");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndFailDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);
        var (unrelatedBranchId, _, _) = await SeedBranchesAsync(client);
        var token = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "ndr.read", unrelatedBranchId);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await scopedClient.GetAsync($"/api/v1/shipments/{shipment.Id}/delivery-attempts");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDeliveryAttempts_WithoutNdrRead_Returns403()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client, "attempts-403");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(client);
        var token = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "shipment.read", originBranchId);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await scopedClient.GetAsync($"/api/v1/shipments/{Guid.NewGuid()}/delivery-attempts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDeliveryAttempts_MerchantCaller_Returns403()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "attempts-merchant");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        await SeedBranchesAsync(client);
        var merchantClient = await CreateMerchantClientAsync(client);

        var response = await merchantClient.GetAsync($"/api/v1/shipments/{Guid.NewGuid()}/delivery-attempts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDeliveryAttempts_UnknownShipment_Returns404()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "attempts-404");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync($"/api/v1/shipments/{Guid.NewGuid()}/delivery-attempts");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDeliveryAttempts_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/shipments/{Guid.NewGuid()}/delivery-attempts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
```

- [ ] **Step 8: Run the NDR and shipment API tests**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~NdrEndpointsTests|FullyQualifiedName~ShipmentsEndpointsTests"`
Expected: PASS (existing + 6 new). Shipments tests confirm the new `/api/v1/shipments` group did not disturb existing shipment routes.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Riders tests/Riders.Tests tests/Api.Tests/NdrEndpointsTests.cs
git commit -m "$(cat <<'EOF'
feat(riders): staff read of a shipment's delivery attempts

GET /api/v1/shipments/{shipmentId}/delivery-attempts returns the attempt
history newest first (no GPS). ndr.read, staff only (merchants 403), 404
unless the shipment's current, origin or destination branch is in scope.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: NDR — `assigned` and `reason` list filters (backend)

**Files:**
- Modify: `src/Modules/Ndr/Application/NdrService.Reads.cs`
- Modify: `src/Modules/Ndr/Presentation/NdrEndpoints.cs`
- Modify: `tests/Ndr.Tests/NdrServiceListTests.cs`
- Modify: `tests/Api.Tests/NdrEndpointsTests.cs`

**Interfaces:**
- Produces: `Task<PagedResult<NdrResponse>> NdrService.ListAsync(NdrStatus? status, int? page, int? pageSize, Guid? shipmentId, string? assigned, NdrReason? reason, ClaimsPrincipal actingUser, CancellationToken)`. HTTP: `GET /api/v1/ndrs?assigned=me|unassigned&reason=<NdrReason>` (invalid `assigned` → 400 `NDR_VALIDATION_FAILED`; invalid `reason` → 400 from model binding via `ExceptionHandlingMiddleware`'s `BadHttpRequestException` handling).

- [ ] **Step 1: Write the failing unit tests**

Change the three existing `service.ListAsync(` calls in `NdrServiceListTests.cs` to pass two extra `null` arguments before the acting user: `ListAsync(null, null, null, null, null, null, ActingUser(...))` (twice) and `ListAsync(null, null, null, targetShipmentId, null, null, ActingUser(...))`.

Append to the class:

```csharp
    private static NDR Ndr(Guid organizationId, Guid? owner, NdrReason reason = NdrReason.CustomerUnavailable, NdrStatus status = NdrStatus.Open) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = Guid.NewGuid(), Reason = reason,
        Status = status, AssignedStaffUserId = owner, CreatedAt = DateTimeOffset.UtcNow
    };

    private static NdrService AllBranchesService(TestDbContext dbContext, Guid organizationId) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new FakeShipmentTransitionService(), new AllBranchesAuthorizationChecker());

    [Fact]
    public async Task ListAsync_AssignedMe_ReturnsOnlyTheCallersNdrs()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var me = Guid.NewGuid();
        var mine = Ndr(organizationId, me);
        dbContext.Set<NDR>().AddRange(mine, Ndr(organizationId, Guid.NewGuid()), Ndr(organizationId, null));
        await dbContext.SaveChangesAsync();

        var result = await AllBranchesService(dbContext, organizationId).ListAsync(null, null, null, null, "me", null, ActingUser(me));

        Assert.Equal(mine.Id, Assert.Single(result.Items).Id);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task ListAsync_AssignedMeWhenCallerOwnsNone_ReturnsAnEmptyPage()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        dbContext.Set<NDR>().AddRange(Ndr(organizationId, Guid.NewGuid()), Ndr(organizationId, null));
        await dbContext.SaveChangesAsync();

        var result = await AllBranchesService(dbContext, organizationId).ListAsync(null, null, null, null, "me", null, ActingUser(Guid.NewGuid()));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task ListAsync_Unassigned_ReturnsOnlyNdrsWithoutAnOwner()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var unowned = Ndr(organizationId, null);
        dbContext.Set<NDR>().AddRange(unowned, Ndr(organizationId, Guid.NewGuid()));
        await dbContext.SaveChangesAsync();

        var result = await AllBranchesService(dbContext, organizationId).ListAsync(null, null, null, null, "Unassigned", null, ActingUser(Guid.NewGuid()));

        Assert.Equal(unowned.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task ListAsync_Reason_FiltersAndCombinesWithStatus()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var openWrongAddress = Ndr(organizationId, null, NdrReason.WrongAddress, NdrStatus.Open);
        dbContext.Set<NDR>().AddRange(
            openWrongAddress,
            Ndr(organizationId, null, NdrReason.WrongAddress, NdrStatus.Contacting),
            Ndr(organizationId, null, NdrReason.CustomerRefused, NdrStatus.Open));
        await dbContext.SaveChangesAsync();
        var service = AllBranchesService(dbContext, organizationId);

        var byReason = await service.ListAsync(null, null, null, null, null, NdrReason.WrongAddress, ActingUser(Guid.NewGuid()));
        var byReasonAndStatus = await service.ListAsync(NdrStatus.Open, null, null, null, null, NdrReason.WrongAddress, ActingUser(Guid.NewGuid()));

        Assert.Equal(2, byReason.TotalCount);
        Assert.Equal(openWrongAddress.Id, Assert.Single(byReasonAndStatus.Items).Id);
    }

    [Fact]
    public async Task ListAsync_InvalidAssigned_Throws()
    {
        await using var dbContext = CreateContext();

        await Assert.ThrowsAsync<NdrValidationException>(() =>
            AllBranchesService(dbContext, Guid.NewGuid()).ListAsync(null, null, null, null, "everyone", null, ActingUser(Guid.NewGuid())));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Ndr.Tests/Ndr.Tests.csproj`
Expected: FAIL to compile — `ListAsync` takes no `assigned`/`reason`.

- [ ] **Step 3: Implement the filters**

In `NdrService.Reads.cs`, change the signature and add the filters right after the existing `shipmentId` filter, before the `OrderByDescending` line:

```csharp
    public async Task<PagedResult<NdrResponse>> ListAsync(NdrStatus? status, int? page, int? pageSize, Guid? shipmentId, string? assigned, NdrReason? reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);

        var query = _dbContext.Set<NDR>().AsQueryable();
        if (status is not null)
        {
            query = query.Where(n => n.Status == status);
        }
        if (shipmentId is not null)
        {
            query = query.Where(n => n.ShipmentId == shipmentId);
        }
        if (reason is not null)
        {
            query = query.Where(n => n.Reason == reason);
        }
        // F15 R3b-1 quick filters, applied in SQL before paging and the scan-info step.
        switch (assigned?.Trim().ToLowerInvariant())
        {
            case null or "":
                break;
            case "me":
            {
                var callerId = actingUser.GetUserId()
                    ?? throw new NdrValidationException("Acting user could not be resolved.");
                query = query.Where(n => n.AssignedStaffUserId == callerId);
                break;
            }
            case "unassigned":
                query = query.Where(n => n.AssignedStaffUserId == null);
                break;
            default:
                throw new NdrValidationException("assigned must be 'me' or 'unassigned'.");
        }
        query = query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id);
```

The rest of the method (scope and paging) is unchanged.

- [ ] **Step 4: Route the list through `HandleAsync`**

In `NdrEndpoints.cs`, replace the list route:

```csharp
        group.MapGet("/", async (NdrStatus? status, int? page, int? pageSize, Guid? shipmentId, string? assigned, NdrReason? reason, NdrService service, HttpContext httpContext) =>
            await HandleAsync(() => service.ListAsync(status, page, pageSize, shipmentId, assigned, reason, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("ndr.read");
```

- [ ] **Step 5: Run the NDR unit tests**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Write the API tests**

Append to `NdrEndpointsTests`:

```csharp
    [Fact]
    public async Task GetNdrs_AssignedAndReasonFilters_RoundTrip()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "ndr-filters");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndFailDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);
        var ndr = await GetOpenNdrForShipmentAsync(client, shipment.Id);

        async Task<PagedResult<NdrResponse>> ListAsync(string query) =>
            (await (await client.GetAsync($"/api/v1/ndrs?{query}")).Content.ReadFromJsonAsync<PagedResult<NdrResponse>>())!;

        Assert.Contains((await ListAsync("assigned=unassigned")).Items, n => n.Id == ndr.Id);
        Assert.DoesNotContain((await ListAsync("assigned=me")).Items, n => n.Id == ndr.Id);

        // Any follow-up update makes the caller the owner (NdrService.UpdateAsync).
        var update = await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/update", new UpdateNdrRequest(NdrStatus.Contacting, "Called once", null, null));
        update.EnsureSuccessStatusCode();

        Assert.Contains((await ListAsync("assigned=me")).Items, n => n.Id == ndr.Id);
        Assert.DoesNotContain((await ListAsync("assigned=unassigned")).Items, n => n.Id == ndr.Id);
        Assert.Contains((await ListAsync("reason=CustomerUnavailable")).Items, n => n.Id == ndr.Id);
        Assert.DoesNotContain((await ListAsync("reason=WrongAddress")).Items, n => n.Id == ndr.Id);
    }

    [Fact]
    public async Task GetNdrs_InvalidFilters_Return400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "ndr-filters-400");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var badAssigned = await client.GetAsync("/api/v1/ndrs?assigned=everyone");
        var badReason = await client.GetAsync("/api/v1/ndrs?reason=Bogus");

        Assert.Equal(HttpStatusCode.BadRequest, badAssigned.StatusCode);
        Assert.Contains("NDR_VALIDATION_FAILED", await badAssigned.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, badReason.StatusCode);
    }
```

- [ ] **Step 7: Run the NDR API tests**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~NdrEndpointsTests"`
Expected: PASS.

- [ ] **Step 8: Build the whole solution**

Run: `dotnet build` (repo root)
Expected: 0 errors — confirms no other caller of the changed `ListAsync`/`GetByIdAsync`/`GetMissingShipmentsAsync` signatures was missed.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Ndr tests/Ndr.Tests tests/Api.Tests/NdrEndpointsTests.cs
git commit -m "$(cat <<'EOF'
feat(ndr): assigned=me|unassigned and reason filters on the NDR list

Both narrow in SQL within the existing branch scope and paging. An
invalid assigned value is a 400 NDR_VALIDATION_FAILED.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Shared web pieces — age helper, URL state, client paging, branch codes, PipelineStrip select/highlight, NDR/Returns switch

All web paths below are relative to `src/web/src`; run commands from `src/web`.

**Files:**
- Create: `lib/dates/age.ts`, `lib/dates/age.test.ts`
- Move: `features/cod/hooks/useFilterPage.ts` → `lib/hooks/useFilterPage.ts`, `features/cod/hooks/useFilterPage.test.ts` → `lib/hooks/useFilterPage.test.ts`
- Create: `lib/hooks/useSearchParamState.ts`, `lib/hooks/useSearchParamState.test.tsx`
- Create: `lib/paging/clientPage.ts`, `lib/paging/clientPage.test.ts`
- Create: `features/branches/lib/branchCode.ts`, `features/branches/lib/branchCode.test.ts`
- Modify: `components/page/PipelineStrip.tsx`, `components/page/page.test.tsx`
- Create: `features/ndr/components/NdrRtoSwitch.tsx`, `features/ndr/components/NdrRtoSwitch.test.tsx`
- Modify: `features/cod/lib/codFormat.ts`, `features/cod/lib/codFormat.test.ts`, `features/cod/components/RiderDepositsTab.tsx`, `features/cod/components/CollectionsTab.tsx`, `features/cod/components/BranchSettlementsTab.tsx`, `features/cod/components/AdjustmentsTab.tsx`, `features/reports/components/dashboard/NeedsActionCard.tsx`

**Interfaces:**
- Produces: `AGE_WARNING_HOURS = 24`, `ageHours(sinceIso: string, now?: Date): number`, `formatAge(sinceIso: string, now?: Date): string` from `@/lib/dates/age`; `useFilterPage(filterKey: string): [number, (page: number) => void]` from `@/lib/hooks/useFilterPage`; `useSearchParamState(): { params: URLSearchParams; update: (patch: Record<string, string | undefined>) => void }` from `@/lib/hooks/useSearchParamState`; `toClientPage<T>(items: T[], page: number, pageSize: number): PagedResult<T>` from `@/lib/paging/clientPage`; `branchCode(branches: Branch[] | undefined, id: string | null): string` from `@/features/branches/lib/branchCode`; `PipelineStrip` gains `highlightKey?: string`, `selectedKey?: string`, `onSelect?: (key: string) => void`; `NdrRtoSwitch({ active: "ndr" | "returns" })`.

- [ ] **Step 1: Write the failing tests**

Create `lib/dates/age.test.ts` (the COD held-time cases move here under the new names):

```ts
import { describe, expect, it } from "vitest";
import { AGE_WARNING_HOURS, ageHours, formatAge } from "./age";

describe("age", () => {
  const now = new Date("2026-10-07T12:00:00Z");

  it("computes hours and formats hours and days", () => {
    expect(ageHours("2026-10-07T09:00:00Z", now)).toBe(3);
    expect(formatAge("2026-10-07T11:30:00Z", now)).toBe("< 1 h");
    expect(formatAge("2026-10-07T05:00:00Z", now)).toBe("7 h");
    expect(formatAge("2026-10-05T16:00:00Z", now)).toBe("1 d 20 h");
    expect(formatAge("2026-10-05T12:00:00Z", now)).toBe("2 d");
  });

  it("never returns negative hours for clock skew", () => {
    expect(ageHours("2026-10-07T13:00:00Z", now)).toBe(0);
    expect(formatAge("2026-10-07T13:00:00Z", now)).toBe("< 1 h");
  });

  it("warns from 24 hours", () => {
    expect(AGE_WARNING_HOURS).toBe(24);
  });
});
```

Create `lib/hooks/useSearchParamState.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderHook } from "@testing-library/react";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/ndrs",
  useSearchParams: () => new URLSearchParams(search),
}));

import { useSearchParamState } from "./useSearchParamState";

beforeEach(() => {
  replace.mockReset();
  search = "";
});

describe("useSearchParamState", () => {
  it("reads params and writes a merged patch without scrolling", () => {
    search = "status=Open&reason=WrongAddress";
    const { result } = renderHook(() => useSearchParamState());
    expect(result.current.params.get("status")).toBe("Open");

    result.current.update({ assigned: "me", reason: undefined });
    expect(replace).toHaveBeenCalledWith("/ndrs?status=Open&assigned=me", { scroll: false });
  });

  it("drops the query string entirely when nothing is left", () => {
    search = "status=Open";
    const { result } = renderHook(() => useSearchParamState());
    result.current.update({ status: undefined });
    expect(replace).toHaveBeenCalledWith("/ndrs", { scroll: false });
  });
});
```

Create `lib/paging/clientPage.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { toClientPage } from "./clientPage";

describe("toClientPage", () => {
  const items = Array.from({ length: 45 }, (_, i) => i + 1);

  it("slices one page and reports totals", () => {
    expect(toClientPage(items, 2, 20)).toEqual({ items: items.slice(20, 40), page: 2, pageSize: 20, totalCount: 45, totalPages: 3 });
  });

  it("clamps a page past the end to the last page", () => {
    expect(toClientPage(items, 9, 20).page).toBe(3);
    expect(toClientPage(items, 9, 20).items).toEqual([41, 42, 43, 44, 45]);
  });

  it("returns one empty page for an empty list", () => {
    expect(toClientPage([], 1, 20)).toEqual({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 1 });
  });
});
```

Create `features/branches/lib/branchCode.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { branchCode } from "./branchCode";
import type { Branch } from "@/features/branches/types";

const branches = [
  { id: "b1", code: "KTM", name: "Kathmandu Hub" },
  { id: "b2", code: "  ", name: "Pokhara Hub" },
] as Branch[];

describe("branchCode", () => {
  it("prefers the code, falls back to the name, and shows a dash when unknown", () => {
    expect(branchCode(branches, "b1")).toBe("KTM");
    expect(branchCode(branches, "b2")).toBe("Pokhara Hub");
    expect(branchCode(branches, "missing")).toBe("—");
    expect(branchCode(branches, null)).toBe("—");
    expect(branchCode(undefined, "b1")).toBe("—");
  });
});
```

Append to `components/page/page.test.tsx` (add `fireEvent` and `vi` to the existing `@testing-library/react` / `vitest` imports):

```tsx
describe("PipelineStrip selection", () => {
  const stages = [
    { key: "Open", label: "Open", count: 4, token: "status-ndr" },
    { key: "Resolved", label: "Resolved", count: null, token: "status-delivered" },
  ];

  it("renders buttons when selectable, marks the highlight and the selection, and reports clicks", () => {
    const onSelect = vi.fn();
    render(<PipelineStrip stages={stages} ariaLabel="NDR status" highlightKey="Open" selectedKey="Resolved" onSelect={onSelect} />);
    const open = screen.getByRole("button", { name: /Open/ });
    expect(open).toHaveAttribute("aria-current", "step");
    expect(open).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByRole("button", { name: /Resolved/ })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByText("—")).toBeInTheDocument();
    fireEvent.click(open);
    expect(onSelect).toHaveBeenCalledWith("Open");
    expect(screen.queryAllByRole("link")).toHaveLength(0);
  });
});
```

Create `features/ndr/components/NdrRtoSwitch.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));

import { NdrRtoSwitch } from "./NdrRtoSwitch";

describe("NdrRtoSwitch", () => {
  it("shows both tabs with the active one selected when both reads are allowed", () => {
    granted = ["ndr.read", "rto.read"];
    render(<NdrRtoSwitch active="returns" />);
    expect(screen.getByRole("tab", { name: "Returns" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "NDR queue" })).toHaveAttribute("href", "/ndrs");
  });

  it("renders nothing when only one side is permitted", () => {
    granted = ["ndr.read"];
    const { container } = render(<NdrRtoSwitch active="ndr" />);
    expect(container).toBeEmptyDOMElement();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npx vitest run src/lib/dates src/lib/hooks/useSearchParamState.test.tsx src/lib/paging src/features/branches/lib src/components/page src/features/ndr/components/NdrRtoSwitch.test.tsx`
Expected: FAIL — modules `./age`, `./useSearchParamState`, `./clientPage`, `./branchCode`, `./NdrRtoSwitch` not found; PipelineStrip selection test fails (no buttons).

- [ ] **Step 3: Implement the helpers**

Create `lib/dates/age.ts`:

```ts
/** Hours after which an open item (rider-held cash, an NDR) counts as overdue. */
export const AGE_WARNING_HOURS = 24;

export function ageHours(sinceIso: string, now: Date = new Date()): number {
  return Math.max(0, (now.getTime() - Date.parse(sinceIso)) / 3_600_000);
}

/** "< 1 h", "7 h", "1 d 20 h", "2 d". */
export function formatAge(sinceIso: string, now: Date = new Date()): string {
  const hours = Math.floor(ageHours(sinceIso, now));
  if (hours < 1) return "< 1 h";
  if (hours < 24) return `${hours} h`;
  const days = Math.floor(hours / 24);
  const rest = hours % 24;
  return rest === 0 ? `${days} d` : `${days} d ${rest} h`;
}
```

Create `lib/hooks/useSearchParamState.ts`:

```ts
"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";

/** Page filters live in the URL: read with `params.get`, write a merged patch (undefined deletes). */
export function useSearchParamState() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();

  function update(patch: Record<string, string | undefined>) {
    const next = new URLSearchParams(params.toString());
    for (const [key, value] of Object.entries(patch)) {
      if (value === undefined) next.delete(key);
      else next.set(key, value);
    }
    const qs = next.toString();
    router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
  }

  return { params, update };
}
```

Create `lib/paging/clientPage.ts`:

```ts
import type { PagedResult } from "@/components/data-table/DataTable";

/** One page of an array the API serves whole (manifests, returns), in DataTable's shape. */
export function toClientPage<T>(items: T[], page: number, pageSize: number): PagedResult<T> {
  const totalPages = Math.max(1, Math.ceil(items.length / pageSize));
  const current = Math.min(Math.max(1, page), totalPages);
  return {
    items: items.slice((current - 1) * pageSize, current * pageSize),
    page: current,
    pageSize,
    totalCount: items.length,
    totalPages,
  };
}
```

Create `features/branches/lib/branchCode.ts`:

```ts
import type { Branch } from "@/features/branches/types";

/** Short route label: the branch code, else its name, else "—". */
export function branchCode(branches: Branch[] | undefined, id: string | null): string {
  if (!id) return "—";
  const branch = branches?.find((b) => b.id === id);
  if (!branch) return "—";
  return branch.code?.trim() ? branch.code : branch.name;
}
```

- [ ] **Step 4: Move `useFilterPage` and the age helpers out of COD**

```bash
git mv src/features/cod/hooks/useFilterPage.ts src/lib/hooks/useFilterPage.ts
git mv src/features/cod/hooks/useFilterPage.test.ts src/lib/hooks/useFilterPage.test.ts
```

In `CollectionsTab.tsx`, `BranchSettlementsTab.tsx`, `AdjustmentsTab.tsx` and `RiderDepositsTab.tsx`, change `@/features/cod/hooks/useFilterPage` to `@/lib/hooks/useFilterPage`.

In `features/cod/lib/codFormat.ts`, delete `HELD_WARNING_HOURS`, `hoursHeld` and `formatHeldFor` (keep `sortHoldings` and everything else).

In `features/cod/lib/codFormat.test.ts`, change the import to `import { collectionLabel, describeVariance, formatRs, sortHoldings } from "./codFormat";`, delete the two `it` blocks "computes hours and formats days" and "never returns negative hours for clock skew", and rename `describe("held time", …)` to `describe("sortHoldings", …)` (it keeps "sorts longest-held first"; drop its now-unused `now` const).

In `RiderDepositsTab.tsx`, replace the codFormat import with:

```ts
import { formatRs, formatTime, formatDate, sortHoldings } from "@/features/cod/lib/codFormat";
import { AGE_WARNING_HOURS, ageHours, formatAge } from "@/lib/dates/age";
```

and rename the uses: `hoursHeld(` → `ageHours(`, `HELD_WARNING_HOURS` → `AGE_WARNING_HOURS`, `formatHeldFor(` → `formatAge(`.

In `NeedsActionCard.tsx`, replace `import { HELD_WARNING_HOURS, hoursHeld } from "@/features/cod/lib/codFormat";` with `import { AGE_WARNING_HOURS, ageHours } from "@/lib/dates/age";` and rename the two uses the same way.

Confirm nothing else references the old names: `grep -rn "hoursHeld\|formatHeldFor\|HELD_WARNING_HOURS\|cod/hooks/useFilterPage" src` → no output.

- [ ] **Step 5: Extend `PipelineStrip`**

Replace `components/page/PipelineStrip.tsx`'s `PipelineStrip` export (keep `PipelineStage`, `TILE` and `StageBody` as they are) and add the `cn` import:

```tsx
import { cn } from "@/lib/utils";
```

```tsx
/**
 * `onSelect` turns every stage into a filter button (`selectedKey` = the active filter, announced
 * with aria-pressed). `highlightKey` marks the stage that needs attention (aria-current="step";
 * each page owns its pure highlight rule). Without `onSelect`, stages link via `href` as before.
 */
export function PipelineStrip({
  stages,
  ariaLabel,
  highlightKey,
  selectedKey,
  onSelect,
}: {
  stages: PipelineStage[];
  ariaLabel: string;
  highlightKey?: string;
  selectedKey?: string;
  onSelect?: (key: string) => void;
}) {
  return (
    <ol aria-label={ariaLabel} className="grid grid-cols-2 gap-2.5 sm:grid-cols-4 xl:auto-cols-fr xl:grid-flow-col xl:grid-cols-none">
      {stages.map((stage) => {
        const highlighted = stage.key === highlightKey;
        const tile = cn(TILE, highlighted && "border-primary ring-1 ring-primary");
        return (
          <li key={stage.key}>
            {onSelect ? (
              <button
                type="button"
                onClick={() => onSelect(stage.key)}
                aria-pressed={stage.key === selectedKey}
                aria-current={highlighted ? "step" : undefined}
                className={cn(tile, "w-full text-left hover:bg-accent", stage.key === selectedKey && "bg-accent")}
              >
                <StageBody stage={stage} />
              </button>
            ) : stage.href ? (
              <Link href={stage.href} aria-current={highlighted ? "step" : undefined} className={cn(tile, "hover:bg-accent")}>
                <StageBody stage={stage} />
              </Link>
            ) : (
              <div className={tile} aria-current={highlighted ? "step" : undefined}>
                <StageBody stage={stage} />
              </div>
            )}
          </li>
        );
      })}
    </ol>
  );
}
```

- [ ] **Step 6: Add the NDR/Returns switch**

Create `features/ndr/components/NdrRtoSwitch.tsx`:

```tsx
"use client";

import Link from "next/link";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { cn } from "@/lib/utils";

const LINKS = [
  { key: "ndr", href: "/ndrs", label: "NDR queue" },
  { key: "returns", href: "/return-shipments", label: "Returns" },
] as const;

// Segmented switch between the two halves of "NDR & RTO". Hidden unless the viewer can read both
// sides — a one-option switch is noise.
export function NdrRtoSwitch({ active }: { active: "ndr" | "returns" }) {
  const canReadNdr = usePermission("ndr.read");
  const canReadRto = usePermission("rto.read");
  if (!canReadNdr || !canReadRto) return null;

  return (
    <div role="tablist" aria-label="NDR and returns" className="inline-flex rounded-lg border border-border bg-muted p-0.5">
      {LINKS.map((link) => (
        <Link
          key={link.key}
          href={link.href}
          role="tab"
          aria-selected={active === link.key}
          className={cn(
            "rounded-md px-3 py-1.5 text-sm font-medium transition-colors",
            active === link.key ? "bg-background text-foreground shadow-sm" : "text-muted-foreground hover:text-foreground",
          )}
        >
          {link.label}
        </Link>
      ))}
    </div>
  );
}
```

- [ ] **Step 7: Run the tests**

Run: `npx vitest run src/lib src/features/branches src/components/page src/features/ndr/components/NdrRtoSwitch.test.tsx src/features/cod src/features/reports`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add -A src/lib src/features/branches/lib src/components/page src/features/ndr/components/NdrRtoSwitch.tsx src/features/ndr/components/NdrRtoSwitch.test.tsx src/features/cod src/features/reports
git commit -m "$(cat <<'EOF'
feat(web): shared age, URL-state, client paging and branch-code helpers

formatAge/ageHours/AGE_WARNING_HOURS move from COD to lib/dates/age and
useFilterPage to lib/hooks (COD imports updated, no duplicate helpers).
PipelineStrip gains selectable stages with a highlight; new NdrRtoSwitch.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Web data layer — types, proxy routes, hooks, page models, panel store

**Files:**
- Modify: `features/manifests/types.ts`, `features/manifests/hooks/useManifests.ts`, `features/manifests/store/manifestPanelStore.ts`, `features/manifests/store/manifestPanelStore.test.ts`
- Create: `features/manifests/hooks/useManifestShipments.ts`, `features/manifests/hooks/useManifests.test.tsx`
- Create: `features/manifests/lib/manifestModel.ts`, `features/manifests/lib/manifestModel.test.ts`
- Create: `app/api/manifests/[id]/shipments/route.ts`, `app/api/manifests/[id]/shipments/route.test.ts`
- Modify: `features/ndr/types.ts`, `features/ndr/hooks/useNdrs.ts`
- Create: `features/ndr/hooks/useDeliveryAttempts.ts`, `features/ndr/hooks/useNdrs.test.tsx`
- Create: `features/ndr/lib/ndrModel.ts`, `features/ndr/lib/ndrModel.test.ts`, `features/ndr/lib/returnModel.ts`, `features/ndr/lib/returnModel.test.ts`
- Create: `app/api/shipments/[awb]/delivery-attempts/route.ts`, `app/api/shipments/[awb]/delivery-attempts/route.test.ts`
- Modify: every test fixture typed `Manifest` that `npx tsc --noEmit` flags for a missing `receivedCount`

**Interfaces:**
- Consumes: HTTP from Tasks 1–4.
- Produces:
  - `Manifest.receivedCount: number`; `type ManifestDirection = "incoming" | "outgoing"`.
  - `useManifests(branchId?: string, status?: string, options?: { enabled?: boolean; direction?: ManifestDirection })` — query key `["manifests", branchId ?? null, status ?? null, direction ?? null]`.
  - `useManifestShipments(manifestId: string, enabled: boolean)` → `ManifestShipmentScan[]`, key `["manifests", manifestId, "shipments"]`.
  - `useManifestPanelStore.openDetail(id: string, direction?: ManifestDirection)`; detail state `{ mode: "detail"; id: string; direction?: ManifestDirection }`.
  - `manifestModel`: `MANIFEST_STAGES`, `ManifestTab`, `parseManifestTab`, `parseManifestStatus`, `manifestStageLabel`, `pickManifestHighlight`, `parcelsSummary`, `stepperState`.
  - `DeliveryAttempt` type; `useDeliveryAttempts(shipmentId: string)` → `{ items: DeliveryAttempt[] }`, key `["delivery-attempts", shipmentId]`.
  - `NdrAssignedFilter = "me" | "unassigned"`; `useNdrs({ status?, shipmentId?, assigned?, reason?, page?, pageSize? })`, key `["ndrs", status, shipmentId, assigned, reason, page, pageSize]` (nulls for missing).
  - `ndrModel`: `NDR_STAGES`, `NDR_REASONS`, `ndrReasonLabel`, `parseNdrStatus`, `parseNdrReason`, `parseAssigned`, `isFollowUpOverdue`, `ownerLabel`, `NEXT_NDR_STATUSES`, `ndrStatusLabel`.
  - `returnModel`: `RETURN_STAGES`, `ReturnStageKey`, `parseReturnStage`, `matchesReturnStage`, `returnStageCount`, `canHandBack`.

- [ ] **Step 1: Write the failing tests**

Create `features/manifests/lib/manifestModel.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { manifestStageLabel, parcelsSummary, parseManifestStatus, parseManifestTab, pickManifestHighlight, stepperState } from "./manifestModel";

describe("pickManifestHighlight", () => {
  it("picks the open stage with the most manifests, ignoring Closed", () => {
    expect(pickManifestHighlight({ Draft: 1, Sealed: 2, InTransit: 5, Received: 3, Closed: 40 })).toBe("InTransit");
  });

  it("breaks ties to the left and treats missing keys as 0", () => {
    expect(pickManifestHighlight({ Sealed: 2, Received: 2 })).toBe("Sealed");
  });

  it("returns nothing when every open stage is empty", () => {
    expect(pickManifestHighlight({ Closed: 9 })).toBeUndefined();
    expect(pickManifestHighlight({})).toBeUndefined();
  });
});

describe("parsers and labels", () => {
  it("defaults the tab to incoming and rejects unknown statuses", () => {
    expect(parseManifestTab(null)).toBe("incoming");
    expect(parseManifestTab("outgoing")).toBe("outgoing");
    expect(parseManifestTab("all")).toBe("all");
    expect(parseManifestTab("bogus")).toBe("incoming");
    expect(parseManifestStatus("Received")).toBe("Received");
    expect(parseManifestStatus("Dispatched")).toBeUndefined();
    expect(manifestStageLabel("Received")).toBe("Receiving");
  });
});

describe("parcelsSummary", () => {
  it("shows received of total and the missing count once receiving has started", () => {
    expect(parcelsSummary({ status: "Received", shipmentCount: 120, receivedCount: 118 })).toEqual({ primary: "118 / 120", missing: 2 });
    expect(parcelsSummary({ status: "Closed", shipmentCount: 3, receivedCount: 3 })).toEqual({ primary: "3 / 3", missing: 0 });
  });

  it("shows the parcel total before receiving", () => {
    expect(parcelsSummary({ status: "InTransit", shipmentCount: 120, receivedCount: 0 })).toEqual({ primary: "120 parcels", missing: 0 });
    expect(parcelsSummary({ status: "Draft", shipmentCount: 1, receivedCount: 0 })).toEqual({ primary: "1 parcel", missing: 0 });
  });
});

describe("stepperState", () => {
  it("marks earlier stages complete, the current one current and later ones upcoming", () => {
    expect(stepperState("InTransit", "Sealed")).toBe("complete");
    expect(stepperState("InTransit", "InTransit")).toBe("current");
    expect(stepperState("InTransit", "Closed")).toBe("upcoming");
  });

  it("treats every stage of a closed manifest as complete", () => {
    expect(stepperState("Closed", "Closed")).toBe("complete");
    expect(stepperState("Closed", "Draft")).toBe("complete");
  });
});
```

Create `features/ndr/lib/ndrModel.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { NEXT_NDR_STATUSES, isFollowUpOverdue, ndrReasonLabel, ownerLabel, parseAssigned, parseNdrReason, parseNdrStatus } from "./ndrModel";

describe("ndrModel", () => {
  it("labels reasons for people", () => {
    expect(ndrReasonLabel("CustomerRequestedReschedule")).toBe("Customer asked to reschedule");
    expect(ndrReasonLabel("CashUnavailable")).toBe("Cash not ready");
  });

  it("parses URL values and rejects unknown ones", () => {
    expect(parseNdrStatus("Open")).toBe("Open");
    expect(parseNdrStatus("Closed")).toBeUndefined();
    expect(parseNdrReason("WrongAddress")).toBe("WrongAddress");
    expect(parseNdrReason("Lost")).toBeUndefined();
    expect(parseAssigned("me")).toBe("me");
    expect(parseAssigned("unassigned")).toBe("unassigned");
    expect(parseAssigned("everyone")).toBeUndefined();
  });

  it("flags follow-ups in the past only for open NDRs", () => {
    const now = new Date("2026-10-15T12:00:00Z");
    expect(isFollowUpOverdue({ followUpAt: "2026-10-15T11:00:00Z", status: "Contacting" }, now)).toBe(true);
    expect(isFollowUpOverdue({ followUpAt: "2026-10-15T13:00:00Z", status: "Contacting" }, now)).toBe(false);
    expect(isFollowUpOverdue({ followUpAt: "2026-10-15T11:00:00Z", status: "Resolved" }, now)).toBe(false);
    expect(isFollowUpOverdue({ followUpAt: null, status: "Open" }, now)).toBe(false);
  });

  it("names the owner", () => {
    const userName = (id: string | null) => (id === "u2" ? "Sita Rai" : "Staff");
    expect(ownerLabel("u1", "u1", userName)).toBe("You");
    expect(ownerLabel("u2", "u1", userName)).toBe("Sita Rai");
    expect(ownerLabel(null, "u1", userName)).toBe("Unassigned");
  });

  it("lists only the backend's legal forward moves", () => {
    expect(NEXT_NDR_STATUSES.Open).toEqual(["Contacting"]);
    expect(NEXT_NDR_STATUSES.CustomerConfirmed).toEqual(["ReattemptScheduled", "RtoRequested"]);
    expect(NEXT_NDR_STATUSES.ReattemptScheduled).toEqual([]);
  });
});
```

Create `features/ndr/lib/returnModel.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { canHandBack, matchesReturnStage, parseReturnStage, returnStageCount } from "./returnModel";
import type { ReturnShipment } from "@/features/ndr/types";

const base: ReturnShipment = {
  id: "r1", originalShipmentId: "s1", originalAwb: "KTM-1", returnAwb: "RTO-20261015-001", rtoReason: "Refused",
  originBranchId: "b2", destinationBranchId: "b1", currentStatus: "RtoAtOrigin", currentBranchId: "b1", merchantConfirmedAt: null,
};

describe("returnModel", () => {
  it("parses stages", () => {
    expect(parseReturnStage("RtoAtOrigin")).toBe("RtoAtOrigin");
    expect(parseReturnStage("PendingConfirmation")).toBe("PendingConfirmation");
    expect(parseReturnStage("Delivered")).toBeUndefined();
  });

  it("matches rows to a stage; pending confirmation means not yet confirmed", () => {
    expect(matchesReturnStage(base, "RtoAtOrigin")).toBe(true);
    expect(matchesReturnStage(base, "RtoInTransit")).toBe(false);
    expect(matchesReturnStage(base, "PendingConfirmation")).toBe(true);
    expect(matchesReturnStage({ ...base, merchantConfirmedAt: "2026-10-15T00:00:00Z" }, "PendingConfirmation")).toBe(false);
  });

  it("reads stage counts from the summary", () => {
    const summary = { byRtoStage: { RtoAtOrigin: 3 }, merchantConfirmed: 1, pendingConfirmation: 7 };
    expect(returnStageCount(summary, "RtoAtOrigin")).toBe(3);
    expect(returnStageCount(summary, "RtoInitiated")).toBe(0);
    expect(returnStageCount(summary, "PendingConfirmation")).toBe(7);
  });

  it("allows hand-back only at origin and before confirmation", () => {
    expect(canHandBack(base)).toBe(true);
    expect(canHandBack({ ...base, currentStatus: "RtoDelivered" })).toBe(false);
    expect(canHandBack({ ...base, merchantConfirmedAt: "2026-10-15T00:00:00Z" })).toBe(false);
  });
});
```

Create `features/manifests/hooks/useManifests.test.tsx`:

```tsx
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useManifests } from "./useManifests";
import { useManifestShipments } from "./useManifestShipments";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

afterEach(() => vi.restoreAllMocks());

describe("manifest hooks", () => {
  it("sends branch, status and direction to the list route", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] });
    const { result } = renderHook(() => useManifests("b1", "Received", { direction: "incoming" }), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/manifests?branchId=b1&status=Received&direction=incoming", undefined);
  });

  it("omits direction when none is given", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] });
    const { result } = renderHook(() => useManifests(undefined, undefined), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/manifests", undefined);
  });

  it("reads a manifest's parcels only when enabled", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] });
    const { result } = renderHook(() => useManifestShipments("m1", true), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/manifests/m1/shipments", undefined);
  });
});
```

Create `features/ndr/hooks/useNdrs.test.tsx`:

```tsx
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useNdrs } from "./useNdrs";
import { useDeliveryAttempts } from "./useDeliveryAttempts";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

afterEach(() => vi.restoreAllMocks());

describe("NDR hooks", () => {
  it("sends the quick filters to the list route", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }) });
    const { result } = renderHook(() => useNdrs({ status: "Open", assigned: "me", reason: "WrongAddress", page: 2, pageSize: 20 }), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/ndrs?status=Open&assigned=me&reason=WrongAddress&page=2&pageSize=20", undefined);
  });

  it("reads delivery attempts by shipment id and stays idle without one", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ items: [] }) });
    const idle = renderHook(() => useDeliveryAttempts(""), { wrapper });
    expect(idle.result.current.fetchStatus).toBe("idle");
    const { result } = renderHook(() => useDeliveryAttempts("s1"), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/shipments/s1/delivery-attempts", undefined);
  });
});
```

Create `app/api/manifests/[id]/shipments/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/manifests/[id]/shipments", () => {
  it("proxies to the backend parcel list", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => [] } as unknown as Response);
    const response = await GET(new Request("http://localhost/api/manifests/m1/shipments"), { params: { id: "m1" } });
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/manifests/m1/shipments");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new Request("http://localhost/api/manifests/m1/shipments"), { params: { id: "m1" } });
    expect(response.status).toBe(401);
  });
});
```

Create `app/api/shipments/[awb]/delivery-attempts/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/shipments/[awb]/delivery-attempts", () => {
  it("proxies the shipment id to the backend attempts read", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => ({ items: [] }) } as unknown as Response);
    const response = await GET(new Request("http://localhost/api/shipments/s1/delivery-attempts"), { params: { awb: "s1" } });
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/shipments/s1/delivery-attempts");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new Request("http://localhost/api/shipments/s1/delivery-attempts"), { params: { awb: "s1" } });
    expect(response.status).toBe(401);
  });
});
```

In `features/manifests/store/manifestPanelStore.test.ts`, add:

```ts
  it("openDetail carries the direction relative to the page branch", () => {
    useManifestPanelStore.getState().openDetail("m1", "incoming");
    expect(useManifestPanelStore.getState().panel).toEqual({ mode: "detail", id: "m1", direction: "incoming" });
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npx vitest run src/features/manifests src/features/ndr/lib src/features/ndr/hooks src/app/api/manifests src/app/api/shipments`
Expected: FAIL — models, `useManifestShipments`, `useDeliveryAttempts` and both routes missing; `useManifests` ignores `direction`; `useNdrs` ignores `assigned`/`reason`; store drops `direction`.

- [ ] **Step 3: Types**

In `features/manifests/types.ts`, add `receivedCount: number;` to `Manifest` (after `shipmentCount`) and append:

```ts
export type ManifestDirection = "incoming" | "outgoing";
```

In `features/ndr/types.ts`, append:

```ts
export type NdrAssignedFilter = "me" | "unassigned";

// GET /api/v1/shipments/{shipmentId}/delivery-attempts. riderId is the Rider id (usePeopleNames().riderName).
export interface DeliveryAttempt {
  attemptNumber: number;
  attemptedAt: string;
  succeeded: boolean;
  reason: NdrReason | null;
  notes: string | null;
  customerContacted: boolean;
  nextAction: string | null;
  riderId: string;
}
```

- [ ] **Step 4: Proxy routes**

Create `app/api/manifests/[id]/shipments/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/manifests/${params.id}/shipments`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `app/api/shipments/[awb]/delivery-attempts/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

// The segment is named [awb] because Next.js allows one dynamic name per level and the sibling
// /api/shipments/[awb] routes already use it; here it carries the shipment id.
export async function GET(_request: Request, { params }: { params: { awb: string } }) {
  try {
    const response = await apiFetch(`/api/v1/shipments/${encodeURIComponent(params.awb)}/delivery-attempts`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 5: Hooks and store**

Replace `features/manifests/hooks/useManifests.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest, ManifestDirection } from "@/features/manifests/types";

export function useManifests(
  branchId?: string,
  status?: string,
  options: { enabled?: boolean; direction?: ManifestDirection } = {},
) {
  const { enabled = true, direction } = options;
  const params = new URLSearchParams();
  if (branchId) params.set("branchId", branchId);
  if (status) params.set("status", status);
  if (direction) params.set("direction", direction);
  const query = params.toString();

  return useQuery({
    queryKey: ["manifests", branchId ?? null, status ?? null, direction ?? null],
    queryFn: () => clientFetch<Manifest[]>(`/api/manifests${query ? `?${query}` : ""}`),
    enabled,
  });
}
```

Create `features/manifests/hooks/useManifestShipments.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ManifestShipmentScan } from "@/features/manifests/types";

// Under ["manifests", id, …] so receive-scan and close (which invalidate ["manifests"]) refresh it.
export function useManifestShipments(manifestId: string, enabled: boolean) {
  return useQuery({
    queryKey: ["manifests", manifestId, "shipments"],
    queryFn: () => clientFetch<ManifestShipmentScan[]>(`/api/manifests/${manifestId}/shipments`),
    enabled: enabled && manifestId !== "",
  });
}
```

Replace `features/ndr/hooks/useNdrs.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Ndr, NdrAssignedFilter, NdrReason, NdrStatus } from "@/features/ndr/types";

export function useNdrs(
  params: { status?: NdrStatus; shipmentId?: string; assigned?: NdrAssignedFilter; reason?: NdrReason; page?: number; pageSize?: number } = {},
) {
  const { status, shipmentId, assigned, reason, page, pageSize } = params;
  const query = new URLSearchParams();
  if (status) query.set("status", status);
  if (shipmentId) query.set("shipmentId", shipmentId);
  if (assigned) query.set("assigned", assigned);
  if (reason) query.set("reason", reason);
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["ndrs", status ?? null, shipmentId ?? null, assigned ?? null, reason ?? null, page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<Ndr>>(`/api/ndrs${search ? `?${search}` : ""}`),
  });
}
```

Create `features/ndr/hooks/useDeliveryAttempts.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { DeliveryAttempt } from "@/features/ndr/types";

export function useDeliveryAttempts(shipmentId: string) {
  return useQuery({
    queryKey: ["delivery-attempts", shipmentId],
    queryFn: () => clientFetch<{ items: DeliveryAttempt[] }>(`/api/shipments/${encodeURIComponent(shipmentId)}/delivery-attempts`),
    enabled: shipmentId !== "",
  });
}
```

Replace `features/manifests/store/manifestPanelStore.ts`:

```ts
import { create } from "zustand";
import type { ManifestDirection } from "@/features/manifests/types";

type ManifestPanelState =
  | { mode: "closed" }
  | { mode: "create" }
  | { mode: "detail"; id: string; direction?: ManifestDirection };

interface ManifestPanelStore {
  panel: ManifestPanelState;
  openCreate: () => void;
  openDetail: (id: string, direction?: ManifestDirection) => void;
  close: () => void;
}

// Global so any component in the (staff) tree can open the manifest sheet without routing to a
// separate page (docs/ARCHITECTURE.md §9a). Keyed by the manifest's Guid id, not Code — there is
// no lookup-by-code endpoint, only GET /{id}. `direction` is relative to the page's branch filter
// (F15 R3b-1 Sheet eyebrow); absent when opened from elsewhere.
export const useManifestPanelStore = create<ManifestPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  openDetail: (id, direction) => set({ panel: direction ? { mode: "detail", id, direction } : { mode: "detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

- [ ] **Step 6: Page models**

Create `features/manifests/lib/manifestModel.ts`:

```ts
import type { ManifestStatus } from "@/features/manifests/types";

// Five stages, labelled as the backend behaves: there is no "Dispatched" status (dispatch moves
// Sealed → InTransit) and Received means receiving has started.
export const MANIFEST_STAGES: { status: ManifestStatus; label: string; token: string }[] = [
  { status: "Draft", label: "Draft", token: "status-created" },
  { status: "Sealed", label: "Sealed", token: "status-sealed" },
  { status: "InTransit", label: "In transit", token: "status-in-transit" },
  { status: "Received", label: "Receiving", token: "status-received" },
  { status: "Closed", label: "Closed", token: "status-delivered" },
];

const ORDER = MANIFEST_STAGES.map((s) => s.status);

export type ManifestTab = "incoming" | "outgoing" | "all";

export function parseManifestTab(value: string | null): ManifestTab {
  return value === "outgoing" || value === "all" ? value : "incoming";
}

export function parseManifestStatus(value: string | null): ManifestStatus | undefined {
  return ORDER.find((s) => s === value);
}

export function manifestStageLabel(status: ManifestStatus): string {
  return MANIFEST_STAGES.find((s) => s.status === status)?.label ?? status;
}

/** The open stage holding the most manifests (Closed excluded); ties go left; none when all 0. */
export function pickManifestHighlight(counts: Record<string, number>): ManifestStatus | undefined {
  let best: ManifestStatus | undefined;
  let bestCount = 0;
  for (const status of ORDER) {
    if (status === "Closed") continue;
    const count = counts[status] ?? 0;
    if (count > bestCount) {
      best = status;
      bestCount = count;
    }
  }
  return best;
}

/** "118 / 120" (+ missing) once receiving has started; "120 parcels" before. */
export function parcelsSummary(m: { status: ManifestStatus; shipmentCount: number; receivedCount: number }): { primary: string; missing: number } {
  if (m.status === "Received" || m.status === "Closed") {
    return { primary: `${m.receivedCount} / ${m.shipmentCount}`, missing: Math.max(0, m.shipmentCount - m.receivedCount) };
  }
  return { primary: `${m.shipmentCount} ${m.shipmentCount === 1 ? "parcel" : "parcels"}`, missing: 0 };
}

export type StepState = "complete" | "current" | "upcoming";

export function stepperState(current: ManifestStatus, step: ManifestStatus): StepState {
  if (current === "Closed") return "complete";
  const currentIndex = ORDER.indexOf(current);
  const stepIndex = ORDER.indexOf(step);
  if (stepIndex < currentIndex) return "complete";
  return stepIndex === currentIndex ? "current" : "upcoming";
}
```

Create `features/ndr/lib/ndrModel.ts`:

```ts
import type { NdrAssignedFilter, NdrReason, NdrStatus } from "@/features/ndr/types";

export const NDR_STAGES: { status: NdrStatus; label: string; token: string }[] = [
  { status: "Open", label: "Open", token: "status-ndr" },
  { status: "Contacting", label: "Contacting", token: "status-out-for-delivery" },
  { status: "CustomerConfirmed", label: "Customer confirmed", token: "status-received" },
  { status: "ReattemptScheduled", label: "Reattempt scheduled", token: "status-sealed" },
  { status: "RtoRequested", label: "RTO requested", token: "status-rto" },
  { status: "Resolved", label: "Resolved", token: "status-delivered" },
];

const REASON_LABELS: Record<NdrReason, string> = {
  CustomerUnavailable: "Customer unavailable",
  PhoneUnreachable: "Phone unreachable",
  WrongAddress: "Wrong address",
  IncompleteAddress: "Incomplete address",
  CustomerRefused: "Customer refused",
  CustomerRequestedReschedule: "Customer asked to reschedule",
  CashUnavailable: "Cash not ready",
  DamagedParcel: "Damaged parcel",
  Other: "Other",
};

export const NDR_REASONS = Object.keys(REASON_LABELS) as NdrReason[];

export function ndrReasonLabel(reason: NdrReason): string {
  return REASON_LABELS[reason] ?? reason;
}

export function ndrStatusLabel(status: NdrStatus): string {
  return NDR_STAGES.find((s) => s.status === status)?.label ?? status;
}

export function parseNdrStatus(value: string | null): NdrStatus | undefined {
  return NDR_STAGES.find((s) => s.status === value)?.status;
}

export function parseNdrReason(value: string | null): NdrReason | undefined {
  return NDR_REASONS.find((r) => r === value);
}

export function parseAssigned(value: string | null): NdrAssignedFilter | undefined {
  return value === "me" || value === "unassigned" ? value : undefined;
}

export function isFollowUpOverdue(ndr: { followUpAt: string | null; status: NdrStatus }, now: Date = new Date()): boolean {
  return ndr.status !== "Resolved" && ndr.followUpAt !== null && Date.parse(ndr.followUpAt) < now.getTime();
}

export function ownerLabel(ownerId: string | null, myUserId: string | undefined, userName: (id: string | null) => string): string {
  if (!ownerId) return "Unassigned";
  return ownerId === myUserId ? "You" : userName(ownerId);
}

// Mirrors NdrService.IsLegalForwardMove: one forward step at a time, CustomerConfirmed branches.
export const NEXT_NDR_STATUSES: Record<NdrStatus, NdrStatus[]> = {
  Open: ["Contacting"],
  Contacting: ["CustomerConfirmed"],
  CustomerConfirmed: ["ReattemptScheduled", "RtoRequested"],
  ReattemptScheduled: [],
  RtoRequested: [],
  Resolved: [],
};
```

Create `features/ndr/lib/returnModel.ts`:

```ts
import type { ReturnShipment } from "@/features/ndr/types";
import type { ReturnShipmentSummary } from "@/features/ndr/hooks/useReturnShipmentsSummary";

export const RETURN_STAGES = [
  { key: "RtoInitiated", label: "RTO initiated", token: "status-rto" },
  { key: "RtoInTransit", label: "In transit back", token: "status-rto" },
  { key: "RtoAtOrigin", label: "At origin", token: "status-rto" },
  { key: "RtoDelivered", label: "Delivered to merchant", token: "status-delivered" },
  { key: "PendingConfirmation", label: "Pending confirmation", token: "status-out-for-delivery" },
] as const;

export type ReturnStageKey = (typeof RETURN_STAGES)[number]["key"];

export function parseReturnStage(value: string | null): ReturnStageKey | undefined {
  return RETURN_STAGES.find((s) => s.key === value)?.key;
}

/** Pending confirmation = not yet confirmed by the merchant; the others match the live status. */
export function matchesReturnStage(r: ReturnShipment, stage: ReturnStageKey): boolean {
  return stage === "PendingConfirmation" ? r.merchantConfirmedAt === null : r.currentStatus === stage;
}

export function returnStageCount(summary: ReturnShipmentSummary, stage: ReturnStageKey): number {
  return stage === "PendingConfirmation" ? summary.pendingConfirmation : summary.byRtoStage[stage] ?? 0;
}

// NdrService.ConfirmReturnShipmentAsync moves the original shipment RtoAtOrigin → RtoDelivered.
export function canHandBack(r: ReturnShipment): boolean {
  return r.currentStatus === "RtoAtOrigin" && r.merchantConfirmedAt === null;
}
```

- [ ] **Step 7: Fix `Manifest` fixtures**

Run: `npx tsc --noEmit`
For every error "Property 'receivedCount' is missing in type …" add `receivedCount: 0,` after `shipmentCount` in that fixture (e.g. `ManifestDraftSection.test.tsx`, scanning tests). Re-run until `npx tsc --noEmit` reports 0 errors.

- [ ] **Step 8: Run the tests**

Run: `npx vitest run src/features/manifests src/features/ndr src/app/api/manifests src/app/api/shipments src/features/scanning`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add -A src/features/manifests src/features/ndr src/app/api/manifests src/app/api/shipments src/features/scanning
git commit -m "$(cat <<'EOF'
feat(web): data layer for manifests, NDR and returns redesign

receivedCount and direction on manifests, parcel list and delivery
attempts hooks + proxy routes, assigned/reason on the NDR list, and pure
page models (stage strips, highlight rules, legal NDR moves, hand-back
rule).

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Manifests page — header, lifecycle strip, direction tabs, table

**Files:**
- Modify: `app/(staff)/manifests/page.tsx`
- Modify: `app/(staff)/manifests/page.test.tsx` (rewrite)

**Interfaces:**
- Consumes: `useSearchParamState`, `useFilterPage`, `toClientPage`, `branchCode`, `PipelineStrip` (Task 5); `useManifests(..., { direction })`, `manifestModel`, `openDetail(id, direction)` (Task 6); `usePeopleNames` (`riderName`), `useSession` (`hasAllBranchesScope`, `branches[].branchId`), `useBranches`, `usePermission`.
- Produces: URL contract for `/manifests`: `?tab=incoming|outgoing|all` (default incoming), `?status=<ManifestStatus>`, `?branch=<id>`.

- [ ] **Step 1: Write the failing page test**

Replace `app/(staff)/manifests/page.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import type { Manifest } from "@/features/manifests/types";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/manifests",
  useSearchParams: () => new URLSearchParams(search),
}));

const useManifests = vi.fn();
vi.mock("@/features/manifests/hooks/useManifests", () => ({
  useManifests: (...args: unknown[]) => useManifests(...args),
}));
let summary: { data?: Record<string, number>; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/manifests/hooks/useManifestsSummary", () => ({ useManifestsSummary: () => summary }));
vi.mock("@/features/auth/hooks/useSession", () => ({
  useSession: () => ({ data: { userId: "u1", hasAllBranchesScope: true, branches: [] } }),
}));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", code: "KTM", name: "Kathmandu Hub" }, { id: "b2", code: "PKR", name: "Pokhara Hub" }] }),
}));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ riderName: (id: string | null) => (id ? "Hari Thapa" : "—"), userName: () => "Staff", branchName: () => "Branch" }),
}));
const openDetail = vi.fn();
const openCreate = vi.fn();
vi.mock("@/features/manifests/store/manifestPanelStore", () => ({
  useManifestPanelStore: (selector: (s: unknown) => unknown) => selector({ panel: { mode: "closed" }, openCreate, openDetail }),
}));

import ManifestsPage from "./page";

function manifest(overrides: Partial<Manifest>): Manifest {
  return {
    id: "m1", code: "MNF-20261015-001", fromBranchId: "b1", toBranchId: "b2", purpose: "Forward",
    vehiclePlate: "BA 2 KHA 1234", driverId: "r1", status: "Received", shipmentCount: 120, receivedCount: 118,
    createdAt: "2026-10-15T09:00:00Z", ...overrides,
  };
}

beforeEach(() => {
  replace.mockReset();
  openDetail.mockReset();
  search = "";
  granted = ["manifest.create", "manifest.receive"];
  summary = { data: { Draft: 1, Sealed: 2, InTransit: 5, Received: 3, Closed: 40 }, isLoading: false, isError: false, refetch: vi.fn() };
  useManifests.mockReset();
  useManifests.mockReturnValue({ data: [manifest({})], isLoading: false, isError: false, refetch: vi.fn() });
});

describe("ManifestsPage", () => {
  it("shows the five-stage strip from the summary and highlights the busiest open stage", () => {
    render(<ManifestsPage />);
    const strip = screen.getByRole("list", { name: "Manifest lifecycle" });
    expect(strip).toHaveTextContent("Receiving");
    expect(screen.getByRole("button", { name: /In transit/ })).toHaveAttribute("aria-current", "step");
    expect(screen.getByText("Across your branches")).toBeInTheDocument();
  });

  it("never renders zeros while the summary loads or fails", () => {
    summary = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    const { unmount } = render(<ManifestsPage />);
    expect(screen.getByRole("status", { name: "Loading manifest lifecycle" })).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Manifest lifecycle" })).toBeNull();
    unmount();

    summary = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<ManifestsPage />);
    expect(screen.getByText("Couldn't load manifest counts")).toBeInTheDocument();
  });

  it("defaults to the incoming tab and writes tab and stage changes to the URL", () => {
    render(<ManifestsPage />);
    expect(useManifests).toHaveBeenCalledWith(undefined, undefined, { direction: "incoming" });
    fireEvent.mouseDown(screen.getByRole("tab", { name: "Outgoing" }));
    expect(replace).toHaveBeenCalledWith("/manifests?tab=outgoing", { scroll: false });
    fireEvent.click(screen.getByRole("button", { name: /Sealed/ }));
    expect(replace).toHaveBeenCalledWith("/manifests?status=Sealed", { scroll: false });
  });

  it("reads tab, status and branch from the URL; the All tab sends no direction", () => {
    search = "tab=all&status=Received&branch=b2";
    render(<ManifestsPage />);
    expect(useManifests).toHaveBeenCalledWith("b2", "Received", { direction: undefined });
    fireEvent.click(screen.getByRole("button", { name: "Clear status filter Receiving" }));
    expect(replace).toHaveBeenCalledWith("/manifests?tab=all&branch=b2", { scroll: false });
  });

  it("words the parcels column by stage and routes with branch codes", () => {
    useManifests.mockReturnValue({
      data: [manifest({}), manifest({ id: "m2", code: "MNF-20261015-002", status: "Draft", shipmentCount: 5, receivedCount: 0, vehiclePlate: null, driverId: null })],
      isLoading: false, isError: false, refetch: vi.fn(),
    });
    render(<ManifestsPage />);
    expect(screen.getByText("118 / 120")).toBeInTheDocument();
    expect(screen.getByText("2 missing")).toBeInTheDocument();
    expect(screen.getByText("5 parcels")).toBeInTheDocument();
    expect(screen.getAllByText("KTM → PKR")).toHaveLength(2);
    expect(screen.getByText("Not assigned")).toBeInTheDocument();
    expect(screen.getByText("Hari Thapa")).toBeInTheDocument();
    expect(screen.getByText(/All my branches · 2 open/)).toBeInTheDocument();
  });

  it("opens the Sheet with the direction only when a branch is picked", () => {
    render(<ManifestsPage />);
    fireEvent.click(screen.getByRole("button", { name: /MNF-20261015-001/ }));
    expect(openDetail).toHaveBeenCalledWith("m1", undefined);

    search = "branch=b2";
    render(<ManifestsPage />);
    fireEvent.click(screen.getAllByRole("button", { name: /MNF-20261015-001/ })[1]);
    expect(openDetail).toHaveBeenLastCalledWith("m1", "incoming");
  });

  it("shows an error state with retry when the list fails, and gates the header actions", () => {
    granted = [];
    useManifests.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    render(<ManifestsPage />);
    expect(screen.getByText("Couldn't load manifests")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /New manifest/ })).toBeNull();
    expect(screen.queryByRole("link", { name: /Receive at scan station/ })).toBeNull();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx vitest run "src/app/(staff)/manifests/page.test.tsx"`
Expected: FAIL — the old page has no lifecycle list, tabs, or direction argument.

- [ ] **Step 3: Implement the page**

Replace `app/(staff)/manifests/page.tsx`:

```tsx
"use client";

import { Suspense } from "react";
import Link from "next/link";
import { Plus, ScanLine, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { PageHeader } from "@/components/page/PageHeader";
import { PipelineStrip, type PipelineStage } from "@/components/page/PipelineStrip";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useSession } from "@/features/auth/hooks/useSession";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { branchCode } from "@/features/branches/lib/branchCode";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useManifests } from "@/features/manifests/hooks/useManifests";
import { useManifestsSummary } from "@/features/manifests/hooks/useManifestsSummary";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";
import {
  MANIFEST_STAGES, manifestStageLabel, parcelsSummary, parseManifestStatus, parseManifestTab, pickManifestHighlight, type ManifestTab,
} from "@/features/manifests/lib/manifestModel";
import { useFilterPage } from "@/lib/hooks/useFilterPage";
import { useSearchParamState } from "@/lib/hooks/useSearchParamState";
import { toClientPage } from "@/lib/paging/clientPage";
import type { Manifest } from "@/features/manifests/types";

const ALL_BRANCHES = "all";
const PAGE_SIZE = 20;
const createdFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });

const TABS: { key: ManifestTab; label: string }[] = [
  { key: "incoming", label: "Incoming" },
  { key: "outgoing", label: "Outgoing" },
  { key: "all", label: "All" },
];

const EMPTY_COPY: Record<ManifestTab, string> = {
  incoming: "Nothing is headed to this branch right now.",
  outgoing: "Nothing has left this branch. Create a manifest to start a transfer.",
  all: "Create a manifest to start a transfer between branches.",
};

function ManifestsPageContent() {
  const { params, update } = useSearchParamState();
  const tab = parseManifestTab(params.get("tab"));
  const status = parseManifestStatus(params.get("status"));
  const branchId = params.get("branch") ?? undefined;

  const { data: session } = useSession();
  const { data: branches } = useBranches();
  const scopedBranches = (branches ?? []).filter(
    (b) => session?.hasAllBranchesScope || session?.branches.some((s) => s.branchId === b.id),
  );
  const showBranchPicker = Boolean(session?.hasAllBranchesScope) || scopedBranches.length > 1;
  const branchLabel = branchId ? scopedBranches.find((b) => b.id === branchId)?.name ?? "Selected branch" : "All my branches";

  const list = useManifests(branchId, status, { direction: tab === "all" ? undefined : tab });
  const summary = useManifestsSummary();
  const names = usePeopleNames();
  const canCreate = usePermission("manifest.create");
  const canReceive = usePermission("manifest.receive");
  const panel = useManifestPanelStore((s) => s.panel);
  const openCreate = useManifestPanelStore((s) => s.openCreate);
  const openDetail = useManifestPanelStore((s) => s.openDetail);
  const [page, setPage] = useFilterPage(`${tab}|${status ?? ""}|${branchId ?? ""}`);

  // The Sheet eyebrow says INCOMING/OUTGOING only when that is relative to one picked branch.
  const sheetDirection = branchId !== undefined && tab !== "all" ? tab : undefined;
  const openCount = list.data?.filter((m) => m.status !== "Closed").length;

  const stages: PipelineStage[] = MANIFEST_STAGES.map((s) => ({
    key: s.status,
    label: s.label,
    token: s.token,
    count: summary.data ? summary.data[s.status] ?? 0 : null,
  }));

  const columns: ColumnDef<Manifest>[] = [
    {
      key: "code",
      header: "Manifest",
      render: (m) => (
        <button type="button" onClick={() => openDetail(m.id, sheetDirection)} className="flex flex-col items-start gap-0.5 text-left">
          <span className="font-mono text-sm font-medium text-foreground hover:underline">{m.code}</span>
          <span className="text-xs text-muted-foreground">{m.purpose === "Return" ? "RTO" : "Forward"}</span>
        </button>
      ),
    },
    {
      key: "route",
      header: "Route",
      render: (m) => <span className="font-mono text-sm">{`${branchCode(branches, m.fromBranchId)} → ${branchCode(branches, m.toBranchId)}`}</span>,
    },
    {
      key: "vehicle",
      header: "Vehicle · driver",
      render: (m) => (
        <div className="flex flex-col gap-0.5">
          <span className="text-sm">{m.vehiclePlate ?? "Not assigned"}</span>
          <span className="text-xs text-muted-foreground">{m.driverId ? names.riderName(m.driverId) : "—"}</span>
        </div>
      ),
    },
    {
      key: "parcels",
      header: "Parcels",
      render: (m) => {
        const parcels = parcelsSummary(m);
        return (
          <div className="flex flex-col gap-0.5">
            <span className="text-sm tabular-nums">{parcels.primary}</span>
            {parcels.missing > 0 && <span className="text-xs font-medium text-destructive">{`${parcels.missing} missing`}</span>}
          </div>
        );
      },
    },
    { key: "status", header: "Status", render: (m) => <StatusBadge status={m.status} namespace="Manifest" /> },
    {
      key: "created",
      header: "Created",
      render: (m) => (
        <time dateTime={m.createdAt} className="whitespace-nowrap text-sm text-muted-foreground">
          {createdFormat.format(new Date(m.createdAt))}
        </time>
      ),
    },
  ];

  return (
    <div className="space-y-5">
      <PageHeader
        eyebrow="Operations"
        title="Manifests"
        description={`${branchLabel} · ${openCount === undefined ? "—" : `${openCount} open`}`}
        actions={
          <>
            {showBranchPicker && (
              <Select value={branchId ?? ALL_BRANCHES} onValueChange={(v) => update({ branch: v === ALL_BRANCHES ? undefined : v })}>
                <SelectTrigger className="w-48" aria-label="Branch">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={ALL_BRANCHES}>All my branches</SelectItem>
                  {scopedBranches.map((b) => (
                    <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
            {canReceive && (
              <Button asChild variant="outline" className="gap-1.5">
                <Link href="/scan">
                  <ScanLine className="h-4 w-4" aria-hidden="true" />
                  Receive at scan station
                </Link>
              </Button>
            )}
            {canCreate && (
              <Button type="button" className="gap-1.5" onClick={() => openCreate()}>
                <Plus className="h-4 w-4" aria-hidden="true" />
                New manifest
              </Button>
            )}
          </>
        }
      />

      <section aria-label="Manifest lifecycle summary" className="space-y-2">
        {summary.isLoading && (
          <div role="status" aria-label="Loading manifest lifecycle" className="grid grid-cols-2 gap-2.5 sm:grid-cols-5">
            {MANIFEST_STAGES.map((s) => (
              <Skeleton key={s.status} className="h-20 rounded-lg" />
            ))}
          </div>
        )}
        {summary.isError && (
          <ErrorState title="Couldn't load manifest counts" description="The lifecycle strip is unavailable right now." onRetry={() => summary.refetch()} />
        )}
        {summary.data && (
          <>
            <PipelineStrip
              ariaLabel="Manifest lifecycle"
              stages={stages}
              highlightKey={pickManifestHighlight(summary.data)}
              selectedKey={status}
              onSelect={(key) => update({ status: key === status ? undefined : key })}
            />
            <p className="text-2xs text-muted-foreground">Across your branches</p>
          </>
        )}
      </section>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <Tabs value={tab} onValueChange={(v) => update({ tab: parseManifestTab(v) === "incoming" ? undefined : v })}>
          <TabsList>
            {TABS.map((t) => (
              <TabsTrigger key={t.key} value={t.key}>{t.label}</TabsTrigger>
            ))}
          </TabsList>
        </Tabs>
        {status && (
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-8 gap-1.5"
            aria-label={`Clear status filter ${manifestStageLabel(status)}`}
            onClick={() => update({ status: undefined })}
          >
            {`Status: ${manifestStageLabel(status)}`}
            <X className="h-3.5 w-3.5" aria-hidden="true" />
          </Button>
        )}
      </div>

      {list.isError ? (
        <ErrorState title="Couldn't load manifests" description="Something went wrong fetching the manifest list." onRetry={() => list.refetch()} />
      ) : (
        <DataTable
          columns={columns}
          data={list.data && toClientPage(list.data, page, PAGE_SIZE)}
          isLoading={list.isLoading}
          emptyTitle="No manifests here"
          emptyDescription={EMPTY_COPY[tab]}
          onPageChange={setPage}
          rowKey={(m) => m.id}
          selectedRowId={panel.mode === "detail" ? panel.id : undefined}
        />
      )}
    </div>
  );
}

export default function ManifestsPage() {
  return (
    <Suspense>
      <ManifestsPageContent />
    </Suspense>
  );
}
```

Note on the default tab: writing `tab=incoming` would make the default URL noisy, so the tab handler deletes `tab` when Incoming is chosen. The test's `?tab=outgoing` assertion relies on this.

- [ ] **Step 4: Run the page test**

Run: `npx vitest run "src/app/(staff)/manifests/page.test.tsx"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add "src/app/(staff)/manifests"
git commit -m "$(cat <<'EOF'
feat(web): manifests page with lifecycle strip and incoming/outgoing tabs

Five-stage strip from /manifests/summary with the busiest open stage
highlighted; tabs, status and branch live in the URL; parcels column
shows received/total and missing once receiving starts; route by branch
code; driver by rider name.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Manifest Sheet — header, stepper, receive reconciliation, parcel list, close footer

**Files:**
- Create: `features/manifests/components/ManifestStepper.tsx`
- Create: `features/manifests/components/ManifestReconciliation.tsx`
- Create: `features/manifests/components/ManifestCloseAction.tsx`
- Modify: `features/manifests/components/ManifestDetailSheet.tsx` (rewrite)
- Modify: `features/manifests/components/ManifestDraftSection.tsx`, `features/manifests/components/ManifestSealedSection.tsx` (permission props)
- Modify: `features/manifests/components/ManifestDraftSection.test.tsx`, `features/manifests/components/ManifestInTransitSection.tsx` (helper copy), `features/manifests/hooks/useCloseManifest.ts` (summary invalidation)
- Delete: `features/manifests/components/ManifestReceivedSection.tsx`, `features/manifests/components/ManifestReceivedSection.test.tsx`
- Create: `features/manifests/components/ManifestDetailSheet.test.tsx`

**Interfaces:**
- Consumes: `useManifest`, `useMissingShipments`, `useManifestShipments`, `useCloseManifest`, `usePeopleNames`, `usePermission`, `useShipmentPanelStore.openDetail(awb)`, `manifestModel.stepperState`/`MANIFEST_STAGES`, panel `direction` (Task 6).
- Produces: `ManifestDraftSection({ manifest, canSeal = true })`, `ManifestSealedSection({ manifest, canDispatch = true })`, `ManifestStepper({ status })`, `ManifestReconciliation({ manifest })`, `ManifestCloseAction({ manifest })`.

- [ ] **Step 1: Write the failing tests**

Create `features/manifests/components/ManifestDetailSheet.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { Manifest, ManifestShipmentScan, MissingShipment } from "@/features/manifests/types";

let manifestQuery: { data?: Manifest; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/manifests/hooks/useManifest", () => ({ useManifest: () => manifestQuery }));
let missingQuery: { data?: MissingShipment[]; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/manifests/hooks/useMissingShipments", () => ({ useMissingShipments: () => missingQuery }));
let parcelsQuery: { data?: ManifestShipmentScan[]; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/manifests/hooks/useManifestShipments", () => ({ useManifestShipments: () => parcelsQuery }));
const closeMutate = vi.fn();
vi.mock("@/features/manifests/hooks/useCloseManifest", () => ({ useCloseManifest: () => ({ mutate: closeMutate, isPending: false, isError: false }) }));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ riderName: () => "Hari Thapa", userName: () => "Staff", branchName: (id: string | null) => (id === "b1" ? "Kathmandu Hub" : "Pokhara Hub") }),
}));
const openShipment = vi.fn();
vi.mock("@/features/shipments/store/shipmentPanelStore", () => ({
  useShipmentPanelStore: (selector: (s: unknown) => unknown) => selector({ openDetail: openShipment }),
}));
let panel: { mode: "detail"; id: string; direction?: "incoming" | "outgoing" } = { mode: "detail", id: "m1" };
vi.mock("@/features/manifests/store/manifestPanelStore", () => ({
  useManifestPanelStore: (selector: (s: unknown) => unknown) => selector({ panel, close: vi.fn() }),
}));
vi.mock("@/features/manifests/components/ManifestDraftSection", () => ({
  ManifestDraftSection: ({ canSeal }: { canSeal: boolean }) => <p>{`draft-section seal:${canSeal}`}</p>,
}));
vi.mock("@/features/manifests/components/ManifestSealedSection", () => ({ ManifestSealedSection: () => <p>sealed-section</p> }));
vi.mock("@/features/manifests/components/ManifestInTransitSection", () => ({ ManifestInTransitSection: () => <p>receive-scan</p> }));

import { ManifestDetailSheet } from "./ManifestDetailSheet";

const base: Manifest = {
  id: "m1", code: "MNF-20261015-001", fromBranchId: "b1", toBranchId: "b2", purpose: "Forward",
  vehiclePlate: "BA 2 KHA 1234", driverId: "r1", status: "Received", shipmentCount: 120, receivedCount: 118,
  createdAt: "2026-10-15T09:00:00Z",
};

beforeEach(() => {
  closeMutate.mockReset();
  openShipment.mockReset();
  granted = ["manifest.close", "manifest.seal", "manifest.dispatch"];
  panel = { mode: "detail", id: "m1", direction: "incoming" };
  manifestQuery = { data: base, isLoading: false, isError: false, refetch: vi.fn() };
  missingQuery = {
    data: [
      { shipmentId: "s1", awb: "KTM-000201", scannedAt: "2026-10-14T15:00:00Z" },
      { shipmentId: "0f8e2a6c-1111-2222-3333-444455556666", awb: "", scannedAt: "2026-10-14T15:01:00Z" },
    ],
    isLoading: false, isError: false, refetch: vi.fn(),
  };
  parcelsQuery = {
    data: [
      { id: "ms1", shipmentId: "s1", awb: "KTM-000201", scannedAt: "2026-10-14T15:00:00Z", receivedAt: null },
      { id: "ms2", shipmentId: "s2", awb: "KTM-000202", scannedAt: "2026-10-14T15:02:00Z", receivedAt: "2026-10-15T08:00:00Z" },
    ],
    isLoading: false, isError: false, refetch: vi.fn(),
  };
});

describe("ManifestDetailSheet", () => {
  it("renders the header, eyebrow with direction and purpose, and the status-only stepper", () => {
    render(<ManifestDetailSheet />);
    expect(screen.getByText("MANIFEST · INCOMING · FORWARD")).toBeInTheDocument();
    expect(screen.getByText("MNF-20261015-001")).toBeInTheDocument();
    expect(screen.getByText("Kathmandu Hub")).toBeInTheDocument();
    expect(screen.getByText("Hari Thapa")).toBeInTheDocument();
    const steps = screen.getByRole("list", { name: "Manifest progress" });
    expect(steps.querySelector('[aria-current="step"]')).toHaveTextContent("Receiving");
    expect(screen.getByText("Stage times need backend support.")).toHaveAttribute("data-coming-soon");
  });

  it("shows reconciliation counts, the missing list with a short-id fallback, and the parcel list", () => {
    render(<ManifestDetailSheet />);
    expect(screen.getByText("118")).toBeInTheDocument();
    expect(screen.getByText("of 120 verified")).toBeInTheDocument();
    expect(screen.getByRole("progressbar", { name: "Parcels verified" })).toHaveAttribute("aria-valuenow", "118");
    expect(screen.getAllByText("KTM-000201")).toHaveLength(2);
    expect(screen.getByText("0f8e2a6c")).toBeInTheDocument();
    expect(screen.getByText("Not yet")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Open parcel KTM-000201" }));
    expect(openShipment).toHaveBeenCalledWith("KTM-000201");
  });

  it("confirms 'Close with N missing' before closing", () => {
    render(<ManifestDetailSheet />);
    fireEvent.click(screen.getByRole("button", { name: "Close with 2 missing" }));
    const dialog = screen.getByRole("alertdialog");
    expect(within(dialog).getByText("2 parcels stay listed as missing.")).toBeInTheDocument();
    expect(closeMutate).not.toHaveBeenCalled();
    fireEvent.click(within(dialog).getByRole("button", { name: "Close with 2 missing" }));
    expect(closeMutate).toHaveBeenCalledTimes(1);
  });

  it("closes directly when nothing is missing, and hides close without manifest.close", () => {
    manifestQuery = { ...manifestQuery, data: { ...base, receivedCount: 120 } };
    const { unmount } = render(<ManifestDetailSheet />);
    fireEvent.click(screen.getByRole("button", { name: "Close manifest" }));
    expect(closeMutate).toHaveBeenCalledTimes(1);
    unmount();

    granted = [];
    render(<ManifestDetailSheet />);
    // Exact names: the Sheet's own dismiss button is also called "Close".
    expect(screen.queryByRole("button", { name: /^Close (manifest|with)/ })).toBeNull();
  });

  it("gives each sub-read its own loading, error and empty state", () => {
    missingQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    parcelsQuery = { data: [], isLoading: false, isError: false, refetch: vi.fn() };
    render(<ManifestDetailSheet />);
    expect(screen.getByText("Couldn't load missing parcels")).toBeInTheDocument();
    expect(screen.getByText("No parcels on this manifest.")).toBeInTheDocument();
  });

  it("titles a closed manifest's missing list and drops scan and close", () => {
    manifestQuery = { ...manifestQuery, data: { ...base, status: "Closed" } };
    render(<ManifestDetailSheet />);
    expect(screen.getByText("Closed with 2 missing")).toBeInTheDocument();
    expect(screen.queryByText("receive-scan")).toBeNull();
    expect(screen.queryByRole("button", { name: /^Close (manifest|with)/ })).toBeNull();
  });

  it("passes seal permission to the draft section and omits the direction when the store has none", () => {
    granted = [];
    panel = { mode: "detail", id: "m1" };
    manifestQuery = { ...manifestQuery, data: { ...base, status: "Draft", receivedCount: 0, purpose: "Return" } };
    render(<ManifestDetailSheet />);
    expect(screen.getByText("draft-section seal:false")).toBeInTheDocument();
    expect(screen.getByText("MANIFEST · RTO")).toBeInTheDocument();
    expect(screen.queryByRole("progressbar")).toBeNull();
  });

  it("renders a skeleton while loading and an error with retry", () => {
    manifestQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<ManifestDetailSheet />);
    expect(screen.getByText("Couldn't load this manifest")).toBeInTheDocument();
  });

  it("shows 0 of 0 verified with an empty bar for a manifest with no parcels", () => {
    manifestQuery = { ...manifestQuery, data: { ...base, shipmentCount: 0, receivedCount: 0 } };
    missingQuery = { data: [], isLoading: false, isError: false, refetch: vi.fn() };
    render(<ManifestDetailSheet />);
    expect(screen.getByText("of 0 verified")).toBeInTheDocument();
    expect(screen.getByRole("progressbar", { name: "Parcels verified" }).firstElementChild).toHaveStyle({ width: "0%" });
  });
});
```

In `features/manifests/components/ManifestDraftSection.test.tsx`, add a `receivedCount: 0` to its fixture if Task 6 did not already, and add a case:

```tsx
  it("hides the seal button without manifest.seal", () => {
    renderWithProviders(<ManifestDraftSection manifest={manifest} canSeal={false} />);
    expect(screen.queryByRole("button", { name: /seal manifest/i })).not.toBeInTheDocument();
  });
```

(Use that file's existing render helper and fixture names; if they differ, adapt the two identifiers — the assertion stays the same.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npx vitest run src/features/manifests/components`
Expected: FAIL — the Sheet has no eyebrow, stepper, reconciliation or close footer; `canSeal` is ignored.

- [ ] **Step 3: Permission props on the existing sections**

In `ManifestDraftSection.tsx`, change the signature to `export function ManifestDraftSection({ manifest, canSeal = true }: { manifest: Manifest; canSeal?: boolean })` and wrap the existing Seal `<Button …>…</Button>` in `{canSeal && ( … )}`.

In `ManifestSealedSection.tsx`, change the signature to `export function ManifestSealedSection({ manifest, canDispatch = true }: { manifest: Manifest; canDispatch?: boolean })` and wrap the existing Dispatch `<Button …>…</Button>` in `{canDispatch && ( … )}`. When `canDispatch` is false, also skip rendering the plate/driver inputs: wrap the section's whole returned JSX as `if (!canDispatch) return <p className="text-sm text-muted-foreground">Sealed and waiting for dispatch.</p>;` placed right before the existing `return (`.

- [ ] **Step 4: Stepper**

Create `features/manifests/components/ManifestStepper.tsx`:

```tsx
import { Check, Circle, CircleDot } from "lucide-react";
import { cn } from "@/lib/utils";
import { MANIFEST_STAGES, stepperState } from "@/features/manifests/lib/manifestModel";
import type { ManifestStatus } from "@/features/manifests/types";

const STATE_TEXT = { complete: "done", current: "current stage", upcoming: "not yet" } as const;

// Status-only: the backend stores no stage timestamps (follow-up 1), so each step says done /
// current / not yet by icon and text, never colour alone.
export function ManifestStepper({ status }: { status: ManifestStatus }) {
  return (
    <div className="space-y-2">
      <ol aria-label="Manifest progress" className="grid grid-cols-5 gap-2">
        {MANIFEST_STAGES.map((stage) => {
          const state = stepperState(status, stage.status);
          const Icon = state === "complete" ? Check : state === "current" ? CircleDot : Circle;
          return (
            <li key={stage.status} aria-current={state === "current" ? "step" : undefined} className="flex flex-col items-center gap-1 text-center">
              <Icon className={cn("h-4 w-4", state === "upcoming" ? "text-muted-foreground" : "text-foreground")} aria-hidden="true" />
              <span className={cn("text-2xs", state === "upcoming" ? "text-muted-foreground" : "font-medium text-foreground")}>{stage.label}</span>
              <span className="sr-only">{STATE_TEXT[state]}</span>
            </li>
          );
        })}
      </ol>
      <p data-coming-soon="" className="text-2xs text-muted-foreground">Stage times need backend support.</p>
    </div>
  );
}
```

- [ ] **Step 5: Reconciliation and parcel list**

Create `features/manifests/components/ManifestReconciliation.tsx`:

```tsx
"use client";

import Link from "next/link";
import { Package, PackageCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { SectionCard } from "@/components/page/SectionCard";
import { ErrorState } from "@/components/states/ErrorState";
import { useMissingShipments } from "@/features/manifests/hooks/useMissingShipments";
import { useManifestShipments } from "@/features/manifests/hooks/useManifestShipments";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import type { Manifest } from "@/features/manifests/types";

const timeFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });

function parcelLabel(p: { awb: string; shipmentId: string }): string {
  return p.awb || p.shipmentId.slice(0, 8);
}

export function ManifestReconciliation({ manifest }: { manifest: Manifest }) {
  const missing = useMissingShipments(manifest.id, true);
  const parcels = useManifestShipments(manifest.id, true);
  const openShipment = useShipmentPanelStore((s) => s.openDetail);
  const closed = manifest.status === "Closed";
  const total = manifest.shipmentCount;
  const received = manifest.receivedCount;
  const share = total > 0 ? received / total : 0;

  return (
    <div className="space-y-4">
      <SectionCard
        title="Receive reconciliation"
        icon={PackageCheck}
        action={closed ? undefined : <Link href="/scan" className="font-medium text-primary hover:underline">Open scan station</Link>}
      >
        <div className="space-y-3">
          <p className="flex items-baseline gap-1.5">
            <span className="text-2xl font-semibold tabular-nums tracking-tight">{received}</span>
            <span className="text-sm text-muted-foreground">{`of ${total} verified`}</span>
          </p>
          <div
            role="progressbar"
            aria-label="Parcels verified"
            aria-valuemin={0}
            aria-valuemax={total}
            aria-valuenow={received}
            className="h-2 overflow-hidden rounded-full bg-muted"
          >
            <div className="h-full rounded-full bg-status-delivered" style={{ width: `${Math.round(share * 100)}%` }} />
          </div>

          <h3 className="pt-1 text-sm font-semibold">
            {closed && missing.data ? `Closed with ${missing.data.length} missing` : "Missing parcels"}
          </h3>
          {missing.isLoading && <Skeleton className="h-16 w-full" />}
          {missing.isError && (
            <ErrorState title="Couldn't load missing parcels" description="Reconciliation is unavailable right now." onRetry={() => missing.refetch()} />
          )}
          {missing.data && missing.data.length === 0 && <p className="text-sm text-muted-foreground">No parcels missing.</p>}
          {missing.data && missing.data.length > 0 && (
            <ul className="divide-y divide-border">
              {missing.data.map((p) => (
                <li key={p.shipmentId} className="flex items-center justify-between gap-3 py-2">
                  <div className="min-w-0">
                    <p className="font-mono text-sm">{parcelLabel(p)}</p>
                    <p className="text-xs text-muted-foreground">{`Scanned onto this manifest ${timeFormat.format(new Date(p.scannedAt))}`}</p>
                  </div>
                  {p.awb && (
                    <Button
                      type="button"
                      variant="link"
                      size="sm"
                      className="h-auto shrink-0 p-0"
                      aria-label={`Open parcel ${p.awb}`}
                      onClick={() => openShipment(p.awb)}
                    >
                      Open parcel
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      </SectionCard>

      <SectionCard title="Parcels in this manifest" icon={Package}>
        {parcels.isLoading && <Skeleton className="h-24 w-full" />}
        {parcels.isError && (
          <ErrorState title="Couldn't load the parcel list" description="Try again in a moment." onRetry={() => parcels.refetch()} />
        )}
        {parcels.data && parcels.data.length === 0 && <p className="text-sm text-muted-foreground">No parcels on this manifest.</p>}
        {parcels.data && parcels.data.length > 0 && (
          <Table>
            <TableHeader>
              <TableRow className="hover:bg-transparent">
                <TableHead className="text-2xs">AWB</TableHead>
                <TableHead className="text-2xs">Scanned on</TableHead>
                <TableHead className="text-2xs">Received</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {parcels.data.map((p) => (
                <TableRow key={p.id}>
                  <TableCell className="font-mono text-sm">{parcelLabel(p)}</TableCell>
                  <TableCell className="text-sm text-muted-foreground">{timeFormat.format(new Date(p.scannedAt))}</TableCell>
                  <TableCell className="text-sm">{p.receivedAt ? timeFormat.format(new Date(p.receivedAt)) : "Not yet"}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </SectionCard>
    </div>
  );
}
```

- [ ] **Step 6: Close footer**

Create `features/manifests/components/ManifestCloseAction.tsx`:

```tsx
"use client";

import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle, AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useCloseManifest } from "@/features/manifests/hooks/useCloseManifest";
import type { Manifest } from "@/features/manifests/types";

// The backend allows closing with parcels still unscanned (they stay listed as missing), so the
// missing case confirms first; a fully received manifest closes in one click.
export function ManifestCloseAction({ manifest }: { manifest: Manifest }) {
  const canClose = usePermission("manifest.close");
  const close = useCloseManifest(manifest.id);
  if (!canClose) return null;

  const missingCount = Math.max(0, manifest.shipmentCount - manifest.receivedCount);
  const label = missingCount > 0 ? `Close with ${missingCount} missing` : "Close manifest";

  return (
    <div className="mt-auto space-y-2 border-t border-border pt-4">
      {missingCount === 0 ? (
        <Button type="button" className="w-full" disabled={close.isPending} onClick={() => close.mutate()}>
          {close.isPending ? <Spinner className="h-4 w-4" /> : label}
        </Button>
      ) : (
        <AlertDialog>
          <AlertDialogTrigger asChild>
            <Button type="button" className="w-full" disabled={close.isPending}>
              {close.isPending ? <Spinner className="h-4 w-4" /> : label}
            </Button>
          </AlertDialogTrigger>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>{`Close ${manifest.code}?`}</AlertDialogTitle>
              <AlertDialogDescription>{`${missingCount} parcels stay listed as missing.`}</AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>Keep receiving</AlertDialogCancel>
              <AlertDialogAction onClick={() => close.mutate()}>{label}</AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      )}
      {close.isError && <p role="alert" className="text-sm text-destructive">Couldn't close this manifest. Try again.</p>}
    </div>
  );
}
```

- [ ] **Step 7: Rewrite the Sheet**

Replace `features/manifests/components/ManifestDetailSheet.tsx`:

```tsx
"use client";

import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useManifest } from "@/features/manifests/hooks/useManifest";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";
import { ManifestDraftSection } from "@/features/manifests/components/ManifestDraftSection";
import { ManifestSealedSection } from "@/features/manifests/components/ManifestSealedSection";
import { ManifestInTransitSection } from "@/features/manifests/components/ManifestInTransitSection";
import { ManifestStepper } from "@/features/manifests/components/ManifestStepper";
import { ManifestReconciliation } from "@/features/manifests/components/ManifestReconciliation";
import { ManifestCloseAction } from "@/features/manifests/components/ManifestCloseAction";

export function ManifestDetailSheet() {
  const panel = useManifestPanelStore((s) => s.panel);
  const close = useManifestPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";
  const direction = panel.mode === "detail" ? panel.direction : undefined;

  const { data: manifest, isLoading, isError, refetch } = useManifest(id);
  const names = usePeopleNames();
  const canSeal = usePermission("manifest.seal");
  const canDispatch = usePermission("manifest.dispatch");

  const receiving = manifest?.status === "InTransit" || manifest?.status === "Received";
  const reconciling = receiving || manifest?.status === "Closed";
  const eyebrow = manifest
    ? ["MANIFEST", direction?.toUpperCase(), manifest.purpose === "Return" ? "RTO" : "FORWARD"].filter(Boolean).join(" · ")
    : "MANIFEST";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="flex w-full flex-col gap-6 overflow-y-auto sm:max-w-xl">
        {!manifest && <SheetTitle className="sr-only">Manifest</SheetTitle>}
        {isLoading && <DetailSkeleton fieldRows={2} />}
        {isError && (
          <ErrorState
            title="Couldn't load this manifest"
            description="It may not exist, or you may not have access to it."
            onRetry={() => refetch()}
          />
        )}
        {manifest && (
          <>
            <SheetHeaderBlock
              eyebrow={eyebrow}
              title={manifest.code}
              mono
              status={<StatusBadge status={manifest.status} namespace="Manifest" />}
              facts={[
                { label: "From", value: names.branchName(manifest.fromBranchId) },
                { label: "To", value: names.branchName(manifest.toBranchId) },
                { label: "Vehicle", value: manifest.vehiclePlate ?? "Not assigned" },
                { label: "Driver", value: manifest.driverId ? names.riderName(manifest.driverId) : "—" },
              ]}
            />
            <ManifestStepper status={manifest.status} />

            {manifest.status === "Draft" && <ManifestDraftSection manifest={manifest} canSeal={canSeal} />}
            {manifest.status === "Sealed" && <ManifestSealedSection manifest={manifest} canDispatch={canDispatch} />}
            {receiving && <ManifestInTransitSection manifest={manifest} />}
            {reconciling && <ManifestReconciliation manifest={manifest} />}
            {receiving && <ManifestCloseAction manifest={manifest} />}
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}
```

Delete the replaced section:

```bash
git rm src/features/manifests/components/ManifestReceivedSection.tsx src/features/manifests/components/ManifestReceivedSection.test.tsx
```

The strip's summary lives under `["manifests-summary"]`, outside the `["manifests"]` prefix. In `hooks/useCloseManifest.ts`, add `queryClient.invalidateQueries({ queryKey: ["manifests-summary"] });` after the existing invalidation so closing moves the strip count.

In `ManifestInTransitSection.tsx`, replace the closing helper paragraph text with: "Scan each parcel as it comes off the vehicle. You can close the manifest with parcels still missing." (the old text referred to a Received section that no longer exists).

- [ ] **Step 8: Run the tests**

Run: `npx vitest run src/features/manifests src/features/scanning`
Expected: PASS. (`features/scanning/components/ReconciliationPanel` keeps using `useMissingShipments`, whose signature is unchanged.)

- [ ] **Step 9: Commit**

```bash
git add -A src/features/manifests
git commit -m "$(cat <<'EOF'
feat(web): manifest Sheet with stepper, receive reconciliation and parcel list

Header block with direction/purpose eyebrow, status-only stepper (stage
times need backend), received-of-total with missing parcels and the full
parcel list, and a close footer that confirms 'Close with N missing'.
Seal and dispatch respect their permissions.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: NDR queue page — status strip, quick filters, age and follow-up warnings

**Files:**
- Modify: `app/(staff)/ndrs/page.tsx` (rewrite)
- Modify: `app/(staff)/ndrs/page.test.tsx` (rewrite)

**Interfaces:**
- Consumes: `useSearchParamState`, `useFilterPage`, `PipelineStrip`, `NdrRtoSwitch`, `formatAge`/`ageHours`/`AGE_WARNING_HOURS` (Task 5); `useNdrs({ status, assigned, reason, page, pageSize })`, `ndrModel` (Task 6); `useNdrsSummary`, `useSession`, `usePeopleNames`, `useNdrPanelStore`.
- Produces: URL contract for `/ndrs`: `?status=<NdrStatus>`, `?assigned=me|unassigned`, `?reason=<NdrReason>`.

- [ ] **Step 1: Write the failing page test**

Replace `app/(staff)/ndrs/page.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { formatAge } from "@/lib/dates/age";
import type { Ndr } from "@/features/ndr/types";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/ndrs",
  useSearchParams: () => new URLSearchParams(search),
}));

const useNdrs = vi.fn();
vi.mock("@/features/ndr/hooks/useNdrs", () => ({ useNdrs: (p: unknown) => useNdrs(p) }));
let summary: { data?: Record<string, number>; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/ndr/hooks/useNdrsSummary", () => ({ useNdrsSummary: () => summary }));
vi.mock("@/features/auth/hooks/useSession", () => ({ useSession: () => ({ data: { userId: "u1" } }) }));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: () => true }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ userName: (id: string | null) => (id === "u2" ? "Sita Rai" : "Staff"), riderName: () => "Rider", branchName: () => "Branch" }),
}));
const openDetail = vi.fn();
vi.mock("@/features/ndr/store/ndrPanelStore", () => ({
  useNdrPanelStore: (selector: (s: unknown) => unknown) => selector({ panel: { mode: "closed" }, openDetail }),
}));

import NdrsPage from "./page";

const hoursAgo = (h: number) => new Date(Date.now() - h * 3_600_000).toISOString();

function ndr(overrides: Partial<Ndr>): Ndr {
  return {
    id: "n1", shipmentId: "s1", awb: "KTM-000301", reason: "WrongAddress", assignedStaffUserId: null, status: "Open",
    customerResponse: null, followUpAt: null, nextAction: null, resolution: null, resolvedAt: null, createdAt: hoursAgo(2),
    ...overrides,
  };
}

function page(items: Ndr[]) {
  return { data: { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 }, isLoading: false, isError: false, refetch: vi.fn() };
}

beforeEach(() => {
  replace.mockReset();
  openDetail.mockReset();
  search = "";
  summary = { data: { Open: 4, Contacting: 2, Resolved: 9 }, isLoading: false, isError: false, refetch: vi.fn() };
  useNdrs.mockReset();
  useNdrs.mockReturnValue(page([ndr({})]));
});

describe("NdrsPage", () => {
  it("shows the six-stage strip with Open highlighted when NDRs are open", () => {
    render(<NdrsPage />);
    expect(screen.getByRole("heading", { name: "Failed deliveries" })).toBeInTheDocument();
    expect(screen.getByRole("tablist", { name: "NDR and returns" })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "NDR status" }).querySelectorAll("li")).toHaveLength(6);
    expect(screen.getByRole("button", { name: /^Open/ })).toHaveAttribute("aria-current", "step");
  });

  it("highlights nothing when no NDR is open, and never shows zeros while loading", () => {
    summary = { data: { Open: 0, Resolved: 3 }, isLoading: false, isError: false, refetch: vi.fn() };
    const { unmount } = render(<NdrsPage />);
    expect(screen.getByRole("button", { name: /^Open/ })).not.toHaveAttribute("aria-current");
    unmount();
    summary = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    render(<NdrsPage />);
    expect(screen.getByRole("status", { name: "Loading NDR counts" })).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "NDR status" })).toBeNull();
  });

  it("passes URL filters to the list hook and resets to page 1", () => {
    search = "status=Contacting&assigned=me&reason=WrongAddress";
    render(<NdrsPage />);
    expect(useNdrs).toHaveBeenCalledWith({ status: "Contacting", assigned: "me", reason: "WrongAddress", page: 1, pageSize: 20 });
  });

  it("toggles the mutually exclusive owner chips in the URL", () => {
    render(<NdrsPage />);
    fireEvent.click(screen.getByRole("button", { name: "Assigned to me" }));
    expect(replace).toHaveBeenLastCalledWith("/ndrs?assigned=me", { scroll: false });

    search = "assigned=me";
    render(<NdrsPage />);
    const mine = screen.getAllByRole("button", { name: "Assigned to me" })[1];
    expect(mine).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(screen.getAllByRole("button", { name: "Unassigned" })[1]);
    expect(replace).toHaveBeenLastCalledWith("/ndrs?assigned=unassigned", { scroll: false });
    fireEvent.click(mine);
    expect(replace).toHaveBeenLastCalledWith("/ndrs", { scroll: false });
  });

  it("sets and clears the status filter from the strip", () => {
    render(<NdrsPage />);
    fireEvent.click(screen.getByRole("button", { name: /^Contacting/ }));
    expect(replace).toHaveBeenLastCalledWith("/ndrs?status=Contacting", { scroll: false });
  });

  it("warns on NDRs open 24 h or more, with the age as text", () => {
    const old = hoursAgo(30);
    useNdrs.mockReturnValue(page([ndr({ id: "n1", createdAt: old }), ndr({ id: "n2", awb: "KTM-000302", createdAt: hoursAgo(2) })]));
    render(<NdrsPage />);
    expect(screen.getByText(formatAge(old))).toHaveClass("text-destructive");
    expect(screen.getByText(formatAge(hoursAgo(2)))).not.toHaveClass("text-destructive");
  });

  it("labels owners, reasons and overdue follow-ups", () => {
    useNdrs.mockReturnValue(page([
      ndr({ id: "n1", assignedStaffUserId: "u1", status: "Contacting", followUpAt: hoursAgo(1) }),
      ndr({ id: "n2", awb: "KTM-000302", assignedStaffUserId: "u2", reason: "CustomerRequestedReschedule" }),
      ndr({ id: "n3", awb: "KTM-000303" }),
    ]));
    render(<NdrsPage />);
    const table = screen.getByRole("table");
    expect(within(table).getByText("You")).toBeInTheDocument();
    expect(within(table).getByText("Sita Rai")).toBeInTheDocument();
    expect(within(table).getByText("Unassigned")).toBeInTheDocument();
    expect(within(table).getByText("Customer asked to reschedule")).toBeInTheDocument();
    expect(within(table).getByText(/Follow-up overdue/)).toBeInTheDocument();
    fireEvent.click(within(table).getByRole("button", { name: "KTM-000302" }));
    expect(openDetail).toHaveBeenCalledWith("n2");
  });

  it("shows an error state with retry when the queue fails", () => {
    useNdrs.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    render(<NdrsPage />);
    expect(screen.getByText("Couldn't load NDRs")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx vitest run "src/app/(staff)/ndrs/page.test.tsx"`
Expected: FAIL — old page has no strip list, quick filters or switch.

- [ ] **Step 3: Implement the page**

Replace `app/(staff)/ndrs/page.tsx`:

```tsx
"use client";

import { Suspense } from "react";
import { X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { PageHeader } from "@/components/page/PageHeader";
import { PipelineStrip, type PipelineStage } from "@/components/page/PipelineStrip";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useSession } from "@/features/auth/hooks/useSession";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useNdrs } from "@/features/ndr/hooks/useNdrs";
import { useNdrsSummary } from "@/features/ndr/hooks/useNdrsSummary";
import { useNdrPanelStore } from "@/features/ndr/store/ndrPanelStore";
import { NdrRtoSwitch } from "@/features/ndr/components/NdrRtoSwitch";
import {
  NDR_REASONS, NDR_STAGES, isFollowUpOverdue, ndrReasonLabel, ndrStatusLabel, ownerLabel, parseAssigned, parseNdrReason, parseNdrStatus,
} from "@/features/ndr/lib/ndrModel";
import { AGE_WARNING_HOURS, ageHours, formatAge } from "@/lib/dates/age";
import { useFilterPage } from "@/lib/hooks/useFilterPage";
import { useSearchParamState } from "@/lib/hooks/useSearchParamState";
import { cn } from "@/lib/utils";
import type { Ndr, NdrAssignedFilter } from "@/features/ndr/types";

const PAGE_SIZE = 20;
const ANY_REASON = "any";
const followUpFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });

const OWNER_CHIPS: { key: NdrAssignedFilter; label: string }[] = [
  { key: "me", label: "Assigned to me" },
  { key: "unassigned", label: "Unassigned" },
];

function NdrsPageContent() {
  const { params, update } = useSearchParamState();
  const status = parseNdrStatus(params.get("status"));
  const assigned = parseAssigned(params.get("assigned"));
  const reason = parseNdrReason(params.get("reason"));
  const [page, setPage] = useFilterPage(`${status ?? ""}|${assigned ?? ""}|${reason ?? ""}`);

  const list = useNdrs({ status, assigned, reason, page, pageSize: PAGE_SIZE });
  const summary = useNdrsSummary();
  const { data: session } = useSession();
  const names = usePeopleNames();
  const panel = useNdrPanelStore((s) => s.panel);
  const openDetail = useNdrPanelStore((s) => s.openDetail);
  const now = new Date();

  const stages: PipelineStage[] = NDR_STAGES.map((s) => ({
    key: s.status,
    label: s.label,
    token: s.token,
    count: summary.data ? summary.data[s.status] ?? 0 : null,
  }));
  const highlight = summary.data && (summary.data.Open ?? 0) > 0 ? "Open" : undefined;

  const columns: ColumnDef<Ndr>[] = [
    {
      key: "awb",
      header: "AWB",
      render: (n) => (
        <button type="button" onClick={() => openDetail(n.id)} className="font-mono text-sm font-medium text-foreground hover:underline">
          {n.awb}
        </button>
      ),
    },
    { key: "reason", header: "Reason", render: (n) => <span className="text-sm">{ndrReasonLabel(n.reason)}</span> },
    {
      key: "age",
      header: "Age",
      render: (n) => (
        <span className={cn("text-sm tabular-nums", ageHours(n.createdAt, now) >= AGE_WARNING_HOURS && "font-semibold text-destructive")}>
          {formatAge(n.createdAt, now)}
        </span>
      ),
    },
    {
      key: "owner",
      header: "Owner",
      render: (n) => <span className="text-sm">{ownerLabel(n.assignedStaffUserId, session?.userId, names.userName)}</span>,
    },
    {
      key: "status",
      header: "Status",
      render: (n) => {
        const overdue = isFollowUpOverdue(n, now);
        return (
          <div className="flex flex-col items-start gap-1">
            <StatusBadge status={n.status} namespace="Ndr" />
            {n.followUpAt && n.status !== "Resolved" && (
              <span className={cn("text-xs", overdue ? "font-medium text-destructive" : "text-muted-foreground")}>
                {`${overdue ? "Follow-up overdue · " : "Follow up "}${followUpFormat.format(new Date(n.followUpAt))}`}
              </span>
            )}
          </div>
        );
      },
    },
  ];

  return (
    <div className="space-y-5">
      <PageHeader eyebrow="Operations · NDR & RTO" title="Failed deliveries" actions={<NdrRtoSwitch active="ndr" />} />

      {summary.isLoading && (
        <div role="status" aria-label="Loading NDR counts" className="grid grid-cols-2 gap-2.5 sm:grid-cols-3 xl:grid-cols-6">
          {NDR_STAGES.map((s) => (
            <Skeleton key={s.status} className="h-20 rounded-lg" />
          ))}
        </div>
      )}
      {summary.isError && (
        <ErrorState title="Couldn't load NDR counts" description="The status strip is unavailable right now." onRetry={() => summary.refetch()} />
      )}
      {summary.data && (
        <PipelineStrip
          ariaLabel="NDR status"
          stages={stages}
          highlightKey={highlight}
          selectedKey={status}
          onSelect={(key) => update({ status: key === status ? undefined : key })}
        />
      )}

      <div className="flex flex-wrap items-center gap-2">
        {OWNER_CHIPS.map((chip) => (
          <Button
            key={chip.key}
            type="button"
            size="sm"
            variant={assigned === chip.key ? "secondary" : "outline"}
            aria-pressed={assigned === chip.key}
            onClick={() => update({ assigned: assigned === chip.key ? undefined : chip.key })}
          >
            {chip.label}
          </Button>
        ))}
        <Select value={reason ?? ANY_REASON} onValueChange={(v) => update({ reason: v === ANY_REASON ? undefined : v })}>
          <SelectTrigger className="h-9 w-60" aria-label="Reason">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ANY_REASON}>Any reason</SelectItem>
            {NDR_REASONS.map((r) => (
              <SelectItem key={r} value={r}>{ndrReasonLabel(r)}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        {status && (
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="gap-1.5"
            aria-label={`Clear status filter ${ndrStatusLabel(status)}`}
            onClick={() => update({ status: undefined })}
          >
            {`Status: ${ndrStatusLabel(status)}`}
            <X className="h-3.5 w-3.5" aria-hidden="true" />
          </Button>
        )}
      </div>

      {list.isError ? (
        <ErrorState title="Couldn't load NDRs" description="Something went wrong fetching the NDR queue." onRetry={() => list.refetch()} />
      ) : (
        <DataTable
          columns={columns}
          data={list.data}
          isLoading={list.isLoading}
          emptyTitle="No failed deliveries here"
          emptyDescription={assigned || reason || status ? "Nothing matches these filters." : "Failed deliveries that need follow-up show up here."}
          onPageChange={setPage}
          rowKey={(n) => n.id}
          selectedRowId={panel.mode === "detail" ? panel.id : undefined}
        />
      )}
    </div>
  );
}

export default function NdrsPage() {
  return (
    <Suspense>
      <NdrsPageContent />
    </Suspense>
  );
}
```

- [ ] **Step 4: Run the page test**

Run: `npx vitest run "src/app/(staff)/ndrs/page.test.tsx"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add "src/app/(staff)/ndrs"
git commit -m "$(cat <<'EOF'
feat(web): NDR queue with status strip, owner and reason quick filters

Six-stage strip with Open highlighted, Assigned to me / Unassigned and
reason filters in the URL, age with a 24 h warning, owner names, and
follow-up due / overdue under the status.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 10: NDR Sheet — parcel facts, attempts, follow-up form, resolve choices

**Files:**
- Create: `features/ndr/schema.ts`
- Create: `features/ndr/components/NdrAttemptsCard.tsx`, `features/ndr/components/NdrFollowUpCard.tsx`, `features/ndr/components/NdrResolveCard.tsx`
- Modify: `features/ndr/components/NdrDetailSheet.tsx` (rewrite)
- Create: `features/ndr/components/NdrDetailSheet.test.tsx`
- Modify: `features/ndr/hooks/useUpdateNdr.ts`, `features/ndr/hooks/useResolveNdr.ts` (summary invalidation)
- Delete: `features/ndr/components/NdrUpdateSection.tsx`, `NdrUpdateSection.test.tsx`, `NdrReattemptSection.tsx`, `NdrRtoSection.tsx`, `NdrRtoSection.test.tsx`, `NdrResolveOtherAction.tsx`

**Interfaces:**
- Consumes: `useNdr`, `useUpdateNdr`, `useResolveNdr`, `useDeliveryAttempts`, `ndrModel` (`NEXT_NDR_STATUSES`, `ndrStatusLabel`, `ndrReasonLabel`, `ownerLabel`), `formatAge`, `useShipment(awb)`, `useMerchants`, `formatRs`, `usePeopleNames`, `useSession`, `usePermission`, `NdrResolvedSummary` (unchanged).
- Produces: `ndrFollowUpSchema` / `NdrFollowUpFormValues`; `NdrAttemptsCard({ shipmentId })`; `NdrFollowUpCard({ ndr, onSaved })`; `NdrResolveCard({ ndr, onResolved })`.

- [ ] **Step 1: Write the failing tests**

Create `features/ndr/components/NdrDetailSheet.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { DeliveryAttempt, Ndr } from "@/features/ndr/types";
import type { Shipment } from "@/features/shipments/types";

let ndrQuery: { data?: Ndr; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/ndr/hooks/useNdr", () => ({ useNdr: () => ndrQuery }));
let shipmentQuery: { data?: Shipment; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/shipments/hooks/useShipment", () => ({ useShipment: () => shipmentQuery }));
vi.mock("@/features/shipments/hooks/useMerchants", () => ({ useMerchants: () => ({ data: [{ id: "mer1", displayName: "Daraz Seller", isActive: true }] }) }));
let attemptsQuery: { data?: { items: DeliveryAttempt[] }; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/ndr/hooks/useDeliveryAttempts", () => ({ useDeliveryAttempts: () => attemptsQuery }));
const updateMutateAsync = vi.fn();
vi.mock("@/features/ndr/hooks/useUpdateNdr", () => ({ useUpdateNdr: () => ({ mutateAsync: updateMutateAsync, isPending: false }) }));
const resolveMutateAsync = vi.fn();
vi.mock("@/features/ndr/hooks/useResolveNdr", () => ({ useResolveNdr: () => ({ mutateAsync: resolveMutateAsync, isPending: false }) }));
vi.mock("@/features/ndr/hooks/useReturnShipments", () => ({ useReturnShipments: () => ({ data: [] }) }));
vi.mock("@/features/ndr/store/returnShipmentPanelStore", () => ({
  useReturnShipmentPanelStore: (selector: (s: unknown) => unknown) => selector({ openDetail: vi.fn() }),
}));
vi.mock("@/features/auth/hooks/useSession", () => ({ useSession: () => ({ data: { userId: "u1" } }) }));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ riderName: () => "Hari Thapa", userName: () => "Sita Rai", branchName: () => "Branch" }),
}));
vi.mock("@/features/ndr/store/ndrPanelStore", () => ({
  useNdrPanelStore: (selector: (s: unknown) => unknown) => selector({ panel: { mode: "detail", id: "n1" }, close: vi.fn() }),
}));

import { NdrDetailSheet } from "./NdrDetailSheet";

const baseNdr: Ndr = {
  id: "n1", shipmentId: "s1", awb: "KTM-000301", reason: "CustomerUnavailable", assignedStaffUserId: "u1", status: "Open",
  customerResponse: null, followUpAt: null, nextAction: null, resolution: null, resolvedAt: null, createdAt: "2026-10-14T06:00:00Z",
};

beforeEach(() => {
  updateMutateAsync.mockReset().mockResolvedValue({});
  resolveMutateAsync.mockReset().mockResolvedValue({});
  granted = ["ndr.manage", "ndr.resolve"];
  ndrQuery = { data: baseNdr, isLoading: false, isError: false, refetch: vi.fn() };
  shipmentQuery = {
    data: { awb: "KTM-000301", recipientName: "Ram Shrestha", merchantId: "mer1", codAmount: 2500 } as Shipment,
    isLoading: false, isError: false, refetch: vi.fn(),
  };
  attemptsQuery = {
    data: { items: [
      { attemptNumber: 2, attemptedAt: "2026-10-14T05:00:00Z", succeeded: false, reason: "CustomerUnavailable", notes: "Gate locked", customerContacted: true, nextAction: "Reattempt", riderId: "r1" },
      { attemptNumber: 1, attemptedAt: "2026-10-13T05:00:00Z", succeeded: false, reason: "PhoneUnreachable", notes: null, customerContacted: false, nextAction: null, riderId: "r1" },
    ] },
    isLoading: false, isError: false, refetch: vi.fn(),
  };
});

describe("NdrDetailSheet", () => {
  it("shows the eyebrow, parcel facts from the shipment read and the attempt count", () => {
    render(<NdrDetailSheet />);
    expect(screen.getByText(/^NDR · Opened/)).toBeInTheDocument();
    expect(screen.getByText("Ram Shrestha")).toBeInTheDocument();
    expect(screen.getByText("Daraz Seller")).toBeInTheDocument();
    expect(screen.getByText("Rs 2,500")).toBeInTheDocument();
    expect(screen.getByText("Owner: you")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Assign to…" })).toBeDisabled();
  });

  it("falls back to dashes with a retry when the shipment read fails", () => {
    shipmentQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<NdrDetailSheet />);
    expect(screen.getByText("Parcel details are unavailable.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(shipmentQuery.refetch).toHaveBeenCalled();
  });

  it("lists attempts newest first with reason, rider and notes", () => {
    render(<NdrDetailSheet />);
    const attempts = screen.getByRole("list", { name: "Delivery attempts" });
    const rows = within(attempts).getAllByRole("listitem");
    expect(rows[0]).toHaveTextContent("Attempt 2");
    expect(rows[0]).toHaveTextContent("Customer unavailable · customer contacted");
    expect(rows[0]).toHaveTextContent("Hari Thapa");
    expect(rows[0]).toHaveTextContent("Gate locked");
    expect(rows[1]).toHaveTextContent("Phone unreachable");
  });

  it("gives the attempts card its own empty and error states", () => {
    attemptsQuery = { data: { items: [] }, isLoading: false, isError: false, refetch: vi.fn() };
    const { unmount } = render(<NdrDetailSheet />);
    expect(screen.getByText("No delivery attempts recorded.")).toBeInTheDocument();
    unmount();
    attemptsQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<NdrDetailSheet />);
    expect(screen.getByText("Couldn't load delivery attempts")).toBeInTheDocument();
  });

  it("submits the follow-up with the only legal next status", async () => {
    const user = userEvent.setup();
    render(<NdrDetailSheet />);
    await user.type(screen.getByLabelText("Customer response"), "Home after 5");
    await user.click(screen.getByRole("button", { name: "Save follow-up" }));
    await waitFor(() =>
      expect(updateMutateAsync).toHaveBeenCalledWith({ status: "Contacting", customerResponse: "Home after 5", nextAction: undefined, followUpAt: undefined }),
    );
    expect(await screen.findByText("Follow-up saved.")).toBeInTheDocument();
  });

  it("requires a next status when the NDR can branch", async () => {
    const user = userEvent.setup();
    ndrQuery = { ...ndrQuery, data: { ...baseNdr, status: "CustomerConfirmed" } };
    render(<NdrDetailSheet />);
    await user.click(screen.getByRole("button", { name: "Save follow-up" }));
    expect(await screen.findByText("Choose the next status.")).toBeInTheDocument();
    expect(updateMutateAsync).not.toHaveBeenCalled();
  });

  it("disables reattempt and return for an Open NDR and confirms 'Resolve other'", async () => {
    render(<NdrDetailSheet />);
    expect(screen.getByRole("radio", { name: /Reattempt/ })).toBeDisabled();
    expect(screen.getByRole("radio", { name: /Return to origin/ })).toBeDisabled();
    expect(screen.getByText("Available once the NDR is RTO requested.")).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: /Resolve other/ })).toBeChecked();
    fireEvent.click(screen.getByRole("button", { name: "Resolve" }));
    const dialog = screen.getByRole("alertdialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Resolve" }));
    await waitFor(() => expect(resolveMutateAsync).toHaveBeenCalledWith({ resolution: "Other", rtoReason: undefined }));
  });

  it("requires a reason before starting a return, then confirms", async () => {
    const user = userEvent.setup();
    ndrQuery = { ...ndrQuery, data: { ...baseNdr, status: "RtoRequested" } };
    render(<NdrDetailSheet />);
    expect(screen.getByRole("radio", { name: /Return to origin/ })).toBeChecked();
    await user.click(screen.getByRole("button", { name: "Start return" }));
    expect(screen.getByText("Give a reason for the return.")).toBeInTheDocument();
    expect(resolveMutateAsync).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText("Reason for the return"), "Refused twice");
    await user.click(screen.getByRole("button", { name: "Start return" }));
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Start return" }));
    await waitFor(() => expect(resolveMutateAsync).toHaveBeenCalledWith({ resolution: "Rto", rtoReason: "Refused twice" }));
  });

  it("schedules a reattempt without a dialog and explains the missing date", async () => {
    ndrQuery = { ...ndrQuery, data: { ...baseNdr, status: "ReattemptScheduled", followUpAt: "2026-10-16T04:00:00Z" } };
    render(<NdrDetailSheet />);
    expect(screen.queryByRole("button", { name: "Save follow-up" })).toBeNull();
    expect(screen.getByText(/booking a dated reattempt needs backend support/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Schedule reattempt" }));
    await waitFor(() => expect(resolveMutateAsync).toHaveBeenCalledWith({ resolution: "Reattempt", rtoReason: undefined }));
  });

  it("is read-only without ndr.manage and hides resolve without ndr.resolve", () => {
    granted = [];
    render(<NdrDetailSheet />);
    expect(screen.getByText("You can view this follow-up but not change it.")).toBeInTheDocument();
    expect(screen.queryByRole("radio")).toBeNull();
  });

  it("shows the resolved summary instead of both forms", () => {
    ndrQuery = { ...ndrQuery, data: { ...baseNdr, status: "Resolved", resolution: "Other", resolvedAt: "2026-10-15T00:00:00Z" } };
    render(<NdrDetailSheet />);
    expect(screen.getByText(/Resolved as/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Save follow-up" })).toBeNull();
    expect(screen.queryByRole("radio")).toBeNull();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npx vitest run src/features/ndr/components/NdrDetailSheet.test.tsx`
Expected: FAIL — no eyebrow, facts, attempts card, follow-up card or resolve choices.

- [ ] **Step 3: Schema**

Create `features/ndr/schema.ts`:

```ts
import { z } from "zod";

// Status lists only the backend's legal next moves (NEXT_NDR_STATUSES), so a chosen value is
// always valid; empty means the user has not picked between two branches yet.
export const ndrFollowUpSchema = z.object({
  status: z.string().min(1, "Choose the next status."),
  customerResponse: z.string(),
  nextAction: z.string(),
  followUpAt: z.string(),
});

export type NdrFollowUpFormValues = z.infer<typeof ndrFollowUpSchema>;
```

- [ ] **Step 4: Attempts card**

Create `features/ndr/components/NdrAttemptsCard.tsx`:

```tsx
"use client";

import { Truck } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { SectionCard } from "@/components/page/SectionCard";
import { ErrorState } from "@/components/states/ErrorState";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useDeliveryAttempts } from "@/features/ndr/hooks/useDeliveryAttempts";
import { ndrReasonLabel } from "@/features/ndr/lib/ndrModel";

const attemptFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });

export function NdrAttemptsCard({ shipmentId }: { shipmentId: string }) {
  const attempts = useDeliveryAttempts(shipmentId);
  const names = usePeopleNames();

  return (
    <SectionCard title="Delivery attempts" icon={Truck}>
      {attempts.isLoading && <Skeleton className="h-20 w-full" />}
      {attempts.isError && (
        <ErrorState title="Couldn't load delivery attempts" description="Try again in a moment." onRetry={() => attempts.refetch()} />
      )}
      {attempts.data && attempts.data.items.length === 0 && <p className="text-sm text-muted-foreground">No delivery attempts recorded.</p>}
      {attempts.data && attempts.data.items.length > 0 && (
        <ol aria-label="Delivery attempts" className="divide-y divide-border">
          {attempts.data.items.map((a) => (
            <li key={`${a.attemptNumber}-${a.attemptedAt}`} className="space-y-0.5 py-2.5">
              <div className="flex items-baseline justify-between gap-3">
                <span className="text-sm font-semibold">{`Attempt ${a.attemptNumber}`}</span>
                <time dateTime={a.attemptedAt} className="whitespace-nowrap text-xs text-muted-foreground">
                  {attemptFormat.format(new Date(a.attemptedAt))}
                </time>
              </div>
              <p className="text-sm">
                {`${a.succeeded ? "Delivered" : a.reason ? ndrReasonLabel(a.reason) : "Failed"}${a.customerContacted ? " · customer contacted" : ""}`}
              </p>
              <p className="text-xs text-muted-foreground">{names.riderName(a.riderId)}</p>
              {a.notes && <p className="text-xs text-muted-foreground">{a.notes}</p>}
              {a.nextAction && <p className="text-xs text-muted-foreground">{`Next: ${a.nextAction}`}</p>}
            </li>
          ))}
        </ol>
      )}
    </SectionCard>
  );
}
```

- [ ] **Step 5: Follow-up card**

Create `features/ndr/components/NdrFollowUpCard.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Controller, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { MessageSquare } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DateTimePicker } from "@/components/ui/date-time-picker";
import { FieldError } from "@/components/ui/field-error";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { Textarea } from "@/components/ui/textarea";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { SectionCard } from "@/components/page/SectionCard";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useSession } from "@/features/auth/hooks/useSession";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useUpdateNdr } from "@/features/ndr/hooks/useUpdateNdr";
import { NEXT_NDR_STATUSES, ndrStatusLabel, ownerLabel } from "@/features/ndr/lib/ndrModel";
import { ndrFollowUpSchema, type NdrFollowUpFormValues } from "@/features/ndr/schema";
import type { Ndr, NdrStatus } from "@/features/ndr/types";

const followUpFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });

function Recorded({ ndr }: { ndr: Ndr }) {
  const rows = [
    { label: "Customer response", value: ndr.customerResponse },
    { label: "Next action", value: ndr.nextAction },
    { label: "Follow up", value: ndr.followUpAt ? followUpFormat.format(new Date(ndr.followUpAt)) : null },
  ].filter((r) => r.value);
  if (rows.length === 0) return null;
  return (
    <dl className="mb-4 grid grid-cols-1 gap-2 rounded-lg bg-muted/50 p-3 sm:grid-cols-3">
      {rows.map((r) => (
        <div key={r.label} className="space-y-0.5">
          <dt className="text-2xs text-muted-foreground">{r.label}</dt>
          <dd className="text-sm">{r.value}</dd>
        </div>
      ))}
    </dl>
  );
}

// UpdateAsync accepts exactly one legal forward move per save, so the form exists only while a
// move is possible (Open, Contacting, CustomerConfirmed) and Status lists only those moves.
export function NdrFollowUpCard({ ndr, onSaved }: { ndr: Ndr; onSaved: () => void }) {
  const canManage = usePermission("ndr.manage");
  const { data: session } = useSession();
  const names = usePeopleNames();
  const updateNdr = useUpdateNdr(ndr.id);
  const [serverError, setServerError] = useState<string | null>(null);
  const moves = NEXT_NDR_STATUSES[ndr.status];
  const editable = canManage && moves.length > 0;

  const { register, control, handleSubmit, formState: { errors } } = useForm<NdrFollowUpFormValues>({
    resolver: zodResolver(ndrFollowUpSchema),
    defaultValues: { status: moves.length === 1 ? moves[0] : "", customerResponse: "", nextAction: "", followUpAt: "" },
  });

  async function onSubmit(values: NdrFollowUpFormValues) {
    setServerError(null);
    try {
      await updateNdr.mutateAsync({
        status: values.status as NdrStatus,
        customerResponse: values.customerResponse.trim() || undefined,
        nextAction: values.nextAction.trim() || undefined,
        followUpAt: values.followUpAt ? new Date(values.followUpAt).toISOString() : undefined,
      });
      onSaved();
    } catch {
      setServerError("Couldn't save this follow-up. Check the fields and try again.");
    }
  }

  const owner = ownerLabel(ndr.assignedStaffUserId, session?.userId, names.userName);
  const ownerAction = (
    <div className="flex items-center gap-2">
      <span className="text-xs text-muted-foreground">{owner === "You" ? "Owner: you" : `Owner: ${owner}`}</span>
      <Tooltip>
        <TooltipTrigger asChild>
          <span tabIndex={0}>
            <Button type="button" variant="outline" size="sm" disabled>Assign to…</Button>
          </span>
        </TooltipTrigger>
        <TooltipContent>Needs backend</TooltipContent>
      </Tooltip>
    </div>
  );

  return (
    <SectionCard title="Follow-up" icon={MessageSquare} action={ownerAction}>
      <Recorded ndr={ndr} />
      {!canManage && <p className="text-sm text-muted-foreground">You can view this follow-up but not change it.</p>}
      {canManage && !editable && <p className="text-sm text-muted-foreground">The customer outcome is recorded. Resolve the NDR below.</p>}
      {editable && (
        <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="ndr-customer-response">Customer response</Label>
            <Textarea id="ndr-customer-response" {...register("customerResponse")} />
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="ndr-next-status">Next status</Label>
              <Controller
                name="status"
                control={control}
                render={({ field }) => (
                  <Select value={field.value || undefined} onValueChange={field.onChange}>
                    <SelectTrigger
                      id="ndr-next-status"
                      aria-invalid={!!errors.status}
                      aria-describedby={errors.status ? "ndr-next-status-error" : undefined}
                    >
                      <SelectValue placeholder="Choose…" />
                    </SelectTrigger>
                    <SelectContent>
                      {moves.map((s) => (
                        <SelectItem key={s} value={s}>{ndrStatusLabel(s)}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
              {errors.status && <FieldError id="ndr-next-status-error">{errors.status.message}</FieldError>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="ndr-follow-up-at">Follow up again</Label>
              <Controller
                name="followUpAt"
                control={control}
                render={({ field }) => <DateTimePicker id="ndr-follow-up-at" value={field.value} onChange={field.onChange} clearable />}
              />
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="ndr-next-action">Next action note</Label>
            <Input id="ndr-next-action" {...register("nextAction")} />
          </div>
          {serverError && <FieldError id="ndr-follow-up-server-error">{serverError}</FieldError>}
          <Button type="submit" variant="outline" disabled={updateNdr.isPending}>
            {updateNdr.isPending ? <Spinner className="h-4 w-4" /> : "Save follow-up"}
          </Button>
        </form>
      )}
    </SectionCard>
  );
}
```

- [ ] **Step 6: Resolve card**

Create `features/ndr/components/NdrResolveCard.tsx`:

```tsx
"use client";

import { useState } from "react";
import { CheckCircle2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { FieldError } from "@/components/ui/field-error";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { Textarea } from "@/components/ui/textarea";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { SectionCard } from "@/components/page/SectionCard";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useResolveNdr } from "@/features/ndr/hooks/useResolveNdr";
import { cn } from "@/lib/utils";
import type { Ndr, NdrStatus } from "@/features/ndr/types";

type Choice = "Reattempt" | "Rto" | "Other";

// NdrService.ResolveAsync: Reattempt only from ReattemptScheduled, Rto only from RtoRequested,
// Other from any open state. Unavailable choices stay visible, disabled, and say why.
const CHOICES: { key: Choice; label: string; description: string; requires?: NdrStatus; unavailable?: string }[] = [
  { key: "Reattempt", label: "Reattempt", description: "Send the parcel out for delivery again.", requires: "ReattemptScheduled", unavailable: "Available once the NDR is Reattempt scheduled." },
  { key: "Rto", label: "Return to origin", description: "Send the parcel back to the merchant.", requires: "RtoRequested", unavailable: "Available once the NDR is RTO requested." },
  { key: "Other", label: "Resolve other", description: "Close the NDR with no parcel movement." },
];

const SUBMIT_LABEL: Record<Choice, string> = { Reattempt: "Schedule reattempt", Rto: "Start return", Other: "Resolve" };

function defaultChoice(status: NdrStatus): Choice {
  if (status === "ReattemptScheduled") return "Reattempt";
  if (status === "RtoRequested") return "Rto";
  return "Other";
}

export function NdrResolveCard({ ndr, onResolved }: { ndr: Ndr; onResolved: () => void }) {
  const canResolve = usePermission("ndr.resolve");
  const resolveNdr = useResolveNdr(ndr.id);
  const [choice, setChoice] = useState<Choice>(defaultChoice(ndr.status));
  const [rtoReason, setRtoReason] = useState("");
  const [reasonError, setReasonError] = useState<string | null>(null);
  const [serverError, setServerError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  if (!canResolve) return null;

  async function submit() {
    setServerError(null);
    try {
      await resolveNdr.mutateAsync({ resolution: choice, rtoReason: choice === "Rto" ? rtoReason.trim() : undefined });
      onResolved();
    } catch {
      setServerError("Couldn't resolve this NDR. Try again.");
    }
  }

  function onPrimary() {
    if (choice === "Rto" && !rtoReason.trim()) {
      setReasonError("Give a reason for the return.");
      return;
    }
    setReasonError(null);
    if (choice === "Reattempt") void submit();
    else setConfirming(true);
  }

  return (
    <SectionCard title="Resolve" icon={CheckCircle2}>
      <div className="space-y-4">
        <fieldset className="space-y-2">
          <legend className="sr-only">Resolution</legend>
          {CHOICES.map((c) => {
            const enabled = !c.requires || c.requires === ndr.status;
            const checked = choice === c.key;
            return (
              <label
                key={c.key}
                className={cn(
                  "flex gap-3 rounded-lg border p-3 focus-within:ring-2 focus-within:ring-ring",
                  checked ? "border-primary bg-accent" : "border-border",
                  enabled ? "cursor-pointer" : "cursor-not-allowed opacity-60",
                )}
              >
                <input
                  type="radio"
                  name={`ndr-resolution-${ndr.id}`}
                  value={c.key}
                  checked={checked}
                  disabled={!enabled}
                  onChange={() => setChoice(c.key)}
                  className="mt-0.5 h-4 w-4 accent-primary"
                />
                <span className="space-y-0.5">
                  <span className="block text-sm font-medium">{c.label}</span>
                  <span className="block text-xs text-muted-foreground">{enabled ? c.description : c.unavailable}</span>
                </span>
              </label>
            );
          })}
        </fieldset>

        {choice === "Reattempt" && (
          <p data-coming-soon="" className="text-xs text-muted-foreground">
            The follow-up date is the scheduling note; booking a dated reattempt needs backend support.
          </p>
        )}

        {choice === "Rto" && (
          <div className="space-y-2">
            <Label htmlFor="ndr-rto-reason">Reason for the return</Label>
            <Textarea
              id="ndr-rto-reason"
              value={rtoReason}
              onChange={(e) => setRtoReason(e.target.value)}
              aria-invalid={!!reasonError}
              aria-describedby={reasonError ? "ndr-rto-reason-error" : undefined}
            />
            {reasonError && <FieldError id="ndr-rto-reason-error">{reasonError}</FieldError>}
          </div>
        )}

        {serverError && <FieldError id="ndr-resolve-server-error">{serverError}</FieldError>}
        <Button type="button" onClick={onPrimary} disabled={resolveNdr.isPending}>
          {resolveNdr.isPending ? <Spinner className="h-4 w-4" /> : SUBMIT_LABEL[choice]}
        </Button>
      </div>

      <AlertDialog open={confirming} onOpenChange={setConfirming}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{choice === "Rto" ? `Start the return for ${ndr.awb}?` : `Resolve ${ndr.awb} with no parcel movement?`}</AlertDialogTitle>
            <AlertDialogDescription>
              {choice === "Rto"
                ? "The parcel heads back to the origin branch and a return shipment is created."
                : "The NDR closes. Nothing moves the parcel."}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Go back</AlertDialogCancel>
            <AlertDialogAction onClick={() => void submit()}>{SUBMIT_LABEL[choice]}</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </SectionCard>
  );
}
```

- [ ] **Step 7: Rewrite the Sheet**

Replace `features/ndr/components/NdrDetailSheet.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet";
import { TooltipProvider } from "@/components/ui/tooltip";
import { ErrorState } from "@/components/states/ErrorState";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { formatRs } from "@/features/cod/lib/codFormat";
import { useShipment } from "@/features/shipments/hooks/useShipment";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { useNdr } from "@/features/ndr/hooks/useNdr";
import { useDeliveryAttempts } from "@/features/ndr/hooks/useDeliveryAttempts";
import { useNdrPanelStore } from "@/features/ndr/store/ndrPanelStore";
import { NdrAttemptsCard } from "@/features/ndr/components/NdrAttemptsCard";
import { NdrFollowUpCard } from "@/features/ndr/components/NdrFollowUpCard";
import { NdrResolveCard } from "@/features/ndr/components/NdrResolveCard";
import { NdrResolvedSummary } from "@/features/ndr/components/NdrResolvedSummary";
import { formatAge } from "@/lib/dates/age";

const openedFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });

export function NdrDetailSheet() {
  const panel = useNdrPanelStore((s) => s.panel);
  const close = useNdrPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";
  const [notice, setNotice] = useState<{ id: string; text: string } | null>(null);

  const { data: ndr, isLoading, isError, refetch } = useNdr(id);
  // The NDR list does not serve recipient/merchant/COD (follow-up 5) — read them from the shipment.
  const shipment = useShipment(ndr?.awb ?? "");
  const attempts = useDeliveryAttempts(ndr?.shipmentId ?? "");
  const { data: merchants } = useMerchants();

  const noticeText = notice?.id === id ? notice.text : null;
  const merchantName = shipment.data ? merchants?.find((m) => m.id === shipment.data!.merchantId)?.displayName ?? "—" : "—";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <TooltipProvider>
          <div className="flex flex-col gap-6">
            {!ndr && <SheetTitle className="sr-only">NDR</SheetTitle>}
            {isLoading && <DetailSkeleton fieldRows={1} />}
            {isError && (
              <ErrorState title="Couldn't load this NDR" description="It may not exist, or you may not have access to it." onRetry={() => refetch()} />
            )}
            {ndr && (
              <>
                <SheetHeaderBlock
                  eyebrow={`NDR · Opened ${openedFormat.format(new Date(ndr.createdAt))} · ${formatAge(ndr.createdAt)}`}
                  title={ndr.awb}
                  mono
                  status={<StatusBadge status={ndr.status} namespace="Ndr" />}
                  facts={[
                    { label: "Recipient", value: shipment.data?.recipientName ?? "—" },
                    { label: "Merchant", value: merchantName },
                    { label: "COD", value: shipment.data ? formatRs(shipment.data.codAmount) : "—" },
                    { label: "Attempts", value: attempts.data ? String(attempts.data.items.length) : "—" },
                  ]}
                />
                {shipment.isError && (
                  <p className="text-xs text-muted-foreground">
                    Parcel details are unavailable.{" "}
                    <Button type="button" variant="link" size="sm" className="h-auto p-0 text-xs" onClick={() => shipment.refetch()}>
                      Retry
                    </Button>
                  </p>
                )}
                {noticeText && <p role="status" className="text-sm font-medium text-foreground">{noticeText}</p>}

                <NdrAttemptsCard shipmentId={ndr.shipmentId} />

                {ndr.status === "Resolved" ? (
                  <NdrResolvedSummary ndr={ndr} />
                ) : (
                  <>
                    <NdrFollowUpCard key={`follow-up-${ndr.status}`} ndr={ndr} onSaved={() => setNotice({ id, text: "Follow-up saved." })} />
                    <NdrResolveCard key={`resolve-${ndr.status}`} ndr={ndr} onResolved={() => setNotice({ id, text: "NDR resolved." })} />
                  </>
                )}
              </>
            )}
          </div>
        </TooltipProvider>
      </SheetContent>
    </Sheet>
  );
}
```

The `key`s remount both cards when the status changes after a save, so the form re-reads its legal moves and the resolve default.

The status strip reads `["ndrs-summary"]`, outside the `["ndrs"]` prefix the mutations invalidate. In `hooks/useUpdateNdr.ts` add `queryClient.invalidateQueries({ queryKey: ["ndrs-summary"] });` to `onSuccess`; in `hooks/useResolveNdr.ts` add both `["ndrs-summary"]` and `["return-shipments-summary"]`.

Delete the replaced sections:

```bash
git rm src/features/ndr/components/NdrUpdateSection.tsx src/features/ndr/components/NdrUpdateSection.test.tsx src/features/ndr/components/NdrReattemptSection.tsx src/features/ndr/components/NdrRtoSection.tsx src/features/ndr/components/NdrRtoSection.test.tsx src/features/ndr/components/NdrResolveOtherAction.tsx
```

- [ ] **Step 8: Run the NDR tests**

Run: `npx vitest run src/features/ndr "src/app/(staff)/ndrs"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add -A src/features/ndr
git commit -m "$(cat <<'EOF'
feat(web): NDR Sheet with attempts, follow-up form and resolve choices

Parcel facts from the shipment read, attempt history from the new
delivery-attempts read, a React Hook Form follow-up that offers only the
backend's legal next status, and resolve radio cards that enable each
choice only in the state the backend accepts (RTO needs a reason, RTO and
Other confirm first). Assign owner and dated reattempt need backend.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 11: Returns page and Sheet — stage strip, return chain, hand back

**Files:**
- Modify: `app/(staff)/return-shipments/page.tsx` (rewrite), `app/(staff)/return-shipments/page.test.tsx` (rewrite)
- Modify: `features/ndr/components/ReturnShipmentDetailSheet.tsx` (rewrite), `features/ndr/components/ReturnShipmentDetailSheet.test.tsx` (rewrite)
- Modify: `features/ndr/hooks/useConfirmReturnShipment.ts` (summary + events invalidation)

**Interfaces:**
- Consumes: `returnModel` (Task 6); `useSearchParamState`, `useFilterPage`, `toClientPage`, `branchCode`, `PipelineStrip`, `NdrRtoSwitch` (Task 5); `useReturnShipments`, `useReturnShipmentsSummary`, `useReturnShipment`, `useConfirmReturnShipment`, `useShipment`, `useShipmentEvents`, `useMerchants`, `TimelineTab` (R2), `ComingSoonSlot`, `useShipmentPanelStore`.
- Produces: URL contract for `/return-shipments`: `?stage=<ReturnStageKey>` (client-side filter).

- [ ] **Step 1: Write the failing tests**

Replace `app/(staff)/return-shipments/page.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { ReturnShipment } from "@/features/ndr/types";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/return-shipments",
  useSearchParams: () => new URLSearchParams(search),
}));

let listQuery: { data?: ReturnShipment[]; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/ndr/hooks/useReturnShipments", () => ({ useReturnShipments: () => listQuery }));
let summary: { data?: { byRtoStage: Record<string, number>; merchantConfirmed: number; pendingConfirmation: number }; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/ndr/hooks/useReturnShipmentsSummary", () => ({ useReturnShipmentsSummary: () => summary }));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", code: "KTM", name: "Kathmandu Hub" }, { id: "b2", code: "PKR", name: "Pokhara Hub" }] }),
}));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ branchName: (id: string | null) => (id === "b1" ? "Kathmandu Hub" : "Pokhara Hub"), userName: () => "Staff", riderName: () => "Rider" }),
}));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: () => true }));
const openDetail = vi.fn();
vi.mock("@/features/ndr/store/returnShipmentPanelStore", () => ({
  useReturnShipmentPanelStore: (selector: (s: unknown) => unknown) => selector({ panel: { mode: "closed" }, openDetail }),
}));

import ReturnShipmentsPage from "./page";

function row(overrides: Partial<ReturnShipment>): ReturnShipment {
  return {
    id: "r1", originalShipmentId: "s1", originalAwb: "KTM-000301", returnAwb: "RTO-20261015-001", rtoReason: "Refused twice",
    originBranchId: "b2", destinationBranchId: "b1", currentStatus: "RtoAtOrigin", currentBranchId: "b1", merchantConfirmedAt: null,
    ...overrides,
  };
}

beforeEach(() => {
  replace.mockReset();
  openDetail.mockReset();
  search = "";
  summary = { data: { byRtoStage: { RtoInTransit: 2, RtoAtOrigin: 3, RtoDelivered: 4 }, merchantConfirmed: 4, pendingConfirmation: 5 }, isLoading: false, isError: false, refetch: vi.fn() };
  listQuery = {
    data: [row({}), row({ id: "r2", returnAwb: "RTO-20261015-002", currentStatus: "RtoInTransit", currentBranchId: null })],
    isLoading: false, isError: false, refetch: vi.fn(),
  };
});

describe("ReturnShipmentsPage", () => {
  it("shows the five-stage strip with At origin highlighted and the NDR/Returns switch", () => {
    render(<ReturnShipmentsPage />);
    expect(screen.getByRole("heading", { name: "Returns to origin" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Returns" })).toHaveAttribute("aria-selected", "true");
    const strip = screen.getByRole("list", { name: "Return stages" });
    expect(strip.querySelectorAll("li")).toHaveLength(5);
    expect(screen.getByRole("button", { name: /^At origin/ })).toHaveAttribute("aria-current", "step");
    expect(screen.getByRole("button", { name: /^Pending confirmation/ })).toHaveTextContent("5");
  });

  it("filters rows by the stage in the URL and writes stage clicks back", () => {
    search = "stage=RtoInTransit";
    render(<ReturnShipmentsPage />);
    const table = screen.getByRole("table");
    expect(within(table).getByText("RTO-20261015-002")).toBeInTheDocument();
    expect(within(table).queryByText("RTO-20261015-001")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: /^At origin/ }));
    expect(replace).toHaveBeenLastCalledWith("/return-shipments?stage=RtoAtOrigin", { scroll: false });
  });

  it("links each return to its original, routes by code and falls back to 'In transit'", () => {
    render(<ReturnShipmentsPage />);
    const table = screen.getByRole("table");
    expect(within(table).getAllByText("from KTM-000301")).toHaveLength(2);
    expect(within(table).getAllByText("PKR → KTM")).toHaveLength(2);
    expect(within(table).getByText("In transit")).toBeInTheDocument();
    expect(within(table).getAllByText("Not confirmed")).toHaveLength(2);
    fireEvent.click(within(table).getByRole("button", { name: /RTO-20261015-001/ }));
    expect(openDetail).toHaveBeenCalledWith("r1");
  });

  it("never shows zeros while loading, and shows an error when the list fails", () => {
    summary = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    listQuery = { data: undefined, isLoading: false, isError: true, refetch: vi.fn() };
    render(<ReturnShipmentsPage />);
    expect(screen.getByRole("status", { name: "Loading return stages" })).toBeInTheDocument();
    expect(screen.getByText("Couldn't load returns")).toBeInTheDocument();
  });
});
```

Replace `features/ndr/components/ReturnShipmentDetailSheet.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, within } from "@testing-library/react";
import { renderWithIntl as render } from "@/test/renderWithIntl";
import type { ReturnShipment } from "@/features/ndr/types";
import type { Shipment, ShipmentEvent } from "@/features/shipments/types";

let returnQuery: { data?: ReturnShipment; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/ndr/hooks/useReturnShipment", () => ({ useReturnShipment: () => returnQuery }));
const confirmMutate = vi.fn();
vi.mock("@/features/ndr/hooks/useConfirmReturnShipment", () => ({ useConfirmReturnShipment: () => ({ mutate: confirmMutate, isPending: false, isError: false }) }));
let shipmentQuery: { data?: Shipment; isError: boolean };
vi.mock("@/features/shipments/hooks/useShipment", () => ({ useShipment: () => shipmentQuery }));
const useShipmentEvents = vi.fn();
vi.mock("@/features/shipments/hooks/useShipmentEvents", () => ({ useShipmentEvents: (awb: string) => useShipmentEvents(awb) }));
vi.mock("@/features/shipments/hooks/useMerchants", () => ({ useMerchants: () => ({ data: [{ id: "mer1", displayName: "Daraz Seller", isActive: true }] }) }));
vi.mock("@/features/shipments/components/detail/TimelineTab", () => ({
  TimelineTab: ({ events }: { events: ShipmentEvent[] }) => <p>{`timeline with ${events.length} events`}</p>,
}));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ branchName: (id: string | null) => (id === "b1" ? "Kathmandu Hub" : "Pokhara Hub"), userName: () => "Staff", riderName: () => "Rider" }),
}));
let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));
const openShipment = vi.fn();
vi.mock("@/features/shipments/store/shipmentPanelStore", () => ({
  useShipmentPanelStore: (selector: (s: unknown) => unknown) => selector({ openDetail: openShipment }),
}));
vi.mock("@/features/ndr/store/returnShipmentPanelStore", () => ({
  useReturnShipmentPanelStore: (selector: (s: unknown) => unknown) => selector({ panel: { mode: "detail", id: "r1" }, close: vi.fn() }),
}));

import { ReturnShipmentDetailSheet } from "./ReturnShipmentDetailSheet";

const base: ReturnShipment = {
  id: "r1", originalShipmentId: "s1", originalAwb: "KTM-000301", returnAwb: "RTO-20261015-001", rtoReason: "Refused twice",
  originBranchId: "b2", destinationBranchId: "b1", currentStatus: "RtoAtOrigin", currentBranchId: "b1", merchantConfirmedAt: null,
};

beforeEach(() => {
  confirmMutate.mockReset();
  openShipment.mockReset();
  granted = ["rto.manage"];
  returnQuery = { data: base, isLoading: false, isError: false, refetch: vi.fn() };
  shipmentQuery = { data: { awb: "KTM-000301", merchantId: "mer1" } as Shipment, isError: false };
  useShipmentEvents.mockReset();
  useShipmentEvents.mockReturnValue({ data: [{ id: "e1" }, { id: "e2" }], isLoading: false, isError: false, refetch: vi.fn() });
});

describe("ReturnShipmentDetailSheet", () => {
  it("renders the header with reason, back-to, now-at and merchant", () => {
    render(<ReturnShipmentDetailSheet />);
    expect(screen.getByText("RETURN · Refused twice")).toBeInTheDocument();
    expect(screen.getByText("RTO-20261015-001", { selector: "h2" })).toBeInTheDocument();
    expect(screen.getAllByText("Kathmandu Hub")).toHaveLength(2);
    expect(screen.getByText("Daraz Seller")).toBeInTheDocument();
  });

  it("links to the original shipment and tells its story from the original AWB", () => {
    render(<ReturnShipmentDetailSheet />);
    expect(useShipmentEvents).toHaveBeenCalledWith("KTM-000301");
    expect(screen.getByText("timeline with 2 events")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Open original shipment KTM-000301" }));
    expect(openShipment).toHaveBeenCalledWith("KTM-000301");
  });

  it("offers hand-back at origin with handover details as coming soon, behind a confirm", () => {
    render(<ReturnShipmentDetailSheet />);
    expect(screen.getByRole("region", { name: "Handover details" })).toHaveAttribute("data-coming-soon");
    fireEvent.click(screen.getByRole("button", { name: "Confirm handover" }));
    const dialog = screen.getByRole("alertdialog");
    expect(confirmMutate).not.toHaveBeenCalled();
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirm handover" }));
    expect(confirmMutate).toHaveBeenCalledTimes(1);
  });

  it("hides hand-back before origin, without rto.manage, and once confirmed", () => {
    returnQuery = { ...returnQuery, data: { ...base, currentStatus: "RtoInTransit", currentBranchId: null } };
    const first = render(<ReturnShipmentDetailSheet />);
    expect(screen.queryByRole("button", { name: "Confirm handover" })).toBeNull();
    expect(screen.getByText("In transit")).toBeInTheDocument();
    expect(screen.getByText("Hand-back opens once the parcel is at the origin branch.")).toBeInTheDocument();
    first.unmount();

    granted = [];
    returnQuery = { ...returnQuery, data: base };
    const second = render(<ReturnShipmentDetailSheet />);
    expect(screen.queryByRole("button", { name: "Confirm handover" })).toBeNull();
    second.unmount();

    granted = ["rto.manage"];
    returnQuery = { ...returnQuery, data: { ...base, currentStatus: "RtoDelivered", merchantConfirmedAt: "2026-10-15T08:00:00Z" } };
    render(<ReturnShipmentDetailSheet />);
    expect(screen.queryByRole("button", { name: "Confirm handover" })).toBeNull();
    expect(screen.getByText(/^Confirmed by merchant/)).toBeInTheDocument();
  });

  it("shows dashes and an error state when the original's reads fail", () => {
    shipmentQuery = { data: undefined, isError: true };
    useShipmentEvents.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    render(<ReturnShipmentDetailSheet />);
    expect(screen.getByText("Couldn't load the parcel history")).toBeInTheDocument();
    expect(screen.queryByText("Daraz Seller")).toBeNull();
  });
});
```

`SheetTitle` renders an `h2`; the `selector` keeps the title assertion from colliding with the linked "Return" card, which shows the same AWB.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npx vitest run "src/app/(staff)/return-shipments" src/features/ndr/components/ReturnShipmentDetailSheet.test.tsx`
Expected: FAIL.

- [ ] **Step 3: Implement the page**

Replace `app/(staff)/return-shipments/page.tsx`:

```tsx
"use client";

import { Suspense } from "react";
import { Skeleton } from "@/components/ui/skeleton";
import { PageHeader } from "@/components/page/PageHeader";
import { PipelineStrip, type PipelineStage } from "@/components/page/PipelineStrip";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { branchCode } from "@/features/branches/lib/branchCode";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useReturnShipments } from "@/features/ndr/hooks/useReturnShipments";
import { useReturnShipmentsSummary } from "@/features/ndr/hooks/useReturnShipmentsSummary";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";
import { NdrRtoSwitch } from "@/features/ndr/components/NdrRtoSwitch";
import { RETURN_STAGES, matchesReturnStage, parseReturnStage, returnStageCount } from "@/features/ndr/lib/returnModel";
import { useFilterPage } from "@/lib/hooks/useFilterPage";
import { useSearchParamState } from "@/lib/hooks/useSearchParamState";
import { toClientPage } from "@/lib/paging/clientPage";
import type { ReturnShipment } from "@/features/ndr/types";

const PAGE_SIZE = 20;
const confirmedFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric" });

function ReturnShipmentsPageContent() {
  const { params, update } = useSearchParamState();
  const stage = parseReturnStage(params.get("stage"));
  const [page, setPage] = useFilterPage(stage ?? "");

  const list = useReturnShipments();
  const summary = useReturnShipmentsSummary();
  const { data: branches } = useBranches();
  const names = usePeopleNames();
  const panel = useReturnShipmentPanelStore((s) => s.panel);
  const openDetail = useReturnShipmentPanelStore((s) => s.openDetail);

  const rows = list.data?.filter((r) => !stage || matchesReturnStage(r, stage));
  const stages: PipelineStage[] = RETURN_STAGES.map((s) => ({
    key: s.key,
    label: s.label,
    token: s.token,
    count: summary.data ? returnStageCount(summary.data, s.key) : null,
  }));
  const highlight = summary.data && returnStageCount(summary.data, "RtoAtOrigin") > 0 ? "RtoAtOrigin" : undefined;

  const columns: ColumnDef<ReturnShipment>[] = [
    {
      key: "returnAwb",
      header: "Return AWB",
      render: (r) => (
        <button type="button" onClick={() => openDetail(r.id)} className="flex flex-col items-start gap-0.5 text-left">
          <span className="font-mono text-sm font-medium text-foreground hover:underline">{r.returnAwb}</span>
          <span className="font-mono text-xs text-muted-foreground">{`from ${r.originalAwb || "—"}`}</span>
        </button>
      ),
    },
    { key: "reason", header: "Reason", render: (r) => <span className="line-clamp-2 max-w-56 text-sm">{r.rtoReason}</span> },
    {
      key: "route",
      header: "Route",
      render: (r) => <span className="font-mono text-sm">{`${branchCode(branches, r.originBranchId)} → ${branchCode(branches, r.destinationBranchId)}`}</span>,
    },
    { key: "nowAt", header: "Now at", render: (r) => <span className="text-sm">{r.currentBranchId ? names.branchName(r.currentBranchId) : "In transit"}</span> },
    {
      key: "status",
      header: "Status",
      render: (r) => (
        <div className="flex flex-col items-start gap-1">
          <StatusBadge status={r.currentStatus} />
          <span className="text-xs text-muted-foreground">
            {r.merchantConfirmedAt ? `Merchant confirmed ${confirmedFormat.format(new Date(r.merchantConfirmedAt))}` : "Not confirmed"}
          </span>
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-5">
      <PageHeader eyebrow="Operations · NDR & RTO" title="Returns to origin" actions={<NdrRtoSwitch active="returns" />} />

      {summary.isLoading && (
        <div role="status" aria-label="Loading return stages" className="grid grid-cols-2 gap-2.5 sm:grid-cols-5">
          {RETURN_STAGES.map((s) => (
            <Skeleton key={s.key} className="h-20 rounded-lg" />
          ))}
        </div>
      )}
      {summary.isError && (
        <ErrorState title="Couldn't load return counts" description="The stage strip is unavailable right now." onRetry={() => summary.refetch()} />
      )}
      {summary.data && (
        <PipelineStrip
          ariaLabel="Return stages"
          stages={stages}
          highlightKey={highlight}
          selectedKey={stage}
          onSelect={(key) => update({ stage: key === stage ? undefined : key })}
        />
      )}

      {list.isError ? (
        <ErrorState title="Couldn't load returns" description="Something went wrong fetching the return list." onRetry={() => list.refetch()} />
      ) : (
        <DataTable
          columns={columns}
          data={rows && toClientPage(rows, page, PAGE_SIZE)}
          isLoading={list.isLoading}
          emptyTitle="No returns here"
          emptyDescription={stage ? "Nothing is at this stage right now." : "Shipments resolved as RTO show up here."}
          onPageChange={setPage}
          rowKey={(r) => r.id}
          selectedRowId={panel.mode === "detail" ? panel.id : undefined}
        />
      )}
    </div>
  );
}

export default function ReturnShipmentsPage() {
  return (
    <Suspense>
      <ReturnShipmentsPageContent />
    </Suspense>
  );
}
```

- [ ] **Step 4: Implement the Sheet**

Replace `features/ndr/components/ReturnShipmentDetailSheet.tsx`:

```tsx
"use client";

import { ArrowRight, History, PackageCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle, AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { SectionCard } from "@/components/page/SectionCard";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { ErrorState } from "@/components/states/ErrorState";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useShipment } from "@/features/shipments/hooks/useShipment";
import { useShipmentEvents } from "@/features/shipments/hooks/useShipmentEvents";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import { TimelineTab } from "@/features/shipments/components/detail/TimelineTab";
import { useReturnShipment } from "@/features/ndr/hooks/useReturnShipment";
import { useConfirmReturnShipment } from "@/features/ndr/hooks/useConfirmReturnShipment";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";
import { canHandBack } from "@/features/ndr/lib/returnModel";

const confirmedFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" });

function shortReason(reason: string): string {
  return reason.length > 40 ? `${reason.slice(0, 39)}…` : reason;
}

export function ReturnShipmentDetailSheet() {
  const panel = useReturnShipmentPanelStore((s) => s.panel);
  const close = useReturnShipmentPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";

  const { data: returnShipment, isLoading, isError, refetch } = useReturnShipment(id);
  // The return lifecycle runs on the ORIGINAL shipment's status, so its staff events are the
  // return's story; its read also gives the merchant (ReturnShipment has no MerchantId).
  const originalAwb = returnShipment?.originalAwb ?? "";
  const original = useShipment(originalAwb);
  const events = useShipmentEvents(originalAwb);
  const { data: merchants } = useMerchants();
  const names = usePeopleNames();
  const canManage = usePermission("rto.manage");
  const confirm = useConfirmReturnShipment(id);
  const openShipment = useShipmentPanelStore((s) => s.openDetail);

  const merchantName = original.data ? merchants?.find((m) => m.id === original.data!.merchantId)?.displayName ?? "—" : "—";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="flex w-full flex-col gap-6 overflow-y-auto sm:max-w-xl">
        {!returnShipment && <SheetTitle className="sr-only">Return shipment</SheetTitle>}
        {isLoading && <DetailSkeleton fieldRows={1} />}
        {isError && (
          <ErrorState title="Couldn't load this return" description="It may not exist, or you may not have access to it." onRetry={() => refetch()} />
        )}
        {returnShipment && (
          <>
            <SheetHeaderBlock
              eyebrow={`RETURN · ${shortReason(returnShipment.rtoReason)}`}
              title={returnShipment.returnAwb}
              mono
              status={<StatusBadge status={returnShipment.currentStatus} />}
              facts={[
                { label: "Back to", value: names.branchName(returnShipment.destinationBranchId) },
                { label: "Now at", value: returnShipment.currentBranchId ? names.branchName(returnShipment.currentBranchId) : "In transit" },
                { label: "Merchant", value: merchantName },
              ]}
            />

            <div className="grid grid-cols-[1fr_auto_1fr] items-center gap-2">
              <button
                type="button"
                disabled={!returnShipment.originalAwb}
                onClick={() => openShipment(returnShipment.originalAwb)}
                aria-label={`Open original shipment ${returnShipment.originalAwb}`}
                className="rounded-lg border border-border p-3 text-left transition-colors hover:bg-accent disabled:pointer-events-none"
              >
                <span className="block text-2xs text-muted-foreground">Original</span>
                <span className="block font-mono text-sm">{returnShipment.originalAwb || "—"}</span>
              </button>
              <ArrowRight className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
              <div className="rounded-lg border border-primary p-3">
                <span className="block text-2xs text-muted-foreground">Return</span>
                <span className="block font-mono text-sm">{returnShipment.returnAwb}</span>
              </div>
            </div>

            <SectionCard title="What happened" icon={History} description="Events on the original shipment, newest first.">
              {events.isLoading && <Skeleton className="h-24 w-full" />}
              {events.isError && (
                <ErrorState title="Couldn't load the parcel history" description="It needs shipment read access." onRetry={() => events.refetch()} />
              )}
              {events.data && <TimelineTab events={events.data} />}
            </SectionCard>

            {returnShipment.merchantConfirmedAt ? (
              <p className="text-sm text-foreground">{`Confirmed by merchant ${confirmedFormat.format(new Date(returnShipment.merchantConfirmedAt))}`}</p>
            ) : canManage && canHandBack(returnShipment) ? (
              <SectionCard title="Hand back to merchant" icon={PackageCheck}>
                <div className="space-y-4">
                  <p className="text-sm text-muted-foreground">The parcel is at the origin branch. Confirm once the merchant has it back.</p>
                  <ComingSoonSlot title="Handover details" description="Received by and parcel condition are not recorded yet." />
                  <AlertDialog>
                    <AlertDialogTrigger asChild>
                      <Button type="button" disabled={confirm.isPending}>
                        {confirm.isPending ? <Spinner className="h-4 w-4" /> : "Confirm handover"}
                      </Button>
                    </AlertDialogTrigger>
                    <AlertDialogContent>
                      <AlertDialogHeader>
                        <AlertDialogTitle>{`Confirm handover of ${returnShipment.returnAwb}?`}</AlertDialogTitle>
                        <AlertDialogDescription>This marks the return delivered to the merchant. It can&apos;t be undone.</AlertDialogDescription>
                      </AlertDialogHeader>
                      <AlertDialogFooter>
                        <AlertDialogCancel>Not yet</AlertDialogCancel>
                        <AlertDialogAction onClick={() => confirm.mutate()}>Confirm handover</AlertDialogAction>
                      </AlertDialogFooter>
                    </AlertDialogContent>
                  </AlertDialog>
                  {confirm.isError && <p role="alert" className="text-sm text-destructive">Couldn&apos;t confirm the handover. Try again.</p>}
                </div>
              </SectionCard>
            ) : canManage ? (
              <p className="text-sm text-muted-foreground">Hand-back opens once the parcel is at the origin branch.</p>
            ) : null}
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}
```

In `features/ndr/hooks/useConfirmReturnShipment.ts`, add to `onSuccess` after the existing invalidation:

```ts
      queryClient.invalidateQueries({ queryKey: ["return-shipments-summary"] });
      queryClient.invalidateQueries({ queryKey: ["shipments"] });
```

- [ ] **Step 5: Run the tests**

Run: `npx vitest run "src/app/(staff)/return-shipments" src/features/ndr`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A "src/app/(staff)/return-shipments" src/features/ndr
git commit -m "$(cat <<'EOF'
feat(web): returns page and Sheet with stage strip and return chain

Five-stage strip with At origin highlighted filters the list in the URL;
rows link each return to its original AWB. The Sheet tells the story
from the original shipment's events and offers hand-back at origin
behind a confirm; handover details need backend.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 12: Documentation, full verification, live check

**Files:**
- Modify: `docs/API-SPEC.md` (new §6.3–§6.5 after §6.2)
- Modify: `docs/FRONTEND-ROADMAP.md` (F15 table + R3b-1 follow-ups)
- Modify: `DESIGN.md` (PipelineStrip line in the page building blocks paragraph)

- [ ] **Step 1: API-SPEC**

Insert after the end of §6.2 (before `## 7. International Shipments API`):

````markdown
### 6.3 Manifests (staff)

```http
GET /api/v1/manifests                       # list (array), branch-scoped
GET /api/v1/manifests/{id}                  # single
GET /api/v1/manifests/{id}/missing-shipments
GET /api/v1/manifests/{id}/shipments        # every parcel on the manifest (F15 R3b-1)
```

All require `manifest.read`. **Single reads are branch-scoped** (F15 R3b-1 fix): `/{id}`,
`/missing-shipments` and `/shipments` return `404` unless the manifest's `FromBranchId` or
`ToBranchId` is in the caller's `manifest.read` scope — the same rule as the list.

- `GET /manifests` takes `branchId`, `status` and `direction=incoming|outgoing`. `incoming` keeps
  manifests whose `ToBranchId` is `branchId` (or, with no `branchId`, any branch in scope);
  `outgoing` the same on `FromBranchId`; omitted = either side. An invalid value → `400
  MANIFEST_VALIDATION_FAILED`. Ordered `CreatedAt` descending, not paginated.
- Every manifest response has `receivedCount` — parcels receive-scanned at the destination.
- `/shipments` returns `[{ id, shipmentId, awb, scannedAt, receivedAt }]` ordered by `scannedAt`;
  `/missing-shipments` returns `[{ shipmentId, awb, scannedAt }]` (unreceived rows). Both resolve
  AWBs with one batched lookup.

### 6.4 Delivery attempts (staff)

`GET /api/v1/shipments/{shipmentId}/delivery-attempts` — `ndr.read`, staff only (merchant callers
→ `403`). `404` unless the shipment exists in the tenant and its current, origin or destination
branch is in the caller's `ndr.read` scope. Response, newest first (`attemptedAt`, then
`attemptNumber`, descending), not paginated:

```json
{ "items": [{ "attemptNumber": 2, "attemptedAt": "2026-09-30T04:20:00Z", "succeeded": false,
  "reason": "CustomerUnavailable", "notes": "Gate locked", "customerContacted": true,
  "nextAction": "Reattempt", "riderId": "…" }] }
```

`riderId` is the rider's id (not a user id). GPS is not exposed.

### 6.5 NDR list filters (staff)

`GET /api/v1/ndrs` adds `assigned=me|unassigned` (`me` = the caller owns it; invalid → `400
NDR_VALIDATION_FAILED`) and `reason=<NdrReason>` (invalid → `400`). Both narrow in SQL within the
existing branch scope, before paging.
````

- [ ] **Step 2: FRONTEND-ROADMAP**

In the Phase F15 table, replace the `R3b — Staff network` row with:

```markdown
| R3b-1 — Parcel movement | Manifests (lifecycle strip, incoming/outgoing, receive reconciliation), NDR queue (owner/reason filters, attempts, follow-up, resolve), Returns (stage strip, return chain, hand back) | Done — spec `docs/superpowers/specs/2026-10-14-f15-r3b1-parcel-movement-design.md` |
| R3b-2 — People & places | Riders + vehicles, live map, branches (artboards first) | Planned |
```

After the "Post-R3a fix pass" paragraph, add:

```markdown
**R3b-1 (2026-10-15):** manifest single reads (`/{id}`, `/missing-shipments`, new `/shipments`)
are now branch-scoped (404 out of scope); `direction` filter and `receivedCount` on manifests;
staff `GET /shipments/{id}/delivery-attempts`; `assigned`/`reason` NDR filters. Pages follow the
code where the spec differed: NDR follow-up offers only the legal next status, resolve choices
enable per NDR state, return hand-back is at `RtoAtOrigin`.

**Backend follow-ups from R3b-1:**
1. Manifest stage timestamps (sealed / dispatched / received / closed at) for the stepper.
2. Delivery attempt limit and "attempt N of M".
3. Assign / reassign an NDR owner.
4. Dated reattempt on NDR resolve.
5. Recipient / merchant / COD on NDR list rows.
6. `MerchantId` and `CreatedAt` on ReturnShipment (merchant column, "in return" age), and batch the per-row scan-info lookup in the returns list.
7. Handover details on return confirmation (received by, condition).
8. Branch-filterable manifest summary and a paged manifest list.
9. Branch scope on the NDR and return-shipment single reads (`GET /ndrs/{id}`, `/return-shipments/{id}`), matching the manifest fix.
```

(Item 9 is new: the same unscoped single-read shape exists in `NdrService.GetByIdAsync` and `GetReturnShipmentByIdAsync`; it is out of this slice's agreed scope, so it is logged, not fixed.)

- [ ] **Step 3: DESIGN.md**

In the "Shared page-level building blocks" paragraph, change `` `PipelineStrip` (ordered stage counts) `` to `` `PipelineStrip` (ordered stage counts; with `onSelect` each stage is a filter button, and `highlightKey` marks the one stage that needs attention — each page owns that rule) ``.

- [ ] **Step 4: Full verification**

Run each and confirm the expected result:

```bash
dotnet build                                                            # 0 errors
dotnet test tests/Manifests.Tests/Manifests.Tests.csproj                # PASS
dotnet test tests/Riders.Tests/Riders.Tests.csproj                      # PASS
dotnet test tests/Ndr.Tests/Ndr.Tests.csproj                            # PASS
dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~ManifestsEndpointsTests|FullyQualifiedName~NdrEndpointsTests|FullyQualifiedName~RidersEndpointsTests|FullyQualifiedName~ShipmentsEndpointsTests|FullyQualifiedName~TenantIsolationTests"   # PASS
cd src/web
npx tsc --noEmit        # 0 errors
npm run lint            # clean
npm test                # all pass
npm run build           # succeeds (retry once if next/font fails on a Google Fonts network flake)
```

If any fails, fix it in the task that owns the code before continuing.

- [ ] **Step 5: Live check in Chrome**

Start the API (`dotnet run --project src/Api`) and web (`npm run dev` in `src/web`) against `courier-postgres-1`, sign in to a seeded demo org, and check:
1. `/manifests`: strip counts match `/api/manifests/summary`; Incoming/Outgoing/All change rows; a branch-scoped user cannot open another branch's manifest by pasting its id into the Sheet (error state, not data).
2. A manifest in Receiving: "‹received› of ‹total› verified", missing list, parcel list, "Close with N missing" confirm.
3. `/ndrs`: Assigned to me / Unassigned / reason filters change rows and the URL; an Open NDR's Sheet shows attempts, a follow-up save moves it to Contacting and the strip updates.
4. `/return-shipments`: stage click filters; a return at origin offers Confirm handover; the timeline shows the original AWB's events.
5. Light and dark mode, and a 390 px wide window (no horizontal scroll).

Note anything off and fix it before the final commit.

- [ ] **Step 6: Commit**

```bash
git add docs/API-SPEC.md docs/FRONTEND-ROADMAP.md DESIGN.md
git commit -m "$(cat <<'EOF'
docs: R3b-1 manifest/attempt/NDR reads, roadmap row and follow-ups

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```
