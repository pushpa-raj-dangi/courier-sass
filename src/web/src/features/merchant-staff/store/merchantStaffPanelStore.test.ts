import { describe, expect, it, beforeEach } from "vitest";
import { useMerchantStaffPanelStore } from "./merchantStaffPanelStore";

describe("useMerchantStaffPanelStore", () => {
  beforeEach(() => {
    useMerchantStaffPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useMerchantStaffPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate switches to the create panel", () => {
    useMerchantStaffPanelStore.getState().openCreate();
    expect(useMerchantStaffPanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("close returns to closed", () => {
    useMerchantStaffPanelStore.getState().openCreate();
    useMerchantStaffPanelStore.getState().close();
    expect(useMerchantStaffPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
