"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import { generateIdempotencyKey } from "@/lib/api/idempotencyKey";
import type { PickupRequest } from "@/features/merchant-pickups/types";
import type { CreateMerchantPickupFormValues } from "@/features/merchant-pickups/schema";

export function useCreateMerchantPickup() {
  const queryClient = useQueryClient();
  return useMutation({
    // The backend's IdempotencyMiddleware requires this header for every merchant-JWT POST to
    // /api/v1/pickups — omitting it 400s with IDEMPOTENCY_KEY_REQUIRED on every pickup request
    // (found via Frontend Phase F10 Task 16's live E2E pass).
    mutationFn: (input: CreateMerchantPickupFormValues) =>
      clientFetch<PickupRequest>("/api/merchant/pickups", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Idempotency-Key": generateIdempotencyKey() },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-pickups"] });
    },
  });
}
