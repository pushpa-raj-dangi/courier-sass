# International I1a — i18n Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the Next.js app English/Nepali UI support (cookie-based locale, no route prefixes), locale-aware money/number/date formatting with an optional Bikram Sambat calendar, a language switcher, tenant-default locale delivered through `/auth/me`, and the tenant locale fields in the Settings → Profile form; translate the staff shell and login as the first consumers.

**Architecture:** `next-intl` (App Router, "without i18n routing") reads the locale from a `courier_locale` cookie in `src/i18n/request.ts`; the calendar preference lives in a `courier_calendar` cookie surfaced to client code through a small context. Formatting helpers live in `src/lib/i18n` and are pure and unit-tested. The backend `GET /api/v1/auth/me` gains a `locale` block (tenant defaults) so a first-time user with no cookie gets their organization's default. Feature strings are extracted feature by feature; missing Nepali keys are prevented by a catalog-parity test.

**Tech Stack:** Next.js 14.2 (App Router), React 18, next-intl 4 (peer range supports Next 12–16), `@remotemerge/nepali-date-converter` (BS ↔ AD), Vitest + Testing Library, ASP.NET Core / xUnit for the one backend task.

**Spec:** `docs/superpowers/specs/2026-09-21-international-support-design.md` (§7 Localization, §8 step I1). **Depends on:** I0 (merged): `OrganizationSettings.DefaultLocale` / `CalendarPreference` / `TimeZoneId` exist.

## Scope rulings (made while planning)

- **Split I1:** this plan is **I1a** (frontend foundation, formatting, preferences, shell + login translation, settings UI, `/auth/me` locale block). **I1b** (notification-template `Locale` dimension, backend) is independent and gets its own plan.
- **Public tracking localization and its language switcher stay in I7**, as the spec assigns. Only the *login* page (pre-auth) gets a switcher here.
- **Tenant default via `/auth/me`, not `/settings/organization`:** the latter requires `org.settings.manage`, which most staff lack.
- **Locale preference is a cookie**, not a route prefix: routes are auth-gated and `/track/{awb}` must not change.
- **Not in I1a:** translating the remaining ~50 feature files, the command-palette "quick action" labels, merchant/rider/platform tracks, and adopting the new formatters at the ~54 existing ad-hoc formatting sites. Untranslated keys fall back to English by construction (only translated strings go through `t()`).
- **Nepali strings are machine-drafted and need review by a native speaker before release.**

## Global Constraints

- Locales: exactly `en` and `ne` (backend `OrganizationLocaleOptions.SupportedLocales`). Calendars: exactly `Gregorian` and `BikramSambat`. Default time zone `Asia/Kathmandu`.
- Cookie names: `courier_locale`, `courier_calendar` (non-HttpOnly, `Path=/`, `SameSite=Lax`, one year). They hold no sensitive data.
- Always use shadcn/ui components (Select, DropdownMenu, …), never raw HTML form primitives. Detail/create views stay Sheets (no new routed pages).
- No new library beyond `next-intl` and `@remotemerge/nepali-date-converter`.
- BS conversion range: AD 1918-04-13 … 2043-04-13 (library limit). Outside it, formatting **must fall back to Gregorian**, never throw.
- The API stays UTC/ISO; BS conversion is client-side only.
- The `en` catalog text of every translated string must equal the string it replaces, so existing English assertions keep passing.
- Frontend commands run from `src/web`. Commit messages end with `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- Environment note: the shared Postgres/Redis containers (`courier-postgres-1`, `courier-redis-1`) must be running for the backend task's DB-backed tests.

## File Structure

| File | Responsibility |
|---|---|
| `src/Modules/Identity/Application/OrganizationSettingsDtos.cs` (mod), `OrganizationSettingsService.cs` (mod), `AuthDtos.cs` (mod), `AuthService.cs` (mod) | Expose tenant locale defaults on `/auth/me` |
| `src/web/messages/en.json`, `ne.json` | Message catalogs |
| `src/web/src/i18n/config.ts` | Locale/calendar constants and resolvers (pure) |
| `src/web/src/i18n/request.ts` | next-intl server config (cookie → locale + messages) |
| `src/web/src/i18n/PreferencesProvider.tsx` | Client context carrying the calendar preference |
| `src/web/src/i18n/preferences.ts` | Client cookie helpers |
| `src/web/src/lib/i18n/bsDate.ts`, `format.ts`, `useFormatters.ts` | BS conversion, pure formatters, client hook |
| `src/web/src/components/layout/LanguageSwitcher.tsx`, `LocaleSync.tsx` | Switcher UI; tenant-default sync |
| `src/web/src/test/renderWithIntl.tsx` | Test helper |
| `src/web/next.config.mjs`, `vitest.config.ts`, `tailwind.config.ts`, `src/app/layout.tsx` | Wiring, fonts |
| Modified for translation | `src/lib/navigation/navItems.ts`, `components/nav-main.tsx`, `components/app-sidebar.tsx`, `components/layout/{UserMenu,HeaderSearch,AppShell,CommandPalette}.tsx`, `app/login/page.tsx`, `features/auth/{LoginForm,schema}.ts(x)` |
| Settings UI | `features/admin/settings/{types,schema}.ts`, `components/OrganizationProfileForm.tsx` |

---

### Task 1: Tenant locale on `/auth/me` (backend)

**Files:**
- Modify: `src/Modules/Identity/Application/OrganizationSettingsDtos.cs`
- Modify: `src/Modules/Identity/Application/OrganizationSettingsService.cs`
- Modify: `src/Modules/Identity/Application/AuthDtos.cs:9`
- Modify: `src/Modules/Identity/Application/AuthService.cs` (`GetMeAsync`, ~line 401)
- Test: `tests/Identity.Tests/OrganizationSettingsServiceTests.cs`
- Test: `tests/Api.Tests/AuthEndpointsTests.cs` (the existing `/me` test near line 77)

**Interfaces:**
- Consumes: `OrganizationSettingsService` (already injected into `AuthService` as `_organizationSettingsService`), `OrganizationSettings.DefaultLocale/CalendarPreference/TimeZoneId` (I0).
- Produces: `record OrganizationLocaleDto(string DefaultLocale, string CalendarPreference, string TimeZoneId)`; `OrganizationSettingsService.GetLocaleAsync(Guid organizationId, CancellationToken)`; `MeResponse` gains a trailing `OrganizationLocaleDto? Locale = null`, serialized as `locale: { defaultLocale, calendarPreference, timeZoneId }`.

- [ ] **Step 1: Write the failing service test**

Add to `OrganizationSettingsServiceTests` (reuses `NewService`, `NewOrganization`, `TestDbContextFactory`, `NullTenantProvider` already in that file):

```csharp
    [Fact]
    public async Task GetLocaleAsync_NewRow_ReturnsDefaults()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();

        var locale = await NewService(dbContext).GetLocaleAsync(organization.Id);

        Assert.Equal(new OrganizationLocaleDto("en", "Gregorian", "Asia/Kathmandu"), locale);
    }

    [Fact]
    public async Task GetLocaleAsync_ReturnsSavedValues()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var service = NewService(dbContext);
        await service.UpdateProfileAsync(organization.Id, Guid.NewGuid(),
            new OrganizationProfileDto(null, null, null, null, "Asia/Kathmandu", null, "ne", "BikramSambat"));

        var locale = await service.GetLocaleAsync(organization.Id);

        Assert.Equal(new OrganizationLocaleDto("ne", "BikramSambat", "Asia/Kathmandu"), locale);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run (from repo root): `dotnet test tests/Identity.Tests --filter "FullyQualifiedName~GetLocaleAsync"`
Expected: build FAIL — `OrganizationLocaleDto` / `GetLocaleAsync` do not exist.

- [ ] **Step 3: Implement**

`OrganizationSettingsDtos.cs` — add after `OrganizationSettingsDto`:

```csharp
public sealed record OrganizationLocaleDto(string DefaultLocale, string CalendarPreference, string TimeZoneId);
```

`OrganizationSettingsService.cs` — add next to `GetSecurityPolicyAsync`:

```csharp
    // Guid-keyed and permission-free on purpose: every signed-in staff user needs their
    // organization's locale defaults (via /auth/me), not only those allowed to manage settings.
    public async Task<OrganizationLocaleDto> GetLocaleAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateAsync(organizationId, cancellationToken);
        return new OrganizationLocaleDto(settings.DefaultLocale, settings.CalendarPreference, settings.TimeZoneId);
    }
```

`AuthDtos.cs` line 9 — append the optional trailing parameter:

```csharp
public record MeResponse(Guid UserId, Guid OrganizationId, string OrganizationName, string Email, string FullName, IReadOnlyList<string> Permissions, bool HasAllBranchesScope, IReadOnlyList<MeBranchGrant> Branches, OrganizationLocaleDto? Locale = null);
```

`AuthService.GetMeAsync` — before the `return`, and change the return:

```csharp
        var locale = await _organizationSettingsService.GetLocaleAsync(user.OrganizationId!.Value, cancellationToken);

        return new MeResponse(user.Id, user.OrganizationId!.Value, organizationName, user.Email, user.FullName, permissions, hasAllBranchesScope, branches, locale);
```

- [ ] **Step 4: Extend the API test**

In `tests/Api.Tests/AuthEndpointsTests.cs`, in the existing test that reads `MeResponse` (`var me = await response.Content.ReadFromJsonAsync<MeResponse>();`, ~line 77), add after the existing assertions:

```csharp
        Assert.NotNull(me!.Locale);
        Assert.Equal("en", me.Locale!.DefaultLocale);
        Assert.Equal("Gregorian", me.Locale.CalendarPreference);
        Assert.Equal("Asia/Kathmandu", me.Locale.TimeZoneId);
```

(`me` is already asserted non-null earlier; if the file uses `me!` everywhere, match that style.)

- [ ] **Step 5: Run tests**

Run: `dotnet build src/Api` then
`dotnet test tests/Identity.Tests --filter "FullyQualifiedName~OrganizationSettingsServiceTests"` and
`dotnet test tests/Api.Tests --filter "FullyQualifiedName~AuthEndpointsTests"`
Expected: PASS (Postgres must be up on 5434). Also run `dotnet test tests/Identity.Tests` fully to confirm no regression from the `AuthService` change.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Identity tests/Identity.Tests tests/Api.Tests/AuthEndpointsTests.cs
git commit -m "$(cat <<'EOF'
feat(identity): return tenant locale defaults from /auth/me

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: next-intl foundation, catalogs, provider wiring

**Files:**
- Modify: `src/web/package.json` (+ lockfile), `src/web/next.config.mjs`, `src/web/vitest.config.ts`, `src/web/tailwind.config.ts`, `src/web/src/app/layout.tsx`
- Create: `src/web/src/i18n/config.ts`, `src/i18n/request.ts`, `src/i18n/PreferencesProvider.tsx`, `src/i18n/preferences.ts`
- Create: `src/web/messages/en.json`, `src/web/messages/ne.json`
- Create: `src/web/src/test/renderWithIntl.tsx`
- Test: `src/web/src/i18n/config.test.ts`, `src/web/src/i18n/messages.test.ts`, `src/web/src/i18n/preferences.test.ts`

**Interfaces:**
- Consumes: nothing from Task 1 (independent).
- Produces (later tasks rely on these exact names):
  - `@/i18n/config`: `LOCALES`, `Locale`, `DEFAULT_LOCALE`, `CALENDARS`, `CalendarPreference`, `DEFAULT_CALENDAR`, `DEFAULT_TIME_ZONE`, `LOCALE_COOKIE`, `CALENDAR_COOKIE`, `isLocale`, `resolveLocale(v)`, `isCalendar`, `resolveCalendar(v)`.
  - `@/i18n/PreferencesProvider`: `PreferencesProvider({calendar, children})`, `useCalendarPreference(): CalendarPreference`.
  - `@/i18n/preferences`: `setPreferenceCookie(name, value)`, `readCookie(name): string | null`.
  - `@/test/renderWithIntl`: `renderWithIntl(ui, {locale?})` and `IntlTestProvider({locale?, children})`.
  - Message namespaces used later: `common`, `nav`, `header`, `userMenu`, `language`, `auth.login`, `auth.showcase`, `auth.validation`, `settings.profile`.

- [ ] **Step 1: Install and confirm compatibility**

Run (in `src/web`): `npm install next-intl@^4 @remotemerge/nepali-date-converter`
Then `npm ls next next-intl react` — expected: `next-intl` resolves against `next@14.2.35` with no peer-dependency error (its peer range is `next ^12 || … || ^14 || ^15 || ^16`). If npm reports an unresolvable peer conflict, stop and report BLOCKED with the output.

- [ ] **Step 2: Write the failing config tests**

`src/web/src/i18n/config.test.ts`:

```ts
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
```

`src/web/src/i18n/messages.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import en from "../../messages/en.json";
import ne from "../../messages/ne.json";

function flatten(obj: Record<string, unknown>, prefix = ""): Record<string, string> {
  return Object.entries(obj).reduce<Record<string, string>>((acc, [key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    if (typeof value === "string") {
      acc[path] = value;
    } else {
      Object.assign(acc, flatten(value as Record<string, unknown>, path));
    }
    return acc;
  }, {});
}

describe("message catalogs", () => {
  const enFlat = flatten(en);
  const neFlat = flatten(ne);

  it("have identical key sets, so no key can be missing in Nepali", () => {
    expect(Object.keys(neFlat).sort()).toEqual(Object.keys(enFlat).sort());
  });

  it("have no empty strings", () => {
    for (const [key, value] of [...Object.entries(enFlat), ...Object.entries(neFlat)]) {
      expect(value.trim(), key).not.toBe("");
    }
  });

  it("use the same ICU placeholders in both languages", () => {
    const placeholders = (s: string) => (s.match(/\{[^}]+\}/g) ?? []).sort();
    for (const key of Object.keys(enFlat)) {
      expect(placeholders(neFlat[key]), key).toEqual(placeholders(enFlat[key]));
    }
  });
});
```

`src/web/src/i18n/preferences.test.ts`:

```ts
import { beforeEach, describe, expect, it } from "vitest";
import { readCookie, setPreferenceCookie } from "./preferences";

describe("preference cookies", () => {
  beforeEach(() => {
    document.cookie = "courier_locale=; Path=/; Max-Age=0";
  });

  it("returns null when the cookie is absent", () => {
    expect(readCookie("courier_locale")).toBeNull();
  });

  it("round-trips a value", () => {
    setPreferenceCookie("courier_locale", "ne");
    expect(readCookie("courier_locale")).toBe("ne");
  });

  it("does not confuse cookies with a shared name prefix", () => {
    setPreferenceCookie("courier_locale_extra", "x");
    expect(readCookie("courier_locale")).toBeNull();
    document.cookie = "courier_locale_extra=; Path=/; Max-Age=0";
  });
});
```

- [ ] **Step 3: Run to verify they fail**

Run (in `src/web`): `npx vitest run src/i18n`
Expected: FAIL — modules/catalog files don't exist.

- [ ] **Step 4: Implement config, cookies, provider**

`src/web/src/i18n/config.ts`:

```ts
export const LOCALES = ["en", "ne"] as const;
export type Locale = (typeof LOCALES)[number];
export const DEFAULT_LOCALE: Locale = "en";

export const CALENDARS = ["Gregorian", "BikramSambat"] as const;
export type CalendarPreference = (typeof CALENDARS)[number];
export const DEFAULT_CALENDAR: CalendarPreference = "Gregorian";

export const DEFAULT_TIME_ZONE = "Asia/Kathmandu";

export const LOCALE_COOKIE = "courier_locale";
export const CALENDAR_COOKIE = "courier_calendar";

export function isLocale(value: unknown): value is Locale {
  return typeof value === "string" && (LOCALES as readonly string[]).includes(value);
}

export function resolveLocale(value: string | null | undefined): Locale {
  return isLocale(value) ? value : DEFAULT_LOCALE;
}

export function isCalendar(value: unknown): value is CalendarPreference {
  return typeof value === "string" && (CALENDARS as readonly string[]).includes(value);
}

export function resolveCalendar(value: string | null | undefined): CalendarPreference {
  return isCalendar(value) ? value : DEFAULT_CALENDAR;
}
```

`src/web/src/i18n/preferences.ts`:

```ts
// Non-sensitive UI preferences, so a plain (non-HttpOnly) cookie is correct: client code sets it
// and the server reads it on the next request (src/i18n/request.ts, src/app/layout.tsx).
const ONE_YEAR_SECONDS = 60 * 60 * 24 * 365;

export function setPreferenceCookie(name: string, value: string): void {
  document.cookie = `${name}=${encodeURIComponent(value)}; Path=/; Max-Age=${ONE_YEAR_SECONDS}; SameSite=Lax`;
}

export function readCookie(name: string): string | null {
  const prefix = `${name}=`;
  for (const part of document.cookie.split("; ")) {
    if (part.startsWith(prefix)) {
      return decodeURIComponent(part.slice(prefix.length));
    }
  }
  return null;
}
```

`src/web/src/i18n/PreferencesProvider.tsx`:

```tsx
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
```

- [ ] **Step 5: Create the catalogs**

`src/web/messages/en.json`:

```json
{
  "common": {
    "appName": "Courier Platform"
  },
  "nav": {
    "group": "Platform",
    "dashboard": "Dashboard",
    "shipments": "Shipments",
    "branches": "Branches",
    "manifests": "Manifests",
    "ndrs": "NDRs",
    "returnShipments": "Return Shipments",
    "riders": "Riders",
    "liveMap": "Live Map",
    "myDeliveries": "My Deliveries",
    "cod": "COD",
    "billing": "Billing",
    "auditLogs": "Audit Logs",
    "admin": "Admin"
  },
  "header": {
    "searchPlaceholder": "Search or type a command...",
    "language": "Language"
  },
  "userMenu": {
    "logOut": "Log out"
  },
  "language": {
    "en": "English",
    "ne": "नेपाली"
  },
  "auth": {
    "login": {
      "title": "Sign in",
      "description": "Manage branches, shipments, and deliveries.",
      "email": "Email",
      "password": "Password",
      "showPassword": "Show password",
      "hidePassword": "Hide password",
      "submit": "Sign in",
      "submitting": "Signing in...",
      "failed": "Login failed.",
      "newHere": "New here?",
      "createOrganization": "Create an organization"
    },
    "showcase": {
      "headline": "Every parcel, accounted for.",
      "description": "Scan-driven custody, hub transfers, delivery attempts and COD settlement — one system that always knows where a parcel is and who has it.",
      "booked": "Booked",
      "bookedCaption": "Received at the origin branch counter.",
      "inTransit": "In transit",
      "inTransitCaption": "Scanned through every hub transfer.",
      "outForDelivery": "Out for delivery",
      "outForDeliveryCaption": "Assigned to a rider, attempt logged.",
      "delivered": "Delivered",
      "deliveredCaption": "Proof of delivery, COD reconciled."
    },
    "validation": {
      "emailInvalid": "Enter a valid email address.",
      "passwordRequired": "Password is required."
    }
  },
  "settings": {
    "profile": {
      "locale": "Language",
      "calendar": "Calendar",
      "calendarGregorian": "Gregorian (AD)",
      "calendarBikramSambat": "Bikram Sambat (BS)"
    }
  }
}
```

`src/web/messages/ne.json` (machine-drafted — must be reviewed by a native speaker before release):

```json
{
  "common": {
    "appName": "कुरियर प्लेटफर्म"
  },
  "nav": {
    "group": "प्लेटफर्म",
    "dashboard": "ड्यासबोर्ड",
    "shipments": "ढुवानीहरू",
    "branches": "शाखाहरू",
    "manifests": "म्यानिफेस्टहरू",
    "ndrs": "NDR",
    "returnShipments": "फिर्ता ढुवानी",
    "riders": "राइडरहरू",
    "liveMap": "लाइभ नक्सा",
    "myDeliveries": "मेरो डेलिभरी",
    "cod": "COD",
    "billing": "बिलिङ",
    "auditLogs": "अडिट लग",
    "admin": "प्रशासन"
  },
  "header": {
    "searchPlaceholder": "खोज्नुहोस् वा आदेश टाइप गर्नुहोस्...",
    "language": "भाषा"
  },
  "userMenu": {
    "logOut": "लग आउट"
  },
  "language": {
    "en": "English",
    "ne": "नेपाली"
  },
  "auth": {
    "login": {
      "title": "साइन इन",
      "description": "शाखा, ढुवानी र डेलिभरी व्यवस्थापन गर्नुहोस्।",
      "email": "इमेल",
      "password": "पासवर्ड",
      "showPassword": "पासवर्ड देखाउनुहोस्",
      "hidePassword": "पासवर्ड लुकाउनुहोस्",
      "submit": "साइन इन",
      "submitting": "साइन इन हुँदैछ...",
      "failed": "लगइन असफल भयो।",
      "newHere": "नयाँ हुनुहुन्छ?",
      "createOrganization": "संस्था सिर्जना गर्नुहोस्"
    },
    "showcase": {
      "headline": "हरेक पार्सल, हिसाबसहित।",
      "description": "स्क्यानमा आधारित कस्टडी, हब ट्रान्सफर, डेलिभरी प्रयास र COD सेटलमेन्ट — पार्सल कहाँ छ र कससँग छ भन्ने सधैं थाहा हुने एउटै प्रणाली।",
      "booked": "बुक भयो",
      "bookedCaption": "उत्पत्ति शाखाको काउन्टरमा प्राप्त भयो।",
      "inTransit": "ढुवानीमा",
      "inTransitCaption": "हरेक हब ट्रान्सफरमा स्क्यान गरियो।",
      "outForDelivery": "डेलिभरीका लागि निस्कियो",
      "outForDeliveryCaption": "राइडरलाई खटाइयो, प्रयास दर्ता गरियो।",
      "delivered": "डेलिभर भयो",
      "deliveredCaption": "डेलिभरीको प्रमाण, COD मिलान भयो।"
    },
    "validation": {
      "emailInvalid": "मान्य इमेल ठेगाना लेख्नुहोस्।",
      "passwordRequired": "पासवर्ड आवश्यक छ।"
    }
  },
  "settings": {
    "profile": {
      "locale": "भाषा",
      "calendar": "पात्रो",
      "calendarGregorian": "ग्रेगोरियन (ई.सं.)",
      "calendarBikramSambat": "विक्रम सम्वत् (वि.सं.)"
    }
  }
}
```

- [ ] **Step 6: Server config, plugin, layout, fonts**

`src/web/src/i18n/request.ts`:

```ts
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
```

(`await` on Next 14's synchronous `cookies()` is harmless and keeps the file valid if Next is upgraded.)

`src/web/next.config.mjs` — add the plugin and compose it with Serwist:

```js
import withSerwistInit from "@serwist/next";
import createNextIntlPlugin from "next-intl/plugin";

const withNextIntl = createNextIntlPlugin("./src/i18n/request.ts");
```
(keep the existing `withSerwist` definition and comments) and change the export to:

```js
export default withSerwist(withNextIntl(nextConfig));
```

`src/web/src/app/layout.tsx` — add the imports, a Devanagari font, make `RootLayout` async, and wrap children:

```tsx
import { Noto_Sans_Devanagari } from "next/font/google";
import { cookies } from "next/headers";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages } from "next-intl/server";
import { PreferencesProvider } from "@/i18n/PreferencesProvider";
import { CALENDAR_COOKIE, resolveCalendar } from "@/i18n/config";

// IBM Plex Sans has no Devanagari glyphs; without this, Nepali text falls back to an arbitrary
// system font. Added as a fallback in tailwind.config.ts's sans stack.
const notoDevanagari = Noto_Sans_Devanagari({
  subsets: ["devanagari"],
  weight: ["400", "500", "600", "700"],
  variable: "--font-devanagari",
});
```

```tsx
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
```

`src/web/tailwind.config.ts` — read `theme.extend.fontFamily.sans`; append `"var(--font-devanagari)"` to the end of that stack (before the generic fallback if one exists). Do not change any other font entry.

- [ ] **Step 7: Test helper and vitest config**

`src/web/src/test/renderWithIntl.tsx`:

```tsx
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
```

`src/web/vitest.config.ts` — inside `test`, add (per the next-intl testing docs, it is ESM-only):

```ts
    server: { deps: { inline: ["next-intl"] } },
```

If `import en from "../../messages/en.json"` fails type-checking, set `"resolveJsonModule": true` in `src/web/tsconfig.json` (Next's default tsconfig normally already has it).

- [ ] **Step 8: Run tests, type-check**

Run (in `src/web`): `npx vitest run src/i18n` — Expected: PASS (all config, messages, preferences tests).
Run: `npx tsc --noEmit` — Expected: no errors.
Run: `npm run build` — Expected: build succeeds with the plugin composed with Serwist (stop the dev server first if one is running, since it shares `.next`). If the build fails on the plugin/Serwist composition, report the exact error.

- [ ] **Step 9: Commit**

```bash
git add src/web/package.json src/web/package-lock.json src/web/next.config.mjs src/web/vitest.config.ts src/web/tailwind.config.ts src/web/src/app/layout.tsx src/web/src/i18n src/web/src/test src/web/messages
git commit -m "$(cat <<'EOF'
feat(web): add next-intl foundation with en/ne catalogs

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Bikram Sambat conversion and locale-aware formatters

**Files:**
- Create: `src/web/src/lib/i18n/bsDate.ts`, `src/web/src/lib/i18n/format.ts`, `src/web/src/lib/i18n/useFormatters.ts`
- Test: `src/web/src/lib/i18n/bsDate.test.ts`, `format.test.ts`, `useFormatters.test.tsx`

**Interfaces:**
- Consumes: `@/i18n/config` (`Locale`, `CalendarPreference`, `DEFAULT_TIME_ZONE`, `resolveLocale`), `@/i18n/PreferencesProvider` (`useCalendarPreference`), `@/test/renderWithIntl` (`IntlTestProvider`), `useSession` (`@/features/auth/hooks/useSession`, returns `MeResponse | null`; Task 4 adds `locale` to its type — this task reads it defensively).
- Produces:
  - `bsDate.ts`: `interface BsDate { year: number; month: number; day: number }`, `BS_MONTHS_EN`, `BS_MONTHS_NE` (12 names), `toBsDate(date: Date, timeZone: string): BsDate | null`.
  - `format.ts`: `interface FormatContext { locale: Locale; calendar: CalendarPreference; timeZone: string }`, `formatMoney(amount, currency, locale)`, `formatNumber(value, locale)`, `formatDate(value, ctx, options?: {withTime?: boolean})`, `toDevanagariDigits(s)`.
  - `useFormatters.ts`: `useFormatters()` → `{ formatMoney, formatNumber, formatDate, formatDateTime }` bound to the current locale/calendar/time zone.

- [ ] **Step 1: Write the failing BS tests**

`src/web/src/lib/i18n/bsDate.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { BS_MONTHS_EN, BS_MONTHS_NE, toBsDate } from "./bsDate";

describe("toBsDate", () => {
  // Vector taken verbatim from the library's README: 2023-04-28 AD = 2080-01-15 BS.
  it("converts the documented reference date", () => {
    expect(toBsDate(new Date("2023-04-28T06:00:00Z"), "UTC")).toEqual({ year: 2080, month: 1, day: 15 });
  });

  it("uses the calendar date in the given time zone, not in UTC", () => {
    const instant = new Date("2023-04-27T20:00:00Z"); // 2023-04-28 01:45 in Kathmandu (UTC+5:45)
    expect(toBsDate(instant, "Asia/Kathmandu")).toEqual({ year: 2080, month: 1, day: 15 });
    expect(toBsDate(instant, "UTC")).toEqual({ year: 2080, month: 1, day: 14 });
  });

  it("returns null outside the library's supported AD range", () => {
    expect(toBsDate(new Date("1900-01-01T00:00:00Z"), "UTC")).toBeNull();
    expect(toBsDate(new Date("2050-01-01T00:00:00Z"), "UTC")).toBeNull();
  });

  it("returns null for an invalid date instead of throwing", () => {
    expect(toBsDate(new Date("not a date"), "UTC")).toBeNull();
  });

  it("has twelve month names in each language", () => {
    expect(BS_MONTHS_EN).toHaveLength(12);
    expect(BS_MONTHS_NE).toHaveLength(12);
    expect(BS_MONTHS_EN[0]).toBe("Baisakh");
    expect(BS_MONTHS_NE[0]).toBe("बैशाख");
  });
});
```

- [ ] **Step 2: Write the failing formatter tests**

`src/web/src/lib/i18n/format.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { formatDate, formatMoney, formatNumber, toDevanagariDigits } from "./format";

const nbsp = / /g;
const kathmandu = { timeZone: "Asia/Kathmandu" } as const;

describe("formatMoney", () => {
  it("formats NPR in English with grouping and two decimals", () => {
    expect(formatMoney(1250, "NPR", "en").replace(nbsp, " ")).toBe("NPR 1,250.00");
  });

  it("formats a foreign currency without converting it", () => {
    expect(formatMoney(19.5, "USD", "en")).toBe("$19.50");
  });

  it("uses Devanagari digits for Nepali", () => {
    expect(formatMoney(1250, "NPR", "ne")).toMatch(/[०-९]/);
  });
});

describe("formatNumber", () => {
  it("groups thousands in English", () => {
    expect(formatNumber(1234567, "en")).toBe("1,234,567");
  });
});

describe("toDevanagariDigits", () => {
  it("maps 0-9 to ०-९ and leaves other characters alone", () => {
    expect(toDevanagariDigits("2080-01-15")).toBe("२०८०-०१-१५");
  });
});

describe("formatDate", () => {
  const instant = new Date("2023-04-27T20:00:00Z"); // 28 Apr 2023 in Kathmandu

  it("formats Gregorian in English using the given time zone", () => {
    expect(formatDate(instant, { locale: "en", calendar: "Gregorian", ...kathmandu })).toBe("Apr 28, 2023");
  });

  it("formats Bikram Sambat in English", () => {
    expect(formatDate(instant, { locale: "en", calendar: "BikramSambat", ...kathmandu })).toBe("15 Baisakh 2080");
  });

  it("formats Bikram Sambat in Nepali with Devanagari digits", () => {
    expect(formatDate(instant, { locale: "ne", calendar: "BikramSambat", ...kathmandu })).toBe("१५ बैशाख २०८०");
  });

  it("accepts ISO strings and epoch numbers", () => {
    const ctx = { locale: "en", calendar: "Gregorian", ...kathmandu } as const;
    expect(formatDate("2023-04-27T20:00:00Z", ctx)).toBe("Apr 28, 2023");
    expect(formatDate(instant.getTime(), ctx)).toBe("Apr 28, 2023");
  });

  it("falls back to Gregorian when the date is outside the BS range", () => {
    expect(formatDate("2050-01-01T00:00:00Z", { locale: "en", calendar: "BikramSambat", timeZone: "UTC" })).toBe(
      "Jan 1, 2050",
    );
  });

  it("returns an empty string for an invalid date", () => {
    expect(formatDate("garbage", { locale: "en", calendar: "Gregorian", timeZone: "UTC" })).toBe("");
  });

  it("appends a time when asked", () => {
    const text = formatDate(instant, { locale: "en", calendar: "Gregorian", ...kathmandu }, { withTime: true });
    expect(text.replace(nbsp, " ")).toMatch(/^Apr 28, 2023.*1:45\s?AM$/);
  });
});
```

`src/web/src/lib/i18n/useFormatters.test.tsx`:

```tsx
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { IntlTestProvider } from "@/test/renderWithIntl";
import { PreferencesProvider } from "@/i18n/PreferencesProvider";
import { useFormatters } from "./useFormatters";

function Probe({ instant }: { instant: string }) {
  const { formatDate, formatMoney } = useFormatters();
  return (
    <div>
      <span data-testid="date">{formatDate(instant)}</span>
      <span data-testid="money">{formatMoney(1250, "NPR").replace(/ /g, " ")}</span>
    </div>
  );
}

function renderProbe(options: { locale: "en" | "ne"; calendar: "Gregorian" | "BikramSambat"; timeZoneId?: string }) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      userId: "u",
      organizationId: "o",
      organizationName: "Org",
      email: "a@b.c",
      fullName: "A B",
      permissions: [],
      hasAllBranchesScope: true,
      branches: [],
      locale: { defaultLocale: options.locale, calendarPreference: options.calendar, timeZoneId: options.timeZoneId ?? "Asia/Kathmandu" },
    }),
  }) as unknown as typeof fetch;
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <IntlTestProvider locale={options.locale}>
        <PreferencesProvider calendar={options.calendar}>
          <Probe instant="2023-04-27T20:00:00Z" />
        </PreferencesProvider>
      </IntlTestProvider>
    </QueryClientProvider>,
  );
}

describe("useFormatters", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("formats with the active locale, calendar and the tenant's time zone", async () => {
    renderProbe({ locale: "en", calendar: "BikramSambat" });
    await waitFor(() => expect(screen.getByTestId("date")).toHaveTextContent("15 Baisakh 2080"));
    expect(screen.getByTestId("money")).toHaveTextContent("NPR 1,250.00");
  });

  it("uses the session time zone (a UTC tenant sees the 27th, i.e. BS 14)", async () => {
    renderProbe({ locale: "en", calendar: "BikramSambat", timeZoneId: "UTC" });
    await waitFor(() => expect(screen.getByTestId("date")).toHaveTextContent("14 Baisakh 2080"));
  });

  it("falls back to Asia/Kathmandu before the session loads", () => {
    global.fetch = vi.fn().mockReturnValue(new Promise(() => {})) as unknown as typeof fetch;
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <IntlTestProvider>
          <PreferencesProvider calendar="Gregorian">
            <Probe instant="2023-04-27T20:00:00Z" />
          </PreferencesProvider>
        </IntlTestProvider>
      </QueryClientProvider>,
    );
    expect(screen.getByTestId("date")).toHaveTextContent("Apr 28, 2023");
  });
});
```

- [ ] **Step 3: Run to verify they fail**

Run (in `src/web`): `npx vitest run src/lib/i18n`
Expected: FAIL — modules don't exist.

- [ ] **Step 4: Implement `bsDate.ts`**

```ts
import DateConverter from "@remotemerge/nepali-date-converter";

export interface BsDate {
  year: number;
  month: number; // 1-12 (Baisakh = 1)
  day: number; // day of month
}

export const BS_MONTHS_EN = [
  "Baisakh", "Jestha", "Ashadh", "Shrawan", "Bhadra", "Ashwin",
  "Kartik", "Mangsir", "Poush", "Magh", "Falgun", "Chaitra",
] as const;

export const BS_MONTHS_NE = [
  "बैशाख", "जेठ", "असार", "श्रावण", "भदौ", "आश्विन",
  "कार्तिक", "मंसिर", "पौष", "माघ", "फाल्गुन", "चैत्र",
] as const;

// The library's documented AD range. ISO date strings compare correctly as plain strings.
const AD_MIN = "1918-04-13";
const AD_MAX = "2043-04-13";

// The *calendar date in the given time zone* is what gets converted: a parcel scanned at 01:45
// Kathmandu time on the 28th is the 28th in Nepal even though it is still the 27th in UTC.
function isoDateInZone(date: Date, timeZone: string): string {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone, year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(date);
  const get = (type: string) => parts.find((p) => p.type === type)!.value;
  return `${get("year")}-${get("month")}-${get("day")}`;
}

export function toBsDate(date: Date, timeZone: string): BsDate | null {
  if (Number.isNaN(date.getTime())) {
    return null;
  }
  try {
    const iso = isoDateInZone(date, timeZone);
    if (iso < AD_MIN || iso > AD_MAX) {
      return null;
    }
    const bs = new DateConverter(iso).toBs();
    return { year: bs.year, month: bs.month, day: bs.date };
  } catch {
    return null;
  }
}
```

- [ ] **Step 5: Implement `format.ts`**

```ts
import type { CalendarPreference, Locale } from "@/i18n/config";
import { BS_MONTHS_EN, BS_MONTHS_NE, toBsDate } from "./bsDate";

export interface FormatContext {
  locale: Locale;
  calendar: CalendarPreference;
  timeZone: string;
}

const DEVANAGARI_DIGITS = ["०", "१", "२", "३", "४", "५", "६", "७", "८", "९"];

// Deterministic (does not depend on the runtime's ICU data), used for BS dates.
export function toDevanagariDigits(text: string): string {
  return text.replace(/[0-9]/g, (d) => DEVANAGARI_DIGITS[Number(d)]);
}

export function formatMoney(amount: number, currency: string, locale: Locale): string {
  return new Intl.NumberFormat(locale, { style: "currency", currency }).format(amount);
}

export function formatNumber(value: number, locale: Locale): string {
  return new Intl.NumberFormat(locale).format(value);
}

function toDate(value: Date | string | number): Date {
  return value instanceof Date ? value : new Date(value);
}

// Amounts are never converted here — formatting only. Currency conversion is FxRateService's job.
export function formatDate(value: Date | string | number, ctx: FormatContext, options: { withTime?: boolean } = {}): string {
  const date = toDate(value);
  if (Number.isNaN(date.getTime())) {
    return "";
  }

  const time = options.withTime
    ? new Intl.DateTimeFormat(ctx.locale, { timeZone: ctx.timeZone, hour: "numeric", minute: "2-digit" }).format(date)
    : null;

  if (ctx.calendar === "BikramSambat") {
    const bs = toBsDate(date, ctx.timeZone);
    if (bs) {
      const names = ctx.locale === "ne" ? BS_MONTHS_NE : BS_MONTHS_EN;
      const text = `${bs.day} ${names[bs.month - 1]} ${bs.year}`;
      const dated = ctx.locale === "ne" ? toDevanagariDigits(text) : text;
      return time ? `${dated}, ${time}` : dated;
    }
    // Outside the BS conversion range: fall through to Gregorian rather than show nothing.
  }

  return new Intl.DateTimeFormat(ctx.locale, {
    timeZone: ctx.timeZone,
    dateStyle: "medium",
    ...(options.withTime ? { timeStyle: "short" as const } : {}),
  }).format(date);
}
```

- [ ] **Step 6: Implement `useFormatters.ts`**

```ts
"use client";

import { useCallback, useMemo } from "react";
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
```

(`useCallback` import is unused — remove it if lint flags it.)

Because `useSession()` returns `MeResponse`, add the optional field to the type now so this compiles — in `src/web/src/services/authService.ts`, extend `MeResponse`:

```ts
export interface MeLocale {
  defaultLocale: string;
  calendarPreference: string;
  timeZoneId: string;
}

export interface MeResponse {
  // …existing fields unchanged…
  locale?: MeLocale;
}
```

- [ ] **Step 7: Run tests**

Run (in `src/web`): `npx vitest run src/lib/i18n` — Expected: PASS.
If any assertion fails purely because the runtime's ICU data renders differently (for example the Nepali currency string not containing Devanagari digits, or the `Apr 28, 2023` medium-date pattern), **do not edit the expectation to match silently**: report DONE_WITH_CONCERNS with the actual output and the Node version (`node -p "process.versions.icu"`).
If a BS vector (`15 Baisakh 2080`, or BS 14 for UTC) fails, report the library's actual output; do not adjust the vector.
Run: `npx tsc --noEmit` — Expected: no errors.

- [ ] **Step 8: Commit**

```bash
git add src/web/src/lib/i18n src/web/src/services/authService.ts
git commit -m "$(cat <<'EOF'
feat(web): add Bikram Sambat conversion and locale-aware formatters

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Language switcher and tenant-default sync

**Files:**
- Create: `src/web/src/components/layout/LanguageSwitcher.tsx`, `src/web/src/components/layout/LocaleSync.tsx`
- Modify: `src/web/src/components/layout/AppShell.tsx`
- Test: `src/web/src/components/layout/LanguageSwitcher.test.tsx`, `LocaleSync.test.tsx`

**Interfaces:**
- Consumes: `@/i18n/config` (`LOCALES`, `LOCALE_COOKIE`, `CALENDAR_COOKIE`, `resolveLocale`, `isLocale`, `isCalendar`), `@/i18n/preferences` (`setPreferenceCookie`, `readCookie`), `useSession` (session `locale` block, Task 3's type), `renderWithIntl` / `IntlTestProvider`, messages `header.language`, `language.*`.
- Produces: `<LanguageSwitcher />` (dropdown; writes `courier_locale`, then `router.refresh()`); `<LocaleSync />` (renders nothing; when a cookie is absent, seeds it from the tenant default in the session and refreshes once).

- [ ] **Step 1: Write the failing tests**

`src/web/src/components/layout/LanguageSwitcher.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithIntl } from "@/test/renderWithIntl";
import { readCookie } from "@/i18n/preferences";
import { LanguageSwitcher } from "./LanguageSwitcher";

const refresh = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh }),
}));

describe("LanguageSwitcher", () => {
  beforeEach(() => {
    refresh.mockClear();
    document.cookie = "courier_locale=; Path=/; Max-Age=0";
  });

  it("offers both languages by their native names and marks the current one", async () => {
    renderWithIntl(<LanguageSwitcher />);
    await userEvent.click(screen.getByRole("button", { name: "Language" }));

    expect(await screen.findByRole("menuitem", { name: /English/ })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: /नेपाली/ })).toBeInTheDocument();
  });

  it("stores the chosen locale in the cookie and refreshes the route", async () => {
    renderWithIntl(<LanguageSwitcher />);
    await userEvent.click(screen.getByRole("button", { name: "Language" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: /नेपाली/ }));

    expect(readCookie("courier_locale")).toBe("ne");
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it("labels the trigger in Nepali when the locale is ne", () => {
    renderWithIntl(<LanguageSwitcher />, { locale: "ne" });
    expect(screen.getByRole("button", { name: "भाषा" })).toBeInTheDocument();
  });
});
```

`src/web/src/components/layout/LocaleSync.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import { readCookie } from "@/i18n/preferences";
import { LocaleSync } from "./LocaleSync";

const refresh = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh }),
}));

function mockSession(locale: unknown) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      userId: "u", organizationId: "o", organizationName: "Org", email: "a@b.c", fullName: "A B",
      permissions: [], hasAllBranchesScope: true, branches: [], locale,
    }),
  }) as unknown as typeof fetch;
}

function renderSync() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <LocaleSync />
    </QueryClientProvider>,
  );
}

describe("LocaleSync", () => {
  beforeEach(() => {
    refresh.mockClear();
    document.cookie = "courier_locale=; Path=/; Max-Age=0";
    document.cookie = "courier_calendar=; Path=/; Max-Age=0";
  });

  it("seeds both cookies from the tenant default when the user has no preference, then refreshes once", async () => {
    mockSession({ defaultLocale: "ne", calendarPreference: "BikramSambat", timeZoneId: "Asia/Kathmandu" });
    renderSync();

    await waitFor(() => expect(readCookie("courier_locale")).toBe("ne"));
    expect(readCookie("courier_calendar")).toBe("BikramSambat");
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it("never overrides a preference the user already chose", async () => {
    document.cookie = "courier_locale=en; Path=/";
    document.cookie = "courier_calendar=Gregorian; Path=/";
    mockSession({ defaultLocale: "ne", calendarPreference: "BikramSambat", timeZoneId: "Asia/Kathmandu" });
    renderSync();

    await new Promise((r) => setTimeout(r, 50));
    expect(readCookie("courier_locale")).toBe("en");
    expect(readCookie("courier_calendar")).toBe("Gregorian");
    expect(refresh).not.toHaveBeenCalled();
  });

  it("ignores unsupported tenant values rather than writing them", async () => {
    mockSession({ defaultLocale: "fr", calendarPreference: "Julian", timeZoneId: "Asia/Kathmandu" });
    renderSync();

    await new Promise((r) => setTimeout(r, 50));
    expect(readCookie("courier_locale")).toBeNull();
    expect(readCookie("courier_calendar")).toBeNull();
    expect(refresh).not.toHaveBeenCalled();
  });

  it("does nothing without a session (401)", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 401, json: async () => null }) as unknown as typeof fetch;
    renderSync();

    await new Promise((r) => setTimeout(r, 50));
    expect(refresh).not.toHaveBeenCalled();
  });
});
```

(`@testing-library/user-event` — confirm it is already a devDependency: `grep user-event package.json`. If it is not, use `fireEvent` from `@testing-library/react` with `pointerDown`/`click` following how an existing Radix dropdown test in this repo opens its menu — e.g. `UserMenu.test.tsx` — instead of adding a dependency.)

- [ ] **Step 2: Run to verify they fail**

Run: `npx vitest run src/components/layout/LanguageSwitcher.test.tsx src/components/layout/LocaleSync.test.tsx`
Expected: FAIL — components don't exist.

- [ ] **Step 3: Implement**

`LanguageSwitcher.tsx`:

```tsx
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
```

`LocaleSync.tsx`:

```tsx
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
```

`AppShell.tsx` — import both, render `<LocaleSync />` as the first child of `<SidebarProvider>`, and `<LanguageSwitcher />` immediately before `<ThemeToggle />` in the header's right-hand group:

```tsx
import { LanguageSwitcher } from "@/components/layout/LanguageSwitcher";
import { LocaleSync } from "@/components/layout/LocaleSync";
```
```tsx
    <SidebarProvider>
      <LocaleSync />
      <AppSidebar />
```
```tsx
          <div className="ml-auto flex items-center gap-3">
            <LanguageSwitcher />
            <ThemeToggle />
```

- [ ] **Step 4: Run tests and type-check**

Run: `npx vitest run src/components/layout` — Expected: PASS (new tests and the existing layout tests).
Run: `npx tsc --noEmit` — Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/components/layout
git commit -m "$(cat <<'EOF'
feat(web): add language switcher and tenant-default locale sync

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Translate the staff shell

**Files:**
- Modify: `src/web/src/lib/navigation/navItems.ts`, `src/web/src/components/nav-main.tsx`, `src/web/src/components/app-sidebar.tsx`, `src/web/src/components/layout/UserMenu.tsx`, `src/web/src/components/layout/HeaderSearch.tsx`, `src/web/src/components/layout/CommandPalette.tsx`
- Test (update): `src/web/src/components/nav-main.test.tsx`, `app-sidebar.test.tsx`, `components/layout/UserMenu.test.tsx`, `components/layout/CommandPalette.test.tsx`
- Test (add): Nepali-rendering cases in `nav-main.test.tsx` and `UserMenu.test.tsx`

**Interfaces:**
- Consumes: messages `nav.*`, `common.appName`, `header.searchPlaceholder`, `userMenu.logOut` (Task 2); `renderWithIntl` / `IntlTestProvider`.
- Produces: `NavItem.labelKey: NavLabelKey` replaces `NavItem.label`; `NavLabelKey` is the union of the `nav.*` item keys.

- [ ] **Step 1: Update `navItems.ts`**

Replace the `NavItem` interface and every `label:` with a `labelKey:`:

```ts
export type NavLabelKey =
  | "dashboard" | "shipments" | "branches" | "manifests" | "ndrs" | "returnShipments"
  | "riders" | "liveMap" | "myDeliveries" | "cod" | "billing" | "auditLogs" | "admin";

export interface NavItem {
  href: string;
  labelKey: NavLabelKey;
  permission: string | string[];
  icon: LucideIcon;
}
```

Entries (order, hrefs, permissions and icons unchanged): `Dashboard`→`dashboard`, `Shipments`→`shipments`, `Branches`→`branches`, `Manifests`→`manifests`, `NDRs`→`ndrs`, `Return Shipments`→`returnShipments`, `Riders`→`riders`, `Live Map`→`liveMap`, `My Deliveries`→`myDeliveries`, `COD`→`cod`, `Billing`→`billing`, `Audit Logs`→`auditLogs`, `Admin`→`admin`.

- [ ] **Step 2: Write/update the failing tests first**

Find every consumer of the old field: use the Grep tool for `item\.label|NAV_ITEMS` under `src/web/src` and list them in your report. Then:

- In `nav-main.test.tsx`, `app-sidebar.test.tsx`, `UserMenu.test.tsx`, `CommandPalette.test.tsx` replace `render(` with `renderWithIntl(` (import from `@/test/renderWithIntl`), keeping the `QueryClientProvider` etc. inside the passed element. English assertions must keep passing unchanged.
- Add to `nav-main.test.tsx` (using its existing `renderNavMain` helper extended with an optional `locale` argument that is passed to `renderWithIntl`):

```tsx
  it("renders nav labels in Nepali", async () => {
    renderNavMain(["report.read", "shipment.read"], "ne");

    await waitFor(() => expect(screen.getByText("ड्यासबोर्ड")).toBeInTheDocument());
    expect(screen.getByText("ढुवानीहरू")).toBeInTheDocument();
    expect(screen.getByText("प्लेटफर्म")).toBeInTheDocument();
    expect(screen.queryByText("Dashboard")).not.toBeInTheDocument();
  });
```

- Add to `UserMenu.test.tsx` a case that opens the menu with `locale: "ne"` and expects `लग आउट` (follow how the file's existing test opens the menu for the "Log out" assertion).

Run: `npx vitest run src/components/nav-main.test.tsx src/components/layout/UserMenu.test.tsx`
Expected: FAIL (Nepali assertions; and the old components don't use `useTranslations` yet / `labelKey` mismatch).

- [ ] **Step 3: Implement**

`nav-main.tsx`:
- `import { useTranslations } from "next-intl";`
- In `NavMain`: `const t = useTranslations("nav");` and change `<SidebarGroupLabel>Platform</SidebarGroupLabel>` to `<SidebarGroupLabel>{t("group")}</SidebarGroupLabel>`.
- In `NavMainItem`: `const t = useTranslations("nav");` at the top (before the early `return null`, hooks must not be conditional), then `const label = t(item.labelKey);` and use `label` for both `tooltip={label}` and `<span>{label}</span>`.

`app-sidebar.tsx`: `import { useTranslations } from "next-intl";`; in `SidebarBrand` add `const t = useTranslations("common");` and change the fallback `session?.organizationName ?? "Courier Platform"` to `session?.organizationName ?? t("appName")`.

`UserMenu.tsx`: `import { useTranslations } from "next-intl";`; `const t = useTranslations("userMenu");` at the top of `UserMenu()` (before the early returns); replace the literal `Log out` text node with `{t("logOut")}`.

`HeaderSearch.tsx`: `import { useTranslations } from "next-intl";`; `const t = useTranslations("header");` and replace `Search or type a command...` with `{t("searchPlaceholder")}`.

`CommandPalette.tsx`: the Pages group renders each `NAV_ITEMS` entry through a small component that currently uses `item.label` in two places — the `matches(query, item.label)` filter (~line 133) and the `<span>{item.label}</span>` (~line 140). In that component add `const tNav = useTranslations("nav");` **before** its early return, compute `const label = tNav(item.labelKey);`, and use `label` in both places. Leave the "quick action" labels (`New Shipment`, …) as literals — out of scope for I1a.

- [ ] **Step 4: Run tests, type-check, lint**

Run: `npx vitest run src/components` — Expected: PASS (all shell tests, English and Nepali).
Run: `npx tsc --noEmit` — Expected: no errors (any missed `item.label` consumer shows up here).
Run: `npm run lint` — Expected: no new errors.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/lib/navigation src/web/src/components
git commit -m "$(cat <<'EOF'
feat(web): translate the staff shell (nav, user menu, header search)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Translate the login page

**Files:**
- Modify: `src/web/src/app/login/page.tsx`, `src/web/src/features/auth/LoginForm.tsx`, `src/web/src/features/auth/schema.ts` (`loginSchema` only)
- Test (update/add): `src/web/src/features/auth/LoginForm.test.tsx`

**Interfaces:**
- Consumes: messages `auth.login.*`, `auth.showcase.*`, `auth.validation.*`, `common.appName`, `header.language` (Task 2); `<LanguageSwitcher />` (Task 4); `renderWithIntl`.
- Produces: `loginSchema` messages are now **message keys** (`"emailInvalid"`, `"passwordRequired"`) translated at display time via `useTranslations("auth.validation")`.

- [ ] **Step 1: Confirm `loginSchema` has a single consumer**

Grep for `loginSchema` under `src/web/src`. If it is used anywhere other than `LoginForm.tsx` and `schema.ts` (and tests), stop and report — the key-based messages would leak into other UI.

- [ ] **Step 2: Update/add the tests first**

In `LoginForm.test.tsx` replace `render(` with `renderWithIntl(` (import from `@/test/renderWithIntl`). Existing English assertions (`/enter a valid email/i`, the server error case, the sign-in button) must still pass. Add:

```tsx
  it("shows validation errors in Nepali", async () => {
    renderWithIntl(<LoginForm />, { locale: "ne" });

    fireEvent.click(screen.getByRole("button", { name: "साइन इन" }));

    expect(await screen.findByText("मान्य इमेल ठेगाना लेख्नुहोस्।")).toBeInTheDocument();
    expect(screen.getByText("पासवर्ड आवश्यक छ।")).toBeInTheDocument();
  });

  it("falls back to the translated generic error when the server sends none", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: false, json: async () => ({}) });
    renderWithIntl(<LoginForm />, { locale: "ne" });

    fireEvent.change(screen.getByLabelText("इमेल"), { target: { value: "a@b.com" } });
    fireEvent.change(screen.getByLabelText("पासवर्ड"), { target: { value: "secret" } });
    fireEvent.click(screen.getByRole("button", { name: "साइन इन" }));

    expect(await screen.findByText("लगइन असफल भयो।")).toBeInTheDocument();
  });
```

Run: `npx vitest run src/features/auth/LoginForm.test.tsx`
Expected: FAIL (Nepali cases).

- [ ] **Step 3: Implement**

`schema.ts` — change only `loginSchema`:

```ts
export const loginSchema = z.object({
  email: z.string().email("emailInvalid"),
  password: z.string().min(1, "passwordRequired"),
});
```

`LoginForm.tsx`:
- `import { useTranslations } from "next-intl";`
- At the top of `LoginForm`: `const t = useTranslations("auth.login"); const tv = useTranslations("auth.validation");`
- Replace literals: `Sign in` (`<h1>`) → `{t("title")}`; the `CardDescription` text → `{t("description")}`; `Email` label → `{t("email")}`; `Password` label → `{t("password")}`; `aria-label={showPassword ? "Hide password" : "Show password"}` → `aria-label={showPassword ? t("hidePassword") : t("showPassword")}`; the submit label `{isSubmitting ? "Signing in..." : "Sign in"}` → `{isSubmitting ? t("submitting") : t("submit")}`; `setServerError(body.error ?? "Login failed.")` → `setServerError(body.error ?? t("failed"))`.
- Field errors: `{errors.email.message}` → `{tv(errors.email.message as "emailInvalid" | "passwordRequired")}` and likewise for `errors.password.message`.

`app/login/page.tsx` — make it an async server component and translate; add the language switcher top-right:

```tsx
import Link from "next/link";
import { Package } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { LoginForm } from "@/features/auth/LoginForm";
import { AuthShowcasePanel } from "@/components/auth/AuthShowcasePanel";
import { MobileBrandLockup } from "@/components/auth/MobileBrandLockup";
import { LanguageSwitcher } from "@/components/layout/LanguageSwitcher";
import type { AuthLifecycleStop } from "@/components/auth/AuthLifecycleGraphic";

export default async function LoginPage() {
  const tCommon = await getTranslations("common");
  const t = await getTranslations("auth");
  const stops: AuthLifecycleStop[] = [
    { label: t("showcase.booked"), caption: t("showcase.bookedCaption"), tone: "neutral" },
    { label: t("showcase.inTransit"), caption: t("showcase.inTransitCaption"), tone: "motion" },
    { label: t("showcase.outForDelivery"), caption: t("showcase.outForDeliveryCaption"), tone: "highlight" },
    { label: t("showcase.delivered"), caption: t("showcase.deliveredCaption"), tone: "success" },
  ];

  return (
    <main className="relative grid min-h-screen lg:grid-cols-2">
      <AuthShowcasePanel
        icon={Package}
        brand={tCommon("appName")}
        headline={t("showcase.headline")}
        description={t("showcase.description")}
        stops={stops}
      />
      <div className="flex flex-col items-center justify-center gap-6 bg-background p-4">
        <div className="absolute right-4 top-4">
          <LanguageSwitcher />
        </div>
        <div className="flex flex-col items-center gap-2 lg:hidden">
          <MobileBrandLockup icon={Package} brand={tCommon("appName")} />
        </div>
        <LoginForm />
        <p className="text-sm text-muted-foreground">
          {t("login.newHere")}{" "}
          <Link href="/register" className="font-medium text-foreground underline-offset-4 hover:underline">
            {t("login.createOrganization")}
          </Link>
        </p>
      </div>
    </main>
  );
}
```

If a `login/page.test.tsx` exists, update it to `await` the async component or remove nothing — check `ls src/app/login`. If none exists, add none (the form is covered; page composition is verified in the browser at the end).

- [ ] **Step 4: Run tests, type-check, lint**

Run: `npx vitest run src/features/auth` — Expected: PASS.
Run: `npx tsc --noEmit` and `npm run lint` — Expected: clean.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/app/login src/web/src/features/auth
git commit -m "$(cat <<'EOF'
feat(web): translate the login page and add a language switcher

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Locale and calendar in Settings → Profile

**Files:**
- Modify: `src/web/src/features/admin/settings/types.ts`, `schema.ts`, `components/OrganizationProfileForm.tsx`
- Modify (only if the hook's payload type is narrower): `src/web/src/features/admin/settings/hooks/useUpdateOrganizationProfile.ts`
- Test: `src/web/src/features/admin/settings/components/OrganizationProfileForm.test.tsx`

**Interfaces:**
- Consumes: `LOCALES`, `CALENDARS`, `Locale`, `CalendarPreference` (`@/i18n/config`); messages `settings.profile.*`, `language.*` (Task 2); shadcn `Select` (`@/components/ui/select` — confirm it exists; if not, add it with the shadcn CLI `npx shadcn@latest add select` and report it); backend `PATCH /settings/organization/profile` now accepts and returns `defaultLocale`/`calendarPreference` (I0).
- Produces: the form reads and writes `defaultLocale` and `calendarPreference`.

- [ ] **Step 1: Extend types and schema**

`types.ts` — in `OrganizationProfile` add:

```ts
  defaultLocale: string;
  calendarPreference: string;
```

`schema.ts` — add the import and two fields to `organizationProfileSchema`:

```ts
import { CALENDARS, LOCALES } from "@/i18n/config";
```
```ts
  defaultLocale: z.enum(LOCALES),
  calendarPreference: z.enum(CALENDARS),
```

- [ ] **Step 2: Write the failing tests first**

Read the existing `OrganizationProfileForm.test.tsx` and how it (a) mocks the settings/mutation hooks or `fetch`, and (b) builds its fixture profile. Then:
- Add `defaultLocale: "en", calendarPreference: "Gregorian"` to every fixture profile in that file, and wrap renders with `renderWithIntl`.
- Add these cases (adapt the fixture/helper names to that file; find how another test in `src/web/src` opens a shadcn `Select` — grep `getByRole("combobox"` — and reuse that interaction):

```tsx
  it("shows the saved language and calendar", async () => {
    renderForm({ defaultLocale: "ne", calendarPreference: "BikramSambat" });

    expect(await screen.findByRole("combobox", { name: "Language" })).toHaveTextContent("नेपाली");
    expect(screen.getByRole("combobox", { name: "Calendar" })).toHaveTextContent("Bikram Sambat (BS)");
  });

  it("submits the chosen language and calendar with the profile", async () => {
    const { mutate } = renderForm({ defaultLocale: "en", calendarPreference: "Gregorian" });

    // open the Language select, choose नेपाली; open Calendar, choose Bikram Sambat (BS); then submit
    await chooseOption("Language", "नेपाली");
    await chooseOption("Calendar", "Bikram Sambat (BS)");
    fireEvent.click(screen.getByRole("button", { name: "Save Profile" }));

    await waitFor(() =>
      expect(mutate).toHaveBeenCalledWith(expect.objectContaining({ defaultLocale: "ne", calendarPreference: "BikramSambat" })),
    );
  });
```

Run: `npx vitest run src/features/admin/settings`
Expected: FAIL (fields not rendered / not submitted).

- [ ] **Step 3: Implement the form fields**

In `OrganizationProfileForm.tsx`:
- Imports: `Controller` from `react-hook-form` (add to the existing import), `useTranslations` from `next-intl`, `Select, SelectContent, SelectItem, SelectTrigger, SelectValue` from `@/components/ui/select`, `LOCALES, CALENDARS` from `@/i18n/config`.
- Inside the component: `const t = useTranslations("settings.profile"); const tLanguage = useTranslations("language");`
- `useForm` destructure gains `control`.
- In the `reset({...})` call add `defaultLocale: (data.profile.defaultLocale as OrganizationProfileFormValues["defaultLocale"]) ?? "en"` and `calendarPreference: (data.profile.calendarPreference as OrganizationProfileFormValues["calendarPreference"]) ?? "Gregorian"`.
- In `onSubmit`'s payload add `defaultLocale: values.defaultLocale, calendarPreference: values.calendarPreference`.
- Add these two blocks after the Timezone block (before Business Hours):

```tsx
      <div className="space-y-2">
        <Label htmlFor="defaultLocale">{t("locale")}</Label>
        <Controller
          control={control}
          name="defaultLocale"
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger id="defaultLocale" aria-label={t("locale")}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {LOCALES.map((locale) => (
                  <SelectItem key={locale} value={locale}>
                    {tLanguage(locale)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      </div>
      <div className="space-y-2">
        <Label htmlFor="calendarPreference">{t("calendar")}</Label>
        <Controller
          control={control}
          name="calendarPreference"
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger id="calendarPreference" aria-label={t("calendar")}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {CALENDARS.map((calendar) => (
                  <SelectItem key={calendar} value={calendar}>
                    {t(calendar === "Gregorian" ? "calendarGregorian" : "calendarBikramSambat")}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      </div>
```

- If `useUpdateOrganizationProfile`'s payload type is `Omit<OrganizationProfile, …>`-based it picks the new fields up automatically; if it lists fields explicitly, add the two.
- Existing labels in this form (Support Email, …) stay English in I1a.

- [ ] **Step 4: Run tests, type-check, lint**

Run: `npx vitest run src/features/admin` — Expected: PASS.
Run: `npx tsc --noEmit` and `npm run lint` — Expected: clean.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/admin
git commit -m "$(cat <<'EOF'
feat(web): edit tenant language and calendar in Settings > Profile

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Documentation and full verification

**Files:**
- Modify: `docs/INTERNATIONAL.md`, `docs/ROADMAP.md`
- Create (if `src/web/README.md` has an i18n gap): nothing else — document in `docs/INTERNATIONAL.md`.

**Interfaces:** none.

- [ ] **Step 1: Update `docs/INTERNATIONAL.md`**

Change the status table so I0 stays "Implemented" and add rows: `I1a | i18n foundation (frontend) | Implemented — …verification pending as noted` and `I1b | Notification-template locale | Planned`. Add an "Internationalized UI (I1a)" section stating, factually and only what was built: locale from the `courier_locale` cookie (no URL prefix); `courier_calendar` cookie; tenant defaults delivered on `GET /api/v1/auth/me` as `locale { defaultLocale, calendarPreference, timeZoneId }` and applied only when the user has no cookie; the switcher in the staff header and on the login page; formatters `formatMoney/formatNumber/formatDate` in `src/web/src/lib/i18n` with the BS range limit (AD 1918-04-13…2043-04-13, Gregorian fallback outside it); catalogs in `src/web/messages` with a parity test; translated so far: staff shell and login; not yet translated: everything else (falls back to English) and public tracking (I7); Nepali strings are machine-drafted and need native review; a tenant changing its default does not change users who already chose a language.

- [ ] **Step 2: Update `docs/ROADMAP.md`**

Match the file's existing style; mark I1a as implemented and I1b as planned. Do not use the word "complete".

- [ ] **Step 3: Verify everything that can be verified**

Run (in `src/web`, dev server stopped): `npx tsc --noEmit`, `npm run lint`, `npm test`, `npm run build`. Expected: all succeed. Report exact counts for `npm test` and any pre-existing failures you can prove pre-date this branch (compare against `git stash`-free evidence: run the failing test file on `master`'s commit in a temporary worktree if needed — never with bare `git stash`).
Run (repo root): `dotnet build Courier.slnx`, `dotnet test tests/Identity.Tests`, and `dotnet test tests/Api.Tests --filter "FullyQualifiedName!~PlatformSettingsJwtRotationTests"` (the excluded test rotates the shared JWT signing key and is known to cause unrelated failures when run in parallel; run it alone afterwards).
Expected: green except the documented timing-sensitive `ShipmentCreated_SendsRealEmailToMerchantContactEmail` (re-run it alone if it fails).

- [ ] **Step 4: Report what still needs a human**

State plainly: the Nepali translations need native review; the language switcher and tenant-default seeding are covered by unit tests but the full cookie → server-render → refresh loop needs one run in a real browser session (the controller/user logs in; do not try to create an account or type a password).

- [ ] **Step 5: Commit**

```bash
git add docs
git commit -m "$(cat <<'EOF'
docs: document international I1a (i18n foundation)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```
