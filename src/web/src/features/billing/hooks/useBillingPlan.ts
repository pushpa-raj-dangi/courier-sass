"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PlanSummary } from "@/features/billing/types";

export function useBillingPlan() {
  return useQuery({
    queryKey: ["billing-plan"],
    queryFn: () => clientFetch<PlanSummary>("/api/billing/plan"),
  });
}
