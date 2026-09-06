"use client";

import { useState } from "react";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useSession } from "@/features/auth/hooks/useSession";
import { useNdrs } from "@/features/ndr/hooks/useNdrs";
import { useNdrPanelStore } from "@/features/ndr/store/ndrPanelStore";
import type { Ndr, NdrStatus } from "@/features/ndr/types";

const STATUS_OPTIONS: NdrStatus[] = ["Open", "Contacting", "CustomerConfirmed", "ReattemptScheduled", "RtoRequested", "Resolved"];

export default function NdrsPage() {
  const [status, setStatus] = useState<NdrStatus | "all">("all");
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const { data, isLoading, isError, refetch } = useNdrs({
    status: status === "all" ? undefined : status,
    page,
    pageSize,
  });
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

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load NDRs"
        description="Something went wrong fetching the NDR queue."
        onRetry={() => refetch()}
      />
    );
  }

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
      <DataTable
        columns={columns}
        data={data}
        isLoading={false}
        emptyTitle="No NDRs"
        emptyDescription="Failed deliveries that need follow-up will show up here."
        onPageChange={setPage}
        rowKey={(n) => n.id}
      />
    </div>
  );
}
