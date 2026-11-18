"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PickupRequest } from "@/features/merchant-pickups/types";

export function useCancelMerchantPickup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      clientFetch<PickupRequest>(`/api/merchant/pickups/${id}/cancel`, { method: "POST" }),
    // On settle, not just success: a 400 ("a rider already collected") means the list is stale.
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-pickups"] });
    },
  });
}
