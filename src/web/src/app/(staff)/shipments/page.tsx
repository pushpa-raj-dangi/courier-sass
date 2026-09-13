"use client";

import { useState } from "react";
import { Plus, Package, Truck, Send, CheckCircle2, AlertTriangle, Undo2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useShipments } from "@/features/shipments/hooks/useShipments";
import { useShipmentsSummary } from "@/features/shipments/hooks/useShipmentsSummary";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import type { Shipment } from "@/features/shipments/types";

const currencyFormatter = new Intl.NumberFormat("en-NP", {
  style: "currency",
  currency: "NPR",
  notation: "compact",
  maximumFractionDigits: 1,
});

// Bucket boundaries mirror components/status-badge/statusConfig.ts's own token groups exactly
// (not an independently invented split) so a tile's color always matches every StatusBadge of
// the statuses it counts, in the table directly below it.
function buildTiles(counts: Record<string, number>): StatTile[] {
  const sum = (...keys: string[]) => keys.reduce((total, key) => total + (counts[key] ?? 0), 0);
  return [
    { label: "Created", value: sum("Created", "PickupAssigned").toLocaleString(), icon: Package, badgeClass: "bg-status-created/15 text-status-created" },
    { label: "In Transit", value: sum("PickedUp", "AtOriginBranch", "Sorted", "InTransit", "AtDestinationBranch").toLocaleString(), icon: Truck, badgeClass: "bg-status-in-transit/15 text-status-in-transit" },
    { label: "Out for Delivery", value: sum("OutForDelivery").toLocaleString(), icon: Send, badgeClass: "bg-status-out-for-delivery/15 text-status-out-for-delivery" },
    { label: "Delivered", value: sum("Delivered").toLocaleString(), icon: CheckCircle2, badgeClass: "bg-status-delivered/15 text-status-delivered", highlight: true },
    { label: "NDR", value: sum("DeliveryFailed", "NdrCreated").toLocaleString(), icon: AlertTriangle, badgeClass: "bg-status-ndr/15 text-status-ndr" },
    { label: "RTO", value: sum("RtoInitiated", "RtoInTransit", "RtoAtOrigin", "RtoDelivered").toLocaleString(), icon: Undo2, badgeClass: "bg-status-rto/15 text-status-rto" },
  ];
}

export default function ShipmentsPage() {
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const { data, isLoading, isError, refetch } = useShipments(page, pageSize);
  const summary = useShipmentsSummary();
  const { data: serviceTypes } = useServiceTypes();
  const canCreate = usePermission("shipment.create");
  const openCreate = useShipmentPanelStore((s) => s.openCreate);
  const openDetail = useShipmentPanelStore((s) => s.openDetail);

  const serviceTypeName = (id: string) => serviceTypes?.find((s) => s.id === id)?.name ?? "—";

  const columns: ColumnDef<Shipment>[] = [
    {
      key: "awb",
      header: "AWB",
      render: (s) => (
        <button
          type="button"
          onClick={() => openDetail(s.awb)}
          className="font-medium text-foreground hover:underline"
        >
          {s.awb}
        </button>
      ),
    },
    { key: "recipient", header: "Recipient", render: (s) => s.recipientName },
    { key: "status", header: "Status", render: (s) => <StatusBadge status={s.currentStatus} /> },
    { key: "serviceType", header: "Service Type", render: (s) => serviceTypeName(s.serviceTypeId) },
    { key: "cod", header: "COD Amount", render: (s) => currencyFormatter.format(s.codAmount) },
    {
      key: "createdAt",
      header: "Created At",
      render: (s) => new Date(s.createdAt).toLocaleDateString(),
    },
  ];

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Shipments</h1>
        {canCreate && (
          <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            New Shipment
          </Button>
        )}
      </div>
      <SummaryStatsBar
        tiles={summary.data ? buildTiles(summary.data) : []}
        isLoading={summary.isLoading}
        isError={summary.isError}
        skeletonCount={6}
      />
      {isLoading && (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      )}
      {!isLoading && isError && (
        <ErrorState
          title="Couldn't load shipments"
          description="Something went wrong fetching the shipment list."
          onRetry={() => refetch()}
        />
      )}
      {!isLoading && !isError && (
        <DataTable
          columns={columns}
          data={data}
          isLoading={false}
          emptyTitle="No shipments yet"
          emptyDescription="Book your first shipment to get started."
          onPageChange={setPage}
          rowKey={(s) => s.id}
        />
      )}
    </div>
  );
}
