import { create } from "zustand";

type ReturnShipmentPanelState = { mode: "closed" } | { mode: "detail"; id: string };

interface ReturnShipmentPanelStore {
  panel: ReturnShipmentPanelState;
  openDetail: (id: string) => void;
  close: () => void;
}

// Global so any component in the (staff) tree can open the return-shipment sheet without routing
// to a separate page (docs/ARCHITECTURE.md §9a). No create mode — return shipments are only ever
// system-created by NdrService.ResolveAsync when an NDR resolves as Rto/AutoRto, never through
// this UI.
export const useReturnShipmentPanelStore = create<ReturnShipmentPanelStore>((set) => ({
  panel: { mode: "closed" },
  openDetail: (id) => set({ panel: { mode: "detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
