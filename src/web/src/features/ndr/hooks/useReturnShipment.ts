"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ReturnShipment } from "@/features/ndr/types";

export function useReturnShipment(id: string) {
  return useQuery({
    queryKey: ["return-shipments", id],
    queryFn: () => clientFetch<ReturnShipment>(`/api/return-shipments/${id}`),
    enabled: id !== "",
  });
}
