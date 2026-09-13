"use client";

import { useMutation } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useRevokeAllSessions() {
  return useMutation({
    mutationFn: () => clientFetch<void>("/api/settings/organization/revoke-sessions", { method: "POST" }),
  });
}
