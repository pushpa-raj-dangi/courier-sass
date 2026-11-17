"use client";

import { useQueries } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Shipment } from "@/features/merchant-shipments/types";
import { EMPTY_MERCHANT_LIST_PARAMS, toMerchantApiQuery } from "@/features/merchant-shipments/lib/listParams";
import { MERCHANT_SHIPMENT_TABS, type MerchantShipmentTab } from "@/features/merchant-shipments/lib/statusGroups";

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
