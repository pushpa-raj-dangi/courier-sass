import "fake-indexeddb/auto";
import { beforeEach, describe, expect, it } from "vitest";
import {
  addOutboxItem,
  listOutboxItems,
  removeOutboxItem,
  resetOutboxDbForTests,
  updateOutboxItem,
} from "./outboxDb";
import type { QueuedAction } from "./types";

const pickupAction: QueuedAction = { kind: "pickup-scan", riderId: "r1", shipmentId: "s1", awb: "AWB001" };
const failAction: QueuedAction = {
  kind: "delivery-fail",
  riderId: "r1",
  shipmentId: "s2",
  input: { shipmentId: "s2", reason: "CustomerUnavailable", customerContacted: true },
};

describe("outboxDb", () => {
  beforeEach(async () => {
    resetOutboxDbForTests();
  });

  it("adds an item with a generated id, idempotency key, timestamp, and pending status", async () => {
    const item = await addOutboxItem(pickupAction);

    expect(item.id).toBeTruthy();
    expect(item.idempotencyKey).toBeTruthy();
    expect(item.status).toBe("pending");
    expect(item.action).toEqual(pickupAction);
  });

  it("lists items ordered by createdAt ascending (FIFO)", async () => {
    const first = await addOutboxItem(pickupAction);
    const second = await addOutboxItem(failAction);

    const items = await listOutboxItems();

    expect(items.map((i) => i.id)).toEqual([first.id, second.id]);
  });

  it("updates an item in place", async () => {
    const item = await addOutboxItem(pickupAction);

    await updateOutboxItem(item.id, { status: "stuck", lastError: "boom" });

    const [updated] = await listOutboxItems();
    expect(updated.status).toBe("stuck");
    expect(updated.lastError).toBe("boom");
  });

  it("removes an item", async () => {
    const item = await addOutboxItem(pickupAction);

    await removeOutboxItem(item.id);

    expect(await listOutboxItems()).toHaveLength(0);
  });
});
