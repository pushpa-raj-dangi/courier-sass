"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useBranchSettlements } from "@/features/cod/hooks/useBranchSettlements";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { usePermission } from "@/features/auth/hooks/usePermission";
import type { BranchCashSettlement } from "@/features/cod/types";

export function BranchSettlementsTab() {
  const [branchId, setBranchId] = useState<string | "all">("all");
  const [unverifiedOnly, setUnverifiedOnly] = useState(false);
  const [page, setPage] = useState(1);
  const pageSize = 20;

  const { data: branches } = useBranches();
  const { data, isLoading } = useBranchSettlements({
    branchId: branchId === "all" ? undefined : branchId,
    unverifiedOnly: unverifiedOnly || undefined,
    page,
    pageSize,
  });
  const openDetail = useCodPanelStore((s) => s.openBranchSettlementDetail);
  const openCreate = useCodPanelStore((s) => s.openBranchSettlementCreate);
  const canReconcile = usePermission("cod.reconcile");

  const branchesById = new Map((branches ?? []).map((b) => [b.id, b.name]));

  const columns: ColumnDef<BranchCashSettlement>[] = [
    {
      key: "branchId",
      header: "Branch",
      render: (b) => (
        <button type="button" onClick={() => openDetail(b.id)} className="font-medium text-foreground hover:underline">
          {branchesById.get(b.branchId) ?? b.branchId}
        </button>
      ),
    },
    { key: "confirmedAmount", header: "Confirmed Amount", render: (b) => `Rs. ${b.confirmedAmount.toFixed(2)}` },
    { key: "riderCount", header: "Rider Settlements", render: (b) => String(b.riderCashSettlementIds.length) },
    { key: "reconciledAt", header: "Reconciled At", render: (b) => new Date(b.reconciledAt).toLocaleDateString() },
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
              id="unverified-only"
              checked={unverifiedOnly}
              onCheckedChange={(checked) => { setUnverifiedOnly(checked === true); setPage(1); }}
            />
            <Label htmlFor="unverified-only">Unverified only</Label>
          </div>
        </div>
        {canReconcile && <Button type="button" onClick={openCreate}>New Settlement</Button>}
      </div>
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No branch settlements"
        emptyDescription="Branch settlements are created when a branch reconciles its riders' deposited COD."
        onPageChange={setPage}
        rowKey={(b) => b.id}
      />
    </div>
  );
}
