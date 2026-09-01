"use client";

import { useMutation } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { UploadPodFileResult } from "@/features/riders/types";

export function usePodUpload(riderId: string) {
  return useMutation({
    mutationFn: ({ kind, blob }: { kind: "Signature" | "Photo"; blob: Blob }) =>
      clientFetch<UploadPodFileResult>(`/api/riders/${riderId}/pod-uploads?kind=${kind}`, {
        method: "POST",
        headers: { "Content-Type": blob.type || "application/octet-stream" },
        body: blob,
      }),
  });
}
