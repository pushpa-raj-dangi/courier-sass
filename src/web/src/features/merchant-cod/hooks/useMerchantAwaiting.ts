"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { MerchantAwaitingCollection } from "@/features/merchant-cod/types";

export function useMerchantAwaiting(page: number, pageSize = 20) {
  return useQuery({
    queryKey: ["merchant-cod-awaiting", page, pageSize],
    queryFn: () =>
      clientFetch<PagedResult<MerchantAwaitingCollection>>(
        `/api/merchant/cod/awaiting?page=${page}&pageSize=${pageSize}`
      ),
  });
}
