"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { UserBranchGrant } from "@/features/admin/types";

export function useUserBranchGrants(userId: string) {
  return useQuery({
    queryKey: ["admin", "users", userId, "branches"],
    queryFn: () => clientFetch<UserBranchGrant[]>(`/api/users/${userId}/branches`),
    enabled: userId.length > 0,
  });
}
