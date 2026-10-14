"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantSettlement } from "@/features/cod/types";

export function useMerchantSettlements(merchantId?: string, range: { from?: string; to?: string } = {}) {
  const { from, to } = range;
  const query = new URLSearchParams();
  if (merchantId) query.set("merchantId", merchantId);
  if (from) query.set("from", from);
  if (to) query.set("to", to);
  const search = query.toString();
  return useQuery({
    queryKey: ["cod", "merchant-settlements", merchantId ?? null, from ?? null, to ?? null],
    queryFn: () => clientFetch<MerchantSettlement[]>(`/api/cod/merchant-settlements${search ? `?${search}` : ""}`),
  });
}
