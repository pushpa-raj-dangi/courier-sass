import { create } from "zustand";
import type { MyTask } from "@/features/riders/types";

type RiderAppPanelState =
  | { mode: "closed" }
  | { mode: "record-attempt"; task: MyTask };

interface RiderAppPanelStore {
  panel: RiderAppPanelState;
  openRecordAttempt: (task: MyTask) => void;
  close: () => void;
}

export const useRiderAppPanelStore = create<RiderAppPanelStore>((set) => ({
  panel: { mode: "closed" },
  openRecordAttempt: (task) => set({ panel: { mode: "record-attempt", task } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
