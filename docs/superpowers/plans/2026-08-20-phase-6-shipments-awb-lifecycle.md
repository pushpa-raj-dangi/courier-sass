# Phase 6 — Shipments, AWB, Lifecycle, Tracking Events Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Full happy-path shipment lifecycle (`CREATED` → `DELIVERED`) works with correct append-only `ShipmentEvent`s and locked pricing (`docs/ROADMAP.md` Phase 6 exit criteria).

**Architecture:** A new `Shipments` module (`src/Modules/Shipments/Shipments.csproj`) owns `Shipment, ShipmentItem, ShipmentEvent`, following the `Domain/Application/Infrastructure/Presentation` shape already used by every prior module. `Shipments` takes project references to `SharedKernel, Audit, Geography, Branches, Merchants, Pricing` — the widest reference graph of any module so far, because booking a shipment legitimately needs a rating zone (`Geography`/`Branches`), a merchant identity (`Merchants`), and a computed price (`Pricing`). Unlike Phase 5's cross-module reads (which went through small lookup interfaces to avoid touching another module's `DbSet`), `Shipments` calls `Pricing.Application.PricingService.QuoteAsync` **directly** as an Application-layer dependency — this is not a data-leak risk like `ZoneRate`/`AdditionalCharge` was, because `QuoteAsync`'s entire job *is* "compute a price for a shipment"; there is no narrower interface to interpose. `Shipments` still goes through `IBranchLookupService`/`IMerchantLookupService`/`IGeographyLookupService.IsValidHierarchyAsync` for existence/validation checks on Branch/Merchant/geography rows, exactly like every prior module.

`docs/SHIPMENT-LIFECYCLE.md`'s full transition table is normally driven by `Manifest` (Phase 7) and `Rider`/`DeliveryAttempt`/`DeliveryProof` (Phase 8) operations, none of which exist yet. This phase drives the full `Created → Delivered` path via **direct staff-triggered transition endpoints**, gated by the exact permissions the real Phase 7/8 operations will eventually use (`shipment.assign/scan/sort/transfer`, `delivery.assign/complete`) — this is not throwaway code; Phase 7/8 will build real `Manifest`/`Rider` mechanics around these same permission checks and are expected to supersede/extend these endpoints, particularly `deliver` (Phase 8 adds real POD — OTP/signature/photo).

No `PUT`/`PATCH`/`DELETE` exists anywhere on `ShipmentEvent` — every transition writes exactly one new append-only row, mirroring `RateCard`/`ZoneRate`/`AdditionalCharge`'s no-mutation precedent from Phase 5.

**Scope deliberately excluded from this phase:** `Manifest` entity / real barcode-driven scan validation (Phase 7 — this phase's `scan`-named endpoints are direct staff-triggered status writes gated by the `shipment.scan` permission, not actual barcode/manifest validation); `Rider`/`DeliveryTask`/`DeliveryAttempt`/`DeliveryProof` entities (Phase 8 — `pickup-assign`/`out-for-delivery` take a plain `AssignedUserId`, not a `Rider`; `deliver` takes plain fields, not OTP/signature/photo); `NDR`/`RTO`/`DELIVERY_FAILED` transition (Phase 9 — a failed delivery attempt isn't representable this phase, `OutForDelivery → Delivered` is the only path out of `OutForDelivery`); `ShipmentEvent.Latitude`/`Longitude` population (columns exist per `DOMAIN-MODEL.md`'s schema but nothing populates them yet — GPS-from-rider-app territory, Phase 8); webhooks/notifications firing on shipment events (Phase 11); public `/track/{awb}` endpoint (Phase 12 — `GET /shipments/{awb}` added this phase is the *internal* staff/merchant endpoint, not the public one; a later phase must build a public-safe projection before repurposing it, not this phase's concern).

**Tech Stack:** Same as Phases 1-5 — EF Core 10 + Npgsql, xUnit + `Microsoft.EntityFrameworkCore.InMemory` for unit tests, `Microsoft.AspNetCore.Mvc.Testing` for API-level integration tests against the real Postgres container from `docker-compose.yml` (the shared `courier-postgres-1` container on port 5434 — reuse it, do not start a second one). Manual exception-based validation (`ShipmentValidationException`), not FluentValidation — confirmed unused anywhere in this codebase across Phases 1-5.

**Spec:** `docs/ROADMAP.md` (Phase 6), `docs/SHIPMENT-LIFECYCLE.md`, `docs/DOMAIN-MODEL.md` (Shipments & Tracking section, updated by Task 1), `docs/API-SPEC.md` §`SHIPMENT_INVALID_TRANSITION` error shape, `CLAUDE.md` §9-12 — this project folds the design directly into this plan doc rather than a separate `docs/superpowers/specs/` file (established convention across Phases 0-5); the full design was agreed section-by-section with the user in this session's brainstorming conversation before this plan was written.

## Global Constraints

- Every tenant-owned entity gets a non-nullable `OrganizationId`; every query is scoped server-side — never trust an id from the client without checking it resolves within the caller's tenant.
- A module reaches another module's data only through that module's Application-layer interface — except `Pricing.Application.PricingService`, which `Shipments` calls directly (see Architecture above for why this specific case is not the `ZoneRate`/`AdditionalCharge` anti-pattern).
- No `PUT`/`PATCH`/`DELETE` on `ShipmentEvent` — ever. Every transition inserts exactly one new row.
- Every money column: `HasPrecision(18, 2)`. Weight column: `HasPrecision(10, 3)` (matches `WeightSlab`). Dimension columns (`LengthCm`/`WidthCm`/`HeightCm`): `HasPrecision(10, 2)`.
- Manual exception-based validation (`ShipmentValidationException`), not FluentValidation.
- `DeclaredValue` is always server-computed as `sum(ShipmentItem.Quantity * UnitValue)` — never accepted directly from the client.
- `DeliveryCharge`/`InsuranceCharge`/`OtherCharges`/`TotalCharge` are set exactly once at booking from `PricingService.QuoteAsync` and never recalculated afterward, even if the `RateCard` changes later.
- Reuse existing `shipment.create/read/update/cancel/assign/scan/sort/transfer` and `delivery.assign/complete` permissions from `PermissionCatalog.cs` for staff callers — no staff catalog changes. Merchant callers use three **new** constants added to `MerchantPermissionCatalog` in Task 2, using the identical string values as their staff counterparts (`"shipment.create"`, `"shipment.read"`, `"shipment.cancel"`) — same key, two independent catalogs/checkers, exactly how Phase 4 designed the two authorization tracks to never need to know about each other.

---

### Task 1: Update DOMAIN-MODEL.md and Scaffold the Shipments Module

**Files:**
- Modify: `docs/DOMAIN-MODEL.md`
- Create: `src/Modules/Shipments/Shipments.csproj`
- Create: `tests/Shipments.Tests/Shipments.Tests.csproj`
- Modify: `Courier.slnx`
- Modify: `src/Persistence/Persistence.csproj` (add reference to Shipments)
- Modify: `src/Api/Api.csproj` (add reference to Shipments)

**Interfaces:**
- Consumes: nothing (first task).
- Produces: an empty-but-building `Shipments` project wired into the solution with the reference graph `SharedKernel, Audit, Geography, Branches, Merchants, Pricing ← Shipments`, `..., Shipments ← Persistence`, `..., Shipments ← Api`. No cycles. `Api.Tests` gets transitive access through `Api.csproj`, so it needs no direct reference (same as `Pricing.Tests` in Phase 5).

- [ ] **Step 1: Update the Shipments & Tracking section of DOMAIN-MODEL.md**

Replace the existing `## Shipments & Tracking` section (currently reading `Shipment { Id (internal UUID), Awb ... }` through the `ShipmentEvent { ... }` closing fence and the Custody note) with:

```text
## Shipments & Tracking — `OrganizationId` present

```text
Shipment { Id (internal UUID), Awb (business identifier, unique per (OrganizationId, Awb),
             format {OrgSlugPrefix}{yyyyMMdd}{6-digit sequence}, e.g. ACME20260820000001),
           OrganizationId, MerchantId,
           OriginBranchId, DestinationBranchId, CurrentBranchId (nullable),
           CurrentCustodianUserId (nullable), CurrentCustodianRiderId (nullable — always
             null until Phase 8 introduces Rider; kept as a real column now so Phase 8
             needs no migration to add it),
           CurrentStatus (Created|PickupAssigned|PickedUp|AtOriginBranch|Sorted|InTransit|
             AtDestinationBranch|OutForDelivery|Delivered|Cancelled — NDR/RTO states added
             Phase 9), ServiceTypeId,
           WeightKg, LengthCm (nullable), WidthCm (nullable), HeightCm (nullable),
           DeclaredValue (= sum of ShipmentItem.Quantity*UnitValue, server-computed),
           CodAmount, WantsInsurance,
           DeliveryCharge, InsuranceCharge, OtherCharges, TotalCharge,
             # ^ all four snapshotted from PricingService.QuoteAsync at booking time and
             # never recalculated — DeliveryCharge+InsuranceCharge+OtherCharges equal the
             # pre-discount Subtotal; TotalCharge is the actual post-discount amount, so the
             # three charge fields will not sum to TotalCharge when a merchant discount
             # applied (expected, not a bug — every component stays individually traceable
             # per CLAUDE.md §23)
           RecipientName, RecipientPhone, RecipientProvinceId, RecipientDistrictId,
             RecipientLocalLevelId, RecipientWardNumber, RecipientAddressLine,
           SenderName, SenderPhone, SenderProvinceId, SenderDistrictId, SenderLocalLevelId,
             SenderWardNumber, SenderAddressLine }

ShipmentItem { Id, ShipmentId, Description, Quantity, UnitValue }

ShipmentEvent { Id, ShipmentId, EventType (Created|PickupAssigned|PickedUp|
                ReceivedAtOrigin|Sorted|Dispatched|ReceivedAtDestination|OutForDelivery|
                Delivered|Cancelled), PreviousStatus (nullable — null for the Created
                event), NewStatus, BranchId (nullable), UserId (nullable), RiderId
                (nullable — always null this phase), Timestamp, Latitude (nullable),
                Longitude (nullable), Notes (nullable), Metadata (nullable) }
                # append-only, never updated/deleted
```

**Custody note:** `CurrentCustodianId` is polymorphic (a branch-based staff user or a
rider) in practice modeled as two nullable FKs (`CurrentCustodianUserId`,
`CurrentCustodianRiderId`) with exactly one non-null at a time, rather than a true
polymorphic FK — kept explicit for referential integrity rather than using a
type+id pair.

**RecipientAddress/SenderAddress** are structured geography FKs (Province/District/
LocalLevel/WardNumber + AddressLine), the identical shape `Branch`/`MerchantAddress` already
use — not the single free-text field an earlier draft of this section implied.
```

- [ ] **Step 2: Update the Relationship Summary**

In `docs/DOMAIN-MODEL.md`'s `## Relationship Summary` block, after the existing `Merchant 1─* Shipment *─1 Branch (origin/destination/current)` line, add:

```text
Shipment 1─* ShipmentItem
Shipment 1─* ShipmentEvent
Shipment *─1 Pricing.ServiceType
```

(The `Shipment 1─* ShipmentItem` / `Shipment 1─* ShipmentEvent` lines already exist a few lines down in that block from the original sketch — check first; only add whichever of these three lines are genuinely missing, do not duplicate.)

- [ ] **Step 3: Create the two projects**

```bash
dotnet new classlib -n Shipments -o src/Modules/Shipments
dotnet new xunit -n Shipments.Tests -o tests/Shipments.Tests
dotnet sln Courier.slnx add src/Modules/Shipments/Shipments.csproj tests/Shipments.Tests/Shipments.Tests.csproj
```

Delete the template `src/Modules/Shipments/Class1.cs` and `tests/Shipments.Tests/UnitTest1.cs` — both projects start empty.

- [ ] **Step 4: Add the ASP.NET Core framework reference and EF packages**

```bash
dotnet add src/Modules/Shipments/Shipments.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Modules/Shipments/Shipments.csproj package Microsoft.EntityFrameworkCore.Relational
dotnet add tests/Shipments.Tests/Shipments.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory
```

In `src/Modules/Shipments/Shipments.csproj`, add inside a sibling `<ItemGroup>`:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

- [ ] **Step 5: Wire project references**

```bash
dotnet add src/Modules/Shipments/Shipments.csproj reference src/SharedKernel/SharedKernel.csproj
dotnet add src/Modules/Shipments/Shipments.csproj reference src/Modules/Audit/Audit.csproj
dotnet add src/Modules/Shipments/Shipments.csproj reference src/Modules/Geography/Geography.csproj
dotnet add src/Modules/Shipments/Shipments.csproj reference src/Modules/Branches/Branches.csproj
dotnet add src/Modules/Shipments/Shipments.csproj reference src/Modules/Merchants/Merchants.csproj
dotnet add src/Modules/Shipments/Shipments.csproj reference src/Modules/Pricing/Pricing.csproj
dotnet add src/Persistence/Persistence.csproj reference src/Modules/Shipments/Shipments.csproj
dotnet add src/Api/Api.csproj reference src/Modules/Shipments/Shipments.csproj
dotnet add tests/Shipments.Tests/Shipments.Tests.csproj reference src/Modules/Shipments/Shipments.csproj
```

- [ ] **Step 6: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (pre-existing NU1510/CS8602 warnings from earlier phases are fine; no new ones).

- [ ] **Step 7: Commit**

```bash
git add docs/DOMAIN-MODEL.md Courier.slnx src/Modules/Shipments tests/Shipments.Tests src/Persistence/Persistence.csproj src/Api/Api.csproj
git commit -m "docs: update domain model for Phase 6; scaffold Shipments module project"
```

---

### Task 2: Merchants — Add Shipment Permissions to MerchantPermissionCatalog

**Files:**
- Modify: `src/Modules/Merchants/Application/MerchantPermissionCatalog.cs`
- Test: `tests/Merchants.Tests/MerchantPermissionCatalogTests.cs` (create if it doesn't exist; if a test file for this catalog already exists, extend it instead)

**Interfaces:**
- Consumes: nothing new.
- Produces: `MerchantPermissionCatalog.ShipmentCreate = "shipment.create"`, `.ShipmentRead = "shipment.read"`, `.ShipmentCancel = "shipment.cancel"` — Task 6 (`ShipmentService`) and Task 8 (Presentation) consume these constants for merchant-JWT authorization checks. Both `Owner` and `Staff` merchant roles get all three (a merchant's staff members are the ones doing day-to-day booking in practice; RBAC.md doesn't split shipment access between Owner/Staff, so granting both is the more useful default — flagged as a judgment call, easy to narrow later if wrong).

- [ ] **Step 1: Write the failing test**

`tests/Merchants.Tests/MerchantPermissionCatalogTests.cs`:

```csharp
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Xunit;

namespace Courier.Merchants.Tests;

public class MerchantPermissionCatalogTests
{
    [Theory]
    [InlineData(MerchantUserRole.Owner)]
    [InlineData(MerchantUserRole.Staff)]
    public void RolePermissions_BothRoles_IncludeAllThreeShipmentPermissions(MerchantUserRole role)
    {
        var permissions = MerchantPermissionCatalog.RolePermissions[role];

        Assert.Contains(MerchantPermissionCatalog.ShipmentCreate, permissions);
        Assert.Contains(MerchantPermissionCatalog.ShipmentRead, permissions);
        Assert.Contains(MerchantPermissionCatalog.ShipmentCancel, permissions);
    }

    [Fact]
    public void ShipmentPermissionConstants_MatchStaffPermissionKeys()
    {
        // Deliberately the same string values as the staff PermissionCatalog's
        // shipment.create/read/cancel — two independent catalogs/checkers, same key names,
        // by design (see this plan's Global Constraints).
        Assert.Equal("shipment.create", MerchantPermissionCatalog.ShipmentCreate);
        Assert.Equal("shipment.read", MerchantPermissionCatalog.ShipmentRead);
        Assert.Equal("shipment.cancel", MerchantPermissionCatalog.ShipmentCancel);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter MerchantPermissionCatalogTests -v n`
Expected: FAIL — `ShipmentCreate`/`ShipmentRead`/`ShipmentCancel` don't exist.

- [ ] **Step 3: Add the constants and wire them into both role lists**

In `src/Modules/Merchants/Application/MerchantPermissionCatalog.cs`, replace the full file contents:

```csharp
using Courier.Merchants.Domain;

namespace Courier.Merchants.Application;

// Merchant-facing permissions are a small, fixed catalog — unlike staff Roles, merchants cannot
// define custom roles in v1 (RBAC.md §5). Owner holds every merchant permission; Staff holds a
// fixed subset with no staff-management visibility.
public static class MerchantPermissionCatalog
{
    public const string ProfileRead = "merchant.profile.read";
    public const string ProfileUpdate = "merchant.profile.update";
    public const string StaffManage = "merchant.staff.manage";
    public const string KycUpload = "merchant.kyc.upload";
    // Same string values as the staff PermissionCatalog's shipment.create/read/cancel —
    // two independent catalogs/checkers (MerchantAuthorizationChecker vs PermissionChecker),
    // same key names by design (ROADMAP.md Phase 6).
    public const string ShipmentCreate = "shipment.create";
    public const string ShipmentRead = "shipment.read";
    public const string ShipmentCancel = "shipment.cancel";

    private static readonly string[] OwnerPermissions =
    {
        ProfileRead, ProfileUpdate, StaffManage, KycUpload, ShipmentCreate, ShipmentRead, ShipmentCancel
    };

    private static readonly string[] StaffPermissions =
    {
        ProfileRead, KycUpload, ShipmentCreate, ShipmentRead, ShipmentCancel
    };

    public static readonly IReadOnlyDictionary<MerchantUserRole, IReadOnlySet<string>> RolePermissions =
        new Dictionary<MerchantUserRole, IReadOnlySet<string>>
        {
            [MerchantUserRole.Owner] = OwnerPermissions.ToHashSet(),
            [MerchantUserRole.Staff] = StaffPermissions.ToHashSet()
        };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter MerchantPermissionCatalogTests -v n`
Expected: PASS (3 tests).

- [ ] **Step 5: Verify the solution builds and existing Merchants tests still pass**

```bash
dotnet build Courier.slnx
dotnet test tests/Merchants.Tests/Merchants.Tests.csproj
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`; all Merchants.Tests pass (existing tests shouldn't be affected by additive permission constants).

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Merchants/Application/MerchantPermissionCatalog.cs tests/Merchants.Tests/MerchantPermissionCatalogTests.cs
git commit -m "feat: add shipment permissions to MerchantPermissionCatalog"
```

---

### Task 3: Shipments Domain — Entities and Enums

**Files:**
- Create: `src/Modules/Shipments/Domain/ShipmentStatus.cs`
- Create: `src/Modules/Shipments/Domain/ShipmentEventType.cs`
- Create: `src/Modules/Shipments/Domain/Shipment.cs`
- Create: `src/Modules/Shipments/Domain/ShipmentItem.cs`
- Create: `src/Modules/Shipments/Domain/ShipmentEvent.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: all three entity types and two enums — every later task in this module consumes these exact shapes.

- [ ] **Step 1: Write the enums**

`src/Modules/Shipments/Domain/ShipmentStatus.cs`:

```csharp
namespace Courier.Shipments.Domain;

// NDR/RTO states (RtoInitiated, RtoInTransit, etc.) are added in Phase 9 — this phase only
// models the happy path plus Cancelled (ROADMAP.md Phase 6).
public enum ShipmentStatus
{
    Created,
    PickupAssigned,
    PickedUp,
    AtOriginBranch,
    Sorted,
    InTransit,
    AtDestinationBranch,
    OutForDelivery,
    Delivered,
    Cancelled
}
```

`src/Modules/Shipments/Domain/ShipmentEventType.cs`:

```csharp
namespace Courier.Shipments.Domain;

public enum ShipmentEventType
{
    Created,
    PickupAssigned,
    PickedUp,
    ReceivedAtOrigin,
    Sorted,
    Dispatched,
    ReceivedAtDestination,
    OutForDelivery,
    Delivered,
    Cancelled
}
```

- [ ] **Step 2: Write the entities**

`src/Modules/Shipments/Domain/Shipment.cs`:

```csharp
namespace Courier.Shipments.Domain;

// All charge fields are snapshotted once at booking from PricingService.QuoteAsync and never
// recalculated (CLAUDE.md §16) — a later RateCard version change must not alter this shipment's
// price. CurrentCustodianRiderId is always null until Phase 8 introduces Rider; kept as a real
// column now so that phase needs no migration to add it (see this plan's Architecture section).
public class Shipment
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Awb { get; set; }
    public Guid MerchantId { get; set; }
    public Guid OriginBranchId { get; set; }
    public Guid DestinationBranchId { get; set; }
    public Guid? CurrentBranchId { get; set; }
    public Guid? CurrentCustodianUserId { get; set; }
    public Guid? CurrentCustodianRiderId { get; set; }
    public ShipmentStatus CurrentStatus { get; set; }
    public Guid ServiceTypeId { get; set; }

    public decimal WeightKg { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }

    public decimal DeclaredValue { get; set; }
    public decimal CodAmount { get; set; }
    public bool WantsInsurance { get; set; }

    public decimal DeliveryCharge { get; set; }
    public decimal InsuranceCharge { get; set; }
    public decimal OtherCharges { get; set; }
    public decimal TotalCharge { get; set; }

    public required string RecipientName { get; set; }
    public required string RecipientPhone { get; set; }
    public required Guid RecipientProvinceId { get; set; }
    public required Guid RecipientDistrictId { get; set; }
    public required Guid RecipientLocalLevelId { get; set; }
    public required int RecipientWardNumber { get; set; }
    public required string RecipientAddressLine { get; set; }

    public required string SenderName { get; set; }
    public required string SenderPhone { get; set; }
    public required Guid SenderProvinceId { get; set; }
    public required Guid SenderDistrictId { get; set; }
    public required Guid SenderLocalLevelId { get; set; }
    public required int SenderWardNumber { get; set; }
    public required string SenderAddressLine { get; set; }
}
```

`src/Modules/Shipments/Domain/ShipmentItem.cs`:

```csharp
namespace Courier.Shipments.Domain;

public class ShipmentItem
{
    public Guid Id { get; set; }
    public Guid ShipmentId { get; set; }
    public required string Description { get; set; }
    public int Quantity { get; set; }
    public decimal UnitValue { get; set; }
}
```

`src/Modules/Shipments/Domain/ShipmentEvent.cs`:

```csharp
namespace Courier.Shipments.Domain;

// Append-only — no update/delete anywhere in the API surface. PreviousStatus is null only for
// the very first (Created) event. RiderId is always null this phase (no Rider entity yet).
public class ShipmentEvent
{
    public Guid Id { get; set; }
    public Guid ShipmentId { get; set; }
    public ShipmentEventType EventType { get; set; }
    public ShipmentStatus? PreviousStatus { get; set; }
    public ShipmentStatus NewStatus { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? RiderId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? Notes { get; set; }
    public string? Metadata { get; set; }
}
```

- [ ] **Step 3: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Shipments/Domain
git commit -m "feat: add Shipments domain entities"
```

---

### Task 4: Shipments Infrastructure — EF Configurations

**Files:**
- Create: `src/Modules/Shipments/Infrastructure/ShipmentConfiguration.cs`
- Create: `src/Modules/Shipments/Infrastructure/ShipmentItemConfiguration.cs`
- Create: `src/Modules/Shipments/Infrastructure/ShipmentEventConfiguration.cs`

**Interfaces:**
- Consumes: `Shipments.Domain` entities (Task 3), `Geography.Domain`/`Branches.Domain`/`Merchants.Domain`/`Pricing.Domain` types (Task 1's references).
- Produces: three `IEntityTypeConfiguration<T>` classes — Task 9's `AppDbContext` wiring applies these.

- [ ] **Step 1: Write ShipmentConfiguration**

`src/Modules/Shipments/Infrastructure/ShipmentConfiguration.cs`:

```csharp
using Courier.Branches.Domain;
using Courier.Geography.Domain;
using Courier.Merchants.Domain;
using Courier.Pricing.Domain;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Shipments.Infrastructure;

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipments");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Awb).HasMaxLength(30).IsRequired();
        builder.Property(s => s.RecipientName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.RecipientPhone).HasMaxLength(30).IsRequired();
        builder.Property(s => s.RecipientAddressLine).HasMaxLength(500).IsRequired();
        builder.Property(s => s.SenderName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.SenderPhone).HasMaxLength(30).IsRequired();
        builder.Property(s => s.SenderAddressLine).HasMaxLength(500).IsRequired();

        builder.Property(s => s.WeightKg).HasPrecision(10, 3);
        builder.Property(s => s.LengthCm).HasPrecision(10, 2);
        builder.Property(s => s.WidthCm).HasPrecision(10, 2);
        builder.Property(s => s.HeightCm).HasPrecision(10, 2);
        builder.Property(s => s.DeclaredValue).HasPrecision(18, 2);
        builder.Property(s => s.CodAmount).HasPrecision(18, 2);
        builder.Property(s => s.DeliveryCharge).HasPrecision(18, 2);
        builder.Property(s => s.InsuranceCharge).HasPrecision(18, 2);
        builder.Property(s => s.OtherCharges).HasPrecision(18, 2);
        builder.Property(s => s.TotalCharge).HasPrecision(18, 2);

        // Every tenant-filtered query hits this column.
        builder.HasIndex(s => s.OrganizationId);
        // AWB is unique per org (DOMAIN-MODEL.md) — this is also the index
        // ShipmentService.GenerateAwbAsync's collision-retry relies on catching a violation of.
        builder.HasIndex(s => new { s.OrganizationId, s.Awb }).IsUnique();
        builder.HasIndex(s => s.MerchantId);

        // Cross-module FKs: Shipments has project references to Branches/Merchants/Pricing/
        // Geography (Task 1), so these are configured directly here, mirroring how
        // Merchants/Branches configure their own Geography FKs.
        builder.HasOne<Branch>().WithMany().HasForeignKey(s => s.OriginBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(s => s.DestinationBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(s => s.CurrentBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(s => s.MerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ServiceType>().WithMany().HasForeignKey(s => s.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Province>().WithMany().HasForeignKey(s => s.RecipientProvinceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<District>().WithMany().HasForeignKey(s => s.RecipientDistrictId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalLevel>().WithMany().HasForeignKey(s => s.RecipientLocalLevelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Province>().WithMany().HasForeignKey(s => s.SenderProvinceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<District>().WithMany().HasForeignKey(s => s.SenderDistrictId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalLevel>().WithMany().HasForeignKey(s => s.SenderLocalLevelId).OnDelete(DeleteBehavior.Restrict);

        // No FK to Organization/User here: Shipments has no project reference to Identity —
        // wired centrally in Persistence/AppDbContext.OnModelCreating instead (Task 9).
        // CurrentCustodianUserId is likewise left unconstrained at the DB level for the same
        // reason (a real FK to Users would require the same central-wiring treatment; deferred
        // since nothing in this phase queries through it, only stores/clears it).
    }
}
```

- [ ] **Step 2: Write ShipmentItemConfiguration**

`src/Modules/Shipments/Infrastructure/ShipmentItemConfiguration.cs`:

```csharp
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Shipments.Infrastructure;

public class ShipmentItemConfiguration : IEntityTypeConfiguration<ShipmentItem>
{
    public void Configure(EntityTypeBuilder<ShipmentItem> builder)
    {
        builder.ToTable("ShipmentItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Description).HasMaxLength(500).IsRequired();
        builder.Property(i => i.UnitValue).HasPrecision(18, 2);

        builder.HasIndex(i => i.ShipmentId);
        builder.HasOne<Shipment>().WithMany().HasForeignKey(i => i.ShipmentId).OnDelete(DeleteBehavior.Cascade);

        // No OrganizationId of its own: tenant isolation flows transitively through
        // ShipmentId -> Shipment.OrganizationId, same reasoning as ZoneRate/AdditionalCharge
        // in Phase 5. Every query against this table scopes through a Shipment the caller's
        // tenant already owns.
    }
}
```

- [ ] **Step 3: Write ShipmentEventConfiguration**

`src/Modules/Shipments/Infrastructure/ShipmentEventConfiguration.cs`:

```csharp
using Courier.Branches.Domain;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Shipments.Infrastructure;

public class ShipmentEventConfiguration : IEntityTypeConfiguration<ShipmentEvent>
{
    public void Configure(EntityTypeBuilder<ShipmentEvent> builder)
    {
        builder.ToTable("ShipmentEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.Metadata).HasColumnType("jsonb");
        builder.Property(e => e.Latitude).HasPrecision(9, 6);
        builder.Property(e => e.Longitude).HasPrecision(9, 6);

        // Query pattern: "give me this shipment's full event history in order" — this index
        // makes that cheap; Timestamp is included so the common ORDER BY needs no extra sort.
        builder.HasIndex(e => new { e.ShipmentId, e.Timestamp });
        builder.HasOne<Shipment>().WithMany().HasForeignKey(e => e.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);

        // No OrganizationId of its own: same reasoning as ShipmentItemConfiguration.
    }
}
```

- [ ] **Step 4: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Shipments/Infrastructure
git commit -m "feat: add Shipments EF configurations"
```

---

### Task 5: Shipments Application — DTOs and ShipmentValidationException

**Files:**
- Create: `src/Modules/Shipments/Application/ShipmentDtos.cs`

**Interfaces:**
- Consumes: `Shipments.Domain` entities (Task 3).
- Produces: every request/response record and `ShipmentValidationException` — Tasks 6-8 (services) and Task 10 (endpoints) all consume these exact shapes.

- [ ] **Step 1: Write the DTOs**

`src/Modules/Shipments/Application/ShipmentDtos.cs`:

```csharp
using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

public record CreateShipmentItemRequest(string Description, int Quantity, decimal UnitValue);

public record CreateShipmentRequest(
    Guid? MerchantId, // ignored/forced to caller's own id for merchant-JWT callers; required for staff callers
    Guid OriginBranchId,
    Guid DestinationBranchId,
    Guid ServiceTypeId,
    decimal WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    decimal CodAmount,
    bool WantsInsurance,
    string RecipientName,
    string RecipientPhone,
    Guid RecipientProvinceId,
    Guid RecipientDistrictId,
    Guid RecipientLocalLevelId,
    int RecipientWardNumber,
    string RecipientAddressLine,
    string SenderName,
    string SenderPhone,
    Guid SenderProvinceId,
    Guid SenderDistrictId,
    Guid SenderLocalLevelId,
    int SenderWardNumber,
    string SenderAddressLine,
    List<CreateShipmentItemRequest> Items);

public record ShipmentItemResponse(Guid Id, string Description, int Quantity, decimal UnitValue);

public record ShipmentResponse(
    Guid Id,
    string Awb,
    Guid MerchantId,
    Guid OriginBranchId,
    Guid DestinationBranchId,
    Guid? CurrentBranchId,
    Guid? CurrentCustodianUserId,
    ShipmentStatus CurrentStatus,
    Guid ServiceTypeId,
    decimal WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    decimal DeclaredValue,
    decimal CodAmount,
    bool WantsInsurance,
    decimal DeliveryCharge,
    decimal InsuranceCharge,
    decimal OtherCharges,
    decimal TotalCharge,
    string RecipientName,
    string RecipientPhone,
    string RecipientAddressLine,
    string SenderName,
    string SenderPhone,
    string SenderAddressLine,
    List<ShipmentItemResponse> Items);

public record AssignPickupRequest(Guid AssignedUserId);
public record AssignDeliveryRequest(Guid AssignedUserId);
public record DeliverShipmentRequest(string RecipientName, string? Notes);

public class ShipmentValidationException : Exception
{
    public ShipmentValidationException(string message) : base(message) { }
}

// Thrown specifically for an invalid state transition — carries the exact fields
// API-SPEC.md documents in its SHIPMENT_INVALID_TRANSITION error shape ({shipmentId, from, to}),
// so the Presentation layer can map it to that precise response without re-deriving anything.
public class ShipmentInvalidTransitionException : ShipmentValidationException
{
    public ShipmentInvalidTransitionException(Guid shipmentId, ShipmentStatus from, ShipmentStatus to)
        : base($"Cannot transition shipment from {from} to {to}")
    {
        ShipmentId = shipmentId;
        From = from;
        To = to;
    }

    public Guid ShipmentId { get; }
    public ShipmentStatus From { get; }
    public ShipmentStatus To { get; }
}
```

- [ ] **Step 2: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Shipments/Application/ShipmentDtos.cs
git commit -m "feat: add Shipments DTOs and validation exceptions"
```

---

### Task 6: Shipments Application — ShipmentService.CreateShipmentAsync (Booking + AWB + Pricing)

**Files:**
- Create: `src/Modules/Shipments/Application/ShipmentService.cs` (this task writes the constructor, `CreateShipmentAsync`, and the private `GenerateAwbAsync` helper only — Task 7 extends this same file with the transition methods, Task 8 with read methods)
- Test: `tests/Shipments.Tests/ShipmentServiceCreationTests.cs`

**Interfaces:**
- Consumes: `IGeographyLookupService.IsValidHierarchyAsync`/`DeliveryZoneExistsAsync` (Phase 3/5), `IBranchLookupService.GetZoneInfoAsync` (Phase 5), `IMerchantLookupService.ExistsAsync` (Phase 5), `PricingService.QuoteAsync` (Phase 5, called directly), `Shipments.Domain` entities (Task 3), `Shipments.Application` DTOs (Task 5).
- Produces: `ShipmentService.CreateShipmentAsync(CreateShipmentRequest, ClaimsPrincipal, CancellationToken) -> ShipmentResponse` — Task 10's endpoints and Tasks 7-8 (same class) consume this. `ShipmentService`'s field layout (`_dbContext`, `_currentTenantProvider`, `_auditLogger`, `_branchLookupService`, `_geographyLookupService`, `_merchantLookupService`, `_pricingService`, `_branchAuthorizationChecker`) is fixed here — Task 7 adds no new constructor parameters.

- [ ] **Step 1: Write the failing tests**

`tests/Shipments.Tests/ShipmentServiceCreationTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Geography.Application;
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

public class ShipmentServiceCreationTests
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
            modelBuilder.ApplyConfiguration(new ShipmentConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentItemConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentEventConfiguration());
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

    private static (Guid OriginBranchId, Guid DestinationBranchId, Guid OriginZoneId, Guid DestinationZoneId, Guid ServiceTypeId) SeedRoutableRateCard(TestDbContext dbContext, Guid organizationId, decimal baseRate = 150m)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;

        var rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
        dbContext.Set<RateCard>().Add(rateCard);
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = weightSlabId, BaseRate = baseRate });
        dbContext.SaveChanges();

        return (originBranchId, destinationBranchId, originZoneId, destinationZoneId, serviceTypeId);
    }

    private static ShipmentService CreateService(TestDbContext dbContext, Guid organizationId, Dictionary<Guid, Guid?> zonesByBranch)
    {
        var branchLookup = new FakeBranchLookup(zonesByBranch);
        var pricingService = new PricingService(dbContext, new FixedTenantProvider(organizationId), branchLookup, new AlwaysTrueMerchantLookup());
        return new ShipmentService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchLookup,
            new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup(), pricingService, new AllBranchesAuthorizationChecker());
    }

    private static CreateShipmentRequest ValidRequest(Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, decimal weightKg = 0.7m, decimal codAmount = 0m, bool wantsInsurance = false, Guid? merchantId = null) => new(
        merchantId ?? Guid.NewGuid(), originBranchId, destinationBranchId, serviceTypeId, weightKg, null, null, null, codAmount, wantsInsurance,
        "Recipient One", "9800000000", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Recipient Address",
        "Sender One", "9800000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 2, 500m) });

    [Fact]
    public async Task CreateShipmentAsync_ValidRequest_ComputesDeclaredValueFromItems()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedRoutableRateCard(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new() { [seed.OriginBranchId] = seed.OriginZoneId, [seed.DestinationBranchId] = seed.DestinationZoneId });

        var result = await service.CreateShipmentAsync(ValidRequest(seed.OriginBranchId, seed.DestinationBranchId, seed.ServiceTypeId), ActingUser(Guid.NewGuid()));

        Assert.Equal(1000m, result.DeclaredValue); // 2 * 500
        Assert.Equal(150m, result.TotalCharge);
        Assert.Equal(ShipmentStatus.Created, result.CurrentStatus);
    }

    [Fact]
    public async Task CreateShipmentAsync_ValidRequest_GeneratesUniqueAwbWithOrgPrefix()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedRoutableRateCard(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new() { [seed.OriginBranchId] = seed.OriginZoneId, [seed.DestinationBranchId] = seed.DestinationZoneId });

        var first = await service.CreateShipmentAsync(ValidRequest(seed.OriginBranchId, seed.DestinationBranchId, seed.ServiceTypeId), ActingUser(Guid.NewGuid()));
        var second = await service.CreateShipmentAsync(ValidRequest(seed.OriginBranchId, seed.DestinationBranchId, seed.ServiceTypeId), ActingUser(Guid.NewGuid()));

        Assert.NotEqual(first.Awb, second.Awb);
        Assert.Matches(@"^[A-Z0-9]{1,6}\d{8}\d{6}$", first.Awb);
    }

    [Fact]
    public async Task CreateShipmentAsync_ValidRequest_WritesSingleCreatedEvent()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedRoutableRateCard(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new() { [seed.OriginBranchId] = seed.OriginZoneId, [seed.DestinationBranchId] = seed.DestinationZoneId });

        var result = await service.CreateShipmentAsync(ValidRequest(seed.OriginBranchId, seed.DestinationBranchId, seed.ServiceTypeId), ActingUser(Guid.NewGuid()));

        var events = await dbContext.Set<ShipmentEvent>().Where(e => e.ShipmentId == result.Id).ToListAsync();
        var created = Assert.Single(events);
        Assert.Equal(ShipmentEventType.Created, created.EventType);
        Assert.Null(created.PreviousStatus);
        Assert.Equal(ShipmentStatus.Created, created.NewStatus);
        Assert.Equal(seed.OriginBranchId, created.BranchId);
    }

    [Fact]
    public async Task CreateShipmentAsync_EmptyItems_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var seed = SeedRoutableRateCard(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId, new() { [seed.OriginBranchId] = seed.OriginZoneId, [seed.DestinationBranchId] = seed.DestinationZoneId });
        var request = ValidRequest(seed.OriginBranchId, seed.DestinationBranchId, seed.ServiceTypeId) with { Items = new List<CreateShipmentItemRequest>() };

        await Assert.ThrowsAsync<ShipmentValidationException>(() => service.CreateShipmentAsync(request, ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateShipmentAsync_NoRateDefined_ThrowsShipmentValidationException()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        // No RateCard/ZoneRate seeded at all for this org.
        var service = CreateService(dbContext, organizationId, new() { [originBranchId] = originZoneId, [destinationBranchId] = destinationZoneId });

        await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            service.CreateShipmentAsync(ValidRequest(originBranchId, destinationBranchId, serviceTypeId), ActingUser(Guid.NewGuid())));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter ShipmentServiceCreationTests -v n`
Expected: FAIL — `ShipmentService` doesn't exist.

- [ ] **Step 3: Write ShipmentService (constructor + CreateShipmentAsync + AWB generation)**

`src/Modules/Shipments/Application/ShipmentService.cs`:

```csharp
using System.Security.Claims;
using System.Text.RegularExpressions;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Geography.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public partial class ShipmentService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IGeographyLookupService _geographyLookupService;
    private readonly IMerchantLookupService _merchantLookupService;
    private readonly PricingService _pricingService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public ShipmentService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IBranchLookupService branchLookupService,
        IGeographyLookupService geographyLookupService,
        IMerchantLookupService merchantLookupService,
        PricingService pricingService,
        IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchLookupService = branchLookupService;
        _geographyLookupService = geographyLookupService;
        _merchantLookupService = merchantLookupService;
        _pricingService = pricingService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<ShipmentResponse> CreateShipmentAsync(CreateShipmentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        // Merchant-JWT callers get their own id forced regardless of what the body says — never
        // trust a client-supplied MerchantId on a merchant token (CLAUDE.md §33).
        Guid merchantId;
        var callerMerchantId = actingUser.GetMerchantId();
        if (callerMerchantId is Guid selfMerchantId)
        {
            merchantId = selfMerchantId;
        }
        else
        {
            var actingUserId = actingUser.GetUserId()
                ?? throw new ShipmentValidationException("Acting user could not be resolved.");
            if (request.MerchantId is not Guid staffSuppliedMerchantId)
            {
                throw new ShipmentValidationException("MerchantId is required.");
            }
            if (!await _merchantLookupService.ExistsAsync(staffSuppliedMerchantId, cancellationToken))
            {
                throw new ShipmentValidationException("Merchant was not found in this organization.");
            }
            merchantId = staffSuppliedMerchantId;

            var accessibleForCreate = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "shipment.create", cancellationToken);
            if (accessibleForCreate is not null && !accessibleForCreate.Contains(request.OriginBranchId))
            {
                throw new ShipmentValidationException("Origin branch is outside your branch access.");
            }
        }

        // Origin/destination branch existence is validated inside PricingService.QuoteAsync below
        // (via the same IBranchLookupService.GetZoneInfoAsync call) — no separate pre-check here,
        // that would just be a redundant duplicate lookup with an equivalent error message.

        if (!await _geographyLookupService.IsValidHierarchyAsync(request.RecipientProvinceId, request.RecipientDistrictId, request.RecipientLocalLevelId, request.RecipientWardNumber, cancellationToken))
        {
            throw new ShipmentValidationException("Recipient province/district/local level/ward do not form a valid geography chain.");
        }
        if (!await _geographyLookupService.IsValidHierarchyAsync(request.SenderProvinceId, request.SenderDistrictId, request.SenderLocalLevelId, request.SenderWardNumber, cancellationToken))
        {
            throw new ShipmentValidationException("Sender province/district/local level/ward do not form a valid geography chain.");
        }

        if (request.Items.Count == 0)
        {
            throw new ShipmentValidationException("A shipment must have at least one item.");
        }

        var declaredValue = request.Items.Sum(i => i.Quantity * i.UnitValue);

        PriceQuoteResponse quote;
        try
        {
            quote = await _pricingService.QuoteAsync(
                new PriceQuoteRequest(request.OriginBranchId, request.DestinationBranchId, request.WeightKg, request.ServiceTypeId, request.CodAmount, declaredValue, request.WantsInsurance, merchantId),
                cancellationToken);
        }
        catch (PricingValidationException ex)
        {
            throw new ShipmentValidationException(ex.Message);
        }

        var insuranceCharge = quote.AppliedCharges.Where(c => c.Type == AdditionalChargeType.Insurance).Sum(c => c.Amount);
        var otherCharges = quote.AppliedCharges.Where(c => c.Type is AdditionalChargeType.Fuel or AdditionalChargeType.Other).Sum(c => c.Amount);
        var deliveryCharge = quote.BaseRate;

        // The whole build-and-save is retried, not just AWB generation: GenerateAwbAsync's
        // AnyAsync check is only a fast-path pre-check, not a guarantee — two concurrent requests
        // for the same org on the same day can both pass it and then collide on the real unique
        // (OrganizationId, Awb) index at SaveChangesAsync. On that failure the loser must discard
        // its now-conflicted tracked entities (EF won't let you re-Add an entity still tracked
        // from a failed SaveChanges) and regenerate a fresh AWB, which will now see the winner's
        // committed row and compute a genuinely higher sequence — same race-handling shape as
        // Phase 5's RateCard versioning fix (commit a83c320), just applied around the entities
        // instead of a single row update.
        Shipment shipment = null!;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var awb = await GenerateAwbAsync(organizationId, cancellationToken);

            shipment = new Shipment
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Awb = awb,
                MerchantId = merchantId,
                OriginBranchId = request.OriginBranchId,
                DestinationBranchId = request.DestinationBranchId,
                CurrentBranchId = request.OriginBranchId,
                CurrentStatus = ShipmentStatus.Created,
                ServiceTypeId = request.ServiceTypeId,
                WeightKg = request.WeightKg,
                LengthCm = request.LengthCm,
                WidthCm = request.WidthCm,
                HeightCm = request.HeightCm,
                DeclaredValue = declaredValue,
                CodAmount = request.CodAmount,
                WantsInsurance = request.WantsInsurance,
                DeliveryCharge = deliveryCharge,
                InsuranceCharge = insuranceCharge,
                OtherCharges = otherCharges,
                TotalCharge = quote.Total,
                RecipientName = request.RecipientName,
                RecipientPhone = request.RecipientPhone,
                RecipientProvinceId = request.RecipientProvinceId,
                RecipientDistrictId = request.RecipientDistrictId,
                RecipientLocalLevelId = request.RecipientLocalLevelId,
                RecipientWardNumber = request.RecipientWardNumber,
                RecipientAddressLine = request.RecipientAddressLine,
                SenderName = request.SenderName,
                SenderPhone = request.SenderPhone,
                SenderProvinceId = request.SenderProvinceId,
                SenderDistrictId = request.SenderDistrictId,
                SenderLocalLevelId = request.SenderLocalLevelId,
                SenderWardNumber = request.SenderWardNumber,
                SenderAddressLine = request.SenderAddressLine
            };
            _dbContext.Set<Shipment>().Add(shipment);

            foreach (var item in request.Items)
            {
                _dbContext.Set<ShipmentItem>().Add(new ShipmentItem
                {
                    Id = Guid.NewGuid(),
                    ShipmentId = shipment.Id,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitValue = item.UnitValue
                });
            }

            _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                EventType = ShipmentEventType.Created,
                PreviousStatus = null,
                NewStatus = ShipmentStatus.Created,
                BranchId = request.OriginBranchId,
                UserId = actingUser.GetUserId(),
                Timestamp = DateTimeOffset.UtcNow
            });

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                foreach (var entry in _dbContext.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }
        }

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "shipment.created", "Shipment", shipment.Id.ToString()), cancellationToken);

        return await LoadResponseAsync(shipment.Id, cancellationToken);
    }

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NonAlphanumericRegex();

    // Format: {OrgSlugPrefix}{yyyyMMdd}{6-digit sequence}, e.g. ACME20260820000001. Prefix is the
    // org's Slug uppercased with non-alphanumeric characters stripped, truncated to 6 chars (a
    // Slug can be any string up to 100 chars per OrganizationConfiguration.cs — nothing sanitizes
    // it at registration, so this must). If that leaves an empty prefix (a slug made entirely of
    // symbols), fall back to the first 6 hex chars of OrganizationId — always non-empty, always
    // deterministic per org. Sequence: current max existing suffix for
    // (OrganizationId, today's date-prefix) + 1, insert-and-catch-DbUpdateException-retry on the
    // unique (OrganizationId, Awb) index, same race-handling shape as Phase 5's RateCard
    // versioning fix (commit a83c320) — no new counter table.
    private async Task<string> GenerateAwbAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var organization = await _dbContext.Set<Courier.Identity.Domain.Organization>().FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);
        var rawPrefix = NonAlphanumericRegex().Replace(organization?.Slug.ToUpperInvariant() ?? string.Empty, string.Empty);
        var prefix = rawPrefix.Length > 0 ? rawPrefix[..Math.Min(6, rawPrefix.Length)] : organizationId.ToString("N")[..6].ToUpperInvariant();
        var datePart = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var awbPrefix = $"{prefix}{datePart}";

        var maxExistingSuffix = await _dbContext.Set<Shipment>()
            .Where(s => s.OrganizationId == organizationId && s.Awb.StartsWith(awbPrefix))
            .Select(s => s.Awb)
            .ToListAsync(cancellationToken);
        var nextSequence = maxExistingSuffix.Count == 0
            ? 1
            : maxExistingSuffix.Select(a => int.Parse(a[^6..])).Max() + 1;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = $"{awbPrefix}{nextSequence + attempt:D6}";
            var taken = await _dbContext.Set<Shipment>().AnyAsync(s => s.OrganizationId == organizationId && s.Awb == candidate, cancellationToken);
            if (!taken)
            {
                return candidate;
            }
        }

        throw new ShipmentValidationException("Could not generate a unique AWB after 5 attempts — please retry.");
    }

    private async Task<ShipmentResponse> LoadResponseAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstAsync(s => s.Id == shipmentId, cancellationToken);
        var items = await _dbContext.Set<ShipmentItem>().Where(i => i.ShipmentId == shipmentId).ToListAsync(cancellationToken);
        return ToResponse(shipment, items);
    }

    private static ShipmentResponse ToResponse(Shipment shipment, List<ShipmentItem> items) => new(
        shipment.Id, shipment.Awb, shipment.MerchantId, shipment.OriginBranchId, shipment.DestinationBranchId,
        shipment.CurrentBranchId, shipment.CurrentCustodianUserId, shipment.CurrentStatus, shipment.ServiceTypeId,
        shipment.WeightKg, shipment.LengthCm, shipment.WidthCm, shipment.HeightCm,
        shipment.DeclaredValue, shipment.CodAmount, shipment.WantsInsurance,
        shipment.DeliveryCharge, shipment.InsuranceCharge, shipment.OtherCharges, shipment.TotalCharge,
        shipment.RecipientName, shipment.RecipientPhone, shipment.RecipientAddressLine,
        shipment.SenderName, shipment.SenderPhone, shipment.SenderAddressLine,
        items.Select(i => new ShipmentItemResponse(i.Id, i.Description, i.Quantity, i.UnitValue)).ToList());
}
```

Note this class is declared `partial` — Task 7 adds the transition methods and Task 8 adds the read methods to the same `ShipmentService` type in separate files (`ShipmentService.Transitions.cs`, `ShipmentService.Reads.cs`), keeping each file focused per this plan's file-structure guidance rather than one large file.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter ShipmentServiceCreationTests -v n`
Expected: PASS (5 tests).

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Shipments/Application/ShipmentService.cs tests/Shipments.Tests/ShipmentServiceCreationTests.cs
git commit -m "feat: add ShipmentService.CreateShipmentAsync with AWB generation and pricing integration"
```

---

### Task 7: Shipments Application — ShipmentService Transition Methods

**Files:**
- Create: `src/Modules/Shipments/Application/ShipmentService.Transitions.cs` (`partial class ShipmentService`, no new constructor parameters)
- Test: `tests/Shipments.Tests/ShipmentServiceTransitionTests.cs`

**Interfaces:**
- Consumes: `ShipmentService`'s fields from Task 6 (same partial class, same constructor — no changes).
- Produces: `ShipmentService.{AssignPickupAsync, PickupScanAsync, ReceiveAtOriginAsync, SortAsync, DispatchAsync, ReceiveAtDestinationAsync, AssignDeliveryAsync, DeliverAsync, CancelAsync}(Guid shipmentId, ..., ClaimsPrincipal, CancellationToken) -> ShipmentResponse?` (null = shipment not found in this tenant/merchant scope) — Task 10's endpoints consume these.

- [ ] **Step 1: Write the failing tests**

`tests/Shipments.Tests/ShipmentServiceTransitionTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Geography.Application;
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

// Fakes duplicated per-file (file-scoped) matching ShipmentServiceCreationTests.cs's pattern —
// mirrors how RateCardServiceTests.cs/PricingServiceTests.cs in Phase 5 each define their own
// file-scoped fakes rather than sharing a fixture file, since each test file is independently
// readable without hunting for a shared fixtures file.
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

public class ShipmentServiceTransitionTests
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
            new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup(), pricingService, new AllBranchesAuthorizationChecker());
    }

    private static async Task<(ShipmentService Service, Guid ShipmentId, Guid OriginBranchId, Guid DestinationBranchId)> SeedCreatedShipmentAsync(TestDbContext dbContext, Guid organizationId)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;
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

        return (service, created.Id, originBranchId, destinationBranchId);
    }

    [Fact]
    public async Task FullHappyPath_CreatedToDelivered_WalksEveryTransitionWithCorrectEventsAndCustody()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, originBranchId, destinationBranchId) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        var riderUserId = Guid.NewGuid();

        var afterAssign = await service.AssignPickupAsync(shipmentId, new AssignPickupRequest(riderUserId), actingUser);
        Assert.Equal(ShipmentStatus.PickupAssigned, afterAssign!.CurrentStatus);
        Assert.Equal(riderUserId, afterAssign.CurrentCustodianUserId);

        var afterScan = await service.PickupScanAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.PickedUp, afterScan!.CurrentStatus);
        Assert.Equal(riderUserId, afterScan.CurrentCustodianUserId);

        var afterReceiveOrigin = await service.ReceiveAtOriginAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.AtOriginBranch, afterReceiveOrigin!.CurrentStatus);
        Assert.Equal(originBranchId, afterReceiveOrigin.CurrentBranchId);
        Assert.Null(afterReceiveOrigin.CurrentCustodianUserId);

        var afterSort = await service.SortAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.Sorted, afterSort!.CurrentStatus);
        Assert.Equal(originBranchId, afterSort.CurrentBranchId);

        var afterDispatch = await service.DispatchAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.InTransit, afterDispatch!.CurrentStatus);
        Assert.Null(afterDispatch.CurrentBranchId);

        var afterReceiveDest = await service.ReceiveAtDestinationAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.AtDestinationBranch, afterReceiveDest!.CurrentStatus);
        Assert.Equal(destinationBranchId, afterReceiveDest.CurrentBranchId);

        var afterOutForDelivery = await service.AssignDeliveryAsync(shipmentId, new AssignDeliveryRequest(riderUserId), actingUser);
        Assert.Equal(ShipmentStatus.OutForDelivery, afterOutForDelivery!.CurrentStatus);
        Assert.Equal(riderUserId, afterOutForDelivery.CurrentCustodianUserId);
        Assert.Null(afterOutForDelivery.CurrentBranchId);

        var afterDeliver = await service.DeliverAsync(shipmentId, new DeliverShipmentRequest("Recipient", "left at door"), actingUser);
        Assert.Equal(ShipmentStatus.Delivered, afterDeliver!.CurrentStatus);
        Assert.Null(afterDeliver.CurrentCustodianUserId);
        Assert.Null(afterDeliver.CurrentBranchId);

        var events = await dbContext.Set<ShipmentEvent>().Where(e => e.ShipmentId == shipmentId).OrderBy(e => e.Timestamp).ToListAsync();
        Assert.Equal(9, events.Count);
        Assert.Equal(new[]
        {
            ShipmentEventType.Created, ShipmentEventType.PickupAssigned, ShipmentEventType.PickedUp,
            ShipmentEventType.ReceivedAtOrigin, ShipmentEventType.Sorted, ShipmentEventType.Dispatched,
            ShipmentEventType.ReceivedAtDestination, ShipmentEventType.OutForDelivery, ShipmentEventType.Delivered
        }, events.Select(e => e.EventType));
    }

    [Fact]
    public async Task PickupScanAsync_FromCreatedStatus_ThrowsInvalidTransition()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);

        var ex = await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => service.PickupScanAsync(shipmentId, ActingUser(Guid.NewGuid())));
        Assert.Equal(ShipmentStatus.Created, ex.From);
        Assert.Equal(ShipmentStatus.PickedUp, ex.To);
    }

    [Fact]
    public async Task CancelAsync_FromCreated_Succeeds()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);

        var result = await service.CancelAsync(shipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.Cancelled, result!.CurrentStatus);
    }

    [Fact]
    public async Task CancelAsync_FromPickedUp_ThrowsInvalidTransition()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        await service.AssignPickupAsync(shipmentId, new AssignPickupRequest(Guid.NewGuid()), actingUser);
        await service.PickupScanAsync(shipmentId, actingUser);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => service.CancelAsync(shipmentId, actingUser));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter ShipmentServiceTransitionTests -v n`
Expected: FAIL — transition methods don't exist.

- [ ] **Step 3: Write the transition methods**

`src/Modules/Shipments/Application/ShipmentService.Transitions.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public partial class ShipmentService
{
    private async Task<ShipmentResponse?> TransitionAsync(
        Guid shipmentId, ShipmentStatus expectedFrom, ShipmentStatus to, ShipmentEventType eventType,
        Action<Shipment> applyCustody, string auditAction, ClaimsPrincipal actingUser, CancellationToken cancellationToken)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }

        if (shipment.CurrentStatus != expectedFrom)
        {
            throw new ShipmentInvalidTransitionException(shipmentId, shipment.CurrentStatus, to);
        }

        var previousStatus = shipment.CurrentStatus;
        shipment.CurrentStatus = to;
        applyCustody(shipment);

        _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            EventType = eventType,
            PreviousStatus = previousStatus,
            NewStatus = to,
            BranchId = shipment.CurrentBranchId,
            UserId = actingUser.GetUserId(),
            Timestamp = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), auditAction, "Shipment", shipmentId.ToString()), cancellationToken);

        return await LoadResponseAsync(shipmentId, cancellationToken);
    }

    private async Task CheckBranchAccessAsync(ClaimsPrincipal actingUser, string permission, Guid branchId, CancellationToken cancellationToken)
    {
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, permission, cancellationToken);
        if (accessible is not null && !accessible.Contains(branchId))
        {
            throw new ShipmentValidationException($"Branch is outside your branch access for permission '{permission}'.");
        }
    }

    public Task<ShipmentResponse?> AssignPickupAsync(Guid shipmentId, AssignPickupRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.Created, ShipmentStatus.PickupAssigned, ShipmentEventType.PickupAssigned,
            s => s.CurrentCustodianUserId = request.AssignedUserId, "shipment.pickup_assigned", actingUser, cancellationToken);

    public Task<ShipmentResponse?> PickupScanAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.PickupAssigned, ShipmentStatus.PickedUp, ShipmentEventType.PickedUp,
            _ => { }, "shipment.picked_up", actingUser, cancellationToken);

    public async Task<ShipmentResponse?> ReceiveAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        await CheckBranchAccessAsync(actingUser, "shipment.scan", shipment.OriginBranchId, cancellationToken);

        return await TransitionAsync(shipmentId, ShipmentStatus.PickedUp, ShipmentStatus.AtOriginBranch, ShipmentEventType.ReceivedAtOrigin,
            s => { s.CurrentBranchId = s.OriginBranchId; s.CurrentCustodianUserId = null; }, "shipment.received_at_origin", actingUser, cancellationToken);
    }

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

    public async Task<ShipmentResponse?> AssignDeliveryAsync(Guid shipmentId, AssignDeliveryRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        await CheckBranchAccessAsync(actingUser, "delivery.assign", shipment.DestinationBranchId, cancellationToken);

        return await TransitionAsync(shipmentId, ShipmentStatus.AtDestinationBranch, ShipmentStatus.OutForDelivery, ShipmentEventType.OutForDelivery,
            s => { s.CurrentCustodianUserId = request.AssignedUserId; s.CurrentBranchId = null; }, "shipment.out_for_delivery", actingUser, cancellationToken);
    }

    public Task<ShipmentResponse?> DeliverAsync(Guid shipmentId, DeliverShipmentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.OutForDelivery, ShipmentStatus.Delivered, ShipmentEventType.Delivered,
            s => s.CurrentCustodianUserId = null, "shipment.delivered", actingUser, cancellationToken);

    public async Task<ShipmentResponse?> CancelAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        if (actingUser.GetMerchantId() is Guid callerMerchantId && shipment.MerchantId != callerMerchantId)
        {
            return null;
        }

        if (shipment.CurrentStatus != ShipmentStatus.Created && shipment.CurrentStatus != ShipmentStatus.PickupAssigned)
        {
            throw new ShipmentInvalidTransitionException(shipmentId, shipment.CurrentStatus, ShipmentStatus.Cancelled);
        }

        var previousStatus = shipment.CurrentStatus;
        shipment.CurrentStatus = ShipmentStatus.Cancelled;
        shipment.CurrentCustodianUserId = null;
        shipment.CurrentBranchId = null;

        _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            EventType = ShipmentEventType.Cancelled,
            PreviousStatus = previousStatus,
            NewStatus = ShipmentStatus.Cancelled,
            UserId = actingUser.GetUserId(),
            Timestamp = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "shipment.cancelled", "Shipment", shipmentId.ToString()), cancellationToken);

        return await LoadResponseAsync(shipmentId, cancellationToken);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter ShipmentServiceTransitionTests -v n`
Expected: PASS (4 tests).

- [ ] **Step 5: Verify the solution builds and all Shipments.Tests pass**

```bash
dotnet build Courier.slnx
dotnet test tests/Shipments.Tests/Shipments.Tests.csproj
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`; all Shipments.Tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Shipments/Application/ShipmentService.Transitions.cs tests/Shipments.Tests/ShipmentServiceTransitionTests.cs
git commit -m "feat: add ShipmentService transition methods for the full Created-to-Delivered path"
```

---

### Task 8: Shipments Application — ShipmentService Read Methods

**Files:**
- Create: `src/Modules/Shipments/Application/ShipmentService.Reads.cs` (`partial class ShipmentService`, no new constructor parameters)
- Test: `tests/Shipments.Tests/ShipmentServiceReadTests.cs`

**Interfaces:**
- Consumes: `ShipmentService`'s fields from Task 6.
- Produces: `ShipmentService.{GetByAwbAsync(string awb, ClaimsPrincipal, CancellationToken) -> ShipmentResponse?, ListAsync(ClaimsPrincipal, CancellationToken) -> List<ShipmentResponse>}` — Task 10's endpoints consume these. Both apply merchant-scoping (a merchant-JWT caller only sees `MerchantId == self`) in addition to the tenant query filter that already applies to every query.

- [ ] **Step 1: Write the failing tests**

`tests/Shipments.Tests/ShipmentServiceReadTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Geography.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.Shipments.Application;
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

public class ShipmentServiceReadTests
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

    private static ClaimsPrincipal StaffUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));
    private static ClaimsPrincipal MerchantUser(Guid merchantId) => new(new ClaimsIdentity(new[] { new Claim("merchant_id", merchantId.ToString()) }, "test"));

    private static ShipmentService CreateService(TestDbContext dbContext, Guid organizationId, Dictionary<Guid, Guid?> zonesByBranch)
    {
        var branchLookup = new FakeBranchLookup(zonesByBranch);
        var pricingService = new PricingService(dbContext, new FixedTenantProvider(organizationId), branchLookup, new AlwaysTrueMerchantLookup());
        return new ShipmentService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchLookup,
            new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup(), pricingService, new AllBranchesAuthorizationChecker());
    }

    private static async Task<ShipmentResponse> SeedShipmentForMerchantAsync(TestDbContext dbContext, Guid organizationId, Guid merchantId)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;
        var rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
        dbContext.Set<RateCard>().Add(rateCard);
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = weightSlabId, BaseRate = 100m });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, organizationId, new() { [originBranchId] = originZoneId, [destinationBranchId] = destinationZoneId });
        var request = new CreateShipmentRequest(
            merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
            "Recipient", "9800000000", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Recipient Address",
            "Sender", "9800000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Sender Address",
            new List<CreateShipmentItemRequest> { new("Item", 1, 100m) });
        return await service.CreateShipmentAsync(request, StaffUser(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetByAwbAsync_MerchantOwnsShipment_ReturnsIt()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var created = await SeedShipmentForMerchantAsync(dbContext, organizationId, merchantId);
        var service = CreateService(dbContext, organizationId, new());

        var result = await service.GetByAwbAsync(created.Awb, MerchantUser(merchantId));

        Assert.NotNull(result);
        Assert.Equal(created.Id, result!.Id);
    }

    [Fact]
    public async Task GetByAwbAsync_DifferentMerchant_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var created = await SeedShipmentForMerchantAsync(dbContext, organizationId, merchantId);
        var service = CreateService(dbContext, organizationId, new());

        var result = await service.GetByAwbAsync(created.Awb, MerchantUser(otherMerchantId));

        Assert.Null(result);
    }

    [Fact]
    public async Task ListAsync_MerchantCaller_OnlySeesOwnShipments()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        await SeedShipmentForMerchantAsync(dbContext, organizationId, merchantId);
        await SeedShipmentForMerchantAsync(dbContext, organizationId, otherMerchantId);
        var service = CreateService(dbContext, organizationId, new());

        var result = await service.ListAsync(MerchantUser(merchantId));

        Assert.All(result, s => Assert.Equal(merchantId, s.MerchantId));
    }

    [Fact]
    public async Task ListAsync_StaffCaller_SeesAllOrgShipments()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new());

        var result = await service.ListAsync(StaffUser(Guid.NewGuid()));

        Assert.Equal(2, result.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter ShipmentServiceReadTests -v n`
Expected: FAIL — `GetByAwbAsync`/`ListAsync` don't exist.

- [ ] **Step 3: Write the read methods**

`src/Modules/Shipments/Application/ShipmentService.Reads.cs`:

```csharp
using System.Security.Claims;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public partial class ShipmentService
{
    public async Task<ShipmentResponse?> GetByAwbAsync(string awb, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Awb == awb, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        if (actingUser.GetMerchantId() is Guid callerMerchantId && shipment.MerchantId != callerMerchantId)
        {
            return null;
        }

        return await LoadResponseAsync(shipment.Id, cancellationToken);
    }

    public async Task<List<ShipmentResponse>> ListAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Shipment>().AsQueryable();
        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            query = query.Where(s => s.MerchantId == callerMerchantId);
        }

        var shipments = await query.ToListAsync(cancellationToken);
        var responses = new List<ShipmentResponse>();
        foreach (var shipment in shipments)
        {
            var items = await _dbContext.Set<ShipmentItem>().Where(i => i.ShipmentId == shipment.Id).ToListAsync(cancellationToken);
            responses.Add(ToResponse(shipment, items));
        }
        return responses;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter ShipmentServiceReadTests -v n`
Expected: PASS (4 tests).

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Shipments/Application/ShipmentService.Reads.cs tests/Shipments.Tests/ShipmentServiceReadTests.cs
git commit -m "feat: add ShipmentService read methods with merchant/tenant scoping"
```

---

### Task 9: Shipments Presentation — Endpoints and DI Wiring

**Files:**
- Create: `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: `ShipmentService` (Tasks 6-8), `IPermissionChecker`/`IMerchantAuthorizationChecker` (Identity/SharedKernel, existing).
- Produces: all 11 HTTP endpoints listed in this plan's Architecture section.

- [ ] **Step 1: Write the endpoints**

`src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs`:

```csharp
using Courier.Merchants.Application;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Shipments.Presentation;

public static class ShipmentsEndpoints
{
    public static void MapShipmentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/shipments").RequireAuthorization();

        // POST / and /{id}/cancel accept EITHER a staff JWT (shipment.create/shipment.cancel via
        // IPermissionChecker) OR a merchant JWT (MerchantPermissionCatalog.ShipmentCreate/
        // ShipmentCancel via IMerchantAuthorizationChecker) — no existing single filter in this
        // codebase covers "either token kind", so both checkers are resolved and checked manually
        // here rather than chaining .RequirePermission()/.RequireMerchantPermission() (which each
        // assume exactly one token kind, per SharedKernel's existing filters).
        group.MapPost("/", async (CreateShipmentRequest request, ShipmentService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (httpContext.User.GetMerchantId() is not null)
            {
                if (!await merchantAuthorizationChecker.HasPermissionAsync(httpContext.User, MerchantPermissionCatalog.ShipmentCreate, httpContext.RequestAborted))
                {
                    return Results.Forbid();
                }
            }
            else if (httpContext.User.GetUserId() is not null)
            {
                if (!await permissionChecker.HasPermissionAsync(httpContext.User, "shipment.create", httpContext.RequestAborted))
                {
                    return Results.Forbid();
                }
            }
            else
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await service.CreateShipmentAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/shipments/{result.Awb}", result);
            }
            catch (ShipmentValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "SHIPMENT_VALIDATION_FAILED", message = ex.Message } });
            }
        });

        group.MapGet("/{awb}", async (string awb, ShipmentService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (!await HasReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker))
            {
                return httpContext.User.GetMerchantId() is null && httpContext.User.GetUserId() is null ? Results.Unauthorized() : Results.Forbid();
            }

            var result = await service.GetByAwbAsync(awb, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/", async (ShipmentService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (!await HasReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker))
            {
                return httpContext.User.GetMerchantId() is null && httpContext.User.GetUserId() is null ? Results.Unauthorized() : Results.Forbid();
            }

            return Results.Ok(await service.ListAsync(httpContext.User, httpContext.RequestAborted));
        });

        group.MapPost("/{id:guid}/pickup-assign", async (Guid id, AssignPickupRequest request, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.AssignPickupAsync(id, request, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("shipment.assign");

        group.MapPost("/{id:guid}/pickup-scan", async (Guid id, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.PickupScanAsync(id, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("shipment.scan");

        group.MapPost("/{id:guid}/receive-origin", async (Guid id, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.ReceiveAtOriginAsync(id, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("shipment.scan");

        group.MapPost("/{id:guid}/sort", async (Guid id, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.SortAsync(id, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("shipment.sort");

        group.MapPost("/{id:guid}/dispatch", async (Guid id, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.DispatchAsync(id, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("shipment.transfer");

        group.MapPost("/{id:guid}/receive-dest", async (Guid id, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.ReceiveAtDestinationAsync(id, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("shipment.scan");

        group.MapPost("/{id:guid}/out-for-delivery", async (Guid id, AssignDeliveryRequest request, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.AssignDeliveryAsync(id, request, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("delivery.assign");

        group.MapPost("/{id:guid}/deliver", async (Guid id, DeliverShipmentRequest request, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.DeliverAsync(id, request, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("delivery.complete");

        group.MapPost("/{id:guid}/cancel", async (Guid id, ShipmentService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (httpContext.User.GetMerchantId() is not null)
            {
                if (!await merchantAuthorizationChecker.HasPermissionAsync(httpContext.User, MerchantPermissionCatalog.ShipmentCancel, httpContext.RequestAborted))
                {
                    return Results.Forbid();
                }
            }
            else if (httpContext.User.GetUserId() is not null)
            {
                if (!await permissionChecker.HasPermissionAsync(httpContext.User, "shipment.cancel", httpContext.RequestAborted))
                {
                    return Results.Forbid();
                }
            }
            else
            {
                return Results.Unauthorized();
            }

            return await HandleTransitionAsync(() => service.CancelAsync(id, httpContext.User, httpContext.RequestAborted), id);
        });
    }

    // Same dual-auth reasoning as POST / and /{id}/cancel above: shipment.read (staff) or
    // MerchantPermissionCatalog.ShipmentRead (merchant) — checked manually since no existing
    // filter in this codebase covers "either token kind".
    private static async Task<bool> HasReadAccessAsync(HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker)
    {
        if (httpContext.User.GetMerchantId() is not null)
        {
            return await merchantAuthorizationChecker.HasPermissionAsync(httpContext.User, MerchantPermissionCatalog.ShipmentRead, httpContext.RequestAborted);
        }
        if (httpContext.User.GetUserId() is not null)
        {
            return await permissionChecker.HasPermissionAsync(httpContext.User, "shipment.read", httpContext.RequestAborted);
        }
        return false;
    }

    private static async Task<IResult> HandleTransitionAsync(Func<Task<ShipmentResponse?>> operation, Guid shipmentId)
    {
        try
        {
            var result = await operation();
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (ShipmentInvalidTransitionException ex)
        {
            return Results.BadRequest(new
            {
                error = new
                {
                    code = "SHIPMENT_INVALID_TRANSITION",
                    message = ex.Message,
                    details = new { shipmentId = ex.ShipmentId, from = ex.From.ToString(), to = ex.To.ToString() }
                }
            });
        }
        catch (ShipmentValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "SHIPMENT_VALIDATION_FAILED", message = ex.Message } });
        }
    }
}
```

Note: `GET /{awb}` and `GET /` check read access manually via `HasReadAccessAsync` (same dual-auth shape as create/cancel) rather than chaining `.RequirePermission(...)`, since that filter only checks the staff `IPermissionChecker` — a merchant-JWT caller would always fail it. Beyond the permission check, merchant-scoping happens inside `ShipmentService` itself (a merchant sees only their own shipments; a staff caller sees the whole org, since `shipment.read` branch-scoping isn't part of this phase's design per the approved brainstorming). `RequireAuthorization()` on the group already rejects unauthenticated callers before the handler runs.

- [ ] **Step 2: Wire DI and endpoint mapping in Program.cs**

In `src/Api/Program.cs`, add `using Courier.Shipments.Application;` and `using Courier.Shipments.Presentation;` to the top `using` block, alphabetically between the `Courier.Pricing.Presentation` and `Courier.SharedKernel` usings.

After the `builder.Services.AddScoped<ReferenceDataService>();` line, add:

```csharp
builder.Services.AddScoped<ShipmentService>();
```

After the existing `app.MapPricingEndpoints();` line, add:

```csharp
app.MapShipmentsEndpoints();
```

- [ ] **Step 3: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Shipments/Presentation src/Api/Program.cs
git commit -m "feat: add Shipments endpoints and DI wiring"
```

---

### Task 10: Persistence — Wire Shipments into AppDbContext, Add Migration

**Files:**
- Modify: `src/Persistence/AppDbContext.cs`
- Create: `src/Persistence/Migrations/` (generated)

**Interfaces:**
- Consumes: all Shipments configurations (Task 4).
- Produces: `AppDbContext.{Shipments, ShipmentItems, ShipmentEvents}`, a migration creating all three tables with FKs/indexes/query filters. Task 11's Api.Tests query through this context.

- [ ] **Step 1: Update AppDbContext**

Add `using Courier.Shipments.Domain;` and `using Courier.Shipments.Infrastructure;` to the top of `src/Persistence/AppDbContext.cs`, alphabetically after `Courier.Pricing.Infrastructure` and before `Courier.SharedKernel`.

Add to the `DbSet` list, after `MerchantRateCards`:

```csharp
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentItem> ShipmentItems => Set<ShipmentItem>();
    public DbSet<ShipmentEvent> ShipmentEvents => Set<ShipmentEvent>();
```

In `OnModelCreating`, add to the `ApplyConfiguration` block, after `MerchantRateCardConfiguration`:

```csharp
        modelBuilder.ApplyConfiguration(new ShipmentConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentItemConfiguration());
        modelBuilder.ApplyConfiguration(new ShipmentEventConfiguration());
```

After the existing `modelBuilder.Entity<MerchantRateCard>().HasOne<Organization>()...` block, add:

```csharp
        // Same reasoning for Shipment.OrganizationId: Shipments has no project reference to
        // Identity either.
        modelBuilder.Entity<Shipment>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
```

After the existing `modelBuilder.Entity<MerchantRateCard>().HasQueryFilter(...)` line, add:

```csharp
        modelBuilder.Entity<Shipment>().HasQueryFilter(s => _currentTenantProvider.OrganizationId != null && s.OrganizationId == _currentTenantProvider.OrganizationId);
```

After the existing "No tenant filter on ZoneRate/AdditionalCharge" comment block, add:

```csharp
        // No tenant filter on ShipmentItem/ShipmentEvent: neither carries its own
        // OrganizationId — tenant isolation flows transitively through
        // ShipmentId -> Shipment.OrganizationId (which does have a filter, above). Every query
        // against these two tables in Shipments.Application scopes through a Shipment the
        // caller's tenant already owns.
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
dotnet ef migrations add AddShipments --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj
dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj --connection "Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password"
```

Expected: a new migration file appears under `src/Persistence/Migrations/`; `database update` reports it applied `AddShipments` with no errors.

- [ ] **Step 3: Verify the new tables exist**

```bash
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"Shipments\""
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"ShipmentItems\""
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"ShipmentEvents\""
```

Expected: each `\d` prints a column listing matching the entities from Task 3.

- [ ] **Step 4: Verify the solution still builds and all tests pass**

```bash
dotnet build Courier.slnx
dotnet test Courier.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`; all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Persistence
git commit -m "feat: wire Shipments into AppDbContext with migration"
```

---

### Task 11: Api.Tests — End-to-End Happy-Path, Pricing-Lock, Tenant/Merchant Isolation, and Branch-Scope Tests

**Files:**
- Create: `tests/Api.Tests/ShipmentsEndpointsTests.cs`

**Interfaces:**
- Consumes: every endpoint from Task 9, against the real Postgres-backed `AppDbContext` from Task 10.
- Produces: nothing further consumed — this is the exit-criteria proof for Phase 6.

- [ ] **Step 1: Write the tests**

`tests/Api.Tests/ShipmentsEndpointsTests.cs`:

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
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class ShipmentsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public ShipmentsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Shipments Org {suffix}", $"shipments-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
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

    private async Task SeedRateCardAsync(HttpClient client, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, decimal baseRate)
    {
        var originBranch = (await (await client.GetAsync($"/api/v1/branches/{originBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var destBranch = (await (await client.GetAsync($"/api/v1/branches/{destinationBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var weightSlabs = await (await client.GetAsync("/api/v1/weight-slabs")).Content.ReadFromJsonAsync<List<WeightSlabResponse>>();
        var slab = weightSlabs!.Single(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m);

        var rateCardResponse = await client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest($"Card-{Guid.NewGuid():N}", DateTimeOffset.UtcNow));
        var rateCard = await rateCardResponse.Content.ReadFromJsonAsync<RateCardResponse>();
        await client.PostAsJsonAsync($"/api/v1/rate-cards/{rateCard!.Id}/zone-rates",
            new CreateZoneRateRequest(originBranch.DeliveryZoneId!.Value, destBranch.DeliveryZoneId!.Value, serviceTypeId, slab.Id, baseRate));
    }

    private static CreateShipmentRequest ValidShipmentRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 2, 500m) });

    private async Task<Guid> CreateMerchantAsync(HttpClient staffClient)
    {
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Shipper Pvt. Ltd.", "Shipper", "01-4444444", "shipper@example.com",
            $"shipper-owner-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return merchant!.Id;
    }

    [Fact]
    public async Task FullHappyPath_CreatedToDelivered_Succeeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "happy");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 150m);
        var merchantId = await CreateMerchantAsync(client);

        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(1000m, shipment!.DeclaredValue);
        Assert.Equal(150m, shipment.TotalCharge);

        var riderUserId = Guid.NewGuid(); // no Rider entity yet — any Guid is accepted as the assignee this phase

        var afterAssign = await (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/pickup-assign", new AssignPickupRequest(riderUserId))).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.PickupAssigned, afterAssign!.CurrentStatus);

        var afterScan = await (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/pickup-scan", new { })).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.PickedUp, afterScan!.CurrentStatus);

        var afterOrigin = await (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { })).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.AtOriginBranch, afterOrigin!.CurrentStatus);

        var afterSort = await (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/sort", new { })).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Sorted, afterSort!.CurrentStatus);

        var afterDispatch = await (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/dispatch", new { })).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.InTransit, afterDispatch!.CurrentStatus);

        var afterDest = await (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-dest", new { })).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.AtDestinationBranch, afterDest!.CurrentStatus);

        var afterOutForDelivery = await (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/out-for-delivery", new AssignDeliveryRequest(riderUserId))).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.OutForDelivery, afterOutForDelivery!.CurrentStatus);

        var deliverResponse = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/deliver", new DeliverShipmentRequest("Recipient One", "left with security guard"));
        Assert.Equal(HttpStatusCode.OK, deliverResponse.StatusCode);
        var delivered = await deliverResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Delivered, delivered!.CurrentStatus);
        Assert.Null(delivered.CurrentBranchId);
        Assert.Null(delivered.CurrentCustodianUserId);
    }

    [Fact]
    public async Task Deliver_FromCreatedStatus_Returns400WithInvalidTransitionDetails()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "bad-transition");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(client);
        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();

        var response = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment!.Id}/deliver", new DeliverShipmentRequest("X", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("SHIPMENT_INVALID_TRANSITION", body);
    }

    [Fact]
    public async Task PricingSnapshot_RateCardSupersededAfterBooking_ShipmentTotalChargeUnchanged()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "price-lock");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 150m);
        var merchantId = await CreateMerchantAsync(client);

        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(150m, shipment!.TotalCharge);

        // Supersede the RateCard with a new, higher rate — same route/service/weight.
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 999m);

        var reloaded = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(150m, reloaded!.TotalCharge);
    }

    [Fact]
    public async Task Shipments_OrgBCannotReadOrgAsShipment()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "iso-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(clientA);
        await SeedRateCardAsync(clientA, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(clientA);
        var shipment = await (await clientA.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "iso-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var response = await clientB.GetAsync($"/api/v1/shipments/{shipment!.Awb}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Returns (OwnerEmail, Password) for a freshly created merchant, mirroring
    // tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs's CreateMerchantAsync exactly — that
    // file's helper captures the owner email at creation time rather than trying to re-derive it
    // later, since CreateMerchantRequest's owner email is caller-supplied, not server-generated.
    private async Task<(string OwnerEmail, string Password)> CreateMerchantWithLoginAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"shipper-owner-{label}-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            $"Shipper {label} Pvt. Ltd.", $"Shipper {label}", "01-4444444", $"shipper-{label}@example.com",
            ownerEmail, "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, $"Address {label}");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        return (ownerEmail, "P@ssw0rd123!");
    }

    [Fact]
    public async Task Merchant_CanCreateAndCancelOwnShipment_ButNotAnotherMerchants()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "merchant-flow");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);

        var (ownerEmailA, passwordA) = await CreateMerchantWithLoginAsync(staffClient, "a");
        var merchantAClient = _factory.CreateClient();
        var merchantALoginResponse = await merchantAClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmailA, passwordA));
        Assert.Equal(HttpStatusCode.OK, merchantALoginResponse.StatusCode);
        var merchantAAuth = await merchantALoginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantAClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", merchantAAuth!.AccessToken);

        // Merchant JWT callers never supply MerchantId in the body — it's forced server-side from
        // the token's merchant_id claim, so Guid.Empty here proves that (a real value would only
        // matter if the server trusted it, which it must not per CLAUDE.md §33).
        var createResponse = await merchantAClient.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(Guid.Empty, originBranchId, destinationBranchId, serviceTypeId));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var shipmentA = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(merchantAAuth.MerchantId, shipmentA!.MerchantId);

        var cancelResponse = await merchantAClient.PostAsync($"/api/v1/shipments/{shipmentA.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Cancelled, cancelled!.CurrentStatus);

        // A second merchant, in the same org, must not be able to read or cancel merchant A's
        // shipment — this is the merchant-scoping axis, distinct from the tenant axis proven by
        // Shipments_OrgBCannotReadOrgAsShipment above (same org here, different merchant).
        var (ownerEmailB, passwordB) = await CreateMerchantWithLoginAsync(staffClient, "b");
        var merchantBClient = _factory.CreateClient();
        var merchantBLoginResponse = await merchantBClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmailB, passwordB));
        var merchantBAuth = await merchantBLoginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", merchantBAuth!.AccessToken);

        // shipmentA is already Cancelled at this point, so re-cancelling from B would 400 even if
        // isolation were broken — create a second, still-cancellable shipment for A to make this
        // a real isolation check rather than a coincidental transition-guard pass.
        var secondCreateResponse = await merchantAClient.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(Guid.Empty, originBranchId, destinationBranchId, serviceTypeId));
        var shipmentA2 = await secondCreateResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        var readAsB = await merchantBClient.GetAsync($"/api/v1/shipments/{shipmentA2!.Awb}");
        Assert.Equal(HttpStatusCode.NotFound, readAsB.StatusCode);

        var cancelAsB = await merchantBClient.PostAsync($"/api/v1/shipments/{shipmentA2.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, cancelAsB.StatusCode);
    }

    [Fact]
    public async Task PostShipments_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateShipment_ConcurrentRequestsForSameOrg_BothSucceedWithDistinctAwbs()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "awb-race");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(client);

        // Two concurrent requests share the same WebApplicationFactory but each gets its own DI
        // scope/DbContext/DB connection per ASP.NET Core request handling, so this genuinely
        // races against the real Postgres container — proving ShipmentService.CreateShipmentAsync's
        // catch-DbUpdateException-and-retry loop actually resolves a real AWB collision, not just
        // that the unique (OrganizationId, Awb) index is valid SQL. Both requests use the same
        // origin/destination/date, so GenerateAwbAsync's pre-check sees the same "no existing AWB
        // yet" snapshot for both and they collide on the same candidate at SaveChangesAsync.
        var first = client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var second = client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var responses = await Task.WhenAll(first, second);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var shipmentsCreated = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<ShipmentResponse>()));
        Assert.NotEqual(shipmentsCreated[0]!.Awb, shipmentsCreated[1]!.Awb);
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter ShipmentsEndpointsTests -v n`
Expected: PASS (7 tests).

- [ ] **Step 3: Verify the full solution builds and all tests pass**

```bash
dotnet build Courier.slnx
dotnet test Courier.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`; all tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/Api.Tests/ShipmentsEndpointsTests.cs
git commit -m "test: add end-to-end shipment lifecycle, pricing-lock, and isolation tests"
```

---

### Task 12: Update ROADMAP.md Status

**Files:**
- Modify: `docs/ROADMAP.md`

**Interfaces:**
- Consumes: nothing.
- Produces: an updated Phase 6 status note, mirroring Phases 1-5's, so Phase 7's "Entry" check has a clear record of what's actually done.

- [ ] **Step 1: Add the status note**

In `docs/ROADMAP.md`, immediately after the existing Phase 6 paragraph (`*Exit:* full happy-path shipment lifecycle (\`CREATED\` → \`DELIVERED\`) works with correct append-only events and locked pricing.`), add:

```text

*Status (2026-08-20): Complete.* A new `Shipments` module implements `Shipment, ShipmentItem,
ShipmentEvent` per `DOMAIN-MODEL.md`. Since `Manifest`/`Rider`/`DeliveryAttempt`/`DeliveryProof`
don't exist until Phase 7/8, the full `Created → Delivered` path is driven by direct
staff-triggered transition endpoints (`pickup-assign, pickup-scan, receive-origin, sort, dispatch,
receive-dest, out-for-delivery, deliver`) gated by the exact permissions (`shipment.assign/scan/
sort/transfer`, `delivery.assign/complete`) the real Phase 7/8 operations will eventually use —
`deliver` takes plain recipient-name/notes fields, expected to be superseded by Phase 8's real POD.
Every transition writes exactly one append-only `ShipmentEvent`, proven by
`tests/Api.Tests/ShipmentsEndpointsTests.cs`'s `FullHappyPath_CreatedToDelivered_Succeeds` (9
events, correct custody at every step) and `tests/Shipments.Tests/ShipmentServiceTransitionTests.cs`.
Pricing is locked at booking: `POST /api/v1/shipments` calls `PricingService.QuoteAsync` (Phase 5)
directly and snapshots `DeliveryCharge/InsuranceCharge/OtherCharges/TotalCharge` onto the
`Shipment` row — `PricingSnapshot_RateCardSupersededAfterBooking_ShipmentTotalChargeUnchanged`
proves a later `RateCard` version change doesn't alter an already-booked shipment's price. AWB
format is `{OrgSlugPrefix}{yyyyMMdd}{6-digit sequence}`, generated with an insert-and-retry
collision handler mirroring Phase 5's `RateCard` versioning race fix. Both staff and merchant JWT
tracks can create/read/cancel shipments (merchant scoped to `MerchantId == self`, new
`shipment.create/read/cancel` constants added to `MerchantPermissionCatalog` reusing the staff
catalog's identical string keys) — `Merchant_CanCreateAndCancelOwnShipment_ButNotAnotherMerchants`
proves merchant-to-merchant isolation within the same org, alongside `Shipments_
OrgBCannotReadOrgAsShipment` for tenant isolation. Deferred to later phases: real barcode/manifest
scan validation (Phase 7), `Rider`/POD (Phase 8), `NDR`/`RTO`/`DELIVERY_FAILED` (Phase 9),
`ShipmentEvent` GPS population (Phase 8), webhooks (Phase 11), public `/track/{awb}` (Phase 12).
```

- [ ] **Step 2: Commit**

```bash
git add docs/ROADMAP.md
git commit -m "docs: mark Phase 6 exit criteria met"
```

---

## Plan Self-Review Notes

- **Spec coverage:** All three Shipments entities (Tasks 3-4), the full 10-endpoint transition table with per-endpoint permission/branch-scope/custody rules (Tasks 6-9), AWB generation with collision retry (Task 6), the pricing-integration flow with the exact charge-field mapping rule (Task 6), dual staff/merchant authorization on create/cancel (Tasks 6, 9), merchant-scoped reads (Task 8), persistence wiring (Task 10), and the exit-criteria proof itself (Task 11's `FullHappyPath_CreatedToDelivered_Succeeds` plus the pricing-lock and isolation tests) are all covered. `docs/ROADMAP.md` Phase 6's exact exit wording ("full happy-path shipment lifecycle CREATED → DELIVERED works with correct append-only events and locked pricing") maps directly onto those two tests.
- **Placeholder scan:** No TBD/TODO markers, no "fix before running" notes, no `Assert.True(true)` stand-ins — every test is complete, runnable code. `Merchant_CanCreateAndCancelOwnShipment_ButNotAnotherMerchants` (Task 11) logs in as two separate merchants via a `CreateMerchantWithLoginAsync` helper that captures the owner email at creation time (mirroring `tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs`'s `CreateMerchantAsync`/`MerchantOwnerLogin_ThenGetProfile_Succeeds` pattern exactly, confirmed against that file's actual current content), then proves both that a merchant JWT's body-supplied `MerchantId` is ignored in favor of the token's own, and that merchant B cannot read or cancel merchant A's shipment within the same org.
- **Type consistency:** `CreateShipmentRequest`/`ShipmentResponse`/`ShipmentItemResponse` field names and order match exactly between Task 5's definition, `ShipmentService.CreateShipmentAsync`'s construction (Task 6), and Task 11's test usage. `ShipmentStatus`/`ShipmentEventType` enum members match consistently across Tasks 3, 6, 7, and every test file. `ShipmentInvalidTransitionException.{ShipmentId, From, To}` matches between Task 5's definition, Task 7's `TransitionAsync`/`CancelAsync` usage, and Task 9's Presentation-layer catch block. `IBranchLookupService.GetZoneInfoAsync`/`IMerchantLookupService.ExistsAsync`/`IGeographyLookupService.IsValidHierarchyAsync` signatures match their real Phase 3/5 definitions exactly (verified by reading those files fresh before writing this plan, not from memory).
- **Judgment calls made while writing (see this fork's separate report to the parent conversation for the summary).**
