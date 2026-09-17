"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export interface RiderLocation {
  riderId: string;
  latitude: number;
  longitude: number;
  accuracyMeters: number | null;
  recordedAt: string;
}

const POLL_INTERVAL_MS = 20_000;

export function useRiderLocations(branchId: string | undefined) {
  return useQuery({
    queryKey: ["riders", "locations", branchId],
    queryFn: () => clientFetch<RiderLocation[]>(`/api/riders/branches/${branchId}/locations`),
    enabled: branchId !== undefined,
    refetchInterval: POLL_INTERVAL_MS,
  });
}
