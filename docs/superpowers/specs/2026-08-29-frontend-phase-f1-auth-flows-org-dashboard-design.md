# Frontend Phase F1 — Auth Flows + Organization Dashboard — Design Spec

**Date:** 2026-08-29
**Status:** Approved for implementation planning
**Scope:** `docs/FRONTEND-ROADMAP.md` Phase F1 — session-expiry re-authentication, and the
organization dashboard (`GET /api/v1/reports/dashboard/organization`). Branch/rider performance
breakdowns are explicitly Phase F8's scope, not this phase's.

## 1. Context

Phase F0 shipped the design system, app shell, auth BFF (login/logout/session, all working
end-to-end against the real API), `usePermission`, `DataTable`, `StatusBadge`, and empty/error/
loading state primitives. `src/web/src/app/(staff)/dashboard/page.tsx` is currently a placeholder
`EmptyState`. This phase replaces that placeholder with real data and adds the piece F0's auth BFF
didn't cover: what happens when a session outlives its refresh token while the user is mid-page.

Two things surfaced during F0 that this phase must account for:

- **A convention change** (recorded in `docs/ARCHITECTURE.md` §4 and memory
  `feedback_courier_frontend_shadcn_blocks.md`, from Phase F0 review feedback): prefer shadcn/ui's
  official blocks and components over hand-built compositions where a good fit exists. This phase
  uses shadcn's `Dialog` primitive (`npx shadcn add dialog`) for the re-auth modal and `Spinner`
  (`npx shadcn add spinner`) for loading states — no hand-rolled equivalents. There is no official
  shadcn block for a session-expiry re-auth modal specifically, so that composition is custom (the
  convention's own carve-out: custom is for what shadcn has no block for).
- **A real bug in `StatusBadge`'s status keys.** `src/web/src/components/status-badge/
  statusConfig.ts` keys (`CREATED`, `IN_TRANSIT`, ...) were written against `SHIPMENT-LIFECYCLE.md`'s
  prose notation, not the backend's actual wire format. `Courier.Shipments.Domain.ShipmentStatus`
  is a plain C# enum (`Created, PickupAssigned, PickedUp, AtOriginBranch, Sorted, InTransit,
  AtDestinationBranch, OutForDelivery, DeliveryFailed, Delivered, Cancelled, NdrCreated,
  RtoInitiated, RtoInTransit, RtoAtOrigin, RtoDelivered`), and System.Text.Json serializes it (and
  dictionary keys typed by it) using the enum member's own PascalCase name, not
  `SCREAMING_SNAKE_CASE`. Every real shipment status from any real API response would have silently
  fallen back to `StatusBadge`'s "unknown status" gray dot. This phase is the first to consume real
  status data (`OrgDashboardResponse.TodaysShipmentsByStatus`), so it must fix this first.

## 2. Fix: `statusConfig.ts` key casing

Rewrite every key in `STATUS_CONFIG` (`src/web/src/components/status-badge/statusConfig.ts`) to
the exact `ShipmentStatus` enum member name. No other file changes — `resolveStatusConfig`'s
signature, the token names, and every consumer (`StatusBadge`) stay identical; only the map's keys
change. `NDR.Status`/`Manifest`-family enums follow the same PascalCase pattern (confirmed via
`Courier.Ndr.Domain.NdrStatus`) — worth remembering for any future phase adding a status display
for those, though none is in this phase's scope.

## 3. Session-expiry re-authentication

**Problem:** F0's server-side `getValidAccessToken()` transparently refreshes an expired access
token using the refresh token — invisible to the client. But when the refresh token itself has
also expired or been revoked, every BFF route this phase (and every later phase) calls returns
`401`, and the client has no shared way to detect that or recover without a jarring full-page
redirect that loses whatever the user was doing.

**Design:**

- `src/web/src/lib/api/clientFetch.ts` — a client-side fetch wrapper for calling same-origin
  `/api/*` BFF routes: `clientFetch<T>(path: string, init?: RequestInit): Promise<T>`. Throws a
  new `SessionExpiredError` (extends `Error`) when the response status is `401`; throws a plain
  `Error` for any other non-OK status; otherwise returns the parsed JSON body. Every client-side
  data hook (this phase's `useOrgDashboard`, and every later phase's feature hooks) uses this
  instead of a raw `fetch`, so the 401-detection point is singular and shared.
- `src/web/src/lib/store/authStore.ts` — a new small Zustand store, `sessionExpired: boolean`,
  `showReauthDialog: () => void`, `hideReauthDialog: () => void`. This is genuinely global,
  client-only, non-server-derived UI state (spec's own Phase F0 §6 carve-out for Zustand).
- `providers.tsx` configures a `QueryCache({ onError })` (TanStack Query's global error hook) that
  calls `useAuthStore.getState().showReauthDialog()` when the error is a `SessionExpiredError`.
  This covers every query in the app, present and future, from one place.
- `src/web/src/components/layout/ReauthDialog.tsx` — a shadcn `Dialog` (installed via
  `npx shadcn add dialog`), mounted once in `AppShell` so it's available on every staff route.
  Watches `useAuthStore().sessionExpired`. When open: shows the user's last-known email (read from
  `useSession()`'s cached data, read-only) and a password field, reusing the existing
  `POST /api/auth/login` route. On success: calls `hideReauthDialog()` and
  `queryClient.invalidateQueries()` so every previously-failed query refetches and the page resumes
  in place — no navigation, no lost scroll position or form state elsewhere on the page. A "Sign in
  as someone else" link calls `POST /api/auth/logout` and routes to `/login`, for the case where
  the user can't recall the password or wants to switch accounts. Loading state on submit uses
  shadcn's `Spinner`, not text-only ("Signing in...").
- The dialog is non-dismissable by clicking outside or Escape while `sessionExpired` is true (a
  session-expired re-auth is exactly the "needs protected focus" case `impeccable`'s craft-floor
  guidance carves out as a legitimate reason for a modal) — the only ways out are successful
  re-auth or the explicit sign-out link.

## 4. Organization dashboard

**Data source:** `GET /api/v1/reports/dashboard/organization` (no query params needed for this
phase's scope — confirmed by reading `OrgDashboardService.GetAsync`: `TodaysShipmentsByStatus` is
always computed against the literal UTC calendar day regardless of any `from`/`to`, and the rate
fields this phase uses default server-side to a trailing 30-day window when `from`/`to` are
omitted). `BranchPerformance`/`RiderPerformance` fields on the response exist but are Phase F8's
concern — this phase's `OrgDashboardSummary` client-side type only carries the fields it uses,
not the full response shape, so nothing downstream accidentally depends on the deferred fields.

**BFF route:** `src/web/src/app/api/reports/dashboard/organization/route.ts` — thin proxy via
F0's `apiFetch`, matching the shape of F0's auth routes: call the backend, return the JSON body
as-is on success, propagate `401` on failure (letting `clientFetch`'s shared detection handle it).

**Client hook:** `src/web/src/features/reports/hooks/useOrgDashboard.ts` — a TanStack Query hook
(`queryKey: ["org-dashboard"]`) using `clientFetch` against the new BFF route.

**Content (three sections, each a set of shadcn `Card`-based stat tiles):**

1. **Today's shipment funnel.** `TodaysShipmentsByStatus` (a `Record<ShipmentStatus, number>`)
   bucketed into the same 7 groups CLAUDE.md §29 names and `StatusBadge`'s status tokens already
   group by hue — reusing that existing mapping keeps the funnel's colors consistent with every
   `StatusBadge` elsewhere in the app rather than inventing a second grouping:
   - Created: `Created`, `PickupAssigned` (slate / `status-created`)
   - Picked Up: `PickedUp` (marigold / `status-in-transit`)
   - In Transit: `AtOriginBranch`, `Sorted`, `InTransit`, `AtDestinationBranch` (marigold /
     `status-in-transit`)
   - Out for Delivery: `OutForDelivery` (bright marigold / `status-out-for-delivery`)
   - Delivered: `Delivered` (pine / `status-delivered`)
   - NDR: `DeliveryFailed`, `NdrCreated` (vermillion / `status-delivery-failed`, `status-ndr`)
   - RTO: `RtoInitiated`, `RtoInTransit`, `RtoAtOrigin`, `RtoDelivered` (brick / `status-rto`)
   - Cancelled: `Cancelled` (slate / `status-cancelled`) — CLAUDE.md §29 doesn't list this bucket
     explicitly, but omitting real data isn't the right default; it gets its own tile.
2. **COD financial summary.** `Financial` (`CodFinancialSummary`: `Expected, Collected, Pending,
   Settled, MerchantPayable`) as five currency tiles.
3. **Top-line rates.** `OverallDeliverySuccessRate`, `OverallNdrRate`, `OverallRtoRate` (each a
   `double?`, rendered as a percentage, `—` when `null`) and `AverageDeliveryTime` (a `TimeSpan?`,
   rendered as `Xh Ym`, `—` when `null`).

The exact tile layout, spacing, and visual treatment (grid arrangement, number emphasis, whether
the funnel reads as a bar/row or discrete tiles) is this phase's own design work product, done with
the `dataviz` skill during implementation — same division of labor Phase F0 used for its design
tokens (this spec fixes *what* data appears and *how it's sourced*, not the exact pixel layout).

**Loading/error/empty states:** loading uses shadcn's `Spinner` (not F0's `Skeleton`-based
`LoadingState`, which was designed for list/table content, not a stat-tile grid); error uses F0's
existing `ErrorState` with a retry action wired to `useOrgDashboard`'s `refetch`; there is no empty
state for this page — the API always returns a fully-populated `OrgDashboardResponse` structure
(zero counts render as `0`/`—`, not an absent section).

## 5. What this spec deliberately does not decide

- Exact tile visual layout/spacing — Phase F1's own implementation work via the `dataviz` skill.
- Branch/rider performance tables — Phase F8.
- A date-range picker — confirmed out of scope for F1 (the API's `from`/`to` params aren't needed
  by any field this phase surfaces); Phase F8's "full dashboard build-out" owns this if it's still
  wanted then.
- Whether `LoginForm`'s own "Signing in..." text state should also switch to `Spinner` — F0's
  `LoginForm` isn't touched by this phase per `docs/ARCHITECTURE.md`'s note that Phase F0's
  hand-built components aren't retroactively required to change; `ReauthDialog` (new in this
  phase) uses `Spinner` from the start.
