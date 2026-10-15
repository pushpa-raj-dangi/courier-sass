import { describe, expect, it } from "vitest";
import { collectionLabel, describeVariance, formatRs, sortHoldings } from "./codFormat";

describe("formatRs", () => {
  it("uses Nepali/Indian digit grouping with an Rs prefix and no decimals for whole rupees", () => {
    expect(formatRs(1248500)).toBe("Rs 12,48,500");
    expect(formatRs(0)).toBe("Rs 0");
    expect(formatRs(2350)).toBe("Rs 2,350");
  });

  it("shows two decimals whenever there are non-zero paise", () => {
    expect(formatRs(2350.5)).toBe("Rs 2,350.50");
    expect(formatRs(1248500.05)).toBe("Rs 12,48,500.05");
    expect(formatRs(-12.3)).toBe("Rs -12.30");
  });

  it("treats sub-paisa float noise as whole rupees", () => {
    expect(formatRs(0.1 + 0.2 - 0.3 + 100)).toBe("Rs 100");
    expect(formatRs(-0.001)).toBe("Rs 0");
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

describe("sortHoldings", () => {
  it("sorts longest-held first", () => {
    const items = [
      { riderId: "a", branchId: "b", parcelCount: 1, amount: 1, oldestCollectedAt: "2026-10-07T10:00:00Z", latestCollectedAt: "2026-10-07T10:00:00Z" },
      { riderId: "c", branchId: "b", parcelCount: 1, amount: 1, oldestCollectedAt: "2026-10-05T10:00:00Z", latestCollectedAt: "2026-10-05T10:00:00Z" },
    ];
    expect(sortHoldings(items).map((i) => i.riderId)).toEqual(["c", "a"]);
  });
});

describe("collectionLabel", () => {
  it("prefers the AWB and falls back to the short shipment id", () => {
    expect(collectionLabel({ awb: "KTM-000123", shipmentId: "0f8e2a6c-1111-2222-3333-444455556666" })).toBe("KTM-000123");
    expect(collectionLabel({ awb: null, shipmentId: "0f8e2a6c-1111-2222-3333-444455556666" })).toBe("0f8e2a6c");
  });
});
