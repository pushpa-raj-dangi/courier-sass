"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Rider } from "@/features/riders/types";

export function useRider(id: string) {
  return useQuery({
    queryKey: ["riders", id],
    queryFn: () => clientFetch<Rider>(`/api/riders/${id}`),
    enabled: id !== "",
  });
}
