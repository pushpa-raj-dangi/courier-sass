import { describe, expect, it, beforeEach } from "vitest";
import { useMerchantKycPanelStore } from "./merchantKycPanelStore";

describe("useMerchantKycPanelStore", () => {
  beforeEach(() => {
    useMerchantKycPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useMerchantKycPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openUpload switches to the upload panel", () => {
    useMerchantKycPanelStore.getState().openUpload();
    expect(useMerchantKycPanelStore.getState().panel).toEqual({ mode: "upload", documentType: "CitizenshipDocument" });
  });

  it("openUpload keeps a preselected document type", () => {
    useMerchantKycPanelStore.getState().openUpload("BankAccountProof");
    expect(useMerchantKycPanelStore.getState().panel).toEqual({ mode: "upload", documentType: "BankAccountProof" });
  });

  it("close returns to closed", () => {
    useMerchantKycPanelStore.getState().openUpload();
    useMerchantKycPanelStore.getState().close();
    expect(useMerchantKycPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
