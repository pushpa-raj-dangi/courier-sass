"use client";

import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { QuoteInput, QuoteResponse } from "@/features/shipments/types";

export function useShipmentQuote(input: QuoteInput | null) {
  const [debounced, setDebounced] = useState<QuoteInput | null>(null);

  useEffect(() => {
    const timeout = setTimeout(() => setDebounced(input), 500);
    return () => clearTimeout(timeout);
  }, [input]);

  return useQuery({
    queryKey: ["shipment-quote", debounced],
    queryFn: () =>
      clientFetch<QuoteResponse>("/api/pricing/quote", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(debounced),
      }),
    enabled: debounced !== null,
  });
}
