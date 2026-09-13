"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { BranchType } from "@/features/branches/types";

export interface BranchSummary {
  total: number;
  active: number;
  inactive: number;
  byType: Record<BranchType, number>;
}

export function useBranchesSummary() {
  return useQuery({
    queryKey: ["branches-summary"],
    queryFn: () => clientFetch<BranchSummary>("/api/branches/summary"),
  });
}
