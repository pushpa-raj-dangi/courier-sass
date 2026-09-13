# Tenant Admin Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a tenant `OrganizationOwner`/`OrganizationAdmin` a self-service Settings surface covering organization profile, security policy (refresh-token lifetime, minimum password length, revoke-all-sessions), operational defaults (default service type/branch for shipment creation), and notification template management.

**Architecture:** A new `OrganizationSettings` entity (Identity module, one row per org, get-or-created lazily) backs three of the four areas through a new `OrganizationSettingsService`. `AuthService.IssueRefreshTokenAsync`/`AcceptInviteAsync` are changed to read the org's configured refresh-token lifetime and minimum password length instead of hardcoded literals. Notification templates reuse the existing (currently write-less) `NotificationTemplate` table in the Notifications module through a new `NotificationTemplateService`. Frontend: a new "Settings" tab appended to the existing staff Admin page, following this codebase's established RHF+Zod form and Sheet-over-Pages conventions.

**Tech Stack:** ASP.NET Core / C# / EF Core 10 / PostgreSQL — same as every existing module. Frontend: Next.js App Router BFF pattern, TanStack Query, React Hook Form + Zod, shadcn/ui, Zustand.

**Spec:** `docs/superpowers/specs/2026-09-13-tenant-admin-settings-design.md` — this plan implements every section of that spec; read both together.

## Global Constraints

- Target framework `net10.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` — matches every existing `.csproj` in this repo.
- No module references another module's `DbSet<T>` directly, and no module takes a project reference that would create a cycle. This plan adds exactly one new project reference: `Identity.csproj` → `Pricing.csproj` (Task 5). Verified acyclic: `Pricing.csproj` references `SharedKernel`, `Audit`, `Geography`, `Merchants`, `Branches` — none of which reference `Identity`.
- Every tenant-scoped table's EF query filter is `_currentTenantProvider.OrganizationId != null && x.OrganizationId == _currentTenantProvider.OrganizationId`. `OrganizationSettings` is tenant-scoped the same way; every read/write in `OrganizationSettingsService` goes through the ambient filter except the two Guid-keyed methods called from `AuthService` pre-tenant-context flows (`GetSecurityPolicyAsync`), which use `.IgnoreQueryFilters()` with an explicit `OrganizationId` predicate — same convention as `OrganizationAdminService.IsActiveAsync`.
- Every enum exposed over JSON needs `[JsonConverter(typeof(JsonStringEnumConverter))]` or it silently serializes as a raw integer — this codebase has hit that bug repeatedly (`BranchType`, `ShipmentStatus`, `ManifestPurpose`, `ManifestStatus`, three `Ndr` enums, three `Cod` enums, `SubscriptionStatus`). `NotificationChannel` (`src/Modules/Notifications/Domain/NotificationChannel.cs`) is exposed over JSON for the first time in this plan (Task 7) and currently lacks the attribute — Task 7 adds it.
- Migrations: `dotnet ef migrations add <Name> --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`, then inspect the generated file before committing — this codebase has been bitten before by EF silently merging/renaming indexes.
- Error contract: every business-rule exception is caught in the `Presentation` endpoint and mapped to `Results.BadRequest(new { error = new { code = "SOME_CODE", message = ex.Message } })` — never a raw 500 for an expected validation failure.
- Backend unit test DB conventions are **per-module, not uniform** — verified directly rather than assumed: `Identity.Tests` (Tasks 1-4) uses a shared-Postgres `TestDbContextFactory` (port 5434, confirm `courier-postgres-1` is running via `docker ps` — do not `docker compose up` a second instance per project memory). `Pricing.Tests` (Task 5) and `Notifications.Tests` (Task 7) instead each define their own lightweight EF Core **InMemory** `TestDbContext` scoped to just that module's own `IEntityTypeConfiguration`s, per those two projects' existing test files (`PricingServiceTests.cs`, `NotificationDispatchServiceTests.cs`) — match whichever convention the module already uses, never introduce a third pattern.
- Frontend: every new UI control is a shadcn/ui primitive already present in `src/web/src/components/ui/` — never a raw HTML `<input>`/`<select>`/`<button>`. This codebase has **no** `alert-dialog.tsx` and no existing confirm-dialog pattern anywhere (`RoleDetailSheet.tsx`'s delete button is a plain `variant="destructive"` `Button` with no confirmation step) — the "Revoke all sessions" button in Task 3 follows that exact existing precedent, not a new dialog.
- Detail/edit UI for a single existing record (Profile, Security, Defaults) is an inline form on the tab panel itself, per the approved spec §9 — these are not lists, so they are not Sheets. Notification Templates *is* a list, so it follows this project's established Sheet-over-Pages convention (a Zustand-driven right-side `Sheet`), per feedback memory.
- No permission-catalog backfill migration for pre-existing organizations. Precedent: `AdditionalRoleSeeds.ByRoleName` (Phase F9) is applied only at `RegisterOrganizationAsync` time — organizations registered before that phase never retroactively received those roles either, and that gap was accepted, not backfilled. An owner of a pre-existing org who wants `org.settings.manage` today already has a working path with zero new code: create a custom role with that permission via the existing Roles tab (`RoleService.CreateRoleAsync` accepts any `PermissionCatalog` key) and assign it to themselves.
- No email/SMS reminder, no MFA/IP-allowlisting/SSO, no password complexity rules beyond a minimum length, no general password-change endpoint, no `OrganizationSettings` backfill migration, no branding beyond a logo URL — see spec §3. Do not add any of these "while you're in there."

---

## Task 1: `OrganizationSettings` domain + migration + permission catalog + service foundation

**Files:**
- Create: `src/Modules/Identity/Domain/OrganizationSettings.cs`
- Create: `src/Modules/Identity/Infrastructure/OrganizationSettingsConfiguration.cs`
- Create: `src/Modules/Identity/Application/OrganizationSettingsDtos.cs`
- Create: `src/Modules/Identity/Application/OrganizationSettingsService.cs`
- Modify: `src/Modules/Identity/Application/PermissionCatalog.cs`
- Modify: `src/Api/Program.cs` (register `OrganizationSettingsService`)
- Create: `tests/Identity.Tests/OrganizationSettingsServiceTests.cs`
- Create: `tests/Identity.Tests/PermissionCatalogTests.cs` (modify — add two-entry assertion; file already exists per Global Constraints test-project listing)

**Interfaces:**
- Produces: `Courier.Identity.Domain.OrganizationSettings` (fields per spec §4).
- Produces: `Courier.Identity.Application.OrganizationProfileDto(string? SupportEmail, string? SupportPhone, string? AddressLine, string? LogoUrl, string TimeZoneId, string? BusinessHours)`, `SecurityPolicyDto(int RefreshTokenLifetimeDays, int MinPasswordLength)`, `OperationalDefaultsDto(Guid? DefaultServiceTypeId, Guid? DefaultOriginBranchId)`, `OrganizationSettingsDto(OrganizationProfileDto Profile, SecurityPolicyDto Security, OperationalDefaultsDto Defaults)`.
- Produces: `Courier.Identity.Application.OrganizationSettingsValidationException`.
- Produces on `OrganizationSettingsService`: `Task<OrganizationSettingsDto> GetAsync(Guid organizationId, CancellationToken ct = default)` and `Task<SecurityPolicyDto> GetSecurityPolicyAsync(Guid organizationId, CancellationToken ct = default)` — both get-or-create. Later tasks add `UpdateProfileAsync`, `UpdateSecurityAsync`, `UpdateDefaultsAsync`, `RevokeAllSessionsAsync` to this same class/file.
- Produces two new `PermissionCatalog.All` entries: `"org.settings.manage"`, `"notification_template.manage"`.

- [ ] **Step 1: Write the failing domain + get-or-create test**

```csharp
// tests/Identity.Tests/OrganizationSettingsServiceTests.cs
using Courier.Audit.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Identity.Tests;

public class OrganizationSettingsServiceTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; }
    }

    private static Organization NewOrganization(DbContext dbContext)
    {
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = true };
        dbContext.Set<Organization>().Add(organization);
        return organization;
    }

    [Fact]
    public async Task GetAsync_NoRowExists_CreatesRowWithDefaults()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger());

        var result = await service.GetAsync(organization.Id);

        Assert.Equal("Asia/Kathmandu", result.Profile.TimeZoneId);
        Assert.Equal(7, result.Security.RefreshTokenLifetimeDays);
        Assert.Equal(8, result.Security.MinPasswordLength);
        Assert.Null(result.Defaults.DefaultServiceTypeId);
        var persisted = dbContext.Set<OrganizationSettings>().IgnoreQueryFilters().Single(s => s.OrganizationId == organization.Id);
        Assert.Equal(7, persisted.RefreshTokenLifetimeDays);
    }

    [Fact]
    public async Task GetAsync_RowAlreadyExists_ReturnsExistingValuesWithoutOverwriting()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        dbContext.Set<OrganizationSettings>().Add(new OrganizationSettings { OrganizationId = organization.Id, RefreshTokenLifetimeDays = 14, MinPasswordLength = 10, TimeZoneId = "Asia/Kathmandu" });
        await dbContext.SaveChangesAsync();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger());

        var result = await service.GetAsync(organization.Id);

        Assert.Equal(14, result.Security.RefreshTokenLifetimeDays);
        Assert.Equal(10, result.Security.MinPasswordLength);
        Assert.Single(dbContext.Set<OrganizationSettings>().IgnoreQueryFilters().Where(s => s.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task GetSecurityPolicyAsync_NoRowExists_ReturnsDefaultsAndPersistsRow()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = NewOrganization(dbContext);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger());

        var result = await service.GetSecurityPolicyAsync(organization.Id);

        Assert.Equal(7, result.RefreshTokenLifetimeDays);
        Assert.Equal(8, result.MinPasswordLength);
    }
}
```

```csharp
// tests/Identity.Tests/PermissionCatalogTests.cs — add this test (append if the file already has other tests; do not remove existing ones)
[Fact]
public void All_ContainsOrganizationSettingsAndNotificationTemplatePermissions()
{
    Assert.Contains(PermissionCatalog.All, p => p.Key == "org.settings.manage");
    Assert.Contains(PermissionCatalog.All, p => p.Key == "notification_template.manage");
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter "OrganizationSettingsServiceTests|PermissionCatalogTests"`
Expected: FAIL to compile — `OrganizationSettings`, `OrganizationSettingsService`, and the two new permission keys don't exist yet.

- [ ] **Step 3: Add the domain entity**

```csharp
// src/Modules/Identity/Domain/OrganizationSettings.cs
namespace Courier.Identity.Domain;

// One row per organization, created lazily on first read or write (OrganizationSettingsService
// get-or-creates — no backfill migration, no "row missing" error path anywhere else in the
// codebase needs to exist).
public class OrganizationSettings
{
    public Guid OrganizationId { get; set; }

    // Profile
    public string? SupportEmail { get; set; }
    public string? SupportPhone { get; set; }
    public string? AddressLine { get; set; }
    public string? LogoUrl { get; set; }
    public string TimeZoneId { get; set; } = "Asia/Kathmandu";
    // Free text, mirrors Branch.OperatingHours — no structured-hours type exists in this codebase.
    public string? BusinessHours { get; set; }
    public DateTimeOffset? ProfileUpdatedAt { get; set; }
    public Guid? ProfileUpdatedByUserId { get; set; }

    // Security policy
    public int RefreshTokenLifetimeDays { get; set; } = 7;
    public int MinPasswordLength { get; set; } = 8;
    public DateTimeOffset? SecurityUpdatedAt { get; set; }
    public Guid? SecurityUpdatedByUserId { get; set; }

    // Operational defaults
    public Guid? DefaultServiceTypeId { get; set; }
    public Guid? DefaultOriginBranchId { get; set; }
    public DateTimeOffset? DefaultsUpdatedAt { get; set; }
    public Guid? DefaultsUpdatedByUserId { get; set; }
}
```

- [ ] **Step 4: Add the EF configuration**

```csharp
// src/Modules/Identity/Infrastructure/OrganizationSettingsConfiguration.cs
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Identity.Infrastructure;

public class OrganizationSettingsConfiguration : IEntityTypeConfiguration<OrganizationSettings>
{
    public void Configure(EntityTypeBuilder<OrganizationSettings> builder)
    {
        builder.ToTable("OrganizationSettings");
        builder.HasKey(s => s.OrganizationId);
        builder.Property(s => s.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(s => s.SupportEmail).HasMaxLength(320);
        builder.Property(s => s.SupportPhone).HasMaxLength(30);
        builder.Property(s => s.LogoUrl).HasMaxLength(2000);

        builder.HasOne<Organization>()
            .WithOne()
            .HasForeignKey<OrganizationSettings>(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 5: Add the two permission catalog entries**

In `src/Modules/Identity/Application/PermissionCatalog.cs`, add to the end of the `All` array (before the closing `};`, immediately after the existing `org.delete` entry):

```csharp
        ("org.settings.manage", "Manage organization profile, security policy and operational defaults"),
        ("notification_template.manage", "Manage notification templates"),
```

- [ ] **Step 6: Add the DTOs and validation exception**

```csharp
// src/Modules/Identity/Application/OrganizationSettingsDtos.cs
namespace Courier.Identity.Application;

public sealed record OrganizationProfileDto(string? SupportEmail, string? SupportPhone, string? AddressLine, string? LogoUrl, string TimeZoneId, string? BusinessHours);
public sealed record SecurityPolicyDto(int RefreshTokenLifetimeDays, int MinPasswordLength);
public sealed record OperationalDefaultsDto(Guid? DefaultServiceTypeId, Guid? DefaultOriginBranchId);
public sealed record OrganizationSettingsDto(OrganizationProfileDto Profile, SecurityPolicyDto Security, OperationalDefaultsDto Defaults);

public class OrganizationSettingsValidationException : Exception
{
    public OrganizationSettingsValidationException(string message) : base(message) { }
}
```

- [ ] **Step 7: Add the service with `GetAsync`/`GetSecurityPolicyAsync`**

```csharp
// src/Modules/Identity/Application/OrganizationSettingsService.cs
using Courier.Audit.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class OrganizationSettingsService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;

    public OrganizationSettingsService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
    }

    public async Task<OrganizationSettingsDto> GetAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateAsync(organizationId, cancellationToken);
        return ToDto(settings);
    }

    // Guid-keyed, no ClaimsPrincipal: called from AuthService's login/refresh/invite-accept flows
    // (Task 4), which run before or entirely without a resolved acting-staff-user principal.
    public async Task<SecurityPolicyDto> GetSecurityPolicyAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var settings = await GetOrCreateAsync(organizationId, cancellationToken);
        return new SecurityPolicyDto(settings.RefreshTokenLifetimeDays, settings.MinPasswordLength);
    }

    // IgnoreQueryFilters(): every caller of this method either already holds a resolved ambient
    // tenant (staff Settings endpoints, Task 2/3/5) or explicitly runs with no ambient tenant at
    // all (AuthService's pre-authentication flows, Task 4) — the explicit organizationId
    // predicate is the real scope in both cases, matching OrganizationAdminService.IsActiveAsync.
    private async Task<OrganizationSettings> GetOrCreateAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.Set<OrganizationSettings>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        settings = new OrganizationSettings { OrganizationId = organizationId };
        _dbContext.Set<OrganizationSettings>().Add(settings);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private static OrganizationSettingsDto ToDto(OrganizationSettings s) => new(
        new OrganizationProfileDto(s.SupportEmail, s.SupportPhone, s.AddressLine, s.LogoUrl, s.TimeZoneId, s.BusinessHours),
        new SecurityPolicyDto(s.RefreshTokenLifetimeDays, s.MinPasswordLength),
        new OperationalDefaultsDto(s.DefaultServiceTypeId, s.DefaultOriginBranchId));
}
```

- [ ] **Step 8: Generate and inspect the migration**

Run: `dotnet ef migrations add AddOrganizationSettings --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`

Expected in the generated file: a new `OrganizationSettings` table (PK `OrganizationId`, FK to `Organizations` with cascade delete) and two new rows inserted into `Permissions` via `InsertData` (for `org.settings.manage` and `notification_template.manage`, ids computed by `PermissionCatalog.IdFor`). Confirm no unrelated table changed.

- [ ] **Step 9: Register the service in `Program.cs`**

In `src/Api/Program.cs`, immediately after the existing `builder.Services.AddScoped<IOrganizationLookupService, OrganizationAdminService>();` line, add:

```csharp
builder.Services.AddScoped<OrganizationSettingsService>();
```

- [ ] **Step 10: Run the tests to verify they pass**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter "OrganizationSettingsServiceTests|PermissionCatalogTests"`
Expected: PASS

- [ ] **Step 11: Build the whole solution to catch any ripple**

Run: `dotnet build Courier.slnx`
Expected: Success (this also confirms no other module's `FakeAuditLogger`/seed test broke from the two new catalog entries — nothing currently asserts an exact count of `PermissionCatalog.All`, but confirm via `dotnet build` there's no compile break).

- [ ] **Step 12: Commit**

```bash
git add src/Modules/Identity/Domain/OrganizationSettings.cs src/Modules/Identity/Infrastructure/OrganizationSettingsConfiguration.cs src/Modules/Identity/Application/OrganizationSettingsDtos.cs src/Modules/Identity/Application/OrganizationSettingsService.cs src/Modules/Identity/Application/PermissionCatalog.cs src/Api/Program.cs src/Persistence/Migrations tests/Identity.Tests/OrganizationSettingsServiceTests.cs tests/Identity.Tests/PermissionCatalogTests.cs
git commit -m "feat(identity): add OrganizationSettings foundation and two new permissions"
```

---

## Task 2: Organization Profile — backend endpoint + audit + Settings tab shell + frontend

**Files:**
- Modify: `src/Modules/Identity/Application/OrganizationSettingsService.cs` (add `UpdateProfileAsync`)
- Create: `src/Modules/Identity/Presentation/OrganizationSettingsEndpoints.cs`
- Modify: `src/Api/Program.cs` (map new endpoints)
- Modify: `tests/Identity.Tests/OrganizationSettingsServiceTests.cs` (add `UpdateProfileAsync` tests)
- Create: `tests/Api.Tests/OrganizationSettingsEndpointsTests.cs`
- Create: `src/web/src/features/admin/settings/types.ts`
- Create: `src/web/src/features/admin/settings/schema.ts`
- Create: `src/web/src/features/admin/settings/hooks/useOrganizationSettings.ts`
- Create: `src/web/src/features/admin/settings/hooks/useUpdateOrganizationProfile.ts`
- Create: `src/web/src/features/admin/settings/components/SettingsTab.tsx`
- Create: `src/web/src/features/admin/settings/components/OrganizationProfileForm.tsx`
- Create: `src/web/src/app/api/settings/organization/route.ts`
- Create: `src/web/src/app/api/settings/organization/profile/route.ts`
- Modify: `src/web/src/app/(staff)/admin/page.tsx`
- Create: `src/web/src/features/admin/settings/hooks/useOrganizationSettings.test.tsx`
- Create: `src/web/src/app/api/settings/organization/route.test.ts`

**Interfaces:**
- Consumes: `OrganizationSettingsDto`, `OrganizationProfileDto` from Task 1.
- Produces: `OrganizationSettingsService.UpdateProfileAsync(Guid organizationId, Guid actingUserId, OrganizationProfileDto profile, CancellationToken ct = default) -> Task<OrganizationProfileDto>`.
- Produces endpoints: `GET /api/v1/settings/organization` (returns `OrganizationSettingsDto`), `PATCH /api/v1/settings/organization/profile` (body `OrganizationProfileDto`, returns updated `OrganizationProfileDto`) — both `.RequirePermission("org.settings.manage")`.
- Produces frontend: `useOrganizationSettings()` (`TanStack Query`, key `["organization-settings"]`), `useUpdateOrganizationProfile()` (mutation, invalidates that key). `SettingsTab` is the component later tasks (3, 5, 7) add their own nested-tab panel into.

- [ ] **Step 1: Write the failing backend test**

Append to `tests/Identity.Tests/OrganizationSettingsServiceTests.cs`:

```csharp
[Fact]
public async Task UpdateProfileAsync_PersistsFieldsAndWritesAuditEntry()
{
    using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
    var organization = NewOrganization(dbContext);
    await dbContext.SaveChangesAsync();
    var auditLogger = new FakeAuditLogger();
    var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), auditLogger);
    var actingUserId = Guid.NewGuid();
    var profile = new OrganizationProfileDto("support@example.test", "01-4444444", "New Road, Kathmandu", null, "Asia/Kathmandu", "9:00 AM - 6:00 PM");

    var result = await service.UpdateProfileAsync(organization.Id, actingUserId, profile);

    Assert.Equal("support@example.test", result.SupportEmail);
    var persisted = dbContext.Set<OrganizationSettings>().IgnoreQueryFilters().Single(s => s.OrganizationId == organization.Id);
    Assert.Equal("support@example.test", persisted.SupportEmail);
    Assert.NotNull(persisted.ProfileUpdatedAt);
    Assert.Equal(actingUserId, persisted.ProfileUpdatedByUserId);
    var entry = Assert.Single(auditLogger.Entries);
    Assert.Equal("organization.profile_updated", entry.Action);
    Assert.Equal(organization.Id, entry.OrganizationId);
    Assert.Equal(actingUserId, entry.UserId);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter UpdateProfileAsync_PersistsFieldsAndWritesAuditEntry`
Expected: FAIL — `UpdateProfileAsync` doesn't exist.

- [ ] **Step 3: Implement `UpdateProfileAsync`**

Add to `OrganizationSettingsService` (in `src/Modules/Identity/Application/OrganizationSettingsService.cs`, after `GetSecurityPolicyAsync`):

```csharp
public async Task<OrganizationProfileDto> UpdateProfileAsync(Guid organizationId, Guid actingUserId, OrganizationProfileDto profile, CancellationToken cancellationToken = default)
{
    var settings = await GetOrCreateAsync(organizationId, cancellationToken);
    var before = new OrganizationProfileDto(settings.SupportEmail, settings.SupportPhone, settings.AddressLine, settings.LogoUrl, settings.TimeZoneId, settings.BusinessHours);

    settings.SupportEmail = profile.SupportEmail;
    settings.SupportPhone = profile.SupportPhone;
    settings.AddressLine = profile.AddressLine;
    settings.LogoUrl = profile.LogoUrl;
    settings.TimeZoneId = string.IsNullOrWhiteSpace(profile.TimeZoneId) ? "Asia/Kathmandu" : profile.TimeZoneId;
    settings.BusinessHours = profile.BusinessHours;
    settings.ProfileUpdatedAt = DateTimeOffset.UtcNow;
    settings.ProfileUpdatedByUserId = actingUserId;
    await _dbContext.SaveChangesAsync(cancellationToken);

    await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "organization.profile_updated", "OrganizationSettings", organizationId.ToString(),
        Before: System.Text.Json.JsonSerializer.Serialize(before), After: System.Text.Json.JsonSerializer.Serialize(profile)), cancellationToken);

    return new OrganizationProfileDto(settings.SupportEmail, settings.SupportPhone, settings.AddressLine, settings.LogoUrl, settings.TimeZoneId, settings.BusinessHours);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter UpdateProfileAsync_PersistsFieldsAndWritesAuditEntry`
Expected: PASS

- [ ] **Step 5: Write the failing API integration test**

```csharp
// tests/Api.Tests/OrganizationSettingsEndpointsTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class OrganizationSettingsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrganizationSettingsEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest($"Settings Org {suffix}", $"settings-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task GetOrganizationSettings_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/settings/organization");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrganizationSettings_OwnerToken_ReturnsDefaults()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/settings/organization");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OrganizationSettingsDto>();
        Assert.Equal("Asia/Kathmandu", body!.Profile.TimeZoneId);
        Assert.Equal(7, body.Security.RefreshTokenLifetimeDays);
    }

    [Fact]
    public async Task PatchProfile_OwnerToken_UpdatesAndPersists()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var profile = new OrganizationProfileDto("support@example.test", null, null, null, "Asia/Kathmandu", null);

        var patchResponse = await client.PatchAsJsonAsync("/api/v1/settings/organization/profile", profile);
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/v1/settings/organization");
        var body = await getResponse.Content.ReadFromJsonAsync<OrganizationSettingsDto>();
        Assert.Equal("support@example.test", body!.Profile.SupportEmail);
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet build Courier.slnx`
Expected: FAIL to compile — `MapOrganizationSettingsEndpoints`/routes don't exist yet.

- [ ] **Step 7: Implement the endpoints**

```csharp
// src/Modules/Identity/Presentation/OrganizationSettingsEndpoints.cs
using Courier.Identity.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Identity.Presentation;

public static class OrganizationSettingsEndpoints
{
    public static void MapOrganizationSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/settings/organization").WithTags("Organization Settings");

        group.MapGet("/", async (OrganizationSettingsService service, ICurrentTenantProvider tenantProvider, CancellationToken cancellationToken) =>
        {
            var organizationId = tenantProvider.OrganizationId!.Value;
            return Results.Ok(await service.GetAsync(organizationId, cancellationToken));
        }).RequirePermission("org.settings.manage");

        group.MapPatch("/profile", async (OrganizationProfileDto request, OrganizationSettingsService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var organizationId = tenantProvider.OrganizationId!.Value;
            var actingUserId = httpContext.User.GetUserId()!.Value;
            try
            {
                return Results.Ok(await service.UpdateProfileAsync(organizationId, actingUserId, request, httpContext.RequestAborted));
            }
            catch (OrganizationSettingsValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "ORGANIZATION_SETTINGS_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("org.settings.manage");
    }
}
```

`RequirePermission` already calls `.RequireAuthorization()` internally (`src/SharedKernel/RequirePermissionFilter.cs`), and `IPermissionChecker.HasPermissionAsync` requires a resolvable `GetUserId()` — both routes are unreachable without a valid bearer token, so `tenantProvider.OrganizationId!.Value` and `httpContext.User.GetUserId()!.Value` are safe here, matching `RolesEndpoints`'s identical non-null-forgiving usage.

In `src/Api/Program.cs`, add `app.MapOrganizationSettingsEndpoints();` immediately after the existing `app.MapRolesEndpoints();` line.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj --filter OrganizationSettingsEndpointsTests`
Expected: PASS

- [ ] **Step 9: Commit backend**

```bash
git add src/Modules/Identity/Application/OrganizationSettingsService.cs src/Modules/Identity/Presentation/OrganizationSettingsEndpoints.cs src/Api/Program.cs tests/Identity.Tests/OrganizationSettingsServiceTests.cs tests/Api.Tests/OrganizationSettingsEndpointsTests.cs
git commit -m "feat(identity): add organization profile settings endpoint"
```

- [ ] **Step 10: Write the failing frontend hook test**

```tsx
// src/web/src/features/admin/settings/hooks/useOrganizationSettings.test.tsx
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useOrganizationSettings } from "./useOrganizationSettings";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useOrganizationSettings", () => {
  it("fetches from the BFF proxy route", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        profile: { supportEmail: null, supportPhone: null, addressLine: null, logoUrl: null, timeZoneId: "Asia/Kathmandu", businessHours: null },
        security: { refreshTokenLifetimeDays: 7, minPasswordLength: 8 },
        defaults: { defaultServiceTypeId: null, defaultOriginBranchId: null },
      }),
    });

    const { result } = renderHook(() => useOrganizationSettings(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.profile.timeZoneId).toBe("Asia/Kathmandu");
    expect(global.fetch).toHaveBeenCalledWith("/api/settings/organization", undefined);
  });
});
```

- [ ] **Step 11: Run test to verify it fails**

Run: `cd src/web && npx vitest run src/features/admin/settings/hooks/useOrganizationSettings.test.tsx`
Expected: FAIL — module doesn't exist.

- [ ] **Step 12: Implement types, schema, hook, and proxy route**

```ts
// src/web/src/features/admin/settings/types.ts
export interface OrganizationProfile {
  supportEmail: string | null;
  supportPhone: string | null;
  addressLine: string | null;
  logoUrl: string | null;
  timeZoneId: string;
  businessHours: string | null;
}

export interface SecurityPolicy {
  refreshTokenLifetimeDays: number;
  minPasswordLength: number;
}

export interface OperationalDefaults {
  defaultServiceTypeId: string | null;
  defaultOriginBranchId: string | null;
}

export interface OrganizationSettings {
  profile: OrganizationProfile;
  security: SecurityPolicy;
  defaults: OperationalDefaults;
}
```

```ts
// src/web/src/features/admin/settings/schema.ts
import { z } from "zod";

export const organizationProfileSchema = z.object({
  supportEmail: z.string().email("Enter a valid email.").optional().or(z.literal("")),
  supportPhone: z.string().optional().or(z.literal("")),
  addressLine: z.string().optional().or(z.literal("")),
  logoUrl: z.string().optional().or(z.literal("")),
  timeZoneId: z.string().min(1, "Timezone is required."),
  businessHours: z.string().optional().or(z.literal("")),
});

export type OrganizationProfileFormValues = z.infer<typeof organizationProfileSchema>;
```

```ts
// src/web/src/features/admin/settings/hooks/useOrganizationSettings.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { OrganizationSettings } from "@/features/admin/settings/types";

export function useOrganizationSettings() {
  return useQuery({
    queryKey: ["organization-settings"],
    queryFn: () => clientFetch<OrganizationSettings>("/api/settings/organization"),
  });
}
```

```ts
// src/web/src/features/admin/settings/hooks/useUpdateOrganizationProfile.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { OrganizationProfile } from "@/features/admin/settings/types";

export function useUpdateOrganizationProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: OrganizationProfile) =>
      clientFetch<OrganizationProfile>("/api/settings/organization/profile", {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["organization-settings"] }),
  });
}
```

```ts
// src/web/src/app/api/settings/organization/route.ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/settings/organization");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```ts
// src/web/src/app/api/settings/organization/profile/route.ts
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function PATCH(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/settings/organization/profile", {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 13: Run test to verify it passes**

Run: `cd src/web && npx vitest run src/features/admin/settings/hooks/useOrganizationSettings.test.tsx`
Expected: PASS

- [ ] **Step 14: Write the failing proxy route test**

```ts
// src/web/src/app/api/settings/organization/route.test.ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("/api/settings/organization", () => {
  it("GET proxies the organization settings", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ profile: { timeZoneId: "Asia/Kathmandu" } }),
    } as Response);

    const response = await GET();

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/settings/organization");
    expect(response.status).toBe(200);
  });
});
```

- [ ] **Step 15: Run test, confirm it passes** (it should already pass given Step 12's implementation — this step exists to lock the proxy contract with a regression test)

Run: `cd src/web && npx vitest run src/app/api/settings/organization/route.test.ts`
Expected: PASS

- [ ] **Step 16: Build the Settings tab shell and Organization Profile form**

```tsx
// src/web/src/features/admin/settings/components/OrganizationProfileForm.tsx
"use client";

import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { Skeleton } from "@/components/ui/skeleton";
import { useOrganizationSettings } from "@/features/admin/settings/hooks/useOrganizationSettings";
import { useUpdateOrganizationProfile } from "@/features/admin/settings/hooks/useUpdateOrganizationProfile";
import { organizationProfileSchema, type OrganizationProfileFormValues } from "@/features/admin/settings/schema";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";

export function OrganizationProfileForm() {
  const { data, isLoading } = useOrganizationSettings();
  const updateProfile = useUpdateOrganizationProfile();
  const [serverError, setServerError] = useState<string | null>(null);

  const { register, handleSubmit, reset, formState: { errors, isSubmitting } } = useForm<OrganizationProfileFormValues>({
    resolver: zodResolver(organizationProfileSchema),
  });

  useEffect(() => {
    if (data) {
      reset({
        supportEmail: data.profile.supportEmail ?? "",
        supportPhone: data.profile.supportPhone ?? "",
        addressLine: data.profile.addressLine ?? "",
        logoUrl: data.profile.logoUrl ?? "",
        timeZoneId: data.profile.timeZoneId,
        businessHours: data.profile.businessHours ?? "",
      });
    }
  }, [data, reset]);

  async function onSubmit(values: OrganizationProfileFormValues) {
    setServerError(null);
    try {
      await updateProfile.mutateAsync({
        supportEmail: values.supportEmail || null,
        supportPhone: values.supportPhone || null,
        addressLine: values.addressLine || null,
        logoUrl: values.logoUrl || null,
        timeZoneId: values.timeZoneId,
        businessHours: values.businessHours || null,
      });
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  if (isLoading) {
    return (
      <div className="space-y-3" role="status" aria-live="polite">
        <span className="sr-only">Loading</span>
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-10 w-1/2" />
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="max-w-xl space-y-4" noValidate>
      <div className="space-y-2">
        <Label htmlFor="supportEmail">Support Email</Label>
        <Input id="supportEmail" aria-invalid={!!errors.supportEmail} {...register("supportEmail")} />
        {errors.supportEmail && <FieldError id="supportEmail-error">{errors.supportEmail.message}</FieldError>}
      </div>
      <div className="space-y-2">
        <Label htmlFor="supportPhone">Support Phone</Label>
        <Input id="supportPhone" {...register("supportPhone")} />
      </div>
      <div className="space-y-2">
        <Label htmlFor="addressLine">Address</Label>
        <Input id="addressLine" {...register("addressLine")} />
      </div>
      <div className="space-y-2">
        <Label htmlFor="logoUrl">Logo URL</Label>
        <Input id="logoUrl" {...register("logoUrl")} />
      </div>
      <div className="space-y-2">
        <Label htmlFor="timeZoneId">Timezone</Label>
        <Input id="timeZoneId" aria-invalid={!!errors.timeZoneId} {...register("timeZoneId")} />
        {errors.timeZoneId && <FieldError id="timeZoneId-error">{errors.timeZoneId.message}</FieldError>}
      </div>
      <div className="space-y-2">
        <Label htmlFor="businessHours">Business Hours</Label>
        <Input id="businessHours" placeholder="9:00 AM - 6:00 PM" {...register("businessHours")} />
      </div>
      {serverError && <FieldError id="profile-server-error">{serverError}</FieldError>}
      <Button type="submit" disabled={isSubmitting}>
        {isSubmitting ? <Spinner className="h-4 w-4" /> : "Save Profile"}
      </Button>
    </form>
  );
}
```

```tsx
// src/web/src/features/admin/settings/components/SettingsTab.tsx
"use client";

import { useState } from "react";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { OrganizationProfileForm } from "@/features/admin/settings/components/OrganizationProfileForm";

// Nested tabs grow additively, same convention as the outer Admin page's TABS array — a later
// task appends its own TabsTrigger/TabsContent pair here without restructuring this file.
const SUB_TABS = [
  { key: "profile", label: "Profile", permission: "org.settings.manage" },
] as const;

type SubTabKey = (typeof SUB_TABS)[number]["key"];

export function SettingsTab() {
  const canManageSettings = usePermission("org.settings.manage");
  const permitted: Record<SubTabKey, boolean> = { profile: canManageSettings };
  const visibleSubTabs = SUB_TABS.filter((t) => permitted[t.key]);
  const [requestedTab, setRequestedTab] = useState<SubTabKey | null>(null);
  const activeTab = requestedTab && permitted[requestedTab] ? requestedTab : (visibleSubTabs[0]?.key ?? null);

  if (!activeTab) {
    return null;
  }

  return (
    <Tabs value={activeTab} onValueChange={(v) => setRequestedTab(v as SubTabKey)}>
      <TabsList>
        {visibleSubTabs.map((tab) => (
          <TabsTrigger key={tab.key} value={tab.key}>{tab.label}</TabsTrigger>
        ))}
      </TabsList>
      <TabsContent value="profile"><OrganizationProfileForm /></TabsContent>
    </Tabs>
  );
}
```

- [ ] **Step 17: Wire the Settings tab into the Admin page**

In `src/web/src/app/(staff)/admin/page.tsx`:

1. Add the import: `import { SettingsTab } from "@/features/admin/settings/components/SettingsTab";`
2. Append to the `TABS` array (after the `users` entry): `{ key: "settings", label: "Settings", permission: "org.settings.manage" }`
3. Add alongside the existing `usePermission` calls: `const canManageSettings = usePermission("org.settings.manage");`
4. Extend the `permitted` object: `const permitted = { roles: canManageRoles, users: canReadUsers, settings: canManageSettings };`
5. Add a new `<TabsContent value="settings"><SettingsTab /></TabsContent>` after the existing `users` `TabsContent`.

(`notification_template.manage` is deliberately not OR'd into `permitted.settings` yet — Task 7 extends this exact line once the Notification Templates panel exists, per the spec's "tab OR, section AND" split.)

- [ ] **Step 18: Manually verify in the browser**

Run the web dev server (`cd src/web && npm run dev`), log in as a freshly-registered organization's owner (registration grants `org.settings.manage` automatically per Task 1), navigate to `/admin`, confirm the new "Settings" tab appears with a "Profile" sub-tab, submit a change, and confirm it persists across a page reload.

- [ ] **Step 19: Run the full frontend test suite for regressions**

Run: `cd src/web && npx vitest run`
Expected: PASS (no existing test touches `(staff)/admin/page.tsx`'s exact tab count, so this should not break anything, but confirm).

- [ ] **Step 20: Commit frontend**

```bash
git add src/web/src/features/admin/settings src/web/src/app/api/settings src/web/src/app/(staff)/admin/page.tsx
git commit -m "feat(web): add Settings tab with organization profile form"
```

---

## Task 3: Security Policy + Revoke Sessions — backend + frontend

**Files:**
- Modify: `src/Modules/Identity/Application/OrganizationSettingsService.cs` (add `UpdateSecurityAsync`, `RevokeAllSessionsAsync`)
- Modify: `src/Modules/Identity/Presentation/OrganizationSettingsEndpoints.cs` (add two routes)
- Modify: `tests/Identity.Tests/OrganizationSettingsServiceTests.cs`
- Modify: `tests/Api.Tests/OrganizationSettingsEndpointsTests.cs`
- Create: `src/web/src/features/admin/settings/hooks/useUpdateSecurityPolicy.ts`
- Create: `src/web/src/features/admin/settings/hooks/useRevokeAllSessions.ts`
- Create: `src/web/src/features/admin/settings/components/SecurityPolicyForm.tsx`
- Modify: `src/web/src/features/admin/settings/components/SettingsTab.tsx`
- Modify: `src/web/src/features/admin/settings/schema.ts`
- Create: `src/web/src/app/api/settings/organization/security/route.ts`
- Create: `src/web/src/app/api/settings/organization/revoke-sessions/route.ts`

**Interfaces:**
- Consumes: `IOrganizationLookupService.RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken ct)` (already exists, implemented by `OrganizationAdminService`, currently only called from `Courier.Platform.Application.OrganizationLifecycleService`).
- Produces: `OrganizationSettingsService.UpdateSecurityAsync(Guid organizationId, Guid actingUserId, SecurityPolicyDto policy, CancellationToken ct = default) -> Task<SecurityPolicyDto>`, throwing `OrganizationSettingsValidationException` when `RefreshTokenLifetimeDays` is outside `[1,30]` or `MinPasswordLength` is outside `[8,64]`.
- Produces: `OrganizationSettingsService.RevokeAllSessionsAsync(Guid organizationId, Guid actingUserId, CancellationToken ct = default) -> Task`.
- Produces endpoints: `PATCH /api/v1/settings/organization/security`, `POST /api/v1/settings/organization/revoke-sessions` — both `.RequirePermission("org.settings.manage")`.

- [ ] **Step 1: Write the failing backend tests**

Append to `tests/Identity.Tests/OrganizationSettingsServiceTests.cs`:

```csharp
[Theory]
[InlineData(0, 8)]
[InlineData(31, 8)]
[InlineData(7, 7)]
[InlineData(7, 65)]
public async Task UpdateSecurityAsync_OutOfBoundsValues_ThrowsValidationException(int refreshDays, int minPasswordLength)
{
    using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
    var organization = NewOrganization(dbContext);
    await dbContext.SaveChangesAsync();
    var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger());

    await Assert.ThrowsAsync<OrganizationSettingsValidationException>(() =>
        service.UpdateSecurityAsync(organization.Id, Guid.NewGuid(), new SecurityPolicyDto(refreshDays, minPasswordLength)));
}

[Fact]
public async Task UpdateSecurityAsync_ValidValues_PersistsAndWritesAuditEntry()
{
    using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
    var organization = NewOrganization(dbContext);
    await dbContext.SaveChangesAsync();
    var auditLogger = new FakeAuditLogger();
    var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), auditLogger);
    var actingUserId = Guid.NewGuid();

    var result = await service.UpdateSecurityAsync(organization.Id, actingUserId, new SecurityPolicyDto(14, 12));

    Assert.Equal(14, result.RefreshTokenLifetimeDays);
    Assert.Equal(12, result.MinPasswordLength);
    var persisted = dbContext.Set<OrganizationSettings>().IgnoreQueryFilters().Single(s => s.OrganizationId == organization.Id);
    Assert.Equal(14, persisted.RefreshTokenLifetimeDays);
    Assert.Equal(actingUserId, persisted.SecurityUpdatedByUserId);
    Assert.Contains(auditLogger.Entries, e => e.Action == "organization.security_updated");
}

[Fact]
public async Task RevokeAllSessionsAsync_RevokesEveryActiveRefreshTokenForOrganization()
{
    using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
    var organization = NewOrganization(dbContext);
    var user = new User { Id = Guid.NewGuid(), OrganizationId = organization.Id, Email = $"user-{Guid.NewGuid():N}@example.test", PasswordHash = "hash", FullName = "Some User", IsActive = true };
    dbContext.Set<User>().Add(user);
    var token = new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = "hash-value", ExpiresAt = DateTimeOffset.UtcNow.AddDays(7) };
    dbContext.Set<RefreshToken>().Add(token);
    await dbContext.SaveChangesAsync();
    var lookupService = new OrganizationAdminService(dbContext, new FakeAuditLogger());
    var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger(), lookupService);

    await service.RevokeAllSessionsAsync(organization.Id, Guid.NewGuid());

    var reloaded = dbContext.Set<RefreshToken>().Single(rt => rt.Id == token.Id);
    Assert.NotNull(reloaded.RevokedAt);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter "UpdateSecurityAsync|RevokeAllSessionsAsync"`
Expected: FAIL to compile — `UpdateSecurityAsync`/`RevokeAllSessionsAsync` don't exist, and the constructor doesn't yet accept an `IOrganizationLookupService`.

- [ ] **Step 3: Add the `IOrganizationLookupService` dependency and implement both methods**

In `src/Modules/Identity/Application/OrganizationSettingsService.cs`, add the constructor parameter and field:

```csharp
private readonly IOrganizationLookupService _organizationLookupService;

public OrganizationSettingsService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger, IOrganizationLookupService organizationLookupService)
{
    _dbContext = dbContext;
    _currentTenantProvider = currentTenantProvider;
    _auditLogger = auditLogger;
    _organizationLookupService = organizationLookupService;
}
```

(This replaces the 3-parameter constructor from Task 1 — update every existing call site in `OrganizationSettingsServiceTests.cs` from Task 1/Task 2 to pass a fourth argument. Use a shared `new OrganizationAdminService(dbContext, new FakeAuditLogger())` instance for tests that don't exercise `RevokeAllSessionsAsync`, since `IOrganizationLookupService` must be non-null.)

Add the two methods after `UpdateProfileAsync`:

```csharp
public async Task<SecurityPolicyDto> UpdateSecurityAsync(Guid organizationId, Guid actingUserId, SecurityPolicyDto policy, CancellationToken cancellationToken = default)
{
    if (policy.RefreshTokenLifetimeDays is < 1 or > 30)
    {
        throw new OrganizationSettingsValidationException("Refresh token lifetime must be between 1 and 30 days.");
    }
    if (policy.MinPasswordLength is < 8 or > 64)
    {
        throw new OrganizationSettingsValidationException("Minimum password length must be between 8 and 64 characters.");
    }

    var settings = await GetOrCreateAsync(organizationId, cancellationToken);
    var before = new SecurityPolicyDto(settings.RefreshTokenLifetimeDays, settings.MinPasswordLength);

    settings.RefreshTokenLifetimeDays = policy.RefreshTokenLifetimeDays;
    settings.MinPasswordLength = policy.MinPasswordLength;
    settings.SecurityUpdatedAt = DateTimeOffset.UtcNow;
    settings.SecurityUpdatedByUserId = actingUserId;
    await _dbContext.SaveChangesAsync(cancellationToken);

    await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "organization.security_updated", "OrganizationSettings", organizationId.ToString(),
        Before: System.Text.Json.JsonSerializer.Serialize(before), After: System.Text.Json.JsonSerializer.Serialize(policy)), cancellationToken);

    return new SecurityPolicyDto(settings.RefreshTokenLifetimeDays, settings.MinPasswordLength);
}

public async Task RevokeAllSessionsAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default)
{
    await _organizationLookupService.RevokeStaffRefreshTokensAsync(organizationId, cancellationToken);
    await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "organization.sessions_revoked", "OrganizationSettings", organizationId.ToString()), cancellationToken);
}
```

- [ ] **Step 4: Update `Program.cs` DI (no change needed)**

`IOrganizationLookupService` is already registered (`builder.Services.AddScoped<IOrganizationLookupService, OrganizationAdminService>();`) — the constructor change resolves automatically. Skip to Step 5.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter "UpdateSecurityAsync|RevokeAllSessionsAsync|GetAsync_|GetSecurityPolicyAsync_|UpdateProfileAsync_"`
Expected: PASS (this also re-runs Task 1/2's tests to confirm the constructor-signature change didn't break them).

- [ ] **Step 6: Write the failing API integration tests**

Append to `tests/Api.Tests/OrganizationSettingsEndpointsTests.cs`:

```csharp
[Fact]
public async Task PatchSecurity_InvalidRefreshDays_Returns400()
{
    var client = _factory.CreateClient();
    var auth = await RegisterOrgAsync(client);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

    var response = await client.PatchAsJsonAsync("/api/v1/settings/organization/security", new SecurityPolicyDto(0, 8));

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("ORGANIZATION_SETTINGS_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
}

[Fact]
public async Task PatchSecurity_ValidValues_UpdatesSettings()
{
    var client = _factory.CreateClient();
    var auth = await RegisterOrgAsync(client);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

    var response = await client.PatchAsJsonAsync("/api/v1/settings/organization/security", new SecurityPolicyDto(14, 10));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var getResponse = await client.GetAsync("/api/v1/settings/organization");
    var body = await getResponse.Content.ReadFromJsonAsync<OrganizationSettingsDto>();
    Assert.Equal(14, body!.Security.RefreshTokenLifetimeDays);
}

[Fact]
public async Task RevokeSessions_ExistingRefreshToken_IsRevoked()
{
    var client = _factory.CreateClient();
    var auth = await RegisterOrgAsync(client);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

    var revokeResponse = await client.PostAsync("/api/v1/settings/organization/revoke-sessions", null);
    Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

    // The owner's own already-issued refresh token from registration must now be revoked —
    // exercised end-to-end via the public /auth/refresh endpoint rather than reaching into the
    // database, matching how this test suite already treats RefreshToken as an implementation
    // detail elsewhere.
    var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
    Assert.Equal(HttpStatusCode.BadRequest, refreshResponse.StatusCode);
}
```

Add `using System.Text.Json;` to the top of the file if not already present.

- [ ] **Step 7: Run tests to verify they fail**

Run: `dotnet build Courier.slnx`
Expected: FAIL to compile — the two new routes don't exist yet.

- [ ] **Step 8: Implement the two endpoints**

Add to `src/Modules/Identity/Presentation/OrganizationSettingsEndpoints.cs`, inside `MapOrganizationSettingsEndpoints`, after the `/profile` route:

```csharp
group.MapPatch("/security", async (SecurityPolicyDto request, OrganizationSettingsService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
{
    var organizationId = tenantProvider.OrganizationId!.Value;
    var actingUserId = httpContext.User.GetUserId()!.Value;
    try
    {
        return Results.Ok(await service.UpdateSecurityAsync(organizationId, actingUserId, request, httpContext.RequestAborted));
    }
    catch (OrganizationSettingsValidationException ex)
    {
        return Results.BadRequest(new { error = new { code = "ORGANIZATION_SETTINGS_VALIDATION_FAILED", message = ex.Message } });
    }
}).RequirePermission("org.settings.manage");

group.MapPost("/revoke-sessions", async (OrganizationSettingsService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
{
    var organizationId = tenantProvider.OrganizationId!.Value;
    var actingUserId = httpContext.User.GetUserId()!.Value;
    await service.RevokeAllSessionsAsync(organizationId, actingUserId, httpContext.RequestAborted);
    return Results.Ok();
}).RequirePermission("org.settings.manage");
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj --filter OrganizationSettingsEndpointsTests`
Expected: PASS

- [ ] **Step 10: Commit backend**

```bash
git add src/Modules/Identity/Application/OrganizationSettingsService.cs src/Modules/Identity/Presentation/OrganizationSettingsEndpoints.cs tests/Identity.Tests/OrganizationSettingsServiceTests.cs tests/Api.Tests/OrganizationSettingsEndpointsTests.cs
git commit -m "feat(identity): add security policy and revoke-sessions to organization settings"
```

- [ ] **Step 11: Add the security policy schema and hooks**

Append to `src/web/src/features/admin/settings/schema.ts`:

```ts
export const securityPolicySchema = z.object({
  refreshTokenLifetimeDays: z.coerce.number().int().min(1, "Must be at least 1 day.").max(30, "Must be at most 30 days."),
  minPasswordLength: z.coerce.number().int().min(8, "Must be at least 8 characters.").max(64, "Must be at most 64 characters."),
});

export type SecurityPolicyFormValues = z.infer<typeof securityPolicySchema>;
```

```ts
// src/web/src/features/admin/settings/hooks/useUpdateSecurityPolicy.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { SecurityPolicy } from "@/features/admin/settings/types";

export function useUpdateSecurityPolicy() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: SecurityPolicy) =>
      clientFetch<SecurityPolicy>("/api/settings/organization/security", {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["organization-settings"] }),
  });
}
```

```ts
// src/web/src/features/admin/settings/hooks/useRevokeAllSessions.ts
"use client";

import { useMutation } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useRevokeAllSessions() {
  return useMutation({
    mutationFn: () => clientFetch<void>("/api/settings/organization/revoke-sessions", { method: "POST" }),
  });
}
```

```ts
// src/web/src/app/api/settings/organization/security/route.ts
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function PATCH(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/settings/organization/security", {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```ts
// src/web/src/app/api/settings/organization/revoke-sessions/route.ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST() {
  try {
    const response = await apiFetch("/api/v1/settings/organization/revoke-sessions", { method: "POST" });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 12: Build the Security Policy form with the Revoke Sessions button**

```tsx
// src/web/src/features/admin/settings/components/SecurityPolicyForm.tsx
"use client";

import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { Skeleton } from "@/components/ui/skeleton";
import { Separator } from "@/components/ui/separator";
import { useOrganizationSettings } from "@/features/admin/settings/hooks/useOrganizationSettings";
import { useUpdateSecurityPolicy } from "@/features/admin/settings/hooks/useUpdateSecurityPolicy";
import { useRevokeAllSessions } from "@/features/admin/settings/hooks/useRevokeAllSessions";
import { securityPolicySchema, type SecurityPolicyFormValues } from "@/features/admin/settings/schema";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";

export function SecurityPolicyForm() {
  const { data, isLoading } = useOrganizationSettings();
  const updateSecurity = useUpdateSecurityPolicy();
  const revokeAllSessions = useRevokeAllSessions();
  const [serverError, setServerError] = useState<string | null>(null);
  const [revokeResult, setRevokeResult] = useState<"success" | "error" | null>(null);

  const { register, handleSubmit, reset, formState: { errors, isSubmitting } } = useForm<SecurityPolicyFormValues>({
    resolver: zodResolver(securityPolicySchema),
  });

  useEffect(() => {
    if (data) {
      reset({ refreshTokenLifetimeDays: data.security.refreshTokenLifetimeDays, minPasswordLength: data.security.minPasswordLength });
    }
  }, [data, reset]);

  async function onSubmit(values: SecurityPolicyFormValues) {
    setServerError(null);
    try {
      await updateSecurity.mutateAsync(values);
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  async function handleRevokeAllSessions() {
    setRevokeResult(null);
    try {
      await revokeAllSessions.mutateAsync();
      setRevokeResult("success");
    } catch {
      setRevokeResult("error");
    }
  }

  if (isLoading) {
    return (
      <div className="space-y-3" role="status" aria-live="polite">
        <span className="sr-only">Loading</span>
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-10 w-full" />
      </div>
    );
  }

  return (
    <div className="max-w-xl space-y-6">
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
        <div className="space-y-2">
          <Label htmlFor="refreshTokenLifetimeDays">Session Length (days)</Label>
          <Input id="refreshTokenLifetimeDays" type="number" min={1} max={30} aria-invalid={!!errors.refreshTokenLifetimeDays} {...register("refreshTokenLifetimeDays")} />
          {errors.refreshTokenLifetimeDays && <FieldError id="refreshTokenLifetimeDays-error">{errors.refreshTokenLifetimeDays.message}</FieldError>}
        </div>
        <div className="space-y-2">
          <Label htmlFor="minPasswordLength">Minimum Password Length</Label>
          <Input id="minPasswordLength" type="number" min={8} max={64} aria-invalid={!!errors.minPasswordLength} {...register("minPasswordLength")} />
          {errors.minPasswordLength && <FieldError id="minPasswordLength-error">{errors.minPasswordLength.message}</FieldError>}
        </div>
        {serverError && <FieldError id="security-server-error">{serverError}</FieldError>}
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? <Spinner className="h-4 w-4" /> : "Save Security Policy"}
        </Button>
      </form>
      <Separator />
      <div className="space-y-2">
        <Label>Revoke All Sessions</Label>
        <p className="text-sm text-muted-foreground">Immediately signs out every staff member in your organization. They will need to log in again.</p>
        <Button type="button" variant="destructive" onClick={handleRevokeAllSessions} disabled={revokeAllSessions.isPending}>
          {revokeAllSessions.isPending ? <Spinner className="h-4 w-4" /> : "Revoke All Sessions"}
        </Button>
        {revokeResult === "success" && <p className="text-sm text-status-delivered">All sessions revoked.</p>}
        {revokeResult === "error" && <FieldError id="revoke-sessions-error">Couldn't revoke sessions. Try again.</FieldError>}
      </div>
    </div>
  );
}
```

- [ ] **Step 13: Wire the Security panel into `SettingsTab`**

In `src/web/src/features/admin/settings/components/SettingsTab.tsx`:
1. Add the import: `import { SecurityPolicyForm } from "@/features/admin/settings/components/SecurityPolicyForm";`
2. Add to `SUB_TABS`: `{ key: "security", label: "Security", permission: "org.settings.manage" }`
3. Extend the `permitted` record: `{ profile: canManageSettings, security: canManageSettings }`
4. Add `<TabsContent value="security"><SecurityPolicyForm /></TabsContent>` after the `profile` `TabsContent`.

- [ ] **Step 14: Manually verify in the browser**

With the dev server running, open Settings → Security, change the session length, save, reload and confirm it persisted. Click "Revoke All Sessions" and confirm the success message appears (a full end-to-end revoke verification — that the owner's own session actually ends — is covered by the backend integration test in Step 6, not manual UI testing, since it would log the tester out of their own testing session).

- [ ] **Step 15: Run the full frontend test suite for regressions**

Run: `cd src/web && npx vitest run`
Expected: PASS

- [ ] **Step 16: Commit frontend**

```bash
git add src/web/src/features/admin/settings src/web/src/app/api/settings
git commit -m "feat(web): add security policy form and revoke-all-sessions action"
```

---

## Task 4: `AuthService` enforces configured refresh-token lifetime and minimum password length

**Files:**
- Modify: `src/Modules/Identity/Application/AuthService.cs`
- Modify: `tests/Identity.Tests/AuthServiceTests.cs`
- Modify: `tests/Identity.Tests/AuthServiceAcceptInviteTests.cs`
- Modify: `tests/Api.Tests/AuthEndpointsTests.cs` (compile fix only, if it constructs `AuthService` directly — verify during Step 2)

**Interfaces:**
- Consumes: `OrganizationSettingsService.GetSecurityPolicyAsync(Guid organizationId, CancellationToken ct) -> Task<SecurityPolicyDto>` (Task 1).
- Changes: `AuthService`'s constructor gains a 7th parameter, `OrganizationSettingsService organizationSettingsService`. `IssueRefreshTokenAsync(Guid userId, CancellationToken ct)` becomes `IssueRefreshTokenAsync(Guid userId, Guid organizationId, CancellationToken ct)` — a private method, so only this file's four call sites need updating.

- [ ] **Step 1: Write the failing tests**

`tests/Identity.Tests/AuthServiceTests.cs` already has a `CreateService(AppDbContext dbContext)` helper (constructs `AuthService` with its 6 current constructor args) and a `RegisterOrganizationAsync_ThenLoginAsync_Succeeds` test showing the register→login shape to reuse. Append:

```csharp
[Fact]
public async Task LoginAsync_UsesOrganizationConfiguredRefreshTokenLifetime()
{
    await using var dbContext = CreateContext();
    var service = CreateService(dbContext);
    var suffix = Guid.NewGuid().ToString("N")[..8];
    var registerRequest = new RegisterOrganizationRequest($"Org {suffix}", $"org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
    var registered = await service.RegisterOrganizationAsync(registerRequest);

    var settingsService = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new NoopAuditLogger(), new OrganizationAdminService(dbContext, new NoopAuditLogger()));
    await settingsService.UpdateSecurityAsync(registered.OrganizationId, Guid.NewGuid(), new SecurityPolicyDto(1, 8));

    var loginResult = await service.LoginAsync(new LoginRequest($"owner-{suffix}@example.com", "P@ssw0rd123!"));

    var token = dbContext.Set<RefreshToken>().OrderByDescending(t => t.ExpiresAt).First(t => t.UserId == loginResult.UserId);
    Assert.InRange(token.ExpiresAt, DateTimeOffset.UtcNow.AddDays(1).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1).AddMinutes(1));
}
```

`tests/Identity.Tests/AuthServiceAcceptInviteTests.cs` already has `CreateService(AppDbContext dbContext)` and `SeedInvitedUserAsync(dbContext, ...)` returning `(Guid OrganizationId, Guid UserId, string PlainTextToken)`. Append:

```csharp
[Fact]
public async Task AcceptInviteAsync_PasswordShorterThanOrganizationMinimum_ThrowsAuthenticationFailedException()
{
    await using var dbContext = CreateContext();
    var (organizationId, _, plainTextToken) = await SeedInvitedUserAsync(dbContext);
    var settingsService = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new NoopAuditLogger(), new OrganizationAdminService(dbContext, new NoopAuditLogger()));
    await settingsService.UpdateSecurityAsync(organizationId, Guid.NewGuid(), new SecurityPolicyDto(7, 12));
    var service = CreateService(dbContext);

    await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
        service.AcceptInviteAsync(new AcceptInviteRequest(plainTextToken, "short7!")));
}

[Fact]
public async Task AcceptInviteAsync_PasswordAtOrganizationMinimum_Succeeds()
{
    await using var dbContext = CreateContext();
    var (organizationId, userId, plainTextToken) = await SeedInvitedUserAsync(dbContext);
    var settingsService = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new NoopAuditLogger(), new OrganizationAdminService(dbContext, new NoopAuditLogger()));
    await settingsService.UpdateSecurityAsync(organizationId, Guid.NewGuid(), new SecurityPolicyDto(7, 12));
    var service = CreateService(dbContext);

    var result = await service.AcceptInviteAsync(new AcceptInviteRequest(plainTextToken, "twelve-chars"));

    Assert.Equal(userId, result.UserId);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build Courier.slnx`
Expected: FAIL to compile — `AuthService`'s constructor doesn't yet accept `OrganizationSettingsService`, so both test files' `CreateService`/direct-construction call sites fail to compile too. `dotnet build`'s error list is the authoritative source of every call site that needs the Step 4 fix — don't rely on this plan's own list being exhaustive.

- [ ] **Step 3: Update the constructor and both call-affecting methods**

In `src/Modules/Identity/Application/AuthService.cs`:

```csharp
private readonly OrganizationSettingsService _organizationSettingsService;

public AuthService(DbContext dbContext, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService, IAuditLogger auditLogger, IBranchLookupService branchLookupService, IOrganizationSubscriptionStarter organizationSubscriptionStarter, OrganizationSettingsService organizationSettingsService)
{
    _dbContext = dbContext;
    _passwordHasher = passwordHasher;
    _jwtTokenService = jwtTokenService;
    _auditLogger = auditLogger;
    _branchLookupService = branchLookupService;
    _organizationSubscriptionStarter = organizationSubscriptionStarter;
    _organizationSettingsService = organizationSettingsService;
}
```

Change `IssueRefreshTokenAsync`:

```csharp
private async Task<(RefreshToken Entity, string PlainText)> IssueRefreshTokenAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
{
    var securityPolicy = await _organizationSettingsService.GetSecurityPolicyAsync(organizationId, cancellationToken);
    var plainText = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    var entity = new RefreshToken
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = HashToken(plainText),
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(securityPolicy.RefreshTokenLifetimeDays)
    };
    _dbContext.Set<RefreshToken>().Add(entity);
    return (entity, plainText);
}
```

Update each of the four call sites (search the file for `IssueRefreshTokenAsync(` to find all of them):
- `RegisterOrganizationAsync`, currently `await IssueRefreshTokenAsync(owner.Id, cancellationToken);` → `await IssueRefreshTokenAsync(owner.Id, organization.Id, cancellationToken);`
- `LoginAsync`, currently `await IssueRefreshTokenAsync(user.Id, cancellationToken);` → `await IssueRefreshTokenAsync(user.Id, user.OrganizationId.Value, cancellationToken);`
- `RefreshTokenAsync`, currently `await IssueRefreshTokenAsync(user.Id, cancellationToken);` → `await IssueRefreshTokenAsync(user.Id, user.OrganizationId!.Value, cancellationToken);`
- `AcceptInviteAsync`, currently `await IssueRefreshTokenAsync(user.Id, cancellationToken);` → `await IssueRefreshTokenAsync(user.Id, user.OrganizationId!.Value, cancellationToken);`

In `AcceptInviteAsync`, add the minimum-password-length check immediately after the `user` lookup (`var user = await _dbContext.Set<User>().IgnoreQueryFilters().FirstAsync(...)`) and before `user.PasswordHash = _passwordHasher.Hash(request.Password);`:

```csharp
var securityPolicy = await _organizationSettingsService.GetSecurityPolicyAsync(user.OrganizationId!.Value, cancellationToken);
if (request.Password.Length < securityPolicy.MinPasswordLength)
{
    throw new AuthenticationFailedException($"Password must be at least {securityPolicy.MinPasswordLength} characters.");
}
```

- [ ] **Step 4: Fix every other broken call site**

`tests/Api.Tests/AuthEndpointsTests.cs` never constructs `AuthService` directly (it resolves everything through `WebApplicationFactory<Program>`, whose DI container already supplies every dependency via `Program.cs`'s registrations, unchanged by this task) — nothing to fix there.

In `tests/Identity.Tests/AuthServiceTests.cs`, add the extra constructor argument at all three `new AuthService(...)` call sites:
- Line ~71, inside `CreateService`: append `, new OrganizationSettingsService(dbContext, new NullTenantProvider(), new NoopAuditLogger(), new OrganizationAdminService(dbContext, new NoopAuditLogger()))` before the closing `)`.
- Line ~175 (the test constructing `AuthService` directly with a custom `subscriptionStarter`): same append.
- Line ~199 (the test using `ThrowingOrganizationSubscriptionStarter`): same append.

In `tests/Identity.Tests/AuthServiceAcceptInviteTests.cs`, the single `new AuthService(...)` call site inside `CreateService` (line ~51) gets the same append.

Run: `dotnet build Courier.slnx` after making these edits and confirm the error list is now empty — if it names any other file, apply the identical fix there (same append, same `NullTenantProvider`/`NoopAuditLogger`/`OrganizationAdminService` construction each file already uses for its other fakes).

- [ ] **Step 5: Run the full Identity test suite**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj`
Expected: PASS — every test, not just the two new ones, confirming the constructor change didn't silently break an unrelated existing test.

- [ ] **Step 6: Run the full Api test suite for the same reason**

Run: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Identity/Application/AuthService.cs tests/Identity.Tests/AuthServiceTests.cs tests/Identity.Tests/AuthServiceAcceptInviteTests.cs
git commit -m "feat(identity): enforce per-organization refresh-token lifetime and minimum password length"
```

---

## Task 5: Operational Defaults — `IServiceTypeLookupService` + backend + frontend

**Files:**
- Create: `src/Modules/Pricing/Application/IServiceTypeLookupService.cs`
- Modify: `src/Modules/Pricing/Application/PricingService.cs`
- Modify: `src/Modules/Identity/Identity.csproj` (add `ProjectReference` to `Pricing.csproj`)
- Modify: `src/Modules/Identity/Application/OrganizationSettingsService.cs` (add `UpdateDefaultsAsync`)
- Modify: `src/Modules/Identity/Presentation/OrganizationSettingsEndpoints.cs`
- Modify: `src/Api/Program.cs` (register `IServiceTypeLookupService`)
- Create: `tests/Pricing.Tests/PricingServiceServiceTypeLookupTests.cs`
- Modify: `tests/Identity.Tests/OrganizationSettingsServiceTests.cs`
- Modify: `tests/Api.Tests/OrganizationSettingsEndpointsTests.cs`
- Create: `src/web/src/features/admin/settings/hooks/useUpdateOperationalDefaults.ts`
- Create: `src/web/src/features/admin/settings/components/OperationalDefaultsForm.tsx`
- Modify: `src/web/src/features/admin/settings/components/SettingsTab.tsx`
- Create: `src/web/src/app/api/settings/organization/defaults/route.ts`

**Interfaces:**
- Produces: `Courier.Pricing.Application.IServiceTypeLookupService.ExistsAsync(Guid serviceTypeId, CancellationToken ct = default) -> Task<bool>`, implemented directly on `PricingService` (same "service class implements its own lookup interface" pattern as `BranchService : IBranchLookupService`).
- Consumes: `IBranchLookupService.GetZoneInfoAsync(Guid branchId, CancellationToken ct) -> Task<BranchZoneInfo?>` (already exists; a non-null result means the branch exists — `Identity.csproj` already references `Branches.csproj`, no new reference needed for this part).
- Produces: `OrganizationSettingsService.UpdateDefaultsAsync(Guid organizationId, Guid actingUserId, OperationalDefaultsDto defaults, CancellationToken ct = default) -> Task<OperationalDefaultsDto>`, throwing `OrganizationSettingsValidationException` when a non-null `DefaultOriginBranchId` doesn't resolve via `IBranchLookupService.GetZoneInfoAsync` or a non-null `DefaultServiceTypeId` doesn't resolve via `IServiceTypeLookupService.ExistsAsync`.
- Produces endpoint: `PATCH /api/v1/settings/organization/defaults`, `.RequirePermission("org.settings.manage")`.

- [ ] **Step 1: Write the failing `IServiceTypeLookupService` test**

Unlike Identity/Cod/Billing, `Pricing.Tests` does **not** use a shared-Postgres `TestDbContextFactory` — `PricingServiceTests.cs` defines its own EF Core InMemory `TestDbContext` (applying just the Pricing module's own `IEntityTypeConfiguration`s) and calls `EnsureCreated()`, which also applies each configuration's `HasData` seed — `ServiceTypeConfiguration`'s seed already includes a `ServiceTypeCode.Standard` row, which is why `PricingServiceTests.cs` can do `dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard)` right after `CreateContext()` with no manual insert. Reuse that exact pattern:

```csharp
// tests/Pricing.Tests/PricingServiceServiceTypeLookupTests.cs
using Courier.Branches.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Pricing.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NullBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult<BranchZoneInfo?>(null);
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

public class PricingServiceServiceTypeLookupTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ServiceTypeConfiguration());
            modelBuilder.ApplyConfiguration(new WeightSlabConfiguration());
            modelBuilder.ApplyConfiguration(new RateCardConfiguration());
            modelBuilder.ApplyConfiguration(new ZoneRateConfiguration());
            modelBuilder.ApplyConfiguration(new AdditionalChargeConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantRateCardConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task ExistsAsync_KnownSeededServiceType_ReturnsTrue()
    {
        await using var dbContext = CreateContext();
        var standardId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        IServiceTypeLookupService service = new PricingService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NullBranchLookup(), new AlwaysTrueMerchantLookup());

        Assert.True(await service.ExistsAsync(standardId));
    }

    [Fact]
    public async Task ExistsAsync_UnknownServiceType_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        IServiceTypeLookupService service = new PricingService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NullBranchLookup(), new AlwaysTrueMerchantLookup());

        Assert.False(await service.ExistsAsync(Guid.NewGuid()));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Pricing.Tests/Pricing.Tests.csproj --filter PricingServiceServiceTypeLookupTests`
Expected: FAIL to compile — `IServiceTypeLookupService` doesn't exist and `PricingService` doesn't implement it.

- [ ] **Step 3: Add the interface and implement it on `PricingService`**

```csharp
// src/Modules/Pricing/Application/IServiceTypeLookupService.cs
namespace Courier.Pricing.Application;

public interface IServiceTypeLookupService
{
    // ServiceType is global reference data (no OrganizationId — see ServiceType.cs's own doc
    // comment), so this check is deliberately not tenant-scoped.
    Task<bool> ExistsAsync(Guid serviceTypeId, CancellationToken cancellationToken = default);
}
```

In `src/Modules/Pricing/Application/PricingService.cs`, change the class declaration to `public class PricingService : IServiceTypeLookupService` and add, just before the class's closing brace:

```csharp
public async Task<bool> ExistsAsync(Guid serviceTypeId, CancellationToken cancellationToken = default)
    => await _dbContext.Set<Courier.Pricing.Domain.ServiceType>().AnyAsync(s => s.Id == serviceTypeId, cancellationToken);
```

- [ ] **Step 4: Register the interface mapping in `Program.cs`**

Immediately after the existing `builder.Services.AddScoped<PricingService>();` line, add:

```csharp
builder.Services.AddScoped<IServiceTypeLookupService>(sp => sp.GetRequiredService<PricingService>());
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Pricing.Tests/Pricing.Tests.csproj --filter PricingServiceServiceTypeLookupTests`
Expected: PASS

- [ ] **Step 6: Add the `Identity` → `Pricing` project reference**

In `src/Modules/Identity/Identity.csproj`, add inside the existing `<ItemGroup>` of `ProjectReference` entries:

```xml
<ProjectReference Include="..\Pricing\Pricing.csproj" />
```

Run: `dotnet build Courier.slnx` and confirm this introduces no circular-reference error (expected — `Pricing.csproj` does not reference `Identity.csproj`, verified in Global Constraints).

- [ ] **Step 7: Write the failing `UpdateDefaultsAsync` tests**

Append to `tests/Identity.Tests/OrganizationSettingsServiceTests.cs`. This file's `OrganizationSettingsService` constructor now takes 4 args (Task 3) — these tests add a 5th and 6th (`IBranchLookupService`, `IServiceTypeLookupService`), so also update every existing `new OrganizationSettingsService(...)` call in this file to append two more fake arguments once Step 8 changes the constructor:

```csharp
private sealed class StubBranchLookupService : IBranchLookupService
{
    private readonly HashSet<Guid> _existingBranchIds;
    public StubBranchLookupService(params Guid[] existingBranchIds) => _existingBranchIds = existingBranchIds.ToHashSet();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_existingBranchIds.Contains(branchId) ? new BranchZoneInfo(branchId, null) : null);
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

private sealed class StubServiceTypeLookupService : IServiceTypeLookupService
{
    private readonly HashSet<Guid> _existingIds;
    public StubServiceTypeLookupService(params Guid[] existingIds) => _existingIds = existingIds.ToHashSet();
    public Task<bool> ExistsAsync(Guid serviceTypeId, CancellationToken cancellationToken = default) => Task.FromResult(_existingIds.Contains(serviceTypeId));
}

[Fact]
public async Task UpdateDefaultsAsync_UnknownBranch_ThrowsValidationException()
{
    using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
    var organization = NewOrganization(dbContext);
    await dbContext.SaveChangesAsync();
    var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger(), new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(), new StubServiceTypeLookupService());

    await Assert.ThrowsAsync<OrganizationSettingsValidationException>(() =>
        service.UpdateDefaultsAsync(organization.Id, Guid.NewGuid(), new OperationalDefaultsDto(null, Guid.NewGuid())));
}

[Fact]
public async Task UpdateDefaultsAsync_UnknownServiceType_ThrowsValidationException()
{
    using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
    var organization = NewOrganization(dbContext);
    await dbContext.SaveChangesAsync();
    var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger(), new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(), new StubServiceTypeLookupService());

    await Assert.ThrowsAsync<OrganizationSettingsValidationException>(() =>
        service.UpdateDefaultsAsync(organization.Id, Guid.NewGuid(), new OperationalDefaultsDto(Guid.NewGuid(), null)));
}

[Fact]
public async Task UpdateDefaultsAsync_ValidIds_PersistsAndWritesAuditEntry()
{
    using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
    var organization = NewOrganization(dbContext);
    await dbContext.SaveChangesAsync();
    var branchId = Guid.NewGuid();
    var serviceTypeId = Guid.NewGuid();
    var auditLogger = new FakeAuditLogger();
    var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), auditLogger, new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(branchId), new StubServiceTypeLookupService(serviceTypeId));

    var result = await service.UpdateDefaultsAsync(organization.Id, Guid.NewGuid(), new OperationalDefaultsDto(serviceTypeId, branchId));

    Assert.Equal(serviceTypeId, result.DefaultServiceTypeId);
    Assert.Equal(branchId, result.DefaultOriginBranchId);
    Assert.Contains(auditLogger.Entries, e => e.Action == "organization.defaults_updated");
}

[Fact]
public async Task UpdateDefaultsAsync_NullIds_ClearsDefaultsWithoutValidation()
{
    using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
    var organization = NewOrganization(dbContext);
    await dbContext.SaveChangesAsync();
    var service = new OrganizationSettingsService(dbContext, new NullTenantProvider(), new FakeAuditLogger(), new OrganizationAdminService(dbContext, new FakeAuditLogger()), new StubBranchLookupService(), new StubServiceTypeLookupService());

    var result = await service.UpdateDefaultsAsync(organization.Id, Guid.NewGuid(), new OperationalDefaultsDto(null, null));

    Assert.Null(result.DefaultServiceTypeId);
    Assert.Null(result.DefaultOriginBranchId);
}
```

- [ ] **Step 8: Run tests to verify they fail**

Run: `dotnet build Courier.slnx`
Expected: FAIL to compile — `UpdateDefaultsAsync` doesn't exist and the constructor doesn't take 6 args yet.

- [ ] **Step 9: Extend the constructor and implement `UpdateDefaultsAsync`**

In `src/Modules/Identity/Application/OrganizationSettingsService.cs`, add the using and two fields/params:

```csharp
using Courier.Branches.Application;
using Courier.Pricing.Application;
```

```csharp
private readonly IBranchLookupService _branchLookupService;
private readonly IServiceTypeLookupService _serviceTypeLookupService;

public OrganizationSettingsService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger, IOrganizationLookupService organizationLookupService, IBranchLookupService branchLookupService, IServiceTypeLookupService serviceTypeLookupService)
{
    _dbContext = dbContext;
    _currentTenantProvider = currentTenantProvider;
    _auditLogger = auditLogger;
    _organizationLookupService = organizationLookupService;
    _branchLookupService = branchLookupService;
    _serviceTypeLookupService = serviceTypeLookupService;
}
```

Add the method after `RevokeAllSessionsAsync`:

```csharp
public async Task<OperationalDefaultsDto> UpdateDefaultsAsync(Guid organizationId, Guid actingUserId, OperationalDefaultsDto defaults, CancellationToken cancellationToken = default)
{
    if (defaults.DefaultOriginBranchId is Guid branchId && await _branchLookupService.GetZoneInfoAsync(branchId, cancellationToken) is null)
    {
        throw new OrganizationSettingsValidationException("Default origin branch does not exist.");
    }
    if (defaults.DefaultServiceTypeId is Guid serviceTypeId && !await _serviceTypeLookupService.ExistsAsync(serviceTypeId, cancellationToken))
    {
        throw new OrganizationSettingsValidationException("Default service type does not exist.");
    }

    var settings = await GetOrCreateAsync(organizationId, cancellationToken);
    var before = new OperationalDefaultsDto(settings.DefaultServiceTypeId, settings.DefaultOriginBranchId);

    settings.DefaultServiceTypeId = defaults.DefaultServiceTypeId;
    settings.DefaultOriginBranchId = defaults.DefaultOriginBranchId;
    settings.DefaultsUpdatedAt = DateTimeOffset.UtcNow;
    settings.DefaultsUpdatedByUserId = actingUserId;
    await _dbContext.SaveChangesAsync(cancellationToken);

    await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "organization.defaults_updated", "OrganizationSettings", organizationId.ToString(),
        Before: System.Text.Json.JsonSerializer.Serialize(before), After: System.Text.Json.JsonSerializer.Serialize(defaults)), cancellationToken);

    return new OperationalDefaultsDto(settings.DefaultServiceTypeId, settings.DefaultOriginBranchId);
}
```

Update every other existing `new OrganizationSettingsService(...)` call site across `tests/Identity.Tests/OrganizationSettingsServiceTests.cs` and `tests/Identity.Tests/AuthServiceTests.cs`/`AuthServiceAcceptInviteTests.cs` (from Tasks 1-4) to append two more arguments: a permissive stub branch lookup and a permissive stub service-type lookup. Since `AuthServiceTests.cs`/`AuthServiceAcceptInviteTests.cs` only ever call `GetSecurityPolicyAsync` (never `UpdateDefaultsAsync`), a minimal always-throws-if-called stub is fine there — reuse this file's own `StubBranchLookupService`/`StubServiceTypeLookupService` (both default to "nothing exists" when constructed with no ids, which is fine since those two test files never touch defaults).

- [ ] **Step 10: Run tests to verify they pass**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj`
Expected: PASS (full module run, not just the filter, to catch every call site touched in Step 9).

- [ ] **Step 11: Write the failing API integration tests**

Append to `tests/Api.Tests/OrganizationSettingsEndpointsTests.cs`:

```csharp
[Fact]
public async Task PatchDefaults_UnknownServiceType_Returns400()
{
    var client = _factory.CreateClient();
    var auth = await RegisterOrgAsync(client);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

    var response = await client.PatchAsJsonAsync("/api/v1/settings/organization/defaults", new OperationalDefaultsDto(Guid.NewGuid(), null));

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task PatchDefaults_ValidServiceType_UpdatesSettings()
{
    var client = _factory.CreateClient();
    var auth = await RegisterOrgAsync(client);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
    var serviceTypesResponse = await client.GetAsync("/api/v1/service-types");
    var serviceTypes = await serviceTypesResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
    var serviceTypeId = serviceTypes![0].GetProperty("id").GetGuid();

    var response = await client.PatchAsJsonAsync("/api/v1/settings/organization/defaults", new OperationalDefaultsDto(serviceTypeId, null));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var getResponse = await client.GetAsync("/api/v1/settings/organization");
    var body = await getResponse.Content.ReadFromJsonAsync<OrganizationSettingsDto>();
    Assert.Equal(serviceTypeId, body!.Defaults.DefaultServiceTypeId);
}
```

The route is `GET /api/v1/service-types` (`PricingEndpoints.cs`, dual-auth staff/merchant, requires `pricing.read` for a staff bearer token) — `ServiceType` is global reference data seeded once at startup, and the newly-registered `OrganizationOwner` token already holds every permission including `pricing.read`, so no extra setup is needed before this call.

- [ ] **Step 12: Run tests to verify they fail, then implement the endpoint**

Run: `dotnet build Courier.slnx` — expect a compile failure until the route exists.

Add to `src/Modules/Identity/Presentation/OrganizationSettingsEndpoints.cs`, after the `/security` route:

```csharp
group.MapPatch("/defaults", async (OperationalDefaultsDto request, OrganizationSettingsService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
{
    var organizationId = tenantProvider.OrganizationId!.Value;
    var actingUserId = httpContext.User.GetUserId()!.Value;
    try
    {
        return Results.Ok(await service.UpdateDefaultsAsync(organizationId, actingUserId, request, httpContext.RequestAborted));
    }
    catch (OrganizationSettingsValidationException ex)
    {
        return Results.BadRequest(new { error = new { code = "ORGANIZATION_SETTINGS_VALIDATION_FAILED", message = ex.Message } });
    }
}).RequirePermission("org.settings.manage");
```

- [ ] **Step 13: Run tests to verify they pass**

Run: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj --filter OrganizationSettingsEndpointsTests`
Expected: PASS

- [ ] **Step 14: Commit backend**

```bash
git add src/Modules/Pricing/Application/IServiceTypeLookupService.cs src/Modules/Pricing/Application/PricingService.cs src/Modules/Identity/Identity.csproj src/Modules/Identity/Application/OrganizationSettingsService.cs src/Modules/Identity/Presentation/OrganizationSettingsEndpoints.cs src/Api/Program.cs tests/Pricing.Tests/PricingServiceServiceTypeLookupTests.cs tests/Identity.Tests/OrganizationSettingsServiceTests.cs tests/Identity.Tests/AuthServiceTests.cs tests/Identity.Tests/AuthServiceAcceptInviteTests.cs tests/Api.Tests/OrganizationSettingsEndpointsTests.cs
git commit -m "feat(identity): add operational defaults (service type, origin branch) to organization settings"
```

- [ ] **Step 15: Add the frontend hook, form, and proxy route**

```ts
// src/web/src/features/admin/settings/hooks/useUpdateOperationalDefaults.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { OperationalDefaults } from "@/features/admin/settings/types";

export function useUpdateOperationalDefaults() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: OperationalDefaults) =>
      clientFetch<OperationalDefaults>("/api/settings/organization/defaults", {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["organization-settings"] }),
  });
}
```

```ts
// src/web/src/app/api/settings/organization/defaults/route.ts
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function PATCH(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/settings/organization/defaults", {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```tsx
// src/web/src/features/admin/settings/components/OperationalDefaultsForm.tsx
"use client";

import { useEffect, useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { Skeleton } from "@/components/ui/skeleton";
import { useOrganizationSettings } from "@/features/admin/settings/hooks/useOrganizationSettings";
import { useUpdateOperationalDefaults } from "@/features/admin/settings/hooks/useUpdateOperationalDefaults";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";

interface DefaultsFormValues {
  defaultServiceTypeId: string;
  defaultOriginBranchId: string;
}

const NONE_VALUE = "__none__";

export function OperationalDefaultsForm() {
  const { data, isLoading } = useOrganizationSettings();
  const { data: branches } = useBranches();
  const { data: serviceTypes } = useServiceTypes();
  const updateDefaults = useUpdateOperationalDefaults();
  const [serverError, setServerError] = useState<string | null>(null);

  const { control, handleSubmit, reset, formState: { isSubmitting } } = useForm<DefaultsFormValues>({
    defaultValues: { defaultServiceTypeId: NONE_VALUE, defaultOriginBranchId: NONE_VALUE },
  });

  useEffect(() => {
    if (data) {
      reset({
        defaultServiceTypeId: data.defaults.defaultServiceTypeId ?? NONE_VALUE,
        defaultOriginBranchId: data.defaults.defaultOriginBranchId ?? NONE_VALUE,
      });
    }
  }, [data, reset]);

  async function onSubmit(values: DefaultsFormValues) {
    setServerError(null);
    try {
      await updateDefaults.mutateAsync({
        defaultServiceTypeId: values.defaultServiceTypeId === NONE_VALUE ? null : values.defaultServiceTypeId,
        defaultOriginBranchId: values.defaultOriginBranchId === NONE_VALUE ? null : values.defaultOriginBranchId,
      });
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  if (isLoading) {
    return (
      <div className="space-y-3" role="status" aria-live="polite">
        <span className="sr-only">Loading</span>
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-10 w-full" />
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="max-w-xl space-y-4" noValidate>
      <div className="space-y-2">
        <Label htmlFor="defaultServiceTypeId">Default Service Type</Label>
        <Controller
          name="defaultServiceTypeId"
          control={control}
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger id="defaultServiceTypeId" aria-label="Default service type"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value={NONE_VALUE}>None</SelectItem>
                {serviceTypes?.map((s) => (
                  <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      </div>
      <div className="space-y-2">
        <Label htmlFor="defaultOriginBranchId">Default Origin Branch</Label>
        <Controller
          name="defaultOriginBranchId"
          control={control}
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger id="defaultOriginBranchId" aria-label="Default origin branch"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value={NONE_VALUE}>None</SelectItem>
                {branches?.map((b) => (
                  <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      </div>
      {serverError && <FieldError id="defaults-server-error">{serverError}</FieldError>}
      <Button type="submit" disabled={isSubmitting}>
        {isSubmitting ? <Spinner className="h-4 w-4" /> : "Save Defaults"}
      </Button>
    </form>
  );
}
```

- [ ] **Step 16: Wire the Defaults panel into `SettingsTab`**

In `src/web/src/features/admin/settings/components/SettingsTab.tsx`:
1. Import `OperationalDefaultsForm`.
2. Add `{ key: "defaults", label: "Defaults", permission: "org.settings.manage" }` to `SUB_TABS`.
3. Extend `permitted`: add `defaults: canManageSettings`.
4. Add `<TabsContent value="defaults"><OperationalDefaultsForm /></TabsContent>`.

- [ ] **Step 17: Manually verify in the browser, then run the full frontend suite**

Open Settings → Defaults, pick a service type and branch, save, reload, confirm persistence.

Run: `cd src/web && npx vitest run`
Expected: PASS

- [ ] **Step 18: Commit frontend**

```bash
git add src/web/src/features/admin/settings src/web/src/app/api/settings
git commit -m "feat(web): add operational defaults form to Settings"
```

---

## Task 6: Shipment creation form pre-fills from organization defaults

**Files:**
- Modify: `src/web/src/features/shipments/components/ShipmentCreateSheet.tsx`
- Create: `src/web/src/features/shipments/components/ShipmentCreateSheet.test.tsx`

**Interfaces:**
- Consumes: `useOrganizationSettings()` from Task 2 (`OrganizationSettings.defaults.defaultOriginBranchId` / `defaultServiceTypeId`, both `string | null`).
- No new exports — this task only changes `ShipmentCreateSheet`'s internal behavior.

- [ ] **Step 1: Write the failing component test**

```tsx
// src/web/src/features/shipments/components/ShipmentCreateSheet.test.tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("@/features/shipments/hooks/useMerchants", () => ({ useMerchants: () => ({ data: [] }) }));
vi.mock("@/features/shipments/hooks/useServiceTypes", () => ({
  useServiceTypes: () => ({ data: [{ id: "st-standard", code: "Standard", name: "Standard" }] }),
}));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "branch-ktm", code: "KTM-HUB", name: "Kathmandu Hub", isActive: true }] }),
}));
vi.mock("@/features/shipments/hooks/useShipmentQuote", () => ({ useShipmentQuote: () => ({ data: null, isFetching: false }) }));
vi.mock("@/features/shipments/hooks/useCreateShipment", () => ({ useCreateShipment: () => ({ mutateAsync: vi.fn(), isPending: false }) }));
vi.mock("@/features/admin/settings/hooks/useOrganizationSettings", () => ({
  useOrganizationSettings: () => ({
    data: {
      profile: { supportEmail: null, supportPhone: null, addressLine: null, logoUrl: null, timeZoneId: "Asia/Kathmandu", businessHours: null },
      security: { refreshTokenLifetimeDays: 7, minPasswordLength: 8 },
      defaults: { defaultServiceTypeId: "st-standard", defaultOriginBranchId: "branch-ktm" },
    },
  }),
}));

let panelMode: "create" | "closed" = "create";
vi.mock("@/features/shipments/store/shipmentPanelStore", () => ({
  useShipmentPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: panelMode }, close: vi.fn(), openDetail: vi.fn() }),
}));

import { ShipmentCreateSheet } from "./ShipmentCreateSheet";

describe("ShipmentCreateSheet", () => {
  it("pre-fills origin branch and service type from organization defaults when the sheet opens", async () => {
    panelMode = "create";
    render(<ShipmentCreateSheet />);

    expect(await screen.findByText("Kathmandu Hub")).toBeInTheDocument();
    expect(await screen.findByText("Standard")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npx vitest run src/features/shipments/components/ShipmentCreateSheet.test.tsx`
Expected: FAIL — the Select triggers show their placeholder text ("Select type"-equivalent), not "Kathmandu Hub"/"Standard", since nothing pre-fills them yet.

- [ ] **Step 3: Implement the pre-fill effect**

In `src/web/src/features/shipments/components/ShipmentCreateSheet.tsx`:

1. Add `useEffect` to the existing `import { useState } from "react";` line, making it `import { useEffect, useState } from "react";`.
2. Add the import: `import { useOrganizationSettings } from "@/features/admin/settings/hooks/useOrganizationSettings";`
3. Immediately after the existing `const { data: branches } = useBranches();` line, add:

```tsx
const { data: organizationSettings } = useOrganizationSettings();
```

4. Immediately after the `const [originBranchId, destinationBranchId, ...] = watch([...]);` line, add:

```tsx
useEffect(() => {
  if (!isOpen || !organizationSettings) {
    return;
  }
  const { defaultOriginBranchId, defaultServiceTypeId } = organizationSettings.defaults;
  if (!originBranchId && defaultOriginBranchId) {
    setValue("originBranchId", defaultOriginBranchId);
  }
  if (!serviceTypeId && defaultServiceTypeId) {
    setValue("serviceTypeId", defaultServiceTypeId);
  }
  // originBranchId/serviceTypeId are intentionally excluded from deps — including them would
  // re-run this effect (and re-apply the default) every time the user edits the very fields it
  // sets, fighting a deliberate override. It should re-run only when the sheet opens fresh or the
  // org's defaults change.
  // eslint-disable-next-line react-hooks/exhaustive-deps
}, [isOpen, organizationSettings, setValue]);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/web && npx vitest run src/features/shipments/components/ShipmentCreateSheet.test.tsx`
Expected: PASS

- [ ] **Step 5: Manually verify in the browser**

With the dev server running and an org's Operational Defaults already configured (Task 5), open the shipment creation Sheet and confirm the Service Type and Origin Branch fields are pre-selected but still changeable. Open it again after manually changing one of the fields in a previous session, confirming a *fresh* open re-applies the default (expected — this is a pre-fill, not a sticky user preference).

- [ ] **Step 6: Run the full frontend test suite for regressions**

Run: `cd src/web && npx vitest run`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/shipments/components/ShipmentCreateSheet.tsx src/web/src/features/shipments/components/ShipmentCreateSheet.test.tsx
git commit -m "feat(web): pre-fill shipment creation form from organization defaults"
```

---

## Task 7: Notification Templates — `NotificationChannel` JSON fix + backend + frontend

**Files:**
- Modify: `src/Modules/Notifications/Domain/NotificationChannel.cs`
- Create: `src/Modules/Notifications/Application/NotificationTemplateDtos.cs`
- Create: `src/Modules/Notifications/Application/NotificationTemplateService.cs`
- Create: `src/Modules/Notifications/Presentation/NotificationTemplatesEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/Notifications.Tests/NotificationTemplateServiceTests.cs`
- Create: `tests/Api.Tests/NotificationTemplatesEndpointsTests.cs`
- Create: `src/web/src/features/admin/settings/notification-templates/types.ts`
- Create: `src/web/src/features/admin/settings/notification-templates/schema.ts`
- Create: `src/web/src/features/admin/settings/notification-templates/hooks/useNotificationTemplates.ts`
- Create: `src/web/src/features/admin/settings/notification-templates/hooks/useUpsertNotificationTemplate.ts`
- Create: `src/web/src/features/admin/settings/notification-templates/store/notificationTemplatePanelStore.ts`
- Create: `src/web/src/features/admin/settings/notification-templates/components/NotificationTemplatesPanel.tsx`
- Create: `src/web/src/features/admin/settings/notification-templates/components/NotificationTemplateEditSheet.tsx`
- Create: `src/web/src/app/api/settings/notification-templates/route.ts`
- Modify: `src/web/src/app/(staff)/admin/page.tsx`
- Modify: `src/web/src/features/admin/settings/components/SettingsTab.tsx`

**Interfaces:**
- Produces: `Courier.Notifications.Application.NotificationTemplateDto(Guid Id, string EventType, NotificationChannel Channel, string Body, bool IsActive)`.
- Produces on `NotificationTemplateService`: `ListAsync(CancellationToken ct = default) -> Task<IReadOnlyList<NotificationTemplateDto>>`, `UpsertAsync(Guid actingUserId, string eventType, NotificationChannel channel, string body, bool isActive, CancellationToken ct = default) -> Task<NotificationTemplateDto>` — throws `NotificationValidationException` (already exists) for an `eventType` not in `NotificationDefaultTemplates.Email.Keys`.
- Produces endpoints: `GET /api/v1/settings/notification-templates`, `PUT /api/v1/settings/notification-templates` — both `.RequirePermission("notification_template.manage")`.

- [ ] **Step 1: Write the failing `NotificationChannel` serialization test and service tests**

```csharp
// tests/Notifications.Tests/NotificationTemplateServiceTests.cs
using Courier.Audit.Application;
using Courier.Notifications.Application;
using Courier.Notifications.Domain;
using Courier.Notifications.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Notifications.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class FakeAuditLogger : IAuditLogger
{
    public List<AuditEntry> Entries { get; } = new();
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; }
}

public class NotificationTemplateServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new NotificationTemplateConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task UpsertAsync_UnknownEventType_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var service = new NotificationTemplateService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new FakeAuditLogger());

        await Assert.ThrowsAsync<NotificationValidationException>(() =>
            service.UpsertAsync(Guid.NewGuid(), "not.a.real.event", NotificationChannel.Email, "Body", true));
    }

    [Fact]
    public async Task UpsertAsync_NoExistingRow_InsertsOneRowAndWritesAuditEntry()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var auditLogger = new FakeAuditLogger();
        var service = new NotificationTemplateService(dbContext, new FixedTenantProvider(organizationId), auditLogger);

        var result = await service.UpsertAsync(Guid.NewGuid(), "shipment.created", NotificationChannel.Email, "Custom body {Awb}", true);

        Assert.Equal("Custom body {Awb}", result.Body);
        Assert.Single(dbContext.Set<NotificationTemplate>().Where(t => t.OrganizationId == organizationId));
        Assert.Contains(auditLogger.Entries, e => e.Action == "notification_template.upserted");
    }

    [Fact]
    public async Task UpsertAsync_ExistingRowForSameEventAndChannel_UpdatesInPlaceRatherThanInserting()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = new NotificationTemplateService(dbContext, new FixedTenantProvider(organizationId), new FakeAuditLogger());
        await service.UpsertAsync(Guid.NewGuid(), "shipment.created", NotificationChannel.Email, "First body", true);

        var result = await service.UpsertAsync(Guid.NewGuid(), "shipment.created", NotificationChannel.Email, "Second body", false);

        Assert.Equal("Second body", result.Body);
        Assert.False(result.IsActive);
        Assert.Single(dbContext.Set<NotificationTemplate>().Where(t => t.OrganizationId == organizationId));
    }

    [Fact]
    public async Task ListAsync_ScopesToCurrentOrganization()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        dbContext.Set<NotificationTemplate>().Add(new NotificationTemplate { Id = Guid.NewGuid(), OrganizationId = otherOrganizationId, EventType = "shipment.created", Channel = NotificationChannel.Email, Body = "Other org's body", IsActive = true });
        await dbContext.SaveChangesAsync();
        var service = new NotificationTemplateService(dbContext, new FixedTenantProvider(organizationId), new FakeAuditLogger());
        await service.UpsertAsync(Guid.NewGuid(), "shipment.delivered", NotificationChannel.Email, "This org's body", true);

        var result = await service.ListAsync();

        var single = Assert.Single(result);
        Assert.Equal("This org's body", single.Body);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Notifications.Tests/Notifications.Tests.csproj --filter NotificationTemplateServiceTests`
Expected: FAIL to compile — `NotificationTemplateService`/`NotificationTemplateDto` don't exist.

- [ ] **Step 3: Fix the `NotificationChannel` JSON serialization gap**

```csharp
// src/Modules/Notifications/Domain/NotificationChannel.cs
using System.Text.Json.Serialization;

namespace Courier.Notifications.Domain;

// Exposed over JSON for the first time by the Notification Templates API (Task 7) — without this
// attribute it would silently serialize as a raw integer, the same bug this codebase has already
// hit on BranchType, ShipmentStatus, ManifestPurpose/ManifestStatus, three Ndr enums, three Cod
// enums, and SubscriptionStatus.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationChannel
{
    Sms,
    Email,
    WhatsApp,
    Push
}
```

- [ ] **Step 4: Add the DTOs and service**

```csharp
// src/Modules/Notifications/Application/NotificationTemplateDtos.cs
using Courier.Notifications.Domain;

namespace Courier.Notifications.Application;

public sealed record NotificationTemplateDto(Guid Id, string EventType, NotificationChannel Channel, string Body, bool IsActive);
```

```csharp
// src/Modules/Notifications/Application/NotificationTemplateService.cs
using Courier.Audit.Application;
using Courier.Notifications.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Notifications.Application;

public class NotificationTemplateService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;

    public NotificationTemplateService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
    }

    public async Task<IReadOnlyList<NotificationTemplateDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new NotificationValidationException("No active organization context.");
        return await _dbContext.Set<NotificationTemplate>()
            .Where(t => t.OrganizationId == organizationId)
            .Select(t => new NotificationTemplateDto(t.Id, t.EventType, t.Channel, t.Body, t.IsActive))
            .ToListAsync(cancellationToken);
    }

    // Looks up any existing row for (org, eventType, channel) regardless of IsActive and updates
    // it in place rather than always inserting — NotificationTemplateConfiguration's own index
    // comment notes this combination is deliberately not unique, and NotificationDispatchService
    // picks an arbitrary row via FirstOrDefaultAsync when more than one exists, so letting repeat
    // edits accumulate duplicate rows would make dispatch's chosen row nondeterministic.
    public async Task<NotificationTemplateDto> UpsertAsync(Guid actingUserId, string eventType, NotificationChannel channel, string body, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!NotificationDefaultTemplates.Email.ContainsKey(eventType))
        {
            throw new NotificationValidationException($"Unknown event type '{eventType}'.");
        }
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new NotificationValidationException("No active organization context.");

        var existing = await _dbContext.Set<NotificationTemplate>()
            .FirstOrDefaultAsync(t => t.OrganizationId == organizationId && t.EventType == eventType && t.Channel == channel, cancellationToken);

        NotificationTemplate template;
        if (existing is null)
        {
            template = new NotificationTemplate { Id = Guid.NewGuid(), OrganizationId = organizationId, EventType = eventType, Channel = channel, Body = body, IsActive = isActive };
            _dbContext.Set<NotificationTemplate>().Add(template);
        }
        else
        {
            existing.Body = body;
            existing.IsActive = isActive;
            template = existing;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "notification_template.upserted", "NotificationTemplate", template.Id.ToString(),
            After: System.Text.Json.JsonSerializer.Serialize(new { eventType, channel, body, isActive })), cancellationToken);

        return new NotificationTemplateDto(template.Id, template.EventType, template.Channel, template.Body, template.IsActive);
    }
}
```

- [ ] **Step 5: Register the service in `Program.cs`**

Immediately after the existing `builder.Services.AddScoped<NotificationDispatchService>();` line, add:

```csharp
builder.Services.AddScoped<NotificationTemplateService>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Notifications.Tests/Notifications.Tests.csproj --filter NotificationTemplateServiceTests`
Expected: PASS

- [ ] **Step 7: Commit backend service**

```bash
git add src/Modules/Notifications/Domain/NotificationChannel.cs src/Modules/Notifications/Application/NotificationTemplateDtos.cs src/Modules/Notifications/Application/NotificationTemplateService.cs src/Api/Program.cs tests/Notifications.Tests/NotificationTemplateServiceTests.cs
git commit -m "feat(notifications): add notification template list/upsert service"
```

- [ ] **Step 8: Write the failing API integration test**

```csharp
// tests/Api.Tests/NotificationTemplatesEndpointsTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Identity.Application;
using Courier.Notifications.Application;
using Courier.Notifications.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class NotificationTemplatesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public NotificationTemplatesEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest($"Templates Org {suffix}", $"templates-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task GetTemplates_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/settings/notification-templates");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutTemplate_UnknownEventType_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.PutAsJsonAsync("/api/v1/settings/notification-templates", new { eventType = "not.a.real.event", channel = NotificationChannel.Email, body = "Body", isActive = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutTemplate_ValidEventType_UpsertsAndAppearsInList()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var putResponse = await client.PutAsJsonAsync("/api/v1/settings/notification-templates", new { eventType = "shipment.delivered", channel = NotificationChannel.Email, body = "Custom delivered body", isActive = true });
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/v1/settings/notification-templates");
        var templates = await listResponse.Content.ReadFromJsonAsync<List<NotificationTemplateDto>>();
        Assert.Contains(templates!, t => t.EventType == "shipment.delivered" && t.Body == "Custom delivered body");
    }
}
```

- [ ] **Step 9: Run test to verify it fails**

Run: `dotnet build Courier.slnx`
Expected: FAIL to compile — the routes don't exist yet.

- [ ] **Step 10: Implement the endpoints**

```csharp
// src/Modules/Notifications/Presentation/NotificationTemplatesEndpoints.cs
using Courier.Notifications.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Notifications.Presentation;

public record UpsertNotificationTemplateRequest(string EventType, Courier.Notifications.Domain.NotificationChannel Channel, string Body, bool IsActive);

public static class NotificationTemplatesEndpoints
{
    public static void MapNotificationTemplatesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/settings/notification-templates").WithTags("Notification Templates");

        group.MapGet("/", async (NotificationTemplateService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken))).RequirePermission("notification_template.manage");

        group.MapPut("/", async (UpsertNotificationTemplateRequest request, NotificationTemplateService service, HttpContext httpContext) =>
        {
            var actingUserId = httpContext.User.GetUserId()!.Value;
            try
            {
                return Results.Ok(await service.UpsertAsync(actingUserId, request.EventType, request.Channel, request.Body, request.IsActive, httpContext.RequestAborted));
            }
            catch (NotificationValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "NOTIFICATION_TEMPLATE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("notification_template.manage");
    }
}
```

In `src/Api/Program.cs`, add `app.MapNotificationTemplatesEndpoints();` immediately after the existing `app.MapWebhooksEndpoints();` line.

- [ ] **Step 11: Run tests to verify they pass**

Run: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj --filter NotificationTemplatesEndpointsTests`
Expected: PASS

- [ ] **Step 12: Commit backend endpoints**

```bash
git add src/Modules/Notifications/Presentation/NotificationTemplatesEndpoints.cs src/Api/Program.cs tests/Api.Tests/NotificationTemplatesEndpointsTests.cs
git commit -m "feat(notifications): add notification templates endpoints"
```

- [ ] **Step 13: Add frontend types, schema, hooks, store, and proxy route**

```ts
// src/web/src/features/admin/settings/notification-templates/types.ts
export type NotificationChannel = "Sms" | "Email" | "WhatsApp" | "Push";

export interface NotificationTemplate {
  id: string;
  eventType: string;
  channel: NotificationChannel;
  body: string;
  isActive: boolean;
}

export interface UpsertNotificationTemplateInput {
  eventType: string;
  channel: NotificationChannel;
  body: string;
  isActive: boolean;
}
```

```ts
// src/web/src/features/admin/settings/notification-templates/schema.ts
import { z } from "zod";

export const notificationTemplateSchema = z.object({
  body: z.string().min(1, "Body is required."),
  isActive: z.boolean(),
});

export type NotificationTemplateFormValues = z.infer<typeof notificationTemplateSchema>;
```

```ts
// src/web/src/features/admin/settings/notification-templates/hooks/useNotificationTemplates.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { NotificationTemplate } from "@/features/admin/settings/notification-templates/types";

export function useNotificationTemplates() {
  return useQuery({
    queryKey: ["notification-templates"],
    queryFn: () => clientFetch<NotificationTemplate[]>("/api/settings/notification-templates"),
  });
}
```

```ts
// src/web/src/features/admin/settings/notification-templates/hooks/useUpsertNotificationTemplate.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { NotificationTemplate, UpsertNotificationTemplateInput } from "@/features/admin/settings/notification-templates/types";

export function useUpsertNotificationTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: UpsertNotificationTemplateInput) =>
      clientFetch<NotificationTemplate>("/api/settings/notification-templates", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["notification-templates"] }),
  });
}
```

```ts
// src/web/src/features/admin/settings/notification-templates/store/notificationTemplatePanelStore.ts
import { create } from "zustand";
import type { NotificationChannel } from "@/features/admin/settings/notification-templates/types";

type Panel = { mode: "closed" } | { mode: "edit"; eventType: string; channel: NotificationChannel };

interface NotificationTemplatePanelStore {
  panel: Panel;
  openEdit: (eventType: string, channel: NotificationChannel) => void;
  close: () => void;
}

// Global, same convention as ndrPanelStore/branchPanelStore — the edit Sheet opens over any row
// in the DataTable, list and detail live in separate components.
export const useNotificationTemplatePanelStore = create<NotificationTemplatePanelStore>((set) => ({
  panel: { mode: "closed" },
  openEdit: (eventType, channel) => set({ panel: { mode: "edit", eventType, channel } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

```ts
// src/web/src/app/api/settings/notification-templates/route.ts
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/settings/notification-templates");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function PUT(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/settings/notification-templates", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 14: Build the DataTable panel and edit Sheet**

The full event/channel cross-product is fixed: `NotificationDefaultTemplates.Email`'s five event-type keys (`shipment.created`, `shipment.delivered`, `shipment.ndr_created`, `cod.collected`, `user.invited`) × the four `NotificationChannel` values. A row shows the org's override if one exists in the fetched list, else "(using default)".

```tsx
// src/web/src/features/admin/settings/notification-templates/components/NotificationTemplatesPanel.tsx
"use client";

import { Badge } from "@/components/ui/badge";
import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { useNotificationTemplates } from "@/features/admin/settings/notification-templates/hooks/useNotificationTemplates";
import { useNotificationTemplatePanelStore } from "@/features/admin/settings/notification-templates/store/notificationTemplatePanelStore";
import { NotificationTemplateEditSheet } from "@/features/admin/settings/notification-templates/components/NotificationTemplateEditSheet";
import type { NotificationChannel, NotificationTemplate } from "@/features/admin/settings/notification-templates/types";

const EVENT_TYPES = ["shipment.created", "shipment.delivered", "shipment.ndr_created", "cod.collected", "user.invited"];
const CHANNELS: NotificationChannel[] = ["Sms", "Email", "WhatsApp", "Push"];

interface Row {
  eventType: string;
  channel: NotificationChannel;
  override: NotificationTemplate | null;
}

export function NotificationTemplatesPanel() {
  const { data, isLoading } = useNotificationTemplates();
  const openEdit = useNotificationTemplatePanelStore((s) => s.openEdit);

  const rows: Row[] = EVENT_TYPES.flatMap((eventType) =>
    CHANNELS.map((channel) => ({
      eventType,
      channel,
      override: data?.find((t) => t.eventType === eventType && t.channel === channel) ?? null,
    }))
  );

  const columns: ColumnDef<Row>[] = [
    { key: "eventType", header: "Event", render: (r) => r.eventType },
    { key: "channel", header: "Channel", render: (r) => r.channel },
    {
      key: "status",
      header: "Status",
      render: (r) =>
        r.override ? (
          <Badge variant={r.override.isActive ? "default" : "secondary"}>{r.override.isActive ? "Custom" : "Custom (inactive)"}</Badge>
        ) : (
          <Badge variant="outline">Using default</Badge>
        ),
    },
    { key: "body", header: "Body", render: (r) => (r.override ? r.override.body : "—") },
    {
      key: "actions",
      header: "",
      render: (r) => (
        <button type="button" className="text-sm font-medium text-foreground hover:underline" onClick={() => openEdit(r.eventType, r.channel)}>
          Edit
        </button>
      ),
    },
  ];

  const pagedRows: PagedResult<Row> = { items: rows, page: 1, pageSize: rows.length, totalCount: rows.length, totalPages: 1 };

  return (
    <div className="space-y-4">
      <DataTable
        columns={columns}
        data={pagedRows}
        isLoading={isLoading}
        emptyTitle="No notification event types configured"
        onPageChange={() => {}}
        rowKey={(r) => `${r.eventType}-${r.channel}`}
      />
      <NotificationTemplateEditSheet />
    </div>
  );
}
```

(`onPageChange` is a no-op — confirmed against `DataTableProps<T>` in `DataTable.tsx` and the NDRs page's real call site: `rowKey` and `emptyTitle` are required props, `emptyDescription` is optional and omitted here since the 20-row fixed cross-product is never actually empty. `onPageChange` exists only to page a real multi-page result — this table is always exactly one page (`totalPages: 1`), so the callback is never invoked.)

```tsx
// src/web/src/features/admin/settings/notification-templates/components/NotificationTemplateEditSheet.tsx
"use client";

import { useEffect, useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useNotificationTemplates } from "@/features/admin/settings/notification-templates/hooks/useNotificationTemplates";
import { useUpsertNotificationTemplate } from "@/features/admin/settings/notification-templates/hooks/useUpsertNotificationTemplate";
import { useNotificationTemplatePanelStore } from "@/features/admin/settings/notification-templates/store/notificationTemplatePanelStore";
import { notificationTemplateSchema, type NotificationTemplateFormValues } from "@/features/admin/settings/notification-templates/schema";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";

export function NotificationTemplateEditSheet() {
  const panel = useNotificationTemplatePanelStore((s) => s.panel);
  const close = useNotificationTemplatePanelStore((s) => s.close);
  const isOpen = panel.mode === "edit";
  const { data: templates } = useNotificationTemplates();
  const upsert = useUpsertNotificationTemplate();
  const [serverError, setServerError] = useState<string | null>(null);

  const { register, handleSubmit, control, reset, formState: { errors, isSubmitting } } = useForm<NotificationTemplateFormValues>({
    resolver: zodResolver(notificationTemplateSchema),
    defaultValues: { body: "", isActive: true },
  });

  useEffect(() => {
    if (panel.mode === "edit") {
      const existing = templates?.find((t) => t.eventType === panel.eventType && t.channel === panel.channel);
      reset({ body: existing?.body ?? "", isActive: existing?.isActive ?? true });
    }
  }, [panel, templates, reset]);

  async function onSubmit(values: NotificationTemplateFormValues) {
    if (panel.mode !== "edit") return;
    setServerError(null);
    try {
      await upsert.mutateAsync({ eventType: panel.eventType, channel: panel.channel, body: values.body, isActive: values.isActive });
      close();
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{panel.mode === "edit" ? `${panel.eventType} — ${panel.channel}` : "Edit Template"}</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="body">Body</Label>
            <Textarea id="body" rows={5} aria-invalid={!!errors.body} {...register("body")} />
            {errors.body && <FieldError id="body-error">{errors.body.message}</FieldError>}
          </div>
          <Controller
            name="isActive"
            control={control}
            render={({ field }) => (
              <div className="flex items-center gap-2">
                <Checkbox id="isActive" checked={field.value} onCheckedChange={field.onChange} />
                <Label htmlFor="isActive" className="font-normal">Active</Label>
              </div>
            )}
          />
          {serverError && <FieldError id="template-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Save Template"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 15: Wire the Notification Templates panel into `SettingsTab` and the outer Admin page**

In `src/web/src/features/admin/settings/components/SettingsTab.tsx`:
1. Import `NotificationTemplatesPanel` and add `const canManageTemplates = usePermission("notification_template.manage");`.
2. Add `{ key: "notification-templates", label: "Notification Templates", permission: "notification_template.manage" }` to `SUB_TABS`.
3. Extend `permitted`: add `"notification-templates": canManageTemplates`.
4. Add `<TabsContent value="notification-templates"><NotificationTemplatesPanel /></TabsContent>`.

In `src/web/src/app/(staff)/admin/page.tsx` (per the approved spec's "tab OR, section AND" split, deferred from Task 2):
1. Extend the `permitted` object's `settings` entry from `canManageSettings` to `canManageSettings || canManageTemplates` (the `canManageTemplates` variable was already added in Task 2's Step 17 — if it wasn't, add `const canManageTemplates = usePermission("notification_template.manage");` now alongside the other `usePermission` calls).

- [ ] **Step 16: Manually verify in the browser**

Open Settings → Notification Templates, confirm all 20 event/channel rows render, edit `shipment.created`/`Email`'s body, save, confirm the row now shows "Custom" and the new body text, reload and confirm it persisted.

- [ ] **Step 17: Run the full frontend test suite for regressions**

Run: `cd src/web && npx vitest run`
Expected: PASS

- [ ] **Step 18: Commit frontend**

```bash
git add src/web/src/features/admin/settings src/web/src/app/api/settings/notification-templates src/web/src/app/(staff)/admin/page.tsx
git commit -m "feat(web): add notification templates management to Settings"
```
