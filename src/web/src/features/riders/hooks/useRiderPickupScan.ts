"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useRiderPickupScan(riderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (awb: string) =>
      clientFetch<{ id: string; currentStatus: string }>(`/api/riders/${riderId}/scan/pickup`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ awb }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["riders", "me", "tasks", "today"] });
    },
  });
}
