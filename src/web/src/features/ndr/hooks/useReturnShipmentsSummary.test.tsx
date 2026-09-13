import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useReturnShipmentsSummary } from "./useReturnShipmentsSummary";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useReturnShipmentsSummary", () => {
  it("fetches the summary proxy route", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ byRtoStage: { RtoInitiated: 1, RtoInTransit: 2, RtoAtOrigin: 1, RtoDelivered: 3 }, merchantConfirmed: 3, pendingConfirmation: 4 }),
    });

    const { result } = renderHook(() => useReturnShipmentsSummary(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.merchantConfirmed).toBe(3);
    expect(global.fetch).toHaveBeenCalledWith("/api/return-shipments/summary", undefined);
  });
});
