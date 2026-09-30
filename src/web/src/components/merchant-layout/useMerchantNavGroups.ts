"use client";

import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";
import { groupNavItems } from "@/components/console/navModel";
import type { ConsoleNavGroup } from "@/components/console/types";
import { MERCHANT_NAV_GROUPS, MERCHANT_NAV_ITEMS } from "./merchantNav";

export function useMerchantNavGroups(): { groups: ConsoleNavGroup[]; isLoading: boolean } {
  const { data: session, isLoading } = useMerchantSession();
  const granted = session?.permissions ?? [];
  const items = MERCHANT_NAV_ITEMS.filter((item) => !item.permission || granted.includes(item.permission)).map(
    (item) => ({ href: item.href, label: item.label, icon: item.icon, group: item.group, matchPrefix: true })
  );
  return { groups: groupNavItems(items, MERCHANT_NAV_GROUPS), isLoading };
}
