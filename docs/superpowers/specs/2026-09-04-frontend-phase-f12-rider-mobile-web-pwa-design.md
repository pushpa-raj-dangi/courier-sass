# Frontend Phase F12 — Rider Mobile-Web (PWA) — Design Spec

## 1. Context

Frontend Phase F5 shipped a **desktop-friendly approximation** of rider
self-service: `(staff)/my-deliveries` (task list) + `RecordAttemptSheet`
(canvas signature pad, `capture="environment"` file input for photo,
`navigator.geolocation` for GPS) — explicitly scoped that way, with a
dedicated rider mobile-web experience flagged as a later roadmap candidate
(`docs/FRONTEND-ROADMAP.md` "Later" section: *"Phase F12 — Rider Mobile-Web
(if F5's desktop approximation proves insufficient for real field use)"*).

This phase is that follow-up. Real Nepal courier riders work one-handed, on
phones, often in low-signal areas (rural wards, inside buildings, moving
vehicles) — the desktop approximation has no offline story at all (every
mutation is a direct `clientFetch` call that simply fails with no
connectivity) and no camera-based scanning (manual AWB entry only, a gap
already deferred twice — Frontend Phase F4 for manifest scanning, F7 for
COD operations — both explicitly noting "a camera scanner is a later
roadmap candidate").

All backend APIs this phase's UI drives already exist and are unchanged in
shape (`Riders` module, Phase 8): `POST /api/v1/riders/{id}/scan/pickup`,
`POST /api/v1/riders/{id}/delivery-attempts/fail`, `POST
/api/v1/riders/{id}/delivery-attempts/complete`, `POST
/api/v1/riders/{id}/pod-uploads`, `GET /api/v1/riders/me`, `GET
/api/v1/riders/me/tasks/today`. One real backend gap surfaced while
designing the offline queue (§4) — closed first, per this project's
established pattern.

## 2. Goals / Exit Criteria

- A rider can install the app to their home screen (PWA manifest + service
  worker, scoped to `/rider/*`, distinct from the staff back-office).
- A rider can view today's assigned tasks, scan a pickup, and record a
  delivery attempt (success with POD, or failure with a reason) **with zero
  network connectivity**, and have every queued action reach the server
  automatically once connectivity returns — proven with the browser's
  offline dev-tools toggle, not just code review.
- A rider can scan an AWB barcode/QR with the device camera as the primary
  input method, with manual entry always available as a fallback.
- No mutation can be double-applied by a retried/replayed queue item
  (proven by a backend integration test forcing a duplicate replay).
- `(staff)/my-deliveries` and its now-superseded components are deleted, not
  left as dead code.
- Mobile Lighthouse PWA audit passes (installable, works offline, viewport
  configured) on a real build.

## 3. Non-Goals (explicit deferrals, confirmed with the user before design)

- **Push notifications** for new task assignment. iOS Web Push only works
  for an installed home-screen PWA (Safari 16.4+), and iOS PWAs opened via
  Safari tabs (the EU-mandated flow since 2024) lose push entirely — too
  inconsistent a base to build a workflow on yet. v1 relies on the existing
  foreground poll / pull-to-refresh pattern for new tasks.
- **iOS full parity.** iOS Safari has zero support for the Background Sync
  API and evicts PWA storage after ~7 days idle with a ~50MB cap (both
  confirmed live in research, not assumed) — the offline queue still works
  on iOS, but only while the app is open and foregrounded, not via a true
  background sync. This is a platform ceiling this design works around
  (foreground/reconnect draining, §4), not a gap to close.
- **Dispatcher-side UI** (`/riders` create-rider, assign-task, vehicles) is
  unchanged — this phase is rider self-service only.
- **Rider-specific session length.** The existing 7-day refresh-token cookie
  is reused unchanged; a rider re-authenticates roughly weekly, same as any
  staff user. Extending it is a one-line backend config change if ever
  wanted, deliberately not bundled into this phase.
- **A fully separate deployable app.** Considered and rejected — see §5.1.

## 4. Backend Changes (done first)

### 4.1 Idempotency protection for rider action endpoints

`IdempotencyMiddleware` (`src/Api/Middleware/IdempotencyMiddleware.cs`)
today only protects **merchant**-JWT POSTs on three routes (`/shipments`,
`/pickups`, `/shipments/{id}/cancel`) — a staff/rider-JWT caller on any
route is explicitly passed through untouched (`IdempotencyMiddleware.cs:30`'s
comment: *"avoids retrofitting every existing staff-facing test from prior
phases"*). That exemption is correct for the *existing* staff surface (which
has no offline-queue-driven retries to protect against), but this phase
introduces exactly that: a queued mutation replayed after a false-negative
network failure could double-collect COD, create a duplicate
`DeliveryAttempt` row, or double-consume a `pod-upload`.

Fix, scoped narrowly to avoid the blanket-staff-idempotency regression risk
the original exemption comment was protecting against:

- `IsIdempotentRoute` gains a second, separate match list for exactly the
  four rider-action routes this phase's queue replays: `POST
  /api/v1/riders/{id}/scan/pickup`, `POST
  /api/v1/riders/{id}/delivery-attempts/fail`, `POST
  /api/v1/riders/{id}/delivery-attempts/complete`, `POST
  /api/v1/riders/{id}/pod-uploads`. No other staff route gains a new
  requirement — every existing staff-facing test keeps passing unchanged.
- The `merchantId is null` branch is narrowed: it still passes through
  untouched for every route *except* this new rider-action list, where it
  now requires an `Idempotency-Key` header and keys the stored record on
  `staff:{userId}:{idempotencyKey}` (`context.User.GetUserId()`, already
  available — every JWT carries it) instead of `merchantId`.
- Same conflict/replay/24h-TTL/only-cache-2xx semantics as the existing
  merchant path — no new behavior class, just a second identity dimension.

### 4.2 Nothing else changes on the backend

`Riders` module DTOs/routes/permissions are otherwise unchanged — this
phase is additive (one middleware extension) plus the frontend rebuild.

## 5. Frontend

### 5.1 App structure — real `/rider` path segment, same deployable

Recommended and confirmed: a new `app/rider/` subtree in the **same**
Next.js app (`src/web`) — not a separate deployable. `ARCHITECTURE.md`
explicitly commits to "one deployable Next.js frontend"; a second app would
duplicate the entire BFF/session/auth layer for no real benefit, since
Next.js already code-splits per route so the rider bundle (barcode scanner,
IndexedDB queue) never loads for a staff user on `/branches`.

**One correction from the chat-approved sketch:** the rider surface must
live under a **real URL path segment** (`app/rider/...`), not a parenthetical
route group like `(staff)`. A PWA's installable `scope`/`start_url` and a
service worker's registration `scope` all need a real URL prefix to isolate
from the staff app's own manifest/service-worker — a route group produces no
URL segment at all, so it can't provide that isolation. This doesn't change
anything already approved (still one app, one deployable, reused BFF/auth) —
it's a routing-mechanics detail, not a scope change.

```text
app/
├── rider/
│   ├── layout.tsx          # mobile shell: bottom tab bar, sync-status indicator,
│   │                       # registers the Serwist service worker scoped to /rider/
│   ├── manifest.ts         # -> /rider/manifest.webmanifest, scope: "/rider/",
│   │                       # start_url: "/rider/tasks", name: "Courier Rider"
│   ├── tasks/page.tsx      # today's task list (replaces (staff)/my-deliveries)
│   ├── scan/page.tsx       # full-screen camera scanner (§5.3)
│   └── sw.ts               # Serwist service worker source (built to public/sw-rider.js)
├── (staff)/                # unchanged
│   └── my-deliveries/      # DELETED — superseded by app/rider/tasks
└── ...
```

`middleware.ts` gains a `/rider/:path*` matcher requiring a valid staff
session (same cookie check as `(staff)/*` today) — not narrowed to the
`PickupRider`/`DeliveryRider` role at the middleware layer, matching the
existing `my-deliveries` UX: a logged-in staff user with no linked `Rider`
record sees the same "no rider profile" `EmptyState` the current page shows,
enforced by `GET /api/v1/riders/me` returning 404, not by route-level role
gating.

### 5.2 Offline queue

New `features/rider-app/sync/` module:

- **`outboxDb.ts`** — `idb` (3KB, actively maintained IndexedDB wrapper; not
  Dexie/RxDB — the need is one outbox table plus a small task-list read
  cache, not general-purpose sync) — schema: `{ id, url, method, headers,
  body: Blob | string, idempotencyKey, shipmentId, createdAt, status:
  "pending" | "syncing" | "stuck", lastError? }`.
- **`enqueue.ts`** — every rider mutation hook (`useRiderPickupScan`,
  `useRecordFailedAttempt`, `usePodUpload`, `useCompleteDelivery`) is
  rewritten to write to the outbox instead of calling `clientFetch`
  directly, generating an `Idempotency-Key` via the existing
  `src/web/src/lib/api/idempotencyKey.ts` helper (already built for merchant
  mutations in Phase F10 — reused verbatim, not reinvented) and applying an
  optimistic TanStack Query cache update immediately.
- **`syncManager.ts`** — drains the outbox in per-shipment FIFO order
  (a pickup scan must reach the server before that shipment's delivery
  attempt does) on: `window.addEventListener("online")`, `visibilitychange`
  → visible, and a 30s foreground-only poll (covers a connection
  `navigator.onLine` reports as "up" but that is in practice too degraded to
  succeed). Background Sync API (`self.registration.sync.register(...)`) is
  registered as a bonus fast-path where supported (Chrome/Android) —
  explicitly never the sole mechanism, since iOS has no support for it at
  all (confirmed in research, §3).
- **Stuck items**: a replay that fails with a 4xx meaning "retrying can't
  help" (e.g. wrong shipment state) is marked `"stuck"` and surfaced in the
  UI for manual review — never retried forever, never silently dropped.
- **`useOutboxStatus()`** hook — feeds the mobile shell's persistent
  sync-status indicator (pending count, last-synced time).

Photo/signature blobs are stored as real `Blob`s in IndexedDB (not
base64 — avoids ~33% size bloat against iOS's ~50MB PWA storage ceiling).

### 5.3 Camera barcode scanning

New `features/rider-app/scanning/BarcodeScanner.tsx` — a thin custom
wrapper, not `html5-qrcode` (flagged in research as under-maintained, and it
bundles UI chrome this design doesn't want): native `BarcodeDetector` API
first (Chrome/Android — the likely majority case), lazy-loaded
`@zxing/browser` as the fallback decoder when `BarcodeDetector` is
unavailable (Safari/iOS/older browsers). Full-screen `getUserMedia` camera
view with a large tap-to-focus target; a manual-AWB-entry text field is
always visible below the viewfinder, not hidden behind a toggle — covers
damaged/unreadable barcodes and denied camera permission without a dead end.

### 5.4 Mobile shell

Bottom tab bar (**Today's Tasks** / **Scan** / **Profile**) replacing the
staff sidebar entirely for this surface. Large touch targets (44px+)
throughout. The existing `SignaturePad` (canvas-based, built in F5) and the
`navigator.geolocation` GPS-capture pattern from `RecordAttemptSheet` carry
over largely as-is into a new `RecordAttemptSheet`-equivalent — the same
capture UX, rewired to enqueue through §5.2 instead of calling `clientFetch`
directly.

### 5.5 PWA (Serwist)

`@serwist/next` + `serwist` (the actively-maintained successor to the
archived `next-pwa` — confirmed current in research). `app/rider/sw.ts`
precaches the rider shell + today's-tasks data for offline first-load;
`app/rider/manifest.ts` scopes `start_url`/`scope` to `/rider/`. The
existing staff app gains no service worker or manifest — this phase is
scoped to making the rider surface installable, not the whole product.

### 5.6 Retiring `my-deliveries`

`(staff)/my-deliveries/page.tsx`, `RecordAttemptSheet`,
`PickupScanControl`, and `myDeliveriesPanelStore` are deleted outright (not
left as dead/unreferenced code) once `app/rider/tasks` reaches parity —
confirmed in §2's exit criteria. `features/riders/` (dispatcher-facing:
`RiderCreateSheet`, `VehicleCreateSheet`, `AssignTaskSheet`, the `useRiders`/
`useCreateRider`/`useVehicles` hooks) is untouched; only the rider
*self-service* slice moves.

## 6. Testing

- **Backend:** integration test forcing a duplicate replay of each of the 4
  newly-protected rider routes with the same `Idempotency-Key` — proves the
  second call returns the cached first response, not a second side effect
  (mirrors the existing merchant idempotency test shape). Existing staff
  route tests are asserted unchanged (no new header requirement leaks onto
  routes outside the 4-route list).
- **Frontend unit/component:** outbox enqueue/drain logic (fake IndexedDB via
  `fake-indexeddb`), FIFO ordering across a pickup+delivery pair for the same
  shipment, stuck-item classification, `BarcodeScanner`'s
  `BarcodeDetector`-available vs. fallback branches, manifest/service-worker
  build output.
- **Manual E2E, offline-specific (this phase's most important verification —
  no automated test proves real offline behavior end-to-end):** using
  Chrome DevTools' offline throttle, walk pickup-scan → delivery-attempt
  (success with POD, and separately failure) fully offline, confirm the UI
  reflects "pending sync," re-enable the network, confirm the outbox drains
  and the shipment's real state on the server matches — against the real
  API/Postgres, not mocks, matching this project's established E2E
  convention for every prior frontend phase.
- **Manual E2E, scanning:** a real device camera against a printed AWB
  barcode/QR, plus the manual-entry fallback path.
- Lighthouse PWA audit on a production build.

## 7. Security Notes

- The offline queue introduces no new trust boundary — every replayed
  mutation still goes through the same BFF route, same httpOnly session
  cookie, same server-side permission/branch-scope checks as a live call.
  Nothing about "queued and replayed later" bypasses authorization.
- §4.1's idempotency key is scoped per-user (`staff:{userId}:...`), so one
  rider's queued/replayed keys can never collide with or be replayed against
  another rider's session, even if two riders reuse the same client-generated
  key value by coincidence.
- Camera access (`getUserMedia`) is requested only on the dedicated `/scan`
  page, on explicit user action (not on page load) — denial falls back
  cleanly to manual entry, never blocks the flow.
