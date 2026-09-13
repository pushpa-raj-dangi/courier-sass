"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { OrganizationSettings } from "@/features/admin/settings/types";

export function useOrganizationSettings() {
  return useQuery({
    queryKey: ["organization-settings"],
    queryFn: () => clientFetch<OrganizationSettings>("/api/settings/organization"),
  });
}
