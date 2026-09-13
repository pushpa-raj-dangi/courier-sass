"use client";

import { Plus, Building2, CheckCircle2, XCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useBranchesSummary, type BranchSummary } from "@/features/branches/hooks/useBranchesSummary";
import { useBranchPanelStore } from "@/features/branches/store/branchPanelStore";
import type { Branch } from "@/features/branches/types";

// Total/Active/Inactive only -- BranchType has no established color-token mapping the way
// shipment/manifest/NDR statuses do (BranchType isn't rendered via StatusBadge anywhere), so a
// per-type tile row would need 5 more neutral tiles with no color signal to differentiate them.
// byType stays in the API response for a future iteration rather than forcing that now (YAGNI).
function buildTiles(summary: BranchSummary): StatTile[] {
  return [
    { label: "Total Branches", value: summary.total.toLocaleString(), icon: Building2, badgeClass: "bg-muted text-muted-foreground" },
    { label: "Active", value: summary.active.toLocaleString(), icon: CheckCircle2, badgeClass: "bg-status-delivered/15 text-status-delivered", highlight: true },
    { label: "Inactive", value: summary.inactive.toLocaleString(), icon: XCircle, badgeClass: "bg-status-cancelled/15 text-status-cancelled" },
  ];
}

export default function BranchesPage() {
  const { data, isLoading, isError, refetch } = useBranches();
  const summary = useBranchesSummary();
  const canCreate = usePermission("branch.create");
  const openCreate = useBranchPanelStore((s) => s.openCreate);
  const openDetail = useBranchPanelStore((s) => s.openDetail);

  const columns: ColumnDef<Branch>[] = [
    { key: "code", header: "Code", render: (b) => b.code },
    {
      key: "name",
      header: "Name",
      render: (b) => (
        <button
          type="button"
          onClick={() => openDetail(b.id)}
          className="font-medium text-foreground hover:underline"
        >
          {b.name}
        </button>
      ),
    },
    { key: "type", header: "Type", render: (b) => b.type },
    { key: "active", header: "Active", render: (b) => (b.isActive ? "Yes" : "No") },
  ];

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Branches</h1>
        {canCreate && (
          <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            New Branch
          </Button>
        )}
      </div>
      <SummaryStatsBar
        tiles={summary.data ? buildTiles(summary.data) : []}
        isLoading={summary.isLoading}
        isError={summary.isError}
        skeletonCount={3}
      />
      {isError ? (
        <ErrorState
          title="Couldn't load branches"
          description="Something went wrong fetching the branch list."
          onRetry={() => refetch()}
        />
      ) : (
        <DataTable
          columns={columns}
          data={
            data && {
              items: data,
              page: 1,
              pageSize: data.length,
              totalCount: data.length,
              totalPages: 1,
            }
          }
          isLoading={isLoading}
          emptyTitle="No branches yet"
          emptyDescription="Create your organization's first branch to get started."
          onPageChange={() => {}}
          rowKey={(b) => b.id}
        />
      )}
    </div>
  );
}
