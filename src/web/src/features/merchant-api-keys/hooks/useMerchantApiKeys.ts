"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ApiKey } from "@/features/merchant-api-keys/types";

export function useMerchantApiKeys() {
  return useQuery({
    queryKey: ["merchant-api-keys"],
    queryFn: () => clientFetch<ApiKey[]>("/api/merchant/api-keys"),
  });
}
