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

*Status: Complete.* Closed the 4 real gaps found while investigating: `OrgDashboardResponse.RiderPerformance`
was already returned by the backend but silently dropped by the frontend (new `RiderPerformanceChart`,
structurally identical to `BranchPerformanceChart`, labeling riders by `licenseNumber` per every other
rider-identifying UI in this app); the organization dashboard had no date-range control despite the
backend already supporting one (new From/To filter, UTC-anchored per Frontend Phase F7's own lesson,
also fixing `RateTiles`' previously-hardcoded "Last 30 days" caption to reflect the active range); and
neither CSV export endpoint (`/export/shipments`, `/export/cod-summary`) had any frontend UI or a
`Content-Disposition` header (fixed on the backend first — without it, browsers try to render the CSV
instead of downloading it), closed with a new `ExportReportsCard` (branch + date-range filters, two
download links via plain browser navigation to a BFF passthrough route) gated on `report.export`. See
`docs/superpowers/specs/2026-09-01-frontend-phase-f8-reports-analytics-design.md` for full detail.

Manual E2E verification against the real API/Postgres (fresh org, seeded branches/zones/rate card/
merchant/riders, one shipment run through pickup → manifest → delivery with COD collected) found one
real bug in `RateTiles.formatDuration`: it split .NET's TimeSpan "c"-format string on the first "."
assuming that character was always the day separator, so a day-less duration that still carries
fractional seconds (the common case, e.g. `"00:00:02.8836000"`) rendered as `"8836000h NaNm"` on the
dashboard's Key Rates card. Fixed with a regex that only treats a leading `d.` as a day part when it's
actually followed by `hh:mm:ss`, with regression tests for both the day-less and with-day cases.
Confirmed the Rider Performance chart, the date-range filter (including its empty-range and
reset-to-real-data behavior), both CSV exports (content, `Content-Disposition` filename, and
branch-scoped filtering), and the `report.export` permission gate (enforced server-side; the frontend
card returns `null` outright when absent, matching this codebase's `canCreate`/`canUpdate` convention).

## Phase F9 — RBAC / Admin

Role and permission management screens, `UserBranch` assignment UI (including
temporary/expiring grants), user invite/deactivate management.

*Entry:* F8 done. *Exit:* an OrganizationAdmin can create a role, assign
permissions, and grant a user branch access (including a temporary grant) entirely
through the UI.

*Status: Complete.* Shipped an `/admin` page with permission-gated `RolesTab`
(list, create, edit-permissions, delete) and `UsersTab` (paginated list, invite,
deactivate/reactivate, per-user branch grants with an optional expiry) — each tab's
own Sheets (`RoleCreateSheet`/`RoleDetailSheet`, `UserInviteSheet`/
`UserDetailSheet`) plus a `UserBranchGrants` panel. The sidebar's `Admin` entry is
visible to `role.manage` OR `user.read`; the page itself gates each tab
independently and defaults to whichever tab the caller can actually see, since the
backend's per-endpoint permission check is stricter than that OR. New public
`/accept-invite?token=` page lets an invited user set their password and lands
them signed in.

8 backend gaps closed first (`docs/CLARIFICATIONS.md` entry #18): `BranchScope`
had no `JsonStringEnumConverter`; role default-seeding covered only 3 of 14
non-platform roles (`AdditionalRoleSeeds` now seeds all 14, grounded in
`docs/RBAC.md` §4); no `/roles` or `/permissions` endpoints existed at all; no
paginated `/users` list, per-user branch grant list/grant/revoke, or
deactivate/reactivate endpoints existed; no invite flow existed end-to-end
(`UserInvite` entity + migration, `POST /users/invite`, `POST
/auth/accept-invite`, a `user.invited` notification template, and `Identity`
gaining its first dependency on `Events` for `IOutboxWriter`). See
`docs/superpowers/specs/2026-09-02-frontend-phase-f9-rbac-admin-design.md` and
the accompanying plan for full detail.

A whole-branch review (10 findings) caught two CRITICAL cross-tenant gaps before
merge: `GrantBranchAsync` and `InviteUserAsync` both wrote a `UserBranch`/
invite-scoped grant against a caller-supplied `BranchId` without ever checking it
belonged to the caller's own organization — a org admin could grant (or invite a
user into) another tenant's branch by ID. Both now validate the branch through
`IBranchLookupService` first and write nothing if it doesn't resolve, with
regression tests. Also fixed: `admin/page.tsx`'s tab-gating (see above),
`AcceptInviteForm`'s fetch call missing a try/catch on this public unauthenticated
page, and a consistency-only `OrganizationId` filter added to
`RoleService.DeleteRoleAsync`'s still-assigned check.

Manual E2E verification against the real API/Postgres/Mailhog (fresh org
registration confirmed all 14 default roles seed correctly; created a custom role
with a permission subset, then edited and attempted-deleted it while assigned —
correctly rejected; invited a user by email and retrieved the real invite email
from Mailhog with a correctly-scoped one-time token; accepted the invite, then
confirmed login with the new password and that the token cannot be reused; granted
a second branch with a near-immediate expiry and confirmed the branch-scoped
`branch.read` list correctly excluded it once expired while keeping the
unexpired grant; deactivated the user and confirmed both login and the
already-issued refresh token were rejected, then reactivated and confirmed both
work again) found no further bugs beyond the review's two CRITICAL findings above.

## Phase F10 — Merchant Portal

Booking, tracking, COD settlements/invoices, KYC, staff management, and API key
management for merchants — the original backend Phase 12b scope, on top of
Phase 12's merchant JWT/API-key track.

*Entry:* F9 done. *Exit:* a merchant can log in through a session fully
separate from staff, book a shipment with a live price preview, track it
through delivery, raise and cancel pickup requests, view and export their COD
settlements as invoices, upload and track KYC documents, invite staff (Owner
only), and manage API keys (Owner only).

*Status: Complete.* Shipped a full `MerchantAppShell` (session cookie
separate from the staff track, permission-gated `MerchantNavMain`) with six
feature pages under `/merchant`: `Shipments` (booking Sheet with a live
debounced price-quote preview, list, detail Sheet, cancel), `Pickups`
(request/cancel), `COD & Invoices` (settlement list, printable/exportable
invoice Sheet), `KYC` (document upload with client-side type/extension
validation), `Staff` (Owner-only invite, list), and `API Keys` (Owner-only
create with a one-time plaintext reveal, scope selection, revoke). 16 tasks
executed via `superpowers:subagent-driven-development`
(`docs/superpowers/plans/2026-09-03-frontend-phase-f10-merchant-portal.md`),
plus a route-tree fix mid-plan (Next.js route-group vs. real segment
collision between `(merchant)` and `(staff)`) and a final whole-branch review.

4 backend gaps closed first: `KycDocumentType`/`MerchantUserRole`/
`PickupRequestStatus` missing `JsonStringEnumConverter` (the same recurring
bug class as every prior phase); `GET /api/v1/merchant/auth/me`; making
`/service-types` and `/pricing/quote` dual-auth (staff JWT or merchant
JWT/API-key, forcing `MerchantId` server-side on the merchant path); and `GET
/api/v1/merchant/branches` for the booking form's origin/destination picker.

**Real bugs found only by a genuine live E2E pass (dotnet run + npm run dev
against real Postgres, not mocks or unit tests) — the only verification this
Sheet/list UI surface had, since Tasks 9-15 shipped with no dedicated
component tests for the Sheets themselves:**
1. `LocalLevelType` also missing `JsonStringEnumConverter` — the recurring
   bug class hit an 8th time (`docs/CLARIFICATIONS.md` #19 already covered
   the three F10-specific enums; this is a 4th, discovered only once the
   booking Sheet's Recipient/Sender `GeographyPicker` was actually driven
   live) — every local-level select rendered an empty `"City ()"` type
   suffix.
2. The merchant booking form's `GeographyPicker` calls
   `/api/v1/geography/*`, which was staff-only — a merchant JWT/API-key got
   403 for every province/district/local-level lookup, permanently blocking
   the Recipient/Sender address section. Fixed with dual-auth on
   `GeographyEndpoints` (mirroring `PricingEndpoints`'s established pattern)
   plus a new `merchant.geography.read` permission and parallel
   `/api/merchant/geography/*` BFF routes.
3. A blank COD Amount field serializes to `NaN` via React Hook Form's
   `valueAsNumber: true`, and `JSON.stringify(NaN)` is the literal `null`,
   500ing the backend's non-nullable `PriceQuoteRequest.CodAmount` — hit on
   both the merchant and (latent, previously unexercised) staff booking
   Sheets. Fixed with a shared `toSafeQuoteAmount` coercion.
4. The backend's `IdempotencyMiddleware` requires an `Idempotency-Key` header
   on every merchant-JWT POST to `/shipments`, `/shipments/{id}/cancel`, and
   `/pickups` (staff JWT is exempt) — none of the merchant BFF routes or
   their mutation hooks ever sent one, so every booking/cancel/pickup attempt
   400'd with `IDEMPOTENCY_KEY_REQUIRED`. Fixed with a shared
   `generateIdempotencyKey()` helper wired into all three mutation hooks and
   their BFF routes.
5. Every merchant BFF route unconditionally called `await response.json()`
   on the backend response; a bare `Results.Forbid()`/`Results.Unauthorized()`
   returns an empty body, which throws on parse and was being swallowed into
   a hardcoded generic 401 — masking the backend's real 403 for a Staff-role
   session hitting an Owner-only route (`/merchant/staff`, `/merchant/api-keys`).
   Fixed uniformly across all 16 merchant BFF route files with
   `.catch(() => null)` on the parse, preserving the real status code.

All 5 fixed directly, each as its own `fix:` commit, with regression tests
added where the bug class was testable (`GeographyEndpointsTests`'
`GetProvinces_MerchantOwnerToken_Succeeds`/`GetDistricts_..._FiltersByProvince`
and a raw-JSON-body assertion for the enum fix; `toSafeQuoteAmount.test.ts`;
per-route Idempotency-Key-forwarding tests on the shipments/cancel/pickups
BFF routes plus their mutation hooks; a new `staff/route.test.ts` — this BFF
route had none before — covering the empty-body-403 case specifically).

Manual E2E verification drove the full merchant journey end-to-end: Owner
login → book a shipment with a live NPR quote preview → raise and cancel a
pickup → (staff side) pickup-scan, manifest transfer, COD delivery → rider,
branch, finance, and merchant COD settlement, confirmed `Settled` with the
correct `NPR 2,400.00` total payable (COD 2,500 − courier fee 100) both via
the API and the invoice Sheet's line item → KYC upload accepted for PDF/JPEG/
PNG, a `.txt` rejected client-side with zero network request → staff invite,
confirmed a Staff-role session's nav correctly hides `Staff`/`API Keys` and a
direct navigation to `/merchant/staff` is genuinely blocked server-side (not
just hidden client-side) → API key create/reveal-once/revoke lifecycle,
confirmed the plaintext is never present in `GET /api/v1/merchant/api-keys`'s
response and cannot be recovered after the create Sheet closes.

**Deliberately deferred, not fixed:** the CSV invoice download's
formula-injection guard (`toCsv`, shipped in a prior COD phase) was not
re-verified against a real spreadsheet application during this pass — parked
consistent with the existing test-only verification noted for that guard.

**Final whole-branch review (opus, `master..HEAD`) found 9 items; 3 fixed in
one fix-wave commit, 6 parked.** All 3 fixed items trace back to the same root
cause — `MerchantPermissionCatalog` gaining `GeographyRead` (this phase's own
Task 16 E2E fix) without propagating to every place that enumerates the
merchant permission set: `MerchantPermissionCatalogTests.
Owner_HoldsAllFifteenPermissions` was a genuinely failing test on HEAD
(`Expected: 15, Actual: 16`) — the count and `Contains` list were never
updated; `MerchantApiKeyCreateSheet`'s 14-entry scope picker was missing
`merchant.geography.read`, so a merchant narrowing a new API key's scopes
could never grant it geography access even though an unscoped key could; and
(found during the same read, not GeographyRead-related) `codAmount` in the
booking schema is a required `z.number()` with no default — a blank input
reaches Zod as `NaN` via `valueAsNumber`, forcing every non-COD booking to
explicitly type "0", which the live quote preview on the same field already
tolerated via `toSafeQuoteAmount` but the actual submit-time validation did
not. Fixed with a `setValueAs` coercion at the `register()` call (not a
`z.preprocess`, which breaks the resolver's inferred type against `useForm`'s
declared `FieldValues`).

**6 items deliberately deferred, not fixed — worth a glance in a future
phase or a small F10.1 patch:** (1) no merchant-specific reauth/session-expiry
recovery UI exists — `MerchantAppShell.tsx` never mounts a `ReauthDialog`
equivalent, so a merchant session that actually expires mid-use leaves every
page's queries silently erroring with no path back to a working session
short of manually navigating to `/merchant-login`; the existing
`ReauthDialog` is hardcoded to the staff `/api/auth/login` route and
`useSession()`, so this needs a dedicated merchant variant, not just
re-mounting the existing one. (2) The staff-vs-merchant dual-auth check
(`HasPricingReadAccessAsync`/`HasGeographyReadAccessAsync` and inline logic
in `ShipmentsEndpoints.cs`) is hand-rolled independently three times rather
than through one shared filter — a future merchant-scoped endpoint will
likely need a fourth copy. (3) The `merchant.geography.read` permission
string is checked in `GeographyEndpoints.cs` via a hardcoded literal rather
than `MerchantPermissionCatalog.GeographyRead` (to avoid a Geography→
Merchants circular reference), with only a code comment — not the compiler —
keeping the two in sync. (4) All ~16 merchant BFF `route.ts` files repeat an
identical fetch/parse/status-forward/catch block (the shape this phase's own
empty-body-403 fix had to be retrofitted across one file at a time) with no
shared `proxyJson()` helper. (5) `MerchantShipmentCreateSheet.tsx` and its
siblings are a near-total copy-paste of the staff `ShipmentCreateSheet.tsx`
family; the `codAmount` bug above already shows the duplication lapsing in
practice (fixed on the merchant copy, latent on the staff copy, out of this
phase's scope). (6) `MerchantDashboardStats.tsx` fetches only the first 100
shipments (`useMerchantShipments(1, 100)`) to compute its tile counts —
already documented in-code as an accepted no-new-backend-read tradeoff, but a
merchant with more than 100 shipments gets silently undercounted tiles with
no indication the numbers are partial.

## Phase F11 — Public Tracking

A standalone, unauthenticated `/track` (AWB search) + `/track/[awb]` (results
timeline) surface over the existing backend `GET /track/{awb}` endpoint
(built in backend Phase 12), withholding the fields listed in CLAUDE.md §24.

*Entry:* F10 done. *Exit:* a visitor with only an AWB can open `/track`,
enter it, and land on `/track/{awb}` showing the current status and a
chronological event timeline — no login, no cookies, no app-shell chrome —
and the public endpoint's per-IP rate limit works correctly once real
browser traffic routes through the Next.js BFF.

*Status: Complete.* Spec at
`docs/superpowers/specs/2026-09-04-frontend-phase-f11-public-tracking-design.md`,
plan at
`docs/superpowers/plans/2026-09-04-frontend-phase-f11-public-tracking.md`,
8 tasks executed inline via `superpowers:executing-plans`, TDD throughout.

2 backend gaps closed first, found while inspecting the existing (Phase 12)
`/track/{awb}` endpoint rather than during any live E2E pass:
1. `PublicTrackingStatus` missing `JsonStringEnumConverter` — the same
   recurring bug class hit in every prior phase for a different enum
   (`ShipmentStatus`, `BranchType`, `KycDocumentType`/`MerchantUserRole`/
   `PickupRequestStatus`, etc.); this one had simply never been touched
   until now.
2. The IP rate limiter (`IpRateLimitFilter`) read
   `Connection.RemoteIpAddress` directly — once real browser traffic routes
   through the Next.js BFF (required, since the backend has no CORS policy
   for direct browser calls), every visitor would have collapsed into one
   shared rate-limit bucket keyed on the BFF's own connection IP, a
   self-inflicted DoS flagged as a known-deferred risk in Phase 12's final
   review (`docs/CLARIFICATIONS.md` §13.1). Fixed with a configuration-driven
   `ForwardedHeadersMiddleware` (`ForwardedHeaders:KnownProxies`/
   `KnownNetworks`, empty by default in `appsettings.json` — a production
   deployment must explicitly configure its real load balancer/ingress
   network) plus the BFF route forwarding an inbound `X-Forwarded-For`
   verbatim. Deliberately scoped so an untrusted direct caller can't spoof
   the header to bypass the limit — verified with a dedicated integration
   test proving exactly that.

New `features/tracking/` folder: `useTrackShipment` (a dedicated fetcher, not
the shared `clientFetch`, since public tracking needs a real 404-vs-429
distinction and the actual `Retry-After` value that `clientFetch` discards),
`TrackingSearchForm`, `TrackingTimeline`, and new `"Public.*"` entries in the
shared `statusConfig.ts` namespace (reusing the existing `StatusBadge`
component rather than a bespoke one).

**Verified against the real backend end-to-end** (registered a fresh org via
the API, seeded branches/zones/rate-card/merchant/rider, booked a real
shipment, drove it through pickup → origin-receive → manifest scan/seal/
dispatch/receive to `AtDestinationBranch`, then hit the public `/track/{awb}`
endpoint both directly and through the Next.js BFF route): the public status
serializes as a string and collapses the internal event history correctly
(4 public events from 7 internal `ShipmentEvent` rows), no excluded field
(branch id, staff/rider identity, financial fields) appears anywhere in the
response, an unknown AWB returns 404, and the BFF correctly forwards an
inbound `X-Forwarded-For` header. No real bugs found in this pass — every
DTO field-name mismatch was caught and fixed while writing the verification
script itself, not after.

**One thing this session could not verify:** the actual `/track` and
`/track/[awb]` React pages rendered in a browser. This sandbox has no
outbound network access, and `next/font/google` in the root `layout.tsx` —
shared by every page in the app, not just this phase's — hangs indefinitely
retrying its font fetch during dev-server compilation of any page route
(confirmed twice, independent of any change in this phase; API routes like
`/api/track/{awb}` compile and serve fine, since route handlers never import
the root layout). The full frontend test suite (275 tests, covering every
loading/success/404/429/error state with mocked data) plus the real
backend+BFF verification above are the evidence this phase's UI logic is
correct; the actual pixels were never seen rendered this session. Worth a
quick visual confirmation in an environment with normal network access
before or shortly after this ships.

## Phase F12 — Rider Mobile-Web (PWA)

Replaces the F5 desktop-approximated rider self-service surface
(`(staff)/my-deliveries`) with a real installable, offline-capable,
camera-scanning mobile-web PWA at `/rider/*`, reusing the existing BFF/
httpOnly-cookie session unchanged. Every rider write (pickup scan, delivery
fail, delivery complete) goes through an IndexedDB-backed outbox queue
(`features/rider-app/sync/`) instead of a direct fetch, drained on
reconnect/foreground/a 30s poll.

*Entry:* F5 done, desktop approximation confirmed insufficient for real
field use (no offline support, no camera scanning, not installable).
*Exit:* a rider can install `/rider/tasks` to a phone home screen, see
today's tasks, scan an AWB (camera or manual entry), record a delivery
attempt (signature/photo/GPS/COD) or a failure, and have every one of those
writes survive a real network outage — queued locally, replayed exactly
once on reconnect, never duplicated even if a queued item's own success
response never reached the client.

*Status: Complete.* Spec at
`docs/superpowers/specs/2026-09-04-frontend-phase-f12-rider-mobile-web-pwa-design.md`,
plan at
`docs/superpowers/plans/2026-09-04-frontend-phase-f12-rider-mobile-web-pwa.md`,
14 tasks executed via `superpowers:subagent-driven-development` in an
isolated worktree, TDD throughout.

**1 backend gap closed first** (Task 1), found while designing the outbox's
replay contract rather than during any live E2E pass: `IdempotencyMiddleware`
protected only merchant-JWT routes (`POST /shipments`, `/pickups`, cancel).
A rider's offline-queued mutation can be replayed after a false-negative
network failure (the request actually succeeded server-side but the client
never saw the response), so the four rider action routes
(`scan/pickup`, `delivery-attempts/fail`, `delivery-attempts/complete`,
`pod-uploads`) now require the same `Idempotency-Key` header, partitioned
per staff user (`staff:{userId}:{key}`) rather than per merchant. 6 more
pre-existing backend test files beyond the plan's own list turned out to
call these routes as setup helpers and needed the identical mechanical
header fix (`Cod`/`Manifests`/`Ndr`/`PublicTracking`/`Reports`/
`MerchantApi` *EndpointsTests.cs) — applying the fix to every affected call
site was correct, not scope creep.

Tech: **Serwist** (`@serwist/next`) for the PWA/service worker — not
`next-pwa` (archived since Aug 2023); **`idb`** for the outbox's IndexedDB
access — not Dexie/RxDB (one table, no general sync engine needed); native
`BarcodeDetector` for camera scanning, with `@zxing/browser` dynamically
imported only when unavailable (confirmed in the production build:
`/rider/scan` builds at 6.11 kB, proving zxing never enters the eager
bundle). New code lives entirely under `features/rider-app/` and
`app/rider/`; the old `features/riders/*` hooks/components/store and
`(staff)/my-deliveries` page were deleted in Task 13 once nothing else
referenced them, and the staff sidebar's "My Deliveries" link now points at
`/rider/tasks` instead (kept as a legitimate secondary entry point — a
rider using a shared office PC before ever installing to a phone home
screen).

One real, load-bearing bug found and fixed during Task 11's review (not
during manual E2E — the task reviewer caught it from the diff): the Scan
page's `onDetect` callback wasn't memoized and was a dependency of the
camera-scanning effect, so nearly every detection tore down and restarted
the camera — if the same barcode was still in view when it restarted, a
second `pickupScan.mutate()` could fire with a fresh idempotency key,
reaching the sync layer as a genuine duplicate write. Fixed by holding
`onDetect` in a ref (dropping it from the effect's dependencies entirely)
and deliberately stopping the camera after each detection so the rider
explicitly restarts scanning for the next parcel, instead of either
double-firing or silently freezing.

**Verified against the real backend end-to-end**, live in a browser (unlike
F11, `npm run build && npm run start` compiled and served without the
`next/font/google` network hang F11 hit — building the fonts once earlier
this session was apparently enough): registered a fresh org, seeded
branches/zones/rate-card/merchant/pickup+delivery riders, booked a real
shipment, and drove it through the actual rider actions rather than
directly manipulating shipment state — pickup scan (400 without an
`Idempotency-Key`, 200 with one, an identical replayed response on a
same-key retry, no second `PickedUp` transition), origin receive, manifest
scan/seal/dispatch/receive to `AtDestinationBranch`, a POD upload, and
`delivery-attempts/complete` with GPS coordinates and a collected COD
amount (again proving replay-safety and a 409 `IDEMPOTENCY_KEY_REUSED` on
a same-key-different-body retry) — the shipment reached `Delivered`.
Then, live in Chrome against the running app: logged in as the delivery
rider, confirmed the sidebar's "My Deliveries" link now resolves to
`/rider/tasks`, confirmed the bottom tab bar (Tasks/Scan/Profile) and each
tab's real content (task list empty-state, Profile showing the seeded
rider's role/license, no stuck-queue card when the queue is genuinely
empty), confirmed the Scan page's manual-entry fallback correctly trims/
uppercases input and routes an unmatched AWB to a clear error message,
confirmed the real `courier-rider-outbox` and `serwist-expiration`
IndexedDB databases exist and the real `sw-rider.js` service worker is
registered at the correct `/rider/` scope with a valid manifest (name,
icons, `start_url`, `scope`, `display: standalone` all present). Then
proved the offline path for real, not just via unit test: seeded a second
pickup task, logged in as the pickup rider, stubbed `fetch` to reject
exactly the `scan/pickup` BFF call (simulating a real network failure),
submitted the AWB — the outbox correctly queued a pending item and the
status bar showed "1 pending" — then did a genuine full page reload
(fetch override necessarily gone, proving this wasn't the same in-memory
state) and confirmed both that the outbox item was gone (auto-drained on
load) and, via a direct backend call, that the shipment's real server-side
status was now `PickedUp` — an offline-queued write surviving a reload and
successfully reaching the server on reconnect, exactly as designed.

**What this session could not verify:** real camera hardware (no webcam in
this sandbox — the manual-entry fallback path was verified instead, and
Task 11's own automated tests cover the `BarcodeDetector`/`@zxing/browser`
logic with a mocked camera), real GPS hardware, an actual "Add to Home
Screen" install flow on a physical device, and a literal Lighthouse PWA
audit run (no Lighthouse/DevTools-protocol tool available in this
environment) — the individual installability criteria Lighthouse's PWA
category checks were instead confirmed directly (valid manifest response,
registered service worker at the correct scope, viewport meta tag present).
One real, minor UX gap found during the live pass and left as a follow-up
rather than fixed in this phase: the Scan page's camera-preview area
(`aspect-square w-full`) grows disproportionately tall on wide desktop/
tablet viewports, pushing "Start Scanning" and the manual-entry fallback
below the fold with no obvious scroll affordance — harmless on an actual
phone-width screen (the PWA's primary target), but worth a `max-height`
constraint given `nav-main.tsx`'s own repointed entry deliberately keeps
desktop as a legitimate secondary access path.

## Phase F13 — Public Marketing Landing Page

Adds the one surface Phase F0's "Ledger Counter" design system deliberately
excludes (`DESIGN.md`: *"clarity, density, and correctness outrank marketing
polish"*): a public marketing page at `/` that sells the platform itself to
prospective courier companies, replacing the previous `getValidAccessToken()`
→ `redirect("/login" | "/dashboard")` behavior. Visually distinct dark
"Night Dispatch" identity (`src/app/globals.css`'s new `--marketing-*`
tokens, `.dark`-independent — same fixed-regardless-of-theme pattern as
`--auth-panel`), Framer Motion throughout, all content composed in
`features/marketing/`.

*Entry:* F0–F12 complete, no backend changes required (static marketing
copy only, reusing the existing `getValidAccessToken()` helper). *Exit:* `/`
renders the marketing page for every visitor with an auth-aware CTA
("Sign in" vs "Go to dashboard"), no fabricated testimonials/logos/metrics
anywhere, every animated section falls back to a static fully-visible state
under `prefers-reduced-motion`, `/login`/`/merchant-login`/`/dashboard`/
`/track` unchanged.

*Status: Complete.* Spec at
`docs/superpowers/specs/2026-09-07-frontend-phase-f13-marketing-landing-page-design.md`,
plan at
`docs/superpowers/plans/2026-09-07-frontend-phase-f13-marketing-landing-page.md`,
14 tasks executed inline (`superpowers:executing-plans`) in an isolated
worktree, TDD throughout. No backend changes.

One real test-infrastructure issue found and fixed during Task 3/9, not
anticipated in the plan: `framer-motion`'s `useReducedMotion` caches a
module-level singleton in `motion-dom` (`hasReducedMotionListener`) that
reads `window.matchMedia(...).matches` exactly once per process and relies
on a live `change` event afterward — a `matchMedia`-only mock (the plan's
original approach) only ever reflects whichever test ran first, and even
`vi.resetModules()` couldn't force a re-init because Vite pre-bundles
`framer-motion`/`motion-dom` for tests. Fixed by mocking `useReducedMotion`
directly via `vi.mock("framer-motion", ...)` in both
`motionVariants.test.tsx` and `ProductWalkthrough.test.tsx` instead.

**Verified live in a browser** (`npm run dev`, Chrome via
`claude-in-chrome`): loaded `/` and confirmed, with real screenshots, that
the hero's staggered entrance animation, the feature grid's and
how-it-works' scroll-triggered `whileInView` reveals (real
`IntersectionObserver`, not the jsdom stub), and the product walkthrough's
auto-cycling through all four stops (Created → In transit → Out for
delivery → Delivered, progress dots advancing) all genuinely animate as
designed — this is real evidence, not just passing unit tests, since
`whileInView`/`IntersectionObserver` behavior can't be exercised by jsdom
at all. Confirmed zero browser console errors or warnings on load.
Confirmed the Credibility section's honest "No case studies yet" copy
renders correctly (apostrophe-escaping lint fix included). Clicked the
nav's "Sign in" CTA and confirmed it navigates to the real `/login` page
unchanged. `npm run build` compiled cleanly; grepped the emitted CSS and
confirmed all 7 `marketing-*` Tailwind utilities were actually generated
(not silently purged), and `/` builds as a dynamic route (45.8 kB route JS,
144 kB First Load JS) since it reads the session cookie. Full suite: 353
tests / 126 files passing (up from the pre-phase baseline of 314/116),
zero regressions confirmed both via the full run and via isolated reruns
of every non-marketing `app`/`features` test file. Lint clean.

**What this session could not verify:** a real mobile-width viewport — the
browser automation's `resize_window` call to 375×812 did not actually
narrow the rendered viewport in this sandbox (the nav's `md:flex` links
stayed visible, which would only happen above the 768px breakpoint), so
the responsive collapse behavior described in the design spec was not
visually confirmed this session, only exercised by Tailwind's compiled
output existing. Also unverified: a literal Lighthouse pass, real assistive
-technology screen-reader behavior (only the automated landmark/heading-
order tests stand in for this), and cross-browser rendering outside this
session's Chrome instance.

### Design Refinement — 2026-09-07 (Impeccable-guided audit)

The shipped F13 page above was refined for design specificity rather than
left as originally built, following an `/impeccable`-guided critique
(single-context source review, since the deliverable was a plan for
approval rather than a persisted critique artifact, plus a clean
`detect.mjs` deterministic scan). User approved the full plan, chose to
extend the lifecycle tri-tone structurally, and chose to leave the "Sign
in" CTA copy as-is (no prospective-customer acquisition path added).

- **Section order:** `HowItWorks` and `ProductWalkthrough` moved adjacent
  to `Hero` — they describe the same 4-stage lifecycle per `content.ts`'s
  own tone-mirroring comment, and `FeatureGrid` previously split the pair.
  New order: Hero → HowItWorks → ProductWalkthrough → FeatureGrid →
  Credibility → FinalCta.
- **Hero:** asymmetric two-column layout (copy/CTA left, `ProductMockupPanel`
  full-size right) replacing the centered stack; removed the decorative
  `blur-[120px]` radial glow and the single-word (`text-marketing-accent`)
  headline accent on "accounted for." — both named anti-patterns in the
  frontend-design skill's generic-AI-design checklist.
- **New `LifecycleRail.tsx`:** extracted the duplicated dot-and-connecting-
  line tone rail (previously separate hand-rolled copies in
  `HowItWorks.tsx` and `ProductMockupPanel.tsx`) into one shared component
  — progress mode (`activeIndex` set, Hero/Walkthrough) and legend mode
  (`activeIndex` omitted, HowItWorks). This is how the lifecycle tri-tone
  (`neutral`/`motion`/`success`, `toneColor.ts`) now extends structurally
  across three touchpoints instead of two independent, driftable copies.
- **`ProductMockupPanel`:** the all-caps tracked "TRACKING PREVIEW" label
  changed to sentence case (another named anti-pattern).
- **Motion:** the scroll-triggered `whileInView` fade-up-stagger
  (`motionVariants.ts`) was removed from `FeatureGrid`, `HowItWorks`,
  `Credibility`, and `FinalCta` — **superseding the "feature grid's and
  how-it-works' scroll-triggered `whileInView` reveals" claim in the
  verification note above**, which described the pre-refinement build only.
  `FeatureGrid`, `Credibility`, and `FinalCta` are now plain server
  components (no client JS for that motion). The page's two authored
  motion moments are now Hero's on-load stagger and ProductWalkthrough's
  existing auto-cycle.
- **`Credibility`:** redesigned from a third copy of the bordered-card grid
  (shared with FeatureGrid/HowItWorks) into a quieter single-column ruled
  list, since it's conceptually a set of promises, not features or steps —
  `CREDIBILITY_POINTS` copy in `content.ts` unchanged.
- **`MarketingNav`:** added a mobile drawer (`Sheet`, matching the app's
  own mobile-nav convention) so Features/How it works/Track a parcel are
  reachable below the `md` breakpoint — the original F13 build had no
  mobile nav fallback at all.

*Verified:* all 36 pre-existing marketing tests pass unmodified, lint
clean, `tsc --noEmit` and `next build` succeed with no new errors, confirmed
visually in a browser at desktop width. Mobile-viewport confirmation was
again blocked by the same `resize_window`-doesn't-narrow-the-viewport
sandbox limitation noted above; not re-investigated this session.

### Light/Dark Toggle (2026-09-07, same-day follow-up)

Explicit user request to add a real light/dark toggle to the marketing
page, overriding the "fixed dark, independent of app theme" decision
above (confirmed via brainstorming before implementing, since it reverses
a documented decision). `--marketing-*` moved from unconditional `:root`
values to a proper light (`:root`)/dark (`.dark`) pair sharing the app's
existing `next-themes` state (`ThemeToggle`/`providers.tsx`), so a
visitor's choice on marketing now carries through to `/login` and beyond.

- Light values keep marketing's own hue family (228° neutrals, 236°
  indigo accent) rather than the app's warm Ledger Counter paper palette,
  per user's explicit choice — marketing keeps a distinct identity in
  both modes rather than converging on the operational app's look.
- Added three new tokens, `--marketing-tone-neutral/-motion/-success`,
  so `toneColor.ts`'s `TONE_COLOR` (previously three literal dark-only
  `hsl(...)` strings) now references `hsl(var(--marketing-tone-*))` and
  re-themes automatically — the light values reuse the app's own
  already-verified light-mode `--status-created/-in-transit/-delivered`
  tokens rather than inventing new hexes.
- New `MarketingThemeToggle.tsx` (marketing-styled, not the app's
  `ThemeToggle` component, which would render with the wrong — app-theme
  — tokens on this page) wired into `MarketingNav`, visible at every
  breakpoint next to the mobile-menu trigger.
- Every new color pairing was verified against WCAG AA (4.5:1 text, 3:1
  non-text) with a throwaway contrast script before being committed —
  see the `:root`/`.dark` comments in `globals.css` for the exact ratios
  and derivation.

*Verified:* all 39 marketing tests pass (3 new, for `MarketingThemeToggle`),
lint and `next build` clean, confirmed visually in a browser — light mode
renders correctly by default (this app's `next-themes` `defaultTheme` is
`"light"` app-wide; marketing now follows that default rather than always
opening dark, an accepted consequence of sharing global theme state) and
forcing `.dark` via `localStorage` confirmed the original dark palette,
tri-tone rail included, still renders correctly.

**Root-caused and resolved, not app code:** a "Hydration failed" console
error initially appeared on both `/` and `/login` in this dev browser
session, with the theme-toggle/mobile-menu buttons vanishing after
React's mismatch-recovery re-render. Investigation
(`navigator.serviceWorker.getRegistrations()` / `caches.keys()`) found a
service worker registered at root scope (`http://localhost:3100/`,
script `sw.js`) backed by a cache named `interviewdeck-v1` — leftover
from an unrelated prior local project that had previously run on the
same port in this browser profile, still intercepting every request to
the origin and serving its own stale bundle underneath this app's fresh
dev-server responses. This repo has no `sw.js` at all (only
`sw-rider.js`, scoped to `/rider/`, and even that is disabled in
`next dev` per `next.config.mjs`'s `disable: process.env.NODE_ENV ===
"development"`), so the registration was fully external contamination,
not a code defect. Unregistering it and clearing its cache
(`registrations[0].unregister()` + `caches.delete("interviewdeck-v1")`)
resolved the hydration error completely — confirmed with zero console
errors on repeated reloads of both `/` and `/login`, and a working
end-to-end click of the theme toggle. No code change was needed or made
for this; noted here so a future session hitting the same symptom in
this browser profile knows to check for a stale service worker first.

## Phase F14 — Marketing Visual Redesign ("Custody Thread")

**Spec:** `docs/superpowers/specs/2026-09-21-marketing-visual-redesign-design.md` ·
**Plan:** `docs/superpowers/plans/2026-09-21-marketing-visual-redesign.md`

Second visual pass over the public surfaces, evolving (not replacing) Night
Dispatch:

- Editorial hero (fluid oversized headline, mono AWB metadata row, real
  dashboard screenshot overlapped by three labelled sample-data cards).
- Scroll-linked custody thread with numbered checkpoints 01–06: custody chain,
  scan & manifest, COD ledger, exceptions (NDR/RTO), roles & branch scope,
  screens.
- Three inline-SVG diagrams driven by data and pure helpers
  (`codBreakdown`, `reconcileManifest`).
- `/pricing`, `/track`, `/track/[awb]` restyled with shared primitives; the
  auth showcase panel gets a mono stamp.
- No new dependencies; every animation has a reduced-motion static fallback.
- Removed as superseded: `HowItWorks`, `FeatureGrid`, `ProductWalkthrough`,
  `Credibility`, `RouteLineGraphic`.

*Verified (2026-09-21):* `npm run lint` and `npm run build` clean; full suite 640/642
passing — the 2 failures were 5s timeouts in unrelated COD/reports sheet tests (present at
baseline, pass when run alone). Visually confirmed in a browser: `/` (dark and light),
`/pricing`, `/track`, `/login`; no horizontal overflow at 375px (measured in a 375px iframe).
Not browser-verified: `/register`, `/track/[awb]` with live data, and `prefers-reduced-motion`
(covered by unit tests only). Contrast used existing verified tokens only; no new colors.

## Phase F15 — Console Redesign

Design source: claude.ai artifact "Courier Platform UI Redesign"
(https://claude.ai/artifact/1dGwukUsc2jwxhThg5eyHu). Frontend only; data the API does not
serve renders `ComingSoonSlot`. Each sub-phase has its own spec → plan → implementation cycle.

| Sub-phase | Scope | Status |
|---|---|---|
| R1 — Console Foundation | Tokens, `ConsoleShell` (staff/merchant/platform), grouped dark rail, header (breadcrumb, AD/BS date, notifications slot), page primitives, table/badge restyle | Done — spec `docs/superpowers/specs/2026-09-30-f15-r1-console-foundation-design.md` |
| R2 — Staff operations | Dashboard, Shipments + detail Sheet, `/scan` Scan station | Done — spec `docs/superpowers/specs/2026-10-05-f15-r2-staff-operations-design.md` |
| R3a — Staff finance | COD reconciliation: money-flow strip, rider deposits + variance, record-deposit Sheet, merchant settlement breakdown | Done — spec `docs/superpowers/specs/2026-10-07-f15-r3a-cod-finance-design.md` |
| R3b — Staff network | Manifests, NDR, RTO, riders, live map, branches (artboards first) | Planned |
| R4 — Rider PWA | Run screen, POD flow, scan, profile | Planned |
| R5 — Merchant portal + public tracking | 7 merchant pages, `/track` | Planned |
| R6 — Platform + admin | Platform pages, admin, audit, billing, auth pages | Planned |

**Backend follow-ups from R1:** notification feed (header bell); nav badge counts (e.g. open
NDRs); plan/branch usage in the session payload (sidebar plan card).

**Backend follow-ups from R2:**
1. Hourly booked/delivered flow.
2. Prior-period deltas for KPIs and pipeline.
3. Needs-action counts: manifests with missing parcels, riders holding cash > 24 h, SLA risk.
4. Per-branch on-hand / out-for-delivery counts.
5. COD split: rider-held vs deposited-unverified. (done in R3a)
6. Per-shipment COD, delivery attempts, entity audit reads.
7. Standalone sort operation + sort bins.
8. "Next expected" (run stop, attempt N of M) for the custody card.
9. Bulk CSV shipment upload; label printing; rider reassignment.
10. Server-side actor display names on events.
11. "Needs action" shipments filter.

**Backend follow-ups from R3a:**
1. Shortage/overage reason + approval on rider deposits (adjustments for `RiderCashSettlement`, threshold-based Finance Manager approval).
2. Cash denomination breakdown on deposits.
3. Digital (Fonepay/QR) vs cash split on deposits.
4. Close branch day (lock + day report).
5. COD ledger read + CSV export.
6. Link COD collections to delivery runs (run id on deposits).
7. Separate COD handling-fee line on merchant settlement items.

## Later

All frontend phases planned through F14 are complete. Any further frontend
work (e.g. a UI for backend Phase 14 — route optimization, GPS, ETA, smart
dispatch — once that backend phase exists) gets its own brainstorm → spec →
plan cycle per this repo's standing workflow, same as every phase above.
