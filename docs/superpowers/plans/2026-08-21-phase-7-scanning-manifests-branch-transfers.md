# Phase 7 — Barcode Scanning, Sorting, Manifests, Branch Transfers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A manifest can move shipments between two branches with correct custody updates and a provable missing-shipment exception path (`docs/ROADMAP.md` Phase 7 exit criteria).

**Architecture:** A new `Manifests` module (`src/Modules/Manifests/Manifests.csproj`) owns `Manifest, ManifestShipment, ShipmentTransfer`, following the `Domain/Application/Infrastructure/Presentation` shape every prior module uses. `Manifests` takes project references to `SharedKernel, Audit, Branches, Shipments` — it needs `Branches.Domain.Branch` for `FromBranchId`/`ToBranchId` FKs (exactly like `Shipments` referencing `Branch` for `OriginBranchId`/`DestinationBranchId`), and it needs `Shipments` to drive real shipment-status transitions.

Rather than touching `Shipment`'s `DbSet` directly from `Manifests` (the anti-pattern Phase 4/5 both got bitten by once each — see this project's memory), `Shipments.Application` exposes a new narrow interface, `IShipmentTransitionService`, mirroring Phase 5's `IBranchLookupService` pattern. `ManifestService` performs the `BRANCH-OPERATIONS.md` §1 five checks itself (permission, via `.RequirePermission()` at the route; branch access, via `IBranchAuthorizationChecker`; shipment state and custody, via a read-only `ShipmentScanInfo` projection) and then calls `IShipmentTransitionService` only to *execute* the already-validated transition. The permission/branch-access checks that used to live inside `ShipmentService.SortAsync`/`ReceiveAtDestinationAsync` move up into `ManifestService` — `manifest.create`/`manifest.seal`/`manifest.dispatch`/`manifest.receive`/`manifest.close` fully replace `shipment.sort`/`shipment.scan`/`shipment.transfer` for these three specific operations, per this phase's approved design.

**Resolved design decisions (approved in this session's brainstorming, restated here since the plan is the artifact executors read):**
1. **A "sort scan" *is* "add to manifest."** `POST /manifests/{id}/scan` scans an AWB against an open `DRAFT` manifest, transitions the shipment `AtOriginBranch → Sorted`, and creates the `ManifestShipment` row, all in one call. This retires the standalone `POST /shipments/{id}/sort` endpoint (Task 13) — a shipment can no longer be sorted outside a manifest.
2. **`Dispatched` is not a persisted resting status.** The spec lists `Manifest.Status` as `Draft|Sealed|Dispatched|InTransit|Received|Closed`, but nothing distinguishes a "dispatched-but-not-yet-in-transit" state — `POST /manifests/{id}/dispatch` goes straight from `Sealed` to `InTransit`, recording `VehiclePlate`/`DriverId` in the same call and bulk-flipping every `ManifestShipment`'s shipment `Sorted → InTransit`. The C# `ManifestStatus` enum therefore has 5 members, not 6 (Task 1 updates `DOMAIN-MODEL.md`/`BRANCH-OPERATIONS.md` to match, same as Phase 5's dropped `RateCardRule`).
3. **No separate `Transfer` scan type.** CLAUDE.md §13's original 7-op scan list (`Receive, Sort, Dispatch, Transfer, RiderPickup, RiderDelivery, RTO`) predates `BRANCH-OPERATIONS.md`'s manifest lifecycle, which only ever describes `Dispatch`/`Receive` for branch-to-branch moves. This phase implements `Sort` (via `/scan`), `Dispatch`, and `Receive` (via `/receive-scan`) as the complete branch-transfer scan surface — `RiderPickup`/`RiderDelivery` are Phase 8, `RTO` is Phase 9.
4. **Missing-shipment detection needs no new column.** A `ManifestShipment` row with `ReceivedAt == null` after the manifest is `Closed` *is* the missing-shipment flag — `GET /manifests/{id}/missing-shipments` queries for exactly that.

**Sequencing constraint (why the tasks are ordered this way):** the existing Phase 6 endpoints `POST /shipments/{id}/sort`, `/dispatch`, `/receive-dest` and their two Api.Tests happy-path tests currently provide the *only* way to walk a shipment from `AtOriginBranch` to `AtDestinationBranch` over HTTP. They cannot be deleted until `Manifests` fully exists and can replace that flow — deleting them earlier would leave those tests permanently red for several tasks, violating "tests pass" at every task checkpoint. So: `Shipments` gets an *additive* `IShipmentTransitionService` early (Task 3, breaks nothing), all of `Manifests` gets built and proven end-to-end over real HTTP (Tasks 4-12), and only then does a final cleanup task (Task 13) retire the three old endpoints and rewrite the two now-redundant tests to go through `Manifests` instead. One exception: `ShipmentService`'s single-shipment `DispatchAsync` structurally cannot survive alongside the new bulk `DispatchManyAsync` that `IShipmentTransitionService` needs (dispatch is now inherently a manifest-wide action) — but rather than break `/dispatch`'s two existing test call sites early, Task 3 adds `DispatchManyAsync` as a *new, additional* method and leaves the old single-shipment `DispatchAsync` (and its endpoint) alone until Task 13's cleanup, where both get removed together with the test fixes.

**Tech Stack:** Same as Phases 1-6 — EF Core 10 + Npgsql, xUnit + `Microsoft.EntityFrameworkCore.InMemory` for unit tests, `Microsoft.AspNetCore.Mvc.Testing` for API-level integration tests against the real Postgres container (`courier-postgres-1`, port 5434 — reuse it, do not start a second one). Manual exception-based validation, not FluentValidation.

**Spec:** `docs/ROADMAP.md` (Phase 7), `docs/BRANCH-OPERATIONS.md`, `docs/DOMAIN-MODEL.md` (Scanning & Manifests section), `docs/SHIPMENT-LIFECYCLE.md` §5 — this project folds the design directly into this plan doc rather than a separate `docs/superpowers/specs/` file (established convention across Phases 0-6); the full design was agreed section-by-section with the user in this session's brainstorming conversation before this plan was written.

## Global Constraints

- Every tenant-owned entity gets a non-nullable `OrganizationId`; every query is scoped server-side. `ManifestShipment`/`ShipmentTransfer` have no `OrganizationId` of their own — isolation flows transitively through `ManifestId → Manifest.OrganizationId`, exactly like `ShipmentItem`/`ShipmentEvent` in Phase 6. Grep-check every new read method against this before considering the phase done (per this project's memory: this exact bug shape bit Phase 4 and Phase 5 once each).
- A module reaches another module's data only through that module's Application-layer interface — `Manifests` never touches `Shipment`'s `DbSet` directly; it goes through the new `IShipmentTransitionService`.
- No `PUT`/`PATCH`/`DELETE` on `Manifest`/`ManifestShipment`/`ShipmentTransfer` — manifest state only ever moves forward through its lifecycle actions.
- Manual exception-based validation (`ManifestValidationException` and its `ManifestScanRejectedException`/`ManifestNotFoundException` siblings), not FluentValidation.
- The five `BRANCH-OPERATIONS.md` §1 checks (permission → branch access → shipment state → expected operation → custody consistency) apply to both `/scan` and `/receive-scan`; a scan that fails any check must not write a `ShipmentEvent` or mutate `ManifestShipment`.
- Reuse existing `manifest.create/seal/dispatch/receive` permissions from `PermissionCatalog.cs` — no changes needed for those four. `manifest.close` is also already seeded. One genuinely new permission, `manifest.read`, is added in Task 2 (the original v1 catalog never included a manifest read permission — a real gap, not an oversight to work around).

---

### Task 1: Docs — Update DOMAIN-MODEL.md and BRANCH-OPERATIONS.md; Scaffold the Manifests Module

**Files:**
- Modify: `docs/DOMAIN-MODEL.md`
- Modify: `docs/BRANCH-OPERATIONS.md`
- Create: `src/Modules/Manifests/Manifests.csproj`
- Create: `tests/Manifests.Tests/Manifests.Tests.csproj`
- Modify: `Courier.slnx`
- Modify: `src/Persistence/Persistence.csproj`
- Modify: `src/Api/Api.csproj`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: an empty-but-building `Manifests` project wired into the solution with the reference graph `SharedKernel, Audit, Branches, Shipments ← Manifests`, `..., Manifests ← Persistence`, `..., Manifests ← Api`. `Api.Tests` gets transitive access through `Api.csproj`.

- [ ] **Step 1: Update the Scanning & Manifests section of DOMAIN-MODEL.md**

Replace the existing `## Scanning & Manifests` section (the `Manifest { ... }` / `ManifestShipment { ... }` / `ShipmentTransfer { ... }` block) with:

```text
## Scanning & Manifests — `OrganizationId` present

```text
Manifest { Id, OrganizationId, Code (e.g. MNF-20260818-001, unique per (OrganizationId, Code)),
           FromBranchId, ToBranchId, VehiclePlate (nullable until dispatch), DriverId (nullable —
           no FK yet, Rider doesn't exist until Phase 8), Status (Draft|Sealed|InTransit|Received|
           Closed — no separate "Dispatched" resting state; POST /manifests/{id}/dispatch moves
           Sealed straight to InTransit in one action, recording VehiclePlate/DriverId at the same
           time, since nothing distinguishes a dispatched-but-not-yet-in-transit state in practice),
           ShipmentCount, CreatedByUserId, CreatedAt }

ManifestShipment { Id, ManifestId, ShipmentId, ScannedAt, ReceivedAt (nullable — a null value on a
                    Closed manifest IS the missing-shipment flag; no separate IsMissing column) }

ShipmentTransfer { Id, ManifestId, ShipmentId, FromBranchId, ToBranchId, Timestamp }
             # append-only physical-custody record, one row per shipment at receive-time,
             # separate from Shipments.ShipmentEvent (SHIPMENT-LIFECYCLE.md §5)
```
```

- [ ] **Step 2: Add the ShipmentTransfer relationship line**

In `docs/DOMAIN-MODEL.md`'s `## Relationship Summary` block, after the existing `Manifest 1─* ManifestShipment *─1 Shipment` line, add:

```text
Manifest 1─* ShipmentTransfer *─1 Shipment
```

- [ ] **Step 3: Update BRANCH-OPERATIONS.md's manifest lifecycle**

In `docs/BRANCH-OPERATIONS.md`'s `## 2. Manifests` section, replace the `### Lifecycle` block's opening line and `DISPATCHED` bullet:

Replace:
```text
DRAFT → SEALED → DISPATCHED → IN_TRANSIT → RECEIVED → CLOSED
```
with:
```text
DRAFT → SEALED → IN_TRANSIT → RECEIVED → CLOSED
```

Replace the `* **DISPATCHED**: vehicle/driver recorded, shipments transition to \`IN_TRANSIT\`
  (shipment-level) as part of this action.` bullet with:
```text
* **SEALED → IN_TRANSIT**: a single `dispatch` action records vehicle/driver and moves every
  `ManifestShipment`'s shipment `Sorted → InTransit` — there is no separately observable
  "dispatched but not yet in transit" resting state, so `Manifest.Status` goes straight to
  `IN_TRANSIT` (Phase 7 plan's resolved design decision #2).
```

- [ ] **Step 4: Create the two projects**

```bash
dotnet new classlib -n Manifests -o src/Modules/Manifests
dotnet new xunit -n Manifests.Tests -o tests/Manifests.Tests
dotnet sln Courier.slnx add src/Modules/Manifests/Manifests.csproj tests/Manifests.Tests/Manifests.Tests.csproj
```

Delete the template `src/Modules/Manifests/Class1.cs` and `tests/Manifests.Tests/UnitTest1.cs`.

- [ ] **Step 5: Add the ASP.NET Core framework reference and EF packages**

```bash
dotnet add src/Modules/Manifests/Manifests.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Modules/Manifests/Manifests.csproj package Microsoft.EntityFrameworkCore.Relational
dotnet add tests/Manifests.Tests/Manifests.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory
```

In `src/Modules/Manifests/Manifests.csproj`, add inside a sibling `<ItemGroup>`:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

- [ ] **Step 6: Wire project references**

```bash
dotnet add src/Modules/Manifests/Manifests.csproj reference src/SharedKernel/SharedKernel.csproj
dotnet add src/Modules/Manifests/Manifests.csproj reference src/Modules/Audit/Audit.csproj
dotnet add src/Modules/Manifests/Manifests.csproj reference src/Modules/Branches/Branches.csproj
dotnet add src/Modules/Manifests/Manifests.csproj reference src/Modules/Shipments/Shipments.csproj
dotnet add src/Persistence/Persistence.csproj reference src/Modules/Manifests/Manifests.csproj
dotnet add src/Api/Api.csproj reference src/Modules/Manifests/Manifests.csproj
dotnet add tests/Manifests.Tests/Manifests.Tests.csproj reference src/Modules/Manifests/Manifests.csproj
```

- [ ] **Step 7: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add docs/DOMAIN-MODEL.md docs/BRANCH-OPERATIONS.md Courier.slnx src/Modules/Manifests tests/Manifests.Tests src/Persistence/Persistence.csproj src/Api/Api.csproj
git commit -m "docs: update domain model for Phase 7; scaffold Manifests module project"
```

---

### Task 2: Identity — Add the manifest.read Permission

**Files:**
- Modify: `src/Modules/Identity/Application/PermissionCatalog.cs`
- Modify: `docs/RBAC.md`
- Test: `tests/Identity.Tests/PermissionCatalogTests.cs` (create — no existing test file covers this catalog)

**Interfaces:**
- Consumes: nothing new.
- Produces: `"manifest.read"` as a real seeded `Permission` row (once Task 11's migration runs) — Task 10's `GET /manifests/{id}` and `GET /manifests/{id}/missing-shipments` endpoints gate on it via `.RequirePermission("manifest.read")`.

- [ ] **Step 1: Write the failing test**

`tests/Identity.Tests/PermissionCatalogTests.cs`:

```csharp
using Courier.Identity.Application;
using Xunit;

namespace Courier.Identity.Tests;

public class PermissionCatalogTests
{
    [Fact]
    public void All_IncludesManifestRead()
    {
        Assert.Contains(PermissionCatalog.All, p => p.Key == "manifest.read");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter PermissionCatalogTests -v n`
Expected: FAIL — no entry with `Key == "manifest.read"`.

- [ ] **Step 3: Add the permission**

In `src/Modules/Identity/Application/PermissionCatalog.cs`, in the `All` list, add a new line immediately before `("manifest.create", "Create manifests"),`:

```csharp
        ("manifest.read", "Read manifests"),
```

- [ ] **Step 4: Update RBAC.md's concrete catalog**

In `docs/RBAC.md`, in the concrete permission catalog block that currently reads:

```text
manifest.create          manifest.seal           manifest.dispatch
manifest.receive         manifest.close
```

replace with:

```text
manifest.read            manifest.create         manifest.seal
manifest.dispatch        manifest.receive        manifest.close
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter PermissionCatalogTests -v n`
Expected: PASS.

- [ ] **Step 6: Verify the solution builds and existing Identity tests still pass**

```bash
dotnet build Courier.slnx
dotnet test tests/Identity.Tests/Identity.Tests.csproj
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`; all Identity.Tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Identity/Application/PermissionCatalog.cs docs/RBAC.md tests/Identity.Tests/PermissionCatalogTests.cs
git commit -m "feat: add manifest.read permission"
```

---

### Task 3: Shipments — Add IShipmentTransitionService for the Manifests Module to Drive Transitions

**Files:**
- Create: `src/Modules/Shipments/Application/IShipmentTransitionService.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentService.cs` (add `: IShipmentTransitionService` to the class declaration)
- Modify: `src/Modules/Shipments/Application/ShipmentService.Reads.cs` (add `GetScanInfoAsync`/`GetScanInfoByAwbAsync`)
- Modify: `src/Modules/Shipments/Application/ShipmentService.Transitions.cs` (strip the internal branch check from `SortAsync`/`ReceiveAtDestinationAsync`; add `DispatchManyAsync` — the existing single-shipment `DispatchAsync` and its `/dispatch` endpoint are left untouched here, see this plan's Architecture section for why)
- Modify: `src/Api/Program.cs` (register `IShipmentTransitionService`)
- Test: `tests/Shipments.Tests/ShipmentServiceScanInfoTests.cs` (create)
- Test: `tests/Shipments.Tests/ShipmentServiceTransitionTests.cs` (extend — add `DispatchManyAsync` coverage)

**Interfaces:**
- Consumes: `Shipments.Domain` entities (existing), `ShipmentService`'s existing constructor/fields (existing).
- Produces: `ShipmentScanInfo(Guid ShipmentId, Guid OrganizationId, string Awb, ShipmentStatus CurrentStatus, Guid? CurrentBranchId, Guid OriginBranchId, Guid DestinationBranchId)` and `IShipmentTransitionService { GetScanInfoAsync, GetScanInfoByAwbAsync, SortAsync, DispatchManyAsync, ReceiveAtDestinationAsync }` — Task 6 onward (`Manifests.Application`) consumes this interface exclusively; it never touches `Shipment`'s `DbSet`.

- [ ] **Step 1: Write the failing tests**

`tests/Shipments.Tests/ShipmentServiceScanInfoTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Geography.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.Shipments.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Shipments.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FakeBranchLookup : IBranchLookupService
{
    private readonly Dictionary<Guid, Guid?> _zonesByBranch;
    public FakeBranchLookup(Dictionary<Guid, Guid?> zonesByBranch) => _zonesByBranch = zonesByBranch;
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_zonesByBranch.TryGetValue(branchId, out var zoneId) ? new BranchZoneInfo(branchId, zoneId) : null);
}

file class AlwaysTrueGeographyLookup : IGeographyLookupService
{
    public Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class FixedOrganizationLookup : IOrganizationLookupService
{
    private readonly string? _slug;
    public FixedOrganizationLookup(string? slug) => _slug = slug;
    public Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(_slug);
}

public class ShipmentServiceScanInfoTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
            modelBuilder.ApplyConfiguration(new ServiceTypeConfiguration());
            modelBuilder.ApplyConfiguration(new WeightSlabConfiguration());
            modelBuilder.ApplyConfiguration(new RateCardConfiguration());
            modelBuilder.ApplyConfiguration(new ZoneRateConfiguration());
            modelBuilder.ApplyConfiguration(new AdditionalChargeConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantRateCardConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentItemConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentEventConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static ShipmentService CreateService(TestDbContext dbContext, Guid organizationId, Dictionary<Guid, Guid?> zonesByBranch)
    {
        var branchLookup = new FakeBranchLookup(zonesByBranch);
        var pricingService = new PricingService(dbContext, new FixedTenantProvider(organizationId), branchLookup, new AlwaysTrueMerchantLookup());
        return new ShipmentService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchLookup,
            new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup(), pricingService, new AllBranchesAuthorizationChecker(),
            new FixedOrganizationLookup("testorg"));
    }

    private static async Task<(ShipmentService Service, Guid ShipmentId, string Awb, Guid OriginBranchId, Guid DestinationBranchId)> SeedCreatedShipmentAsync(TestDbContext dbContext, Guid organizationId)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Test Org", Slug = "testorg" });

        var rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
        dbContext.Set<RateCard>().Add(rateCard);
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = weightSlabId, BaseRate = 100m });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, organizationId, new() { [originBranchId] = originZoneId, [destinationBranchId] = destinationZoneId });
        var request = new CreateShipmentRequest(
            Guid.NewGuid(), originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
            "Recipient", "9800000000", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Recipient Address",
            "Sender", "9800000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Sender Address",
            new List<CreateShipmentItemRequest> { new("Item", 1, 100m) });
        var created = await service.CreateShipmentAsync(request, ActingUser(Guid.NewGuid()));

        return (service, created.Id, created.Awb, originBranchId, destinationBranchId);
    }

    [Fact]
    public async Task GetScanInfoAsync_ExistingShipment_ReturnsProjection()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, awb, originBranchId, destinationBranchId) = await SeedCreatedShipmentAsync(dbContext, organizationId);

        IShipmentTransitionService transitionService = service;
        var info = await transitionService.GetScanInfoAsync(shipmentId);

        Assert.NotNull(info);
        Assert.Equal(shipmentId, info!.ShipmentId);
        Assert.Equal(organizationId, info.OrganizationId);
        Assert.Equal(awb, info.Awb);
        Assert.Equal(ShipmentStatus.Created, info.CurrentStatus);
        Assert.Equal(originBranchId, info.CurrentBranchId);
        Assert.Equal(originBranchId, info.OriginBranchId);
        Assert.Equal(destinationBranchId, info.DestinationBranchId);
    }

    [Fact]
    public async Task GetScanInfoAsync_UnknownShipment_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), new());
        IShipmentTransitionService transitionService = service;

        Assert.Null(await transitionService.GetScanInfoAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetScanInfoByAwbAsync_ExistingShipment_ReturnsProjection()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, awb, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        IShipmentTransitionService transitionService = service;

        var info = await transitionService.GetScanInfoByAwbAsync(awb);

        Assert.NotNull(info);
        Assert.Equal(shipmentId, info!.ShipmentId);
    }

    [Fact]
    public async Task GetScanInfoByAwbAsync_UnknownAwb_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), new());
        IShipmentTransitionService transitionService = service;

        Assert.Null(await transitionService.GetScanInfoByAwbAsync("NOSUCHAWB"));
    }

    [Fact]
    public async Task DispatchManyAsync_MultipleSortedShipments_AllMoveToInTransit()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId1, _, originBranchId, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        await service.AssignPickupAsync(shipmentId1, new AssignPickupRequest(Guid.NewGuid()), actingUser);
        await service.PickupScanAsync(shipmentId1, actingUser);
        await service.ReceiveAtOriginAsync(shipmentId1, actingUser);
        await service.SortAsync(shipmentId1, actingUser);

        var seed2 = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var shipmentId2 = seed2.ShipmentId;
        await service.AssignPickupAsync(shipmentId2, new AssignPickupRequest(Guid.NewGuid()), actingUser);
        await service.PickupScanAsync(shipmentId2, actingUser);
        await service.ReceiveAtOriginAsync(shipmentId2, actingUser);
        await service.SortAsync(shipmentId2, actingUser);

        IShipmentTransitionService transitionService = service;
        await transitionService.DispatchManyAsync(new[] { shipmentId1, shipmentId2 }, actingUser);

        var info1 = await transitionService.GetScanInfoAsync(shipmentId1);
        var info2 = await transitionService.GetScanInfoAsync(shipmentId2);
        Assert.Equal(ShipmentStatus.InTransit, info1!.CurrentStatus);
        Assert.Null(info1.CurrentBranchId);
        Assert.Equal(ShipmentStatus.InTransit, info2!.CurrentStatus);

        var dispatchedEvents = await dbContext.Set<ShipmentEvent>()
            .Where(e => e.EventType == ShipmentEventType.Dispatched)
            .ToListAsync();
        Assert.Equal(2, dispatchedEvents.Count);
    }

    [Fact]
    public async Task DispatchManyAsync_ShipmentNotSorted_ThrowsInvalidTransitionAndAppliesNothing()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        IShipmentTransitionService transitionService = service;

        // Still Created, never sorted.
        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => transitionService.DispatchManyAsync(new[] { shipmentId }, ActingUser(Guid.NewGuid())));

        var info = await transitionService.GetScanInfoAsync(shipmentId);
        Assert.Equal(ShipmentStatus.Created, info!.CurrentStatus);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter ShipmentServiceScanInfoTests -v n`
Expected: FAIL — `IShipmentTransitionService` doesn't exist yet.

- [ ] **Step 3: Write IShipmentTransitionService**

`src/Modules/Shipments/Application/IShipmentTransitionService.cs`:

```csharp
using System.Security.Claims;
using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

// Read-only projection for the Manifests module's five-check scan validation
// (BRANCH-OPERATIONS.md §1) — deliberately narrower than ShipmentResponse: only the fields a scan
// check needs (state + custody), not pricing/recipient/sender data Manifests has no reason to see.
public record ShipmentScanInfo(
    Guid ShipmentId,
    Guid OrganizationId,
    string Awb,
    ShipmentStatus CurrentStatus,
    Guid? CurrentBranchId,
    Guid OriginBranchId,
    Guid DestinationBranchId);

// Lets the Manifests module drive real shipment-status transitions without touching Shipment's
// DbSet directly, mirroring Phase 5's IBranchLookupService pattern. Manifests performs the
// permission + branch-access + expected-operation checks itself (BRANCH-OPERATIONS.md §1 checks
// 1/2/4) before calling these — the transition methods below only enforce the state-machine check
// (#3, via the underlying TransitionAsync's expectedFrom guard) and are not separately
// permission-gated, since that gate now lives one layer up in ManifestService.
public interface IShipmentTransitionService
{
    Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default);
    Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Declare the interface on ShipmentService**

In `src/Modules/Shipments/Application/ShipmentService.cs`, change:

```csharp
public partial class ShipmentService
{
```

to:

```csharp
public partial class ShipmentService : IShipmentTransitionService
{
```

- [ ] **Step 5: Add GetScanInfoAsync/GetScanInfoByAwbAsync**

In `src/Modules/Shipments/Application/ShipmentService.Reads.cs`, add at the end of the `partial class ShipmentService` body (before the final closing brace):

```csharp

    public async Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        return shipment is null ? null : ToScanInfo(shipment);
    }

    public async Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Awb == awb, cancellationToken);
        return shipment is null ? null : ToScanInfo(shipment);
    }

    private static ShipmentScanInfo ToScanInfo(Shipment shipment) => new(
        shipment.Id, shipment.OrganizationId, shipment.Awb, shipment.CurrentStatus,
        shipment.CurrentBranchId, shipment.OriginBranchId, shipment.DestinationBranchId);
```

- [ ] **Step 6: Strip the branch check from SortAsync/ReceiveAtDestinationAsync; add DispatchManyAsync**

In `src/Modules/Shipments/Application/ShipmentService.Transitions.cs`, replace the `SortAsync` method body:

```csharp
    public async Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        await CheckBranchAccessAsync(actingUser, "shipment.sort", shipment.OriginBranchId, cancellationToken);

        return await TransitionAsync(shipmentId, ShipmentStatus.AtOriginBranch, ShipmentStatus.Sorted, ShipmentEventType.Sorted,
            _ => { }, "shipment.sorted", actingUser, cancellationToken);
    }
```

with:

```csharp
    // Branch-scope/permission enforcement for this operation now lives one layer up in
    // Manifests.Application.ManifestService.ScanAsync (BRANCH-OPERATIONS.md §1 checks 1/2), which
    // is the only caller of this method as of Phase 7 — its own manifest.create permission and
    // FromBranchId branch check happen before this is ever reached, so re-checking
    // "shipment.sort"/branch access here would be redundant and would wrongly require callers to
    // hold a permission this phase's design deliberately retired for this operation.
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.AtOriginBranch, ShipmentStatus.Sorted, ShipmentEventType.Sorted,
            _ => { }, "shipment.sorted", actingUser, cancellationToken);
```

Replace the `ReceiveAtDestinationAsync` method body:

```csharp
    public async Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        await CheckBranchAccessAsync(actingUser, "shipment.scan", shipment.DestinationBranchId, cancellationToken);

        return await TransitionAsync(shipmentId, ShipmentStatus.InTransit, ShipmentStatus.AtDestinationBranch, ShipmentEventType.ReceivedAtDestination,
            s => s.CurrentBranchId = s.DestinationBranchId, "shipment.received_at_destination", actingUser, cancellationToken);
    }
```

with:

```csharp
    // Same reasoning as SortAsync above: Manifests.Application.ManifestService.ReceiveScanAsync is
    // the only caller as of Phase 7, and it enforces manifest.receive + ToBranchId branch access
    // itself before calling this.
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.InTransit, ShipmentStatus.AtDestinationBranch, ShipmentEventType.ReceivedAtDestination,
            s => s.CurrentBranchId = s.DestinationBranchId, "shipment.received_at_destination", actingUser, cancellationToken);
```

Add `DispatchManyAsync` immediately after the existing single-shipment `DispatchAsync` method (leave that existing method untouched — it still backs `/dispatch` until Task 13):

```csharp

    // Bulk counterpart to the single-shipment DispatchAsync above, used exclusively by
    // Manifests.Application.ManifestService.DispatchAsync to move every shipment on a manifest
    // Sorted -> InTransit as one unit. Deliberately does NOT loop calling the single-shipment
    // TransitionAsync helper (which SaveChangesAsync's per call) — that would commit N separate
    // transactions for one manifest-level dispatch action. Instead every shipment's mutation +
    // ShipmentEvent is staged in-memory and committed in exactly one SaveChangesAsync, which EF
    // Core already wraps in a single implicit database transaction on a relational provider — true
    // atomicity without introducing an explicit BeginTransactionAsync (which the in-memory provider
    // this module's unit tests use does not support).
    public async Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipments = await _dbContext.Set<Shipment>().Where(s => shipmentIds.Contains(s.Id)).ToListAsync(cancellationToken);
        var actingUserId = actingUser.GetUserId();
        var now = DateTimeOffset.UtcNow;

        foreach (var shipment in shipments)
        {
            if (shipment.CurrentStatus != ShipmentStatus.Sorted)
            {
                throw new ShipmentInvalidTransitionException(shipment.Id, shipment.CurrentStatus, ShipmentStatus.InTransit);
            }
            var previousStatus = shipment.CurrentStatus;
            var eventBranchId = shipment.CurrentBranchId;
            shipment.CurrentStatus = ShipmentStatus.InTransit;
            shipment.CurrentBranchId = null;

            _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                EventType = ShipmentEventType.Dispatched,
                PreviousStatus = previousStatus,
                NewStatus = ShipmentStatus.InTransit,
                BranchId = eventBranchId,
                UserId = actingUserId,
                Timestamp = now
            });
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ShipmentValidationException("One or more shipments on this manifest were modified concurrently; dispatch was not applied.");
        }
    }
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
dotnet test tests/Shipments.Tests/Shipments.Tests.csproj
```

Expected: PASS — the new `ShipmentServiceScanInfoTests` and every pre-existing `Shipments.Tests` test (including `ShipmentServiceTransitionTests`'s full happy path, which still calls the unchanged single-shipment `DispatchAsync` directly).

- [ ] **Step 8: Register IShipmentTransitionService in DI**

In `src/Api/Program.cs`, after the existing `builder.Services.AddScoped<ShipmentService>();` line, add:

```csharp
builder.Services.AddScoped<IShipmentTransitionService>(sp => sp.GetRequiredService<ShipmentService>());
```

- [ ] **Step 9: Verify the full solution builds and all tests pass**

```bash
dotnet build Courier.slnx
dotnet test Courier.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`; all tests pass (no regressions — `/sort`, `/dispatch`, `/receive-dest` endpoints and their tests are untouched by this task).

- [ ] **Step 10: Commit**

```bash
git add src/Modules/Shipments/Application src/Api/Program.cs tests/Shipments.Tests/ShipmentServiceScanInfoTests.cs
git commit -m "feat: add IShipmentTransitionService for the Manifests module"
```

---

### Task 4: Manifests Domain — Entities and Enum

**Files:**
- Create: `src/Modules/Manifests/Domain/ManifestStatus.cs`
- Create: `src/Modules/Manifests/Domain/Manifest.cs`
- Create: `src/Modules/Manifests/Domain/ManifestShipment.cs`
- Create: `src/Modules/Manifests/Domain/ShipmentTransfer.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: all three entity types and the enum — every later task in this module consumes these exact shapes.

- [ ] **Step 1: Write the enum**

`src/Modules/Manifests/Domain/ManifestStatus.cs`:

```csharp
namespace Courier.Manifests.Domain;

// No "Dispatched" member — this plan's resolved design decision #2: nothing distinguishes a
// dispatched-but-not-yet-in-transit resting state, so the dispatch action moves Sealed straight to
// InTransit.
public enum ManifestStatus
{
    Draft,
    Sealed,
    InTransit,
    Received,
    Closed
}
```

- [ ] **Step 2: Write the entities**

`src/Modules/Manifests/Domain/Manifest.cs`:

```csharp
namespace Courier.Manifests.Domain;

public class Manifest
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Code { get; set; }
    public Guid FromBranchId { get; set; }
    public Guid ToBranchId { get; set; }
    public string? VehiclePlate { get; set; }
    public Guid? DriverId { get; set; }
    public ManifestStatus Status { get; set; }
    public int ShipmentCount { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

`src/Modules/Manifests/Domain/ManifestShipment.cs`:

```csharp
namespace Courier.Manifests.Domain;

// A null ReceivedAt on a Closed manifest IS the missing-shipment flag — no separate IsMissing
// column (this plan's resolved design decision #4).
public class ManifestShipment
{
    public Guid Id { get; set; }
    public Guid ManifestId { get; set; }
    public Guid ShipmentId { get; set; }
    public DateTimeOffset ScannedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
}
```

`src/Modules/Manifests/Domain/ShipmentTransfer.cs`:

```csharp
namespace Courier.Manifests.Domain;

// Append-only physical-custody record, one row per shipment at receive-time — distinct from
// Shipments.Domain.ShipmentEvent per SHIPMENT-LIFECYCLE.md §5.
public class ShipmentTransfer
{
    public Guid Id { get; set; }
    public Guid ManifestId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid FromBranchId { get; set; }
    public Guid ToBranchId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
```

- [ ] **Step 3: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Manifests/Domain
git commit -m "feat: add Manifests domain entities"
```

---

### Task 5: Manifests Infrastructure — EF Configurations

**Files:**
- Create: `src/Modules/Manifests/Infrastructure/ManifestConfiguration.cs`
- Create: `src/Modules/Manifests/Infrastructure/ManifestShipmentConfiguration.cs`
- Create: `src/Modules/Manifests/Infrastructure/ShipmentTransferConfiguration.cs`

**Interfaces:**
- Consumes: `Manifests.Domain` entities (Task 4), `Branches.Domain.Branch` (Task 1's reference).
- Produces: three `IEntityTypeConfiguration<T>` classes — Task 11's `AppDbContext` wiring applies these.

- [ ] **Step 1: Write ManifestConfiguration**

`src/Modules/Manifests/Infrastructure/ManifestConfiguration.cs`:

```csharp
using Courier.Branches.Domain;
using Courier.Manifests.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Manifests.Infrastructure;

public class ManifestConfiguration : IEntityTypeConfiguration<Manifest>
{
    public void Configure(EntityTypeBuilder<Manifest> builder)
    {
        builder.ToTable("Manifests");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Code).HasMaxLength(30).IsRequired();
        builder.Property(m => m.VehiclePlate).HasMaxLength(30);

        // Every tenant-filtered query hits this column.
        builder.HasIndex(m => m.OrganizationId);
        // Code is unique per org — also the index GenerateManifestCodeAsync's collision-retry
        // relies on catching a violation of, mirroring Shipment.Awb's uniqueness (Phase 6).
        builder.HasIndex(m => new { m.OrganizationId, m.Code }).IsUnique();

        builder.HasOne<Branch>().WithMany().HasForeignKey(m => m.FromBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(m => m.ToBranchId).OnDelete(DeleteBehavior.Restrict);

        // No FK to Organization/User here: Manifests has no project reference to Identity —
        // wired centrally in Persistence/AppDbContext.OnModelCreating instead, same as Shipment.
    }
}
```

- [ ] **Step 2: Write ManifestShipmentConfiguration**

`src/Modules/Manifests/Infrastructure/ManifestShipmentConfiguration.cs`:

```csharp
using Courier.Manifests.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Manifests.Infrastructure;

public class ManifestShipmentConfiguration : IEntityTypeConfiguration<ManifestShipment>
{
    public void Configure(EntityTypeBuilder<ManifestShipment> builder)
    {
        builder.ToTable("ManifestShipments");
        builder.HasKey(ms => ms.Id);

        builder.HasIndex(ms => ms.ManifestId);
        // Not unique on ShipmentId: a shipment legitimately gets a second ManifestShipment row on
        // a later return-leg manifest once Phase 9 adds RTO — only "one row per (ManifestId,
        // ShipmentId) pair" needs to hold, which the natural insert path already guarantees (Sort
        // moves the shipment out of AtOriginBranch, so a second /scan attempt against the SAME
        // manifest fails the shipment-state check in ManifestService.ScanAsync before reaching here).
        builder.HasIndex(ms => ms.ShipmentId);

        builder.HasOne<Manifest>().WithMany().HasForeignKey(ms => ms.ManifestId).OnDelete(DeleteBehavior.Cascade);

        // No OrganizationId of its own: tenant isolation flows transitively through
        // ManifestId -> Manifest.OrganizationId, same reasoning as ShipmentItem/ShipmentEvent in
        // Phase 6. Shipments has no project reference back to Manifests, so ShipmentId is left
        // unconstrained at the DB level here (mirrors Shipment.CurrentCustodianUserId having no
        // FK) — every query against this table in Manifests.Application scopes through a Manifest
        // the caller's tenant already owns.
    }
}
```

- [ ] **Step 3: Write ShipmentTransferConfiguration**

`src/Modules/Manifests/Infrastructure/ShipmentTransferConfiguration.cs`:

```csharp
using Courier.Branches.Domain;
using Courier.Manifests.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Manifests.Infrastructure;

public class ShipmentTransferConfiguration : IEntityTypeConfiguration<ShipmentTransfer>
{
    public void Configure(EntityTypeBuilder<ShipmentTransfer> builder)
    {
        builder.ToTable("ShipmentTransfers");
        builder.HasKey(t => t.Id);

        builder.HasIndex(t => t.ShipmentId);
        builder.HasOne<Manifest>().WithMany().HasForeignKey(t => t.ManifestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(t => t.FromBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(t => t.ToBranchId).OnDelete(DeleteBehavior.Restrict);

        // No OrganizationId of its own: same reasoning as ManifestShipmentConfiguration.
    }
}
```

- [ ] **Step 4: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Manifests/Infrastructure
git commit -m "feat: add Manifests EF configurations"
```

---

### Task 6: Manifests Application — DTOs and Exceptions

**Files:**
- Create: `src/Modules/Manifests/Application/ManifestDtos.cs`

**Interfaces:**
- Consumes: `Manifests.Domain` entities (Task 4).
- Produces: every request/response record and exception type — Tasks 7-10 all consume these exact shapes.

- [ ] **Step 1: Write the DTOs and exceptions**

`src/Modules/Manifests/Application/ManifestDtos.cs`:

```csharp
using Courier.Manifests.Domain;

namespace Courier.Manifests.Application;

public record CreateManifestRequest(Guid FromBranchId, Guid ToBranchId);

public record ManifestResponse(
    Guid Id,
    string Code,
    Guid FromBranchId,
    Guid ToBranchId,
    string? VehiclePlate,
    Guid? DriverId,
    ManifestStatus Status,
    int ShipmentCount,
    DateTimeOffset CreatedAt);

public record ScanShipmentRequest(string Awb);

public record ManifestShipmentResponse(Guid Id, Guid ShipmentId, string Awb, DateTimeOffset ScannedAt, DateTimeOffset? ReceivedAt);

public record DispatchManifestRequest(string VehiclePlate, Guid? DriverId);

public record MissingShipmentResponse(Guid ShipmentId, string Awb, DateTimeOffset ScannedAt);

public class ManifestValidationException : Exception
{
    public ManifestValidationException(string message) : base(message) { }
}

public class ManifestNotFoundException : Exception
{
    public ManifestNotFoundException(Guid manifestId) : base($"Manifest {manifestId} was not found.")
    {
        ManifestId = manifestId;
    }

    public Guid ManifestId { get; }
}

// Thrown by ScanAsync/ReceiveScanAsync for any BRANCH-OPERATIONS.md §1 five-check rejection —
// carries a stable ReasonCode so the Presentation layer and API callers can branch on the specific
// failure mode (SHIPMENT_NOT_FOUND, WRONG_STATE, WRONG_CUSTODY, MANIFEST_NOT_OPEN,
// MANIFEST_NOT_RECEIVING, NOT_ON_MANIFEST, ALREADY_RECEIVED).
public class ManifestScanRejectedException : ManifestValidationException
{
    public ManifestScanRejectedException(string reasonCode, string message) : base(message)
    {
        ReasonCode = reasonCode;
    }

    public string ReasonCode { get; }
}
```

- [ ] **Step 2: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Manifests/Application/ManifestDtos.cs
git commit -m "feat: add Manifests DTOs and exceptions"
```

---

### Task 7: Manifests Application — ManifestService.CreateManifestAsync

**Files:**
- Create: `src/Modules/Manifests/Application/ManifestService.cs` (this task writes the constructor, `CreateManifestAsync`, `GenerateManifestCodeAsync`, `LoadResponseAsync`/`ToResponse`, and the shared `CheckBranchAccessAsync` helper — later tasks extend this same partial class in separate files)
- Test: `tests/Manifests.Tests/ManifestServiceCreationTests.cs`

**Interfaces:**
- Consumes: `IBranchLookupService.GetZoneInfoAsync` (Phase 5), `IBranchAuthorizationChecker` (Phase 2), `IShipmentTransitionService` (Task 3), `Manifests.Domain` entities (Task 4), `Manifests.Application` DTOs (Task 6).
- Produces: `ManifestService.CreateManifestAsync(CreateManifestRequest, ClaimsPrincipal, CancellationToken) -> ManifestResponse`. `ManifestService`'s field layout (`_dbContext`, `_currentTenantProvider`, `_auditLogger`, `_shipmentTransitionService`, `_branchAuthorizationChecker`, `_branchLookupService`) and its private `CheckBranchAccessAsync`/`LoadResponseAsync`/`ToResponse` helpers are fixed here — Tasks 8-9 add no new constructor parameters and reuse these exact helpers.

- [ ] **Step 1: Write the failing tests**

`tests/Manifests.Tests/ManifestServiceCreationTests.cs`:

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

file class FakeBranchLookup : IBranchLookupService
{
    private readonly HashSet<Guid> _knownBranchIds;
    public FakeBranchLookup(params Guid[] knownBranchIds) => _knownBranchIds = knownBranchIds.ToHashSet();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_knownBranchIds.Contains(branchId) ? new BranchZoneInfo(branchId, null) : null);
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class NoBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(new HashSet<Guid>());
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

// Empty stub — CreateManifestAsync never calls IShipmentTransitionService, but ManifestService's
// constructor requires one. Later test files (Task 8+) provide a fuller in-memory fake.
file class StubShipmentTransitionService : IShipmentTransitionService
{
    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class ManifestServiceCreationTests
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

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static ManifestService CreateService(TestDbContext dbContext, Guid organizationId, Guid fromBranchId, Guid toBranchId, IBranchAuthorizationChecker? branchAuthorizationChecker = null) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new StubShipmentTransitionService(),
            branchAuthorizationChecker ?? new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));

    [Fact]
    public async Task CreateManifestAsync_ValidRequest_CreatesDraftManifestWithGeneratedCode()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId, fromBranchId, toBranchId);

        var result = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));

        Assert.Equal(ManifestStatus.Draft, result.Status);
        Assert.Equal(fromBranchId, result.FromBranchId);
        Assert.Equal(toBranchId, result.ToBranchId);
        Assert.Equal(0, result.ShipmentCount);
        Assert.Matches(@"^MNF-\d{8}-\d{3}$", result.Code);
    }

    [Fact]
    public async Task CreateManifestAsync_TwoManifestsSameDaySameOrg_GetDistinctSequentialCodes()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId, fromBranchId, toBranchId);

        var first = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));
        var second = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));

        Assert.NotEqual(first.Code, second.Code);
        Assert.EndsWith("001", first.Code);
        Assert.EndsWith("002", second.Code);
    }

    [Fact]
    public async Task CreateManifestAsync_SameFromAndToBranch_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId, branchId, branchId);

        await Assert.ThrowsAsync<ManifestValidationException>(() =>
            service.CreateManifestAsync(new CreateManifestRequest(branchId, branchId), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateManifestAsync_UnknownFromBranch_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId, Guid.NewGuid(), toBranchId);

        await Assert.ThrowsAsync<ManifestValidationException>(() =>
            service.CreateManifestAsync(new CreateManifestRequest(Guid.NewGuid(), toBranchId), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateManifestAsync_CallerDeniedBranchAccess_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId, fromBranchId, toBranchId, new NoBranchesAuthorizationChecker());

        await Assert.ThrowsAsync<ManifestValidationException>(() =>
            service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid())));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Manifests.Tests/Manifests.Tests.csproj --filter ManifestServiceCreationTests -v n`
Expected: FAIL — `ManifestService` doesn't exist.

- [ ] **Step 3: Write ManifestService**

`src/Modules/Manifests/Application/ManifestService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Manifests.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Manifests.Application;

public partial class ManifestService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly IBranchLookupService _branchLookupService;

    public ManifestService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IShipmentTransitionService shipmentTransitionService,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IBranchLookupService branchLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _shipmentTransitionService = shipmentTransitionService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _branchLookupService = branchLookupService;
    }

    public async Task<ManifestResponse> CreateManifestAsync(CreateManifestRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ManifestValidationException("No active organization context.");

        if (request.FromBranchId == request.ToBranchId)
        {
            throw new ManifestValidationException("FromBranchId and ToBranchId must be different branches.");
        }
        if (await _branchLookupService.GetZoneInfoAsync(request.FromBranchId, cancellationToken) is null)
        {
            throw new ManifestValidationException("FromBranchId does not refer to a branch in this organization.");
        }
        if (await _branchLookupService.GetZoneInfoAsync(request.ToBranchId, cancellationToken) is null)
        {
            throw new ManifestValidationException("ToBranchId does not refer to a branch in this organization.");
        }

        await CheckBranchAccessAsync(actingUser, "manifest.create", request.FromBranchId, cancellationToken);

        var actingUserId = actingUser.GetUserId()
            ?? throw new ManifestValidationException("Acting user could not be resolved.");

        // Insert-and-catch-DbUpdateException-retry on the unique (OrganizationId, Code) index —
        // same race-handling shape as Shipment.Awb generation (Phase 6, commit a83c320's pattern).
        const int maxCodeAttempts = 3;
        Manifest manifest = null!;
        for (var attempt = 1; attempt <= maxCodeAttempts; attempt++)
        {
            var code = await GenerateManifestCodeAsync(organizationId, cancellationToken);
            manifest = new Manifest
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Code = code,
                FromBranchId = request.FromBranchId,
                ToBranchId = request.ToBranchId,
                Status = ManifestStatus.Draft,
                ShipmentCount = 0,
                CreatedByUserId = actingUserId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _dbContext.Set<Manifest>().Add(manifest);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException) when (attempt < maxCodeAttempts)
            {
                _dbContext.Entry(manifest).State = EntityState.Detached;
            }
        }

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "manifest.created", "Manifest", manifest.Id.ToString()), cancellationToken);
        return await LoadResponseAsync(manifest.Id, cancellationToken);
    }

    // Format: MNF-{yyyyMMdd}-{3-digit sequence}, e.g. MNF-20260818-001 (BRANCH-OPERATIONS.md §2's
    // literal example) — unique per (OrganizationId, Code). Sequence: current max existing suffix
    // for (OrganizationId, today's date-prefix) + 1. Does NOT itself guarantee uniqueness under
    // concurrency; CreateManifestAsync's catch-DbUpdateException-retry loop is what makes this
    // safe, exactly like Shipment.Awb generation (Phase 6).
    private async Task<string> GenerateManifestCodeAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var datePart = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var codePrefix = $"MNF-{datePart}-";

        var existingCodes = await _dbContext.Set<Manifest>()
            .Where(m => m.OrganizationId == organizationId && m.Code.StartsWith(codePrefix))
            .Select(m => m.Code)
            .ToListAsync(cancellationToken);
        var nextSequence = existingCodes.Count == 0
            ? 1
            : existingCodes.Select(c => int.Parse(c[^3..])).Max() + 1;

        return $"{codePrefix}{nextSequence:D3}";
    }

    private async Task CheckBranchAccessAsync(ClaimsPrincipal actingUser, string permission, Guid branchId, CancellationToken cancellationToken)
    {
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, permission, cancellationToken);
        if (accessible is not null && !accessible.Contains(branchId))
        {
            throw new ManifestValidationException($"Branch is outside your branch access for permission '{permission}'.");
        }
    }

    private async Task<ManifestResponse> LoadResponseAsync(Guid manifestId, CancellationToken cancellationToken)
    {
        var manifest = await _dbContext.Set<Manifest>().FirstAsync(m => m.Id == manifestId, cancellationToken);
        return ToResponse(manifest);
    }

    private static ManifestResponse ToResponse(Manifest manifest) => new(
        manifest.Id, manifest.Code, manifest.FromBranchId, manifest.ToBranchId,
        manifest.VehiclePlate, manifest.DriverId, manifest.Status, manifest.ShipmentCount, manifest.CreatedAt);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Manifests.Tests/Manifests.Tests.csproj --filter ManifestServiceCreationTests -v n`
Expected: PASS (5 tests).

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Manifests/Application/ManifestService.cs tests/Manifests.Tests/ManifestServiceCreationTests.cs
git commit -m "feat: add ManifestService.CreateManifestAsync with code generation"
```

---

### Task 8: Manifests Application — ManifestService.ScanAsync and SealAsync

**Files:**
- Create: `src/Modules/Manifests/Application/ManifestService.Scanning.cs`
- Test: `tests/Manifests.Tests/ManifestServiceScanningTests.cs`

**Interfaces:**
- Consumes: `ManifestService`'s constructor/helpers (Task 7), `IShipmentTransitionService.GetScanInfoByAwbAsync`/`SortAsync` (Task 3).
- Produces: `ManifestService.ScanAsync(Guid, ScanShipmentRequest, ClaimsPrincipal, CancellationToken) -> ManifestShipmentResponse` and `ManifestService.SealAsync(Guid, ClaimsPrincipal, CancellationToken) -> ManifestResponse` — Task 10's endpoints and Task 12's Api.Tests consume these.

- [ ] **Step 1: Write the failing tests**

`tests/Manifests.Tests/ManifestServiceScanningTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Manifests.Application;
using Courier.Manifests.Domain;
using Courier.Manifests.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
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

file class FakeBranchLookup : IBranchLookupService
{
    private readonly HashSet<Guid> _knownBranchIds;
    public FakeBranchLookup(params Guid[] knownBranchIds) => _knownBranchIds = knownBranchIds.ToHashSet();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_knownBranchIds.Contains(branchId) ? new BranchZoneInfo(branchId, null) : null);
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

// In-memory stand-in for the real Shipments-module ShipmentService — lets ManifestService's scan
// logic be unit-tested without needing Shipments' full EF model, mirroring how Shipments.Tests
// itself fakes IBranchLookupService/IGeographyLookupService rather than standing up real ones.
file class FakeShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public List<Guid> SortCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);

    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.Values.FirstOrDefault(s => s.Awb == awb));

    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        SortCalls.Add(shipmentId);
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.Sorted };
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class ManifestServiceScanningTests
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

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static async Task<(ManifestService Service, FakeShipmentTransitionService Shipments, Guid ManifestId, Guid FromBranchId, Guid ToBranchId)> SeedDraftManifestAsync(TestDbContext dbContext, Guid organizationId)
    {
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var shipments = new FakeShipmentTransitionService();
        var service = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments,
            new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));

        var manifest = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));
        return (service, shipments, manifest.Id, fromBranchId, toBranchId);
    }

    private static ShipmentScanInfo AtOriginInfo(Guid fromBranchId, Guid toBranchId, string awb = "AWB001") => new(
        Guid.NewGuid(), Guid.NewGuid(), awb, ShipmentStatus.AtOriginBranch, fromBranchId, fromBranchId, toBranchId);

    [Fact]
    public async Task ScanAsync_ShipmentAtOriginBranch_AddsToManifestAndSortsShipment()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, fromBranchId, toBranchId) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());
        var info = AtOriginInfo(fromBranchId, toBranchId);
        shipments.ShipmentsById[info.ShipmentId] = info;

        var result = await service.ScanAsync(manifestId, new ScanShipmentRequest(info.Awb), ActingUser(Guid.NewGuid()));

        Assert.Equal(info.ShipmentId, result.ShipmentId);
        Assert.Equal(info.Awb, result.Awb);
        Assert.Null(result.ReceivedAt);
        Assert.Contains(info.ShipmentId, shipments.SortCalls);

        var manifest = await dbContext.Set<Manifest>().FirstAsync(m => m.Id == manifestId);
        Assert.Equal(1, manifest.ShipmentCount);
    }

    [Fact]
    public async Task ScanAsync_UnknownAwb_ThrowsWithShipmentNotFoundReasonCode()
    {
        await using var dbContext = CreateContext();
        var (service, _, manifestId, _, _) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ScanAsync(manifestId, new ScanShipmentRequest("NOSUCHAWB"), ActingUser(Guid.NewGuid())));
        Assert.Equal("SHIPMENT_NOT_FOUND", ex.ReasonCode);
    }

    [Fact]
    public async Task ScanAsync_ShipmentNotAtOriginBranch_ThrowsWithWrongStateReasonCode()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, fromBranchId, toBranchId) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());
        var info = AtOriginInfo(fromBranchId, toBranchId) with { CurrentStatus = ShipmentStatus.Created };
        shipments.ShipmentsById[info.ShipmentId] = info;

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ScanAsync(manifestId, new ScanShipmentRequest(info.Awb), ActingUser(Guid.NewGuid())));
        Assert.Equal("WRONG_STATE", ex.ReasonCode);
    }

    [Fact]
    public async Task ScanAsync_ShipmentAtDifferentBranch_ThrowsWithWrongCustodyReasonCode()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, fromBranchId, toBranchId) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());
        var info = AtOriginInfo(fromBranchId, toBranchId) with { CurrentBranchId = Guid.NewGuid() };
        shipments.ShipmentsById[info.ShipmentId] = info;

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ScanAsync(manifestId, new ScanShipmentRequest(info.Awb), ActingUser(Guid.NewGuid())));
        Assert.Equal("WRONG_CUSTODY", ex.ReasonCode);
    }

    [Fact]
    public async Task ScanAsync_ManifestAlreadySealed_ThrowsWithManifestNotOpenReasonCode()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, fromBranchId, toBranchId) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());
        var seedInfo = AtOriginInfo(fromBranchId, toBranchId);
        shipments.ShipmentsById[seedInfo.ShipmentId] = seedInfo;
        await service.ScanAsync(manifestId, new ScanShipmentRequest(seedInfo.Awb), ActingUser(Guid.NewGuid()));
        await service.SealAsync(manifestId, ActingUser(Guid.NewGuid()));

        var secondInfo = AtOriginInfo(fromBranchId, toBranchId, "AWB002");
        shipments.ShipmentsById[secondInfo.ShipmentId] = secondInfo;

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ScanAsync(manifestId, new ScanShipmentRequest(secondInfo.Awb), ActingUser(Guid.NewGuid())));
        Assert.Equal("MANIFEST_NOT_OPEN", ex.ReasonCode);
    }

    [Fact]
    public async Task ScanAsync_UnknownManifest_ThrowsManifestNotFoundException()
    {
        await using var dbContext = CreateContext();
        var shipments = new FakeShipmentTransitionService();
        var service = new ManifestService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), shipments,
            new AllBranchesAuthorizationChecker(), new FakeBranchLookup());

        await Assert.ThrowsAsync<ManifestNotFoundException>(() =>
            service.ScanAsync(Guid.NewGuid(), new ScanShipmentRequest("AWB001"), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task SealAsync_DraftManifestWithShipments_MovesToSealed()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, fromBranchId, toBranchId) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());
        var info = AtOriginInfo(fromBranchId, toBranchId);
        shipments.ShipmentsById[info.ShipmentId] = info;
        await service.ScanAsync(manifestId, new ScanShipmentRequest(info.Awb), ActingUser(Guid.NewGuid()));

        var result = await service.SealAsync(manifestId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ManifestStatus.Sealed, result.Status);
    }

    [Fact]
    public async Task SealAsync_NoShipmentsScanned_Throws()
    {
        await using var dbContext = CreateContext();
        var (service, _, manifestId, _, _) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<ManifestValidationException>(() => service.SealAsync(manifestId, ActingUser(Guid.NewGuid())));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Manifests.Tests/Manifests.Tests.csproj --filter ManifestServiceScanningTests -v n`
Expected: FAIL — `ScanAsync`/`SealAsync` don't exist.

- [ ] **Step 3: Write ScanAsync and SealAsync**

`src/Modules/Manifests/Application/ManifestService.Scanning.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Manifests.Domain;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Manifests.Application;

public partial class ManifestService
{
    public async Task<ManifestShipmentResponse> ScanAsync(Guid manifestId, ScanShipmentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ManifestValidationException("No active organization context.");
        if (string.IsNullOrWhiteSpace(request.Awb))
        {
            throw new ManifestValidationException("Awb is required.");
        }

        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken)
            ?? throw new ManifestNotFoundException(manifestId);

        // Check 1 (permission) is enforced at the route via .RequirePermission("manifest.create");
        // check 2 (branch access) here.
        await CheckBranchAccessAsync(actingUser, "manifest.create", manifest.FromBranchId, cancellationToken);

        if (manifest.Status != ManifestStatus.Draft)
        {
            throw new ManifestScanRejectedException("MANIFEST_NOT_OPEN", $"Manifest is {manifest.Status}, not open for adding shipments.");
        }

        var scanInfo = await _shipmentTransitionService.GetScanInfoByAwbAsync(request.Awb, cancellationToken)
            ?? throw new ManifestScanRejectedException("SHIPMENT_NOT_FOUND", $"No shipment found for AWB '{request.Awb}'.");

        // Check 3 (shipment state) + check 4 (expected operation) collapse into one check here: the
        // only operation this endpoint performs IS the AtOriginBranch -> Sorted transition, so
        // "shipment is AtOriginBranch" fully captures both.
        if (scanInfo.CurrentStatus != ShipmentStatus.AtOriginBranch)
        {
            throw new ManifestScanRejectedException("WRONG_STATE", $"Shipment must be AtOriginBranch to be added to a manifest, was {scanInfo.CurrentStatus}.");
        }
        // Check 5 (custody consistency): the shipment must physically be at the branch this
        // manifest departs from.
        if (scanInfo.CurrentBranchId != manifest.FromBranchId)
        {
            throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's current branch does not match this manifest's FromBranchId.");
        }

        await _shipmentTransitionService.SortAsync(scanInfo.ShipmentId, actingUser, cancellationToken);

        var manifestShipment = new ManifestShipment
        {
            Id = Guid.NewGuid(),
            ManifestId = manifestId,
            ShipmentId = scanInfo.ShipmentId,
            ScannedAt = DateTimeOffset.UtcNow,
            ReceivedAt = null
        };
        _dbContext.Set<ManifestShipment>().Add(manifestShipment);
        manifest.ShipmentCount += 1;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "manifest.shipment_scanned", "Manifest", manifestId.ToString()), cancellationToken);
        return new ManifestShipmentResponse(manifestShipment.Id, scanInfo.ShipmentId, scanInfo.Awb, manifestShipment.ScannedAt, manifestShipment.ReceivedAt);
    }

    public async Task<ManifestResponse> SealAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ManifestValidationException("No active organization context.");

        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken)
            ?? throw new ManifestNotFoundException(manifestId);

        await CheckBranchAccessAsync(actingUser, "manifest.seal", manifest.FromBranchId, cancellationToken);

        if (manifest.Status != ManifestStatus.Draft)
        {
            throw new ManifestValidationException($"Manifest must be Draft to seal, was {manifest.Status}.");
        }
        if (manifest.ShipmentCount == 0)
        {
            throw new ManifestValidationException("Manifest has no shipments to seal.");
        }

        manifest.Status = ManifestStatus.Sealed;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "manifest.sealed", "Manifest", manifestId.ToString()), cancellationToken);
        return ToResponse(manifest);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Manifests.Tests/Manifests.Tests.csproj --filter ManifestServiceScanningTests -v n`
Expected: PASS (8 tests).

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Manifests/Application/ManifestService.Scanning.cs tests/Manifests.Tests/ManifestServiceScanningTests.cs
git commit -m "feat: add ManifestService.ScanAsync and SealAsync with five-check scan validation"
```

---

### Task 9: Manifests Application — DispatchAsync, ReceiveScanAsync, CloseAsync, and Reads

**Files:**
- Create: `src/Modules/Manifests/Application/ManifestService.Transitions.cs`
- Create: `src/Modules/Manifests/Application/ManifestService.Reads.cs`
- Test: `tests/Manifests.Tests/ManifestServiceTransitionsTests.cs`

**Interfaces:**
- Consumes: `ManifestService`'s constructor/helpers (Task 7), `IShipmentTransitionService.DispatchManyAsync`/`ReceiveAtDestinationAsync`/`GetScanInfoAsync` (Task 3).
- Produces: `DispatchAsync`, `ReceiveScanAsync`, `CloseAsync`, `GetByIdAsync`, `GetMissingShipmentsAsync` — Task 10's endpoints consume all five; Task 12's Api.Tests exercises the full lifecycle through them.

- [ ] **Step 1: Write the failing tests**

`tests/Manifests.Tests/ManifestServiceTransitionsTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Manifests.Application;
using Courier.Manifests.Domain;
using Courier.Manifests.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
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

file class FakeBranchLookup : IBranchLookupService
{
    private readonly HashSet<Guid> _knownBranchIds;
    public FakeBranchLookup(params Guid[] knownBranchIds) => _knownBranchIds = knownBranchIds.ToHashSet();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_knownBranchIds.Contains(branchId) ? new BranchZoneInfo(branchId, null) : null);
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class FakeShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public List<Guid> DispatchManyCalls { get; } = new();
    public List<Guid> ReceiveCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);

    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.Values.FirstOrDefault(s => s.Awb == awb));

    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.Sorted };
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        foreach (var id in shipmentIds)
        {
            DispatchManyCalls.Add(id);
            var info = ShipmentsById[id];
            ShipmentsById[id] = info with { CurrentStatus = ShipmentStatus.InTransit, CurrentBranchId = null };
        }
        return Task.CompletedTask;
    }

    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        ReceiveCalls.Add(shipmentId);
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.AtDestinationBranch, CurrentBranchId = info.DestinationBranchId };
        return Task.FromResult<ShipmentResponse?>(null);
    }
}

public class ManifestServiceTransitionsTests
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

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static async Task<(ManifestService Service, FakeShipmentTransitionService Shipments, Guid ManifestId, Guid FromBranchId, Guid ToBranchId, Guid ShipmentId, string Awb)> SeedSealedManifestWithOneShipmentAsync(TestDbContext dbContext, Guid organizationId)
    {
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var shipments = new FakeShipmentTransitionService();
        var service = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments,
            new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));

        var manifest = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));
        var shipmentId = Guid.NewGuid();
        var awb = "AWB001";
        shipments.ShipmentsById[shipmentId] = new ShipmentScanInfo(shipmentId, organizationId, awb, ShipmentStatus.AtOriginBranch, fromBranchId, fromBranchId, toBranchId);
        await service.ScanAsync(manifest.Id, new ScanShipmentRequest(awb), ActingUser(Guid.NewGuid()));
        await service.SealAsync(manifest.Id, ActingUser(Guid.NewGuid()));

        return (service, shipments, manifest.Id, fromBranchId, toBranchId, shipmentId, awb);
    }

    [Fact]
    public async Task DispatchAsync_SealedManifest_MovesToInTransitAndDispatchesAllShipments()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, _, _, shipmentId, _) = await SeedSealedManifestWithOneShipmentAsync(dbContext, Guid.NewGuid());

        var result = await service.DispatchAsync(manifestId, new DispatchManifestRequest("BA 2 KHA 1234", null), ActingUser(Guid.NewGuid()));

        Assert.Equal(ManifestStatus.InTransit, result.Status);
        Assert.Equal("BA 2 KHA 1234", result.VehiclePlate);
        Assert.Contains(shipmentId, shipments.DispatchManyCalls);
    }

    [Fact]
    public async Task DispatchAsync_DraftManifest_Throws()
    {
        await using var dbContext = CreateContext();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var shipments = new FakeShipmentTransitionService();
        var service = new ManifestService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), shipments,
            new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));
        var manifest = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<ManifestValidationException>(() =>
            service.DispatchAsync(manifest.Id, new DispatchManifestRequest("BA 2 KHA 1234", null), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task DispatchAsync_MissingVehiclePlate_Throws()
    {
        await using var dbContext = CreateContext();
        var (service, _, manifestId, _, _, _, _) = await SeedSealedManifestWithOneShipmentAsync(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<ManifestValidationException>(() =>
            service.DispatchAsync(manifestId, new DispatchManifestRequest("", null), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ReceiveScanAsync_ShipmentInTransitOnThisManifest_MarksReceivedAndWritesTransfer()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipments, manifestId, fromBranchId, toBranchId, shipmentId, awb) = await SeedSealedManifestWithOneShipmentAsync(dbContext, organizationId);
        await service.DispatchAsync(manifestId, new DispatchManifestRequest("BA 2 KHA 1234", null), ActingUser(Guid.NewGuid()));

        var result = await service.ReceiveScanAsync(manifestId, new ScanShipmentRequest(awb), ActingUser(Guid.NewGuid()));

        Assert.Equal(shipmentId, result.ShipmentId);
        Assert.NotNull(result.ReceivedAt);
        Assert.Contains(shipmentId, shipments.ReceiveCalls);

        var manifest = await dbContext.Set<Manifest>().FirstAsync(m => m.Id == manifestId);
        Assert.Equal(ManifestStatus.Received, manifest.Status);

        var transfer = await dbContext.Set<ShipmentTransfer>().SingleAsync(t => t.ManifestId == manifestId);
        Assert.Equal(shipmentId, transfer.ShipmentId);
        Assert.Equal(fromBranchId, transfer.FromBranchId);
        Assert.Equal(toBranchId, transfer.ToBranchId);
    }

    [Fact]
    public async Task ReceiveScanAsync_ShipmentNotOnThisManifest_ThrowsWithNotOnManifestReasonCode()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipments, manifestId, _, _, _, _) = await SeedSealedManifestWithOneShipmentAsync(dbContext, organizationId);
        await service.DispatchAsync(manifestId, new DispatchManifestRequest("BA 2 KHA 1234", null), ActingUser(Guid.NewGuid()));

        var strangerId = Guid.NewGuid();
        shipments.ShipmentsById[strangerId] = new ShipmentScanInfo(strangerId, organizationId, "AWB999", ShipmentStatus.InTransit, null, Guid.NewGuid(), Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ReceiveScanAsync(manifestId, new ScanShipmentRequest("AWB999"), ActingUser(Guid.NewGuid())));
        Assert.Equal("NOT_ON_MANIFEST", ex.ReasonCode);
    }

    [Fact]
    public async Task ReceiveScanAsync_AlreadyReceived_ThrowsWithAlreadyReceivedReasonCode()
    {
        await using var dbContext = CreateContext();
        var (service, _, manifestId, _, _, _, awb) = await SeedSealedManifestWithOneShipmentAsync(dbContext, Guid.NewGuid());
        await service.DispatchAsync(manifestId, new DispatchManifestRequest("BA 2 KHA 1234", null), ActingUser(Guid.NewGuid()));
        await service.ReceiveScanAsync(manifestId, new ScanShipmentRequest(awb), ActingUser(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ReceiveScanAsync(manifestId, new ScanShipmentRequest(awb), ActingUser(Guid.NewGuid())));
        Assert.Equal("ALREADY_RECEIVED", ex.ReasonCode);
    }

    [Fact]
    public async Task CloseAsync_ReceivedManifestWithUnscannedShipment_ClosesAndSurfacesMissingShipment()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var shipments = new FakeShipmentTransitionService();
        var service = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments,
            new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));
        var manifest = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));

        var receivedShipmentId = Guid.NewGuid();
        shipments.ShipmentsById[receivedShipmentId] = new ShipmentScanInfo(receivedShipmentId, organizationId, "AWB-RECEIVED", ShipmentStatus.AtOriginBranch, fromBranchId, fromBranchId, toBranchId);
        await service.ScanAsync(manifest.Id, new ScanShipmentRequest("AWB-RECEIVED"), ActingUser(Guid.NewGuid()));

        var missingShipmentId = Guid.NewGuid();
        shipments.ShipmentsById[missingShipmentId] = new ShipmentScanInfo(missingShipmentId, organizationId, "AWB-MISSING", ShipmentStatus.AtOriginBranch, fromBranchId, fromBranchId, toBranchId);
        await service.ScanAsync(manifest.Id, new ScanShipmentRequest("AWB-MISSING"), ActingUser(Guid.NewGuid()));

        await service.SealAsync(manifest.Id, ActingUser(Guid.NewGuid()));
        await service.DispatchAsync(manifest.Id, new DispatchManifestRequest("BA 2 KHA 1234", null), ActingUser(Guid.NewGuid()));
        await service.ReceiveScanAsync(manifest.Id, new ScanShipmentRequest("AWB-RECEIVED"), ActingUser(Guid.NewGuid()));

        var closed = await service.CloseAsync(manifest.Id, ActingUser(Guid.NewGuid()));
        Assert.Equal(ManifestStatus.Closed, closed.Status);

        var missing = await service.GetMissingShipmentsAsync(manifest.Id);
        var missingShipment = Assert.Single(missing!);
        Assert.Equal(missingShipmentId, missingShipment.ShipmentId);
        Assert.Equal("AWB-MISSING", missingShipment.Awb);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownManifest_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = new ManifestService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new FakeShipmentTransitionService(),
            new AllBranchesAuthorizationChecker(), new FakeBranchLookup());

        Assert.Null(await service.GetByIdAsync(Guid.NewGuid()));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Manifests.Tests/Manifests.Tests.csproj --filter ManifestServiceTransitionsTests -v n`
Expected: FAIL — `DispatchAsync`/`ReceiveScanAsync`/`CloseAsync`/`GetByIdAsync`/`GetMissingShipmentsAsync` don't exist.

- [ ] **Step 3: Write DispatchAsync, ReceiveScanAsync, CloseAsync**

`src/Modules/Manifests/Application/ManifestService.Transitions.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Manifests.Domain;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Manifests.Application;

public partial class ManifestService
{
    public async Task<ManifestResponse> DispatchAsync(Guid manifestId, DispatchManifestRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ManifestValidationException("No active organization context.");

        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken)
            ?? throw new ManifestNotFoundException(manifestId);

        await CheckBranchAccessAsync(actingUser, "manifest.dispatch", manifest.FromBranchId, cancellationToken);

        if (manifest.Status != ManifestStatus.Sealed)
        {
            throw new ManifestValidationException($"Manifest must be Sealed to dispatch, was {manifest.Status}.");
        }
        if (string.IsNullOrWhiteSpace(request.VehiclePlate))
        {
            throw new ManifestValidationException("VehiclePlate is required to dispatch a manifest.");
        }

        var shipmentIds = await _dbContext.Set<ManifestShipment>()
            .Where(ms => ms.ManifestId == manifestId)
            .Select(ms => ms.ShipmentId)
            .ToListAsync(cancellationToken);

        // This plan's resolved design decision #2: goes straight to InTransit, no separate
        // persisted "Dispatched" resting state.
        await _shipmentTransitionService.DispatchManyAsync(shipmentIds, actingUser, cancellationToken);

        manifest.VehiclePlate = request.VehiclePlate;
        manifest.DriverId = request.DriverId;
        manifest.Status = ManifestStatus.InTransit;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "manifest.dispatched", "Manifest", manifestId.ToString()), cancellationToken);
        return ToResponse(manifest);
    }

    public async Task<ManifestShipmentResponse> ReceiveScanAsync(Guid manifestId, ScanShipmentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ManifestValidationException("No active organization context.");
        if (string.IsNullOrWhiteSpace(request.Awb))
        {
            throw new ManifestValidationException("Awb is required.");
        }

        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken)
            ?? throw new ManifestNotFoundException(manifestId);

        await CheckBranchAccessAsync(actingUser, "manifest.receive", manifest.ToBranchId, cancellationToken);

        if (manifest.Status is not (ManifestStatus.InTransit or ManifestStatus.Received))
        {
            throw new ManifestScanRejectedException("MANIFEST_NOT_RECEIVING", $"Manifest is {manifest.Status}, not open for receiving.");
        }

        var scanInfo = await _shipmentTransitionService.GetScanInfoByAwbAsync(request.Awb, cancellationToken)
            ?? throw new ManifestScanRejectedException("SHIPMENT_NOT_FOUND", $"No shipment found for AWB '{request.Awb}'.");

        // Check 4 (expected operation): the shipment must actually be listed on THIS manifest, not
        // just any in-transit shipment.
        var manifestShipment = await _dbContext.Set<ManifestShipment>()
            .FirstOrDefaultAsync(ms => ms.ManifestId == manifestId && ms.ShipmentId == scanInfo.ShipmentId, cancellationToken)
            ?? throw new ManifestScanRejectedException("NOT_ON_MANIFEST", "This shipment is not listed on this manifest.");

        if (manifestShipment.ReceivedAt is not null)
        {
            throw new ManifestScanRejectedException("ALREADY_RECEIVED", "This shipment has already been receive-scanned on this manifest.");
        }
        if (scanInfo.CurrentStatus != ShipmentStatus.InTransit)
        {
            throw new ManifestScanRejectedException("WRONG_STATE", $"Shipment must be InTransit to receive, was {scanInfo.CurrentStatus}.");
        }
        // Check 5 (custody consistency): an in-transit shipment has no current branch, and its
        // declared destination must match where this manifest is actually headed.
        if (scanInfo.CurrentBranchId is not null)
        {
            throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's custody is inconsistent with being in transit.");
        }
        if (scanInfo.DestinationBranchId != manifest.ToBranchId)
        {
            throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's destination branch does not match this manifest's ToBranchId.");
        }

        await _shipmentTransitionService.ReceiveAtDestinationAsync(scanInfo.ShipmentId, actingUser, cancellationToken);

        manifestShipment.ReceivedAt = DateTimeOffset.UtcNow;
        if (manifest.Status == ManifestStatus.InTransit)
        {
            manifest.Status = ManifestStatus.Received;
        }
        _dbContext.Set<ShipmentTransfer>().Add(new ShipmentTransfer
        {
            Id = Guid.NewGuid(),
            ManifestId = manifestId,
            ShipmentId = scanInfo.ShipmentId,
            FromBranchId = manifest.FromBranchId,
            ToBranchId = manifest.ToBranchId,
            Timestamp = manifestShipment.ReceivedAt.Value
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "manifest.shipment_received", "Manifest", manifestId.ToString()), cancellationToken);
        return new ManifestShipmentResponse(manifestShipment.Id, scanInfo.ShipmentId, scanInfo.Awb, manifestShipment.ScannedAt, manifestShipment.ReceivedAt);
    }

    public async Task<ManifestResponse> CloseAsync(Guid manifestId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ManifestValidationException("No active organization context.");

        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken)
            ?? throw new ManifestNotFoundException(manifestId);

        await CheckBranchAccessAsync(actingUser, "manifest.close", manifest.ToBranchId, cancellationToken);

        // InTransit is allowed too, not just Received: a manifest where staff never scanned a
        // single shipment during receiving (everything on it turned out missing) must still be
        // closeable, not stuck open forever.
        if (manifest.Status is not (ManifestStatus.InTransit or ManifestStatus.Received))
        {
            throw new ManifestValidationException($"Manifest must be InTransit or Received to close, was {manifest.Status}.");
        }

        manifest.Status = ManifestStatus.Closed;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "manifest.closed", "Manifest", manifestId.ToString()), cancellationToken);
        return ToResponse(manifest);
    }
}
```

- [ ] **Step 4: Write GetByIdAsync and GetMissingShipmentsAsync**

`src/Modules/Manifests/Application/ManifestService.Reads.cs`:

```csharp
using Courier.Manifests.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Manifests.Application;

public partial class ManifestService
{
    public async Task<ManifestResponse?> GetByIdAsync(Guid manifestId, CancellationToken cancellationToken = default)
    {
        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken);
        return manifest is null ? null : ToResponse(manifest);
    }

    // Meaningful once the manifest is Closed (a null ReceivedAt then IS the missing-shipment
    // exception per this plan's resolved design decision #4) — also usable while a manifest is
    // still InTransit/Received to show which shipments haven't been scanned in yet.
    public async Task<List<MissingShipmentResponse>?> GetMissingShipmentsAsync(Guid manifestId, CancellationToken cancellationToken = default)
    {
        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken);
        if (manifest is null)
        {
            return null;
        }

        var unreceived = await _dbContext.Set<ManifestShipment>()
            .Where(ms => ms.ManifestId == manifestId && ms.ReceivedAt == null)
            .ToListAsync(cancellationToken);

        var result = new List<MissingShipmentResponse>();
        foreach (var manifestShipment in unreceived)
        {
            var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(manifestShipment.ShipmentId, cancellationToken);
            result.Add(new MissingShipmentResponse(manifestShipment.ShipmentId, scanInfo?.Awb ?? string.Empty, manifestShipment.ScannedAt));
        }
        return result;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/Manifests.Tests/Manifests.Tests.csproj
```

Expected: PASS — every test in `Manifests.Tests` so far.

- [ ] **Step 6: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Manifests/Application/ManifestService.Transitions.cs src/Modules/Manifests/Application/ManifestService.Reads.cs tests/Manifests.Tests/ManifestServiceTransitionsTests.cs
git commit -m "feat: add ManifestService.DispatchAsync, ReceiveScanAsync, CloseAsync, and reads"
```

---

### Task 10: Manifests Presentation — Endpoints and DI Wiring

**Files:**
- Create: `src/Modules/Manifests/Presentation/ManifestsEndpoints.cs`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: `ManifestService` (Tasks 7-9), `RequirePermission` extension (`Courier.SharedKernel`, existing).
- Produces: all 8 HTTP endpoints under `/api/v1/manifests` — Task 12's Api.Tests exercises every one of them.

- [ ] **Step 1: Write the endpoints**

`src/Modules/Manifests/Presentation/ManifestsEndpoints.cs`:

```csharp
using Courier.Manifests.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Manifests.Presentation;

public static class ManifestsEndpoints
{
    public static void MapManifestsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/manifests").RequireAuthorization();

        group.MapPost("/", async (CreateManifestRequest request, ManifestService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CreateManifestAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/manifests/{result.Id}", result);
            }
            catch (ManifestValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "MANIFEST_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("manifest.create");

        group.MapGet("/{id:guid}", async (Guid id, ManifestService service, HttpContext httpContext) =>
        {
            var result = await service.GetByIdAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("manifest.read");

        group.MapGet("/{id:guid}/missing-shipments", async (Guid id, ManifestService service, HttpContext httpContext) =>
        {
            var result = await service.GetMissingShipmentsAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("manifest.read");

        group.MapPost("/{id:guid}/scan", async (Guid id, ScanShipmentRequest request, ManifestService service, HttpContext httpContext) =>
            await HandleScanAsync(() => service.ScanAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("manifest.create");

        group.MapPost("/{id:guid}/seal", async (Guid id, ManifestService service, HttpContext httpContext) =>
            await HandleManifestAsync(() => service.SealAsync(id, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("manifest.seal");

        group.MapPost("/{id:guid}/dispatch", async (Guid id, DispatchManifestRequest request, ManifestService service, HttpContext httpContext) =>
            await HandleManifestAsync(() => service.DispatchAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("manifest.dispatch");

        group.MapPost("/{id:guid}/receive-scan", async (Guid id, ScanShipmentRequest request, ManifestService service, HttpContext httpContext) =>
            await HandleScanAsync(() => service.ReceiveScanAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("manifest.receive");

        group.MapPost("/{id:guid}/close", async (Guid id, ManifestService service, HttpContext httpContext) =>
            await HandleManifestAsync(() => service.CloseAsync(id, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("manifest.close");
    }

    private static async Task<IResult> HandleManifestAsync(Func<Task<ManifestResponse>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (ManifestNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ManifestValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "MANIFEST_VALIDATION_FAILED", message = ex.Message } });
        }
    }

    private static async Task<IResult> HandleScanAsync(Func<Task<ManifestShipmentResponse>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (ManifestNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ManifestScanRejectedException ex)
        {
            return Results.BadRequest(new { error = new { code = "MANIFEST_SCAN_REJECTED", reason = ex.ReasonCode, message = ex.Message } });
        }
        catch (ManifestValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "MANIFEST_VALIDATION_FAILED", message = ex.Message } });
        }
    }
}
```

Note: `ManifestScanRejectedException` extends `ManifestValidationException`, so `HandleScanAsync`'s `catch` blocks are ordered most-derived-first — the same C# requirement `ShipmentsEndpoints.HandleTransitionAsync` already follows for `ShipmentInvalidTransitionException`/`ShipmentValidationException` (Phase 6).

- [ ] **Step 2: Wire DI and endpoint mapping in Program.cs**

In `src/Api/Program.cs`, add `using Courier.Manifests.Application;` and `using Courier.Manifests.Presentation;` alphabetically between the `Courier.Identity.Presentation` and `Courier.Merchants.Application` usings.

After the existing `builder.Services.AddScoped<IShipmentTransitionService>(sp => sp.GetRequiredService<ShipmentService>());` line (added in Task 3), add:

```csharp
builder.Services.AddScoped<ManifestService>();
```

After the existing `app.MapShipmentsEndpoints();` line, add:

```csharp
app.MapManifestsEndpoints();
```

- [ ] **Step 3: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Manifests/Presentation src/Api/Program.cs
git commit -m "feat: add Manifests endpoints and DI wiring"
```

---

### Task 11: Persistence — Wire Manifests into AppDbContext, Add Migration

**Files:**
- Modify: `src/Persistence/AppDbContext.cs`
- Create: `src/Persistence/Migrations/` (generated)

**Interfaces:**
- Consumes: all Manifests configurations (Task 5), the `manifest.read` permission constant (Task 2).
- Produces: `AppDbContext.{Manifests, ManifestShipments, ShipmentTransfers}`, a migration creating all three tables with FKs/indexes/query filter plus the new `manifest.read` `Permission` row. Task 12's Api.Tests query through this context.

- [ ] **Step 1: Update AppDbContext**

Add `using Courier.Manifests.Domain;` and `using Courier.Manifests.Infrastructure;` to the top of `src/Persistence/AppDbContext.cs`, alphabetically after `Courier.Identity.Infrastructure` and before `Courier.Merchants.Domain` (or wherever this codebase's existing using block places `Manifests` alphabetically among its current entries).

Add to the `DbSet` list, after `ShipmentEvents`:

```csharp
    public DbSet<Manifest> Manifests => Set<Manifest>();
    public DbSet<ManifestShipment> ManifestShipments => Set<ManifestShipment>();
    public DbSet<ShipmentTransfer> ShipmentTransfers => Set<ShipmentTransfer>();
```

In `OnModelCreating`, add to the `ApplyConfiguration` block, after `ShipmentEventConfiguration`:

```csharp
        modelBuilder.ApplyConfiguration(new ManifestConfiguration());
        modelBuilder.ApplyConfiguration(new ManifestShipmentConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentTransferConfiguration());
```

After the existing `modelBuilder.Entity<Shipment>().HasOne<Organization>()...` block, add:

```csharp
        // Same reasoning for Manifest.OrganizationId: Manifests has no project reference to
        // Identity either.
        modelBuilder.Entity<Manifest>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
```

After the existing `modelBuilder.Entity<Shipment>().HasQueryFilter(...)` line, add:

```csharp
        modelBuilder.Entity<Manifest>().HasQueryFilter(m => _currentTenantProvider.OrganizationId != null && m.OrganizationId == _currentTenantProvider.OrganizationId);
```

After the existing "No tenant filter on ShipmentItem/ShipmentEvent" comment block, add:

```csharp
        // No tenant filter on ManifestShipment/ShipmentTransfer: neither carries its own
        // OrganizationId — tenant isolation flows transitively through
        // ManifestId -> Manifest.OrganizationId (which does have a filter, above).
```

- [ ] **Step 2: Ensure Postgres is running, then generate and apply the migration**

```bash
docker exec courier-postgres-1 pg_isready -U courier
```

If not running:

```bash
docker compose up -d postgres
```

Then:

```bash
dotnet ef migrations add AddManifests --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj
dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj --connection "Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password"
```

Expected: a new migration file appears under `src/Persistence/Migrations/` containing both the three new tables and an `InsertData` for the new `manifest.read` `Permission` row (from Task 2's `PermissionCatalog.All` change); `database update` reports it applied `AddManifests` with no errors.

- [ ] **Step 3: Verify the new tables and permission row exist**

```bash
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"Manifests\""
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"ManifestShipments\""
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"ShipmentTransfers\""
docker exec courier-postgres-1 psql -U courier -d courier -c "SELECT \"Key\" FROM \"Permissions\" WHERE \"Key\" = 'manifest.read';"
```

Expected: each `\d` prints a column listing matching Task 4's entities; the last query returns exactly one row.

- [ ] **Step 4: Verify the solution still builds and all tests pass**

```bash
dotnet build Courier.slnx
dotnet test Courier.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`; all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Persistence
git commit -m "feat: wire Manifests into AppDbContext with migration"
```

---

### Task 12: Api.Tests — Full Manifest Lifecycle, Missing-Shipment Detection, Five-Check Rejections, and Tenant Isolation

**Files:**
- Create: `tests/Api.Tests/ManifestsEndpointsTests.cs`

**Interfaces:**
- Consumes: every endpoint from Task 10, against the real Postgres-backed `AppDbContext` from Task 11, and the still-live `/shipments/{id}/pickup-assign`, `/pickup-scan`, `/receive-origin` endpoints (unchanged since Phase 6) to get a shipment to `AtOriginBranch` before manifest-scanning it.
- Produces: nothing further consumed — this is the exit-criteria proof for Phase 7, and its full-lifecycle test is the reference flow Task 13 ports the two old Shipments tests onto.

- [ ] **Step 1: Write the tests**

`tests/Api.Tests/ManifestsEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Manifests.Application;
using Courier.Manifests.Domain;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class ManifestsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public ManifestsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Manifests Org {suffix}", $"manifests-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private static CreateBranchRequest ValidBranchRequest(string code) => new(
        code, $"Branch {code}", BranchType.Hub, KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null, null);

    private async Task<(Guid FromBranchId, Guid ToBranchId, Guid ServiceTypeId)> SeedBranchesAsync(HttpClient client)
    {
        var fromBranchResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"FR-{Guid.NewGuid():N}"[..10]));
        var fromBranch = await fromBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();
        var toBranchResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"TO-{Guid.NewGuid():N}"[..10]));
        var toBranch = await toBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var originZoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Origin-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var originZone = await originZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        var destZoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Dest-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var destZone = await destZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        await client.PutAsJsonAsync($"/api/v1/branches/{fromBranch!.Id}/delivery-zone", new { DeliveryZoneId = originZone!.Id });
        await client.PutAsJsonAsync($"/api/v1/branches/{toBranch!.Id}/delivery-zone", new { DeliveryZoneId = destZone!.Id });

        var serviceTypesResponse = await client.GetAsync("/api/v1/service-types");
        var serviceTypes = await serviceTypesResponse.Content.ReadFromJsonAsync<List<ServiceTypeResponse>>();
        var standard = serviceTypes!.Single(s => s.Code == ServiceTypeCode.Standard);

        var weightSlabs = await (await client.GetAsync("/api/v1/weight-slabs")).Content.ReadFromJsonAsync<List<WeightSlabResponse>>();
        var slab = weightSlabs!.Single(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m);
        var rateCardResponse = await client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest($"Card-{Guid.NewGuid():N}", DateTimeOffset.UtcNow));
        var rateCard = await rateCardResponse.Content.ReadFromJsonAsync<RateCardResponse>();
        await client.PostAsJsonAsync($"/api/v1/rate-cards/{rateCard!.Id}/zone-rates",
            new CreateZoneRateRequest(originZone.Id, destZone.Id, standard.Id, slab.Id, 100m));

        return (fromBranch.Id, toBranch.Id, standard.Id);
    }

    private static List<Guid> KathmanduWard1Areas() => new()
    {
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
    };

    private async Task<Guid> CreateMerchantAsync(HttpClient staffClient)
    {
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Shipper Pvt. Ltd.", "Shipper", "01-4444444", "shipper@example.com",
            $"shipper-owner-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return merchant!.Id;
    }

    private static CreateShipmentRequest ValidShipmentRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 1, 500m) });

    // Walks a freshly booked shipment through the still-live Phase 6 endpoints up to
    // AtOriginBranch — the point at which this phase's /manifests/{id}/scan takes over.
    private async Task<ShipmentResponse> CreateShipmentAtOriginBranchAsync(HttpClient client, Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId)
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment!.Id}/pickup-assign", new AssignPickupRequest(Guid.NewGuid()));
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/pickup-scan", new { });
        var afterOrigin = await (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { })).Content.ReadFromJsonAsync<ShipmentResponse>();
        return afterOrigin!;
    }

    [Fact]
    public async Task FullManifestLifecycle_DraftToClosed_MovesShipmentThroughCustodyAndSurfacesMissingShipment()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "happy");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (fromBranchId, toBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);

        var received = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);
        var missing = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);

        var manifestResponse = await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId));
        Assert.Equal(HttpStatusCode.Created, manifestResponse.StatusCode);
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<ManifestResponse>();
        Assert.Equal(ManifestStatus.Draft, manifest!.Status);

        var scanReceivedResponse = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/scan", new ScanShipmentRequest(received.Awb));
        Assert.Equal(HttpStatusCode.OK, scanReceivedResponse.StatusCode);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/scan", new ScanShipmentRequest(missing.Awb));

        var afterScanShipment = await (await client.GetAsync($"/api/v1/shipments/{received.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Sorted, afterScanShipment!.CurrentStatus);

        var sealResponse = await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        var sealed_ = await sealResponse.Content.ReadFromJsonAsync<ManifestResponse>();
        Assert.Equal(ManifestStatus.Sealed, sealed_!.Status);
        Assert.Equal(2, sealed_.ShipmentCount);

        var dispatchResponse = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        var dispatched = await dispatchResponse.Content.ReadFromJsonAsync<ManifestResponse>();
        Assert.Equal(ManifestStatus.InTransit, dispatched!.Status);
        Assert.Equal("BA 2 KHA 1234", dispatched.VehiclePlate);

        var afterDispatchShipment = await (await client.GetAsync($"/api/v1/shipments/{received.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.InTransit, afterDispatchShipment!.CurrentStatus);

        var receiveScanResponse = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(received.Awb));
        Assert.Equal(HttpStatusCode.OK, receiveScanResponse.StatusCode);

        var afterReceiveShipment = await (await client.GetAsync($"/api/v1/shipments/{received.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.AtDestinationBranch, afterReceiveShipment!.CurrentStatus);
        Assert.Equal(toBranchId, afterReceiveShipment.CurrentBranchId);

        var closeResponse = await client.PostAsync($"/api/v1/manifests/{manifest.Id}/close", null);
        var closed = await closeResponse.Content.ReadFromJsonAsync<ManifestResponse>();
        Assert.Equal(ManifestStatus.Closed, closed!.Status);

        var missingResponse = await client.GetAsync($"/api/v1/manifests/{manifest.Id}/missing-shipments");
        var missingList = await missingResponse.Content.ReadFromJsonAsync<List<MissingShipmentResponse>>();
        var missingEntry = Assert.Single(missingList!);
        Assert.Equal(missing.Awb, missingEntry.Awb);

        var afterCloseMissingShipment = await (await client.GetAsync($"/api/v1/shipments/{missing.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Sorted, afterCloseMissingShipment!.CurrentStatus);
    }

    [Fact]
    public async Task Scan_ShipmentStillCreated_Returns400WithWrongStateReason()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "wrong-state");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (fromBranchId, toBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, fromBranchId, toBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();

        var response = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment!.Awb));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("MANIFEST_SCAN_REJECTED", body);
        Assert.Contains("WRONG_STATE", body);
    }

    [Fact]
    public async Task ReceiveScan_ShipmentNotOnThisManifest_Returns400WithNotOnManifestReason()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "not-on-manifest");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (fromBranchId, toBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var otherShipment = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();

        // otherShipment was never scanned onto this manifest, so it's still AtOriginBranch — the
        // manifest itself is still Draft, which fails MANIFEST_NOT_RECEIVING before NOT_ON_MANIFEST
        // would ever be reached, so exercise the check with a manifest that IS receiving instead.
        var scannedShipment = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(scannedShipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));

        var response = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(otherShipment.Awb));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("MANIFEST_SCAN_REJECTED", body);
        Assert.Contains("NOT_ON_MANIFEST", body);
    }

    [Fact]
    public async Task Manifests_OrgBCannotReadOrgAsManifest()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "iso-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (fromBranchId, toBranchId, _) = await SeedBranchesAsync(clientA);
        var manifest = await (await clientA.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "iso-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var response = await clientB.GetAsync($"/api/v1/manifests/{manifest!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostManifests_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests**

```bash
docker exec courier-postgres-1 pg_isready -U courier
dotnet test tests/Api.Tests/Api.Tests.csproj --filter ManifestsEndpointsTests -v n
```

Expected: PASS (6 tests).

- [ ] **Step 3: Verify the full solution builds and all tests pass**

```bash
dotnet build Courier.slnx
dotnet test Courier.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`; all tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/Api.Tests/ManifestsEndpointsTests.cs
git commit -m "test: add end-to-end manifest lifecycle, missing-shipment, and isolation tests"
```

---

### Task 13: Cleanup — Retire the Phase 6 Placeholder Endpoints Now That Manifests Replaces Them

**Files:**
- Modify: `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs` (remove `/sort`, `/dispatch`, `/receive-dest` routes)
- Modify: `src/Modules/Shipments/Application/ShipmentService.Transitions.cs` (remove the now-unreferenced single-shipment `DispatchAsync`)
- Modify: `tests/Shipments.Tests/ShipmentServiceTransitionTests.cs` (3 call sites: `DispatchAsync` → `DispatchManyAsync`)
- Modify: `tests/Api.Tests/ShipmentsEndpointsTests.cs` (2 tests: route their sort/dispatch/receive-dest steps through `Manifests` instead)

**Interfaces:**
- Consumes: `Manifests`' full HTTP surface (Task 10-12), proven working end-to-end by Task 12 — this task is safe only because that proof already exists.
- Produces: nothing new — this is pure retirement of now-redundant surface area, matching this plan's Architecture section's stated sequencing constraint.

- [ ] **Step 1: Remove the three retired routes from ShipmentsEndpoints.cs**

In `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs`, delete these three route blocks entirely (they sit between the `/receive-origin` and `/out-for-delivery` routes):

```csharp
        group.MapPost("/{id:guid}/sort", async (Guid id, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.SortAsync(id, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("shipment.sort");

        group.MapPost("/{id:guid}/dispatch", async (Guid id, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.DispatchAsync(id, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("shipment.transfer");

        group.MapPost("/{id:guid}/receive-dest", async (Guid id, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.ReceiveAtDestinationAsync(id, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("shipment.scan");

```

(Leave `/receive-origin` immediately above and `/out-for-delivery` immediately below untouched — only these three blocks go.)

- [ ] **Step 2: Remove the now-unreferenced single-shipment DispatchAsync**

In `src/Modules/Shipments/Application/ShipmentService.Transitions.cs`, delete this method entirely (it sits between `SortAsync` and `ReceiveAtDestinationAsync`):

```csharp
    public async Task<ShipmentResponse?> DispatchAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        await CheckBranchAccessAsync(actingUser, "shipment.transfer", shipment.OriginBranchId, cancellationToken);

        return await TransitionAsync(shipmentId, ShipmentStatus.Sorted, ShipmentStatus.InTransit, ShipmentEventType.Dispatched,
            s => s.CurrentBranchId = null, "shipment.dispatched", actingUser, cancellationToken);
    }

```

`IShipmentTransitionService`/`DispatchManyAsync` (Task 3) already cover every real caller — this method has had none since Task 12 proved the `Manifests` replacement works.

- [ ] **Step 3: Fix the three unit-test call sites in ShipmentServiceTransitionTests.cs**

In `tests/Shipments.Tests/ShipmentServiceTransitionTests.cs`, in `FullHappyPath_CreatedToDelivered_WalksEveryTransitionWithCorrectEventsAndCustody`, replace:

```csharp
        var afterDispatch = await service.DispatchAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.InTransit, afterDispatch!.CurrentStatus);
        Assert.Null(afterDispatch.CurrentBranchId);
```

with:

```csharp
        await service.DispatchManyAsync(new[] { shipmentId }, actingUser);
        var afterDispatch = await dbContext.Set<Shipment>().FirstAsync(s => s.Id == shipmentId);
        Assert.Equal(ShipmentStatus.InTransit, afterDispatch.CurrentStatus);
        Assert.Null(afterDispatch.CurrentBranchId);
```

In `DeliverAsync_WritesRecipientNameAndNotesOntoDeliveredEvent`, replace:

```csharp
        await service.DispatchAsync(shipmentId, actingUser);
```

with:

```csharp
        await service.DispatchManyAsync(new[] { shipmentId }, actingUser);
```

In `DeliverAsync_CallerDeniedBranchAccess_ThrowsShipmentValidationException`, replace the same line the same way:

```csharp
        await service.DispatchAsync(shipmentId, actingUser);
```

with:

```csharp
        await service.DispatchManyAsync(new[] { shipmentId }, actingUser);
```

- [ ] **Step 4: Run Shipments.Tests to verify the fixes work**

```bash
dotnet test tests/Shipments.Tests/Shipments.Tests.csproj
```

Expected: PASS — the solution will not yet build cleanly end-to-end until Step 5 also fixes `Api.Tests` (which still references the now-deleted `/sort`/`/dispatch`/`/receive-dest` routes), but `Shipments.Tests` itself has no dependency on those routes and passes now.

- [ ] **Step 5: Fix the two Api.Tests call sites in ShipmentsEndpointsTests.cs**

In `tests/Api.Tests/ShipmentsEndpointsTests.cs`, add `using Courier.Manifests.Application;` to the top `using` block, alphabetically after `Courier.Identity.Application` and before `Courier.Merchants.Application`.

Add this private helper method to the `ShipmentsEndpointsTests` class (anywhere among the other private helpers, e.g. right after `SeedRateCardAsync`):

```csharp
    // Replaces the retired POST /shipments/{id}/{sort,dispatch,receive-dest} endpoints — walks a
    // shipment already at AtOriginBranch through to AtDestinationBranch via a real single-shipment
    // manifest, mirroring ManifestsEndpointsTests.FullManifestLifecycle_DraftToClosed's flow.
    private async Task SortDispatchAndReceiveViaManifestAsync(HttpClient client, ShipmentResponse shipment, Guid originBranchId, Guid destinationBranchId)
    {
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));
    }
```

In `FullHappyPath_CreatedToDelivered_Succeeds`, replace:

```csharp
        var afterSort = await (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/sort", new { })).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Sorted, afterSort!.CurrentStatus);

        var afterDispatch = await (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/dispatch", new { })).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.InTransit, afterDispatch!.CurrentStatus);

        var afterDest = await (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-dest", new { })).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.AtDestinationBranch, afterDest!.CurrentStatus);
```

with:

```csharp
        await SortDispatchAndReceiveViaManifestAsync(client, shipment, originBranchId, destinationBranchId);
        var afterDest = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.AtDestinationBranch, afterDest!.CurrentStatus);
```

In `Deliver_ConcurrentRequestsForSameShipment_ExactlyOneSucceeds`, replace:

```csharp
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/sort", new { });
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/dispatch", new { });
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-dest", new { });
```

with:

```csharp
        await SortDispatchAndReceiveViaManifestAsync(client, shipment, originBranchId, destinationBranchId);
```

- [ ] **Step 6: Run the full solution build and test suite**

```bash
docker exec courier-postgres-1 pg_isready -U courier
dotnet build Courier.slnx
dotnet test Courier.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`; every test across every project passes, including the two rewritten `Api.Tests` tests and all of `Manifests.Tests`/`Shipments.Tests`.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Shipments tests/Shipments.Tests/ShipmentServiceTransitionTests.cs tests/Api.Tests/ShipmentsEndpointsTests.cs
git commit -m "refactor: retire Phase 6 placeholder sort/dispatch/receive-dest endpoints now that Manifests replaces them"
```

---

## Phase 7 Exit Criteria (docs/ROADMAP.md)

After Task 13, update `docs/ROADMAP.md`'s Phase 7 section with a `*Status (<date>): Complete.*` paragraph following the exact style of the Phase 5/6 entries already in that file — summarizing the `Manifests` module, the five-check scan validation, the missing-shipment detection mechanism, and the three resolved design decisions from this plan's Architecture section — then commit it separately:

```bash
git add docs/ROADMAP.md
git commit -m "docs: mark Phase 7 exit criteria met"
```

