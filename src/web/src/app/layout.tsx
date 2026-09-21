import type { Metadata } from "next";
import { IBM_Plex_Sans, IBM_Plex_Mono, Space_Grotesk, Noto_Sans_Devanagari } from "next/font/google";
import { cookies } from "next/headers";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages } from "next-intl/server";
import "./globals.css";
import { Providers } from "./providers";
import { PreferencesProvider } from "@/i18n/PreferencesProvider";
import { CALENDAR_COOKIE, resolveCalendar } from "@/i18n/config";

const plexSans = IBM_Plex_Sans({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  variable: "--font-plex-sans",
});
const plexMono = IBM_Plex_Mono({
  subsets: ["latin"],
  weight: ["400", "500", "600"],
  variable: "--font-plex-mono",
});
const spaceGrotesk = Space_Grotesk({
  subsets: ["latin"],
  weight: ["500", "600", "700"],
  variable: "--font-space-grotesk",
});
// IBM Plex Sans has no Devanagari glyphs; without this, Nepali text falls back to an arbitrary
// system font. Added as a fallback in tailwind.config.ts's sans stack.
const notoDevanagari = Noto_Sans_Devanagari({
  subsets: ["devanagari"],
  weight: ["400", "500", "600", "700"],
  variable: "--font-devanagari",
});

export const metadata: Metadata = {
  title: "Courier Platform",
  description: "Nepal domestic courier operations platform",
};

export default async function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  const locale = await getLocale();
  const messages = await getMessages();
  const calendar = resolveCalendar((await cookies()).get(CALENDAR_COOKIE)?.value);
  return (
    <html lang={locale} suppressHydrationWarning>
      <body
        className={`${plexSans.variable} ${plexMono.variable} ${spaceGrotesk.variable} ${notoDevanagari.variable} antialiased`}
      >
        <NextIntlClientProvider locale={locale} messages={messages}>
          <PreferencesProvider calendar={calendar}>
            <Providers>{children}</Providers>
          </PreferencesProvider>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
