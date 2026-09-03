import { create } from "zustand";

type MerchantStaffPanelState = { mode: "closed" } | { mode: "create" };

interface MerchantStaffPanelStore {
  panel: MerchantStaffPanelState;
  openCreate: () => void;
  close: () => void;
}

export const useMerchantStaffPanelStore = create<MerchantStaffPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
