# Phase 10 — COD, Rider Settlements, Branch Reconciliation, Merchant Settlements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn COD from a boolean field on `Shipment` into a real, auditable financial subsystem — one `CODCollection` row per COD shipment that lives through the full `COD_EXPECTED → COD_COLLECTED → RIDER_DEPOSITED → BRANCH_RECONCILED → FINANCE_VERIFIED → MERCHANT_SETTLEMENT_CREATED → SETTLED` chain, plus a controlled, auditable adjustment path for correcting any figure along the way.

**Architecture:** A new `Cod` module (mirroring `Ndr`'s single-module shape) owns all seven entities from `DOMAIN-MODEL.md`'s "COD & Finance" section. `CODCollection` is created eagerly, at shipment-booking time, by a new narrow `ICodCollectionCreationService` that `Shipments.Application.ShipmentService.CreateShipmentAsync` calls right after a COD shipment is persisted — the same "narrow interface, later module reacts to earlier module's event, earlier module gains the project reference" shape `Riders → Ndr` (`INdrCreationService`) already established in Phase 9. `Riders.Application.RiderService.CompleteDeliveryAsync` similarly gains a project reference to `Cod` and calls a second narrow `ICodCollectionService.MarkCollectedAsync` right after creating the `DeliveryProof` row. Every other step in the chain (rider deposit, branch reconciliation, finance verification, merchant settlement creation, payout, adjustment) is a `Cod`-owned endpoint with no cross-module calls at all — `CODCollection` snapshots `MerchantId`, `BranchId`, and `ShipmentTotalCharge` at creation time (the same "lock pricing at booking" principle `Shipment` itself already follows, CLAUDE.md §16), so `Cod` never needs to read back into `Shipments` for any of it. `Cod` has no project reference to `Shipments` at all — this is a cleaner dependency graph than `Ndr`'s, and confirms the eager-creation design the brainstorm settled on was the right call. Steps 3–8 below (every multi-row bulk-status-update chain) are wrapped in an explicit `Database.BeginTransactionAsync()` — the first use of an explicit transaction anywhere in this codebase (confirmed zero prior usages by grep), deliberately established here because financial correctness demands it in a way the physical-custody chains of Phases 7–9 did not.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core / PostgreSQL, xUnit + EF InMemory (unit) / real Postgres via `WebApplicationFactory` (API tests) — same as every prior phase.

**Spec:** `docs/ROADMAP.md` (Phase 10 section), `docs/COD-LIFECYCLE.md` (full document — state machine, worked example, reconciliation visibility, adjustments, RTO interaction), `docs/DOMAIN-MODEL.md` ("COD & Finance" section), `CLAUDE.md` §22-23. This session's bounded brainstorm — approved in chat, no separate spec file, consistent with every prior phase in this project — resolved four design points beyond what the docs state literally: (1) `CODCollection` is created eagerly at booking, not lazily at delivery; (2) `CODCollection.Status` carries the full 7-state lifecycle chain (COD-LIFECYCLE.md's own prose says "the COD lifecycle status... lives on CODCollection, not on Shipment" — read literally, not just the narrower Expected/Collected pair the entity's field-list table shows); (3) `RiderCashSettlement.BranchCashSettlementId` (child-side FK) replaces `BranchCashSettlement.RiderCashSettlementIds[]` (array), matching this codebase's established one-to-many convention (`ManifestShipment.ManifestId`, `DeliveryTask.DeliveryRunId`) instead of a Postgres array column; (4) `Adjustment` always requires a separate approval step (no configurable-threshold infrastructure exists, so "approval where appropriate" resolves to "always" for v1). This plan additionally resolves, during file-reading research for this plan itself: `CODCollection` snapshots `MerchantId`/`BranchId`/`ShipmentTotalCharge` at creation instead of `Cod` reading back into `Shipments`, and the conditional `cod.collect` permission check on `CompleteDeliveryAsync` uses the existing `SharedKernel.IPermissionChecker.HasPermissionAsync` (already registered in DI, previously only consumed by the `RequirePermission` route filter) rather than inventing a new mechanism.

## Global Constraints

- Never trust a client-supplied `TenantId`/`BranchId` (CLAUDE.md §33) — every write resolves `OrganizationId` from `ICurrentTenantProvider`.
- Permission checks are permission-based (CLAUDE.md §6) — reuse the already-seeded `cod.read`, `cod.collect`, `cod.reconcile`, `cod.settle`, `cod.adjust` keys (confirmed present in `src/Modules/Identity/Application/PermissionCatalog.cs`); no new permission keys needed this phase. No new roles are seeded — `OrganizationOwner` already gets every permission via `AuthService.RegisterOrganizationAsync`'s existing loop, and `DeliveryRider`'s seeded role already includes `cod.collect` (line ~78 of that file). `FinanceManager` is not seeded, matching Phase 9's precedent of not seeding `BranchManager` either — orgs create custom roles via `role.manage` as needed.
- `Cod` must never call `_dbContext.Set<Shipment>()` or reference `Courier.Shipments` at all — every financial fact `Cod` needs about a shipment (`MerchantId`, `BranchId`, `ShipmentTotalCharge`, `ExpectedAmount`) is passed in as plain parameters by the caller (`Shipments.Application.ShipmentService` at booking, `Riders.Application.RiderService` at delivery) and snapshotted onto `CODCollection` — this is a stricter, cleaner version of the "no cached custody copy" principle CLAUDE.md §12 states for `Shipment`, applied to financial figures the same way `Shipment.DeliveryCharge`/`TotalCharge` already snapshot pricing at booking (CLAUDE.md §16).
- Every one of `CODCollection`, `RiderCashSettlement`, `BranchCashSettlement`, `FinancialTransaction`, `MerchantSettlement`, `Adjustment` carries its own `OrganizationId` (DOMAIN-MODEL.md annotates the whole "COD & Finance" section `OrganizationId present`) — direct query filters, not transitive ones. `MerchantSettlementItem` has no own `OrganizationId` (tenant isolation flows transitively through `MerchantSettlementId -> MerchantSettlement.OrganizationId`, same pattern as `ShipmentItem`/`ZoneRate`).
- Tasks 7–11 (every step from rider deposit through merchant payout, plus adjustment approval) wrap their multi-row bulk update + parent-row insert in `await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);` … `await transaction.CommitAsync(cancellationToken);`, with the whole body in a `try`/`catch` that calls `await transaction.RollbackAsync(cancellationToken);` before rethrowing on any exception. This is a deliberate deviation from `Ndr`/`Riders`' existing multi-`SaveChangesAsync`-with-no-transaction style (e.g. `RiderService.RecordFailedAttemptAsync`) — those chains move physical custody, where a crash mid-chain strands a parcel in a state a human can still visually find and fix; these chains move money across multiple rows, where a crash mid-chain could silently double-count or lose a `CODCollection`'s status with no physical object to reconcile against. Every transactional method's XML doc comment states this explicitly. A transaction alone guards against a crash mid-chain, not a genuine concurrent-request race between two independent callers — where a real race is possible (two callers finance-verifying the same `BranchCashSettlement` at once), a DB uniqueness constraint backs it too (see `FinancialTransactionConfiguration`, Task 2), the same lesson Phase 5's `RateCard` versioning fix already established.
- Every real state change writes an `AuditLog` entry via the existing `IAuditLogger` pattern (CLAUDE.md §28), same as `Ndr`/`Riders`.
- `MerchantSettlementItem.CourierFee` is read from `CODCollection.ShipmentTotalCharge` (the snapshot taken at booking), never recomputed — CLAUDE.md §16's "changing today's rate must not change yesterday's shipment price" applies transitively to the fee a settlement deducts from a COD amount.
- Never overwrite a financial figure in place outside the `Adjustment` flow (CLAUDE.md §23) — every status-chain step below only ever moves a `CODCollection` forward one status and stamps the FK/timestamp fields specific to that step; only `CodService.ApproveAdjustmentAsync` (Task 10) ever rewrites `MerchantSettlementItem.OtherDeductions`/`NetPayable`, and only after a separate approval call.

---

## Task 1: Cod module scaffold — project file and Domain layer

**Files:**
- Create: `src/Modules/Cod/Cod.csproj`
- Create: `src/Modules/Cod/Domain/CodCollectionStatus.cs`
- Create: `src/Modules/Cod/Domain/FinancialTransactionType.cs`
- Create: `src/Modules/Cod/Domain/MerchantSettlementStatus.cs`
- Create: `src/Modules/Cod/Domain/CODCollection.cs`
- Create: `src/Modules/Cod/Domain/RiderCashSettlement.cs`
- Create: `src/Modules/Cod/Domain/BranchCashSettlement.cs`
- Create: `src/Modules/Cod/Domain/FinancialTransaction.cs`
- Create: `src/Modules/Cod/Domain/MerchantSettlement.cs`
- Create: `src/Modules/Cod/Domain/MerchantSettlementItem.cs`
- Create: `src/Modules/Cod/Domain/Adjustment.cs`

**Interfaces:**
- Produces: every Domain type below, consumed by Task 2 (Infrastructure), Task 3 (`AppDbContext`), and Tasks 4–11 (Application/Presentation).

- [ ] **Step 1: Create the project file**

`src/Modules/Cod/Cod.csproj` — mirrors `Ndr.csproj`'s shape. References `Branches` (`IBranchLookupService`, to validate a caller-supplied `BranchId` exists before writing a `RiderCashSettlement`/`BranchCashSettlement`) and `Merchants` (`IMerchantLookupService`, to validate a caller-supplied `MerchantId` exists before generating a `MerchantSettlement`) — deliberately **no** reference to `Shipments` (see Global Constraints) and **no** reference to `Identity` (no user provisioning happens in this module, same as `Ndr`):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.11" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.11" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\SharedKernel\SharedKernel.csproj" />
    <ProjectReference Include="..\Audit\Audit.csproj" />
    <ProjectReference Include="..\Branches\Branches.csproj" />
    <ProjectReference Include="..\Merchants\Merchants.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: `CodCollectionStatus`, `FinancialTransactionType`, `MerchantSettlementStatus`**

`src/Modules/Cod/Domain/CodCollectionStatus.cs`:

```csharp
namespace Courier.Cod.Domain;

// COD-LIFECYCLE.md §1's full seven-state chain, all living on CODCollection.Status — this plan's
// resolved reading of COD-LIFECYCLE.md's prose ("the COD lifecycle status... lives on
// CODCollection, not on Shipment"), not the narrower Expected/Collected pair the entity's
// field-list table alone shows. Strictly forward-only; no step in this plan ever moves a
// CODCollection backward.
public enum CodCollectionStatus
{
    CodExpected,
    CodCollected,
    RiderDeposited,
    BranchReconciled,
    FinanceVerified,
    MerchantSettlementCreated,
    Settled
}
```

`src/Modules/Cod/Domain/FinancialTransactionType.cs`:

```csharp
namespace Courier.Cod.Domain;

// COD-LIFECYCLE.md §1's table links FinancialTransaction to exactly one step
// (BRANCH_RECONCILED -> FINANCE_VERIFIED); single member by design, not an oversight — easy to
// extend if a future phase gives FinancialTransaction a second real use.
public enum FinancialTransactionType
{
    CodVerification
}
```

`src/Modules/Cod/Domain/MerchantSettlementStatus.cs`:

```csharp
namespace Courier.Cod.Domain;

public enum MerchantSettlementStatus
{
    Created,
    Settled
}
```

- [ ] **Step 3: `CODCollection`**

`src/Modules/Cod/Domain/CODCollection.cs`:

```csharp
namespace Courier.Cod.Domain;

// Created eagerly by Cod.Application.CodService.CreateExpectedAsync, called from
// Shipments.Application.ShipmentService.CreateShipmentAsync immediately after a shipment with
// CodAmount > 0 is persisted (Task 4/5) — this plan's resolved design decision (bounded brainstorm):
// eager creation at booking, not lazy creation at delivery, so "Expected COD" reporting is a direct
// query against this table instead of a Shipment-table fallback, and a DB unique index enforces "at
// most one CODCollection per shipment" from day one (see CodCollectionConfiguration, Task 2).
//
// MerchantId, BranchId, and ShipmentTotalCharge are snapshotted here at creation time rather than
// looked up from Shipments later — Cod has no project reference to Shipments at all (see this
// plan's Global Constraints). BranchId is the shipment's DestinationBranchId at booking time (the
// branch that will attempt delivery and collect the cash) — the same branch Ndr/Riders already
// scope NDR/delivery-attempt operations to.
public class CODCollection
{
    public Guid Id { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid MerchantId { get; set; }
    public Guid BranchId { get; set; }
    public decimal ExpectedAmount { get; set; }
    public decimal ShipmentTotalCharge { get; set; }
    public decimal? CollectedAmount { get; set; }
    public CodCollectionStatus Status { get; set; }
    public Guid? CollectedByRiderId { get; set; }
    public DateTimeOffset? CollectedAt { get; set; }
    public Guid? DeliveryProofId { get; set; }
    public Guid? RiderCashSettlementId { get; set; }
    public Guid? MerchantSettlementItemId { get; set; }
}
```

- [ ] **Step 4: `RiderCashSettlement` and `BranchCashSettlement`**

`src/Modules/Cod/Domain/RiderCashSettlement.cs`:

```csharp
namespace Courier.Cod.Domain;

// Created by Cod.Application.CodService.CreateRiderSettlementAsync (Task 6) — a rider's end-of-run
// cash handover, covering every one of that rider's CodCollected CODCollection rows at the time of
// the call.
public class RiderCashSettlement
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid RiderId { get; set; }
    public Guid BranchId { get; set; }
    public decimal DeclaredAmount { get; set; }
    public int ShipmentCount { get; set; }
    public DateTimeOffset SettledAt { get; set; }
    public Guid ConfirmedByUserId { get; set; }
    public Guid? BranchCashSettlementId { get; set; }
}
```

`src/Modules/Cod/Domain/BranchCashSettlement.cs`:

```csharp
namespace Courier.Cod.Domain;

// Created by Cod.Application.CodService.CreateBranchSettlementAsync (Task 7). Deliberately has no
// RiderCashSettlementIds[] array column, unlike DOMAIN-MODEL.md's original sketch — the covered
// rider settlements are the RiderCashSettlement rows whose BranchCashSettlementId points back here
// (a child-side FK), matching this codebase's established one-to-many convention
// (ManifestShipment.ManifestId, DeliveryTask.DeliveryRunId) instead of a Postgres array column.
// This plan's resolved design decision (bounded brainstorm).
public class BranchCashSettlement
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid BranchId { get; set; }
    public decimal ConfirmedAmount { get; set; }
    public Guid ReconciledByUserId { get; set; }
    public DateTimeOffset ReconciledAt { get; set; }
}
```

- [ ] **Step 5: `FinancialTransaction`**

`src/Modules/Cod/Domain/FinancialTransaction.cs`:

```csharp
namespace Courier.Cod.Domain;

// Created by Cod.Application.CodService.CreateFinanceVerificationAsync (Task 8) — one row per
// BranchCashSettlement verified. ReferenceEntityType/ReferenceEntityId point at the
// BranchCashSettlement this verification covers (generic reference-entity pair, matching this
// entity's DOMAIN-MODEL.md shape, kept extensible even though FinancialTransactionType has a single
// member this phase).
public class FinancialTransaction
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public FinancialTransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public required string ReferenceEntityType { get; set; }
    public Guid ReferenceEntityId { get; set; }
    public Guid VerifiedByUserId { get; set; }
    public DateTimeOffset VerifiedAt { get; set; }
}
```

- [ ] **Step 6: `MerchantSettlement` and `MerchantSettlementItem`**

`src/Modules/Cod/Domain/MerchantSettlement.cs`:

```csharp
namespace Courier.Cod.Domain;

// Created by Cod.Application.CodService.CreateMerchantSettlementAsync (Task 9) — on-demand only,
// no scheduled generation (Hangfire/background-job infrastructure is Phase 11's, per
// CLARIFICATIONS.md #9's Ambiguity 4 precedent for AutoRTO). PeriodFrom/PeriodTo use
// DateTimeOffset, matching this codebase's existing convention for period-like fields
// (Pricing.Domain.RateCard.EffectiveFrom/EffectiveTo).
public class MerchantSettlement
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid MerchantId { get; set; }
    public DateTimeOffset PeriodFrom { get; set; }
    public DateTimeOffset PeriodTo { get; set; }
    public decimal TotalPayable { get; set; }
    public MerchantSettlementStatus Status { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
}
```

`src/Modules/Cod/Domain/MerchantSettlementItem.cs`:

```csharp
namespace Courier.Cod.Domain;

// One row per shipment folded into a MerchantSettlement. CODAmount/CourierFee are copied from the
// covering CODCollection's ExpectedAmount/ShipmentTotalCharge at settlement-creation time (both
// already locked at booking, see CODCollection's doc comment) — CourierFee is never recomputed
// from a current rate card. OtherDeductions defaults to 0 and is the only field
// Cod.Application.CodService.ApproveAdjustmentAsync (Task 10) is ever allowed to rewrite, which
// also recomputes NetPayable at the same time. No own OrganizationId — tenant isolation flows
// transitively through MerchantSettlementId -> MerchantSettlement.OrganizationId, same pattern as
// ShipmentItem -> Shipment / ZoneRate -> RateCard.
public class MerchantSettlementItem
{
    public Guid Id { get; set; }
    public Guid MerchantSettlementId { get; set; }
    public Guid ShipmentId { get; set; }
    public decimal CODAmount { get; set; }
    public decimal CourierFee { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal NetPayable { get; set; }
}
```

- [ ] **Step 7: `Adjustment`**

`src/Modules/Cod/Domain/Adjustment.cs`:

```csharp
namespace Courier.Cod.Domain;

// Created unapplied by Cod.Application.CodService.RequestAdjustmentAsync, applied only by a
// separate CodService.ApproveAdjustmentAsync call (Task 10) — this plan's resolved design decision
// (bounded brainstorm): Adjustment always requires a separate approval step, since no
// configurable-threshold infrastructure exists yet to decide when one is "appropriate"
// (CLAUDE.md §23). v1 supports EntityType = "MerchantSettlementItem" only, targeting
// OtherDeductions — the only adjustment case with a concrete business need right now; Applied
// stays false until ApproveAdjustmentAsync actually rewrites the target row.
public class Adjustment
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public decimal BeforeValue { get; set; }
    public decimal AfterValue { get; set; }
    public required string Reason { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public bool Applied { get; set; }
}
```

- [ ] **Step 8: Build the (currently empty-of-logic) project to confirm the scaffold compiles**

Run: `dotnet build src/Modules/Cod/Cod.csproj`
Expected: Build succeeds.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Cod/Cod.csproj src/Modules/Cod/Domain/
git commit -m "feat(cod): scaffold Cod module and domain entities"
```

---

## Task 2: Cod Infrastructure — EF configurations

**Files:**
- Create: `src/Modules/Cod/Infrastructure/CODCollectionConfiguration.cs`
- Create: `src/Modules/Cod/Infrastructure/RiderCashSettlementConfiguration.cs`
- Create: `src/Modules/Cod/Infrastructure/BranchCashSettlementConfiguration.cs`
- Create: `src/Modules/Cod/Infrastructure/FinancialTransactionConfiguration.cs`
- Create: `src/Modules/Cod/Infrastructure/MerchantSettlementConfiguration.cs`
- Create: `src/Modules/Cod/Infrastructure/MerchantSettlementItemConfiguration.cs`
- Create: `src/Modules/Cod/Infrastructure/AdjustmentConfiguration.cs`

**Interfaces:**
- Produces: seven `IEntityTypeConfiguration<T>` classes Task 3's `AppDbContext.OnModelCreating` applies.

- [ ] **Step 1: `CODCollection`**

`src/Modules/Cod/Infrastructure/CODCollectionConfiguration.cs`:

```csharp
using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Cod.Infrastructure;

public class CODCollectionConfiguration : IEntityTypeConfiguration<CODCollection>
{
    public void Configure(EntityTypeBuilder<CODCollection> builder)
    {
        builder.ToTable("CODCollections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ExpectedAmount).HasPrecision(18, 2);
        builder.Property(c => c.ShipmentTotalCharge).HasPrecision(18, 2);
        builder.Property(c => c.CollectedAmount).HasPrecision(18, 2);

        builder.HasIndex(c => c.OrganizationId);
        builder.HasIndex(c => c.MerchantId);
        builder.HasIndex(c => c.BranchId);
        builder.HasIndex(c => c.Status);
        // At most one CODCollection per shipment ever — enforced at the database, not just by
        // CreateExpectedAsync only being called once per shipment at booking (this plan's eager
        // creation decision).
        builder.HasIndex(c => c.ShipmentId).IsUnique();

        // No FK to Shipment (ShipmentId) or DeliveryProof (DeliveryProofId) — Cod has no project
        // reference to Shipments or Riders' EF model, same cross-module precedent as
        // ManifestShipment.ShipmentId (Phase 7) and NDR.ShipmentId (Phase 9).
        builder.HasOne<RiderCashSettlement>().WithMany().HasForeignKey(c => c.RiderCashSettlementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MerchantSettlementItem>().WithMany().HasForeignKey(c => c.MerchantSettlementItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 2: `RiderCashSettlement` and `BranchCashSettlement`**

`src/Modules/Cod/Infrastructure/RiderCashSettlementConfiguration.cs`:

```csharp
using Courier.Branches.Domain;
using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Cod.Infrastructure;

public class RiderCashSettlementConfiguration : IEntityTypeConfiguration<RiderCashSettlement>
{
    public void Configure(EntityTypeBuilder<RiderCashSettlement> builder)
    {
        builder.ToTable("RiderCashSettlements");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.DeclaredAmount).HasPrecision(18, 2);

        builder.HasIndex(r => r.OrganizationId);
        builder.HasIndex(r => r.RiderId);
        builder.HasIndex(r => r.BranchCashSettlementId);

        builder.HasOne<Branch>().WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BranchCashSettlement>().WithMany().HasForeignKey(r => r.BranchCashSettlementId).OnDelete(DeleteBehavior.Restrict);
        // No FK to Rider (RiderId) — Cod has no project reference to Riders' EF model.
    }
}
```

`src/Modules/Cod/Infrastructure/BranchCashSettlementConfiguration.cs`:

```csharp
using Courier.Branches.Domain;
using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Cod.Infrastructure;

public class BranchCashSettlementConfiguration : IEntityTypeConfiguration<BranchCashSettlement>
{
    public void Configure(EntityTypeBuilder<BranchCashSettlement> builder)
    {
        builder.ToTable("BranchCashSettlements");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.ConfirmedAmount).HasPrecision(18, 2);

        builder.HasIndex(b => b.OrganizationId);
        builder.HasIndex(b => b.BranchId);

        builder.HasOne<Branch>().WithMany().HasForeignKey(b => b.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 3: `FinancialTransaction`**

`src/Modules/Cod/Infrastructure/FinancialTransactionConfiguration.cs`:

```csharp
using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Cod.Infrastructure;

public class FinancialTransactionConfiguration : IEntityTypeConfiguration<FinancialTransaction>
{
    public void Configure(EntityTypeBuilder<FinancialTransaction> builder)
    {
        builder.ToTable("FinancialTransactions");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Amount).HasPrecision(18, 2);
        builder.Property(f => f.ReferenceEntityType).HasMaxLength(100).IsRequired();

        builder.HasIndex(f => f.OrganizationId);
        // Unique, not just a lookup index: enforces "at most one FinancialTransaction per
        // (ReferenceEntityType, ReferenceEntityId)" at the database — CreateFinanceVerificationAsync
        // (Task 9) also does a pre-check AnyAsync for a clean error message in the common case, but
        // a bare pre-check is a TOCTOU race under concurrent requests (Phase 5's exact rate-card
        // versioning lesson, commit a83c320) — only this constraint, backed by a caught
        // DbUpdateException on the actual insert, makes "verify the same BranchCashSettlement twice"
        // truly impossible under concurrency. Proven by Task 14's Postgres concurrency test.
        builder.HasIndex(f => new { f.ReferenceEntityType, f.ReferenceEntityId }).IsUnique();
    }
}
```

- [ ] **Step 4: `MerchantSettlement` and `MerchantSettlementItem`**

`src/Modules/Cod/Infrastructure/MerchantSettlementConfiguration.cs`:

```csharp
using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Cod.Infrastructure;

public class MerchantSettlementConfiguration : IEntityTypeConfiguration<MerchantSettlement>
{
    public void Configure(EntityTypeBuilder<MerchantSettlement> builder)
    {
        builder.ToTable("MerchantSettlements");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.TotalPayable).HasPrecision(18, 2);

        builder.HasIndex(m => m.OrganizationId);
        builder.HasIndex(m => m.MerchantId);
        // No FK to Merchant — Cod's project reference to Merchants is Application-layer only
        // (IMerchantLookupService), not its EF model, same cross-module precedent as
        // Shipment.MerchantId (Phase 6).
    }
}
```

`src/Modules/Cod/Infrastructure/MerchantSettlementItemConfiguration.cs`:

```csharp
using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Cod.Infrastructure;

public class MerchantSettlementItemConfiguration : IEntityTypeConfiguration<MerchantSettlementItem>
{
    public void Configure(EntityTypeBuilder<MerchantSettlementItem> builder)
    {
        builder.ToTable("MerchantSettlementItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.CODAmount).HasPrecision(18, 2);
        builder.Property(i => i.CourierFee).HasPrecision(18, 2);
        builder.Property(i => i.OtherDeductions).HasPrecision(18, 2);
        builder.Property(i => i.NetPayable).HasPrecision(18, 2);

        builder.HasIndex(i => i.ShipmentId).IsUnique();
        // A shipment can only ever be folded into one MerchantSettlement — enforced here, matching
        // CODCollection.MerchantSettlementItemId's stamped-once usage in CreateMerchantSettlementAsync
        // (Task 9).

        builder.HasOne<MerchantSettlement>().WithMany().HasForeignKey(i => i.MerchantSettlementId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 5: `Adjustment`**

`src/Modules/Cod/Infrastructure/AdjustmentConfiguration.cs`:

```csharp
using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Cod.Infrastructure;

public class AdjustmentConfiguration : IEntityTypeConfiguration<Adjustment>
{
    public void Configure(EntityTypeBuilder<Adjustment> builder)
    {
        builder.ToTable("Adjustments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.BeforeValue).HasPrecision(18, 2);
        builder.Property(a => a.AfterValue).HasPrecision(18, 2);

        builder.HasIndex(a => a.OrganizationId);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
    }
}
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Modules/Cod/Cod.csproj`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Cod/Infrastructure/
git commit -m "feat(cod): add EF Core configurations for Cod module entities"
```

---

## Task 3: Persistence wiring — `AppDbContext`, `Persistence.csproj`, `Api.csproj`, `Courier.slnx`, EF migration

**Files:**
- Modify: `src/Persistence/AppDbContext.cs`
- Modify: `src/Persistence/Persistence.csproj`
- Modify: `src/Api/Api.csproj`
- Modify: `Courier.slnx`
- Create (generated): `src/Persistence/Migrations/*_AddCod.cs` and `.Designer.cs`, updated `AppDbContextModelSnapshot.cs`

**Interfaces:**
- Produces: `AppDbContext`'s seven new `DbSet<T>` properties Tasks 4–11's `Cod.Application.CodService` uses via `_dbContext.Set<T>()`.

- [ ] **Step 1: Add usings and `DbSet`s**

In `src/Persistence/AppDbContext.cs`, add after the `Ndr`-module usings:

```csharp
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
```

Add after the existing `DbSet<ReturnShipment> ReturnShipments` line:

```csharp
    public DbSet<CODCollection> CODCollections => Set<CODCollection>();
    public DbSet<RiderCashSettlement> RiderCashSettlements => Set<RiderCashSettlement>();
    public DbSet<BranchCashSettlement> BranchCashSettlements => Set<BranchCashSettlement>();
    public DbSet<FinancialTransaction> FinancialTransactions => Set<FinancialTransaction>();
    public DbSet<MerchantSettlement> MerchantSettlements => Set<MerchantSettlement>();
    public DbSet<MerchantSettlementItem> MerchantSettlementItems => Set<MerchantSettlementItem>();
    public DbSet<Adjustment> Adjustments => Set<Adjustment>();
```

- [ ] **Step 2: Apply the seven configurations**

Add after the existing `modelBuilder.ApplyConfiguration(new ReturnShipmentConfiguration());` line:

```csharp
        modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
        modelBuilder.ApplyConfiguration(new RiderCashSettlementConfiguration());
        modelBuilder.ApplyConfiguration(new BranchCashSettlementConfiguration());
        modelBuilder.ApplyConfiguration(new FinancialTransactionConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantSettlementConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantSettlementItemConfiguration());
        modelBuilder.ApplyConfiguration(new AdjustmentConfiguration());
```

- [ ] **Step 3: Centralized Organization FK wiring**

Add after the existing `Ndr`/`ReturnShipment` `HasOne<Organization>()` block:

```csharp
        // Same reasoning for the six Cod entities below that carry their own OrganizationId: Cod
        // has no project reference to Identity. MerchantSettlementItem is excluded (no own
        // OrganizationId — see MerchantSettlementItemConfiguration).
        modelBuilder.Entity<CODCollection>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RiderCashSettlement>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BranchCashSettlement>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(b => b.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<FinancialTransaction>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(f => f.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MerchantSettlement>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Adjustment>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 4: Tenant query filters**

Add after the existing `Ndr`/`ReturnShipment` `HasQueryFilter` lines:

```csharp
        modelBuilder.Entity<CODCollection>().HasQueryFilter(c => _currentTenantProvider.OrganizationId != null && c.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<RiderCashSettlement>().HasQueryFilter(r => _currentTenantProvider.OrganizationId != null && r.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<BranchCashSettlement>().HasQueryFilter(b => _currentTenantProvider.OrganizationId != null && b.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<FinancialTransaction>().HasQueryFilter(f => _currentTenantProvider.OrganizationId != null && f.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<MerchantSettlement>().HasQueryFilter(m => _currentTenantProvider.OrganizationId != null && m.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Adjustment>().HasQueryFilter(a => _currentTenantProvider.OrganizationId != null && a.OrganizationId == _currentTenantProvider.OrganizationId);
```

Also add, immediately after those, a comment documenting the one deliberately-unfiltered entity (matching the existing comment style at the bottom of `OnModelCreating`):

```csharp
        // No tenant filter on MerchantSettlementItem: no own OrganizationId — tenant isolation
        // flows transitively through MerchantSettlementId -> MerchantSettlement.OrganizationId
        // (which does have a filter, above). Every query against this table in
        // Cod.Application.CodService scopes through a MerchantSettlement the caller's tenant
        // already owns.
```

- [ ] **Step 5: Add project references**

In `src/Persistence/Persistence.csproj`, add after the existing `<ProjectReference Include="..\Modules\Ndr\Ndr.csproj" />` line:

```xml
    <ProjectReference Include="..\Modules\Cod\Cod.csproj" />
```

In `src/Api/Api.csproj`, add after the same `Ndr.csproj` reference line:

```xml
    <ProjectReference Include="..\Modules\Cod\Cod.csproj" />
```

In `Courier.slnx`, add inside the `/src/Modules/` folder, after the `Ndr.csproj` entry:

```xml
    <Project Path="src/Modules/Cod/Cod.csproj" />
```

- [ ] **Step 6: Build to confirm the model wires up**

Run: `dotnet build src/Api/Api.csproj`
Expected: Build succeeds.

- [ ] **Step 7: Generate the migration**

Run: `dotnet ef migrations add AddCod --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: A new migration file is created under `src/Persistence/Migrations/` creating `CODCollections`, `RiderCashSettlements`, `BranchCashSettlements`, `FinancialTransactions`, `MerchantSettlements`, `MerchantSettlementItems`, `Adjustments` tables with the FKs/indexes from Task 2, and `AppDbContextModelSnapshot.cs` is updated. Confirm by reading the generated migration file that no unrelated tables changed.

- [ ] **Step 8: Apply the migration against the shared local Postgres**

Run: `docker exec courier-postgres-1 pg_isready -U courier` to confirm the shared container (port 5434) is up first — do not `docker compose up` a second one. Then:
`dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: Migration applies cleanly with no errors.

- [ ] **Step 9: Commit**

```bash
git add src/Persistence/AppDbContext.cs src/Persistence/Persistence.csproj src/Api/Api.csproj Courier.slnx src/Persistence/Migrations/
git commit -m "feat(cod): wire Cod module into AppDbContext with migration"
```

---

## Task 4: Cod Application core — DTOs, `ICodCollectionCreationService`, `ICodCollectionService`, `CodService` scaffold

**Files:**
- Create: `src/Modules/Cod/Application/CodDtos.cs`
- Create: `src/Modules/Cod/Application/ICodCollectionCreationService.cs`
- Create: `src/Modules/Cod/Application/ICodCollectionService.cs`
- Create: `src/Modules/Cod/Application/CodService.cs`
- Create: `src/Modules/Cod/Application/CodService.Collections.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/Cod.Tests/Cod.Tests.csproj`
- Modify: `Courier.slnx`
- Test: `tests/Cod.Tests/CodServiceCollectionsTests.cs`

**Interfaces:**
- Produces: `ICodCollectionCreationService.CreateExpectedAsync(Guid shipmentId, Guid organizationId, Guid merchantId, Guid branchId, decimal expectedAmount, decimal shipmentTotalCharge, ClaimsPrincipal actingUser, CancellationToken) -> Task` (Task 5's `ShipmentService.CreateShipmentAsync` consumes this) and `ICodCollectionService.MarkCollectedAsync(Guid shipmentId, decimal collectedAmount, Guid riderId, Guid deliveryProofId, ClaimsPrincipal actingUser, CancellationToken) -> Task` (Task 6's `RiderService.CompleteDeliveryAsync` consumes this).

- [ ] **Step 1: Create the test project**

`tests/Cod.Tests/Cod.Tests.csproj` — mirrors `tests/Ndr.Tests/Ndr.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.11" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Modules\Cod\Cod.csproj" />
  </ItemGroup>

</Project>
```

Add it to `Courier.slnx`'s `/tests/` folder, after the `Branches.Tests.csproj` entry (alphabetical, matching the existing ordering): `<Project Path="tests/Cod.Tests/Cod.Tests.csproj" />`.

- [ ] **Step 2: DTOs and exception type**

`src/Modules/Cod/Application/CodDtos.cs` — this file grows across later tasks (7–13); this step adds only what Task 4 itself needs:

```csharp
using Courier.Cod.Domain;

namespace Courier.Cod.Application;

public record CodCollectionResponse(
    Guid Id, Guid ShipmentId, Guid MerchantId, Guid BranchId, decimal ExpectedAmount, decimal ShipmentTotalCharge,
    decimal? CollectedAmount, CodCollectionStatus Status, Guid? CollectedByRiderId, DateTimeOffset? CollectedAt,
    Guid? DeliveryProofId, Guid? RiderCashSettlementId, Guid? MerchantSettlementItemId);

public class CodValidationException : Exception
{
    public CodValidationException(string message) : base(message) { }
}
```

- [ ] **Step 3: `ICodCollectionCreationService` and `ICodCollectionService`**

`src/Modules/Cod/Application/ICodCollectionCreationService.cs`:

```csharp
using System.Security.Claims;

namespace Courier.Cod.Application;

// Called exactly once this phase: Shipments.Application.ShipmentService.CreateShipmentAsync, right
// after a shipment with CodAmount > 0 is persisted (Task 5) — the narrow cross-module-write
// interface pattern established by IUserProvisioningService (Phase 8) and INdrCreationService
// (Phase 9). expectedAmount/shipmentTotalCharge/merchantId/branchId are snapshotted onto the new
// CODCollection row rather than looked up later (see this plan's Global Constraints).
public interface ICodCollectionCreationService
{
    Task CreateExpectedAsync(Guid shipmentId, Guid organizationId, Guid merchantId, Guid branchId, decimal expectedAmount, decimal shipmentTotalCharge, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
}
```

`src/Modules/Cod/Application/ICodCollectionService.cs`:

```csharp
using System.Security.Claims;

namespace Courier.Cod.Application;

// Called exactly once this phase: Riders.Application.RiderService.CompleteDeliveryAsync, right
// after the DeliveryProof row is persisted (Task 6) — same narrow cross-module-write pattern as
// ICodCollectionCreationService above.
public interface ICodCollectionService
{
    Task MarkCollectedAsync(Guid shipmentId, decimal collectedAmount, Guid riderId, Guid deliveryProofId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: `CodService` constructor and shared helpers**

`src/Modules/Cod/Application/CodService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Domain;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

public partial class CodService : ICodCollectionCreationService, ICodCollectionService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly IMerchantLookupService _merchantLookupService;

    public CodService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IBranchLookupService branchLookupService,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IMerchantLookupService merchantLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchLookupService = branchLookupService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _merchantLookupService = merchantLookupService;
    }

    private async Task CheckBranchAccessAsync(ClaimsPrincipal actingUser, string permission, Guid branchId, CancellationToken cancellationToken)
    {
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, permission, cancellationToken);
        if (accessible is not null && !accessible.Contains(branchId))
        {
            throw new CodValidationException($"Branch is outside your branch access for permission '{permission}'.");
        }
    }

    private static CodCollectionResponse ToResponse(CODCollection collection) => new(
        collection.Id, collection.ShipmentId, collection.MerchantId, collection.BranchId, collection.ExpectedAmount,
        collection.ShipmentTotalCharge, collection.CollectedAmount, collection.Status, collection.CollectedByRiderId,
        collection.CollectedAt, collection.DeliveryProofId, collection.RiderCashSettlementId, collection.MerchantSettlementItemId);
}
```

- [ ] **Step 5: `CreateExpectedAsync` and `MarkCollectedAsync`**

`src/Modules/Cod/Application/CodService.Collections.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Cod.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

public partial class CodService
{
    public async Task CreateExpectedAsync(Guid shipmentId, Guid organizationId, Guid merchantId, Guid branchId, decimal expectedAmount, decimal shipmentTotalCharge, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var collection = new CODCollection
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            OrganizationId = organizationId,
            MerchantId = merchantId,
            BranchId = branchId,
            ExpectedAmount = expectedAmount,
            ShipmentTotalCharge = shipmentTotalCharge,
            Status = CodCollectionStatus.CodExpected
        };
        _dbContext.Set<CODCollection>().Add(collection);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "cod.collection_expected", "CODCollection", collection.Id.ToString()), cancellationToken);
    }

    public async Task MarkCollectedAsync(Guid shipmentId, decimal collectedAmount, Guid riderId, Guid deliveryProofId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var collection = await _dbContext.Set<CODCollection>().FirstOrDefaultAsync(c => c.ShipmentId == shipmentId, cancellationToken)
            ?? throw new CodValidationException("No CODCollection exists for this shipment.");
        if (collection.Status != CodCollectionStatus.CodExpected)
        {
            throw new CodValidationException($"CODCollection must be CodExpected to record a collection, was {collection.Status}.");
        }

        collection.CollectedAmount = collectedAmount;
        collection.CollectedByRiderId = riderId;
        collection.CollectedAt = DateTimeOffset.UtcNow;
        collection.DeliveryProofId = deliveryProofId;
        collection.Status = CodCollectionStatus.CodCollected;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "cod.collected", "CODCollection", collection.Id.ToString()), cancellationToken);
    }
}
```

- [ ] **Step 6: Wire into `Program.cs`**

In `src/Api/Program.cs`, add `using Courier.Cod.Application;` to the usings, and after the existing `builder.Services.AddScoped<INdrCreationService, NdrCreationService>();` line:

```csharp
builder.Services.AddScoped<CodService>();
builder.Services.AddScoped<ICodCollectionCreationService>(sp => sp.GetRequiredService<CodService>());
builder.Services.AddScoped<ICodCollectionService>(sp => sp.GetRequiredService<CodService>());
```

(Mirrors `IShipmentTransitionService`'s and `IBranchLookupService`'s existing factory-lambda registration shape — one concrete `CodService` instance per request satisfies both narrow interfaces.)

- [ ] **Step 7: Write the failing tests**

Create `tests/Cod.Tests/CodServiceCollectionsTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Cod.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class AlwaysTrueBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public class CodServiceCollectionsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
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

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        new AllBranchesAuthorizationChecker(), new AlwaysTrueMerchantLookup());

    [Fact]
    public async Task CreateExpectedAsync_PersistsRowWithCodExpectedStatus()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);
        var shipmentId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        await service.CreateExpectedAsync(shipmentId, organizationId, merchantId, branchId, 2500m, 150m, ActingUser(Guid.NewGuid()));

        var collection = await dbContext.Set<CODCollection>().SingleAsync(c => c.ShipmentId == shipmentId);
        Assert.Equal(CodCollectionStatus.CodExpected, collection.Status);
        Assert.Equal(2500m, collection.ExpectedAmount);
        Assert.Equal(150m, collection.ShipmentTotalCharge);
        Assert.Equal(merchantId, collection.MerchantId);
        Assert.Equal(branchId, collection.BranchId);
        Assert.Null(collection.CollectedAmount);
    }

    [Fact]
    public async Task MarkCollectedAsync_FromCodExpected_TransitionsToCodCollected()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);
        var shipmentId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var deliveryProofId = Guid.NewGuid();
        await service.CreateExpectedAsync(shipmentId, organizationId, Guid.NewGuid(), Guid.NewGuid(), 2500m, 150m, ActingUser(Guid.NewGuid()));

        await service.MarkCollectedAsync(shipmentId, 2500m, riderId, deliveryProofId, ActingUser(Guid.NewGuid()));

        var collection = await dbContext.Set<CODCollection>().SingleAsync(c => c.ShipmentId == shipmentId);
        Assert.Equal(CodCollectionStatus.CodCollected, collection.Status);
        Assert.Equal(2500m, collection.CollectedAmount);
        Assert.Equal(riderId, collection.CollectedByRiderId);
        Assert.Equal(deliveryProofId, collection.DeliveryProofId);
        Assert.NotNull(collection.CollectedAt);
    }

    [Fact]
    public async Task MarkCollectedAsync_NoCodCollectionForShipment_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.MarkCollectedAsync(Guid.NewGuid(), 100m, Guid.NewGuid(), Guid.NewGuid(), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task MarkCollectedAsync_AlreadyCollected_ThrowsAndDoesNotOverwrite()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);
        var shipmentId = Guid.NewGuid();
        await service.CreateExpectedAsync(shipmentId, organizationId, Guid.NewGuid(), Guid.NewGuid(), 2500m, 150m, ActingUser(Guid.NewGuid()));
        await service.MarkCollectedAsync(shipmentId, 2500m, Guid.NewGuid(), Guid.NewGuid(), ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.MarkCollectedAsync(shipmentId, 999m, Guid.NewGuid(), Guid.NewGuid(), ActingUser(Guid.NewGuid())));

        var collection = await dbContext.Set<CODCollection>().SingleAsync(c => c.ShipmentId == shipmentId);
        Assert.Equal(2500m, collection.CollectedAmount);
    }
}
```

- [ ] **Step 8: Run the new tests**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj`
Expected: All 4 tests pass.

- [ ] **Step 9: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeds with zero errors.

- [ ] **Step 10: Commit**

```bash
git add src/Modules/Cod/Application/CodDtos.cs src/Modules/Cod/Application/ICodCollectionCreationService.cs src/Modules/Cod/Application/ICodCollectionService.cs src/Modules/Cod/Application/CodService.cs src/Modules/Cod/Application/CodService.Collections.cs src/Api/Program.cs tests/Cod.Tests/ Courier.slnx
git commit -m "feat(cod): add CodService with CreateExpectedAsync/MarkCollectedAsync"
```

---

## Task 5: Shipments hook — `CODCollection` creation at booking

**Files:**
- Modify: `src/Modules/Shipments/Application/IShipmentTransitionService.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentService.Reads.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentService.cs`
- Modify: `src/Modules/Shipments/Shipments.csproj`
- Test: `tests/Shipments.Tests/ShipmentServiceCodTests.cs` (new)

**Interfaces:**
- Consumes: `ICodCollectionCreationService.CreateExpectedAsync` (Task 4).
- Produces: `ShipmentScanInfo.CodAmount` (trailing optional parameter) — Task 6's `RiderService.CompleteDeliveryAsync` consumes this to decide whether to require `CollectedCodAmount` and call `ICodCollectionService.MarkCollectedAsync`.

- [ ] **Step 1: Add `CodAmount` to `ShipmentScanInfo` as a trailing optional parameter**

In `src/Modules/Shipments/Application/IShipmentTransitionService.cs`, change the `ShipmentScanInfo` record declaration from:

```csharp
public record ShipmentScanInfo(
    Guid ShipmentId,
    Guid OrganizationId,
    string Awb,
    ShipmentStatus CurrentStatus,
    Guid? CurrentBranchId,
    Guid OriginBranchId,
    Guid DestinationBranchId,
    Guid? CurrentCustodianRiderId);
```

to:

```csharp
public record ShipmentScanInfo(
    Guid ShipmentId,
    Guid OrganizationId,
    string Awb,
    ShipmentStatus CurrentStatus,
    Guid? CurrentBranchId,
    Guid OriginBranchId,
    Guid DestinationBranchId,
    Guid? CurrentCustodianRiderId,
    // Phase 10 — trailing optional parameter defaulting to 0m, exactly like Manifest.Purpose's
    // precedent (CLARIFICATIONS.md #9): 11 existing `new ShipmentScanInfo(...)` call sites across
    // Manifests.Tests/Ndr.Tests construct this record positionally with 8 arguments and must keep
    // compiling unchanged. Only Riders.Application.RiderService.CompleteDeliveryAsync (Task 6)
    // reads this field; every one of those 11 test call sites defaults to CodAmount = 0m, which is
    // correct for all of them (none exercise COD collection).
    decimal CodAmount = 0m);
```

- [ ] **Step 2: Populate it in `ToScanInfo`**

In `src/Modules/Shipments/Application/ShipmentService.Reads.cs`, change:

```csharp
    private static ShipmentScanInfo ToScanInfo(Shipment shipment) => new(
        shipment.Id, shipment.OrganizationId, shipment.Awb, shipment.CurrentStatus,
        shipment.CurrentBranchId, shipment.OriginBranchId, shipment.DestinationBranchId,
        shipment.CurrentCustodianRiderId);
```

to:

```csharp
    private static ShipmentScanInfo ToScanInfo(Shipment shipment) => new(
        shipment.Id, shipment.OrganizationId, shipment.Awb, shipment.CurrentStatus,
        shipment.CurrentBranchId, shipment.OriginBranchId, shipment.DestinationBranchId,
        shipment.CurrentCustodianRiderId, shipment.CodAmount);
```

- [ ] **Step 3: Take a new `ICodCollectionCreationService` dependency and call it from `CreateShipmentAsync`**

In `src/Modules/Shipments/Application/ShipmentService.cs`, add `using Courier.Cod.Application;` to the usings, add a new field and constructor parameter:

```csharp
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly IOrganizationLookupService _organizationLookupService;
    private readonly ICodCollectionCreationService _codCollectionCreationService;

    public ShipmentService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IBranchLookupService branchLookupService,
        IGeographyLookupService geographyLookupService,
        IMerchantLookupService merchantLookupService,
        PricingService pricingService,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IOrganizationLookupService organizationLookupService,
        ICodCollectionCreationService codCollectionCreationService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchLookupService = branchLookupService;
        _geographyLookupService = geographyLookupService;
        _merchantLookupService = merchantLookupService;
        _pricingService = pricingService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _organizationLookupService = organizationLookupService;
        _codCollectionCreationService = codCollectionCreationService;
    }
```

(This replaces the existing field list and constructor in full — every existing field/assignment stays, only the new field/parameter/assignment is added.)

Then, in the same file, in `CreateShipmentAsync`, immediately after the existing `await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "shipment.created", "Shipment", shipment.Id.ToString()), cancellationToken);` line and before `return await LoadResponseAsync(shipment.Id, cancellationToken);`, add:

```csharp
        if (shipment.CodAmount > 0)
        {
            await _codCollectionCreationService.CreateExpectedAsync(
                shipment.Id, organizationId, merchantId, request.DestinationBranchId, shipment.CodAmount, shipment.TotalCharge, actingUser, cancellationToken);
        }
```

(`request.DestinationBranchId` is used rather than `shipment.CurrentBranchId` — at `Created` status `CurrentBranchId` already equals `request.OriginBranchId`, not the destination; `CODCollection.BranchId` must be the *destination* branch, the one that will actually attempt delivery and collect the cash, per this plan's Global Constraints and `CODCollection`'s own doc comment.)

- [ ] **Step 4: Add the project reference**

In `src/Modules/Shipments/Shipments.csproj`, add inside the existing `<ItemGroup>` of `<ProjectReference>` entries, after the `Pricing.csproj` reference:

```xml
    <ProjectReference Include="..\Cod\Cod.csproj" />
```

- [ ] **Step 5: Write the failing test**

Create `tests/Shipments.Tests/ShipmentServiceCodTests.cs` — reuses the exact fixtures/fakes/`CreateContext`/`ActingUser` helpers already defined in `tests/Shipments.Tests/ShipmentServiceTransitionTests.cs` (copy verbatim, same established per-file-helper convention), plus a new recording fake for `ICodCollectionCreationService`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
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

file class RecordingCodCollectionCreationService : ICodCollectionCreationService
{
    public List<(Guid ShipmentId, Guid MerchantId, Guid BranchId, decimal ExpectedAmount, decimal ShipmentTotalCharge)> Calls { get; } = new();

    public Task CreateExpectedAsync(Guid shipmentId, Guid organizationId, Guid merchantId, Guid branchId, decimal expectedAmount, decimal shipmentTotalCharge, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        Calls.Add((shipmentId, merchantId, branchId, expectedAmount, shipmentTotalCharge));
        return Task.CompletedTask;
    }
}

public class ShipmentServiceCodTests
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

    private static (ShipmentService Service, RecordingCodCollectionCreationService CodRecorder) CreateService(TestDbContext dbContext, Guid organizationId, Dictionary<Guid, Guid?> zonesByBranch)
    {
        var branchLookup = new FakeBranchLookup(zonesByBranch);
        var pricingService = new PricingService(dbContext, new FixedTenantProvider(organizationId), branchLookup, new AlwaysTrueMerchantLookup());
        var codRecorder = new RecordingCodCollectionCreationService();
        var service = new ShipmentService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchLookup,
            new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup(), pricingService, new AllBranchesAuthorizationChecker(),
            new FixedOrganizationLookup("testorg"), codRecorder);
        return (service, codRecorder);
    }

    private async Task<(Guid OriginBranchId, Guid DestinationBranchId, Guid ServiceTypeId)> SeedRateCardAsync(TestDbContext dbContext, Guid organizationId)
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

        return (originBranchId, destinationBranchId, serviceTypeId);
    }

    private static CreateShipmentRequest ShipmentRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, decimal codAmount) => new(
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, codAmount, false,
        "Recipient", "9800000000", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Recipient Address",
        "Sender", "9800000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item", 1, 100m) });

    [Fact]
    public async Task CreateShipmentAsync_WithCodAmount_CallsCodCollectionCreationServiceWithDestinationBranch()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRateCardAsync(dbContext, organizationId);
        var (service, codRecorder) = CreateService(dbContext, organizationId, new() { [originBranchId] = Guid.NewGuid(), [destinationBranchId] = Guid.NewGuid() });
        var merchantId = Guid.NewGuid();

        var response = await service.CreateShipmentAsync(ShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId, 2500m), ActingUser(Guid.NewGuid()));

        var call = Assert.Single(codRecorder.Calls);
        Assert.Equal(response.Id, call.ShipmentId);
        Assert.Equal(merchantId, call.MerchantId);
        Assert.Equal(destinationBranchId, call.BranchId);
        Assert.Equal(2500m, call.ExpectedAmount);
        Assert.Equal(response.TotalCharge, call.ShipmentTotalCharge);
    }

    [Fact]
    public async Task CreateShipmentAsync_WithZeroCodAmount_NeverCallsCodCollectionCreationService()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRateCardAsync(dbContext, organizationId);
        var (service, codRecorder) = CreateService(dbContext, organizationId, new() { [originBranchId] = Guid.NewGuid(), [destinationBranchId] = Guid.NewGuid() });

        await service.CreateShipmentAsync(ShipmentRequest(Guid.NewGuid(), originBranchId, destinationBranchId, serviceTypeId, 0m), ActingUser(Guid.NewGuid()));

        Assert.Empty(codRecorder.Calls);
    }
}
```

- [ ] **Step 6: Run the new tests**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter "FullyQualifiedName~ShipmentServiceCodTests"`
Expected: Both tests pass.

- [ ] **Step 7: Build the whole solution and run every existing test project**

Run: `dotnet build`
Expected: Build succeeds — every other `ShipmentService` construction call site across `Shipments.Tests`/`Manifests.Tests`/`Riders.Tests`/`Ndr.Tests`/`Api.Tests` fails to compile until updated. Search for every remaining `new ShipmentService(` call site (there is exactly one more, in `tests/Shipments.Tests/ShipmentServiceTransitionTests.cs`'s own `CreateService` helper, and one more in `tests/Shipments.Tests/ShipmentServiceNdrRtoTransitionTests.cs`'s `CreateService` helper) and add a trailing `new RecordingCodCollectionCreationService()`-style no-op argument — for these two files, which don't test COD behavior, add this minimal file-scoped fake directly in each (not the recording one above, just enough to satisfy the constructor):

```csharp
file class NoopCodCollectionCreationService : ICodCollectionCreationService
{
    public Task CreateExpectedAsync(Guid shipmentId, Guid organizationId, Guid merchantId, Guid branchId, decimal expectedAmount, decimal shipmentTotalCharge, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

Add `using Courier.Cod.Application;` to both files' usings, add the fake class above to both, and append `, new NoopCodCollectionCreationService()` as the final constructor argument in both files' `CreateService`/equivalent helper's `new ShipmentService(...)` call.

Run: `dotnet test`
Expected: Every test project passes.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Shipments/Application/IShipmentTransitionService.cs src/Modules/Shipments/Application/ShipmentService.Reads.cs src/Modules/Shipments/Application/ShipmentService.cs src/Modules/Shipments/Shipments.csproj tests/Shipments.Tests/ShipmentServiceCodTests.cs tests/Shipments.Tests/ShipmentServiceTransitionTests.cs tests/Shipments.Tests/ShipmentServiceNdrRtoTransitionTests.cs
git commit -m "feat(shipments): create CODCollection at booking for COD shipments"
```

---

## Task 6: Riders hook — COD collection at delivery completion

**Files:**
- Modify: `src/Modules/Riders/Application/RiderDtos.cs`
- Modify: `src/Modules/Riders/Application/RiderService.cs`
- Modify: `src/Modules/Riders/Application/RiderService.Attempts.cs`
- Modify: `src/Modules/Riders/Riders.csproj`
- Modify: `tests/Riders.Tests/RiderServiceCreationTests.cs`
- Modify: `tests/Riders.Tests/RiderServiceTasksTests.cs`
- Modify: `tests/Riders.Tests/RiderServiceReadsTests.cs`
- Modify: `tests/Riders.Tests/RiderServiceAttemptsTests.cs`
- Test: `tests/Riders.Tests/RiderServiceAttemptsTests.cs` (new cases added to the existing file)

**Interfaces:**
- Consumes: `ICodCollectionService.MarkCollectedAsync` (Task 4), `ShipmentScanInfo.CodAmount` (Task 5).

- [ ] **Step 1: Add `CollectedCodAmount` to `CompleteDeliveryRequest`**

In `src/Modules/Riders/Application/RiderDtos.cs`, change:

```csharp
public record CompleteDeliveryRequest(Guid ShipmentId, string RecipientName, bool OtpVerified, string? SignatureStorageKey, string? PhotoStorageKey, decimal? Latitude, decimal? Longitude);
```

to:

```csharp
public record CompleteDeliveryRequest(Guid ShipmentId, string RecipientName, bool OtpVerified, string? SignatureStorageKey, string? PhotoStorageKey, decimal? Latitude, decimal? Longitude, decimal? CollectedCodAmount = null);
```

(Trailing optional parameter, same reasoning as `ShipmentScanInfo.CodAmount` in Task 5 — every existing `new CompleteDeliveryRequest(...)` call site across `Riders.Tests`/`Api.Tests` keeps compiling unchanged, correctly defaulting to "no COD collected" for shipments that never carry COD in those tests.)

- [ ] **Step 2: Take `ICodCollectionService` and `IPermissionChecker` as new `RiderService` dependencies**

In `src/Modules/Riders/Application/RiderService.cs`, add `using Courier.Cod.Application;` to the usings, and change the field list/constructor:

```csharp
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IObjectStorage _objectStorage;
    private readonly INdrCreationService _ndrCreationService;
    private readonly ICodCollectionService _codCollectionService;
    private readonly IPermissionChecker _permissionChecker;

    public RiderService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IUserProvisioningService userProvisioningService,
        IBranchLookupService branchLookupService,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IShipmentTransitionService shipmentTransitionService,
        IObjectStorage objectStorage,
        INdrCreationService ndrCreationService,
        ICodCollectionService codCollectionService,
        IPermissionChecker permissionChecker)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _userProvisioningService = userProvisioningService;
        _branchLookupService = branchLookupService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _shipmentTransitionService = shipmentTransitionService;
        _objectStorage = objectStorage;
        _ndrCreationService = ndrCreationService;
        _codCollectionService = codCollectionService;
        _permissionChecker = permissionChecker;
    }
```

(`IPermissionChecker` is `Courier.SharedKernel.IPermissionChecker` — already registered in `Program.cs` as `builder.Services.AddScoped<IPermissionChecker, Courier.Identity.Infrastructure.PermissionChecker>();`, previously only consumed by the `RequirePermission` route filter; this is its first Application-layer consumer, for the conditional `cod.collect` check below that the route-level `.RequirePermission("delivery.complete")` alone can't express.)

- [ ] **Step 3: Gate and call it from `CompleteDeliveryAsync`**

In `src/Modules/Riders/Application/RiderService.Attempts.cs`, add `using Courier.SharedKernel;` if not already present (it already is, via `Courier.SharedKernel` for `GetUserId()`), then change `CompleteDeliveryAsync`. Immediately after the existing:

```csharp
        // Check 1 (permission) enforced at the route via .RequirePermission("delivery.complete");
        // check 2 (branch access) here.
        await CheckBranchAccessAsync(actingUser, "delivery.complete", scanInfo.DestinationBranchId, cancellationToken);
```

add:

```csharp
        // Phase 10: a COD shipment additionally requires cod.collect — checked here at the
        // Application layer via IPermissionChecker rather than the route, since the requirement is
        // conditional on the shipment (scanInfo.CodAmount > 0), not a blanket route-level gate.
        if (scanInfo.CodAmount > 0)
        {
            if (!await _permissionChecker.HasPermissionAsync(actingUser, "cod.collect", cancellationToken))
            {
                throw new RiderValidationException("cod.collect permission is required to complete a COD delivery.");
            }
            if (request.CollectedCodAmount is null)
            {
                throw new RiderValidationException("CollectedCodAmount is required to complete a COD delivery.");
            }
        }
```

Then, after the existing block that creates and persists `DeliveryProof` (immediately after `await _dbContext.SaveChangesAsync(cancellationToken);` that follows `_dbContext.Set<DeliveryProof>().Add(proof);`, and before the `DeliveryTask` completion block), add:

```csharp
        if (scanInfo.CodAmount > 0)
        {
            await _codCollectionService.MarkCollectedAsync(request.ShipmentId, request.CollectedCodAmount!.Value, riderId, proof.Id, actingUser, cancellationToken);
        }
```

(This lands after `proof.Id` exists, since `CODCollection.DeliveryProofId` must reference the real persisted row — matches this task's brief exactly.)

- [ ] **Step 4: Add the project reference**

In `src/Modules/Riders/Riders.csproj`, add inside the existing `<ItemGroup>` of `<ProjectReference>` entries, after the `Ndr.csproj` reference:

```xml
    <ProjectReference Include="..\Cod\Cod.csproj" />
```

- [ ] **Step 5: Update every fake `IShipmentTransitionService`/constructor call site in `Riders.Tests`**

`ShipmentScanInfo` already defaults `CodAmount` to `0m` (Task 5) so no fake needs updating for that. But `RiderService`'s constructor now takes two more parameters (`ICodCollectionService`, `IPermissionChecker`) — every `new RiderService(...)` call site across `Riders.Tests` needs two more arguments. Add this shared pair of file-scoped fakes to each of the four files listed below (same minimal-fake shape as `NoopCodCollectionCreationService` in Task 5):

```csharp
file class NoopCodCollectionService : ICodCollectionService
{
    public Task MarkCollectedAsync(Guid shipmentId, decimal collectedAmount, Guid riderId, Guid deliveryProofId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class AlwaysTruePermissionChecker : IPermissionChecker
{
    public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
```

Add `using Courier.Cod.Application;` to each file's usings (`IPermissionChecker` is `Courier.SharedKernel`, already imported everywhere `RiderService` is constructed). Append `, new NoopCodCollectionService(), new AlwaysTruePermissionChecker()` as the final two constructor arguments in every `new RiderService(...)` call site in:

- `tests/Riders.Tests/RiderServiceCreationTests.cs`
- `tests/Riders.Tests/RiderServiceTasksTests.cs`
- `tests/Riders.Tests/RiderServiceReadsTests.cs`
- `tests/Riders.Tests/RiderServiceAttemptsTests.cs` (this file additionally gets the new recording fakes below, replacing the plain no-op pair for its own `CreateService` helper only)

- [ ] **Step 6: Write the failing tests for the new gating/call behavior**

In `tests/Riders.Tests/RiderServiceAttemptsTests.cs`, replace the plain `NoopCodCollectionService`/`AlwaysTruePermissionChecker` pair for this file specifically with recording versions (same names, so Step 5's instruction to add them applies — this file's copies are these, not the plain ones):

```csharp
file class RecordingCodCollectionService : ICodCollectionService
{
    public List<(Guid ShipmentId, decimal CollectedAmount, Guid RiderId, Guid DeliveryProofId)> Calls { get; } = new();

    public Task MarkCollectedAsync(Guid shipmentId, decimal collectedAmount, Guid riderId, Guid deliveryProofId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        Calls.Add((shipmentId, collectedAmount, riderId, deliveryProofId));
        return Task.CompletedTask;
    }
}

file class ConfigurableCodPermissionChecker : IPermissionChecker
{
    public bool GrantCodCollect { get; set; } = true;
    public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(permission != "cod.collect" || GrantCodCollect);
}
```

Wire the file's `CreateService` helper to expose these two (mirroring how it already exposes any other recording fake it returns — read the file's existing `CreateService` return shape first and extend its tuple/out-parameters the same way, adding `RecordingCodCollectionService` and `ConfigurableCodPermissionChecker` alongside whatever it already returns, instead of the plain no-op pair from Step 5). Then add:

```csharp
    [Fact]
    public async Task CompleteDeliveryAsync_CodShipmentWithAmount_CallsMarkCollectedWithProofId()
    {
        // Uses this file's existing shipment-seeding helper (SeedOutForDeliveryShipmentAsync or
        // equivalent — read the file's existing successful-completion test for the exact helper
        // name/shape and reuse it verbatim), with scanInfo.CodAmount set > 0 via the file's fake
        // IShipmentTransitionService.
        var (service, riderId, shipmentId, codRecorder) = await SeedCodShipmentOutForDeliveryAsync(2500m);

        var result = await service.CompleteDeliveryAsync(riderId, new CompleteDeliveryRequest(shipmentId, "Recipient", true, null, null, null, null, 2500m), ActingUser(Guid.NewGuid()));

        var call = Assert.Single(codRecorder.Calls);
        Assert.Equal(shipmentId, call.ShipmentId);
        Assert.Equal(2500m, call.CollectedAmount);
        Assert.Equal(riderId, call.RiderId);
    }

    [Fact]
    public async Task CompleteDeliveryAsync_CodShipmentWithoutCollectedAmount_Throws()
    {
        var (service, riderId, shipmentId, _) = await SeedCodShipmentOutForDeliveryAsync(2500m);

        await Assert.ThrowsAsync<RiderValidationException>(() =>
            service.CompleteDeliveryAsync(riderId, new CompleteDeliveryRequest(shipmentId, "Recipient", true, null, null, null, null, null), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CompleteDeliveryAsync_CodShipmentWithoutCodCollectPermission_Throws()
    {
        var (service, riderId, shipmentId, _, permissionChecker) = await SeedCodShipmentOutForDeliveryAsyncWithPermissionChecker(2500m);
        permissionChecker.GrantCodCollect = false;

        await Assert.ThrowsAsync<RiderValidationException>(() =>
            service.CompleteDeliveryAsync(riderId, new CompleteDeliveryRequest(shipmentId, "Recipient", true, null, null, null, null, 2500m), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CompleteDeliveryAsync_NonCodShipment_NeverCallsMarkCollected()
    {
        var (service, riderId, shipmentId, codRecorder) = await SeedCodShipmentOutForDeliveryAsync(0m);

        await service.CompleteDeliveryAsync(riderId, new CompleteDeliveryRequest(shipmentId, "Recipient", true, null, null, null, null, null), ActingUser(Guid.NewGuid()));

        Assert.Empty(codRecorder.Calls);
    }
```

Implement `SeedCodShipmentOutForDeliveryAsync(decimal codAmount)` and `SeedCodShipmentOutForDeliveryAsyncWithPermissionChecker(decimal codAmount)` as thin wrappers around this file's existing out-for-delivery seeding helper, the only difference being the fake `IShipmentTransitionService.GetScanInfoAsync`'s returned `ShipmentScanInfo` carries `CodAmount: codAmount` and `CreateService` is built with a `RecordingCodCollectionService`/`ConfigurableCodPermissionChecker` pair instead of the plain no-ops — follow this file's existing helper-composition style exactly (read `RiderServiceAttemptsTests.cs`'s current successful-delivery test and its seeding helper in full before writing these two wrappers, since the exact fake-`IShipmentTransitionService` shape and constructor argument order must match what already exists in that file).

- [ ] **Step 7: Run the new tests**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj --filter "FullyQualifiedName~RiderServiceAttemptsTests"`
Expected: All tests in the file pass, including the four new ones.

- [ ] **Step 8: Build the whole solution and run every test project**

Run: `dotnet build`
Expected: Build succeeds.

Run: `dotnet test`
Expected: Every test project passes. `tests/Api.Tests` may need `new CompleteDeliveryRequest(...)` call sites checked (the new parameter is trailing-optional, so no existing call site should need changes) — confirm by running `dotnet test tests/Api.Tests/Api.Tests.csproj` explicitly and reading any compile errors if they occur.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Riders/Application/RiderDtos.cs src/Modules/Riders/Application/RiderService.cs src/Modules/Riders/Application/RiderService.Attempts.cs src/Modules/Riders/Riders.csproj tests/Riders.Tests/
git commit -m "feat(riders): collect COD at delivery completion, gated by cod.collect"
```

---

## Task 7: Rider cash settlements — `POST /cod/rider-settlements`

**Files:**
- Modify: `src/Modules/Cod/Application/CodDtos.cs`
- Create: `src/Modules/Cod/Application/CodService.Settlements.cs`
- Test: `tests/Cod.Tests/CodServiceRiderSettlementsTests.cs`

**Interfaces:**
- Produces: `CodService.CreateRiderSettlementAsync(CreateRiderSettlementRequest, ClaimsPrincipal, CancellationToken) -> Task<RiderCashSettlementResponse>` — Task 13's Presentation layer routes `POST /cod/rider-settlements` to this.

- [ ] **Step 1: Add the DTOs**

Append to `src/Modules/Cod/Application/CodDtos.cs`:

```csharp
public record CreateRiderSettlementRequest(Guid RiderId, Guid BranchId, decimal DeclaredAmount);

public record RiderCashSettlementResponse(
    Guid Id, Guid RiderId, Guid BranchId, decimal DeclaredAmount, int ShipmentCount,
    DateTimeOffset SettledAt, Guid ConfirmedByUserId, Guid? BranchCashSettlementId);
```

- [ ] **Step 2: Implement `CreateRiderSettlementAsync`**

`src/Modules/Cod/Application/CodService.Settlements.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Cod.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

public partial class CodService
{
    // Transactional (this plan's Global Constraints): covers a parent-row insert
    // (RiderCashSettlement) plus a bulk update of every CodCollected CODCollection row for this
    // rider — a crash between the two would leave collections stuck at CodCollected with no
    // RiderCashSettlement to point back to, silently losing the deposit record.
    public async Task<RiderCashSettlementResponse> CreateRiderSettlementAsync(CreateRiderSettlementRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new CodValidationException("Acting user could not be resolved.");

        if (await _branchLookupService.GetZoneInfoAsync(request.BranchId, cancellationToken) is null)
        {
            throw new CodValidationException("BranchId does not refer to a branch in this organization.");
        }
        await CheckBranchAccessAsync(actingUser, "cod.reconcile", request.BranchId, cancellationToken);

        var collections = await _dbContext.Set<CODCollection>()
            .Where(c => c.CollectedByRiderId == request.RiderId && c.BranchId == request.BranchId && c.Status == CodCollectionStatus.CodCollected)
            .ToListAsync(cancellationToken);
        if (collections.Count == 0)
        {
            throw new CodValidationException("No collected COD found for this rider at this branch.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var settlement = new RiderCashSettlement
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                RiderId = request.RiderId,
                BranchId = request.BranchId,
                DeclaredAmount = request.DeclaredAmount,
                ShipmentCount = collections.Count,
                SettledAt = DateTimeOffset.UtcNow,
                ConfirmedByUserId = actingUserId
            };
            _dbContext.Set<RiderCashSettlement>().Add(settlement);

            foreach (var collection in collections)
            {
                collection.RiderCashSettlementId = settlement.Id;
                collection.Status = CodCollectionStatus.RiderDeposited;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.rider_settlement_created", "RiderCashSettlement", settlement.Id.ToString()), cancellationToken);
            return ToRiderSettlementResponse(settlement);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static RiderCashSettlementResponse ToRiderSettlementResponse(RiderCashSettlement settlement) => new(
        settlement.Id, settlement.RiderId, settlement.BranchId, settlement.DeclaredAmount, settlement.ShipmentCount,
        settlement.SettledAt, settlement.ConfirmedByUserId, settlement.BranchCashSettlementId);
}
```

- [ ] **Step 3: Write the failing tests**

Create `tests/Cod.Tests/CodServiceRiderSettlementsTests.cs` — reuses the exact `NoopAuditLogger`/`FixedTenantProvider`/`AllBranchesAuthorizationChecker`/`AlwaysTrueBranchLookup`/`AlwaysTrueMerchantLookup`/`ActingUser` helpers from `tests/Cod.Tests/CodServiceCollectionsTests.cs` (copy verbatim, same established per-file-helper convention):

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Cod.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class AlwaysTrueBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public class CodServiceRiderSettlementsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
            modelBuilder.ApplyConfiguration(new RiderCashSettlementConfiguration());
        }
    }

    // Postgres-only feature (transactions are no-ops against the InMemory provider, which has no
    // real transaction log) — EF Core's InMemory provider silently accepts
    // BeginTransactionAsync/CommitAsync/RollbackAsync calls without erroring, so these tests run
    // unchanged against it; only the dedicated concurrency test in Task 14's Api.Tests suite proves
    // the transaction is real, against actual Postgres (mirroring Phase 5's rate-card race test
    // precedent).
    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        new AllBranchesAuthorizationChecker(), new AlwaysTrueMerchantLookup());

    private static async Task<CODCollection> SeedCollectedAsync(TestDbContext dbContext, Guid organizationId, Guid riderId, Guid branchId, decimal amount)
    {
        var collection = new CODCollection
        {
            Id = Guid.NewGuid(),
            ShipmentId = Guid.NewGuid(),
            OrganizationId = organizationId,
            MerchantId = Guid.NewGuid(),
            BranchId = branchId,
            ExpectedAmount = amount,
            ShipmentTotalCharge = 150m,
            CollectedAmount = amount,
            CollectedByRiderId = riderId,
            CollectedAt = DateTimeOffset.UtcNow,
            Status = CodCollectionStatus.CodCollected
        };
        dbContext.Set<CODCollection>().Add(collection);
        await dbContext.SaveChangesAsync();
        return collection;
    }

    [Fact]
    public async Task CreateRiderSettlementAsync_WithCollectedCod_BulkTransitionsToRiderDeposited()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var first = await SeedCollectedAsync(dbContext, organizationId, riderId, branchId, 2500m);
        var second = await SeedCollectedAsync(dbContext, organizationId, riderId, branchId, 1000m);
        var service = CreateService(dbContext, organizationId);

        var response = await service.CreateRiderSettlementAsync(new CreateRiderSettlementRequest(riderId, branchId, 3500m), ActingUser(Guid.NewGuid()));

        Assert.Equal(2, response.ShipmentCount);
        Assert.Equal(3500m, response.DeclaredAmount);
        var reloadedFirst = await dbContext.Set<CODCollection>().SingleAsync(c => c.Id == first.Id);
        var reloadedSecond = await dbContext.Set<CODCollection>().SingleAsync(c => c.Id == second.Id);
        Assert.Equal(CodCollectionStatus.RiderDeposited, reloadedFirst.Status);
        Assert.Equal(CodCollectionStatus.RiderDeposited, reloadedSecond.Status);
        Assert.Equal(response.Id, reloadedFirst.RiderCashSettlementId);
        Assert.Equal(response.Id, reloadedSecond.RiderCashSettlementId);
    }

    [Fact]
    public async Task CreateRiderSettlementAsync_NoCollectedCod_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.CreateRiderSettlementAsync(new CreateRiderSettlementRequest(Guid.NewGuid(), Guid.NewGuid(), 100m), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateRiderSettlementAsync_AlreadyDeposited_NotIncludedTwice()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await SeedCollectedAsync(dbContext, organizationId, riderId, branchId, 2500m);
        var service = CreateService(dbContext, organizationId);
        await service.CreateRiderSettlementAsync(new CreateRiderSettlementRequest(riderId, branchId, 2500m), ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.CreateRiderSettlementAsync(new CreateRiderSettlementRequest(riderId, branchId, 2500m), ActingUser(Guid.NewGuid())));
    }
}
```

- [ ] **Step 4: Run the new tests**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter "FullyQualifiedName~CodServiceRiderSettlementsTests"`
Expected: All 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Cod/Application/CodDtos.cs src/Modules/Cod/Application/CodService.Settlements.cs tests/Cod.Tests/CodServiceRiderSettlementsTests.cs
git commit -m "feat(cod): add rider cash settlement creation"
```

---

## Task 8: Branch cash settlements — `POST /cod/branch-settlements`

**Files:**
- Modify: `src/Modules/Cod/Application/CodDtos.cs`
- Modify: `src/Modules/Cod/Application/CodService.Settlements.cs`
- Test: `tests/Cod.Tests/CodServiceBranchSettlementsTests.cs`

**Interfaces:**
- Produces: `CodService.CreateBranchSettlementAsync(CreateBranchSettlementRequest, ClaimsPrincipal, CancellationToken) -> Task<BranchCashSettlementResponse>`.

- [ ] **Step 1: Add the DTOs**

Append to `src/Modules/Cod/Application/CodDtos.cs`:

```csharp
public record CreateBranchSettlementRequest(Guid BranchId, List<Guid> RiderCashSettlementIds, decimal ConfirmedAmount);

public record BranchCashSettlementResponse(
    Guid Id, Guid BranchId, decimal ConfirmedAmount, Guid ReconciledByUserId, DateTimeOffset ReconciledAt,
    List<Guid> RiderCashSettlementIds);
```

- [ ] **Step 2: Implement `CreateBranchSettlementAsync`**

Append to `src/Modules/Cod/Application/CodService.Settlements.cs` (inside the existing `partial class CodService` body, after `ToRiderSettlementResponse`):

```csharp
    // Transactional, same reasoning as CreateRiderSettlementAsync — parent-row insert
    // (BranchCashSettlement) plus a bulk FK-stamp on every named RiderCashSettlement plus a bulk
    // status update on every CODCollection those rider settlements cover.
    public async Task<BranchCashSettlementResponse> CreateBranchSettlementAsync(CreateBranchSettlementRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new CodValidationException("Acting user could not be resolved.");

        if (await _branchLookupService.GetZoneInfoAsync(request.BranchId, cancellationToken) is null)
        {
            throw new CodValidationException("BranchId does not refer to a branch in this organization.");
        }
        await CheckBranchAccessAsync(actingUser, "cod.reconcile", request.BranchId, cancellationToken);
        if (request.RiderCashSettlementIds.Count == 0)
        {
            throw new CodValidationException("At least one RiderCashSettlementId is required.");
        }

        var riderSettlements = await _dbContext.Set<RiderCashSettlement>()
            .Where(r => request.RiderCashSettlementIds.Contains(r.Id) && r.BranchId == request.BranchId && r.BranchCashSettlementId == null)
            .ToListAsync(cancellationToken);
        if (riderSettlements.Count != request.RiderCashSettlementIds.Count)
        {
            throw new CodValidationException("One or more RiderCashSettlementIds are invalid, belong to a different branch, or are already reconciled.");
        }

        var riderSettlementIds = riderSettlements.Select(r => r.Id).ToArray();
        var collections = await _dbContext.Set<CODCollection>()
            .Where(c => c.RiderCashSettlementId != null && riderSettlementIds.Contains(c.RiderCashSettlementId.Value) && c.Status == CodCollectionStatus.RiderDeposited)
            .ToListAsync(cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var settlement = new BranchCashSettlement
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                BranchId = request.BranchId,
                ConfirmedAmount = request.ConfirmedAmount,
                ReconciledByUserId = actingUserId,
                ReconciledAt = DateTimeOffset.UtcNow
            };
            _dbContext.Set<BranchCashSettlement>().Add(settlement);

            foreach (var riderSettlement in riderSettlements)
            {
                riderSettlement.BranchCashSettlementId = settlement.Id;
            }
            foreach (var collection in collections)
            {
                collection.Status = CodCollectionStatus.BranchReconciled;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.branch_settlement_created", "BranchCashSettlement", settlement.Id.ToString()), cancellationToken);
            return ToBranchSettlementResponse(settlement, riderSettlementIds.ToList());
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static BranchCashSettlementResponse ToBranchSettlementResponse(BranchCashSettlement settlement, List<Guid> riderCashSettlementIds) => new(
        settlement.Id, settlement.BranchId, settlement.ConfirmedAmount, settlement.ReconciledByUserId, settlement.ReconciledAt, riderCashSettlementIds);
```

(Note: this `Step 2` code block is appended *inside* the existing class body from Task 7 — it is not a standalone file. The class's closing `}` from Task 7 moves to after this new method.)

- [ ] **Step 3: Write the failing tests**

Create `tests/Cod.Tests/CodServiceBranchSettlementsTests.cs` — reuses the same fakes/helpers as `CodServiceRiderSettlementsTests.cs` (copy the `NoopAuditLogger`/`FixedTenantProvider`/`AllBranchesAuthorizationChecker`/`AlwaysTrueBranchLookup`/`AlwaysTrueMerchantLookup`/`CreateContext`/`ActingUser`/`CreateService` block verbatim), plus a seeding helper that creates a `RiderCashSettlement` with its covered `CODCollection`s already at `RiderDeposited`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Cod.Tests;

// (NoopAuditLogger, FixedTenantProvider, AllBranchesAuthorizationChecker, AlwaysTrueBranchLookup,
// AlwaysTrueMerchantLookup — identical to CodServiceRiderSettlementsTests.cs, copied verbatim.)
file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class AlwaysTrueBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public class CodServiceBranchSettlementsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
            modelBuilder.ApplyConfiguration(new RiderCashSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new BranchCashSettlementConfiguration());
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

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        new AllBranchesAuthorizationChecker(), new AlwaysTrueMerchantLookup());

    private static async Task<RiderCashSettlement> SeedRiderDepositedAsync(TestDbContext dbContext, Guid organizationId, Guid branchId, decimal amount)
    {
        var settlement = new RiderCashSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, RiderId = Guid.NewGuid(), BranchId = branchId,
            DeclaredAmount = amount, ShipmentCount = 1, SettledAt = DateTimeOffset.UtcNow, ConfirmedByUserId = Guid.NewGuid()
        };
        dbContext.Set<RiderCashSettlement>().Add(settlement);
        dbContext.Set<CODCollection>().Add(new CODCollection
        {
            Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = Guid.NewGuid(),
            BranchId = branchId, ExpectedAmount = amount, ShipmentTotalCharge = 150m, CollectedAmount = amount,
            Status = CodCollectionStatus.RiderDeposited, RiderCashSettlementId = settlement.Id
        });
        await dbContext.SaveChangesAsync();
        return settlement;
    }

    [Fact]
    public async Task CreateBranchSettlementAsync_WithRiderDeposits_BulkTransitionsToBranchReconciled()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var riderSettlement = await SeedRiderDepositedAsync(dbContext, organizationId, branchId, 2500m);
        var service = CreateService(dbContext, organizationId);

        var response = await service.CreateBranchSettlementAsync(
            new CreateBranchSettlementRequest(branchId, new List<Guid> { riderSettlement.Id }, 2500m), ActingUser(Guid.NewGuid()));

        Assert.Equal(new List<Guid> { riderSettlement.Id }, response.RiderCashSettlementIds);
        var reloadedRiderSettlement = await dbContext.Set<RiderCashSettlement>().SingleAsync(r => r.Id == riderSettlement.Id);
        Assert.Equal(response.Id, reloadedRiderSettlement.BranchCashSettlementId);
        var collection = await dbContext.Set<CODCollection>().SingleAsync(c => c.RiderCashSettlementId == riderSettlement.Id);
        Assert.Equal(CodCollectionStatus.BranchReconciled, collection.Status);
    }

    [Fact]
    public async Task CreateBranchSettlementAsync_UnknownRiderCashSettlementId_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.CreateBranchSettlementAsync(new CreateBranchSettlementRequest(Guid.NewGuid(), new List<Guid> { Guid.NewGuid() }, 100m), ActingUser(Guid.NewGuid())));
    }
}
```

- [ ] **Step 4: Run the new tests**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter "FullyQualifiedName~CodServiceBranchSettlementsTests"`
Expected: Both tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Cod/Application/CodDtos.cs src/Modules/Cod/Application/CodService.Settlements.cs tests/Cod.Tests/CodServiceBranchSettlementsTests.cs
git commit -m "feat(cod): add branch cash settlement creation"
```

---

## Task 9: Finance verification — `POST /cod/finance-verifications`

**Files:**
- Modify: `src/Modules/Cod/Application/CodDtos.cs`
- Modify: `src/Modules/Cod/Application/CodService.Settlements.cs`
- Test: `tests/Cod.Tests/CodServiceFinanceVerificationsTests.cs`

**Interfaces:**
- Produces: `CodService.CreateFinanceVerificationAsync(CreateFinanceVerificationRequest, ClaimsPrincipal, CancellationToken) -> Task<FinancialTransactionResponse>`.

- [ ] **Step 1: Add the DTOs**

Append to `src/Modules/Cod/Application/CodDtos.cs`:

```csharp
public record CreateFinanceVerificationRequest(Guid BranchCashSettlementId, decimal Amount);

public record FinancialTransactionResponse(
    Guid Id, FinancialTransactionType Type, decimal Amount, string ReferenceEntityType, Guid ReferenceEntityId,
    Guid VerifiedByUserId, DateTimeOffset VerifiedAt);
```

- [ ] **Step 2: Implement `CreateFinanceVerificationAsync`**

Append to `src/Modules/Cod/Application/CodService.Settlements.cs` (inside the class body, after `ToBranchSettlementResponse`). `cod.settle` is org-wide (`RBAC.md` §4: FinanceManager holds it with `BranchScope.AllBranches`) — no `CheckBranchAccessAsync` call here, matching how report endpoints skip branch scoping too:

```csharp
    // Transactional, same reasoning as the two settlement methods above — parent-row insert
    // (FinancialTransaction) plus a bulk status update on every CODCollection the verified
    // BranchCashSettlement covers.
    public async Task<FinancialTransactionResponse> CreateFinanceVerificationAsync(CreateFinanceVerificationRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new CodValidationException("Acting user could not be resolved.");

        var branchCashSettlementExists = await _dbContext.Set<BranchCashSettlement>().AnyAsync(b => b.Id == request.BranchCashSettlementId, cancellationToken);
        if (!branchCashSettlementExists)
        {
            throw new CodValidationException("BranchCashSettlementId was not found.");
        }
        var alreadyVerified = await _dbContext.Set<FinancialTransaction>()
            .AnyAsync(f => f.ReferenceEntityType == "BranchCashSettlement" && f.ReferenceEntityId == request.BranchCashSettlementId, cancellationToken);
        if (alreadyVerified)
        {
            throw new CodValidationException("This BranchCashSettlement has already been finance-verified.");
        }

        var riderSettlementIds = await _dbContext.Set<RiderCashSettlement>()
            .Where(r => r.BranchCashSettlementId == request.BranchCashSettlementId)
            .Select(r => r.Id)
            .ToArrayAsync(cancellationToken);
        var collections = await _dbContext.Set<CODCollection>()
            .Where(c => c.RiderCashSettlementId != null && riderSettlementIds.Contains(c.RiderCashSettlementId.Value) && c.Status == CodCollectionStatus.BranchReconciled)
            .ToListAsync(cancellationToken);
        if (collections.Count == 0)
        {
            throw new CodValidationException("No branch-reconciled COD collections found for this branch settlement.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var financialTransaction = new FinancialTransaction
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Type = FinancialTransactionType.CodVerification,
                Amount = request.Amount,
                ReferenceEntityType = "BranchCashSettlement",
                ReferenceEntityId = request.BranchCashSettlementId,
                VerifiedByUserId = actingUserId,
                VerifiedAt = DateTimeOffset.UtcNow
            };
            _dbContext.Set<FinancialTransaction>().Add(financialTransaction);

            foreach (var collection in collections)
            {
                collection.Status = CodCollectionStatus.FinanceVerified;
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The unique (ReferenceEntityType, ReferenceEntityId) index (Task 2) caught a
                // concurrent duplicate verification of the same BranchCashSettlement that raced past
                // the AnyAsync pre-check above — surfaced as a clean validation error, not a 500.
                // Proven by Task 14's Postgres concurrency test.
                throw new CodValidationException("This BranchCashSettlement has already been finance-verified.");
            }
            await transaction.CommitAsync(cancellationToken);

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.finance_verified", "FinancialTransaction", financialTransaction.Id.ToString()), cancellationToken);
            return ToFinancialTransactionResponse(financialTransaction);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static FinancialTransactionResponse ToFinancialTransactionResponse(FinancialTransaction transaction) => new(
        transaction.Id, transaction.Type, transaction.Amount, transaction.ReferenceEntityType, transaction.ReferenceEntityId,
        transaction.VerifiedByUserId, transaction.VerifiedAt);
```

- [ ] **Step 3: Write the failing tests**

Create `tests/Cod.Tests/CodServiceFinanceVerificationsTests.cs` — same fakes/helpers block as `CodServiceBranchSettlementsTests.cs` (copy verbatim), plus a seeding helper for a `BranchCashSettlement` whose covered `CODCollection` is at `BranchReconciled`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Cod.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class AlwaysTrueBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public class CodServiceFinanceVerificationsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
            modelBuilder.ApplyConfiguration(new RiderCashSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new BranchCashSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new FinancialTransactionConfiguration());
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

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        new AllBranchesAuthorizationChecker(), new AlwaysTrueMerchantLookup());

    private static async Task<BranchCashSettlement> SeedBranchReconciledAsync(TestDbContext dbContext, Guid organizationId, decimal amount)
    {
        var branchSettlement = new BranchCashSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, BranchId = Guid.NewGuid(),
            ConfirmedAmount = amount, ReconciledByUserId = Guid.NewGuid(), ReconciledAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<BranchCashSettlement>().Add(branchSettlement);
        var riderSettlement = new RiderCashSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, RiderId = Guid.NewGuid(), BranchId = branchSettlement.BranchId,
            DeclaredAmount = amount, ShipmentCount = 1, SettledAt = DateTimeOffset.UtcNow, ConfirmedByUserId = Guid.NewGuid(),
            BranchCashSettlementId = branchSettlement.Id
        };
        dbContext.Set<RiderCashSettlement>().Add(riderSettlement);
        dbContext.Set<CODCollection>().Add(new CODCollection
        {
            Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = Guid.NewGuid(),
            BranchId = branchSettlement.BranchId, ExpectedAmount = amount, ShipmentTotalCharge = 150m, CollectedAmount = amount,
            Status = CodCollectionStatus.BranchReconciled, RiderCashSettlementId = riderSettlement.Id
        });
        await dbContext.SaveChangesAsync();
        return branchSettlement;
    }

    [Fact]
    public async Task CreateFinanceVerificationAsync_WithBranchReconciled_BulkTransitionsToFinanceVerified()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchSettlement = await SeedBranchReconciledAsync(dbContext, organizationId, 2500m);
        var service = CreateService(dbContext, organizationId);

        var response = await service.CreateFinanceVerificationAsync(new CreateFinanceVerificationRequest(branchSettlement.Id, 2500m), ActingUser(Guid.NewGuid()));

        Assert.Equal(FinancialTransactionType.CodVerification, response.Type);
        var collection = await dbContext.Set<CODCollection>().SingleAsync();
        Assert.Equal(CodCollectionStatus.FinanceVerified, collection.Status);
    }

    [Fact]
    public async Task CreateFinanceVerificationAsync_SameBranchSettlementTwice_ThrowsOnSecondCall()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchSettlement = await SeedBranchReconciledAsync(dbContext, organizationId, 2500m);
        var service = CreateService(dbContext, organizationId);
        await service.CreateFinanceVerificationAsync(new CreateFinanceVerificationRequest(branchSettlement.Id, 2500m), ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.CreateFinanceVerificationAsync(new CreateFinanceVerificationRequest(branchSettlement.Id, 2500m), ActingUser(Guid.NewGuid())));
    }
}
```

- [ ] **Step 4: Run the new tests**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter "FullyQualifiedName~CodServiceFinanceVerificationsTests"`
Expected: Both tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Cod/Application/CodDtos.cs src/Modules/Cod/Application/CodService.Settlements.cs tests/Cod.Tests/CodServiceFinanceVerificationsTests.cs
git commit -m "feat(cod): add finance verification"
```

---

## Task 10: Merchant settlement creation and payout — `POST /cod/merchant-settlements`, `POST /cod/merchant-settlements/{id}/settle`

**Files:**
- Modify: `src/Modules/Cod/Application/CodDtos.cs`
- Create: `src/Modules/Cod/Application/CodService.MerchantSettlements.cs`
- Test: `tests/Cod.Tests/CodServiceMerchantSettlementsTests.cs`

**Interfaces:**
- Produces: `CodService.CreateMerchantSettlementAsync(CreateMerchantSettlementRequest, ClaimsPrincipal, CancellationToken) -> Task<MerchantSettlementResponse>`, `CodService.SettleMerchantSettlementAsync(Guid id, ClaimsPrincipal, CancellationToken) -> Task<MerchantSettlementResponse?>`.

- [ ] **Step 1: Add the DTOs**

Append to `src/Modules/Cod/Application/CodDtos.cs`:

```csharp
public record CreateMerchantSettlementRequest(Guid MerchantId, DateTimeOffset PeriodFrom, DateTimeOffset PeriodTo);

public record MerchantSettlementItemResponse(Guid Id, Guid ShipmentId, decimal CODAmount, decimal CourierFee, decimal OtherDeductions, decimal NetPayable);

public record MerchantSettlementResponse(
    Guid Id, Guid MerchantId, DateTimeOffset PeriodFrom, DateTimeOffset PeriodTo, decimal TotalPayable,
    MerchantSettlementStatus Status, DateTimeOffset? SettledAt, List<MerchantSettlementItemResponse> Items);
```

- [ ] **Step 2: Implement `CreateMerchantSettlementAsync` and `SettleMerchantSettlementAsync`**

`src/Modules/Cod/Application/CodService.MerchantSettlements.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Cod.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

public partial class CodService
{
    // Transactional (this plan's Global Constraints): parent-row insert (MerchantSettlement) plus
    // one MerchantSettlementItem insert and one CODCollection status update per covered shipment.
    // cod.settle is org-wide (RBAC.md §4) — no CheckBranchAccessAsync call, same as
    // CreateFinanceVerificationAsync.
    public async Task<MerchantSettlementResponse> CreateMerchantSettlementAsync(CreateMerchantSettlementRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new CodValidationException("Acting user could not be resolved.");

        if (!await _merchantLookupService.ExistsAsync(request.MerchantId, cancellationToken))
        {
            throw new CodValidationException("MerchantId does not refer to a merchant in this organization.");
        }
        if (request.PeriodTo < request.PeriodFrom)
        {
            throw new CodValidationException("PeriodTo must not be before PeriodFrom.");
        }

        var collections = await _dbContext.Set<CODCollection>()
            .Where(c => c.MerchantId == request.MerchantId
                        && c.Status == CodCollectionStatus.FinanceVerified
                        && c.MerchantSettlementItemId == null
                        && c.CollectedAt != null && c.CollectedAt >= request.PeriodFrom && c.CollectedAt <= request.PeriodTo)
            .ToListAsync(cancellationToken);
        if (collections.Count == 0)
        {
            throw new CodValidationException("No finance-verified COD collections found for this merchant in this period.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var settlement = new MerchantSettlement
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                MerchantId = request.MerchantId,
                PeriodFrom = request.PeriodFrom,
                PeriodTo = request.PeriodTo,
                Status = MerchantSettlementStatus.Created
            };
            _dbContext.Set<MerchantSettlement>().Add(settlement);

            var items = new List<MerchantSettlementItem>();
            foreach (var collection in collections)
            {
                // CODAmount is the amount actually collected (collection.CollectedAmount), not the
                // originally-quoted ExpectedAmount — the two are equal in the happy path but a
                // partial/short collection must settle against what was actually taken from the
                // customer, per COD-LIFECYCLE.md §3's shortage/overage visibility principle.
                var codAmount = collection.CollectedAmount!.Value;
                var courierFee = collection.ShipmentTotalCharge;
                var item = new MerchantSettlementItem
                {
                    Id = Guid.NewGuid(),
                    MerchantSettlementId = settlement.Id,
                    ShipmentId = collection.ShipmentId,
                    CODAmount = codAmount,
                    CourierFee = courierFee,
                    OtherDeductions = 0m,
                    NetPayable = codAmount - courierFee
                };
                items.Add(item);
                _dbContext.Set<MerchantSettlementItem>().Add(item);

                collection.MerchantSettlementItemId = item.Id;
                collection.Status = CodCollectionStatus.MerchantSettlementCreated;
            }
            settlement.TotalPayable = items.Sum(i => i.NetPayable);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.merchant_settlement_created", "MerchantSettlement", settlement.Id.ToString()), cancellationToken);
            return ToMerchantSettlementResponse(settlement, items);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // Transactional — parent-row status flip (MerchantSettlement) plus a bulk CODCollection status
    // update for every item it covers.
    public async Task<MerchantSettlementResponse?> SettleMerchantSettlementAsync(Guid merchantSettlementId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new CodValidationException("Acting user could not be resolved.");

        var settlement = await _dbContext.Set<MerchantSettlement>().FirstOrDefaultAsync(m => m.Id == merchantSettlementId, cancellationToken);
        if (settlement is null)
        {
            return null;
        }
        if (settlement.Status != MerchantSettlementStatus.Created)
        {
            throw new CodValidationException($"MerchantSettlement must be Created to settle, was {settlement.Status}.");
        }

        var items = await _dbContext.Set<MerchantSettlementItem>().Where(i => i.MerchantSettlementId == merchantSettlementId).ToListAsync(cancellationToken);
        var itemIds = items.Select(i => i.Id).ToArray();
        var collections = await _dbContext.Set<CODCollection>()
            .Where(c => c.MerchantSettlementItemId != null && itemIds.Contains(c.MerchantSettlementItemId.Value))
            .ToListAsync(cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            settlement.Status = MerchantSettlementStatus.Settled;
            settlement.SettledAt = DateTimeOffset.UtcNow;
            foreach (var collection in collections)
            {
                collection.Status = CodCollectionStatus.Settled;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.merchant_settlement_settled", "MerchantSettlement", settlement.Id.ToString()), cancellationToken);
            return ToMerchantSettlementResponse(settlement, items);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static MerchantSettlementResponse ToMerchantSettlementResponse(MerchantSettlement settlement, List<MerchantSettlementItem> items) => new(
        settlement.Id, settlement.MerchantId, settlement.PeriodFrom, settlement.PeriodTo, settlement.TotalPayable,
        settlement.Status, settlement.SettledAt,
        items.Select(i => new MerchantSettlementItemResponse(i.Id, i.ShipmentId, i.CODAmount, i.CourierFee, i.OtherDeductions, i.NetPayable)).ToList());
}
```

- [ ] **Step 3: Write the failing tests**

Create `tests/Cod.Tests/CodServiceMerchantSettlementsTests.cs` — same fakes/helpers block as prior task test files (copy verbatim), plus a seeding helper for `FinanceVerified` collections for a merchant:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Cod.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class AlwaysTrueBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public class CodServiceMerchantSettlementsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantSettlementItemConfiguration());
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

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        new AllBranchesAuthorizationChecker(), new AlwaysTrueMerchantLookup());

    private static async Task<CODCollection> SeedFinanceVerifiedAsync(TestDbContext dbContext, Guid organizationId, Guid merchantId, decimal codAmount, decimal totalCharge, DateTimeOffset collectedAt)
    {
        var collection = new CODCollection
        {
            Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = merchantId,
            BranchId = Guid.NewGuid(), ExpectedAmount = codAmount, ShipmentTotalCharge = totalCharge, CollectedAmount = codAmount,
            CollectedAt = collectedAt, Status = CodCollectionStatus.FinanceVerified
        };
        dbContext.Set<CODCollection>().Add(collection);
        await dbContext.SaveChangesAsync();
        return collection;
    }

    [Fact]
    public async Task CreateMerchantSettlementAsync_MatchesWorkedExampleFromCodLifecycleDoc()
    {
        // COD-LIFECYCLE.md §2: COD Amount 2500, Courier Delivery Fee 150, Merchant Payable 2350.
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var periodFrom = DateTimeOffset.UtcNow.AddDays(-7);
        var periodTo = DateTimeOffset.UtcNow.AddDays(1);
        await SeedFinanceVerifiedAsync(dbContext, organizationId, merchantId, 2500m, 150m, DateTimeOffset.UtcNow);
        var service = CreateService(dbContext, organizationId);

        var response = await service.CreateMerchantSettlementAsync(new CreateMerchantSettlementRequest(merchantId, periodFrom, periodTo), ActingUser(Guid.NewGuid()));

        var item = Assert.Single(response.Items);
        Assert.Equal(2500m, item.CODAmount);
        Assert.Equal(150m, item.CourierFee);
        Assert.Equal(0m, item.OtherDeductions);
        Assert.Equal(2350m, item.NetPayable);
        Assert.Equal(2350m, response.TotalPayable);
        Assert.Equal(MerchantSettlementStatus.Created, response.Status);
    }

    [Fact]
    public async Task CreateMerchantSettlementAsync_OutsidePeriod_ExcludesCollection()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        await SeedFinanceVerifiedAsync(dbContext, organizationId, merchantId, 2500m, 150m, DateTimeOffset.UtcNow.AddDays(-30));
        var service = CreateService(dbContext, organizationId);

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.CreateMerchantSettlementAsync(new CreateMerchantSettlementRequest(merchantId, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task SettleMerchantSettlementAsync_FromCreated_TransitionsCollectionsToSettled()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        await SeedFinanceVerifiedAsync(dbContext, organizationId, merchantId, 2500m, 150m, DateTimeOffset.UtcNow);
        var service = CreateService(dbContext, organizationId);
        var created = await service.CreateMerchantSettlementAsync(
            new CreateMerchantSettlementRequest(merchantId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)), ActingUser(Guid.NewGuid()));

        var settled = await service.SettleMerchantSettlementAsync(created.Id, ActingUser(Guid.NewGuid()));

        Assert.Equal(MerchantSettlementStatus.Settled, settled!.Status);
        Assert.NotNull(settled.SettledAt);
        var collection = await dbContext.Set<CODCollection>().SingleAsync();
        Assert.Equal(CodCollectionStatus.Settled, collection.Status);
    }

    [Fact]
    public async Task SettleMerchantSettlementAsync_AlreadySettled_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        await SeedFinanceVerifiedAsync(dbContext, organizationId, merchantId, 2500m, 150m, DateTimeOffset.UtcNow);
        var service = CreateService(dbContext, organizationId);
        var created = await service.CreateMerchantSettlementAsync(
            new CreateMerchantSettlementRequest(merchantId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)), ActingUser(Guid.NewGuid()));
        await service.SettleMerchantSettlementAsync(created.Id, ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<CodValidationException>(() => service.SettleMerchantSettlementAsync(created.Id, ActingUser(Guid.NewGuid())));
    }
}
```

- [ ] **Step 4: Run the new tests**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter "FullyQualifiedName~CodServiceMerchantSettlementsTests"`
Expected: All 4 tests pass, including the worked-example test matching `COD-LIFECYCLE.md` §2 exactly.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Cod/Application/CodDtos.cs src/Modules/Cod/Application/CodService.MerchantSettlements.cs tests/Cod.Tests/CodServiceMerchantSettlementsTests.cs
git commit -m "feat(cod): add merchant settlement creation and payout"
```

---

## Task 11: Adjustments — `POST /cod/adjustments`, `POST /cod/adjustments/{id}/approve`

**Files:**
- Modify: `src/Modules/Cod/Application/CodDtos.cs`
- Create: `src/Modules/Cod/Application/CodService.Adjustments.cs`
- Test: `tests/Cod.Tests/CodServiceAdjustmentsTests.cs`

**Interfaces:**
- Produces: `CodService.RequestAdjustmentAsync(RequestAdjustmentRequest, ClaimsPrincipal, CancellationToken) -> Task<AdjustmentResponse>`, `CodService.ApproveAdjustmentAsync(Guid id, ClaimsPrincipal, CancellationToken) -> Task<AdjustmentResponse?>`.

- [ ] **Step 1: Add the DTOs**

Append to `src/Modules/Cod/Application/CodDtos.cs`:

```csharp
public record RequestAdjustmentRequest(string EntityType, Guid EntityId, decimal AfterValue, string Reason);

public record AdjustmentResponse(
    Guid Id, string EntityType, Guid EntityId, decimal BeforeValue, decimal AfterValue, string Reason,
    Guid RequestedByUserId, DateTimeOffset RequestedAt, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt, bool Applied);
```

- [ ] **Step 2: Implement `RequestAdjustmentAsync` and `ApproveAdjustmentAsync`**

`src/Modules/Cod/Application/CodService.Adjustments.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Cod.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

public partial class CodService
{
    // v1 supports exactly one EntityType — the only concrete adjustment case with a real business
    // need right now (this plan's Global Constraints / DOMAIN-MODEL.md's MerchantSettlementItem doc
    // comment). ApproveAdjustmentAsync's switch below is the single place this whitelist is
    // enforced on the write side too.
    private const string AdjustableEntityType = "MerchantSettlementItem";

    // Never touches the target row — only inserts the Adjustment record itself, unapplied. This
    // plan's resolved design decision: Adjustment always requires a separate approval step
    // (bounded brainstorm), so a single-row insert needs no transaction.
    public async Task<AdjustmentResponse> RequestAdjustmentAsync(RequestAdjustmentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new CodValidationException("Acting user could not be resolved.");

        if (request.EntityType != AdjustableEntityType)
        {
            throw new CodValidationException($"Adjustments are only supported for EntityType '{AdjustableEntityType}'.");
        }
        var item = await _dbContext.Set<MerchantSettlementItem>().FirstOrDefaultAsync(i => i.Id == request.EntityId, cancellationToken)
            ?? throw new CodValidationException("EntityId does not refer to a MerchantSettlementItem in this organization.");

        var adjustment = new Adjustment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            BeforeValue = item.OtherDeductions,
            AfterValue = request.AfterValue,
            Reason = request.Reason,
            RequestedByUserId = actingUserId,
            RequestedAt = DateTimeOffset.UtcNow,
            Applied = false
        };
        _dbContext.Set<Adjustment>().Add(adjustment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.adjustment_requested", "Adjustment", adjustment.Id.ToString()), cancellationToken);
        return ToAdjustmentResponse(adjustment);
    }

    // Transactional: rewrites MerchantSettlementItem.OtherDeductions/NetPayable, recomputes the
    // parent MerchantSettlement.TotalPayable, and flags the Adjustment Applied — three rows that
    // must move together or not at all, the one place outside the booking-time snapshot where a
    // financial figure is ever rewritten in place (CLAUDE.md §23), which is exactly why it demands
    // a transaction even though it is not part of the main status chain.
    public async Task<AdjustmentResponse?> ApproveAdjustmentAsync(Guid adjustmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new CodValidationException("Acting user could not be resolved.");

        var adjustment = await _dbContext.Set<Adjustment>().FirstOrDefaultAsync(a => a.Id == adjustmentId, cancellationToken);
        if (adjustment is null)
        {
            return null;
        }
        if (adjustment.Applied)
        {
            throw new CodValidationException("This adjustment has already been approved and applied.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            switch (adjustment.EntityType)
            {
                case AdjustableEntityType:
                    var item = await _dbContext.Set<MerchantSettlementItem>().FirstAsync(i => i.Id == adjustment.EntityId, cancellationToken);
                    item.OtherDeductions = adjustment.AfterValue;
                    item.NetPayable = item.CODAmount - item.CourierFee - item.OtherDeductions;

                    var settlement = await _dbContext.Set<MerchantSettlement>().FirstAsync(m => m.Id == item.MerchantSettlementId, cancellationToken);
                    settlement.TotalPayable = await _dbContext.Set<MerchantSettlementItem>()
                        .Where(i => i.MerchantSettlementId == settlement.Id)
                        .SumAsync(i => i.Id == item.Id ? item.NetPayable : i.NetPayable, cancellationToken);
                    break;

                default:
                    throw new CodValidationException($"Unsupported adjustment EntityType '{adjustment.EntityType}'.");
            }

            adjustment.ApprovedByUserId = actingUserId;
            adjustment.ApprovedAt = DateTimeOffset.UtcNow;
            adjustment.Applied = true;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "cod.adjustment_approved", "Adjustment", adjustment.Id.ToString()), cancellationToken);
            return ToAdjustmentResponse(adjustment);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static AdjustmentResponse ToAdjustmentResponse(Adjustment adjustment) => new(
        adjustment.Id, adjustment.EntityType, adjustment.EntityId, adjustment.BeforeValue, adjustment.AfterValue, adjustment.Reason,
        adjustment.RequestedByUserId, adjustment.RequestedAt, adjustment.ApprovedByUserId, adjustment.ApprovedAt, adjustment.Applied);
}
```

(The `SumAsync` line above deliberately substitutes `item.NetPayable`, the in-memory just-computed value, for the one row already tracked and about to be saved — a fresh `SumAsync` over the table would read the pre-update value for that row within the same transaction/`SaveChanges` batch since the update hasn't been flushed yet. `EF.Property`/raw SQL is unnecessary here since both `item` and the query result are the same `DbContext`'s tracked entities and the substitution is exact by `Id`.)

- [ ] **Step 3: Write the failing tests**

Create `tests/Cod.Tests/CodServiceAdjustmentsTests.cs` — same fakes/helpers block as prior task test files (copy verbatim), plus a seeding helper for a `MerchantSettlement` + `MerchantSettlementItem` pair:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Cod.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class AlwaysTrueBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public class CodServiceAdjustmentsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantSettlementItemConfiguration());
            modelBuilder.ApplyConfiguration(new AdjustmentConfiguration());
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

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        new AllBranchesAuthorizationChecker(), new AlwaysTrueMerchantLookup());

    private static async Task<(MerchantSettlement Settlement, MerchantSettlementItem Item)> SeedSettlementItemAsync(TestDbContext dbContext, Guid organizationId)
    {
        var settlement = new MerchantSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = Guid.NewGuid(),
            PeriodFrom = DateTimeOffset.UtcNow.AddDays(-7), PeriodTo = DateTimeOffset.UtcNow,
            TotalPayable = 2350m, Status = MerchantSettlementStatus.Created
        };
        dbContext.Set<MerchantSettlement>().Add(settlement);
        var item = new MerchantSettlementItem
        {
            Id = Guid.NewGuid(), MerchantSettlementId = settlement.Id, ShipmentId = Guid.NewGuid(),
            CODAmount = 2500m, CourierFee = 150m, OtherDeductions = 0m, NetPayable = 2350m
        };
        dbContext.Set<MerchantSettlementItem>().Add(item);
        await dbContext.SaveChangesAsync();
        return (settlement, item);
    }

    [Fact]
    public async Task RequestAdjustmentAsync_CapturesCurrentOtherDeductionsAsBeforeValue()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (_, item) = await SeedSettlementItemAsync(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId);

        var response = await service.RequestAdjustmentAsync(
            new RequestAdjustmentRequest("MerchantSettlementItem", item.Id, 100m, "Damaged parcel deduction"), ActingUser(Guid.NewGuid()));

        Assert.Equal(0m, response.BeforeValue);
        Assert.Equal(100m, response.AfterValue);
        Assert.False(response.Applied);
        var unchangedItem = await dbContext.Set<MerchantSettlementItem>().SingleAsync(i => i.Id == item.Id);
        Assert.Equal(0m, unchangedItem.OtherDeductions);
    }

    [Fact]
    public async Task RequestAdjustmentAsync_UnsupportedEntityType_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<CodValidationException>(() =>
            service.RequestAdjustmentAsync(new RequestAdjustmentRequest("Shipment", Guid.NewGuid(), 50m, "n/a"), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ApproveAdjustmentAsync_RewritesOtherDeductionsAndRecomputesNetPayableAndTotalPayable()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (settlement, item) = await SeedSettlementItemAsync(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId);
        var requested = await service.RequestAdjustmentAsync(
            new RequestAdjustmentRequest("MerchantSettlementItem", item.Id, 100m, "Damaged parcel deduction"), ActingUser(Guid.NewGuid()));

        var approved = await service.ApproveAdjustmentAsync(requested.Id, ActingUser(Guid.NewGuid()));

        Assert.True(approved!.Applied);
        Assert.NotNull(approved.ApprovedAt);
        var updatedItem = await dbContext.Set<MerchantSettlementItem>().SingleAsync(i => i.Id == item.Id);
        Assert.Equal(100m, updatedItem.OtherDeductions);
        Assert.Equal(2250m, updatedItem.NetPayable); // 2500 - 150 - 100
        var updatedSettlement = await dbContext.Set<MerchantSettlement>().SingleAsync(m => m.Id == settlement.Id);
        Assert.Equal(2250m, updatedSettlement.TotalPayable);
    }

    [Fact]
    public async Task ApproveAdjustmentAsync_AlreadyApplied_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (_, item) = await SeedSettlementItemAsync(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId);
        var requested = await service.RequestAdjustmentAsync(
            new RequestAdjustmentRequest("MerchantSettlementItem", item.Id, 100m, "Damaged parcel deduction"), ActingUser(Guid.NewGuid()));
        await service.ApproveAdjustmentAsync(requested.Id, ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<CodValidationException>(() => service.ApproveAdjustmentAsync(requested.Id, ActingUser(Guid.NewGuid())));
    }
}
```

- [ ] **Step 4: Run the new tests**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter "FullyQualifiedName~CodServiceAdjustmentsTests"`
Expected: All 4 tests pass.

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Cod/Application/CodDtos.cs src/Modules/Cod/Application/CodService.Adjustments.cs tests/Cod.Tests/CodServiceAdjustmentsTests.cs
git commit -m "feat(cod): add adjustment request/approve flow"
```

---

## Task 12: Reads — collections list/get, reconciliation summary, merchant settlement list/get

**Files:**
- Modify: `src/Modules/Cod/Application/CodDtos.cs`
- Create: `src/Modules/Cod/Application/CodService.Reads.cs`
- Test: `tests/Cod.Tests/CodServiceReadsTests.cs`

**Interfaces:**
- Produces: `GetCollectionByIdAsync`, `ListCollectionsAsync`, `GetReconciliationSummaryAsync`, `ListMerchantSettlementsAsync`, `GetMerchantSettlementByIdAsync` — Task 13's Presentation layer routes every remaining `GET /cod/*` endpoint to these.

- [ ] **Step 1: Add the DTO**

Append to `src/Modules/Cod/Application/CodDtos.cs`:

```csharp
public record CodReconciliationSummaryResponse(
    decimal ExpectedCod, decimal CollectedCod, decimal RiderDepositedCod, decimal BranchReceivedCod,
    decimal MerchantPayableCod, decimal TotalCourierFees);
```

- [ ] **Step 2: Implement the five read methods**

`src/Modules/Cod/Application/CodService.Reads.cs`:

```csharp
using System.Security.Claims;
using Courier.Cod.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

public partial class CodService
{
    public async Task<CodCollectionResponse?> GetCollectionByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var collection = await _dbContext.Set<CODCollection>().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return collection is null ? null : ToResponse(collection);
    }

    // BranchId lives directly on CODCollection (this plan's eager-creation snapshot design) — a
    // single SQL WHERE clause, unlike Ndr.Application.NdrService.ListAsync's per-row
    // IShipmentTransitionService.GetScanInfoAsync lookup loop (Phase 9), which needed one because
    // NDR has no branch column of its own.
    public async Task<List<CodCollectionResponse>> ListCollectionsAsync(CodCollectionStatus? status, Guid? branchId, Guid? merchantId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<CODCollection>().AsQueryable();
        if (status is not null)
        {
            query = query.Where(c => c.Status == status);
        }

        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            // Merchant-JWT callers are hard-scoped to their own id regardless of what the query
            // string says — never trust a client-supplied MerchantId on a merchant token
            // (CLAUDE.md §33), same precedent as Shipments.Application.ShipmentService.CreateShipmentAsync.
            query = query.Where(c => c.MerchantId == callerMerchantId);
        }
        else
        {
            if (merchantId is Guid requestedMerchantId)
            {
                query = query.Where(c => c.MerchantId == requestedMerchantId);
            }
            if (branchId is Guid requestedBranchId)
            {
                query = query.Where(c => c.BranchId == requestedBranchId);
            }

            var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "cod.read", cancellationToken);
            if (accessibleBranchIds is not null)
            {
                var accessibleArray = accessibleBranchIds.ToArray();
                query = query.Where(c => accessibleArray.Contains(c.BranchId));
            }
        }

        var collections = await query.ToListAsync(cancellationToken);
        return collections.Select(ToResponse).ToList();
    }

    // CLAUDE.md §23's reconciliation figures, computed over whatever scope ListCollectionsAsync
    // above would also apply (merchant-JWT self-scope, or staff branch/merchant filters +
    // cod.read branch access) plus an optional CollectedAt date-range filter. A CODCollection still
    // at CodExpected has a null CollectedAt and is never excluded by the date filter — it hasn't
    // happened yet, so it can't be said to fall outside any date range.
    public async Task<CodReconciliationSummaryResponse> GetReconciliationSummaryAsync(Guid? branchId, Guid? merchantId, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<CODCollection>().AsQueryable();
        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            query = query.Where(c => c.MerchantId == callerMerchantId);
        }
        else
        {
            if (merchantId is Guid requestedMerchantId)
            {
                query = query.Where(c => c.MerchantId == requestedMerchantId);
            }
            if (branchId is Guid requestedBranchId)
            {
                query = query.Where(c => c.BranchId == requestedBranchId);
            }

            var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "cod.read", cancellationToken);
            if (accessibleBranchIds is not null)
            {
                var accessibleArray = accessibleBranchIds.ToArray();
                query = query.Where(c => accessibleArray.Contains(c.BranchId));
            }
        }
        if (from is DateTimeOffset fromDate)
        {
            query = query.Where(c => c.CollectedAt == null || c.CollectedAt >= fromDate);
        }
        if (to is DateTimeOffset toDate)
        {
            query = query.Where(c => c.CollectedAt == null || c.CollectedAt <= toDate);
        }

        var collections = await query.ToListAsync(cancellationToken);
        var reachedStatuses = new[] { CodCollectionStatus.RiderDeposited, CodCollectionStatus.BranchReconciled, CodCollectionStatus.FinanceVerified, CodCollectionStatus.MerchantSettlementCreated, CodCollectionStatus.Settled };
        var branchReceivedStatuses = new[] { CodCollectionStatus.BranchReconciled, CodCollectionStatus.FinanceVerified, CodCollectionStatus.MerchantSettlementCreated, CodCollectionStatus.Settled };

        var expectedCod = collections.Where(c => c.Status == CodCollectionStatus.CodExpected).Sum(c => c.ExpectedAmount);
        var collectedCod = collections.Where(c => c.CollectedAmount is not null).Sum(c => c.CollectedAmount!.Value);
        var riderDepositedCod = collections.Where(c => reachedStatuses.Contains(c.Status)).Sum(c => c.CollectedAmount!.Value);
        var branchReceivedCod = collections.Where(c => branchReceivedStatuses.Contains(c.Status)).Sum(c => c.CollectedAmount!.Value);
        var totalCourierFees = collections.Where(c => c.CollectedAmount is not null).Sum(c => c.ShipmentTotalCharge);

        var settlementItemIds = collections.Where(c => c.MerchantSettlementItemId is not null).Select(c => c.MerchantSettlementItemId!.Value).ToArray();
        var merchantPayableCod = settlementItemIds.Length == 0
            ? 0m
            : await _dbContext.Set<MerchantSettlementItem>().Where(i => settlementItemIds.Contains(i.Id)).SumAsync(i => i.NetPayable, cancellationToken);

        return new CodReconciliationSummaryResponse(expectedCod, collectedCod, riderDepositedCod, branchReceivedCod, merchantPayableCod, totalCourierFees);
    }

    public async Task<List<MerchantSettlementResponse>> ListMerchantSettlementsAsync(Guid? merchantId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<MerchantSettlement>().AsQueryable();
        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            query = query.Where(m => m.MerchantId == callerMerchantId);
        }
        else if (merchantId is Guid requestedMerchantId)
        {
            query = query.Where(m => m.MerchantId == requestedMerchantId);
        }

        var settlements = await query.ToListAsync(cancellationToken);
        var responses = new List<MerchantSettlementResponse>();
        foreach (var settlement in settlements)
        {
            var items = await _dbContext.Set<MerchantSettlementItem>().Where(i => i.MerchantSettlementId == settlement.Id).ToListAsync(cancellationToken);
            responses.Add(ToMerchantSettlementResponse(settlement, items));
        }
        return responses;
    }

    public async Task<MerchantSettlementResponse?> GetMerchantSettlementByIdAsync(Guid id, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var settlement = await _dbContext.Set<MerchantSettlement>().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (settlement is null)
        {
            return null;
        }
        if (actingUser.GetMerchantId() is Guid callerMerchantId && settlement.MerchantId != callerMerchantId)
        {
            return null;
        }

        var items = await _dbContext.Set<MerchantSettlementItem>().Where(i => i.MerchantSettlementId == id).ToListAsync(cancellationToken);
        return ToMerchantSettlementResponse(settlement, items);
    }
}
```

- [ ] **Step 3: Write the failing tests**

Create `tests/Cod.Tests/CodServiceReadsTests.cs` — same fakes/helpers block as prior task test files, except `AllBranchesAuthorizationChecker` is replaced per-test where branch scoping matters:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Cod.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class RestrictedBranchAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid> _accessibleBranchIds;
    public RestrictedBranchAuthorizationChecker(params Guid[] accessibleBranchIds) => _accessibleBranchIds = accessibleBranchIds.ToHashSet();
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(_accessibleBranchIds);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessibleBranchIds.Contains(branchId));
}

file class AlwaysTrueBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public class CodServiceReadsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantSettlementItemConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal StaffActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static ClaimsPrincipal MerchantActingUser(Guid merchantId) => new(new ClaimsIdentity(new[] { new Claim("merchant_id", merchantId.ToString()) }, "test"));

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker branchAuthorizationChecker) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        branchAuthorizationChecker, new AlwaysTrueMerchantLookup());

    private static async Task<CODCollection> SeedCollectionAsync(TestDbContext dbContext, Guid organizationId, Guid merchantId, Guid branchId, CodCollectionStatus status, decimal? collectedAmount)
    {
        var collection = new CODCollection
        {
            Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = merchantId,
            BranchId = branchId, ExpectedAmount = 2500m, ShipmentTotalCharge = 150m, CollectedAmount = collectedAmount,
            CollectedAt = collectedAmount is null ? null : DateTimeOffset.UtcNow, Status = status
        };
        dbContext.Set<CODCollection>().Add(collection);
        await dbContext.SaveChangesAsync();
        return collection;
    }

    [Fact]
    public async Task ListCollectionsAsync_StaffWithRestrictedBranchAccess_OnlySeesAccessibleBranch()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var accessibleBranchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        var accessible = await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), accessibleBranchId, CodCollectionStatus.CodExpected, null);
        await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), otherBranchId, CodCollectionStatus.CodExpected, null);
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(accessibleBranchId));

        var results = await service.ListCollectionsAsync(null, null, null, StaffActingUser(Guid.NewGuid()));

        var result = Assert.Single(results);
        Assert.Equal(accessible.Id, result.Id);
    }

    [Fact]
    public async Task ListCollectionsAsync_MerchantCaller_OnlySeesOwnMerchantRegardlessOfBranchAccess()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var own = await SeedCollectionAsync(dbContext, organizationId, merchantId, Guid.NewGuid(), CodCollectionStatus.CodExpected, null);
        await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid(), CodCollectionStatus.CodExpected, null);
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var results = await service.ListCollectionsAsync(null, null, null, MerchantActingUser(merchantId));

        var result = Assert.Single(results);
        Assert.Equal(own.Id, result.Id);
    }

    [Fact]
    public async Task GetReconciliationSummaryAsync_ComputesEachFigureFromTheRightStatusSet()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), branchId, CodCollectionStatus.CodExpected, null); // ExpectedAmount 2500, not yet collected
        await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), branchId, CodCollectionStatus.CodCollected, 1000m);
        await SeedCollectionAsync(dbContext, organizationId, Guid.NewGuid(), branchId, CodCollectionStatus.BranchReconciled, 500m);
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var summary = await service.GetReconciliationSummaryAsync(null, null, null, null, StaffActingUser(Guid.NewGuid()));

        Assert.Equal(2500m, summary.ExpectedCod); // only the CodExpected row
        Assert.Equal(1500m, summary.CollectedCod); // 1000 + 500, both have a CollectedAmount
        Assert.Equal(500m, summary.RiderDepositedCod); // only BranchReconciled has reached RiderDeposited+
        Assert.Equal(500m, summary.BranchReceivedCod); // only BranchReconciled has reached BranchReconciled+
        Assert.Equal(300m, summary.TotalCourierFees); // 150 + 150, both collected rows
    }
}
```

- [ ] **Step 4: Run the new tests**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter "FullyQualifiedName~CodServiceReadsTests"`
Expected: All 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Cod/Application/CodDtos.cs src/Modules/Cod/Application/CodService.Reads.cs tests/Cod.Tests/CodServiceReadsTests.cs
git commit -m "feat(cod): add collections/reconciliation/merchant-settlement reads"
```

---

## Task 13: Cod Presentation — `CodEndpoints.cs` and `Program.cs` wiring

**Files:**
- Create: `src/Modules/Cod/Presentation/CodEndpoints.cs`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Produces: every `/api/v1/cod/*` route this plan's design specifies.

- [ ] **Step 1: `CodEndpoints.cs`**

`src/Modules/Cod/Presentation/CodEndpoints.cs` — mirrors `NdrEndpoints.cs`'s shape:

```csharp
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Cod.Presentation;

public static class CodEndpoints
{
    public static void MapCodEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/cod")
        .WithTags("COD Management")
        .RequireAuthorization();

        group.MapGet("/collections", async (CodCollectionStatus? status, Guid? branchId, Guid? merchantId, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListCollectionsAsync(status, branchId, merchantId, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");

        group.MapGet("/collections/{id:guid}", async (Guid id, CodService service, HttpContext httpContext) =>
        {
            var result = await service.GetCollectionByIdAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("cod.read");

        group.MapGet("/reconciliation", async (Guid? branchId, Guid? merchantId, DateTimeOffset? from, DateTimeOffset? to, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.GetReconciliationSummaryAsync(branchId, merchantId, from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");

        group.MapPost("/rider-settlements", async (CreateRiderSettlementRequest request, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.CreateRiderSettlementAsync(request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.reconcile");

        group.MapPost("/branch-settlements", async (CreateBranchSettlementRequest request, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.CreateBranchSettlementAsync(request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.reconcile");

        group.MapPost("/finance-verifications", async (CreateFinanceVerificationRequest request, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.CreateFinanceVerificationAsync(request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.settle");

        group.MapPost("/merchant-settlements", async (CreateMerchantSettlementRequest request, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.CreateMerchantSettlementAsync(request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.settle");

        group.MapGet("/merchant-settlements", async (Guid? merchantId, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListMerchantSettlementsAsync(merchantId, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");

        group.MapGet("/merchant-settlements/{id:guid}", async (Guid id, CodService service, HttpContext httpContext) =>
        {
            var result = await service.GetMerchantSettlementByIdAsync(id, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("cod.read");

        group.MapPost("/merchant-settlements/{id:guid}/settle", async (Guid id, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.SettleMerchantSettlementAsync(id, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.settle");

        group.MapPost("/adjustments", async (RequestAdjustmentRequest request, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.RequestAdjustmentAsync(request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.adjust");

        group.MapPost("/adjustments/{id:guid}/approve", async (Guid id, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.ApproveAdjustmentAsync(id, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.adjust");
    }

    private static async Task<IResult> HandleAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            var result = await operation();
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (CodValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "COD_VALIDATION_FAILED", message = ex.Message } });
        }
    }
}
```

- [ ] **Step 2: Wire into `Program.cs`**

In `src/Api/Program.cs`, add `using Courier.Cod.Presentation;` to the usings, and after the existing `app.MapNdrEndpoints();` line:

```csharp
app.MapCodEndpoints();
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Cod/Presentation/CodEndpoints.cs src/Api/Program.cs
git commit -m "feat(cod): expose COD/settlement endpoints"
```

---

## Task 14: API tests — full worked-example end-to-end, plus a Postgres concurrency test for finance verification

**Files:**
- Create: `tests/Api.Tests/CodEndpointsTests.cs`

**Interfaces:**
- Consumes: every `/api/v1/cod/*` route (Task 13) plus `/api/v1/riders/{id}/delivery-attempts/complete` (Task 6) over real HTTP against a real Postgres database (`WebApplicationFactory<Program>`, same as `tests/Api.Tests/NdrEndpointsTests.cs`).

- [ ] **Step 1: Write the end-to-end worked-example test and the concurrency test**

Create `tests/Api.Tests/CodEndpointsTests.cs` — reuses the exact `RegisterOrgAsync`/`ValidBranchRequest`/`KathmanduWard1Areas`/`SeedBranchesAsync`/`CreateMerchantAsync`/`CreateRiderAsync` helpers already defined in `tests/Api.Tests/NdrEndpointsTests.cs` (copy verbatim, same established per-file-helper convention this codebase's `Api.Tests` files already follow):

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Manifests.Application;
using Courier.Manifests.Domain;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class CodEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public CodEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Cod Org {suffix}", $"cod-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private static CreateBranchRequest ValidBranchRequest(string code) => new(
        code, $"Branch {code}", BranchType.Hub, KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null, null);

    private static List<Guid> KathmanduWard1Areas() => new()
    {
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
    };

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
        await client.PutAsJsonAsync($"/api/v1/branches/{fromBranch!.Id}/delivery-zone", new AssignDeliveryZoneRequest(originZone!.Id));
        await client.PutAsJsonAsync($"/api/v1/branches/{toBranch!.Id}/delivery-zone", new AssignDeliveryZoneRequest(destZone!.Id));

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

    private async Task<Guid> CreateMerchantAsync(HttpClient staffClient)
    {
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Shipper Pvt. Ltd.", "Shipper", "01-4444444", "shipper@example.com",
            $"shipper-owner-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return merchant!.Id;
    }

    private async Task<Guid> CreateRiderAsync(HttpClient client, Guid branchId, RiderRoleType roleType)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, $"LIC-{suffix[..10]}", branchId, roleType));
        var rider = await response.Content.ReadFromJsonAsync<RiderResponse>();
        return rider!.Id;
    }

    private static CreateShipmentRequest CodShipmentRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, decimal codAmount) => new(
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, codAmount, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 1, 500m) });

    // Books a COD shipment and walks it all the way to a completed COD delivery — pickup (Phase 8),
    // sort/dispatch/receive via Manifests (Phase 7), assign+complete delivery with
    // CollectedCodAmount (Phase 10, Task 6). Mirrors NdrEndpointsTests.cs's BookAndFailDeliveryAsync
    // shape exactly, just ending in a successful COD delivery instead of a failed attempt.
    private async Task<ShipmentResponse> BookAndCompleteCodDeliveryAsync(
        HttpClient client, Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId,
        Guid pickupRiderId, Guid deliveryRiderId, decimal codAmount)
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", CodShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId, codAmount));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        await client.PostAsJsonAsync($"/api/v1/riders/{pickupRiderId}/scan/pickup", new RiderScanRequest(shipment.Awb));
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { });

        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));
        var completeResponse = await client.PostAsJsonAsync($"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete",
            new CompleteDeliveryRequest(shipment.Id, "Recipient One", true, null, null, null, null, codAmount));
        completeResponse.EnsureSuccessStatusCode();

        return (await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>())!;
    }

    private async Task<CodCollectionResponse> GetCollectionForShipmentAsync(HttpClient client, Guid shipmentId)
    {
        var collections = await (await client.GetAsync("/api/v1/cod/collections")).Content.ReadFromJsonAsync<List<CodCollectionResponse>>();
        return collections!.Single(c => c.ShipmentId == shipmentId);
    }

    [Fact]
    public async Task FullCodChain_BookingToMerchantPayout_MatchesWorkedExampleAndMovesThroughEveryStatus()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-success");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var shipment = await BookAndCompleteCodDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        Assert.Equal(ShipmentStatus.Delivered, shipment.CurrentStatus);

        var afterCollection = await GetCollectionForShipmentAsync(client, shipment.Id);
        Assert.Equal(CodCollectionStatus.CodCollected, afterCollection.Status);
        Assert.Equal(2500m, afterCollection.CollectedAmount);
        Assert.Equal(destinationBranchId, afterCollection.BranchId);

        var riderSettlement = await (await client.PostAsJsonAsync("/api/v1/cod/rider-settlements",
            new CreateRiderSettlementRequest(deliveryRiderId, destinationBranchId, 2500m))).Content.ReadFromJsonAsync<RiderCashSettlementResponse>();
        var afterRiderDeposit = await GetCollectionForShipmentAsync(client, shipment.Id);
        Assert.Equal(CodCollectionStatus.RiderDeposited, afterRiderDeposit.Status);

        var branchSettlement = await (await client.PostAsJsonAsync("/api/v1/cod/branch-settlements",
            new CreateBranchSettlementRequest(destinationBranchId, new List<Guid> { riderSettlement!.Id }, 2500m))).Content.ReadFromJsonAsync<BranchCashSettlementResponse>();
        var afterBranchReconcile = await GetCollectionForShipmentAsync(client, shipment.Id);
        Assert.Equal(CodCollectionStatus.BranchReconciled, afterBranchReconcile.Status);

        var financeVerifyResponse = await client.PostAsJsonAsync("/api/v1/cod/finance-verifications",
            new CreateFinanceVerificationRequest(branchSettlement!.Id, 2500m));
        financeVerifyResponse.EnsureSuccessStatusCode();
        var afterFinanceVerify = await GetCollectionForShipmentAsync(client, shipment.Id);
        Assert.Equal(CodCollectionStatus.FinanceVerified, afterFinanceVerify.Status);

        var merchantSettlementResponse = await client.PostAsJsonAsync("/api/v1/cod/merchant-settlements",
            new CreateMerchantSettlementRequest(merchantId, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));
        merchantSettlementResponse.EnsureSuccessStatusCode();
        var merchantSettlement = await merchantSettlementResponse.Content.ReadFromJsonAsync<MerchantSettlementResponse>();
        var item = Assert.Single(merchantSettlement!.Items);
        // COD-LIFECYCLE.md §2's worked-example formula, proven against this test's own booked
        // shipment's actual locked-in TotalCharge rather than a hardcoded fee — robust to rate card
        // fixture changes while still proving CODAmount - CourierFee == NetPayable exactly.
        Assert.Equal(2500m, item.CODAmount);
        Assert.Equal(shipment.TotalCharge, item.CourierFee);
        Assert.Equal(2500m - shipment.TotalCharge, item.NetPayable);
        Assert.Equal(MerchantSettlementStatus.Created, merchantSettlement.Status);
        var afterSettlementCreated = await GetCollectionForShipmentAsync(client, shipment.Id);
        Assert.Equal(CodCollectionStatus.MerchantSettlementCreated, afterSettlementCreated.Status);

        var settleResponse = await client.PostAsync($"/api/v1/cod/merchant-settlements/{merchantSettlement.Id}/settle", null);
        settleResponse.EnsureSuccessStatusCode();
        var settled = await settleResponse.Content.ReadFromJsonAsync<MerchantSettlementResponse>();
        Assert.Equal(MerchantSettlementStatus.Settled, settled!.Status);
        Assert.NotNull(settled.SettledAt);
        var afterSettled = await GetCollectionForShipmentAsync(client, shipment.Id);
        Assert.Equal(CodCollectionStatus.Settled, afterSettled.Status);
    }

    [Fact]
    public async Task ConcurrentFinanceVerification_SameBranchCashSettlement_OnlyOneSucceeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-race");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndCompleteCodDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var riderSettlement = await (await client.PostAsJsonAsync("/api/v1/cod/rider-settlements",
            new CreateRiderSettlementRequest(deliveryRiderId, destinationBranchId, 2500m))).Content.ReadFromJsonAsync<RiderCashSettlementResponse>();
        var branchSettlement = await (await client.PostAsJsonAsync("/api/v1/cod/branch-settlements",
            new CreateBranchSettlementRequest(destinationBranchId, new List<Guid> { riderSettlement!.Id }, 2500m))).Content.ReadFromJsonAsync<BranchCashSettlementResponse>();

        // Every request shares the same Authorization header on the same HttpClient — HttpClient is
        // safe for concurrent requests, and each HTTP request gets its own scoped DbContext via DI
        // (same reasoning Phase 5's rate-card concurrency test relies on).
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            client.PostAsJsonAsync("/api/v1/cod/finance-verifications", new CreateFinanceVerificationRequest(branchSettlement!.Id, 2500m))));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
        var afterRace = await GetCollectionForShipmentAsync(client, shipment.Id);
        Assert.Equal(CodCollectionStatus.FinanceVerified, afterRace.Status);
    }
}
```

- [ ] **Step 2: Run the new tests**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~CodEndpointsTests"`
Expected: Both tests pass — this requires `courier-postgres-1` (port 5434) running; confirm with `docker exec courier-postgres-1 pg_isready -U courier` first if the run fails unexpectedly.

- [ ] **Step 3: Build and run the entire solution's tests one more time**

Run: `dotnet build`
Expected: Build succeeds.

Run: `dotnet test`
Expected: Every test project passes, with no regressions in any prior phase's tests.

- [ ] **Step 4: Commit**

```bash
git add tests/Api.Tests/CodEndpointsTests.cs
git commit -m "test(cod): add end-to-end worked-example and finance-verification race tests"
```

---

## Task 15: Docs sync — `CLARIFICATIONS.md`, `DOMAIN-MODEL.md`, `ROADMAP.md`

**Files:**
- Modify: `docs/CLARIFICATIONS.md`
- Modify: `docs/DOMAIN-MODEL.md`
- Modify: `docs/ROADMAP.md`

**Interfaces:** none — documentation only.

- [ ] **Step 1: Add `CLARIFICATIONS.md` entry #11**

Append a new section after entry #10 (`## 10. Phase 9 follow-up...`) and before the `## Summary of schema additions implied by this document` heading:

```markdown
## 11. Phase 10: `CODCollection`'s full lifecycle status, eager creation, settlement linkage, and adjustment approval

**Where:** `CLAUDE.md` §22-23, `COD-LIFECYCLE.md` (all sections), `DOMAIN-MODEL.md` "COD & Finance"

**Ambiguity 1:** `DOMAIN-MODEL.md`'s `CODCollection` entity lists `Status (Expected|Collected)` —
only the first of `COD-LIFECYCLE.md` §1's seven states — but that same document's prose states
"the COD *lifecycle* status above lives on `CODCollection`, not on `Shipment`", naming all seven
states as one chain.

**Resolution 1:** `CODCollection.Status` is a seven-member enum
(`CodExpected|CodCollected|RiderDeposited|BranchReconciled|FinanceVerified|
MerchantSettlementCreated|Settled`), read literally from the prose rather than the narrower table.
Every step in the chain (`Cod.Application.CodService.CreateRiderSettlementAsync`,
`CreateBranchSettlementAsync`, `CreateFinanceVerificationAsync`, `CreateMerchantSettlementAsync`,
`SettleMerchantSettlementAsync`) bulk-updates the `Status` of every `CODCollection` row it covers,
so "where is this shipment's COD right now" is always a single-row read, never a join across the
five downstream tables.

**Ambiguity 2:** `COD-LIFECYCLE.md` §1's table says `CODCollection` is "created at delivery, linked
to `DeliveryProof`" for the `COD_EXPECTED -> COD_COLLECTED` transition — but §3 separately defines
"Expected COD" as `Σ Shipment.CODAmount` for not-yet-delivered shipments, implying `CODCollection`
rows exist before delivery too.

**Resolution 2:** `CODCollection` is created eagerly, at shipment-booking time (a new
`ICodCollectionCreationService` — the same narrow cross-module-write interface shape
`INdrCreationService` established in Phase 9 — called from
`Shipments.Application.ShipmentService.CreateShipmentAsync` whenever `Shipment.CodAmount > 0`), at
`CodExpected`. `RiderService.CompleteDeliveryAsync` (Phase 8) then calls a second narrow interface,
`ICodCollectionService.MarkCollectedAsync`, to move the *existing* row to `CodCollected` at
delivery — matching the "linked to `DeliveryProof`" half of the table literally (`DeliveryProofId`
is stamped at that step) while resolving the apparent contradiction with §3. `MerchantId`,
`BranchId` (the shipment's `DestinationBranchId` at booking), and `ShipmentTotalCharge` are
snapshotted onto the row at creation — `Cod` has no project reference to `Shipments` at all as a
result, a stricter version of the "no cached custody copy" principle (`CLAUDE.md` §12) applied to
financial figures the same way `Shipment.TotalCharge` itself already locks pricing at booking
(`CLAUDE.md` §16).

**Ambiguity 3:** `DOMAIN-MODEL.md`'s `BranchCashSettlement` entity lists a
`RiderCashSettlementIds[]` array field to link the rider settlements it covers, but this codebase's
established one-to-many convention elsewhere (`ManifestShipment.ManifestId`,
`DeliveryTask.DeliveryRunId`) is always a child-side foreign key, never a parent-side array column.

**Resolution 3:** `RiderCashSettlement` gains a nullable `BranchCashSettlementId` foreign key
instead; `BranchCashSettlement` has no array column. `CreateBranchSettlementAsync`'s response still
returns the covered `RiderCashSettlementIds` as a `List<Guid>`, computed from the query, so no
information is lost to API consumers — only the storage shape differs from the original sketch.

**Ambiguity 4:** `CLAUDE.md` §23 says adjustments require approval "where appropriate" for amounts
above "a configurable threshold", but no threshold configuration entity or setting exists anywhere
in this codebase.

**Resolution 4:** `Adjustment` always requires a separate approval step in v1 — `RequestAdjustmentAsync`
only ever inserts an unapplied `Adjustment` row (`Applied = false`); a second call,
`ApproveAdjustmentAsync`, is the only place that ever rewrites the target figure
(`MerchantSettlementItem.OtherDeductions`/`NetPayable`, and the parent
`MerchantSettlement.TotalPayable`). No threshold configuration was needed to resolve this — "where
appropriate" resolves to "always" until a future phase introduces per-org configurable amounts.

**Implementation note (not an ambiguity, but worth recording):** `Riders.Application.RiderService.
CompleteDeliveryAsync`'s conditional `cod.collect` permission check (required only when the
shipment being delivered carries COD) is the first Application-layer consumer of
`SharedKernel.IPermissionChecker` — previously only the `RequirePermission` route filter used it.
`FinancialTransactionConfiguration` gives `(ReferenceEntityType, ReferenceEntityId)` a *unique*
index (not just a lookup index) so "verify the same `BranchCashSettlement` twice" is DB-enforced,
not just caught by an `AnyAsync` pre-check — the same read-then-write race lesson as Phase 5's
`RateCard` versioning fix (`CLAUDE.md`'s cross-phase rule), proven by a real `Task.WhenAll`
concurrent-request test against Postgres.

---
```

- [ ] **Step 2: Replace `DOMAIN-MODEL.md`'s "COD & Finance" section**

Replace the existing block (currently `## COD & Finance — \`OrganizationId\` present` through its closing ` ``` `) with:

```markdown
## COD & Finance — `OrganizationId` present (`MerchantSettlementItem` excepted — see below)

```text
CODCollection { Id, ShipmentId (unique), OrganizationId, MerchantId, BranchId, ExpectedAmount,
                 ShipmentTotalCharge, CollectedAmount (nullable),
                 Status (CodExpected|CodCollected|RiderDeposited|BranchReconciled|
                 FinanceVerified|MerchantSettlementCreated|Settled),         [C#11]
                 CollectedByRiderId (nullable), CollectedAt (nullable), DeliveryProofId (nullable),
                 RiderCashSettlementId (nullable), MerchantSettlementItemId (nullable) }
                 # Created eagerly at booking (CodExpected) by a new ICodCollectionCreationService,
                 # called from Shipments.Application.ShipmentService.CreateShipmentAsync — MerchantId/
                 # BranchId/ShipmentTotalCharge are snapshotted here, so Cod never reads back into
                 # Shipments. Status carries the full lifecycle chain (COD-LIFECYCLE.md §1), not just
                 # Expected/Collected. [C#11]

RiderCashSettlement { Id, OrganizationId, RiderId, BranchId, DeclaredAmount, ShipmentCount,
                        SettledAt, ConfirmedByUserId, BranchCashSettlementId (nullable) }         [C#11]

BranchCashSettlement { Id, OrganizationId, BranchId, ConfirmedAmount, ReconciledByUserId,
                         ReconciledAt }
                         # No RiderCashSettlementIds[] array (unlike this section's original
                         # sketch) — RiderCashSettlement.BranchCashSettlementId is the link. [C#11]

FinancialTransaction { Id, OrganizationId, Type (CodVerification), Amount, ReferenceEntityType,
                         ReferenceEntityId, VerifiedByUserId, VerifiedAt }
                         # (ReferenceEntityType, ReferenceEntityId) is a unique index — at most one
                         # verification per BranchCashSettlement, DB-enforced. [C#11]

MerchantSettlement { Id, OrganizationId, MerchantId, PeriodFrom, PeriodTo,
                       TotalPayable, Status (Created|Settled), SettledAt (nullable) }

MerchantSettlementItem { Id, MerchantSettlementId, ShipmentId (unique), CODAmount,
                           CourierFee, OtherDeductions, NetPayable }
                           # No own OrganizationId — flows transitively through
                           # MerchantSettlementId -> MerchantSettlement.OrganizationId. CourierFee is
                           # copied from the covering CODCollection.ShipmentTotalCharge, never
                           # recomputed from a current rate card (CLAUDE.md §16).

Adjustment { Id, OrganizationId, EntityType, EntityId, BeforeValue, AfterValue,
              Reason, RequestedByUserId, RequestedAt, ApprovedByUserId (nullable),
              ApprovedAt (nullable), Applied (bool) }
              # v1 only supports EntityType = "MerchantSettlementItem". Applied stays false until a
              # separate ApproveAdjustmentAsync call — approval is always required, no configurable
              # threshold exists yet. [C#11]
```
```

- [ ] **Step 3: Update `ROADMAP.md`'s Phase 10 section**

Replace the existing Phase 10 block:

```markdown
## Phase 10 — COD, Rider Settlements, Branch Reconciliation, Merchant Settlements

Full `COD-LIFECYCLE.md` chain: `CODCollection, RiderCashSettlement,
BranchCashSettlement, MerchantSettlement, MerchantSettlementItem,
FinancialTransaction`, plus the `Adjustment` record type.
*Exit:* the worked example in `COD-LIFECYCLE.md` §2 is reproducible end-to-end from a
delivered COD shipment through to a settled merchant payout, with every step
traceable and every adjustment audited.
```

with:

```markdown
## Phase 10 — COD, Rider Settlements, Branch Reconciliation, Merchant Settlements

Full `COD-LIFECYCLE.md` chain: `CODCollection, RiderCashSettlement,
BranchCashSettlement, MerchantSettlement, MerchantSettlementItem,
FinancialTransaction`, plus the `Adjustment` record type.
*Exit:* the worked example in `COD-LIFECYCLE.md` §2 is reproducible end-to-end from a
delivered COD shipment through to a settled merchant payout, with every step
traceable and every adjustment audited.

*Status (2026-08-25): Complete.* A new `Cod` module implements every entity from
`DOMAIN-MODEL.md`'s "COD & Finance" section, with `CLARIFICATIONS.md` #11 recording four
deliberate resolutions (`CODCollection.Status` carries the full seven-state lifecycle chain;
`CODCollection` is created eagerly at booking, not lazily at delivery; `RiderCashSettlement.
BranchCashSettlementId` replaces `BranchCashSettlement.RiderCashSettlementIds[]`; `Adjustment`
always requires a separate approval step). `CODCollection` is created by a new narrow
`ICodCollectionCreationService`, called from `Shipments.Application.ShipmentService.
CreateShipmentAsync` for any shipment with `CodAmount > 0` — the same cross-module-write pattern
`INdrCreationService` established in Phase 9. `Riders.Application.RiderService.
CompleteDeliveryAsync` gained a conditional `cod.collect` permission check (via
`SharedKernel.IPermissionChecker`, its first Application-layer consumer) and calls a second narrow
`ICodCollectionService.MarkCollectedAsync` to record the actual collected amount against the
`DeliveryProof` just created. `Cod` has no project reference to `Shipments` at all — every
financial fact it needs (`MerchantId`, `BranchId`, `ShipmentTotalCharge`) is snapshotted onto
`CODCollection` at creation, so the full rider-deposit -> branch-reconciliation ->
finance-verification -> merchant-settlement -> payout chain never reads back into another module.
Every multi-row step in that chain (`CreateRiderSettlementAsync` through `SettleMerchantSettlementAsync`,
plus `ApproveAdjustmentAsync`) runs inside an explicit `Database.BeginTransactionAsync()` — the
first use of an explicit transaction anywhere in this codebase, deliberately established here for
financial correctness. `FinancialTransactionConfiguration`'s unique `(ReferenceEntityType,
ReferenceEntityId)` index makes "verify the same `BranchCashSettlement` twice" impossible under
concurrency (not just checked-then-written), proven by a real `Task.WhenAll` race test against
Postgres — mirroring Phase 5's `RateCard` versioning fix. The full worked example from
`COD-LIFECYCLE.md` §2 (COD 2500, courier fee from the shipment's own locked-in `TotalCharge`,
net payable = the difference) is proven end-to-end over real HTTP in
`tests/Api.Tests/CodEndpointsTests.cs`'s `FullCodChain_BookingToMerchantPayout_
MatchesWorkedExampleAndMovesThroughEveryStatus` test. Deferred to later phases: scheduled/recurring
merchant settlement generation (needs Phase 11's Hangfire infrastructure — this phase is on-demand
only), and adjustment support for entity types other than `MerchantSettlementItem` (no other
concrete business need exists yet).
```

- [ ] **Step 4: Commit**

```bash
git add docs/CLARIFICATIONS.md docs/DOMAIN-MODEL.md docs/ROADMAP.md
git commit -m "docs: mark Phase 10 exit criteria met and record its resolved design ambiguities"
```

---

## Self-Review

**Spec coverage:** all seven `DOMAIN-MODEL.md` "COD & Finance" entities (Task 1) · the full `COD-LIFECYCLE.md` §1 state chain, one method per transition (Tasks 5-11) · the §2 worked example, proven both at the unit level (Task 10's `CreateMerchantSettlementAsync_MatchesWorkedExampleFromCodLifecycleDoc`) and end-to-end over real HTTP (Task 14) · §3's reconciliation visibility figures (Task 12's `GetReconciliationSummaryAsync`) · §4's never-silently-adjust principle with a mandatory approval step (Task 11) · §5's RTO/COD independence (no code change needed — `CODCollection` simply never leaves `CodExpected` for an RTO'd shipment, since `RiderService.RecordFailedAttemptAsync`'s NDR path never calls `MarkCollectedAsync`) · CLAUDE.md §33's never-trust-client-supplied-financial-data rule (every amount computed server-side from locked-in `Shipment`/`CODCollection` figures, never accepted from a request body as the figure that gets persisted) · tenant isolation on every new entity (Task 3's query filters) · branch scoping on every branch-relevant read/write (Tasks 7-8's `CheckBranchAccessAsync`, Task 12's `ListCollectionsAsync`/`GetReconciliationSummaryAsync`) · merchant-track self-scoping (Task 12, mirroring `ShipmentService.ListAsync`'s established pattern) · docs updated (Task 15). Every `ROADMAP.md` Phase 10 exit-criteria clause has a task and a proving test.

**Placeholder scan:** no TBD/TODO markers; every design decision left open by the source docs (COD lifecycle status scope, creation timing, settlement linkage shape, adjustment approval) is resolved explicitly in this plan's Architecture section and Task 15's `CLARIFICATIONS.md` entry, not deferred. The one genuinely narrowed scope (`Adjustment` supporting only `EntityType = "MerchantSettlementItem"`, scheduled merchant settlements deferred to Phase 11) is stated plainly in the Architecture section and Task 15's docs, not hidden.

**Type consistency:** `ICodCollectionCreationService.CreateExpectedAsync`'s six-parameter signature (Task 4) matches exactly between the interface, `CodService`'s implementation, `ShipmentService.CreateShipmentAsync`'s call site (Task 5), and every test fake across `Shipments.Tests`/`Riders.Tests`. `ICodCollectionService.MarkCollectedAsync`'s signature matches between the interface, `CodService`'s implementation, and `RiderService.CompleteDeliveryAsync`'s call site (Task 6). `ShipmentScanInfo.CodAmount` and `CompleteDeliveryRequest.CollectedCodAmount` are both trailing-optional, confirmed against every existing positional call site found by grep (11 for `ShipmentScanInfo`, all existing `CompleteDeliveryRequest` construction sites in `Riders.Tests`/`Api.Tests`) before this plan assumed they'd keep compiling unchanged. `CodCollectionResponse`/`RiderCashSettlementResponse`/`BranchCashSettlementResponse`/`FinancialTransactionResponse`/`MerchantSettlementResponse`/`AdjustmentResponse`/`CodReconciliationSummaryResponse` field shapes are identical everywhere constructed (`CodService.cs`'s and each partial file's `ToXResponse` helpers) and consumed (Task 13's Presentation layer, Task 14's tests). `RiderService`'s two new constructor parameters (`ICodCollectionService`, `IPermissionChecker`) are threaded through every one of its five test files' `new RiderService(...)` call sites (Task 6). `ShipmentService`'s one new constructor parameter (`ICodCollectionCreationService`) is threaded through both of its remaining call sites not otherwise touched by Task 5's own new test file.
