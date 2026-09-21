import {
  LayoutDashboard,
  Package,
  Building2,
  Wallet,
  Truck,
  Bike,
  ClipboardCheck,
  AlertTriangle,
  Undo2,
  ShieldCheck,
  CreditCard,
  History,
  MapPin,
  type LucideIcon,
} from "lucide-react";

export type NavLabelKey =
  | "dashboard" | "shipments" | "branches" | "manifests" | "ndrs" | "returnShipments"
  | "riders" | "liveMap" | "myDeliveries" | "cod" | "billing" | "auditLogs" | "admin";

export interface NavItem {
  href: string;
  labelKey: NavLabelKey;
  permission: string | string[];
  icon: LucideIcon;
}

// The full staff nav catalog, shared by the sidebar (NavMain) and the command palette's Pages
// group so the two never drift. Phases F1-F9 each add their real route once built; entries below
// point at routes that don't exist until their phase lands, which is fine — Link just 404s until
// then, and the item is hidden entirely for any user lacking the permission regardless.
export const NAV_ITEMS: NavItem[] = [
  { href: "/dashboard", labelKey: "dashboard", permission: "report.read", icon: LayoutDashboard },
  { href: "/shipments", labelKey: "shipments", permission: "shipment.read", icon: Package },
  { href: "/branches", labelKey: "branches", permission: "branch.read", icon: Building2 },
  { href: "/manifests", labelKey: "manifests", permission: "manifest.read", icon: Truck },
  { href: "/ndrs", labelKey: "ndrs", permission: "ndr.read", icon: AlertTriangle },
  { href: "/return-shipments", labelKey: "returnShipments", permission: "rto.read", icon: Undo2 },
  { href: "/riders", labelKey: "riders", permission: "rider.read", icon: Bike },
  { href: "/riders/live-map", labelKey: "liveMap", permission: "rider.location.read", icon: MapPin },
  { href: "/rider/tasks", labelKey: "myDeliveries", permission: "delivery.attempt", icon: ClipboardCheck },
  { href: "/cod", labelKey: "cod", permission: "cod.read", icon: Wallet },
  { href: "/billing", labelKey: "billing", permission: "org.billing.manage", icon: CreditCard },
  { href: "/audit-logs", labelKey: "auditLogs", permission: "audit.read", icon: History },
  { href: "/admin", labelKey: "admin", permission: ["role.manage", "user.read"], icon: ShieldCheck },
];
