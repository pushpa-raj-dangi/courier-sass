import { create } from "zustand";

type BranchPanelState =
  | { mode: "closed" }
  | { mode: "create" }
  | { mode: "detail"; branchId: string };

interface BranchPanelStore {
  panel: BranchPanelState;
  openCreate: () => void;
  openDetail: (branchId: string) => void;
  close: () => void;
}

// Global so any component in the (staff) tree — a list row, BranchSwitcher in
// TopNav, a future dashboard quick-link — can open the branch sheet without
// routing to a separate page (CLAUDE.md convention: detail/create UI is
// always a right-side Sheet, never its own route).
export const useBranchPanelStore = create<BranchPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  openDetail: (branchId) => set({ panel: { mode: "detail", branchId } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
