import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useMerchantShipmentQuote } from "./useMerchantShipmentQuote";
import * as clientFetchModule from "@/lib/api/clientFetch";

function wrapper({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useMerchantShipmentQuote", () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("does not fetch when input is null", () => {
    const spy = vi.spyOn(clientFetchModule, "clientFetch");
    renderHook(() => useMerchantShipmentQuote(null), { wrapper });
    expect(spy).not.toHaveBeenCalled();
  });

  it("fetches a quote after the debounce delay, always sending merchantId: null", async () => {
    const spy = vi
      .spyOn(clientFetchModule, "clientFetch")
      .mockResolvedValue({ baseRate: 100, appliedCharges: [], discountPercent: 0, subtotal: 100, total: 100 });

    const input = {
      originBranchId: "b1",
      destinationBranchId: "b2",
      weightKg: 2,
      serviceTypeId: "st1",
      codAmount: 0,
      declaredValue: 500,
      wantsInsurance: false,
    };
    renderHook(() => useMerchantShipmentQuote(input), { wrapper });

    expect(spy).not.toHaveBeenCalled();
    vi.advanceTimersByTime(500);
    await waitFor(() =>
      expect(spy).toHaveBeenCalledWith(
        "/api/merchant/pricing/quote",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ ...input, merchantId: null }),
        })
      )
    );
  });
});
