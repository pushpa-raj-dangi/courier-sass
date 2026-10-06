// Mirrors backend ShipmentStatus (src/Modules/Shipments/Domain/ShipmentStatus.cs) exactly.
export const ALL_SHIPMENT_STATUSES = [
  "Created", "PickupAssigned", "PickedUp", "AtOriginBranch", "Sorted", "InTransit", "AtDestinationBranch",
  "OutForDelivery", "DeliveryFailed", "Delivered", "Cancelled", "NdrCreated", "RtoInitiated", "RtoInTransit",
  "RtoAtOrigin", "RtoDelivered", "CustomsPrepared", "HandedToCarrier", "InInternationalTransit",
  "DeliveredAbroad", "CustomsHold", "InternationalException", "CustomsClearance", "ReceivedFromCarrier",
] as const;

export type ShipmentTab = "all" | "at-branch" | "in-transit" | "out-for-delivery" | "ndr" | "rto";

// Single source for the Shipments tabs and the dashboard pipeline links. Delivered, Cancelled
// and international statuses are intentionally in no tab — they show under "All" only.
export const SHIPMENT_TABS: { id: ShipmentTab; label: string; statuses: string[] }[] = [
  { id: "all", label: "All", statuses: [] },
  {
    id: "at-branch",
    label: "At branch",
    statuses: ["Created", "PickupAssigned", "PickedUp", "AtOriginBranch", "Sorted", "AtDestinationBranch"],
  },
  { id: "in-transit", label: "In transit", statuses: ["InTransit"] },
  { id: "out-for-delivery", label: "Out for delivery", statuses: ["OutForDelivery"] },
  { id: "ndr", label: "NDR", statuses: ["DeliveryFailed", "NdrCreated"] },
  { id: "rto", label: "RTO", statuses: ["RtoInitiated", "RtoInTransit", "RtoAtOrigin", "RtoDelivered"] },
];

export function isShipmentTab(value: string | null): value is ShipmentTab {
  return value !== null && SHIPMENT_TABS.some((t) => t.id === value);
}

export function tabStatuses(tab: ShipmentTab): string[] {
  return SHIPMENT_TABS.find((t) => t.id === tab)?.statuses ?? [];
}

export function sumStatuses(counts: Record<string, number> | undefined, statuses: readonly string[]): number {
  if (!counts) return 0;
  return statuses.reduce((total, status) => total + (counts[status] ?? 0), 0);
}
