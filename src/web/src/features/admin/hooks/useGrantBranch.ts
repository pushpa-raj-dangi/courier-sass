"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { UserBranchGrant, GrantBranchInput } from "@/features/admin/types";

export function useGrantBranch(userId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: GrantBranchInput) =>
      clientFetch<UserBranchGrant>(`/api/users/${userId}/branches`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["admin", "users", userId, "branches"] }),
  });
}
