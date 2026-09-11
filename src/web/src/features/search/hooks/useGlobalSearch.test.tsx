import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useGlobalSearch } from "./useGlobalSearch";
import * as searchApiModule from "@/features/search/api/searchApi";
import { EMPTY_SEARCH_RESULTS } from "@/features/search/types";

function wrapper({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useGlobalSearch", () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("does not search a query of 1 character or fewer", () => {
    const spy = vi.spyOn(searchApiModule, "searchEntities");
    renderHook(() => useGlobalSearch("a"), { wrapper });

    vi.advanceTimersByTime(300);
    expect(spy).not.toHaveBeenCalled();
  });

  it("searches after the debounce delay once the query is long enough", async () => {
    const spy = vi.spyOn(searchApiModule, "searchEntities").mockResolvedValue(EMPTY_SEARCH_RESULTS);
    const { result } = renderHook(() => useGlobalSearch("AWB123"), { wrapper });

    vi.advanceTimersByTime(300);

    await waitFor(() => expect(spy).toHaveBeenCalledWith("AWB123"));
    await waitFor(() => expect(result.current.data).toEqual(EMPTY_SEARCH_RESULTS));
  });
});
