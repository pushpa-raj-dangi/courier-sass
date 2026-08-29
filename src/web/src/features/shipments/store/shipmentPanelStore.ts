import { create } from "zustand";

type ShipmentPanelState =
  | { mode: "closed" }
  | { mode: "create" }
  | { mode: "detail"; awb: string };

interface ShipmentPanelStore {
  panel: ShipmentPanelState;
  openCreate: () => void;
  openDetail: (awb: string) => void;
  close: () => void;
}

// Global so any component in the (staff) tree can open the shipment sheet without routing to a
// separate page (docs/ARCHITECTURE.md §9a). Keyed by AWB, not a Guid id — GET /{awb} is how the
// backend reads a shipment, and it's the natural human-facing identifier for this domain.
export const useShipmentPanelStore = create<ShipmentPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  openDetail: (awb) => set({ panel: { mode: "detail", awb } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
