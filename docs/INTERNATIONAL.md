# International Support

Design: `docs/superpowers/specs/2026-09-21-international-support-design.md`.
Plans: `docs/superpowers/plans/`.

## Status

| Step | Scope | State |
|---|---|---|
| I0 | Money, FX rates, tenant locale | Implemented — verified against Postgres on 2026-09-21 (full Api suite 294/294; 8 distinct tests are flaky only under full-suite parallel load, all passing in isolation) |
| I1a | i18n foundation (frontend) | Implemented — Nepali strings are machine-drafted and need native review; the cookie → server-render → refresh loop has only been covered by unit tests, not a real browser session |
| I1b | Notification-template locale | Implemented — Nepali default bodies are machine-drafted and need native review |
| I2 | Countries, country zones, international address | Implemented — seeds Nepal + UN member states + a handful of dependent territories (not the complete ISO-3166-1 list); every country starts unserviced; Nepali names filled in for a 12-country starter set |
| I3 | International module core | Implemented — see below |
| I4–I8 | See the design spec §8 | Planned |

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
  set to `en`); `(OrganizationId, EventType, Channel, Locale)` is the logical lookup key the upsert
  path uses to find-or-update an override — it is a plain (non-unique) index, not a database
  uniqueness constraint, so `NotificationDispatchService` still resolves defensively by taking the
  first active row per locale. Nepali compiled-in defaults exist for all five known event types
  alongside the existing English ones (`NotificationDefaultTemplates`).
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

## Countries and country zones (I2)

- **Data.** `Country` (`Geography` module, platform-level, no `OrganizationId`) is seeded with
  Nepal, every UN member state, Vatican City, Palestine, Kosovo, Taiwan, and a set of dependent
  territories relevant to international shipping (Hong Kong, Macao, Puerto Rico, and others — see
  `CountrySeedData`). Every row starts `IsServiced = false`; a SuperAdmin turns destinations on
  individually at `/platform/countries`. There is no create/delete API — the row set is fixed to
  the seed; more territories are added later via an additive migration if needed.
- **Names.** `NameEn` is authoritative for every row. `NameNe` is filled in only for a 12-country
  starter set (India, China, USA, UK, UAE, Qatar, Australia, Canada, Malaysia, South Korea, Saudi
  Arabia, Japan); every other row falls back to `NameEn` in the UI.
- **Country zones.** `CountryZone` ships empty; a SuperAdmin creates zones and assigns countries to
  them at `/platform/countries`. No pricing or reporting consumes zones yet (I5).
- **API.** `GET /api/v1/geography/countries` (serviced-only, dual-auth: staff `geography.read` or
  merchant `merchant.geography.read`) is the read any address form will use from I3 onward.
  `GET`/`PATCH /api/v1/platform/countries` and `GET`/`POST`/`PATCH /api/v1/platform/country-zones`
  are SuperAdmin-only — no new permission constants were needed. Both `PATCH` routes are
  full-replace, not merge-patch: `PATCH /platform/countries/{id}` always requires both
  `isServiced` and `countryZoneId` in the body (omitting one resets it to `false`/`null`), and
  `PATCH /platform/country-zones/{id}` always requires both `name` and `description`. The admin
  UI always sends both; a future API caller must too.
- **`InternationalAddress`.** A format-validated value object (`Geography.Domain`) with no
  consumer yet; I3 will embed it in the shipment recipient/sender when `Scope = International`.
- **Why `docs/RBAC.md` and `docs/API-SPEC.md` weren't touched.** Neither document catalogs
  platform-track/internal SuperAdmin routes at all: `API-SPEC.md` only documents the
  merchant-facing API, and `RBAC.md` doesn't mention `RequireSuperAdmin` or any platform route.
  This feature's platform endpoints are documented here in `INTERNATIONAL.md` instead, consistent
  with how `FxRate`'s platform routes were handled in I0.

### Known limitations

- The seed is not the complete ISO-3166-1 alpha-2 list — uninhabited/unshippable territories
  (Antarctica, Bouvet Island, etc.) are excluded, and some inhabited dependent territories may be
  missing. There is no way to add a missing country without a migration.
- `InternationalAddress.CountryCode` is format-validated only; nothing yet checks it against a
  real `Country` row or `IsServiced`.

## International module core (I3)

- **`Scope`/`Direction`.** `Shipment` (`Shipments.Domain`) gains two enums, both set once at
  creation and never changed afterward: `Scope` (`Domestic`, `International` — default
  `Domestic`) and `Direction` (`Domestic`, `Outbound`, `Inbound` — default `Domestic`). Every
  pre-I3 row defaults to `Domestic`/`Domestic` via the migration, so existing domestic shipments
  and the existing domestic creation path are unaffected.
- **Address columns.** `RecipientProvinceId`/`RecipientDistrictId`/`RecipientLocalLevelId`/
  `RecipientWardNumber` and the equivalent `Sender*` fields are now nullable. New nullable columns
  `RecipientCity`/`RecipientRegion`/`RecipientPostalCode`/`RecipientCountryCode` (and the
  equivalent `Sender*`) hold the foreign-side address. `RecipientAddressLine`/`SenderAddressLine`
  (existing columns) are reused as `InternationalAddress.Line1` for whichever side is foreign —
  there is no separate `Line1` column. Which side is foreign is determined by `Direction`:
  `Outbound` → the recipient is foreign (sender stays Nepal); `Inbound` → the sender is foreign
  (recipient stays Nepal). `Shipments.Application.ShipmentAddressValidator` enforces this: the
  Nepal-side party must have a full, valid province/district/local-level/ward chain (checked via
  `IGeographyLookupService.IsValidHierarchyAsync`, same as a domestic shipment), and the
  foreign-side party's `AddressLine`/`City`/`CountryCode` must parse as a valid
  `InternationalAddress` (`Geography.Domain`). `Region`/`PostalCode` are passed into
  `InternationalAddress.Create` too, but it only format-checks `Line1`/`City`/`CountryCode`;
  `InternationalShipmentService.CreateAsync` enforces the column limits for the foreign side
  (`City`/`Region` ≤ 200, `PostalCode` ≤ 20) as `400 INTERNATIONAL_SHIPMENT_VALIDATION_FAILED`.
  Any `City`/`Region`/`PostalCode`/`CountryCode` sent for the **Nepal-side** party is dropped
  (stored as null) by `CreateShipmentShellAsync` — that party is addressed by its geography chain
  only. The foreign `CountryCode` is stored upper-cased.
- **Country service check.** Before creating the shell, `InternationalShipmentService.CreateAsync`
  (`International.Application`) resolves the foreign-side `CountryCode` and calls the new
  `Geography.Application.ICountryLookupService.IsServicedAsync` (I2's first consumer of
  `Country.IsServiced`). A missing/unserviced country returns `400
  INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` ("Destination country is not currently serviced.")
  before the gateway is ever called.
- **COD guard.** The international creation request has no `CodAmount` field at all — COD on an
  international shipment is impossible to express, not merely rejected at runtime.
- **`InternationalLeg`/`CustomsItem`** (new `International.Domain`, shipment-owned data, unlike
  `Country`/`FxRate`'s platform-level reference data). `InternationalLeg` carries `OrganizationId`
  and the standard tenant query filter directly. `CustomsItem` does **not** carry its own
  `OrganizationId` or query filter — tenant isolation flows transitively through
  `InternationalLegId -> InternationalLeg.OrganizationId` (`AppDbContext`, ~line 452), the same
  pattern used for `ShipmentItem`/`ManifestShipment`. Any query against `CustomsItems` must go
  through, or join to, `InternationalLeg` to get tenant isolation — a raw `CustomsItems.Where(...)`
  with no `InternationalLeg` join would enumerate every tenant's rows. `InternationalLeg` is
  one-to-one with `Shipment` by `ShipmentId`: contents description, ISO alpha-2 country of origin,
  `CustomsPurpose` (`Gift`, `CommercialSale`, `Sample`, `Return`, `Documents`, `Other`), `Incoterm`
  (`Ddu`, `Ddp`), an optional export licence reference, `CustomsCompletedAt` (null until the
  declaration is completed), and the provisional pricing fields below. `CustomsItem` is one row per
  the shipment's existing `ShipmentItem` (`HsCode` + a `Money` customs value), written by
  `InternationalShipmentService.UpdateCustomsAsync` — every call fully replaces the shipment's
  existing `CustomsItem` rows rather than merging. The request must carry **exactly one** customs
  item per `ShipmentItem` — no duplicates, no subset, no foreign item ids — validated against the
  shipment's real items via the new `Shipments.Application.IShipmentItemLookupService.GetItemsAsync`
  (which joins through `Shipment` so its tenant filter applies — `ShipmentItem` has none of its
  own), not a DB-level FK (`CustomsItem` lives in a different module's `DbSet`).
  `InternationalLeg.ShipmentId` **does** have a DB FK to `Shipments` (Restrict), wired centrally
  in `AppDbContext` (migration `AddInternationalLegShipmentForeignKey`), so an orphan leg is
  impossible.
- **Customs validation (`PATCH /{id}/customs`).** All failures are `400
  INTERNATIONAL_SHIPMENT_VALIDATION_FAILED`: `ContentsDescription` required, ≤ 1000;
  `CountryOfOrigin` a two-letter code that exists in `Country` (`ICountryLookupService.ExistsAsync`
  — serviced or not); `Purpose`/`Incoterm` defined enum names; `ExportLicenceReference` ≤ 200;
  `Items` non-null/non-empty; per item `HsCode` required, ≤ 20, `CustomsValueAmount` ≥ 0,
  `CustomsValueCurrency` a valid three-letter code. The declaration is **frozen once the shipment
  reaches `CustomsPrepared`**: it is editable only while the shipment is in `Created` …
  `AtDestinationBranch`; any other status is rejected with `400
  INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` (not `SHIPMENT_INVALID_TRANSITION` — a PATCH is not a
  status transition, and the rule belongs to International's own aggregate). Every update is
  audited (`international_leg.customs_updated`) with the full declaration (contents, country,
  purpose, incoterm, licence, items) as JSON in `Before` (null on the first completion) and
  `After`.
- **`CustomsPrepared` reachability.** One new `ShipmentStatus`/`ShipmentEventType` value,
  `CustomsPrepared`, appended at the end of both enums. `InternationalShipmentService.PrepareCustomsAsync`
  rejects the call (`400 INTERNATIONAL_SHIPMENT_VALIDATION_FAILED`) unless
  `InternationalLeg.CustomsCompletedAt` is set, then calls
  `IShipmentInternationalGateway.TransitionToCustomsPreparedAsync`, which picks the expected
  starting status dynamically: `Sorted` when `OriginBranchId == DestinationBranchId` (the common
  case — a branch that handles exports locally, mirroring `Manifests.Application.ManifestService.CreateAsync`'s
  hard rejection of `FromBranchId == ToBranchId`, so there is no manifest path from `Sorted` to
  `AtDestinationBranch` in that case), or `AtDestinationBranch` when they differ (after the
  existing, unmodified manifest dispatch/receive cycle). This is the one deliberate deviation from
  every other transition method's single hardcoded `expectedFrom`. `TransitionToCustomsPreparedAsync`
  also rejects `Scope = Domestic` shipments outright.
- **API — four routes, all under `/api/v1/international/shipments`, staff-only (JWT + permission
  + branch scope).** Missing permission → `403`; cross-tenant id → `404`.
  - `POST /` — `international.create`, branch-scoped to `OriginBranchId` (same rule as domestic
    `POST /api/v1/shipments`, which also rejects an origin outside the caller's `shipment.create`
    branches). **One transaction:** `InternationalShipmentService.CreateAsync` first runs every
    International-side validation (direction, items non-empty with no null entries, item
    `Quantity > 0` and `UnitValue ≥ 0`, weight > 0, charge ≥ 0, currency, foreign address lengths,
    serviced country), then opens a DB transaction (`IsRelational()`-guarded, same
    pattern as `CodService.Settlements.cs`/`AuthService.cs`) around the gateway's
    `CreateShipmentShellAsync` (Shipment/ShipmentItems/creation event, audit, `shipment.created`
    outbox row), the `InternationalLeg` insert and its audit; any failure rolls all of it back. The
    gateway shares the same scoped `AppDbContext`, so its `SaveChangesAsync` calls enlist.
    **AWB allocation is serialized per organization:** before computing the next AWB (max existing
    same-day sequence + 1), the gateway takes a Postgres transaction-scoped advisory lock,
    `pg_advisory_xact_lock(0x41574231 /* "AWB1" */, <32-bit fold of OrganizationId>)`
    (`ShipmentService.BeginAwbAllocationAsync`). Inside this create transaction the lock is held
    until the whole create commits, so a concurrent same-org create waits and then sees the
    committed AWB instead of computing the same number (without it, racers could not see the
    winner's uncommitted AWB, kept colliding on `IX_Shipments_OrganizationId_Awb` and exhausted the
    retry budget → 500). Same-org international creates are therefore serialized end to end;
    different orgs don't contend (a 32-bit fold collision would only serialize two orgs, never
    break correctness). Domestic `POST /api/v1/shipments` shares the lock: it has no ambient
    transaction, so it opens a short one covering just the AWB read and Shipment insert and commits
    it immediately (its audit/COD/outbox writes stay outside, as before). The existing
    catch-`DbUpdateException` retry loop remains as a fallback; it still works inside a transaction
    because EF Core wraps each `SaveChangesAsync` in a savepoint. The InMemory provider (unit
    tests) takes no lock. Covered by 10-way (international) and 20-way (domestic) concurrent
    same-org API tests. The gateway itself also rejects a null item, `Quantity ≤ 0` or
    `UnitValue < 0` (defense in depth), and validates, before writing anything, that `MerchantId`
    exists in the caller's tenant (`IMerchantLookupService.ExistsAsync`), that both branches exist
    in the tenant (`IBranchLookupService.GetZoneInfoAsync`), that `Direction` is `Outbound`/
    `Inbound`, and the origin branch scope — `400 SHIPMENT_VALIDATION_FAILED` on failure. Returns
    `201` with `{ shipment, leg }` (`InternationalShipmentDetailResponse`).
  - `GET /{id}` — `international.read`. Merged `Shipment` + `InternationalLeg` + `CustomsItem`
    detail (`leg.customsItems`: `shipmentItemId`, `hsCode`, `customsValueAmount`,
    `customsValueCurrency`), via `InternationalShipmentQueryService.GetAsync`. `404` if the shipment
    has no `InternationalLeg`, isn't in the caller's tenant, or — for a branch-restricted caller —
    touches none of their branches as origin, destination or current branch (the same visibility
    rule as `GET /api/v1/shipments`).
  - `PATCH /{id}/customs` — `international.manage_customs`. Full-replace customs declaration (see
    validation above). Same `404` visibility rule as GET.
  - `POST /{id}/prepare-customs` — `international.manage_customs`. Same `404` visibility rule, then
    `400 INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` if customs isn't complete yet, then the gateway
    requires the caller to have branch access to the shipment's **current** branch (the branch
    physically holding it) — `400 SHIPMENT_VALIDATION_FAILED` otherwise, same as a scan at the wrong
    branch. Triggers the `CustomsPrepared` transition; `InternationalEndpoints.cs` maps a thrown
    `ShipmentInvalidTransitionException` to `400 SHIPMENT_INVALID_TRANSITION` via
    `Results.BadRequest` if the state guard fails, matching every other
    `ShipmentInvalidTransitionException` mapping in this codebase (e.g. `API-SPEC.md`'s error
    envelope example).
- **Cross-module contracts.** `International` never touches `Shipment`/`ShipmentItem`'s `DbSet`s
  directly. It goes through three narrow interfaces implemented by `Shipments.Application`:
  `IShipmentInternationalGateway` (`CreateShipmentShellAsync`, `TransitionToCustomsPreparedAsync`
  — implemented by `ShipmentService` in `ShipmentService.International.cs`), `IShipmentItemLookupService`
  (`GetItemsAsync`, a narrow read projection for building `CustomsItem` rows), and
  `IShipmentTransitionService.GetResponseAsync` (a new method added to the existing transition
  interface so `InternationalShipmentQueryService.GetAsync` can merge in the full shipment detail
  without a second, module-crossing query) plus the existing `GetScanInfoAsync` (status and
  origin/destination/current branch for the PATCH/prepare-customs visibility and edit-freeze
  checks). This follows the same precedent
  `Manifests`/`Ndr`/`Riders` already use for driving real `Shipment` transitions.
- **The `Direction == Outbound` guard.** Nine `Shipments.Application.ShipmentService` methods each
  gain a leading check that rejects the call with `ShipmentInvalidTransitionException` when
  `shipment.Direction == ShipmentDirection.Outbound`: `AssignDeliveryToRiderAsync`,
  `CompleteDeliveryAsync`, `FailDeliveryAsync`, `TransitionToNdrCreatedAsync`,
  `ReattemptDeliveryAsync`, `InitiateRtoAsync`, `DispatchManyRtoAsync`, `ReceiveRtoAtOriginAsync`,
  `ConfirmRtoDeliveredAsync`. Eight of the nine call a shared `GuardAgainstOutboundInternationalAsync`
  helper at the top of the method; only `DispatchManyRtoAsync` (the sole bulk method in this list —
  `DispatchManyAsync`, the non-RTO bulk dispatch method, is not one of the nine and has no guard at
  all) inlines the same `Direction == Outbound` check per-shipment inside its loop instead, since it
  operates on a list of shipments rather than a single one. This is deliberately keyed on
  **`Direction`, not `Scope`** — these are
  exactly the methods that would otherwise let an outbound international shipment skip
  `CustomsPrepared` and fall straight into domestic last-mile/NDR/RTO statuses. Every earlier
  shared status (`PickupAssigned` through `AtDestinationBranch`, dispatch, manifest receive) is
  **not** guarded, because both `Outbound` and (once I4 ships `ReceivedFromCarrier`) `Inbound`
  shipments legitimately pass through those unchanged. `Inbound` is deliberately **not** guarded on
  the nine methods above either: an inbound shipment's last mile (from `AtDestinationBranch`
  onward) is supposed to use these same domestic methods unchanged once I4 lets it reach that
  status — in I3 this is simply unreachable for `Inbound`, since nothing yet drives an inbound
  shipment to `AtDestinationBranch`.
- **`ProvisionalTotalCharge`/`IsProvisionalPricing`.** No real pricing engine runs for international
  shipments (I5 doesn't exist yet). `InternationalLeg.ProvisionalTotalChargeAmount`/`Currency` is a
  staff-entered `Money` figure supplied on the create request, and `IsProvisionalPricing` is always
  `true` in this plan. `Shipment.DeliveryCharge`/`TotalCharge` are set to this same provisional
  amount (with `InsuranceCharge`/`OtherCharges` at zero) so existing shipment-charge fields aren't
  left blank, but nothing about them is a real carrier-cost-based quote. I5 replaces this with a
  real `PriceBreakdown`; this plan does not define how existing I3-era provisional shipments get
  reconciled once I5 ships.

### Known limitations

- No carrier integration: no carrier port/adapters, no handoff/receive scans, no tracking ingest.
  Every I3 shipment is a manually-entered shell.
- No real pricing: `ProvisionalTotalCharge` is staff-entered, not computed from any rate card, FX
  conversion, or carrier quote.
- No frontend and no merchant-facing API — the four routes above are staff-only and have no UI.
- `Inbound` shipments cannot progress past `Created` in this plan. `AssignPickupToRiderAsync`
  through `ReceiveAtDestinationAsync` are unguarded and would technically accept an `Inbound`
  shipment, but nothing in I3 creates the carrier-side event that would legitimately start that
  chain for an inbound parcel (that's I4's `ReceivedFromCarrier`), so in practice every `Inbound`
  shipment created today just sits at `Created`.
- The `GET /{id}` response's embedded `shipment` object reuses the existing `ShipmentResponse` DTO
  unchanged — it does not expose `Scope`, `Direction`, or the foreign-side `City`/`Region`/
  `PostalCode`/`CountryCode` fields. Only `RecipientAddressLine`/`SenderAddressLine` (the `Line1`
  reused column) are visible for the foreign party today; a caller must already know which side is
  foreign (from the request they sent) to interpret the response correctly.
