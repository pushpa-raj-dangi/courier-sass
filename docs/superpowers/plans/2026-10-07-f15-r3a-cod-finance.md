# F15 R3a — COD Finance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the staff `/cod` page around the design canvas `Cod` artboard — money-flow strip, rider deposits with server-computed variance, record-deposit Sheet, reconciling merchant settlement breakdown — backed by a small read-only Cod API slice.

**Architecture:** Backend adds two read endpoints (`/cod/flow`, `/cod/rider-cash`), a date filter and `expectedAmount`/`variance` on rider settlements, and a `riderCashSettlementId` filter on collections — all in the existing `CodService` partials, branch-scoped through `IBranchAuthorizationChecker`. Web adds pure model files under `features/cod/lib/`, thin TanStack hooks + Next proxy routes, and rewrites the page shell, rider-deposit tab and two Sheets; R2 dashboard cards consume the new reads.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core (InMemory unit tests + Postgres API tests), xUnit; Next.js App Router, TypeScript, TanStack Query, Zustand, React Hook Form + Zod, shadcn/ui, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-10-07-f15-r3a-cod-finance-design.md`

## Global Constraints

- No migrations, no new entities, no new write endpoints, no change to existing write behaviour, no new npm or NuGet dependencies.
- Every number shown is server data; loading and error states never render `0` — use skeletons, `ErrorState`, or "—".
- Data the API does not serve renders `ComingSoonSlot` (`@/components/page/ComingSoonSlot`) or a disabled control labelled "Needs backend".
- `/cod/flow` and `/cod/rider-cash` are staff-only: merchant callers get **403**. All Cod reads stay tenant-filtered and branch-scoped via `GetAccessibleBranchIdsAsync(user, "cod.read")`; a `branchId` filter only narrows.
- Date filters (`from`/`to`) are normalised to UTC; `from > to` → 400 `COD_VALIDATION_FAILED`. Web sends UTC-anchored strings `YYYY-MM-DDT00:00:00Z` / `YYYY-MM-DDT23:59:59Z` (`toUtcRange` in `@/lib/dates/rangePresets`).
- Money renders as `Rs 12,48,500` (en-IN grouping) via `formatRs`; variance text is always present ("Matched", "Short − Rs 500", "Over + Rs 200", minus sign U+2212); colour is secondary.
- Detail/create views are right-side Sheets driven by `useCodPanelStore` — never routed pages.
- shadcn/ui components from `@/components/ui/*` only (Tabs, Select, Table, Button, Input, Label, Skeleton, Spinner, Sheet, Tooltip).
- Use Tailwind type-ramp classes (`text-xs`, `text-sm`, `text-base`, `text-lg`) — no arbitrary `text-[13px]`-style sizes in new code.
- Permissions: page reads `cod.read`; Record deposit `cod.reconcile`; Mark settled `cod.settle`; Request adjustment `cod.adjust` — hidden when missing. Never role-name checks.
- Web checks: `npm run build`, `npm run lint`, `npm test` pass; `npx tsc --noEmit` shows no errors beyond the 8 pre-existing ones on master. Backend: `dotnet build` and `dotnet test` pass (Api.Tests need courier-postgres-1, courier-redis-1, courier-rabbitmq-1, courier-mailhog running — `docker start` them; never `docker compose up` a second Postgres).
- Never assert on ICU month text in tests; compute expected strings with the same formatter.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` verbatim.

## Review Focus

1. A rider settlement with no linked collections (legacy row) → `expectedAmount` 0, `variance` = declared, no exception — pinned in Task 1.
2. `/cod/flow` for a scope with no COD rows (or an out-of-scope `branchId`) → all six stages present with 0 amount/count, not a missing array or 500 — pinned in Task 2.
3. Record deposit with blank, zero, negative or 3-decimal "Cash received" → inline validation, no request sent — pinned in Task 5.
4. A holding row whose rider name can't be resolved (viewer lacks `user.read`) → shows "Rider", still has a working Record deposit action — pinned in Task 5.
5. A merchant settlement with zero items → breakdown all zero, no mismatch warning, no `NaN` — pinned in Task 3.

---

### Task 1: Rider settlement `expectedAmount`/`variance`, date filter, collections settlement filter (backend)

**Files:**
- Modify: `src/Modules/Cod/Application/CodDtos.cs`
- Modify: `src/Modules/Cod/Application/CodService.cs`
- Modify: `src/Modules/Cod/Application/CodService.Settlements.cs`
- Modify: `src/Modules/Cod/Application/CodService.Reads.cs`
- Modify: `src/Modules/Cod/Presentation/CodEndpoints.cs`
- Modify: `tests/Cod.Tests/CodServiceRiderSettlementsListTests.cs`
- Modify: every existing caller of `ListCollectionsAsync(` / `ListRiderSettlementsAsync(` under `tests/` (find with `grep -rn "ListCollectionsAsync(\|ListRiderSettlementsAsync(" tests --include=*.cs`)
- Modify: `tests/Api.Tests/CodEndpointsTests.cs`

**Interfaces:**
- Produces: `RiderCashSettlementResponse(..., Guid? BranchCashSettlementId, decimal ExpectedAmount, decimal Variance)`; `ListRiderSettlementsAsync(Guid? branchId, Guid? riderId, bool? unreconciledOnly, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken)`; `ListCollectionsAsync(CodCollectionStatus? status, Guid? branchId, Guid? merchantId, Guid? riderCashSettlementId, int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken)`; private helper `CodService.NormalizeRange(DateTimeOffset? from, DateTimeOffset? to)` (used again by Task 2). HTTP: `GET /api/v1/cod/rider-settlements?from=&to=`, `GET /api/v1/cod/collections?riderCashSettlementId=`.

- [ ] **Step 1: Write the failing unit tests**

In `tests/Cod.Tests/CodServiceRiderSettlementsListTests.cs`:

1. Add `modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());` inside `TestDbContext.OnModelCreating` (keep the existing `RiderCashSettlementConfiguration` line).
2. Change `SeedAsync` to accept an optional settled time and declared amount:

```csharp
    private static async Task<RiderCashSettlement> SeedAsync(TestDbContext dbContext, Guid organizationId, Guid riderId, Guid branchId, Guid? branchCashSettlementId = null, DateTimeOffset? settledAt = null, decimal declaredAmount = 2500m)
    {
        var settlement = new RiderCashSettlement
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, RiderId = riderId, BranchId = branchId,
            DeclaredAmount = declaredAmount, ShipmentCount = 1, SettledAt = settledAt ?? DateTimeOffset.UtcNow,
            ConfirmedByUserId = Guid.NewGuid(), BranchCashSettlementId = branchCashSettlementId
        };
        dbContext.Set<RiderCashSettlement>().Add(settlement);
        await dbContext.SaveChangesAsync();
        return settlement;
    }

    private static async Task SeedLinkedCollectionAsync(TestDbContext dbContext, Guid organizationId, Guid branchId, Guid riderCashSettlementId, decimal collectedAmount)
    {
        dbContext.Set<CODCollection>().Add(new CODCollection
        {
            Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = Guid.NewGuid(),
            BranchId = branchId, ExpectedAmount = collectedAmount, ShipmentTotalCharge = 150m, CollectedAmount = collectedAmount,
            CollectedAt = DateTimeOffset.UtcNow, Status = CodCollectionStatus.RiderDeposited, RiderCashSettlementId = riderCashSettlementId
        });
        await dbContext.SaveChangesAsync();
    }
```

3. Update the four existing `ListRiderSettlementsAsync(` calls in this file to pass two extra `null` arguments after the third argument (e.g. `ListRiderSettlementsAsync(null, null, true, null, null, null, null, ActingUser(...))`).
4. Append these tests to the class:

```csharp
    [Fact]
    public async Task ListRiderSettlementsAsync_ComputesExpectedAndVariance()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var exact = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), branchId, declaredAmount: 3000m);
        await SeedLinkedCollectionAsync(dbContext, organizationId, branchId, exact.Id, 1000m);
        await SeedLinkedCollectionAsync(dbContext, organizationId, branchId, exact.Id, 2000m);
        var shortSettlement = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), branchId, declaredAmount: 2000m);
        await SeedLinkedCollectionAsync(dbContext, organizationId, branchId, shortSettlement.Id, 2500m);
        var over = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), branchId, declaredAmount: 2700m);
        await SeedLinkedCollectionAsync(dbContext, organizationId, branchId, over.Id, 2500m);
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListRiderSettlementsAsync(null, null, null, null, null, null, null, ActingUser(Guid.NewGuid()));

        var byId = result.Items.ToDictionary(r => r.Id);
        Assert.Equal(3000m, byId[exact.Id].ExpectedAmount);
        Assert.Equal(0m, byId[exact.Id].Variance);
        Assert.Equal(2500m, byId[shortSettlement.Id].ExpectedAmount);
        Assert.Equal(-500m, byId[shortSettlement.Id].Variance);
        Assert.Equal(200m, byId[over.Id].Variance);
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_SettlementWithNoLinkedCollections_ExpectedIsZero()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var legacy = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), Guid.NewGuid(), declaredAmount: 1200m);
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());

        var result = await service.ListRiderSettlementsAsync(null, null, null, null, null, null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(legacy.Id, single.Id);
        Assert.Equal(0m, single.ExpectedAmount);
        Assert.Equal(1200m, single.Variance);
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_FromTo_FiltersOnSettledAt_NormalisedToUtc()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var inRange = await SeedAsync(dbContext, organizationId, Guid.NewGuid(), branchId, settledAt: new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        await SeedAsync(dbContext, organizationId, Guid.NewGuid(), branchId, settledAt: new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(dbContext, organizationId, new AllBranchesAuthorizationChecker());
        var nepal = TimeSpan.FromMinutes(345);

        var result = await service.ListRiderSettlementsAsync(null, null, null,
            new DateTimeOffset(2026, 10, 5, 5, 45, 0, nepal), new DateTimeOffset(2026, 10, 6, 5, 44, 59, nepal),
            null, null, ActingUser(Guid.NewGuid()));

        var single = Assert.Single(result.Items);
        Assert.Equal(inRange.Id, single.Id);
    }

    [Fact]
    public async Task ListRiderSettlementsAsync_FromAfterTo_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), new AllBranchesAuthorizationChecker());

        await Assert.ThrowsAsync<CodValidationException>(() => service.ListRiderSettlementsAsync(null, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1), null, null, ActingUser(Guid.NewGuid())));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Cod.Tests --filter "FullyQualifiedName~CodServiceRiderSettlementsListTests"`
Expected: build FAIL (no overload with 8 parameters / `ExpectedAmount` not defined).

- [ ] **Step 3: Implement**

`CodDtos.cs` — replace the `RiderCashSettlementResponse` record:

```csharp
public record RiderCashSettlementResponse(
    Guid Id, Guid RiderId, Guid BranchId, decimal DeclaredAmount, int ShipmentCount,
    DateTimeOffset SettledAt, Guid ConfirmedByUserId, Guid? BranchCashSettlementId,
    decimal ExpectedAmount, decimal Variance);
```

`CodService.cs` — add below `CheckBranchAccessAsync`:

```csharp
    // R3a: every Cod date filter is normalised to UTC (an offset like +05:45 must never reach
    // Npgsql's timestamptz as a non-UTC value) and an inverted range is a 400, never a 500.
    private static (DateTimeOffset? From, DateTimeOffset? To) NormalizeRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        var fromUtc = from?.ToUniversalTime();
        var toUtc = to?.ToUniversalTime();
        if (fromUtc is DateTimeOffset f && toUtc is DateTimeOffset t && f > t)
        {
            throw new CodValidationException("'from' must be on or before 'to'.");
        }
        return (fromUtc, toUtc);
    }
```

`CodService.Settlements.cs` — replace `ToRiderSettlementResponse` and its call in `CreateRiderSettlementAsync`:

```csharp
    private static RiderCashSettlementResponse ToRiderSettlementResponse(RiderCashSettlement settlement, decimal expectedAmount) => new(
        settlement.Id, settlement.RiderId, settlement.BranchId, settlement.DeclaredAmount, settlement.ShipmentCount,
        settlement.SettledAt, settlement.ConfirmedByUserId, settlement.BranchCashSettlementId,
        expectedAmount, settlement.DeclaredAmount - expectedAmount);
```

and in `CreateRiderSettlementAsync` change `return ToRiderSettlementResponse(settlement);` to:

```csharp
            return ToRiderSettlementResponse(settlement, collections.Sum(c => c.CollectedAmount ?? 0m));
```

`CodService.Reads.cs` — `ListCollectionsAsync`: change the signature to
`ListCollectionsAsync(CodCollectionStatus? status, Guid? branchId, Guid? merchantId, Guid? riderCashSettlementId, int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)` and add directly after the `status` filter:

```csharp
        if (riderCashSettlementId is Guid requestedSettlementId)
        {
            query = query.Where(c => c.RiderCashSettlementId == requestedSettlementId);
        }
```

`ListRiderSettlementsAsync`: replace the whole method with:

```csharp
    public async Task<PagedResult<RiderCashSettlementResponse>> ListRiderSettlementsAsync(
        Guid? branchId, Guid? riderId, bool? unreconciledOnly, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize,
        ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var (fromUtc, toUtc) = NormalizeRange(from, to);

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
        if (fromUtc is DateTimeOffset fromDate)
        {
            query = query.Where(r => r.SettledAt >= fromDate);
        }
        if (toUtc is DateTimeOffset toDate)
        {
            query = query.Where(r => r.SettledAt <= toDate);
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

        // One grouped query for the whole page (no N+1): each settlement's expected amount is the
        // sum of what its linked collections actually collected.
        var pageIds = settlements.Select(r => r.Id).ToArray();
        var expectedById = await _dbContext.Set<CODCollection>()
            .Where(c => c.OrganizationId == organizationId && c.RiderCashSettlementId != null && pageIds.Contains(c.RiderCashSettlementId.Value))
            .GroupBy(c => c.RiderCashSettlementId!.Value)
            .Select(g => new { SettlementId = g.Key, Expected = g.Sum(c => c.CollectedAmount ?? 0m) })
            .ToDictionaryAsync(x => x.SettlementId, x => x.Expected, cancellationToken);

        var items = settlements
            .Select(r => ToRiderSettlementResponse(r, expectedById.TryGetValue(r.Id, out var expected) ? expected : 0m))
            .ToList();
        return new PagedResult<RiderCashSettlementResponse>(items, normalizedPage, normalizedPageSize, totalCount);
    }
```

`CodEndpoints.cs` — replace the `/collections` and `/rider-settlements` GET mappings:

```csharp
        group.MapGet("/collections", async (CodCollectionStatus? status, Guid? branchId, Guid? merchantId, Guid? riderCashSettlementId, int? page, int? pageSize, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListCollectionsAsync(status, branchId, merchantId, riderCashSettlementId, page, pageSize, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");
```

```csharp
        group.MapGet("/rider-settlements", async (Guid? branchId, Guid? riderId, bool? unreconciledOnly, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize, CodService service, HttpContext httpContext) =>
            await HandleAsync(() => service.ListRiderSettlementsAsync(branchId, riderId, unreconciledOnly, from, to, page, pageSize, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");
```

Then update every other caller found by `grep -rn "ListCollectionsAsync(\|ListRiderSettlementsAsync(" tests src --include=*.cs`: insert `null, ` as the new 4th argument for `ListCollectionsAsync`, and `null, null, ` after the 3rd argument for `ListRiderSettlementsAsync`.

- [ ] **Step 4: Run unit tests**

Run: `dotnet test tests/Cod.Tests`
Expected: PASS (all, including the four new tests).

- [ ] **Step 5: Add API tests**

Append to `tests/Api.Tests/CodEndpointsTests.cs` (inside the class):

```csharp
    // ---- F15 R3a: rider-settlement variance, date filter, collections-by-settlement ----

    private static string UtcParam(DateTimeOffset value) => Uri.EscapeDataString(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));

    [Fact]
    public async Task RiderSettlement_ShortDeposit_ReportsExpectedAndVariance_AndFiltersByDate()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-r3a-variance");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndCompleteCodDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);

        var createResponse = await client.PostAsJsonAsync("/api/v1/cod/rider-settlements", new CreateRiderSettlementRequest(deliveryRiderId, destinationBranchId, 2400m));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<RiderCashSettlementResponse>();
        Assert.Equal(2500m, created!.ExpectedAmount);
        Assert.Equal(-100m, created.Variance);

        var inRange = await (await client.GetAsync($"/api/v1/cod/rider-settlements?from={UtcParam(DateTimeOffset.UtcNow.AddDays(-1))}&to={UtcParam(DateTimeOffset.UtcNow.AddDays(1))}"))
            .Content.ReadFromJsonAsync<PagedResult<RiderCashSettlementResponse>>();
        var listed = Assert.Single(inRange!.Items, r => r.Id == created.Id);
        Assert.Equal(-100m, listed.Variance);

        var future = await (await client.GetAsync($"/api/v1/cod/rider-settlements?from={UtcParam(DateTimeOffset.UtcNow.AddDays(1))}"))
            .Content.ReadFromJsonAsync<PagedResult<RiderCashSettlementResponse>>();
        Assert.DoesNotContain(future!.Items, r => r.Id == created.Id);

        var inverted = await client.GetAsync($"/api/v1/cod/rider-settlements?from={UtcParam(DateTimeOffset.UtcNow)}&to={UtcParam(DateTimeOffset.UtcNow.AddDays(-1))}");
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);

        var bySettlement = await (await client.GetAsync($"/api/v1/cod/collections?riderCashSettlementId={created.Id}"))
            .Content.ReadFromJsonAsync<PagedResult<CodCollectionResponse>>();
        var parcel = Assert.Single(bySettlement!.Items);
        Assert.Equal(shipment.Id, parcel.ShipmentId);
    }
```

- [ ] **Step 6: Run API tests**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CodEndpointsTests"`
Expected: PASS.

- [ ] **Step 7: Full backend suite and commit**

Run: `dotnet build` then `dotnet test tests/Cod.Tests` and `dotnet test tests/Api.Tests --filter "FullyQualifiedName~Cod"` → PASS.

```bash
git add src/Modules/Cod tests/Cod.Tests tests/Api.Tests
git commit -m "feat(cod): rider settlement expected/variance, date filter and collections-by-settlement (F15 R3a)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `GET /cod/flow` and `GET /cod/rider-cash` (backend)

**Files:**
- Modify: `src/Modules/Cod/Application/CodDtos.cs`
- Create: `src/Modules/Cod/Application/CodService.Flow.cs`
- Modify: `src/Modules/Cod/Presentation/CodEndpoints.cs`
- Create: `tests/Cod.Tests/CodServiceFlowTests.cs`
- Modify: `tests/Api.Tests/CodEndpointsTests.cs`

**Interfaces:**
- Consumes: `CodService.NormalizeRange` (Task 1).
- Produces: `CodFlowStageResponse(string Key, decimal Amount, int Count)`, `CodFlowResponse(List<CodFlowStageResponse> Stages)`, `RiderCashHoldingResponse(Guid RiderId, Guid BranchId, int ParcelCount, decimal Amount, DateTimeOffset OldestCollectedAt, DateTimeOffset LatestCollectedAt)`, `RiderCashResponse(List<RiderCashHoldingResponse> Items)`; `CodService.GetFlowAsync(Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal, CancellationToken)`, `CodService.GetRiderCashAsync(Guid? branchId, ClaimsPrincipal, CancellationToken)`. HTTP JSON (camelCase): `{ "stages": [{ "key", "amount", "count" }] }` with keys in order `expected, collected, riderDeposited, branchReconciled, financeVerified, settled`; `{ "items": [{ "riderId", "branchId", "parcelCount", "amount", "oldestCollectedAt", "latestCollectedAt" }] }`.

- [ ] **Step 1: Write the failing unit tests**

Create `tests/Cod.Tests/CodServiceFlowTests.cs`:

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

file class ScopedBranchAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid>? _accessible;
    public ScopedBranchAuthorizationChecker(params Guid[] accessible) => _accessible = accessible.Length == 0 ? null : accessible.ToHashSet();
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessible);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessible is null || _accessible.Contains(branchId));
}

file class NoBranchLookup : IBranchLookupService
{
    public Task<PagedResult<BranchAdminRecord>> ListForOrganizationAsync(Guid organizationId, int? page, int? pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<int> CountActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

file class NoMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class NoAwbLookup : IShipmentAwbLookupService
{
    public Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class CodServiceFlowTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyConfiguration(new CODCollectionConfiguration());
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ClaimsPrincipal Staff() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    private static CodService CreateService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker checker) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NoBranchLookup(),
        checker, new NoMerchantLookup(), new NoAwbLookup(), new NoopOutboxWriter());

    private static void Seed(TestDbContext dbContext, Guid organizationId, Guid branchId, CodCollectionStatus status, decimal expected,
        decimal? collected = null, Guid? riderId = null, DateTimeOffset? createdAt = null, DateTimeOffset? collectedAt = null)
    {
        dbContext.Set<CODCollection>().Add(new CODCollection
        {
            Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = organizationId, MerchantId = Guid.NewGuid(),
            BranchId = branchId, ExpectedAmount = expected, ShipmentTotalCharge = 150m, CollectedAmount = collected,
            CollectedByRiderId = riderId, CollectedAt = collected is null ? null : collectedAt ?? DateTimeOffset.UtcNow,
            Status = status, CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        });
    }

    [Fact]
    public async Task GetFlowAsync_StagesAreCumulativeAndNonIncreasing()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var branch = Guid.NewGuid();
        Seed(db, org, branch, CodCollectionStatus.CodExpected, 1000m);
        Seed(db, org, branch, CodCollectionStatus.CodCollected, 2000m, 2000m, Guid.NewGuid());
        Seed(db, org, branch, CodCollectionStatus.RiderDeposited, 3000m, 3000m, Guid.NewGuid());
        Seed(db, org, branch, CodCollectionStatus.BranchReconciled, 4000m, 4000m, Guid.NewGuid());
        Seed(db, org, branch, CodCollectionStatus.FinanceVerified, 5000m, 5000m, Guid.NewGuid());
        Seed(db, org, branch, CodCollectionStatus.MerchantSettlementCreated, 6000m, 6000m, Guid.NewGuid());
        Seed(db, org, branch, CodCollectionStatus.Settled, 7000m, 6900m, Guid.NewGuid());
        await db.SaveChangesAsync();

        var flow = await CreateService(db, org, new ScopedBranchAuthorizationChecker()).GetFlowAsync(null, null, null, Staff());

        Assert.Equal(new[] { "expected", "collected", "riderDeposited", "branchReconciled", "financeVerified", "settled" }, flow.Stages.Select(s => s.Key));
        Assert.Equal(new[] { 28000m, 26900m, 24900m, 21900m, 17900m, 6900m }, flow.Stages.Select(s => s.Amount));
        Assert.Equal(new[] { 7, 6, 5, 4, 3, 1 }, flow.Stages.Select(s => s.Count));
    }

    [Fact]
    public async Task GetFlowAsync_EmptyScope_ReturnsSixZeroStages()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        Seed(db, org, Guid.NewGuid(), CodCollectionStatus.CodExpected, 1000m);
        Seed(db, Guid.NewGuid(), Guid.NewGuid(), CodCollectionStatus.CodExpected, 9999m); // other tenant
        await db.SaveChangesAsync();

        var flow = await CreateService(db, org, new ScopedBranchAuthorizationChecker()).GetFlowAsync(Guid.NewGuid(), null, null, Staff());

        Assert.Equal(6, flow.Stages.Count);
        Assert.All(flow.Stages, s => { Assert.Equal(0m, s.Amount); Assert.Equal(0, s.Count); });
    }

    [Fact]
    public async Task GetFlowAsync_RespectsBranchScope_AndCreatedAtRange()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        Seed(db, org, mine, CodCollectionStatus.CodExpected, 1000m, createdAt: new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero));
        Seed(db, org, mine, CodCollectionStatus.CodExpected, 500m, createdAt: new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
        Seed(db, org, other, CodCollectionStatus.CodExpected, 7000m, createdAt: new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero));
        await db.SaveChangesAsync();

        var flow = await CreateService(db, org, new ScopedBranchAuthorizationChecker(mine)).GetFlowAsync(null,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 7, 23, 59, 59, TimeSpan.Zero), Staff());

        Assert.Equal(1000m, flow.Stages[0].Amount);
        Assert.Equal(1, flow.Stages[0].Count);
    }

    [Fact]
    public async Task GetFlowAsync_FromAfterTo_Throws()
    {
        await using var db = CreateContext();
        await Assert.ThrowsAsync<CodValidationException>(() => CreateService(db, Guid.NewGuid(), new ScopedBranchAuthorizationChecker())
            .GetFlowAsync(null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1), Staff()));
    }

    [Fact]
    public async Task GetRiderCashAsync_GroupsUndepositedCashByRiderAndBranch_OldestFirst()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var riderA = Guid.NewGuid();
        var riderB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Seed(db, org, branch, CodCollectionStatus.CodCollected, 1000m, 1000m, riderA, collectedAt: now.AddHours(-2));
        Seed(db, org, branch, CodCollectionStatus.CodCollected, 1500m, 1500m, riderA, collectedAt: now.AddHours(-1));
        Seed(db, org, branch, CodCollectionStatus.CodCollected, 800m, 800m, riderB, collectedAt: now.AddHours(-30));
        Seed(db, org, branch, CodCollectionStatus.RiderDeposited, 9000m, 9000m, riderA, collectedAt: now.AddHours(-50)); // already deposited
        Seed(db, org, branch, CodCollectionStatus.CodExpected, 400m); // not collected
        await db.SaveChangesAsync();

        var cash = await CreateService(db, org, new ScopedBranchAuthorizationChecker()).GetRiderCashAsync(null, Staff());

        Assert.Equal(2, cash.Items.Count);
        Assert.Equal(riderB, cash.Items[0].RiderId);
        Assert.Equal(800m, cash.Items[0].Amount);
        var a = cash.Items[1];
        Assert.Equal(riderA, a.RiderId);
        Assert.Equal(2, a.ParcelCount);
        Assert.Equal(2500m, a.Amount);
        Assert.True(a.OldestCollectedAt < a.LatestCollectedAt);
    }

    [Fact]
    public async Task GetRiderCashAsync_ExcludesOutOfScopeBranches()
    {
        await using var db = CreateContext();
        var org = Guid.NewGuid();
        var mine = Guid.NewGuid();
        Seed(db, org, mine, CodCollectionStatus.CodCollected, 1000m, 1000m, Guid.NewGuid());
        Seed(db, org, Guid.NewGuid(), CodCollectionStatus.CodCollected, 2000m, 2000m, Guid.NewGuid());
        await db.SaveChangesAsync();

        var cash = await CreateService(db, org, new ScopedBranchAuthorizationChecker(mine)).GetRiderCashAsync(null, Staff());

        var single = Assert.Single(cash.Items);
        Assert.Equal(mine, single.BranchId);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Cod.Tests --filter "FullyQualifiedName~CodServiceFlowTests"`
Expected: build FAIL (`GetFlowAsync` not defined).

- [ ] **Step 3: Implement**

Append to `CodDtos.cs`:

```csharp
public record CodFlowStageResponse(string Key, decimal Amount, int Count);

public record CodFlowResponse(List<CodFlowStageResponse> Stages);

public record RiderCashHoldingResponse(
    Guid RiderId, Guid BranchId, int ParcelCount, decimal Amount, DateTimeOffset OldestCollectedAt, DateTimeOffset LatestCollectedAt);

public record RiderCashResponse(List<RiderCashHoldingResponse> Items);
```

Create `src/Modules/Cod/Application/CodService.Flow.cs`:

```csharp
using System.Security.Claims;
using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

// F15 R3a read slice. "Reached" (cumulative) semantics: a collection counts in every stage it has
// reached so far, so stage totals never increase left to right and each gap is money waiting at
// that step. Distinct from GetReconciliationSummaryAsync's outstanding-only ExpectedCod
// (CLARIFICATIONS: COD flow stages).
public partial class CodService
{
    private static readonly CodCollectionStatus[] RiderDepositedOrLater =
        { CodCollectionStatus.RiderDeposited, CodCollectionStatus.BranchReconciled, CodCollectionStatus.FinanceVerified, CodCollectionStatus.MerchantSettlementCreated, CodCollectionStatus.Settled };
    private static readonly CodCollectionStatus[] BranchReconciledOrLater =
        { CodCollectionStatus.BranchReconciled, CodCollectionStatus.FinanceVerified, CodCollectionStatus.MerchantSettlementCreated, CodCollectionStatus.Settled };
    private static readonly CodCollectionStatus[] FinanceVerifiedOrLater =
        { CodCollectionStatus.FinanceVerified, CodCollectionStatus.MerchantSettlementCreated, CodCollectionStatus.Settled };

    private sealed record FlowGroup(CodCollectionStatus Status, bool Collected, int Count, decimal Expected, decimal CollectedSum);

    public async Task<CodFlowResponse> GetFlowAsync(Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");
        var (fromUtc, toUtc) = NormalizeRange(from, to);

        var query = await StaffScopedCollectionsAsync(organizationId, branchId, actingUser, cancellationToken);
        if (fromUtc is DateTimeOffset fromDate)
        {
            query = query.Where(c => c.CreatedAt >= fromDate);
        }
        if (toUtc is DateTimeOffset toDate)
        {
            query = query.Where(c => c.CreatedAt <= toDate);
        }

        // One grouped SQL query (at most 7 statuses × 2 rows) — never materialises collections.
        var groups = await query
            .GroupBy(c => new { c.Status, Collected = c.CollectedAmount != null })
            .Select(g => new { g.Key.Status, g.Key.Collected, Count = g.Count(), Expected = g.Sum(c => c.ExpectedAmount), CollectedSum = g.Sum(c => c.CollectedAmount ?? 0m) })
            .ToListAsync(cancellationToken);
        var rows = groups.Select(g => new FlowGroup(g.Status, g.Collected, g.Count, g.Expected, g.CollectedSum)).ToList();

        return new CodFlowResponse(new List<CodFlowStageResponse>
        {
            new("expected", rows.Sum(r => r.Expected), rows.Sum(r => r.Count)),
            Reached("collected", rows.Where(r => r.Collected)),
            Reached("riderDeposited", rows.Where(r => RiderDepositedOrLater.Contains(r.Status))),
            Reached("branchReconciled", rows.Where(r => BranchReconciledOrLater.Contains(r.Status))),
            Reached("financeVerified", rows.Where(r => FinanceVerifiedOrLater.Contains(r.Status))),
            Reached("settled", rows.Where(r => r.Status == CodCollectionStatus.Settled)),
        });
    }

    public async Task<RiderCashResponse> GetRiderCashAsync(Guid? branchId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var query = await StaffScopedCollectionsAsync(organizationId, branchId, actingUser, cancellationToken);
        var rows = await query
            .Where(c => c.Status == CodCollectionStatus.CodCollected && c.CollectedByRiderId != null && c.CollectedAt != null)
            .GroupBy(c => new { RiderId = c.CollectedByRiderId!.Value, c.BranchId })
            .Select(g => new
            {
                g.Key.RiderId,
                g.Key.BranchId,
                Count = g.Count(),
                Amount = g.Sum(c => c.CollectedAmount ?? 0m),
                Oldest = g.Min(c => c.CollectedAt!.Value),
                Latest = g.Max(c => c.CollectedAt!.Value),
            })
            .ToListAsync(cancellationToken);

        return new RiderCashResponse(rows
            .OrderBy(r => r.Oldest)
            .Select(r => new RiderCashHoldingResponse(r.RiderId, r.BranchId, r.Count, r.Amount, r.Oldest, r.Latest))
            .ToList());
    }

    private static CodFlowStageResponse Reached(string key, IEnumerable<FlowGroup> rows)
    {
        var list = rows.ToList();
        return new CodFlowStageResponse(key, list.Sum(r => r.CollectedSum), list.Sum(r => r.Count));
    }

    // Staff-only scope (endpoints reject merchant callers before reaching here): tenant filter,
    // optional branch narrowing, then cod.read branch access.
    private async Task<IQueryable<CODCollection>> StaffScopedCollectionsAsync(Guid organizationId, Guid? branchId, ClaimsPrincipal actingUser, CancellationToken cancellationToken)
    {
        var query = _dbContext.Set<CODCollection>().Where(c => c.OrganizationId == organizationId);
        if (branchId is Guid requestedBranchId)
        {
            query = query.Where(c => c.BranchId == requestedBranchId);
        }
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "cod.read", cancellationToken);
        if (accessibleBranchIds is not null)
        {
            var accessibleArray = accessibleBranchIds.ToArray();
            query = query.Where(c => accessibleArray.Contains(c.BranchId));
        }
        return query;
    }
}
```

In `CodEndpoints.cs`, add after the `/reconciliation` mapping:

```csharp
        // F15 R3a staff-only reads — merchant callers are rejected outright (403), never scoped.
        group.MapGet("/flow", async (Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, CodService service, HttpContext httpContext) =>
        {
            if (httpContext.User.GetMerchantId() is not null)
            {
                return Results.Forbid();
            }
            return await HandleAsync(() => service.GetFlowAsync(branchId, from, to, httpContext.User, httpContext.RequestAborted));
        }).RequirePermission("cod.read");

        group.MapGet("/rider-cash", async (Guid? branchId, CodService service, HttpContext httpContext) =>
        {
            if (httpContext.User.GetMerchantId() is not null)
            {
                return Results.Forbid();
            }
            return await HandleAsync(() => service.GetRiderCashAsync(branchId, httpContext.User, httpContext.RequestAborted));
        }).RequirePermission("cod.read");
```

(`GetMerchantId` is in `Courier.SharedKernel`, already imported.)

- [ ] **Step 4: Run unit tests**

Run: `dotnet test tests/Cod.Tests`
Expected: PASS.

- [ ] **Step 5: Add API tests**

Append to `tests/Api.Tests/CodEndpointsTests.cs`:

```csharp
    private record FlowStage(string Key, decimal Amount, int Count);
    private record FlowBody(List<FlowStage> Stages);
    private record RiderCashItem(Guid RiderId, Guid BranchId, int ParcelCount, decimal Amount, DateTimeOffset OldestCollectedAt, DateTimeOffset LatestCollectedAt);
    private record RiderCashBody(List<RiderCashItem> Items);

    [Fact]
    public async Task FlowAndRiderCash_TrackCashFromDoorstepToDeposit()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-r3a-flow");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        await BookAndCompleteCodDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId, 2500m);

        var flowResponse = await client.GetAsync("/api/v1/cod/flow");
        Assert.Equal(HttpStatusCode.OK, flowResponse.StatusCode);
        var flow = await flowResponse.Content.ReadFromJsonAsync<FlowBody>();
        Assert.Equal(new[] { "expected", "collected", "riderDeposited", "branchReconciled", "financeVerified", "settled" }, flow!.Stages.Select(s => s.Key));
        Assert.Equal(2500m, flow.Stages[1].Amount);
        Assert.Equal(0m, flow.Stages[2].Amount);

        var holding = await (await client.GetAsync("/api/v1/cod/rider-cash")).Content.ReadFromJsonAsync<RiderCashBody>();
        var row = Assert.Single(holding!.Items);
        Assert.Equal(deliveryRiderId, row.RiderId);
        Assert.Equal(destinationBranchId, row.BranchId);
        Assert.Equal(2500m, row.Amount);

        (await client.PostAsJsonAsync("/api/v1/cod/rider-settlements", new CreateRiderSettlementRequest(deliveryRiderId, destinationBranchId, 2500m))).EnsureSuccessStatusCode();

        var afterDeposit = await (await client.GetAsync("/api/v1/cod/rider-cash")).Content.ReadFromJsonAsync<RiderCashBody>();
        Assert.Empty(afterDeposit!.Items);
        var flowAfter = await (await client.GetAsync("/api/v1/cod/flow")).Content.ReadFromJsonAsync<FlowBody>();
        Assert.Equal(2500m, flowAfter!.Stages[2].Amount);

        var inverted = await client.GetAsync($"/api/v1/cod/flow?from={UtcParam(DateTimeOffset.UtcNow)}&to={UtcParam(DateTimeOffset.UtcNow.AddDays(-1))}");
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
    }

    [Fact]
    public async Task FlowAndRiderCash_MerchantCaller_Returns403()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "cod-r3a-merchant");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (_, ownerEmail, password) = await CreateMerchantWithLoginAsync(staffClient, "r3a");
        var merchantClient = await LoginMerchantAsync(ownerEmail, password);

        Assert.Equal(HttpStatusCode.Forbidden, (await merchantClient.GetAsync("/api/v1/cod/flow")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await merchantClient.GetAsync("/api/v1/cod/rider-cash")).StatusCode);
    }
```

- [ ] **Step 6: Run API tests, build, commit**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CodEndpointsTests"` → PASS; `dotnet build` → 0 errors.

```bash
git add src/Modules/Cod tests/Cod.Tests tests/Api.Tests
git commit -m "feat(cod): staff COD flow and rider cash reads (F15 R3a)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Web data layer — types, proxy routes, hooks, pure models

**Files:**
- Modify: `src/web/src/features/cod/types.ts`
- Create: `src/web/src/app/api/cod/flow/route.ts`, `route.test.ts`
- Create: `src/web/src/app/api/cod/rider-cash/route.ts`, `route.test.ts`
- Create: `src/web/src/features/cod/hooks/useCodFlow.ts`, `useRiderCash.ts`
- Modify: `src/web/src/features/cod/hooks/useRiderSettlements.ts`, `useCodCollections.ts`, `useCreateRiderSettlement.ts`
- Create: `src/web/src/features/cod/lib/codFormat.ts`, `codFormat.test.ts`
- Create: `src/web/src/features/cod/lib/codTabs.ts`, `codTabs.test.ts`
- Create: `src/web/src/features/cod/lib/flowModel.ts`, `flowModel.test.ts`
- Create: `src/web/src/features/cod/lib/settlementBreakdown.ts`, `settlementBreakdown.test.ts`

**Interfaces:**
- Produces (types): `RiderCashSettlement` gains `expectedAmount: number; variance: number`; `CodFlowStageKey`, `CodFlowStage { key; amount; count }`, `CodFlow { stages }`, `RiderCashHolding { riderId; branchId; parcelCount; amount; oldestCollectedAt; latestCollectedAt }`, `RiderCash { items }`.
- Produces (hooks): `useCodFlow(params: { branchId?: string; from?: string; to?: string }, options?: { enabled?: boolean })` key `["cod","flow",branchId??null,from??null,to??null]`; `useRiderCash(params: { branchId?: string }, options?: { enabled?: boolean })` key `["cod","rider-cash",branchId??null]`; `useRiderSettlements({ ..., from?, to? })`; `useCodCollections({ ..., riderCashSettlementId? }, options?: { enabled?: boolean })`.
- Produces (lib): `formatRs(n)`, `formatDate(iso)`, `formatTime(iso)`, `describeVariance(v): { tone: "matched"|"short"|"over"; label: string; token: string }`, `HELD_WARNING_HOURS = 24`, `hoursHeld(iso, now?)`, `formatHeldFor(iso, now?)`, `sortHoldings(items)`; `COD_TABS`, `CodTab`, `DEFAULT_COD_TAB`, `parseCodTab(v)`, `RANGE_PRESETS`, `RANGE_LABELS`, `parseRangePreset(v)`; `FLOW_LABELS`, `STAGE_TAB`, `pickBottleneck(stages)`, `stageAmount(flow, key)`; `buildSettlementBreakdown(settlement, adjustments): SettlementBreakdown`.

- [ ] **Step 1: Write the failing tests**

`src/web/src/features/cod/lib/codFormat.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { describeVariance, formatHeldFor, formatRs, hoursHeld, sortHoldings } from "./codFormat";

const grouping = new Intl.NumberFormat("en-IN", { maximumFractionDigits: 2 });

describe("formatRs", () => {
  it("uses Nepali/Indian digit grouping with an Rs prefix", () => {
    expect(formatRs(1248500)).toBe(`Rs ${grouping.format(1248500)}`);
    expect(formatRs(1248500)).toBe("Rs 12,48,500");
    expect(formatRs(2350.5)).toBe("Rs 2,350.5");
  });
});

describe("describeVariance", () => {
  it("labels matched, short and over with text", () => {
    expect(describeVariance(0)).toEqual({ tone: "matched", label: "Matched", token: "status-delivered" });
    expect(describeVariance(-500)).toEqual({ tone: "short", label: "Short − Rs 500", token: "status-ndr" });
    expect(describeVariance(200)).toEqual({ tone: "over", label: "Over + Rs 200", token: "status-in-transit" });
  });

  it("treats sub-paisa float noise as matched", () => {
    expect(describeVariance(0.1 + 0.2 - 0.3).tone).toBe("matched");
  });
});

describe("held time", () => {
  const now = new Date("2026-10-07T12:00:00Z");
  it("computes hours and formats days", () => {
    expect(hoursHeld("2026-10-07T09:00:00Z", now)).toBe(3);
    expect(formatHeldFor("2026-10-07T11:30:00Z", now)).toBe("< 1 h");
    expect(formatHeldFor("2026-10-07T09:00:00Z", now)).toBe("3 h");
    expect(formatHeldFor("2026-10-06T10:00:00Z", now)).toBe("1 d 2 h");
    expect(formatHeldFor("2026-10-05T12:00:00Z", now)).toBe("2 d");
  });

  it("never returns negative hours for clock skew", () => {
    expect(hoursHeld("2026-10-07T13:00:00Z", now)).toBe(0);
  });

  it("sorts longest-held first", () => {
    const items = [
      { riderId: "a", branchId: "b", parcelCount: 1, amount: 1, oldestCollectedAt: "2026-10-07T10:00:00Z", latestCollectedAt: "2026-10-07T10:00:00Z" },
      { riderId: "c", branchId: "b", parcelCount: 1, amount: 1, oldestCollectedAt: "2026-10-05T10:00:00Z", latestCollectedAt: "2026-10-05T10:00:00Z" },
    ];
    expect(sortHoldings(items).map((i) => i.riderId)).toEqual(["c", "a"]);
  });
});
```

`src/web/src/features/cod/lib/codTabs.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { COD_TABS, DEFAULT_COD_TAB, parseCodTab, parseRangePreset } from "./codTabs";

describe("codTabs", () => {
  it("lists the six tabs in order", () => {
    expect(COD_TABS.map((t) => t.key)).toEqual(["collections", "rider-deposits", "branch-settlements", "merchant-settlements", "adjustments", "ledger"]);
  });
  it("falls back to rider deposits for unknown tabs", () => {
    expect(parseCodTab("ledger")).toBe("ledger");
    expect(parseCodTab("reconciliation")).toBe(DEFAULT_COD_TAB);
    expect(parseCodTab(null)).toBe("rider-deposits");
  });
  it("defaults the range to 7 days", () => {
    expect(parseRangePreset("30d")).toBe("30d");
    expect(parseRangePreset("90d")).toBe("7d");
    expect(parseRangePreset(null)).toBe("7d");
  });
});
```

`src/web/src/features/cod/lib/flowModel.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { FLOW_LABELS, STAGE_TAB, pickBottleneck, stageAmount } from "./flowModel";
import type { CodFlow, CodFlowStage } from "@/features/cod/types";

const stages = (amounts: number[]): CodFlowStage[] =>
  (["expected", "collected", "riderDeposited", "branchReconciled", "financeVerified", "settled"] as const).map((key, i) => ({ key, amount: amounts[i], count: i }));

describe("pickBottleneck", () => {
  it("returns the stage before the largest drop", () => {
    expect(pickBottleneck(stages([100, 90, 40, 35, 30, 20]))).toBe("collected");
  });
  it("prefers the leftmost on ties", () => {
    expect(pickBottleneck(stages([100, 80, 60, 60, 60, 60]))).toBe("expected");
  });
  it("returns null when nothing drops", () => {
    expect(pickBottleneck(stages([50, 50, 50, 50, 50, 50]))).toBeNull();
    expect(pickBottleneck(stages([0, 0, 0, 0, 0, 0]))).toBeNull();
  });
});

describe("flow helpers", () => {
  it("labels and routes every stage", () => {
    expect(FLOW_LABELS.settled).toBe("Settled to merchants");
    expect(STAGE_TAB.collected).toBe("rider-deposits");
    expect(STAGE_TAB.riderDeposited).toBe("branch-settlements");
    expect(STAGE_TAB.financeVerified).toBe("merchant-settlements");
    expect(STAGE_TAB.expected).toBe("collections");
  });
  it("reads a stage amount, undefined when missing", () => {
    const flow: CodFlow = { stages: stages([9, 8, 7, 6, 5, 4]) };
    expect(stageAmount(flow, "financeVerified")).toBe(5);
    expect(stageAmount(undefined, "financeVerified")).toBeUndefined();
  });
});
```

`src/web/src/features/cod/lib/settlementBreakdown.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { buildSettlementBreakdown } from "./settlementBreakdown";
import type { Adjustment, MerchantSettlement } from "@/features/cod/types";

const settlement = (items: MerchantSettlement["items"], totalPayable: number): MerchantSettlement => ({
  id: "ms1", merchantId: "m1", periodFrom: "2026-10-01T00:00:00Z", periodTo: "2026-10-07T00:00:00Z", totalPayable, status: "Created", settledAt: null, items,
});
const item = (id: string, cod: number, fee: number, other: number) => ({ id, shipmentId: `s-${id}`, shipmentAwb: `AWB-${id}`, codAmount: cod, courierFee: fee, otherDeductions: other, netPayable: cod - fee - other });
const adjustment = (entityId: string, before: number, after: number, applied = true): Adjustment => ({
  id: `a-${entityId}`, entityType: "MerchantSettlementItem", entityId, beforeValue: before, afterValue: after, reason: "r", requestedByUserId: "u", requestedAt: "2026-10-06T00:00:00Z", approvedByUserId: applied ? "u2" : null, approvedAt: applied ? "2026-10-06T01:00:00Z" : null, applied,
});

describe("buildSettlementBreakdown", () => {
  it("sums lines and agrees with the server payable", () => {
    const b = buildSettlementBreakdown(settlement([item("1", 2500, 150, 0), item("2", 1000, 100, 50)], 3200), []);
    expect(b).toMatchObject({ parcelCount: 2, codTotal: 3500, courierFees: 250, otherDeductions: 50, adjustmentsIncluded: 0, payable: 3200, linesNet: 3200, mismatch: false });
  });

  it("reports approved adjustments on this settlement's items only, as an included amount", () => {
    const b = buildSettlementBreakdown(settlement([item("1", 2500, 150, 50)], 2300), [
      adjustment("1", 0, 50), adjustment("1", 0, 999, false), adjustment("other", 0, 70),
    ]);
    expect(b.adjustmentsIncluded).toBe(50);
    expect(b.otherDeductions).toBe(50);
  });

  it("flags a mismatch instead of hiding it", () => {
    const b = buildSettlementBreakdown(settlement([item("1", 2500, 150, 0)], 2000), []);
    expect(b.mismatch).toBe(true);
    expect(b.linesNet).toBe(2350);
    expect(b.payable).toBe(2000);
  });

  it("handles a settlement with no items", () => {
    const b = buildSettlementBreakdown(settlement([], 0), []);
    expect(b).toMatchObject({ parcelCount: 0, codTotal: 0, courierFees: 0, otherDeductions: 0, payable: 0, linesNet: 0, mismatch: false });
    expect(Number.isNaN(b.codTotal)).toBe(false);
  });
});
```

`src/web/src/app/api/cod/flow/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/cod/flow", () => {
  it("forwards the query string to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => ({ stages: [] }) } as Response);
    const response = await GET(new NextRequest("http://localhost/api/cod/flow?branchId=b1&from=2026-10-01T00:00:00Z"));
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/cod/flow?branchId=b1&from=2026-10-01T00:00:00Z");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new NextRequest("http://localhost/api/cod/flow"));
    expect(response.status).toBe(401);
  });
});
```

`src/web/src/app/api/cod/rider-cash/route.test.ts`: identical to the flow test with every `flow` replaced by `rider-cash`, `{ stages: [] }` replaced by `{ items: [] }`, and the first request URL `http://localhost/api/cod/rider-cash?branchId=b1` expecting `"/api/v1/cod/rider-cash?branchId=b1"`.

- [ ] **Step 2: Run tests to verify they fail**

Run (in `src/web`): `npx vitest run src/features/cod/lib src/app/api/cod/flow src/app/api/cod/rider-cash`
Expected: FAIL (modules not found).

- [ ] **Step 3: Implement types, routes, hooks, models**

`features/cod/types.ts` — add two fields to `RiderCashSettlement` (after `branchCashSettlementId`):

```ts
  expectedAmount: number;
  variance: number;
```

and append:

```ts
export type CodFlowStageKey = "expected" | "collected" | "riderDeposited" | "branchReconciled" | "financeVerified" | "settled";

export interface CodFlowStage {
  key: CodFlowStageKey;
  amount: number;
  count: number;
}

export interface CodFlow {
  stages: CodFlowStage[];
}

export interface RiderCashHolding {
  riderId: string;
  branchId: string;
  parcelCount: number;
  amount: number;
  oldestCollectedAt: string;
  latestCollectedAt: string;
}

export interface RiderCash {
  items: RiderCashHolding[];
}
```

`app/api/cod/flow/route.ts`:

```ts
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/cod/flow${query}`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`app/api/cod/rider-cash/route.ts`: the same file with `/api/v1/cod/flow` replaced by `/api/v1/cod/rider-cash`.

`features/cod/hooks/useCodFlow.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CodFlow } from "@/features/cod/types";

export function useCodFlow(params: { branchId?: string; from?: string; to?: string } = {}, options: { enabled?: boolean } = {}) {
  const { branchId, from, to } = params;
  const query = new URLSearchParams();
  if (branchId) query.set("branchId", branchId);
  if (from) query.set("from", from);
  if (to) query.set("to", to);
  const search = query.toString();

  return useQuery({
    queryKey: ["cod", "flow", branchId ?? null, from ?? null, to ?? null],
    queryFn: () => clientFetch<CodFlow>(`/api/cod/flow${search ? `?${search}` : ""}`),
    enabled: options.enabled ?? true,
  });
}
```

`features/cod/hooks/useRiderCash.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { RiderCash } from "@/features/cod/types";

export function useRiderCash(params: { branchId?: string } = {}, options: { enabled?: boolean } = {}) {
  const { branchId } = params;
  const search = branchId ? `?branchId=${encodeURIComponent(branchId)}` : "";
  return useQuery({
    queryKey: ["cod", "rider-cash", branchId ?? null],
    queryFn: () => clientFetch<RiderCash>(`/api/cod/rider-cash${search}`),
    enabled: options.enabled ?? true,
  });
}
```

`features/cod/hooks/useRiderSettlements.ts` — replace the function:

```ts
export function useRiderSettlements(params: { branchId?: string; riderId?: string; unreconciledOnly?: boolean; from?: string; to?: string; page?: number; pageSize?: number } = {}) {
  const { branchId, riderId, unreconciledOnly, from, to, page, pageSize } = params;
  const query = new URLSearchParams();
  if (branchId) query.set("branchId", branchId);
  if (riderId) query.set("riderId", riderId);
  if (unreconciledOnly) query.set("unreconciledOnly", "true");
  if (from) query.set("from", from);
  if (to) query.set("to", to);
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["cod", "rider-settlements", branchId ?? null, riderId ?? null, unreconciledOnly ?? null, from ?? null, to ?? null, page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<RiderCashSettlement>>(`/api/cod/rider-settlements${search ? `?${search}` : ""}`),
  });
}
```

`features/cod/hooks/useCodCollections.ts` — replace the function:

```ts
export function useCodCollections(
  params: { status?: CodCollectionStatus; branchId?: string; merchantId?: string; riderCashSettlementId?: string; page?: number; pageSize?: number } = {},
  options: { enabled?: boolean } = {},
) {
  const { status, branchId, merchantId, riderCashSettlementId, page, pageSize } = params;
  const query = new URLSearchParams();
  if (status) query.set("status", status);
  if (branchId) query.set("branchId", branchId);
  if (merchantId) query.set("merchantId", merchantId);
  if (riderCashSettlementId) query.set("riderCashSettlementId", riderCashSettlementId);
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["cod", "collections", status ?? null, branchId ?? null, merchantId ?? null, riderCashSettlementId ?? null, page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<CodCollection>>(`/api/cod/collections${search ? `?${search}` : ""}`),
    enabled: options.enabled ?? true,
  });
}
```

`features/cod/hooks/useCreateRiderSettlement.ts` — replace the `onSuccess` body with one prefix invalidation (covers rider settlements, collections, flow and rider cash):

```ts
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["cod"] });
    },
```

`features/cod/lib/codFormat.ts`:

```ts
import type { RiderCashHolding } from "@/features/cod/types";

const grouping = new Intl.NumberFormat("en-IN", { maximumFractionDigits: 2 });
const dateFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric" });
const timeFormat = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit" });

/** "Rs 12,48,500" — Nepali/Indian lakh grouping, up to 2 decimals. */
export function formatRs(amount: number): string {
  return `Rs ${grouping.format(amount)}`;
}

export function formatDate(iso: string): string {
  return dateFormat.format(new Date(iso));
}

export function formatTime(iso: string): string {
  return timeFormat.format(new Date(iso));
}

export type VarianceTone = "matched" | "short" | "over";

/** Text always carries the meaning; `token` is a status colour for the dot only. */
export function describeVariance(variance: number): { tone: VarianceTone; label: string; token: string } {
  const rounded = Math.round(variance * 100) / 100;
  if (rounded === 0) return { tone: "matched", label: "Matched", token: "status-delivered" };
  if (rounded < 0) return { tone: "short", label: `Short − ${formatRs(-rounded)}`, token: "status-ndr" };
  return { tone: "over", label: `Over + ${formatRs(rounded)}`, token: "status-in-transit" };
}

export const HELD_WARNING_HOURS = 24;

export function hoursHeld(oldestIso: string, now: Date = new Date()): number {
  return Math.max(0, (now.getTime() - Date.parse(oldestIso)) / 3_600_000);
}

export function formatHeldFor(oldestIso: string, now: Date = new Date()): string {
  const hours = Math.floor(hoursHeld(oldestIso, now));
  if (hours < 1) return "< 1 h";
  if (hours < 24) return `${hours} h`;
  const days = Math.floor(hours / 24);
  const rest = hours % 24;
  return rest === 0 ? `${days} d` : `${days} d ${rest} h`;
}

/** Longest-held first (so every "held over 24 h" row is on top). */
export function sortHoldings(items: RiderCashHolding[]): RiderCashHolding[] {
  return [...items].sort((a, b) => Date.parse(a.oldestCollectedAt) - Date.parse(b.oldestCollectedAt));
}
```

`features/cod/lib/codTabs.ts`:

```ts
import type { RangePreset } from "@/lib/dates/rangePresets";

export const COD_TABS = [
  { key: "collections", label: "Collections" },
  { key: "rider-deposits", label: "Rider deposits" },
  { key: "branch-settlements", label: "Branch settlements" },
  { key: "merchant-settlements", label: "Merchant settlements" },
  { key: "adjustments", label: "Adjustments" },
  { key: "ledger", label: "Ledger" },
] as const;

export type CodTab = (typeof COD_TABS)[number]["key"];

export const DEFAULT_COD_TAB: CodTab = "rider-deposits";

export function parseCodTab(value: string | null): CodTab {
  return COD_TABS.some((t) => t.key === value) ? (value as CodTab) : DEFAULT_COD_TAB;
}

export const RANGE_PRESETS: RangePreset[] = ["today", "7d", "30d"];

export const RANGE_LABELS: Record<RangePreset, string> = { today: "Today", "7d": "Last 7 days", "30d": "Last 30 days" };

export function parseRangePreset(value: string | null): RangePreset {
  return RANGE_PRESETS.includes(value as RangePreset) ? (value as RangePreset) : "7d";
}
```

`features/cod/lib/flowModel.ts`:

```ts
import type { CodFlow, CodFlowStage, CodFlowStageKey } from "@/features/cod/types";
import type { CodTab } from "@/features/cod/lib/codTabs";

export const FLOW_LABELS: Record<CodFlowStageKey, string> = {
  expected: "Expected",
  collected: "Collected",
  riderDeposited: "Rider deposited",
  branchReconciled: "Branch reconciled",
  financeVerified: "Finance verified",
  settled: "Settled to merchants",
};

/** The tab where the money waiting at each stage gets moved on. */
export const STAGE_TAB: Record<CodFlowStageKey, CodTab> = {
  expected: "collections",
  collected: "rider-deposits",
  riderDeposited: "branch-settlements",
  branchReconciled: "branch-settlements",
  financeVerified: "merchant-settlements",
  settled: "merchant-settlements",
};

/** The stage just before the largest drop in amount (leftmost on ties); null when nothing drops. */
export function pickBottleneck(stages: CodFlowStage[]): CodFlowStageKey | null {
  let best: CodFlowStageKey | null = null;
  let bestDrop = 0;
  for (let i = 0; i < stages.length - 1; i++) {
    const drop = stages[i].amount - stages[i + 1].amount;
    if (drop > bestDrop) {
      bestDrop = drop;
      best = stages[i].key;
    }
  }
  return best;
}

export function stageAmount(flow: CodFlow | undefined, key: CodFlowStageKey): number | undefined {
  return flow?.stages.find((s) => s.key === key)?.amount;
}
```

`features/cod/lib/settlementBreakdown.ts`:

```ts
import type { Adjustment, MerchantSettlement } from "@/features/cod/types";

export interface SettlementBreakdown {
  parcelCount: number;
  codTotal: number;
  courierFees: number;
  /** Already includes approved adjustments (applying one rewrites the item server-side). */
  otherDeductions: number;
  /** Net change in deductions from applied adjustments on this settlement's items. */
  adjustmentsIncluded: number;
  /** The server's totalPayable — the only figure shown as "Merchant payable". */
  payable: number;
  /** Σ item netPayable, compared against payable. */
  linesNet: number;
  mismatch: boolean;
}

const cents = (n: number) => Math.round(n * 100);

export function buildSettlementBreakdown(settlement: MerchantSettlement, adjustments: Adjustment[]): SettlementBreakdown {
  const items = settlement.items;
  const sum = (pick: (i: (typeof items)[number]) => number) => items.reduce((total, i) => total + pick(i), 0);
  const itemIds = new Set(items.map((i) => i.id));
  const adjustmentsIncluded = adjustments
    .filter((a) => a.applied && a.entityType === "MerchantSettlementItem" && itemIds.has(a.entityId))
    .reduce((total, a) => total + (a.afterValue - a.beforeValue), 0);
  const linesNet = sum((i) => i.netPayable);
  return {
    parcelCount: items.length,
    codTotal: sum((i) => i.codAmount),
    courierFees: sum((i) => i.courierFee),
    otherDeductions: sum((i) => i.otherDeductions),
    adjustmentsIncluded,
    payable: settlement.totalPayable,
    linesNet,
    mismatch: cents(linesNet) !== cents(settlement.totalPayable),
  };
}
```

- [ ] **Step 4: Run tests**

Run: `npx vitest run src/features/cod src/app/api/cod` → PASS (existing cod tests included; if an existing test asserts the old `["cod","rider-settlements"]`/`["cod","collections"]` invalidations of `useCreateRiderSettlement`, update it to expect the single `["cod"]` invalidation).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/cod src/web/src/app/api/cod
git commit -m "feat(web): COD flow, rider cash and variance data layer (F15 R3a)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Page shell — header, money-flow strip, URL-state tabs

**Files:**
- Create: `src/web/src/features/cod/hooks/useCodUrlState.ts`
- Create: `src/web/src/features/cod/components/CodFlowStrip.tsx`, `CodFlowStrip.test.tsx`
- Modify (rewrite): `src/web/src/app/(staff)/cod/page.tsx`, `page.test.tsx`
- Create: `src/web/src/features/cod/components/RiderDepositsTab.tsx` (placeholder export only in this task; Task 5 fills it)
- Delete (after grep confirms no other importer): `src/web/src/features/cod/components/ReconciliationTab.tsx`, `src/web/src/features/cod/hooks/useCodReconciliation.ts`, `src/web/src/features/cod/hooks/useCodSummary.ts`, `useCodSummary.test.tsx`

**Interfaces:**
- Consumes: Task 3 (`useCodFlow`, `COD_TABS`, `parseCodTab`, `parseRangePreset`, `RANGE_PRESETS`, `RANGE_LABELS`, `FLOW_LABELS`, `STAGE_TAB`, `pickBottleneck`, `formatRs`), `presetDays`/`toUtcRange` (`@/lib/dates/rangePresets`), `useSession` (`@/features/auth/hooks/useSession` → `data.hasAllBranchesScope`, `data.branches: { branchId }[]`), `useBranches`, `PageHeader`, `ComingSoonSlot`, `ErrorState`.
- Produces: `useCodUrlState(): { tab: CodTab; range: RangePreset; branchId: string | undefined; setTab(t); setRange(r); setBranch(id?) }` (URL keys `tab`, `range`, `branch`); `CodFlowStrip({ flow, isLoading, isError, onRetry, onSelectTab })`; `RiderDepositsTab({ branchId, from, to }: { branchId?: string; from?: string; to?: string })`.

- [ ] **Step 1: Write the failing tests**

`src/web/src/features/cod/components/CodFlowStrip.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { CodFlowStrip } from "./CodFlowStrip";
import { formatRs } from "@/features/cod/lib/codFormat";
import type { CodFlow } from "@/features/cod/types";

const flow: CodFlow = {
  stages: [
    { key: "expected", amount: 1248500, count: 482 },
    { key: "collected", amount: 986200, count: 391 },
    { key: "riderDeposited", amount: 840000, count: 331 },
    { key: "branchReconciled", amount: 795400, count: 314 },
    { key: "financeVerified", amount: 610000, count: 241 },
    { key: "settled", amount: 480300, count: 190 },
  ],
};

describe("CodFlowStrip", () => {
  it("renders six stages with amounts and counts", () => {
    render(<CodFlowStrip flow={flow} isLoading={false} isError={false} onRetry={vi.fn()} onSelectTab={vi.fn()} />);
    expect(screen.getAllByRole("button")).toHaveLength(6);
    expect(screen.getByText(formatRs(1248500))).toBeInTheDocument();
    expect(screen.getByText("482 parcels")).toBeInTheDocument();
    expect(screen.getByText("Settled to merchants")).toBeInTheDocument();
  });

  it("marks the bottleneck stage", () => {
    render(<CodFlowStrip flow={flow} isLoading={false} isError={false} onRetry={vi.fn()} onSelectTab={vi.fn()} />);
    // largest drop is expected → collected (262,300)
    expect(screen.getByRole("button", { name: /Expected/ })).toHaveAttribute("aria-current", "step");
    expect(screen.getByRole("button", { name: /Collected/ })).not.toHaveAttribute("aria-current");
  });

  it("switches to the stage's tab on click", () => {
    const onSelectTab = vi.fn();
    render(<CodFlowStrip flow={flow} isLoading={false} isError={false} onRetry={vi.fn()} onSelectTab={onSelectTab} />);
    fireEvent.click(screen.getByRole("button", { name: /Collected/ }));
    expect(onSelectTab).toHaveBeenCalledWith("rider-deposits");
  });

  it("never shows zeros while loading or on error", () => {
    const { rerender } = render(<CodFlowStrip flow={undefined} isLoading isError={false} onRetry={vi.fn()} onSelectTab={vi.fn()} />);
    expect(screen.getByRole("status", { name: "Loading COD lifecycle" })).toBeInTheDocument();
    expect(screen.queryByText(/Rs 0/)).toBeNull();
    const onRetry = vi.fn();
    rerender(<CodFlowStrip flow={undefined} isLoading={false} isError onRetry={onRetry} onSelectTab={vi.fn()} />);
    expect(screen.getByText("Couldn't load the COD flow")).toBeInTheDocument();
    expect(screen.queryByText(/Rs 0/)).toBeNull();
  });
});
```

Replace `src/web/src/app/(staff)/cod/page.test.tsx` with:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/cod",
  useSearchParams: () => new URLSearchParams(search),
}));

const useCodFlow = vi.fn();
vi.mock("@/features/cod/hooks/useCodFlow", () => ({ useCodFlow: (p: unknown) => useCodFlow(p) }));
let session = { hasAllBranchesScope: false, branches: [{ branchId: "b1" }] };
vi.mock("@/features/auth/hooks/useSession", () => ({ useSession: () => ({ data: session }) }));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", name: "Lalitpur Branch" }, { id: "b2", name: "Pokhara Hub" }] }),
}));
vi.mock("@/features/cod/components/CollectionsTab", () => ({ CollectionsTab: () => <div>collections-tab</div> }));
vi.mock("@/features/cod/components/RiderDepositsTab", () => ({ RiderDepositsTab: () => <div>rider-deposits-tab</div> }));
vi.mock("@/features/cod/components/BranchSettlementsTab", () => ({ BranchSettlementsTab: () => <div>branch-tab</div> }));
vi.mock("@/features/cod/components/MerchantSettlementsTab", () => ({ MerchantSettlementsTab: () => <div>merchant-tab</div> }));
vi.mock("@/features/cod/components/AdjustmentsTab", () => ({ AdjustmentsTab: () => <div>adjustments-tab</div> }));
vi.mock("@/features/cod/components/CollectionDetailSheet", () => ({ CollectionDetailSheet: () => null }));
vi.mock("@/features/cod/components/RiderSettlementCreateSheet", () => ({ RiderSettlementCreateSheet: () => null }));
vi.mock("@/features/cod/components/RiderSettlementDetailSheet", () => ({ RiderSettlementDetailSheet: () => null }));
vi.mock("@/features/cod/components/BranchSettlementCreateSheet", () => ({ BranchSettlementCreateSheet: () => null }));
vi.mock("@/features/cod/components/BranchSettlementDetailSheet", () => ({ BranchSettlementDetailSheet: () => null }));
vi.mock("@/features/cod/components/MerchantSettlementCreateSheet", () => ({ MerchantSettlementCreateSheet: () => null }));
vi.mock("@/features/cod/components/MerchantSettlementDetailSheet", () => ({ MerchantSettlementDetailSheet: () => null }));
vi.mock("@/features/cod/components/AdjustmentDetailSheet", () => ({ AdjustmentDetailSheet: () => null }));

import CodPage from "./page";

beforeEach(() => {
  replace.mockReset();
  useCodFlow.mockReset();
  useCodFlow.mockReturnValue({ data: undefined, isLoading: true, isError: false, refetch: vi.fn() });
  search = "";
  session = { hasAllBranchesScope: false, branches: [{ branchId: "b1" }] };
});

describe("CodPage", () => {
  it("defaults to the rider deposits tab and a 7-day range", () => {
    render(<CodPage />);
    expect(screen.getByRole("heading", { name: "COD reconciliation" })).toBeInTheDocument();
    expect(screen.getByText("rider-deposits-tab")).toBeInTheDocument();
    expect(screen.getByText(/All my branches · Last 7 days/)).toBeInTheDocument();
    const params = useCodFlow.mock.calls[0][0] as { from: string; to: string };
    expect(params.from).toMatch(/T00:00:00Z$/);
    expect(params.to).toMatch(/T23:59:59Z$/);
  });

  it("reads the tab from the URL and writes tab changes back", () => {
    search = "tab=ledger";
    render(<CodPage />);
    expect(screen.getByText("Ledger")).toBeInTheDocument();
    expect(screen.getByText(/Every COD money movement/)).toBeInTheDocument();
    fireEvent.mouseDown(screen.getByRole("tab", { name: "Adjustments" }));
    expect(replace).toHaveBeenCalledWith("/cod?tab=adjustments", { scroll: false });
  });

  it("hides the branch picker for single-branch users and shows it for multi-branch users", () => {
    render(<CodPage />);
    expect(screen.queryByRole("combobox", { name: "Branch" })).toBeNull();
    session = { hasAllBranchesScope: false, branches: [{ branchId: "b1" }, { branchId: "b2" }] };
    render(<CodPage />);
    expect(screen.getByRole("combobox", { name: "Branch" })).toBeInTheDocument();
  });

  it("passes the URL branch to the flow query", () => {
    search = "branch=b1";
    render(<CodPage />);
    expect(useCodFlow.mock.calls[0][0]).toMatchObject({ branchId: "b1" });
    expect(screen.getByText(/Lalitpur Branch · Last 7 days/)).toBeInTheDocument();
  });

  it("renders the needs-backend actions disabled", () => {
    render(<CodPage />);
    expect(screen.getByRole("button", { name: "Download ledger" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Close branch day" })).toBeDisabled();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx vitest run src/features/cod/components/CodFlowStrip.test.tsx "src/app/(staff)/cod"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`features/cod/hooks/useCodUrlState.ts`:

```ts
"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { parseCodTab, parseRangePreset, type CodTab } from "@/features/cod/lib/codTabs";
import type { RangePreset } from "@/lib/dates/rangePresets";

export function useCodUrlState() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  function update(patch: Record<string, string | undefined>) {
    const next = new URLSearchParams(searchParams.toString());
    for (const [key, value] of Object.entries(patch)) {
      if (value === undefined) next.delete(key);
      else next.set(key, value);
    }
    const qs = next.toString();
    router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
  }

  return {
    tab: parseCodTab(searchParams.get("tab")),
    range: parseRangePreset(searchParams.get("range")),
    branchId: searchParams.get("branch") ?? undefined,
    setTab: (tab: CodTab) => update({ tab }),
    setRange: (range: RangePreset) => update({ range }),
    setBranch: (branchId?: string) => update({ branch: branchId }),
  };
}
```

`features/cod/components/CodFlowStrip.tsx`:

```tsx
"use client";

import { Skeleton } from "@/components/ui/skeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { cn } from "@/lib/utils";
import { FLOW_LABELS, STAGE_TAB, pickBottleneck } from "@/features/cod/lib/flowModel";
import { formatRs } from "@/features/cod/lib/codFormat";
import type { CodTab } from "@/features/cod/lib/codTabs";
import type { CodFlow } from "@/features/cod/types";

const parcels = new Intl.NumberFormat("en-IN");

export function CodFlowStrip({
  flow,
  isLoading,
  isError,
  onRetry,
  onSelectTab,
}: {
  flow: CodFlow | undefined;
  isLoading: boolean;
  isError: boolean;
  onRetry: () => void;
  onSelectTab: (tab: CodTab) => void;
}) {
  if (isLoading) {
    return (
      <div role="status" aria-label="Loading COD lifecycle" className="grid grid-cols-2 gap-2 sm:grid-cols-3 xl:grid-cols-6">
        {Array.from({ length: 6 }).map((_, i) => (
          <Skeleton key={i} className="h-24 rounded-lg" />
        ))}
      </div>
    );
  }
  if (isError || !flow) {
    return <ErrorState title="Couldn't load the COD flow" description="Money totals are unavailable right now." onRetry={onRetry} />;
  }

  const bottleneck = pickBottleneck(flow.stages);
  return (
    <ol aria-label="COD lifecycle" className="grid grid-cols-2 gap-2 sm:grid-cols-3 xl:grid-cols-6">
      {flow.stages.map((stage, i) => {
        const active = stage.key === bottleneck;
        const muted = active ? "text-primary-foreground/80" : "text-muted-foreground";
        return (
          <li key={stage.key}>
            <button
              type="button"
              onClick={() => onSelectTab(STAGE_TAB[stage.key])}
              aria-current={active ? "step" : undefined}
              className={cn(
                "flex h-full w-full flex-col gap-1 rounded-lg border px-4 py-3 text-left transition-colors",
                active ? "border-primary bg-primary text-primary-foreground" : "border-border bg-card text-foreground hover:bg-accent",
              )}
            >
              <span className={cn("font-mono text-xs tracking-widest", muted)}>{String(i + 1).padStart(2, "0")}</span>
              <span className={cn("text-xs font-medium", muted)}>{FLOW_LABELS[stage.key]}</span>
              <span className="text-lg font-semibold tabular-nums tracking-tight">{formatRs(stage.amount)}</span>
              <span className={cn("text-xs", muted)}>{parcels.format(stage.count)} parcels</span>
            </button>
          </li>
        );
      })}
    </ol>
  );
}
```

`features/cod/components/RiderDepositsTab.tsx` (placeholder for Task 5):

```tsx
"use client";

export function RiderDepositsTab(_props: { branchId?: string; from?: string; to?: string }) {
  return null;
}
```

Rewrite `app/(staff)/cod/page.tsx`:

```tsx
"use client";

import { Suspense } from "react";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { PageHeader } from "@/components/page/PageHeader";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { useSession } from "@/features/auth/hooks/useSession";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useCodFlow } from "@/features/cod/hooks/useCodFlow";
import { useCodUrlState } from "@/features/cod/hooks/useCodUrlState";
import { COD_TABS, RANGE_LABELS, RANGE_PRESETS, parseCodTab, parseRangePreset } from "@/features/cod/lib/codTabs";
import { presetDays, toUtcRange } from "@/lib/dates/rangePresets";
import { CodFlowStrip } from "@/features/cod/components/CodFlowStrip";
import { CollectionsTab } from "@/features/cod/components/CollectionsTab";
import { CollectionDetailSheet } from "@/features/cod/components/CollectionDetailSheet";
import { RiderDepositsTab } from "@/features/cod/components/RiderDepositsTab";
import { RiderSettlementCreateSheet } from "@/features/cod/components/RiderSettlementCreateSheet";
import { RiderSettlementDetailSheet } from "@/features/cod/components/RiderSettlementDetailSheet";
import { BranchSettlementsTab } from "@/features/cod/components/BranchSettlementsTab";
import { BranchSettlementCreateSheet } from "@/features/cod/components/BranchSettlementCreateSheet";
import { BranchSettlementDetailSheet } from "@/features/cod/components/BranchSettlementDetailSheet";
import { MerchantSettlementsTab } from "@/features/cod/components/MerchantSettlementsTab";
import { MerchantSettlementCreateSheet } from "@/features/cod/components/MerchantSettlementCreateSheet";
import { MerchantSettlementDetailSheet } from "@/features/cod/components/MerchantSettlementDetailSheet";
import { AdjustmentsTab } from "@/features/cod/components/AdjustmentsTab";
import { AdjustmentDetailSheet } from "@/features/cod/components/AdjustmentDetailSheet";

const ALL_BRANCHES = "all";

function NeedsBackendButton({ label }: { label: string }) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span tabIndex={0}>
          <Button type="button" variant="outline" disabled>
            {label}
          </Button>
        </span>
      </TooltipTrigger>
      <TooltipContent>Needs backend</TooltipContent>
    </Tooltip>
  );
}

function CodPageContent() {
  const { tab, range, branchId, setTab, setRange, setBranch } = useCodUrlState();
  const days = presetDays(range);
  const { from, to } = toUtcRange(days.from, days.to);
  const flow = useCodFlow({ branchId, from, to });

  const { data: session } = useSession();
  const { data: branches } = useBranches();
  const scopedBranches = (branches ?? []).filter(
    (b) => session?.hasAllBranchesScope || session?.branches.some((s) => s.branchId === b.id),
  );
  const showBranchPicker = Boolean(session?.hasAllBranchesScope) || scopedBranches.length > 1;
  const branchLabel = branchId ? scopedBranches.find((b) => b.id === branchId)?.name ?? "Selected branch" : "All my branches";

  return (
    <TooltipProvider>
      <div className="space-y-5">
        <PageHeader
          eyebrow="Finance"
          title="COD reconciliation"
          description={`${branchLabel} · ${RANGE_LABELS[range]}`}
          actions={
            <div className="flex flex-wrap items-center gap-2">
              {showBranchPicker && (
                <Select value={branchId ?? ALL_BRANCHES} onValueChange={(v) => setBranch(v === ALL_BRANCHES ? undefined : v)}>
                  <SelectTrigger className="w-48" aria-label="Branch">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={ALL_BRANCHES}>All my branches</SelectItem>
                    {scopedBranches.map((b) => (
                      <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
              <Select value={range} onValueChange={(v) => setRange(parseRangePreset(v))}>
                <SelectTrigger className="w-36" aria-label="Date range">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {RANGE_PRESETS.map((p) => (
                    <SelectItem key={p} value={p}>{RANGE_LABELS[p]}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <NeedsBackendButton label="Download ledger" />
              <NeedsBackendButton label="Close branch day" />
            </div>
          }
        />

        <CodFlowStrip flow={flow.data} isLoading={flow.isLoading} isError={flow.isError} onRetry={() => flow.refetch()} onSelectTab={setTab} />

        <Tabs value={tab} onValueChange={(v) => setTab(parseCodTab(v))}>
          <TabsList>
            {COD_TABS.map((t) => (
              <TabsTrigger key={t.key} value={t.key}>{t.label}</TabsTrigger>
            ))}
          </TabsList>
          <TabsContent value="collections"><CollectionsTab /></TabsContent>
          <TabsContent value="rider-deposits"><RiderDepositsTab branchId={branchId} from={from} to={to} /></TabsContent>
          <TabsContent value="branch-settlements"><BranchSettlementsTab /></TabsContent>
          <TabsContent value="merchant-settlements"><MerchantSettlementsTab /></TabsContent>
          <TabsContent value="adjustments"><AdjustmentsTab /></TabsContent>
          <TabsContent value="ledger">
            <ComingSoonSlot title="Ledger" description="Every COD money movement with before/after values and export." minHeight={220} />
          </TabsContent>
        </Tabs>

        <CollectionDetailSheet />
        <RiderSettlementCreateSheet />
        <RiderSettlementDetailSheet />
        <BranchSettlementCreateSheet />
        <BranchSettlementDetailSheet />
        <MerchantSettlementCreateSheet />
        <MerchantSettlementDetailSheet />
        <AdjustmentDetailSheet />
      </div>
    </TooltipProvider>
  );
}

export default function CodPage() {
  return (
    <Suspense>
      <CodPageContent />
    </Suspense>
  );
}
```

Then: `grep -rn "ReconciliationTab\|useCodReconciliation\|useCodSummary" src/web/src` — if only the files listed for deletion (and each other) reference them, delete `ReconciliationTab.tsx`, `useCodReconciliation.ts`, `useCodSummary.ts`, `useCodSummary.test.tsx`. Leave the `/api/cod/reconciliation` and `/api/cod/summary` proxy routes in place.

- [ ] **Step 4: Run tests**

Run: `npx vitest run src/features/cod "src/app/(staff)/cod"` → PASS. Run `npx tsc --noEmit` → only the 8 baseline errors.

- [ ] **Step 5: Commit**

```bash
git add -A src/web/src/features/cod "src/web/src/app/(staff)/cod"
git commit -m "feat(web): COD reconciliation page shell with money-flow strip and URL tabs (F15 R3a)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Rider deposits tab, Record deposit Sheet, deposit detail Sheet

**Files:**
- Create: `src/web/src/features/cod/components/VarianceBadge.tsx`
- Modify (fill): `src/web/src/features/cod/components/RiderDepositsTab.tsx`; Create `RiderDepositsTab.test.tsx`
- Modify: `src/web/src/features/cod/store/codPanelStore.ts`, `codPanelStore.test.ts`
- Modify (redesign): `src/web/src/features/cod/components/RiderSettlementCreateSheet.tsx`, `RiderSettlementCreateSheet.test.tsx`
- Modify: `src/web/src/features/cod/components/RiderSettlementDetailSheet.tsx`
- Modify: `src/web/src/features/cod/schema.ts`
- Delete (after grep confirms the page no longer imports it): `src/web/src/features/cod/components/RiderSettlementsTab.tsx`

**Interfaces:**
- Consumes: Task 3 (`useRiderCash`, `useRiderSettlements({from,to})`, `useCodCollections(params, {enabled})`, `formatRs`, `formatTime`, `formatDate`, `describeVariance`, `formatHeldFor`, `hoursHeld`, `HELD_WARNING_HOURS`, `sortHoldings`), `usePeopleNames` (`@/features/shipments/hooks/usePeopleNames` → `riderName(id)`, `branchName(id)`), `usePermission`, `SectionCard`, `SheetHeaderBlock`, `ComingSoonSlot`, `DataTable`, `EmptyState`, `ErrorState`.
- Produces: store `openRiderSettlementCreate(prefill?: { riderId: string; branchId: string })`, panel `{ mode: "rider-settlement-create"; prefill?: { riderId: string; branchId: string } }`; `VarianceBadge({ variance }: { variance: number })`; `HoldingStatus({ oldestCollectedAt })` (internal).

- [ ] **Step 1: Write the failing tests**

Add to `src/web/src/features/cod/store/codPanelStore.test.ts`:

```ts
  it("opens the rider-settlement create sheet with an optional prefill", () => {
    useCodPanelStore.getState().openRiderSettlementCreate({ riderId: "r1", branchId: "b1" });
    expect(useCodPanelStore.getState().panel).toEqual({ mode: "rider-settlement-create", prefill: { riderId: "r1", branchId: "b1" } });
    useCodPanelStore.getState().openRiderSettlementCreate();
    expect(useCodPanelStore.getState().panel).toEqual({ mode: "rider-settlement-create", prefill: undefined });
  });
```

(import `useCodPanelStore` the way the existing test file does.)

Create `src/web/src/features/cod/components/RiderDepositsTab.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { formatRs } from "@/features/cod/lib/codFormat";

const now = Date.parse("2026-10-07T12:00:00Z");
const riderCash = vi.fn();
vi.mock("@/features/cod/hooks/useRiderCash", () => ({ useRiderCash: (...a: unknown[]) => riderCash(...a) }));
const riderSettlements = vi.fn();
vi.mock("@/features/cod/hooks/useRiderSettlements", () => ({ useRiderSettlements: (p: unknown) => riderSettlements(p) }));
let perms = new Set(["cod.read", "cod.reconcile"]);
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => perms.has(p) }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ riderName: (id: string) => (id === "r1" ? "Bikash Tamang" : "Rider"), branchName: () => "Lalitpur Branch", userName: () => "Staff" }),
}));
const openCreate = vi.fn();
const openDetail = vi.fn();
vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (sel: (s: unknown) => unknown) => sel({ openRiderSettlementCreate: openCreate, openRiderSettlementDetail: openDetail }),
}));

import { RiderDepositsTab } from "./RiderDepositsTab";

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(now);
  perms = new Set(["cod.read", "cod.reconcile"]);
  openCreate.mockReset();
  riderCash.mockReturnValue({
    data: {
      items: [
        { riderId: "r1", branchId: "b1", parcelCount: 14, amount: 38400, oldestCollectedAt: "2026-10-07T09:00:00Z", latestCollectedAt: "2026-10-07T11:00:00Z" },
        { riderId: "r2", branchId: "b1", parcelCount: 7, amount: 16900, oldestCollectedAt: "2026-10-06T10:00:00Z", latestCollectedAt: "2026-10-06T11:00:00Z" },
      ],
    },
    isLoading: false,
    isError: false,
  });
  riderSettlements.mockReturnValue({
    data: { items: [{ id: "rs1", riderId: "r1", branchId: "b1", declaredAmount: 37900, shipmentCount: 14, settledAt: "2026-10-07T10:00:00Z", confirmedByUserId: "u1", branchCashSettlementId: null, expectedAmount: 38400, variance: -500 }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 },
    isLoading: false,
    isError: false,
  });
});

describe("RiderDepositsTab", () => {
  it("lists holdings longest-held first with an over-24 h warning", () => {
    render(<RiderDepositsTab from="a" to="b" />);
    const rows = within(screen.getByRole("table", { name: "Holding cash" })).getAllByRole("row").slice(1);
    expect(within(rows[0]).getByText("Rider")).toBeInTheDocument();
    expect(within(rows[0]).getByText("Held over 24 h")).toBeInTheDocument();
    expect(within(rows[0]).getByText("1 d 2 h")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Bikash Tamang")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Holding")).toBeInTheDocument();
    expect(within(rows[1]).getByText(formatRs(38400))).toBeInTheDocument();
  });

  it("opens Record deposit prefilled — including for a rider whose name is unknown", () => {
    render(<RiderDepositsTab from="a" to="b" />);
    const rows = within(screen.getByRole("table", { name: "Holding cash" })).getAllByRole("row").slice(1);
    fireEvent.click(within(rows[0]).getByRole("button", { name: "Record deposit" }));
    expect(openCreate).toHaveBeenCalledWith({ riderId: "r2", branchId: "b1" });
  });

  it("hides Record deposit without cod.reconcile", () => {
    perms = new Set(["cod.read"]);
    render(<RiderDepositsTab from="a" to="b" />);
    expect(screen.queryByRole("button", { name: "Record deposit" })).toBeNull();
  });

  it("shows recent deposits with server variance and passes the range", () => {
    render(<RiderDepositsTab branchId="b1" from="2026-10-01T00:00:00Z" to="2026-10-07T23:59:59Z" />);
    expect(screen.getByText(`Short − ${formatRs(500)}`)).toBeInTheDocument();
    expect(screen.getByText("Awaiting branch")).toBeInTheDocument();
    expect(riderSettlements.mock.calls[0][0]).toMatchObject({ branchId: "b1", from: "2026-10-01T00:00:00Z", to: "2026-10-07T23:59:59Z" });
    expect(riderCash.mock.calls[0][0]).toEqual({ branchId: "b1" });
  });

  it("shows an empty state instead of zeros when nobody holds cash", () => {
    riderCash.mockReturnValue({ data: { items: [] }, isLoading: false, isError: false });
    render(<RiderDepositsTab from="a" to="b" />);
    expect(screen.getByText("No rider is holding COD cash right now.")).toBeInTheDocument();
  });
});
```

Replace `src/web/src/features/cod/components/RiderSettlementCreateSheet.test.tsx` with:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { formatRs } from "@/features/cod/lib/codFormat";

vi.mock("@/features/branches/hooks/useBranches", () => ({ useBranches: () => ({ data: [{ id: "b1", name: "Kathmandu Hub" }] }) }));
vi.mock("@/features/riders/hooks/useRiders", () => ({
  useRiders: () => ({ data: [{ id: "r1", userId: "u1", baseBranchId: "b1", licenseNumber: "LIC-001", roleType: "DeliveryRider", isActive: true }] }),
}));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ riderName: () => "Rider", branchName: () => "Kathmandu Hub", userName: () => "Staff" }),
}));
vi.mock("@/features/cod/hooks/useRiderCash", () => ({
  useRiderCash: () => ({ data: { items: [{ riderId: "r1", branchId: "b1", parcelCount: 3, amount: 2500, oldestCollectedAt: "2026-10-07T09:00:00Z", latestCollectedAt: "2026-10-07T10:00:00Z" }] }, isLoading: false }),
}));
const mutateAsync = vi.fn();
vi.mock("@/features/cod/hooks/useCreateRiderSettlement", () => ({ useCreateRiderSettlement: () => ({ mutateAsync, isPending: false }) }));
const close = vi.fn();
let panel: unknown = { mode: "rider-settlement-create" };
vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (selector: (s: unknown) => unknown) => selector({ panel, close }),
}));

import { RiderSettlementCreateSheet } from "./RiderSettlementCreateSheet";

function renderSheet() {
  return render(<QueryClientProvider client={new QueryClient()}><RiderSettlementCreateSheet /></QueryClientProvider>);
}

beforeEach(() => {
  mutateAsync.mockReset();
  close.mockReset();
  panel = { mode: "rider-settlement-create" };
});

describe("RiderSettlementCreateSheet", () => {
  it("submits the selected branch, rider and cash received, then closes", async () => {
    mutateAsync.mockResolvedValue({ id: "rs1" });
    renderSheet();
    fireEvent.click(screen.getByLabelText("Branch"));
    fireEvent.click(await screen.findByRole("option", { name: "Kathmandu Hub" }));
    fireEvent.click(screen.getByLabelText("Rider"));
    fireEvent.click(await screen.findByRole("option", { name: "LIC-001" }));
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "2500" } });
    fireEvent.click(screen.getByRole("button", { name: /record deposit/i }));
    await waitFor(() => expect(mutateAsync).toHaveBeenCalledWith({ riderId: "r1", branchId: "b1", declaredAmount: 2500 }));
    await waitFor(() => expect(close).toHaveBeenCalled());
  });

  it("prefills from the holding row and previews variance live", async () => {
    panel = { mode: "rider-settlement-create", prefill: { riderId: "r1", branchId: "b1" } };
    renderSheet();
    expect(await screen.findByText(formatRs(2500))).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "2000" } });
    expect(screen.getByText(`Short − ${formatRs(500)}`)).toBeInTheDocument();
    expect(screen.getByText(/Shortage\/overage reasons and approval need backend support/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "2500" } });
    expect(screen.getByText("Matched")).toBeInTheDocument();
  });

  it.each([["", "Declared amount is required."], ["0", "Declared amount must be greater than zero."], ["-5", "Declared amount must be greater than zero."], ["10.555", "Use at most 2 decimal places."]])(
    "rejects cash received %s without sending",
    async (value, message) => {
      panel = { mode: "rider-settlement-create", prefill: { riderId: "r1", branchId: "b1" } };
      renderSheet();
      fireEvent.change(screen.getByLabelText("Cash received"), { target: { value } });
      fireEvent.click(screen.getByRole("button", { name: /record deposit/i }));
      expect(await screen.findByText(message)).toBeInTheDocument();
      expect(mutateAsync).not.toHaveBeenCalled();
    },
  );

  it("keeps the sheet open and shows the server error", async () => {
    mutateAsync.mockRejectedValue(new Error(JSON.stringify({ error: { code: "COD_VALIDATION_FAILED", message: "No collected COD found for this rider at this branch." } })));
    panel = { mode: "rider-settlement-create", prefill: { riderId: "r1", branchId: "b1" } };
    renderSheet();
    fireEvent.change(screen.getByLabelText("Cash received"), { target: { value: "2500" } });
    fireEvent.click(screen.getByRole("button", { name: /record deposit/i }));
    expect(await screen.findByText("No collected COD found for this rider at this branch.")).toBeInTheDocument();
    expect(close).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx vitest run src/features/cod/components/RiderDepositsTab.test.tsx src/features/cod/components/RiderSettlementCreateSheet.test.tsx src/features/cod/store`
Expected: FAIL.

- [ ] **Step 3: Implement**

`codPanelStore.ts` — change the create variant and action:

```ts
type RiderDepositPrefill = { riderId: string; branchId: string };
```

(add above `CodPanelState`), replace `| { mode: "rider-settlement-create" }` with `| { mode: "rider-settlement-create"; prefill?: RiderDepositPrefill }`, change the interface member to `openRiderSettlementCreate: (prefill?: RiderDepositPrefill) => void;`, and the implementation to:

```ts
  openRiderSettlementCreate: (prefill) => set({ panel: { mode: "rider-settlement-create", prefill } }),
```

`schema.ts` — replace the `declaredAmount` line of `createRiderSettlementSchema` with:

```ts
  declaredAmount: z
    .number({ error: "Declared amount is required." })
    .positive("Declared amount must be greater than zero.")
    .refine((v) => /^\d+(\.\d{1,2})?$/.test(String(v)), "Use at most 2 decimal places."),
```

`VarianceBadge.tsx`:

```tsx
import { describeVariance } from "@/features/cod/lib/codFormat";

export function VarianceBadge({ variance }: { variance: number }) {
  const v = describeVariance(variance);
  return (
    <span className="inline-flex items-center gap-1.5 rounded bg-muted px-2 py-0.5 text-xs font-medium text-foreground">
      <span className="h-2 w-2 rounded-full" style={{ backgroundColor: `hsl(var(--${v.token}))` }} aria-hidden="true" />
      {v.label}
    </span>
  );
}
```

`RiderDepositsTab.tsx` (replace the placeholder):

```tsx
"use client";

import { useState } from "react";
import { Wallet, ReceiptText } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { SectionCard } from "@/components/page/SectionCard";
import { ErrorState } from "@/components/states/ErrorState";
import { EmptyState } from "@/components/states/EmptyState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useRiderCash } from "@/features/cod/hooks/useRiderCash";
import { useRiderSettlements } from "@/features/cod/hooks/useRiderSettlements";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { HELD_WARNING_HOURS, formatHeldFor, formatRs, formatTime, formatDate, hoursHeld, sortHoldings } from "@/features/cod/lib/codFormat";
import { VarianceBadge } from "@/features/cod/components/VarianceBadge";
import type { RiderCashSettlement } from "@/features/cod/types";

function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase()).join("") || "R";
}

function HoldingStatus({ oldestCollectedAt }: { oldestCollectedAt: string }) {
  const overdue = hoursHeld(oldestCollectedAt) >= HELD_WARNING_HOURS;
  return (
    <span className="inline-flex items-center gap-1.5 rounded bg-muted px-2 py-0.5 text-xs font-medium">
      <span className="h-2 w-2 rounded-full" style={{ backgroundColor: `hsl(var(--${overdue ? "status-ndr" : "status-created"}))` }} aria-hidden="true" />
      {overdue ? "Held over 24 h" : "Holding"}
    </span>
  );
}

export function RiderDepositsTab({ branchId, from, to }: { branchId?: string; from?: string; to?: string }) {
  const canReconcile = usePermission("cod.reconcile");
  const names = usePeopleNames();
  const openCreate = useCodPanelStore((s) => s.openRiderSettlementCreate);
  const openDetail = useCodPanelStore((s) => s.openRiderSettlementDetail);
  const cash = useRiderCash({ branchId });
  const [page, setPage] = useState(1);
  const deposits = useRiderSettlements({ branchId, from, to, page, pageSize: 20 });

  const holdings = sortHoldings(cash.data?.items ?? []);

  const depositColumns: ColumnDef<RiderCashSettlement>[] = [
    {
      key: "rider",
      header: "Rider",
      render: (r) => (
        <button type="button" onClick={() => openDetail(r.id)} className="font-medium text-foreground hover:underline">
          {names.riderName(r.riderId)}
        </button>
      ),
    },
    { key: "parcels", header: "Parcels", render: (r) => String(r.shipmentCount) },
    { key: "expected", header: "Expected", render: (r) => formatRs(r.expectedAmount) },
    { key: "deposited", header: "Deposited", render: (r) => formatRs(r.declaredAmount) },
    { key: "variance", header: "Variance", render: (r) => <VarianceBadge variance={r.variance} /> },
    { key: "recorded", header: "Recorded at", render: (r) => `${formatDate(r.settledAt)} ${formatTime(r.settledAt)}` },
    { key: "branch", header: "Branch state", render: (r) => (r.branchCashSettlementId ? "Reconciled" : "Awaiting branch") },
  ];

  return (
    <div className="space-y-4">
      <SectionCard title="Holding cash" icon={Wallet} description="Collected COD riders have not deposited yet.">
        {cash.isLoading && <Skeleton className="h-32 w-full" />}
        {cash.isError && <ErrorState title="Couldn't load rider cash" onRetry={() => cash.refetch?.()} />}
        {cash.data && holdings.length === 0 && <EmptyState title="No rider is holding COD cash right now." />}
        {holdings.length > 0 && (
          <Table aria-label="Holding cash">
            <TableHeader>
              <TableRow>
                <TableHead>Rider</TableHead>
                <TableHead>Branch</TableHead>
                <TableHead className="text-right">Parcels</TableHead>
                <TableHead className="text-right">Amount held</TableHead>
                <TableHead>Held for</TableHead>
                <TableHead>Status</TableHead>
                {canReconcile && <TableHead><span className="sr-only">Actions</span></TableHead>}
              </TableRow>
            </TableHeader>
            <TableBody>
              {holdings.map((h) => {
                const name = names.riderName(h.riderId);
                return (
                  <TableRow key={`${h.riderId}-${h.branchId}`}>
                    <TableCell>
                      <span className="flex items-center gap-2.5">
                        <span className="flex h-8 w-8 items-center justify-center rounded-full bg-muted text-xs font-semibold" aria-hidden="true">{initials(name)}</span>
                        <span className="font-medium">{name}</span>
                      </span>
                    </TableCell>
                    <TableCell>{names.branchName(h.branchId)}</TableCell>
                    <TableCell className="text-right tabular-nums">{h.parcelCount}</TableCell>
                    <TableCell className="text-right font-medium tabular-nums">{formatRs(h.amount)}</TableCell>
                    <TableCell className="tabular-nums">{formatHeldFor(h.oldestCollectedAt)}</TableCell>
                    <TableCell><HoldingStatus oldestCollectedAt={h.oldestCollectedAt} /></TableCell>
                    {canReconcile && (
                      <TableCell className="text-right">
                        <Button type="button" size="sm" onClick={() => openCreate({ riderId: h.riderId, branchId: h.branchId })}>
                          Record deposit
                        </Button>
                      </TableCell>
                    )}
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )}
      </SectionCard>

      <SectionCard title="Recent deposits" icon={ReceiptText} description="Deposits recorded in the selected range.">
        {deposits.isError ? (
          <ErrorState title="Couldn't load deposits" onRetry={() => deposits.refetch?.()} />
        ) : (
          <DataTable
            columns={depositColumns}
            data={deposits.data}
            isLoading={deposits.isLoading}
            emptyTitle="No deposits in this range"
            emptyDescription="Deposits appear here once a rider hands over collected COD."
            onPageChange={setPage}
            rowKey={(r) => r.id}
          />
        )}
      </SectionCard>
    </div>
  );
}
```

(Check `EmptyState`'s props by opening `components/states/EmptyState.tsx`; if it takes `title`/`description` as shown, use it as written, otherwise pass the same copy through its actual prop names.)

Rewrite `RiderSettlementCreateSheet.tsx`:

```tsx
"use client";

import { useEffect, useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Info } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useRiderCash } from "@/features/cod/hooks/useRiderCash";
import { createRiderSettlementSchema, type CreateRiderSettlementFormValues } from "@/features/cod/schema";
import { useCreateRiderSettlement } from "@/features/cod/hooks/useCreateRiderSettlement";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { codErrorMessage } from "@/features/cod/lib/codErrorMessage";
import { describeVariance, formatRs, formatTime } from "@/features/cod/lib/codFormat";

export function RiderSettlementCreateSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const isOpen = panel.mode === "rider-settlement-create";
  const prefill = panel.mode === "rider-settlement-create" ? panel.prefill : undefined;

  const createSettlement = useCreateRiderSettlement();
  const { data: branches } = useBranches();
  const { data: riders } = useRiders();
  const names = usePeopleNames();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    handleSubmit,
    control,
    watch,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateRiderSettlementFormValues>({ resolver: zodResolver(createRiderSettlementSchema) });

  useEffect(() => {
    if (isOpen) {
      reset({ riderId: prefill?.riderId ?? "", branchId: prefill?.branchId ?? "", declaredAmount: undefined as unknown as number });
      setServerError(null);
    }
  }, [isOpen, prefill?.riderId, prefill?.branchId, reset]);

  const selectedBranchId = watch("branchId");
  const selectedRiderId = watch("riderId");
  const received = watch("declaredAmount");
  const eligibleRiders = riders?.filter((r) => r.isActive && r.baseBranchId === selectedBranchId);
  const cash = useRiderCash({ branchId: selectedBranchId || undefined }, { enabled: isOpen && Boolean(selectedBranchId) });
  const holding = cash.data?.items.find((h) => h.riderId === selectedRiderId && h.branchId === selectedBranchId);
  const preview = holding && typeof received === "number" && !Number.isNaN(received) ? describeVariance(received - holding.amount) : null;
  const riderLabel = (id: string, license: string) => {
    const name = names.riderName(id);
    return name === "Rider" ? license : name;
  };

  async function onSubmit(values: CreateRiderSettlementFormValues) {
    setServerError(null);
    try {
      await createSettlement.mutateAsync(values);
      close();
    } catch (error) {
      setServerError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Record deposit</SheetTitle>
          <SheetDescription>
            {holding
              ? `${names.riderName(holding.riderId)} · ${holding.parcelCount} COD parcels · last collected ${formatTime(holding.latestCollectedAt)}`
              : "Count the cash a rider hands over and record it against their collections."}
          </SheetDescription>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="branchId">Branch</Label>
            <Controller
              name="branchId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="branchId" aria-label="Branch" aria-invalid={!!errors.branchId} aria-describedby={errors.branchId ? "branchId-error" : undefined}>
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
            {errors.branchId && <FieldError id="branchId-error">{errors.branchId.message}</FieldError>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="riderId">Rider</Label>
            <Controller
              name="riderId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="riderId" aria-label="Rider" aria-invalid={!!errors.riderId} aria-describedby={errors.riderId ? "riderId-error" : undefined}>
                    <SelectValue placeholder="Select rider" />
                  </SelectTrigger>
                  <SelectContent>
                    {eligibleRiders?.map((r) => (
                      <SelectItem key={r.id} value={r.id}>{riderLabel(r.id, r.licenseNumber)}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.riderId && <FieldError id="riderId-error">{errors.riderId.message}</FieldError>}
          </div>

          <dl className="space-y-2 rounded-lg border border-border p-4 text-sm">
            <div className="flex justify-between">
              <dt className="text-muted-foreground">Expected (held)</dt>
              <dd className="font-semibold tabular-nums">{holding ? formatRs(holding.amount) : "—"}</dd>
            </div>
            <div className="space-y-2">
              <Label htmlFor="declaredAmount">Cash received</Label>
              <Controller
                name="declaredAmount"
                control={control}
                render={({ field }) => (
                  <Input
                    id="declaredAmount"
                    aria-label="Cash received"
                    type="number"
                    step="0.01"
                    inputMode="decimal"
                    aria-invalid={!!errors.declaredAmount}
                    aria-describedby={errors.declaredAmount ? "declaredAmount-error" : undefined}
                    value={field.value ?? ""}
                    onChange={(e) => field.onChange(e.target.value === "" ? undefined : Number(e.target.value))}
                  />
                )}
              />
              {errors.declaredAmount && <FieldError id="declaredAmount-error">{errors.declaredAmount.message}</FieldError>}
            </div>
            <div className="flex justify-between border-t border-border pt-2">
              <dt className="font-medium">Variance <span className="text-xs font-normal text-muted-foreground">(preview)</span></dt>
              <dd className="font-semibold">{preview ? preview.label : "—"}</dd>
            </div>
          </dl>

          {preview && preview.tone !== "matched" && (
            <p className="flex gap-2 rounded-lg bg-muted p-3 text-xs text-muted-foreground">
              <Info className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              Shortage/overage reasons and approval need backend support. The deposit and its variance will be recorded.
            </p>
          )}

          <ComingSoonSlot className="p-4" title="Note denominations" description="Count cash by note (1000 × 30, 500 × 10…)." />
          <ComingSoonSlot className="p-4" title="Fonepay / QR split" description="Separate verified digital payments from cash." />

          {serverError && <FieldError id="rider-settlement-server-error">{serverError}</FieldError>}
          <Button type="submit" className="w-full" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Record deposit"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
```

`RiderSettlementDetailSheet.tsx` — keep its structure; change:
1. Replace the rider/branch lookups with `const names = usePeopleNames();` (import from `@/features/shipments/hooks/usePeopleNames`; drop now-unused `useRiders`/`useBranches` imports) and render `names.riderName(settlement.riderId)` / `names.branchName(settlement.branchId)`.
2. Replace the `Declared Amount` field with three: `<Field label="Expected" value={formatRs(settlement.expectedAmount)} />`, `<Field label="Deposited" value={formatRs(settlement.declaredAmount)} />`, and a Variance cell `<div className="flex flex-col gap-1"><span className="text-xs text-muted-foreground">Variance</span><VarianceBadge variance={settlement.variance} /></div>`; rename "Shipment Count" → "Parcels", "Settled At" → "Recorded at" (value `formatDate(...) + " " + formatTime(...)`), "Reconciled" values "Reconciled"/"Awaiting branch".
3. Below the grid, list the settlement's parcels:

```tsx
  const parcels = useCodCollections({ riderCashSettlementId: id, pageSize: 200 }, { enabled: isOpen && Boolean(id) });
  const openCollection = useCodPanelStore((s) => s.openCollectionDetail);
```

```tsx
              <div className="space-y-2">
                <h3 className="text-sm font-medium">Parcels in this deposit</h3>
                {parcels.isLoading && <DetailSkeleton fieldRows={1} />}
                {parcels.data && (
                  <ul className="divide-y divide-border rounded-lg border border-border text-sm">
                    {parcels.data.items.map((c) => (
                      <li key={c.id} className="flex items-center justify-between px-3 py-2">
                        <span className="text-muted-foreground">{c.collectedAt ? `${formatDate(c.collectedAt)} ${formatTime(c.collectedAt)}` : "—"}</span>
                        <span className="tabular-nums">{c.collectedAmount === null ? "—" : formatRs(c.collectedAmount)}</span>
                        <Button type="button" size="sm" variant="ghost" onClick={() => openCollection(c.id)}>View</Button>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
```

Finally `grep -rn "RiderSettlementsTab" src/web/src` — if only its own file remains, delete `RiderSettlementsTab.tsx`.

- [ ] **Step 4: Run tests**

Run: `npx vitest run src/features/cod "src/app/(staff)/cod"` → PASS. `npx tsc --noEmit` → baseline 8 only.

- [ ] **Step 5: Commit**

```bash
git add -A src/web/src/features/cod
git commit -m "feat(web): rider deposits with holding cash, variance and record-deposit sheet (F15 R3a)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Merchant settlement detail — breakdown and trace one line

**Files:**
- Modify (redesign): `src/web/src/features/cod/components/MerchantSettlementDetailSheet.tsx`
- Create: `src/web/src/features/cod/components/MerchantSettlementDetailSheet.test.tsx`

**Interfaces:**
- Consumes: Task 3 (`buildSettlementBreakdown`, `formatRs`, `formatDate`), `useAdjustments({ applied: true, pageSize: 200 })`, `useMerchants` (`@/features/shipments/hooks/useMerchants` → `{ id, displayName }[]`), `SheetHeaderBlock` (props `eyebrow`, `title`, `mono?`, `status?`, `facts?`), existing `AdjustmentForm` (kept in-file), `useSettleMerchantSettlement`, `usePermission`.
- Produces: no new exports.

- [ ] **Step 1: Write the failing test**

`MerchantSettlementDetailSheet.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { formatRs } from "@/features/cod/lib/codFormat";

let settlement: unknown;
vi.mock("@/features/cod/hooks/useMerchantSettlement", () => ({
  useMerchantSettlement: () => ({ data: settlement, isLoading: false, isError: false, refetch: vi.fn() }),
}));
vi.mock("@/features/cod/hooks/useAdjustments", () => ({
  useAdjustments: () => ({ data: { items: [{ id: "a1", entityType: "MerchantSettlementItem", entityId: "i1", beforeValue: 0, afterValue: 50, reason: "Damaged", requestedByUserId: "u", requestedAt: "2026-10-06T00:00:00Z", approvedByUserId: "u2", approvedAt: "2026-10-06T01:00:00Z", applied: true }] } }),
}));
vi.mock("@/features/shipments/hooks/useMerchants", () => ({ useMerchants: () => ({ data: [{ id: "m1", displayName: "Everest Organics" }] }) }));
const settle = vi.fn();
vi.mock("@/features/cod/hooks/useSettleMerchantSettlement", () => ({ useSettleMerchantSettlement: () => ({ mutateAsync: settle, isPending: false }) }));
vi.mock("@/features/cod/hooks/useRequestAdjustment", () => ({ useRequestAdjustment: () => ({ mutateAsync: vi.fn() }) }));
let perms = new Set(["cod.read", "cod.settle", "cod.adjust"]);
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => perms.has(p) }));
vi.mock("@/features/cod/store/codPanelStore", () => ({
  useCodPanelStore: (sel: (s: unknown) => unknown) => sel({ panel: { mode: "merchant-settlement-detail", id: "ms1" }, close: vi.fn() }),
}));

import { MerchantSettlementDetailSheet } from "./MerchantSettlementDetailSheet";

const base = {
  id: "ms1", merchantId: "m1", periodFrom: "2026-10-01T00:00:00Z", periodTo: "2026-10-07T00:00:00Z", status: "Created", settledAt: null,
  items: [
    { id: "i1", shipmentId: "s1", shipmentAwb: "NX-00148", codAmount: 2500, courierFee: 150, otherDeductions: 50, netPayable: 2300 },
    { id: "i2", shipmentId: "s2", shipmentAwb: "NX-00149", codAmount: 1000, courierFee: 100, otherDeductions: 0, netPayable: 900 },
  ],
};

function renderSheet() {
  return render(<QueryClientProvider client={new QueryClient()}><MerchantSettlementDetailSheet /></QueryClientProvider>);
}

beforeEach(() => {
  perms = new Set(["cod.read", "cod.settle", "cod.adjust"]);
  settlement = { ...base, totalPayable: 3200 };
});

describe("MerchantSettlementDetailSheet", () => {
  it("shows the breakdown ending in the server payable", () => {
    renderSheet();
    expect(screen.getByText("Everest Organics")).toBeInTheDocument();
    const breakdown = screen.getByRole("list", { name: "Payable breakdown" });
    expect(within(breakdown).getByText("2 delivered COD parcels")).toBeInTheDocument();
    expect(within(breakdown).getByText(formatRs(3500))).toBeInTheDocument();
    expect(within(breakdown).getByText(`− ${formatRs(250)}`)).toBeInTheDocument();
    expect(within(breakdown).getByText(`Includes ${formatRs(50)} from approved adjustments`)).toBeInTheDocument();
    expect(within(breakdown).getByText(formatRs(3200))).toBeInTheDocument();
    expect(screen.queryByText(/don't add up/)).toBeNull();
  });

  it("warns when lines don't add up to the payable", () => {
    settlement = { ...base, totalPayable: 3000 };
    renderSheet();
    expect(screen.getByText(/Lines don't add up to the payable/)).toBeInTheDocument();
  });

  it("traces a selected line", () => {
    renderSheet();
    fireEvent.click(screen.getByRole("button", { name: "Trace NX-00149" }));
    const trace = screen.getByRole("region", { name: "Trace one line" });
    expect(within(trace).getByText("NX-00149")).toBeInTheDocument();
    expect(within(trace).getByText(formatRs(900))).toBeInTheDocument();
  });

  it("hides settle and adjust actions without permissions", () => {
    perms = new Set(["cod.read"]);
    renderSheet();
    expect(screen.queryByRole("button", { name: "Mark settled" })).toBeNull();
    expect(screen.queryByRole("button", { name: /Request adjustment/ })).toBeNull();
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/features/cod/components/MerchantSettlementDetailSheet.test.tsx` → FAIL.

- [ ] **Step 3: Implement**

In `MerchantSettlementDetailSheet.tsx`: keep the `AdjustmentForm` component unchanged except its submit button label "Request Adjustment" → "Request adjustment". Replace the `Field` helper and the `MerchantSettlementDetailSheet` component with:

```tsx
function BreakdownRow({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <li className={`flex justify-between py-2 ${strong ? "pt-3 font-semibold" : "border-b border-border"}`}>
      <span>{label}</span>
      <span className={`tabular-nums ${strong ? "text-base text-primary" : "font-medium"}`}>{value}</span>
    </li>
  );
}

export function MerchantSettlementDetailSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const isOpen = panel.mode === "merchant-settlement-detail";
  const id = panel.mode === "merchant-settlement-detail" ? panel.id : "";

  const [adjustingItemId, setAdjustingItemId] = useState<string | null>(null);
  const [tracedItemId, setTracedItemId] = useState<string | null>(null);
  const [settleError, setSettleError] = useState<string | null>(null);
  const canAdjust = usePermission("cod.adjust");
  const canSettle = usePermission("cod.settle");

  const { data: settlement, isLoading, isError, refetch } = useMerchantSettlement(id);
  const { data: adjustments } = useAdjustments({ applied: true, pageSize: 200 });
  const { data: merchants } = useMerchants();
  const settleMutation = useSettleMerchantSettlement(id);

  const breakdown = settlement ? buildSettlementBreakdown(settlement, adjustments?.items ?? []) : null;
  const traced = settlement?.items.find((i) => i.id === tracedItemId) ?? settlement?.items[0];
  const merchantName = merchants?.find((m) => m.id === settlement?.merchantId)?.displayName ?? "Merchant";

  async function handleSettle() {
    setSettleError(null);
    try {
      await settleMutation.mutateAsync();
    } catch (error) {
      setSettleError(codErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { setAdjustingItemId(null); setTracedItemId(null); close(); } }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-2xl">
        <SheetHeader className="sr-only">
          <SheetTitle>Merchant settlement</SheetTitle>
          <SheetDescription>Payable breakdown and line items</SheetDescription>
        </SheetHeader>
        <div className="space-y-6">
          {isLoading && <DetailSkeleton fieldRows={2} />}
          {isError && <ErrorState title="Couldn't load this settlement" description="Something went wrong." onRetry={() => refetch()} />}
          {settlement && breakdown && (
            <>
              <SheetHeaderBlock
                eyebrow="Merchant settlement"
                title={merchantName}
                status={<StatusBadge status={settlement.status} namespace="MerchantSettlement" />}
                facts={[
                  { label: "Settlement", value: <span className="font-mono text-xs">{settlement.id.slice(0, 8)}</span> },
                  { label: "Period", value: `${formatDate(settlement.periodFrom)} – ${formatDate(settlement.periodTo)}` },
                  { label: "Settled", value: settlement.settledAt ? formatDate(settlement.settledAt) : "—" },
                ]}
              />

              <div className="grid gap-5 md:grid-cols-[minmax(0,1fr)_240px]">
                <ul aria-label="Payable breakdown" className="text-sm">
                  <BreakdownRow label={`${breakdown.parcelCount} delivered COD parcels`} value={formatRs(breakdown.codTotal)} />
                  <BreakdownRow label="Delivery charges (booking-time prices)" value={`− ${formatRs(breakdown.courierFees)}`} />
                  <BreakdownRow label="Other deductions" value={`− ${formatRs(breakdown.otherDeductions)}`} />
                  {breakdown.adjustmentsIncluded !== 0 && (
                    <li className="border-b border-border pb-2 text-xs text-muted-foreground">
                      Includes {formatRs(breakdown.adjustmentsIncluded)} from approved adjustments
                    </li>
                  )}
                  <BreakdownRow label="Merchant payable" value={formatRs(breakdown.payable)} strong />
                  {breakdown.mismatch && (
                    <li role="alert" className="mt-2 rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-xs text-destructive">
                      Lines don&apos;t add up to the payable — contact finance. Lines total {formatRs(breakdown.linesNet)}; payable is {formatRs(breakdown.payable)}.
                    </li>
                  )}
                </ul>

                {traced && (
                  <section aria-label="Trace one line" className="flex flex-col gap-2 rounded-lg bg-accent p-4 text-sm">
                    <span className="font-mono text-xs tracking-widest text-muted-foreground">TRACE ONE LINE</span>
                    <span className="font-mono font-medium">{traced.shipmentAwb}</span>
                    <span className="flex justify-between"><span>COD collected</span><span className="tabular-nums">{formatRs(traced.codAmount)}</span></span>
                    <span className="flex justify-between"><span>Courier fee</span><span className="tabular-nums">− {formatRs(traced.courierFee)}</span></span>
                    <span className="flex justify-between"><span>Other deductions</span><span className="tabular-nums">− {formatRs(traced.otherDeductions)}</span></span>
                    <span className="flex justify-between border-t border-border pt-2 font-semibold"><span>Payable</span><span className="tabular-nums">{formatRs(traced.netPayable)}</span></span>
                  </section>
                )}
              </div>

              <div className="space-y-2">
                <h3 className="text-sm font-medium">Line items</h3>
                <ul className="divide-y divide-border rounded-lg border border-border">
                  {settlement.items.map((item) => (
                    <li key={item.id} className="p-3 text-sm">
                      <div className="flex flex-wrap items-center gap-3">
                        <button
                          type="button"
                          aria-label={`Trace ${item.shipmentAwb}`}
                          onClick={() => setTracedItemId(item.id)}
                          className="font-mono font-medium text-foreground hover:underline"
                        >
                          {item.shipmentAwb}
                        </button>
                        <span className="ml-auto grid grid-cols-4 gap-3 text-right tabular-nums text-muted-foreground">
                          <span>{formatRs(item.codAmount)}</span>
                          <span>− {formatRs(item.courierFee)}</span>
                          <span>− {formatRs(item.otherDeductions)}</span>
                          <span className="font-medium text-foreground">{formatRs(item.netPayable)}</span>
                        </span>
                        {canAdjust && settlement.status === "Created" && adjustingItemId !== item.id && (
                          <Button type="button" size="sm" variant="outline" onClick={() => setAdjustingItemId(item.id)}>
                            Request adjustment
                          </Button>
                        )}
                      </div>
                      {adjustingItemId === item.id && <AdjustmentForm item={item} onDone={() => setAdjustingItemId(null)} />}
                    </li>
                  ))}
                </ul>
              </div>

              {canSettle && (
                <div className="space-y-2">
                  {settleError && <FieldError id="settle-error">{settleError}</FieldError>}
                  <Button type="button" disabled={settlement.status !== "Created" || settleMutation.isPending} onClick={handleSettle}>
                    {settleMutation.isPending ? <Spinner className="h-4 w-4" /> : "Mark settled"}
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

Update imports: add `SheetDescription` from `@/components/ui/sheet`, `SheetHeaderBlock` from `@/components/page/SheetHeaderBlock`, `useAdjustments` from `@/features/cod/hooks/useAdjustments`, `useMerchants` from `@/features/shipments/hooks/useMerchants`, `buildSettlementBreakdown` from `@/features/cod/lib/settlementBreakdown`, `formatDate`/`formatRs` from `@/features/cod/lib/codFormat`; remove unused `Label` only if `AdjustmentForm` no longer uses it (it does — keep). If `SheetHeaderBlock`'s `facts[].value` is typed `string`, pass the settlement id as a plain string `settlement.id.slice(0, 8)` instead of the `<span>`.

- [ ] **Step 4: Run tests**

Run: `npx vitest run src/features/cod` → PASS (fix any existing test that asserted the old "Settle"/"Request Adjustment" labels to the new ones).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/cod
git commit -m "feat(web): merchant settlement payable breakdown and line trace (F15 R3a)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Collections, Branch settlements, Adjustments — consistent money, dates and names

**Files:**
- Modify: `src/web/src/features/cod/components/CollectionsTab.tsx`, `BranchSettlementsTab.tsx`, `AdjustmentsTab.tsx`, `MerchantSettlementsTab.tsx`
- Create: `src/web/src/features/cod/components/AdjustmentsTab.test.tsx`

**Interfaces:**
- Consumes: Task 3 `formatRs`, `formatDate`; `usePeopleNames().userName(id)`.
- Produces: nothing new.

- [ ] **Step 1: Write the failing test**

`AdjustmentsTab.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { formatRs } from "@/features/cod/lib/codFormat";

vi.mock("@/features/cod/hooks/useAdjustments", () => ({
  useAdjustments: () => ({
    data: { items: [{ id: "a1", entityType: "MerchantSettlementItem", entityId: "i1", beforeValue: 0, afterValue: 1250.5, reason: "Damaged parcel", requestedByUserId: "u1", requestedAt: "2026-10-06T00:00:00Z", approvedByUserId: null, approvedAt: null, applied: false }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 },
    isLoading: false,
  }),
}));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({ usePeopleNames: () => ({ userName: () => "Ramesh Shrestha", riderName: () => "Rider", branchName: () => "Branch" }) }));
vi.mock("@/features/cod/store/codPanelStore", () => ({ useCodPanelStore: (sel: (s: unknown) => unknown) => sel({ openAdjustmentDetail: vi.fn() }) }));

import { AdjustmentsTab } from "./AdjustmentsTab";

describe("AdjustmentsTab", () => {
  it("formats money consistently and names the requester", () => {
    render(<AdjustmentsTab />);
    expect(screen.getByText(formatRs(1250.5))).toBeInTheDocument();
    expect(screen.getByText("Ramesh Shrestha")).toBeInTheDocument();
    expect(screen.getByText("Pending")).toBeInTheDocument();
    expect(screen.queryByText(/Rs\. /)).toBeNull();
  });
});
```

(Read `AdjustmentsTab.tsx` first; if it uses other hooks — e.g. a filter `Select` with its own state or `usePermission` — add matching mocks in the test.)

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/features/cod/components/AdjustmentsTab.test.tsx` → FAIL.

- [ ] **Step 3: Implement**

Make exactly these edits:
- `CollectionsTab.tsx`: `formatAmount` returns `value === null ? "—" : formatRs(value)`; `collectedAt` column renders `c.collectedAt ? formatDate(c.collectedAt) : "—"`; header labels → "Expected", "Collected", "Status", "Collected on".
- `BranchSettlementsTab.tsx`: `confirmedAmount` → `formatRs(b.confirmedAmount)` with header "Confirmed"; `riderCount` header "Rider deposits"; `reconciledAt` → `formatDate(b.reconciledAt)` with header "Reconciled on".
- `AdjustmentsTab.tsx`: `beforeValue`/`afterValue` → `formatRs(...)` with headers "Before"/"After"; add a column after `reason`: `{ key: "requestedBy", header: "Requested by", render: (a) => names.userName(a.requestedByUserId) }` with `const names = usePeopleNames();` at the top of the component; `requestedAt` → `formatDate(a.requestedAt)` header "Requested on"; status header "Status" keeps "Applied"/"Pending".
- `MerchantSettlementsTab.tsx`: period cell `{formatDate(s.periodFrom)} – {formatDate(s.periodTo)}`; payable cell `{formatRs(s.totalPayable)}`; settled cell `{s.settledAt ? formatDate(s.settledAt) : "—"}`.
- Each file imports `formatRs`/`formatDate` from `@/features/cod/lib/codFormat` as needed. No behaviour changes.

- [ ] **Step 4: Run tests**

Run: `npx vitest run src/features/cod` → PASS (update any existing assertions on the old `Rs. 2500.00` strings to `formatRs(2500)`).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/cod
git commit -m "feat(web): consistent money, dates and names across COD tabs (F15 R3a)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Dashboard — rider-held split and "riders holding cash > 24 h"

**Files:**
- Modify: `src/web/src/features/reports/components/dashboard/CodRangeCard.tsx`
- Modify: `src/web/src/features/reports/components/dashboard/NeedsActionCard.tsx`
- Modify: `src/web/src/app/(staff)/dashboard/page.tsx`, `page.test.tsx`

**Interfaces:**
- Consumes: Task 3 `useCodFlow`, `useRiderCash`, `stageAmount`, `hoursHeld`, `HELD_WARNING_HOURS`; `rangeToApi(range)` (already in the dashboard page).
- Produces: `CodRangeCard({ financial, rangeLabel, range }: { financial: CodFinancialSummary; rangeLabel: string; range: { from?: string; to?: string } })`.

- [ ] **Step 1: Write the failing tests**

In `src/web/src/app/(staff)/dashboard/page.test.tsx`, add mocks next to the existing ones:

```tsx
const codFlow = vi.fn();
vi.mock("@/features/cod/hooks/useCodFlow", () => ({ useCodFlow: (p: unknown, o: unknown) => codFlow(p, o) }));
const riderCash = vi.fn();
vi.mock("@/features/cod/hooks/useRiderCash", () => ({ useRiderCash: (p: unknown, o: unknown) => riderCash(p, o) }));
```

In the file's `beforeEach` (create one if absent) set defaults:

```tsx
  codFlow.mockReturnValue({
    data: { stages: [
      { key: "expected", amount: 10000, count: 4 }, { key: "collected", amount: 8000, count: 3 }, { key: "riderDeposited", amount: 6000, count: 2 },
      { key: "branchReconciled", amount: 5000, count: 2 }, { key: "financeVerified", amount: 4000, count: 1 }, { key: "settled", amount: 1000, count: 1 },
    ] },
    isLoading: false, isError: false,
  });
  riderCash.mockReturnValue({
    data: { items: [
      { riderId: "r1", branchId: "b1", parcelCount: 2, amount: 1500, oldestCollectedAt: new Date(Date.now() - 30 * 3_600_000).toISOString(), latestCollectedAt: new Date().toISOString() },
      { riderId: "r2", branchId: "b1", parcelCount: 1, amount: 500, oldestCollectedAt: new Date(Date.now() - 2 * 3_600_000).toISOString(), latestCollectedAt: new Date().toISOString() },
    ] },
    isLoading: false, isError: false,
  });
```

Add tests (use the file's existing render helper and `perms` set; ensure `perms` includes `"cod.read"` and whatever the org dashboard needs):

```tsx
  it("shows rider-held and deposited-unverified COD from the new reads", async () => {
    renderDashboard(); // the file's existing helper
    const npr = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR", maximumFractionDigits: 0 });
    expect(await screen.findByText("Rider-held")).toBeInTheDocument();
    expect(screen.getByText(npr.format(2000))).toBeInTheDocument();
    expect(screen.getByText("Deposited, not yet verified")).toBeInTheDocument();
    expect(screen.getByText(npr.format(2000 /* 6000 − 4000 */))).toBeInTheDocument();
    expect(screen.queryByText("Rider-held vs deposited")).toBeNull();
  });

  it("lists riders holding cash over 24 h in Needs action", async () => {
    renderDashboard();
    const link = await screen.findByRole("link", { name: /Riders holding cash > 24 h/ });
    expect(link).toHaveAttribute("href", "/cod?tab=rider-deposits");
    expect(within(link).getByText("1")).toBeInTheDocument();
  });

  it("does not fetch COD reads without cod.read", () => {
    perms.delete("cod.read");
    renderDashboard();
    expect(riderCash.mock.calls.every(([, o]) => (o as { enabled: boolean }).enabled === false)).toBe(true);
    expect(screen.queryByText(/Riders holding cash/)).toBeNull();
  });
```

(If the two figures collide in `getByText` because both equal Rs 2,000, use `getAllByText(npr.format(2000))` and assert length ≥ 2. Adapt `renderDashboard`/`perms` names to what the file actually defines — read the file first.)

- [ ] **Step 2: Run to verify they fail**

Run: `npx vitest run "src/app/(staff)/dashboard"` → FAIL.

- [ ] **Step 3: Implement**

`CodRangeCard.tsx` — add `range` to the props, add imports:

```tsx
import { Skeleton } from "@/components/ui/skeleton";
import { useCodFlow } from "@/features/cod/hooks/useCodFlow";
import { useRiderCash } from "@/features/cod/hooks/useRiderCash";
import { stageAmount } from "@/features/cod/lib/flowModel";
```

inside the component after `const canCod = ...`:

```tsx
  const flow = useCodFlow(range, { enabled: canCod });
  const cash = useRiderCash({}, { enabled: canCod });
  const riderHeld = cash.data ? cash.data.items.reduce((t, h) => t + h.amount, 0) : undefined;
  const deposited = stageAmount(flow.data, "riderDeposited");
  const verified = stageAmount(flow.data, "financeVerified");
  const unverified = deposited !== undefined && verified !== undefined ? deposited - verified : undefined;
```

and replace the `<ComingSoonSlot ... "Rider-held vs deposited" ... />` line with:

```tsx
      {canCod && (
        <dl aria-label="Cash in hand" className="mt-3 grid grid-cols-2 gap-3 rounded-lg border border-border p-3">
          {[
            { label: "Rider-held", value: riderHeld, loading: cash.isLoading, error: cash.isError },
            { label: "Deposited, not yet verified", value: unverified, loading: flow.isLoading, error: flow.isError },
          ].map((cell) => (
            <div key={cell.label} className="flex flex-col gap-1">
              <dt className="text-xs text-muted-foreground">{cell.label}</dt>
              <dd className="text-base font-semibold tabular-nums">
                {cell.loading ? <Skeleton className="h-5 w-20" /> : cell.error || cell.value === undefined ? "—" : npr.format(cell.value)}
              </dd>
            </div>
          ))}
        </dl>
      )}
```

Remove the now-unused `ComingSoonSlot` import if nothing else in the file uses it.

`NeedsActionCard.tsx` — add imports `useRiderCash` and `{ HELD_WARNING_HOURS, hoursHeld }` from `@/features/cod/lib/codFormat`; add after the manifests hook:

```tsx
  const canCod = usePermission("cod.read");
  const riderCash = useRiderCash({}, { enabled: canCod });
```

append to `rows`:

```tsx
    {
      key: "cod-cash",
      label: "Riders holding cash > 24 h",
      description: "Collected COD not yet deposited",
      href: "/cod?tab=rider-deposits",
      count: riderCash.data ? riderCash.data.items.filter((h) => hoursHeld(h.oldestCollectedAt) >= HELD_WARNING_HOURS).length : undefined,
      visible: canCod && !riderCash.isError,
    },
```

and change the `ComingSoonSlot` description to `"Manifests with missing parcels and express parcels at SLA risk."`.

`app/(staff)/dashboard/page.tsx` — change `<CodRangeCard financial={org.data.financial} rangeLabel={label} />` to `<CodRangeCard financial={org.data.financial} rangeLabel={label} range={rangeToApi(range)} />`.

- [ ] **Step 4: Run tests**

Run: `npx vitest run "src/app/(staff)/dashboard" src/features/reports` → PASS. Update `NeedsActionCard`/`CodRangeCard` unit tests if they exist and now need the two new hook mocks.

- [ ] **Step 5: Full web verification and commit**

Run in `src/web`: `npm test`, `npm run lint`, `npx tsc --noEmit` (8 baseline errors only), `npm run build`.

```bash
git add src/web/src
git commit -m "feat(web): dashboard rider-held COD and riders-holding-cash alert (F15 R3a)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Documentation

**Files:**
- Modify: `docs/API-SPEC.md`, `docs/FRONTEND-ROADMAP.md`, `DESIGN.md`, `docs/CLARIFICATIONS.md`

- [ ] **Step 1: API-SPEC.md**

Find the COD section (`grep -n "cod/rider-settlements\|/cod/" docs/API-SPEC.md`). Add:

```markdown
### GET /api/v1/cod/flow

Staff only (`cod.read`); merchant callers → 403. Query: `branchId` (narrows within scope), `from`,
`to` (inclusive, on COD collection `CreatedAt`, normalised to UTC; `from > to` → 400
`COD_VALIDATION_FAILED`). Returns six cumulative ("reached") stages, in order:
`expected, collected, riderDeposited, branchReconciled, financeVerified, settled`, each
`{ key, amount, count }`. `expected` sums `ExpectedAmount` over all rows; later stages sum
`CollectedAmount` over rows that reached that status or later. Totals never increase left to right.

### GET /api/v1/cod/rider-cash

Staff only (`cod.read`); merchant callers → 403. Query: `branchId`. Cash collected but not yet
deposited (`CodCollected`), one row per rider + branch:
`{ riderId, branchId, parcelCount, amount, oldestCollectedAt, latestCollectedAt }`, oldest first.
```

Under `GET /api/v1/cod/rider-settlements` add the `from`/`to` query parameters (on `SettledAt`, UTC, `from > to` → 400) and document the two new response fields `expectedAmount` (Σ collected amount of the settlement's linked collections) and `variance` (`declaredAmount − expectedAmount`; negative = short). Under `GET /api/v1/cod/collections` add the `riderCashSettlementId` filter.

- [ ] **Step 2: FRONTEND-ROADMAP.md**

In the Phase F15 table replace the R3 row with two rows:

```markdown
| R3a — Staff finance | COD reconciliation: money-flow strip, rider deposits + variance, record-deposit Sheet, merchant settlement breakdown | Done — spec `docs/superpowers/specs/2026-10-07-f15-r3a-cod-finance-design.md` |
| R3b — Staff network | Manifests, NDR, RTO, riders, live map, branches (artboards first) | Planned |
```

Below the R2 follow-ups add:

```markdown
**Backend follow-ups from R3a:**
1. Shortage/overage reason + approval on rider deposits (adjustments for `RiderCashSettlement`, threshold-based Finance Manager approval).
2. Cash denomination breakdown on deposits.
3. Digital (Fonepay/QR) vs cash split on deposits.
4. Close branch day (lock + day report).
5. COD ledger read + CSV export.
6. Link COD collections to delivery runs (run id on deposits).
7. Separate COD handling-fee line on merchant settlement items.
```

and remove R2 follow-up item 5 ("COD split: rider-held vs deposited-unverified") or mark it "(done in R3a)".

- [ ] **Step 3: DESIGN.md and CLARIFICATIONS.md**

In `DESIGN.md`, next to the R2 component notes, add (match the file's rule style):

- **Money-flow strip** — six cumulative COD stages as connected cards (step number, label, `Rs` amount, parcel count); exactly one card highlighted cobalt: the stage before the largest drop. Cards switch to the tab that moves that money on. Loading = skeleton, error = ErrorState — never zeros.
- **Variance wording** — always text: "Matched", "Short − Rs N", "Over + Rs N"; the status-colour dot is secondary. Previews computed in the browser are labelled "(preview)".

Append to `docs/CLARIFICATIONS.md` (next free number — check the last heading first; 26 if 25 is last):

```markdown
## 26. COD flow stages are cumulative and keyed on creation date (2026-10-07)

`GET /cod/flow` counts a collection in every stage it has *reached* (e.g. a Settled parcel counts
in collected, rider deposited, branch reconciled, finance verified and settled), and its date range
filters on the collection's `CreatedAt` ("parcels booked in this period, and how far their money
got"). `GET /cod/reconciliation`'s `expectedCod` is different on purpose: it sums only collections
still at `CodExpected` (outstanding), filtered by `CollectedAt`. The two endpoints answer different
questions and both remain.
```

- [ ] **Step 4: Commit**

```bash
git add docs DESIGN.md
git commit -m "docs: F15 R3a COD endpoints, roadmap split, design rules and clarification

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Self-Review Notes (plan author)

- Spec coverage: §3.1 → Task 2; §3.2 → Task 2; §3.3 → Task 1; §4 → Task 4; §5 → Tasks 3–4; §6.1–6.3 → Task 5; §7.1 → Task 6; §7.2 → Task 7; §7.3 → Task 4; §8 → Task 8; §9 tests are embedded per task; §11 → Task 9.
- Spec correction made while planning (spec updated in the same commit as this plan): §7.1 "Approved adjustments" is a caption ("Includes Rs N from approved adjustments"), not a subtracted line — applying an adjustment already rewrites the item's `otherDeductions`, `netPayable` and the settlement `totalPayable`, so a separate line would double-count.
- Deliberate choices recorded in task text: rider settlements' date filter goes through `HandleAsync` so an inverted range is a 400; `useCreateRiderSettlement` invalidates the whole `["cod"]` prefix; the record-deposit Sheet closes on success (spec §6.3) instead of opening the detail Sheet; deleted dead code (`ReconciliationTab`, `useCodReconciliation`, `useCodSummary`, `RiderSettlementsTab`) only after a grep shows no importers.
