import type { ReactElement, ReactNode } from "react";
import { render } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import en from "../../messages/en.json";
import ne from "../../messages/ne.json";

const catalogs = { en, ne } as const;
type TestLocale = keyof typeof catalogs;

export function IntlTestProvider({ locale = "en", children }: { locale?: TestLocale; children: ReactNode }) {
  return (
    <NextIntlClientProvider locale={locale} messages={catalogs[locale]}>
      {children}
    </NextIntlClientProvider>
  );
}

export function renderWithIntl(ui: ReactElement, { locale = "en" }: { locale?: TestLocale } = {}) {
  return render(<IntlTestProvider locale={locale}>{ui}</IntlTestProvider>);
}
