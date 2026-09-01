import { describe, expect, it, beforeEach } from "vitest";
import { useCodPanelStore } from "./codPanelStore";

describe("useCodPanelStore", () => {
  beforeEach(() => {
    useCodPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useCodPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCollectionDetail sets collection-detail mode with the given id", () => {
    useCodPanelStore.getState().openCollectionDetail("c1");
    expect(useCodPanelStore.getState().panel).toEqual({ mode: "collection-detail", id: "c1" });
  });

  it("openRiderSettlementCreate sets rider-settlement-create mode", () => {
    useCodPanelStore.getState().openRiderSettlementCreate();
    expect(useCodPanelStore.getState().panel).toEqual({ mode: "rider-settlement-create" });
  });

  it("openAdjustmentDetail sets adjustment-detail mode with the given id", () => {
    useCodPanelStore.getState().openAdjustmentDetail("a1");
    expect(useCodPanelStore.getState().panel).toEqual({ mode: "adjustment-detail", id: "a1" });
  });

  it("close returns to closed from any mode", () => {
    useCodPanelStore.getState().openMerchantSettlementDetail("m1");
    useCodPanelStore.getState().close();
    expect(useCodPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
