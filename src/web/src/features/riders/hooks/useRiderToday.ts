"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { RiderToday } from "@/features/riders/types";

export function useRiderToday(riderId: string, enabled: boolean) {
  return useQuery({
    queryKey: ["riders", riderId, "today"],
    queryFn: () => clientFetch<RiderToday>(`/api/riders/${riderId}/today`),
    enabled: enabled && riderId !== "",
  });
}
