import DateConverter from "@remotemerge/nepali-date-converter";

export interface BsDate {
  year: number;
  month: number; // 1-12 (Baisakh = 1)
  day: number; // day of month
}

export const BS_MONTHS_EN = [
  "Baisakh", "Jestha", "Ashadh", "Shrawan", "Bhadra", "Ashwin",
  "Kartik", "Mangsir", "Poush", "Magh", "Falgun", "Chaitra",
] as const;

export const BS_MONTHS_NE = [
  "बैशाख", "जेठ", "असार", "श्रावण", "भदौ", "आश्विन",
  "कार्तिक", "मंसिर", "पौष", "माघ", "फाल्गुन", "चैत्र",
] as const;

// The library's documented AD range. ISO date strings compare correctly as plain strings.
const AD_MIN = "1918-04-13";
const AD_MAX = "2043-04-13";

// The *calendar date in the given time zone* is what gets converted: a parcel scanned at 01:45
// Kathmandu time on the 28th is the 28th in Nepal even though it is still the 27th in UTC.
function isoDateInZone(date: Date, timeZone: string): string {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone, year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(date);
  const get = (type: string) => parts.find((p) => p.type === type)!.value;
  return `${get("year")}-${get("month")}-${get("day")}`;
}

export function toBsDate(date: Date, timeZone: string): BsDate | null {
  if (Number.isNaN(date.getTime())) {
    return null;
  }
  try {
    const iso = isoDateInZone(date, timeZone);
    if (iso < AD_MIN || iso > AD_MAX) {
      return null;
    }
    const bs = new DateConverter(iso).toBs();
    return { year: bs.year, month: bs.month, day: bs.date };
  } catch {
    return null;
  }
}
