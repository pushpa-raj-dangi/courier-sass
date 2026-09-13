import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useOrganizationSettings } from "./useOrganizationSettings";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useOrganizationSettings", () => {
  it("fetches from the BFF proxy route", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        profile: { supportEmail: null, supportPhone: null, addressLine: null, logoUrl: null, timeZoneId: "Asia/Kathmandu", businessHours: null },
        security: { refreshTokenLifetimeDays: 7, minPasswordLength: 8 },
        defaults: { defaultServiceTypeId: null, defaultOriginBranchId: null },
      }),
    });

    const { result } = renderHook(() => useOrganizationSettings(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.profile.timeZoneId).toBe("Asia/Kathmandu");
    expect(global.fetch).toHaveBeenCalledWith("/api/settings/organization", undefined);
  });
});
