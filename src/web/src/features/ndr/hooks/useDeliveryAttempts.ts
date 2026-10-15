"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { DeliveryAttempt } from "@/features/ndr/types";

export function useDeliveryAttempts(shipmentId: string) {
  return useQuery({
    queryKey: ["delivery-attempts", shipmentId],
    queryFn: () => clientFetch<{ items: DeliveryAttempt[] }>(`/api/shipments/${encodeURIComponent(shipmentId)}/delivery-attempts`),
    enabled: shipmentId !== "",
  });
}
