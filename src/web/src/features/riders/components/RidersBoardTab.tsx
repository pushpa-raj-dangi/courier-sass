"use client";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { ErrorState } from "@/components/states/ErrorState";
import { formatRs } from "@/features/cod/lib/codFormat";
import { RiderInactiveBadge, RiderStageBadge } from "@/features/riders/components/RiderStageBadge";
import {
  lastSeenText, progressShare, riderDayStage, riderInitials, roleLabel, type RiderCashTotal,
} from "@/features/riders/lib/riderModel";
import type { RiderBoardRow } from "@/features/riders/types";
import { useFilterPage } from "@/lib/hooks/useFilterPage";
import { toClientPage } from "@/lib/paging/clientPage";
import { cn } from "@/lib/utils";

const PAGE_SIZE = 20;

export function RidersBoardTab({
  rows, isLoading, isError, onRetry, cash, showCash, selectedRiderId, onOpenRider, filterKey, now,
}: {
  rows?: RiderBoardRow[];
  isLoading: boolean;
  isError: boolean;
  onRetry: () => void;
  cash?: Map<string, RiderCashTotal>;
  showCash: boolean;
  selectedRiderId?: string;
  onOpenRider: (riderId: string) => void;
  filterKey: string;
  now: Date;
}) {
  const [page, setPage] = useFilterPage(filterKey);

  const columns: ColumnDef<RiderBoardRow>[] = [
    {
      key: "rider",
      header: "Rider",
      render: (r) => (
        <button type="button" onClick={() => onOpenRider(r.riderId)} className="flex items-center gap-2.5 text-left">
          <Avatar className="h-8 w-8" aria-hidden="true">
            <AvatarFallback className="text-2xs font-medium">{riderInitials(r.fullName)}</AvatarFallback>
          </Avatar>
          <span className="flex flex-col">
            <span className="text-sm font-medium text-foreground hover:underline">{r.fullName}</span>
            <span className="text-xs text-muted-foreground">{roleLabel(r.roleType)}</span>
          </span>
        </button>
      ),
    },
    {
      key: "vehicle",
      header: "Vehicle",
      render: (r) =>
        r.vehiclePlate ? <span className="font-mono text-sm">{r.vehiclePlate}</span> : <span className="text-sm text-muted-foreground">Unassigned</span>,
    },
    {
      key: "today",
      header: "Today",
      render: (r) =>
        !r.run ? (
          <span className="text-sm text-muted-foreground">No run</span>
        ) : (
          <div className="flex min-w-28 flex-col gap-1">
            <span className="text-sm tabular-nums">{`${r.tasksCompleted} of ${r.tasksAssigned}`}</span>
            <span className="block h-1 rounded-full bg-muted" aria-hidden="true">
              <span
                className="block h-1 rounded-full bg-status-delivered"
                style={{ width: `${Math.round(progressShare(r.tasksCompleted, r.tasksAssigned) * 100)}%` }}
              />
            </span>
            {r.tasksFailed > 0 && <span className="text-xs font-medium text-destructive">{`${r.tasksFailed} failed`}</span>}
          </div>
        ),
    },
    {
      key: "lastSeen",
      header: "Last seen",
      render: (r) => {
        const seen = lastSeenText(r.lastPingAt, now);
        return <span className={cn("whitespace-nowrap text-sm", seen.stale ? "text-muted-foreground" : "text-foreground")}>{seen.text}</span>;
      },
    },
    ...(showCash
      ? [
          {
            key: "cash",
            header: "Cash held",
            render: (r: RiderBoardRow) => {
              const holding = cash?.get(r.riderId);
              return <span className="text-sm tabular-nums">{holding ? formatRs(holding.amount) : "—"}</span>;
            },
          },
        ]
      : []),
    {
      key: "status",
      header: "Status",
      render: (r) => (r.isActive ? <RiderStageBadge stage={riderDayStage(r)} /> : <RiderInactiveBadge />),
    },
  ];

  if (isError) {
    return <ErrorState title="Couldn't load riders" description="Something went wrong fetching today's rider board." onRetry={onRetry} />;
  }

  return (
    <DataTable
      columns={columns}
      data={rows && toClientPage(rows, page, PAGE_SIZE)}
      isLoading={isLoading}
      emptyTitle="No riders here"
      emptyDescription="No rider matches these filters. Clear a filter or add a rider."
      onPageChange={setPage}
      rowKey={(r) => r.riderId}
      selectedRowId={selectedRiderId}
    />
  );
}
