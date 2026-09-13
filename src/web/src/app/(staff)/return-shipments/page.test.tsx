import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import ReturnShipmentsPage from "./page";

vi.mock("@/features/ndr/hooks/useReturnShipments", () => ({
  useReturnShipments: () => ({ data: [], isLoading: false, isError: false, refetch: vi.fn() }),
}));
vi.mock("@/features/ndr/hooks/useReturnShipmentsSummary", () => ({
  useReturnShipmentsSummary: () => ({
    data: { byRtoStage: { RtoInitiated: 1, RtoInTransit: 2, RtoAtOrigin: 1, RtoDelivered: 3 }, merchantConfirmed: 5, pendingConfirmation: 4 },
    isLoading: false,
    isError: false,
  }),
}));
vi.mock("@/features/branches/hooks/useBranches", () => ({ useBranches: () => ({ data: [] }) }));
vi.mock("@/features/ndr/store/returnShipmentPanelStore", () => ({
  useReturnShipmentPanelStore: (selector: (s: { openDetail: () => void }) => unknown) => selector({ openDetail: vi.fn() }),
}));

describe("ReturnShipmentsPage", () => {
  it("renders the summary strip above the table using the summary hook's data", async () => {
    render(<ReturnShipmentsPage />);
    await waitFor(() => expect(screen.getByText("RTO Initiated")).toBeInTheDocument());
    expect(screen.getByText("Merchant Confirmed")).toBeInTheDocument();
    expect(screen.getByText("5")).toBeInTheDocument();
  });
});
