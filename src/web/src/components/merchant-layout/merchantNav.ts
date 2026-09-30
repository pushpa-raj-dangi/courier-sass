import { KeyRound, LayoutDashboard, Package, ShieldCheck, Truck, Users, Wallet, type LucideIcon } from "lucide-react";

export type MerchantNavGroup = "overview" | "shipping" | "money" | "account";

export interface MerchantNavItem {
  href: string;
  label: string;
  /** Omitted means always visible — only Dashboard has no permission gate. */
  permission?: string;
  icon: LucideIcon;
  group: MerchantNavGroup;
}

export const MERCHANT_NAV_GROUPS: { id: MerchantNavGroup; label: string }[] = [
  { id: "overview", label: "Overview" },
  { id: "shipping", label: "Shipping" },
  { id: "money", label: "Money" },
  { id: "account", label: "Account" },
];

export const MERCHANT_NAV_ITEMS: MerchantNavItem[] = [
  { href: "/merchant/dashboard", label: "Dashboard", icon: LayoutDashboard, group: "overview" },
  { href: "/merchant/shipments", label: "Shipments", permission: "shipment.read", icon: Package, group: "shipping" },
  { href: "/merchant/pickups", label: "Pickups", permission: "merchant.pickups.read", icon: Truck, group: "shipping" },
  { href: "/merchant/cod", label: "COD & Invoices", permission: "merchant.cod.read", icon: Wallet, group: "money" },
  { href: "/merchant/kyc", label: "KYC", permission: "merchant.kyc.upload", icon: ShieldCheck, group: "account" },
  { href: "/merchant/staff", label: "Staff", permission: "merchant.staff.manage", icon: Users, group: "account" },
  { href: "/merchant/api-keys", label: "API Keys", permission: "merchant.apikeys.manage", icon: KeyRound, group: "account" },
];
