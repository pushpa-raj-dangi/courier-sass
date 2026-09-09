"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

// Every lifecycle action is a bare POST with no body — the backend derives everything
// it needs (which organization, who performed it) from the route param and the caller's
// JWT. Never add a client-computed field here (CLAUDE.md: never trust amounts/roles from
// the frontend applies equally to lifecycle state — the server is the sole authority).
function useLifecycleAction(action: "suspend" | "reactivate" | "delete" | "restore") {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (organizationId: string) =>
      clientFetch(`/api/platform/organizations/${organizationId}/${action}`, { method: "POST" }),
    onSuccess: () => {
      // Prefix match invalidates both ["platform-organizations"] (the list) and every
      // ["platform-organizations", id] detail query.
      queryClient.invalidateQueries({ queryKey: ["platform-organizations"] });
    },
  });
}

export function useSuspendOrganization() {
  return useLifecycleAction("suspend");
}
export function useReactivateOrganization() {
  return useLifecycleAction("reactivate");
}
export function useDeleteOrganization() {
  return useLifecycleAction("delete");
}
export function useRestoreOrganization() {
  return useLifecycleAction("restore");
}
