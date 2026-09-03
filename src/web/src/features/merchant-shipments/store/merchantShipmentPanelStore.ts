import { create } from "zustand";

type MerchantShipmentPanelState =
  | { mode: "closed" }
  | { mode: "create" }
  | { mode: "detail"; awb: string };

interface MerchantShipmentPanelStore {
  panel: MerchantShipmentPanelState;
  openCreate: () => void;
  openDetail: (awb: string) => void;
  close: () => void;
}

export const useMerchantShipmentPanelStore = create<MerchantShipmentPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  openDetail: (awb) => set({ panel: { mode: "detail", awb } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
