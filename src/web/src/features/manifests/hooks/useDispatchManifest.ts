"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest, DispatchManifestInput } from "@/features/manifests/types";

export function useDispatchManifest(manifestId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: DispatchManifestInput) =>
      clientFetch<Manifest>(`/api/manifests/${manifestId}/dispatch`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["manifests", manifestId] });
    },
  });
}
