import { describe, expect, it, vi } from "vitest";
import { act, renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useCompletePayment } from "./useCompletePayment";
import * as clientFetchModule from "@/lib/api/clientFetch";

// AppShell's TrialStatusBanner (mounted once per hard page load, across every staff page) reads
// "billing-status" and never remounts on the client-side navigation a user takes from the
// callback page back into the app — these queries must be invalidated by this mutation itself,
// not left to a page remount that may never happen.
describe("useCompletePayment", () => {
  function makeWrapper(queryClient: QueryClient) {
    return function Wrapper({ children }: { children: React.ReactNode }) {
      return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    };
  }

  it("invalidates billing-status, billing-subscription, and billing-invoices on success", async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    queryClient.setQueryData(["billing-status"], { status: "TrialExpired" });
    queryClient.setQueryData(["billing-subscription"], { status: "TrialExpired" });
    queryClient.setQueryData(["billing-invoices"], []);
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ succeeded: true, message: "Payment confirmed." });

    const { result } = renderHook(() => useCompletePayment(), { wrapper: makeWrapper(queryClient) });

    await act(async () => {
      await result.current.mutateAsync({ paymentId: "p1", callbackParameters: {} });
    });

    await waitFor(() => {
      expect(queryClient.getQueryState(["billing-status"])?.isInvalidated).toBe(true);
      expect(queryClient.getQueryState(["billing-subscription"])?.isInvalidated).toBe(true);
      expect(queryClient.getQueryState(["billing-invoices"])?.isInvalidated).toBe(true);
    });
  });

  it("does not invalidate anything when the gateway denies the payment", async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    queryClient.setQueryData(["billing-status"], { status: "TrialExpired" });
    vi.spyOn(clientFetchModule, "clientFetch").mockRejectedValue(new Error("PAYMENT_NOT_CONFIRMED"));

    const { result } = renderHook(() => useCompletePayment(), { wrapper: makeWrapper(queryClient) });

    await act(async () => {
      await result.current.mutateAsync({ paymentId: "p1", callbackParameters: {} }).catch(() => {});
    });

    expect(queryClient.getQueryState(["billing-status"])?.isInvalidated).toBe(false);
  });
});
