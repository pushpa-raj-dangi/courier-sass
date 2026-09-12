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
  type LucideIcon,
} from "lucide-react";

export interface NavItem {
  href: string;
  label: string;
  permission: string | string[];
  icon: LucideIcon;
}

// The full staff nav catalog, shared by the sidebar (NavMain) and the command palette's Pages
// group so the two never drift. Phases F1-F9 each add their real route once built; entries below
// point at routes that don't exist until their phase lands, which is fine — Link just 404s until
// then, and the item is hidden entirely for any user lacking the permission regardless.
export const NAV_ITEMS: NavItem[] = [
  { href: "/dashboard", label: "Dashboard", permission: "report.read", icon: LayoutDashboard },
  { href: "/shipments", label: "Shipments", permission: "shipment.read", icon: Package },
  { href: "/branches", label: "Branches", permission: "branch.read", icon: Building2 },
  { href: "/manifests", label: "Manifests", permission: "manifest.read", icon: Truck },
  { href: "/ndrs", label: "NDRs", permission: "ndr.read", icon: AlertTriangle },
  { href: "/return-shipments", label: "Return Shipments", permission: "rto.read", icon: Undo2 },
  { href: "/riders", label: "Riders", permission: "rider.read", icon: Bike },
  { href: "/rider/tasks", label: "My Deliveries", permission: "delivery.attempt", icon: ClipboardCheck },
  { href: "/cod", label: "COD", permission: "cod.read", icon: Wallet },
  { href: "/billing", label: "Billing", permission: "org.billing.manage", icon: CreditCard },
  { href: "/audit-logs", label: "Audit Logs", permission: "audit.read", icon: History },
  { href: "/admin", label: "Admin", permission: ["role.manage", "user.read"], icon: ShieldCheck },
];
