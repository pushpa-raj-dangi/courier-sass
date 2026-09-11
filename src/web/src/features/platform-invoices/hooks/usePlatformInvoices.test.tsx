import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { usePlatformInvoices } from "./usePlatformInvoices";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const pagedResult = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 };

describe("usePlatformInvoices", () => {
  it("builds the query string from page/pageSize with no status or search", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => usePlatformInvoices("all", "", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/invoices?page=1&pageSize=25", undefined);
  });

  it("includes status in the query string when not 'all'", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => usePlatformInvoices("Overdue", "", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/invoices?page=1&pageSize=25&status=Overdue", undefined);
  });

  it("includes search in the query string when set", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => usePlatformInvoices("all", "kathmandu", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/invoices?page=1&pageSize=25&search=kathmandu", undefined);
  });

  it("reflects the page argument in the query string", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => usePlatformInvoices("all", "", 3, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/invoices?page=3&pageSize=25", undefined);
  });
});
