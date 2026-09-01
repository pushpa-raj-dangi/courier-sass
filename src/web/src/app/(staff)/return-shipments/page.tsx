"use client";

import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useReturnShipments } from "@/features/ndr/hooks/useReturnShipments";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";
import type { ReturnShipment } from "@/features/ndr/types";

export default function ReturnShipmentsPage() {
  const { data, isLoading, isError, refetch } = useReturnShipments();
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
        title="Couldn't load return shipments"
        description="Something went wrong fetching the RTO list."
        onRetry={() => refetch()}
      />
    );
  }

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
      <DataTable
        columns={columns}
        data={pagedData}
        isLoading={false}
        emptyTitle="No return shipments"
        emptyDescription="Shipments resolved as RTO will show up here."
        onPageChange={() => {}}
        rowKey={(r) => r.id}
      />
    </div>
  );
}
