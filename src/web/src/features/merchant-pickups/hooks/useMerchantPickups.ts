"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PickupRequest } from "@/features/merchant-pickups/types";

// Not paginated — GET /api/v1/pickups returns a plain List<PickupRequestResponse>, matching
// every other small merchant-scoped list this phase touches (staff, api-keys, cod settlements).
export function useMerchantPickups() {
  return useQuery({
    queryKey: ["merchant-pickups"],
    queryFn: () => clientFetch<PickupRequest[]>("/api/merchant/pickups"),
  });
}
