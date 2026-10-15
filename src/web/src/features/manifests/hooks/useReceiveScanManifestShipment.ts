"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ManifestShipmentScan } from "@/features/manifests/types";

export function useReceiveScanManifestShipment(manifestId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (awb: string) =>
      clientFetch<ManifestShipmentScan>(`/api/manifests/${manifestId}/receive-scan`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ awb }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["manifests"] });
      queryClient.invalidateQueries({ queryKey: ["manifests-summary"] });
    },
  });
}
