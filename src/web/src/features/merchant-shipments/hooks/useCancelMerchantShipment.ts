"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import { generateIdempotencyKey } from "@/lib/api/idempotencyKey";
import type { Shipment } from "@/features/merchant-shipments/types";

export function useCancelMerchantShipment() {
  const queryClient = useQueryClient();
  return useMutation({
    // The backend's IdempotencyMiddleware requires this header for every merchant-JWT POST to
    // /api/v1/shipments/{id}/cancel — omitting it 400s with IDEMPOTENCY_KEY_REQUIRED on every
    // cancel attempt (found via Frontend Phase F10 Task 16's live E2E pass).
    mutationFn: (id: string) =>
      clientFetch<Shipment>(`/api/merchant/shipments/${id}/cancel`, {
        method: "POST",
        headers: { "Idempotency-Key": generateIdempotencyKey() },
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-shipments"] });
    },
  });
}
