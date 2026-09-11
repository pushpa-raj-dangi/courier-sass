"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { OrganizationUser } from "@/features/platform-organizations/types";

export function useOrganizationUsers(organizationId: string | null, page: number, pageSize: number) {
  return useQuery({
    queryKey: ["platform-organizations", organizationId, "users", page, pageSize],
    queryFn: () => clientFetch<PagedResult<OrganizationUser>>(`/api/platform/organizations/${organizationId}/users?page=${page}&pageSize=${pageSize}`),
    enabled: organizationId !== null,
  });
}
