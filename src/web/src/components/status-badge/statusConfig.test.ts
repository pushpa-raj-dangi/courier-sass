import { describe, expect, it } from "vitest";
import { resolveStatusConfig } from "./statusConfig";

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
