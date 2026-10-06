import { describe, expect, it } from "vitest";
import { INITIAL_SCAN_SESSION, MAX_LOG, scanSessionReducer, type ScanSessionState } from "./scanSessionReducer";

const settle = (s: ScanSessionState, awb: string, ok = true) =>
  scanSessionReducer(s, { type: "settled", awb, modeId: "dispatch", ok, text: ok ? "Accepted" : "Nope", at: "2026-10-05T10:00:00Z" });

describe("scanSessionReducer", () => {
  it("runs the first request immediately", () => {
    const s = scanSessionReducer(INITIAL_SCAN_SESSION, { type: "requested", awb: "A" });
    expect(s.inFlight).toBe("A");
    expect(s.queued).toBeNull();
  });

  it("queues at most one request while busy, latest wins", () => {
    let s = scanSessionReducer(INITIAL_SCAN_SESSION, { type: "requested", awb: "A" });
    s = scanSessionReducer(s, { type: "requested", awb: "B" });
    s = scanSessionReducer(s, { type: "requested", awb: "C" });
    expect(s.inFlight).toBe("A");
    expect(s.queued).toBe("C");
  });

  it("ignores a request for the code already in flight or queued", () => {
    let s = scanSessionReducer(INITIAL_SCAN_SESSION, { type: "requested", awb: "A" });
    s = scanSessionReducer(s, { type: "requested", awb: "A" });
    expect(s.queued).toBeNull();
  });

  it("settling logs newest-first, sets last and promotes the queued code", () => {
    let s = scanSessionReducer(INITIAL_SCAN_SESSION, { type: "requested", awb: "A" });
    s = scanSessionReducer(s, { type: "requested", awb: "B" });
    s = settle(s, "A");
    expect(s.inFlight).toBe("B");
    expect(s.queued).toBeNull();
    expect(s.last).toMatchObject({ awb: "A", ok: true });
    s = settle(s, "B", false);
    expect(s.inFlight).toBeNull();
    expect(s.log.map((e) => e.awb)).toEqual(["B", "A"]);
    expect(new Set(s.log.map((e) => e.id)).size).toBe(2);
  });

  it("caps the log", () => {
    let s = INITIAL_SCAN_SESSION;
    for (let i = 0; i < MAX_LOG + 5; i++) {
      s = scanSessionReducer(s, { type: "requested", awb: `A${i}` });
      s = settle(s, `A${i}`);
    }
    expect(s.log).toHaveLength(MAX_LOG);
    expect(s.log[0].awb).toBe(`A${MAX_LOG + 4}`);
  });
});
