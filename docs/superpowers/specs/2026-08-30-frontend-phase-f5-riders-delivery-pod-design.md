# Frontend Phase F5 — Riders + Delivery + POD — Design

**Date:** 2026-08-30
**Status:** Approved, ready for implementation planning.
**Roadmap entry:** `docs/FRONTEND-ROADMAP.md` Phase F5.

## Context

F0–F4 are complete (design system/shell, auth + org dashboard, branches, shipments,
manifests). Phase 8 (backend) shipped a full `Riders` module — `Rider, Vehicle,
DeliveryRun, DeliveryTask, DeliveryAttempt, DeliveryProof` — but `src/web` has no UI
for any of it. This phase builds that UI, per `docs/FRONTEND-ROADMAP.md`'s F5 entry:
rider assignment, delivery run/task views, delivery attempt recording (reason codes
from `CLAUDE.md` §19), and POD capture (signature/photo/OTP/GPS, desktop-friendly
approximation — no dedicated rider mobile-web app, that's the later F12 candidate).

Investigating the existing backend surface (`src/Modules/Riders/`) before design
turned up three real gaps, all resolved with the user below and folded into this
spec rather than deferred:

1. No list/read endpoint exists for `DeliveryRun`/`DeliveryTask` at all — every
   existing Riders endpoint is a single-shipment-scoped write. Nothing can build any
   "run/task view" without at least one new read endpoint (same shape as F4's
   missing `GET /api/v1/manifests` gap).
2. A logged-in rider has no way to discover their own `Rider.Id`. `GET
   /api/v1/riders` requires `rider.read`, which the seeded `PickupRider`/
   `DeliveryRider` roles don't hold (they hold `delivery.attempt`,
   `delivery.complete`, `cod.collect`, `shipment.scan` only —
   `AuthService.cs:83`). Every other Riders endpoint is `{riderId}`-scoped, so a
   rider literally cannot call any of them today.
3. None of the 5 Riders enums (`DeliveryTaskType`, `DeliveryTaskStatus`,
   `DeliveryRunStatus`, `RiderRoleType`, `DeliveryAttemptReason`) have
   `JsonStringEnumConverter`. This is the same latent bug class hit and fixed
   narrowly, one enum at a time, in F2 (`BranchType`), F3 (`ShipmentStatus`), and F4
   (`ManifestPurpose`/`ManifestStatus`) — the 4th occurrence, just not yet
   discovered here because no frontend code has touched these enums yet.

## Resolved product decisions (confirmed with user during brainstorming)

- **Rider self-service is in scope.** Riders already get real login credentials
  (`ProvisionStaffUserAsync`, Phase 8), and CLAUDE.md §17 frames a rider-facing
  experience as core to the domain. This phase adds a "My Deliveries" view a
  logged-in rider uses directly (on the same staff back-office, desktop-friendly —
  not a separate app), rather than staying dispatcher-only. F12 later swaps this
  for a dedicated mobile experience if the desktop approximation proves
  insufficient; it doesn't need to invent a new interaction model, just a new shell
  around the same underlying flows.
- **Task assignment uses AWB lookup, not a browsable eligible-shipments list.**
  Matches F4's already-established manual-AWB-entry pattern for finding a specific
  shipment during an operational action. No new shipment-list filter needed.
- **Rider and Vehicle get full management screens** (list + create), not just a
  bare picker control — following the F2 Branches precedent, since someone needs to
  onboard riders through the UI and deferring it risks the same kind of
  discovered-mid-phase gap F4 hit with the manifest list.
- **No dispatcher "all riders today" overview board.** Per-rider assignment (AWB
  lookup → pick rider) is the full dispatcher-side scope this phase. A cross-rider
  workload board is closer to F8's Rider Performance dashboard territory — deferred
  there, not duplicated here.
- **"My Deliveries" shows only today's active run.** Pending pickup/delivery tasks
  for today, dropping off once completed/failed. No history view — matches the
  domain shape (`DeliveryRun` is literally one row per rider+branch+day) and keeps
  this phase out of F8's reporting scope.

## Backend changes

### 1. Enum serialization fix (all 5 at once)

Add `[JsonConverter(typeof(JsonStringEnumConverter))]` directly to each enum
declaration — `DeliveryTaskType`, `DeliveryTaskStatus`, `DeliveryRunStatus`,
`RiderRoleType`, `DeliveryAttemptReason` — same narrow, per-enum fix used in
F2/F3/F4, just applied to all 5 up front since this investigation already found
every one of them, instead of rediscovering them one at a time during later E2E
passes.

### 2. `GET /api/v1/riders/me`

Resolves the caller's own `Rider` row via `actingUser.GetUserId()` matched against
`Rider.UserId`. Same shape as Phase F0's `GET /api/v1/auth/me`: authenticated, no
extra permission requirement (a rider has no permission that would let them read
their own record otherwise), 404 if the caller has no `Rider` row. Returns the
existing `RiderResponse`.

### 3. `GET /api/v1/riders/me/tasks/today`

Self-scoped by design — no `{id}` route parameter, so there's no risk of a rider
querying another rider's tasks via a guessed id (this module's established
cross-tenant-bug pattern is exactly "a new accessor forgets a scope check"; making
the scope implicit in the route shape removes the class of bug rather than relying
on a check to catch it). Authenticated, no extra permission requirement, same
reasoning as `/me` above.

Resolves the caller's `Rider`, finds today's `DeliveryRun` for
`(RiderId, Date=today, Status=Active)` (may not exist — returns an empty result,
not 404, since "no run yet today" is a normal state), and returns its pending
`DeliveryTask`s enriched with the shipment fields a rider needs to act on the task
— `DeliveryTaskResponse` alone only carries a bare `ShipmentId`, useless for a task
card. New response shape:

```csharp
public record MyTaskResponse(
    Guid TaskId, Guid ShipmentId, DeliveryTaskType Type, int Sequence,
    string Awb, string RecipientName, string RecipientPhone,
    string RecipientAddressLine, int RecipientWardNumber, decimal CodAmount);

public record MyTasksTodayResponse(Guid? DeliveryRunId, List<MyTaskResponse> Tasks);
```

Enrichment goes through a new narrow `IShipmentTaskLookupService` in
`Shipments.Application` (mirrors the existing `IBranchLookupService`/
`IManifestReportingLookupService` cross-module lookup pattern — `Riders` still never
references `Shipments`' `DbSet` directly):

```csharp
public interface IShipmentTaskLookupService
{
    Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default);
}

public record ShipmentTaskInfo(
    string Awb, string RecipientName, string RecipientPhone,
    string RecipientAddressLine, int RecipientWardNumber, decimal CodAmount);
```

Not persisted/cached anywhere — computed live per request, same as every other
Reads method in this codebase.

### Not in scope this phase (parked, matches precedent of not over-building)

- Marking a `DeliveryRun` `Completed` — nothing in the Phase 8 backend ever
  transitions this field; it's a pre-existing gap, not something F5's UI needs to
  fix (the "My Deliveries" list just naturally empties as tasks complete).
- Any cross-rider or cross-branch task visibility (no dispatcher overview, per the
  resolved decision above).

## Frontend structure

New `src/web/src/features/riders/` feature folder, following the F2–F4 established
conventions (Sheet-over-pages via a Zustand panel store, `DataTable` for lists,
TanStack Query hooks per endpoint, Zod schemas for forms).

### Riders management

- **`/riders` list** (sidebar entry gated on `rider.read`): `DataTable` — license
  number, base branch, role type, active status. Mirrors `BranchList`.
- **Rider create Sheet** (`riderPanelStore`): name, email, password, phone, license
  number, base branch (reuses the branch picker pattern), role type
  (`PickupRider`/`DeliveryRider`).
- **Vehicle create Sheet**: type, plate number, capacity, optional rider assignment.
  Reachable from the same `/riders` page (a second tab or an adjacent action, not a
  separate nav entry — vehicles are a minor sub-resource here).

### Assign Task flow

A Sheet opened from an "Assign" action (entry point: a new button on `/riders`, or
the shipment detail Sheet — either works, implementer's call during planning):

1. AWB input → `GET /api/v1/shipments/{awb}`.
2. Shows shipment summary (recipient, current status). Task type is inferred from
   status: `Created` → Pickup, `AtDestinationBranch` → Delivery; any other status
   disables assignment with an inline explanation (e.g. "Shipment must be Created or
   AtDestinationBranch to assign a task, is currently X").
3. Rider picker: `GET /api/v1/riders`, client-filtered to active riders whose
   `BaseBranchId` matches the relevant branch (origin for pickup, destination for
   delivery) — same origin/destination branch logic `AssignTaskAsync` itself already
   enforces server-side; the filter is a UX convenience, not a security boundary.
4. Submit → `POST /api/v1/riders/tasks`.

### My Deliveries (rider self-service)

- **`/my-deliveries`** (sidebar entry gated on `delivery.attempt` OR
  `delivery.complete` — the two permissions only rider roles hold, so this
  naturally only appears for a rider).
- On load: `GET /api/v1/riders/me` (resolve own `Rider.Id`, needed for the
  per-task action endpoints which are `{riderId}`-scoped) then
  `GET /api/v1/riders/me/tasks/today`.
- Renders pending tasks as cards: AWB, recipient name/phone/address, a COD badge if
  `CodAmount > 0`. Per-task action:
  - **Pickup task** → "Scan pickup" — AWB re-entry/confirm →
    `POST /riders/{id}/scan/pickup`.
  - **Delivery task** → "Record attempt" — opens the Record Attempt Sheet below.

### Record Attempt Sheet

Opened from a Delivery task card. Success/failure choice branches the form:

- **Failure**: `DeliveryAttemptReason` select (the exact 9 values from CLAUDE.md
  §19), notes, optional GPS capture, customer-contacted checkbox, optional
  next-action text → `POST /riders/{id}/delivery-attempts/fail`.
- **Success (POD capture)**:
  - **Signature**: hand-built `<canvas>` pad (pointer events, clear button), no new
    library. `canvas.toBlob()` → raw-body upload to
    `POST /riders/{id}/pod-uploads?kind=Signature` → `storageKey`. Optional (matches
    `DeliveryProof.SignatureStorageKey`'s nullability).
  - **Photo**: `<input type="file" accept="image/*" capture="environment">` — a
    plain file picker on desktop, opens the camera automatically on a mobile
    browser, no separate code path needed. Same raw-body upload pattern,
    `kind=Photo`. Optional.
  - **OTP**: a plain "Customer verified via OTP" checkbox — `OtpVerified` is
    explicitly client-reported with no real verification flow (a deliberate Phase 8
    deferral); the UI shouldn't imply more rigor than exists server-side.
  - **GPS**: "Capture current location" button via
    `navigator.geolocation.getCurrentPosition`. Optional, fails gracefully
    (permission denied/unavailable) without blocking submission.
  - **COD**: if the task's `CodAmount > 0`, a required `CollectedCodAmount` field
    appears, pre-filled with the declared amount, editable — gated additionally on
    `cod.collect` (already granted to `DeliveryRider`).
  - Submit assembles both storage keys (if present) →
    `POST /riders/{id}/delivery-attempts/complete`.

## Testing

- **Backend**: unit tests for `GetMyRiderAsync`/`GetMyTasksTodayAsync` (rider not
  found → null/empty; correct self-scoping) and the new
  `IShipmentTaskLookupService` implementation; an integration test proving
  `GET /riders/me` 404s for a non-rider user and that
  `GET /riders/me/tasks/today` never returns another rider's tasks.
- **Frontend**: Vitest component tests for the signature pad (draw → produces a
  non-empty blob; clear → empties it), the Record Attempt form's success/failure
  branching, and the Assign Task flow's AWB-lookup → rider-picker → submit sequence
  (including the disabled-state explanation for an ineligible shipment status).
- **Manual E2E** against the real API/Postgres before the phase is called done —
  every prior phase's own investigation found real bugs (missing enum converters,
  stale query keys, wrong BFF paths) invisible to unit tests alone; this phase's
  own audit already found 3 such gaps before writing a line of code, so the same
  discipline applies to whatever's still hiding in the new code.

## Definition of Done (CLAUDE.md §37, applied to this phase)

Domain-accurate UI; permission-gated (`rider.read`, `rider.create`,
`delivery.assign`, `delivery.attempt`, `delivery.complete`, `cod.collect`) and
self-scoped where specified above; loading/error/empty states on every new screen;
backend + frontend tests passing; `docs/FRONTEND-ROADMAP.md` F5 entry updated with
completion notes; build green; no regressions in F0–F4.
