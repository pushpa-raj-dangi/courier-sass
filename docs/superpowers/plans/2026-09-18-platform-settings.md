# Platform Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a SuperAdmin view and live-edit platform-wide configuration (eSewa/Khalti keys, SMTP, JWT signing key, rate limits, frontend base URL, billing grace period) from the Platform UI, with changes taking effect across every running process within seconds, no restart, no redeploy.

**Architecture:** A new `PlatformSettings`/`PlatformSettingDefinitions` table pair backs two independent live-update paths: non-secret values flow through a custom `IConfigurationProvider` (boot-safe raw SQL, since it loads before the DI container exists) that `IOptionsMonitor<T>` consumers already react to; secret values (eSewa/Khalti/JWT keys) decrypt lazily through `IPostConfigureOptions<T>`, fed by a singleton cache a Redis-subscribed `BackgroundService` refreshes. A `PlatformSettingsService` (application layer) validates, encrypts, audits, and publishes the Redis invalidation signal on every write.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core (Npgsql), raw ADO.NET (Npgsql) for boot-time config loading, ASP.NET Core Data Protection API, StackExchange.Redis pub/sub, Next.js/TanStack Query/shadcn on the frontend.

**Spec:** `docs/superpowers/specs/2026-09-18-platform-settings-design.md`

## Global Constraints

- Every new endpoint uses `.RequireSuperAdmin()` (`src/SharedKernel/RequireSuperAdminFilter.cs`) — this codebase has no granular permission grid for the Platform module.
- Route prefix `/api/v1/platform/...`, matching every sibling Platform endpoint group.
- Secret values are never returned in plaintext by any GET endpoint — masked display only, write-only replace.
- Every write is audited via `IAuditLogger.LogAsync(AuditEntry, ...)` with `OrganizationId: null`, `IsCrossTenantAction: true`, and `Before`/`After` = `"[REDACTED]"` for any `IsSecret` key.
- `Jwt.SigningKey` requires the caller to pass `confirmationPhrase == "REVOKE ALL SESSIONS"`, enforced server-side.
- All backend code targets `net10.0` with `Nullable` enabled, matching every existing module.
- Frontend: routed page (not a Sheet) under `/platform/settings`, backend calls only through a Next.js proxy route (`platformApiFetch`), never directly from the browser.

---

## Task 1: Add package references the Platform module needs

**Files:**
- Modify: `src/Modules/Platform/Platform.csproj`

**Interfaces:**
- Produces: `NpgsqlConnection`/`NpgsqlCommand`/`Npgsql.PostgresException` and `StackExchange.Redis.IConnectionMultiplexer`/`RedisChannel` become available to every later task in this module.

- [ ] **Step 1: Add the two package references**

In `src/Modules/Platform/Platform.csproj`, inside the existing `<ItemGroup>` that lists `Microsoft.EntityFrameworkCore`, add two lines so the `<ItemGroup>` reads:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.11" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.11" />
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.22.0" />
    <PackageReference Include="Hangfire.Core" Version="1.8.24" />
    <PackageReference Include="Npgsql" Version="10.0.3" />
    <PackageReference Include="StackExchange.Redis" Version="3.1.31" />
  </ItemGroup>
```

- [ ] **Step 2: Verify the project restores and builds**

Run: `dotnet build src/Modules/Platform/Platform.csproj`
Expected: Build succeeds (no source changes yet, just new package references).

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Platform/Platform.csproj
git commit -m "chore(platform): add Npgsql and StackExchange.Redis package references"
```

---

## Task 2: Domain entities, EF configuration, catalog, and migration

**Files:**
- Create: `src/Modules/Platform/Domain/PlatformSettingDefinition.cs`
- Create: `src/Modules/Platform/Domain/PlatformSetting.cs`
- Create: `src/Modules/Platform/Infrastructure/PlatformSettingCatalog.cs`
- Create: `src/Modules/Platform/Infrastructure/PlatformSettingDefinitionConfiguration.cs`
- Create: `src/Modules/Platform/Infrastructure/PlatformSettingConfiguration.cs`
- Modify: `src/Persistence/AppDbContext.cs`
- Create: EF Core migration under `src/Persistence/Migrations/`

**Interfaces:**
- Produces: `Courier.Platform.Domain.PlatformSettingDefinition { Key, Category, ConfigPath, ValueType, IsSecret, RequiresConfirmation, Description }`, `Courier.Platform.Domain.PlatformSetting { Key, Value, IsOverridden, UpdatedAt, UpdatedByUserId }`, `Courier.Platform.Infrastructure.PlatformSettingCatalog.Definitions` (`IReadOnlyList<PlatformSettingDefinition>`) and its `Category*`/`ValueType*` string constants — every later task reads these.

- [ ] **Step 1: Create the domain entities**

`src/Modules/Platform/Domain/PlatformSettingDefinition.cs`:

```csharp
namespace Courier.Platform.Domain;

// Static catalog of platform-wide settings a SuperAdmin can override at runtime — rows are
// seeded via migration (PlatformSettingCatalog.Definitions), never created through the API.
public class PlatformSettingDefinition
{
    public string Key { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string ConfigPath { get; set; } = default!;
    public string ValueType { get; set; } = default!;
    public bool IsSecret { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string? Description { get; set; }
}
```

`src/Modules/Platform/Domain/PlatformSetting.cs`:

```csharp
namespace Courier.Platform.Domain;

// One row per PlatformSettingDefinition.Key, created lazily on first override. IsOverridden ==
// false means "not set, the app falls back to appsettings/env" — see
// PlatformSettingsConfigurationProvider for how this fallback is actually resolved.
public class PlatformSetting
{
    public string Key { get; set; } = default!;
    public string? Value { get; set; }
    public bool IsOverridden { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedByUserId { get; set; }
}
```

- [ ] **Step 2: Create the settings catalog**

`src/Modules/Platform/Infrastructure/PlatformSettingCatalog.cs`:

```csharp
using Courier.Platform.Domain;

namespace Courier.Platform.Infrastructure;

// The fixed set of platform-wide settings this feature manages. Seeded into
// PlatformSettingDefinitions via migration HasData (PlatformSettingDefinitionConfiguration) —
// not user-creatable through the API; a SuperAdmin edits VALUES, not this catalog.
public static class PlatformSettingCatalog
{
    public const string CategoryPaymentGateway = "PaymentGateway";
    public const string CategoryNotifications = "Notifications";
    public const string CategorySecurity = "Security";
    public const string CategoryBranding = "Branding";

    public const string ValueTypeString = "String";
    public const string ValueTypeInt = "Int";
    public const string ValueTypeBool = "Bool";

    public static readonly IReadOnlyList<PlatformSettingDefinition> Definitions = new List<PlatformSettingDefinition>
    {
        new() { Key = "Esewa.ProductCode", Category = CategoryPaymentGateway, ConfigPath = "Esewa:ProductCode", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "eSewa merchant product code." },
        new() { Key = "Esewa.SecretKey", Category = CategoryPaymentGateway, ConfigPath = "Esewa:SecretKey", ValueType = ValueTypeString, IsSecret = true, RequiresConfirmation = false, Description = "eSewa HMAC signing secret." },
        new() { Key = "Esewa.FormUrl", Category = CategoryPaymentGateway, ConfigPath = "Esewa:FormUrl", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "eSewa checkout form endpoint." },
        new() { Key = "Esewa.StatusCheckUrl", Category = CategoryPaymentGateway, ConfigPath = "Esewa:StatusCheckUrl", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "eSewa server-to-server status check endpoint." },
        new() { Key = "Khalti.SecretKey", Category = CategoryPaymentGateway, ConfigPath = "Khalti:SecretKey", ValueType = ValueTypeString, IsSecret = true, RequiresConfirmation = false, Description = "Khalti live secret key." },
        new() { Key = "Khalti.InitiateUrl", Category = CategoryPaymentGateway, ConfigPath = "Khalti:InitiateUrl", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "Khalti payment initiate endpoint." },
        new() { Key = "Khalti.LookupUrl", Category = CategoryPaymentGateway, ConfigPath = "Khalti:LookupUrl", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "Khalti payment lookup endpoint." },
        new() { Key = "Khalti.WebsiteUrl", Category = CategoryPaymentGateway, ConfigPath = "Khalti:WebsiteUrl", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "Website URL Khalti associates with this integration." },
        new() { Key = "Smtp.Host", Category = CategoryNotifications, ConfigPath = "Smtp:Host", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "SMTP server host." },
        new() { Key = "Smtp.Port", Category = CategoryNotifications, ConfigPath = "Smtp:Port", ValueType = ValueTypeInt, IsSecret = false, RequiresConfirmation = false, Description = "SMTP server port." },
        new() { Key = "Smtp.From", Category = CategoryNotifications, ConfigPath = "Smtp:From", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "From address for outgoing email." },
        new() { Key = "Jwt.SigningKey", Category = CategorySecurity, ConfigPath = "Jwt:SigningKey", ValueType = ValueTypeString, IsSecret = true, RequiresConfirmation = true, Description = "JWT signing key. Rotating this logs out every user platform-wide." },
        new() { Key = "Jwt.Issuer", Category = CategorySecurity, ConfigPath = "Jwt:Issuer", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "JWT issuer claim." },
        new() { Key = "Jwt.Audience", Category = CategorySecurity, ConfigPath = "Jwt:Audience", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "JWT audience claim." },
        new() { Key = "Jwt.AccessTokenMinutes", Category = CategorySecurity, ConfigPath = "Jwt:AccessTokenMinutes", ValueType = ValueTypeInt, IsSecret = false, RequiresConfirmation = false, Description = "Access token lifetime, in minutes." },
        new() { Key = "RateLimiting.ApiKeyRequestsPerMinute", Category = CategorySecurity, ConfigPath = "RateLimiting:ApiKeyRequestsPerMinute", ValueType = ValueTypeInt, IsSecret = false, RequiresConfirmation = false, Description = "Per-API-key request limit per minute." },
        new() { Key = "RateLimiting.PublicTrackingRequestsPerMinute", Category = CategorySecurity, ConfigPath = "RateLimiting:PublicTrackingRequestsPerMinute", ValueType = ValueTypeInt, IsSecret = false, RequiresConfirmation = false, Description = "Per-IP public tracking request limit per minute." },
        new() { Key = "Frontend.BaseUrl", Category = CategoryBranding, ConfigPath = "Frontend:BaseUrl", ValueType = ValueTypeString, IsSecret = false, RequiresConfirmation = false, Description = "Base URL used to build links in outgoing emails/notifications." },
        new() { Key = "Billing.GraceDays", Category = CategoryBranding, ConfigPath = "Billing:GraceDays", ValueType = ValueTypeInt, IsSecret = false, RequiresConfirmation = false, Description = "Days after an invoice's due date before an organization is suspended." },
    };
}
```

- [ ] **Step 3: Create the EF Core entity configurations**

`src/Modules/Platform/Infrastructure/PlatformSettingDefinitionConfiguration.cs`:

```csharp
using Courier.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Platform.Infrastructure;

public class PlatformSettingDefinitionConfiguration : IEntityTypeConfiguration<PlatformSettingDefinition>
{
    public void Configure(EntityTypeBuilder<PlatformSettingDefinition> builder)
    {
        builder.ToTable("PlatformSettingDefinitions");
        builder.HasKey(d => d.Key);
        builder.Property(d => d.Key).HasMaxLength(100);
        builder.Property(d => d.Category).HasMaxLength(50).IsRequired();
        builder.Property(d => d.ConfigPath).HasMaxLength(150).IsRequired();
        builder.Property(d => d.ValueType).HasMaxLength(20).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(500);
        builder.HasData(PlatformSettingCatalog.Definitions);
    }
}
```

`src/Modules/Platform/Infrastructure/PlatformSettingConfiguration.cs`:

```csharp
using Courier.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Platform.Infrastructure;

public class PlatformSettingConfiguration : IEntityTypeConfiguration<PlatformSetting>
{
    public void Configure(EntityTypeBuilder<PlatformSetting> builder)
    {
        builder.ToTable("PlatformSettings");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(100);
        builder.HasOne<PlatformSettingDefinition>().WithMany().HasForeignKey(s => s.Key).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 4: Register the new entities in `AppDbContext`**

In `src/Persistence/AppDbContext.cs`, add a `using Courier.Platform.Domain;` and `using Courier.Platform.Infrastructure;` near the other module usings (alphabetically, after the `Courier.Pricing.*` usings and before `Courier.Reports`/`Courier.Riders` — follow the existing alphabetical grouping in the file).

Add two `DbSet` properties right after `public DbSet<Payment> Payments => Set<Payment>();` (line 106):

```csharp
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PlatformSettingDefinition> PlatformSettingDefinitions => Set<PlatformSettingDefinition>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
```

Add two `ApplyConfiguration` calls right after `modelBuilder.ApplyConfiguration(new Courier.Billing.Infrastructure.PaymentConfiguration());` (line 181):

```csharp
        modelBuilder.ApplyConfiguration(new Courier.Billing.Infrastructure.PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new PlatformSettingDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new PlatformSettingConfiguration());
```

- [ ] **Step 5: Verify the Persistence project builds**

Run: `dotnet build src/Persistence/Persistence.csproj`
Expected: Build succeeds.

- [ ] **Step 6: Generate the migration**

Run: `dotnet ef migrations add AddPlatformSettings --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: A new migration file appears under `src/Persistence/Migrations/`, creating `PlatformSettingDefinitions` and `PlatformSettings` tables and seeding 19 rows into `PlatformSettingDefinitions` via `migrationBuilder.InsertData`.

- [ ] **Step 7: Apply the migration against the shared dev Postgres and verify**

Run: `dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: Command succeeds; the two new tables exist with 19 rows in `PlatformSettingDefinitions` and 0 rows in `PlatformSettings`.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Platform/Domain/PlatformSettingDefinition.cs src/Modules/Platform/Domain/PlatformSetting.cs src/Modules/Platform/Infrastructure/PlatformSettingCatalog.cs src/Modules/Platform/Infrastructure/PlatformSettingDefinitionConfiguration.cs src/Modules/Platform/Infrastructure/PlatformSettingConfiguration.cs src/Persistence/AppDbContext.cs src/Persistence/Migrations/
git commit -m "feat(platform): add PlatformSetting/PlatformSettingDefinition entities and seed migration"
```

---

## Task 3: Boot-safe configuration provider for non-secret values

**Files:**
- Create: `src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsConfigurationSource.cs`
- Create: `src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsConfigurationProvider.cs`
- Test: `tests/Platform.Tests/PlatformSettingsConfigurationProviderTests.cs`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: `PlatformSettingCatalog` table names (`"PlatformSettings"`, `"PlatformSettingDefinitions"`), columns `"Key"`, `"Value"`, `"IsOverridden"`, `"ConfigPath"`, `"IsSecret"` from Task 2.
- Produces: `Courier.Platform.Infrastructure.Configuration.PlatformSettingsConfigurationProvider` with a public `Reload()` method — Task 4's `PlatformSettingsReloader` and Task 6's `PlatformSettingsChangeListener` call it via `IConfigurationRoot.Providers`.

- [ ] **Step 1: Write the failing integration test**

`tests/Platform.Tests/PlatformSettingsConfigurationProviderTests.cs`:

```csharp
using Courier.Platform.Infrastructure.Configuration;
using Courier.Platform.Tests;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Courier.Platform.Tests;

public class PlatformSettingsConfigurationProviderTests
{
    [Fact]
    public void Load_NoOverrides_ReturnsEmptyData()
    {
        var provider = new PlatformSettingsConfigurationProvider(TestDbContextFactory.GetConnectionString());

        provider.Load();

        Assert.False(provider.TryGet("Esewa:ProductCode", out _));
    }

    [Fact]
    public void Load_TableMissing_DoesNotThrow()
    {
        // A connection string pointing at a database with no PlatformSettings table yet must
        // resolve to "no overrides" rather than crash — this is the real fresh-install boot
        // path (see PlatformSettingsConfigurationProvider's Load() comment).
        var provider = new PlatformSettingsConfigurationProvider(
            "Host=localhost;Port=5434;Database=postgres;Username=courier;Password=courier_dev_password");

        var exception = Record.Exception(() => provider.Load());

        Assert.Null(exception);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Platform.Tests --filter PlatformSettingsConfigurationProviderTests`
Expected: FAIL — `PlatformSettingsConfigurationProvider` doesn't exist yet.

- [ ] **Step 3: Write the configuration source and provider**

`src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsConfigurationSource.cs`:

```csharp
using Microsoft.Extensions.Configuration;

namespace Courier.Platform.Infrastructure.Configuration;

public class PlatformSettingsConfigurationSource : IConfigurationSource
{
    private readonly string _connectionString;

    public PlatformSettingsConfigurationSource(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new PlatformSettingsConfigurationProvider(_connectionString);
}
```

`src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsConfigurationProvider.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Courier.Platform.Infrastructure.Configuration;

// Non-secret values only — see spec §5. WebApplicationBuilder.Configuration is a
// ConfigurationManager, which runs a newly-Add()ed source's Load() synchronously, immediately,
// before a single builder.Services.Add...() line has resolved anything. No DI container exists
// yet at that point, so this cannot resolve IDataProtectionProvider to decrypt secrets — it opens
// its own plain NpgsqlConnection instead, using the connection string captured at construction.
public class PlatformSettingsConfigurationProvider : ConfigurationProvider
{
    private readonly string _connectionString;

    public PlatformSettingsConfigurationProvider(string connectionString)
    {
        _connectionString = connectionString;
    }

    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            using var command = new NpgsqlCommand(
                "SELECT d.\"ConfigPath\", s.\"Value\" FROM \"PlatformSettings\" s " +
                "JOIN \"PlatformSettingDefinitions\" d ON s.\"Key\" = d.\"Key\" " +
                "WHERE s.\"IsOverridden\" = TRUE AND d.\"IsSecret\" = FALSE",
                connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                data[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // "PlatformSettings"/"PlatformSettingDefinitions" don't exist yet — a brand-new
            // database where migrations (Program.cs) haven't run yet, since they run AFTER
            // builder.Build(), i.e. after this Load() already executed once. Treat as
            // "no overrides configured" rather than crashing startup.
        }
        Data = data;
    }

    // Re-runs Load() and fires the base class's change-token notification — the same mechanism
    // IOptionsMonitor<T> already listens to, so no custom pub/sub-to-options wiring is needed.
    public void Reload()
    {
        Load();
        OnReload();
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Platform.Tests --filter PlatformSettingsConfigurationProviderTests`
Expected: PASS.

- [ ] **Step 5: Register the source in `Program.cs`**

In `src/Api/Program.cs`, add a `using Courier.Platform.Infrastructure.Configuration;` alongside the existing `using Courier.Platform.Infrastructure;` (line 39). Immediately after line 61 (`var builder = WebApplication.CreateBuilder(args);`), before the `builder.Host.UseSerilog(...)` block, add:

```csharp
// Layers non-secret DB-backed setting overrides on top of appsettings/env — added last, so it
// wins on key conflicts. Must run before any builder.Services.Configure<T>(...) call below reads
// these keys via builder.Configuration.GetSection(...). See spec §5 for why secrets can't flow
// through this path.
builder.Configuration.Add(new PlatformSettingsConfigurationSource(builder.Configuration.GetConnectionString("Postgres")!));
```

- [ ] **Step 6: Run the full Platform test project and verify no regressions**

Run: `dotnet test tests/Platform.Tests`
Expected: All tests pass.

- [ ] **Step 7: Verify the API still boots**

Run: `dotnet build src/Api/Api.csproj`
Expected: Build succeeds.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsConfigurationSource.cs src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsConfigurationProvider.cs tests/Platform.Tests/PlatformSettingsConfigurationProviderTests.cs src/Api/Program.cs
git commit -m "feat(platform): add boot-safe IConfigurationProvider for non-secret platform settings"
```

---

## Task 4: Secret cache and reload helper

**Files:**
- Create: `src/Modules/Platform/Infrastructure/Configuration/IPlatformSettingsCache.cs`
- Create: `src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsCache.cs`
- Create: `src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsReloader.cs`
- Test: `tests/Platform.Tests/PlatformSettingsCacheTests.cs`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: `PlatformSetting`, `PlatformSettingDefinition` (Task 2); `PlatformSettingsConfigurationProvider` (Task 3).
- Produces: `Courier.Platform.Infrastructure.Configuration.IPlatformSettingsCache { string? TryGetSecret(string configPath); Task RefreshAsync(CancellationToken); }` and `PlatformSettingsReloader.ReloadAsync(IConfiguration, IPlatformSettingsCache, CancellationToken)` — Task 5's post-configure classes and Task 6's `PlatformSettingsChangeListener` depend on these.

- [ ] **Step 1: Write the failing test**

`tests/Platform.Tests/PlatformSettingsCacheTests.cs`:

```csharp
using Courier.Persistence;
using Courier.Platform.Domain;
using Courier.Platform.Infrastructure.Configuration;
using Courier.Platform.Tests;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Platform.Tests;

public class PlatformSettingsCacheTests
{
    private sealed class NullTenantProvider : Courier.SharedKernel.ICurrentTenantProvider
    {
        public Guid? OrganizationId => null;
    }

    private static IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Courier.SharedKernel.ICurrentTenantProvider>(new NullTenantProvider());
        services.AddScoped(sp => TestDbContextFactory.Create(sp.GetRequiredService<Courier.SharedKernel.ICurrentTenantProvider>()));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddDataProtection();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task RefreshAsync_SecretOverrideExists_DecryptsAndCachesIt()
    {
        var scopeFactory = BuildScopeFactory();
        using (var scope = scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("PlatformSettings.Secrets");
            var key = $"Esewa.SecretKey.{Guid.NewGuid():N}";
            dbContext.Set<PlatformSettingDefinition>().Add(new PlatformSettingDefinition { Key = key, Category = "PaymentGateway", ConfigPath = $"Test:{key}", ValueType = "String", IsSecret = true });
            dbContext.Set<PlatformSetting>().Add(new PlatformSetting { Key = key, Value = protector.Protect("s3cr3t"), IsOverridden = true, UpdatedAt = DateTime.UtcNow, UpdatedByUserId = Guid.NewGuid() });
            await dbContext.SaveChangesAsync();

            var cache = new PlatformSettingsCache(scopeFactory);
            await cache.RefreshAsync();

            Assert.Equal("s3cr3t", cache.TryGetSecret($"Test:{key}"));
        }
    }

    [Fact]
    public void TryGetSecret_NothingCachedYet_ReturnsNull()
    {
        var cache = new PlatformSettingsCache(BuildScopeFactory());

        Assert.Null(cache.TryGetSecret("Esewa:SecretKey"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Platform.Tests --filter PlatformSettingsCacheTests`
Expected: FAIL — `PlatformSettingsCache` doesn't exist yet.

- [ ] **Step 3: Write the cache interface and implementation**

`src/Modules/Platform/Infrastructure/Configuration/IPlatformSettingsCache.cs`:

```csharp
namespace Courier.Platform.Infrastructure.Configuration;

public interface IPlatformSettingsCache
{
    // Synchronous, in-memory only — called from IPostConfigureOptions<T>.PostConfigure, which
    // cannot be async. Returns null when no override is cached for this ConfigPath (fall back
    // to whatever appsettings/env already bound).
    string? TryGetSecret(string configPath);

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
```

`src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsCache.cs`:

```csharp
using Courier.Platform.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Courier.Platform.Infrastructure.Configuration;

// Singleton. Decrypted secret values (Esewa/Khalti/Jwt SecretKey overrides) live here, refreshed
// by PlatformSettingsChangeListener on startup, on a Redis invalidation message, and on a
// reconciliation timer. IPostConfigureOptions<T> implementations (Task 5) read from this
// synchronously — never touch the database directly on the request path.
public class PlatformSettingsCache : IPlatformSettingsCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private volatile Dictionary<string, string> _secrets = new();

    public PlatformSettingsCache(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public string? TryGetSecret(string configPath) => _secrets.TryGetValue(configPath, out var value) ? value : null;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var dataProtectionProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var protector = dataProtectionProvider.CreateProtector("PlatformSettings.Secrets");

        var next = new Dictionary<string, string>();
        try
        {
            var rows = await (
                from s in dbContext.Set<PlatformSetting>()
                join d in dbContext.Set<PlatformSettingDefinition>() on s.Key equals d.Key
                where s.IsOverridden && d.IsSecret
                select new { s.Value, d.ConfigPath }
            ).ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (row.Value is not null)
                {
                    next[row.ConfigPath] = protector.Unprotect(row.Value);
                }
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Same fresh-install race as PlatformSettingsConfigurationProvider — this service
            // starts after migrations run in this codebase's own startup order, but a production
            // deploy that starts the app before its separate migration step must not crash here.
        }
        _secrets = next;
    }
}
```

`src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsReloader.cs`:

```csharp
using Microsoft.Extensions.Configuration;

namespace Courier.Platform.Infrastructure.Configuration;

// One call refreshes both halves of the mechanism together — the non-secret IConfiguration
// provider (Task 3) and the secret cache (this file) — so IOptionsMonitor<T> consumers see a
// consistent view after a single reload, whether the changed field was secret or not.
public static class PlatformSettingsReloader
{
    public static async Task ReloadAsync(IConfiguration configuration, IPlatformSettingsCache cache, CancellationToken cancellationToken = default)
    {
        if (configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers.OfType<PlatformSettingsConfigurationProvider>())
            {
                provider.Reload();
            }
        }
        await cache.RefreshAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Platform.Tests --filter PlatformSettingsCacheTests`
Expected: PASS.

- [ ] **Step 5: Register the cache in `Program.cs`**

In `src/Api/Program.cs`, near the other Platform service registrations (after line 199, `builder.Services.AddScoped<ImpersonationService>();`), add:

```csharp
builder.Services.AddSingleton<IPlatformSettingsCache, PlatformSettingsCache>();
```

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Platform/Infrastructure/Configuration/IPlatformSettingsCache.cs src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsCache.cs src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsReloader.cs tests/Platform.Tests/PlatformSettingsCacheTests.cs src/Api/Program.cs
git commit -m "feat(platform): add secret settings cache and unified reload helper"
```

---

## Task 5: Secret post-configure options for the 4 affected options types

**Files:**
- Create: `src/Modules/Platform/Infrastructure/Configuration/SecretPostConfigureOptions.cs`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: `IPlatformSettingsCache.TryGetSecret` (Task 4); `EsewaOptions`/`KhaltiOptions` (`Courier.Billing.Infrastructure`), `JwtOptions` (`Courier.Identity.Application`), `MerchantJwtOptions` (`Courier.Merchants.Application`) — all pre-existing.
- Produces: nothing new consumed elsewhere — these run automatically whenever `IOptionsMonitor<T>.CurrentValue` is (re)built for the 4 types, once registered.

- [ ] **Step 1: Write the 4 post-configure classes**

`src/Modules/Platform/Infrastructure/Configuration/SecretPostConfigureOptions.cs`:

```csharp
using Courier.Billing.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Microsoft.Extensions.Options;

namespace Courier.Platform.Infrastructure.Configuration;

// Runs lazily, inside IOptionsFactory<T>, only when something resolves
// IOptionsMonitor<T>.CurrentValue — always inside a fully-built DI container, unlike
// PlatformSettingsConfigurationProvider.Load() (Task 3). Overwrites just the one secret
// property after Configure<T>(section) already populated everything else from IConfiguration.

public class EsewaSecretPostConfigureOptions : IPostConfigureOptions<EsewaOptions>
{
    private readonly IPlatformSettingsCache _cache;
    public EsewaSecretPostConfigureOptions(IPlatformSettingsCache cache) => _cache = cache;

    public void PostConfigure(string? name, EsewaOptions options)
    {
        var overrideValue = _cache.TryGetSecret("Esewa:SecretKey");
        if (overrideValue is not null)
        {
            options.SecretKey = overrideValue;
        }
    }
}

public class KhaltiSecretPostConfigureOptions : IPostConfigureOptions<KhaltiOptions>
{
    private readonly IPlatformSettingsCache _cache;
    public KhaltiSecretPostConfigureOptions(IPlatformSettingsCache cache) => _cache = cache;

    public void PostConfigure(string? name, KhaltiOptions options)
    {
        var overrideValue = _cache.TryGetSecret("Khalti:SecretKey");
        if (overrideValue is not null)
        {
            options.SecretKey = overrideValue;
        }
    }
}

public class JwtSecretPostConfigureOptions : IPostConfigureOptions<JwtOptions>
{
    private readonly IPlatformSettingsCache _cache;
    public JwtSecretPostConfigureOptions(IPlatformSettingsCache cache) => _cache = cache;

    public void PostConfigure(string? name, JwtOptions options)
    {
        var overrideValue = _cache.TryGetSecret("Jwt:SigningKey");
        if (overrideValue is not null)
        {
            options.SigningKey = overrideValue;
        }
    }
}

public class MerchantJwtSecretPostConfigureOptions : IPostConfigureOptions<MerchantJwtOptions>
{
    private readonly IPlatformSettingsCache _cache;
    public MerchantJwtSecretPostConfigureOptions(IPlatformSettingsCache cache) => _cache = cache;

    public void PostConfigure(string? name, MerchantJwtOptions options)
    {
        var overrideValue = _cache.TryGetSecret("Jwt:SigningKey");
        if (overrideValue is not null)
        {
            options.SigningKey = overrideValue;
        }
    }
}
```

- [ ] **Step 2: Register all 4 in `Program.cs`**

In `src/Api/Program.cs`, right after the `IPlatformSettingsCache` registration added in Task 5, add:

```csharp
builder.Services.ConfigureOptions<EsewaSecretPostConfigureOptions>();
builder.Services.ConfigureOptions<KhaltiSecretPostConfigureOptions>();
builder.Services.ConfigureOptions<JwtSecretPostConfigureOptions>();
builder.Services.ConfigureOptions<MerchantJwtSecretPostConfigureOptions>();
```

- [ ] **Step 3: Verify the API builds**

Run: `dotnet build src/Api/Api.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Platform/Infrastructure/Configuration/SecretPostConfigureOptions.cs src/Api/Program.cs
git commit -m "feat(platform): wire secret cache into Esewa/Khalti/Jwt/MerchantJwt options"
```

---

## Task 6: Redis-subscribed reload background service

**Files:**
- Create: `src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsChangeListener.cs`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: `PlatformSettingsReloader.ReloadAsync` (Task 4), `IConnectionMultiplexer` (already registered, `Program.cs:125-126`).
- Produces: subscribes to Redis channel `"platform-settings:changed"` — Task 8's `PlatformSettingsService` publishes to this same channel after every write.

- [ ] **Step 1: Write the background service**

`src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsChangeListener.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Courier.Platform.Infrastructure.Configuration;

// First Redis pub/sub usage in this codebase (elsewhere Redis is only used via GetDatabase(),
// e.g. RedisRateLimiter). Because pub/sub has no delivery guarantee, a 5-minute reconciliation
// timer runs alongside the subscription as defense-in-depth, so a missed message self-heals
// instead of silently drifting.
public class PlatformSettingsChangeListener : BackgroundService
{
    public const string ChannelName = "platform-settings:changed";
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromMinutes(5);

    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration _configuration;
    private readonly IPlatformSettingsCache _cache;
    private readonly ILogger<PlatformSettingsChangeListener> _logger;

    public PlatformSettingsChangeListener(IConnectionMultiplexer redis, IConfiguration configuration, IPlatformSettingsCache cache, ILogger<PlatformSettingsChangeListener> logger)
    {
        _redis = redis;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PlatformSettingsReloader.ReloadAsync(_configuration, _cache, stoppingToken);

        var subscriber = _redis.GetSubscriber();
        await subscriber.SubscribeAsync(RedisChannel.Literal(ChannelName), async (_, _) =>
        {
            try
            {
                await PlatformSettingsReloader.ReloadAsync(_configuration, _cache, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload platform settings after a change notification.");
            }
        });

        using var timer = new PeriodicTimer(ReconciliationInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PlatformSettingsReloader.ReloadAsync(_configuration, _cache, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Periodic platform settings reconciliation failed.");
            }
        }
    }
}
```

- [ ] **Step 2: Register the hosted service in `Program.cs`**

Right after the 4 `ConfigureOptions<...>()` calls added in Task 5, add:

```csharp
builder.Services.AddHostedService<PlatformSettingsChangeListener>();
```

- [ ] **Step 3: Verify the API builds and boots**

Run: `dotnet build src/Api/Api.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Platform/Infrastructure/Configuration/PlatformSettingsChangeListener.cs src/Api/Program.cs
git commit -m "feat(platform): add Redis-subscribed reload listener for platform settings"
```

---

## Task 7: Fix JWT signing-key validation to read live

**Files:**
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: `IOptionsMonitor<JwtOptions>` (available once Task 8 swaps `JwtOptions` consumers — but the resolver fix itself only needs `IOptionsMonitor<JwtOptions>`, which already exists as a framework type; Task 8's consumer swaps are independent of this).

- [ ] **Step 1: Add the missing `using`**

In `src/Api/Program.cs`, add `using Microsoft.Extensions.Options;` alongside the other `Microsoft.*` usings (after line 57, `using Microsoft.IdentityModel.Tokens;`).

- [ ] **Step 2: Replace the static `AddJwtBearer` configuration with a live resolver**

Replace this block (`Program.cs:257-278`):

```csharp
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, ASP.NET Core's default inbound claim map silently rewrites
        // well-known JWT claim names (e.g. "sub" -> ClaimTypes.NameIdentifier) on the
        // ClaimsPrincipal built from the token, which breaks any code (PermissionChecker,
        // audit logging, future endpoints) that reads HttpContext.User.FindFirst("sub")
        // directly. Disabling it keeps claims exactly as JwtTokenService issued them.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SigningKey"]!)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AuthSchemes.ApiKey, _ => { });
builder.Services.AddAuthorization();
```

with:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, ASP.NET Core's default inbound claim map silently rewrites
        // well-known JWT claim names (e.g. "sub" -> ClaimTypes.NameIdentifier) on the
        // ClaimsPrincipal built from the token, which breaks any code (PermissionChecker,
        // audit logging, future endpoints) that reads HttpContext.User.FindFirst("sub")
        // directly. Disabling it keeps claims exactly as JwtTokenService issued them.
        options.MapInboundClaims = false;
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AuthSchemes.ApiKey, _ => { });
builder.Services.AddAuthorization();

// IssuerSigningKeyResolver is invoked per token validation (unlike a plain IssuerSigningKey
// assignment, captured once at startup) — the generic Configure<TDep> overload injects a live
// IOptionsMonitor<JwtOptions> into this delegate (resolved once, from the root provider, when
// JwtBearerOptions is built), so the resolver closure reads jwtMonitor.CurrentValue.SigningKey
// fresh on every request. This is what makes rotating Jwt.SigningKey through the platform
// settings UI (Task 8-12) actually take effect for validation, not just for newly-signed tokens.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptionsMonitor<JwtOptions>>((options, jwtMonitor) =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtMonitor.CurrentValue.Issuer,
            ValidAudience = jwtMonitor.CurrentValue.Audience,
            IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
                new SecurityKey[] { new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtMonitor.CurrentValue.SigningKey)) },
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true
        };
    });
```

- [ ] **Step 3: Verify the API builds**

Run: `dotnet build src/Api/Api.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Run the full API test suite to confirm auth still works end-to-end**

Run: `dotnet test tests/Api.Tests`
Expected: All existing tests pass (login/auth-dependent tests exercise the new resolver path implicitly).

- [ ] **Step 5: Commit**

```bash
git add src/Api/Program.cs
git commit -m "fix(auth): make JWT signing-key validation read live via IssuerSigningKeyResolver"
```

---

## Task 8: Swap the 4 JWT-issuing services to `IOptionsMonitor`

**Files:**
- Modify: `src/Modules/Identity/Infrastructure/JwtTokenService.cs`
- Modify: `src/Modules/Platform/Infrastructure/PlatformJwtTokenService.cs`
- Modify: `src/Modules/Platform/Infrastructure/ImpersonationTokenService.cs`
- Modify: `src/Modules/Merchants/Infrastructure/MerchantJwtTokenService.cs`

**Interfaces:**
- Produces: no signature changes to `IJwtTokenService`/`IPlatformJwtTokenService`/`IImpersonationTokenService`/`IMerchantJwtTokenService` — internal implementation swap only, existing callers are unaffected.

- [ ] **Step 1: Swap `JwtTokenService`**

Replace `src/Modules/Identity/Infrastructure/JwtTokenService.cs` in full:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Courier.Identity.Application;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Courier.Identity.Infrastructure;

public class JwtTokenService : IJwtTokenService
{
    private readonly IOptionsMonitor<JwtOptions> _optionsMonitor;

    public JwtTokenService(IOptionsMonitor<JwtOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public string IssueAccessToken(Guid userId, Guid organizationId, string email)
    {
        var options = _optionsMonitor.CurrentValue;
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("org_id", organizationId.ToString()),
            new Claim("email", email)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 2: Swap `PlatformJwtTokenService`**

Replace `src/Modules/Platform/Infrastructure/PlatformJwtTokenService.cs` in full:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Courier.Identity.Application;
using Courier.Platform.Application;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Courier.Platform.Infrastructure;

// Shares JwtOptions (signing key/issuer/audience) with the staff/merchant tracks — same signing
// key means all three tracks validate against the one AddJwtBearer() scheme already registered
// in Program.cs (Phase 1); "is_super_admin" is the one claim that makes a token reach any
// RequireSuperAdmin-gated route, and deliberately carries no "org_id" claim at all (a SuperAdmin
// is not a member of any organization — HttpContextCurrentTenantProvider.OrganizationId will
// correctly resolve to null for this token, exactly like a background job's ambient context).
public class PlatformJwtTokenService : IPlatformJwtTokenService
{
    private readonly IOptionsMonitor<JwtOptions> _optionsMonitor;

    public PlatformJwtTokenService(IOptionsMonitor<JwtOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public string IssueAccessToken(Guid userId, string email)
    {
        var options = _optionsMonitor.CurrentValue;
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("email", email),
            new Claim("is_super_admin", "true")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 3: Swap `ImpersonationTokenService`**

Replace `src/Modules/Platform/Infrastructure/ImpersonationTokenService.cs` in full:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Courier.Identity.Application;
using Courier.Platform.Application;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Courier.Platform.Infrastructure;

// A deliberately different shape from PlatformJwtTokenService's own token: this one carries
// "org_id" (so HttpContextCurrentTenantProvider/PermissionChecker/branch-scope checks all
// resolve exactly as they would for a real login by the target user — this codebase's JWTs
// carry no embedded permissions, everything is re-resolved from the database by sub/org_id on
// every request, so no other backend code needs to change for impersonation to work) plus
// "impersonated_by"/"is_impersonation", and NOT "is_super_admin" — an impersonation token must
// never itself pass RequireSuperAdmin.
public class ImpersonationTokenService : IImpersonationTokenService
{
    private static readonly TimeSpan ImpersonationLifetime = TimeSpan.FromMinutes(30);

    private readonly IOptionsMonitor<JwtOptions> _optionsMonitor;

    public ImpersonationTokenService(IOptionsMonitor<JwtOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public ImpersonationTokenIssued IssueImpersonationAccessToken(Guid targetUserId, Guid organizationId, string email, Guid impersonatingSuperAdminId)
    {
        var options = _optionsMonitor.CurrentValue;
        var claims = new[]
        {
            new Claim("sub", targetUserId.ToString()),
            new Claim("org_id", organizationId.ToString()),
            new Claim("email", email),
            new Claim("impersonated_by", impersonatingSuperAdminId.ToString()),
            new Claim("is_impersonation", "true")
        };

        var expiresAt = DateTimeOffset.UtcNow.Add(ImpersonationLifetime);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        return new ImpersonationTokenIssued(accessToken, expiresAt);
    }
}
```

- [ ] **Step 4: Swap `MerchantJwtTokenService`**

Replace `src/Modules/Merchants/Infrastructure/MerchantJwtTokenService.cs` in full:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Courier.Merchants.Application;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Courier.Merchants.Infrastructure;

public class MerchantJwtTokenService : IMerchantJwtTokenService
{
    private readonly IOptionsMonitor<MerchantJwtOptions> _optionsMonitor;

    public MerchantJwtTokenService(IOptionsMonitor<MerchantJwtOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public string IssueAccessToken(Guid merchantUserId, Guid merchantId, Guid organizationId, string email)
    {
        var options = _optionsMonitor.CurrentValue;
        var claims = new[]
        {
            new Claim("sub", merchantUserId.ToString()),
            new Claim("org_id", organizationId.ToString()),
            new Claim("merchant_id", merchantId.ToString()),
            new Claim("email", email)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 5: Run the affected test suites**

Run: `dotnet test tests/Identity.Tests tests/Platform.Tests tests/Merchants.Tests tests/Api.Tests`
Expected: All pass — no consumer of `IJwtTokenService`/`IPlatformJwtTokenService`/`IImpersonationTokenService`/`IMerchantJwtTokenService` changed signature, so no other code needs edits.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Identity/Infrastructure/JwtTokenService.cs src/Modules/Platform/Infrastructure/PlatformJwtTokenService.cs src/Modules/Platform/Infrastructure/ImpersonationTokenService.cs src/Modules/Merchants/Infrastructure/MerchantJwtTokenService.cs
git commit -m "refactor(auth): read JWT signing options live via IOptionsMonitor in token issuers"
```

---

## Task 9: Swap eSewa/Khalti gateway clients to `IOptionsMonitor`

**Files:**
- Modify: `src/Modules/Billing/Infrastructure/EsewaGatewayClient.cs`
- Modify: `src/Modules/Billing/Infrastructure/KhaltiGatewayClient.cs`

**Interfaces:**
- Produces: no change to `IPaymentGatewayClient` — internal implementation swap only.

- [ ] **Step 1: Swap `EsewaGatewayClient`**

In `src/Modules/Billing/Infrastructure/EsewaGatewayClient.cs`, replace:

```csharp
    private readonly EsewaOptions _options;
    private readonly HttpClient _httpClient;

    public PaymentGateway Gateway => PaymentGateway.Esewa;

    public EsewaGatewayClient(IOptions<EsewaOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient("Esewa");
    }
```

with:

```csharp
    private readonly IOptionsMonitor<EsewaOptions> _optionsMonitor;
    private readonly HttpClient _httpClient;

    private EsewaOptions Options => _optionsMonitor.CurrentValue;

    public PaymentGateway Gateway => PaymentGateway.Esewa;

    public EsewaGatewayClient(IOptionsMonitor<EsewaOptions> optionsMonitor, IHttpClientFactory httpClientFactory)
    {
        _optionsMonitor = optionsMonitor;
        _httpClient = httpClientFactory.CreateClient("Esewa");
    }
```

Then replace every remaining `_options.` in the file with `Options.` — 6 occurrences, on the lines building `signature`/`fields` in `InitiateAsync` (`_options.ProductCode` x2), the return in `InitiateAsync` (`_options.FormUrl`), `CallStatusCheckApiAsync` (`_options.StatusCheckUrl`, `_options.ProductCode`), and `SignFields` (`_options.SecretKey`).

- [ ] **Step 2: Swap `KhaltiGatewayClient`**

In `src/Modules/Billing/Infrastructure/KhaltiGatewayClient.cs`, replace:

```csharp
    private readonly KhaltiOptions _options;
    private readonly HttpClient _httpClient;

    public PaymentGateway Gateway => PaymentGateway.Khalti;

    public KhaltiGatewayClient(IOptions<KhaltiOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient("Khalti");
    }
```

with:

```csharp
    private readonly IOptionsMonitor<KhaltiOptions> _optionsMonitor;
    private readonly HttpClient _httpClient;

    private KhaltiOptions Options => _optionsMonitor.CurrentValue;

    public PaymentGateway Gateway => PaymentGateway.Khalti;

    public KhaltiGatewayClient(IOptionsMonitor<KhaltiOptions> optionsMonitor, IHttpClientFactory httpClientFactory)
    {
        _optionsMonitor = optionsMonitor;
        _httpClient = httpClientFactory.CreateClient("Khalti");
    }
```

Then replace every remaining `_options.` in the file with `Options.` — 4 occurrences, in `InitiateAsync` (`_options.WebsiteUrl`, `_options.InitiateUrl`, `_options.SecretKey`) and `CheckStatusAsync` (`_options.LookupUrl`, `_options.SecretKey`).

- [ ] **Step 3: Run the Billing test suite**

Run: `dotnet test tests/Billing.Tests`
Expected: All pass.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Billing/Infrastructure/EsewaGatewayClient.cs src/Modules/Billing/Infrastructure/KhaltiGatewayClient.cs
git commit -m "refactor(billing): read Esewa/Khalti gateway options live via IOptionsMonitor"
```

---

## Task 10: Swap `SubscriptionService`/`InvoiceGenerationJob` to `IOptionsMonitor`

**Files:**
- Modify: `src/Modules/Billing/Application/SubscriptionService.cs`
- Modify: `src/Modules/Billing/Application/InvoiceGenerationJob.cs`

**Interfaces:**
- Produces: no public signature change.

- [ ] **Step 1: Swap `SubscriptionService`**

In `src/Modules/Billing/Application/SubscriptionService.cs`, replace:

```csharp
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly BillingOptions _options;
    private readonly ICurrentTenantProvider _currentTenantProvider;

    public SubscriptionService(DbContext dbContext, IAuditLogger auditLogger, IOptions<BillingOptions> options, ICurrentTenantProvider currentTenantProvider)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _options = options.Value;
        _currentTenantProvider = currentTenantProvider;
    }
```

with:

```csharp
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly IOptionsMonitor<BillingOptions> _optionsMonitor;
    private readonly ICurrentTenantProvider _currentTenantProvider;

    private BillingOptions Options => _optionsMonitor.CurrentValue;

    public SubscriptionService(DbContext dbContext, IAuditLogger auditLogger, IOptionsMonitor<BillingOptions> optionsMonitor, ICurrentTenantProvider currentTenantProvider)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _optionsMonitor = optionsMonitor;
        _currentTenantProvider = currentTenantProvider;
    }
```

Then in `StartTrialAsync`, replace `TrialEndsAt = now.AddDays(_options.TrialDays),` with `TrialEndsAt = now.AddDays(Options.TrialDays),`.

- [ ] **Step 2: Swap `InvoiceGenerationJob`**

In `src/Modules/Billing/Application/InvoiceGenerationJob.cs`, replace:

```csharp
    private readonly DbContext _dbContext;
    private readonly BillingOptions _options;

    public InvoiceGenerationJob(DbContext dbContext, IOptions<BillingOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }
```

with:

```csharp
    private readonly DbContext _dbContext;
    private readonly IOptionsMonitor<BillingOptions> _optionsMonitor;

    public InvoiceGenerationJob(DbContext dbContext, IOptionsMonitor<BillingOptions> optionsMonitor)
    {
        _dbContext = dbContext;
        _optionsMonitor = optionsMonitor;
    }
```

Then replace `DueAt = cycleEnd.AddDays(_options.GraceDays),` with `DueAt = cycleEnd.AddDays(_optionsMonitor.CurrentValue.GraceDays),`.

- [ ] **Step 3: Run the Billing test suite**

Run: `dotnet test tests/Billing.Tests`
Expected: All pass.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Billing/Application/SubscriptionService.cs src/Modules/Billing/Application/InvoiceGenerationJob.cs
git commit -m "refactor(billing): read BillingOptions live via IOptionsMonitor"
```

---

## Task 11: `PlatformSettingsService` application service

**Files:**
- Create: `src/Modules/Platform/Application/PlatformSettingsService.cs`
- Test: `tests/Platform.Tests/PlatformSettingsServiceTests.cs`

**Interfaces:**
- Consumes: `PlatformSetting`/`PlatformSettingDefinition` (Task 2), `PlatformSettingsReloader`/`IPlatformSettingsCache` (Task 4), `IAuditLogger` (existing), `IDataProtectionProvider` (existing), `IConnectionMultiplexer` (existing).
- Produces: `Courier.Platform.Application.PlatformSettingsService { Task<List<PlatformSettingResponse>> GetAllAsync(CancellationToken); Task UpdateAsync(string key, string value, string? confirmationPhrase, Guid actingUserId, CancellationToken); Task ResetAsync(string key, Guid actingUserId, CancellationToken); }`, `PlatformSettingResponse` record, `PlatformSettingsValidationException` — Task 12's endpoints depend on all three.

- [ ] **Step 1: Write the failing tests**

`tests/Platform.Tests/PlatformSettingsServiceTests.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Platform.Application;
using Courier.Platform.Infrastructure.Configuration;
using Courier.Platform.Tests;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace Courier.Platform.Tests;

public class PlatformSettingsServiceTests
{
    private sealed class NullTenantProvider : Courier.SharedKernel.ICurrentTenantProvider
    {
        public Guid? OrganizationId => null;
    }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopCache : IPlatformSettingsCache
    {
        public string? TryGetSecret(string configPath) => null;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static (PlatformSettingsService Service, FakeAuditLogger AuditLogger) BuildService()
    {
        var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var services = new ServiceCollection();
        services.AddDataProtection();
        var dataProtectionProvider = services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
        var configuration = new ConfigurationBuilder().Build();
        // Same hardcoded dev-container address every other Redis-touching test in this repo
        // uses (RedisRateLimiterTests.cs, RedisIdempotencyStoreTests.cs) — matches
        // docker-compose's Redis port mapping, not guessed from the Postgres connection string.
        var redis = ConnectionMultiplexer.Connect("localhost:6380");
        var auditLogger = new FakeAuditLogger();
        var service = new PlatformSettingsService(dbContext, auditLogger, dataProtectionProvider, redis, configuration, new NoopCache());
        return (service, auditLogger);
    }

    [Fact]
    public async Task UpdateAsync_NonSecretKey_PersistsPlaintextAndAuditsRealValues()
    {
        var (service, auditLogger) = BuildService();

        await service.UpdateAsync("Frontend.BaseUrl", "https://example.test", null, Guid.NewGuid());
        var all = await service.GetAllAsync();

        var setting = all.Single(s => s.Key == "Frontend.BaseUrl");
        Assert.True(setting.IsOverridden);
        Assert.Equal("https://example.test", setting.DisplayValue);
        var entry = auditLogger.Entries.Single(e => e.Action == "platform_setting.updated" && e.EntityId == "Frontend.BaseUrl");
        Assert.Equal("https://example.test", entry.After);
        Assert.True(entry.IsCrossTenantAction);
        Assert.Null(entry.OrganizationId);
    }

    [Fact]
    public async Task UpdateAsync_SecretKey_MasksDisplayValueAndRedactsAudit()
    {
        var (service, auditLogger) = BuildService();

        await service.UpdateAsync("Esewa.SecretKey", "super-secret-value", null, Guid.NewGuid());
        var all = await service.GetAllAsync();

        var setting = all.Single(s => s.Key == "Esewa.SecretKey");
        Assert.True(setting.IsOverridden);
        Assert.DoesNotContain("super-secret-value", setting.DisplayValue);
        var entry = auditLogger.Entries.Single(e => e.Action == "platform_setting.updated" && e.EntityId == "Esewa.SecretKey");
        Assert.Equal("[REDACTED]", entry.After);
    }

    [Fact]
    public async Task UpdateAsync_IntValueTypeWithNonNumericValue_ThrowsValidationException()
    {
        var (service, _) = BuildService();

        await Assert.ThrowsAsync<PlatformSettingsValidationException>(
            () => service.UpdateAsync("Billing.GraceDays", "not-a-number", null, Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_RequiresConfirmationKeyWithoutPhrase_ThrowsValidationException()
    {
        var (service, _) = BuildService();

        await Assert.ThrowsAsync<PlatformSettingsValidationException>(
            () => service.UpdateAsync("Jwt.SigningKey", "a-new-signing-key-value", null, Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_RequiresConfirmationKeyWithCorrectPhrase_Succeeds()
    {
        var (service, _) = BuildService();

        await service.UpdateAsync("Jwt.SigningKey", "a-new-signing-key-value", "REVOKE ALL SESSIONS", Guid.NewGuid());
        var all = await service.GetAllAsync();

        Assert.True(all.Single(s => s.Key == "Jwt.SigningKey").IsOverridden);
    }

    [Fact]
    public async Task ResetAsync_PreviouslyOverriddenKey_ClearsOverride()
    {
        var (service, _) = BuildService();
        await service.UpdateAsync("Frontend.BaseUrl", "https://example.test", null, Guid.NewGuid());

        await service.ResetAsync("Frontend.BaseUrl", Guid.NewGuid());
        var all = await service.GetAllAsync();

        var setting = all.Single(s => s.Key == "Frontend.BaseUrl");
        Assert.False(setting.IsOverridden);
        Assert.Null(setting.DisplayValue);
    }

    [Fact]
    public async Task GetAllAsync_UnknownDefaultState_ReturnsAllCatalogEntriesNotOverridden()
    {
        var (service, _) = BuildService();

        var all = await service.GetAllAsync();

        Assert.Equal(19, all.Count);
        Assert.All(all, s => Assert.False(s.IsOverridden));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Platform.Tests --filter PlatformSettingsServiceTests`
Expected: FAIL — `PlatformSettingsService` doesn't exist yet.

Note: these tests share the same `PlatformSettings`/`PlatformSettingDefinitions` rows as every other test run against the shared dev Postgres — `GetAllAsync_UnknownDefaultState_ReturnsAllCatalogEntriesNotOverridden` will only see exactly 19 not-overridden rows if it runs before any other test in this file overrides one. Run it in isolation if the full-file run shows a mismatch (`dotnet test tests/Platform.Tests --filter GetAllAsync_UnknownDefaultState_ReturnsAllCatalogEntriesNotOverridden`), and treat any residual overridden values from other tests as expected cross-test noise on a shared database, not a bug — this matches how every other test in this codebase already shares the one dev Postgres instance.

- [ ] **Step 3: Write `PlatformSettingsService`**

`src/Modules/Platform/Application/PlatformSettingsService.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Platform.Domain;
using Courier.Platform.Infrastructure;
using Courier.Platform.Infrastructure.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Courier.Platform.Application;

public record PlatformSettingResponse(
    string Key,
    string Category,
    string ValueType,
    bool IsSecret,
    bool RequiresConfirmation,
    string? Description,
    bool IsOverridden,
    string? DisplayValue,
    DateTime? UpdatedAt,
    Guid? UpdatedByUserId);

public class PlatformSettingsValidationException : Exception
{
    public PlatformSettingsValidationException(string message) : base(message) { }
}

public class PlatformSettingsService
{
    private const string ConfirmationPhrase = "REVOKE ALL SESSIONS";
    private const string ProtectorPurpose = "PlatformSettings.Secrets";

    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration _configuration;
    private readonly IPlatformSettingsCache _cache;

    public PlatformSettingsService(
        DbContext dbContext,
        IAuditLogger auditLogger,
        IDataProtectionProvider dataProtectionProvider,
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        IPlatformSettingsCache cache)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _dataProtectionProvider = dataProtectionProvider;
        _redis = redis;
        _configuration = configuration;
        _cache = cache;
    }

    public async Task<List<PlatformSettingResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var definitions = await _dbContext.Set<PlatformSettingDefinition>()
            .OrderBy(d => d.Category).ThenBy(d => d.Key)
            .ToListAsync(cancellationToken);
        var settings = await _dbContext.Set<PlatformSetting>().ToDictionaryAsync(s => s.Key, cancellationToken);
        var protector = _dataProtectionProvider.CreateProtector(ProtectorPurpose);

        return definitions.Select(d =>
        {
            settings.TryGetValue(d.Key, out var setting);
            var isOverridden = setting?.IsOverridden ?? false;
            string? displayValue = null;
            if (isOverridden && setting!.Value is not null)
            {
                displayValue = d.IsSecret ? MaskSecret(protector.Unprotect(setting.Value)) : setting.Value;
            }
            return new PlatformSettingResponse(
                d.Key, d.Category, d.ValueType, d.IsSecret, d.RequiresConfirmation, d.Description,
                isOverridden, displayValue, setting?.UpdatedAt, setting?.UpdatedByUserId);
        }).ToList();
    }

    public async Task UpdateAsync(string key, string value, string? confirmationPhrase, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var definition = await _dbContext.Set<PlatformSettingDefinition>().FirstOrDefaultAsync(d => d.Key == key, cancellationToken)
            ?? throw new PlatformSettingsValidationException($"Unknown setting key '{key}'.");

        ValidateType(definition, value);

        if (definition.RequiresConfirmation && confirmationPhrase != ConfirmationPhrase)
        {
            throw new PlatformSettingsValidationException($"This change requires typing the confirmation phrase \"{ConfirmationPhrase}\".");
        }

        var setting = await _dbContext.Set<PlatformSetting>().FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        var previousValue = setting?.IsOverridden == true ? setting.Value : null;

        var storedValue = definition.IsSecret
            ? _dataProtectionProvider.CreateProtector(ProtectorPurpose).Protect(value)
            : value;

        var now = DateTime.UtcNow;
        if (setting is null)
        {
            setting = new PlatformSetting { Key = key };
            _dbContext.Set<PlatformSetting>().Add(setting);
        }
        setting.Value = storedValue;
        setting.IsOverridden = true;
        setting.UpdatedAt = now;
        setting.UpdatedByUserId = actingUserId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(
            null, actingUserId, "platform_setting.updated", "PlatformSetting", key,
            Before: definition.IsSecret ? "[REDACTED]" : previousValue,
            After: definition.IsSecret ? "[REDACTED]" : value,
            IsCrossTenantAction: true), cancellationToken);

        await PlatformSettingsReloader.ReloadAsync(_configuration, _cache, cancellationToken);
        await PublishChangeAsync();
    }

    public async Task ResetAsync(string key, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var setting = await _dbContext.Set<PlatformSetting>().FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting is null || !setting.IsOverridden)
        {
            return;
        }

        setting.IsOverridden = false;
        setting.Value = null;
        setting.UpdatedAt = DateTime.UtcNow;
        setting.UpdatedByUserId = actingUserId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(
            null, actingUserId, "platform_setting.reset", "PlatformSetting", key,
            IsCrossTenantAction: true), cancellationToken);

        await PlatformSettingsReloader.ReloadAsync(_configuration, _cache, cancellationToken);
        await PublishChangeAsync();
    }

    private async Task PublishChangeAsync()
    {
        await _redis.GetSubscriber().PublishAsync(RedisChannel.Literal(PlatformSettingsChangeListener.ChannelName), "reload");
    }

    private static string MaskSecret(string plaintext) => plaintext.Length <= 4 ? "••••" : $"••••{plaintext[^4..]}";

    private static void ValidateType(PlatformSettingDefinition definition, string value)
    {
        if (definition.ValueType == PlatformSettingCatalog.ValueTypeInt && !int.TryParse(value, out _))
        {
            throw new PlatformSettingsValidationException($"'{definition.Key}' must be a whole number.");
        }
        if (definition.ValueType == PlatformSettingCatalog.ValueTypeBool && !bool.TryParse(value, out _))
        {
            throw new PlatformSettingsValidationException($"'{definition.Key}' must be true or false.");
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Platform.Tests --filter PlatformSettingsServiceTests`
Expected: PASS.

- [ ] **Step 5: Register the service in `Program.cs`**

In `src/Api/Program.cs`, near the other Platform service registrations (right after the `AddHostedService<PlatformSettingsChangeListener>()` line added in Task 6), add:

```csharp
builder.Services.AddScoped<PlatformSettingsService>();
```

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Platform/Application/PlatformSettingsService.cs tests/Platform.Tests/PlatformSettingsServiceTests.cs src/Api/Program.cs
git commit -m "feat(platform): add PlatformSettingsService with validation, encryption, and audit"
```

---

## Task 12: `PlatformSettingsEndpoints` and API tests

**Files:**
- Create: `src/Modules/Platform/Presentation/PlatformSettingsEndpoints.cs`
- Test: `tests/Api.Tests/PlatformSettingsEndpointsTests.cs`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: `PlatformSettingsService` (Task 11).
- Produces: `GET /api/v1/platform/settings`, `PUT /api/v1/platform/settings/{key}`, `POST /api/v1/platform/settings/{key}/reset`.

- [ ] **Step 1: Write the endpoints**

`src/Modules/Platform/Presentation/PlatformSettingsEndpoints.cs`:

```csharp
using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public record UpdatePlatformSettingRequest(string Value, string? ConfirmationPhrase);

public static class PlatformSettingsEndpoints
{
    public static void MapPlatformSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/settings").WithTags("Platform Settings");

        group.MapGet("/", async (PlatformSettingsService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAllAsync(cancellationToken)))
            .RequireSuperAdmin();

        group.MapPut("/{key}", async (string key, UpdatePlatformSettingRequest request, PlatformSettingsService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                await service.UpdateAsync(key, request.Value, request.ConfirmationPhrase, httpContext.User.GetUserId()!.Value, cancellationToken);
                return Results.NoContent();
            }
            catch (PlatformSettingsValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "PLATFORM_SETTING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();

        group.MapPost("/{key}/reset", async (string key, PlatformSettingsService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            await service.ResetAsync(key, httpContext.User.GetUserId()!.Value, cancellationToken);
            return Results.NoContent();
        }).RequireSuperAdmin();
    }
}
```

- [ ] **Step 2: Map the endpoints in `Program.cs`**

In `src/Api/Program.cs`, add `app.MapPlatformSettingsEndpoints();` alongside the other `app.MapPlatform*Endpoints();` calls.

- [ ] **Step 3: Write the failing API tests**

`tests/Api.Tests/PlatformSettingsEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class PlatformSettingsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformSettingsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> LoginAsSuperAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task GetSettings_AsSuperAdmin_ReturnsCatalog()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/platform/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.EnumerateArray().Any(s => s.GetProperty("key").GetString() == "Frontend.BaseUrl"));
    }

    [Fact]
    public async Task UpdateSetting_NonSecretValue_ReturnsNoContentAndPersists()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var marker = $"https://updated-{Guid.NewGuid():N}.test";

        var putResponse = await client.PutAsJsonAsync("/api/v1/platform/settings/Frontend.BaseUrl", new { value = marker, confirmationPhrase = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/v1/platform/settings");
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        var setting = body.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "Frontend.BaseUrl");
        Assert.Equal(marker, setting.GetProperty("displayValue").GetString());
    }

    [Fact]
    public async Task UpdateSetting_RequiresConfirmationWithoutPhrase_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync("/api/v1/platform/settings/Jwt.SigningKey", new { value = "a-new-signing-key", confirmationPhrase = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResetSetting_PreviouslyUpdated_ReturnsNoContentAndClearsOverride()
    {
        var client = _factory.CreateClient();
        var token = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await client.PutAsJsonAsync("/api/v1/platform/settings/Billing.GraceDays", new { value = "14", confirmationPhrase = (string?)null });

        var resetResponse = await client.PostAsync("/api/v1/platform/settings/Billing.GraceDays/reset", null);

        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);
        var getResponse = await client.GetAsync("/api/v1/platform/settings");
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        var setting = body.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "Billing.GraceDays");
        Assert.False(setting.GetProperty("isOverridden").GetBoolean());
    }

    [Fact]
    public async Task GetSettings_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = _factory.CreateClient();
        var suffix = $"nsa-settings-{Guid.NewGuid():N}"[..20];
        var registerRequest = new RegisterOrganizationRequest(
            $"Not Super Admin Settings Org {suffix}", $"nsa-set-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner");
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var registerBody = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registerBody.AccessToken);
        var response = await client.GetAsync("/api/v1/platform/settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter PlatformSettingsEndpointsTests`
Expected: PASS.

- [ ] **Step 5: Run the full backend test suite**

Run: `dotnet test`
Expected: All tests pass across every project.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Platform/Presentation/PlatformSettingsEndpoints.cs tests/Api.Tests/PlatformSettingsEndpointsTests.cs src/Api/Program.cs
git commit -m "feat(platform): add PlatformSettingsEndpoints (GET/PUT/reset)"
```

---

## Task 13: JWT rotation end-to-end test

**Files:**
- Test: `tests/Api.Tests/PlatformSettingsJwtRotationTests.cs`

**Interfaces:**
- Consumes: `PlatformSettingsEndpoints` (Task 12), the `IssuerSigningKeyResolver` fix (Task 7).

- [ ] **Step 1: Write the test**

`tests/Api.Tests/PlatformSettingsJwtRotationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

// Confirms the fix in Task 7 actually works end-to-end: rotating Jwt.SigningKey through the
// platform settings API must invalidate tokens signed with the OLD key and accept tokens signed
// with the NEW one — same-process, since PlatformSettingsService.UpdateAsync calls
// PlatformSettingsReloader.ReloadAsync() synchronously before returning (no Redis round-trip
// needed for THIS process to observe its own change), so this test is deterministic.
public class PlatformSettingsJwtRotationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformSettingsJwtRotationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> LoginAsSuperAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task RotatingSigningKey_OldTokenRejected_NewTokenAccepted()
    {
        var client = _factory.CreateClient();
        var oldToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);

        var rotateResponse = await client.PutAsJsonAsync("/api/v1/platform/settings/Jwt.SigningKey",
            new { value = $"rotated-signing-key-{Guid.NewGuid():N}-needs-to-be-long-enough-for-hmacsha256", confirmationPhrase = "REVOKE ALL SESSIONS" });
        Assert.Equal(HttpStatusCode.NoContent, rotateResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);
        var oldTokenResponse = await client.GetAsync("/api/v1/platform/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, oldTokenResponse.StatusCode);

        var newClient = _factory.CreateClient();
        var newToken = await LoginAsSuperAdminAsync(newClient);
        newClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
        var newTokenResponse = await newClient.GetAsync("/api/v1/platform/settings");
        Assert.Equal(HttpStatusCode.OK, newTokenResponse.StatusCode);

        // Leave the signing key at its default for every other test in this shared-Postgres
        // suite — reset before returning control, same convention as every other cross-test
        // side-effecting operation in this project.
        await newClient.PostAsync("/api/v1/platform/settings/Jwt.SigningKey/reset", null);
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/Api.Tests --filter PlatformSettingsJwtRotationTests`
Expected: PASS. If it fails with the old token still being accepted, the `IssuerSigningKeyResolver` fix in Task 7 was not applied correctly — re-check that `Program.cs` no longer assigns `IssuerSigningKey` anywhere and that the `AddOptions<JwtBearerOptions>` block is in place.

- [ ] **Step 3: Run the full backend test suite once more**

Run: `dotnet test`
Expected: All tests pass, including every test that logs in via any of the 3 token tracks (staff/platform/merchant) — confirms the reset in Step 1 of this test correctly restored the default signing key for subsequent test classes.

- [ ] **Step 4: Commit**

```bash
git add tests/Api.Tests/PlatformSettingsJwtRotationTests.cs
git commit -m "test(platform): verify JWT signing-key rotation invalidates old tokens end-to-end"
```

---

## Task 14: Add missing shadcn components and frontend types

**Files:**
- Create: `src/web/src/components/ui/alert-dialog.tsx` (via shadcn CLI)
- Create: `src/web/src/components/ui/switch.tsx` (via shadcn CLI)
- Create: `src/web/src/features/platform-settings/types.ts`

**Interfaces:**
- Produces: `PlatformSetting`, `PlatformSettingCategory`, `PlatformSettingValueType` types — every later frontend task imports these.

- [ ] **Step 1: Add the shadcn components**

Run: `cd src/web && npx shadcn@latest add alert-dialog switch`
Expected: `src/web/src/components/ui/alert-dialog.tsx` and `src/web/src/components/ui/switch.tsx` are created, exporting the standard shadcn API (`AlertDialog`, `AlertDialogTrigger`, `AlertDialogContent`, `AlertDialogHeader`, `AlertDialogFooter`, `AlertDialogTitle`, `AlertDialogDescription`, `AlertDialogAction`, `AlertDialogCancel`; `Switch`).

- [ ] **Step 2: Write the types file**

`src/web/src/features/platform-settings/types.ts`:

```typescript
export type PlatformSettingCategory = "PaymentGateway" | "Notifications" | "Security" | "Branding";
export type PlatformSettingValueType = "String" | "Int" | "Bool";

export interface PlatformSetting {
  key: string;
  category: PlatformSettingCategory;
  valueType: PlatformSettingValueType;
  isSecret: boolean;
  requiresConfirmation: boolean;
  description: string | null;
  isOverridden: boolean;
  displayValue: string | null;
  updatedAt: string | null;
  updatedByUserId: string | null;
}
```

- [ ] **Step 3: Verify the frontend still type-checks**

Run: `cd src/web && npm run typecheck` (or `npx tsc --noEmit` if no dedicated script exists — check `src/web/package.json` scripts first)
Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add src/web/src/components/ui/alert-dialog.tsx src/web/src/components/ui/switch.tsx src/web/src/features/platform-settings/types.ts
git commit -m "feat(web): add alert-dialog/switch shadcn components and platform-settings types"
```

---

## Task 15: `usePlatformSettings` hooks and Next.js proxy routes

**Files:**
- Create: `src/web/src/features/platform-settings/hooks/usePlatformSettings.ts`
- Create: `src/web/src/features/platform-settings/hooks/useUpdatePlatformSetting.ts`
- Create: `src/web/src/features/platform-settings/hooks/useResetPlatformSetting.ts`
- Create: `src/web/src/app/api/platform/settings/route.ts`
- Create: `src/web/src/app/api/platform/settings/[key]/route.ts`
- Create: `src/web/src/app/api/platform/settings/[key]/reset/route.ts`

**Interfaces:**
- Consumes: `PlatformSetting` (Task 14), `clientFetch` (`@/lib/api/clientFetch`, existing), `platformApiFetch` (`@/lib/auth/platformApiFetch`, existing).
- Produces: `usePlatformSettings()`, `useUpdatePlatformSetting()`, `useResetPlatformSetting()` — Task 16's settings page depends on all three.

- [ ] **Step 1: Write the query hook**

`src/web/src/features/platform-settings/hooks/usePlatformSettings.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PlatformSetting } from "@/features/platform-settings/types";

export function usePlatformSettings() {
  return useQuery({
    queryKey: ["platform-settings", "list"],
    queryFn: () => clientFetch<PlatformSetting[]>("/api/platform/settings"),
  });
}
```

- [ ] **Step 2: Write the mutation hooks**

`src/web/src/features/platform-settings/hooks/useUpdatePlatformSetting.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

interface UpdatePlatformSettingInput {
  key: string;
  value: string;
  confirmationPhrase?: string;
}

export function useUpdatePlatformSetting() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ key, value, confirmationPhrase }: UpdatePlatformSettingInput) =>
      clientFetch(`/api/platform/settings/${key}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ value, confirmationPhrase }),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["platform-settings"] }),
  });
}
```

`src/web/src/features/platform-settings/hooks/useResetPlatformSetting.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useResetPlatformSetting() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (key: string) => clientFetch(`/api/platform/settings/${key}/reset`, { method: "POST" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["platform-settings"] }),
  });
}
```

- [ ] **Step 3: Write the proxy routes**

`src/web/src/app/api/platform/settings/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function GET() {
  try {
    const response = await platformApiFetch("/api/v1/platform/settings");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/platform/settings/[key]/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function PUT(request: Request, { params }: { params: { key: string } }) {
  const requestBody = await request.text();
  try {
    const response = await platformApiFetch(`/api/v1/platform/settings/${params.key}`, {
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

`src/web/src/app/api/platform/settings/[key]/reset/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function POST(_request: Request, { params }: { params: { key: string } }) {
  try {
    const response = await platformApiFetch(`/api/v1/platform/settings/${params.key}/reset`, { method: "POST" });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 4: Verify the frontend type-checks**

Run: `cd src/web && npx tsc --noEmit`
Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/platform-settings/hooks/usePlatformSettings.ts src/web/src/features/platform-settings/hooks/useUpdatePlatformSetting.ts src/web/src/features/platform-settings/hooks/useResetPlatformSetting.ts src/web/src/app/api/platform/settings/route.ts "src/web/src/app/api/platform/settings/[key]/route.ts" "src/web/src/app/api/platform/settings/[key]/reset/route.ts"
git commit -m "feat(web): add platform settings hooks and BFF proxy routes"
```

---

## Task 16: Platform Settings page and nav link

**Files:**
- Create: `src/web/src/app/platform/settings/page.tsx`
- Create: `src/web/src/app/platform/settings/page.test.tsx`
- Modify: `src/web/src/features/platform/PlatformNav.tsx`

**Interfaces:**
- Consumes: `usePlatformSettings`/`useUpdatePlatformSetting`/`useResetPlatformSetting` (Task 15), `PlatformSetting` (Task 14), shadcn `Tabs`/`AlertDialog`/`Input`/`Label`/`Button`/`Spinner` (existing + Task 14), `ErrorState` (existing, `@/components/states/ErrorState`).

- [ ] **Step 1: Write the settings page**

`src/web/src/app/platform/settings/page.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { usePlatformSettings } from "@/features/platform-settings/hooks/usePlatformSettings";
import { useUpdatePlatformSetting } from "@/features/platform-settings/hooks/useUpdatePlatformSetting";
import { useResetPlatformSetting } from "@/features/platform-settings/hooks/useResetPlatformSetting";
import type { PlatformSetting, PlatformSettingCategory } from "@/features/platform-settings/types";

const CATEGORIES: { key: PlatformSettingCategory; label: string }[] = [
  { key: "PaymentGateway", label: "Payment Gateways" },
  { key: "Notifications", label: "Notifications" },
  { key: "Security", label: "Security" },
  { key: "Branding", label: "Branding & Flags" },
];

const CONFIRMATION_PHRASE = "REVOKE ALL SESSIONS";

function SettingField({ setting }: { setting: PlatformSetting }) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState("");
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [confirmText, setConfirmText] = useState("");
  const update = useUpdatePlatformSetting();
  const reset = useResetPlatformSetting();

  async function save(confirmationPhrase?: string) {
    await update.mutateAsync({ key: setting.key, value: draft, confirmationPhrase });
    setEditing(false);
    setDraft("");
    setConfirmOpen(false);
    setConfirmText("");
  }

  function handleSaveClick() {
    if (setting.requiresConfirmation) {
      setConfirmOpen(true);
      return;
    }
    void save();
  }

  const showEditableInput = editing || !setting.isSecret;

  return (
    <div className="space-y-2 border-b pb-4 last:border-b-0">
      <div className="flex items-center justify-between">
        <Label htmlFor={setting.key}>{setting.key}</Label>
        {setting.isOverridden && (
          <Button size="sm" variant="ghost" onClick={() => reset.mutate(setting.key)} disabled={reset.isPending}>
            Reset to default
          </Button>
        )}
      </div>
      {setting.description && <p className="text-sm text-muted-foreground">{setting.description}</p>}
      {showEditableInput ? (
        <div className="flex gap-2">
          <Input
            id={setting.key}
            type={setting.valueType === "Int" ? "number" : "text"}
            value={editing ? draft : (setting.displayValue ?? "")}
            placeholder={setting.isSecret ? "Enter a new value" : undefined}
            onChange={(e) => {
              setEditing(true);
              setDraft(e.target.value);
            }}
          />
          {editing && (
            <Button size="sm" onClick={handleSaveClick} disabled={update.isPending}>
              {update.isPending ? <Spinner className="h-4 w-4" /> : "Save"}
            </Button>
          )}
        </div>
      ) : (
        <div className="flex gap-2">
          <Input id={setting.key} value={setting.displayValue ?? "(not set)"} readOnly />
          <Button size="sm" variant="outline" onClick={() => setEditing(true)}>
            Replace
          </Button>
        </div>
      )}
      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Rotate {setting.key}?</AlertDialogTitle>
            <AlertDialogDescription>
              This immediately logs out every user across every organization on the platform. Type{" "}
              <strong>{CONFIRMATION_PHRASE}</strong> to confirm.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <Input value={confirmText} onChange={(e) => setConfirmText(e.target.value)} />
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction disabled={confirmText !== CONFIRMATION_PHRASE} onClick={() => void save(confirmText)}>
              Confirm
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

export default function PlatformSettingsPage() {
  const { data, isLoading, isError, refetch } = usePlatformSettings();

  if (isLoading) {
    return (
      <div className="space-y-4 p-6">
        <h1 className="font-display text-xl font-semibold">Platform Settings</h1>
        <Spinner className="h-6 w-6" />
      </div>
    );
  }
  if (isError || !data) {
    return (
      <ErrorState
        title="Couldn't load platform settings"
        description="Something went wrong fetching platform settings."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4 p-6">
      <h1 className="font-display text-xl font-semibold">Platform Settings</h1>
      <Tabs defaultValue="PaymentGateway">
        <TabsList>
          {CATEGORIES.map((c) => (
            <TabsTrigger key={c.key} value={c.key}>
              {c.label}
            </TabsTrigger>
          ))}
        </TabsList>
        {CATEGORIES.map((c) => (
          <TabsContent key={c.key} value={c.key} className="space-y-4">
            {data
              .filter((s) => s.category === c.key)
              .map((s) => (
                <SettingField key={s.key} setting={s} />
              ))}
          </TabsContent>
        ))}
      </Tabs>
    </div>
  );
}
```

- [ ] **Step 2: Add the nav link**

In `src/web/src/features/platform/PlatformNav.tsx`, add a `Settings` entry to the `links` array, after `Queues`:

```typescript
const links = [
  { href: "/platform/dashboard", label: "Dashboard" },
  { href: "/platform/organizations", label: "Organizations" },
  { href: "/platform/plans", label: "Plans" },
  { href: "/platform/invoices", label: "Invoices" },
  { href: "/platform/audit-logs", label: "Audit Logs" },
  { href: "/platform/jobs", label: "Jobs" },
  { href: "/platform/queues", label: "Queues" },
  { href: "/platform/settings", label: "Settings" },
];
```

- [ ] **Step 3: Write the page test**

`src/web/src/app/platform/settings/page.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const settings = [
  {
    key: "Esewa.ProductCode",
    category: "PaymentGateway",
    valueType: "String",
    isSecret: false,
    requiresConfirmation: false,
    description: "eSewa merchant product code.",
    isOverridden: false,
    displayValue: null,
    updatedAt: null,
    updatedByUserId: null,
  },
  {
    key: "Jwt.SigningKey",
    category: "Security",
    valueType: "String",
    isSecret: true,
    requiresConfirmation: true,
    description: "JWT signing key. Rotating this logs out every user platform-wide.",
    isOverridden: true,
    displayValue: "••1234",
    updatedAt: "2026-09-18T00:00:00Z",
    updatedByUserId: "11111111-1111-1111-1111-111111111111",
  },
];

const usePlatformSettings = vi.fn(() => ({ data: settings, isLoading: false, isError: false, refetch: vi.fn() }));
vi.mock("@/features/platform-settings/hooks/usePlatformSettings", () => ({
  usePlatformSettings: () => usePlatformSettings(),
}));
vi.mock("@/features/platform-settings/hooks/useUpdatePlatformSetting", () => ({
  useUpdatePlatformSetting: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
vi.mock("@/features/platform-settings/hooks/useResetPlatformSetting", () => ({
  useResetPlatformSetting: () => ({ mutate: vi.fn(), isPending: false }),
}));

import PlatformSettingsPage from "./page";

describe("PlatformSettingsPage", () => {
  it("renders the Payment Gateways tab's settings by default", () => {
    render(<PlatformSettingsPage />);

    expect(screen.getByText("Esewa.ProductCode")).toBeInTheDocument();
  });

  it("shows a Reset to default action for an overridden setting", () => {
    render(<PlatformSettingsPage />);

    expect(screen.getAllByText("Reset to default").length).toBeGreaterThan(0);
  });
});
```

- [ ] **Step 4: Run the frontend test**

Run: `cd src/web && npx vitest run src/app/platform/settings/page.test.tsx`
Expected: PASS.

- [ ] **Step 5: Run the full frontend test suite and type-check**

Run: `cd src/web && npx vitest run && npx tsc --noEmit`
Expected: All pass, no type errors.

- [ ] **Step 6: Manually verify in the browser**

Run: `cd src/web && npm run dev` (with the API also running), log in as SuperAdmin, navigate to `/platform/settings`, confirm all 4 tabs render with their fields, edit a non-secret value and confirm it persists after a page reload, and confirm the JWT signing key field requires the confirmation dialog before saving.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/app/platform/settings/page.tsx src/web/src/app/platform/settings/page.test.tsx src/web/src/features/platform/PlatformNav.tsx
git commit -m "feat(web): add Platform Settings page with per-field edit/replace/confirm"
```

---

## Self-Review Notes

- **Spec coverage:** All 4 setting categories (Task 2's catalog), live DB-override mechanism split by secret/non-secret (Tasks 3-6), JWT rotation confirmation flow (Tasks 7, 11, 13, 16), audit redaction (Task 11), SuperAdmin-only auth (Task 12), Redis pub/sub + reconciliation fallback (Task 6), masked secrets in the API/UI (Tasks 11, 16), routed page not a Sheet (Task 16) — all covered.
- **Placeholder scan:** No TODO/TBD; every step shows real, complete code or an exact command.
- **Type consistency:** `PlatformSettingResponse`'s fields (`Key, Category, ValueType, IsSecret, RequiresConfirmation, Description, IsOverridden, DisplayValue, UpdatedAt, UpdatedByUserId`) match `PlatformSetting.ts`'s camelCase fields exactly (`key, category, valueType, isSecret, requiresConfirmation, description, isOverridden, displayValue, updatedAt, updatedByUserId`) under System.Text.Json's default camelCase serialization, consistent with how `Plan`/`MonthlyPriceNpr` already round-trips in `platform-plans`. `IPlatformSettingsCache.TryGetSecret`/`RefreshAsync` signatures are identical between the interface (Task 4), the implementation (Task 4), the 4 post-configure consumers (Task 5), and the reloader/listener/service call sites (Tasks 4, 6, 11).
- **Out of scope, confirmed absent from every task:** rewriting `.env`/compose files, bootstrap connection strings, org-level settings, multi-environment promotion, SMTP auth fields — none appear anywhere above.
