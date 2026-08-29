"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Shipment, CreateShipmentInput } from "@/features/shipments/types";

export function useCreateShipment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateShipmentInput) =>
      clientFetch<Shipment>("/api/shipments", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["shipments"] });
    },
  });
}
