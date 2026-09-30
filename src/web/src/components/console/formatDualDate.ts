import { BS_MONTHS_EN, BS_MONTHS_NE, toBsDate } from "@/lib/i18n/bsDate";

export const CONSOLE_TIME_ZONE = "Asia/Kathmandu";

const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"] as const;
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"] as const;

const numericFormatter = new Intl.DateTimeFormat("en-US", {
  timeZone: CONSOLE_TIME_ZONE,
  year: "numeric",
  month: "numeric",
  day: "numeric",
});

// Both calendars describe the same Kathmandu calendar day — a parcel scanned at 00:15 NPT
// belongs to that day even while it is still the previous day in UTC.
export function formatDualDate(date: Date, locale: string): { ad: string; bs: string | null } {
  // Extract numeric date components (ICU-stable) from Kathmandu timezone
  const parts = numericFormatter.formatToParts(date);
  let year = 0;
  let month = 0;
  let day = 0;

  for (const part of parts) {
    if (part.type === "year") {
      year = parseInt(part.value, 10);
    } else if (part.type === "month") {
      month = parseInt(part.value, 10);
    } else if (part.type === "day") {
      day = parseInt(part.value, 10);
    }
  }

  // Compute weekday from Kathmandu date via UTC (year/month/day are stable, weekday is not locale-dependent)
  const weekdayIndex = new Date(Date.UTC(year, month - 1, day)).getUTCDay();
  const weekday = WEEKDAYS[weekdayIndex];

  const ad = `${weekday}, ${day} ${MONTHS[month - 1]} ${year}`;

  const bsDate = toBsDate(date, CONSOLE_TIME_ZONE);
  if (!bsDate) {
    return { ad, bs: null };
  }
  const months = locale === "ne" ? BS_MONTHS_NE : BS_MONTHS_EN;
  return { ad, bs: `${months[bsDate.month - 1]} ${bsDate.day}, ${bsDate.year}` };
}
