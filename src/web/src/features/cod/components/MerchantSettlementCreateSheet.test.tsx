import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

vi.mock("@/features/shipments/hooks/useMerchants", () => ({
  useMerchants: () => ({ data: [{ id: "m1", displayName: "Shipper Pvt. Ltd.", isActive: true }] }),
}));

const mutateAsync = vi.fn();
vi.mock("@/features/cod/hooks/useCreateMerchantSettlement", () => ({
  useCreateMerchantSettlement: () => ({ mutateAsync, isPending: false }),
}));

const openMerchantSettlementDetail = vi.fn();
vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: "merchant-settlement-create" }, close: vi.fn(), openMerchantSettlementDetail }),
}));

import { MerchantSettlementCreateSheet } from "./MerchantSettlementCreateSheet";

describe("MerchantSettlementCreateSheet", () => {
  it("submits the selected merchant and period as an ISO date range", async () => {
    mutateAsync.mockResolvedValue({ id: "ms1" });
    render(<MerchantSettlementCreateSheet />);

    fireEvent.click(screen.getByLabelText("Merchant"));
    fireEvent.click(await screen.findByRole("option", { name: "Shipper Pvt. Ltd." }));
    fireEvent.change(screen.getByLabelText("Period From"), { target: { value: "2026-08-01" } });
    fireEvent.change(screen.getByLabelText("Period To"), { target: { value: "2026-08-31" } });
    fireEvent.click(screen.getByRole("button", { name: /create settlement/i }));

    await waitFor(() => {
      expect(mutateAsync).toHaveBeenCalledWith({ merchantId: "m1", periodFrom: "2026-08-01T00:00:00Z", periodTo: "2026-08-31T23:59:59Z" });
    });
    await waitFor(() => {
      expect(openMerchantSettlementDetail).toHaveBeenCalledWith("ms1");
    });
  });
});
