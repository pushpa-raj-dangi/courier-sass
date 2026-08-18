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
