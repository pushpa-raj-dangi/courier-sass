"use client";

import Link from "next/link";
import { ChevronRight } from "lucide-react";
import { SectionCard } from "@/components/page/SectionCard";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { cn } from "@/lib/utils";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useNdrsSummary } from "@/features/ndr/hooks/useNdrsSummary";
import { useReturnShipmentsSummary } from "@/features/ndr/hooks/useReturnShipmentsSummary";
import { useManifestsSummary } from "@/features/manifests/hooks/useManifestsSummary";
import { useRiderCash } from "@/features/cod/hooks/useRiderCash";
import { HELD_WARNING_HOURS, hoursHeld } from "@/features/cod/lib/codFormat";
import { openNdrCount } from "@/features/reports/lib/dashboardModel";

interface Row {
  key: string;
  label: string;
  description: string;
  href: string;
  count: number | undefined;
  visible: boolean;
}

export function NeedsActionCard() {
  const canNdr = usePermission("ndr.read");
  const canRto = usePermission("rto.read");
  const canManifest = usePermission("manifest.read");
  const ndrs = useNdrsSummary({ enabled: canNdr });
  const rtos = useReturnShipmentsSummary({ enabled: canRto });
  const manifests = useManifestsSummary({ enabled: canManifest });
  const canCod = usePermission("cod.read");
  const riderCash = useRiderCash({}, { enabled: canCod });

  const rows: Row[] = [
    { key: "ndr", label: "Open NDRs", description: "Delivery failed, awaiting resolution", href: "/ndrs", count: ndrs.data ? openNdrCount(ndrs.data) : undefined, visible: canNdr && !ndrs.isError },
    { key: "rto", label: "RTOs awaiting merchant sign-off", description: "Returned to origin, not confirmed", href: "/return-shipments", count: rtos.data?.pendingConfirmation, visible: canRto && !rtos.isError },
    { key: "manifests", label: "Manifests in transit", description: "Dispatched and on the road", href: "/manifests", count: manifests.data?.InTransit ?? (manifests.data ? 0 : undefined), visible: canManifest && !manifests.isError },
    {
      key: "cod-cash",
      label: "Riders holding cash > 24 h",
      description: "Collected COD not yet deposited",
      href: "/cod?tab=rider-deposits",
      count: riderCash.data ? riderCash.data.items.filter((h) => hoursHeld(h.oldestCollectedAt) >= HELD_WARNING_HOURS).length : undefined,
      visible: canCod && !riderCash.isError,
    },
  ];

  return (
    <section aria-label="Needs action">
      <SectionCard title="Needs action" className="h-full">
        <ul className="flex flex-col gap-1">
          {rows
            .filter((r) => r.visible)
            .map((r) => (
              <li key={r.key}>
                <Link href={r.href} className="flex items-center gap-3 rounded-lg px-2 py-2.5 hover:bg-accent">
                  <span className={cn("min-w-9 text-center font-display text-lg font-semibold tabular-nums", r.count === 0 && "text-muted-foreground")}>
                    {r.count === undefined ? "…" : r.count}
                  </span>
                  <span className="flex flex-1 flex-col">
                    <span className={cn("text-[13.5px] font-medium", r.count === 0 && "text-muted-foreground")}>{r.label}</span>
                    <span className="text-xs text-muted-foreground">{r.description}</span>
                  </span>
                  <ChevronRight className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
                </Link>
              </li>
            ))}
        </ul>
        <ComingSoonSlot
          className="mt-3 p-4"
          title="More exceptions"
          description="Manifests with missing parcels and express parcels at SLA risk."
        />
      </SectionCard>
    </section>
  );
}
