"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { addOutboxItem } from "@/features/rider-app/sync/outboxDb";
import { drainOutbox } from "@/features/rider-app/sync/syncManager";

export function useRiderPickupScan(riderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ awb, shipmentId }: { awb: string; shipmentId: string }) => {
      await addOutboxItem({ kind: "pickup-scan", riderId, shipmentId, awb });
      void drainOutbox();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["riders", "me", "tasks", "today"] });
    },
  });
}
