"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest } from "@/features/manifests/types";

export function useSealManifest(manifestId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => clientFetch<Manifest>(`/api/manifests/${manifestId}/seal`, { method: "POST" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["manifests"] });
      queryClient.invalidateQueries({ queryKey: ["manifests-summary"] });
    },
  });
}
