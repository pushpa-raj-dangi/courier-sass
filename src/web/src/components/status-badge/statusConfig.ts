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
  DeliveryFailed: { label: "Delivery Failed", token: "status-delivery-failed" },
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
