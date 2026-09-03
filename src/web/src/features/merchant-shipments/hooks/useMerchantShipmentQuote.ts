"use client";

import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { QuoteResponse } from "@/features/merchant-shipments/types";

export interface MerchantQuoteInput {
  originBranchId: string;
  destinationBranchId: string;
  weightKg: number;
  serviceTypeId: string;
  codAmount: number;
  declaredValue: number;
  wantsInsurance: boolean;
}

export function useMerchantShipmentQuote(input: MerchantQuoteInput | null) {
  const [debounced, setDebounced] = useState<MerchantQuoteInput | null>(null);

  useEffect(() => {
    const timeout = setTimeout(() => setDebounced(input), 500);
    return () => clearTimeout(timeout);
  }, [input]);

  return useQuery({
    queryKey: ["merchant-shipment-quote", debounced],
    queryFn: () =>
      clientFetch<QuoteResponse>("/api/merchant/pricing/quote", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        // merchantId is always sent as null — the backend forces it to the caller's own
        // merchant id regardless of what's sent here, so there's nothing meaningful to supply
        // from a merchant-track caller.
        body: JSON.stringify({ ...debounced, merchantId: null }),
      }),
    enabled: debounced !== null,
  });
}
