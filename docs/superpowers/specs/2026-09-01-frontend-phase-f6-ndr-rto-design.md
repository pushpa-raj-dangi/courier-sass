# Frontend Phase F6 — NDR + RTO — Design

**Date:** 2026-09-01
**Status:** Approved, ready for implementation planning.
**Roadmap entry:** `docs/FRONTEND-ROADMAP.md` Phase F6.

## Context

F0–F5 are complete (design system/shell, auth + org dashboard, branches, shipments,
manifests, riders/delivery/POD). Phase 9 (backend) shipped a full `Ndr` module —
`NDR`, `ReturnShipment`, the NDR status workflow (`CLAUDE.md` §20), and the
NDR-gateway-only path into RTO (`CLARIFICATIONS.md` #4) — but `src/web` has no UI
for any of it. A shipment that fails delivery today reaches `NdrCreated` and then
has no operable path forward through the UI at all. This phase builds that UI, per
`docs/FRONTEND-ROADMAP.md`'s F6 entry: NDR queue/workflow UI (status transitions
per CLAUDE.md §20) and RTO tracking linked to the original shipment (CLAUDE.md
§21).

Investigating the existing backend surface (`src/Modules/Ndr/`,
`src/Modules/Shipments/Application/IShipmentTransitionService.cs`) before design
turned up five real gaps, all resolved with the user below and folded into this
spec rather than deferred.

## Resolved backend gaps (confirmed with user during brainstorming)

1. **`NdrStatus`, `NdrReason`, `NdrResolution` have no `JsonStringEnumConverter`.**
   Same latent bug class hit and fixed narrowly, enum-by-enum, in F2
   (`BranchType`), F3 (`ShipmentStatus`), F4 (`ManifestPurpose`/`ManifestStatus`),
   and finally all-at-once in F5 (5 Riders enums). Fixed proactively here for all 3
   Ndr enums, same as F5's approach, rather than rediscovering them one at a time
   during E2E verification.
2. **`NdrResponse` carries no shipment AWB** — only the opaque `ShipmentId`,
   useless for a staff-facing queue. `NdrService.UpdateAsync`/`ResolveAsync`
   already call `IShipmentTransitionService.GetScanInfoAsync` (which has `Awb`) for
   their own branch-access check; `GetByIdAsync` and the all-branches path of
   `ListAsync` do not yet fetch it.
3. **`ReturnShipmentResponse` carries no *original* shipment AWB** — only
   `OriginalShipmentId`. `ToReturnShipmentResponseAsync` already fetches `scanInfo`
   for `CurrentStatus`; the same call has `Awb`.
4. **No way to find "the NDR(s) for shipment X".** `GET /api/v1/ndrs` filters only
   by `status`. A shipment can accumulate more than one NDR over its lifetime (at
   most one unresolved at a time — `NdrConfiguration.cs`'s documented invariant),
   so this needs to return a list, not assume a single row.
5. **No way to find "the return shipment for original shipment X".** Same shape of
   gap on `GET /api/v1/return-shipments`. At most one `ReturnShipment` per original
   shipment (RTO is a terminal path — nothing in Phase 9 ever reopens a shipment
   for redelivery after `RtoInitiated`), so the frontend can safely take the first
   result.

Gaps 4 and 5 exist specifically so the shipment detail Sheet can show "this
shipment has an open NDR" / "this shipment was returned as RTO-XXX" — the exit
criteria's "linkage back to its original AWB is visible on both records"
requirement, which needs a query path in both directions (return shipment →
original AWB, and original shipment → its return AWB).

## Backend changes

### 1. Enum serialization fix (all 3 at once)

Add `[JsonConverter(typeof(JsonStringEnumConverter))]` directly to `NdrStatus`,
`NdrReason`, `NdrResolution` — same narrow, per-enum fix used in F2/F3/F4/F5.

### 2. `NdrResponse.Awb`

```csharp
public record NdrResponse(
    Guid Id, Guid ShipmentId, string Awb, NdrReason Reason, Guid? AssignedStaffUserId, NdrStatus Status,
    string? CustomerResponse, DateTimeOffset? FollowUpAt, string? NextAction, NdrResolution? Resolution, DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt);
```

`ToResponse` becomes `ToResponseAsync`, taking the already-resolved
`ShipmentScanInfo` where the caller has one (`UpdateAsync`/`ResolveAsync`) and
fetching it directly where it doesn't yet (`GetByIdAsync`, `ListAsync`'s two
paths) — `GetScanInfoAsync` is already the established per-row cost `ListAsync`'s
branch-restricted path pays; the all-branches path now pays it too, for every row
on the page (bounded by page size, same as every other paginated list in this
codebase).

### 3. `ReturnShipmentResponse.OriginalAwb`

```csharp
public record ReturnShipmentResponse(
    Guid Id, Guid OriginalShipmentId, string OriginalAwb, string ReturnAwb, string RtoReason,
    Guid OriginBranchId, Guid DestinationBranchId,
    ShipmentStatus CurrentStatus, Guid? CurrentBranchId, DateTimeOffset? MerchantConfirmedAt);
```

`ToReturnShipmentResponseAsync` already has `scanInfo` in hand — just add
`scanInfo?.Awb ?? string.Empty` (mirrors the existing `CurrentStatus` fallback's
style; a null `scanInfo` here would mean the original shipment was deleted, which
never happens in this codebase — no soft/hard delete exists for `Shipment`).

### 4. `GET /api/v1/ndrs?shipmentId=`

New optional query parameter alongside the existing `status`/`page`/`pageSize`.
Filters `_dbContext.Set<NDR>()` by `ShipmentId` before the existing status filter
and branch-scoping logic — composes with both, doesn't replace them. Returns the
existing paginated shape (in practice a page of ≤ a handful of rows per shipment).

### 5. `GET /api/v1/return-shipments?originalShipmentId=`

New optional query parameter on the existing (non-paginated) list endpoint.
Filters `_dbContext.Set<ReturnShipment>()` by `OriginalShipmentId` before the
existing branch-scoping filter.

### Not in scope this phase

- No change to `IShipmentTransitionService` or its `ShipmentScanInfo` record —
  gaps 2/3 reuse the existing `Awb` field already on it.
- No new permission catalog entries — `ndr.read`, `ndr.manage`, `ndr.resolve`,
  `rto.read`, `rto.manage` already exist and are exactly the five actions this
  phase's UI needs.
- No change to `NdrService.ListAsync`'s documented pagination limitation for
  branch-restricted callers (in-memory pagination after per-row filtering) — a
  pre-existing, already-accepted tradeoff, not something this phase's `shipmentId`
  filter makes worse (a shipment-scoped query returns at most a few rows either
  way).

## Frontend structure

New `src/web/src/features/ndr/` feature folder, following the F2–F5 established
conventions (Sheet-over-pages via a Zustand panel store, `DataTable` for lists,
TanStack Query hooks per endpoint, Zod schemas for forms where user input exists).

### NDR queue

- **`/ndrs` list** (sidebar entry gated on `ndr.read`, icon `AlertTriangle`):
  `DataTable` — AWB, Reason (raw enum string, matching `RecordAttemptSheet`'s
  existing precedent of not building a label map), `StatusBadge status={ndr.status}
  namespace="Ndr"`, Created At, Assigned Staff. A status filter dropdown drives the
  `status` query param. Row click opens the detail Sheet.
- **`ndrPanelStore`** — `{ mode: "closed" } | { mode: "detail"; id: string }`, same
  shape as `manifestPanelStore` (no create mode — NDRs are only ever
  system-created via `NdrCreationService`, never through this UI).
- **`NdrDetailSheet`** — header: AWB, `StatusBadge`, Reason, Assigned Staff,
  Created At. Body swaps by status, mirroring `ManifestDetailSheet`'s
  one-section-per-status pattern:
  - **`Open` / `Contacting` / `CustomerConfirmed`** (`NdrUpdateSection`):
    `CustomerResponse`/`NextAction`/`FollowUpAt` fields plus a forward-move
    control limited to the legal next status per
    `NdrService.IsLegalForwardMove` — `Open` shows one "Move to Contacting"
    button; `Contacting` shows one "Move to Customer Confirmed" button;
    `CustomerConfirmed` shows two buttons ("Schedule Reattempt" →
    `ReattemptScheduled`, "Request RTO" → `RtoRequested`), since that's the one
    real branch point in the state machine. Submits via `POST /{id}/update`.
  - **`ReattemptScheduled`** (`NdrReattemptSection`): one "Resolve — Reattempt
    Delivery" action → `POST /{id}/resolve` with `Resolution: "Reattempt"`.
  - **`RtoRequested`** (`NdrRtoSection`): a required `RtoReason` text field, then
    "Resolve — Initiate RTO" → `POST /{id}/resolve` with `Resolution: "Rto",
    RtoReason`.
  - **Any non-`Resolved` status**: a secondary, always-available "Resolve — Other"
    action (short reason note, no `RtoReason`) → `Resolution: "Other"` — matches
    the backend's real catch-all, legal from any non-`Resolved` state. Rendered as
    a de-emphasized action below the status-specific section, not competing with
    it visually.
  - **`Resolved`**: read-only summary (Resolution, ResolvedAt). If `Resolution` is
    `Rto`/`AutoRto`, fetches `GET /api/v1/return-shipments?originalShipmentId=
    {ndr.shipmentId}` and, if found, shows a "View Return Shipment" button opening
    `ReturnShipmentDetailSheet`.
- Hooks: `useNdrs(status?, page?)`, `useNdr(id)`, `useUpdateNdr(id)`,
  `useResolveNdr(id)`. Every mutation invalidates the broad `["ndrs"]` key (not
  just `["ndrs", id]`) — F4's manifest-list staleness bug is exactly the mistake
  to not repeat here.

### RTO tracking

- **`/return-shipments` list** (sidebar entry gated on `rto.read`, icon `Undo2`):
  `DataTable` — Return AWB, Original AWB, live `StatusBadge status=
  {returnShipment.currentStatus}` (plain `ShipmentStatus` namespace — `RtoInitiated`
  /`RtoInTransit`/`RtoAtOrigin`/`RtoDelivered` are already in `STATUS_CONFIG`, no
  new entries needed here), Origin Branch, Destination Branch, Merchant Confirmed
  At (or "—"). Row click opens the detail Sheet. No create mode (return shipments
  are only ever system-created via `NdrService.ResolveAsync`).
- **`returnShipmentPanelStore`** + **`ReturnShipmentDetailSheet`** — full record
  plus a **Confirm Return** button gated on `rto.manage`, enabled only when
  `currentStatus === "RtoAtOrigin"` — disabled elsewhere with an inline caption
  ("Return must reach the origin branch before it can be confirmed, currently
  {status}"), mirroring the backend's own state guard in
  `ConfirmReturnShipmentAsync` rather than trusting the button to only ever be
  clicked when valid. Submit → `POST /{id}/confirm`, invalidates `["return-shipments"]`.
- Hooks: `useReturnShipments()`, `useReturnShipment(id)`,
  `useConfirmReturnShipment(id)`.

### Shipment detail linkage

`ShipmentDetailSheet.tsx` gets one addition: when `shipment.currentStatus` is
`"NdrCreated"` or starts with `"Rto"`, a new small section fetches:

- `GET /api/v1/ndrs?shipmentId={shipment.id}` — if any row's `status !==
  "Resolved"`, or as a fallback the most recent row, shows a "View NDR" button
  opening `NdrDetailSheet` for that id.
- `GET /api/v1/return-shipments?originalShipmentId={shipment.id}` — if a row
  exists, shows a "View Return Shipment" button opening
  `ReturnShipmentDetailSheet`.

This is the other half of "linkage... visible on both records" — the return
shipment side already carries `OriginalAwb` directly in its own response (gap 3
above), so `ReturnShipmentDetailSheet` needs no extra fetch to show it.

## Status badges

New `STATUS_CONFIG` entries for `NdrStatus`, namespaced `"Ndr.X"` (same pattern as
the existing `"Manifest.X"` entries), reusing existing tokens rather than minting
new colors:

- `Ndr.Open` → `status-ndr`
- `Ndr.Contacting` → `status-out-for-delivery`
- `Ndr.CustomerConfirmed` → `status-received`
- `Ndr.ReattemptScheduled` → `status-sealed`
- `Ndr.RtoRequested` → `status-rto`
- `Ndr.Resolved` → `status-delivered`

`status-out-for-delivery` (38.0° amber) and `status-in-transit` (35.3° amber) are
close-hue and haven't been validated side-by-side in this specific new grouping —
before finalizing, run the `dataviz` skill's palette validator over exactly the 6
tokens above as a set (not the whole app palette, which F1/F4 already validated)
and adjust any reuse that fails the adjacent-pair floor, preferring a different
existing token over minting a new one.

## Testing

- **Backend**: unit tests for the two new query filters (`shipmentId` on
  `NdrService.ListAsync`, `originalShipmentId` on `ListReturnShipmentsAsync`) and
  for `Awb`/`OriginalAwb` appearing correctly in both response shapes; an
  integration test proving `GET /ndrs?shipmentId=` and
  `GET /return-shipments?originalShipmentId=` still respect branch scoping (a
  branch-restricted caller with no access to either side of the shipment gets an
  empty result, not the row).
- **Frontend**: Vitest tests for `ndrPanelStore`/`returnShipmentPanelStore`, each
  status-section component (`NdrUpdateSection`'s legal-move buttons,
  `NdrRtoSection`'s required-`RtoReason` validation, the Resolve-as-Other path),
  and the shipment-detail linkage section's conditional fetch/render.
- **Manual E2E** against the real API/Postgres before the phase is called done —
  every prior phase's own pass found real bugs (missing enum converters, stale
  query keys, wrong BFF paths) invisible to unit tests alone. Walk a shipment
  through: fail a delivery attempt (existing F5 flow) → confirm it lands in the
  `/ndrs` queue with the correct AWB → `Open → Contacting → CustomerConfirmed →
  RtoRequested` → resolve as RTO → confirm `RTO-{date}-{seq}` appears in
  `/return-shipments` with the correct original AWB → confirm the shipment detail
  Sheet shows "View Return Shipment" and the return shipment Sheet shows the
  original AWB → dispatch/receive the return manifest (existing F4 flow) to reach
  `RtoAtOrigin` → Confirm Return → verify `MerchantConfirmedAt` is set. Also walk
  the `Reattempt` branch once (`CustomerConfirmed → ReattemptScheduled → resolve
  as Reattempt`) and confirm the shipment returns to `OutForDelivery`. Also verify
  a branch-restricted user cannot see an NDR/return shipment outside their branch
  access.

## Definition of Done (CLAUDE.md §37, applied to this phase)

Domain-accurate UI; permission-gated (`ndr.read`, `ndr.manage`, `ndr.resolve`,
`rto.read`, `rto.manage`) and branch-scoped per the backend's existing
`CheckBranchAccessAsync` checks; loading/error/empty states on every new screen;
backend + frontend tests passing; `docs/FRONTEND-ROADMAP.md` F6 entry updated with
completion notes; build green; no regressions in F0–F5.
