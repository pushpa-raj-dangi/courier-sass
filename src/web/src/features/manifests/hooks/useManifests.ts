"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest, ManifestDirection } from "@/features/manifests/types";

export function useManifests(
  branchId?: string,
  status?: string,
  options: { enabled?: boolean; direction?: ManifestDirection } = {},
) {
  const { enabled = true, direction } = options;
  const params = new URLSearchParams();
  if (branchId) params.set("branchId", branchId);
  if (status) params.set("status", status);
  if (direction) params.set("direction", direction);
  const query = params.toString();

  return useQuery({
    queryKey: ["manifests", branchId ?? null, status ?? null, direction ?? null],
    queryFn: () => clientFetch<Manifest[]>(`/api/manifests${query ? `?${query}` : ""}`),
    enabled,
  });
}
