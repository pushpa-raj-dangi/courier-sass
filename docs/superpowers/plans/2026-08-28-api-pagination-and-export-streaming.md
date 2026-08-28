# API Pagination (High-Volume Lists) + CSV Export Streaming Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add offset pagination to `GET /api/v1/shipments`, `GET /api/v1/cod/collections`, and `GET /api/v1/ndrs`, and stream (instead of fully in-memory buffer) the two CSV export endpoints in Reports.

**Architecture:** A new shared `PagedResult<T>`/`PaginationDefaults` pair in `SharedKernel` used by all three list endpoints. Since `Shipment`, `CODCollection`, and `NDR` have no creation-timestamp column today, a migration adds `CreatedAt` to all three (backfilled via `now()`), which becomes the stable sort key (`CreatedAt` desc, `Id` asc tiebreaker) every paginated query needs for correct `OFFSET/LIMIT` behavior. `NdrService.ListAsync` gets true SQL-level pagination only for `AllBranches`-scope callers; branch-restricted callers keep today's in-memory branch filter, now followed by in-memory paging (documented limitation, not a regression). The two CSV export endpoints keep their existing row-materialization + name-batch-resolution shape (a redesign is out of scope) but stop building the whole formatted CSV as one in-memory string, streaming it line-by-line to the response instead.

**Tech Stack:** ASP.NET Core Minimal APIs, EF Core 10 (Npgsql), xUnit, `WebApplicationFactory` for API-level tests, real Postgres (shared `courier-postgres-1` container, port 5434) for `Api.Tests`.

**Spec:** `docs/superpowers/specs/2026-08-28-api-pagination-and-export-streaming-design.md`

## Global Constraints

- `PagedResult<T>` shape: `{ Items: T[], Page: int, PageSize: int, TotalCount: int, TotalPages: int }` (`TotalPages` computed, not stored).
- `PaginationDefaults`: `DefaultPageSize = 25`, `MaxPageSize = 200`; `page` clamps to a minimum of 1, `pageSize` clamps to `[1, MaxPageSize]`.
- Stable sort on every paginated query: `OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)`.
- No N+1 fixes, missing-index fixes, or `AsNoTracking()` audit in this plan — explicitly deferred (spec §6).
- No pagination for Manifests, Merchants, Riders, Webhooks, Branches, Users, or Pricing reference-data endpoints in this plan — explicitly deferred (spec §6).
- CSV export endpoints (`/api/v1/reports/export/shipments`, `/api/v1/reports/export/cod-summary`) get **no** `page`/`pageSize` params — they stay full-range dumps, only their memory shape changes.
- Every `git commit` step in this plan follows the repo's existing convention: plain `git add <files>` + `git commit -m "..."` (no `--no-verify`, no amend).

---

### Task 1: `PagedResult<T>` and `PaginationDefaults` in SharedKernel

**Files:**
- Create: `src/SharedKernel/PagedResult.cs`
- Create: `src/SharedKernel/PaginationDefaults.cs`
- Test: `tests/SharedKernel.Tests/PaginationDefaultsTests.cs`

**Interfaces:**
- Produces: `Courier.SharedKernel.PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)` with computed `int TotalPages`.
- Produces: `Courier.SharedKernel.PaginationDefaults.Normalize(int? page, int? pageSize) : (int Page, int PageSize)`, and public constants `DefaultPageSize`/`MaxPageSize`.

- [ ] **Step 1: Write the failing tests**

Create `tests/SharedKernel.Tests/PaginationDefaultsTests.cs`:

```csharp
using Courier.SharedKernel;

namespace Courier.SharedKernel.Tests;

public class PaginationDefaultsTests
{
    [Fact]
    public void Normalize_NullPageAndPageSize_UsesDefaults()
    {
        var (page, pageSize) = PaginationDefaults.Normalize(null, null);

        Assert.Equal(1, page);
        Assert.Equal(PaginationDefaults.DefaultPageSize, pageSize);
    }

    [Fact]
    public void Normalize_PageBelowOne_ClampsToOne()
    {
        var (page, _) = PaginationDefaults.Normalize(0, 10);

        Assert.Equal(1, page);
    }

    [Fact]
    public void Normalize_PageSizeAboveMax_ClampsToMax()
    {
        var (_, pageSize) = PaginationDefaults.Normalize(1, 10_000);

        Assert.Equal(PaginationDefaults.MaxPageSize, pageSize);
    }

    [Fact]
    public void Normalize_PageSizeBelowOne_ClampsToOne()
    {
        var (_, pageSize) = PaginationDefaults.Normalize(1, 0);

        Assert.Equal(1, pageSize);
    }

    [Fact]
    public void Normalize_ValidValues_PassThroughUnchanged()
    {
        var (page, pageSize) = PaginationDefaults.Normalize(3, 50);

        Assert.Equal(3, page);
        Assert.Equal(50, pageSize);
    }
}

public class PagedResultTests
{
    [Fact]
    public void TotalPages_ZeroTotalCount_IsZero()
    {
        var result = new PagedResult<int>(Array.Empty<int>(), 1, 25, 0);

        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public void TotalPages_ExactMultiple_DoesNotRoundUp()
    {
        var result = new PagedResult<int>(Array.Empty<int>(), 1, 25, 50);

        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public void TotalPages_WithRemainder_RoundsUp()
    {
        var result = new PagedResult<int>(Array.Empty<int>(), 1, 25, 51);

        Assert.Equal(3, result.TotalPages);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail to compile**

Run: `dotnet test tests/SharedKernel.Tests/SharedKernel.Tests.csproj`
Expected: FAIL — `PaginationDefaults`/`PagedResult<T>` do not exist yet (CS0246).

- [ ] **Step 3: Create `PagedResult<T>`**

Create `src/SharedKernel/PagedResult.cs`:

```csharp
namespace Courier.SharedKernel;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
```

- [ ] **Step 4: Create `PaginationDefaults`**

Create `src/SharedKernel/PaginationDefaults.cs`:

```csharp
namespace Courier.SharedKernel;

public static class PaginationDefaults
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/SharedKernel.Tests/SharedKernel.Tests.csproj`
Expected: PASS (all 8 new tests).

- [ ] **Step 6: Commit**

```bash
git add src/SharedKernel/PagedResult.cs src/SharedKernel/PaginationDefaults.cs tests/SharedKernel.Tests/PaginationDefaultsTests.cs
git commit -m "feat(shared-kernel): add PagedResult<T> and PaginationDefaults"
```

---

### Task 2: Add `CreatedAt` to `Shipment`, `CODCollection`, `NDR`

**Context:** None of these three entities have a creation-timestamp column, and all use random (`Guid.NewGuid()`) ids, so there is nothing to sort on for deterministic "newest first" pagination (spec §3a). This task adds the column (migration + domain + EF config + set-at-creation) and also surfaces it on each entity's response DTO, both because a courier-ops list without a "when was this created" field is a weak API and because it lets Tasks 3–5's pagination tests assert real chronological order instead of only set-membership.

**Files:**
- Modify: `src/Modules/Shipments/Domain/Shipment.cs`
- Modify: `src/Modules/Cod/Domain/CODCollection.cs`
- Modify: `src/Modules/Ndr/Domain/NDR.cs`
- Modify: `src/Modules/Shipments/Infrastructure/ShipmentConfiguration.cs`
- Modify: `src/Modules/Cod/Infrastructure/CODCollectionConfiguration.cs`
- Modify: `src/Modules/Ndr/Infrastructure/NdrConfiguration.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentService.cs:145`
- Modify: `src/Modules/Cod/Application/CodService.Collections.cs:15`
- Modify: `src/Modules/Ndr/Application/NdrCreationService.cs:34`
- Modify: `src/Modules/Shipments/Application/ShipmentDtos.cs` (`ShipmentResponse` record)
- Modify: `src/Modules/Shipments/Application/ShipmentService.cs:278` (`ToResponse`)
- Modify: `src/Modules/Cod/Application/CodDtos.cs` (`CodCollectionResponse` record)
- Modify: `src/Modules/Cod/Application/CodService.cs:49` (`ToResponse`)
- Modify: `src/Modules/Ndr/Application/NdrDtos.cs` (`NdrResponse` record)
- Modify: `src/Modules/Ndr/Application/NdrService.cs:97` (`ToResponse`)
- Modify: `tests/Riders.Tests/RiderServiceAttemptsTests.cs:64-67`
- Modify: `tests/Ndr.Tests/NdrCreationServiceTests.cs:46-49`
- Modify: `tests/Riders.Tests/RiderServiceTasksTests.cs:69-72`
- Test: `tests/Api.Tests/ShipmentsEndpointsTests.cs` (new fact)
- Create: EF Core migration under `src/Persistence/Migrations`

**Interfaces:**
- Produces: `Shipment.CreatedAt`, `CODCollection.CreatedAt`, `NDR.CreatedAt` (`DateTimeOffset`, non-nullable), set at creation, DB-defaulted to `now()` for backfill.
- Produces: `ShipmentResponse.CreatedAt`, `CodCollectionResponse.CreatedAt`, `NdrResponse.CreatedAt` — each appended as the **last** positional parameter (existing callers keep compiling unless they construct the record positionally, which only the three listed test files do).

- [ ] **Step 1: Write the failing test**

Add to `tests/Api.Tests/ShipmentsEndpointsTests.cs` (near the other booking-flow tests):

```csharp
    [Fact]
    public async Task CreateShipment_SetsCreatedAtCloseToNow()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "created-at");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(client);

        var before = DateTimeOffset.UtcNow;
        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(shipment!.CreatedAt, before, after);
    }
```

- [ ] **Step 2: Run test to verify it fails to compile**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter CreateShipment_SetsCreatedAtCloseToNow`
Expected: FAIL — `ShipmentResponse` has no `CreatedAt` member (CS1729/CS0117).

- [ ] **Step 3: Add `CreatedAt` to the three domain entities**

In `src/Modules/Shipments/Domain/Shipment.cs`, add after `public Guid Id { get; set; }`:

```csharp
    public DateTimeOffset CreatedAt { get; set; }
```

In `src/Modules/Cod/Domain/CODCollection.cs`, add after `public Guid Id { get; set; }`:

```csharp
    public DateTimeOffset CreatedAt { get; set; }
```

In `src/Modules/Ndr/Domain/NDR.cs`, add after `public Guid Id { get; set; }`:

```csharp
    public DateTimeOffset CreatedAt { get; set; }
```

- [ ] **Step 4: Configure the DB default so existing rows backfill on migration**

In `src/Modules/Shipments/Infrastructure/ShipmentConfiguration.cs`, add inside `Configure`, right after `builder.HasKey(s => s.Id);`:

```csharp
        builder.Property(s => s.CreatedAt).HasDefaultValueSql("now()");
```

In `src/Modules/Cod/Infrastructure/CODCollectionConfiguration.cs`, add right after `builder.HasKey(c => c.Id);`:

```csharp
        builder.Property(c => c.CreatedAt).HasDefaultValueSql("now()");
```

In `src/Modules/Ndr/Infrastructure/NdrConfiguration.cs`, add right after `builder.HasKey(n => n.Id);`:

```csharp
        builder.Property(n => n.CreatedAt).HasDefaultValueSql("now()");
```

- [ ] **Step 5: Generate the migration**

Run: `dotnet ef migrations add AddCreatedAtToShipmentCodCollectionNdr --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: a new file under `src/Persistence/Migrations/` adding a non-nullable `CreatedAt` `timestamp with time zone` column with `defaultValueSql: "now()"` to `Shipments`, `CODCollections`, and `Ndrs`, plus an updated `AppDbContextModelSnapshot.cs`.

- [ ] **Step 6: Set `CreatedAt` explicitly at each creation site**

In `src/Modules/Shipments/Application/ShipmentService.cs`, in the `shipment = new Shipment { ... }` initializer (around line 145):

```csharp
            shipment = new Shipment
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                OrganizationId = organizationId,
```

In `src/Modules/Cod/Application/CodService.Collections.cs`, in `CreateExpectedAsync` (around line 15):

```csharp
        var collection = new CODCollection
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            ShipmentId = shipmentId,
```

In `src/Modules/Ndr/Application/NdrCreationService.cs`, in `CreateFromFailedAttemptAsync` (around line 34):

```csharp
        var ndr = new NDR
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            OrganizationId = organizationId,
```

- [ ] **Step 7: Add `CreatedAt` to the three response DTOs**

In `src/Modules/Shipments/Application/ShipmentDtos.cs`, change the `ShipmentResponse` record's last two lines from:

```csharp
    string SenderAddressLine,
    List<ShipmentItemResponse> Items);
```

to:

```csharp
    string SenderAddressLine,
    List<ShipmentItemResponse> Items,
    DateTimeOffset CreatedAt);
```

In `src/Modules/Cod/Application/CodDtos.cs`, change the `CodCollectionResponse` record from:

```csharp
public record CodCollectionResponse(
    Guid Id, Guid ShipmentId, Guid MerchantId, Guid BranchId, decimal ExpectedAmount, decimal ShipmentTotalCharge,
    decimal? CollectedAmount, CodCollectionStatus Status, Guid? CollectedByRiderId, DateTimeOffset? CollectedAt,
    Guid? DeliveryProofId, Guid? RiderCashSettlementId, Guid? MerchantSettlementItemId);
```

to:

```csharp
public record CodCollectionResponse(
    Guid Id, Guid ShipmentId, Guid MerchantId, Guid BranchId, decimal ExpectedAmount, decimal ShipmentTotalCharge,
    decimal? CollectedAmount, CodCollectionStatus Status, Guid? CollectedByRiderId, DateTimeOffset? CollectedAt,
    Guid? DeliveryProofId, Guid? RiderCashSettlementId, Guid? MerchantSettlementItemId, DateTimeOffset CreatedAt);
```

In `src/Modules/Ndr/Application/NdrDtos.cs`, change the `NdrResponse` record from:

```csharp
public record NdrResponse(
    Guid Id, Guid ShipmentId, NdrReason Reason, Guid? AssignedStaffUserId, NdrStatus Status,
    string? CustomerResponse, DateTimeOffset? FollowUpAt, string? NextAction, NdrResolution? Resolution, DateTimeOffset? ResolvedAt);
```

to:

```csharp
public record NdrResponse(
    Guid Id, Guid ShipmentId, NdrReason Reason, Guid? AssignedStaffUserId, NdrStatus Status,
    string? CustomerResponse, DateTimeOffset? FollowUpAt, string? NextAction, NdrResolution? Resolution, DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt);
```

- [ ] **Step 8: Update the three `ToResponse` mappers**

In `src/Modules/Shipments/Application/ShipmentService.cs`, change `ToResponse` (around line 278) from ending in:

```csharp
        items.Select(i => new ShipmentItemResponse(i.Id, i.Description, i.Quantity, i.UnitValue)).ToList());
```

to:

```csharp
        items.Select(i => new ShipmentItemResponse(i.Id, i.Description, i.Quantity, i.UnitValue)).ToList(),
        shipment.CreatedAt);
```

In `src/Modules/Cod/Application/CodService.cs`, change `ToResponse` (around line 49) from:

```csharp
    private static CodCollectionResponse ToResponse(CODCollection collection) => new(
        collection.Id, collection.ShipmentId, collection.MerchantId, collection.BranchId, collection.ExpectedAmount,
        collection.ShipmentTotalCharge, collection.CollectedAmount, collection.Status, collection.CollectedByRiderId,
        collection.CollectedAt, collection.DeliveryProofId, collection.RiderCashSettlementId, collection.MerchantSettlementItemId);
```

to:

```csharp
    private static CodCollectionResponse ToResponse(CODCollection collection) => new(
        collection.Id, collection.ShipmentId, collection.MerchantId, collection.BranchId, collection.ExpectedAmount,
        collection.ShipmentTotalCharge, collection.CollectedAmount, collection.Status, collection.CollectedByRiderId,
        collection.CollectedAt, collection.DeliveryProofId, collection.RiderCashSettlementId, collection.MerchantSettlementItemId,
        collection.CreatedAt);
```

In `src/Modules/Ndr/Application/NdrService.cs`, change `ToResponse` (around line 97) from:

```csharp
    private static NdrResponse ToResponse(NDR ndr) => new(
        ndr.Id, ndr.ShipmentId, ndr.Reason, ndr.AssignedStaffUserId, ndr.Status,
        ndr.CustomerResponse, ndr.FollowUpAt, ndr.NextAction, ndr.Resolution, ndr.ResolvedAt);
```

to:

```csharp
    private static NdrResponse ToResponse(NDR ndr) => new(
        ndr.Id, ndr.ShipmentId, ndr.Reason, ndr.AssignedStaffUserId, ndr.Status,
        ndr.CustomerResponse, ndr.FollowUpAt, ndr.NextAction, ndr.Resolution, ndr.ResolvedAt,
        ndr.CreatedAt);
```

- [ ] **Step 9: Fix the three test fakes that construct `ShipmentResponse` positionally**

In `tests/Riders.Tests/RiderServiceAttemptsTests.cs`, change:

```csharp
        var response = new ShipmentResponse(
            shipmentId, "TESTAWB1", merchantId, Guid.NewGuid(), Guid.NewGuid(), null, null, null,
            ShipmentStatus.Delivered, Guid.NewGuid(), 1m, null, null, null, 100m, 0m, false, 100m, 0m, 0m, 100m,
            recipientName, "9800000000", "Address", "Sender", "9800000001", "Address", new List<ShipmentItemResponse>());
```

to:

```csharp
        var response = new ShipmentResponse(
            shipmentId, "TESTAWB1", merchantId, Guid.NewGuid(), Guid.NewGuid(), null, null, null,
            ShipmentStatus.Delivered, Guid.NewGuid(), 1m, null, null, null, 100m, 0m, false, 100m, 0m, 0m, 100m,
            recipientName, "9800000000", "Address", "Sender", "9800000001", "Address", new List<ShipmentItemResponse>(), DateTimeOffset.UtcNow);
```

In `tests/Ndr.Tests/NdrCreationServiceTests.cs`, change:

```csharp
        var response = new ShipmentResponse(
            shipmentId, AwbToReturn, MerchantIdToReturn, Guid.NewGuid(), Guid.NewGuid(), null, null, null,
            ShipmentStatus.NdrCreated, Guid.NewGuid(), 1m, null, null, null, 100m, 0m, false, 100m, 0m, 0m, 100m,
            "Recipient", "9800000000", "Address", "Sender", "9800000001", "Address", new List<ShipmentItemResponse>());
```

to:

```csharp
        var response = new ShipmentResponse(
            shipmentId, AwbToReturn, MerchantIdToReturn, Guid.NewGuid(), Guid.NewGuid(), null, null, null,
            ShipmentStatus.NdrCreated, Guid.NewGuid(), 1m, null, null, null, 100m, 0m, false, 100m, 0m, 0m, 100m,
            "Recipient", "9800000000", "Address", "Sender", "9800000001", "Address", new List<ShipmentItemResponse>(), DateTimeOffset.UtcNow);
```

In `tests/Riders.Tests/RiderServiceTasksTests.cs`, change:

```csharp
        return Task.FromResult<ShipmentResponse?>(new ShipmentResponse(
            shipmentId, info.Awb, Guid.NewGuid(), info.OriginBranchId, info.DestinationBranchId, null, null, riderId,
            ShipmentStatus.PickedUp, Guid.NewGuid(), 1m, null, null, null, 100m, 0m, false, 100m, 0m, 0m, 100m,
            "Recipient", "9800000000", "Address", "Sender", "9800000001", "Address", new List<ShipmentItemResponse>()));
```

to:

```csharp
        return Task.FromResult<ShipmentResponse?>(new ShipmentResponse(
            shipmentId, info.Awb, Guid.NewGuid(), info.OriginBranchId, info.DestinationBranchId, null, null, riderId,
            ShipmentStatus.PickedUp, Guid.NewGuid(), 1m, null, null, null, 100m, 0m, false, 100m, 0m, 0m, 100m,
            "Recipient", "9800000000", "Address", "Sender", "9800000001", "Address", new List<ShipmentItemResponse>(), DateTimeOffset.UtcNow));
```

- [ ] **Step 10: Run the full build to catch any remaining compile errors**

Run: `dotnet build`
Expected: build succeeds with no errors.

- [ ] **Step 11: Run the affected test projects**

Check the shared Postgres container is up: `docker exec courier-postgres-1 pg_isready -U courier` (start it with `docker start courier-postgres-1` if not; never `docker compose up` a second instance).

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter CreateShipment_SetsCreatedAtCloseToNow`
Expected: PASS.

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj`
Expected: all PASS (the two fixed fakes still compile and behave identically).

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj`
Expected: all PASS.

- [ ] **Step 12: Commit**

```bash
git add src/Modules/Shipments/Domain/Shipment.cs src/Modules/Cod/Domain/CODCollection.cs src/Modules/Ndr/Domain/NDR.cs \
  src/Modules/Shipments/Infrastructure/ShipmentConfiguration.cs src/Modules/Cod/Infrastructure/CODCollectionConfiguration.cs src/Modules/Ndr/Infrastructure/NdrConfiguration.cs \
  src/Modules/Shipments/Application/ShipmentService.cs src/Modules/Cod/Application/CodService.Collections.cs src/Modules/Ndr/Application/NdrCreationService.cs \
  src/Modules/Shipments/Application/ShipmentDtos.cs src/Modules/Cod/Application/CodDtos.cs src/Modules/Ndr/Application/NdrDtos.cs \
  src/Modules/Cod/Application/CodService.cs src/Modules/Ndr/Application/NdrService.cs \
  src/Persistence/Migrations tests/Api.Tests/ShipmentsEndpointsTests.cs \
  tests/Riders.Tests/RiderServiceAttemptsTests.cs tests/Ndr.Tests/NdrCreationServiceTests.cs tests/Riders.Tests/RiderServiceTasksTests.cs
git commit -m "feat(shipments,cod,ndr): add CreatedAt column and expose it on each entity's response DTO"
```

---

### Task 3: Paginate `GET /api/v1/shipments`

**Files:**
- Modify: `src/Modules/Shipments/Application/ShipmentService.Reads.cs`
- Modify: `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs:74-82`
- Test: `tests/Api.Tests/ShipmentsEndpointsTests.cs`

**Interfaces:**
- Consumes: `PagedResult<T>` and `PaginationDefaults.Normalize` from `Courier.SharedKernel` (Task 1); `ShipmentResponse.CreatedAt` (Task 2).
- Produces: `ShipmentService.ListAsync(int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken)` returning `Task<PagedResult<ShipmentResponse>>` (was `Task<List<ShipmentResponse>>`).

- [ ] **Step 1: Write the failing test**

Add to `tests/Api.Tests/ShipmentsEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task List_Paginates_ReturnsDisjointPagesInNewestFirstOrder()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "list-page");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(client);

        var booked = new List<ShipmentResponse>();
        for (var i = 0; i < 3; i++)
        {
            var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();
            booked.Add(shipment!);
        }

        var page1 = await (await client.GetAsync("/api/v1/shipments?page=1&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<ShipmentResponse>>();
        var page2 = await (await client.GetAsync("/api/v1/shipments?page=2&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<ShipmentResponse>>();

        Assert.Equal(3, page1!.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2!.Items);

        var page1Ids = page1.Items.Select(s => s.Id).ToHashSet();
        var page2Ids = page2.Items.Select(s => s.Id).ToHashSet();
        Assert.Empty(page1Ids.Intersect(page2Ids));
        Assert.Equal(booked.Select(s => s.Id).ToHashSet(), page1Ids.Union(page2Ids));

        // Newest-first: the two most recently booked shipments are on page 1.
        var expectedNewestFirst = booked.OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id).Select(s => s.Id).ToList();
        Assert.Equal(expectedNewestFirst.Take(2), page1.Items.Select(s => s.Id));
        Assert.Equal(expectedNewestFirst.Skip(2), page2.Items.Select(s => s.Id));
    }
```

Add `using Courier.SharedKernel;` to the file's `using` block (it currently has none).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter List_Paginates_ReturnsDisjointPagesInNewestFirstOrder`
Expected: FAIL — deserializing into `PagedResult<ShipmentResponse>` fails (the endpoint still returns a bare JSON array), and/or `page`/`pageSize` query params are ignored.

- [ ] **Step 3: Paginate `ShipmentService.ListAsync`**

In `src/Modules/Shipments/Application/ShipmentService.Reads.cs`, replace the whole `ListAsync` method with:

```csharp
    public async Task<PagedResult<ShipmentResponse>> ListAsync(int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Shipment>().AsQueryable();
        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            query = query.Where(s => s.MerchantId == callerMerchantId);
        }
        else
        {
            var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "shipment.read", cancellationToken);
            if (accessibleBranchIds is not null)
            {
                var accessibleBranchIdsArray = accessibleBranchIds.ToArray();
                query = query.Where(s =>
                    accessibleBranchIdsArray.Contains(s.OriginBranchId) ||
                    accessibleBranchIdsArray.Contains(s.DestinationBranchId) ||
                    (s.CurrentBranchId != null && accessibleBranchIdsArray.Contains(s.CurrentBranchId.Value)));
            }
        }

        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var shipments = await query
            .OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var responses = new List<ShipmentResponse>();
        foreach (var shipment in shipments)
        {
            var items = await _dbContext.Set<ShipmentItem>().Where(i => i.ShipmentId == shipment.Id).ToListAsync(cancellationToken);
            responses.Add(ToResponse(shipment, items));
        }
        return new PagedResult<ShipmentResponse>(responses, normalizedPage, normalizedPageSize, totalCount);
    }
```

- [ ] **Step 4: Wire the endpoint**

In `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs`, replace:

```csharp
        group.MapGet("/", async (ShipmentService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (!await HasReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker))
            {
                return httpContext.User.GetMerchantId() is null && httpContext.User.GetUserId() is null ? Results.Unauthorized() : Results.Forbid();
            }

            return Results.Ok(await service.ListAsync(httpContext.User, httpContext.RequestAborted));
        });
```

with:

```csharp
        group.MapGet("/", async (int? page, int? pageSize, ShipmentService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (!await HasReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker))
            {
                return httpContext.User.GetMerchantId() is null && httpContext.User.GetUserId() is null ? Results.Unauthorized() : Results.Forbid();
            }

            return Results.Ok(await service.ListAsync(page, pageSize, httpContext.User, httpContext.RequestAborted));
        });
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter List_Paginates_ReturnsDisjointPagesInNewestFirstOrder`
Expected: PASS.

- [ ] **Step 6: Run the full Api.Tests suite to catch regressions**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: all PASS — in particular `Shipments_OrgBCannotReadOrgAsShipment` and `PricingSnapshot_RateCardSupersededAfterBooking_ShipmentTotalChargeUnchanged`, which don't call `ListAsync` but share the same file/DTO.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Shipments/Application/ShipmentService.Reads.cs src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs tests/Api.Tests/ShipmentsEndpointsTests.cs
git commit -m "feat(shipments): paginate GET /api/v1/shipments"
```

---

### Task 4: Paginate `GET /api/v1/cod/collections`

**Files:**
- Modify: `src/Modules/Cod/Application/CodService.Reads.cs`
- Modify: `src/Modules/Cod/Presentation/CodEndpoints.cs:19-21`
- Test: `tests/Api.Tests/CodEndpointsTests.cs`

**Interfaces:**
- Consumes: `PagedResult<T>`, `PaginationDefaults.Normalize` (Task 1); `CodCollectionResponse.CreatedAt` (Task 2).
- Produces: `CodService.ListCollectionsAsync(CodCollectionStatus? status, Guid? branchId, Guid? merchantId, int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken)` returning `Task<PagedResult<CodCollectionResponse>>` (was `Task<List<CodCollectionResponse>>`).

- [ ] **Step 1: Write the failing test**

Add to `tests/Api.Tests/CodEndpointsTests.cs` (add `using Courier.SharedKernel;` to the file's `using` block, which currently has none):

```csharp
    [Fact]
    public async Task ListCollections_Paginates_ReturnsDisjointPagesInNewestFirstOrder()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "cod-list-page");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);

        var booked = new List<ShipmentResponse>();
        for (var i = 0; i < 3; i++)
        {
            var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", CodShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId, 500m))).Content.ReadFromJsonAsync<ShipmentResponse>();
            booked.Add(shipment!);
        }

        var page1 = await (await client.GetAsync("/api/v1/cod/collections?page=1&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<CodCollectionResponse>>();
        var page2 = await (await client.GetAsync("/api/v1/cod/collections?page=2&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<CodCollectionResponse>>();

        Assert.Equal(3, page1!.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2!.Items);

        var page1ShipmentIds = page1.Items.Select(c => c.ShipmentId).ToHashSet();
        var page2ShipmentIds = page2.Items.Select(c => c.ShipmentId).ToHashSet();
        Assert.Empty(page1ShipmentIds.Intersect(page2ShipmentIds));
        Assert.Equal(booked.Select(s => s.Id).ToHashSet(), page1ShipmentIds.Union(page2ShipmentIds));

        var expectedNewestFirst = booked.OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id).Select(s => s.Id).ToList();
        Assert.Equal(expectedNewestFirst.Take(2), page1.Items.Select(c => c.ShipmentId));
        Assert.Equal(expectedNewestFirst.Skip(2), page2.Items.Select(c => c.ShipmentId));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter ListCollections_Paginates_ReturnsDisjointPagesInNewestFirstOrder`
Expected: FAIL — `page`/`pageSize` are ignored and the response body doesn't deserialize into `PagedResult<CodCollectionResponse>`.

- [ ] **Step 3: Paginate `CodService.ListCollectionsAsync`**

In `src/Modules/Cod/Application/CodService.Reads.cs`, replace the whole `ListCollectionsAsync` method with:

```csharp
    public async Task<PagedResult<CodCollectionResponse>> ListCollectionsAsync(CodCollectionStatus? status, Guid? branchId, Guid? merchantId, int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CodValidationException("No active organization context.");

        var query = _dbContext.Set<CODCollection>().Where(c => c.OrganizationId == organizationId);
        if (status is not null)
        {
            query = query.Where(c => c.Status == status);
        }

        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            query = query.Where(c => c.MerchantId == callerMerchantId);
        }
        else
        {
            if (merchantId is Guid requestedMerchantId)
            {
                query = query.Where(c => c.MerchantId == requestedMerchantId);
            }
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
        }

        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var collections = await query
            .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<CodCollectionResponse>(collections.Select(ToResponse).ToList(), normalizedPage, normalizedPageSize, totalCount);
    }
```

- [ ] **Step 4: Wire the endpoint**

In `src/Modules/Cod/Presentation/CodEndpoints.cs`, replace:

```csharp
        group.MapGet("/collections", async (CodCollectionStatus? status, Guid? branchId, Guid? merchantId, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListCollectionsAsync(status, branchId, merchantId, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");
```

with:

```csharp
        group.MapGet("/collections", async (CodCollectionStatus? status, Guid? branchId, Guid? merchantId, int? page, int? pageSize, CodService service, HttpContext httpContext) =>
            Results.Ok(await service.ListCollectionsAsync(status, branchId, merchantId, page, pageSize, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("cod.read");
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter ListCollections_Paginates_ReturnsDisjointPagesInNewestFirstOrder`
Expected: PASS.

- [ ] **Step 6: Run the full Api.Tests suite to catch regressions**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Cod/Application/CodService.Reads.cs src/Modules/Cod/Presentation/CodEndpoints.cs tests/Api.Tests/CodEndpointsTests.cs
git commit -m "feat(cod): paginate GET /api/v1/cod/collections"
```

---

### Task 5: Paginate `GET /api/v1/ndrs`

**Context:** `NDR` has no branch column of its own (`NdrConfiguration.cs`); a branch-restricted caller's scope check requires a per-row shipment lookup that can't be pushed into SQL without giving the Ndr module EF-model access to Shipment — a module boundary this codebase has already deliberately drawn (Phase 9). So `AllBranches`-scope callers (`GetAccessibleBranchIdsAsync` returns `null`) get true SQL-level pagination; branch-restricted callers keep today's in-memory branch filter, now followed by in-memory paging. This is a known, documented limitation (spec §3, §6), not a bug to work around in this task.

**Files:**
- Modify: `src/Modules/Ndr/Application/NdrService.Reads.cs`
- Modify: `src/Modules/Ndr/Presentation/NdrEndpoints.cs:19-21`
- Test: `tests/Api.Tests/NdrEndpointsTests.cs`

**Interfaces:**
- Consumes: `PagedResult<T>`, `PaginationDefaults.Normalize` (Task 1); `NdrResponse.CreatedAt` (Task 2).
- Produces: `NdrService.ListAsync(NdrStatus? status, int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken)` returning `Task<PagedResult<NdrResponse>>` (was `Task<List<NdrResponse>>`).

- [ ] **Step 1: Write the failing test**

Add to `tests/Api.Tests/NdrEndpointsTests.cs` (add `using Courier.SharedKernel;` to the file's `using` block, which currently has none):

```csharp
    [Fact]
    public async Task List_Paginates_ReturnsDisjointPagesInNewestFirstOrder()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "ndr-list-page");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var failed = new List<ShipmentResponse>();
        for (var i = 0; i < 3; i++)
        {
            var shipment = await BookAndFailDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);
            failed.Add(shipment);
        }

        var page1 = await (await client.GetAsync("/api/v1/ndrs?page=1&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<NdrResponse>>();
        var page2 = await (await client.GetAsync("/api/v1/ndrs?page=2&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<NdrResponse>>();

        Assert.Equal(3, page1!.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2!.Items);

        var page1ShipmentIds = page1.Items.Select(n => n.ShipmentId).ToHashSet();
        var page2ShipmentIds = page2.Items.Select(n => n.ShipmentId).ToHashSet();
        Assert.Empty(page1ShipmentIds.Intersect(page2ShipmentIds));
        Assert.Equal(failed.Select(s => s.Id).ToHashSet(), page1ShipmentIds.Union(page2ShipmentIds));
    }
```

(This test registers as `OrganizationOwner`, an `AllBranches`-scope role, so it exercises the SQL-level pagination path — the in-memory branch-restricted path has no dedicated new test in this task; it is unchanged behavior with paging appended on top, per the documented limitation above.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter List_Paginates_ReturnsDisjointPagesInNewestFirstOrder`
Expected: FAIL — multiple tests across the solution now share this method name; if xUnit's filter is ambiguous, use `--filter "FullyQualifiedName~NdrEndpointsTests.List_Paginates_ReturnsDisjointPagesInNewestFirstOrder"` instead. Either way: FAIL, `page`/`pageSize` ignored, response doesn't deserialize into `PagedResult<NdrResponse>`.

- [ ] **Step 3: Paginate `NdrService.ListAsync`**

In `src/Modules/Ndr/Application/NdrService.Reads.cs`, add `using Courier.SharedKernel;` to the top of the file, then replace the whole `ListAsync` method with:

```csharp
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
            // AllBranches scope: no per-row branch check needed, so pagination happens at the SQL
            // level like every other paginated list in this change.
            var totalCount = await query.CountAsync(cancellationToken);
            var ndrs = await query
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToListAsync(cancellationToken);
            return new PagedResult<NdrResponse>(ndrs.Select(ToResponse).ToList(), normalizedPage, normalizedPageSize, totalCount);
        }

        // Branch-restricted caller: NDR has no branch column of its own (NdrConfiguration.cs), so
        // the branch check requires a per-row shipment lookup that can't be pushed into SQL —
        // same shape ListAsync already used before pagination. Pagination is applied after the
        // in-memory filter for this path only (spec's documented limitation).
        var candidates = await query.ToListAsync(cancellationToken);
        var scoped = new List<NDR>();
        foreach (var ndr in candidates)
        {
            var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(ndr.ShipmentId, cancellationToken);
            if (scanInfo is not null && accessibleBranchIds.Contains(scanInfo.DestinationBranchId))
            {
                scoped.Add(ndr);
            }
        }

        var pagedScoped = scoped.Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize).ToList();
        return new PagedResult<NdrResponse>(pagedScoped.Select(ToResponse).ToList(), normalizedPage, normalizedPageSize, scoped.Count);
    }
```

- [ ] **Step 4: Wire the endpoint**

In `src/Modules/Ndr/Presentation/NdrEndpoints.cs`, replace:

```csharp
        group.MapGet("/", async (NdrStatus? status, NdrService service, HttpContext httpContext) =>
            Results.Ok(await service.ListAsync(status, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("ndr.read");
```

with:

```csharp
        group.MapGet("/", async (NdrStatus? status, int? page, int? pageSize, NdrService service, HttpContext httpContext) =>
            Results.Ok(await service.ListAsync(status, page, pageSize, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("ndr.read");
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~NdrEndpointsTests.List_Paginates_ReturnsDisjointPagesInNewestFirstOrder"`
Expected: PASS.

- [ ] **Step 6: Run the full Api.Tests suite to catch regressions**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: all PASS — in particular `FullRtoPath_FailedAttemptToRtoDelivered_MovesThroughEveryStatusViaManifests`, which calls `GetOpenNdrForShipmentAsync` (itself calling `GET /api/v1/ndrs` and deserializing into `List<NdrResponse>` — this must be updated too).

In `tests/Api.Tests/NdrEndpointsTests.cs`, update `GetOpenNdrForShipmentAsync` from:

```csharp
    private async Task<NdrResponse> GetOpenNdrForShipmentAsync(HttpClient client, Guid shipmentId)
    {
        var ndrs = await (await client.GetAsync("/api/v1/ndrs")).Content.ReadFromJsonAsync<List<NdrResponse>>();
        return ndrs!.Single(n => n.ShipmentId == shipmentId);
    }
```

to:

```csharp
    private async Task<NdrResponse> GetOpenNdrForShipmentAsync(HttpClient client, Guid shipmentId)
    {
        var ndrs = await (await client.GetAsync("/api/v1/ndrs?pageSize=200")).Content.ReadFromJsonAsync<PagedResult<NdrResponse>>();
        return ndrs!.Items.Single(n => n.ShipmentId == shipmentId);
    }
```

Then re-run: `dotnet test tests/Api.Tests/Api.Tests.csproj`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Ndr/Application/NdrService.Reads.cs src/Modules/Ndr/Presentation/NdrEndpoints.cs tests/Api.Tests/NdrEndpointsTests.cs
git commit -m "feat(ndr): paginate GET /api/v1/ndrs"
```

---

### Task 6: Stream the two CSV export endpoints

**Context (spec §4):** `ReportExportService`'s two export methods already fully materialize their row list before building the CSV — that's required by the existing name-batch-resolution pattern (collect every distinct branch/merchant id across all rows, then resolve names in two more batched calls) and isn't being redesigned here. What changes is the last step: instead of `CsvWriter.Write` building the *entire formatted CSV* as one `StringBuilder`/`string`, `CsvWriter.WriteLines` yields it line-by-line and the endpoint streams those lines straight to the HTTP response.

**Files:**
- Modify: `src/Modules/Reports/Infrastructure/CsvWriter.cs`
- Modify: `src/Modules/Reports/Application/ReportExportService.cs`
- Modify: `src/Modules/Reports/Presentation/ReportsEndpoints.cs`
- Test: `tests/Reports.Tests/CsvWriterTests.cs`

**Interfaces:**
- Produces: `CsvWriter.WriteLines(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows) : IEnumerable<string>` — yields the header line first, then one CRLF-terminated line per row. `CsvWriter.Write` is untouched.
- Produces: `ReportExportService.GetShipmentsCsvAsync`/`GetCodSummaryCsvAsync` now return `Task<IEnumerable<string>>` (was `Task<string>`).

- [ ] **Step 1: Write the failing test**

Add to `tests/Reports.Tests/CsvWriterTests.cs`:

```csharp
    [Fact]
    public void WriteLines_MatchesWrite_ForSameContent()
    {
        var headers = new[] { "Name", "Note" };
        var rows = new[] { (IReadOnlyList<string?>)new[] { "Acme, Inc.", "Said \"hello\"\nagain" } };

        var expected = CsvWriter.Write(headers, rows);
        var actual = string.Concat(CsvWriter.WriteLines(headers, rows));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WriteLines_YieldsOneLinePerRowPlusHeader()
    {
        var headers = new[] { "Awb" };
        var rows = new[]
        {
            (IReadOnlyList<string?>)new[] { "AWB1" },
            (IReadOnlyList<string?>)new[] { "AWB2" },
        };

        var lines = CsvWriter.WriteLines(headers, rows).ToList();

        Assert.Equal(3, lines.Count);
        Assert.Equal("Awb\r\n", lines[0]);
        Assert.Equal("AWB1\r\n", lines[1]);
        Assert.Equal("AWB2\r\n", lines[2]);
    }

    [Theory]
    [InlineData("=cmd|'/c calc'!A1")]
    [InlineData("+1+1")]
    public void WriteLines_FieldStartingWithFormulaCharacter_IsPrefixedWithSingleQuote(string maliciousField)
    {
        var lines = CsvWriter.WriteLines(
            new[] { "MerchantName" },
            new[] { (IReadOnlyList<string?>)new[] { maliciousField } }).ToList();

        Assert.Equal("'" + maliciousField + "\r\n", lines[1]);
    }
```

Add `using System.Linq;` to the top of the file if not already implicitly available (the project has `ImplicitUsings` enabled per its `.csproj`, so this is likely unnecessary — check the existing file's top for a `using` block before adding one).

- [ ] **Step 2: Run test to verify it fails to compile**

Run: `dotnet test tests/Reports.Tests/Reports.Tests.csproj`
Expected: FAIL — `CsvWriter.WriteLines` does not exist yet (CS0117).

- [ ] **Step 3: Add `WriteLines` to `CsvWriter`**

In `src/Modules/Reports/Infrastructure/CsvWriter.cs`, add this method to the class (alongside the existing `Write`, reusing the same private `EscapeField`):

```csharp
    public static IEnumerable<string> WriteLines(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        yield return string.Join(',', headers.Select(EscapeField)) + "\r\n";
        foreach (var row in rows)
        {
            yield return string.Join(',', row.Select(EscapeField)) + "\r\n";
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Reports.Tests/Reports.Tests.csproj`
Expected: PASS (all `CsvWriterTests`, both `Write`'s original 5 and `WriteLines`'s new 4).

- [ ] **Step 5: Switch `ReportExportService` to `WriteLines`**

In `src/Modules/Reports/Application/ReportExportService.cs`, change the `GetShipmentsCsvAsync` signature from:

```csharp
    public async Task<string> GetShipmentsCsvAsync(Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
```

to:

```csharp
    public async Task<IEnumerable<string>> GetShipmentsCsvAsync(Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
```

and its final line from `return CsvWriter.Write(headers, csvRows);` to `return CsvWriter.WriteLines(headers, csvRows);`.

Change the `GetCodSummaryCsvAsync` signature from:

```csharp
    public async Task<string> GetCodSummaryCsvAsync(Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
```

to:

```csharp
    public async Task<IEnumerable<string>> GetCodSummaryCsvAsync(Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
```

and its final line from `return CsvWriter.Write(headers, csvRows);` to `return CsvWriter.WriteLines(headers, csvRows);`.

- [ ] **Step 6: Stream the response in `ReportsEndpoints.cs`**

In `src/Modules/Reports/Presentation/ReportsEndpoints.cs`, replace the two export route registrations:

```csharp
        group.MapGet("/export/shipments", async (Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ReportExportService service, HttpContext httpContext) =>
            await HandleCsvAsync(() => service.GetShipmentsCsvAsync(branchId, from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.export");

        group.MapGet("/export/cod-summary", async (Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ReportExportService service, HttpContext httpContext) =>
            await HandleCsvAsync(() => service.GetCodSummaryCsvAsync(branchId, from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.export");
```

with:

```csharp
        group.MapGet("/export/shipments", async (Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ReportExportService service, HttpContext httpContext) =>
            await HandleCsvAsync(httpContext, () => service.GetShipmentsCsvAsync(branchId, from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.export");

        group.MapGet("/export/cod-summary", async (Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ReportExportService service, HttpContext httpContext) =>
            await HandleCsvAsync(httpContext, () => service.GetCodSummaryCsvAsync(branchId, from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.export");
```

Then replace the `HandleCsvAsync` helper:

```csharp
    private static async Task<IResult> HandleCsvAsync(Func<Task<string>> operation)
    {
        try
        {
            var csv = await operation();
            return Results.Text(csv, "text/csv");
        }
        catch (ReportsValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "REPORTS_VALIDATION_FAILED", message = ex.Message } });
        }
        catch (InvalidDateRangeException ex)
        {
            return Results.BadRequest(new { error = new { code = "INVALID_DATE_RANGE", message = ex.Message } });
        }
    }
```

with:

```csharp
    private static async Task<IResult> HandleCsvAsync(HttpContext httpContext, Func<Task<IEnumerable<string>>> operation)
    {
        IEnumerable<string> lines;
        try
        {
            lines = await operation();
        }
        catch (ReportsValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "REPORTS_VALIDATION_FAILED", message = ex.Message } });
        }
        catch (InvalidDateRangeException ex)
        {
            return Results.BadRequest(new { error = new { code = "INVALID_DATE_RANGE", message = ex.Message } });
        }

        httpContext.Response.ContentType = "text/csv";
        await using var writer = new StreamWriter(httpContext.Response.Body, leaveOpen: true);
        foreach (var line in lines)
        {
            await writer.WriteAsync(line);
        }
        await writer.FlushAsync();
        return Results.Empty;
    }
```

Add `using System.Text;` and `using System.Collections.Generic;` to the top of the file only if the build in the next step reports them missing (the project's `ImplicitUsings` setting likely already covers both).

- [ ] **Step 7: Run the full build**

Run: `dotnet build`
Expected: build succeeds with no errors.

- [ ] **Step 8: Run Reports.Tests and the Api.Tests export tests to confirm no regressions**

Run: `dotnet test tests/Reports.Tests/Reports.Tests.csproj`
Expected: all PASS.

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~ReportsEndpointsTests"`
Expected: all PASS — in particular `ExportShipments_ReturnsCsvWithHeaderRow`, `ExportCodSummary_ReturnsCsvWithHeaderRow`, and both `BranchFromAnotherOrganization` validation tests, all unchanged and still asserting on the HTTP response body.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Reports/Infrastructure/CsvWriter.cs src/Modules/Reports/Application/ReportExportService.cs src/Modules/Reports/Presentation/ReportsEndpoints.cs tests/Reports.Tests/CsvWriterTests.cs
git commit -m "perf(reports): stream CSV exports instead of buffering the whole file in memory"
```

---

### Task 7: Full regression pass

**Files:** none (verification only).

- [ ] **Step 1: Full solution build**

Run: `dotnet build`
Expected: build succeeds, zero errors, zero new warnings.

- [ ] **Step 2: Run every test project touched by this plan**

Confirm the shared Postgres container is running: `docker exec courier-postgres-1 pg_isready -U courier` (start with `docker start courier-postgres-1` if needed).

Run each of:
- `dotnet test tests/SharedKernel.Tests/SharedKernel.Tests.csproj`
- `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj`
- `dotnet test tests/Cod.Tests/Cod.Tests.csproj`
- `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj`
- `dotnet test tests/Riders.Tests/Riders.Tests.csproj`
- `dotnet test tests/Reports.Tests/Reports.Tests.csproj`
- `dotnet test tests/Api.Tests/Api.Tests.csproj`

Expected: all PASS, no unrelated failures.

- [ ] **Step 3: Update ROADMAP.md if this repo tracks pagination as a checklist item**

Check `ROADMAP.md` for any Phase 13/14 line item referencing pagination or API performance; if present, mark it done referencing this plan and the spec. If no such line exists, skip this step — this change is not a new roadmap phase (spec's scope note).

- [ ] **Step 4: Final commit (only if Step 3 changed a file)**

```bash
git add ROADMAP.md
git commit -m "docs(roadmap): note list-endpoint pagination + export streaming completed"
```
