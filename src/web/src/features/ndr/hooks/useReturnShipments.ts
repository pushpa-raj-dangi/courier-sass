"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ReturnShipment } from "@/features/ndr/types";

export function useReturnShipments(originalShipmentId?: string) {
  const query = new URLSearchParams();
  if (originalShipmentId) query.set("originalShipmentId", originalShipmentId);
  const search = query.toString();

  return useQuery({
    queryKey: ["return-shipments", originalShipmentId ?? null],
    queryFn: () => clientFetch<ReturnShipment[]>(`/api/return-shipments${search ? `?${search}` : ""}`),
  });
}
