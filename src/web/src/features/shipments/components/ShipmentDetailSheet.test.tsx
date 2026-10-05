import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, within } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";

vi.mock("@/features/shipments/store/shipmentPanelStore", () => ({
  useShipmentPanelStore: (selector: (s: object) => unknown) => selector({ panel: { mode: "detail", awb: "QA-1" }, close: vi.fn() }),
}));
const useShipment = vi.fn();
vi.mock("@/features/shipments/hooks/useShipment", () => ({ useShipment: () => useShipment() }));
const useShipmentEvents = vi.fn();
vi.mock("@/features/shipments/hooks/useShipmentEvents", () => ({ useShipmentEvents: () => useShipmentEvents() }));
vi.mock("@/features/shipments/hooks/useMerchants", () => ({ useMerchants: () => ({ data: [{ id: "m1", displayName: "Everest Organics" }] }) }));
vi.mock("@/features/shipments/hooks/useServiceTypes", () => ({ useServiceTypes: () => ({ data: [{ id: "s1", name: "Express" }] }) }));
vi.mock("@/features/shipments/hooks/useReceiveAtOrigin", () => ({ useReceiveAtOrigin: () => ({ mutate: vi.fn(), isPending: false }) }));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => p === "shipment.scan" }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({
    branchName: (id: string | null) => (id ? { b1: "Kathmandu Hub", b2: "Lalitpur Branch" }[id] ?? "Unknown branch" : "—"),
    userName: (id: string | null) => (id ? "Staff" : "—"),
    riderName: (id: string | null) => (id ? "Rider" : "—"),
  }),
}));
vi.mock("@/features/shipments/components/ShipmentNdrRtoLinks", () => ({ ShipmentNdrRtoLinks: () => <div>ndr-rto-links</div> }));

import { ShipmentDetailSheet } from "./ShipmentDetailSheet";

const shipment = {
  id: "x1", awb: "QA-1", merchantId: "m1", originBranchId: "b1", destinationBranchId: "b2", currentBranchId: "b2",
  currentCustodianUserId: null, currentCustodianRiderId: "r1", currentStatus: "OutForDelivery", serviceTypeId: "s1",
  weightKg: 1.2, lengthCm: null, widthCm: null, heightCm: null, declaredValue: 3000, codAmount: 2500, wantsInsurance: false,
  deliveryCharge: 150, insuranceCharge: 0, otherCharges: 0, totalCharge: 150, recipientName: "Sita Shrestha",
  recipientPhone: "9841", recipientAddressLine: "Jawalakhel-4", senderName: "Everest", senderPhone: "01", senderAddressLine: "Thamel",
  items: [{ id: "i1", description: "Tea", quantity: 2, unitValue: 500 }], createdAt: "2026-10-05T05:00:00Z", lastEventAt: "2026-10-05T08:00:00Z",
};
const events = [
  { id: "e1", eventType: "Created", previousStatus: null, newStatus: "Created", branchId: "b1", userId: "u1", riderId: null, timestamp: "2026-10-05T05:00:00Z", notes: null },
  { id: "e2", eventType: "OutForDelivery", previousStatus: "AtDestinationBranch", newStatus: "OutForDelivery", branchId: "b2", userId: "u2", riderId: "r1", timestamp: "2026-10-05T08:00:00Z", notes: "run 7" },
];

describe("ShipmentDetailSheet", () => {
  beforeEach(() => {
    useShipment.mockReturnValue({ data: shipment, isLoading: false, isError: false, refetch: vi.fn() });
    useShipmentEvents.mockReturnValue({ data: events, isLoading: false, isError: false, refetch: vi.fn() });
  });

  it("renders header facts, the right-now card and the stepper", () => {
    renderWithIntl(<ShipmentDetailSheet />);
    expect(screen.getByText("AWB · EXPRESS · COD")).toBeInTheDocument();
    const card = screen.getByRole("region", { name: "Current custody" });
    expect(within(card).getByText("Rider")).toBeInTheDocument();
    // Owning branch and destination are both Lalitpur for an out-for-delivery parcel.
    expect(within(card).getAllByText("Lalitpur Branch")).toHaveLength(2);
    const current = screen.getByRole("list", { name: "Progress" }).querySelector("[aria-current='step']");
    expect(current).toHaveTextContent("Out for delivery");
  });

  it("shows the timeline newest first with notes", () => {
    renderWithIntl(<ShipmentDetailSheet />);
    const items = within(screen.getByRole("list", { name: "Timeline" })).getAllByRole("listitem");
    expect(items[0]).toHaveTextContent("Out for delivery");
    expect(items[0]).toHaveTextContent("run 7");
    expect(items[1]).toHaveTextContent("Booked");
  });

  it("renders COD, Attempts and Audit as needs-backend slots", () => {
    renderWithIntl(<ShipmentDetailSheet />);
    for (const name of ["COD", "Attempts", "Audit"]) {
      fireEvent.mouseDown(screen.getByRole("tab", { name }));
      expect(screen.getAllByText("Needs backend").length).toBeGreaterThan(0);
    }
  });

  it("isolates an events error to the timeline", () => {
    useShipmentEvents.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    renderWithIntl(<ShipmentDetailSheet />);
    expect(screen.getByText("Couldn't load the history")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Current custody" })).toBeInTheDocument();
  });

  it("explains a 404 as not found or out of scope", () => {
    useShipment.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    renderWithIntl(<ShipmentDetailSheet />);
    expect(screen.getByText("Shipment not found or not in your branches")).toBeInTheDocument();
  });

  it("offers receive-at-origin only for picked-up shipments", () => {
    useShipment.mockReturnValue({ data: { ...shipment, currentStatus: "PickedUp" }, isLoading: false, isError: false, refetch: vi.fn() });
    renderWithIntl(<ShipmentDetailSheet />);
    expect(screen.getByRole("button", { name: "Receive at origin" })).toBeInTheDocument();
    expect(screen.getByText("Status changes only through scans and rider actions")).toBeInTheDocument();
  });
});
