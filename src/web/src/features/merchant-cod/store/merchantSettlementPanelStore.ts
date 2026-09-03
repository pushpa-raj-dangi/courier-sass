import { create } from "zustand";

type MerchantSettlementPanelState = { mode: "closed" } | { mode: "detail"; id: string };

interface MerchantSettlementPanelStore {
  panel: MerchantSettlementPanelState;
  openDetail: (id: string) => void;
  close: () => void;
}

// No create mode — MerchantSettlement rows are born by the courier's own staff-side COD
// workflow (Frontend Phase F7), never by a merchant action. This portal is read-only over them.
export const useMerchantSettlementPanelStore = create<MerchantSettlementPanelStore>((set) => ({
  panel: { mode: "closed" },
  openDetail: (id) => set({ panel: { mode: "detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
