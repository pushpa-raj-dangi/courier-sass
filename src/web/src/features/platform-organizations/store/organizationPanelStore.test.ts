import { describe, expect, it, beforeEach } from "vitest";
import { useOrganizationPanelStore } from "./organizationPanelStore";

describe("useOrganizationPanelStore", () => {
  beforeEach(() => {
    useOrganizationPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useOrganizationPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openDetail switches to the detail panel with the given organization id", () => {
    useOrganizationPanelStore.getState().openDetail("org-1");
    expect(useOrganizationPanelStore.getState().panel).toEqual({ mode: "detail", organizationId: "org-1" });
  });

  it("close returns to closed from the detail panel", () => {
    useOrganizationPanelStore.getState().openDetail("org-1");
    useOrganizationPanelStore.getState().close();
    expect(useOrganizationPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
