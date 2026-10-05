import { describe, expect, it } from "vitest";
import { ALL_SHIPMENT_STATUSES, SHIPMENT_TABS, isShipmentTab, sumStatuses, tabStatuses } from "./statusGroups";

describe("statusGroups", () => {
  it("never puts a status in two tabs", () => {
    const seen = new Map<string, string>();
    for (const tab of SHIPMENT_TABS) {
      for (const status of tab.statuses) {
        expect(seen.get(status), `${status} in ${tab.id} and ${seen.get(status)}`).toBeUndefined();
        seen.set(status, tab.id);
      }
    }
  });

  it("leaves exactly Delivered, Cancelled and the international statuses to 'All'", () => {
    const grouped = new Set(SHIPMENT_TABS.flatMap((t) => t.statuses));
    const ungrouped = ALL_SHIPMENT_STATUSES.filter((s) => !grouped.has(s)).sort();
    expect(ungrouped).toEqual(
      [
        "Cancelled", "CustomsClearance", "CustomsHold", "CustomsPrepared", "Delivered", "DeliveredAbroad",
        "HandedToCarrier", "InInternationalTransit", "InternationalException", "ReceivedFromCarrier",
      ].sort()
    );
  });

  it("'all' has no status filter", () => {
    expect(tabStatuses("all")).toEqual([]);
  });

  it("sums counts for a group, treating missing keys as 0", () => {
    expect(sumStatuses({ DeliveryFailed: 2, NdrCreated: 3, Delivered: 9 }, tabStatuses("ndr"))).toBe(5);
    expect(sumStatuses(undefined, tabStatuses("ndr"))).toBe(0);
  });

  it("recognises tab ids", () => {
    expect(isShipmentTab("rto")).toBe(true);
    expect(isShipmentTab("bogus")).toBe(false);
    expect(isShipmentTab(null)).toBe(false);
  });
});
