"use client";

import { Plus, FileText, Lock, Truck, PackageCheck, CheckCircle2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useUiStore } from "@/lib/store/uiStore";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useManifests } from "@/features/manifests/hooks/useManifests";
import { useManifestsSummary } from "@/features/manifests/hooks/useManifestsSummary";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";
import type { Manifest } from "@/features/manifests/types";

function buildTiles(counts: Record<string, number>): StatTile[] {
  const value = (key: string) => (counts[key] ?? 0).toLocaleString();
  return [
    { label: "Draft", value: value("Draft"), icon: FileText, badgeClass: "bg-status-created/15 text-status-created" },
    { label: "Sealed", value: value("Sealed"), icon: Lock, badgeClass: "bg-status-sealed/15 text-status-sealed" },
    { label: "In Transit", value: value("InTransit"), icon: Truck, badgeClass: "bg-status-in-transit/15 text-status-in-transit" },
    { label: "Received", value: value("Received"), icon: PackageCheck, badgeClass: "bg-status-received/15 text-status-received" },
    { label: "Closed", value: value("Closed"), icon: CheckCircle2, badgeClass: "bg-status-delivered/15 text-status-delivered" },
  ];
}

export default function ManifestsPage() {
  const activeBranchId = useUiStore((s) => s.activeBranchId);
  const { data, isLoading, isError, refetch } = useManifests(activeBranchId ?? undefined);
  const summary = useManifestsSummary();
  const { data: branches } = useBranches();
  const canCreate = usePermission("manifest.create");
  const openCreate = useManifestPanelStore((s) => s.openCreate);
  const openDetail = useManifestPanelStore((s) => s.openDetail);

  const branchName = (id: string) => branches?.find((b) => b.id === id)?.name ?? "—";

  const columns: ColumnDef<Manifest>[] = [
    {
      key: "code",
      header: "Code",
      render: (m) => (
        <button
          type="button"
          onClick={() => openDetail(m.id)}
          className="font-medium text-foreground hover:underline"
        >
          {m.code}
        </button>
      ),
    },
    { key: "fromBranch", header: "From Branch", render: (m) => branchName(m.fromBranchId) },
    { key: "toBranch", header: "To Branch", render: (m) => branchName(m.toBranchId) },
    { key: "purpose", header: "Purpose", render: (m) => m.purpose },
    {
      key: "status",
      header: "Status",
      render: (m) => <StatusBadge status={m.status} namespace="Manifest" />,
    },
    { key: "shipmentCount", header: "Shipments", render: (m) => String(m.shipmentCount) },
    {
      key: "createdAt",
      header: "Created At",
      render: (m) => new Date(m.createdAt).toLocaleDateString(),
    },
  ];

  const pagedData: PagedResult<Manifest> | undefined = data && {
    items: data,
    page: 1,
    pageSize: data.length,
    totalCount: data.length,
    totalPages: 1,
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Manifests</h1>
        {canCreate && (
          <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            New Manifest
          </Button>
        )}
      </div>
      <SummaryStatsBar
        tiles={summary.data ? buildTiles(summary.data) : []}
        isLoading={summary.isLoading}
        isError={summary.isError}
        skeletonCount={5}
      />
      {isLoading && (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      )}
      {!isLoading && isError && (
        <ErrorState
          title="Couldn't load manifests"
          description="Something went wrong fetching the manifest list."
          onRetry={() => refetch()}
        />
      )}
      {!isLoading && !isError && (
        <DataTable
          columns={columns}
          data={pagedData}
          isLoading={false}
          emptyTitle="No manifests yet"
          emptyDescription="Create a manifest to start building an outgoing transfer."
          onPageChange={() => {}}
          rowKey={(m) => m.id}
        />
      )}
    </div>
  );
}
