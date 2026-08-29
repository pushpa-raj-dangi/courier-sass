"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export interface DeliveryZone {
  id: string;
  name: string;
  coveredAreaIds: string[];
}

export function useDeliveryZones() {
  return useQuery({
    queryKey: ["delivery-zones"],
    queryFn: () => clientFetch<DeliveryZone[]>("/api/delivery-zones"),
  });
}
