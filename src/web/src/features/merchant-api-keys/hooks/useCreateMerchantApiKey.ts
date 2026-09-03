"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ApiKeyCreated } from "@/features/merchant-api-keys/types";

export function useCreateMerchantApiKey() {
  const queryClient = useQueryClient();
  return useMutation({
    // null scopes means "default to every permission my role grants" (CreateApiKeyRequest's own
    // doc comment) — there is no way to request zero scopes, matching the backend contract.
    mutationFn: (scopes: string[] | null) =>
      clientFetch<ApiKeyCreated>("/api/merchant/api-keys", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ scopes }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-api-keys"] });
    },
  });
}
