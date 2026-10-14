# F15 Console Redesign — R3b-1: Parcel Movement (Manifests, NDR, Returns) — Design Spec

## 1. Context

R3b ("Staff network", `docs/FRONTEND-ROADMAP.md` Phase F15) was split during brainstorming into
**R3b-1 — Parcel movement** (this spec: Manifests, NDR queue, Returns) and **R3b-2 — People &
places** (Riders + vehicles, Live map, Branches; its own spec later).

The design canvas (claude.ai artifact https://claude.ai/artifact/1dGwukUsc2jwxhThg5eyHu) had no
artboards for these pages. Three were drawn and approved for this spec, in the row "Network:
parcel movement":

| Artboard | Page |
|---|---|
| `project/Manifests.dc.html` — Manifests + receive reconciliation | `app/(staff)/manifests` |
| `project/Ndr.dc.html` — NDR queue + follow-up sheet | `app/(staff)/ndrs` |
| `project/Rto.dc.html` — Returns to origin + return chain | `app/(staff)/return-shipments` |

The artboards use sample data and draw a few things the API does not serve; §2 and §9 say exactly
which. They use the codebase type scale (DESIGN.md On-Scale Rule).

Pages are rebuilt with the R1 page primitives (`PageHeader`, `SectionCard`, `PipelineStrip`,
`ComingSoonSlot`, `SheetHeaderBlock`), R2 patterns (URL state, `usePeopleNames`, row highlight via
`selectedRowId`) and R3a patterns (`useFilterPage`, branch picker rules).

### Scope boundary (approved — same as R2 and R3a)

A **small, read-only backend slice** (§3) is in scope: new read endpoints, new query filters and
additive response fields computed from data that already exists, plus one branch-scope security
fix. **No migrations, no new entities, no new write endpoints, no change to existing write
behaviour.** Anything the artboards show that needs new writes or stored data renders a
`ComingSoonSlot` / disabled "Needs backend" control or is omitted, and is listed in §9 — never mock
values (DESIGN.md No-Fake-Numbers Rule).

## 2. Artboard elements vs data source

| Artboard element | Source in R3b-1 |
|---|---|
| Manifest lifecycle strip counts | Existing `GET /manifests/summary` |
| Incoming / Outgoing tabs | New `direction` filter (§3.1.2) |
| "118 / 120" parcels per row | New `receivedCount` (§3.1.3) + existing `shipmentCount` |
| Stepper stage times (sealed 9:12 PM …) | Not stored — status-only stepper, "Needs backend" note |
| "Dispatched" stage | Does not exist: dispatch moves Sealed → InTransit; strip has 5 stages |
| Missing parcels + last scan | Existing `/missing-shipments` (batched, §3.1.5) |
| Parcel list in manifest Sheet | New `GET /manifests/{id}/shipments` (§3.1.4) |
| NDR status strip | Existing `GET /ndrs/summary` |
| "Assigned to me / Unassigned" | New `assigned` filter (§3.3) |
| Reason filter | New `reason` filter (§3.3) |
| Recipient / merchant / COD on NDR rows | Not served on the list — omitted from rows; shown in the Sheet from existing `GET /shipments/{awb}` |
| Attempt history in NDR Sheet | New `GET /shipments/{shipmentId}/delivery-attempts` (§3.2) |
| "Attempt 2 **of 3**" | No attempt limit exists — shows "Attempt 2" / "2 attempts" only |
| Follow-up form, Reattempt / RTO / Resolve | Existing `POST /ndrs/{id}/update` and `/resolve` |
| "Reattempt on ‹date›" | `resolve` has no date — follow-up date is the scheduling note; dated reattempt "Needs backend" |
| Assign an owner | Write — disabled "Needs backend" |
| Returns stage strip | Existing `GET /return-shipments/summary` |
| Return ↔ original AWB, reason, route, now at, status, confirmed | Existing `ReturnShipmentResponse` |
| Merchant column, "In return 3 d" | No `MerchantId`/`CreatedAt` on ReturnShipment — omitted |
| "What happened" return chain | Existing staff `GET /shipments/{awb}/events` for the original AWB |
| Hand back: received by / condition | `confirm` takes no body — `ComingSoonSlot` |

## 3. Backend slice (read-only)

Conventions everywhere: tenant isolation stays on the existing global query filters; branch scope
through `IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, "<permission>")` (null =
all branches); filters only narrow within scope; validation errors use each module's existing 400
contract; staff-only routes return **403** to merchant callers.

### 3.1 Manifests module

1. **Branch-scoped single reads (security fix).** `GET /api/v1/manifests/{id}` and
   `GET /api/v1/manifests/{id}/missing-shipments` take the acting user and return **404** unless the
   manifest's `FromBranchId` or `ToBranchId` is in the caller's `manifest.read` scope — the same
   rule as the list and as R2's shipment single read. `ManifestService.GetByIdAsync` and
   `GetMissingShipmentsAsync` gain a `ClaimsPrincipal actingUser` parameter; every caller is
   updated.
2. **`direction` filter on `GET /api/v1/manifests`** — `incoming` | `outgoing`, optional:
   - `incoming`: `ToBranchId == branchId` when `branchId` is given, else `ToBranchId` in the
     caller's scope (no restriction under all-branches scope).
   - `outgoing`: the same on `FromBranchId`.
   - Omitted: today's behaviour (touches the branch either way).
   - Invalid value → 400 `MANIFEST_VALIDATION_FAILED` (the list route today returns `Results.Ok`
     directly; it is routed through the module's validation handling so this is a 400, not a 500).
   The response stays a plain array; ordering unchanged (`CreatedAt` descending).
3. **`receivedCount` on `ManifestResponse`** (all endpoints that return it): count of
   `ManifestShipment` rows with `ReceivedAt != null`. List responses compute it for every listed
   manifest in **one** grouped query keyed by manifest id (no N+1); single responses use one
   `CountAsync`.
4. **New `GET /api/v1/manifests/{id}/shipments`** (`manifest.read`, scope as §3.1.1): every parcel
   on the manifest as `{ shipmentId, awb, scannedAt, receivedAt }` (the existing
   `ManifestShipmentResponse` minus its row id is acceptable — reuse the record), ordered by
   `scannedAt` ascending. Not paginated (bounded by one vehicle load). AWBs via one batched lookup.
5. **Batch the missing-shipments AWB lookup.** `GetMissingShipmentsAsync` replaces its per-row
   `GetScanInfoAsync` loop with one call to the existing
   `Shipments.Application.IShipmentReportingLookupService.GetAwbsByShipmentIdsAsync` (Manifests
   already references Shipments). §3.1.4 uses the same lookup. Response shape unchanged.

### 3.2 Riders module — delivery attempts read

Ndr cannot reference Riders (Riders already references Ndr), so the read lives with the entity.

**New `GET /api/v1/shipments/{shipmentId}/delivery-attempts`** — staff only (`ndr.read`; merchant
callers 403). Returns **404** unless the shipment exists in the tenant and its current, origin or
destination branch is in the caller's `ndr.read` scope (via `IShipmentTransitionService.GetScanInfoAsync`,
the same rule as the shipment single read). Response:

```json
{ "items": [
  { "attemptNumber": 2, "attemptedAt": "2026-09-30T04:20:00Z", "succeeded": false,
    "reason": "CustomerUnavailable", "notes": "Gate locked…", "customerContacted": true,
    "nextAction": "Reattempt", "riderId": "…" }
] }
```

Newest first (`attemptedAt` descending, then `attemptNumber` descending). Not paginated. GPS is not
exposed (not needed on screen; keeps the payload minimal).

### 3.3 Ndr module — list filters

`GET /api/v1/ndrs` gains two optional filters, narrowing within the existing scope and keeping the
existing paging:

- `assigned`: `me` → `AssignedStaffUserId == caller`; `unassigned` → `AssignedStaffUserId == null`.
  Invalid value → 400 `NDR_VALIDATION_FAILED`.
- `reason`: an `NdrReason` value (model-bound enum; invalid → 400 by the framework).

Both are applied in SQL before the existing paging/scan-info step.

## 4. Shared web pieces

- **`PipelineStrip`** (R1) drives every status strip; each page supplies a pure highlight rule:
  `pickManifestHighlight` (stage with the most open manifests, Closed excluded, ties leftmost, none
  when all 0), NDR: `Open` when > 0, Returns: `At origin` when > 0. Strips are buttons that set the
  page's status filter.
- **`formatAge(iso, now)`** → "< 1 h", "7 h", "1 d 20 h", plus `AGE_WARNING_HOURS = 24`. R3a's
  `formatHeldFor`/`hoursHeld` move from `features/cod/lib/codFormat.ts` to `lib/dates/age.ts` under
  these names and COD imports them from there (no duplicate helper).
- **`NdrRtoSwitch`** — segmented two-link switch ("NDR queue", "Returns") in the header of both
  pages, tab semantics (`role="tablist"`/`aria-selected`), each link shown only with its read
  permission (`ndr.read` / `rto.read`); hidden entirely when only one side is permitted.
- **Route codes** — `FROM → TO` uses branch codes from the existing branches lookup, mono; falls back
  to the branch name when a code is missing.
- All new UI uses shadcn/ui components (Select, Tabs, Checkbox, DatePicker, AlertDialog, Pagination,
  Skeleton, Spinner) and the on-scale type steps.

## 5. Manifests page (`app/(staff)/manifests/page.tsx`)

### 5.1 Shell

- `PageHeader`: breadcrumb "Operations / Manifests", title "Manifests", subtitle
  "‹branch label› · ‹N› open".
- **Branch picker** with the `/cod` rules: shown only with more than one branch or all-branches
  scope; default "All my branches"; URL `?branch=`.
- Actions: "Receive at scan station" (outline link to `/scan`, `manifest.receive`), "New manifest"
  (primary, `manifest.create`, opens the existing create Sheet).
- **Lifecycle strip** — five cards from `/manifests/summary`: Draft · Sealed · In transit ·
  Receiving · Closed (labels follow the backend: `Received` means receiving has started).
  The summary is not branch-filtered; the strip's caption says "Across your branches".
  Card click sets `?status=`.
- **Tabs** (`?tab=`): `incoming` (default) · `outgoing` · `all` → `direction=incoming|outgoing|∅`.
  A status filter chip ("Status: Receiving ×") shows when `?status=` is set.

### 5.2 Table

`DataTable` over the array, paged client-side (20 rows) with `useFilterPage` keyed on
tab/status/branch.

| Column | Content |
|---|---|
| Manifest | Mono code; purpose ("Forward" / "RTO") beneath |
| Route | `FROM → TO` codes (§4) |
| Vehicle · driver | Plate or "Not assigned"; driver name via `usePeopleNames`, else "—" |
| Parcels | Receiving/Closed: "‹received› / ‹total›" and, when received < total, "‹N› missing" in destructive text; otherwise "‹total› parcels" |
| Status | `StatusBadge` (namespace `Manifest`) |
| Created | Date + time |

Row click opens the Sheet; the open row is highlighted. Empty: "No manifests here" with a tab-aware
description. Error: `ErrorState` with retry.

### 5.3 Detail Sheet (existing `ManifestDetailSheet`, restyled)

- `SheetHeaderBlock`: eyebrow "MANIFEST · INCOMING|OUTGOING · FORWARD|RTO" (direction relative to the
  page branch; omitted on the All tab or with no branch), mono code, status badge; meta row From,
  To, Vehicle, Driver.
- **Stepper**: Draft → Sealed → In transit → Receiving → Closed, status-derived only; complete /
  current / upcoming by icon and text (never colour alone). A muted line "Stage times need backend
  support" (`data-coming-soon`).
- **Body by status** (container stable, content state-driven, as today):
  - Draft: existing scan + seal section.
  - Sealed: existing dispatch form.
  - InTransit / Received: **Receive reconciliation** `SectionCard` — "‹received› of ‹total›
    verified" with a progress bar (value also as text), the missing list from `/missing-shipments`
    (mono AWB, "Sealed into this manifest ‹time›", "Open parcel" → shipment Sheet via its store),
    then **Parcels in this manifest** from §3.1.4 (AWB, out time, in time or "Not yet"). The existing
    receive-scan input stays; a link "Open scan station" goes to `/scan`.
  - Closed: the same reconciliation, read-only; missing list titled "Closed with ‹N› missing".
- **Footer**: the status's primary action behind its existing permission — Seal
  (`manifest.seal`), Dispatch (`manifest.dispatch`), Close (`manifest.close`). Close reads
  "Close with ‹N› missing" when parcels are missing and confirms via `AlertDialog` ("‹N› parcels
  stay listed as missing"); otherwise "Close manifest". Hidden when not permitted.
- Loading: `DetailSkeleton`; each sub-read (missing, parcels) has its own loading/error/empty state.

## 6. NDR queue (`app/(staff)/ndrs/page.tsx`)

### 6.1 Shell

- `PageHeader`: breadcrumb "Operations / NDR & RTO", title "Failed deliveries", `NdrRtoSwitch` on
  the right.
- **Status strip** — six cards from `/ndrs/summary`: Open · Contacting · Customer confirmed ·
  Reattempt scheduled · RTO requested · Resolved; Open highlighted when > 0; click sets `?status=`.
- **Quick filters** (URL state): toggle chips "Assigned to me" (`?assigned=me`) and "Unassigned"
  (`?assigned=unassigned`), mutually exclusive; Reason `Select` (`?reason=`, human labels).
  Paging resets via `useFilterPage`.

### 6.2 Table (server-paged, 20)

| Column | Content |
|---|---|
| AWB | Mono |
| Reason | Human label (`ndrReasonLabel`) |
| Age | `formatAge(createdAt)`; ≥ 24 h destructive + semibold (text carries meaning) |
| Owner | "You" / name via `usePeopleNames` / "Unassigned" |
| Status | `StatusBadge` (namespace `Ndr`); beneath, "Follow up ‹time›" when `followUpAt` is set, destructive when overdue |

### 6.3 Sheet (existing `NdrDetailSheet`, restyled)

- Header: eyebrow "NDR · OPENED ‹date time› · ‹age›", mono AWB, status badge. Meta row Recipient,
  Merchant, COD from existing `GET /shipments/{awb}` ("—" + inline retry on failure); Attempts =
  number of attempts from §3.2 ("—" while loading).
- **Attempts** `SectionCard` from §3.2: attempt number, reason label, time, rider name
  (`usePeopleNames`), notes. Loading skeleton; error with retry; empty "No delivery attempts
  recorded".
- **Follow-up** card (`ndr.manage`; read-only fields without it): Customer response (textarea),
  Status (`Select` of non-resolved statuses), Next action note, Follow up again (`DatePicker` +
  time) → `POST /ndrs/{id}/update`. React Hook Form + Zod. "Owner: you" shows once the caller owns
  it; an "Assign to…" control is disabled with a "Needs backend" tooltip.
- **Resolve** (`ndr.resolve`): three radio cards — Reattempt; Return to origin (requires an RTO
  reason, textarea); Resolve other — submitted via `POST /ndrs/{id}/resolve`. A note under
  Reattempt: "The follow-up date is the scheduling note; booking a dated reattempt needs backend
  support."
- **Footer**: "Save follow-up" (outline) and a primary button worded by choice — "Schedule
  reattempt" / "Start return" / "Resolve". RTO and Other confirm via `AlertDialog`.
- Resolved NDRs show the existing `NdrResolvedSummary` instead of both forms.
- Success: close nothing; toast; invalidate `["ndrs"]` (list, single, summary).

## 7. Returns (`app/(staff)/return-shipments/page.tsx`)

### 7.1 Shell

- `PageHeader`: breadcrumb "Operations / NDR & RTO / Returns", title "Returns to origin",
  `NdrRtoSwitch`.
- **Stage strip** — five cards from `/return-shipments/summary`: RTO initiated · In transit back ·
  At origin · Delivered to merchant (from `byRtoStage`) · Pending confirmation
  (`pendingConfirmation`). At origin highlighted when > 0. Click filters the table client-side
  (`?stage=`).

### 7.2 Table (array, client-paged 20)

| Column | Content |
|---|---|
| Return AWB | Mono; "from ‹original AWB›" beneath (mono, muted) |
| Reason | `rtoReason` |
| Route | `FROM → TO` codes (origin → destination) |
| Now at | Current branch name, or "In transit" when null |
| Status | `StatusBadge`; beneath "Merchant confirmed ‹date›" or "Not confirmed" |

### 7.3 Sheet (existing `ReturnShipmentDetailSheet`, restyled)

- Header: eyebrow "RETURN · ‹reason›", mono return AWB, status badge; meta row Back to, Now at,
  Merchant (from the original shipment read; "—" on failure).
- **Linked cards**: Original (opens the shipment Sheet) → Return.
- **What happened**: existing staff `GET /shipments/{awb}/events` for the **original** AWB, newest
  first, using the R2 timeline row component. (The return lifecycle runs on the original shipment's
  status — `ReturnShipmentResponse.CurrentStatus` is the original shipment's status.)
- **Hand back to merchant** (`rto.manage`; shown only when status is `RtoDelivered` and not yet
  confirmed): explanation text and "Confirm handover" → existing `POST /return-shipments/{id}/confirm`
  after an `AlertDialog`. "Received by" and "Parcel condition" render as a `ComingSoonSlot`.
- Confirmed returns show "Confirmed by merchant ‹date›".

## 8. Tests

**Backend**
- Manifests.Tests: `direction` incoming/outgoing with and without `branchId`, invalid → 400;
  `receivedCount` on list and single (grouped, no N+1 — assert one query via counting stub or by
  behaviour); `/shipments` read contents + order; out-of-scope single, missing and shipments reads
  → not found; batched missing lookup returns the same AWBs.
- Riders.Tests: attempts ordering, scope (out of scope → not found, unknown shipment → not found).
- Ndr.Tests: `assigned=me|unassigned`, `reason`, combined with `status`; invalid `assigned` → 400.
- Api.Tests: for each new/changed read — 401 unauthenticated, 403 missing permission, 403 merchant
  caller (attempts), 404 out of branch scope (manifest single, missing, shipments, attempts), filter
  round-trips.

**Web (vitest)**
- Helpers: `pickManifestHighlight`, `formatAge` (moved COD tests keep passing).
- Manifests page: strip from summary + highlight; tab/status/branch in the URL; parcels column
  wording; skeleton and error never render 0.
- Manifest Sheet: reconciliation counts, missing list, parcel list states, "Close with N missing"
  confirm, permission-gated footer.
- NDR page: strip, quick filters → hook params, age warning text, owner labels, follow-up overdue.
- NDR Sheet: attempts states, follow-up submit payload, resolve wording per choice, RTO reason
  required, resolved summary.
- Returns page + Sheet: stage filter, "In transit" fallback, hand-back visibility rule, confirm
  dialog, coming-soon handover fields.
- `NdrRtoSwitch`: permission gating.

## 9. Exit criteria and follow-ups

### Exit criteria

1. The three pages and Sheets match §5–§7; every number is server data; loading/error never renders
   as 0.
2. Branch-scope fix in place: out-of-scope manifest and attempts reads are 404.
3. `dotnet build` and `dotnet test` (Manifests, Riders, Ndr, Api) pass; web `npm run build`,
   `npm run lint`, `npm test` pass; `npx tsc --noEmit` reports **0 errors**.
4. Verified live in Chrome against a seeded demo org.
5. `docs/API-SPEC.md`, `docs/FRONTEND-ROADMAP.md` (R3b-1 row + follow-ups), `DESIGN.md` (if a new
   pattern lands) updated.

### Non-goals

R3b-2 (Riders + vehicles, Live map, Branches); merchant-portal views; any new write behaviour;
i18n of page copy beyond existing English.

### Backend follow-ups (log in FRONTEND-ROADMAP F15)

1. Manifest stage timestamps (sealed / dispatched / received / closed at) for the stepper.
2. Delivery attempt limit and "attempt N of M".
3. Assign / reassign an NDR owner.
4. Dated reattempt on NDR resolve.
5. Recipient / merchant / COD on NDR list rows.
6. `MerchantId` and `CreatedAt` on ReturnShipment (Merchant column, "in return" age), and batch the
   per-row scan-info lookup in the returns list.
7. Handover details on return confirmation (received by, condition).
8. Branch-filterable manifest summary and a paged manifest list.
