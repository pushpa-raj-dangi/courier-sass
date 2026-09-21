"use client";

import { useRouter } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { Check, Languages } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { LOCALES, LOCALE_COOKIE, resolveLocale, type Locale } from "@/i18n/config";
import { setPreferenceCookie } from "@/i18n/preferences";

export function LanguageSwitcher() {
  const t = useTranslations("header");
  const tLanguage = useTranslations("language");
  const router = useRouter();
  const current = resolveLocale(useLocale());

  function choose(locale: Locale) {
    setPreferenceCookie(LOCALE_COOKIE, locale);
    router.refresh();
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          aria-label={t("language")}
          className="flex h-9 w-9 items-center justify-center rounded-lg border border-input text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
        >
          <Languages className="h-4 w-4" strokeWidth={2} />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-40">
        {LOCALES.map((locale) => (
          <DropdownMenuItem key={locale} onSelect={() => choose(locale)} className="justify-between gap-3">
            {tLanguage(locale)}
            {locale === current && <Check className="h-4 w-4" strokeWidth={2} aria-hidden="true" />}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
