"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Adjustment } from "@/features/cod/types";

export function useAdjustments(
  params: { applied?: boolean; merchantSettlementId?: string; from?: string; to?: string; page?: number; pageSize?: number } = {},
  options: { enabled?: boolean } = {},
) {
  const { applied, merchantSettlementId, from, to, page, pageSize } = params;
  const query = new URLSearchParams();
  if (applied !== undefined) query.set("applied", String(applied));
  if (merchantSettlementId) query.set("merchantSettlementId", merchantSettlementId);
  if (from) query.set("from", from);
  if (to) query.set("to", to);
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["cod", "adjustments", applied ?? null, merchantSettlementId ?? null, from ?? null, to ?? null, page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<Adjustment>>(`/api/cod/adjustments${search ? `?${search}` : ""}`),
    enabled: options.enabled ?? true,
  });
}
