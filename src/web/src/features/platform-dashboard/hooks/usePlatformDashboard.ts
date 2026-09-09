"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PlatformMetrics } from "@/features/platform-dashboard/types";

export function usePlatformDashboard() {
  return useQuery({
    queryKey: ["platform-dashboard"],
    queryFn: () => clientFetch<PlatformMetrics>("/api/platform/dashboard"),
  });
}
