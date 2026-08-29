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
};

export function resolveStatusConfig(status: string): StatusConfigEntry {
  return STATUS_CONFIG[status] ?? { label: status, token: "status-cancelled" };
}
