"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { usePathname } from "next/navigation";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useSession } from "@/features/auth/hooks/useSession";
import { NAV_ITEMS, type NavItem } from "@/lib/navigation/navItems";
import {
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuSkeleton,
} from "@/components/ui/sidebar";

// How many rows to show before we know the session's real, permission-filtered list — a rough
// average rather than NAV_ITEMS.length, since most roles see a subset of the full catalog.
const NAV_SKELETON_ROWS = 7;

// Flat links, not the block's original collapsible groups — no nav item in this app has
// sub-items today, and CLAUDE.md's YAGNI guidance says not to build that structure speculatively.
export function NavMain({ onNavigate }: { onNavigate?: () => void }) {
  const t = useTranslations("nav");
  const pathname = usePathname();
  const { isLoading } = useSession();

  return (
    <SidebarGroup>
      <SidebarGroupLabel>{t("group")}</SidebarGroupLabel>
      <SidebarMenu>
        {isLoading
          ? Array.from({ length: NAV_SKELETON_ROWS }).map((_, i) => (
              <SidebarMenuItem key={i}>
                <SidebarMenuSkeleton showIcon data-testid="nav-item-skeleton" />
              </SidebarMenuItem>
            ))
          : NAV_ITEMS.map((item) => (
              <NavMainItem key={item.href} item={item} active={pathname === item.href} onNavigate={onNavigate} />
            ))}
      </SidebarMenu>
    </SidebarGroup>
  );
}

function NavMainItem({ item, active, onNavigate }: { item: NavItem; active: boolean; onNavigate?: () => void }) {
  const t = useTranslations("nav");
  const permissions = Array.isArray(item.permission) ? item.permission : [item.permission];
  const hasFirst = usePermission(permissions[0]);
  const hasSecond = usePermission(permissions[1] ?? permissions[0]);
  const allowed = hasFirst || hasSecond;
  if (!allowed) {
    return null;
  }
  const Icon = item.icon;
  const label = t(item.labelKey);
  return (
    <SidebarMenuItem>
      <SidebarMenuButton asChild isActive={active} tooltip={label}>
        <Link href={item.href} onClick={onNavigate}>
          <Icon strokeWidth={2} />
          <span>{label}</span>
        </Link>
      </SidebarMenuButton>
    </SidebarMenuItem>
  );
}
