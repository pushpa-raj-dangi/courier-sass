"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { AvailablePlan } from "@/features/billing/types";

export function useAvailablePlans() {
  return useQuery({
    queryKey: ["billing-available-plans"],
    queryFn: () => clientFetch<AvailablePlan[]>("/api/billing/plans"),
  });
}
