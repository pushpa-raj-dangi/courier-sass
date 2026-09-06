"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Shipment } from "@/features/shipments/types";

export function useReceiveAtOrigin(shipmentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () =>
      clientFetch<Shipment>(`/api/shipments/${shipmentId}/receive-origin`, {
        method: "POST",
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["shipments"] });
    },
  });
}
