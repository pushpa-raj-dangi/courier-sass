# Roadmap

Phase order is fixed by CLAUDE.md §36 and must not be reordered or parallelized
across unrelated phases — each phase's entry criteria assume the previous phase is
actually done (per the Definition of Done, CLAUDE.md §37), not just started.

**Phase −1 (this work): Architecture & Domain Analysis.** Produces the `docs/` suite
(`PRODUCT-SPEC.md`, `ARCHITECTURE.md`, `DOMAIN-MODEL.md`, `RBAC.md`,
`SHIPMENT-LIFECYCLE.md`, `COD-LIFECYCLE.md`, `BRANCH-OPERATIONS.md`, `API-SPEC.md`,
`ROADMAP.md` — this file) plus `CLARIFICATIONS.md` resolving spec ambiguities. No
application code is written in this phase. Exit criteria: all nine docs exist and are
internally consistent with `CLARIFICATIONS.md`.

For every phase below, "Definition of Done" always means the full CLAUDE.md §37
checklist (domain model, migration, application logic, authorization, tenant
isolation, branch scope where relevant, validation, audit where relevant, API where
relevant, frontend where relevant, loading/error states, tests, docs updated, build
green, tests green, no unrelated regressions) — not repeated per phase below, only
phase-specific entry/exit notes are.

## Phase 0 — Repository & Infrastructure

Repository scaffold, Docker Compose (`api`, `web`, `postgres`, `redis`, `rabbitmq`),
API host with logging (Serilog) and global error handling wired per
`ARCHITECTURE.md`, Web host skeleton. No business modules yet.
*Entry:* Phase −1 docs complete. *Exit:* `docker compose up` yields a running API +
Web that talk to Postgres/Redis/RabbitMQ; empty but real CI (build + test) green.

## Phase 1 — Authentication, Organization, Users, Roles, Permissions, Tenant Isolation, Audit

Implements `Identity` module per `DOMAIN-MODEL.md`/`RBAC.md`, including `UserRole`
(`CLARIFICATIONS.md` #1) and the global tenant query filter
(`ARCHITECTURE.md` §3). `AuditLog` (module-agnostic, written to by everything after).
*Entry:* Phase 0 done. *Exit:* an org can be created, a user can log in, permission
checks work end-to-end on at least one protected endpoint, tenant isolation is
covered by an integration test that proves cross-tenant access is rejected.

*Status (2026-08-18): Complete.* Organization self-registration, login, refresh token
rotation, permission-checked `GET /api/v1/users`, and an automated tenant-isolation test
are implemented and passing. `SuperAdmin`/platform-level auth remains deferred (see
`CLARIFICATIONS.md` #5) — no phase depends on it yet.

## Phase 2 — Branches, UserBranch, Branch Authorization

`Branch` (with `ParentBranchId` hierarchy), `UserBranch` (with `ExpiresAt`,
`CLARIFICATIONS.md` #6), branch-scope authorization layered on top of Phase 1's
permission checks.
*Exit:* branch-scoped permission checks proven by tests covering single-branch,
multi-branch, all-branches, hierarchy, and expired-temporary-grant cases.

*Status (2026-08-18): Complete.* `Branch` (with `ParentBranchId` hierarchy) and `UserBranch`
(with `ExpiresAt`) are implemented — `Branch` in a new `Branches` module, `UserBranch` in
`Identity` per `ARCHITECTURE.md` §2. Branch-scope authorization
(`IBranchAuthorizationChecker`, `RequireBranchScopedPermission`) is layered on top of Phase 1's
permission checks and exercised end-to-end via `POST/GET /api/v1/branches`. Automated tests
cover single-branch, multi-branch, all-branches, parent-branch-hierarchy, and
expired-temporary-grant authorization scenarios (`tests/Identity.Tests/
BranchAuthorizationCheckerTests.cs`). Geography fields on `Branch` remain a free-text
placeholder (`AddressLine`) pending Phase 3 per this phase's entry note.

## Phase 3 — Nepal Geography, Zones, Coverage

`Province, District, LocalLevel, Ward, Area, DeliveryZone` (`CLARIFICATIONS.md` #2).
Branch geography fields become FKs into this module, replacing any earlier free-text
placeholder.
*Exit:* branch coverage/zone lookups work off normalized data, no free-text
geography remains on `Branch`.

*Status (2026-08-18): Complete.* A new `Geography` module implements `Province, District,
LocalLevel(Type), Ward, Area, DeliveryZone` per `DOMAIN-MODEL.md`. All 7 real provinces and all
77 real districts are seeded; `LocalLevel/Ward/Area` are seeded for the 5 districts CLAUDE.md §1's
example org operates in (Kathmandu, Lalitpur, Bhaktapur, Kaski, Rupandehi) — full national
local-level coverage is an explicit follow-up data-loading task, not part of this phase's
application-logic scope. `Branch` now carries `ProvinceId/DistrictId/LocalLevelId/WardNumber` FKs
in place of the Phase 2 free-text placeholder; `BranchService` validates the full chain
server-side via `IGeographyLookupService` before persisting (`tests/Api.Tests/
BranchesEndpointsTests.cs`'s `CreateBranch_MismatchedGeographyChain_Returns400`).
`GET /api/v1/geography/{provinces,districts,local-levels,wards,areas}` (permission
`geography.read`) and `POST/GET/PUT /api/v1/delivery-zones` (`zone.read/create/update`, tenant-
isolated like `Branch`) are exercised end-to-end in `tests/Api.Tests/GeographyEndpointsTests.cs`
and `tests/Api.Tests/DeliveryZonesEndpointsTests.cs`.

## Phase 4 — Merchants, Merchant Users, KYC, Merchant Settings

`Merchant`, `MerchantUser`, KYC document handling (via the storage abstraction),
merchant-scoped authorization (`RBAC.md` §5).
*Exit:* a merchant account can be onboarded and its staff can log in with
correctly scoped permissions.

*Status (2026-08-19): Complete.* A new `Merchants` module implements `Merchant, MerchantUser,
MerchantAddress, MerchantKycDocument, MerchantRefreshToken` per `DOMAIN-MODEL.md`. Merchant
authorization is a separate track from staff RBAC (`RBAC.md` §5): `MerchantUser.Role` is a
2-value enum (`Owner`/`Staff`) checked against a small `MerchantPermissionCatalog`, with its own
`IMerchantAuthorizationChecker`/`RequireMerchantPermission` filter and its own
`IMerchantJwtTokenService` issuing tokens that share the staff signing key/issuer/audience but
carry a distinct `merchant_id` claim. Org staff onboard a merchant in one call
(`POST /api/v1/merchants`, `merchant.create`) — creating the `Merchant`, its owner `MerchantUser`,
and a default pickup address, with the address's geography chain validated server-side via
`IGeographyLookupService` exactly like Phase 3's `Branch` creation
(`tests/Api.Tests/MerchantsEndpointsTests.cs`'s `CreateMerchant_MismatchedGeographyChain_Returns400`).
The owner can log in (`POST /api/v1/merchant/auth/login`), invite `MerchantStaff`
(`POST /api/v1/merchant/staff`, owner-only — proven end-to-end in
`tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs`), and both roles can upload KYC documents
(`POST /api/v1/merchant/kyc-documents`, raw-body upload against a new `IObjectStorage`
local-filesystem implementation in `SharedKernel`) for org staff to review
(`POST /api/v1/merchants/{id}/kyc-documents/{docId}/review`, `merchant.kyc.review`). `Merchant`,
`MerchantUser`, `MerchantAddress`, and `MerchantKycDocument` are all tenant-isolated
(`tests/Api.Tests/MerchantsEndpointsTests.cs`'s `ListMerchants_OrgBCannotSeeOrgAsMerchant`,
`MerchantKycEndpointsTests.cs`'s `Review_FromDifferentOrganization_Returns404`). Deferred to later
phases per this phase's plan: `ApiKey`/Merchant API (Phase 12), a merchant-update endpoint,
`MerchantAddress` CRUD beyond the one address created at onboarding.

## Phase 5 — Rate Cards, Pricing Engine

`RateCard, RateCardRule, ServiceType, WeightSlab, Zone, ZoneRate, AdditionalCharge,
MerchantRateCard`, versioning (CLAUDE.md §16).
*Exit:* a price can be computed for a given origin/destination/weight/service/COD/
insurance/merchant combination, and is provably versioned (changing today's rate
does not alter a previously computed quote).

## Phase 6 — Shipments, AWB, Shipment Items, Lifecycle, Tracking Events

Core `Shipment` aggregate, `ShipmentEvent`, the state machine and transition table
from `SHIPMENT-LIFECYCLE.md` up through booking/pickup/in-transit/delivered (NDR/RTO
come in Phase 9). Price snapshot at booking from Phase 5.
*Exit:* full happy-path shipment lifecycle (`CREATED` → `DELIVERED`) works with
correct append-only events and locked pricing.

## Phase 7 — Barcode Scanning, Sorting, Manifests, Branch Transfers

`Manifest, ManifestShipment, ShipmentTransfer`, the five scan validations from
`BRANCH-OPERATIONS.md` §1, manifest lifecycle `DRAFT → CLOSED` including
missing-shipment detection.
*Exit:* a manifest can move shipments between two branches with correct custody
updates and a provable missing-shipment exception path.

## Phase 8 — Riders, Assignments, Delivery, Delivery Attempts, POD

`Rider, Vehicle, RiderAssignment, DeliveryRun, DeliveryTask, DeliveryAttempt,
DeliveryProof`.
*Exit:* a rider can be assigned a delivery, record a failed attempt with a reason
from CLAUDE.md §19's list, or complete delivery with OTP/signature/photo/GPS POD.

## Phase 9 — NDR, Reattempt, RTO

`NDR` entity/workflow (CLAUDE.md §20), the reattempt loop back to
`OUT_FOR_DELIVERY`, and the RTO path corrected in `SHIPMENT-LIFECYCLE.md` §2
(RTO only reachable via NDR resolution, including system `AutoRTO`).
*Exit:* both the reattempt loop and the full RTO path (`RTO_INITIATED` →
`RTO_DELIVERED`) are exercised by tests, with the direct
`DELIVERY_FAILED → RTO_INITIATED` shortcut proven absent.

## Phase 10 — COD, Rider Settlements, Branch Reconciliation, Merchant Settlements

Full `COD-LIFECYCLE.md` chain: `CODCollection, RiderCashSettlement,
BranchCashSettlement, MerchantSettlement, MerchantSettlementItem,
FinancialTransaction`, plus the `Adjustment` record type.
*Exit:* the worked example in `COD-LIFECYCLE.md` §2 is reproducible end-to-end from a
delivered COD shipment through to a settled merchant payout, with every step
traceable and every adjustment audited.

## Phase 11 — Notifications, SMS, Email, Push, Webhooks

Event-driven notification pipeline (CLAUDE.md §27) off the Phase-0/1 outbox, plus
`Webhooks` module per `API-SPEC.md` §5 (signature, retry, delivery log, replay).
*Exit:* at least one real channel (e.g. SMS or email) fires off a domain event with
a configurable template; a webhook subscriber receives a correctly signed event and
a forced failure demonstrably retries then logs.

## Phase 12 — Merchant Portal, Public Tracking, Merchant API

Frontend merchant portal, `/track/{awb}` (`API-SPEC.md` §4), full Merchant API
catalog (`API-SPEC.md` §3) with API keys (`CLARIFICATIONS.md` #7), rate limiting,
idempotency.
*Exit:* an external client can create a shipment, poll its status, and see a
settlement purely through the documented Merchant API; public tracking correctly
withholds the excluded fields.

## Phase 13 — Reports, Analytics, Advanced Dashboards

Org dashboard (CLAUDE.md §29) and branch dashboard (`BRANCH-OPERATIONS.md` §3) fully
built out, `report.export`.
*Exit:* dashboards reflect real data from all prior phases with correct branch/role
scoping, no hardcoded/demo data paths left in.

## Phase 14 — Route Optimization, GPS, ETA, Smart Dispatch, Partner Integrations

Explicitly out of v1 scope per `PRODUCT-SPEC.md` §5 — only entered once Phases 0–13
are in production and there's a real operational need driving it, not speculatively.

## Cross-Phase Rule

Any ambiguity discovered while implementing a later phase gets resolved the same way
this batch was: added to `CLARIFICATIONS.md` with a stated resolution *before*
writing the code that depends on the resolution, not decided ad hoc inline.
