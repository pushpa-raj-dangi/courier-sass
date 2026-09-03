"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Shipment } from "@/features/merchant-shipments/types";

export function useMerchantShipments(page: number, pageSize: number) {
  return useQuery({
    queryKey: ["merchant-shipments", page, pageSize],
    queryFn: () =>
      clientFetch<PagedResult<Shipment>>(`/api/merchant/shipments?page=${page}&pageSize=${pageSize}`),
  });
}
