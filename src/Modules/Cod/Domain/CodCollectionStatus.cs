namespace Courier.Cod.Domain;

// COD-LIFECYCLE.md §1's full seven-state chain, all living on CODCollection.Status — this plan's
// resolved reading of COD-LIFECYCLE.md's prose ("the COD lifecycle status... lives on
// CODCollection, not on Shipment"), not the narrower Expected/Collected pair the entity's
// field-list table alone shows. Strictly forward-only; no step in this plan ever moves a
// CODCollection backward.
public enum CodCollectionStatus
{
    CodExpected,
    CodCollected,
    RiderDeposited,
    BranchReconciled,
    FinanceVerified,
    MerchantSettlementCreated,
    Settled
}
