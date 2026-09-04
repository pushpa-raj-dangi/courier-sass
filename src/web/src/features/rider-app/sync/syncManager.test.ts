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
const completeActionWithPhoto: QueuedAction = {
  kind: "delivery-complete",
  riderId: "r1",
  shipmentId: "s5",
  recipientName: "Recipient Two",
  otpVerified: true,
  latitude: null,
  longitude: null,
  collectedCodAmount: null,
  signatureBlob: new Blob(["sig"], { type: "image/png" }),
  photoBlob: new Blob(["photo"], { type: "image/jpeg" }),
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

  it("persists the signature storage key to the outbox item before the complete call — it survives that call's failure, proving it isn't just held in a local variable", async () => {
    await addOutboxItem(completeAction);
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({ storageKey: "sig-key-1" }) }) // pod-upload succeeds
      .mockRejectedValueOnce(new TypeError("Failed to fetch")); // complete call fails (network failure)
    vi.stubGlobal("fetch", fetchMock);

    await drainOutbox();

    expect(fetchMock).toHaveBeenCalledTimes(2);
    const [item] = await listOutboxItems();
    // A network failure on the *second* call is retryable, so the item stays "pending" rather
    // than "stuck" — but the point of this test is that the signature key survived that failure
    // in the persisted record, not just in an in-memory variable that would be lost.
    expect(item.status).toBe("pending");
    expect(item.action.kind).toBe("delivery-complete");
    if (item.action.kind === "delivery-complete") {
      expect(item.action.signatureStorageKey).toBe("sig-key-1");
    }
  });

  it("uploads both signature and photo blobs when both are present, and includes both storage keys in the complete request", async () => {
    await addOutboxItem(completeActionWithPhoto);
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({ storageKey: "sig-key-2" }) }) // signature upload
      .mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({ storageKey: "photo-key-2" }) }) // photo upload
      .mockResolvedValueOnce({ ok: true, status: 200, json: async () => ({ id: "a2", succeeded: true }) }); // complete
    vi.stubGlobal("fetch", fetchMock);

    const result = await drainOutbox();

    expect(result.synced).toHaveLength(1);
    expect(fetchMock).toHaveBeenNthCalledWith(1, "/api/riders/r1/pod-uploads?kind=Signature", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenNthCalledWith(2, "/api/riders/r1/pod-uploads?kind=Photo", expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      "/api/riders/r1/delivery-attempts/complete",
      expect.objectContaining({ body: expect.stringContaining("sig-key-2") })
    );
    const completeBody = fetchMock.mock.calls[2]?.[1]?.body as string;
    expect(completeBody).toContain("photo-key-2");
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

  it("guards against two concurrent drainOutbox() calls double-processing the same item", async () => {
    await addOutboxItem(pickupAction);

    let resolveFetch: (value: unknown) => void = () => {};
    const pendingResponse = new Promise((resolve) => {
      resolveFetch = resolve;
    });
    const fetchMock = vi.fn().mockReturnValue(pendingResponse);
    vi.stubGlobal("fetch", fetchMock);

    // Both calls happen synchronously, before the in-flight fetch resolves — this reproduces
    // startSyncManager()'s realistic overlap (the "online" listener, the "visibilitychange"
    // listener, and the 30s poll can all fire while a prior drain is still mid-flight).
    const drain1 = drainOutbox();
    const drain2 = drainOutbox();

    resolveFetch({ ok: true, status: 200, json: async () => ({}) });

    const [result1, result2] = await Promise.all([drain1, drain2]);

    // Only ONE network call for the item — the second caller awaited the first pass's
    // in-flight promise instead of starting its own pass over the same "pending" item.
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result1.synced).toHaveLength(1);
    expect(result2).toBe(result1);
    expect(await listOutboxItems()).toHaveLength(0);

    // A drain started *after* the first has fully settled is a fresh, independent pass.
    fetchMock.mockClear();
    await addOutboxItem(pickupAction);
    fetchMock.mockResolvedValue({ ok: true, status: 200, json: async () => ({}) });
    await drainOutbox();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
