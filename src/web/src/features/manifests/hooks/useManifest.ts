"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest } from "@/features/manifests/types";

export function useManifest(id: string) {
  return useQuery({
    queryKey: ["manifests", id],
    queryFn: () => clientFetch<Manifest>(`/api/manifests/${id}`),
    enabled: id !== "",
  });
}
