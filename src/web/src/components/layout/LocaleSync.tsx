"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useSession } from "@/features/auth/hooks/useSession";
import { CALENDAR_COOKIE, LOCALE_COOKIE, isCalendar, isLocale } from "@/i18n/config";
import { readCookie, setPreferenceCookie } from "@/i18n/preferences";

// A user's own choice (the cookie) always wins. Only when a cookie is absent do we seed it from
// the organization's default, delivered on /auth/me for every staff user. Unsupported values are
// ignored rather than written, so a bad backend value can never break rendering.
export function LocaleSync() {
  const { data: session } = useSession();
  const router = useRouter();

  useEffect(() => {
    const tenant = session?.locale;
    if (!tenant) {
      return;
    }
    let changed = false;
    if (readCookie(LOCALE_COOKIE) === null && isLocale(tenant.defaultLocale)) {
      setPreferenceCookie(LOCALE_COOKIE, tenant.defaultLocale);
      changed = true;
    }
    if (readCookie(CALENDAR_COOKIE) === null && isCalendar(tenant.calendarPreference)) {
      setPreferenceCookie(CALENDAR_COOKIE, tenant.calendarPreference);
      changed = true;
    }
    if (changed) {
      router.refresh();
    }
  }, [session, router]);

  return null;
}
