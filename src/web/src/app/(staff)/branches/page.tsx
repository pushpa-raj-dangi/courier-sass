"use client";

import { Suspense, type ReactNode } from "react";
import { Plus } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { PageHeader } from "@/components/page/PageHeader";
import { PipelineStrip, type PipelineStage } from "@/components/page/PipelineStrip";
import { ErrorState } from "@/components/states/ErrorState";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useBranchActivity } from "@/features/branches/hooks/useBranchActivity";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useBranchesSummary } from "@/features/branches/hooks/useBranchesSummary";
import { BRANCH_TYPES, branchTypeLabel, filterBranches, parseBranchType, zeroActivity } from "@/features/branches/lib/branchModel";
import { useBranchPanelStore } from "@/features/branches/store/branchPanelStore";
import type { Branch, BranchActivityRow } from "@/features/branches/types";
import { useDistrictNames } from "@/features/geography/hooks/useDistrictNames";
import { useFilterPage } from "@/lib/hooks/useFilterPage";
import { useSearchParamState } from "@/lib/hooks/useSearchParamState";
import { toClientPage } from "@/lib/paging/clientPage";

const PAGE_SIZE = 20;

function BranchesPageContent() {
  const { params, update } = useSearchParamState();
  const type = parseBranchType(params.get("type"));
  const q = params.get("q") ?? "";
  const showInactive = params.get("inactive") === "1";

  const list = useBranches();
  const summary = useBranchesSummary();
  const activity = useBranchActivity();
  const districtNames = useDistrictNames((list.data ?? []).map((b) => b.provinceId));
  const canCreate = usePermission("branch.create");
  const panel = useBranchPanelStore((s) => s.panel);
  const openCreate = useBranchPanelStore((s) => s.openCreate);
  const openDetail = useBranchPanelStore((s) => s.openDetail);
  const [page, setPage] = useFilterPage(`${type ?? ""}|${q}|${showInactive ? 1 : 0}`);

  // Activity rows are sparse for all-branches users: once loaded, a missing branch is all zeros.
  const activityById = activity.data ? new Map(activity.data.items.map((r) => [r.branchId, r])) : undefined;
  const shown = list.data && filterBranches(list.data, { type, q, showInactive });

  function live(branchId: string, pick: (row: BranchActivityRow) => number): ReactNode {
    if (!activityById) return <span className="text-sm text-muted-foreground">—</span>;
    return <span className="text-sm tabular-nums">{pick(activityById.get(branchId) ?? zeroActivity(branchId)).toLocaleString("en-US")}</span>;
  }

  const stages: PipelineStage[] = BRANCH_TYPES.map((t) => ({
    key: t.type,
    label: t.label,
    token: "status-created",
    count: summary.data ? summary.data.byType[t.type] ?? 0 : null,
  }));

  const columns: ColumnDef<Branch>[] = [
    {
      key: "branch",
      header: "Branch",
      render: (b) => (
        <div className="flex items-center gap-2">
          <button type="button" onClick={() => openDetail(b.id)} className="flex flex-col items-start gap-0.5 text-left">
            <span className="text-sm font-medium text-foreground hover:underline">{b.name}</span>
            <span className="font-mono text-xs text-muted-foreground">{b.code}</span>
          </button>
          {!b.isActive && (
            <Badge variant="secondary" className="font-normal">
              Inactive
            </Badge>
          )}
        </div>
      ),
    },
    {
      key: "type",
      header: "Type",
      render: (b) => (
        <Badge variant="outline" className="whitespace-nowrap font-normal">
          {branchTypeLabel(b.type)}
        </Badge>
      ),
    },
    { key: "district", header: "District", render: (b) => <span className="text-sm">{districtNames.get(b.districtId) ?? "—"}</span> },
    { key: "onHand", header: "On hand", render: (b) => live(b.id, (r) => r.onHand) },
    { key: "outForDelivery", header: "Out for delivery", render: (b) => live(b.id, (r) => r.outForDelivery) },
    { key: "ridersOut", header: "Riders out", render: (b) => live(b.id, (r) => r.ridersOut) },
    {
      key: "exceptions",
      header: "Exceptions",
      render: (b) => {
        if (!activityById) return <span className="text-sm text-muted-foreground">—</span>;
        const open = activityById.get(b.id)?.openExceptions ?? 0;
        return open === 0 ? (
          <span className="text-sm text-muted-foreground">None</span>
        ) : (
          <span className="text-sm font-medium text-status-ndr">{`${open} open`}</span>
        );
      },
    },
  ];

  return (
    <div className="space-y-5">
      <PageHeader
        eyebrow="Network"
        title="Branches"
        description={summary.data ? `${summary.data.active} active · ${summary.data.inactive} inactive` : "—"}
        actions={
          canCreate && (
            <Button type="button" className="gap-1.5" onClick={() => openCreate()}>
              <Plus className="h-4 w-4" aria-hidden="true" />
              Add branch
            </Button>
          )
        }
      />

      <section aria-label="Branch type summary">
        {summary.isLoading && (
          <div role="status" aria-label="Loading branch types" className="grid grid-cols-2 gap-2.5 sm:grid-cols-5">
            {BRANCH_TYPES.map((t) => (
              <Skeleton key={t.type} className="h-20 rounded-lg" />
            ))}
          </div>
        )}
        {summary.isError && (
          <ErrorState title="Couldn't load branch counts" description="The type summary is unavailable right now." onRetry={() => summary.refetch()} />
        )}
        {summary.data && (
          <PipelineStrip
            ariaLabel="Branch types"
            stages={stages}
            selectedKey={type}
            onSelect={(key) => update({ type: key === type ? undefined : key })}
          />
        )}
      </section>

      <div className="flex flex-wrap items-center gap-3">
        <Input
          value={q}
          onChange={(e) => update({ q: e.target.value || undefined })}
          placeholder="Search code or name"
          aria-label="Search branches"
          className="w-64"
        />
        <div className="flex items-center gap-2">
          <Checkbox
            id="branches-active-only"
            checked={!showInactive}
            onCheckedChange={(checked) => update({ inactive: checked === true ? undefined : "1" })}
          />
          <Label htmlFor="branches-active-only" className="text-sm font-normal">Active only</Label>
        </div>
      </div>

      {activity.isError && (
        <ErrorState
          title="Couldn't load live counts"
          description="On hand, out for delivery, riders out and exceptions are unavailable right now."
          onRetry={() => activity.refetch()}
        />
      )}

      {list.isError ? (
        <ErrorState title="Couldn't load branches" description="Something went wrong fetching the branch list." onRetry={() => list.refetch()} />
      ) : (
        <DataTable
          columns={columns}
          data={shown && toClientPage(shown, page, PAGE_SIZE)}
          isLoading={list.isLoading}
          emptyTitle="No branches here"
          emptyDescription="No branch matches these filters. Clear a filter or add a branch."
          onPageChange={setPage}
          rowKey={(b) => b.id}
          selectedRowId={panel.mode === "detail" ? panel.branchId : undefined}
        />
      )}
    </div>
  );
}

export default function BranchesPage() {
  return (
    <Suspense>
      <BranchesPageContent />
    </Suspense>
  );
}
