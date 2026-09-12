"use client";

import { Spinner } from "@/components/ui/spinner";
import { Badge } from "@/components/ui/badge";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { usePlatformQueues } from "@/features/platform-queues/hooks/usePlatformQueues";
import type { QueueSummary } from "@/features/platform-queues/types";

function toSinglePage<T>(items: T[]): PagedResult<T> {
  return { items, page: 1, pageSize: Math.max(items.length, 1), totalCount: items.length, totalPages: 1 };
}

function isUnhealthy(queue: QueueSummary): boolean {
  return queue.messagesReady > 0 && queue.consumers === 0;
}

export default function PlatformQueuesPage() {
  const { data, isLoading, isError, refetch } = usePlatformQueues();

  const columns: ColumnDef<QueueSummary>[] = [
    { key: "name", header: "Queue", render: (q) => q.name },
    { key: "messagesReady", header: "Ready", render: (q) => q.messagesReady },
    { key: "messagesUnacknowledged", header: "Unacked", render: (q) => q.messagesUnacknowledged },
    { key: "consumers", header: "Consumers", render: (q) => q.consumers },
    {
      key: "status",
      header: "Status",
      render: (q) => <Badge variant={isUnhealthy(q) ? "destructive" : "secondary"}>{isUnhealthy(q) ? "Unhealthy" : "Healthy"}</Badge>,
    },
  ];

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }
  if (isError || !data) {
    return (
      <ErrorState
        title="Couldn't load queues"
        description="Something went wrong fetching RabbitMQ queue status."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4 p-6">
      <h1 className="font-display text-xl font-semibold">Queues</h1>
      <DataTable
        columns={columns}
        data={toSinglePage(data)}
        isLoading={false}
        emptyTitle="No queues found"
        onPageChange={() => {}}
        rowKey={(q) => q.name}
      />
    </div>
  );
}
