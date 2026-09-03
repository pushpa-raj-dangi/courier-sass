"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantSettlement } from "@/features/merchant-cod/types";

export function useMerchantSettlement(id: string) {
  return useQuery({
    queryKey: ["merchant-cod-settlements", id],
    queryFn: () => clientFetch<MerchantSettlement>(`/api/merchant/cod/settlements/${id}`),
    enabled: id !== "",
  });
}
