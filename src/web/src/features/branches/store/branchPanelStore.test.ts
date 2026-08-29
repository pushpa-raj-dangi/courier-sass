import { describe, expect, it, beforeEach } from "vitest";
import { useBranchPanelStore } from "./branchPanelStore";

describe("useBranchPanelStore", () => {
  beforeEach(() => {
    useBranchPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useBranchPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate switches to the create panel", () => {
    useBranchPanelStore.getState().openCreate();
    expect(useBranchPanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("openDetail switches to the detail panel with the given branch id", () => {
    useBranchPanelStore.getState().openDetail("branch-1");
    expect(useBranchPanelStore.getState().panel).toEqual({ mode: "detail", branchId: "branch-1" });
  });

  it("close returns to closed from either panel", () => {
    useBranchPanelStore.getState().openDetail("branch-1");
    useBranchPanelStore.getState().close();
    expect(useBranchPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
