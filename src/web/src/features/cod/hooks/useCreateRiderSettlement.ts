"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { RiderCashSettlement, CreateRiderSettlementInput } from "@/features/cod/types";

export function useCreateRiderSettlement() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateRiderSettlementInput) =>
      clientFetch<RiderCashSettlement>("/api/cod/rider-settlements", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod", "rider-settlements"] });
      queryClient.invalidateQueries({ queryKey: ["cod", "collections"] });
    },
  });
}
