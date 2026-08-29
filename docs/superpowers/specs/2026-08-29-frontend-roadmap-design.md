# Frontend Roadmap — Design Spec

**Date:** 2026-08-29
**Status:** Approved for Phase F0 planning
**Scope:** Establishes the conventions every phase in `docs/FRONTEND-ROADMAP.md`
(Phases F0–F9, staff back-office) inherits. Merchant portal (F10) and public
tracking (F11) are out of scope for this spec — they get their own spec when their
turn comes.

## 1. Context

Backend Phases 0–13 are complete (backend slices only, per `docs/ROADMAP.md`).
`src/web` is an untouched `create-next-app` skeleton: Next.js 14.2.35 (App Router),
React 18, TypeScript, Tailwind CSS 3.4, TanStack Query 5, React Hook Form + Zod,
Vitest + Testing Library. No shadcn/ui, no Zustand, no API client, no auth wiring
exist yet — all of Phase F0's job.

`ARCHITECTURE.md` §4 already commits to: one deployable Next.js app, the
`app/ features/ components/ lib/ services/` folder structure, TanStack Query for
server state, Zustand only for genuinely global client state, React Hook Form +
Zod, and the rule that the frontend never computes or trusts
price/COD/tenant/branch/role values — it only echoes what the server returned.
This spec does not revisit those; it fills in what wasn't yet decided: auth
transport, design-system approach, and per-phase conventions.

## 2. Decision: staff back-office first

Confirmed with the user. The frontend covers three distinct experiences (staff
back-office, merchant portal, public tracking) that are independent enough to be
separate roadmap entries, each with its own spec/plan/implementation cycle — too
much surface for one spec. Staff back-office goes first because it's the largest
internal-value surface and exercises nearly every backend module already built.

## 3. Decision: fresh design system, no existing brand constraints

No existing brand assets. Phase F0 defines the visual identity from scratch using
the `frontend-design` and `impeccable` skills, tailored to a Nepal domestic courier
/ logistics operations product — not a generic SaaS admin template. Concretely,
this phase must produce a considered, non-default look (shadcn/ui as the component
*primitive* layer, themed — not shipped with stock defaults): a type scale, a color
system that includes semantic/status colors for shipment states
(`IN_TRANSIT`, `OUT_FOR_DELIVERY`, `DELIVERED`, `NDR`, `RTO`, etc. —
`SHIPMENT-LIFECYCLE.md`'s state list), spacing scale, and motion conventions for
state changes (e.g. a scan confirming, a status transitioning).

## 4. Auth transport: httpOnly-cookie BFF, not client-stored JWTs

`API-SPEC.md` §2 defines staff sessions as JWT access token + refresh token with
rotation on use. The frontend must not store either token anywhere JS can read
(`localStorage`, non-httpOnly cookies) — CLAUDE.md §33 treats token handling as a
security-critical concern, and XSS-exfiltrable tokens are the standard failure mode
this decision avoids.

Design: Next.js Route Handlers under `app/api/auth/*` act as a thin
backend-for-frontend:

- `POST /api/auth/login` — calls the real API's login endpoint, sets the access +
  refresh tokens as `httpOnly`, `Secure`, `SameSite=Lax` cookies, returns only
  non-sensitive session info (user id, org id, permissions list) to the client.
- A shared server-side helper attaches the bearer token to proxied API calls and
  performs refresh-rotation transparently when the access token is expired,
  updating the cookie before the caller sees a response.
- `POST /api/auth/logout` clears the cookies and calls the backend's logout/token
  revocation if one exists.
- Client components never see raw tokens; they see session state (user, org,
  permissions) via a small `useSession()` hook backed by a server-fetched value.

This keeps the "never trust frontend-computed tenant/branch/role" rule (CLAUDE.md
§33) intact in the other direction too: permissions displayed client-side are an
echo of what the server's JWT claims say, never locally computed.

## 5. Permission-aware UI: `usePermission`

A single hook, `usePermission(permission: string, options?: { branchId?: string })`,
reads the session's permission set (and branch-scope grants where relevant) and
returns a boolean. Nav items, action buttons, and route guards all call this hook
rather than checking role names — mirroring the backend's permission-based model
(CLAUDE.md §6, RBAC.md §1) so the two layers can't drift into "UI shows an action
the backend will 403 on" or vice versa. This hook is built in Phase F0 and consumed
by every later phase.

## 6. Data layer conventions

- `services/<feature>/*.ts` — thin functions wrapping fetch calls to the Next.js
  BFF routes (which proxy to the real API), one file per feature area matching
  `features/`.
- `features/<feature>/hooks/*.ts` — TanStack Query hooks (`useShipments`,
  `useShipment(id)`, etc.) wrapping the services layer with query keys, caching,
  and invalidation rules.
- Zod schemas live alongside the feature they validate and mirror the backend's
  FluentValidation rules where practical (per `ARCHITECTURE.md` §4) — not
  auto-generated from the backend in this phase; kept in sync by hand, flagged as
  a future improvement if drift becomes a real problem.
- Zustand stores are limited to: active branch selector, sidebar collapsed state.
  Nothing server-derived goes into Zustand.

## 7. Component & primitive inventory (Phase F0 deliverables)

Built once in F0, reused by F1–F9:

- App shell: top nav, collapsible sidebar (permission-filtered nav items), org
  name + branch switcher, user menu.
- Auth screens: login, session-expired interstitial.
- Data table pattern: sortable/filterable, server-side pagination (matching the
  API's existing pagination per `docs/superpowers/specs/2026-08-28-api-pagination-
  and-export-streaming-design.md`), empty and loading skeleton states.
- Status badge: shipment/NDR/manifest/COD status → consistent color + label
  mapping, single source of truth (no per-feature reimplementation).
- Form patterns: React Hook Form + Zod + shadcn form primitives, consistent error
  display.
- Empty/error/loading state components: used everywhere data can be absent,
  errored, or in flight — no feature ships an ad hoc spinner or blank div.

## 8. Testing

Vitest + Testing Library (already in the skeleton) for component and hook tests.
Each phase's exit criteria requires tests for: permission-gated rendering (an
element that should hide/show based on `usePermission`), the phase's core
interactive flow (e.g. F3's booking form submits and shows the server-computed
price, not a client-guessed one), and empty/error states. No end-to-end/browser
test tooling is introduced in this spec — if it becomes necessary it's a decision
for a later phase's spec, not assumed here.

## 9. Phase discipline

Every phase in `docs/FRONTEND-ROADMAP.md` follows the same shape backend phases
did: entry criteria assume the previous phase is genuinely done, exit criteria are
concrete and testable, and a status write-up is appended to the roadmap doc on
completion (mirroring `docs/ROADMAP.md`'s per-phase "Status:" notes). Each phase
gets its own brainstorm-confirmed scope (already done here for F0–F9 as a set),
its own implementation plan via the `writing-plans` skill, and its own
subagent-driven-development + finishing-a-development-branch cycle — no shortcuts,
per the standing workflow preference for this project.

## 10. What this spec deliberately does not decide

- Exact color/type token values — that's Phase F0 implementation work product,
  produced live with the frontend-design/impeccable skills, not pre-decided here.
- Merchant portal and public tracking conventions — separate future specs.
- Rider mobile-web — noted as a possible Phase F12 only if F5's desktop
  approximation proves insufficient; not designed here.
- Whether to auto-generate Zod schemas from backend validation — flagged as a
  future improvement, not committed to.
