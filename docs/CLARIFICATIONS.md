# CLAUDE.md Clarifications & Resolved Ambiguities

This document resolves specific ambiguities, contradictions, and gaps found in the root
`CLAUDE.md` specification during initial architecture review (per §40, before any
implementation begins). Each entry references the section(s) in question, states the
ambiguity, and records the binding resolution. Where the resolution implies a schema
addition, it is noted so `DOMAIN-MODEL.md` and the Phase 1/2 migrations stay consistent
with this file.

Treat this file as authoritative wherever it conflicts with a literal reading of
CLAUDE.md — it exists specifically to remove the ambiguity CLAUDE.md left open.

---

## 1. Missing User↔Role assignment entity

**Where:** §4 (Tenant Architecture), §6 (RBAC)

**Ambiguity:** The core model lists `Organization, Branch, User, Role, Permission,
UserBranch, RolePermission`. `UserBranch` and `RolePermission` are explicit join
entities, but nothing maps a `User` to a `Role`.

**Resolution:**

* Add `UserRole { Id, UserId, RoleId, OrganizationId, AssignedAt, AssignedByUserId }`.
* A user may hold multiple roles simultaneously. Effective permissions = union of all
  permissions across all assigned roles.
* `Role` rows are scoped to an `OrganizationId` (nullable = platform-level role, see
  §5 below). At organization creation, the default roles in §6 are seeded as rows
  owned by that organization, not shared global rows — this allows an org to later
  clone/adjust a default role's permission set without affecting other tenants.
* Combining with §7's branch scoping: effective authorization for an action is
  `permission ∈ user's roles' permissions` AND `branch ∈ user's UserBranch grants
  (or AllBranches, see #6)`.

---

## 2. `Municipality` vs `LocalLevel` terminology mismatch

**Where:** §5 (Branch fields), §8 (Nepal Geography)

**Ambiguity:** §5 gives Branch a flat `Municipality` string field. §8 defines a
normalized hierarchy `Province → District → LocalLevel → Ward → Area → DeliveryZone`
and explicitly warns against storing routing-relevant geography as free text. Nepal's
actual administrative unit is "Local Level" (which has a `Type`: Metropolitan City /
Sub-Metropolitan City / Municipality / Rural Municipality) — "Municipality" is only
one subtype, not the general term.

**Resolution:**

* Branch does **not** store `Province`/`District`/`Municipality` as strings. It stores
  FKs into the normalized geography tables: `ProvinceId, DistrictId, LocalLevelId,
  WardNumber` (+ free-text `AddressLine` for the unstructured street-level remainder).
* `LocalLevel { Id, DistrictId, Name, Type }` where `Type` is an enum
  (`Metropolitan | SubMetropolitan | Municipality | RuralMunicipality`).
* Anywhere else in this spec that says "Municipality" (e.g. §5's Branch field list)
  should be read as "LocalLevelId FK", not a literal municipality-only string.

---

## 3. Two different "how to do a feature" workflows

**Where:** §34 (Development Rules for Claude Code), §38 (Claude Code Workflow)

**Ambiguity:** §34 gives a 10-item checklist ending in "Implement the smallest coherent
change." §38 gives a separate 10-step workflow (Analyze → Explain → List files → DB
changes → Auth/security → Implement → Test → Build → Review → Summarize). They overlap
but are not the same list, and CLAUDE.md doesn't say which governs.

**Resolution:** §38 is the step sequence actually followed per feature request. §34's
items are not a competing process — they are the checklist that must be satisfied
**inside §38 Step 1 (Analyze)**:

* §34 items 1–9 (architecture, module, entities, services, DB changes, authorization,
  audit, events, tests) → all happen during §38 Step 1 "Analyze" and are what gets
  reported in §38 Steps 2–5 ("Explain", "List affected modules/files", "Identify DB
  changes", "Identify authorization/security implications").
* §34 item 10 ("Implement the smallest coherent change") → §38 Step 6 "Implement".

No standalone use of §34 as a separate process going forward — it's folded into §38.

---

## 4. Two undefined/contradictory entry paths into RTO

**Where:** §10 (Shipment State Machine — Return lifecycle), §20 (NDR)

**Ambiguity:** §10's diagram shows `DELIVERY_FAILED → RTO_INITIATED` directly. §20's
NDR entity has its own status `RTO_REQUESTED`. As written, a failed delivery could
apparently skip the NDR workflow entirely and go straight to RTO, which conflicts with
§20 existing as "the dedicated NDR workflow" and with §39's requirement that every
status change represent a real, traceable operational process.

**Resolution:** NDR is the sole gateway into RTO. The real transition sequence is:

```text
OUT_FOR_DELIVERY
  ↓ (delivery attempt fails)
DELIVERY_FAILED
  ↓ (system creates)
NDR_CREATED  (NDR.Status = OPEN)
  ↓
NDR.Status: CONTACTING → CUSTOMER_CONFIRMED → RTO_REQUESTED
  ↓ (NDR resolution triggers shipment transition)
RTO_INITIATED
  ↓
RTO_IN_TRANSIT → RTO_AT_ORIGIN → RTO_DELIVERED
```

* §10's Return lifecycle diagram is corrected to insert the NDR step rather than
  jumping `DELIVERY_FAILED → RTO_INITIATED`.
* Auto-RTO policies (e.g., "auto-return after 3 failed attempts with no customer
  response") are implemented as an automated NDR resolution (`NDR.Resolution =
  AutoRTO`, `NDR.Status = RESOLVED`) — the shipment-level transition still fires
  through the NDR resolution handler, not a separate direct path. This preserves a
  single, auditable trigger point for `shipment.rto`.

---

## 5. Undefined scope for `SuperAdmin`, and no distinction between `OrganizationOwner`/`OrganizationAdmin`

**Where:** §4 (tenant isolation), §6 (RBAC role list)

**Ambiguity:** §4 states a user must never access another tenant's data. §6 lists
`SuperAdmin` in the same flat role list as tenant-scoped roles like
`OrganizationOwner`/`OrganizationAdmin`, without saying whether `SuperAdmin` is a
deliberate cross-tenant exception or just the top role inside one org. Nor is the
difference between `OrganizationOwner` and `OrganizationAdmin` stated.

**Resolution:**

* `SuperAdmin` is a **platform-level** role, not an organization-scoped one. Its
  `UserRole.OrganizationId` is `null`. It exists purely for platform operators
  (provisioning tenants, managing subscription/billing plans, emergency support
  access) and is the one deliberate, documented exception to §4's tenant isolation
  rule — every action it takes must write an audit record (§28) with a
  `IsCrossTenantAction = true` flag and the target `OrganizationId`, and any
  "impersonate a tenant user" support action must be time-boxed and separately
  logged.
* `OrganizationOwner`: exactly one per organization; automatically granted to the
  user who completes signup; the only role permitted to transfer ownership, manage
  billing/subscription, or delete the organization. Cannot be removed except via an
  explicit ownership-transfer flow.
* `OrganizationAdmin`: identical permission set to `OrganizationOwner` **except**
  `org.billing.manage`, `org.ownership.transfer`, and `org.delete`. Multiple users
  may hold this role.

---

## 6. No data-model mechanism for "all branches" or "temporary" branch access

**Where:** §7 (Branch Access)

**Ambiguity:** §7 requires branch access to support one branch, multiple branches,
**all branches**, branch hierarchy, and **temporary** assignment, all "via
`UserBranch`" — but a plain join table naturally only represents discrete, permanent
rows. Neither a wildcard nor an expiry concept is defined.

**Resolution:**

* `UserBranch { Id, UserId, BranchId, OrganizationId, GrantedAt, ExpiresAt (nullable),
  GrantedByUserId }`. A non-null `ExpiresAt` in the past makes the grant inactive;
  authorization queries always filter `ExpiresAt IS NULL OR ExpiresAt > now()`. This
  covers "temporary assignment" without a separate table or background job.
* "All branches" is **not** represented as N `UserBranch` rows. It's a separate flag
  on the role/user-role assignment: `UserRole.BranchScope` enum
  (`SpecificBranches | AllBranches`). When `AllBranches`, the authorization check for
  that role's permissions passes for any branch in the organization and consults
  `UserBranch` rows only for scope narrowing when relevant (e.g. default landing
  branch for UI). This keeps "all branches" cheap (no need to insert/maintain a row
  per branch as new branches are added — a real requirement, since new branches must
  automatically be in scope for an "all branches" grant).
* "Branch hierarchy" access (e.g., a Hub manager implicitly covers child branches)
  is resolved via `Branch.ParentBranchId` (already in §5): granting `UserBranch` on a
  parent branch implicitly includes descendant branches at authorization-check time
  — this is a graph walk, not additional rows.

---

## 7. Merchant API key tenancy resolution is unspecified

**Where:** §15 (Merchant Domain), §25 (Merchant API)

**Ambiguity:** §15 says merchants live inside an Organization; §25 defines merchant-
facing REST endpoints authenticated via API key, but nothing states how an inbound
API key resolves to *both* the owning Organization and the specific Merchant record,
which §33 requires to happen entirely server-side.

**Resolution:**

* API keys are issued per-`Merchant`, not per-`Organization`. `ApiKey { Id,
  MerchantId, OrganizationId, HashedKey, Prefix, Scopes[], IsActive, CreatedAt,
  RevokedAt }`. `OrganizationId` is denormalized onto the key row (rather than
  requiring a join through Merchant every request) purely for lookup performance —
  it must always be kept consistent with `Merchant.OrganizationId` (enforced by a DB
  constraint/trigger, not application code alone).
* Every merchant API request resolves `(OrganizationId, MerchantId)` from the hashed
  API key server-side during authentication middleware, before any handler runs. The
  request body/headers are never trusted for either value, consistent with §33.

---

## 8. Phase 8 gaps: PickupRider's permission set, RiderDelivery's scan step, and staff user provisioning

**Where:** §6/§17-19 (Rider domain), `RBAC.md` §4, `BRANCH-OPERATIONS.md` §4

**Ambiguity 1:** `RBAC.md` §4 spells out `DeliveryRider`'s permission set explicitly but only
describes `PickupRider` as "Pickup task permissions" — no concrete list.

**Resolution 1:** `PickupRider` holds `shipment.scan` only. A pickup rider's entire job this phase
is the pickup scan (`RiderPickupScanAsync`); nothing else in the v1 permission catalog is
pickup-specific. `DeliveryRider` holds `delivery.attempt`, `delivery.complete`, `cod.collect`,
`shipment.scan` exactly as `RBAC.md` §4 already specified. Both roles are seeded per-organization
at registration (`AuthService.RegisterOrganizationAsync`), same as `OrganizationOwner`.

**Ambiguity 2:** `BRANCH-OPERATIONS.md` §4 names `RiderDelivery` as if it were its own scan action
distinct from assignment, but `SHIPMENT-LIFECYCLE.md`'s transition table has exactly one arrow into
`OUT_FOR_DELIVERY` ("delivery rider assignment + out-for-delivery scan") — never two, unlike pickup
which has two (`PICKUP_ASSIGNED` is a real, separately-observable status).

**Resolution 2:** `RiderDelivery` is not a second call after assignment. Assigning a delivery task
to a rider (`RiderService.AssignTaskAsync` with `Type = Delivery`) performs the
`AtDestinationBranch -> OutForDelivery` transition in the same call — that assignment call IS the
`RiderDelivery` scan. `RiderPickup` keeps its two-step shape (assign, then a separate physical scan)
because `PICKUP_ASSIGNED` is a real status a dispatch board needs to show; there's no equivalent
named status between "delivery assigned" and "out for delivery" for a second step to represent.

**Ambiguity 3:** `DOMAIN-MODEL.md`'s `Rider.UserId` requires "a User with the PickupRider/
DeliveryRider role", but nothing in Phases 1-7 ever creates a second staff `User` in an
organization — `AuthService.RegisterOrganizationAsync` only creates the org-registration owner, and
no endpoint anywhere invites/provisions additional staff.

**Resolution 3:** Phase 8 adds a narrow `Identity.Application.IUserProvisioningService` (used only
by `Riders.Application.RiderService.CreateRiderAsync`) that creates a `User` + `UserRole`
(`BranchScope.SpecificBranches`) + `UserBranch` grant for one of the organization's already-seeded
role names. This is deliberately NOT a general "invite any staff member with any role" feature — a
real user-management/invite UI (covering `BranchManager`, `Dispatcher`, `SortingStaff`, etc.) is
still deferred, tracked as a known gap for whichever future phase needs it.

---

## 9. Phase 9: RTO transport reuse, `ReturnShipment`'s missing status column, and `NdrReason` duplication

**Where:** §13/§14 (Scanning/Manifests), §20/§21 (NDR/RTO), `BRANCH-OPERATIONS.md` §1

**Ambiguity 1:** `BRANCH-OPERATIONS.md` §1 lists `RTO` as one of the seven scan operation types
alongside `Receive, Sort, Dispatch, Transfer, RiderPickup, RiderDelivery` — implying RTO needs its
own transport mechanism — but `CLAUDE.md` §14/`BRANCH-OPERATIONS.md` §2 already define `Manifest`
as the one branch-to-branch transport primitive, and building a second one for RTO alone would
duplicate everything `Manifest` already does (missing-shipment detection, vehicle/driver capture,
seal/dispatch/receive/close lifecycle).

**Resolution 1:** `Manifest` gains a `Purpose` field (`Forward | Return`, defaults to `Forward`).
A `Return`-purpose manifest is created exactly like a forward one, just with `FromBranchId`/
`ToBranchId` pointing the opposite direction (from the branch that attempted delivery back toward
the branch the parcel originated at). The existing `/dispatch`/`/receive-scan` endpoints branch on
`Purpose` to call the RTO-specific pair of `IShipmentTransitionService` methods
(`DispatchManyRtoAsync`/`ReceiveRtoAtOriginAsync`) instead of the forward pair; `/scan` instead
rejects a Return-purpose manifest outright (`MANIFEST_WRONG_PURPOSE`) since a new `/rto-scan`
endpoint (mirroring `/scan`) exists specifically for adding shipments to a Return manifest, without
any status transition of its own — unlike the forward path's sort-scan, `RTO_INITIATED` has no
resting-status equivalent to move into, so the shipment stays `RtoInitiated` until the whole
manifest dispatches at once. `/seal`/`/close` need no branching — neither one touches
shipment-specific state.

**Ambiguity 2:** `DOMAIN-MODEL.md`'s original `ReturnShipment` sketch included `Status` and
`ReceivedAt` columns, but the underlying `Shipment` (via `OriginalShipmentId`) is already the
single source of truth for both once `RtoInitiated`/`RtoInTransit`/`RtoAtOrigin`/`RtoDelivered`
exist as real `ShipmentStatus` values — a second, independently-updated copy would need `Manifests`
to gain a project reference to `Ndr` purely to keep it in sync (`Manifests`' RTO receive-scan is
what actually knows when a return arrives).

**Resolution 2:** `ReturnShipment` does not store `Status`/`CurrentBranchId`/`ReceivedAt`.
`Ndr.Application.NdrService`'s `ReturnShipmentResponse` reads both live from
`IShipmentTransitionService.GetScanInfoAsync(OriginalShipmentId)` at request time instead — the
same "no cached copy of custody" principle CLAUDE.md §12 already states for `Shipment` itself,
applied one level up. `MerchantConfirmedAt` is the one exception: it is a genuine
`Ndr`-module-owned fact (when staff confirmed the merchant accepted the return) with no
cross-module coordination needed, so it stays a real stored column.

**Ambiguity 3:** `NDR.Reason` needs `DOMAIN-MODEL.md`'s "from `DeliveryAttempt.Reason`" list —
identical to `Riders.Domain.DeliveryAttemptReason` (Phase 8) — but `Ndr` has no natural reason to
depend on `Riders` (NDR resolution and RTO have nothing to do with rider assignment/tasks), and
`Riders` already depends on `Ndr` (for `INdrCreationService`) — a reference the other way would be
circular.

**Resolution 3:** `Ndr.Domain.NdrReason` is a fresh, identically-valued copy of
`Riders.Domain.DeliveryAttemptReason`, not a shared reference — the same choice
`DeliveryAttemptReason` itself already made relative to `CLAUDE.md` §19 in Phase 8.
`Riders.Application.RiderService` converts between the two via an explicit `switch` expression
(not a raw enum-to-enum cast), so a future divergence between the two lists fails at compile time
instead of silently miscasting.

**Ambiguity 4:** `CLAUDE.md` §20 describes an "auto-return after N failed attempts" policy for
`AutoRTO`, but no `N` is specified anywhere and no scheduled-job infrastructure exists yet in this
codebase (Hangfire is in the tech stack per `CLAUDE.md` §1 but nothing uses it before Phase 11).

**Resolution 4:** `NdrResolution.AutoRto` is a selectable resolution outcome only this phase —
identical in every way to `NdrResolution.Rto` except the audit trail records which one a caller
chose. No scheduled trigger exists to set it automatically; a human or a future automated caller
(once Phase 11's background-job infrastructure lands and product defines `N`) must still call
`POST /ndrs/{id}/resolve`. This still satisfies `CLARIFICATIONS.md` #4's single-auditable-trigger-
point requirement — `AutoRTO` was never required to be automatic, only to remain a distinct,
recorded resolution reason from an ordinary staff-initiated `Rto`.

---

## 10. Phase 9 follow-up: `ShipmentStatus`/`ShipmentEventType` must only ever grow by appending

**Where:** `src/Modules/Shipments/Domain/ShipmentStatus.cs`, `ShipmentEventType.cs`

**Issue:** Both enums persist as plain PostgreSQL `integer` columns with no `HasConversion<string>()`.
Phase 9's original implementation inserted five new members mid-list (between `DeliveryFailed` and
`Delivered`), which silently reassigned `Delivered`'s ordinal from 9 to 14 and `Cancelled`'s from 10
to 15 — corrupting the meaning of any already-persisted row. Caught in Phase 9's final whole-branch
review, fixed by moving the new members after `Cancelled`.

**Rule going forward:** new members MUST always be appended at the end of both enums, never inserted
between existing members, for as long as they persist as raw integers. If a future phase needs to
insert a member logically "in the middle" of the conceptual status list, append it physically at the
end of the C# enum anyway — the enum's declaration order and the domain's logical status order are
allowed to diverge; only the declaration order affects the database.

---

## 11. Phase 10: `CODCollection`'s full lifecycle status, eager creation, settlement linkage, and adjustment approval

**Where:** `CLAUDE.md` §22-23, `COD-LIFECYCLE.md` (all sections), `DOMAIN-MODEL.md` "COD & Finance"

**Ambiguity 1:** `DOMAIN-MODEL.md`'s `CODCollection` entity lists `Status (Expected|Collected)` —
only the first of `COD-LIFECYCLE.md` §1's seven states — but that same document's prose states
"the COD *lifecycle* status above lives on `CODCollection`, not on `Shipment`", naming all seven
states as one chain.

**Resolution 1:** `CODCollection.Status` is a seven-member enum
(`CodExpected|CodCollected|RiderDeposited|BranchReconciled|FinanceVerified|
MerchantSettlementCreated|Settled`), read literally from the prose rather than the narrower table.
Every step in the chain (`Cod.Application.CodService.CreateRiderSettlementAsync`,
`CreateBranchSettlementAsync`, `CreateFinanceVerificationAsync`, `CreateMerchantSettlementAsync`,
`SettleMerchantSettlementAsync`) bulk-updates the `Status` of every `CODCollection` row it covers,
so "where is this shipment's COD right now" is always a single-row read, never a join across the
five downstream tables.

**Ambiguity 2:** `COD-LIFECYCLE.md` §1's table says `CODCollection` is "created at delivery, linked
to `DeliveryProof`" for the `COD_EXPECTED -> COD_COLLECTED` transition — but §3 separately defines
"Expected COD" as `Σ Shipment.CODAmount` for not-yet-delivered shipments, implying `CODCollection`
rows exist before delivery too.

**Resolution 2:** `CODCollection` is created eagerly, at shipment-booking time (a new
`ICodCollectionCreationService` — the same narrow cross-module-write interface shape
`INdrCreationService` established in Phase 9 — called from
`Shipments.Application.ShipmentService.CreateShipmentAsync` whenever `Shipment.CodAmount > 0`), at
`CodExpected`. `RiderService.CompleteDeliveryAsync` (Phase 8) then calls a second narrow interface,
`ICodCollectionService.MarkCollectedAsync`, to move the *existing* row to `CodCollected` at
delivery — matching the "linked to `DeliveryProof`" half of the table literally (`DeliveryProofId`
is stamped at that step) while resolving the apparent contradiction with §3. `MerchantId`,
`BranchId` (the shipment's `DestinationBranchId` at booking), and `ShipmentTotalCharge` are
snapshotted onto the row at creation — `Cod` has no project reference to `Shipments` at all as a
result, a stricter version of the "no cached custody copy" principle (`CLAUDE.md` §12) applied to
financial figures the same way `Shipment.TotalCharge` itself already locks pricing at booking
(`CLAUDE.md` §16).

**Ambiguity 3:** `DOMAIN-MODEL.md`'s `BranchCashSettlement` entity lists a
`RiderCashSettlementIds[]` array field to link the rider settlements it covers, but this codebase's
established one-to-many convention elsewhere (`ManifestShipment.ManifestId`,
`DeliveryTask.DeliveryRunId`) is always a child-side foreign key, never a parent-side array column.

**Resolution 3:** `RiderCashSettlement` gains a nullable `BranchCashSettlementId` foreign key
instead; `BranchCashSettlement` has no array column. `CreateBranchSettlementAsync`'s response still
returns the covered `RiderCashSettlementIds` as a `List<Guid>`, computed from the query, so no
information is lost to API consumers — only the storage shape differs from the original sketch.

**Ambiguity 4:** `CLAUDE.md` §23 says adjustments require approval "where appropriate" for amounts
above "a configurable threshold", but no threshold configuration entity or setting exists anywhere
in this codebase.

**Resolution 4:** `Adjustment` always requires a separate approval step in v1 — `RequestAdjustmentAsync`
only ever inserts an unapplied `Adjustment` row (`Applied = false`); a second call,
`ApproveAdjustmentAsync`, is the only place that ever rewrites the target figure
(`MerchantSettlementItem.OtherDeductions`/`NetPayable`, and the parent
`MerchantSettlement.TotalPayable`). No threshold configuration was needed to resolve this — "where
appropriate" resolves to "always" until a future phase introduces per-org configurable amounts.

**Implementation note (not an ambiguity, but worth recording):** `Riders.Application.RiderService.
CompleteDeliveryAsync`'s conditional `cod.collect` permission check (required only when the
shipment being delivered carries COD) is the first Application-layer consumer of
`SharedKernel.IPermissionChecker` — previously only the `RequirePermission` route filter used it.
`FinancialTransactionConfiguration` gives `(ReferenceEntityType, ReferenceEntityId)` a *unique*
index (not just a lookup index) so "verify the same `BranchCashSettlement` twice" is DB-enforced,
not just caught by an `AnyAsync` pre-check — the same read-then-write race lesson as Phase 5's
`RateCard` versioning fix (`CLAUDE.md`'s cross-phase rule), proven by a real `Task.WhenAll`
concurrent-request test against Postgres.

---

## 12. Phase 11: outbox envelope shape, webhook secret storage, retry policy, template seeding, and the background-context query-filter gotcha

**Where:** `CLAUDE.md` §26-27, `ARCHITECTURE.md` "Domain events & reliability"/"Background jobs —
Hangfire", `DOMAIN-MODEL.md` "Notifications"/"Webhooks", `API-SPEC.md` §5

**Ambiguity 1:** `DOMAIN-MODEL.md`'s `WebhookSubscription` entity lists `Secret (hashed at rest)`,
but HMAC-signing an outgoing payload requires the plaintext secret at send time — a one-way hash
cannot produce a valid signature.

**Resolution 1:** `WebhookSubscription.Secret` is encrypted at rest (ASP.NET Core Data
Protection, `Courier.Webhooks.Infrastructure.DataProtectionSecretProtector`), not hashed — a
reversible operation, decrypted only inside `WebhookDeliveryAttemptService`'s signing path. The
plaintext is returned exactly once, in `POST /api/v1/webhooks`'s response body, and never
appears in any `GET` response afterward.

**Ambiguity 2:** No source document specifies a webhook retry attempt count or backoff schedule
— `API-SPEC.md` §5 says only "exponential backoff... capped attempts."

**Resolution 2:** 5 attempts, backoff 1m/2m/4m/8m/16m (attempt *N*'s delay is 2^(N-1) minutes),
then the delivery is left in a terminal state (`NextRetryAt` null, `DeliveredAt` still null) —
visible via `GET /webhooks/{id}/deliveries` and recoverable via `POST
/webhooks/deliveries/{id}/replay`, which resets the attempt counter to 0 and re-sends
immediately rather than continuing the old cycle.

**Ambiguity 3:** `CLAUDE.md` §27 says notification templates "must be configurable," but no
source document specifies whether default templates are seeded per-organization (like `Role`
rows at registration) or resolved some other way.

**Resolution 3:** No `NotificationTemplate` rows are seeded anywhere this phase — no
org-registration-time seeding, no migration data seed. `NotificationDispatchService` checks for
an active org-specific override row first; if none exists (every organization, every event, this
phase), it falls back to a compiled-in default body
(`Courier.Notifications.Application.NotificationDefaultTemplates`). This avoids a new
`Identity -> Notifications` module dependency that eager per-org seeding at registration would
have required, and a future template-management API (deferred, out of this phase's scope) simply
inserts override rows into the same table this resolver already checks — no code change needed
when that API lands.

**Real bug class found only by the end-to-end test, not any unit test (recorded here because it
will recur in a future phase's own background job/consumer unless watched for):**
`AppDbContext`'s tenant query filters (`_currentTenantProvider.OrganizationId != null && ...`)
evaluate to *false for every row* — not "unfiltered," silently *empty* — whenever
`ICurrentTenantProvider.OrganizationId` is null, which is always true inside a `BackgroundService`
or Hangfire job, since neither runs inside an HTTP request. Three call sites this phase queried a
tenant-filtered table from exactly such a context and got silently-empty results back — no
exception, nothing logged, just a feature that quietly never works:
`WebhookDeliveryAttemptService.AttemptAsync`'s `WebhookSubscription` join (every delivery attempt
was a no-op, stuck at `Attempt = 0` forever), `NotificationDispatchService`'s `NotificationTemplate`
lookup (an org's override template would never be found), and
`IMerchantContactLookupService.GetContactEmailAsync`'s `Merchant` lookup (every dispatch resolved
to `NoRecipient` regardless of a real `ContactEmail` on file). All three were invisible to unit
tests, because this project's unit-test `DbContext` fixtures never configure the tenant filter in
the first place (per this project's own established pattern) — only a real `AppDbContext`-backed
integration test exercising the actual background code path could catch it, which is exactly what
`NotificationsAndWebhooksEndToEndTests.cs` did. Fixed two ways depending on the caller's shape:
`OutboxPublisherJob`/`WebhooksConsumer`'s own subscription-matching query (both already correct,
predating this bug) and `WebhookDeliveryAttemptService`/`NotificationDispatchService`'s internal
queries use `.IgnoreQueryFilters()` directly, since the caller already resolved and trusts
`organizationId` from the outbox envelope; `IMerchantContactLookupService.GetContactEmailAsync`
instead gained `organizationId` as an explicit required parameter (not just an internal
`IgnoreQueryFilters()` bypass), since it is a general-purpose, potentially-shared lookup interface
where a silent unconditional bypass could become a real cross-tenant leak for some future
HTTP-scoped caller that assumed the ambient filter still applied. **General rule for any future
phase adding a `BackgroundService` or Hangfire job:** grep every query that job/consumer reaches,
transitively, against every entity carrying `HasQueryFilter` in `AppDbContext`, and add an explicit
scope (either `IgnoreQueryFilters()` plus a trusted explicit predicate, or a required
`organizationId` parameter) — never assume "it compiled and the unit tests passed" proves a
background-context query is correct, since this project's unit-test fixtures cannot catch this
class of bug by construction.

**Implementation note (not an ambiguity, but worth recording):** `WebhookDelivery` gains one
field beyond `DOMAIN-MODEL.md`'s original sketch: `SourceEventId` (the originating
`OutboxMessage.Id`), with a unique index on `(WebhookSubscriptionId, SourceEventId)` — this is
the concrete mechanism behind "a redelivered RabbitMQ message can't double-create delivery rows,"
the same DB-enforced-not-just-checked-then-written lesson `FinancialTransaction`'s uniqueness
constraint established in Phase 10. `NotificationLog.RecipientAddress` is nullable (`string?`),
accommodating the `NoRecipient` status (no `Merchant.ContactEmail` on file) — also a departure
from the literal, non-nullable field `DOMAIN-MODEL.md`'s original sketch showed. The outbox
publisher wraps each `OutboxMessage`'s bare JSON payload in a small envelope
(`{ EventId, OrganizationId, EventType, Data }`) before publishing to RabbitMQ — `OrganizationId`
and `EventId` are separate columns on `OutboxMessage`, not part of the payload each producing
module built, and both consumers need them (tenant scoping, idempotency respectively).

---

## 13. Phase 12: self-escalation carve-out, ApiKey credential format, two public-tracking status-mapping corrections, and three implementation-time fixes

**Where:** §15/§25 (Merchant Domain, Merchant API), §24 (Public Tracking), §33 (Security)

**Self-escalation carve-out.** `RequireMerchantPermission` (JWT-only) and the new
`RequireMerchantPermissionAnyCredential` (JWT or API key) are two separate SharedKernel
extensions, not one widened filter. API-key management itself, merchant staff invite, KYC
upload, and profile update stay on the JWT-only extension deliberately — an API key's default
scope is every permission its creating Owner holds, so if key-management endpoints accepted API-key
auth, a key could create, list, or revoke other keys (including itself). Only Shipments, Webhooks,
Cod settlement reads, and Pickups were switched onto the any-credential extension.

**`ApiKey` credential format.** `{Prefix}.{Secret}` where `Prefix` is 8 random hex characters
(globally unique, indexed, used for O(1) lookup) and `Secret` is a 32-byte
`RandomNumberGenerator`-sourced value, SHA-256 hashed at rest — same hash-at-rest pattern as
`RefreshToken` (Phase 1) and `WebhookSubscription.Secret`'s pre-Phase-11 hashing precedent. The
plaintext is shown exactly once, in the creation response.

**Public tracking status mapping corrections.** The design spec's §9 table was written against
CLAUDE.md's illustrative state names, not the real 16-value `ShipmentStatus` enum. Three
corrections made during implementation (Task 12): `AtOriginBranch` was regrouped from the
spec's `Received` bucket into `InTransit` — it happens chronologically *after* `PickedUp`, so
the original grouping would have made the public status visibly regress; `PickupAssigned`
(unmentioned in the spec table) joins `Created` under `Received`; `Cancelled` (also unmentioned)
got its own new 8th public status rather than being folded into `DeliveryAttemptUnsuccessful`,
since collapsing a genuine cancellation into that bucket would misleadingly imply a retry is
still coming. See `PublicTrackingStatusMapper.Map` for the authoritative, exhaustive mapping —
`PublicTrackingStatusMapperTests.Map_EveryDefinedShipmentStatus_HasAMapping` guards against a
future `ShipmentStatus` value ever landing unmapped.

**Idempotency and rate limiting scope, confirmed as designed.** Idempotency-Key enforcement and
rate limiting are both self-selecting on *credential kind* (merchant JWT/API key vs staff JWT vs
anonymous), not on route ownership — every existing staff-facing test from prior phases needed
zero changes, confirmed by the full-solution test run staying green after every task in this
phase's plan.

**`ApiKey.MerchantId` foreign key gap (Task 2).** The plan's own brief omitted a required FK —
`ApiKey.MerchantId` needed an explicit `.HasOne<Merchant>()` registration in
`ApiKeyConfiguration.cs`, matching every other Merchants-module child entity's established
convention (`MerchantKycDocument`, `MerchantAddress`, `MerchantUser` all declare this locally).
Caught by task review, fixed in a follow-up round before the migration was finalized —
`ApiKeyConfiguration.cs` now has `builder.HasOne<Merchant>().WithMany().HasForeignKey(k =>
k.MerchantId).OnDelete(DeleteBehavior.Restrict)` alongside its indexes on `OrganizationId`,
`MerchantId`, and a unique index on `Prefix`.

**Cross-tenant AWB collision risk on `/track/{awb}` (Task 12).** `Shipment.Awb` uniqueness is
enforced only per-`(OrganizationId, Awb)` (a pre-existing Phase 6 design —
`ShipmentConfiguration.cs`'s unique index), not globally. This was safe until Phase 12 because
every prior lookup path went through the tenant query filter; the new anonymous `/track/{awb}`
endpoint is the first lookup with zero tenant scope, making a same-day cross-org AWB collision
(plausible given the org-slug-prefix + date + per-org-daily-sequence generation algorithm) a real
cross-tenant data leak risk. Resolution: `PublicTrackingService.GetByAwbAsync` fails closed — if
more than one `Shipment` row matches the AWB, it returns not-found rather than arbitrarily
picking one. The ROOT CAUSE (making AWB actually globally unique) is explicitly deferred to a
future phase — it's Phase 6 schema/generation logic requiring its own migration and rollout, out
of Phase 12's scope. A related, smaller follow-up item: `ShipmentConfiguration.cs` also has no
standalone index on `Awb` alone (only the composite `(OrganizationId, Awb)`), so the anonymous
tracking endpoint's lookup can't use an efficient leading-column index seek — worth revisiting
alongside the uniqueness fix.

**Serilog `writeToProviders`/`preserveStaticLogger` interaction (Task 13).** Implementing the
required test technique (a custom `ILoggerProvider` capturing `ILogger<T>` output) required
adding `writeToProviders: true` to the existing `builder.Host.UseSerilog(...)` call in
`Program.cs` (Serilog doesn't forward to DI-registered `ILoggerProvider`s by default) plus
`preserveStaticLogger: true` (to fix a cross-host static-`Log.Logger`-clobbering race across
`WebApplicationFactory`'s parallel test-host construction — an established pattern in this
codebase, see the `MigrationLock` precedent). `preserveStaticLogger: true` alone would have
silently broken the pre-existing `app.UseSerilogRequestLogging()`'s production
request-completion logging (that middleware falls back to the static `Log.Logger` when no
explicit `Logger` option is given, and `preserveStaticLogger: true` stops that static field from
ever being assigned). Caught by task review before merge. Resolution: `UseSerilogRequestLogging`
is now called with an explicit `options.Logger` resolved from DI
(`app.Services.GetRequiredService<Serilog.ILogger>()`), so it no longer depends on the static
field at all, while `preserveStaticLogger: true` is kept for the test-race fix it was added for.

### Follow-ups identified in final review (not fixed this phase)

*Recorded 2026-08-27, from the final whole-branch review of Phase 12.* Five of that review's
findings were fixed in the same fix wave (see the commit `fix: address final-review findings …` —
idempotency no longer caching non-2xx responses, the API-key path enforcing `Merchant.IsActive`,
`PickupRequest.MerchantId`'s missing FK, `ApiKeyAuthenticationHandler`'s
`API_KEY_REVOKED`/`API_KEY_INVALID` mislabeling, and `merchant.apikeys.manage` being excluded from
every issued key). The seven items below were deliberately **not** fixed and are recorded here so
they are carried forward rather than silently dropped.

**13.1 Public tracking's `IpRateLimitFilter` has no reverse-proxy awareness.** `IpRateLimitFilter`
partitions on `HttpContext.Connection.RemoteIpAddress`, and `Program.cs` never calls
`UseForwardedHeaders()`. Behind any load balancer or ingress — this project's stated deployment
target (`ARCHITECTURE.md`) — every caller presents the proxy's address, so all public traffic
collapses into a single shared rate-limit bucket and the first busy client locks everyone out of
`/track/{awb}`: a self-inflicted DoS. Not fixed now because `UseForwardedHeaders()` is only safe
with `KnownProxies`/`KnownNetworks` populated for the real topology (an unrestricted forwarded-header
middleware lets any caller spoof `X-Forwarded-For` and evade the limiter entirely), and that
topology hasn't been chosen yet. A future phase must configure forwarded headers against the actual
infrastructure — and verify the limiter partitions on the real client IP — **before** `/track/{awb}`
is served through a proxy in production.

**13.2 `API_KEY_INVALID`/`API_KEY_REVOKED` are never delivered to the client.** The design spec's
§11 error contract names both codes, but `ApiKeyAuthenticationHandler`'s
`AuthenticateResult.Fail("…")` message strings are not surfaced anywhere in the pipeline — a caller
presenting a bad or revoked key gets a generic, empty-body `401`, with no way to tell "this key was
revoked" from "this key never existed". The fix wave corrected which of the two strings each branch
reports, but that is internal semantics only; the delivery mechanism is still missing. Not fixed now
because delivering them correctly needs a custom `HandleChallengeAsync` override, and with two
authentication schemes registered against the same policy (`Bearer` + `ApiKey`) both schemes'
challenge handlers run on failure — sequencing the response writes so one handler doesn't clobber the
other's body needs more care than a same-day fix round allows. A future phase should implement this
properly: most likely have only the scheme that was actually attempted write a body, or centralize
the error-envelope writing in one place shared by both schemes.

**13.3 No in-flight reservation for `Idempotency-Key` — concurrent retries with the same key can
both execute.** `IdempotencyMiddleware` reads the store before the request and writes to it only
after the response is produced. Two requests carrying the same key that arrive before the first
completes therefore both miss the cache and both run, e.g. creating two shipments with two COD
amounts under one key. This is the single most common real-world trigger for idempotency — a
client-side timeout followed by an immediate retry — so the mechanism does not yet protect against
its primary case. Not fixed now because it requires a new atomic primitive on `IIdempotencyStore`
(a Redis `SETNX`-style pending marker claimed before `_next` and converted to the real record
afterwards, with a TTL so a crashed request can't wedge a key), plus a product decision on what the
second concurrent caller should see while the first is still in flight — block and wait, `409`, or
`202` with a poll location. That is real design work, not a fix-round change. A future phase should
design and add it.

**13.4 Extra `ApiKeys` lookup (and occasional write) on every JWT request to the four multi-scheme
groups.** `ApiKeyAuthenticationHandler.ExtractCredential` also reads `Authorization: Bearer …`, and
`raw.Split('.', 2)` yields two parts for a three-segment JWT as readily as for a real
`{Prefix}.{Secret}` key. Every staff- or merchant-JWT call to Shipments, Webhooks, Pickups, or the
COD-settlement reads consequently issues a `WHERE "Prefix" = …` query against `ApiKeys` (whose
result is a guaranteed miss, and on a freak match would also `SaveChangesAsync` a `LastUsedAt`
update) before the JWT scheme succeeds. Correctness is unaffected — this is a performance note only,
which is why it was left alone in a pre-merge fix round. A future pass could add a cheap shape
pre-check (the prefix is exactly 8 lowercase hex characters) and skip the database entirely when the
candidate can't possibly be a key.

**13.5 Failed API-key authentication attempts aren't logged.** `ApiKeyRequestLoggingMiddleware` is
gated on the `api_key_scopes` claim, which only a *successfully* authenticated key ever carries, so
a rejected key produces no log line at all. `API-SPEC.md` §2 names this logging as the feed for
abuse detection, yet brute-force and credential-stuffing attempts against the Merchant API are
currently invisible. Deferred because it needs its own design decision about where failed-auth
logging belongs in the pipeline: the existing middleware sits after authentication/authorization has
already resolved, so a rejected credential never reaches it as currently gated, and the natural
alternatives (an `OnAuthenticationFailed`-style hook on the scheme, or a dedicated middleware ahead
of the gate) have different access to the request context. A future phase should pick one and cover
it with tests.

**13.6 `ShipmentEvent.UserId`/`AuditLog.UserId` can now hold an `ApiKey.Id`, with no discriminator.**
Those columns already conflated staff `User.Id` with `MerchantUser.Id` (a pre-existing condition —
merchant JWTs have written `MerchantUser.Id` there since Phase 4), but Phase 12 is the first time
the stored value can identify a **non-person** actor: `ApiKeyAuthenticationHandler` puts `ApiKey.Id`
in the `sub` claim, so an API-key-driven shipment creation records a key, not a human. This bears
directly on CLAUDE.md §39's "who performed the operation" traceability requirement — reading such a
row back cannot currently answer whether a person or an integration acted. Recorded as a
documentation note only; no code change is proposed here, because adding an actor-type discriminator
touches the audit and event schemas across every prior phase. Any future phase revisiting audit or
event actor identity should start from the fact that a `UserId` column is not always a person.

**13.7 Redis is now a hard, fail-closed availability dependency on the shipment-creation and
public-tracking paths.** `IRateLimiter` and `IIdempotencyStore` are both Redis-backed and neither
has a degraded-service fallback, so a Redis outage turns `POST /api/v1/shipments`,
`POST /api/v1/pickups`, and `/track/{awb}` into `500`s rather than gracefully bypassing rate
limiting and idempotency. This is very likely the *correct* behaviour — silently skipping
idempotency on a financial write path would risk duplicate COD shipments, and failing closed on
rate limiting is defensible — but it is a new single point of failure on the revenue path that was
never explicitly adopted as policy anywhere in the docs. Recorded here as a deliberate decision, now
written down. Revisit only if Redis availability becomes a real operational concern, and if so
decide per-concern (idempotency should almost certainly stay fail-closed even if rate limiting is
allowed to fail open).

---

**F0.1 No API exposes the logged-in user's permissions or branch grants.** Discovered
planning Frontend Phase F0 (`docs/FRONTEND-ROADMAP.md`,
`docs/superpowers/specs/2026-08-29-frontend-roadmap-design.md`). `POST /api/v1/auth/login`
returns only `AuthResult(AccessToken, RefreshToken, UserId, OrganizationId)`
(`Modules/Identity/Application/AuthDtos.cs`), and `JwtTokenService.IssueAccessToken` puts only
`sub`/`org_id`/`email` in the JWT (`Modules/Identity/Infrastructure/JwtTokenService.cs`). Nothing
lets a client discover the user's permission set or `UserBranch` grants — needed by F0's
`usePermission` hook and permission-gated nav (design spec §5).

**Resolution:** Add `GET /api/v1/auth/me` (JWT-authenticated, no extra permission required — any
authenticated user may read their own session) returning `{ userId, organizationId, email,
fullName, permissions: string[], branches: [{ branchId, branchName, scope, expiresAt }] }`. The
permission list reuses `PermissionChecker`'s existing role→permission query
(`Set<Role>().Where(r => Set<UserRole>().Any(ur => ur.UserId == userId && ur.RoleId == r.Id))
.SelectMany(r => r.Permissions).Select(p => p.Key).Distinct()`) rather than duplicating that logic.
This is a read-only addition to the existing `Identity` module — no schema change, no new domain
logic, consistent with `AuthEndpoints`'s existing shape. Implemented as Task 1 of the Frontend
Phase F0 plan (`docs/superpowers/plans/2026-08-29-frontend-phase-f0-design-system-app-shell.md`).

---

**F2.1 No generic branch-update endpoint exists.** Discovered planning Frontend Phase F2
(`docs/superpowers/specs/2026-08-29-frontend-phase-f2-branches-branch-dashboard-design.md`).
`BranchesEndpoints.cs` exposes `POST /api/v1/branches` (create), `GET /api/v1/branches` (list),
`GET /api/v1/branches/{id}` (detail), and `PUT /api/v1/branches/{id}/delivery-zone` — that last one
is the *only* field a branch's own creator can change after creation. `FRONTEND-ROADMAP.md`'s
Phase F2 entry says "branch list/detail/create/update UI," but there is nothing to build "update"
against beyond delivery zone.

**Resolution: deferred, not added this phase.** Frontend Phase F2's branch detail page renders
every other field (`Code`, `Name`, `Type`, the geography chain, `AddressLine`, `Phone`,
`OperatingHours`, `ParentBranchId`) read-only — no UI is built for an operation the backend can't
perform. A future phase (frontend or backend) should add `PUT /api/v1/branches/{id}` (a normal
full-record update, matching the shape `CreateBranchRequest` already establishes) if editing branch
details after creation becomes a real operational need; nothing currently depends on this gap being
closed.

---

## 14. Frontend Phase F3: no staff-facing tracking-event endpoint, no shipment list filters

**Gap 1 — tracking timeline.** `ShipmentResponse` exposes only `CurrentStatus`; no endpoint
projects a shipment's `ShipmentEvent` history for staff use. The only reader of `ShipmentEvent`
rows is `PublicTrackingService` (backing the public `/track/{awb}` endpoint), which is
deliberately limited per CLAUDE.md §24 — it excludes internal branch/user/notes data, so it's the
wrong shape for an internal ops view even if reused as-is.

**Resolution:** Frontend Phase F3 ships the shipment detail Sheet without a tracking timeline —
current status only, via `StatusBadge`. **Unblocks with:** a new staff-facing
`GET /api/v1/shipments/{awb}/events` endpoint (or equivalent) projecting `ShipmentEvent` rows
(`EventType, PreviousStatus, NewStatus, BranchId, UserId, RiderId, Timestamp, Notes`) for the
caller's own org, permission `shipment.read`. Not built this phase — backend work is paused.

**Gap 2 — list filters.** `GET /api/v1/shipments` accepts only `page`/`pageSize` — no status,
branch, date-range, or AWB/recipient search query parameter.

**Resolution:** Frontend Phase F3 ships the shipment list with server-side pagination only, no
filter UI. **Unblocks with:** query parameters on the existing list endpoint (e.g. `status`,
`originBranchId`/`destinationBranchId`, `createdFrom`/`createdTo`, `search`) plus the
corresponding `ShipmentService.ListAsync` filtering logic. Not built this phase.

Both decisions made with the user during Phase F3's brainstorming (2026-08-29), consistent with
the "for now focus on frontend" instruction governing this and neighboring phases.

---

## 15. Frontend Phase F4: Manifests had no list endpoint; manual-AWB scanning only this phase

**Gap — no manifest list/search endpoint.** `ManifestsEndpoints.cs` exposed only `GET /{id:guid}`
(by internal id, known only right after creation) and `GET /{id:guid}/missing-shipments` — no way
to browse manifests at all, unlike every other entity (Branches, Shipments). This uniquely blocked
a real chunk of Frontend Phase F4's own scope: the destination branch's staff has no way to *find*
an inbound manifest to receive, since receiving naturally happens at a different branch/session
than creation.

**Resolution: added, not deferred.** `GET /api/v1/manifests?branchId=&status=` — a plain
(non-paginated) list filtered to manifests where the caller's accessible branches (for
`manifest.read`) include `FromBranchId` or `ToBranchId`. Framed as closing a foundational
inconsistency every other entity already had, not new business feature work.

**Deferred, not silently dropped: camera-based barcode/QR scanning.** This phase's scan
inputs (build-manifest scan, receive-scan) are manual AWB text entry only. Unblocks with a
scanning library choice and camera-permission UX — no backend dependency, purely a frontend
addition whenever a future phase picks it up.

**Also found, not fixed (out of scope for this task):** running `validate_palette.js` against the
existing shipment-status palette plus this phase's two new manifest tokens surfaced that two
*pre-existing* tokens (`--status-delivered` and `--status-created`) already sit below the
normal-vision adjacent-pair floor (ΔE 11.2, light mode) against each other — a debt that predates
this phase and isn't something either of this phase's two new tokens (`--status-sealed`,
`--status-received`) touches or worsens. Worth a dedicated pass revisiting the whole shipment
status palette at some point; not blocking anything today since every status always ships with a
visible text label, never color alone.

---

## 16. Frontend Phase F6: `Ndr` responses need shipment AWB, and no lookup-by-shipment filters exist

**Where:** `Ndr.Application.NdrDtos` (`NdrResponse`, `ReturnShipmentResponse`), `Ndr.Presentation.NdrEndpoints`

**Gap 1 — no AWB on either response.** `NdrResponse` and `ReturnShipmentResponse` both carry only
an opaque `ShipmentId`/`OriginalShipmentId` Guid — useless for a staff-facing queue or
return-tracking screen, which needs the human-facing AWB. `NdrService.UpdateAsync`/`ResolveAsync`
and `ToReturnShipmentResponseAsync` already fetch `IShipmentTransitionService.GetScanInfoAsync`
(which carries `Awb`) for other reasons; `NdrService.GetByIdAsync` and `ListAsync`'s all-branches
path do not yet.

**Resolution:** `NdrResponse` gains `Awb`; `ReturnShipmentResponse` gains `OriginalAwb`. Both are
sourced from the same `ShipmentScanInfo` lookup every affected method already performs or now
performs once per row (bounded by page size, same cost class as every other paginated list in this
codebase).

**Gap 2 — no way to find "the NDR(s)/return shipment for shipment X".** Neither
`GET /api/v1/ndrs` nor `GET /api/v1/return-shipments` accepts a shipment-scoped filter, so a
shipment's own detail view has no way to discover its NDR history or RTO linkage.

**Resolution:** `GET /api/v1/ndrs` gains an optional `shipmentId` query parameter (composes with
the existing `status` filter and branch-scoping); `GET /api/v1/return-shipments` gains an optional
`originalShipmentId` query parameter (composes with the existing branch-scoping). A shipment may
accumulate more than one `NDR` over its lifetime (at most one unresolved at a time, per
`NdrConfiguration.cs`'s documented invariant) but at most one `ReturnShipment` (RTO is a terminal
path with no reopening logic in this codebase).

Discovered planning Frontend Phase F6 (`docs/FRONTEND-ROADMAP.md`,
`docs/superpowers/specs/2026-09-01-frontend-phase-f6-ndr-rto-design.md`). Implemented as Tasks 2-5
of the Frontend Phase F6 plan.

---

## 17. Frontend Phase F7: `Cod` enums need JSON serialization, no list endpoints for rider/branch settlements or adjustments, and a missing settle-guard on adjustment approval

**Where:** `Cod.Domain.CodCollectionStatus`/`MerchantSettlementStatus`/`FinancialTransactionType`, `Cod.Application.CodService`, `Cod.Presentation.CodEndpoints`

**Gap 1 — no `JsonStringEnumConverter` on any of the 3 Cod enums.** Same latent bug class hit and
fixed in every prior phase (`BranchType` F2, `ShipmentStatus` F3, `ManifestPurpose`/`ManifestStatus`
F4, 5 Riders enums F5, 3 Ndr enums F6). Fixed proactively here for all 3 Cod enums at once.

**Gap 2 — no `GET` to list `RiderCashSettlement` or `BranchCashSettlement` rows.** Only `POST`
(create) existed for either, so a branch user had no way to see which rider settlements were still
unreconciled or which branch settlements were still unverified without already knowing an id —
exactly the gap Phase 10's final review flagged as making shortages/overages structurally
unreadable.

**Gap 3 — no `GET` to list `Adjustment` rows.** Only `POST /adjustments` (needs an `EntityId` the
caller already knows) and `POST /adjustments/{id}/approve` (needs an `Id` the caller already knows)
existed — an approver had no way to discover pending adjustments at all. Also flagged in Phase 10's
final review.

**Resolution (gaps 2–3):** `GET /api/v1/cod/rider-settlements`, `GET /api/v1/cod/branch-settlements`,
and `GET /api/v1/cod/adjustments` added, all paginated and `cod.read`-gated, matching
`ListCollectionsAsync`'s existing filter/branch-scoping shape.

**Gap 4 — `ApproveAdjustmentAsync` had no guard against approving against an already-`Settled`
`MerchantSettlement`.** It unconditionally rewrote `MerchantSettlementItem.OtherDeductions`/
`NetPayable` and recomputed `MerchantSettlement.TotalPayable` with no check on
`MerchantSettlement.Status`. If the parent settlement was already settled (merchant already paid
the old figure), approving a pending adjustment silently changed `TotalPayable` on a row that's
supposed to be closed, with no new payout to match — a real correctness bug against CLAUDE.md §23.

**Resolution (gap 4):** `ApproveAdjustmentAsync` now throws `CodValidationException` before opening
its transaction when the target `MerchantSettlement.Status == Settled`, mirroring
`SettleMerchantSettlementAsync`'s own status guard.

Discovered planning Frontend Phase F7 (`docs/FRONTEND-ROADMAP.md`,
`docs/superpowers/specs/2026-09-01-frontend-phase-f7-cod-operations-design.md`). Implemented as
Tasks 1–5 of the Frontend Phase F7 plan.

**Also found, not fixed — pre-existing `--status-in-transit`/`--status-out-for-delivery` dark-mode
duplicate:** validating the new `Cod.*` status-badge token group (Task 14) via the dataviz skill's
palette validator surfaced a genuine defect in `src/web/src/app/globals.css`'s `.dark` block, not
just a marginal floor miss: `--status-in-transit` and `--status-out-for-delivery` are byte-identical
(both `38.0 78.9% 57.3%`, ΔE 0.0) in dark mode, while light mode correctly keeps them distinct
(`35.3°` vs `38.0°` hue). This predates Frontend Phase F7 and already affects every `ShipmentStatus`
badge using these tokens today (`PickedUp`/`AtOriginBranch`/`Sorted`/`AtDestinationBranch` all share
`--status-in-transit`, indistinguishable in dark mode from any `OutForDelivery` badge). The `Cod.*`
chain avoids `--status-in-transit` entirely rather than reusing it (a brute-force search over the
7 candidate tokens' orderings found an arrangement using `--status-sealed`/`--status-received`/
`--status-rto` instead that cleanly passes both the normal-vision and CVD floors for every adjacent
pair — see `statusConfig.ts`'s own comment). Fixing the root cause requires re-deriving a correct,
independently-validated third amber shade for dark mode across the whole app, which is a properly
scoped design task on its own, not a one-line color tweak bundled into this phase.

---

## 18. Frontend Phase F9: `Identity` had almost no Role/Permission/UserBranch/invite management surface at all

**Where:** `Identity.Domain.BranchScope`, `Identity.Application.AuthService`/`RoleService`/`UserManagementService`, `Identity.Presentation.RolesEndpoints`/`UsersEndpoints`/`AuthEndpoints`, `Notifications.Application.NotificationDispatchService`

**Gap 1 — no `JsonStringEnumConverter` on `BranchScope`.** Same latent bug class hit and fixed in
every prior phase (`BranchType` F2, `ShipmentStatus` F3, `ManifestPurpose`/`ManifestStatus` F4, 5
Riders enums F5, 3 Ndr enums F6, 3 Cod enums F7). Fixed proactively here.

**Gap 2 — no Role CRUD, no Permission catalog read endpoint, no RolePermission assignment
endpoint.** The `Role`/`Permission`/`UserRole` domain model has carried the shape needed for all
of this since Phase 1 (`Role.OrganizationId` non-null already distinguishes a custom org role from
a `null`-owned platform role, `Role.IsSystemDefault` already distinguishes a seeded role), but
zero HTTP surface existed to create, edit, delete, or even list a role, or to see what permissions
exist to assign. **Resolution:** `GET /api/v1/permissions` (grouped catalog), `GET/POST/PUT/DELETE
/api/v1/roles[/{id}]`, all `role.manage`-gated (Tasks 3–5).

**Gap 3 — only 3 of CLAUDE.md §6's 17 default roles were ever seeded** (`OrganizationOwner`,
`DeliveryRider`, `PickupRider`, added incrementally across Phases 1 and 8). Any org wanting e.g. a
`FinanceManager` or `BranchManager` role had no way to get one short of hand-rolling a custom role
through this phase's own new Role UI. **Resolution:** 11 more org-scoped roles from CLAUDE.md §6
seeded at registration (`OrganizationAdmin`, `OperationsManager`, `BranchManager`, `Dispatcher`,
`SortingStaff`, `CounterStaff`, `FinanceManager`, `FinanceStaff`, `CustomerSupport`, `Auditor`,
`HubStaff` — 14 seeded roles total), permission sets grounded in `docs/RBAC.md` §4's illustrative
coverage table where it names one explicitly, reasoned from the same table's role descriptions
otherwise (Task 2). `SuperAdmin` (platform-level, `OrganizationId = null`) and `Merchant`/
`MerchantStaff` (the existing `MerchantUser`/API-key auth track, `docs/RBAC.md` §5, a wholly
separate identity space) are deliberately excluded — no platform-admin UI exists or is planned
this phase, and merchant-side roles belong to Frontend Phase F10's merchant portal, not this one.

**Gap 4 — no `UserBranch` grant/list/revoke endpoint at all**, despite
`BranchAuthorizationChecker.GetAccessibleBranchIdsAsync` (`Infrastructure/
BranchAuthorizationChecker.cs`) already reading and correctly enforcing `UserBranch.ExpiresAt` —
confirmed by the pre-existing `HasAccessAsync_ExpiredGrant_IsRejected`/
`HasAccessAsync_ExpiredAndValidGrants_FiltersPerRow` tests in
`tests/Identity.Tests/BranchAuthorizationCheckerTests.cs`, which needed no changes this phase. The
enforcement was real; only the management surface to create/see/revoke a grant was missing.
**Resolution:** `GET/POST /api/v1/users/{id}/branches`, `DELETE /api/v1/users/{id}/branches/
{branchId}`, `user.read`/`user.update`-gated (Task 6).

**Gap 5 — `GET /api/v1/users` had no pagination and returned only `{Id, Email, FullName,
IsActive}`, with no role or branch-grant visibility.** Its missing explicit `OrganizationId`
filter is *not* a cross-tenant bug — `Persistence/AppDbContext.cs` already registers a global
`HasQueryFilter` on `User` scoped to the ambient tenant (line 328) — but the endpoint was
otherwise unusable for an admin screen. **Resolution:** pagination + `RoleName`/`BranchCount`
enrichment (Task 7).

**Gap 6 — no user deactivate/reactivate endpoint**, despite `AuthService.LoginAsync`/
`RefreshTokenAsync` already correctly rejecting an inactive `User.IsActive == false` account — the
enforcement existed, the toggle didn't. **Resolution:** `PATCH /api/v1/users/{id}/deactivate` /
`/reactivate`, `user.update`-gated (Task 8).

**Gap 7 — no user-invite flow of any kind**, and the one adjacent mechanism
(`IUserProvisioningService.ProvisionStaffUserAsync`) is deliberately narrow (an upfront plaintext
password, a role name that must already be seeded, a single branch — built for Riders' internal
self-service rider creation, not a general admin invite). **Resolution:** a new `UserInvite`
entity (Task 9) plus `POST /api/v1/users/invite` (Task 11) and `POST /api/v1/auth/accept-invite`
(Task 12), following the email-invite design decision recorded in the phase's design spec.

**Gap 8 (discovered building Gap 7's resolution) — `NotificationDispatchService.
DispatchEventAsync` could only resolve a recipient by reading `MerchantId` from the event payload
and looking it up via `IMerchantContactLookupService`; there was no path for an event whose
payload already carries its own recipient address**, which the `user.invited` event needs (the
invited user is not a merchant). **Resolution:** an additive `RecipientEmail` payload-property
check before the existing `MerchantId` branch (Task 10) — existing `MerchantId`-keyed events are
untouched.

Discovered planning Frontend Phase F9 (`docs/FRONTEND-ROADMAP.md`,
`docs/superpowers/specs/2026-09-02-frontend-phase-f9-rbac-admin-design.md`). Implemented as
Tasks 1–13 of the Frontend Phase F9 plan.

---

## 19. Frontend Phase F10: three more enums needed `JsonStringEnumConverter`, plus three new merchant-reachable read routes

Same recurring bug class hit in every prior phase (`BranchType` F2, `ShipmentStatus` F3,
`ManifestPurpose`/`ManifestStatus` F4, 5 Riders enums F5, 3 Ndr enums F6, 3 Cod enums F7):
`KycDocumentType`, `MerchantUserRole`, and `PickupRequestStatus` had no
`[JsonConverter(typeof(JsonStringEnumConverter))]`. `PickupRequestStatus` dates back to Phase 12
but was never caught until this phase's merchant-pickups screen became the first UI anywhere to
render `PickupRequestResponse.Status`. Fixed directly on all three enums.

Building the Merchant Portal frontend also required three backend reads that had no
merchant-reachable path at all, found by direct inspection before any frontend code was written:

1. No `GET /api/v1/merchant/auth/me` existed — the frontend session hook needs one call
   to learn who is logged in, their role, and their resolved permission set, mirroring staff's
   own `GET /api/v1/auth/me`. Added.
2. `GET /api/v1/branches`, while nominally reachable by a merchant JWT (staff and merchant
   tokens both validate under the shared "Bearer" scheme), always returned `403` for a merchant
   caller — `RequirePermissionFilter` resolves the generic `"sub"` claim against staff
   `UserRole`/`RolePermission` tables, where no matching row exists for a merchant user id.
   Added a dedicated `GET /api/v1/merchant/branches` (org-scoped, no accessible-branch-id
   narrowing — merchants have no branch concept) instead of touching the staff route.
3. `POST /api/v1/pricing/quote` and `GET /api/v1/service-types` were both staff-only
   (`GetUserId() is null → Unauthorized`, `.RequirePermission("pricing.read")`) — the merchant
   booking form needs both for its live quote preview and Service Type picker. Extended both to
   the same dual-auth pattern `ShipmentsEndpoints` already uses, gated by a new
   `MerchantPermissionCatalog.PricingRead = "merchant.pricing.read"` (Owner + Staff). Also fixed
   a real latent risk this widening would otherwise have introduced: `PricingService.QuoteAsync`
   trusted the client-supplied `MerchantId` unmodified (used to resolve a `MerchantRateCard`
   discount) — a merchant caller could have read another merchant's negotiated rate by passing
   their id. The endpoint now forces `MerchantId` to the caller's own id for any merchant-JWT/
   API-key caller, mirroring `CreateShipmentRequest`'s existing "server overrides the
   client-supplied value" doc comment.

Deferred, not fixed this phase (recorded here so they aren't silently dropped):
- No update-role/deactivate endpoint on `MerchantStaffEndpoints` (only invite + list exist).
- No public tracking-event timeline for a shipment anywhere in the backend — affects the staff
  app too, not merchant-specific; `ShipmentResponse` has no embedded event history and no
  `/events` route exists.
- `MerchantAddress` has no Presentation endpoint at all — a merchant cannot save/reuse a sender
  address; the booking form's sender fields are entered manually every time.
- No merchant-facing webhook-subscription UI, though the backend (`WebhooksEndpoints`,
  `MerchantPermissionCatalog.WebhooksManage`) already fully supports it — out of this phase's
  named scope (booking/tracking/COD/invoices/API keys).

---

## Summary of schema additions implied by this document

* `UserRole` (new entity) — resolves #1, carries `BranchScope` enum — resolves #6.
* `LocalLevel` (new entity, `Type` enum) — resolves #2; `Branch` gains
  `ProvinceId/DistrictId/LocalLevelId/WardNumber` FKs in place of free-text fields.
* `NDR.Resolution` value `AutoRTO` as the single trigger for system-initiated RTO —
  resolves #4.
* `Role.OrganizationId` (nullable, null = platform role) — resolves #5.
* `UserBranch.ExpiresAt` (nullable) — resolves #6.
* `ApiKey.MerchantId` + denormalized `OrganizationId` — resolves #7.

These should be reflected when `DOMAIN-MODEL.md` and the Phase 1/2 EF Core models are
written.
