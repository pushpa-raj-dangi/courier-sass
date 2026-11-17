"use client";

import { KpiTile } from "@/components/page/KpiTile";
import { Skeleton } from "@/components/ui/skeleton";
import { formatRs } from "@/features/cod/lib/codFormat";
import { useMerchantCodSummary } from "@/features/merchant-cod/hooks/useMerchantCodSummary";
import { deliveredCodParcelsCaption, formatDayMonth, plural, settlementsCaption } from "../lib/dashboardLogic";
import { SectionError } from "@/components/states/SectionError";

export function MoneyBand() {
  const { data, isLoading, isError, refetch } = useMerchantCodSummary();

  if (isLoading) {
    return (
      <div className="grid gap-4 md:grid-cols-3">
        {[0, 1, 2].map((i) => (
          <Skeleton key={i} data-testid="dashboard-skeleton" className="h-24 rounded-xl" />
        ))}
      </div>
    );
  }

  if (isError || !data) {
    return (
      <div className="rounded-xl border border-destructive/30 bg-destructive/5 p-4">
        <SectionError message="Couldn't load your COD summary." onRetry={() => refetch()} />
      </div>
    );
  }

  const settled = data.settledThisMonth;
  return (
    <div className="grid gap-4 md:grid-cols-3">
      <div className="flex flex-col gap-1.5 rounded-xl bg-primary p-4 text-primary-foreground">
        <span className="text-sm opacity-90">Collected, not yet settled</span>
        <span className="text-2xl font-semibold tabular-nums tracking-tight">
          {formatRs(data.collectedNotSettled.amount)}
        </span>
        <span className="text-2xs opacity-90">{deliveredCodParcelsCaption(data.collectedNotSettled.count)}</span>
      </div>
      <KpiTile
        label="In settlement"
        value={formatRs(data.inSettlement.amount)}
        note={settlementsCaption(data.inSettlement.count)}
      />
      <KpiTile
        label="Settled this month"
        value={formatRs(settled.amount)}
        note={`${plural(settled.payouts, "payout")}${settled.lastSettledAt ? ` · last on ${formatDayMonth(settled.lastSettledAt)}` : ""}`}
      />
    </div>
  );
}
