"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PickupRequest } from "@/features/merchant-pickups/types";

export function useCancelMerchantPickup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      clientFetch<PickupRequest>(`/api/merchant/pickups/${id}/cancel`, { method: "POST" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-pickups"] });
    },
  });
}
