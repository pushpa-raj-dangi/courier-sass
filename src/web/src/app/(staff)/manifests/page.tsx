"use client";

import { Suspense } from "react";
import Link from "next/link";
import { Plus, ScanLine, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { PageHeader } from "@/components/page/PageHeader";
import { PipelineStrip, type PipelineStage } from "@/components/page/PipelineStrip";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useSession } from "@/features/auth/hooks/useSession";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { branchCode } from "@/features/branches/lib/branchCode";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useManifests } from "@/features/manifests/hooks/useManifests";
import { useManifestsSummary } from "@/features/manifests/hooks/useManifestsSummary";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";
import {
  MANIFEST_STAGES, manifestStageLabel, parcelsSummary, parseManifestStatus, parseManifestTab, pickManifestHighlight, type ManifestTab,
} from "@/features/manifests/lib/manifestModel";
import { useFilterPage } from "@/lib/hooks/useFilterPage";
import { useSearchParamState } from "@/lib/hooks/useSearchParamState";
import { toClientPage } from "@/lib/paging/clientPage";
import type { Manifest } from "@/features/manifests/types";

const ALL_BRANCHES = "all";
const PAGE_SIZE = 20;
const createdFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });

const TABS: { key: ManifestTab; label: string }[] = [
  { key: "incoming", label: "Incoming" },
  { key: "outgoing", label: "Outgoing" },
  { key: "all", label: "All" },
];

const EMPTY_COPY: Record<ManifestTab, string> = {
  incoming: "Nothing is headed to this branch right now.",
  outgoing: "Nothing has left this branch. Create a manifest to start a transfer.",
  all: "Create a manifest to start a transfer between branches.",
};

function ManifestsPageContent() {
  const { params, update } = useSearchParamState();
  const tab = parseManifestTab(params.get("tab"));
  const status = parseManifestStatus(params.get("status"));
  const branchId = params.get("branch") ?? undefined;

  const { data: session } = useSession();
  const { data: branches } = useBranches();
  const scopedBranches = (branches ?? []).filter(
    (b) => session?.hasAllBranchesScope || session?.branches.some((s) => s.branchId === b.id),
  );
  const showDirectionHint = Boolean(session?.hasAllBranchesScope) && !branchId;
  const showBranchPicker =Boolean(session?.hasAllBranchesScope) || scopedBranches.length > 1;
  const branchLabel = branchId ? scopedBranches.find((b) => b.id === branchId)?.name ?? "Selected branch" : "All my branches";

  const list = useManifests(branchId, status, { direction: tab === "all" ? undefined : tab });
  const summary = useManifestsSummary();
  const names = usePeopleNames();
  const canCreate = usePermission("manifest.create");
  const canReceive = usePermission("manifest.receive");
  const panel = useManifestPanelStore((s) => s.panel);
  const openCreate = useManifestPanelStore((s) => s.openCreate);
  const openDetail = useManifestPanelStore((s) => s.openDetail);
  const [page, setPage] = useFilterPage(`${tab}|${status ?? ""}|${branchId ?? ""}`);

  // The Sheet eyebrow says INCOMING/OUTGOING only when that is relative to one picked branch.
  const sheetDirection = branchId !== undefined && tab !== "all" ? tab : undefined;
  const openCount = list.data?.filter((m) => m.status !== "Closed").length;

  const stages: PipelineStage[] = MANIFEST_STAGES.map((s) => ({
    key: s.status,
    label: s.label,
    token: s.token,
    count: summary.data ? summary.data[s.status] ?? 0 : null,
  }));

  const columns: ColumnDef<Manifest>[] = [
    {
      key: "code",
      header: "Manifest",
      render: (m) => (
        <button type="button" onClick={() => openDetail(m.id, sheetDirection)} className="flex flex-col items-start gap-0.5 text-left">
          <span className="font-mono text-sm font-medium text-foreground hover:underline">{m.code}</span>
          <span className="text-xs text-muted-foreground">{m.purpose === "Return" ? "RTO" : "Forward"}</span>
        </button>
      ),
    },
    {
      key: "route",
      header: "Route",
      render: (m) => <span className="font-mono text-sm">{`${branchCode(branches, m.fromBranchId)} → ${branchCode(branches, m.toBranchId)}`}</span>,
    },
    {
      key: "vehicle",
      header: "Vehicle · driver",
      render: (m) => (
        <div className="flex flex-col gap-0.5">
          <span className="text-sm">{m.vehiclePlate ?? "Not assigned"}</span>
          <span className="text-xs text-muted-foreground">{m.driverId ? names.riderName(m.driverId) : "—"}</span>
        </div>
      ),
    },
    {
      key: "parcels",
      header: "Parcels",
      render: (m) => {
        const parcels = parcelsSummary(m);
        return (
          <div className="flex flex-col gap-0.5">
            <span className="text-sm tabular-nums">{parcels.primary}</span>
            {parcels.missing > 0 && <span className="text-xs font-medium text-destructive">{`${parcels.missing} missing`}</span>}
          </div>
        );
      },
    },
    { key: "status", header: "Status", render: (m) => <StatusBadge status={m.status} namespace="Manifest" /> },
    {
      key: "created",
      header: "Created",
      render: (m) => (
        <time dateTime={m.createdAt} className="whitespace-nowrap text-sm text-muted-foreground">
          {createdFormat.format(new Date(m.createdAt))}
        </time>
      ),
    },
  ];

  return (
    <div className="space-y-5">
      <PageHeader
        eyebrow="Operations"
        title="Manifests"
        description={`${branchLabel} · ${openCount === undefined ? "—" : `${openCount} open`}`}
        actions={
          <>
            {showBranchPicker && (
              <Select value={branchId ?? ALL_BRANCHES} onValueChange={(v) => update({ branch: v === ALL_BRANCHES ? undefined : v })}>
                <SelectTrigger className="w-48" aria-label="Branch">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={ALL_BRANCHES}>All my branches</SelectItem>
                  {scopedBranches.map((b) => (
                    <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
            {canReceive && (
              <Button asChild variant="outline" className="gap-1.5">
                <Link href="/scan">
                  <ScanLine className="h-4 w-4" aria-hidden="true" />
                  Receive at scan station
                </Link>
              </Button>
            )}
            {canCreate && (
              <Button type="button" className="gap-1.5" onClick={() => openCreate()}>
                <Plus className="h-4 w-4" aria-hidden="true" />
                New manifest
              </Button>
            )}
          </>
        }
      />

      <section aria-label="Manifest lifecycle summary" className="space-y-2">
        {summary.isLoading && (
          <div role="status" aria-label="Loading manifest lifecycle" className="grid grid-cols-2 gap-2.5 sm:grid-cols-5">
            {MANIFEST_STAGES.map((s) => (
              <Skeleton key={s.status} className="h-20 rounded-lg" />
            ))}
          </div>
        )}
        {summary.isError && (
          <ErrorState title="Couldn't load manifest counts" description="The lifecycle strip is unavailable right now." onRetry={() => summary.refetch()} />
        )}
        {summary.data && (
          <>
            <PipelineStrip
              ariaLabel="Manifest lifecycle"
              stages={stages}
              highlightKey={pickManifestHighlight(summary.data)}
              selectedKey={status}
              onSelect={(key) => update({ status: key === status ? undefined : key })}
            />
            <p className="text-2xs text-muted-foreground">Across your branches</p>
          </>
        )}
      </section>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-col gap-1.5">
          <Tabs value={tab} onValueChange={(v) => update({ tab: parseManifestTab(v) === "incoming" ? undefined : v })}>
            <TabsList>
              {TABS.map((t) => (
                <TabsTrigger key={t.key} value={t.key}>{t.label}</TabsTrigger>
              ))}
            </TabsList>
          </Tabs>
          {showDirectionHint && <p className="text-xs text-muted-foreground">Pick a branch to split incoming and outgoing.</p>}
        </div>
        {status && (
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-8 gap-1.5"
            aria-label={`Clear status filter ${manifestStageLabel(status)}`}
            onClick={() => update({ status: undefined })}
          >
            {`Status: ${manifestStageLabel(status)}`}
            <X className="h-3.5 w-3.5" aria-hidden="true" />
          </Button>
        )}
      </div>

      {list.isError ? (
        <ErrorState title="Couldn't load manifests" description="Something went wrong fetching the manifest list." onRetry={() => list.refetch()} />
      ) : (
        <DataTable
          columns={columns}
          data={list.data && toClientPage(list.data, page, PAGE_SIZE)}
          isLoading={list.isLoading}
          emptyTitle="No manifests here"
          emptyDescription={EMPTY_COPY[tab]}
          onPageChange={setPage}
          rowKey={(m) => m.id}
          selectedRowId={panel.mode === "detail" ? panel.id : undefined}
        />
      )}
    </div>
  );
}

export default function ManifestsPage() {
  return (
    <Suspense>
      <ManifestsPageContent />
    </Suspense>
  );
}
