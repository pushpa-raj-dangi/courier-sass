"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Rider } from "@/features/riders/types";

export function useRiders() {
  return useQuery({
    queryKey: ["riders"],
    queryFn: () => clientFetch<Rider[]>("/api/riders"),
  });
}
