"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useRevokeBranch(userId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (branchId: string) => clientFetch<void>(`/api/users/${userId}/branches/${branchId}`, { method: "DELETE" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["admin", "users", userId, "branches"] }),
  });
}
