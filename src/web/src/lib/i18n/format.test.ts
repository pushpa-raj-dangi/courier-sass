import { describe, expect, it } from "vitest";
import { formatDate, formatMoney, formatNumber, toDevanagariDigits } from "./format";

const nbsp = /[  ]/g;
const kathmandu = { timeZone: "Asia/Kathmandu" } as const;

describe("formatMoney", () => {
  it("formats NPR in English with grouping and two decimals", () => {
    expect(formatMoney(1250, "NPR", "en").replace(nbsp, " ")).toBe("NPR 1,250.00");
  });

  it("formats a foreign currency without converting it", () => {
    expect(formatMoney(19.5, "USD", "en")).toBe("$19.50");
  });

  it("uses Devanagari digits for Nepali", () => {
    expect(formatMoney(1250, "NPR", "ne")).toMatch(/[०-९]/);
  });
});

describe("formatNumber", () => {
  it("groups thousands in English", () => {
    expect(formatNumber(1234567, "en")).toBe("1,234,567");
  });
});

describe("toDevanagariDigits", () => {
  it("maps 0-9 to ०-९ and leaves other characters alone", () => {
    expect(toDevanagariDigits("2080-01-15")).toBe("२०८०-०१-१५");
  });
});

describe("formatDate", () => {
  const instant = new Date("2023-04-27T20:00:00Z"); // 28 Apr 2023 in Kathmandu

  it("formats Gregorian in English using the given time zone", () => {
    expect(formatDate(instant, { locale: "en", calendar: "Gregorian", ...kathmandu })).toBe("Apr 28, 2023");
  });

  it("formats Bikram Sambat in English", () => {
    expect(formatDate(instant, { locale: "en", calendar: "BikramSambat", ...kathmandu })).toBe("15 Baisakh 2080");
  });

  it("formats Bikram Sambat in Nepali with Devanagari digits", () => {
    expect(formatDate(instant, { locale: "ne", calendar: "BikramSambat", ...kathmandu })).toBe("१५ बैशाख २०८०");
  });

  it("accepts ISO strings and epoch numbers", () => {
    const ctx = { locale: "en", calendar: "Gregorian", ...kathmandu } as const;
    expect(formatDate("2023-04-27T20:00:00Z", ctx)).toBe("Apr 28, 2023");
    expect(formatDate(instant.getTime(), ctx)).toBe("Apr 28, 2023");
  });

  it("falls back to Gregorian when the date is outside the BS range", () => {
    expect(formatDate("2050-01-01T00:00:00Z", { locale: "en", calendar: "BikramSambat", timeZone: "UTC" })).toBe(
      "Jan 1, 2050",
    );
  });

  it("returns an empty string for an invalid date", () => {
    expect(formatDate("garbage", { locale: "en", calendar: "Gregorian", timeZone: "UTC" })).toBe("");
  });

  it("appends a time when asked", () => {
    const text = formatDate(instant, { locale: "en", calendar: "Gregorian", ...kathmandu }, { withTime: true });
    expect(text.replace(nbsp, " ")).toMatch(/^Apr 28, 2023.*1:45\s?AM$/);
  });
});
