"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Shipment } from "@/features/shipments/types";

export function useShipments(page: number, pageSize: number) {
  return useQuery({
    queryKey: ["shipments", page, pageSize],
    queryFn: () =>
      clientFetch<PagedResult<Shipment>>(`/api/shipments?page=${page}&pageSize=${pageSize}`),
  });
}
