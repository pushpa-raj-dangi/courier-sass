import { create } from "zustand";

type NdrPanelState = { mode: "closed" } | { mode: "detail"; id: string };

interface NdrPanelStore {
  panel: NdrPanelState;
  openDetail: (id: string) => void;
  close: () => void;
}

// Global so any component in the (staff) tree can open the NDR sheet without routing to a
// separate page (docs/ARCHITECTURE.md §9a). No create mode — NDRs are only ever system-created by
// NdrCreationService.CreateFromFailedAttemptAsync, never through this UI.
export const useNdrPanelStore = create<NdrPanelStore>((set) => ({
  panel: { mode: "closed" },
  openDetail: (id) => set({ panel: { mode: "detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
