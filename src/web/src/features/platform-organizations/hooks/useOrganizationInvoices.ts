"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { OrganizationInvoice } from "@/features/platform-organizations/types";

export function useOrganizationInvoices(organizationId: string | null, page: number, pageSize: number) {
  return useQuery({
    queryKey: ["platform-organizations", organizationId, "invoices", page, pageSize],
    queryFn: () => clientFetch<PagedResult<OrganizationInvoice>>(`/api/platform/organizations/${organizationId}/invoices?page=${page}&pageSize=${pageSize}`),
    enabled: organizationId !== null,
  });
}
