"use client";

import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useMerchantSettlement } from "@/features/merchant-cod/hooks/useMerchantSettlement";
import { useMerchantSettlementPanelStore } from "@/features/merchant-cod/store/merchantSettlementPanelStore";
import { toCsv } from "@/features/merchant-cod/lib/toCsv";

const currencyFormatter = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

function downloadCsv(filename: string, csv: string) {
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}

// Doubles as the invoice view per the design spec's resolved decision — no separate Invoice
// entity/entity exists, so this Sheet's own printable layout plus a CSV export of the already-
// fetched settlement is the whole "invoice" feature.
export function MerchantSettlementDetailSheet() {
  const panel = useMerchantSettlementPanelStore((s) => s.panel);
  const close = useMerchantSettlementPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";

  const { data: settlement, isLoading, isError, refetch } = useMerchantSettlement(id);

  function handleDownloadCsv() {
    if (!settlement) return;
    const csv = toCsv(
      ["Shipment AWB", "COD Amount", "Courier Fee", "Other Deductions", "Net Payable"],
      settlement.items.map((item) => [
        item.shipmentAwb,
        item.codAmount.toFixed(2),
        item.courierFee.toFixed(2),
        item.otherDeductions.toFixed(2),
        item.netPayable.toFixed(2),
      ])
    );
    downloadCsv(`settlement-${settlement.id}.csv`, csv);
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl print:max-w-none print:overflow-visible">
        <style>{`
          @media print {
            .merchant-invoice-no-print { display: none !important; }
          }
        `}</style>
        <SheetHeader className="merchant-invoice-no-print">
          <SheetTitle>Settlement Invoice</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && (
            <ErrorState
              title="Couldn't load this settlement"
              description="It may not exist, or you may not have access to it."
              onRetry={() => refetch()}
            />
          )}
          {settlement && (
            <>
              <div className="flex gap-2 merchant-invoice-no-print">
                <Button variant="outline" size="sm" onClick={() => window.print()}>
                  Print
                </Button>
                <Button variant="outline" size="sm" onClick={handleDownloadCsv}>
                  Download CSV
                </Button>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Status</span>
                  <StatusBadge status={settlement.status} namespace="MerchantSettlement" />
                </div>
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Period</span>
                  <span className="text-sm font-medium text-foreground">
                    {new Date(settlement.periodFrom).toLocaleDateString()} – {new Date(settlement.periodTo).toLocaleDateString()}
                  </span>
                </div>
                {settlement.settledAt && (
                  <div className="flex flex-col gap-1">
                    <span className="text-xs text-muted-foreground">Settled At</span>
                    <span className="text-sm font-medium text-foreground">{new Date(settlement.settledAt).toLocaleString()}</span>
                  </div>
                )}
              </div>

              <div className="space-y-2">
                <span className="text-xs text-muted-foreground">Line Items</span>
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-border text-left text-xs text-muted-foreground">
                      <th className="py-2">Shipment</th>
                      <th className="py-2 text-right">COD Amount</th>
                      <th className="py-2 text-right">Courier Fee</th>
                      <th className="py-2 text-right">Net Payable</th>
                    </tr>
                  </thead>
                  <tbody>
                    {settlement.items.map((item) => (
                      <tr key={item.id} className="border-b border-border/50">
                        <td className="py-2">{item.shipmentAwb}</td>
                        <td className="py-2 text-right">{currencyFormatter.format(item.codAmount)}</td>
                        <td className="py-2 text-right">{currencyFormatter.format(item.courierFee)}</td>
                        <td className="py-2 text-right">{currencyFormatter.format(item.netPayable)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="flex items-center justify-between border-t border-border pt-4">
                <span className="text-sm font-semibold text-foreground">Total Payable</span>
                <span className="text-lg font-semibold text-foreground">{currencyFormatter.format(settlement.totalPayable)}</span>
              </div>
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
