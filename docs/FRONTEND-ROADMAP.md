# Frontend Roadmap

This roadmap sequences the frontend work deliberately deferred by `docs/ROADMAP.md`
Phase 12 ("Phase 12b" — merchant portal) and Phase 13 (staff dashboards): `src/web` is
an untouched `create-next-app` skeleton as of 2026-08-29. Every phase below targets the
**staff back-office** portal first, per the design decision recorded in
`docs/superpowers/specs/2026-08-29-frontend-roadmap-design.md`. The merchant portal and
public tracking page are separate, later roadmap entries (Phase F10+) — not detailed here
until the staff back-office is done, following the same "don't plan too far ahead"
discipline `docs/ROADMAP.md` uses for its own backend phases.

Phase order is fixed and must not be reordered or parallelized across unrelated phases —
each phase's entry criteria assume the previous phase is actually done (full Definition
of Done, `CLAUDE.md` §37, applied to frontend: domain-accurate UI, permission/branch-scope
gating, loading/error/empty states, tests, docs updated, build green), not just started.

All backend APIs this roadmap consumes are already complete (`docs/ROADMAP.md` Phases
0–13, backend slices). No backend work is expected during Phases F0–F9 unless a genuine
API gap is discovered, in which case it's resolved the same way `docs/ROADMAP.md`'s
Cross-Phase Rule handles backend ambiguities: recorded in `CLARIFICATIONS.md` before the
dependent frontend code is written.

## Phase F0 — Design System & App Shell

Establish the visual language and reusable primitives every later phase composes:
design tokens (type scale, color system including shipment-status semantic colors,
spacing, motion), themed shadcn/ui component set, app shell (top nav, sidebar,
branch/org switcher), auth screens (login, forgot/reset if in scope), data table
pattern, status badge component, loading/empty/error state patterns, the BFF auth
route handlers (httpOnly cookie session, refresh rotation) described in the design
spec.

*Entry:* None (first frontend phase). *Exit:* a logged-in user lands on an empty
shell dashboard using only real auth against the Phase 1 API; every primitive listed
above exists, is themed (not default shadcn), and has at least one consuming page;
Vitest component tests cover the shell and auth flow; `docs/superpowers/specs/`
design doc's decisions are reflected in code, not just written down.

*Status (2026-08-29): Complete.* Implemented per
`docs/superpowers/plans/2026-08-29-frontend-phase-f0-design-system-app-shell.md`. A new
`GET /api/v1/auth/me` endpoint (backend `Identity` module, `CLARIFICATIONS.md` #F0.1) exposes
the logged-in user's permissions and branch grants — the gap the frontend design spec flagged
before any UI code was written. Design tokens (color, type, radius, status semantics) were
produced via the `frontend-design` and `impeccable` skills; the critique pass caught and fixed a
real defect before it shipped (the originally proposed in-transit amber failed WCAG's 3:1
non-text contrast minimum against the paper background, and the proposed RTO color sat too
close to the delivery-failed/NDR hue to distinguish at a glance — both corrected and verified
with computed contrast ratios). `StatusBadge` renders a neutral pill with a color-coded dot
rather than a saturated fill, since several status tokens aren't safe as text-on-fill at every
lightness. The auth BFF (Next.js Route Handlers holding JWT access/refresh tokens in `httpOnly`
cookies, never exposed to client JS), `usePermission`-gated app shell (top nav, sidebar, branch
switcher — the latter correctly renders nothing for a single/no-branch user), `DataTable`, and
empty/error/loading state primitives are all built and consumed by a real login → dashboard
flow, verified end-to-end against the real API (login, session hydration, permission-gated nav,
logout) — not just unit-tested. Two real bugs were caught during that verification and fixed:
a stale Docker image serving a 404 for the new endpoint (rebuilt), and a `vi.mock` hoisting
error in a test (fixed with `vi.hoisted`). Deferred to Phase F1: the actual organization
dashboard content (this phase's dashboard is intentionally just an `EmptyState` placeholder).

## Phase F1 — Auth Flows + Organization Dashboard

Full login/logout/session-expiry handling, permission-aware nav built on the
`usePermission` hook, and the organization dashboard (CLAUDE.md §29 / backend
`GET /api/v1/reports/dashboard/organization`): today's shipment funnel, NDR/RTO
counts, COD summary tiles.

*Entry:* F0 done. *Exit:* dashboard reflects real data end-to-end against the Phase
13 reporting API, correctly scoped to the logged-in user's organization; session
expiry redirects to login without a full page-state loss surprise.

*Status (2026-08-29): Complete.* Implemented per
`docs/superpowers/plans/2026-08-29-frontend-phase-f1-auth-flows-org-dashboard.md`. Fixed a real
Phase F0 bug first: `StatusBadge`'s status keys used `SCREAMING_SNAKE_CASE` from
`SHIPMENT-LIFECYCLE.md`'s prose, not the backend's actual PascalCase `ShipmentStatus` enum wire
format — every real status would have silently shown as "unknown" the moment this phase wired in
real data. Session-expiry is resolved as a **re-authentication dialog**, not a redirect (a stronger
outcome than this phase's own exit-criteria wording): a shared `clientFetch` wrapper throws
`SessionExpiredError` on any BFF-route 401 (which only ever means the refresh token itself failed —
a merely-expired access token is invisible to the client, refreshed transparently server-side per
Phase F0), a global TanStack Query `QueryCache.onError` catches it and flips a Zustand flag, and a
non-dismissable `ReauthDialog` (shadcn `Dialog`, hand-authored against this repo's existing Radix
conventions — the shadcn CLI's newer version prompts interactively for a component library with no
non-interactive flag this harness can answer) lets the user sign back in and resume the page in
place, no navigation. The organization dashboard (shipment-status funnel, COD financial summary,
top-line rate KPIs — branch/rider breakdowns stay deferred to Phase F8) was built with the
`dataviz` skill, which caught a second real defect before it shipped: the original
`--status-delivered` (forest-green) failed CVD adjacent-pair separation against `--status-ndr`'s
vermillion (ΔE 4.9 protanopia, below the legal floor) the first time both colors appeared
side-by-side in one view — shifted to a teal-green, and `--status-delivery-failed` was retired
entirely (it and NDR were two lightness steps of the same hue, ΔE 7.2 even for normal vision,
also below the legal floor) since the backend auto-transitions `DELIVERY_FAILED` to `NDR_CREATED`
in the same request anyway. Verified end-to-end against the real API: login → dashboard with real
data → logout → middleware redirect, plus the loading spinner state, all confirmed live in browser.
The specific mid-page "access token expired but refresh also fails" trigger wasn't reproduced live
(httpOnly cookies are deliberately unreachable from browser-automation JS — the security design
working as intended) but is covered by real component-mount integration tests (`providers.test.tsx`
exercises the actual `QueryCache` wiring with a real throwing query; `ReauthDialog.test.tsx`
exercises the dialog's own open/submit/resume behavior against a real fetch-mocked 401→200 login).

**Update 2026-08-29 (same day, post-completion polish):** Three follow-up passes on the dashboard
UI, all user-requested after F1 was marked done. (1) `impeccable`'s distill → clarify → polish
sequence: `RateTiles` was four separate small cards, breaking from the other two sections'
one-Card-per-section pattern (craft-floor's "same-size cards as page structure" default) —
unified; the rate KPIs' trailing-30-day window wasn't stated anywhere despite sitting under a
section titled "Today's," a real ambiguity — added a "Last 30 days" caption; themed `::selection`
(a browser default every other surface in the app already themes). (2) A dedicated `/impeccable
polish` pass found the app shell had zero mobile handling anywhere — no breakpoint classes, no
drawer, `Sheet` (built in Phase F0) sitting completely unused. Added `MobileNav` (hamburger +
`Sheet` drawer, `md:hidden`) and made the permanent `Sidebar` `hidden md:block`, extracting shared
`SidebarNav` so the two can't drift. This is `AppShell`/`Sidebar` scope, not dashboard-page scope,
but the dashboard couldn't be verified mobile-ready without it — confirmed with the user before
proceeding. (3) Redesigned the three dashboard components to match a reference screenshot the user
provided: icon badges + subtitles on section headers, a divider between the shipment funnel's two
rows, COD's `Merchant Payable` pulled into its own highlighted card, and tinted circular icon
badges on each rate tile — all colors sourced from tokens already in the palette (status colors,
`--accent`), not new ad hoc values, so the match stays consistent with the rest of the app rather
than forking its own visual language.

## Phase F2 — Branches + Branch Dashboard

Branch list/detail/create/update UI (Phase 2 + Phase 3 geography chain pickers),
branch dashboard (`BRANCH-OPERATIONS.md` §3: incoming/outgoing shipments, pending
sorting/dispatch, received/pending manifests, riders, COD, exceptions), branch
switcher wired to real `UserBranch` scope.

*Entry:* F1 done. *Exit:* a user with multi-branch access can switch branches and
see branch-scoped data change accordingly; a single-branch user never sees a
switcher; geography pickers validate the same chain the backend enforces.

*Status: Done (2026-08-29).* Branch list, create form (with cascading Province →
District → Local Level pickers, backed by `/api/geography/*` BFF routes), and
detail page (read-only record + editable Delivery Zone, gated by
`branch.update`) are live. The detail page composes `BranchShipments` and
`BranchExceptions` below the record, sourced from
`GET /api/v1/reports/dashboard/branch/{id}`. `BranchSwitcher` now navigates to
`/branches/{id}` on selection.

Manual E2E verification ran against the real API and Postgres (not mocks) and
found a real backend bug: `BranchType` had no `JsonStringEnumConverter`
registered, so `POST /api/v1/branches` 500'd on the frontend's string payload
and `GET` responses serialized `Type` as a raw integer (`BranchRecord.tsx`
would have rendered "1" instead of "PickupPoint"). Fixed by adding
`[JsonConverter(typeof(JsonStringEnumConverter))]` to the `BranchType` enum
itself (`src/Modules/Branches/Domain/BranchType.cs`) — scoped to this enum only,
per user decision; other enums (`ShipmentStatus`, `NdrStatus`, etc.) still
serialize as integers and will need the same fix if/when a future frontend
phase hits them the same way. Verified: full create → view → edit-delivery-zone
→ reload-persists flow in-browser; `BranchSwitcher` correctly renders nothing
for this org's single branch (multi-branch navigation not exercised — no
multi-branch test org was available this session).

## Phase F3 — Shipments

Shipment booking form (rate quote preview via Phase 5 pricing API, price is always
server-computed and read-only in the UI per CLAUDE.md §33), shipment list with
filters/pagination, shipment detail page with the tracking-event timeline and
custody history.

*Entry:* F2 done. *Exit:* a shipment can be booked, appears correctly in the list
with server-side pagination, and its detail page's timeline matches the backend's
`ShipmentEvent` history exactly (no client-side reconstruction of state).

*Status: Done (2026-08-29), with two deliberate scope reductions.* No tracking timeline (no
staff-facing `ShipmentEvent` endpoint exists) and no list filters (the list endpoint only accepts
`page`/`pageSize`) — both decided with the user before design and recorded in
`docs/CLARIFICATIONS.md` §14. Shipped: the shipment list (`/shipments`), a Sheet-based booking
flow with a debounced live price-quote preview (reusing `GeographyPicker` for two independent
Recipient/Sender instances), and a Sheet-based detail view — all following the F2-established
Sheet-over-pages convention (`docs/ARCHITECTURE.md` §9a), driven by a new `shipmentPanelStore`.

Manual E2E verification against the real API/Postgres found and fixed three real bugs, not caught
by any automated test:
1. **`GET /api/v1/pricing/reference/service-types` doesn't exist** — the actual route is
   `GET /api/v1/service-types` (a plain `/api/v1` reference group, not nested under `/pricing`).
   The BFF route's backend URL was corrected; the frontend-facing `/api/pricing/service-types`
   path itself was kept as-is since it groups sensibly with the other pricing BFF routes.
2. **`ShipmentStatus` had no `JsonStringEnumConverter`**, so `CurrentStatus` serialized as a raw
   integer — `StatusBadge` rendered literally "0" instead of "Created" for every shipment, on both
   the list and detail views. Unlike Phase F2's `BranchType` fix, this one couldn't be sidestepped
   by avoiding the field (Status is the one thing `StatusBadge` exists to render) — fixed the same
   way, narrowly, via `[JsonConverter(typeof(JsonStringEnumConverter))]` on the enum itself. Every
   other enum in the backend (`NdrStatus`, `CodCollectionStatus`, etc.) still has this latent bug
   and will need the same treatment whenever a future phase's own E2E pass reaches it.
3. **`GeographyPicker` hardcoded its internal element ids** (`id="province"`, `id="ward-number"`,
   etc.), which worked fine when a page only ever rendered one instance (Branches) but produced
   duplicate DOM ids — breaking `<label htmlFor>` association for the second instance — the moment
   a form needed two independent pickers on one page (Recipient + Sender here). Fixed by adding an
   optional `idPrefix` prop; Branches' single usage is unaffected since the prop defaults to no
   prefix. Any future feature reusing `GeographyPicker` more than once per form should pass a
   distinct `idPrefix` per instance.

Also found, not a code defect: `Kathmandu Metropolitan City`'s seed data only has wards 1-3 (same
limitation Phase F2 hit) — ward numbers used in manual testing must stay within that range.

## Phase F4 — Scanning + Manifests + Branch Transfer

Barcode/QR scan UI (camera-based where feasible, manual AWB entry fallback) for
receive/sort/dispatch/transfer operations, manifest create/seal/dispatch/receive
flow (`Manifest` lifecycle), missing-shipment reconciliation UI on manifest receive.

*Entry:* F3 done. *Exit:* a manifest can be built by scanning, sealed, dispatched,
and received with missing shipments correctly flagged, exercising the full Phase 7
manifest lifecycle through the UI only.

*Status: Done (2026-08-29).* Manual-AWB-entry scanning only — no camera-based scan UI, per a
decision recorded with the user in `docs/CLARIFICATIONS.md` §15 before design (matches every other
scan surface in this codebase; a camera scanner is a later roadmap candidate, not scoped here).
Shipped: the manifest list (`/manifests`, linked from the sidebar), a Sheet-based create flow, and
a single status-driven detail Sheet whose body swaps between five sections matching the backend's
`ManifestStatus` state machine — Draft (scan AWBs + seal), Sealed (vehicle plate/driver + dispatch),
InTransit (receive-scan), Received (missing-shipments panel + close), Closed (read-only) — all
following the F2-established Sheet-over-pages convention (`docs/ARCHITECTURE.md` §9a), driven by a
new `manifestPanelStore`. `docs/CLARIFICATIONS.md` §15 also resolved this phase's one real API gap:
`GET /api/v1/manifests` didn't exist before this phase (every other list-owning module had one) —
added with the same branch-scoped filtering shape as `ShipmentService.Reads.cs`'s `ListAsync`,
covered by new unit tests (`ManifestServiceListTests.cs`) and an integration test proving a
branch-scoped user with access to neither side of a manifest cannot see it in the list
(`ManifestsEndpointsTests.cs`).

Manual E2E verification against the real API/Postgres found and fixed three real bugs, not caught
by any automated test:
1. **`ManifestPurpose` and `ManifestStatus` had no `JsonStringEnumConverter`** — `POST
   /api/v1/manifests` 500'd outright (`System.Text.Json` can't deserialize `"purpose":"Forward"`
   into an enum without one), and every `GET` response would have serialized `status` as a raw
   integer, breaking `StatusBadge` the same way Phase F3's `ShipmentStatus` did. Fixed the same way,
   narrowly, via `[JsonConverter(typeof(JsonStringEnumConverter))]` on both enums. This is now the
   third enum this latent bug class has hit (`BranchType` F2, `ShipmentStatus` F3); every other
   backend enum still carries it and will need the same fix whenever a future phase's own E2E pass
   reaches it.
2. **The five manifest-action mutation hooks (`useScanManifestShipment`, `useSealManifest`,
   `useDispatchManifest`, `useReceiveScanManifestShipment`, `useCloseManifest`) only invalidated
   `queryKey: ["manifests", manifestId]`** — a sibling of the list's `["manifests", branchId,
   status]` key, not a parent of it, so TanStack Query never refetched the list. Scanning, sealing,
   dispatching, receive-scanning, or closing a manifest updated its own detail Sheet correctly but
   left `/manifests` showing the stale status and shipment count until a full page reload.
   `useCreateManifest` already invalidated the broader `["manifests"]` key; the other five now match
   that pattern.
3. Setting up a realistic end-to-end scenario (org → two branches with delivery zones → merchant →
   rate card + zone rate → shipment → rider pickup → receive-origin, all prerequisites for a
   shipment to reach the `AtOriginBranch` state manifest scanning requires) surfaced no further
   defects, but confirms the chain documented in `BRANCH-OPERATIONS.md` §1 and
   `SHIPMENT-LIFECYCLE.md` is exactly as long as it looks — a fresh shipment is not scannable onto a
   manifest until it has been assigned to a rider, pickup-scanned, and received at its origin
   branch.

Also added (not a defect, an omission in Task 11's plan step 1): `/manifests` was missing from
`Sidebar.tsx`'s `NAV_ITEMS`, gated on `manifest.read` like every other entry — without it the route
existed but had no way to reach it from the UI.

*Status: Not started.*

## Phase F5 — Riders + Delivery + POD

Rider assignment UI, delivery run/task views, delivery attempt recording (reason
codes from CLAUDE.md §19), POD capture (signature/photo/OTP/GPS — desktop-friendly
approximation; a dedicated rider mobile-web experience is a later roadmap
candidate, not required here).

*Entry:* F4 done. *Exit:* a delivery attempt (success or failure) can be recorded
end-to-end with POD captured on success, matching Phase 8's backend contract.

*Status: Done (2026-09-01).* Desktop-friendly POD approximation only, per the design spec's
resolved scope: a hand-drawn `<canvas>` signature pad and a file-input photo picker (with
`capture="environment"` as a mobile-camera hint) stand in for a dedicated rider mobile-web
experience, which remains a later roadmap candidate. Shipped: a `/riders` list page (create-rider
Sheet, sidebar entry gated on `rider.read`) with an inline Vehicles section (create-vehicle Sheet);
an AWB-lookup-based Assign Task flow (no dispatcher overview board or run-history view, both
deliberately out of scope per the design spec) that infers Pickup vs. Delivery from the shipment's
current status and filters the rider picker to active riders at the relevant branch; a
`/my-deliveries` rider self-service page (gated on `delivery.attempt`) showing only today's active
run's pending tasks, each enriched with recipient/AWB/COD details; a pickup-scan action; and a
Record Attempt Sheet with a success/failure toggle — the failure branch captures a CLAUDE.md §19
reason code and notes, the success branch captures recipient name, OTP confirmation, a drawn
signature, an optional photo, best-effort GPS (via `navigator.geolocation`, silently skipped if
denied/unavailable), and a required collected-COD amount whenever the task has a nonzero
`codAmount`. Three real backend gaps were resolved before frontend work began (Tasks 1-4): all 5
Riders enums (`DeliveryTaskType`, `DeliveryTaskStatus`, `DeliveryRunStatus`, `RiderRoleType`,
`DeliveryAttemptReason`) got `JsonStringEnumConverter` in one pass, closing the same latent bug
class hit narrowly in every prior phase (`BranchType` F2, `ShipmentStatus` F3, `ManifestPurpose`/
`ManifestStatus` F4) before it could bite this phase too; a new `IShipmentTaskLookupService`
narrow-interface (matching this codebase's established cross-module lookup pattern) lets Riders
enrich a `DeliveryTask` with its shipment's AWB/recipient/COD without touching Shipments' DbSet
directly; and `GET /api/v1/riders/me` / `GET /api/v1/riders/me/tasks/today` were added for
rider-self-lookup, since no prior phase needed a "who am I" path from a logged-in rider's `UserId`
to their own `Rider` record.

Manual E2E verification against the real API/Postgres (both servers run live, driven through the
real Next.js BFF proxy layer with real login cookies, not the mocked `clientFetch`/`apiFetch` every
unit test uses) walked the complete scenario end-to-end: create both rider types, assign a pickup
task via AWB lookup, log in as that rider, scan the pickup, move the shipment to
`AtDestinationBranch` via the real Manifests flow, assign a delivery task to a second rider, log in
as them, record a failed attempt (confirmed the shipment reached `NdrCreated` with custody
correctly retained by the rider, per Phase 9's design), and — on a second COD shipment — complete a
delivery with a drawn signature, a photo, OTP checked, real GPS coordinates, and a collected COD
amount, confirming `Delivered` status, cleared custody, a real `CodCollection` row with the correct
`collectedAmount`, and the completed task correctly disappearing from `GetMyTasksTodayAsync`'s
pending-only list. Unlike every prior phase's E2E pass, this one found no defects in the code this
phase touched — a clean result, plausibly because Task 1's enum fix was applied proactively (all 5
at once) rather than discovered reactively through a 500 during this pass, the way F2-F4's enum
bugs each were.

One confirmed-correct scope boundary surfaced during E2E, not a defect: assigning a delivery task
after an NDR fails with a 400 (`Shipment must be AtDestinationBranch ... was NdrCreated`) — a
shipment cannot be reassigned for delivery straight out of `NdrCreated`. Per CLAUDE.md §20, an NDR
must pass through its own resolution workflow (`REATTEMPT_SCHEDULED`, etc.) to return the shipment
to a deliverable state first, and that workflow's UI is Phase F6's responsibility, not this phase's
— the Assign Task Sheet here is intentionally scoped to fresh tasks only (`Created` or
`AtDestinationBranch` shipments). Also observed, and explicitly left unfixed as out of this phase's
scope (a pre-existing Phase 3 Geography gap, not something Riders' own code touches):
`DeliveryZoneService.ValidateAndLoadAreasAsync` throws an unhandled `NullReferenceException` (500)
rather than a validation 400 when `CoveredAreaIds` is omitted from `POST /api/v1/delivery-zones` —
worth a narrow fix whenever a future phase's own E2E pass reaches that endpoint.

## Phase F6 — NDR + RTO

NDR queue/workflow UI (status transitions per CLAUDE.md §20), RTO tracking linked
to the original shipment (CLAUDE.md §21).

*Entry:* F5 done. *Exit:* an NDR can be worked from `OPEN` through to
`RESOLVED`/`RTO_REQUESTED` entirely through the UI, and an RTO shipment's linkage
back to its original AWB is visible on both records.

*Status: Done (2026-09-01).* Shipped the full NDR workflow (`/ndrs` list + status-driven detail
Sheet — `NdrUpdateSection` for `Open`/`Contacting`/`CustomerConfirmed`'s forward moves,
`NdrReattemptSection`/`NdrRtoSection` for the two resolution paths off `CustomerConfirmed`, an
always-available `NdrResolveOtherAction`, and a `NdrResolvedSummary` linking to the resulting
return shipment) and RTO tracking (`/return-shipments` list + `ReturnShipmentDetailSheet` with a
Confirm Return action gated on both `rto.manage` and the live shipment status actually being
`RtoAtOrigin`, matching the backend's own state guard). Both `ShipmentDetailSheet` and
`ReturnShipmentDetailSheet` now show the AWB linkage in both directions, closing this phase's exit
criteria.

Five real backend gaps were resolved before frontend work began (Tasks 1-5, recorded in
`docs/CLARIFICATIONS.md` §16): all 3 Ndr enums (`NdrStatus`, `NdrReason`, `NdrResolution`) got
`JsonStringEnumConverter` in one pass, closing the same latent bug class hit narrowly in every
prior phase; `NdrResponse`/`ReturnShipmentResponse` were enriched with `Awb`/`OriginalAwb`, sourced
from the `ShipmentScanInfo` lookup those methods already performed or now perform once per row; and
`GET /api/v1/ndrs`/`GET /api/v1/return-shipments` gained `shipmentId`/`originalShipmentId` filters,
letting the shipment detail Sheet discover its own NDR history and RTO linkage.

One deliberate deviation from the design spec: `NdrResolveOtherAction` ships without the
short-reason-note field the spec described, since `NdrService.ResolveAsync`'s `Other` branch never
reads or persists `ResolveNdrRequest.RtoReason` for that resolution — no `NDR` column exists to
hold it. Building an input for a value the backend silently discards would have been misleading.

Manual E2E verification against the real API/Postgres: clean pass, no real bugs found. Registered a
fresh org via `/api/v1/auth/register`, seeded branches/zones/rate card/merchant/riders via the API
(the exact request shapes `NdrEndpointsTests.cs` already uses), then drove three real shipments
through pickup → manifest → failed delivery → NDR entirely through the live UI at
`localhost:3000`/`localhost:5013` against the shared Postgres container: one resolved via Request
RTO (walked the return leg through a real `Purpose: Return` manifest — dispatch → receive-scan —
and confirmed the "Confirm Return" button stays disabled until the shipment actually reaches
`RtoAtOrigin`, then completes it to `RtoDelivered`), one via Schedule Reattempt (verified the
shipment returns to `OutForDelivery`), and one via Resolve — Other from `Open` (verified it
resolves without touching shipment status, per the spec deviation above). `ShipmentDetailSheet`
showed both "View NDR" and "View Return Shipment" linkage buttons throughout. Branch-restricted
access filtering (exit-criteria item 10) was verified via the already-passing
`ListAsync_BranchRestricted_FiltersToAccessibleBranchOnly` and
`ListReturnShipmentsAsync_BranchRestricted_FiltersToAccessibleBranchOnly` unit tests rather than a
second live-UI pass with a second restricted user, since a live-UI branch-restriction check
exercises identical frontend code to the unrestricted passes above and the backend scoping logic
itself is what those tests already isolate and assert against real Postgres.

## Phase F7 — COD Operations

COD collection views, rider cash settlement, branch cash settlement,
reconciliation screens surfacing expected/collected/deposited/reconciled amounts
per CLAUDE.md §22–23, with all adjustment actions requiring a reason and showing
before/after values.

*Entry:* F6 done. *Exit:* the full COD chain (`COD_EXPECTED` → `SETTLED`) is
visible and actionable through the UI, matching the backend's already-proven
Phase 10 chain; every adjustment UI action requires and displays a reason.

*Status: Complete.* Shipped `CollectionsTab`, `RiderSettlementsTab` (+create/detail
Sheets), `BranchSettlementsTab` (+create/detail Sheet with an embedded Finance
Verify action, since no `FinancialTransaction` list endpoint exists),
`MerchantSettlementsTab` (+create/detail Sheet with Settle and per-line-item
Request Adjustment), `AdjustmentsTab` (+detail Sheet with Approve), and a
`ReconciliationTab` surfacing CLAUDE.md §22–23's six summary figures — all under
the existing `/cod` nav entry with a plain button-row tab switcher (no new Tabs
library). 4 backend gaps closed first: 3 Cod enums missing
`JsonStringEnumConverter`, no list endpoints for `RiderCashSettlement`/
`BranchCashSettlement`/`Adjustment` (flagged in Phase 10's own final review as
worth surfacing here), and a missing guard letting `ApproveAdjustmentAsync`
silently rewrite `TotalPayable` on an already-`Settled` `MerchantSettlement`. See
`docs/CLARIFICATIONS.md` entry #17 and
`docs/superpowers/specs/2026-09-01-frontend-phase-f7-cod-operations-design.md`
for full detail.

Validating the new `Cod.*` status-badge token group (via the dataviz skill)
surfaced a second, pre-existing bug: `--status-in-transit` and
`--status-out-for-delivery` are byte-identical in dark mode (ΔE 0.0, not just a
marginal floor miss), already affecting every `ShipmentStatus` badge using these
tokens today. The `Cod.*` chain avoids `--status-in-transit` entirely — a
brute-force search over the 7 candidate tokens' orderings found an arrangement
using `--status-sealed`/`--status-received`/`--status-rto` instead that cleanly
passes both the normal-vision and CVD floors for every adjacent pair. Root-cause
fix (re-deriving a correct third amber shade for dark mode, app-wide) parked in
`docs/CLARIFICATIONS.md` entry #17 as its own properly-scoped design task.

Manual E2E verification (registered a fresh org via the API, seeded
branches/zones/rate-card/merchant/riders via the API using the exact shapes
`CodEndpointsTests.cs` already uses, then drove a real COD shipment through the
live UI across all 6 tabs including an adjustment + settle) found and fixed one
real bug: `MerchantSettlementCreateSheet` sent a bare `"YYYY-MM-DD"` from
`<input type="date">` with no explicit offset — .NET's `DateTimeOffset` JSON
deserializer then defaulted to the API host's *local* timezone offset (this host
runs in Nepal Standard Time, UTC+5:45), and Npgsql's `timestamptz` writer rejects
any non-zero offset outright, so every real merchant-settlement creation 500'd.
Fixed by anchoring both period dates to explicit UTC (`T00:00:00Z`/`T23:59:59Z`)
before they leave the create-Sheet. Branch-scoping for the 3 new list endpoints
was verified via the existing branch-restricted unit/integration test coverage
(Tasks 2–4, 6) rather than a full second-user UI walkthrough, since those tests
already exercise the identical `_branchAuthorizationChecker` code path this
phase's endpoints reuse verbatim from `ListCollectionsAsync`.

## Phase F8 — Reports & Analytics

Full dashboard build-out beyond F1's org-dashboard slice: branch performance,
rider performance, delivery success/NDR/RTO rates, CSV export UI (Phase 13's
streaming export endpoints).

*Entry:* F7 done. *Exit:* all Phase 13 reporting/export endpoints have a
corresponding UI, no hardcoded/demo data paths remain.

*Status: Not started.*

## Phase F9 — RBAC / Admin

Role and permission management screens, `UserBranch` assignment UI (including
temporary/expiring grants), user invite/deactivate management.

*Entry:* F8 done. *Exit:* an OrganizationAdmin can create a role, assign
permissions, and grant a user branch access (including a temporary grant) entirely
through the UI.

*Status: Not started.*

## Later (not yet planned in detail)

- **Phase F10 — Merchant Portal** (booking, tracking, COD settlements, invoices,
  API key management — the original backend Phase 12b scope).
- **Phase F11 — Public Tracking** (`/track/{awb}`, unauthenticated, withholds the
  fields listed in CLAUDE.md §24).
- **Phase F12 — Rider Mobile-Web** (if F5's desktop approximation proves
  insufficient for real field use).

These get their own brainstorm → spec → plan cycle once F0–F9 are done, per this
repo's standing workflow.
