"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { QueueSummary } from "@/features/platform-queues/types";

export function usePlatformQueues() {
  return useQuery({
    queryKey: ["platform-queues", "list"],
    queryFn: () => clientFetch<QueueSummary[]>("/api/platform/queues"),
    refetchInterval: 10000,
  });
}
