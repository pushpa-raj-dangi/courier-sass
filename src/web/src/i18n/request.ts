import { cookies } from "next/headers";
import { getRequestConfig } from "next-intl/server";
import { DEFAULT_TIME_ZONE, LOCALE_COOKIE, resolveLocale } from "./config";

// "Without i18n routing": the locale comes from the courier_locale cookie, never the URL, so
// every existing route (and the public /track/{awb} link) is unchanged.
export default getRequestConfig(async () => {
  const store = await cookies();
  const locale = resolveLocale(store.get(LOCALE_COOKIE)?.value);
  return {
    locale,
    timeZone: DEFAULT_TIME_ZONE,
    messages: (await import(`../../messages/${locale}.json`)).default,
  };
});
