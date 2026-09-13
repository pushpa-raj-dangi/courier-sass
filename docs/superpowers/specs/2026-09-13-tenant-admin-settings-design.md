# Design: Tenant Admin Settings

**Date:** 2026-09-13
**Status:** Approved for planning
**Track:** Identity module (organization self-service settings) + Notifications module (template management)

## 1. Problem

There is no self-service settings surface for a tenant admin today. `Organization`
(`src/Modules/Identity/Domain/Organization.cs`) carries only `Name`, `Slug`, `IsActive`,
`SuspendedBySystem`, `DeletedAt` — everything else that touches "how does this org's account
behave" is either hardcoded globally or reachable only by a cross-tenant SuperAdmin through
Platform:

* Refresh-token lifetime is a flat 7 days for every org (`AuthService.IssueRefreshTokenAsync`).
* Password length/complexity is validated **only** in the frontend Zod schema
  (`features/auth/schema.ts`'s `.min(8)`) — there is no backend enforcement anywhere.
* `NotificationTemplate` (`src/Modules/Notifications/Domain/NotificationTemplate.cs`) already
  exists as an org-specific override of a notification body, but its own doc comment says *"a
  future template-management API (deferred) would let an org create/edit these"* — nothing
  creates or edits rows in that table today, so every organization silently uses the compiled-in
  defaults in `NotificationDefaultTemplates.Email`.
* `RevokeStaffRefreshTokensAsync` (force-logout every staff user in an org) exists and is used by
  Platform's suspend/reactivate flow, but a tenant admin has no way to trigger it themselves.
* Shipment creation always requires the counter staff to pick origin branch and service type by
  hand; there is no per-org default to pre-fill either.

## 2. Goals

A user holding `org.settings.manage` or `notification_template.manage` gets a **Settings** tab
in the existing staff Admin page (`(staff)/admin/page.tsx`, alongside Roles/Users) covering:

1. **Organization profile** — support email/phone, address, logo, timezone, business hours.
2. **Security policy** — refresh-token lifetime (1–30 days), minimum password length (8–64), and
   a "revoke all sessions" action.
3. **Operational defaults** — default service type and default origin branch, used only to
   pre-fill the shipment creation form.
4. **Notification templates** — list + edit the per-(event, channel) template bodies that
   `NotificationDispatchService` already knows how to look up but nothing lets an org create.

Every write is tenant-scoped (`ICurrentTenantProvider`, never an org id from the request) and
audited with before/after values per CLAUDE.md §23/§28.

## 3. Non-Goals

* **Per-org password complexity rules beyond a minimum length** (no required character classes,
  no breach-list checking). Minimum length is the concrete gap found in this codebase; anything
  richer is a separate future spec.
* **A general password-change endpoint.** None exists today for any user type; adding one is out
  of scope. `MinPasswordLength` is enforced at the one place a password is actually set
  server-side today — invite-accept (`AuthService.AcceptInviteAsync`) — plus organization
  self-registration, which uses the flat default of 8 since the org (and its `OrganizationSettings`
  row) doesn't exist yet when that password is chosen.
* **MFA, IP allowlisting, SSO.** Not requested, no existing scaffolding to extend.
* **Backfilling `OrganizationSettings` rows for existing organizations via a data migration.**
  Read and write paths both get-or-create the row lazily (§4), so no seed/backfill step is needed
  and no code path has to handle "row is missing" as a distinct error case.
* **Branding beyond a logo URL** (no theme colors, no custom domain). `LogoUrl` reuses the
  existing object-storage abstraction the same way merchant KYC documents already do; no new
  upload plumbing.

## 4. Data model

New table, Identity module, one row per organization:

```csharp
// src/Modules/Identity/Domain/OrganizationSettings.cs
namespace Courier.Identity.Domain;

public class OrganizationSettings
{
    public Guid OrganizationId { get; set; } // PK, FK -> Organizations.Id

    // Profile
    public string? SupportEmail { get; set; }
    public string? SupportPhone { get; set; }
    public string? AddressLine { get; set; }
    public string? LogoUrl { get; set; }
    public string TimeZoneId { get; set; } = "Asia/Kathmandu";
    public string? BusinessHours { get; set; } // free text, mirrors Branch.OperatingHours (Branch.cs:25) — no existing structured-hours type in this codebase to match instead
    public DateTimeOffset? ProfileUpdatedAt { get; set; }
    public Guid? ProfileUpdatedByUserId { get; set; }

    // Security policy
    public int RefreshTokenLifetimeDays { get; set; } = 7; // bounded 1-30 at write time
    public int MinPasswordLength { get; set; } = 8;         // bounded 8-64 at write time
    public DateTimeOffset? SecurityUpdatedAt { get; set; }
    public Guid? SecurityUpdatedByUserId { get; set; }

    // Operational defaults
    public Guid? DefaultServiceTypeId { get; set; }  // FK -> Pricing.ServiceType, nullable
    public Guid? DefaultOriginBranchId { get; set; } // FK -> Branches.Branch, nullable
    public DateTimeOffset? DefaultsUpdatedAt { get; set; }
    public Guid? DefaultsUpdatedByUserId { get; set; }
}
```

EF configuration (`OrganizationSettingsConfiguration.cs`) keys on `OrganizationId`, matches the
`Organization` query-filter pattern used elsewhere in Identity (tenant-scoped, `IgnoreQueryFilters()`
only from cross-tenant Platform callers — not applicable here since every consumer of this entity
is already inside a resolved tenant context).

**Get-or-create, not a required row.** `OrganizationSettingsService.GetAsync(organizationId)`
returns an existing row or creates one with defaults and saves it — the same pattern every read
and every partial-update method uses before applying its own section's changes. This means:

* No migration-time backfill for existing organizations.
* No "settings row missing" error path to test or handle anywhere else in the codebase.
* `AuthService.IssueRefreshTokenAsync` and the invite-accept password check both call
  `GetAsync` too, so a brand-new org (whose owner hasn't touched Settings yet) still gets
  correct defaults instead of a null-reference or a special-cased fallback.

## 5. Permissions

Two new entries in `PermissionCatalog.All`:

```csharp
("org.settings.manage", "Manage organization profile, security policy and operational defaults"),
("notification_template.manage", "Manage notification templates"),
```

No seed changes needed in `AdditionalRoleSeeds.cs` or `AuthService.RegisterOrganizationAsync`:
`OrganizationOwner` is granted every row in the `Permission` table at registration time
(`AuthService.cs:63-77`), and `OrganizationAdmin`'s seed is `PermissionCatalog.All` minus
`{org.billing.manage, org.ownership.transfer, org.delete}` — neither new key is in that
exclusion list, so both roles get both permissions automatically. No other default role is
granted either permission, matching the "Owner + Admin only" decision.

## 6. Backend — Organization Settings (profile, security, defaults)

`src/Modules/Identity/Application/OrganizationSettingsService.cs`:

```csharp
public sealed record OrganizationProfileDto(string? SupportEmail, string? SupportPhone, string? AddressLine, string? LogoUrl, string TimeZoneId, string? BusinessHours);
public sealed record SecurityPolicyDto(int RefreshTokenLifetimeDays, int MinPasswordLength);
public sealed record OperationalDefaultsDto(Guid? DefaultServiceTypeId, Guid? DefaultOriginBranchId);
public sealed record OrganizationSettingsDto(OrganizationProfileDto Profile, SecurityPolicyDto Security, OperationalDefaultsDto Defaults);

public class OrganizationSettingsService
{
    // Constructor takes DbContext, ICurrentTenantProvider, IAuditLogger, IBranchLookupService,
    // IServiceTypeLookupService — same DI shape as BranchService. Every method below resolves
    // organizationId from _currentTenantProvider.OrganizationId and the acting user's id from
    // actingUser.GetUserId() internally, never from a request-body/query parameter.
    //
    // GetAsync(ClaimsPrincipal actingUser) -> OrganizationSettingsDto (get-or-create per §4)
    // UpdateProfileAsync(ClaimsPrincipal actingUser, OrganizationProfileDto) -> OrganizationProfileDto
    // UpdateSecurityAsync(ClaimsPrincipal actingUser, SecurityPolicyDto) -> SecurityPolicyDto
    //   throws OrganizationSettingsValidationException if RefreshTokenLifetimeDays not in [1,30]
    //   or MinPasswordLength not in [8,64]
    // UpdateDefaultsAsync(ClaimsPrincipal actingUser, OperationalDefaultsDto) -> OperationalDefaultsDto
    //   throws OrganizationSettingsValidationException if DefaultOriginBranchId is set but
    //   IBranchLookupService.GetZoneInfoAsync returns null for it (branch doesn't exist / isn't
    //   this org's, via the ambient tenant query filter), or if DefaultServiceTypeId is set but
    //   IServiceTypeLookupService.ExistsAsync returns false for it (ServiceType is global
    //   reference data per its own doc comment — no tenant scoping needed for this check)
    // RevokeAllSessionsAsync(ClaimsPrincipal actingUser) -> calls IOrganizationLookupService.RevokeStaffRefreshTokensAsync

    // Plain-organizationId read, no ClaimsPrincipal: AuthService's login/refresh/invite-accept
    // flows (§7) run before or entirely without a resolved acting-staff-user principal, the same
    // reason IBranchLookupService exposes Guid-keyed reads alongside BranchService's
    // ClaimsPrincipal-keyed mutations. Also get-or-creates per §4.
    // GetSecurityPolicyAsync(Guid organizationId, CancellationToken) -> SecurityPolicyDto
}
```

`IBranchLookupService` is already referenced by Identity (`Identity.csproj` already has a
`ProjectReference` to `Branches.csproj`) — `GetZoneInfoAsync` is reused as-is, a non-null result
means the branch exists in the caller's org. `IServiceTypeLookupService` does not exist yet;
add a minimal one in `src/Modules/Pricing/Application/IServiceTypeLookupService.cs` mirroring
`IBranchLookupService`'s shape:

```csharp
namespace Courier.Pricing.Application;

public interface IServiceTypeLookupService
{
    Task<bool> ExistsAsync(Guid serviceTypeId, CancellationToken cancellationToken = default);
}
```

plus its one-line EF-backed implementation in `Pricing.Infrastructure`, DI registration in
`Program.cs` alongside the existing lookup-service registrations, and a new
`<ProjectReference Include="..\Pricing\Pricing.csproj" />` added to `Identity.csproj` (the same
kind of one-directional module dependency `Identity -> Branches` already is — `Pricing` gains no
reference back to `Identity`).

Each `Update*Async` call: loads (or creates) the row, applies the section's fields, sets that
section's `*UpdatedAt`/`*UpdatedByUserId`, saves, then writes one `AuditEntry` (Before = JSON of
the prior section DTO, After = JSON of the new one) via the existing `IAuditLogger` — same shape
as every other audited mutation in the codebase (e.g. `OrganizationAdminService.SuspendForNonPaymentAsync`).

Endpoints, `src/Modules/Identity/Presentation/OrganizationSettingsEndpoints.cs`, following
`BranchesEndpoints`'s shape (service takes `HttpContext.User`, resolves the caller's org and user
id from it, `.RequirePermission(...)` on each route):

```
GET   /api/v1/settings/organization                -> org.settings.manage
PATCH /api/v1/settings/organization/profile         -> org.settings.manage
PATCH /api/v1/settings/organization/security        -> org.settings.manage
PATCH /api/v1/settings/organization/defaults        -> org.settings.manage
POST  /api/v1/settings/organization/revoke-sessions -> org.settings.manage
```

Validation errors return `400` with `{ error: { code: "ORGANIZATION_SETTINGS_VALIDATION_FAILED", message } }`,
matching the `BranchValidationException`/`WebhookValidationException` error-contract convention.

## 7. Enforcement changes to existing code

* `AuthService.IssueRefreshTokenAsync(userId, ...)` currently hardcodes
  `ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)`. It gains an `organizationId` parameter — every
  call site (`LoginAsync`, `RefreshTokenAsync`, `AcceptInviteAsync`) already resolves the
  organization a few lines earlier for its own suspended-check, so this is passing along a value
  already in scope, not a new lookup — and reads `RefreshTokenLifetimeDays` via
  `OrganizationSettingsService.GetSecurityPolicyAsync(organizationId)` instead of the literal.
  `AuthService` gains a constructor dependency on `OrganizationSettingsService`, both in
  `Identity.Application` already — no new project reference needed for this part.
* `AuthService.AcceptInviteAsync` looks up the invite's organization's `MinPasswordLength` via the
  same `GetSecurityPolicyAsync` call and rejects a shorter password with a `400` before hashing
  it — the first backend-side password length check in the codebase (today's frontend-only
  `.min(8)` in `features/auth/schema.ts` stays as the fast client-side check, this is the
  authoritative server-side one).
* `RegisterOrganizationAsync` (org self-service signup) does **not** consult `OrganizationSettings`
  for its owner password — the org doesn't exist yet at that point in the transaction — and keeps
  using the flat length-8 minimum the frontend already enforces.

## 8. Backend — Notification Templates

`src/Modules/Notifications/Application/NotificationTemplateService.cs`:

```csharp
public sealed record NotificationTemplateDto(Guid Id, string EventType, NotificationChannel Channel, string Body, bool IsActive);

public class NotificationTemplateService
{
    // Same DI/resolution shape as OrganizationSettingsService: organizationId from
    // ICurrentTenantProvider, acting user id from ClaimsPrincipal.GetUserId(), never a
    // client-supplied id.
    //
    // ListAsync(ClaimsPrincipal actingUser) -> IReadOnlyList<NotificationTemplateDto>
    //   one row per (EventType, Channel) that has ever been customized for this org
    // UpsertAsync(ClaimsPrincipal actingUser, string eventType, NotificationChannel channel, string body, bool isActive)
    //   -> NotificationTemplateDto
    //   validates eventType against a fixed catalog (see below) and rejects unknown event types
    //   with NotificationTemplateValidationException
}
```

**Event-type catalog.** `NotificationDefaultTemplates.Email.Keys` today only lists the event
types that have a compiled-in Email fallback (`shipment.created`, `shipment.delivered`,
`shipment.ndr_created`, `cod.collected`, `user.invited`) — this is exactly the boundary of what a
customized template can meaningfully affect, since `NotificationDispatchService` only dispatches
a channel it has a resolvable body for. `UpsertAsync` validates `eventType` against this same
list (exposed as `NotificationDefaultTemplates.Email.Keys`, no duplicated list) rather than
accepting arbitrary strings that would silently never fire.

Endpoints, `src/Modules/Notifications/Presentation/NotificationTemplatesEndpoints.cs`:

```
GET /api/v1/settings/notification-templates           -> notification_template.manage
PUT /api/v1/settings/notification-templates            -> notification_template.manage
    body: { eventType, channel, body, isActive }
```

Audit action: `notification_template.upserted`, `EntityType = "NotificationTemplate"`.

## 9. Frontend

* `(staff)/admin/page.tsx`: the `permission` field on each `TABS` entry is documentation only —
  actual gating is the hand-written `permitted` object a few lines down
  (`{ roles: canManageRoles, users: canReadUsers }`). Append
  `{ key: "settings", label: "Settings", permission: "org.settings.manage" }` to `TABS`, add
  `const canManageSettings = usePermission("org.settings.manage");` and
  `const canManageTemplates = usePermission("notification_template.manage");` alongside the
  existing `usePermission` calls, and extend `permitted` with
  `settings: canManageSettings || canManageTemplates` — an OR, since either permission alone
  should surface the tab; each of the tab's four sub-sections then independently checks its own
  permission before rendering, the same "tab OR, section AND" split this file already documents
  for cross-permission visibility.
* `src/features/admin/components/SettingsTab.tsx` — a nested `Tabs` (Profile / Security /
  Defaults / Notification Templates), each panel gated on its own permission check:
  * **Profile / Security / Defaults**: inline forms (shadcn `Card` + `Form`, React Hook Form +
    Zod), one `useMutation` per PATCH endpoint, matching `features/admin/hooks/useUpdateRole.ts`'s
    shape. Not Sheets — each edits one existing record in place, not a list of records to open.
    Security tab's "Revoke all sessions" is a destructive-styled button behind a confirm dialog
    (shadcn `AlertDialog`, same pattern as any other destructive action in this codebase).
  * **Notification Templates**: a `DataTable` (columns: Event, Channel, Active, preview of Body)
    + an edit `Sheet` driven by a Zustand store (`notificationTemplatePanelStore.ts`), following
    this project's established Sheet-over-Pages convention for list+detail. The event/channel
    pairs shown are the full cross-product of the fixed event catalog × `NotificationChannel`
    enum values, each row showing either the org's override or "(using default)" when no row
    exists yet for that pair — editing one always upserts.
* New hooks in `features/admin/hooks/`: `useOrganizationSettings.ts`,
  `useUpdateOrganizationProfile.ts`, `useUpdateSecurityPolicy.ts`, `useUpdateOperationalDefaults.ts`,
  `useRevokeAllSessions.ts`, `useNotificationTemplates.ts`, `useUpsertNotificationTemplate.ts` — all
  `TanStack Query`, invalidating the `["organization-settings"]` / `["notification-templates"]`
  query keys on success.
* Next.js proxy routes: `src/app/api/settings/organization/route.ts` (+ `profile|security|defaults`
  sub-routes, `POST .../revoke-sessions/route.ts`) and `src/app/api/settings/notification-templates/route.ts`
  — thin passthroughs to the backend, same shape as `src/app/api/branches/route.ts`.
* Shipment creation form (`features/shipments/schema.ts` + its create form component): on mount,
  pre-fills `serviceType`/`originBranchId` from `useOrganizationSettings()`'s `defaults` when the
  fields are still empty and a default exists — fully overridable, no behavior change when no
  default is configured.

## 10. Testing

* **Unit** (`Identity.Tests`): `OrganizationSettingsServiceTests` — get-or-create on first read;
  `UpdateSecurityAsync` rejects `RefreshTokenLifetimeDays` outside `[1,30]` and `MinPasswordLength`
  outside `[8,64]`; `UpdateDefaultsAsync` rejects a `DefaultServiceTypeId`/`DefaultOriginBranchId`
  that doesn't resolve for the calling org; each `Update*Async` writes exactly one audit entry
  with correct before/after. `Notifications.Tests`: `NotificationTemplateServiceTests` — `UpsertAsync`
  rejects an event type outside the fixed catalog; a second upsert for the same (event, channel)
  updates the existing row rather than inserting a duplicate.
* **Integration** (`Api.Tests`, `WebApplicationFactory<Program>`): tenant isolation — org A's
  token cannot read or write org B's settings/templates regardless of any id in the request;
  permission enforcement — `403` without `org.settings.manage` / `notification_template.manage`;
  `AuthServiceTests` — a refresh token issued for an org with `RefreshTokenLifetimeDays = 1`
  actually expires after 1 day, not the old flat 7; invite-accept rejects a password shorter than
  the org's configured `MinPasswordLength` and accepts one at the boundary.
* **API tests**: PATCH/PUT validation error shapes (`400` + `ORGANIZATION_SETTINGS_VALIDATION_FAILED`
  / notification template's equivalent code); `POST revoke-sessions` actually revokes existing
  refresh tokens for the org (reuses the same assertion style as Platform's existing suspend
  tests for `RevokeStaffRefreshTokensAsync`).
* **Frontend**: hook tests mocking `clientFetch` for each new hook; proxy route tests for each new
  `route.ts`; `SettingsTab` render tests per sub-section (permission-gated visibility, form
  submit calls the right mutation); shipment creation form test confirming defaults pre-fill only
  when the field is empty and a default exists.
