"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { BranchCashSettlement } from "@/features/cod/types";

export function useBranchSettlements(params: { branchId?: string; unverifiedOnly?: boolean; page?: number; pageSize?: number } = {}) {
  const { branchId, unverifiedOnly, page, pageSize } = params;
  const query = new URLSearchParams();
  if (branchId) query.set("branchId", branchId);
  if (unverifiedOnly) query.set("unverifiedOnly", "true");
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["cod", "branch-settlements", branchId ?? null, unverifiedOnly ?? null, page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<BranchCashSettlement>>(`/api/cod/branch-settlements${search ? `?${search}` : ""}`),
  });
}
