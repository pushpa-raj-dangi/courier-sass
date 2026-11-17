export type MerchantStatusGroup =
  | "booked"
  | "inTransit"
  | "outForDelivery"
  | "needsAttention"
  | "returning"
  | "delivered";

// Merchant-vocabulary grouping of the internal ShipmentStatus names (R5b-1 spec, "Status groups").
// Cancelled is in no group; it shows under "All" only.
export const MERCHANT_STATUS_GROUPS: Record<MerchantStatusGroup, { label: string; statuses: readonly string[] }> = {
  booked: { label: "Booked", statuses: ["Created", "PickupAssigned"] },
  inTransit: {
    label: "In transit",
    statuses: [
      "PickedUp", "AtOriginBranch", "Sorted", "InTransit", "AtDestinationBranch", "CustomsPrepared",
      "HandedToCarrier", "InInternationalTransit", "CustomsHold", "CustomsClearance", "ReceivedFromCarrier",
    ],
  },
  outForDelivery: { label: "Out for delivery", statuses: ["OutForDelivery"] },
  needsAttention: { label: "Needs attention", statuses: ["DeliveryFailed", "NdrCreated", "InternationalException"] },
  returning: { label: "Returning", statuses: ["RtoInitiated", "RtoInTransit", "RtoAtOrigin", "RtoDelivered"] },
  delivered: { label: "Delivered", statuses: ["Delivered", "DeliveredAbroad"] },
};

export type MerchantShipmentTab = "all" | MerchantStatusGroup;

export const MERCHANT_SHIPMENT_TABS: readonly MerchantShipmentTab[] = [
  "all", "booked", "inTransit", "outForDelivery", "needsAttention", "returning", "delivered",
];

export function isMerchantShipmentTab(value: string | null): value is MerchantShipmentTab {
  return value !== null && (MERCHANT_SHIPMENT_TABS as readonly string[]).includes(value);
}

export function tabStatuses(tab: MerchantShipmentTab): readonly string[] {
  return tab === "all" ? [] : MERCHANT_STATUS_GROUPS[tab].statuses;
}

export function groupOf(status: string): MerchantStatusGroup | null {
  for (const [group, def] of Object.entries(MERCHANT_STATUS_GROUPS)) {
    if (def.statuses.includes(status)) return group as MerchantStatusGroup;
  }
  return null;
}
