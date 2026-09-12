import { create } from "zustand";
import type { RecentJobSummary } from "@/features/platform-jobs/types";

type PlatformJobPanelState = { mode: "closed" } | { mode: "detail"; job: RecentJobSummary };

interface PlatformJobPanelStore {
  panel: PlatformJobPanelState;
  openDetail: (job: RecentJobSummary) => void;
  close: () => void;
}

export const usePlatformJobPanelStore = create<PlatformJobPanelStore>((set) => ({
  panel: { mode: "closed" },
  openDetail: (job) => set({ panel: { mode: "detail", job } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
