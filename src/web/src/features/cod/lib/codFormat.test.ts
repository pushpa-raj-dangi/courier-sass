import { describe, expect, it } from "vitest";
import { describeVariance, formatHeldFor, formatRs, hoursHeld, sortHoldings } from "./codFormat";

const grouping = new Intl.NumberFormat("en-IN", { maximumFractionDigits: 2 });

describe("formatRs", () => {
  it("uses Nepali/Indian digit grouping with an Rs prefix", () => {
    expect(formatRs(1248500)).toBe(`Rs ${grouping.format(1248500)}`);
    expect(formatRs(1248500)).toBe("Rs 12,48,500");
    expect(formatRs(2350.5)).toBe("Rs 2,350.5");
  });
});

describe("describeVariance", () => {
  it("labels matched, short and over with text", () => {
    expect(describeVariance(0)).toEqual({ tone: "matched", label: "Matched", token: "status-delivered" });
    expect(describeVariance(-500)).toEqual({ tone: "short", label: "Short − Rs 500", token: "status-ndr" });
    expect(describeVariance(200)).toEqual({ tone: "over", label: "Over + Rs 200", token: "status-in-transit" });
  });

  it("treats sub-paisa float noise as matched", () => {
    expect(describeVariance(0.1 + 0.2 - 0.3).tone).toBe("matched");
  });
});

describe("held time", () => {
  const now = new Date("2026-10-07T12:00:00Z");
  it("computes hours and formats days", () => {
    expect(hoursHeld("2026-10-07T09:00:00Z", now)).toBe(3);
    expect(formatHeldFor("2026-10-07T11:30:00Z", now)).toBe("< 1 h");
    expect(formatHeldFor("2026-10-07T09:00:00Z", now)).toBe("3 h");
    expect(formatHeldFor("2026-10-06T10:00:00Z", now)).toBe("1 d 2 h");
    expect(formatHeldFor("2026-10-05T12:00:00Z", now)).toBe("2 d");
  });

  it("never returns negative hours for clock skew", () => {
    expect(hoursHeld("2026-10-07T13:00:00Z", now)).toBe(0);
  });

  it("sorts longest-held first", () => {
    const items = [
      { riderId: "a", branchId: "b", parcelCount: 1, amount: 1, oldestCollectedAt: "2026-10-07T10:00:00Z", latestCollectedAt: "2026-10-07T10:00:00Z" },
      { riderId: "c", branchId: "b", parcelCount: 1, amount: 1, oldestCollectedAt: "2026-10-05T10:00:00Z", latestCollectedAt: "2026-10-05T10:00:00Z" },
    ];
    expect(sortHoldings(items).map((i) => i.riderId)).toEqual(["c", "a"]);
  });
});
