"use client";

import { KpiTile } from "@/components/page/KpiTile";
import { Skeleton } from "@/components/ui/skeleton";
import { formatRs } from "@/features/cod/lib/codFormat";
import { useMerchantCodSummary } from "@/features/merchant-cod/hooks/useMerchantCodSummary";
import { formatDayMonth, settlementsCaption } from "../lib/dashboardLogic";

export function MoneyBand() {
  const { data, isLoading } = useMerchantCodSummary();

  if (isLoading) {
    return (
      <div className="grid gap-4 md:grid-cols-3">
        {[0, 1, 2].map((i) => (
          <Skeleton key={i} className="h-24 rounded-xl" />
        ))}
      </div>
    );
  }

  const settled = data?.settledThisMonth;
  return (
    <div className="grid gap-4 md:grid-cols-3">
      <div className="flex flex-col gap-1.5 rounded-xl bg-primary p-4 text-primary-foreground">
        <span className="text-sm opacity-90">Collected, not yet settled</span>
        <span className="text-2xl font-semibold tabular-nums tracking-tight">
          {data ? formatRs(data.collectedNotSettled.amount) : "—"}
        </span>
        <span className="text-2xs opacity-90">
          {data ? `${data.collectedNotSettled.count} delivered COD parcels` : "—"}
        </span>
      </div>
      <KpiTile
        label="In settlement"
        value={data ? formatRs(data.inSettlement.amount) : "—"}
        note={data ? settlementsCaption(data.inSettlement.count) : "—"}
      />
      <KpiTile
        label="Settled this month"
        value={settled ? formatRs(settled.amount) : "—"}
        note={
          settled
            ? `${settled.payouts} payouts${settled.lastSettledAt ? ` · last on ${formatDayMonth(settled.lastSettledAt)}` : ""}`
            : "—"
        }
      />
    </div>
  );
}
