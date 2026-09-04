import "fake-indexeddb/auto";
import { describe, expect, it, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { resetOutboxDbForTests, listOutboxItems } from "@/features/rider-app/sync/outboxDb";
import { useCompleteDelivery } from "./useCompleteDelivery";

vi.mock("@/features/rider-app/sync/syncManager", () => ({ drainOutbox: vi.fn() }));

function wrapper({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient();
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useCompleteDelivery", () => {
  beforeEach(() => resetOutboxDbForTests());

  it("enqueues a delivery-complete action carrying the raw blobs, not a pre-resolved storage key", async () => {
    const { result } = renderHook(() => useCompleteDelivery("r1"), { wrapper });
    const signatureBlob = new Blob(["sig"], { type: "image/png" });

    result.current.mutate({
      shipmentId: "s1",
      recipientName: "Recipient One",
      otpVerified: true,
      latitude: null,
      longitude: null,
      collectedCodAmount: null,
      signatureBlob,
      photoBlob: null,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const items = await listOutboxItems();
    expect(items).toHaveLength(1);
    expect(items[0].action.kind).toBe("delivery-complete");
    // Not `toBe(signatureBlob)`: every IndexedDB write structured-clones its value (spec
    // behavior, real browsers included), so the persisted Blob is never the same object
    // reference as the one passed in — the meaningful assertion is that it's still a real
    // Blob carrying the original bytes/type, not a pre-resolved storage key string.
    const persistedBlob = (items[0].action as { signatureBlob: Blob | null }).signatureBlob;
    expect(persistedBlob).toBeInstanceOf(Blob);
    expect(persistedBlob?.type).toBe("image/png");
    expect(persistedBlob?.size).toBe(signatureBlob.size);
    await expect(persistedBlob?.text()).resolves.toBe(await signatureBlob.text());
  });
});
