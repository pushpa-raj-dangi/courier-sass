# Architecture

> This document describes the architecture as it actually exists in this repository as of
> 2026-08-29 (backend through Phase 13, frontend through Phase F2), plus the target direction
> for sections not yet built. Where a section describes something not yet implemented, it is
> marked **(target, not yet built)** — don't infer that clicking around the current app will show
> it.

## 1. Architecture Style

The application follows **Clean Architecture inside a modular monolith** (CLAUDE.md §2, §3).

There is:

* One deployable ASP.NET Core API
* One deployable Next.js frontend, talking to the API only through its own BFF route handlers
  (§9)
* One PostgreSQL database
* Explicit internal module boundaries
* Independent domain/application/infrastructure concerns
* No microservices in v1
* No distributed transactions
* No unnecessary CQRS/MediatR ceremony

The architecture is designed around two equally important concerns:

1. **Strong domain and application boundaries**
2. **A modern, dense, professional operational UI**

The product is an operations-heavy logistics platform. Therefore, the frontend should optimize for:

* Fast scanning
* High information density without visual clutter
* Clear status hierarchy
* Efficient tables and filters
* Fast keyboard/mouse workflows
* Responsive layouts
* Consistent interaction patterns
* Strong visual feedback
* Minimal cognitive load
* Professional enterprise SaaS aesthetics

---

## 2. Backend Module Architecture

Each module owns its own:

```text
Domain/
Application/
Infrastructure/
Presentation/
```

and its own EF Core entity configurations.

A module may depend on another module's **Application-layer interfaces**, but must never directly
access another module's Domain internals or database tables.

The actual module list in `src/Modules/` (some names differ from an earlier idealized sketch —
this is the real, current list):

```text
src/
├── Modules/
│   ├── Identity/          # Organization, User, Role, Permission, UserRole, UserBranch
│   ├── Geography/         # Province, District, LocalLevel, Ward, Area, DeliveryZone
│   ├── Branches/          # Branch and branch hierarchy
│   ├── Merchants/         # Merchant, MerchantUser, KYC, ApiKey
│   ├── Pricing/           # ServiceType, WeightSlab, RateCard, ZoneRate, AdditionalCharge,
│   │                      # MerchantRateCard (no separate RateCardRule or Pricing.Zone entity —
│   │                      # see docs/DOMAIN-MODEL.md for the deliberate simplifications)
│   ├── Shipments/         # Shipment, ShipmentItem, ShipmentEvent, custody, PickupRequest,
│   │                      # and public /track/{awb} (PublicTrackingEndpoints.cs) — there is no
│   │                      # separate Tracking module; public tracking is a Shipments concern
│   ├── Manifests/         # Manifest, ManifestShipment, ShipmentTransfer, scan validation
│   │                      # (named Manifests, not Scanning)
│   ├── Riders/             # Rider, Vehicle, DeliveryRun, DeliveryTask, DeliveryAttempt,
│   │                      # DeliveryProof (POD) — delivery/POD live inside Riders, not a
│   │                      # separate Delivery module. RiderAssignment does not exist;
│   │                      # Rider.BaseBranchId is the sole branch scope.
│   ├── Ndr/                # NDR, ReturnShipment — RTO lives inside Ndr as ReturnShipment,
│   │                      # not a separate RTO module. (Entity is `NDR`, all-caps, to avoid a
│   │                      # C# namespace collision with `Courier.Ndr`.)
│   ├── Cod/                # CODCollection, RiderCashSettlement, BranchCashSettlement,
│   │                      # FinancialTransaction, MerchantSettlement, MerchantSettlementItem,
│   │                      # Adjustment
│   ├── Notifications/      # Domain event → template → channel worker (email real via MailKit/
│   │                      # mailhog in dev; SMS/WhatsApp/Push are log-only stubs so far)
│   ├── Webhooks/           # WebhookSubscription, WebhookDelivery, HMAC signature, retry, replay
│   ├── Reports/            # Dashboards (org + branch), CSV exports — no owned entities/migration,
│   │                      # composes narrow lookup interfaces implemented in each owning module
│   ├── Events/              # OutboxMessage, OutboxPublisherJob, RabbitMqEventPublisher — the
│   │                      # outbox implementation itself (§5)
│   └── Audit/              # AuditLog
│
├── SharedKernel/
│   ├── ICurrentTenantProvider / IPermissionChecker / IBranchAuthorizationChecker /
│   │   IMerchantAuthorizationChecker
│   ├── IIdempotencyStore / IRateLimiter (Redis-backed implementations)
│   ├── IObjectStorage (+ LocalFileSystemObjectStorage for dev)
│   ├── PagedResult / PaginationDefaults
│   └── RabbitMqTopology
│
├── Api/                    # Composition root / API host
└── web/                    # Next.js frontend (lowercase `web`, not `Web`)
```

`SharedKernel` must remain intentionally small.

If multiple modules require substantial shared business logic, one module should own that concept
and expose an Application-layer contract.

Business logic must not migrate into `SharedKernel` simply for convenience.

---

## 3. Multi-Tenancy

Every tenant-owned entity contains:

```text
OrganizationId
```

as a non-nullable property.

EF Core global query filters enforce:

```text
OrganizationId == currentTenantId
```

for tenant-scoped entities.

The tenant is resolved server-side from the authenticated principal:

* JWT `org_id` claim for staff sessions
* API key record for merchant API calls

The tenant must **never** be trusted from:

* Request body
* Query string
* Route parameter
* Custom HTTP header supplied by the client

Unfiltered database access must always be explicit — a query that needs to reach across the
tenant filter (a background job, a lookup keyed by an already-verified id) uses
`.IgnoreQueryFilters()` with a comment explaining why, or takes `organizationId` as an explicit
parameter rather than relying on ambient context. See
`docs/CLARIFICATIONS.md` entry #12 for the specific gotcha this guards against: EF Core's tenant
`HasQueryFilter` evaluates to **false for every row** (not "unfiltered") whenever
`ICurrentTenantProvider.OrganizationId` is null, which is always true outside an HTTP request
(Hangfire jobs, `BackgroundService`s) — silently returning empty results is easy to miss without
an explicit end-to-end test.

**Recurring bug class worth grep-checking in any new module/accessor:** a table that has its own
`OrganizationId` column is filter-protected automatically, but a table that carries tenant
isolation only *transitively* (through a parent FK, e.g. a `RateCard`'s child rows) is not — a new
read method that queries the child table directly without joining back to the parent's
`OrganizationId` is a real, previously-caught cross-tenant leak shape in this codebase (hit in
Phases 4, 5, 10, and 12 in slightly different forms — see `[[project-courier-roadmap-status]]`
memory for specifics).

---

## 4. Branch Scope

Authorization evaluates:

```text
Permission
+
Branch Scope
```

for every protected request.

The effective branch scope comes from:

```text
UserBranch
UserRole.BranchScope
```

and is resolved server-side.

The client may display branch context but cannot grant itself access to another branch.

---

## 5. Domain Events and Reliability

Aggregates raise domain events.

Application services collect those events and persist them through the outbox pattern (the
`Events` module: `OutboxMessage`, `OutboxWriterService`, `OutboxPublisherJob`,
`RabbitMqEventPublisher`).

Events are **not** published directly to RabbitMQ inside the business transaction.

The transaction contains:

```text
Business State Change
        +
OutboxMessage
```

The background publisher (a Hangfire recurring job, `outbox-publisher`) then:

```text
Outbox
   ↓
RabbitMQ (topic exchange courier.domain-events)
   ↓
Consumers
   ↓
Notifications / Webhooks / Internal Reactions
```

This prevents:

```text
Shipment = Delivered
Event = Lost
```

scenarios caused by a process crash between database commit and message publication.

**Currently wired (Phase 11), as a deliberate proving set — not full breadth:**

```text
shipment.created
shipment.delivered
shipment.ndr_created
cod.collected
```

The remaining events CLAUDE.md §26 lists (`shipment.picked_up`, `shipment.in_transit`,
`shipment.out_for_delivery`, `shipment.delivery_failed`, `shipment.rto`, `cod.settled`) are not
yet wired — extend `NotificationDefaultTemplates`/webhook subscription types when a phase needs
them.

In-process reactions may use lightweight dependency injection when durability is not required.

Anything that:

* crosses a module boundary
* must survive process failure
* triggers external integration
* sends notifications/webhooks

must use the outbox.

**EF Core InMemory + transactions gotcha:** `Database.BeginTransactionAsync()` throws
`InvalidOperationException` unconditionally against EF Core's InMemory test provider — production
code must guard with `_dbContext.Database.IsRelational()` before opening a transaction (skip only
for a non-relational provider; never catch/swallow a real attempted call). The multi-`SaveChanges`
financial chains in `Cod` (rider/branch/finance settlement, merchant settlement + payout,
adjustment approval) are the first place this pattern was established — the first explicit
`BeginTransactionAsync()` usage anywhere in this codebase.

---

## 6. Infrastructure

### PostgreSQL

PostgreSQL is the primary relational database.

EF Core owns persistence for module aggregates.

Each module owns its EF Core configurations.

Database access must respect:

```text
Tenant Boundary
Branch Boundary
Module Boundary
```

**Enum persistence gotcha:** `ShipmentStatus`/`ShipmentEventType` (and most other domain enums)
persist as plain PostgreSQL `integer` with no `HasConversion<string>()`. Any future migration that
adds enum members must **append at the end, never insert mid-list** — inserting reassigns every
later member's ordinal against already-persisted rows (this happened once, in Phase 9, and was
caught only at final whole-branch review — see `docs/CLARIFICATIONS.md` entry #10). Separately,
there is still no global `JsonStringEnumConverter` registered for the API — every enum-typed API
field serializes as a raw integer over the wire *unless* it individually carries
`[JsonConverter(typeof(JsonStringEnumConverter))]` on its own declaration. This was first fixed,
narrowly, on `BranchType` during Frontend Phase F2's manual end-to-end verification, and the same
recurring gap has since been found and fixed enum-by-enum in nearly every phase through F11 (full
list and phase-by-phase detail in `docs/CLARIFICATIONS.md` #10, #14-#20) — it is not a
comprehensively-closed class of bug, only a repeatedly-patched one. Any enum newly exposed on an
API response should be checked for this converter before being considered done, and any enum not
yet reached by a frontend phase's own E2E pass should still be assumed to be missing it.

### Redis

Currently used for:

```text
Merchant API rate limiting (IRateLimiter)
Merchant API idempotency keys (IIdempotencyStore)
```

**(target, not yet built):** general read caching for Geography, Permissions, and Rate Cards is a
reasonable future use of Redis, but is not implemented yet — every geography/permission/pricing
read currently goes straight to Postgres. Redis must not become the primary source of business
truth. Redis is also a hard fail-closed dependency for shipment creation and public tracking today
(a deliberate decision — never silently skip idempotency on a financial write path — but a
documented single point of failure).

### RabbitMQ

RabbitMQ handles durable asynchronous integration events, consumed by the `Events` module's
publisher and, downstream, `Notifications`/`Webhooks`.

RabbitMQ is not used simply to make synchronous application code appear "distributed."

### Hangfire

Hangfire currently runs two recurring jobs:

```text
outbox-publisher   — publishes OutboxMessage rows to RabbitMQ
webhook-retry      — retries failed WebhookDelivery attempts with exponential backoff
```

**(target, not yet built):** settlement generation, NDR SLA reminders, scheduled notifications,
and expired branch grant cleanup are reasonable future Hangfire jobs, not implemented yet. The
Hangfire dashboard is mounted inside the API behind an authorized route.

---

## 7. Observability

Structured Serilog logging is in place. Request logging includes method/path/status/duration.

**(target, not yet built):** a propagated correlation/trace ID through
`HTTP → Application Service → Domain Event → Outbox → RabbitMQ → Background Job`, and
OpenTelemetry instrumentation for HTTP/EF Core/RabbitMQ/Hangfire. Full observability
infrastructure is not required for the first development phases and has not been prioritized yet.

---

## 8. Frontend Overview

The frontend is a **Next.js + TypeScript enterprise SaaS application** using the App Router, built
as a BFF (Backend-for-Frontend): the browser never talks to the ASP.NET Core API directly. Every
request goes through a Next.js Route Handler under `app/api/*`, which holds the JWT in an httpOnly
cookie and proxies to the real API server-side. This keeps the access token out of reach of any
client-side JS (XSS blast-radius reduction) and lets the BFF layer normalize errors/pagination
before they reach a component.

```text
Page / Sheet component
        ↓
Feature query hook (TanStack Query, calls clientFetch directly — see §26)
        ↓
clientFetch() — client-side fetch wrapper, throws typed SessionExpiredError on 401
        ↓
Next.js Route Handler under app/api/{feature}/... (BFF — holds the httpOnly session cookie)
        ↓
apiFetch() — server-side authenticated fetch, throws on missing/expired session
        ↓
ASP.NET Core API
        ↓
Application Service → Domain → Infrastructure → PostgreSQL
```

**One deliberate exception to this diagram:** public tracking (`/track`, `/track/{awb}`, Frontend
Phase F11) is genuinely anonymous end to end — no session exists to hold, so its BFF route
(`app/api/track/[awb]/route.ts`) calls the API directly via plain `fetch`, not `apiFetch` (which
requires a valid session and throws otherwise), and its client-side hook does not use the shared
`clientFetch` either (see `docs/CLARIFICATIONS.md` #20 for why: public tracking needs the real HTTP
status and a response header `clientFetch` discards). Any future unauthenticated route should
follow this same pattern, not force one of the two authenticated helpers to fit.

The frontend architecture prioritizes operational efficiency and visual quality.

**Actual current folder structure** (Frontend Phases F0–F2 built so far — this is real, not
aspirational; it deliberately differs from an earlier idealized sketch, noted inline):

```text
src/
├── app/
│   ├── (staff)/           # authenticated staff shell (route group) — layout.tsx mounts AppShell
│   │   │                  # plus every feature's globally-available Sheet components (§9a)
│   │   ├── dashboard/      # org dashboard (note: (staff)/dashboard, not a (dashboard) group)
│   │   └── branches/       # list page only — detail/create are Sheets, not routes (§9a)
│   ├── api/                # BFF route handlers, one subtree per feature — provinces, districts,
│   │   │                   # local-levels, wards, branches, delivery-zones, reports/dashboard, auth
│   │   └── ...
│   └── login/
│
├── features/                # one folder per domain feature — currently: auth, branches,
│   │                        # geography, reports. (shipments, merchants, riders, cod, ndr, rto,
│   │                        # tracking, settings do not exist yet — later phases)
│   └── {feature}/
│       ├── components/      # feature-specific components, including any {Feature}CreateSheet.tsx
│       │                    # / {Feature}DetailSheet.tsx (§9a)
│       ├── hooks/            # TanStack Query hooks — call clientFetch directly; there is
│       │                    # deliberately no separate services/ or queries/ subfolder per
│       │                    # feature (CLAUDE.md §31's original sketch proposed one; the hook
│       │                    # IS the service in this codebase's actual convention)
│       ├── store/             # only where the feature needs cross-component UI state (e.g.
│       │                    # branchPanelStore.ts for the detail/create Sheet's open/id state)
│       │                    # — most features don't need one
│       ├── types.ts
│       └── schema.ts          # Zod schemas, RHF-consumed
│
├── components/
│   ├── ui/                  # shadcn/ui primitives, hand-authored where the CLI's interactive
│   │                        # prompt couldn't be scripted (button, card, dialog, dropdown-menu,
│   │                        # input, label, select, separator, sheet, skeleton, spinner, table,
│   │                        # avatar, badge)
│   ├── layout/               # AppShell, TopNav, Sidebar, MobileNav, BranchSwitcher, ReauthDialog
│   ├── data-table/            # generic DataTable<T> (built on the shadcn Table primitive)
│   ├── states/                # EmptyState, ErrorState
│   └── status-badge/          # StatusBadge + statusConfig.ts (single source of truth for
│                              # status → label/color across the whole app, §17)
│
├── lib/
│   ├── api/clientFetch.ts     # client-side fetch, session-expiry handling
│   ├── auth/apiFetch.ts       # server-side authenticated fetch (used only inside app/api/*)
│   ├── store/                 # app-wide (not feature-scoped) Zustand stores: uiStore
│   │                          # (activeBranchId, sidebarCollapsed), authStore
│   ├── env.ts
│   └── utils.ts
│
└── services/                  # thin — currently just authService.ts; most features don't have
                               # one (their hooks call clientFetch directly, see above)
```

There is no top-level `hooks/`, `stores/`, `types/`, or `config/` directory as an earlier sketch
proposed — state, types, and hooks live inside the feature that owns them, plus the two
app-wide exceptions in `lib/store/` (branch selection, sidebar collapse — genuinely cross-feature
concerns, not one feature's business).

---

## 9. Frontend State Management

### TanStack Query

TanStack Query owns **server state**: API data, loading/error states, cache, refetching,
mutations, query invalidation. Currently covers session/auth, the org and branch dashboards, and
all Branches data. Shipment/merchant/rider/COD/reports queries will follow the same pattern as
those features are built.

### Zustand

Zustand is reserved for genuinely global client-only state — never a second server-state system.

**Currently in use:**

```text
uiStore (lib/store/uiStore.ts)     — activeBranchId, sidebarCollapsed
authStore (lib/store/authStore.ts)  — client-visible auth/session bits
branchPanelStore (features/branches/store/branchPanelStore.ts) — which detail/create Sheet is
                                       open and for which record id (§9a)
```

**(target, not yet built):** command palette state, per-table density/column-visibility
preferences. Add these only when the feature that needs them actually gets built — don't
pre-create empty stores.

---

## 9a. Detail and Create UI Pattern — Sheets, Never Separate Pages

**Every feature's detail and create UI is an instantly-opened, right-side shadcn `Sheet`, never a
separate routed page or `next/link` navigation.** This is a standing, explicit convention (user's
direction during Frontend Phase F2, retrofitted onto Branches immediately) that applies to every
future feature — Shipments, Merchants, Riders, COD, NDR, RTO, everything.

The concrete shape, established by `features/branches/`:

* A feature-scoped Zustand store (e.g. `features/branches/store/branchPanelStore.ts`) holds
  `{ mode: "closed" | "create" | "detail", id? }` plus `openCreate()`, `openDetail(id)`,
  `close()`. Panel state lives in the store — **never** in the URL as a route param or query
  string, and never via `<Link>`.
* The Sheet components (`{Feature}CreateSheet.tsx`, `{Feature}DetailSheet.tsx`) mount once,
  globally, in `app/(staff)/layout.tsx` alongside `{children}` — not inside the feature's own
  list page. This is what lets any component anywhere in the staff app (a list row, the
  `BranchSwitcher` in `TopNav`, a future dashboard quick-link) open the panel via the store,
  without needing to be a descendant of the list page that happens to render it.
* List rows and "New X" buttons are plain `<button onClick={() => store.openX(...)}>` —
  never `next/link`/`<Link>` for this purpose.
* A TanStack Query hook consumed only from a globally-mounted Sheet (e.g. `useBranch(id)`,
  `useBranchDashboard(id)`) needs `enabled: id !== ""` (or equivalent) guarding, since the hook
  is now always mounted regardless of whether its Sheet is open, and must not fire with an empty
  id while closed.
* On successful create, the flow calls `openDetail(created.id)` to switch panels in place —
  there is no detail page to `router.push` to.

**Why:** separate pages per record made navigation feel heavy for an operations-dense app where
the user is scanning a list and popping into records constantly; an instant slide-over keeps the
list's context in view. This does **not** vary by form complexity — even Branches' multi-section
create form (geography cascading pickers, parent branch, delivery zone) is a Sheet, not a full
page. There is no "complex workflows get a full page" carve-out.

---

## 10. Design System

The frontend must use a **consistent design system**, rather than allowing each feature to invent
its own UI.

The visual language should be:

> **Modern enterprise SaaS + operational density + restrained monochrome foundation + semantic
> accent colors.**

The UI should feel closer to a polished modern logistics/operations platform than a generic CRUD
dashboard. This is the direction Phase F0's design tokens, established via the `frontend-design`
and `impeccable` skills, already target.

**`PRODUCT.md` and `DESIGN.md` at the repo root** are the `impeccable` skill's own durable
context files — product/user context and the documented design system (palette, type, the
design-token sidecar in `.impeccable/design.json`) — auto-maintained by the skill across
sessions and committed like any other doc. `.impeccable/config.json` (shared) is committed
alongside them; `.impeccable/config.local.json`, `.impeccable/hook.cache.json`, and
`.impeccable/live/` are per-developer/session state and gitignored. Frontend design work should
let the skill keep these in sync rather than hand-maintaining a duplicate description here — this
section stays the narrative summary, `DESIGN.md` stays the source of truth for exact values.

Primary visual characteristics:

* Clean
* Professional
* Compact
* Spacious where hierarchy requires it
* Information-dense where operations require it
* Minimal unnecessary decoration
* Strong typography
* Clear status hierarchy
* Consistent spacing
* Consistent borders/radius
* Subtle shadows
* Excellent hover/focus states

Avoid:

* Excessive gradients
* Excessive colored cards
* Giant dashboard statistics
* Decorative illustrations in operational screens
* Excessive rounded containers
* Random icon usage
* Inconsistent spacing
* Rainbow-colored status badges

---

## 11. Design Tokens

The application uses centralized design tokens, defined as HSL CSS custom properties in
`app/globals.css`.

Tokens cover:

```text
Colors (including per-status tokens — status-created, status-in-transit,
        status-out-for-delivery, status-delivered, status-ndr, status-rto, status-cancelled)
Typography
Spacing
Border Radius
Shadows
Component Heights
```

**(target, not yet built):** dedicated elevation, motion, layout-width, and z-index token scales
— not yet formalized as named tokens, though used ad hoc where needed.

The visual foundation is predominantly neutral. Semantic colors are reserved for meaning:

```text
Success → delivered / completed / healthy
Warning → pending / attention required
Destructive → failed / cancelled / overdue
Info → informational state
Neutral → inactive / draft / unknown
```

Color must communicate meaning rather than decorate the interface. Every categorical/status
palette in this codebase is validated for colorblind-safety via the `dataviz` skill's
`validate_palette.js` before shipping — this caught a real defect once (`DeliveryFailed` and
`NdrCreated` originally had separate lightness steps of the same vermillion hue that failed the
normal-vision adjacent-pair floor; they now deliberately share one token, since the backend
auto-transitions one into the other in the same request anyway).

---

## 12. Typography

Typography is a primary part of the visual hierarchy.

Use a modern sans-serif type system with clear distinction between:

```text
Page Title
Section Title
Card Title
Body
Metadata
Table Data
Labels
Helper Text
Status Text
```

Operational tables should prioritize readable numbers, compact line height, tabular numerals, and
clear column hierarchy. Numbers such as COD amount, shipment count, weight, distance, and
settlement amount should be visually easy to scan.

---

## 13. Application Shell

The authenticated app uses `AppShell` (`components/layout/AppShell.tsx`):

```text
┌───────────────────────────────────────────────────────┐
│ TopNav — BranchSwitcher, user menu                     │
├──────────────┬────────────────────────────────────────┤
│              │                                        │
│ Sidebar      │ Main Content                           │
│ (Nav)        │                                        │
│              │ Page content (Sheets overlay from      │
│ MobileNav on │ the right when a detail/create panel    │
│ small screens│ is open — §9a)                          │
└──────────────┴────────────────────────────────────────┘
```

Built: collapsible sidebar (`uiStore.sidebarCollapsed`), active navigation state, branch context
via `BranchSwitcher` (renders nothing for a single-branch user — a switcher implies a choice),
responsive/mobile navigation via `MobileNav`, session-expiry handling via `ReauthDialog` (a popup
password-entry dialog, not a redirect-to-login or toast).

**(target, not yet built):** global search, command palette, a notifications bell/center. Use
shadcn/ui's official blocks as the baseline when building these — CLAUDE.md and
`[[feedback-courier-frontend-shadcn-blocks]]`'s established convention for this project.

The sidebar should remain visually quiet. The active navigation item should provide clear
hierarchy without excessive color.

---

## 14. Navigation Architecture

Navigation is permission-aware — `Sidebar.tsx` filters each nav item by `usePermission`, and hides
(not disables-and-errors) anything the user can't access.

**Actual current nav** (Frontend Phases F0–F2):

```text
Dashboard   (report.read)
Shipments   (shipment.read)   — nav item exists; /shipments page not yet built (Phase F3)
Branches    (branch.read)     — built
COD         (cod.read)        — nav item exists; /cod page not yet built (later phase)
```

**(target — the eventual full nav tree, built out feature-phase by feature-phase, not yet
built beyond what's listed above):**

```text
Dashboard

Operations
  Shipments
  Scanning
  Manifests
  Delivery
  NDR
  RTO

Network
  Branches
  Riders
  Vehicles

Commercial
  Merchants
  Pricing
  COD
  Settlements

Analytics
  Reports
  Dashboard

Administration
  Users
  Roles & Permissions
  Settings
  Audit
```

Navigation should not expose inaccessible modules and then show authorization errors after
clicking — and, per the note above, should not link to a route that doesn't exist yet either; when
building a new nav group, ship the nav item and its page in the same phase.

---

## 15. Dashboard Design

Dashboards must avoid the traditional "Huge Card / Huge Number" repeated pattern. Instead they
combine key metrics, operational trends, exception queues, and performance data in one glance —
this is already the shape of the built Organization Dashboard (`ShipmentFunnel`, `RateTiles`,
`FinancialSummary`) and Branch Dashboard (`BranchShipments`, `BranchExceptions`, the latter now
rendered inside the branch's detail Sheet per §9a rather than a standalone page).

```text
┌───────────────────────────────────────────────────────────┐
│ Overview                         Branch: Kathmandu ▼       │
│ Today · Last 7 Days · Last 30 Days                        │
├───────────────────────────────────────────────────────────┤
│ Shipments     Delivered     Pending       COD             │
│ 12,842        10,431       1,208         NPR 4.8M         │
├──────────────────────────────┬────────────────────────────┤
│ Shipment Trend               │ Delivery Performance       │
│                              │                            │
│          chart               │       chart / metrics      │
├──────────────────────────────┼────────────────────────────┤
│ Exceptions                   │ Recent Activity            │
│ NDR / RTO / Failed Delivery  │                            │
└──────────────────────────────┴────────────────────────────┘
```

Important operational information should appear above decorative analytics. Trend charts /
Recent Activity are **(target, not yet built)** — the current dashboards show the top metrics row
and exception/status breakdowns, not yet a time-series trend chart.

---

## 16. Data-Heavy Screens

The application will contain operationally dense screens (shipment list, manifest list, rider
tasks, COD collections, settlement records, merchant transactions, audit logs) — most not yet
built beyond Branches' list.

The generic `DataTable<T>` component (`components/data-table/DataTable.tsx`) currently supports:
loading/empty/error states, pagination (`PagedResult<T>` contract), row click handling.

**(target, not yet built):** sticky headers, column visibility toggling, sorting, filtering,
search, row selection, bulk actions, density control. Add these to `DataTable` generically as the
first feature that genuinely needs one requires it — don't build one-off table components per
feature.

Avoid forcing every table into a mobile card layout when doing so destroys information density.

---

## 17. Shipment UI (target, not yet built — Frontend Phase F3)

Shipment management is a primary workflow. The shipment details page should use a structured
operational layout:

```text
Shipment Header
├── AWB
├── Status
├── Service Type
├── Merchant
├── COD
└── Actions

Shipment Summary
├── Origin
├── Destination
├── Customer
├── Package
└── Financial Summary

Tracking Timeline
├── Created
├── Picked Up
├── In Transit
├── At Branch
├── Out for Delivery
└── Delivered

Operational Information
├── Scan History
├── Transfer History
├── Delivery Attempts
├── NDR
├── RTO
└── POD
```

Per §9a, this renders as a detail Sheet, not a separate route — the most important information
must be visible without navigating through multiple pages or panels.

---

## 18. Status System

Statuses have a consistent visual language across the whole application, driven by one source of
truth: `components/status-badge/statusConfig.ts` maps every backend status string to a
`{ label, token }` pair, and `StatusBadge` renders `Icon + Label` from it — never color alone, and
never a duplicated mapping in a second file. The same status always uses the same treatment across
tables, cards, timelines, detail views, and filters, because they all resolve through this one
config.

```text
Created → Picked Up → In Transit → At Branch → Out for Delivery → Delivered
                                                        ↘ NDR → RTO
Cancelled
```

---

## 19. Forms

All forms use React Hook Form + Zod. Backend validation uses FluentValidation; frontend schemas
mirror backend rules where practical (matching Zod v4's `{ error: "..." }` API, not v3's
`required_error`/`invalid_type_error`).

Forms provide: clear labels, required indicators, inline validation, helpful error messages,
proper field grouping, keyboard navigation, a loading state during submission (disabling duplicate
submits), and server error handling.

Complex forms use sections rather than one extremely long scroll — Branches' create form
(identity fields, cascading geography picker, contact/hours, parent branch) is the established
example. **All detail/create forms render inside a Sheet per §9a — there is no separate "simple
forms get a sheet, complex forms get a full page" split.** A long form scrolls inside the Sheet.

---

## 20. Command and Search UX (target, not yet built)

Global search, a command palette, and quick actions (search AWB, search merchant, search rider,
create shipment, scan shipment, create manifest, record delivery attempt, open COD settlement) are
a reasonable future addition once enough features exist to search across, but none of this is
built yet.

---

## 21. Loading, Empty and Error States

Every data screen explicitly handles loading, empty, and error states, using shared components:
`Spinner` (shadcn) for loading, `EmptyState`/`ErrorState` (`components/states/`) for the other
two, each with an icon, a clear explanation, and (for empty) a primary action or (for error) a
retry action. A permission-denied case shows a clear authorization message, never a misleading
empty state.

Do not create custom inconsistent spinners throughout the application — always the shared
`Spinner`.

---

## 22. Feedback and Notifications

Confirmation for a destructive/consequential action uses the hand-authored `Dialog` primitive
(`components/ui/dialog.tsx`, built on `@radix-ui/react-dialog`) — e.g. `ReauthDialog` for
session-expiry re-authentication.

**(target, not yet built):** a toast system, and confirmation dialogs for the specific destructive
actions CLAUDE.md §22/§28 call out for later features (cancel shipment, deactivate rider, reverse
settlement, remove branch access) — none of those flows exist yet.

---

## 23. Responsive Design

The app supports desktop, tablet, and mobile, but responsive design does not mean blindly
converting every desktop table into cards.

**Built:** `MobileNav` (a `Sheet`-based mobile navigation drawer, added during Phase F1's polish
pass after a real gap was found — zero breakpoint classes anywhere and the `Sheet` component sat
unused), responsive breakpoints on the app shell and dashboard tile grids.

Complex administrative workflows may remain desktop-first where mobile conversion would materially
reduce usability — not yet a concrete decision point since no operationally-dense mobile-specific
flow (rider app surfaces, scanning) has been built.

---

## 24. Accessibility (target — partially met, not fully audited)

The frontend targets WCAG 2.1 AA-level practices: keyboard navigation, visible focus states,
semantic HTML, accessible labels, proper dialog behavior, sufficient contrast, no color-only
meaning (§18's icon+label status badges satisfy this one directly), accessible tables and form
errors. Not yet subject to a dedicated accessibility audit pass.

---

## 25. Component Strategy

Use shadcn/ui as the foundation. **Every control — from a full page pattern down to a single form
input — must be a shadcn/ui block or component, never a raw HTML primitive (`<input type="date">`,
`<input type="checkbox">`, a hand-rolled `role="tablist"` button row, a bespoke Previous/Next
pager) and never a from-scratch reimplementation of something shadcn already ships.** Install via
`npx shadcn add <name>` and restyle onto this project's design tokens rather than rebuilding the
structure by hand. This convention applies starting Frontend Phase F1 onward; Phase F0's hand-built
`AppShell`/`Sidebar`/`LoginForm` predate it and aren't retroactively required to change. `npx shadcn
add <name> -y -o` runs non-interactively in this environment (confirmed working as of the
Pagination install below) — pass `-o` whenever the component's dependency graph touches an
already-installed file (e.g. `pagination` pulls in `button`), since the CLI otherwise stops on an
overwrite prompt it can't answer non-interactively without that flag; diff first with `npx shadcn
diff <name>` if an existing file might carry local edits worth preserving.

**Currently installed** (`components/ui/`): `avatar`, `badge`, `breadcrumb`, `button`, `calendar`,
`card`, `chart`, `checkbox`, `date-picker` (custom, `Calendar`+`Popover`-based, not a shadcn
registry item), `date-time-picker` (same), `dialog`, `dropdown-menu`, `input`, `label`,
`pagination`, `popover`, `select`, `separator`, `sheet`, `sidebar`, `skeleton`, `spinner`, `table`,
`tabs`, `textarea`, `tooltip`. `DataTable` (§13) drives its pager through `pagination`'s
`Pagination`/`PaginationContent`/`PaginationItem`/`PaginationPrevious`/`PaginationNext` rather than
a hand-built `<Button>` pair — the last raw-primitive control in the shared component layer was
retired this way; every list page routes through `DataTable`, so no other hand-built pagination
exists anywhere in `src/web`.

Every visible component should trace back to a shadcn primitive or block, restyled — never a
from-scratch reimplementation of something shadcn already provides.

---

## 26. Feature UI Boundaries

Feature-specific components live inside their feature. Actual current shape (see §8's tree) —
`components/`, `hooks/`, `store/` (only when needed), `types.ts`, `schema.ts`. There is
deliberately no separate `services/`/`queries/`/`schemas/` subfolder per feature: the hook is the
service (it calls `clientFetch` directly), and `schema.ts` is a single file, not a folder, since
one feature rarely needs more than a handful of Zod schemas.

Shared components must remain presentation-oriented. Business rules should not be hidden inside
generic UI components.

---

## 27. API and Frontend Data Flow

See §8 for the actual flow including the BFF hop — the frontend never talks to the ASP.NET Core
API directly from the browser. The frontend must not contain business decisions that belong to
the backend.

---

## 28. Security Boundaries

The frontend may hide unavailable actions for UX purposes, but this is **not authorization**. The
backend remains authoritative for tenant, branch, role, permission, price, COD, shipment state,
and every other financial value. Never trust `organizationId`, `branchId`, `role`, `permission`,
`price`, `COD`, or `settlement amount` from client-controlled input — the frontend treats every
server-returned value as authoritative and echoes it, never computes it independently.

---

## 29. Caching Strategy

TanStack Query owns application-level server caching on the frontend. Redis is currently used for
rate limiting and merchant API idempotency (§6) — general read caching (geography, rate cards,
permissions) is a reasonable future addition, not implemented yet. Do not cache highly mutable
financial or operational state without an explicit invalidation strategy.

---

## 30. Object Storage

Object storage is abstracted behind `SharedKernel.IObjectStorage`. Local development uses
`LocalFileSystemObjectStorage`; production may use S3/Azure Blob without application code needing
to know which. Current use case: POD photos/signatures (Riders module), KYC documents (Merchants
module) — both via the same raw-body-upload pattern.

---

## 31. Deployment Architecture

Docker Compose is used for v1.

```text
                         ┌──────────────┐
                         │    Browser   │
                         └──────┬───────┘
                                │
                    ┌───────────┴───────────┐
                    │                       │
              ┌─────▼─────┐           ┌────▼────┐
              │   Next.js │           │   API    │
              │    Web    │──BFF────▶│  .NET    │
              └───────────┘           └────┬─────┘
                                          │
                ┌─────────────────────────┼────────────────────┐
                │                         │                    │
          ┌─────▼─────┐             ┌─────▼─────┐       ┌─────▼─────┐
          │ PostgreSQL│             │   Redis   │       │ RabbitMQ  │
          └───────────┘             └───────────┘       └─────┬─────┘
                                                             │
                                                       ┌─────▼─────┐
                                                       │ Hangfire  │
                                                       │(inside API)│
                                                       └───────────┘
```

Compose services: `postgres`, `redis`, `rabbitmq`, `mailhog` (dev-only SMTP capture for
Notifications' email channel), `api`, `web`. The Hangfire dashboard is mounted inside the API
behind an authorized route, not a separate service.

---

## 32. Architecture Principles

### Backend

```text
Domain owns business rules.
Application owns use cases.
Infrastructure owns external concerns.
Presentation owns HTTP/API concerns.
```

### Frontend

```text
Features own feature workflows.
Components own presentation.
TanStack Query owns server state.
Zustand owns client-only global state (app-wide) and per-feature panel/UI state (§9a).
Detail and create UI is always a Sheet, never a separate route (§9a).
Backend owns business decisions.
Design system owns visual consistency.
```

### System

```text
Tenant boundary is mandatory.
Branch boundary is mandatory.
Financial operations are authoritative on the server.
Domain events are durable through the outbox.
External side effects are asynchronous where appropriate.
```

---

## 33. What This Architecture Deliberately Avoids

**No microservices.** v1 remains a modular monolith. Do not introduce microservices simply because
modules exist — modules provide boundaries without operational complexity.

**No CQRS/MediatR ceremony.** Application services expose clear methods (`CreateShipment()`,
`GetShipment()`, `AssignRider()`, `RecordDeliveryAttempt()`, `GenerateSettlement()`, etc.). Do not
introduce MediatR merely to wrap every method in a command/handler pair.

**No business logic in controllers or React components.** Controllers/API endpoints orchestrate
HTTP concerns; business rules live in Domain/Application. React components render state and
collect user interaction; business decisions belong to the backend.

**No UI duplication.** Do not create multiple versions of Table, Modal, Dialog, Sheet, Form Field,
Status Badge, Page Header, Empty State, or Loading State when the same pattern already exists —
extend the design system instead.

**No visual inconsistency.** Feature teams must not independently invent colors, spacing, border
radius, typography, button styles, status colors, or table patterns. All follow the shared design
system.

---

## 34. Definition of a "Done" UI

A feature is not frontend-complete simply because the API works. A production-ready screen
includes: responsive layout, loading/empty/error/permission states, success feedback, validation
feedback, accessible interactions, consistent spacing/typography/status treatment, correct
responsive behavior, keyboard-friendly primary workflows, no unnecessary visual decoration, and
uses existing design-system components (never a one-off).

For operational screens, also: search, filtering, sorting, pagination, bulk actions where
relevant, a clear primary action, and efficient information density.

---

## 35. Product UX Direction

The product should feel like a **premium modern logistics SaaS platform**, not an internal CRUD
administration panel.

```text
Professional + Minimal + Dense + Fast + Modern + Operational
```

The interface communicates hierarchy through typography, spacing, alignment, surface elevation,
borders, icons, semantic color, and motion — rather than relying heavily on color.

> **Every visual element must either improve hierarchy, communicate state, or improve workflow
> speed.** If an element does none of these, it probably shouldn't exist.
