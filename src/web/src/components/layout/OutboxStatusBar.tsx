"use client";

import { useOutboxStatus } from "@/features/rider-app/sync/useOutboxStatus";

export function OutboxStatusBar() {
  const { pendingCount, stuckItems, lastSyncedAt } = useOutboxStatus();

  if (pendingCount === 0 && stuckItems.length === 0) {
    return null;
  }

  return (
    <div className="flex items-center justify-between border-b bg-muted px-4 py-1.5 text-xs text-muted-foreground">
      <span>
        {pendingCount > 0 && `${pendingCount} pending`}
        {pendingCount > 0 && stuckItems.length > 0 && " · "}
        {stuckItems.length > 0 && <span className="text-destructive">{stuckItems.length} needs review</span>}
      </span>
      {lastSyncedAt && <span>Last synced {new Date(lastSyncedAt).toLocaleTimeString()}</span>}
    </div>
  );
}
