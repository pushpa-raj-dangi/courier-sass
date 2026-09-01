"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Ndr } from "@/features/ndr/types";

export function useNdr(id: string) {
  return useQuery({
    queryKey: ["ndrs", id],
    queryFn: () => clientFetch<Ndr>(`/api/ndrs/${id}`),
    enabled: id !== "",
  });
}
