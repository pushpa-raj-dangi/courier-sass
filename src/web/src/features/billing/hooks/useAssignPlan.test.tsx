import { describe, expect, it, vi } from "vitest";
import { act, renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useAssignPlan } from "./useAssignPlan";
import * as clientFetchModule from "@/lib/api/clientFetch";

// PlanPicker (rendered inline on /billing, no navigation/remount involved) calls this mutation —
// AppShell's TrialStatusBanner (mounted once per hard page load, across every staff page) reads
// "billing-status" and would otherwise keep showing a stale "trial ending" banner after a trial
// user picks a real plan.
describe("useAssignPlan", () => {
  function makeWrapper(queryClient: QueryClient) {
    return function Wrapper({ children }: { children: React.ReactNode }) {
      return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    };
  }

  it("invalidates billing-status along with billing-subscription and billing-invoices on success", async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    queryClient.setQueryData(["billing-status"], { status: "Trial" });
    queryClient.setQueryData(["billing-subscription"], { status: "Trial" });
    queryClient.setQueryData(["billing-invoices"], []);
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ id: "sub1", status: "Active" });

    const { result } = renderHook(() => useAssignPlan(), { wrapper: makeWrapper(queryClient) });

    await act(async () => {
      await result.current.mutateAsync("plan1");
    });

    await waitFor(() => {
      expect(queryClient.getQueryState(["billing-status"])?.isInvalidated).toBe(true);
      expect(queryClient.getQueryState(["billing-subscription"])?.isInvalidated).toBe(true);
      expect(queryClient.getQueryState(["billing-invoices"])?.isInvalidated).toBe(true);
    });
  });
});
