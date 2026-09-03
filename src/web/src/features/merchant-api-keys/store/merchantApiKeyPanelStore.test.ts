import { describe, expect, it, beforeEach } from "vitest";
import { useMerchantApiKeyPanelStore } from "./merchantApiKeyPanelStore";

describe("useMerchantApiKeyPanelStore", () => {
  beforeEach(() => {
    useMerchantApiKeyPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useMerchantApiKeyPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate switches to the create panel", () => {
    useMerchantApiKeyPanelStore.getState().openCreate();
    expect(useMerchantApiKeyPanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("close returns to closed", () => {
    useMerchantApiKeyPanelStore.getState().openCreate();
    useMerchantApiKeyPanelStore.getState().close();
    expect(useMerchantApiKeyPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
