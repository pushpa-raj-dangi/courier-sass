"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Shipment } from "@/features/merchant-shipments/types";
import type { CreateMerchantShipmentFormValues } from "@/features/merchant-shipments/schema";

export function useCreateMerchantShipment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateMerchantShipmentFormValues) =>
      clientFetch<Shipment>("/api/merchant/shipments", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-shipments"] });
    },
  });
}
