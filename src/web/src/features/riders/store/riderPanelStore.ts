import { create } from "zustand";

type RiderPanelState =
  | { mode: "closed" }
  | { mode: "create-rider" }
  | { mode: "create-vehicle" };

interface RiderPanelStore {
  panel: RiderPanelState;
  openCreateRider: () => void;
  openCreateVehicle: () => void;
  close: () => void;
}

export const useRiderPanelStore = create<RiderPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreateRider: () => set({ panel: { mode: "create-rider" } }),
  openCreateVehicle: () => set({ panel: { mode: "create-vehicle" } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
