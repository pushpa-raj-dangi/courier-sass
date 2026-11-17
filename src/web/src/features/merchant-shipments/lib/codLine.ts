import type { MerchantShipmentCodStatus } from "@/features/merchant-cod/types";

const dayMonth = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short" });
const dayMonthYear = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric" });

/** One-line COD settlement state for the "Where it is now" block of the shipment detail Sheet. */
export function codLine(status: MerchantShipmentCodStatus | undefined, codAmount: number): string {
  if (codAmount === 0) return "Prepaid";
  if (!status) return "—";
  switch (status.state) {
    case "Prepaid":
      return "Prepaid";
    case "NotCollected":
      return "Not yet collected";
    case "CollectedAwaitingSettlement":
      return "Collected, awaiting settlement";
    case "InSettlement":
      return status.periodFrom && status.periodTo
        ? `In settlement (${dayMonth.format(new Date(status.periodFrom))}–${dayMonth.format(new Date(status.periodTo))})`
        : "In settlement";
    case "Settled":
      return status.settledAt ? `Settled on ${dayMonthYear.format(new Date(status.settledAt))}` : "Settled";
  }
}
