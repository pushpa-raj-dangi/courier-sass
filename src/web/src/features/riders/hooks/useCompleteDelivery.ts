"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CompleteDeliveryInput, DeliveryAttemptResult } from "@/features/riders/types";

export function useCompleteDelivery(riderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CompleteDeliveryInput) =>
      clientFetch<DeliveryAttemptResult>(`/api/riders/${riderId}/delivery-attempts/complete`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["riders", "me", "tasks", "today"] });
    },
  });
}
