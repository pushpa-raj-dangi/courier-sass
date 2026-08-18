# Phase 1 — Identity, Auth, RBAC, Tenant Isolation, Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `docs/ROADMAP.md` Phase 1 — an organization can self-register (creating its
`OrganizationOwner`), a user can log in and refresh their session, permission checks work
end-to-end on at least one protected endpoint, and tenant isolation is proven by an
integration test showing cross-tenant access is rejected.

**Architecture:** One class-library project per module (`src/Modules/<Module>/<Module>.csproj`),
each internally split into `Domain/Application/Infrastructure/Presentation` folders, per
`docs/ARCHITECTURE.md` and this session's module-layout decision. A new `src/Persistence`
project hosts the single `AppDbContext` (referencing each module's Domain entities for
`DbSet<T>` declarations) so no module needs to reference the concrete DbContext type —
module Application/Infrastructure code depends only on the EF Core base `DbContext` class,
resolved via DI. `src/SharedKernel` hosts only the cross-module authorization abstractions
(`ICurrentTenantProvider`, `IPermissionChecker`, the `RequirePermission` endpoint filter) —
no business entities. Tenant isolation is enforced via EF Core global query filters keyed off
`ICurrentTenantProvider`, resolved server-side from the JWT's `org_id` claim — never from
client input, per CLAUDE.md §33.

**Tech Stack:** EF Core 10 + Npgsql provider, `System.IdentityModel.Tokens.Jwt` for issuing
JWTs, `Microsoft.AspNetCore.Authentication.JwtBearer` for validating them,
`Microsoft.Extensions.Identity.Core`'s `PasswordHasher<T>` for password hashing, xUnit +
`Microsoft.AspNetCore.Mvc.Testing` for integration tests against the real Postgres container
from `docker-compose.yml`.

**Spec:** `docs/ROADMAP.md` (Phase 1), `docs/DOMAIN-MODEL.md` (Identity & Tenancy, Audit
sections), `docs/RBAC.md`, `docs/ARCHITECTURE.md` §2–3, `docs/CLARIFICATIONS.md` #1 and #5.

## Global Constraints

- Modular monolith; module boundaries are project references, not folders (this session's
  decision) — a module may reference another module's project but by convention only uses
  its Application-layer types (documented, not compiler-enforced beyond the project
  boundary itself).
- No `if (user.Role == "Admin")` anywhere — all authorization is permission-based via
  `IPermissionChecker`, per CLAUDE.md §6.
- `TenantId`/`OrganizationId` is never trusted from client input — always resolved
  server-side from the authenticated principal, per CLAUDE.md §33 and `ARCHITECTURE.md` §3.
- Tenant-scoped entities (`User`, `Role`, `UserRole`, `AuditLog`) get an EF Core
  `HasQueryFilter` keyed off `ICurrentTenantProvider.OrganizationId`. The two deliberate,
  narrow exceptions (`IgnoreQueryFilters()` for the pre-authentication email lookups in
  login/registration) must stay narrowly scoped to those two call sites — never a general
  bypass.
- `Permission` rows are global reference data (no `OrganizationId`), seeded once via EF Core
  migration `HasData` from the full catalog in `docs/RBAC.md` §3.
- SuperAdmin / platform-level (cross-tenant) auth is explicitly OUT of scope for this phase
  (`CLARIFICATIONS.md` #5 describes it; nothing in Phase 1's exit criteria requires it) —
  `User.OrganizationId == null` login attempts are rejected with a clear error, not silently
  mishandled.
- Commit after every task using conventional prefixes (`feat:`, `chore:`).
- Every integration test in this plan that touches `AppDbContext` requires Postgres running
  (`docker compose up -d postgres`) — same pattern established in Phase 0.

---

### Task 1: Scaffold Module Projects and Solution Wiring

**Files:**
- Create: `src/SharedKernel/SharedKernel.csproj`
- Create: `src/Modules/Identity/Identity.csproj`
- Create: `src/Modules/Audit/Audit.csproj`
- Create: `src/Persistence/Persistence.csproj`
- Modify: `Courier.slnx` (add all four projects)
- Modify: `src/Api/Api.csproj` (project references + packages)

**Interfaces:**
- Consumes: nothing (first task).
- Produces: four empty-but-building class library projects wired into the solution and
  referenced from `Api`, with the reference graph
  `SharedKernel ← Audit ← Identity`, `SharedKernel, Identity, Audit ← Persistence`,
  `SharedKernel, Identity, Audit, Persistence ← Api`. No cycles. Later tasks add real code
  into these shells.

- [ ] **Step 1: Create the four projects**

```bash
dotnet new classlib -n SharedKernel -o src/SharedKernel
dotnet new classlib -n Audit -o src/Modules/Audit
dotnet new classlib -n Identity -o src/Modules/Identity
dotnet new classlib -n Persistence -o src/Persistence
dotnet sln Courier.slnx add src/SharedKernel/SharedKernel.csproj src/Modules/Audit/Audit.csproj src/Modules/Identity/Identity.csproj src/Persistence/Persistence.csproj
```

Delete the template `Class1.cs` file each `dotnet new classlib` generates in all four
project folders — they start empty.

- [ ] **Step 2: Add framework references for the Presentation layers**

`SharedKernel.csproj`, `Identity.csproj`, and `Audit.csproj` each need ASP.NET Core types
(`IEndpointRouteBuilder`, `IEndpointFilter`, `HttpContext`) for their `Presentation`
folders. Add to each of the three `.csproj` files, inside the existing `<PropertyGroup>`
or as a sibling `<ItemGroup>`:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

- [ ] **Step 3: Wire project references**

```bash
dotnet add src/Modules/Identity/Identity.csproj reference src/SharedKernel/SharedKernel.csproj
dotnet add src/Modules/Identity/Identity.csproj reference src/Modules/Audit/Audit.csproj
dotnet add src/Persistence/Persistence.csproj reference src/SharedKernel/SharedKernel.csproj
dotnet add src/Persistence/Persistence.csproj reference src/Modules/Identity/Identity.csproj
dotnet add src/Persistence/Persistence.csproj reference src/Modules/Audit/Audit.csproj
dotnet add src/Api/Api.csproj reference src/SharedKernel/SharedKernel.csproj
dotnet add src/Api/Api.csproj reference src/Modules/Identity/Identity.csproj
dotnet add src/Api/Api.csproj reference src/Modules/Audit/Audit.csproj
dotnet add src/Api/Api.csproj reference src/Persistence/Persistence.csproj
```

- [ ] **Step 4: Add EF Core packages**

```bash
dotnet add src/Modules/Identity/Identity.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Modules/Identity/Identity.csproj package Microsoft.Extensions.Identity.Core
dotnet add src/Modules/Identity/Identity.csproj package System.IdentityModel.Tokens.Jwt
dotnet add src/Modules/Audit/Audit.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Persistence/Persistence.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Api/Api.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Api/Api.csproj package Microsoft.EntityFrameworkCore.Design
dotnet add src/Api/Api.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet tool install --local dotnet-ef
```

(`dotnet tool install --local` creates `.config/dotnet-tools.json` on first use — if it
errors because no tool manifest exists yet, run `dotnet new tool-manifest` first, then
retry.)

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (four new empty projects plus the
existing `Api`/`Api.Tests`).

- [ ] **Step 6: Commit**

```bash
git add Courier.slnx src/SharedKernel src/Modules src/Persistence src/Api/Api.csproj .config
git commit -m "chore: scaffold SharedKernel, Identity, Audit, Persistence module projects"
```

---

### Task 2: SharedKernel — Tenant/Permission Abstractions and Authorization Filter

**Files:**
- Create: `src/SharedKernel/ICurrentTenantProvider.cs`
- Create: `src/SharedKernel/IPermissionChecker.cs`
- Create: `src/SharedKernel/RequirePermissionFilter.cs`
- Create: `tests/SharedKernel.Tests/SharedKernel.Tests.csproj`
- Create: `tests/SharedKernel.Tests/RequirePermissionFilterTests.cs`
- Modify: `Courier.slnx`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `ICurrentTenantProvider { Guid? OrganizationId { get; } }` — every later module's
    `AppDbContext` query filters and tenant-scoped services depend on this.
  - `IPermissionChecker { Task<bool> HasPermissionAsync(ClaimsPrincipal user, string
    permission, CancellationToken ct = default); }` — implemented by Identity.Infrastructure
    in Task 8, consumed here by the filter.
  - `public static class PermissionEndpointExtensions { public static
    RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string
    permission); }` — every later Presentation endpoint that needs a permission check
    calls `.RequirePermission("resource.action")`.

- [ ] **Step 1: Scaffold the test project**

```bash
dotnet new xunit -n SharedKernel.Tests -o tests/SharedKernel.Tests
dotnet sln Courier.slnx add tests/SharedKernel.Tests/SharedKernel.Tests.csproj
dotnet add tests/SharedKernel.Tests/SharedKernel.Tests.csproj reference src/SharedKernel/SharedKernel.csproj
```

Delete the generated `tests/SharedKernel.Tests/UnitTest1.cs`.

- [ ] **Step 2: Write the interfaces (needed before the filter and its test can compile)**

`src/SharedKernel/ICurrentTenantProvider.cs`:

```csharp
namespace Courier.SharedKernel;

public interface ICurrentTenantProvider
{
    Guid? OrganizationId { get; }
}
```

`src/SharedKernel/IPermissionChecker.cs`:

```csharp
using System.Security.Claims;

namespace Courier.SharedKernel;

public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Write the failing test for the filter**

`tests/SharedKernel.Tests/RequirePermissionFilterTests.cs`:

```csharp
using System.Security.Claims;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class RequirePermissionFilterTests
{
    private sealed class FakePermissionChecker : IPermissionChecker
    {
        private readonly bool _result;
        public FakePermissionChecker(bool result) => _result = result;

        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private static EndpointFilterInvocationContext BuildContext(bool authenticated, IPermissionChecker checker)
    {
        var services = new ServiceCollection();
        services.AddSingleton(checker);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (authenticated)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "TestAuth"));
        }
        return new DefaultEndpointFilterInvocationContext(httpContext);
    }

    [Fact]
    public async Task InvokeAsync_UserHasPermission_CallsNext()
    {
        var filter = new RequirePermissionFilter("user.read");
        var context = BuildContext(authenticated: true, new FakePermissionChecker(true));
        var nextCalled = false;

        var result = await filter.InvokeAsync(context, _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        Assert.True(nextCalled);
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
    }

    [Fact]
    public async Task InvokeAsync_UserLacksPermission_ReturnsForbid()
    {
        var filter = new RequirePermissionFilter("user.read");
        var context = BuildContext(authenticated: true, new FakePermissionChecker(false));

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, statusResult.StatusCode);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/SharedKernel.Tests/SharedKernel.Tests.csproj`
Expected: FAIL to compile — `RequirePermissionFilter` doesn't exist yet.

- [ ] **Step 5: Implement the filter**

`src/SharedKernel/RequirePermissionFilter.cs`:

```csharp
using Microsoft.AspNetCore.Http;

namespace Courier.SharedKernel;

public class RequirePermissionFilter : IEndpointFilter
{
    private readonly string _permission;

    public RequirePermissionFilter(string permission)
    {
        _permission = permission;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var checker = httpContext.RequestServices.GetRequiredService<IPermissionChecker>();

        var hasPermission = await checker.HasPermissionAsync(httpContext.User, _permission, httpContext.RequestAborted);
        if (!hasPermission)
        {
            return Results.Forbid();
        }

        return await next(context);
    }
}

public static class PermissionEndpointExtensions
{
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission)
    {
        return builder
            .RequireAuthorization()
            .AddEndpointFilter(new RequirePermissionFilter(permission));
    }
}
```

Add `using Microsoft.Extensions.DependencyInjection;` to the top of
`RequirePermissionFilter.cs` for `GetRequiredService`.

Note: `Results.Forbid()` requires an authentication scheme to be configured to produce a
proper 403 vs 401 distinction — Task 7 wires JWT Bearer authentication into `Api`; until
then this compiles and the unit test (which bypasses real auth) still passes because it
calls `InvokeAsync` directly rather than through the full ASP.NET Core pipeline.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/SharedKernel.Tests/SharedKernel.Tests.csproj`
Expected: PASS (2 tests)

- [ ] **Step 7: Commit**

```bash
git add Courier.slnx src/SharedKernel tests/SharedKernel.Tests
git commit -m "feat: add ICurrentTenantProvider, IPermissionChecker, RequirePermission filter"
```

---

### Task 3: Audit Module — AuditLog Entity and IAuditLogger

**Files:**
- Create: `src/Modules/Audit/Domain/AuditLog.cs`
- Create: `src/Modules/Audit/Application/IAuditLogger.cs`
- Create: `src/Modules/Audit/Application/AuditEntry.cs`
- Create: `src/Modules/Audit/Infrastructure/AuditLogConfiguration.cs`
- Create: `src/Modules/Audit/Infrastructure/AuditLogger.cs`
- Create: `tests/Audit.Tests/Audit.Tests.csproj`
- Create: `tests/Audit.Tests/AuditLoggerTests.cs`
- Modify: `Courier.slnx`

**Interfaces:**
- Consumes: nothing new (EF Core base `DbContext`, already referenced).
- Produces: `IAuditLogger { Task LogAsync(AuditEntry entry, CancellationToken ct =
  default); }` and its EF-backed implementation `AuditLogger`. Task 6's `AuthService`
  depends on `IAuditLogger` to log registration/login.

- [ ] **Step 1: Write the domain entity**

`src/Modules/Audit/Domain/AuditLog.cs`:

```csharp
namespace Courier.Audit.Domain;

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? UserId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public bool IsCrossTenantAction { get; set; }
}
```

- [ ] **Step 2: Write the application-layer contract**

`src/Modules/Audit/Application/AuditEntry.cs`:

```csharp
namespace Courier.Audit.Application;

public record AuditEntry(
    Guid? OrganizationId,
    Guid? UserId,
    string Action,
    string EntityType,
    string EntityId,
    string? Before = null,
    string? After = null,
    string? IpAddress = null,
    string? UserAgent = null,
    bool IsCrossTenantAction = false);
```

`src/Modules/Audit/Application/IAuditLogger.cs`:

```csharp
namespace Courier.Audit.Application;

public interface IAuditLogger
{
    Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Scaffold the test project**

```bash
dotnet new xunit -n Audit.Tests -o tests/Audit.Tests
dotnet sln Courier.slnx add tests/Audit.Tests/Audit.Tests.csproj
dotnet add tests/Audit.Tests/Audit.Tests.csproj reference src/Modules/Audit/Audit.csproj
dotnet add tests/Audit.Tests/Audit.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory
```

Delete the generated `tests/Audit.Tests/UnitTest1.cs`.

- [ ] **Step 4: Write the failing test**

`tests/Audit.Tests/AuditLoggerTests.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Audit.Domain;
using Courier.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Audit.Tests;

file class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
    }
}

public class AuditLoggerTests
{
    [Fact]
    public async Task LogAsync_PersistsAuditLogRow()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new TestDbContext(options);
        var logger = new AuditLogger(dbContext);
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await logger.LogAsync(new AuditEntry(orgId, userId, "user.login", "User", userId.ToString()));

        var saved = await dbContext.Set<AuditLog>().SingleAsync();
        Assert.Equal("user.login", saved.Action);
        Assert.Equal(orgId, saved.OrganizationId);
        Assert.Equal(userId, saved.UserId);
        Assert.True(saved.Timestamp > DateTimeOffset.UtcNow.AddMinutes(-1));
    }
}
```

- [ ] **Step 5: Run test to verify it fails**

Run: `dotnet test tests/Audit.Tests/Audit.Tests.csproj`
Expected: FAIL to compile — `AuditLogConfiguration` and `AuditLogger` don't exist yet.

- [ ] **Step 6: Implement the EF configuration and the logger**

`src/Modules/Audit/Infrastructure/AuditLogConfiguration.cs`:

```csharp
using Courier.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Audit.Infrastructure;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
        builder.HasIndex(a => new { a.OrganizationId, a.Timestamp });
    }
}
```

`src/Modules/Audit/Infrastructure/AuditLogger.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Audit.Infrastructure;

public class AuditLogger : IAuditLogger
{
    private readonly DbContext _dbContext;

    public AuditLogger(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<AuditLog>().Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = entry.OrganizationId,
            UserId = entry.UserId,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Before = entry.Before,
            After = entry.After,
            IpAddress = entry.IpAddress,
            UserAgent = entry.UserAgent,
            Timestamp = DateTimeOffset.UtcNow,
            IsCrossTenantAction = entry.IsCrossTenantAction
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/Audit.Tests/Audit.Tests.csproj`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add Courier.slnx src/Modules/Audit tests/Audit.Tests
git commit -m "feat: add AuditLog entity and EF-backed IAuditLogger"
```

---

### Task 4: Identity Domain, Permission Catalog, EF Configurations, AppDbContext, Initial Migration

**Files:**
- Create: `src/Modules/Identity/Domain/Organization.cs`
- Create: `src/Modules/Identity/Domain/User.cs`
- Create: `src/Modules/Identity/Domain/Role.cs`
- Create: `src/Modules/Identity/Domain/Permission.cs`
- Create: `src/Modules/Identity/Domain/UserRole.cs`
- Create: `src/Modules/Identity/Domain/BranchScope.cs`
- Create: `src/Modules/Identity/Domain/RefreshToken.cs`
- Create: `src/Modules/Identity/Application/PermissionCatalog.cs`
- Create: `src/Modules/Identity/Infrastructure/OrganizationConfiguration.cs`
- Create: `src/Modules/Identity/Infrastructure/UserConfiguration.cs`
- Create: `src/Modules/Identity/Infrastructure/RoleConfiguration.cs`
- Create: `src/Modules/Identity/Infrastructure/PermissionConfiguration.cs`
- Create: `src/Modules/Identity/Infrastructure/UserRoleConfiguration.cs`
- Create: `src/Modules/Identity/Infrastructure/RefreshTokenConfiguration.cs`
- Create: `src/Persistence/AppDbContext.cs`
- Create: `src/Persistence/Migrations/` (generated)
- Modify: `src/Api/appsettings.Development.json` (Postgres connection string already
  present from Phase 0 — no change needed, referenced here for clarity)

**Interfaces:**
- Consumes: `ICurrentTenantProvider` (Task 2), `AuditLogConfiguration` pattern (Task 3).
- Produces: `AppDbContext : DbContext` with `DbSet<Organization>`, `DbSet<User>`,
  `DbSet<Role>`, `DbSet<Permission>`, `DbSet<UserRole>`, `DbSet<RefreshToken>`,
  `DbSet<Audit.Domain.AuditLog>`, tenant query filters applied, and a migration that
  creates all tables plus seeds the full `docs/RBAC.md` §3 permission catalog. Task 6's
  `AuthService` and Task 8's `IPermissionChecker` implementation both query through this
  context.

- [ ] **Step 1: Write the domain entities**

`src/Modules/Identity/Domain/Organization.cs`:

```csharp
namespace Courier.Identity.Domain;

public class Organization
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string SubscriptionPlan { get; set; } = "Free";
    public bool IsActive { get; set; } = true;
}
```

`src/Modules/Identity/Domain/User.cs`:

```csharp
namespace Courier.Identity.Domain;

public class User
{
    public Guid Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string FullName { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}
```

`src/Modules/Identity/Domain/Role.cs`:

```csharp
namespace Courier.Identity.Domain;

public class Role
{
    public Guid Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public required string Name { get; set; }
    public bool IsSystemDefault { get; set; }
    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
}
```

`src/Modules/Identity/Domain/Permission.cs`:

```csharp
namespace Courier.Identity.Domain;

public class Permission
{
    public Guid Id { get; set; }
    public required string Key { get; set; }
    public required string Description { get; set; }
}
```

`src/Modules/Identity/Domain/BranchScope.cs`:

```csharp
namespace Courier.Identity.Domain;

public enum BranchScope
{
    SpecificBranches,
    AllBranches
}
```

`src/Modules/Identity/Domain/UserRole.cs`:

```csharp
namespace Courier.Identity.Domain;

public class UserRole
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid OrganizationId { get; set; }
    public BranchScope BranchScope { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public Guid AssignedByUserId { get; set; }
}
```

`src/Modules/Identity/Domain/RefreshToken.cs`:

```csharp
namespace Courier.Identity.Domain;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
}
```

- [ ] **Step 2: Write the permission catalog**

`src/Modules/Identity/Application/PermissionCatalog.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Courier.Identity.Application;

public static class PermissionCatalog
{
    public static readonly IReadOnlyList<(string Key, string Description)> All = new (string, string)[]
    {
        ("shipment.create", "Create shipments"),
        ("shipment.read", "Read shipments"),
        ("shipment.update", "Update shipments"),
        ("shipment.cancel", "Cancel shipments"),
        ("shipment.assign", "Assign shipments to riders"),
        ("shipment.scan", "Scan shipments"),
        ("shipment.sort", "Sort shipments"),
        ("shipment.transfer", "Transfer shipments between branches"),
        ("delivery.assign", "Assign delivery tasks"),
        ("delivery.attempt", "Record delivery attempts"),
        ("delivery.complete", "Complete deliveries"),
        ("ndr.read", "Read NDRs"),
        ("ndr.manage", "Manage NDRs"),
        ("ndr.resolve", "Resolve NDRs"),
        ("rto.read", "Read RTOs"),
        ("rto.manage", "Manage RTOs"),
        ("cod.read", "Read COD records"),
        ("cod.collect", "Collect COD"),
        ("cod.reconcile", "Reconcile COD"),
        ("cod.settle", "Settle COD"),
        ("cod.adjust", "Adjust COD figures"),
        ("manifest.create", "Create manifests"),
        ("manifest.seal", "Seal manifests"),
        ("manifest.dispatch", "Dispatch manifests"),
        ("manifest.receive", "Receive manifests"),
        ("manifest.close", "Close manifests"),
        ("branch.read", "Read branches"),
        ("branch.create", "Create branches"),
        ("branch.update", "Update branches"),
        ("merchant.read", "Read merchants"),
        ("merchant.create", "Create merchants"),
        ("merchant.update", "Update merchants"),
        ("merchant.kyc.review", "Review merchant KYC"),
        ("rider.read", "Read riders"),
        ("rider.create", "Create riders"),
        ("rider.assign", "Assign riders"),
        ("pricing.read", "Read pricing"),
        ("pricing.manage", "Manage pricing"),
        ("report.read", "Read reports"),
        ("report.export", "Export reports"),
        ("user.read", "Read users"),
        ("user.create", "Create users"),
        ("user.update", "Update users"),
        ("role.manage", "Manage roles"),
        ("audit.read", "Read audit logs"),
        ("org.billing.manage", "Manage organization billing"),
        ("org.ownership.transfer", "Transfer organization ownership"),
        ("org.delete", "Delete an organization"),
    };

    public static Guid IdFor(string key)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }
}
```

- [ ] **Step 3: Write the EF configurations**

`src/Modules/Identity/Infrastructure/OrganizationConfiguration.cs`:

```csharp
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Identity.Infrastructure;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Name).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Slug).HasMaxLength(100).IsRequired();
        builder.HasIndex(o => o.Slug).IsUnique();
    }
}
```

`src/Modules/Identity/Infrastructure/UserConfiguration.cs`:

```csharp
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Identity.Infrastructure;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();
    }
}
```

`src/Modules/Identity/Infrastructure/RoleConfiguration.cs`:

```csharp
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Identity.Infrastructure;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.HasMany(r => r.Permissions).WithMany()
            .UsingEntity(j => j.ToTable("RolePermissions"));
    }
}
```

`src/Modules/Identity/Infrastructure/PermissionConfiguration.cs`:

```csharp
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Identity.Infrastructure;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Key).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(300).IsRequired();
        builder.HasIndex(p => p.Key).IsUnique();

        builder.HasData(PermissionCatalog.All.Select(p => new Permission
        {
            Id = PermissionCatalog.IdFor(p.Key),
            Key = p.Key,
            Description = p.Description
        }));
    }
}
```

`src/Modules/Identity/Infrastructure/UserRoleConfiguration.cs`:

```csharp
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Identity.Infrastructure;

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(ur => ur.Id);
        builder.HasIndex(ur => new { ur.UserId, ur.RoleId }).IsUnique();
    }
}
```

`src/Modules/Identity/Infrastructure/RefreshTokenConfiguration.cs`:

```csharp
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Identity.Infrastructure;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(rt => rt.Id);
        builder.Property(rt => rt.TokenHash).IsRequired();
        builder.HasIndex(rt => rt.TokenHash).IsUnique();
    }
}
```

- [ ] **Step 4: Write `AppDbContext` with tenant query filters**

`src/Persistence/AppDbContext.cs`:

```csharp
using Courier.Audit.Domain;
using Courier.Audit.Infrastructure;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Persistence;

public class AppDbContext : DbContext
{
    private readonly ICurrentTenantProvider _currentTenantProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantProvider currentTenantProvider)
        : base(options)
    {
        _currentTenantProvider = currentTenantProvider;
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new PermissionConfiguration());
        modelBuilder.ApplyConfiguration(new UserRoleConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());

        modelBuilder.Entity<User>().HasQueryFilter(u => u.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Role>().HasQueryFilter(r => r.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<UserRole>().HasQueryFilter(ur => ur.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(a => a.OrganizationId == _currentTenantProvider.OrganizationId);
    }
}
```

- [ ] **Step 5: Register a design-time factory for migration tooling**

`dotnet ef migrations add` needs to construct `AppDbContext` without a full DI container.
Create `src/Persistence/AppDbContextFactory.cs`:

```csharp
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Courier.Persistence;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private sealed class DesignTimeTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId => null;
    }

    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password");
        return new AppDbContext(optionsBuilder.Options, new DesignTimeTenantProvider());
    }
}
```

- [ ] **Step 6: Ensure Postgres is running, then generate and apply the migration**

```bash
docker compose up -d postgres
```

Wait for it healthy (`docker compose ps`), then:

```bash
dotnet ef migrations add InitialCreate --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj
dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj --connection "Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password"
```

Expected: migration files appear under `src/Persistence/Migrations/`; `database update`
reports it applied `InitialCreate` with no errors.

- [ ] **Step 7: Verify the seeded permission catalog landed**

```bash
docker compose exec -T postgres psql -U courier -d courier -c "SELECT COUNT(*) FROM \"Permissions\";"
```

Expected: count of `47`.

- [ ] **Step 8: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 9: Commit**

```bash
git add Courier.slnx src/Modules/Identity src/Persistence
git commit -m "feat: add Identity domain entities, EF configurations, AppDbContext, and initial migration"
```

---

### Task 5: Identity Application — Password Hasher and JWT Token Service

**Files:**
- Create: `src/Modules/Identity/Application/IPasswordHasher.cs`
- Create: `src/Modules/Identity/Infrastructure/PasswordHasherAdapter.cs`
- Create: `src/Modules/Identity/Application/IJwtTokenService.cs`
- Create: `src/Modules/Identity/Application/JwtOptions.cs`
- Create: `src/Modules/Identity/Infrastructure/JwtTokenService.cs`
- Create: `tests/Identity.Tests/Identity.Tests.csproj`
- Create: `tests/Identity.Tests/JwtTokenServiceTests.cs`
- Modify: `Courier.slnx`

**Interfaces:**
- Consumes: nothing new.
- Produces: `IPasswordHasher { string Hash(string password); bool Verify(string password,
  string hash); }` and `IJwtTokenService { string IssueAccessToken(Guid userId, Guid
  organizationId, string email); }`. Task 6's `AuthService` consumes both.

- [ ] **Step 1: Scaffold the test project**

```bash
dotnet new xunit -n Identity.Tests -o tests/Identity.Tests
dotnet sln Courier.slnx add tests/Identity.Tests/Identity.Tests.csproj
dotnet add tests/Identity.Tests/Identity.Tests.csproj reference src/Modules/Identity/Identity.csproj
```

Delete the generated `tests/Identity.Tests/UnitTest1.cs`.

- [ ] **Step 2: Write the password hasher contract and adapter**

`src/Modules/Identity/Application/IPasswordHasher.cs`:

```csharp
namespace Courier.Identity.Application;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
```

`src/Modules/Identity/Infrastructure/PasswordHasherAdapter.cs`:

```csharp
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace Courier.Identity.Infrastructure;

public class PasswordHasherAdapter : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(null!, password);

    public bool Verify(string password, string hash)
        => _inner.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;
}
```

(`PasswordHasher<TUser>.HashPassword`/`VerifyHashedPassword` don't actually read the
`user` argument's properties — it exists only for extensibility hooks unrelated to this
usage — passing `null!` is the standard pattern when the salt/algorithm doesn't depend on
user data.)

- [ ] **Step 3: Write the JWT contract, options, and failing test**

`src/Modules/Identity/Application/JwtOptions.cs`:

```csharp
namespace Courier.Identity.Application;

public class JwtOptions
{
    public required string SigningKey { get; set; }
    public required string Issuer { get; set; }
    public required string Audience { get; set; }
    public int AccessTokenMinutes { get; set; } = 15;
}
```

`src/Modules/Identity/Application/IJwtTokenService.cs`:

```csharp
namespace Courier.Identity.Application;

public interface IJwtTokenService
{
    string IssueAccessToken(Guid userId, Guid organizationId, string email);
}
```

`tests/Identity.Tests/JwtTokenServiceTests.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using Courier.Identity.Application;
using Courier.Identity.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Identity.Tests;

public class JwtTokenServiceTests
{
    [Fact]
    public void IssueAccessToken_IncludesExpectedClaims()
    {
        var options = Options.Create(new JwtOptions
        {
            SigningKey = "unit-test-signing-key-at-least-32-bytes-long!",
            Issuer = "courier-tests",
            Audience = "courier-tests",
            AccessTokenMinutes = 15
        });
        var service = new JwtTokenService(options);
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        var token = service.IssueAccessToken(userId, orgId, "owner@example.com");

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(token);
        Assert.Equal(userId.ToString(), parsed.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal(orgId.ToString(), parsed.Claims.Single(c => c.Type == "org_id").Value);
        Assert.Equal("owner@example.com", parsed.Claims.Single(c => c.Type == "email").Value);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj`
Expected: FAIL to compile — `JwtTokenService` doesn't exist yet.

- [ ] **Step 5: Implement the JWT token service**

`src/Modules/Identity/Infrastructure/JwtTokenService.cs`:

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
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string IssueAccessToken(Guid userId, Guid organizationId, string email)
    {
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("org_id", organizationId.ToString()),
            new Claim("email", email)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add Courier.slnx src/Modules/Identity tests/Identity.Tests
git commit -m "feat: add password hasher adapter and JWT access token issuance"
```

---

### Task 6: Identity Application — AuthService (Register, Login, Refresh)

**Files:**
- Create: `src/Modules/Identity/Application/AuthService.cs`
- Create: `src/Modules/Identity/Application/AuthDtos.cs`
- Modify: `tests/Identity.Tests/Identity.Tests.csproj` (add EF Core Npgsql + DI packages)
- Create: `tests/Identity.Tests/AuthServiceTests.cs`

**Interfaces:**
- Consumes: `DbContext` (base type), `IPasswordHasher`, `IJwtTokenService`,
  `IAuditLogger` (Audit module), `PermissionCatalog` (Task 4).
- Produces: `AuthService` with `RegisterOrganizationAsync`, `LoginAsync`,
  `RefreshTokenAsync`. Task 7's Presentation endpoints call these three methods directly.

- [ ] **Step 1: Write the DTOs**

`src/Modules/Identity/Application/AuthDtos.cs`:

```csharp
namespace Courier.Identity.Application;

public record RegisterOrganizationRequest(string OrganizationName, string OrganizationSlug, string OwnerEmail, string OwnerPassword, string OwnerFullName);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record AuthResult(string AccessToken, string RefreshToken, Guid UserId, Guid OrganizationId);

public class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException(string message) : base(message) { }
}
```

- [ ] **Step 2: Add packages needed for the integration test**

```bash
dotnet add tests/Identity.Tests/Identity.Tests.csproj reference src/Persistence/Persistence.csproj
dotnet add tests/Identity.Tests/Identity.Tests.csproj reference src/SharedKernel/SharedKernel.csproj
dotnet add tests/Identity.Tests/Identity.Tests.csproj reference src/Modules/Audit/Audit.csproj
dotnet add tests/Identity.Tests/Identity.Tests.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add tests/Identity.Tests/Identity.Tests.csproj package Microsoft.Extensions.Options
```

- [ ] **Step 3: Write the failing integration test**

`tests/Identity.Tests/AuthServiceTests.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Identity.Application;
using Courier.Identity.Infrastructure;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Identity.Tests;

file class NullTenantProvider : ICurrentTenantProvider
{
    public Guid? OrganizationId => null;
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class AuthServiceTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password")
            .Options;
        return new AppDbContext(options, new NullTenantProvider());
    }

    private static AuthService CreateService(AppDbContext dbContext)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            SigningKey = "identity-tests-signing-key-at-least-32-bytes!",
            Issuer = "courier-tests",
            Audience = "courier-tests"
        });
        return new AuthService(dbContext, new PasswordHasherAdapter(), new JwtTokenService(jwtOptions), new NoopAuditLogger());
    }

    [Fact]
    public async Task RegisterOrganizationAsync_ThenLoginAsync_Succeeds()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Test Org {suffix}", $"test-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");

        var registerResult = await service.RegisterOrganizationAsync(request);
        Assert.NotEqual(Guid.Empty, registerResult.UserId);

        var loginResult = await service.LoginAsync(new LoginRequest(request.OwnerEmail, "P@ssw0rd123!"));
        Assert.Equal(registerResult.UserId, loginResult.UserId);
        Assert.Equal(registerResult.OrganizationId, loginResult.OrganizationId);
        Assert.False(string.IsNullOrWhiteSpace(loginResult.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(loginResult.RefreshToken));
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsAuthenticationFailed()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Test Org {suffix}", $"test-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        await service.RegisterOrganizationAsync(request);

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => service.LoginAsync(new LoginRequest(request.OwnerEmail, "wrong-password")));
    }

    [Fact]
    public async Task RefreshTokenAsync_RotatesToken()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Test Org {suffix}", $"test-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var registerResult = await service.RegisterOrganizationAsync(request);

        var refreshed = await service.RefreshTokenAsync(new RefreshRequest(registerResult.RefreshToken));

        Assert.Equal(registerResult.UserId, refreshed.UserId);
        Assert.NotEqual(registerResult.RefreshToken, refreshed.RefreshToken);

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => service.RefreshTokenAsync(new RefreshRequest(registerResult.RefreshToken)));
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Ensure Postgres is up (`docker compose up -d postgres`), then:
Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter AuthServiceTests`
Expected: FAIL to compile — `AuthService` doesn't exist yet.

- [ ] **Step 5: Implement `AuthService`**

`src/Modules/Identity/Application/AuthService.cs`:

```csharp
using System.Security.Cryptography;
using Courier.Audit.Application;
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class AuthService
{
    private readonly DbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IAuditLogger _auditLogger;

    public AuthService(DbContext dbContext, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _auditLogger = auditLogger;
    }

    public async Task<AuthResult> RegisterOrganizationAsync(RegisterOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        // Pre-authentication: no tenant is known yet, so the global org-scoped
        // query filter must be bypassed for this platform-wide uniqueness check.
        var emailTaken = await _dbContext.Set<User>().IgnoreQueryFilters()
            .AnyAsync(u => u.Email == request.OwnerEmail, cancellationToken);
        if (emailTaken)
        {
            throw new AuthenticationFailedException("Email is already registered.");
        }

        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = request.OrganizationName,
            Slug = request.OrganizationSlug
        };
        _dbContext.Set<Organization>().Add(organization);

        var allPermissions = await _dbContext.Set<Permission>().ToListAsync(cancellationToken);
        var ownerRole = new Role
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Name = "OrganizationOwner",
            IsSystemDefault = true
        };
        _dbContext.Set<Role>().Add(ownerRole);
        // These Permission rows are already tracked (loaded above), so EF Core inserts
        // only the join-table rows here, not new Permission rows.
        foreach (var permission in allPermissions)
        {
            ownerRole.Permissions.Add(permission);
        }

        var owner = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Email = request.OwnerEmail,
            PasswordHash = _passwordHasher.Hash(request.OwnerPassword),
            FullName = request.OwnerFullName
        };
        _dbContext.Set<User>().Add(owner);

        _dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            RoleId = ownerRole.Id,
            OrganizationId = organization.Id,
            BranchScope = BranchScope.AllBranches,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = owner.Id
        });

        var refreshToken = await IssueRefreshTokenAsync(owner.Id, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organization.Id, owner.Id, "organization.registered", "Organization", organization.Id.ToString()), cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(owner.Id, organization.Id, owner.Email);
        return new AuthResult(accessToken, refreshToken.PlainText, owner.Id, organization.Id);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Set<User>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new AuthenticationFailedException("Invalid email or password.");
        }
        if (user.OrganizationId is null)
        {
            throw new AuthenticationFailedException("Platform accounts cannot log in through this endpoint.");
        }

        var refreshToken = await IssueRefreshTokenAsync(user.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(user.OrganizationId, user.Id, "user.login", "User", user.Id.ToString()), cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user.Id, user.OrganizationId.Value, user.Email);
        return new AuthResult(accessToken, refreshToken.PlainText, user.Id, user.OrganizationId.Value);
    }

    public async Task<AuthResult> RefreshTokenAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(request.RefreshToken);
        var existing = await _dbContext.Set<RefreshToken>()
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);

        if (existing is null || existing.RevokedAt is not null || existing.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new AuthenticationFailedException("Refresh token is invalid or expired.");
        }

        var user = await _dbContext.Set<User>().IgnoreQueryFilters()
            .FirstAsync(u => u.Id == existing.UserId, cancellationToken);

        var newRefreshToken = await IssueRefreshTokenAsync(user.Id, cancellationToken);
        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenId = newRefreshToken.Entity.Id;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user.Id, user.OrganizationId!.Value, user.Email);
        return new AuthResult(accessToken, newRefreshToken.PlainText, user.Id, user.OrganizationId.Value);
    }

    private async Task<(RefreshToken Entity, string PlainText)> IssueRefreshTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var plainText = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashToken(plainText),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        _dbContext.Set<RefreshToken>().Add(entity);
        await Task.CompletedTask;
        return (entity, plainText);
    }

    private static string HashToken(string token)
        => Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter AuthServiceTests`
Expected: PASS (3 tests)

- [ ] **Step 7: Run the full test suite to check for regressions**

Run: `dotnet test Courier.slnx`
Expected: all tests pass (Api.Tests's `HealthReadyTests` still requires
`docker compose up -d` for redis/rabbitmq too, per Phase 0's established pattern).

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Identity tests/Identity.Tests
git commit -m "feat: implement AuthService with organization registration, login, and refresh token rotation"
```

---

### Task 7: Wire Auth Into the Api Host

**Files:**
- Modify: `src/Api/Program.cs`
- Modify: `src/Api/appsettings.Development.json`
- Modify: `src/Api/Api.csproj` (project references were added in Task 1 — verify)
- Create: `src/Modules/Identity/Presentation/AuthEndpoints.cs`
- Create: `src/SharedKernel/HttpContextCurrentTenantProvider.cs` (implementation lives here
  since it needs `IHttpContextAccessor`, an ASP.NET Core type already framework-referenced)
- Create: `tests/Api.Tests/AuthEndpointsTests.cs`

**Interfaces:**
- Consumes: `AuthService` (Task 6), `ICurrentTenantProvider` (Task 2).
- Produces: `POST /api/v1/auth/register`, `POST /api/v1/auth/login`,
  `POST /api/v1/auth/refresh`, all reachable over real HTTP, backed by a real Postgres
  `AppDbContext`, with JWT Bearer authentication configured (but not yet required by any
  endpoint except through Task 8's `.RequirePermission(...)`).

- [ ] **Step 1: Implement `ICurrentTenantProvider`**

`src/SharedKernel/HttpContextCurrentTenantProvider.cs`:

```csharp
using Microsoft.AspNetCore.Http;

namespace Courier.SharedKernel;

public class HttpContextCurrentTenantProvider : ICurrentTenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentTenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? OrganizationId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User.FindFirst("org_id")?.Value;
            return Guid.TryParse(claim, out var id) ? id : null;
        }
    }
}
```

- [ ] **Step 2: Write the auth endpoints**

`src/Modules/Identity/Presentation/AuthEndpoints.cs`:

```csharp
using Courier.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Identity.Presentation;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth");

        group.MapPost("/register", async (RegisterOrganizationRequest request, AuthService authService) =>
        {
            try
            {
                var result = await authService.RegisterOrganizationAsync(request);
                return Results.Ok(result);
            }
            catch (AuthenticationFailedException ex)
            {
                return Results.BadRequest(new { error = new { code = "REGISTRATION_FAILED", message = ex.Message } });
            }
        });

        group.MapPost("/login", async (LoginRequest request, AuthService authService) =>
        {
            try
            {
                var result = await authService.LoginAsync(request);
                return Results.Ok(result);
            }
            catch (AuthenticationFailedException ex)
            {
                return Results.Json(new { error = new { code = "LOGIN_FAILED", message = ex.Message } }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        group.MapPost("/refresh", async (RefreshRequest request, AuthService authService) =>
        {
            try
            {
                var result = await authService.RefreshTokenAsync(request);
                return Results.Ok(result);
            }
            catch (AuthenticationFailedException ex)
            {
                return Results.Json(new { error = new { code = "REFRESH_FAILED", message = ex.Message } }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });
    }
}
```

- [ ] **Step 3: Add JWT config to `appsettings.Development.json`**

Add a `Jwt` section (alongside the existing `ConnectionStrings`):

```json
"Jwt": {
  "SigningKey": "dev-only-signing-key-replace-with-a-real-secret-in-production-32b+",
  "Issuer": "courier-api",
  "Audience": "courier-clients",
  "AccessTokenMinutes": 15
}
```

- [ ] **Step 4: Wire everything into `Program.cs`**

Add these `using` statements near the top:

```csharp
using Courier.Audit.Application;
using Courier.Audit.Infrastructure;
using Courier.Identity.Application;
using Courier.Identity.Infrastructure;
using Courier.Identity.Presentation;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
```

After the existing `builder.Services.AddHealthChecks()...` block, add:

```csharp
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenantProvider, HttpContextCurrentTenantProvider>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());

builder.Services.AddScoped<IPasswordHasher, PasswordHasherAdapter>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<AuthService>();

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
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
    });
builder.Services.AddAuthorization();
```

After `var app = builder.Build();`, before `app.UseSerilogRequestLogging();`, add:

```csharp
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}
```

After `app.UseMiddleware<Courier.Api.Middleware.ExceptionHandlingMiddleware>();`, add:

```csharp
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
```

- [ ] **Step 5: Write the failing integration test**

`tests/Api.Tests/AuthEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class AuthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_Login_Refresh_FullFlow_Succeeds()
    {
        var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var registerRequest = new RegisterOrganizationRequest(
            $"E2E Org {suffix}", $"e2e-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        var registerResult = await registerResponse.Content.ReadFromJsonAsync<AuthResult>();
        Assert.NotNull(registerResult);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(registerRequest.OwnerEmail, "P@ssw0rd123!"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(registerResult!.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var registerRequest = new RegisterOrganizationRequest(
            $"E2E Org {suffix}", $"e2e-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(registerRequest.OwnerEmail, "wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Ensure Postgres is up. Run: `ASPNETCORE_ENVIRONMENT=Development dotnet test
tests/Api.Tests/Api.Tests.csproj --filter AuthEndpointsTests`
Expected: FAIL — either a compile error (before Program.cs wiring exists) or a runtime
404, depending on how much of Step 4 is in place; this step exists to confirm the test
harness itself is exercised before declaring success.

- [ ] **Step 7: Run test to verify it passes**

Run: `ASPNETCORE_ENVIRONMENT=Development dotnet test tests/Api.Tests/Api.Tests.csproj
--filter AuthEndpointsTests`
Expected: PASS (2 tests)

- [ ] **Step 8: Run the full Api.Tests suite for regressions**

Run: `ASPNETCORE_ENVIRONMENT=Development dotnet test tests/Api.Tests/Api.Tests.csproj`
(with `docker compose up -d postgres redis rabbitmq` running)
Expected: all tests pass, including the pre-existing `HealthPingTests`,
`ExceptionHandlingMiddlewareTests`, `HealthReadyTests`.

- [ ] **Step 9: Commit**

```bash
git add src/Api src/Modules/Identity/Presentation src/SharedKernel tests/Api.Tests
git commit -m "feat: wire JWT auth and register/login/refresh endpoints into the API host"
```

---

### Task 8: Permission-Checked `GET /api/v1/users` Endpoint

**Files:**
- Create: `src/Modules/Identity/Infrastructure/PermissionChecker.cs`
- Create: `src/Modules/Identity/Presentation/UsersEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/Api.Tests/UsersEndpointsTests.cs`

**Interfaces:**
- Consumes: `IPermissionChecker` (Task 2's interface), `DbContext`.
- Produces: `PermissionChecker : IPermissionChecker` (queries `UserRole` → `Role` →
  `Permission` for the authenticated user, tenant-filtered) and
  `GET /api/v1/users` requiring `user.read`, returning only the caller's organization's
  users. This is the "at least one protected endpoint" required by Phase 1's exit
  criteria.

- [ ] **Step 1: Implement `PermissionChecker`**

`src/Modules/Identity/Infrastructure/PermissionChecker.cs`:

```csharp
using System.Security.Claims;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Infrastructure;

public class PermissionChecker : IPermissionChecker
{
    private readonly DbContext _dbContext;

    public PermissionChecker(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default)
    {
        var subClaim = user.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subClaim, out var userId))
        {
            return false;
        }

        return await _dbContext.Set<UserRole>()
            .Where(ur => ur.UserId == userId)
            .Join(_dbContext.Set<Role>(), ur => ur.RoleId, r => r.Id, (ur, r) => r)
            .SelectMany(r => r.Permissions)
            .AnyAsync(p => p.Key == permission, cancellationToken);
    }
}
```

(This query relies on the `AppDbContext` tenant filter already scoping `UserRole`/`Role`
to the caller's organization via the JWT's `org_id` claim — no explicit
`OrganizationId` check is needed here.)

- [ ] **Step 2: Write the users list endpoint**

`src/Modules/Identity/Presentation/UsersEndpoints.cs`:

```csharp
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Presentation;

public record UserListItem(Guid Id, string Email, string FullName, bool IsActive);

public static class UsersEndpoints
{
    public static void MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/users", async (DbContext dbContext) =>
        {
            var users = await dbContext.Set<User>()
                .Select(u => new UserListItem(u.Id, u.Email, u.FullName, u.IsActive))
                .ToListAsync();
            return Results.Ok(users);
        }).RequirePermission("user.read");
    }
}
```

- [ ] **Step 3: Wire the module's DI and endpoint mapping into `Program.cs`**

Add to the `using` list: `using Courier.Identity.Domain;` is not needed in `Program.cs`
itself. Add this line near the other `builder.Services.AddScoped<...>` calls:

```csharp
builder.Services.AddScoped<IPermissionChecker, Courier.Identity.Infrastructure.PermissionChecker>();
```

Add this line next to `app.MapAuthEndpoints();`:

```csharp
app.MapUsersEndpoints();
```

- [ ] **Step 4: Write the failing tests**

`tests/Api.Tests/UsersEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class UsersEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UsersEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Users Org {suffix}", $"users-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task GetUsers_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_OwnerWithUserReadPermission_Returns200WithSelf()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var users = await response.Content.ReadFromJsonAsync<List<UserListItem>>();
        Assert.Single(users!);
        Assert.Equal(auth.UserId, users![0].Id);
    }
}

file record UserListItem(Guid Id, string Email, string FullName, bool IsActive);
```

- [ ] **Step 5: Run tests to verify they fail**

Ensure Postgres is up. Run: `ASPNETCORE_ENVIRONMENT=Development dotnet test
tests/Api.Tests/Api.Tests.csproj --filter UsersEndpointsTests`
Expected: FAIL — endpoint doesn't exist yet (404) before Steps 1–3 are in place.

- [ ] **Step 6: Run tests to verify they pass**

Run: `ASPNETCORE_ENVIRONMENT=Development dotnet test tests/Api.Tests/Api.Tests.csproj
--filter UsersEndpointsTests`
Expected: PASS (2 tests)

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Identity src/Api/Program.cs tests/Api.Tests/UsersEndpointsTests.cs
git commit -m "feat: add permission-checked GET /api/v1/users endpoint"
```

---

### Task 9: Tenant Isolation Test, Full Verification, Docs Update

**Files:**
- Create: `tests/Api.Tests/TenantIsolationTests.cs`
- Modify: `docs/ROADMAP.md` (mark Phase 1 exit criteria met)

**Interfaces:**
- Consumes: everything from Tasks 1–8.
- Produces: the explicit cross-tenant-rejection test required by Phase 1's exit
  criteria, plus a fully green full-solution test run.

- [ ] **Step 1: Write the tenant isolation test**

`tests/Api.Tests/TenantIsolationTests.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class TenantIsolationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TenantIsolationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12];
        var request = new RegisterOrganizationRequest(
            $"Org {suffix}", $"org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task GetUsers_OrgAOwner_NeverSeesOrgBUsers()
    {
        var client = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(client, "a");
        var orgB = await RegisterOrgAsync(client, "b");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var response = await client.GetAsync("/api/v1/users");
        response.EnsureSuccessStatusCode();

        var users = await response.Content.ReadFromJsonAsync<List<UserListItem>>();
        Assert.Single(users!);
        Assert.Equal(orgA.UserId, users![0].Id);
        Assert.DoesNotContain(users, u => u.Id == orgB.UserId);
    }
}

file record UserListItem(Guid Id, string Email, string FullName, bool IsActive);
```

- [ ] **Step 2: Run the test**

Ensure Postgres is up. Run: `ASPNETCORE_ENVIRONMENT=Development dotnet test
tests/Api.Tests/Api.Tests.csproj --filter TenantIsolationTests`
Expected: PASS — proves org A's owner cannot see org B's users even though both exist in
the same database, satisfying Phase 1's tenant-isolation exit criterion.

- [ ] **Step 3: Run the entire solution's test suite**

```bash
docker compose up -d postgres redis rabbitmq
```

Wait for all three healthy, then:

Run: `ASPNETCORE_ENVIRONMENT=Development dotnet test Courier.slnx`
Expected: every test project passes — `Api.Tests`, `SharedKernel.Tests`, `Audit.Tests`,
`Identity.Tests` — with zero failures.

- [ ] **Step 4: Tear down**

```bash
docker compose down
```

- [ ] **Step 5: Update `docs/ROADMAP.md`**

In the `## Phase 1` section, append a line after the existing "Exit:" sentence:

```markdown
*Status (2026-08-18): Complete.* Organization self-registration, login, refresh token
rotation, permission-checked `GET /api/v1/users`, and an automated tenant-isolation test
are implemented and passing. `SuperAdmin`/platform-level auth remains deferred (see
`CLARIFICATIONS.md` #5) — no phase depends on it yet.
```

- [ ] **Step 6: Commit**

```bash
git add tests/Api.Tests/TenantIsolationTests.cs docs/ROADMAP.md
git commit -m "test: add cross-tenant isolation test; mark Phase 1 exit criteria met"
```

---

## Self-Review Notes

- **Spec coverage:** Organization creation (Task 6/7), user login (Task 6/7), permission
  checks end-to-end (Tasks 2 and 8), tenant isolation proof (Task 9) — all four Phase 1
  exit-criteria items from `docs/ROADMAP.md` are covered by an explicit task and test.
  `UserRole`/`RolePermission`/`AuditLog` (`CLARIFICATIONS.md` #1) are implemented; branch
  scoping (`CLARIFICATIONS.md` #6) is deliberately deferred to Phase 2 since `Branch`
  doesn't exist yet — `UserRole.BranchScope` exists as a column but is not yet consulted
  by any authorization check (there is nothing to scope to).
- **No placeholders:** every step contains complete file contents or exact commands.
- **Type/interface consistency checked:** `IAuditLogger`/`AuditEntry` (Task 3) match the
  call sites in `AuthService` (Task 6); `IPermissionChecker`/`RequirePermission` (Task 2)
  match `PermissionChecker`'s registration and `UsersEndpoints`' usage (Task 8);
  `ICurrentTenantProvider` (Task 2) matches `AppDbContext`'s constructor (Task 4) and
  `HttpContextCurrentTenantProvider` (Task 7); `AuthResult`/`RegisterOrganizationRequest`/
  `LoginRequest`/`RefreshRequest` (Task 6) match every Presentation and test usage in
  Tasks 7–9.
- **Deliberately out of scope, documented above:** SuperAdmin/platform auth,
  role-management CRUD endpoints, an audit-log read endpoint (no consumer needs it yet),
  seeding roles other than `OrganizationOwner`, Redis-backed permission caching.
