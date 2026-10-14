import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { formatRs } from "@/features/cod/lib/codFormat";

const settlement = {
  id: "rs1", riderId: "r1", branchId: "b1", declaredAmount: 3000, shipmentCount: 3, settledAt: "2026-10-07T10:00:00Z",
  confirmedByUserId: "u1", branchCashSettlementId: null, expectedAmount: 3000, variance: 0,
};
const useRiderSettlementsSpy = vi.fn();
vi.mock("@/features/cod/hooks/useRiderSettlements", () => ({
  useRiderSettlements: (...args: unknown[]) => {
    useRiderSettlementsSpy(...args);
    return { data: { items: [settlement] }, isLoading: false, isError: false, refetch: vi.fn() };
  },
}));
const parcel = (id: string, amount: number) => ({
  id, shipmentId: `s-${id}`, merchantId: "m1", branchId: "b1", expectedAmount: amount, shipmentTotalCharge: 150,
  collectedAmount: amount, status: "RiderDeposited", collectedByRiderId: "r1", collectedAt: "2026-10-07T09:00:00Z",
  deliveryProofId: null, riderCashSettlementId: "rs1", merchantSettlementItemId: null, createdAt: "2026-10-07T08:00:00Z", awb: `AWB-${id.toUpperCase()}`,
});
let parcelsResult: unknown;
const refetchParcels = vi.fn();
vi.mock("@/features/cod/hooks/useCodCollections", () => ({ useCodCollections: () => parcelsResult }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ riderName: () => "Ram Thapa", branchName: () => "Kathmandu Hub", userName: () => "Staff" }),
}));
let panel: unknown;
vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (sel: (s: unknown) => unknown) => sel({ panel, close: vi.fn(), openCollectionDetail: vi.fn() }),
}));

import { RiderSettlementDetailSheet } from "./RiderSettlementDetailSheet";

beforeEach(() => {
  panel = { mode: "rider-settlement-detail", id: "rs1" };
  refetchParcels.mockReset();
  useRiderSettlementsSpy.mockReset();
  parcelsResult = { data: { items: [parcel("c1", 1000), parcel("c2", 2000), parcel("c3", 0)] }, isLoading: false, isError: false, refetch: refetchParcels };
});

describe("RiderSettlementDetailSheet", () => {
  it("looks the deposit up only while open", () => {
    render(<RiderSettlementDetailSheet />);
    expect(useRiderSettlementsSpy).toHaveBeenCalledWith(expect.objectContaining({ pageSize: 200 }), { enabled: true });
    useRiderSettlementsSpy.mockReset();
    panel = { mode: "closed" };
    render(<RiderSettlementDetailSheet />);
    expect(useRiderSettlementsSpy).toHaveBeenCalledWith(expect.anything(), { enabled: false });
  });

  it("lists the deposit's parcels without a partial-count note when all are shown", () => {
    render(<RiderSettlementDetailSheet />);
    expect(screen.getByText(formatRs(2000))).toBeInTheDocument();
    expect(screen.getByText("AWB-C2")).toBeInTheDocument();
    expect(screen.queryByText(/Showing/)).toBeNull();
  });

  it("says how many parcels are shown when fewer than the deposit's count", () => {
    parcelsResult = { data: { items: [parcel("c1", 1000), parcel("c2", 2000)] }, isLoading: false, isError: false, refetch: refetchParcels };
    render(<RiderSettlementDetailSheet />);
    expect(screen.getByText("Showing 2 of 3 parcels")).toBeInTheDocument();
  });

  it("shows an empty state when no parcels are linked", () => {
    parcelsResult = { data: { items: [] }, isLoading: false, isError: false, refetch: refetchParcels };
    render(<RiderSettlementDetailSheet />);
    expect(screen.getByText("No parcels linked to this deposit.")).toBeInTheDocument();
    expect(screen.queryByText(/Showing/)).toBeNull();
  });

  it("shows an error state with retry when parcels fail to load", () => {
    parcelsResult = { data: undefined, isLoading: false, isError: true, refetch: refetchParcels };
    render(<RiderSettlementDetailSheet />);
    expect(screen.getByText("Couldn't load parcels")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /retry/i }));
    expect(refetchParcels).toHaveBeenCalled();
  });
});
