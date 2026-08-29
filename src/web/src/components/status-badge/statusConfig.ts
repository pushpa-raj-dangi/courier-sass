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
  CREATED: { label: "Created", token: "status-created" },
  PICKUP_ASSIGNED: { label: "Pickup Assigned", token: "status-created" },
  PICKED_UP: { label: "Picked Up", token: "status-in-transit" },
  AT_ORIGIN_BRANCH: { label: "At Origin Branch", token: "status-in-transit" },
  SORTED: { label: "Sorted", token: "status-in-transit" },
  IN_TRANSIT: { label: "In Transit", token: "status-in-transit" },
  AT_DESTINATION_BRANCH: { label: "At Destination Branch", token: "status-in-transit" },
  OUT_FOR_DELIVERY: { label: "Out for Delivery", token: "status-out-for-delivery" },
  DELIVERED: { label: "Delivered", token: "status-delivered" },
  DELIVERY_FAILED: { label: "Delivery Failed", token: "status-delivery-failed" },
  NDR_CREATED: { label: "NDR", token: "status-ndr" },
  REATTEMPT: { label: "Reattempt", token: "status-out-for-delivery" },
  RTO_INITIATED: { label: "RTO Initiated", token: "status-rto" },
  RTO_IN_TRANSIT: { label: "RTO In Transit", token: "status-rto" },
  RTO_AT_ORIGIN: { label: "RTO At Origin", token: "status-rto" },
  RTO_DELIVERED: { label: "RTO Delivered", token: "status-rto" },
  CANCELLED: { label: "Cancelled", token: "status-cancelled" },
};

export function resolveStatusConfig(status: string): StatusConfigEntry {
  return STATUS_CONFIG[status] ?? { label: status, token: "status-cancelled" };
}
