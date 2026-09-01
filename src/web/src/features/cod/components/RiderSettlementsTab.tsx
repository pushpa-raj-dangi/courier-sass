"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useRiderSettlements } from "@/features/cod/hooks/useRiderSettlements";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { usePermission } from "@/features/auth/hooks/usePermission";
import type { RiderCashSettlement } from "@/features/cod/types";

export function RiderSettlementsTab() {
  const [branchId, setBranchId] = useState<string | "all">("all");
  const [unreconciledOnly, setUnreconciledOnly] = useState(false);
  const [page, setPage] = useState(1);
  const pageSize = 20;

  const { data: branches } = useBranches();
  const { data: riders } = useRiders();
  const { data, isLoading } = useRiderSettlements({
    branchId: branchId === "all" ? undefined : branchId,
    unreconciledOnly: unreconciledOnly || undefined,
    page,
    pageSize,
  });
  const openDetail = useCodPanelStore((s) => s.openRiderSettlementDetail);
  const openCreate = useCodPanelStore((s) => s.openRiderSettlementCreate);
  const canReconcile = usePermission("cod.reconcile");

  const branchesById = new Map((branches ?? []).map((b) => [b.id, b.name]));
  const ridersById = new Map((riders ?? []).map((r) => [r.id, r.licenseNumber]));

  const columns: ColumnDef<RiderCashSettlement>[] = [
    {
      key: "riderId",
      header: "Rider",
      render: (r) => (
        <button type="button" onClick={() => openDetail(r.id)} className="font-medium text-foreground hover:underline">
          {ridersById.get(r.riderId) ?? r.riderId}
        </button>
      ),
    },
    { key: "branchId", header: "Branch", render: (r) => branchesById.get(r.branchId) ?? r.branchId },
    { key: "declaredAmount", header: "Declared Amount", render: (r) => `Rs. ${r.declaredAmount.toFixed(2)}` },
    { key: "shipmentCount", header: "Shipment Count", render: (r) => String(r.shipmentCount) },
    { key: "settledAt", header: "Settled At", render: (r) => new Date(r.settledAt).toLocaleDateString() },
    { key: "reconciled", header: "Reconciled", render: (r) => (r.branchCashSettlementId ? "Yes" : "Not yet") },
  ];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-3">
          <Select value={branchId} onValueChange={(v) => { setBranchId(v); setPage(1); }}>
            <SelectTrigger className="w-48" aria-label="Filter by branch">
              <SelectValue placeholder="All branches" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All branches</SelectItem>
              {branches?.map((b) => (
                <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <div className="flex items-center gap-2">
            <Checkbox
              id="unreconciled-only"
              checked={unreconciledOnly}
              onCheckedChange={(checked) => { setUnreconciledOnly(checked === true); setPage(1); }}
            />
            <Label htmlFor="unreconciled-only">Unreconciled only</Label>
          </div>
        </div>
        {canReconcile && <Button type="button" onClick={openCreate}>New Settlement</Button>}
      </div>
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No rider settlements"
        emptyDescription="Rider settlements are created when a rider deposits their collected COD."
        onPageChange={setPage}
        rowKey={(r) => r.id}
      />
    </div>
  );
}
