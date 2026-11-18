import { CONSOLE_TIME_ZONE } from "@/components/console/formatDualDate";
import { isoDateInZone } from "@/lib/i18n/bsDate";

/** Today's calendar date ("yyyy-MM-dd") in the console time zone (Asia/Kathmandu). */
export function kathmanduToday(now: Date = new Date()): string {
  return isoDateInZone(now, CONSOLE_TIME_ZONE);
}

function utcDay(iso: string): number {
  const [y, m, d] = iso.split("-").map(Number);
  return Date.UTC(y, m - 1, d);
}

/** Whole calendar days from one "yyyy-MM-dd" date to another (negative when `to` is earlier). */
export function daysBetween(fromIsoDate: string, toIsoDate: string): number {
  return Math.round((utcDay(toIsoDate) - utcDay(fromIsoDate)) / 86_400_000);
}

export function relativeDayLabel(isoDate: string, now: Date = new Date()): string {
  const diff = daysBetween(kathmanduToday(now), isoDate);
  if (diff < 0) return "Date passed";
  if (diff === 0) return "Today";
  if (diff === 1) return "Tomorrow";
  return `In ${diff} days`;
}
