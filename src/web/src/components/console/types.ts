import type { LucideIcon } from "lucide-react";
import type { ReactNode } from "react";

export interface ConsoleNavItem {
  href: string;
  /** Already translated by the caller. */
  label: string;
  icon: LucideIcon;
  /** Optional trailing badge; not wired to data in R1. */
  badge?: ReactNode;
  /** Active for descendants too (pathname startsWith href + "/"). */
  matchPrefix?: boolean;
}

export interface ConsoleNavGroup {
  id: string;
  label: string;
  items: ConsoleNavItem[];
}

export interface ConsoleNavGroupDef {
  id: string;
  label: string;
}

export interface ConsoleBrand {
  icon: LucideIcon;
  name: ReactNode;
  subtitle?: ReactNode;
  isLoading?: boolean;
}
