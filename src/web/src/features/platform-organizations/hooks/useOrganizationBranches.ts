"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { OrganizationBranch } from "@/features/platform-organizations/types";

export function useOrganizationBranches(organizationId: string | null, page: number, pageSize: number) {
  return useQuery({
    queryKey: ["platform-organizations", organizationId, "branches", page, pageSize],
    queryFn: () => clientFetch<PagedResult<OrganizationBranch>>(`/api/platform/organizations/${organizationId}/branches?page=${page}&pageSize=${pageSize}`),
    enabled: organizationId !== null,
  });
}
