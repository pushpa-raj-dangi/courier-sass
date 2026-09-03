"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantKycDocument } from "@/features/merchant-kyc/types";

export function useMerchantKycDocuments() {
  return useQuery({
    queryKey: ["merchant-kyc-documents"],
    queryFn: () => clientFetch<MerchantKycDocument[]>("/api/merchant/kyc-documents"),
  });
}
