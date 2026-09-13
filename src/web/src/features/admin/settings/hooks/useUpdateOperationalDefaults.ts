"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { OperationalDefaults } from "@/features/admin/settings/types";

export function useUpdateOperationalDefaults() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: OperationalDefaults) =>
      clientFetch<OperationalDefaults>("/api/settings/organization/defaults", {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["organization-settings"] }),
  });
}
