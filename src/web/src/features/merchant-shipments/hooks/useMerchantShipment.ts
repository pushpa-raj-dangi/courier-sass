"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Shipment } from "@/features/merchant-shipments/types";

export function useMerchantShipment(awb: string) {
  return useQuery({
    queryKey: ["merchant-shipments", awb],
    queryFn: () => clientFetch<Shipment>(`/api/merchant/shipments/${awb}`),
    enabled: awb !== "",
  });
}
