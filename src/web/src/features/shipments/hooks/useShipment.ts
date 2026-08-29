"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Shipment } from "@/features/shipments/types";

export function useShipment(awb: string) {
  return useQuery({
    queryKey: ["shipments", awb],
    queryFn: () => clientFetch<Shipment>(`/api/shipments/${awb}`),
    enabled: awb !== "",
  });
}
