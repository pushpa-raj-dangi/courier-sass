"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Branch } from "@/features/merchant-shipments/types";

export function useMerchantBranches() {
  return useQuery({
    queryKey: ["merchant-branches"],
    queryFn: () => clientFetch<Branch[]>("/api/merchant/branches"),
  });
}
