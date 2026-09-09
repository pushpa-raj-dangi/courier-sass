"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { AdminInvoice } from "@/features/platform-invoices/types";

export function usePlatformInvoices(status?: string) {
  return useQuery({
    queryKey: ["platform-invoices", status ?? "all"],
    queryFn: () => clientFetch<AdminInvoice[]>(`/api/platform/invoices${status ? `?status=${status}` : ""}`),
  });
}
