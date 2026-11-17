"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantShipmentCodStatus } from "@/features/merchant-cod/types";

export function useMerchantShipmentCodStatus(shipmentId: string | undefined) {
  return useQuery({
    queryKey: ["merchant-cod-shipment-status", shipmentId],
    queryFn: () => clientFetch<MerchantShipmentCodStatus>(`/api/merchant/cod/shipments/${shipmentId}/status`),
    enabled: !!shipmentId,
  });
}
