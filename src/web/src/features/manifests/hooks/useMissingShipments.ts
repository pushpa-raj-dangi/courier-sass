"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MissingShipment } from "@/features/manifests/types";

export function useMissingShipments(manifestId: string, enabled: boolean) {
  return useQuery({
    queryKey: ["manifests", manifestId, "missing-shipments"],
    queryFn: () => clientFetch<MissingShipment[]>(`/api/manifests/${manifestId}/missing-shipments`),
    enabled: enabled && manifestId !== "",
  });
}
