"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/utils";

const links = [
  { href: "/platform/dashboard", label: "Dashboard" },
  { href: "/platform/organizations", label: "Organizations" },
  { href: "/platform/plans", label: "Plans" },
  { href: "/platform/invoices", label: "Invoices" },
];

export function PlatformNav() {
  const pathname = usePathname();
  return (
    <nav className="flex gap-4 border-b bg-card px-6 py-3">
      {links.map((link) => (
        <Link
          key={link.href}
          href={link.href}
          className={cn("text-sm font-medium text-muted-foreground hover:text-foreground", pathname.startsWith(link.href) && "text-foreground")}
        >
          {link.label}
        </Link>
      ))}
    </nav>
  );
}
