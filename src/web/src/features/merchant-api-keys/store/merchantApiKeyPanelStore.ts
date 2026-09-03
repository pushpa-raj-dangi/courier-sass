import { create } from "zustand";

type MerchantApiKeyPanelState = { mode: "closed" } | { mode: "create" };

interface MerchantApiKeyPanelStore {
  panel: MerchantApiKeyPanelState;
  openCreate: () => void;
  close: () => void;
}

export const useMerchantApiKeyPanelStore = create<MerchantApiKeyPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
