# Frontend Phase F7 — COD Operations — Design

**Date:** 2026-09-01
**Status:** Approved, ready for implementation planning.
**Roadmap entry:** `docs/FRONTEND-ROADMAP.md` Phase F7.

## Context

F0–F6 are complete (design system/shell, auth + org dashboard, branches, shipments,
manifests, riders/delivery/POD, NDR + RTO). Backend Phase 10 shipped a full `Cod`
module — the seven-state `CODCollection` lifecycle (`CLAUDE.md` §22), rider/branch
cash settlement, finance verification, merchant settlement + payout, and a
reason-required adjustment workflow (`CLAUDE.md` §23) — but `src/web` has no UI for
any of it. This phase builds that UI, per `docs/FRONTEND-ROADMAP.md`'s F7 entry:
COD collection views, rider/branch cash settlement, and reconciliation screens
surfacing expected/collected/deposited/reconciled amounts, with every adjustment
action requiring and displaying a reason.

Investigating the existing backend surface (`src/Modules/Cod/`) before design
turned up real gaps, confirmed with the user during brainstorming and folded into
this spec rather than deferred again — Phase 10's own final review had already
flagged two of these as "worth surfacing again before or early in Phase 12," and
this is that surfacing point.

## Resolved backend gaps (confirmed with user during brainstorming)

1. **`CodCollectionStatus`, `MerchantSettlementStatus`, `FinancialTransactionType`
   have no `JsonStringEnumConverter`.** Same latent bug class hit and fixed in
   every prior phase (`BranchType` F2, `ShipmentStatus` F3, `ManifestPurpose`/
   `ManifestStatus` F4, 5 Riders enums F5, 3 Ndr enums F6). Fixed proactively here
   for all 3 Cod enums at once, same as F5's/F6's approach.
2. **No `GET` to list `RiderCashSettlement` or `BranchCashSettlement` rows.** Only
   `POST` (create) exists for either. A branch user has no way to see "which rider
   settlements are still unreconciled" or "which branch settlements are still
   unverified" without already knowing an id — exactly the gap Phase 10's review
   flagged as making shortages/overages structurally unreadable.
3. **No `GET` to list `Adjustment` rows.** Only `POST /adjustments` (request, needs
   an `EntityId` the caller already knows) and `POST /adjustments/{id}/approve`
   (needs an `Id` the caller already knows) exist — an approver has no way to
   discover pending adjustments at all. Phase 10's review flagged this exact gap.
4. **`ApproveAdjustmentAsync` has no guard against approving against an
   already-`Settled` `MerchantSettlement`.** It unconditionally rewrites
   `MerchantSettlementItem.OtherDeductions`/`NetPayable` and recomputes
   `MerchantSettlement.TotalPayable` with no check on `MerchantSettlement.Status`.
   If the parent settlement was already settled (merchant already paid the old
   figure), approving a pending adjustment today silently changes `TotalPayable`
   on a row that's supposed to be closed, with no new payout to match — a real
   correctness bug against `CLAUDE.md` §23 ("never silently adjust financial
   amounts"). Also flagged (implicitly, as "no guard") in Phase 10's review.

## Backend changes

### 1. Enum serialization fix (all 3 at once)

Add `[JsonConverter(typeof(JsonStringEnumConverter))]` directly to
`CodCollectionStatus`, `MerchantSettlementStatus`, `FinancialTransactionType` —
same narrow, per-enum fix used in every prior phase.

### 2. `GET /api/v1/cod/rider-settlements`

```csharp
Task<PagedResult<RiderCashSettlementResponse>> ListRiderSettlementsAsync(
    Guid? branchId, Guid? riderId, bool? unreconciledOnly, int? page, int? pageSize,
    ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
```

Same shape as `ListCollectionsAsync`: filter `_dbContext.Set<RiderCashSettlement>()`
by `OrganizationId`, then `branchId`/`riderId` if supplied, then
`unreconciledOnly` (`BranchCashSettlementId == null`) if `true` — that flag is what
the Branch Settlements create-Sheet uses to populate its rider-settlement picker.
`RiderCashSettlement` carries its own `BranchId`, so branch-access scoping
(`_branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "cod.read",
...)`) is a direct `WHERE`, no join, same as `CODCollection`. `cod.read`. Paginated,
default sort `SettledAt DESC`.

### 3. `GET /api/v1/cod/branch-settlements`

```csharp
Task<PagedResult<BranchCashSettlementResponse>> ListBranchSettlementsAsync(
    Guid? branchId, bool? unverifiedOnly, int? page, int? pageSize,
    ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
```

Same pattern. `unverifiedOnly` means "no `FinancialTransaction` row references
this `BranchCashSettlement.Id` yet" (mirrors `CreateFinanceVerificationAsync`'s own
`alreadyVerified` check) — an `IN`/`NOT EXISTS` against
`_dbContext.Set<FinancialTransaction>()` filtered to
`ReferenceEntityType == "BranchCashSettlement"`. `cod.read`. Paginated, default
sort `ReconciledAt DESC`.

### 4. `GET /api/v1/cod/adjustments`

```csharp
Task<PagedResult<AdjustmentResponse>> ListAdjustmentsAsync(
    bool? applied, int? page, int? pageSize,
    ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
```

Filter `_dbContext.Set<Adjustment>()` by `OrganizationId` (own column, no join
needed — same as `ApproveAdjustmentAsync`'s existing lookup), then `Applied` if
supplied. `cod.read` for the list itself — `cod.adjust` stays required to actually
call `RequestAdjustmentAsync`/`ApproveAdjustmentAsync`, matching the existing
read/write permission split used everywhere else in this module (e.g.
`cod.read` lists collections, `cod.reconcile` creates settlements). Paginated,
default sort `RequestedAt DESC`.

### 5. `ApproveAdjustmentAsync` settle-guard

Immediately after loading `adjustment` and before the `switch`, load the target
`MerchantSettlement` (same query the `case AdjustableEntityType` branch already
runs, just moved earlier) and throw
`new CodValidationException("Cannot approve an adjustment against an already-Settled MerchantSettlement.")`
when `settlement.Status == MerchantSettlementStatus.Settled` — mirrors
`SettleMerchantSettlementAsync`'s own status guard pattern. Since v1 only supports
`EntityType == "MerchantSettlementItem"` (see `Adjustment.cs`'s doc comment), this
check applies unconditionally rather than living inside the `switch`'s single case;
if a future `EntityType` is ever added, its own guard moves in there with it.

### Not in scope this phase

- No new permission catalog entries — `cod.read`, `cod.collect`, `cod.reconcile`,
  `cod.settle`, `cod.adjust` already exist and are exactly the five actions this
  phase's UI needs.
- No `GET /adjustments/{id}` — the list response already carries every field a
  detail view needs (`AdjustmentResponse` has no fields omitted from the list
  shape, unlike `MerchantSettlementResponse` which nests `Items`), so a Sheet can
  render straight from the row the list query already fetched.
- No change to `FinancialTransaction` — it gets no list endpoint this phase either
  (see Frontend structure below for why: verification is folded into the Branch
  Settlement detail Sheet, not a standalone list).

## Frontend structure

New `src/web/src/features/cod/` feature folder, following the F2–F6 established
conventions (Sheet-over-pages via a Zustand panel store, `DataTable` for lists,
TanStack Query hooks per endpoint, Zod schemas for forms where user input exists).

One sidebar entry — `{ href: "/cod", label: "COD", permission: "cod.read", icon:
Wallet }` in `nav-main.tsx` — leading to a single `/cod` page with shadcn `Tabs`
for the six sections below (tab state in the URL via a `?tab=` search param, so a
direct link/refresh preserves which sub-view is open, same as how `page`/`status`
query params already work on other list pages).

### Collections tab

Read-only. `DataTable` — AWB (via a small per-row `GET`-free join: `CodCollectionResponse`
has no AWB field today and none is added this phase, since the tab's own
status/branch/merchant filters plus the amounts already answer "where does this
COD stand," and adding an AWB lookup here would repeat the same per-row-fetch cost
NDR's F6 gap-2 fix paid for a real workflow need — this tab has none), Expected
Amount, Collected Amount, `StatusBadge status={c.status} namespace="Cod"`,
Collected At. Filters: status dropdown, branch picker, merchant picker (branch/
merchant pickers reuse the existing `GeographyPicker`/merchant-select components
F3 already built). Row click opens a read-only `CodCollectionDetailSheet`. No
create — collections are born automatically at booking.

### Rider Settlements tab

`DataTable` — Rider, Branch, Declared Amount, Shipment Count, Settled At,
"Reconciled" badge (derived client-side from `branchCashSettlementId != null`, no
new status enum). Filters: branch, "unreconciled only" toggle (drives
`unreconciledOnly=true`). A **"New Settlement"** button (`cod.reconcile`) opens a
Create Sheet: rider picker, branch picker, declared amount input → `POST
/rider-settlements`. Row click opens a read-only detail Sheet. Every create-mutation
success invalidates both `["cod", "rider-settlements"]` and `["cod", "collections"]`
(declaring a rider settlement flips covered collections to `RiderDeposited`).

### Branch Settlements tab

`DataTable` — Branch, Confirmed Amount, Reconciled At, Reconciled By, "Verified"
badge (derived client-side from whether the row's detail fetch found a linked
verification — see below). Filters: branch, "unverified only" toggle. A **"New
Settlement"** button (`cod.reconcile`) opens a Create Sheet: branch picker, then a
multi-select list of that branch's unreconciled rider settlements (`GET
/rider-settlements?branchId=&unreconciledOnly=true`), confirmed amount input →
`POST /branch-settlements`. Row click opens a detail Sheet showing the covered
`RiderCashSettlementIds` (resolved to rider names via the rider-settlements list
already in cache) plus, if not yet verified, a **"Finance Verify"** action
(`cod.settle`) — amount input → `POST /finance-verifications` with this row's id.
This is why no separate Finance Verification tab exists: the backend has no list
endpoint for `FinancialTransaction` at all (not added this phase — see Backend
changes), and verifying is conceptually "the next single action on a specific
branch settlement," not a browsable queue of its own. Every settlement/
verification mutation invalidates `["cod", "branch-settlements"]`,
`["cod", "rider-settlements"]` (for the reconciled badge), and `["cod",
"collections"]`.

### Merchant Settlements tab

`DataTable` — Merchant, Period From/To, Total Payable, `StatusBadge
status={s.status} namespace="Cod"`, Settled At. Filter: merchant picker. A **"New
Settlement"** button (`cod.settle`) opens a Create Sheet: merchant picker, period
date-range picker → `POST /merchant-settlements`. Row click opens a detail Sheet:
period summary, a line-item table (Shipment, COD Amount, Courier Fee, Other
Deductions, Net Payable), a **"Request Adjustment"** action per line item
(`cod.adjust`, only rendered while the settlement's own `Status === "Created"` —
matches the backend's only-supported `EntityType`) opening a small form (new
`OtherDeductions` value + required reason textarea) → `POST /adjustments`, and a
top-level **"Settle"** button (`cod.settle`, enabled only while `Status ===
"Created"`, disabled elsewhere with an inline caption mirroring the RTO
Confirm-Return precedent from F6) → `POST /merchant-settlements/{id}/settle`.
Every mutation invalidates `["cod", "merchant-settlements"]` and, for adjustment
requests, `["cod", "adjustments"]`.

### Adjustments tab

`DataTable` — Entity Type, Entity Id (truncated, full id in a tooltip — this is
the one place the UI surfaces a raw id, since v1 has exactly one `EntityType` and
no friendlier label to join against without a new backend field), Before Value,
After Value, Reason, Requested By, Requested At, "Applied" badge. Filter: applied/
pending toggle. Row click (pending rows only) opens a confirmation Sheet showing
Before → After with the reason prominently displayed (per CLAUDE.md §23's
before/after-value display requirement) and an **"Approve"** action (`cod.adjust`)
→ `POST /adjustments/{id}/approve`, surfacing the new settle-guard's
`CodValidationException` message inline if the parent settlement was already
settled out from under the request. Invalidates `["cod", "adjustments"]` and
`["cod", "merchant-settlements"]` on success.

### Reconciliation tab

Dashboard-style, no `DataTable`. Filters: branch picker, merchant picker,
date-range picker → `GET /reconciliation`. Six stat tiles (reusing the `reports`
feature's existing tile component from `RateTiles.tsx`/`FinancialSummary.tsx`
rather than building a new one): Expected COD, Collected COD, Rider-Deposited COD,
Branch-Received COD, Merchant-Payable COD, Total Courier Fees — CLAUDE.md §23's
exact figure list.

### Hooks (`src/web/src/features/cod/hooks/`)

`useCodCollections(filters)`, `useCodCollection(id)`, `useRiderSettlements(filters)`,
`useCreateRiderSettlement()`, `useBranchSettlements(filters)`,
`useCreateBranchSettlement()`, `useCreateFinanceVerification()`,
`useMerchantSettlements(merchantId?)`, `useMerchantSettlement(id)`,
`useCreateMerchantSettlement()`, `useSettleMerchantSettlement()`,
`useAdjustments(filters)`, `useRequestAdjustment()`, `useApproveAdjustment()`,
`useCodReconciliation(filters)`. Every mutation's `onSuccess` invalidates the
broad list key (`["cod", "<entity>"]`, not a narrower keyed variant) — F4's
manifest-list staleness bug is exactly the mistake to not repeat here — plus
whichever sibling keys are documented per-tab above.

## Status badges

New `STATUS_CONFIG` entries, namespaced `"Cod.X"` (same reasoning as `"Ndr.X"`/
`"Manifest.X"`: flat keys risk collision, here specifically to keep
`MerchantSettlementStatus.Settled` distinct from `CodCollectionStatus.Settled`
under one Record):

- `Cod.CodExpected` → `status-created`
- `Cod.CodCollected` → `status-out-for-delivery`
- `Cod.RiderDeposited` → `status-in-transit`
- `Cod.BranchReconciled` → `status-sealed`
- `Cod.FinanceVerified` → `status-received`
- `Cod.MerchantSettlementCreated` → `status-ndr`
- `Cod.Settled` → `status-delivered`

`MerchantSettlementStatus` gets its own `"MerchantSettlement.X"` namespace rather
than sharing `"Cod.X"` — its `Settled` member would otherwise collide with
`CodCollectionStatus.Settled`'s key above under one flat `Record`:
`MerchantSettlement.Created` → `status-created`,
`MerchantSettlement.Settled` → `status-delivered`.

Before finalizing, run the `dataviz` skill's palette validator over the 7
`Cod.X` tokens as a set (this module's chain is longer than NDR's 6-state one, so
it reuses more tokens than F6 did and needs its own adjacent-pair check, not an
assumption that F6's validation covers it) and adjust any reuse that fails the
floor, preferring a different existing token over minting a new one.

## Testing

- **Backend**: unit tests for the three new list endpoints (including their
  `unreconciledOnly`/`unverifiedOnly`/`applied` filters and branch-scoping for the
  two settlement lists), a unit test proving `ApproveAdjustmentAsync` now throws
  `CodValidationException` against an already-`Settled` `MerchantSettlement`, and
  a regression test confirming the existing happy path (non-settled parent) still
  applies correctly. Integration test proving the three new list endpoints respect
  tenant/branch scoping the same way `ListCollectionsAsync` already does.
- **Frontend**: Vitest tests for the `cod` panel store, the Branch Settlement
  create-Sheet's rider-settlement multi-select, the adjustment request/approve
  forms' reason-required validation, and the Settle-button's disabled-state logic.
- **Manual E2E** against the real API/Postgres before the phase is called done —
  every prior phase's own pass found real bugs invisible to unit tests alone. Walk
  a COD shipment through: book with `CodAmount > 0` → deliver (existing F5 flow,
  confirm a `CodCollection` appears in the Collections tab at `CodCollected`) →
  create a rider settlement (confirm it flips to `RiderDeposited`) → create a
  branch settlement from that rider settlement (confirm `BranchReconciled`) →
  finance-verify it (confirm `FinanceVerified`) → create a merchant settlement for
  the period (confirm `MerchantSettlementCreated`) → request and approve an
  adjustment on the line item (confirm `TotalPayable` updates) → settle the
  merchant settlement (confirm `Settled`, and confirm a second adjustment request
  against the now-settled parent is correctly rejected by the new guard) → confirm
  the Reconciliation tab's six figures match at each stage. Also verify a
  branch-restricted user cannot see another branch's rider/branch settlements.

## Definition of Done (CLAUDE.md §37, applied to this phase)

Domain-accurate UI matching the full `COD_EXPECTED` → `SETTLED` chain;
permission-gated (`cod.read`, `cod.collect`, `cod.reconcile`, `cod.settle`,
`cod.adjust`) and branch-scoped per the backend's existing checks; every
adjustment action requires and displays a reason and before/after values
(`CLAUDE.md` §23); loading/error/empty states on every new screen; backend +
frontend tests passing; `docs/FRONTEND-ROADMAP.md` F7 entry updated with
completion notes; build green; no regressions in F0–F6.
