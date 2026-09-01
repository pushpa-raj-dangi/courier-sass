"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Adjustment, RequestAdjustmentInput } from "@/features/cod/types";

export function useRequestAdjustment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: RequestAdjustmentInput) =>
      clientFetch<Adjustment>("/api/cod/adjustments", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod", "adjustments"] });
    },
  });
}
