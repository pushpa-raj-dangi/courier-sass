# Trial-Based Plan Assignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close Platform Known Limitation #1 — no organization ever actually gets a `Plan`/`Subscription` assigned in a real request path — by giving every self-registered organization a 7-day trial, letting an `OrganizationOwner` self-serve pick a real `Plan` before or after the trial ends, backend-enforcing a write-block once an unconverted trial expires, and letting a `SuperAdmin` assign/override any org's `Plan` directly (which is also how the ~25,922 already-registered organizations get fixed).

**Architecture:** `Billing.Application.SubscriptionService` (already exists, already has a working `AssignPlanAsync`) gains a `Trial`/`TrialExpired` status, a nullable `PlanId` on `Subscription`, and three new methods (`StartTrialAsync`, `IsWriteBlockedAsync`, `GetSubscriptionStatusAsync`) exposed through the existing `IBillingLookupService` cross-module interface. `Identity` cannot reference `Billing` directly (`Billing` already references `Identity`, which would cycle), so registration-time trial start is wired the same way `IBranchPlanLimitProvider`/`BranchPlanLimitAdapter` already bridges `Branches -> Billing`: a new `Identity.Application.IOrganizationSubscriptionStarter` interface, implemented by a new `Api/OrganizationSubscriptionStarterAdapter`. A new `TrialExpiryJob` (Hangfire, same shape as `OverdueInvoiceJob`) flips expired trials; a new `TrialEnforcementMiddleware` (same pipeline position as `IdempotencyMiddleware`) blocks non-GET requests for a `TrialExpired` org with `402 TRIAL_EXPIRED`. `Platform` already fully references `Billing` (see `PlatformPlansEndpoints`), so its own new assign-plan endpoint calls `SubscriptionService` directly, no adapter needed.

**Tech Stack:** ASP.NET Core / C# / EF Core 10 / PostgreSQL / Hangfire — same as every existing module. Frontend: Next.js App Router BFF pattern, TanStack Query, shadcn/ui — same as the existing `/billing` and `/platform/organizations` pages.

**Spec:** `docs/superpowers/specs/2026-09-10-billing-trial-plan-assignment-design.md` — this plan implements every section of that spec; read both together.

## Global Constraints

- Target framework `net10.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` — no new `.csproj` files are created by this plan (no new module), so this only matters if a step asks you to double-check an existing file's header.
- No module ever references another module's `DbSet<T>` directly, and no module takes a project reference that would create a cycle. `Identity` gets **no** new project reference in this plan — `IOrganizationSubscriptionStarter` lives in `Identity.Application` and is implemented in `src/Api/` (the one place with references to every module and referenced by none), exactly like `BranchPlanLimitAdapter`/`MerchantOrganizationLookupAdapter` already do.
- Every tenant-scoped table's EF query filter is `_currentTenantProvider.OrganizationId != null && x.OrganizationId == _currentTenantProvider.OrganizationId`. `SubscriptionService`'s methods are dual-use (called under a real ambient tenant from staff `/billing` endpoints, and under a null ambient tenant from `Platform`) — every new/changed method that reads `Subscription`/`Plan` uses `.IgnoreQueryFilters()` with its own explicit `organizationId` predicate, matching `GetPlanSummaryAsync`/`ListInvoicesAsync`'s already-established convention in this exact file.
- Integer-persisted enums in this codebase (`ShipmentStatus`, `SubscriptionStatus`, etc.) must only ever grow by **appending** new members, never inserting mid-list — this codebase has shipped a real bug from this exact mistake before (`docs/CLARIFICATIONS.md` entry). `SubscriptionStatus`'s two new members go at the end (`3`, `4`).
- Every enum exposed over JSON needs `[JsonConverter(typeof(JsonStringEnumConverter))]` or it silently serializes as a raw integer — this codebase has hit that bug on `BranchType`, `ShipmentStatus`, `ManifestPurpose`, `ManifestStatus`, three `Ndr` enums, and three `Cod` enums. `SubscriptionStatus` already has the attribute (`src/Modules/Billing/Domain/SubscriptionStatus.cs:7`) — adding new members to it needs no new converter work, just don't remove the attribute.
- Migrations: `dotnet ef migrations add <Name> --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`, then inspect the generated file before committing.
- Error contract: every business-rule exception is caught in the `Presentation` endpoint and mapped to `Results.BadRequest(new { error = new { code = "SOME_CODE", message = ex.Message } })` — never a raw 500 for an expected validation failure. This plan reuses `Billing.Application.BillingValidationException` throughout — no new exception type is needed.
- Module unit tests run against the **real shared Postgres** on port 5434 via `TestDbContextFactory` (already exists per module) — not EF Core's InMemory provider. Run `docker start courier-postgres-1 courier-redis-1 courier-rabbitmq-1` before any test task if the containers aren't already up.
- Frontend: every new UI control is a shadcn/ui primitive or block — never a raw HTML `<input>`/`<select>`/`<button>` styled by hand. `components/ui/select.tsx` already exists in this repo, use it directly (no `npx shadcn add` needed). Detail/create UI is always an instantly-opened right-side `Sheet` driven by a Zustand store — the `/billing` page itself is an existing full page, not a Sheet, and this plan does not change that; the `OrganizationDetailSheet.tsx` change in Task 8 fits an existing Sheet, it does not introduce a new one.
- No email/SMS trial-ending reminder, no backfill migration for already-registered organizations, no annual billing, no `MaxStaffUsers`/`MaxMonthlyShipments` enforcement — see spec §3/§11. Do not add any of these "while you're in there."

---

## Task 1: Trial domain model + `SubscriptionService` updates

**Files:**
- Modify: `src/Modules/Billing/Domain/SubscriptionStatus.cs`
- Modify: `src/Modules/Billing/Domain/Subscription.cs`
- Modify: `src/Modules/Billing/Application/BillingOptions.cs`
- Modify: `src/Modules/Billing/Application/IBillingLookupService.cs`
- Modify: `src/Modules/Billing/Application/SubscriptionService.cs`
- Modify: `tests/Billing.Tests/SubscriptionServiceTests.cs`
- Modify: `tests/Api.Tests/BillingEndpointsTests.cs` (compile fix — `AssignPlanAsync` gains a required 3rd argument)
- Modify: `tests/Api.Tests/BranchesEndpointsTests.cs` (compile fix + stale comment)
- Modify: `tests/Platform.Tests/OrganizationLifecycleServiceTests.cs` (compile fix — `FakeBillingLookupService` needs the 3 new interface members)
- Modify: `tests/Platform.Tests/PlatformDashboardServiceTests.cs` (compile fix — same)

**Interfaces:**
- Produces: `Courier.Billing.Domain.SubscriptionStatus.Trial` (3), `.TrialExpired` (4).
- Produces: `Courier.Billing.Domain.Subscription.PlanId` is now `Guid?`; new `Subscription.TrialEndsAt` (`DateTimeOffset?`).
- Produces: `Courier.Billing.Application.SubscriptionStatusInfo(SubscriptionStatus Status, DateTimeOffset? TrialEndsAt, string? PlanName, decimal? MonthlyPriceNpr)`.
- Produces on `IBillingLookupService`/`SubscriptionService`: `Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default)`, `Task<bool> IsWriteBlockedAsync(Guid organizationId, CancellationToken cancellationToken = default)`, `Task<SubscriptionStatusInfo?> GetSubscriptionStatusAsync(Guid organizationId, CancellationToken cancellationToken = default)`.
- Changes: `AssignPlanAsync(Guid organizationId, Guid planId, CancellationToken cancellationToken = default)` becomes `AssignPlanAsync(Guid organizationId, Guid planId, Guid actingUserId, CancellationToken cancellationToken = default)` — every caller (currently only test code) must be updated. It is **not** added to `IBillingLookupService` (that interface is lookups only, by its own existing convention) — callers that need it (Task 3, Task 6) take a dependency on the concrete `SubscriptionService` class directly, exactly like `PlatformPlansEndpoints` already does for `PlanService`.

- [ ] **Step 1: Append the two new `SubscriptionStatus` members**

```csharp
using System.Text.Json.Serialization;

namespace Courier.Billing.Domain;

// Same fix as PaymentGateway.cs — without this, PlanSummary.Status (exposed to the frontend's
// Billing page as "Active"/"PastDue"/"Cancelled") serializes as a raw integer instead.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubscriptionStatus
{
    Active = 0,
    PastDue = 1,
    Cancelled = 2,
    // Appended, never inserted mid-list — this codebase has shipped a real bug from
    // reordering an integer-persisted enum before (docs/CLARIFICATIONS.md).
    Trial = 3,
    TrialExpired = 4
}
```

- [ ] **Step 2: Make `Subscription.PlanId` nullable and add `TrialEndsAt`**

```csharp
namespace Courier.Billing.Domain;

// One row per organization — live operational state, not versioned/append-only like
// Pricing.RateCard (a Subscription is not "priced at a point in time," it's "which Plan
// applies right now"). Invoice is what snapshots price at a point in time.
public class Subscription
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    // Null while Status is Trial/TrialExpired — a trial has no committed Plan yet.
    public Guid? PlanId { get; set; }
    public SubscriptionStatus Status { get; set; }
    // Set only when Status is Trial; read by TrialExpiryJob to decide when to flip to
    // TrialExpired. Left populated (not cleared) once a real Plan is chosen — it becomes inert,
    // matching how CurrentCycleStart/End are never retroactively cleared either.
    public DateTimeOffset? TrialEndsAt { get; set; }
    public DateTimeOffset CurrentCycleStart { get; set; }
    public DateTimeOffset CurrentCycleEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

- [ ] **Step 3: Add `TrialDays` to `BillingOptions`**

```csharp
namespace Courier.Billing.Application;

public class BillingOptions
{
    public int GraceDays { get; set; } = 7;
    public int TrialDays { get; set; } = 7;
}
```

- [ ] **Step 4: Generate and inspect the migration**

Run: `dotnet ef migrations add AddSubscriptionTrial --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`

Expected in the generated file: `PlanId` column on `Subscriptions` becomes nullable (`Guid?` / `uuid NULL`), and a new nullable `TrialEndsAt` (`timestamptz NULL`) column is added. There is no FK constraint on `Subscriptions.PlanId` today (no navigation property was ever configured in `SubscriptionConfiguration.cs` or `AppDbContext.OnModelCreating`) — confirm the generated migration does not introduce one either; if it does, something about the model changed unexpectedly and needs investigating before proceeding.

- [ ] **Step 5: Add the new lookup interface members**

In `src/Modules/Billing/Application/IBillingLookupService.cs`, add the new record and the three new interface members (leave every existing member untouched):

```csharp
namespace Courier.Billing.Application;

public record PlanSummary(string PlanName, decimal MonthlyPriceNpr, Courier.Billing.Domain.SubscriptionStatus Status);
public record PlatformBillingMetrics(decimal MonthlyRecurringRevenueNpr, int OverdueInvoiceCount);
public record InvoiceAdminRecord(Guid Id, Guid OrganizationId, string PlanName, decimal AmountNpr, Courier.Billing.Domain.InvoiceStatus Status, DateTimeOffset DueAt, DateTimeOffset? PaidAt);
// Unlike PlanSummary (which requires a chosen Plan), this reflects a Subscription's real state
// even during/after a trial, when PlanId is null — PlanName/MonthlyPriceNpr are null in that case.
public record SubscriptionStatusInfo(Courier.Billing.Domain.SubscriptionStatus Status, DateTimeOffset? TrialEndsAt, string? PlanName, decimal? MonthlyPriceNpr);

public interface IBillingLookupService
{
    // Null means "no limit" (unlimited) OR "this org has no Subscription yet" — both cases the
    // caller (BranchService, via IBranchPlanLimitProvider) must treat identically as "do not block."
    Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default);

    // Cross-tenant read for Platform's PlatformDashboardService (SuperAdmin dashboard) — always
    // called with ICurrentTenantProvider.OrganizationId == null, so the implementation must
    // IgnoreQueryFilters().
    Task<PlatformBillingMetrics> GetPlatformMetricsAsync(CancellationToken cancellationToken = default);

    // Thin wrapper over SubscriptionService.ListInvoicesAsync, added to the interface so Platform
    // never needs to inject SubscriptionService directly (mirrors the rest of this interface's
    // narrow-lookup shape).
    Task<List<InvoiceResponse>> ListInvoicesForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);

    // Cross-org read for Platform's PlatformDashboardService (SuperAdmin invoices list) — like
    // GetPlatformMetricsAsync above, always called with a null ambient tenant, so the
    // implementation must IgnoreQueryFilters().
    Task<List<InvoiceAdminRecord>> ListAllInvoicesAsync(Courier.Billing.Domain.InvoiceStatus? status, CancellationToken cancellationToken = default);

    // Starts a 7-day (BillingOptions.TrialDays) trial with no committed Plan — called once, from
    // Identity's AuthService.RegisterOrganizationAsync via the Api-layer
    // IOrganizationSubscriptionStarter adapter (Task 2), and never again for that organization.
    Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default);

    // True only when Status == TrialExpired — consumed by TrialEnforcementMiddleware (Task 5) on
    // every non-GET request. An org with no Subscription at all, or Active/PastDue/Trial/
    // Cancelled, is never blocked by this — only a lapsed, never-converted trial is.
    Task<bool> IsWriteBlockedAsync(Guid organizationId, CancellationToken cancellationToken = default);

    // Unlike GetPlanSummaryAsync (which returns null whenever PlanId is null), this always
    // returns the Subscription's real state when one exists — used by the staff /billing page's
    // trial countdown and Platform's org-detail Sheet (Task 6/7/8).
    Task<SubscriptionStatusInfo?> GetSubscriptionStatusAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6: Update `SubscriptionService`'s constructor and existing join queries, add the three new methods, and audit `AssignPlanAsync`**

Replace the whole file with:

```csharp
using Courier.Audit.Application;
using Courier.Billing.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Courier.Billing.Application;

public record SubscriptionResponse(Guid Id, Guid OrganizationId, Guid PlanId, string PlanName, SubscriptionStatus Status, DateTimeOffset CurrentCycleStart, DateTimeOffset CurrentCycleEnd);
public record InvoiceResponse(Guid Id, string PlanName, decimal AmountNpr, DateTimeOffset CycleStart, DateTimeOffset CycleEnd, InvoiceStatus Status, DateTimeOffset DueAt, DateTimeOffset? PaidAt);

public class SubscriptionService : IBillingLookupService
{
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

    // Called by the staff self-serve /billing/plan endpoint (real ambient tenant, Task 3) and by
    // Platform's SuperAdmin org-detail Sheet (null ambient tenant, Task 6) to upgrade/downgrade/
    // first-assign an org's Plan.
    public async Task<SubscriptionResponse> AssignPlanAsync(Guid organizationId, Guid planId, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.Set<Plan>().FirstOrDefaultAsync(p => p.Id == planId && p.IsActive, cancellationToken)
            ?? throw new BillingValidationException("Plan was not found or is retired.");

        var existing = await _dbContext.Set<Subscription>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);
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

        // IsCrossTenantAction is derived from the real ambient tenant, never trusted from a
        // caller-supplied flag — true whenever the acting request's own org differs from (or has
        // no) the organization being changed, which is always true for a null-ambient-tenant
        // Platform caller and always false for an org's own self-serve call.
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "subscription.plan_assigned", "Subscription", existing.Id.ToString(), IsCrossTenantAction: _currentTenantProvider.OrganizationId != organizationId), cancellationToken);

        return new SubscriptionResponse(existing.Id, existing.OrganizationId, existing.PlanId!.Value, plan.Name, existing.Status, existing.CurrentCycleStart, existing.CurrentCycleEnd);
    }

    // Called exactly once per organization, from AuthService.RegisterOrganizationAsync via
    // IOrganizationSubscriptionStarter (Task 2). Deliberately assigns no Plan — a fresh platform
    // install with zero Plans configured yet must still let organizations register.
    public async Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PlanId = null,
            Status = SubscriptionStatus.Trial,
            TrialEndsAt = now.AddDays(_options.TrialDays),
            CurrentCycleStart = now,
            CurrentCycleEnd = now.AddMonths(1),
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.Set<Subscription>().Add(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "subscription.trial_started", "Subscription", subscription.Id.ToString()), cancellationToken);
    }

    public async Task<SubscriptionResponse?> GetCurrentAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var result = await (
            from s in _dbContext.Set<Subscription>()
            join p in _dbContext.Set<Plan>() on s.PlanId equals (Guid?)p.Id
            where s.OrganizationId == organizationId
            select new SubscriptionResponse(s.Id, s.OrganizationId, p.Id, p.Name, s.Status, s.CurrentCycleStart, s.CurrentCycleEnd)
        ).FirstOrDefaultAsync(cancellationToken);
        return result;
    }

    public async Task<int?> GetMaxBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await (
            from s in _dbContext.Set<Subscription>()
            join p in _dbContext.Set<Plan>() on s.PlanId equals (Guid?)p.Id
            where s.OrganizationId == organizationId
            select (int?)p.MaxBranches
        ).FirstOrDefaultAsync(cancellationToken);
        // No Subscription row, or a Trial/TrialExpired row with no PlanId, both naturally yield
        // null here (the join simply finds no match) — treated as "unlimited," matching this
        // interface's existing fail-open default. A trial organization is not branch-limited.
    }

    // Dual-use: called under a REAL ambient tenant from BillingEndpoints (staff billing tab), and
    // under a NULL ambient tenant from Platform's OrganizationLifecycleService/PlatformDashboardService
    // (Platform always runs with no tenant context). IgnoreQueryFilters() is required so the
    // null-tenant caller isn't zeroed out by the fail-closed ambient filter — safe for the
    // real-tenant caller too, since the explicit `s.OrganizationId == organizationId` predicate
    // below already does the real scoping.
    public async Task<PlanSummary?> GetPlanSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await (
            from s in _dbContext.Set<Subscription>().IgnoreQueryFilters()
            join p in _dbContext.Set<Plan>().IgnoreQueryFilters() on s.PlanId equals (Guid?)p.Id
            where s.OrganizationId == organizationId
            select new PlanSummary(p.Name, p.MonthlyPriceNpr, s.Status)
        ).FirstOrDefaultAsync(cancellationToken);
    }

    // See IBillingLookupService.GetSubscriptionStatusAsync's doc comment — unlike
    // GetPlanSummaryAsync above, this returns a result even when PlanId is null (Trial/
    // TrialExpired with no Plan chosen yet). Same dual-use IgnoreQueryFilters() reasoning.
    public async Task<SubscriptionStatusInfo?> GetSubscriptionStatusAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.Set<Subscription>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        string? planName = null;
        decimal? monthlyPriceNpr = null;
        if (subscription.PlanId is Guid planId)
        {
            var plan = await _dbContext.Set<Plan>().IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
            planName = plan?.Name;
            monthlyPriceNpr = plan?.MonthlyPriceNpr;
        }

        return new SubscriptionStatusInfo(subscription.Status, subscription.TrialEndsAt, planName, monthlyPriceNpr);
    }

    // Consumed by TrialEnforcementMiddleware on every non-GET request — kept to a single-column
    // projection since it runs far more often than any other method here. The middleware always
    // supplies a real organizationId from the current request's own tenant claim, but this still
    // follows the same IgnoreQueryFilters()-plus-explicit-predicate convention as every other
    // method in this class, for consistency rather than correctness.
    public async Task<bool> IsWriteBlockedAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var status = await _dbContext.Set<Subscription>().IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId)
            .Select(s => (SubscriptionStatus?)s.Status)
            .FirstOrDefaultAsync(cancellationToken);
        return status == SubscriptionStatus.TrialExpired;
    }

    // Dual-use, same reasoning as GetPlanSummaryAsync above — called under a real ambient tenant
    // (staff billing tab) and under a null ambient tenant (Platform org-detail Sheet's Invoices
    // section via ListInvoicesForOrganizationAsync below).
    public async Task<List<InvoiceResponse>> ListInvoicesAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Invoice>().IgnoreQueryFilters()
            .Where(i => i.OrganizationId == organizationId)
            .OrderByDescending(i => i.CycleStart)
            .Select(i => new InvoiceResponse(i.Id, i.PlanNameSnapshot, i.AmountNprSnapshot, i.CycleStart, i.CycleEnd, i.Status, i.DueAt, i.PaidAt))
            .ToListAsync(cancellationToken);
    }

    // Cross-org by design (a platform-wide metric) — IgnoreQueryFilters() is required since
    // the caller (Platform) always runs with a null ambient tenant.
    public async Task<PlatformBillingMetrics> GetPlatformMetricsAsync(CancellationToken cancellationToken = default)
    {
        var mrr = await (
            from s in _dbContext.Set<Subscription>().IgnoreQueryFilters()
            join p in _dbContext.Set<Plan>().IgnoreQueryFilters() on s.PlanId equals (Guid?)p.Id
            where s.Status == SubscriptionStatus.Active
            select p.MonthlyPriceNpr
        ).SumAsync(cancellationToken);

        var overdueCount = await _dbContext.Set<Invoice>().IgnoreQueryFilters()
            .CountAsync(i => i.Status == InvoiceStatus.Overdue, cancellationToken);

        return new PlatformBillingMetrics(mrr, overdueCount);
    }

    public Task<List<InvoiceResponse>> ListInvoicesForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => ListInvoicesAsync(organizationId, cancellationToken);

    // Cross-org by design (Platform's SuperAdmin invoices list) — IgnoreQueryFilters() is
    // required since the caller always runs with a null ambient tenant, same as
    // GetPlatformMetricsAsync above.
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
}
```

Note `GetCurrentAsync` (unused by any endpoint today, but part of the public API) now selects `p.Id`/`p.Name` from the joined `Plan` instead of `existing.PlanId`/reading a variable named `plan` — this is required because `existing.PlanId` is now `Guid?` and `SubscriptionResponse.PlanId` is non-nullable `Guid`; the join already guarantees `p.Id` is the real, non-null match.

- [ ] **Step 7: Fix the two existing `Api.Tests` call sites**

In `tests/Api.Tests/BillingEndpointsTests.cs`, change:

```csharp
await subscriptionService.AssignPlanAsync(org.OrganizationId, plan.Id);
```

to:

```csharp
await subscriptionService.AssignPlanAsync(org.OrganizationId, plan.Id, org.UserId);
```

In `tests/Api.Tests/BranchesEndpointsTests.cs`, change:

```csharp
await subscriptionService.AssignPlanAsync(auth.OrganizationId, plan.Id);
```

to:

```csharp
await subscriptionService.AssignPlanAsync(auth.OrganizationId, plan.Id, auth.UserId);
```

and update the now-stale comment directly above it (currently: `// Org gets a default (unlimited) plan at self-registration (Task 7) — the first branch // succeeds regardless of any limit.`) to:

```csharp
        // Org gets a Trial subscription with no Plan at self-registration (Task 2 of the
        // trial-plan-assignment plan) — the first branch succeeds regardless of any limit,
        // since GetMaxBranchesAsync treats "no Plan chosen" the same as "unlimited."
```

- [ ] **Step 8: Fix the two `Platform.Tests` fakes**

In both `tests/Platform.Tests/OrganizationLifecycleServiceTests.cs` and `tests/Platform.Tests/PlatformDashboardServiceTests.cs`, add these three members to each file's `FakeBillingLookupService`:

```csharp
        public Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> IsWriteBlockedAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<SubscriptionStatusInfo?> GetSubscriptionStatusAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult<SubscriptionStatusInfo?>(null);
```

- [ ] **Step 9: Update `SubscriptionServiceTests.cs`'s existing tests for the new constructor and `AssignPlanAsync` signature**

Replace the whole file with:

```csharp
using Courier.Audit.Application;
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.SharedKernel;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Billing.Tests;

public class SubscriptionServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public FixedTenantProvider(Guid? organizationId) { OrganizationId = organizationId; }
        public Guid? OrganizationId { get; }
    }

    // Platform (OrganizationLifecycleService/PlatformDashboardService) always calls
    // GetPlanSummaryAsync/ListInvoicesAsync with a null ambient tenant — a real EF-backed test
    // using this provider is the only way to catch a missing IgnoreQueryFilters() here, since a
    // hand-written fake IBillingLookupService (as Platform.Tests uses) can't exercise
    // AppDbContext's actual fail-closed query filter behavior.
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();
        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private static SubscriptionService CreateService(Courier.Persistence.AppDbContext dbContext, Courier.SharedKernel.ICurrentTenantProvider tenantProvider, FakeAuditLogger? auditLogger = null, int trialDays = 7) =>
        new(dbContext, auditLogger ?? new FakeAuditLogger(), Options.Create(new BillingOptions { TrialDays = trialDays }), tenantProvider);

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
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));

        var result = await service.AssignPlanAsync(organizationId, plan.Id, Guid.NewGuid());

        Assert.Equal(SubscriptionStatus.Active, result.Status);
        Assert.Equal(plan.Id, result.PlanId);
    }

    [Fact]
    public async Task AssignPlanAsync_LogsAuditEntry()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var plan = await SeedPlanAsync(dbContext);
        var auditLogger = new FakeAuditLogger();
        var actingUserId = Guid.NewGuid();
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId), auditLogger);

        await service.AssignPlanAsync(organizationId, plan.Id, actingUserId);

        var entry = Assert.Single(auditLogger.Entries);
        Assert.Equal("subscription.plan_assigned", entry.Action);
        Assert.Equal(actingUserId, entry.UserId);
        Assert.False(entry.IsCrossTenantAction);
    }

    [Fact]
    public async Task AssignPlanAsync_FromNullAmbientTenant_LogsCrossTenantAuditEntry()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var plan = await SeedPlanAsync(dbContext);
        var auditLogger = new FakeAuditLogger();
        var service = CreateService(dbContext, new NullTenantProvider(), auditLogger);

        await service.AssignPlanAsync(organizationId, plan.Id, Guid.NewGuid());

        var entry = Assert.Single(auditLogger.Entries);
        Assert.True(entry.IsCrossTenantAction);
    }

    [Fact]
    public async Task GetMaxBranchesAsync_PlanWithLimit_ReturnsLimit()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var plan = await SeedPlanAsync(dbContext, maxBranches: 3);
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));
        await service.AssignPlanAsync(organizationId, plan.Id, Guid.NewGuid());

        var max = await service.GetMaxBranchesAsync(organizationId);

        Assert.Equal(3, max);
    }

    [Fact]
    public async Task GetMaxBranchesAsync_NoSubscription_ReturnsNullUnlimited()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));

        var max = await service.GetMaxBranchesAsync(organizationId);

        Assert.Null(max);
    }

    [Fact]
    public async Task GetMaxBranchesAsync_TrialWithNoPlan_ReturnsNullUnlimited()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));
        await service.StartTrialAsync(organizationId, Guid.NewGuid());

        var max = await service.GetMaxBranchesAsync(organizationId);

        Assert.Null(max);
    }

    // Regression test for the Critical silent-data-empty bug: under Platform's real null ambient
    // tenant, GetPlanSummaryAsync must still return the org's actual plan summary, not null
    // (which the fail-closed Subscription/Plan query filters would otherwise force).
    [Fact]
    public async Task GetPlanSummaryAsync_WithNullAmbientTenant_ReturnsRealPlanSummary()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Growth", Slug = $"growth-{Guid.NewGuid():N}", MonthlyPriceNpr = 3000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new NullTenantProvider());

        var result = await service.GetPlanSummaryAsync(organizationId);

        Assert.NotNull(result);
        Assert.Equal("Growth", result!.PlanName);
        Assert.Equal(3000m, result.MonthlyPriceNpr);
        Assert.Equal(SubscriptionStatus.Active, result.Status);
    }

    // Regression test for the same bug in ListInvoicesAsync — the org-detail Sheet's Invoices
    // section is populated from this method under Platform's null ambient tenant.
    [Fact]
    public async Task ListInvoicesAsync_WithNullAmbientTenant_ReturnsRealInvoices()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Growth", Slug = $"growth-{Guid.NewGuid():N}", MonthlyPriceNpr = 3000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow };
        var invoice = new Invoice { Id = Guid.NewGuid(), OrganizationId = organizationId, SubscriptionId = subscription.Id, PlanId = plan.Id, PlanNameSnapshot = plan.Name, AmountNprSnapshot = plan.MonthlyPriceNpr, CycleStart = DateTimeOffset.UtcNow.AddMonths(-1), CycleEnd = DateTimeOffset.UtcNow, Status = InvoiceStatus.Paid, DueAt = DateTimeOffset.UtcNow.AddDays(-1), PaidAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1) };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        dbContext.Set<Invoice>().Add(invoice);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new NullTenantProvider());

        var result = await service.ListInvoicesAsync(organizationId);

        var singleInvoice = Assert.Single(result);
        Assert.Equal(invoice.Id, singleInvoice.Id);
        Assert.Equal(plan.Name, singleInvoice.PlanName);
    }

    [Fact]
    public async Task StartTrialAsync_CreatesTrialSubscriptionWithNoPlan()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId), trialDays: 7);
        var beforeCall = DateTimeOffset.UtcNow;

        await service.StartTrialAsync(organizationId, Guid.NewGuid());

        var subscription = dbContext.Set<Subscription>().Single(s => s.OrganizationId == organizationId);
        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
        Assert.Null(subscription.PlanId);
        Assert.NotNull(subscription.TrialEndsAt);
        Assert.True(subscription.TrialEndsAt >= beforeCall.AddDays(7) && subscription.TrialEndsAt <= DateTimeOffset.UtcNow.AddDays(7));
    }

    [Fact]
    public async Task StartTrialAsync_LogsAuditEntry()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var auditLogger = new FakeAuditLogger();
        var actingUserId = Guid.NewGuid();
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId), auditLogger);

        await service.StartTrialAsync(organizationId, actingUserId);

        var entry = Assert.Single(auditLogger.Entries);
        Assert.Equal("subscription.trial_started", entry.Action);
        Assert.Equal(actingUserId, entry.UserId);
    }

    [Fact]
    public async Task IsWriteBlockedAsync_TrialExpiredStatus_ReturnsTrue()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        dbContext.Set<Subscription>().Add(new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, Status = SubscriptionStatus.TrialExpired, TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-1), CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));

        Assert.True(await service.IsWriteBlockedAsync(organizationId));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trial)]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Cancelled)]
    public async Task IsWriteBlockedAsync_AnyOtherStatus_ReturnsFalse(SubscriptionStatus status)
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        dbContext.Set<Subscription>().Add(new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, Status = status, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));

        Assert.False(await service.IsWriteBlockedAsync(organizationId));
    }

    [Fact]
    public async Task IsWriteBlockedAsync_NoSubscription_ReturnsFalse()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));

        Assert.False(await service.IsWriteBlockedAsync(organizationId));
    }

    [Fact]
    public async Task GetSubscriptionStatusAsync_TrialWithNoPlan_ReturnsStatusWithNullPlanFields()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));
        await service.StartTrialAsync(organizationId, Guid.NewGuid());

        var result = await service.GetSubscriptionStatusAsync(organizationId);

        Assert.NotNull(result);
        Assert.Equal(SubscriptionStatus.Trial, result!.Status);
        Assert.NotNull(result.TrialEndsAt);
        Assert.Null(result.PlanName);
        Assert.Null(result.MonthlyPriceNpr);
    }

    [Fact]
    public async Task GetSubscriptionStatusAsync_WithNullAmbientTenant_ReturnsRealStatus()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Growth", Slug = $"growth-{Guid.NewGuid():N}", MonthlyPriceNpr = 3000m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new NullTenantProvider());

        var result = await service.GetSubscriptionStatusAsync(organizationId);

        Assert.NotNull(result);
        Assert.Equal("Growth", result!.PlanName);
        Assert.Equal(3000m, result.MonthlyPriceNpr);
    }

    [Fact]
    public async Task GetSubscriptionStatusAsync_NoSubscription_ReturnsNull()
    {
        var organizationId = Guid.NewGuid();
        using var dbContext = TestDbContextFactory.Create(new FixedTenantProvider(organizationId));
        var service = CreateService(dbContext, new FixedTenantProvider(organizationId));

        Assert.Null(await service.GetSubscriptionStatusAsync(organizationId));
    }
}
```

- [ ] **Step 10: Run the affected test projects**

Run: `dotnet test tests/Billing.Tests/Billing.Tests.csproj && dotnet test tests/Platform.Tests/Platform.Tests.csproj`
Expected: all green, including every pre-existing test in both projects.

- [ ] **Step 11: Full-solution build**

Run: `dotnet build Courier.slnx`
Expected: succeeds. If `tests/Api.Tests` fails to compile, re-check Step 7's two call sites were updated exactly as shown (these are the only two production/test call sites outside `Billing.Tests`/`Platform.Tests` that reference `AssignPlanAsync` today).

- [ ] **Step 12: Commit**

```bash
git add src/Modules/Billing tests/Billing.Tests tests/Api.Tests/BillingEndpointsTests.cs tests/Api.Tests/BranchesEndpointsTests.cs tests/Platform.Tests/OrganizationLifecycleServiceTests.cs tests/Platform.Tests/PlatformDashboardServiceTests.cs src/Persistence/Migrations
git commit -m "feat(billing): add trial subscription status and SubscriptionService support"
```

---

## Task 2: Registration wiring — start a trial at self-registration

**Files:**
- Create: `src/Modules/Identity/Application/IOrganizationSubscriptionStarter.cs`
- Create: `src/Api/OrganizationSubscriptionStarterAdapter.cs`
- Modify: `src/Modules/Identity/Application/AuthService.cs`
- Modify: `src/Api/Program.cs`
- Modify: `tests/Identity.Tests/AuthServiceTests.cs`
- Modify: `tests/Identity.Tests/AuthServiceAcceptInviteTests.cs`
- Modify: `tests/Identity.Tests/UserProvisioningServiceTests.cs`

**Interfaces:**
- Consumes: `IBillingLookupService.StartTrialAsync` (Task 1).
- Produces: `Courier.Identity.Application.IOrganizationSubscriptionStarter.StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default)`.
- Produces: `Courier.Api.OrganizationSubscriptionStarterAdapter : IOrganizationSubscriptionStarter`.

- [ ] **Step 1: Add the local port interface in `Identity.Application`**

```csharp
namespace Courier.Identity.Application;

// Local port for starting a new organization's trial subscription, consumed by
// AuthService.RegisterOrganizationAsync. Defined here — rather than AuthService taking a direct
// dependency on Courier.Billing.Application.IBillingLookupService — to avoid a circular project
// reference: Billing already references Identity (for Organization), so a reverse
// Identity -> Billing reference would create a cycle. The Api project already references both
// modules and is referenced by neither, so it is the one place that can bridge this (see
// Api/OrganizationSubscriptionStarterAdapter.cs), matching the same shape already used for
// Courier.Branches.Application.IBranchPlanLimitProvider / BranchPlanLimitAdapter.
public interface IOrganizationSubscriptionStarter
{
    Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Add the Api-layer adapter**

```csharp
using Courier.Billing.Application;
using Courier.Identity.Application;

namespace Courier.Api;

// Bridges Identity.Application.IOrganizationSubscriptionStarter (consumed by
// AuthService.RegisterOrganizationAsync) to Billing.Application.IBillingLookupService.
// StartTrialAsync without either module taking a project reference to the other — see
// IOrganizationSubscriptionStarter.cs for why a direct Identity -> Billing reference would
// create a project-reference cycle. The Api project already references both modules
// (transitively, via Persistence) and is referenced by neither, so it is the one place that can
// wire this adapter — same shape as BranchPlanLimitAdapter/MerchantOrganizationLookupAdapter.
public class OrganizationSubscriptionStarterAdapter : IOrganizationSubscriptionStarter
{
    private readonly IBillingLookupService _billingLookupService;

    public OrganizationSubscriptionStarterAdapter(IBillingLookupService billingLookupService)
    {
        _billingLookupService = billingLookupService;
    }

    public Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default) =>
        _billingLookupService.StartTrialAsync(organizationId, actingUserId, cancellationToken);
}
```

- [ ] **Step 3: Wire the adapter in `Program.cs`**

In `src/Api/Program.cs`, immediately after the existing line `builder.Services.AddScoped<IBranchPlanLimitProvider, Courier.Api.BranchPlanLimitAdapter>();` (currently line 148), add:

```csharp
builder.Services.AddScoped<Courier.Identity.Application.IOrganizationSubscriptionStarter, Courier.Api.OrganizationSubscriptionStarterAdapter>();
```

- [ ] **Step 4: Wire `AuthService` to call it at the end of registration**

In `src/Modules/Identity/Application/AuthService.cs`, add the constructor dependency:

```csharp
public class AuthService
{
    private readonly DbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IOrganizationSubscriptionStarter _organizationSubscriptionStarter;

    // ... DummyPasswordHash field unchanged ...

    public AuthService(DbContext dbContext, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService, IAuditLogger auditLogger, IBranchLookupService branchLookupService, IOrganizationSubscriptionStarter organizationSubscriptionStarter)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _auditLogger = auditLogger;
        _branchLookupService = branchLookupService;
        _organizationSubscriptionStarter = organizationSubscriptionStarter;
    }
```

Then, in `RegisterOrganizationAsync`, immediately after the existing `await _auditLogger.LogAsync(new AuditEntry(organization.Id, owner.Id, "organization.registered", "Organization", organization.Id.ToString()), cancellationToken);` line and before `var accessToken = ...`, add:

```csharp
        // Every self-registered organization starts on a 7-day trial with no committed Plan —
        // TrialEnforcementMiddleware only blocks writes once TrialExpiryJob flips this to
        // TrialExpired with no real Plan chosen. Called after the main SaveChangesAsync above
        // (not before) so this follows the same "save the primary entities, then record the
        // event" sequencing this method already uses for the "organization.registered" audit
        // entry directly above.
        await _organizationSubscriptionStarter.StartTrialAsync(organization.Id, owner.Id, cancellationToken);
```

- [ ] **Step 5: Update the three test files' `CreateService` helpers**

In each of `tests/Identity.Tests/AuthServiceTests.cs`, `tests/Identity.Tests/AuthServiceAcceptInviteTests.cs`, and `tests/Identity.Tests/UserProvisioningServiceTests.cs`, add this fake next to the existing `file class NullBranchLookupService : IBranchLookupService { ... }` block:

```csharp
file class NoopOrganizationSubscriptionStarter : IOrganizationSubscriptionStarter
{
    public Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

and change each file's `CreateService` helper's final line from:

```csharp
        return new AuthService(dbContext, new PasswordHasherAdapter(), new JwtTokenService(jwtOptions), new NoopAuditLogger(), new NullBranchLookupService());
```

to:

```csharp
        return new AuthService(dbContext, new PasswordHasherAdapter(), new JwtTokenService(jwtOptions), new NoopAuditLogger(), new NullBranchLookupService(), new NoopOrganizationSubscriptionStarter());
```

- [ ] **Step 6: Write a failing test proving registration starts a trial**

Add to `tests/Identity.Tests/AuthServiceTests.cs`, replacing `NoopOrganizationSubscriptionStarter` usage in this one new test with a recording fake so the test can assert on what was actually called — add this second fake alongside the no-op one:

```csharp
file class RecordingOrganizationSubscriptionStarter : IOrganizationSubscriptionStarter
{
    public List<(Guid OrganizationId, Guid ActingUserId)> Calls { get; } = new();
    public Task StartTrialAsync(Guid organizationId, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        Calls.Add((organizationId, actingUserId));
        return Task.CompletedTask;
    }
}
```

Then add the test:

```csharp
    [Fact]
    public async Task RegisterOrganizationAsync_StartsTrialSubscriptionForNewOrganization()
    {
        await using var dbContext = CreateContext();
        var jwtOptions = Options.Create(new JwtOptions
        {
            SigningKey = "identity-tests-signing-key-at-least-32-bytes!",
            Issuer = "courier-tests",
            Audience = "courier-tests"
        });
        var subscriptionStarter = new RecordingOrganizationSubscriptionStarter();
        var service = new AuthService(dbContext, new PasswordHasherAdapter(), new JwtTokenService(jwtOptions), new NoopAuditLogger(), new NullBranchLookupService(), subscriptionStarter);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var result = await service.RegisterOrganizationAsync(new RegisterOrganizationRequest(
            $"Trial Org {suffix}", $"trial-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person"));

        var call = Assert.Single(subscriptionStarter.Calls);
        Assert.Equal(result.OrganizationId, call.OrganizationId);
        Assert.Equal(result.UserId, call.ActingUserId);
    }
```

- [ ] **Step 7: Run the test to verify it fails, then passes**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter RegisterOrganizationAsync_StartsTrialSubscriptionForNewOrganization`
Expected before Steps 1-4: compile error (`IOrganizationSubscriptionStarter` doesn't exist). After: PASS.

- [ ] **Step 8: Run the full `Identity.Tests` project**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj`
Expected: all green, including every pre-existing test (now passing the new fake).

- [ ] **Step 9: Full-solution build and Api.Tests regression**

Run: `dotnet build Courier.slnx && dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: all green — every existing `*EndpointsTests.cs` that calls `RegisterOrgAsync`/`/api/v1/auth/register` must still pass unchanged (the new DI-wired `IOrganizationSubscriptionStarter` resolves automatically once Step 3's registration is in place).

- [ ] **Step 10: Commit**

```bash
git add src/Modules/Identity src/Api/Program.cs src/Api/OrganizationSubscriptionStarterAdapter.cs tests/Identity.Tests
git commit -m "feat(identity): start a trial subscription at self-registration"
```

---

## Task 3: Self-serve plan endpoints

**Files:**
- Modify: `src/Modules/Billing/Application/PlanService.cs`
- Modify: `src/Modules/Billing/Presentation/BillingEndpoints.cs`
- Modify: `tests/Billing.Tests/PlanServiceTests.cs`
- Modify: `tests/Api.Tests/BillingEndpointsTests.cs`

**Interfaces:**
- Consumes: `SubscriptionService.AssignPlanAsync`/`GetSubscriptionStatusAsync` (Task 1).
- Produces: `Courier.Billing.Application.PlanService.ListActivePlansAsync(CancellationToken)`.
- Produces routes: `GET /api/v1/billing/plans`, `POST /api/v1/billing/plan`, `GET /api/v1/billing/subscription`.

- [ ] **Step 1: Add `ListActivePlansAsync` to `PlanService`**

In `src/Modules/Billing/Application/PlanService.cs`, add this method (it reuses the existing `PlanResponse` record and `ToResponse` helper already in this file — do not duplicate them):

```csharp
    // Staff-facing subset of ListPlansAsync (which is SuperAdmin-only and includes retired
    // plans) — an OrganizationOwner choosing a plan must never be offered a retired one.
    public async Task<List<PlanResponse>> ListActivePlansAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Set<Plan>().Where(p => p.IsActive).OrderBy(p => p.MonthlyPriceNpr).Select(p => ToResponse(p)).ToListAsync(cancellationToken);
```

- [ ] **Step 2: Write the failing `PlanServiceTests` case**

Add to `tests/Billing.Tests/PlanServiceTests.cs`:

```csharp
    [Fact]
    public async Task ListActivePlansAsync_ExcludesRetiredPlans()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var service = new PlanService(dbContext, new FakeAuditLogger());
        var activeSlug = $"active-{Guid.NewGuid():N}";
        var retiredSlug = $"retired-{Guid.NewGuid():N}";
        var active = await service.CreatePlanAsync(new CreatePlanRequest("Active Plan", activeSlug, 500m, null, null, null, false), Guid.NewGuid());
        var retired = await service.CreatePlanAsync(new CreatePlanRequest("Retired Plan", retiredSlug, 900m, null, null, null, false), Guid.NewGuid());
        await service.RetirePlanAsync(retired.Id, Guid.NewGuid());

        var result = await service.ListActivePlansAsync();

        Assert.Contains(result, p => p.Id == active.Id);
        Assert.DoesNotContain(result, p => p.Id == retired.Id);
    }
```

- [ ] **Step 3: Run it to verify it fails, then implement, then pass**

Run: `dotnet test tests/Billing.Tests/Billing.Tests.csproj --filter ListActivePlansAsync_ExcludesRetiredPlans`
Expected before Step 1: compile error (no such method). After Step 1: PASS.

- [ ] **Step 4: Add the three new routes to `BillingEndpoints.cs`**

Replace the whole file with:

```csharp
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Billing.Presentation;

public record PayInvoiceRequest(PaymentGateway Gateway, string ReturnBaseUrl);
public record AssignPlanRequest(Guid PlanId);

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

        // Unlike GET /plan above (404s whenever no Plan is chosen — e.g. during a trial), this
        // always reflects the Subscription's real state, so the staff Billing page can render a
        // trial countdown or a "trial expired" prompt instead of treating a 404 as an error.
        group.MapGet("/subscription", async (SubscriptionService service, ICurrentTenantProvider tenantProvider, CancellationToken cancellationToken) =>
        {
            var status = await service.GetSubscriptionStatusAsync(tenantProvider.OrganizationId!.Value, cancellationToken);
            return status is null ? Results.NotFound() : Results.Ok(status);
        }).RequirePermission("org.billing.manage");

        group.MapGet("/plans", async (PlanService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListActivePlansAsync(cancellationToken)))
            .RequirePermission("org.billing.manage");

        // The one call this entire feature is about — finally reachable from a real request.
        // Reuses AssignPlanAsync unmodified: it creates-or-updates the org's Subscription, sets
        // Status = Active, and (since a Trial subscription row already exists from
        // registration) takes the "existing is not null" branch, leaving CurrentCycleStart/End
        // as originally set at trial start rather than resetting the billing cycle clock.
        group.MapPost("/plan", async (AssignPlanRequest request, SubscriptionService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.AssignPlanAsync(tenantProvider.OrganizationId!.Value, request.PlanId, httpContext.User.GetUserId()!.Value, cancellationToken);
                return Results.Ok(result);
            }
            catch (BillingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BILLING_VALIDATION_FAILED", message = ex.Message } });
            }
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

The only real changes in this step are the two new routes (`/subscription`, `/plans`) and the new `/plan` POST route plus the `AssignPlanRequest` record — `/invoices/{id:guid}/pay` and `/payments/{id:guid}/callback` above are reproduced unchanged from the current file.

- [ ] **Step 5: Write the failing `Api.Tests` cases**

Add to `tests/Api.Tests/BillingEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task GetSubscription_DuringTrial_ReturnsTrialStatusWithNoPlan()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "trialstatus");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/billing/subscription");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Trial", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("trialEndsAt").ValueKind != JsonValueKind.Null);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("planName").ValueKind);
    }

    [Fact]
    public async Task ListPlans_ReturnsOnlyActivePlans()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "listplans");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        using (var scope = _factory.Services.CreateScope())
        {
            var planService = scope.ServiceProvider.GetRequiredService<PlanService>();
            var retired = await planService.CreatePlanAsync(new CreatePlanRequest("ToRetire", $"to-retire-{Guid.NewGuid():N}", 100m, null, null, null, false), Guid.NewGuid());
            await planService.RetirePlanAsync(retired.Id, Guid.NewGuid());
        }

        var response = await client.GetAsync("/api/v1/billing/plans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plans = await response.Content.ReadFromJsonAsync<List<PlanResponse>>();
        Assert.DoesNotContain(plans!, p => !p.IsActive);
    }

    [Fact]
    public async Task AssignPlan_SelfServe_MovesOrgOffTrial()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "selfserveplan");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        Guid planId;
        using (var scope = _factory.Services.CreateScope())
        {
            var planService = scope.ServiceProvider.GetRequiredService<PlanService>();
            var plan = await planService.CreatePlanAsync(new CreatePlanRequest("SelfServe", $"self-serve-{Guid.NewGuid():N}", 500m, null, null, null, false), Guid.NewGuid());
            planId = plan.Id;
        }

        var response = await client.PostAsJsonAsync("/api/v1/billing/plan", new AssignPlanRequest(planId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var subscriptionResponse = await client.GetAsync("/api/v1/billing/subscription");
        var body = await subscriptionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.Equal("SelfServe", body.GetProperty("planName").GetString());
    }
```

- [ ] **Step 6: Run to verify failure then success**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "GetSubscription_DuringTrial_ReturnsTrialStatusWithNoPlan|ListPlans_ReturnsOnlyActivePlans|AssignPlan_SelfServe_MovesOrgOffTrial"`
Expected before Step 4: 404/compile-shaped failures. After: PASS.

- [ ] **Step 7: Full `Api.Tests` and `Billing.Tests` regression**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj && dotnet test tests/Billing.Tests/Billing.Tests.csproj`
Expected: all green.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Billing tests/Billing.Tests/PlanServiceTests.cs tests/Api.Tests/BillingEndpointsTests.cs
git commit -m "feat(billing): add self-serve plan listing, assignment, and status endpoints"
```

---

## Task 4: `TrialExpiryJob`

**Files:**
- Create: `src/Modules/Billing/Application/TrialExpiryJob.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Billing.Tests/TrialExpiryJobTests.cs`

**Interfaces:**
- Produces: `Courier.Billing.Application.TrialExpiryJob.RunAsync(CancellationToken)` — consumed by this task's own `Program.cs` `RecurringJob.AddOrUpdate` call.

- [ ] **Step 1: Write the failing test**

```csharp
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.SharedKernel;
using Xunit;

namespace Courier.Billing.Tests;

public class TrialExpiryJobTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    [Fact]
    public async Task RunAsync_TrialPastEndDateWithNoPlan_FlipsToTrialExpired()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = null, Status = SubscriptionStatus.Trial, TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-1), CurrentCycleStart = DateTimeOffset.UtcNow.AddDays(-7), CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow.AddDays(-7) };
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();
        var job = new TrialExpiryJob(dbContext);

        await job.RunAsync();

        var updated = dbContext.Set<Subscription>().IgnoreQueryFilters().Single(s => s.Id == subscription.Id);
        Assert.Equal(SubscriptionStatus.TrialExpired, updated.Status);
    }

    [Fact]
    public async Task RunAsync_TrialNotYetEnded_LeavesUnchanged()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = null, Status = SubscriptionStatus.Trial, TrialEndsAt = DateTimeOffset.UtcNow.AddDays(3), CurrentCycleStart = DateTimeOffset.UtcNow, CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();
        var job = new TrialExpiryJob(dbContext);

        await job.RunAsync();

        var unchanged = dbContext.Set<Subscription>().IgnoreQueryFilters().Single(s => s.Id == subscription.Id);
        Assert.Equal(SubscriptionStatus.Trial, unchanged.Status);
    }

    [Fact]
    public async Task RunAsync_TrialEndedButAlreadyOnRealPlan_LeavesUnchanged()
    {
        // A trial that already converted (AssignPlanAsync flipped it to Active) must never be
        // touched by this job even if its stale TrialEndsAt is in the past — only Status ==
        // Trial rows are candidates.
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var organizationId = Guid.NewGuid();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Converted", Slug = $"converted-{Guid.NewGuid():N}", MonthlyPriceNpr = 500m, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var subscription = new Subscription { Id = Guid.NewGuid(), OrganizationId = organizationId, PlanId = plan.Id, Status = SubscriptionStatus.Active, TrialEndsAt = DateTimeOffset.UtcNow.AddDays(-1), CurrentCycleStart = DateTimeOffset.UtcNow.AddDays(-7), CurrentCycleEnd = DateTimeOffset.UtcNow.AddMonths(1), CreatedAt = DateTimeOffset.UtcNow.AddDays(-7) };
        dbContext.Set<Plan>().Add(plan);
        dbContext.Set<Subscription>().Add(subscription);
        await dbContext.SaveChangesAsync();
        var job = new TrialExpiryJob(dbContext);

        await job.RunAsync();

        var unchanged = dbContext.Set<Subscription>().IgnoreQueryFilters().Single(s => s.Id == subscription.Id);
        Assert.Equal(SubscriptionStatus.Active, unchanged.Status);
    }
}
```

- [ ] **Step 2: Run it to verify it fails to compile**

Run: `dotnet test tests/Billing.Tests/Billing.Tests.csproj --filter TrialExpiryJobTests`
Expected: FAIL — `TrialExpiryJob` doesn't exist yet.

- [ ] **Step 3: Write `TrialExpiryJob`**

```csharp
using Courier.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Billing.Application;

// Hangfire recurring job, same "no ambient tenant, IgnoreQueryFilters() everywhere" shape as
// InvoiceGenerationJob/OverdueInvoiceJob. Unlike those two, this job needs no cross-module call —
// flipping Trial -> TrialExpired touches only this module's own Subscription table, so there is
// no equivalent of OverdueInvoiceJob's separate post-SaveChangesAsync organization-suspend step.
public class TrialExpiryJob
{
    private readonly DbContext _dbContext;

    public TrialExpiryJob(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredTrials = await _dbContext.Set<Subscription>().IgnoreQueryFilters()
            .Where(s => s.Status == SubscriptionStatus.Trial && s.TrialEndsAt != null && s.TrialEndsAt <= now)
            .ToListAsync(cancellationToken);

        if (expiredTrials.Count == 0)
        {
            return;
        }

        foreach (var subscription in expiredTrials)
        {
            subscription.Status = SubscriptionStatus.TrialExpired;
            subscription.UpdatedAt = now;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Billing.Tests/Billing.Tests.csproj --filter TrialExpiryJobTests`
Expected: PASS, all 3 cases.

- [ ] **Step 5: Register the job in `Program.cs`**

Immediately after the existing line `builder.Services.AddScoped<OverdueInvoiceJob>();` (currently line 170), add:

```csharp
builder.Services.AddScoped<TrialExpiryJob>();
```

Immediately after the existing line `RecurringJob.AddOrUpdate<OverdueInvoiceJob>("billing-overdue-invoice-suspension", job => job.RunAsync(CancellationToken.None), "0 2 * * *");` (currently line 417), add:

```csharp
RecurringJob.AddOrUpdate<TrialExpiryJob>("billing-trial-expiry", job => job.RunAsync(CancellationToken.None), "0 3 * * *");
```

- [ ] **Step 6: Full-solution build**

Run: `dotnet build Courier.slnx`
Expected: succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Billing/Application/TrialExpiryJob.cs src/Api/Program.cs tests/Billing.Tests/TrialExpiryJobTests.cs
git commit -m "feat(billing): add trial expiry recurring job"
```

---

## Task 5: `TrialEnforcementMiddleware`

**Files:**
- Create: `src/Api/Middleware/TrialEnforcementMiddleware.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Api.Tests/TrialEnforcementMiddlewareTests.cs`

**Interfaces:**
- Consumes: `IBillingLookupService.IsWriteBlockedAsync` (Task 1), `ICurrentTenantProvider.OrganizationId` (existing).

- [ ] **Step 1: Write the middleware**

```csharp
using Courier.Billing.Application;
using Courier.SharedKernel;

namespace Courier.Api.Middleware;

// Backend enforcement of trial expiry (CLAUDE.md §33: never trust the frontend as the
// enforcement boundary). Blocks non-GET requests for an org whose trial lapsed without a chosen
// Plan (Subscription.Status == TrialExpired, set by TrialExpiryJob). Placed after
// UseAuthentication()/UseAuthorization() (tenant identity is already resolved regardless of auth
// scheme — both the staff JWT and the merchant ApiKey scheme set an "org_id" claim, which
// ICurrentTenantProvider reads) and before IdempotencyMiddleware, so a request that is going to
// be rejected anyway never touches the idempotency store.
public class TrialEnforcementMiddleware
{
    private static readonly string[] ExemptPathPrefixes =
    [
        "/api/v1/auth",
        "/api/v1/billing",
        "/api/v1/platform",
        "/track",
        "/health",
        "/swagger",
        "/hangfire"
    ];

    private readonly RequestDelegate _next;

    public TrialEnforcementMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IBillingLookupService billingLookupService, ICurrentTenantProvider currentTenantProvider)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (ExemptPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var organizationId = currentTenantProvider.OrganizationId;
        if (organizationId is null)
        {
            await _next(context);
            return;
        }

        if (await billingLookupService.IsWriteBlockedAsync(organizationId.Value, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"error":{"code":"TRIAL_EXPIRED","message":"Your trial has ended. Choose a plan to continue."}}""");
            return;
        }

        await _next(context);
    }
}
```

- [ ] **Step 2: Register it in `Program.cs`**

In `src/Api/Program.cs`, insert it between the existing `app.UseMiddleware<Courier.Api.Middleware.ApiKeyRequestLoggingMiddleware>();` and `app.UseMiddleware<Courier.Api.Middleware.IdempotencyMiddleware>();` lines (currently lines 364-365):

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<Courier.Api.Middleware.ApiKeyRequestLoggingMiddleware>();
app.UseMiddleware<Courier.Api.Middleware.TrialEnforcementMiddleware>();
app.UseMiddleware<Courier.Api.Middleware.IdempotencyMiddleware>();
```

- [ ] **Step 3: Write the failing integration test**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.Identity.Application;
using Courier.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

public class TrialEnforcementMiddlewareTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TrialEnforcementMiddlewareTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"{label} Org {suffix}", $"{label}-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private async Task ExpireTrialAsync(Guid organizationId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await dbContext.Set<Subscription>().IgnoreQueryFilters().SingleAsync(s => s.OrganizationId == organizationId);
        subscription.Status = SubscriptionStatus.TrialExpired;
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task PostBranch_TrialExpired_Returns402()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "trialblock");
        await ExpireTrialAsync(org.OrganizationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/branches", new { });

        Assert.Equal((HttpStatusCode)402, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("TRIAL_EXPIRED", body);
    }

    [Fact]
    public async Task GetRequest_TrialExpired_StillSucceeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "trialblockread");
        await ExpireTrialAsync(org.OrganizationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/branches");

        Assert.NotEqual((HttpStatusCode)402, response.StatusCode);
    }

    [Fact]
    public async Task PostBillingPlan_TrialExpired_IsExemptAndSucceeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "trialblockexempt");
        await ExpireTrialAsync(org.OrganizationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        Guid planId;
        using (var scope = _factory.Services.CreateScope())
        {
            var planService = scope.ServiceProvider.GetRequiredService<PlanService>();
            var plan = await planService.CreatePlanAsync(new CreatePlanRequest("Escape", $"escape-{Guid.NewGuid():N}", 500m, null, null, null, false), Guid.NewGuid());
            planId = plan.Id;
        }

        var response = await client.PostAsJsonAsync("/api/v1/billing/plan", new { PlanId = planId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostBranch_StillWithinTrial_Succeeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "trialactive");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/branches");

        Assert.NotEqual((HttpStatusCode)402, response.StatusCode);
    }
}
```

- [ ] **Step 4: Run to verify failure then success**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter TrialEnforcementMiddlewareTests`
Expected before Steps 1-2: `PostBranch_TrialExpired_Returns402` fails (no 402 returned). After: all 4 PASS.

- [ ] **Step 5: Full `Api.Tests` regression**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: all green — every existing write-endpoint test must still pass unchanged (they all run within an active, unexpired trial or a fully paid Active subscription, neither of which this middleware ever blocks).

- [ ] **Step 6: Commit**

```bash
git add src/Api/Middleware/TrialEnforcementMiddleware.cs src/Api/Program.cs tests/Api.Tests/TrialEnforcementMiddlewareTests.cs
git commit -m "feat(api): enforce write-blocking for organizations with an expired trial"
```

---

## Task 6: SuperAdmin manual plan assignment

**Files:**
- Modify: `src/Modules/Platform/Application/PlatformDashboardService.cs`
- Modify: `src/Modules/Platform/Presentation/PlatformOrganizationsEndpoints.cs`
- Modify: `tests/Platform.Tests/PlatformDashboardServiceTests.cs`
- Modify: `tests/Api.Tests/PlatformOrganizationsEndpointsTests.cs` (already exists — has `LoginAsSuperAdminAsync(HttpClient client) : Task<string>` and registers organizations inline per test; add to it, do not create a new file)

**Interfaces:**
- Consumes: `SubscriptionService.AssignPlanAsync`, `IBillingLookupService.GetSubscriptionStatusAsync` (Task 1).
- Produces route: `POST /api/v1/platform/organizations/{id:guid}/plan`.
- Changes: `Courier.Platform.Application.OrganizationDetail` record gains `SubscriptionStatus? SubscriptionStatus` and `DateTimeOffset? TrialEndsAt` members.

- [ ] **Step 1: Enrich `OrganizationDetail` with subscription status**

In `src/Modules/Platform/Application/PlatformDashboardService.cs`, change the record and `GetOrganizationDetailAsync`:

```csharp
public record OrganizationDetail(OrganizationRecord Organization, string? PlanName, Courier.Billing.Domain.SubscriptionStatus? SubscriptionStatus, DateTimeOffset? TrialEndsAt, List<OrganizationUserRecord> Users, List<BranchAdminRecord> Branches, List<InvoiceResponse> Invoices, List<AuditLogRecord> RecentAuditEntries);
```

```csharp
    public async Task<OrganizationDetail?> GetOrganizationDetailAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await _organizationLookupService.GetAsync(organizationId, cancellationToken);
        if (organization is null)
        {
            return null;
        }

        var subscriptionStatus = await _billingLookupService.GetSubscriptionStatusAsync(organizationId, cancellationToken);
        var users = await _organizationLookupService.ListUsersAsync(organizationId, cancellationToken);
        var branches = await _branchLookupService.ListForOrganizationAsync(organizationId, cancellationToken);
        var invoices = await _billingLookupService.ListInvoicesForOrganizationAsync(organizationId, cancellationToken);
        var auditEntries = await _auditLogReader.ListForOrganizationAsync(organizationId, 50, cancellationToken);

        return new OrganizationDetail(organization, subscriptionStatus?.PlanName, subscriptionStatus?.Status, subscriptionStatus?.TrialEndsAt, users, branches, invoices, auditEntries);
    }
```

- [ ] **Step 2: Update `PlatformDashboardServiceTests.cs`'s fake and any `OrganizationDetail` construction**

Search `tests/Platform.Tests/PlatformDashboardServiceTests.cs` for any positional construction of `OrganizationDetail` (there may be none, since the only existing test for this method — `GetOrganizationDetailAsync_UnknownOrganization_ReturnsNull` — only asserts a null return). Confirm this and make no further change here beyond what Task 1 Step 8 already added to this file's `FakeBillingLookupService`.

- [ ] **Step 3: Add the SuperAdmin assign-plan endpoint**

In `src/Modules/Platform/Presentation/PlatformOrganizationsEndpoints.cs`, add the `Courier.Billing.Application` import and a new request record plus route:

```csharp
using Courier.Billing.Application;
using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public record AssignOrganizationPlanRequest(Guid PlanId);

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

        // Platform already fully references Billing directly (see PlatformPlansEndpoints's own
        // comment) — no adapter/interface needed here, unlike Identity's registration-time wiring.
        // This is what fixes an already-registered organization sitting with no Subscription at
        // all (pre-dating this feature) or one whose trial expired without converting.
        group.MapPost("/{id:guid}/plan", async (Guid id, AssignOrganizationPlanRequest request, SubscriptionService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.AssignPlanAsync(id, request.PlanId, httpContext.User.GetUserId()!.Value, cancellationToken);
                return Results.Ok(result);
            }
            catch (BillingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "BILLING_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireSuperAdmin();
    }
}
```

- [ ] **Step 4: Write the failing tests in `tests/Api.Tests/PlatformOrganizationsEndpointsTests.cs`**

Add `using Courier.Billing.Application;`, `using Microsoft.Extensions.DependencyInjection;`, and `using System.Net.Http.Headers;` to this file's existing `using` block if not already present, then add:

```csharp
    [Fact]
    public async Task AssignPlan_AsSuperAdmin_UpdatesOrganizationSubscription()
    {
        var client = _factory.CreateClient();
        var suffix = $"platform-assign-{Guid.NewGuid():N}"[..24];
        var registerRequest = new RegisterOrganizationRequest(
            $"Platform Assign Org {suffix}", $"pa-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner");
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var org = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;

        Guid planId;
        using (var scope = _factory.Services.CreateScope())
        {
            var planService = scope.ServiceProvider.GetRequiredService<PlanService>();
            var plan = await planService.CreatePlanAsync(new CreatePlanRequest("PlatformAssigned", $"platform-assigned-{Guid.NewGuid():N}", 750m, null, null, null, false), Guid.NewGuid());
            planId = plan.Id;
        }

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var response = await client.PostAsJsonAsync($"/api/v1/platform/organizations/{org.OrganizationId}/plan", new AssignOrganizationPlanRequest(planId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AssignPlan_AsRegularStaff_Returns403()
    {
        var client = _factory.CreateClient();
        var suffix = $"platform-assign-forbidden-{Guid.NewGuid():N}"[..24];
        var registerRequest = new RegisterOrganizationRequest(
            $"Platform Assign Forbidden Org {suffix}", $"paf-{suffix}", $"owner-{suffix}@example.test", "P@ssw0rd123!", "Owner");
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var org = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.PostAsJsonAsync($"/api/v1/platform/organizations/{org.OrganizationId}/plan", new AssignOrganizationPlanRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
```

- [ ] **Step 5: Run to verify failure then success**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "AssignPlan_AsSuperAdmin_UpdatesOrganizationSubscription|AssignPlan_AsRegularStaff_Returns403"`
Expected before Step 3: 404 for both. After: PASS.

- [ ] **Step 6: Full `Platform.Tests` and `Api.Tests` regression**

Run: `dotnet test tests/Platform.Tests/Platform.Tests.csproj && dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: all green.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Platform tests/Platform.Tests tests/Api.Tests
git commit -m "feat(platform): let a SuperAdmin assign or override an organization's plan"
```

---

## Task 7: Frontend — trial banner and self-serve plan picker on `/billing`

**Files:**
- Modify: `src/web/src/lib/api/clientFetch.ts`
- Modify: `src/web/src/lib/queryClient.ts`
- Modify: `src/web/src/features/billing/types.ts`
- Create: `src/web/src/features/billing/hooks/useSubscriptionStatus.ts`
- Create: `src/web/src/features/billing/hooks/useAvailablePlans.ts`
- Create: `src/web/src/features/billing/hooks/useAssignPlan.ts`
- Create: `src/web/src/app/api/billing/subscription/route.ts`
- Create: `src/web/src/app/api/billing/plans/route.ts`
- Modify: `src/web/src/app/api/billing/plan/route.ts`
- Modify: `src/web/src/app/(staff)/billing/page.tsx`
- Modify: `src/web/src/lib/api/clientFetch.test.ts`
- Delete: `src/web/src/features/billing/hooks/useBillingPlan.ts` (superseded by `useSubscriptionStatus` — Step 8 rewrites `page.tsx` to stop importing it, and it has no other consumer in the codebase, so it becomes dead code once that lands)

**Interfaces:**
- Consumes: `GET /api/v1/billing/subscription`, `GET /api/v1/billing/plans`, `POST /api/v1/billing/plan` (Task 3).
- Produces: `TrialExpiredError` (thrown by `clientFetch` on a `402` response), `useSubscriptionStatus()`, `useAvailablePlans()`, `useAssignPlan()`.

- [ ] **Step 1: Add `TrialExpiredError` to `clientFetch.ts`**

```typescript
export class SessionExpiredError extends Error {
  constructor() {
    super("Session expired");
    this.name = "SessionExpiredError";
  }
}

export class TrialExpiredError extends Error {
  constructor() {
    super("Trial expired");
    this.name = "TrialExpiredError";
  }
}

export async function clientFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init);
  if (response.status === 401) {
    throw new SessionExpiredError();
  }
  if (response.status === 402) {
    throw new TrialExpiredError();
  }
  if (!response.ok) {
    let bodyText: string | null = null;
    try {
      const body = await response.json();
      if (body !== null) {
        bodyText = JSON.stringify(body);
      }
    } catch {
      // body wasn't JSON — fall back to the generic message below
    }
    throw new Error(bodyText ?? `Request to ${path} failed with ${response.status}`);
  }
  return response.json();
}
```

- [ ] **Step 2: Write the failing `clientFetch.test.ts` case**

Open `src/web/src/lib/api/clientFetch.test.ts` first and match its existing test style for the `401`/`SessionExpiredError` case exactly (same mocking approach, same assertion style), adding a parallel case:

```typescript
it("throws TrialExpiredError on a 402 response", async () => {
  // mirror this file's existing 401/SessionExpiredError test's fetch mock setup exactly,
  // just returning status: 402 instead of 401
  await expect(clientFetch("/api/some-endpoint")).rejects.toThrow(TrialExpiredError);
});
```

- [ ] **Step 3: Run it to verify it fails, then passes**

Run: `npm --prefix src/web test -- clientFetch`
Expected before Step 1: FAIL. After: PASS, and every pre-existing case in this file (including the `401` case) still passes.

- [ ] **Step 4: Wire the redirect in `queryClient.ts`**

```typescript
import { QueryCache, MutationCache, QueryClient } from "@tanstack/react-query";
import { SessionExpiredError, TrialExpiredError } from "@/lib/api/clientFetch";
import { useAuthStore } from "@/lib/store/authStore";

function redirectToBillingOnTrialExpired(error: unknown) {
  if (error instanceof TrialExpiredError && typeof window !== "undefined" && window.location.pathname !== "/billing") {
    window.location.href = "/billing";
  }
}

export const queryClient = new QueryClient({
  queryCache: new QueryCache({
    onError: (error) => {
      if (error instanceof SessionExpiredError) {
        useAuthStore.getState().showReauthDialog();
      }
      redirectToBillingOnTrialExpired(error);
    },
  }),
  mutationCache: new MutationCache({
    onError: redirectToBillingOnTrialExpired,
  }),
});
```

- [ ] **Step 5: Add the new status/plan types**

In `src/web/src/features/billing/types.ts`, widen `PlanSummary.status` and add the two new types (leave `Invoice`/`GatewayCheckoutInfo` untouched):

```typescript
export interface PlanSummary {
  planName: string;
  monthlyPriceNpr: number;
  status: "Active" | "PastDue" | "Cancelled" | "Trial" | "TrialExpired";
}

export interface SubscriptionStatusInfo {
  status: "Active" | "PastDue" | "Cancelled" | "Trial" | "TrialExpired";
  trialEndsAt: string | null;
  planName: string | null;
  monthlyPriceNpr: number | null;
}

export interface AvailablePlan {
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

- [ ] **Step 6: Add the three BFF routes**

`src/web/src/app/api/billing/subscription/route.ts` (new):

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/billing/subscription");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/billing/plans/route.ts` (new):

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/billing/plans");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/billing/plan/route.ts` (modify — add `POST` alongside the existing `GET`):

```typescript
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

export async function POST(request: Request) {
  try {
    const payload = await request.json();
    const response = await apiFetch("/api/v1/billing/plan", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 7: Add the three hooks**

`src/web/src/features/billing/hooks/useSubscriptionStatus.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { SubscriptionStatusInfo } from "@/features/billing/types";

export function useSubscriptionStatus() {
  return useQuery({
    queryKey: ["billing-subscription"],
    queryFn: () => clientFetch<SubscriptionStatusInfo>("/api/billing/subscription"),
  });
}
```

`src/web/src/features/billing/hooks/useAvailablePlans.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { AvailablePlan } from "@/features/billing/types";

export function useAvailablePlans() {
  return useQuery({
    queryKey: ["billing-available-plans"],
    queryFn: () => clientFetch<AvailablePlan[]>("/api/billing/plans"),
  });
}
```

`src/web/src/features/billing/hooks/useAssignPlan.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useAssignPlan() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (planId: string) =>
      clientFetch("/api/billing/plan", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ planId }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["billing-subscription"] });
      queryClient.invalidateQueries({ queryKey: ["billing-plan"] });
      queryClient.invalidateQueries({ queryKey: ["billing-invoices"] });
    },
  });
}
```

- [ ] **Step 8: Update the `/billing` page**

Modify `src/web/src/app/(staff)/billing/page.tsx` — add the trial banner and plan picker above the existing invoices table, replacing the current plan-line paragraph:

```tsx
"use client";

import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { FieldError } from "@/components/ui/field-error";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useBillingInvoices } from "@/features/billing/hooks/useBillingInvoices";
import { usePayInvoice } from "@/features/billing/hooks/usePayInvoice";
import { useSubscriptionStatus } from "@/features/billing/hooks/useSubscriptionStatus";
import { useAvailablePlans } from "@/features/billing/hooks/useAvailablePlans";
import { useAssignPlan } from "@/features/billing/hooks/useAssignPlan";
import { redirectToGateway } from "@/features/billing/GatewayRedirectForm";
import type { Invoice } from "@/features/billing/types";

function daysLeft(trialEndsAt: string): number {
  const ms = new Date(trialEndsAt).getTime() - Date.now();
  return Math.max(0, Math.ceil(ms / (1000 * 60 * 60 * 24)));
}

function PlanPicker() {
  const { data: plans, isLoading } = useAvailablePlans();
  const assignPlan = useAssignPlan();
  const [assignError, setAssignError] = useState<string | null>(null);

  const choose = async (planId: string) => {
    setAssignError(null);
    try {
      await assignPlan.mutateAsync(planId);
    } catch {
      setAssignError("Couldn't switch to that plan. Please try again.");
    }
  };

  if (isLoading) {
    return <Spinner className="h-5 w-5" />;
  }

  return (
    <div className="space-y-3">
      {assignError && <FieldError id="assign-plan-error">{assignError}</FieldError>}
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {plans?.map((plan) => (
          <div key={plan.id} className="rounded-lg border p-4">
            <div className="flex items-center gap-2">
              <h3 className="font-semibold">{plan.name}</h3>
              {plan.isDefault && <Badge variant="secondary">Recommended</Badge>}
            </div>
            <p className="mt-1 text-sm text-muted-foreground">NPR {plan.monthlyPriceNpr}/month</p>
            <Button
              size="sm"
              className="mt-3"
              disabled={assignPlan.isPending}
              onClick={() => choose(plan.id)}
            >
              Choose this plan
            </Button>
          </div>
        ))}
      </div>
    </div>
  );
}

export default function BillingPage() {
  const { data: subscription, isLoading: subscriptionLoading } = useSubscriptionStatus();
  const { data: invoices, isLoading: invoicesLoading, isError, refetch } = useBillingInvoices();
  const payInvoice = usePayInvoice();
  const [payError, setPayError] = useState<string | null>(null);

  const pay = async (invoiceId: string, gateway: "Esewa" | "Khalti") => {
    setPayError(null);
    try {
      const checkoutInfo = await payInvoice.mutateAsync({ invoiceId, gateway });
      redirectToGateway(checkoutInfo);
    } catch {
      setPayError(`Couldn't start the ${gateway} checkout. Please try again.`);
    }
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

  if (subscriptionLoading || invoicesLoading) {
    return <div className="flex items-center justify-center p-12"><Spinner className="h-6 w-6" /></div>;
  }
  if (isError) {
    return <ErrorState title="Couldn't load billing" description="Something went wrong fetching your billing details." onRetry={() => refetch()} />;
  }

  const needsPlan = subscription?.status === "Trial" || subscription?.status === "TrialExpired";

  return (
    <div className="space-y-6">
      <div>
        <h1 className="font-display text-xl font-semibold">Billing</h1>
        {subscription?.status === "Active" && (
          <p className="mt-1 text-sm text-muted-foreground">Current plan: {subscription.planName} — NPR {subscription.monthlyPriceNpr}/month (Active)</p>
        )}
        {subscription?.status === "Trial" && subscription.trialEndsAt && (
          <p className="mt-1 text-sm text-muted-foreground">
            You're on a free trial — {daysLeft(subscription.trialEndsAt)} day{daysLeft(subscription.trialEndsAt) === 1 ? "" : "s"} left. Choose a plan below to keep going after it ends.
          </p>
        )}
        {subscription?.status === "TrialExpired" && (
          <p className="mt-1 text-sm text-destructive">Your trial has ended. Choose a plan below to continue using the app.</p>
        )}
        {(subscription?.status === "PastDue" || subscription?.status === "Cancelled") && (
          <p className="mt-1 text-sm text-muted-foreground">Current plan: {subscription.planName} ({subscription.status})</p>
        )}
      </div>

      {needsPlan && <PlanPicker />}

      {payError && <FieldError id="billing-pay-error">{payError}</FieldError>}
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

Delete `src/web/src/features/billing/hooks/useBillingPlan.ts` now that this page no longer imports it — confirm first with `grep -rl "useBillingPlan" src/web/src` that the only remaining match is the file itself (no other consumer), then remove it.

- [ ] **Step 9: Run the frontend test suite**

Run: `npm --prefix src/web test`
Expected: all green, including `clientFetch.test.ts` and any existing `BillingPage`-adjacent tests. If a snapshot or query-key-based test for the old plan-line paragraph exists and fails, update its expected text to match the new conditional rendering above — do not change the component to match a stale test.

- [ ] **Step 10: Type-check and lint**

Run: `npm --prefix src/web run build` (or the project's existing `tsc --noEmit && next lint` combo, per `docs/FRONTEND-ROADMAP.md`'s established verification commands — check `package.json` scripts if unsure)
Expected: clean.

- [ ] **Step 11: Manual verification**

Start the API (`dotnet run --project src/Api/Api.csproj`) and the frontend dev server (`npm --prefix src/web run dev`), register a fresh organization through the real UI, and confirm the `/billing` page shows the trial banner and a plan picker with at least one seeded Plan (create one first via `/platform/plans` logged in as SuperAdmin if none exist). Choose a plan and confirm the banner switches to "Current plan: ... (Active)".

- [ ] **Step 12: Commit**

```bash
git add src/web/src/lib/api/clientFetch.ts src/web/src/lib/api/clientFetch.test.ts src/web/src/lib/queryClient.ts src/web/src/features/billing src/web/src/app/api/billing "src/web/src/app/(staff)/billing/page.tsx"
git commit -m "feat(web): show trial status and self-serve plan picker on Billing page"
```

---

## Task 8: Frontend — SuperAdmin "Assign Plan" on the organization detail Sheet

**Files:**
- Create: `src/web/src/app/api/platform/organizations/[id]/plan/route.ts`
- Modify: `src/web/src/features/platform-organizations/hooks/useOrganizationLifecycleActions.ts` (or create a sibling hook file — match whichever this repo's existing convention favors; `useOrganizationLifecycleActions.ts` already groups suspend/reactivate/delete/restore as one file's exports, so add this mutation there too for consistency)
- Modify: `src/web/src/features/platform-organizations/OrganizationDetailSheet.tsx`
- Modify: `src/web/src/features/platform-organizations/types.ts`
- Modify: `src/web/src/features/platform-organizations/OrganizationDetailSheet.test.tsx`

**Interfaces:**
- Consumes: `POST /api/v1/platform/organizations/{id}/plan` (Task 6), `usePlatformPlans()` (existing, `src/web/src/features/platform-plans/hooks/usePlatformPlans.ts`).
- Produces: `useAssignOrganizationPlan()`.

- [ ] **Step 1: Add the BFF route**

```typescript
import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function POST(request: Request, { params }: { params: { id: string } }) {
  try {
    const payload = await request.json();
    const response = await platformApiFetch(`/api/v1/platform/organizations/${params.id}/plan`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 2: Add `subscriptionStatus`/`trialEndsAt` to the frontend's `OrganizationDetail` type**

In `src/web/src/features/platform-organizations/types.ts`, change the existing `OrganizationDetail` interface (leave every other interface in this file untouched):

```typescript
export interface OrganizationDetail {
  organization: {
    id: string;
    name: string;
    slug: string;
    isActive: boolean;
    suspendedBySystem: boolean;
    deletedAt: string | null;
    createdAt: string;
  };
  planName: string | null;
  subscriptionStatus: "Active" | "PastDue" | "Cancelled" | "Trial" | "TrialExpired" | null;
  trialEndsAt: string | null;
  users: OrganizationUser[];
  branches: OrganizationBranch[];
  invoices: OrganizationInvoice[];
  recentAuditEntries: OrganizationAuditEntry[];
}
```

- [ ] **Step 3: Add the mutation hook**

In `src/web/src/features/platform-organizations/hooks/useOrganizationLifecycleActions.ts`, add alongside the existing exports:

```typescript
export function useAssignOrganizationPlan() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ organizationId, planId }: { organizationId: string; planId: string }) =>
      clientFetch(`/api/platform/organizations/${organizationId}/plan`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ planId }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["platform-organizations"] });
    },
  });
}
```

- [ ] **Step 4: Add the "Assign Plan" control to `OrganizationDetailSheet.tsx`**

Replace the existing status/plan-name line (currently):

```tsx
            <div className="flex items-center gap-2">
              <Badge
                variant={data.organization.deletedAt ? "destructive" : data.organization.isActive ? "default" : "secondary"}
              >
                {data.organization.deletedAt ? "Deleted" : data.organization.isActive ? "Active" : "Suspended"}
              </Badge>
              <span className="text-sm text-muted-foreground">{data.planName ?? "No plan assigned"}</span>
            </div>
```

with:

```tsx
            <div className="flex flex-wrap items-center gap-2">
              <Badge
                variant={data.organization.deletedAt ? "destructive" : data.organization.isActive ? "default" : "secondary"}
              >
                {data.organization.deletedAt ? "Deleted" : data.organization.isActive ? "Active" : "Suspended"}
              </Badge>
              <span className="text-sm text-muted-foreground">
                {data.planName ?? (data.subscriptionStatus === "Trial" ? "On trial — no plan chosen" : data.subscriptionStatus === "TrialExpired" ? "Trial expired — no plan chosen" : "No plan assigned")}
              </span>
            </div>

            <AssignPlanControl organizationId={organizationId} />
```

Add the import at the top of the file:

```tsx
import { useState } from "react";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { usePlatformPlans } from "@/features/platform-plans/hooks/usePlatformPlans";
import { useAssignOrganizationPlan } from "@/features/platform-organizations/hooks/useOrganizationLifecycleActions";
```

(Add `useState`/the two new hook imports alongside this file's existing `"use client"`/`useRouter` import block — do not duplicate an existing import line.)

Add this small component in the same file, right before `export function OrganizationDetailSheet()`:

```tsx
function AssignPlanControl({ organizationId }: { organizationId: string | null }) {
  const { data: plans } = usePlatformPlans();
  const assignPlan = useAssignOrganizationPlan();
  const [selectedPlanId, setSelectedPlanId] = useState<string>("");

  if (!organizationId) {
    return null;
  }

  return (
    <div className="flex items-center gap-2">
      <Select value={selectedPlanId} onValueChange={setSelectedPlanId}>
        <SelectTrigger className="w-48">
          <SelectValue placeholder="Assign a plan" />
        </SelectTrigger>
        <SelectContent>
          {plans?.filter((p) => p.isActive).map((plan) => (
            <SelectItem key={plan.id} value={plan.id}>{plan.name} — NPR {plan.monthlyPriceNpr}/mo</SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Button
        size="sm"
        disabled={!selectedPlanId || assignPlan.isPending}
        onClick={() => assignPlan.mutate({ organizationId, planId: selectedPlanId })}
      >
        Assign Plan
      </Button>
    </div>
  );
}
```

- [ ] **Step 5: Update `OrganizationDetailSheet.test.tsx`**

In `src/web/src/features/platform-organizations/OrganizationDetailSheet.test.tsx`, add `subscriptionStatus: "Active" as const` and `trialEndsAt: null` to the `makeDetail()` helper's returned object (right after its existing `planName: "Growth",` line):

```tsx
function makeDetail(overrides: Partial<{ isActive: boolean; deletedAt: string | null }> = {}) {
  return {
    organization: {
      id: "org-1",
      name: "Kathmandu Couriers",
      slug: "kathmandu-couriers",
      isActive: overrides.isActive ?? true,
      suspendedBySystem: false,
      deletedAt: overrides.deletedAt ?? null,
      createdAt: "2026-01-01T00:00:00Z",
    },
    planName: "Growth",
    subscriptionStatus: "Active" as const,
    trialEndsAt: null,
    users: [{ id: "u1", email: "owner@kathmandu.example", fullName: "Owner Person", isActive: true }],
    branches: [{ id: "b1", code: "KTM-01", name: "Kathmandu Hub", isActive: true }],
    invoices: [
      {
        id: "i1",
        planName: "Growth",
        amountNpr: 5000,
        cycleStart: "2026-08-01T00:00:00Z",
        cycleEnd: "2026-08-31T00:00:00Z",
        status: "Paid" as const,
        dueAt: "2026-08-05T00:00:00Z",
        paidAt: "2026-08-02T00:00:00Z",
      },
    ],
    recentAuditEntries: [
      {
        id: "a1",
        action: "organization.suspended",
        entityType: "Organization",
        entityId: "org-1",
        userId: "u1",
        impersonatedByUserId: null,
        isCrossTenantAction: false,
        timestamp: "2026-09-01T00:00:00Z",
      },
    ],
  };
}
```

Add a mock for the two new hooks this component now uses, alongside the file's existing `vi.mock` calls:

```tsx
const platformPlans = [{ id: "plan-1", name: "Growth", slug: "growth", monthlyPriceNpr: 3000, maxBranches: null, maxStaffUsers: null, maxMonthlyShipments: null, isDefault: true, isActive: true }];
vi.mock("@/features/platform-plans/hooks/usePlatformPlans", () => ({
  usePlatformPlans: () => ({ data: platformPlans, isLoading: false }),
}));

const assignPlanMutate = vi.fn();
vi.mock("@/features/platform-organizations/hooks/useOrganizationLifecycleActions", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/platform-organizations/hooks/useOrganizationLifecycleActions")>();
  return {
    ...actual,
    useSuspendOrganization: () => ({ mutate: suspendMutate, isPending: false }),
    useReactivateOrganization: () => ({ mutate: reactivateMutate, isPending: false }),
    useDeleteOrganization: () => ({ mutate: deleteMutate, isPending: false }),
    useRestoreOrganization: () => ({ mutate: restoreMutate, isPending: false }),
    useAssignOrganizationPlan: () => ({ mutate: assignPlanMutate, isPending: false }),
  };
});
```

This replaces the file's existing `vi.mock("@/features/platform-organizations/hooks/useOrganizationLifecycleActions", ...)` block (which previously returned only the four lifecycle hooks with no `importOriginal` wrapper) — the `importOriginal` form is only needed here because this module now exports a 5th hook (`useAssignOrganizationPlan`, added in Step 3) that the mock must also provide; without it, importing the real module's other unmocked exports would fail. Then add one new test at the end of the `describe` block, before its closing `});`:

```tsx
  it("shows an Assign Plan control and calls the mutation with the selected plan", async () => {
    assignPlanMutate.mockClear();
    usePlatformOrganization.mockReturnValue({ data: makeDetail(), isLoading: false, isError: false, refetch: vi.fn() });
    render(<OrganizationDetailSheet />);

    expect(screen.getByText(/Assign a plan/i)).toBeInTheDocument();
  });
```

(A full select-and-click interaction test for shadcn's `Select` requires simulating Radix's portal-based listbox — keep this first test to confirming the control renders; a deeper interaction test can follow the same pattern this repo's other shadcn `Select` usages already use for testing, if any exist — check `grep -rl "SelectTrigger" src/web/src/features/*/*.test.tsx` for a precedent before deciding whether to add one.)

- [ ] **Step 6: Run the frontend test suite**

Run: `npm --prefix src/web test`
Expected: all green, including the updated `OrganizationDetailSheet.test.tsx`.

- [ ] **Step 7: Type-check**

Run: `npm --prefix src/web run build`
Expected: clean — in particular, confirm the `OrganizationDetailResponse`-equivalent type from Step 2 matches every field `PlatformDashboardService.GetOrganizationDetailAsync` (Task 6) now actually returns.

- [ ] **Step 8: Manual verification**

As a SuperAdmin, open an organization's detail Sheet in `/platform/organizations`, confirm the plan line reflects "On trial — no plan chosen" for a freshly registered org, assign a plan from the new control, and confirm the Sheet's plan line updates after the mutation succeeds.

- [ ] **Step 9: Commit**

```bash
git add src/web/src/app/api/platform/organizations src/web/src/features/platform-organizations
git commit -m "feat(web): let a SuperAdmin assign a plan from the organization detail Sheet"
```

---

## Task 9: Documentation and full regression

**Files:**
- Modify: `docs/PLATFORM-ROADMAP.md`
- Modify: `docs/CLARIFICATIONS.md`

**Interfaces:** none — documentation and verification only.

- [ ] **Step 1: Close Known Limitation #1 in `PLATFORM-ROADMAP.md`**

In `docs/PLATFORM-ROADMAP.md`, under "## Known Limitations", change item 1's status. Keep the original text as history but append:

```markdown
   **Update 2026-09-10 — closed.** See
   `docs/superpowers/specs/2026-09-10-billing-trial-plan-assignment-design.md` and
   `docs/superpowers/plans/2026-09-10-billing-trial-plan-assignment.md`. Every self-registered
   organization now starts a 7-day trial (`SubscriptionStatus.Trial`, no `Plan` yet) via
   `AuthService.RegisterOrganizationAsync` → `IOrganizationSubscriptionStarter` →
   `SubscriptionService.StartTrialAsync`. An `OrganizationOwner` can self-serve pick a `Plan`
   from `/billing` (`GET /api/v1/billing/plans`, `POST /api/v1/billing/plan`) at any time,
   before or after the trial ends. `TrialExpiryJob` flips an unconverted trial to
   `TrialExpired`, at which point `TrialEnforcementMiddleware` blocks non-GET requests with
   `402 TRIAL_EXPIRED` until a `Plan` is chosen — backend-enforced, not a frontend-only gate. A
   `SuperAdmin` can also assign/override any org's `Plan` directly from
   `/platform/organizations` (`POST /api/v1/platform/organizations/{id}/plan`), which is the
   path available for the organizations that pre-date this feature and have no `Subscription`
   at all (deliberately not backfilled — see the design spec's Open Questions for why).
```

- [ ] **Step 2: Add a `CLARIFICATIONS.md` entry**

Append a new numbered entry (use the next available number — check the file's current highest entry number first) documenting: the trial model chosen (7 days, no Plan required, soft-lock enforcement via middleware rather than the existing hard-lock suspension mechanism, since an owner must still be able to log in to pick a plan), and the decision not to backfill already-registered organizations into a trial state.

- [ ] **Step 3: Full backend regression**

Run: `dotnet build Courier.slnx && dotnet test Courier.slnx`
Expected: all green across every test project — `Billing.Tests`, `Identity.Tests`, `Branches.Tests`, `Platform.Tests`, `Api.Tests`, and every other module's test project unaffected by this plan.

- [ ] **Step 4: Full frontend regression**

Run: `npm --prefix src/web test && npm --prefix src/web run build`
Expected: all green, production build succeeds.

- [ ] **Step 5: Commit**

```bash
git add docs/PLATFORM-ROADMAP.md docs/CLARIFICATIONS.md
git commit -m "docs: close Platform Known Limitation #1 (trial-based plan assignment)"
```
