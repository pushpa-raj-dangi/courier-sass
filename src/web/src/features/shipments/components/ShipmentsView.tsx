"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { PageHeader } from "@/components/page/PageHeader";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { useShipments } from "@/features/shipments/hooks/useShipments";
import { useShipmentsSummary } from "@/features/shipments/hooks/useShipmentsSummary";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import { EMPTY_LIST_PARAMS, hasActiveFilters, parseListParams, toSearchParams, type ShipmentListParams } from "@/features/shipments/lib/listParams";
import { ALL_SHIPMENT_STATUSES, SHIPMENT_TABS, sumStatuses, type ShipmentTab } from "@/features/shipments/lib/statusGroups";
import { formatRelativeTime } from "@/features/shipments/lib/relativeTime";
import type { Shipment } from "@/features/shipments/types";
import { ShipmentFilterBar } from "./ShipmentFilterBar";

const money = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });

export function ShipmentsView() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const params = parseListParams(new URLSearchParams(searchParams.toString()));

  const { data, isLoading, isError, refetch } = useShipments(params);
  const summary = useShipmentsSummary();
  const { data: branches } = useBranches();
  const { data: merchants } = useMerchants();
  const { data: serviceTypes } = useServiceTypes();
  const canCreate = usePermission("shipment.create");
  const openCreate = useShipmentPanelStore((s) => s.openCreate);
  const openDetail = useShipmentPanelStore((s) => s.openDetail);
  const panel = useShipmentPanelStore((s) => s.panel);

  function navigate(next: ShipmentListParams) {
    const qs = toSearchParams(next).toString();
    router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
  }
  // Any filter/tab change resets to page 1 and drops dashboard-supplied explicit statuses.
  function patch(change: Partial<ShipmentListParams>) {
    navigate({ ...params, statuses: "tab" in change ? [] : params.statuses, ...change, page: 1 });
  }

  const branchById = new Map((branches ?? []).map((b) => [b.id, b]));
  const merchantName = (id: string) => merchants?.find((m) => m.id === id)?.displayName ?? "—";
  const serviceName = (id: string) => serviceTypes?.find((s) => s.id === id)?.name ?? "—";
  const code = (id: string) => branchById.get(id)?.code ?? "—";
  const allCount = summary.data ? sumStatuses(summary.data, ALL_SHIPMENT_STATUSES) : undefined;
  const filtered = hasActiveFilters(params);
  const activeTab: ShipmentTab | "" = params.statuses.length > 0 ? "" : params.tab;

  const columns: ColumnDef<Shipment>[] = [
    {
      key: "awb",
      header: "AWB",
      render: (s) => (
        <div className="flex flex-col">
          <button type="button" onClick={() => openDetail(s.awb)} className="w-fit font-mono font-medium text-foreground hover:underline">
            {s.awb}
          </button>
          <span className="text-[11.5px] text-muted-foreground">{new Date(s.createdAt).toLocaleString()}</span>
        </div>
      ),
    },
    { key: "merchant", header: "Merchant", render: (s) => merchantName(s.merchantId) },
    {
      key: "recipient",
      header: "Recipient",
      render: (s) => (
        <div className="flex flex-col">
          <span>{s.recipientName}</span>
          <span className="text-[11.5px] text-muted-foreground">{s.recipientAddressLine}</span>
        </div>
      ),
    },
    { key: "route", header: "Route", render: (s) => <span className="font-mono text-xs">{`${code(s.originBranchId)} → ${code(s.destinationBranchId)}`}</span> },
    { key: "service", header: "Service", render: (s) => serviceName(s.serviceTypeId) },
    {
      key: "cod",
      header: "COD",
      render: (s) => <span className="block text-right font-medium tabular-nums">{s.codAmount > 0 ? `Rs ${money.format(s.codAmount)}` : "Prepaid"}</span>,
    },
    { key: "status", header: "Status", render: (s) => <StatusBadge status={s.currentStatus} /> },
    {
      key: "lastScan",
      header: "Last scan",
      render: (s) => <span className="text-xs text-muted-foreground">{s.lastEventAt ? formatRelativeTime(s.lastEventAt) : "—"}</span>,
    },
  ];

  return (
    <div className="space-y-5">
      <PageHeader
        title="Shipments"
        description={data ? `${data.totalCount.toLocaleString("en-US")} shipments in your branch scope` : undefined}
        actions={
          canCreate && (
            <Button className="gap-1.5" onClick={() => openCreate()}>
              <Plus className="h-4 w-4" aria-hidden="true" />
              Book shipment
            </Button>
          )
        }
      />

      <Tabs value={activeTab} onValueChange={(tab) => patch({ tab: tab as ShipmentTab })}>
        <TabsList className="w-full justify-start overflow-x-auto">
          {SHIPMENT_TABS.map((tab) => {
            const count = tab.id === "all" ? allCount : summary.data ? sumStatuses(summary.data, tab.statuses) : undefined;
            return (
              <TabsTrigger key={tab.id} value={tab.id} className="gap-2">
                {tab.label}
                {count !== undefined && (
                  <span className="rounded-full bg-muted px-1.5 text-[11.5px] tabular-nums text-muted-foreground">{count.toLocaleString("en-US")}</span>
                )}
              </TabsTrigger>
            );
          })}
        </TabsList>
      </Tabs>

      <ShipmentFilterBar params={params} onChange={patch} onClear={() => navigate({ ...EMPTY_LIST_PARAMS, tab: params.tab })} />

      {isError ? (
        <ErrorState title="Couldn't load shipments" description="Something went wrong fetching the shipment list." onRetry={() => refetch()} />
      ) : (
        <DataTable
          columns={columns}
          data={data}
          isLoading={isLoading}
          emptyTitle={filtered ? "No shipments match these filters" : "No shipments yet"}
          emptyDescription={filtered ? "Try a different search or clear the filters." : "Book your first shipment to get started."}
          onPageChange={(page) => navigate({ ...params, page })}
          rowKey={(s) => s.id}
          selectedRowId={panel.mode === "detail" ? data?.items.find((s) => s.awb === panel.awb)?.id : undefined}
        />
      )}
    </div>
  );
}
