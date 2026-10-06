"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useNdrsSummary(options: { enabled?: boolean } = {}) {
  const { enabled = true } = options;
  return useQuery({
    queryKey: ["ndrs-summary"],
    queryFn: () => clientFetch<Record<string, number>>("/api/ndrs/summary"),
    enabled,
  });
}
