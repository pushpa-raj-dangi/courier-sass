import { create } from "zustand";
import { listOutboxItems } from "./outboxDb";
import type { OutboxItem } from "./types";

interface OutboxStatusState {
  pendingCount: number;
  stuckItems: OutboxItem[];
  lastSyncedAt: number | null;
  refresh: () => Promise<void>;
  recordSync: () => void;
}

export const useOutboxStatusStore = create<OutboxStatusState>((set) => ({
  pendingCount: 0,
  stuckItems: [],
  lastSyncedAt: null,
  refresh: async () => {
    const items = await listOutboxItems();
    set({
      pendingCount: items.filter((i) => i.status !== "stuck").length,
      stuckItems: items.filter((i) => i.status === "stuck"),
    });
  },
  recordSync: () => set({ lastSyncedAt: Date.now() }),
}));
