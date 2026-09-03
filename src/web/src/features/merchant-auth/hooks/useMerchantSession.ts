"use client";

import { useQuery } from "@tanstack/react-query";
import type { MerchantMeResponse } from "@/services/merchantAuthService";

async function fetchMerchantSession(): Promise<MerchantMeResponse | null> {
  const response = await fetch("/api/merchant-auth/session");
  if (response.status === 401) {
    return null;
  }
  if (!response.ok) {
    throw new Error("MERCHANT_SESSION_FETCH_FAILED");
  }
  return response.json();
}

export function useMerchantSession() {
  return useQuery({
    queryKey: ["merchant-session"],
    queryFn: fetchMerchantSession,
    staleTime: 60_000,
    retry: false,
  });
}
