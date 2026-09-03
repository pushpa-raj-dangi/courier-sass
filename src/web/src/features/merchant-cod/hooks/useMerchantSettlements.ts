"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantSettlement } from "@/features/merchant-cod/types";

// Not paginated — GET /api/v1/cod/settlements returns a plain List<MerchantSettlementResponse>,
// matching every other small merchant-scoped list this phase touches (pickups, staff, api-keys).
export function useMerchantSettlements() {
  return useQuery({
    queryKey: ["merchant-cod-settlements"],
    queryFn: () => clientFetch<MerchantSettlement[]>("/api/merchant/cod/settlements"),
  });
}
