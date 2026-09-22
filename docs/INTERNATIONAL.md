# International Support

Design: `docs/superpowers/specs/2026-09-21-international-support-design.md`.
Plans: `docs/superpowers/plans/`.

## Status

| Step | Scope | State |
|---|---|---|
| I0 | Money, FX rates, tenant locale | Implemented — verified against Postgres on 2026-09-21 (full Api suite 290/290; four tests are flaky only under full-suite parallel load) |
| I1a | i18n foundation (frontend) | Implemented — Nepali strings are machine-drafted and need native review; the cookie → server-render → refresh loop has only been covered by unit tests, not a real browser session |
| I1b | Notification-template locale | Implemented — Nepali default bodies are machine-drafted and need native review |
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

## Notification locale (I1b)

- **Locale source.** A dispatched notification's locale is the recipient organization's
  `OrganizationSettings.DefaultLocale` (I0), read at dispatch time via
  `IOrganizationLocaleLookup.GetDefaultLocaleAsync` and normalized (unsupported/missing falls back
  to `en`) by `NotificationLocales.Normalize`. There is no per-recipient locale — every notification
  for a given event goes out in the tenant's one default language.
- **Fallback order.** `NotificationTemplateResolver.Resolve` tries, in order: (1) the org's active
  override in the resolved locale, (2) the compiled-in default body in the resolved locale, (3) the
  org's active override in `en`, (4) the compiled-in default body in `en`. A locale-appropriate
  compiled default deliberately beats an org's English override, so a Nepali tenant never silently
  gets English when a Nepali default exists.
- **Storage.** `NotificationTemplate.Locale` (`en`/`ne`, default `en`) was added by migration
  `20260921143005_AddNotificationTemplateLocale` as an additive, backfilled column (existing rows
  set to `en`); the uniqueness of an override is now `(OrganizationId, EventType, Channel, Locale)`.
  Nepali compiled-in defaults exist for all five known event types alongside the existing English
  ones (`NotificationDefaultTemplates`).
- **API.** `PUT /api/v1/settings/notification-templates` takes an optional `locale` field (defaults
  to `en` when omitted); an unsupported value returns 400 (`NOTIFICATION_TEMPLATE_VALIDATION_FAILED`).
  `GET /api/v1/settings/notification-templates` returns each override's `locale`. Both require
  `notification_template.manage` and are audited.
- **Admin panel.** The notification templates panel shows one row per (event type, language) pair —
  each event type doubled into an English row and a Nepali row — with its own "Using
  default"/"Custom" status and its own edit Sheet.

### Known limitations

- No per-recipient locale: a notification always uses the tenant's single default locale, not an
  individual customer's or merchant staff member's preference.
- `NotificationLog` does not record which locale a dispatched notification was rendered in.
- The admin panel itself (labels, chrome) is not translated; only the template bodies it edits are
  locale-aware.
