"use client";

import { useQuery } from "@tanstack/react-query";
import type { PublicTrackingResult } from "@/features/tracking/types";

// A dedicated fetcher, not the shared clientFetch (lib/api/clientFetch.ts): clientFetch
// collapses every non-OK response into a single Error carrying only a stringified message,
// discarding both the HTTP status and any response headers. Public tracking genuinely needs
// both — a distinct 404 vs. 429 UI state, and the 429's real Retry-After header value (design
// spec §5.4) — so this hook talks to the BFF route directly instead.
export class TrackingNotFoundError extends Error {
  constructor() {
    super("No shipment found for this AWB.");
    this.name = "TrackingNotFoundError";
  }
}

export class TrackingRateLimitedError extends Error {
  constructor(public readonly retryAfterSeconds: number) {
    super("Too many lookups.");
    this.name = "TrackingRateLimitedError";
  }
}

async function fetchTracking(awb: string): Promise<PublicTrackingResult> {
  const response = await fetch(`/api/track/${encodeURIComponent(awb)}`);
  if (response.status === 404) {
    throw new TrackingNotFoundError();
  }
  if (response.status === 429) {
    const retryAfter = Number(response.headers.get("Retry-After") ?? "60");
    throw new TrackingRateLimitedError(Number.isFinite(retryAfter) ? retryAfter : 60);
  }
  if (!response.ok) {
    throw new Error("TRACKING_REQUEST_FAILED");
  }
  return response.json();
}

const TERMINAL_STATUSES = new Set<string>(["Delivered", "Cancelled"]);

export function useTrackShipment(awb: string, options?: { poll?: boolean }) {
  const poll = options?.poll ?? true;
  return useQuery({
    queryKey: ["track", awb],
    queryFn: () => fetchTracking(awb),
    enabled: awb !== "",
    retry: false,
    refetchInterval: poll
      ? (query) => {
          const data = query.state.data;
          if (!data || TERMINAL_STATUSES.has(data.currentStatus)) {
            return false;
          }
          return 15000;
        }
      : false,
  });
}
