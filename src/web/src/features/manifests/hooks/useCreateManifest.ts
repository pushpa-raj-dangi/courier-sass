"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest, CreateManifestInput } from "@/features/manifests/types";

export function useCreateManifest() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateManifestInput) =>
      clientFetch<Manifest>("/api/manifests", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["manifests"] });
      queryClient.invalidateQueries({ queryKey: ["manifests-summary"] });
    },
  });
}
