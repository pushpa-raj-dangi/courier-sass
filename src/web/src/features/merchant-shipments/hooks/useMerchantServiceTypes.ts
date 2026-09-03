"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ServiceType } from "@/features/merchant-shipments/types";

export function useMerchantServiceTypes() {
  return useQuery({
    queryKey: ["merchant-service-types"],
    queryFn: () => clientFetch<ServiceType[]>("/api/merchant/service-types"),
  });
}
