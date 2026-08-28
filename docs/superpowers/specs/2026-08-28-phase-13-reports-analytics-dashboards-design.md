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
  it in its own `Infrastructure` project, and the consumer never touches another
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
│                     six lookup interfaces below — Application-layer contracts,
│                     each implemented in its owning module's Infrastructure)
├── Infrastructure   (CSV-writing helper only — no DbContext, no entities)
└── Presentation     (ReportsEndpoints: 4 routes)
```

`Reports.Application`'s services depend on the lookup interfaces via DI; the
concrete implementations live in `Shipments.Infrastructure`,
`Manifests.Infrastructure`, `Riders.Infrastructure`, `Ndr.Infrastructure`, and
`Cod.Infrastructure` (the five new interfaces in §5), plus the two **existing**
Phase-5 interfaces `Branches.Application.IBranchLookupService` and
`Merchants.Application.IMerchantLookupService` reused as-is (no changes needed
there) to resolve branch/merchant names for the two CSV exports' `BranchName`/
`MerchantName` columns. `Reports.csproj` references each of those `*.Application`
projects — the same shape Pricing's project-reference graph already
established, just wider.

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

    // Total shipments created in range, per branch — the shared denominator
    // for NDR Rate / RTO Rate (§8) and Branch Performance volume.
    Task<IReadOnlyDictionary<Guid, int>> GetShipmentVolumeByBranchAsync(
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
    // branch dashboard's "Exceptions... open NDRs".
    Task<int> GetOpenNdrCountForBranchAsync(
        Guid branchId, CancellationToken ct = default);

    // NDR Rate = NDRs created in range / shipments created in range (the
    // latter supplied by the caller from §5.1's GetShipmentVolumeByBranchAsync
    // — Ndr has no project reference to Shipments, matching its established
    // Phase 9 "no cross-module DbSet access" boundary). RTO Rate = shipments
    // reaching any Rto* status in range / shipments created in range, counted
    // via ReturnShipment (one row per RTO, per Phase 9).
    Task<IReadOnlyDictionary<Guid, int>> GetNdrCountByBranchAsync(
        IReadOnlySet<Guid>? branchIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, int>> GetRtoCountByBranchAsync(
        IReadOnlySet<Guid>? branchIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
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
  `RequirePermission("report.read")`, then
  `IBranchAuthorizationChecker.HasAccessAsync(user, "report.read", branchId)` —
  `403` if false, matching every other single-branch endpoint's pattern
  (`Riders`/`Ndr`/`Manifests`).
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
composers — they call the lookup interfaces above (parallel `Task.WhenAll`
where independent) and assemble the response DTO; no business logic beyond the
rate/ratio math already spelled out in §5. `NDR Rate` and `RTO Rate` divide
§5.4's counts by §5.1's `GetShipmentVolumeByBranchAsync` denominator
per-branch, then sum for the org-wide figure (avoids the classic "average of
per-branch rates" mistake when branch volumes differ).

## 9. Error Codes

`INVALID_DATE_RANGE` (§7) joins the existing error-envelope catalog. No other
new codes — `403`/`404` reuse the existing branch-access and not-found
conventions already used by every other branch-scoped endpoint.

## 10. Testing

- **Unit** (per-lookup-service, against real Postgres per this project's
  established "test against the real infra it depends on" convention from
  Phase 10/11/12): each of the five new lookup implementations, covering both
  the correctness of its formula/bucketing and — explicitly, per §1's
  recurring-bug-class note — that it excludes another organization's rows and
  (where branch-scoped) another branch's rows, even when the caller supplies a
  guessed/leaked id.
- **Unit**: `OrgDashboardService`/`BranchDashboardService`'s rate/ratio math
  (division-by-zero when a branch has no shipments in range; NDR/RTO rate
  aggregation across uneven branch volumes per §8) and the CSV writer's
  escaping behavior.
- **API**: org dashboard for an `AllBranches` caller vs. a single-branch
  caller (proving the degrade-not-error behavior from §6); branch dashboard
  `403` for a caller without access to the requested branch; both export
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
