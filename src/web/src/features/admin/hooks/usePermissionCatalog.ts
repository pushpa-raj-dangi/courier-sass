"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PermissionGroup } from "@/features/admin/types";

export function usePermissionCatalog() {
  return useQuery({
    queryKey: ["admin", "permissions"],
    queryFn: () => clientFetch<PermissionGroup[]>("/api/permissions"),
  });
}
