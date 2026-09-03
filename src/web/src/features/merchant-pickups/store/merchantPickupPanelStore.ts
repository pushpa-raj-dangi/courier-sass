import { create } from "zustand";

// No "detail" mode — unlike shipments, a pickup request's full state fits in the list row plus
// a cancel action; nothing warrants its own detail Sheet.
type MerchantPickupPanelState = { mode: "closed" } | { mode: "create" };

interface MerchantPickupPanelStore {
  panel: MerchantPickupPanelState;
  openCreate: () => void;
  close: () => void;
}

export const useMerchantPickupPanelStore = create<MerchantPickupPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
