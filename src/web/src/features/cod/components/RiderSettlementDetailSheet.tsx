"use client";

import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { useRiderSettlements } from "@/features/cod/hooks/useRiderSettlements";
import { useCodCollections } from "@/features/cod/hooks/useCodCollections";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { VarianceBadge } from "@/features/cod/components/VarianceBadge";
import { collectionLabel, formatDate, formatRs, formatTime } from "@/features/cod/lib/codFormat";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function RiderSettlementDetailSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const openCollection = useCodPanelStore((s) => s.openCollectionDetail);
  const isOpen = panel.mode === "rider-settlement-detail";
  const id = panel.mode === "rider-settlement-detail" ? panel.id : "";

  // No GET-by-id endpoint exists for a single RiderCashSettlement (only the list) — the list is
  // already in cache from the deposits tab's own query in the common case, and refetching the
  // unfiltered first page is a reasonable, cheap fallback otherwise (this list is small in practice:
  // one row per rider per settlement run).
  const { data, isLoading, isError, refetch } = useRiderSettlements({ pageSize: 200 }, { enabled: isOpen });
  const settlement = data?.items.find((r) => r.id === id);
  const names = usePeopleNames();
  const parcels = useCodCollections({ riderCashSettlementId: id, pageSize: 200 }, { enabled: isOpen && Boolean(id) });

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Rider Settlement</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && <DetailSkeleton fieldRows={1} />}
          {isError && (
            <ErrorState title="Couldn't load this settlement" description="Something went wrong." onRetry={() => refetch()} />
          )}
          {!isLoading && !isError && !settlement && (
            <ErrorState title="Settlement not found" description="It may have been reconciled off the current page." onRetry={() => refetch()} />
          )}
          {settlement && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <Field label="Rider" value={names.riderName(settlement.riderId)} />
                <Field label="Branch" value={names.branchName(settlement.branchId)} />
                <Field label="Expected" value={formatRs(settlement.expectedAmount)} />
                <Field label="Deposited" value={formatRs(settlement.declaredAmount)} />
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Variance</span>
                  <VarianceBadge variance={settlement.variance} />
                </div>
                <Field label="Parcels" value={String(settlement.shipmentCount)} />
                <Field label="Recorded at" value={`${formatDate(settlement.settledAt)} ${formatTime(settlement.settledAt)}`} />
                <Field label="Reconciled" value={settlement.branchCashSettlementId ? "Reconciled" : "Awaiting branch"} />
              </div>
              <div className="space-y-2">
                <h3 className="text-sm font-medium">Parcels in this deposit</h3>
                {parcels.isLoading && <DetailSkeleton fieldRows={1} />}
                {parcels.isError && (
                  <ErrorState title="Couldn't load parcels" description="Something went wrong." onRetry={() => parcels.refetch()} />
                )}
                {parcels.data && parcels.data.items.length === 0 && (
                  <p className="rounded-lg border border-border p-3 text-sm text-muted-foreground">No parcels linked to this deposit.</p>
                )}
                {parcels.data && parcels.data.items.length > 0 && parcels.data.items.length < settlement.shipmentCount && (
                  <p className="text-xs text-muted-foreground">
                    Showing {parcels.data.items.length} of {settlement.shipmentCount} parcels
                  </p>
                )}
                {parcels.data && parcels.data.items.length > 0 && (
                  <ul className="divide-y divide-border rounded-lg border border-border text-sm">
                    {parcels.data.items.map((c) => (
                      <li key={c.id} className="flex items-center justify-between gap-3 px-3 py-2">
                        <span className="flex flex-col">
                          <span className="font-mono font-medium">{collectionLabel(c)}</span>
                          <span className="text-xs text-muted-foreground">{c.collectedAt ? `${formatDate(c.collectedAt)} ${formatTime(c.collectedAt)}` : "—"}</span>
                        </span>
                        <span className="tabular-nums">{c.collectedAmount === null ? "—" : formatRs(c.collectedAmount)}</span>
                        <Button type="button" size="sm" variant="ghost" onClick={() => openCollection(c.id)}>View</Button>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
