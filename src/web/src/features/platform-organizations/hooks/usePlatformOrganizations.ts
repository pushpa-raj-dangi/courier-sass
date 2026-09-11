"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { OrganizationSummary } from "@/features/platform-organizations/types";

export type OrganizationStatusFilter = "all" | "active" | "suspended" | "deleted";

export function usePlatformOrganizations(search: string, status: OrganizationStatusFilter, page: number, pageSize: number) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (search) {
    params.set("search", search);
  }
  // A single "status" filter maps to the backend's two independent isActive/isDeleted params:
  // Active = isActive true & not deleted; Suspended = isActive false & not deleted; Deleted =
  // isDeleted true (isActive is always false for a deleted org, so it doesn't need to be sent).
  if (status === "active") {
    params.set("isActive", "true");
    params.set("isDeleted", "false");
  } else if (status === "suspended") {
    params.set("isActive", "false");
    params.set("isDeleted", "false");
  } else if (status === "deleted") {
    params.set("isDeleted", "true");
  }

  return useQuery({
    queryKey: ["platform-organizations", "list", search, status, page, pageSize],
    queryFn: () => clientFetch<PagedResult<OrganizationSummary>>(`/api/platform/organizations?${params.toString()}`),
  });
}
