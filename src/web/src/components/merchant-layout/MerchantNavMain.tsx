"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { LayoutDashboard, Package, Truck, Wallet, ShieldCheck, Users, KeyRound, type LucideIcon } from "lucide-react";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import {
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar";

interface MerchantNavItem {
  href: string;
  label: string;
  /** Omitted means always visible — only Dashboard has no permission gate. */
  permission?: string;
  icon: LucideIcon;
}

// The full merchant nav catalog. Tasks 9-14 each add their real route once built; entries below
// point at routes that don't exist until their task lands, which is fine — Link just 404s until
// then, and every gated entry is hidden entirely for a session lacking the permission regardless.
const NAV_ITEMS: MerchantNavItem[] = [
  { href: "/merchant/dashboard", label: "Dashboard", icon: LayoutDashboard },
  { href: "/merchant/shipments", label: "Shipments", permission: "shipment.read", icon: Package },
  { href: "/merchant/pickups", label: "Pickups", permission: "merchant.pickups.read", icon: Truck },
  { href: "/merchant/cod", label: "COD & Invoices", permission: "merchant.cod.read", icon: Wallet },
  { href: "/merchant/kyc", label: "KYC", permission: "merchant.kyc.upload", icon: ShieldCheck },
  { href: "/merchant/staff", label: "Staff", permission: "merchant.staff.manage", icon: Users },
  { href: "/merchant/api-keys", label: "API Keys", permission: "merchant.apikeys.manage", icon: KeyRound },
];

export function MerchantNavMain({ onNavigate }: { onNavigate?: () => void }) {
  const pathname = usePathname();
  return (
    <SidebarGroup>
      <SidebarGroupLabel>Merchant Portal</SidebarGroupLabel>
      <SidebarMenu>
        {NAV_ITEMS.map((item) => (
          <MerchantNavMainItem key={item.href} item={item} active={pathname === item.href} onNavigate={onNavigate} />
        ))}
      </SidebarMenu>
    </SidebarGroup>
  );
}

function MerchantNavMainItem({
  item,
  active,
  onNavigate,
}: {
  item: MerchantNavItem;
  active: boolean;
  onNavigate?: () => void;
}) {
  // Hooks can't be called conditionally, so this always calls useMerchantPermission even for the
  // permission-less Dashboard entry — passing "" is safe (never a real permission string, always
  // resolves to whatever useMerchantPermission("") returns) since `allowed` below short-circuits
  // on !item.permission before ever reading it.
  const hasPermission = useMerchantPermission(item.permission ?? "");
  const allowed = !item.permission || hasPermission;
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
