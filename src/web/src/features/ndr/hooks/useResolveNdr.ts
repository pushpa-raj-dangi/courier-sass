"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Ndr, ResolveNdrInput } from "@/features/ndr/types";

export function useResolveNdr(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: ResolveNdrInput) =>
      clientFetch<Ndr>(`/api/ndrs/${id}/resolve`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["ndrs"] });
      queryClient.invalidateQueries({ queryKey: ["ndrs-summary"] });
      queryClient.invalidateQueries({ queryKey: ["return-shipments"] });
      queryClient.invalidateQueries({ queryKey: ["return-shipments-summary"] });
    },
  });
}
