"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { OrgDashboardSummary } from "@/features/reports/types";

export function useOrgDashboard() {
  return useQuery({
    queryKey: ["org-dashboard"],
    queryFn: () => clientFetch<OrgDashboardSummary>("/api/reports/dashboard/organization"),
  });
}
