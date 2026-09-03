import { describe, expect, it, beforeEach } from "vitest";
import { useMerchantPickupPanelStore } from "./merchantPickupPanelStore";

describe("useMerchantPickupPanelStore", () => {
  beforeEach(() => {
    useMerchantPickupPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useMerchantPickupPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate switches to the create panel", () => {
    useMerchantPickupPanelStore.getState().openCreate();
    expect(useMerchantPickupPanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("close returns to closed", () => {
    useMerchantPickupPanelStore.getState().openCreate();
    useMerchantPickupPanelStore.getState().close();
    expect(useMerchantPickupPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
