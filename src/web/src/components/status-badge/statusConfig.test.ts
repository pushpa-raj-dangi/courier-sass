import { describe, expect, it } from "vitest";
import { ALL_SHIPMENT_STATUSES } from "@/features/shipments/lib/statusGroups";
import { resolveStatusConfig, STATUS_CONFIG } from "./statusConfig";

describe("resolveStatusConfig — Public namespace", () => {
  it("resolves PublicTrackingStatus members to their own label, not ShipmentStatus's", () => {
    expect(resolveStatusConfig("AtDestinationBranch", "Public").label).toBe("At Destination Hub");
    expect(resolveStatusConfig("AtDestinationBranch").label).toBe("In Transit");
  });

  it("resolves every PublicTrackingStatus member to its own config entry, not the generic fallback", () => {
    const expectedLabels: Record<string, string> = {
      Received: "Received",
      PickedUp: "Picked Up",
      InTransit: "In Transit",
      AtDestinationBranch: "At Destination Hub",
      OutForDelivery: "Out for Delivery",
      Delivered: "Delivered",
      DeliveryAttemptUnsuccessful: "Delivery Attempt Unsuccessful",
      Cancelled: "Cancelled",
      ReturningToSender: "Returning to Sender",
    };
    for (const [member, expectedLabel] of Object.entries(expectedLabels)) {
      const config = resolveStatusConfig(member, "Public");
      expect(config.label).toBe(expectedLabel);
    }
  });
});

describe("resolveStatusConfig — Merchant namespace coverage", () => {
  it("has an explicit Merchant. entry for every ShipmentStatus", () => {
    for (const name of ALL_SHIPMENT_STATUSES) {
      expect(STATUS_CONFIG[`Merchant.${name}`], name).toBeDefined();
    }
  });

  it("uses merchant vocabulary for returns", () => {
    expect(resolveStatusConfig("RtoDelivered", "Merchant").label).toBe("Returned to you");
    expect(resolveStatusConfig("RtoInTransit", "Merchant").label).toBe("Returning to you");
  });

  it("spot-checks labels and tokens", () => {
    expect(resolveStatusConfig("Created", "Merchant")).toEqual({ label: "Booked", token: "status-created" });
    expect(resolveStatusConfig("InternationalException", "Merchant")).toEqual({ label: "Delivery failed", token: "status-ndr" });
    expect(resolveStatusConfig("DeliveredAbroad", "Merchant")).toEqual({ label: "Delivered", token: "status-delivered" });
  });
});
