"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ShipmentEvent } from "@/features/shipments/types";

export function useShipmentEvents(awb: string) {
  return useQuery({
    queryKey: ["shipments", awb, "events"],
    queryFn: () => clientFetch<ShipmentEvent[]>(`/api/shipments/${encodeURIComponent(awb)}/events`),
    enabled: awb !== "",
  });
}
