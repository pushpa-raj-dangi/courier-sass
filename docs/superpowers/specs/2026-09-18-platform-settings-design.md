# Design: Platform Settings (SuperAdmin-managed config, live sync)

**Date:** 2026-09-18
**Status:** Approved for planning
**Track:** Platform module (backend), platform-settings feature (frontend)

## 1. Problem

Every piece of runtime configuration today — eSewa/Khalti gateway keys, SMTP host, JWT
signing key, rate-limit thresholds, the frontend base URL, the billing grace period — lives
only in `appsettings.{Environment}.json` / Docker Compose env vars, bound once at process
startup (mostly via `services.Configure<T>(...)` → `IOptions<T>`, some read directly off
`IConfiguration` per call). Changing any of these values today requires editing a file and
restarting/redeploying the process. There is no way for a SuperAdmin to change, audit, or even
*see* the current value of these settings from the Platform UI (`src/Modules/Platform/`,
`src/web/src/app/platform/`), which already covers Organizations, Plans, Invoices, Jobs,
Queues, and Audit but has no Settings surface.

Separately, `OrganizationSettings` (`src/Modules/Identity/Domain/OrganizationSettings.cs`,
[[2026-09-13-tenant-admin-settings-design]]) already gives each *tenant* a DB-backed,
audited settings row. This spec builds the equivalent for *platform-wide* config — a distinct,
smaller table, not an extension of that one.

## 2. Goals

A user holding `platform.settings.read` / `platform.settings.update` gets a **Settings** page
under `/platform/settings` (SuperAdmin only, alongside Organizations/Plans/Jobs) that can view
and edit, live, without a restart:

1. **Payment gateways** — eSewa (`ProductCode`, `SecretKey`, `FormUrl`, `StatusCheckUrl`),
   Khalti (`SecretKey`, `InitiateUrl`, `LookupUrl`, `WebsiteUrl`).
2. **Notifications** — SMTP `Host`, `Port`, `From`.
3. **Security & auth** — JWT `SigningKey`, `Issuer`, `Audience`, `AccessTokenMinutes`;
   rate-limit thresholds `RateLimiting:ApiKeyRequestsPerMinute`,
   `RateLimiting:PublicTrackingRequestsPerMinute`.
4. **Branding & misc** — `Frontend:BaseUrl`, `Billing:GraceDays`, plus a new
   `PlatformFlags` bucket for future boolean feature flags (empty catalog at ship time, just
   the plumbing).

Changing a value takes effect across every running process (API + Hangfire worker, and any
future API replica) within roughly a second, with no restart, via a DB-backed override layered
on top of `IConfiguration` and a Redis pub/sub reload signal. Every write is audited
(`User, Action, Entity=PlatformSetting, EntityId=Key, Before, After, IP, UserAgent, Timestamp`)
per CLAUDE.md §23/§28, with secret values redacted in the audit record itself.

## 3. Non-Goals

* **Org-level settings management from this screen.** `OrganizationSettings` stays exactly as
  it is — org-admin-managed, untouched by this work. This spec is platform-wide only.
* **Bootstrap connection strings / `ASPNETCORE_ENVIRONMENT`.** `ConnectionStrings:Postgres`
  (etc.) can never be DB-managed — the app has to already be connected to Postgres before it
  can read a `PlatformSetting` row. These remain appsettings/env-only, full stop.
* **Rewriting `.env` / `docker-compose.yml` files or recreating containers.** Out of scope
  entirely — this is a DB-override mechanism, not infra automation.
* **Multi-environment settings promotion** (e.g. "copy staging settings to prod"). Each
  deployment's Postgres is independent; no cross-environment sync.
* **SMTP authentication (username/password).** The current `EmailNotificationChannel`
  (`src/Modules/Notifications/Application/EmailNotificationChannel.cs`) connects with no auth
  (dev mail-catcher today; a real relay tomorrow would need this) — adding SMTP auth fields is
  a separate future change, not bundled here. Only the three fields that exist today
  (`Host`, `Port`, `From`) are covered.
* **A generic/admin-configurable catalog editor.** The set of manageable keys
  (`PlatformSettingDefinition` rows) is seeded in code via an EF Core migration, not
  user-creatable through the UI — SuperAdmin edits *values*, not the catalog of what's
  editable.

## 4. Data model

New tables, Platform module (`src/Modules/Platform/Domain/`), no relation to
`OrganizationSettings`:

```csharp
// PlatformSettingDefinition.cs — seeded via migration HasData, not user-editable
public class PlatformSettingDefinition
{
    public string Key { get; set; } = default!;          // e.g. "Esewa.SecretKey"
    public string Category { get; set; } = default!;      // PaymentGateway | Notifications | Security | Branding
    public string ConfigPath { get; set; } = default!;    // e.g. "Esewa:SecretKey" — IConfiguration key it overrides
    public string ValueType { get; set; } = default!;     // String | Int | Bool
    public bool IsSecret { get; set; }
    public bool RequiresConfirmation { get; set; }         // true only for Jwt.SigningKey
    public string? Description { get; set; }               // shown in UI as help text
}

// PlatformSetting.cs — one row per key, created lazily on first override
public class PlatformSetting
{
    public string Key { get; set; } = default!;    // PK, FK -> PlatformSettingDefinition.Key
    public string? Value { get; set; }              // encrypted (IDataProtector) when Definition.IsSecret
    public bool IsOverridden { get; set; }           // false = "not set, fall back to appsettings"
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }
}
```

**Fallback chain per key, resolved by the config provider (§5):**
`PlatformSetting.Value` (if `IsOverridden`) → `appsettings.{Environment}.json` →
`appsettings.json`. An empty `platform_settings` table (day one, or after a `reset`) reproduces
today's behavior exactly — nothing changes until a SuperAdmin explicitly sets a value.

EF Core migration: `AddPlatformSettings` — creates both tables, seeds
`PlatformSettingDefinition` rows for the ~14 keys in Goals §2 via `HasData`. No backfill of
`PlatformSetting` itself (all rows start `IsOverridden = false`).

## 5. Backend components

`src/Modules/Platform/Infrastructure/Configuration/`:

* **`PlatformSettingsConfigurationSource` / `PlatformSettingsConfigurationProvider`** —
  `IConfigurationProvider` that, on `Load()`, queries all `IsOverridden = true` rows (via a
  scoped `IServiceScopeFactory`, since config loading happens outside a request scope) and
  sets each `ConfigPath` in the provider's data dictionary, decrypting `IsSecret` values first.
  Registered in `Program.cs` via `builder.Configuration.Add(new PlatformSettingsConfigurationSource(...))`
  **after** `AddJsonFile("appsettings.json")`/`AddJsonFile("appsettings.{env}.json")`/
  `AddEnvironmentVariables()`, so DB values win when `IsOverridden = true`, matching the
  fallback chain in §4. Exposes `Reload()`, which re-runs `Load()` and fires .NET's built-in
  configuration change-token — this is the same mechanism `IOptionsMonitor<T>` already listens
  to, so no custom pub/sub-to-options wiring is needed on the consumer side.
* **`PlatformSettingsChangeListener`** — a `BackgroundService` subscribed to Redis channel
  `platform-settings:changed`. On message, resolves the registered
  `PlatformSettingsConfigurationProvider` instance from `IConfigurationRoot.Providers` and
  calls `Reload()`.
* **`Application/PlatformSettingsService.cs`** — `GetAllAsync()` (grouped by category, secret
  values masked), `UpdateAsync(key, value, confirmationPhrase?, userId)`:
  1. Look up `PlatformSettingDefinition`; 404 if unknown key.
  2. Type-validate `value` against `ValueType` (reject non-numeric for an `Int` key, etc.).
  3. If `Definition.RequiresConfirmation`, require `confirmationPhrase == "REVOKE ALL SESSIONS"`
     (checked server-side — the API contract enforces this even if called directly, not just
     the UI) or reject with a 400.
  4. Encrypt via `IDataProtector` (purpose string `"PlatformSettings.Secrets"`) when
     `Definition.IsSecret`.
  5. Upsert `PlatformSetting`, write the audit record (`Before`/`After` = `"[REDACTED]"` for
     secrets, per CLAUDE.md's sensitive-data-masking rule), publish to Redis, `Reload()`
     locally so the same process sees its own change immediately.
  `ResetAsync(key, userId)` — sets `IsOverridden = false`, same audit/publish/reload path.
* **`Presentation/PlatformSettingsEndpoints.cs`** — `GET /api/platform/settings`,
  `PUT /api/platform/settings/{key}`, `POST /api/platform/settings/{key}/reset`. Same
  SuperAdmin-only authorization pattern as `PlatformPlansEndpoints.cs`.

**Consumer changes — three real cases, not six:**

Checked each of the 14 keys' actual consumers before assuming a blanket "swap every `IOptions<T>`" rule:

* **`RateLimiting:*` and `Smtp:*`, `Frontend:BaseUrl`** already read straight off the injected
  `IConfiguration` per call (`RateLimitFilters.cs:40`, `EmailNotificationChannel.cs:23-25`,
  `UserManagementService.cs:233`) — these become live automatically once the provider is
  registered. **No consumer code changes needed for these.**
* **`EsewaOptions`, `KhaltiOptions`, `BillingOptions`** are bound via `Configure<T>()` and
  injected as `IOptions<T>` (snapshotted once per DI resolution scope, not live). Consumers
  swap `IOptions<T>` → `IOptionsMonitor<T>` and `.Value` → `.CurrentValue`.
* **`JwtOptions` needs a real fix, not just a swap.** `Program.cs:271` currently builds
  `TokenValidationParameters.IssuerSigningKey` **once**, at startup, directly from
  `jwtSection["SigningKey"]` — a plain string capture, nothing live about it at all. Naively
  rotating the DB-backed key would desync signing (new tokens signed with the new key) from
  validation (still checking against the old fixed key) and break auth entirely, not just log
  everyone out as intended. Fix: set
  `TokenValidationParameters.IssuerSigningKeyResolver = (token, secToken, kid, parameters) =>
  new[] { new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptionsMonitor.CurrentValue.SigningKey)) }`
  so validation reads the live value per-request. Token *issuance* (wherever `AuthService`
  signs a new JWT) swaps `IOptions<JwtOptions>` → `IOptionsMonitor<JwtOptions>` the same as
  Esewa/Khalti. `MerchantJwtOptions` (bound to the same `"Jwt"` section,
  `Program.cs:139`) needs the identical resolver fix wherever merchant tokens are validated.

**Security:**
* New permissions `platform.settings.read`, `platform.settings.update`, SuperAdmin-role-only,
  enforced identically to existing Platform endpoints.
* Secret values are write-only from the API's perspective — `GET` never returns a decryptable
  value, only a masked placeholder (e.g. last 4 chars) and `IsOverridden`/`UpdatedAt`/
  `UpdatedByUserId`. There is no "reveal" endpoint.

## 6. Frontend

`src/web/src/features/platform-settings/` + `src/web/src/app/platform/settings/page.tsx`,
following the `platform-plans` feature's structure:

* `hooks/usePlatformSettings.ts` (TanStack Query) — fetch grouped-by-category list; mutation
  hooks for update/reset with cache invalidation.
* Page renders four tabs (shadcn `Tabs`) — Payment Gateways, Notifications, Security, Branding
  & Flags — each field as shadcn `Input`/`Switch` sized to `ValueType`, with a per-field
  "Save" (optimistic mutation) and "Reset to default" action. Secret fields render masked with
  a "Replace" affordance (a fresh `Input` appears to type a new value; never pre-filled).
* `RequiresConfirmation` fields (JWT signing key) route the save through a shadcn
  `AlertDialog` requiring the exact phrase `REVOKE ALL SESSIONS` to be typed before the
  mutation fires, with a clear one-sentence warning about platform-wide logout.
* Per [[feedback_courier_sheet_over_pages]] this stays a routed page (not a Sheet) — it's a
  standing settings surface, not a create/detail record, consistent with how
  `platform-plans`/`platform-organizations` are already routed pages rather than Sheets.

## 7. Testing

* **Unit** — `PlatformSettingsService`: type validation per `ValueType`, confirmation-phrase
  enforcement, encrypt/decrypt round-trip, fallback-to-appsettings resolution when
  `IsOverridden = false`.
* **Integration** — `PlatformSettingsConfigurationProvider`: DB value overrides appsettings
  value; `Reload()` after a simulated Redis message updates an `IOptionsMonitor<T>.CurrentValue`
  observed by a test consumer; JWT `IssuerSigningKeyResolver` validates a token signed with a
  rotated key and rejects one signed with the stale key.
* **API** — permission/authorization checks on all three endpoints; validation error
  contracts; audit record written with redacted `Before`/`After` for a secret key.

## 8. Rollout

Migration adds both tables and seeds the definition catalog; no existing behavior changes
until a SuperAdmin performs a first override — day one, `platform_settings` is empty and the
app runs identically to today.
