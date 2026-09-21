export const LOCALES = ["en", "ne"] as const;
export type Locale = (typeof LOCALES)[number];
export const DEFAULT_LOCALE: Locale = "en";

export const CALENDARS = ["Gregorian", "BikramSambat"] as const;
export type CalendarPreference = (typeof CALENDARS)[number];
export const DEFAULT_CALENDAR: CalendarPreference = "Gregorian";

export const DEFAULT_TIME_ZONE = "Asia/Kathmandu";

export const LOCALE_COOKIE = "courier_locale";
export const CALENDAR_COOKIE = "courier_calendar";

export function isLocale(value: unknown): value is Locale {
  return typeof value === "string" && (LOCALES as readonly string[]).includes(value);
}

export function resolveLocale(value: string | null | undefined): Locale {
  return isLocale(value) ? value : DEFAULT_LOCALE;
}

export function isCalendar(value: unknown): value is CalendarPreference {
  return typeof value === "string" && (CALENDARS as readonly string[]).includes(value);
}

export function resolveCalendar(value: string | null | undefined): CalendarPreference {
  return isCalendar(value) ? value : DEFAULT_CALENDAR;
}
