"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ServiceType } from "@/features/shipments/types";

export function useServiceTypes() {
  return useQuery({
    queryKey: ["service-types"],
    queryFn: () => clientFetch<ServiceType[]>("/api/pricing/service-types"),
  });
}
