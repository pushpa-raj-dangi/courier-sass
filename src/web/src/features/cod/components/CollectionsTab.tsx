"use client";

import { useState } from "react";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useCodCollections } from "@/features/cod/hooks/useCodCollections";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { formatDate, formatRs } from "@/features/cod/lib/codFormat";
import type { CodCollection, CodCollectionStatus } from "@/features/cod/types";

const STATUS_OPTIONS: CodCollectionStatus[] = [
  "CodExpected", "CodCollected", "RiderDeposited", "BranchReconciled", "FinanceVerified", "MerchantSettlementCreated", "Settled",
];

function formatAmount(value: number | null): string {
  return value === null ? "—" : formatRs(value);
}

export function CollectionsTab() {
  const [status, setStatus] = useState<CodCollectionStatus | "all">("all");
  const [branchId, setBranchId] = useState<string | "all">("all");
  const [merchantId, setMerchantId] = useState<string | "all">("all");
  const [page, setPage] = useState(1);
  const pageSize = 20;

  const { data: branches } = useBranches();
  const { data: merchants } = useMerchants();
  const { data, isLoading } = useCodCollections({
    status: status === "all" ? undefined : status,
    branchId: branchId === "all" ? undefined : branchId,
    merchantId: merchantId === "all" ? undefined : merchantId,
    page,
    pageSize,
  });
  const openDetail = useCodPanelStore((s) => s.openCollectionDetail);

  const columns: ColumnDef<CodCollection>[] = [
    {
      key: "shipmentId",
      header: "Shipment",
      render: (c) => (
        <button type="button" onClick={() => openDetail(c.id)} className="font-medium text-foreground hover:underline">
          {c.shipmentId.slice(0, 8)}
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
        <Select value={status} onValueChange={(v) => { setStatus(v as CodCollectionStatus | "all"); setPage(1); }}>
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
        <Select value={merchantId} onValueChange={(v) => { setMerchantId(v); setPage(1); }}>
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
