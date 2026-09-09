import { create } from "zustand";

interface PlanPanelStore {
  isCreateOpen: boolean;
  openCreate: () => void;
  close: () => void;
}

// Global so the plans page's "New Plan" button can open the create sheet without routing to a
// separate page (CLAUDE.md convention: detail/create UI is always a right-side Sheet, never its
// own route). No detail mode yet — plans are list + create + retire only for this task.
export const usePlanPanelStore = create<PlanPanelStore>((set) => ({
  isCreateOpen: false,
  openCreate: () => set({ isCreateOpen: true }),
  close: () => set({ isCreateOpen: false }),
}));
