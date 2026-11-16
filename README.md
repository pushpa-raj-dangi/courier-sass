# Courier SaaS — Nepal domestic courier & logistics platform

A multi-tenant SaaS for courier companies in Nepal: branches and hubs, riders, merchants, scan-driven
parcel movement, manifests between hubs, NDR and returns, cash-on-delivery reconciliation, a rider
PWA, a merchant portal with a REST API and webhooks, and public tracking.

The core idea is **traceability of the physical parcel**: every status change comes from a real
operation (a scan, a manifest, a rider assignment, a delivery attempt), so the system can always
answer *where is the parcel, who has it, which branch owns it, what happened, when, by whom, was
COD collected, and has the merchant been settled*.

## Features

- **Multi-tenant, multi-branch.** Every record belongs to an organisation; users get branch-scoped
  access (one branch, several, or all). Tenant and branch scope are enforced server-side.
- **Permission-based RBAC.** Roles are collections of permissions (`shipment.scan`, `cod.settle`, …);
  17 default roles from SuperAdmin to Merchant Staff.
- **Shipments.** AWB generation, a validated state machine (Created → Picked up → At origin → Sorted →
  In transit → At destination → Out for delivery → Delivered), append-only tracking events, custody
  history.
- **Scanning and manifests.** Camera or typed AWB scanning; hub-to-hub manifests (Draft → Sealed →
  In transit → Received → Closed) with receive reconciliation and missing-parcel detection.
- **Riders.** Delivery runs and tasks, live GPS, proof of delivery (signature, photo, GPS, OTP flag),
  delivery attempts and an offline-first rider PWA with a sync outbox.
- **NDR and RTO.** A follow-up workflow for failed deliveries and a linked return-to-origin chain.
- **COD.** A full money trail: expected → collected → rider deposited → branch reconciled → finance
  verified → merchant settlement → settled, with audited adjustments.
- **Pricing.** Versioned rate cards by zone, weight slab and service type; historical prices never
  change.
- **Nepal geography.** Province → district → local level → ward → area, driving zones, coverage and
  pricing.
- **Merchant portal and API.** Merchant users, KYC, API keys, idempotent REST API, signed webhooks
  with retries and replay.
- **Platform admin.** SuperAdmin console, tenant lifecycle, billing (eSewa and Khalti), audit log,
  jobs and queues.
- **International.** Cross-border shipments with customs and carrier hand-off.

## Tech stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core (.NET 10), C#, minimal APIs, modular monolith |
| Data | PostgreSQL 16, Entity Framework Core (Npgsql), Redis |
| Messaging and jobs | RabbitMQ, Hangfire, transactional outbox |
| Frontend | Next.js 14 (App Router), TypeScript, Tailwind CSS, shadcn/ui, TanStack Query, Zustand, React Hook Form + Zod |
| Testing | xUnit (unit, Postgres integration, API), Vitest + Testing Library |
| Infrastructure | Docker, Docker Compose |

## Repository layout

```text
src/
├── Api/                 ASP.NET Core host: composition root, middleware, endpoint mapping
├── Modules/             one folder per bounded context, each Domain / Application / Infrastructure / Presentation
│   ├── Identity         auth, users, roles, permissions, organisations
│   ├── Branches         branches, user-branch access, branch authorization
│   ├── Geography        Nepal provinces → wards, delivery zones
│   ├── Merchants        merchants, merchant users, KYC, API keys
│   ├── Pricing          service types, weight slabs, rate cards
│   ├── Shipments        shipment aggregate, state machine, tracking events
│   ├── Manifests        hub-to-hub transfers, receive reconciliation
│   ├── Riders           riders, vehicles, runs, tasks, attempts, proof of delivery, GPS
│   ├── Ndr              NDR workflow, return shipments
│   ├── Cod              COD collections, rider/branch/merchant settlements, adjustments
│   ├── Notifications    templated SMS / email / push via queue workers
│   ├── Webhooks         merchant webhooks, signatures, delivery log, replay
│   ├── Reports          dashboards, exports, branch activity
│   ├── Audit            audit log
│   ├── Billing          plans, invoices, eSewa / Khalti payments
│   ├── Platform         SuperAdmin, tenant lifecycle, platform settings
│   ├── International    cross-border shipments, customs, carriers
│   └── Events           outbox and event dispatch
├── Persistence/         AppDbContext, migrations, global tenant query filters
├── SharedKernel/        cross-cutting primitives (current tenant, permissions, paging)
└── web/                 Next.js app: staff console, rider PWA, merchant portal, public tracking
tests/                   one xUnit project per module, plus Api.Tests (end-to-end over HTTP)
docs/                    product, architecture, domain, RBAC, lifecycle and API specs; roadmaps
```

## Getting started

### Prerequisites

- .NET 10 SDK
- Node.js 20+ and npm
- Docker Desktop

### 1. Start the infrastructure

```bash
docker compose up -d postgres redis rabbitmq mailhog
```

| Service | Host port |
|---|---|
| PostgreSQL | `5434` |
| Redis | `6380` |
| RabbitMQ (AMQP / management UI) | `5672` / `15672` |
| MailHog (SMTP / web UI) | `1025` / `8025` |

### 2. Run the API

```bash
dotnet run --project src/Api
```

The API listens on `http://localhost:5013` and applies database migrations on startup.
`src/Api/appsettings.Development.json` holds **development-only** values: the local database
password, a JWT signing key and the seeded SuperAdmin (`superadmin@courier.local`). Replace all of
them with real secrets in any other environment.

### 3. Run the web app

```bash
cd src/web
npm ci
echo API_BASE_URL=http://localhost:5013 > .env.local
npm run dev
```

Open `http://localhost:3000`. Register an organisation to get an owner account, then add branches,
zones, a rate card, merchants and riders.

### Or run everything in Docker

```bash
docker compose up --build
```

The API is then on `http://localhost:5001` and the web app on `http://localhost:3000`.

## Tests

```bash
# backend — module tests (Postgres-backed tests need the compose Postgres on port 5434)
dotnet test tests/Shipments.Tests/Shipments.Tests.csproj

# backend — everything (Api.Tests also needs Redis, RabbitMQ and MailHog running)
dotnet test

# frontend
cd src/web
npm test            # Vitest
npx tsc --noEmit    # type check
npm run lint
npm run build
```

## Documentation

| Document | What it covers |
|---|---|
| [`docs/PRODUCT-SPEC.md`](docs/PRODUCT-SPEC.md) | Product scope and users |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Modular monolith, layering, cross-module rules |
| [`docs/DOMAIN-MODEL.md`](docs/DOMAIN-MODEL.md) | Entities and aggregates |
| [`docs/RBAC.md`](docs/RBAC.md) | Roles, permissions, branch scope |
| [`docs/SHIPMENT-LIFECYCLE.md`](docs/SHIPMENT-LIFECYCLE.md) | Shipment states and valid transitions |
| [`docs/COD-LIFECYCLE.md`](docs/COD-LIFECYCLE.md) | COD money trail and reconciliation |
| [`docs/BRANCH-OPERATIONS.md`](docs/BRANCH-OPERATIONS.md) | Scanning, sorting, manifests, custody |
| [`docs/API-SPEC.md`](docs/API-SPEC.md) | Merchant API, webhooks, tracking, staff endpoints |
| [`docs/INTERNATIONAL.md`](docs/INTERNATIONAL.md) | Cross-border shipments |
| [`docs/ROADMAP.md`](docs/ROADMAP.md), [`docs/FRONTEND-ROADMAP.md`](docs/FRONTEND-ROADMAP.md), [`docs/PLATFORM-ROADMAP.md`](docs/PLATFORM-ROADMAP.md) | Delivery phases, status and follow-ups |
| [`DESIGN.md`](DESIGN.md), [`PRODUCT.md`](PRODUCT.md) | UI design system and product voice |

## Status

Backend phases 0–13, frontend phases F0–F14, the platform phase, international I0–I4 and the F15
console redesign through R4 (rider PWA) are complete. Next up: R5 (merchant portal and public
tracking redesign) and R6 (platform and admin screens). Route optimisation, ETA and smart dispatch
(backend phase 14) are deliberately deferred. See the roadmaps for details and open follow-ups.
