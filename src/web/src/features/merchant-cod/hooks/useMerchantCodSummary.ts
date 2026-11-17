"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantCodSummary } from "@/features/merchant-cod/types";

export function useMerchantCodSummary({ enabled = true }: { enabled?: boolean } = {}) {
  return useQuery({
    enabled,
    queryKey: ["merchant-cod-summary"],
    queryFn: () => clientFetch<MerchantCodSummary>("/api/merchant/cod/summary"),
  });
}
