"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { OperationalDefaults } from "@/features/admin/settings/types";

// Consumed by the shipment-creation flow (ShipmentCreateSheet) to pre-fill origin branch/service
// type for counter staff. Deliberately separate from useOrganizationSettings: that hook hits
// GET /api/v1/settings/organization, which is gated on org.settings.manage and 403s for
// CounterStaff/OperationsManager — the exact roles that create shipments all day. This hook hits
// the narrower GET /api/v1/settings/organization/defaults, gated on shipment.create instead, and
// returns only OperationalDefaults (no profile/security data).
export function useShipmentDefaults() {
  return useQuery({
    queryKey: ["shipment-defaults"],
    queryFn: () => clientFetch<OperationalDefaults>("/api/settings/organization/defaults"),
    retry: false,
  });
}
