# International Support

Design: `docs/superpowers/specs/2026-09-21-international-support-design.md`.
Plans: `docs/superpowers/plans/`.

## Status

| Step | Scope | State |
|---|---|---|
| I0 | Money, FX rates, tenant locale | Implemented — DB-backed tests (Identity/Api) and both migrations still need a first run against Postgres |
| I1–I8 | See the design spec §8 | Planned |

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
