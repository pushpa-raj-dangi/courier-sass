"use client";

import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { SectionCard } from "@/components/page/SectionCard";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { SectionError } from "@/components/states/SectionError";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { formatRs } from "@/features/cod/lib/codFormat";
import { useMerchantSettlement } from "@/features/merchant-cod/hooks/useMerchantSettlement";
import { deliveredCodParcelsCaption } from "@/features/merchant-cod/lib/codCopy";
import { formatPeriod } from "@/features/merchant-cod/lib/formatPeriod";
import { settlementExportUrl } from "@/features/merchant-cod/lib/settlementExportUrl";
import { settlementTotals } from "@/features/merchant-cod/lib/settlementTotals";
import { useMerchantSettlementPanelStore } from "@/features/merchant-cod/store/merchantSettlementPanelStore";

function Row({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex items-center justify-between py-1.5 text-sm">
      <dt className={strong ? "font-semibold text-foreground" : "text-muted-foreground"}>{label}</dt>
      <dd className={strong ? "font-semibold tabular-nums text-foreground" : "tabular-nums text-foreground"}>{value}</dd>
    </div>
  );
}

export function MerchantSettlementDetailSheet() {
  const panel = useMerchantSettlementPanelStore((s) => s.panel);
  const close = useMerchantSettlementPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";

  const { data: settlement, isLoading, isError, refetch } = useMerchantSettlement(id);
  const totals = settlement ? settlementTotals(settlement.items) : null;

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="flex w-full flex-col overflow-y-auto sm:max-w-xl">
        {settlement ? (
          <SheetHeaderBlock
            eyebrow={`SETTLEMENT · ${formatPeriod(settlement.periodFrom, settlement.periodTo)}`}
            title={formatRs(settlement.totalPayable)}
            status={<StatusBadge status={settlement.status} namespace="MerchantPayout" />}
          />
        ) : (
          <SheetHeader>
            <SheetTitle>Settlement</SheetTitle>
          </SheetHeader>
        )}
        <SheetDescription className={settlement ? undefined : "sr-only"}>
          {settlement ? deliveredCodParcelsCaption(settlement.items.length) : "Settlement details"}
        </SheetDescription>

        <div className="flex-1 space-y-5">
          {isLoading && <DetailSkeleton fieldRows={2} />}
          {isError && <SectionError message="Couldn't load this settlement." onRetry={() => refetch()} />}
          {settlement && totals && (
            <>
              <SectionCard title="How it adds up">
                <dl className="divide-y divide-border">
                  <Row label="COD collected" value={formatRs(totals.cod)} />
                  <Row label="Courier fees" value={`− ${formatRs(totals.fees)}`} />
                  {totals.other > 0 && <Row label="Other deductions" value={`− ${formatRs(totals.other)}`} />}
                  <Row label="You receive" value={formatRs(totals.net)} strong />
                </dl>
              </SectionCard>

              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border text-left text-2xs text-muted-foreground">
                    <th className="py-2 font-medium">AWB</th>
                    <th className="py-2 text-right font-medium">COD</th>
                    <th className="py-2 text-right font-medium">Fee</th>
                    <th className="py-2 text-right font-medium">Net</th>
                  </tr>
                </thead>
                <tbody>
                  {settlement.items.map((item) => (
                    <tr key={item.id} className="border-b border-border/50">
                      <td className="py-2 font-mono">{item.shipmentAwb}</td>
                      <td className="py-2 text-right tabular-nums">{formatRs(item.codAmount)}</td>
                      <td className="py-2 text-right tabular-nums">{formatRs(item.courierFee)}</td>
                      <td className="py-2 text-right tabular-nums">{formatRs(item.netPayable)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
        </div>

        {settlement && (
          <SheetFooter>
            <Button asChild variant="outline">
              <a href={settlementExportUrl(settlement.id)} download>
                Download CSV
              </a>
            </Button>
          </SheetFooter>
        )}
      </SheetContent>
    </Sheet>
  );
}
