"use client";

import { EmptyState } from "@/components/states/EmptyState";
import { ErrorState } from "@/components/states/ErrorState";
import { Skeleton } from "@/components/ui/skeleton";
import { RiderTaskCard } from "@/features/rider-app/components/RiderTaskCard";
import { useMyRider } from "@/features/riders/hooks/useMyRider";
import { useMyTasksToday } from "@/features/riders/hooks/useMyTasksToday";
import { useRiderAppPanelStore } from "@/features/rider-app/store/riderAppPanelStore";

export default function RiderTasksPage() {
  const { data: rider, isLoading: riderLoading, isError: riderError } = useMyRider();
  const { data: tasksToday, isLoading: tasksLoading, isError: tasksError, refetch } = useMyTasksToday(!!rider);

  if (riderLoading || (rider && tasksLoading)) {
    return (
      <div className="space-y-3 p-4" role="status" aria-live="polite">
        <span className="sr-only">Loading</span>
        <Skeleton className="h-6 w-40" />
        {Array.from({ length: 3 }).map((_, i) => (
          <Skeleton key={i} className="h-24 w-full rounded-lg" />
        ))}
      </div>
    );
  }

  if (riderError) {
    return <EmptyState title="No rider profile" description="This account isn't linked to a rider record." />;
  }

  if (tasksError) {
    return <ErrorState title="Couldn't load today's deliveries" description="Something went wrong." onRetry={() => refetch()} />;
  }

  const tasks = tasksToday?.tasks ?? [];

  return (
    <div className="space-y-4 p-4">
      <h1 className="text-xl font-semibold">Today&apos;s Tasks</h1>
      {tasks.length === 0 ? (
        <EmptyState title="No deliveries assigned today" description="Pickup and delivery tasks assigned to you today will show up here." />
      ) : (
        <div className="space-y-3">
          {tasks.map((task) => (
            <RiderTaskCard
              key={task.taskId}
              task={task}
              riderId={rider!.id}
              onRecordAttempt={(t) => useRiderAppPanelStore.getState().openRecordAttempt(t)}
            />
          ))}
        </div>
      )}
    </div>
  );
}
