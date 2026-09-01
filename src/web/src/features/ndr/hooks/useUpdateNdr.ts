"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Ndr, UpdateNdrInput } from "@/features/ndr/types";

export function useUpdateNdr(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: UpdateNdrInput) =>
      clientFetch<Ndr>(`/api/ndrs/${id}/update`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["ndrs"] });
    },
  });
}
