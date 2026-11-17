"use client";

import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { formatDate, formatRs, formatTime } from "@/features/cod/lib/codFormat";
import type { Branch, ServiceType, Shipment } from "@/features/merchant-shipments/types";

function when(iso: string) {
  return `${formatDate(iso)}, ${formatTime(iso)}`;
}

export function MerchantShipmentsTable({
  data,
  isLoading,
  branches,
  serviceTypes,
  onPageChange,
  onOpen,
}: {
  data?: PagedResult<Shipment>;
  isLoading: boolean;
  branches: Branch[] | undefined;
  serviceTypes: ServiceType[] | undefined;
  onPageChange: (page: number) => void;
  onOpen: (awb: string) => void;
}) {
  const branchName = (id: string) => branches?.find((b) => b.id === id)?.name ?? "—";
  const serviceName = (id: string) => serviceTypes?.find((s) => s.id === id)?.name ?? "—";

  const columns: ColumnDef<Shipment>[] = [
    {
      key: "awb",
      header: "AWB",
      render: (s) => (
        <div className="flex flex-col">
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              onOpen(s.awb);
            }}
            className="w-fit font-mono font-medium text-foreground hover:underline"
          >
            {s.awb}
          </button>
          <span className="text-2xs text-muted-foreground">{when(s.createdAt)}</span>
        </div>
      ),
    },
    {
      key: "recipient",
      header: "Recipient",
      render: (s) => (
        <div className="flex flex-col">
          <span>{s.recipientName}</span>
          <span className="text-2xs text-muted-foreground">{s.recipientAddressLine}</span>
        </div>
      ),
    },
    {
      key: "destination",
      header: "Destination",
      render: (s) => <span data-testid={`destination-${s.id}`}>{branchName(s.destinationBranchId)}</span>,
    },
    { key: "service", header: "Service", render: (s) => serviceName(s.serviceTypeId) },
    {
      key: "cod",
      header: "COD",
      render: (s) => (
        <span className="block text-right font-medium tabular-nums">{s.codAmount > 0 ? formatRs(s.codAmount) : "Prepaid"}</span>
      ),
    },
    {
      key: "charge",
      header: "Charge",
      render: (s) => <span className="block text-right tabular-nums">{formatRs(s.totalCharge)}</span>,
    },
    { key: "status", header: "Status", render: (s) => <StatusBadge status={s.currentStatus} namespace="Merchant" /> },
    {
      key: "updated",
      header: "Updated",
      render: (s) => <span className="text-xs text-muted-foreground">{when(s.lastEventAt ?? s.createdAt)}</span>,
    },
  ];

  return (
    <DataTable
      columns={columns}
      data={data}
      isLoading={isLoading}
      emptyTitle="No shipments here yet"
      emptyDescription="Shipments that match this tab and filters will show up here."
      onPageChange={onPageChange}
      rowKey={(s) => s.id}
      onRowClick={(s) => onOpen(s.awb)}
    />
  );
}
