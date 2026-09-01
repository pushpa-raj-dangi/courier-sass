"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CodCollection } from "@/features/cod/types";

export function useCodCollection(id: string) {
  return useQuery({
    queryKey: ["cod", "collections", id],
    queryFn: () => clientFetch<CodCollection>(`/api/cod/collections/${id}`),
    enabled: id.length > 0,
  });
}
