import { describe, expect, it } from "vitest";
import { ALL_SHIPMENT_STATUSES } from "@/features/shipments/lib/statusGroups";
import { ACTIVE_RETURN_STATUSES, MERCHANT_STATUS_GROUPS, MERCHANT_SHIPMENT_TABS, groupOf } from "./statusGroups";

describe("merchant status groups", () => {
  it("maps every status except Cancelled to exactly one group", () => {
    for (const status of ALL_SHIPMENT_STATUSES) {
      const owners = Object.values(MERCHANT_STATUS_GROUPS).filter((g) => g.statuses.includes(status));
      expect(owners.length, status).toBe(status === "Cancelled" ? 0 : 1);
    }
  });

  it("resolves groupOf", () => {
    expect(groupOf("RtoDelivered")).toBe("returning");
    expect(groupOf("Cancelled")).toBeNull();
    expect(groupOf("Bogus")).toBeNull();
  });

  it("keeps RtoDelivered in the Returning tab but not in active returns", () => {
    expect(MERCHANT_STATUS_GROUPS.returning.statuses).toContain("RtoDelivered");
    expect(ACTIVE_RETURN_STATUSES).toEqual(["RtoInitiated", "RtoInTransit", "RtoAtOrigin"]);
  });

  it("lists tabs in order", () => {
    expect(MERCHANT_SHIPMENT_TABS).toEqual([
      "all", "booked", "inTransit", "outForDelivery", "needsAttention", "returning", "delivered",
    ]);
  });
});
