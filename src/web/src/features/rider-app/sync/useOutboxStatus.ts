"use client";

import { useEffect } from "react";
import { useOutboxStatusStore } from "./outboxStatusStore";

export function useOutboxStatus() {
  const pendingCount = useOutboxStatusStore((s) => s.pendingCount);
  const pendingItems = useOutboxStatusStore((s) => s.pendingItems);
  const stuckItems = useOutboxStatusStore((s) => s.stuckItems);
  const lastSyncedAt = useOutboxStatusStore((s) => s.lastSyncedAt);
  const refresh = useOutboxStatusStore((s) => s.refresh);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return { pendingCount, pendingItems, stuckItems, lastSyncedAt, refresh };
}
