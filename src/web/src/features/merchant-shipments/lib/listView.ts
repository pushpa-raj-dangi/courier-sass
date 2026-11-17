import { plural } from "@/features/merchant-dashboard/lib/dashboardLogic";

/** "{all} shipments booked · {onTheWay} on the way"; "—" for a count that failed to load. */
export function shipmentsDescription(all: number | null, onTheWay: number | null): string {
  const booked = all === null ? "— shipments" : plural(all, "shipment");
  return `${booked} booked · ${onTheWay ?? "—"} on the way`;
}
