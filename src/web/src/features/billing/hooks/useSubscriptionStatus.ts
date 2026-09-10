"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { SubscriptionStatusInfo } from "@/features/billing/types";

export function useSubscriptionStatus() {
  return useQuery({
    queryKey: ["billing-subscription"],
    queryFn: () => clientFetch<SubscriptionStatusInfo>("/api/billing/subscription"),
  });
}
