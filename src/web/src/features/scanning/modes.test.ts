import { beforeEach, describe, expect, it, vi } from "vitest";

const clientFetch = vi.fn();
vi.mock("@/lib/api/clientFetch", async (orig) => ({ ...(await orig<object>()), clientFetch: (...a: unknown[]) => clientFetch(...a) }));

import { SCAN_MODES, getScanMode, isValidAwb, type ScanContext } from "./modes";
import type { Manifest } from "@/features/manifests/types";

const manifest = { id: "m1", code: "MNF-1", fromBranchId: "a", toBranchId: "b", purpose: "Forward", status: "Draft", shipmentCount: 0 } as Manifest;
const can = (granted: string[]) => (p: string) => granted.includes(p);
const mode = (id: string) => SCAN_MODES.find((m) => m.id === id)!;

describe("scan modes", () => {
  // Vitest 4: a bare mockReset() leaves the mock in a state where a later mockRejectedValue fails the
  // test with the rejection even when it is handled; re-seeding an implementation avoids that.
  beforeEach(() => {
    clientFetch.mockReset().mockImplementation(async () => undefined);
  });

  it("receive with a manifest posts receive-scan", async () => {
    clientFetch.mockResolvedValue({ awb: "QA-1" });
    const ctx: ScanContext = { kind: "manifest", manifest: { ...manifest, status: "InTransit" } };
    const outcome = await mode("receive").submit!("QA-1", ctx);
    expect(clientFetch).toHaveBeenCalledWith("/api/manifests/m1/receive-scan", expect.objectContaining({ method: "POST", body: JSON.stringify({ awb: "QA-1" }) }));
    expect(outcome.ok).toBe(true);
  });

  it("receive without a manifest looks up then receives at origin", async () => {
    clientFetch.mockResolvedValueOnce({ id: "s1", awb: "QA-1", recipientName: "Sita", codAmount: 990 }).mockResolvedValueOnce({});
    const outcome = await mode("receive").submit!("QA-1", { kind: "none" });
    expect(clientFetch.mock.calls[0][0]).toBe("/api/shipments/QA-1");
    expect(clientFetch.mock.calls[1][0]).toBe("/api/shipments/s1/receive-origin");
    expect(outcome).toMatchObject({ ok: true, summary: expect.stringContaining("Sita") });
  });

  it("dispatch and rto post to the manifest scan endpoints", async () => {
    clientFetch.mockResolvedValue({});
    await mode("dispatch").submit!("QA-1", { kind: "manifest", manifest });
    await mode("rto").submit!("QA-1", { kind: "manifest", manifest: { ...manifest, purpose: "Return" } });
    expect(clientFetch.mock.calls.map((c) => c[0])).toEqual(["/api/manifests/m1/scan", "/api/manifests/m1/rto-scan"]);
  });

  it("handover looks up the shipment then assigns a delivery task", async () => {
    clientFetch.mockResolvedValueOnce({ id: "s1", awb: "QA-1", recipientName: "Sita", codAmount: 0 }).mockResolvedValueOnce({ id: "t1" });
    await mode("handover").submit!("QA-1", { kind: "rider", rider: { id: "r1" } as never });
    expect(clientFetch.mock.calls[1]).toEqual([
      "/api/riders/tasks",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ riderId: "r1", shipmentId: "s1", type: "Delivery" }) }),
    ]);
  });

  it("returns a rejected outcome instead of throwing", async () => {
    clientFetch.mockRejectedValue(new Error(JSON.stringify({ error: { reason: "NOT_ON_MANIFEST" } })));
    const outcome = await mode("dispatch").submit!("QA-9", { kind: "manifest", manifest });
    expect(outcome).toEqual({ ok: false, awb: "QA-9", message: "This shipment isn't on this manifest." });
  });

  it("rejects codes that are not AWB-shaped before any lookup", async () => {
    for (const code of ["AB", "QA 1", "QA/1", "../X", "A".repeat(31), "QA-1?X"]) {
      const outcome = await mode("receive").submit!(code, { kind: "none" });
      expect(outcome).toEqual({ ok: false, awb: code, message: "Not a valid AWB" });
    }
    const handover = await mode("handover").submit!("QA/1", { kind: "rider", rider: { id: "r1" } as never });
    expect(handover).toMatchObject({ ok: false, message: "Not a valid AWB" });
    expect(clientFetch).not.toHaveBeenCalled();
  });

  it("accepts real AWB formats", () => {
    for (const code of ["ACME20261005000001", "Q1A2B320261005000042", "RTO-20261005-001", "QA-1"]) expect(isValidAwb(code)).toBe(true);
  });

  it("sort has no submit", () => {
    expect(mode("sort").submit).toBeUndefined();
  });

  it("availability mirrors endpoint permissions", () => {
    // Without shipment.scan, Receive needs a manifest — so it needs the picker (manifest.read) too.
    expect(mode("receive").isAvailable(can(["manifest.receive"]))).toBe(false);
    expect(mode("receive").isAvailable(can(["manifest.receive", "manifest.read"]))).toBe(true);
    expect(mode("receive").requiresContext(can(["manifest.receive"]))).toBe(true);
    expect(mode("receive").requiresContext(can(["shipment.scan"]))).toBe(false);
    expect(mode("dispatch").isAvailable(can(["manifest.create"]))).toBe(false);
    expect(mode("dispatch").isAvailable(can(["manifest.create", "manifest.read"]))).toBe(true);
    expect(mode("handover").isAvailable(can(["delivery.assign"]))).toBe(false);
    expect(mode("handover").isAvailable(can(["delivery.assign", "rider.read"]))).toBe(true);
  });

  it("getScanMode falls back to the first available mode", () => {
    expect(getScanMode("dispatch", can(["shipment.scan"]))?.id).toBe("receive");
    expect(getScanMode("bogus", can(["manifest.create", "manifest.read"]))?.id).toBe("sort");
    expect(getScanMode(null, can([]))).toBeUndefined();
  });

  it("manifest filters pick eligible manifests in scope", () => {
    const inScope = (id: string) => id === "b" || id === "a";
    expect(mode("receive").manifestFilter!({ ...manifest, status: "InTransit" }, inScope)).toBe(true);
    expect(mode("receive").manifestFilter!({ ...manifest, status: "Draft" }, inScope)).toBe(false);
    expect(mode("dispatch").manifestFilter!(manifest, inScope)).toBe(true);
    expect(mode("rto").manifestFilter!(manifest, inScope)).toBe(false);
  });
});
