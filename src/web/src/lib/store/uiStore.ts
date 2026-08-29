import { create } from "zustand";

interface UiState {
  activeBranchId: string | null;
  setActiveBranchId: (id: string | null) => void;
}

export const useUiStore = create<UiState>((set) => ({
  activeBranchId: null,
  setActiveBranchId: (id) => set({ activeBranchId: id }),
}));
