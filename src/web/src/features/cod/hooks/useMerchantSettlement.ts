"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantSettlement } from "@/features/cod/types";

export function useMerchantSettlement(id: string) {
  return useQuery({
    queryKey: ["cod", "merchant-settlements", id],
    queryFn: () => clientFetch<MerchantSettlement>(`/api/cod/merchant-settlements/${id}`),
    enabled: id.length > 0,
  });
}
