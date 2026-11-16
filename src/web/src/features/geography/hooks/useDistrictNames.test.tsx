import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useDistrictNames } from "./useDistrictNames";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useDistrictNames", () => {
  it("reads each distinct province once and maps district ids to names", async () => {
    global.fetch = vi.fn().mockImplementation(async (url: string) => ({
      ok: true,
      status: 200,
      json: async () =>
        url.endsWith("p1")
          ? [{ id: "d1", provinceId: "p1", name: "Kathmandu" }]
          : [{ id: "d2", provinceId: "p2", name: "Kaski" }],
    }));

    const { result } = renderHook(() => useDistrictNames(["p1", "p2", "p1"]), { wrapper });

    await waitFor(() => expect(result.current.get("d2")).toBe("Kaski"));
    expect(result.current.get("d1")).toBe("Kathmandu");
    expect(global.fetch).toHaveBeenCalledTimes(2);
  });
});
