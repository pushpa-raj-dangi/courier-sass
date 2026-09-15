"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Invoice } from "@/features/billing/types";

export function useBillingInvoices(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["billing-invoices"],
    queryFn: () => clientFetch<Invoice[]>("/api/billing/invoices"),
    enabled: options?.enabled,
  });
}
