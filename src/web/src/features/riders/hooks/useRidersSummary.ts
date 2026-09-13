"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export interface RiderSummary {
  total: number;
  active: number;
  inactive: number;
  currentlyAssigned: number;
}

export function useRidersSummary() {
  return useQuery({
    queryKey: ["riders-summary"],
    queryFn: () => clientFetch<RiderSummary>("/api/riders/summary"),
  });
}
