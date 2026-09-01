"use client";

import { EmptyState } from "@/components/states/EmptyState";
import { ErrorState } from "@/components/states/ErrorState";
import { Spinner } from "@/components/ui/spinner";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PickupScanControl } from "@/features/riders/components/PickupScanControl";
import { useMyRider } from "@/features/riders/hooks/useMyRider";
import { useMyTasksToday } from "@/features/riders/hooks/useMyTasksToday";
import type { MyTask } from "@/features/riders/types";

function TaskCard({ task, riderId }: { task: MyTask; riderId: string }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between text-base">
          <span>{task.type} &middot; {task.awb}</span>
          {task.codAmount > 0 && <Badge variant="outline">COD Rs. {task.codAmount}</Badge>}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-1 text-sm">
        <p className="font-medium text-foreground">{task.recipientName}</p>
        <p className="text-muted-foreground">{task.recipientPhone}</p>
        <p className="text-muted-foreground">{task.recipientAddressLine}, Ward {task.recipientWardNumber}</p>
        <div className="pt-2">
          {task.type === "Pickup" && <PickupScanControl riderId={riderId} awb={task.awb} />}
        </div>
      </CardContent>
    </Card>
  );
}

export default function MyDeliveriesPage() {
  const { data: rider, isLoading: riderLoading, isError: riderError } = useMyRider();
  const { data: tasksToday, isLoading: tasksLoading, isError: tasksError, refetch } = useMyTasksToday(!!rider);

  if (riderLoading || (rider && tasksLoading)) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (riderError) {
    return (
      <EmptyState
        title="No rider profile"
        description="This account isn't linked to a rider record, so there's nothing to show here."
      />
    );
  }

  if (tasksError) {
    return (
      <ErrorState
        title="Couldn't load today's deliveries"
        description="Something went wrong fetching your assigned tasks."
        onRetry={() => refetch()}
      />
    );
  }

  const tasks = tasksToday?.tasks ?? [];

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">My Deliveries</h1>
      {tasks.length === 0 ? (
        <EmptyState
          title="No deliveries assigned today"
          description="Pickup and delivery tasks assigned to you today will show up here."
        />
      ) : (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          {tasks.map((task) => (
            <TaskCard key={task.taskId} task={task} riderId={rider!.id} />
          ))}
        </div>
      )}
    </div>
  );
}
