# International I1b — Notification-Template Locale

Parent: `2026-09-21-international-support-design.md` (§7 Localization, §8 step I1). Follows I0 (tenant `DefaultLocale`) and I1a (frontend i18n foundation).

## 1. Goal

Notification emails render in the organization's default language (`en` or `ne`), with English fallback. Organizations can override the body per locale.

## 2. Decisions

- **Locale source: tenant default.** Every notification uses the organization's `OrganizationSettings.DefaultLocale`. No per-recipient or per-event locale in this step.
- **Fallback order** prefers a locale-appropriate body over an org customization in the wrong language:
  1. Org active override for the resolved locale.
  2. Compiled default for the resolved locale.
  3. Org active `en` override.
  4. Compiled `en` default.
- The compiled `ne` defaults are machine-drafted and need native-speaker review (same caveat as I1a).

## 3. Data model

`NotificationTemplate` gains `Locale` (string, max 10, required, default `en`). Supported values: `en`, `ne`.

- The lookup index becomes `(OrganizationId, EventType, Channel, Locale)`; still non-unique, as before.
- Additive migration; existing rows are backfilled to `en`, so current overrides keep working.
- `NotificationLog` is unchanged.

## 4. Locale resolution

Notifications defines `IOrganizationLocaleLookup.GetDefaultLocaleAsync(organizationId)`. An adapter in `Courier.Api` (same pattern as `BranchPlanLimitAdapter`) reads Identity's `OrganizationSettings`. Notifications gains no project reference to Identity. Dispatch runs with no tenant context, so the adapter filters by the explicit `organizationId` and bypasses the ambient tenant filter. A missing or unsupported value resolves to `en`.

## 5. Defaults

`NotificationDefaultTemplates` becomes locale-keyed (`en`, `ne`), with all five events (`shipment.created`, `shipment.delivered`, `shipment.ndr_created`, `cod.collected`, `user.invited`) in both. A test asserts both locales cover the same event set and use the same placeholders.

## 6. API

`GET` and `PUT /api/v1/settings/notification-templates` (`notification_template.manage`):

- The DTO and request gain `locale`. It is optional on PUT and defaults to `en`.
- An unsupported locale returns 400 `NOTIFICATION_TEMPLATE_VALIDATION_FAILED`.
- Upsert matches on `(org, eventType, channel, locale)`.
- The audit record includes the locale.

## 7. Frontend

The existing panel and edit Sheet under `features/admin/settings/notification-templates` add a locale column and selector, and the Zod schema and types carry `locale`. Sheet driven by the existing Zustand store; shadcn components only. Loading and error states already exist and are kept.

## 8. Tests

- **Unit:** fallback chain (all four steps), locale validation, defaults parity.
- **Integration (DB):** tenant isolation across locales; migration backfill to `en`; upsert does not duplicate per locale.
- **API:** list/upsert contract, invalid locale, permission.
- **End to end:** a `ne` tenant's `shipment.created` email arrives in Nepali; an `en` tenant is unchanged.
- **Frontend:** panel and sheet with locale.

## 9. Out of scope

Per-recipient locale, translating the admin panel itself, and recording locale on `NotificationLog`.
