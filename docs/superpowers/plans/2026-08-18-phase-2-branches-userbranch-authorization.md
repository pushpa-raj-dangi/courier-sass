# Phase 2 — Branches, UserBranch, Branch Authorization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `docs/ROADMAP.md` Phase 2 — a `Branch` entity (with `ParentBranchId`
hierarchy) and `UserBranch` grants (with `ExpiresAt`) exist, and branch-scope authorization is
layered on top of Phase 1's permission checks, proven by tests covering single-branch,
multi-branch, all-branches, hierarchy, and expired-temporary-grant cases.

**Architecture:** A new `Branches` module (`src/Modules/Branches/Branches.csproj`) owns the
`Branch` entity per `ARCHITECTURE.md` §2's module list. `UserBranch` stays in the `Identity`
module (same source). The dependency runs one direction only — `Identity → Branches` — because
`Identity` already owns the combined "does this user hold this permission, for this branch"
authorization decision (it owns `PermissionChecker` from Phase 1) and needs `Branches`' branch
hierarchy to finish that decision; `Branches` has no reciprocal need for anything in `Identity`.
This keeps the module graph acyclic: `Branches` exposes an `IBranchHierarchyService`
Application-layer interface that `Identity.Infrastructure`'s new `BranchAuthorizationChecker`
consumes, rather than either module reaching into the other's Domain internals directly. The
one place `Branch.OrganizationId` needs an FK to `Organization` (owned by `Identity`) is
`Persistence/AppDbContext.OnModelCreating`, which already references both modules — this avoids
giving `Branches` a reference to `Identity` just for that one constraint.

Geography on `Branch` (`ProvinceId/DistrictId/LocalLevelId/WardNumber`) is deliberately **not**
built this phase — `ROADMAP.md` Phase 3 replaces a free-text placeholder with real FKs once the
Geography module exists. Phase 2's `Branch` carries a plain `AddressLine` string instead.

**Tech Stack:** Same as Phase 1 — EF Core 10 + Npgsql, xUnit + `Microsoft.AspNetCore.Mvc.Testing`
for API-level integration tests against the real Postgres container from `docker-compose.yml`,
`Microsoft.EntityFrameworkCore.InMemory` for pure in-memory unit tests that don't need tenant
query filters (mirrors `tests/Audit.Tests`).

**Spec:** `docs/ROADMAP.md` (Phase 2), `docs/DOMAIN-MODEL.md` (Branches, Identity & Tenancy
sections), `docs/RBAC.md` §1 (authorization decision formula), `docs/ARCHITECTURE.md` §2–3,
`docs/CLARIFICATIONS.md` #2 and #6.

## Global Constraints

- No `if (user.Role == "Admin")` anywhere — branch authorization is an additional check layered
  on top of `IPermissionChecker`, never a replacement for it, per CLAUDE.md §6.
- The authorization decision (`RBAC.md` §1) is:
  `Allow IFF user has a UserRole granting the permission AND (that UserRole.BranchScope ==
  AllBranches OR targetBranchId ∈ user's active UserBranch grants, including descendants of any
  granted parent branch)`. "Active" excludes `UserBranch` rows whose `ExpiresAt` has passed
  (`ExpiresAt IS NULL OR ExpiresAt > now()`).
- `Branch.OrganizationId`/target branch id is never trusted from client input for anything
  beyond "which branch does this request concern" (route parameter) — the *access decision*
  itself is always resolved server-side from the authenticated principal's grants, per
  CLAUDE.md §33.
- Tenant-scoped entities (`Branch`, `UserBranch`) get the same fail-closed EF Core
  `HasQueryFilter` pattern Phase 1 established on `User`/`Role`/`UserRole`/`AuditLog`: an
  explicit `_currentTenantProvider.OrganizationId != null &&` guard, so an unresolved tenant
  matches nothing rather than degrading into `WHERE "OrganizationId" IS NULL`.
- `Branches` module has no project reference to `Identity` (see Architecture above) — do not
  add one. If a future task seems to need it, that is a signal the design needs to change, not
  a signal to add the reference.
- Commit after every task using conventional prefixes (`feat:`, `chore:`, `refactor:`).
- Every integration test in this plan that touches `AppDbContext` requires Postgres running
  (`docker compose up -d postgres`), same as Phase 1.

---

### Task 1: Scaffold Branches Module and Wire Project References

**Files:**
- Create: `src/Modules/Branches/Branches.csproj`
- Create: `tests/Branches.Tests/Branches.Tests.csproj`
- Modify: `Courier.slnx`
- Modify: `src/Modules/Identity/Identity.csproj` (add reference to Branches)
- Modify: `src/Persistence/Persistence.csproj` (add reference to Branches)
- Modify: `src/Api/Api.csproj` (add reference to Branches)
- Modify: `tests/Identity.Tests/Identity.Tests.csproj` (add reference to Branches)

**Interfaces:**
- Consumes: nothing (first task).
- Produces: an empty-but-building `Branches` project wired into the solution with the reference
  graph `SharedKernel, Audit ← Branches`, `SharedKernel, Audit, Branches ← Identity`,
  `..., Branches ← Persistence`, `..., Branches ← Api`. No cycles — `Branches` does not
  reference `Identity`. Later tasks add real code.

- [ ] **Step 1: Create the two projects**

```bash
dotnet new classlib -n Branches -o src/Modules/Branches
dotnet new xunit -n Branches.Tests -o tests/Branches.Tests
dotnet sln Courier.slnx add src/Modules/Branches/Branches.csproj tests/Branches.Tests/Branches.Tests.csproj
```

Delete the template `src/Modules/Branches/Class1.cs` and `tests/Branches.Tests/UnitTest1.cs` —
both projects start empty.

- [ ] **Step 2: Add the ASP.NET Core framework reference to Branches.csproj**

`Branches.Presentation` (added in a later task) needs `IEndpointRouteBuilder`,
`RouteHandlerBuilder`, `HttpContext`. Add to `src/Modules/Branches/Branches.csproj`, inside a
sibling `<ItemGroup>`:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

- [ ] **Step 3: Add EF Core packages**

```bash
dotnet add src/Modules/Branches/Branches.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Modules/Branches/Branches.csproj package Microsoft.EntityFrameworkCore.Relational
dotnet add tests/Branches.Tests/Branches.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory
```

- [ ] **Step 4: Wire project references**

```bash
dotnet add src/Modules/Branches/Branches.csproj reference src/SharedKernel/SharedKernel.csproj
dotnet add src/Modules/Branches/Branches.csproj reference src/Modules/Audit/Audit.csproj
dotnet add src/Modules/Identity/Identity.csproj reference src/Modules/Branches/Branches.csproj
dotnet add src/Persistence/Persistence.csproj reference src/Modules/Branches/Branches.csproj
dotnet add src/Api/Api.csproj reference src/Modules/Branches/Branches.csproj
dotnet add tests/Branches.Tests/Branches.Tests.csproj reference src/Modules/Branches/Branches.csproj
dotnet add tests/Identity.Tests/Identity.Tests.csproj reference src/Modules/Branches/Branches.csproj
```

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add Courier.slnx src/Modules/Branches tests/Branches.Tests src/Modules/Identity/Identity.csproj src/Persistence/Persistence.csproj src/Api/Api.csproj tests/Identity.Tests/Identity.Tests.csproj
git commit -m "chore: scaffold Branches module project and wire references"
```

---

### Task 2: SharedKernel — Branch Authorization Abstractions and Endpoint Filter

**Files:**
- Create: `src/SharedKernel/ClaimsPrincipalExtensions.cs`
- Create: `src/SharedKernel/IBranchAuthorizationChecker.cs`
- Create: `src/SharedKernel/RequireBranchScopedPermissionFilter.cs`
- Create: `tests/SharedKernel.Tests/RequireBranchScopedPermissionFilterTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `ClaimsPrincipalExtensions.GetUserId(this ClaimsPrincipal)` — a small DRY helper; Task 4
    refactors `PermissionChecker` to use it, and `BranchAuthorizationChecker`/
    `BranchesEndpoints` use it from the start instead of duplicating the "sub" claim parse.
  - `IBranchAuthorizationChecker { Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(...);
    Task<bool> HasAccessAsync(...); }` — implemented by `Identity.Infrastructure` in Task 4,
    consumed by the filter here and by `Branches.Application.BranchService` in Task 7.
  - `public static class BranchAccessEndpointExtensions { public static RouteHandlerBuilder
    RequireBranchScopedPermission(this RouteHandlerBuilder builder, string permission, string
    routeParameterName = "id"); }` — Task 8's branch-detail endpoint calls
    `.RequireBranchScopedPermission("branch.read")`.

- [ ] **Step 1: Write the `ClaimsPrincipalExtensions` helper**

`src/SharedKernel/ClaimsPrincipalExtensions.cs`:

```csharp
using System.Security.Claims;

namespace Courier.SharedKernel;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirst("sub")?.Value, out var id) ? id : null;
}
```

- [ ] **Step 2: Write the `IBranchAuthorizationChecker` interface**

`src/SharedKernel/IBranchAuthorizationChecker.cs`:

```csharp
using System.Security.Claims;

namespace Courier.SharedKernel;

public interface IBranchAuthorizationChecker
{
    // Null means the user holds a permission-granting role with BranchScope.AllBranches —
    // callers should treat this as "no branch filter applies", not as "empty set".
    Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default);

    Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Write the failing test for the new endpoint filter**

`tests/SharedKernel.Tests/RequireBranchScopedPermissionFilterTests.cs`:

```csharp
using System.Security.Claims;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class RequireBranchScopedPermissionFilterTests
{
    private sealed class FakeBranchAuthorizationChecker : IBranchAuthorizationChecker
    {
        private readonly bool _result;
        public FakeBranchAuthorizationChecker(bool result) => _result = result;

        public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private static EndpointFilterInvocationContext BuildContext(Guid? routeBranchId, IBranchAuthorizationChecker checker)
    {
        var services = new ServiceCollection();
        services.AddSingleton(checker);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "TestAuth"));
        if (routeBranchId is not null)
        {
            httpContext.Request.RouteValues = new RouteValueDictionary { ["id"] = routeBranchId.Value.ToString() };
        }
        return new DefaultEndpointFilterInvocationContext(httpContext);
    }

    [Fact]
    public async Task InvokeAsync_UserHasBranchAccess_CallsNext()
    {
        var filter = new RequireBranchScopedPermissionFilter("branch.read", "id");
        var context = BuildContext(Guid.NewGuid(), new FakeBranchAuthorizationChecker(true));
        var nextCalled = false;

        var result = await filter.InvokeAsync(context, _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        Assert.True(nextCalled);
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
    }

    [Fact]
    public async Task InvokeAsync_UserLacksBranchAccess_ReturnsForbid()
    {
        var filter = new RequireBranchScopedPermissionFilter("branch.read", "id");
        var context = BuildContext(Guid.NewGuid(), new FakeBranchAuthorizationChecker(false));

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, statusResult.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_MissingRouteValue_ReturnsBadRequest()
    {
        var filter = new RequireBranchScopedPermissionFilter("branch.read", "id");
        var context = BuildContext(routeBranchId: null, new FakeBranchAuthorizationChecker(true));

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, statusResult.StatusCode);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/SharedKernel.Tests/SharedKernel.Tests.csproj`
Expected: FAIL to compile — `RequireBranchScopedPermissionFilter` doesn't exist yet.

- [ ] **Step 5: Implement the filter**

`src/SharedKernel/RequireBranchScopedPermissionFilter.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Courier.SharedKernel;

public class RequireBranchScopedPermissionFilter : IEndpointFilter
{
    private readonly string _permission;
    private readonly string _routeParameterName;

    public RequireBranchScopedPermissionFilter(string permission, string routeParameterName)
    {
        _permission = permission;
        _routeParameterName = routeParameterName;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (!httpContext.Request.RouteValues.TryGetValue(_routeParameterName, out var routeValue) ||
            !Guid.TryParse(routeValue?.ToString(), out var branchId))
        {
            return Results.BadRequest(new
            {
                error = new
                {
                    code = "INVALID_BRANCH_ID",
                    message = $"Route parameter '{_routeParameterName}' is not a valid branch id."
                }
            });
        }

        var checker = httpContext.RequestServices.GetRequiredService<IBranchAuthorizationChecker>();
        var hasAccess = await checker.HasAccessAsync(httpContext.User, _permission, branchId, httpContext.RequestAborted);
        if (!hasAccess)
        {
            return Results.Forbid();
        }

        return await next(context);
    }
}

public static class BranchAccessEndpointExtensions
{
    public static RouteHandlerBuilder RequireBranchScopedPermission(this RouteHandlerBuilder builder, string permission, string routeParameterName = "id")
    {
        return builder
            .RequireAuthorization()
            .AddEndpointFilter(new RequireBranchScopedPermissionFilter(permission, routeParameterName));
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/SharedKernel.Tests/SharedKernel.Tests.csproj`
Expected: PASS (5 tests — 2 existing `RequirePermissionFilterTests` + 3 new)

- [ ] **Step 7: Commit**

```bash
git add src/SharedKernel tests/SharedKernel.Tests
git commit -m "feat: add branch authorization abstractions and RequireBranchScopedPermission filter"
```

---

### Task 3: Branches Module — Domain, Hierarchy Service, EF Configuration

**Files:**
- Create: `src/Modules/Branches/Domain/BranchType.cs`
- Create: `src/Modules/Branches/Domain/Branch.cs`
- Create: `src/Modules/Branches/Application/IBranchHierarchyService.cs`
- Create: `src/Modules/Branches/Infrastructure/BranchConfiguration.cs`
- Create: `src/Modules/Branches/Infrastructure/BranchHierarchyService.cs`
- Create: `tests/Branches.Tests/BranchHierarchyServiceTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `Branch` entity, `BranchConfiguration` (EF mapping, no `Organization` FK — see
  Architecture note above), `IBranchHierarchyService.GetDescendantBranchIdsAsync(Guid, ...)` —
  returns the branch itself plus every descendant (self-inclusive). Task 4's
  `BranchAuthorizationChecker` and Task 5's `AppDbContext` both depend on these.

- [ ] **Step 1: Write the domain entities**

`src/Modules/Branches/Domain/BranchType.cs`:

```csharp
namespace Courier.Branches.Domain;

public enum BranchType
{
    HeadOffice,
    Hub,
    Branch,
    PickupPoint,
    Warehouse
}
```

`src/Modules/Branches/Domain/Branch.cs`:

```csharp
namespace Courier.Branches.Domain;

public class Branch
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public BranchType Type { get; set; }

    // Free-text placeholder for Nepal geography. ROADMAP Phase 3 replaces this with
    // ProvinceId/DistrictId/LocalLevelId/WardNumber FKs into the Geography module, which does
    // not exist yet (CLARIFICATIONS.md #2).
    public required string AddressLine { get; set; }

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? Phone { get; set; }
    public string? OperatingHours { get; set; }
    public Guid? ParentBranchId { get; set; }
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 2: Write the hierarchy service contract**

`src/Modules/Branches/Application/IBranchHierarchyService.cs`:

```csharp
namespace Courier.Branches.Application;

public interface IBranchHierarchyService
{
    // Returns branchId itself plus every descendant branch id (walking ParentBranchId).
    // Scoped to the current tenant automatically via AppDbContext's query filter.
    Task<IReadOnlySet<Guid>> GetDescendantBranchIdsAsync(Guid branchId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Write the EF configuration**

`src/Modules/Branches/Infrastructure/BranchConfiguration.cs`:

```csharp
using Courier.Branches.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Branches.Infrastructure;

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Code).HasMaxLength(20).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.AddressLine).HasMaxLength(500).IsRequired();
        builder.Property(b => b.Phone).HasMaxLength(30);
        builder.Property(b => b.OperatingHours).HasMaxLength(100);

        // Branch codes (e.g. "KTM-HUB") are unique per organization, not globally.
        builder.HasIndex(b => new { b.OrganizationId, b.Code }).IsUnique();

        // Every tenant-filtered query hits this column.
        builder.HasIndex(b => b.OrganizationId);
        builder.HasIndex(b => b.ParentBranchId);

        // Self-referencing hierarchy FK. Restrict: a branch with children cannot be deleted
        // out from under them by a cascading delete.
        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(b => b.ParentBranchId)
            .OnDelete(DeleteBehavior.Restrict);

        // No FK to Organization here: Branches has no project reference to Identity (see this
        // plan's Architecture section). That FK is configured centrally in
        // Persistence/AppDbContext.OnModelCreating instead.
    }
}
```

- [ ] **Step 4: Write the failing test for the hierarchy walk**

`tests/Branches.Tests/BranchHierarchyServiceTests.cs`:

```csharp
using Courier.Branches.Domain;
using Courier.Branches.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Branches.Tests;

file class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new BranchConfiguration());
    }
}

public class BranchHierarchyServiceTests
{
    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    private static Branch NewBranch(Guid id, Guid? parentId = null) => new()
    {
        Id = id,
        OrganizationId = Guid.NewGuid(),
        Code = id.ToString("N")[..8],
        Name = "Test Branch",
        Type = BranchType.Branch,
        AddressLine = "Somewhere",
        ParentBranchId = parentId
    };

    [Fact]
    public async Task GetDescendantBranchIdsAsync_RootWithDescendants_ReturnsSelfAndAllDescendants()
    {
        await using var dbContext = CreateContext();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();

        dbContext.Set<Branch>().AddRange(
            NewBranch(rootId),
            NewBranch(childId, rootId),
            NewBranch(grandchildId, childId),
            NewBranch(unrelatedId));
        await dbContext.SaveChangesAsync();

        var service = new BranchHierarchyService(dbContext);
        var result = await service.GetDescendantBranchIdsAsync(rootId);

        Assert.Equal(new HashSet<Guid> { rootId, childId, grandchildId }, result);
    }

    [Fact]
    public async Task GetDescendantBranchIdsAsync_MidLevelBranch_ExcludesAncestors()
    {
        await using var dbContext = CreateContext();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();

        dbContext.Set<Branch>().AddRange(
            NewBranch(rootId),
            NewBranch(childId, rootId),
            NewBranch(grandchildId, childId));
        await dbContext.SaveChangesAsync();

        var service = new BranchHierarchyService(dbContext);
        var result = await service.GetDescendantBranchIdsAsync(childId);

        Assert.Equal(new HashSet<Guid> { childId, grandchildId }, result);
    }

    [Fact]
    public async Task GetDescendantBranchIdsAsync_LeafBranch_ReturnsOnlySelf()
    {
        await using var dbContext = CreateContext();
        var leafId = Guid.NewGuid();
        dbContext.Set<Branch>().Add(NewBranch(leafId));
        await dbContext.SaveChangesAsync();

        var service = new BranchHierarchyService(dbContext);
        var result = await service.GetDescendantBranchIdsAsync(leafId);

        Assert.Equal(new HashSet<Guid> { leafId }, result);
    }
}
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `dotnet test tests/Branches.Tests/Branches.Tests.csproj`
Expected: FAIL to compile — `BranchHierarchyService` doesn't exist yet.

- [ ] **Step 6: Implement the hierarchy service**

`src/Modules/Branches/Infrastructure/BranchHierarchyService.cs`:

```csharp
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Branches.Infrastructure;

public class BranchHierarchyService : IBranchHierarchyService
{
    private readonly DbContext _dbContext;

    public BranchHierarchyService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlySet<Guid>> GetDescendantBranchIdsAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var branches = await _dbContext.Set<Branch>()
            .Select(b => new { b.Id, b.ParentBranchId })
            .ToListAsync(cancellationToken);

        var childrenByParent = branches
            .Where(b => b.ParentBranchId is not null)
            .GroupBy(b => b.ParentBranchId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(b => b.Id).ToList());

        var result = new HashSet<Guid> { branchId };
        var queue = new Queue<Guid>();
        queue.Enqueue(branchId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenByParent.TryGetValue(current, out var children))
            {
                continue;
            }
            foreach (var childId in children)
            {
                if (result.Add(childId))
                {
                    queue.Enqueue(childId);
                }
            }
        }
        return result;
    }
}
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test tests/Branches.Tests/Branches.Tests.csproj`
Expected: PASS (3 tests)

- [ ] **Step 8: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Branches tests/Branches.Tests
git commit -m "feat: add Branch entity and branch hierarchy service"
```

---

### Task 4: Identity Module — UserBranch Entity and BranchAuthorizationChecker

**Files:**
- Create: `src/Modules/Identity/Domain/UserBranch.cs`
- Create: `src/Modules/Identity/Infrastructure/UserBranchConfiguration.cs`
- Create: `src/Modules/Identity/Infrastructure/BranchAuthorizationChecker.cs`
- Modify: `src/Modules/Identity/Infrastructure/PermissionChecker.cs` (use
  `ClaimsPrincipalExtensions.GetUserId` instead of the inline claim parse)

**Interfaces:**
- Consumes: `IBranchHierarchyService` (Task 3), `IBranchAuthorizationChecker`,
  `ClaimsPrincipalExtensions.GetUserId` (Task 2).
- Produces: `UserBranch` entity, `BranchAuthorizationChecker : IBranchAuthorizationChecker`.
  Task 5's `AppDbContext` gets `DbSet<UserBranch>`; Task 7's `BranchService` and the DI
  registration in Task 8 consume `IBranchAuthorizationChecker`.

- [ ] **Step 1: Write the `UserBranch` entity**

`src/Modules/Identity/Domain/UserBranch.cs`:

```csharp
namespace Courier.Identity.Domain;

public class UserBranch
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BranchId { get; set; }
    public Guid OrganizationId { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid GrantedByUserId { get; set; }
}
```

- [ ] **Step 2: Write the EF configuration**

`src/Modules/Identity/Infrastructure/UserBranchConfiguration.cs`:

```csharp
using Courier.Branches.Domain;
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Identity.Infrastructure;

public class UserBranchConfiguration : IEntityTypeConfiguration<UserBranch>
{
    public void Configure(EntityTypeBuilder<UserBranch> builder)
    {
        builder.ToTable("UserBranches");
        builder.HasKey(ub => ub.Id);

        // Every tenant-filtered query hits this column.
        builder.HasIndex(ub => ub.OrganizationId);
        // Not unique: a new grant row is added after an old one expires, preserving history.
        builder.HasIndex(ub => new { ub.UserId, ub.BranchId });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ub => ub.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(ub => ub.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(ub => ub.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 3: Implement `BranchAuthorizationChecker`**

This is the RBAC.md §1 decision formula. No pre-existing test drives this one (its integration
tests come in Task 6, once the migration in Task 5 has run) — implement directly.

`src/Modules/Identity/Infrastructure/BranchAuthorizationChecker.cs`:

```csharp
using System.Security.Claims;
using Courier.Branches.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Infrastructure;

public class BranchAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly DbContext _dbContext;
    private readonly IBranchHierarchyService _branchHierarchyService;

    public BranchAuthorizationChecker(DbContext dbContext, IBranchHierarchyService branchHierarchyService)
    {
        _dbContext = dbContext;
        _branchHierarchyService = branchHierarchyService;
    }

    public async Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default)
    {
        var userId = user.GetUserId();
        if (userId is null)
        {
            return new HashSet<Guid>();
        }

        // A deactivated user holds no access, mirroring PermissionChecker.
        var isActive = await _dbContext.Set<User>()
            .AnyAsync(u => u.Id == userId && u.IsActive, cancellationToken);
        if (!isActive)
        {
            return new HashSet<Guid>();
        }

        var grantingBranchScopes = await _dbContext.Set<UserRole>()
            .Where(ur => ur.UserId == userId)
            .Where(ur => _dbContext.Set<Role>().Any(r => r.Id == ur.RoleId && r.Permissions.Any(p => p.Key == permission)))
            .Select(ur => ur.BranchScope)
            .ToListAsync(cancellationToken);

        if (grantingBranchScopes.Count == 0)
        {
            // User holds no role granting this permission at all.
            return new HashSet<Guid>();
        }

        if (grantingBranchScopes.Contains(BranchScope.AllBranches))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var grantedBranchIds = await _dbContext.Set<UserBranch>()
            .Where(ub => ub.UserId == userId && (ub.ExpiresAt == null || ub.ExpiresAt > now))
            .Select(ub => ub.BranchId)
            .ToListAsync(cancellationToken);

        var accessible = new HashSet<Guid>();
        foreach (var branchId in grantedBranchIds)
        {
            accessible.UnionWith(await _branchHierarchyService.GetDescendantBranchIdsAsync(branchId, cancellationToken));
        }
        return accessible;
    }

    public async Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default)
    {
        var accessible = await GetAccessibleBranchIdsAsync(user, permission, cancellationToken);
        return accessible is null || accessible.Contains(branchId);
    }
}
```

- [ ] **Step 4: Refactor `PermissionChecker` to use the new claim helper**

In `src/Modules/Identity/Infrastructure/PermissionChecker.cs`, replace:

```csharp
        var subClaim = user.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subClaim, out var userId))
        {
            return false;
        }
```

with:

```csharp
        var userId = user.GetUserId();
        if (userId is null)
        {
            return false;
        }
```

Add `using Courier.SharedKernel;` at the top of the file (it is not yet imported there).

- [ ] **Step 5: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Identity
git commit -m "feat: add UserBranch entity and BranchAuthorizationChecker"
```

---

### Task 5: Persistence — Wire Branch/UserBranch into AppDbContext, Add Migration

**Files:**
- Modify: `src/Persistence/AppDbContext.cs`
- Create: `src/Persistence/Migrations/` (generated)

**Interfaces:**
- Consumes: `Branch`/`BranchConfiguration` (Task 3), `UserBranch`/`UserBranchConfiguration`
  (Task 4).
- Produces: `AppDbContext.Branches`, `AppDbContext.UserBranches`, a migration creating both
  tables plus their FKs/indexes. Task 6's tests and Task 7's `BranchService` both query through
  this context.

- [ ] **Step 1: Update `AppDbContext`**

Replace the full contents of `src/Persistence/AppDbContext.cs`:

```csharp
using Courier.Audit.Domain;
using Courier.Audit.Infrastructure;
using Courier.Branches.Domain;
using Courier.Branches.Infrastructure;
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
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new PermissionConfiguration());
        modelBuilder.ApplyConfiguration(new UserRoleConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new BranchConfiguration());
        modelBuilder.ApplyConfiguration(new UserBranchConfiguration());

        // Branch.OrganizationId -> Organizations FK lives here rather than in
        // BranchConfiguration (Branches module) because Branches deliberately has no project
        // reference to Identity — the dependency runs the other way (Identity -> Branches, for
        // UserBranch's FK to Branch and IBranchHierarchyService). Persistence already
        // references both modules, so it's the one place that can wire this specific
        // constraint without introducing a project cycle.
        modelBuilder.Entity<Branch>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(b => b.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Tenant filters fail CLOSED: the explicit `OrganizationId != null` guard stops EF Core's
        // null-semantics translation from degrading these into `WHERE "OrganizationId" IS NULL`
        // when no tenant is resolved, which would otherwise match org-less rows (planned for
        // future platform/SuperAdmin accounts) instead of matching nothing.
        modelBuilder.Entity<User>().HasQueryFilter(u => _currentTenantProvider.OrganizationId != null && u.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Role>().HasQueryFilter(r => _currentTenantProvider.OrganizationId != null && r.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<UserRole>().HasQueryFilter(ur => _currentTenantProvider.OrganizationId != null && ur.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(a => _currentTenantProvider.OrganizationId != null && a.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Branch>().HasQueryFilter(b => _currentTenantProvider.OrganizationId != null && b.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<UserBranch>().HasQueryFilter(ub => _currentTenantProvider.OrganizationId != null && ub.OrganizationId == _currentTenantProvider.OrganizationId);

        // Defensive: nothing queries Organizations with a tenant context today (registration only
        // inserts), but without this a future `Organizations.ToListAsync()` would enumerate every
        // tenant on the platform.
        modelBuilder.Entity<Organization>().HasQueryFilter(o => _currentTenantProvider.OrganizationId != null && o.Id == _currentTenantProvider.OrganizationId);

        // No tenant filter on RefreshToken: its lookups happen before the caller's tenant is known
        // (see AuthService.RefreshTokenAsync), and rows are keyed by an unguessable SHA-256 token
        // hash rather than being enumerable. Filtering it would break the refresh flow outright.
    }
}
```

- [ ] **Step 2: Ensure Postgres is running, then generate and apply the migration**

```bash
docker compose up -d postgres
```

Wait for it healthy (`docker compose ps`), then:

```bash
dotnet ef migrations add AddBranchesAndUserBranches --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj
dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj --connection "Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password"
```

Expected: a new migration file appears under `src/Persistence/Migrations/`; `database update`
reports it applied `AddBranchesAndUserBranches` with no errors.

- [ ] **Step 3: Verify the new tables exist**

```bash
docker compose exec -T postgres psql -U courier -d courier -c "\d \"Branches\""
docker compose exec -T postgres psql -U courier -d courier -c "\d \"UserBranches\""
```

Expected: both commands print a column listing (not "Did not find any relation").

- [ ] **Step 4: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/Persistence
git commit -m "feat: wire Branch and UserBranch into AppDbContext with migration"
```

---

### Task 6: Identity.Tests — BranchAuthorizationChecker Integration Tests

**Files:**
- Create: `tests/Identity.Tests/TestDbContextFactory.cs`
- Modify: `tests/Identity.Tests/AuthServiceTests.cs` (use the extracted factory instead of its
  own private Postgres-bootstrap helpers)
- Create: `tests/Identity.Tests/BranchAuthorizationCheckerTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (Task 5), `BranchAuthorizationChecker`/`BranchHierarchyService`
  (Tasks 3–4).
- Produces: `TestDbContextFactory.Create(ICurrentTenantProvider)` — a shared Postgres-backed
  `AppDbContext` factory with lazy one-time schema migration, reused by both this file and
  `AuthServiceTests`.

- [ ] **Step 1: Extract the shared test `AppDbContext` factory**

`AuthServiceTests.cs` currently has its own private `GetConnectionString`/`CreateContextCore`/
`EnsureSchema`/`CreateContext`/`SchemaReady` members. `BranchAuthorizationCheckerTests` needs the
exact same Postgres bootstrap, but constructed with a *different* `ICurrentTenantProvider` per
test (a fixed organization id, not always `null`) — extracting this now avoids two copies of the
migration-race-handling logic drifting apart.

`tests/Identity.Tests/TestDbContextFactory.cs`:

```csharp
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Courier.Identity.Tests;

internal static class TestDbContextFactory
{
    // Applied once per test-assembly process. Api.Tests may migrate the same database
    // concurrently (Program.cs auto-migrates in Development); EF Core's migration history
    // table makes a second Migrate() a no-op, and a lost race surfaces as a duplicate-object
    // error that is safe to swallow once the schema is verified to be up to date.
    private static readonly Lazy<bool> SchemaReady = new(EnsureSchema, LazyThreadSafetyMode.ExecutionAndPublication);

    // CI supplies Postgres through ConnectionStrings__Postgres (port 5432 service container);
    // local development falls back to the docker-compose port mapping in this worktree.
    public static string GetConnectionString()
    {
        return Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password";
    }

    public static AppDbContext Create(ICurrentTenantProvider tenantProvider)
    {
        _ = SchemaReady.Value;
        return CreateCore(tenantProvider);
    }

    private static AppDbContext CreateCore(ICurrentTenantProvider tenantProvider)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(GetConnectionString())
            .Options;
        return new AppDbContext(options, tenantProvider);
    }

    private static bool EnsureSchema()
    {
        using var dbContext = CreateCore(new NullTenantProvider());
        try
        {
            dbContext.Database.Migrate();
        }
        catch (PostgresException)
        {
            // Another test assembly may have applied the same migration concurrently. If the
            // schema is now current, the race is benign; otherwise the failure is real.
            if (dbContext.Database.GetPendingMigrations().Any())
            {
                throw;
            }
        }
        return true;
    }

    private sealed class NullTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId => null;
    }
}
```

- [ ] **Step 2: Refactor `AuthServiceTests` to use the shared factory**

Replace the top of `tests/Identity.Tests/AuthServiceTests.cs` (everything from the `using`
block through the end of `CreateService`, i.e. lines 1–81 in the current file) with:

```csharp
using Courier.Audit.Application;
using Courier.Identity.Application;
using Courier.Identity.Infrastructure;
using Courier.Persistence;
using Courier.SharedKernel;
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
    private static AppDbContext CreateContext() => TestDbContextFactory.Create(new NullTenantProvider());

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
```

Leave the three `[Fact]` methods below it (`RegisterOrganizationAsync_ThenLoginAsync_Succeeds`,
`LoginAsync_WrongPassword_ThrowsAuthenticationFailed`, `RefreshTokenAsync_RotatesToken`)
untouched — they already call `CreateContext()`/`CreateService(...)` and need no changes.

- [ ] **Step 3: Run the existing Identity.Tests to confirm the refactor didn't break anything**

Ensure Postgres is up (`docker compose up -d postgres`), then:
Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter AuthServiceTests`
Expected: PASS (3 tests, same as before the refactor)

- [ ] **Step 4: Write the BranchAuthorizationChecker integration tests**

`tests/Identity.Tests/BranchAuthorizationCheckerTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Branches.Domain;
using Courier.Branches.Infrastructure;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Identity.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

public class BranchAuthorizationCheckerTests
{
    private static async Task<Guid> SeedUserAsync(AppDbContext dbContext, Guid organizationId, string permissionKey, BranchScope branchScope)
    {
        dbContext.Set<Organization>().Add(new Organization
        {
            Id = organizationId,
            Name = $"Test Org {organizationId:N}",
            Slug = $"test-org-{organizationId:N}"
        });

        var permission = await dbContext.Set<Permission>().FirstAsync(p => p.Key == permissionKey);
        var role = new Role { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "TestRole", IsSystemDefault = false };
        role.Permissions.Add(permission);
        dbContext.Set<Role>().Add(role);

        var userId = Guid.NewGuid();
        dbContext.Set<User>().Add(new User
        {
            Id = userId,
            OrganizationId = organizationId,
            Email = $"user-{userId:N}@example.com",
            PasswordHash = "not-used-in-this-test",
            FullName = "Test User"
        });

        dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = role.Id,
            OrganizationId = organizationId,
            BranchScope = branchScope,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = userId
        });

        await dbContext.SaveChangesAsync();
        return userId;
    }

    private static Branch NewBranch(Guid organizationId, Guid id, Guid? parentId = null) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        Code = id.ToString("N")[..8],
        Name = "Test Branch",
        Type = BranchType.Branch,
        AddressLine = "Somewhere in Kathmandu",
        ParentBranchId = parentId
    };

    private static ClaimsPrincipal BuildPrincipal(Guid userId)
        => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "TestAuth"));

    private static BranchAuthorizationChecker CreateChecker(AppDbContext dbContext)
        => new(dbContext, new BranchHierarchyService(dbContext));

    [Fact]
    public async Task HasAccessAsync_SingleBranchGrant_AllowsGrantedBranchOnly()
    {
        var organizationId = Guid.NewGuid();
        await using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var userId = await SeedUserAsync(dbContext, organizationId, "branch.read", BranchScope.SpecificBranches);

        var grantedBranchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        dbContext.Set<Branch>().AddRange(
            NewBranch(organizationId, grantedBranchId),
            NewBranch(organizationId, otherBranchId));
        dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(), UserId = userId, BranchId = grantedBranchId, OrganizationId = organizationId,
            GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = userId
        });
        await dbContext.SaveChangesAsync();

        var checker = CreateChecker(dbContext);
        var principal = BuildPrincipal(userId);

        Assert.True(await checker.HasAccessAsync(principal, "branch.read", grantedBranchId));
        Assert.False(await checker.HasAccessAsync(principal, "branch.read", otherBranchId));
    }

    [Fact]
    public async Task HasAccessAsync_MultiBranchGrant_AllowsAllGrantedBranches()
    {
        var organizationId = Guid.NewGuid();
        await using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var userId = await SeedUserAsync(dbContext, organizationId, "branch.read", BranchScope.SpecificBranches);

        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var ungranted = Guid.NewGuid();
        dbContext.Set<Branch>().AddRange(
            NewBranch(organizationId, branchA),
            NewBranch(organizationId, branchB),
            NewBranch(organizationId, ungranted));
        dbContext.Set<UserBranch>().AddRange(
            new UserBranch { Id = Guid.NewGuid(), UserId = userId, BranchId = branchA, OrganizationId = organizationId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = userId },
            new UserBranch { Id = Guid.NewGuid(), UserId = userId, BranchId = branchB, OrganizationId = organizationId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = userId });
        await dbContext.SaveChangesAsync();

        var checker = CreateChecker(dbContext);
        var principal = BuildPrincipal(userId);

        Assert.True(await checker.HasAccessAsync(principal, "branch.read", branchA));
        Assert.True(await checker.HasAccessAsync(principal, "branch.read", branchB));
        Assert.False(await checker.HasAccessAsync(principal, "branch.read", ungranted));
    }

    [Fact]
    public async Task GetAccessibleBranchIdsAsync_AllBranchesScope_ReturnsNullAndAllowsAnyBranch()
    {
        var organizationId = Guid.NewGuid();
        await using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var userId = await SeedUserAsync(dbContext, organizationId, "branch.read", BranchScope.AllBranches);

        var someBranchId = Guid.NewGuid();
        dbContext.Set<Branch>().Add(NewBranch(organizationId, someBranchId));
        await dbContext.SaveChangesAsync();

        var checker = CreateChecker(dbContext);
        var principal = BuildPrincipal(userId);

        var accessible = await checker.GetAccessibleBranchIdsAsync(principal, "branch.read");
        Assert.Null(accessible);
        Assert.True(await checker.HasAccessAsync(principal, "branch.read", someBranchId));
    }

    [Fact]
    public async Task HasAccessAsync_ParentBranchGrant_CoversDescendantBranches()
    {
        var organizationId = Guid.NewGuid();
        await using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var userId = await SeedUserAsync(dbContext, organizationId, "branch.read", BranchScope.SpecificBranches);

        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();
        dbContext.Set<Branch>().AddRange(
            NewBranch(organizationId, parentId),
            NewBranch(organizationId, childId, parentId),
            NewBranch(organizationId, unrelatedId));
        dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(), UserId = userId, BranchId = parentId, OrganizationId = organizationId,
            GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = userId
        });
        await dbContext.SaveChangesAsync();

        var checker = CreateChecker(dbContext);
        var principal = BuildPrincipal(userId);

        Assert.True(await checker.HasAccessAsync(principal, "branch.read", childId));
        Assert.False(await checker.HasAccessAsync(principal, "branch.read", unrelatedId));
    }

    [Fact]
    public async Task HasAccessAsync_ExpiredGrant_IsRejected()
    {
        var organizationId = Guid.NewGuid();
        await using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var userId = await SeedUserAsync(dbContext, organizationId, "branch.read", BranchScope.SpecificBranches);

        var branchId = Guid.NewGuid();
        dbContext.Set<Branch>().Add(NewBranch(organizationId, branchId));
        dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(), UserId = userId, BranchId = branchId, OrganizationId = organizationId,
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-10), ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            GrantedByUserId = userId
        });
        await dbContext.SaveChangesAsync();

        var checker = CreateChecker(dbContext);
        var principal = BuildPrincipal(userId);

        Assert.False(await checker.HasAccessAsync(principal, "branch.read", branchId));
    }
}
```

- [ ] **Step 5: Run the new tests**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter BranchAuthorizationCheckerTests`
Expected: PASS (5 tests, covering single-branch, multi-branch, all-branches, hierarchy, and
expired-grant — the exact scenario list from `ROADMAP.md` Phase 2's exit criteria)

- [ ] **Step 6: Commit**

```bash
git add tests/Identity.Tests
git commit -m "test: add BranchAuthorizationChecker scenario matrix; extract shared test DbContext factory"
```

---

### Task 7: Branches.Application — BranchService

**Files:**
- Create: `src/Modules/Branches/Application/BranchDtos.cs`
- Create: `src/Modules/Branches/Application/BranchService.cs`

**Interfaces:**
- Consumes: `ICurrentTenantProvider` (SharedKernel), `IAuditLogger` (Audit.Application),
  `IBranchAuthorizationChecker` (SharedKernel, implemented in Task 4).
- Produces: `BranchService.CreateBranchAsync/GetBranchAsync/ListBranchesAsync`,
  `BranchValidationException`. Task 8's `BranchesEndpoints` calls these three methods directly.

- [ ] **Step 1: Write the DTOs and validation exception**

`src/Modules/Branches/Application/BranchDtos.cs`:

```csharp
using Courier.Branches.Domain;

namespace Courier.Branches.Application;

public record CreateBranchRequest(
    string Code,
    string Name,
    BranchType Type,
    string AddressLine,
    string? Phone,
    string? OperatingHours,
    decimal? Latitude,
    decimal? Longitude,
    Guid? ParentBranchId);

public record BranchResponse(
    Guid Id,
    string Code,
    string Name,
    BranchType Type,
    string AddressLine,
    string? Phone,
    string? OperatingHours,
    decimal? Latitude,
    decimal? Longitude,
    Guid? ParentBranchId,
    bool IsActive);

public class BranchValidationException : Exception
{
    public BranchValidationException(string message) : base(message) { }
}
```

- [ ] **Step 2: Implement `BranchService`**

`src/Modules/Branches/Application/BranchService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Branches.Application;

public class BranchService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public BranchService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<BranchResponse> CreateBranchAsync(CreateBranchRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new BranchValidationException("No active organization context.");

        var codeTaken = await _dbContext.Set<Branch>()
            .AnyAsync(b => b.Code == request.Code, cancellationToken);
        if (codeTaken)
        {
            throw new BranchValidationException($"Branch code '{request.Code}' is already in use.");
        }

        if (request.ParentBranchId is Guid parentId)
        {
            var parentExists = await _dbContext.Set<Branch>()
                .AnyAsync(b => b.Id == parentId, cancellationToken);
            if (!parentExists)
            {
                throw new BranchValidationException("Parent branch was not found in this organization.");
            }
        }

        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = request.Code,
            Name = request.Name,
            Type = request.Type,
            AddressLine = request.AddressLine,
            Phone = request.Phone,
            OperatingHours = request.OperatingHours,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            ParentBranchId = request.ParentBranchId,
            IsActive = true
        };
        _dbContext.Set<Branch>().Add(branch);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "branch.created", "Branch", branch.Id.ToString()), cancellationToken);

        return ToResponse(branch);
    }

    public async Task<BranchResponse?> GetBranchAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var branch = await _dbContext.Set<Branch>().FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        return branch is null ? null : ToResponse(branch);
    }

    public async Task<List<BranchResponse>> ListBranchesAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, "branch.read", cancellationToken);

        var query = _dbContext.Set<Branch>().AsQueryable();
        if (accessibleBranchIds is not null)
        {
            query = query.Where(b => accessibleBranchIds.Contains(b.Id));
        }

        var branches = await query.ToListAsync(cancellationToken);
        return branches.Select(ToResponse).ToList();
    }

    private static BranchResponse ToResponse(Branch branch) => new(
        branch.Id, branch.Code, branch.Name, branch.Type, branch.AddressLine,
        branch.Phone, branch.OperatingHours, branch.Latitude, branch.Longitude,
        branch.ParentBranchId, branch.IsActive);
}
```

- [ ] **Step 3: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Branches/Application
git commit -m "feat: add BranchService application layer"
```

---

### Task 8: Branches.Presentation — Endpoints, DI Wiring

**Files:**
- Create: `src/Modules/Branches/Presentation/BranchesEndpoints.cs`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: `BranchService` (Task 7), `IBranchAuthorizationChecker`/
  `RequireBranchScopedPermission` (Task 2), `IBranchHierarchyService`/`BranchHierarchyService`
  (Task 3), `BranchAuthorizationChecker` (Task 4).
- Produces: `POST /api/v1/branches`, `GET /api/v1/branches`, `GET /api/v1/branches/{id}` — the
  end-to-end proof of branch-scoped authorization Task 9 tests against.

- [ ] **Step 1: Write the endpoints**

`src/Modules/Branches/Presentation/BranchesEndpoints.cs`:

```csharp
using Courier.Branches.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Branches.Presentation;

public static class BranchesEndpoints
{
    public static void MapBranchesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/branches");

        group.MapPost("/", async (CreateBranchRequest request, BranchService branchService, HttpContext httpContext) =>
        {
            var actingUserId = httpContext.User.GetUserId();
            if (actingUserId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await branchService.CreateBranchAsync(request, actingUserId.Value, httpContext.RequestAborted);
                return Results.Created($"/api/v1/branches/{result.Id}", result);
            }
            catch (BranchValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BRANCH_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("branch.create");

        group.MapGet("/", async (BranchService branchService, HttpContext httpContext) =>
        {
            var branches = await branchService.ListBranchesAsync(httpContext.User, httpContext.RequestAborted);
            return Results.Ok(branches);
        }).RequirePermission("branch.read");

        // Branch-scoped, not just permission-scoped: RequireBranchScopedPermission checks both
        // "does this user hold branch.read" and "is {id} within this user's branch grants" in
        // one filter, so no separate .RequirePermission(...) is chained here.
        group.MapGet("/{id:guid}", async (Guid id, BranchService branchService, CancellationToken cancellationToken) =>
        {
            var branch = await branchService.GetBranchAsync(id, cancellationToken);
            return branch is null ? Results.NotFound() : Results.Ok(branch);
        }).RequireBranchScopedPermission("branch.read");
    }
}
```

- [ ] **Step 2: Wire DI registrations and the endpoint map into `Program.cs`**

In `src/Api/Program.cs`, add these `using` directives near the top (alongside the existing
`Courier.Identity.*` ones):

```csharp
using Courier.Branches.Application;
using Courier.Branches.Infrastructure;
using Courier.Branches.Presentation;
```

Add these two service registrations directly after the existing
`builder.Services.AddScoped<IPermissionChecker, Courier.Identity.Infrastructure.PermissionChecker>();`
line:

```csharp
builder.Services.AddScoped<IBranchHierarchyService, BranchHierarchyService>();
builder.Services.AddScoped<IBranchAuthorizationChecker, Courier.Identity.Infrastructure.BranchAuthorizationChecker>();
builder.Services.AddScoped<BranchService>();
```

Add the endpoint map directly after the existing `app.MapUsersEndpoints();` line:

```csharp
app.MapBranchesEndpoints();
```

- [ ] **Step 3: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Branches/Presentation src/Api/Program.cs
git commit -m "feat: wire branch endpoints and branch authorization DI into the API host"
```

---

### Task 9: Api.Tests — End-to-End Branch Endpoint and Authorization Tests

**Files:**
- Create: `tests/Api.Tests/BranchesEndpointsTests.cs`
- Create: `tests/Api.Tests/BranchAuthorizationEndpointTests.cs`

**Interfaces:**
- Consumes: the live `/api/v1/branches*` endpoints (Task 8) through
  `WebApplicationFactory<Program>`, same pattern as `tests/Api.Tests/UsersEndpointsTests.cs`.
- Produces: nothing further downstream — this is the phase's final proof.

- [ ] **Step 1: Write `BranchesEndpointsTests`**

`tests/Api.Tests/BranchesEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class BranchesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BranchesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Branches Org {suffix}", $"branches-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task PostBranches_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "KTM-HUB", "Kathmandu Hub", BranchType.Hub, "New Road, Kathmandu", null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateThenGet_OwnerWithAllBranchesScope_Succeeds()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "KTM-HUB", "Kathmandu Hub", BranchType.Hub, "New Road, Kathmandu", "01-4444444", "09:00-18:00", 27.7m, 85.3m, null));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.NotNull(created);

        var getResponse = await client.GetAsync($"/api/v1/branches/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal("KTM-HUB", fetched.Code);
    }

    [Fact]
    public async Task CreateBranch_DuplicateCode_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var request = new CreateBranchRequest("DUP-01", "First", BranchType.Branch, "Somewhere", null, null, null, null, null);
        await client.PostAsJsonAsync("/api/v1/branches", request);

        var response = await client.PostAsJsonAsync("/api/v1/branches", request with { Name = "Second" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBranch_NonExistent_Returns404()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync($"/api/v1/branches/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListBranches_ReturnsCreatedBranch()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "LST-01", "List Test Branch", BranchType.Branch, "Somewhere", null, null, null, null, null));

        var response = await client.GetAsync("/api/v1/branches");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var branches = await response.Content.ReadFromJsonAsync<List<BranchResponse>>();
        Assert.Contains(branches!, b => b.Code == "LST-01");
    }
}
```

- [ ] **Step 2: Write `BranchAuthorizationEndpointTests`**

This proves the full HTTP pipeline (`RequireBranchScopedPermission` → `BranchAuthorizationChecker`
→ `BranchHierarchyService`) rejects an out-of-scope request and accepts an in-scope one — including
the hierarchy case (a grant on the parent branch covers the child).

`tests/Api.Tests/BranchAuthorizationEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

public class BranchAuthorizationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BranchAuthorizationEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Scoped Org {suffix}", $"scoped-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task GetBranch_ScopedUser_ForbiddenWithoutGrant_ThenAllowedAfterParentGrant()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);

        var parentResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "PAR-01", "Parent Hub", BranchType.Hub, "Somewhere", null, null, null, null, null));
        var parent = await parentResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var childResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "CHD-01", "Child Branch", BranchType.Branch, "Somewhere Else", null, null, null, null, parent!.Id));
        var child = await childResponse.Content.ReadFromJsonAsync<BranchResponse>();

        // No grant/user-management endpoint exists yet this phase, so the scoped test user's
        // role and UserBranch grant are seeded directly against AppDbContext, exactly like a
        // future "invite user" flow would persist them.
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var permission = await dbContext.Set<Permission>().FirstAsync(p => p.Key == "branch.read");
        var role = new Role { Id = Guid.NewGuid(), OrganizationId = owner.OrganizationId, Name = "ScopedReader", IsSystemDefault = false };
        role.Permissions.Add(permission);
        dbContext.Set<Role>().Add(role);

        var scopedUserId = Guid.NewGuid();
        dbContext.Set<User>().Add(new User
        {
            Id = scopedUserId,
            OrganizationId = owner.OrganizationId,
            Email = $"scoped-{scopedUserId:N}@example.com",
            PasswordHash = "not-used-in-this-test",
            FullName = "Scoped Reader"
        });
        dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = scopedUserId,
            RoleId = role.Id,
            OrganizationId = owner.OrganizationId,
            BranchScope = BranchScope.SpecificBranches,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = owner.UserId
        });
        await dbContext.SaveChangesAsync();

        var scopedToken = jwtTokenService.IssueAccessToken(scopedUserId, owner.OrganizationId, $"scoped-{scopedUserId:N}@example.com");
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopedToken);

        var forbiddenResponse = await scopedClient.GetAsync($"/api/v1/branches/{child.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(),
            UserId = scopedUserId,
            BranchId = parent.Id,
            OrganizationId = owner.OrganizationId,
            GrantedAt = DateTimeOffset.UtcNow,
            GrantedByUserId = owner.UserId
        });
        await dbContext.SaveChangesAsync();

        var allowedResponse = await scopedClient.GetAsync($"/api/v1/branches/{child.Id}");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
    }
}
```

- [ ] **Step 3: Run the full Api.Tests suite**

Ensure Postgres is up (`docker compose up -d postgres`), then:
Run: `dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: PASS (all tests, including the 5 new `BranchesEndpointsTests` and the 1 new
`BranchAuthorizationEndpointTests`)

- [ ] **Step 4: Run the full solution test suite**

Run: `dotnet build Courier.slnx && dotnet test Courier.slnx`
Expected: `Build succeeded`, all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/Api.Tests
git commit -m "test: add end-to-end branch endpoint and branch-scoped authorization tests"
```

---

### Task 10: Update ROADMAP.md Status

**Files:**
- Modify: `docs/ROADMAP.md`

**Interfaces:**
- Consumes: nothing.
- Produces: an updated Phase 2 status note, mirroring Phase 1's, so the next phase's "Entry"
  check has a clear record of what's actually done.

- [ ] **Step 1: Add the status note**

In `docs/ROADMAP.md`, immediately after the existing Phase 2 paragraph:

```text
## Phase 2 — Branches, UserBranch, Branch Authorization

`Branch` (with `ParentBranchId` hierarchy), `UserBranch` (with `ExpiresAt`,
`CLARIFICATIONS.md` #6), branch-scope authorization layered on top of Phase 1's
permission checks.
*Exit:* branch-scoped permission checks proven by tests covering single-branch,
multi-branch, all-branches, hierarchy, and expired-temporary-grant cases.
```

add:

```text

*Status (2026-08-18): Complete.* `Branch` (with `ParentBranchId` hierarchy) and `UserBranch`
(with `ExpiresAt`) are implemented — `Branch` in a new `Branches` module, `UserBranch` in
`Identity` per `ARCHITECTURE.md` §2. Branch-scope authorization
(`IBranchAuthorizationChecker`, `RequireBranchScopedPermission`) is layered on top of Phase 1's
permission checks and exercised end-to-end via `POST/GET /api/v1/branches`. Automated tests
cover single-branch, multi-branch, all-branches, parent-branch-hierarchy, and
expired-temporary-grant authorization scenarios (`tests/Identity.Tests/
BranchAuthorizationCheckerTests.cs`). Geography fields on `Branch` remain a free-text
placeholder (`AddressLine`) pending Phase 3 per this phase's entry note.
```

- [ ] **Step 2: Commit**

```bash
git add docs/ROADMAP.md
git commit -m "docs: mark Phase 2 exit criteria met"
```
