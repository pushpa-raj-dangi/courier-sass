import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useRiderLocations } from "./useRiderLocations";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useRiderLocations", () => {
  it("fetches locations for the given branch", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [{ riderId: "r1", latitude: 27.7, longitude: 85.3, accuracyMeters: 10, recordedAt: "2026-09-17T00:00:00Z" }],
    });

    const { result } = renderHook(() => useRiderLocations("branch-1"), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.[0].riderId).toBe("r1");
    expect(global.fetch).toHaveBeenCalledWith("/api/riders/branches/branch-1/locations", undefined);
  });

  it("does not fetch when branchId is undefined", () => {
    global.fetch = vi.fn();

    renderHook(() => useRiderLocations(undefined), { wrapper });

    expect(global.fetch).not.toHaveBeenCalled();
  });
});
