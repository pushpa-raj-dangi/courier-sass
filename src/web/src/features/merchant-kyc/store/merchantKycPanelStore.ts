import { create } from "zustand";
import type { KycDocumentType } from "@/features/merchant-kyc/types";

type MerchantKycPanelState = { mode: "closed" } | { mode: "upload"; documentType: KycDocumentType };

interface MerchantKycPanelStore {
  panel: MerchantKycPanelState;
  openUpload: (documentType?: KycDocumentType) => void;
  close: () => void;
}

export const useMerchantKycPanelStore = create<MerchantKycPanelStore>((set) => ({
  panel: { mode: "closed" },
  openUpload: (documentType = "CitizenshipDocument") => set({ panel: { mode: "upload", documentType } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
