"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useManifestsSummary() {
  return useQuery({
    queryKey: ["manifests-summary"],
    queryFn: () => clientFetch<Record<string, number>>("/api/manifests/summary"),
  });
}
