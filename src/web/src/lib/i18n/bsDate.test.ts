import { describe, expect, it } from "vitest";
import { BS_MONTHS_EN, BS_MONTHS_NE, toBsDate } from "./bsDate";

describe("toBsDate", () => {
  // Vector taken verbatim from the library's README: 2023-04-28 AD = 2080-01-15 BS.
  it("converts the documented reference date", () => {
    expect(toBsDate(new Date("2023-04-28T06:00:00Z"), "UTC")).toEqual({ year: 2080, month: 1, day: 15 });
  });

  it("uses the calendar date in the given time zone, not in UTC", () => {
    const instant = new Date("2023-04-27T20:00:00Z"); // 2023-04-28 01:45 in Kathmandu (UTC+5:45)
    expect(toBsDate(instant, "Asia/Kathmandu")).toEqual({ year: 2080, month: 1, day: 15 });
    expect(toBsDate(instant, "UTC")).toEqual({ year: 2080, month: 1, day: 14 });
  });

  it("returns null outside the library's supported AD range", () => {
    expect(toBsDate(new Date("1900-01-01T00:00:00Z"), "UTC")).toBeNull();
    expect(toBsDate(new Date("2050-01-01T00:00:00Z"), "UTC")).toBeNull();
  });

  it("returns null for an invalid date instead of throwing", () => {
    expect(toBsDate(new Date("not a date"), "UTC")).toBeNull();
  });

  it("has twelve month names in each language", () => {
    expect(BS_MONTHS_EN).toHaveLength(12);
    expect(BS_MONTHS_NE).toHaveLength(12);
    expect(BS_MONTHS_EN[0]).toBe("Baisakh");
    expect(BS_MONTHS_NE[0]).toBe("बैशाख");
  });
});
