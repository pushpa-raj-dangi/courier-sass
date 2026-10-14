import { create } from "zustand";

type RiderDepositPrefill = { riderId: string; branchId: string };

type CodPanelState =
  | { mode: "closed" }
  | { mode: "collection-detail"; id: string }
  | { mode: "rider-settlement-create"; prefill?: RiderDepositPrefill }
  | { mode: "rider-settlement-detail"; id: string }
  | { mode: "branch-settlement-create" }
  | { mode: "branch-settlement-detail"; id: string }
  | { mode: "merchant-settlement-create" }
  | { mode: "merchant-settlement-detail"; id: string }
  | { mode: "adjustment-detail"; id: string };

interface CodPanelStore {
  panel: CodPanelState;
  openCollectionDetail: (id: string) => void;
  openRiderSettlementCreate: (prefill?: RiderDepositPrefill) => void;
  openRiderSettlementDetail: (id: string) => void;
  openBranchSettlementCreate: () => void;
  openBranchSettlementDetail: (id: string) => void;
  openMerchantSettlementCreate: () => void;
  openMerchantSettlementDetail: (id: string) => void;
  openAdjustmentDetail: (id: string) => void;
  close: () => void;
}

// One shared store for every /cod sheet (docs/ARCHITECTURE.md §9a) — only one sheet is ever open at
// a time on this single-page, tabbed UI, so a discriminated union across all 5 sheet kinds is
// simpler than 5 separate per-entity stores (contrast Ndr's two-store split, which had two
// genuinely separate pages).
export const useCodPanelStore = create<CodPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCollectionDetail: (id) => set({ panel: { mode: "collection-detail", id } }),
  openRiderSettlementCreate: (prefill) => set({ panel: { mode: "rider-settlement-create", prefill } }),
  openRiderSettlementDetail: (id) => set({ panel: { mode: "rider-settlement-detail", id } }),
  openBranchSettlementCreate: () => set({ panel: { mode: "branch-settlement-create" } }),
  openBranchSettlementDetail: (id) => set({ panel: { mode: "branch-settlement-detail", id } }),
  openMerchantSettlementCreate: () => set({ panel: { mode: "merchant-settlement-create" } }),
  openMerchantSettlementDetail: (id) => set({ panel: { mode: "merchant-settlement-detail", id } }),
  openAdjustmentDetail: (id) => set({ panel: { mode: "adjustment-detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
