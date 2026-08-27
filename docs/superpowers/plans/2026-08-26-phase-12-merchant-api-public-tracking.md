# Phase 12 — Merchant API, Public Tracking, API Keys Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **This round the user chose Subagent-Driven execution** (departing from Phase 11's inline-execution precedent).

**Goal:** Build the backend-only slice of Phase 12: API keys as a second Merchant API credential, Redis-backed rate limiting and idempotency, a merchant `PickupRequest` feature, merchant-scoped COD settlement reads, and public `/track/{awb}` tracking — completing the Merchant API catalog from `API-SPEC.md` §3.

**Architecture:** A new `ApiKey` entity (Merchants module) authenticates via a second ASP.NET Core auth scheme (`"ApiKey"`) that manufactures the same claim shape a merchant JWT already carries, so existing `GetMerchantId()`-based code works unchanged for either credential. Redis gets its first real (non-health-check) use via two small SharedKernel abstractions (`IRateLimiter`, `IIdempotencyStore`). A new `PickupRequest` entity lives in the Shipments module. Public tracking is a new anonymous, rate-limited endpoint reading Shipment data with `.IgnoreQueryFilters()` (no tenant context exists on an anonymous request).

**Tech Stack:** ASP.NET Core minimal APIs, EF Core/PostgreSQL, StackExchange.Redis (new), existing JWT bearer auth infrastructure.

**Spec:** `docs/superpowers/specs/2026-08-26-phase-12-merchant-api-public-tracking-design.md`

## Global Constraints

- Backend-only this phase — no frontend work (`src/web` stays untouched).
- `ApiKey.Scopes[]` reuses `MerchantPermissionCatalog` permission strings verbatim — no new permission model.
- API keys authenticate via a new `"ApiKey"` scheme producing claims `sub` (=`ApiKey.Id`), `org_id`, `merchant_id`, `api_key_scopes` — the exact same claim *types* (`sub`, `org_id`, `merchant_id`) a merchant JWT already emits (`MerchantJwtTokenService.IssueAccessToken`), so `ClaimsPrincipalExtensions.GetMerchantId()`/`GetUserId()` and `HttpContextCurrentTenantProvider` (which reads `"org_id"`) work unchanged for either credential.
- API-key auth is **opt-in per endpoint group**, not a blanket replacement of `.RequireMerchantPermission()`. Endpoints extended to accept it this phase: Shipments (already dual-branched manually), Webhooks, new Cod settlement reads, new Pickups routes. Endpoints that stay JWT-only (deliberately, to avoid a self-escalation surface): API-key management itself, merchant staff invite, KYC upload, profile update.
- Idempotency (`POST /shipments`, `POST /shipments/{id}/cancel`, `POST /pickups`) is enforced **only for merchant-track callers** (JWT or API key) — staff JWT callers on the same routes are completely untouched, per the approved spec's explicit scope decision (avoids retrofitting prior-phase staff tests).
- Any query touching a tenant-scoped table from a context with no authenticated caller (public tracking) must use a deliberate, commented `.IgnoreQueryFilters()` plus an explicit trusted predicate — never rely on the ambient tenant filter, per the Phase 11 lesson recorded in `CLARIFICATIONS.md` #12 (`AppDbContext`'s `HasQueryFilter`s evaluate to false for every row, not "unfiltered," when `ICurrentTenantProvider.OrganizationId` is null).
- New entities follow this codebase's established tenant-isolation pattern: own `OrganizationId` column, FK to `Organization` + `HasQueryFilter` registered in `AppDbContext.OnModelCreating` (Persistence has the only project reference that can wire this, mirroring every prior phase's entities).
- Any new entry added to `Identity.Application.PermissionCatalog.All` needs its own EF migration generated and applied **immediately** in the same task — it's `HasData`-seeded (`PermissionConfiguration.cs`), so a later phase's `Database.Migrate()` will otherwise fail `PendingModelChangesWarning` (the exact Phase 7 `manifest.read` lesson).
- Every new migration: `dotnet ef migrations add <Name> --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`, then confirm by reading the generated file that no unrelated tables changed. Migrations apply automatically the next time a test runs `WebApplicationFactory<Program>` in Development (`Program.cs`'s `Database.Migrate()` call) — no separate `dotnet ef database update` step.
- Full solution build + test run (`dotnet build Courier.slnx` then `dotnet test Courier.slnx`) must stay green after every task.

---

### Task 1: Redis Infrastructure — `IRateLimiter` and `IIdempotencyStore`

**Files:**
- Modify: `src/SharedKernel/SharedKernel.csproj` (add `StackExchange.Redis` package)
- Create: `src/SharedKernel/IRateLimiter.cs`
- Create: `src/SharedKernel/RedisRateLimiter.cs`
- Create: `src/SharedKernel/IIdempotencyStore.cs`
- Create: `src/SharedKernel/RedisIdempotencyStore.cs`
- Modify: `src/Api/Api.csproj` (add `StackExchange.Redis` explicitly too, since `Program.cs` constructs the `IConnectionMultiplexer` directly)
- Modify: `src/Api/Program.cs` (add `IConnectionMultiplexer` singleton registration, `IRateLimiter`/`IIdempotencyStore` registration, right after the existing RabbitMQ `IConnection` singleton block)
- Test: `tests/SharedKernel.Tests/RedisRateLimiterTests.cs` (new test project — check first whether `tests/SharedKernel.Tests/` already exists; if not, create it following the exact `.csproj` shape of `tests/Events.Tests/Events.Tests.csproj`)
- Test: `tests/SharedKernel.Tests/RedisIdempotencyStoreTests.cs`

**Interfaces:**
- Produces: `Courier.SharedKernel.IRateLimiter.TryAcquireAsync(string partitionKey, int limit, TimeSpan window, CancellationToken ct = default) : Task<RateLimitResult>`, where `RateLimitResult` is `record RateLimitResult(bool Allowed, TimeSpan RetryAfter)`.
- Produces: `Courier.SharedKernel.IIdempotencyStore.GetAsync(string key, CancellationToken ct = default) : Task<IdempotencyRecord?>` and `.StoreAsync(string key, IdempotencyRecord record, TimeSpan ttl, CancellationToken ct = default) : Task`, where `IdempotencyRecord` is `record IdempotencyRecord(string RequestBodyHash, int StatusCode, string ResponseBody)`.
- Consumes: nothing from earlier tasks (this is the foundational task).

These tests run against the real dev Redis container (`courier-redis-1`, connection string `localhost:6380` per `appsettings.Development.json`) — not a fake, matching this project's established "test against the real infrastructure it depends on" pattern. Before starting, confirm the container is running: `docker ps --filter name=courier-redis-1`; if stopped, `docker start courier-redis-1`.

- [ ] **Step 1: Add the StackExchange.Redis package**

Run: `dotnet add src/SharedKernel/SharedKernel.csproj package StackExchange.Redis`
Run: `dotnet add src/Api/Api.csproj package StackExchange.Redis`
Expected: Both `.csproj` files gain a `<PackageReference Include="StackExchange.Redis" ... />` line with the resolved latest stable version.

- [ ] **Step 2: Create the test project if it doesn't already exist**

Run: `ls tests/SharedKernel.Tests` — if it errors (doesn't exist), create `tests/SharedKernel.Tests/SharedKernel.Tests.csproj` matching the exact package versions already pinned in `tests/Events.Tests/Events.Tests.csproj` (read that file first), with a single `<ProjectReference Include="..\..\src\SharedKernel\SharedKernel.csproj" />`. Then: `dotnet sln Courier.slnx add tests/SharedKernel.Tests/SharedKernel.Tests.csproj`.

- [ ] **Step 3: Write the failing rate limiter test**

Create `tests/SharedKernel.Tests/RedisRateLimiterTests.cs`:

```csharp
using Courier.SharedKernel;
using StackExchange.Redis;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class RedisRateLimiterTests
{
    private static IConnectionMultiplexer CreateConnection() => ConnectionMultiplexer.Connect("localhost:6380");

    [Fact]
    public async Task TryAcquireAsync_UnderLimit_ReturnsAllowedTrue()
    {
        var limiter = new RedisRateLimiter(CreateConnection());
        var partitionKey = $"test-{Guid.NewGuid():N}";

        var result = await limiter.TryAcquireAsync(partitionKey, limit: 5, window: TimeSpan.FromSeconds(10));

        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task TryAcquireAsync_ExceedsLimit_ReturnsAllowedFalseWithPositiveRetryAfter()
    {
        var limiter = new RedisRateLimiter(CreateConnection());
        var partitionKey = $"test-{Guid.NewGuid():N}";

        for (var i = 0; i < 3; i++)
        {
            await limiter.TryAcquireAsync(partitionKey, limit: 3, window: TimeSpan.FromSeconds(10));
        }
        var result = await limiter.TryAcquireAsync(partitionKey, limit: 3, window: TimeSpan.FromSeconds(10));

        Assert.False(result.Allowed);
        Assert.True(result.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task TryAcquireAsync_DifferentPartitionKeys_AreIndependent()
    {
        var limiter = new RedisRateLimiter(CreateConnection());
        var partitionA = $"test-a-{Guid.NewGuid():N}";
        var partitionB = $"test-b-{Guid.NewGuid():N}";

        for (var i = 0; i < 3; i++)
        {
            await limiter.TryAcquireAsync(partitionA, limit: 3, window: TimeSpan.FromSeconds(10));
        }
        var resultA = await limiter.TryAcquireAsync(partitionA, limit: 3, window: TimeSpan.FromSeconds(10));
        var resultB = await limiter.TryAcquireAsync(partitionB, limit: 3, window: TimeSpan.FromSeconds(10));

        Assert.False(resultA.Allowed);
        Assert.True(resultB.Allowed);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/SharedKernel.Tests/SharedKernel.Tests.csproj`
Expected: FAIL to compile — `RedisRateLimiter`/`RateLimitResult` don't exist yet.

- [ ] **Step 5: Implement `IRateLimiter`/`RedisRateLimiter`**

Create `src/SharedKernel/IRateLimiter.cs`:

```csharp
namespace Courier.SharedKernel;

public record RateLimitResult(bool Allowed, TimeSpan RetryAfter);

public interface IRateLimiter
{
    Task<RateLimitResult> TryAcquireAsync(string partitionKey, int limit, TimeSpan window, CancellationToken cancellationToken = default);
}
```

Create `src/SharedKernel/RedisRateLimiter.cs`:

```csharp
using StackExchange.Redis;

namespace Courier.SharedKernel;

// Fixed-window counter, not sliding — a burst right at a window boundary can admit up to 2x
// the limit across the boundary. Accepted trade-off: a sliding-window implementation needs a
// sorted-set + per-request cleanup, meaningfully more Redis round-trips, for a guarantee this
// phase's rate limits (abuse prevention, not billing-grade precision) don't need.
public class RedisRateLimiter : IRateLimiter
{
    private readonly IConnectionMultiplexer _connection;

    public RedisRateLimiter(IConnectionMultiplexer connection)
    {
        _connection = connection;
    }

    public async Task<RateLimitResult> TryAcquireAsync(string partitionKey, int limit, TimeSpan window, CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        var windowBucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / (long)window.TotalSeconds;
        var key = $"ratelimit:{partitionKey}:{windowBucket}";

        var count = await db.StringIncrementAsync(key);
        if (count == 1)
        {
            await db.KeyExpireAsync(key, window);
        }

        if (count > limit)
        {
            var ttl = await db.KeyTimeToLiveAsync(key);
            return new RateLimitResult(false, ttl ?? window);
        }

        return new RateLimitResult(true, TimeSpan.Zero);
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/SharedKernel.Tests/SharedKernel.Tests.csproj`
Expected: PASS (all 3 tests).

- [ ] **Step 7: Write the failing idempotency store test**

Create `tests/SharedKernel.Tests/RedisIdempotencyStoreTests.cs`:

```csharp
using Courier.SharedKernel;
using StackExchange.Redis;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class RedisIdempotencyStoreTests
{
    private static IConnectionMultiplexer CreateConnection() => ConnectionMultiplexer.Connect("localhost:6380");

    [Fact]
    public async Task GetAsync_NoRecordStored_ReturnsNull()
    {
        var store = new RedisIdempotencyStore(CreateConnection());
        var key = $"test-{Guid.NewGuid():N}";

        var result = await store.GetAsync(key);

        Assert.Null(result);
    }

    [Fact]
    public async Task StoreThenGet_RoundTripsExactRecord()
    {
        var store = new RedisIdempotencyStore(CreateConnection());
        var key = $"test-{Guid.NewGuid():N}";
        var record = new IdempotencyRecord("hash-abc", 201, "{\"id\":\"123\"}");

        await store.StoreAsync(key, record, TimeSpan.FromMinutes(5));
        var result = await store.GetAsync(key);

        Assert.NotNull(result);
        Assert.Equal(record.RequestBodyHash, result!.RequestBodyHash);
        Assert.Equal(record.StatusCode, result.StatusCode);
        Assert.Equal(record.ResponseBody, result.ResponseBody);
    }
}
```

- [ ] **Step 8: Run the test to verify it fails**

Run: `dotnet test tests/SharedKernel.Tests/SharedKernel.Tests.csproj`
Expected: FAIL to compile — `RedisIdempotencyStore`/`IdempotencyRecord` don't exist yet.

- [ ] **Step 9: Implement `IIdempotencyStore`/`RedisIdempotencyStore`**

Create `src/SharedKernel/IIdempotencyStore.cs`:

```csharp
namespace Courier.SharedKernel;

public record IdempotencyRecord(string RequestBodyHash, int StatusCode, string ResponseBody);

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task StoreAsync(string key, IdempotencyRecord record, TimeSpan ttl, CancellationToken cancellationToken = default);
}
```

Create `src/SharedKernel/RedisIdempotencyStore.cs`:

```csharp
using System.Text.Json;
using StackExchange.Redis;

namespace Courier.SharedKernel;

public class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _connection;

    public RedisIdempotencyStore(IConnectionMultiplexer connection)
    {
        _connection = connection;
    }

    public async Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        var value = await db.StringGetAsync($"idem:{key}");
        if (!value.HasValue)
        {
            return null;
        }
        return JsonSerializer.Deserialize<IdempotencyRecord>(value!);
    }

    public async Task StoreAsync(string key, IdempotencyRecord record, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        await db.StringSetAsync($"idem:{key}", JsonSerializer.Serialize(record), ttl);
    }
}
```

- [ ] **Step 10: Run the test to verify it passes**

Run: `dotnet test tests/SharedKernel.Tests/SharedKernel.Tests.csproj`
Expected: PASS (both tests).

- [ ] **Step 11: Wire the Redis connection and both services into `Program.cs`**

Add `using StackExchange.Redis;` near the top of `src/Api/Program.cs`. Right after the existing RabbitMQ `IConnection` singleton registration block (before `builder.Services.AddHangfire(...)`), add:

```csharp
// First real (non-health-check) use of Redis in this codebase — backs the new
// IRateLimiter/IIdempotencyStore (Phase 12). Registered as a singleton the same way the
// RabbitMQ connection above is: ConnectionMultiplexer is thread-safe and expensive to open,
// so the whole process shares one.
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
builder.Services.AddSingleton<IRateLimiter, RedisRateLimiter>();
builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
```

- [ ] **Step 12: Build and run the full solution to confirm no regressions**

Run: `dotnet build Courier.slnx`
Expected: Build succeeds.
Run: `dotnet test Courier.slnx`
Expected: All existing tests still pass; the 5 new `SharedKernel.Tests` tests pass too.

- [ ] **Step 13: Commit**

```bash
git add src/SharedKernel src/Api/Api.csproj src/Api/Program.cs tests/SharedKernel.Tests Courier.slnx
git commit -m "feat(shared-kernel): add Redis-backed IRateLimiter and IIdempotencyStore"
```

---

### Task 2: `ApiKey` domain entity, EF configuration, and migration

**Files:**
- Create: `src/Modules/Merchants/Domain/ApiKey.cs`
- Create: `src/Modules/Merchants/Infrastructure/ApiKeyConfiguration.cs`
- Modify: `src/Persistence/AppDbContext.cs` (`DbSet<ApiKey>`, `ApplyConfiguration`, FK to `Organization`, tenant `HasQueryFilter`)
- Migration: `src/Persistence/Migrations/*_AddApiKeys.cs` (generated)

**Interfaces:**
- Produces: `Courier.Merchants.Domain.ApiKey` with fields `Id, MerchantId, OrganizationId, HashedKey, Prefix, string[] Scopes, IsActive, CreatedAt, RevokedAt (DateTime?), LastUsedAt (DateTime?)` — matches `docs/CLARIFICATIONS.md` #7 plus `LastUsedAt`, needed for the request-logging/last-seen story in Task 13 and not worth a follow-up migration to bolt on later.
- Consumes: nothing new — `Merchant`/`Organization` FKs only, both already in `AppDbContext`.

No dedicated unit test this task — `ApiKey` is a plain data-holder entity with no domain logic (parallel to `WebhookSubscription`, which also shipped with config-only, no domain test, and got behavioral coverage from `ApiKeyService`'s own tests in Task 3).

- [ ] **Step 1: Create the `ApiKey` domain entity**

Create `src/Modules/Merchants/Domain/ApiKey.cs`:

```csharp
namespace Courier.Merchants.Domain;

// HashedKey stores SHA-256(secret) — never the plaintext. The plaintext (formatted
// "{Prefix}.{secret}") is shown exactly once, in the POST /api/v1/merchant/api-keys response
// body, same one-time-reveal pattern as WebhookSubscription.Secret. Prefix is stored separately,
// unhashed, so ApiKeyAuthenticationHandler (Task 5) can look up a candidate key by prefix in
// O(1) before it ever hashes and compares the full secret.
public class ApiKey
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Guid OrganizationId { get; set; }
    public required string HashedKey { get; set; }
    public required string Prefix { get; set; }
    public required string[] Scopes { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

Create `src/Modules/Merchants/Infrastructure/ApiKeyConfiguration.cs`:

```csharp
using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Merchants.Infrastructure;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("ApiKeys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.HashedKey).IsRequired();
        builder.Property(k => k.Prefix).HasMaxLength(16).IsRequired();

        builder.HasIndex(k => k.OrganizationId);
        builder.HasIndex(k => k.MerchantId);
        // ApiKeyAuthenticationHandler's (Task 5) hot path: look up the one active key row for a
        // presented prefix. Unique because a prefix collision would make lookup ambiguous —
        // ApiKeyService (Task 3) retries generation on collision.
        builder.HasIndex(k => k.Prefix).IsUnique();
    }
}
```

- [ ] **Step 3: Register `ApiKey` in `AppDbContext`**

In `src/Persistence/AppDbContext.cs`:

Add a `DbSet` next to the other Merchants entities (near line 62, after `MerchantKycDocument`):

```csharp
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
```

Add the configuration application next to the other Merchants ones (near line 117, after `MerchantKycDocumentConfiguration`):

```csharp
        modelBuilder.ApplyConfiguration(new ApiKeyConfiguration());
```

Add the FK-to-`Organization` registration next to the other Merchants entities' (near line 184, after `MerchantKycDocument`'s), following the same "Merchants has no project reference to Identity" comment precedent already on that block:

```csharp
        modelBuilder.Entity<ApiKey>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(k => k.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
```

Add the tenant query filter next to the other Merchants entities' (near line 309, after `MerchantKycDocument`'s):

```csharp
        modelBuilder.Entity<ApiKey>().HasQueryFilter(k => _currentTenantProvider.OrganizationId != null && k.OrganizationId == _currentTenantProvider.OrganizationId);
```

Note: `ApiKeyAuthenticationHandler` (Task 5) runs *before* any tenant context exists for the request, so its own lookup query must use `.IgnoreQueryFilters()` with an explicit `Prefix == candidatePrefix && IsActive` predicate — same reasoning as the public-tracking endpoint (Task 12). This is implemented in Task 5, noted here so the filter above isn't mistaken for something that would block it.

- [ ] **Step 4: Generate and inspect the migration**

Run: `dotnet ef migrations add AddApiKeys --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`

Read the generated migration file. Expected: only one new table, `ApiKeys`, with columns matching the entity, a unique index on `Prefix`, non-unique indexes on `OrganizationId`/`MerchantId`, and an FK to `Organizations`. No unrelated table should appear in the diff — if one does, stop and investigate before proceeding (an untracked prior model change, not something to paper over here).

- [ ] **Step 5: Build and test the full solution**

Run: `dotnet build Courier.slnx`
Expected: Build succeeds.
Run: `dotnet test Courier.slnx`
Expected: All existing tests still pass (the new table has no consumers yet, so no new tests are expected to appear this task).

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Merchants/Domain/ApiKey.cs src/Modules/Merchants/Infrastructure/ApiKeyConfiguration.cs src/Persistence/AppDbContext.cs src/Persistence/Migrations
git commit -m "feat(merchants): add ApiKey entity, EF configuration, and migration"
```

---

### Task 3: `ApiKeyService` (Create/List/Revoke) + DTOs + unit tests

**Files:**
- Modify: `src/Modules/Merchants/Application/MerchantPermissionCatalog.cs` (add `ApiKeysManage`, Owner-only)
- Create: `src/Modules/Merchants/Application/ApiKeyDtos.cs`
- Create: `src/Modules/Merchants/Application/ApiKeyService.cs`
- Modify: `src/Api/Program.cs` (`builder.Services.AddScoped<ApiKeyService>();` next to the other Merchants services, near line 126)
- Test: `tests/Merchants.Tests/ApiKeyServiceTests.cs`

**Interfaces:**
- Produces: `Courier.Merchants.Application.ApiKeyService.CreateApiKeyAsync(CreateApiKeyRequest, ClaimsPrincipal actingUser, CancellationToken) : Task<ApiKeyCreatedResponse>`, `.ListApiKeysAsync(ClaimsPrincipal user, CancellationToken) : Task<List<ApiKeyResponse>>`, `.RevokeApiKeyAsync(Guid id, ClaimsPrincipal actingUser, CancellationToken) : Task<ApiKeyResponse?>`.
- Consumes: `Courier.Merchants.Domain.ApiKey` (Task 2), existing `MerchantUser`/`Merchant`, `MerchantPermissionCatalog`, `IAuditLogger`.

Only JWT-authenticated Owners reach this service (Task 4 wires its endpoints JWT-only, per the self-escalation carve-out in Global Constraints) — so `actingUser.GetUserId()` here always resolves a real `MerchantUser.Id`, never an `ApiKey.Id`, and needs no special-casing.

- [ ] **Step 1: Add the `ApiKeysManage` merchant permission**

In `src/Modules/Merchants/Application/MerchantPermissionCatalog.cs`, add a constant next to `WebhooksManage`:

```csharp
    // API key management is an account-level integration setting, same sensitivity tier as
    // StaffManage/WebhooksManage — Owner-only. An API key can never hold this scope itself
    // (enforced by keeping key-management endpoints JWT-only in Task 4, not by this catalog),
    // which is what keeps an API key from managing/revoking other API keys.
    public const string ApiKeysManage = "merchant.apikeys.manage";
```

Add it to `OwnerPermissions` only (not `StaffPermissions`):

```csharp
    private static readonly string[] OwnerPermissions =
    {
        ProfileRead, ProfileUpdate, StaffManage, KycUpload, ShipmentCreate, ShipmentRead, ShipmentCancel, WebhooksManage, ApiKeysManage
    };
```

- [ ] **Step 2: Write the DTOs**

Create `src/Modules/Merchants/Application/ApiKeyDtos.cs`:

```csharp
namespace Courier.Merchants.Application;

// Scopes null/empty on create means "default to every permission the creating Owner's own role
// grants" (spec §4.1) — not "no permissions". A caller who genuinely wants a narrower key must
// list scopes explicitly; there is no way to request zero scopes.
public record CreateApiKeyRequest(string[]? Scopes = null);

public record ApiKeyResponse(Guid Id, string Prefix, string[] Scopes, bool IsActive, DateTime CreatedAt, DateTime? RevokedAt, DateTime? LastUsedAt);

// PlaintextKey appears here only — the one-time reveal. Never reconstructable from ApiKeyResponse
// or any later read.
public record ApiKeyCreatedResponse(Guid Id, string PlaintextKey, string Prefix, string[] Scopes, DateTime CreatedAt);
```

- [ ] **Step 3: Write the failing tests**

Create `tests/Merchants.Tests/ApiKeyServiceTests.cs`:

```csharp
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Courier.Audit.Application;
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Merchants.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class ApiKeyServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantUserConfiguration());
            modelBuilder.ApplyConfiguration(new ApiKeyConfiguration());
        }
    }

    private static TestDbContext CreateContext() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static ClaimsPrincipal OwnerPrincipal(Guid userId, Guid merchantId) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", userId.ToString()),
        new Claim("merchant_id", merchantId.ToString())
    }, "test"));

    private static async Task<MerchantUser> SeedOwnerAsync(TestDbContext dbContext, Guid merchantId, Guid organizationId, MerchantUserRole role = MerchantUserRole.Owner)
    {
        var owner = new MerchantUser
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            OrganizationId = organizationId,
            Email = $"owner-{Guid.NewGuid():N}@example.com",
            PasswordHash = "not-used",
            FullName = "Owner Person",
            Role = role,
            IsActive = true
        };
        dbContext.Set<MerchantUser>().Add(owner);
        await dbContext.SaveChangesAsync();
        return owner;
    }

    [Fact]
    public async Task CreateApiKeyAsync_NoScopesRequested_DefaultsToAllOfCreatorsPermissions()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        var service = new ApiKeyService(dbContext, new NoopAuditLogger());

        var result = await service.CreateApiKeyAsync(new CreateApiKeyRequest(), OwnerPrincipal(owner.Id, merchantId));

        Assert.Equal(MerchantPermissionCatalog.RolePermissions[MerchantUserRole.Owner].OrderBy(s => s), result.Scopes.OrderBy(s => s));
    }

    [Fact]
    public async Task CreateApiKeyAsync_ScopeExceedsCreatorsPermissions_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var staff = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid(), MerchantUserRole.Staff);
        var service = new ApiKeyService(dbContext, new NoopAuditLogger());

        await Assert.ThrowsAsync<MerchantValidationException>(() =>
            service.CreateApiKeyAsync(new CreateApiKeyRequest(new[] { MerchantPermissionCatalog.StaffManage }), OwnerPrincipal(staff.Id, merchantId)));
    }

    [Fact]
    public async Task CreateApiKeyAsync_ReturnedPlaintextSecretHashesToStoredHashedKey()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        var service = new ApiKeyService(dbContext, new NoopAuditLogger());

        var result = await service.CreateApiKeyAsync(new CreateApiKeyRequest(), OwnerPrincipal(owner.Id, merchantId));
        var stored = await dbContext.Set<ApiKey>().SingleAsync(k => k.Id == result.Id);
        var secret = result.PlaintextKey.Split('.', 2)[1];

        Assert.Equal(stored.Prefix, result.Prefix);
        Assert.Equal(stored.HashedKey, Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
    }

    [Fact]
    public async Task ListApiKeysAsync_ReturnsOnlyKeysForThatMerchant()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        var otherOwner = await SeedOwnerAsync(dbContext, otherMerchantId, Guid.NewGuid());
        var service = new ApiKeyService(dbContext, new NoopAuditLogger());
        await service.CreateApiKeyAsync(new CreateApiKeyRequest(), OwnerPrincipal(owner.Id, merchantId));
        await service.CreateApiKeyAsync(new CreateApiKeyRequest(), OwnerPrincipal(otherOwner.Id, otherMerchantId));

        var listed = await service.ListApiKeysAsync(OwnerPrincipal(owner.Id, merchantId));

        Assert.Single(listed);
    }

    [Fact]
    public async Task RevokeApiKeyAsync_OwnKey_SetsInactiveAndRevokedAt()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        var service = new ApiKeyService(dbContext, new NoopAuditLogger());
        var created = await service.CreateApiKeyAsync(new CreateApiKeyRequest(), OwnerPrincipal(owner.Id, merchantId));

        var revoked = await service.RevokeApiKeyAsync(created.Id, OwnerPrincipal(owner.Id, merchantId));

        Assert.NotNull(revoked);
        Assert.False(revoked!.IsActive);
        Assert.NotNull(revoked.RevokedAt);
    }

    [Fact]
    public async Task RevokeApiKeyAsync_KeyBelongsToAnotherMerchant_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        var otherOwner = await SeedOwnerAsync(dbContext, otherMerchantId, Guid.NewGuid());
        var service = new ApiKeyService(dbContext, new NoopAuditLogger());
        var created = await service.CreateApiKeyAsync(new CreateApiKeyRequest(), OwnerPrincipal(otherOwner.Id, otherMerchantId));

        var result = await service.RevokeApiKeyAsync(created.Id, OwnerPrincipal(owner.Id, merchantId));

        Assert.Null(result);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj`
Expected: FAIL to compile — `ApiKeyService` doesn't exist yet.

- [ ] **Step 5: Implement `ApiKeyService`**

Create `src/Modules/Merchants/Application/ApiKeyService.cs`:

```csharp
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Courier.Audit.Application;
using Courier.Merchants.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class ApiKeyService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;

    public ApiKeyService(DbContext dbContext, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
    }

    public async Task<ApiKeyCreatedResponse> CreateApiKeyAsync(CreateApiKeyRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = actingUser.GetMerchantId()
            ?? throw new MerchantValidationException("Acting merchant could not be resolved.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new MerchantValidationException("Acting user could not be resolved.");

        var actingMerchantUser = await _dbContext.Set<MerchantUser>()
            .FirstOrDefaultAsync(mu => mu.Id == actingUserId, cancellationToken)
            ?? throw new MerchantValidationException("Acting merchant user could not be resolved.");

        var grantablePermissions = MerchantPermissionCatalog.RolePermissions[actingMerchantUser.Role];
        var scopes = request.Scopes is { Length: > 0 } ? request.Scopes : grantablePermissions.ToArray();
        var invalidScope = scopes.FirstOrDefault(s => !grantablePermissions.Contains(s));
        if (invalidScope is not null)
        {
            throw new MerchantValidationException($"Scope '{invalidScope}' exceeds the creating user's own permissions.");
        }

        var (prefix, secret) = await GenerateUniqueCredentialAsync(cancellationToken);
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            OrganizationId = actingMerchantUser.OrganizationId,
            HashedKey = HashSecret(secret),
            Prefix = prefix,
            Scopes = scopes,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Set<ApiKey>().Add(apiKey);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(actingMerchantUser.OrganizationId, actingUserId, "merchant_api_key.created", "ApiKey", apiKey.Id.ToString()), cancellationToken);

        return new ApiKeyCreatedResponse(apiKey.Id, $"{prefix}.{secret}", prefix, scopes, apiKey.CreatedAt);
    }

    public async Task<List<ApiKeyResponse>> ListApiKeysAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var merchantId = user.GetMerchantId()
            ?? throw new MerchantValidationException("Acting merchant could not be resolved.");

        var keys = await _dbContext.Set<ApiKey>()
            .Where(k => k.MerchantId == merchantId)
            .ToListAsync(cancellationToken);
        return keys.Select(ToResponse).ToList();
    }

    public async Task<ApiKeyResponse?> RevokeApiKeyAsync(Guid id, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = actingUser.GetMerchantId()
            ?? throw new MerchantValidationException("Acting merchant could not be resolved.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new MerchantValidationException("Acting user could not be resolved.");

        var apiKey = await _dbContext.Set<ApiKey>()
            .FirstOrDefaultAsync(k => k.Id == id && k.MerchantId == merchantId, cancellationToken);
        if (apiKey is null)
        {
            return null;
        }

        apiKey.IsActive = false;
        apiKey.RevokedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(apiKey.OrganizationId, actingUserId, "merchant_api_key.revoked", "ApiKey", apiKey.Id.ToString()), cancellationToken);

        return ToResponse(apiKey);
    }

    // Deliberate IgnoreQueryFilters: Prefix uniqueness (ApiKeyConfiguration's unique index) is
    // global across all tenants, not per-org — a prefix collision check must see every org's
    // rows, not just the caller's, or two orgs could collide on the same prefix and break
    // ApiKeyAuthenticationHandler's (Task 5) O(1) lookup.
    private async Task<(string Prefix, string Secret)> GenerateUniqueCredentialAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var prefix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            var exists = await _dbContext.Set<ApiKey>().IgnoreQueryFilters().AnyAsync(k => k.Prefix == prefix, cancellationToken);
            if (!exists)
            {
                var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                return (prefix, secret);
            }
        }
        throw new MerchantValidationException("Could not generate a unique API key prefix. Please retry.");
    }

    private static string HashSecret(string secret) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static ApiKeyResponse ToResponse(ApiKey apiKey) => new(
        apiKey.Id, apiKey.Prefix, apiKey.Scopes, apiKey.IsActive, apiKey.CreatedAt, apiKey.RevokedAt, apiKey.LastUsedAt);
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj`
Expected: PASS (all 6 new tests).

- [ ] **Step 7: Register `ApiKeyService` in `Program.cs`**

In `src/Api/Program.cs`, next to the other Merchants service registrations (near line 126, after `MerchantKycService`):

```csharp
builder.Services.AddScoped<ApiKeyService>();
```

- [ ] **Step 8: Build and test the full solution**

Run: `dotnet build Courier.slnx`
Expected: Build succeeds.
Run: `dotnet test Courier.slnx`
Expected: All existing tests still pass; the 6 new `ApiKeyServiceTests` pass too.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Merchants/Application/MerchantPermissionCatalog.cs src/Modules/Merchants/Application/ApiKeyDtos.cs src/Modules/Merchants/Application/ApiKeyService.cs src/Api/Program.cs tests/Merchants.Tests/ApiKeyServiceTests.cs
git commit -m "feat(merchants): add ApiKeyService with create/list/revoke and scope validation"
```

---

### Task 4: `ApiKey` management endpoints (JWT-only, Owner-only)

**Files:**
- Create: `src/Modules/Merchants/Presentation/ApiKeyEndpoints.cs`
- Modify: `src/Api/Program.cs` (`app.MapApiKeyEndpoints();` next to the other merchant endpoint-group registrations, near line 210)
- Test: `tests/Api.Tests/ApiKeyEndpointsTests.cs`

**Interfaces:**
- Produces: `POST /api/v1/merchant/api-keys`, `GET /api/v1/merchant/api-keys`, `DELETE /api/v1/merchant/api-keys/{id}` — all gated `.RequireMerchantPermission(MerchantPermissionCatalog.ApiKeysManage)`, i.e. **merchant-JWT only** (that extension is JWT-only per its existing implementation — Task 5 does not touch it). This is the endpoint-level half of the self-escalation carve-out.
- Consumes: `ApiKeyService` (Task 3).

- [ ] **Step 1: Write the endpoints**

Create `src/Modules/Merchants/Presentation/ApiKeyEndpoints.cs`:

```csharp
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Merchants.Presentation;

public static class ApiKeyEndpoints
{
    public static void MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchant/api-keys").WithTags("Merchant API Keys");

        // JWT-only by construction: RequireMerchantPermission (unlike the new
        // RequireMerchantPermissionAnyCredential added in Task 5) only ever accepts the default
        // "Bearer" scheme. An API key can never reach these three routes, so it can never create,
        // list, or revoke another API key — the self-escalation carve-out from Global Constraints.
        group.MapPost("/", async (CreateApiKeyRequest request, ApiKeyService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CreateApiKeyAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/merchant/api-keys/{result.Id}", result);
            }
            catch (MerchantValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "MERCHANT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireMerchantPermission(MerchantPermissionCatalog.ApiKeysManage);

        group.MapGet("/", async (ApiKeyService service, HttpContext httpContext, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListApiKeysAsync(httpContext.User, cancellationToken)))
            .RequireMerchantPermission(MerchantPermissionCatalog.ApiKeysManage);

        group.MapDelete("/{id:guid}", async (Guid id, ApiKeyService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            var result = await service.RevokeApiKeyAsync(id, httpContext.User, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireMerchantPermission(MerchantPermissionCatalog.ApiKeysManage);
    }
}
```

- [ ] **Step 2: Register the endpoint group**

In `src/Api/Program.cs`, next to the other merchant endpoint-group registrations (near line 210, after `app.MapMerchantStaffEndpoints();`):

```csharp
app.MapApiKeyEndpoints();
```

- [ ] **Step 3: Write the API test**

Read `tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs` first to copy its exact registration/login helper calls (`RegisterOrgAsync`-equivalent for merchants, JSON client conventions) before writing this file, then create `tests/Api.Tests/ApiKeyEndpointsTests.cs` covering:

- Owner JWT: `POST /api/v1/merchant/api-keys` with no body scopes → `201`, response contains a `plaintextKey` formatted `{prefix}.{secret}`, and the prefix segment matches the returned `prefix` field.
- Owner JWT: `GET /api/v1/merchant/api-keys` after creating one → `200`, list contains the created key's `id`, and the response body (serialized as a string) never contains the substring `"hashedKey"` or `"HashedKey"` — proving the DTO shape, not just C# access, keeps the hash out of any response.
- Owner JWT: `DELETE /api/v1/merchant/api-keys/{id}` → `200`, `isActive: false`, `revokedAt` non-null.
- Staff JWT (not Owner): `POST /api/v1/merchant/api-keys` → `403` (Staff never has `MerchantPermissionCatalog.ApiKeysManage`).
- No auth header: `POST /api/v1/merchant/api-keys` → `401`.

- [ ] **Step 4: Run the test to verify it fails, then build and test the full solution**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter ApiKeyEndpointsTests`
Expected: FAIL (endpoints not registered yet — write this test after Steps 1-2 are in place per the task's own step order above is fine; if run strictly TDD-first, expect route-not-found failures before Step 1/2).
Run: `dotnet build Courier.slnx && dotnet test Courier.slnx`
Expected: Build succeeds; all existing tests plus the new `ApiKeyEndpointsTests` pass.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Merchants/Presentation/ApiKeyEndpoints.cs src/Api/Program.cs tests/Api.Tests/ApiKeyEndpointsTests.cs
git commit -m "feat(merchants): add JWT-only API key management endpoints"
```

---

### Task 5: `ApiKey` auth scheme + unified merchant permission check + Shipments/Webhooks multi-scheme

**Files:**
- Create: `src/SharedKernel/AuthSchemes.cs`
- Create: `src/Modules/Merchants/Infrastructure/ApiKeyAuthenticationHandler.cs`
- Modify: `src/Modules/Merchants/Infrastructure/MerchantAuthorizationChecker.cs` (add the `api_key_scopes` branch)
- Modify: `src/SharedKernel/RequireMerchantPermissionFilter.cs` (add `RequireMerchantPermissionAnyCredential` alongside the existing, untouched `RequireMerchantPermission`)
- Modify: `src/Api/Program.cs` (register the `"ApiKey"` scheme; add `Courier.Merchants.Domain`/`Microsoft.AspNetCore.Authentication` usings)
- Modify: `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs` (group-level `.RequireAuthorization()` → both schemes)
- Modify: `src/Modules/Webhooks/Presentation/WebhooksEndpoints.cs` (all 6 `.RequireMerchantPermission(...)` → `.RequireMerchantPermissionAnyCredential(...)`)
- Test: `tests/Merchants.Tests/MerchantAuthorizationCheckerTests.cs` (add 2 cases for the new branch)
- Test: `tests/Api.Tests/ApiKeyAuthenticationTests.cs`

**Interfaces:**
- Produces: `Courier.SharedKernel.AuthSchemes.{Bearer, ApiKey}` (string constants); `Courier.Merchants.Infrastructure.ApiKeyAuthenticationHandler` (scheme `"ApiKey"`); `Courier.SharedKernel.MerchantPermissionAnyCredentialEndpointExtensions.RequireMerchantPermissionAnyCredential(this RouteHandlerBuilder, string permission)`.
- Consumes: `ApiKey` entity (Task 2), existing `MerchantAuthorizationChecker`/`RequireMerchantPermissionFilter`, existing `ShipmentsEndpoints`/`WebhooksEndpoints`.

This is the task described in Global Constraints' self-escalation carve-out: `RequireMerchantPermission` (JWT-only, used by `ApiKeyEndpoints`, staff invite, KYC upload, profile update) is **not modified** — only a new, separate, explicitly opt-in extension is added, and only Shipments (already dual-branched) and Webhooks (this task) are switched onto multi-scheme auth. Cod (Task 11) and Pickups (Task 10) endpoints are written directly against the new extension when they're created, so they need no follow-up change here.

- [ ] **Step 1: Add the `AuthSchemes` constants**

Create `src/SharedKernel/AuthSchemes.cs`:

```csharp
namespace Courier.SharedKernel;

public static class AuthSchemes
{
    // Mirrors Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme's
    // value ("Bearer") without adding a JwtBearer package reference to SharedKernel, which has no
    // other ASP.NET Core auth dependency today.
    public const string Bearer = "Bearer";
    public const string ApiKey = "ApiKey";
}
```

- [ ] **Step 2: Write the failing `MerchantAuthorizationChecker` tests for the new branch**

In `tests/Merchants.Tests/MerchantAuthorizationCheckerTests.cs`, add a principal helper and two tests:

```csharp
    private static ClaimsPrincipal ApiKeyPrincipal(params string[] scopes) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", Guid.NewGuid().ToString()),
        new Claim("api_key_scopes", string.Join(' ', scopes))
    }, "test"));

    [Fact]
    public async Task HasPermissionAsync_ApiKeyPrincipalWithScope_ReturnsTrue()
    {
        await using var dbContext = CreateContext();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(ApiKeyPrincipal("shipment.create", "shipment.read"), "shipment.read");

        Assert.True(result);
    }

    [Fact]
    public async Task HasPermissionAsync_ApiKeyPrincipalWithoutScope_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(ApiKeyPrincipal("shipment.read"), MerchantPermissionCatalog.ApiKeysManage);

        Assert.False(result);
    }
```

Note these two tests need no seeded `MerchantUser`/`Merchant` row at all — that's the point of the branch (an API key isn't a user).

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj`
Expected: FAIL — both new tests currently fall through to the `MerchantUser` lookup path and return `false` for the wrong reason (no such user exists), so the "with scope → true" case fails while "without scope → false" would accidentally pass; treat both as red until the branch exists, don't rely on the accidental pass.

- [ ] **Step 4: Add the `api_key_scopes` branch to `MerchantAuthorizationChecker`**

In `src/Modules/Merchants/Infrastructure/MerchantAuthorizationChecker.cs`, add at the top of `HasPermissionAsync`, before the existing `GetUserId()` line:

```csharp
    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default)
    {
        // An ApiKey-authenticated principal (Task 5) carries its granted scopes directly on the
        // claim, not a MerchantUser row — checked here and nowhere else, so both credential types
        // share this one entry point.
        var scopesClaim = user.FindFirst("api_key_scopes")?.Value;
        if (scopesClaim is not null)
        {
            return scopesClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(permission);
        }

        var merchantUserId = user.GetUserId();
        // ... rest of the existing method body is unchanged from here down
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj`
Expected: PASS (all `MerchantAuthorizationCheckerTests`, including the 2 new ones).

- [ ] **Step 6: Implement `ApiKeyAuthenticationHandler`**

Create `src/Modules/Merchants/Infrastructure/ApiKeyAuthenticationHandler.cs`:

```csharp
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Courier.Merchants.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Courier.Merchants.Infrastructure;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly DbContext _dbContext;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        DbContext dbContext) : base(options, logger, encoder)
    {
        _dbContext = dbContext;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var raw = ExtractCredential(Request);
        if (raw is null)
        {
            return AuthenticateResult.NoResult();
        }

        var parts = raw.Split('.', 2);
        if (parts.Length != 2)
        {
            return AuthenticateResult.Fail("API_KEY_INVALID");
        }
        var (prefix, secret) = (parts[0], parts[1]);

        // No tenant context exists yet at this point in the pipeline (this handler runs before
        // AppDbContext's tenant filter has anything to key off) — IgnoreQueryFilters with an
        // explicit trusted predicate (unique Prefix) is the deliberate, documented exception from
        // Global Constraints, same reasoning as public tracking (Task 12).
        var apiKey = await _dbContext.Set<ApiKey>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.Prefix == prefix);
        if (apiKey is null || !apiKey.IsActive || apiKey.RevokedAt is not null)
        {
            return AuthenticateResult.Fail("API_KEY_REVOKED");
        }

        var hashedSecret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hashedSecret), Encoding.UTF8.GetBytes(apiKey.HashedKey)))
        {
            return AuthenticateResult.Fail("API_KEY_INVALID");
        }

        apiKey.LastUsedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Same claim types (sub, org_id, merchant_id) MerchantJwtTokenService.IssueAccessToken
        // already emits, so ClaimsPrincipalExtensions/HttpContextCurrentTenantProvider work
        // unchanged for either credential — api_key_scopes is the one addition, read only by
        // MerchantAuthorizationChecker's new branch (Step 4).
        var claims = new[]
        {
            new Claim("sub", apiKey.Id.ToString()),
            new Claim("org_id", apiKey.OrganizationId.ToString()),
            new Claim("merchant_id", apiKey.MerchantId.ToString()),
            new Claim("api_key_scopes", string.Join(' ', apiKey.Scopes))
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    private static string? ExtractCredential(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader) && !string.IsNullOrEmpty(apiKeyHeader))
        {
            return apiKeyHeader.ToString();
        }
        // A real JWT also arrives via "Authorization: Bearer <token>" and is picked up here too —
        // harmlessly. A JWT has 3 dot-separated segments, so Split('.', 2) below treats its
        // base64url header as "Prefix", which never matches a stored ApiKey.Prefix; this handler
        // then returns AuthenticateResult.Fail (not an exception), and the separately-registered
        // "Bearer" scheme authenticates the same header independently as a JWT.
        if (request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var value = authHeader.ToString();
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return value["Bearer ".Length..];
            }
        }
        return null;
    }
}
```

- [ ] **Step 7: Add the `RequireMerchantPermissionAnyCredential` extension**

In `src/SharedKernel/RequireMerchantPermissionFilter.cs`, append (leave everything above it untouched):

```csharp

public static class MerchantPermissionAnyCredentialEndpointExtensions
{
    // Explicitly opt-in, separate from RequireMerchantPermission (JWT-only) above — see
    // ApiKeyEndpoints.cs's comment on why key-management endpoints must NOT use this.
    public static RouteHandlerBuilder RequireMerchantPermissionAnyCredential(this RouteHandlerBuilder builder, string permission)
    {
        return builder
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Bearer, AuthSchemes.ApiKey)
                .RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true))
            .AddEndpointFilter(new RequireMerchantPermissionFilter(permission));
    }
}
```

- [ ] **Step 8: Register the `"ApiKey"` scheme in `Program.cs`**

Add `using Microsoft.AspNetCore.Authentication;` and `using Courier.Merchants.Infrastructure;` (the latter is likely already present via another Merchants using) near the top of `src/Api/Program.cs`. Change the `AddAuthentication` block:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // ... unchanged ...
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AuthSchemes.ApiKey, _ => { });
builder.Services.AddAuthorization();
```

- [ ] **Step 9: Switch Shipments to multi-scheme auth**

In `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs`, change:

```csharp
        var group = app.MapGroup("/api/v1/shipments").RequireAuthorization().WithTags("Shipments Management");
```

to:

```csharp
        // Multi-scheme so API-key merchant callers (Task 5) reach the same manual dual-auth
        // branching below as merchant JWT callers already did — per-route permission checks are
        // unchanged and still fail closed for any credential that resolves neither GetMerchantId()
        // nor GetUserId() (e.g. a staff-only route reached by an API key, which has no User row).
        var group = app.MapGroup("/api/v1/shipments")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Bearer, AuthSchemes.ApiKey)
                .RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true))
            .WithTags("Shipments Management");
```

- [ ] **Step 10: Switch Webhooks to `RequireMerchantPermissionAnyCredential`**

In `src/Modules/Webhooks/Presentation/WebhooksEndpoints.cs`, replace all 6 occurrences of `.RequireMerchantPermission(MerchantPermissionCatalog.WebhooksManage)` with `.RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.WebhooksManage)`. No other change needed — `ResolveCaller` already reads `GetMerchantId()`/`tenantProvider.OrganizationId` (`"org_id"` claim), both present on an API-key principal.

- [ ] **Step 11: Write the API-level auth tests**

Read `tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs` first to reuse its merchant-registration/login helpers, then create `tests/Api.Tests/ApiKeyAuthenticationTests.cs` covering:

- Create a merchant, log in as Owner, create an API key via `POST /api/v1/merchant/api-keys`, then use its `plaintextKey` as `X-Api-Key` to call `POST /api/v1/shipments` → `201` (proves the whole chain: handler → claims → `ShipmentsEndpoints`' existing merchant branch → `ShipmentService`).
- Same API key via `Authorization: Bearer {plaintextKey}` (not `X-Api-Key`) → `201` too (both header forms work).
- Revoke the key, retry the same call → `401`.
- A garbage `X-Api-Key: not-a-real-key` → `401`.
- Valid prefix, wrong secret → `401`.
- API key created with a `Scopes` array that excludes `shipment.create` → `POST /api/v1/shipments` with that key → `403`.
- The API key from the Webhooks-scope test can reach `GET /api/v1/webhooks` when its scopes include `merchant.webhooks.manage`, and gets `403` when they don't.

- [ ] **Step 12: Run the new test, then build and test the full solution**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter ApiKeyAuthenticationTests`
Expected: PASS once Steps 1-10 are all in place.
Run: `dotnet build Courier.slnx && dotnet test Courier.slnx`
Expected: Build succeeds; every existing Shipments/Webhooks/Merchants test (staff-JWT and merchant-JWT paths) still passes unmodified — this step specifically guards against a regression in the existing manual dual-auth branches.

- [ ] **Step 13: Commit**

```bash
git add src/SharedKernel/AuthSchemes.cs src/SharedKernel/RequireMerchantPermissionFilter.cs src/Modules/Merchants/Infrastructure/ApiKeyAuthenticationHandler.cs src/Modules/Merchants/Infrastructure/MerchantAuthorizationChecker.cs src/Api/Program.cs src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs src/Modules/Webhooks/Presentation/WebhooksEndpoints.cs tests/Merchants.Tests/MerchantAuthorizationCheckerTests.cs tests/Api.Tests/ApiKeyAuthenticationTests.cs
git commit -m "feat(auth): add ApiKey authentication scheme and wire it into Shipments and Webhooks"
```

---

### Task 6: Rate limiting — `ApiKeyRateLimitFilter` and `IpRateLimitFilter`

**Files:**
- Create: `src/SharedKernel/RateLimitFilters.cs`
- Modify: `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs` (chain `.RequireApiKeyRateLimit()` onto the group)
- Modify: `src/Modules/Webhooks/Presentation/WebhooksEndpoints.cs` (chain `.RequireApiKeyRateLimit()` onto the group)
- Modify: `src/Api/appsettings.Development.json` (add a `RateLimiting` section)
- Test: `tests/Api.Tests/RateLimitingTests.cs`

**Interfaces:**
- Produces: `Courier.SharedKernel.RateLimitEndpointExtensions.RequireApiKeyRateLimit(this RouteGroupBuilder)` and `.RequireIpRateLimit(this RouteHandlerBuilder)`.
- Consumes: `IRateLimiter` (Task 1).

Applied as reusable endpoint filters, not app-wide middleware, per spec §6 — "wrapping only the Merchant API group and the public tracking endpoint." `ApiKeyRateLimitFilter` self-selects: it only enforces a limit when the caller authenticated via the `"ApiKey"` scheme (presence of the `api_key_scopes` claim); a staff or merchant JWT caller reaching the same group (e.g. Shipments, which both credential types already share) passes through unlimited, same self-selecting shape as the idempotency middleware (Task 7) and request logging (Task 13). Cod (Task 11) and Pickups (Task 10) groups apply `.RequireApiKeyRateLimit()` directly when they're created — no follow-up needed here. Public tracking (Task 12) applies `.RequireIpRateLimit()` directly on its single route when it's created.

- [ ] **Step 1: Implement the filters and extensions**

Create `src/SharedKernel/RateLimitFilters.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Courier.SharedKernel;

// Only enforces a limit for an ApiKey-authenticated caller (presence of the api_key_scopes
// claim, set only by ApiKeyAuthenticationHandler, Task 5) — a staff or merchant JWT caller
// reaching the same route group passes through unlimited. Partitioned per ApiKey.Id, so one
// merchant's key exhausting its quota never affects another key or a JWT caller.
public class ApiKeyRateLimitFilter : IEndpointFilter
{
    private readonly string _configKey;
    private readonly int _defaultLimit;
    private readonly TimeSpan _window;

    public ApiKeyRateLimitFilter(string configKey, int defaultLimit, TimeSpan window)
    {
        _configKey = configKey;
        _defaultLimit = defaultLimit;
        _window = window;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (httpContext.User.FindFirst("api_key_scopes") is null)
        {
            return await next(context);
        }
        var apiKeyId = httpContext.User.GetUserId();
        if (apiKeyId is null)
        {
            return await next(context);
        }

        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var limit = configuration.GetValue<int?>(_configKey) ?? _defaultLimit;
        var limiter = httpContext.RequestServices.GetRequiredService<IRateLimiter>();
        var result = await limiter.TryAcquireAsync($"apikey:{apiKeyId}", limit, _window, httpContext.RequestAborted);
        if (!result.Allowed)
        {
            return RateLimitExceededResult(httpContext, result.RetryAfter);
        }

        return await next(context);
    }
}

// Partitioned per client IP — the only credential-free signal available on an anonymous
// endpoint (public tracking, Task 12).
public class IpRateLimitFilter : IEndpointFilter
{
    private readonly string _configKey;
    private readonly int _defaultLimit;
    private readonly TimeSpan _window;

    public IpRateLimitFilter(string configKey, int defaultLimit, TimeSpan window)
    {
        _configKey = configKey;
        _defaultLimit = defaultLimit;
        _window = window;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var limit = configuration.GetValue<int?>(_configKey) ?? _defaultLimit;
        var limiter = httpContext.RequestServices.GetRequiredService<IRateLimiter>();
        var result = await limiter.TryAcquireAsync($"ip:{ip}", limit, _window, httpContext.RequestAborted);
        if (!result.Allowed)
        {
            return RateLimitExceededResult(httpContext, result.RetryAfter);
        }

        return await next(context);
    }
}

internal static class RateLimitResultHelper
{
    public static IResult RateLimitExceededResult(HttpContext httpContext, TimeSpan retryAfter)
    {
        httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        return Results.Json(
            new { error = new { code = "RATE_LIMIT_EXCEEDED", message = "Too many requests." } },
            statusCode: StatusCodes.Status429TooManyRequests);
    }
}

public static class RateLimitEndpointExtensions
{
    public static RouteGroupBuilder RequireApiKeyRateLimit(this RouteGroupBuilder builder)
        => builder.AddEndpointFilter(new ApiKeyRateLimitFilter("RateLimiting:ApiKeyRequestsPerMinute", 60, TimeSpan.FromMinutes(1)));

    public static RouteHandlerBuilder RequireIpRateLimit(this RouteHandlerBuilder builder)
        => builder.AddEndpointFilter(new IpRateLimitFilter("RateLimiting:PublicTrackingRequestsPerMinute", 30, TimeSpan.FromMinutes(1)));
}
```

Fix the two filters' calls to reference `RateLimitResultHelper.RateLimitExceededResult(...)` explicitly (both live in the same file, static helper class kept separate from the two filter classes for clarity).

- [ ] **Step 2: Wire the filter into Shipments and Webhooks**

In `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs`, chain onto the group from Task 5's Step 9:

```csharp
        var group = app.MapGroup("/api/v1/shipments")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Bearer, AuthSchemes.ApiKey)
                .RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true))
            .RequireApiKeyRateLimit()
            .WithTags("Shipments Management");
```

In `src/Modules/Webhooks/Presentation/WebhooksEndpoints.cs`:

```csharp
        var group = app.MapGroup("/api/v1/webhooks").RequireApiKeyRateLimit().WithTags("Webhooks");
```

- [ ] **Step 3: Add the config section**

In `src/Api/appsettings.Development.json`, add (documenting the defaults already hardcoded in the filters — not required for them to function):

```json
  "RateLimiting": {
    "ApiKeyRequestsPerMinute": 60,
    "PublicTrackingRequestsPerMinute": 30
  },
```

- [ ] **Step 4: Write the rate limiting test**

Read `tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs` and this task's Task 5 `ApiKeyAuthenticationTests.cs` first (for the merchant-registration and API-key-creation helpers), then create `tests/Api.Tests/RateLimitingTests.cs`:

- Build a scoped `WebApplicationFactory<Program>` via `_factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:ApiKeyRequestsPerMinute"] = "2" })))` so the test doesn't need 60 real requests.
- Create a merchant, an API key, then call `GET /api/v1/shipments` 3 times with that key on the scoped factory's client: first two `200`, third `429` with a `Retry-After` header present and the response body containing `RATE_LIMIT_EXCEEDED`.
- A staff-JWT caller (not API-key) making the same number of calls on the same overridden-config factory is never rate-limited (proves the self-selecting `api_key_scopes`-presence check works, mirroring the idempotency staff-exemption test planned for Task 7).

- [ ] **Step 5: Run the test, then build and test the full solution**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter RateLimitingTests`
Expected: PASS.
Run: `dotnet build Courier.slnx && dotnet test Courier.slnx`
Expected: Build succeeds; all existing tests (including every Shipments/Webhooks test, none of which authenticate via API key and so never trip the new limiter) still pass.

- [ ] **Step 6: Commit**

```bash
git add src/SharedKernel/RateLimitFilters.cs src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs src/Modules/Webhooks/Presentation/WebhooksEndpoints.cs src/Api/appsettings.Development.json tests/Api.Tests/RateLimitingTests.cs
git commit -m "feat(rate-limiting): add Redis-backed per-API-key and per-IP rate limit filters"
```

---

### Task 7: Idempotency middleware for merchant-track write routes

**Files:**
- Create: `src/Api/Middleware/IdempotencyMiddleware.cs`
- Modify: `src/Api/Program.cs` (`app.UseMiddleware<Courier.Api.Middleware.IdempotencyMiddleware>();` right after `app.UseAuthorization();`)
- Test: `tests/Api.Tests/IdempotencyTests.cs`

**Interfaces:**
- Produces: `Courier.Api.Middleware.IdempotencyMiddleware`.
- Consumes: `IIdempotencyStore` (Task 1).

Must be **middleware**, not an `IEndpointFilter` — minimal-API endpoint filters run after model binding has already consumed the request body stream, but this middleware needs the raw pre-binding bytes to hash. Placed after `UseAuthorization()` so `context.User` already reflects either JWT or ApiKey claims (multi-scheme policy evaluation inside `UseAuthorization()` populates the merged principal as a side effect) but before any endpoint's model binder runs. Self-selects on **route** (allow-listed by path+method, not by which module owns it — `POST /pickups` doesn't exist until Task 10, but this middleware's path check already covers it so no follow-up change is needed there) and on **caller kind** (`GetMerchantId() is not null` — staff JWT and unauthenticated callers on the same three routes pass straight through, per Global Constraints).

- [ ] **Step 1: Implement the middleware**

Create `src/Api/Middleware/IdempotencyMiddleware.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Courier.SharedKernel;

namespace Courier.Api.Middleware;

public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly Regex ShipmentCancelPattern = new(@"^/api/v1/shipments/[0-9a-fA-F-]{36}/cancel$", RegexOptions.Compiled);

    public IdempotencyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IIdempotencyStore idempotencyStore)
    {
        if (!IsIdempotentRoute(context.Request))
        {
            await _next(context);
            return;
        }

        var merchantId = context.User.GetMerchantId();
        if (merchantId is null)
        {
            // Staff-JWT (or unauthenticated) callers on these same routes are completely
            // untouched — Global Constraints' explicit scope decision, avoids retrofitting every
            // existing staff-facing test from prior phases.
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keyHeader) || string.IsNullOrWhiteSpace(keyHeader))
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key header is required for this request.");
            return;
        }
        var idempotencyKey = keyHeader.ToString();

        context.Request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync();
        }
        context.Request.Body.Position = 0;
        var bodyHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody)));

        var storeKey = $"{merchantId}:{idempotencyKey}";
        var existing = await idempotencyStore.GetAsync(storeKey, context.RequestAborted);
        if (existing is not null)
        {
            if (existing.RequestBodyHash != bodyHash)
            {
                await WriteErrorAsync(context, StatusCodes.Status409Conflict, "IDEMPOTENCY_KEY_REUSED", "This Idempotency-Key was already used with a different request body.");
                return;
            }

            context.Response.StatusCode = existing.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(existing.ResponseBody);
            return;
        }

        var originalBody = context.Response.Body;
        await using var capturedBody = new MemoryStream();
        context.Response.Body = capturedBody;

        await _next(context);

        capturedBody.Position = 0;
        var responseText = await new StreamReader(capturedBody).ReadToEndAsync();
        capturedBody.Position = 0;
        await capturedBody.CopyToAsync(originalBody);
        context.Response.Body = originalBody;

        await idempotencyStore.StoreAsync(storeKey, new IdempotencyRecord(bodyHash, context.Response.StatusCode, responseText), TimeSpan.FromHours(24), context.RequestAborted);
    }

    private static bool IsIdempotentRoute(HttpRequest request)
    {
        if (request.Method != HttpMethods.Post)
        {
            return false;
        }
        var path = request.Path.Value ?? string.Empty;
        return path == "/api/v1/shipments" || path == "/api/v1/pickups" || ShipmentCancelPattern.IsMatch(path);
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string code, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = new { code, message } }));
    }
}
```

- [ ] **Step 2: Register the middleware in `Program.cs`**

Right after `app.UseAuthorization();` (before any `app.MapXxxEndpoints();` call):

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<Courier.Api.Middleware.IdempotencyMiddleware>();
```

- [ ] **Step 3: Write the idempotency tests**

Read `tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs` and `tests/Api.Tests/ShipmentsEndpointsTests.cs` first (for merchant registration/login and the shipment-request-seeding helpers), then create `tests/Api.Tests/IdempotencyTests.cs` covering:

- Merchant JWT, `POST /api/v1/shipments` with `Idempotency-Key: k1` twice, identical body → first `201`; second call returns the **exact same response body** (same shipment `Id`/`Awb`) with no second shipment actually created (assert via `GET /api/v1/shipments` count).
- Merchant JWT, same `Idempotency-Key: k1` reused with a **different** body → `409 IDEMPOTENCY_KEY_REUSED`.
- Merchant JWT, `POST /api/v1/shipments` with **no** `Idempotency-Key` header → `400 IDEMPOTENCY_KEY_REQUIRED`.
- Staff JWT (not merchant), `POST /api/v1/shipments` with no `Idempotency-Key` header → succeeds unmodified (`201`, same as every existing staff-facing shipment test) — proving the merchant-only scoping decision didn't regress staff flows.
- API-key caller (from Task 5's fixtures), same `Idempotency-Key` reuse behavior as the merchant-JWT case — proving the middleware's `GetMerchantId()` check (not "is this specifically a JWT") is what gates it.

- [ ] **Step 4: Run the tests, then build and test the full solution**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter IdempotencyTests`
Expected: PASS.
Run: `dotnet build Courier.slnx && dotnet test Courier.slnx`
Expected: Build succeeds; every existing staff-facing `ShipmentsEndpointsTests` case (none of which send an `Idempotency-Key` header) still passes unmodified — this is the regression guard the merchant-only scoping decision depends on.

- [ ] **Step 5: Commit**

```bash
git add src/Api/Middleware/IdempotencyMiddleware.cs src/Api/Program.cs tests/Api.Tests/IdempotencyTests.cs
git commit -m "feat(idempotency): add Redis-backed Idempotency-Key middleware for merchant-track write routes"
```

---

### Task 8: `PickupRequest` domain entity, EF configuration, migration, and new permissions

**Files:**
- Create: `src/Modules/Shipments/Domain/PickupRequestStatus.cs`
- Create: `src/Modules/Shipments/Domain/PickupRequest.cs`
- Create: `src/Modules/Shipments/Infrastructure/PickupRequestConfiguration.cs`
- Modify: `src/Persistence/AppDbContext.cs` (`DbSet<PickupRequest>`, `ApplyConfiguration`, FK, tenant `HasQueryFilter`)
- Modify: `src/Modules/Identity/Application/PermissionCatalog.cs` (add `pickup.read`)
- Modify: `src/Modules/Merchants/Application/MerchantPermissionCatalog.cs` (add `PickupsCreate`, `PickupsRead`, `PickupsCancel`, Owner + Staff)
- Migration: `src/Persistence/Migrations/*_AddPickupRequestsAndPickupReadPermission.cs` (generated — one migration covers both the new table and the new `HasData`-seeded permission row, per Global Constraints' "immediate migration" rule)

**Interfaces:**
- Produces: `Courier.Shipments.Domain.PickupRequest` with fields `Id, OrganizationId, MerchantId, BranchId, Guid[] ShipmentIds, RequestedForDate (DateOnly), Notes (string?), Status (PickupRequestStatus), CreatedAt, UpdatedAt`; `PickupRequestStatus { Requested, Scheduled, Completed, Cancelled }`.
- Consumes: nothing new — FKs to `Organization`/`Branch` only (no FK to `Shipment` for `ShipmentIds[]`, same cross-module precedent as `Shipment.MerchantId` — validated at the Application layer in Task 9, not the EF model).

No dedicated unit test this task, same reasoning as Task 2 — a plain data-holder entity gets behavioral coverage from `PickupRequestService`'s own tests (Task 9).

- [ ] **Step 1: Create `PickupRequestStatus` and `PickupRequest`**

Create `src/Modules/Shipments/Domain/PickupRequestStatus.cs`:

```csharp
namespace Courier.Shipments.Domain;

public enum PickupRequestStatus
{
    Requested,
    Scheduled,
    Completed,
    Cancelled
}
```

Create `src/Modules/Shipments/Domain/PickupRequest.cs`:

```csharp
namespace Courier.Shipments.Domain;

// BranchId is resolved from the requested shipments' own OriginBranchId at creation
// (PickupRequestService, Task 9) — every ShipmentId in one request must share one
// OriginBranchId, so this column is a validated denormalization, not caller-supplied input.
// No rider/DeliveryRun linkage yet (spec §3 Non-Goals) — Status only ever reaches Cancelled
// via the merchant this phase; Scheduled/Completed are modeled now so the column exists, but
// nothing transitions a row into them until the Phase 8 DeliveryRun wiring lands.
public class PickupRequest
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid MerchantId { get; set; }
    public Guid BranchId { get; set; }
    public required Guid[] ShipmentIds { get; set; }
    public DateOnly RequestedForDate { get; set; }
    public string? Notes { get; set; }
    public PickupRequestStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

Create `src/Modules/Shipments/Infrastructure/PickupRequestConfiguration.cs`:

```csharp
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Shipments.Infrastructure;

public class PickupRequestConfiguration : IEntityTypeConfiguration<PickupRequest>
{
    public void Configure(EntityTypeBuilder<PickupRequest> builder)
    {
        builder.ToTable("PickupRequests");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        // Guid[] maps to a native Postgres uuid[] column, same as WebhookSubscription.Events'
        // string[] -> text[] — no HasColumnType needed, Npgsql's EF Core provider handles both.

        builder.HasIndex(p => p.OrganizationId);
        builder.HasIndex(p => p.MerchantId);
        builder.HasIndex(p => p.BranchId);
    }
}
```

- [ ] **Step 3: Register `PickupRequest` in `AppDbContext`**

In `src/Persistence/AppDbContext.cs`, following the exact same three-spot pattern as Task 2's `ApiKey` (DbSet near the other Shipments entities, `ApplyConfiguration` next to `ShipmentItemConfiguration`, FK-to-`Organization` and FK-to-`Branch` registrations, tenant `HasQueryFilter` next to `Shipment`'s):

```csharp
    public DbSet<PickupRequest> PickupRequests => Set<PickupRequest>();
```

```csharp
        modelBuilder.ApplyConfiguration(new PickupRequestConfiguration());
```

```csharp
        modelBuilder.Entity<PickupRequest>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(p => p.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PickupRequest>().HasOne<Branch>()
            .WithMany()
            .HasForeignKey(p => p.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
```

```csharp
        modelBuilder.Entity<PickupRequest>().HasQueryFilter(p => _currentTenantProvider.OrganizationId != null && p.OrganizationId == _currentTenantProvider.OrganizationId);
```

- [ ] **Step 4: Add the `pickup.read` staff permission**

In `src/Modules/Identity/Application/PermissionCatalog.cs`, add to `All`, next to the manifest entries:

```csharp
        ("pickup.read", "Read merchant pickup requests"),
```

`OrganizationOwner` needs no separate role-seeding change — `AuthService.RegisterOrganizationAsync` already assigns every seeded permission to that role automatically (confirmed in prior exploration).

- [ ] **Step 5: Add the three merchant pickup permissions**

In `src/Modules/Merchants/Application/MerchantPermissionCatalog.cs`, add next to the shipment constants:

```csharp
    public const string PickupsCreate = "merchant.pickups.create";
    public const string PickupsRead = "merchant.pickups.read";
    public const string PickupsCancel = "merchant.pickups.cancel";
```

Add all three to both `OwnerPermissions` and `StaffPermissions` — same tier as the existing `ShipmentCreate/Read/Cancel` staff already holds:

```csharp
    private static readonly string[] OwnerPermissions =
    {
        ProfileRead, ProfileUpdate, StaffManage, KycUpload, ShipmentCreate, ShipmentRead, ShipmentCancel, WebhooksManage, ApiKeysManage, PickupsCreate, PickupsRead, PickupsCancel
    };

    private static readonly string[] StaffPermissions =
    {
        ProfileRead, KycUpload, ShipmentCreate, ShipmentRead, ShipmentCancel, PickupsCreate, PickupsRead, PickupsCancel
    };
```

- [ ] **Step 6: Generate and inspect the migration**

Run: `dotnet ef migrations add AddPickupRequestsAndPickupReadPermission --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`

Read the generated migration file. Expected: a new `PickupRequests` table (columns matching the entity, `uuid[]` for `ShipmentIds`, indexes on `OrganizationId`/`MerchantId`/`BranchId`, FKs to `Organizations`/`Branches`) **and** one new `HasData` row insert into the `Permissions` table for `pickup.read` — both in this single migration. No unrelated table should appear in the diff.

- [ ] **Step 7: Build and test the full solution**

Run: `dotnet build Courier.slnx`
Expected: Build succeeds.
Run: `dotnet test Courier.slnx`
Expected: All existing tests still pass (the new table/permission have no consumers yet).

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Shipments/Domain/PickupRequest.cs src/Modules/Shipments/Domain/PickupRequestStatus.cs src/Modules/Shipments/Infrastructure/PickupRequestConfiguration.cs src/Persistence/AppDbContext.cs src/Modules/Identity/Application/PermissionCatalog.cs src/Modules/Merchants/Application/MerchantPermissionCatalog.cs src/Persistence/Migrations
git commit -m "feat(shipments): add PickupRequest entity, pickup.read permission, and migration"
```

---

### Task 9: `PickupRequestService` (Create/List/Cancel) + DTOs + unit tests

**Files:**
- Create: `src/Modules/Shipments/Application/PickupRequestDtos.cs`
- Create: `src/Modules/Shipments/Application/PickupRequestService.cs`
- Modify: `src/Api/Program.cs` (`builder.Services.AddScoped<PickupRequestService>();` next to `ShipmentService`)
- Test: `tests/Shipments.Tests/PickupRequestServiceTests.cs`

**Interfaces:**
- Produces: `Courier.Shipments.Application.PickupRequestService.CreatePickupRequestAsync(CreatePickupRequestRequest, ClaimsPrincipal actingUser, CancellationToken) : Task<PickupRequestResponse>`, `.ListAsync(ClaimsPrincipal actingUser, CancellationToken) : Task<List<PickupRequestResponse>>`, `.CancelAsync(Guid id, ClaimsPrincipal actingUser, CancellationToken) : Task<PickupRequestResponse?>`.
- Consumes: `PickupRequest` (Task 8), existing `Shipment`, `IBranchAuthorizationChecker`, reuses the existing `Courier.Shipments.Application.ShipmentValidationException` (no new exception type).

`ListAsync` mirrors `ShipmentService.Reads.cs`'s `ListAsync` exactly: merchant-track callers (`GetMerchantId() is not null`) see only their own rows; staff callers are scoped through `IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "pickup.read", ct)` with the same "`null` means no filter, non-null means restrict" contract.

- [ ] **Step 1: Write the DTOs**

Create `src/Modules/Shipments/Application/PickupRequestDtos.cs`:

```csharp
using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

public record CreatePickupRequestRequest(Guid[] ShipmentIds, DateOnly RequestedForDate, string? Notes);

public record PickupRequestResponse(Guid Id, Guid MerchantId, Guid BranchId, Guid[] ShipmentIds, DateOnly RequestedForDate, string? Notes, PickupRequestStatus Status, DateTime CreatedAt, DateTime UpdatedAt);
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Shipments.Tests/PickupRequestServiceTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Shipments.Domain;
using Courier.Shipments.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Shipments.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class RestrictedBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid> _accessibleBranchIds;
    public RestrictedBranchesAuthorizationChecker(IReadOnlySet<Guid> accessibleBranchIds) => _accessibleBranchIds = accessibleBranchIds;
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(_accessibleBranchIds);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessibleBranchIds.Contains(branchId));
}

public class PickupRequestServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ShipmentConfiguration());
            modelBuilder.ApplyConfiguration(new PickupRequestConfiguration());
        }
    }

    private static TestDbContext CreateContext() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static ClaimsPrincipal StaffUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));
    private static ClaimsPrincipal MerchantUser(Guid merchantId) => new(new ClaimsIdentity(new[] { new Claim("merchant_id", merchantId.ToString()) }, "test"));

    // Inserted directly (bypassing ShipmentService) — this test only needs existing Shipment
    // rows with a known MerchantId/OriginBranchId, not full booking/pricing validation.
    private static Shipment NewShipment(Guid merchantId, Guid originBranchId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        Awb = $"AWB-{Guid.NewGuid():N}"[..12],
        MerchantId = merchantId,
        OriginBranchId = originBranchId,
        DestinationBranchId = Guid.NewGuid(),
        CurrentStatus = ShipmentStatus.Created,
        ServiceTypeId = Guid.NewGuid(),
        WeightKg = 1m,
        RecipientName = "Recipient", RecipientPhone = "9800000000",
        RecipientProvinceId = Guid.NewGuid(), RecipientDistrictId = Guid.NewGuid(), RecipientLocalLevelId = Guid.NewGuid(), RecipientWardNumber = 1, RecipientAddressLine = "Addr",
        SenderName = "Sender", SenderPhone = "9800000001",
        SenderProvinceId = Guid.NewGuid(), SenderDistrictId = Guid.NewGuid(), SenderLocalLevelId = Guid.NewGuid(), SenderWardNumber = 1, SenderAddressLine = "Addr"
    };

    private static PickupRequestService CreateService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker? branchAuthorizationChecker = null) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchAuthorizationChecker ?? new AllBranchesAuthorizationChecker());

    [Fact]
    public async Task CreatePickupRequestAsync_ShipmentsShareOriginBranch_CreatesRequestForThatBranch()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var shipmentA = NewShipment(merchantId, originBranchId);
        var shipmentB = NewShipment(merchantId, originBranchId);
        dbContext.Set<Shipment>().AddRange(shipmentA, shipmentB);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId);

        var result = await service.CreatePickupRequestAsync(
            new CreatePickupRequestRequest(new[] { shipmentA.Id, shipmentB.Id }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), "Ring the bell"),
            MerchantUser(merchantId));

        Assert.Equal(originBranchId, result.BranchId);
        Assert.Equal(PickupRequestStatus.Requested, result.Status);
    }

    [Fact]
    public async Task CreatePickupRequestAsync_ShipmentBelongsToAnotherMerchant_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var foreignShipment = NewShipment(otherMerchantId, originBranchId);
        dbContext.Set<Shipment>().Add(foreignShipment);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { foreignShipment.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(merchantId)));
    }

    [Fact]
    public async Task CreatePickupRequestAsync_ShipmentsWithDifferentOriginBranches_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var shipmentA = NewShipment(merchantId, Guid.NewGuid());
        var shipmentB = NewShipment(merchantId, Guid.NewGuid());
        dbContext.Set<Shipment>().AddRange(shipmentA, shipmentB);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { shipmentA.Id, shipmentB.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(merchantId)));
    }

    [Fact]
    public async Task ListAsync_MerchantCaller_OnlySeesOwnRequests()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var mine = NewShipment(merchantId, branchId);
        var theirs = NewShipment(otherMerchantId, branchId);
        dbContext.Set<Shipment>().AddRange(mine, theirs);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId);
        await service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { mine.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(merchantId));
        await service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { theirs.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(otherMerchantId));

        var result = await service.ListAsync(MerchantUser(merchantId));

        Assert.All(result, p => Assert.Equal(merchantId, p.MerchantId));
    }

    [Fact]
    public async Task ListAsync_StaffCallerRestrictedToOneBranch_OnlySeesRequestsForThatBranch()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var inScopeBranch = Guid.NewGuid();
        var outOfScopeBranch = Guid.NewGuid();
        var inScopeShipment = NewShipment(Guid.NewGuid(), inScopeBranch);
        var outOfScopeShipment = NewShipment(Guid.NewGuid(), outOfScopeBranch);
        dbContext.Set<Shipment>().AddRange(inScopeShipment, outOfScopeShipment);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId);
        var inScopeRequest = await service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { inScopeShipment.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(inScopeShipment.MerchantId));
        await service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { outOfScopeShipment.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(outOfScopeShipment.MerchantId));
        var restrictedService = CreateService(dbContext, organizationId, new RestrictedBranchesAuthorizationChecker(new HashSet<Guid> { inScopeBranch }));

        var result = await restrictedService.ListAsync(StaffUser(Guid.NewGuid()));

        var resultIds = result.Select(p => p.Id).ToHashSet();
        Assert.Contains(inScopeRequest.Id, resultIds);
        Assert.Single(result);
    }

    [Fact]
    public async Task CancelAsync_OwnRequest_SetsCancelledStatus()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var shipment = NewShipment(merchantId, Guid.NewGuid());
        dbContext.Set<Shipment>().Add(shipment);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, Guid.NewGuid());
        var created = await service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { shipment.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(merchantId));

        var result = await service.CancelAsync(created.Id, MerchantUser(merchantId));

        Assert.NotNull(result);
        Assert.Equal(PickupRequestStatus.Cancelled, result!.Status);
    }

    [Fact]
    public async Task CancelAsync_AlreadyCancelled_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var shipment = NewShipment(merchantId, Guid.NewGuid());
        dbContext.Set<Shipment>().Add(shipment);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, Guid.NewGuid());
        var created = await service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { shipment.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(merchantId));
        await service.CancelAsync(created.Id, MerchantUser(merchantId));

        await Assert.ThrowsAsync<ShipmentValidationException>(() => service.CancelAsync(created.Id, MerchantUser(merchantId)));
    }

    [Fact]
    public async Task CancelAsync_BelongsToAnotherMerchant_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var shipment = NewShipment(merchantId, Guid.NewGuid());
        dbContext.Set<Shipment>().Add(shipment);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, Guid.NewGuid());
        var created = await service.CreatePickupRequestAsync(new CreatePickupRequestRequest(new[] { shipment.Id }, DateOnly.FromDateTime(DateTime.UtcNow), null), MerchantUser(merchantId));

        var result = await service.CancelAsync(created.Id, MerchantUser(otherMerchantId));

        Assert.Null(result);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj`
Expected: FAIL to compile — `PickupRequestService` doesn't exist yet.

- [ ] **Step 4: Implement `PickupRequestService`**

Create `src/Modules/Shipments/Application/PickupRequestService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public class PickupRequestService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public PickupRequestService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger, IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<PickupRequestResponse> CreatePickupRequestAsync(CreatePickupRequestRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = actingUser.GetMerchantId()
            ?? throw new ShipmentValidationException("Acting merchant could not be resolved.");
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("Acting organization could not be resolved.");

        if (request.ShipmentIds is not { Length: > 0 })
        {
            throw new ShipmentValidationException("At least one shipment id is required.");
        }

        var distinctIds = request.ShipmentIds.Distinct().ToArray();
        var shipments = await _dbContext.Set<Shipment>()
            .Where(s => distinctIds.Contains(s.Id) && s.MerchantId == merchantId)
            .ToListAsync(cancellationToken);
        if (shipments.Count != distinctIds.Length)
        {
            throw new ShipmentValidationException("One or more shipment ids were not found for this merchant.");
        }

        var originBranchIds = shipments.Select(s => s.OriginBranchId).Distinct().ToList();
        if (originBranchIds.Count != 1)
        {
            throw new ShipmentValidationException("All shipments in one pickup request must share the same origin branch.");
        }

        var pickupRequest = new PickupRequest
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            MerchantId = merchantId,
            BranchId = originBranchIds[0],
            ShipmentIds = distinctIds,
            RequestedForDate = request.RequestedForDate,
            Notes = request.Notes,
            Status = PickupRequestStatus.Requested,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.Set<PickupRequest>().Add(pickupRequest);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "pickup_request.created", "PickupRequest", pickupRequest.Id.ToString()), cancellationToken);

        return ToResponse(pickupRequest);
    }

    public async Task<List<PickupRequestResponse>> ListAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<PickupRequest>().AsQueryable();
        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            query = query.Where(p => p.MerchantId == callerMerchantId);
        }
        else
        {
            var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "pickup.read", cancellationToken);
            if (accessibleBranchIds is not null)
            {
                var accessibleBranchIdsArray = accessibleBranchIds.ToArray();
                query = query.Where(p => accessibleBranchIdsArray.Contains(p.BranchId));
            }
        }

        var results = await query.ToListAsync(cancellationToken);
        return results.Select(ToResponse).ToList();
    }

    public async Task<PickupRequestResponse?> CancelAsync(Guid id, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = actingUser.GetMerchantId()
            ?? throw new ShipmentValidationException("Acting merchant could not be resolved.");

        var pickupRequest = await _dbContext.Set<PickupRequest>()
            .FirstOrDefaultAsync(p => p.Id == id && p.MerchantId == merchantId, cancellationToken);
        if (pickupRequest is null)
        {
            return null;
        }
        if (pickupRequest.Status is PickupRequestStatus.Completed or PickupRequestStatus.Cancelled)
        {
            throw new ShipmentValidationException($"Pickup request cannot be cancelled from status '{pickupRequest.Status}'.");
        }

        pickupRequest.Status = PickupRequestStatus.Cancelled;
        pickupRequest.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(pickupRequest.OrganizationId, actingUser.GetUserId(), "pickup_request.cancelled", "PickupRequest", pickupRequest.Id.ToString()), cancellationToken);

        return ToResponse(pickupRequest);
    }

    private static PickupRequestResponse ToResponse(PickupRequest p) => new(
        p.Id, p.MerchantId, p.BranchId, p.ShipmentIds, p.RequestedForDate, p.Notes, p.Status, p.CreatedAt, p.UpdatedAt);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj`
Expected: PASS (all 8 new tests).

- [ ] **Step 6: Register `PickupRequestService` in `Program.cs`**

Next to `builder.Services.AddScoped<ShipmentService>();`:

```csharp
builder.Services.AddScoped<PickupRequestService>();
```

- [ ] **Step 7: Build and test the full solution**

Run: `dotnet build Courier.slnx`
Expected: Build succeeds.
Run: `dotnet test Courier.slnx`
Expected: All existing tests still pass; the 8 new `PickupRequestServiceTests` pass too.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Shipments/Application/PickupRequestDtos.cs src/Modules/Shipments/Application/PickupRequestService.cs src/Api/Program.cs tests/Shipments.Tests/PickupRequestServiceTests.cs
git commit -m "feat(shipments): add PickupRequestService with create/list/cancel and branch-scoped staff reads"
```

---

### Task 10: Pickups endpoints + API tests

**Files:**
- Create: `src/Modules/Shipments/Presentation/PickupsEndpoints.cs`
- Modify: `src/Api/Program.cs` (`app.MapPickupsEndpoints();` next to `app.MapShipmentsEndpoints();`)
- Test: `tests/Api.Tests/PickupsEndpointsTests.cs`

**Interfaces:**
- Produces: `POST /api/v1/pickups` (merchant-track only, `MerchantPermissionCatalog.PickupsCreate`), `GET /api/v1/pickups` (dual-purpose: merchant-track sees own via `PickupsRead`, staff sees branch-scoped via `pickup.read`), `POST /api/v1/pickups/{id}/cancel` (merchant-track only, `PickupsCancel`).
- Consumes: `PickupRequestService` (Task 9), `RequireMerchantPermissionAnyCredential` (Task 5), `RequireApiKeyRateLimit` (Task 6). `POST /api/v1/pickups` is already covered by `IdempotencyMiddleware`'s path allow-list (Task 7) with no further change needed here.

- [ ] **Step 1: Write the endpoints**

Create `src/Modules/Shipments/Presentation/PickupsEndpoints.cs`:

```csharp
using Courier.Merchants.Application;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Shipments.Presentation;

public static class PickupsEndpoints
{
    public static void MapPickupsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pickups")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Bearer, AuthSchemes.ApiKey)
                .RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true))
            .RequireApiKeyRateLimit()
            .WithTags("Pickups");

        group.MapPost("/", async (CreatePickupRequestRequest request, PickupRequestService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CreatePickupRequestAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/pickups/{result.Id}", result);
            }
            catch (ShipmentValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "SHIPMENT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.PickupsCreate);

        // Dual-purpose read, same manual-branching reasoning as ShipmentsEndpoints'
        // HasReadAccessAsync: merchant-track callers (PickupsRead) see their own requests; staff
        // callers (pickup.read) see requests for their accessible branches. No existing filter
        // covers "either token kind, different permission string per kind."
        group.MapGet("/", async (PickupRequestService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (httpContext.User.GetMerchantId() is not null)
            {
                if (!await merchantAuthorizationChecker.HasPermissionAsync(httpContext.User, MerchantPermissionCatalog.PickupsRead, httpContext.RequestAborted))
                {
                    return Results.Forbid();
                }
            }
            else if (httpContext.User.GetUserId() is not null)
            {
                if (!await permissionChecker.HasPermissionAsync(httpContext.User, "pickup.read", httpContext.RequestAborted))
                {
                    return Results.Forbid();
                }
            }
            else
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await service.ListAsync(httpContext.User, httpContext.RequestAborted));
        });

        group.MapPost("/{id:guid}/cancel", async (Guid id, PickupRequestService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CancelAsync(id, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (ShipmentValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "SHIPMENT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.PickupsCancel);
    }
}
```

- [ ] **Step 2: Register the endpoint group**

In `src/Api/Program.cs`, next to `app.MapShipmentsEndpoints();`:

```csharp
app.MapPickupsEndpoints();
```

- [ ] **Step 3: Write the API tests**

Read `tests/Api.Tests/ShipmentsEndpointsTests.cs` first (branch/shipment seeding helpers) and Task 5's `ApiKeyAuthenticationTests.cs` (API-key fixture helper), then create `tests/Api.Tests/PickupsEndpointsTests.cs` covering:

- Merchant JWT: create two shipments sharing one origin branch, `POST /api/v1/pickups` with both ids → `201`, response `branchId` equals that shared origin branch.
- Merchant JWT: `POST /api/v1/pickups` with a shipment id belonging to a different merchant → `400 SHIPMENT_VALIDATION_FAILED`.
- Merchant JWT: `GET /api/v1/pickups` → sees only its own created requests.
- Staff JWT with org-wide branch access: `GET /api/v1/pickups` → sees the merchant's request too (branch-scoped, not merchant-scoped, for staff).
- Merchant JWT: `POST /api/v1/pickups/{id}/cancel` on its own request → `200`, `status: "Cancelled"`.
- Merchant JWT: cancel a request belonging to a different merchant → `404`.
- API-key caller (scoped to include `merchant.pickups.create`) can `POST /api/v1/pickups` successfully — proving the multi-scheme group wiring, matching the existing Shipments precedent.

- [ ] **Step 4: Run the tests, then build and test the full solution**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter PickupsEndpointsTests`
Expected: PASS.
Run: `dotnet build Courier.slnx && dotnet test Courier.slnx`
Expected: Build succeeds; all existing tests still pass.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Shipments/Presentation/PickupsEndpoints.cs src/Api/Program.cs tests/Api.Tests/PickupsEndpointsTests.cs
git commit -m "feat(shipments): add Pickups endpoints with merchant/API-key and staff dual access"
```

---

### Task 11: Merchant-scoped COD settlement endpoints + API tests

**Files:**
- Modify: `src/Modules/Merchants/Application/MerchantPermissionCatalog.cs` (add `CodSettlementsRead`, Owner + Staff)
- Modify: `src/Modules/Cod/Presentation/CodEndpoints.cs` (add a second, multi-scheme route group)
- Test: `tests/Api.Tests/CodEndpointsTests.cs` (extend the existing file)

**Interfaces:**
- Produces: `GET /api/v1/cod/settlements`, `GET /api/v1/cod/settlements/{id}` — new, additive, merchant-facing routes, dual-scheme (JWT or API key).
- Consumes: `CodService.ListMerchantSettlementsAsync`/`.GetMerchantSettlementByIdAsync` (existing, unmodified) — confirmed in exploration to already self-scope a merchant-JWT caller to `GetMerchantId()`, so no service change is needed, only new route registrations. The existing staff-only `/api/v1/cod/merchant-settlements` routes (single-scheme `.RequireAuthorization()` + `.RequirePermission("cod.read")`) are untouched — today a merchant JWT calling those existing routes already gets `403` (staff `IPermissionChecker` finds no `User` row for a `MerchantUser.Id`), confirming there was genuinely no merchant-reachable path before this task, not just an auth-scheme gap.

- [ ] **Step 1: Add the `CodSettlementsRead` merchant permission**

In `src/Modules/Merchants/Application/MerchantPermissionCatalog.cs`, add:

```csharp
    // Merchant Staff already sees COD amounts embedded in their own shipments via GET
    // /shipments — a settlements read for the same merchant is no new sensitive exposure, so
    // this sits at the same Owner+Staff tier as ShipmentRead, not the Owner-only management tier.
    public const string CodSettlementsRead = "merchant.cod.read";
```

Add it to both `OwnerPermissions` and `StaffPermissions`:

```csharp
    private static readonly string[] OwnerPermissions =
    {
        ProfileRead, ProfileUpdate, StaffManage, KycUpload, ShipmentCreate, ShipmentRead, ShipmentCancel, WebhooksManage, ApiKeysManage, PickupsCreate, PickupsRead, PickupsCancel, CodSettlementsRead
    };

    private static readonly string[] StaffPermissions =
    {
        ProfileRead, KycUpload, ShipmentCreate, ShipmentRead, ShipmentCancel, PickupsCreate, PickupsRead, PickupsCancel, CodSettlementsRead
    };
```

- [ ] **Step 2: Add the merchant-facing route group**

In `src/Modules/Cod/Presentation/CodEndpoints.cs`, add `using Courier.Merchants.Application;` and, inside `MapCodEndpoints` (after the existing staff `group`'s routes, before the method's closing brace):

```csharp
        // Additive merchant-facing read path — the existing /merchant-settlements routes above
        // stay staff-only and untouched. Reuses the exact same CodService methods, which already
        // self-scope a merchant-JWT-or-API-key caller to GetMerchantId() (confirmed in Task 11's
        // exploration) — no service change needed, only these new route registrations.
        var merchantGroup = app.MapGroup("/api/v1/cod/settlements")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Bearer, AuthSchemes.ApiKey)
                .RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true))
            .RequireApiKeyRateLimit()
            .WithTags("COD Management");

        merchantGroup.MapGet("/", async (CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListMerchantSettlementsAsync(null, httpContext.User, httpContext.RequestAborted)))
            .RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.CodSettlementsRead);

        merchantGroup.MapGet("/{id:guid}", async (Guid id, CodService service, HttpContext httpContext) =>
        {
            var result = await service.GetMerchantSettlementByIdAsync(id, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.CodSettlementsRead);
```

- [ ] **Step 3: Write the API tests**

Read `tests/Api.Tests/CodEndpointsTests.cs` first (existing staff-side COD fixture/seeding helpers) and Task 5's `ApiKeyAuthenticationTests.cs`, then add to that file (or a focused new one, whichever reads more naturally alongside the existing COD tests) covering:

- Merchant JWT: seed a `MerchantSettlement` for that merchant (via the existing staff-only creation flow used elsewhere in this test file), then `GET /api/v1/cod/settlements` → `200`, contains it.
- Merchant JWT: `GET /api/v1/cod/settlements` for a merchant with no settlements → `200`, empty list (not `403`/`404`).
- Merchant JWT: `GET /api/v1/cod/settlements/{id}` for another merchant's settlement id → `404` (not `403` — doesn't leak existence).
- API-key caller with `merchant.cod.read` in scope: `GET /api/v1/cod/settlements` → `200`.
- Staff JWT calling the **existing** `/api/v1/cod/merchant-settlements` route still works unmodified (`200`) — regression guard proving this task didn't touch the staff path.

- [ ] **Step 4: Run the tests, then build and test the full solution**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter CodEndpointsTests`
Expected: PASS.
Run: `dotnet build Courier.slnx && dotnet test Courier.slnx`
Expected: Build succeeds; all existing tests still pass.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Merchants/Application/MerchantPermissionCatalog.cs src/Modules/Cod/Presentation/CodEndpoints.cs tests/Api.Tests/CodEndpointsTests.cs
git commit -m "feat(cod): add merchant-facing settlement read endpoints"
```

---

### Task 12: Public tracking — `/track/{awb}`

**Files:**
- Create: `src/Modules/Shipments/Application/PublicTrackingDtos.cs`
- Create: `src/Modules/Shipments/Application/PublicTrackingStatusMapper.cs`
- Create: `src/Modules/Shipments/Application/PublicTrackingService.cs`
- Create: `src/Modules/Shipments/Presentation/PublicTrackingEndpoints.cs`
- Modify: `src/Api/Program.cs` (`builder.Services.AddScoped<PublicTrackingService>();`, `app.MapPublicTrackingEndpoints();`)
- Test: `tests/Shipments.Tests/PublicTrackingStatusMapperTests.cs`
- Test: `tests/Api.Tests/PublicTrackingEndpointTests.cs`

**Interfaces:**
- Produces: `PublicTrackingStatus` enum, `PublicTrackingStatusMapper.Map(ShipmentStatus) : PublicTrackingStatus`, `PublicTrackingService.GetByAwbAsync(string awb, CancellationToken) : Task<PublicTrackingResponse?>`, `GET /track/{awb}`.
- Consumes: existing `Shipment`/`ShipmentEvent`/`Branch`, `IpRateLimitFilter` (Task 6).

**Correction to the approved spec's literal mapping table:** the spec's §9 table was written against CLAUDE.md's illustrative state names, not the real 16-value `ShipmentStatus` enum (which has no `RiderPickupScan`/`Dispatched`, and does have `PickupAssigned`/`Cancelled`, neither mentioned in that table). Two fixes made here, to be recorded in Task 15's docs sync as resolved ambiguities:
1. The spec grouped `AtOriginBranch` with `Created` under `Received` — but `AtOriginBranch` happens chronologically *after* `PickedUp` in the real state machine, so that grouping would make the public status visibly regress (`PickedUp` → back to `Received`). `AtOriginBranch` is regrouped here with `Sorted`/`InTransit` instead, keeping the public timeline monotonic.
2. `PickupAssigned` (ungrouped in the spec) joins `Created` under `Received`. `Cancelled` (also ungrouped) gets its own new 8th public status, `Cancelled` — collapsing a genuine cancellation into `DeliveryAttemptUnsuccessful` would misleadingly imply a retry is still coming.

Final mapping (exhaustive over all 16 `ShipmentStatus` values):

```
Received                  ← Created, PickupAssigned
PickedUp                  ← PickedUp
InTransit                 ← AtOriginBranch, Sorted, InTransit
AtDestinationBranch       ← AtDestinationBranch
OutForDelivery            ← OutForDelivery
Delivered                 ← Delivered
DeliveryAttemptUnsuccessful ← DeliveryFailed, NdrCreated, RtoInitiated, RtoInTransit, RtoAtOrigin, RtoDelivered
Cancelled                 ← Cancelled
```

- [ ] **Step 1: Write the DTOs**

Create `src/Modules/Shipments/Application/PublicTrackingDtos.cs`:

```csharp
namespace Courier.Shipments.Application;

// CLAUDE.md §24's safe subset only: a status label, a timestamp, and an optional city/hub-level
// location. No branch ID, address, coordinates, staff/rider identity, notes, financial field, or
// recipient PII beyond what the requester (holder of the AWB) already possesses.
public enum PublicTrackingStatus
{
    Received,
    PickedUp,
    InTransit,
    AtDestinationBranch,
    OutForDelivery,
    Delivered,
    DeliveryAttemptUnsuccessful,
    Cancelled
}

public record PublicTrackingEvent(PublicTrackingStatus Status, DateTime Timestamp, string? Location);

public record PublicTrackingResponse(string Awb, PublicTrackingStatus CurrentStatus, List<PublicTrackingEvent> Events);
```

- [ ] **Step 2: Write the failing mapper tests**

Create `tests/Shipments.Tests/PublicTrackingStatusMapperTests.cs`:

```csharp
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Xunit;

namespace Courier.Shipments.Tests;

public class PublicTrackingStatusMapperTests
{
    [Theory]
    [InlineData(ShipmentStatus.Created, PublicTrackingStatus.Received)]
    [InlineData(ShipmentStatus.PickupAssigned, PublicTrackingStatus.Received)]
    [InlineData(ShipmentStatus.PickedUp, PublicTrackingStatus.PickedUp)]
    [InlineData(ShipmentStatus.AtOriginBranch, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.Sorted, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.InTransit, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.AtDestinationBranch, PublicTrackingStatus.AtDestinationBranch)]
    [InlineData(ShipmentStatus.OutForDelivery, PublicTrackingStatus.OutForDelivery)]
    [InlineData(ShipmentStatus.Delivered, PublicTrackingStatus.Delivered)]
    [InlineData(ShipmentStatus.DeliveryFailed, PublicTrackingStatus.DeliveryAttemptUnsuccessful)]
    [InlineData(ShipmentStatus.NdrCreated, PublicTrackingStatus.DeliveryAttemptUnsuccessful)]
    [InlineData(ShipmentStatus.RtoInitiated, PublicTrackingStatus.DeliveryAttemptUnsuccessful)]
    [InlineData(ShipmentStatus.RtoInTransit, PublicTrackingStatus.DeliveryAttemptUnsuccessful)]
    [InlineData(ShipmentStatus.RtoAtOrigin, PublicTrackingStatus.DeliveryAttemptUnsuccessful)]
    [InlineData(ShipmentStatus.RtoDelivered, PublicTrackingStatus.DeliveryAttemptUnsuccessful)]
    [InlineData(ShipmentStatus.Cancelled, PublicTrackingStatus.Cancelled)]
    public void Map_KnownStatus_ReturnsExpectedPublicStatus(ShipmentStatus input, PublicTrackingStatus expected)
    {
        Assert.Equal(expected, PublicTrackingStatusMapper.Map(input));
    }

    [Fact]
    public void Map_EveryDefinedShipmentStatus_HasAMapping()
    {
        // Guards against a future ShipmentStatus value silently leaking through unmapped —
        // exactly the property spec §12's testing section requires.
        foreach (var status in Enum.GetValues<ShipmentStatus>())
        {
            var exception = Record.Exception(() => PublicTrackingStatusMapper.Map(status));
            Assert.Null(exception);
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj`
Expected: FAIL to compile — `PublicTrackingStatusMapper` doesn't exist yet.

- [ ] **Step 4: Implement the mapper**

Create `src/Modules/Shipments/Application/PublicTrackingStatusMapper.cs`:

```csharp
using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

public static class PublicTrackingStatusMapper
{
    public static PublicTrackingStatus Map(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Created or ShipmentStatus.PickupAssigned => PublicTrackingStatus.Received,
        ShipmentStatus.PickedUp => PublicTrackingStatus.PickedUp,
        ShipmentStatus.AtOriginBranch or ShipmentStatus.Sorted or ShipmentStatus.InTransit => PublicTrackingStatus.InTransit,
        ShipmentStatus.AtDestinationBranch => PublicTrackingStatus.AtDestinationBranch,
        ShipmentStatus.OutForDelivery => PublicTrackingStatus.OutForDelivery,
        ShipmentStatus.Delivered => PublicTrackingStatus.Delivered,
        ShipmentStatus.DeliveryFailed or ShipmentStatus.NdrCreated or ShipmentStatus.RtoInitiated
            or ShipmentStatus.RtoInTransit or ShipmentStatus.RtoAtOrigin or ShipmentStatus.RtoDelivered
            => PublicTrackingStatus.DeliveryAttemptUnsuccessful,
        ShipmentStatus.Cancelled => PublicTrackingStatus.Cancelled,
        _ => throw new NotSupportedException($"ShipmentStatus '{status}' has no public tracking mapping.")
    };
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Implement `PublicTrackingService`**

Create `src/Modules/Shipments/Application/PublicTrackingService.cs`:

```csharp
using Courier.Branches.Domain;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public class PublicTrackingService
{
    private readonly DbContext _dbContext;

    public PublicTrackingService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PublicTrackingResponse?> GetByAwbAsync(string awb, CancellationToken cancellationToken = default)
    {
        // No tenant context exists on an anonymous request — deliberate IgnoreQueryFilters with an
        // explicit trusted predicate (exact Awb match, a shared-secret-strength business key), same
        // reasoning as ApiKeyAuthenticationHandler (Task 5). The AWB is the only input this endpoint
        // accepts or needs (CLAUDE.md §24); there is no tenant/org concept to leak.
        var shipment = await _dbContext.Set<Shipment>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Awb == awb, cancellationToken);
        if (shipment is null)
        {
            return null;
        }

        // ShipmentEvent carries no tenant filter of its own (AppDbContext.cs's documented
        // transitive-through-Shipment precedent) — no IgnoreQueryFilters needed here, there's
        // nothing to bypass.
        var events = await _dbContext.Set<ShipmentEvent>()
            .Where(e => e.ShipmentId == shipment.Id)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(cancellationToken);

        var branchIds = events.Where(e => e.BranchId is not null).Select(e => e.BranchId!.Value).Distinct().ToArray();
        var branchNames = await _dbContext.Set<Branch>().IgnoreQueryFilters()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var publicEvents = new List<PublicTrackingEvent>();
        PublicTrackingStatus? lastStatus = null;
        foreach (var e in events)
        {
            var mapped = PublicTrackingStatusMapper.Map(e.NewStatus);
            if (mapped == lastStatus)
            {
                // Collapse consecutive internal statuses that map to the same public bucket (e.g.
                // AtOriginBranch -> Sorted, both InTransit) into one entry — the public timeline
                // shows one InTransit row, not a confusing repeat.
                continue;
            }
            var location = e.BranchId is Guid branchId && branchNames.TryGetValue(branchId, out var name) ? name : null;
            publicEvents.Add(new PublicTrackingEvent(mapped, e.Timestamp.UtcDateTime, location));
            lastStatus = mapped;
        }

        return new PublicTrackingResponse(shipment.Awb, PublicTrackingStatusMapper.Map(shipment.CurrentStatus), publicEvents);
    }
}
```

- [ ] **Step 7: Implement the endpoint**

Create `src/Modules/Shipments/Presentation/PublicTrackingEndpoints.cs`:

```csharp
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Shipments.Presentation;

public static class PublicTrackingEndpoints
{
    public static void MapPublicTrackingEndpoints(this IEndpointRouteBuilder app)
    {
        // No RequireAuthorization anywhere on this route — genuinely anonymous, per CLAUDE.md §24.
        app.MapGet("/track/{awb}", async (string awb, PublicTrackingService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetByAwbAsync(awb, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .RequireIpRateLimit()
        .WithTags("Public Tracking");
    }
}
```

- [ ] **Step 8: Register the service and endpoint in `Program.cs`**

```csharp
builder.Services.AddScoped<PublicTrackingService>();
```

next to `ShipmentService`'s registration, and:

```csharp
app.MapPublicTrackingEndpoints();
```

next to `app.MapShipmentsEndpoints();`.

- [ ] **Step 9: Write the API-level tests**

Read `tests/Api.Tests/ShipmentsEndpointsTests.cs` first (shipment-seeding and scan-transition helpers), then create `tests/Api.Tests/PublicTrackingEndpointTests.cs` covering:

- `GET /track/{awb}` for a freshly created shipment, no auth header at all → `200`, `currentStatus: "Received"`.
- Same shipment after driving it through a couple of real staff scan transitions (e.g. `receive-origin`) → `currentStatus` updates accordingly (`InTransit`), and the events list never regresses to an earlier bucket.
- `GET /track/{awb}` for an unknown AWB → `404`.
- **Excluded-fields assertion** (per spec §12, a serialization-level check, not just "isn't in the DTO"): read the raw response body as a string and assert it contains none of `"branchId"`, `"userId"`, `"riderId"`, `"latitude"`, `"longitude"`, `"notes"`, `"metadata"`, `"codAmount"`, `"deliveryCharge"`, `"declaredValue"` (case-insensitive).
- A forced rate-limit test: scoped `WebApplicationFactory` with `RateLimiting:PublicTrackingRequestsPerMinute` overridden to `2`, call `/track/{awb}` 3 times from the test client → third call `429` with `Retry-After` header.

- [ ] **Step 10: Run the tests, then build and test the full solution**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter PublicTrackingStatusMapperTests`
Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter PublicTrackingEndpointTests`
Expected: PASS.
Run: `dotnet build Courier.slnx && dotnet test Courier.slnx`
Expected: Build succeeds; all existing tests still pass.

- [ ] **Step 11: Commit**

```bash
git add src/Modules/Shipments/Application/PublicTrackingDtos.cs src/Modules/Shipments/Application/PublicTrackingStatusMapper.cs src/Modules/Shipments/Application/PublicTrackingService.cs src/Modules/Shipments/Presentation/PublicTrackingEndpoints.cs src/Api/Program.cs tests/Shipments.Tests/PublicTrackingStatusMapperTests.cs tests/Api.Tests/PublicTrackingEndpointTests.cs
git commit -m "feat(tracking): add public /track/{awb} endpoint with safe status mapping and IP rate limiting"
```

---

### Task 13: Request logging middleware for `ApiKey`-authenticated requests

**Files:**
- Create: `src/Api/Middleware/ApiKeyRequestLoggingMiddleware.cs`
- Modify: `src/Api/Program.cs` (register right after `app.UseAuthorization();`, before `IdempotencyMiddleware`)
- Test: `tests/Api.Tests/ApiKeyRequestLoggingTests.cs`

**Interfaces:**
- Produces: `Courier.Api.Middleware.ApiKeyRequestLoggingMiddleware`.
- Consumes: nothing new — reads the `api_key_scopes`/`sub`/`org_id`/`merchant_id` claims already established by `ApiKeyAuthenticationHandler` (Task 5).

Self-selects the same way `ApiKeyRateLimitFilter` does: only logs when the caller authenticated via the `"ApiKey"` scheme — every staff/merchant-JWT/anonymous request is untouched (they're already covered by the existing generic `UseSerilogRequestLogging()`, unchanged). `TraceIdentifier` is reused as the correlation id, matching `ExceptionHandlingMiddleware`'s existing pattern — no new correlation-id mechanism introduced.

- [ ] **Step 1: Implement the middleware**

Create `src/Api/Middleware/ApiKeyRequestLoggingMiddleware.cs`:

```csharp
using System.Diagnostics;

namespace Courier.Api.Middleware;

public class ApiKeyRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyRequestLoggingMiddleware> _logger;

    public ApiKeyRequestLoggingMiddleware(RequestDelegate next, ILogger<ApiKeyRequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.FindFirst("api_key_scopes") is null)
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        await _next(context);
        stopwatch.Stop();

        _logger.LogInformation(
            "ApiKey request {ApiKeyId} org={OrganizationId} merchant={MerchantId} {Method} {Path} -> {StatusCode} in {ElapsedMs}ms (trace {TraceId})",
            context.User.FindFirst("sub")?.Value,
            context.User.FindFirst("org_id")?.Value,
            context.User.FindFirst("merchant_id")?.Value,
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            context.TraceIdentifier);
    }
}
```

- [ ] **Step 2: Register the middleware in `Program.cs`**

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<Courier.Api.Middleware.ApiKeyRequestLoggingMiddleware>();
app.UseMiddleware<Courier.Api.Middleware.IdempotencyMiddleware>();
```

- [ ] **Step 3: Write the test**

Create `tests/Api.Tests/ApiKeyRequestLoggingTests.cs`. Since Serilog writes to the console sink only, capture log records via a custom `ILoggerProvider` registered into a scoped `WebApplicationFactory<Program>` (the standard xUnit technique for asserting on `ILogger<T>` output, not a Serilog-specific mechanism):

```csharp
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Courier.Api.Tests;

file class CapturingLogger : ILogger
{
    private readonly ConcurrentBag<string> _messages;
    public CapturingLogger(ConcurrentBag<string> messages) => _messages = messages;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _messages.Add(formatter(state, exception));
}

file class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentBag<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
    public void Dispose() { }
}

public class ApiKeyRequestLoggingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ApiKeyRequestLoggingTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task ApiKeyAuthenticatedRequest_LogsOneStructuredEntry()
    {
        var capturingProvider = new CapturingLoggerProvider();
        var scopedFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(capturingProvider)));
        var client = scopedFactory.CreateClient();

        // Reuse the merchant-registration + API-key-creation helper pattern from
        // ApiKeyAuthenticationTests (Task 5) to obtain a valid plaintext key, then:
        // client.DefaultRequestHeaders.Add("X-Api-Key", plaintextKey);
        // await client.GetAsync("/api/v1/shipments");

        Assert.Contains(capturingProvider.Messages, m => m.Contains("ApiKey request") && m.Contains("/api/v1/shipments"));
    }

    [Fact]
    public async Task StaffJwtRequest_LogsNothing()
    {
        var capturingProvider = new CapturingLoggerProvider();
        var scopedFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(capturingProvider)));
        var client = scopedFactory.CreateClient();

        // Register + log in as staff (existing helper pattern), call any authenticated route with
        // the staff JWT, then assert no "ApiKey request" entry was captured — proving the
        // self-selecting claim check actually gates this middleware.

        Assert.DoesNotContain(capturingProvider.Messages, m => m.Contains("ApiKey request"));
    }
}
```

Fill in the two commented sections using the exact merchant-registration/API-key-creation and staff-registration helpers already established in `ApiKeyAuthenticationTests.cs` (Task 5) and `ShipmentsEndpointsTests.cs`.

- [ ] **Step 4: Run the tests, then build and test the full solution**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter ApiKeyRequestLoggingTests`
Expected: PASS.
Run: `dotnet build Courier.slnx && dotnet test Courier.slnx`
Expected: Build succeeds; all existing tests still pass.

- [ ] **Step 5: Commit**

```bash
git add src/Api/Middleware/ApiKeyRequestLoggingMiddleware.cs src/Api/Program.cs tests/Api.Tests/ApiKeyRequestLoggingTests.cs
git commit -m "feat(observability): log one structured entry per ApiKey-authenticated request"
```

---

### Task 14: End-to-end exit-criteria proof test

**Files:**
- Test: `tests/Api.Tests/MerchantApiExitCriteriaEndToEndTests.cs`

**Interfaces:**
- Consumes: every production piece from Tasks 1-13. No production code changes this task — purely a proving test, matching spec §2's exit criteria stated almost verbatim: *"an external client can create a shipment, poll its status, and see a settlement purely through the documented Merchant API."*

- [ ] **Step 1: Write the end-to-end test**

Read `tests/Api.Tests/ShipmentsEndpointsTests.cs` (branch/zone/rate-card seeding helpers), `tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs` (merchant registration/login), `tests/Api.Tests/ApiKeyEndpointsTests.cs` (Task 4, API key creation), and `tests/Api.Tests/CodEndpointsTests.cs` (staff-side settlement-creation flow) first, then create `tests/Api.Tests/MerchantApiExitCriteriaEndToEndTests.cs` with one test that:

1. **Setup (staff JWT, unavoidable bootstrapping — this is infrastructure a real courier ops team configures once, not part of the "external client" claim):** register an organization; create origin/destination branches and delivery zones; seed a rate card and zone rate; register a merchant; onboard a merchant Owner user.
2. **Obtain credentials (merchant JWT, one-time):** log in as the merchant Owner, `POST /api/v1/merchant/api-keys` to get a `plaintextKey`. From this point on, the test's `HttpClient` never sets an `Authorization: Bearer` header again — only `X-Api-Key`.
3. **Create (API key only):** `POST /api/v1/shipments` with the API key → `201`, capture the `Awb`.
4. **Poll (API key only):** `GET /api/v1/shipments/{awb}` with the API key → `200`, matches what was created.
5. **Generate a settlement (staff JWT, unavoidable — settlement creation is staff-only this phase, per Task 11's scope):** drive the shipment through enough real transitions/COD collection/branch-settlement/finance-verification/merchant-settlement-creation steps (reusing `CodEndpointsTests.cs`'s existing staff flow) to produce one real `MerchantSettlement` row for this merchant.
6. **Read the settlement (API key only):** `GET /api/v1/cod/settlements` with the API key → `200`, contains the settlement from Step 5.
7. **Assert no JWT was used for steps 3, 4, and 6** — structurally guaranteed by never adding an `Authorization` header to the client after Step 2, not by inspecting request headers after the fact.

- [ ] **Step 2: Run the test, then build and test the full solution**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter MerchantApiExitCriteriaEndToEndTests`
Expected: PASS.
Run: `dotnet build Courier.slnx && dotnet test Courier.slnx`
Expected: Build succeeds; all existing tests still pass. This is the last task with production-adjacent risk — treat any failure here as a real integration gap, not a test-only issue, since it's exercising the full chain Tasks 1-13 built.

- [ ] **Step 3: Commit**

```bash
git add tests/Api.Tests/MerchantApiExitCriteriaEndToEndTests.cs
git commit -m "test(api): add end-to-end proof that the Merchant API satisfies Phase 12's exit criteria"
```

---

### Task 15: Docs sync

**Files:**
- Modify: `docs/CLARIFICATIONS.md` (new `## 13.` section)
- Modify: `docs/ROADMAP.md` (Phase 12 status paragraph)
- Modify: `docs/API-SPEC.md` (§3 Merchant API catalog, §4 Public Tracking, §5 Webhooks — reflect what actually got built)
- Modify: `docs/DOMAIN-MODEL.md` (add `ApiKey` and `PickupRequest`)

No code changes this task.

- [ ] **Step 1: Add `CLARIFICATIONS.md` #13**

Append a new section, following the exact numbered-heading style of #7-#12 (read the tail of the file first to confirm #12 is still the last entry):

```markdown
## 13. Phase 12: self-escalation carve-out, ApiKey credential format, and two public-tracking status-mapping corrections

**Self-escalation carve-out.** `RequireMerchantPermission` (JWT-only) and the new
`RequireMerchantPermissionAnyCredential` (JWT or API key) are two separate SharedKernel
extensions, not one widened filter. API-key management itself, merchant staff invite, KYC
upload, and profile update stay on the JWT-only extension deliberately — an API key's default
scope is every permission its creating Owner holds, so if key-management endpoints accepted API-key
auth, a key could create, list, or revoke other keys (including itself). Only Shipments, Webhooks,
Cod settlement reads, and Pickups were switched onto the any-credential extension.

**`ApiKey` credential format.** `{Prefix}.{Secret}` where `Prefix` is 8 random hex characters
(globally unique, indexed, used for O(1) lookup) and `Secret` is a 32-byte
`RandomNumberGenerator`-sourced value, SHA-256 hashed at rest — same hash-at-rest pattern as
`RefreshToken` (Phase 1) and `WebhookSubscription.Secret`'s pre-Phase-11 hashing precedent. The
plaintext is shown exactly once, in the creation response.

**Public tracking status mapping corrections.** The design spec's §9 table was written against
CLAUDE.md's illustrative state names, not the real 16-value `ShipmentStatus` enum. Three
corrections made during implementation (Task 12): `AtOriginBranch` was regrouped from the
spec's `Received` bucket into `InTransit` — it happens chronologically *after* `PickedUp`, so
the original grouping would have made the public status visibly regress; `PickupAssigned`
(unmentioned in the spec table) joins `Created` under `Received`; `Cancelled` (also unmentioned)
got its own new 8th public status rather than being folded into `DeliveryAttemptUnsuccessful`,
since collapsing a genuine cancellation into that bucket would misleadingly imply a retry is
still coming. See `PublicTrackingStatusMapper.Map` for the authoritative, exhaustive mapping —
`PublicTrackingStatusMapperTests.Map_EveryDefinedShipmentStatus_HasAMapping` guards against a
future `ShipmentStatus` value ever landing unmapped.

**Idempotency and rate limiting scope, confirmed as designed.** Idempotency-Key enforcement and
rate limiting are both self-selecting on *credential kind* (merchant JWT/API key vs staff JWT vs
anonymous), not on route ownership — every existing staff-facing test from prior phases needed
zero changes, confirmed by the full-solution test run staying green after every task in this
phase's plan.
```

- [ ] **Step 2: Add the `ROADMAP.md` Phase 12 status paragraph**

Read the current Phase 12 section first (it currently ends right after the *Exit:* line, no *Status* paragraph yet), then append a paragraph in the same style as Phase 11's, e.g.:

```markdown
*Status (<today's date>): Complete — backend slice only.* API keys (`ApiKey` entity +
`ApiKey`/`Bearer` dual-scheme auth), Redis-backed rate limiting and idempotency (first real
Redis use in this codebase), a real `PickupRequest` feature, merchant-scoped COD settlement
reads, and public `/track/{awb}` are all implemented and proven end-to-end
(`tests/Api.Tests/MerchantApiExitCriteriaEndToEndTests.cs`). `CLARIFICATIONS.md` #13 records
three resolved design ambiguities. The frontend merchant portal (`src/web`, still an untouched
`create-next-app` skeleton) is deliberately deferred to a follow-up Phase 12b — this phase's
scope decision, made explicit up front in the design spec's scope note.
```

- [ ] **Step 3: Update `API-SPEC.md`**

Read the full current file first. Update:
- §3 Merchant API catalog: add the now-implemented routes — `POST/GET/DELETE /api/v1/merchant/api-keys`, `POST/GET /api/v1/pickups`, `POST /api/v1/pickups/{id}/cancel`, `GET /api/v1/cod/settlements`, `GET /api/v1/cod/settlements/{id}` — and note that `Shipments`/`Webhooks`/these new routes all accept both a merchant JWT and an API key.
- §4 Public Tracking: confirm/update the documented response shape to match `PublicTrackingResponse` (`awb`, `currentStatus`, `events[]` of `{status, timestamp, location}`), and the 8-value `PublicTrackingStatus` enum (not the CLAUDE.md §24 prose list).
- §5 Webhooks: note dual-credential access (already implied by §3, make it explicit here too).

- [ ] **Step 4: Update `DOMAIN-MODEL.md`**

Read the full current file first, then add `ApiKey` (Merchants module) and `PickupRequest` (Shipments module) entity definitions matching Tasks 2 and 8's actual shipped field lists, in whatever format the rest of that file uses for entity documentation.

- [ ] **Step 5: Commit**

```bash
git add docs/CLARIFICATIONS.md docs/ROADMAP.md docs/API-SPEC.md docs/DOMAIN-MODEL.md
git commit -m "docs: sync Phase 12 (Merchant API, public tracking, API keys) into the design docs"
```

---

## Self-Review

- [ ] Every spec section (1-13) maps to at least one task above: §4.1/4.3→Tasks 1-2, §4.1 service/endpoints→Tasks 3-4, §5→Task 5, §6→Task 6, §7→Task 7, §4.2→Tasks 8-10, §8 (Cod)→Task 11, §9→Task 12, §10→Task 13, §8 (catalog completion as a whole)/§2 exit criteria→Task 14, §11 error codes→woven into Tasks 5-7/12 inline (no standalone task needed, each code is introduced at its owning task), §12 testing→woven throughout, §13 isolation checklist→Tasks 2/8/12 tenant-filter design.
- [ ] No placeholder/TODO content remains in any task's code blocks — every code sample above is complete, compilable-looking C# with real types (verified by re-reading Tasks 1-15 in sequence during this self-review pass).
- [ ] Type consistency check: `ApiKey.Scopes`/`PickupRequest.ShipmentIds` are `string[]`/`Guid[]` consistently across the entity (Task 2/8), the DTOs (Task 3/9), and every test that constructs them. `IIdempotencyStore`/`IRateLimiter` signatures (Task 1) match every call site in Tasks 6-7 exactly (`string key`/`string partitionKey`, not renamed elsewhere).
- [ ] Every task ends with a build+test verification step and a commit step; no task leaves the solution in a red state for a later task to inherit.
- [ ] Global Constraints' immediate-migration rule is honored: Task 8 bundles the `pickup.read` `PermissionCatalog.All` addition into the same migration as the `PickupRequests` table, not a follow-up.
