"use client";

import { Navigation, Phone } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { formatRs } from "@/features/cod/lib/codFormat";
import { SyncNote } from "@/features/rider-app/components/SyncNote";
import { useRiderPickupScan } from "@/features/rider-app/hooks/useRiderPickupScan";
import { awbTail, mapsHref, type TaskSyncState } from "@/features/rider-app/lib/runModel";
import { useRiderAppPanelStore } from "@/features/rider-app/store/riderAppPanelStore";
import { useOutboxStatusStore } from "@/features/rider-app/sync/outboxStatusStore";
import type { MyTodayTask } from "@/features/riders/types";

export function NextStopCard({ task, riderId, syncState }: { task: MyTodayTask; riderId: string; syncState: TaskSyncState }) {
  const pickupScan = useRiderPickupScan(riderId);
  const openRecordAttempt = useRiderAppPanelStore((s) => s.openRecordAttempt);

  return (
    <article aria-label={`Next stop: ${task.recipientName}`} className="space-y-3 rounded-xl border-2 border-primary bg-card p-4">
      <div className="flex items-start gap-3">
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary text-sm font-semibold text-primary-foreground" aria-hidden="true">
          {task.sequence}
        </span>
        <div className="min-w-0 flex-1 space-y-0.5">
          <p className="font-mono text-2xs uppercase tracking-[0.08em] text-primary">{task.type === "Pickup" ? "Next pickup" : "Next stop"}</p>
          <p className="text-base font-semibold">{task.recipientName}</p>
          <p className="text-sm text-muted-foreground">{`${task.recipientAddressLine}, Ward ${task.recipientWardNumber}`}</p>
        </div>
      </div>
      <div className="flex flex-wrap gap-1.5">
        <Badge variant="outline" className={task.codAmount > 0 ? "font-semibold" : "font-normal"}>
          {task.codAmount > 0 ? `COD ${formatRs(task.codAmount)}` : "Prepaid"}
        </Badge>
        <Badge variant="outline" className="font-mono font-normal">{awbTail(task.awb)}</Badge>
      </div>
      <SyncNote state={syncState} />
      <div className="grid grid-cols-[1fr_1fr_1.4fr] gap-2">
        <Button asChild variant="outline" className="h-12 gap-1.5">
          <a href={`tel:${task.recipientPhone}`}>
            <Phone className="h-4 w-4" aria-hidden="true" />
            Call
          </a>
        </Button>
        <Button asChild variant="outline" className="h-12 gap-1.5">
          <a href={mapsHref(task.recipientAddressLine, task.recipientWardNumber)} target="_blank" rel="noopener noreferrer">
            <Navigation className="h-4 w-4" aria-hidden="true" />
            Map
          </a>
        </Button>
        {task.type === "Delivery" ? (
          <Button type="button" className="h-12 text-base" onClick={() => openRecordAttempt(task)}>
            Deliver
          </Button>
        ) : (
          <Button
            type="button"
            className="h-12 text-base"
            disabled={pickupScan.isPending || syncState !== "synced"}
            onClick={() =>
              pickupScan.mutate(
                { awb: task.awb, shipmentId: task.shipmentId },
                { onSuccess: () => void useOutboxStatusStore.getState().refresh() },
              )
            }
          >
            {pickupScan.isPending ? <Spinner className="h-4 w-4" /> : "Scan pickup"}
          </Button>
        )}
      </div>
    </article>
  );
}
