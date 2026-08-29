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

*Status: Not started.*

## Phase F3 — Shipments

Shipment booking form (rate quote preview via Phase 5 pricing API, price is always
server-computed and read-only in the UI per CLAUDE.md §33), shipment list with
filters/pagination, shipment detail page with the tracking-event timeline and
custody history.

*Entry:* F2 done. *Exit:* a shipment can be booked, appears correctly in the list
with server-side pagination, and its detail page's timeline matches the backend's
`ShipmentEvent` history exactly (no client-side reconstruction of state).

*Status: Not started.*

## Phase F4 — Scanning + Manifests + Branch Transfer

Barcode/QR scan UI (camera-based where feasible, manual AWB entry fallback) for
receive/sort/dispatch/transfer operations, manifest create/seal/dispatch/receive
flow (`Manifest` lifecycle), missing-shipment reconciliation UI on manifest receive.

*Entry:* F3 done. *Exit:* a manifest can be built by scanning, sealed, dispatched,
and received with missing shipments correctly flagged, exercising the full Phase 7
manifest lifecycle through the UI only.

*Status: Not started.*

## Phase F5 — Riders + Delivery + POD

Rider assignment UI, delivery run/task views, delivery attempt recording (reason
codes from CLAUDE.md §19), POD capture (signature/photo/OTP/GPS — desktop-friendly
approximation; a dedicated rider mobile-web experience is a later roadmap
candidate, not required here).

*Entry:* F4 done. *Exit:* a delivery attempt (success or failure) can be recorded
end-to-end with POD captured on success, matching Phase 8's backend contract.

*Status: Not started.*

## Phase F6 — NDR + RTO

NDR queue/workflow UI (status transitions per CLAUDE.md §20), RTO tracking linked
to the original shipment (CLAUDE.md §21).

*Entry:* F5 done. *Exit:* an NDR can be worked from `OPEN` through to
`RESOLVED`/`RTO_REQUESTED` entirely through the UI, and an RTO shipment's linkage
back to its original AWB is visible on both records.

*Status: Not started.*

## Phase F7 — COD Operations

COD collection views, rider cash settlement, branch cash settlement,
reconciliation screens surfacing expected/collected/deposited/reconciled amounts
per CLAUDE.md §22–23, with all adjustment actions requiring a reason and showing
before/after values.

*Entry:* F6 done. *Exit:* the full COD chain (`COD_EXPECTED` → `SETTLED`) is
visible and actionable through the UI, matching the backend's already-proven
Phase 10 chain; every adjustment UI action requires and displays a reason.

*Status: Not started.*

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
