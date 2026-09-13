"use client";

import { Undo2, Truck, PackageCheck, CheckCircle2, Clock } from "lucide-react";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { ErrorState } from "@/components/states/ErrorState";
import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useReturnShipments } from "@/features/ndr/hooks/useReturnShipments";
import { useReturnShipmentsSummary, type ReturnShipmentSummary } from "@/features/ndr/hooks/useReturnShipmentsSummary";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";
import type { ReturnShipment } from "@/features/ndr/types";

function buildTiles(summary: ReturnShipmentSummary): StatTile[] {
  const stage = (key: string) => (summary.byRtoStage[key] ?? 0).toLocaleString();
  return [
    { label: "RTO Initiated", value: stage("RtoInitiated"), icon: Undo2, badgeClass: "bg-status-rto/15 text-status-rto" },
    { label: "RTO In Transit", value: stage("RtoInTransit"), icon: Truck, badgeClass: "bg-status-rto/15 text-status-rto" },
    { label: "RTO At Origin", value: stage("RtoAtOrigin"), icon: PackageCheck, badgeClass: "bg-status-rto/15 text-status-rto" },
    { label: "RTO Delivered", value: stage("RtoDelivered"), icon: CheckCircle2, badgeClass: "bg-status-rto/15 text-status-rto" },
    { label: "Merchant Confirmed", value: summary.merchantConfirmed.toLocaleString(), icon: CheckCircle2, badgeClass: "bg-status-delivered/15 text-status-delivered", highlight: true },
    { label: "Pending Confirmation", value: summary.pendingConfirmation.toLocaleString(), icon: Clock, badgeClass: "bg-muted text-muted-foreground" },
  ];
}

export default function ReturnShipmentsPage() {
  const { data, isLoading, isError, refetch } = useReturnShipments();
  const summary = useReturnShipmentsSummary();
  const { data: branches } = useBranches();
  const openDetail = useReturnShipmentPanelStore((s) => s.openDetail);

  const branchName = (id: string) => branches?.find((b) => b.id === id)?.name ?? "—";

  const columns: ColumnDef<ReturnShipment>[] = [
    {
      key: "returnAwb",
      header: "Return AWB",
      render: (r) => (
        <button type="button" onClick={() => openDetail(r.id)} className="font-medium text-foreground hover:underline">
          {r.returnAwb}
        </button>
      ),
    },
    { key: "originalAwb", header: "Original AWB", render: (r) => r.originalAwb },
    { key: "status", header: "Status", render: (r) => <StatusBadge status={r.currentStatus} /> },
    { key: "originBranch", header: "Origin Branch", render: (r) => branchName(r.originBranchId) },
    { key: "destinationBranch", header: "Destination Branch", render: (r) => branchName(r.destinationBranchId) },
    {
      key: "merchantConfirmedAt",
      header: "Merchant Confirmed",
      render: (r) => (r.merchantConfirmedAt ? new Date(r.merchantConfirmedAt).toLocaleDateString() : "—"),
    },
  ];

  const pagedData: PagedResult<ReturnShipment> | undefined = data && {
    items: data,
    page: 1,
    pageSize: data.length,
    totalCount: data.length,
    totalPages: 1,
  };

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">Return Shipments</h1>
      <SummaryStatsBar
        tiles={summary.data ? buildTiles(summary.data) : []}
        isLoading={summary.isLoading}
        isError={summary.isError}
        skeletonCount={6}
      />
      {isError ? (
        <ErrorState
          title="Couldn't load return shipments"
          description="Something went wrong fetching the RTO list."
          onRetry={() => refetch()}
        />
      ) : (
        <DataTable
          columns={columns}
          data={pagedData}
          isLoading={isLoading}
          emptyTitle="No return shipments"
          emptyDescription="Shipments resolved as RTO will show up here."
          onPageChange={() => {}}
          rowKey={(r) => r.id}
        />
      )}
    </div>
  );
}
