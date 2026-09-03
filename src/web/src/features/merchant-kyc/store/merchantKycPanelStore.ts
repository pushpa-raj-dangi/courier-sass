import { create } from "zustand";

type MerchantKycPanelState = { mode: "closed" } | { mode: "upload" };

interface MerchantKycPanelStore {
  panel: MerchantKycPanelState;
  openUpload: () => void;
  close: () => void;
}

export const useMerchantKycPanelStore = create<MerchantKycPanelStore>((set) => ({
  panel: { mode: "closed" },
  openUpload: () => set({ panel: { mode: "upload" } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
