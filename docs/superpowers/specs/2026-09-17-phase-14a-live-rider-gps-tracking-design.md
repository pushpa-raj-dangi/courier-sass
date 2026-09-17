# Phase 14a — Live Rider GPS Tracking — Design Spec

## 1. Context

`PRODUCT-SPEC.md` §5 and `ROADMAP.md`'s Phase 14 entry explicitly deferred
"Live GPS rider tracking maps" out of v1, gated behind "a real operational
need driving it, not speculatively." Phases 0–13 (backend) and Frontend
Phases F0–F13 plus the Platform/Billing track are complete per
`ROADMAP.md`/`FRONTEND-ROADMAP.md`/`PLATFORM-ROADMAP.md`; this is the first
slice of Phase 14 to be built, confirmed with the user as the actual driver.

Today, GPS only exists as a **point-in-time** capture: `DeliveryAttempt` and
`DeliveryProof` each carry a `Latitude`/`Longitude` pair recorded once, at
the moment of that event (`DOMAIN-MODEL.md`'s Riders & Delivery section).
There is no continuous position stream and no live map anywhere in the
product. The rider-facing surface is the Phase F12 mobile-web PWA
(`app/rider/*`, `features/rider-app/`) — a real browser tab, not a native
app, which matters directly to this design (§3).

Phase 14 as a whole bundles five independent pieces (route optimization,
live GPS tracking, ETA prediction, smart dispatch, partner courier
integrations). Per the project's decomposition practice, each gets its own
brainstorm → spec → plan cycle; this document covers **only** live GPS
tracking. The other four are out of scope here entirely, not merely
deferred within this design.

## 2. Goals / Exit Criteria

- A rider's position updates automatically while the `/rider` PWA is open
  and the tab is in the foreground, with no dedicated "start sharing my
  location" action beyond having the app open.
- A branch-scoped "Live Map" view lets ops/dispatch staff at a branch see
  every currently-active rider assigned to that branch as a pin, each
  showing how long ago its position was last reported (an honest "last seen
  2m ago," never a fake continuously-moving pin once reporting has stopped).
- A delivery run's breadcrumb trail (every position reported while that run
  was active) can be retrieved for audit/dispute resolution, scoped the same
  way as every other rider-owned record.
- Old position pings are pruned automatically (§4.4) — this feature does not
  grow an unbounded table.
- Full tenant isolation and branch-scope authorization on every new
  endpoint, proven the same way every prior phase proved it: an integration
  test showing Org B / an out-of-branch user cannot read Org A's / another
  branch's rider positions.

## 3. Non-Goals (explicit deferrals, confirmed with the user before design)

- **Background/backgrounded-tab tracking.** iOS Safari (and, more loosely,
  Android Chrome) suspends `watchPosition` once a PWA tab is backgrounded or
  the screen locks. Reaching true always-on tracking would mean wrapping the
  PWA in a native shell (e.g. Capacitor) — a real scope increase (build
  pipeline, app-store distribution, native permissions) the user explicitly
  declined for this slice. This design is foreground-only by construction,
  not as a bug to fix later.
- **Customer-facing "rider is nearby" indicator on `/track/{awb}`.** Touches
  the public tracking surface CLAUDE.md §24 explicitly locks down (never
  expose internal staff/branch-security data). Doing this correctly needs
  its own privacy-scoped design — approximate radius, when to start/stop
  showing it, what happens if the rider's position is stale — once the
  internal map is proven in real use. Tracked as a follow-up sub-project,
  not built here.
- **Real-time push (WebSockets/SignalR).** The foreground-only cap already
  bounds how "live" this can meaningfully be; a ~15–20s poll interval is not
  meaningfully worse than a push channel here, and adding a stateful hub (plus
  a Redis backplane once this runs on more than one API instance) is not
  justified by this slice's need. Revisit only if real dispatcher usage
  proves polling latency is an actual operational problem.
- **Route optimization, ETA prediction, smart dispatch, partner courier
  integrations.** Separate Phase 14 sub-projects, each with its own spec.
- **A `RiderLocationCurrent` cache table.** "Latest position per rider" is
  answered by querying `RiderLocationPing` (indexed, `DISTINCT ON`-style
  latest-per-rider), not a redundantly-maintained second row — the same
  "derive rather than duplicate" call Phase 9 made for `ReturnShipment`.
  Revisit only if this query is proven to be a real bottleneck at scale.

## 4. Backend Changes

### 4.1 Domain model — one new entity

New `Riders.Domain.RiderLocationPing`:

```text
RiderLocationPing { Id, RiderId, DeliveryRunId (nullable),
                     Latitude, Longitude, AccuracyMeters (nullable),
                     RecordedAt }
```

No own `OrganizationId` — isolation flows transitively through
`RiderId -> Rider.OrganizationId`, the same pattern already established for
`DeliveryTask` (via `DeliveryRunId -> DeliveryRun -> RiderId`). `RecordedAt`
is client-reported (the device's own GPS timestamp) but the server also
independently indexes on its own insert order for pruning purposes — the
client value is trusted for *ordering within a run's breadcrumb*, never for
anything security- or billing-relevant, matching this codebase's existing
posture on client-reported fields (e.g. `DeliveryProof.OtpVerified`, itself
called out in `DOMAIN-MODEL.md` as client-reported pending real OTP
delivery).

`DeliveryRunId` is populated by looking up the rider's active `DeliveryRun`
for today at ping-insert time (same auto-create/reuse pattern
`RiderService.Tasks.cs` already implements) — it is nullable because a rider
can have the PWA open with no active run yet (e.g. before their first task
of the day is assigned).

### 4.2 New permission

Added to `Identity.Application.PermissionCatalog` alongside the existing
`rider.read`/`rider.create`/`rider.assign` block:

```text
("rider.location.read", "Read rider live location and breadcrumb history"),
```

Granted to exactly the roles that hold `rider.read` today, verified against
the real seed data in `AdditionalRoleSeeds.cs` rather than `RBAC.md`'s
illustrative table: `OrganizationAdmin` (via its "everything except
billing/ownership/delete" grant), `OperationsManager` (explicit), and
`Auditor` (via its blanket `*.read` grant), plus `OrganizationOwner`
(seeded separately with the full permission set). Notably `BranchManager`,
`Dispatcher`, and `HubStaff` do **not** hold `rider.read` today, so they get
no `rider.location.read` grant either — this design does not expand who can
see rider profiles/locations beyond the existing `rider.read` boundary,
even though that means `BranchManager` (the role most likely to actually
want a branch's Live Map) cannot see it out of the box. That's a
pre-existing RBAC gap on `rider.read` itself, not something to fix as a
side effect of this design — flagging it in case the user wants
`rider.read`/`rider.location.read` added to `BranchManager`/`Dispatcher` as
a separate, explicit RBAC change. The rider's own
self-reporting endpoint (§4.3) needs no new permission — it follows the
existing `/riders/me/*` self-lookup pattern, which authorizes "is this JWT's
subject a rider" rather than a permission grant.

### 4.3 New endpoints (`Riders.Presentation.RidersEndpoints`)

- `POST /api/v1/riders/me/location` — body `{ latitude, longitude,
  accuracyMeters?, recordedAt }`. Rider-self endpoint (same authorization
  shape as `GET /api/v1/riders/me/tasks/today`): resolves the caller's
  `Rider` row from the JWT's `UserId`, looks up/creates today's
  `DeliveryRun` the same way task assignment does, inserts one
  `RiderLocationPing`. Returns `204`. **Not** run through
  `IdempotencyMiddleware`'s rider-route list from Phase F12's design — a
  duplicate location ping is harmless (it's just an extra row with the same
  coordinates), unlike a duplicate COD collection or delivery attempt.
- `GET /api/v1/branches/{branchId}/riders/locations` — `rider.location.read`
  + existing branch-scope authorization (identical check shape to every
  other branch-scoped read in this codebase). Scopes riders by
  `Rider.BaseBranchId == branchId && IsActive`, matching the exact
  convention `RiderReportingLookupService.GetBranchRiderTaskLoadAsync`
  already established for "riders at this branch" (not the active run's
  branch — verified against the existing code, not assumed). Returns the
  latest `RiderLocationPing` per matching rider, with each rider's
  `RecordedAt` age so the frontend can render "last seen Xm ago" instead of
  a raw timestamp.
- `GET /api/v1/delivery-runs/{id}/breadcrumbs` — `rider.location.read`,
  branch-scoped via the run's `BranchId`. Returns the full ordered
  `RiderLocationPing` list for that run.

### 4.4 Retention — Hangfire recurring job

New recurring job (registered alongside the existing Phase 11 Hangfire jobs)
deletes `RiderLocationPing` rows older than 30 days, running daily. 30 days
comfortably covers the "audit a recent delivery dispute" use case from §2
without the table growing unbounded. This default can change with a single
config value if real usage calls for a longer/shorter window — not treated
as a hard product decision baked into the schema.

### 4.5 Nothing else changes

`Shipments`, `Manifests`, `Cod`, `Ndr` modules are untouched. This is
additive-only within `Riders` plus one new `Identity` permission row.

## 5. Frontend

### 5.1 Rider PWA — location-reporting hook

New `features/rider-app/location/useLocationReporting.ts`: calls
`navigator.geolocation.watchPosition` while `document.visibilityState ===
"visible"`, throttled to emit at most once per ~20s (geolocation watch
callbacks can fire far more often than that; the hook debounces before
sending). Stops the watch on `visibilitychange` → hidden and restarts it on
→ visible, so no position is ever reported while backgrounded. Posts
directly via `clientFetch` — **not** routed through the Phase F12 offline
outbox (`features/rider-app/sync/`): a location ping made while offline is
stale by the time connectivity returns and replaying it would show a wrong
"current" position, so pings made while offline are simply dropped, not
queued. Mounted once in `app/rider/layout.tsx` so it runs across every
rider screen, not per-page.

Camera/GPS permission prompt reuses the existing pattern from
`RecordAttemptSheet`'s `navigator.geolocation` capture (F5/F12) — no new
permission-request UX pattern introduced.

### 5.2 Staff web app — Live Map view

New `features/rider-tracking/` feature folder, one new page under the
branch dashboard surface (branch-scoped like every other branch view),
gated on `rider.location.read`. Polls
`GET /api/v1/branches/{branchId}/riders/locations` on a 15–20s TanStack
Query `refetchInterval`, rendering each rider as a map pin with name,
vehicle, and "last seen" age; a run's breadcrumb trail is a secondary
drill-down (click a rider → fetch that run's breadcrumbs) rather than a
top-level page, since it's an audit/dispute tool, not a daily-use view.

**New dependency:** `leaflet` + `react-leaflet`, tiles from OpenStreetMap.
No map library exists anywhere in `src/web` today (confirmed by inspection —
neither `leaflet`, `mapbox`, nor `maplibre` appear in `package.json` or any
source file). Leaflet + OSM needs no API key or billing account, consistent
with this being a self-hosted, Nepal-focused platform; Mapbox/Google Maps
remain an option to swap in later if the org wants better tile quality or
already holds a provider account, but that's not a blocker for this slice.

### 5.3 Nothing else in the frontend changes

`features/riders/` (dispatcher-facing create-rider/assign-task) and
`features/rider-app/sync/`, `scanning/` are untouched.

## 6. Testing

- **Backend integration:** tenant isolation (Org B cannot read Org A's rider
  locations/breadcrumbs) and branch-scope authorization on both new `GET`
  endpoints — a user without branch access to the rider's `BaseBranchId` is
  rejected on `.../riders/locations`, and a user without branch access to
  the run's own `BranchId` is rejected on `.../breadcrumbs` — the standard
  proof shape every prior phase used.
- **Backend integration:** posting a location ping auto-attaches to the
  caller's active `DeliveryRun` when one exists, and succeeds with a null
  `DeliveryRunId` when none does yet.
- **Backend unit:** the retention job deletes pings older than the
  configured window and leaves newer ones untouched.
- **Backend permission test:** a role holding `rider.read` but not
  `rider.location.read` is rejected on both new `GET` endpoints (proves the
  new permission is actually enforced, not just declared).
- **Frontend unit:** `useLocationReporting`'s throttle and
  visibility-pause/resume behavior (mocked `navigator.geolocation` and
  `document.visibilityState`).
- **Manual E2E:** open `/rider` on a real device, confirm pings arrive; open
  the staff Live Map for that branch, confirm the pin appears and its "last
  seen" age advances after backgrounding the rider tab — against the real
  API/Postgres, matching this project's established E2E convention.

## 7. Security Notes

- `rider.location.read` is a new, narrowly-scoped permission — it is not
  folded into the existing `rider.read` grant, so an org can grant "see
  rider profiles" without also granting "see where riders physically are
  right now," if that distinction ever matters to a customer.
- The self-reporting endpoint trusts only that the caller's JWT resolves to
  a real `Rider` row; it cannot report a location on behalf of any other
  rider (`RiderId` is derived server-side from the JWT, never taken from the
  request body).
- No location data is exposed on any public/anonymous endpoint — this
  design touches only staff-authenticated, branch-scoped, and
  rider-self-authenticated routes. `/track/{awb}` is explicitly untouched
  (§3).
