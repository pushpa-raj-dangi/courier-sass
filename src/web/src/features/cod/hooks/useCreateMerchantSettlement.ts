"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantSettlement, CreateMerchantSettlementInput } from "@/features/cod/types";

export function useCreateMerchantSettlement() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateMerchantSettlementInput) =>
      clientFetch<MerchantSettlement>("/api/cod/merchant-settlements", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod", "merchant-settlements"] });
      queryClient.invalidateQueries({ queryKey: ["cod", "collections"] });
    },
  });
}
