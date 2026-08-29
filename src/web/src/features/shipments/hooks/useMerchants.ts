"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Merchant } from "@/features/shipments/types";

export function useMerchants() {
  return useQuery({
    queryKey: ["merchants"],
    queryFn: () => clientFetch<Merchant[]>("/api/merchants"),
  });
}
