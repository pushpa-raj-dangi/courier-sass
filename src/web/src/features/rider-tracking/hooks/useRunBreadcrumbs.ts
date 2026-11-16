"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { RiderLocation } from "@/features/rider-tracking/hooks/useRiderLocations";

/** Today's pings on one delivery run, oldest first (GET /riders/runs/{runId}/breadcrumbs). */
export function useRunBreadcrumbs(runId: string | null, enabled: boolean) {
  return useQuery({
    queryKey: ["riders", "runs", runId, "breadcrumbs"],
    queryFn: () => clientFetch<RiderLocation[]>(`/api/riders/runs/${runId}/breadcrumbs`),
    enabled: enabled && runId !== null,
  });
}
