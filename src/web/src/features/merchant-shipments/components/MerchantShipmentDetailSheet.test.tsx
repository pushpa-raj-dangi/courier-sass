import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";

let shipment: Record<string, unknown>;
let canCancel = true;
let trackState: { data?: unknown; isError: boolean; isLoading: boolean; refetch: () => void };
const mutateAsync = vi.fn();

vi.mock("@/features/merchant-shipments/store/merchantShipmentPanelStore", () => ({
  useMerchantShipmentPanelStore: (sel: (s: unknown) => unknown) =>
    sel({ panel: { mode: "detail", awb: "QA-1" }, close: vi.fn() }),
}));
vi.mock("@/features/merchant-shipments/hooks/useMerchantShipment", () => ({
  useMerchantShipment: () => ({ data: shipment, isLoading: false, isError: false, refetch: vi.fn() }),
}));
vi.mock("@/features/merchant-shipments/hooks/useMerchantBranches", () => ({
  useMerchantBranches: () => ({
    data: [
      { id: "b2", code: "LAL", name: "Lalitpur" },
      { id: "b1", code: "KTM", name: "Kathmandu" },
    ],
  }),
}));
vi.mock("@/features/merchant-shipments/hooks/useMerchantServiceTypes", () => ({
  useMerchantServiceTypes: () => ({ data: [{ id: "s1", code: "Express", name: "Express" }] }),
}));
vi.mock("@/features/merchant-shipments/hooks/useCancelMerchantShipment", () => ({
  useCancelMerchantShipment: () => ({ mutateAsync, isPending: false }),
}));
vi.mock("@/features/merchant-auth/hooks/useMerchantPermission", () => ({ useMerchantPermission: () => canCancel }));
vi.mock("@/features/merchant-cod/hooks/useMerchantShipmentCodStatus", () => ({
  useMerchantShipmentCodStatus: () => ({
    data: { state: "NotCollected", settlementId: null, periodFrom: null, periodTo: null, settledAt: null },
  }),
}));
vi.mock("@/features/tracking/hooks/useTrackShipment", () => ({
  useTrackShipment: () => trackState,
}));

import { MerchantShipmentDetailSheet } from "./MerchantShipmentDetailSheet";

const base = {
  id: "x1", awb: "QA-1", merchantId: "m1", originBranchId: "b1", destinationBranchId: "b2", currentBranchId: "b2",
  currentCustodianUserId: null, currentCustodianRiderId: null, currentStatus: "Created", serviceTypeId: "s1",
  weightKg: 1.5, lengthCm: null, widthCm: null, heightCm: null, declaredValue: 0, codAmount: 2500, wantsInsurance: false,
  deliveryCharge: 150, insuranceCharge: 0, otherCharges: 0, totalCharge: 150, recipientName: "Sita", recipientPhone: "98",
  recipientAddressLine: "Jawalakhel", senderName: "S", senderPhone: "1", senderAddressLine: "A", items: [],
  createdAt: "2026-10-05T05:00:00Z", lastEventAt: "2026-10-06T05:00:00Z",
};

beforeEach(() => {
  shipment = { ...base };
  canCancel = true;
  trackState = {
    data: {
      awb: "QA-1",
      currentStatus: "Received",
      events: [{ status: "Received", timestamp: "2026-10-05T05:00:00Z", location: null }],
      destinationArea: null,
      senderName: null,
    },
    isError: false,
    isLoading: false,
    refetch: vi.fn(),
  };
  mutateAsync.mockReset();
});

describe("MerchantShipmentDetailSheet", () => {
  it("shows prepaid and omits 'You receive' for a prepaid shipment", () => {
    shipment = { ...base, codAmount: 0 };
    render(<MerchantShipmentDetailSheet />);
    expect(screen.getAllByText("Prepaid").length).toBeGreaterThan(0);
    expect(screen.queryByText("You receive")).toBeNull();
  });

  it("shows the net 'You receive' for a COD shipment", () => {
    render(<MerchantShipmentDetailSheet />);
    expect(screen.getByText("You receive")).toBeInTheDocument();
    expect(screen.getByText("Rs 2,350")).toBeInTheDocument();
    expect(screen.getByText("Not yet collected")).toBeInTheDocument();
  });

  it("shows Cancel for a Created shipment with permission, and confirms before cancelling", () => {
    render(<MerchantShipmentDetailSheet />);
    fireEvent.click(screen.getByRole("button", { name: "Cancel shipment" }));
    expect(screen.getByText("Cancel this shipment? This can't be undone.")).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("hides Cancel without permission", () => {
    canCancel = false;
    render(<MerchantShipmentDetailSheet />);
    expect(screen.queryByRole("button", { name: "Cancel shipment" })).toBeNull();
  });

  it("hides Cancel for a Delivered shipment", () => {
    shipment = { ...base, currentStatus: "Delivered" };
    render(<MerchantShipmentDetailSheet />);
    expect(screen.queryByRole("button", { name: "Cancel shipment" })).toBeNull();
  });

  it("shows Journey unavailable on a tracking error while facts still render", () => {
    trackState = { isError: true, isLoading: false, refetch: vi.fn() };
    render(<MerchantShipmentDetailSheet />);
    expect(screen.getByText("Journey unavailable right now")).toBeInTheDocument();
    expect(screen.getByText("Your charge")).toBeInTheDocument();
    expect(screen.getByText("Lalitpur")).toBeInTheDocument();
  });

  it("renders no rider or staff names", () => {
    render(<MerchantShipmentDetailSheet />);
    expect(screen.queryByText(/Rider/)).toBeNull();
  });
});
