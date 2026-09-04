"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { addOutboxItem } from "@/features/rider-app/sync/outboxDb";
import { drainOutbox } from "@/features/rider-app/sync/syncManager";
import type { RecordFailedAttemptFields } from "@/features/rider-app/sync/types";

export function useRecordFailedAttempt(riderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: RecordFailedAttemptFields) => {
      await addOutboxItem({ kind: "delivery-fail", riderId, shipmentId: input.shipmentId, input });
      void drainOutbox();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["riders", "me", "tasks", "today"] });
    },
  });
}
