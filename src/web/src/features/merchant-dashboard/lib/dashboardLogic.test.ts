import { describe, expect, it } from "vitest";
import { attentionRows, firstName, greetingFor, sumCounts } from "./dashboardLogic";

describe("dashboardLogic", () => {
  it("picks greeting by local hour", () => {
    expect(greetingFor(0)).toBe("morning");
    expect(greetingFor(11)).toBe("morning");
    expect(greetingFor(12)).toBe("afternoon");
    expect(greetingFor(16)).toBe("afternoon");
    expect(greetingFor(17)).toBe("evening");
  });
  it("takes the first name", () => {
    expect(firstName("Asha Gurung")).toBe("Asha");
    expect(firstName(undefined)).toBe("");
  });
  it("sums counts, null if any is null", () => {
    expect(sumCounts([1, 2, 3])).toBe(6);
    expect(sumCounts([1, null, 3])).toBeNull();
  });
  it("builds attention rows", () => {
    expect(attentionRows(0, 0)).toEqual([]);
    expect(attentionRows(2, 0).map((r) => r.text)).toEqual(["2 failed deliveries"]);
    expect(attentionRows(null, 1).map((r) => r.text)).toEqual(["— failed deliveries", "1 returning to you"]);
  });
});
