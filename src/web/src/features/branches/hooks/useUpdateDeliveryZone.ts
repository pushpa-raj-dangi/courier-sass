"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Branch } from "@/features/branches/types";

export function useUpdateDeliveryZone(branchId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (deliveryZoneId: string) =>
      clientFetch<Branch>(`/api/branches/${branchId}/delivery-zone`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ deliveryZoneId }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["branches", branchId] });
    },
  });
}
