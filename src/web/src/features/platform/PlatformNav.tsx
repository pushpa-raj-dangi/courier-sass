"use client";

import Link from "next/link";
import { useRouter, usePathname } from "next/navigation";
import { LogOut } from "lucide-react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";

const links = [
  { href: "/platform/dashboard", label: "Dashboard" },
  { href: "/platform/organizations", label: "Organizations" },
  { href: "/platform/plans", label: "Plans" },
  { href: "/platform/invoices", label: "Invoices" },
  { href: "/platform/audit-logs", label: "Audit Logs" },
  { href: "/platform/jobs", label: "Jobs" },
];

export function PlatformNav() {
  const router = useRouter();
  const pathname = usePathname();

  const handleSignOut = async () => {
    await fetch("/api/platform-auth/logout", { method: "POST" });
    router.push("/platform-login");
  };

  return (
    <nav className="flex items-center gap-4 border-b bg-card px-6 py-3">
      {links.map((link) => (
        <Link
          key={link.href}
          href={link.href}
          className={cn("text-sm font-medium text-muted-foreground hover:text-foreground", pathname.startsWith(link.href) && "text-foreground")}
        >
          {link.label}
        </Link>
      ))}
      <Button size="sm" variant="ghost" className="ml-auto gap-2 text-muted-foreground hover:text-foreground" onClick={handleSignOut}>
        <LogOut className="h-4 w-4" strokeWidth={2} />
        Sign out
      </Button>
    </nav>
  );
}
