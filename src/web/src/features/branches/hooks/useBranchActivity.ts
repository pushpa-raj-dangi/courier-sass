"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { BranchActivity } from "@/features/branches/types";

export function useBranchActivity(options: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: ["branches", "activity"],
    queryFn: () => clientFetch<BranchActivity>("/api/branches/activity"),
    enabled: options.enabled ?? true,
  });
}
