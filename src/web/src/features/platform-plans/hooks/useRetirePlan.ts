"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useRetirePlan() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (planId: string) => clientFetch(`/api/platform/plans/${planId}/retire`, { method: "POST" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["platform-plans"] }),
  });
}
