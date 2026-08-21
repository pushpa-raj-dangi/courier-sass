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

*Status (2026-08-20): Complete.* A new `Pricing` module implements `ServiceType, WeightSlab,
RateCard, ZoneRate, AdditionalCharge, MerchantRateCard` per `DOMAIN-MODEL.md` — with two
deliberate simplifications from the original design: no `RateCardRule` entity (the engine queries
`ZoneRate`/`AdditionalCharge` directly by `RateCardId`, since neither needed an intermediate rule
layer), and no separate `Pricing.Zone` entity (`ZoneRate` FKs directly into `Geography.DeliveryZone`,
which already had the identical shape). `Branch` gained a new ops-assigned `DeliveryZoneId`
(`PUT /api/v1/branches/{id}/delivery-zone`) to resolve a shipment's origin/destination to a rating
zone. Versioning is enforced by construction, not convention: `POST /api/v1/rate-cards` supersedes
the org's current default (`Version + 1`, previous row flips to `IsActive = false`), and no
`PUT`/`PATCH`/`DELETE` exists anywhere on `RateCard`/`ZoneRate`/`AdditionalCharge` — proven by
`tests/Api.Tests/PricingEndpointsTests.cs`'s `RateCardVersioning_SupersededCardRowsUnchanged_NewQuoteUsesNewNumbers`
(old rows byte-for-byte unchanged after supersession, new quote uses new numbers) and
`AddZoneRate_OnSupersededRateCard_Returns400`. `POST /api/v1/pricing/quote` resolves a rate card
(merchant override via `MerchantRateCard` if present, else the org default), weight slab, and zone
pair, then returns a fully itemized result (base rate, each applied charge, discount, total) —
`MerchantOverride_UsedInsteadOfOrgDefault_DiscountApplied` proves the override path.
`RateCard`/`MerchantRateCard` are tenant-isolated (`RateCards_OrgBCannotSeeOrgAsRateCards`). Every
cross-module read (`Branch` zone data, `Merchant` existence, `DeliveryZone` existence) goes through
a small Application-layer interface (`IBranchLookupService`, `IMerchantLookupService`,
`IGeographyLookupService.DeliveryZoneExistsAsync`) rather than a direct cross-module `DbSet` query.
Deferred to later phases: quote persistence (Phase 6 will snapshot the resolved charge fields onto
`Shipment`), a "clone previous version" convenience endpoint, org-configurable `WeightSlab`, and
`AsOf`/historical-timestamp quote resolution.

## Phase 6 — Shipments, AWB, Shipment Items, Lifecycle, Tracking Events

Core `Shipment` aggregate, `ShipmentEvent`, the state machine and transition table
from `SHIPMENT-LIFECYCLE.md` up through booking/pickup/in-transit/delivered (NDR/RTO
come in Phase 9). Price snapshot at booking from Phase 5.
*Exit:* full happy-path shipment lifecycle (`CREATED` → `DELIVERED`) works with
correct append-only events and locked pricing.

*Status (2026-08-20): Complete.* A new `Shipments` module implements `Shipment, ShipmentItem,
ShipmentEvent` per `DOMAIN-MODEL.md`. Since `Manifest`/`Rider`/`DeliveryAttempt`/`DeliveryProof`
don't exist until Phase 7/8, the full `Created → Delivered` path is driven by direct
staff-triggered transition endpoints (`pickup-assign, pickup-scan, receive-origin, sort, dispatch,
receive-dest, out-for-delivery, deliver`) gated by the exact permissions (`shipment.assign/scan/
sort/transfer`, `delivery.assign/complete`) the real Phase 7/8 operations will eventually use —
`deliver` takes plain recipient-name/notes fields, expected to be superseded by Phase 8's real POD.
Every transition writes exactly one append-only `ShipmentEvent`, proven by
`tests/Api.Tests/ShipmentsEndpointsTests.cs`'s `FullHappyPath_CreatedToDelivered_Succeeds` (9
events, correct custody at every step) and `tests/Shipments.Tests/ShipmentServiceTransitionTests.cs`.
Pricing is locked at booking: `POST /api/v1/shipments` calls `PricingService.QuoteAsync` (Phase 5)
directly and snapshots `DeliveryCharge/InsuranceCharge/OtherCharges/TotalCharge` onto the
`Shipment` row — `PricingSnapshot_RateCardSupersededAfterBooking_ShipmentTotalChargeUnchanged`
proves a later `RateCard` version change doesn't alter an already-booked shipment's price. AWB
format is `{OrgSlugPrefix}{yyyyMMdd}{6-digit sequence}`, generated with an insert-and-retry
collision handler mirroring Phase 5's `RateCard` versioning race fix. Both staff and merchant JWT
tracks can create/read/cancel shipments (merchant scoped to `MerchantId == self`, new
`shipment.create/read/cancel` constants added to `MerchantPermissionCatalog` reusing the staff
catalog's identical string keys) — `Merchant_CanCreateAndCancelOwnShipment_ButNotAnotherMerchants`
proves merchant-to-merchant isolation within the same org, alongside `Shipments_
OrgBCannotReadOrgAsShipment` for tenant isolation. Deferred to later phases: real barcode/manifest
scan validation (Phase 7), `Rider`/POD (Phase 8), `NDR`/`RTO`/`DELIVERY_FAILED` (Phase 9),
`ShipmentEvent` GPS population (Phase 8), webhooks (Phase 11), public `/track/{awb}` (Phase 12).

## Phase 7 — Barcode Scanning, Sorting, Manifests, Branch Transfers

`Manifest, ManifestShipment, ShipmentTransfer`, the five scan validations from
`BRANCH-OPERATIONS.md` §1, manifest lifecycle `DRAFT → CLOSED` including
missing-shipment detection.
*Exit:* a manifest can move shipments between two branches with correct custody
updates and a provable missing-shipment exception path.

*Status (2026-08-21): Complete.* A new `Manifests` module implements `Manifest, ManifestShipment,
ShipmentTransfer` per `DOMAIN-MODEL.md`, with three deliberate simplifications from the original
sketch (all approved during brainstorming and reflected in `DOMAIN-MODEL.md`/`BRANCH-OPERATIONS.md`):
no separate "Dispatched" resting `ManifestStatus` (`POST /manifests/{id}/dispatch` moves `Sealed`
straight to `InTransit` in one action, since nothing distinguishes a dispatched-but-not-yet-in-transit
state in practice); a "sort scan" *is* "add to manifest" (`POST /manifests/{id}/scan` transitions the
shipment `AtOriginBranch → Sorted` and creates the `ManifestShipment` row in one call, retiring the
Phase 6 placeholder `POST /shipments/{id}/sort` endpoint entirely — a shipment can no longer be sorted
outside a manifest); and no separate `Transfer` scan type (`Sort`/`Dispatch`/`Receive` are the complete
branch-transfer scan surface this phase, `RiderPickup`/`RiderDelivery` are Phase 8, `RTO` is Phase 9).
The five `BRANCH-OPERATIONS.md` §1 checks (permission → branch access → shipment state → expected
operation → custody consistency) are enforced in `ManifestService.ScanAsync`/`ReceiveScanAsync`, proven
by `tests/Manifests.Tests/ManifestServiceScanningTests.cs` and `ManifestServiceTransitionsTests.cs`
rejecting each failure mode with a stable `ManifestScanRejectedException.ReasonCode`
(`SHIPMENT_NOT_FOUND`, `WRONG_STATE`, `WRONG_CUSTODY`, `MANIFEST_NOT_OPEN`, `MANIFEST_NOT_RECEIVING`,
`NOT_ON_MANIFEST`, `ALREADY_RECEIVED`) without writing a `ShipmentEvent`. Missing-shipment detection
needs no new column: a `ManifestShipment.ReceivedAt` still `null` after `Close` *is* the exception,
surfaced via `GET /manifests/{id}/missing-shipments` —
`tests/Api.Tests/ManifestsEndpointsTests.cs`'s `FullManifestLifecycle_DraftToClosed_
MovesShipmentThroughCustodyAndSurfacesMissingShipment` proves the full `Draft → Closed` lifecycle over
real HTTP end to end, including that a dispatched-but-never-received shipment is left correctly
`InTransit` (not silently reset) at close. `Manifests` never touches `Shipment`'s `DbSet` directly:
`Shipments.Application` exposes a new narrow `IShipmentTransitionService` (mirroring Phase 5's
`IBranchLookupService` pattern) that `ManifestService` calls only to *execute* transitions it has
already validated itself — the permission/branch-access enforcement that used to live inside
`ShipmentService.SortAsync`/`ReceiveAtDestinationAsync` moved up into `ManifestService`, and a new bulk
`DispatchManyAsync` replaced the single-shipment `DispatchAsync` (dispatch is now inherently a
manifest-wide action, batched into one `SaveChangesAsync` for true atomicity without introducing
explicit database transactions, which the in-memory EF provider this project's unit tests use doesn't
support). `Manifest`/`ManifestShipment`/`ShipmentTransfer` are tenant-isolated the same way
`ShipmentItem`/`ShipmentEvent` were in Phase 6 (isolation flows transitively through
`ManifestId → Manifest.OrganizationId`, with no child table carrying its own `OrganizationId`) —
proven by `Manifests_OrgBCannotReadOrgAsManifest`. One genuinely new permission, `manifest.read`, was
added (the original v1 catalog never included a manifest read permission); its own EF migration had to
be generated and applied immediately rather than deferred to the `AddManifests` migration, since EF's
`PendingModelChangesWarning` fails any integration test that runs `Database.Migrate()` as soon as the
seeded permission catalog changes. Deferred to later phases: real barcode/QR hardware integration and
bulk/continuous scan UX (CLAUDE.md §13 lists these as supported inputs; this phase implements the
validation and state-machine plumbing every scan mode will share, not a scanning UI), the missing-shipment
investigation workflow beyond the queryable exception list (`BRANCH-OPERATIONS.md` §2 explicitly flags
this as out of scope for this phase), and `RiderPickup`/`RiderDelivery`/`RTO` scan types (Phase 8/9).

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
