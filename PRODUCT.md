# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Internal staff of Nepal domestic courier companies, operating the back-office
web app (`src/web`) this project's design work targets first (merchant portal
and public tracking are later roadmap phases, not in scope yet). Primary
personas, per `docs/PRODUCT-SPEC.md` §3 and `docs/RBAC.md`:

- **Organization Owner/Admin, Operations Manager** — configure branches, roles,
  rate cards; cross-branch oversight and exception handling; org-wide
  dashboards and settlements.
- **Branch Manager** — branch dashboard, staff assignment, manifest sign-off,
  scoped to one or more branches via `UserBranch`.
- **Dispatcher / Sorting / Counter Staff** — scanning, sorting, manifest
  creation, counter booking, at a physical branch counter.
- **Pickup / Delivery Rider** — mobile-first: assigned tasks, navigation
  handoff, OTP, COD collection, POD capture in the field. (Rider UI is a
  desktop-friendly approximation in the current roadmap; a dedicated
  mobile-web rider experience is a later, separately-scoped phase — Phase F12.)
- **Finance Manager/Staff** — COD reconciliation, merchant settlements,
  audited adjustments.
- **Customer Support** — shipment lookup, NDR follow-up, customer
  communication.
- **Auditor** — read-only audit trail and financial history.
- **Merchant/Merchant Staff** — e-commerce sellers who book shipments, track
  parcels, view settlements (own portal, Phase F10, not yet built).
- **Public recipient/sender** — unauthenticated `/track/{awb}` visitor
  (Phase F11, not yet built).
- **SuperAdmin** — platform operator (not a tenant), provisions organizations
  and does platform support; never touches day-to-day courier operations.

All staff roles work inside one Organization's data at a time, with access
further scoped to specific branches (`UserBranch`) — never org-wide by
default.

## Product Purpose

A multi-tenant SaaS platform letting Nepal-based courier companies run their
entire domestic parcel operation — branches, hubs, riders, merchants, COD, and
delivery networks — on one system. The subject matter is the **physical
lifecycle of a parcel**: custody, movement, delivery, and financial
settlement, not merely order/shipment creation. Success means the system can
always answer: where is the parcel, who currently has it, which branch owns
it, what happened to it and when, who performed the operation, was COD
collected, has the merchant been settled, why did a delivery fail, and what
happens next (`CLAUDE.md` §39, `docs/PRODUCT-SPEC.md` §1).

## Positioning

Not an e-commerce order manager. A neighboring "shipment tracker" product
could not truthfully copy this platform's core claim: every status change is
backed by a real physical operation (scan, manifest, delivery attempt), never
an arbitrary edit, and every parcel's custody chain and every rupee of COD is
traceable end-to-end from collection through merchant settlement, with no
silent adjustments (`CLAUDE.md` §39, §22–23).

## Operating Context

- **Multi-tenant, multi-branch:** one Organization = one courier company,
  owning branches, users, merchants, shipments, and rate cards. Tenant and
  branch scoping are enforced server-side; the frontend never trusts a
  tenant/branch/role/price/COD value handed to it (`CLAUDE.md` §4, §33).
- **Permission-based RBAC**, not role-name checks — UI gating uses the
  `usePermission` hook against a real permission list (e.g. `shipment.create`,
  `cod.collect`, `manifest.read`), never `user.role === "Admin"` (`CLAUDE.md`
  §6, `docs/RBAC.md`).
- **Nepal-specific geography** (Province → District → Local Level → Ward)
  drives branch coverage, delivery zones, and pricing via cascading pickers,
  not free text.
- **Scan-driven, manifest-based operations**: barcode/QR scanning for
  receive/sort/dispatch/transfer/rider pickup/rider delivery; hub-to-hub and
  branch-to-branch transfers move through a `Manifest` lifecycle
  (Draft → Sealed → Dispatched → InTransit → Received → Closed) with
  missing-shipment reconciliation on receive.
- **Shipment lifecycle** is a state machine (Created → ... → Delivered, plus
  NDR/RTO branches) — status changes must correspond to a real operational
  event, never an arbitrary admin edit.
- **COD is a financial subsystem**, not a boolean: collection → rider deposit
  → branch reconciliation → finance verification → merchant settlement, fully
  traceable; adjustments always show reason, before/after values, and require
  approval where appropriate.
- **Sheet-over-pages convention** (`docs/ARCHITECTURE.md` §9a): detail/create
  views in the staff back-office are an instant right-side Sheet driven by a
  Zustand store, not a separate routed page.
- Currency is NPR only; no multi-currency, no cross-border shipping, no live
  GPS tracking maps, no route optimization in the current scope (deferred to
  Phase 14+, `docs/PRODUCT-SPEC.md` §5).

## Capabilities and Constraints

- **Stack (already established in code, not open):** Next.js 14 (App Router),
  TypeScript, Tailwind CSS, shadcn/ui (Radix primitives), TanStack Query for
  server state, Zustand only for genuinely global/UI client state (e.g. which
  Sheet is open), React Hook Form + Zod for forms, Vitest + Testing Library
  for tests. A Next.js Route Handler BFF holds JWT access/refresh tokens in
  httpOnly cookies — never exposed to client JS.
- Rate/price and COD figures shown in the UI are always server-computed and
  read-only; historical shipments preserve the price calculated at booking
  time even if today's rate card changes.
- Every sensitive action (status corrections, COD adjustments, role/branch
  changes) must be traceable to an audit record; the UI must surface
  reason/before/after for adjustments, not just accept a raw new value.
- Backend enums are inconsistently JSON-serialized as of this writing — several
  (`ShipmentStatus`, `BranchType`, `ManifestStatus`/`ManifestPurpose`) needed a
  `JsonStringEnumConverter` fix discovered during frontend E2E passes; others
  (`NdrStatus`, `CodCollectionStatus`, etc.) may still raw-serialize as
  integers until a phase's own verification reaches them — a UI consuming a
  new enum should verify its wire format rather than assume PascalCase.
  strings.
- No brand name/logo exists yet (see Brand Commitments) — treat the product as
  unnamed/white-label; don't invent a name or identity in design work.
- No camera-based barcode scanning in the current scope — scan surfaces use
  manual AWB entry (a documented, deliberate scope cut, not a gap to silently
  fill in).
- Rider UI in the current roadmap phase (F5) is a desktop-friendly
  approximation of field operations, not a mobile-optimized experience.

## Brand Commitments

None yet. The product has no confirmed market name — the codebase title
("Courier Platform") is a placeholder, and there is no logo or other brand
asset. Design work should not invent or lock in a name/identity; keep the
visual language generic/white-label-appropriate until a name is confirmed.

## Evidence on Hand

No real customer data, testimonials, case studies, or press exists — this is
an internally-developed SaaS platform still in active phased build-out
(`docs/FRONTEND-ROADMAP.md`: staff back-office phases F0–F4 complete as of
2026-08-29, F5 onward not started). All data seen in development is seeded
test data (test organizations, branches, merchants, shipments). Future design
work must not fabricate testimonials, customer logos, or usage metrics.

## Product Principles

1. **Physical reality over convenience.** Every status shown in the UI must
   trace back to a real scan, manifest, or delivery-attempt event — never let
   a screen imply a state change that didn't actually happen operationally.
2. **Server is the source of truth for anything sensitive.** Tenant scope,
   branch scope, role/permission, price, and COD amounts are always rendered
   from server data and never computed, overridden, or assumed client-side.
3. **Full traceability, no silent adjustments.** Any correction to a financial
   or status value must be visible in the UI as an audited change with
   reason and before/after values, not a quiet overwrite.
4. **Design for the branch counter and the back office, not a storefront.**
   This is Operate-mode software for people doing scan-heavy, permission-gated
   operational work under time pressure — clarity, density, and correctness
   outrank marketing polish.
5. **Sheet-over-pages, one design system.** New surfaces extend the
   already-established shadcn/ui-based system and Sheet-driven interaction
   pattern rather than introducing a competing visual or navigational idiom.

## Accessibility & Inclusion

WCAG 2.1 AA is the binding, formal standard for all future UI work (confirmed
2026-08-29) — not best-effort. This continues the rigor already applied in
Phase F0/F1: computed contrast ratios for status colors, colorblind-safe hue
separation (checked against protanopia ΔE), and semantic (non-color-only)
status indicators (`StatusBadge`'s dot + label pattern). Future design and
review passes should verify against this standard explicitly, not assume prior
passes generalize to new colors/components.
