import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
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

// DatePicker's Calendar opens on the current month by default — pinning system time keeps the
// visible month (and thus which [data-day] cells are on-screen without extra nav-button clicks)
// deterministic regardless of when this suite runs.
beforeEach(() => {
  // Only Date is faked — waitFor below needs real timers to poll.
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 7, 15));
});
afterEach(() => {
  vi.useRealTimers();
});

// react-day-picker's own <td data-day="yyyy-MM-dd"> is locale-independent; the inner button's own
// data-day (CalendarDayButton, calendar.tsx) uses toLocaleDateString() and isn't safe to match on.
function pickCalendarDate(triggerLabel: string, isoDate: string) {
  fireEvent.click(screen.getByLabelText(triggerLabel));
  const cell = document.querySelector(`td[data-day="${isoDate}"]`);
  const button = cell?.querySelector("button");
  if (!button) throw new Error(`No calendar day button found for ${isoDate}`);
  fireEvent.click(button);
}

describe("MerchantSettlementCreateSheet", () => {
  it("submits the selected merchant and period as an ISO date range", async () => {
    mutateAsync.mockResolvedValue({ id: "ms1" });
    render(<MerchantSettlementCreateSheet />);

    fireEvent.click(screen.getByLabelText("Merchant"));
    fireEvent.click(await screen.findByRole("option", { name: "Shipper Pvt. Ltd." }));
    pickCalendarDate("Period From", "2026-08-01");
    pickCalendarDate("Period To", "2026-08-31");
    fireEvent.click(screen.getByRole("button", { name: /create settlement/i }));

    await waitFor(() => {
      expect(mutateAsync).toHaveBeenCalledWith({ merchantId: "m1", periodFrom: "2026-08-01T00:00:00Z", periodTo: "2026-08-31T23:59:59Z" });
    });
    await waitFor(() => {
      expect(openMerchantSettlementDetail).toHaveBeenCalledWith("ms1");
    });
  });
});
