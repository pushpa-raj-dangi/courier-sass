"use client";

import { useMutation } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { GatewayCheckoutInfo } from "@/features/billing/types";

export function usePayInvoice() {
  return useMutation({
    mutationFn: ({ invoiceId, gateway }: { invoiceId: string; gateway: "Esewa" | "Khalti" }) =>
      clientFetch<GatewayCheckoutInfo>(`/api/billing/invoices/${invoiceId}/pay`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ gateway, returnBaseUrl: `${window.location.origin}/billing/callback` }),
      }),
  });
}
