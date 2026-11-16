"use client";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { progressShare } from "@/features/riders/lib/riderModel";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import type { DeliveryTaskStatus, RiderToday } from "@/features/riders/types";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";

const VISIBLE_TASKS = 8;
const STATUS_TEXT: Record<DeliveryTaskStatus, string> = { Pending: "Pending", Completed: "Done", Failed: "Failed" };

function percent(part: number, total: number): string {
  return `${Math.round(progressShare(part, total) * 100)}%`;
}

export function RiderTodayCard({ today }: { today: RiderToday }) {
  const { rider, tasks } = today;
  const openShipment = useShipmentPanelStore((s) => s.openDetail);
  const closeRider = useRiderPanelStore((s) => s.close);

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-semibold">Today</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        {!rider.run ? (
          <p className="text-sm text-muted-foreground">No run today</p>
        ) : (
          <>
            <p className="text-sm tabular-nums text-foreground">{`${rider.tasksCompleted} of ${rider.tasksAssigned} done · ${rider.tasksFailed} failed`}</p>
            <div
              role="img"
              aria-label={`${rider.tasksCompleted} completed and ${rider.tasksFailed} failed of ${rider.tasksAssigned} tasks`}
              className="flex h-1.5 overflow-hidden rounded-full bg-muted"
            >
              <span className="h-full bg-status-delivered" style={{ width: percent(rider.tasksCompleted, rider.tasksAssigned) }} />
              <span className="h-full bg-destructive" style={{ width: percent(rider.tasksFailed, rider.tasksAssigned) }} />
            </div>
            {tasks.length === 0 ? (
              <p className="text-sm text-muted-foreground">No tasks on this run yet.</p>
            ) : (
              <ol className="divide-y divide-border rounded-md border border-border">
                {tasks.slice(0, VISIBLE_TASKS).map((task) => (
                  <li key={task.taskId} className="flex items-center gap-3 px-3 py-2">
                    <span className="w-5 shrink-0 text-xs tabular-nums text-muted-foreground">{task.sequence}</span>
                    {task.awb ? (
                      <Button
                        type="button"
                        variant="link"
                        className="h-auto p-0 font-mono text-sm"
                        onClick={() => {
                          closeRider();
                          openShipment(task.awb);
                        }}
                      >
                        {task.awb}
                      </Button>
                    ) : (
                      <span className="font-mono text-sm text-muted-foreground">—</span>
                    )}
                    <span className="text-xs text-muted-foreground">{task.type}</span>
                    <span className="ml-auto text-xs text-foreground">{STATUS_TEXT[task.status]}</span>
                  </li>
                ))}
              </ol>
            )}
            {tasks.length > VISIBLE_TASKS && (
              <p className="text-xs text-muted-foreground">{`+ ${tasks.length - VISIBLE_TASKS} more tasks`}</p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
