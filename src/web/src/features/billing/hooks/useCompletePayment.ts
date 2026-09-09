"use client";

import { useMutation } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useCompletePayment() {
  return useMutation({
    mutationFn: ({ paymentId, callbackParameters }: { paymentId: string; callbackParameters: Record<string, string> }) =>
      clientFetch<{ succeeded: boolean; message: string }>(`/api/billing/payments/${paymentId}/callback`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(callbackParameters),
      }),
  });
}
