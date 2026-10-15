"use client";

import { Suspense } from "react";
import { Skeleton } from "@/components/ui/skeleton";
import { PageHeader } from "@/components/page/PageHeader";
import { PipelineStrip, type PipelineStage } from "@/components/page/PipelineStrip";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { branchCode } from "@/features/branches/lib/branchCode";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useReturnShipments } from "@/features/ndr/hooks/useReturnShipments";
import { useReturnShipmentsSummary } from "@/features/ndr/hooks/useReturnShipmentsSummary";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";
import { NdrRtoSwitch } from "@/features/ndr/components/NdrRtoSwitch";
import { RETURN_STAGES, matchesReturnStage, parseReturnStage, returnStageCount } from "@/features/ndr/lib/returnModel";
import { useFilterPage } from "@/lib/hooks/useFilterPage";
import { useSearchParamState } from "@/lib/hooks/useSearchParamState";
import { toClientPage } from "@/lib/paging/clientPage";
import type { ReturnShipment } from "@/features/ndr/types";

const PAGE_SIZE = 20;
const confirmedFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric" });

function ReturnShipmentsPageContent() {
  const { params, update } = useSearchParamState();
  const stage = parseReturnStage(params.get("stage"));
  const [page, setPage] = useFilterPage(stage ?? "");

  const list = useReturnShipments();
  const summary = useReturnShipmentsSummary();
  const { data: branches } = useBranches();
  const names = usePeopleNames();
  const panel = useReturnShipmentPanelStore((s) => s.panel);
  const openDetail = useReturnShipmentPanelStore((s) => s.openDetail);

  const rows = list.data?.filter((r) => !stage || matchesReturnStage(r, stage));
  const stages: PipelineStage[] = RETURN_STAGES.map((s) => ({
    key: s.key,
    label: s.label,
    token: s.token,
    count: summary.data ? returnStageCount(summary.data, s.key) : null,
  }));
  const highlight = summary.data && returnStageCount(summary.data, "RtoAtOrigin") > 0 ? "RtoAtOrigin" : undefined;

  const columns: ColumnDef<ReturnShipment>[] = [
    {
      key: "returnAwb",
      header: "Return AWB",
      render: (r) => (
        <button type="button" onClick={() => openDetail(r.id)} className="flex flex-col items-start gap-0.5 text-left">
          <span className="font-mono text-sm font-medium text-foreground hover:underline">{r.returnAwb}</span>
          <span className="font-mono text-xs text-muted-foreground">{`from ${r.originalAwb || "—"}`}</span>
        </button>
      ),
    },
    { key: "reason", header: "Reason", render: (r) => <span className="line-clamp-2 max-w-56 text-sm">{r.rtoReason}</span> },
    {
      key: "route",
      header: "Route",
      render: (r) => <span className="font-mono text-sm">{`${branchCode(branches, r.originBranchId)} → ${branchCode(branches, r.destinationBranchId)}`}</span>,
    },
    { key: "nowAt", header: "Now at", render: (r) => <span className="text-sm">{r.currentBranchId ? names.branchName(r.currentBranchId) : "In transit"}</span> },
    {
      key: "status",
      header: "Status",
      render: (r) => (
        <div className="flex flex-col items-start gap-1">
          <StatusBadge status={r.currentStatus} />
          <span className="text-xs text-muted-foreground">
            {r.merchantConfirmedAt ? `Merchant confirmed ${confirmedFormat.format(new Date(r.merchantConfirmedAt))}` : "Not confirmed"}
          </span>
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-5">
      <PageHeader eyebrow="Operations · NDR & RTO" title="Returns to origin" actions={<NdrRtoSwitch active="returns" />} />

      {summary.isLoading && (
        <div role="status" aria-label="Loading return stages" className="grid grid-cols-2 gap-2.5 sm:grid-cols-5">
          {RETURN_STAGES.map((s) => (
            <Skeleton key={s.key} className="h-20 rounded-lg" />
          ))}
        </div>
      )}
      {summary.isError && (
        <ErrorState title="Couldn't load return counts" description="The stage strip is unavailable right now." onRetry={() => summary.refetch()} />
      )}
      {summary.data && (
        <PipelineStrip
          ariaLabel="Return stages"
          stages={stages}
          highlightKey={highlight}
          selectedKey={stage}
          onSelect={(key) => update({ stage: key === stage ? undefined : key })}
        />
      )}

      {list.isError ? (
        <ErrorState title="Couldn't load returns" description="Something went wrong fetching the return list." onRetry={() => list.refetch()} />
      ) : (
        <DataTable
          columns={columns}
          data={rows && toClientPage(rows, page, PAGE_SIZE)}
          isLoading={list.isLoading}
          emptyTitle="No returns here"
          emptyDescription={stage ? "Nothing is at this stage right now." : "Shipments resolved as RTO show up here."}
          onPageChange={setPage}
          rowKey={(r) => r.id}
          selectedRowId={panel.mode === "detail" ? panel.id : undefined}
        />
      )}
    </div>
  );
}

export default function ReturnShipmentsPage() {
  return (
    <Suspense>
      <ReturnShipmentsPageContent />
    </Suspense>
  );
}
