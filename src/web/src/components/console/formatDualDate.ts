import { BS_MONTHS_EN, BS_MONTHS_NE, toBsDate } from "@/lib/i18n/bsDate";

export const CONSOLE_TIME_ZONE = "Asia/Kathmandu";

const adFormatter = new Intl.DateTimeFormat("en-GB", {
  timeZone: CONSOLE_TIME_ZONE,
  weekday: "short",
  day: "numeric",
  month: "short",
  year: "numeric",
});

// Both calendars describe the same Kathmandu calendar day — a parcel scanned at 00:15 NPT
// belongs to that day even while it is still the previous day in UTC.
export function formatDualDate(date: Date, locale: string): { ad: string; bs: string | null } {
  const ad = adFormatter.format(date);
  const bsDate = toBsDate(date, CONSOLE_TIME_ZONE);
  if (!bsDate) {
    return { ad, bs: null };
  }
  const months = locale === "ne" ? BS_MONTHS_NE : BS_MONTHS_EN;
  return { ad, bs: `${months[bsDate.month - 1]} ${bsDate.day}, ${bsDate.year}` };
}
