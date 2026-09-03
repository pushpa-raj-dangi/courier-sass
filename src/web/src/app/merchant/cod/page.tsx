"use client";

import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { EmptyState } from "@/components/states/EmptyState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useMerchantSettlements } from "@/features/merchant-cod/hooks/useMerchantSettlements";
import { useMerchantSettlementPanelStore } from "@/features/merchant-cod/store/merchantSettlementPanelStore";

const currencyFormatter = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

export default function MerchantCodPage() {
  const { data: settlements, isLoading, isError, refetch } = useMerchantSettlements();
  const openDetail = useMerchantSettlementPanelStore((s) => s.openDetail);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load COD settlements"
        description="Something went wrong fetching your settlements."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">COD & Invoices</h1>
      {!settlements || settlements.length === 0 ? (
        <EmptyState title="No settlements yet" description="Settlements appear here once your courier issues one for a period." />
      ) : (
        <div className="overflow-hidden rounded-xl border border-border bg-muted/40">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Period</TableHead>
                <TableHead>Total Payable</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Settled At</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {settlements.map((s) => (
                <TableRow key={s.id} className="cursor-pointer" onClick={() => openDetail(s.id)}>
                  <TableCell>
                    {new Date(s.periodFrom).toLocaleDateString()} – {new Date(s.periodTo).toLocaleDateString()}
                  </TableCell>
                  <TableCell>{currencyFormatter.format(s.totalPayable)}</TableCell>
                  <TableCell><StatusBadge status={s.status} namespace="MerchantSettlement" /></TableCell>
                  <TableCell>{s.settledAt ? new Date(s.settledAt).toLocaleDateString() : "—"}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
