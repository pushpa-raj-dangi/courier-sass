"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { EmptyState } from "@/components/states/EmptyState";
import { LoadingState } from "@/components/states/LoadingState";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { BorderedTableContainer } from "@/components/data-table/BorderedTableContainer";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useMerchantSettlements } from "@/features/cod/hooks/useMerchantSettlements";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { MerchantLevelNote } from "@/features/cod/components/MerchantLevelNote";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { formatDate, formatRs } from "@/features/cod/lib/codFormat";

// MerchantSettlement's list endpoint is a plain, non-paginated array (Global Constraints) — this
// tab renders the raw Table primitives directly instead of DataTable, which assumes a PagedResult.
// Merchant settlements have no branch — only the page's date range applies (period overlap).
export function MerchantSettlementsTab({ branchFiltered = false, from, to }: { branchFiltered?: boolean; from?: string; to?: string }) {
  const [merchantId, setMerchantId] = useState<string | "all">("all");
  const { data: merchants } = useMerchants();
  const { data: settlements, isLoading } = useMerchantSettlements(merchantId === "all" ? undefined : merchantId, { from, to });
  const openDetail = useCodPanelStore((s) => s.openMerchantSettlementDetail);
  const openCreate = useCodPanelStore((s) => s.openMerchantSettlementCreate);
  const canSettle = usePermission("cod.settle");

  const merchantsById = new Map((merchants ?? []).map((m) => [m.id, m.displayName]));

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-3">
          <Select value={merchantId} onValueChange={setMerchantId}>
            <SelectTrigger className="w-48" aria-label="Filter by merchant">
              <SelectValue placeholder="All merchants" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All merchants</SelectItem>
              {merchants?.map((m) => (
                <SelectItem key={m.id} value={m.id}>{m.displayName}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          {branchFiltered && <MerchantLevelNote />}
        </div>
        {canSettle && <Button type="button" onClick={openCreate}>New Settlement</Button>}
      </div>
      {isLoading && <LoadingState rows={5} />}
      {!isLoading && (settlements?.length ?? 0) === 0 && (
        <EmptyState title="No merchant settlements" description="Create one from finance-verified COD collections for a merchant and period." />
      )}
      {!isLoading && (settlements?.length ?? 0) > 0 && (
        <BorderedTableContainer>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Merchant</TableHead>
                <TableHead>Period</TableHead>
                <TableHead>Total Payable</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Settled At</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {settlements!.map((s) => (
                <TableRow key={s.id}>
                  <TableCell>
                    <button type="button" onClick={() => openDetail(s.id)} className="font-medium text-foreground hover:underline">
                      {merchantsById.get(s.merchantId) ?? s.merchantId}
                    </button>
                  </TableCell>
                  <TableCell>{formatDate(s.periodFrom)} – {formatDate(s.periodTo)}</TableCell>
                  <TableCell>{formatRs(s.totalPayable)}</TableCell>
                  <TableCell><StatusBadge status={s.status} namespace="MerchantSettlement" /></TableCell>
                  <TableCell>{s.settledAt ? formatDate(s.settledAt) : "—"}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </BorderedTableContainer>
      )}
    </div>
  );
}
