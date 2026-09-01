"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CodReconciliationSummary } from "@/features/cod/types";

export function useCodReconciliation(params: { branchId?: string; merchantId?: string; from?: string; to?: string } = {}) {
  const { branchId, merchantId, from, to } = params;
  const query = new URLSearchParams();
  if (branchId) query.set("branchId", branchId);
  if (merchantId) query.set("merchantId", merchantId);
  if (from) query.set("from", from);
  if (to) query.set("to", to);
  const search = query.toString();

  return useQuery({
    queryKey: ["cod", "reconciliation", branchId ?? null, merchantId ?? null, from ?? null, to ?? null],
    queryFn: () => clientFetch<CodReconciliationSummary>(`/api/cod/reconciliation${search ? `?${search}` : ""}`),
  });
}
