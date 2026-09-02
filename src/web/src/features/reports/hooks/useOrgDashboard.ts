"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { OrgDashboardSummary } from "@/features/reports/types";

export function useOrgDashboard(params: { from?: string; to?: string } = {}) {
  const { from, to } = params;
  const query = new URLSearchParams();
  if (from) query.set("from", from);
  if (to) query.set("to", to);
  const search = query.toString();

  return useQuery({
    queryKey: ["org-dashboard", from ?? null, to ?? null],
    queryFn: () => clientFetch<OrgDashboardSummary>(`/api/reports/dashboard/organization${search ? `?${search}` : ""}`),
  });
}
