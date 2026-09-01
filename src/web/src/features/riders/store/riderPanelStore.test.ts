import { describe, expect, it, beforeEach } from "vitest";
import { useRiderPanelStore } from "./riderPanelStore";

describe("useRiderPanelStore", () => {
  beforeEach(() => {
    useRiderPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useRiderPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreateRider switches to the create-rider panel", () => {
    useRiderPanelStore.getState().openCreateRider();
    expect(useRiderPanelStore.getState().panel).toEqual({ mode: "create-rider" });
  });

  it("close returns to closed", () => {
    useRiderPanelStore.getState().openCreateRider();
    useRiderPanelStore.getState().close();
    expect(useRiderPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
