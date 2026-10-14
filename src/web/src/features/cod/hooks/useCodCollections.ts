"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { CodCollection, CodCollectionStatus } from "@/features/cod/types";

export function useCodCollections(
  params: { status?: CodCollectionStatus; branchId?: string; merchantId?: string; riderCashSettlementId?: string; page?: number; pageSize?: number } = {},
  options: { enabled?: boolean } = {},
) {
  const { status, branchId, merchantId, riderCashSettlementId, page, pageSize } = params;
  const query = new URLSearchParams();
  if (status) query.set("status", status);
  if (branchId) query.set("branchId", branchId);
  if (merchantId) query.set("merchantId", merchantId);
  if (riderCashSettlementId) query.set("riderCashSettlementId", riderCashSettlementId);
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["cod", "collections", status ?? null, branchId ?? null, merchantId ?? null, riderCashSettlementId ?? null, page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<CodCollection>>(`/api/cod/collections${search ? `?${search}` : ""}`),
    enabled: options.enabled ?? true,
  });
}
