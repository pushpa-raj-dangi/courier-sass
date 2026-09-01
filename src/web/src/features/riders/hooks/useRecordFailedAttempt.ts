"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { DeliveryAttemptResult, RecordFailedAttemptInput } from "@/features/riders/types";

export function useRecordFailedAttempt(riderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: RecordFailedAttemptInput) =>
      clientFetch<DeliveryAttemptResult>(`/api/riders/${riderId}/delivery-attempts/fail`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["riders", "me", "tasks", "today"] });
    },
  });
}
