# Frontend Phase F4 — Scanning + Manifests + Branch Transfer — Design Spec

**Date:** 2026-08-29
**Status:** Approved for implementation planning
**Scope:** `docs/FRONTEND-ROADMAP.md` Phase F4 — manifest create/scan/seal/dispatch/receive/close,
manual-AWB scanning input, missing-shipment reconciliation on receive.

## 1. Context

Phases F0–F3 shipped the design system, app shell, auth, dashboards, Branches, and Shipments
(list + Sheet-based create/detail, per `docs/ARCHITECTURE.md` §9a). This phase adds the Manifests
feature area covering the full `Manifest` lifecycle (`Draft → Sealed → InTransit → Received →
Closed`, per CLAUDE.md §14) — building an outgoing manifest by scanning shipments onto it,
sealing, dispatching with vehicle/driver info, receiving at the destination branch with
missing-shipment detection, and closing.

Backend work is paused for now (user's explicit instruction), so this phase works strictly within
the existing `Manifests` API surface, with one narrow, necessary exception (§2).

Two real gaps surfaced researching that surface, both resolved with the user before design:

- **No manifest list/search endpoint.** `ManifestsEndpoints.cs` exposes only `GET /{id:guid}`
  (by internal id, known only right after creation) and `GET /{id:guid}/missing-shipments` — no
  way to browse or search manifests at all. Unlike Phase F2/F3's gaps (a missing timeline, missing
  filters — both nice-to-haves layered on top of an otherwise-complete core flow), this one blocks
  roughly a third of this phase outright: the destination branch's staff has no way to *find* an
  inbound manifest to receive, since receiving naturally happens at a different branch/session
  than creation. **Resolved: add a minimal `GET /api/v1/manifests` list endpoint now** (§2) —
  framed as closing a foundational inconsistency every other entity (Branches, Shipments) already
  has, not new business feature work.
- **Camera-based barcode/QR scanning** is a materially larger addition (a scanning library, camera
  permission handling, device testing) than this phase's other work. **Resolved: manual AWB text
  entry only this phase** — the roadmap text explicitly allows this as a fallback; camera-based
  scanning is deferred, not silently dropped. Recorded in `docs/CLARIFICATIONS.md` (§3 below).

## 2. Backend addition: `GET /api/v1/manifests`

**Files:**
- Modify: `src/Modules/Manifests/Application/ManifestService.Reads.cs` — add `ListAsync(Guid?
  branchId, ManifestStatus? status, ClaimsPrincipal actingUser, CancellationToken)` returning
  `List<ManifestResponse>` (no pagination, matching Branches' `GET /api/v1/branches` precedent —
  manifest volume per branch is naturally bounded, unlike Shipments which earned real
  server-side pagination).
- Modify: `src/Modules/Manifests/Presentation/ManifestsEndpoints.cs` — add `GET /` on the
  existing `/api/v1/manifests` group, permission `manifest.read`.
- Test: `tests/Manifests.Tests/` — a unit test asserting the branch-scoping filter (a manifest
  whose `FromBranchId`/`ToBranchId` are both outside the caller's accessible branches is excluded)
  and the `status` filter.
- Test: `tests/Api.Tests/` — an integration test confirming a caller without branch access to
  either side of a manifest doesn't see it in the list (matching this project's established
  "always test the branch-access boundary explicitly" convention).

**Filtering logic** (mirrors `CheckBranchAccessAsync`'s existing pattern in `ManifestService.cs`):
a manifest is visible if the caller's `GetAccessibleBranchIdsAsync(actingUser, "manifest.read",
...)` result is `null` (all-branches scope) or contains `FromBranchId` **or** `ToBranchId` — a
branch can be sending or receiving. `branchId` query param further narrows to manifests touching
that specific branch; `status` query param filters by `ManifestStatus`.

## 3. `CLARIFICATIONS.md` entry

A new numbered entry (§15, following §14) documenting: the list-endpoint gap and its resolution
(added, not deferred — unlike every other gap this project has hit), and the manual-AWB-only
scanning decision with what unblocks camera-based scanning later (a scanning library choice +
camera permission UX, no backend dependency).

## 4. Routing and panel state

Per `docs/ARCHITECTURE.md` §9a:

```text
/manifests           list — DataTable, permission manifest.read
```

Create and detail are Sheets, driven by a new `features/manifests/store/manifestPanelStore.ts`,
mirroring `branchPanelStore.ts`/`shipmentPanelStore.ts` exactly:

```typescript
type ManifestPanelState =
  | { mode: "closed" }
  | { mode: "create" }
  | { mode: "detail"; id: string };  // keyed by the manifest's Guid id, not Code — GET /{id} is
                                      // the only read the backend supports for a single manifest;
                                      // Code has no lookup-by-code endpoint
```

## 5. Manifest list page

`DataTable` fed by `GET /api/manifests` (BFF proxy passing `branchId`/`status` query params
through) and a `useManifests(branchId?, status?)` hook.

Columns: Code, From Branch, To Branch (both resolved via `useBranches()`, reused from Branches),
Purpose (plain text — "Forward"/"Return" — not a `StatusBadge`, it's a category not a lifecycle
state, matching how Branch `Type` is rendered), Status (`StatusBadge`), Shipment Count, Created At.

A branch filter defaults to the currently-active branch (`useUiStore`'s `activeBranchId`, set by
`BranchSwitcher` — its first real second consumer after the branch dashboard sheet, giving it a
second reason to exist beyond F2's original single use). "New Manifest" button (permission
`manifest.create`) opens the create Sheet.

## 6. Manifest statuses — `StatusBadge` treatment

`statusConfig.ts` gains five new entries (`Draft`, `Sealed`, `InTransit`, `Received`, `Closed`) —
note `InTransit` is a real *name collision* with a `ShipmentStatus` value already in the config
map; since `statusConfig.ts` is a single flat `Record<string, StatusConfigEntry>` keyed by the
raw status string, a second `InTransit` key would silently overwrite the shipment one. This must
be resolved by namespacing manifest statuses in the map (e.g. `Manifest.Draft`, `Manifest.Sealed`,
...) and having `StatusBadge` accept an optional `namespace` prop (defaulting to none, so every
existing shipment-status callsite is unaffected) that prefixes the lookup key. Before picking the
five colors, **invoke the `dataviz` skill** for guidance — same division of labor as every prior
phase's status/tile treatment (F1's dashboard tiles, F2's branch dashboard) — and re-validate the
full palette (existing shipment tokens + the five new manifest tokens together) with
`validate_palette.js`, since new tokens can collide with existing adjacent-pair contrast even if
each looks fine alone.

## 7. `ManifestCreateSheet`

Fields matching `CreateManifestRequest`: From Branch, To Branch (`useBranches()`, both required,
must differ — client-side check mirroring the backend's own `FromBranchId == ToBranchId`
rejection, so the error surfaces before a round-trip), Purpose (Forward/Return, defaults to
Forward). On success, `openDetail(created.id)`.

## 8. `ManifestDetailSheet`

One Sheet, sectioned by `manifest.status` — not one Sheet variant per status, matching every
established Sheet's "sections in one scrollable panel" shape:

```text
Header: Code, StatusBadge, From Branch, To Branch, Purpose, Shipment Count

Draft:
  AWB scan input (Enter submits) -> POST /{id}/scan (or /rto-scan when Purpose is Return)
  A running "scanned this session" list, appended to from each successful scan response
  (ManifestShipmentResponse) — ManifestResponse itself carries no shipment list, only
  ShipmentCount, so this list is honestly session-local and resets on reload; ShipmentCount
  (refetched after each scan) is the durable source of truth shown in the header.
  "Seal Manifest" button (disabled until ShipmentCount > 0) -> POST /{id}/seal

Sealed:
  Vehicle Plate (required), Driver (optional Select, sourced from `GET /api/v1/riders` — there is
  no dedicated "Driver" entity; `DriverId` is a bare unvalidated `Guid?` server-side, and `Rider`
  is the closest existing concept for "the person driving this transfer's vehicle." Confirmed via
  `RidersEndpoints.cs`: `GET /api/v1/riders` already exists and returns a list, reused as-is — no
  backend change needed for this field.)
  "Dispatch" button -> POST /{id}/dispatch, moves Sealed -> InTransit directly (no separate
  "Dispatched" status — matches ManifestStatus's own code comment)

InTransit:
  AWB receive-scan input (Enter submits) -> POST /{id}/receive-scan
  Same running "scanned this session" list pattern as Draft

Received:
  Missing-shipments panel -> GET /{id}/missing-shipments (a real server list, shown directly —
  the one place in this Sheet where the backend already tracks a durable list)
  "Close Manifest" button -> POST /{id}/close

Closed:
  Read-only summary, no actions
```

Every scan/transition error surfaces the backend's real reason. `ManifestScanRejectedException`'s
`ReasonCode` (`SHIPMENT_NOT_FOUND`, `WRONG_STATE`, `WRONG_CUSTODY`, `MANIFEST_NOT_OPEN`,
`MANIFEST_NOT_RECEIVING`, `NOT_ON_MANIFEST`, `ALREADY_RECEIVED`) maps to a plain-language message
via a small lookup table in the frontend — never the raw code shown to the user.

## 9. BFF routes

```text
app/api/manifests/route.ts                       GET (list, branchId/status), POST (create)
app/api/manifests/[id]/route.ts                   GET (detail)
app/api/manifests/[id]/scan/route.ts              POST
app/api/manifests/[id]/rto-scan/route.ts          POST
app/api/manifests/[id]/seal/route.ts              POST
app/api/manifests/[id]/dispatch/route.ts          POST
app/api/manifests/[id]/receive-scan/route.ts      POST
app/api/manifests/[id]/close/route.ts             POST
app/api/manifests/[id]/missing-shipments/route.ts GET
```

All thin proxies, matching every prior phase's established pattern exactly.

## 10. Permissions

```text
manifest.read     — list page, detail sheet
manifest.create   — "New Manifest" button, create sheet, the scan action (matches the backend's
                    own choice of permission for POST /{id}/scan)
manifest.seal     — "Seal Manifest" button
manifest.dispatch — "Dispatch" button
manifest.receive  — receive-scan action
manifest.close    — "Close Manifest" button
```

## 11. Testing approach

Matches every prior phase: backend unit + integration tests for the new list endpoint (§2); BFF
route tests (mocked `apiFetch`); a hook test where there's real logic beyond a bare `useQuery`
wrapper (none anticipated here — no debounce/derivation logic like F3's quote hook); component
tests for the create Sheet's validation and the detail Sheet's status-section switching and
error-reason-code mapping.
