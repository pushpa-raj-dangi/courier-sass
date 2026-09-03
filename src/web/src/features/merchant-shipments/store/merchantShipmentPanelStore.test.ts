import { describe, expect, it, beforeEach } from "vitest";
import { useMerchantShipmentPanelStore } from "./merchantShipmentPanelStore";

describe("useMerchantShipmentPanelStore", () => {
  beforeEach(() => {
    useMerchantShipmentPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useMerchantShipmentPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate switches to the create panel", () => {
    useMerchantShipmentPanelStore.getState().openCreate();
    expect(useMerchantShipmentPanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("openDetail switches to the detail panel with the given awb", () => {
    useMerchantShipmentPanelStore.getState().openDetail("AWB123");
    expect(useMerchantShipmentPanelStore.getState().panel).toEqual({ mode: "detail", awb: "AWB123" });
  });

  it("close returns to closed from either panel", () => {
    useMerchantShipmentPanelStore.getState().openDetail("AWB123");
    useMerchantShipmentPanelStore.getState().close();
    expect(useMerchantShipmentPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
