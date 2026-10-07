# F15 Console Redesign — R3a: COD Finance — Design Spec

## 1. Context

R3 ("Staff finance & network", `docs/FRONTEND-ROADMAP.md` Phase F15) was split during
brainstorming into **R3a — Finance** (this spec: the COD page) and **R3b — Network** (manifests,
NDR, RTO, riders, live map, branches; its own spec later, which starts by drawing artboards since
the canvas has none for those pages).

R3a rebuilds `app/(staff)/cod/page.tsx` against the design canvas artboard `Cod`
(claude.ai artifact https://claude.ai/artifact/1dGwukUsc2jwxhThg5eyHu, `project/Cod.dc.html`),
using the R1 page primitives (`PageHeader`, `SectionCard`, `PipelineStrip`, `ComingSoonSlot`,
`SheetHeaderBlock`) and R2 patterns (URL state, `usePeopleNames`, range presets).

### Scope boundary (approved — same as R2)

A **small, read-only backend slice** in the Cod module (§3) is in scope: new read endpoints,
new query filters and additive response fields computed from data that already exists. **No
migrations, no new entities, no new write endpoints, no change to existing write behaviour.**
Everything the artboard shows that needs new writes or new stored data renders a
`ComingSoonSlot` / disabled "Needs backend" control and is listed in §10 — never mock numbers
(DESIGN.md No-Fake-Numbers Rule).

### What the artboard shows vs what exists

| Artboard element | Source in R3a |
|---|---|
| Six-step COD lifecycle strip (amount + parcel count) | New `GET /cod/flow` (§3.1) |
| Rider deposit table: expected / deposited / variance | New `expectedAmount`/`variance` on rider settlements (§3.3) |
| Riders "on run" / "cash held 26 h" | New `GET /cod/rider-cash` (§3.2) |
| Run id column (DR-0930-07) | Not served — dropped (§10) |
| Record deposit: expected, cash counted, variance | Existing `POST /cod/rider-settlements` + rider-cash expected; variance previewed client-side |
| Denomination breakdown, Fonepay QR split | Needs backend |
| Shortage reason, approver note, approval threshold | Needs backend (adjustments only support `MerchantSettlementItem`) |
| Close branch day, Download ledger, Ledger tab | Needs backend |
| Merchant settlement breakdown + trace one line | Existing merchant settlement items + adjustments |
| "COD handling fee 1%" line | Not served (only `otherDeductions`) — rendered as "Other deductions" |

## 2. Goals / Exit Criteria

1. `/cod` shows a page header with branch picker and range presets, the money-flow strip, and
   tabs Collections · Rider deposits · Branch settlements · Merchant settlements · Adjustments ·
   Ledger, with the active tab in the URL.
2. Rider deposits shows who holds cash now, recent deposits with server-computed variance, and a
   record-deposit Sheet with a live variance preview.
3. Merchant settlement detail shows a reconciling payable breakdown and per-line trace.
4. Dashboard's "Rider-held vs deposited" slot and a "Riders holding cash > 24 h" needs-action row
   use the new reads.
5. Every number is server data; loading/error never renders as 0.
6. Tests per §8 pass; web `npm run build`, `npm run lint`, `npm test` pass; `npx tsc --noEmit`
   shows no errors beyond the 8 pre-existing ones; `dotnet build` and `dotnet test` pass.

### Non-goals

- Any new write behaviour (shortage approval, denominations, QR split, close day, ledger, export).
- R3b network pages.
- Merchant-portal COD pages (R5).
- i18n of page copy beyond what already exists (English, as R2).

## 3. Backend slice (Cod module, read-only)

All endpoints live under the existing `/api/v1/cod` group and follow its conventions: tenant
filter (`OrganizationId`), branch scope via `IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, "cod.read")`
(a `branchId` filter can only narrow within scope; an out-of-scope `branchId` yields an empty
result, not an error), `cod.read` permission, validation errors as the module's existing 400
contract. Staff-only: merchant callers receive **403** on the two new endpoints (§3.1, §3.2).

### 3.1 `GET /api/v1/cod/flow?branchId=&from=&to=` (new)

Response:

```json
{ "stages": [
  { "key": "expected",         "amount": 1248500, "count": 482 },
  { "key": "collected",        "amount":  986200, "count": 391 },
  { "key": "riderDeposited",   "amount":  840000, "count": 331 },
  { "key": "branchReconciled", "amount":  795400, "count": 314 },
  { "key": "financeVerified",  "amount":  610000, "count": 241 },
  { "key": "settled",          "amount":  480300, "count": 190 }
] }
```

"Reached" (cumulative) semantics over `CODCollection` rows in scope:

| Stage | Rows | Amount |
|---|---|---|
| expected | all rows | `ExpectedAmount` |
| collected | `CollectedAmount != null` | `CollectedAmount` |
| riderDeposited | status ∈ {RiderDeposited, BranchReconciled, FinanceVerified, MerchantSettlementCreated, Settled} | `CollectedAmount` |
| branchReconciled | status ∈ {BranchReconciled, FinanceVerified, MerchantSettlementCreated, Settled} | `CollectedAmount` |
| financeVerified | status ∈ {FinanceVerified, MerchantSettlementCreated, Settled} | `CollectedAmount` |
| settled | status = Settled | `CollectedAmount` |

`from`/`to` filter on `CODCollection.CreatedAt` (inclusive), normalised to UTC like the R2
shipment list filters; `from > to` → 400. Omitted range = all time. Aggregation is pushed to SQL
(one grouped query or per-stage `SumAsync`/`CountAsync`; never materialise rows). Counts and
amounts are monotonically non-increasing left to right by construction. The existing
`/cod/reconciliation` and `/cod/summary` endpoints are unchanged.

### 3.2 `GET /api/v1/cod/rider-cash?branchId=` (new)

Cash collected but not yet deposited, one row per `(CollectedByRiderId, BranchId)` over rows with
`Status == CodCollected`:

```json
{ "items": [
  { "riderId": "…", "branchId": "…", "parcelCount": 14, "amount": 38400,
    "oldestCollectedAt": "2026-10-06T05:10:00Z", "latestCollectedAt": "2026-10-06T11:07:00Z" }
] }
```

Ordered by `oldestCollectedAt` ascending. Not paginated (bounded by active riders per branch).
`amount` sums `CollectedAmount`. Rows with null `CollectedByRiderId` are excluded.

### 3.3 Rider settlements: date filter and variance fields

- `GET /api/v1/cod/rider-settlements` gains optional `from`/`to` on `SettledAt` (inclusive, UTC
  normalised, `from > to` → 400). Existing parameters and paging unchanged.
- `RiderCashSettlementResponse` gains two additive fields (all endpoints that return it):
  - `expectedAmount`: Σ `CollectedAmount` of `CODCollection` rows whose
    `RiderCashSettlementId == settlement.Id`.
  - `variance`: `DeclaredAmount − expectedAmount` (negative = short, positive = over).
- List responses load expected sums for the page in **one** grouped query keyed by settlement id
  (no N+1).
- `GET /api/v1/cod/collections` gains an optional `riderCashSettlementId` filter (narrows within
  existing tenant/merchant/branch scope), used by the settlement detail Sheet (§6.2).

## 4. Page shell (`app/(staff)/cod/page.tsx`)

- `PageHeader`: breadcrumb "Finance / COD", title "COD reconciliation", subtitle
  "<branch label> · <range label>".
- Right controls:
  - **Branch picker** (shadcn `Select`), shown only when the session has more than one branch or
    all-branches scope; default "All my branches". Value in URL `?branch=`.
  - **Range presets** Today / 7 days / 30 days (R2 `rangePresets`), default 7 days, URL `?range=`,
    sent to the API as UTC-anchored `YYYY-MM-DDT00:00:00Z` / `YYYY-MM-DDT23:59:59Z`.
  - "Download ledger" and "Close branch day" buttons rendered **disabled** with a
    "Needs backend" tooltip.
- **Money-flow strip** (§5) under the header.
- **Tabs** (shadcn `Tabs`), active tab in URL `?tab=` (`collections` | `rider-deposits` |
  `branch-settlements` | `merchant-settlements` | `adjustments` | `ledger`; invalid → default
  `rider-deposits`).
- Removed: the five-tile `SummaryStatsBar` and the Reconciliation tab (`ReconciliationTab`),
  superseded by the strip. `useCodReconciliation`/`useCodSummary` are deleted only if nothing
  else imports them (grep first).
- Existing create/detail Sheets and `codPanelStore` stay mounted at page level.

## 5. Money-flow strip (`features/cod/components/CodFlowStrip.tsx`)

- Six connected cards: step number (`01`…`06`), label, amount, parcel count.
- Labels: Expected · Collected · Rider deposited · Branch reconciled · Finance verified ·
  Settled to merchants.
- Amounts formatted `Rs 12,48,500` (Nepali grouping, no decimals unless non-zero paise); counts
  "482 parcels".
- **Highlight** (cobalt fill): the stage *before* the largest drop in amount between adjacent
  stages (where money is waiting to move on). Ties → leftmost. All drops 0 → no highlight. Pure
  function `pickBottleneck(stages)`.
- Cards are buttons that switch tab: collected → `rider-deposits`; riderDeposited and
  branchReconciled → `branch-settlements`; financeVerified → `merchant-settlements`; expected →
  `collections`; settled → `merchant-settlements`.
- Loading: skeleton strip. Error: `ErrorState` with retry. Never zeros.

## 6. Rider deposits tab (`features/cod/components/RiderDepositsTab.tsx`)

### 6.1 Holding cash (from §3.2)

`SectionCard` "Holding cash". Columns: Rider (initials + name via `usePeopleNames`; fallback
"Rider"), Branch (name via existing branch lookup), Parcels, Amount held, Held for
(`formatHeldFor(oldestCollectedAt, now)` → "3 h", "1 d 2 h"), Status badge, action.

- Status: "Holding" (neutral) or "Held over 24 h" (warning tone) when held ≥ 24 h; over-24 h rows
  sort first, then by `oldestCollectedAt`.
- Action "Record deposit" (requires `cod.reconcile`; hidden otherwise) opens the record-deposit
  Sheet pre-filled with that row.
- Empty state: "No rider is holding COD cash right now."

### 6.2 Recent deposits (from §3.3, filtered by page range + branch)

`SectionCard` "Recent deposits". Columns: Rider, Parcels, Expected, Deposited, Variance, Recorded
at, Branch state ("Reconciled" when `branchCashSettlementId` set, else "Awaiting branch").

- Variance wording via pure `describeVariance(variance)`: 0 → "Matched" (teal); < 0 →
  "Short − Rs 500" (destructive); > 0 → "Over + Rs 200" (amber). Text always present.
- Row click opens the existing `RiderSettlementDetailSheet`, which adds Expected, Variance and
  the settlement's parcels via `GET /cod/collections?riderCashSettlementId=` (§3.3).
- Paginated with the existing shadcn pagination pattern.

### 6.3 Record deposit Sheet (redesigned `RiderSettlementCreateSheet`)

- `SheetHeaderBlock`: rider name, branch, "N COD parcels · last collected <time>".
- Rows: "Expected (held)" from the rider-cash row; input "Cash received" (React Hook Form + Zod,
  > 0, max 2 decimals).
- Live preview "Variance" with `describeVariance(received − expected)`; labelled as a preview.
- Non-zero variance shows an info note: "Shortage/overage reasons and approval need backend
  support. The deposit and its variance will be recorded."
- `ComingSoonSlot`s: "Note denominations", "Fonepay / QR split".
- Submit → existing `useCreateRiderSettlement` (`riderId`, `branchId`, `declaredAmount`). Success:
  close Sheet; invalidate `["cod"]` prefix and the flow/rider-cash keys; server variance appears
  in Recent deposits. Error: inline `codErrorMessage` in the Sheet; Sheet stays open.
- Opened without a pre-filled row (if any existing entry point remains) it falls back to the
  current rider/branch selection behaviour.

## 7. Other tabs

### 7.1 Merchant settlement detail (`MerchantSettlementDetailSheet` redesign)

- Header: merchant name, settlement id, period, status badge.
- Breakdown (pure `buildSettlementBreakdown(settlement, adjustments)`):
  - "N delivered COD parcels" = Σ `codAmount`
  - "Delivery charges (booking-time prices)" = − Σ `courierFee`
  - "Other deductions" = − Σ `otherDeductions` (already includes approved adjustments — applying
    an adjustment rewrites the item's `otherDeductions`, `netPayable` and the settlement's
    `totalPayable` server-side, so adjustments are never a separate subtracted line)
  - Caption under it, only when non-zero: "Includes Rs N from approved adjustments", N = Σ
    (`afterValue − beforeValue`) of applied adjustments whose `entityType ==
    "MerchantSettlementItem"` and `entityId` ∈ this settlement's item ids
  - **Merchant payable** = server `totalPayable`.
  - If Σ`netPayable` of items ≠ `totalPayable`, show a warning row "Lines don't add up to the
    payable — contact finance" with both figures. Never silently reconcile.
- Items table: AWB, COD, Fee, Deductions, Net. Selecting a row shows "Trace one line"
  (cobalt-tint card): COD collected, − courier fee, − other deductions, = payable.
- Actions unchanged: "Mark settled" (`cod.settle`), "Request adjustment" per item (`cod.adjust`).

### 7.2 Collections, Branch settlements, Adjustments

Restyled to R1/R2 table/badge/empty-state patterns; behaviour unchanged; user/rider ids rendered
as names via `usePeopleNames` where available.

### 7.3 Ledger

`ComingSoonSlot` "Ledger" — "Every COD money movement with before/after values and export."

## 8. Dashboard hookups (R2 components)

- `CodRangeCard`: the "Rider-held vs deposited" `ComingSoonSlot` becomes a two-value split:
  **Rider-held** = Σ `amount` from `/cod/rider-cash`; **Deposited, not yet verified** = flow
  `riderDeposited.amount − financeVerified.amount` for the dashboard range. Loading → skeleton;
  error → "—" with retry.
- `NeedsActionCard`: new row "Riders holding cash > 24 h" (count of rider-cash rows held ≥ 24 h),
  linking to `/cod?tab=rider-deposits`. Hidden on error (consistent with the other rows).
- Both queries `enabled` only with `cod.read`.

## 9. Testing

**Backend (`tests/Cod.Tests` / `tests/Api.Tests`):**
- Flow: reached semantics per stage; monotonic non-increasing; `from`/`to` on `CreatedAt`, UTC
  offset input, `from > to` → 400; branch scope narrows; other tenant excluded; merchant → 403.
- Rider-cash: groups by rider + branch; excludes deposited and null-rider rows; ordering;
  branch scope; merchant → 403.
- Rider settlements: `expectedAmount`/`variance` for exact, short, over; batched (one extra
  query for a page); `from`/`to` filter; existing tests unchanged.
- Collections: `riderCashSettlementId` filter narrows, cannot escape branch/merchant scope.

**Web (vitest):**
- Pure: `pickBottleneck` (ties, flat), `describeVariance`, `formatHeldFor`,
  `buildSettlementBreakdown` (adjustments, mismatch), range → UTC strings, stage → tab mapping.
- Components: tab ↔ URL; branch/range in URL; strip loading/error (no zeros); holding sort and
  over-24 h badge; Record deposit hidden without `cod.reconcile`; live variance preview; submit
  success/error; Mark settled / Request adjustment hidden without `cod.settle` / `cod.adjust`;
  dashboard rider-held split and needs-action row; disabled Needs-backend buttons.
- Never assert on ICU month text; compute expected strings with the same formatter.

## 10. Backend follow-ups surfaced by R3a

1. Shortage/overage reason + approval on rider deposits (adjustments for `RiderCashSettlement`,
   threshold-based Finance Manager approval).
2. Cash denomination breakdown on deposits.
3. Digital (Fonepay/QR) vs cash split on deposits.
4. Close branch day (lock + day report).
5. COD ledger read + CSV export.
6. Link COD collections to delivery runs (run id on deposits).
7. Separate COD handling-fee line on merchant settlement items.

## 11. Documentation

- `docs/API-SPEC.md`: `/cod/flow`, `/cod/rider-cash`, rider-settlement `from`/`to`,
  `expectedAmount`, `variance`.
- `docs/FRONTEND-ROADMAP.md`: split R3 row into R3a (Done when merged, this spec) and R3b
  (Planned); add §10 as "Backend follow-ups from R3a".
- `DESIGN.md`: money-flow strip (cumulative stages, single bottleneck highlight) and variance
  wording rule (text always, colour secondary).
- `docs/CLARIFICATIONS.md`: new entry "COD flow stages are cumulative ('reached'), keyed on
  collection creation date" explaining why it differs from `/cod/reconciliation`'s
  outstanding-only `expectedCod`.

## 12. Delivery

One branch/worktree, executed via the standard SDD pipeline (plan → subagent-driven
development → final review → merge). Backend tasks first (§3), then strip and shell, rider
deposits, merchant settlement detail, other tabs, dashboard hookups, docs.
