import {
  Building2,
  Cog,
  FileText,
  Globe,
  History,
  LayoutDashboard,
  ListChecks,
  Layers,
  Server,
} from "lucide-react";
import { groupNavItems } from "@/components/console/navModel";

// Platform operators (SuperAdmin) see every entry — no permission filtering at this layer,
// same as the previous top-bar PlatformNav.
export const PLATFORM_NAV = groupNavItems(
  [
    { href: "/platform/dashboard", label: "Dashboard", icon: LayoutDashboard, group: "tenants", matchPrefix: true },
    { href: "/platform/organizations", label: "Organizations", icon: Building2, group: "tenants", matchPrefix: true },
    { href: "/platform/plans", label: "Plans", icon: Layers, group: "tenants", matchPrefix: true },
    { href: "/platform/invoices", label: "Invoices", icon: FileText, group: "tenants", matchPrefix: true },
    { href: "/platform/countries", label: "Countries", icon: Globe, group: "reference", matchPrefix: true },
    { href: "/platform/jobs", label: "Jobs", icon: ListChecks, group: "system", matchPrefix: true },
    { href: "/platform/queues", label: "Queues", icon: Server, group: "system", matchPrefix: true },
    { href: "/platform/audit-logs", label: "Audit Logs", icon: History, group: "system", matchPrefix: true },
    { href: "/platform/settings", label: "Settings", icon: Cog, group: "system", matchPrefix: true },
  ],
  [
    { id: "tenants", label: "Tenants" },
    { id: "reference", label: "Reference" },
    { id: "system", label: "System" },
  ]
);
