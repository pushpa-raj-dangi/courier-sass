"use client";

import { useMutation } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useImpersonate() {
  return useMutation({
    mutationFn: ({ organizationId, userId }: { organizationId: string; userId: string }) =>
      clientFetch(`/api/platform/organizations/${organizationId}/users/${userId}/impersonate`, { method: "POST" }),
  });
}
