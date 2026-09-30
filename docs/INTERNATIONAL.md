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
| I4 | Carrier layer (booking, handoff/receive, tracking ingest) | Implemented — see below |
| I5–I8 | See the design spec §8 | Planned |

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

## Carrier layer (I4)

Design: `docs/superpowers/specs/2026-09-24-international-i4-carrier-layer-design.md`. Everything
below lives in the `International` module (`Application`/`Domain`/`Infrastructure`/`Presentation`);
`Shipments` stays the sole owner of the state machine, custody and `ShipmentEvent`s. Seven new
`ShipmentStatus`/`ShipmentEventType` values were appended (ordinal-append-only) after
`CustomsPrepared`: `HandedToCarrier`, `InInternationalTransit`, `DeliveredAbroad`, `CustomsHold`,
`InternationalException`, `CustomsClearance`, `ReceivedFromCarrier`.

### Transitions (`InternationalTransitionRules`)

`Shipments.Domain.InternationalTransitionRules` is the single table for every carrier-driven and
staff-override move (see `SHIPMENT-LIFECYCLE.md` for the full table). Two triggers:
`Carrier` (every listed row) and `StaffOverride` (only the rows out of `CustomsHold`/
`InternationalException`, marked `StaffOverridable`). Handoff (`CustomsPrepared → HandedToCarrier`)
and receive (`→ ReceivedFromCarrier`) are separate scan operations with their own gateway methods
and are **not** in this table.

`IShipmentInternationalGateway` gained four methods, all implemented in
`ShipmentService.InternationalLifecycle.cs` with their own
`StageInternationalTransition`/`SaveInternationalTransitionAsync` pair — **not**
`TransitionAsync`, and **no outbox row is written** (the design spec originally claimed transitions
get the outbox "for free"; that was wrong and has been corrected there):

- `HandToCarrierAsync` — `CustomsPrepared → HandedToCarrier`; requires the operator to have branch
  access to the shipment's current branch; clears `CurrentBranchId`/`CurrentCustodianUserId`/
  `CurrentCustodianRiderId`.
- `ReceiveFromCarrierAsync` — from `InInternationalTransit`/`CustomsClearance` to
  `ReceivedFromCarrier`; requires branch access to `OriginBranchId`; sets
  `CurrentBranchId = OriginBranchId`, `CurrentCustodianUserId` = the scanning user. **Same-branch
  inbound receive:** when `OriginBranchId == DestinationBranchId`, the same call stages a *second*
  transition (`ReceivedFromCarrier → AtDestinationBranch`, event type `ReceivedAtDestination`,
  timestamped 1 ms later so timelines order correctly) into the same `SaveChangesAsync` — one scan,
  two immutable `ShipmentEvent`s. Otherwise the shipment stays at `ReceivedFromCarrier` for the
  normal sort/manifest flow.
- `ApplyCarrierStatusAsync` — carrier-driven, no user; the `ShipmentEvent.Timestamp` is the
  carrier's `OccurredAt`, **clamped to 1 ms after the shipment's latest existing event** when the
  carrier time is not later (carrier lag, a late handoff scan, a backdated manual entry) so the
  timeline never goes out of order; the carrier's own time is always kept in the event metadata as
  `carrierOccurredAt`. Already-at-target is a no-op
  (`AlreadyAtStatus`); any other invalid move throws `ShipmentInvalidTransitionException`.
- `OverrideInternationalStatusAsync` — staff override with a required non-empty reason (≤2000
  chars, the `ShipmentEvent.Notes` limit); audited with before/after status and reason.

**Inbound sort from `ReceivedFromCarrier`.** `ShipmentService.SortAsync` picks its expected
from-status dynamically: `ReceivedFromCarrier` when the shipment is `Direction == Inbound` and
currently at that status, otherwise the usual `AtOriginBranch`. `ManifestService.ScanAsync` (the
only caller, owner of the branch/expected-status checks) accepts the same inbound exception. No
other transition method changes.

**Concurrency.** A save that hits an xmin conflict throws `ShipmentConcurrencyException` (derives
from `ShipmentInvalidTransitionException`, itself from `ShipmentValidationException`) instead of
the plain concurrency exception `TransitionAsync` uses. `CarrierEventApplier` catches it ahead of
`ShipmentValidationException` and treats it as **retryable** (`Pending`, counts toward the 5-attempt
cap) rather than `Rejected` — a lost race to a concurrent update is not a business-rule violation.

### Custody

- **Handoff:** clears `CurrentBranchId`/`CurrentCustodianUserId`/`CurrentCustodianRiderId` as part
  of the shipment transition; in the same DB transaction,
  `InternationalCarrierLifecycleService.HandoffAsync` stamps `CarrierBooking.CustodyStartedAt`
  through a **conditional `ExecuteUpdate`** guarded on the booking still being `Booked`. If a
  concurrent `booking/cancel` cancelled it first, the conditional update affects zero rows, a
  validation exception is thrown, and the whole transaction — the shipment transition included —
  rolls back. While `HandedToCarrier` or later, the carrier is the custodian (derived from status +
  the active booking; `Shipment` has no carrier-account column).
- **Receive:** `CurrentBranchId` = the receiving (origin) branch, `CurrentCustodianUserId` = the
  scanning user.

### Ingest — store then process

Every source (webhook, poll, manual) writes through the single `CarrierTrackingEventWriter.InsertAsync`,
which deduplicates by `(CarrierAccountId, CarrierEventId)`: an in-batch de-dup, a lookup against
already-stored rows, and — because the webhook path has no ambient transaction serializing it
against a concurrent poll — a fallback to inserting one row at a time on a
`DbUpdateException`. Only a genuine Postgres unique-index violation (`PostgresException.SqlState ==
"23505"`, `PostgresErrorCodes.UniqueViolation`) is treated as an expected duplicate and dropped
silently; any other `DbUpdateException` on that single-row retry is logged before the row is
dropped, never silently swallowed. `International.csproj` references `Npgsql` directly
(Application layer, not just Infrastructure) so this check can compare `SqlState` precisely instead
of a broad `catch (DbUpdateException)`.

- **Webhook** — `POST /api/v1/carrier-webhooks/{webhookPublicId}`, anonymous
  (`AllowAnonymous().RequireIpRateLimit()`). Resolves the `CarrierAccount` by `WebhookPublicId` via
  one `IgnoreQueryFilters()` lookup (an anonymous request has no tenant); unknown, inactive, or an
  account with no webhook secret → 404; a provider without webhook support → 404. Verifies the
  signature via `provider.VerifyWebhook` — **the constant-time comparison is each
  `ICarrierProvider`'s own responsibility**; there is no shared helper in the carrier port or its
  infrastructure. The only implementation exercised today is the test-only
  `FakeHttpCarrierProvider` (`CryptographicOperations.FixedTimeEquals`); a real adapter (I6) must
  implement its own. Bad signature → 401, logged, nothing stored; unparseable body with a valid
  signature → 400 `CARRIER_WEBHOOK_INVALID_PAYLOAD`, nothing stored. Body capped at 1 MB
  (`CarrierWebhookEndpoints.MaxBodyBytes`), checked against both `Content-Length` and the actual
  bytes read; over the cap → 413. **This route shares the same per-IP rate-limit bucket as public
  tracking** — `RequireIpRateLimit()` uses the same config key
  (`RateLimiting:PublicTrackingRequestsPerMinute`, default 30/minute) and the same `ip:{address}`
  partition key, so a carrier's webhook calls and public tracking hits from the same IP draw down
  the same counter.
- **Polling** — Hangfire recurring job `carrier-tracking-poll` (every 15 minutes,
  `CarrierTrackingPollJob.RunAsync`), plus an immediate per-booking enqueue
  (`PollBookingAsync`) after an inbound booking is linked. Covers `Booked` bookings created in the
  last 60 days (`MaxBookingAge`) on an active, polling-enabled account, capped at 50 bookings per
  account per run. **Failures are isolated per booking:** one tenant's booking failing (bad
  ciphertext, an unhandled provider exception) is caught, logged, and the run moves on to the next
  booking and tenant — it never aborts the whole run. A `CancellationToken` cancellation
  (`cancellationToken.IsCancellationRequested`) is deliberately **not** swallowed by that same
  catch filter, so a real shutdown/timeout still propagates. `CarrierTrackingWindow.IsTracked`
  gates which statuses are still worth polling (outbound: `HandedToCarrier` through
  `InternationalException`; inbound: `Created` through `InternationalException`) — a terminal
  shipment (`DeliveredAbroad`, `Delivered`, `Cancelled`, or inbound past `ReceivedFromCarrier`) is
  skipped.
- **Manual** — `POST /{id}/carrier-events` (`international.tracking.manage`). Requires a `Booked`
  booking; the status code must be in that provider's vocabulary (`IsKnownStatusCode`);
  `OccurredAt` cannot be more than 5 minutes in the future, and for an outbound booking cannot be
  earlier than the handoff (`CustodyStartedAt`, minus the same 5-minute skew) — inbound has no such
  floor, since the foreign carrier's scans precede our own records. Inserted with `Source = Manual`,
  `EnteredByUserId`, audited (`carrier_event.manual_recorded`).

### Processing (`CarrierEventProcessorJob` / `CarrierEventApplier`)

Hangfire recurring job `carrier-event-processor` (every 30 s) plus an immediate enqueue after every
insert. **A Postgres session advisory lock serializes whole runs:** `RunAsync` calls
`pg_try_advisory_lock` on a fixed key before touching any event; a run that finds the lock already
held (another run — recurring, enqueued, or a test's direct call — is mid-batch) **returns
immediately without processing anything**. This exists because the per-event claim alone isn't
enough — two overlapping runs could each claim a *different* event for the *same* shipment and
apply them out of order, or race on the same `Shipment` row. The per-event atomic claim
(`Pending → Processing`, via `ExecuteUpdateAsync`) stays as defense in depth (e.g. a run holding the
lock that's killed mid-batch still leaves its claims to be reclaimed).

- **Stale-claim reclaim:** any row still `Processing` after 5 minutes (`StaleClaimAge`) is reset to
  `Pending` at the start of each run, before new candidates are selected.
- **Retry cap:** `CarrierEventApplier.MaxAttempts = 5`. An unexpected exception (not a state-machine
  rejection) leaves the event `Pending` with a note; once `ProcessingAttempts >= 5` it is instead
  marked `Rejected` with a "gave up after N attempts" note.
- **Matching:** by `(CarrierAccountId, CarrierAwb)` against a **live `Booked`** booking only — never
  by AWB alone (so the same AWB used by two tenants' accounts can never cross-match). No matching
  booking → `Unmatched` (kept, not retried on its own — see requeue below).
- **`Unmatched` re-queue:** when a booking is created or linked (`CarrierBookingService`'s
  `CompleteBookingAsync`/`LinkInboundAsync`), any `Unmatched` event already stored for that
  `(CarrierAccountId, CarrierAwb)` is reset to `Pending` and re-enqueued — an event that arrived
  before its booking existed (e.g. an early webhook) gets another chance once the booking shows up.
- **Mapping:** `provider.MapStatus(code, direction)`; `null` → `Ignored` (informational, shown on
  the timeline).
- **Applying:** `IShipmentInternationalGateway.ApplyCarrierStatusAsync`. Already at target →
  `Ignored`; `ShipmentConcurrencyException` → `Pending` (retryable, see Concurrency above); any
  other `ShipmentValidationException` (including `ShipmentInvalidTransitionException` — a stale or
  out-of-order event) → `Rejected`; success → `Applied`.
- Events within one organization's batch are processed **in `OccurredAt` order** (then `ReceivedAt`),
  so an out-of-order delivery (e.g. `DELIVERED` inserted before `TRANSIT`) still reaches the right
  end state.
- **A carrier event that arrives before the handoff scan** *does* match its `Booked` outbound
  booking (booking happens before handoff), but the shipment is still `CustomsPrepared` — not a
  valid `Carrier`-trigger from-status in `InternationalTransitionRules` — so
  `ApplyCarrierStatusAsync` throws `ShipmentInvalidTransitionException` and the event ends
  `Rejected`. Unlike `Unmatched`, a `Rejected` event is **never automatically re-evaluated**
  (`RequeueUnmatchedAsync` only resets `Unmatched` rows); it stays `Rejected` even after the
  shipment is later handed off.

**Known limitation carried into production:** `InternationalTransitionRules` allows
`CustomsHold → InInternationalTransit` unconditionally for the `Carrier` trigger (not gated on
event recency), so **a late, older carrier event can still release a `CustomsHold`** even if the
hold is still real at the carrier — there is no check that a carrier event is newer than the
shipment's current status before applying a hold-release.

### Background tenant scope

`CarrierEventProcessorJob` and `CarrierTrackingPollJob` run outside any HTTP request, but the
tenant-filtered application services they call (`CarrierEventApplier`, `CarrierBookingPoller`, the
gateway, every EF Core query filter) need an organization context. `ITenantScopeFactory.CreateScope
(organizationId)` (`Courier.SharedKernel`) opens a new DI scope and sets its scoped
`TenantScopeOverride.OrganizationId`; `HttpContextCurrentTenantProvider` checks that override
*first*, before falling back to the request's `org_id` JWT claim. No request path ever sets the
override, so ordinary HTTP requests are unaffected. Both jobs group their cross-tenant candidate
rows (queried with `IgnoreQueryFilters()`, the same deliberate pattern as `OutboxPublisherJob`) by
`OrganizationId` and open one tenant scope per organization per run.

### Secrets

Credentials and the webhook secret are encrypted with ASP.NET Data Protection under two distinct
purpose strings (`International.CarrierAccount.Credentials`,
`International.CarrierAccount.WebhookSecret` — `DataProtectionCarrierSecretProtector`), so
credential ciphertext can never be decrypted as a webhook secret or vice versa. Decrypted only
inside `CarrierAccountSecrets`/the secret protector, immediately before a provider call. Never
returned by the API except the one-time secret in the create/rotate responses
(`CarrierAccountSecretResponse`); every other account response exposes only `hasCredentials`/
`hasWebhookSecret` booleans. `RawResponse`/`RawPayload` are masked before storage
(`CarrierPayloadSanitizer`) using every known secret value (decrypted credentials plus the webhook
secret) so a stored payload can't leak a secret even if a carrier happens to echo it back.

### Resilience and error mapping

`AddCarrierHttpClient<TClient, TImplementation>` wires a standard `Microsoft.Extensions.Http.Resilience`
pipeline per typed carrier `HttpClient`: total timeout 30 s → retry (3 attempts, exponential
backoff with jitter, on 5xx/408/429/`HttpRequestException`/timeouts) → circuit breaker (50% failure
ratio over a 30 s window, minimum throughput 10, 30 s break) → per-attempt timeout 10 s — exactly
the spec §5.9 defaults; tests shrink these via `CarrierResilienceOptions`.
`InternationalEndpointErrors.ExecuteAsync` is the one exception → HTTP mapping for every I4 route:
`CarrierBookingFailedException` → `502 CARRIER_BOOKING_FAILED`; `CarrierUnavailableException`
(circuit open) → `503 CARRIER_UNAVAILABLE`; `InternationalShipmentValidationException` → `400
INTERNATIONAL_SHIPMENT_VALIDATION_FAILED`; `CarrierAccountValidationException` → `400
CARRIER_ACCOUNT_VALIDATION_FAILED`; `ShipmentInvalidTransitionException` → `400
SHIPMENT_INVALID_TRANSITION` (with `{shipmentId, from, to}` detail); any other
`ShipmentValidationException` → `400 SHIPMENT_VALIDATION_FAILED`.

### Booking (`CarrierBookingService`)

`POST /{id}/book` (`international.book`, `Idempotency-Key` header required, ≤200 chars):

1. Validates tenant/branch visibility, `Direction == Outbound`, status `CustomsPrepared`, the
   account is active and in-tenant, and (for a manual-AWB carrier) that the supplied AWB is free —
   all before any write.
2. **Idempotent replay:** the same key always returns the same outcome (`Failed` → re-throws the
   same 502; `Pending` and not stale → "still in progress"; resolved → the stored response) without
   ever calling the carrier twice — *unless* the replayed booking is `Pending` and older than
   `StalePendingThreshold` (2 minutes), in which case it falls through to the resume path below.
3. **Stale-`Pending` resume:** if the leg already has a live booking (`Pending` or `Booked`) —
   found either by the idempotency-key replay lookup or by a fresh `/book` call using a *different*
   key — a `Booked` one is rejected outright ("cancel it first"); a `Pending` one younger than 2
   minutes is rejected as "still in progress"; a `Pending` one **at least 2 minutes old** is
   resumed: the row is claimed atomically (compare-and-set on `UpdatedAt`, so two concurrent resume
   attempts can never both proceed), then the carrier is called again using the **original stale
   booking's own stored `IdempotencyKey` and `ServiceLevel`** — never the new request's — since the
   original attempt may already have reached the carrier.
4. **Carrier call runs with `CancellationToken.None`** once the `Pending` row is committed — a
   client disconnect must never abandon a booking that may already be in flight at the carrier; the
   resilience pipeline enforces its own timeout independently.
5. On success, the carrier's own AWB, service level and raw response are persisted via a
   **conditional `ExecuteUpdate` guarded on the row still being `Pending`**
   (`TryMarkBookedIfPendingAsync`) — so a losing concurrent attempt (or a fully-resolved row) can
   never be overwritten. **The AWB is persisted before any label upload is attempted** — a label
   upload failure is logged and leaves the booking `Booked` with no stored label, it never fails
   the booking itself or gets retried. On failure, the same conditional-update pattern
   (`TryMarkFailedIfPendingAsync`) marks the booking `Failed` + `FailureReason`, returning `502
   CARRIER_BOOKING_FAILED` (or `503 CARRIER_UNAVAILABLE` if the circuit is open); the shipment
   itself is untouched (`CustomsPrepared`) either way, and a new booking may be attempted.
6. Any `Unmatched` carrier events already stored for the resulting `(CarrierAccountId, CarrierAwb)`
   are requeued, and the booking is audited (`carrier_booking.booked`/`carrier_booking.failed`).

`CarrierBooking.LabelContentType` was added by its own later migration
(`AddCarrierBookingLabelContentType`), separate from the initial `AddCarrierLayer` migration that
created the three new tables.

`POST /{id}/booking/cancel` (`international.book`) — only while `Booked` and the shipment hasn't
been handed over; calls `CancelAsync` first, then marks `Cancelled` on success.

`POST /{id}/inbound-booking` (`international.book`) — only `Direction == Inbound` at `Created` with
no live booking; creates a `Booked` booking directly (no carrier call, since the foreign carrier
already assigned the AWB); if the account is polling-enabled, enqueues an immediate poll.

### Carrier account management

`/api/v1/international/carrier-accounts` (`international.carrier.manage`): `GET /`, `GET /{id}`,
`POST /` (create — returns the one-time webhook secret if generated), `PUT /{id}` (display name,
`IsActive`, `PollingEnabled` — covers both deactivation and reactivation; there is no separate
`deactivate` route), `POST /{id}/credentials` (replace), `POST /{id}/webhook-secret/rotate`
(returns the new secret once). `GET /{id}/booking/label` (`international.read`) streams the live
booking's stored label. Account responses always include the webhook URL
(`/api/v1/carrier-webhooks/{webhookPublicId}`) and `hasCredentials`/`hasWebhookSecret` booleans,
never the secrets themselves.

### Known limitations

- No real carrier adapter yet — `ManualCarrierProvider` (staff-entered AWB, no network call, no
  labels/webhooks/polling) is the only production adapter; DHL Express is I6.
- No pricing/`GetRatesAsync` — carrier rate quoting is I5.
- No frontend, no merchant API, no merchant-facing webhook event types or notifications for the
  carrier layer — all I4 routes are staff-only; I7 covers the merchant-facing surface.
- International returns are not supported — `CustomsHold`/`InternationalException` exit back into
  the forward lifecycle only (via carrier events or staff override); there is no RTO-equivalent
  path for an international shipment.
- Existing tenants' roles are **not** backfilled with the three new permissions
  (`international.carrier.manage`, `international.book`, `international.tracking.manage`) or the
  now-wider `international.handoff`/`international.receive` grants — `AdditionalRoleSeeds` and the
  `OrganizationOwner` all-permissions grant only run at organization registration
  (`AuthService.RegisterOrganizationAsync`); an organization created before I4 shipped keeps its
  pre-I4 `RolePermission` rows.
- A carrier event that arrives before the shipment has actually been handed off ends `Rejected`
  (the booking matches, but `CustomsPrepared` isn't a valid carrier-trigger from-status) and is
  **never automatically re-evaluated** — see Processing above. A carrier event that arrives before
  any booking exists at all ends `Unmatched` instead, and *is* re-evaluated, but only when a new
  booking is created or linked for that exact `(CarrierAccountId, CarrierAwb)` — not on a generic
  retry loop.
- Polling only covers bookings created in the last 60 days (`CarrierTrackingPollJob.MaxBookingAge`)
  — an older `Booked` booking that somehow still needs tracking would need a manual carrier event
  or a status override instead.
- Webhooks share the same per-IP rate-limit bucket as public tracking (see Ingest above) — a
  high-volume carrier webhook source behind a shared/NAT'd IP could be throttled by unrelated public
  tracking traffic from the same address, or vice versa.
- The constant-time webhook signature comparison is left to each `ICarrierProvider`'s own
  `VerifyWebhook` implementation; there is no shared helper, so a careless future adapter could
  introduce a timing side-channel.
- `InternationalTransitionRules` allows `CustomsHold → InInternationalTransit` unconditionally for
  carrier-triggered events, so a stale/out-of-order carrier event can release a `CustomsHold` that
  is still real at the carrier (see Processing above).
