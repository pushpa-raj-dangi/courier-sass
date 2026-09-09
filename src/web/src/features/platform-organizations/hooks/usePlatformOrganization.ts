"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { OrganizationDetail } from "@/features/platform-organizations/types";

export function usePlatformOrganization(organizationId: string | null) {
  return useQuery({
    queryKey: ["platform-organizations", organizationId],
    queryFn: () => clientFetch<OrganizationDetail>(`/api/platform/organizations/${organizationId}`),
    enabled: organizationId !== null,
  });
}
