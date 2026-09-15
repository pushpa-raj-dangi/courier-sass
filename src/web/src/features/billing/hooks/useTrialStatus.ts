"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { TrialStatusInfo } from "@/features/billing/types";

export function useTrialStatus() {
  return useQuery({
    queryKey: ["billing-status"],
    queryFn: () => clientFetch<TrialStatusInfo>("/api/billing/status"),
  });
}
