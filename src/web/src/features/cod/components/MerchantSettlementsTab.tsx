"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { EmptyState } from "@/components/states/EmptyState";
import { LoadingState } from "@/components/states/LoadingState";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useMerchantSettlements } from "@/features/cod/hooks/useMerchantSettlements";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { usePermission } from "@/features/auth/hooks/usePermission";

// MerchantSettlement's list endpoint is a plain, non-paginated array (Global Constraints) — this
// tab renders the raw Table primitives directly instead of DataTable, which assumes a PagedResult.
export function MerchantSettlementsTab() {
  const [merchantId, setMerchantId] = useState<string | "all">("all");
  const { data: merchants } = useMerchants();
  const { data: settlements, isLoading } = useMerchantSettlements(merchantId === "all" ? undefined : merchantId);
  const openDetail = useCodPanelStore((s) => s.openMerchantSettlementDetail);
  const openCreate = useCodPanelStore((s) => s.openMerchantSettlementCreate);
  const canSettle = usePermission("cod.settle");

  const merchantsById = new Map((merchants ?? []).map((m) => [m.id, m.displayName]));

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
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
        {canSettle && <Button type="button" onClick={openCreate}>New Settlement</Button>}
      </div>
      {isLoading && <LoadingState rows={5} />}
      {!isLoading && (settlements?.length ?? 0) === 0 && (
        <EmptyState title="No merchant settlements" description="Create one from finance-verified COD collections for a merchant and period." />
      )}
      {!isLoading && (settlements?.length ?? 0) > 0 && (
        <div className="overflow-hidden rounded-xl border border-border bg-muted/40">
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
                  <TableCell>{new Date(s.periodFrom).toLocaleDateString()} – {new Date(s.periodTo).toLocaleDateString()}</TableCell>
                  <TableCell>Rs. {s.totalPayable.toFixed(2)}</TableCell>
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
