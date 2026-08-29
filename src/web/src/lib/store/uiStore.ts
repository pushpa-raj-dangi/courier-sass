import { create } from "zustand";

interface UiState {
  activeBranchId: string | null;
  setActiveBranchId: (id: string | null) => void;
  sidebarCollapsed: boolean;
  toggleSidebar: () => void;
}

export const useUiStore = create<UiState>((set) => ({
  activeBranchId: null,
  setActiveBranchId: (id) => set({ activeBranchId: id }),
  sidebarCollapsed: false,
  toggleSidebar: () => set((state) => ({ sidebarCollapsed: !state.sidebarCollapsed })),
}));
