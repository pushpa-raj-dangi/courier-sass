import { create } from "zustand";

type RiderPanelState =
  | { mode: "closed" }
  | { mode: "create-rider" };

interface RiderPanelStore {
  panel: RiderPanelState;
  openCreateRider: () => void;
  close: () => void;
}

export const useRiderPanelStore = create<RiderPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreateRider: () => set({ panel: { mode: "create-rider" } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
