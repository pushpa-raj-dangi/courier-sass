"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/utils";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useUiStore } from "@/lib/store/uiStore";

interface NavItem {
  href: string;
  label: string;
  permission: string;
}

// The full staff nav catalog. Phases F1-F9 each add their real route once built; entries below
// point at routes that don't exist until their phase lands, which is fine — Link just 404s
// until then, and the item is hidden entirely for any user lacking the permission regardless.
const NAV_ITEMS: NavItem[] = [
  { href: "/dashboard", label: "Dashboard", permission: "report.read" },
  { href: "/shipments", label: "Shipments", permission: "shipment.read" },
  { href: "/branches", label: "Branches", permission: "branch.read" },
  { href: "/cod", label: "COD", permission: "cod.read" },
];

export function Sidebar() {
  const pathname = usePathname();
  const collapsed = useUiStore((s) => s.sidebarCollapsed);

  return (
    <nav
      className={cn(
        "flex flex-col gap-1 border-r border-border bg-card p-2 transition-all",
        collapsed ? "w-16" : "w-56"
      )}
      aria-label="Primary"
    >
      {NAV_ITEMS.map((item) => (
        <SidebarLink key={item.href} item={item} active={pathname === item.href} collapsed={collapsed} />
      ))}
    </nav>
  );
}

function SidebarLink({ item, active, collapsed }: { item: NavItem; active: boolean; collapsed: boolean }) {
  const allowed = usePermission(item.permission);
  if (!allowed) {
    return null;
  }
  return (
    <Link
      href={item.href}
      className={cn(
        "rounded-md px-3 py-2 text-sm font-medium text-foreground hover:bg-accent",
        active && "bg-accent",
        collapsed && "text-center"
      )}
    >
      {collapsed ? item.label[0] : item.label}
    </Link>
  );
}
