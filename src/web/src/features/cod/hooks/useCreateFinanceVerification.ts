"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CreateFinanceVerificationInput } from "@/features/cod/types";

interface FinancialTransactionResult {
  id: string;
  referenceEntityId: string;
}

export function useCreateFinanceVerification() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateFinanceVerificationInput) =>
      clientFetch<FinancialTransactionResult>("/api/cod/finance-verifications", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod", "branch-settlements"] });
      queryClient.invalidateQueries({ queryKey: ["cod", "collections"] });
    },
  });
}
