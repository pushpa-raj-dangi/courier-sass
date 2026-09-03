import { describe, expect, it } from "vitest";
import { toSafeQuoteAmount } from "./toSafeQuoteAmount";

describe("toSafeQuoteAmount", () => {
  it("returns 0 for NaN (an empty valueAsNumber-registered field)", () => {
    expect(toSafeQuoteAmount(NaN)).toBe(0);
  });

  it("returns 0 for null", () => {
    expect(toSafeQuoteAmount(null)).toBe(0);
  });

  it("returns 0 for undefined", () => {
    expect(toSafeQuoteAmount(undefined)).toBe(0);
  });

  it("returns the value unchanged for a real number", () => {
    expect(toSafeQuoteAmount(2500)).toBe(2500);
  });

  it("returns 0 unchanged (a real, meaningful zero)", () => {
    expect(toSafeQuoteAmount(0)).toBe(0);
  });

  // The regression this whole helper exists for: JSON.stringify serializes NaN as the literal
  // `null`, which is exactly what crashed PriceQuoteRequest.CodAmount server-side.
  it("never produces a value that JSON.stringify turns into null", () => {
    expect(JSON.parse(JSON.stringify({ codAmount: toSafeQuoteAmount(NaN) })).codAmount).toBe(0);
  });
});
