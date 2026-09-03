"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PickupRequest } from "@/features/merchant-pickups/types";
import type { CreateMerchantPickupFormValues } from "@/features/merchant-pickups/schema";

export function useCreateMerchantPickup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateMerchantPickupFormValues) =>
      clientFetch<PickupRequest>("/api/merchant/pickups", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-pickups"] });
    },
  });
}
