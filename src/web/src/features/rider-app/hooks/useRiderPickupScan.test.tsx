import "fake-indexeddb/auto";
import { describe, expect, it, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { resetOutboxDbForTests, listOutboxItems } from "@/features/rider-app/sync/outboxDb";
import { useRiderPickupScan } from "./useRiderPickupScan";

vi.mock("@/features/rider-app/sync/syncManager", () => ({ drainOutbox: vi.fn() }));

function wrapper({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient();
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useRiderPickupScan", () => {
  beforeEach(() => resetOutboxDbForTests());

  it("enqueues a pickup-scan action instead of calling fetch directly", async () => {
    const { result } = renderHook(() => useRiderPickupScan("r1"), { wrapper });

    result.current.mutate({ awb: "AWB001", shipmentId: "s1" });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const items = await listOutboxItems();
    expect(items).toHaveLength(1);
    expect(items[0].action).toEqual({ kind: "pickup-scan", riderId: "r1", shipmentId: "s1", awb: "AWB001" });
  });
});
