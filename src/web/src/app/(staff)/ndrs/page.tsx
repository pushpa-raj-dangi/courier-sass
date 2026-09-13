"use client";

import { useState } from "react";
import { AlertTriangle, Phone, CheckCircle2, RotateCcw, Undo2 } from "lucide-react";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar";
import { useSession } from "@/features/auth/hooks/useSession";
import { useNdrs } from "@/features/ndr/hooks/useNdrs";
import { useNdrsSummary } from "@/features/ndr/hooks/useNdrsSummary";
import { useNdrPanelStore } from "@/features/ndr/store/ndrPanelStore";
import type { Ndr, NdrStatus } from "@/features/ndr/types";

const STATUS_OPTIONS: NdrStatus[] = ["Open", "Contacting", "CustomerConfirmed", "ReattemptScheduled", "RtoRequested", "Resolved"];

function buildTiles(counts: Record<string, number>): StatTile[] {
  const value = (key: string) => (counts[key] ?? 0).toLocaleString();
  return [
    { label: "Open", value: value("Open"), icon: AlertTriangle, badgeClass: "bg-status-ndr/15 text-status-ndr" },
    { label: "Contacting", value: value("Contacting"), icon: Phone, badgeClass: "bg-status-out-for-delivery/15 text-status-out-for-delivery" },
    { label: "Customer Confirmed", value: value("CustomerConfirmed"), icon: CheckCircle2, badgeClass: "bg-status-received/15 text-status-received" },
    { label: "Reattempt Scheduled", value: value("ReattemptScheduled"), icon: RotateCcw, badgeClass: "bg-status-sealed/15 text-status-sealed" },
    { label: "RTO Requested", value: value("RtoRequested"), icon: Undo2, badgeClass: "bg-status-rto/15 text-status-rto" },
    { label: "Resolved", value: value("Resolved"), icon: CheckCircle2, badgeClass: "bg-status-delivered/15 text-status-delivered", highlight: true },
  ];
}

export default function NdrsPage() {
  const [status, setStatus] = useState<NdrStatus | "all">("all");
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const { data, isLoading, isError, refetch } = useNdrs({
    status: status === "all" ? undefined : status,
    page,
    pageSize,
  });
  const summary = useNdrsSummary();
  const openDetail = useNdrPanelStore((s) => s.openDetail);
  const { data: session } = useSession();

  const columns: ColumnDef<Ndr>[] = [
    {
      key: "awb",
      header: "AWB",
      render: (n) => (
        <button type="button" onClick={() => openDetail(n.id)} className="font-medium text-foreground hover:underline">
          {n.awb}
        </button>
      ),
    },
    { key: "reason", header: "Reason", render: (n) => n.reason },
    { key: "status", header: "Status", render: (n) => <StatusBadge status={n.status} namespace="Ndr" /> },
    { key: "createdAt", header: "Created At", render: (n) => new Date(n.createdAt).toLocaleDateString() },
    {
      key: "assignedStaff",
      header: "Assigned Staff",
      render: (n) =>
        !n.assignedStaffUserId ? "—" : n.assignedStaffUserId === session?.userId ? "You" : "Assigned",
    },
  ];

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">NDRs</h1>
        <Select
          value={status}
          onValueChange={(v) => {
            setStatus(v as NdrStatus | "all");
            setPage(1);
          }}
        >
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
      </div>
      <SummaryStatsBar
        tiles={summary.data ? buildTiles(summary.data) : []}
        isLoading={summary.isLoading}
        isError={summary.isError}
        skeletonCount={6}
      />
      {isError ? (
        <ErrorState
          title="Couldn't load NDRs"
          description="Something went wrong fetching the NDR queue."
          onRetry={() => refetch()}
        />
      ) : (
        <DataTable
          columns={columns}
          data={data}
          isLoading={isLoading}
          emptyTitle="No NDRs"
          emptyDescription="Failed deliveries that need follow-up will show up here."
          onPageChange={setPage}
          rowKey={(n) => n.id}
        />
      )}
    </div>
  );
}
