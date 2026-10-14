"use client";

import { useEffect, useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { formatDualDate } from "./formatDualDate";

// `now` is for tests; in the app the date is read after mount so server and client HTML
// never disagree (the server may render in another time zone or on another day).
export function DualDate({ now }: { now?: Date }) {
  const locale = useLocale();
  const t = useTranslations("console");
  const [date, setDate] = useState<Date | null>(now ?? null);

  useEffect(() => {
    if (now) {
      return;
    }
    setDate(new Date());
    // Keep the date current if the console stays open across midnight.
    const id = setInterval(() => setDate(new Date()), 60_000);
    return () => clearInterval(id);
  }, [now]);

  if (!date) {
    return null;
  }
  const { ad, bs } = formatDualDate(date, locale);
  return (
    <div className="hidden text-right leading-tight lg:block">
      <div className="text-sm font-medium text-foreground">{ad}</div>
      {bs && (
        <div className="text-xs text-muted-foreground">
          {bs} {t("bsSuffix")}
        </div>
      )}
    </div>
  );
}
