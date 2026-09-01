"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Rider, CreateRiderInput } from "@/features/riders/types";

export function useCreateRider() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateRiderInput) =>
      clientFetch<Rider>("/api/riders", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["riders"] });
    },
  });
}
