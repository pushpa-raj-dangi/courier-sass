"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { NAV_ITEMS, type NavItem } from "@/lib/navigation/navItems";
import {
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar";

// Flat links, not the block's original collapsible groups — no nav item in this app has
// sub-items today, and CLAUDE.md's YAGNI guidance says not to build that structure speculatively.
export function NavMain({ onNavigate }: { onNavigate?: () => void }) {
  const pathname = usePathname();
  return (
    <SidebarGroup>
      <SidebarGroupLabel>Platform</SidebarGroupLabel>
      <SidebarMenu>
        {NAV_ITEMS.map((item) => (
          <NavMainItem key={item.href} item={item} active={pathname === item.href} onNavigate={onNavigate} />
        ))}
      </SidebarMenu>
    </SidebarGroup>
  );
}

function NavMainItem({ item, active, onNavigate }: { item: NavItem; active: boolean; onNavigate?: () => void }) {
  const permissions = Array.isArray(item.permission) ? item.permission : [item.permission];
  const hasFirst = usePermission(permissions[0]);
  const hasSecond = usePermission(permissions[1] ?? permissions[0]);
  const allowed = hasFirst || hasSecond;
  if (!allowed) {
    return null;
  }
  const Icon = item.icon;
  return (
    <SidebarMenuItem>
      <SidebarMenuButton asChild isActive={active} tooltip={item.label}>
        <Link href={item.href} onClick={onNavigate}>
          <Icon strokeWidth={2} />
          <span>{item.label}</span>
        </Link>
      </SidebarMenuButton>
    </SidebarMenuItem>
  );
}
