"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ManifestShipmentScan } from "@/features/manifests/types";

export function useScanManifestShipment(manifestId: string, purpose: "Forward" | "Return") {
  const queryClient = useQueryClient();
  const path = purpose === "Return" ? "rto-scan" : "scan";
  return useMutation({
    mutationFn: (awb: string) =>
      clientFetch<ManifestShipmentScan>(`/api/manifests/${manifestId}/${path}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ awb }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["manifests"] });
    },
  });
}
