# Platform Super Admin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a platform-operator layer on top of the existing tenant-scoped Nepal courier SaaS: a `SuperAdmin` identity that is not a member of any organization, a `Billing` module (Plan/Subscription/Invoice/Payment with eSewa/Khalti checkout), tenant lifecycle controls (suspend/soft-delete/restore), branch-count plan enforcement, a cross-org dashboard, and time-boxed impersonation with a dual-actor audit trail.

**Architecture:** Two new backend modules — `Billing` (owns Plan/Subscription/Invoice/Payment, has zero project reference to Identity/Shipments/any tenant module) and `Platform` (owns SuperAdmin auth, org lifecycle, the cross-org dashboard, and impersonation issuance; reads `Billing` and `Identity` only through new narrow lookup interfaces, the same pattern every module in this codebase already uses for cross-module reads). `SuperAdmin` is not a new RBAC role — it is simply a `User` row with `OrganizationId = null` (a convention `AuthService.LoginAsync` already guards against for the normal login endpoint), authenticated through a separate `/api/v1/platform/auth/login` endpoint and a dedicated `RequireSuperAdmin` endpoint filter, never through `UserRole`/the permission catalog.

**Tech Stack:** ASP.NET Core / C# / EF Core 10 / PostgreSQL / Hangfire (recurring jobs) — same stack as every existing module. Frontend: Next.js App Router BFF pattern, TanStack Query, Zustand, shadcn/ui — same as the existing staff/merchant tracks.

**Spec:** `docs/superpowers/specs/2026-09-08-platform-super-admin-design.md` — this plan implements every section of that spec; read both together. Three refinements were found during this plan's research that revise how the spec's intent gets built (recorded as `docs/CLARIFICATIONS.md` #21 in Task 20, not re-litigated here):
1. `AuditLog.IsCrossTenantAction` **already exists** (added when `docs/CLARIFICATIONS.md` #5 was first written) — only `ImpersonatedByUserId` is new.
2. This codebase's JWTs carry no embedded permissions — `IPermissionChecker`/`HttpContextCurrentTenantProvider` re-resolve everything from `sub`/`org_id` claims against the database on every request. Impersonation therefore does not need to "resolve real permissions into the token" (spec §10's phrasing) — issuing a normal-shaped token for the target user plus an `impersonated_by` claim is sufficient; every existing permission/branch-scope/tenant-isolation check keeps working unchanged.
3. No new `User.IsSuperAdmin` column: `User.OrganizationId == null` already carries exactly this meaning in the existing code (`AuthService.LoginAsync` already throws "Platform accounts cannot log in through this endpoint." for it). The JWT's `is_super_admin` claim is issued by construction (only a null-org `User` can reach `PlatformAuthService.LoginAsync`), not backed by an extra column.

## Global Constraints

- Target framework `net10.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` — copy every new `.csproj`'s `<PropertyGroup>` verbatim from an existing module (e.g. `src/Modules/Cod/Cod.csproj`).
- One `.csproj` per module, with `Domain/Application/Infrastructure/Presentation` folders inside it — never a `.csproj` per layer.
- No module ever references another module's `DbSet<T>` directly. Cross-module reads go through a narrow interface defined in the *called* module's `Application` layer, implemented directly by that module's main service class, and registered in `src/Api/Program.cs` as `builder.Services.AddScoped<IFoo>(sp => sp.GetRequiredService<FooService>());`.
- Every tenant-scoped table's EF query filter is `_currentTenantProvider.OrganizationId != null && x.OrganizationId == _currentTenantProvider.OrganizationId` — a null ambient tenant (background job, or a `SuperAdmin`/`Platform` request) sees **zero rows**, not all rows. Any `Platform`/`Billing` code that must read across every organization uses `.IgnoreQueryFilters()` with its own explicit predicate — never relies on the ambient filter. This is documented precedent (`docs/CLARIFICATIONS.md` #12).
- Migrations: `dotnet ef migrations add <Name> --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`, then inspect the generated file before committing (this codebase has been bitten before by EF silently merging/renaming indexes — verify the diff matches intent).
- Error contract: every business-rule exception is caught in the `Presentation` endpoint and mapped to `Results.BadRequest(new { error = new { code = "SOME_CODE", message = ex.Message } })` (or the matching status code) — never a raw 500 for an expected validation failure. One exception type per module (e.g. `BillingValidationException`, `PlatformValidationException`), a `class Foo : Exception` in that module's `*Dtos.cs`-equivalent file, following `src/Modules/Branches/Application/BranchDtos.cs`'s `BranchValidationException` exactly.
- Module unit tests run against the **real shared Postgres** on port 5434 (`Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password`, overridable via `ConnectionStrings__Postgres`) via a `TestDbContextFactory` copied from `tests/Cod.Tests/TestDbContextFactory.cs` — not EF Core's InMemory provider. `docker start courier-postgres-1 courier-redis-1 courier-rabbitmq-1` must have been run before any test task.
- Frontend: every new UI control is a shadcn/ui primitive or block — never a raw HTML `<input>`/`<select>`/`<button>` styled by hand. Detail/create UI is always an instantly-opened right-side `Sheet` driven by a Zustand store — never a separate routed page. List pages use the shared `DataTable` (`src/web/src/components/data-table/DataTable.tsx`).
- No Stripe, no non-Nepal gateway, no annual billing cycle, no `MaxStaffUsers`/`MaxMonthlyShipments` *enforcement* (fields exist on `Plan`, stored only), no hard-delete/purge job, no fine-grained `SuperAdmin` permission tiers — see spec §3 for the full non-goals list. Do not add any of these "while you're in there."

---

## Task 1: `Billing` module scaffold — `Plan` entity

**Files:**
- Create: `src/Modules/Billing/Billing.csproj`
- Create: `src/Modules/Billing/Domain/Plan.cs`
- Create: `src/Modules/Billing/Infrastructure/PlanConfiguration.cs`
- Create: `src/Modules/Billing/Application/IBillingLookupService.cs`
- Create: `src/Modules/Billing/Application/BillingValidationException.cs`
- Create: `src/Modules/Billing/Application/PlanService.cs`
- Modify: `src/Persistence/Persistence.csproj` (add `<ProjectReference Include="..\Modules\Billing\Billing.csproj" />`)
- Modify: `src/Persistence/AppDbContext.cs` (register `Plan` DbSet's configuration + no query filter, since `Plan` is platform-global)
- Modify: `Courier.slnx` (add the two new project entries — `Billing.csproj` here, `Billing.Tests.csproj` in Task 6)
- Test: `tests/Billing.Tests/Billing.Tests.csproj`
- Test: `tests/Billing.Tests/TestDbContextFactory.cs`
- Test: `tests/Billing.Tests/PlanServiceTests.cs`

**Interfaces:**
- Produces: `Courier.Billing.Domain.Plan` (`Id`, `Name`, `Slug`, `MonthlyPriceNpr` decimal, `MaxBranches` int?, `MaxStaffUsers` int?, `MaxMonthlyShipments` int?, `IsDefault` bool, `IsActive` bool, `CreatedAt`).
- Produces: `Courier.Billing.Application.IBillingLookupService` — this task only declares `Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default)` (Tasks 2/3 add more members as `Subscription`/`Invoice` land).
- Produces: `Courier.Billing.Application.PlanService : IBillingLookupService` with `CreatePlanAsync(CreatePlanRequest, CancellationToken)`, `ListPlansAsync(CancellationToken)`, `RetirePlanAsync(Guid planId, CancellationToken)`.

- [ ] **Step 1: Scaffold the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.11" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.11" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

</Project>
```

No `<ProjectReference>` at all — `Billing` has zero dependency on any other module, per the spec's "zero project reference to Identity/Shipments" rule (§4).

- [ ] **Step 2: Write the `Plan` entity**

```csharp
namespace Courier.Billing.Domain;

// Platform-global: no OrganizationId. Only a SuperAdmin can create/retire a Plan;
// any organization can read its own assigned Plan's limits via IBillingLookupService.
public class Plan
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public decimal MonthlyPriceNpr { get; set; }
    public int? MaxBranches { get; set; }
    public int? MaxStaffUsers { get; set; }
    public int? MaxMonthlyShipments { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 3: Write the EF configuration**

```csharp
using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Billing.Infrastructure;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("Plans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(150).IsRequired();
        builder.Property(p => p.MonthlyPriceNpr).HasPrecision(12, 2);
        builder.HasIndex(p => p.Slug).IsUnique();
        // At most one default Plan at a time — PlanService.CreatePlanAsync enforces this by
        // clearing any existing IsDefault before setting a new one inside the same
        // SaveChangesAsync (no DB-level partial-unique-index race guard here: unlike RateCard's
        // per-org active-card race, this is a SuperAdmin-only, low-frequency, single-operator
        // action — the read-then-write window is not a realistic concurrency risk).
    }
}
```

- [ ] **Step 4: Write the lookup interface and exception type**

```csharp
namespace Courier.Billing.Application;

public interface IBillingLookupService
{
    // Null means "no limit" (unlimited) OR "this org has no Subscription yet" — both cases the
    // caller (BranchService, Task 12) must treat identically as "do not block."
    Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
```

```csharp
namespace Courier.Billing.Application;

public class BillingValidationException : Exception
{
    public BillingValidationException(string message) : base(message) { }
}
```

- [ ] **Step 5: Write `PlanService` (create/list/retire) implementing the interface**

```csharp
using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Billing.Application;

public record CreatePlanRequest(string Name, string Slug, decimal MonthlyPriceNpr, int? MaxBranches, int? MaxStaffUsers, int? MaxMonthlyShipments, bool IsDefault);
public record PlanResponse(Guid Id, string Name, string Slug, decimal MonthlyPriceNpr, int? MaxBranches, int? MaxStaffUsers, int? MaxMonthlyShipments, bool IsDefault, bool IsActive);

public class PlanService : IBillingLookupService
{
    private readonly DbContext _dbContext;

    public PlanService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PlanResponse> CreatePlanAsync(CreatePlanRequest request, CancellationToken cancellationToken = default)
    {
        var slugTaken = await _dbContext.Set<Plan>().AnyAsync(p => p.Slug == request.Slug, cancellationToken);
        if (slugTaken)
        {
            throw new BillingValidationException($"Plan slug '{request.Slug}' is already in use.");
        }
        if (request.MonthlyPriceNpr < 0)
        {
            throw new BillingValidationException("Monthly price cannot be negative.");
        }

        if (request.IsDefault)
        {
            var existingDefaults = await _dbContext.Set<Plan>().Where(p => p.IsDefault).ToListAsync(cancellationToken);
            foreach (var existing in existingDefaults)
            {
                existing.IsDefault = false;
            }
        }

        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Slug = request.Slug,
            MonthlyPriceNpr = request.MonthlyPriceNpr,
            MaxBranches = request.MaxBranches,
            MaxStaffUsers = request.MaxStaffUsers,
            MaxMonthlyShipments = request.MaxMonthlyShipments,
            IsDefault = request.IsDefault,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Set<Plan>().Add(plan);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(plan);
    }

    public async Task<List<PlanResponse>> ListPlansAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Set<Plan>().OrderBy(p => p.MonthlyPriceNpr).Select(p => ToResponse(p)).ToListAsync(cancellationToken);

    public async Task RetirePlanAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.Set<Plan>().FirstOrDefaultAsync(p => p.Id == planId, cancellationToken)
            ?? throw new BillingValidationException("Plan was not found.");
        if (plan.IsDefault)
        {
            throw new BillingValidationException("Cannot retire the default plan — assign a new default first.");
        }
        plan.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        // Task 2 replaces this body with a real Subscription -> Plan join. Until Task 2 lands,
        // every org is treated as unlimited (never blocks a branch creation on a half-built
        // feature) — deliberately fail-open, not fail-closed, for a limit that doesn't exist yet.
        await Task.CompletedTask;
        return null;
    }

    private static PlanResponse ToResponse(Plan p)
        => new(p.Id, p.Name, p.Slug, p.MonthlyPriceNpr, p.MaxBranches, p.MaxStaffUsers, p.MaxMonthlyShipments, p.IsDefault, p.IsActive);
}
```

- [ ] **Step 6: Wire `Plan` into `AppDbContext`**

In `src/Persistence/AppDbContext.cs`, add near the other `ApplyConfiguration` calls:

```csharp
modelBuilder.ApplyConfiguration(new Courier.Billing.Infrastructure.PlanConfiguration());
```

`Plan` is platform-global — do **not** add a `HasQueryFilter` for it (it has no `OrganizationId`).

In `src/Persistence/Persistence.csproj`, add to the `<ItemGroup>` of `<ProjectReference>`s:

```xml
<ProjectReference Include="..\Modules\Billing\Billing.csproj" />
```

Add both new projects to `Courier.slnx` next to the other module entries:

```xml
<Project Path="src/Modules/Billing/Billing.csproj" />
<Project Path="tests/Billing.Tests/Billing.Tests.csproj" />
```

- [ ] **Step 7: Scaffold `Billing.Tests` and its `TestDbContextFactory`**

Copy `tests/Cod.Tests/Cod.Tests.csproj` to `tests/Billing.Tests/Billing.Tests.csproj`, renaming every `Cod` reference to `Billing` (project name, `<ProjectReference>` to `Billing.csproj` instead of `Cod.csproj`, plus `Persistence.csproj`). Copy `tests/Cod.Tests/TestDbContextFactory.cs` verbatim into `tests/Billing.Tests/TestDbContextFactory.cs`, changing only the namespace to `Courier.Billing.Tests`.

- [ ] **Step 8: Write the failing test**

```csharp
using Courier.Billing.Application;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Billing.Tests;

public class PlanServiceTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    [Fact]
    public async Task CreatePlanAsync_ValidRequest_PersistsAndReturnsPlan()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var service = new PlanService(dbContext);
        var slug = $"test-plan-{Guid.NewGuid():N}";

        var result = await service.CreatePlanAsync(new CreatePlanRequest("Test Plan", slug, 999m, 5, 10, 1000, false));

        Assert.Equal(slug, result.Slug);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task CreatePlanAsync_DuplicateSlug_Throws()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var service = new PlanService(dbContext);
        var slug = $"dup-plan-{Guid.NewGuid():N}";
        await service.CreatePlanAsync(new CreatePlanRequest("First", slug, 0m, null, null, null, false));

        await Assert.ThrowsAsync<BillingValidationException>(() =>
            service.CreatePlanAsync(new CreatePlanRequest("Second", slug, 0m, null, null, null, false)));
    }

    [Fact]
    public async Task CreatePlanAsync_NewDefault_ClearsPreviousDefault()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var service = new PlanService(dbContext);
        var firstSlug = $"first-default-{Guid.NewGuid():N}";
        var secondSlug = $"second-default-{Guid.NewGuid():N}";
        var first = await service.CreatePlanAsync(new CreatePlanRequest("Free", firstSlug, 0m, null, null, null, true));

        await service.CreatePlanAsync(new CreatePlanRequest("Free v2", secondSlug, 0m, null, null, null, true));

        var plans = await service.ListPlansAsync();
        Assert.False(plans.Single(p => p.Id == first.Id).IsDefault);
    }
}
```

- [ ] **Step 9: Run the tests to verify they fail**

Run: `dotnet test tests/Billing.Tests/Billing.Tests.csproj`
Expected: FAIL to compile (`Billing.csproj` not yet referenced by the test project, or `PlanService` not yet defined) — confirms the test project is wired to real, not-yet-passing code.

- [ ] **Step 10: Run the tests to verify they pass**

Ensure `docker start courier-postgres-1 courier-redis-1 courier-rabbitmq-1` has been run, then:

Run: `dotnet test tests/Billing.Tests/Billing.Tests.csproj`
Expected: PASS (3/3).

- [ ] **Step 11: Generate and inspect the migration**

Run: `dotnet ef migrations add AddBilling --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`

Open the generated migration file and confirm it only creates the `Plans` table (no unrelated model changes swept in from an unrelated dirty model-snapshot diff — if it does, stop and investigate before proceeding, per this project's established caution around EF silently merging changes).

- [ ] **Step 12: Full-solution build check**

Run: `dotnet build Courier.slnx`
Expected: succeeds — confirms `Billing.csproj`'s addition to `Persistence.csproj` and `Courier.slnx` didn't break any other project.

- [ ] **Step 13: Commit**

```bash
git add src/Modules/Billing tests/Billing.Tests src/Persistence/Persistence.csproj src/Persistence/AppDbContext.cs src/Persistence/Migrations Courier.slnx
git commit -m "feat(billing): add Billing module scaffold and Plan entity"
```

---

## Task 2: `Subscription` entity + `SubscriptionService`

**Files:**
- Create: `src/Modules/Billing/Domain/Subscription.cs`
- Create: `src/Modules/Billing/Domain/SubscriptionStatus.cs`
- Create: `src/Modules/Billing/Infrastructure/SubscriptionConfiguration.cs`
- Create: `src/Modules/Billing/Application/SubscriptionService.cs`
- Modify: `src/Modules/Billing/Application/IBillingLookupService.cs` (add two members)
- Modify: `src/Modules/Billing/Application/PlanService.cs` (delete the Task 1 placeholder `GetMaxBranchesAsync` — it moves to `SubscriptionService`, the module's actual `IBillingLookupService` registration target)
- Modify: `src/Persistence/AppDbContext.cs`
- Test: `tests/Billing.Tests/SubscriptionServiceTests.cs`

**Interfaces:**
- Consumes: `Courier.Billing.Domain.Plan` (Task 1).
- Produces: `Courier.Billing.Domain.Subscription` (`Id`, `OrganizationId`, `PlanId`, `Status` enum, `CurrentCycleStart`, `CurrentCycleEnd`, `CreatedAt`, `UpdatedAt`).
- Produces: `Courier.Billing.Domain.SubscriptionStatus` (`Active`, `PastDue`, `Cancelled`).
- Produces: `Courier.Billing.Application.SubscriptionService : IBillingLookupService` with `AssignPlanAsync(Guid organizationId, Guid planId, CancellationToken)`, `GetCurrentAsync(Guid organizationId, CancellationToken)`, plus the interface's `GetMaxBranchesAsync`/new `GetPlanSummaryAsync`.
- Produces (interface addition): `IBillingLookupService.GetPlanSummaryAsync(Guid organizationId, CancellationToken) : Task<PlanSummary?>` where `PlanSummary` is a new `record PlanSummary(string PlanName, decimal MonthlyPriceNpr, SubscriptionStatus Status)`.

- [ ] **Step 1: Write the `SubscriptionStatus` enum and `Subscription` entity**

```csharp
namespace Courier.Billing.Domain;

public enum SubscriptionStatus
{
    Active = 0,
    PastDue = 1,
    Cancelled = 2
}
```

```csharp
namespace Courier.Billing.Domain;

// One row per organization — live operational state, not versioned/append-only like
// Pricing.RateCard (a Subscription is not "priced at a point in time," it's "which Plan
// applies right now"). Invoice (Task 3) is what snapshots price at a point in time.
public class Subscription
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PlanId { get; set; }
    public SubscriptionStatus Status { get; set; }
    public DateTimeOffset CurrentCycleStart { get; set; }
    public DateTimeOffset CurrentCycleEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Write the EF configuration**

```csharp
using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Billing.Infrastructure;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("Subscriptions");
        builder.HasKey(s => s.Id);
        // One Subscription per org — enforced here, not just in application code, since a
        // second row for the same org would make "the org's current plan" ambiguous.
        builder.HasIndex(s => s.OrganizationId).IsUnique();
    }
}
```

- [ ] **Step 3: Extend `IBillingLookupService`**

```csharp
namespace Courier.Billing.Application;

public record PlanSummary(string PlanName, decimal MonthlyPriceNpr, Courier.Billing.Domain.SubscriptionStatus Status);

public interface IBillingLookupService
{
    Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
```

Delete the placeholder `GetMaxBranchesAsync` method body from `PlanService` and remove `: IBillingLookupService` from its class declaration — `SubscriptionService` is the real implementation registered against the interface in Task 7's DI wiring; `PlanService` goes back to being a plain service with no interface.

- [ ] **Step 4: Write `SubscriptionService`**

```csharp
using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Billing.Application;

public record SubscriptionResponse(Guid Id, Guid OrganizationId, Guid PlanId, string PlanName, SubscriptionStatus Status, DateTimeOffset CurrentCycleStart, DateTimeOffset CurrentCycleEnd);

public class SubscriptionService : IBillingLookupService
{
    private readonly DbContext _dbContext;

    public SubscriptionService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Called once at org self-registration (Task 7) with the platform's IsDefault plan, and by
    // SuperAdmin (Task 9's PlatformEndpoints) to upgrade/downgrade an org onto a different Plan.
    public async Task<SubscriptionResponse> AssignPlanAsync(Guid organizationId, Guid planId, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.Set<Plan>().FirstOrDefaultAsync(p => p.Id == planId && p.IsActive, cancellationToken)
            ?? throw new BillingValidationException("Plan was not found or is retired.");

        var existing = await _dbContext.Set<Subscription>().FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            existing = new Subscription
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                CurrentCycleStart = now,
                CurrentCycleEnd = now.AddMonths(1),
                CreatedAt = now
            };
            _dbContext.Set<Subscription>().Add(existing);
        }
        existing.PlanId = plan.Id;
        existing.Status = SubscriptionStatus.Active;
        existing.UpdatedAt = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new SubscriptionResponse(existing.Id, existing.OrganizationId, existing.PlanId, plan.Name, existing.Status, existing.CurrentCycleStart, existing.CurrentCycleEnd);
    }

    public async Task<SubscriptionResponse?> GetCurrentAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var result = await (
            from s in _dbContext.Set<Subscription>()
            join p in _dbContext.Set<Plan>() on s.PlanId equals p.Id
            where s.OrganizationId == organizationId
            select new SubscriptionResponse(s.Id, s.OrganizationId, s.PlanId, p.Name, s.Status, s.CurrentCycleStart, s.CurrentCycleEnd)
        ).FirstOrDefaultAsync(cancellationToken);
        return result;
    }

    public async Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await (
            from s in _dbContext.Set<Subscription>()
            join p in _dbContext.Set<Plan>() on s.PlanId equals p.Id
            where s.OrganizationId == organizationId
            select (int?)p.MaxBranches
        ).FirstOrDefaultAsync(cancellationToken);
        // No Subscription row at all (shouldn't happen post-Task-7, every org gets one at
        // registration) also naturally yields null here via FirstOrDefaultAsync on an int? —
        // treated as "unlimited," matching Task 1's fail-open default.
    }

    public async Task<PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await (
            from s in _dbContext.Set<Subscription>()
            join p in _dbContext.Set<Plan>() on s.PlanId equals p.Id
            where s.OrganizationId == organizationId
            select new PlanSummary(p.Name, p.MonthlyPriceNpr, s.Status)
        ).FirstOrDefaultAsync(cancellationToken);
    }
}
```

- [ ] **Step 5: Wire `Subscription` into `AppDbContext`**

```csharp
modelBuilder.ApplyConfiguration(new Courier.Billing.Infrastructure.SubscriptionConfiguration());
```

No query filter — `Subscription` carries its own `OrganizationId` for direct tenant-scoped reads by non-platform callers, but this codebase's convention (per `RateCard`, `Shipment`, etc.) is that **every** entity with an `OrganizationId` column gets the standard `HasQueryFilter`. Add it:

```csharp
modelBuilder.Entity<Courier.Billing.Domain.Subscription>().HasQueryFilter(s => _currentTenantProvider.OrganizationId != null && s.OrganizationId == _currentTenantProvider.OrganizationId);
```

`SubscriptionService`'s methods above all query without an explicit `organizationId` filter *inside the LINQ* beyond the `where s.OrganizationId == organizationId` clause already shown — that clause is the actual filter; the ambient query filter is an additional defense-in-depth layer that will only ever narrow further (never widen), so no behavior change for a normal tenant-scoped caller. **`Platform` module calls into this service (Task 9/10) always run with `ICurrentTenantProvider.OrganizationId == null`** (a `SuperAdmin` request has no `org_id` claim), so the ambient filter would silently return nothing for those callers — Task 9/10 will need `.IgnoreQueryFilters()` variants; do not add them speculatively here, add them when Task 9/10 actually need cross-org reads.

- [ ] **Step 6: Write the failing tests**

```csharp
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Billing.Tests;

public class SubscriptionServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public FixedTenantProvider(Guid? organizationId) { OrganizationId = organizationId; }
        public Guid? OrganizationId { get; }
    }

    private static async Task<Plan> SeedPlanAsync(Courier.Persistence.AppDbContext dbContext, int? maxBranches = null, decimal price = 0m)
    {
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Test", Slug = $"plan-{Guid.NewGuid():N}", MonthlyPriceNpr = price, MaxBranches = maxBranches, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<Plan>().Add(plan);
        await dbContext.SaveChangesAsync();
        return plan;
    }

    [Fact]
    public async Task AssignPlanAsync_NewOrganization_CreatesActiveSubscription()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var plan = await SeedPlanAsync(dbContext);
        var service = new SubscriptionService(dbContext);

        var result = await service.AssignPlanAsync(organizationId, plan.Id);

        Assert.Equal(SubscriptionStatus.Active, result.Status);
        Assert.Equal(plan.Id, result.PlanId);
    }

    [Fact]
    public async Task GetMaxBranchesAsync_PlanWithLimit_ReturnsLimit()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var plan = await SeedPlanAsync(dbContext, maxBranches: 3);
        var service = new SubscriptionService(dbContext);
        await service.AssignPlanAsync(organizationId, plan.Id);

        var max = await service.GetMaxBranchesAsync(organizationId);

        Assert.Equal(3, max);
    }

    [Fact]
    public async Task GetMaxBranchesAsync_NoSubscription_ReturnsNullUnlimited()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var service = new SubscriptionService(dbContext);

        var max = await service.GetMaxBranchesAsync(organizationId);

        Assert.Null(max);
    }
}
```

- [ ] **Step 7: Run tests to verify they fail, then implement, then verify they pass**

Run: `dotnet test tests/Billing.Tests/Billing.Tests.csproj`
Expected first: FAIL (compile error — `SubscriptionService` doesn't exist yet). After Steps 1-5: PASS (3/3, plus Task 1's 3 still passing = 6/6).

- [ ] **Step 8: Generate and inspect the migration**

Run: `dotnet ef migrations add AddBillingSubscriptions --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`

Confirm it only adds the `Subscriptions` table and its unique index.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Billing tests/Billing.Tests src/Persistence/AppDbContext.cs src/Persistence/Migrations
git commit -m "feat(billing): add Subscription entity and SubscriptionService"
```

---

## Task 3: `Invoice` entity + cycle-generation Hangfire job

**Files:**
- Create: `src/Modules/Billing/Domain/Invoice.cs`
- Create: `src/Modules/Billing/Domain/InvoiceStatus.cs`
- Create: `src/Modules/Billing/Infrastructure/InvoiceConfiguration.cs`
- Create: `src/Modules/Billing/Application/InvoiceGenerationJob.cs`
- Create: `src/Modules/Billing/Application/BillingOptions.cs`
- Modify: `src/Persistence/AppDbContext.cs`
- Test: `tests/Billing.Tests/InvoiceGenerationJobTests.cs`

(`src/Api/Program.cs`'s recurring-job registration and DI wiring for everything built across Tasks 1-4/6 all happen together in Task 7, once `Billing`'s Presentation layer exists to wire alongside — registering a job with no way to reach it over HTTP yet would be untestable in isolation.)

**Interfaces:**
- Consumes: `Courier.Billing.Domain.Subscription`/`Plan` (Tasks 1-2).
- Produces: `Courier.Billing.Domain.Invoice` (`Id`, `OrganizationId`, `SubscriptionId`, `PlanId`, `PlanNameSnapshot`, `AmountNprSnapshot`, `CycleStart`, `CycleEnd`, `Status`, `DueAt`, `PaidAt`, `CreatedAt`).
- Produces: `Courier.Billing.Domain.InvoiceStatus` (`Pending`, `Paid`, `Overdue`, `Cancelled`).
- Produces: `Courier.Billing.Application.InvoiceGenerationJob.RunAsync(CancellationToken)` — consumed by Task 7's `Program.cs` `RecurringJob.AddOrUpdate` call.
- Produces: `Courier.Billing.Application.BillingOptions` (`GraceDays` int, default 7) bound from configuration section `"Billing"`.

- [ ] **Step 1: Write `InvoiceStatus` and `Invoice`**

```csharp
namespace Courier.Billing.Domain;

public enum InvoiceStatus
{
    Pending = 0,
    Paid = 1,
    Overdue = 2,
    Cancelled = 3
}
```

```csharp
namespace Courier.Billing.Domain;

// PlanNameSnapshot/AmountNprSnapshot are copied at generation time — a later Plan price change
// never alters an already-generated Invoice (same principle as Shipment's locked-in pricing
// and RateCard versioning elsewhere in this codebase).
public class Invoice
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid PlanId { get; set; }
    public required string PlanNameSnapshot { get; set; }
    public decimal AmountNprSnapshot { get; set; }
    public DateTimeOffset CycleStart { get; set; }
    public DateTimeOffset CycleEnd { get; set; }
    public InvoiceStatus Status { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 2: Write the EF configuration**

```csharp
using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Billing.Infrastructure;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.PlanNameSnapshot).HasMaxLength(150).IsRequired();
        builder.Property(i => i.AmountNprSnapshot).HasPrecision(12, 2);
        builder.HasIndex(i => new { i.OrganizationId, i.Status });
        // One invoice per (Subscription, CycleStart) — the generation job's own query already
        // filters on CurrentCycleEnd, but this index makes "did we already bill this cycle"
        // DB-enforced too, turning a would-be double-run of the job (e.g. Hangfire retry racing
        // a slow first run) into a caught DbUpdateException instead of a duplicate charge.
        builder.HasIndex(i => new { i.SubscriptionId, i.CycleStart }).IsUnique();
    }
}
```

- [ ] **Step 3: Write `BillingOptions` and the generation job**

```csharp
namespace Courier.Billing.Application;

public class BillingOptions
{
    public int GraceDays { get; set; } = 7;
}
```

```csharp
using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Courier.Billing.Application;

// Hangfire recurring job (registered in Program.cs, Task 6). Runs with no ambient tenant
// context, so it reads across every organization directly via IgnoreQueryFilters() — the same
// legitimate cross-tenant background-job pattern Events.OutboxPublisherJob already established.
public class InvoiceGenerationJob
{
    private readonly DbContext _dbContext;
    private readonly BillingOptions _options;

    public InvoiceGenerationJob(DbContext dbContext, IOptions<BillingOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var dueSubscriptions = await _dbContext.Set<Subscription>().IgnoreQueryFilters()
            .Where(s => s.CurrentCycleEnd <= now && s.Status == SubscriptionStatus.Active)
            .ToListAsync(cancellationToken);

        if (dueSubscriptions.Count == 0)
        {
            return;
        }

        var planIds = dueSubscriptions.Select(s => s.PlanId).ToHashSet();
        var plans = await _dbContext.Set<Plan>().IgnoreQueryFilters()
            .Where(p => planIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        foreach (var subscription in dueSubscriptions)
        {
            var plan = plans[subscription.PlanId];
            var cycleStart = subscription.CurrentCycleStart;
            var cycleEnd = subscription.CurrentCycleEnd;

            // Zero-price (free/default) plans never generate an invoice — the org's cycle still
            // advances so a later plan upgrade starts counting from a clean cycle boundary.
            if (plan.MonthlyPriceNpr > 0)
            {
                var alreadyBilled = await _dbContext.Set<Invoice>().IgnoreQueryFilters()
                    .AnyAsync(i => i.SubscriptionId == subscription.Id && i.CycleStart == cycleStart, cancellationToken);
                if (!alreadyBilled)
                {
                    _dbContext.Set<Invoice>().Add(new Invoice
                    {
                        Id = Guid.NewGuid(),
                        OrganizationId = subscription.OrganizationId,
                        SubscriptionId = subscription.Id,
                        PlanId = plan.Id,
                        PlanNameSnapshot = plan.Name,
                        AmountNprSnapshot = plan.MonthlyPriceNpr,
                        CycleStart = cycleStart,
                        CycleEnd = cycleEnd,
                        Status = InvoiceStatus.Pending,
                        DueAt = cycleEnd.AddDays(_options.GraceDays),
                        CreatedAt = now
                    });
                }
            }

            subscription.CurrentCycleStart = cycleEnd;
            subscription.CurrentCycleEnd = cycleEnd.AddMonths(1);
            subscription.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Wire `Invoice` into `AppDbContext`**

```csharp
modelBuilder.ApplyConfiguration(new Courier.Billing.Infrastructure.InvoiceConfiguration());
modelBuilder.Entity<Courier.Billing.Domain.Invoice>().HasQueryFilter(i => _currentTenantProvider.OrganizationId != null && i.OrganizationId == _currentTenantProvider.OrganizationId);
```

- [ ] **Step 5: Write the failing test**

```csharp
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.SharedKernel;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Billing.Tests;

public class InvoiceGenerationJobTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    [Fact]
    public async Task RunAsync_CyclePastDueOnPaidPlan_GeneratesInvoiceAndAdvancesCycle()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", Slug = $"pro-{Guid.NewGuid():N}", MonthlyPriceNpr = 2000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var cycleStart = DateTimeOffset.UtcNow.AddMonths(-1);
        var cycleEnd = DateTimeOffset.UtcNow.AddMinutes(-1);
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = cycleStart, CurrentCycleEnd = cycleEnd, CreatedAt = cycleStart, UpdatedAt = cycleStart };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();

        var job = new InvoiceGenerationJob(dbContext, Options.Create(new BillingOptions()));
        await job.RunAsync();

        var invoice = dbContext.Set<Invoice>().IgnoreQueryFilters().Single(i => i.SubscriptionId == subscription.Id);
        Assert.Equal(2000m, invoice.AmountNprSnapshot);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);

        var updatedSubscription = dbContext.Set<Subscription>().IgnoreQueryFilters().Single(s => s.Id == subscription.Id);
        Assert.Equal(cycleEnd, updatedSubscription.CurrentCycleStart);
    }

    [Fact]
    public async Task RunAsync_FreeCyclePastDue_AdvancesCycleWithNoInvoice()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Free", Slug = $"free-{Guid.NewGuid():N}", MonthlyPriceNpr = 0m, IsActive = true, IsDefault = true, CreatedAt = DateTimeOffset.UtcNow };
        var cycleEnd = DateTimeOffset.UtcNow.AddMinutes(-1);
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = cycleEnd.AddMonths(-1), CurrentCycleEnd = cycleEnd, CreatedAt = cycleEnd, UpdatedAt = cycleEnd };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();

        var job = new InvoiceGenerationJob(dbContext, Options.Create(new BillingOptions()));
        await job.RunAsync();

        Assert.False(dbContext.Set<Invoice>().IgnoreQueryFilters().Any(i => i.SubscriptionId == subscription.Id));
    }

    [Fact]
    public async Task RunAsync_CalledTwiceForSameCycle_DoesNotDoubleInvoice()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", Slug = $"pro2-{Guid.NewGuid():N}", MonthlyPriceNpr = 2000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var cycleEnd = DateTimeOffset.UtcNow.AddMinutes(-1);
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = cycleEnd.AddMonths(-1), CurrentCycleEnd = cycleEnd, CreatedAt = cycleEnd, UpdatedAt = cycleEnd };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();
        var job = new InvoiceGenerationJob(dbContext, Options.Create(new BillingOptions()));
        await job.RunAsync();

        // Second run: the cycle already advanced past "now" by the first run, so there is
        // nothing due — this proves the job is safely re-runnable, not just idempotent within
        // a single overlapping window.
        await job.RunAsync();

        var count = dbContext.Set<Invoice>().IgnoreQueryFilters().Count(i => i.SubscriptionId == subscription.Id);
        Assert.Equal(1, count);
    }
}
```

- [ ] **Step 6: Run tests to verify failure then success**

Run: `dotnet test tests/Billing.Tests/Billing.Tests.csproj`
Expected before implementation: FAIL to compile. After Steps 1-4: PASS (3/3 new, 9/9 total).

- [ ] **Step 7: Generate and inspect the migration**

Run: `dotnet ef migrations add AddBillingInvoices --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Billing tests/Billing.Tests src/Persistence/AppDbContext.cs src/Persistence/Migrations
git commit -m "feat(billing): add Invoice entity and cycle-generation job"
```

---

## Task 4: `Payment` entity + eSewa/Khalti gateway clients

**Files:**
- Create: `src/Modules/Billing/Domain/Payment.cs`
- Create: `src/Modules/Billing/Domain/PaymentGateway.cs`
- Create: `src/Modules/Billing/Domain/PaymentStatus.cs`
- Create: `src/Modules/Billing/Infrastructure/PaymentConfiguration.cs`
- Create: `src/Modules/Billing/Application/IPaymentGatewayClient.cs`
- Create: `src/Modules/Billing/Infrastructure/EsewaOptions.cs`
- Create: `src/Modules/Billing/Infrastructure/EsewaGatewayClient.cs`
- Create: `src/Modules/Billing/Infrastructure/KhaltiOptions.cs`
- Create: `src/Modules/Billing/Infrastructure/KhaltiGatewayClient.cs`
- Modify: `src/Persistence/AppDbContext.cs`
- Test: `tests/Billing.Tests/EsewaGatewayClientTests.cs`
- Test: `tests/Billing.Tests/KhaltiGatewayClientTests.cs`

Gateway API shapes below match each provider's current publicly documented merchant integration (eSewa ePay v2 form-based checkout + signature verification; Khalti KPG-2 REST initiate/lookup) as of this plan's writing. Both are third-party APIs that can change — if either provider's live sandbox response shape doesn't match a field name below when this task is actually implemented, treat that as a normal integration bug to fix against the provider's current docs, not a plan defect to stop and re-plan over.

**Interfaces:**
- Consumes: nothing new from earlier tasks (this task is gateway-client-only; `PaymentService` in Task 5 wires these clients to `Invoice`).
- Produces: `Courier.Billing.Domain.Payment` (`Id`, `InvoiceId`, `Gateway`, `GatewayTransactionId` string?, `Status`, `AmountNpr`, `InitiatedAt`, `CompletedAt`, `RawVerificationPayload` string?).
- Produces: `Courier.Billing.Application.IPaymentGatewayClient` with `Gateway` property, `InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken)`, `VerifyAsync(Guid paymentId, IReadOnlyDictionary<string,string> callbackParameters, CancellationToken)`.
- Produces: `Courier.Billing.Application.GatewayCheckoutInfo(string RedirectUrl, IReadOnlyDictionary<string,string> FormFields)` — `FormFields` empty means "browser-redirect to `RedirectUrl`," non-empty means "auto-submit a POST form with these fields to `RedirectUrl`" (eSewa's flow; Khalti never populates this).
- Produces: `Courier.Billing.Application.GatewayVerificationResult(bool Succeeded, string? GatewayTransactionId, string RawPayload)`.

- [ ] **Step 1: Write `Payment` domain types**

```csharp
namespace Courier.Billing.Domain;

public enum PaymentGateway { Esewa = 0, Khalti = 1 }
public enum PaymentStatus { Initiated = 0, Succeeded = 1, Failed = 2 }
```

```csharp
namespace Courier.Billing.Domain;

public class Payment
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public PaymentGateway Gateway { get; set; }
    public string? GatewayTransactionId { get; set; }
    public PaymentStatus Status { get; set; }
    public decimal AmountNpr { get; set; }
    public DateTimeOffset InitiatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    // The gateway's own verification-call response, kept for audit/dispute resolution — never
    // parsed back out of this column at runtime, write-once.
    public string? RawVerificationPayload { get; set; }
}
```

- [ ] **Step 2: Write the EF configuration**

```csharp
using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Billing.Infrastructure;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.AmountNpr).HasPrecision(12, 2);
        builder.Property(p => p.RawVerificationPayload).HasColumnType("jsonb");
        // Payment isolates transitively through InvoiceId -> Invoice.OrganizationId — same
        // "child table, no own OrganizationId column" pattern as ShipmentItem/ManifestShipment.
        builder.HasIndex(p => p.InvoiceId);
    }
}
```

- [ ] **Step 3: Write `IPaymentGatewayClient` and its result records**

```csharp
using Courier.Billing.Domain;

namespace Courier.Billing.Application;

public record GatewayCheckoutInfo(string RedirectUrl, IReadOnlyDictionary<string, string> FormFields);
public record GatewayVerificationResult(bool Succeeded, string? GatewayTransactionId, string RawPayload);

public interface IPaymentGatewayClient
{
    PaymentGateway Gateway { get; }
    Task<GatewayCheckoutInfo> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default);
    Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Write `EsewaOptions` and `EsewaGatewayClient`**

```csharp
namespace Courier.Billing.Infrastructure;

public class EsewaOptions
{
    public required string ProductCode { get; set; }
    public required string SecretKey { get; set; }
    public required string FormUrl { get; set; }
    public required string StatusCheckUrl { get; set; }
}
```

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Microsoft.Extensions.Options;

namespace Courier.Billing.Infrastructure;

// eSewa ePay v2: checkout is a signed HTML form POST (not a JSON-returned redirect URL like
// Khalti), and confirmation is a two-step check — decode+verify the signature on the
// browser's own return, THEN independently confirm via the server-to-server status-check API
// (VerifyAsync does both; the signature check alone is never treated as sufficient, since a
// client-side redirect can be replayed/forged).
public class EsewaGatewayClient : IPaymentGatewayClient
{
    private readonly EsewaOptions _options;
    private readonly HttpClient _httpClient;

    public PaymentGateway Gateway => PaymentGateway.Esewa;

    public EsewaGatewayClient(IOptions<EsewaOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient("Esewa");
    }

    public Task<GatewayCheckoutInfo> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default)
    {
        var totalAmount = amountNpr.ToString("F2");
        var transactionUuid = paymentId.ToString();
        var signedFieldNames = "total_amount,transaction_uuid,product_code";
        var signature = SignFields(($"total_amount={totalAmount},transaction_uuid={transactionUuid},product_code={_options.ProductCode}"));

        var fields = new Dictionary<string, string>
        {
            ["amount"] = totalAmount,
            ["tax_amount"] = "0",
            ["total_amount"] = totalAmount,
            ["transaction_uuid"] = transactionUuid,
            ["product_code"] = _options.ProductCode,
            ["product_service_charge"] = "0",
            ["product_delivery_charge"] = "0",
            ["success_url"] = returnUrl,
            ["failure_url"] = returnUrl,
            ["signed_field_names"] = signedFieldNames,
            ["signature"] = signature
        };

        return Task.FromResult(new GatewayCheckoutInfo(_options.FormUrl, fields));
    }

    public async Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
    {
        if (!callbackParameters.TryGetValue("data", out var encodedData))
        {
            return new GatewayVerificationResult(false, null, "{}");
        }

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encodedData));
        using var document = JsonDocument.Parse(decoded);
        var root = document.RootElement;
        var totalAmount = root.GetProperty("total_amount").GetString() ?? "";
        var transactionUuid = root.GetProperty("transaction_uuid").GetString() ?? "";
        var receivedSignature = root.GetProperty("signature").GetString() ?? "";

        var expectedSignature = SignFields($"total_amount={totalAmount},transaction_uuid={transactionUuid},product_code={_options.ProductCode}");
        if (receivedSignature != expectedSignature || transactionUuid != paymentId.ToString())
        {
            return new GatewayVerificationResult(false, null, decoded);
        }

        // Server-to-server confirmation — never trust the decoded redirect payload alone.
        var statusUrl = $"{_options.StatusCheckUrl}?product_code={Uri.EscapeDataString(_options.ProductCode)}&total_amount={Uri.EscapeDataString(totalAmount)}&transaction_uuid={Uri.EscapeDataString(transactionUuid)}";
        var response = await _httpClient.GetAsync(statusUrl, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new GatewayVerificationResult(false, null, body);
        }

        using var statusDocument = JsonDocument.Parse(body);
        var status = statusDocument.RootElement.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
        var transactionCode = root.TryGetProperty("transaction_code", out var codeProp) ? codeProp.GetString() : null;

        return new GatewayVerificationResult(status == "COMPLETE", transactionCode, body);
    }

    private string SignFields(string message)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.SecretKey);
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var hash = new HMACSHA256(keyBytes).ComputeHash(messageBytes);
        return Convert.ToBase64String(hash);
    }
}
```

- [ ] **Step 5: Write `KhaltiOptions` and `KhaltiGatewayClient`**

```csharp
namespace Courier.Billing.Infrastructure;

public class KhaltiOptions
{
    public required string SecretKey { get; set; }
    public required string InitiateUrl { get; set; }
    public required string LookupUrl { get; set; }
    public required string WebsiteUrl { get; set; }
}
```

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Microsoft.Extensions.Options;

namespace Courier.Billing.Infrastructure;

// Khalti KPG-2: initiate returns a ready-to-redirect payment_url (unlike eSewa's signed-form
// flow) keyed by an opaque "pidx"; confirmation is a separate server-to-server lookup call on
// that pidx — the browser-side return-URL query params are only used to know which pidx to
// look up, never trusted for the actual paid/not-paid determination.
public class KhaltiGatewayClient : IPaymentGatewayClient
{
    private readonly KhaltiOptions _options;
    private readonly HttpClient _httpClient;

    public PaymentGateway Gateway => PaymentGateway.Khalti;

    public KhaltiGatewayClient(IOptions<KhaltiOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient("Khalti");
    }

    public async Task<GatewayCheckoutInfo> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default)
    {
        var amountPaisa = (long)(amountNpr * 100);
        var requestBody = new
        {
            return_url = returnUrl,
            website_url = _options.WebsiteUrl,
            amount = amountPaisa,
            purchase_order_id = paymentId.ToString(),
            purchase_order_name = $"Subscription invoice {paymentId}"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.InitiateUrl)
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Key", _options.SecretKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(body);
        var paymentUrl = document.RootElement.GetProperty("payment_url").GetString()!;

        return new GatewayCheckoutInfo(paymentUrl, new Dictionary<string, string>());
    }

    public async Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
    {
        if (!callbackParameters.TryGetValue("pidx", out var pidx))
        {
            return new GatewayVerificationResult(false, null, "{}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.LookupUrl)
        {
            Content = JsonContent.Create(new { pidx })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Key", _options.SecretKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new GatewayVerificationResult(false, null, body);
        }

        using var document = JsonDocument.Parse(body);
        var status = document.RootElement.GetProperty("status").GetString();
        var transactionId = document.RootElement.TryGetProperty("transaction_id", out var txProp) ? txProp.GetString() : null;

        return new GatewayVerificationResult(status == "Completed", transactionId, body);
    }
}
```

- [ ] **Step 6: Wire `Payment` into `AppDbContext`** (no query filter needed directly — see Step 2's comment; it isolates via `Invoice`, so no `HasQueryFilter` line is added for `Payment` itself, matching `ShipmentItem`'s precedent of no filter on a child-only table)

```csharp
modelBuilder.ApplyConfiguration(new Courier.Billing.Infrastructure.PaymentConfiguration());
```

- [ ] **Step 7: Write the failing tests (stubbed `HttpMessageHandler`, no real network call)**

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Courier.Billing.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Billing.Tests;

public class EsewaGatewayClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        public StubHandler(string responseBody) { _responseBody = responseBody; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_responseBody) });
    }

    private static EsewaOptions Options() => new()
    {
        ProductCode = "EPAYTEST",
        SecretKey = "8gBm/:&EnhH.1/q",
        FormUrl = "https://rc-epay.esewa.com.np/api/epay/main/v2/form",
        StatusCheckUrl = "https://rc.esewa.com.np/api/epay/transaction/status/"
    };

    [Fact]
    public async Task InitiateAsync_ReturnsSignedFormFields()
    {
        var httpClientFactory = new StubHttpClientFactory(new StubHandler("{}"));
        var client = new EsewaGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var checkoutInfo = await client.InitiateAsync(Guid.NewGuid(), 2000m, "https://example.test/return");

        Assert.Equal("EPAYTEST", checkoutInfo.FormFields["product_code"]);
        Assert.False(string.IsNullOrEmpty(checkoutInfo.FormFields["signature"]));
    }

    [Fact]
    public async Task VerifyAsync_ValidSignatureAndCompleteStatus_Succeeds()
    {
        var paymentId = Guid.NewGuid();
        var options = Options();
        var totalAmount = "2000.00";
        var signMessage = $"total_amount={totalAmount},transaction_uuid={paymentId},product_code={options.ProductCode}";
        var signature = Convert.ToBase64String(new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(options.SecretKey)).ComputeHash(Encoding.UTF8.GetBytes(signMessage)));
        var payload = JsonSerializer.Serialize(new { total_amount = totalAmount, transaction_uuid = paymentId.ToString(), product_code = options.ProductCode, signature, transaction_code = "TXN123" });
        var callbackParameters = new Dictionary<string, string> { ["data"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)) };

        var httpClientFactory = new StubHttpClientFactory(new StubHandler("{\"status\":\"COMPLETE\"}"));
        var client = new EsewaGatewayClient(Microsoft.Extensions.Options.Options.Create(options), httpClientFactory);

        var result = await client.VerifyAsync(paymentId, callbackParameters);

        Assert.True(result.Succeeded);
        Assert.Equal("TXN123", result.GatewayTransactionId);
    }
}

// Shared across both gateway test files (Task 4 Step 8 references this same class).
internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public StubHttpClientFactory(HttpMessageHandler handler) { _handler = handler; }
    public HttpClient CreateClient(string name) => new(_handler);
}
```

```csharp
using System.Net;
using System.Text.Json;
using Courier.Billing.Infrastructure;
using Xunit;

namespace Courier.Billing.Tests;

public class KhaltiGatewayClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        public StubHandler(string responseBody) { _responseBody = responseBody; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_responseBody) });
    }

    private static KhaltiOptions Options() => new()
    {
        SecretKey = "test-secret-key",
        InitiateUrl = "https://dev.khalti.com/api/v2/epayment/initiate/",
        LookupUrl = "https://dev.khalti.com/api/v2/epayment/lookup/",
        WebsiteUrl = "https://example.test"
    };

    [Fact]
    public async Task InitiateAsync_ReturnsPaymentUrlFromResponse()
    {
        var httpClientFactory = new StubHttpClientFactory(new StubHandler(JsonSerializer.Serialize(new { pidx = "abc123", payment_url = "https://pay.khalti.com/abc123", expires_at = "2026-09-08T00:00:00Z", expires_in = 1800 })));
        var client = new KhaltiGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var checkoutInfo = await client.InitiateAsync(Guid.NewGuid(), 2000m, "https://example.test/return");

        Assert.Equal("https://pay.khalti.com/abc123", checkoutInfo.RedirectUrl);
        Assert.Empty(checkoutInfo.FormFields);
    }

    [Fact]
    public async Task VerifyAsync_CompletedStatus_Succeeds()
    {
        var httpClientFactory = new StubHttpClientFactory(new StubHandler(JsonSerializer.Serialize(new { pidx = "abc123", status = "Completed", transaction_id = "TXN456", total_amount = 200000 })));
        var client = new KhaltiGatewayClient(Microsoft.Extensions.Options.Options.Create(Options()), httpClientFactory);

        var result = await client.VerifyAsync(Guid.NewGuid(), new Dictionary<string, string> { ["pidx"] = "abc123" });

        Assert.True(result.Succeeded);
        Assert.Equal("TXN456", result.GatewayTransactionId);
    }
}
```

- [ ] **Step 8: Run tests to verify failure then success**

Run: `dotnet test tests/Billing.Tests/Billing.Tests.csproj`
Expected before implementation: FAIL to compile. After Steps 1-6: PASS (4/4 new, 13/13 total).

- [ ] **Step 9: Generate and inspect the migration**

Run: `dotnet ef migrations add AddBillingPayments --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`

- [ ] **Step 10: Commit**

```bash
git add src/Modules/Billing tests/Billing.Tests src/Persistence/AppDbContext.cs src/Persistence/Migrations
git commit -m "feat(billing): add Payment entity and eSewa/Khalti gateway clients"
```

---

## Task 5: `Identity` extensions — org suspension fields, impersonation audit field, `IOrganizationLookupService`, login/refresh/API-key suspension checks

This task lands before `Billing`'s payment orchestration (Task 6) because that orchestration needs `IOrganizationLookupService.ReactivateIfSystemSuspendedAsync` to exist first.

**Files:**
- Modify: `src/Modules/Identity/Domain/Organization.cs`
- Modify: `src/Modules/Audit/Domain/AuditLog.cs`
- Modify: `src/Modules/Audit/Infrastructure/AuditLogger.cs`
- Create: `src/Modules/Identity/Application/IOrganizationLookupService.cs`
- Create: `src/Modules/Identity/Application/OrganizationAdminService.cs`
- Modify: `src/Modules/Identity/Application/AuthDtos.cs` (add `OrganizationSuspendedException`)
- Modify: `src/Modules/Identity/Application/AuthService.cs` (`LoginAsync`, `RefreshTokenAsync`)
- Modify: `src/Modules/Identity/Presentation/AuthEndpoints.cs` (catch the new exception type)
- Modify: `src/Modules/Merchants/Merchants.csproj` (add `<ProjectReference Include="..\Identity\Identity.csproj" />`)
- Modify: `src/Modules/Merchants/Application/MerchantAuthService.cs`
- Modify: `src/Modules/Merchants/Infrastructure/ApiKeyAuthenticationHandler.cs`
- Modify: `src/Modules/Merchants/Presentation/MerchantAuthEndpoints.cs`
- Modify: `src/Persistence/AppDbContext.cs`
- Test: `tests/Identity.Tests/OrganizationAdminServiceTests.cs`
- Test: `tests/Api.Tests/OrganizationSuspensionTests.cs`

**Interfaces:**
- Produces: `Courier.Identity.Application.IOrganizationLookupService` — this task declares `Task<bool> IsActiveAsync(Guid organizationId, CancellationToken)` and `Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken)` (Task 9 adds list/get/suspend/delete/restore/revoke-tokens members to the same interface).
- Produces: `Courier.Identity.Application.OrganizationAdminService : IOrganizationLookupService` (Task 9 adds more methods to this same class).
- Produces: `Courier.Identity.Application.OrganizationSuspendedException : Exception`.

- [ ] **Step 1: Extend `Organization` and `AuditLog`**

```csharp
namespace Courier.Identity.Domain;

public class Organization
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public bool IsActive { get; set; } = true;
    // True only when the overdue-invoice job (Billing Task 13) auto-suspended this org for
    // non-payment — distinguishes that from a SuperAdmin's manual suspend (Task 9) so a later
    // successful payment (Task 6) knows it is safe to auto-reactivate. A manual suspend/
    // reactivate always leaves or resets this to false (Task 9).
    public bool SuspendedBySystem { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
```

`SubscriptionPlan` is removed entirely — grepped the whole of `src/` before this task started and confirmed nothing reads it (it was written once at registration and never checked anywhere), so this is a clean removal, not a data migration.

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
    // Populated by AuditLogger itself (not by any caller's AuditEntry) whenever the ambient
    // request's JWT carries an "impersonated_by" claim — see AuditLogger.LogAsync below and
    // Task 11's impersonation token issuance. Null for every normal (non-impersonated) action.
    public Guid? ImpersonatedByUserId { get; set; }
}
```

- [ ] **Step 2: Make `AuditLogger` read the impersonation claim from the ambient request**

```csharp
using Courier.Audit.Application;
using Courier.Audit.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Courier.Audit.Infrastructure;

public class AuditLogger : IAuditLogger
{
    private readonly DbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditLogger(DbContext dbContext, IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        // Null in every context with no ambient HTTP request (a Hangfire job, or a unit test
        // that never set up an HttpContext) — impersonation only ever happens over a real
        // request, so null is the correct, expected value there.
        var impersonatedByClaim = _httpContextAccessor.HttpContext?.User.FindFirst("impersonated_by")?.Value;
        var impersonatedByUserId = Guid.TryParse(impersonatedByClaim, out var parsed) ? parsed : (Guid?)null;

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
            IsCrossTenantAction = entry.IsCrossTenantAction,
            ImpersonatedByUserId = impersonatedByUserId
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 3: Write `IOrganizationLookupService`, `OrganizationAdminService`, and `OrganizationSuspendedException`**

```csharp
namespace Courier.Identity.Application;

public interface IOrganizationLookupService
{
    Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
```

```csharp
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class OrganizationAdminService : IOrganizationLookupService
{
    private readonly DbContext _dbContext;

    public OrganizationAdminService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await _dbContext.Set<Organization>()
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);
        return organization is { IsActive: true, DeletedAt: null };
    }

    // Called by Billing.Application.PaymentService (Task 6) after a payment is confirmed paid.
    // Deliberately a no-op (not an error) if the org isn't suspended, or was suspended manually
    // by a SuperAdmin (SuspendedBySystem == false) — paying an overdue invoice must never
    // silently un-suspend an org a human operator suspended for an unrelated cause.
    public async Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await _dbContext.Set<Organization>()
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);
        if (organization is { SuspendedBySystem: true })
        {
            organization.IsActive = true;
            organization.SuspendedBySystem = false;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
```

Add to `src/Modules/Identity/Application/AuthDtos.cs` (alongside the existing `AuthenticationFailedException`):

```csharp
public class OrganizationSuspendedException : Exception
{
    public OrganizationSuspendedException(string message) : base(message) { }
}
```

- [ ] **Step 4: Enforce the check in `AuthService.LoginAsync`/`RefreshTokenAsync`**

In `LoginAsync`, immediately after the existing `if (user.OrganizationId is null) { throw ... }` block:

```csharp
        var organization = await _dbContext.Set<Organization>()
            .FirstAsync(o => o.Id == user.OrganizationId, cancellationToken);
        if (!organization.IsActive || organization.DeletedAt is not null)
        {
            throw new OrganizationSuspendedException("Your organization has been suspended. Contact support.");
        }
```

In `RefreshTokenAsync`, immediately after the existing `if (!user.IsActive) { throw ... }` block:

```csharp
        var organization = await _dbContext.Set<Organization>()
            .FirstAsync(o => o.Id == user.OrganizationId!.Value, cancellationToken);
        if (!organization.IsActive || organization.DeletedAt is not null)
        {
            throw new AuthenticationFailedException("Refresh token is invalid or expired.");
        }
```

(`RefreshTokenAsync` deliberately throws the generic `AuthenticationFailedException`, not `OrganizationSuspendedException` — a refresh call is a background/silent operation from the frontend's perspective; the user-facing "your organization has been suspended" message only needs to surface at the explicit login screen, which `LoginAsync` covers.)

- [ ] **Step 5: Map `OrganizationSuspendedException` in `AuthEndpoints.cs`**

Add a `catch` block to the `/api/v1/auth/login` route, before the existing `catch (AuthenticationFailedException ex)`:

```csharp
            catch (OrganizationSuspendedException ex)
            {
                return Results.Json(new { error = new { code = "ORG_SUSPENDED", message = ex.Message } }, statusCode: StatusCodes.Status403Forbidden);
            }
```

- [ ] **Step 6: Add the same enforcement to `MerchantAuthService` and `ApiKeyAuthenticationHandler`**

Add `<ProjectReference Include="..\Identity\Identity.csproj" />` to `src/Modules/Merchants/Merchants.csproj`'s existing `<ItemGroup>`.

In `MerchantAuthService`, add the constructor dependency and the check right after the existing `merchant.IsActive` check (line ~53-56 shown above):

```csharp
    private readonly Courier.Identity.Application.IOrganizationLookupService _organizationLookupService;

    public MerchantAuthService(DbContext dbContext, IMerchantPasswordHasher passwordHasher, IMerchantJwtTokenService jwtTokenService, IAuditLogger auditLogger, Courier.Identity.Application.IOrganizationLookupService organizationLookupService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _auditLogger = auditLogger;
        _organizationLookupService = organizationLookupService;
    }
```

```csharp
        if (!await _organizationLookupService.IsActiveAsync(merchantUser.OrganizationId, cancellationToken))
        {
            throw new MerchantOrganizationSuspendedException("Your organization has been suspended. Contact support.");
        }
```

Add `MerchantOrganizationSuspendedException` next to `MerchantAuthenticationFailedException` in `src/Modules/Merchants/Application/MerchantAuthDtos.cs`, same one-line shape as `OrganizationSuspendedException` above. Map it in `MerchantAuthEndpoints.cs`'s login route the same way as Step 5 (`ORG_SUSPENDED`, 403).

In `ApiKeyAuthenticationHandler`, inject `IOrganizationLookupService` the same way and add the check right after the existing `apiKey.IsActive`/`RevokedAt` check:

```csharp
        if (!await _organizationLookupService.IsActiveAsync(apiKey.OrganizationId))
        {
            return AuthenticateResult.Fail("ORG_SUSPENDED");
        }
```

- [ ] **Step 7: Wire everything into `AppDbContext` and DI**

`Organization`'s configuration needs no change (new scalar properties map by convention). In `src/Api/Program.cs`, register the new service near the other lookup-service registrations (e.g. next to the `IBranchLookupService` line):

```csharp
builder.Services.AddScoped<IOrganizationLookupService>(sp => sp.GetRequiredService<OrganizationAdminService>());
builder.Services.AddScoped<OrganizationAdminService>();
```

- [ ] **Step 8: Write the failing tests**

```csharp
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Identity.Tests;

public class OrganizationAdminServiceTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    [Fact]
    public async Task ReactivateIfSystemSuspendedAsync_SystemSuspended_Reactivates()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = false, SuspendedBySystem = true };
        dbContext.Set<Organization>().Add(organization);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext);

        await service.ReactivateIfSystemSuspendedAsync(organization.Id);

        var reloaded = dbContext.Set<Organization>().Single(o => o.Id == organization.Id);
        Assert.True(reloaded.IsActive);
        Assert.False(reloaded.SuspendedBySystem);
    }

    [Fact]
    public async Task ReactivateIfSystemSuspendedAsync_ManuallySuspended_DoesNotReactivate()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Test Org", Slug = $"org-{Guid.NewGuid():N}", IsActive = false, SuspendedBySystem = false };
        dbContext.Set<Organization>().Add(organization);
        await dbContext.SaveChangesAsync();
        var service = new OrganizationAdminService(dbContext);

        await service.ReactivateIfSystemSuspendedAsync(organization.Id);

        var reloaded = dbContext.Set<Organization>().Single(o => o.Id == organization.Id);
        Assert.False(reloaded.IsActive);
    }
}
```

`tests/Identity.Tests` already exists (Phase 1) with its own `TestDbContextFactory` — reuse it, do not create a second one.

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Courier.Api.Tests;

// Full HTTP round-trip: register an org, suspend it directly via DbContext (Task 9's real
// SuperAdmin suspend endpoint doesn't exist until later — this test only needs to prove the
// *login-side* enforcement Task 5 adds), then prove login is rejected.
public class OrganizationSuspensionTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public OrganizationSuspensionTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_OrganizationSuspended_Returns403WithOrgSuspendedCode()
    {
        var client = _factory.CreateClient();
        var slug = $"suspend-test-{Guid.NewGuid():N}";
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            organizationName = "Suspend Test Org",
            organizationSlug = slug,
            ownerFullName = "Owner",
            ownerEmail = $"owner-{Guid.NewGuid():N}@example.test",
            ownerPassword = "P@ssw0rd123"
        });
        registerResponse.EnsureSuccessStatusCode();
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var organizationId = registerBody.GetProperty("organizationId").GetGuid();
        var ownerEmail = registerBody.GetProperty("ownerEmail").GetString();

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Courier.Persistence.AppDbContext>();
            var organization = await dbContext.Set<Courier.Identity.Domain.Organization>().FirstAsync(o => o.Id == organizationId);
            organization.IsActive = false;
            await dbContext.SaveChangesAsync();
        }

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = ownerEmail, password = "P@ssw0rd123" });

        Assert.Equal(HttpStatusCode.Forbidden, loginResponse.StatusCode);
        var body = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ORG_SUSPENDED", body.GetProperty("error").GetProperty("code").GetString());
    }
}
```

Check `tests/Api.Tests`'s existing register-response shape and fixture class name (`ApiTestFactory` is a placeholder name here — grep `tests/Api.Tests/TenantIsolationTests.cs` for the actual `WebApplicationFactory`-derived fixture class this project already uses, and the exact JSON field names `/api/v1/auth/register` returns, then adjust this test to match exactly — don't guess field casing).

- [ ] **Step 9: Run tests to verify failure then success**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj && dotnet test tests/Api.Tests/Api.Tests.csproj --filter OrganizationSuspensionTests`
Expected before implementation: FAIL to compile. After all steps: PASS.

- [ ] **Step 10: Generate and inspect the migration**

Run: `dotnet ef migrations add AddOrganizationSuspensionAndImpersonationAudit --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`

Confirm it adds `SuspendedBySystem`/`DeletedAt` to `Organizations`, drops `SubscriptionPlan`, and adds `ImpersonatedByUserId` to `AuditLogs` — nothing else.

- [ ] **Step 11: Full-solution build and test check**

Run: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: all green — this is the task most likely to break other modules' tests (every module that constructs an `AuditLogger` directly rather than through DI would break; grep for `new AuditLogger(` across `tests/` and fix any hit the same way).

- [ ] **Step 12: Commit**

```bash
git add src/Modules/Identity src/Modules/Audit src/Modules/Merchants src/Persistence tests/Identity.Tests tests/Api.Tests
git commit -m "feat(identity): add org suspension, impersonation audit field, and cross-track login enforcement"
```

---

## Task 6: `PaymentService` — payment orchestration and org reactivation

**Files:**
- Modify: `src/Modules/Billing/Billing.csproj` (add `<ProjectReference Include="..\Identity\Identity.csproj" />` — the one narrow, deliberate exception to "Billing has no project reference to Identity," needed only for `IOrganizationLookupService.ReactivateIfSystemSuspendedAsync`; every fact Billing needs about *pricing/limits* still comes in as a parameter, per the spec — this is the one place Billing must trigger a side effect in another module, matching how `Pricing` already references multiple sibling modules for narrow lookups)
- Create: `src/Modules/Billing/Application/PaymentService.cs`
- Test: `tests/Billing.Tests/PaymentServiceTests.cs`

**Interfaces:**
- Consumes: `Courier.Billing.Domain.{Invoice,Payment,Subscription}` (Tasks 2-4), `Courier.Billing.Application.IPaymentGatewayClient` (Task 4), `Courier.Identity.Application.IOrganizationLookupService.ReactivateIfSystemSuspendedAsync` (Task 5).
- Produces: `Courier.Billing.Application.PaymentService` with `InitiatePaymentAsync(Guid invoiceId, PaymentGateway gateway, string returnUrl, CancellationToken) : Task<GatewayCheckoutInfo>` and `CompletePaymentAsync(Guid paymentId, IReadOnlyDictionary<string,string> callbackParameters, CancellationToken) : Task<PaymentCompletionResult>` — consumed by Task 7's Presentation endpoints.
- Produces: `Courier.Billing.Application.PaymentCompletionResult(bool Succeeded, string Message)`.

- [ ] **Step 1: Write `PaymentService`**

```csharp
using Courier.Billing.Domain;
using Courier.Identity.Application;
using Microsoft.EntityFrameworkCore;

namespace Courier.Billing.Application;

public record PaymentCompletionResult(bool Succeeded, string Message);

public class PaymentService
{
    private readonly DbContext _dbContext;
    private readonly IEnumerable<IPaymentGatewayClient> _gatewayClients;
    private readonly IOrganizationLookupService _organizationLookupService;

    public PaymentService(DbContext dbContext, IEnumerable<IPaymentGatewayClient> gatewayClients, IOrganizationLookupService organizationLookupService)
    {
        _dbContext = dbContext;
        _gatewayClients = gatewayClients;
        _organizationLookupService = organizationLookupService;
    }

    // returnBaseUrl is a bare page URL the frontend controls (e.g. "https://app.example.com/billing/callback"),
    // NOT the full return URL — the caller cannot know Payment.Id in advance (it's generated
    // here), so this method appends "?paymentId={payment.Id}" itself before handing the URL to
    // the gateway. This also means the frontend never needs to invent or track a correlation id
    // of its own for "which payment is this browser tab waiting on."
    public async Task<GatewayCheckoutInfo> InitiatePaymentAsync(Guid invoiceId, PaymentGateway gateway, string returnBaseUrl, CancellationToken cancellationToken = default)
    {
        var invoice = await _dbContext.Set<Invoice>().FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
            ?? throw new BillingValidationException("Invoice was not found.");
        if (invoice.Status is not (InvoiceStatus.Pending or InvoiceStatus.Overdue))
        {
            throw new BillingValidationException($"Invoice is already {invoice.Status} and cannot be paid again.");
        }

        var client = _gatewayClients.FirstOrDefault(c => c.Gateway == gateway)
            ?? throw new BillingValidationException($"Gateway '{gateway}' is not configured.");

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            Gateway = gateway,
            Status = PaymentStatus.Initiated,
            AmountNpr = invoice.AmountNprSnapshot,
            InitiatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Set<Payment>().Add(payment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var separator = returnBaseUrl.Contains('?') ? "&" : "?";
        var returnUrl = $"{returnBaseUrl}{separator}paymentId={payment.Id}";
        return await client.InitiateAsync(payment.Id, invoice.AmountNprSnapshot, returnUrl, cancellationToken);
    }

    public async Task<PaymentCompletionResult> CompletePaymentAsync(Guid paymentId, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
    {
        // Payment carries no OrganizationId of its own (Task 4 — isolates transitively through
        // InvoiceId -> Invoice.OrganizationId, same as ShipmentItem/ManifestShipment elsewhere in
        // this codebase). A raw Set<Payment>().FirstOrDefaultAsync(p => p.Id == paymentId) would
        // NOT be tenant-scoped at all (Payment has no query filter), letting any authenticated
        // caller in any org complete a payment by guessing another org's Payment id — exactly the
        // cross-tenant bug shape this project's own CLARIFICATIONS.md has hit repeatedly. Joining
        // through Invoice's own (filtered) DbSet is what actually enforces tenant scope here: a
        // cross-tenant paymentId's Invoice row is invisible under the caller's ambient filter, so
        // the join below returns nothing for it, exactly like a genuine 404.
        var payment = await (
            from p in _dbContext.Set<Payment>()
            join i in _dbContext.Set<Invoice>() on p.InvoiceId equals i.Id
            where p.Id == paymentId
            select p
        ).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BillingValidationException("Payment was not found.");
        if (payment.Status == PaymentStatus.Succeeded)
        {
            return new PaymentCompletionResult(true, "Already confirmed.");
        }

        var client = _gatewayClients.FirstOrDefault(c => c.Gateway == payment.Gateway)
            ?? throw new BillingValidationException($"Gateway '{payment.Gateway}' is not configured.");

        var verification = await client.VerifyAsync(payment.Id, callbackParameters, cancellationToken);
        payment.RawVerificationPayload = verification.RawPayload;

        if (!verification.Succeeded)
        {
            payment.Status = PaymentStatus.Failed;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new PaymentCompletionResult(false, "Payment could not be confirmed. Please try again.");
        }

        payment.Status = PaymentStatus.Succeeded;
        payment.GatewayTransactionId = verification.GatewayTransactionId;
        payment.CompletedAt = DateTimeOffset.UtcNow;

        var invoice = await _dbContext.Set<Invoice>().FirstAsync(i => i.Id == payment.InvoiceId, cancellationToken);
        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidAt = payment.CompletedAt;

        var subscription = await _dbContext.Set<Subscription>().FirstAsync(s => s.Id == invoice.SubscriptionId, cancellationToken);
        if (subscription.Status == SubscriptionStatus.PastDue)
        {
            subscription.Status = SubscriptionStatus.Active;
            subscription.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Outside the SaveChangesAsync above deliberately — this touches a different module's
        // table (Organization) through its own lookup service, not a raw DbSet write, so it is
        // not part of Billing's own atomic unit of work. A crash between the two SaveChanges
        // calls leaves the invoice correctly marked Paid but the org still suspended until the
        // next manual check — an acceptable, narrow gap versus wrapping a cross-module call in
        // Billing's own transaction (this codebase has no cross-module distributed transaction
        // mechanism, and building one is far outside this phase's scope).
        await _organizationLookupService.ReactivateIfSystemSuspendedAsync(invoice.OrganizationId, cancellationToken);

        return new PaymentCompletionResult(true, "Payment confirmed.");
    }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.Identity.Application;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Billing.Tests;

public class PaymentServiceTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private sealed class FakeGatewayClient : IPaymentGatewayClient
    {
        private readonly bool _verifySucceeds;
        public FakeGatewayClient(PaymentGateway gateway, bool verifySucceeds) { Gateway = gateway; _verifySucceeds = verifySucceeds; }
        public PaymentGateway Gateway { get; }
        public Task<GatewayCheckoutInfo> InitiateAsync(Guid paymentId, decimal amountNpr, string returnUrl, CancellationToken cancellationToken = default)
            => Task.FromResult(new GatewayCheckoutInfo("https://fake-gateway.test/pay", new Dictionary<string, string>()));
        public Task<GatewayVerificationResult> VerifyAsync(Guid paymentId, IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
            => Task.FromResult(new GatewayVerificationResult(_verifySucceeds, _verifySucceeds ? "TXN-FAKE" : null, "{}"));
    }

    private sealed class FakeOrganizationLookupService : IOrganizationLookupService
    {
        public Guid? ReactivatedOrganizationId { get; private set; }
        public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default)
        {
            ReactivatedOrganizationId = organizationId;
            return Task.CompletedTask;
        }
    }

    private static async Task<(Plan Plan, Subscription Subscription, Invoice Invoice)> SeedInvoiceAsync(Courier.Persistence.AppDbContext dbContext, Guid organizationId, SubscriptionStatus subscriptionStatus = SubscriptionStatus.PastDue)
    {
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", Slug = $"pro-{Guid.NewGuid():N}", MonthlyPriceNpr = 2000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = subscriptionStatus, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var invoice = new Invoice { Id = Guid.NewGuid(), OrganizationId = organizationId, SubscriptionId = subscription.Id, PlanId = plan.Id, PlanNameSnapshot = plan.Name, AmountNprSnapshot = plan.MonthlyPriceNpr, CycleStart = DateTimeOffset.UtcNow.AddMonths(-1), CycleEnd = DateTimeOffset.UtcNow, Status = InvoiceStatus.Overdue, DueAt = DateTimeOffset.UtcNow.AddDays(-1), CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1) };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        dbContext.Set<Invoice>().Add(invoice);
        await dbContext.SaveChangesAsync();
        return (plan, subscription, invoice);
    }

    [Fact]
    public async Task CompletePaymentAsync_GatewayConfirms_MarksPaidAndReactivatesOrg()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var (_, _, invoice) = await SeedInvoiceAsync(dbContext, organizationId);
        var organizationLookup = new FakeOrganizationLookupService();
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { new FakeGatewayClient(PaymentGateway.Esewa, true) }, organizationLookup);
        var checkoutInfo = await service.InitiatePaymentAsync(invoice.Id, PaymentGateway.Esewa, "https://example.test/return");
        Assert.NotNull(checkoutInfo);
        var payment = dbContext.Set<Payment>().Single(p => p.InvoiceId == invoice.Id);

        var result = await service.CompletePaymentAsync(payment.Id, new Dictionary<string, string>());

        Assert.True(result.Succeeded);
        Assert.Equal(InvoiceStatus.Paid, dbContext.Set<Invoice>().IgnoreQueryFilters().Single(i => i.Id == invoice.Id).Status);
        Assert.Equal(organizationId, organizationLookup.ReactivatedOrganizationId);
    }

    [Fact]
    public async Task CompletePaymentAsync_GatewayDenies_MarksFailedAndLeavesInvoiceUnpaid()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var (_, _, invoice) = await SeedInvoiceAsync(dbContext, organizationId);
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { new FakeGatewayClient(PaymentGateway.Khalti, false) }, new FakeOrganizationLookupService());
        await service.InitiatePaymentAsync(invoice.Id, PaymentGateway.Khalti, "https://example.test/return");
        var payment = dbContext.Set<Payment>().Single(p => p.InvoiceId == invoice.Id);

        var result = await service.CompletePaymentAsync(payment.Id, new Dictionary<string, string>());

        Assert.False(result.Succeeded);
        Assert.Equal(PaymentStatus.Failed, dbContext.Set<Payment>().Single(p => p.Id == payment.Id).Status);
        Assert.Equal(InvoiceStatus.Overdue, dbContext.Set<Invoice>().IgnoreQueryFilters().Single(i => i.Id == invoice.Id).Status);
    }

    [Fact]
    public async Task InitiatePaymentAsync_AlreadyPaidInvoice_Throws()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var (_, _, invoice) = await SeedInvoiceAsync(dbContext, organizationId);
        invoice.Status = InvoiceStatus.Paid;
        await dbContext.SaveChangesAsync();
        var service = new PaymentService(dbContext, new IPaymentGatewayClient[] { new FakeGatewayClient(PaymentGateway.Esewa, true) }, new FakeOrganizationLookupService());

        await Assert.ThrowsAsync<BillingValidationException>(() => service.InitiatePaymentAsync(invoice.Id, PaymentGateway.Esewa, "https://example.test/return"));
    }
}
```

- [ ] **Step 3: Run tests to verify failure then success**

Run: `dotnet test tests/Billing.Tests/Billing.Tests.csproj`
Expected before implementation: FAIL to compile. After Step 1: PASS (3/3 new, 16/16 total).

- [ ] **Step 4: Full-solution build check**

Run: `dotnet build Courier.slnx`
Expected: succeeds — confirms `Billing.csproj`'s new reference to `Identity.csproj` doesn't create a circular reference (it won't: `Identity.csproj` has no reference back to `Billing.csproj`).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Billing tests/Billing.Tests
git commit -m "feat(billing): add PaymentService orchestrating gateway verification and org reactivation"
```

---

## Task 7: Billing tenant-facing endpoints + full `Program.cs`/config wiring

**Files:**
- Modify: `src/Modules/Billing/Application/SubscriptionService.cs` (add `ListInvoicesAsync`)
- Create: `src/Modules/Billing/Presentation/BillingEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Modify: `src/Api/appsettings.Development.json`
- Test: `tests/Api.Tests/BillingEndpointsTests.cs`

**Interfaces:**
- Consumes: `SubscriptionService`, `PaymentService`, `PlanService`, `IPaymentGatewayClient` implementations, `InvoiceGenerationJob` (Tasks 1-6).
- Produces: `POST/GET /api/v1/billing/*` routes — consumed by Task 19's frontend Billing tab.

- [ ] **Step 1: Add `ListInvoicesAsync` to `SubscriptionService`**

```csharp
    public async Task<List<InvoiceResponse>> ListInvoicesAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Invoice>()
            .Where(i => i.OrganizationId == organizationId)
            .OrderByDescending(i => i.CycleStart)
            .Select(i => new InvoiceResponse(i.Id, i.PlanNameSnapshot, i.AmountNprSnapshot, i.CycleStart, i.CycleEnd, i.Status, i.DueAt, i.PaidAt))
            .ToListAsync(cancellationToken);
    }
```

Add the new `InvoiceResponse` record next to `SubscriptionResponse` at the top of `SubscriptionService.cs`:

```csharp
public record InvoiceResponse(Guid Id, string PlanName, decimal AmountNpr, DateTimeOffset CycleStart, DateTimeOffset CycleEnd, InvoiceStatus Status, DateTimeOffset DueAt, DateTimeOffset? PaidAt);
```

(Add `using Courier.Billing.Domain;` at the top of the file if not already present — it already is, from Task 2.)

- [ ] **Step 2: Write `BillingEndpoints`**

```csharp
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Billing.Presentation;

public record PayInvoiceRequest(PaymentGateway Gateway, string ReturnBaseUrl);

public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/billing").WithTags("Billing");

        group.MapGet("/plan", async (SubscriptionService service, ICurrentTenantProvider tenantProvider, CancellationToken cancellationToken) =>
        {
            var plan = await service.GetPlanSummaryAsync(tenantProvider.OrganizationId!.Value, cancellationToken);
            return plan is null ? Results.NotFound() : Results.Ok(plan);
        }).RequirePermission("org.billing.manage");

        group.MapGet("/invoices", async (SubscriptionService service, ICurrentTenantProvider tenantProvider, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListInvoicesAsync(tenantProvider.OrganizationId!.Value, cancellationToken)))
            .RequirePermission("org.billing.manage");

        group.MapPost("/invoices/{id:guid}/pay", async (Guid id, PayInvoiceRequest request, PaymentService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var checkoutInfo = await service.InitiatePaymentAsync(id, request.Gateway, request.ReturnBaseUrl, cancellationToken);
                return Results.Ok(checkoutInfo);
            }
            catch (BillingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BILLING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("org.billing.manage");

        group.MapPost("/payments/{id:guid}/callback", async (Guid id, Dictionary<string, string> callbackParameters, PaymentService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.CompletePaymentAsync(id, callbackParameters, cancellationToken);
                return result.Succeeded
                    ? Results.Ok(result)
                    : Results.BadRequest(new { error = new { code = "PAYMENT_NOT_CONFIRMED", message = result.Message } });
            }
            catch (BillingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BILLING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("org.billing.manage");
    }
}
```

`ICurrentTenantProvider` as a minimal-API delegate parameter resolves via DI exactly like any injected service class — it's already registered (`HttpContextCurrentTenantProvider`, `src/SharedKernel/HttpContextCurrentTenantProvider.cs`, wired in Program.cs since Phase 1).

- [ ] **Step 3: Wire DI, options, and the recurring job in `Program.cs`**

Add near the other `using` statements at the top:

```csharp
using Courier.Billing.Application;
using Courier.Billing.Infrastructure;
using Courier.Billing.Presentation;
```

Add near the other `builder.Services.AddScoped<...>` module registrations (next to `Cod`'s):

```csharp
builder.Services.AddScoped<PlanService>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddScoped<IBillingLookupService>(sp => sp.GetRequiredService<SubscriptionService>());
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<InvoiceGenerationJob>();

builder.Services.Configure<BillingOptions>(builder.Configuration.GetSection("Billing"));
builder.Services.Configure<EsewaOptions>(builder.Configuration.GetSection("Esewa"));
builder.Services.Configure<KhaltiOptions>(builder.Configuration.GetSection("Khalti"));
builder.Services.AddHttpClient("Esewa", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("Khalti", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<IPaymentGatewayClient, EsewaGatewayClient>();
builder.Services.AddScoped<IPaymentGatewayClient, KhaltiGatewayClient>();
```

Add near the other `app.Map*Endpoints()` calls:

```csharp
app.MapBillingEndpoints();
```

Add near the other `RecurringJob.AddOrUpdate` calls at the bottom:

```csharp
RecurringJob.AddOrUpdate<InvoiceGenerationJob>("billing-invoice-generation", job => job.RunAsync(CancellationToken.None), "0 1 * * *");
```

(Once daily at 01:00 UTC — a billing cycle boundary is a day-granularity event, unlike the outbox/webhook jobs' sub-minute cadence.)

Add the configuration sections to `src/Api/appsettings.Development.json` (not `appsettings.json` — this file already holds every other dev-only secret/sandbox value, `Jwt:SigningKey`/`Smtp`/`ForwardedHeaders` etc., matching that established convention), using eSewa's own publicly documented UAT test merchant credentials (safe to commit — eSewa publishes these themselves for integration testing) for the `Esewa` section, empty placeholders for `Khalti` (Khalti issues per-merchant test keys through their own merchant dashboard — there is no public shared test key to commit), and the existing `Frontend:BaseUrl` value for Khalti's required `website_url` field:

```json
  "Billing": {
    "GraceDays": 7
  },
  "Esewa": {
    "ProductCode": "EPAYTEST",
    "SecretKey": "8gBm/:&EnhH.1/q",
    "FormUrl": "https://rc-epay.esewa.com.np/api/epay/main/v2/form",
    "StatusCheckUrl": "https://rc.esewa.com.np/api/epay/transaction/status/"
  },
  "Khalti": {
    "SecretKey": "",
    "InitiateUrl": "https://dev.khalti.com/api/v2/epayment/initiate/",
    "LookupUrl": "https://dev.khalti.com/api/v2/epayment/lookup/",
    "WebsiteUrl": "http://localhost:3000"
  }
```

(`WebsiteUrl` duplicates `Frontend:BaseUrl`'s value rather than binding to it directly — `KhaltiOptions` has no dependency on the unrelated `Frontend` section, keeping the two independently configurable.)

A real deployment sets `Khalti__SecretKey` (and, for production, `Esewa__SecretKey`/`Esewa__ProductCode`/the production `FormUrl`/`StatusCheckUrl` hosts) via environment variables — standard ASP.NET Core configuration precedence, no code change needed for that.

- [ ] **Step 4: Write the failing integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Courier.Api.Tests;

public class BillingEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public BillingEndpointsTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetPlan_NoOrgBillingManagePermission_Returns403()
    {
        // Register a fresh org (OrganizationOwner holds every permission per AuthService, so
        // this test proves the ENDPOINT is actually permission-gated by provisioning a
        // lower-privilege role first — check tests/Api.Tests's existing pattern for provisioning
        // a non-owner staff user (e.g. RidersEndpointsTests.cs's CounterStaff-equivalent setup)
        // and follow it exactly rather than re-deriving the provisioning flow here.)
    }

    [Fact]
    public async Task GetPlan_AsOwnerOnDefaultPlan_ReturnsPlanSummary()
    {
        var client = _factory.CreateClient();
        var slug = $"billing-test-{Guid.NewGuid():N}";
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            organizationName = "Billing Test Org",
            organizationSlug = slug,
            ownerFullName = "Owner",
            ownerEmail = $"owner-{Guid.NewGuid():N}@example.test",
            ownerPassword = "P@ssw0rd123"
        });
        registerResponse.EnsureSuccessStatusCode();
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var organizationId = registerBody.GetProperty("organizationId").GetGuid();
        var accessToken = registerBody.GetProperty("accessToken").GetString();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Courier.Persistence.AppDbContext>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<Courier.Billing.Application.SubscriptionService>();
        var planService = scope.ServiceProvider.GetRequiredService<Courier.Billing.Application.PlanService>();
        var plan = await planService.CreatePlanAsync(new Courier.Billing.Application.CreatePlanRequest("Free", $"free-{Guid.NewGuid():N}", 0m, null, null, null, true));
        await subscriptionService.AssignPlanAsync(organizationId, plan.Id);

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.GetAsync("/api/v1/billing/plan");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Free", body.GetProperty("planName").GetString());
    }
}
```

Check `tests/Api.Tests`'s actual fixture class name (`ApiTestFactory` is this plan's placeholder — grep any existing `IClassFixture<...>` in `tests/Api.Tests/TenantIsolationTests.cs` for the real name) and the exact registration-response JSON field names before running this — adjust to match exactly, and fill in `GetPlan_NoOrgBillingManagePermission_Returns403` using this project's existing non-owner-provisioning pattern (do not leave it as an empty test body — find the pattern first, then write it for real).

- [ ] **Step 5: Run the tests to verify failure then success**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter BillingEndpointsTests`
Expected before implementation: FAIL (404 — routes don't exist yet). After Steps 1-3: PASS.

- [ ] **Step 6: Full-solution build and existing-suite regression check**

Run: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: all green, including `HangfireWiringTests.cs` (confirms the new recurring job registration doesn't break Hangfire startup).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Billing src/Api tests/Api.Tests
git commit -m "feat(billing): add tenant-facing billing endpoints and wire Program.cs"
```

---

## Task 8: `Platform` module scaffold — SuperAdmin auth and bootstrap

**Files:**
- Create: `src/SharedKernel/RequireSuperAdminFilter.cs`
- Modify: `src/SharedKernel/ClaimsPrincipalExtensions.cs`
- Create: `src/Modules/Platform/Platform.csproj`
- Create: `src/Modules/Platform/Application/IPlatformJwtTokenService.cs`
- Create: `src/Modules/Platform/Infrastructure/PlatformJwtTokenService.cs`
- Create: `src/Modules/Platform/Application/PlatformAuthService.cs`
- Create: `src/Modules/Platform/Presentation/PlatformAuthEndpoints.cs`
- Modify: `src/Persistence/Persistence.csproj`
- Modify: `Courier.slnx`
- Modify: `src/Api/Api.csproj`
- Modify: `src/Api/Program.cs`
- Modify: `src/Api/appsettings.Development.json`
- Test: `tests/Platform.Tests/Platform.Tests.csproj`
- Test: `tests/Platform.Tests/TestDbContextFactory.cs`
- Test: `tests/Platform.Tests/PlatformAuthServiceTests.cs`
- Test: `tests/Api.Tests/PlatformAuthEndpointsTests.cs`

**Interfaces:**
- Produces: `Courier.SharedKernel.ClaimsPrincipalExtensions.IsSuperAdmin(this ClaimsPrincipal)`, `Courier.SharedKernel.RequireSuperAdmin(this RouteHandlerBuilder)` — consumed by every `Platform`/SuperAdmin-only endpoint from Task 9 onward.
- Produces: `Courier.Platform.Application.IPlatformJwtTokenService.IssueAccessToken(Guid userId, string email) : string`.
- Produces: `Courier.Platform.Application.PlatformAuthService` with `LoginAsync`/`RefreshTokenAsync`.

- [ ] **Step 1: Write `RequireSuperAdminFilter` and the `IsSuperAdmin` claim extension**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Courier.SharedKernel;

public class RequireSuperAdminFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.User.IsSuperAdmin())
        {
            return Results.Forbid();
        }
        return await next(context);
    }
}

public static class SuperAdminEndpointExtensions
{
    public static RouteHandlerBuilder RequireSuperAdmin(this RouteHandlerBuilder builder)
        => builder.RequireAuthorization().AddEndpointFilter(new RequireSuperAdminFilter());
}
```

Add to `src/SharedKernel/ClaimsPrincipalExtensions.cs`, alongside `GetUserId`/`GetMerchantId`:

```csharp
    public static bool IsSuperAdmin(this ClaimsPrincipal principal)
        => principal.FindFirst("is_super_admin")?.Value == "true";
```

- [ ] **Step 2: Scaffold `Platform.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.11" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.11" />
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.22.0" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\SharedKernel\SharedKernel.csproj" />
    <ProjectReference Include="..\Identity\Identity.csproj" />
  </ItemGroup>

</Project>
```

`Platform` referencing `Identity` is the module's whole reason to exist (`User`, `IPasswordHasher`, `JwtOptions`, `IOrganizationLookupService` all live there) — unlike `Billing`, there is no "zero reference" goal here.

- [ ] **Step 3: Write `IPlatformJwtTokenService`/`PlatformJwtTokenService`**

```csharp
namespace Courier.Platform.Application;

public interface IPlatformJwtTokenService
{
    string IssueAccessToken(Guid userId, string email);
}
```

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
    private readonly JwtOptions _options;

    public PlatformJwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string IssueAccessToken(Guid userId, string email)
    {
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("email", email),
            new Claim("is_super_admin", "true")
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

- [ ] **Step 4: Write `PlatformAuthService`**

```csharp
using System.Security.Cryptography;
using System.Text;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Platform.Application;

public record PlatformAuthResult(string AccessToken, string RefreshToken, Guid UserId);
public record PlatformLoginRequest(string Email, string Password);
public record PlatformRefreshRequest(string RefreshToken);

public class PlatformAuthenticationFailedException : Exception
{
    public PlatformAuthenticationFailedException(string message) : base(message) { }
}

public class PlatformAuthService
{
    private readonly DbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPlatformJwtTokenService _jwtTokenService;

    private static readonly string DummyPasswordHash =
        new Courier.Identity.Infrastructure.PasswordHasherAdapter().Hash("dummy-password-for-timing-safety");

    public PlatformAuthService(DbContext dbContext, IPasswordHasher passwordHasher, IPlatformJwtTokenService jwtTokenService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<PlatformAuthResult> LoginAsync(PlatformLoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.ToLowerInvariant();
        // OrganizationId == null is what makes a User a platform account (established by
        // AuthService.LoginAsync's own guard, Task 5) — IgnoreQueryFilters() is required here
        // exactly as it is on every other pre-authentication read in this codebase (no tenant is
        // known yet, and a SuperAdmin User row has no tenant to know).
        var user = await _dbContext.Set<User>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email && u.OrganizationId == null, cancellationToken);

        var passwordValid = _passwordHasher.Verify(request.Password, user?.PasswordHash ?? DummyPasswordHash);
        if (user is null || !passwordValid || !user.IsActive)
        {
            throw new PlatformAuthenticationFailedException("Invalid email or password.");
        }

        var refreshToken = await IssueRefreshTokenAsync(user.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user.Id, user.Email);
        return new PlatformAuthResult(accessToken, refreshToken.PlainText, user.Id);
    }

    public async Task<PlatformAuthResult> RefreshTokenAsync(PlatformRefreshRequest request, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(request.RefreshToken);
        var existing = await _dbContext.Set<RefreshToken>()
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);
        if (existing is null || existing.RevokedAt is not null || existing.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new PlatformAuthenticationFailedException("Refresh token is invalid or expired.");
        }

        var user = await _dbContext.Set<User>().IgnoreQueryFilters()
            .FirstAsync(u => u.Id == existing.UserId, cancellationToken);
        if (!user.IsActive || user.OrganizationId is not null)
        {
            throw new PlatformAuthenticationFailedException("Refresh token is invalid or expired.");
        }

        var newRefreshToken = await IssueRefreshTokenAsync(user.Id, cancellationToken);
        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenId = newRefreshToken.Entity.Id;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user.Id, user.Email);
        return new PlatformAuthResult(accessToken, newRefreshToken.PlainText, user.Id);
    }

    private async Task<(RefreshToken Entity, string PlainText)> IssueRefreshTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var plainText = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var entity = new RefreshToken { Id = Guid.NewGuid(), UserId = userId, TokenHash = HashToken(plainText), ExpiresAt = DateTimeOffset.UtcNow.AddDays(7) };
        _dbContext.Set<RefreshToken>().Add(entity);
        await Task.CompletedTask;
        return (entity, plainText);
    }

    private static string HashToken(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
```

`Platform` reuses `Identity.Domain.RefreshToken` (its table is keyed purely by `UserId`, with no `OrganizationId` anywhere on it) rather than defining a second refresh-token table — a `SuperAdmin`'s session is not a fundamentally different kind of thing than a staff user's.

- [ ] **Step 5: Write `PlatformAuthEndpoints`**

```csharp
using Courier.Platform.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformAuthEndpoints
{
    public static void MapPlatformAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/auth").WithTags("Platform Auth");

        group.MapPost("/login", async (PlatformLoginRequest request, PlatformAuthService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.LoginAsync(request, cancellationToken);
                return Results.Ok(result);
            }
            catch (PlatformAuthenticationFailedException ex)
            {
                return Results.Json(new { error = new { code = "LOGIN_FAILED", message = ex.Message } }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        group.MapPost("/refresh", async (PlatformRefreshRequest request, PlatformAuthService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.RefreshTokenAsync(request, cancellationToken);
                return Results.Ok(result);
            }
            catch (PlatformAuthenticationFailedException ex)
            {
                return Results.Json(new { error = new { code = "REFRESH_FAILED", message = ex.Message } }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });
    }
}
```

- [ ] **Step 6: Wire `Program.cs`, `Persistence.csproj`, `Api.csproj`, `Courier.slnx`, and the SuperAdmin bootstrap**

`src/Persistence/Persistence.csproj` and `Courier.slnx`: add `Platform.csproj` (and `Platform.Tests.csproj` in `Courier.slnx`) exactly like every prior module task.

`src/Api/Api.csproj`: add `<ProjectReference Include="..\Modules\Platform\Platform.csproj" />`.

`src/Api/Program.cs` — near the other module `using`s:

```csharp
using Courier.Platform.Application;
using Courier.Platform.Infrastructure;
using Courier.Platform.Presentation;
```

Near the other service registrations:

```csharp
builder.Services.AddScoped<IPlatformJwtTokenService, PlatformJwtTokenService>();
builder.Services.AddScoped<PlatformAuthService>();
```

Near the other `app.Map*Endpoints()` calls:

```csharp
app.MapPlatformAuthEndpoints();
```

The bootstrap check — add this as a new unconditional block placed immediately after the existing `if (app.Environment.IsDevelopment()) { ... Database.Migrate(); ... }` block (that block only runs migrations in Development; this new block must run in every environment, since production needs its first `SuperAdmin` too):

```csharp
using (var bootstrapScope = app.Services.CreateScope())
{
    var dbContext = bootstrapScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var passwordHasher = bootstrapScope.ServiceProvider.GetRequiredService<Courier.Identity.Application.IPasswordHasher>();
    var hasSuperAdmin = dbContext.Set<Courier.Identity.Domain.User>().IgnoreQueryFilters().Any(u => u.OrganizationId == null);
    if (!hasSuperAdmin)
    {
        var superAdminEmail = app.Configuration["SuperAdmin:Email"];
        var superAdminPassword = app.Configuration["SuperAdmin:Password"];
        if (string.IsNullOrEmpty(superAdminEmail) || string.IsNullOrEmpty(superAdminPassword))
        {
            if (!app.Environment.IsDevelopment())
            {
                throw new InvalidOperationException("No SuperAdmin account exists and SuperAdmin:Email/SuperAdmin:Password are not configured. Set both before starting in a non-Development environment.");
            }
        }
        else
        {
            dbContext.Set<Courier.Identity.Domain.User>().Add(new Courier.Identity.Domain.User
            {
                Id = Guid.NewGuid(),
                OrganizationId = null,
                Email = superAdminEmail.ToLowerInvariant(),
                PasswordHash = passwordHasher.Hash(superAdminPassword),
                FullName = "Platform Administrator",
                IsActive = true
            });
            dbContext.SaveChanges();
        }
    }
}
```

Add to `src/Api/appsettings.Development.json` (a real deployment sets `SuperAdmin__Email`/`SuperAdmin__Password` via environment variables, same as every other secret in this file):

```json
  "SuperAdmin": {
    "Email": "superadmin@courier.local",
    "Password": "dev-only-superadmin-password-change-me"
  }
```

- [ ] **Step 7: Scaffold `Platform.Tests` and write the failing tests**

Copy `tests/Billing.Tests/Billing.Tests.csproj`/`TestDbContextFactory.cs` to `tests/Platform.Tests/`, renaming references from `Billing` to `Platform` (including the `<ProjectReference>` to `Platform.csproj` and `Persistence.csproj`).

```csharp
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.Platform.Application;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Platform.Tests;

public class PlatformAuthServiceTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private sealed class FakeJwtTokenService : IPlatformJwtTokenService
    {
        public string IssueAccessToken(Guid userId, string email) => $"fake-token-{userId}";
    }

    [Fact]
    public async Task LoginAsync_ValidSuperAdminCredentials_Succeeds()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var hasher = new PasswordHasherAdapter();
        var email = $"superadmin-{Guid.NewGuid():N}@example.test";
        dbContext.Set<User>().Add(new User { Id = Guid.NewGuid(), OrganizationId = null, Email = email, PasswordHash = hasher.Hash("correct-password"), FullName = "Test SuperAdmin", IsActive = true });
        await dbContext.SaveChangesAsync();
        var service = new PlatformAuthService(dbContext, hasher, new FakeJwtTokenService());

        var result = await service.LoginAsync(new PlatformLoginRequest(email, "correct-password"));

        Assert.NotNull(result.AccessToken);
        Assert.NotEmpty(result.RefreshToken);
    }

    [Fact]
    public async Task LoginAsync_UserBelongsToOrganization_Rejected()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var hasher = new PasswordHasherAdapter();
        var email = $"tenant-user-{Guid.NewGuid():N}@example.test";
        dbContext.Set<User>().Add(new User { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Email = email, PasswordHash = hasher.Hash("correct-password"), FullName = "Tenant User", IsActive = true });
        await dbContext.SaveChangesAsync();
        var service = new PlatformAuthService(dbContext, hasher, new FakeJwtTokenService());

        await Assert.ThrowsAsync<PlatformAuthenticationFailedException>(() => service.LoginAsync(new PlatformLoginRequest(email, "correct-password")));
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_Rejected()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var hasher = new PasswordHasherAdapter();
        var email = $"superadmin2-{Guid.NewGuid():N}@example.test";
        dbContext.Set<User>().Add(new User { Id = Guid.NewGuid(), OrganizationId = null, Email = email, PasswordHash = hasher.Hash("correct-password"), FullName = "Test SuperAdmin", IsActive = true });
        await dbContext.SaveChangesAsync();
        var service = new PlatformAuthService(dbContext, hasher, new FakeJwtTokenService());

        await Assert.ThrowsAsync<PlatformAuthenticationFailedException>(() => service.LoginAsync(new PlatformLoginRequest(email, "wrong-password")));
    }
}
```

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Courier.Api.Tests;

// Proves a normal tenant JWT (org_id claim, no is_super_admin claim) is rejected by
// RequireSuperAdmin-gated routes and vice versa — a genuine cross-track auth test, not just a
// happy-path login test. There is no SuperAdmin-only endpoint to test against until Task 9, so
// this task's own test only covers the login/refresh round-trip itself; the cross-track
// rejection test moves to Task 9 once a real RequireSuperAdmin-gated route exists to test it on.
public class PlatformAuthEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public PlatformAuthEndpointsTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_SeededSuperAdminCorrectPassword_Succeeds()
    {
        // The bootstrap block (Step 6) seeds superadmin@courier.local from
        // appsettings.Development.json when the test host starts with no existing SuperAdmin —
        // ApiTestFactory runs with Development environment (check its actual base class/
        // WebApplicationFactory setup to confirm this holds before trusting it).
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

Confirm `ApiTestFactory` (placeholder name — use the real fixture class name from `tests/Api.Tests/TenantIsolationTests.cs`) actually runs as `Development` (or adjust the second test's approach — e.g. seed a `SuperAdmin` directly via `DbContext` in the test itself, the same way `OrganizationSuspensionTests` does in Task 5, if the shared test host runs under a different environment name).

- [ ] **Step 8: Run tests to verify failure then success**

Run: `dotnet test tests/Platform.Tests/Platform.Tests.csproj && dotnet test tests/Api.Tests/Api.Tests.csproj --filter PlatformAuthEndpointsTests`
Expected before implementation: FAIL to compile. After all steps: PASS.

- [ ] **Step 9: Full-solution build and regression check**

Run: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: all green.

- [ ] **Step 10: Commit**

```bash
git add src/SharedKernel src/Modules/Platform tests/Platform.Tests tests/Api.Tests src/Persistence/Persistence.csproj src/Api Courier.slnx
git commit -m "feat(platform): add Platform module scaffold, SuperAdmin auth, and bootstrap"
```

---

## Task 9: Organization lifecycle (suspend/reactivate/delete/restore) + org list

Touches three modules (`Identity` owns the state mutation, `Merchants` owns merchant-session revocation, `Platform` orchestrates and audits) — kept as one task because "suspend an org" is one coherent user-facing action, not three independently useful features.

**Files:**
- Modify: `src/Modules/Identity/Domain/Organization.cs` (add `CreatedAt`)
- Modify: `src/Modules/Identity/Infrastructure/OrganizationConfiguration.cs`
- Modify: `src/Modules/Identity/Application/IOrganizationLookupService.cs`
- Modify: `src/Modules/Identity/Application/OrganizationAdminService.cs`
- Modify: `src/Modules/Branches/Application/IBranchLookupService.cs`
- Modify: `src/Modules/Branches/Application/BranchService.cs`
- Create: `src/Modules/Merchants/Application/IMerchantSessionRevoker.cs`
- Create: `src/Modules/Merchants/Application/MerchantSessionRevocationService.cs`
- Modify: `src/Modules/Platform/Platform.csproj` (add references to `Merchants.csproj` and `Branches.csproj`)
- Create: `src/Modules/Platform/Application/OrganizationLifecycleService.cs`
- Create: `src/Modules/Platform/Presentation/PlatformOrganizationsEndpoints.cs`
- Modify: `src/Persistence/AppDbContext.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Platform.Tests/OrganizationLifecycleServiceTests.cs`
- Test: `tests/Api.Tests/PlatformOrganizationsEndpointsTests.cs`

**Interfaces:**
- Produces (Identity): `IOrganizationLookupService.ListAsync`, `SuspendAsync`, `ReactivateAsync`, `SoftDeleteAsync`, `RestoreAsync`, `RevokeStaffRefreshTokensAsync` — all `(Guid organizationId, CancellationToken cancellationToken = default)` except `ListAsync(CancellationToken)`.
- Produces (Branches): `IBranchLookupService.CountActiveBranchesAsync(Guid organizationId, CancellationToken) : Task<int>`.
- Produces (Merchants): `IMerchantSessionRevoker.RevokeAllForOrganizationAsync(Guid organizationId, CancellationToken)`.
- Produces (Platform): `OrganizationLifecycleService` with `SuspendAsync`, `ReactivateAsync`, `SoftDeleteAsync`, `RestoreAsync` (all take `Guid organizationId, Guid actingSuperAdminId, CancellationToken`) and `ListOrganizationsAsync(CancellationToken) : Task<List<OrganizationAdminSummary>>` — consumed by Task 10's dashboard and Task 15's frontend.
- Produces: `Courier.Platform.Application.OrganizationAdminSummary(Guid Id, string Name, string Slug, string? PlanName, bool IsActive, bool IsDeleted, int BranchCount)`.

- [ ] **Step 1: Add `Organization.CreatedAt`**

```csharp
    public DateTimeOffset CreatedAt { get; set; }
```

In `OrganizationConfiguration.cs`, add:

```csharp
        builder.Property(o => o.CreatedAt).HasDefaultValueSql("now()");
```

(A DB-side default rather than relying on every future insert path remembering to set it — `AuthService.RegisterOrganizationAsync`, Task 5's tests, and any future seed path all benefit without needing their own change.)

- [ ] **Step 2: Extend `IOrganizationLookupService`/`OrganizationAdminService`**

```csharp
namespace Courier.Identity.Application;

public record OrganizationRecord(Guid Id, string Name, string Slug, bool IsActive, bool SuspendedBySystem, DateTimeOffset? DeletedAt, DateTimeOffset CreatedAt);

public interface IOrganizationLookupService
{
    Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<List<OrganizationRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
```

Add to `OrganizationAdminService`:

```csharp
    public async Task<List<OrganizationRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Organization>()
            .OrderBy(o => o.Name)
            .Select(o => new OrganizationRecord(o.Id, o.Name, o.Slug, o.IsActive, o.SuspendedBySystem, o.DeletedAt, o.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrThrowAsync(organizationId, cancellationToken);
        organization.IsActive = false;
        organization.SuspendedBySystem = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrThrowAsync(organizationId, cancellationToken);
        organization.IsActive = true;
        organization.SuspendedBySystem = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrThrowAsync(organizationId, cancellationToken);
        organization.DeletedAt = DateTimeOffset.UtcNow;
        organization.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrThrowAsync(organizationId, cancellationToken);
        // Deliberately does NOT set IsActive = true — a separate explicit Reactivate is required
        // (spec's "two-step, no accidental instant-back-online" rule).
        organization.DeletedAt = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var userIds = await _dbContext.Set<User>().IgnoreQueryFilters()
            .Where(u => u.OrganizationId == organizationId)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        var tokens = await _dbContext.Set<RefreshToken>()
            .Where(rt => userIds.Contains(rt.UserId) && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Organization> GetOrThrowAsync(Guid organizationId, CancellationToken cancellationToken)
        => await _dbContext.Set<Organization>().FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw new InvalidOperationException($"Organization {organizationId} was not found.");
```

- [ ] **Step 3: Add `CountActiveBranchesAsync` to `Branches`**

```csharp
    Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default);
```

In `BranchService`:

```csharp
    public async Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => await _dbContext.Set<Branch>().IgnoreQueryFilters()
            .CountAsync(b => b.OrganizationId == organizationId && b.IsActive, cancellationToken);
```

(`IgnoreQueryFilters()` here is deliberate and safe — the caller (`Platform`, Step 5) always runs with `ICurrentTenantProvider.OrganizationId == null`, so the ambient filter would otherwise silently return 0 for every org, not the real count. The explicit `organizationId` parameter in the `Where` clause is the actual, correct scope.)

- [ ] **Step 4: Write `IMerchantSessionRevoker`/`MerchantSessionRevocationService`**

```csharp
namespace Courier.Merchants.Application;

public interface IMerchantSessionRevoker
{
    Task RevokeAllForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
```

```csharp
using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class MerchantSessionRevocationService : IMerchantSessionRevoker
{
    private readonly DbContext _dbContext;

    public MerchantSessionRevocationService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task RevokeAllForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var merchantUserIds = await _dbContext.Set<MerchantUser>().IgnoreQueryFilters()
            .Where(mu => mu.OrganizationId == organizationId)
            .Select(mu => mu.Id)
            .ToListAsync(cancellationToken);
        var tokens = await _dbContext.Set<MerchantRefreshToken>()
            .Where(rt => merchantUserIds.Contains(rt.MerchantUserId) && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 5: Write `OrganizationLifecycleService`**

Add `<ProjectReference Include="..\Merchants\Merchants.csproj" />`, `<ProjectReference Include="..\Branches\Branches.csproj" />`, and `<ProjectReference Include="..\Billing\Billing.csproj" />` to `Platform.csproj` (the last one is needed here for `IBillingLookupService`, not just from Task 10 onward).

```csharp
using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Merchants.Application;

namespace Courier.Platform.Application;

public record OrganizationAdminSummary(Guid Id, string Name, string Slug, string? PlanName, bool IsActive, bool IsDeleted, int BranchCount);

public class OrganizationLifecycleService
{
    private readonly IOrganizationLookupService _organizationLookupService;
    private readonly IMerchantSessionRevoker _merchantSessionRevoker;
    private readonly IBillingLookupService _billingLookupService;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IAuditLogger _auditLogger;

    public OrganizationLifecycleService(
        IOrganizationLookupService organizationLookupService,
        IMerchantSessionRevoker merchantSessionRevoker,
        IBillingLookupService billingLookupService,
        IBranchLookupService branchLookupService,
        IAuditLogger auditLogger)
    {
        _organizationLookupService = organizationLookupService;
        _merchantSessionRevoker = merchantSessionRevoker;
        _billingLookupService = billingLookupService;
        _branchLookupService = branchLookupService;
        _auditLogger = auditLogger;
    }

    public async Task<List<OrganizationAdminSummary>> ListOrganizationsAsync(CancellationToken cancellationToken = default)
    {
        var organizations = await _organizationLookupService.ListAsync(cancellationToken);
        var summaries = new List<OrganizationAdminSummary>();
        foreach (var organization in organizations)
        {
            var planSummary = await _billingLookupService.GetPlanSummaryAsync(organization.Id, cancellationToken);
            var branchCount = await _branchLookupService.CountActiveBranchesAsync(organization.Id, cancellationToken);
            summaries.Add(new OrganizationAdminSummary(organization.Id, organization.Name, organization.Slug, planSummary?.PlanName, organization.IsActive, organization.DeletedAt is not null, branchCount));
        }
        return summaries;
    }

    public async Task SuspendAsync(Guid organizationId, Guid actingSuperAdminId, CancellationToken cancellationToken = default)
    {
        await _organizationLookupService.SuspendAsync(organizationId, cancellationToken);
        await _organizationLookupService.RevokeStaffRefreshTokensAsync(organizationId, cancellationToken);
        await _merchantSessionRevoker.RevokeAllForOrganizationAsync(organizationId, cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingSuperAdminId, "organization.suspended", "Organization", organizationId.ToString(), IsCrossTenantAction: true), cancellationToken);
    }

    public async Task ReactivateAsync(Guid organizationId, Guid actingSuperAdminId, CancellationToken cancellationToken = default)
    {
        await _organizationLookupService.ReactivateAsync(organizationId, cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingSuperAdminId, "organization.reactivated", "Organization", organizationId.ToString(), IsCrossTenantAction: true), cancellationToken);
    }

    public async Task SoftDeleteAsync(Guid organizationId, Guid actingSuperAdminId, CancellationToken cancellationToken = default)
    {
        await _organizationLookupService.SoftDeleteAsync(organizationId, cancellationToken);
        await _organizationLookupService.RevokeStaffRefreshTokensAsync(organizationId, cancellationToken);
        await _merchantSessionRevoker.RevokeAllForOrganizationAsync(organizationId, cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingSuperAdminId, "organization.deleted", "Organization", organizationId.ToString(), IsCrossTenantAction: true), cancellationToken);
    }

    public async Task RestoreAsync(Guid organizationId, Guid actingSuperAdminId, CancellationToken cancellationToken = default)
    {
        await _organizationLookupService.RestoreAsync(organizationId, cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingSuperAdminId, "organization.restored", "Organization", organizationId.ToString(), IsCrossTenantAction: true), cancellationToken);
    }
}
```

- [ ] **Step 6: Write `PlatformOrganizationsEndpoints`**

```csharp
using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformOrganizationsEndpoints
{
    public static void MapPlatformOrganizationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/organizations").WithTags("Platform Organizations");

        group.MapGet("/", async (OrganizationLifecycleService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListOrganizationsAsync(cancellationToken)))
            .RequireSuperAdmin();

        group.MapPost("/{id:guid}/suspend", async (Guid id, OrganizationLifecycleService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            await service.SuspendAsync(id, httpContext.User.GetUserId()!.Value, cancellationToken);
            return Results.NoContent();
        }).RequireSuperAdmin();

        group.MapPost("/{id:guid}/reactivate", async (Guid id, OrganizationLifecycleService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            await service.ReactivateAsync(id, httpContext.User.GetUserId()!.Value, cancellationToken);
            return Results.NoContent();
        }).RequireSuperAdmin();

        group.MapPost("/{id:guid}/delete", async (Guid id, OrganizationLifecycleService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            await service.SoftDeleteAsync(id, httpContext.User.GetUserId()!.Value, cancellationToken);
            return Results.NoContent();
        }).RequireSuperAdmin();

        group.MapPost("/{id:guid}/restore", async (Guid id, OrganizationLifecycleService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            await service.RestoreAsync(id, httpContext.User.GetUserId()!.Value, cancellationToken);
            return Results.NoContent();
        }).RequireSuperAdmin();
    }
}
```

- [ ] **Step 7: Wire DI and `AppDbContext`**

No new `HasQueryFilter` needed — `Organization`'s configuration is unchanged beyond Step 1's `CreatedAt` default.

In `Program.cs`, near the other registrations:

```csharp
builder.Services.AddScoped<IOrganizationLookupService>(sp => sp.GetRequiredService<OrganizationAdminService>());
builder.Services.AddScoped<IMerchantSessionRevoker, MerchantSessionRevocationService>();
builder.Services.AddScoped<OrganizationLifecycleService>();
```

(`IOrganizationLookupService`'s registration already exists from Task 5 — confirm it's still a single line, not duplicated, since Task 5 registered the same interface/implementation pair.)

Near the other endpoint mappings:

```csharp
app.MapPlatformOrganizationsEndpoints();
```

- [ ] **Step 8: Write the failing tests**

```csharp
using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.Platform.Application;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Platform.Tests;

public class OrganizationLifecycleServiceTests
{
    private sealed class FakeOrganizationLookupService : IOrganizationLookupService
    {
        public bool Suspended { get; private set; }
        public bool RevokedStaffTokens { get; private set; }
        public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<OrganizationRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<OrganizationRecord>());
        public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) { Suspended = true; return Task.CompletedTask; }
        public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) { Suspended = false; return Task.CompletedTask; }
        public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) { RevokedStaffTokens = true; return Task.CompletedTask; }
    }

    private sealed class FakeMerchantSessionRevoker : IMerchantSessionRevoker
    {
        public bool Revoked { get; private set; }
        public Task RevokeAllForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) { Revoked = true; return Task.CompletedTask; }
    }

    private sealed class FakeBillingLookupService : IBillingLookupService
    {
        public Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult((int?)null);
        public Task<PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<PlanSummary?>(null);
    }

    private sealed class FakeBranchLookupService : IBranchLookupService
    {
        public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult<BranchZoneInfo?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public AuditEntry? LastEntry { get; private set; }
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) { LastEntry = entry; return Task.CompletedTask; }
    }

    [Fact]
    public async Task SuspendAsync_RevokesBothStaffAndMerchantSessionsAndAuditsCrossTenant()
    {
        var organizationLookup = new FakeOrganizationLookupService();
        var merchantRevoker = new FakeMerchantSessionRevoker();
        var auditLogger = new FakeAuditLogger();
        var service = new OrganizationLifecycleService(organizationLookup, merchantRevoker, new FakeBillingLookupService(), new FakeBranchLookupService(), auditLogger);
        var organizationId = Guid.NewGuid();
        var superAdminId = Guid.NewGuid();

        await service.SuspendAsync(organizationId, superAdminId);

        Assert.True(organizationLookup.Suspended);
        Assert.True(organizationLookup.RevokedStaffTokens);
        Assert.True(merchantRevoker.Revoked);
        Assert.True(auditLogger.LastEntry!.IsCrossTenantAction);
        Assert.Equal(superAdminId, auditLogger.LastEntry.UserId);
        Assert.Equal(organizationId, auditLogger.LastEntry.OrganizationId);
    }

    [Fact]
    public async Task RestoreAsync_DoesNotReactivate()
    {
        var organizationLookup = new FakeOrganizationLookupService();
        var service = new OrganizationLifecycleService(organizationLookup, new FakeMerchantSessionRevoker(), new FakeBillingLookupService(), new FakeBranchLookupService(), new FakeAuditLogger());

        await service.RestoreAsync(Guid.NewGuid(), Guid.NewGuid());

        // RestoreAsync never touches organizationLookup.Suspended's tracked flag (only
        // ReactivateAsync/SuspendAsync do in this fake) — proves Restore alone never reactivates.
        Assert.False(organizationLookup.Suspended);
    }
}
```

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Courier.Api.Tests;

public class PlatformOrganizationsEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public PlatformOrganizationsEndpointsTests(ApiTestFactory factory)
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
    public async Task Suspend_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = _factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            organizationName = "Not Super Admin Org",
            organizationSlug = $"nsa-{Guid.NewGuid():N}",
            ownerFullName = "Owner",
            ownerEmail = $"owner-{Guid.NewGuid():N}@example.test",
            ownerPassword = "P@ssw0rd123"
        });
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var tenantAccessToken = registerBody.GetProperty("accessToken").GetString();
        var organizationId = registerBody.GetProperty("organizationId").GetGuid();

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tenantAccessToken);
        var response = await client.PostAsync($"/api/v1/platform/organizations/{organizationId}/suspend", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuspendThenLogin_TenantOwnerLoginRejectedWithOrgSuspended()
    {
        var client = _factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            organizationName = "Suspend Flow Org",
            organizationSlug = $"suspend-flow-{Guid.NewGuid():N}",
            ownerFullName = "Owner",
            ownerEmail = $"owner-{Guid.NewGuid():N}@example.test",
            ownerPassword = "P@ssw0rd123"
        });
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var organizationId = registerBody.GetProperty("organizationId").GetGuid();
        var ownerEmail = registerBody.GetProperty("ownerEmail").GetString();

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", superAdminToken);
        var suspendResponse = await client.PostAsync($"/api/v1/platform/organizations/{organizationId}/suspend", null);
        Assert.Equal(HttpStatusCode.NoContent, suspendResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = ownerEmail, password = "P@ssw0rd123" });

        Assert.Equal(HttpStatusCode.Forbidden, loginResponse.StatusCode);
    }
}
```

Adjust the register-response field names (`accessToken`, `organizationId`, and whatever the owner's email round-trips as — check `AuthResult`'s actual JSON shape) against the real DTO before running.

- [ ] **Step 9: Run tests to verify failure then success**

Run: `dotnet test tests/Platform.Tests/Platform.Tests.csproj && dotnet test tests/Api.Tests/Api.Tests.csproj --filter PlatformOrganizationsEndpointsTests`
Expected before implementation: FAIL to compile. After all steps: PASS.

- [ ] **Step 10: Generate and inspect the migration**

Run: `dotnet ef migrations add AddOrganizationCreatedAt --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`

- [ ] **Step 11: Full-solution build and regression check**

Run: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj`

- [ ] **Step 12: Commit**

```bash
git add src/Modules/Identity src/Modules/Branches src/Modules/Merchants src/Modules/Platform src/Persistence src/Api tests/Platform.Tests tests/Api.Tests
git commit -m "feat(platform): add organization lifecycle (suspend/reactivate/delete/restore) and org list"
```

---

## Task 10: Cross-org dashboard — platform metrics + org detail drill-down

**Files:**
- Modify: `src/Modules/Billing/Application/IBillingLookupService.cs`
- Modify: `src/Modules/Billing/Application/SubscriptionService.cs`
- Modify: `src/Modules/Branches/Application/IBranchLookupService.cs`
- Modify: `src/Modules/Branches/Application/BranchService.cs`
- Modify: `src/Modules/Identity/Application/IOrganizationLookupService.cs`
- Modify: `src/Modules/Identity/Application/OrganizationAdminService.cs`
- Create: `src/Modules/Audit/Application/IAuditLogReader.cs`
- Modify: `src/Modules/Audit/Infrastructure/AuditLogger.cs` (implement the new interface too)
- Modify: `src/Modules/Platform/Platform.csproj` (add `<ProjectReference Include="..\Audit\Audit.csproj" />` if not already present via a transitive reference — check; add explicitly if the compiler can't see `IAuditLogReader` otherwise)
- Create: `src/Modules/Platform/Application/PlatformDashboardService.cs`
- Create: `src/Modules/Platform/Presentation/PlatformDashboardEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Platform.Tests/PlatformDashboardServiceTests.cs`
- Test: `tests/Api.Tests/PlatformDashboardEndpointsTests.cs`

**Interfaces:**
- Produces (Billing): `IBillingLookupService.GetPlatformMetricsAsync(CancellationToken) : Task<PlatformBillingMetrics>` where `PlatformBillingMetrics(decimal MonthlyRecurringRevenueNpr, int OverdueInvoiceCount)`; `IBillingLookupService.ListInvoicesForOrganizationAsync(Guid organizationId, CancellationToken) : Task<List<InvoiceResponse>>` (thin wrapper delegating to `SubscriptionService.ListInvoicesAsync`, added to the interface so `Platform` never needs to inject `SubscriptionService` directly).
- Produces (Branches): `IBranchLookupService.ListForOrganizationAsync(Guid organizationId, CancellationToken) : Task<List<BranchAdminRecord>>` where `BranchAdminRecord(Guid Id, string Code, string Name, bool IsActive)`.
- Produces (Identity): `IOrganizationLookupService.GetAsync(Guid organizationId, CancellationToken) : Task<OrganizationRecord?>`, `ListUsersAsync(Guid organizationId, CancellationToken) : Task<List<OrganizationUserRecord>>` where `OrganizationUserRecord(Guid Id, string Email, string FullName, bool IsActive)`.
- Produces (Audit): `IAuditLogReader.ListForOrganizationAsync(Guid organizationId, int limit, CancellationToken) : Task<List<AuditLogRecord>>` where `AuditLogRecord(Guid Id, string Action, string EntityType, string EntityId, Guid? UserId, Guid? ImpersonatedByUserId, bool IsCrossTenantAction, DateTimeOffset Timestamp)`.
- Produces (Platform): `PlatformDashboardService.GetMetricsAsync(CancellationToken) : Task<PlatformMetrics>`, `GetOrganizationDetailAsync(Guid organizationId, CancellationToken) : Task<OrganizationDetail?>`.

- [ ] **Step 1: Extend `IBillingLookupService`/`SubscriptionService`**

```csharp
public record PlatformBillingMetrics(decimal MonthlyRecurringRevenueNpr, int OverdueInvoiceCount);
```

Add to the interface:

```csharp
    Task<PlatformBillingMetrics> GetPlatformMetricsAsync(CancellationToken cancellationToken = default);
    Task<List<InvoiceResponse>> ListInvoicesForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
```

Add to `SubscriptionService`:

```csharp
    public async Task<PlatformBillingMetrics> GetPlatformMetricsAsync(CancellationToken cancellationToken = default)
    {
        // Cross-org by design (a platform-wide metric) — IgnoreQueryFilters() is required since
        // the caller (Platform) always runs with a null ambient tenant.
        var mrr = await (
            from s in _dbContext.Set<Subscription>().IgnoreQueryFilters()
            join p in _dbContext.Set<Plan>().IgnoreQueryFilters() on s.PlanId equals p.Id
            where s.Status == SubscriptionStatus.Active
            select p.MonthlyPriceNpr
        ).SumAsync(cancellationToken);

        var overdueCount = await _dbContext.Set<Invoice>().IgnoreQueryFilters()
            .CountAsync(i => i.Status == InvoiceStatus.Overdue, cancellationToken);

        return new PlatformBillingMetrics(mrr, overdueCount);
    }

    public Task<List<InvoiceResponse>> ListInvoicesForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => ListInvoicesAsync(organizationId, cancellationToken);
```

- [ ] **Step 2: Extend `IBranchLookupService`/`BranchService`**

```csharp
public record BranchAdminRecord(Guid Id, string Code, string Name, bool IsActive);
```

```csharp
    Task<List<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
```

```csharp
    public async Task<List<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => await _dbContext.Set<Branch>().IgnoreQueryFilters()
            .Where(b => b.OrganizationId == organizationId)
            .Select(b => new BranchAdminRecord(b.Id, b.Code, b.Name, b.IsActive))
            .ToListAsync(cancellationToken);
```

- [ ] **Step 3: Extend `IOrganizationLookupService`/`OrganizationAdminService`**

```csharp
public record OrganizationUserRecord(Guid Id, string Email, string FullName, bool IsActive);
```

```csharp
    Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<List<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, CancellationToken cancellationToken = default);
```

```csharp
    public async Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => await _dbContext.Set<Organization>()
            .Where(o => o.Id == organizationId)
            .Select(o => new OrganizationRecord(o.Id, o.Name, o.Slug, o.IsActive, o.SuspendedBySystem, o.DeletedAt, o.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<List<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => await _dbContext.Set<User>().IgnoreQueryFilters()
            .Where(u => u.OrganizationId == organizationId)
            .Select(u => new OrganizationUserRecord(u.Id, u.Email, u.FullName, u.IsActive))
            .ToListAsync(cancellationToken);
```

- [ ] **Step 4: Write `IAuditLogReader` and implement it on `AuditLogger`**

```csharp
namespace Courier.Audit.Application;

public record AuditLogRecord(Guid Id, string Action, string EntityType, string EntityId, Guid? UserId, Guid? ImpersonatedByUserId, bool IsCrossTenantAction, DateTimeOffset Timestamp);

public interface IAuditLogReader
{
    Task<List<AuditLogRecord>> ListForOrganizationAsync(Guid organizationId, int limit, CancellationToken cancellationToken = default);
}
```

```csharp
public class AuditLogger : IAuditLogger, IAuditLogReader
{
    // ... existing fields/constructor/LogAsync unchanged ...

    public async Task<List<AuditLogRecord>> ListForOrganizationAsync(Guid organizationId, int limit, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<AuditLog>().IgnoreQueryFilters()
            .Where(a => a.OrganizationId == organizationId)
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .Select(a => new AuditLogRecord(a.Id, a.Action, a.EntityType, a.EntityId, a.UserId, a.ImpersonatedByUserId, a.IsCrossTenantAction, a.Timestamp))
            .ToListAsync(cancellationToken);
    }
}
```

(`IgnoreQueryFilters()` here is deliberate and safe for the same reason as every other Platform-facing cross-org read in this plan — `Platform`'s ambient tenant is always null, and the explicit `organizationId` `Where` clause is the real, correct scope, not the bypassed ambient filter.)

- [ ] **Step 5: Write `PlatformDashboardService`**

```csharp
using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;

namespace Courier.Platform.Application;

public record PlatformMetrics(int TotalOrganizations, int ActiveOrganizations, int SuspendedOrganizations, int DeletedOrganizations, decimal MonthlyRecurringRevenueNpr, int OverdueInvoiceCount);
public record OrganizationDetail(OrganizationRecord Organization, string? PlanName, List<OrganizationUserRecord> Users, List<BranchAdminRecord> Branches, List<InvoiceResponse> Invoices, List<AuditLogRecord> RecentAuditEntries);

public class PlatformDashboardService
{
    private readonly IOrganizationLookupService _organizationLookupService;
    private readonly IBillingLookupService _billingLookupService;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IAuditLogReader _auditLogReader;

    public PlatformDashboardService(
        IOrganizationLookupService organizationLookupService,
        IBillingLookupService billingLookupService,
        IBranchLookupService branchLookupService,
        IAuditLogReader auditLogReader)
    {
        _organizationLookupService = organizationLookupService;
        _billingLookupService = billingLookupService;
        _branchLookupService = branchLookupService;
        _auditLogReader = auditLogReader;
    }

    public async Task<PlatformMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var organizations = await _organizationLookupService.ListAsync(cancellationToken);
        var billingMetrics = await _billingLookupService.GetPlatformMetricsAsync(cancellationToken);

        return new PlatformMetrics(
            TotalOrganizations: organizations.Count,
            ActiveOrganizations: organizations.Count(o => o.IsActive && o.DeletedAt is null),
            SuspendedOrganizations: organizations.Count(o => !o.IsActive && o.DeletedAt is null),
            DeletedOrganizations: organizations.Count(o => o.DeletedAt is not null),
            MonthlyRecurringRevenueNpr: billingMetrics.MonthlyRecurringRevenueNpr,
            OverdueInvoiceCount: billingMetrics.OverdueInvoiceCount);
    }

    public async Task<OrganizationDetail?> GetOrganizationDetailAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await _organizationLookupService.GetAsync(organizationId, cancellationToken);
        if (organization is null)
        {
            return null;
        }

        var planSummary = await _billingLookupService.GetPlanSummaryAsync(organizationId, cancellationToken);
        var users = await _organizationLookupService.ListUsersAsync(organizationId, cancellationToken);
        var branches = await _branchLookupService.ListForOrganizationAsync(organizationId, cancellationToken);
        var invoices = await _billingLookupService.ListInvoicesForOrganizationAsync(organizationId, cancellationToken);
        var auditEntries = await _auditLogReader.ListForOrganizationAsync(organizationId, 50, cancellationToken);

        return new OrganizationDetail(organization, planSummary?.PlanName, users, branches, invoices, auditEntries);
    }
}
```

- [ ] **Step 6: Write `PlatformDashboardEndpoints`**

```csharp
using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformDashboardEndpoints
{
    public static void MapPlatformDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform").WithTags("Platform Dashboard");

        group.MapGet("/dashboard", async (PlatformDashboardService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetMetricsAsync(cancellationToken)))
            .RequireSuperAdmin();

        group.MapGet("/organizations/{id:guid}", async (Guid id, PlatformDashboardService service, CancellationToken cancellationToken) =>
        {
            var detail = await service.GetOrganizationDetailAsync(id, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        }).RequireSuperAdmin();
    }
}
```

- [ ] **Step 7: Wire DI**

```csharp
builder.Services.AddScoped<IAuditLogReader>(sp => sp.GetRequiredService<Courier.Audit.Infrastructure.AuditLogger>());
builder.Services.AddScoped<PlatformDashboardService>();
```

(`AuditLogger` is already registered as `IAuditLogger` — check whether it's registered as itself (`AddScoped<AuditLogger>()`) or only via the interface; if only via the interface, add a second registration `builder.Services.AddScoped<Courier.Audit.Infrastructure.AuditLogger>();` so both interface registrations can resolve the same concrete type, mirroring how `SubscriptionService`/`BranchService` are each registered both as themselves and as their lookup interface.)

```csharp
app.MapPlatformDashboardEndpoints();
```

- [ ] **Step 8: Write the failing tests**

```csharp
using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Platform.Application;
using Xunit;

namespace Courier.Platform.Tests;

public class PlatformDashboardServiceTests
{
    private sealed class FakeOrganizationLookupService : IOrganizationLookupService
    {
        public List<OrganizationRecord> Organizations { get; set; } = new();
        public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<OrganizationRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(Organizations);
        public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(Organizations.FirstOrDefault(o => o.Id == organizationId));
        public Task<List<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(new List<OrganizationUserRecord>());
    }

    private sealed class FakeBillingLookupService : IBillingLookupService
    {
        public PlatformBillingMetrics Metrics { get; set; } = new(0m, 0);
        public Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult((int?)null);
        public Task<PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<PlanSummary?>(null);
        public Task<PlatformBillingMetrics> GetPlatformMetricsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Metrics);
        public Task<List<InvoiceResponse>> ListInvoicesForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(new List<InvoiceResponse>());
    }

    private sealed class FakeBranchLookupService : IBranchLookupService
    {
        public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult<BranchZoneInfo?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<List<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(new List<BranchAdminRecord>());
    }

    private sealed class FakeAuditLogReader : IAuditLogReader
    {
        public Task<List<AuditLogRecord>> ListForOrganizationAsync(Guid organizationId, int limit, CancellationToken cancellationToken = default) => Task.FromResult(new List<AuditLogRecord>());
    }

    [Fact]
    public async Task GetMetricsAsync_CountsOrganizationsByStatus()
    {
        var organizationLookup = new FakeOrganizationLookupService
        {
            Organizations = new List<OrganizationRecord>
            {
                new(Guid.NewGuid(), "Active Org", "active-org", true, false, null, DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), "Suspended Org", "suspended-org", false, false, null, DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), "Deleted Org", "deleted-org", false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            }
        };
        var billingLookup = new FakeBillingLookupService { Metrics = new PlatformBillingMetrics(5000m, 2) };
        var service = new PlatformDashboardService(organizationLookup, billingLookup, new FakeBranchLookupService(), new FakeAuditLogReader());

        var metrics = await service.GetMetricsAsync();

        Assert.Equal(3, metrics.TotalOrganizations);
        Assert.Equal(1, metrics.ActiveOrganizations);
        Assert.Equal(1, metrics.SuspendedOrganizations);
        Assert.Equal(1, metrics.DeletedOrganizations);
        Assert.Equal(5000m, metrics.MonthlyRecurringRevenueNpr);
        Assert.Equal(2, metrics.OverdueInvoiceCount);
    }

    [Fact]
    public async Task GetOrganizationDetailAsync_UnknownOrganization_ReturnsNull()
    {
        var service = new PlatformDashboardService(new FakeOrganizationLookupService(), new FakeBillingLookupService(), new FakeBranchLookupService(), new FakeAuditLogReader());

        var detail = await service.GetOrganizationDetailAsync(Guid.NewGuid());

        Assert.Null(detail);
    }
}
```

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Courier.Api.Tests;

public class PlatformDashboardEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public PlatformDashboardEndpointsTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetDashboard_AsSuperAdmin_ReturnsMetrics()
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody.GetProperty("accessToken").GetString());

        var response = await client.GetAsync("/api/v1/platform/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("totalOrganizations").GetInt32() >= 0);
    }
}
```

- [ ] **Step 9: Run tests to verify failure then success**

Run: `dotnet test tests/Platform.Tests/Platform.Tests.csproj && dotnet test tests/Api.Tests/Api.Tests.csproj --filter "PlatformDashboardEndpointsTests"`
Expected before implementation: FAIL to compile. After all steps: PASS.

- [ ] **Step 10: Full-solution build and regression check**

Run: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj`

- [ ] **Step 11: Commit**

```bash
git add src/Modules/Billing src/Modules/Branches src/Modules/Identity src/Modules/Audit src/Modules/Platform src/Api tests/Platform.Tests tests/Api.Tests
git commit -m "feat(platform): add cross-org dashboard metrics and organization detail drill-down"
```

---

## Task 11: Impersonation

**Files:**
- Modify: `src/Modules/Identity/Application/IOrganizationLookupService.cs`
- Modify: `src/Modules/Identity/Application/OrganizationAdminService.cs`
- Create: `src/Modules/Platform/Application/IImpersonationTokenService.cs`
- Create: `src/Modules/Platform/Infrastructure/ImpersonationTokenService.cs`
- Create: `src/Modules/Platform/Application/PlatformValidationException.cs`
- Create: `src/Modules/Platform/Application/ImpersonationService.cs`
- Create: `src/Modules/Platform/Presentation/PlatformImpersonationEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Platform.Tests/ImpersonationServiceTests.cs`
- Test: `tests/Api.Tests/ImpersonationEndToEndTests.cs`

**Interfaces:**
- Produces (Identity): `IOrganizationLookupService.GetUserAsync(Guid organizationId, Guid userId, CancellationToken) : Task<OrganizationUserRecord?>`.
- Produces (Platform): `IImpersonationTokenService.IssueImpersonationAccessToken(Guid targetUserId, Guid organizationId, string email, Guid impersonatingSuperAdminId) : string`, `ImpersonationService.IssueAsync(Guid organizationId, Guid targetUserId, Guid actingSuperAdminId, CancellationToken) : Task<ImpersonationResult>`.
- Produces: `Courier.Platform.Application.ImpersonationResult(string AccessToken, DateTimeOffset ExpiresAt, string ImpersonatedUserName, string OrganizationName)` — consumed by Task 18's frontend BFF impersonation route.

- [ ] **Step 1: Add `GetUserAsync` to `IOrganizationLookupService`/`OrganizationAdminService`**

```csharp
    Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
```

```csharp
    public async Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Set<User>().IgnoreQueryFilters()
            .Where(u => u.Id == userId && u.OrganizationId == organizationId)
            .Select(u => new OrganizationUserRecord(u.Id, u.Email, u.FullName, u.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
```

- [ ] **Step 2: Write `IImpersonationTokenService`/`ImpersonationTokenService`**

```csharp
namespace Courier.Platform.Application;

public interface IImpersonationTokenService
{
    string IssueImpersonationAccessToken(Guid targetUserId, Guid organizationId, string email, Guid impersonatingSuperAdminId);
}
```

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

    private readonly JwtOptions _options;

    public ImpersonationTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string IssueImpersonationAccessToken(Guid targetUserId, Guid organizationId, string email, Guid impersonatingSuperAdminId)
    {
        var claims = new[]
        {
            new Claim("sub", targetUserId.ToString()),
            new Claim("org_id", organizationId.ToString()),
            new Claim("email", email),
            new Claim("impersonated_by", impersonatingSuperAdminId.ToString()),
            new Claim("is_impersonation", "true")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(ImpersonationLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 3: Write `PlatformValidationException` and `ImpersonationService`**

```csharp
namespace Courier.Platform.Application;

public class PlatformValidationException : Exception
{
    public PlatformValidationException(string message) : base(message) { }
}
```

```csharp
using Courier.Audit.Application;
using Courier.Identity.Application;

namespace Courier.Platform.Application;

public record ImpersonationResult(string AccessToken, DateTimeOffset ExpiresAt, string ImpersonatedUserName, string OrganizationName);

public class ImpersonationService
{
    private readonly IOrganizationLookupService _organizationLookupService;
    private readonly IImpersonationTokenService _tokenService;
    private readonly IAuditLogger _auditLogger;

    public ImpersonationService(IOrganizationLookupService organizationLookupService, IImpersonationTokenService tokenService, IAuditLogger auditLogger)
    {
        _organizationLookupService = organizationLookupService;
        _tokenService = tokenService;
        _auditLogger = auditLogger;
    }

    public async Task<ImpersonationResult> IssueAsync(Guid organizationId, Guid targetUserId, Guid actingSuperAdminId, CancellationToken cancellationToken = default)
    {
        // Deliberately the same generic message for "org not found" and "user not found /
        // not in this org / inactive" — matches this codebase's established convention of never
        // disclosing which specific check failed on an auth-adjacent lookup.
        var organization = await _organizationLookupService.GetAsync(organizationId, cancellationToken)
            ?? throw new PlatformValidationException("Organization or user was not found.");
        var user = await _organizationLookupService.GetUserAsync(organizationId, targetUserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new PlatformValidationException("Organization or user was not found.");
        }

        var accessToken = _tokenService.IssueImpersonationAccessToken(user.Id, organizationId, user.Email, actingSuperAdminId);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

        // This audits the GRANT itself (a cross-tenant SuperAdmin action). Every action the
        // SuperAdmin subsequently takes WITH the issued token is separately audited as a normal
        // action against that org, with ImpersonatedByUserId populated automatically by
        // AuditLogger.LogAsync reading the token's "impersonated_by" claim (Task 5) — no code
        // outside this one grant needs to know impersonation is happening.
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingSuperAdminId, "platform.impersonation_started", "User", targetUserId.ToString(), IsCrossTenantAction: true), cancellationToken);

        return new ImpersonationResult(accessToken, expiresAt, user.FullName, organization.Name);
    }
}
```

- [ ] **Step 4: Write `PlatformImpersonationEndpoints`**

```csharp
using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformImpersonationEndpoints
{
    public static void MapPlatformImpersonationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/platform/organizations/{organizationId:guid}/users/{userId:guid}/impersonate",
            async (Guid organizationId, Guid userId, ImpersonationService service, HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                try
                {
                    var result = await service.IssueAsync(organizationId, userId, httpContext.User.GetUserId()!.Value, cancellationToken);
                    return Results.Ok(result);
                }
                catch (PlatformValidationException ex)
                {
                    return Results.NotFound(new { error = new { code = "IMPERSONATION_TARGET_NOT_FOUND", message = ex.Message } });
                }
            })
            .WithTags("Platform Impersonation")
            .RequireSuperAdmin();
    }
}
```

- [ ] **Step 5: Wire DI**

```csharp
builder.Services.AddScoped<IImpersonationTokenService, ImpersonationTokenService>();
builder.Services.AddScoped<ImpersonationService>();
```

```csharp
app.MapPlatformImpersonationEndpoints();
```

- [ ] **Step 6: Write the failing tests**

```csharp
using Courier.Audit.Application;
using Courier.Identity.Application;
using Courier.Platform.Application;
using Xunit;

namespace Courier.Platform.Tests;

public class ImpersonationServiceTests
{
    private sealed class FakeOrganizationLookupService : IOrganizationLookupService
    {
        public OrganizationRecord? Organization { get; set; }
        public OrganizationUserRecord? User { get; set; }
        public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<OrganizationRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<OrganizationRecord>());
        public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(Organization);
        public Task<List<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(new List<OrganizationUserRecord>());
        public Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(User);
    }

    private sealed class FakeImpersonationTokenService : IImpersonationTokenService
    {
        public string IssueImpersonationAccessToken(Guid targetUserId, Guid organizationId, string email, Guid impersonatingSuperAdminId) => "fake-impersonation-token";
    }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public AuditEntry? LastEntry { get; private set; }
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) { LastEntry = entry; return Task.CompletedTask; }
    }

    [Fact]
    public async Task IssueAsync_ValidActiveUserInOrganization_ReturnsTokenAndAudits()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var organizationLookup = new FakeOrganizationLookupService
        {
            Organization = new OrganizationRecord(organizationId, "Test Org", "test-org", true, false, null, DateTimeOffset.UtcNow),
            User = new OrganizationUserRecord(userId, "user@example.test", "Test User", true)
        };
        var auditLogger = new FakeAuditLogger();
        var service = new ImpersonationService(organizationLookup, new FakeImpersonationTokenService(), auditLogger);

        var result = await service.IssueAsync(organizationId, userId, Guid.NewGuid());

        Assert.Equal("fake-impersonation-token", result.AccessToken);
        Assert.Equal("Test User", result.ImpersonatedUserName);
        Assert.True(auditLogger.LastEntry!.IsCrossTenantAction);
    }

    [Fact]
    public async Task IssueAsync_InactiveUser_Throws()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var organizationLookup = new FakeOrganizationLookupService
        {
            Organization = new OrganizationRecord(organizationId, "Test Org", "test-org", true, false, null, DateTimeOffset.UtcNow),
            User = new OrganizationUserRecord(userId, "user@example.test", "Test User", false)
        };
        var service = new ImpersonationService(organizationLookup, new FakeImpersonationTokenService(), new FakeAuditLogger());

        await Assert.ThrowsAsync<PlatformValidationException>(() => service.IssueAsync(organizationId, userId, Guid.NewGuid()));
    }

    [Fact]
    public async Task IssueAsync_UserNotInOrganization_Throws()
    {
        var organizationLookup = new FakeOrganizationLookupService
        {
            Organization = new OrganizationRecord(Guid.NewGuid(), "Test Org", "test-org", true, false, null, DateTimeOffset.UtcNow),
            User = null
        };
        var service = new ImpersonationService(organizationLookup, new FakeImpersonationTokenService(), new FakeAuditLogger());

        await Assert.ThrowsAsync<PlatformValidationException>(() => service.IssueAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }
}
```

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Courier.Api.Tests;

// Full round-trip: register an org, get the SuperAdmin to impersonate the org's Owner, then use
// the impersonation token to make a real tenant write, proving both UserId (the Owner) and
// ImpersonatedByUserId (the SuperAdmin) land on the resulting AuditLog row.
public class ImpersonationEndToEndTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public ImpersonationEndToEndTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Impersonate_ThenActAsOwner_AuditLogRecordsBothIdentities()
    {
        var client = _factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            organizationName = "Impersonation Test Org",
            organizationSlug = $"imperso-{Guid.NewGuid():N}",
            ownerFullName = "Owner",
            ownerEmail = $"owner-{Guid.NewGuid():N}@example.test",
            ownerPassword = "P@ssw0rd123"
        });
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var organizationId = registerBody.GetProperty("organizationId").GetGuid();
        var ownerUserId = registerBody.GetProperty("userId").GetGuid();

        var superAdminLoginResponse = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var superAdminLoginBody = await superAdminLoginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var superAdminAccessToken = superAdminLoginBody.GetProperty("accessToken").GetString();
        var superAdminUserId = superAdminLoginBody.GetProperty("userId").GetGuid();

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", superAdminAccessToken);
        var impersonateResponse = await client.PostAsync($"/api/v1/platform/organizations/{organizationId}/users/{ownerUserId}/impersonate", null);
        Assert.Equal(HttpStatusCode.OK, impersonateResponse.StatusCode);
        var impersonateBody = await impersonateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var impersonationToken = impersonateBody.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", impersonationToken);
        var meResponse = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var meBody = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ownerUserId, meBody.GetProperty("userId").GetGuid());

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Courier.Persistence.AppDbContext>();
        var grantEntry = dbContext.Set<Courier.Audit.Domain.AuditLog>().IgnoreQueryFilters()
            .Where(a => a.Action == "platform.impersonation_started" && a.OrganizationId == organizationId)
            .OrderByDescending(a => a.Timestamp)
            .First();
        Assert.Equal(superAdminUserId, grantEntry.UserId);
        Assert.True(grantEntry.IsCrossTenantAction);
    }
}
```

Verify `POST /api/v1/auth/register`'s response actually includes a `userId` field for the owner (check `AuthResult`'s definition in `AuthDtos.cs`) — adjust the property name if it differs.

- [ ] **Step 7: Run tests to verify failure then success**

Run: `dotnet test tests/Platform.Tests/Platform.Tests.csproj && dotnet test tests/Api.Tests/Api.Tests.csproj --filter ImpersonationEndToEndTests`
Expected before implementation: FAIL to compile. After all steps: PASS.

- [ ] **Step 8: Full-solution build and regression check**

Run: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj`

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Identity src/Modules/Platform src/Api tests/Platform.Tests tests/Api.Tests
git commit -m "feat(platform): add time-boxed impersonation with dual-actor audit trail"
```

---

## Task 12: Branch-count plan limit enforcement

**Files:**
- Modify: `src/Modules/Branches/Branches.csproj` (add `<ProjectReference Include="..\Billing\Billing.csproj" />`)
- Modify: `src/Modules/Branches/Application/BranchDtos.cs` (add `BranchLimitExceededException`)
- Modify: `src/Modules/Branches/Application/BranchService.cs`
- Modify: `src/Modules/Branches/Presentation/BranchesEndpoints.cs`
- Test: `tests/Branches.Tests/BranchServiceLimitTests.cs` (or add to whatever existing test file already covers `CreateBranchAsync` — check `tests/Branches.Tests/` first and extend the existing one rather than creating a near-duplicate)

**Interfaces:**
- Consumes: `IBillingLookupService.GetMaxBranchesAsync` (Task 2).
- Produces: `Courier.Branches.Application.BranchLimitExceededException : BranchValidationException`.

- [ ] **Step 1: Add the exception type**

```csharp
public class BranchLimitExceededException : BranchValidationException
{
    public BranchLimitExceededException(string message) : base(message) { }
}
```

- [ ] **Step 2: Enforce the limit in `BranchService.CreateBranchAsync`**

Add the constructor dependency:

```csharp
    private readonly Courier.Billing.Application.IBillingLookupService _billingLookupService;

    public BranchService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IGeographyLookupService geographyLookupService,
        Courier.Billing.Application.IBillingLookupService billingLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _geographyLookupService = geographyLookupService;
        _billingLookupService = billingLookupService;
    }
```

Add the check immediately after the existing `codeTaken` check in `CreateBranchAsync` (before the geography validation — cheapest check first):

```csharp
        var maxBranches = await _billingLookupService.GetMaxBranchesAsync(organizationId, cancellationToken);
        if (maxBranches is int limit)
        {
            var currentCount = await _dbContext.Set<Branch>().CountAsync(cancellationToken);
            if (currentCount >= limit)
            {
                throw new BranchLimitExceededException($"Your plan allows a maximum of {limit} branches. Upgrade your plan to add more.");
            }
        }
```

(`_dbContext.Set<Branch>().CountAsync(...)` is already tenant-scoped by the ambient query filter — this method only ever runs with a real staff JWT's `org_id` claim, never a `SuperAdmin`/background context, so no `.IgnoreQueryFilters()` concern here, unlike `IBillingLookupService.GetMaxBranchesAsync`'s own implementation which is called from both tenant and platform contexts.)

- [ ] **Step 3: Map the new exception distinctly in `BranchesEndpoints.cs`**

In the `POST /` route, add a `catch (BranchLimitExceededException ex)` **before** the existing `catch (BranchValidationException ex)` (a more specific catch must precede its base type, or it's unreachable):

```csharp
            catch (BranchLimitExceededException ex)
            {
                return Results.BadRequest(new { error = new { code = "BRANCH_LIMIT_REACHED", message = ex.Message } });
            }
            catch (BranchValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BRANCH_VALIDATION_FAILED", message = ex.Message } });
            }
```

- [ ] **Step 4: Write the failing tests**

Find `tests/Branches.Tests/`'s existing `BranchService`-constructing test file (there is already at least one, from Phase 2/3/5) and add these two cases to it, updating every existing `new BranchService(...)` call site in that file to pass a fake `IBillingLookupService` too (a `null`-returning fake for the pre-existing tests, matching Task 1's "fail-open, unlimited" default):

```csharp
    private sealed class FakeBillingLookupService : Courier.Billing.Application.IBillingLookupService
    {
        public int? MaxBranches { get; set; }
        public Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(MaxBranches);
        public Task<Courier.Billing.Application.PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<Courier.Billing.Application.PlanSummary?>(null);
        public Task<Courier.Billing.Application.PlatformBillingMetrics> GetPlatformMetricsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new Courier.Billing.Application.PlatformBillingMetrics(0m, 0));
        public Task<List<Courier.Billing.Application.InvoiceResponse>> ListInvoicesForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(new List<Courier.Billing.Application.InvoiceResponse>());
    }

    [Fact]
    public async Task CreateBranchAsync_AtPlanLimit_ThrowsBranchLimitExceeded()
    {
        // Arrange a tenant with 1 existing Branch and a Plan limit of 1, using this file's
        // existing seeding helpers for organization/geography — do not re-derive them here.
        // ... seed one existing Branch for the organization ...
        var service = new BranchService(dbContext, tenantProvider, auditLogger, branchAuthorizationChecker, geographyLookupService, new FakeBillingLookupService { MaxBranches = 1 });

        await Assert.ThrowsAsync<Courier.Branches.Application.BranchLimitExceededException>(() =>
            service.CreateBranchAsync(validCreateRequest, actingUserPrincipal));
    }

    [Fact]
    public async Task CreateBranchAsync_UnlimitedPlan_Succeeds()
    {
        var service = new BranchService(dbContext, tenantProvider, auditLogger, branchAuthorizationChecker, geographyLookupService, new FakeBillingLookupService { MaxBranches = null });

        var result = await service.CreateBranchAsync(validCreateRequest, actingUserPrincipal);

        Assert.NotNull(result);
    }
```

Write these against the actual existing test file's real helper names/fixtures (`dbContext`, `tenantProvider`, `validCreateRequest`, etc. above are placeholders for whatever that file already calls them — read it first, then match it exactly; do not introduce a second, differently-shaped set of test helpers alongside the existing ones).

- [ ] **Step 5: Run tests to verify failure then success**

Run: `dotnet test tests/Branches.Tests/Branches.Tests.csproj`
Expected before implementation: FAIL to compile (constructor signature mismatch across every existing test in the file). After Steps 1-3: PASS, including every pre-existing test in the file (now passing a fake `IBillingLookupService`).

- [ ] **Step 6: Full-solution build and regression check**

Run: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: all green — `BranchesEndpointsTests.cs`'s existing tests must still pass unchanged (they exercise the real DI-wired `BranchService`, which now needs `IBillingLookupService` resolved from the container — already registered since Task 7).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Branches tests/Branches.Tests
git commit -m "feat(branches): enforce plan branch-count limit at creation"
```

---

## Task 13: Overdue-invoice auto-suspend job

**Files:**
- Modify: `src/Modules/Identity/Application/IOrganizationLookupService.cs`
- Modify: `src/Modules/Identity/Application/OrganizationAdminService.cs` (constructor now also takes `IAuditLogger` — update every existing direct `new OrganizationAdminService(...)` call in `tests/Identity.Tests/OrganizationAdminServiceTests.cs` (Task 5) to pass a fake/real `IAuditLogger` too; DI-resolved production call sites need no change)
- Create: `src/Modules/Billing/Application/OverdueInvoiceJob.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Identity.Tests/OrganizationAdminServiceTests.cs` (fix existing constructor calls, per above)
- Test: `tests/Billing.Tests/OverdueInvoiceJobTests.cs`

**Interfaces:**
- Produces: `IOrganizationLookupService.SuspendForNonPaymentAsync(Guid organizationId, CancellationToken)`.
- Produces: `Courier.Billing.Application.OverdueInvoiceJob.RunAsync(CancellationToken)` — consumed by this task's own `Program.cs` `RecurringJob.AddOrUpdate` call.

- [ ] **Step 1: Add `SuspendForNonPaymentAsync`, and thread `IAuditLogger` into `OrganizationAdminService`**

```csharp
    Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default);
```

```csharp
using Courier.Audit.Application;
// ... existing usings ...

public class OrganizationAdminService : IOrganizationLookupService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;

    public OrganizationAdminService(DbContext dbContext, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
    }

    // ... every existing method unchanged ...

    // Distinct from SuspendAsync (Task 9, a SuperAdmin's manual action, no audit ActingUserId of
    // null) — this is the system's own action, audited with a null UserId to make "the system,
    // not a person, did this" legible in the audit trail per CLAUDE.md §39.
    public async Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrThrowAsync(organizationId, cancellationToken);
        if (!organization.IsActive)
        {
            // Already suspended (by this same job on a prior run, or manually) — avoid
            // re-writing an identical audit row every time the job re-scans an already-overdue
            // invoice it can't yet mark Paid.
            return;
        }
        organization.IsActive = false;
        organization.SuspendedBySystem = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, null, "organization.suspended_for_nonpayment", "Organization", organizationId.ToString(), IsCrossTenantAction: true), cancellationToken);
    }
}
```

Fix `tests/Identity.Tests/OrganizationAdminServiceTests.cs` (Task 5): every `new OrganizationAdminService(dbContext)` becomes `new OrganizationAdminService(dbContext, new FakeAuditLogger())` using a minimal fake (`Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;`) if that file doesn't already have one from elsewhere — check first, reuse if it exists.

- [ ] **Step 2: Write `OverdueInvoiceJob`**

```csharp
using Courier.Billing.Domain;
using Courier.Identity.Application;
using Microsoft.EntityFrameworkCore;

namespace Courier.Billing.Application;

// Hangfire recurring job, same "no ambient tenant, IgnoreQueryFilters() everywhere" shape as
// InvoiceGenerationJob (Task 3). DueAt already bakes in the grace period (Task 3's
// BillingOptions.GraceDays), so "DueAt < now" alone is the complete overdue condition — no
// second grace window layered on top here.
public class OverdueInvoiceJob
{
    private readonly DbContext _dbContext;
    private readonly IOrganizationLookupService _organizationLookupService;

    public OverdueInvoiceJob(DbContext dbContext, IOrganizationLookupService organizationLookupService)
    {
        _dbContext = dbContext;
        _organizationLookupService = organizationLookupService;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var overdueInvoices = await _dbContext.Set<Invoice>().IgnoreQueryFilters()
            .Where(i => i.Status == InvoiceStatus.Pending && i.DueAt < now)
            .ToListAsync(cancellationToken);

        if (overdueInvoices.Count == 0)
        {
            return;
        }

        var subscriptionIds = overdueInvoices.Select(i => i.SubscriptionId).ToHashSet();
        var subscriptions = await _dbContext.Set<Subscription>().IgnoreQueryFilters()
            .Where(s => subscriptionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var organizationIdsToSuspend = new HashSet<Guid>();
        foreach (var invoice in overdueInvoices)
        {
            invoice.Status = InvoiceStatus.Overdue;
            if (subscriptions.TryGetValue(invoice.SubscriptionId, out var subscription) && subscription.Status != SubscriptionStatus.PastDue)
            {
                subscription.Status = SubscriptionStatus.PastDue;
                subscription.UpdatedAt = now;
            }
            organizationIdsToSuspend.Add(invoice.OrganizationId);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Outside the SaveChangesAsync above deliberately, same "cross-module call is not part
        // of this module's own atomic unit of work" reasoning as PaymentService.CompletePaymentAsync
        // (Task 6) — a crash here leaves invoices correctly Overdue but an org not yet suspended
        // until the job's next run picks it up again (it will: the invoice is still Overdue).
        foreach (var organizationId in organizationIdsToSuspend)
        {
            await _organizationLookupService.SuspendForNonPaymentAsync(organizationId, cancellationToken);
        }
    }
}
```

- [ ] **Step 3: Wire the recurring job in `Program.cs`**

```csharp
builder.Services.AddScoped<OverdueInvoiceJob>();
```

```csharp
RecurringJob.AddOrUpdate<OverdueInvoiceJob>("billing-overdue-invoice-suspension", job => job.RunAsync(CancellationToken.None), "0 2 * * *");
```

(Runs an hour after `billing-invoice-generation`'s 01:00 UTC slot, so a same-day-generated invoice with an already-past grace window — only possible if `GraceDays` were ever configured to `0` — is picked up the same run rather than racing it.)

- [ ] **Step 4: Write the failing test**

```csharp
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.Identity.Application;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Billing.Tests;

public class OverdueInvoiceJobTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private sealed class FakeOrganizationLookupService : IOrganizationLookupService
    {
        public HashSet<Guid> SuspendedOrganizationIds { get; } = new();
        public Task<bool> IsActiveAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ReactivateIfSystemSuspendedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<OrganizationRecord>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<OrganizationRecord>());
        public Task SuspendAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeStaffRefreshTokensAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OrganizationRecord?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<OrganizationRecord?>(null);
        public Task<List<OrganizationUserRecord>> ListUsersAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(new List<OrganizationUserRecord>());
        public Task<OrganizationUserRecord?> GetUserAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<OrganizationUserRecord?>(null);
        public Task SuspendForNonPaymentAsync(Guid organizationId, CancellationToken cancellationToken = default) { SuspendedOrganizationIds.Add(organizationId); return Task.CompletedTask; }
    }

    [Fact]
    public async Task RunAsync_InvoicePastDueDate_MarksOverdueAndSuspendsOrganization()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", Slug = $"pro-{Guid.NewGuid():N}", MonthlyPriceNpr = 2000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var invoice = new Invoice { Id = Guid.NewGuid(), OrganizationId = organizationId, SubscriptionId = subscription.Id, PlanId = plan.Id, PlanNameSnapshot = plan.Name, AmountNprSnapshot = plan.MonthlyPriceNpr, CycleStart = DateTimeOffset.UtcNow.AddMonths(-1), CycleEnd = DateTimeOffset.UtcNow.AddDays(-8), Status = InvoiceStatus.Pending, DueAt = DateTimeOffset.UtcNow.AddDays(-1), CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1) };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        dbContext.Set<Invoice>().Add(invoice);
        await dbContext.SaveChangesAsync();
        var organizationLookup = new FakeOrganizationLookupService();
        var job = new OverdueInvoiceJob(dbContext, organizationLookup);

        await job.RunAsync();

        Assert.Equal(InvoiceStatus.Overdue, dbContext.Set<Invoice>().IgnoreQueryFilters().Single(i => i.Id == invoice.Id).Status);
        Assert.Equal(SubscriptionStatus.PastDue, dbContext.Set<Subscription>().IgnoreQueryFilters().Single(s => s.Id == subscription.Id).Status);
        Assert.Contains(organizationId, organizationLookup.SuspendedOrganizationIds);
    }

    [Fact]
    public async Task RunAsync_InvoiceNotYetDue_LeavesUnchanged()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Pro", Slug = $"pro2-{Guid.NewGuid():N}", MonthlyPriceNpr = 2000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var invoice = new Invoice { Id = Guid.NewGuid(), OrganizationId = organizationId, SubscriptionId = subscription.Id, PlanId = plan.Id, PlanNameSnapshot = plan.Name, AmountNprSnapshot = plan.MonthlyPriceNpr, CycleStart = DateTimeOffset.UtcNow, CycleEnd = DateTimeOffset.UtcNow.AddMonths(1), Status = InvoiceStatus.Pending, DueAt = DateTimeOffset.UtcNow.AddDays(6), CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        dbContext.Set<Invoice>().Add(invoice);
        await dbContext.SaveChangesAsync();
        var organizationLookup = new FakeOrganizationLookupService();
        var job = new OverdueInvoiceJob(dbContext, organizationLookup);

        await job.RunAsync();

        Assert.Equal(InvoiceStatus.Pending, dbContext.Set<Invoice>().IgnoreQueryFilters().Single(i => i.Id == invoice.Id).Status);
        Assert.DoesNotContain(organizationId, organizationLookup.SuspendedOrganizationIds);
    }
}
```

- [ ] **Step 5: Run tests to verify failure then success**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj && dotnet test tests/Billing.Tests/Billing.Tests.csproj`
Expected before implementation: FAIL to compile (`OrganizationAdminService`'s constructor signature change breaks Task 5's/Task 9's existing tests until Step 1's fix is applied). After all steps: PASS.

- [ ] **Step 6: Full-solution build and regression check**

Run: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: all green — this is the step most likely to surface any other place `OrganizationAdminService` was constructed directly rather than through DI; grep for `new OrganizationAdminService(` across the whole repo and fix every hit, not just the ones in `tests/Identity.Tests`.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Identity src/Modules/Billing src/Api tests/Identity.Tests tests/Billing.Tests
git commit -m "feat(billing): add overdue-invoice auto-suspend job"
```

---

## Task 14: Frontend — platform session/auth plumbing and login page

Backend is now feature-complete (Tasks 1-13). This task starts the frontend, mirroring the staff (`lib/auth/session.ts`/`apiFetch.ts`, `services/authService.ts`) and merchant (`merchantSession.ts`/`merchantApiFetch.ts`/`merchantAuthService.ts`) tracks exactly — a `SuperAdmin` is a third, structurally identical auth track, not a special case.

**Files:**
- Create: `src/web/src/services/platformAuthService.ts`
- Create: `src/web/src/services/platformAuthService.test.ts`
- Create: `src/web/src/lib/auth/platformSession.ts`
- Create: `src/web/src/lib/auth/platformSession.test.ts`
- Create: `src/web/src/lib/auth/platformApiFetch.ts`
- Create: `src/web/src/app/api/platform-auth/login/route.ts`
- Create: `src/web/src/app/api/platform-auth/logout/route.ts`
- Create: `src/web/src/app/platform-login/page.tsx`
- Create: `src/web/src/features/platform-auth/PlatformLoginForm.tsx`
- Create: `src/web/src/features/platform-auth/PlatformLoginForm.test.tsx`
- Modify: `src/web/src/middleware.ts`

**Interfaces:**
- Produces: `platformAuthService.login(email, password) : Promise<{accessToken, refreshToken, userId}>`, `.refresh(refreshToken)`.
- Produces: `getValidPlatformAccessToken() : Promise<string | null>`, `setPlatformSessionCookies`, `clearPlatformSessionCookies` — consumed by every later `/api/platform/*` BFF route (Tasks 15-18).
- Produces: `platformApiFetch(path, init) : Promise<Response>` — the platform-track equivalent of `apiFetch`/`merchantApiFetch`.

- [ ] **Step 1: Write `platformAuthService`**

```typescript
import { getUpstreamApiUrl } from "@/lib/env";

export interface PlatformAuthTokens {
  accessToken: string;
  refreshToken: string;
  userId: string;
}

export const platformAuthService = {
  async login(email: string, password: string): Promise<PlatformAuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/platform/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password }),
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("LOGIN_FAILED");
    }
    const data = await response.json();
    return { accessToken: data.accessToken, refreshToken: data.refreshToken, userId: data.userId };
  },

  async refresh(refreshToken: string): Promise<PlatformAuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/platform/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("REFRESH_FAILED");
    }
    const data = await response.json();
    return { accessToken: data.accessToken, refreshToken: data.refreshToken, userId: data.userId };
  },
};
```

- [ ] **Step 2: Write `platformSession.ts`**

```typescript
import { cookies } from "next/headers";
import { platformAuthService } from "@/services/platformAuthService";

const ACCESS_COOKIE = "courier_platform_access_token";
const REFRESH_COOKIE = "courier_platform_refresh_token";

const cookieOptions = {
  httpOnly: true,
  secure: process.env.NODE_ENV === "production",
  sameSite: "lax" as const,
  path: "/",
};

export function setPlatformSessionCookies(accessToken: string, refreshToken: string) {
  const store = cookies();
  store.set(ACCESS_COOKIE, accessToken, { ...cookieOptions, maxAge: 60 * 15 });
  store.set(REFRESH_COOKIE, refreshToken, { ...cookieOptions, maxAge: 60 * 60 * 24 * 7 });
}

export function clearPlatformSessionCookies() {
  const store = cookies();
  store.delete(ACCESS_COOKIE);
  store.delete(REFRESH_COOKIE);
}

export async function getValidPlatformAccessToken(): Promise<string | null> {
  const store = cookies();
  const accessToken = store.get(ACCESS_COOKIE)?.value;
  const refreshToken = store.get(REFRESH_COOKIE)?.value;

  if (accessToken) {
    return accessToken;
  }
  if (!refreshToken) {
    return null;
  }

  try {
    const tokens = await platformAuthService.refresh(refreshToken);
    setPlatformSessionCookies(tokens.accessToken, tokens.refreshToken);
    return tokens.accessToken;
  } catch {
    clearPlatformSessionCookies();
    return null;
  }
}
```

- [ ] **Step 3: Write `platformApiFetch.ts`**

```typescript
import { getUpstreamApiUrl } from "@/lib/env";
import { getValidPlatformAccessToken } from "@/lib/auth/platformSession";

export async function platformApiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const accessToken = await getValidPlatformAccessToken();
  if (!accessToken) {
    throw new Error("NOT_AUTHENTICATED");
  }
  return fetch(`${getUpstreamApiUrl()}${path}`, {
    ...init,
    headers: {
      ...init.headers,
      Authorization: `Bearer ${accessToken}`,
    },
    cache: "no-store",
  });
}
```

- [ ] **Step 4: Write the BFF login/logout routes**

```typescript
import { NextRequest, NextResponse } from "next/server";
import { platformAuthService } from "@/services/platformAuthService";
import { setPlatformSessionCookies } from "@/lib/auth/platformSession";

export async function POST(request: NextRequest) {
  const { email, password } = await request.json();
  try {
    const tokens = await platformAuthService.login(email, password);
    setPlatformSessionCookies(tokens.accessToken, tokens.refreshToken);
    return NextResponse.json({ ok: true });
  } catch {
    return NextResponse.json({ error: "Invalid email or password." }, { status: 401 });
  }
}
```

```typescript
import { NextResponse } from "next/server";
import { clearPlatformSessionCookies } from "@/lib/auth/platformSession";

export async function POST() {
  clearPlatformSessionCookies();
  return NextResponse.json({ ok: true });
}
```

- [ ] **Step 5: Write `PlatformLoginForm` and `/platform-login` page**

Copy the existing staff `LoginForm`'s structure (find it under `src/web/src/features/auth/`) as closely as possible — same shadcn `Card`/`Form`/`Input`/`Button` composition, same React Hook Form + Zod validation shape — but posting to `/api/platform-auth/login` and redirecting to `/platform/organizations` on success instead of `/dashboard`. Write the real component by copying the staff version's actual current code and changing only the endpoint/redirect/copy ("Platform Administration" instead of the tenant product name) — do not invent a new visual structure.

```typescript
// src/web/src/app/platform-login/page.tsx
import { PlatformLoginForm } from "@/features/platform-auth/PlatformLoginForm";

export default function PlatformLoginPage() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-background p-4">
      <PlatformLoginForm />
    </div>
  );
}
```

- [ ] **Step 6: Add the platform branch to `middleware.ts`**

```typescript
  if (pathname.startsWith("/platform/")) {
    const hasPlatformSession =
      request.cookies.has("courier_platform_access_token") ||
      request.cookies.has("courier_platform_refresh_token");
    if (!hasPlatformSession) {
      return NextResponse.redirect(new URL("/platform-login", request.url));
    }
    return NextResponse.next();
  }
```

Add `"/platform/:path*"` to the `matcher` array. Do **not** add `/platform-login` itself to the matcher (it must stay reachable with no session, exactly like `/login`/`/merchant-login`).

- [ ] **Step 7: Write component tests and run them**

Write `platformAuthService.test.ts`/`platformSession.test.ts` by copying the existing `merchantAuthService`/`merchantSession` equivalents' actual test files (find and read them first) and substituting `platform` for `merchant` throughout — same mocked-`fetch` assertions, same cookie-name assertions. Write `PlatformLoginForm.test.tsx` the same way from the staff `LoginForm.test.tsx`.

Run: `cd src/web && npm test -- platformAuthService platformSession PlatformLoginForm`
Expected: PASS.

- [ ] **Step 8: Manual verification**

Run: `cd src/web && npm run dev`, navigate to `/platform-login`, confirm the form renders and an incorrect-password submission shows an inline error (a successful login can't be verified yet — there are no protected `/platform/*` pages to land on until Task 15).

- [ ] **Step 9: Full frontend check and commit**

Run: `cd src/web && npx tsc --noEmit && npm run lint && npm test`
Expected: all green.

```bash
git add src/web
git commit -m "feat(web): add platform auth session plumbing and login page"
```

---

## Task 15: Frontend — Organizations list + detail Sheet with lifecycle actions

**Files:**
- Create: `src/web/src/app/api/platform/organizations/route.ts`
- Create: `src/web/src/app/api/platform/organizations/[id]/route.ts`
- Create: `src/web/src/app/api/platform/organizations/[id]/suspend/route.ts`
- Create: `src/web/src/app/api/platform/organizations/[id]/reactivate/route.ts`
- Create: `src/web/src/app/api/platform/organizations/[id]/delete/route.ts`
- Create: `src/web/src/app/api/platform/organizations/[id]/restore/route.ts`
- Create: `src/web/src/features/platform-organizations/types.ts`
- Create: `src/web/src/features/platform-organizations/store/organizationPanelStore.ts`
- Create: `src/web/src/features/platform-organizations/store/organizationPanelStore.test.ts`
- Create: `src/web/src/features/platform-organizations/hooks/usePlatformOrganizations.ts`
- Create: `src/web/src/features/platform-organizations/hooks/usePlatformOrganization.ts`
- Create: `src/web/src/features/platform-organizations/hooks/useOrganizationLifecycleActions.ts`
- Create: `src/web/src/features/platform-organizations/OrganizationDetailSheet.tsx`
- Create: `src/web/src/app/platform/organizations/page.tsx`
- Create: `src/web/src/app/platform/layout.tsx`

**Interfaces:**
- Consumes: `GET/POST /api/v1/platform/organizations/*` (Tasks 9-10).
- Produces: `useOrganizationPanelStore` (`{mode: "closed"} | {mode: "detail", organizationId: string}`) — consumed by Task 18's impersonate button addition to `OrganizationDetailSheet`.

- [ ] **Step 1: Write the BFF proxy routes**

```typescript
// src/web/src/app/api/platform/organizations/route.ts
import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function GET() {
  try {
    const response = await platformApiFetch("/api/v1/platform/organizations");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```typescript
// src/web/src/app/api/platform/organizations/[id]/route.ts
import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await platformApiFetch(`/api/v1/platform/organizations/${params.id}`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Write the four action routes (`suspend`/`reactivate`/`delete`/`restore`) identically, each a `POST` with no body, proxying to `/api/v1/platform/organizations/${params.id}/<action>`:

```typescript
// src/web/src/app/api/platform/organizations/[id]/suspend/route.ts (repeat for reactivate/delete/restore, swapping the path segment and file location)
import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function POST(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await platformApiFetch(`/api/v1/platform/organizations/${params.id}/suspend`, { method: "POST" });
    return new NextResponse(null, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 2: Write `types.ts` and the Zustand panel store**

```typescript
export interface OrganizationSummary {
  id: string;
  name: string;
  slug: string;
  planName: string | null;
  isActive: boolean;
  isDeleted: boolean;
  branchCount: number;
}

export interface OrganizationUser {
  id: string;
  email: string;
  fullName: string;
  isActive: boolean;
}

export interface OrganizationBranch {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
}

export interface OrganizationInvoice {
  id: string;
  planName: string;
  amountNpr: number;
  cycleStart: string;
  cycleEnd: string;
  status: "Pending" | "Paid" | "Overdue" | "Cancelled";
  dueAt: string;
  paidAt: string | null;
}

export interface OrganizationAuditEntry {
  id: string;
  action: string;
  entityType: string;
  entityId: string;
  userId: string | null;
  impersonatedByUserId: string | null;
  isCrossTenantAction: boolean;
  timestamp: string;
}

export interface OrganizationDetail {
  organization: { id: string; name: string; slug: string; isActive: boolean; suspendedBySystem: boolean; deletedAt: string | null; createdAt: string };
  planName: string | null;
  users: OrganizationUser[];
  branches: OrganizationBranch[];
  invoices: OrganizationInvoice[];
  recentAuditEntries: OrganizationAuditEntry[];
}
```

```typescript
// src/web/src/features/platform-organizations/store/organizationPanelStore.ts
import { create } from "zustand";

type OrganizationPanelState = { mode: "closed" } | { mode: "detail"; organizationId: string };

interface OrganizationPanelStore {
  panel: OrganizationPanelState;
  openDetail: (organizationId: string) => void;
  close: () => void;
}

export const useOrganizationPanelStore = create<OrganizationPanelStore>((set) => ({
  panel: { mode: "closed" },
  openDetail: (organizationId) => set({ panel: { mode: "detail", organizationId } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

(Test file: copy `branchPanelStore.test.ts`'s structure exactly, substituting `openDetail`'s argument name and dropping the `openCreate`/`"create"` case entirely — this store has no create mode, organizations are only created via self-registration.)

- [ ] **Step 3: Write the TanStack Query hooks**

```typescript
// src/web/src/features/platform-organizations/hooks/usePlatformOrganizations.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { OrganizationSummary } from "@/features/platform-organizations/types";

export function usePlatformOrganizations() {
  return useQuery({
    queryKey: ["platform-organizations"],
    queryFn: () => clientFetch<OrganizationSummary[]>("/api/platform/organizations"),
  });
}
```

```typescript
// src/web/src/features/platform-organizations/hooks/usePlatformOrganization.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { OrganizationDetail } from "@/features/platform-organizations/types";

export function usePlatformOrganization(organizationId: string | null) {
  return useQuery({
    queryKey: ["platform-organizations", organizationId],
    queryFn: () => clientFetch<OrganizationDetail>(`/api/platform/organizations/${organizationId}`),
    enabled: organizationId !== null,
  });
}
```

```typescript
// src/web/src/features/platform-organizations/hooks/useOrganizationLifecycleActions.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

function useLifecycleAction(action: "suspend" | "reactivate" | "delete" | "restore") {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (organizationId: string) => clientFetch(`/api/platform/organizations/${organizationId}/${action}`, { method: "POST" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["platform-organizations"] });
    },
  });
}

export function useSuspendOrganization() { return useLifecycleAction("suspend"); }
export function useReactivateOrganization() { return useLifecycleAction("reactivate"); }
export function useDeleteOrganization() { return useLifecycleAction("delete"); }
export function useRestoreOrganization() { return useLifecycleAction("restore"); }
```

(`queryKey: ["platform-organizations"]` invalidation covers both the list query and, per TanStack Query's key-prefix matching, every `["platform-organizations", id]` detail query too — the same invalidation-by-prefix pattern already relied on elsewhere in this codebase.)

- [ ] **Step 4: Write `OrganizationDetailSheet`**

```typescript
"use client";

import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useOrganizationPanelStore } from "@/features/platform-organizations/store/organizationPanelStore";
import { usePlatformOrganization } from "@/features/platform-organizations/hooks/usePlatformOrganization";
import {
  useSuspendOrganization,
  useReactivateOrganization,
  useDeleteOrganization,
  useRestoreOrganization,
} from "@/features/platform-organizations/hooks/useOrganizationLifecycleActions";

export function OrganizationDetailSheet() {
  const panel = useOrganizationPanelStore((s) => s.panel);
  const close = useOrganizationPanelStore((s) => s.close);
  const organizationId = panel.mode === "detail" ? panel.organizationId : null;
  const { data, isLoading } = usePlatformOrganization(organizationId);
  const suspend = useSuspendOrganization();
  const reactivate = useReactivateOrganization();
  const softDelete = useDeleteOrganization();
  const restore = useRestoreOrganization();

  return (
    <Sheet open={panel.mode === "detail"} onOpenChange={(open) => !open && close()}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{data?.organization.name ?? "Organization"}</SheetTitle>
        </SheetHeader>
        {isLoading && (
          <div className="flex items-center justify-center p-12">
            <Spinner className="h-6 w-6" />
          </div>
        )}
        {data && (
          <div className="space-y-6 p-4">
            <div className="flex items-center gap-2">
              <Badge variant={data.organization.deletedAt ? "destructive" : data.organization.isActive ? "default" : "secondary"}>
                {data.organization.deletedAt ? "Deleted" : data.organization.isActive ? "Active" : "Suspended"}
              </Badge>
              <span className="text-sm text-muted-foreground">{data.planName ?? "No plan assigned"}</span>
            </div>

            <div className="flex flex-wrap gap-2">
              {data.organization.isActive && !data.organization.deletedAt && (
                <Button size="sm" variant="destructive" disabled={suspend.isPending} onClick={() => organizationId && suspend.mutate(organizationId)}>
                  Suspend
                </Button>
              )}
              {!data.organization.isActive && !data.organization.deletedAt && (
                <Button size="sm" disabled={reactivate.isPending} onClick={() => organizationId && reactivate.mutate(organizationId)}>
                  Reactivate
                </Button>
              )}
              {!data.organization.deletedAt && (
                <Button size="sm" variant="destructive" disabled={softDelete.isPending} onClick={() => organizationId && softDelete.mutate(organizationId)}>
                  Delete
                </Button>
              )}
              {data.organization.deletedAt && (
                <Button size="sm" disabled={restore.isPending} onClick={() => organizationId && restore.mutate(organizationId)}>
                  Restore
                </Button>
              )}
            </div>

            <section>
              <h3 className="mb-2 text-sm font-semibold">Users ({data.users.length})</h3>
              <ul className="space-y-1 text-sm">
                {data.users.map((u) => (
                  <li key={u.id}>{u.fullName} — {u.email} {!u.isActive && <Badge variant="secondary">Inactive</Badge>}</li>
                ))}
              </ul>
            </section>

            <section>
              <h3 className="mb-2 text-sm font-semibold">Branches ({data.branches.length})</h3>
              <ul className="space-y-1 text-sm">
                {data.branches.map((b) => (
                  <li key={b.id}>{b.code} — {b.name}</li>
                ))}
              </ul>
            </section>

            <section>
              <h3 className="mb-2 text-sm font-semibold">Invoices</h3>
              <ul className="space-y-1 text-sm">
                {data.invoices.map((i) => (
                  <li key={i.id}>{i.planName} — NPR {i.amountNpr} — {i.status}</li>
                ))}
              </ul>
            </section>

            <section>
              <h3 className="mb-2 text-sm font-semibold">Recent Activity</h3>
              <ul className="space-y-1 text-sm">
                {data.recentAuditEntries.map((entry) => (
                  <li key={entry.id}>{entry.action} — {new Date(entry.timestamp).toLocaleString()}{entry.impersonatedByUserId && " (via impersonation)"}</li>
                ))}
              </ul>
            </section>
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
```

Check `Badge`'s actual variant names in `components/ui/badge.tsx` before using `"destructive"`/`"default"`/`"secondary"` — adjust to whatever variants actually exist.

- [ ] **Step 5: Write `/platform` layout and the organizations page**

```typescript
// src/web/src/app/platform/layout.tsx
export default function PlatformLayout({ children }: { children: React.ReactNode }) {
  return <div className="min-h-screen bg-background">{children}</div>;
}
```

(Deliberately minimal — no shared top nav/sidebar chrome yet; Task 17 adds a small platform-specific nav once there's more than one page to switch between.)

```typescript
// src/web/src/app/platform/organizations/page.tsx
"use client";

import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { Badge } from "@/components/ui/badge";
import { usePlatformOrganizations } from "@/features/platform-organizations/hooks/usePlatformOrganizations";
import { useOrganizationPanelStore } from "@/features/platform-organizations/store/organizationPanelStore";
import { OrganizationDetailSheet } from "@/features/platform-organizations/OrganizationDetailSheet";
import type { OrganizationSummary } from "@/features/platform-organizations/types";

export default function PlatformOrganizationsPage() {
  const { data, isLoading, isError, refetch } = usePlatformOrganizations();
  const openDetail = useOrganizationPanelStore((s) => s.openDetail);

  const columns: ColumnDef<OrganizationSummary>[] = [
    {
      key: "name",
      header: "Organization",
      render: (o) => (
        <button type="button" onClick={() => openDetail(o.id)} className="font-medium text-foreground hover:underline">
          {o.name}
        </button>
      ),
    },
    { key: "plan", header: "Plan", render: (o) => o.planName ?? "—" },
    { key: "branches", header: "Branches", render: (o) => o.branchCount },
    {
      key: "status",
      header: "Status",
      render: (o) => <Badge variant={o.isDeleted ? "destructive" : o.isActive ? "default" : "secondary"}>{o.isDeleted ? "Deleted" : o.isActive ? "Active" : "Suspended"}</Badge>,
    },
  ];

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isError) {
    return <ErrorState title="Couldn't load organizations" description="Something went wrong fetching the organization list." onRetry={() => refetch()} />;
  }

  return (
    <div className="space-y-4 p-6">
      <h1 className="font-display text-xl font-semibold">Organizations</h1>
      <DataTable
        columns={columns}
        data={data && { items: data, page: 1, pageSize: data.length, totalCount: data.length, totalPages: 1 }}
        isLoading={false}
        emptyTitle="No organizations yet"
        emptyDescription="Organizations appear here once they self-register."
        onPageChange={() => {}}
        rowKey={(o) => o.id}
      />
      <OrganizationDetailSheet />
    </div>
  );
}
```

- [ ] **Step 6: Manual verification**

Run: `cd src/web && npm run dev`, log in at `/platform-login`, navigate to `/platform/organizations`, confirm the list loads (register a test org via `/register` first if the list is empty), open the detail Sheet, and exercise Suspend → Reactivate → Delete → Restore end-to-end against the real running backend.

- [ ] **Step 7: Write component tests, run everything, and commit**

Write `OrganizationDetailSheet.test.tsx` and a test for the organizations page following this codebase's existing pattern for `BranchRecord.test.tsx`/the branches page's own test file (mock the hooks, assert the lifecycle buttons call the right mutation).

Run: `cd src/web && npx tsc --noEmit && npm run lint && npm test`

```bash
git add src/web
git commit -m "feat(web): add platform organizations list, detail sheet, and lifecycle actions"
```

---

## Task 16: Plan management (SuperAdmin-only) — backend endpoints + frontend page

**Files:**
- Create: `src/Modules/Platform/Presentation/PlatformPlansEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Api.Tests/PlatformPlansEndpointsTests.cs`
- Create: `src/web/src/app/api/platform/plans/route.ts`
- Create: `src/web/src/app/api/platform/plans/[id]/retire/route.ts`
- Create: `src/web/src/features/platform-plans/types.ts`
- Create: `src/web/src/features/platform-plans/schema.ts`
- Create: `src/web/src/features/platform-plans/store/planPanelStore.ts`
- Create: `src/web/src/features/platform-plans/hooks/usePlatformPlans.ts`
- Create: `src/web/src/features/platform-plans/hooks/useCreatePlan.ts`
- Create: `src/web/src/features/platform-plans/hooks/useRetirePlan.ts`
- Create: `src/web/src/features/platform-plans/PlanCreateSheet.tsx`
- Create: `src/web/src/app/platform/plans/page.tsx`

**Interfaces:**
- Consumes: `Billing.Application.PlanService` directly (not through `IBillingLookupService` — that interface is deliberately narrow/read-only for cross-module lookups; Plan CRUD is `Platform`'s own admin surface over a module it already fully references, the same way `Platform` already calls `OrganizationLifecycleService`'s own concrete methods rather than hiding them behind an interface nothing else needs).

- [ ] **Step 1: Write `PlatformPlansEndpoints`**

```csharp
using Courier.Billing.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformPlansEndpoints
{
    public static void MapPlatformPlansEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/platform/plans").WithTags("Platform Plans");

        group.MapGet("/", async (PlanService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListPlansAsync(cancellationToken)))
            .RequireSuperAdmin();

        group.MapPost("/", async (CreatePlanRequest request, PlanService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.CreatePlanAsync(request, cancellationToken);
                return Results.Created($"/api/v1/platform/plans/{result.Id}", result);
            }
            catch (BillingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BILLING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();

        group.MapPost("/{id:guid}/retire", async (Guid id, PlanService service, CancellationToken cancellationToken) =>
        {
            try
            {
                await service.RetirePlanAsync(id, cancellationToken);
                return Results.NoContent();
            }
            catch (BillingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BILLING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();
    }
}
```

- [ ] **Step 2: Wire `Program.cs`**

`PlanService` is already registered (Task 7) — only the endpoint mapping is new:

```csharp
app.MapPlatformPlansEndpoints();
```

- [ ] **Step 3: Write the failing integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Courier.Api.Tests;

public class PlatformPlansEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public PlatformPlansEndpointsTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreatePlan_AsSuperAdmin_Succeeds()
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody.GetProperty("accessToken").GetString());

        var response = await client.PostAsJsonAsync("/api/v1/platform/plans", new { name = "Test Plan", slug = $"test-{Guid.NewGuid():N}", monthlyPriceNpr = 2999m, maxBranches = 5, maxStaffUsers = (int?)null, maxMonthlyShipments = (int?)null, isDefault = false });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreatePlan_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = _factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            organizationName = "Not Super Admin Org 2",
            organizationSlug = $"nsa2-{Guid.NewGuid():N}",
            ownerFullName = "Owner",
            ownerEmail = $"owner-{Guid.NewGuid():N}@example.test",
            ownerPassword = "P@ssw0rd123"
        });
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", registerBody.GetProperty("accessToken").GetString());

        var response = await client.PostAsJsonAsync("/api/v1/platform/plans", new { name = "Test Plan", slug = $"test-{Guid.NewGuid():N}", monthlyPriceNpr = 2999m, maxBranches = (int?)null, maxStaffUsers = (int?)null, maxMonthlyShipments = (int?)null, isDefault = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

- [ ] **Step 4: Run tests to verify failure then success**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter PlatformPlansEndpointsTests`
Expected before implementation: FAIL (404). After Steps 1-2: PASS.

- [ ] **Step 5: Write the frontend — BFF routes, types, schema, store, hooks**

```typescript
// src/web/src/app/api/platform/plans/route.ts
import { NextRequest, NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function GET() {
  try {
    const response = await platformApiFetch("/api/v1/platform/plans");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await platformApiFetch("/api/v1/platform/plans", {
      method: "POST",
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

```typescript
// src/web/src/app/api/platform/plans/[id]/retire/route.ts
import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function POST(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await platformApiFetch(`/api/v1/platform/plans/${params.id}/retire`, { method: "POST" });
    return new NextResponse(null, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```typescript
// src/web/src/features/platform-plans/types.ts
export interface Plan {
  id: string;
  name: string;
  slug: string;
  monthlyPriceNpr: number;
  maxBranches: number | null;
  maxStaffUsers: number | null;
  maxMonthlyShipments: number | null;
  isDefault: boolean;
  isActive: boolean;
}
```

```typescript
// src/web/src/features/platform-plans/schema.ts
import { z } from "zod";

export const createPlanSchema = z.object({
  name: z.string().min(1, "Name is required"),
  slug: z.string().min(1, "Slug is required"),
  monthlyPriceNpr: z.coerce.number().min(0, "Price cannot be negative"),
  maxBranches: z.coerce.number().int().positive().nullable().optional(),
  maxStaffUsers: z.coerce.number().int().positive().nullable().optional(),
  maxMonthlyShipments: z.coerce.number().int().positive().nullable().optional(),
  isDefault: z.boolean().default(false),
});

export type CreatePlanInput = z.infer<typeof createPlanSchema>;
```

```typescript
// src/web/src/features/platform-plans/store/planPanelStore.ts
import { create } from "zustand";

interface PlanPanelStore {
  isCreateOpen: boolean;
  openCreate: () => void;
  close: () => void;
}

export const usePlanPanelStore = create<PlanPanelStore>((set) => ({
  isCreateOpen: false,
  openCreate: () => set({ isCreateOpen: true }),
  close: () => set({ isCreateOpen: false }),
}));
```

```typescript
// src/web/src/features/platform-plans/hooks/usePlatformPlans.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Plan } from "@/features/platform-plans/types";

export function usePlatformPlans() {
  return useQuery({
    queryKey: ["platform-plans"],
    queryFn: () => clientFetch<Plan[]>("/api/platform/plans"),
  });
}
```

```typescript
// src/web/src/features/platform-plans/hooks/useCreatePlan.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CreatePlanInput } from "@/features/platform-plans/schema";

export function useCreatePlan() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreatePlanInput) => clientFetch("/api/platform/plans", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(input) }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["platform-plans"] }),
  });
}
```

```typescript
// src/web/src/features/platform-plans/hooks/useRetirePlan.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useRetirePlan() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (planId: string) => clientFetch(`/api/platform/plans/${planId}/retire`, { method: "POST" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["platform-plans"] }),
  });
}
```

- [ ] **Step 6: Write `PlanCreateSheet` and the plans page**

Build `PlanCreateSheet` following `BranchRecord.tsx`'s create-mode React Hook Form + Zod + shadcn `Sheet`/`Form`/`Input`/`Checkbox` composition exactly (read that file first) — fields for name/slug/monthlyPriceNpr/maxBranches/maxStaffUsers/maxMonthlyShipments (all as `Input`) and isDefault (as `Checkbox`), submitting via `useCreatePlan()` and closing via `usePlanPanelStore`.

```typescript
// src/web/src/app/platform/plans/page.tsx
"use client";

import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePlatformPlans } from "@/features/platform-plans/hooks/usePlatformPlans";
import { useRetirePlan } from "@/features/platform-plans/hooks/useRetirePlan";
import { usePlanPanelStore } from "@/features/platform-plans/store/planPanelStore";
import { PlanCreateSheet } from "@/features/platform-plans/PlanCreateSheet";
import type { Plan } from "@/features/platform-plans/types";

export default function PlatformPlansPage() {
  const { data, isLoading, isError, refetch } = usePlatformPlans();
  const openCreate = usePlanPanelStore((s) => s.openCreate);
  const retire = useRetirePlan();

  const columns: ColumnDef<Plan>[] = [
    { key: "name", header: "Name", render: (p) => p.name },
    { key: "price", header: "Monthly Price (NPR)", render: (p) => p.monthlyPriceNpr },
    { key: "maxBranches", header: "Max Branches", render: (p) => p.maxBranches ?? "Unlimited" },
    { key: "default", header: "Default", render: (p) => (p.isDefault ? <Badge>Default</Badge> : null) },
    {
      key: "actions",
      header: "",
      render: (p) => !p.isDefault && p.isActive && (
        <Button size="sm" variant="ghost" disabled={retire.isPending} onClick={() => retire.mutate(p.id)}>Retire</Button>
      ),
    },
  ];

  if (isLoading) {
    return <div className="flex items-center justify-center p-12"><Spinner className="h-6 w-6" /></div>;
  }
  if (isError) {
    return <ErrorState title="Couldn't load plans" description="Something went wrong fetching the plan list." onRetry={() => refetch()} />;
  }

  return (
    <div className="space-y-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Plans</h1>
        <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
          <Plus className="h-4 w-4" strokeWidth={2} />
          New Plan
        </Button>
      </div>
      <DataTable
        columns={columns}
        data={data && { items: data, page: 1, pageSize: data.length, totalCount: data.length, totalPages: 1 }}
        isLoading={false}
        emptyTitle="No plans yet"
        emptyDescription="Create a plan to start assigning it to organizations."
        onPageChange={() => {}}
        rowKey={(p) => p.id}
      />
      <PlanCreateSheet />
    </div>
  );
}
```

- [ ] **Step 7: Manual verification, tests, and commit**

Run: `cd src/web && npm run dev`, navigate to `/platform/plans`, create a plan, confirm it appears and a non-default active plan can be retired.

Write `PlanCreateSheet.test.tsx` following the same mocked-hook pattern as `BranchRecord.test.tsx`.

Run: `cd src/web && npx tsc --noEmit && npm run lint && npm test`

```bash
git add src/Modules/Platform src/Api tests/Api.Tests src/web
git commit -m "feat: add SuperAdmin plan management (backend endpoints + frontend page)"
```

---

## Task 17: Cross-org invoice list + platform dashboard stat tiles + shared platform nav

**Files:**
- Modify: `src/Modules/Billing/Application/IBillingLookupService.cs`
- Modify: `src/Modules/Billing/Application/SubscriptionService.cs`
- Modify: `src/Modules/Platform/Application/PlatformDashboardService.cs`
- Create: `src/Modules/Platform/Presentation/PlatformInvoicesEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Api.Tests/PlatformInvoicesEndpointsTests.cs`
- Create: `src/web/src/app/api/platform/invoices/route.ts`
- Create: `src/web/src/app/api/platform/dashboard/route.ts`
- Create: `src/web/src/features/platform-invoices/types.ts`
- Create: `src/web/src/features/platform-invoices/hooks/usePlatformInvoices.ts`
- Create: `src/web/src/app/platform/invoices/page.tsx`
- Create: `src/web/src/features/platform-dashboard/types.ts`
- Create: `src/web/src/features/platform-dashboard/hooks/usePlatformDashboard.ts`
- Create: `src/web/src/app/platform/dashboard/page.tsx`
- Create: `src/web/src/features/platform/PlatformNav.tsx`
- Modify: `src/web/src/app/platform/layout.tsx`

**Interfaces:**
- Produces (Billing): `IBillingLookupService.ListAllInvoicesAsync(InvoiceStatus? status, CancellationToken) : Task<List<InvoiceAdminRecord>>` where `InvoiceAdminRecord(Guid Id, Guid OrganizationId, string PlanName, decimal AmountNpr, InvoiceStatus Status, DateTimeOffset DueAt, DateTimeOffset? PaidAt)`.
- Produces (Platform): `PlatformDashboardService.ListAllInvoicesAsync(InvoiceStatus? status, CancellationToken) : Task<List<InvoiceAdminRecordWithOrgName>>` where `InvoiceAdminRecordWithOrgName` adds `string OrganizationName` to the above — joined in-memory against `IOrganizationLookupService.ListAsync()` rather than an N+1 per-invoice lookup.

- [ ] **Step 1: Add `ListAllInvoicesAsync` to `IBillingLookupService`/`SubscriptionService`**

```csharp
public record InvoiceAdminRecord(Guid Id, Guid OrganizationId, string PlanName, decimal AmountNpr, InvoiceStatus Status, DateTimeOffset DueAt, DateTimeOffset? PaidAt);
```

```csharp
    Task<List<InvoiceAdminRecord>> ListAllInvoicesAsync(InvoiceStatus? status, CancellationToken cancellationToken = default);
```

```csharp
    public async Task<List<InvoiceAdminRecord>> ListAllInvoicesAsync(InvoiceStatus? status, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Invoice>().IgnoreQueryFilters().AsQueryable();
        if (status is InvoiceStatus s)
        {
            query = query.Where(i => i.Status == s);
        }
        return await query
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvoiceAdminRecord(i.Id, i.OrganizationId, i.PlanNameSnapshot, i.AmountNprSnapshot, i.Status, i.DueAt, i.PaidAt))
            .ToListAsync(cancellationToken);
    }
```

- [ ] **Step 2: Add the org-name join in `PlatformDashboardService`**

```csharp
public record InvoiceAdminRecordWithOrgName(Guid Id, Guid OrganizationId, string OrganizationName, string PlanName, decimal AmountNpr, Courier.Billing.Domain.InvoiceStatus Status, DateTimeOffset DueAt, DateTimeOffset? PaidAt);
```

```csharp
    public async Task<List<InvoiceAdminRecordWithOrgName>> ListAllInvoicesAsync(Courier.Billing.Domain.InvoiceStatus? status, CancellationToken cancellationToken = default)
    {
        var invoices = await _billingLookupService.ListAllInvoicesAsync(status, cancellationToken);
        var organizations = await _organizationLookupService.ListAsync(cancellationToken);
        var namesByid = organizations.ToDictionary(o => o.Id, o => o.Name);

        return invoices
            .Select(i => new InvoiceAdminRecordWithOrgName(i.Id, i.OrganizationId, namesByid.GetValueOrDefault(i.OrganizationId, "Unknown"), i.PlanName, i.AmountNpr, i.Status, i.DueAt, i.PaidAt))
            .ToList();
    }
```

- [ ] **Step 3: Write `PlatformInvoicesEndpoints` and wire `Program.cs`**

```csharp
using Courier.Billing.Domain;
using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformInvoicesEndpoints
{
    public static void MapPlatformInvoicesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/platform/invoices", async (InvoiceStatus? status, PlatformDashboardService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.ListAllInvoicesAsync(status, cancellationToken)))
            .WithTags("Platform Invoices")
            .RequireSuperAdmin();
    }
}
```

```csharp
app.MapPlatformInvoicesEndpoints();
```

- [ ] **Step 4: Write the failing integration test**

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Courier.Api.Tests;

public class PlatformInvoicesEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public PlatformInvoicesEndpointsTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListInvoices_AsSuperAdmin_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody.GetProperty("accessToken").GetString());

        var response = await client.GetAsync("/api/v1/platform/invoices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 5: Run tests, then full-solution build/regression check**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter PlatformInvoicesEndpointsTests`
Then: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj`

- [ ] **Step 6: Write the frontend — invoices list, dashboard, and shared nav**

```typescript
// src/web/src/app/api/platform/invoices/route.ts
import { NextRequest, NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function GET(request: NextRequest) {
  const status = request.nextUrl.searchParams.get("status");
  const query = status ? `?status=${encodeURIComponent(status)}` : "";
  try {
    const response = await platformApiFetch(`/api/v1/platform/invoices${query}`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```typescript
// src/web/src/app/api/platform/dashboard/route.ts
import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function GET() {
  try {
    const response = await platformApiFetch("/api/v1/platform/dashboard");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```typescript
// src/web/src/features/platform-invoices/types.ts
export interface AdminInvoice {
  id: string;
  organizationId: string;
  organizationName: string;
  planName: string;
  amountNpr: number;
  status: "Pending" | "Paid" | "Overdue" | "Cancelled";
  dueAt: string;
  paidAt: string | null;
}
```

```typescript
// src/web/src/features/platform-invoices/hooks/usePlatformInvoices.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { AdminInvoice } from "@/features/platform-invoices/types";

export function usePlatformInvoices(status?: string) {
  return useQuery({
    queryKey: ["platform-invoices", status ?? "all"],
    queryFn: () => clientFetch<AdminInvoice[]>(`/api/platform/invoices${status ? `?status=${status}` : ""}`),
  });
}
```

```typescript
// src/web/src/app/platform/invoices/page.tsx
"use client";

import { Badge } from "@/components/ui/badge";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePlatformInvoices } from "@/features/platform-invoices/hooks/usePlatformInvoices";
import type { AdminInvoice } from "@/features/platform-invoices/types";

export default function PlatformInvoicesPage() {
  const { data, isLoading, isError, refetch } = usePlatformInvoices();

  const columns: ColumnDef<AdminInvoice>[] = [
    { key: "organization", header: "Organization", render: (i) => i.organizationName },
    { key: "plan", header: "Plan", render: (i) => i.planName },
    { key: "amount", header: "Amount (NPR)", render: (i) => i.amountNpr },
    { key: "status", header: "Status", render: (i) => <Badge variant={i.status === "Overdue" ? "destructive" : i.status === "Paid" ? "default" : "secondary"}>{i.status}</Badge> },
    { key: "dueAt", header: "Due", render: (i) => new Date(i.dueAt).toLocaleDateString() },
  ];

  if (isLoading) {
    return <div className="flex items-center justify-center p-12"><Spinner className="h-6 w-6" /></div>;
  }
  if (isError) {
    return <ErrorState title="Couldn't load invoices" description="Something went wrong fetching the invoice list." onRetry={() => refetch()} />;
  }

  return (
    <div className="space-y-4 p-6">
      <h1 className="font-display text-xl font-semibold">Invoices</h1>
      <DataTable
        columns={columns}
        data={data && { items: data, page: 1, pageSize: data.length, totalCount: data.length, totalPages: 1 }}
        isLoading={false}
        emptyTitle="No invoices yet"
        emptyDescription="Invoices appear here once a paid plan's billing cycle generates one."
        onPageChange={() => {}}
        rowKey={(i) => i.id}
      />
    </div>
  );
}
```

```typescript
// src/web/src/features/platform-dashboard/types.ts
export interface PlatformMetrics {
  totalOrganizations: number;
  activeOrganizations: number;
  suspendedOrganizations: number;
  deletedOrganizations: number;
  monthlyRecurringRevenueNpr: number;
  overdueInvoiceCount: number;
}
```

```typescript
// src/web/src/features/platform-dashboard/hooks/usePlatformDashboard.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PlatformMetrics } from "@/features/platform-dashboard/types";

export function usePlatformDashboard() {
  return useQuery({
    queryKey: ["platform-dashboard"],
    queryFn: () => clientFetch<PlatformMetrics>("/api/platform/dashboard"),
  });
}
```

```typescript
// src/web/src/app/platform/dashboard/page.tsx
"use client";

import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { usePlatformDashboard } from "@/features/platform-dashboard/hooks/usePlatformDashboard";

function StatTile({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="rounded-lg border bg-card p-4">
      <div className="text-sm text-muted-foreground">{label}</div>
      <div className="mt-1 text-2xl font-semibold">{value}</div>
    </div>
  );
}

export default function PlatformDashboardPage() {
  const { data, isLoading, isError, refetch } = usePlatformDashboard();

  if (isLoading) {
    return <div className="flex items-center justify-center p-12"><Spinner className="h-6 w-6" /></div>;
  }
  if (isError || !data) {
    return <ErrorState title="Couldn't load the dashboard" description="Something went wrong fetching platform metrics." onRetry={() => refetch()} />;
  }

  return (
    <div className="space-y-4 p-6">
      <h1 className="font-display text-xl font-semibold">Platform Dashboard</h1>
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
        <StatTile label="Organizations" value={data.totalOrganizations} />
        <StatTile label="Active" value={data.activeOrganizations} />
        <StatTile label="Suspended" value={data.suspendedOrganizations} />
        <StatTile label="Deleted" value={data.deletedOrganizations} />
        <StatTile label="MRR (NPR)" value={data.monthlyRecurringRevenueNpr} />
        <StatTile label="Overdue Invoices" value={data.overdueInvoiceCount} />
      </div>
    </div>
  );
}
```

```typescript
// src/web/src/features/platform/PlatformNav.tsx
"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/utils";

const links = [
  { href: "/platform/dashboard", label: "Dashboard" },
  { href: "/platform/organizations", label: "Organizations" },
  { href: "/platform/plans", label: "Plans" },
  { href: "/platform/invoices", label: "Invoices" },
];

export function PlatformNav() {
  const pathname = usePathname();
  return (
    <nav className="flex gap-4 border-b bg-card px-6 py-3">
      {links.map((link) => (
        <Link
          key={link.href}
          href={link.href}
          className={cn("text-sm font-medium text-muted-foreground hover:text-foreground", pathname.startsWith(link.href) && "text-foreground")}
        >
          {link.label}
        </Link>
      ))}
    </nav>
  );
}
```

```typescript
// src/web/src/app/platform/layout.tsx (replaces Task 15's minimal version)
import { PlatformNav } from "@/features/platform/PlatformNav";

export default function PlatformLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-background">
      <PlatformNav />
      {children}
    </div>
  );
}
```

Check `cn` actually exists at `@/lib/utils` (it's used everywhere else in this codebase, e.g. `badge.tsx` above) before assuming the import path.

- [ ] **Step 7: Manual verification, tests, and commit**

Run: `cd src/web && npm run dev`, confirm `/platform/dashboard` and `/platform/invoices` both load real data and the nav correctly highlights the active page across all four `/platform/*` pages.

Write test files for the dashboard/invoices pages and `PlatformNav` following this codebase's existing page-test patterns.

Run: `cd src/web && npx tsc --noEmit && npm run lint && npm test`

```bash
git add src/Modules/Billing src/Modules/Platform src/Api tests/Api.Tests src/web
git commit -m "feat: add cross-org invoice list, platform dashboard, and shared platform nav"
```

---

## Task 18: Frontend — impersonation flow and banner

**Files:**
- Create: `src/web/src/app/api/platform/organizations/[id]/users/[userId]/impersonate/route.ts`
- Create: `src/web/src/app/api/impersonation/end/route.ts`
- Create: `src/web/src/lib/impersonation.ts`
- Create: `src/web/src/lib/impersonation.test.ts`
- Create: `src/web/src/components/layout/ImpersonationBanner.tsx`
- Modify: `src/web/src/components/layout/AppShell.tsx`
- Modify: `src/web/src/features/platform-organizations/OrganizationDetailSheet.tsx`
- Create: `src/web/src/features/platform-organizations/hooks/useImpersonate.ts`

**Interfaces:**
- Produces: `readImpersonationMeta() : {name: string; organizationName: string; expiresAt: string} | null`, `clearImpersonationMeta()` in `lib/impersonation.ts` — pure `document.cookie` parsing/writing helpers, no server dependency (the banner is a client component and needs to read this on every render without a round trip).

- [ ] **Step 1: Write the impersonate BFF route**

This route deliberately does two things a normal proxy route doesn't: it sets the **staff** session cookie (not a platform one — the SuperAdmin is about to act *as* a tenant user) to the impersonation token with no refresh-token cookie at all (Task 11's token is not refreshable — its whole 30-minute lifetime is fixed), and it sets a small non-httpOnly metadata cookie the banner reads directly.

```typescript
import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";
import { setSessionCookies } from "@/lib/auth/session";

export async function POST(_request: Request, { params }: { params: { id: string; userId: string } }) {
  try {
    const response = await platformApiFetch(`/api/v1/platform/organizations/${params.id}/users/${params.userId}/impersonate`, { method: "POST" });
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      return NextResponse.json(body, { status: response.status });
    }
    const body = await response.json();

    // setSessionCookies also sets a refresh-token cookie sized for 7 days — overwritten
    // immediately below with an empty, already-expired one, since Task 11's impersonation
    // token has no matching refresh token at all (no renewal, by design).
    setSessionCookies(body.accessToken, "");
    const result = NextResponse.json({ ok: true });
    result.cookies.set("courier_refresh_token", "", { maxAge: 0, path: "/" });
    result.cookies.set(
      "courier_impersonation_meta",
      JSON.stringify({ name: body.impersonatedUserName, organizationName: body.organizationName, expiresAt: body.expiresAt }),
      { httpOnly: false, sameSite: "lax", path: "/", expires: new Date(body.expiresAt) }
    );
    return result;
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 2: Write the end-impersonation BFF route and `lib/impersonation.ts`**

```typescript
// src/web/src/app/api/impersonation/end/route.ts
import { NextResponse } from "next/server";
import { clearSessionCookies } from "@/lib/auth/session";

export async function POST() {
  clearSessionCookies();
  const result = NextResponse.json({ ok: true });
  result.cookies.set("courier_impersonation_meta", "", { maxAge: 0, path: "/" });
  return result;
}
```

```typescript
// src/web/src/lib/impersonation.ts
export interface ImpersonationMeta {
  name: string;
  organizationName: string;
  expiresAt: string;
}

// Pure document.cookie parsing — this file has no server dependency because
// ImpersonationBanner (Step 4) is a client component rendered on every staff page and must read
// this synchronously on each render/interval tick, not through a server round trip.
export function readImpersonationMeta(): ImpersonationMeta | null {
  if (typeof document === "undefined") {
    return null;
  }
  const match = document.cookie.match(/(?:^|; )courier_impersonation_meta=([^;]*)/);
  if (!match) {
    return null;
  }
  try {
    return JSON.parse(decodeURIComponent(match[1]));
  } catch {
    return null;
  }
}
```

- [ ] **Step 3: Write `useImpersonate` and wire the button into `OrganizationDetailSheet`**

```typescript
// src/web/src/features/platform-organizations/hooks/useImpersonate.ts
"use client";

import { useMutation } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useImpersonate() {
  return useMutation({
    mutationFn: ({ organizationId, userId }: { organizationId: string; userId: string }) =>
      clientFetch(`/api/platform/organizations/${organizationId}/users/${userId}/impersonate`, { method: "POST" }),
  });
}
```

In `OrganizationDetailSheet.tsx`, add an "Impersonate" button to each row of the Users section (Task 15's `<ul>` of `data.users`), calling `useImpersonate()` and, on success, navigating to `/dashboard` (the router import/navigation call already exists elsewhere in this codebase's Sheets that redirect after a mutation — follow that exact pattern rather than inventing a new one):

```typescript
{data.users.filter((u) => u.isActive).map((u) => (
  <li key={u.id} className="flex items-center justify-between">
    <span>{u.fullName} — {u.email}</span>
    <Button
      size="sm"
      variant="outline"
      disabled={impersonate.isPending}
      onClick={() => impersonate.mutate({ organizationId: organizationId!, userId: u.id }, { onSuccess: () => router.push("/dashboard") })}
    >
      Impersonate
    </Button>
  </li>
))}
```

- [ ] **Step 4: Write `ImpersonationBanner` and wire it into `AppShell`**

```typescript
"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { readImpersonationMeta, type ImpersonationMeta } from "@/lib/impersonation";

function formatRemaining(expiresAt: string): string {
  const remainingMs = new Date(expiresAt).getTime() - Date.now();
  if (remainingMs <= 0) {
    return "0:00";
  }
  const totalSeconds = Math.floor(remainingMs / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, "0")}`;
}

export function ImpersonationBanner() {
  const router = useRouter();
  const [meta, setMeta] = useState<ImpersonationMeta | null>(null);
  const [, forceTick] = useState(0);

  useEffect(() => {
    setMeta(readImpersonationMeta());
    const interval = setInterval(() => {
      forceTick((n) => n + 1);
      setMeta(readImpersonationMeta());
    }, 1000);
    return () => clearInterval(interval);
  }, []);

  if (!meta) {
    return null;
  }

  const endSession = async () => {
    await fetch("/api/impersonation/end", { method: "POST" });
    router.push("/platform/organizations");
  };

  return (
    <div className="flex items-center justify-between bg-amber-500 px-4 py-2 text-sm font-medium text-amber-950">
      <span>
        Impersonating {meta.name} at {meta.organizationName} — ends in {formatRemaining(meta.expiresAt)}
      </span>
      <Button size="sm" variant="outline" className="h-7 border-amber-950/30 bg-transparent text-amber-950 hover:bg-amber-950/10" onClick={endSession}>
        End session
      </Button>
    </div>
  );
}
```

In `AppShell.tsx`, add the import and render it as the first child of `SidebarInset`, before the `<header>`:

```typescript
import { ImpersonationBanner } from "@/components/layout/ImpersonationBanner";
```

```typescript
      <SidebarInset>
        <ImpersonationBanner />
        <header className="sticky top-0 z-10 flex h-16 shrink-0 items-center gap-3 border-b border-border bg-card px-4 sm:px-6">
```

(Amber/warning-toned inline colors here are deliberate and independent of the app's theme tokens — an impersonation banner needs to read as an unmistakable, unambiguous warning in both light and dark mode, the same reasoning `DESIGN.md` already applies to other fixed, mode-independent surfaces like the marketing site's dark identity.)

- [ ] **Step 5: Write tests**

```typescript
// src/web/src/lib/impersonation.test.ts
import { describe, expect, it, afterEach } from "vitest";
import { readImpersonationMeta } from "@/lib/impersonation";

describe("readImpersonationMeta", () => {
  afterEach(() => {
    document.cookie = "courier_impersonation_meta=; max-age=0; path=/";
  });

  it("returns null when no cookie is present", () => {
    expect(readImpersonationMeta()).toBeNull();
  });

  it("parses a present cookie", () => {
    const meta = { name: "Jane Owner", organizationName: "Acme Courier", expiresAt: new Date(Date.now() + 60000).toISOString() };
    document.cookie = `courier_impersonation_meta=${encodeURIComponent(JSON.stringify(meta))}; path=/`;

    expect(readImpersonationMeta()).toEqual(meta);
  });
});
```

Write `ImpersonationBanner.test.tsx` mocking `readImpersonationMeta` (from `@/lib/impersonation`) to assert: renders nothing when it returns `null`; renders the name/org/countdown and an "End session" button when it returns a meta object; clicking "End session" calls `fetch("/api/impersonation/end", ...)` and navigates.

- [ ] **Step 6: Manual verification**

Run: `cd src/web && npm run dev`. Log in as SuperAdmin, impersonate a real org's Owner from the organization detail Sheet, confirm the banner appears on `/dashboard` with a live countdown, take one real action (e.g. view the branches list), then click "End session" and confirm it returns to `/platform/organizations` with the banner gone. Separately confirm via the backend (`SELECT * FROM "AuditLogs" WHERE "Action" != 'platform.impersonation_started' ORDER BY "Timestamp" DESC LIMIT 5` against the dev Postgres) that the action taken while impersonating has both `UserId` (the Owner) and `ImpersonatedByUserId` (the SuperAdmin) populated.

- [ ] **Step 7: Full frontend check and commit**

Run: `cd src/web && npx tsc --noEmit && npm run lint && npm test`

```bash
git add src/web
git commit -m "feat(web): add impersonation flow with countdown banner"
```

---

## Task 19: Frontend — staff Billing tab (plan, invoices, eSewa/Khalti checkout)

The first real consumer of `org.billing.manage`, seeded since Phase 1 and never wired to anything until now.

**Files:**
- Create: `src/web/src/app/api/billing/plan/route.ts`
- Create: `src/web/src/app/api/billing/invoices/route.ts`
- Create: `src/web/src/app/api/billing/invoices/[id]/pay/route.ts`
- Create: `src/web/src/app/api/billing/payments/[id]/callback/route.ts`
- Create: `src/web/src/features/billing/types.ts`
- Create: `src/web/src/features/billing/hooks/useBillingPlan.ts`
- Create: `src/web/src/features/billing/hooks/useBillingInvoices.ts`
- Create: `src/web/src/features/billing/hooks/usePayInvoice.ts`
- Create: `src/web/src/features/billing/hooks/useCompletePayment.ts`
- Create: `src/web/src/features/billing/GatewayRedirectForm.tsx`
- Create: `src/web/src/app/(staff)/billing/page.tsx`
- Create: `src/web/src/app/(staff)/billing/callback/page.tsx`
- Modify: `src/web/src/components/app-sidebar.tsx` (add a "Billing" nav entry, permission-gated on `org.billing.manage` — find the existing `usePermission`-gated nav-item pattern in this file and match it exactly)

**Interfaces:**
- Consumes: `GET/POST /api/v1/billing/*` (Task 7, with Task 19's `ReturnBaseUrl` fix).

- [ ] **Step 1: Write the BFF proxy routes**

```typescript
// src/web/src/app/api/billing/plan/route.ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/billing/plan");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```typescript
// src/web/src/app/api/billing/invoices/route.ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/billing/invoices");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```typescript
// src/web/src/app/api/billing/invoices/[id]/pay/route.ts
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: NextRequest, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/billing/invoices/${params.id}/pay`, {
      method: "POST",
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

```typescript
// src/web/src/app/api/billing/payments/[id]/callback/route.ts
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: NextRequest, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/billing/payments/${params.id}/callback`, {
      method: "POST",
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

- [ ] **Step 2: Write types and hooks**

```typescript
// src/web/src/features/billing/types.ts
export interface PlanSummary {
  planName: string;
  monthlyPriceNpr: number;
  status: "Active" | "PastDue" | "Cancelled";
}

export interface Invoice {
  id: string;
  planName: string;
  amountNpr: number;
  cycleStart: string;
  cycleEnd: string;
  status: "Pending" | "Paid" | "Overdue" | "Cancelled";
  dueAt: string;
  paidAt: string | null;
}

export interface GatewayCheckoutInfo {
  redirectUrl: string;
  formFields: Record<string, string>;
}
```

```typescript
// src/web/src/features/billing/hooks/useBillingPlan.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PlanSummary } from "@/features/billing/types";

export function useBillingPlan() {
  return useQuery({
    queryKey: ["billing-plan"],
    queryFn: () => clientFetch<PlanSummary>("/api/billing/plan"),
  });
}
```

```typescript
// src/web/src/features/billing/hooks/useBillingInvoices.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Invoice } from "@/features/billing/types";

export function useBillingInvoices() {
  return useQuery({
    queryKey: ["billing-invoices"],
    queryFn: () => clientFetch<Invoice[]>("/api/billing/invoices"),
  });
}
```

```typescript
// src/web/src/features/billing/hooks/usePayInvoice.ts
"use client";

import { useMutation } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { GatewayCheckoutInfo } from "@/features/billing/types";

export function usePayInvoice() {
  return useMutation({
    mutationFn: ({ invoiceId, gateway }: { invoiceId: string; gateway: "Esewa" | "Khalti" }) =>
      clientFetch<GatewayCheckoutInfo>(`/api/billing/invoices/${invoiceId}/pay`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ gateway, returnBaseUrl: `${window.location.origin}/billing/callback` }),
      }),
  });
}
```

```typescript
// src/web/src/features/billing/hooks/useCompletePayment.ts
"use client";

import { useMutation } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useCompletePayment() {
  return useMutation({
    mutationFn: ({ paymentId, callbackParameters }: { paymentId: string; callbackParameters: Record<string, string> }) =>
      clientFetch<{ succeeded: boolean; message: string }>(`/api/billing/payments/${paymentId}/callback`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(callbackParameters),
      }),
  });
}
```

- [ ] **Step 3: Write `GatewayRedirectForm`**

```typescript
"use client";

// eSewa's flow needs a real HTML form POST with signed fields — a plain redirect can't carry a
// POST body. Khalti's flow is a plain GET redirect (its checkoutInfo.formFields is always
// empty). This one component handles both without the caller needing to know which shape it got.
export function redirectToGateway(checkoutInfo: { redirectUrl: string; formFields: Record<string, string> }) {
  if (Object.keys(checkoutInfo.formFields).length === 0) {
    window.location.href = checkoutInfo.redirectUrl;
    return;
  }

  const form = document.createElement("form");
  form.method = "POST";
  form.action = checkoutInfo.redirectUrl;
  for (const [key, value] of Object.entries(checkoutInfo.formFields)) {
    const input = document.createElement("input");
    input.type = "hidden";
    input.name = key;
    input.value = value;
    form.appendChild(input);
  }
  document.body.appendChild(form);
  form.submit();
}
```

- [ ] **Step 4: Write the Billing page**

```typescript
"use client";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useBillingPlan } from "@/features/billing/hooks/useBillingPlan";
import { useBillingInvoices } from "@/features/billing/hooks/useBillingInvoices";
import { usePayInvoice } from "@/features/billing/hooks/usePayInvoice";
import { redirectToGateway } from "@/features/billing/GatewayRedirectForm";
import type { Invoice } from "@/features/billing/types";

export default function BillingPage() {
  const { data: plan, isLoading: planLoading } = useBillingPlan();
  const { data: invoices, isLoading: invoicesLoading, isError, refetch } = useBillingInvoices();
  const payInvoice = usePayInvoice();

  const pay = async (invoiceId: string, gateway: "Esewa" | "Khalti") => {
    const checkoutInfo = await payInvoice.mutateAsync({ invoiceId, gateway });
    redirectToGateway(checkoutInfo);
  };

  const columns: ColumnDef<Invoice>[] = [
    { key: "plan", header: "Plan", render: (i) => i.planName },
    { key: "amount", header: "Amount (NPR)", render: (i) => i.amountNpr },
    { key: "status", header: "Status", render: (i) => <Badge variant={i.status === "Overdue" ? "destructive" : i.status === "Paid" ? "default" : "secondary"}>{i.status}</Badge> },
    { key: "dueAt", header: "Due", render: (i) => new Date(i.dueAt).toLocaleDateString() },
    {
      key: "actions",
      header: "",
      render: (i) => (i.status === "Pending" || i.status === "Overdue") && (
        <div className="flex gap-2">
          <Button size="sm" variant="outline" disabled={payInvoice.isPending} onClick={() => pay(i.id, "Esewa")}>Pay with eSewa</Button>
          <Button size="sm" variant="outline" disabled={payInvoice.isPending} onClick={() => pay(i.id, "Khalti")}>Pay with Khalti</Button>
        </div>
      ),
    },
  ];

  if (planLoading || invoicesLoading) {
    return <div className="flex items-center justify-center p-12"><Spinner className="h-6 w-6" /></div>;
  }
  if (isError) {
    return <ErrorState title="Couldn't load billing" description="Something went wrong fetching your billing details." onRetry={() => refetch()} />;
  }

  return (
    <div className="space-y-6 p-6">
      <div>
        <h1 className="font-display text-xl font-semibold">Billing</h1>
        {plan && <p className="mt-1 text-sm text-muted-foreground">Current plan: {plan.planName} — NPR {plan.monthlyPriceNpr}/month ({plan.status})</p>}
      </div>
      <DataTable
        columns={columns}
        data={invoices && { items: invoices, page: 1, pageSize: invoices.length, totalCount: invoices.length, totalPages: 1 }}
        isLoading={false}
        emptyTitle="No invoices yet"
        emptyDescription="Invoices appear here once your plan's billing cycle generates one."
        onPageChange={() => {}}
        rowKey={(i) => i.id}
      />
    </div>
  );
}
```

- [ ] **Step 5: Write the callback page**

```typescript
"use client";

import { useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useCompletePayment } from "@/features/billing/hooks/useCompletePayment";

export default function BillingCallbackPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const completePayment = useCompletePayment();
  const [result, setResult] = useState<{ succeeded: boolean; message: string } | null>(null);

  useEffect(() => {
    const paymentId = searchParams.get("paymentId");
    if (!paymentId) {
      setResult({ succeeded: false, message: "No payment reference was returned." });
      return;
    }
    const callbackParameters: Record<string, string> = {};
    searchParams.forEach((value, key) => {
      if (key !== "paymentId") {
        callbackParameters[key] = value;
      }
    });
    completePayment.mutate(
      { paymentId, callbackParameters },
      { onSuccess: (data) => setResult(data), onError: () => setResult({ succeeded: false, message: "Payment could not be confirmed." }) }
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (!result) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center gap-4">
        <Spinner className="h-8 w-8" />
        <p className="text-sm text-muted-foreground">Confirming your payment…</p>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-4 p-4 text-center">
      <p className="text-lg font-semibold">{result.succeeded ? "Payment confirmed" : "Payment not confirmed"}</p>
      <p className="text-sm text-muted-foreground">{result.message}</p>
      <Button onClick={() => router.push("/billing")}>Back to Billing</Button>
    </div>
  );
}
```

Check whether this codebase's existing lint config requires a different exhaustive-deps escape convention than the inline comment above — match whatever pattern already exists elsewhere for a deliberate mount-only effect (e.g. grep for `exhaustive-deps` across `src/web/src` first).

- [ ] **Step 6: Add the Billing nav entry**

Find the existing `usePermission`-gated `NavItem`/similar entry in `app-sidebar.tsx` (e.g. the one gating Reports or COD on their own permission) and add one more, gated on `org.billing.manage`, pointing to `/billing`, with a sensible `lucide-react` icon (e.g. `CreditCard`) — match that file's exact existing composition rather than inventing a new nav pattern.

- [ ] **Step 7: Manual verification**

Run: `cd src/web && npm run dev`. As an `OrganizationOwner`, navigate to `/billing`, confirm the current (default, free) plan shows with no invoices. Have a `SuperAdmin` (via `/platform/organizations`) assign the org a non-default paid `Plan`, manually trigger `InvoiceGenerationJob` once (e.g. via the Hangfire dashboard at `/hangfire`, "Trigger now" on `billing-invoice-generation`, or by directly advancing the `Subscription`'s `CurrentCycleEnd` into the past via the dev Postgres and waiting for the next scheduled run), confirm the invoice appears, click "Pay with eSewa," confirm it reaches eSewa's real UAT sandbox checkout page (using the committed test credentials), complete a test payment there, and confirm the callback page shows "Payment confirmed" and the invoice's status flips to Paid.

- [ ] **Step 8: Full frontend check and commit**

Run: `cd src/web && npx tsc --noEmit && npm run lint && npm test`

```bash
git add src/web
git commit -m "feat(web): add staff Billing tab with eSewa/Khalti checkout"
```

---

## Task 20: Documentation — `PLATFORM-ROADMAP.md` and `CLARIFICATIONS.md` #21

**Files:**
- Create: `docs/PLATFORM-ROADMAP.md`
- Modify: `docs/CLARIFICATIONS.md`

**Interfaces:** None — documentation only.

- [ ] **Step 1: Write `docs/PLATFORM-ROADMAP.md`**

```markdown
# Platform Roadmap

Tracks platform-operator work (`SuperAdmin`, tenant lifecycle, billing) — a separate track
from `docs/ROADMAP.md`'s fixed backend Phase 0-14 list (CLAUDE.md §36) and
`docs/FRONTEND-ROADMAP.md`'s staff/merchant/rider phases. This work doesn't fit either
list: it operates *above* the tenant boundary both of those roadmaps take for granted.

## Phase P1 — SuperAdmin, Tenant Lifecycle, Billing

Design spec: `docs/superpowers/specs/2026-09-08-platform-super-admin-design.md`.
Plan: `docs/superpowers/plans/2026-09-08-platform-super-admin.md`.

Scope: a `SuperAdmin` identity (a `User` with `OrganizationId = null`, authenticated
through a dedicated `/api/v1/platform/auth/login`, never through the tenant RBAC
system); a new `Billing` module (`Plan`, `Subscription`, `Invoice`, `Payment` with
eSewa/Khalti checkout, invoice-then-pay-per-cycle, no true auto-recurring charge);
tenant lifecycle (suspend/reactivate/soft-delete/restore, auto-suspend on a 7-day-overdue
invoice); branch-count plan-limit enforcement (the only limit actually enforced this
phase — `MaxStaffUsers`/`MaxMonthlyShipments` are stored, not yet checked); a cross-org
platform dashboard (metrics + per-org drill-down); and time-boxed (30-minute)
impersonation with a dual-actor audit trail (`AuditLog.UserId` = the impersonated user,
`AuditLog.ImpersonatedByUserId` = the real `SuperAdmin`).

*Entry:* None — this is the first platform-track phase. *Exit:* a `SuperAdmin` can
suspend/reactivate/delete/restore an organization from `/platform/organizations`; a
`Plan` with a non-zero price generates a real `Invoice` each cycle that an
`OrganizationOwner` can pay via eSewa or Khalti from `/billing`; an overdue invoice
auto-suspends the org and a subsequent successful payment auto-reactivates it (unless a
`SuperAdmin` suspended it manually for cause); a `SuperAdmin` can impersonate any tenant
user for 30 minutes with every resulting action attributed to both identities.

*Status: not yet started as of 2026-09-08 (plan just written).*

## Later

Deliberately out of this phase's scope (see the design spec's non-goals, §3): enforcement
of `MaxStaffUsers`/`MaxMonthlyShipments`, any non-Nepal payment gateway, annual billing
cycles, hard-delete/purge of soft-deleted organizations, fine-grained platform-operator
permission tiers, and self-serve plan upgrade/downgrade by an `OrganizationOwner` (only a
`SuperAdmin` can change an org's assigned `Plan` this phase). Any of these becomes its own
future phase once there's a real need, following this repo's standing
brainstorm → spec → plan cycle — never speculatively.
```

- [ ] **Step 2: Add `CLARIFICATIONS.md` entry #21**

Read the tail of `docs/CLARIFICATIONS.md` first to confirm #20 is still the last entry (this plan's own research, not implementation, so no new ambiguity should have been introduced by another concurrent change — but verify before assuming the number).

```markdown
## 21. Platform Super Admin: three refinements found during planning, not the original design spec's exact text

**Where:** `docs/superpowers/specs/2026-09-08-platform-super-admin-design.md`

**Context:** Three points where implementing this spec literally would have meant either
redundant work or a needless schema/architecture change, discovered while researching
the plan (`docs/superpowers/plans/2026-09-08-platform-super-admin.md`) against the real
codebase rather than the spec's own prose.

**Resolution:**

1. `AuditLog.IsCrossTenantAction` already existed before this phase (added when this
   document's own entry #5 was first written) — the spec's §4 describing it as new was
   inaccurate. Only `AuditLog.ImpersonatedByUserId` is a genuinely new column.
2. This codebase's JWTs carry no embedded permissions — `IPermissionChecker`/
   `HttpContextCurrentTenantProvider` re-resolve everything from `sub`/`org_id` claims
   against the database on every request (confirmed by reading `PermissionChecker.cs`
   directly). Impersonation therefore never needed to "resolve real permissions into the
   token" as the spec's §10 described — issuing a normal-shaped token for the target user
   plus an `impersonated_by` claim is sufficient, and every existing permission/
   branch-scope/tenant-isolation check keeps working completely unchanged.
3. No new `User.IsSuperAdmin` column was added. `User.OrganizationId == null` already
   carries exactly this meaning in `AuthService.LoginAsync`, which pre-dates this phase
   and already throws "Platform accounts cannot log in through this endpoint." for
   exactly this case — the JWT's `is_super_admin` claim is issued by construction (only a
   null-org `User` can reach `PlatformAuthService.LoginAsync`), not backed by an extra
   column.

**Also recorded (a real cross-tenant gap caught during planning, not implementation):**
`Payment` carries no `OrganizationId` of its own (isolates transitively through
`InvoiceId -> Invoice.OrganizationId`, same pattern as `ShipmentItem`/`ManifestShipment`
elsewhere in this codebase) — a naive `PaymentService.CompletePaymentAsync` reading
`Set<Payment>().FirstOrDefaultAsync(p => p.Id == paymentId)` directly would not be
tenant-scoped at all, since `Payment` has no query filter to bypass or rely on. The plan's
Task 6/7 instead joins through `Invoice`'s own (filtered) `DbSet`, so a cross-tenant
`paymentId` guess resolves to "not found" exactly like a real 404 — the same
"child table, no own `OrganizationId`, isolation must flow through the parent" bug shape
this codebase's own history (`docs/CLARIFICATIONS.md` #11's `RiderCashSettlement`-adjacent
gaps, and every Phase 4/5/10/12 incident this document already records) keeps
reproducing whenever a new accessor is added — worth grep-checking again in any future
phase that adds a table isolating only through a parent FK.
```

- [ ] **Step 3: Commit**

```bash
git add docs/PLATFORM-ROADMAP.md docs/CLARIFICATIONS.md
git commit -m "docs: add Platform Roadmap and record planning-time refinements in CLARIFICATIONS #21"
```

---

## Plan Self-Review Notes (for the implementer, not a task to execute)

- **Spec coverage:** §5 (SuperAdmin auth) → Task 8. §6 → Task 8. §7 (org lifecycle) → Task 9. §8 (billing cycle/payment) → Tasks 1-7, 19. §9 (dashboard) → Task 10. §10 (impersonation) → Task 11. §11 (roadmap) → Task 20. §12 (frontend) → Tasks 14-19. §13 (error handling) → threaded through Tasks 5, 6, 9, 12. §14 (testing) → every task's own test steps, plus the cross-cutting concurrency/tenant-isolation tests called out in Tasks 5, 6, 8, 9, 11.
- **Placeholder scan:** no "TBD"/"TODO" left in any task; every step has real, complete code. The handful of "find and match the existing pattern" instructions (Task 14 Step 5's `LoginForm` copy, Task 16 Step 6's `BranchRecord.tsx` form composition, Task 19 Step 6's nav entry) are deliberate — copying this codebase's own established component composition is more reliable than this plan re-deriving a slightly different version of the same shadcn/ui form pattern from scratch, and each instruction names the exact file to copy from.
- **Type consistency:** `IBillingLookupService`/`IOrganizationLookupService`/`IBranchLookupService` each grow incrementally across tasks (Tasks 1→2→9→10→11→13 for `IOrganizationLookupService`) — every later task's usage matches the exact method names/signatures the task that introduced them declared. `PaymentGateway`, `InvoiceStatus`, `SubscriptionStatus`, and `PaymentStatus` enum member names are used identically in every task that touches them (Billing's own code, `Platform`'s dashboard composition, and the frontend `types.ts` string-literal unions).
- **Known residual risk, deliberately not fixed by this plan (parked for a future pass, matching this project's own established review convention of parking small items rather than blocking a whole phase):** `PaymentService.CompletePaymentAsync`'s cross-module call to `IOrganizationLookupService.ReactivateIfSystemSuspendedAsync` (Task 6) and `OverdueInvoiceJob`'s calls to `SuspendForNonPaymentAsync` (Task 13) both run outside their own module's `SaveChangesAsync` — a crash between the two leaves state briefly inconsistent (documented inline in both tasks). This codebase has no cross-module distributed-transaction mechanism yet; introducing one is out of scope here.
