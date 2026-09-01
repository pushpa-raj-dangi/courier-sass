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
};

export function resolveStatusConfig(status: string, namespace?: string): StatusConfigEntry {
  const key = namespace ? `${namespace}.${status}` : status;
  return STATUS_CONFIG[key] ?? { label: status, token: "status-cancelled" };
}
