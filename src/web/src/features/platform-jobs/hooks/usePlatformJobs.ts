"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { JobsOverview } from "@/features/platform-jobs/types";

export function usePlatformJobs() {
  return useQuery({
    queryKey: ["platform-jobs", "overview"],
    queryFn: () => clientFetch<JobsOverview>("/api/platform/jobs"),
    refetchInterval: 10000,
  });
}
