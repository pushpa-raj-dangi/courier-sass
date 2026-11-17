// Single source of truth for shipment/NDR/RTO status -> display label + color token
// (SHIPMENT-LIFECYCLE.md's state list). `token` names a CSS custom property pair defined in
// globals.css (--{token} and --{token}-foreground) — StatusBadge resolves it via inline style
// referencing var(--{token}) rather than a Tailwind class, since Tailwind's JIT scanner cannot
// see a dynamically interpolated class name (`bg-${token}`) at build time.
export interface StatusConfigEntry {
  label: string;
  token: string;
}

export const STATUS_CONFIG: Record<string, StatusConfigEntry> = {
  Created: { label: "Created", token: "status-created" },
  PickupAssigned: { label: "Created", token: "status-created" },
  PickedUp: { label: "Picked Up", token: "status-in-transit" },
  AtOriginBranch: { label: "In Transit", token: "status-in-transit" },
  Sorted: { label: "In Transit", token: "status-in-transit" },
  InTransit: { label: "In Transit", token: "status-in-transit" },
  AtDestinationBranch: { label: "In Transit", token: "status-in-transit" },
  OutForDelivery: { label: "Out for Delivery", token: "status-out-for-delivery" },
  Delivered: { label: "Delivered", token: "status-delivered" },
  // Shares NDR's token/label, not its own — the backend auto-transitions
  // DELIVERY_FAILED -> NDR_CREATED in the same request (Phase 9), so a user
  // essentially never sees this rest as a distinct status. Also fixes a real CVD
  // defect: the two previously had separate lightness steps of the same vermillion
  // hue, which failed dataviz's normal-vision adjacent-pair floor (ΔE 7.2 < 15).
  DeliveryFailed: { label: "NDR", token: "status-ndr" },
  NdrCreated: { label: "NDR", token: "status-ndr" },
  RtoInitiated: { label: "RTO Initiated", token: "status-rto" },
  RtoInTransit: { label: "RTO In Transit", token: "status-rto" },
  RtoAtOrigin: { label: "RTO At Origin", token: "status-rto" },
  RtoDelivered: { label: "RTO Delivered", token: "status-rto" },
  Cancelled: { label: "Cancelled", token: "status-cancelled" },
  // Manifest statuses — namespaced ("Manifest.X") since ManifestStatus and ShipmentStatus both
  // have an "InTransit" member; without namespacing, a flat key would let one silently overwrite
  // the other in this Record. Draft/Closed deliberately reuse an existing shipment-status token
  // (same real-world meaning: "just created, nothing actioned yet" / "done") rather than minting
  // new colors for concepts this palette already has a color for.
  "Manifest.Draft": { label: "Draft", token: "status-created" },
  "Manifest.Sealed": { label: "Sealed", token: "status-sealed" },
  "Manifest.InTransit": { label: "In Transit", token: "status-in-transit" },
  "Manifest.Received": { label: "Received", token: "status-received" },
  "Manifest.Closed": { label: "Closed", token: "status-delivered" },

  // NDR statuses — namespaced ("Ndr.X") for the same reason Manifest.X is: a flat key risks
  // collision with ShipmentStatus/ManifestStatus members. All 6 reuse existing tokens rather than
  // minting new colors. Validated as a group via the dataviz skill's palette validator (Frontend
  // Phase F6): passes the normal-vision adjacent-pair floor (worst pair ΔE 15.3-15.6), but
  // Resolved (status-delivered) vs RtoRequested (status-rto) fails CVD-specific separation
  // (ΔE 4.8 deutan light / 2.6 protan dark, both well under the 6 floor) — a pre-existing weakness
  // of the low-chroma --status-rto token itself (the same pair already coexists unnamespaced in
  // ShipmentStatus's own Delivered/Rto* badges today), not something this new grouping creates.
  // Every alternative substitution tried during validation either reintroduced a normal-vision
  // failure or a worse CVD failure, so this keeps the semantically strongest mapping rather than a
  // worse-fitting one — same "known debt, not blocking, every status ships with a visible label"
  // call CLARIFICATIONS.md #14/#15 already made for two other pre-existing pairs.
  "Ndr.Open": { label: "Open", token: "status-ndr" },
  "Ndr.Contacting": { label: "Contacting", token: "status-out-for-delivery" },
  "Ndr.CustomerConfirmed": { label: "Customer Confirmed", token: "status-received" },
  "Ndr.ReattemptScheduled": { label: "Reattempt Scheduled", token: "status-sealed" },
  "Ndr.RtoRequested": { label: "RTO Requested", token: "status-rto" },
  "Ndr.Resolved": { label: "Resolved", token: "status-delivered" },

  // Cod collection statuses (COD-LIFECYCLE.md §1's full seven-state chain) — namespaced ("Cod.X")
  // for the same reason "Ndr.X"/"Manifest.X" are: a flat key risks collision, here specifically
  // between the two Settled members below and CodCollectionStatus's own Settled. This is the
  // longest status chain of any namespace so far (7 states vs. Ndr's 6). Validated as its own
  // adjacent-pair group via the dataviz skill's palette validator (Frontend Phase F7).
  //
  // Validation found a genuine pre-existing bug, not just a close call: --status-in-transit and
  // --status-out-for-delivery are byte-identical in dark mode (both 38.0 78.9% 57.3% — ΔE 0.0,
  // ordinal not just close), unlike light mode where they're clearly distinct (35.3° vs 38.0° hue).
  // This chain deliberately avoids --status-in-transit entirely (rather than accepting the
  // known-debt pattern used for the Ndr.Resolved/Ndr.RtoRequested pair above) since an exact
  // duplicate isn't a marginal floor miss to document — it's unusable regardless of labeling.
  // Parked, not fixed here: the underlying globals.css duplicate also affects every ShipmentStatus
  // badge using these two tokens (PickedUp/AtOriginBranch/Sorted/AtDestinationBranch vs.
  // OutForDelivery) and re-deriving a correct third amber shade for dark mode is a properly scoped
  // design task of its own, not a one-line fix — see CLARIFICATIONS.md entry #17.
  //
  // A brute-force search over the 7 candidate tokens' 120 possible orderings (script run, not
  // eyeballed) found this exact arrangement passes both the normal-vision ΔE>=15 floor and CVD
  // separation for every adjacent pair, in both light and dark — a clean pass, not documented debt.
  "Cod.CodExpected": { label: "Expected", token: "status-created" },
  "Cod.CodCollected": { label: "Collected", token: "status-out-for-delivery" },
  "Cod.RiderDeposited": { label: "Rider Deposited", token: "status-sealed" },
  "Cod.BranchReconciled": { label: "Branch Reconciled", token: "status-received" },
  "Cod.FinanceVerified": { label: "Finance Verified", token: "status-rto" },
  "Cod.MerchantSettlementCreated": { label: "Settlement Created", token: "status-ndr" },
  "Cod.Settled": { label: "Settled", token: "status-delivered" },

  // MerchantSettlementStatus — its own namespace (not "Cod.X") because its Settled member would
  // otherwise collide with CodCollectionStatus.Settled's key immediately above under one flat
  // Record.
  "MerchantSettlement.Created": { label: "Awaiting payout", token: "status-created" },
  "MerchantSettlement.Settled": { label: "Settled", token: "status-delivered" },

  // PickupRequestStatus — namespaced ("PickupRequest.X") for the same collision-avoidance
  // reason every other namespace here is (a flat key risks colliding with another enum's same
  // member name; PickupRequestStatus has no direct name clash today, but every multi-enum
  // status set in this file is namespaced on principle, not only when a clash is proven).
  "PickupRequest.Requested": { label: "Requested", token: "status-created" },
  "PickupRequest.Scheduled": { label: "Scheduled", token: "status-sealed" },
  "PickupRequest.Completed": { label: "Completed", token: "status-delivered" },
  "PickupRequest.Cancelled": { label: "Cancelled", token: "status-cancelled" },

  // KycStatus — first UI anywhere to render this enum (Frontend Phase F10). Reuses existing
  // tokens: Pending reads as "just submitted, nothing decided yet" (same as Created), Approved
  // as a positive terminal state (same as Delivered), Rejected as a negative terminal state
  // (same as Cancelled).
  "Kyc.Pending": { label: "Pending", token: "status-created" },
  "Kyc.Approved": { label: "Approved", token: "status-delivered" },
  "Kyc.Rejected": { label: "Rejected", token: "status-cancelled" },

  // ShipmentStatus in merchant vocabulary (R5b-1) — namespaced "Merchant.X" because the merchant
  // portal collapses internal branch/sort steps and renames returns ("Returning to you").
  "Merchant.Created": { label: "Booked", token: "status-created" },
  "Merchant.PickupAssigned": { label: "Booked", token: "status-created" },
  "Merchant.PickedUp": { label: "Picked up", token: "status-in-transit" },
  "Merchant.AtOriginBranch": { label: "In transit", token: "status-in-transit" },
  "Merchant.Sorted": { label: "In transit", token: "status-in-transit" },
  "Merchant.InTransit": { label: "In transit", token: "status-in-transit" },
  "Merchant.CustomsPrepared": { label: "In transit", token: "status-in-transit" },
  "Merchant.HandedToCarrier": { label: "In transit", token: "status-in-transit" },
  "Merchant.InInternationalTransit": { label: "In transit", token: "status-in-transit" },
  "Merchant.CustomsHold": { label: "In transit", token: "status-in-transit" },
  "Merchant.CustomsClearance": { label: "In transit", token: "status-in-transit" },
  "Merchant.ReceivedFromCarrier": { label: "In transit", token: "status-in-transit" },
  "Merchant.AtDestinationBranch": { label: "At destination branch", token: "status-in-transit" },
  "Merchant.OutForDelivery": { label: "Out for delivery", token: "status-out-for-delivery" },
  "Merchant.DeliveryFailed": { label: "Delivery failed", token: "status-ndr" },
  "Merchant.NdrCreated": { label: "Delivery failed", token: "status-ndr" },
  "Merchant.InternationalException": { label: "Delivery failed", token: "status-ndr" },
  "Merchant.RtoInitiated": { label: "Returning to you", token: "status-rto" },
  "Merchant.RtoInTransit": { label: "Returning to you", token: "status-rto" },
  "Merchant.RtoAtOrigin": { label: "Returning to you", token: "status-rto" },
  "Merchant.RtoDelivered": { label: "Returned to you", token: "status-rto" },
  "Merchant.Delivered": { label: "Delivered", token: "status-delivered" },
  "Merchant.DeliveredAbroad": { label: "Delivered", token: "status-delivered" },
  "Merchant.Cancelled": { label: "Cancelled", token: "status-cancelled" },

  // PublicTrackingStatus — the anonymous /track/{awb} surface (Frontend Phase F11). Namespaced
  // ("Public.X") on the same principle as every other multi-enum status set in this file, even
  // though most member names would otherwise collide outright with ShipmentStatus's own keys
  // above (e.g. "PickedUp", "AtDestinationBranch") and would silently inherit that internal-
  // facing enum's mapping instead of the public-facing one intended here.
  "Public.Received": { label: "Received", token: "status-created" },
  "Public.PickedUp": { label: "Picked Up", token: "status-in-transit" },
  "Public.InTransit": { label: "In Transit", token: "status-in-transit" },
  "Public.AtDestinationBranch": { label: "At Destination Hub", token: "status-in-transit" },
  "Public.OutForDelivery": { label: "Out for Delivery", token: "status-out-for-delivery" },
  "Public.Delivered": { label: "Delivered", token: "status-delivered" },
  "Public.DeliveryAttemptUnsuccessful": { label: "Delivery Attempt Unsuccessful", token: "status-ndr" },
  "Public.Cancelled": { label: "Cancelled", token: "status-cancelled" },
  "Public.ReturningToSender": { label: "Returning to Sender", token: "status-rto" },
};

export function resolveStatusConfig(status: string, namespace?: string): StatusConfigEntry {
  const key = namespace ? `${namespace}.${status}` : status;
  return STATUS_CONFIG[key] ?? { label: status, token: "status-cancelled" };
}
