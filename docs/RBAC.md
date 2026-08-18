# RBAC — Roles, Permissions, Branch Scope

Authorization is **permission-based**, never role-name-based
(`if (user.Role == "Admin")` is forbidden — CLAUDE.md §6). Roles are just named,
organization-owned bundles of permissions; the authorization check always evaluates a
permission plus a branch scope, never a role identity.

## 1. Authorization Decision

```text
Allow(action) IFF:
  user has a UserRole granting `permission` for action        AND
  ( that UserRole.BranchScope == AllBranches
    OR target BranchId ∈ user's active UserBranch grants
         (including descendants of any granted parent branch) )
```

See `CLARIFICATIONS.md` #1 and #6 for the `UserRole`/`UserBranch` schema this relies
on. "Active" excludes `UserBranch` rows whose `ExpiresAt` has passed.

`SuperAdmin` is the one documented exception: it operates with `OrganizationId = null`
and bypasses tenant/branch scoping entirely, but every action is written to
`AuditLog` with `IsCrossTenantAction = true` (`CLARIFICATIONS.md` #5).

## 2. Default Roles

| Role | Org-scoped? | Notes |
|---|---|---|
| SuperAdmin | No (platform) | Tenant provisioning, platform support only |
| OrganizationOwner | Yes, exactly 1 per org | Only role that can manage billing, transfer ownership, delete org |
| OrganizationAdmin | Yes | Same as Owner minus billing/ownership/delete |
| OperationsManager | Yes | Cross-branch operational permissions |
| BranchManager | Yes, branch-scoped | Full operational permissions within assigned branch(es) |
| Dispatcher | Yes, branch-scoped | Manifest/transfer/dispatch permissions |
| SortingStaff | Yes, branch-scoped | Scan/sort permissions |
| CounterStaff | Yes, branch-scoped | Booking/counter shipment creation |
| PickupRider | Yes, branch-scoped | Pickup task permissions |
| DeliveryRider | Yes, branch-scoped | Delivery task, POD, COD collection permissions |
| FinanceManager | Yes | COD reconciliation, settlement approval, adjustments |
| FinanceStaff | Yes | COD reconciliation, no adjustment approval |
| CustomerSupport | Yes | Shipment read, NDR follow-up |
| Merchant | N/A — Merchant-scoped, see below | Own shipments only |
| MerchantStaff | N/A — Merchant-scoped | Subset of Merchant permissions |
| Auditor | Yes | Read-only across audit/financial history |
| HubStaff | Yes, branch-scoped | Hub receive/dispatch permissions |

Default roles are seeded per-Organization at creation time (not shared global rows),
so an org can later clone/adjust one without affecting other tenants
(`CLARIFICATIONS.md` #1).

## 3. Permission Catalog

Format: `resource.action`. This is the full v1 catalog from CLAUDE.md §6; extend it
here as new modules ship rather than inventing ad hoc checks elsewhere.

```text
shipment.create        shipment.read          shipment.update
shipment.cancel        shipment.assign        shipment.scan
shipment.sort          shipment.transfer

delivery.assign        delivery.attempt       delivery.complete

ndr.read                ndr.manage             ndr.resolve

rto.read                 rto.manage

cod.read                 cod.collect            cod.reconcile           cod.settle
cod.adjust

manifest.create          manifest.seal           manifest.dispatch
manifest.receive         manifest.close

branch.read               branch.create           branch.update

merchant.read             merchant.create         merchant.update
merchant.kyc.review

rider.read                 rider.create           rider.assign

pricing.read               pricing.manage

report.read                 report.export

user.read                   user.create             user.update
role.manage

audit.read

org.billing.manage          org.ownership.transfer   org.delete
```

`ndr.*`, `rto.*`, `manifest.*`, `merchant.kyc.review`, `pricing.*`, `role.manage`,
`org.*` extend the exact list in CLAUDE.md §6 — that list was illustrative
("Examples:"), this is the concrete v1 catalog the code checks against.

## 4. Illustrative Role → Permission Coverage

Not exhaustive — the source of truth is the seeded permission rows per role, not this
table — but indicative of intent:

* **BranchManager**: `shipment.read/assign/scan`, `manifest.*`, `delivery.assign`,
  `ndr.manage`, `cod.reconcile`, scoped to their assigned branch(es) only (the exact
  example given in CLAUDE.md §7).
* **FinanceManager**: `cod.*` including `cod.adjust` and `cod.settle`, `report.read/export`,
  org-wide (`BranchScope = AllBranches`) since settlement spans branches.
* **DeliveryRider**: `delivery.attempt`, `delivery.complete`, `cod.collect`,
  `shipment.scan` (rider pickup/delivery scans only), branch-scoped to their base
  branch.
* **Merchant**: `shipment.create/read/cancel` limited to shipments where
  `Shipment.MerchantId == self`, `cod.read` limited to own settlements — enforced at
  the application-service query level, not just the permission catalog, since a
  Merchant is not a courier-org staff member and has no `UserBranch` concept at all.
* **Auditor**: `*.read` + `audit.read` across the org, no write permissions anywhere.

## 5. Merchant/MerchantStaff Authorization Model

Merchants are not staff users and do not use `UserBranch`/branch scoping. A
`MerchantUser` (or a merchant's `ApiKey`, see `CLARIFICATIONS.md` #7) is scoped by
`MerchantId` within an `OrganizationId`; every query additionally filters
`MerchantId == self`, on top of the standard `OrganizationId` tenant filter.
`MerchantStaff` holds a subset of `Merchant` permissions (e.g. no settlement
visibility) — modeled the same `UserRole` way, just with `Role.OrganizationId` set to
the owning courier org and permissions restricted to the merchant-facing catalog
subset.
