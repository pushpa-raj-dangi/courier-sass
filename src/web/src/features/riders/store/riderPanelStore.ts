import { create } from "zustand";

type RiderPanelState =
  | { mode: "closed" }
  | { mode: "create-rider" }
  | { mode: "create-vehicle" }
  | { mode: "assign-task" };

interface RiderPanelStore {
  panel: RiderPanelState;
  openCreateRider: () => void;
  openCreateVehicle: () => void;
  openAssignTask: () => void;
  close: () => void;
}

export const useRiderPanelStore = create<RiderPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreateRider: () => set({ panel: { mode: "create-rider" } }),
  openCreateVehicle: () => set({ panel: { mode: "create-vehicle" } }),
  openAssignTask: () => set({ panel: { mode: "assign-task" } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
