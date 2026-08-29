"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { LayoutDashboard, Package, Building2, Wallet, Truck, type LucideIcon } from "lucide-react";
import { cn } from "@/lib/utils";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useUiStore } from "@/lib/store/uiStore";

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
  { href: "/cod", label: "COD", permission: "cod.read", icon: Wallet },
];

// The nav content shared by the permanent desktop Sidebar and MobileNav's slide-out drawer, so
// the two never drift apart on nav items or permission-gating logic.
export function SidebarNav({
  collapsed = false,
  onNavigate,
}: {
  collapsed?: boolean;
  onNavigate?: () => void;
}) {
  const pathname = usePathname();
  return (
    <nav className="flex flex-col gap-1" aria-label="Primary">
      {NAV_ITEMS.map((item) => (
        <SidebarLink
          key={item.href}
          item={item}
          active={pathname === item.href}
          collapsed={collapsed}
          onNavigate={onNavigate}
        />
      ))}
    </nav>
  );
}

// Permanently visible from md (768px) up. Below that, MobileNav's Sheet-based drawer (opened
// from TopNav's hamburger trigger) carries the same SidebarNav content instead — a fixed-width
// column sidebar has no mobile fallback otherwise, eating most of a phone-width viewport.
export function Sidebar() {
  const collapsed = useUiStore((s) => s.sidebarCollapsed);

  return (
    <div
      className={cn(
        "hidden shrink-0 border-r border-border bg-card p-2 transition-all md:block",
        collapsed ? "w-16" : "w-56"
      )}
    >
      <SidebarNav collapsed={collapsed} />
    </div>
  );
}

function SidebarLink({
  item,
  active,
  collapsed,
  onNavigate,
}: {
  item: NavItem;
  active: boolean;
  collapsed: boolean;
  onNavigate?: () => void;
}) {
  const allowed = usePermission(item.permission);
  if (!allowed) {
    return null;
  }
  const Icon = item.icon;
  return (
    <Link
      href={item.href}
      onClick={onNavigate}
      className={cn(
        "flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium text-muted-foreground transition-colors hover:bg-accent hover:text-foreground",
        active && "bg-accent text-foreground",
        collapsed && "justify-center"
      )}
    >
      <Icon className="h-[18px] w-[18px] shrink-0" strokeWidth={2} />
      {!collapsed && item.label}
    </Link>
  );
}
