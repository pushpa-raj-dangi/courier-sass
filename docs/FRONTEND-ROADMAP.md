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

*Status: Not started.*

## Phase F1 — Auth Flows + Organization Dashboard

Full login/logout/session-expiry handling, permission-aware nav built on the
`usePermission` hook, and the organization dashboard (CLAUDE.md §29 / backend
`GET /api/v1/reports/dashboard/organization`): today's shipment funnel, NDR/RTO
counts, COD summary tiles.

*Entry:* F0 done. *Exit:* dashboard reflects real data end-to-end against the Phase
13 reporting API, correctly scoped to the logged-in user's organization; session
expiry redirects to login without a full page-state loss surprise.

*Status: Not started.*

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
