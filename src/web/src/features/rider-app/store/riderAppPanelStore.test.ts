import { describe, expect, it } from "vitest";
import { useRiderAppPanelStore } from "./riderAppPanelStore";
import type { MyTask } from "@/features/riders/types";

const task: MyTask = {
  taskId: "t1", shipmentId: "s1", type: "Delivery", sequence: 1, awb: "AWB001",
  recipientName: "Recipient One", recipientPhone: "9800000000", recipientAddressLine: "New Road", recipientWardNumber: 5, codAmount: 0,
};

describe("riderAppPanelStore", () => {
  it("opens the record-attempt panel with the given task, then closes", () => {
    useRiderAppPanelStore.getState().openRecordAttempt(task);
    expect(useRiderAppPanelStore.getState().panel).toEqual({ mode: "record-attempt", task });

    useRiderAppPanelStore.getState().close();
    expect(useRiderAppPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
