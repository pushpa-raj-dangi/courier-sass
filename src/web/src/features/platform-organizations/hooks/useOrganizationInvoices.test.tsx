import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useOrganizationInvoices } from "./useOrganizationInvoices";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const pagedResult = { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 };

describe("useOrganizationInvoices", () => {
  it("requests the given organization's invoices page", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => useOrganizationInvoices("org-1", 1, 10), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/organizations/org-1/invoices?page=1&pageSize=10", undefined);
  });

  it("does not fetch when organizationId is null", () => {
    global.fetch = vi.fn();

    renderHook(() => useOrganizationInvoices(null, 1, 10), { wrapper });

    expect(global.fetch).not.toHaveBeenCalled();
  });
});
