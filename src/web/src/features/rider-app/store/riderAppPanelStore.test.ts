import { beforeEach, describe, expect, it } from "vitest";
import type { MyTask } from "@/features/riders/types";
import { useRiderAppPanelStore } from "./riderAppPanelStore";

const task: MyTask = {
  taskId: "t1", shipmentId: "s1", type: "Delivery", sequence: 1, awb: "AWB001",
  recipientName: "R", recipientPhone: "9800000000", recipientAddressLine: "A", recipientWardNumber: 1, codAmount: 0,
};

describe("useRiderAppPanelStore", () => {
  beforeEach(() => useRiderAppPanelStore.getState().close());

  it("opens the POD Sheet on delivery by default, or straight to the failure form", () => {
    useRiderAppPanelStore.getState().openRecordAttempt(task);
    expect(useRiderAppPanelStore.getState().panel).toEqual({ mode: "record-attempt", task, start: "deliver" });
    useRiderAppPanelStore.getState().openRecordAttempt(task, "fail");
    expect(useRiderAppPanelStore.getState().panel).toEqual({ mode: "record-attempt", task, start: "fail" });
    useRiderAppPanelStore.getState().close();
    expect(useRiderAppPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
