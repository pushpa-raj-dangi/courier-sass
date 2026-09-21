import { describe, expect, it } from "vitest";
import {
  CALENDARS,
  DEFAULT_CALENDAR,
  DEFAULT_LOCALE,
  LOCALES,
  isCalendar,
  isLocale,
  resolveCalendar,
  resolveLocale,
} from "./config";

describe("i18n config", () => {
  it("supports exactly en and ne, defaulting to en", () => {
    expect([...LOCALES]).toEqual(["en", "ne"]);
    expect(DEFAULT_LOCALE).toBe("en");
  });

  it("supports exactly Gregorian and BikramSambat, defaulting to Gregorian", () => {
    expect([...CALENDARS]).toEqual(["Gregorian", "BikramSambat"]);
    expect(DEFAULT_CALENDAR).toBe("Gregorian");
  });

  it.each([["en", true], ["ne", true], ["EN", false], ["fr", false], ["", false], [undefined, false], [null, false]])(
    "isLocale(%s) is %s",
    (value, expected) => expect(isLocale(value)).toBe(expected),
  );

  it.each([["ne", "ne"], ["en", "en"], ["fr", "en"], ["NE", "en"], [undefined, "en"], [null, "en"], ["", "en"]])(
    "resolveLocale(%s) -> %s",
    (value, expected) => expect(resolveLocale(value)).toBe(expected),
  );

  it.each([["BikramSambat", true], ["Gregorian", true], ["bikramsambat", false], ["Julian", false], [undefined, false]])(
    "isCalendar(%s) is %s",
    (value, expected) => expect(isCalendar(value)).toBe(expected),
  );

  it.each([["BikramSambat", "BikramSambat"], ["Julian", "Gregorian"], [undefined, "Gregorian"], [null, "Gregorian"]])(
    "resolveCalendar(%s) -> %s",
    (value, expected) => expect(resolveCalendar(value)).toBe(expected),
  );
});
