import { create } from "zustand";

type ManifestPanelState =
  | { mode: "closed" }
  | { mode: "create" }
  | { mode: "detail"; id: string };

interface ManifestPanelStore {
  panel: ManifestPanelState;
  openCreate: () => void;
  openDetail: (id: string) => void;
  close: () => void;
}

// Global so any component in the (staff) tree can open the manifest sheet without routing to a
// separate page (docs/ARCHITECTURE.md §9a). Keyed by the manifest's Guid id, not Code — there is
// no lookup-by-code endpoint, only GET /{id}.
export const useManifestPanelStore = create<ManifestPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  openDetail: (id) => set({ panel: { mode: "detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
