# Phase 5 — Rate Cards, Pricing Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A price can be computed for a given origin-branch/destination-branch/weight/service-type/COD/insurance/merchant combination, and is provably versioned — changing today's rate does not alter a previously computed quote (`docs/ROADMAP.md` Phase 5 exit criteria).

**Architecture:** A new `Pricing` module (`src/Modules/Pricing/Pricing.csproj`) owns `ServiceType, WeightSlab, RateCard, ZoneRate, AdditionalCharge, MerchantRateCard`, following the `Domain/Application/Infrastructure/Presentation` shape already used by `Geography`/`Branches`/`Merchants`. `Pricing` takes project references to `SharedKernel`, `Audit`, `Geography`, `Merchants`, and `Branches` — the widest reference graph of any module so far, because the pricing engine legitimately needs a rating zone from `Geography` (via `Branch.DeliveryZoneId`, new this phase), a merchant identity from `Merchants`, and branch data from `Branches`. Every cross-module read goes through a small Application-layer interface on the owning module (`IGeographyLookupService`, a new `IBranchLookupService`, a new `IMerchantLookupService`) — `Pricing` never touches another module's `DbSet` directly, matching `ARCHITECTURE.md` §2's rule. `Pricing` itself has no project reference to `Identity`; its `RateCard`/`MerchantRateCard` → `Organization` FK and tenant `HasQueryFilter` are wired centrally in `Persistence/AppDbContext.cs`, exactly like `DeliveryZone`/`Merchant` today.

Three deliberate deviations from `docs/DOMAIN-MODEL.md` as currently written (Task 1 updates that doc to match):
1. **No `RateCardRule` entity.** `ZoneRate`/`AdditionalCharge` already carry `RateCardId` directly; the engine queries them directly. `RateCardRule` as documented added indirection with no evaluable logic of its own (YAGNI, CLAUDE.md §34).
2. **No `Pricing.Zone` entity.** `ZoneRate.OriginZoneId`/`DestinationZoneId` FK straight into `Geography.DeliveryZone` (`Id, OrganizationId, Name, CoveredAreas[]` — already exactly the shape DOMAIN-MODEL.md described for `Pricing.Zone`).
3. **`Branch` gains a new `DeliveryZoneId` (nullable FK into `Geography.DeliveryZone`).** Ops-assigned, not geography-derived — `Branch` deliberately has no `Area`-level FK (a `Ward` can hold multiple `Area`s with no way to disambiguate which one a given branch sits in), so resolving a rating zone from raw province/district/ward data is not possible; ops staff assign the zone explicitly, either at branch creation or via a new narrow `PUT /api/v1/branches/{id}/delivery-zone` endpoint (using the `branch.update` permission that has existed in the catalog since Phase 1 with no endpoint until now — still no general branch-update endpoint, this is a single-field addition only).
4. **`WeightSlab.MaxWeightKg` is nullable** (open-ended top slab, e.g. "10kg+"), not the plain non-nullable field DOMAIN-MODEL.md shows today.

Rate versioning: at most one `RateCard` per organization has `IsActive = true` (enforced at the application-service level — query-then-update in the same `SaveChangesAsync`, same style as `BranchService.CreateBranchAsync`'s `codeTaken` check; no filtered/partial unique index exists anywhere in this codebase yet, and this phase doesn't introduce the first one). Creating a new `RateCard` (`POST /api/v1/rate-cards`) supersedes the org's current default: the new row gets `Version = previous.Version + 1` (or `1` if none exists), `IsActive = true`; the previous row flips to `IsActive = false, EffectiveTo = new.EffectiveFrom`. The previous row's `ZoneRate`/`AdditionalCharge` children are never mutated again — `ZoneRate`/`AdditionalCharge` can only be POSTed onto a `RateCard` that is still `IsActive = true`; write attempts against a superseded card are rejected. No `PUT`/`PATCH`/`DELETE` exists for `RateCard`/`ZoneRate`/`AdditionalCharge` at all — creation-only, which is what makes the immutability guarantee provable by inspection rather than by convention.

No quote is persisted this phase (stateless calculation; Phase 6 will snapshot the resolved charge fields onto `Shipment`). This codebase has no `Money` value object despite `ARCHITECTURE.md` §2's aspirational mention — every money-shaped column here uses plain `decimal` with an explicit `HasPrecision(18, 2)` (weight columns use `HasPrecision(10, 3)`), set as a precedent since this is the first module with real currency amounts (Phase 4's backlog flagged `MerchantAddress.Latitude/Longitude` for the exact omission this plan avoids repeating). FluentValidation is listed in CLAUDE.md's tech stack but is not actually used anywhere in this codebase — every module validates manually via a per-module `XValidationException` thrown in the Application service and mapped to `400` in Presentation; this plan follows that established convention instead of introducing FluentValidation.

**Scope deliberately excluded from this phase:** no quote persistence / `PriceQuote` entity; no "clone previous `RateCard` version" convenience endpoint (a new version starts empty, populated via the `zone-rates`/`additional-charges` POST endpoints); no org-configurable `WeightSlab` (global/shared only, seeded once like `ServiceType`); no `RateCardRule` entity at all; no `AsOf`/historical-timestamp quote resolution (deferred to Phase 6, when repricing/displaying historical shipments needs it — not needed to prove versioning this phase, since immutability of superseded rows is the proof).

**Tech Stack:** Same as Phases 1-4 — EF Core 10 + Npgsql, xUnit + `Microsoft.EntityFrameworkCore.InMemory` for unit tests, `Microsoft.AspNetCore.Mvc.Testing` for API-level integration tests against the real Postgres container from `docker-compose.yml` (the shared `courier-postgres-1` container on port 5434 — reuse it, do not start a second one).

**Spec:** `docs/ROADMAP.md` (Phase 5), `docs/DOMAIN-MODEL.md` (Pricing section, updated by Task 1), `docs/ARCHITECTURE.md` §2-3, `CLAUDE.md` §16 — this project folds the design directly into this plan doc rather than a separate `docs/superpowers/specs/` file (established convention across Phases 0-4); the full design was agreed section-by-section with the user in this session's brainstorming conversation before this plan was written.

## Global Constraints

- Every tenant-owned entity gets a non-nullable `OrganizationId`; every query is scoped server-side (`ARCHITECTURE.md` §3) — never trust an id from the client without checking it resolves within the caller's tenant.
- A module reaches another module's data only through that module's Application-layer interface — never a direct `_dbContext.Set<OtherModuleEntity>()` call into a module you don't own.
- No `PUT`/`PATCH`/`DELETE` on `RateCard`, `ZoneRate`, or `AdditionalCharge` — ever. Creation-only.
- Every money column: `HasPrecision(18, 2)`. Every weight column: `HasPrecision(10, 3)`.
- Manual exception-based validation (`PricingValidationException`, `BranchValidationException`), not FluentValidation.
- Reuse the existing `pricing.read`/`pricing.manage` permissions from `PermissionCatalog.cs` — no catalog changes.

---

### Task 1: Update DOMAIN-MODEL.md and Scaffold the Pricing Module

**Files:**
- Modify: `docs/DOMAIN-MODEL.md`
- Create: `src/Modules/Pricing/Pricing.csproj`
- Create: `tests/Pricing.Tests/Pricing.Tests.csproj`
- Modify: `Courier.slnx`
- Modify: `src/Persistence/Persistence.csproj` (add reference to Pricing)
- Modify: `src/Api/Api.csproj` (add reference to Pricing)

**Interfaces:**
- Consumes: nothing (first task).
- Produces: an empty-but-building `Pricing` project wired into the solution with the reference graph `SharedKernel, Audit, Geography, Merchants, Branches ← Pricing`, `..., Pricing ← Persistence`, `..., Pricing ← Api`. No cycles. `Api.Tests` gets transitive access through `Api.csproj`, so it needs no direct reference. Later tasks add real code.

- [ ] **Step 1: Update the Pricing section of DOMAIN-MODEL.md**

Replace lines 80-99 of `docs/DOMAIN-MODEL.md` (the `## Pricing` section) with:

```text
## Pricing — `OrganizationId` present (except shared `ServiceType`/`WeightSlab` templates)

```text
ServiceType { Id, Code (Standard|Express|SameDay|Heavy|Special), Name }
WeightSlab { Id, MinWeightKg, MaxWeightKg (nullable — open-ended top slab, e.g. "10kg+") }
RateCard { Id, OrganizationId, Name, EffectiveFrom, EffectiveTo (nullable),
           Version, IsActive }
ZoneRate { Id, RateCardId, OriginZoneId, DestinationZoneId (both FK straight into
           Geography.DeliveryZone — Pricing has no separate Zone entity; DeliveryZone
           already has the identical OrganizationId+Name+CoveredAreas shape),
           ServiceTypeId, WeightSlabId, BaseRate }
AdditionalCharge { Id, RateCardId, Type (Insurance|COD|Fuel|Other), CalculationType
                    (Flat|Percentage), Value }
MerchantRateCard { Id, MerchantId, OrganizationId, RateCardId,
                    OverrideDiscountPercent (nullable) }
```

`RateCard` is immutable once created — a rate change is a new `RateCard` version with a new
`EffectiveFrom` (`Version` incremented, previous row's `IsActive` flips to `false` and
`EffectiveTo` is set), never an in-place edit (CLAUDE.md §16). `ZoneRate`/`AdditionalCharge` rows
can only be added while their parent `RateCard` is still the org's active version; a superseded
`RateCard`'s children are frozen forever. No `RateCardRule` entity exists — the pricing engine
queries `ZoneRate`/`AdditionalCharge` directly by `RateCardId`, since both already carry it and no
extra evaluable rule logic is needed beyond that direct lookup (ROADMAP.md Phase 5).

`Branch` (see the Branches section above) gains a `DeliveryZoneId` (nullable FK into
`Geography.DeliveryZone`) as part of Phase 5 — ops-assigned, used to resolve a shipment's
origin/destination branch to a rating zone for `ZoneRate` lookup.
```

- [ ] **Step 2: Add `Branch.DeliveryZoneId` to the Branches section**

In `docs/DOMAIN-MODEL.md`, in the `## Branches` section, change:

```text
Branch { Id, OrganizationId, Code, Name, Type (HeadOffice|Hub|Branch|PickupPoint|
         Warehouse), ProvinceId, DistrictId, LocalLevelId, WardNumber,       [C#2]
         AddressLine, Latitude, Longitude, Phone, OperatingHours,
         ParentBranchId (nullable, self-FK), IsActive }
```

to:

```text
Branch { Id, OrganizationId, Code, Name, Type (HeadOffice|Hub|Branch|PickupPoint|
         Warehouse), ProvinceId, DistrictId, LocalLevelId, WardNumber,       [C#2]
         AddressLine, Latitude, Longitude, Phone, OperatingHours,
         ParentBranchId (nullable, self-FK), DeliveryZoneId (nullable FK into
         Geography.DeliveryZone, ops-assigned — Phase 5), IsActive }
```

- [ ] **Step 3: Update the Relationship Summary**

In `docs/DOMAIN-MODEL.md`'s `## Relationship Summary` block, change:

```text
Organization 1─* RateCard 1─* ZoneRate
```

to:

```text
Organization 1─* RateCard 1─* ZoneRate
RateCard 1─* AdditionalCharge
Geography.DeliveryZone 1─* ZoneRate (as OriginZone, and separately as DestinationZone)
Branch *─1 Geography.DeliveryZone
Merchant 1─(0..1)─ MerchantRateCard *─1 RateCard
```

- [ ] **Step 4: Create the two projects**

```bash
dotnet new classlib -n Pricing -o src/Modules/Pricing
dotnet new xunit -n Pricing.Tests -o tests/Pricing.Tests
dotnet sln Courier.slnx add src/Modules/Pricing/Pricing.csproj tests/Pricing.Tests/Pricing.Tests.csproj
```

Delete the template `src/Modules/Pricing/Class1.cs` and `tests/Pricing.Tests/UnitTest1.cs` — both projects start empty.

- [ ] **Step 5: Add the ASP.NET Core framework reference and EF packages**

```bash
dotnet add src/Modules/Pricing/Pricing.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Modules/Pricing/Pricing.csproj package Microsoft.EntityFrameworkCore.Relational
dotnet add tests/Pricing.Tests/Pricing.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory
```

In `src/Modules/Pricing/Pricing.csproj`, add inside a sibling `<ItemGroup>`:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

- [ ] **Step 6: Wire project references**

```bash
dotnet add src/Modules/Pricing/Pricing.csproj reference src/SharedKernel/SharedKernel.csproj
dotnet add src/Modules/Pricing/Pricing.csproj reference src/Modules/Audit/Audit.csproj
dotnet add src/Modules/Pricing/Pricing.csproj reference src/Modules/Geography/Geography.csproj
dotnet add src/Modules/Pricing/Pricing.csproj reference src/Modules/Merchants/Merchants.csproj
dotnet add src/Modules/Pricing/Pricing.csproj reference src/Modules/Branches/Branches.csproj
dotnet add src/Persistence/Persistence.csproj reference src/Modules/Pricing/Pricing.csproj
dotnet add src/Api/Api.csproj reference src/Modules/Pricing/Pricing.csproj
dotnet add tests/Pricing.Tests/Pricing.Tests.csproj reference src/Modules/Pricing/Pricing.csproj
```

- [ ] **Step 7: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (pre-existing NU1510/CS8602 warnings from earlier phases are fine; no new ones).

- [ ] **Step 8: Commit**

```bash
git add docs/DOMAIN-MODEL.md Courier.slnx src/Modules/Pricing tests/Pricing.Tests src/Persistence/Persistence.csproj src/Api/Api.csproj
git commit -m "docs: update domain model for Phase 5; scaffold Pricing module project"
```

---

### Task 2: Geography — Add DeliveryZoneExistsAsync

**Files:**
- Modify: `src/Modules/Geography/Application/IGeographyLookupService.cs`
- Modify: `src/Modules/Geography/Application/GeographyQueryService.cs`
- Test: `tests/Geography.Tests/GeographyQueryServiceTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `IGeographyLookupService.DeliveryZoneExistsAsync(Guid zoneId, CancellationToken)` — Task 4 (`BranchService`) and Task 7 (`RateCardService`) consume this. Tenant scoping is implicit via `DeliveryZone`'s existing `HasQueryFilter` on the shared `DbContext` (same pattern as every other cross-module lookup in this codebase) — no `organizationId` parameter needed.

- [ ] **Step 1: Add the failing test**

Append to `tests/Geography.Tests/GeographyQueryServiceTests.cs` (open the file first to match its existing `class`/`using` structure — add this as a new `[Fact]` method inside the existing test class, and add `using Courier.SharedKernel;` plus a `file class FixedTenantProvider : ICurrentTenantProvider` at the bottom if the file doesn't already define one, mirroring `DeliveryZoneServiceTests.cs`'s fixture):

```csharp
[Fact]
public async Task DeliveryZoneExistsAsync_ZoneInCurrentOrg_ReturnsTrue()
{
    await using var dbContext = CreateSeededContext();
    var organizationId = Guid.NewGuid();
    var zone = new DeliveryZone { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Zone A" };
    dbContext.Set<DeliveryZone>().Add(zone);
    await dbContext.SaveChangesAsync();

    var service = new GeographyQueryService(dbContext);

    Assert.True(await service.DeliveryZoneExistsAsync(zone.Id));
}

[Fact]
public async Task DeliveryZoneExistsAsync_UnknownZoneId_ReturnsFalse()
{
    await using var dbContext = CreateSeededContext();
    var service = new GeographyQueryService(dbContext);

    Assert.False(await service.DeliveryZoneExistsAsync(Guid.NewGuid()));
}
```

If `GeographyQueryServiceTests.cs`'s `CreateSeededContext()`/`TestDbContext` doesn't already apply `DeliveryZoneConfiguration` in its `OnModelCreating`, add `modelBuilder.ApplyConfiguration(new DeliveryZoneConfiguration());` to it — check `DeliveryZoneServiceTests.cs`'s `TestDbContext` for the exact line if unsure, they should match. Note: the in-memory provider does not evaluate `HasQueryFilter` against a real `ICurrentTenantProvider` unless the `TestDbContext` also wires one in — since `GeographyQueryServiceTests.cs`'s existing `TestDbContext` likely doesn't take a tenant provider (its other methods like `ListProvincesAsync` query filter-free tables), check whether `DeliveryZone`'s `HasQueryFilter` from `AppDbContext` is even present on this lighter `TestDbContext` — it is NOT (that filter is wired centrally in `AppDbContext.OnModelCreating`, not in `DeliveryZoneConfiguration`), so these two tests exercise `DeliveryZoneExistsAsync`'s query logic only, not tenant filtering (tenant filtering is proven separately at the `Api.Tests` level in Task 12).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Geography.Tests/Geography.Tests.csproj --filter DeliveryZoneExistsAsync -v n`
Expected: FAIL — `GeographyQueryService` has no `DeliveryZoneExistsAsync` method (compile error).

- [ ] **Step 3: Add the method to the interface**

In `src/Modules/Geography/Application/IGeographyLookupService.cs`, add inside the interface:

```csharp
// True iff a DeliveryZone with this id exists — scoped to the current tenant automatically via
// AppDbContext's query filter on DeliveryZone (no explicit organizationId parameter needed).
Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement it on GeographyQueryService**

In `src/Modules/Geography/Application/GeographyQueryService.cs`, add:

```csharp
public async Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default) =>
    await _dbContext.Set<DeliveryZone>().AnyAsync(z => z.Id == zoneId, cancellationToken);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Geography.Tests/Geography.Tests.csproj --filter DeliveryZoneExistsAsync -v n`
Expected: PASS (2 tests).

- [ ] **Step 6: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Geography tests/Geography.Tests
git commit -m "feat: add DeliveryZoneExistsAsync to IGeographyLookupService"
```

---

### Task 3: Merchants — Add IMerchantLookupService

**Files:**
- Create: `src/Modules/Merchants/Application/IMerchantLookupService.cs`
- Create: `src/Modules/Merchants/Application/MerchantLookupService.cs`
- Test: `tests/Merchants.Tests/MerchantLookupServiceTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `IMerchantLookupService.ExistsAsync(Guid merchantId, CancellationToken)` — Task 7 (`RateCardService`) and Task 8 (`PricingService`) consume this. Tenant scoping is implicit via `Merchant`'s existing `HasQueryFilter`.

- [ ] **Step 1: Write the failing test**

`tests/Merchants.Tests/MerchantLookupServiceTests.cs`:

```csharp
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Merchants.Tests;

public class MerchantLookupServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task ExistsAsync_KnownMerchant_ReturnsTrue()
    {
        await using var dbContext = CreateContext();
        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            LegalName = "Acme Pvt. Ltd.",
            DisplayName = "Acme",
            ContactPhone = "01-4444444",
            ContactEmail = "acme@example.com",
            KycStatus = KycStatus.Pending,
            IsActive = true
        };
        dbContext.Set<Merchant>().Add(merchant);
        await dbContext.SaveChangesAsync();

        var service = new MerchantLookupService(dbContext);

        Assert.True(await service.ExistsAsync(merchant.Id));
    }

    [Fact]
    public async Task ExistsAsync_UnknownMerchant_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        var service = new MerchantLookupService(dbContext);

        Assert.False(await service.ExistsAsync(Guid.NewGuid()));
    }
}
```

Check `Merchant.cs`'s exact required properties before writing this test (open `src/Modules/Merchants/Domain/Merchant.cs`) — match its actual constructor/required-member shape rather than guessing.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter MerchantLookupServiceTests -v n`
Expected: FAIL — `MerchantLookupService` doesn't exist.

- [ ] **Step 3: Write the interface and implementation**

`src/Modules/Merchants/Application/IMerchantLookupService.cs`:

```csharp
namespace Courier.Merchants.Application;

public interface IMerchantLookupService
{
    // True iff a Merchant with this id exists — scoped to the current tenant automatically via
    // AppDbContext's query filter on Merchant (no explicit organizationId parameter needed).
    Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default);
}
```

`src/Modules/Merchants/Application/MerchantLookupService.cs`:

```csharp
using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class MerchantLookupService : IMerchantLookupService
{
    private readonly DbContext _dbContext;

    public MerchantLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Merchant>().AnyAsync(m => m.Id == merchantId, cancellationToken);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter MerchantLookupServiceTests -v n`
Expected: PASS (2 tests).

- [ ] **Step 5: Register in DI**

In `src/Api/Program.cs`, after the existing `builder.Services.AddScoped<MerchantKycService>();` line, add:

```csharp
builder.Services.AddScoped<MerchantLookupService>();
builder.Services.AddScoped<IMerchantLookupService>(sp => sp.GetRequiredService<MerchantLookupService>());
```

- [ ] **Step 6: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Merchants tests/Merchants.Tests src/Api/Program.cs
git commit -m "feat: add IMerchantLookupService"
```

---

### Task 4: Branches — Add DeliveryZoneId, IBranchLookupService, and the Zone-Assignment Endpoint

**Files:**
- Modify: `src/Modules/Branches/Domain/Branch.cs`
- Create: `src/Modules/Branches/Application/IBranchLookupService.cs`
- Modify: `src/Modules/Branches/Application/BranchDtos.cs`
- Modify: `src/Modules/Branches/Application/BranchService.cs`
- Modify: `src/Modules/Branches/Infrastructure/BranchConfiguration.cs`
- Modify: `src/Modules/Branches/Presentation/BranchesEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Branches.Tests/` (extend existing `BranchService` tests — open the existing test file first to match its structure)
- Test: `tests/Api.Tests/BranchesEndpointsTests.cs`

**Interfaces:**
- Consumes: `IGeographyLookupService.DeliveryZoneExistsAsync` (Task 2).
- Produces: `Branch.DeliveryZoneId` (`Guid?`), `IBranchLookupService.GetZoneInfoAsync(Guid branchId, CancellationToken) -> BranchZoneInfo?` where `BranchZoneInfo(Guid BranchId, Guid? DeliveryZoneId)` — Task 8's `PricingService` consumes this. `BranchService.AssignDeliveryZoneAsync`. `PUT /api/v1/branches/{id}/delivery-zone`.

- [ ] **Step 1: Add DeliveryZoneId to the Branch entity**

In `src/Modules/Branches/Domain/Branch.cs`, add after `ParentBranchId`:

```csharp
    // Ops-assigned rating zone for the pricing engine (ROADMAP.md Phase 5) — not derived from
    // ProvinceId/DistrictId/LocalLevelId/WardNumber above, since Branch has no Area-level FK to
    // match against DeliveryZone.CoveredAreas (a Ward can hold multiple Areas with no way to tell
    // which one a given branch is in).
    public Guid? DeliveryZoneId { get; set; }
```

- [ ] **Step 2: Add the IBranchLookupService interface**

`src/Modules/Branches/Application/IBranchLookupService.cs`:

```csharp
namespace Courier.Branches.Application;

public record BranchZoneInfo(Guid BranchId, Guid? DeliveryZoneId);

public interface IBranchLookupService
{
    // Null iff no branch with this id exists (or it's outside the caller's tenant, via the
    // existing query filter). A non-null result with DeliveryZoneId == null means the branch
    // exists but has no rating zone assigned yet.
    Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Extend BranchDtos.cs**

In `src/Modules/Branches/Application/BranchDtos.cs`, replace the full file contents:

```csharp
using Courier.Branches.Domain;

namespace Courier.Branches.Application;

public record CreateBranchRequest(
    string Code,
    string Name,
    BranchType Type,
    Guid ProvinceId,
    Guid DistrictId,
    Guid LocalLevelId,
    int WardNumber,
    string AddressLine,
    string? Phone,
    string? OperatingHours,
    decimal? Latitude,
    decimal? Longitude,
    Guid? ParentBranchId,
    Guid? DeliveryZoneId = null);

public record BranchResponse(
    Guid Id,
    string Code,
    string Name,
    BranchType Type,
    Guid ProvinceId,
    Guid DistrictId,
    Guid LocalLevelId,
    int WardNumber,
    string AddressLine,
    string? Phone,
    string? OperatingHours,
    decimal? Latitude,
    decimal? Longitude,
    Guid? ParentBranchId,
    Guid? DeliveryZoneId,
    bool IsActive);

public record AssignDeliveryZoneRequest(Guid DeliveryZoneId);

public class BranchValidationException : Exception
{
    public BranchValidationException(string message) : base(message) { }
}
```

- [ ] **Step 4: Update BranchService**

In `src/Modules/Branches/Application/BranchService.cs`:

Add `: IBranchLookupService` to the class declaration:

```csharp
public class BranchService : IBranchLookupService
```

In `CreateBranchAsync`, after the existing `ParentBranchId` validation block (after the `if (request.ParentBranchId is Guid parentId) { ... }` block) and before constructing `branch`, add:

```csharp
        if (request.DeliveryZoneId is Guid deliveryZoneId)
        {
            var zoneExists = await _geographyLookupService.DeliveryZoneExistsAsync(deliveryZoneId, cancellationToken);
            if (!zoneExists)
            {
                throw new BranchValidationException("Delivery zone was not found in this organization.");
            }
        }
```

In the `branch` object construction, add `DeliveryZoneId = request.DeliveryZoneId,` alongside the other assigned fields.

Update `ToResponse` to include the new field:

```csharp
    private static BranchResponse ToResponse(Branch branch) => new(
        branch.Id, branch.Code, branch.Name, branch.Type,
        branch.ProvinceId, branch.DistrictId, branch.LocalLevelId, branch.WardNumber,
        branch.AddressLine, branch.Phone, branch.OperatingHours, branch.Latitude, branch.Longitude,
        branch.ParentBranchId, branch.DeliveryZoneId, branch.IsActive);
```

Add two new methods (place after `ListBranchesAsync`, before the `private static ToResponse`):

```csharp
    public async Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var branch = await _dbContext.Set<Branch>().FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        return branch is null ? null : new BranchZoneInfo(branch.Id, branch.DeliveryZoneId);
    }

    public async Task<BranchResponse?> AssignDeliveryZoneAsync(Guid branchId, AssignDeliveryZoneRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new BranchValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new BranchValidationException("Acting user could not be resolved.");

        var branch = await _dbContext.Set<Branch>().FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        if (branch is null)
        {
            return null;
        }

        var zoneExists = await _geographyLookupService.DeliveryZoneExistsAsync(request.DeliveryZoneId, cancellationToken);
        if (!zoneExists)
        {
            throw new BranchValidationException("Delivery zone was not found in this organization.");
        }

        var before = branch.DeliveryZoneId?.ToString() ?? "(none)";
        branch.DeliveryZoneId = request.DeliveryZoneId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "branch.delivery_zone_assigned", "Branch", branch.Id.ToString(), before, request.DeliveryZoneId.ToString()), cancellationToken);

        return ToResponse(branch);
    }
```

- [ ] **Step 5: Update BranchConfiguration**

In `src/Modules/Branches/Infrastructure/BranchConfiguration.cs`, add after the existing `builder.HasIndex(b => b.LocalLevelId);` line:

```csharp
        builder.HasIndex(b => b.DeliveryZoneId);
```

And after the existing `LocalLevel` FK block (after the `builder.HasOne<LocalLevel>()...` statement), add:

```csharp
        builder.HasOne<DeliveryZone>()
            .WithMany()
            .HasForeignKey(b => b.DeliveryZoneId)
            .OnDelete(DeleteBehavior.Restrict);
```

Add `using Courier.Geography.Domain;` if not already present (it is, from the existing `Province`/`District`/`LocalLevel` usings) — `DeliveryZone` lives in the same namespace.

- [ ] **Step 6: Add the endpoint**

In `src/Modules/Branches/Presentation/BranchesEndpoints.cs`, add after the existing `group.MapGet("/{id:guid}", ...)` block, before the closing braces:

```csharp
        group.MapPut("/{id:guid}/delivery-zone", async (Guid id, AssignDeliveryZoneRequest request, BranchService branchService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await branchService.AssignDeliveryZoneAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (BranchValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BRANCH_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireBranchScopedPermission("branch.update");
```

- [ ] **Step 7: Register IBranchLookupService in DI**

In `src/Api/Program.cs`, after the existing `builder.Services.AddScoped<BranchService>();` line, add:

```csharp
builder.Services.AddScoped<IBranchLookupService>(sp => sp.GetRequiredService<BranchService>());
```

- [ ] **Step 8: Write unit tests for the new BranchService behavior**

Open `tests/Branches.Tests/` and find the existing `BranchService` test file (its `TestDbContext` already applies `BranchConfiguration` and the `Geography` configurations it needs — check it includes `DeliveryZoneConfiguration` too; if not, add `modelBuilder.ApplyConfiguration(new DeliveryZoneConfiguration());` to its `OnModelCreating`, mirroring the existing province/district/local-level lines). Add these `[Fact]`s to that file, using its existing fixture helpers (a fake `IGeographyLookupService` — check whether the existing file already has one; if it's the real `GeographyQueryService` against seeded data instead, follow that same pattern rather than introducing a new fake):

```csharp
[Fact]
public async Task GetZoneInfoAsync_KnownBranch_ReturnsDeliveryZoneId()
{
    await using var dbContext = CreateSeededContext();
    var zoneId = Guid.NewGuid();
    var branch = new Branch
    {
        Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Code = "KTM-01", Name = "Kathmandu Hub",
        Type = BranchType.Hub, ProvinceId = KathmanduProvinceId, DistrictId = KathmanduDistrictId,
        LocalLevelId = KathmanduLocalLevelId, WardNumber = 1, AddressLine = "New Road",
        DeliveryZoneId = zoneId, IsActive = true
    };
    dbContext.Set<Branch>().Add(branch);
    await dbContext.SaveChangesAsync();

    var service = new BranchService(dbContext, new FixedTenantProvider(branch.OrganizationId), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new GeographyQueryService(dbContext));

    var result = await service.GetZoneInfoAsync(branch.Id);

    Assert.NotNull(result);
    Assert.Equal(zoneId, result!.DeliveryZoneId);
}

[Fact]
public async Task GetZoneInfoAsync_UnknownBranch_ReturnsNull()
{
    await using var dbContext = CreateSeededContext();
    var service = new BranchService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new GeographyQueryService(dbContext));

    Assert.Null(await service.GetZoneInfoAsync(Guid.NewGuid()));
}
```

Match the exact names of this file's existing fixtures (`CreateSeededContext`, tenant provider fake, audit logger fake, branch-authorization-checker fake, geography province/district/local-level id constants) — read the file first and adjust the field names above to whatever it actually calls them; do not introduce duplicate fixture types if equivalents already exist in that file.

- [ ] **Step 9: Run Branches.Tests**

Run: `dotnet test tests/Branches.Tests/Branches.Tests.csproj -v n`
Expected: PASS, including the two new tests.

- [ ] **Step 10: Write an API-level test for the zone-assignment endpoint**

Open `tests/Api.Tests/BranchesEndpointsTests.cs` and add (matching its existing `RegisterOrgAsync`/request-building helpers — read the file first and adjust names to match):

```csharp
[Fact]
public async Task AssignDeliveryZone_UnknownZoneId_Returns400()
{
    var client = _factory.CreateClient();
    var org = await RegisterOrgAsync(client, "zone-bad");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

    var createResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest("KTM-ZONE-BAD"));
    var branch = await createResponse.Content.ReadFromJsonAsync<BranchResponse>();

    var response = await client.PutAsJsonAsync($"/api/v1/branches/{branch!.Id}/delivery-zone", new AssignDeliveryZoneRequest(Guid.NewGuid()));

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task AssignDeliveryZone_ValidZone_UpdatesBranch()
{
    var client = _factory.CreateClient();
    var org = await RegisterOrgAsync(client, "zone-ok");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

    var createResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest("KTM-ZONE-OK"));
    var branch = await createResponse.Content.ReadFromJsonAsync<BranchResponse>();

    var zoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest("Zone A", KathmanduWard1AreaIds()));
    var zone = await zoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();

    var response = await client.PutAsJsonAsync($"/api/v1/branches/{branch!.Id}/delivery-zone", new AssignDeliveryZoneRequest(zone!.Id));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var updated = await response.Content.ReadFromJsonAsync<BranchResponse>();
    Assert.Equal(zone.Id, updated!.DeliveryZoneId);
}
```

`KathmanduWard1AreaIds()` and `ValidBranchRequest(code)` should already exist as helpers in this file or `DeliveryZonesEndpointsTests.cs`/`MerchantsEndpointsTests.cs` (the latter has a `KathmanduWard1Areas`-shaped helper per Task 2's earlier reading) — reuse them; add a local equivalent only if genuinely missing, using `GeographyIds` the same way those files do.

- [ ] **Step 11: Run Api.Tests**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter BranchesEndpointsTests -v n`
Expected: PASS, including the two new tests.

- [ ] **Step 12: Ensure Postgres is running, then generate and apply the migration**

```bash
docker exec courier-postgres-1 pg_isready -U courier
```

If not running (this project reuses the shared container — do not `docker compose up` a second one):

```bash
docker compose up -d postgres
```

Then:

```bash
dotnet ef migrations add AddBranchDeliveryZone --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj
dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj --connection "Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password"
```

Expected: a new migration file appears under `src/Persistence/Migrations/`; `database update` reports it applied `AddBranchDeliveryZone` with no errors.

- [ ] **Step 13: Verify the solution builds and all tests pass**

```bash
dotnet build Courier.slnx
dotnet test Courier.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`; all tests pass.

- [ ] **Step 14: Commit**

```bash
git add src/Modules/Branches tests/Branches.Tests tests/Api.Tests/BranchesEndpointsTests.cs src/Api/Program.cs src/Persistence
git commit -m "feat: add Branch.DeliveryZoneId, IBranchLookupService, and zone-assignment endpoint"
```

---

### Task 5: Pricing Domain — Entities and Enums

**Files:**
- Create: `src/Modules/Pricing/Domain/ServiceTypeCode.cs`
- Create: `src/Modules/Pricing/Domain/ServiceType.cs`
- Create: `src/Modules/Pricing/Domain/WeightSlab.cs`
- Create: `src/Modules/Pricing/Domain/RateCard.cs`
- Create: `src/Modules/Pricing/Domain/ZoneRate.cs`
- Create: `src/Modules/Pricing/Domain/AdditionalChargeType.cs`
- Create: `src/Modules/Pricing/Domain/ChargeCalculationType.cs`
- Create: `src/Modules/Pricing/Domain/AdditionalCharge.cs`
- Create: `src/Modules/Pricing/Domain/MerchantRateCard.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: all six Pricing entity types and three enums — every later task in this module consumes these exact shapes.

- [ ] **Step 1: Write the enums**

`src/Modules/Pricing/Domain/ServiceTypeCode.cs`:

```csharp
namespace Courier.Pricing.Domain;

public enum ServiceTypeCode
{
    Standard,
    Express,
    SameDay,
    Heavy,
    Special
}
```

`src/Modules/Pricing/Domain/AdditionalChargeType.cs`:

```csharp
namespace Courier.Pricing.Domain;

public enum AdditionalChargeType
{
    Insurance,
    COD,
    Fuel,
    Other
}
```

`src/Modules/Pricing/Domain/ChargeCalculationType.cs`:

```csharp
namespace Courier.Pricing.Domain;

public enum ChargeCalculationType
{
    Flat,
    Percentage
}
```

- [ ] **Step 2: Write the entities**

`src/Modules/Pricing/Domain/ServiceType.cs`:

```csharp
namespace Courier.Pricing.Domain;

// Shared/global reference data (like Geography's Province/District) — no OrganizationId, seeded
// once, no create/update endpoint this phase.
public class ServiceType
{
    public Guid Id { get; set; }
    public ServiceTypeCode Code { get; set; }
    public required string Name { get; set; }
}
```

`src/Modules/Pricing/Domain/WeightSlab.cs`:

```csharp
namespace Courier.Pricing.Domain;

// Shared/global reference data. MaxWeightKg is nullable to express an open-ended top slab
// (e.g. "10kg+"). A weight w belongs to a slab iff MinWeightKg <= w AND (MaxWeightKg is null OR
// w < MaxWeightKg).
public class WeightSlab
{
    public Guid Id { get; set; }
    public decimal MinWeightKg { get; set; }
    public decimal? MaxWeightKg { get; set; }
}
```

`src/Modules/Pricing/Domain/RateCard.cs`:

```csharp
namespace Courier.Pricing.Domain;

// Immutable once created (CLAUDE.md §16): a rate change creates a new RateCard row with
// Version = previous.Version + 1 and flips the previous row's IsActive to false. At most one
// RateCard per OrganizationId has IsActive == true at a time (enforced in RateCardService, not a
// DB constraint — see this plan's Architecture section).
public class RateCard
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public int Version { get; set; }
    public bool IsActive { get; set; }
}
```

`src/Modules/Pricing/Domain/ZoneRate.cs`:

```csharp
namespace Courier.Pricing.Domain;

// OriginZoneId/DestinationZoneId FK into Geography.DeliveryZone directly — Pricing has no
// separate Zone entity (see this plan's Architecture section, deviation 2). No OrganizationId of
// its own: tenant isolation flows transitively through RateCardId -> RateCard.OrganizationId, so
// every query here must be scoped through a RateCard the caller's tenant actually owns.
public class ZoneRate
{
    public Guid Id { get; set; }
    public Guid RateCardId { get; set; }
    public Guid OriginZoneId { get; set; }
    public Guid DestinationZoneId { get; set; }
    public Guid ServiceTypeId { get; set; }
    public Guid WeightSlabId { get; set; }
    public decimal BaseRate { get; set; }
}
```

`src/Modules/Pricing/Domain/AdditionalCharge.cs`:

```csharp
namespace Courier.Pricing.Domain;

// Same no-own-OrganizationId reasoning as ZoneRate. Type drives whether the pricing engine
// applies this charge to a given quote (Fuel/Other always; Insurance only if the request opts
// in; COD only if CodAmount > 0) — see Pricing.Application.PricingService.
public class AdditionalCharge
{
    public Guid Id { get; set; }
    public Guid RateCardId { get; set; }
    public AdditionalChargeType Type { get; set; }
    public ChargeCalculationType CalculationType { get; set; }
    public decimal Value { get; set; }
}
```

`src/Modules/Pricing/Domain/MerchantRateCard.cs`:

```csharp
namespace Courier.Pricing.Domain;

// At most one row per MerchantId (enforced by a unique index — see MerchantRateCardConfiguration).
// A merchant with no row here uses their org's current default RateCard with no discount.
public class MerchantRateCard
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid RateCardId { get; set; }
    public decimal? OverrideDiscountPercent { get; set; }
}
```

- [ ] **Step 3: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Pricing/Domain
git commit -m "feat: add Pricing domain entities"
```

---

### Task 6: Pricing Infrastructure — EF Configurations and Seed Data

**Files:**
- Create: `src/Modules/Pricing/Infrastructure/PricingIds.cs`
- Create: `src/Modules/Pricing/Infrastructure/PricingSeedData.cs`
- Create: `src/Modules/Pricing/Infrastructure/ServiceTypeConfiguration.cs`
- Create: `src/Modules/Pricing/Infrastructure/WeightSlabConfiguration.cs`
- Create: `src/Modules/Pricing/Infrastructure/RateCardConfiguration.cs`
- Create: `src/Modules/Pricing/Infrastructure/ZoneRateConfiguration.cs`
- Create: `src/Modules/Pricing/Infrastructure/AdditionalChargeConfiguration.cs`
- Create: `src/Modules/Pricing/Infrastructure/MerchantRateCardConfiguration.cs`
- Test: `tests/Pricing.Tests/PricingSeedDataTests.cs`

**Interfaces:**
- Consumes: `Pricing.Domain` entities (Task 5), `Geography.Domain.DeliveryZone` (Task 1's reference), `Merchants.Domain.Merchant` (Task 1's reference).
- Produces: all six `IEntityTypeConfiguration<T>` classes and seed helpers — Task 9's `AppDbContext` wiring applies these.

- [ ] **Step 1: Write the deterministic seed-id helper**

`src/Modules/Pricing/Infrastructure/PricingIds.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Courier.Pricing.Domain;

namespace Courier.Pricing.Infrastructure;

// Deterministic Guid generation for seeded reference rows, mirroring
// Courier.Geography.Infrastructure.GeographyIds — the same key always produces the same Guid, so
// HasData rows stay stable across regenerated EF migrations.
public static class PricingIds
{
    public static Guid ServiceTypeId(ServiceTypeCode code) => IdFor($"servicetype:{code}");

    public static Guid WeightSlabId(decimal minWeightKg, decimal? maxWeightKg) =>
        IdFor($"weightslab:{minWeightKg}:{(maxWeightKg?.ToString() ?? "open")}");

    private static Guid IdFor(string key)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }
}
```

- [ ] **Step 2: Write the seed data**

`src/Modules/Pricing/Infrastructure/PricingSeedData.cs`:

```csharp
using Courier.Pricing.Domain;

namespace Courier.Pricing.Infrastructure;

// Default seeded reference data. WeightSlabs is a reasonable default ladder, not a hard business
// requirement — easy to challenge/adjust later; org-specific pricing is fully expressed via
// different ZoneRate.BaseRate values per slab, not different slab boundaries (WeightSlab is
// shared/global by design, see this plan's Architecture section).
public static class PricingSeedData
{
    public static readonly (ServiceTypeCode Code, string Name)[] ServiceTypes =
    {
        (ServiceTypeCode.Standard, "Standard"),
        (ServiceTypeCode.Express, "Express"),
        (ServiceTypeCode.SameDay, "Same Day"),
        (ServiceTypeCode.Heavy, "Heavy"),
        (ServiceTypeCode.Special, "Special"),
    };

    public static readonly (decimal MinWeightKg, decimal? MaxWeightKg)[] WeightSlabs =
    {
        (0m, 0.5m),
        (0.5m, 1m),
        (1m, 2m),
        (2m, 5m),
        (5m, 10m),
        (10m, null),
    };
}
```

- [ ] **Step 3: Write a test proving the seed data is well-formed**

`tests/Pricing.Tests/PricingSeedDataTests.cs`:

```csharp
using Courier.Pricing.Infrastructure;
using Xunit;

namespace Courier.Pricing.Tests;

public class PricingSeedDataTests
{
    [Fact]
    public void ServiceTypes_AllCodesAreDistinct()
    {
        var codes = PricingSeedData.ServiceTypes.Select(s => s.Code).ToList();
        Assert.Equal(codes.Distinct().Count(), codes.Count);
    }

    [Fact]
    public void WeightSlabs_AreContiguousWithNoGapsOrOverlaps()
    {
        var slabs = PricingSeedData.WeightSlabs.OrderBy(s => s.MinWeightKg).ToList();
        for (var i = 0; i < slabs.Count - 1; i++)
        {
            Assert.Equal(slabs[i].MaxWeightKg, slabs[i + 1].MinWeightKg);
        }
        Assert.Null(slabs[^1].MaxWeightKg);
    }

    [Fact]
    public void PricingIds_SameInputProducesSameGuid()
    {
        var first = PricingIds.ServiceTypeId(Courier.Pricing.Domain.ServiceTypeCode.Standard);
        var second = PricingIds.ServiceTypeId(Courier.Pricing.Domain.ServiceTypeCode.Standard);
        Assert.Equal(first, second);
    }
}
```

- [ ] **Step 4: Run the test**

Run: `dotnet test tests/Pricing.Tests/Pricing.Tests.csproj --filter PricingSeedDataTests -v n`
Expected: PASS (3 tests).

- [ ] **Step 5: Write the EF configurations**

`src/Modules/Pricing/Infrastructure/ServiceTypeConfiguration.cs`:

```csharp
using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Pricing.Infrastructure;

public class ServiceTypeConfiguration : IEntityTypeConfiguration<ServiceType>
{
    public void Configure(EntityTypeBuilder<ServiceType> builder)
    {
        builder.ToTable("ServiceTypes");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(50).IsRequired();
        builder.HasIndex(s => s.Code).IsUnique();

        builder.HasData(PricingSeedData.ServiceTypes.Select(s => new ServiceType
        {
            Id = PricingIds.ServiceTypeId(s.Code),
            Code = s.Code,
            Name = s.Name
        }));

        // No tenant filter: shared reference data, identical across every tenant, seeded once
        // (mirrors Geography's Province/District — ARCHITECTURE.md §2).
    }
}
```

`src/Modules/Pricing/Infrastructure/WeightSlabConfiguration.cs`:

```csharp
using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Pricing.Infrastructure;

public class WeightSlabConfiguration : IEntityTypeConfiguration<WeightSlab>
{
    public void Configure(EntityTypeBuilder<WeightSlab> builder)
    {
        builder.ToTable("WeightSlabs");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.MinWeightKg).HasPrecision(10, 3);
        builder.Property(w => w.MaxWeightKg).HasPrecision(10, 3);

        builder.HasData(PricingSeedData.WeightSlabs.Select(w => new WeightSlab
        {
            Id = PricingIds.WeightSlabId(w.MinWeightKg, w.MaxWeightKg),
            MinWeightKg = w.MinWeightKg,
            MaxWeightKg = w.MaxWeightKg
        }));

        // No tenant filter: shared reference data (see ServiceTypeConfiguration).
    }
}
```

`src/Modules/Pricing/Infrastructure/RateCardConfiguration.cs`:

```csharp
using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Pricing.Infrastructure;

public class RateCardConfiguration : IEntityTypeConfiguration<RateCard>
{
    public void Configure(EntityTypeBuilder<RateCard> builder)
    {
        builder.ToTable("RateCards");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(150).IsRequired();

        // Every tenant-filtered query hits this column.
        builder.HasIndex(r => r.OrganizationId);
        // RateCardService.CreateRateCardAsync queries "the org's current IsActive card" — this
        // index makes that lookup and the "at most one active per org" check both cheap.
        builder.HasIndex(r => new { r.OrganizationId, r.IsActive });

        // No FK to Organization here: Pricing has no project reference to Identity — wired
        // centrally in Persistence/AppDbContext.OnModelCreating instead (Task 9).
    }
}
```

`src/Modules/Pricing/Infrastructure/ZoneRateConfiguration.cs`:

```csharp
using Courier.Geography.Domain;
using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Pricing.Infrastructure;

public class ZoneRateConfiguration : IEntityTypeConfiguration<ZoneRate>
{
    public void Configure(EntityTypeBuilder<ZoneRate> builder)
    {
        builder.ToTable("ZoneRates");
        builder.HasKey(z => z.Id);
        builder.Property(z => z.BaseRate).HasPrecision(18, 2);

        builder.HasIndex(z => z.RateCardId);
        // A RateCard should have at most one BaseRate for a given route/service/weight
        // combination — this is the exact tuple PricingService.QuoteAsync looks up by.
        builder.HasIndex(z => new { z.RateCardId, z.OriginZoneId, z.DestinationZoneId, z.ServiceTypeId, z.WeightSlabId }).IsUnique();

        builder.HasOne<RateCard>().WithMany().HasForeignKey(z => z.RateCardId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ServiceType>().WithMany().HasForeignKey(z => z.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WeightSlab>().WithMany().HasForeignKey(z => z.WeightSlabId).OnDelete(DeleteBehavior.Restrict);

        // Geography FKs: Pricing has a project reference to Geography (Task 1), so these are
        // configured directly here, mirroring Branches'/Merchants' Province/District FK pattern.
        // Two independent FKs to the same DeliveryZone table (origin and destination) — fine,
        // there's no navigation collection on DeliveryZone for either side to bind to.
        builder.HasOne<DeliveryZone>().WithMany().HasForeignKey(z => z.OriginZoneId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryZone>().WithMany().HasForeignKey(z => z.DestinationZoneId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

`src/Modules/Pricing/Infrastructure/AdditionalChargeConfiguration.cs`:

```csharp
using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Pricing.Infrastructure;

public class AdditionalChargeConfiguration : IEntityTypeConfiguration<AdditionalCharge>
{
    public void Configure(EntityTypeBuilder<AdditionalCharge> builder)
    {
        builder.ToTable("AdditionalCharges");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Value).HasPrecision(18, 2);

        builder.HasIndex(a => a.RateCardId);

        builder.HasOne<RateCard>().WithMany().HasForeignKey(a => a.RateCardId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

`src/Modules/Pricing/Infrastructure/MerchantRateCardConfiguration.cs`:

```csharp
using Courier.Merchants.Domain;
using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Pricing.Infrastructure;

public class MerchantRateCardConfiguration : IEntityTypeConfiguration<MerchantRateCard>
{
    public void Configure(EntityTypeBuilder<MerchantRateCard> builder)
    {
        builder.ToTable("MerchantRateCards");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.OverrideDiscountPercent).HasPrecision(5, 2);

        builder.HasIndex(m => m.OrganizationId);
        // At most one MerchantRateCard row per merchant (RateCardService.UpsertMerchantRateCardAsync
        // relies on this to decide insert vs. update).
        builder.HasIndex(m => m.MerchantId).IsUnique();

        builder.HasOne<RateCard>().WithMany().HasForeignKey(m => m.RateCardId).OnDelete(DeleteBehavior.Restrict);

        // Merchants FK: Pricing has a project reference to Merchants (Task 1), so configured
        // directly here, mirroring Branches'/Merchants' Geography FK pattern.
        builder.HasOne<Merchant>().WithMany().HasForeignKey(m => m.MerchantId).OnDelete(DeleteBehavior.Restrict);

        // No FK to Organization here: Pricing has no project reference to Identity — wired
        // centrally in Persistence/AppDbContext.OnModelCreating instead (Task 9), same reasoning
        // as DeliveryZone/Merchant.
    }
}
```

- [ ] **Step 6: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Pricing/Infrastructure tests/Pricing.Tests/PricingSeedDataTests.cs
git commit -m "feat: add Pricing EF configurations and seed data"
```

---

### Task 7: Pricing Application — RateCardService (RateCard/ZoneRate/AdditionalCharge/MerchantRateCard)

**Files:**
- Create: `src/Modules/Pricing/Application/RateCardDtos.cs`
- Create: `src/Modules/Pricing/Application/RateCardService.cs`
- Test: `tests/Pricing.Tests/RateCardServiceTests.cs`

**Interfaces:**
- Consumes: `IGeographyLookupService.DeliveryZoneExistsAsync` (Task 2), `IMerchantLookupService.ExistsAsync` (Task 3), `Pricing.Domain` entities (Task 5), `Pricing.Infrastructure` configurations (Task 6).
- Produces: `RateCardService.{CreateRateCardAsync, GetRateCardAsync, ListRateCardsAsync, AddZoneRateAsync, ListZoneRatesAsync, AddAdditionalChargeAsync, ListAdditionalChargesAsync, UpsertMerchantRateCardAsync, GetMerchantRateCardAsync}`, `PricingValidationException` — Task 9's endpoints and Task 8's `PricingService` (indirectly, by sharing the same DB rows) consume these.

- [ ] **Step 1: Write the DTOs and exception**

`src/Modules/Pricing/Application/RateCardDtos.cs`:

```csharp
using Courier.Pricing.Domain;

namespace Courier.Pricing.Application;

public record CreateRateCardRequest(string Name, DateTimeOffset EffectiveFrom);

public record RateCardResponse(Guid Id, string Name, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, int Version, bool IsActive);

public record CreateZoneRateRequest(Guid OriginZoneId, Guid DestinationZoneId, Guid ServiceTypeId, Guid WeightSlabId, decimal BaseRate);

public record ZoneRateResponse(Guid Id, Guid RateCardId, Guid OriginZoneId, Guid DestinationZoneId, Guid ServiceTypeId, Guid WeightSlabId, decimal BaseRate);

public record CreateAdditionalChargeRequest(AdditionalChargeType Type, ChargeCalculationType CalculationType, decimal Value);

public record AdditionalChargeResponse(Guid Id, Guid RateCardId, AdditionalChargeType Type, ChargeCalculationType CalculationType, decimal Value);

public record UpsertMerchantRateCardRequest(Guid RateCardId, decimal? OverrideDiscountPercent);

public record MerchantRateCardResponse(Guid Id, Guid MerchantId, Guid RateCardId, decimal? OverrideDiscountPercent);

public record ServiceTypeResponse(Guid Id, ServiceTypeCode Code, string Name);

public record WeightSlabResponse(Guid Id, decimal MinWeightKg, decimal? MaxWeightKg);

public class PricingValidationException : Exception
{
    public PricingValidationException(string message) : base(message) { }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Pricing.Tests/RateCardServiceTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Pricing.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
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

public class RateCardServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ServiceTypeConfiguration());
            modelBuilder.ApplyConfiguration(new WeightSlabConfiguration());
            modelBuilder.ApplyConfiguration(new RateCardConfiguration());
            modelBuilder.ApplyConfiguration(new ZoneRateConfiguration());
            modelBuilder.ApplyConfiguration(new AdditionalChargeConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantRateCardConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", userId.ToString())
    }, "test"));

    private static RateCardService CreateService(TestDbContext dbContext, Guid organizationId) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup());

    [Fact]
    public async Task CreateRateCardAsync_FirstCardForOrg_IsVersionOneAndActive()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        var result = await service.CreateRateCardAsync(new CreateRateCardRequest("Default", DateTimeOffset.UtcNow), ActingUser(Guid.NewGuid()));

        Assert.Equal(1, result.Version);
        Assert.True(result.IsActive);
        Assert.Null(result.EffectiveTo);
    }

    [Fact]
    public async Task CreateRateCardAsync_SecondCardForOrg_SupersedesFirstWithoutMutatingItsChildren()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());

        var first = await service.CreateRateCardAsync(new CreateRateCardRequest("v1", DateTimeOffset.UtcNow), actingUser);
        var serviceTypeId = dbContext.Set<ServiceType>().First().Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First().Id;
        var originZone = Guid.NewGuid();
        var destZone = Guid.NewGuid();
        var zoneRate = await service.AddZoneRateAsync(first.Id, new CreateZoneRateRequest(originZone, destZone, serviceTypeId, weightSlabId, 100m), actingUser);

        var second = await service.CreateRateCardAsync(new CreateRateCardRequest("v2", DateTimeOffset.UtcNow.AddDays(1)), actingUser);

        Assert.Equal(2, second.Version);
        Assert.True(second.IsActive);

        var firstReloaded = await service.GetRateCardAsync(first.Id);
        Assert.False(firstReloaded!.IsActive);
        Assert.NotNull(firstReloaded.EffectiveTo);

        var firstZoneRates = await service.ListZoneRatesAsync(first.Id);
        Assert.Single(firstZoneRates);
        Assert.Equal(100m, firstZoneRates[0].BaseRate);
    }

    [Fact]
    public async Task AddZoneRateAsync_OnSupersededRateCard_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());
        var actingUser = ActingUser(Guid.NewGuid());

        var first = await service.CreateRateCardAsync(new CreateRateCardRequest("v1", DateTimeOffset.UtcNow), actingUser);
        await service.CreateRateCardAsync(new CreateRateCardRequest("v2", DateTimeOffset.UtcNow.AddDays(1)), actingUser);

        var serviceTypeId = dbContext.Set<ServiceType>().First().Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First().Id;

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.AddZoneRateAsync(first.Id, new CreateZoneRateRequest(Guid.NewGuid(), Guid.NewGuid(), serviceTypeId, weightSlabId, 50m), actingUser));
    }

    [Fact]
    public async Task UpsertMerchantRateCardAsync_FirstCall_Inserts_SecondCall_Updates()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        var merchantId = Guid.NewGuid();

        var rateCard = await service.CreateRateCardAsync(new CreateRateCardRequest("v1", DateTimeOffset.UtcNow), actingUser);

        var inserted = await service.UpsertMerchantRateCardAsync(merchantId, new UpsertMerchantRateCardRequest(rateCard.Id, 10m), actingUser);
        Assert.Equal(10m, inserted.OverrideDiscountPercent);

        var updated = await service.UpsertMerchantRateCardAsync(merchantId, new UpsertMerchantRateCardRequest(rateCard.Id, 15m), actingUser);
        Assert.Equal(15m, updated.OverrideDiscountPercent);
        Assert.Equal(inserted.Id, updated.Id);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Pricing.Tests/Pricing.Tests.csproj --filter RateCardServiceTests -v n`
Expected: FAIL — `RateCardService` doesn't exist.

- [ ] **Step 4: Write RateCardService**

`src/Modules/Pricing/Application/RateCardService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Pricing.Application;

public class RateCardService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IGeographyLookupService _geographyLookupService;
    private readonly IMerchantLookupService _merchantLookupService;

    public RateCardService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IGeographyLookupService geographyLookupService,
        IMerchantLookupService merchantLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _geographyLookupService = geographyLookupService;
        _merchantLookupService = merchantLookupService;
    }

    public async Task<RateCardResponse> CreateRateCardAsync(CreateRateCardRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new PricingValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new PricingValidationException("Acting user could not be resolved.");

        var previous = await _dbContext.Set<RateCard>().FirstOrDefaultAsync(rc => rc.IsActive, cancellationToken);

        var rateCard = new RateCard
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = null,
            Version = previous is null ? 1 : previous.Version + 1,
            IsActive = true
        };
        _dbContext.Set<RateCard>().Add(rateCard);

        if (previous is not null)
        {
            previous.IsActive = false;
            previous.EffectiveTo = request.EffectiveFrom;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "rate_card.created", "RateCard", rateCard.Id.ToString()), cancellationToken);

        return ToResponse(rateCard);
    }

    public async Task<RateCardResponse?> GetRateCardAsync(Guid rateCardId, CancellationToken cancellationToken = default)
    {
        var rateCard = await _dbContext.Set<RateCard>().FirstOrDefaultAsync(rc => rc.Id == rateCardId, cancellationToken);
        return rateCard is null ? null : ToResponse(rateCard);
    }

    public async Task<List<RateCardResponse>> ListRateCardsAsync(CancellationToken cancellationToken = default)
    {
        var rateCards = await _dbContext.Set<RateCard>().OrderByDescending(rc => rc.Version).ToListAsync(cancellationToken);
        return rateCards.Select(ToResponse).ToList();
    }

    public async Task<ZoneRateResponse> AddZoneRateAsync(Guid rateCardId, CreateZoneRateRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new PricingValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new PricingValidationException("Acting user could not be resolved.");

        var rateCard = await _dbContext.Set<RateCard>().FirstOrDefaultAsync(rc => rc.Id == rateCardId, cancellationToken)
            ?? throw new PricingValidationException("Rate card was not found in this organization.");
        if (!rateCard.IsActive)
        {
            throw new PricingValidationException("Cannot add a zone rate to a superseded rate card.");
        }

        if (!await _geographyLookupService.DeliveryZoneExistsAsync(request.OriginZoneId, cancellationToken))
        {
            throw new PricingValidationException("Origin delivery zone was not found in this organization.");
        }
        if (!await _geographyLookupService.DeliveryZoneExistsAsync(request.DestinationZoneId, cancellationToken))
        {
            throw new PricingValidationException("Destination delivery zone was not found in this organization.");
        }
        if (!await _dbContext.Set<ServiceType>().AnyAsync(s => s.Id == request.ServiceTypeId, cancellationToken))
        {
            throw new PricingValidationException("Unknown service type.");
        }
        if (!await _dbContext.Set<WeightSlab>().AnyAsync(w => w.Id == request.WeightSlabId, cancellationToken))
        {
            throw new PricingValidationException("Unknown weight slab.");
        }

        var zoneRate = new ZoneRate
        {
            Id = Guid.NewGuid(),
            RateCardId = rateCardId,
            OriginZoneId = request.OriginZoneId,
            DestinationZoneId = request.DestinationZoneId,
            ServiceTypeId = request.ServiceTypeId,
            WeightSlabId = request.WeightSlabId,
            BaseRate = request.BaseRate
        };
        _dbContext.Set<ZoneRate>().Add(zoneRate);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "zone_rate.created", "ZoneRate", zoneRate.Id.ToString()), cancellationToken);

        return ToResponse(zoneRate);
    }

    public async Task<List<ZoneRateResponse>> ListZoneRatesAsync(Guid rateCardId, CancellationToken cancellationToken = default)
    {
        var zoneRates = await _dbContext.Set<ZoneRate>().Where(z => z.RateCardId == rateCardId).ToListAsync(cancellationToken);
        return zoneRates.Select(ToResponse).ToList();
    }

    public async Task<AdditionalChargeResponse> AddAdditionalChargeAsync(Guid rateCardId, CreateAdditionalChargeRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new PricingValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new PricingValidationException("Acting user could not be resolved.");

        var rateCard = await _dbContext.Set<RateCard>().FirstOrDefaultAsync(rc => rc.Id == rateCardId, cancellationToken)
            ?? throw new PricingValidationException("Rate card was not found in this organization.");
        if (!rateCard.IsActive)
        {
            throw new PricingValidationException("Cannot add an additional charge to a superseded rate card.");
        }

        var charge = new AdditionalCharge
        {
            Id = Guid.NewGuid(),
            RateCardId = rateCardId,
            Type = request.Type,
            CalculationType = request.CalculationType,
            Value = request.Value
        };
        _dbContext.Set<AdditionalCharge>().Add(charge);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "additional_charge.created", "AdditionalCharge", charge.Id.ToString()), cancellationToken);

        return ToResponse(charge);
    }

    public async Task<List<AdditionalChargeResponse>> ListAdditionalChargesAsync(Guid rateCardId, CancellationToken cancellationToken = default)
    {
        var charges = await _dbContext.Set<AdditionalCharge>().Where(c => c.RateCardId == rateCardId).ToListAsync(cancellationToken);
        return charges.Select(ToResponse).ToList();
    }

    public async Task<MerchantRateCardResponse> UpsertMerchantRateCardAsync(Guid merchantId, UpsertMerchantRateCardRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new PricingValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new PricingValidationException("Acting user could not be resolved.");

        if (!await _merchantLookupService.ExistsAsync(merchantId, cancellationToken))
        {
            throw new PricingValidationException("Merchant was not found in this organization.");
        }
        if (!await _dbContext.Set<RateCard>().AnyAsync(rc => rc.Id == request.RateCardId, cancellationToken))
        {
            throw new PricingValidationException("Rate card was not found in this organization.");
        }

        var existing = await _dbContext.Set<MerchantRateCard>().FirstOrDefaultAsync(m => m.MerchantId == merchantId, cancellationToken);
        if (existing is null)
        {
            existing = new MerchantRateCard
            {
                Id = Guid.NewGuid(),
                MerchantId = merchantId,
                OrganizationId = organizationId,
                RateCardId = request.RateCardId,
                OverrideDiscountPercent = request.OverrideDiscountPercent
            };
            _dbContext.Set<MerchantRateCard>().Add(existing);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "merchant_rate_card.created", "MerchantRateCard", existing.Id.ToString()), cancellationToken);
        }
        else
        {
            existing.RateCardId = request.RateCardId;
            existing.OverrideDiscountPercent = request.OverrideDiscountPercent;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "merchant_rate_card.updated", "MerchantRateCard", existing.Id.ToString()), cancellationToken);
        }

        return ToResponse(existing);
    }

    public async Task<MerchantRateCardResponse?> GetMerchantRateCardAsync(Guid merchantId, CancellationToken cancellationToken = default)
    {
        var merchantRateCard = await _dbContext.Set<MerchantRateCard>().FirstOrDefaultAsync(m => m.MerchantId == merchantId, cancellationToken);
        return merchantRateCard is null ? null : ToResponse(merchantRateCard);
    }

    private static RateCardResponse ToResponse(RateCard rateCard) =>
        new(rateCard.Id, rateCard.Name, rateCard.EffectiveFrom, rateCard.EffectiveTo, rateCard.Version, rateCard.IsActive);

    private static ZoneRateResponse ToResponse(ZoneRate zoneRate) =>
        new(zoneRate.Id, zoneRate.RateCardId, zoneRate.OriginZoneId, zoneRate.DestinationZoneId, zoneRate.ServiceTypeId, zoneRate.WeightSlabId, zoneRate.BaseRate);

    private static AdditionalChargeResponse ToResponse(AdditionalCharge charge) =>
        new(charge.Id, charge.RateCardId, charge.Type, charge.CalculationType, charge.Value);

    private static MerchantRateCardResponse ToResponse(MerchantRateCard merchantRateCard) =>
        new(merchantRateCard.Id, merchantRateCard.MerchantId, merchantRateCard.RateCardId, merchantRateCard.OverrideDiscountPercent);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Pricing.Tests/Pricing.Tests.csproj --filter RateCardServiceTests -v n`
Expected: PASS (4 tests).

- [ ] **Step 6: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Pricing/Application/RateCardDtos.cs src/Modules/Pricing/Application/RateCardService.cs tests/Pricing.Tests/RateCardServiceTests.cs
git commit -m "feat: add Pricing RateCardService"
```

---

### Task 8: Pricing Application — PricingService (Quote Calculation) and ReferenceDataService

**Files:**
- Create: `src/Modules/Pricing/Application/PricingDtos.cs`
- Create: `src/Modules/Pricing/Application/PricingService.cs`
- Create: `src/Modules/Pricing/Application/ReferenceDataService.cs`
- Test: `tests/Pricing.Tests/PricingServiceTests.cs`

**Interfaces:**
- Consumes: `IBranchLookupService.GetZoneInfoAsync` (Task 4), `IMerchantLookupService.ExistsAsync` (Task 3), `RateCardService`'s underlying `Pricing.Domain` rows (Task 7).
- Produces: `PricingService.QuoteAsync(PriceQuoteRequest, CancellationToken) -> PriceQuoteResponse`, `ReferenceDataService.{ListServiceTypesAsync, ListWeightSlabsAsync}` — Task 9's endpoints consume these.

- [ ] **Step 1: Write the DTOs**

`src/Modules/Pricing/Application/PricingDtos.cs`:

```csharp
using Courier.Pricing.Domain;

namespace Courier.Pricing.Application;

public record PriceQuoteRequest(
    Guid OriginBranchId,
    Guid DestinationBranchId,
    decimal WeightKg,
    Guid ServiceTypeId,
    decimal CodAmount,
    decimal DeclaredValue,
    bool WantsInsurance,
    Guid? MerchantId);

public record AppliedCharge(AdditionalChargeType Type, decimal Amount);

public record PriceQuoteResponse(
    decimal BaseRate,
    IReadOnlyList<AppliedCharge> AppliedCharges,
    decimal DiscountPercent,
    decimal Subtotal,
    decimal Total,
    Guid RateCardId,
    int RateCardVersion);
```

- [ ] **Step 2: Write the failing tests**

`tests/Pricing.Tests/PricingServiceTests.cs`:

```csharp
using Courier.Branches.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Pricing.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class FakeBranchLookup : IBranchLookupService
{
    private readonly Dictionary<Guid, Guid?> _zonesByBranch;
    public FakeBranchLookup(Dictionary<Guid, Guid?> zonesByBranch) => _zonesByBranch = zonesByBranch;

    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_zonesByBranch.TryGetValue(branchId, out var zoneId) ? new BranchZoneInfo(branchId, zoneId) : null);
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public class PricingServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ServiceTypeConfiguration());
            modelBuilder.ApplyConfiguration(new WeightSlabConfiguration());
            modelBuilder.ApplyConfiguration(new RateCardConfiguration());
            modelBuilder.ApplyConfiguration(new ZoneRateConfiguration());
            modelBuilder.ApplyConfiguration(new AdditionalChargeConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantRateCardConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static (Guid OriginBranchId, Guid DestinationBranchId, Guid OriginZoneId, Guid DestinationZoneId, RateCard RateCard, ZoneRate ZoneRate) SeedBasicRoute(TestDbContext dbContext, Guid organizationId, decimal baseRate = 100m)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;

        var rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
        dbContext.Set<RateCard>().Add(rateCard);
        var zoneRate = new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = weightSlabId, BaseRate = baseRate };
        dbContext.Set<ZoneRate>().Add(zoneRate);
        dbContext.SaveChanges();

        return (originBranchId, destinationBranchId, originZoneId, destinationZoneId, rateCard, zoneRate);
    }

    private static PricingService CreateService(TestDbContext dbContext, Guid organizationId, Dictionary<Guid, Guid?> zonesByBranch) =>
        new(dbContext, new FixedTenantProvider(organizationId), new FakeBranchLookup(zonesByBranch), new AlwaysTrueMerchantLookup());

    [Fact]
    public async Task QuoteAsync_NoCharges_ReturnsBaseRateAsTotal()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        var result = await service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null));

        Assert.Equal(100m, result.BaseRate);
        Assert.Empty(result.AppliedCharges);
        Assert.Equal(100m, result.Total);
        Assert.Equal(seed.RateCard.Id, result.RateCardId);
    }

    [Fact]
    public async Task QuoteAsync_InsuranceNotRequested_IsNotApplied()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        dbContext.Set<AdditionalCharge>().Add(new AdditionalCharge { Id = Guid.NewGuid(), RateCardId = seed.RateCard.Id, Type = AdditionalChargeType.Insurance, CalculationType = ChargeCalculationType.Percentage, Value = 2m });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        var result = await service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, 1000m, false, null));

        Assert.Empty(result.AppliedCharges);
        Assert.Equal(100m, result.Total);
    }

    [Fact]
    public async Task QuoteAsync_InsuranceRequested_AppliesPercentOfDeclaredValue()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        dbContext.Set<AdditionalCharge>().Add(new AdditionalCharge { Id = Guid.NewGuid(), RateCardId = seed.RateCard.Id, Type = AdditionalChargeType.Insurance, CalculationType = ChargeCalculationType.Percentage, Value = 2m });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        var result = await service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, 1000m, true, null));

        var insuranceCharge = Assert.Single(result.AppliedCharges);
        Assert.Equal(AdditionalChargeType.Insurance, insuranceCharge.Type);
        Assert.Equal(20m, insuranceCharge.Amount); // 2% of 1000
        Assert.Equal(120m, result.Total);
    }

    [Fact]
    public async Task QuoteAsync_CodAmountZero_CodChargeNotApplied()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        dbContext.Set<AdditionalCharge>().Add(new AdditionalCharge { Id = Guid.NewGuid(), RateCardId = seed.RateCard.Id, Type = AdditionalChargeType.COD, CalculationType = ChargeCalculationType.Percentage, Value = 1m });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        var result = await service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null));

        Assert.Empty(result.AppliedCharges);
    }

    [Fact]
    public async Task QuoteAsync_FuelSurcharge_AppliesAsPercentOfBaseRate()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId, baseRate: 200m);
        dbContext.Set<AdditionalCharge>().Add(new AdditionalCharge { Id = Guid.NewGuid(), RateCardId = seed.RateCard.Id, Type = AdditionalChargeType.Fuel, CalculationType = ChargeCalculationType.Percentage, Value = 10m });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        var result = await service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null));

        var fuelCharge = Assert.Single(result.AppliedCharges);
        Assert.Equal(20m, fuelCharge.Amount); // 10% of 200 base
        Assert.Equal(220m, result.Total);
    }

    [Fact]
    public async Task QuoteAsync_MerchantWithOverride_AppliesDiscountToTotal()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        var merchantId = Guid.NewGuid();
        dbContext.Set<MerchantRateCard>().Add(new MerchantRateCard { Id = Guid.NewGuid(), MerchantId = merchantId, OrganizationId = organizationId, RateCardId = seed.RateCard.Id, OverrideDiscountPercent = 10m });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        var result = await service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, merchantId));

        Assert.Equal(10m, result.DiscountPercent);
        Assert.Equal(90m, result.Total); // 100 base - 10%
    }

    [Fact]
    public async Task QuoteAsync_WeightAtSlabUpperBound_ResolvesToNextSlab()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var lowerSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;
        var upperSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 1m && w.MaxWeightKg == 2m).Id;
        var rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
        dbContext.Set<RateCard>().Add(rateCard);
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = lowerSlabId, BaseRate = 50m });
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = upperSlabId, BaseRate = 75m });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new()
        {
            [originBranchId] = originZoneId,
            [destinationBranchId] = destinationZoneId
        });

        // Exactly 1.0kg: the lower slab (0.5-1) is exclusive at its MaxWeightKg, so 1.0kg must
        // resolve to the upper slab (1-2), proving the boundary is MinWeightKg <= w < MaxWeightKg.
        var result = await service.QuoteAsync(new PriceQuoteRequest(originBranchId, destinationBranchId, 1.0m, serviceTypeId, 0m, 0m, false, null));

        Assert.Equal(75m, result.BaseRate);
    }

    [Fact]
    public async Task QuoteAsync_OpenEndedTopSlab_ResolvesForVeryHeavyWeight()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var topSlabId = dbContext.Set<WeightSlab>().First(w => w.MaxWeightKg == null).Id;
        var rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
        dbContext.Set<RateCard>().Add(rateCard);
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = topSlabId, BaseRate = 500m });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new()
        {
            [originBranchId] = originZoneId,
            [destinationBranchId] = destinationZoneId
        });

        var result = await service.QuoteAsync(new PriceQuoteRequest(originBranchId, destinationBranchId, 250m, serviceTypeId, 0m, 0m, false, null));

        Assert.Equal(500m, result.BaseRate);
    }

    [Fact]
    public async Task QuoteAsync_NoZoneRateForCombination_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = seed.OriginZoneId,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var expressServiceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Express).Id;

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, expressServiceTypeId, 0m, 0m, false, null)));
    }

    [Fact]
    public async Task QuoteAsync_OriginBranchHasNoZoneAssigned_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedBasicRoute(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new()
        {
            [seed.OriginBranchId] = null,
            [seed.DestinationBranchId] = seed.DestinationZoneId
        });
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.QuoteAsync(new PriceQuoteRequest(seed.OriginBranchId, seed.DestinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null)));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Pricing.Tests/Pricing.Tests.csproj --filter PricingServiceTests -v n`
Expected: FAIL — `PricingService` doesn't exist.

- [ ] **Step 4: Write PricingService and ReferenceDataService**

`src/Modules/Pricing/Application/PricingService.cs`:

```csharp
using Courier.Branches.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Pricing.Application;

public class PricingService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IMerchantLookupService _merchantLookupService;

    public PricingService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IBranchLookupService branchLookupService,
        IMerchantLookupService merchantLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _branchLookupService = branchLookupService;
        _merchantLookupService = merchantLookupService;
    }

    // Deterministic charge-evaluation order: Fuel/Other apply unconditionally, before the
    // input-driven Insurance/COD charges, so applied-charge order (and therefore any
    // Fuel/Other percentage compounding against the running subtotal) never depends on how the
    // RateCard's rows happen to be stored.
    private static readonly AdditionalChargeType[] ChargeEvaluationOrder =
    {
        AdditionalChargeType.Fuel,
        AdditionalChargeType.Other,
        AdditionalChargeType.Insurance,
        AdditionalChargeType.COD
    };

    public async Task<PriceQuoteResponse> QuoteAsync(PriceQuoteRequest request, CancellationToken cancellationToken = default)
    {
        _ = _currentTenantProvider.OrganizationId
            ?? throw new PricingValidationException("No active organization context.");

        var originZone = await _branchLookupService.GetZoneInfoAsync(request.OriginBranchId, cancellationToken)
            ?? throw new PricingValidationException("Origin branch was not found in this organization.");
        var destinationZone = await _branchLookupService.GetZoneInfoAsync(request.DestinationBranchId, cancellationToken)
            ?? throw new PricingValidationException("Destination branch was not found in this organization.");

        if (originZone.DeliveryZoneId is null || destinationZone.DeliveryZoneId is null)
        {
            throw new PricingValidationException("Origin or destination branch has no delivery zone assigned.");
        }

        var weightSlab = await _dbContext.Set<WeightSlab>()
            .Where(w => w.MinWeightKg <= request.WeightKg && (w.MaxWeightKg == null || request.WeightKg < w.MaxWeightKg))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new PricingValidationException($"No weight slab covers {request.WeightKg}kg.");

        RateCard? rateCard;
        var discountPercent = 0m;

        if (request.MerchantId is Guid merchantId)
        {
            if (!await _merchantLookupService.ExistsAsync(merchantId, cancellationToken))
            {
                throw new PricingValidationException("Merchant was not found in this organization.");
            }

            var merchantRateCard = await _dbContext.Set<MerchantRateCard>().FirstOrDefaultAsync(m => m.MerchantId == merchantId, cancellationToken);
            if (merchantRateCard is not null)
            {
                rateCard = await _dbContext.Set<RateCard>().FirstOrDefaultAsync(rc => rc.Id == merchantRateCard.RateCardId, cancellationToken);
                discountPercent = merchantRateCard.OverrideDiscountPercent ?? 0m;
            }
            else
            {
                rateCard = await _dbContext.Set<RateCard>().FirstOrDefaultAsync(rc => rc.IsActive, cancellationToken);
            }
        }
        else
        {
            rateCard = await _dbContext.Set<RateCard>().FirstOrDefaultAsync(rc => rc.IsActive, cancellationToken);
        }

        if (rateCard is null)
        {
            throw new PricingValidationException("No active rate card found for this organization.");
        }

        var zoneRate = await _dbContext.Set<ZoneRate>().FirstOrDefaultAsync(z =>
                z.RateCardId == rateCard.Id &&
                z.OriginZoneId == originZone.DeliveryZoneId &&
                z.DestinationZoneId == destinationZone.DeliveryZoneId &&
                z.ServiceTypeId == request.ServiceTypeId &&
                z.WeightSlabId == weightSlab.Id, cancellationToken)
            ?? throw new PricingValidationException("No rate is defined for this origin/destination/service/weight combination.");

        var charges = await _dbContext.Set<AdditionalCharge>().Where(c => c.RateCardId == rateCard.Id).ToListAsync(cancellationToken);

        var runningSubtotal = zoneRate.BaseRate;
        var applied = new List<AppliedCharge>();

        foreach (var chargeType in ChargeEvaluationOrder)
        {
            foreach (var charge in charges.Where(c => c.Type == chargeType))
            {
                var appliesNow = chargeType switch
                {
                    AdditionalChargeType.Insurance => request.WantsInsurance,
                    AdditionalChargeType.COD => request.CodAmount > 0,
                    _ => true
                };
                if (!appliesNow)
                {
                    continue;
                }

                var baseAmount = chargeType switch
                {
                    AdditionalChargeType.Insurance => request.DeclaredValue,
                    AdditionalChargeType.COD => request.CodAmount,
                    _ => runningSubtotal
                };

                var amount = charge.CalculationType == ChargeCalculationType.Flat
                    ? charge.Value
                    : Math.Round(charge.Value / 100m * baseAmount, 2, MidpointRounding.AwayFromZero);

                applied.Add(new AppliedCharge(charge.Type, amount));
                runningSubtotal += amount;
            }
        }

        var total = Math.Round(runningSubtotal * (1 - discountPercent / 100m), 2, MidpointRounding.AwayFromZero);

        return new PriceQuoteResponse(zoneRate.BaseRate, applied, discountPercent, runningSubtotal, total, rateCard.Id, rateCard.Version);
    }
}
```

`src/Modules/Pricing/Application/ReferenceDataService.cs`:

```csharp
using Courier.Pricing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Pricing.Application;

public class ReferenceDataService
{
    private readonly DbContext _dbContext;

    public ReferenceDataService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ServiceTypeResponse>> ListServiceTypesAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Set<ServiceType>()
            .OrderBy(s => s.Name)
            .Select(s => new ServiceTypeResponse(s.Id, s.Code, s.Name))
            .ToListAsync(cancellationToken);

    public async Task<List<WeightSlabResponse>> ListWeightSlabsAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Set<WeightSlab>()
            .OrderBy(w => w.MinWeightKg)
            .Select(w => new WeightSlabResponse(w.Id, w.MinWeightKg, w.MaxWeightKg))
            .ToListAsync(cancellationToken);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Pricing.Tests/Pricing.Tests.csproj --filter PricingServiceTests -v n`
Expected: PASS (10 tests).

- [ ] **Step 6: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Pricing/Application/PricingDtos.cs src/Modules/Pricing/Application/PricingService.cs src/Modules/Pricing/Application/ReferenceDataService.cs tests/Pricing.Tests/PricingServiceTests.cs
git commit -m "feat: add Pricing quote calculation engine"
```

---

### Task 9: Pricing Presentation — Endpoints and DI Wiring

**Files:**
- Create: `src/Modules/Pricing/Presentation/PricingEndpoints.cs`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: `RateCardService`, `PricingService`, `ReferenceDataService` (Tasks 7-8).
- Produces: all 13 HTTP endpoints listed in this plan's Architecture section (`GET/POST /api/v1/rate-cards...`, `GET /api/v1/service-types`, `GET /api/v1/weight-slabs`, `GET/PUT /api/v1/merchants/{merchantId}/rate-card`, `POST /api/v1/pricing/quote`).

- [ ] **Step 1: Write the endpoints**

`src/Modules/Pricing/Presentation/PricingEndpoints.cs`:

```csharp
using Courier.Pricing.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Pricing.Presentation;

public static class PricingEndpoints
{
    public static void MapPricingEndpoints(this IEndpointRouteBuilder app)
    {
        var referenceGroup = app.MapGroup("/api/v1");

        referenceGroup.MapGet("/service-types", async (ReferenceDataService referenceDataService, CancellationToken cancellationToken) =>
            Results.Ok(await referenceDataService.ListServiceTypesAsync(cancellationToken))).RequirePermission("pricing.read");

        referenceGroup.MapGet("/weight-slabs", async (ReferenceDataService referenceDataService, CancellationToken cancellationToken) =>
            Results.Ok(await referenceDataService.ListWeightSlabsAsync(cancellationToken))).RequirePermission("pricing.read");

        var rateCardsGroup = app.MapGroup("/api/v1/rate-cards");

        rateCardsGroup.MapGet("/", async (RateCardService rateCardService, CancellationToken cancellationToken) =>
            Results.Ok(await rateCardService.ListRateCardsAsync(cancellationToken))).RequirePermission("pricing.read");

        rateCardsGroup.MapGet("/{id:guid}", async (Guid id, RateCardService rateCardService, CancellationToken cancellationToken) =>
        {
            var rateCard = await rateCardService.GetRateCardAsync(id, cancellationToken);
            return rateCard is null ? Results.NotFound() : Results.Ok(rateCard);
        }).RequirePermission("pricing.read");

        rateCardsGroup.MapPost("/", async (CreateRateCardRequest request, RateCardService rateCardService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await rateCardService.CreateRateCardAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/rate-cards/{result.Id}", result);
            }
            catch (PricingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "PRICING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("pricing.manage");

        rateCardsGroup.MapGet("/{id:guid}/zone-rates", async (Guid id, RateCardService rateCardService, CancellationToken cancellationToken) =>
            Results.Ok(await rateCardService.ListZoneRatesAsync(id, cancellationToken))).RequirePermission("pricing.read");

        rateCardsGroup.MapPost("/{id:guid}/zone-rates", async (Guid id, CreateZoneRateRequest request, RateCardService rateCardService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await rateCardService.AddZoneRateAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/rate-cards/{id}/zone-rates/{result.Id}", result);
            }
            catch (PricingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "PRICING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("pricing.manage");

        rateCardsGroup.MapGet("/{id:guid}/additional-charges", async (Guid id, RateCardService rateCardService, CancellationToken cancellationToken) =>
            Results.Ok(await rateCardService.ListAdditionalChargesAsync(id, cancellationToken))).RequirePermission("pricing.read");

        rateCardsGroup.MapPost("/{id:guid}/additional-charges", async (Guid id, CreateAdditionalChargeRequest request, RateCardService rateCardService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await rateCardService.AddAdditionalChargeAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/rate-cards/{id}/additional-charges/{result.Id}", result);
            }
            catch (PricingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "PRICING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("pricing.manage");

        var merchantRateCardGroup = app.MapGroup("/api/v1/merchants");

        merchantRateCardGroup.MapGet("/{merchantId:guid}/rate-card", async (Guid merchantId, RateCardService rateCardService, CancellationToken cancellationToken) =>
        {
            var merchantRateCard = await rateCardService.GetMerchantRateCardAsync(merchantId, cancellationToken);
            return merchantRateCard is null ? Results.NotFound() : Results.Ok(merchantRateCard);
        }).RequirePermission("pricing.read");

        merchantRateCardGroup.MapPut("/{merchantId:guid}/rate-card", async (Guid merchantId, UpsertMerchantRateCardRequest request, RateCardService rateCardService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await rateCardService.UpsertMerchantRateCardAsync(merchantId, request, httpContext.User, httpContext.RequestAborted);
                return Results.Ok(result);
            }
            catch (PricingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "PRICING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("pricing.manage");

        var quoteGroup = app.MapGroup("/api/v1/pricing");

        quoteGroup.MapPost("/quote", async (PriceQuoteRequest request, PricingService pricingService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await pricingService.QuoteAsync(request, httpContext.RequestAborted);
                return Results.Ok(result);
            }
            catch (PricingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "PRICING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("pricing.read");
    }
}
```

- [ ] **Step 2: Wire DI and endpoint mapping in Program.cs**

In `src/Api/Program.cs`, add `using Courier.Pricing.Application;` and `using Courier.Pricing.Presentation;` to the top `using` block (alphabetically between the `Courier.Persistence` and `Courier.SharedKernel` usings — actually `Pricing` sorts after `Persistence` and before `SharedKernel`, matching this file's existing alphabetical `using` ordering).

After the `builder.Services.AddScoped<IMerchantLookupService>(sp => sp.GetRequiredService<MerchantLookupService>());` line added in Task 3 (and after Task 4's `IBranchLookupService` registration), add:

```csharp
builder.Services.AddScoped<RateCardService>();
builder.Services.AddScoped<PricingService>();
builder.Services.AddScoped<ReferenceDataService>();
```

After the existing `app.MapMerchantKycEndpoints();` line, add:

```csharp
app.MapPricingEndpoints();
```

- [ ] **Step 3: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Pricing/Presentation src/Api/Program.cs
git commit -m "feat: add Pricing endpoints and DI wiring"
```

---

### Task 10: Persistence — Wire Pricing into AppDbContext, Add Migration

**Files:**
- Modify: `src/Persistence/AppDbContext.cs`
- Create: `src/Persistence/Migrations/` (generated)

**Interfaces:**
- Consumes: all Pricing configurations (Task 6).
- Produces: `AppDbContext.{ServiceTypes, WeightSlabs, RateCards, ZoneRates, AdditionalCharges, MerchantRateCards}`, a migration creating all six tables plus FKs/indexes/seed rows/query filters. Task 11's Api.Tests query through this context.

- [ ] **Step 1: Update AppDbContext**

Add `using Courier.Pricing.Domain;` and `using Courier.Pricing.Infrastructure;` to the top of `src/Persistence/AppDbContext.cs` (alphabetically, after `Courier.Merchants.Infrastructure` and before `Courier.SharedKernel`).

Add to the `DbSet` list, after `MerchantRefreshTokens`:

```csharp
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<WeightSlab> WeightSlabs => Set<WeightSlab>();
    public DbSet<RateCard> RateCards => Set<RateCard>();
    public DbSet<ZoneRate> ZoneRates => Set<ZoneRate>();
    public DbSet<AdditionalCharge> AdditionalCharges => Set<AdditionalCharge>();
    public DbSet<MerchantRateCard> MerchantRateCards => Set<MerchantRateCard>();
```

In `OnModelCreating`, add to the `ApplyConfiguration` block, after `MerchantRefreshTokenConfiguration`:

```csharp
        modelBuilder.ApplyConfiguration(new ServiceTypeConfiguration());
        modelBuilder.ApplyConfiguration(new WeightSlabConfiguration());
        modelBuilder.ApplyConfiguration(new RateCardConfiguration());
        modelBuilder.ApplyConfiguration(new ZoneRateConfiguration());
        modelBuilder.ApplyConfiguration(new AdditionalChargeConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantRateCardConfiguration());
```

After the existing `modelBuilder.Entity<MerchantKycDocument>().HasOne<Organization>()...` block, add:

```csharp
        // Same reasoning for RateCard/MerchantRateCard.OrganizationId: Pricing has no project
        // reference to Identity either.
        modelBuilder.Entity<RateCard>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(rc => rc.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MerchantRateCard>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(mrc => mrc.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
```

After the existing `modelBuilder.Entity<MerchantKycDocument>().HasQueryFilter(...)` line, add:

```csharp
        modelBuilder.Entity<RateCard>().HasQueryFilter(rc => _currentTenantProvider.OrganizationId != null && rc.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<MerchantRateCard>().HasQueryFilter(mrc => _currentTenantProvider.OrganizationId != null && mrc.OrganizationId == _currentTenantProvider.OrganizationId);
```

After the existing "No tenant filter on Province/District/LocalLevel/Ward/Area" comment block, add:

```csharp
        // No tenant filter on ServiceType/WeightSlab: shared reference data, identical across
        // every tenant (same reasoning as Province/District/LocalLevel/Ward/Area above).

        // No tenant filter on ZoneRate/AdditionalCharge: neither carries its own OrganizationId —
        // tenant isolation flows transitively through RateCardId -> RateCard.OrganizationId
        // (which does have a filter, above). Every query against these two tables in
        // Pricing.Application scopes through a RateCard the caller's tenant already owns.
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
dotnet ef migrations add AddPricing --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj
dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj --connection "Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password"
```

Expected: a new migration file appears under `src/Persistence/Migrations/`; `database update` reports it applied `AddPricing` with no errors.

- [ ] **Step 3: Verify the new tables and seed rows exist**

```bash
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"ServiceTypes\""
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"WeightSlabs\""
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"RateCards\""
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"ZoneRates\""
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"AdditionalCharges\""
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"MerchantRateCards\""
docker exec courier-postgres-1 psql -U courier -d courier -c "SELECT \"Code\", \"Name\" FROM \"ServiceTypes\" ORDER BY \"Name\";"
docker exec courier-postgres-1 psql -U courier -d courier -c "SELECT \"MinWeightKg\", \"MaxWeightKg\" FROM \"WeightSlabs\" ORDER BY \"MinWeightKg\";"
```

Expected: each `\d` prints a column listing; the `SELECT`s print 5 service types and 6 weight slabs matching `PricingSeedData`.

- [ ] **Step 4: Verify the solution still builds and all tests pass**

```bash
dotnet build Courier.slnx
dotnet test Courier.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`; all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Persistence
git commit -m "feat: wire Pricing into AppDbContext with migration and seed data"
```

---

### Task 11: Api.Tests — End-to-End Rate Card, Quote, Versioning, and Tenant Isolation Tests

**Files:**
- Create: `tests/Api.Tests/PricingEndpointsTests.cs`

**Interfaces:**
- Consumes: every endpoint from Task 9, against the real Postgres-backed `AppDbContext` from Task 10.
- Produces: nothing further consumed — this is the exit-criteria proof for Phase 5.

- [ ] **Step 1: Write the tests**

`tests/Api.Tests/PricingEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class PricingEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public PricingEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12];
        var request = new RegisterOrganizationRequest(
            $"Pricing Org {suffix}", $"pricing-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private static CreateBranchRequest ValidBranchRequest(string code, Guid? deliveryZoneId = null) => new(
        code, $"Branch {code}", BranchType.Hub, KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null, deliveryZoneId);

    private static List<Guid> KathmanduWard1Areas() => new()
    {
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
    };

    private async Task<(Guid OriginBranchId, Guid DestinationBranchId, Guid ServiceTypeId)> SeedRoutableBranchesAsync(HttpClient client)
    {
        var originZoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Origin-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var originZone = await originZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        var destZoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Dest-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var destZone = await destZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();

        var originBranchResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"OZ-{Guid.NewGuid():N}"[..10], originZone!.Id));
        var originBranch = await originBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();
        var destBranchResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"DZ-{Guid.NewGuid():N}"[..10], destZone!.Id));
        var destBranch = await destBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var serviceTypesResponse = await client.GetAsync("/api/v1/service-types");
        var serviceTypes = await serviceTypesResponse.Content.ReadFromJsonAsync<List<ServiceTypeResponse>>();
        var standard = serviceTypes!.Single(s => s.Code == ServiceTypeCode.Standard);

        return (originBranch!.Id, destBranch!.Id, standard.Id);
    }

    private async Task<RateCardResponse> SeedRateCardWithZoneRateAsync(HttpClient client, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, decimal baseRate)
    {
        var originBranch = (await (await client.GetAsync($"/api/v1/branches/{originBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var destBranch = (await (await client.GetAsync($"/api/v1/branches/{destinationBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;

        var weightSlabsResponse = await client.GetAsync("/api/v1/weight-slabs");
        var weightSlabs = await weightSlabsResponse.Content.ReadFromJsonAsync<List<WeightSlabResponse>>();
        var slab = weightSlabs!.Single(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m);

        var rateCardResponse = await client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest($"Card-{Guid.NewGuid():N}", DateTimeOffset.UtcNow));
        var rateCard = await rateCardResponse.Content.ReadFromJsonAsync<RateCardResponse>();

        await client.PostAsJsonAsync($"/api/v1/rate-cards/{rateCard!.Id}/zone-rates",
            new CreateZoneRateRequest(originBranch.DeliveryZoneId!.Value, destBranch.DeliveryZoneId!.Value, serviceTypeId, slab.Id, baseRate));

        return rateCard;
    }

    [Fact]
    public async Task Quote_FullRoundTrip_ReturnsExpectedTotal()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "quote");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardWithZoneRateAsync(client, originBranchId, destinationBranchId, serviceTypeId, 150m);

        var response = await client.PostAsJsonAsync("/api/v1/pricing/quote",
            new PriceQuoteRequest(originBranchId, destinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<PriceQuoteResponse>();
        Assert.Equal(150m, quote!.Total);
    }

    [Fact]
    public async Task Quote_NoRateDefined_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "quote-missing");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        // No rate card created at all for this org.

        var response = await client.PostAsJsonAsync("/api/v1/pricing/quote",
            new PriceQuoteRequest(originBranchId, destinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RateCardVersioning_SupersededCardRowsUnchanged_NewQuoteUsesNewNumbers()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "version");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        var v1 = await SeedRateCardWithZoneRateAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);

        var v1QuoteBefore = await (await client.PostAsJsonAsync("/api/v1/pricing/quote",
            new PriceQuoteRequest(originBranchId, destinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null)))
            .Content.ReadFromJsonAsync<PriceQuoteResponse>();
        Assert.Equal(100m, v1QuoteBefore!.Total);

        var v2 = await SeedRateCardWithZoneRateAsync(client, originBranchId, destinationBranchId, serviceTypeId, 175m);
        Assert.Equal(v1.Version + 1, v2.Version);

        // Old card's own zone-rates are unchanged (never mutated) — proves immutability, not just
        // that a new quote picks up new numbers.
        var v1ZoneRatesAfter = await (await client.GetAsync($"/api/v1/rate-cards/{v1.Id}/zone-rates")).Content.ReadFromJsonAsync<List<ZoneRateResponse>>();
        Assert.Single(v1ZoneRatesAfter!);
        Assert.Equal(100m, v1ZoneRatesAfter![0].BaseRate);

        var v1After = await (await client.GetAsync($"/api/v1/rate-cards/{v1.Id}")).Content.ReadFromJsonAsync<RateCardResponse>();
        Assert.False(v1After!.IsActive);

        var newQuote = await (await client.PostAsJsonAsync("/api/v1/pricing/quote",
            new PriceQuoteRequest(originBranchId, destinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null)))
            .Content.ReadFromJsonAsync<PriceQuoteResponse>();
        Assert.Equal(175m, newQuote!.Total);
        Assert.Equal(v2.Id, newQuote.RateCardId);
    }

    [Fact]
    public async Task AddZoneRate_OnSupersededRateCard_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "superseded-write");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        var v1 = await SeedRateCardWithZoneRateAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        await SeedRateCardWithZoneRateAsync(client, originBranchId, destinationBranchId, serviceTypeId, 200m);

        var originBranch = (await (await client.GetAsync($"/api/v1/branches/{originBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var destBranch = (await (await client.GetAsync($"/api/v1/branches/{destinationBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var slab = (await (await client.GetAsync("/api/v1/weight-slabs")).Content.ReadFromJsonAsync<List<WeightSlabResponse>>())!.Single(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m);

        var response = await client.PostAsJsonAsync($"/api/v1/rate-cards/{v1.Id}/zone-rates",
            new CreateZoneRateRequest(originBranch.DeliveryZoneId!.Value, destBranch.DeliveryZoneId!.Value, serviceTypeId, slab.Id, 999m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MerchantOverride_UsedInsteadOfOrgDefault_DiscountApplied()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "merchant-override");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardWithZoneRateAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantRateCard = await SeedRateCardWithZoneRateAsync(client, originBranchId, destinationBranchId, serviceTypeId, 300m); // becomes org default (v2)

        var merchantResponse = await client.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Merchant Override Pvt. Ltd.", "Merchant Override", "01-4444444", "override@example.com",
            $"override-owner-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        var merchant = await merchantResponse.Content.ReadFromJsonAsync<MerchantResponse>();

        await client.PutAsJsonAsync($"/api/v1/merchants/{merchant!.Id}/rate-card", new UpsertMerchantRateCardRequest(merchantRateCard.Id, 20m));

        var quoteWithMerchant = await (await client.PostAsJsonAsync("/api/v1/pricing/quote",
            new PriceQuoteRequest(originBranchId, destinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, merchant.Id)))
            .Content.ReadFromJsonAsync<PriceQuoteResponse>();

        Assert.Equal(240m, quoteWithMerchant!.Total); // 300 - 20%
    }

    [Fact]
    public async Task RateCards_OrgBCannotSeeOrgAsRateCards()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "iso-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(clientA);
        var rateCardA = await SeedRateCardWithZoneRateAsync(clientA, originBranchId, destinationBranchId, serviceTypeId, 100m);

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "iso-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var listResponse = await clientB.GetAsync("/api/v1/rate-cards");
        var rateCards = await listResponse.Content.ReadFromJsonAsync<List<RateCardResponse>>();
        Assert.DoesNotContain(rateCards!, rc => rc.Id == rateCardA.Id);

        var getResponse = await clientB.GetAsync($"/api/v1/rate-cards/{rateCardA.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task PostRateCards_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest("Default", DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter PricingEndpointsTests -v n`
Expected: PASS (8 tests). If `RateCardVersioning_SupersededCardRowsUnchanged_NewQuoteUsesNewNumbers` or `AddZoneRate_OnSupersededRateCard_Returns400` fail, re-check `RateCardService.CreateRateCardAsync`'s previous-row update actually persists before the second `SeedRateCardWithZoneRateAsync` call reads it back — both run against the real Postgres container so there's no InMemory-provider timing quirk to chase, but do verify `SaveChangesAsync` is awaited before the response is returned (systematic-debugging skill applies if this doesn't come up green on the first real run).

- [ ] **Step 3: Verify the full solution builds and all tests pass**

```bash
dotnet build Courier.slnx
dotnet test Courier.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`; all tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/Api.Tests/PricingEndpointsTests.cs
git commit -m "test: add end-to-end pricing quote, versioning, and tenant isolation tests"
```

---

### Task 12: Update ROADMAP.md Status

**Files:**
- Modify: `docs/ROADMAP.md`

**Interfaces:**
- Consumes: nothing.
- Produces: an updated Phase 5 status note, mirroring Phases 1-4's, so Phase 6's "Entry" check has a clear record of what's actually done.

- [ ] **Step 1: Add the status note**

In `docs/ROADMAP.md`, immediately after the existing Phase 5 paragraph:

```text
## Phase 5 — Rate Cards, Pricing Engine

`RateCard, RateCardRule, ServiceType, WeightSlab, Zone, ZoneRate, AdditionalCharge,
MerchantRateCard`, versioning (CLAUDE.md §16).
*Exit:* a price can be computed for a given origin/destination/weight/service/COD/
insurance/merchant combination, and is provably versioned (changing today's rate
does not alter a previously computed quote).
```

add:

```text

*Status (2026-08-20): Complete.* A new `Pricing` module implements `ServiceType, WeightSlab,
RateCard, ZoneRate, AdditionalCharge, MerchantRateCard` per `DOMAIN-MODEL.md` — with two
deliberate simplifications from the original design: no `RateCardRule` entity (the engine queries
`ZoneRate`/`AdditionalCharge` directly by `RateCardId`, since neither needed an intermediate rule
layer), and no separate `Pricing.Zone` entity (`ZoneRate` FKs directly into `Geography.DeliveryZone`,
which already had the identical shape). `Branch` gained a new ops-assigned `DeliveryZoneId`
(`PUT /api/v1/branches/{id}/delivery-zone`) to resolve a shipment's origin/destination to a rating
zone. Versioning is enforced by construction, not convention: `POST /api/v1/rate-cards` supersedes
the org's current default (`Version + 1`, previous row flips to `IsActive = false`), and no
`PUT`/`PATCH`/`DELETE` exists anywhere on `RateCard`/`ZoneRate`/`AdditionalCharge` — proven by
`tests/Api.Tests/PricingEndpointsTests.cs`'s `RateCardVersioning_SupersededCardRowsUnchanged_NewQuoteUsesNewNumbers`
(old rows byte-for-byte unchanged after supersession, new quote uses new numbers) and
`AddZoneRate_OnSupersededRateCard_Returns400`. `POST /api/v1/pricing/quote` resolves a rate card
(merchant override via `MerchantRateCard` if present, else the org default), weight slab, and zone
pair, then returns a fully itemized result (base rate, each applied charge, discount, total) —
`MerchantOverride_UsedInsteadOfOrgDefault_DiscountApplied` proves the override path.
`RateCard`/`MerchantRateCard` are tenant-isolated (`RateCards_OrgBCannotSeeOrgAsRateCards`). Every
cross-module read (`Branch` zone data, `Merchant` existence, `DeliveryZone` existence) goes through
a small Application-layer interface (`IBranchLookupService`, `IMerchantLookupService`,
`IGeographyLookupService.DeliveryZoneExistsAsync`) rather than a direct cross-module `DbSet` query.
Deferred to later phases: quote persistence (Phase 6 will snapshot the resolved charge fields onto
`Shipment`), a "clone previous version" convenience endpoint, org-configurable `WeightSlab`, and
`AsOf`/historical-timestamp quote resolution.
```

- [ ] **Step 2: Commit**

```bash
git add docs/ROADMAP.md
git commit -m "docs: mark Phase 5 exit criteria met"
```

---

## Plan Self-Review Notes

- **Spec coverage:** All six Pricing entities (Tasks 5-6), the two cross-module lookup interfaces plus `Branch.DeliveryZoneId` (Tasks 2-4), the full CRUD+versioning surface (Task 7), the calculation engine (Task 8), all 13 endpoints (Task 9), persistence wiring with seed data (Task 10), and the exit-criteria proof itself (Task 11's `RateCardVersioning_SupersededCardRowsUnchanged_NewQuoteUsesNewNumbers`) are all covered. `docs/ROADMAP.md` Phase 5's exact exit wording ("a price can be computed... and is provably versioned") maps directly onto that one test plus `Quote_FullRoundTrip_ReturnsExpectedTotal`.
- **Placeholder scan:** No TBD/TODO markers; every code step contains complete, compilable-intent code, not a description of code. The one place this plan says "check the existing file and adjust names" (Task 4 Step 8, Task 11's helper reuse) is deliberate — those existing fixture/helper names genuinely aren't known without opening files this plan's author didn't have open at those exact points, and the instruction is specific about what to look for and why, not a vague "handle it."
- **Type consistency:** `PriceQuoteRequest`/`PriceQuoteResponse`/`AppliedCharge` field names and order match exactly between Task 8's definition, `PricingService.QuoteAsync`'s construction, and Task 11's test assertions. `RateCardResponse.Version`/`IsActive`/`EffectiveTo` match between Task 7's `RateCardService` and Task 11's versioning test. `BranchZoneInfo(Guid BranchId, Guid? DeliveryZoneId)` matches between Task 4's definition, `PricingService`'s consumption, and Task 8's test fake. `IGeographyLookupService.DeliveryZoneExistsAsync(Guid zoneId, ...)` (no `organizationId` parameter — tenant scoping is implicit via the query filter) is used consistently in Task 4's `BranchService` and Task 7's `RateCardService`.
- **Scope-exclusion decisions:** No `RateCardRule`, no `Pricing.Zone`, no quote persistence, no org-configurable `WeightSlab`, no `AsOf` resolution — all four were explicitly approved by the user in this session's brainstorming conversation before this plan was written, not invented here.
- **A judgment call worth flagging to the plan's reviewer:** Task 9's endpoint permission split (`pricing.read` for the quote endpoint, not a narrower `pricing.quote`) reuses the two permissions that already existed in `PermissionCatalog.cs` since Phase 1, per the approved design — this means any staff member who can read pricing data can also compute quotes, which is the intended behavior (quoting is a read-shaped operation) but worth a second look during review since it's a slightly unusual reuse of a "read" permission for what triggers real computation (no side effects, though — no state is written).
