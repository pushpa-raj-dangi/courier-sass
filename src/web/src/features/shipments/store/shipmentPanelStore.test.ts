import { describe, expect, it, beforeEach } from "vitest";
import { useShipmentPanelStore } from "./shipmentPanelStore";

describe("useShipmentPanelStore", () => {
  beforeEach(() => {
    useShipmentPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useShipmentPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate switches to the create panel", () => {
    useShipmentPanelStore.getState().openCreate();
    expect(useShipmentPanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("openDetail switches to the detail panel with the given awb", () => {
    useShipmentPanelStore.getState().openDetail("AWB123");
    expect(useShipmentPanelStore.getState().panel).toEqual({ mode: "detail", awb: "AWB123" });
  });

  it("close returns to closed from either panel", () => {
    useShipmentPanelStore.getState().openDetail("AWB123");
    useShipmentPanelStore.getState().close();
    expect(useShipmentPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
