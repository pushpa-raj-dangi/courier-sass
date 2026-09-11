"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { AdminInvoice } from "@/features/platform-invoices/types";

export function usePlatformInvoices(status: string, search: string, page: number, pageSize: number) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (status && status !== "all") {
    params.set("status", status);
  }
  if (search) {
    params.set("search", search);
  }

  return useQuery({
    queryKey: ["platform-invoices", "list", status, search, page, pageSize],
    queryFn: () => clientFetch<PagedResult<AdminInvoice>>(`/api/platform/invoices?${params.toString()}`),
  });
}
