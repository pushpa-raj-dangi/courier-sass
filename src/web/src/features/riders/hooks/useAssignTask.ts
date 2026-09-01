"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { AssignDeliveryTaskInput } from "@/features/riders/types";

export function useAssignTask() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: AssignDeliveryTaskInput) =>
      clientFetch<{ id: string }>("/api/riders/tasks", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["shipments"] });
    },
  });
}
