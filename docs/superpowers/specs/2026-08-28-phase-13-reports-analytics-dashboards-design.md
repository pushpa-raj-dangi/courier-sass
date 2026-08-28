# Phase 13 — Reports, Analytics, Advanced Dashboards — Design Spec

**Scope note:** ROADMAP.md's Phase 13 entry describes dashboards in product terms
(CLAUDE.md §29/§30). Matching every prior phase's precedent (and Phase 12's own
explicit scope split), this spec covers the **backend-only** slice: a new
`Reports` module exposing dashboard-shaped read endpoints and CSV export, proven
via API tests. The Next.js UI for these dashboards — and the still-deferred
merchant portal (12b) — remain a later phase. `src/web` stays untouched.

## 1. Context

- No `Reports` module exists. `report.read`/`report.export` are already rows in
  `Identity.Application.PermissionCatalog` (added at catalog-authoring time, never
  consumed by any endpoint yet). `OrganizationOwner` already holds every
  permission in the catalog (`AuthService.RegisterOrganizationAsync` grants
  `allPermissions`), so `report.read`/`report.export` are exercisable today with
  zero role-seeding change. No other CLAUDE.md §6 role (`FinanceManager`,
  `BranchManager`, `Auditor`, `OperationsManager`, ...) is seeded anywhere in code
  yet — only `OrganizationOwner`, `DeliveryRider`, `PickupRider` exist as real
  rows. This phase does not seed new roles; it only wires two permissions that
  already exist.
- `Shipment` has no `CreatedAt`/`UpdatedAt` columns — creation time and every
  status transition live exclusively as `ShipmentEvent` rows (`Timestamp`,
  `EventType`, `PreviousStatus`, `NewStatus`). Any "today's shipments" or
  "average delivery time" metric reads `ShipmentEvent`, not `Shipment`.
- Nine modules exist with data this phase needs to read:
  `Shipments` (status, `CurrentBranchId`), `Manifests` (`Status`, `ToBranchId`),
  `Riders`/`DeliveryTask`/`DeliveryAttempt` (task load, success/failure),
  `Ndr`/`ReturnShipment` (NDR/RTO counts), `Cod`/`MerchantSettlement` (financial
  totals), `Branches` (branch names for labeling).
- Phase 5 (Pricing) established this codebase's pattern for cross-module reads:
  the consuming module defines a narrow interface, the owning module implements
  it with a plain class in its own `Application` layer (verified against real
  code: `IBranchLookupService` -> `Branches.Application.BranchService`,
  `IMerchantLookupService` -> `Merchants.Application.MerchantLookupService` —
  `Infrastructure` in this codebase is reserved for things like
  `BranchAuthorizationChecker`/external adapters, not plain EF LINQ reads
  against a module's own `DbSet`), and the consumer never touches another
  module's `DbSet` directly. This phase reuses that pattern rather than
  introducing a shared reporting `DbContext` or raw cross-module SQL, which
  CLAUDE.md §3's module-boundary rule would rule out.
- **Recurring bug class (hit in Phases 4, 5, 10, 12):** a new read accessor
  forgetting the tenant/branch filter its sibling paths already enforce. This
  phase adds roughly six new cross-module read accessors at once — the single
  largest batch of new accessors in one phase to date. Verified against
  `AppDbContext`'s actual `HasQueryFilter` list (§11): every entity this phase
  reads — `Shipment`, `Manifest`, `Rider`, `DeliveryAttempt`, `NDR`,
  `ReturnShipment`, `CODCollection`, `MerchantSettlement` — already carries its
  own `OrganizationId` with an ambient tenant filter, so (matching
  `IBranchLookupService`/`IMerchantLookupService`'s existing precedent) none of
  the new lookup methods take an `organizationId` parameter; tenant isolation
  is automatic as long as the request is HTTP-scoped (it always is here — no
  background job touches this module). The one exception is `DeliveryTask`/
  `DeliveryRun` (Riders), which — like `ManifestShipment`/`ShipmentTransfer`
  before them — carry **no own `OrganizationId`** and get no ambient filter at
  all; §5.3 isolates them transitively by joining through `Rider` (which does
  have the filter), never querying `DeliveryTasks`/`DeliveryRuns` standalone.
  Branch scoping is a separate, non-tenant concern (§11): every branch-scoped
  method takes a caller-verified `branchId`/`branchIds` parameter, since
  `UserBranch` grants have no query-filter equivalent.

## 2. Goals / Exit Criteria

Per `ROADMAP.md`: *"dashboards reflect real data from all prior phases with
correct branch/role scoping, no hardcoded/demo data paths left in."*

Concretely:

- `GET /api/v1/reports/dashboard/organization` returns every CLAUDE.md §29
  figure, computed from real rows, scoped to the caller's accessible branches.
- `GET /api/v1/reports/dashboard/branch/{branchId}` returns every
  `BRANCH-OPERATIONS.md` §3 figure for one branch, rejecting callers without
  access to that branch.
- `GET /api/v1/reports/export/shipments` and
  `GET /api/v1/reports/export/cod-summary` stream real CSV data, gated by
  `report.export` (distinct from `report.read` on the dashboards).
- Every new cross-module query is unit-tested against real Postgres for both
  correctness and tenant/branch isolation (per the recurring-bug-class note
  above).

## 3. Non-Goals (explicit deferrals)

- Any frontend/dashboard UI (both the staff dashboards here and the merchant
  portal from 12b).
- Caching or pre-aggregation (materialized views, scheduled Hangfire rollups).
  All figures are computed live via `GroupBy`/`Count`/`Sum` queries. Flagged as a
  future perf item only if it becomes one — no evidence yet that it will,
  matching this project's established YAGNI discipline (e.g. Phase 12's Redis
  fail-closed note was written down *after* the need was real, not speculated
  ahead of it).
- Seeding any new role (`FinanceManager`, `BranchManager`, `Auditor`,
  `OperationsManager`). Only the already-cataloged `report.read`/`report.export`
  permissions are consumed.
- A `NotificationTemplate`-style CRUD API for report definitions — the two
  dashboards and two exports below are the fixed v1 set, not a generic
  report-builder.
- Historical trend storage/snapshotting. The `from`/`to` window (§7) is always
  computed against current live data, never a stored point-in-time snapshot.

## 4. Module

New `src/Modules/Reports/` following the standard four-layer layout:

```
Reports
├── Domain          (empty — Reports owns no entities of its own)
├── Application      (dashboard/export DTOs, IOrgDashboardService,
│                     IBranchDashboardService, IReportExportService, and the
│                     five lookup interfaces below — Application-layer
│                     contracts, each implemented by a plain class in its
│                     owning module's own Application layer)
├── Infrastructure   (CSV-writing helper only — no DbContext, no entities)
└── Presentation     (ReportsEndpoints: 4 routes)
```

`Reports.Application`'s services depend on the lookup interfaces via DI; the
concrete implementations live in `Shipments.Application`,
`Manifests.Application`, `Riders.Application`, `Ndr.Application`, and
`Cod.Application` (the five new interfaces in §5, one new plain class per
module — e.g. `Manifests.Application.ManifestReportingLookupService` — not a
new partial-class file on the existing `ManifestService`/etc., since these
reads are unrelated to those services' own responsibilities), plus the two
**existing**
Phase-5 interfaces `Branches.Application.IBranchLookupService` and
`Merchants.Application.IMerchantLookupService` reused as-is (no changes needed
there) to resolve branch/merchant names for the two CSV exports' `BranchName`/
`MerchantName` columns. This codebase uses one `.csproj` per module (`Domain`/
`Application`/`Infrastructure`/`Presentation` are folders/namespaces inside
it, not separate projects), so `Reports.csproj` takes a `ProjectReference` to
each of `Shipments.csproj`, `Manifests.csproj`, `Riders.csproj`, `Ndr.csproj`,
`Cod.csproj`, `Branches.csproj`, `Merchants.csproj` — seven references, wider
than any existing module's, but the same per-module (not per-layer) reference
shape every module already uses (e.g. `Shipments.csproj` already references
eight sibling modules).

## 5. Cross-Module Lookup Interfaces

No method takes `organizationId` — every query relies on `AppDbContext`'s
ambient tenant filter (directly for entities with their own `OrganizationId`
column, transitively through `Rider` for `DeliveryTask`/`DeliveryRun`), same
as `IBranchLookupService`/`IMerchantLookupService` today. Where a result is
branch-scoped, methods take a caller-verified `IReadOnlySet<Guid>? branchIds`
(`null` = every branch in the org — the same convention
`IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync` already returns, so
`Reports.Application` passes that result straight through) or a single
caller-verified `branchId` for the branch-dashboard methods.

### 5.1 `IShipmentReportingLookupService` (Shipments)

```csharp
public interface IShipmentReportingLookupService
{
    // "Today's Shipments" breakdown (CLAUDE.md §29): shipments whose *first*
    // ShipmentEvent (EventType == Created) falls within [from, to), grouped by
    // their *current* status. Resolves CLAUDE.md's ambiguous "Today's Shipments
    // / Created / Picked Up / ... / Delivered" list as "today's cohort, by
    // current stage" rather than "events that happened today" — the latter
    // would double-count a shipment across multiple status buckets on the same
    // day, which the dashboard's mutually-exclusive bucket list rules out.
    Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCohortStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    // Branch dashboard's Outgoing/Pending-sorting/Pending-dispatch (BRANCH-
    // OPERATIONS.md §3): all three are readable purely from CurrentBranchId +
    // CurrentStatus, since a manifest dispatch is exactly what flips a
    // shipment's status away from Sorted (Phase 7) — no join to Manifest
    // needed here. PendingSorting = CurrentStatus == AtOriginBranch at this
    // branch; PendingDispatch = CurrentStatus == Sorted at this branch;
    // Outgoing = the union of both, matching the doc's literal wording.
    Task<BranchShipmentCounts> GetBranchShipmentCountsAsync(
        Guid branchId, CancellationToken ct = default);

    // Delivered shipments in range: AVG(deliveredEvent.Timestamp -
    // createdEvent.Timestamp), joined per ShipmentId via ShipmentEvent.
    Task<TimeSpan?> GetAverageDeliveryTimeAsync(
        IReadOnlySet<Guid>? branchIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    // Shipment IDs created in range, bucketed by DestinationBranchId — the shared input for NDR
    // Rate / RTO Rate / Branch Performance volume (§8). Returns IDs, not just counts, because
    // neither NDR nor ReturnShipment carries its own creation timestamp (§5.4) — Reports.
    // Application resolves this set once, then hands it to INdrReportingLookupService as a plain
    // `WHERE ShipmentId IN (...)` filter, rather than Ndr needing to know about dates at all.
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetShipmentIdsByDestinationBranchAsync(
        IReadOnlySet<Guid>? branchIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public record BranchShipmentCounts(int Outgoing, int PendingSorting, int PendingDispatch);
```

### 5.2 `IManifestReportingLookupService` (Manifests)

```csharp
public interface IManifestReportingLookupService
{
    // Incoming (ToBranchId == branchId, Status == InTransit) and Pending
    // manifests (same query — BRANCH-OPERATIONS.md §3 lists these as two
    // separate bullets over the same underlying set: manifests in transit
    // toward this branch, awaiting receipt) and Received (Status == Closed,
    // ClosedAt/CreatedAt within a trailing window, default 24h).
    Task<BranchManifestCounts> GetBranchManifestCountsAsync(
        Guid branchId, CancellationToken ct = default);

    // Sum of missing-shipment exceptions across this branch's manifests —
    // reuses the same "expected ManifestShipment rows with no matching
    // receive-scan" logic Phase 7's existing GET /{id}/missing-shipments
    // already computes per-manifest, applied across every manifest destined
    // for this branch instead of just one.
    Task<int> GetOpenMissingShipmentExceptionCountAsync(
        Guid branchId, CancellationToken ct = default);
}

public record BranchManifestCounts(int InTransitTowardBranch, int RecentlyClosed);
```

### 5.3 `IRiderReportingLookupService` (Riders)

```csharp
public interface IRiderReportingLookupService
{
    // Active riders based at this branch, with their current open
    // (non-Completed) DeliveryTask count — branch dashboard's "Riders...
    // current task load".
    Task<IReadOnlyList<RiderTaskLoad>> GetBranchRiderTaskLoadAsync(
        Guid branchId, CancellationToken ct = default);

    // DeliveryAttempt.Succeeded ratio within [from, to), bucketed by the
    // attempting rider's BaseBranchId — feeds both "Delivery Success Rate"
    // and "Rider Performance" (org dashboard, CLAUDE.md §29) and the branch
    // dashboard's "Deliveries... success rate".
    Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>> GetSuccessRateByBranchAsync(
        IReadOnlySet<Guid>? branchIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>> GetSuccessRateByRiderAsync(
        IReadOnlySet<Guid>? branchIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public record RiderTaskLoad(Guid RiderId, int OpenTaskCount);
public record DeliverySuccessStats(int Attempts, int Successes);
```

### 5.4 `INdrReportingLookupService` (Ndr)

```csharp
public interface INdrReportingLookupService
{
    // Open NDRs (Status != Resolved) for shipments currently at this branch —
    // branch dashboard's "Exceptions... open NDRs". Neither NDR nor its
    // shipment carries a branch column NDR can filter by directly (Phase 9's
    // established shape), so this reuses NdrService.ListAsync's existing
    // per-row IShipmentTransitionService.GetScanInfoAsync branch-resolution
    // loop — org-ambient-filtered NDR rows first, then filtered in memory by
    // DestinationBranchId. Same cost profile as that existing method; this is
    // a second, narrower caller of the identical pattern, not a new one.
    Task<int> GetOpenNdrCountForBranchAsync(
        Guid branchId, CancellationToken ct = default);

    // NDR Rate / RTO Rate (§8) need "created in range", but neither NDR nor
    // ReturnShipment has a creation timestamp of its own (Phase 9 deliberately
    // left both thin — NDR creation is driven entirely by the automatic
    // DELIVERY_FAILED -> NDR_CREATED transition, with no persisted NDR-side
    // clock). Resolving "in range" therefore has to start from the shipment,
    // not the NDR row. §5.1's GetShipmentIdsByDestinationBranchAsync already
    // resolves "shipments created in range, per branch" — Reports.Application
    // flattens that to one shipment-id set and passes it here as a plain
    // membership filter; every method below is a single `WHERE ShipmentId IN
    // (...)` query, org-scoped automatically via NDR/ReturnShipment's own
    // ambient filter, with no date parameter and no branch parameter of its
    // own. Reports.Application intersects the returned set back against its
    // per-branch id lists to get per-branch counts/rates (§8).
    Task<IReadOnlySet<Guid>> GetShipmentIdsWithAnyNdrAsync(
        IReadOnlySet<Guid> shipmentIds, CancellationToken ct = default);

    Task<IReadOnlySet<Guid>> GetShipmentIdsWithRtoAsync(
        IReadOnlySet<Guid> shipmentIds, CancellationToken ct = default);
}
```

### 5.5 `ICodReportingLookupService` (Cod)

```csharp
public interface ICodReportingLookupService
{
    // Org dashboard Financial section (CLAUDE.md §29):
    //   Expected  = SUM(ExpectedAmount) where Status != Settled
    //   Collected = SUM(CollectedAmount) where Status >= CodCollected (i.e. not
    //               CodExpected) — money physically in the system somewhere
    //   Pending   = Collected - SUM(CollectedAmount) where Status == Settled
    //   Settled   = SUM(MerchantSettlement.TotalPayable) where Status == Settled
    //   MerchantPayable = SUM(MerchantSettlement.TotalPayable) where
    //               Status == Created (created, not yet paid out)
    // These five formulas are this phase's resolved reading of CLAUDE.md §29's
    // bare field list — written here explicitly since, like COD-LIFECYCLE.md's
    // Phase-10 status-model ambiguity, the doc names the figures without
    // defining them precisely.
    Task<CodFinancialSummary> GetOrgFinancialSummaryAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken ct = default);

    // Branch dashboard's "COD... RIDER_DEPOSITED/BRANCH_RECONCILED totals
    // pending finance verification" — CLAUDE.md §30's literal wording, no
    // reinterpretation needed.
    Task<decimal> GetBranchPendingFinanceVerificationTotalAsync(
        Guid branchId, CancellationToken ct = default);
}

public record CodFinancialSummary(
    decimal Expected, decimal Collected, decimal Pending,
    decimal Settled, decimal MerchantPayable);
```

## 6. Endpoints

All four routes live in `Reports.Presentation.ReportsEndpoints`, under
`/api/v1/reports`, staff-JWT only (no merchant/API-key access — these are
internal operational views, not part of the Merchant API catalog).

- **`GET /api/v1/reports/dashboard/organization?from=&to=`** —
  `RequirePermission("report.read")`. `from`/`to` optional, default trailing 30
  days (§7), used only for the Operational section; the Today's-Shipments and
  Financial sections are independently scoped as described in §5.1/§5.5. Calls
  `IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, "report.read")`
  once and threads the resulting `branchIds` (or `null` for all-branches) into
  every lookup call — a `BranchManager` scoped to one branch sees an
  "organization" dashboard containing only their branch's data, not an error;
  this matches how every other org-wide list endpoint in this codebase already
  degrades for a branch-scoped caller.
- **`GET /api/v1/reports/dashboard/branch/{branchId}?from=&to=`** —
  `RequirePermission("report.read")`, then the same
  `CheckBranchAccessAsync(actingUser, "report.read", branchId)` pattern every
  other module's `Application` service already private-implements
  (`ManifestService`/`NdrService`/`RiderService`/`CodService`) —
  `IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync`, reject if the
  accessible set is non-null and excludes `branchId`. Matching that
  established convention exactly: this throws a new
  `ReportsValidationException`, caught by the endpoint's `HandleAsync` and
  mapped to `400 REPORTS_VALIDATION_FAILED` — **not** `403`. (No existing
  single-item branch-scoped *read* endpoint in this codebase currently
  enforces branch access at all — `CLARIFICATIONS.md`/Phase 9's memory record
  this as an acknowledged, not-yet-fixed gap on `NdrService.GetByIdAsync` — so
  this dashboard endpoint is the first read path to add the check, and reuses
  the write-path convention rather than inventing a new one.)
- **`GET /api/v1/reports/export/shipments?from=&to=&branchId=`** —
  `RequirePermission("report.export")`. `branchId` optional (defaults to the
  caller's full accessible set). Streams `text/csv`; columns: `Awb, Status,
  OriginBranch, DestinationBranch, MerchantName, CodAmount, TotalCharge,
  CreatedAt, DeliveredAt`. `CreatedAt`/`DeliveredAt` read from `ShipmentEvent`
  per §5.1 (`DeliveredAt` blank if not yet delivered). Financial columns are
  fine here — unlike public tracking (CLAUDE.md §24), this is an internal,
  permissioned, staff-only report.
- **`GET /api/v1/reports/export/cod-summary?from=&to=&branchId=`** —
  `RequirePermission("report.export")`. Columns: `Awb, MerchantName,
  BranchName, ExpectedAmount, CollectedAmount, Status, CollectedAt`.

Neither existing lookup interface can resolve a display name today —
`IBranchLookupService` only has `GetZoneInfoAsync` (`BranchId`/`DeliveryZoneId`)
and `IMerchantLookupService` only has `ExistsAsync` — so both CSV exports also
need one small additive method on each (bulk, not per-row, to avoid an N+1 per
CSV row):

```csharp
// Branches.Application.IBranchLookupService — new method alongside GetZoneInfoAsync
Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken ct = default);

// Merchants.Application.IMerchantLookupService — new method alongside ExistsAsync
Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken ct = default);
```

Purely additive — existing callers/implementations of both interfaces are
unaffected; `BranchService`/`MerchantLookupService` each gain one new method.

CSV writing is a small hand-rolled helper in `Reports.Infrastructure`
(quote-and-escape any field containing a comma/quote/newline) — not a new
NuGet package. Matches CLAUDE.md §34 and the precedent Phase 12 set for its
hand-rolled Redis rate limiter over a third-party package: a correct CSV writer
for a handful of known, mostly-numeric columns is a short, easily-tested
function, not a case for a new dependency.

## 7. Date-Range Handling

- `from`/`to` are optional `DateTimeOffset` query parameters on all four
  routes. Default window: `[UtcNow - 30 days, UtcNow]`.
- The Today's-Shipments cohort (§5.1) and the branch-dashboard's live-state
  bullets (pending sorting/dispatch, incoming manifests, exceptions, rider
  task load) are **not** affected by `from`/`to` — they always reflect current
  state / the current calendar day, per CLAUDE.md §29's explicit "Today's"
  label and BRANCH-OPERATIONS.md §3's inherently-current-state bullets.
  `from`/`to` only widen or narrow the Operational section (success/NDR/RTO
  rates, average delivery time, branch/rider performance) and the two export
  endpoints.
- Invalid range (`from > to`, or a window wider than 1 year) → `400
  INVALID_DATE_RANGE`.

## 8. DTO Composition (Application layer)

`Reports.Application.OrgDashboardService` and `BranchDashboardService` are thin
composers — they call the lookup interfaces above and assemble the response
DTO; no business logic beyond the rate/ratio math already spelled out in §5.
For NDR Rate / RTO Rate specifically: `OrgDashboardService` calls §5.1's
`GetShipmentIdsByDestinationBranchAsync` once, flattens the per-branch id
lists into one set, passes that set to §5.4's
`GetShipmentIdsWithAnyNdrAsync`/`GetShipmentIdsWithRtoAsync`, then — entirely
in-memory, no further DB calls — intersects the returned id sets back against
each branch's own id list to get a per-branch numerator, dividing by that
branch's volume (the id list's `Count`) for the per-branch rate, then summing
numerators/denominators across branches (not averaging the rates) for the
org-wide figure — avoids the classic "average of per-branch rates" mistake
when branch volumes differ. These two calls (`Task.WhenAll`) run in parallel
with the independent Financial/Operational lookups (§5.3/§5.5).

## 9. Error Codes

Two new codes, both raised via a new `Reports.Application.ReportsValidationException`
and mapped to `400` by `ReportsEndpoints`' `HandleAsync` — matching the
existing per-module `{Module}ValidationException` -> `Results.BadRequest(new {
error = new { code, message } })` convention (`NdrEndpoints`,
`ManifestsEndpoints`, ...), not a new pattern:

- `REPORTS_VALIDATION_FAILED` — the branch-access rejection from §6.
- `INVALID_DATE_RANGE` — §7's `from > to` / >1-year-window rejection.

## 10. Testing

- **Unit** (per-lookup-service, against real Postgres via the existing
  `Identity.Tests.TestDbContextFactory`-style fixture — not EF Core InMemory,
  which most other modules' plain business-logic tests use): each of the five
  new lookup implementations, covering both the correctness of its
  formula/bucketing and — explicitly, per §1's recurring-bug-class note —
  that it excludes another organization's rows and (where branch-scoped)
  another branch's rows, even when the caller supplies a guessed/leaked id.
  Real Postgres specifically because these tests are verifying
  `HasQueryFilter` / ambient-tenant-filter behavior itself, the same reason
  `Identity.Tests.BranchAuthorizationCheckerTests` already tests against real
  Postgres rather than InMemory — not a blanket "this phase uses real infra"
  rule.
- **Unit**: `OrgDashboardService`/`BranchDashboardService`'s rate/ratio math
  (division-by-zero when a branch has no shipments in range; NDR/RTO rate
  aggregation across uneven branch volumes per §8) and the CSV writer's
  escaping behavior.
- **API**: org dashboard for an `AllBranches` caller vs. a single-branch
  caller (proving the degrade-not-error behavior from §6); branch dashboard
  `400 REPORTS_VALIDATION_FAILED` for a caller without access to the
  requested branch; both export
  endpoints' `Content-Type`/header row/row-count/value correctness against
  seeded data; `report.read` and `report.export` enforced as genuinely
  separate permissions (a caller with only one of the two is rejected from the
  other's routes).

## 11. Tenant/Branch Isolation Checklist

- No lookup interface method takes `organizationId`. Every implementation
  relies on `AppDbContext`'s ambient tenant filter — directly (the query's
  root entity already carries `OrganizationId`) or transitively (`Riders`'
  `DeliveryTask`/`DeliveryRun` methods must query through a `Rider`
  navigation/join, never `DbSet<DeliveryTask>()`/`DbSet<DeliveryRun>()`
  standalone, since neither carries its own `OrganizationId` or filter). This
  is safe specifically because every call in this phase originates from an
  HTTP-scoped staff request — `ICurrentTenantProvider` is guaranteed non-null
  — unlike Phase 11's Hangfire-job case, where the same ambient filter
  silently returns zero rows for everyone.
- Branch-scoped lookups take either a single caller-verified `branchId` (after
  `HasAccessAsync` has already rejected an inaccessible one) or the full
  `branchIds` set already resolved by `GetAccessibleBranchIdsAsync` — no
  lookup implementation re-derives branch access itself; `Reports` is the
  single place that resolves access, every module it calls just filters by
  the set it's handed.
- `Reports` module itself owns no tables, so there is no new entity to
  forget an `OrganizationId` column on — the isolation risk here is entirely
  in the five new *owning-module* accessors, which is why §10 calls out
  cross-tenant/cross-branch leakage as an explicit required test per accessor,
  not an incidental one.
