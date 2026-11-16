import { create } from "zustand";
import { listOutboxItems } from "./outboxDb";
import type { OutboxItem } from "./types";

interface OutboxStatusState {
  pendingCount: number;
  /** Items still queued on this phone (pending or syncing) — per-task "waiting to sync" state. */
  pendingItems: OutboxItem[];
  stuckItems: OutboxItem[];
  lastSyncedAt: number | null;
  refresh: () => Promise<void>;
  recordSync: () => void;
}

export const useOutboxStatusStore = create<OutboxStatusState>((set) => ({
  pendingCount: 0,
  pendingItems: [],
  stuckItems: [],
  lastSyncedAt: null,
  refresh: async () => {
    const items = await listOutboxItems();
    const pendingItems = items.filter((i) => i.status !== "stuck");
    set({
      pendingCount: pendingItems.length,
      pendingItems,
      stuckItems: items.filter((i) => i.status === "stuck"),
    });
  },
  recordSync: () => set({ lastSyncedAt: Date.now() }),
}));
