"use client";

import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { useRiderSettlements } from "@/features/cod/hooks/useRiderSettlements";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { useBranches } from "@/features/branches/hooks/useBranches";

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
  const isOpen = panel.mode === "rider-settlement-detail";
  const id = panel.mode === "rider-settlement-detail" ? panel.id : "";

  // No GET-by-id endpoint exists for a single RiderCashSettlement (only the list) — the list is
  // already in cache from RiderSettlementsTab's own query in the common case, and refetching the
  // unfiltered first page is a reasonable, cheap fallback otherwise (this list is small in practice:
  // one row per rider per settlement run).
  const { data, isLoading, isError, refetch } = useRiderSettlements({ pageSize: 200 });
  const settlement = data?.items.find((r) => r.id === id);
  const { data: riders } = useRiders();
  const { data: branches } = useBranches();
  const rider = riders?.find((r) => r.id === settlement?.riderId);
  const branch = branches?.find((b) => b.id === settlement?.branchId);

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Rider Settlement</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && (
            <ErrorState title="Couldn't load this settlement" description="Something went wrong." onRetry={() => refetch()} />
          )}
          {!isLoading && !isError && !settlement && (
            <ErrorState title="Settlement not found" description="It may have been reconciled off the current page." onRetry={() => refetch()} />
          )}
          {settlement && (
            <div className="grid grid-cols-2 gap-4">
              <Field label="Rider" value={rider?.licenseNumber ?? settlement.riderId} />
              <Field label="Branch" value={branch?.name ?? settlement.branchId} />
              <Field label="Declared Amount" value={`Rs. ${settlement.declaredAmount.toFixed(2)}`} />
              <Field label="Shipment Count" value={String(settlement.shipmentCount)} />
              <Field label="Settled At" value={new Date(settlement.settledAt).toLocaleString()} />
              <Field label="Reconciled" value={settlement.branchCashSettlementId ? "Yes" : "Not yet"} />
            </div>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
