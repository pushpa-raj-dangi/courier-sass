import { describe, expect, it, beforeEach } from "vitest";
import { useNdrPanelStore } from "./ndrPanelStore";

describe("useNdrPanelStore", () => {
  beforeEach(() => {
    useNdrPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useNdrPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openDetail switches to the detail panel with the given id", () => {
    useNdrPanelStore.getState().openDetail("n1");
    expect(useNdrPanelStore.getState().panel).toEqual({ mode: "detail", id: "n1" });
  });

  it("close returns to closed", () => {
    useNdrPanelStore.getState().openDetail("n1");
    useNdrPanelStore.getState().close();
    expect(useNdrPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
