import { describe, expect, it } from "vitest";
import { BS_MONTHS_EN, BS_MONTHS_NE, toBsDate } from "@/lib/i18n/bsDate";
import { CONSOLE_TIME_ZONE, formatDualDate } from "./formatDualDate";

describe("formatDualDate", () => {
  it("formats the AD date and the converter's BS date for the Kathmandu day", () => {
    const date = new Date("2026-09-30T06:00:00Z");
    const bs = toBsDate(date, CONSOLE_TIME_ZONE)!;
    const result = formatDualDate(date, "en");

    expect(result.ad).toContain("Wed");
    expect(result.ad).toContain("30 Sep 2026");
    expect(result.bs).toBe(`${BS_MONTHS_EN[bs.month - 1]} ${bs.day}, ${bs.year}`);
  });

  it("uses the Kathmandu calendar day near UTC midnight", () => {
    // 18:30 UTC on 30 Sep = 00:15 on 1 Oct in Kathmandu (UTC+5:45).
    const date = new Date("2026-09-30T18:30:00Z");
    const result = formatDualDate(date, "en");
    const bsKathmandu = toBsDate(date, CONSOLE_TIME_ZONE)!;
    const bsUtc = toBsDate(date, "UTC")!;

    expect(result.ad).toContain("1 Oct 2026");
    expect(bsKathmandu.day).not.toBe(bsUtc.day);
    expect(result.bs).toBe(`${BS_MONTHS_EN[bsKathmandu.month - 1]} ${bsKathmandu.day}, ${bsKathmandu.year}`);
  });

  it("uses Nepali month names for the ne locale", () => {
    const date = new Date("2026-09-30T06:00:00Z");
    const bs = toBsDate(date, CONSOLE_TIME_ZONE)!;
    expect(formatDualDate(date, "ne").bs).toBe(`${BS_MONTHS_NE[bs.month - 1]} ${bs.day}, ${bs.year}`);
  });

  it("returns bs null outside the converter's range", () => {
    expect(formatDualDate(new Date("2050-01-01T00:00:00Z"), "en").bs).toBeNull();
  });
});
