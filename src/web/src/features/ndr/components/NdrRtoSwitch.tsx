"use client";

import Link from "next/link";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { cn } from "@/lib/utils";

const LINKS = [
  { key: "ndr", href: "/ndrs", label: "NDR queue" },
  { key: "returns", href: "/return-shipments", label: "Returns" },
] as const;

// Segmented switch between the two halves of "NDR & RTO". Hidden unless the viewer can read both
// sides — a one-option switch is noise.
export function NdrRtoSwitch({ active }: { active: "ndr" | "returns" }) {
  const canReadNdr = usePermission("ndr.read");
  const canReadRto = usePermission("rto.read");
  if (!canReadNdr || !canReadRto) return null;

  return (
    <div role="tablist" aria-label="NDR and returns" className="inline-flex rounded-lg border border-border bg-muted p-0.5">
      {LINKS.map((link) => (
        <Link
          key={link.key}
          href={link.href}
          role="tab"
          aria-selected={active === link.key}
          className={cn(
            "rounded-md px-3 py-1.5 text-sm font-medium transition-colors",
            active === link.key ? "bg-background text-foreground shadow-sm" : "text-muted-foreground hover:text-foreground",
          )}
        >
          {link.label}
        </Link>
      ))}
    </div>
  );
}
