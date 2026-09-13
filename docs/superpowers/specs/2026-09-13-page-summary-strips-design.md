# Design: Summary Stat Strips on Core Operational Pages

**Date:** 2026-09-13
**Status:** Approved for planning
**Track:** Cross-module frontend/backend enhancement (no new module)

## 1. Problem

The 7 core staff list pages (Shipments, Branches, Riders, COD, Manifests, NDRs, Return
Shipments) show only a paginated table — a user has no at-a-glance sense of "how many are in
each state" without opening the org dashboard (`/dashboard`, Reports module) or paging through
the table themselves. The org dashboard already has this pattern (`QuickStatsStrip.tsx`,
`src/web/src/features/reports/components/`) but it's org-wide, date-ranged, and gated behind
`report.read` — not available to a user who only holds e.g. `shipment.read` and views their own
branch's Shipments page.

## 2. Goals

* Each of the 7 pages gets a row of stat tiles above its existing table/tabs, showing a
  status/category breakdown scoped to the user's accessible branches.
* Counts are always-on-branch-scope totals, independent of whatever filters are active on the
  table below (confirmed with the user — not filter-reactive).
* Visible to anyone who can already see the page (gated by that page's own read permission —
  `shipment.read`, `branch.read`, `rider.read`, `manifest.read`, `ndr.read`, `rto.read`,
  `cod.read` — never `report.read`).

## 3. Non-Goals

* Admin/platform/merchant-portal pages, Audit Logs, Billing — out of scope for this batch (user
  confirmed "core ops pages only").
* Per-tab summaries inside COD — one strip above all 6 tabs, not 6 separate strips.
* Reacting to table filters/search — counts are branch-scoped totals only (user confirmed).
* Real-time push updates — standard TanStack Query fetch-on-mount/focus-refetch, no polling.
* Any change to the Reports module's existing org/branch dashboards — those are untouched;
  this is new, separate, per-module surface.

## 4. Architecture Pattern

**Frontend:** one new shared component, `src/web/src/components/summary-stats/SummaryStatsBar.tsx`,
generalizing the tile look of `features/reports/components/QuickStatsStrip.tsx` (icon badge +
label + value in a `Card`, responsive `grid-cols-2 lg:grid-cols-N`). Props:

```ts
type StatTile = { label: string; value: string; icon: LucideIcon; badgeClass: string; highlight?: boolean };
function SummaryStatsBar({ tiles, isLoading, isError }: { tiles: StatTile[]; isLoading: boolean; isError: boolean }): JSX.Element
```

Loading state renders `N` skeleton-height placeholder cards (`N` = tiles.length once known, or a
fixed count passed in); error state renders nothing but a small inline `ErrorState`-style note
(never blocks the table below from rendering — the table's own loading/error state is
independent and already exists).

**Backend:** each *owning* module gets its own `GET .../summary` endpoint — not routed through
Reports. Each new endpoint:

1. Requires that module's existing read permission (never `report.read`).
2. Resolves accessible branch IDs via the same `IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, "<module>.read")`
   convention `BranchDashboardService`/`OrgDashboardService`/every module's existing branch-scoped
   service already uses (`null` = all branches, else restrict to the set).
3. Delegates to a new or extended method on that module's existing Application-layer service
   (or, for Shipments/Manifests/Riders/NDR, its existing `*ReportingLookupService` — these
   already live in the owning module's own `Application/` folder, e.g.
   `Shipments/Application/ShipmentReportingLookupService.cs`, not a separate Infrastructure
   project, so extending them is a same-file addition).
4. Tenant scoping is free — every entity already has a `HasQueryFilter` on `OrganizationId` at
   the `AppDbContext` level (`src/Persistence/AppDbContext.cs`); only branch-scoping needs
   explicit code, matching every existing lookup method.

No new project, no new cross-module reference. Reports module is untouched.

Every existing frontend list hook in this codebase calls a Next.js **BFF proxy route**
(`src/web/src/app/api/<x>/route.ts`, a thin `apiFetch` passthrough attaching the session's
bearer token server-side) rather than the .NET API directly — e.g. `useShipments` calls
`/api/shipments`, which proxies to `/api/v1/shipments`. Each new summary endpoint needs a
matching new proxy route (`src/web/src/app/api/<x>/summary/route.ts`) alongside the six that
already exist for these modules (`api/shipments`, `api/branches`, `api/riders`,
`api/manifests`, `api/ndrs`, `api/return-shipments`; `api/cod/<tab>` already exists per-tab, so
COD gets `api/cod/summary`). This is a real, separate file per page, not implied by the backend
endpoint's existence.

## 5. Per-Page Detail

### 5.1 Shipments — `GET /api/v1/shipments/summary`

New method on `IShipmentReportingLookupService` (`Shipments/Application/`):

```csharp
Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCurrentStatusCountsAsync(
    IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
```

Groups `Shipment` by `CurrentStatus`. Branch scoping must reuse the exact predicate
`ShipmentService.Reads.cs:ListAsync` already establishes for the shipment list itself — a
shipment counts toward a restricted caller's total if any of `OriginBranchId`,
`DestinationBranchId`, or `CurrentBranchId` (when non-null) is in their accessible set:

```csharp
query.Where(s =>
    accessibleBranchIdsArray.Contains(s.OriginBranchId) ||
    accessibleBranchIdsArray.Contains(s.DestinationBranchId) ||
    (s.CurrentBranchId != null && accessibleBranchIdsArray.Contains(s.CurrentBranchId.Value)));
```

A bare `CurrentBranchId`-only filter (`GetBranchShipmentCountsAsync`'s approach, which takes a
single `branchId` for one specific branch's own dashboard rather than a caller's whole
accessible set) would silently drop every in-transit/out-for-delivery/RTO-in-transit shipment
from a branch-restricted user's counts, since `CurrentBranchId` is null exactly during those
states (`ShipmentService.Transitions.cs` clears it to null on dispatch/out-for-delivery/RTO
dispatch) — precisely the states this summary needs to show. No date range — this is a live
snapshot, unlike the existing `GetCohortStatusCountsAsync` (cohort-by-creation-date, used only
by the org dashboard).

Frontend collapses the 15 raw `ShipmentStatus` values into 6 tiles for display: **Created**
(Created), **Picked Up** (PickupAssigned + PickedUp), **In Transit** (AtOriginBranch + Sorted +
InTransit + AtDestinationBranch + OutForDelivery), **Delivered** (Delivered), **NDR**
(DeliveryFailed + NdrCreated), **RTO** (RtoInitiated + RtoInTransit + RtoAtOrigin + RtoDelivered).
Cancelled is omitted from tiles (shown nowhere currently on this page either). The raw
dictionary is returned as-is from the API; bucketing is a frontend concern only (keeps the
endpoint reusable if bucketing needs differ later).

Endpoint: new `group.MapGet("/summary", ...)` in `ShipmentsEndpoints.cs`,
`.RequirePermission("shipment.read")`. Note this file already maps `GET /{awb}` (an
unconstrained string route param, since AWB is a string, not a `Guid`) ahead of the plain `GET
/`; ASP.NET Core's endpoint routing resolves a literal segment (`/summary`) ahead of a parameter
segment (`/{awb}`) by precedence regardless of registration order, so this doesn't get swallowed
as a lookup for AWB `"summary"` — but it's worth an explicit API test asserting that.

### 5.2 Branches — `GET /api/v1/branches/summary`

No cross-module lookup needed — direct query in `Branches/Application/BranchService.cs` (new
method `GetSummaryAsync`), since Branches don't need another module's data:

```csharp
Task<BranchSummary> GetSummaryAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
public record BranchSummary(int Total, int Active, int Inactive, IReadOnlyDictionary<BranchType, int> ByType);
```

Uses `IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, "branch.read")` to restrict
`Id` when scoped; groups by `IsActive` and `BranchType`. Endpoint added to
`BranchesEndpoints.cs`, `.RequireBranchScopedPermission("branch.read")` is not quite right here
since this returns an aggregate, not a single branch — use plain `.RequirePermission("branch.read")`
and apply the accessible-set filter inside the service (matches how `ShipmentService`,
`NdrService`, etc. already do it for list/aggregate endpoints, vs. `RequireBranchScopedPermission`
which is for single-resource-by-route-id endpoints like branch update).

### 5.3 Riders — `GET /api/v1/riders/summary`

New method on `IRiderReportingLookupService`:

```csharp
Task<RiderSummary> GetSummaryAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
public record RiderSummary(int Total, int Active, int Inactive, int CurrentlyAssigned);
```

Groups `Rider` by `IsActive`, filtered by `BaseBranchId` when scoped (riders are base-assigned
to a branch, unlike shipments' physical-custody tracking). `CurrentlyAssigned` reuses the
existing open-task-count logic (`GetBranchRiderTaskLoadAsync`'s per-rider `OpenTaskCount`,
generalized here to count riders with `OpenTaskCount > 0` across the accessible set, not just
one branch — small extension, not a new query pattern). Endpoint on `RidersEndpoints.cs`,
`.RequirePermission("rider.read")`.

### 5.4 Manifests — `GET /api/v1/manifests/summary`

New method on `IManifestReportingLookupService`:

```csharp
Task<IReadOnlyDictionary<ManifestStatus, int>> GetStatusCountsAsync(
    IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
```

Groups `Manifest` by `Status`, filtered where `branchIds` contains **either** `FromBranchId` or
`ToBranchId` (a manifest belongs to both the sending and receiving branch's view — a Branch
Manager at the destination branch should see inbound manifests counted too). Endpoint on
`ManifestsEndpoints.cs`, `.RequirePermission("manifest.read")`.

### 5.5 NDRs — `GET /api/v1/ndrs/summary`

New method on `INdrReportingLookupService`:

```csharp
Task<IReadOnlyDictionary<NdrStatus, int>> GetStatusCountsAsync(
    IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
```

`NDR` (`Ndr/Domain/NDR.cs`) has no branch column of its own. Correction after checking
`NdrService.Reads.cs:ListAsync` directly (the actual multi-row precedent, not just the
single-NDR `CheckBranchAccessAsync` check): the established scoping field is
`scanInfo.DestinationBranchId` alone (per-row `IShipmentTransitionService.GetScanInfoAsync`
lookup, since the filter can't be pushed into SQL) — not the origin/destination/current OR
predicate §5.1 uses for the Shipments list. Those are two different, already-established
conventions for two different entities; NDR summary must follow NDR's own. Endpoint on
`NdrEndpoints.cs`, `.RequirePermission("ndr.read")`.

### 5.6 Return Shipments — `GET /api/v1/return-shipments/summary`

New method, same interface (`INdrReportingLookupService`, since `NdrEndpoints.cs` already maps
both `/ndrs` and `/return-shipments` groups from the same `NdrService`):

```csharp
Task<ReturnShipmentSummary> GetReturnShipmentSummaryAsync(
    IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
public record ReturnShipmentSummary(
    IReadOnlyDictionary<ShipmentStatus, int> ByRtoStage, int MerchantConfirmed, int PendingConfirmation);
```

Correction after checking `NdrService.ReturnShipments.cs:ListReturnShipmentsAsync` directly:
`ReturnShipment` does *not* need a join for branch scoping — unlike bare `NDR`, it already
stores `DestinationBranchId` as a real column (despite the name, per that file's existing
comment this holds the *original* shipment's origin branch — where the parcel physically ends
up after `RtoAtOrigin`), so the accessible-branch filter pushes straight into SQL:
`query.Where(r => accessibleBranchIdsArray.Contains(r.DestinationBranchId))`, identical to
`ListReturnShipmentsAsync`'s own filter. `CurrentStatus` still isn't stored on `ReturnShipment`
itself (`ReturnShipment.cs`'s doc comment: the underlying `Shipment` is the single source of
truth), so grouping the 4 RTO statuses into `ByRtoStage` requires the same per-row
`IShipmentTransitionService.GetScanInfoAsync(OriginalShipmentId)` lookup
`ListReturnShipmentsAsync` already pays for every row it returns — no new performance
characteristic, just reused. `MerchantConfirmed`/`PendingConfirmation` counts come directly off
`ReturnShipment.MerchantConfirmedAt` with no lookup needed. Endpoint on the `returnShipments`
group in `NdrEndpoints.cs`, `.RequirePermission("rto.read")`.

### 5.7 COD — `GET /api/v1/cod/summary`

No new lookup method — `ICodReportingLookupService.GetOrgFinancialSummaryAsync(IReadOnlySet<Guid>? branchIds)`
already returns exactly `CodFinancialSummary(Expected, Collected, Pending, Settled, MerchantPayable)`
with no date bound (already a live snapshot, not cohort-based). Just a thin new endpoint in
`CodEndpoints.cs` that resolves accessible branch IDs for `"cod.read"` and calls the existing
method — no Application-layer change beyond the endpoint itself. One strip sits above the
`Tabs` in `cod/page.tsx`, shared by all 6 tabs (not per-tab).

## 6. Frontend Integration

Per page: one new hook (`features/<x>/hooks/use<X>Summary.ts`, TanStack Query, same
`clientFetch` convention every existing hook uses) + a small tile-mapping function local to that
page (raw counts → `StatTile[]`, choosing icon/badgeClass per status — reuses the existing
`StatusBadge` color tokens where they already map 1:1, e.g. `bg-status-delivered/15
text-status-delivered`, so tile colors visually match the `StatusBadge` colors already used in
each page's table). `<SummaryStatsBar>` is inserted directly under each page's `<h1>` heading,
above the existing table/tabs. Loading/error states are independent of the table's own — the
table already fetches and renders on its own query; the summary strip must not block it.

## 7. Testing

* **Unit** (per module's existing test project): new lookup/service methods — status grouping
  correctness, empty-org/zero-row case, branch-set narrowing.
* **Integration**: branch-scope enforcement per new endpoint — a user restricted to Branch A
  never sees Branch B's counts folded into the total; unscoped (`AllBranches`) role sees the
  org-wide total.
* **API**: each new endpoint — 401 without auth, 403 without the permission, 200 shape with the
  permission.
* **Frontend**: one render test per page confirming `SummaryStatsBar` renders tiles from the
  hook's data, and a `SummaryStatsBar` unit test for its own loading/error/tiles states.

## 8. Rollout

Each of the 7 pages is an independent vertical slice (own endpoint, own lookup method, own hook,
own page edit) — safe to implement as parallel subagent tasks grouped by module, per this
project's usual subagent-driven-development workflow. `SummaryStatsBar` itself is a
prerequisite shared component built first, before the 7 page tasks fan out.
