import type { CalendarPreference, Locale } from "@/i18n/config";
import { BS_MONTHS_EN, BS_MONTHS_NE, toBsDate } from "./bsDate";

export interface FormatContext {
  locale: Locale;
  calendar: CalendarPreference;
  timeZone: string;
}

const DEVANAGARI_DIGITS = ["०", "१", "२", "३", "४", "५", "६", "७", "८", "९"];

// Deterministic (does not depend on the runtime's ICU data), used for BS dates.
export function toDevanagariDigits(text: string): string {
  return text.replace(/[0-9]/g, (d) => DEVANAGARI_DIGITS[Number(d)]);
}

export function formatMoney(amount: number, currency: string, locale: Locale): string {
  return new Intl.NumberFormat(locale, { style: "currency", currency }).format(amount);
}

export function formatNumber(value: number, locale: Locale): string {
  return new Intl.NumberFormat(locale).format(value);
}

function toDate(value: Date | string | number): Date {
  return value instanceof Date ? value : new Date(value);
}

// Amounts are never converted here — formatting only. Currency conversion is FxRateService's job.
export function formatDate(value: Date | string | number, ctx: FormatContext, options: { withTime?: boolean } = {}): string {
  const date = toDate(value);
  if (Number.isNaN(date.getTime())) {
    return "";
  }

  const time = options.withTime
    ? new Intl.DateTimeFormat(ctx.locale, { timeZone: ctx.timeZone, hour: "numeric", minute: "2-digit" }).format(date)
    : null;

  if (ctx.calendar === "BikramSambat") {
    const bs = toBsDate(date, ctx.timeZone);
    if (bs) {
      const names = ctx.locale === "ne" ? BS_MONTHS_NE : BS_MONTHS_EN;
      const text = `${bs.day} ${names[bs.month - 1]} ${bs.year}`;
      const dated = ctx.locale === "ne" ? toDevanagariDigits(text) : text;
      return time ? `${dated}, ${time}` : dated;
    }
    // Outside the BS conversion range: fall through to Gregorian rather than show nothing.
  }

  return new Intl.DateTimeFormat(ctx.locale, {
    timeZone: ctx.timeZone,
    dateStyle: "medium",
    ...(options.withTime ? { timeStyle: "short" as const } : {}),
  }).format(date);
}
