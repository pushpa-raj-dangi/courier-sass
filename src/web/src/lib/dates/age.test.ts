import { describe, expect, it } from "vitest";
import { AGE_WARNING_HOURS, ageHours, formatAge } from "./age";

describe("age", () => {
  const now = new Date("2026-10-07T12:00:00Z");

  it("computes hours and formats hours and days", () => {
    expect(ageHours("2026-10-07T09:00:00Z", now)).toBe(3);
    expect(formatAge("2026-10-07T11:30:00Z", now)).toBe("< 1 h");
    expect(formatAge("2026-10-07T05:00:00Z", now)).toBe("7 h");
    expect(formatAge("2026-10-05T16:00:00Z", now)).toBe("1 d 20 h");
    expect(formatAge("2026-10-05T12:00:00Z", now)).toBe("2 d");
  });

  it("never returns negative hours for clock skew", () => {
    expect(ageHours("2026-10-07T13:00:00Z", now)).toBe(0);
    expect(formatAge("2026-10-07T13:00:00Z", now)).toBe("< 1 h");
  });

  it("warns from 24 hours", () => {
    expect(AGE_WARNING_HOURS).toBe(24);
  });
});
