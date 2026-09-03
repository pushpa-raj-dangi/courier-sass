import { describe, expect, it, beforeEach } from "vitest";
import { useMerchantSettlementPanelStore } from "./merchantSettlementPanelStore";

describe("useMerchantSettlementPanelStore", () => {
  beforeEach(() => {
    useMerchantSettlementPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useMerchantSettlementPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openDetail switches to the detail panel with the given id", () => {
    useMerchantSettlementPanelStore.getState().openDetail("s1");
    expect(useMerchantSettlementPanelStore.getState().panel).toEqual({ mode: "detail", id: "s1" });
  });

  it("close returns to closed", () => {
    useMerchantSettlementPanelStore.getState().openDetail("s1");
    useMerchantSettlementPanelStore.getState().close();
    expect(useMerchantSettlementPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
