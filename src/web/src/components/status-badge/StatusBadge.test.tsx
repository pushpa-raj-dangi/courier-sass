import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { StatusBadge } from "./StatusBadge";

describe("StatusBadge", () => {
  it("renders the mapped label for a real backend status (PascalCase wire format)", () => {
    render(<StatusBadge status="InTransit" />);
    expect(screen.getByText("In Transit")).toBeInTheDocument();
  });

  it("renders the mapped label for every real ShipmentStatus enum member", () => {
    const cases: Array<[string, string]> = [
      ["Created", "Created"],
      ["PickupAssigned", "Created"],
      ["PickedUp", "Picked Up"],
      ["AtOriginBranch", "In Transit"],
      ["Sorted", "In Transit"],
      ["AtDestinationBranch", "In Transit"],
      ["OutForDelivery", "Out for Delivery"],
      ["Delivered", "Delivered"],
      ["DeliveryFailed", "NDR"],
      ["NdrCreated", "NDR"],
      ["RtoInitiated", "RTO Initiated"],
      ["RtoInTransit", "RTO In Transit"],
      ["RtoAtOrigin", "RTO At Origin"],
      ["RtoDelivered", "RTO Delivered"],
      ["Cancelled", "Cancelled"],
    ];
    for (const [status, label] of cases) {
      const { unmount } = render(<StatusBadge status={status} />);
      expect(screen.getByText(label)).toBeInTheDocument();
      unmount();
    }
  });

  it("falls back to the raw status string for a genuinely unknown status", () => {
    render(<StatusBadge status="SomeFutureStatus" />);
    expect(screen.getByText("SomeFutureStatus")).toBeInTheDocument();
  });
});
