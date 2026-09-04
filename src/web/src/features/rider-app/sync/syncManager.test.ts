import "fake-indexeddb/auto";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { addOutboxItem, listOutboxItems, resetOutboxDbForTests } from "./outboxDb";
import { drainOutbox } from "./syncManager";
import type { QueuedAction } from "./types";

const pickupAction: QueuedAction = { kind: "pickup-scan", riderId: "r1", shipmentId: "s1", awb: "AWB001" };
const failAction: QueuedAction = {
  kind: "delivery-fail",
  riderId: "r1",
  shipmentId: "s2",
  input: { shipmentId: "s2", reason: "CustomerUnavailable", customerContacted: true },
};
const completeAction: QueuedAction = {
  kind: "delivery-complete",
  riderId: "r1",
  shipmentId: "s3",
  recipientName: "Recipient One",
  otpVerified: true,
  latitude: null,
  longitude: null,
  collectedCodAmount: null,
  signatureBlob: new Blob(["sig"], { type: "image/png" }),
  photoBlob: null,
  signatureStorageKey: null,
  photoStorageKey: null,
};

describe("drainOutbox", () => {
  beforeEach(() => {
    resetOutboxDbForTests();
    vi.restoreAllMocks();
  });

  it("replays a pickup-scan item and removes it on success", async () => {
    await addOutboxItem(pickupAction);
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({}) }));

    const result = await drainOutbox();

    expect(result.synced).toHaveLength(1);
    expect(await listOutboxItems()).toHaveLength(0);
    expect(fetch).toHaveBeenCalledWith(
      "/api/riders/r1/scan/pickup",
      expect.objectContaining({ method: "POST", headers: expect.objectContaining({ "Idempotency-Key": expect.any(String) }) })
    );
  });

  it("uploads signature/photo blobs before calling delivery-attempts/complete, and persists the storage key between attempts", async () => {
    await addOutboxItem(completeAction);
    const fetchMock = vi.fn()
      .mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({ storageKey: "sig-key-1" }) }) // pod-upload
      .mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({ id: "a1", succeeded: true }) }); // complete
    vi.stubGlobal("fetch", fetchMock);

    await drainOutbox();

    expect(fetchMock).toHaveBeenNthCalledWith(1, "/api/riders/r1/pod-uploads?kind=Signature", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "/api/riders/r1/delivery-attempts/complete",
      expect.objectContaining({ body: expect.stringContaining("sig-key-1") })
    );
  });

  it("marks a 4xx failure as stuck and does not retry it on a later drain", async () => {
    await addOutboxItem(failAction);
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: false, status: 400, text: async () => "invalid state" }));

    const result = await drainOutbox();

    expect(result.stuck).toHaveLength(1);
    const [item] = await listOutboxItems();
    expect(item.status).toBe("stuck");
    expect(item.lastError).toContain("invalid state");

    vi.mocked(fetch).mockClear();
    await drainOutbox();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("leaves a network-failure item pending (not stuck) so a later drain retries it", async () => {
    await addOutboxItem(pickupAction);
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));

    const result = await drainOutbox();

    expect(result.stuck).toHaveLength(0);
    expect(result.synced).toHaveLength(0);
    const [item] = await listOutboxItems();
    expect(item.status).toBe("pending");
  });

  it("blocks later items for the same shipment once one goes stuck, but still drains a different shipment's item", async () => {
    await addOutboxItem({ kind: "pickup-scan", riderId: "r1", shipmentId: "s1", awb: "AWB001" });
    await addOutboxItem({
      kind: "delivery-fail",
      riderId: "r1",
      shipmentId: "s1",
      input: { shipmentId: "s1", reason: "CustomerUnavailable", customerContacted: true },
    });
    await addOutboxItem({ kind: "pickup-scan", riderId: "r1", shipmentId: "s-other", awb: "AWB002" });

    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
        const body = typeof init?.body === "string" ? init.body : "";
        return body.includes("AWB001")
          ? Promise.resolve({ ok: false, status: 400, text: async () => "wrong state" })
          : Promise.resolve({ ok: true, status: 200, json: async () => ({}) });
      })
    );

    const result = await drainOutbox();

    expect(result.stuck).toHaveLength(1); // the first s1 item
    expect(result.synced).toHaveLength(1); // the s-other item, drained despite s1 being blocked
    const remaining = await listOutboxItems();
    expect(remaining).toHaveLength(2); // the stuck s1 item + the still-queued second s1 item
  });
});
