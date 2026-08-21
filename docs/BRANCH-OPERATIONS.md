# Branch Operations — Scanning, Manifests, Dashboard

## 1. Scan Operations (CLAUDE.md §13)

Supported inputs: AWB barcode, QR code, single scan, continuous scan, bulk scan.

Supported operations: `Receive, Sort, Dispatch, Transfer, RiderPickup, RiderDelivery,
RTO`.

Every scan validates, in order, before it's allowed to mutate anything:

1. **User permission** — does the acting user hold the permission for this operation
   (e.g. `shipment.scan` plus the specific operation context).
2. **Branch access** — is the target shipment's relevant branch within the user's
   `UserBranch`/`AllBranches` scope (`RBAC.md`).
3. **Shipment state** — is the shipment's `CurrentStatus` one that legally permits this
   operation (cross-checked against `SHIPMENT-LIFECYCLE.md`'s transition table).
4. **Expected operation** — does this scan type match what's expected next for this
   shipment (e.g. a `Dispatch` scan on a shipment that hasn't been `Sorted` yet is
   rejected, not silently accepted).
5. **Shipment custody** — is the shipment's current physical custody consistent with
   this operation being physically possible (e.g. can't `RiderDelivery`-scan a
   shipment whose custody says it's still at a different branch).

A scan that fails any check is rejected with a specific error, logged, and does not
write a `ShipmentEvent`.

## 2. Manifests (CLAUDE.md §14)

`Manifest` groups many shipments moving together between two branches.

```text
Manifest { Id, Code (e.g. MNF-20260818-001), OrganizationId,
           FromBranchId, ToBranchId, VehicleId/Plate, DriverId,
           Status, ShipmentCount, CreatedByUserId, CreatedAt }
ManifestShipment { ManifestId, ShipmentId, ScannedAt (added to manifest),
                    ReceivedAt (nullable — set on receive scan) }
ShipmentTransfer { ManifestId, ShipmentId, FromBranchId, ToBranchId, Timestamp }
```

### Lifecycle

```text
DRAFT → SEALED → IN_TRANSIT → RECEIVED → CLOSED
```

* **DRAFT**: shipments are added via sort/dispatch scans; can still be modified.
* **SEALED**: no further shipments can be added; count is locked.
* **SEALED → IN_TRANSIT**: a single `dispatch` action records vehicle/driver and moves every
  `ManifestShipment`'s shipment `Sorted → InTransit` — there is no separately observable
  "dispatched but not yet in transit" resting state, so `Manifest.Status` goes straight to
  `IN_TRANSIT` (Phase 7 plan's resolved design decision #2).
* **IN_TRANSIT**: en route to `ToBranchId`.
* **RECEIVED**: destination branch scans every shipment against the manifest.
  * Each successfully scanned shipment: `ManifestShipment.ReceivedAt` set,
    shipment custody moves to `ToBranchId`, shipment status →
    `AT_DESTINATION_BRANCH`.
  * Any shipment on the manifest not scanned within the receiving session is
    flagged **missing** — a distinct, visible exception state (not silently
    dropped), surfaced on the branch dashboard until resolved.
* **CLOSED**: receiving session finalized; missing-shipment exceptions, if any,
  are handed to an investigation workflow (out of scope to fully design here —
  flag for `NDR`-style follow-up in a later phase if volume warrants a dedicated
  entity).

## 3. Branch Dashboard (CLAUDE.md §30)

Visible to branch-scoped users according to their permissions, filtered to the
branches they have access to:

* Incoming shipments (manifests `IN_TRANSIT` toward this branch)
* Outgoing shipments (shipments `AT_ORIGIN_BRANCH`/`SORTED`, not yet dispatched)
* Pending sorting (received but not yet `SORTED`)
* Pending dispatch (`SORTED` but not yet on a `DISPATCHED` manifest)
* Received manifests (recently `CLOSED` at this branch)
* Pending manifests (`IN_TRANSIT` awaiting receipt at this branch)
* Riders (assigned to this branch, their current task load)
* Deliveries (today's `OUT_FOR_DELIVERY` count, success rate)
* COD (branch-level `RIDER_DEPOSITED`/`BRANCH_RECONCILED` totals pending finance
  verification)
* Exceptions (missing manifest shipments, open NDRs, failed deliveries needing
  action)

## 4. Rider Operations Touching Branch Custody

`RiderPickup` and `RiderDelivery` scans are the two scan types that move custody
between a branch and a rider (`CurrentCustodianId` = the rider) rather than between
two branches — see `SHIPMENT-LIFECYCLE.md` §5 and the `Riders`/`Delivery` modules for
the rider-side task flow.
