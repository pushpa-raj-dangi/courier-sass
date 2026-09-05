"use client";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useRiderPickupScan } from "@/features/rider-app/hooks/useRiderPickupScan";
import { useOutboxStatus } from "@/features/rider-app/sync/useOutboxStatus";
import type { MyTask } from "@/features/riders/types";

export function RiderTaskCard({
  task,
  riderId,
  onRecordAttempt,
}: {
  task: MyTask;
  riderId: string;
  onRecordAttempt: (task: MyTask) => void;
}) {
  const pickupScan = useRiderPickupScan(riderId);
  const { stuckItems } = useOutboxStatus();
  // pickupScan.isSuccess only means the action was enqueued into the outbox (optimistic) — not
  // that the server has actually accepted it. If that outbox item later goes "stuck" (e.g. a
  // permanent 4xx), reconcile against the outbox status so the card doesn't keep claiming
  // "Picked up." forever with no visible link to the fix in Profile > Needs Review.
  const isStuck = stuckItems.some((item) => item.action.shipmentId === task.shipmentId);

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
          {task.type === "Pickup" && (
            isStuck ? (
              <p className="text-sm font-medium text-destructive">Needs review — see Profile</p>
            ) : pickupScan.isSuccess ? (
              <p className="text-sm text-muted-foreground">Picked up.</p>
            ) : (
              <Button
                type="button"
                size="lg"
                className="w-full"
                disabled={pickupScan.isPending}
                onClick={() => pickupScan.mutate({ awb: task.awb, shipmentId: task.shipmentId })}
              >
                {pickupScan.isPending ? <Spinner className="h-4 w-4" /> : "Scan Pickup"}
              </Button>
            )
          )}
          {task.type === "Delivery" && (
            <Button type="button" size="lg" className="w-full" onClick={() => onRecordAttempt(task)}>
              Record Attempt
            </Button>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
