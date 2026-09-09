"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { OrganizationSummary } from "@/features/platform-organizations/types";

export function usePlatformOrganizations() {
  return useQuery({
    queryKey: ["platform-organizations"],
    queryFn: () => clientFetch<OrganizationSummary[]>("/api/platform/organizations"),
  });
}
