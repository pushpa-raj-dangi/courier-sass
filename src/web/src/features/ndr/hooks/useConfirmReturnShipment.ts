"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ReturnShipment } from "@/features/ndr/types";

export function useConfirmReturnShipment(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => clientFetch<ReturnShipment>(`/api/return-shipments/${id}/confirm`, { method: "POST" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["return-shipments"] });
    },
  });
}
