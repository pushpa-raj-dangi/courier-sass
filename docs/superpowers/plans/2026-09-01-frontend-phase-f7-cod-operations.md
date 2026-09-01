# Frontend Phase F7 — COD Operations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the staff back-office UI for the full COD lifecycle (CLAUDE.md §22–23), closing 4 real backend gaps found during design (3 unconverted enums, no list endpoints for rider/branch settlements or adjustments, no guard against approving an adjustment against an already-Settled MerchantSettlement).

**Architecture:** 6 backend tasks fix enum serialization, add three new paginated list endpoints to the existing `Cod` module's `CodService.Reads.cs`/`CodEndpoints.cs`, and close a real financial correctness gap in `ApproveAdjustmentAsync`. 9 frontend tasks build `src/web/src/features/cod/` following the exact conventions F2–F6 already established (Sheet-over-pages via a single Zustand panel store, TanStack Query hooks per endpoint, Next.js Route Handlers as a thin BFF proxy, one tab per COD sub-workflow under the `/cod` route that already exists in `nav-main.tsx`).

**Tech Stack:** ASP.NET Core / EF Core / xUnit (backend); Next.js 14 App Router, TanStack Query, Zustand, React Hook Form + Zod, Vitest + Testing Library (frontend). No new npm/NuGet packages — the tab switcher is a plain button row (no `@radix-ui/react-tabs` dependency exists in this repo and none is needed), and date-range filters use plain `<Input type="date">` (no calendar/popover library exists either).

**Spec:** `docs/superpowers/specs/2026-09-01-frontend-phase-f7-cod-operations-design.md`

## Global Constraints

- Detail/create UI is always a right-side `Sheet` driven by a Zustand store, never a routed page (`docs/ARCHITECTURE.md` §9a). One shared `codPanelStore` covers all 5 sheet kinds this phase needs (collection detail, rider-settlement create/detail, branch-settlement create/detail, merchant-settlement create/detail, adjustment detail) — only one sheet is ever open at a time on the `/cod` page.
- Price, COD amount, permissions, and branch scope are never trusted from the frontend — every new screen only reads/writes through the existing permission-checked backend endpoints (CLAUDE.md §33).
- Every new screen gets loading/error/empty states using the existing `LoadingState`/`ErrorState`/`EmptyState`/`Spinner` primitives — no ad hoc spinners.
- All 3 Cod enums (`CodCollectionStatus`, `MerchantSettlementStatus`, `FinancialTransactionType`) serialize as PascalCase strings once Task 1 lands — every later task's TypeScript types use string literal unions, never numbers.
- `GET /api/v1/cod/collections`, the new `GET /api/v1/cod/rider-settlements`, `GET /api/v1/cod/branch-settlements`, and `GET /api/v1/cod/adjustments` are all paginated (`PagedResult<T>`, matches `@/components/data-table/DataTable`'s `PagedResult<T>` exactly — `items`/`page`/`pageSize`/`totalCount`/`totalPages`, camelCase). `GET /api/v1/cod/merchant-settlements` is a plain, non-paginated array (matches the existing `ListMerchantSettlementsAsync` return type, unchanged this phase) — don't build pagination UI for the Merchant Settlements tab.
- `cod.read` gates every list/detail view; `cod.reconcile` gates creating a rider or branch settlement and finance-verifying a branch settlement; `cod.settle` gates creating/settling a merchant settlement; `cod.adjust` gates requesting/approving an adjustment. Gate with the existing `usePermission(permission)` hook, same as every other feature.
- The `/cod` sidebar entry already exists in `src/web/src/components/nav-main.tsx:34` (`{ href: "/cod", label: "COD", permission: "cod.read", icon: Wallet }`) — no nav change needed, only the `/cod` page itself.
- Backend test command: `dotnet test tests/Cod.Tests/Cod.Tests.csproj` (unit) and `dotnet test tests/Api.Tests/Api.Tests.csproj --filter CodEndpointsTests` (integration, needs the local Postgres container per `[[Courier Local Postgres]]` — already running on port 5434, do not start a second one). Frontend: `cd src/web && npm test` (Vitest) and `npm run build` (production build/type-check) after every frontend task.

---

## Task 1: Fix Cod enum JSON serialization; record the gaps in CLARIFICATIONS.md

**Files:**
- Modify: `src/Modules/Cod/Domain/CodCollectionStatus.cs`
- Modify: `src/Modules/Cod/Domain/MerchantSettlementStatus.cs`
- Modify: `src/Modules/Cod/Domain/FinancialTransactionType.cs`
- Modify: `docs/CLARIFICATIONS.md`
- Test: `tests/Cod.Tests/CodEnumSerializationTests.cs`

**Interfaces:**
- Produces: all 3 enums now carry `[JsonConverter(typeof(JsonStringEnumConverter))]`, matching `ShipmentStatus`/`ManifestStatus`/the 5 Riders enums/the 3 Ndr enums' existing fix. Every later task's DTOs using these enums serialize as strings.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using Courier.Cod.Domain;
using Xunit;

namespace Courier.Cod.Tests;

public class CodEnumSerializationTests
{
    [Fact]
    public void CodCollectionStatus_SerializesAsString() =>
        Assert.Equal("\"CodExpected\"", JsonSerializer.Serialize(CodCollectionStatus.CodExpected));

    [Fact]
    public void MerchantSettlementStatus_SerializesAsString() =>
        Assert.Equal("\"Settled\"", JsonSerializer.Serialize(MerchantSettlementStatus.Settled));

    [Fact]
    public void FinancialTransactionType_SerializesAsString() =>
        Assert.Equal("\"CodVerification\"", JsonSerializer.Serialize(FinancialTransactionType.CodVerification));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter CodEnumSerializationTests`
Expected: FAIL — each assertion gets `"0"`/`"1"` etc. instead of the quoted name.

- [ ] **Step 3: Add the converter to each enum**

`src/Modules/Cod/Domain/CodCollectionStatus.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Courier.Cod.Domain;

// COD-LIFECYCLE.md §1's full seven-state chain, all living on CODCollection.Status — this plan's
// resolved reading of COD-LIFECYCLE.md's prose ("the COD lifecycle status... lives on
// CODCollection, not on Shipment"), not the narrower Expected/Collected pair the entity's
// field-list table alone shows. Strictly forward-only; no step in this plan ever moves a
// CODCollection backward.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CodCollectionStatus
{
    CodExpected,
    CodCollected,
    RiderDeposited,
    BranchReconciled,
    FinanceVerified,
    MerchantSettlementCreated,
    Settled
}
```

`src/Modules/Cod/Domain/MerchantSettlementStatus.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Courier.Cod.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MerchantSettlementStatus
{
    Created,
    Settled
}
```

`src/Modules/Cod/Domain/FinancialTransactionType.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Courier.Cod.Domain;

// COD-LIFECYCLE.md §1's table links FinancialTransaction to exactly one step
// (BRANCH_RECONCILED -> FINANCE_VERIFIED); single member by design, not an oversight — easy to
// extend if a future phase gives FinancialTransaction a second real use.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FinancialTransactionType
{
    CodVerification
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter CodEnumSerializationTests`
Expected: PASS

- [ ] **Step 5: Record the gaps in CLARIFICATIONS.md**

Append a new entry after entry 16 (`## 16. Frontend Phase F6: ...`), before the `## Summary of schema additions implied by this document` section:

```markdown
## 17. Frontend Phase F7: `Cod` enums need JSON serialization, no list endpoints for rider/branch settlements or adjustments, and a missing settle-guard on adjustment approval

**Where:** `Cod.Domain.CodCollectionStatus`/`MerchantSettlementStatus`/`FinancialTransactionType`, `Cod.Application.CodService`, `Cod.Presentation.CodEndpoints`

**Gap 1 — no `JsonStringEnumConverter` on any of the 3 Cod enums.** Same latent bug class hit and
fixed in every prior phase (`BranchType` F2, `ShipmentStatus` F3, `ManifestPurpose`/`ManifestStatus`
F4, 5 Riders enums F5, 3 Ndr enums F6). Fixed proactively here for all 3 Cod enums at once.

**Gap 2 — no `GET` to list `RiderCashSettlement` or `BranchCashSettlement` rows.** Only `POST`
(create) existed for either, so a branch user had no way to see which rider settlements were still
unreconciled or which branch settlements were still unverified without already knowing an id —
exactly the gap Phase 10's final review flagged as making shortages/overages structurally
unreadable.

**Gap 3 — no `GET` to list `Adjustment` rows.** Only `POST /adjustments` (needs an `EntityId` the
caller already knows) and `POST /adjustments/{id}/approve` (needs an `Id` the caller already knows)
existed — an approver had no way to discover pending adjustments at all. Also flagged in Phase 10's
final review.

**Resolution (gaps 2–3):** `GET /api/v1/cod/rider-settlements`, `GET /api/v1/cod/branch-settlements`,
and `GET /api/v1/cod/adjustments` added, all paginated and `cod.read`-gated, matching
`ListCollectionsAsync`'s existing filter/branch-scoping shape.

**Gap 4 — `ApproveAdjustmentAsync` had no guard against approving against an already-`Settled`
`MerchantSettlement`.** It unconditionally rewrote `MerchantSettlementItem.OtherDeductions`/
`NetPayable` and recomputed `MerchantSettlement.TotalPayable` with no check on
`MerchantSettlement.Status`. If the parent settlement was already settled (merchant already paid
the old figure), approving a pending adjustment silently changed `TotalPayable` on a row that's
supposed to be closed, with no new payout to match — a real correctness bug against CLAUDE.md §23.

**Resolution (gap 4):** `ApproveAdjustmentAsync` now throws `CodValidationException` before opening
its transaction when the target `MerchantSettlement.Status == Settled`, mirroring
`SettleMerchantSettlementAsync`'s own status guard.

Discovered planning Frontend Phase F7 (`docs/FRONTEND-ROADMAP.md`,
`docs/superpowers/specs/2026-09-01-frontend-phase-f7-cod-operations-design.md`). Implemented as
Tasks 1–5 of the Frontend Phase F7 plan.

---
```

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Cod/Domain/CodCollectionStatus.cs src/Modules/Cod/Domain/MerchantSettlementStatus.cs src/Modules/Cod/Domain/FinancialTransactionType.cs docs/CLARIFICATIONS.md tests/Cod.Tests/CodEnumSerializationTests.cs
git commit -m "fix(cod): serialize CodCollectionStatus/MerchantSettlementStatus/FinancialTransactionType as strings"
```

---

## Task 2: `GET /api/v1/cod/rider-settlements`

**Files:**
- Modify: `src/Modules/Cod/Application/CodService.Reads.cs`
- Modify: `src/Modules/Cod/Presentation/CodEndpoints.cs`
- Test: `tests/Cod.Tests/CodServiceRiderSettlementsListTests.cs`

**Interfaces:**
- Consumes: `RiderCashSettlement` (Task 1's build, unchanged shape), `CodService.ToRiderSettlementResponse(RiderCashSettlement)` — existing `private static` method in `CodService.Settlements.cs`, callable from any partial-class file including `CodService.Reads.cs`. `PaginationDefaults.Normalize(int? page, int? pageSize)` — existing `SharedKernel` helper already used by `ListCollectionsAsync`.
- Produces: `CodService.ListRiderSettlementsAsync(Guid? branchId, Guid? riderId, bool? unreconciledOnly, int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken)` returning `Task<PagedResult<RiderCashSettlementResponse>>`. `GET /api/v1/cod/rider-settlements?branchId=&riderId=&unreconciledOnly=&page=&pageSize=`, `cod.read`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Events.Application;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Cod.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class RestrictedBranchAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid> _accessibleBranchIds;
    public RestrictedBranchAuthorizationChecker(params Guid[] accessibleBranchIds) => _accessibleBranchIds = accessibleBranchIds.ToHashSet();
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(_accessibleBranchIds);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessibleBranchIds.Contains(branchId));
}

file class AlwaysTrueBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class CodServiceRiderSettlementsListTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RiderCashSettlementConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker branchAuthorizationChecker) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        branchAuthorizationChecker, new AlwaysTrueMerchantLookup(), new NoopOutboxWriter());

    private static async Task<RiderCashSettlement> SeedAsync(TestDbContext dbContext, Guid organizationId, Guid riderId, Guid branchId, Guid? branchCashSettlementId = null)
    {
        var settlement = new RiderCashSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, RiderId = riderId, BranchId = branchId,
            DeclaredAmount = 2500m, ShipmentCount = 1, SettledAt = DateTimeOffset.UtcNow,
            ConfirmedByUserId = Guid.NewGuid(), BranchCashSettlementId = branchCashSettlementId
        };
        dbContext.Set<RiderCashSettlement>().Add(settlement);
        await dbContext.SaveChangesAsync();
        return settlement;
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_ExcludesForeignOrganization()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var own = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        await SeedAsync(dbContext, foreignOrganizationId, Guid.NewGuid(), Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListRiderSettlementsAsync(null, null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(own.Id, single.Id);
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_BranchRestricted_FiltersToAccessibleBranchOnly()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var accessibleBranchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        var accessible = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), accessibleBranchId);
        await SeedAsync(dbContext, organizationId, Guid.NewGuid(), otherBranchId);
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(accessibleBranchId));

        var result = await service.ListRiderSettlementsAsync(null, null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(accessible.Id, single.Id);
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_UnreconciledOnly_ExcludesRowsWithBranchCashSettlementId()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var unreconciled = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), branchId);
        await SeedAsync(dbContext, organizationId, Guid.NewGuid(), branchId, branchCashSettlementId: Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListRiderSettlementsAsync(null, null, true, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(unreconciled.Id, single.Id);
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_FilteredByRiderId_ReturnsOnlyMatching()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var targetRiderId = Guid.NewGuid();
        var target = await SeedAsync(dbContext, organizationId, targetRiderId, Guid.NewGuid());
        await SeedAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListRiderSettlementsAsync(null, targetRiderId, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(target.Id, single.Id);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter CodServiceRiderSettlementsListTests`
Expected: FAIL — `CodService` has no `ListRiderSettlementsAsync` member (compile error).

- [ ] **Step 3: Implement `ListRiderSettlementsAsync`**

Add to `src/Modules/Cod/Application/CodService.Reads.cs`, after the existing `using` block's methods (before the closing brace of the `partial class CodService`):

```csharp
    // Mirrors ListCollectionsAsync's shape exactly: RiderCashSettlement carries its own BranchId
    // (Task 6 of Phase 10's original plan), so branch-access scoping is a direct WHERE, no join.
    // unreconciledOnly drives the Branch Settlements create-Sheet's rider-settlement picker
    // (Frontend Phase F7).
    public async Task<PagedResult<RiderCashSettlementResponse>> ListRiderSettlementsAsync(
        Guid? branchId, Guid? riderId, bool? unreconciledOnly, int? page, int? pageSize,
        ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var query = _dbContext.Set<RiderCashSettlement>().Where(r => r.OrganizationId == organizationId);
        if (branchId is Guid requestedBranchId)
        {
            query = query.Where(r => r.BranchId == requestedBranchId);
        }
        if (riderId is Guid requestedRiderId)
        {
            query = query.Where(r => r.RiderId == requestedRiderId);
        }
        if (unreconciledOnly == true)
        {
            query = query.Where(r => r.BranchCashSettlementId == null);
        }

        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "cod.read", cancellationToken);
        if (accessibleBranchIds is not null)
        {
            var accessibleArray = accessibleBranchIds.ToArray();
            query = query.Where(r => accessibleArray.Contains(r.BranchId));
        }

        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var settlements = await query
            .OrderByDescending(r => r.SettledAt).ThenBy(r => r.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<RiderCashSettlementResponse>(settlements.Select(ToRiderSettlementResponse).ToList(), normalizedPage, normalizedPageSize, totalCount);
    }
```

- [ ] **Step 4: Wire the endpoint**

In `src/Modules/Cod/Presentation/CodEndpoints.cs`, insert immediately before the existing `group.MapPost("/rider-settlements", ...)` line:

```csharp
        group.MapGet("/rider-settlements", async (Guid? branchId, Guid? riderId, bool? unreconciledOnly, int? page, int? pageSize, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListRiderSettlementsAsync(branchId, riderId, unreconciledOnly, page, pageSize, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");

```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter CodServiceRiderSettlementsListTests`
Expected: PASS (4 tests)

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Cod/Application/CodService.Reads.cs src/Modules/Cod/Presentation/CodEndpoints.cs tests/Cod.Tests/CodServiceRiderSettlementsListTests.cs
git commit -m "feat(cod): add GET /api/v1/cod/rider-settlements list endpoint"
```

---

## Task 3: `GET /api/v1/cod/branch-settlements`

**Files:**
- Modify: `src/Modules/Cod/Application/CodService.Reads.cs`
- Modify: `src/Modules/Cod/Presentation/CodEndpoints.cs`
- Test: `tests/Cod.Tests/CodServiceBranchSettlementsListTests.cs`

**Interfaces:**
- Consumes: `BranchCashSettlement`, `FinancialTransaction`, `RiderCashSettlement` — all existing domain types. `CodService.ToBranchSettlementResponse(BranchCashSettlement, List<Guid>)` — existing `private static` method in `CodService.Settlements.cs`.
- Produces: `CodService.ListBranchSettlementsAsync(Guid? branchId, bool? unverifiedOnly, int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken)` returning `Task<PagedResult<BranchCashSettlementResponse>>`. `GET /api/v1/cod/branch-settlements?branchId=&unverifiedOnly=&page=&pageSize=`, `cod.read`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Events.Application;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Cod.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class RestrictedBranchAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid> _accessibleBranchIds;
    public RestrictedBranchAuthorizationChecker(params Guid[] accessibleBranchIds) => _accessibleBranchIds = accessibleBranchIds.ToHashSet();
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(_accessibleBranchIds);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessibleBranchIds.Contains(branchId));
}

file class AlwaysTrueBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class CodServiceBranchSettlementsListTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new BranchCashSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new RiderCashSettlementConfiguration());
            modelBuilder.ApplyConfiguration(new FinancialTransactionConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker branchAuthorizationChecker) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        branchAuthorizationChecker, new AlwaysTrueMerchantLookup(), new NoopOutboxWriter());

    private static async Task<BranchCashSettlement> SeedAsync(TestDbContext dbContext, Guid organizationId, Guid branchId)
    {
        var settlement = new BranchCashSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, BranchId = branchId,
            ConfirmedAmount = 2500m, ReconciledByUserId = Guid.NewGuid(), ReconciledAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<BranchCashSettlement>().Add(settlement);
        await dbContext.SaveChangesAsync();
        return settlement;
    }

    private static async Task VerifyAsync(TestDbContext dbContext, Guid organizationId, Guid branchCashSettlementId)
    {
        dbContext.Set<FinancialTransaction>().Add(new FinancialTransaction
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Type = FinancialTransactionType.CodVerification,
            Amount = 2500m, ReferenceEntityType = "BranchCashSettlement", ReferenceEntityId = branchCashSettlementId,
            VerifiedByUserId = Guid.NewGuid(), VerifiedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task ListBranchSettlementsAsync_ExcludesForeignOrganization()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var own = await SeedAsync(dbContext, organizationId, Guid.NewGuid());
        await SeedAsync(dbContext, foreignOrganizationId, Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListBranchSettlementsAsync(null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(own.Id, single.Id);
    }

    [Fact]
    public async Task ListBranchSettlementsAsync_BranchRestricted_FiltersToAccessibleBranchOnly()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var accessibleBranchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        var accessible = await SeedAsync(dbContext, organizationId, accessibleBranchId);
        await SeedAsync(dbContext, organizationId, otherBranchId);
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(accessibleBranchId));

        var result = await service.ListBranchSettlementsAsync(null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(accessible.Id, single.Id);
    }

    [Fact]
    public async Task ListBranchSettlementsAsync_UnverifiedOnly_ExcludesFinanceVerifiedRows()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var unverified = await SeedAsync(dbContext, organizationId, Guid.NewGuid());
        var verified = await SeedAsync(dbContext, organizationId, Guid.NewGuid());
        await VerifyAsync(dbContext, organizationId, verified.Id);
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListBranchSettlementsAsync(null, true, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(unverified.Id, single.Id);
    }

    [Fact]
    public async Task ListBranchSettlementsAsync_IncludesCoveredRiderCashSettlementIds()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchSettlement = await SeedAsync(dbContext, organizationId, Guid.NewGuid());
        var riderSettlement = new RiderCashSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, RiderId = Guid.NewGuid(), BranchId = branchSettlement.BranchId,
            DeclaredAmount = 2500m, ShipmentCount = 1, SettledAt = DateTimeOffset.UtcNow,
            ConfirmedByUserId = Guid.NewGuid(), BranchCashSettlementId = branchSettlement.Id
        };
        dbContext.Set<RiderCashSettlement>().Add(riderSettlement);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListBranchSettlementsAsync(null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(riderSettlement.Id, Assert.Single(single.RiderCashSettlementIds));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter CodServiceBranchSettlementsListTests`
Expected: FAIL — `CodService` has no `ListBranchSettlementsAsync` member (compile error).

- [ ] **Step 3: Implement `ListBranchSettlementsAsync`**

Add to `src/Modules/Cod/Application/CodService.Reads.cs`, after `ListRiderSettlementsAsync`:

```csharp
    // unverifiedOnly means "no FinancialTransaction row references this BranchCashSettlement.Id
    // yet" — mirrors CreateFinanceVerificationAsync's own alreadyVerified pre-check. Drives the
    // Branch Settlements tab's "unverified only" filter (Frontend Phase F7).
    public async Task<PagedResult<BranchCashSettlementResponse>> ListBranchSettlementsAsync(
        Guid? branchId, bool? unverifiedOnly, int? page, int? pageSize,
        ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var query = _dbContext.Set<BranchCashSettlement>().Where(b => b.OrganizationId == organizationId);
        if (branchId is Guid requestedBranchId)
        {
            query = query.Where(b => b.BranchId == requestedBranchId);
        }
        if (unverifiedOnly == true)
        {
            var verifiedIds = _dbContext.Set<FinancialTransaction>()
                .Where(f => f.OrganizationId == organizationId && f.ReferenceEntityType == "BranchCashSettlement")
                .Select(f => f.ReferenceEntityId);
            query = query.Where(b => !verifiedIds.Contains(b.Id));
        }

        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "cod.read", cancellationToken);
        if (accessibleBranchIds is not null)
        {
            var accessibleArray = accessibleBranchIds.ToArray();
            query = query.Where(b => accessibleArray.Contains(b.BranchId));
        }

        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var settlements = await query
            .OrderByDescending(b => b.ReconciledAt).ThenBy(b => b.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var settlementIds = settlements.Select(b => b.Id).ToArray();
        var riderSettlementIdsBySettlement = await _dbContext.Set<RiderCashSettlement>()
            .Where(r => r.BranchCashSettlementId != null && settlementIds.Contains(r.BranchCashSettlementId.Value))
            .GroupBy(r => r.BranchCashSettlementId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.Select(r => r.Id).ToList(), cancellationToken);

        var responses = settlements
            .Select(b => ToBranchSettlementResponse(b, riderSettlementIdsBySettlement.TryGetValue(b.Id, out var ids) ? ids : new List<Guid>()))
            .ToList();
        return new PagedResult<BranchCashSettlementResponse>(responses, normalizedPage, normalizedPageSize, totalCount);
    }
```

- [ ] **Step 4: Wire the endpoint**

In `src/Modules/Cod/Presentation/CodEndpoints.cs`, insert immediately before the existing `group.MapPost("/branch-settlements", ...)` line:

```csharp
        group.MapGet("/branch-settlements", async (Guid? branchId, bool? unverifiedOnly, int? page, int? pageSize, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListBranchSettlementsAsync(branchId, unverifiedOnly, page, pageSize, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");

```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter CodServiceBranchSettlementsListTests`
Expected: PASS (4 tests)

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Cod/Application/CodService.Reads.cs src/Modules/Cod/Presentation/CodEndpoints.cs tests/Cod.Tests/CodServiceBranchSettlementsListTests.cs
git commit -m "feat(cod): add GET /api/v1/cod/branch-settlements list endpoint"
```

---

## Task 4: `GET /api/v1/cod/adjustments`

**Files:**
- Modify: `src/Modules/Cod/Application/CodService.Reads.cs`
- Modify: `src/Modules/Cod/Presentation/CodEndpoints.cs`
- Test: `tests/Cod.Tests/CodServiceAdjustmentsListTests.cs`

**Interfaces:**
- Consumes: `Adjustment` (existing domain type, own `OrganizationId` column). `CodService.ToAdjustmentResponse(Adjustment)` — existing `private static` method in `CodService.Adjustments.cs`.
- Produces: `CodService.ListAdjustmentsAsync(bool? applied, int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken)` returning `Task<PagedResult<AdjustmentResponse>>`. `GET /api/v1/cod/adjustments?applied=&page=&pageSize=`, `cod.read`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Cod.Infrastructure;
using Courier.Events.Application;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Cod.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class AlwaysTrueBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class CodServiceAdjustmentsListTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new AdjustmentConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AlwaysTrueBranchLookup(),
        new AllBranchesAuthorizationChecker(), new AlwaysTrueMerchantLookup(), new NoopOutboxWriter());

    private static async Task<Adjustment> SeedAsync(TestDbContext dbContext, Guid organizationId, bool applied)
    {
        var adjustment = new Adjustment
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, EntityType = "MerchantSettlementItem", EntityId = Guid.NewGuid(),
            BeforeValue = 0m, AfterValue = 100m, Reason = "Damaged parcel deduction",
            RequestedByUserId = Guid.NewGuid(), RequestedAt = DateTimeOffset.UtcNow,
            ApprovedByUserId = applied ? Guid.NewGuid() : null, ApprovedAt = applied ? DateTimeOffset.UtcNow : null, Applied = applied
        };
        dbContext.Set<Adjustment>().Add(adjustment);
        await dbContext.SaveChangesAsync();
        return adjustment;
    }

    [Fact]
    public async Task ListAdjustmentsAsync_ExcludesForeignOrganization()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var own = await SeedAsync(dbContext, organizationId, applied: false);
        await SeedAsync(dbContext, foreignOrganizationId, applied: false);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListAdjustmentsAsync(null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(own.Id, single.Id);
    }

    [Fact]
    public async Task ListAdjustmentsAsync_FilteredByAppliedFalse_ReturnsOnlyPending()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var pending = await SeedAsync(dbContext, organizationId, applied: false);
        await SeedAsync(dbContext, organizationId, applied: true);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListAdjustmentsAsync(false, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(pending.Id, single.Id);
    }

    [Fact]
    public async Task ListAdjustmentsAsync_FilteredByAppliedTrue_ReturnsOnlyApplied()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        await SeedAsync(dbContext, organizationId, applied: false);
        var applied = await SeedAsync(dbContext, organizationId, applied: true);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListAdjustmentsAsync(true, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(applied.Id, single.Id);
    }

    [Fact]
    public async Task ListAdjustmentsAsync_NoFilter_ReturnsBoth()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        await SeedAsync(dbContext, organizationId, applied: false);
        await SeedAsync(dbContext, organizationId, applied: true);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListAdjustmentsAsync(null, null, null, ActingUser(Guid.NewGuid()));

        Assert.Equal(2, result.TotalCount);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter CodServiceAdjustmentsListTests`
Expected: FAIL — `CodService` has no `ListAdjustmentsAsync` member (compile error).

- [ ] **Step 3: Implement `ListAdjustmentsAsync`**

Add to `src/Modules/Cod/Application/CodService.Reads.cs`, after `ListBranchSettlementsAsync`:

```csharp
    // Adjustment carries its own OrganizationId column (no join needed, same as
    // ApproveAdjustmentAsync's own lookup). No branch scoping — Adjustment targets a
    // MerchantSettlementItem, which has no BranchId either (cod.settle/cod.adjust are org-wide
    // permissions, same precedent as CreateFinanceVerificationAsync/CreateMerchantSettlementAsync).
    public async Task<PagedResult<AdjustmentResponse>> ListAdjustmentsAsync(
        bool? applied, int? page, int? pageSize,
        ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var query = _dbContext.Set<Adjustment>().Where(a => a.OrganizationId == organizationId);
        if (applied is bool requestedApplied)
        {
            query = query.Where(a => a.Applied == requestedApplied);
        }

        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var adjustments = await query
            .OrderByDescending(a => a.RequestedAt).ThenBy(a => a.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AdjustmentResponse>(adjustments.Select(ToAdjustmentResponse).ToList(), normalizedPage, normalizedPageSize, totalCount);
    }
```

- [ ] **Step 4: Wire the endpoint**

In `src/Modules/Cod/Presentation/CodEndpoints.cs`, insert immediately before the existing `group.MapPost("/adjustments", ...)` line:

```csharp
        group.MapGet("/adjustments", async (bool? applied, int? page, int? pageSize, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListAdjustmentsAsync(applied, page, pageSize, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");

```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter CodServiceAdjustmentsListTests`
Expected: PASS (4 tests)

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Cod/Application/CodService.Reads.cs src/Modules/Cod/Presentation/CodEndpoints.cs tests/Cod.Tests/CodServiceAdjustmentsListTests.cs
git commit -m "feat(cod): add GET /api/v1/cod/adjustments list endpoint"
```

---

## Task 5: `ApproveAdjustmentAsync` settle-guard fix

**Files:**
- Modify: `src/Modules/Cod/Application/CodService.Adjustments.cs`
- Test: `tests/Cod.Tests/CodServiceAdjustmentsTests.cs`

**Interfaces:**
- Consumes: `MerchantSettlement.Status` (existing field). No new public interface — this changes `ApproveAdjustmentAsync`'s existing behavior (a new failure mode) without changing its signature.
- Produces: `ApproveAdjustmentAsync` now throws `CodValidationException("Cannot approve an adjustment against an already-Settled MerchantSettlement.")` when the target's parent `MerchantSettlement.Status == MerchantSettlementStatus.Settled`.

- [ ] **Step 1: Write the failing test**

Add to `tests/Cod.Tests/CodServiceAdjustmentsTests.cs`, after `ApproveAdjustmentAsync_AlreadyApplied_Throws`:

```csharp
    [Fact]
    public async Task ApproveAdjustmentAsync_ParentSettlementAlreadySettled_ThrowsAndDoesNotMutate()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (settlement, item) = await SeedSettlementItemAsync(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId);
        var requested = await service.RequestAdjustmentAsync(
            new RequestAdjustmentRequest("MerchantSettlementItem", item.Id, 100m, "Damaged parcel deduction"), ActingUser(Guid.NewGuid()));

        // Settle the parent out from under the pending adjustment — mirrors a real timing gap
        // between "adjustment requested" and "approver gets to it," during which Finance could have
        // already settled the merchant on the original figure.
        settlement.Status = MerchantSettlementStatus.Settled;
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<CodValidationException>(() => service.ApproveAdjustmentAsync(requested.Id, ActingUser(Guid.NewGuid())));

        var unchangedItem = await dbContext.Set<MerchantSettlementItem>().SingleAsync(i => i.Id == item.Id);
        Assert.Equal(0m, unchangedItem.OtherDeductions);
        var unchangedSettlement = await dbContext.Set<MerchantSettlement>().SingleAsync(m => m.Id == settlement.Id);
        Assert.Equal(2350m, unchangedSettlement.TotalPayable);
        var unchangedAdjustment = await dbContext.Set<Adjustment>().SingleAsync(a => a.Id == requested.Id);
        Assert.False(unchangedAdjustment.Applied);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter ApproveAdjustmentAsync_ParentSettlementAlreadySettled_ThrowsAndDoesNotMutate`
Expected: FAIL — no guard exists yet, so the call succeeds and mutates `TotalPayable`/`OtherDeductions` instead of throwing.

- [ ] **Step 3: Add the guard**

In `src/Modules/Cod/Application/CodService.Adjustments.cs`, replace the body of `ApproveAdjustmentAsync` between the `Applied` check and the transaction setup:

```csharp
        if (adjustment.Applied)
        {
            throw new CodValidationException("This adjustment has already been approved and applied.");
        }

        // Guarded before opening a transaction, same reasoning as every other pre-check in this
        // module: rewriting a MerchantSettlementItem after its parent MerchantSettlement has
        // already been Settled (merchant already paid the old figure) would silently change
        // TotalPayable on a row that's supposed to be closed, with no new payout to match —
        // CLAUDE.md §23.
        MerchantSettlementItem? targetItem = null;
        if (adjustment.EntityType == AdjustableEntityType)
        {
            targetItem = await _dbContext.Set<MerchantSettlementItem>().FirstOrDefaultAsync(i => i.Id == adjustment.EntityId, cancellationToken)
                ?? throw new CodValidationException("The MerchantSettlementItem this adjustment targets no longer exists.");
            var targetSettlement = await _dbContext.Set<MerchantSettlement>()
                .FirstAsync(m => m.Id == targetItem.MerchantSettlementId && m.OrganizationId == organizationId, cancellationToken);
            if (targetSettlement.Status == MerchantSettlementStatus.Settled)
            {
                throw new CodValidationException("Cannot approve an adjustment against an already-Settled MerchantSettlement.");
            }
        }

```

Then, inside the `try` block's `switch`, replace the `case AdjustableEntityType:` body's first line (the `var item = await _dbContext.Set<MerchantSettlementItem>()...` lookup) with a reuse of the already-fetched, already-tracked instance instead of re-querying:

```csharp
                case AdjustableEntityType:
                    // targetItem is guaranteed non-null here — the guard above already fetched and
                    // validated it for this exact EntityType, and it's the same tracked instance
                    // (EF Core's identity map) any second FirstOrDefaultAsync would return anyway.
                    var item = targetItem!;
                    item.OtherDeductions = adjustment.AfterValue;
                    item.NetPayable = item.CODAmount - item.CourierFee - item.OtherDeductions;
```

Leave the rest of the `case` block (the `settlement.TotalPayable = ...` recomputation and `break;`) unchanged.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj --filter CodServiceAdjustmentsTests`
Expected: PASS (all `CodServiceAdjustmentsTests`, including the new guard test and every pre-existing test in the file — `ApproveAdjustmentAsync_RewritesOtherDeductionsAndRecomputesNetPayableAndTotalPayable` must still pass unchanged, proving the happy path survived the refactor).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Cod/Application/CodService.Adjustments.cs tests/Cod.Tests/CodServiceAdjustmentsTests.cs
git commit -m "fix(cod): reject adjustment approval against an already-settled MerchantSettlement"
```

---

## Task 6: Integration tests for the 3 new list endpoints (branch scoping)

**Files:**
- Modify: `tests/Api.Tests/CodEndpointsTests.cs`

**Interfaces:**
- Consumes: `RiderCashSettlementResponse`, `BranchCashSettlementResponse`, `AdjustmentResponse`, `PagedResult<T>`, everything else already imported by `CodEndpointsTests.cs` (`SeedBranchesAsync`, `CreateMerchantAsync`, `CreateRiderAsync`, `BookAndCompleteCodDeliveryAsync`, `GetCollectionForShipmentAsync`).
- Produces: no new production interfaces — proves Tasks 2–4's endpoints work end-to-end through the real HTTP pipeline and real Postgres, including branch scoping, which the in-memory unit tests in Tasks 2–4 cannot fully prove (their `TestDbContext` has no `AppDbContext`-level global query filter to accidentally rely on).

- [ ] **Step 1: Write the failing tests**

Add to `tests/Api.Tests/CodEndpointsTests.cs`, after `ConcurrentFinanceVerification_SameBranchCashSettlement_OnlyOneSucceeds` and before the `// ---- Task 11: merchant-facing ...` comment:

```csharp
    // ---- Frontend Phase F7: GET /rider-settlements, /branch-settlements, /adjustments ----

    [Fact]
    public async Task ListRiderSettlements_ReturnsCreatedSettlement_AndRespectsBranchScoping()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-list-rider");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        await BookAndCompleteCodDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var riderSettlement = await (await client.PostAsJsonAsync("/api/v1/cod/rider-settlements",
            new CreateRiderSettlementRequest(deliveryRiderId, destinationBranchId, 2500m))).Content.ReadFromJsonAsync<RiderCashSettlementResponse>();

        var listResponse = await client.GetAsync("/api/v1/cod/rider-settlements");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<PagedResult<RiderCashSettlementResponse>>();
        Assert.Contains(list!.Items, r => r.Id == riderSettlement!.Id);

        var unreconciledResponse = await client.GetAsync("/api/v1/cod/rider-settlements?unreconciledOnly=true");
        var unreconciledList = await unreconciledResponse.Content.ReadFromJsonAsync<PagedResult<RiderCashSettlementResponse>>();
        Assert.Contains(unreconciledList!.Items, r => r.Id == riderSettlement!.Id);

        // Reconciling this rider settlement into a branch settlement must remove it from the
        // unreconciledOnly view without removing it from the unfiltered list.
        await client.PostAsJsonAsync("/api/v1/cod/branch-settlements",
            new CreateBranchSettlementRequest(destinationBranchId, new List<Guid> { riderSettlement!.Id }, 2500m));
        var afterReconcileUnfiltered = await (await client.GetAsync("/api/v1/cod/rider-settlements")).Content.ReadFromJsonAsync<PagedResult<RiderCashSettlementResponse>>();
        Assert.Contains(afterReconcileUnfiltered!.Items, r => r.Id == riderSettlement.Id);
        var afterReconcileUnreconciledOnly = await (await client.GetAsync("/api/v1/cod/rider-settlements?unreconciledOnly=true")).Content.ReadFromJsonAsync<PagedResult<RiderCashSettlementResponse>>();
        Assert.DoesNotContain(afterReconcileUnreconciledOnly!.Items, r => r.Id == riderSettlement.Id);
    }

    [Fact]
    public async Task ListBranchSettlements_ReturnsCreatedSettlement_AndUnverifiedOnlyExcludesVerified()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-list-branch");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        await BookAndCompleteCodDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var riderSettlement = await (await client.PostAsJsonAsync("/api/v1/cod/rider-settlements",
            new CreateRiderSettlementRequest(deliveryRiderId, destinationBranchId, 2500m))).Content.ReadFromJsonAsync<RiderCashSettlementResponse>();
        var branchSettlement = await (await client.PostAsJsonAsync("/api/v1/cod/branch-settlements",
            new CreateBranchSettlementRequest(destinationBranchId, new List<Guid> { riderSettlement!.Id }, 2500m))).Content.ReadFromJsonAsync<BranchCashSettlementResponse>();

        var unverifiedResponse = await client.GetAsync("/api/v1/cod/branch-settlements?unverifiedOnly=true");
        var unverifiedList = await unverifiedResponse.Content.ReadFromJsonAsync<PagedResult<BranchCashSettlementResponse>>();
        Assert.Contains(unverifiedList!.Items, b => b.Id == branchSettlement!.Id);
        Assert.Equal(riderSettlement.Id, Assert.Single(unverifiedList.Items.Single(b => b.Id == branchSettlement!.Id).RiderCashSettlementIds));

        await client.PostAsJsonAsync("/api/v1/cod/finance-verifications", new CreateFinanceVerificationRequest(branchSettlement!.Id, 2500m));

        var afterVerifyUnverifiedOnly = await (await client.GetAsync("/api/v1/cod/branch-settlements?unverifiedOnly=true")).Content.ReadFromJsonAsync<PagedResult<BranchCashSettlementResponse>>();
        Assert.DoesNotContain(afterVerifyUnverifiedOnly!.Items, b => b.Id == branchSettlement.Id);
        var afterVerifyUnfiltered = await (await client.GetAsync("/api/v1/cod/branch-settlements")).Content.ReadFromJsonAsync<PagedResult<BranchCashSettlementResponse>>();
        Assert.Contains(afterVerifyUnfiltered!.Items, b => b.Id == branchSettlement.Id);
    }

    [Fact]
    public async Task ListAdjustments_ReturnsRequestedAdjustment_AndAppliedFilterSplitsPendingFromApproved()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-list-adjustments");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var (merchantId, _, _) = await CreateMerchantWithLoginAsync(client, "list-adjustments");
        var settlement = await SeedMerchantSettlementAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var item = settlement.Items.Single();

        var adjustmentResponse = await client.PostAsJsonAsync("/api/v1/cod/adjustments",
            new RequestAdjustmentRequest("MerchantSettlementItem", item.Id, 100m, "Damaged parcel deduction"));
        adjustmentResponse.EnsureSuccessStatusCode();
        var adjustment = await adjustmentResponse.Content.ReadFromJsonAsync<AdjustmentResponse>();

        var pendingList = await (await client.GetAsync("/api/v1/cod/adjustments?applied=false")).Content.ReadFromJsonAsync<PagedResult<AdjustmentResponse>>();
        Assert.Contains(pendingList!.Items, a => a.Id == adjustment!.Id);

        await client.PostAsync($"/api/v1/cod/adjustments/{adjustment!.Id}/approve", null);

        var afterApprovePending = await (await client.GetAsync("/api/v1/cod/adjustments?applied=false")).Content.ReadFromJsonAsync<PagedResult<AdjustmentResponse>>();
        Assert.DoesNotContain(afterApprovePending!.Items, a => a.Id == adjustment.Id);
        var afterApproveApplied = await (await client.GetAsync("/api/v1/cod/adjustments?applied=true")).Content.ReadFromJsonAsync<PagedResult<AdjustmentResponse>>();
        Assert.Contains(afterApproveApplied!.Items, a => a.Id == adjustment.Id);
    }

    [Fact]
    public async Task ApproveAdjustment_AgainstAlreadySettledMerchantSettlement_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-adjust-settled");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var (merchantId, _, _) = await CreateMerchantWithLoginAsync(client, "adjust-settled");
        var settlement = await SeedMerchantSettlementAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);
        var item = settlement.Items.Single();
        var adjustment = await (await client.PostAsJsonAsync("/api/v1/cod/adjustments",
            new RequestAdjustmentRequest("MerchantSettlementItem", item.Id, 100m, "Damaged parcel deduction"))).Content.ReadFromJsonAsync<AdjustmentResponse>();

        var settleResponse = await client.PostAsync($"/api/v1/cod/merchant-settlements/{settlement.Id}/settle", null);
        settleResponse.EnsureSuccessStatusCode();

        var approveResponse = await client.PostAsync($"/api/v1/cod/adjustments/{adjustment!.Id}/approve", null);

        Assert.Equal(HttpStatusCode.BadRequest, approveResponse.StatusCode);
        var unchangedSettlement = await (await client.GetAsync($"/api/v1/cod/merchant-settlements/{settlement.Id}")).Content.ReadFromJsonAsync<MerchantSettlementResponse>();
        Assert.Equal(settlement.TotalPayable, unchangedSettlement!.TotalPayable);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "CodEndpointsTests&ListRiderSettlements_ReturnsCreatedSettlement_AndRespectsBranchScoping|CodEndpointsTests&ListBranchSettlements_ReturnsCreatedSettlement_AndUnverifiedOnlyExcludesVerified|CodEndpointsTests&ListAdjustments_ReturnsRequestedAdjustment_AndAppliedFilterSplitsPendingFromApproved|CodEndpointsTests&ApproveAdjustment_AgainstAlreadySettledMerchantSettlement_Returns400"`
Expected: FAIL — 404 on the three new `GET` routes before Tasks 2–4 land (this task runs after them, so verify by temporarily confirming the test file compiles and fails meaningfully — Tasks 2–4's endpoints already exist by the time this task executes, so the realistic failure mode here is a genuine assertion failure if any wiring detail was missed; run this step regardless to catch that).

- [ ] **Step 3: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter CodEndpointsTests`
Expected: PASS — the full `CodEndpointsTests` file, including every pre-existing test plus these 4 new ones (requires the local Postgres container running on port 5434).

- [ ] **Step 4: Commit**

```bash
git add tests/Api.Tests/CodEndpointsTests.cs
git commit -m "test(cod): add integration coverage for the 3 new list endpoints and the settle-guard"
```

---

## Task 7: `cod` feature types, read-only BFF routes, and read hooks

**Files:**
- Create: `src/web/src/features/cod/types.ts`
- Create: `src/web/src/app/api/cod/collections/route.ts`
- Create: `src/web/src/app/api/cod/collections/route.test.ts`
- Create: `src/web/src/app/api/cod/collections/[id]/route.ts`
- Create: `src/web/src/app/api/cod/rider-settlements/route.ts`
- Create: `src/web/src/app/api/cod/rider-settlements/route.test.ts`
- Create: `src/web/src/app/api/cod/branch-settlements/route.ts`
- Create: `src/web/src/app/api/cod/merchant-settlements/route.ts`
- Create: `src/web/src/app/api/cod/merchant-settlements/[id]/route.ts`
- Create: `src/web/src/app/api/cod/adjustments/route.ts`
- Create: `src/web/src/app/api/cod/reconciliation/route.ts`
- Create: `src/web/src/features/cod/hooks/useCodCollections.ts`
- Create: `src/web/src/features/cod/hooks/useCodCollection.ts`
- Create: `src/web/src/features/cod/hooks/useRiderSettlements.ts`
- Create: `src/web/src/features/cod/hooks/useBranchSettlements.ts`
- Create: `src/web/src/features/cod/hooks/useMerchantSettlements.ts`
- Create: `src/web/src/features/cod/hooks/useMerchantSettlement.ts`
- Create: `src/web/src/features/cod/hooks/useAdjustments.ts`
- Create: `src/web/src/features/cod/hooks/useCodReconciliation.ts`

**Interfaces:**
- Consumes: `apiFetch` (`@/lib/auth/apiFetch`), `clientFetch` (`@/lib/api/clientFetch`), `PagedResult<T>` (`@/components/data-table/DataTable`).
- Produces: `CodCollection`, `RiderCashSettlement`, `BranchCashSettlement`, `MerchantSettlement`, `MerchantSettlementItem`, `Adjustment`, `CodReconciliationSummary` types plus every `Create*Input` type Task 8's mutation hooks consume. `useCodCollections(filters)`, `useCodCollection(id)`, `useRiderSettlements(filters)`, `useBranchSettlements(filters)`, `useMerchantSettlements(merchantId?)`, `useMerchantSettlement(id)`, `useAdjustments(filters)`, `useCodReconciliation(filters)`.

- [ ] **Step 1: Write the types**

`src/web/src/features/cod/types.ts`:

```typescript
export type CodCollectionStatus =
  | "CodExpected" | "CodCollected" | "RiderDeposited" | "BranchReconciled"
  | "FinanceVerified" | "MerchantSettlementCreated" | "Settled";

export interface CodCollection {
  id: string;
  shipmentId: string;
  merchantId: string;
  branchId: string;
  expectedAmount: number;
  shipmentTotalCharge: number;
  collectedAmount: number | null;
  status: CodCollectionStatus;
  collectedByRiderId: string | null;
  collectedAt: string | null;
  deliveryProofId: string | null;
  riderCashSettlementId: string | null;
  merchantSettlementItemId: string | null;
  createdAt: string;
}

export interface RiderCashSettlement {
  id: string;
  riderId: string;
  branchId: string;
  declaredAmount: number;
  shipmentCount: number;
  settledAt: string;
  confirmedByUserId: string;
  branchCashSettlementId: string | null;
}

export interface CreateRiderSettlementInput {
  riderId: string;
  branchId: string;
  declaredAmount: number;
}

export interface BranchCashSettlement {
  id: string;
  branchId: string;
  confirmedAmount: number;
  reconciledByUserId: string;
  reconciledAt: string;
  riderCashSettlementIds: string[];
}

export interface CreateBranchSettlementInput {
  branchId: string;
  riderCashSettlementIds: string[];
  confirmedAmount: number;
}

export interface CreateFinanceVerificationInput {
  branchCashSettlementId: string;
  amount: number;
}

export type MerchantSettlementStatus = "Created" | "Settled";

export interface MerchantSettlementItem {
  id: string;
  shipmentId: string;
  codAmount: number;
  courierFee: number;
  otherDeductions: number;
  netPayable: number;
}

export interface MerchantSettlement {
  id: string;
  merchantId: string;
  periodFrom: string;
  periodTo: string;
  totalPayable: number;
  status: MerchantSettlementStatus;
  settledAt: string | null;
  items: MerchantSettlementItem[];
}

export interface CreateMerchantSettlementInput {
  merchantId: string;
  periodFrom: string;
  periodTo: string;
}

export interface Adjustment {
  id: string;
  entityType: string;
  entityId: string;
  beforeValue: number;
  afterValue: number;
  reason: string;
  requestedByUserId: string;
  requestedAt: string;
  approvedByUserId: string | null;
  approvedAt: string | null;
  applied: boolean;
}

export interface RequestAdjustmentInput {
  entityType: string;
  entityId: string;
  afterValue: number;
  reason: string;
}

export interface CodReconciliationSummary {
  expectedCod: number;
  collectedCod: number;
  riderDepositedCod: number;
  branchReceivedCod: number;
  merchantPayableCod: number;
  totalCourierFees: number;
}
```

- [ ] **Step 2: Add the read-only BFF routes**

`src/web/src/app/api/cod/collections/route.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/cod/collections${query}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/cod/collections/[id]/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/cod/collections/${params.id}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/cod/rider-settlements/route.ts` (Task 8 adds a `POST` export to this same file):

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/cod/rider-settlements${query}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/cod/branch-settlements/route.ts` (Task 8 adds a `POST` export to this same file):

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/cod/branch-settlements${query}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/cod/merchant-settlements/route.ts` (Task 8 adds a `POST` export to this same file):

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/cod/merchant-settlements${query}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/cod/merchant-settlements/[id]/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/cod/merchant-settlements/${params.id}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/cod/adjustments/route.ts` (Task 8 adds a `POST` export to this same file):

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/cod/adjustments${query}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/cod/reconciliation/route.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/cod/reconciliation${query}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 3: Write BFF route tests for the two query-param routes**

`src/web/src/app/api/cod/collections/route.test.ts`:

```typescript
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";
import { NextRequest } from "next/server";

describe("GET /api/cod/collections", () => {
  it("proxies the query string to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }),
    } as Response);

    const request = new NextRequest("http://localhost/api/cod/collections?status=CodExpected");
    const response = await GET(request);

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/cod/collections?status=CodExpected");
    expect(response.status).toBe(200);
  });
});
```

`src/web/src/app/api/cod/rider-settlements/route.test.ts`:

```typescript
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";
import { NextRequest } from "next/server";

describe("GET /api/cod/rider-settlements", () => {
  it("proxies the query string to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }),
    } as Response);

    const request = new NextRequest("http://localhost/api/cod/rider-settlements?unreconciledOnly=true");
    const response = await GET(request);

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/cod/rider-settlements?unreconciledOnly=true");
    expect(response.status).toBe(200);
  });
});
```

- [ ] **Step 4: Run the new BFF tests**

Run: `cd src/web && npm test -- collections/route.test.ts rider-settlements/route.test.ts`
Expected: PASS (2 tests)

- [ ] **Step 5: Write the read hooks**

`src/web/src/features/cod/hooks/useCodCollections.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { CodCollection, CodCollectionStatus } from "@/features/cod/types";

export function useCodCollections(params: { status?: CodCollectionStatus; branchId?: string; merchantId?: string; page?: number; pageSize?: number } = {}) {
  const { status, branchId, merchantId, page, pageSize } = params;
  const query = new URLSearchParams();
  if (status) query.set("status", status);
  if (branchId) query.set("branchId", branchId);
  if (merchantId) query.set("merchantId", merchantId);
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["cod", "collections", status ?? null, branchId ?? null, merchantId ?? null, page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<CodCollection>>(`/api/cod/collections${search ? `?${search}` : ""}`),
  });
}
```

`src/web/src/features/cod/hooks/useCodCollection.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CodCollection } from "@/features/cod/types";

export function useCodCollection(id: string) {
  return useQuery({
    queryKey: ["cod", "collections", id],
    queryFn: () => clientFetch<CodCollection>(`/api/cod/collections/${id}`),
    enabled: id.length > 0,
  });
}
```

`src/web/src/features/cod/hooks/useRiderSettlements.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { RiderCashSettlement } from "@/features/cod/types";

export function useRiderSettlements(params: { branchId?: string; riderId?: string; unreconciledOnly?: boolean; page?: number; pageSize?: number } = {}) {
  const { branchId, riderId, unreconciledOnly, page, pageSize } = params;
  const query = new URLSearchParams();
  if (branchId) query.set("branchId", branchId);
  if (riderId) query.set("riderId", riderId);
  if (unreconciledOnly) query.set("unreconciledOnly", "true");
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["cod", "rider-settlements", branchId ?? null, riderId ?? null, unreconciledOnly ?? null, page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<RiderCashSettlement>>(`/api/cod/rider-settlements${search ? `?${search}` : ""}`),
  });
}
```

`src/web/src/features/cod/hooks/useBranchSettlements.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { BranchCashSettlement } from "@/features/cod/types";

export function useBranchSettlements(params: { branchId?: string; unverifiedOnly?: boolean; page?: number; pageSize?: number } = {}) {
  const { branchId, unverifiedOnly, page, pageSize } = params;
  const query = new URLSearchParams();
  if (branchId) query.set("branchId", branchId);
  if (unverifiedOnly) query.set("unverifiedOnly", "true");
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["cod", "branch-settlements", branchId ?? null, unverifiedOnly ?? null, page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<BranchCashSettlement>>(`/api/cod/branch-settlements${search ? `?${search}` : ""}`),
  });
}
```

`src/web/src/features/cod/hooks/useMerchantSettlements.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantSettlement } from "@/features/cod/types";

export function useMerchantSettlements(merchantId?: string) {
  const search = merchantId ? `?merchantId=${merchantId}` : "";
  return useQuery({
    queryKey: ["cod", "merchant-settlements", merchantId ?? null],
    queryFn: () => clientFetch<MerchantSettlement[]>(`/api/cod/merchant-settlements${search}`),
  });
}
```

`src/web/src/features/cod/hooks/useMerchantSettlement.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantSettlement } from "@/features/cod/types";

export function useMerchantSettlement(id: string) {
  return useQuery({
    queryKey: ["cod", "merchant-settlements", id],
    queryFn: () => clientFetch<MerchantSettlement>(`/api/cod/merchant-settlements/${id}`),
    enabled: id.length > 0,
  });
}
```

`src/web/src/features/cod/hooks/useAdjustments.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Adjustment } from "@/features/cod/types";

export function useAdjustments(params: { applied?: boolean; page?: number; pageSize?: number } = {}) {
  const { applied, page, pageSize } = params;
  const query = new URLSearchParams();
  if (applied !== undefined) query.set("applied", String(applied));
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["cod", "adjustments", applied ?? null, page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<Adjustment>>(`/api/cod/adjustments${search ? `?${search}` : ""}`),
  });
}
```

`src/web/src/features/cod/hooks/useCodReconciliation.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CodReconciliationSummary } from "@/features/cod/types";

export function useCodReconciliation(params: { branchId?: string; merchantId?: string; from?: string; to?: string } = {}) {
  const { branchId, merchantId, from, to } = params;
  const query = new URLSearchParams();
  if (branchId) query.set("branchId", branchId);
  if (merchantId) query.set("merchantId", merchantId);
  if (from) query.set("from", from);
  if (to) query.set("to", to);
  const search = query.toString();

  return useQuery({
    queryKey: ["cod", "reconciliation", branchId ?? null, merchantId ?? null, from ?? null, to ?? null],
    queryFn: () => clientFetch<CodReconciliationSummary>(`/api/cod/reconciliation${search ? `?${search}` : ""}`),
  });
}
```

- [ ] **Step 6: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS — no existing tests broken, no type errors.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/cod/types.ts src/web/src/app/api/cod src/web/src/features/cod/hooks
git commit -m "feat(cod-frontend): add types, read-only BFF routes, and read hooks"
```

---

## Task 8: Mutation BFF routes, mutation hooks, and the `codErrorMessage` helper

**Files:**
- Modify: `src/web/src/app/api/cod/rider-settlements/route.ts`
- Modify: `src/web/src/app/api/cod/branch-settlements/route.ts`
- Create: `src/web/src/app/api/cod/finance-verifications/route.ts`
- Modify: `src/web/src/app/api/cod/merchant-settlements/route.ts`
- Create: `src/web/src/app/api/cod/merchant-settlements/[id]/settle/route.ts`
- Modify: `src/web/src/app/api/cod/adjustments/route.ts`
- Create: `src/web/src/app/api/cod/adjustments/[id]/approve/route.ts`
- Create: `src/web/src/features/cod/lib/codErrorMessage.ts`
- Create: `src/web/src/features/cod/lib/codErrorMessage.test.ts`
- Create: `src/web/src/features/cod/hooks/useCreateRiderSettlement.ts`
- Create: `src/web/src/features/cod/hooks/useCreateBranchSettlement.ts`
- Create: `src/web/src/features/cod/hooks/useCreateFinanceVerification.ts`
- Create: `src/web/src/features/cod/hooks/useCreateMerchantSettlement.ts`
- Create: `src/web/src/features/cod/hooks/useSettleMerchantSettlement.ts`
- Create: `src/web/src/features/cod/hooks/useRequestAdjustment.ts`
- Create: `src/web/src/features/cod/hooks/useApproveAdjustment.ts`

**Interfaces:**
- Consumes: `CreateRiderSettlementInput`, `CreateBranchSettlementInput`, `CreateFinanceVerificationInput`, `CreateMerchantSettlementInput`, `RequestAdjustmentInput` (Task 7's `types.ts`), `clientFetch` (`@/lib/api/clientFetch`).
- Produces: `codErrorMessage(error: unknown): string`. `useCreateRiderSettlement()`, `useCreateBranchSettlement()`, `useCreateFinanceVerification()`, `useCreateMerchantSettlement()`, `useSettleMerchantSettlement()`, `useRequestAdjustment()`, `useApproveAdjustment()` — every mutation hook's `onSuccess` invalidates `["cod", "collections"]` plus its own entity's broad list key, per the spec's cross-invalidation requirement (settlement/verification/settle/adjustment actions all flip `CodCollection.Status` underneath).

- [ ] **Step 1: Add `POST` to the three existing GET-only route files**

Append to `src/web/src/app/api/cod/rider-settlements/route.ts` (after the existing `GET` export):

```typescript

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/cod/rider-settlements", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Append to `src/web/src/app/api/cod/branch-settlements/route.ts` (after the existing `GET` export):

```typescript

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/cod/branch-settlements", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Append to `src/web/src/app/api/cod/merchant-settlements/route.ts` (after the existing `GET` export):

```typescript

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/cod/merchant-settlements", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Append to `src/web/src/app/api/cod/adjustments/route.ts` (after the existing `GET` export):

```typescript

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/cod/adjustments", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 2: Add the three new action-only route files**

`src/web/src/app/api/cod/finance-verifications/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/cod/finance-verifications", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/cod/merchant-settlements/[id]/settle/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/cod/merchant-settlements/${params.id}/settle`, { method: "POST" });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/cod/adjustments/[id]/approve/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/cod/adjustments/${params.id}/approve`, { method: "POST" });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 3: Write the failing test for `codErrorMessage`**

`src/web/src/features/cod/lib/codErrorMessage.test.ts`:

```typescript
import { describe, expect, it } from "vitest";
import { codErrorMessage } from "./codErrorMessage";

describe("codErrorMessage", () => {
  it("extracts the message from a CodValidationException error body", () => {
    const error = new Error(JSON.stringify({ error: { code: "COD_VALIDATION_FAILED", message: "Cannot approve an adjustment against an already-Settled MerchantSettlement." } }));

    expect(codErrorMessage(error)).toBe("Cannot approve an adjustment against an already-Settled MerchantSettlement.");
  });

  it("falls back to a generic message when the error isn't JSON", () => {
    const error = new Error("Network request failed");

    expect(codErrorMessage(error)).toBe("Something went wrong. Check the fields and try again.");
  });

  it("falls back to a generic message when the JSON has no error.message field", () => {
    const error = new Error(JSON.stringify({ status: 500 }));

    expect(codErrorMessage(error)).toBe("Something went wrong. Check the fields and try again.");
  });
});
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `cd src/web && npm test -- codErrorMessage.test.ts`
Expected: FAIL — `./codErrorMessage` doesn't exist yet.

- [ ] **Step 5: Implement `codErrorMessage`**

`src/web/src/features/cod/lib/codErrorMessage.ts`:

```typescript
// CodEndpoints.HandleAsync (backend) wraps every CodValidationException as
// { error: { code: "COD_VALIDATION_FAILED", message: "<human-readable text>" } } — unlike
// Manifests' scan-rejection codes (manifestErrorMessages.ts), Cod's message is already
// human-readable, so this just extracts it directly rather than mapping a code to a label.
export function codErrorMessage(error: unknown): string {
  try {
    const parsed = JSON.parse((error as Error).message);
    const message = parsed?.error?.message;
    if (typeof message === "string") {
      return message;
    }
  } catch {
    // fall through to generic message
  }
  return "Something went wrong. Check the fields and try again.";
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `cd src/web && npm test -- codErrorMessage.test.ts`
Expected: PASS (3 tests)

- [ ] **Step 7: Write the mutation hooks**

`src/web/src/features/cod/hooks/useCreateRiderSettlement.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { RiderCashSettlement, CreateRiderSettlementInput } from "@/features/cod/types";

export function useCreateRiderSettlement() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateRiderSettlementInput) =>
      clientFetch<RiderCashSettlement>("/api/cod/rider-settlements", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod", "rider-settlements"] });
      queryClient.invalidateQueries({ queryKey: ["cod", "collections"] });
    },
  });
}
```

`src/web/src/features/cod/hooks/useCreateBranchSettlement.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { BranchCashSettlement, CreateBranchSettlementInput } from "@/features/cod/types";

export function useCreateBranchSettlement() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateBranchSettlementInput) =>
      clientFetch<BranchCashSettlement>("/api/cod/branch-settlements", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod", "branch-settlements"] });
      queryClient.invalidateQueries({ queryKey: ["cod", "rider-settlements"] });
      queryClient.invalidateQueries({ queryKey: ["cod", "collections"] });
    },
  });
}
```

`src/web/src/features/cod/hooks/useCreateFinanceVerification.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CreateFinanceVerificationInput } from "@/features/cod/types";

interface FinancialTransactionResult {
  id: string;
  referenceEntityId: string;
}

export function useCreateFinanceVerification() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateFinanceVerificationInput) =>
      clientFetch<FinancialTransactionResult>("/api/cod/finance-verifications", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod", "branch-settlements"] });
      queryClient.invalidateQueries({ queryKey: ["cod", "collections"] });
    },
  });
}
```

`src/web/src/features/cod/hooks/useCreateMerchantSettlement.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantSettlement, CreateMerchantSettlementInput } from "@/features/cod/types";

export function useCreateMerchantSettlement() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateMerchantSettlementInput) =>
      clientFetch<MerchantSettlement>("/api/cod/merchant-settlements", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod", "merchant-settlements"] });
      queryClient.invalidateQueries({ queryKey: ["cod", "collections"] });
    },
  });
}
```

`src/web/src/features/cod/hooks/useSettleMerchantSettlement.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantSettlement } from "@/features/cod/types";

export function useSettleMerchantSettlement(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => clientFetch<MerchantSettlement>(`/api/cod/merchant-settlements/${id}/settle`, { method: "POST" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod", "merchant-settlements"] });
      queryClient.invalidateQueries({ queryKey: ["cod", "collections"] });
    },
  });
}
```

`src/web/src/features/cod/hooks/useRequestAdjustment.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Adjustment, RequestAdjustmentInput } from "@/features/cod/types";

export function useRequestAdjustment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: RequestAdjustmentInput) =>
      clientFetch<Adjustment>("/api/cod/adjustments", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod", "adjustments"] });
    },
  });
}
```

`src/web/src/features/cod/hooks/useApproveAdjustment.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Adjustment } from "@/features/cod/types";

export function useApproveAdjustment(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => clientFetch<Adjustment>(`/api/cod/adjustments/${id}/approve`, { method: "POST" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod", "adjustments"] });
      queryClient.invalidateQueries({ queryKey: ["cod", "merchant-settlements"] });
    },
  });
}
```

- [ ] **Step 8: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS — no existing tests broken, no type errors.

- [ ] **Step 9: Commit**

```bash
git add src/web/src/app/api/cod src/web/src/features/cod/lib src/web/src/features/cod/hooks
git commit -m "feat(cod-frontend): add mutation BFF routes, mutation hooks, and codErrorMessage helper"
```

---

## Task 9: `codPanelStore`, the `/cod` page shell with tab switcher, and the Collections tab

**Files:**
- Create: `src/web/src/features/cod/store/codPanelStore.ts`
- Create: `src/web/src/features/cod/store/codPanelStore.test.ts`
- Create: `src/web/src/features/cod/components/CollectionsTab.tsx`
- Create: `src/web/src/features/cod/components/CollectionDetailSheet.tsx`
- Create: `src/web/src/app/(staff)/cod/page.tsx`

**Interfaces:**
- Consumes: `useCodCollections` (Task 7), `useCodCollection` (Task 7), `useBranches` (`@/features/branches/hooks/useBranches`), `useMerchants` (`@/features/shipments/hooks/useMerchants`), `usePermission` (`@/features/auth/hooks/usePermission`), `DataTable`/`ColumnDef`/`PagedResult` (`@/components/data-table/DataTable`), `StatusBadge` (`@/components/status-badge/StatusBadge`).
- Produces: `useCodPanelStore` — one shared Zustand store for every sheet this phase needs: `{ mode: "closed" } | { mode: "collection-detail"; id } | { mode: "rider-settlement-create" } | { mode: "rider-settlement-detail"; id } | { mode: "branch-settlement-create" } | { mode: "branch-settlement-detail"; id } | { mode: "merchant-settlement-create" } | { mode: "merchant-settlement-detail"; id } | { mode: "adjustment-detail"; id }`, with one `open*` action per mode plus `close()`. `CodPage` at `/cod` — a tab switcher (local `useState`, no URL persistence — no other page in this codebase persists sub-view state in the URL, and Next's `useSearchParams` needs a `<Suspense>` boundary this codebase doesn't otherwise use) that Tasks 10–14 each extend with one more tab.

- [ ] **Step 1: Write the failing store test**

`src/web/src/features/cod/store/codPanelStore.test.ts`:

```typescript
import { describe, expect, it, beforeEach } from "vitest";
import { useCodPanelStore } from "./codPanelStore";

describe("useCodPanelStore", () => {
  beforeEach(() => {
    useCodPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useCodPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCollectionDetail sets collection-detail mode with the given id", () => {
    useCodPanelStore.getState().openCollectionDetail("c1");
    expect(useCodPanelStore.getState().panel).toEqual({ mode: "collection-detail", id: "c1" });
  });

  it("openRiderSettlementCreate sets rider-settlement-create mode", () => {
    useCodPanelStore.getState().openRiderSettlementCreate();
    expect(useCodPanelStore.getState().panel).toEqual({ mode: "rider-settlement-create" });
  });

  it("openAdjustmentDetail sets adjustment-detail mode with the given id", () => {
    useCodPanelStore.getState().openAdjustmentDetail("a1");
    expect(useCodPanelStore.getState().panel).toEqual({ mode: "adjustment-detail", id: "a1" });
  });

  it("close returns to closed from any mode", () => {
    useCodPanelStore.getState().openMerchantSettlementDetail("m1");
    useCodPanelStore.getState().close();
    expect(useCodPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- codPanelStore.test.ts`
Expected: FAIL — `./codPanelStore` doesn't exist yet.

- [ ] **Step 3: Implement `codPanelStore`**

`src/web/src/features/cod/store/codPanelStore.ts`:

```typescript
import { create } from "zustand";

type CodPanelState =
  | { mode: "closed" }
  | { mode: "collection-detail"; id: string }
  | { mode: "rider-settlement-create" }
  | { mode: "rider-settlement-detail"; id: string }
  | { mode: "branch-settlement-create" }
  | { mode: "branch-settlement-detail"; id: string }
  | { mode: "merchant-settlement-create" }
  | { mode: "merchant-settlement-detail"; id: string }
  | { mode: "adjustment-detail"; id: string };

interface CodPanelStore {
  panel: CodPanelState;
  openCollectionDetail: (id: string) => void;
  openRiderSettlementCreate: () => void;
  openRiderSettlementDetail: (id: string) => void;
  openBranchSettlementCreate: () => void;
  openBranchSettlementDetail: (id: string) => void;
  openMerchantSettlementCreate: () => void;
  openMerchantSettlementDetail: (id: string) => void;
  openAdjustmentDetail: (id: string) => void;
  close: () => void;
}

// One shared store for every /cod sheet (docs/ARCHITECTURE.md §9a) — only one sheet is ever open at
// a time on this single-page, tabbed UI, so a discriminated union across all 5 sheet kinds is
// simpler than 5 separate per-entity stores (contrast Ndr's two-store split, which had two
// genuinely separate pages).
export const useCodPanelStore = create<CodPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCollectionDetail: (id) => set({ panel: { mode: "collection-detail", id } }),
  openRiderSettlementCreate: () => set({ panel: { mode: "rider-settlement-create" } }),
  openRiderSettlementDetail: (id) => set({ panel: { mode: "rider-settlement-detail", id } }),
  openBranchSettlementCreate: () => set({ panel: { mode: "branch-settlement-create" } }),
  openBranchSettlementDetail: (id) => set({ panel: { mode: "branch-settlement-detail", id } }),
  openMerchantSettlementCreate: () => set({ panel: { mode: "merchant-settlement-create" } }),
  openMerchantSettlementDetail: (id) => set({ panel: { mode: "merchant-settlement-detail", id } }),
  openAdjustmentDetail: (id) => set({ panel: { mode: "adjustment-detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- codPanelStore.test.ts`
Expected: PASS (5 tests)

- [ ] **Step 5: Build the Collections tab**

`src/web/src/features/cod/components/CollectionsTab.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useCodCollections } from "@/features/cod/hooks/useCodCollections";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import type { CodCollection, CodCollectionStatus } from "@/features/cod/types";

const STATUS_OPTIONS: CodCollectionStatus[] = [
  "CodExpected", "CodCollected", "RiderDeposited", "BranchReconciled", "FinanceVerified", "MerchantSettlementCreated", "Settled",
];

function formatAmount(value: number | null): string {
  return value === null ? "—" : `Rs. ${value.toFixed(2)}`;
}

export function CollectionsTab() {
  const [status, setStatus] = useState<CodCollectionStatus | "all">("all");
  const [branchId, setBranchId] = useState<string | "all">("all");
  const [merchantId, setMerchantId] = useState<string | "all">("all");
  const [page, setPage] = useState(1);
  const pageSize = 20;

  const { data: branches } = useBranches();
  const { data: merchants } = useMerchants();
  const { data, isLoading } = useCodCollections({
    status: status === "all" ? undefined : status,
    branchId: branchId === "all" ? undefined : branchId,
    merchantId: merchantId === "all" ? undefined : merchantId,
    page,
    pageSize,
  });
  const openDetail = useCodPanelStore((s) => s.openCollectionDetail);

  const columns: ColumnDef<CodCollection>[] = [
    {
      key: "shipmentId",
      header: "Shipment",
      render: (c) => (
        <button type="button" onClick={() => openDetail(c.id)} className="font-medium text-foreground hover:underline">
          {c.shipmentId.slice(0, 8)}
        </button>
      ),
    },
    { key: "expectedAmount", header: "Expected Amount", render: (c) => formatAmount(c.expectedAmount) },
    { key: "collectedAmount", header: "Collected Amount", render: (c) => formatAmount(c.collectedAmount) },
    { key: "status", header: "Status", render: (c) => <StatusBadge status={c.status} namespace="Cod" /> },
    { key: "collectedAt", header: "Collected At", render: (c) => (c.collectedAt ? new Date(c.collectedAt).toLocaleDateString() : "—") },
  ];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <Select value={status} onValueChange={(v) => { setStatus(v as CodCollectionStatus | "all"); setPage(1); }}>
          <SelectTrigger className="w-48" aria-label="Filter by status">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All statuses</SelectItem>
            {STATUS_OPTIONS.map((s) => (
              <SelectItem key={s} value={s}>{s}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={branchId} onValueChange={(v) => { setBranchId(v); setPage(1); }}>
          <SelectTrigger className="w-48" aria-label="Filter by branch">
            <SelectValue placeholder="All branches" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All branches</SelectItem>
            {branches?.map((b) => (
              <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={merchantId} onValueChange={(v) => { setMerchantId(v); setPage(1); }}>
          <SelectTrigger className="w-48" aria-label="Filter by merchant">
            <SelectValue placeholder="All merchants" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All merchants</SelectItem>
            {merchants?.map((m) => (
              <SelectItem key={m.id} value={m.id}>{m.displayName}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No COD collections"
        emptyDescription="COD collections are created automatically when a COD shipment is booked."
        onPageChange={setPage}
        rowKey={(c) => c.id}
      />
    </div>
  );
}
```

- [ ] **Step 6: Build the Collection detail sheet**

`src/web/src/features/cod/components/CollectionDetailSheet.tsx`:

```tsx
"use client";

import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useCodCollection } from "@/features/cod/hooks/useCodCollection";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

function formatAmount(value: number | null): string {
  return value === null ? "—" : `Rs. ${value.toFixed(2)}`;
}

export function CollectionDetailSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const isOpen = panel.mode === "collection-detail";
  const id = panel.mode === "collection-detail" ? panel.id : "";

  const { data: collection, isLoading, isError, refetch } = useCodCollection(id);

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>COD Collection</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && (
            <ErrorState
              title="Couldn't load this COD collection"
              description="It may not exist, or you may not have access to it."
              onRetry={() => refetch()}
            />
          )}
          {collection && (
            <div className="grid grid-cols-2 gap-4">
              <div className="flex flex-col gap-1">
                <span className="text-xs text-muted-foreground">Status</span>
                <StatusBadge status={collection.status} namespace="Cod" />
              </div>
              <Field label="Shipment" value={collection.shipmentId} />
              <Field label="Expected Amount" value={formatAmount(collection.expectedAmount)} />
              <Field label="Collected Amount" value={formatAmount(collection.collectedAmount)} />
              <Field label="Collected At" value={collection.collectedAt ? new Date(collection.collectedAt).toLocaleString() : "—"} />
              <Field label="Created At" value={new Date(collection.createdAt).toLocaleString()} />
            </div>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 7: Build the `/cod` page shell with the tab switcher**

`src/web/src/app/(staff)/cod/page.tsx`:

```tsx
"use client";

import { useState } from "react";
import { CollectionsTab } from "@/features/cod/components/CollectionsTab";
import { CollectionDetailSheet } from "@/features/cod/components/CollectionDetailSheet";

// Plain button-row tab switcher, not @radix-ui/react-tabs — no Tabs primitive exists in this repo
// and a 6-way conditional render doesn't need one (CLAUDE.md's YAGNI guidance). Tasks 10-14 each
// append one more entry to TABS, one more `activeTab === "..."` branch, and their own Sheet(s) at
// the bottom — this file grows additively, never restructured, across the rest of this plan.
const TABS = [
  { key: "collections", label: "Collections" },
] as const;

type TabKey = (typeof TABS)[number]["key"];

export default function CodPage() {
  const [activeTab, setActiveTab] = useState<TabKey>("collections");

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">COD</h1>
      <div className="flex gap-1 border-b border-border" role="tablist">
        {TABS.map((tab) => (
          <button
            key={tab.key}
            type="button"
            role="tab"
            aria-selected={activeTab === tab.key}
            onClick={() => setActiveTab(tab.key)}
            className={`px-3 py-2 text-sm font-medium transition-colors ${
              activeTab === tab.key
                ? "border-b-2 border-foreground text-foreground"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === "collections" && <CollectionsTab />}

      <CollectionDetailSheet />
    </div>
  );
}
```

- [ ] **Step 8: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS — no existing tests broken, no type errors. Note: `StatusBadge` will render the `Cod.*` keys' fallback (`resolveStatusConfig`'s `?? { label: status, token: "status-cancelled" }`) until Task 14 adds the real `Cod.*` `STATUS_CONFIG` entries — this is expected and not a bug at this point in the plan.

- [ ] **Step 9: Commit**

```bash
git add src/web/src/features/cod/store src/web/src/features/cod/components/CollectionsTab.tsx src/web/src/features/cod/components/CollectionDetailSheet.tsx "src/web/src/app/(staff)/cod/page.tsx"
git commit -m "feat(cod-frontend): add codPanelStore, /cod page shell, and Collections tab"
```

---

## Task 10: Rider Settlements tab

**Files:**
- Create: `src/web/src/features/cod/schema.ts`
- Create: `src/web/src/features/cod/components/RiderSettlementsTab.tsx`
- Create: `src/web/src/features/cod/components/RiderSettlementCreateSheet.tsx`
- Create: `src/web/src/features/cod/components/RiderSettlementDetailSheet.tsx`
- Create: `src/web/src/features/cod/components/RiderSettlementCreateSheet.test.tsx`
- Modify: `src/web/src/app/(staff)/cod/page.tsx`

**Interfaces:**
- Consumes: `useRiderSettlements`/`useCreateRiderSettlement` (Tasks 7–8), `useRiders` (`@/features/riders/hooks/useRiders`), `useBranches`, `usePermission`, `codErrorMessage` (Task 8), `useCodPanelStore` (Task 9).
- Produces: `createRiderSettlementSchema`/`CreateRiderSettlementFormValues` (`src/web/src/features/cod/schema.ts` — later tasks append their own schemas to this same file). `RiderSettlementsTab`, `RiderSettlementCreateSheet`, `RiderSettlementDetailSheet` components.

- [ ] **Step 1: Write the schema**

`src/web/src/features/cod/schema.ts`:

```typescript
import { z } from "zod";

export const createRiderSettlementSchema = z.object({
  riderId: z.string({ error: "Rider is required." }).min(1, "Rider is required."),
  branchId: z.string({ error: "Branch is required." }).min(1, "Branch is required."),
  declaredAmount: z.coerce.number({ error: "Declared amount is required." }).positive("Declared amount must be greater than zero."),
});

export type CreateRiderSettlementFormValues = z.infer<typeof createRiderSettlementSchema>;
```

- [ ] **Step 2: Write the failing test for the create sheet**

`src/web/src/features/cod/components/RiderSettlementCreateSheet.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", name: "Kathmandu Hub" }] }),
}));
vi.mock("@/features/riders/hooks/useRiders", () => ({
  useRiders: () => ({ data: [{ id: "r1", userId: "u1", baseBranchId: "b1", licenseNumber: "LIC-001", roleType: "DeliveryRider", isActive: true }] }),
}));

const mutateAsync = vi.fn();
vi.mock("@/features/cod/hooks/useCreateRiderSettlement", () => ({
  useCreateRiderSettlement: () => ({ mutateAsync, isPending: false }),
}));

const openRiderSettlementDetail = vi.fn();
vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: "rider-settlement-create" }, close: vi.fn(), openRiderSettlementDetail }),
}));

import { RiderSettlementCreateSheet } from "./RiderSettlementCreateSheet";

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient();
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe("RiderSettlementCreateSheet", () => {
  it("submits the selected branch, rider, and declared amount", async () => {
    mutateAsync.mockResolvedValue({ id: "rs1" });
    renderWithClient(<RiderSettlementCreateSheet />);

    fireEvent.click(screen.getByLabelText("Branch"));
    fireEvent.click(await screen.findByText("Kathmandu Hub"));
    fireEvent.click(screen.getByLabelText("Rider"));
    fireEvent.click(await screen.findByText("LIC-001"));
    fireEvent.change(screen.getByLabelText("Declared Amount"), { target: { value: "2500" } });
    fireEvent.click(screen.getByRole("button", { name: /create settlement/i }));

    await waitFor(() => {
      expect(mutateAsync).toHaveBeenCalledWith({ riderId: "r1", branchId: "b1", declaredAmount: 2500 });
    });
    await waitFor(() => {
      expect(openRiderSettlementDetail).toHaveBeenCalledWith("rs1");
    });
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/web && npm test -- RiderSettlementCreateSheet.test.tsx`
Expected: FAIL — `./RiderSettlementCreateSheet` doesn't exist yet.

- [ ] **Step 4: Build the create sheet**

`src/web/src/features/cod/components/RiderSettlementCreateSheet.tsx`:

```tsx
"use client";

import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { createRiderSettlementSchema, type CreateRiderSettlementFormValues } from "@/features/cod/schema";
import { useCreateRiderSettlement } from "@/features/cod/hooks/useCreateRiderSettlement";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";

export function RiderSettlementCreateSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const openDetail = useCodPanelStore((s) => s.openRiderSettlementDetail);
  const isOpen = panel.mode === "rider-settlement-create";

  const createSettlement = useCreateRiderSettlement();
  const { data: branches } = useBranches();
  const { data: riders } = useRiders();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    handleSubmit,
    control,
    watch,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateRiderSettlementFormValues>({ resolver: zodResolver(createRiderSettlementSchema) });

  const selectedBranchId = watch("branchId");
  const eligibleRiders = riders?.filter((r) => r.isActive && r.baseBranchId === selectedBranchId);

  async function onSubmit(values: CreateRiderSettlementFormValues) {
    setServerError(null);
    try {
      const created = await createSettlement.mutateAsync(values);
      reset();
      openDetail(created.id);
    } catch (error) {
      setServerError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>New Rider Settlement</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="branchId">Branch</Label>
            <Controller
              name="branchId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="branchId" aria-label="Branch">
                    <SelectValue placeholder="Select branch" />
                  </SelectTrigger>
                  <SelectContent>
                    {branches?.map((b) => (
                      <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.branchId && <p className="text-sm text-destructive">{errors.branchId.message}</p>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="riderId">Rider</Label>
            <Controller
              name="riderId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="riderId" aria-label="Rider">
                    <SelectValue placeholder="Select rider" />
                  </SelectTrigger>
                  <SelectContent>
                    {eligibleRiders?.map((r) => (
                      <SelectItem key={r.id} value={r.id}>{r.licenseNumber}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.riderId && <p className="text-sm text-destructive">{errors.riderId.message}</p>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="declaredAmount">Declared Amount</Label>
            <Controller
              name="declaredAmount"
              control={control}
              render={({ field }) => (
                <Input id="declaredAmount" aria-label="Declared Amount" type="number" step="0.01" {...field} />
              )}
            />
            {errors.declaredAmount && <p className="text-sm text-destructive">{errors.declaredAmount.message}</p>}
          </div>
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Settlement"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/web && npm test -- RiderSettlementCreateSheet.test.tsx`
Expected: PASS (1 test)

- [ ] **Step 6: Build the detail sheet**

`src/web/src/features/cod/components/RiderSettlementDetailSheet.tsx`:

```tsx
"use client";

import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { useRiderSettlements } from "@/features/cod/hooks/useRiderSettlements";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { useBranches } from "@/features/branches/hooks/useBranches";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function RiderSettlementDetailSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const isOpen = panel.mode === "rider-settlement-detail";
  const id = panel.mode === "rider-settlement-detail" ? panel.id : "";

  // No GET-by-id endpoint exists for a single RiderCashSettlement (only the list) — the list is
  // already in cache from RiderSettlementsTab's own query in the common case, and refetching the
  // unfiltered first page is a reasonable, cheap fallback otherwise (this list is small in practice:
  // one row per rider per settlement run).
  const { data, isLoading, isError, refetch } = useRiderSettlements({ pageSize: 200 });
  const settlement = data?.items.find((r) => r.id === id);
  const { data: riders } = useRiders();
  const { data: branches } = useBranches();
  const rider = riders?.find((r) => r.id === settlement?.riderId);
  const branch = branches?.find((b) => b.id === settlement?.branchId);

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Rider Settlement</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && (
            <ErrorState title="Couldn't load this settlement" description="Something went wrong." onRetry={() => refetch()} />
          )}
          {!isLoading && !isError && !settlement && (
            <ErrorState title="Settlement not found" description="It may have been reconciled off the current page." onRetry={() => refetch()} />
          )}
          {settlement && (
            <div className="grid grid-cols-2 gap-4">
              <Field label="Rider" value={rider?.licenseNumber ?? settlement.riderId} />
              <Field label="Branch" value={branch?.name ?? settlement.branchId} />
              <Field label="Declared Amount" value={`Rs. ${settlement.declaredAmount.toFixed(2)}`} />
              <Field label="Shipment Count" value={String(settlement.shipmentCount)} />
              <Field label="Settled At" value={new Date(settlement.settledAt).toLocaleString()} />
              <Field label="Reconciled" value={settlement.branchCashSettlementId ? "Yes" : "Not yet"} />
            </div>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 7: Build the tab**

`src/web/src/features/cod/components/RiderSettlementsTab.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useRiderSettlements } from "@/features/cod/hooks/useRiderSettlements";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { usePermission } from "@/features/auth/hooks/usePermission";
import type { RiderCashSettlement } from "@/features/cod/types";

export function RiderSettlementsTab() {
  const [branchId, setBranchId] = useState<string | "all">("all");
  const [unreconciledOnly, setUnreconciledOnly] = useState(false);
  const [page, setPage] = useState(1);
  const pageSize = 20;

  const { data: branches } = useBranches();
  const { data: riders } = useRiders();
  const { data, isLoading } = useRiderSettlements({
    branchId: branchId === "all" ? undefined : branchId,
    unreconciledOnly: unreconciledOnly || undefined,
    page,
    pageSize,
  });
  const openDetail = useCodPanelStore((s) => s.openRiderSettlementDetail);
  const openCreate = useCodPanelStore((s) => s.openRiderSettlementCreate);
  const canReconcile = usePermission("cod.reconcile");

  const branchesById = new Map((branches ?? []).map((b) => [b.id, b.name]));
  const ridersById = new Map((riders ?? []).map((r) => [r.id, r.licenseNumber]));

  const columns: ColumnDef<RiderCashSettlement>[] = [
    {
      key: "riderId",
      header: "Rider",
      render: (r) => (
        <button type="button" onClick={() => openDetail(r.id)} className="font-medium text-foreground hover:underline">
          {ridersById.get(r.riderId) ?? r.riderId}
        </button>
      ),
    },
    { key: "branchId", header: "Branch", render: (r) => branchesById.get(r.branchId) ?? r.branchId },
    { key: "declaredAmount", header: "Declared Amount", render: (r) => `Rs. ${r.declaredAmount.toFixed(2)}` },
    { key: "shipmentCount", header: "Shipment Count", render: (r) => String(r.shipmentCount) },
    { key: "settledAt", header: "Settled At", render: (r) => new Date(r.settledAt).toLocaleDateString() },
    { key: "reconciled", header: "Reconciled", render: (r) => (r.branchCashSettlementId ? "Yes" : "Not yet") },
  ];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-3">
          <Select value={branchId} onValueChange={(v) => { setBranchId(v); setPage(1); }}>
            <SelectTrigger className="w-48" aria-label="Filter by branch">
              <SelectValue placeholder="All branches" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All branches</SelectItem>
              {branches?.map((b) => (
                <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <div className="flex items-center gap-2">
            <Checkbox
              id="unreconciled-only"
              checked={unreconciledOnly}
              onCheckedChange={(checked) => { setUnreconciledOnly(checked === true); setPage(1); }}
            />
            <Label htmlFor="unreconciled-only">Unreconciled only</Label>
          </div>
        </div>
        {canReconcile && <Button type="button" onClick={openCreate}>New Settlement</Button>}
      </div>
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No rider settlements"
        emptyDescription="Rider settlements are created when a rider deposits their collected COD."
        onPageChange={setPage}
        rowKey={(r) => r.id}
      />
    </div>
  );
}
```

- [ ] **Step 8: Wire the tab into the `/cod` page**

In `src/web/src/app/(staff)/cod/page.tsx`, add the import, `TABS` entry, conditional render, and Sheet:

```tsx
import { RiderSettlementsTab } from "@/features/cod/components/RiderSettlementsTab";
import { RiderSettlementCreateSheet } from "@/features/cod/components/RiderSettlementCreateSheet";
import { RiderSettlementDetailSheet } from "@/features/cod/components/RiderSettlementDetailSheet";
```

```tsx
const TABS = [
  { key: "collections", label: "Collections" },
  { key: "rider-settlements", label: "Rider Settlements" },
] as const;
```

```tsx
      {activeTab === "collections" && <CollectionsTab />}
      {activeTab === "rider-settlements" && <RiderSettlementsTab />}

      <CollectionDetailSheet />
      <RiderSettlementCreateSheet />
      <RiderSettlementDetailSheet />
```

- [ ] **Step 9: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS

- [ ] **Step 10: Commit**

```bash
git add src/web/src/features/cod/schema.ts src/web/src/features/cod/components/RiderSettlement*.tsx "src/web/src/app/(staff)/cod/page.tsx"
git commit -m "feat(cod-frontend): add Rider Settlements tab"
```

---

## Task 11: Branch Settlements tab (with the embedded Finance Verify action)

**Files:**
- Create: `src/web/src/features/cod/components/BranchSettlementsTab.tsx`
- Create: `src/web/src/features/cod/components/BranchSettlementCreateSheet.tsx`
- Create: `src/web/src/features/cod/components/BranchSettlementCreateSheet.test.tsx`
- Create: `src/web/src/features/cod/components/BranchSettlementDetailSheet.tsx`
- Modify: `src/web/src/app/(staff)/cod/page.tsx`

**Interfaces:**
- Consumes: `useBranchSettlements`/`useCreateBranchSettlement`/`useRiderSettlements`/`useCreateFinanceVerification` (Tasks 7–8, 10), `useBranches`, `useRiders`, `usePermission`, `codErrorMessage`, `useCodPanelStore`.
- Produces: `BranchSettlementsTab`, `BranchSettlementCreateSheet`, `BranchSettlementDetailSheet` components. No new schema — this create form uses local component state (a rider-settlement multi-select checklist doesn't map cleanly onto one `Controller` per field), matching this codebase's existing precedent (`AssignTaskSheet.tsx`, `ManifestDraftSection.tsx` both use local state instead of `react-hook-form` for interdependent, non-trivial forms).

- [ ] **Step 1: Write the failing test for the create sheet**

`src/web/src/features/cod/components/BranchSettlementCreateSheet.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", name: "Kathmandu Hub" }] }),
}));
vi.mock("@/features/cod/hooks/useRiderSettlements", () => ({
  useRiderSettlements: () => ({
    data: { items: [{ id: "rs1", riderId: "r1", branchId: "b1", declaredAmount: 2500, shipmentCount: 1, settledAt: "2026-09-01T00:00:00Z", confirmedByUserId: "u1", branchCashSettlementId: null }] },
  }),
}));
vi.mock("@/features/riders/hooks/useRiders", () => ({
  useRiders: () => ({ data: [{ id: "r1", userId: "u1", baseBranchId: "b1", licenseNumber: "LIC-001", roleType: "DeliveryRider", isActive: true }] }),
}));

const mutateAsync = vi.fn();
vi.mock("@/features/cod/hooks/useCreateBranchSettlement", () => ({
  useCreateBranchSettlement: () => ({ mutateAsync, isPending: false }),
}));

const openBranchSettlementDetail = vi.fn();
vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: "branch-settlement-create" }, close: vi.fn(), openBranchSettlementDetail }),
}));

import { BranchSettlementCreateSheet } from "./BranchSettlementCreateSheet";

describe("BranchSettlementCreateSheet", () => {
  it("submits the selected branch, checked rider settlements, and confirmed amount", async () => {
    mutateAsync.mockResolvedValue({ id: "bs1" });
    render(<BranchSettlementCreateSheet />);

    fireEvent.click(screen.getByLabelText("Branch"));
    fireEvent.click(await screen.findByText("Kathmandu Hub"));
    fireEvent.click(await screen.findByLabelText(/LIC-001/));
    fireEvent.change(screen.getByLabelText("Confirmed Amount"), { target: { value: "2500" } });
    fireEvent.click(screen.getByRole("button", { name: /create settlement/i }));

    await waitFor(() => {
      expect(mutateAsync).toHaveBeenCalledWith({ branchId: "b1", riderCashSettlementIds: ["rs1"], confirmedAmount: 2500 });
    });
    await waitFor(() => {
      expect(openBranchSettlementDetail).toHaveBeenCalledWith("bs1");
    });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- BranchSettlementCreateSheet.test.tsx`
Expected: FAIL — `./BranchSettlementCreateSheet` doesn't exist yet.

- [ ] **Step 3: Build the create sheet**

`src/web/src/features/cod/components/BranchSettlementCreateSheet.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { useRiderSettlements } from "@/features/cod/hooks/useRiderSettlements";
import { useCreateBranchSettlement } from "@/features/cod/hooks/useCreateBranchSettlement";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";

export function BranchSettlementCreateSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const openDetail = useCodPanelStore((s) => s.openBranchSettlementDetail);
  const isOpen = panel.mode === "branch-settlement-create";

  const [branchId, setBranchId] = useState<string | undefined>(undefined);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [confirmedAmount, setConfirmedAmount] = useState("");
  const [validationError, setValidationError] = useState<string | null>(null);
  const [serverError, setServerError] = useState<string | null>(null);

  const { data: branches } = useBranches();
  const { data: riders } = useRiders();
  const { data: eligible } = useRiderSettlements({ branchId, unreconciledOnly: true, pageSize: 200 });
  const createSettlement = useCreateBranchSettlement();

  const ridersById = new Map((riders ?? []).map((r) => [r.id, r.licenseNumber]));

  function reset() {
    setBranchId(undefined);
    setSelectedIds(new Set());
    setConfirmedAmount("");
    setValidationError(null);
    setServerError(null);
  }

  function toggle(id: string) {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }

  async function handleSubmit() {
    setValidationError(null);
    setServerError(null);
    const amount = Number(confirmedAmount);
    if (!branchId) {
      setValidationError("Branch is required.");
      return;
    }
    if (selectedIds.size === 0) {
      setValidationError("Select at least one rider settlement.");
      return;
    }
    if (!Number.isFinite(amount) || amount <= 0) {
      setValidationError("Confirmed amount must be greater than zero.");
      return;
    }
    try {
      const created = await createSettlement.mutateAsync({
        branchId,
        riderCashSettlementIds: Array.from(selectedIds),
        confirmedAmount: amount,
      });
      reset();
      openDetail(created.id);
    } catch (error) {
      setServerError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { reset(); close(); } }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>New Branch Settlement</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-4">
          <div className="space-y-2">
            <Label htmlFor="branchId">Branch</Label>
            <Select value={branchId} onValueChange={(v) => { setBranchId(v); setSelectedIds(new Set()); }}>
              <SelectTrigger id="branchId" aria-label="Branch">
                <SelectValue placeholder="Select branch" />
              </SelectTrigger>
              <SelectContent>
                {branches?.map((b) => (
                  <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          {branchId && (
            <div className="space-y-2">
              <Label>Unreconciled Rider Settlements</Label>
              <div className="space-y-2 rounded-md border border-border p-3">
                {eligible?.items.length === 0 && (
                  <p className="text-sm text-muted-foreground">No unreconciled rider settlements for this branch.</p>
                )}
                {eligible?.items.map((r) => (
                  <div key={r.id} className="flex items-center gap-2">
                    <Checkbox
                      id={`rider-settlement-${r.id}`}
                      checked={selectedIds.has(r.id)}
                      onCheckedChange={() => toggle(r.id)}
                    />
                    <Label htmlFor={`rider-settlement-${r.id}`}>
                      {ridersById.get(r.riderId) ?? r.riderId} — Rs. {r.declaredAmount.toFixed(2)}
                    </Label>
                  </div>
                ))}
              </div>
            </div>
          )}
          <div className="space-y-2">
            <Label htmlFor="confirmedAmount">Confirmed Amount</Label>
            <Input
              id="confirmedAmount"
              aria-label="Confirmed Amount"
              type="number"
              step="0.01"
              value={confirmedAmount}
              onChange={(e) => setConfirmedAmount(e.target.value)}
            />
          </div>
          {validationError && <p className="text-sm text-destructive">{validationError}</p>}
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="button" disabled={createSettlement.isPending} onClick={handleSubmit}>
            {createSettlement.isPending ? <Spinner className="h-4 w-4" /> : "Create Settlement"}
          </Button>
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- BranchSettlementCreateSheet.test.tsx`
Expected: PASS (1 test)

- [ ] **Step 5: Build the detail sheet with the embedded Finance Verify action**

`src/web/src/features/cod/components/BranchSettlementDetailSheet.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { useBranchSettlements } from "@/features/cod/hooks/useBranchSettlements";
import { useRiderSettlements } from "@/features/cod/hooks/useRiderSettlements";
import { useCreateFinanceVerification } from "@/features/cod/hooks/useCreateFinanceVerification";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function BranchSettlementDetailSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const isOpen = panel.mode === "branch-settlement-detail";
  const id = panel.mode === "branch-settlement-detail" ? panel.id : "";

  const [verifyAmount, setVerifyAmount] = useState("");
  const [serverError, setServerError] = useState<string | null>(null);
  const canVerify = usePermission("cod.settle");

  const { data, isLoading, isError, refetch } = useBranchSettlements({ pageSize: 200 });
  const settlement = data?.items.find((b) => b.id === id);
  const { data: unverified } = useBranchSettlements({ unverifiedOnly: true, pageSize: 200 });
  const isVerified = settlement !== undefined && !unverified?.items.some((b) => b.id === id);
  const { data: riderSettlements } = useRiderSettlements({ pageSize: 200 });
  const { data: riders } = useRiders();
  const { data: branches } = useBranches();
  const branch = branches?.find((b) => b.id === settlement?.branchId);
  const ridersById = new Map((riders ?? []).map((r) => [r.id, r.licenseNumber]));
  const riderSettlementsById = new Map((riderSettlements?.items ?? []).map((r) => [r.id, r]));

  const verifyFinance = useCreateFinanceVerification();

  async function handleVerify() {
    setServerError(null);
    const amount = Number(verifyAmount);
    if (!Number.isFinite(amount) || amount <= 0 || !settlement) {
      setServerError("Amount must be greater than zero.");
      return;
    }
    try {
      await verifyFinance.mutateAsync({ branchCashSettlementId: settlement.id, amount });
      setVerifyAmount("");
    } catch (error) {
      setServerError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { setVerifyAmount(""); setServerError(null); close(); } }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Branch Settlement</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && <ErrorState title="Couldn't load this settlement" description="Something went wrong." onRetry={() => refetch()} />}
          {settlement && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <Field label="Branch" value={branch?.name ?? settlement.branchId} />
                <Field label="Confirmed Amount" value={`Rs. ${settlement.confirmedAmount.toFixed(2)}`} />
                <Field label="Reconciled At" value={new Date(settlement.reconciledAt).toLocaleString()} />
                <Field label="Verified" value={isVerified ? "Yes" : "Not yet"} />
              </div>
              <div className="space-y-2">
                <Label>Covered Rider Settlements</Label>
                <ul className="space-y-1 rounded-md border border-border p-3 text-sm">
                  {settlement.riderCashSettlementIds.map((rsId) => {
                    const riderSettlement = riderSettlementsById.get(rsId);
                    return (
                      <li key={rsId} className="text-foreground">
                        {riderSettlement ? `${ridersById.get(riderSettlement.riderId) ?? riderSettlement.riderId} — Rs. ${riderSettlement.declaredAmount.toFixed(2)}` : rsId}
                      </li>
                    );
                  })}
                </ul>
              </div>
              {!isVerified && canVerify && (
                <div className="space-y-2 rounded-md border border-border p-3">
                  <Label htmlFor="verify-amount">Finance Verify</Label>
                  <Input
                    id="verify-amount"
                    aria-label="Verification Amount"
                    type="number"
                    step="0.01"
                    value={verifyAmount}
                    onChange={(e) => setVerifyAmount(e.target.value)}
                    placeholder="Verified amount"
                  />
                  {serverError && <p className="text-sm text-destructive">{serverError}</p>}
                  <Button type="button" disabled={verifyFinance.isPending} onClick={handleVerify}>
                    {verifyFinance.isPending ? <Spinner className="h-4 w-4" /> : "Verify"}
                  </Button>
                </div>
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 6: Build the tab**

`src/web/src/features/cod/components/BranchSettlementsTab.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useBranchSettlements } from "@/features/cod/hooks/useBranchSettlements";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { usePermission } from "@/features/auth/hooks/usePermission";
import type { BranchCashSettlement } from "@/features/cod/types";

export function BranchSettlementsTab() {
  const [branchId, setBranchId] = useState<string | "all">("all");
  const [unverifiedOnly, setUnverifiedOnly] = useState(false);
  const [page, setPage] = useState(1);
  const pageSize = 20;

  const { data: branches } = useBranches();
  const { data, isLoading } = useBranchSettlements({
    branchId: branchId === "all" ? undefined : branchId,
    unverifiedOnly: unverifiedOnly || undefined,
    page,
    pageSize,
  });
  const openDetail = useCodPanelStore((s) => s.openBranchSettlementDetail);
  const openCreate = useCodPanelStore((s) => s.openBranchSettlementCreate);
  const canReconcile = usePermission("cod.reconcile");

  const branchesById = new Map((branches ?? []).map((b) => [b.id, b.name]));

  const columns: ColumnDef<BranchCashSettlement>[] = [
    {
      key: "branchId",
      header: "Branch",
      render: (b) => (
        <button type="button" onClick={() => openDetail(b.id)} className="font-medium text-foreground hover:underline">
          {branchesById.get(b.branchId) ?? b.branchId}
        </button>
      ),
    },
    { key: "confirmedAmount", header: "Confirmed Amount", render: (b) => `Rs. ${b.confirmedAmount.toFixed(2)}` },
    { key: "riderCount", header: "Rider Settlements", render: (b) => String(b.riderCashSettlementIds.length) },
    { key: "reconciledAt", header: "Reconciled At", render: (b) => new Date(b.reconciledAt).toLocaleDateString() },
  ];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-3">
          <Select value={branchId} onValueChange={(v) => { setBranchId(v); setPage(1); }}>
            <SelectTrigger className="w-48" aria-label="Filter by branch">
              <SelectValue placeholder="All branches" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All branches</SelectItem>
              {branches?.map((b) => (
                <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <div className="flex items-center gap-2">
            <Checkbox
              id="unverified-only"
              checked={unverifiedOnly}
              onCheckedChange={(checked) => { setUnverifiedOnly(checked === true); setPage(1); }}
            />
            <Label htmlFor="unverified-only">Unverified only</Label>
          </div>
        </div>
        {canReconcile && <Button type="button" onClick={openCreate}>New Settlement</Button>}
      </div>
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No branch settlements"
        emptyDescription="Branch settlements are created when a branch reconciles its riders' deposited COD."
        onPageChange={setPage}
        rowKey={(b) => b.id}
      />
    </div>
  );
}
```

- [ ] **Step 7: Wire the tab into the `/cod` page**

In `src/web/src/app/(staff)/cod/page.tsx`, add the import, `TABS` entry, conditional render, and Sheets:

```tsx
import { BranchSettlementsTab } from "@/features/cod/components/BranchSettlementsTab";
import { BranchSettlementCreateSheet } from "@/features/cod/components/BranchSettlementCreateSheet";
import { BranchSettlementDetailSheet } from "@/features/cod/components/BranchSettlementDetailSheet";
```

```tsx
const TABS = [
  { key: "collections", label: "Collections" },
  { key: "rider-settlements", label: "Rider Settlements" },
  { key: "branch-settlements", label: "Branch Settlements" },
] as const;
```

```tsx
      {activeTab === "branch-settlements" && <BranchSettlementsTab />}

      <BranchSettlementCreateSheet />
      <BranchSettlementDetailSheet />
```

- [ ] **Step 8: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add src/web/src/features/cod/components/BranchSettlement*.tsx "src/web/src/app/(staff)/cod/page.tsx"
git commit -m "feat(cod-frontend): add Branch Settlements tab with embedded Finance Verify action"
```

---

## Task 12: Merchant Settlements tab (with Settle and Request Adjustment)

**Files:**
- Modify: `src/web/src/features/cod/schema.ts`
- Create: `src/web/src/features/cod/components/MerchantSettlementsTab.tsx`
- Create: `src/web/src/features/cod/components/MerchantSettlementCreateSheet.tsx`
- Create: `src/web/src/features/cod/components/MerchantSettlementCreateSheet.test.tsx`
- Create: `src/web/src/features/cod/components/MerchantSettlementDetailSheet.tsx`
- Modify: `src/web/src/app/(staff)/cod/page.tsx`

**Interfaces:**
- Consumes: `useMerchantSettlements`/`useMerchantSettlement`/`useCreateMerchantSettlement`/`useSettleMerchantSettlement`/`useRequestAdjustment` (Tasks 7–8), `useMerchants` (`@/features/shipments/hooks/useMerchants`), `usePermission`, `codErrorMessage`, `useCodPanelStore`.
- Produces: `createMerchantSettlementSchema`/`CreateMerchantSettlementFormValues`, `requestAdjustmentSchema`/`RequestAdjustmentFormValues` appended to `src/web/src/features/cod/schema.ts`. `MerchantSettlementsTab`, `MerchantSettlementCreateSheet`, `MerchantSettlementDetailSheet` components.

- [ ] **Step 1: Append the two new schemas**

Append to `src/web/src/features/cod/schema.ts`:

```typescript

export const createMerchantSettlementSchema = z
  .object({
    merchantId: z.string({ error: "Merchant is required." }).min(1, "Merchant is required."),
    periodFrom: z.string({ error: "Period start is required." }).min(1, "Period start is required."),
    periodTo: z.string({ error: "Period end is required." }).min(1, "Period end is required."),
  })
  .refine((values) => new Date(values.periodTo) >= new Date(values.periodFrom), {
    message: "Period end must not be before period start.",
    path: ["periodTo"],
  });

export type CreateMerchantSettlementFormValues = z.infer<typeof createMerchantSettlementSchema>;

export const requestAdjustmentSchema = z.object({
  afterValue: z.coerce.number({ error: "New value is required." }).min(0, "New value cannot be negative."),
  reason: z.string({ error: "Reason is required." }).min(1, "Reason is required — CLAUDE.md §23 requires a reason for every financial adjustment."),
});

export type RequestAdjustmentFormValues = z.infer<typeof requestAdjustmentSchema>;
```

- [ ] **Step 2: Write the failing test for the create sheet**

`src/web/src/features/cod/components/MerchantSettlementCreateSheet.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

vi.mock("@/features/shipments/hooks/useMerchants", () => ({
  useMerchants: () => ({ data: [{ id: "m1", displayName: "Shipper Pvt. Ltd.", isActive: true }] }),
}));

const mutateAsync = vi.fn();
vi.mock("@/features/cod/hooks/useCreateMerchantSettlement", () => ({
  useCreateMerchantSettlement: () => ({ mutateAsync, isPending: false }),
}));

const openMerchantSettlementDetail = vi.fn();
vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: "merchant-settlement-create" }, close: vi.fn(), openMerchantSettlementDetail }),
}));

import { MerchantSettlementCreateSheet } from "./MerchantSettlementCreateSheet";

describe("MerchantSettlementCreateSheet", () => {
  it("submits the selected merchant and period as an ISO date range", async () => {
    mutateAsync.mockResolvedValue({ id: "ms1" });
    render(<MerchantSettlementCreateSheet />);

    fireEvent.click(screen.getByLabelText("Merchant"));
    fireEvent.click(await screen.findByText("Shipper Pvt. Ltd."));
    fireEvent.change(screen.getByLabelText("Period From"), { target: { value: "2026-08-01" } });
    fireEvent.change(screen.getByLabelText("Period To"), { target: { value: "2026-08-31" } });
    fireEvent.click(screen.getByRole("button", { name: /create settlement/i }));

    await waitFor(() => {
      expect(mutateAsync).toHaveBeenCalledWith({ merchantId: "m1", periodFrom: "2026-08-01", periodTo: "2026-08-31" });
    });
    await waitFor(() => {
      expect(openMerchantSettlementDetail).toHaveBeenCalledWith("ms1");
    });
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/web && npm test -- MerchantSettlementCreateSheet.test.tsx`
Expected: FAIL — `./MerchantSettlementCreateSheet` doesn't exist yet.

- [ ] **Step 4: Build the create sheet**

`src/web/src/features/cod/components/MerchantSettlementCreateSheet.tsx`:

```tsx
"use client";

import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { createMerchantSettlementSchema, type CreateMerchantSettlementFormValues } from "@/features/cod/schema";
import { useCreateMerchantSettlement } from "@/features/cod/hooks/useCreateMerchantSettlement";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";

export function MerchantSettlementCreateSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const openDetail = useCodPanelStore((s) => s.openMerchantSettlementDetail);
  const isOpen = panel.mode === "merchant-settlement-create";

  const createSettlement = useCreateMerchantSettlement();
  const { data: merchants } = useMerchants();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    handleSubmit,
    control,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateMerchantSettlementFormValues>({ resolver: zodResolver(createMerchantSettlementSchema) });

  async function onSubmit(values: CreateMerchantSettlementFormValues) {
    setServerError(null);
    try {
      const created = await createSettlement.mutateAsync(values);
      reset();
      openDetail(created.id);
    } catch (error) {
      setServerError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>New Merchant Settlement</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="merchantId">Merchant</Label>
            <Controller
              name="merchantId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="merchantId" aria-label="Merchant">
                    <SelectValue placeholder="Select merchant" />
                  </SelectTrigger>
                  <SelectContent>
                    {merchants?.map((m) => (
                      <SelectItem key={m.id} value={m.id}>{m.displayName}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.merchantId && <p className="text-sm text-destructive">{errors.merchantId.message}</p>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="periodFrom">Period From</Label>
            <Controller
              name="periodFrom"
              control={control}
              render={({ field }) => <Input id="periodFrom" aria-label="Period From" type="date" {...field} />}
            />
            {errors.periodFrom && <p className="text-sm text-destructive">{errors.periodFrom.message}</p>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="periodTo">Period To</Label>
            <Controller
              name="periodTo"
              control={control}
              render={({ field }) => <Input id="periodTo" aria-label="Period To" type="date" {...field} />}
            />
            {errors.periodTo && <p className="text-sm text-destructive">{errors.periodTo.message}</p>}
          </div>
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Settlement"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/web && npm test -- MerchantSettlementCreateSheet.test.tsx`
Expected: PASS (1 test)

- [ ] **Step 6: Build the detail sheet with Settle and per-item Request Adjustment**

`src/web/src/features/cod/components/MerchantSettlementDetailSheet.tsx`:

```tsx
"use client";

import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useMerchantSettlement } from "@/features/cod/hooks/useMerchantSettlement";
import { useSettleMerchantSettlement } from "@/features/cod/hooks/useSettleMerchantSettlement";
import { useRequestAdjustment } from "@/features/cod/hooks/useRequestAdjustment";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";
import { requestAdjustmentSchema, type RequestAdjustmentFormValues } from "@/features/cod/schema";
import type { MerchantSettlementItem } from "@/features/cod/types";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

function AdjustmentForm({ item, onDone }: { item: MerchantSettlementItem; onDone: () => void }) {
  const requestAdjustment = useRequestAdjustment();
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<RequestAdjustmentFormValues>({
    resolver: zodResolver(requestAdjustmentSchema),
    defaultValues: { afterValue: item.otherDeductions },
  });

  async function onSubmit(values: RequestAdjustmentFormValues) {
    setServerError(null);
    try {
      await requestAdjustment.mutateAsync({ entityType: "MerchantSettlementItem", entityId: item.id, ...values });
      onDone();
    } catch (error) {
      setServerError(codErrorMessage(error));
    }
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="mt-2 space-y-2 rounded-md border border-border p-3" noValidate>
      <div className="space-y-1">
        <Label htmlFor={`adjust-value-${item.id}`}>New Other Deductions</Label>
        <Input id={`adjust-value-${item.id}`} aria-label="New Other Deductions" type="number" step="0.01" {...register("afterValue")} />
        {errors.afterValue && <p className="text-sm text-destructive">{errors.afterValue.message}</p>}
      </div>
      <div className="space-y-1">
        <Label htmlFor={`adjust-reason-${item.id}`}>Reason</Label>
        <Input id={`adjust-reason-${item.id}`} aria-label="Reason" {...register("reason")} />
        {errors.reason && <p className="text-sm text-destructive">{errors.reason.message}</p>}
      </div>
      {serverError && <p className="text-sm text-destructive">{serverError}</p>}
      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={isSubmitting}>
          {isSubmitting ? <Spinner className="h-4 w-4" /> : "Request Adjustment"}
        </Button>
        <Button type="button" size="sm" variant="outline" onClick={onDone}>Cancel</Button>
      </div>
    </form>
  );
}

export function MerchantSettlementDetailSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const isOpen = panel.mode === "merchant-settlement-detail";
  const id = panel.mode === "merchant-settlement-detail" ? panel.id : "";

  const [adjustingItemId, setAdjustingItemId] = useState<string | null>(null);
  const [settleError, setSettleError] = useState<string | null>(null);
  const canAdjust = usePermission("cod.adjust");
  const canSettle = usePermission("cod.settle");

  const { data: settlement, isLoading, isError, refetch } = useMerchantSettlement(id);
  const settleMutation = useSettleMerchantSettlement(id);

  async function handleSettle() {
    setSettleError(null);
    try {
      await settleMutation.mutateAsync();
    } catch (error) {
      setSettleError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { setAdjustingItemId(null); close(); } }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-2xl">
        <SheetHeader>
          <SheetTitle>Merchant Settlement</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && <ErrorState title="Couldn't load this settlement" description="Something went wrong." onRetry={() => refetch()} />}
          {settlement && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Status</span>
                  <StatusBadge status={settlement.status} namespace="MerchantSettlement" />
                </div>
                <Field label="Total Payable" value={`Rs. ${settlement.totalPayable.toFixed(2)}`} />
                <Field label="Period From" value={new Date(settlement.periodFrom).toLocaleDateString()} />
                <Field label="Period To" value={new Date(settlement.periodTo).toLocaleDateString()} />
                <Field label="Settled At" value={settlement.settledAt ? new Date(settlement.settledAt).toLocaleString() : "—"} />
              </div>

              <div className="space-y-2">
                <Label>Line Items</Label>
                <div className="space-y-2">
                  {settlement.items.map((item) => (
                    <div key={item.id} className="rounded-md border border-border p-3 text-sm">
                      <div className="flex items-center justify-between">
                        <span className="font-medium text-foreground">Shipment {item.shipmentId.slice(0, 8)}</span>
                        {canAdjust && settlement.status === "Created" && adjustingItemId !== item.id && (
                          <Button type="button" size="sm" variant="outline" onClick={() => setAdjustingItemId(item.id)}>
                            Request Adjustment
                          </Button>
                        )}
                      </div>
                      <div className="mt-1 grid grid-cols-4 gap-2 text-muted-foreground">
                        <span>COD: Rs. {item.codAmount.toFixed(2)}</span>
                        <span>Fee: Rs. {item.courierFee.toFixed(2)}</span>
                        <span>Deductions: Rs. {item.otherDeductions.toFixed(2)}</span>
                        <span>Net: Rs. {item.netPayable.toFixed(2)}</span>
                      </div>
                      {adjustingItemId === item.id && (
                        <AdjustmentForm item={item} onDone={() => setAdjustingItemId(null)} />
                      )}
                    </div>
                  ))}
                </div>
              </div>

              {canSettle && (
                <div className="space-y-2">
                  {settleError && <p className="text-sm text-destructive">{settleError}</p>}
                  <Button type="button" disabled={settlement.status !== "Created" || settleMutation.isPending} onClick={handleSettle}>
                    {settleMutation.isPending ? <Spinner className="h-4 w-4" /> : "Settle"}
                  </Button>
                  {settlement.status !== "Created" && (
                    <p className="text-sm text-muted-foreground">This settlement is already {settlement.status}.</p>
                  )}
                </div>
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 7: Build the tab**

`src/web/src/features/cod/components/MerchantSettlementsTab.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { EmptyState } from "@/components/states/EmptyState";
import { LoadingState } from "@/components/states/LoadingState";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useMerchantSettlements } from "@/features/cod/hooks/useMerchantSettlements";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { usePermission } from "@/features/auth/hooks/usePermission";

// MerchantSettlement's list endpoint is a plain, non-paginated array (Global Constraints) — this
// tab renders the raw Table primitives directly instead of DataTable, which assumes a PagedResult.
export function MerchantSettlementsTab() {
  const [merchantId, setMerchantId] = useState<string | "all">("all");
  const { data: merchants } = useMerchants();
  const { data: settlements, isLoading } = useMerchantSettlements(merchantId === "all" ? undefined : merchantId);
  const openDetail = useCodPanelStore((s) => s.openMerchantSettlementDetail);
  const openCreate = useCodPanelStore((s) => s.openMerchantSettlementCreate);
  const canSettle = usePermission("cod.settle");

  const merchantsById = new Map((merchants ?? []).map((m) => [m.id, m.displayName]));

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Select value={merchantId} onValueChange={setMerchantId}>
          <SelectTrigger className="w-48" aria-label="Filter by merchant">
            <SelectValue placeholder="All merchants" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All merchants</SelectItem>
            {merchants?.map((m) => (
              <SelectItem key={m.id} value={m.id}>{m.displayName}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        {canSettle && <Button type="button" onClick={openCreate}>New Settlement</Button>}
      </div>
      {isLoading && <LoadingState rows={5} />}
      {!isLoading && (settlements?.length ?? 0) === 0 && (
        <EmptyState title="No merchant settlements" description="Create one from finance-verified COD collections for a merchant and period." />
      )}
      {!isLoading && (settlements?.length ?? 0) > 0 && (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Merchant</TableHead>
              <TableHead>Period</TableHead>
              <TableHead>Total Payable</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Settled At</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {settlements!.map((s) => (
              <TableRow key={s.id}>
                <TableCell>
                  <button type="button" onClick={() => openDetail(s.id)} className="font-medium text-foreground hover:underline">
                    {merchantsById.get(s.merchantId) ?? s.merchantId}
                  </button>
                </TableCell>
                <TableCell>{new Date(s.periodFrom).toLocaleDateString()} – {new Date(s.periodTo).toLocaleDateString()}</TableCell>
                <TableCell>Rs. {s.totalPayable.toFixed(2)}</TableCell>
                <TableCell><StatusBadge status={s.status} namespace="MerchantSettlement" /></TableCell>
                <TableCell>{s.settledAt ? new Date(s.settledAt).toLocaleDateString() : "—"}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}
```

- [ ] **Step 8: Wire the tab into the `/cod` page**

In `src/web/src/app/(staff)/cod/page.tsx`, add the import, `TABS` entry, conditional render, and Sheets:

```tsx
import { MerchantSettlementsTab } from "@/features/cod/components/MerchantSettlementsTab";
import { MerchantSettlementCreateSheet } from "@/features/cod/components/MerchantSettlementCreateSheet";
import { MerchantSettlementDetailSheet } from "@/features/cod/components/MerchantSettlementDetailSheet";
```

```tsx
const TABS = [
  { key: "collections", label: "Collections" },
  { key: "rider-settlements", label: "Rider Settlements" },
  { key: "branch-settlements", label: "Branch Settlements" },
  { key: "merchant-settlements", label: "Merchant Settlements" },
] as const;
```

```tsx
      {activeTab === "merchant-settlements" && <MerchantSettlementsTab />}

      <MerchantSettlementCreateSheet />
      <MerchantSettlementDetailSheet />
```

- [ ] **Step 9: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS

- [ ] **Step 10: Commit**

```bash
git add src/web/src/features/cod/schema.ts src/web/src/features/cod/components/MerchantSettlement*.tsx "src/web/src/app/(staff)/cod/page.tsx"
git commit -m "feat(cod-frontend): add Merchant Settlements tab with Settle and Request Adjustment"
```

---

## Task 13: Adjustments tab

**Files:**
- Create: `src/web/src/features/cod/components/AdjustmentsTab.tsx`
- Create: `src/web/src/features/cod/components/AdjustmentDetailSheet.tsx`
- Create: `src/web/src/features/cod/components/AdjustmentDetailSheet.test.tsx`
- Modify: `src/web/src/app/(staff)/cod/page.tsx`

**Interfaces:**
- Consumes: `useAdjustments`/`useApproveAdjustment` (Tasks 7–8), `usePermission`, `codErrorMessage`, `useCodPanelStore`.
- Produces: `AdjustmentsTab`, `AdjustmentDetailSheet` components.

- [ ] **Step 1: Write the failing test for the detail sheet's approve action**

`src/web/src/features/cod/components/AdjustmentDetailSheet.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

vi.mock("@/features/cod/hooks/useAdjustments", () => ({
  useAdjustments: () => ({
    data: {
      items: [{
        id: "a1", entityType: "MerchantSettlementItem", entityId: "item1", beforeValue: 0, afterValue: 100,
        reason: "Damaged parcel deduction", requestedByUserId: "u1", requestedAt: "2026-09-01T00:00:00Z",
        approvedByUserId: null, approvedAt: null, applied: false,
      }],
    },
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
}));

const mutateAsync = vi.fn();
vi.mock("@/features/cod/hooks/useApproveAdjustment", () => ({
  useApproveAdjustment: () => ({ mutateAsync, isPending: false }),
}));

vi.mock("@/features/auth/hooks/usePermission", () => ({
  usePermission: () => true,
}));

vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: "adjustment-detail", id: "a1" }, close: vi.fn() }),
}));

import { AdjustmentDetailSheet } from "./AdjustmentDetailSheet";

describe("AdjustmentDetailSheet", () => {
  it("shows the before/after values and reason, and approves on click", async () => {
    mutateAsync.mockResolvedValue({ id: "a1", applied: true });
    render(<AdjustmentDetailSheet />);

    expect(screen.getByText("Damaged parcel deduction")).toBeInTheDocument();
    expect(screen.getByText(/0.00/)).toBeInTheDocument();
    expect(screen.getByText(/100.00/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /approve/i }));

    await waitFor(() => {
      expect(mutateAsync).toHaveBeenCalled();
    });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- AdjustmentDetailSheet.test.tsx`
Expected: FAIL — `./AdjustmentDetailSheet` doesn't exist yet.

- [ ] **Step 3: Build the detail sheet**

`src/web/src/features/cod/components/AdjustmentDetailSheet.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { useAdjustments } from "@/features/cod/hooks/useAdjustments";
import { useApproveAdjustment } from "@/features/cod/hooks/useApproveAdjustment";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function AdjustmentDetailSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const isOpen = panel.mode === "adjustment-detail";
  const id = panel.mode === "adjustment-detail" ? panel.id : "";

  const [serverError, setServerError] = useState<string | null>(null);
  const canAdjust = usePermission("cod.adjust");

  // No GET-by-id endpoint exists for a single Adjustment (only the list, same shape as
  // RiderCashSettlement/BranchCashSettlement) — AdjustmentResponse has no field the list shape
  // omits, so finding the row in a cached, generously-paged list is sufficient.
  const { data, isLoading, isError, refetch } = useAdjustments({ pageSize: 200 });
  const adjustment = data?.items.find((a) => a.id === id);
  const approveMutation = useApproveAdjustment(id);

  async function handleApprove() {
    setServerError(null);
    try {
      await approveMutation.mutateAsync();
    } catch (error) {
      setServerError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { setServerError(null); close(); } }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-lg">
        <SheetHeader>
          <SheetTitle>Adjustment</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && <ErrorState title="Couldn't load this adjustment" description="Something went wrong." onRetry={() => refetch()} />}
          {!isLoading && !isError && !adjustment && (
            <ErrorState title="Adjustment not found" description="It may be outside the current page." onRetry={() => refetch()} />
          )}
          {adjustment && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <Field label="Entity Type" value={adjustment.entityType} />
                <Field label="Before Value" value={`Rs. ${adjustment.beforeValue.toFixed(2)}`} />
                <Field label="After Value" value={`Rs. ${adjustment.afterValue.toFixed(2)}`} />
                <Field label="Requested At" value={new Date(adjustment.requestedAt).toLocaleString()} />
              </div>
              <div className="space-y-1">
                <span className="text-xs text-muted-foreground">Reason</span>
                <p className="rounded-md border border-border p-3 text-sm text-foreground">{adjustment.reason}</p>
              </div>
              {adjustment.applied ? (
                <p className="text-sm text-muted-foreground">
                  Approved {adjustment.approvedAt ? new Date(adjustment.approvedAt).toLocaleString() : ""}.
                </p>
              ) : (
                canAdjust && (
                  <div className="space-y-2">
                    {serverError && <p className="text-sm text-destructive">{serverError}</p>}
                    <Button type="button" disabled={approveMutation.isPending} onClick={handleApprove}>
                      {approveMutation.isPending ? <Spinner className="h-4 w-4" /> : "Approve"}
                    </Button>
                  </div>
                )
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- AdjustmentDetailSheet.test.tsx`
Expected: PASS (1 test)

- [ ] **Step 5: Build the tab**

`src/web/src/features/cod/components/AdjustmentsTab.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useAdjustments } from "@/features/cod/hooks/useAdjustments";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import type { Adjustment } from "@/features/cod/types";

export function AdjustmentsTab() {
  const [applied, setApplied] = useState<"all" | "pending" | "applied">("all");
  const [page, setPage] = useState(1);
  const pageSize = 20;

  const { data, isLoading } = useAdjustments({
    applied: applied === "all" ? undefined : applied === "applied",
    page,
    pageSize,
  });
  const openDetail = useCodPanelStore((s) => s.openAdjustmentDetail);

  const columns: ColumnDef<Adjustment>[] = [
    {
      key: "entityType",
      header: "Entity Type",
      render: (a) => (
        <button type="button" onClick={() => openDetail(a.id)} className="font-medium text-foreground hover:underline">
          {a.entityType}
        </button>
      ),
    },
    { key: "beforeValue", header: "Before Value", render: (a) => `Rs. ${a.beforeValue.toFixed(2)}` },
    { key: "afterValue", header: "After Value", render: (a) => `Rs. ${a.afterValue.toFixed(2)}` },
    { key: "reason", header: "Reason", render: (a) => a.reason },
    { key: "requestedAt", header: "Requested At", render: (a) => new Date(a.requestedAt).toLocaleDateString() },
    { key: "applied", header: "Status", render: (a) => (a.applied ? "Applied" : "Pending") },
  ];

  return (
    <div className="space-y-4">
      <Select value={applied} onValueChange={(v) => { setApplied(v as "all" | "pending" | "applied"); setPage(1); }}>
        <SelectTrigger className="w-48" aria-label="Filter by status">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">All</SelectItem>
          <SelectItem value="pending">Pending</SelectItem>
          <SelectItem value="applied">Applied</SelectItem>
        </SelectContent>
      </Select>
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No adjustments"
        emptyDescription="Financial adjustments to merchant settlement line items will show up here."
        onPageChange={setPage}
        rowKey={(a) => a.id}
      />
    </div>
  );
}
```

- [ ] **Step 6: Wire the tab into the `/cod` page**

In `src/web/src/app/(staff)/cod/page.tsx`, add the import, `TABS` entry, conditional render, and Sheet:

```tsx
import { AdjustmentsTab } from "@/features/cod/components/AdjustmentsTab";
import { AdjustmentDetailSheet } from "@/features/cod/components/AdjustmentDetailSheet";
```

```tsx
const TABS = [
  { key: "collections", label: "Collections" },
  { key: "rider-settlements", label: "Rider Settlements" },
  { key: "branch-settlements", label: "Branch Settlements" },
  { key: "merchant-settlements", label: "Merchant Settlements" },
  { key: "adjustments", label: "Adjustments" },
] as const;
```

```tsx
      {activeTab === "adjustments" && <AdjustmentsTab />}

      <AdjustmentDetailSheet />
```

- [ ] **Step 7: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/web/src/features/cod/components/Adjustments*.tsx src/web/src/features/cod/components/AdjustmentDetailSheet* "src/web/src/app/(staff)/cod/page.tsx"
git commit -m "feat(cod-frontend): add Adjustments tab"
```

---

## Task 14: Reconciliation tab and `Cod.*`/`MerchantSettlement.*` status badges

**Files:**
- Modify: `src/web/src/components/status-badge/statusConfig.ts`
- Create: `src/web/src/features/cod/components/ReconciliationTab.tsx`
- Modify: `src/web/src/app/(staff)/cod/page.tsx`

**Interfaces:**
- Consumes: `useCodReconciliation` (Task 7), `useBranches`, `useMerchants`, `resolveStatusConfig`/`STATUS_CONFIG` (`@/components/status-badge/statusConfig`).
- Produces: 7 new `"Cod.*"` and 2 new `"MerchantSettlement.*"` `STATUS_CONFIG` entries (retroactively making every `StatusBadge status={...} namespace="Cod"` / `namespace="MerchantSettlement"` call from Tasks 9–13 resolve to a real label/color instead of the `resolveStatusConfig` fallback). `ReconciliationTab` component.

- [ ] **Step 1: Add the new `STATUS_CONFIG` entries**

In `src/web/src/components/status-badge/statusConfig.ts`, append inside the `STATUS_CONFIG` object, after the existing `"Ndr.Resolved"` entry:

```typescript
  // Cod collection statuses (COD-LIFECYCLE.md §1's full seven-state chain) — namespaced ("Cod.X")
  // for the same reason "Ndr.X"/"Manifest.X" are: a flat key risks collision, here specifically
  // between the two Settled members below and CodCollectionStatus's own Settled. This is the
  // longest status chain of any namespace so far (7 states vs. Ndr's 6), so it reuses more tokens
  // than F6 needed and was validated as its own group via the dataviz skill's palette validator
  // (Frontend Phase F7) — see this file's own inline validation note pattern from the Ndr entries
  // above if a future phase's tokens need re-checking.
  "Cod.CodExpected": { label: "Expected", token: "status-created" },
  "Cod.CodCollected": { label: "Collected", token: "status-out-for-delivery" },
  "Cod.RiderDeposited": { label: "Rider Deposited", token: "status-in-transit" },
  "Cod.BranchReconciled": { label: "Branch Reconciled", token: "status-sealed" },
  "Cod.FinanceVerified": { label: "Finance Verified", token: "status-received" },
  "Cod.MerchantSettlementCreated": { label: "Settlement Created", token: "status-ndr" },
  "Cod.Settled": { label: "Settled", token: "status-delivered" },

  // MerchantSettlementStatus — its own namespace (not "Cod.X") because its Settled member would
  // otherwise collide with CodCollectionStatus.Settled's key immediately above under one flat
  // Record.
  "MerchantSettlement.Created": { label: "Created", token: "status-created" },
  "MerchantSettlement.Settled": { label: "Settled", token: "status-delivered" },
```

- [ ] **Step 2: Validate the new token group**

Invoke the `dataviz` skill and ask it to validate exactly the 7 `Cod.*` tokens above as one set (not the whole app palette, which earlier phases already validated) against the normal-vision adjacent-pair floor and the CVD-specific floor, the same way F6's `Ndr.*` group was validated. If any pair fails, substitute a different already-existing token (do not mint a new CSS custom property) and update the mapping above accordingly, following the same "known debt, not blocking" precedent `statusConfig.ts` already documents for the `Ndr.Resolved`/`Ndr.RtoRequested` pair if no clean substitution exists.

- [ ] **Step 3: Build the Reconciliation tab**

`src/web/src/features/cod/components/ReconciliationTab.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Banknote, Wallet, Truck, Building2, HandCoins, Receipt } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { useCodReconciliation } from "@/features/cod/hooks/useCodReconciliation";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";

function formatAmount(value: number): string {
  return `Rs. ${value.toFixed(2)}`;
}

export function ReconciliationTab() {
  const [branchId, setBranchId] = useState<string | "all">("all");
  const [merchantId, setMerchantId] = useState<string | "all">("all");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  const { data: branches } = useBranches();
  const { data: merchants } = useMerchants();
  const { data: summary, isLoading } = useCodReconciliation({
    branchId: branchId === "all" ? undefined : branchId,
    merchantId: merchantId === "all" ? undefined : merchantId,
    from: from || undefined,
    to: to || undefined,
  });

  const tiles = summary
    ? [
        { label: "Expected COD", value: formatAmount(summary.expectedCod), icon: Wallet, badgeClass: "bg-status-created/15 text-status-created" },
        { label: "Collected COD", value: formatAmount(summary.collectedCod), icon: Banknote, badgeClass: "bg-status-out-for-delivery/15 text-status-out-for-delivery" },
        { label: "Rider-Deposited COD", value: formatAmount(summary.riderDepositedCod), icon: Truck, badgeClass: "bg-status-in-transit/15 text-status-in-transit" },
        { label: "Branch-Received COD", value: formatAmount(summary.branchReceivedCod), icon: Building2, badgeClass: "bg-status-sealed/15 text-status-sealed" },
        { label: "Merchant-Payable COD", value: formatAmount(summary.merchantPayableCod), icon: HandCoins, badgeClass: "bg-status-ndr/15 text-status-ndr" },
        { label: "Total Courier Fees", value: formatAmount(summary.totalCourierFees), icon: Receipt, badgeClass: "bg-muted text-muted-foreground" },
      ]
    : [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <Label htmlFor="recon-branch">Branch</Label>
          <Select value={branchId} onValueChange={setBranchId}>
            <SelectTrigger id="recon-branch" className="w-48" aria-label="Filter by branch">
              <SelectValue placeholder="All branches" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All branches</SelectItem>
              {branches?.map((b) => (
                <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="recon-merchant">Merchant</Label>
          <Select value={merchantId} onValueChange={setMerchantId}>
            <SelectTrigger id="recon-merchant" className="w-48" aria-label="Filter by merchant">
              <SelectValue placeholder="All merchants" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All merchants</SelectItem>
              {merchants?.map((m) => (
                <SelectItem key={m.id} value={m.id}>{m.displayName}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="recon-from">From</Label>
          <Input id="recon-from" aria-label="From date" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="recon-to">To</Label>
          <Input id="recon-to" aria-label="To date" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </div>
      </div>

      {isLoading && (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      )}

      {summary && (
        <Card>
          <CardHeader>
            <CardTitle className="font-display text-base">COD Reconciliation</CardTitle>
          </CardHeader>
          <CardContent className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {tiles.map((tile) => {
              const Icon = tile.icon;
              return (
                <div key={tile.label} className="flex items-center gap-3 rounded-lg border border-border p-4">
                  <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full ${tile.badgeClass}`}>
                    <Icon className="h-5 w-5" strokeWidth={2} />
                  </span>
                  <div className="flex flex-col gap-0.5">
                    <span className="text-xs text-muted-foreground">{tile.label}</span>
                    <span className="font-sans text-lg font-semibold text-foreground">{tile.value}</span>
                  </div>
                </div>
              );
            })}
          </CardContent>
        </Card>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Wire the tab into the `/cod` page**

In `src/web/src/app/(staff)/cod/page.tsx`, add the import and final `TABS` entry:

```tsx
import { ReconciliationTab } from "@/features/cod/components/ReconciliationTab";
```

```tsx
const TABS = [
  { key: "collections", label: "Collections" },
  { key: "rider-settlements", label: "Rider Settlements" },
  { key: "branch-settlements", label: "Branch Settlements" },
  { key: "merchant-settlements", label: "Merchant Settlements" },
  { key: "adjustments", label: "Adjustments" },
  { key: "reconciliation", label: "Reconciliation" },
] as const;
```

```tsx
      {activeTab === "reconciliation" && <ReconciliationTab />}
```

(No new Sheet — this tab has no create/detail view.)

- [ ] **Step 5: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS — and every `StatusBadge` rendered by Tasks 9–13 now shows a real label/color instead of the `resolveStatusConfig` fallback (`{ label: status, token: "status-cancelled" }`).

- [ ] **Step 6: Commit**

```bash
git add src/web/src/components/status-badge/statusConfig.ts src/web/src/features/cod/components/ReconciliationTab.tsx "src/web/src/app/(staff)/cod/page.tsx"
git commit -m "feat(cod-frontend): add Reconciliation tab and Cod/MerchantSettlement status badges"
```

---

## Task 15: Manual E2E verification and `FRONTEND-ROADMAP.md` completion

**Files:**
- Modify: `docs/FRONTEND-ROADMAP.md`

**Interfaces:**
- Consumes: nothing new — this task exercises Tasks 1–14's full stack end-to-end against the real API and Postgres, per this project's established practice that every prior phase's own manual pass has found real bugs invisible to unit tests alone.

- [ ] **Step 1: Start the backend and frontend**

Ensure `courier-postgres-1`, `courier-redis-1`, `courier-rabbitmq-1` are running (`docker start courier-postgres-1 courier-redis-1 courier-rabbitmq-1` if not — do not `docker compose up` a second stack, per `[[Courier Local Postgres]]`). Run the API (`dotnet run --project src/Api`) and the frontend (`cd src/web && npm run dev`).

- [ ] **Step 2: Register a fresh org and seed data via the API**

Using the same request shapes `CodEndpointsTests.cs` already uses (`RegisterOrganizationRequest`, branch/zone/rate-card creation, a merchant, a pickup rider and a delivery rider), register a new organization and seed: two branches (origin + destination) with delivery zones and a rate card covering them, a merchant, and one pickup rider + one delivery rider at the respective branches. Log in to the staff web UI as the registered owner.

- [ ] **Step 3: Book and deliver a COD shipment through the UI's existing F3/F5 flows**

Book a shipment for the seeded merchant with `CodAmount` set (e.g. 2500), origin/destination as seeded. Walk it through pickup → origin receive → manifest dispatch/receive (F4) → delivery assignment → complete delivery with `collectedCodAmount` matching (F5's existing rider delivery flow). Confirm in the **Collections** tab that a `CodCollection` row exists, initially at `CodExpected`, then flips to `CodCollected` (Rs. 2500.00) once delivery completes.

- [ ] **Step 4: Walk the COD chain through the new UI**

In the **Rider Settlements** tab, create a settlement for the delivery rider/destination branch (declared amount 2500) — confirm the Collections tab row flips to `RiderDeposited`. In the **Branch Settlements** tab, create a settlement selecting that rider settlement (confirmed amount 2500) — confirm `BranchReconciled`. Open the new branch settlement's detail Sheet and use the embedded **Finance Verify** action (amount 2500) — confirm `FinanceVerified` and that the branch settlement's "Verified" field now reads "Yes" on reopen.

- [ ] **Step 5: Merchant settlement, adjustment, and settle**

In the **Merchant Settlements** tab, create a settlement for the merchant covering today's date range — confirm one line item appears with the expected `CODAmount`/`CourierFee`/`NetPayable`, and the Collections tab row flips to `MerchantSettlementCreated`. Open the line item's **Request Adjustment** form, set a small `OtherDeductions` value with a reason, submit — confirm it appears in the **Adjustments** tab as Pending. Approve it from the Adjustments tab — confirm the Merchant Settlement's `TotalPayable`/line item's `NetPayable` update to reflect the deduction. Click **Settle** on the merchant settlement — confirm `Settled` and the Collections tab row flips to `Settled`. Attempt to request a second adjustment against this now-settled item and confirm it's still allowed to *request* (the guard is on *approve*, not *request* — `RequestAdjustmentAsync` has no settle-check per the spec); then approve it and confirm the new guard rejects it with the `codErrorMessage`-surfaced text visible in the Sheet.

- [ ] **Step 6: Reconciliation tab**

Open the **Reconciliation** tab with no filters — confirm the six figures match the shipment's actual numbers (Expected/Collected/Rider-Deposited/Branch-Received all Rs. 2500.00 pre-adjustment figures as applicable, Merchant-Payable reflecting the approved deduction, Total Courier Fees equal to the shipment's locked-in `TotalCharge`). Filter by the seeded branch and merchant and confirm the same figures persist (single-shipment scenario). Filter by a date range excluding today and confirm all figures drop to zero except any status still legitimately outside the range per `GetReconciliationSummaryAsync`'s null-`CollectedAt` inclusion rule.

- [ ] **Step 7: Branch-scoping check**

Provision a second staff user restricted to a branch other than the ones seeded above (reuse the provisioning pattern from `IUserProvisioningService`/an existing branch-restricted role assignment, or simply create a second branch and a `BranchManager`-equivalent role scoped only to it). Log in as that user and confirm the Rider Settlements and Branch Settlements tabs show no rows from the seeded branches, and that opening either seeded branch's settlement detail directly (if a stale link exists) shows the "not found" state rather than leaking data.

- [ ] **Step 8: Fix any real bugs found**

If Step 3–7 surfaces a real defect (missing invalidation, a wrong filter, a serialization gap), fix it now with a small targeted commit before proceeding — do not defer a bug found during this phase's own E2E pass, matching every prior phase's practice.

- [ ] **Step 9: Update `docs/FRONTEND-ROADMAP.md`**

Replace the Phase F7 section's `*Status: Not started.*` line:

```markdown
*Status: Complete.* Shipped: `CollectionsTab`, `RiderSettlementsTab` (+create/detail Sheets),
`BranchSettlementsTab` (+create/detail Sheet with an embedded Finance Verify action, since no
`FinancialTransaction` list endpoint exists), `MerchantSettlementsTab` (+create/detail Sheet with
Settle and per-line-item Request Adjustment), `AdjustmentsTab` (+detail Sheet with Approve), and a
`ReconciliationTab` surfacing CLAUDE.md §22–23's six summary figures — all under one `/cod` nav
entry with a plain button-row tab switcher (no new Tabs library). 4 backend gaps closed first: 3
Cod enums missing `JsonStringEnumConverter`, no list endpoints for `RiderCashSettlement`/
`BranchCashSettlement`/`Adjustment` (flagged in Phase 10's own final review as worth surfacing
here), and a missing guard letting `ApproveAdjustmentAsync` silently rewrite `TotalPayable` on an
already-`Settled` `MerchantSettlement`. See `docs/CLARIFICATIONS.md` entry #17 and
`docs/superpowers/specs/2026-09-01-frontend-phase-f7-cod-operations-design.md` for full detail.
```

- [ ] **Step 10: Commit**

```bash
git add docs/FRONTEND-ROADMAP.md
git commit -m "docs: mark Frontend Phase F7 (COD Operations) complete"
```

---

## Final Step: Whole-branch review

Once all 15 tasks are committed, request a whole-branch review against this plan and the spec at `docs/superpowers/specs/2026-09-01-frontend-phase-f7-cod-operations-design.md` before merging — matching every prior phase's practice of a final review pass catching cross-task defects invisible to any single task's own review (e.g. Phase 13's wrong-formula-plus-matching-wrong-test class of bug, Phase 10's recurring missing-`OrganizationId`-filter class of bug). Pay particular attention to:
- Every new list/read method in `CodService.Reads.cs` filtering by `OrganizationId` (this project's most recurring bug class, hit in Phases 4/5/10/12).
- The `ApproveAdjustmentAsync` refactor in Task 5 not having silently reintroduced the pre-guard duplicate-fetch pattern or broken the happy path.
- Every mutation hook's cross-invalidation actually covering `["cod", "collections"]` where the spec requires it (Task 8).
- The `/cod` page.tsx's incremental `TABS` edits across Tasks 9–14 not having collided (each task only appends, never reorders or removes another task's entry).

