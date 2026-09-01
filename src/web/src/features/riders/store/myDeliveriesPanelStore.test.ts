import { describe, expect, it, beforeEach } from "vitest";
import { useMyDeliveriesPanelStore } from "./myDeliveriesPanelStore";
import type { MyTask } from "@/features/riders/types";

const task: MyTask = {
  taskId: "t1", shipmentId: "s1", type: "Delivery", sequence: 1,
  awb: "AWB001", recipientName: "Recipient One", recipientPhone: "9800000000",
  recipientAddressLine: "Address", recipientWardNumber: 1, codAmount: 0,
};

describe("useMyDeliveriesPanelStore", () => {
  beforeEach(() => {
    useMyDeliveriesPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useMyDeliveriesPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openRecordAttempt switches to the record-attempt panel with the given task", () => {
    useMyDeliveriesPanelStore.getState().openRecordAttempt(task);
    expect(useMyDeliveriesPanelStore.getState().panel).toEqual({ mode: "record-attempt", task });
  });

  it("close returns to closed", () => {
    useMyDeliveriesPanelStore.getState().openRecordAttempt(task);
    useMyDeliveriesPanelStore.getState().close();
    expect(useMyDeliveriesPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
