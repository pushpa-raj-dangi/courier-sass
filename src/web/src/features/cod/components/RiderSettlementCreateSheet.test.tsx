import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", name: "Kathmandu Hub" }] }),
}));
vi.mock("@/features/riders/hooks/useRiders", () => ({
  useRiders: () => ({ data: [{ id: "r1", userId: "u1", baseBranchId: "b1", licenseNumber: "LIC-001", roleType: "DeliveryRider", isActive: true }] }),
}));

const mutateAsync = vi.fn();
vi.mock("@/features/cod/hooks/useCreateRiderSettlement", () => ({
  useCreateRiderSettlement: () => ({ mutateAsync, isPending: false }),
}));

const openRiderSettlementDetail = vi.fn();
vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: "rider-settlement-create" }, close: vi.fn(), openRiderSettlementDetail }),
}));

import { RiderSettlementCreateSheet } from "./RiderSettlementCreateSheet";

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient();
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe("RiderSettlementCreateSheet", () => {
  it("submits the selected branch, rider, and declared amount", async () => {
    mutateAsync.mockResolvedValue({ id: "rs1" });
    renderWithClient(<RiderSettlementCreateSheet />);

    fireEvent.click(screen.getByLabelText("Branch"));
    fireEvent.click(await screen.findByRole("option", { name: "Kathmandu Hub" }));
    fireEvent.click(screen.getByLabelText("Rider"));
    fireEvent.click(await screen.findByRole("option", { name: "LIC-001" }));
    fireEvent.change(screen.getByLabelText("Declared Amount"), { target: { value: "2500" } });
    fireEvent.click(screen.getByRole("button", { name: /create settlement/i }));

    await waitFor(() => {
      expect(mutateAsync).toHaveBeenCalledWith({ riderId: "r1", branchId: "b1", declaredAmount: 2500 });
    });
    await waitFor(() => {
      expect(openRiderSettlementDetail).toHaveBeenCalledWith("rs1");
    });
  });
});
