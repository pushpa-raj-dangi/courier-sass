"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useCompletePayment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ paymentId, callbackParameters }: { paymentId: string; callbackParameters: Record<string, string> }) =>
      clientFetch<{ succeeded: boolean; message: string }>(`/api/billing/payments/${paymentId}/callback`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(callbackParameters),
      }),
    // The eSewa/Khalti round trip is a full browser navigation away and back, so AppShell (and
    // its TrialStatusBanner, reading the "billing-status" query below) survives it unmounted —
    // without this invalidation it keeps showing whatever trial/paywall state it fetched at that
    // one mount, even after this mutation confirms the org is now paid. "billing-subscription"/
    // "billing-invoices" are invalidated too so /billing itself is never stale if reached via a
    // client-side navigation rather than the hard reload this flow normally produces.
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["billing-status"] });
      queryClient.invalidateQueries({ queryKey: ["billing-subscription"] });
      queryClient.invalidateQueries({ queryKey: ["billing-invoices"] });
    },
  });
}
