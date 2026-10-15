import { create } from "zustand";
import type { ManifestDirection } from "@/features/manifests/types";

type ManifestPanelState =
  | { mode: "closed" }
  | { mode: "create" }
  | { mode: "detail"; id: string; direction?: ManifestDirection };

interface ManifestPanelStore {
  panel: ManifestPanelState;
  openCreate: () => void;
  openDetail: (id: string, direction?: ManifestDirection) => void;
  close: () => void;
}

// Global so any component in the (staff) tree can open the manifest sheet without routing to a
// separate page (docs/ARCHITECTURE.md §9a). Keyed by the manifest's Guid id, not Code — there is
// no lookup-by-code endpoint, only GET /{id}. `direction` is relative to the page's branch filter
// (F15 R3b-1 Sheet eyebrow); absent when opened from elsewhere.
export const useManifestPanelStore = create<ManifestPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  openDetail: (id, direction) => set({ panel: direction ? { mode: "detail", id, direction } : { mode: "detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
