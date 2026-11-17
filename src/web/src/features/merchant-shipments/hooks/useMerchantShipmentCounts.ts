"use client";

import { useQueries, useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Shipment } from "@/features/merchant-shipments/types";
import { EMPTY_MERCHANT_LIST_PARAMS, toMerchantApiQuery } from "@/features/merchant-shipments/lib/listParams";
import {
  ACTIVE_RETURN_STATUSES,
  MERCHANT_SHIPMENT_TABS,
  type MerchantShipmentTab,
} from "@/features/merchant-shipments/lib/statusGroups";

export type MerchantShipmentCounts = Record<MerchantShipmentTab, { count: number | null; isLoading: boolean }>;

export const merchantShipmentCountKey = (tab: MerchantShipmentTab) => ["merchant-shipments", "count", tab] as const;

export function fetchMerchantShipmentCount(tab: MerchantShipmentTab): Promise<number> {
  const query = toMerchantApiQuery({ ...EMPTY_MERCHANT_LIST_PARAMS, tab }, 1).toString();
  return clientFetch<PagedResult<Shipment>>(`/api/merchant/shipments?${query}`).then((r) => r.totalCount);
}

/** One pageSize=1 request per tab; count is the total, null when that request failed. */
export function useMerchantShipmentCounts(): MerchantShipmentCounts {
  const results = useQueries({
    queries: MERCHANT_SHIPMENT_TABS.map((tab) => ({
      queryKey: merchantShipmentCountKey(tab),
      queryFn: () => fetchMerchantShipmentCount(tab),
    })),
  });
  return Object.fromEntries(
    MERCHANT_SHIPMENT_TABS.map((tab, i) => [
      tab,
      { count: results[i].isError ? null : (results[i].data ?? null), isLoading: results[i].isLoading },
    ])
  ) as MerchantShipmentCounts;
}

export const merchantActiveReturnsCountKey = ["merchant-shipments", "count", "activeReturns"] as const;

export function fetchMerchantActiveReturnsCount(): Promise<number> {
  const api = new URLSearchParams({ page: "1", pageSize: "1" });
  ACTIVE_RETURN_STATUSES.forEach((s) => api.append("status", s));
  return clientFetch<PagedResult<Shipment>>(`/api/merchant/shipments?${api.toString()}`).then((r) => r.totalCount);
}

/** Returns still in flight (excludes RtoDelivered); null when the request failed. Dashboard attention row only. */
export function useMerchantActiveReturnsCount(): { count: number | null; isLoading: boolean } {
  const q = useQuery({ queryKey: merchantActiveReturnsCountKey, queryFn: fetchMerchantActiveReturnsCount });
  return { count: q.isError ? null : (q.data ?? null), isLoading: q.isLoading };
}
