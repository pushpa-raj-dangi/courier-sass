"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { RiderCashSettlement } from "@/features/cod/types";

export function useRiderSettlements(params: { branchId?: string; riderId?: string; unreconciledOnly?: boolean; page?: number; pageSize?: number } = {}) {
  const { branchId, riderId, unreconciledOnly, page, pageSize } = params;
  const query = new URLSearchParams();
  if (branchId) query.set("branchId", branchId);
  if (riderId) query.set("riderId", riderId);
  if (unreconciledOnly) query.set("unreconciledOnly", "true");
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["cod", "rider-settlements", branchId ?? null, riderId ?? null, unreconciledOnly ?? null, page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<RiderCashSettlement>>(`/api/cod/rider-settlements${search ? `?${search}` : ""}`),
  });
}
