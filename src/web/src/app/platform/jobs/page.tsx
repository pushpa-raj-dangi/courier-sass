"use client";

import { Badge } from "@/components/ui/badge";
import { ErrorState } from "@/components/states/ErrorState";
import { StatTilesSkeleton } from "@/components/states/StatTilesSkeleton";
import { LoadingState } from "@/components/states/LoadingState";
import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { usePlatformJobs } from "@/features/platform-jobs/hooks/usePlatformJobs";
import { usePlatformJobPanelStore } from "@/features/platform-jobs/store/platformJobPanelStore";
import { PlatformJobDetailSheet } from "@/features/platform-jobs/PlatformJobDetailSheet";
import type { RecurringJobSummary, RecentJobSummary } from "@/features/platform-jobs/types";

function StatTile({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="rounded-lg border bg-card p-4">
      <div className="text-sm text-muted-foreground">{label}</div>
      <div className="mt-1 text-2xl font-semibold">{value}</div>
    </div>
  );
}

function toSinglePage<T>(items: T[]): PagedResult<T> {
  return { items, page: 1, pageSize: Math.max(items.length, 1), totalCount: items.length, totalPages: 1 };
}

export default function PlatformJobsPage() {
  const { data, isLoading, isError, refetch } = usePlatformJobs();
  const openDetail = usePlatformJobPanelStore((s) => s.openDetail);

  const recurringColumns: ColumnDef<RecurringJobSummary>[] = [
    { key: "id", header: "Job", render: (r) => r.id },
    { key: "cron", header: "Cron", render: (r) => r.cron ?? "—" },
    { key: "lastExecution", header: "Last Run", render: (r) => (r.lastExecution ? new Date(r.lastExecution).toLocaleString() : "—") },
    { key: "nextExecution", header: "Next Run", render: (r) => (r.nextExecution ? new Date(r.nextExecution).toLocaleString() : "—") },
    { key: "lastJobState", header: "Last State", render: (r) => (r.lastJobState ? <Badge variant={r.lastJobState === "Failed" ? "destructive" : "secondary"}>{r.lastJobState}</Badge> : "—") },
  ];

  const recentColumns: ColumnDef<RecentJobSummary>[] = [
    { key: "at", header: "Time", render: (r) => new Date(r.at).toLocaleString() },
    {
      key: "jobName",
      header: "Job",
      render: (r) =>
        r.state === "Failed" ? (
          <button type="button" onClick={() => openDetail(r)} className="font-medium text-foreground hover:underline">
            {r.jobName}
          </button>
        ) : (
          r.jobName
        ),
    },
    { key: "state", header: "State", render: (r) => <Badge variant={r.state === "Failed" ? "destructive" : "secondary"}>{r.state}</Badge> },
    { key: "exceptionMessage", header: "Exception", render: (r) => r.exceptionMessage ?? "—" },
  ];

  if (isLoading) {
    return (
      <div className="space-y-6 p-6">
        <h1 className="font-display text-xl font-semibold">Jobs</h1>
        <StatTilesSkeleton count={6} />
        <div className="space-y-2">
          <h2 className="text-sm font-medium text-muted-foreground">Recurring Jobs</h2>
          <LoadingState rows={4} />
        </div>
        <div className="space-y-2">
          <h2 className="text-sm font-medium text-muted-foreground">Recent Activity</h2>
          <LoadingState rows={4} />
        </div>
      </div>
    );
  }
  if (isError || !data) {
    return (
      <ErrorState
        title="Couldn't load jobs"
        description="Something went wrong fetching the background job status."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-6 p-6">
      <h1 className="font-display text-xl font-semibold">Jobs</h1>
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
        <StatTile label="Enqueued" value={data.summary.enqueued} />
        <StatTile label="Scheduled" value={data.summary.scheduled} />
        <StatTile label="Processing" value={data.summary.processing} />
        <StatTile label="Succeeded" value={data.summary.succeeded} />
        <StatTile label="Failed" value={data.summary.failed} />
        <StatTile label="Deleted" value={data.summary.deleted} />
      </div>
      <div className="space-y-2">
        <h2 className="text-sm font-medium text-muted-foreground">Recurring Jobs</h2>
        <DataTable
          columns={recurringColumns}
          data={toSinglePage(data.recurringJobs)}
          isLoading={false}
          emptyTitle="No recurring jobs registered"
          onPageChange={() => {}}
          rowKey={(r) => r.id}
        />
      </div>
      <div className="space-y-2">
        <h2 className="text-sm font-medium text-muted-foreground">Recent Activity</h2>
        <DataTable
          columns={recentColumns}
          data={toSinglePage(data.recentJobs)}
          isLoading={false}
          emptyTitle="No recent job activity"
          onPageChange={() => {}}
          rowKey={(r) => r.jobId}
        />
      </div>
      <PlatformJobDetailSheet />
    </div>
  );
}
