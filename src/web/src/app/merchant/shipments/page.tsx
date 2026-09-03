"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import { useMerchantShipments } from "@/features/merchant-shipments/hooks/useMerchantShipments";
import { useMerchantServiceTypes } from "@/features/merchant-shipments/hooks/useMerchantServiceTypes";
import { useMerchantShipmentPanelStore } from "@/features/merchant-shipments/store/merchantShipmentPanelStore";
import type { Shipment } from "@/features/merchant-shipments/types";

const currencyFormatter = new Intl.NumberFormat("en-NP", {
  style: "currency",
  currency: "NPR",
  notation: "compact",
  maximumFractionDigits: 1,
});

export default function MerchantShipmentsPage() {
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const { data, isLoading, isError, refetch } = useMerchantShipments(page, pageSize);
  const { data: serviceTypes } = useMerchantServiceTypes();
  const canCreate = useMerchantPermission("shipment.create");
  const openCreate = useMerchantShipmentPanelStore((s) => s.openCreate);
  const openDetail = useMerchantShipmentPanelStore((s) => s.openDetail);

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
        title="Couldn't load shipments"
        description="Something went wrong fetching your shipment list."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Shipments</h1>
        {canCreate && (
          <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            Book a Shipment
          </Button>
        )}
      </div>
      <DataTable
        columns={columns}
        data={data}
        isLoading={false}
        emptyTitle="No shipments yet"
        emptyDescription="Book your first shipment to get started."
        onPageChange={setPage}
        rowKey={(s) => s.id}
      />
    </div>
  );
}
