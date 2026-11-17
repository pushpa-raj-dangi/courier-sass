"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Shipment } from "@/features/merchant-shipments/types";
import { toMerchantApiQuery, type MerchantShipmentListParams } from "@/features/merchant-shipments/lib/listParams";

export function useMerchantShipments(params: MerchantShipmentListParams, pageSize = 20) {
  const query = toMerchantApiQuery(params, pageSize).toString();
  return useQuery({
    queryKey: ["merchant-shipments", query],
    queryFn: () => clientFetch<PagedResult<Shipment>>(`/api/merchant/shipments?${query}`),
    placeholderData: keepPreviousData,
  });
}
