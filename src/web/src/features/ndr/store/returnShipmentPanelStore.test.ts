import { describe, expect, it, beforeEach } from "vitest";
import { useReturnShipmentPanelStore } from "./returnShipmentPanelStore";

describe("useReturnShipmentPanelStore", () => {
  beforeEach(() => {
    useReturnShipmentPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useReturnShipmentPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openDetail switches to the detail panel with the given id", () => {
    useReturnShipmentPanelStore.getState().openDetail("rs1");
    expect(useReturnShipmentPanelStore.getState().panel).toEqual({ mode: "detail", id: "rs1" });
  });

  it("close returns to closed", () => {
    useReturnShipmentPanelStore.getState().openDetail("rs1");
    useReturnShipmentPanelStore.getState().close();
    expect(useReturnShipmentPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
