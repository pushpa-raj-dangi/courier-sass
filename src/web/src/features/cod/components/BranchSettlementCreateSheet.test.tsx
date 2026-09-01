import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", name: "Kathmandu Hub" }] }),
}));
vi.mock("@/features/cod/hooks/useRiderSettlements", () => ({
  useRiderSettlements: () => ({
    data: { items: [{ id: "rs1", riderId: "r1", branchId: "b1", declaredAmount: 2500, shipmentCount: 1, settledAt: "2026-09-01T00:00:00Z", confirmedByUserId: "u1", branchCashSettlementId: null }] },
  }),
}));
vi.mock("@/features/riders/hooks/useRiders", () => ({
  useRiders: () => ({ data: [{ id: "r1", userId: "u1", baseBranchId: "b1", licenseNumber: "LIC-001", roleType: "DeliveryRider", isActive: true }] }),
}));

const mutateAsync = vi.fn();
vi.mock("@/features/cod/hooks/useCreateBranchSettlement", () => ({
  useCreateBranchSettlement: () => ({ mutateAsync, isPending: false }),
}));

const openBranchSettlementDetail = vi.fn();
vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: "branch-settlement-create" }, close: vi.fn(), openBranchSettlementDetail }),
}));

import { BranchSettlementCreateSheet } from "./BranchSettlementCreateSheet";

describe("BranchSettlementCreateSheet", () => {
  it("submits the selected branch, checked rider settlements, and confirmed amount", async () => {
    mutateAsync.mockResolvedValue({ id: "bs1" });
    render(<BranchSettlementCreateSheet />);

    fireEvent.click(screen.getByLabelText("Branch"));
    fireEvent.click(await screen.findByRole("option", { name: "Kathmandu Hub" }));
    fireEvent.click(await screen.findByLabelText(/LIC-001/));
    fireEvent.change(screen.getByLabelText("Confirmed Amount"), { target: { value: "2500" } });
    fireEvent.click(screen.getByRole("button", { name: /create settlement/i }));

    await waitFor(() => {
      expect(mutateAsync).toHaveBeenCalledWith({ branchId: "b1", riderCashSettlementIds: ["rs1"], confirmedAmount: 2500 });
    });
    await waitFor(() => {
      expect(openBranchSettlementDetail).toHaveBeenCalledWith("bs1");
    });
  });
});
