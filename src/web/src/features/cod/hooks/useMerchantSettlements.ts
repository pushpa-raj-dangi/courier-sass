"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantSettlement } from "@/features/cod/types";

export function useMerchantSettlements(merchantId?: string) {
  const search = merchantId ? `?merchantId=${merchantId}` : "";
  return useQuery({
    queryKey: ["cod", "merchant-settlements", merchantId ?? null],
    queryFn: () => clientFetch<MerchantSettlement[]>(`/api/cod/merchant-settlements${search}`),
  });
}
