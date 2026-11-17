import { describe, expect, it, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useTrackShipment, TrackingNotFoundError, TrackingRateLimitedError } from "./useTrackShipment";

function wrapper({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useTrackShipment", () => {
  beforeEach(() => {
    global.fetch = vi.fn();
  });

  it("does not fetch when awb is empty", () => {
    renderHook(() => useTrackShipment(""), { wrapper });
    expect(global.fetch).not.toHaveBeenCalled();
  });

  it("sets no refetch interval when poll is false, and polls by default", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => ({ awb: "AWB1", currentStatus: "InTransit", events: [] }),
    });
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const localWrapper = ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );

    const off = renderHook(() => useTrackShipment("AWB-OFF", { poll: false }), { wrapper: localWrapper });
    const on = renderHook(() => useTrackShipment("AWB-ON"), { wrapper: localWrapper });
    await waitFor(() => expect(off.result.current.isSuccess).toBe(true));
    await waitFor(() => expect(on.result.current.isSuccess).toBe(true));

    const intervalOf = (key: string) => {
      const query = queryClient.getQueryCache().find({ queryKey: ["track", key] })!;
      const interval = query.observers[0].options.refetchInterval;
      return typeof interval === "function" ? interval(query) : interval;
    };
    expect(intervalOf("AWB-OFF")).toBe(false);
    expect(intervalOf("AWB-ON")).toBe(15000);
  });

  it("returns the tracking result on success", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => ({ awb: "AWB1", currentStatus: "InTransit", events: [] }),
    });

    const { result } = renderHook(() => useTrackShipment("AWB1"), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.currentStatus).toBe("InTransit");
    expect(global.fetch).toHaveBeenCalledWith("/api/track/AWB1");
  });

  it("surfaces a TrackingNotFoundError on 404", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 404,
      headers: new Headers(),
      json: async () => null,
    });

    const { result } = renderHook(() => useTrackShipment("UNKNOWN"), { wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error).toBeInstanceOf(TrackingNotFoundError);
  });

  it("surfaces a TrackingRateLimitedError with the Retry-After value on 429", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 429,
      headers: new Headers({ "Retry-After": "42" }),
      json: async () => ({ error: { code: "RATE_LIMIT_EXCEEDED" } }),
    });

    const { result } = renderHook(() => useTrackShipment("AWB1"), { wrapper });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error).toBeInstanceOf(TrackingRateLimitedError);
    expect((result.current.error as TrackingRateLimitedError).retryAfterSeconds).toBe(42);
  });
});
