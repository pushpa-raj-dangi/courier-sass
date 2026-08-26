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
