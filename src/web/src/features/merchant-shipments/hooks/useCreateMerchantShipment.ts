"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import { generateIdempotencyKey } from "@/lib/api/idempotencyKey";
import type { Shipment } from "@/features/merchant-shipments/types";
import type { CreateMerchantShipmentFormValues } from "@/features/merchant-shipments/schema";

export function useCreateMerchantShipment() {
  const queryClient = useQueryClient();
  return useMutation({
    // The backend's IdempotencyMiddleware requires this header for every merchant-JWT POST to
    // /api/v1/shipments — omitting it 400s with IDEMPOTENCY_KEY_REQUIRED on every booking attempt
    // (found via Frontend Phase F10 Task 16's live E2E pass; staff callers are exempt, which is
    // why this was never caught by the staff ShipmentCreateSheet's own testing).
    mutationFn: (input: CreateMerchantShipmentFormValues) =>
      clientFetch<Shipment>("/api/merchant/shipments", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Idempotency-Key": generateIdempotencyKey() },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-shipments"] });
    },
  });
}
