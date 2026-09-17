"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useAssignPlan() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (planId: string) =>
      clientFetch("/api/billing/plan", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ planId }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["billing-subscription"] });
      queryClient.invalidateQueries({ queryKey: ["billing-plan"] });
      queryClient.invalidateQueries({ queryKey: ["billing-invoices"] });
      // AppShell's TrialStatusBanner (across every staff page, not just /billing) reads this key
      // — without invalidating it, picking a plan mid-trial leaves the "trial ending" banner
      // showing stale even though the org is now Active, since PlanPicker never navigates/remounts.
      queryClient.invalidateQueries({ queryKey: ["billing-status"] });
    },
  });
}
