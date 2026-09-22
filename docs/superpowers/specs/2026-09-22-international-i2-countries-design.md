# International I2 — Countries, Country Zones, International Address

Parent: `2026-09-21-international-support-design.md` (§4 Geography, §8 step I2). Follows I0 (`Money`/`CurrencyCode`, tenant locale) and I1a/I1b (i18n foundation, notification locale).

## 1. Goal

Give the platform a normalized, platform-level list of countries and a service-list admin screen so SuperAdmin can decide which destinations the courier currently serves. Introduce the `InternationalAddress` value object as a building block for I3, without wiring it to any entity yet.

## 2. Decisions

- **Module placement.** `Country`, `CountryZone` and `InternationalAddress` join the existing `Geography` module (`src/Modules/Geography/{Domain,Application,Infrastructure,Presentation}`), alongside `Province`/`District`/`Ward`/`Area`/`DeliveryZone`. No new module is created in I2; the dedicated `International` module (for `InternationalLeg`, `CustomsDeclaration`, etc.) is introduced in I3.
- **Seed scope.** All ~249 ISO-3166-1 alpha-2 countries are seeded with English name, default currency (ISO-4217 format), and calling code — all objectively sourceable, no translation required. `NameNe` is nullable and left blank except for a short starter set of Nepal's most likely trading partners (India, China, USA, UK, UAE, Qatar, Australia, Canada, Malaysia, South Korea, Saudi Arabia, Japan); the UI falls back to `NameEn` when blank, same pattern as I1b's template fallback.
- **No pre-enabled countries.** Every seeded row starts `IsServiced = false`. The platform makes no coverage claim until a SuperAdmin turns a country on, consistent with "partner carriers only, no own international hubs."
- **Country zones start empty.** `CountryZone` exists as a table and admin CRUD screen, but no rows are seeded — zone taxonomy is a pricing decision (I5) that hasn't been made yet.
- **No Country create/delete.** The row set is fixed to the ISO list. The only mutable fields are `IsServiced` and `CountryZoneId`. This keeps I2 from having to validate arbitrary user-entered ISO codes/currencies.
- **`InternationalAddress` has no consumer yet.** It is a pure value object (format validation only, no DB access, no permission/service-list check baked in) that I3 will embed in the shipment recipient/sender when `Scope = International`. Building it now, tested in isolation, keeps I3 smaller.

## 3. Data model

**`Country`** (`Geography.Domain`):

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Deterministic, `GeographyIds.CountryId(isoCode)` (same hash-from-key pattern as existing `GeographyIds`) |
| `IsoCode` | `string(2)` | Unique, uppercase, ISO-3166-1 alpha-2 |
| `NameEn` | `string` | Required |
| `NameNe` | `string?` | Nullable; UI falls back to `NameEn` |
| `DefaultCurrency` | `string(3)` | ISO-4217 format; parsed via `CurrencyCode.Parse` at the application boundary, stored as plain string (matches `FxRate.FromCurrency`/`ToCurrency`) |
| `CallingCode` | `string` | E.g. `+977`; format-validated, not parsed into a value object |
| `IsServiced` | `bool` | Default `false` |
| `CountryZoneId` | `Guid?` | FK to `CountryZone`, nullable |
| `CreatedAt`/`UpdatedAt` | `DateTimeOffset` | |

**`CountryZone`** (`Geography.Domain`): `Id`, `Name` (unique, required), `Description` (nullable), `CreatedAt`/`UpdatedAt`.

**`InternationalAddress`** (`Geography.Domain`, plain immutable record, no EF mapping): `Line1` (required), `Line2` (nullable), `City` (required), `Region` (nullable), `PostalCode` (nullable), `CountryCode` (required, 2-letter format, validated the same way as `IsoCode` but with no DB lookup — it does not check the code exists in `Country` or that the country `IsServiced`; that policy check belongs to whatever books a shipment in I3).

Both entities are platform-level: no `OrganizationId`, no tenant query filter — same as `FxRate` and the existing Nepal geography tables.

**Migration.** One additive migration, `AddCountriesAndCountryZones`: creates `CountryZones`, then `Countries` (FK to `CountryZones`, nullable), then seeds `Countries` via `HasData` from a new `CountrySeedData.cs` static array. No permission changes, so no second migration is needed (unlike the `AddRiderLocationReadPermission` precedent, which paired a data migration with a permission-insert migration).

## 4. Application layer

`Geography.Application` gains:

- `CountryDto` / `CountryZoneDto` (id, code/name fields, `isServiced`, `countryZoneId` + `countryZoneName`).
- `CountryQueryService`:
  - `GetServicedCountriesAsync()` → full unpaginated list where `IsServiced = true`, ordered by `NameEn`. Backs the geography read endpoint.
  - `GetCountriesForAdminAsync(page, pageSize, search)` → paginated, includes unserviced rows, `search` matches `IsoCode`/`NameEn`/`NameNe`.
- `CountryAdminService.UpdateCountryAsync(countryId, isServiced, countryZoneId)` — validates `countryZoneId` exists if provided, audits before/after `IsServiced`/`CountryZoneId` as `country.updated`, no `OrganizationId` on the audit record (platform-level, same as `fx_rate.recorded`).
- `CountryZoneService` — `List`, `Create`, `Update` (name/description only); duplicate name → 400; audits `country_zone.created` / `country_zone.updated`.

## 5. API

| Route | Auth | Notes |
|---|---|---|
| `GET /api/v1/geography/countries` | Dual: staff `geography.read` or merchant `merchant.geography.read` (same check as existing geography endpoints) | Serviced-only, unpaginated |
| `GET /api/v1/platform/countries?search=&page=&pageSize=` | `RequireSuperAdmin()` | All rows, paginated |
| `PATCH /api/v1/platform/countries/{id}` | `RequireSuperAdmin()` | Body `{ isServiced, countryZoneId }`; 404 unknown id, 400 `COUNTRY_VALIDATION_FAILED` on unknown `countryZoneId` |
| `GET /api/v1/platform/country-zones` | `RequireSuperAdmin()` | List all |
| `POST /api/v1/platform/country-zones` | `RequireSuperAdmin()` | 400 `COUNTRY_ZONE_VALIDATION_FAILED` on duplicate name |
| `PATCH /api/v1/platform/country-zones/{id}` | `RequireSuperAdmin()` | Same validation; 404 unknown id |

No new permission catalog entries — reuses `geography.read` / `merchant.geography.read` (reads) and the existing `RequireSuperAdmin()` extension (writes), matching `FxRateEndpoints`.

## 6. Frontend

`src/web/src/features/platform-countries/` and `platform-country-zones/`, mirroring `features/platform-plans/` exactly: a Zustand store holding only sheet-open state, TanStack Query hooks against Next.js API proxy routes (`src/web/src/app/api/platform/countries/`, `.../country-zones/`), a `CountryEditSheet` (serviced switch + zone select, shadcn `Switch`/`Select`) and a `CountryZoneSheet` (create/edit form). Pages at `src/web/src/app/platform/countries/` (table: ISO code, English/Nepali name, currency, zone, serviced toggle, search box, pagination) with a secondary tab or link to manage zones. Loading and error states via existing shared patterns (`Spinner`, error banners). No merchant- or staff-facing UI in I2 — nothing consumes the serviced list until I3/I7.

## 7. Tests

- **Unit:** `Country`/`CountryZone` invariants (IsoCode format/uniqueness at the DB level, `DefaultCurrency` parses via `CurrencyCode`, `CallingCode` format); `InternationalAddress` validation (required fields, `CountryCode` format); seed-data integrity (exactly the expected count, all `IsoCode` unique, all `DefaultCurrency` values parse as valid `CurrencyCode`, Nepal present with `NP`/`NPR`).
- **Integration (DB):** migration applies cleanly; `UpdateCountryAsync` persists and audits; `CountryZone` CRUD persists and audits; assigning an unknown `CountryZoneId` to a country is rejected.
- **API:** `RequireSuperAdmin()` gating on every `/platform/countries*` and `/platform/country-zones*` route (403 for a non-SuperAdmin org admin); dual-auth on `GET /geography/countries` (staff and merchant both succeed, unauthenticated 401); serviced-only filtering; validation error codes; 404s on unknown ids.
- **Frontend:** country list renders, serviced toggle round-trips through the mutation hook, zone sheet create/edit.

## 8. Docs to update

`INTERNATIONAL.md` (status table row for I2, new section), `DOMAIN-MODEL.md` (`Country`, `CountryZone`, `InternationalAddress`), `RBAC.md` (note the reuse of `geography.read`/`merchant.geography.read` and `RequireSuperAdmin()` for these routes — no new permissions), `API-SPEC.md` (new endpoints).

## 9. Out of scope

Wiring `InternationalAddress` into `Shipment` or any other entity (I3); pricing by zone (I5); native-speaker review of the Nepali starter names; a Country create/delete API; a public/merchant-facing country picker UI (I3/I7).
