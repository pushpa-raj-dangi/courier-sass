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

*Status (2026-08-21): Complete.* A new `Riders` module implements `Rider, Vehicle, DeliveryRun,
DeliveryTask, DeliveryAttempt, DeliveryProof` per `DOMAIN-MODEL.md` — with `RiderAssignment` dropped
from the original sketch (`Rider.BaseBranchId` is the sole branch scope; approved during
brainstorming, `CLARIFICATIONS.md` #8 records the two other Phase 8 design ambiguities it resolved).
A rider is a real `Identity.User` holding the `PickupRider`/`DeliveryRider` role — both roles are now
seeded per-organization at registration alongside `OrganizationOwner`, and a new narrow
`IUserProvisioningService` in `Identity.Application` closes the gap that nothing before this phase
could create a second staff user at all. `RiderPickup`/`RiderDelivery` are the real scan
operations `BRANCH-OPERATIONS.md` §4 earmarked for this module, implemented via five new methods on
`Shipments`' `IShipmentTransitionService` (`AssignPickupToRiderAsync`, `RiderPickupScanAsync`,
`AssignDeliveryToRiderAsync`, `CompleteDeliveryAsync`, `FailDeliveryAsync`) — `Riders` performs the
full `BRANCH-OPERATIONS.md` §1 five-check validation itself before calling in, mirroring the
`Manifests`/`Shipments` division of responsibility Phase 7 established. This retires the Phase 6
placeholder `pickup-assign`/`pickup-scan`/`out-for-delivery`/`deliver` endpoints entirely — a
shipment can no longer move through pickup or delivery outside a real rider. `DeliveryRun` is
auto-created/reused (found-or-created per rider+branch+day the first time a task is assigned, no
explicit "start my run" endpoint) and `DeliveryTask.Sequence` increments within it, proven by
`tests/Riders.Tests/RiderServiceTasksTests.cs`'s
`AssignTaskAsync_TwoPickupTasksSameRiderSameDay_ReuseSameRunWithIncrementingSequence`. A failed
delivery attempt transitions the shipment to the newly-added `DeliveryFailed` status and
deliberately leaves custody (`CurrentCustodianRiderId`) on the rider rather than clearing it — the
parcel is still physically with them until Phase 9's NDR/reattempt/RTO flow moves it again; this
phase stops exactly at `DELIVERY_FAILED` and does not build `NDR_CREATED` or the reattempt loop.
`DeliveryProof.OtpVerified` is a client-reported boolean this phase (no server-side OTP generate/
verify flow — that needs Phase 11's real SMS delivery to be meaningful); signature/photo uploads
reuse the exact `IObjectStorage`/raw-body-upload pattern Phase 4 built for merchant KYC documents.
The full success path (pickup scan through POD capture to `DELIVERED`) and the full failure path
(assignment through a failed attempt to `DELIVERY_FAILED`, proving custody stays with the rider) are
each proven end-to-end over real HTTP in `tests/Api.Tests/RidersEndpointsTests.cs`, alongside tenant
isolation and a permission-scope test proving a `PickupRider` cannot call a `DeliveryRider`-only
endpoint, and a concurrency test proving the `xmin` optimistic-concurrency token still blocks a
double-delivery race through the new `delivery-attempts/complete` endpoint.
`Rider`/`Vehicle`/`DeliveryAttempt`/`DeliveryProof` carry their own `OrganizationId`;
`DeliveryRun`/`DeliveryTask` do not — isolation flows transitively through
`RiderId -> Rider.OrganizationId`, the same pattern `ManifestShipment`/`ShipmentItem` already
established. Deferred to later phases: `NDR`/`RTO` (Phase 9), real COD collection linkage (Phase
10), real OTP delivery (Phase 11), and a general staff user-invite feature beyond the two rider
roles this phase needed.

## Phase 9 — NDR, Reattempt, RTO

`NDR` entity/workflow (CLAUDE.md §20), the reattempt loop back to
`OUT_FOR_DELIVERY`, and the RTO path corrected in `SHIPMENT-LIFECYCLE.md` §2
(RTO only reachable via NDR resolution, including system `AutoRTO`).
*Exit:* both the reattempt loop and the full RTO path (`RTO_INITIATED` →
`RTO_DELIVERED`) are exercised by tests, with the direct
`DELIVERY_FAILED → RTO_INITIATED` shortcut proven absent.

*Status (2026-08-21): Complete.* A new `Ndr` module implements `NDR`/`ReturnShipment` per
`DOMAIN-MODEL.md`, with `CLARIFICATIONS.md` #9 recording three deliberate simplifications
(`ReturnShipment` has no stored status/branch/received-at columns — it reads them live from the
underlying `Shipment`; `NdrReason` is a fresh copy of `Riders.Domain.DeliveryAttemptReason`, not a
shared reference; `AutoRto` is a selectable resolution outcome only, no scheduled trigger exists).
`DELIVERY_FAILED -> NDR_CREATED` happens automatically: `Riders.Application.RiderService.
RecordFailedAttemptAsync` (Phase 8) calls a new narrow `INdrCreationService` in the same request,
immediately after `FailDeliveryAsync` — proven by `tests/Riders.Tests/RiderServiceAttemptsTests.cs`.
NDR's own workflow (`POST /ndrs/{id}/update` walking `Open -> Contacting -> CustomerConfirmed ->
{ReattemptScheduled|RtoRequested}`, `POST /ndrs/{id}/resolve` performing the real side effect) and
the reattempt loop (`NDR_CREATED -> OUT_FOR_DELIVERY` directly, custody untouched, no new
`DeliveryTask` created — a deliberate scope simplification) are exercised end-to-end in
`tests/Api.Tests/NdrEndpointsTests.cs`. `CLARIFICATIONS.md` #4's single-gateway-into-RTO guarantee
(no direct `DELIVERY_FAILED -> RTO_INITIATED` transition anywhere) is proven by
`tests/Shipments.Tests/ShipmentServiceNdrRtoTransitionTests.cs`'s
`InitiateRtoAsync_FromDeliveryFailed_ThrowsInvalidTransition`. RTO's physical return leg reuses
`Manifest` entirely — a new `Manifest.Purpose` field (`Forward|Return`, `CLARIFICATIONS.md` #9)
lets the existing scan/dispatch/receive-scan endpoints drive the RTO status pair
(`RtoInitiated -> RtoInTransit -> RtoAtOrigin`) through a new `/rto-scan` endpoint and
`Purpose`-branched dispatch/receive, proven by `tests/Manifests.Tests/ManifestServicePurposeTests.cs`
and the full `tests/Api.Tests/NdrEndpointsTests.cs`'s
`FullRtoPath_FailedAttemptToRtoDelivered_MovesThroughEveryStatusViaManifests` test (book -> pickup
-> sort/dispatch/receive -> out for delivery -> failed attempt -> NDR workflow -> `Rto` resolution
-> return manifest -> `RTO_DELIVERED`, terminal, over real HTTP end to end). `NDR`/`ReturnShipment`
are both tenant-isolated with their own `OrganizationId` (not transitive), proven by
`Ndrs_OrgBCannotReadOrgAsNdr`. Deferred to later phases: `CODCollection`/rider cash settlement
(Phase 10), a scheduled `AutoRTO` trigger (needs Phase 11's background-job infrastructure and a
product-defined attempt threshold), and a real `DeliveryTask` row for reattempts (the existing
`DeliveryAttempt` audit trail via `AttemptNumber` was judged sufficient for this phase's scope).

## Phase 10 — COD, Rider Settlements, Branch Reconciliation, Merchant Settlements

Full `COD-LIFECYCLE.md` chain: `CODCollection, RiderCashSettlement,
BranchCashSettlement, MerchantSettlement, MerchantSettlementItem,
FinancialTransaction`, plus the `Adjustment` record type.
*Exit:* the worked example in `COD-LIFECYCLE.md` §2 is reproducible end-to-end from a
delivered COD shipment through to a settled merchant payout, with every step
traceable and every adjustment audited.

*Status (2026-08-25): Complete.* A new `Cod` module implements every entity from
`DOMAIN-MODEL.md`'s "COD & Finance" section, with `CLARIFICATIONS.md` #11 recording four
deliberate resolutions (`CODCollection.Status` carries the full seven-state lifecycle chain;
`CODCollection` is created eagerly at booking, not lazily at delivery; `RiderCashSettlement.
BranchCashSettlementId` replaces `BranchCashSettlement.RiderCashSettlementIds[]`; `Adjustment`
always requires a separate approval step). `CODCollection` is created by a new narrow
`ICodCollectionCreationService`, called from `Shipments.Application.ShipmentService.
CreateShipmentAsync` for any shipment with `CodAmount > 0` — the same cross-module-write pattern
`INdrCreationService` established in Phase 9. `Riders.Application.RiderService.
CompleteDeliveryAsync` gained a conditional `cod.collect` permission check (via
`SharedKernel.IPermissionChecker`, its first Application-layer consumer) and calls a second narrow
`ICodCollectionService.MarkCollectedAsync` to record the actual collected amount against the
`DeliveryProof` just created. `Cod` has no project reference to `Shipments` at all — every
financial fact it needs (`MerchantId`, `BranchId`, `ShipmentTotalCharge`) is snapshotted onto
`CODCollection` at creation, so the full rider-deposit -> branch-reconciliation ->
finance-verification -> merchant-settlement -> payout chain never reads back into another module.
Every multi-row step in that chain (`CreateRiderSettlementAsync` through `SettleMerchantSettlementAsync`,
plus `ApproveAdjustmentAsync`) runs inside an explicit `Database.BeginTransactionAsync()` — the
first use of an explicit transaction anywhere in this codebase, deliberately established here for
financial correctness. `FinancialTransactionConfiguration`'s unique `(ReferenceEntityType,
ReferenceEntityId)` index makes "verify the same `BranchCashSettlement` twice" impossible under
concurrency (not just checked-then-written), proven by a real `Task.WhenAll` race test against
Postgres — mirroring Phase 5's `RateCard` versioning fix. The full worked example from
`COD-LIFECYCLE.md` §2 (COD 2500, courier fee from the shipment's own locked-in `TotalCharge`,
net payable = the difference) is proven end-to-end over real HTTP in
`tests/Api.Tests/CodEndpointsTests.cs`'s `FullCodChain_BookingToMerchantPayout_
MatchesWorkedExampleAndMovesThroughEveryStatus` test. Deferred to later phases: scheduled/recurring
merchant settlement generation (needs Phase 11's Hangfire infrastructure — this phase is on-demand
only), and adjustment support for entity types other than `MerchantSettlementItem` (no other
concrete business need exists yet).

## Phase 11 — Notifications, SMS, Email, Push, Webhooks

Event-driven notification pipeline (CLAUDE.md §27) off the Phase-0/1 outbox, plus
`Webhooks` module per `API-SPEC.md` §5 (signature, retry, delivery log, replay).
*Exit:* at least one real channel (e.g. SMS or email) fires off a domain event with
a configurable template; a webhook subscriber receives a correctly signed event and
a forced failure demonstrably retries then logs.

*Status (2026-08-26): Complete.* Three new modules: `Events` (owns `OutboxMessage` and the
narrow `IOutboxWriter` interface every producing module calls, mirroring `Audit`'s `IAuditLogger`
shape), `Notifications` (real SMTP for Email via MailKit, log-only stubs for SMS/WhatsApp/Push),
`Webhooks` (merchant-managed subscriptions, signed delivery, exponential-backoff retry, replay).
Four proving-set domain events wired: `shipment.created`, `shipment.delivered`,
`shipment.ndr_created`, `cod.collected` — each an existing application-service call site gaining
one `IOutboxWriter.WriteAsync` call in the same `SaveChangesAsync` as its business change. This
phase is the first to actually use RabbitMQ and Hangfire (both provisioned since Phase 0 but
unused before now, per `CLARIFICATIONS.md` #4): a Hangfire recurring job publishes unpublished
outbox rows to a shared RabbitMQ topic exchange, and `Notifications`/`Webhooks` each run their
own independent `BackgroundService` consumer bound to their own queue on that exchange — a real
internal broker hop, not a simplified in-process shortcut, matching `ARCHITECTURE.md`'s
already-committed design. `CLARIFICATIONS.md` #12 records three resolved ambiguities
(`WebhookSubscription.Secret` encrypted not hashed; 5-attempt 1m/2m/4m/8m/16m retry backoff; no
`NotificationTemplate` rows seeded, compiled-in defaults used instead) plus one real bug class
found only by the end-to-end test: three background-context queries against tenant-filtered
tables silently returned nothing (not an exception) because `ICurrentTenantProvider.OrganizationId`
is null outside an HTTP request — fixed with `IgnoreQueryFilters()` plus an explicit trusted
predicate, or (for the one general-purpose lookup interface) a required `organizationId`
parameter, never a blanket bypass. `WebhookDelivery.SourceEventId` plus a unique
`(WebhookSubscriptionId, SourceEventId)` index makes duplicate-delivery creation from a
redelivered message DB-impossible, the same TOCTOU-guard lesson `FinancialTransaction`'s
uniqueness constraint established in Phase 10. The full exit criteria — a real email landing in a
dev-only mail-catcher (`docker-compose.yml`'s new `mailhog` service) and a forced-failure webhook
target retrying then appearing failed in the delivery log, then succeeding on replay — is proven
end-to-end over real HTTP, real Postgres, and real RabbitMQ in
`tests/Api.Tests/NotificationsAndWebhooksEndToEndTests.cs`. Deferred to later phases: a
`NotificationTemplate` management API, the six remaining `CLAUDE.md` §26 webhook event types
(subscribable but never published this phase), and the Hangfire recurring jobs `ARCHITECTURE.md`
separately brainstormed that this pipeline itself doesn't need (scheduled merchant-settlement
generation, NDR SLA reminders, expired `UserBranch` cleanup) — all now unblocked by this phase's
Hangfire wiring, none implemented by it.

## Phase 12 — Merchant Portal, Public Tracking, Merchant API

Frontend merchant portal, `/track/{awb}` (`API-SPEC.md` §4), full Merchant API
catalog (`API-SPEC.md` §3) with API keys (`CLARIFICATIONS.md` #7), rate limiting,
idempotency.
*Exit:* an external client can create a shipment, poll its status, and see a
settlement purely through the documented Merchant API; public tracking correctly
withholds the excluded fields.

*Status (2026-08-27): Complete — backend slice only.* API keys (`ApiKey` entity +
`ApiKey`/`Bearer` dual-scheme auth), Redis-backed rate limiting and idempotency (first real
Redis use in this codebase), a real `PickupRequest` feature, merchant-scoped COD settlement
reads, and public `/track/{awb}` are all implemented and proven end-to-end
(`tests/Api.Tests/MerchantApiExitCriteriaEndToEndTests.cs`). `CLARIFICATIONS.md` #13 records
seven resolved design ambiguities/fixes, including a self-escalation carve-out that keeps API-key
management itself JWT-only, and a fail-closed fix for a cross-tenant AWB-collision risk on the
new anonymous tracking endpoint. The frontend merchant portal (`src/web`, an untouched
`create-next-app` skeleton *at the time this phase closed*) was deliberately deferred to a
follow-up Phase 12b — this phase's scope decision, made explicit up front in the design spec's
scope note. It has since been built via the separate Frontend Roadmap track
(`docs/FRONTEND-ROADMAP.md`): the merchant portal itself shipped as Frontend Phase F10, and this
endpoint's own public-facing UI (`/track`, `/track/{awb}`) shipped as Frontend Phase F11 — that
phase also closed two small gaps in this endpoint found by inspection (`PublicTrackingStatus`'s
missing `JsonStringEnumConverter`, and the IP rate limiter's lack of reverse-proxy awareness once
called through a real frontend BFF), recorded in `CLARIFICATIONS.md` #20.

## Phase 13 — Reports, Analytics, Advanced Dashboards

Org dashboard (CLAUDE.md §29) and branch dashboard (`BRANCH-OPERATIONS.md` §3) fully
built out, `report.export`.
*Exit:* dashboards reflect real data from all prior phases with correct branch/role
scoping, no hardcoded/demo data paths left in.

*Status: Complete — backend slice only.* New `Reports` module (no owned entities):
`GET /api/v1/reports/dashboard/organization`, `GET /api/v1/reports/dashboard/branch/{branchId}`,
`GET /api/v1/reports/export/shipments`, `GET /api/v1/reports/export/cod-summary`, gated by
`report.read`/`report.export`. Five new cross-module lookup interfaces
(`IShipmentReportingLookupService`, `IManifestReportingLookupService`,
`IRiderReportingLookupService`, `INdrReportingLookupService`, `ICodReportingLookupService`), each
implemented in its owning module's `Application` layer, following the `IBranchLookupService`
pattern Phase 5 established. All figures computed live (no caching/materialization). The staff
dashboard frontend — like the Phase 12b merchant portal — remains deferred; `src/web` is untouched.
See `docs/superpowers/specs/2026-08-28-phase-13-reports-analytics-dashboards-design.md` for the
full design, including the resolved formulas for COD Expected/Collected/Pending/Settled/Merchant
Payable and NDR Rate/RTO Rate (neither CLAUDE.md §29 nor COD-LIFECYCLE.md/NDR's own entities define
these precisely).

## Phase 14 — Route Optimization, GPS, ETA, Smart Dispatch, Partner Integrations

Explicitly out of v1 scope per `PRODUCT-SPEC.md` §5 — only entered once Phases 0–13
are in production and there's a real operational need driving it, not speculatively.

*Status (Phase 14a — Live Rider GPS Tracking): Shipped 2026-09-17.* A narrow, real
operational need (dispatchers/branch managers needing to see where riders currently are)
was carved out of this phase ahead of the rest: foreground-only rider position reporting
from the rider PWA (`POST /api/v1/riders/me/location`), a branch-scoped Live Map
(`GET /api/v1/riders/branches/{branchId}/locations`) showing each active rider's latest
position, and a per-run breadcrumb trail (`GET /api/v1/riders/runs/{runId}/breadcrumbs`)
for audit/replay of a completed run's path — plus a pre-existing RBAC gap closed as part
of this work, granting `rider.read`/`rider.location.read` to `BranchManager`, `Dispatcher`,
and `OperationsManager`. Route optimization, ETA, smart dispatch, partner courier
integrations, and a customer-facing "rider is nearby" tracking indicator remain separate,
un-started future sub-projects of this phase.

## Cross-Phase Rule

Any ambiguity discovered while implementing a later phase gets resolved the same way
this batch was: added to `CLARIFICATIONS.md` with a stated resolution *before*
writing the code that depends on the resolution, not decided ad hoc inline.

## International Support

Tracked separately in `docs/INTERNATIONAL.md`, with the design in
`docs/superpowers/specs/2026-09-21-international-support-design.md` and the I0 plan in
`docs/superpowers/plans/2026-09-21-international-i0-money-fx-locale.md`. I0 (money, FX rates,
tenant locale) is implemented and verified against Postgres on 2026-09-21 (full Api suite
294/294; 8 distinct tests are flaky only under full-suite parallel load, all passing in
isolation). I1a (frontend i18n
foundation: en/ne catalogs, cookie-based locale, Bikram Sambat formatters, translated staff
shell and login) is implemented, with Nepali strings still needing native review. I1b
(notification-template locale: `NotificationTemplate.Locale`, Nepali default bodies, a
tenant-default-locale fallback resolver, and the API/admin-panel surface) is implemented, with
its Nepali default bodies also needing native review. I2 (Country/CountryZone reference data,
serviced-country toggle, `InternationalAddress` value object) is implemented; I3–I8 are planned.
