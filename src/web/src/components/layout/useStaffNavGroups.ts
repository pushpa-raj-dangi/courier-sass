"use client";

import { useTranslations } from "next-intl";
import { useSession } from "@/features/auth/hooks/useSession";
import { NAV_ITEMS, STAFF_NAV_GROUPS, hasAnyPermission } from "@/lib/navigation/navItems";
import { groupNavItems } from "@/components/console/navModel";
import type { ConsoleNavGroup } from "@/components/console/types";

// Same visibility rule as usePermission without a branch filter: an item shows when the session
// holds any of its permissions. Hidden items are omitted entirely, never disabled.
export function useStaffNavGroups(): { groups: ConsoleNavGroup[]; isLoading: boolean } {
  const t = useTranslations("nav");
  const { data: session, isLoading } = useSession();
  const granted = session?.permissions ?? [];

  const items = NAV_ITEMS.filter((item) => hasAnyPermission(granted, item.permission)).map((item) => ({
    href: item.href,
    label: t(item.labelKey),
    icon: item.icon,
    group: item.group,
    matchPrefix: true,
  }));

  const groups = groupNavItems(
    items,
    STAFF_NAV_GROUPS.map((id) => ({ id, label: t(`groups.${id}`) }))
  );
  return { groups, isLoading };
}
