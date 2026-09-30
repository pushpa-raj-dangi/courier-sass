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

export type StaffNavGroup = "operations" | "finance" | "network" | "admin";

// Order of the section headings in the console rail (F15 R1).
export const STAFF_NAV_GROUPS: StaffNavGroup[] = ["operations", "finance", "network", "admin"];

export function hasAnyPermission(granted: string[], required: string | string[]): boolean {
  const needed = Array.isArray(required) ? required : [required];
  return needed.some((permission) => granted.includes(permission));
}

export interface NavItem {
  href: string;
  labelKey: NavLabelKey;
  permission: string | string[];
  icon: LucideIcon;
  group: StaffNavGroup;
}

// The full staff nav catalog, shared by the console rail (useStaffNavGroups) and the command palette's Pages
// group so the two never drift. Phases F1-F9 each add their real route once built; entries below
// point at routes that don't exist until their phase lands, which is fine — Link just 404s until
// then, and the item is hidden entirely for any user lacking the permission regardless.
export const NAV_ITEMS: NavItem[] = [
  { href: "/dashboard", labelKey: "dashboard", permission: "report.read", icon: LayoutDashboard, group: "operations" },
  { href: "/shipments", labelKey: "shipments", permission: "shipment.read", icon: Package, group: "operations" },
  { href: "/manifests", labelKey: "manifests", permission: "manifest.read", icon: Truck, group: "operations" },
  { href: "/riders", labelKey: "riders", permission: "rider.read", icon: Bike, group: "operations" },
  { href: "/riders/live-map", labelKey: "liveMap", permission: "rider.location.read", icon: MapPin, group: "operations" },
  { href: "/ndrs", labelKey: "ndrs", permission: "ndr.read", icon: AlertTriangle, group: "operations" },
  { href: "/return-shipments", labelKey: "returnShipments", permission: "rto.read", icon: Undo2, group: "operations" },
  { href: "/rider/tasks", labelKey: "myDeliveries", permission: "delivery.attempt", icon: ClipboardCheck, group: "operations" },
  { href: "/cod", labelKey: "cod", permission: "cod.read", icon: Wallet, group: "finance" },
  { href: "/billing", labelKey: "billing", permission: "org.billing.manage", icon: CreditCard, group: "finance" },
  { href: "/branches", labelKey: "branches", permission: "branch.read", icon: Building2, group: "network" },
  { href: "/admin", labelKey: "admin", permission: ["role.manage", "user.read"], icon: ShieldCheck, group: "admin" },
  { href: "/audit-logs", labelKey: "auditLogs", permission: "audit.read", icon: History, group: "admin" },
];
