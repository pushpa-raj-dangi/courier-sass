"use client";

import { useMemo } from "react";
import { useLocale } from "next-intl";
import { DEFAULT_TIME_ZONE, resolveLocale } from "@/i18n/config";
import { useCalendarPreference } from "@/i18n/PreferencesProvider";
import { useSession } from "@/features/auth/hooks/useSession";
import { formatDate, formatMoney, formatNumber, type FormatContext } from "./format";

export function useFormatters() {
  const locale = resolveLocale(useLocale());
  const calendar = useCalendarPreference();
  const { data: session } = useSession();
  // Falls back to Kathmandu until the session loads (and for pages with no session at all).
  const timeZone = session?.locale?.timeZoneId ?? DEFAULT_TIME_ZONE;
  const ctx: FormatContext = useMemo(() => ({ locale, calendar, timeZone }), [locale, calendar, timeZone]);

  return useMemo(
    () => ({
      formatMoney: (amount: number, currency: string) => formatMoney(amount, currency, locale),
      formatNumber: (value: number) => formatNumber(value, locale),
      formatDate: (value: Date | string | number) => formatDate(value, ctx),
      formatDateTime: (value: Date | string | number) => formatDate(value, ctx, { withTime: true }),
    }),
    [locale, ctx],
  );
}
