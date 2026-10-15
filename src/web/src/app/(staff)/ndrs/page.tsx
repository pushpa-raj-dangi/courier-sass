"use client";

import { Suspense } from "react";
import { X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { PageHeader } from "@/components/page/PageHeader";
import { PipelineStrip, type PipelineStage } from "@/components/page/PipelineStrip";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useSession } from "@/features/auth/hooks/useSession";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useNdrs } from "@/features/ndr/hooks/useNdrs";
import { useNdrsSummary } from "@/features/ndr/hooks/useNdrsSummary";
import { useNdrPanelStore } from "@/features/ndr/store/ndrPanelStore";
import { NdrRtoSwitch } from "@/features/ndr/components/NdrRtoSwitch";
import {
  NDR_REASONS, NDR_STAGES, isFollowUpOverdue, ndrReasonLabel, ndrStatusLabel, ownerLabel, parseAssigned, parseNdrReason, parseNdrStatus,
} from "@/features/ndr/lib/ndrModel";
import { AGE_WARNING_HOURS, ageHours, formatAge } from "@/lib/dates/age";
import { useFilterPage } from "@/lib/hooks/useFilterPage";
import { useSearchParamState } from "@/lib/hooks/useSearchParamState";
import { cn } from "@/lib/utils";
import type { Ndr, NdrAssignedFilter } from "@/features/ndr/types";

const PAGE_SIZE = 20;
const ANY_REASON = "any";
const followUpFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });

const OWNER_CHIPS: { key: NdrAssignedFilter; label: string }[] = [
  { key: "me", label: "Assigned to me" },
  { key: "unassigned", label: "Unassigned" },
];

function NdrsPageContent() {
  const { params, update } = useSearchParamState();
  const status = parseNdrStatus(params.get("status"));
  const assigned = parseAssigned(params.get("assigned"));
  const reason = parseNdrReason(params.get("reason"));
  const [page, setPage] = useFilterPage(`${status ?? ""}|${assigned ?? ""}|${reason ?? ""}`);

  const list = useNdrs({ status, assigned, reason, page, pageSize: PAGE_SIZE });
  const summary = useNdrsSummary();
  const { data: session } = useSession();
  const names = usePeopleNames();
  const panel = useNdrPanelStore((s) => s.panel);
  const openDetail = useNdrPanelStore((s) => s.openDetail);
  const now = new Date();

  const stages: PipelineStage[] = NDR_STAGES.map((s) => ({
    key: s.status,
    label: s.label,
    token: s.token,
    count: summary.data ? summary.data[s.status] ?? 0 : null,
  }));
  const highlight = summary.data && (summary.data.Open ?? 0) > 0 ? "Open" : undefined;

  const columns: ColumnDef<Ndr>[] = [
    {
      key: "awb",
      header: "AWB",
      render: (n) => (
        <button type="button" onClick={() => openDetail(n.id)} className="font-mono text-sm font-medium text-foreground hover:underline">
          {n.awb}
        </button>
      ),
    },
    { key: "reason", header: "Reason", render: (n) => <span className="text-sm">{ndrReasonLabel(n.reason)}</span> },
    {
      key: "age",
      header: "Age",
      render: (n) => (
        <span className={cn("text-sm tabular-nums", ageHours(n.createdAt, now) >= AGE_WARNING_HOURS && "font-semibold text-destructive")}>
          {formatAge(n.createdAt, now)}
        </span>
      ),
    },
    {
      key: "owner",
      header: "Owner",
      render: (n) => <span className="text-sm">{ownerLabel(n.assignedStaffUserId, session?.userId, names.userName)}</span>,
    },
    {
      key: "status",
      header: "Status",
      render: (n) => {
        const overdue = isFollowUpOverdue(n, now);
        return (
          <div className="flex flex-col items-start gap-1">
            <StatusBadge status={n.status} namespace="Ndr" />
            {n.followUpAt && n.status !== "Resolved" && (
              <span className={cn("text-xs", overdue ? "font-medium text-destructive" : "text-muted-foreground")}>
                {`${overdue ? "Follow-up overdue · " : "Follow up "}${followUpFormat.format(new Date(n.followUpAt))}`}
              </span>
            )}
          </div>
        );
      },
    },
  ];

  return (
    <div className="space-y-5">
      <PageHeader eyebrow="Operations · NDR & RTO" title="Failed deliveries" actions={<NdrRtoSwitch active="ndr" />} />

      {summary.isLoading && (
        <div role="status" aria-label="Loading NDR counts" className="grid grid-cols-2 gap-2.5 sm:grid-cols-3 xl:grid-cols-6">
          {NDR_STAGES.map((s) => (
            <Skeleton key={s.status} className="h-20 rounded-lg" />
          ))}
        </div>
      )}
      {summary.isError && (
        <ErrorState title="Couldn't load NDR counts" description="The status strip is unavailable right now." onRetry={() => summary.refetch()} />
      )}
      {summary.data && (
        <PipelineStrip
          ariaLabel="NDR status"
          stages={stages}
          highlightKey={highlight}
          selectedKey={status}
          onSelect={(key) => update({ status: key === status ? undefined : key })}
        />
      )}

      <div className="flex flex-wrap items-center gap-2">
        {OWNER_CHIPS.map((chip) => (
          <Button
            key={chip.key}
            type="button"
            size="sm"
            variant={assigned === chip.key ? "secondary" : "outline"}
            aria-pressed={assigned === chip.key}
            onClick={() => update({ assigned: assigned === chip.key ? undefined : chip.key })}
          >
            {chip.label}
          </Button>
        ))}
        <Select value={reason ?? ANY_REASON} onValueChange={(v) => update({ reason: v === ANY_REASON ? undefined : v })}>
          <SelectTrigger className="h-9 w-60" aria-label="Reason">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ANY_REASON}>Any reason</SelectItem>
            {NDR_REASONS.map((r) => (
              <SelectItem key={r} value={r}>{ndrReasonLabel(r)}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        {status && (
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="gap-1.5"
            aria-label={`Clear status filter ${ndrStatusLabel(status)}`}
            onClick={() => update({ status: undefined })}
          >
            {`Status: ${ndrStatusLabel(status)}`}
            <X className="h-3.5 w-3.5" aria-hidden="true" />
          </Button>
        )}
      </div>

      {list.isError ? (
        <ErrorState title="Couldn't load NDRs" description="Something went wrong fetching the NDR queue." onRetry={() => list.refetch()} />
      ) : (
        <DataTable
          columns={columns}
          data={list.data}
          isLoading={list.isLoading}
          emptyTitle="No failed deliveries here"
          emptyDescription={assigned || reason || status ? "Nothing matches these filters." : "Failed deliveries that need follow-up show up here."}
          onPageChange={setPage}
          rowKey={(n) => n.id}
          selectedRowId={panel.mode === "detail" ? panel.id : undefined}
        />
      )}
    </div>
  );
}

export default function NdrsPage() {
  return (
    <Suspense>
      <NdrsPageContent />
    </Suspense>
  );
}
