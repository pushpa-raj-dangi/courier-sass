import { describe, expect, it } from "vitest";
import { INITIAL_SCAN_SESSION, MAX_LOG, scanSessionReducer, type ScanRequest, type ScanSessionState } from "./scanSessionReducer";
import type { ScanMode } from "./modes";

const AT = "2026-10-05T10:00:00Z";
const dispatchMode = { id: "dispatch" } as ScanMode;
const req = (awb: string, mode: ScanMode = dispatchMode): ScanRequest => ({ awb, mode, context: { kind: "none" } });
const ask = (s: ScanSessionState, awb: string, mode?: ScanMode) => scanSessionReducer(s, { type: "requested", request: req(awb, mode), at: AT });
const settle = (s: ScanSessionState, ok = true) =>
  scanSessionReducer(s, { type: "settled", request: s.inFlight!, ok, text: ok ? "Accepted" : "Nope", at: AT });

describe("scanSessionReducer", () => {
  it("runs the first request immediately", () => {
    const s = ask(INITIAL_SCAN_SESSION, "A");
    expect(s.inFlight?.awb).toBe("A");
    expect(s.queued).toBeNull();
  });

  it("queues at most one request while busy, latest wins, and logs the displaced code", () => {
    let s = ask(INITIAL_SCAN_SESSION, "A");
    s = ask(s, "B");
    s = ask(s, "C");
    expect(s.inFlight?.awb).toBe("A");
    expect(s.queued?.awb).toBe("C");
    expect(s.log).toHaveLength(1);
    expect(s.log[0]).toMatchObject({ awb: "B", modeId: "dispatch", ok: false, text: "Skipped — scan again" });
  });

  it("ignores a request for the code already in flight or queued in the same mode", () => {
    let s = ask(INITIAL_SCAN_SESSION, "A");
    s = ask(s, "A");
    expect(s.queued).toBeNull();
    s = ask(s, "B");
    expect(ask(s, "B")).toBe(s);
  });

  it("keeps the mode and context snapshot of each request", () => {
    const receive = { id: "receive" } as ScanMode;
    let s = ask(INITIAL_SCAN_SESSION, "A");
    s = ask(s, "A", receive);
    expect(s.queued?.mode.id).toBe("receive");
  });

  it("settling logs newest-first, sets last and promotes the queued request", () => {
    let s = ask(INITIAL_SCAN_SESSION, "A");
    s = ask(s, "B");
    s = settle(s);
    expect(s.inFlight?.awb).toBe("B");
    expect(s.queued).toBeNull();
    expect(s.last).toMatchObject({ awb: "A", ok: true });
    s = settle(s, false);
    expect(s.inFlight).toBeNull();
    expect(s.log.map((e) => e.awb)).toEqual(["B", "A"]);
    expect(new Set(s.log.map((e) => e.id)).size).toBe(2);
  });

  it("ignores a settle for a request that is not in flight", () => {
    const s = ask(INITIAL_SCAN_SESSION, "A");
    expect(scanSessionReducer(s, { type: "settled", request: req("A"), ok: true, text: "x", at: AT })).toBe(s);
  });

  it("caps the log", () => {
    let s = INITIAL_SCAN_SESSION;
    for (let i = 0; i < MAX_LOG + 5; i++) {
      s = ask(s, `A${i}`);
      s = settle(s);
    }
    expect(s.log).toHaveLength(MAX_LOG);
    expect(s.log[0].awb).toBe(`A${MAX_LOG + 4}`);
  });
});
