"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ManifestShipmentScan } from "@/features/manifests/types";

// Under ["manifests", id, …] so receive-scan and close (which invalidate ["manifests"]) refresh it.
export function useManifestShipments(manifestId: string, enabled: boolean) {
  return useQuery({
    queryKey: ["manifests", manifestId, "shipments"],
    queryFn: () => clientFetch<ManifestShipmentScan[]>(`/api/manifests/${manifestId}/shipments`),
    enabled: enabled && manifestId !== "",
  });
}
