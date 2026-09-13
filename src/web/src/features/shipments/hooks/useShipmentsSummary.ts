"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useShipmentsSummary() {
  return useQuery({
    queryKey: ["shipments-summary"],
    queryFn: () => clientFetch<Record<string, number>>("/api/shipments/summary"),
  });
}
