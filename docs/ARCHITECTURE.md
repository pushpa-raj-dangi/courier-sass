# Architecture

## 1. Style

Clean Architecture inside a **modular monolith** (CLAUDE.md §2, §3). One deployable
API process, one deployable Next.js frontend, organized internally into independent
modules with explicit boundaries. No microservices, no distributed transactions, in
v1.

## 2. Module List

Each module owns its own `Domain / Application / Infrastructure / Presentation`
folders and its own EF Core entity configurations. A module may depend on another
module's **Application-layer interfaces**, never reach directly into another module's
Domain internals or database tables.

```text
src/
├── Modules/
│   ├── Identity/          # Organization, User, Role, Permission, UserRole, UserBranch
│   ├── Geography/         # Province, District, LocalLevel, Ward, Area, DeliveryZone
│   ├── Branches/          # Branch, branch hierarchy
│   ├── Merchants/         # Merchant, MerchantUser, KYC, ApiKey
│   ├── Pricing/           # RateCard, RateCardRule, ServiceType, WeightSlab, Zone, ZoneRate
│   ├── Shipments/         # Shipment, ShipmentItem, ShipmentEvent, custody
│   ├── Scanning/          # Manifest, ManifestShipment, ShipmentTransfer, scan validation
│   ├── Riders/             # Rider, Vehicle, RiderAssignment, DeliveryRun, DeliveryTask
│   ├── Delivery/          # DeliveryAttempt, DeliveryProof (POD)
│   ├── NDR/               # NDR entity + workflow
│   ├── RTO/               # ReturnShipment / return lifecycle
│   ├── COD/               # CODCollection, RiderCashSettlement, BranchCashSettlement,
│   │                       # MerchantSettlement, FinancialTransaction
│   ├── Notifications/     # Domain event → template → channel worker
│   ├── Webhooks/          # WebhookSubscription, WebhookDelivery, signature, replay
│   ├── Tracking/          # Public /track/{awb} read model
│   ├── Reporting/         # Dashboards, exports
│   └── Audit/             # AuditLog (cross-cutting, written to by all modules)
├── SharedKernel/          # Result types, domain event base, value objects (Money, GeoPoint),
│                           # base entity/aggregate root — deliberately thin, no business logic
├── Api/                   # Presentation host: controllers/minimal APIs, composition root
└── Web/                   # Next.js app (see FRONTEND section of this doc)
```

`SharedKernel` must stay small. If two modules seem to need to share real business
logic, that is a signal one of them owns the concept and the other should depend on
its Application interface instead of duplicating logic into SharedKernel.

## 3. Cross-Cutting Concerns

### Multi-tenancy enforcement

* Every tenant-owned entity has a non-nullable `OrganizationId`.
* An EF Core **global query filter** applies `OrganizationId == currentTenantId` to
  every tenant-scoped `DbSet`. The current tenant is resolved server-side from the
  authenticated principal's claims (JWT `org_id` claim for staff sessions, `ApiKey`
  record for merchant API calls) — never from a request body/header/query param
  (CLAUDE.md §33).
* `SuperAdmin` platform operations run through a separate, explicitly-named
  `IPlatformDbContext`/unfiltered access path so that the "no tenant filter" case is
  never accidental — it requires an explicit opt-in, and every use is audited
  (`CLARIFICATIONS.md` #5).

### Branch scope enforcement

* Authorization middleware/attribute checks two things per request: does the user
  hold the required `permission`, and does the user's `UserBranch`/`UserRole.BranchScope`
  grant cover the `BranchId` the request targets (see `RBAC.md`).
* Branch scope is resolved server-side the same way tenant scope is — never trusted
  from the client.

### Domain events & reliability (outbox pattern)

* Domain events (e.g. `ShipmentDelivered`, `CODCollected`, `NDRCreated`) are raised by
  aggregates and collected by the application service, not published directly to
  RabbitMQ inline with the DB transaction.
* An **outbox table** (`OutboxMessage`) is written in the same EF Core transaction as
  the business change. A background publisher (Hangfire recurring job) reads the
  outbox and publishes to RabbitMQ, marking messages delivered. This guarantees a
  status change and its event are never inconsistent (no "shipment says delivered but
  no webhook/notification fired" gap).
* In-process reactions within the same request (e.g. updating a denormalized
  dashboard counter) may use a lightweight in-memory dispatcher; anything that must
  survive a process crash or cross a module boundary goes through the outbox.

### Caching & rate limiting — Redis

* Query result caching for read-heavy, slow-changing data (geography, rate cards,
  permission sets).
* Distributed rate limiting for the Merchant API (per API key) and public tracking
  endpoint (per IP), per CLAUDE.md §25/§33.
* Idempotency-key storage for Merchant API POST requests (§25).

### Background jobs — Hangfire

* Outbox publisher, webhook delivery + retry, scheduled settlement generation,
  NDR SLA/follow-up reminders, expired `UserBranch` grant cleanup query support.

### Logging & observability

* Serilog structured logging with a correlation/trace ID attached to every request
  and propagated into domain events and background jobs.
* OpenTelemetry-ready: instrument HTTP, EF Core, RabbitMQ, and Hangfire so traces can
  be exported once a collector is wired up — do not block on standing up full
  observability infra in early phases.

## 4. Frontend Architecture

Next.js + TypeScript, feature-folder structure (CLAUDE.md §31):

```text
src/
├── app/                 # routing
├── features/
│   ├── auth/
│   ├── shipments/
│   ├── merchants/
│   ├── branches/
│   ├── riders/
│   ├── cod/
│   ├── reports/
│   └── tracking/
├── components/          # shared, presentation-only UI (shadcn/ui based)
├── lib/                 # api client, auth helpers, formatting
└── services/            # per-feature API service functions consumed by TanStack Query
```

* TanStack Query owns server state (fetching, caching, invalidation).
* Zustand only for genuinely global client-only state (e.g. active branch selector,
  sidebar collapse) — not a substitute for server state.
* React Hook Form + Zod for all forms; validation schemas mirror backend
  FluentValidation rules where practical to avoid drift.
* The frontend never computes or trusts price/COD/tenant/branch/role values it
  receives from user input — all such values are display-only echoes of what the
  server returned (CLAUDE.md §33).
* **Prefer shadcn/ui's official blocks over hand-built compositions** for standard
  patterns it already ships a well-fitting block for — sidebar navigation
  (`npx shadcn add sidebar-07` / `sidebar-03`), login/signup pages (`login-03`,
  `login-04`), and dashboard shells (`dashboard-01`) are known-good starting points
  as of this writing. Add the block, then restyle it onto this project's design
  tokens (Phase F0's palette/type system) rather than rebuilding the same structure
  from primitives — a custom composition is for a pattern shadcn has no block for,
  not the default. Loading states use shadcn's own `Spinner` component
  (`npx shadcn add spinner`, built on lucide's `LoaderIcon` with `animate-spin`),
  not a hand-rolled spinner. Every visible component should trace back to a shadcn
  primitive or block, restyled — never a from-scratch reimplementation of something
  shadcn already provides. Apply this starting Frontend Phase F1 onward; Phase F0's
  hand-built `AppShell`/`Sidebar`/`LoginForm` predate this convention and are not
  retroactively required to change unless a phase touching them decides to.

## 5. Deployment (Docker Compose, v1)

Services: `api` (ASP.NET Core), `web` (Next.js), `postgres`, `redis`, `rabbitmq`,
plus the Hangfire dashboard mounted inside `api` behind an authorized route. Object
storage (POD photos/signatures, KYC docs) is behind a storage abstraction interface so
local dev can use filesystem/minio while production targets S3/Azure Blob without
touching application code.

## 6. What This Architecture Deliberately Avoids

* No CQRS/MediatR "for ceremony" (CLAUDE.md §3) — application services expose plain
  commands/queries as methods; MediatR-style indirection is only introduced if a
  module later has a genuine need for pipeline behaviors (e.g. cross-cutting
  validation) that plain DI can't express cleanly.
* No business logic in controllers or React components (CLAUDE.md §3).
* No microservices, no distributed sagas, no eventual-consistency-across-services
  problems in v1 — the outbox pattern above solves the one reliability gap
  (DB-commit vs message-publish) that a monolith with an external broker actually has.
