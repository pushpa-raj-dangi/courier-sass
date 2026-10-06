"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest } from "@/features/manifests/types";

export function useManifests(branchId?: string, status?: string, options: { enabled?: boolean } = {}) {
  const { enabled = true } = options;
  const params = new URLSearchParams();
  if (branchId) params.set("branchId", branchId);
  if (status) params.set("status", status);
  const query = params.toString();

  return useQuery({
    queryKey: ["manifests", branchId ?? null, status ?? null],
    queryFn: () => clientFetch<Manifest[]>(`/api/manifests${query ? `?${query}` : ""}`),
    enabled,
  });
}
