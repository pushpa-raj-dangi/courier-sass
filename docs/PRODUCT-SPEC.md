# Product Specification — Nepal Domestic Courier SaaS

## 1. Vision

A multi-tenant SaaS platform that lets Nepal-based courier companies run their entire
domestic parcel operation — branches, hubs, riders, merchants, COD, and delivery
networks — on one system. The system's primary subject matter is the **physical
lifecycle of a parcel**: custody, movement, delivery, and financial settlement. It is
not an e-commerce order manager; order/shipment creation is only the entry point into
a much longer operational chain (see `SHIPMENT-LIFECYCLE.md`).

The platform must always be able to answer the questions in CLAUDE.md §39
(where is the parcel, who has it, which branch owns it, was COD collected, has the
merchant been settled, why did delivery fail, what happens next) — that traceability
requirement is treated as a hard constraint on every feature, not an aspiration.

## 2. Tenancy Model

* **Tenant = Organization.** One courier company = one Organization. An Organization
  owns branches, users, merchants, shipments, rate cards, and all operational data.
* **Platform operator** (Anthropic-style "us, the SaaS vendor") is a separate,
  non-tenant-scoped layer — the `SuperAdmin` role defined in `RBAC.md` — used for
  provisioning tenants and platform support, never for day-to-day courier operations.

## 3. Personas

| Persona | Belongs to | Primary needs |
|---|---|---|
| Organization Owner / Admin | Organization | Configure branches, roles, rate cards; view org-wide dashboards and settlements |
| Operations Manager | Organization | Cross-branch operational oversight, exception handling |
| Branch Manager | One or more Branches | Branch dashboard, staff assignment, manifest sign-off |
| Dispatcher / Sorting / Counter Staff | One or more Branches | Scanning, sorting, manifest creation, counter booking |
| Pickup / Delivery Rider | One or more Branches | Mobile-first: assigned tasks, navigation, OTP, COD collection, POD capture |
| Finance Manager / Staff | Organization (branch-scoped optional) | COD reconciliation, merchant settlements, adjustments |
| Customer Support | Organization | Shipment lookup, NDR follow-up, customer communication |
| Auditor | Organization | Read-only access to audit trail, financial history |
| Merchant / Merchant Staff | Merchant (child of Organization) | Book shipments, track parcels, view settlements, API access |
| Public recipient/sender | None (unauthenticated) | `/track/{awb}` public tracking |
| SuperAdmin | Platform (no Organization) | Tenant provisioning, platform support, billing plans |

## 4. Core Capabilities (v1 scope)

* Multi-tenant, multi-branch SaaS with permission-based RBAC and branch-scoped access
* Nepal-normalized geography driving branch coverage, zones, and pricing
* Versioned rate cards; price locked at booking time
* Full shipment lifecycle with append-only tracking events and physical custody chain
* Barcode/QR scanning for receive/sort/dispatch/transfer/rider pickup/rider delivery
* Manifest-based hub-to-hub and branch-to-branch transfers with received/missing
  reconciliation
* Rider mobile operations: assignment, navigation handoff, OTP, COD collection,
  signature/photo/GPS proof of delivery
* NDR (non-delivery report) workflow feeding into a controlled RTO lifecycle
* COD subsystem: collection → rider deposit → branch reconciliation → finance
  verification → merchant settlement, fully traceable, no silent adjustments
* Public AWB tracking with a restricted, customer-safe event view
* Merchant portal + versioned REST API + signed, retryable webhooks
* Event-driven notifications (SMS/email/WhatsApp/push) decoupled from business logic
* Audit log for all sensitive operations
* Organization and branch dashboards (operational + financial)

## 5. Explicitly Out of Scope for v1

(Deferred to Phase 14 or later per `ROADMAP.md` — do not build early, do not let
earlier phases block on these.)

* Route optimization / smart dispatch algorithms
* Live GPS rider tracking maps (POD still captures a GPS point per §18, but continuous
  live tracking is not v1)
* ETA prediction
* Partner/third-party courier interlining or integration
* International or cross-border shipping
* Multi-currency (NPR only for v1)
* Native mobile apps (rider app is a mobile-web/PWA experience against the same API
  in v1, not a separate native codebase)

## 6. Non-Functional Requirements

* **Tenant isolation is server-enforced, always** — see CLAUDE.md §4, §33 and
  `CLARIFICATIONS.md`. No exceptions outside the audited `SuperAdmin` path.
* **Financial correctness over convenience** — COD and pricing figures are never
  silently recalculated or overwritten; every change is an audited adjustment
  (CLAUDE.md §23).
* **Operational realism** — status transitions must correspond to a real scan,
  manifest, or delivery-attempt event, never an arbitrary admin edit (CLAUDE.md §39).
* **Auditability** — every sensitive action produces an audit record (CLAUDE.md §28).
* Modular monolith, not microservices, for v1 (CLAUDE.md §2).
