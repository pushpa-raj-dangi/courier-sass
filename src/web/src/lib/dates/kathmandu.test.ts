import { describe, expect, it } from "vitest";
import { daysBetween, kathmanduToday, relativeDayLabel } from "./kathmandu";

const now = new Date("2026-10-31T23:30:00Z"); // 2026-11-01 05:15 in Kathmandu

describe("kathmandu dates", () => {
  it("kathmanduToday uses the Kathmandu calendar, not UTC", () => {
    expect(kathmanduToday(now)).toBe("2026-11-01");
  });

  it("daysBetween counts calendar days across year ends", () => {
    expect(daysBetween("2026-12-31", "2027-01-01")).toBe(1);
    expect(daysBetween("2026-11-05", "2026-11-01")).toBe(-4);
  });

  it("relativeDayLabel", () => {
    expect(relativeDayLabel("2026-11-01", now)).toBe("Today");
    expect(relativeDayLabel("2026-11-02", now)).toBe("Tomorrow");
    expect(relativeDayLabel("2026-11-05", now)).toBe("In 4 days");
    expect(relativeDayLabel("2026-10-31", now)).toBe("Date passed");
  });
});
