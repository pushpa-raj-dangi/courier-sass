"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useBranchSettlements } from "@/features/cod/hooks/useBranchSettlements";
import { useFilterPage } from "@/lib/hooks/useFilterPage";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { formatDate, formatRs } from "@/features/cod/lib/codFormat";
import type { BranchCashSettlement } from "@/features/cod/types";

// Branch and date range come from the /cod page header, like the flow strip.
export function BranchSettlementsTab({ branchId, from, to }: { branchId?: string; from?: string; to?: string }) {
  const [unverifiedOnly, setUnverifiedOnly] = useState(false);
  const [page, setPage] = useFilterPage([unverifiedOnly, branchId, from, to].join("|"));
  const pageSize = 20;

  const { data: branches } = useBranches();
  const { data, isLoading } = useBranchSettlements({
    branchId,
    from,
    to,
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
    { key: "confirmedAmount", header: "Confirmed", render: (b) => formatRs(b.confirmedAmount) },
    { key: "riderCount", header: "Rider deposits", render: (b) => String(b.riderCashSettlementIds.length) },
    { key: "reconciledAt", header: "Reconciled on", render: (b) => formatDate(b.reconciledAt) },
  ];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Checkbox
            id="unverified-only"
            checked={unverifiedOnly}
            onCheckedChange={(checked) => setUnverifiedOnly(checked === true)}
          />
          <Label htmlFor="unverified-only">Unverified only</Label>
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
