"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantCodSummary } from "@/features/merchant-cod/types";

export function useMerchantCodSummary() {
  return useQuery({
    queryKey: ["merchant-cod-summary"],
    queryFn: () => clientFetch<MerchantCodSummary>("/api/merchant/cod/summary"),
  });
}
