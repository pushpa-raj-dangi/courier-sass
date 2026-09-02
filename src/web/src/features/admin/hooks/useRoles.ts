"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Role } from "@/features/admin/types";

export function useRoles() {
  return useQuery({
    queryKey: ["admin", "roles"],
    queryFn: () => clientFetch<Role[]>("/api/roles"),
  });
}
