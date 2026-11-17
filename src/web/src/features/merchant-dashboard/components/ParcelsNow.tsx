"use client";

import Link from "next/link";
import { KpiTile } from "@/components/page/KpiTile";
import { SectionCard } from "@/components/page/SectionCard";
import { Skeleton } from "@/components/ui/skeleton";
import { formatRs } from "@/features/cod/lib/codFormat";
import { useMerchantCodSummary } from "@/features/merchant-cod/hooks/useMerchantCodSummary";
import { useMerchantShipmentCounts } from "@/features/merchant-shipments/hooks/useMerchantShipmentCounts";
import { MERCHANT_STATUS_GROUPS, type MerchantShipmentTab } from "@/features/merchant-shipments/lib/statusGroups";

const TILES = ["booked", "inTransit", "outForDelivery"] as const;

function tileHref(tab: MerchantShipmentTab) {
  return `/merchant/shipments?tab=${tab}`;
}

export function ParcelsNow() {
  const counts = useMerchantShipmentCounts();
  const { data: summary, isLoading: summaryLoading } = useMerchantCodSummary();

  return (
    <SectionCard title="Your parcels right now" className="lg:col-span-2">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        {TILES.map((tab) => {
          const { count, isLoading } = counts[tab];
          return (
            <Link key={tab} href={tileHref(tab)} className="rounded-xl transition-colors hover:bg-muted/40">
              <KpiTile
                label={MERCHANT_STATUS_GROUPS[tab].label}
                value={isLoading ? <Skeleton className="h-8 w-10" /> : (count ?? "—")}
              />
            </Link>
          );
        })}
        <Link href={tileHref("delivered")} className="rounded-xl transition-colors hover:bg-muted/40">
          <KpiTile
            label="Delivered today"
            value={summaryLoading ? <Skeleton className="h-8 w-10" /> : (summary?.deliveredToday.parcels ?? "—")}
            note={summary ? `${formatRs(summary.deliveredToday.codCollected)} COD collected` : undefined}
          />
        </Link>
      </div>
    </SectionCard>
  );
}
