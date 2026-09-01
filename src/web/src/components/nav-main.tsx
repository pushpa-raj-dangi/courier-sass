"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { LayoutDashboard, Package, Building2, Wallet, Truck, Bike, ClipboardCheck, type LucideIcon } from "lucide-react";
import { usePermission } from "@/features/auth/hooks/usePermission";
import {
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar";

interface NavItem {
  href: string;
  label: string;
  permission: string;
  icon: LucideIcon;
}

// The full staff nav catalog. Phases F1-F9 each add their real route once built; entries below
// point at routes that don't exist until their phase lands, which is fine — Link just 404s
// until then, and the item is hidden entirely for any user lacking the permission regardless.
const NAV_ITEMS: NavItem[] = [
  { href: "/dashboard", label: "Dashboard", permission: "report.read", icon: LayoutDashboard },
  { href: "/shipments", label: "Shipments", permission: "shipment.read", icon: Package },
  { href: "/branches", label: "Branches", permission: "branch.read", icon: Building2 },
  { href: "/manifests", label: "Manifests", permission: "manifest.read", icon: Truck },
  { href: "/riders", label: "Riders", permission: "rider.read", icon: Bike },
  { href: "/my-deliveries", label: "My Deliveries", permission: "delivery.attempt", icon: ClipboardCheck },
  { href: "/cod", label: "COD", permission: "cod.read", icon: Wallet },
];

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
  const allowed = usePermission(item.permission);
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
