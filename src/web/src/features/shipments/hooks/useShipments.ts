"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Shipment } from "@/features/shipments/types";
import { toApiQuery, type ShipmentListParams } from "@/features/shipments/lib/listParams";

export function useShipments(params: ShipmentListParams, pageSize = 20) {
  const query = toApiQuery(params, pageSize).toString();
  return useQuery({
    queryKey: ["shipments", "list", query],
    queryFn: () => clientFetch<PagedResult<Shipment>>(`/api/shipments?${query}`),
    placeholderData: keepPreviousData,
  });
}
