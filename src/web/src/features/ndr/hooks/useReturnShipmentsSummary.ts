"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export interface ReturnShipmentSummary {
  byRtoStage: Record<string, number>;
  merchantConfirmed: number;
  pendingConfirmation: number;
}

export function useReturnShipmentsSummary(options: { enabled?: boolean } = {}) {
  const { enabled = true } = options;
  return useQuery({
    queryKey: ["return-shipments-summary"],
    queryFn: () => clientFetch<ReturnShipmentSummary>("/api/return-shipments/summary"),
    enabled,
  });
}
