import { describe, expect, it } from "vitest";
import type { MyTodayTask } from "@/features/riders/types";
import type { OutboxItem } from "@/features/rider-app/sync/types";
import {
  awbTail, cashMatch, failureReasonLabel, firstName, groupRunTasks, handledOnPhone, hasStaffPermissions, mapsHref, nextStop,
  phoneSyncState, settledShipmentIds, taskResultLabel, taskSyncState,
} from "./runModel";

function task(overrides: Partial<MyTodayTask>): MyTodayTask {
  return {
    taskId: "t1", shipmentId: "s1", type: "Delivery", sequence: 1, awb: "NX-260930-00148", recipientName: "Sita Shrestha",
    recipientPhone: "9800000412", recipientAddressLine: "Jawalakhel lane", recipientWardNumber: 4, codAmount: 2500,
    status: "Pending", ...overrides,
  };
}

function outboxItem(shipmentId: string, status: OutboxItem["status"]): OutboxItem {
  return { id: `o-${shipmentId}`, action: { kind: "pickup-scan", riderId: "r1", shipmentId, awb: "A" }, idempotencyKey: "k", createdAt: 0, status };
}

describe("runModel", () => {
  it("finds the next pending stop by sequence", () => {
    const tasks = [task({ taskId: "a", sequence: 3 }), task({ taskId: "b", sequence: 1, status: "Completed" }), task({ taskId: "c", sequence: 2 })];
    expect(nextStop(tasks)?.taskId).toBe("c");
    expect(nextStop([task({ status: "Failed" })])).toBeUndefined();
  });

  it("groups pending deliveries, pending pickups and done tasks in sequence order", () => {
    const groups = groupRunTasks([
      task({ taskId: "d2", sequence: 4 }),
      task({ taskId: "p1", sequence: 2, type: "Pickup" }),
      task({ taskId: "d1", sequence: 3 }),
      task({ taskId: "x", sequence: 1, status: "Completed", type: "Pickup" }),
      task({ taskId: "f", sequence: 5, status: "Failed" }),
    ]);
    expect(groups.toDeliver.map((t) => t.taskId)).toEqual(["d1", "d2"]);
    expect(groups.pickups.map((t) => t.taskId)).toEqual(["p1"]);
    expect(groups.done.map((t) => t.taskId)).toEqual(["x", "f"]);
  });

  it("shortens AWBs and builds a maps search link", () => {
    expect(awbTail("NX-260930-00148")).toBe("…00148");
    expect(awbTail("AB12")).toBe("AB12");
    expect(mapsHref("Jawalakhel lane", 4)).toBe("https://www.google.com/maps/search/?api=1&query=Jawalakhel%20lane%2C%20Ward%204");
  });

  it("compares cash received with the COD due", () => {
    expect(cashMatch(2500, 2500)).toEqual({ kind: "match", difference: 0 });
    expect(cashMatch(2500, 2000)).toEqual({ kind: "short", difference: 500 });
    expect(cashMatch(2500, 2600.5)).toEqual({ kind: "over", difference: 100.5 });
  });

  it("derives a task's sync state from the outbox, needs-review winning", () => {
    expect(taskSyncState("s1", [], [])).toBe("synced");
    expect(taskSyncState("s1", [outboxItem("s1", "pending")], [])).toBe("waiting");
    expect(taskSyncState("s1", [outboxItem("s1", "pending")], [outboxItem("s1", "stuck")])).toBe("needs-review");
    expect(taskSyncState("s1", [outboxItem("s2", "pending")], [])).toBe("synced");
  });

  it("counts a shipment as handled on this phone while it has a pending or stuck outbox item", () => {
    expect(handledOnPhone("s1", [], [])).toBe(false);
    expect(handledOnPhone("s1", [outboxItem("s1", "pending")], [])).toBe(true);
    expect(handledOnPhone("s1", [outboxItem("s1", "syncing")], [])).toBe(true);
    expect(handledOnPhone("s1", [], [outboxItem("s1", "stuck")])).toBe(true);
    expect(handledOnPhone("s1", [outboxItem("s2", "pending")], [outboxItem("s3", "stuck")])).toBe(false);
  });

  it("never shows a shipment handled this session as synced until the run shows it done", () => {
    const session = new Set(["s1"]);
    // Outbox already drained, refetch not back yet: still "waiting", never "synced".
    expect(phoneSyncState("s1", [], [], session)).toBe("waiting");
    expect(phoneSyncState("s1", [], [outboxItem("s1", "stuck")], session)).toBe("needs-review");
    expect(phoneSyncState("s2", [], [], session)).toBe("synced");
    expect(phoneSyncState("s2", [outboxItem("s2", "pending")], [], session)).toBe("waiting");
  });

  it("settles session-handled shipments only once the run shows the task no longer pending", () => {
    const session = new Set(["s1", "s2", "s3"]);
    const tasks = [task({ shipmentId: "s1", status: "Completed" }), task({ shipmentId: "s2", status: "Pending" }), task({ shipmentId: "s4", status: "Failed" })];
    expect(settledShipmentIds(session, tasks)).toEqual(["s1"]);
    expect(settledShipmentIds(session, undefined)).toEqual([]);
  });

  it("treats any permission outside the rider set as staff", () => {
    expect(hasStaffPermissions(["delivery.attempt", "delivery.complete", "cod.collect", "shipment.scan"])).toBe(false);
    expect(hasStaffPermissions(["shipment.scan", "manifest.read"])).toBe(true);
    expect(hasStaffPermissions([])).toBe(false);
  });

  it("words names, failure reasons and task results", () => {
    expect(firstName("Bikash  Tamang")).toBe("Bikash");
    expect(firstName("  ")).toBe("");
    expect(failureReasonLabel("CustomerRequestedReschedule")).toBe("Customer asked to reschedule");
    expect(taskResultLabel(task({ status: "Completed" }))).toBe("Delivered");
    expect(taskResultLabel(task({ status: "Completed", type: "Pickup" }))).toBe("Picked up");
    expect(taskResultLabel(task({ status: "Failed" }))).toBe("Failed");
  });
});
