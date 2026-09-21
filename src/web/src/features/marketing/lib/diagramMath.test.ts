import { describe, expect, it } from "vitest";
import { codBreakdown, formatNpr, reconcileManifest } from "./diagramMath";

describe("codBreakdown", () => {
  it("computes merchant payable as collected minus courier fee (CLAUDE.md §22 example)", () => {
    expect(codBreakdown(2500, 150)).toEqual({ collected: 2500, fee: 150, payable: 2350 });
  });
  it("allows a zero fee", () => {
    expect(codBreakdown(1000, 0).payable).toBe(1000);
  });
  it("rejects a fee larger than the collected amount", () => {
    expect(() => codBreakdown(100, 150)).toThrow(RangeError);
  });
  it("rejects negative amounts", () => {
    expect(() => codBreakdown(-1, 0)).toThrow(RangeError);
    expect(() => codBreakdown(100, -1)).toThrow(RangeError);
  });
});

describe("reconcileManifest", () => {
  it("counts scanned = expected - missing", () => {
    expect(reconcileManifest(120, [87])).toEqual({ expected: 120, scanned: 119, missing: 1, missingIndices: [87] });
  });
  it("de-duplicates, sorts, and drops out-of-range indices", () => {
    expect(reconcileManifest(10, [5, 2, 5, 99, -1]).missingIndices).toEqual([2, 5]);
  });
  it("reports zero missing for a clean manifest", () => {
    expect(reconcileManifest(10, [])).toMatchObject({ scanned: 10, missing: 0 });
  });
});

describe("formatNpr", () => {
  it("prefixes NPR and groups thousands", () => {
    expect(formatNpr(2500)).toBe("NPR 2,500");
    expect(formatNpr(150)).toBe("NPR 150");
  });
});
