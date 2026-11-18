import { create } from "zustand";

type MerchantPickupPanelState = { mode: "closed" } | { mode: "create" } | { mode: "detail"; id: string };

interface MerchantPickupPanelStore {
  panel: MerchantPickupPanelState;
  openCreate: () => void;
  openDetail: (id: string) => void;
  close: () => void;
}

export const useMerchantPickupPanelStore = create<MerchantPickupPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  openDetail: (id) => set({ panel: { mode: "detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
