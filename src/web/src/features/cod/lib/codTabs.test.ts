import { describe, expect, it } from "vitest";
import { COD_TABS, DEFAULT_COD_TAB, parseCodTab, parseRangePreset } from "./codTabs";

describe("codTabs", () => {
  it("lists the six tabs in order", () => {
    expect(COD_TABS.map((t) => t.key)).toEqual(["collections", "rider-deposits", "branch-settlements", "merchant-settlements", "adjustments", "ledger"]);
  });
  it("falls back to rider deposits for unknown tabs", () => {
    expect(parseCodTab("ledger")).toBe("ledger");
    expect(parseCodTab("reconciliation")).toBe(DEFAULT_COD_TAB);
    expect(parseCodTab(null)).toBe("rider-deposits");
  });
  it("defaults the range to 7 days", () => {
    expect(parseRangePreset("30d")).toBe("30d");
    expect(parseRangePreset("90d")).toBe("7d");
    expect(parseRangePreset(null)).toBe("7d");
  });
});
