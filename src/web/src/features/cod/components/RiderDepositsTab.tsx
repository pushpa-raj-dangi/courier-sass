"use client";

import { useState } from "react";
import { Wallet, ReceiptText } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { SectionCard } from "@/components/page/SectionCard";
import { ErrorState } from "@/components/states/ErrorState";
import { EmptyState } from "@/components/states/EmptyState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useRiderCash } from "@/features/cod/hooks/useRiderCash";
import { useRiderSettlements } from "@/features/cod/hooks/useRiderSettlements";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { HELD_WARNING_HOURS, formatHeldFor, formatRs, formatTime, formatDate, hoursHeld, sortHoldings } from "@/features/cod/lib/codFormat";
import { VarianceBadge } from "@/features/cod/components/VarianceBadge";
import type { RiderCashSettlement } from "@/features/cod/types";

function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase()).join("") || "R";
}

function HoldingStatus({ oldestCollectedAt }: { oldestCollectedAt: string }) {
  const overdue = hoursHeld(oldestCollectedAt) >= HELD_WARNING_HOURS;
  return (
    <span className="inline-flex items-center gap-1.5 rounded bg-muted px-2 py-0.5 text-xs font-medium">
      <span className="h-2 w-2 rounded-full" style={{ backgroundColor: `hsl(var(--${overdue ? "status-ndr" : "status-created"}))` }} aria-hidden="true" />
      {overdue ? "Held over 24 h" : "Holding"}
    </span>
  );
}

export function RiderDepositsTab({ branchId, from, to }: { branchId?: string; from?: string; to?: string }) {
  const canReconcile = usePermission("cod.reconcile");
  const names = usePeopleNames();
  const openCreate = useCodPanelStore((s) => s.openRiderSettlementCreate);
  const openDetail = useCodPanelStore((s) => s.openRiderSettlementDetail);
  const cash = useRiderCash({ branchId });
  const [page, setPage] = useState(1);
  const deposits = useRiderSettlements({ branchId, from, to, page, pageSize: 20 });

  const holdings = sortHoldings(cash.data?.items ?? []);

  const depositColumns: ColumnDef<RiderCashSettlement>[] = [
    {
      key: "rider",
      header: "Rider",
      render: (r) => (
        <button type="button" onClick={() => openDetail(r.id)} className="font-medium text-foreground hover:underline">
          {names.riderName(r.riderId)}
        </button>
      ),
    },
    { key: "parcels", header: "Parcels", render: (r) => String(r.shipmentCount) },
    { key: "expected", header: "Expected", render: (r) => formatRs(r.expectedAmount) },
    { key: "deposited", header: "Deposited", render: (r) => formatRs(r.declaredAmount) },
    { key: "variance", header: "Variance", render: (r) => <VarianceBadge variance={r.variance} /> },
    { key: "recorded", header: "Recorded at", render: (r) => `${formatDate(r.settledAt)} ${formatTime(r.settledAt)}` },
    { key: "branch", header: "Branch state", render: (r) => (r.branchCashSettlementId ? "Reconciled" : "Awaiting branch") },
  ];

  return (
    <div className="space-y-4">
      <SectionCard title="Holding cash" icon={Wallet} description="Collected COD riders have not deposited yet.">
        {cash.isLoading && <Skeleton className="h-32 w-full" />}
        {cash.isError && <ErrorState title="Couldn't load rider cash" onRetry={() => cash.refetch?.()} />}
        {cash.data && holdings.length === 0 && <EmptyState title="No rider is holding COD cash right now." />}
        {holdings.length > 0 && (
          <Table aria-label="Holding cash">
            <TableHeader>
              <TableRow>
                <TableHead>Rider</TableHead>
                <TableHead>Branch</TableHead>
                <TableHead className="text-right">Parcels</TableHead>
                <TableHead className="text-right">Amount held</TableHead>
                <TableHead>Held for</TableHead>
                <TableHead>Status</TableHead>
                {canReconcile && <TableHead><span className="sr-only">Actions</span></TableHead>}
              </TableRow>
            </TableHeader>
            <TableBody>
              {holdings.map((h) => {
                const name = names.riderName(h.riderId);
                return (
                  <TableRow key={`${h.riderId}-${h.branchId}`}>
                    <TableCell>
                      <span className="flex items-center gap-2.5">
                        <span className="flex h-8 w-8 items-center justify-center rounded-full bg-muted text-xs font-semibold" aria-hidden="true">{initials(name)}</span>
                        <span className="font-medium">{name}</span>
                      </span>
                    </TableCell>
                    <TableCell>{names.branchName(h.branchId)}</TableCell>
                    <TableCell className="text-right tabular-nums">{h.parcelCount}</TableCell>
                    <TableCell className="text-right font-medium tabular-nums">{formatRs(h.amount)}</TableCell>
                    <TableCell className="tabular-nums">{formatHeldFor(h.oldestCollectedAt)}</TableCell>
                    <TableCell><HoldingStatus oldestCollectedAt={h.oldestCollectedAt} /></TableCell>
                    {canReconcile && (
                      <TableCell className="text-right">
                        <Button type="button" size="sm" onClick={() => openCreate({ riderId: h.riderId, branchId: h.branchId })}>
                          Record deposit
                        </Button>
                      </TableCell>
                    )}
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )}
      </SectionCard>

      <SectionCard title="Recent deposits" icon={ReceiptText} description="Deposits recorded in the selected range.">
        {deposits.isError ? (
          <ErrorState title="Couldn't load deposits" onRetry={() => deposits.refetch?.()} />
        ) : (
          <DataTable
            columns={depositColumns}
            data={deposits.data}
            isLoading={deposits.isLoading}
            emptyTitle="No deposits in this range"
            emptyDescription="Deposits appear here once a rider hands over collected COD."
            onPageChange={setPage}
            rowKey={(r) => r.id}
          />
        )}
      </SectionCard>
    </div>
  );
}
