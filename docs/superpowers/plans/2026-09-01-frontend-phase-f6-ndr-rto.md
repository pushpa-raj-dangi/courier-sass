# Frontend Phase F6 — NDR + RTO Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the staff back-office UI for the NDR workflow (CLAUDE.md §20) and RTO tracking (CLAUDE.md §21), closing 5 real backend gaps found during design (3 unconverted enums, no AWB on either NDR/RTO response, no shipment-scoped lookup filters).

**Architecture:** 5 backend tasks fix enum serialization and enrich the existing `Ndr` module's two read surfaces (`NdrResponse`, `ReturnShipmentResponse`) with the AWB fields and lookup filters a staff UI needs, touching no schema. 8 frontend tasks build `src/web/src/features/ndr/` following the exact conventions F2–F5 already established (Sheet-over-pages via Zustand panel stores, TanStack Query hooks per endpoint, Next.js Route Handlers as a thin BFF proxy, one status-driven section component per NDR status).

**Tech Stack:** ASP.NET Core / EF Core / xUnit (backend); Next.js 14 App Router, TanStack Query, Zustand, Vitest + Testing Library (frontend). No new npm/NuGet packages.

**Spec:** `docs/superpowers/specs/2026-09-01-frontend-phase-f6-ndr-rto-design.md`

## Global Constraints

- Detail UI is always a right-side `Sheet` driven by a Zustand store, never a routed page (`docs/ARCHITECTURE.md` §9a). Neither `NdrDetailSheet` nor `ReturnShipmentDetailSheet` gets a create mode — both entities are only ever system-created.
- Price, COD amount, permissions, and branch scope are never trusted from the frontend — every new screen only reads/writes through the existing permission-checked backend endpoints (CLAUDE.md §33).
- Every new screen gets loading/error/empty states using the existing `LoadingState`/`ErrorState`/`EmptyState`/`Spinner` primitives — no ad hoc spinners.
- `NdrStatus`'s only legal forward moves (`NdrService.IsLegalForwardMove`) are `Open→Contacting`, `Contacting→CustomerConfirmed`, `CustomerConfirmed→{ReattemptScheduled|RtoRequested}`. `Resolved` is never a directly-settable status — only `POST /resolve` sets it, as a side effect of `NdrResolution.{Reattempt|Rto|AutoRto|Other}`. No frontend code should ever construct an `UpdateNdrRequest` targeting `Resolved` or a skipped/backward move — the backend rejects it, but the UI shouldn't offer it in the first place.
- `GET /api/v1/ndrs` is paginated (`PagedResult<NdrResponse>`, matches `@/components/data-table/DataTable`'s `PagedResult<T>` exactly — `items`/`page`/`pageSize`/`totalCount`/`totalPages`, all camelCase). `GET /api/v1/return-shipments` is a plain, non-paginated array — matches Manifests' `GET /api/v1/manifests` precedent. Don't build pagination UI for return shipments.
- All 3 Ndr enums (`NdrStatus`, `NdrReason`, `NdrResolution`) serialize as PascalCase strings once Task 1 lands — every later task's TypeScript types use string literal unions, never numbers.
- Backend test command: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj` (unit) and `dotnet test tests/Api.Tests/Api.Tests.csproj --filter NdrEndpointsTests` (integration, needs the local Postgres container per `[[Courier Local Postgres]]` — already running on port 5434, do not start a second one). Frontend: `cd src/web && npm test` (Vitest) and `npm run build` (production build/type-check) after every frontend task.

---

## Task 1: Fix Ndr enum JSON serialization; record the gap in CLARIFICATIONS.md

**Files:**
- Modify: `src/Modules/Ndr/Domain/NdrStatus.cs`
- Modify: `src/Modules/Ndr/Domain/NdrReason.cs`
- Modify: `src/Modules/Ndr/Domain/NdrResolution.cs`
- Modify: `docs/CLARIFICATIONS.md`
- Test: `tests/Ndr.Tests/NdrEnumSerializationTests.cs`

**Interfaces:**
- Produces: all 3 enums now carry `[JsonConverter(typeof(JsonStringEnumConverter))]`, matching `ShipmentStatus`/`ManifestStatus`/the 5 Riders enums' existing fix. Every later task's DTOs using these enums serialize as strings.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using Courier.Ndr.Domain;
using Xunit;

namespace Courier.Ndr.Tests;

public class NdrEnumSerializationTests
{
    [Fact]
    public void NdrStatus_SerializesAsString() =>
        Assert.Equal("\"Open\"", JsonSerializer.Serialize(NdrStatus.Open));

    [Fact]
    public void NdrReason_SerializesAsString() =>
        Assert.Equal("\"CustomerUnavailable\"", JsonSerializer.Serialize(NdrReason.CustomerUnavailable));

    [Fact]
    public void NdrResolution_SerializesAsString() =>
        Assert.Equal("\"Reattempt\"", JsonSerializer.Serialize(NdrResolution.Reattempt));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj --filter NdrEnumSerializationTests`
Expected: FAIL — each assertion gets `"0"`/`"1"` etc. instead of the quoted name.

- [ ] **Step 3: Add the converter to each enum**

`src/Modules/Ndr/Domain/NdrStatus.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Courier.Ndr.Domain;

// Exact list from CLAUDE.md §20. Legal forward moves (enforced in NdrService.UpdateAsync, Task 7):
// Open -> Contacting -> CustomerConfirmed -> {ReattemptScheduled | RtoRequested} -> Resolved.
// No backward or skipped moves.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NdrStatus
{
    Open,
    Contacting,
    CustomerConfirmed,
    ReattemptScheduled,
    RtoRequested,
    Resolved
}
```

`src/Modules/Ndr/Domain/NdrReason.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Courier.Ndr.Domain;

// Deliberately duplicated from Riders.Domain.DeliveryAttemptReason (identical 9 values, CLAUDE.md
// §19) rather than referenced — avoids Ndr.csproj needing a project reference to Riders.csproj
// purely for one enum's type, mirroring how Riders.Domain.DeliveryAttemptReason was itself already
// a fresh copy of CLAUDE.md §19's list, not shared from anywhere else. Riders.Application.
// RiderService converts its own DeliveryAttemptReason to this one via an explicit switch
// expression (Task 6), not a raw int cast — so a future divergence between the two lists fails
// loudly at compile time instead of silently miscasting.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NdrReason
{
    CustomerUnavailable,
    PhoneUnreachable,
    WrongAddress,
    IncompleteAddress,
    CustomerRefused,
    CustomerRequestedReschedule,
    CashUnavailable,
    DamagedParcel,
    Other
}
```

`src/Modules/Ndr/Domain/NdrResolution.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Courier.Ndr.Domain;

// Exact list from DOMAIN-MODEL.md's NDR entity. AutoRTO is a selectable resolution outcome only
// this phase (this plan's resolved design decision) — no scheduled/cron trigger exists to set it
// automatically; a human or a future automated caller must still call POST /ndrs/{id}/resolve.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NdrResolution
{
    Reattempt,
    Rto,
    AutoRto,
    Other
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj --filter NdrEnumSerializationTests`
Expected: PASS (3/3)

- [ ] **Step 5: Record the gap in `docs/CLARIFICATIONS.md`**

Find this exact text near the end of the file:

```
---

## Summary of schema additions implied by this document
```

Replace it with (inserting a new `## 16.` section before the summary):

```
---

## 16. Frontend Phase F6: `Ndr` responses need shipment AWB, and no lookup-by-shipment filters exist

**Where:** `Ndr.Application.NdrDtos` (`NdrResponse`, `ReturnShipmentResponse`), `Ndr.Presentation.NdrEndpoints`

**Gap 1 — no AWB on either response.** `NdrResponse` and `ReturnShipmentResponse` both carry only
an opaque `ShipmentId`/`OriginalShipmentId` Guid — useless for a staff-facing queue or
return-tracking screen, which needs the human-facing AWB. `NdrService.UpdateAsync`/`ResolveAsync`
and `ToReturnShipmentResponseAsync` already fetch `IShipmentTransitionService.GetScanInfoAsync`
(which carries `Awb`) for other reasons; `NdrService.GetByIdAsync` and `ListAsync`'s all-branches
path do not yet.

**Resolution:** `NdrResponse` gains `Awb`; `ReturnShipmentResponse` gains `OriginalAwb`. Both are
sourced from the same `ShipmentScanInfo` lookup every affected method already performs or now
performs once per row (bounded by page size, same cost class as every other paginated list in this
codebase).

**Gap 2 — no way to find "the NDR(s)/return shipment for shipment X".** Neither
`GET /api/v1/ndrs` nor `GET /api/v1/return-shipments` accepts a shipment-scoped filter, so a
shipment's own detail view has no way to discover its NDR history or RTO linkage.

**Resolution:** `GET /api/v1/ndrs` gains an optional `shipmentId` query parameter (composes with
the existing `status` filter and branch-scoping); `GET /api/v1/return-shipments` gains an optional
`originalShipmentId` query parameter (composes with the existing branch-scoping). A shipment may
accumulate more than one `NDR` over its lifetime (at most one unresolved at a time, per
`NdrConfiguration.cs`'s documented invariant) but at most one `ReturnShipment` (RTO is a terminal
path with no reopening logic in this codebase).

Discovered planning Frontend Phase F6 (`docs/FRONTEND-ROADMAP.md`,
`docs/superpowers/specs/2026-09-01-frontend-phase-f6-ndr-rto-design.md`). Implemented as Tasks 2-5
of the Frontend Phase F6 plan.

---

## Summary of schema additions implied by this document
```

- [ ] **Step 6: Run the full Ndr test suite to check for regressions, then commit**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj`
Expected: PASS (all tests)

```bash
git add src/Modules/Ndr/Domain docs/CLARIFICATIONS.md tests/Ndr.Tests/NdrEnumSerializationTests.cs
git commit -m "fix(ndr): serialize all 3 Ndr enums as strings, not raw integers"
```

---

## Task 2: Add `Awb` to `NdrResponse`

**Files:**
- Modify: `src/Modules/Ndr/Application/NdrDtos.cs`
- Modify: `src/Modules/Ndr/Application/NdrService.cs`
- Modify: `src/Modules/Ndr/Application/NdrService.Resolve.cs`
- Modify: `src/Modules/Ndr/Application/NdrService.Reads.cs`
- Modify: `tests/Ndr.Tests/NdrServiceListTests.cs`
- Test (existing, extended in Step 1): `tests/Ndr.Tests/NdrServiceListTests.cs`

**Interfaces:**
- Consumes: `IShipmentTransitionService.GetScanInfoAsync(Guid, CancellationToken)` → `ShipmentScanInfo?` (has `.Awb`), already used elsewhere in this file (`src/Modules/Shipments/Application/IShipmentTransitionService.cs`).
- Produces: `NdrResponse.Awb` (string) — consumed by every frontend task from Task 6 onward. `ToResponse(NDR ndr, string awb)` — new required second parameter, all 4 call sites in this module updated in this task.

- [ ] **Step 1: Write the failing tests**

In `tests/Ndr.Tests/NdrServiceListTests.cs`, add a file-local `AllBranchesAuthorizationChecker` (the `RestrictedBranchAuthorizationChecker` already there only covers the branch-restricted path) and two new tests. Insert this class after the existing `RestrictedBranchAuthorizationChecker` class (before `FakeShipmentTransitionService`):

```csharp
file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
```

Add these two tests inside `public class NdrServiceListTests { ... }`, after `ListReturnShipmentsAsync_BranchRestricted_FiltersToAccessibleBranchOnly`:

```csharp
    [Fact]
    public async Task ListAsync_AllBranches_IncludesShipmentAwb()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();

        var ndr = new NDR { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = shipmentId, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open };
        dbContext.Set<NDR>().Add(ndr);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[shipmentId] = new ShipmentScanInfo(shipmentId, organizationId, "AWB999", ShipmentStatus.NdrCreated, null, Guid.NewGuid(), Guid.NewGuid(), null);

        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());

        var result = await service.ListAsync(null, null, null, ActingUser(Guid.NewGuid()));

        Assert.Equal("AWB999", Assert.Single(result.Items).Awb);
    }

    [Fact]
    public async Task GetByIdAsync_IncludesShipmentAwb()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();

        var ndr = new NDR { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = shipmentId, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open };
        dbContext.Set<NDR>().Add(ndr);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[shipmentId] = new ShipmentScanInfo(shipmentId, organizationId, "AWB998", ShipmentStatus.NdrCreated, null, Guid.NewGuid(), Guid.NewGuid(), null);

        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());

        var result = await service.GetByIdAsync(ndr.Id);

        Assert.Equal("AWB998", result!.Awb);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj --filter "ListAsync_AllBranches_IncludesShipmentAwb|GetByIdAsync_IncludesShipmentAwb"`
Expected: FAIL — compile error, `NdrResponse` has no `Awb` member yet.

- [ ] **Step 3: Add `Awb` to `NdrResponse`**

In `src/Modules/Ndr/Application/NdrDtos.cs`, change:

```csharp
public record NdrResponse(
    Guid Id, Guid ShipmentId, NdrReason Reason, Guid? AssignedStaffUserId, NdrStatus Status,
    string? CustomerResponse, DateTimeOffset? FollowUpAt, string? NextAction, NdrResolution? Resolution, DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt);
```

to:

```csharp
public record NdrResponse(
    Guid Id, Guid ShipmentId, string Awb, NdrReason Reason, Guid? AssignedStaffUserId, NdrStatus Status,
    string? CustomerResponse, DateTimeOffset? FollowUpAt, string? NextAction, NdrResolution? Resolution, DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt);
```

- [ ] **Step 4: Thread `Awb` through `ToResponse` and its call sites**

In `src/Modules/Ndr/Application/NdrService.cs`, change:

```csharp
    private static NdrResponse ToResponse(NDR ndr) => new(
        ndr.Id, ndr.ShipmentId, ndr.Reason, ndr.AssignedStaffUserId, ndr.Status,
        ndr.CustomerResponse, ndr.FollowUpAt, ndr.NextAction, ndr.Resolution, ndr.ResolvedAt,
        ndr.CreatedAt);
```

to:

```csharp
    private static NdrResponse ToResponse(NDR ndr, string awb) => new(
        ndr.Id, ndr.ShipmentId, awb, ndr.Reason, ndr.AssignedStaffUserId, ndr.Status,
        ndr.CustomerResponse, ndr.FollowUpAt, ndr.NextAction, ndr.Resolution, ndr.ResolvedAt,
        ndr.CreatedAt);
```

and, in the same file's `UpdateAsync`, change `return ToResponse(ndr);` to `return ToResponse(ndr, scanInfo.Awb);` (`scanInfo` is already in scope from the branch-access check above it).

In `src/Modules/Ndr/Application/NdrService.Resolve.cs`'s `ResolveAsync`, change `return ToResponse(ndr);` to `return ToResponse(ndr, scanInfo.Awb);` (`scanInfo` is likewise already in scope).

- [ ] **Step 5: Enrich `GetByIdAsync` and both `ListAsync` paths**

Replace the full contents of `src/Modules/Ndr/Application/NdrService.Reads.cs` with:

```csharp
using System.Security.Claims;
using Courier.Ndr.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public partial class NdrService
{
    public async Task<NdrResponse?> GetByIdAsync(Guid ndrId, CancellationToken cancellationToken = default)
    {
        var ndr = await _dbContext.Set<NDR>().FirstOrDefaultAsync(n => n.Id == ndrId, cancellationToken);
        if (ndr is null)
        {
            return null;
        }
        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(ndr.ShipmentId, cancellationToken);
        return ToResponse(ndr, scanInfo?.Awb ?? string.Empty);
    }

    // Branch-scoped the same way RiderService.ListAsync (Phase 8) and ShipmentService.ListAsync
    // (Phase 6) already are: null from GetAccessibleBranchIdsAsync means AllBranches (no filter).
    // NDR has no branch column of its own — its shipment's DestinationBranchId (the branch that
    // attempted delivery and owns following up, same branch UpdateAsync's CheckBranchAccessAsync
    // call already scopes to) is the relevant scope. That requires a per-row shipment lookup via
    // IShipmentTransitionService.GetScanInfoAsync, which can't be pushed into the SQL query, so we
    // only pay that cost when the caller is actually branch-restricted.
    public async Task<PagedResult<NdrResponse>> ListAsync(NdrStatus? status, int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);

        var query = _dbContext.Set<NDR>().AsQueryable();
        if (status is not null)
        {
            query = query.Where(n => n.Status == status);
        }
        query = query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id);

        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "ndr.read", cancellationToken);
        if (accessibleBranchIds is null)
        {
            // AllBranches scope: pagination still happens at the SQL level; each row on the page
            // (bounded by pageSize) is then enriched with its shipment's Awb — the same per-row
            // cost the branch-restricted path below already pays for every candidate.
            var totalCount = await query.CountAsync(cancellationToken);
            var ndrs = await query
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToListAsync(cancellationToken);
            var pageResponses = new List<NdrResponse>();
            foreach (var ndr in ndrs)
            {
                var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(ndr.ShipmentId, cancellationToken);
                pageResponses.Add(ToResponse(ndr, scanInfo?.Awb ?? string.Empty));
            }
            return new PagedResult<NdrResponse>(pageResponses, normalizedPage, normalizedPageSize, totalCount);
        }

        // Branch-restricted caller: NDR has no branch column of its own (NdrConfiguration.cs), so
        // the branch check requires a per-row shipment lookup that can't be pushed into SQL — same
        // shape ListAsync already used before pagination. Pagination is applied after the
        // in-memory filter for this path only (documented limitation, not a regression). The same
        // per-row ShipmentScanInfo fetched for the branch check is cached here and reused for Awb
        // enrichment below, rather than fetched twice for a row that survives filtering.
        var candidates = await query.ToListAsync(cancellationToken);
        var scoped = new List<NDR>();
        var scanInfoByShipmentId = new Dictionary<Guid, ShipmentScanInfo>();
        foreach (var ndr in candidates)
        {
            var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(ndr.ShipmentId, cancellationToken);
            if (scanInfo is not null && accessibleBranchIds.Contains(scanInfo.DestinationBranchId))
            {
                scoped.Add(ndr);
                scanInfoByShipmentId[ndr.ShipmentId] = scanInfo;
            }
        }

        var pagedScoped = scoped.Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize).ToList();
        var scopedResponses = pagedScoped.Select(ndr => ToResponse(ndr, scanInfoByShipmentId[ndr.ShipmentId].Awb)).ToList();
        return new PagedResult<NdrResponse>(scopedResponses, normalizedPage, normalizedPageSize, scoped.Count);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj --filter "ListAsync_AllBranches_IncludesShipmentAwb|GetByIdAsync_IncludesShipmentAwb"`
Expected: PASS (2/2)

- [ ] **Step 7: Run the full Ndr test suite to check for regressions, then commit**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj`
Expected: PASS (all tests) — the pre-existing `UpdateAsync`/`ResolveAsync` tests still pass since those call sites now pass `scanInfo.Awb`, which was already in scope.

```bash
git add src/Modules/Ndr/Application/NdrDtos.cs src/Modules/Ndr/Application/NdrService.cs src/Modules/Ndr/Application/NdrService.Resolve.cs src/Modules/Ndr/Application/NdrService.Reads.cs tests/Ndr.Tests/NdrServiceListTests.cs
git commit -m "feat(ndr): add shipment Awb to NdrResponse"
```

---

## Task 3: Add `shipmentId` filter to `GET /api/v1/ndrs`

**Files:**
- Modify: `src/Modules/Ndr/Application/NdrService.Reads.cs`
- Modify: `src/Modules/Ndr/Presentation/NdrEndpoints.cs`
- Modify: `tests/Ndr.Tests/NdrServiceListTests.cs`
- Modify: `tests/Api.Tests/NdrEndpointsTests.cs`

**Interfaces:**
- Produces: `NdrService.ListAsync(NdrStatus? status, int? page, int? pageSize, Guid? shipmentId, ClaimsPrincipal actingUser, CancellationToken)` — note `shipmentId` is inserted as the 4th positional parameter, before `actingUser`. Consumed by Task 6's `useNdrs` hook (`shipmentId` query param on `GET /api/v1/ndrs`).

- [ ] **Step 1: Write the failing test**

Add to `tests/Ndr.Tests/NdrServiceListTests.cs`, after `ListAsync_AllBranches_IncludesShipmentAwb`:

```csharp
    [Fact]
    public async Task ListAsync_FilteredByShipmentId_ReturnsOnlyMatchingNdrs()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var targetShipmentId = Guid.NewGuid();
        var otherShipmentId = Guid.NewGuid();

        var targetNdr = new NDR { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = targetShipmentId, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open };
        var otherNdr = new NDR { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = otherShipmentId, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open };
        dbContext.Set<NDR>().AddRange(targetNdr, otherNdr);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[targetShipmentId] = new ShipmentScanInfo(targetShipmentId, organizationId, "AWB001", ShipmentStatus.NdrCreated, null, Guid.NewGuid(), Guid.NewGuid(), null);
        shipments.ShipmentsById[otherShipmentId] = new ShipmentScanInfo(otherShipmentId, organizationId, "AWB002", ShipmentStatus.NdrCreated, null, Guid.NewGuid(), Guid.NewGuid(), null);

        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());

        var result = await service.ListAsync(null, null, null, targetShipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(targetNdr.Id, Assert.Single(result.Items).Id);
    }
```

Update the two existing call sites in this file that now need a `shipmentId` argument — `ListAsync_AllBranches_IncludesShipmentAwb` and `ListAsync_BranchRestricted_FiltersToAccessibleBranchOnly` — changing `service.ListAsync(null, null, null, ActingUser(Guid.NewGuid()))` to `service.ListAsync(null, null, null, null, ActingUser(Guid.NewGuid()))` in both.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj --filter NdrServiceListTests`
Expected: FAIL — compile error, `ListAsync` doesn't accept a `shipmentId` argument yet.

- [ ] **Step 3: Add the `shipmentId` parameter and filter**

In `src/Modules/Ndr/Application/NdrService.Reads.cs`, change the `ListAsync` signature and add the filter:

```csharp
    public async Task<PagedResult<NdrResponse>> ListAsync(NdrStatus? status, int? page, int? pageSize, Guid? shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);

        var query = _dbContext.Set<NDR>().AsQueryable();
        if (status is not null)
        {
            query = query.Where(n => n.Status == status);
        }
        if (shipmentId is not null)
        {
            query = query.Where(n => n.ShipmentId == shipmentId);
        }
        query = query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id);
```

(the rest of the method body is unchanged from Task 2).

- [ ] **Step 4: Wire the query parameter into the endpoint**

In `src/Modules/Ndr/Presentation/NdrEndpoints.cs`, change:

```csharp
        group.MapGet("/", async (NdrStatus? status, int? page, int? pageSize, NdrService service, HttpContext httpContext) =>
            Results.Ok(await service.ListAsync(status, page, pageSize, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("ndr.read");
```

to:

```csharp
        group.MapGet("/", async (NdrStatus? status, int? page, int? pageSize, Guid? shipmentId, NdrService service, HttpContext httpContext) =>
            Results.Ok(await service.ListAsync(status, page, pageSize, shipmentId, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("ndr.read");
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj --filter NdrServiceListTests`
Expected: PASS (all tests in this file)

- [ ] **Step 6: Strengthen the existing integration test to exercise the new filter over real HTTP**

In `tests/Api.Tests/NdrEndpointsTests.cs`, change `GetOpenNdrForShipmentAsync` from a client-side filter to using the new server-side filter (this exercises the new query parameter through the real ASP.NET Core route binder against real Postgres, on every test in this file that already calls this helper):

```csharp
    private async Task<NdrResponse> GetOpenNdrForShipmentAsync(HttpClient client, Guid shipmentId)
    {
        var ndrs = await (await client.GetAsync($"/api/v1/ndrs?shipmentId={shipmentId}")).Content.ReadFromJsonAsync<PagedResult<NdrResponse>>();
        return ndrs!.Items.Single();
    }
```

- [ ] **Step 7: Run the full Ndr and Api test suites, then commit**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj && dotnet test tests/Api.Tests/Api.Tests.csproj --filter NdrEndpointsTests`
Expected: PASS (all tests)

```bash
git add src/Modules/Ndr/Application/NdrService.Reads.cs src/Modules/Ndr/Presentation/NdrEndpoints.cs tests/Ndr.Tests/NdrServiceListTests.cs tests/Api.Tests/NdrEndpointsTests.cs
git commit -m "feat(ndr): add shipmentId filter to GET /api/v1/ndrs"
```

---

## Task 4: Add `OriginalAwb` to `ReturnShipmentResponse`

**Files:**
- Modify: `src/Modules/Ndr/Application/NdrDtos.cs`
- Modify: `src/Modules/Ndr/Application/NdrService.ReturnShipments.cs`
- Modify: `tests/Ndr.Tests/NdrServiceReturnShipmentTests.cs`

**Interfaces:**
- Produces: `ReturnShipmentResponse.OriginalAwb` (string) — consumed by every frontend task from Task 6 onward, and is the direct implementation of the exit criteria's "linkage back to its original AWB is visible on both records" (this is the half that lives on the return-shipment side).

- [ ] **Step 1: Write the failing test**

In `tests/Ndr.Tests/NdrServiceReturnShipmentTests.cs`, add an assertion to the existing `GetReturnShipmentByIdAsync_ReflectsLiveShipmentStatus` test:

```csharp
    [Fact]
    public async Task GetReturnShipmentByIdAsync_ReflectsLiveShipmentStatus()
    {
        await using var dbContext = CreateContext();
        var (returnShipment, service, _) = await SeedReturnShipmentAsync(dbContext, Guid.NewGuid(), ShipmentStatus.RtoAtOrigin);

        var result = await service.GetReturnShipmentByIdAsync(returnShipment.Id);

        Assert.Equal(ShipmentStatus.RtoAtOrigin, result!.CurrentStatus);
        Assert.Equal(returnShipment.ReturnAwb, result.ReturnAwb);
        Assert.Equal("AWB001", result.OriginalAwb);
    }
```

(`SeedReturnShipmentAsync` already seeds the original shipment's `ShipmentScanInfo` with `Awb = "AWB001"` — no other change needed to the seed helper.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj --filter GetReturnShipmentByIdAsync_ReflectsLiveShipmentStatus`
Expected: FAIL — compile error, `ReturnShipmentResponse` has no `OriginalAwb` member yet.

- [ ] **Step 3: Add `OriginalAwb` to `ReturnShipmentResponse`**

In `src/Modules/Ndr/Application/NdrDtos.cs`, change:

```csharp
public record ReturnShipmentResponse(
    Guid Id, Guid OriginalShipmentId, string ReturnAwb, string RtoReason, Guid OriginBranchId, Guid DestinationBranchId,
    ShipmentStatus CurrentStatus, Guid? CurrentBranchId, DateTimeOffset? MerchantConfirmedAt);
```

to:

```csharp
public record ReturnShipmentResponse(
    Guid Id, Guid OriginalShipmentId, string OriginalAwb, string ReturnAwb, string RtoReason, Guid OriginBranchId, Guid DestinationBranchId,
    ShipmentStatus CurrentStatus, Guid? CurrentBranchId, DateTimeOffset? MerchantConfirmedAt);
```

- [ ] **Step 4: Populate it in `ToReturnShipmentResponseAsync`**

In `src/Modules/Ndr/Application/NdrService.ReturnShipments.cs`, change:

```csharp
    private async Task<ReturnShipmentResponse> ToReturnShipmentResponseAsync(ReturnShipment returnShipment, CancellationToken cancellationToken)
    {
        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(returnShipment.OriginalShipmentId, cancellationToken);
        return new ReturnShipmentResponse(
            returnShipment.Id, returnShipment.OriginalShipmentId, returnShipment.ReturnAwb, returnShipment.RtoReason,
            returnShipment.OriginBranchId, returnShipment.DestinationBranchId,
            scanInfo?.CurrentStatus ?? ShipmentStatus.RtoInitiated, scanInfo?.CurrentBranchId, returnShipment.MerchantConfirmedAt);
    }
```

to:

```csharp
    private async Task<ReturnShipmentResponse> ToReturnShipmentResponseAsync(ReturnShipment returnShipment, CancellationToken cancellationToken)
    {
        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(returnShipment.OriginalShipmentId, cancellationToken);
        return new ReturnShipmentResponse(
            returnShipment.Id, returnShipment.OriginalShipmentId, scanInfo?.Awb ?? string.Empty, returnShipment.ReturnAwb, returnShipment.RtoReason,
            returnShipment.OriginBranchId, returnShipment.DestinationBranchId,
            scanInfo?.CurrentStatus ?? ShipmentStatus.RtoInitiated, scanInfo?.CurrentBranchId, returnShipment.MerchantConfirmedAt);
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj --filter GetReturnShipmentByIdAsync_ReflectsLiveShipmentStatus`
Expected: PASS

- [ ] **Step 6: Run the full Ndr test suite to check for regressions, then commit**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj`
Expected: PASS (all tests)

```bash
git add src/Modules/Ndr/Application/NdrDtos.cs src/Modules/Ndr/Application/NdrService.ReturnShipments.cs tests/Ndr.Tests/NdrServiceReturnShipmentTests.cs
git commit -m "feat(ndr): add original shipment Awb to ReturnShipmentResponse"
```

---

## Task 5: Add `originalShipmentId` filter to `GET /api/v1/return-shipments`

**Files:**
- Modify: `src/Modules/Ndr/Application/NdrService.ReturnShipments.cs`
- Modify: `src/Modules/Ndr/Presentation/NdrEndpoints.cs`
- Modify: `tests/Ndr.Tests/NdrServiceListTests.cs`
- Modify: `tests/Api.Tests/NdrEndpointsTests.cs`

**Interfaces:**
- Produces: `NdrService.ListReturnShipmentsAsync(Guid? originalShipmentId, ClaimsPrincipal actingUser, CancellationToken)` — `originalShipmentId` inserted as the first parameter. Consumed by Task 6's `useReturnShipments` hook (`originalShipmentId` query param on `GET /api/v1/return-shipments`).

- [ ] **Step 1: Write the failing test**

Add to `tests/Ndr.Tests/NdrServiceListTests.cs`, after `ListAsync_FilteredByShipmentId_ReturnsOnlyMatchingNdrs`:

```csharp
    [Fact]
    public async Task ListReturnShipmentsAsync_FilteredByOriginalShipmentId_ReturnsOnlyMatching()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var targetOriginalShipmentId = Guid.NewGuid();
        var otherOriginalShipmentId = Guid.NewGuid();

        var targetReturnShipment = new ReturnShipment
        {
            Id = Guid.NewGuid(), OriginalShipmentId = targetOriginalShipmentId, OrganizationId = organizationId,
            ReturnAwb = "RTO-20260901-001", RtoReason = "Customer refused",
            OriginBranchId = Guid.NewGuid(), DestinationBranchId = Guid.NewGuid()
        };
        var otherReturnShipment = new ReturnShipment
        {
            Id = Guid.NewGuid(), OriginalShipmentId = otherOriginalShipmentId, OrganizationId = organizationId,
            ReturnAwb = "RTO-20260901-002", RtoReason = "Wrong address",
            OriginBranchId = Guid.NewGuid(), DestinationBranchId = Guid.NewGuid()
        };
        dbContext.Set<ReturnShipment>().AddRange(targetReturnShipment, otherReturnShipment);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[targetOriginalShipmentId] = new ShipmentScanInfo(targetOriginalShipmentId, organizationId, "AWB001", ShipmentStatus.RtoAtOrigin, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);
        shipments.ShipmentsById[otherOriginalShipmentId] = new ShipmentScanInfo(otherOriginalShipmentId, organizationId, "AWB002", ShipmentStatus.RtoAtOrigin, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());

        var result = await service.ListReturnShipmentsAsync(targetOriginalShipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(targetReturnShipment.Id, Assert.Single(result).Id);
    }
```

Update the existing `ListReturnShipmentsAsync_BranchRestricted_FiltersToAccessibleBranchOnly` test's call site from `service.ListReturnShipmentsAsync(ActingUser(Guid.NewGuid()))` to `service.ListReturnShipmentsAsync(null, ActingUser(Guid.NewGuid()))`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj --filter "ListReturnShipmentsAsync"`
Expected: FAIL — compile error, `ListReturnShipmentsAsync` doesn't accept an `originalShipmentId` argument yet.

- [ ] **Step 3: Add the `originalShipmentId` parameter and filter**

In `src/Modules/Ndr/Application/NdrService.ReturnShipments.cs`, change:

```csharp
    public async Task<List<ReturnShipmentResponse>> ListReturnShipmentsAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<ReturnShipment>().AsQueryable();
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "rto.read", cancellationToken);
```

to:

```csharp
    public async Task<List<ReturnShipmentResponse>> ListReturnShipmentsAsync(Guid? originalShipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<ReturnShipment>().AsQueryable();
        if (originalShipmentId is not null)
        {
            query = query.Where(r => r.OriginalShipmentId == originalShipmentId);
        }
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "rto.read", cancellationToken);
```

(the rest of the method body is unchanged).

- [ ] **Step 4: Wire the query parameter into the endpoint**

In `src/Modules/Ndr/Presentation/NdrEndpoints.cs`, change:

```csharp
        returnShipments.MapGet("/", async (NdrService service, HttpContext httpContext) =>
            Results.Ok(await service.ListReturnShipmentsAsync(httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("rto.read");
```

to:

```csharp
        returnShipments.MapGet("/", async (Guid? originalShipmentId, NdrService service, HttpContext httpContext) =>
            Results.Ok(await service.ListReturnShipmentsAsync(originalShipmentId, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("rto.read");
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj --filter "ListReturnShipmentsAsync"`
Expected: PASS (all tests matching this filter)

- [ ] **Step 6: Strengthen the existing RTO integration test to exercise the new filter**

In `tests/Api.Tests/NdrEndpointsTests.cs`'s `FullRtoPath_FailedAttemptToRtoDelivered_MovesThroughEveryStatusViaManifests` test, change:

```csharp
        var returnShipments = await (await client.GetAsync("/api/v1/return-shipments")).Content.ReadFromJsonAsync<List<ReturnShipmentResponse>>();
        var returnShipment = returnShipments!.Single(r => r.OriginalShipmentId == shipment.Id);
```

to:

```csharp
        var returnShipments = await (await client.GetAsync($"/api/v1/return-shipments?originalShipmentId={shipment.Id}")).Content.ReadFromJsonAsync<List<ReturnShipmentResponse>>();
        var returnShipment = Assert.Single(returnShipments!);
        Assert.Equal(shipment.Awb, returnShipment.OriginalAwb);
```

- [ ] **Step 7: Run the full Ndr and Api test suites, then commit**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj && dotnet test tests/Api.Tests/Api.Tests.csproj --filter NdrEndpointsTests`
Expected: PASS (all tests)

```bash
git add src/Modules/Ndr/Application/NdrService.ReturnShipments.cs src/Modules/Ndr/Presentation/NdrEndpoints.cs tests/Ndr.Tests/NdrServiceListTests.cs tests/Api.Tests/NdrEndpointsTests.cs
git commit -m "feat(ndr): add originalShipmentId filter to GET /api/v1/return-shipments"
```

---

## Task 6: Ndr feature foundation (types, hooks, stores, BFF routes)

**Files:**
- Create: `src/web/src/features/ndr/types.ts`
- Create: `src/web/src/features/ndr/hooks/useNdrs.ts`
- Create: `src/web/src/features/ndr/hooks/useNdr.ts`
- Create: `src/web/src/features/ndr/hooks/useUpdateNdr.ts`
- Create: `src/web/src/features/ndr/hooks/useResolveNdr.ts`
- Create: `src/web/src/features/ndr/hooks/useReturnShipments.ts`
- Create: `src/web/src/features/ndr/hooks/useReturnShipment.ts`
- Create: `src/web/src/features/ndr/hooks/useConfirmReturnShipment.ts`
- Create: `src/web/src/features/ndr/store/ndrPanelStore.ts`
- Create: `src/web/src/features/ndr/store/ndrPanelStore.test.ts`
- Create: `src/web/src/features/ndr/store/returnShipmentPanelStore.ts`
- Create: `src/web/src/features/ndr/store/returnShipmentPanelStore.test.ts`
- Create: `src/web/src/app/api/ndrs/route.ts`
- Create: `src/web/src/app/api/ndrs/route.test.ts`
- Create: `src/web/src/app/api/ndrs/[id]/route.ts`
- Create: `src/web/src/app/api/ndrs/[id]/update/route.ts`
- Create: `src/web/src/app/api/ndrs/[id]/resolve/route.ts`
- Create: `src/web/src/app/api/return-shipments/route.ts`
- Create: `src/web/src/app/api/return-shipments/route.test.ts`
- Create: `src/web/src/app/api/return-shipments/[id]/route.ts`
- Create: `src/web/src/app/api/return-shipments/[id]/confirm/route.ts`

**Interfaces:**
- Produces: `Ndr`, `NdrStatus`, `NdrReason`, `NdrResolution`, `UpdateNdrInput`, `ResolveNdrInput`, `ReturnShipment` types; `useNdrs(params?)`, `useNdr(id)`, `useUpdateNdr(id)`, `useResolveNdr(id)`, `useReturnShipments(originalShipmentId?)`, `useReturnShipment(id)`, `useConfirmReturnShipment(id)` hooks; `useNdrPanelStore`, `useReturnShipmentPanelStore` Zustand stores (both `{ mode: "closed" } | { mode: "detail"; id: string }`, no create mode) — all consumed by Tasks 7–12.

- [ ] **Step 1: Write the failing BFF route tests**

Create `src/web/src/app/api/ndrs/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";
import { NextRequest } from "next/server";

describe("GET /api/ndrs", () => {
  it("proxies the query string to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }),
    } as Response);

    const request = new NextRequest("http://localhost/api/ndrs?status=Open");
    const response = await GET(request);

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/ndrs?status=Open");
    expect(response.status).toBe(200);
  });
});
```

Create `src/web/src/app/api/return-shipments/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";
import { NextRequest } from "next/server";

describe("GET /api/return-shipments", () => {
  it("proxies the query string to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    } as Response);

    const request = new NextRequest("http://localhost/api/return-shipments?originalShipmentId=s1");
    const response = await GET(request);

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/return-shipments?originalShipmentId=s1");
    expect(response.status).toBe(200);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd src/web && npm test -- api/ndrs/route.test.ts api/return-shipments/route.test.ts`
Expected: FAIL — neither route file exists yet.

- [ ] **Step 3: Create the BFF routes**

Create `src/web/src/app/api/ndrs/route.ts`:

```ts
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/ndrs${query}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/app/api/ndrs/[id]/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/ndrs/${params.id}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/app/api/ndrs/[id]/update/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/ndrs/${params.id}/update`, {
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

Create `src/web/src/app/api/ndrs/[id]/resolve/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/ndrs/${params.id}/resolve`, {
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

Create `src/web/src/app/api/return-shipments/route.ts`:

```ts
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/return-shipments${query}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/app/api/return-shipments/[id]/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/return-shipments/${params.id}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/app/api/return-shipments/[id]/confirm/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/return-shipments/${params.id}/confirm`, { method: "POST" });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd src/web && npm test -- api/ndrs/route.test.ts api/return-shipments/route.test.ts`
Expected: PASS (2/2)

- [ ] **Step 5: Add the feature types**

Create `src/web/src/features/ndr/types.ts`:

```ts
export type NdrStatus = "Open" | "Contacting" | "CustomerConfirmed" | "ReattemptScheduled" | "RtoRequested" | "Resolved";
export type NdrReason =
  | "CustomerUnavailable" | "PhoneUnreachable" | "WrongAddress" | "IncompleteAddress"
  | "CustomerRefused" | "CustomerRequestedReschedule" | "CashUnavailable" | "DamagedParcel" | "Other";
export type NdrResolution = "Reattempt" | "Rto" | "AutoRto" | "Other";

export interface Ndr {
  id: string;
  shipmentId: string;
  awb: string;
  reason: NdrReason;
  assignedStaffUserId: string | null;
  status: NdrStatus;
  customerResponse: string | null;
  followUpAt: string | null;
  nextAction: string | null;
  resolution: NdrResolution | null;
  resolvedAt: string | null;
  createdAt: string;
}

export interface UpdateNdrInput {
  status: NdrStatus;
  customerResponse?: string;
  nextAction?: string;
  followUpAt?: string;
}

export interface ResolveNdrInput {
  resolution: NdrResolution;
  rtoReason?: string;
}

export interface ReturnShipment {
  id: string;
  originalShipmentId: string;
  originalAwb: string;
  returnAwb: string;
  rtoReason: string;
  originBranchId: string;
  destinationBranchId: string;
  currentStatus: string;
  currentBranchId: string | null;
  merchantConfirmedAt: string | null;
}
```

- [ ] **Step 6: Add the hooks**

Create `src/web/src/features/ndr/hooks/useNdrs.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Ndr, NdrStatus } from "@/features/ndr/types";

export function useNdrs(params: { status?: NdrStatus; shipmentId?: string; page?: number; pageSize?: number } = {}) {
  const { status, shipmentId, page, pageSize } = params;
  const query = new URLSearchParams();
  if (status) query.set("status", status);
  if (shipmentId) query.set("shipmentId", shipmentId);
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["ndrs", status ?? null, shipmentId ?? null, page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<Ndr>>(`/api/ndrs${search ? `?${search}` : ""}`),
  });
}
```

Create `src/web/src/features/ndr/hooks/useNdr.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Ndr } from "@/features/ndr/types";

export function useNdr(id: string) {
  return useQuery({
    queryKey: ["ndrs", id],
    queryFn: () => clientFetch<Ndr>(`/api/ndrs/${id}`),
    enabled: id !== "",
  });
}
```

Create `src/web/src/features/ndr/hooks/useUpdateNdr.ts`:

```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Ndr, UpdateNdrInput } from "@/features/ndr/types";

export function useUpdateNdr(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: UpdateNdrInput) =>
      clientFetch<Ndr>(`/api/ndrs/${id}/update`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["ndrs"] });
    },
  });
}
```

Create `src/web/src/features/ndr/hooks/useResolveNdr.ts`:

```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Ndr, ResolveNdrInput } from "@/features/ndr/types";

export function useResolveNdr(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: ResolveNdrInput) =>
      clientFetch<Ndr>(`/api/ndrs/${id}/resolve`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["ndrs"] });
      queryClient.invalidateQueries({ queryKey: ["return-shipments"] });
    },
  });
}
```

Create `src/web/src/features/ndr/hooks/useReturnShipments.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ReturnShipment } from "@/features/ndr/types";

export function useReturnShipments(originalShipmentId?: string) {
  const query = new URLSearchParams();
  if (originalShipmentId) query.set("originalShipmentId", originalShipmentId);
  const search = query.toString();

  return useQuery({
    queryKey: ["return-shipments", originalShipmentId ?? null],
    queryFn: () => clientFetch<ReturnShipment[]>(`/api/return-shipments${search ? `?${search}` : ""}`),
  });
}
```

Create `src/web/src/features/ndr/hooks/useReturnShipment.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ReturnShipment } from "@/features/ndr/types";

export function useReturnShipment(id: string) {
  return useQuery({
    queryKey: ["return-shipments", id],
    queryFn: () => clientFetch<ReturnShipment>(`/api/return-shipments/${id}`),
    enabled: id !== "",
  });
}
```

Create `src/web/src/features/ndr/hooks/useConfirmReturnShipment.ts`:

```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ReturnShipment } from "@/features/ndr/types";

export function useConfirmReturnShipment(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => clientFetch<ReturnShipment>(`/api/return-shipments/${id}/confirm`, { method: "POST" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["return-shipments"] });
    },
  });
}
```

- [ ] **Step 7: Write the failing store tests**

Create `src/web/src/features/ndr/store/ndrPanelStore.test.ts`:

```ts
import { describe, expect, it, beforeEach } from "vitest";
import { useNdrPanelStore } from "./ndrPanelStore";

describe("useNdrPanelStore", () => {
  beforeEach(() => {
    useNdrPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useNdrPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openDetail switches to the detail panel with the given id", () => {
    useNdrPanelStore.getState().openDetail("n1");
    expect(useNdrPanelStore.getState().panel).toEqual({ mode: "detail", id: "n1" });
  });

  it("close returns to closed", () => {
    useNdrPanelStore.getState().openDetail("n1");
    useNdrPanelStore.getState().close();
    expect(useNdrPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

Create `src/web/src/features/ndr/store/returnShipmentPanelStore.test.ts` (identical shape):

```ts
import { describe, expect, it, beforeEach } from "vitest";
import { useReturnShipmentPanelStore } from "./returnShipmentPanelStore";

describe("useReturnShipmentPanelStore", () => {
  beforeEach(() => {
    useReturnShipmentPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useReturnShipmentPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openDetail switches to the detail panel with the given id", () => {
    useReturnShipmentPanelStore.getState().openDetail("rs1");
    expect(useReturnShipmentPanelStore.getState().panel).toEqual({ mode: "detail", id: "rs1" });
  });

  it("close returns to closed", () => {
    useReturnShipmentPanelStore.getState().openDetail("rs1");
    useReturnShipmentPanelStore.getState().close();
    expect(useReturnShipmentPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

- [ ] **Step 8: Run tests to verify they fail**

Run: `cd src/web && npm test -- features/ndr/store`
Expected: FAIL — neither store file exists yet.

- [ ] **Step 9: Create the stores**

Create `src/web/src/features/ndr/store/ndrPanelStore.ts`:

```ts
import { create } from "zustand";

type NdrPanelState = { mode: "closed" } | { mode: "detail"; id: string };

interface NdrPanelStore {
  panel: NdrPanelState;
  openDetail: (id: string) => void;
  close: () => void;
}

// Global so any component in the (staff) tree can open the NDR sheet without routing to a
// separate page (docs/ARCHITECTURE.md §9a). No create mode — NDRs are only ever system-created by
// NdrCreationService.CreateFromFailedAttemptAsync, never through this UI.
export const useNdrPanelStore = create<NdrPanelStore>((set) => ({
  panel: { mode: "closed" },
  openDetail: (id) => set({ panel: { mode: "detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

Create `src/web/src/features/ndr/store/returnShipmentPanelStore.ts`:

```ts
import { create } from "zustand";

type ReturnShipmentPanelState = { mode: "closed" } | { mode: "detail"; id: string };

interface ReturnShipmentPanelStore {
  panel: ReturnShipmentPanelState;
  openDetail: (id: string) => void;
  close: () => void;
}

// Global so any component in the (staff) tree can open the return-shipment sheet without routing
// to a separate page (docs/ARCHITECTURE.md §9a). No create mode — return shipments are only ever
// system-created by NdrService.ResolveAsync when an NDR resolves as Rto/AutoRto, never through
// this UI.
export const useReturnShipmentPanelStore = create<ReturnShipmentPanelStore>((set) => ({
  panel: { mode: "closed" },
  openDetail: (id) => set({ panel: { mode: "detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

- [ ] **Step 10: Run tests, typecheck, and commit**

Run: `cd src/web && npm test -- features/ndr api/ndrs api/return-shipments && npm run build`
Expected: PASS, build succeeds.

```bash
git add src/web/src/features/ndr src/web/src/app/api/ndrs src/web/src/app/api/return-shipments
git commit -m "feat(web): add Ndr feature foundation (types, hooks, stores, BFF routes)"
```

---

## Task 7: NDR list page, `StatusBadge` `Ndr.*` tokens, sidebar nav entry

**Files:**
- Modify: `src/web/src/components/status-badge/statusConfig.ts`
- Modify: `src/web/src/components/nav-main.tsx`
- Modify: `src/web/src/components/nav-main.test.tsx`
- Create: `src/web/src/app/(staff)/ndrs/page.tsx`

**Interfaces:**
- Consumes: `useNdrs` (Task 6), `useNdrPanelStore.openDetail` (Task 6).
- Produces: `/ndrs` route; `resolveStatusConfig("Open", "Ndr")` etc. resolve to real tokens (used by `NdrDetailSheet` from Task 8 onward).

- [ ] **Step 1: Validate the NdrStatus token mapping**

Before editing `statusConfig.ts`, invoke the `dataviz` skill to validate this candidate 6-token set as a group (not the whole app palette, which prior phases already validated) — these specific tokens haven't been checked side-by-side in this exact new grouping:

| NdrStatus | Candidate token |
|---|---|
| `Open` | `status-ndr` |
| `Contacting` | `status-out-for-delivery` |
| `CustomerConfirmed` | `status-received` |
| `ReattemptScheduled` | `status-sealed` |
| `RtoRequested` | `status-rto` |
| `Resolved` | `status-delivered` |

If the validator flags any adjacent pair as failing the CVD floor (particularly `status-out-for-delivery` vs. `status-in-transit`, both close-hue ambers, though `status-in-transit` isn't in this set), swap the flagged token for a different existing token — never mint a new CSS color. Proceed to Step 2 with whichever mapping passes.

- [ ] **Step 2: Add the `Ndr.*` entries to `statusConfig.ts`**

In `src/web/src/components/status-badge/statusConfig.ts`, insert after the existing `"Manifest.Closed"` entry (before the closing `};`):

```ts
  // NDR statuses — namespaced ("Ndr.X") for the same reason Manifest.X is: a flat key risks
  // collision with ShipmentStatus/ManifestStatus members. All 6 reuse existing tokens (validated
  // as a group via the dataviz skill before this file was edited) rather than minting new colors.
  "Ndr.Open": { label: "Open", token: "status-ndr" },
  "Ndr.Contacting": { label: "Contacting", token: "status-out-for-delivery" },
  "Ndr.CustomerConfirmed": { label: "Customer Confirmed", token: "status-received" },
  "Ndr.ReattemptScheduled": { label: "Reattempt Scheduled", token: "status-sealed" },
  "Ndr.RtoRequested": { label: "RTO Requested", token: "status-rto" },
  "Ndr.Resolved": { label: "Resolved", token: "status-delivered" },
```

(replace the token values with whatever Step 1's validation settled on, if different from the candidates above).

- [ ] **Step 3: Write the failing nav test**

In `src/web/src/components/nav-main.test.tsx`, add after the `"shows the Riders entry..."` test:

```ts
  it("shows the NDRs entry when the session has ndr.read", async () => {
    renderNavMain(["report.read", "ndr.read"]);

    await waitFor(() => expect(screen.getByText("NDRs")).toBeInTheDocument());
  });
```

- [ ] **Step 4: Run test to verify it fails**

Run: `cd src/web && npm test -- nav-main.test.tsx`
Expected: FAIL — no "NDRs" text in the rendered nav yet.

- [ ] **Step 5: Add the nav entry**

In `src/web/src/components/nav-main.tsx`, add `AlertTriangle` to the `lucide-react` import:

```ts
import { LayoutDashboard, Package, Building2, Wallet, Truck, Bike, ClipboardCheck, AlertTriangle, type LucideIcon } from "lucide-react";
```

and add to `NAV_ITEMS` (after the `/manifests` entry, before `/riders`, keeping the operational-flow ordering the list already follows):

```ts
  { href: "/ndrs", label: "NDRs", permission: "ndr.read", icon: AlertTriangle },
```

- [ ] **Step 6: Run test to verify it passes**

Run: `cd src/web && npm test -- nav-main.test.tsx`
Expected: PASS (all tests in this file)

- [ ] **Step 7: Build the list page**

Create `src/web/src/app/(staff)/ndrs/page.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useNdrs } from "@/features/ndr/hooks/useNdrs";
import { useNdrPanelStore } from "@/features/ndr/store/ndrPanelStore";
import type { Ndr, NdrStatus } from "@/features/ndr/types";

const STATUS_OPTIONS: NdrStatus[] = ["Open", "Contacting", "CustomerConfirmed", "ReattemptScheduled", "RtoRequested", "Resolved"];

export default function NdrsPage() {
  const [status, setStatus] = useState<NdrStatus | "all">("all");
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const { data, isLoading, isError, refetch } = useNdrs({
    status: status === "all" ? undefined : status,
    page,
    pageSize,
  });
  const openDetail = useNdrPanelStore((s) => s.openDetail);

  const columns: ColumnDef<Ndr>[] = [
    {
      key: "awb",
      header: "AWB",
      render: (n) => (
        <button type="button" onClick={() => openDetail(n.id)} className="font-medium text-foreground hover:underline">
          {n.awb}
        </button>
      ),
    },
    { key: "reason", header: "Reason", render: (n) => n.reason },
    { key: "status", header: "Status", render: (n) => <StatusBadge status={n.status} namespace="Ndr" /> },
    { key: "createdAt", header: "Created At", render: (n) => new Date(n.createdAt).toLocaleDateString() },
    { key: "assignedStaff", header: "Assigned Staff", render: (n) => n.assignedStaffUserId ?? "—" },
  ];

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load NDRs"
        description="Something went wrong fetching the NDR queue."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">NDRs</h1>
        <Select
          value={status}
          onValueChange={(v) => {
            setStatus(v as NdrStatus | "all");
            setPage(1);
          }}
        >
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
      </div>
      <DataTable
        columns={columns}
        data={data}
        isLoading={false}
        emptyTitle="No NDRs"
        emptyDescription="Failed deliveries that need follow-up will show up here."
        onPageChange={setPage}
        rowKey={(n) => n.id}
      />
    </div>
  );
}
```

- [ ] **Step 8: Run the full frontend test suite and build, then commit**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds.

```bash
git add src/web/src/components/status-badge/statusConfig.ts src/web/src/components/nav-main.tsx src/web/src/components/nav-main.test.tsx "src/web/src/app/(staff)/ndrs"
git commit -m "feat(web): add NDR list page, Ndr.* status tokens, sidebar nav entry"
```

---

## Task 8: `NdrDetailSheet` shell + `NdrUpdateSection`

**Files:**
- Create: `src/web/src/features/ndr/components/NdrUpdateSection.tsx`
- Create: `src/web/src/features/ndr/components/NdrUpdateSection.test.tsx`
- Create: `src/web/src/features/ndr/components/NdrDetailSheet.tsx`
- Modify: `src/web/src/app/(staff)/layout.tsx`

**Interfaces:**
- Consumes: `useUpdateNdr` (Task 6), `Ndr`/`NdrStatus` types (Task 6).
- Produces: `<NdrUpdateSection ndr={ndr} />` — a status-driven forward-move form covering `Open`/`Contacting`/`CustomerConfirmed`, wired into `NdrDetailSheet` in this task and left for Task 9 to extend with the remaining statuses.

- [ ] **Step 1: Write the failing tests**

Create `src/web/src/features/ndr/components/NdrUpdateSection.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NdrUpdateSection } from "./NdrUpdateSection";
import * as clientFetchModule from "@/lib/api/clientFetch";
import type { Ndr } from "@/features/ndr/types";

const baseNdr: Ndr = {
  id: "n1", shipmentId: "s1", awb: "AWB001", reason: "CustomerUnavailable",
  assignedStaffUserId: null, status: "Open", customerResponse: null, followUpAt: null,
  nextAction: null, resolution: null, resolvedAt: null, createdAt: "2026-09-01T00:00:00Z",
};

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("NdrUpdateSection", () => {
  it("shows a single forward-move button for Open and submits it", async () => {
    const spy = vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ ...baseNdr, status: "Contacting" });
    const user = userEvent.setup();
    renderWithProviders(<NdrUpdateSection ndr={baseNdr} />);

    expect(screen.queryByRole("button", { name: /move to customer confirmed/i })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /move to contacting/i }));

    await waitFor(() =>
      expect(spy).toHaveBeenCalledWith("/api/ndrs/n1/update", expect.objectContaining({ method: "POST" }))
    );
  });

  it("shows two branch buttons for CustomerConfirmed", () => {
    renderWithProviders(<NdrUpdateSection ndr={{ ...baseNdr, status: "CustomerConfirmed" }} />);

    expect(screen.getByRole("button", { name: /schedule reattempt/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /request rto/i })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npm test -- NdrUpdateSection.test.tsx`
Expected: FAIL — the component doesn't exist yet.

- [ ] **Step 3: Build `NdrUpdateSection`**

Create `src/web/src/features/ndr/components/NdrUpdateSection.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Spinner } from "@/components/ui/spinner";
import { useUpdateNdr } from "@/features/ndr/hooks/useUpdateNdr";
import type { Ndr, NdrStatus } from "@/features/ndr/types";

// The one real branch point in NdrService.IsLegalForwardMove: CustomerConfirmed can move to either
// ReattemptScheduled or RtoRequested. Every other status has exactly one legal forward move.
const NEXT_STATUSES: Record<string, { status: NdrStatus; label: string }[]> = {
  Open: [{ status: "Contacting", label: "Move to Contacting" }],
  Contacting: [{ status: "CustomerConfirmed", label: "Move to Customer Confirmed" }],
  CustomerConfirmed: [
    { status: "ReattemptScheduled", label: "Schedule Reattempt" },
    { status: "RtoRequested", label: "Request RTO" },
  ],
};

export function NdrUpdateSection({ ndr }: { ndr: Ndr }) {
  const [customerResponse, setCustomerResponse] = useState("");
  const [nextAction, setNextAction] = useState("");
  const [followUpAt, setFollowUpAt] = useState("");
  const updateNdr = useUpdateNdr(ndr.id);
  const [serverError, setServerError] = useState<string | null>(null);

  const moves = NEXT_STATUSES[ndr.status] ?? [];

  async function handleMove(status: NdrStatus) {
    setServerError(null);
    try {
      await updateNdr.mutateAsync({
        status,
        customerResponse: customerResponse || undefined,
        nextAction: nextAction || undefined,
        followUpAt: followUpAt ? new Date(followUpAt).toISOString() : undefined,
      });
    } catch {
      setServerError("Couldn't update this NDR. Check the fields and try again.");
    }
  }

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="ndr-customer-response">Customer Response</Label>
        <Textarea id="ndr-customer-response" value={customerResponse} onChange={(e) => setCustomerResponse(e.target.value)} />
      </div>
      <div className="space-y-2">
        <Label htmlFor="ndr-next-action">Next Action</Label>
        <Input id="ndr-next-action" value={nextAction} onChange={(e) => setNextAction(e.target.value)} />
      </div>
      <div className="space-y-2">
        <Label htmlFor="ndr-follow-up">Follow Up At</Label>
        <Input id="ndr-follow-up" type="datetime-local" value={followUpAt} onChange={(e) => setFollowUpAt(e.target.value)} />
      </div>
      {serverError && <p className="text-sm text-destructive">{serverError}</p>}
      <div className="flex gap-2">
        {moves.map((move) => (
          <Button key={move.status} type="button" disabled={updateNdr.isPending} onClick={() => handleMove(move.status)}>
            {updateNdr.isPending ? <Spinner className="h-4 w-4" /> : move.label}
          </Button>
        ))}
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/web && npm test -- NdrUpdateSection.test.tsx`
Expected: PASS (2/2)

- [ ] **Step 5: Build the `NdrDetailSheet` shell**

Create `src/web/src/features/ndr/components/NdrDetailSheet.tsx`:

```tsx
"use client";

import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useNdr } from "@/features/ndr/hooks/useNdr";
import { useNdrPanelStore } from "@/features/ndr/store/ndrPanelStore";
import { NdrUpdateSection } from "@/features/ndr/components/NdrUpdateSection";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function NdrDetailSheet() {
  const panel = useNdrPanelStore((s) => s.panel);
  const close = useNdrPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";

  const { data: ndr, isLoading, isError, refetch } = useNdr(id);

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{ndr?.awb ?? "NDR"}</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && (
            <ErrorState
              title="Couldn't load this NDR"
              description="It may not exist, or you may not have access to it."
              onRetry={() => refetch()}
            />
          )}
          {ndr && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Status</span>
                  <StatusBadge status={ndr.status} namespace="Ndr" />
                </div>
                <Field label="Reason" value={ndr.reason} />
                <Field label="Assigned Staff" value={ndr.assignedStaffUserId ?? "Unassigned"} />
                <Field label="Created At" value={new Date(ndr.createdAt).toLocaleString()} />
              </div>

              {(ndr.status === "Open" || ndr.status === "Contacting" || ndr.status === "CustomerConfirmed") && (
                <NdrUpdateSection ndr={ndr} />
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 6: Mount the sheet in the staff layout**

In `src/web/src/app/(staff)/layout.tsx`, add the import and mount alongside the other detail sheets:

```tsx
import { NdrDetailSheet } from "@/features/ndr/components/NdrDetailSheet";
```

```tsx
      <AssignTaskSheet />
      <NdrDetailSheet />
```

- [ ] **Step 7: Run the full frontend test suite and build, then commit**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds.

```bash
git add src/web/src/features/ndr/components/NdrUpdateSection.tsx src/web/src/features/ndr/components/NdrUpdateSection.test.tsx src/web/src/features/ndr/components/NdrDetailSheet.tsx "src/web/src/app/(staff)/layout.tsx"
git commit -m "feat(web): add NdrDetailSheet shell and NdrUpdateSection"
```

---

## Task 9: `NdrReattemptSection`, `NdrRtoSection`, `NdrResolveOtherAction`, `NdrResolvedSummary`

**Files:**
- Create: `src/web/src/features/ndr/components/NdrReattemptSection.tsx`
- Create: `src/web/src/features/ndr/components/NdrRtoSection.tsx`
- Create: `src/web/src/features/ndr/components/NdrRtoSection.test.tsx`
- Create: `src/web/src/features/ndr/components/NdrResolveOtherAction.tsx`
- Create: `src/web/src/features/ndr/components/NdrResolvedSummary.tsx`
- Modify: `src/web/src/features/ndr/components/NdrDetailSheet.tsx`

**Interfaces:**
- Consumes: `useResolveNdr` (Task 6), `useReturnShipments` (Task 6), `useReturnShipmentPanelStore.openDetail` (Task 6).
- Produces: completes `NdrDetailSheet`'s status-driven body for every `NdrStatus` value.

Note on scope vs. the design spec: the spec described `NdrResolveOtherAction` with a short reason-note field. Reviewing `NdrService.ResolveAsync`'s `Other` branch shows it never reads or persists `ResolveNdrRequest.RtoReason` for that resolution (no `NDR` column exists to hold an "Other" note — `NdrDtos.cs` confirmed). Building an input whose value the backend silently discards would be misleading, not thorough, so this task ships `NdrResolveOtherAction` as a single confirm action with no note field — a deliberate simplification, not an oversight.

- [ ] **Step 1: Write the failing `NdrRtoSection` test**

Create `src/web/src/features/ndr/components/NdrRtoSection.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NdrRtoSection } from "./NdrRtoSection";
import * as clientFetchModule from "@/lib/api/clientFetch";
import type { Ndr } from "@/features/ndr/types";

const ndr: Ndr = {
  id: "n1", shipmentId: "s1", awb: "AWB001", reason: "CustomerRefused",
  assignedStaffUserId: null, status: "RtoRequested", customerResponse: null, followUpAt: null,
  nextAction: null, resolution: null, resolvedAt: null, createdAt: "2026-09-01T00:00:00Z",
};

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("NdrRtoSection", () => {
  it("disables the resolve button until a reason is entered", async () => {
    renderWithProviders(<NdrRtoSection ndr={ndr} />);
    expect(screen.getByRole("button", { name: /resolve — initiate rto/i })).toBeDisabled();

    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/rto reason/i), "Customer refused after three contacts");

    expect(screen.getByRole("button", { name: /resolve — initiate rto/i })).toBeEnabled();
  });

  it("submits the resolve request with the entered reason", async () => {
    const spy = vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ ...ndr, status: "Resolved" });
    const user = userEvent.setup();
    renderWithProviders(<NdrRtoSection ndr={ndr} />);

    await user.type(screen.getByLabelText(/rto reason/i), "Customer refused");
    await user.click(screen.getByRole("button", { name: /resolve — initiate rto/i }));

    await waitFor(() =>
      expect(spy).toHaveBeenCalledWith(
        "/api/ndrs/n1/resolve",
        expect.objectContaining({ method: "POST", body: JSON.stringify({ resolution: "Rto", rtoReason: "Customer refused" }) })
      )
    );
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npm test -- NdrRtoSection.test.tsx`
Expected: FAIL — the component doesn't exist yet.

- [ ] **Step 3: Build `NdrReattemptSection`**

Create `src/web/src/features/ndr/components/NdrReattemptSection.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useResolveNdr } from "@/features/ndr/hooks/useResolveNdr";
import type { Ndr } from "@/features/ndr/types";

export function NdrReattemptSection({ ndr }: { ndr: Ndr }) {
  const resolveNdr = useResolveNdr(ndr.id);
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleResolve() {
    setServerError(null);
    try {
      await resolveNdr.mutateAsync({ resolution: "Reattempt" });
    } catch {
      setServerError("Couldn't resolve this NDR. Try again.");
    }
  }

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-foreground">
        Resolving sends this shipment back to Out for Delivery for another attempt.
      </p>
      {serverError && <p className="text-sm text-destructive">{serverError}</p>}
      <Button type="button" disabled={resolveNdr.isPending} onClick={handleResolve}>
        {resolveNdr.isPending ? <Spinner className="h-4 w-4" /> : "Resolve — Reattempt Delivery"}
      </Button>
    </div>
  );
}
```

- [ ] **Step 4: Build `NdrRtoSection`**

Create `src/web/src/features/ndr/components/NdrRtoSection.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Spinner } from "@/components/ui/spinner";
import { useResolveNdr } from "@/features/ndr/hooks/useResolveNdr";
import type { Ndr } from "@/features/ndr/types";

export function NdrRtoSection({ ndr }: { ndr: Ndr }) {
  const [rtoReason, setRtoReason] = useState("");
  const resolveNdr = useResolveNdr(ndr.id);
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleResolve() {
    if (!rtoReason.trim()) return;
    setServerError(null);
    try {
      await resolveNdr.mutateAsync({ resolution: "Rto", rtoReason: rtoReason.trim() });
    } catch {
      setServerError("Couldn't resolve this NDR. Try again.");
    }
  }

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="ndr-rto-reason">RTO Reason</Label>
        <Textarea id="ndr-rto-reason" value={rtoReason} onChange={(e) => setRtoReason(e.target.value)} />
      </div>
      {serverError && <p className="text-sm text-destructive">{serverError}</p>}
      <Button type="button" disabled={!rtoReason.trim() || resolveNdr.isPending} onClick={handleResolve}>
        {resolveNdr.isPending ? <Spinner className="h-4 w-4" /> : "Resolve — Initiate RTO"}
      </Button>
    </div>
  );
}
```

- [ ] **Step 5: Build `NdrResolveOtherAction`**

Create `src/web/src/features/ndr/components/NdrResolveOtherAction.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useResolveNdr } from "@/features/ndr/hooks/useResolveNdr";
import type { Ndr } from "@/features/ndr/types";

// No note field: ResolveNdrRequest.RtoReason is the only free-text field the backend accepts on
// resolve, and NdrService.ResolveAsync's Other branch never reads or persists it (no NDR column
// exists to hold an "Other" note) — building an input for a value the backend silently discards
// would be misleading, not thorough.
export function NdrResolveOtherAction({ ndr }: { ndr: Ndr }) {
  const resolveNdr = useResolveNdr(ndr.id);
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleResolve() {
    setServerError(null);
    try {
      await resolveNdr.mutateAsync({ resolution: "Other" });
    } catch {
      setServerError("Couldn't resolve this NDR. Try again.");
    }
  }

  return (
    <div className="space-y-2">
      {serverError && <p className="text-sm text-destructive">{serverError}</p>}
      <Button type="button" variant="ghost" size="sm" disabled={resolveNdr.isPending} onClick={handleResolve}>
        {resolveNdr.isPending ? <Spinner className="h-4 w-4" /> : "Resolve — Other"}
      </Button>
    </div>
  );
}
```

- [ ] **Step 6: Build `NdrResolvedSummary`**

Create `src/web/src/features/ndr/components/NdrResolvedSummary.tsx`:

```tsx
"use client";

import { Button } from "@/components/ui/button";
import { useReturnShipments } from "@/features/ndr/hooks/useReturnShipments";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";
import type { Ndr } from "@/features/ndr/types";

export function NdrResolvedSummary({ ndr }: { ndr: Ndr }) {
  const isRto = ndr.resolution === "Rto" || ndr.resolution === "AutoRto";
  const { data: returnShipments } = useReturnShipments(isRto ? ndr.shipmentId : undefined);
  const openDetail = useReturnShipmentPanelStore((s) => s.openDetail);
  const returnShipment = returnShipments?.[0];

  return (
    <div className="space-y-2">
      <p className="text-sm text-muted-foreground">
        Resolved as <span className="font-medium text-foreground">{ndr.resolution}</span>.
      </p>
      {isRto && returnShipment && (
        <Button type="button" variant="outline" size="sm" onClick={() => openDetail(returnShipment.id)}>
          View Return Shipment
        </Button>
      )}
    </div>
  );
}
```

- [ ] **Step 7: Wire everything into `NdrDetailSheet`**

In `src/web/src/features/ndr/components/NdrDetailSheet.tsx`, add the imports:

```tsx
import { NdrReattemptSection } from "@/features/ndr/components/NdrReattemptSection";
import { NdrRtoSection } from "@/features/ndr/components/NdrRtoSection";
import { NdrResolveOtherAction } from "@/features/ndr/components/NdrResolveOtherAction";
import { NdrResolvedSummary } from "@/features/ndr/components/NdrResolvedSummary";
```

and change:

```tsx
              {(ndr.status === "Open" || ndr.status === "Contacting" || ndr.status === "CustomerConfirmed") && (
                <NdrUpdateSection ndr={ndr} />
              )}
```

to:

```tsx
              {(ndr.status === "Open" || ndr.status === "Contacting" || ndr.status === "CustomerConfirmed") && (
                <NdrUpdateSection ndr={ndr} />
              )}
              {ndr.status === "ReattemptScheduled" && <NdrReattemptSection ndr={ndr} />}
              {ndr.status === "RtoRequested" && <NdrRtoSection ndr={ndr} />}
              {ndr.status === "Resolved" && <NdrResolvedSummary ndr={ndr} />}
              {ndr.status !== "Resolved" && <NdrResolveOtherAction ndr={ndr} />}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `cd src/web && npm test -- NdrRtoSection.test.tsx`
Expected: PASS (2/2)

- [ ] **Step 9: Run the full frontend test suite and build, then commit**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds.

```bash
git add src/web/src/features/ndr/components
git commit -m "feat(web): complete NdrDetailSheet with Reattempt/RTO/Other resolution and Resolved summary"
```

---

## Task 10: Return Shipments list page, sidebar nav entry

**Files:**
- Modify: `src/web/src/components/nav-main.tsx`
- Modify: `src/web/src/components/nav-main.test.tsx`
- Create: `src/web/src/app/(staff)/return-shipments/page.tsx`

**Interfaces:**
- Consumes: `useReturnShipments` (Task 6), `useReturnShipmentPanelStore.openDetail` (Task 6), `useBranches` (existing, `@/features/branches/hooks/useBranches`).
- Produces: `/return-shipments` route.

- [ ] **Step 1: Write the failing nav test**

In `src/web/src/components/nav-main.test.tsx`, add after the `"shows the NDRs entry..."` test:

```ts
  it("shows the Return Shipments entry when the session has rto.read", async () => {
    renderNavMain(["report.read", "rto.read"]);

    await waitFor(() => expect(screen.getByText("Return Shipments")).toBeInTheDocument());
  });
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npm test -- nav-main.test.tsx`
Expected: FAIL — no "Return Shipments" text in the rendered nav yet.

- [ ] **Step 3: Add the nav entry**

In `src/web/src/components/nav-main.tsx`, add `Undo2` to the `lucide-react` import:

```ts
import { LayoutDashboard, Package, Building2, Wallet, Truck, Bike, ClipboardCheck, AlertTriangle, Undo2, type LucideIcon } from "lucide-react";
```

and add to `NAV_ITEMS` (after the `/ndrs` entry):

```ts
  { href: "/return-shipments", label: "Return Shipments", permission: "rto.read", icon: Undo2 },
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/web && npm test -- nav-main.test.tsx`
Expected: PASS (all tests in this file)

- [ ] **Step 5: Build the list page**

Create `src/web/src/app/(staff)/return-shipments/page.tsx`:

```tsx
"use client";

import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useReturnShipments } from "@/features/ndr/hooks/useReturnShipments";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";
import type { ReturnShipment } from "@/features/ndr/types";

export default function ReturnShipmentsPage() {
  const { data, isLoading, isError, refetch } = useReturnShipments();
  const { data: branches } = useBranches();
  const openDetail = useReturnShipmentPanelStore((s) => s.openDetail);

  const branchName = (id: string) => branches?.find((b) => b.id === id)?.name ?? "—";

  const columns: ColumnDef<ReturnShipment>[] = [
    {
      key: "returnAwb",
      header: "Return AWB",
      render: (r) => (
        <button type="button" onClick={() => openDetail(r.id)} className="font-medium text-foreground hover:underline">
          {r.returnAwb}
        </button>
      ),
    },
    { key: "originalAwb", header: "Original AWB", render: (r) => r.originalAwb },
    { key: "status", header: "Status", render: (r) => <StatusBadge status={r.currentStatus} /> },
    { key: "originBranch", header: "Origin Branch", render: (r) => branchName(r.originBranchId) },
    { key: "destinationBranch", header: "Destination Branch", render: (r) => branchName(r.destinationBranchId) },
    {
      key: "merchantConfirmedAt",
      header: "Merchant Confirmed",
      render: (r) => (r.merchantConfirmedAt ? new Date(r.merchantConfirmedAt).toLocaleDateString() : "—"),
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
    return (
      <ErrorState
        title="Couldn't load return shipments"
        description="Something went wrong fetching the RTO list."
        onRetry={() => refetch()}
      />
    );
  }

  const pagedData: PagedResult<ReturnShipment> | undefined = data && {
    items: data,
    page: 1,
    pageSize: data.length,
    totalCount: data.length,
    totalPages: 1,
  };

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">Return Shipments</h1>
      <DataTable
        columns={columns}
        data={pagedData}
        isLoading={false}
        emptyTitle="No return shipments"
        emptyDescription="Shipments resolved as RTO will show up here."
        onPageChange={() => {}}
        rowKey={(r) => r.id}
      />
    </div>
  );
}
```

- [ ] **Step 6: Run the full frontend test suite and build, then commit**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds.

```bash
git add src/web/src/components/nav-main.tsx src/web/src/components/nav-main.test.tsx "src/web/src/app/(staff)/return-shipments"
git commit -m "feat(web): add Return Shipments list page and sidebar nav entry"
```

---

## Task 11: `ReturnShipmentDetailSheet` + Confirm Return action

**Files:**
- Create: `src/web/src/features/ndr/components/ReturnShipmentDetailSheet.tsx`
- Create: `src/web/src/features/ndr/components/ReturnShipmentDetailSheet.test.tsx`
- Modify: `src/web/src/app/(staff)/layout.tsx`

**Interfaces:**
- Consumes: `useReturnShipment`, `useConfirmReturnShipment` (Task 6), `useReturnShipmentPanelStore` (Task 6), `usePermission` (existing, `@/features/auth/hooks/usePermission`).
- Produces: mounted `<ReturnShipmentDetailSheet />` reachable from Task 10's list page and Task 9's `NdrResolvedSummary`.

- [ ] **Step 1: Write the failing tests**

Create `src/web/src/features/ndr/components/ReturnShipmentDetailSheet.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ReturnShipmentDetailSheet } from "./ReturnShipmentDetailSheet";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";
import * as clientFetchModule from "@/lib/api/clientFetch";
import type { ReturnShipment } from "@/features/ndr/types";

const returnShipment: ReturnShipment = {
  id: "rs1", originalShipmentId: "s1", originalAwb: "AWB001", returnAwb: "RTO-20260901-001",
  rtoReason: "Customer refused", originBranchId: "b2", destinationBranchId: "b1",
  currentStatus: "RtoInTransit", currentBranchId: null, merchantConfirmedAt: null,
};

function mockSessionWithPermissions(permissions: string[]) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      userId: "u1", organizationId: "o1", email: "a@b.com", fullName: "A B",
      permissions, hasAllBranchesScope: true, branches: [],
    }),
  }) as unknown as typeof fetch;
}

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("ReturnShipmentDetailSheet", () => {
  it("disables Confirm Return until the shipment reaches RtoAtOrigin", async () => {
    mockSessionWithPermissions(["rto.manage"]);
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue(returnShipment);
    useReturnShipmentPanelStore.setState({ panel: { mode: "detail", id: "rs1" } });

    renderWithProviders(<ReturnShipmentDetailSheet />);

    await waitFor(() => expect(screen.getByRole("button", { name: /confirm return/i })).toBeDisabled());
  });

  it("enables Confirm Return once the shipment is RtoAtOrigin", async () => {
    mockSessionWithPermissions(["rto.manage"]);
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ ...returnShipment, currentStatus: "RtoAtOrigin" });
    useReturnShipmentPanelStore.setState({ panel: { mode: "detail", id: "rs1" } });

    renderWithProviders(<ReturnShipmentDetailSheet />);

    await waitFor(() => expect(screen.getByRole("button", { name: /confirm return/i })).toBeEnabled());
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npm test -- ReturnShipmentDetailSheet.test.tsx`
Expected: FAIL — the component doesn't exist yet.

- [ ] **Step 3: Build `ReturnShipmentDetailSheet`**

Create `src/web/src/features/ndr/components/ReturnShipmentDetailSheet.tsx`:

```tsx
"use client";

import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useReturnShipment } from "@/features/ndr/hooks/useReturnShipment";
import { useConfirmReturnShipment } from "@/features/ndr/hooks/useConfirmReturnShipment";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function ReturnShipmentDetailSheet() {
  const panel = useReturnShipmentPanelStore((s) => s.panel);
  const close = useReturnShipmentPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";

  const { data: returnShipment, isLoading, isError, refetch } = useReturnShipment(id);
  const { data: branches } = useBranches();
  const confirmReturn = useConfirmReturnShipment(id);
  const canManage = usePermission("rto.manage");
  const branchName = (branchId: string) => branches?.find((b) => b.id === branchId)?.name ?? "—";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{returnShipment?.returnAwb ?? "Return Shipment"}</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && (
            <ErrorState
              title="Couldn't load this return shipment"
              description="It may not exist, or you may not have access to it."
              onRetry={() => refetch()}
            />
          )}
          {returnShipment && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Status</span>
                  <StatusBadge status={returnShipment.currentStatus} />
                </div>
                <Field label="Original AWB" value={returnShipment.originalAwb} />
                <Field label="Origin Branch" value={branchName(returnShipment.originBranchId)} />
                <Field label="Destination Branch" value={branchName(returnShipment.destinationBranchId)} />
                <Field label="RTO Reason" value={returnShipment.rtoReason} />
                <Field
                  label="Merchant Confirmed"
                  value={returnShipment.merchantConfirmedAt ? new Date(returnShipment.merchantConfirmedAt).toLocaleString() : "Not yet"}
                />
              </div>

              {canManage && !returnShipment.merchantConfirmedAt && (
                <div className="space-y-2">
                  {returnShipment.currentStatus !== "RtoAtOrigin" && (
                    <p className="text-sm text-muted-foreground">
                      Return must reach the origin branch before it can be confirmed, currently {returnShipment.currentStatus}.
                    </p>
                  )}
                  <Button
                    type="button"
                    disabled={returnShipment.currentStatus !== "RtoAtOrigin" || confirmReturn.isPending}
                    onClick={() => confirmReturn.mutate()}
                  >
                    {confirmReturn.isPending ? <Spinner className="h-4 w-4" /> : "Confirm Return"}
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

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/web && npm test -- ReturnShipmentDetailSheet.test.tsx`
Expected: PASS (2/2)

- [ ] **Step 5: Mount the sheet in the staff layout**

In `src/web/src/app/(staff)/layout.tsx`, add the import and mount:

```tsx
import { ReturnShipmentDetailSheet } from "@/features/ndr/components/ReturnShipmentDetailSheet";
```

```tsx
      <NdrDetailSheet />
      <ReturnShipmentDetailSheet />
```

- [ ] **Step 6: Run the full frontend test suite and build, then commit**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds.

```bash
git add src/web/src/features/ndr/components/ReturnShipmentDetailSheet.tsx src/web/src/features/ndr/components/ReturnShipmentDetailSheet.test.tsx "src/web/src/app/(staff)/layout.tsx"
git commit -m "feat(web): add ReturnShipmentDetailSheet with Confirm Return action"
```

---

## Task 12: Shipment detail Sheet NDR/RTO linkage

**Files:**
- Create: `src/web/src/features/shipments/components/ShipmentNdrRtoLinks.tsx`
- Create: `src/web/src/features/shipments/components/ShipmentNdrRtoLinks.test.tsx`
- Modify: `src/web/src/features/shipments/components/ShipmentDetailSheet.tsx`

**Interfaces:**
- Consumes: `useNdrs`, `useReturnShipments`, `useNdrPanelStore`, `useReturnShipmentPanelStore` (all Task 6).
- Produces: the other half of "linkage... visible on both records" — the original shipment side. Completes this phase's exit criteria.

- [ ] **Step 1: Write the failing tests**

Create `src/web/src/features/shipments/components/ShipmentNdrRtoLinks.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ShipmentNdrRtoLinks } from "./ShipmentNdrRtoLinks";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("ShipmentNdrRtoLinks", () => {
  it("shows a View NDR button when an unresolved NDR exists for the shipment", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockImplementation(async (path: unknown) => {
      if (typeof path === "string" && path.startsWith("/api/ndrs")) {
        return {
          items: [{
            id: "n1", shipmentId: "s1", awb: "AWB001", reason: "CustomerUnavailable",
            assignedStaffUserId: null, status: "Open", customerResponse: null, followUpAt: null,
            nextAction: null, resolution: null, resolvedAt: null, createdAt: "2026-09-01T00:00:00Z",
          }],
          page: 1, pageSize: 50, totalCount: 1, totalPages: 1,
        };
      }
      return [];
    });

    renderWithProviders(<ShipmentNdrRtoLinks shipmentId="s1" />);

    await waitFor(() => expect(screen.getByRole("button", { name: /view ndr/i })).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /view return shipment/i })).not.toBeInTheDocument();
  });

  it("renders nothing when there is no NDR or return shipment", async () => {
    const spy = vi.spyOn(clientFetchModule, "clientFetch").mockImplementation(async (path: unknown) => {
      if (typeof path === "string" && path.startsWith("/api/ndrs")) {
        return { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 };
      }
      return [];
    });

    const { container } = renderWithProviders(<ShipmentNdrRtoLinks shipmentId="s1" />);

    await waitFor(() => expect(spy).toHaveBeenCalled());
    expect(container).toBeEmptyDOMElement();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npm test -- ShipmentNdrRtoLinks.test.tsx`
Expected: FAIL — the component doesn't exist yet.

- [ ] **Step 3: Build `ShipmentNdrRtoLinks`**

Create `src/web/src/features/shipments/components/ShipmentNdrRtoLinks.tsx`:

```tsx
"use client";

import { Button } from "@/components/ui/button";
import { useNdrs } from "@/features/ndr/hooks/useNdrs";
import { useReturnShipments } from "@/features/ndr/hooks/useReturnShipments";
import { useNdrPanelStore } from "@/features/ndr/store/ndrPanelStore";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";

// Lives in features/shipments (not features/ndr) since it's rendered from and scoped to
// ShipmentDetailSheet — matches this codebase's existing precedent of cross-feature imports where
// the consuming feature owns the component (e.g. ShipmentDetailSheet already imports useBranches
// from features/branches).
export function ShipmentNdrRtoLinks({ shipmentId }: { shipmentId: string }) {
  const { data: ndrs } = useNdrs({ shipmentId, pageSize: 50 });
  const { data: returnShipments } = useReturnShipments(shipmentId);
  const openNdrDetail = useNdrPanelStore((s) => s.openDetail);
  const openReturnShipmentDetail = useReturnShipmentPanelStore((s) => s.openDetail);

  const latestNdr = ndrs?.items.find((n) => n.status !== "Resolved") ?? ndrs?.items[0];
  const returnShipment = returnShipments?.[0];

  if (!latestNdr && !returnShipment) {
    return null;
  }

  return (
    <div className="flex gap-2">
      {latestNdr && (
        <Button type="button" variant="outline" size="sm" onClick={() => openNdrDetail(latestNdr.id)}>
          View NDR
        </Button>
      )}
      {returnShipment && (
        <Button type="button" variant="outline" size="sm" onClick={() => openReturnShipmentDetail(returnShipment.id)}>
          View Return Shipment
        </Button>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/web && npm test -- ShipmentNdrRtoLinks.test.tsx`
Expected: PASS (2/2)

- [ ] **Step 5: Wire it into `ShipmentDetailSheet`**

In `src/web/src/features/shipments/components/ShipmentDetailSheet.tsx`, add the import:

```tsx
import { ShipmentNdrRtoLinks } from "@/features/shipments/components/ShipmentNdrRtoLinks";
```

and change the end of the `Items` section (the last block inside the `{shipment && ( <> ... </> )}`
fragment) from:

```tsx
              <div className="space-y-2">
                <span className="text-xs text-muted-foreground">Items</span>
                <ul className="space-y-1">
                  {shipment.items.map((item) => (
                    <li key={item.id} className="flex justify-between text-sm">
                      <span>{item.description} × {item.quantity}</span>
                      <span>{currencyFormatter.format(item.unitValue)}</span>
                    </li>
                  ))}
                </ul>
              </div>
            </>
          )}
```

to:

```tsx
              <div className="space-y-2">
                <span className="text-xs text-muted-foreground">Items</span>
                <ul className="space-y-1">
                  {shipment.items.map((item) => (
                    <li key={item.id} className="flex justify-between text-sm">
                      <span>{item.description} × {item.quantity}</span>
                      <span>{currencyFormatter.format(item.unitValue)}</span>
                    </li>
                  ))}
                </ul>
              </div>

              {(shipment.currentStatus === "NdrCreated" || shipment.currentStatus.startsWith("Rto")) && (
                <ShipmentNdrRtoLinks shipmentId={shipment.id} />
              )}
            </>
          )}
```

- [ ] **Step 6: Run the full frontend test suite and build, then commit**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds.

```bash
git add src/web/src/features/shipments/components/ShipmentNdrRtoLinks.tsx src/web/src/features/shipments/components/ShipmentNdrRtoLinks.test.tsx src/web/src/features/shipments/components/ShipmentDetailSheet.tsx
git commit -m "feat(web): link shipment detail to its NDR/return shipment records"
```

---

## Task 13: Update `FRONTEND-ROADMAP.md`; manual E2E verification

**Files:**
- Modify: `docs/FRONTEND-ROADMAP.md`

**Interfaces:**
- None — this task verifies the whole phase end-to-end and records completion, no new production code.

- [ ] **Step 1: Run the full backend and frontend suites**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj && dotnet test tests/Api.Tests/Api.Tests.csproj --filter NdrEndpointsTests`
Run: `cd src/web && npm test && npm run build`
Expected: PASS for all of the above. Fix any failure before proceeding — do not mark this phase done with a red suite.

- [ ] **Step 2: Manual E2E verification against the real API/Postgres**

Start both servers (backend API and `src/web` dev server) against the real Postgres container. Log in as a staff user with `ndr.read`, `ndr.manage`, `ndr.resolve`, `rto.read`, `rto.manage`, and walk:

1. Book a shipment and fail a delivery attempt (existing F5 flow: pickup → receive-origin → manifest to destination → assign delivery task → record a failed attempt). Confirm it lands in `/ndrs` with the correct AWB and `Open` status.
2. Open the NDR, move `Open → Contacting → CustomerConfirmed`, entering a Customer Response each time. Confirm each move persists (re-open the sheet, or watch the status badge update).
3. From `CustomerConfirmed`, click "Request RTO". Enter an `RtoReason` and resolve. Confirm the NDR shows `Resolved` with `Resolution: Rto`, and a "View Return Shipment" button appears.
4. Click "View Return Shipment". Confirm the sheet shows the correct `Original AWB` and `RtoInitiated` status, and the `/return-shipments` list shows the same row.
5. Open the original shipment's detail Sheet (from `/shipments`). Confirm a "View Return Shipment" button appears and opens the same return-shipment Sheet.
6. Dispatch/receive the return leg via the existing F4 Manifests flow (`Purpose: Return`, scan the *original* AWB, seal, dispatch, receive-scan) to walk the shipment through `RtoInTransit → RtoAtOrigin`. Confirm the Return Shipment Sheet's "Confirm Return" button is disabled until `RtoAtOrigin`, with the inline caption showing the current status.
7. Once `RtoAtOrigin`, click "Confirm Return". Confirm `Merchant Confirmed` is now set and the shipment reaches `RtoDelivered`.
8. Repeat steps 1–2 on a second shipment, but from `CustomerConfirmed` click "Schedule Reattempt" instead, then resolve as Reattempt. Confirm the shipment returns to `OutForDelivery` and the NDR shows `Resolved`/`Reattempt`.
9. On a third shipment (any pre-`Resolved` NDR status), click "Resolve — Other" and confirm it resolves without touching the shipment's status.
10. As a branch-restricted user (no `UserBranch` grant covering the relevant branch), confirm `/ndrs` and `/return-shipments` do not show NDRs/return shipments outside that user's branch access.

Record any real bugs found and fixed during this pass in Step 3 below, following every prior phase's practice — do not silently fix without documenting.

- [ ] **Step 3: Update `docs/FRONTEND-ROADMAP.md`**

Find this text in the Phase F6 section:

```
*Status: Not started.*

## Phase F7 — COD Operations
```

Replace it with (adjusting the completion note to reflect anything actually found during Step 2's manual pass — do not copy this verbatim if the verification surfaced different findings):

```
*Status: Done (2026-09-01).* Shipped the full NDR workflow (`/ndrs` list + status-driven detail
Sheet — `NdrUpdateSection` for `Open`/`Contacting`/`CustomerConfirmed`'s forward moves,
`NdrReattemptSection`/`NdrRtoSection` for the two resolution paths off `CustomerConfirmed`, an
always-available `NdrResolveOtherAction`, and a `NdrResolvedSummary` linking to the resulting
return shipment) and RTO tracking (`/return-shipments` list + `ReturnShipmentDetailSheet` with a
Confirm Return action gated on both `rto.manage` and the live shipment status actually being
`RtoAtOrigin`, matching the backend's own state guard). Both `ShipmentDetailSheet` and
`ReturnShipmentDetailSheet` now show the AWB linkage in both directions, closing this phase's exit
criteria.

Five real backend gaps were resolved before frontend work began (Tasks 1-5, recorded in
`docs/CLARIFICATIONS.md` §16): all 3 Ndr enums (`NdrStatus`, `NdrReason`, `NdrResolution`) got
`JsonStringEnumConverter` in one pass, closing the same latent bug class hit narrowly in every
prior phase; `NdrResponse`/`ReturnShipmentResponse` were enriched with `Awb`/`OriginalAwb`, sourced
from the `ShipmentScanInfo` lookup those methods already performed or now perform once per row; and
`GET /api/v1/ndrs`/`GET /api/v1/return-shipments` gained `shipmentId`/`originalShipmentId` filters,
letting the shipment detail Sheet discover its own NDR history and RTO linkage.

One deliberate deviation from the design spec: `NdrResolveOtherAction` ships without the
short-reason-note field the spec described, since `NdrService.ResolveAsync`'s `Other` branch never
reads or persists `ResolveNdrRequest.RtoReason` for that resolution — no `NDR` column exists to
hold it. Building an input for a value the backend silently discards would have been misleading.

Manual E2E verification against the real API/Postgres [fill in: clean pass, or real bugs found and
fixed — following every prior phase's practice, do not claim a clean pass unless Step 2 above
actually found nothing].

## Phase F7 — COD Operations
```

- [ ] **Step 4: Commit**

```bash
git add docs/FRONTEND-ROADMAP.md
git commit -m "docs: mark Frontend Phase F6 (NDR + RTO) complete"
```
