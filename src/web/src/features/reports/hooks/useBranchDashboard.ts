"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { BranchDashboardSummary } from "@/features/reports/types";

export function useBranchDashboard(branchId: string) {
  return useQuery({
    queryKey: ["branch-dashboard", branchId],
    queryFn: () => clientFetch<BranchDashboardSummary>(`/api/reports/dashboard/branch/${branchId}`),
  });
}
