"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantSettlement } from "@/features/cod/types";

export function useSettleMerchantSettlement(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => clientFetch<MerchantSettlement>(`/api/cod/merchant-settlements/${id}/settle`, { method: "POST" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod", "merchant-settlements"] });
      queryClient.invalidateQueries({ queryKey: ["cod", "collections"] });
    },
  });
}
