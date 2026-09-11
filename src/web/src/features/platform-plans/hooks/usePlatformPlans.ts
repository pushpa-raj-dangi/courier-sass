"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Plan } from "@/features/platform-plans/types";

const SELECT_PAGE_SIZE = 200; // PaginationDefaults.MaxPageSize — comfortably covers any real plan catalog

export function usePlatformPlans(search: string, page: number, pageSize: number) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (search) {
    params.set("search", search);
  }

  return useQuery({
    queryKey: ["platform-plans", "list", search, page, pageSize],
    queryFn: () => clientFetch<PagedResult<Plan>>(`/api/platform/plans?${params.toString()}`),
  });
}

// Unwraps pagination for callers that need the full active-plan catalog as a flat list — e.g. an
// "Assign Plan" dropdown, which must never silently omit a plan just because it's not on page 1.
export function usePlatformPlansForSelect() {
  const query = useQuery({
    queryKey: ["platform-plans", "select"],
    queryFn: () => clientFetch<PagedResult<Plan>>(`/api/platform/plans?page=1&pageSize=${SELECT_PAGE_SIZE}`),
  });
  return { ...query, data: query.data?.items };
}
