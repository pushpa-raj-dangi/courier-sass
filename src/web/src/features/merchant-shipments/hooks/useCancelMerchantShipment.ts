"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Shipment } from "@/features/merchant-shipments/types";

export function useCancelMerchantShipment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      clientFetch<Shipment>(`/api/merchant/shipments/${id}/cancel`, { method: "POST" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-shipments"] });
    },
  });
}
