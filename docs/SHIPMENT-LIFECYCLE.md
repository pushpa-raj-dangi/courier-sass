# Shipment Lifecycle

This is the corrected, single source of truth for shipment status transitions —
supersedes the diagram ambiguity noted in `CLARIFICATIONS.md` #4.

## 1. Principle

A status change is only ever a side effect of a real operational event: a scan, a
manifest action, a delivery attempt, an NDR resolution, or a booking action. Nothing
sets `Shipment.CurrentStatus` directly from an admin UI field edit (CLAUDE.md §39).
Every transition writes an immutable `ShipmentEvent` (CLAUDE.md §11) and, where
custody physically moved, updates `CurrentBranchId`/`CurrentCustodianId` via a scan or
manifest-receive operation — a status change alone is never treated as proof of
physical transfer (CLAUDE.md §12).

## 2. Full State Diagram

```text
CREATED
  ↓ (pickup rider assigned)
PICKUP_ASSIGNED
  ↓ (rider scans pickup)
PICKED_UP
  ↓ (received scan at origin branch)
AT_ORIGIN_BRANCH
  ↓ (sort scan)
SORTED
  ↓ (manifest dispatched)
IN_TRANSIT
  ↓ (manifest received at destination branch)
AT_DESTINATION_BRANCH
  ↓ (delivery rider assigned + out-for-delivery scan)
OUT_FOR_DELIVERY
  ↓ (delivery attempt succeeds, POD captured)
DELIVERED  ───────────────────────────────────────────► (terminal)

OUT_FOR_DELIVERY
  ↓ (delivery attempt fails)
DELIVERY_FAILED
  ↓ (system auto-creates)
NDR_CREATED  [NDR.Status = OPEN]
  │
  ├─ NDR.Status → CONTACTING → CUSTOMER_CONFIRMED → REATTEMPT_SCHEDULED
  │      ↓
  │   OUT_FOR_DELIVERY   (loop back into delivery lifecycle; AttemptNumber++)
  │
  └─ NDR.Status → CONTACTING → CUSTOMER_CONFIRMED → RTO_REQUESTED
         ↓ (NDR resolution triggers shipment transition)
      RTO_INITIATED
         ↓ (dispatched back toward origin via manifest)
      RTO_IN_TRANSIT
         ↓ (received scan at origin branch)
      RTO_AT_ORIGIN
         ↓ (merchant confirmation / return complete)
      RTO_DELIVERED  ─────────────────────────────────────► (terminal)
```

`RTO_INITIATED` is **only** reachable via an NDR reaching `RESOLVED` with a
return-triggering resolution (including system-driven `AutoRTO` after N failed
attempts, per `CLARIFICATIONS.md` #4). There is no direct
`DELIVERY_FAILED → RTO_INITIATED` transition in the implementation, even though an
earlier draft of the state diagram showed one.

## 3. Valid Transition Table

| From | Allowed To | Triggering Operation |
|---|---|---|
| CREATED | PICKUP_ASSIGNED | Rider assignment |
| PICKUP_ASSIGNED | PICKED_UP | Rider pickup scan |
| PICKED_UP | AT_ORIGIN_BRANCH | Branch receive scan |
| AT_ORIGIN_BRANCH | SORTED | Sort scan |
| SORTED | IN_TRANSIT | Manifest dispatch |
| IN_TRANSIT | AT_DESTINATION_BRANCH | Manifest receive |
| AT_DESTINATION_BRANCH | OUT_FOR_DELIVERY | Delivery rider assignment + scan |
| OUT_FOR_DELIVERY | DELIVERED | Successful delivery attempt + POD |
| OUT_FOR_DELIVERY | DELIVERY_FAILED | Failed delivery attempt |
| DELIVERY_FAILED | NDR_CREATED | System (automatic) |
| NDR reattempt resolution | OUT_FOR_DELIVERY | NDR resolved → reattempt scheduled |
| NDR RTO resolution | RTO_INITIATED | NDR resolved → RTO requested/auto |
| RTO_INITIATED | RTO_IN_TRANSIT | Manifest dispatch (return leg) |
| RTO_IN_TRANSIT | RTO_AT_ORIGIN | Manifest receive at origin |
| RTO_AT_ORIGIN | RTO_DELIVERED | Merchant confirmation |

Any transition not in this table is rejected server-side with a validation error; the
attempted transition and rejection reason are still logged for diagnosis, but no
`ShipmentEvent`/status change is written.

## 4. `ShipmentEvent` (CLAUDE.md §11)

Append-only. Fields: `ShipmentId, EventType, PreviousStatus, NewStatus, BranchId,
UserId, RiderId, Timestamp, Latitude, Longitude, Notes, Metadata`. Historical events
are never edited or deleted — a correction is itself a new event with a `Notes`/
`Metadata` explanation, never a mutation of the original row (mirrors the POD
correction rule in CLAUDE.md §18).

## 5. Physical Custody vs Status

`Shipment.CurrentBranchId` / `CurrentCustodianId` change only through scan/manifest
operations (`Scanning` module), independently of whether a status transition also
occurred in the same operation. The system must be able to answer "who has this
parcel right now" purely from custody state, without inferring it from
`CurrentStatus` — see `BRANCH-OPERATIONS.md` for the scan/manifest operations that
update custody.

## 6. Pricing Snapshot

`Shipment` stores the computed `DeliveryCharge/InsuranceCharge/OtherCharges/
TotalCharge/CODAmount` at booking time, resolved from the `RateCard` active at that
moment (`Pricing` module). These fields are never recalculated from the current rate
card after booking (CLAUDE.md §16) — a later rate card version change must not alter
historical shipments.
