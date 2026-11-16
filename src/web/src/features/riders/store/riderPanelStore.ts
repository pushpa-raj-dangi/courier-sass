import { create } from "zustand";

type RiderPanelState =
  | { mode: "closed" }
  | { mode: "create-rider" }
  | { mode: "create-vehicle" }
  | { mode: "assign-task" }
  | { mode: "detail"; riderId: string };

interface RiderPanelStore {
  panel: RiderPanelState;
  openCreateRider: () => void;
  openCreateVehicle: () => void;
  openAssignTask: () => void;
  openDetail: (riderId: string) => void;
  close: () => void;
}

export const useRiderPanelStore = create<RiderPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreateRider: () => set({ panel: { mode: "create-rider" } }),
  openCreateVehicle: () => set({ panel: { mode: "create-vehicle" } }),
  openAssignTask: () => set({ panel: { mode: "assign-task" } }),
  openDetail: (riderId) => set({ panel: { mode: "detail", riderId } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
