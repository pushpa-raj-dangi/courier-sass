"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Adjustment } from "@/features/cod/types";

export function useApproveAdjustment(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => clientFetch<Adjustment>(`/api/cod/adjustments/${id}/approve`, { method: "POST" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod", "adjustments"] });
      queryClient.invalidateQueries({ queryKey: ["cod", "merchant-settlements"] });
    },
  });
}
