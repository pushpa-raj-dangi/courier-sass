"use client";

import { useQuery } from "@tanstack/react-query";
import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";
import {
  fetchMerchantShipmentCount,
  merchantShipmentCountKey,
} from "@/features/merchant-shipments/hooks/useMerchantShipmentCounts";
import { groupNavItems } from "@/components/console/navModel";
import type { ConsoleNavGroup } from "@/components/console/types";
import { MERCHANT_NAV_GROUPS, MERCHANT_NAV_ITEMS } from "./merchantNav";

export function useMerchantNavGroups(): { groups: ConsoleNavGroup[]; isLoading: boolean } {
  const { data: session, isLoading } = useMerchantSession();
  const granted = session?.permissions ?? [];
  const canReadShipments = granted.includes("shipment.read");
  // Same key as useMerchantShipmentCounts' needsAttention entry, so the request is shared.
  const { data: needsAttention } = useQuery({
    queryKey: merchantShipmentCountKey("needsAttention"),
    queryFn: () => fetchMerchantShipmentCount("needsAttention"),
    enabled: canReadShipments,
  });
  const items = MERCHANT_NAV_ITEMS.filter((item) => !item.permission || granted.includes(item.permission)).map(
    (item) => ({
      href: item.href,
      label: item.label,
      icon: item.icon,
      group: item.group,
      matchPrefix: true,
      badge: item.href === "/merchant/shipments" && needsAttention && needsAttention > 0 ? needsAttention : undefined,
    })
  );
  return { groups: groupNavItems(items, MERCHANT_NAV_GROUPS), isLoading };
}
