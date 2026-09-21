"use client";

import { createContext, useContext, type ReactNode } from "react";
import { DEFAULT_CALENDAR, type CalendarPreference } from "./config";

const CalendarContext = createContext<CalendarPreference>(DEFAULT_CALENDAR);

// The calendar preference is read from its cookie on the server (root layout) and provided here,
// so server and client render the same value (no hydration mismatch).
export function PreferencesProvider({ calendar, children }: { calendar: CalendarPreference; children: ReactNode }) {
  return <CalendarContext.Provider value={calendar}>{children}</CalendarContext.Provider>;
}

export function useCalendarPreference(): CalendarPreference {
  return useContext(CalendarContext);
}
