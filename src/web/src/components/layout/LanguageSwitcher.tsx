"use client";

import { useRouter } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { Languages } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { LOCALES, LOCALE_COOKIE, isLocale, resolveLocale } from "@/i18n/config";
import { setPreferenceCookie } from "@/i18n/preferences";

export function LanguageSwitcher() {
  const t = useTranslations("header");
  const tLanguage = useTranslations("language");
  const router = useRouter();
  const current = resolveLocale(useLocale());

  function choose(value: string) {
    if (!isLocale(value) || value === current) {
      return;
    }
    setPreferenceCookie(LOCALE_COOKIE, value);
    router.refresh();
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="icon" className="rounded-full" aria-label={t("language")}>
          <Languages className="h-4 w-4" strokeWidth={2} />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-40">
        <DropdownMenuRadioGroup value={current} onValueChange={choose}>
          {LOCALES.map((locale) => (
            <DropdownMenuRadioItem key={locale} value={locale}>
              {tLanguage(locale)}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
