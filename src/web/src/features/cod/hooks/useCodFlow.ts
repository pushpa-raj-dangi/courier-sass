"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CodFlow } from "@/features/cod/types";

export function useCodFlow(params: { branchId?: string; from?: string; to?: string } = {}, options: { enabled?: boolean } = {}) {
  const { branchId, from, to } = params;
  const query = new URLSearchParams();
  if (branchId) query.set("branchId", branchId);
  if (from) query.set("from", from);
  if (to) query.set("to", to);
  const search = query.toString();

  return useQuery({
    queryKey: ["cod", "flow", branchId ?? null, from ?? null, to ?? null],
    queryFn: () => clientFetch<CodFlow>(`/api/cod/flow${search ? `?${search}` : ""}`),
    enabled: options.enabled ?? true,
  });
}
