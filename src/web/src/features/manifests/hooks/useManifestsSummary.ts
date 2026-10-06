"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useManifestsSummary(options: { enabled?: boolean } = {}) {
  const { enabled = true } = options;
  return useQuery({
    queryKey: ["manifests-summary"],
    queryFn: () => clientFetch<Record<string, number>>("/api/manifests/summary"),
    enabled,
  });
}
