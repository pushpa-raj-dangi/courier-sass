"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { BranchCashSettlement, CreateBranchSettlementInput } from "@/features/cod/types";

export function useCreateBranchSettlement() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateBranchSettlementInput) =>
      clientFetch<BranchCashSettlement>("/api/cod/branch-settlements", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod", "branch-settlements"] });
      queryClient.invalidateQueries({ queryKey: ["cod", "rider-settlements"] });
      queryClient.invalidateQueries({ queryKey: ["cod", "collections"] });
    },
  });
}
