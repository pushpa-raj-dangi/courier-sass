"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { RiderBoard } from "@/features/riders/types";

export function useRiderBoard(branchId?: string, options: { enabled?: boolean } = {}) {
  const search = branchId ? `?branchId=${encodeURIComponent(branchId)}` : "";
  return useQuery({
    queryKey: ["riders", "board", branchId ?? null],
    queryFn: () => clientFetch<RiderBoard>(`/api/riders/board${search}`),
    enabled: options.enabled ?? true,
  });
}
