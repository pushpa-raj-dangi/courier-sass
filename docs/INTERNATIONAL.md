# International Support

Design: `docs/superpowers/specs/2026-09-21-international-support-design.md`.
Plans: `docs/superpowers/plans/`.

## Status

| Step | Scope | State |
|---|---|---|
| I0 | Money, FX rates, tenant locale | Implemented — DB-backed tests (Identity/Api) and both migrations still need a first run against Postgres |
| I1a | i18n foundation (frontend) | Implemented — Nepali strings are machine-drafted and need native review; the cookie → server-render → refresh loop has only been covered by unit tests, not a real browser session |
| I1b | Notification-template locale | Planned |
| I2–I8 | See the design spec §8 | Planned |

## Money and currency (I0)

- `Courier.SharedKernel.Money` (amount + `CurrencyCode`) is immutable; arithmetic is same-currency only, and a mismatch throws `CurrencyMismatchException`. Crossing currencies requires `ConvertTo` with an explicit rate.
- `ConvertTo` rounds the converted amount to two decimals (away from zero) regardless of target currency, which is correct for NPR (the only conversion target in I0); `Multiply` does not round.
- `CurrencyCode` validates three ASCII letters and normalizes to upper case. It does not check membership in ISO 4217.
- **Every existing persisted amount is NPR.** Merchants are charged in NPR and COD is domestic-only, so existing tables have no currency column. New international entities store an explicit amount and currency per field.

## FX rates (I0)

- `FxRates` is platform-level, append-only, and not tenant-owned (no `OrganizationId`, no query filter). A correction is a new row with a later `EffectiveAt`. `Rate` means 1 unit of `FromCurrency` = `Rate` units of `ToCurrency`.
- `Rate` is stored with 8 decimal places: values are rounded to 8 decimals on entry, and rates below 0.00000001 or at/above 10,000,000,000 are rejected.
- "Current" for a pair is the row with the greatest `EffectiveAt <= asOf` (ties broken by `CreatedAt`). Only the exact pair is used; inverse rates are not derived.
- SuperAdmin records rates manually (source is always `manual`): `POST /api/v1/platform/fx-rates`; list with `GET /api/v1/platform/fx-rates?from=&to=&page=&pageSize=`. Both routes require SuperAdmin. Validation: both currencies must be valid codes and differ, the rate must be greater than zero, and `EffectiveAt` cannot be more than 5 minutes in the future (defaults to now). Failures return 400 with code `FX_RATE_VALIDATION_FAILED`. Each record is audited (`fx_rate.recorded`, no organization).
- Other modules convert through `IFxConversionService.ConvertAsync`, which returns an `FxConversion` holding the source `Money`, the converted `Money`, the `FxRateId` used, and the rate, so callers can snapshot them. A same-currency conversion is the identity with rate 1 and a null `FxRateId`. A missing rate throws `FxRateNotFoundException`.
- An automated rate feed is planned for I5, once a rate source is chosen.

## Tenant locale (I0)

`OrganizationSettings.DefaultLocale` (`en`, `ne`) and `CalendarPreference` (`Gregorian`, `BikramSambat`) are edited through `PATCH /api/v1/settings/organization/profile`, which requires the `org.settings.manage` permission and writes to the caller's own organization. Omitting a field (null) leaves the saved value unchanged; unsupported values return 400. The UI for these settings ships in I1.

## Internationalized UI (I1a)

- **Locale source.** The UI language comes from the `courier_locale` cookie (`en` or `ne`); there is no URL prefix. The calendar comes from the `courier_calendar` cookie (`Gregorian` or `BikramSambat`).
- **Tenant defaults.** `GET /api/v1/auth/me` returns `locale { defaultLocale, calendarPreference, timeZoneId }` from the organization's settings. The frontend applies these only when the user has no cookie yet. A tenant changing its default therefore does not change users who already chose a language.
- **Switcher.** A language switcher is in the staff header and on the login page. Tenant admins edit the default language and calendar in Settings → Profile.
- **Formatters.** `formatMoney`, `formatNumber` and `formatDate` live in `src/web/src/lib/i18n`. Bikram Sambat conversion supports AD 1918-04-13 to 2043-04-13; dates outside that range fall back to Gregorian.
- **Catalogs.** Messages are in `src/web/messages` (`en`, `ne`), with a test that checks the two catalogs have the same keys.
- **Translated so far:** the staff shell (navigation, user menu, header search) and the login page.
- **Not yet translated:** every other screen, which falls back to English, and public tracking (I7). Notification-template locale is I1b.

### Known limitations

- Nepali strings are machine-drafted and need native-speaker review.
- The command-palette quick-action labels remain English.
- The formatters exist but are not yet adopted at the roughly 54 files (about 90 call sites) that still format numbers and dates ad hoc.
- The locale and calendar cookies are `Path=/`, so a staff user who picks Nepali also gets `<html lang="ne">` on screens that are still English (public tracking, marketing, and the merchant, rider and platform portals).
- The root layout now reads cookies, which makes every route dynamically rendered, including `/track/{awb}`. If a CDN is ever put in front, HTML must vary on `courier_locale` and `courier_calendar`.
- The first sign-in of a user with no cookie renders once in English before `LocaleSync` seeds the tenant default and refreshes.
- The login-page switcher position and the cookie → server-render → refresh loop are covered by unit tests only; they still need a run in a real browser session.
