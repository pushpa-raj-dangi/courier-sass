import { describe, expect, it } from "vitest";
import { localIsoDay, presetDays, toUtcRange } from "./rangePresets";

describe("rangePresets", () => {
  const now = new Date(2026, 9, 5, 14, 30); // 5 Oct 2026, local time

  it("formats a local calendar day", () => {
    expect(localIsoDay(new Date(2026, 0, 9))).toBe("2026-01-09");
  });

  it("today is a single day", () => {
    expect(presetDays("today", now)).toEqual({ from: "2026-10-05", to: "2026-10-05" });
  });

  it("7d and 30d include today", () => {
    expect(presetDays("7d", now)).toEqual({ from: "2026-09-29", to: "2026-10-05" });
    expect(presetDays("30d", now)).toEqual({ from: "2026-09-06", to: "2026-10-05" });
  });

  it("anchors days to UTC bounds and omits missing ends", () => {
    expect(toUtcRange("2026-10-01", "2026-10-05")).toEqual({ from: "2026-10-01T00:00:00Z", to: "2026-10-05T23:59:59Z" });
    expect(toUtcRange("", undefined)).toEqual({ from: undefined, to: undefined });
  });
});
