import { describe, expect, it, beforeEach } from "vitest";
import { useManifestPanelStore } from "./manifestPanelStore";

describe("useManifestPanelStore", () => {
  beforeEach(() => {
    useManifestPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useManifestPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate switches to the create panel", () => {
    useManifestPanelStore.getState().openCreate();
    expect(useManifestPanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("openDetail switches to the detail panel with the given id", () => {
    useManifestPanelStore.getState().openDetail("m1");
    expect(useManifestPanelStore.getState().panel).toEqual({ mode: "detail", id: "m1" });
  });

  it("openDetail carries the direction relative to the page branch", () => {
    useManifestPanelStore.getState().openDetail("m1", "incoming");
    expect(useManifestPanelStore.getState().panel).toEqual({ mode: "detail", id: "m1", direction: "incoming" });
  });

  it("close returns to closed from either panel", () => {
    useManifestPanelStore.getState().openDetail("m1");
    useManifestPanelStore.getState().close();
    expect(useManifestPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
