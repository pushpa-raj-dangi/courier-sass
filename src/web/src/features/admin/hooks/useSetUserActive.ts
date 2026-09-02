"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useSetUserActive(userId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (active: boolean) =>
      clientFetch<void>(`/api/users/${userId}/${active ? "reactivate" : "deactivate"}`, { method: "PATCH" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["admin", "users"] }),
  });
}
