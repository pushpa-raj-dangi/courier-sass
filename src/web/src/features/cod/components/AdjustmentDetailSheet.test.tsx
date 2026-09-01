import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

vi.mock("@/features/cod/hooks/useAdjustments", () => ({
  useAdjustments: () => ({
    data: {
      items: [{
        id: "a1", entityType: "MerchantSettlementItem", entityId: "item1", beforeValue: 0, afterValue: 100,
        reason: "Damaged parcel deduction", requestedByUserId: "u1", requestedAt: "2026-09-01T00:00:00Z",
        approvedByUserId: null, approvedAt: null, applied: false,
      }],
    },
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
}));

const mutateAsync = vi.fn();
vi.mock("@/features/cod/hooks/useApproveAdjustment", () => ({
  useApproveAdjustment: () => ({ mutateAsync, isPending: false }),
}));

vi.mock("@/features/auth/hooks/usePermission", () => ({
  usePermission: () => true,
}));

vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: "adjustment-detail", id: "a1" }, close: vi.fn() }),
}));

import { AdjustmentDetailSheet } from "./AdjustmentDetailSheet";

describe("AdjustmentDetailSheet", () => {
  it("shows the before/after values and reason, and approves on click", async () => {
    mutateAsync.mockResolvedValue({ id: "a1", applied: true });
    render(<AdjustmentDetailSheet />);

    expect(screen.getByText("Damaged parcel deduction")).toBeInTheDocument();
    expect(screen.getByText("Rs. 0.00")).toBeInTheDocument();
    expect(screen.getByText("Rs. 100.00")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /approve/i }));

    await waitFor(() => {
      expect(mutateAsync).toHaveBeenCalled();
    });
  });
});
