# COD Lifecycle

COD is a financial subsystem, not a boolean flag on a shipment (CLAUDE.md §22).

## 1. State Machine

```text
COD_EXPECTED
  ↓ (rider collects cash from recipient at delivery)
COD_COLLECTED
  ↓ (rider deposits cash at branch, end of run)
RIDER_DEPOSITED
  ↓ (branch counts and confirms against manifest of collections)
BRANCH_RECONCILED
  ↓ (finance verifies branch deposit against bank/ledger)
FINANCE_VERIFIED
  ↓ (settlement batch generated for merchant, on a schedule or on demand)
MERCHANT_SETTLEMENT_CREATED
  ↓ (payout executed to merchant)
SETTLED
```

Each transition is owned by a specific entity, not a status field flip on `Shipment`:

| Transition | Entity that records it |
|---|---|
| COD_EXPECTED → COD_COLLECTED | `CODCollection` (created at delivery, linked to `DeliveryProof`) |
| COD_COLLECTED → RIDER_DEPOSITED | `RiderCashSettlement` (rider's end-of-run cash handover) |
| RIDER_DEPOSITED → BRANCH_RECONCILED | `BranchCashSettlement` |
| BRANCH_RECONCILED → FINANCE_VERIFIED | `FinancialTransaction` (finance verification entry) |
| FINANCE_VERIFIED → MERCHANT_SETTLEMENT_CREATED | `MerchantSettlement` + `MerchantSettlementItem` rows |
| MERCHANT_SETTLEMENT_CREATED → SETTLED | Payout confirmation on `MerchantSettlement` |

`Shipment.CODAmount` is set at booking time from pricing and never changes; the COD
*lifecycle* status above lives on `CODCollection` (one row per shipment with COD),
not on `Shipment` itself — `Shipment` is not re-purposed as the ledger.

## 2. Worked Example (from CLAUDE.md §22)

```text
COD Amount           = NPR 2,500
Courier Delivery Fee  = NPR 150
─────────────────────────────────
Merchant Payable      = NPR 2,350
```

`MerchantSettlementItem` stores, per shipment: `CODAmount, CourierFee,
OtherDeductions, NetPayable` — every figure traceable back to the `Shipment`'s locked
booking-time pricing and the specific `CODCollection` row, not recomputed from
current rates.

## 3. Reconciliation Visibility (CLAUDE.md §23)

At any point the system must be able to compute, per branch/rider/merchant/date range:

* Expected COD (`Σ Shipment.CODAmount` for COD-enabled shipments not yet delivered)
* Collected COD (`Σ CODCollection` where collected)
* Rider-deposited COD (`Σ RiderCashSettlement`)
* Branch-received COD (`Σ BranchCashSettlement`)
* Merchant-payable COD (`Σ MerchantSettlementItem.NetPayable`)
* Courier fees, adjustments, refunds, shortages, overages

Shortages/overages surface as the delta between `RiderCashSettlement.DeclaredAmount`
and `BranchCashSettlement.ConfirmedAmount` (and similarly at the finance-verification
step) — they are visible discrepancies to resolve via the adjustment process below,
never auto-corrected silently.

## 4. Adjustments (CLAUDE.md §23)

No financial figure is ever overwritten in place. Every adjustment is its own
auditable record:

```text
Adjustment {
  EntityType, EntityId,      # what's being adjusted (e.g. MerchantSettlementItem)
  BeforeValue, AfterValue,
  Reason,
  RequestedByUserId, RequestedAt,
  ApprovedByUserId, ApprovedAt   # required for adjustments above a configurable threshold
}
```

`cod.adjust` (see `RBAC.md`) is a distinct permission from `cod.collect`/
`cod.reconcile`/`cod.settle` — front-line staff can reconcile; only FinanceManager
(or above) can adjust, and adjustments write both an `Adjustment` row and a standard
`AuditLog` entry (CLAUDE.md §28).

## 5. Failure/Return Interaction

If a shipment enters RTO (`SHIPMENT-LIFECYCLE.md`), any `CODCollection` row for it
must be `COD_EXPECTED` still (COD is never collected on a failed/returned delivery) —
the RTO path and the COD path are independent state machines that only intersect in
that RTO implies COD will never reach `COD_COLLECTED` for that shipment.
