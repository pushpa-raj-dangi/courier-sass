"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { RiderCash } from "@/features/cod/types";

export function useRiderCash(params: { branchId?: string } = {}, options: { enabled?: boolean } = {}) {
  const { branchId } = params;
  const search = branchId ? `?branchId=${encodeURIComponent(branchId)}` : "";
  return useQuery({
    queryKey: ["cod", "rider-cash", branchId ?? null],
    queryFn: () => clientFetch<RiderCash>(`/api/cod/rider-cash${search}`),
    enabled: options.enabled ?? true,
  });
}
