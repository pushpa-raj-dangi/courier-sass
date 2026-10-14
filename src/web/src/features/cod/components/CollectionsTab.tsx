"use client";

import { useState } from "react";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useCodCollections } from "@/features/cod/hooks/useCodCollections";
import { useFilterPage } from "@/features/cod/hooks/useFilterPage";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { collectionLabel, formatDate, formatRs } from "@/features/cod/lib/codFormat";
import type { CodCollection, CodCollectionStatus } from "@/features/cod/types";

const STATUS_OPTIONS: CodCollectionStatus[] = [
  "CodExpected", "CodCollected", "RiderDeposited", "BranchReconciled", "FinanceVerified", "MerchantSettlementCreated", "Settled",
];

function formatAmount(value: number | null): string {
  return value === null ? "—" : formatRs(value);
}

// Branch and date range come from the /cod page header, like the flow strip.
export function CollectionsTab({ branchId, from, to }: { branchId?: string; from?: string; to?: string }) {
  const [status, setStatus] = useState<CodCollectionStatus | "all">("all");
  const [merchantId, setMerchantId] = useState<string | "all">("all");
  const [page, setPage] = useFilterPage([status, merchantId, branchId, from, to].join("|"));
  const pageSize = 20;

  const { data: merchants } = useMerchants();
  const { data, isLoading } = useCodCollections({
    status: status === "all" ? undefined : status,
    branchId,
    from,
    to,
    merchantId: merchantId === "all" ? undefined : merchantId,
    page,
    pageSize,
  });
  const openDetail = useCodPanelStore((s) => s.openCollectionDetail);

  const columns: ColumnDef<CodCollection>[] = [
    {
      key: "awb",
      header: "AWB",
      render: (c) => (
        <button type="button" onClick={() => openDetail(c.id)} className="font-mono font-medium text-foreground hover:underline">
          {collectionLabel(c)}
        </button>
      ),
    },
    { key: "expectedAmount", header: "Expected", render: (c) => formatAmount(c.expectedAmount) },
    { key: "collectedAmount", header: "Collected", render: (c) => formatAmount(c.collectedAmount) },
    { key: "status", header: "Status", render: (c) => <StatusBadge status={c.status} namespace="Cod" /> },
    { key: "collectedAt", header: "Collected on", render: (c) => (c.collectedAt ? formatDate(c.collectedAt) : "—") },
  ];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <Select value={status} onValueChange={(v) => setStatus(v as CodCollectionStatus | "all")}>
          <SelectTrigger className="w-48" aria-label="Filter by status">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All statuses</SelectItem>
            {STATUS_OPTIONS.map((s) => (
              <SelectItem key={s} value={s}>{s}</SelectItem>
            ))}
          </SelectContent>
        </Select>
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
      </div>
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No COD collections"
        emptyDescription="COD collections are created automatically when a COD shipment is booked."
        onPageChange={setPage}
        rowKey={(c) => c.id}
      />
    </div>
  );
}
