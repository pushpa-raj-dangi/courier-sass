"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CodFinancialSummary } from "@/features/reports/types";

export function useCodSummary() {
  return useQuery({
    queryKey: ["cod-summary"],
    queryFn: () => clientFetch<CodFinancialSummary>("/api/cod/summary"),
  });
}
