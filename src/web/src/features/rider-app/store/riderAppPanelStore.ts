import { create } from "zustand";
import type { MyTask } from "@/features/riders/types";

export type PodStart = "deliver" | "fail";

type RiderAppPanelState =
  | { mode: "closed" }
  | { mode: "record-attempt"; task: MyTask; start: PodStart };

interface RiderAppPanelStore {
  panel: RiderAppPanelState;
  openRecordAttempt: (task: MyTask, start?: PodStart) => void;
  close: () => void;
}

export const useRiderAppPanelStore = create<RiderAppPanelStore>((set) => ({
  panel: { mode: "closed" },
  openRecordAttempt: (task, start = "deliver") => set({ panel: { mode: "record-attempt", task, start } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
