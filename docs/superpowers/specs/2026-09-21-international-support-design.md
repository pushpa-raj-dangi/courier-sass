# International Support — Design

Date: 2026-09-21
Status: Draft for review

## 1. Goal

Extend the Nepal domestic courier platform to book, track, price and reconcile **international shipments (outbound and inbound)** through partner carriers, and to support **localization** (English/Nepali UI, Bikram Sambat dates, multi-currency money, timezones).

The platform is the system of record for booking, customs data, tracking and finance. **Partner carriers own physical cross-border movement.** No own international hubs.

## 2. Decisions

| Area | Decision |
|---|---|
| Movement | Partner carriers only; our custody ends at a handoff scan and resumes at a receive scan |
| Direction | Outbound and inbound |
| Carriers | `ICarrierProvider` port; `DhlExpressProvider` + `ManualCarrierProvider` first |
| Pricing | Carrier quote → NPR via stored FX rate + markup + surcharges, snapshotted at booking |
| COD | International shipments are prepaid only; COD remains domestic-only (guard rejects COD on international) |
| Localization | `en` + `ne` UI, BS calendar optional, per-tenant currency/timezone |
| Structure | `Shipment` stays the single aggregate; new `International` module holds a one-to-one `InternationalLeg` |

Rejected: extending `Shipment` in place with all international fields (couples domestic and international rules); a separate `InternationalShipment` aggregate (duplicates tracking, custody, scanning, COD hooks, reports and the merchant API).

## 3. Decomposition

- **Sub-project A — Localization foundation** (`Money`, FX, tenant locale, i18n). Prerequisite for B.
- **Sub-project B — International shipping** (countries, customs, carriers, pricing, lifecycle, UI/API).

Each step below gets its own plan and implementation cycle.

## 4. Foundation data model (Sub-project A)

**Money.** `Money(decimal Amount, CurrencyCode Currency)` immutable value object; arithmetic only within the same currency (mismatch throws). `CurrencyCode` is validated ISO-4217. Every existing decimal amount gains a `*_currency` column backfilled to `NPR`; domestic behavior is unchanged.

**FX.** `FxRate(FromCurrency, ToCurrency, Rate, EffectiveAt, Source)`, append-only and versioned. Populated by a Hangfire job with manual entry as fallback; both go through one audited service. Every conversion records the `FxRate.Id` used; history is never recalculated.

**Tenant locale.** On `OrganizationSettings`: `BaseCurrency` (default NPR), `DefaultLocale` (`en`/`ne`), `TimeZone` (default `Asia/Kathmandu`), `CalendarPreference` (Gregorian/BS). Users may override locale and calendar.

**Geography.** New `Country` (ISO-3166 alpha-2, names en/ne, `IsServiced`, default currency, calling code) and `CountryZone` (groups countries for pricing/reporting). Foreign addresses use an `InternationalAddress` value object (`Line1`, `Line2`, `City`, `Region`, `PostalCode`, `CountryCode`); the normalized Nepal chain is unchanged.

**Migrations** are additive only (new tables, defaulted currency columns).

## 5. International module (Sub-project B)

### 5.1 Shipment changes (minimal)
- `Scope` (`Domestic | International`) and `Direction` (`Domestic | Outbound | Inbound`), set at creation, immutable.
- New statuses, reachable only when `Scope = International`:
  - Outbound: `AT_ORIGIN_BRANCH → SORTED → CUSTOMS_PREPARED → HANDED_TO_CARRIER → IN_INTERNATIONAL_TRANSIT → DELIVERED_ABROAD`; `CUSTOMS_HOLD` and `INTERNATIONAL_EXCEPTION` branch from `IN_INTERNATIONAL_TRANSIT`.
  - Inbound: `IN_INTERNATIONAL_TRANSIT → CUSTOMS_CLEARANCE → RECEIVED_FROM_CARRIER → AT_DESTINATION_BRANCH`, then the existing domestic last-mile flow.
- Guards: international statuses rejected on domestic shipments; COD rejected on international shipments. Every transition creates an immutable `ShipmentEvent`.

### 5.2 InternationalLeg (one-to-one by `ShipmentId`)
- `CustomsDeclaration`: contents, HS code per item, customs value (`Money`), country of origin, purpose, incoterm (`DDU`/`DDP`), export licence reference.
- `CarrierBooking`: `CarrierCode`, carrier AWB, service level, label reference (object storage), status, raw payload.
- `DutiesAndTaxes`: estimated/actual `Money`, payer, payment status.
- `PriceBreakdown`: carrier cost (carrier currency), FX rate ID, NPR equivalent, markup rule ID and amount, surcharges, total. Immutable after booking.

### 5.3 Cross-module contract
`Shipments` exposes `IShipmentInternationalGateway` (create shipment shell, apply transitions). `International` never touches shipment tables. `International` publishes domain events (`InternationalShipmentBooked`, `HandedToCarrier`, `CustomsHold`, …) consumed by notifications, webhooks and reports through the existing event pipeline.

### 5.4 Custody
Scan operation `CarrierHandoff` closes our custody with the carrier as custodian; `CarrierReceive` reopens it. Between scans the timeline shows carrier-reported events, labelled external-source.

### 5.5 Authorization
New permissions: `international.create`, `international.read`, `international.manage_customs`, `international.handoff`, `international.receive`. Branch-scoped for scans; tenant-scoped otherwise.

## 6. Carrier integration and pricing

**Port.** `ICarrierProvider`: `GetRatesAsync`, `BookAsync`, `GetLabelAsync`, `CancelAsync`, `GetTrackingAsync`, using platform types only. Adapters in `International.Infrastructure`; registry resolves by `CarrierCode`; per-tenant credentials encrypted at rest. `ManualCarrierProvider` records staff-entered carrier AWB and status updates.

**Resilience.** Polly retry/backoff, circuit breaker, timeout. Booking is idempotent via an idempotency key. A failed booking leaves the shipment in a pre-handoff state with a visible error; no half-booked state.

**Tracking ingest.** Signature-verified carrier webhooks where supported, Hangfire polling otherwise. Events map to platform statuses via a per-adapter mapping table and pass through the state machine; invalid events are logged and rejected. `CarrierTrackingEvent` is append-only, deduplicated by carrier event ID, with raw payloads kept.

**Pricing.**
1. `GetRatesAsync` → carrier quote in carrier currency.
2. Convert to NPR with the latest `FxRate` (ID recorded).
3. Apply `InternationalMarkupRule` (merchant → tenant → default; percentage, flat or both, with floor).
4. Add configurable surcharges as `AdditionalCharge` rows (fuel, customs handling, remote area).
5. Persist `PriceBreakdown` on booking.

Quotes expire after a short window (e.g. 15 minutes). The server re-quotes at confirm; price is never accepted from the client.

**Financials.** Merchant is invoiced in NPR. Carrier cost and duties paid on the merchant's behalf are recorded as `FinancialTransaction`s referencing the FX rate, so per-shipment margin is traceable. Manual adjustments are audited (user, reason, before/after).

## 7. API, frontend, localization

**API (`/api/v1`).** `POST /shipments` gains `scope`, `direction`, `destinationCountry`, `internationalAddress`, `customs`, `incoterm`. `POST /international/quotes`, `POST /international/shipments/{id}/book` (by quote ID), `GET /international/shipments/{id}`, `GET /countries`, `GET /hs-codes?q=`. Existing idempotency, rate limiting and API-key auth apply. New webhook events: `shipment.handed_to_carrier`, `shipment.customs_hold`, `shipment.customs_cleared`, `shipment.delivered_abroad`.

**Frontend.** `features/international/`; all detail/create views are right-side Sheets driven by Zustand stores; shadcn components only. Create flow: country → address → customs items (HS search) → quote/confirm. Detail sheet: merged timeline (carrier events labelled), customs, duties, label, manual-carrier status form. Ops: `CarrierHandoff`/`CarrierReceive` scan modes, customs-hold queue. Admin: FX rates, markup rules, carrier config, country/zone service list. Loading, error and empty states throughout; server enforces permissions.

**Localization.** `next-intl`, `en` + `ne` catalogs per feature folder; locale from user preference then tenant default. Shared `formatMoney/formatDate/formatNumber` in `lib/` honoring locale, timezone and calendar; BS conversion client-side with a tested library; API stays UTC/ISO. Public tracking gets a language switcher and, for international parcels, shows carrier events while hiding internal notes, fees and customs values. Notification templates gain a `Locale` dimension with English fallback. String extraction proceeds feature by feature; missing keys fall back to English.

## 8. Phasing

| Step | Scope |
|---|---|
| I0 | Money and FX: `Money`, currency columns + NPR backfill, `FxRate`, FX job, tenant locale settings |
| I1 | i18n foundation: `next-intl`, formatting utilities, BS calendar, `en`+`ne` shell, locale-aware notification templates |
| I2 | Countries and international address, service-list admin |
| I3 | International module core: `Scope`/`Direction`, statuses, `InternationalLeg`, customs, gateway, permissions, audit |
| I4 | Carrier layer: port, manual adapter, resilience, tracking ingest, handoff/receive scans |
| I5 | Pricing: markup rules, surcharges, quote/confirm, `PriceBreakdown`, `FinancialTransaction`s |
| I6 | DHL Express adapter: rates, booking, label, tracking |
| I7 | Frontend, merchant API, webhooks, localized public tracking, remaining i18n extraction |
| I8 | Reports: volume by country, carrier cost vs charged, customs holds, margin |

I1, I2 and I3 may run in parallel once I0 is done.

## 9. Security and compliance

- Carrier credentials encrypted at rest; masked in logs and UI.
- Customs values and recipient personal data excluded from public tracking.
- Restricted/prohibited-item rule list (configurable data) blocks booking on match.
- `Country.IsServiced` enforced server-side for sanctioned/unserviced destinations.
- Tenant isolation tested on every new table; cross-tenant ID-manipulation test on every new endpoint.

## 10. Testing

- **Unit:** `Money`, FX, markup, quote expiry; state transitions both directions incl. rejected ones; COD-on-international guard.
- **Integration:** domestic regression after the currency migration; outbound and inbound lifecycle with a fake carrier; tenant/branch isolation; idempotent booking; pricing snapshot immutability.
- **API:** auth, authorization, validation, error contracts.
- **Adapter:** contract tests against recorded DHL fixtures.

## 11. Risks

- DHL account/sandbox access needed before I6; manual adapter unblocks everything else.
- Currency backfill touches many tables; I0 gets a full regression run.
- Nepal export/import rules need business input; HS-code and restricted-goods data are seeded configuration, not hardcoded.
- BS calendar accuracy; use a tested library with boundary-date tests.

## 12. Out of scope

International COD, own international hubs, additional carriers beyond DHL, customs brokerage filing, route/ETA optimization (Phase 14).

## 13. Docs to update

`DOMAIN-MODEL.md`, `SHIPMENT-LIFECYCLE.md`, `API-SPEC.md`, `RBAC.md`, `ROADMAP.md`; add `INTERNATIONAL.md`.
