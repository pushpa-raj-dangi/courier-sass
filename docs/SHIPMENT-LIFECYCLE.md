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
| SORTED | CUSTOMS_PREPARED | Customs declaration completed (international, same-branch: `OriginBranchId == DestinationBranchId`) |
| AT_DESTINATION_BRANCH | CUSTOMS_PREPARED | Customs declaration completed (international, different-branch) |
| CUSTOMS_PREPARED | HANDED_TO_CARRIER | Handoff scan (outbound, I4) |
| HANDED_TO_CARRIER | IN_INTERNATIONAL_TRANSIT | Carrier event (outbound, I4) |
| IN_INTERNATIONAL_TRANSIT | DELIVERED_ABROAD | Carrier event (outbound, terminal, I4) |
| IN_INTERNATIONAL_TRANSIT | CUSTOMS_HOLD | Carrier event (outbound, I4) |
| IN_INTERNATIONAL_TRANSIT | INTERNATIONAL_EXCEPTION | Carrier event (outbound, I4) |
| CUSTOMS_HOLD | IN_INTERNATIONAL_TRANSIT | Carrier event or staff override (outbound, I4) |
| CUSTOMS_HOLD | INTERNATIONAL_EXCEPTION | Carrier event or staff override (outbound, I4) |
| INTERNATIONAL_EXCEPTION | IN_INTERNATIONAL_TRANSIT | Carrier event or staff override (outbound, I4) |
| INTERNATIONAL_EXCEPTION | DELIVERED_ABROAD | Carrier event or staff override (outbound, I4) |
| CREATED | IN_INTERNATIONAL_TRANSIT | Carrier event, only once an inbound booking is linked (inbound, I4) |
| IN_INTERNATIONAL_TRANSIT | CUSTOMS_CLEARANCE | Carrier event (inbound, I4) |
| IN_INTERNATIONAL_TRANSIT | CUSTOMS_HOLD | Carrier event (inbound, I4) |
| IN_INTERNATIONAL_TRANSIT | INTERNATIONAL_EXCEPTION | Carrier event (inbound, I4) |
| CUSTOMS_CLEARANCE | CUSTOMS_HOLD | Carrier event (inbound, I4) |
| CUSTOMS_HOLD | CUSTOMS_CLEARANCE | Carrier event or staff override (inbound, I4) |
| CUSTOMS_HOLD | INTERNATIONAL_EXCEPTION | Carrier event or staff override (inbound, I4) |
| INTERNATIONAL_EXCEPTION | IN_INTERNATIONAL_TRANSIT | Carrier event or staff override (inbound, I4) |
| INTERNATIONAL_EXCEPTION | CUSTOMS_CLEARANCE | Carrier event or staff override (inbound, I4) |
| IN_INTERNATIONAL_TRANSIT or CUSTOMS_CLEARANCE | RECEIVED_FROM_CARRIER | Receive scan (inbound, I4) |
| RECEIVED_FROM_CARRIER | AT_DESTINATION_BRANCH | Receive scan, same-branch gateway (inbound, I4, second event of the same scan) |
| RECEIVED_FROM_CARRIER | SORTED | Sort scan (inbound, different-branch gateway, I4 — `SortAsync`'s only other from-status) |

Any transition not in this table is rejected server-side with a validation error; the
attempted transition and rejection reason are still logged for diagnosis, but no
`ShipmentEvent`/status change is written.

### `CUSTOMS_PREPARED` (International, I3)

Only reachable for `Scope = International` shipments (`International.Application.InternationalShipmentService.PrepareCustomsAsync`,
gated on `InternationalLeg.CustomsCompletedAt` being set), via `Shipments.Application.IShipmentInternationalGateway.TransitionToCustomsPreparedAsync`.
Its expected starting status is chosen dynamically rather than being a single hardcoded value,
because — unlike every other transition in this document — outbound international shipments can
legitimately reach `CustomsPrepared` from two different places depending on branch topology:

- From `SORTED`, when the shipment's `OriginBranchId == DestinationBranchId` — the common case of a
  branch that handles exports locally. There is no manifest path between `SORTED` and
  `AT_DESTINATION_BRANCH` in that case (`Manifests.Application.ManifestService.CreateAsync` hard-rejects
  a manifest with `FromBranchId == ToBranchId`), so `CUSTOMS_PREPARED` is the direct next step.
- From `AT_DESTINATION_BRANCH`, when the origin and destination branches differ — after the
  existing, unmodified manifest dispatch/receive cycle has moved the shipment there.

As of I4, `CUSTOMS_PREPARED` is no longer terminal: the handoff scan moves an outbound shipment to
`HANDED_TO_CARRIER` (see the next subsection). Separately, nine last-mile/NDR/RTO methods
(`AssignDeliveryToRiderAsync`, `CompleteDeliveryAsync`, `FailDeliveryAsync`,
`TransitionToNdrCreatedAsync`, `ReattemptDeliveryAsync`, `InitiateRtoAsync`, `DispatchManyRtoAsync`,
`ReceiveRtoAtOriginAsync`, `ConfirmRtoDeliveredAsync`) reject any shipment with `Direction ==
Outbound` outright, so an outbound international shipment cannot skip `CUSTOMS_PREPARED` and fall
into a domestic last-mile/NDR/RTO status. See `docs/INTERNATIONAL.md`'s "International module core
(I3)" section for the full guard rationale (it is keyed on `Direction`, not `Scope`).

### Carrier lifecycle (International, I4)

Seven statuses appended after `CUSTOMS_PREPARED` (ordinal-append-only, per CLARIFICATIONS.md #10):
`HANDED_TO_CARRIER`, `IN_INTERNATIONAL_TRANSIT`, `DELIVERED_ABROAD`, `CUSTOMS_HOLD`,
`INTERNATIONAL_EXCEPTION`, `CUSTOMS_CLEARANCE`, `RECEIVED_FROM_CARRIER`. Every one is reachable only
for `Scope = International` shipments. `Shipments.Domain.InternationalTransitionRules` is the single
table encoding every row above from `HANDED_TO_CARRIER` onward except the handoff/receive scans
themselves, keyed by `(Direction, From, To, Trigger)`:

- **`Carrier`** trigger: every listed outbound/inbound row is allowed, driven by
  `IShipmentInternationalGateway.ApplyCarrierStatusAsync` (carrier-driven, no user, timestamped with
  the carrier's own `OccurredAt`, metadata `{ source: "Carrier", carrierAccountId, carrierAwb,
  carrierEventId, carrierStatusCode, location }`). A target equal to the current status is a no-op;
  any other move not in the table throws `ShipmentInvalidTransitionException`.
- **`StaffOverride`** trigger: only the exits from `CUSTOMS_HOLD`/`INTERNATIONAL_EXCEPTION` — via
  `POST /international/shipments/{id}/status-override` (`international.tracking.manage`), a required
  non-empty reason (≤2000 chars), audited with before/after status and reason.
- **Known gap:** the table allows `CUSTOMS_HOLD → IN_INTERNATIONAL_TRANSIT` for the `Carrier`
  trigger unconditionally — there is no check that a carrier event releasing a hold is newer than
  the shipment's current status, so a late/out-of-order carrier event can release a `CUSTOMS_HOLD`
  that is still real at the carrier. See `docs/INTERNATIONAL.md`'s "Carrier layer (I4)" section.

`HANDED_TO_CARRIER` (`CustomsPrepared → HandedToCarrier`) and `RECEIVED_FROM_CARRIER` are **scan
operations**, not carrier events, each with its own gateway method
(`HandToCarrierAsync`/`ReceiveFromCarrierAsync`) and not present in `InternationalTransitionRules`:

- **Handoff** (outbound only) requires a `Booked` outbound carrier booking and branch access to the
  shipment's current branch; clears `CurrentBranchId`/`CurrentCustodianUserId`/
  `CurrentCustodianRiderId` — the carrier becomes the custodian of record (derived from status +
  the active booking, not a `Shipment` column). The shipment transition and the booking's
  `CustodyStartedAt` stamp commit in the same DB transaction; if the booking was concurrently
  cancelled, the whole transaction rolls back with a validation error.
- **Receive** (inbound only, at `OriginBranchId`) requires a linked, `Booked` inbound carrier
  booking, and is only valid from `IN_INTERNATIONAL_TRANSIT` or `CUSTOMS_CLEARANCE`. Sets
  `CurrentBranchId = OriginBranchId`, `CurrentCustodianUserId` = the scanning user. **Same-branch
  gateway** (`OriginBranchId == DestinationBranchId`): the same scan immediately stages a second
  transition, `RECEIVED_FROM_CARRIER → AT_DESTINATION_BRANCH` (event type `ReceivedAtDestination`,
  timestamped 1 ms after the first so timelines order correctly) — one scan, two immutable
  `ShipmentEvent`s, one save. **Different-branch gateway:** the shipment stays at
  `RECEIVED_FROM_CARRIER`, and `ShipmentService.SortAsync` (via `ManifestService.ScanAsync`, its
  only caller) accepts `RECEIVED_FROM_CARRIER` as an additional from-status — for `Direction ==
  Inbound` only — so the normal `SORTED → manifest → IN_TRANSIT → AT_DESTINATION_BRANCH` flow
  applies from there. No other transition method changes for this.

These four gateway methods (`HandToCarrierAsync`, `ReceiveFromCarrierAsync`,
`ApplyCarrierStatusAsync`, `OverrideInternationalStatusAsync`) use their own
`StageInternationalTransition`/`SaveInternationalTransitionAsync` pair in
`ShipmentService.InternationalLifecycle.cs` — not the domestic `TransitionAsync` — but preserve the
same guarantees: an immutable `ShipmentEvent` per move, an audit entry, and xmin concurrency
detection. A concurrency conflict throws `ShipmentConcurrencyException` (derives from
`ShipmentInvalidTransitionException`) rather than a plain rejection, so a caller that distinguishes
a lost race from a genuine invalid transition (the carrier-event processor) can retry it instead of
giving up. Unlike `TransitionAsync`, **no outbox row is written** by any of these four methods — see
`docs/INTERNATIONAL.md`'s "Carrier layer (I4)" section and the design spec's "Rulings made during
planning and execution" for why.

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
