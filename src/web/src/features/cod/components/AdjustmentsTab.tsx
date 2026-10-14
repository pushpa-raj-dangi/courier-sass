"use client";

import { useState } from "react";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useAdjustments } from "@/features/cod/hooks/useAdjustments";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { formatDate, formatRs } from "@/features/cod/lib/codFormat";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import type { Adjustment } from "@/features/cod/types";

export function AdjustmentsTab() {
  const names = usePeopleNames();
  const [applied, setApplied] = useState<"all" | "pending" | "applied">("all");
  const [page, setPage] = useState(1);
  const pageSize = 20;

  const { data, isLoading } = useAdjustments({
    applied: applied === "all" ? undefined : applied === "applied",
    page,
    pageSize,
  });
  const openDetail = useCodPanelStore((s) => s.openAdjustmentDetail);

  const columns: ColumnDef<Adjustment>[] = [
    {
      key: "entityType",
      header: "Entity Type",
      render: (a) => (
        <button type="button" onClick={() => openDetail(a.id)} className="font-medium text-foreground hover:underline">
          {a.entityType}
        </button>
      ),
    },
    { key: "beforeValue", header: "Before", render: (a) => formatRs(a.beforeValue) },
    { key: "afterValue", header: "After", render: (a) => formatRs(a.afterValue) },
    { key: "reason", header: "Reason", render: (a) => a.reason },
    { key: "requestedBy", header: "Requested by", render: (a) => names.userName(a.requestedByUserId) },
    { key: "requestedAt", header: "Requested on", render: (a) => formatDate(a.requestedAt) },
    { key: "applied", header: "Status", render: (a) => (a.applied ? "Applied" : "Pending") },
  ];

  return (
    <div className="space-y-4">
      <Select value={applied} onValueChange={(v) => { setApplied(v as "all" | "pending" | "applied"); setPage(1); }}>
        <SelectTrigger className="w-48" aria-label="Filter by status">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">All</SelectItem>
          <SelectItem value="pending">Pending</SelectItem>
          <SelectItem value="applied">Applied</SelectItem>
        </SelectContent>
      </Select>
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No adjustments"
        emptyDescription="Financial adjustments to merchant settlement line items will show up here."
        onPageChange={setPage}
        rowKey={(a) => a.id}
      />
    </div>
  );
}
