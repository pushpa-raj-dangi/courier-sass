"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PickupRequest } from "@/features/merchant-pickups/types";

// Not paginated — GET /api/v1/pickups returns a plain List<PickupRequestResponse>, matching
// every other small merchant-scoped list this phase touches (staff, api-keys, cod settlements).
// `enabled` lets the layout-mounted detail Sheet skip the fetch while closed; same key, so it
// shares the page's cache whenever both are mounted.
export function useMerchantPickups(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["merchant-pickups"],
    enabled: options?.enabled ?? true,
    queryFn: () => clientFetch<PickupRequest[]>("/api/merchant/pickups"),
  });
}
