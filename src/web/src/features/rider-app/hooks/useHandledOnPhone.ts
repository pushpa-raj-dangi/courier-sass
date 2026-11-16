"use client";

import { useEffect } from "react";
import { handledOnPhone, phoneSyncState, settledShipmentIds } from "@/features/rider-app/lib/runModel";
import { useHandledStore } from "@/features/rider-app/store/handledStore";
import type { OutboxItem } from "@/features/rider-app/sync/types";
import type { MyTodayTask } from "@/features/riders/types";

/**
 * Whether a stop was already pickup-scanned, delivered or failed on this phone (outbox item, or
 * queued this session and not yet shown done by `me/today`), so no screen offers it again.
 */
export function useHandledOnPhone(tasks: MyTodayTask[] | undefined, pendingItems: OutboxItem[], stuckItems: OutboxItem[]) {
  const handled = useHandledStore((s) => s.handled);
  const forget = useHandledStore((s) => s.forget);

  useEffect(() => {
    for (const id of settledShipmentIds(handled, tasks)) forget(id);
  }, [handled, tasks, forget]);

  return {
    isHandled: (shipmentId: string) => handled.has(shipmentId) || handledOnPhone(shipmentId, pendingItems, stuckItems),
    syncState: (shipmentId: string) => phoneSyncState(shipmentId, pendingItems, stuckItems, handled),
  };
}
