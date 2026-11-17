"use client";

import Link from "next/link";
import { useQueryClient } from "@tanstack/react-query";
import { KpiTile } from "@/components/page/KpiTile";
import { SectionCard } from "@/components/page/SectionCard";
import { Skeleton } from "@/components/ui/skeleton";
import { formatRs } from "@/features/cod/lib/codFormat";
import { useMerchantCodSummary } from "@/features/merchant-cod/hooks/useMerchantCodSummary";
import { useMerchantShipmentCounts } from "@/features/merchant-shipments/hooks/useMerchantShipmentCounts";
import { MERCHANT_STATUS_GROUPS, type MerchantShipmentTab } from "@/features/merchant-shipments/lib/statusGroups";
import { SectionError } from "./SectionError";

const TILES = ["booked", "inTransit", "outForDelivery"] as const;

function tileHref(tab: MerchantShipmentTab) {
  return `/merchant/shipments?tab=${tab}`;
}

const tileClass = "rounded-xl transition-colors hover:bg-muted/40";

export function ParcelsNow({ canReadCod }: { canReadCod: boolean }) {
  const queryClient = useQueryClient();
  const counts = useMerchantShipmentCounts();
  const summary = useMerchantCodSummary({ enabled: canReadCod });

  const needed = [counts.booked, counts.inTransit, counts.outForDelivery, ...(canReadCod ? [] : [counts.delivered])];
  const loading = needed.some((c) => c.isLoading) || (canReadCod && summary.isLoading);
  const failed = needed.some((c) => !c.isLoading && c.count === null) || (canReadCod && summary.isError);

  const retry = () => {
    queryClient.invalidateQueries({ queryKey: ["merchant-shipments", "count"] });
    if (canReadCod) summary.refetch();
  };

  let body;
  if (loading) {
    body = (
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        {[0, 1, 2, 3].map((i) => (
          <Skeleton key={i} data-testid="dashboard-skeleton" className="h-24 rounded-xl" />
        ))}
      </div>
    );
  } else if (failed) {
    body = <SectionError message="Couldn't load your parcel counts." onRetry={retry} />;
  } else {
    body = (
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        {TILES.map((tab) => (
          <Link key={tab} href={tileHref(tab)} className={tileClass}>
            <KpiTile label={MERCHANT_STATUS_GROUPS[tab].label} value={counts[tab].count} />
          </Link>
        ))}
        <Link href={tileHref("delivered")} className={tileClass}>
          {canReadCod && summary.data ? (
            <KpiTile
              label="Delivered today"
              value={summary.data.deliveredToday.parcels}
              note={`${formatRs(summary.data.deliveredToday.codCollected)} COD collected`}
            />
          ) : (
            <KpiTile label="Delivered" value={counts.delivered.count} />
          )}
        </Link>
      </div>
    );
  }

  return (
    <SectionCard title="Your parcels right now" className="lg:col-span-2">
      {body}
    </SectionCard>
  );
}
