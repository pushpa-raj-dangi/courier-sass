import { create } from "zustand";
import type { MyTask } from "@/features/riders/types";

type MyDeliveriesPanelState =
  | { mode: "closed" }
  | { mode: "record-attempt"; task: MyTask };

interface MyDeliveriesPanelStore {
  panel: MyDeliveriesPanelState;
  openRecordAttempt: (task: MyTask) => void;
  close: () => void;
}

export const useMyDeliveriesPanelStore = create<MyDeliveriesPanelStore>((set) => ({
  panel: { mode: "closed" },
  openRecordAttempt: (task) => set({ panel: { mode: "record-attempt", task } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
