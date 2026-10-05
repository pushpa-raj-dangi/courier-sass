# F15 R2 — Staff Operations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the staff Dashboard, Shipments list + detail Sheet, and add a camera-first `/scan` Scan station, backed by a small read-only Shipments API slice (list filters, `lastEventAt`, events endpoint, branch-scoped single reads).

**Architecture:** Backend changes stay inside `src/Modules/Shipments` (Application + Presentation) and reuse the existing tenant filter and `IBranchAuthorizationChecker`. Frontend builds on the R1 primitives (`PageHeader`, `SectionCard`, `KpiTile`, `PipelineStrip`, `ComingSoonSlot`, `SheetHeaderBlock`); pure logic (status groups, URL params, custody chain, stepper, dashboard model, scan reducer, scan modes) lives in `lib/` files with unit tests, components stay thin. The scan station uses a mode registry calling existing write endpoints.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core (Npgsql + InMemory for unit tests), xUnit; Next.js 14 App Router, TypeScript, TanStack Query, Zustand (existing panel stores only), shadcn/ui, Vitest + Testing Library, `@zxing/browser` (already installed).

**Spec:** `docs/superpowers/specs/2026-10-05-f15-r2-staff-operations-design.md`

## Global Constraints

- No migrations, no new write endpoints, no new domain entities, no new npm or NuGet dependencies.
- Data the API does not serve renders `ComingSoonSlot` (`@/components/page/ComingSoonSlot`) — never mock or invented numbers.
- Out-of-scope or other-tenant single-shipment reads return **404**, never 403.
- `GET /{awb}/events` never exposes `Metadata`, `Latitude`, `Longitude`.
- Detail/create views are right-side Sheets driven by the existing Zustand panel stores — never routed pages.
- Use shadcn/ui components from `@/components/ui/*` (Tabs, Select, Popover, Command, Button, Input, Skeleton, Spinner) — no raw hand-built equivalents.
- Ranges/dates sent to the API are UTC-anchored strings (`YYYY-MM-DDT00:00:00Z` / `YYYY-MM-DDT23:59:59Z`), as the existing dashboard does.
- Camera is requested only after an explicit "Start camera" tap.
- Web checks: `npm run build`, `npm run lint`, `npm test` pass; `npx tsc --noEmit` shows no errors beyond the 8 pre-existing ones on master. Backend: `dotnet build` and `dotnet test` pass (Postgres: shared `courier-postgres-1` on port 5434 — do **not** `docker compose up` a second one).
- Never assert on ICU month text in tests (this Node renders en-GB "Sept"); compute expected strings with the same formatter.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` **verbatim** (do not substitute your own model name).
- Run web commands from `src/web`; run dotnet commands from the repo root.

## Review Focus

1. A Branch Manager scoped to one branch types another branch's AWB into the Sheet/scan station → must see "not found", and the events endpoint must 404 too (pinned in Task 2).
2. Search with a single character, whitespace only, or `%`/`_` characters → 1 char is a 400 from the API but the UI never sends it; whitespace is ignored; `%`/`_` match literally (pinned in Task 1 and Task 4).
3. A held barcode in front of the camera (same code every frame) → exactly one write request; a second, different code scanned while the first is in flight is processed after it, not dropped and not parallel (pinned in Tasks 9 and 10).
4. One dashboard endpoint failing (e.g. no `ndr.read` → 403 on `/ndrs/summary`) → the rest of the dashboard still renders and the Needs-action row is hidden, not an error (pinned in Task 8).
5. A shipment in NDR/RTO or an international status opened in the Sheet → stepper shows the exception marker, never a fake "Delivered" step; international statuses appear under "All" only (pinned in Tasks 3 and 5).

---

## File Structure

**Backend (modify):**
- `src/Modules/Shipments/Application/ShipmentDtos.cs` — `ShipmentListFilter`, `ShipmentEventResponse`, `LastEventAt` on `ShipmentResponse`.
- `src/Modules/Shipments/Application/ShipmentService.Reads.cs` — filtered list, batched items/last-event, branch-scoped `GetByAwbAsync`, new `GetEventsByAwbAsync`.
- `src/Modules/Shipments/Application/ShipmentService.cs` — `ToResponse`/`LoadResponseAsync` carry `LastEventAt`.
- `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs` — list query params, events route.
- Tests: `tests/Shipments.Tests/ShipmentServiceReadTests.cs`, `tests/Shipments.Tests/ShipmentListFilterTests.cs` (new), `tests/Api.Tests/ShipmentsEndpointsTests.cs`.

**Frontend (create unless noted):**
- `src/web/src/lib/dates/rangePresets.ts` — shared Today/7d/30d/custom → day strings → UTC range.
- `src/web/src/features/shipments/lib/statusGroups.ts` — tab ↔ statuses, sums.
- `src/web/src/features/shipments/lib/listParams.ts` — URL ⇄ list params ⇄ API query.
- `src/web/src/features/shipments/hooks/useShipments.ts` (modify), `useShipmentEvents.ts`.
- `src/web/src/app/api/shipments/[awb]/events/route.ts`.
- `src/web/src/features/shipments/components/ShipmentsView.tsx`, `ShipmentFilterBar.tsx`; `app/(staff)/shipments/page.tsx` (modify).
- `src/web/src/features/shipments/lib/progress.ts`, `custodyChain.ts`, `eventLabels.ts`; `hooks/usePeopleNames.ts`.
- `src/web/src/features/shipments/components/detail/*` + `ShipmentDetailSheet.tsx` (modify).
- `src/web/src/features/reports/lib/dashboardModel.ts`; `features/reports/components/dashboard/*`; `app/(staff)/dashboard/page.tsx` (modify).
- `src/web/src/components/scanner/BarcodeScanner.tsx` (moved from `features/rider-app/components/`).
- `src/web/src/features/scanning/*` (reducer, modes, errors, beep, components); `app/(staff)/scan/page.tsx`.
- `src/web/src/lib/navigation/navItems.ts`, `src/web/messages/en.json`, `src/web/messages/ne.json` (modify).

---

### Task 1: Shipment list filters, `lastEventAt`, batched list loading (backend)

**Files:**
- Modify: `src/Modules/Shipments/Application/ShipmentDtos.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentService.cs:336-353`
- Modify: `src/Modules/Shipments/Application/ShipmentService.Reads.cs:25-70`
- Modify: `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs:89-97`
- Create: `tests/Shipments.Tests/ShipmentListFilterTests.cs`
- Modify: `tests/Shipments.Tests/ShipmentServiceReadTests.cs`
- Modify: `tests/Api.Tests/ShipmentsEndpointsTests.cs`

**Interfaces:**
- Produces: `record ShipmentListFilter(IReadOnlyList<ShipmentStatus>? Statuses, string? Query, Guid? MerchantId, Guid? ServiceTypeId, Guid? BranchId, DateTimeOffset? CreatedFrom, DateTimeOffset? CreatedTo)` with `static ShipmentListFilter Parse(string[]? status, string? q, Guid? merchantId, Guid? serviceTypeId, Guid? branchId, DateTimeOffset? createdFrom, DateTimeOffset? createdTo)` (throws `ShipmentValidationException`).
- Produces: `ShipmentService.ListAsync(int? page, int? pageSize, ClaimsPrincipal actingUser, ShipmentListFilter? filter = null, CancellationToken cancellationToken = default)`.
- Produces: `ShipmentResponse` gains trailing `DateTimeOffset? LastEventAt = null` (JSON `lastEventAt`).
- Produces: `GET /api/v1/shipments?status=X&status=Y&q=&merchantId=&serviceTypeId=&branchId=&createdFrom=&createdTo=` → 400 `{ error: { code: "SHIPMENT_VALIDATION_FAILED", message } }` on bad input. (The spec's "VALIDATION_ERROR" is replaced by this module's existing code for consistency with every other Shipments 400.)

- [ ] **Step 1: Write the failing filter-parse tests**

Create `tests/Shipments.Tests/ShipmentListFilterTests.cs`:

```csharp
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Xunit;

namespace Courier.Shipments.Tests;

public class ShipmentListFilterTests
{
    [Fact]
    public void Parse_KnownStatuses_ReturnsThem()
    {
        var filter = ShipmentListFilter.Parse(new[] { "InTransit", "Sorted" }, null, null, null, null, null, null);
        Assert.Equal(new[] { ShipmentStatus.InTransit, ShipmentStatus.Sorted }, filter.Statuses);
    }

    [Theory]
    [InlineData("Bogus")]
    [InlineData("3")]
    [InlineData("intransit")]
    public void Parse_UnknownOrNumericOrWrongCaseStatus_Throws(string raw)
    {
        Assert.Throws<ShipmentValidationException>(() => ShipmentListFilter.Parse(new[] { raw }, null, null, null, null, null, null));
    }

    [Fact]
    public void Parse_OneCharQuery_Throws()
    {
        Assert.Throws<ShipmentValidationException>(() => ShipmentListFilter.Parse(null, " a ", null, null, null, null, null));
    }

    [Fact]
    public void Parse_WhitespaceQuery_IsIgnored()
    {
        var filter = ShipmentListFilter.Parse(null, "   ", null, null, null, null, null);
        Assert.Null(filter.Query);
    }

    [Fact]
    public void Parse_QueryIsTrimmed()
    {
        var filter = ShipmentListFilter.Parse(null, "  sita ", null, null, null, null, null);
        Assert.Equal("sita", filter.Query);
    }

    [Fact]
    public void Parse_FromAfterTo_Throws()
    {
        var to = DateTimeOffset.UtcNow;
        Assert.Throws<ShipmentValidationException>(() => ShipmentListFilter.Parse(null, null, null, null, null, to.AddDays(1), to));
    }

    [Fact]
    public void Parse_NoInput_IsEmpty()
    {
        var filter = ShipmentListFilter.Parse(null, null, null, null, null, null, null);
        Assert.True(filter.IsEmpty);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Shipments.Tests --filter "FullyQualifiedName~ShipmentListFilterTests"`
Expected: build FAIL — `ShipmentListFilter` does not exist.

- [ ] **Step 3: Add `ShipmentListFilter` and `LastEventAt`**

In `src/Modules/Shipments/Application/ShipmentDtos.cs`, change the `ShipmentResponse` record's last line from `DateTimeOffset CreatedAt);` to:

```csharp
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastEventAt = null);
```

Append to the same file (after `ShipmentResponse`):

```csharp
// Optional, AND-combined narrowing for GET /api/v1/shipments (F15 R2). Applied AFTER the
// tenant filter and the caller's merchant pin / staff branch scope, so it can only ever narrow.
public record ShipmentListFilter(
    IReadOnlyList<ShipmentStatus>? Statuses,
    string? Query,
    Guid? MerchantId,
    Guid? ServiceTypeId,
    Guid? BranchId,
    DateTimeOffset? CreatedFrom,
    DateTimeOffset? CreatedTo)
{
    public bool IsEmpty =>
        (Statuses is null || Statuses.Count == 0) && Query is null && MerchantId is null &&
        ServiceTypeId is null && BranchId is null && CreatedFrom is null && CreatedTo is null;

    public static ShipmentListFilter Parse(
        string[]? status, string? q, Guid? merchantId, Guid? serviceTypeId, Guid? branchId,
        DateTimeOffset? createdFrom, DateTimeOffset? createdTo)
    {
        List<ShipmentStatus>? statuses = null;
        if (status is { Length: > 0 })
        {
            statuses = new List<ShipmentStatus>();
            foreach (var raw in status)
            {
                // Exact enum member names only — Enum.TryParse would otherwise accept numeric
                // strings ("3") and, with ignoreCase, alternate casings the frontend never sends.
                if (string.IsNullOrEmpty(raw) || !raw.All(char.IsLetter) ||
                    !Enum.TryParse<ShipmentStatus>(raw, ignoreCase: false, out var parsed) ||
                    parsed.ToString() != raw)
                {
                    throw new ShipmentValidationException($"Unknown shipment status '{raw}'.");
                }
                statuses.Add(parsed);
            }
        }

        var query = q?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            query = null;
        }
        else if (query.Length < 2)
        {
            throw new ShipmentValidationException("Search must be at least 2 characters.");
        }

        if (createdFrom is not null && createdTo is not null && createdFrom > createdTo)
        {
            throw new ShipmentValidationException("createdFrom must not be after createdTo.");
        }

        return new ShipmentListFilter(statuses, query, merchantId, serviceTypeId, branchId, createdFrom, createdTo);
    }
}
```

- [ ] **Step 4: Run the parse tests**

Run: `dotnet test tests/Shipments.Tests --filter "FullyQualifiedName~ShipmentListFilterTests"`
Expected: PASS (7 tests; the Theory counts 3).

- [ ] **Step 5: Write failing service tests for filtering, batching and `LastEventAt`**

Append these tests inside `ShipmentServiceReadTests` in `tests/Shipments.Tests/ShipmentServiceReadTests.cs` (add `using Courier.Shipments.Domain;` at the top if absent):

```csharp
    private static async Task MutateAsync(TestDbContext dbContext, Guid shipmentId, Action<Shipment> change)
    {
        var entity = await dbContext.Set<Shipment>().SingleAsync(s => s.Id == shipmentId);
        change(entity);
        await dbContext.SaveChangesAsync();
    }

    private static ShipmentListFilter Filter(
        IReadOnlyList<ShipmentStatus>? statuses = null, string? query = null, Guid? merchantId = null,
        Guid? serviceTypeId = null, Guid? branchId = null, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        new(statuses, query, merchantId, serviceTypeId, branchId, from, to);

    [Fact]
    public async Task ListAsync_StatusFilter_ReturnsOnlyMatchingStatuses()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var a = await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        var b = await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        await MutateAsync(dbContext, b.Id, s => s.CurrentStatus = ShipmentStatus.InTransit);
        var service = CreateService(dbContext, organizationId, new());

        var result = await service.ListAsync(null, null, StaffUser(Guid.NewGuid()), Filter(statuses: new[] { ShipmentStatus.InTransit, ShipmentStatus.Sorted }));

        Assert.Equal(new[] { b.Id }, result.Items.Select(s => s.Id));
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task ListAsync_Query_MatchesAwbPrefixCaseInsensitive_RecipientNameAndPhone()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var byAwb = await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        var byName = await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        var byPhone = await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        await MutateAsync(dbContext, byAwb.Id, s => s.Awb = "ZQ-260930-00001");
        await MutateAsync(dbContext, byName.Id, s => s.RecipientName = "Sita Shrestha");
        await MutateAsync(dbContext, byPhone.Id, s => s.RecipientPhone = "9841555777");
        var service = CreateService(dbContext, organizationId, new());
        var user = StaffUser(Guid.NewGuid());

        Assert.Equal(new[] { byAwb.Id }, (await service.ListAsync(null, null, user, Filter(query: "zq-2609"))).Items.Select(s => s.Id));
        Assert.Equal(new[] { byName.Id }, (await service.ListAsync(null, null, user, Filter(query: "SHRESTHA"))).Items.Select(s => s.Id));
        Assert.Equal(new[] { byPhone.Id }, (await service.ListAsync(null, null, user, Filter(query: "555777"))).Items.Select(s => s.Id));
    }

    [Fact]
    public async Task ListAsync_Query_TreatsPercentAndUnderscoreLiterally()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        var service = CreateService(dbContext, organizationId, new());

        var result = await service.ListAsync(null, null, StaffUser(Guid.NewGuid()), Filter(query: "%_"));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ListAsync_MerchantServiceTypeBranchAndDateFilters_EachNarrow()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var a = await SeedShipmentForMerchantAsync(dbContext, organizationId, merchantId);
        var b = await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        var otherServiceType = Guid.NewGuid();
        await MutateAsync(dbContext, b.Id, s => { s.ServiceTypeId = otherServiceType; s.CreatedAt = DateTimeOffset.UtcNow.AddDays(-10); });
        var service = CreateService(dbContext, organizationId, new());
        var user = StaffUser(Guid.NewGuid());

        Assert.Equal(new[] { a.Id }, (await service.ListAsync(null, null, user, Filter(merchantId: merchantId))).Items.Select(s => s.Id));
        Assert.Equal(new[] { b.Id }, (await service.ListAsync(null, null, user, Filter(serviceTypeId: otherServiceType))).Items.Select(s => s.Id));
        Assert.Equal(new[] { a.Id }, (await service.ListAsync(null, null, user, Filter(branchId: a.OriginBranchId))).Items.Select(s => s.Id));
        Assert.Equal(new[] { b.Id }, (await service.ListAsync(null, null, user, Filter(branchId: b.DestinationBranchId))).Items.Select(s => s.Id));
        Assert.Equal(new[] { a.Id }, (await service.ListAsync(null, null, user, Filter(from: DateTimeOffset.UtcNow.AddDays(-1)))).Items.Select(s => s.Id));
        Assert.Equal(new[] { b.Id }, (await service.ListAsync(null, null, user, Filter(to: DateTimeOffset.UtcNow.AddDays(-5)))).Items.Select(s => s.Id));
    }

    [Fact]
    public async Task ListAsync_CombinedFilters_AreAndedTogether()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var a = await SeedShipmentForMerchantAsync(dbContext, organizationId, merchantId);
        await SeedShipmentForMerchantAsync(dbContext, organizationId, merchantId);
        await MutateAsync(dbContext, a.Id, s => s.CurrentStatus = ShipmentStatus.InTransit);
        var service = CreateService(dbContext, organizationId, new());

        var result = await service.ListAsync(null, null, StaffUser(Guid.NewGuid()),
            Filter(statuses: new[] { ShipmentStatus.InTransit }, merchantId: merchantId));

        Assert.Equal(new[] { a.Id }, result.Items.Select(s => s.Id));
    }

    [Fact]
    public async Task ListAsync_BranchFilterOutsideCallerScope_ReturnsEmptyPage()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var inScope = await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        var outOfScope = await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        var restricted = new RestrictedBranchesAuthorizationChecker(new HashSet<Guid> { inScope.OriginBranchId });
        var service = CreateService(dbContext, organizationId, new(), restricted);

        var result = await service.ListAsync(null, null, StaffUser(Guid.NewGuid()), Filter(branchId: outOfScope.OriginBranchId));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task ListAsync_MerchantCallerFilteringByAnotherMerchant_ReturnsEmptyPage()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        await SeedShipmentForMerchantAsync(dbContext, organizationId, merchantId);
        await SeedShipmentForMerchantAsync(dbContext, organizationId, otherMerchantId);
        var service = CreateService(dbContext, organizationId, new());

        var result = await service.ListAsync(null, null, MerchantUser(merchantId), Filter(merchantId: otherMerchantId));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ListAsync_PopulatesItemsAndLastEventAt()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var a = await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        var latest = DateTimeOffset.UtcNow.AddHours(3);
        dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
        {
            Id = Guid.NewGuid(), ShipmentId = a.Id, EventType = ShipmentEventType.ReceivedAtOrigin,
            PreviousStatus = ShipmentStatus.PickedUp, NewStatus = ShipmentStatus.AtOriginBranch, Timestamp = latest,
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new());

        var listed = (await service.ListAsync(null, null, StaffUser(Guid.NewGuid()))).Items.Single();
        var single = await service.GetByAwbAsync(a.Awb, StaffUser(Guid.NewGuid()));

        Assert.Equal("Item", Assert.Single(listed.Items).Description);
        Assert.Equal(latest, listed.LastEventAt);
        Assert.Equal(latest, single!.LastEventAt);
    }
```

- [ ] **Step 6: Run to verify they fail**

Run: `dotnet test tests/Shipments.Tests --filter "FullyQualifiedName~ShipmentServiceReadTests"`
Expected: build FAIL — no `ListAsync` overload takes a `ShipmentListFilter`.

- [ ] **Step 7: Carry `LastEventAt` through `ToResponse`/`LoadResponseAsync`**

In `src/Modules/Shipments/Application/ShipmentService.cs` replace `LoadResponseAsync` and `ToResponse` (lines 336-353) with:

```csharp
    private async Task<ShipmentResponse> LoadResponseAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstAsync(s => s.Id == shipmentId, cancellationToken);
        var items = await _dbContext.Set<ShipmentItem>().Where(i => i.ShipmentId == shipmentId).ToListAsync(cancellationToken);
        var lastEventAt = await _dbContext.Set<ShipmentEvent>()
            .Where(e => e.ShipmentId == shipmentId)
            .MaxAsync(e => (DateTimeOffset?)e.Timestamp, cancellationToken);
        return ToResponse(shipment, items, lastEventAt);
    }

    private static ShipmentResponse ToResponse(Shipment shipment, List<ShipmentItem> items, DateTimeOffset? lastEventAt = null) => new(
        shipment.Id, shipment.Awb, shipment.MerchantId, shipment.OriginBranchId, shipment.DestinationBranchId,
        shipment.CurrentBranchId, shipment.CurrentCustodianUserId, shipment.CurrentCustodianRiderId, shipment.CurrentStatus, shipment.ServiceTypeId,
        shipment.WeightKg, shipment.LengthCm, shipment.WidthCm, shipment.HeightCm,
        shipment.DeclaredValue, shipment.CodAmount, shipment.WantsInsurance,
        shipment.DeliveryCharge, shipment.InsuranceCharge, shipment.OtherCharges, shipment.TotalCharge,
        shipment.RecipientName, shipment.RecipientPhone, shipment.RecipientAddressLine,
        shipment.SenderName, shipment.SenderPhone, shipment.SenderAddressLine,
        items.Select(i => new ShipmentItemResponse(i.Id, i.Description, i.Quantity, i.UnitValue)).ToList(),
        shipment.CreatedAt,
        lastEventAt);
```

- [ ] **Step 8: Rewrite `ListAsync` with filters and batched loading**

In `src/Modules/Shipments/Application/ShipmentService.Reads.cs` replace the whole `ListAsync` method with:

```csharp
    public async Task<PagedResult<ShipmentResponse>> ListAsync(int? page, int? pageSize, ClaimsPrincipal actingUser, ShipmentListFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Shipment>().AsQueryable();
        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            query = query.Where(s => s.MerchantId == callerMerchantId);
        }
        else
        {
            // Staff caller: scope to branches the caller can access for shipment.read. null means
            // "no filter applies" (BranchScope.AllBranches) per IBranchAuthorizationChecker's
            // documented contract — a non-null set restricts to shipments touching any of those
            // branches as origin, destination, or current physical location.
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

        if (filter is not null)
        {
            query = ApplyFilter(query, filter);
        }

        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var shipments = await query
            .OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        // One query each for the page's items and latest event times (replaces a per-row items
        // query). Output is identical to the former per-row loading.
        var shipmentIds = shipments.Select(s => s.Id).ToArray();
        var itemsByShipment = (await _dbContext.Set<ShipmentItem>()
                .Where(i => shipmentIds.Contains(i.ShipmentId))
                .ToListAsync(cancellationToken))
            .GroupBy(i => i.ShipmentId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var lastEventByShipment = await _dbContext.Set<ShipmentEvent>()
            .Where(e => shipmentIds.Contains(e.ShipmentId))
            .GroupBy(e => e.ShipmentId)
            .Select(g => new { ShipmentId = g.Key, Last = g.Max(e => e.Timestamp) })
            .ToDictionaryAsync(x => x.ShipmentId, x => x.Last, cancellationToken);

        var responses = shipments
            .Select(s => ToResponse(
                s,
                itemsByShipment.TryGetValue(s.Id, out var items) ? items : new List<ShipmentItem>(),
                lastEventByShipment.TryGetValue(s.Id, out var last) ? last : null))
            .ToList();
        return new PagedResult<ShipmentResponse>(responses, normalizedPage, normalizedPageSize, totalCount);
    }

    private static IQueryable<Shipment> ApplyFilter(IQueryable<Shipment> query, ShipmentListFilter filter)
    {
        if (filter.Statuses is { Count: > 0 })
        {
            var statuses = filter.Statuses.ToArray();
            query = query.Where(s => statuses.Contains(s.CurrentStatus));
        }
        if (filter.Query is string raw)
        {
            // StartsWith/Contains translate to LIKE with EF's own escaping of % and _, so user
            // input is matched literally on Npgsql; InMemory evaluates them as plain string ops.
            var upper = raw.ToUpperInvariant();
            var lower = raw.ToLowerInvariant();
            query = query.Where(s =>
                s.Awb.ToUpper().StartsWith(upper) ||
                s.RecipientName.ToLower().Contains(lower) ||
                s.RecipientPhone.Contains(raw));
        }
        if (filter.MerchantId is Guid merchantId)
        {
            query = query.Where(s => s.MerchantId == merchantId);
        }
        if (filter.ServiceTypeId is Guid serviceTypeId)
        {
            query = query.Where(s => s.ServiceTypeId == serviceTypeId);
        }
        if (filter.BranchId is Guid branchId)
        {
            query = query.Where(s => s.OriginBranchId == branchId || s.DestinationBranchId == branchId || s.CurrentBranchId == branchId);
        }
        if (filter.CreatedFrom is DateTimeOffset from)
        {
            query = query.Where(s => s.CreatedAt >= from);
        }
        if (filter.CreatedTo is DateTimeOffset to)
        {
            query = query.Where(s => s.CreatedAt <= to);
        }
        return query;
    }
```

- [ ] **Step 9: Run service tests**

Run: `dotnet test tests/Shipments.Tests --filter "FullyQualifiedName~ShipmentServiceReadTests|FullyQualifiedName~ShipmentListFilterTests"`
Expected: PASS (all, including the pre-existing list tests).

- [ ] **Step 10: Write failing API validation tests**

Append to `ShipmentsEndpointsTests` in `tests/Api.Tests/ShipmentsEndpointsTests.cs`:

```csharp
    [Theory]
    [InlineData("/api/v1/shipments?status=Bogus")]
    [InlineData("/api/v1/shipments?q=a")]
    [InlineData("/api/v1/shipments?createdFrom=2026-10-05T00:00:00Z&createdTo=2026-10-01T00:00:00Z")]
    public async Task List_InvalidFilter_Returns400WithValidationCode(string url)
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "list-bad-filter");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SHIPMENT_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task List_RepeatedStatusParam_FiltersAndReturnsLastEventAt()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "list-status");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(client);
        await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));

        var created = await (await client.GetAsync("/api/v1/shipments?status=Created&status=PickupAssigned")).Content.ReadFromJsonAsync<PagedResult<ShipmentResponse>>();
        var inTransit = await (await client.GetAsync("/api/v1/shipments?status=InTransit")).Content.ReadFromJsonAsync<PagedResult<ShipmentResponse>>();

        Assert.Single(created!.Items);
        Assert.NotNull(created.Items[0].LastEventAt);
        Assert.Empty(inTransit!.Items);
    }
```

- [ ] **Step 11: Run to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~ShipmentsEndpointsTests.List_"`
Expected: FAIL — invalid filters currently return 200.

- [ ] **Step 12: Bind filters on the endpoint**

In `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs` replace the `group.MapGet("/", ...)` block with:

```csharp
        group.MapGet("/", async (int? page, int? pageSize, string[]? status, string? q, Guid? merchantId, Guid? serviceTypeId, Guid? branchId, DateTimeOffset? createdFrom, DateTimeOffset? createdTo, ShipmentService service, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (!await HasReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker))
            {
                return httpContext.User.GetMerchantId() is null && httpContext.User.GetUserId() is null ? Results.Unauthorized() : Results.Forbid();
            }

            ShipmentListFilter filter;
            try
            {
                filter = ShipmentListFilter.Parse(status, q, merchantId, serviceTypeId, branchId, createdFrom, createdTo);
            }
            catch (ShipmentValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "SHIPMENT_VALIDATION_FAILED", message = ex.Message } });
            }

            return Results.Ok(await service.ListAsync(page, pageSize, httpContext.User, filter, httpContext.RequestAborted));
        });
```

- [ ] **Step 13: Run all Shipments + API shipment tests**

Run: `dotnet test tests/Shipments.Tests` then `dotnet test tests/Api.Tests --filter "FullyQualifiedName~Shipments"`
Expected: PASS. Then `dotnet build` at repo root: 0 errors (any other `ListAsync` caller that passed a `CancellationToken` positionally as the 4th argument must be switched to `cancellationToken:` — `grep -rn "ListAsync(page, pageSize" src` should show only the endpoint).

- [ ] **Step 14: Commit**

```bash
git add src/Modules/Shipments tests/Shipments.Tests tests/Api.Tests/ShipmentsEndpointsTests.cs
git commit -m "feat(shipments): list filters, lastEventAt and batched list loading (F15 R2)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Branch-scoped single reads + `GET /{awb}/events` (backend)

**Files:**
- Modify: `src/Modules/Shipments/Application/ShipmentDtos.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentService.Reads.cs:10-23`
- Modify: `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs`
- Modify: `tests/Shipments.Tests/ShipmentServiceReadTests.cs`
- Modify: `tests/Api.Tests/ShipmentsEndpointsTests.cs`

**Interfaces:**
- Consumes: Task 1's `LastEventAt`.
- Produces: `record ShipmentEventResponse(Guid Id, ShipmentEventType EventType, ShipmentStatus? PreviousStatus, ShipmentStatus NewStatus, Guid? BranchId, Guid? UserId, Guid? RiderId, DateTimeOffset Timestamp, string? Notes)`.
- Produces: `Task<IReadOnlyList<ShipmentEventResponse>?> ShipmentService.GetEventsByAwbAsync(string awb, ClaimsPrincipal actingUser, CancellationToken)` — `null` = not found / out of scope.
- Produces: `GET /api/v1/shipments/{awb}/events` → 200 array (ascending), 403 merchant / missing `shipment.read`, 404 not found or out of scope.

- [ ] **Step 1: Write failing service tests**

Append to `ShipmentServiceReadTests`:

```csharp
    [Fact]
    public async Task GetByAwbAsync_StaffOutsideBranchScope_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipment = await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        var restricted = new RestrictedBranchesAuthorizationChecker(new HashSet<Guid> { Guid.NewGuid() });
        var service = CreateService(dbContext, organizationId, new(), restricted);

        Assert.Null(await service.GetByAwbAsync(shipment.Awb, StaffUser(Guid.NewGuid())));
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("destination")]
    [InlineData("current")]
    public async Task GetByAwbAsync_StaffScopedToOriginDestinationOrCurrent_ReturnsShipment(string which)
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipment = await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        var currentBranchId = Guid.NewGuid();
        await MutateAsync(dbContext, shipment.Id, s => s.CurrentBranchId = currentBranchId);
        var branchId = which switch { "origin" => shipment.OriginBranchId, "destination" => shipment.DestinationBranchId, _ => currentBranchId };
        var service = CreateService(dbContext, organizationId, new(), new RestrictedBranchesAuthorizationChecker(new HashSet<Guid> { branchId }));

        Assert.NotNull(await service.GetByAwbAsync(shipment.Awb, StaffUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task GetByAwbAsync_MerchantCaller_IsNotBranchScoped()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var shipment = await SeedShipmentForMerchantAsync(dbContext, organizationId, merchantId);
        var service = CreateService(dbContext, organizationId, new(), new RestrictedBranchesAuthorizationChecker(new HashSet<Guid>()));

        Assert.NotNull(await service.GetByAwbAsync(shipment.Awb, MerchantUser(merchantId)));
    }

    [Fact]
    public async Task GetEventsByAwbAsync_ReturnsEventsAscendingWithoutInternalFields()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipment = await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        var later = DateTimeOffset.UtcNow.AddHours(2);
        dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
        {
            Id = Guid.NewGuid(), ShipmentId = shipment.Id, EventType = ShipmentEventType.ReceivedAtOrigin,
            PreviousStatus = ShipmentStatus.PickedUp, NewStatus = ShipmentStatus.AtOriginBranch,
            BranchId = shipment.OriginBranchId, Timestamp = later, Notes = "dock 2",
            Latitude = 27.7m, Longitude = 85.3m, Metadata = "{\"secret\":1}",
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new());

        var events = await service.GetEventsByAwbAsync(shipment.Awb, StaffUser(Guid.NewGuid()));

        Assert.NotNull(events);
        Assert.True(events!.Count >= 2);
        Assert.Equal(events.OrderBy(e => e.Timestamp).ThenBy(e => e.Id).Select(e => e.Id), events.Select(e => e.Id));
        var last = events[^1];
        Assert.Equal(ShipmentEventType.ReceivedAtOrigin, last.EventType);
        Assert.Equal("dock 2", last.Notes);
        Assert.DoesNotContain(typeof(ShipmentEventResponse).GetProperties(), p => p.Name is "Metadata" or "Latitude" or "Longitude");
    }

    [Fact]
    public async Task GetEventsByAwbAsync_OutOfScopeOrUnknown_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var shipment = await SeedShipmentForMerchantAsync(dbContext, organizationId, Guid.NewGuid());
        var restricted = CreateService(dbContext, organizationId, new(), new RestrictedBranchesAuthorizationChecker(new HashSet<Guid> { Guid.NewGuid() }));
        var open = CreateService(dbContext, organizationId, new());

        Assert.Null(await restricted.GetEventsByAwbAsync(shipment.Awb, StaffUser(Guid.NewGuid())));
        Assert.Null(await open.GetEventsByAwbAsync("NOPE-0000", StaffUser(Guid.NewGuid())));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Shipments.Tests --filter "FullyQualifiedName~ShipmentServiceReadTests"`
Expected: build FAIL (`GetEventsByAwbAsync`, `ShipmentEventResponse` missing).

- [ ] **Step 3: Add the DTO**

Append to `src/Modules/Shipments/Application/ShipmentDtos.cs`:

```csharp
// Staff-only event read (F15 R2). Deliberately omits ShipmentEvent.Metadata, Latitude and
// Longitude — internal data that the Timeline/Custody views do not need.
public record ShipmentEventResponse(
    Guid Id,
    ShipmentEventType EventType,
    ShipmentStatus? PreviousStatus,
    ShipmentStatus NewStatus,
    Guid? BranchId,
    Guid? UserId,
    Guid? RiderId,
    DateTimeOffset Timestamp,
    string? Notes);
```

- [ ] **Step 4: Scope `GetByAwbAsync` and add `GetEventsByAwbAsync`**

In `src/Modules/Shipments/Application/ShipmentService.Reads.cs` replace `GetByAwbAsync` with:

```csharp
    public async Task<ShipmentResponse?> GetByAwbAsync(string awb, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await FindReadableByAwbAsync(awb, actingUser, cancellationToken);
        return shipment is null ? null : await LoadResponseAsync(shipment.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<ShipmentEventResponse>?> GetEventsByAwbAsync(string awb, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await FindReadableByAwbAsync(awb, actingUser, cancellationToken);
        if (shipment is null)
        {
            return null;
        }

        // ShipmentEvent carries no OrganizationId; tenant isolation comes from resolving the
        // (tenant-filtered) Shipment first and reading only its events.
        return await _dbContext.Set<ShipmentEvent>()
            .Where(e => e.ShipmentId == shipment.Id)
            .OrderBy(e => e.Timestamp).ThenBy(e => e.Id)
            .Select(e => new ShipmentEventResponse(e.Id, e.EventType, e.PreviousStatus, e.NewStatus, e.BranchId, e.UserId, e.RiderId, e.Timestamp, e.Notes))
            .ToListAsync(cancellationToken);
    }

    // Single-shipment read rule (CLARIFICATIONS #25): tenant filter (global), merchant callers pinned
    // to their own shipments, staff callers limited to shipments whose origin, destination or
    // current branch is in their shipment.read scope — the same predicate ListAsync uses. A miss
    // is indistinguishable from "does not exist" so AWB existence is never disclosed.
    private async Task<Shipment?> FindReadableByAwbAsync(string awb, ClaimsPrincipal actingUser, CancellationToken cancellationToken)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Awb == awb, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        if (actingUser.GetMerchantId() is Guid callerMerchantId)
        {
            return shipment.MerchantId == callerMerchantId ? shipment : null;
        }

        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "shipment.read", cancellationToken);
        if (accessibleBranchIds is null)
        {
            return shipment;
        }
        var inScope = accessibleBranchIds.Contains(shipment.OriginBranchId) ||
                      accessibleBranchIds.Contains(shipment.DestinationBranchId) ||
                      (shipment.CurrentBranchId is Guid current && accessibleBranchIds.Contains(current));
        return inScope ? shipment : null;
    }
```

- [ ] **Step 5: Run service tests**

Run: `dotnet test tests/Shipments.Tests`
Expected: PASS.

- [ ] **Step 6: Write failing API tests**

Append to `ShipmentsEndpointsTests`. The restricted-user helper follows the invite → accept-invite → login chain already used in `tests/Api.Tests/BillingEndpointsTests.cs:45-90`; open that file and copy its helper verbatim into this test class as `CreateBranchScopedStaffClientAsync(HttpClient ownerClient, Guid branchId, string roleName)`, changing only the role lookup to pick the role whose `Name == roleName` (use `"BranchManager"`, which has `shipment.read`) and returning the authenticated `HttpClient`.

```csharp
    private async Task<(HttpClient Owner, ShipmentResponse Shipment, Guid OriginBranchId)> BookOneAsync(string label)
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, label);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(client);
        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();
        return (client, shipment!, originBranchId);
    }

    [Fact]
    public async Task GetByAwb_StaffScopedToUnrelatedBranch_Returns404_ScopedToOrigin_Returns200()
    {
        var (owner, shipment, originBranchId) = await BookOneAsync("scope-read");
        var otherBranch = await (await owner.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"XB-{Guid.NewGuid():N}"[..10]))).Content.ReadFromJsonAsync<BranchResponse>();

        var outsider = await CreateBranchScopedStaffClientAsync(owner, otherBranch!.Id, "BranchManager");
        var insider = await CreateBranchScopedStaffClientAsync(owner, originBranchId, "BranchManager");

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/shipments/{shipment.Awb}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/shipments/{shipment.Awb}/events")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await insider.GetAsync($"/api/v1/shipments/{shipment.Awb}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await insider.GetAsync($"/api/v1/shipments/{shipment.Awb}/events")).StatusCode);
    }

    [Fact]
    public async Task GetEvents_OwnerSeesCreatedEvent_WithoutMetadataOrCoordinates()
    {
        var (owner, shipment, _) = await BookOneAsync("events-owner");

        var response = await owner.GetAsync($"/api/v1/shipments/{shipment.Awb}/events");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"eventType\":\"Created\"", json);
        Assert.DoesNotContain("metadata", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("latitude", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEvents_OtherTenant_Returns404()
    {
        var (_, shipment, _) = await BookOneAsync("events-tenant-a");
        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "events-tenant-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/v1/shipments/{shipment.Awb}/events")).StatusCode);
    }

    [Fact]
    public async Task GetEvents_MerchantCaller_Returns403()
    {
        var (owner, shipment, _) = await BookOneAsync("events-merchant");
        var (ownerEmail, password) = await CreateMerchantWithLoginAsync(owner, "events");
        var merchantClient = _factory.CreateClient();
        var login = await merchantClient.PostAsJsonAsync("/api/v1/merchant-auth/login", new { email = ownerEmail, password });
        var token = (await login.Content.ReadFromJsonAsync<AuthResult>())!.AccessToken;
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Forbidden, (await merchantClient.GetAsync($"/api/v1/shipments/{shipment.Awb}/events")).StatusCode);
    }
```

Before using the merchant login URL/shape above, confirm it against the existing `Merchant_CanCreateAndCancelOwnShipment_ButNotAnotherMerchants` test in the same file (≈ line 238) and use exactly the login call and token type that test uses.

- [ ] **Step 7: Run to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~ShipmentsEndpointsTests.GetEvents|FullyQualifiedName~ShipmentsEndpointsTests.GetByAwb_Staff"`
Expected: FAIL — events route 404s for everyone (unmapped → the `/{awb}` route doesn't match two segments), outsider gets 200 today.

- [ ] **Step 8: Map the events route**

In `ShipmentsEndpoints.cs`, add directly after the `group.MapGet("/{awb}", ...)` block:

```csharp
        // Staff-only (F15 R2): merchants already see their customer-safe history via /track.
        group.MapGet("/{awb}/events", async (string awb, ShipmentService service, HttpContext httpContext, IPermissionChecker permissionChecker) =>
        {
            if (httpContext.User.GetMerchantId() is not null)
            {
                return Results.Forbid();
            }
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }
            if (!await permissionChecker.HasPermissionAsync(httpContext.User, "shipment.read", httpContext.RequestAborted))
            {
                return Results.Forbid();
            }

            var events = await service.GetEventsByAwbAsync(awb, httpContext.User, httpContext.RequestAborted);
            return events is null ? Results.NotFound() : Results.Ok(events);
        });
```

- [ ] **Step 9: Run tests**

Run: `dotnet test tests/Shipments.Tests` and `dotnet test tests/Api.Tests`
Expected: PASS. If any pre-existing Api test relied on a branch-restricted user reading an out-of-scope AWB, it now gets 404 — that is the intended behaviour change; update the assertion and note the test name in the commit body.

- [ ] **Step 10: Commit**

```bash
git add src/Modules/Shipments tests/Shipments.Tests tests/Api.Tests/ShipmentsEndpointsTests.cs
git commit -m "feat(shipments): branch-scoped AWB reads and staff events endpoint (F15 R2)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Frontend data layer — range presets, status groups, list params, hooks, events proxy

**Files:**
- Create: `src/web/src/lib/dates/rangePresets.ts`, `src/web/src/lib/dates/rangePresets.test.ts`
- Create: `src/web/src/features/shipments/lib/statusGroups.ts`, `statusGroups.test.ts`
- Create: `src/web/src/features/shipments/lib/listParams.ts`, `listParams.test.ts`
- Modify: `src/web/src/features/shipments/types.ts`
- Modify: `src/web/src/features/shipments/hooks/useShipments.ts`
- Create: `src/web/src/features/shipments/hooks/useShipmentEvents.ts`
- Create: `src/web/src/app/api/shipments/[awb]/events/route.ts`, `route.test.ts`

**Interfaces:**
- Produces (`rangePresets.ts`): `type RangePreset = "today" | "7d" | "30d"`; `localIsoDay(d: Date): string`; `presetDays(preset: RangePreset, now?: Date): { from: string; to: string }`; `toUtcRange(from?: string, to?: string): { from?: string; to?: string }`.
- Produces (`statusGroups.ts`): `type ShipmentTab = "all" | "at-branch" | "in-transit" | "out-for-delivery" | "ndr" | "rto"`; `SHIPMENT_TABS: { id: ShipmentTab; label: string; statuses: string[] }[]`; `ALL_SHIPMENT_STATUSES: string[]`; `isShipmentTab(v: string | null): v is ShipmentTab`; `tabStatuses(tab: ShipmentTab): string[]`; `sumStatuses(counts: Record<string, number> | undefined, statuses: string[]): number`.
- Produces (`listParams.ts`): `type BookedRange = "" | RangePreset | "custom"`; `interface ShipmentListParams { tab: ShipmentTab; statuses: string[]; q: string; branchId: string; merchantId: string; serviceTypeId: string; booked: BookedRange; from: string; to: string; page: number }`; `parseListParams(sp: URLSearchParams): ShipmentListParams`; `toSearchParams(p: ShipmentListParams): URLSearchParams`; `toApiQuery(p: ShipmentListParams, pageSize: number, now?: Date): URLSearchParams`; `hasActiveFilters(p: ShipmentListParams): boolean`; `EMPTY_LIST_PARAMS: ShipmentListParams`.
- Produces: `Shipment.lastEventAt: string | null`; `interface ShipmentEvent { id; eventType: string; previousStatus: string | null; newStatus: string; branchId: string | null; userId: string | null; riderId: string | null; timestamp: string; notes: string | null }`.
- Produces: `useShipments(params: ShipmentListParams, pageSize?: number)` (default 20); `useShipmentEvents(awb: string)`.

- [ ] **Step 1: Write failing tests for `rangePresets`, `statusGroups`, `listParams`**

`src/web/src/lib/dates/rangePresets.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { localIsoDay, presetDays, toUtcRange } from "./rangePresets";

describe("rangePresets", () => {
  const now = new Date(2026, 9, 5, 14, 30); // 5 Oct 2026, local time

  it("formats a local calendar day", () => {
    expect(localIsoDay(new Date(2026, 0, 9))).toBe("2026-01-09");
  });

  it("today is a single day", () => {
    expect(presetDays("today", now)).toEqual({ from: "2026-10-05", to: "2026-10-05" });
  });

  it("7d and 30d include today", () => {
    expect(presetDays("7d", now)).toEqual({ from: "2026-09-29", to: "2026-10-05" });
    expect(presetDays("30d", now)).toEqual({ from: "2026-09-06", to: "2026-10-05" });
  });

  it("anchors days to UTC bounds and omits missing ends", () => {
    expect(toUtcRange("2026-10-01", "2026-10-05")).toEqual({ from: "2026-10-01T00:00:00Z", to: "2026-10-05T23:59:59Z" });
    expect(toUtcRange("", undefined)).toEqual({ from: undefined, to: undefined });
  });
});
```

`src/web/src/features/shipments/lib/statusGroups.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { ALL_SHIPMENT_STATUSES, SHIPMENT_TABS, isShipmentTab, sumStatuses, tabStatuses } from "./statusGroups";

describe("statusGroups", () => {
  it("never puts a status in two tabs", () => {
    const seen = new Map<string, string>();
    for (const tab of SHIPMENT_TABS) {
      for (const status of tab.statuses) {
        expect(seen.get(status), `${status} in ${tab.id} and ${seen.get(status)}`).toBeUndefined();
        seen.set(status, tab.id);
      }
    }
  });

  it("leaves exactly Delivered, Cancelled and the international statuses to 'All'", () => {
    const grouped = new Set(SHIPMENT_TABS.flatMap((t) => t.statuses));
    const ungrouped = ALL_SHIPMENT_STATUSES.filter((s) => !grouped.has(s)).sort();
    expect(ungrouped).toEqual(
      [
        "Cancelled", "CustomsClearance", "CustomsHold", "CustomsPrepared", "Delivered", "DeliveredAbroad",
        "HandedToCarrier", "InInternationalTransit", "InternationalException", "ReceivedFromCarrier",
      ].sort()
    );
  });

  it("'all' has no status filter", () => {
    expect(tabStatuses("all")).toEqual([]);
  });

  it("sums counts for a group, treating missing keys as 0", () => {
    expect(sumStatuses({ DeliveryFailed: 2, NdrCreated: 3, Delivered: 9 }, tabStatuses("ndr"))).toBe(5);
    expect(sumStatuses(undefined, tabStatuses("ndr"))).toBe(0);
  });

  it("recognises tab ids", () => {
    expect(isShipmentTab("rto")).toBe(true);
    expect(isShipmentTab("bogus")).toBe(false);
    expect(isShipmentTab(null)).toBe(false);
  });
});
```

`src/web/src/features/shipments/lib/listParams.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { EMPTY_LIST_PARAMS, hasActiveFilters, parseListParams, toApiQuery, toSearchParams } from "./listParams";

describe("listParams", () => {
  it("parses defaults from an empty query string", () => {
    expect(parseListParams(new URLSearchParams())).toEqual(EMPTY_LIST_PARAMS);
  });

  it("round-trips every field through the URL", () => {
    const params = {
      ...EMPTY_LIST_PARAMS,
      tab: "ndr" as const,
      q: "sita",
      branchId: "b1",
      merchantId: "m1",
      serviceTypeId: "s1",
      booked: "custom" as const,
      from: "2026-10-01",
      to: "2026-10-05",
      page: 3,
    };
    expect(parseListParams(toSearchParams(params))).toEqual(params);
  });

  it("omits defaults from the URL", () => {
    expect(toSearchParams(EMPTY_LIST_PARAMS).toString()).toBe("");
  });

  it("ignores an unknown tab and a bad page", () => {
    const p = parseListParams(new URLSearchParams("tab=bogus&page=-4"));
    expect(p.tab).toBe("all");
    expect(p.page).toBe(1);
  });

  it("explicit status params override the tab", () => {
    const p = parseListParams(new URLSearchParams("tab=ndr&status=Created&status=PickupAssigned"));
    const api = toApiQuery(p, 20);
    expect(api.getAll("status")).toEqual(["Created", "PickupAssigned"]);
  });

  it("maps a tab to its statuses for the API", () => {
    const api = toApiQuery({ ...EMPTY_LIST_PARAMS, tab: "rto" }, 20);
    expect(api.getAll("status")).toEqual(["RtoInitiated", "RtoInTransit", "RtoAtOrigin", "RtoDelivered"]);
    expect(api.get("page")).toBe("1");
    expect(api.get("pageSize")).toBe("20");
  });

  it("sends q only when it has at least 2 non-space characters", () => {
    expect(toApiQuery({ ...EMPTY_LIST_PARAMS, q: " a " }, 20).has("q")).toBe(false);
    expect(toApiQuery({ ...EMPTY_LIST_PARAMS, q: " ab " }, 20).get("q")).toBe("ab");
  });

  it("turns booked presets and custom ranges into UTC-anchored bounds", () => {
    const now = new Date(2026, 9, 5, 9, 0);
    const today = toApiQuery({ ...EMPTY_LIST_PARAMS, booked: "today" }, 20, now);
    expect(today.get("createdFrom")).toBe("2026-10-05T00:00:00Z");
    expect(today.get("createdTo")).toBe("2026-10-05T23:59:59Z");
    const custom = toApiQuery({ ...EMPTY_LIST_PARAMS, booked: "custom", from: "2026-10-01", to: "" }, 20, now);
    expect(custom.get("createdFrom")).toBe("2026-10-01T00:00:00Z");
    expect(custom.has("createdTo")).toBe(false);
  });

  it("detects active filters but not the tab or page", () => {
    expect(hasActiveFilters({ ...EMPTY_LIST_PARAMS, tab: "ndr", page: 4 })).toBe(false);
    expect(hasActiveFilters({ ...EMPTY_LIST_PARAMS, merchantId: "m" })).toBe(true);
    expect(hasActiveFilters({ ...EMPTY_LIST_PARAMS, q: "x" })).toBe(true);
  });
});
```

- [ ] **Step 2: Run to verify they fail**

Run (from `src/web`): `npx vitest run src/lib/dates src/features/shipments/lib`
Expected: FAIL — modules not found.

- [ ] **Step 3: Implement the three modules**

`src/web/src/lib/dates/rangePresets.ts`:

```ts
export type RangePreset = "today" | "7d" | "30d";

const PRESET_SPAN_DAYS: Record<RangePreset, number> = { today: 1, "7d": 7, "30d": 30 };

// The user's local calendar day as YYYY-MM-DD (not toISOString, which shifts to UTC).
export function localIsoDay(d: Date): string {
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, "0");
  const dd = String(d.getDate()).padStart(2, "0");
  return `${yyyy}-${mm}-${dd}`;
}

// Inclusive of today: "7d" is today and the 6 days before it.
export function presetDays(preset: RangePreset, now: Date = new Date()): { from: string; to: string } {
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate() - (PRESET_SPAN_DAYS[preset] - 1));
  return { from: localIsoDay(start), to: localIsoDay(now) };
}

// UTC-anchored before leaving the browser — a bare "YYYY-MM-DD" lets .NET's DateTimeOffset
// deserializer default to the API host's local offset, which Npgsql's timestamptz rejects
// (the Frontend Phase F7 bug).
export function toUtcRange(from?: string, to?: string): { from?: string; to?: string } {
  return {
    from: from ? `${from}T00:00:00Z` : undefined,
    to: to ? `${to}T23:59:59Z` : undefined,
  };
}
```

`src/web/src/features/shipments/lib/statusGroups.ts`:

```ts
// Mirrors backend ShipmentStatus (src/Modules/Shipments/Domain/ShipmentStatus.cs) exactly.
export const ALL_SHIPMENT_STATUSES = [
  "Created", "PickupAssigned", "PickedUp", "AtOriginBranch", "Sorted", "InTransit", "AtDestinationBranch",
  "OutForDelivery", "DeliveryFailed", "Delivered", "Cancelled", "NdrCreated", "RtoInitiated", "RtoInTransit",
  "RtoAtOrigin", "RtoDelivered", "CustomsPrepared", "HandedToCarrier", "InInternationalTransit",
  "DeliveredAbroad", "CustomsHold", "InternationalException", "CustomsClearance", "ReceivedFromCarrier",
] as const;

export type ShipmentTab = "all" | "at-branch" | "in-transit" | "out-for-delivery" | "ndr" | "rto";

// Single source for the Shipments tabs and the dashboard pipeline links. Delivered, Cancelled
// and international statuses are intentionally in no tab — they show under "All" only.
export const SHIPMENT_TABS: { id: ShipmentTab; label: string; statuses: string[] }[] = [
  { id: "all", label: "All", statuses: [] },
  {
    id: "at-branch",
    label: "At branch",
    statuses: ["Created", "PickupAssigned", "PickedUp", "AtOriginBranch", "Sorted", "AtDestinationBranch"],
  },
  { id: "in-transit", label: "In transit", statuses: ["InTransit"] },
  { id: "out-for-delivery", label: "Out for delivery", statuses: ["OutForDelivery"] },
  { id: "ndr", label: "NDR", statuses: ["DeliveryFailed", "NdrCreated"] },
  { id: "rto", label: "RTO", statuses: ["RtoInitiated", "RtoInTransit", "RtoAtOrigin", "RtoDelivered"] },
];

export function isShipmentTab(value: string | null): value is ShipmentTab {
  return value !== null && SHIPMENT_TABS.some((t) => t.id === value);
}

export function tabStatuses(tab: ShipmentTab): string[] {
  return SHIPMENT_TABS.find((t) => t.id === tab)?.statuses ?? [];
}

export function sumStatuses(counts: Record<string, number> | undefined, statuses: readonly string[]): number {
  if (!counts) return 0;
  return statuses.reduce((total, status) => total + (counts[status] ?? 0), 0);
}
```

`src/web/src/features/shipments/lib/listParams.ts`:

```ts
import { presetDays, toUtcRange, type RangePreset } from "@/lib/dates/rangePresets";
import { isShipmentTab, tabStatuses, type ShipmentTab } from "./statusGroups";

export type BookedRange = "" | RangePreset | "custom";

export interface ShipmentListParams {
  tab: ShipmentTab;
  /** Explicit statuses (dashboard deep links); win over `tab` when non-empty. */
  statuses: string[];
  q: string;
  branchId: string;
  merchantId: string;
  serviceTypeId: string;
  booked: BookedRange;
  /** YYYY-MM-DD, only used when booked === "custom". */
  from: string;
  to: string;
  page: number;
}

export const EMPTY_LIST_PARAMS: ShipmentListParams = {
  tab: "all",
  statuses: [],
  q: "",
  branchId: "",
  merchantId: "",
  serviceTypeId: "",
  booked: "",
  from: "",
  to: "",
  page: 1,
};

const BOOKED_VALUES: BookedRange[] = ["today", "7d", "30d", "custom"];

export function parseListParams(sp: URLSearchParams): ShipmentListParams {
  const tab = sp.get("tab");
  const booked = sp.get("booked") ?? "";
  const page = Number.parseInt(sp.get("page") ?? "1", 10);
  return {
    tab: isShipmentTab(tab) ? tab : "all",
    statuses: sp.getAll("status"),
    q: sp.get("q") ?? "",
    branchId: sp.get("branchId") ?? "",
    merchantId: sp.get("merchantId") ?? "",
    serviceTypeId: sp.get("serviceTypeId") ?? "",
    booked: (BOOKED_VALUES as string[]).includes(booked) ? (booked as BookedRange) : "",
    from: sp.get("from") ?? "",
    to: sp.get("to") ?? "",
    page: Number.isFinite(page) && page > 0 ? page : 1,
  };
}

export function toSearchParams(p: ShipmentListParams): URLSearchParams {
  const sp = new URLSearchParams();
  if (p.tab !== "all") sp.set("tab", p.tab);
  p.statuses.forEach((s) => sp.append("status", s));
  if (p.q) sp.set("q", p.q);
  if (p.branchId) sp.set("branchId", p.branchId);
  if (p.merchantId) sp.set("merchantId", p.merchantId);
  if (p.serviceTypeId) sp.set("serviceTypeId", p.serviceTypeId);
  if (p.booked) sp.set("booked", p.booked);
  if (p.booked === "custom" && p.from) sp.set("from", p.from);
  if (p.booked === "custom" && p.to) sp.set("to", p.to);
  if (p.page > 1) sp.set("page", String(p.page));
  return sp;
}

export function toApiQuery(p: ShipmentListParams, pageSize: number, now: Date = new Date()): URLSearchParams {
  const api = new URLSearchParams();
  api.set("page", String(p.page));
  api.set("pageSize", String(pageSize));
  const statuses = p.statuses.length > 0 ? p.statuses : tabStatuses(p.tab);
  statuses.forEach((s) => api.append("status", s));
  const q = p.q.trim();
  if (q.length >= 2) api.set("q", q);
  if (p.branchId) api.set("branchId", p.branchId);
  if (p.merchantId) api.set("merchantId", p.merchantId);
  if (p.serviceTypeId) api.set("serviceTypeId", p.serviceTypeId);
  const days = p.booked === "custom" ? { from: p.from, to: p.to } : p.booked ? presetDays(p.booked, now) : { from: "", to: "" };
  const range = toUtcRange(days.from, days.to);
  if (range.from) api.set("createdFrom", range.from);
  if (range.to) api.set("createdTo", range.to);
  return api;
}

export function hasActiveFilters(p: ShipmentListParams): boolean {
  return Boolean(p.q.trim() || p.branchId || p.merchantId || p.serviceTypeId || p.booked || p.statuses.length > 0);
}
```

- [ ] **Step 4: Run tests**

Run: `npx vitest run src/lib/dates src/features/shipments/lib`
Expected: PASS.

- [ ] **Step 5: Write the failing events proxy test**

`src/web/src/app/api/shipments/[awb]/events/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/shipments/[awb]/events", () => {
  it("proxies to the backend events endpoint with an encoded AWB", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => [] } as unknown as Response);

    const response = await GET({} as Request, { params: { awb: "AB 1/2" } });

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/shipments/AB%201%2F2/events");
    expect(response.status).toBe(200);
  });

  it("passes a 404 through", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: false, status: 404, json: async () => null } as unknown as Response);
    const response = await GET({} as Request, { params: { awb: "X" } });
    expect(response.status).toBe(404);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET({} as Request, { params: { awb: "X" } });
    expect(response.status).toBe(401);
  });
});
```

- [ ] **Step 6: Run to verify it fails**

Run: `npx vitest run "src/app/api/shipments/\[awb\]/events"`
Expected: FAIL — `./route` not found.

- [ ] **Step 7: Implement the route, types and hooks**

`src/web/src/app/api/shipments/[awb]/events/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { awb: string } }) {
  try {
    const response = await apiFetch(`/api/v1/shipments/${encodeURIComponent(params.awb)}/events`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

In `src/web/src/features/shipments/types.ts`, add `lastEventAt: string | null;` after `createdAt: string;` in `Shipment`, and append:

```ts
export interface ShipmentEvent {
  id: string;
  eventType: string;
  previousStatus: string | null;
  newStatus: string;
  branchId: string | null;
  userId: string | null;
  riderId: string | null;
  timestamp: string;
  notes: string | null;
}
```

Replace `src/web/src/features/shipments/hooks/useShipments.ts` with:

```ts
"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Shipment } from "@/features/shipments/types";
import { toApiQuery, type ShipmentListParams } from "@/features/shipments/lib/listParams";

export function useShipments(params: ShipmentListParams, pageSize = 20) {
  const query = toApiQuery(params, pageSize).toString();
  return useQuery({
    queryKey: ["shipments", "list", query],
    queryFn: () => clientFetch<PagedResult<Shipment>>(`/api/shipments?${query}`),
    placeholderData: keepPreviousData,
  });
}
```

`src/web/src/features/shipments/hooks/useShipmentEvents.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ShipmentEvent } from "@/features/shipments/types";

export function useShipmentEvents(awb: string) {
  return useQuery({
    queryKey: ["shipments", awb, "events"],
    queryFn: () => clientFetch<ShipmentEvent[]>(`/api/shipments/${encodeURIComponent(awb)}/events`),
    enabled: awb !== "",
  });
}
```

- [ ] **Step 8: Run tests and type-check**

Run: `npx vitest run src/lib/dates src/features/shipments "src/app/api/shipments"` then `npx tsc --noEmit`.
Expected: tests PASS. `tsc` reports the 8 pre-existing errors plus an error in `app/(staff)/shipments/page.tsx` (old `useShipments(page, pageSize)` call) — that file is rewritten in Task 4; leave it.

- [ ] **Step 9: Commit**

```bash
git add src/web/src/lib/dates src/web/src/features/shipments src/web/src/app/api/shipments
git commit -m "feat(web): shipment list params, status groups, events hook and proxy (F15 R2)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Shipments list page (tabs, filter bar, table)

**Files:**
- Create: `src/web/src/features/shipments/components/ShipmentsView.tsx`
- Create: `src/web/src/features/shipments/components/ShipmentFilterBar.tsx`
- Create: `src/web/src/features/shipments/components/FilterChip.tsx`
- Modify: `src/web/src/app/(staff)/shipments/page.tsx`
- Replace: `src/web/src/app/(staff)/shipments/page.test.tsx` → `src/web/src/features/shipments/components/ShipmentsView.test.tsx`

**Interfaces:**
- Consumes: Task 3 (`useShipments(params)`, `parseListParams`, `toSearchParams`, `hasActiveFilters`, `SHIPMENT_TABS`, `sumStatuses`, `presetDays`), existing `useShipmentsSummary`, `useBranches`, `useMerchants`, `useServiceTypes`, `usePermission`, `useShipmentPanelStore`, `useDebouncedValue(value, ms)` (`@/lib/hooks/useDebouncedValue`), `DataTable` (`selectedRowId` prop), `PageHeader`.
- Produces: `ShipmentsView` (default page content), `FilterChip` (`{ label: string; valueLabel: string; options: { value: string; label: string }[]; value: string; onChange(value: string): void }`).

- [ ] **Step 1: Write the failing view test**

Delete `src/web/src/app/(staff)/shipments/page.test.tsx` (it asserts the removed summary strip). Create `src/web/src/features/shipments/components/ShipmentsView.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import type { ShipmentListParams } from "@/features/shipments/lib/listParams";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/shipments",
  useSearchParams: () => new URLSearchParams(search),
}));

const useShipmentsMock = vi.fn();
vi.mock("@/features/shipments/hooks/useShipments", () => ({
  useShipments: (params: ShipmentListParams) => useShipmentsMock(params),
}));
vi.mock("@/features/shipments/hooks/useShipmentsSummary", () => ({
  useShipmentsSummary: () => ({ data: { InTransit: 4, DeliveryFailed: 1, NdrCreated: 2 }, isLoading: false, isError: false }),
}));
vi.mock("@/features/shipments/hooks/useServiceTypes", () => ({ useServiceTypes: () => ({ data: [{ id: "s1", code: "Express", name: "Express" }] }) }));
vi.mock("@/features/shipments/hooks/useMerchants", () => ({ useMerchants: () => ({ data: [{ id: "m1", displayName: "Everest Organics", isActive: true }] }) }));
vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", code: "KTM", name: "Kathmandu Hub" }, { id: "b2", code: "LAL", name: "Lalitpur" }] }),
}));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: () => true }));
const openDetail = vi.fn();
vi.mock("@/features/shipments/store/shipmentPanelStore", () => ({
  useShipmentPanelStore: (selector: (s: object) => unknown) =>
    selector({ openCreate: vi.fn(), openDetail, panel: { mode: "closed" } }),
}));

import { ShipmentsView } from "./ShipmentsView";

const row = {
  id: "x1", awb: "QA-1", merchantId: "m1", originBranchId: "b1", destinationBranchId: "b2", currentBranchId: "b2",
  currentCustodianUserId: null, currentCustodianRiderId: null, currentStatus: "InTransit", serviceTypeId: "s1",
  weightKg: 1, lengthCm: null, widthCm: null, heightCm: null, declaredValue: 0, codAmount: 0, wantsInsurance: false,
  deliveryCharge: 0, insuranceCharge: 0, otherCharges: 0, totalCharge: 0, recipientName: "Sita", recipientPhone: "98",
  recipientAddressLine: "Jawalakhel", senderName: "S", senderPhone: "1", senderAddressLine: "A", items: [],
  createdAt: "2026-10-05T05:00:00Z", lastEventAt: null,
};

function page(items: unknown[]) {
  return { data: { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 }, isLoading: false, isError: false, refetch: vi.fn() };
}

describe("ShipmentsView", () => {
  beforeEach(() => {
    search = "";
    replace.mockReset();
    openDetail.mockReset();
    useShipmentsMock.mockReset().mockReturnValue(page([row]));
  });

  it("shows tab counts from the summary", () => {
    render(<ShipmentsView />);
    expect(screen.getByRole("tab", { name: /NDR\s*3/ })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /In transit\s*4/ })).toBeInTheDocument();
  });

  it("reads the active tab from the URL and passes it to the query", () => {
    search = "tab=ndr";
    render(<ShipmentsView />);
    expect(screen.getByRole("tab", { name: /NDR/ })).toHaveAttribute("aria-selected", "true");
    expect(useShipmentsMock).toHaveBeenLastCalledWith(expect.objectContaining({ tab: "ndr" }));
  });

  it("selecting a tab writes it to the URL and resets the page", () => {
    search = "page=3";
    render(<ShipmentsView />);
    fireEvent.mouseDown(screen.getByRole("tab", { name: /RTO/ }));
    expect(replace).toHaveBeenCalledWith("/shipments?tab=rto", { scroll: false });
  });

  it("debounces search before writing it to the URL", () => {
    vi.useFakeTimers();
    render(<ShipmentsView />);
    fireEvent.change(screen.getByRole("searchbox", { name: /filter shipments/i }), { target: { value: "sita" } });
    expect(replace).not.toHaveBeenCalled();
    act(() => vi.advanceTimersByTime(300));
    expect(replace).toHaveBeenCalledWith("/shipments?q=sita", { scroll: false });
    vi.useRealTimers();
  });

  it("renders route codes and opens the sheet on row click", () => {
    render(<ShipmentsView />);
    expect(screen.getByText("KTM → LAL")).toBeInTheDocument();
    expect(screen.getByText("Prepaid")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "QA-1" }));
    expect(openDetail).toHaveBeenCalledWith("QA-1");
  });

  it("distinguishes a filtered-empty result and offers to clear filters", () => {
    search = "merchantId=m1";
    useShipmentsMock.mockReturnValue(page([]));
    render(<ShipmentsView />);
    expect(screen.getByText("No shipments match these filters")).toBeInTheDocument();
    fireEvent.click(within(screen.getByRole("toolbar")).getByRole("button", { name: /clear filters/i }));
    expect(replace).toHaveBeenCalledWith("/shipments", { scroll: false });
  });

  it("shows the plain empty state with no filters", () => {
    useShipmentsMock.mockReturnValue(page([]));
    render(<ShipmentsView />);
    expect(screen.getByText("No shipments yet")).toBeInTheDocument();
  });
});
```

Note: Radix `TabsTrigger` activates on `mouseDown` in jsdom; if the existing test suite activates tabs differently (search `src` for `TabsTrigger` tests), follow that file's pattern instead.

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/features/shipments/components/ShipmentsView.test.tsx`
Expected: FAIL — `./ShipmentsView` not found.

- [ ] **Step 3: Implement `FilterChip`**

`src/web/src/features/shipments/components/FilterChip.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Check, ChevronDown } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command";
import { cn } from "@/lib/utils";

export interface FilterOption {
  value: string;
  label: string;
}

// One filter dimension as a dashed chip; "" means "Any".
export function FilterChip({
  label,
  options,
  value,
  onChange,
}: {
  label: string;
  options: FilterOption[];
  value: string;
  onChange: (value: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const selected = options.find((o) => o.value === value);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className={cn("h-9 gap-1.5 border-dashed bg-transparent text-[12.5px] font-normal", selected && "border-solid")}
        >
          {label}
          <span className="font-medium text-primary">{selected?.label ?? "Any"}</span>
          <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" aria-hidden="true" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-64 p-0" align="start">
        <Command>
          {options.length > 7 && <CommandInput placeholder={`Search ${label.toLowerCase()}…`} />}
          <CommandList>
            <CommandEmpty>No matches.</CommandEmpty>
            <CommandGroup>
              {[{ value: "", label: "Any" }, ...options].map((option) => (
                <CommandItem
                  key={option.value || "any"}
                  value={`${option.label} ${option.value}`}
                  onSelect={() => {
                    onChange(option.value);
                    setOpen(false);
                  }}
                >
                  <Check className={cn("mr-2 h-4 w-4", option.value === value ? "opacity-100" : "opacity-0")} aria-hidden="true" />
                  {option.label}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
```

- [ ] **Step 4: Implement `ShipmentFilterBar`**

`src/web/src/features/shipments/components/ShipmentFilterBar.tsx`:

```tsx
"use client";

import { useEffect, useState } from "react";
import { Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { useDebouncedValue } from "@/lib/hooks/useDebouncedValue";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { hasActiveFilters, type BookedRange, type ShipmentListParams } from "@/features/shipments/lib/listParams";
import { FilterChip } from "./FilterChip";

const BOOKED_OPTIONS: { value: Exclude<BookedRange, "">; label: string }[] = [
  { value: "today", label: "Today" },
  { value: "7d", label: "Last 7 days" },
  { value: "30d", label: "Last 30 days" },
  { value: "custom", label: "Custom range" },
];

export function ShipmentFilterBar({
  params,
  onChange,
  onClear,
}: {
  params: ShipmentListParams;
  onChange: (patch: Partial<ShipmentListParams>) => void;
  onClear: () => void;
}) {
  const { data: branches } = useBranches();
  const { data: merchants } = useMerchants();
  const { data: serviceTypes } = useServiceTypes();
  const [searchText, setSearchText] = useState(params.q);
  const debounced = useDebouncedValue(searchText, 300);

  // URL → input when the URL changes from elsewhere (Clear filters, back button).
  useEffect(() => setSearchText(params.q), [params.q]);
  // Debounced input → URL. Compared to params.q so a URL-driven reset doesn't loop.
  useEffect(() => {
    if (debounced !== params.q) onChange({ q: debounced });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- only the debounced value drives this
  }, [debounced]);

  return (
    <div role="toolbar" aria-label="Shipment filters" className="flex flex-wrap items-center gap-2">
      <label className="flex h-9 w-full items-center gap-2 rounded-md border border-input bg-card px-2.5 text-[13px] text-muted-foreground sm:w-72">
        <Search className="h-4 w-4 shrink-0" aria-hidden="true" />
        <input
          type="search"
          aria-label="Filter shipments"
          placeholder="Filter by AWB, name or phone"
          value={searchText}
          onChange={(e) => setSearchText(e.target.value)}
          className="min-w-0 flex-1 bg-transparent text-foreground outline-none placeholder:text-muted-foreground"
        />
      </label>
      <FilterChip
        label="Branch"
        value={params.branchId}
        onChange={(branchId) => onChange({ branchId })}
        options={(branches ?? []).map((b) => ({ value: b.id, label: b.name }))}
      />
      <FilterChip
        label="Merchant"
        value={params.merchantId}
        onChange={(merchantId) => onChange({ merchantId })}
        options={(merchants ?? []).map((m) => ({ value: m.id, label: m.displayName }))}
      />
      <FilterChip
        label="Service"
        value={params.serviceTypeId}
        onChange={(serviceTypeId) => onChange({ serviceTypeId })}
        options={(serviceTypes ?? []).map((s) => ({ value: s.id, label: s.name }))}
      />
      <FilterChip
        label="Booked"
        value={params.booked}
        onChange={(booked) => onChange({ booked: booked as BookedRange, from: "", to: "" })}
        options={BOOKED_OPTIONS}
      />
      {params.booked === "custom" && (
        <div className="flex items-center gap-2">
          <DatePicker id="shipments-from" aria-label="Booked from" value={params.from} onChange={(from) => onChange({ from })} clearable />
          <DatePicker id="shipments-to" aria-label="Booked to" value={params.to} onChange={(to) => onChange({ to })} clearable />
        </div>
      )}
      {hasActiveFilters(params) && (
        <Button type="button" variant="ghost" size="sm" className="h-9" onClick={onClear}>
          Clear filters
        </Button>
      )}
    </div>
  );
}
```

- [ ] **Step 5: Implement `ShipmentsView` and the page wrapper**

`src/web/src/features/shipments/components/ShipmentsView.tsx`:

```tsx
"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { PageHeader } from "@/components/page/PageHeader";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { useShipments } from "@/features/shipments/hooks/useShipments";
import { useShipmentsSummary } from "@/features/shipments/hooks/useShipmentsSummary";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import { EMPTY_LIST_PARAMS, hasActiveFilters, parseListParams, toSearchParams, type ShipmentListParams } from "@/features/shipments/lib/listParams";
import { ALL_SHIPMENT_STATUSES, SHIPMENT_TABS, sumStatuses, type ShipmentTab } from "@/features/shipments/lib/statusGroups";
import { formatRelativeTime } from "@/features/shipments/lib/relativeTime";
import type { Shipment } from "@/features/shipments/types";
import { ShipmentFilterBar } from "./ShipmentFilterBar";

const money = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });

export function ShipmentsView() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const params = parseListParams(new URLSearchParams(searchParams.toString()));

  const { data, isLoading, isError, refetch } = useShipments(params);
  const summary = useShipmentsSummary();
  const { data: branches } = useBranches();
  const { data: merchants } = useMerchants();
  const { data: serviceTypes } = useServiceTypes();
  const canCreate = usePermission("shipment.create");
  const openCreate = useShipmentPanelStore((s) => s.openCreate);
  const openDetail = useShipmentPanelStore((s) => s.openDetail);
  const panel = useShipmentPanelStore((s) => s.panel);

  function navigate(next: ShipmentListParams) {
    const qs = toSearchParams(next).toString();
    router.replace(qs ? `${pathname}?${qs}` : pathname, { scroll: false });
  }
  // Any filter/tab change resets to page 1 and drops dashboard-supplied explicit statuses.
  function patch(change: Partial<ShipmentListParams>) {
    navigate({ ...params, statuses: "tab" in change ? [] : params.statuses, ...change, page: 1 });
  }

  const branchById = new Map((branches ?? []).map((b) => [b.id, b]));
  const merchantName = (id: string) => merchants?.find((m) => m.id === id)?.displayName ?? "—";
  const serviceName = (id: string) => serviceTypes?.find((s) => s.id === id)?.name ?? "—";
  const code = (id: string) => branchById.get(id)?.code ?? "—";
  const allCount = summary.data ? sumStatuses(summary.data, ALL_SHIPMENT_STATUSES) : undefined;
  const filtered = hasActiveFilters(params);
  const activeTab: ShipmentTab | "" = params.statuses.length > 0 ? "" : params.tab;

  const columns: ColumnDef<Shipment>[] = [
    {
      key: "awb",
      header: "AWB",
      render: (s) => (
        <div className="flex flex-col">
          <button type="button" onClick={() => openDetail(s.awb)} className="w-fit font-mono font-medium text-foreground hover:underline">
            {s.awb}
          </button>
          <span className="text-[11.5px] text-muted-foreground">{new Date(s.createdAt).toLocaleString()}</span>
        </div>
      ),
    },
    { key: "merchant", header: "Merchant", render: (s) => merchantName(s.merchantId) },
    {
      key: "recipient",
      header: "Recipient",
      render: (s) => (
        <div className="flex flex-col">
          <span>{s.recipientName}</span>
          <span className="text-[11.5px] text-muted-foreground">{s.recipientAddressLine}</span>
        </div>
      ),
    },
    { key: "route", header: "Route", render: (s) => <span className="font-mono text-xs">{`${code(s.originBranchId)} → ${code(s.destinationBranchId)}`}</span> },
    { key: "service", header: "Service", render: (s) => serviceName(s.serviceTypeId) },
    {
      key: "cod",
      header: "COD",
      render: (s) => <span className="block text-right font-medium tabular-nums">{s.codAmount > 0 ? `Rs ${money.format(s.codAmount)}` : "Prepaid"}</span>,
    },
    { key: "status", header: "Status", render: (s) => <StatusBadge status={s.currentStatus} /> },
    {
      key: "lastScan",
      header: "Last scan",
      render: (s) => <span className="text-xs text-muted-foreground">{s.lastEventAt ? formatRelativeTime(s.lastEventAt) : "—"}</span>,
    },
  ];

  return (
    <div className="space-y-5">
      <PageHeader
        title="Shipments"
        description={data ? `${data.totalCount.toLocaleString("en-US")} shipments in your branch scope` : undefined}
        actions={
          canCreate && (
            <Button className="gap-1.5" onClick={() => openCreate()}>
              <Plus className="h-4 w-4" aria-hidden="true" />
              Book shipment
            </Button>
          )
        }
      />

      <Tabs value={activeTab} onValueChange={(tab) => patch({ tab: tab as ShipmentTab })}>
        <TabsList className="w-full justify-start overflow-x-auto">
          {SHIPMENT_TABS.map((tab) => {
            const count = tab.id === "all" ? allCount : summary.data ? sumStatuses(summary.data, tab.statuses) : undefined;
            return (
              <TabsTrigger key={tab.id} value={tab.id} className="gap-2">
                {tab.label}
                {count !== undefined && (
                  <span className="rounded-full bg-muted px-1.5 text-[11.5px] tabular-nums text-muted-foreground">{count.toLocaleString("en-US")}</span>
                )}
              </TabsTrigger>
            );
          })}
        </TabsList>
      </Tabs>

      <ShipmentFilterBar params={params} onChange={patch} onClear={() => navigate({ ...EMPTY_LIST_PARAMS, tab: params.tab })} />

      {isError ? (
        <ErrorState title="Couldn't load shipments" description="Something went wrong fetching the shipment list." onRetry={() => refetch()} />
      ) : (
        <DataTable
          columns={columns}
          data={data}
          isLoading={isLoading}
          emptyTitle={filtered ? "No shipments match these filters" : "No shipments yet"}
          emptyDescription={filtered ? "Try a different search or clear the filters." : "Book your first shipment to get started."}
          onPageChange={(page) => navigate({ ...params, page })}
          rowKey={(s) => s.id}
          selectedRowId={panel.mode === "detail" ? data?.items.find((s) => s.awb === panel.awb)?.id : undefined}
        />
      )}
    </div>
  );
}
```

Note on the "Clear filters" assertion in the test: with no tab set, `navigate({ ...EMPTY_LIST_PARAMS, tab: "all" })` produces `/shipments`. The filtered-empty `EmptyState` shows only its title/description; the clear button is the toolbar's.

`src/web/src/features/shipments/lib/relativeTime.ts` (used for "Last scan"; covered by a test in this step):

```ts
// Compact "Last scan" text: "just now", "12 min ago", "3 h ago", "Yesterday", else a date.
export function formatRelativeTime(iso: string, now: Date = new Date()): string {
  const then = new Date(iso);
  const diffMin = Math.floor((now.getTime() - then.getTime()) / 60000);
  if (diffMin < 1) return "just now";
  if (diffMin < 60) return `${diffMin} min ago`;
  const diffH = Math.floor(diffMin / 60);
  if (diffH < 24) return `${diffH} h ago`;
  if (diffH < 48) return "Yesterday";
  return then.toLocaleDateString();
}
```

`src/web/src/features/shipments/lib/relativeTime.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { formatRelativeTime } from "./relativeTime";

describe("formatRelativeTime", () => {
  const now = new Date("2026-10-05T12:00:00Z");
  it("buckets recent times", () => {
    expect(formatRelativeTime("2026-10-05T11:59:40Z", now)).toBe("just now");
    expect(formatRelativeTime("2026-10-05T11:48:00Z", now)).toBe("12 min ago");
    expect(formatRelativeTime("2026-10-05T09:00:00Z", now)).toBe("3 h ago");
    expect(formatRelativeTime("2026-10-04T08:00:00Z", now)).toBe("Yesterday");
  });
  it("falls back to a date for older times", () => {
    const iso = "2026-09-20T08:00:00Z";
    expect(formatRelativeTime(iso, now)).toBe(new Date(iso).toLocaleDateString());
  });
});
```

Replace `src/web/src/app/(staff)/shipments/page.tsx` with:

```tsx
import { Suspense } from "react";
import { ShipmentsView } from "@/features/shipments/components/ShipmentsView";

// useSearchParams in the view needs a Suspense boundary for the App Router build.
export default function ShipmentsPage() {
  return (
    <Suspense>
      <ShipmentsView />
    </Suspense>
  );
}
```

- [ ] **Step 6: Run tests**

Run: `npx vitest run src/features/shipments`
Expected: PASS. If `SummaryStatsBar` is now unused anywhere (`grep -rn "SummaryStatsBar" src`), leave it — other pages may import it; do not delete shared components in this task.

- [ ] **Step 7: Type-check and lint**

Run: `npx tsc --noEmit` (no errors beyond the 8 pre-existing) and `npm run lint`.

- [ ] **Step 8: Commit**

```bash
git add -A src/web/src/features/shipments "src/web/src/app/(staff)/shipments"
git commit -m "feat(web): shipments list with status tabs, URL filters and route column (F15 R2)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Sheet logic — progress stepper model, custody chain, event labels, people names

**Files:**
- Create: `src/web/src/features/shipments/lib/progress.ts`, `progress.test.ts`
- Create: `src/web/src/features/shipments/lib/custodyChain.ts`, `custodyChain.test.ts`
- Create: `src/web/src/features/shipments/lib/eventLabels.ts`, `eventLabels.test.ts`
- Create: `src/web/src/features/shipments/hooks/usePeopleNames.ts`, `usePeopleNames.test.tsx`

**Interfaces:**
- Consumes: `ShipmentEvent` (Task 3).
- Produces (`progress.ts`): `PROGRESS_STEPS: { key: string; label: string }[]` (7 steps); `type StepState = "complete" | "current" | "upcoming"`; `type ProgressException = "ndr" | "rto" | "cancelled" | null`; `buildProgress(status: string): { steps: { key: string; label: string; state: StepState }[]; exception: ProgressException; exceptionAfter: number }` (`exceptionAfter` = index of the last reached forward step, -1 if none).
- Produces (`custodyChain.ts`): `interface CustodySegment { holderKey: string; riderId: string | null; userId: string | null; branchId: string | null; from: string; to: string | null; eventCount: number }`; `buildCustodyChain(events: ShipmentEvent[]): CustodySegment[]` (input any order; output oldest first; last segment `to: null`); `custodySince(events: ShipmentEvent[]): string | null`; `formatDuration(fromIso: string, toIso: string | null, now?: Date): string`.
- Produces (`eventLabels.ts`): `eventTitle(eventType: string): string`.
- Produces (`usePeopleNames.ts`): `usePeopleNames(): { branchName(id: string | null): string; riderName(id: string | null): string; userName(id: string | null): string }` — falls back to "Rider"/"Staff"/"—".

- [ ] **Step 1: Write failing tests**

`progress.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { buildProgress, PROGRESS_STEPS } from "./progress";

const states = (status: string) => buildProgress(status).steps.map((s) => s.state);

describe("buildProgress", () => {
  it("has seven forward steps", () => {
    expect(PROGRESS_STEPS.map((s) => s.label)).toEqual([
      "Booked", "Picked up", "Origin hub", "In transit", "Destination", "Out for delivery", "Delivered",
    ]);
  });

  it("marks earlier steps complete and the current one current", () => {
    expect(states("OutForDelivery")).toEqual(["complete", "complete", "complete", "complete", "complete", "current", "upcoming"]);
    expect(states("Created")).toEqual(["current", "upcoming", "upcoming", "upcoming", "upcoming", "upcoming", "upcoming"]);
    expect(states("Sorted")[2]).toBe("current");
  });

  it("marks everything complete when delivered", () => {
    expect(states("Delivered").every((s) => s === "complete")).toBe(true);
    expect(buildProgress("Delivered").exception).toBeNull();
  });

  it("stops at out-for-delivery with an NDR marker", () => {
    const p = buildProgress("NdrCreated");
    expect(p.exception).toBe("ndr");
    expect(p.exceptionAfter).toBe(5);
    expect(p.steps[6].state).toBe("upcoming");
    expect(p.steps.some((s) => s.state === "current")).toBe(false);
  });

  it("shows RTO after the furthest forward step and never claims delivery", () => {
    const p = buildProgress("RtoInTransit");
    expect(p.exception).toBe("rto");
    expect(p.steps[6].state).toBe("upcoming");
  });

  it("treats international and unknown statuses as in transit, cancelled as an exception", () => {
    expect(states("InInternationalTransit")[3]).toBe("current");
    expect(buildProgress("Cancelled").exception).toBe("cancelled");
  });
});
```

`custodyChain.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import type { ShipmentEvent } from "@/features/shipments/types";
import { buildCustodyChain, custodySince, formatDuration } from "./custodyChain";

function ev(id: string, timestamp: string, holder: Partial<Pick<ShipmentEvent, "branchId" | "userId" | "riderId">>): ShipmentEvent {
  return { id, eventType: "Sorted", previousStatus: null, newStatus: "Sorted", branchId: null, userId: null, riderId: null, timestamp, notes: null, ...holder };
}

describe("buildCustodyChain", () => {
  const events = [
    ev("3", "2026-10-05T08:00:00Z", { branchId: "ktm", userId: "u2" }),
    ev("1", "2026-10-05T06:00:00Z", { riderId: "r1" }),
    ev("2", "2026-10-05T07:00:00Z", { branchId: "ktm", userId: "u1" }),
    ev("4", "2026-10-05T09:00:00Z", { riderId: "r2", branchId: "ktm" }),
  ];

  it("sorts, collapses same-branch staff events and splits on rider change", () => {
    const chain = buildCustodyChain(events);
    expect(chain.map((s) => s.holderKey)).toEqual(["rider:r1", "branch:ktm", "rider:r2"]);
    expect(chain[1]).toMatchObject({ from: "2026-10-05T07:00:00Z", to: "2026-10-05T09:00:00Z", eventCount: 2, userId: "u2" });
    expect(chain[2].to).toBeNull();
  });

  it("returns an empty chain for no events", () => {
    expect(buildCustodyChain([])).toEqual([]);
    expect(custodySince([])).toBeNull();
  });

  it("custodySince is the start of the current segment", () => {
    expect(custodySince(events)).toBe("2026-10-05T09:00:00Z");
  });

  it("formats durations", () => {
    expect(formatDuration("2026-10-05T08:00:00Z", "2026-10-05T10:18:00Z")).toBe("2 h 18 m");
    expect(formatDuration("2026-10-05T08:00:00Z", "2026-10-05T08:05:00Z")).toBe("5 m");
    expect(formatDuration("2026-10-03T08:00:00Z", "2026-10-05T10:00:00Z")).toBe("2 d 2 h");
    expect(formatDuration("2026-10-05T08:00:00Z", null, new Date("2026-10-05T09:00:00Z"))).toBe("1 h 0 m");
  });
});
```

`eventLabels.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { eventTitle } from "./eventLabels";

describe("eventTitle", () => {
  it("maps known event types", () => {
    expect(eventTitle("ReceivedAtDestination")).toBe("Received at destination");
    expect(eventTitle("OutForDelivery")).toBe("Out for delivery");
  });
  it("humanises unknown types", () => {
    expect(eventTitle("SomethingNewHappened")).toBe("Something new happened");
  });
});
```

`usePeopleNames.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { renderHook } from "@testing-library/react";

let canReadUsers = false;
let canReadRiders = true;
vi.mock("@/features/auth/hooks/usePermission", () => ({
  usePermission: (p: string) => (p === "user.read" ? canReadUsers : p === "rider.read" ? canReadRiders : false),
}));
const useAdminUsers = vi.fn();
vi.mock("@/features/admin/hooks/useAdminUsers", () => ({ useAdminUsers: (...a: unknown[]) => useAdminUsers(...a) }));
const useRiders = vi.fn();
vi.mock("@/features/riders/hooks/useRiders", () => ({ useRiders: (...a: unknown[]) => useRiders(...a) }));
vi.mock("@/features/branches/hooks/useBranches", () => ({ useBranches: () => ({ data: [{ id: "b1", name: "Lalitpur Branch", code: "LAL" }] }) }));

import { usePeopleNames } from "./usePeopleNames";

describe("usePeopleNames", () => {
  it("falls back to generic labels without user.read", () => {
    canReadUsers = false;
    useAdminUsers.mockReturnValue({ data: undefined });
    useRiders.mockReturnValue({ data: [{ id: "r1", userId: "u9", licenseNumber: "L-1", isActive: true }] });
    const { result } = renderHook(() => usePeopleNames());
    expect(result.current.userName("u1")).toBe("Staff");
    expect(result.current.riderName("r1")).toBe("Rider");
    expect(result.current.branchName("b1")).toBe("Lalitpur Branch");
    expect(result.current.branchName(null)).toBe("—");
    expect(useAdminUsers).toHaveBeenCalledWith({ pageSize: 200, enabled: false });
  });

  it("resolves staff and rider names with user.read", () => {
    canReadUsers = true;
    useAdminUsers.mockReturnValue({ data: { items: [{ id: "u9", fullName: "Bikash Tamang" }, { id: "u1", fullName: "Pooja M" }] } });
    useRiders.mockReturnValue({ data: [{ id: "r1", userId: "u9", licenseNumber: "L-1", isActive: true }] });
    const { result } = renderHook(() => usePeopleNames());
    expect(result.current.userName("u1")).toBe("Pooja M");
    expect(result.current.riderName("r1")).toBe("Bikash Tamang");
    expect(result.current.userName("unknown")).toBe("Staff");
  });
});
```

- [ ] **Step 2: Run to verify they fail**

Run: `npx vitest run src/features/shipments/lib src/features/shipments/hooks/usePeopleNames.test.tsx`
Expected: FAIL — modules missing.

- [ ] **Step 3: Implement**

`progress.ts`:

```ts
export const PROGRESS_STEPS = [
  { key: "booked", label: "Booked" },
  { key: "picked-up", label: "Picked up" },
  { key: "origin", label: "Origin hub" },
  { key: "in-transit", label: "In transit" },
  { key: "destination", label: "Destination" },
  { key: "out-for-delivery", label: "Out for delivery" },
  { key: "delivered", label: "Delivered" },
] as const;

export type StepState = "complete" | "current" | "upcoming";
export type ProgressException = "ndr" | "rto" | "cancelled" | null;

// Index of the forward step a status sits at. International statuses ride the in-transit step.
const STEP_INDEX: Record<string, number> = {
  Created: 0, PickupAssigned: 0, PickedUp: 1, AtOriginBranch: 2, Sorted: 2, InTransit: 3,
  CustomsPrepared: 3, HandedToCarrier: 3, InInternationalTransit: 3, CustomsHold: 3,
  InternationalException: 3, CustomsClearance: 3, ReceivedFromCarrier: 3, DeliveredAbroad: 6,
  AtDestinationBranch: 4, OutForDelivery: 5, Delivered: 6,
};

export function buildProgress(status: string): {
  steps: { key: string; label: string; state: StepState }[];
  exception: ProgressException;
  exceptionAfter: number;
} {
  let exception: ProgressException = null;
  let reached: number;
  if (status === "DeliveryFailed" || status === "NdrCreated") {
    exception = "ndr";
    reached = 5;
  } else if (status.startsWith("Rto")) {
    exception = "rto";
    reached = 5;
  } else if (status === "Cancelled") {
    exception = "cancelled";
    reached = 0;
  } else {
    reached = STEP_INDEX[status] ?? 3;
  }

  const delivered = exception === null && reached === 6;
  const steps = PROGRESS_STEPS.map((step, i) => {
    let state: StepState;
    if (delivered || i < reached) state = "complete";
    else if (i === reached && exception === null) state = "current";
    else if (i === reached && exception !== null) state = "complete";
    else state = "upcoming";
    return { key: step.key, label: step.label, state };
  });
  return { steps, exception, exceptionAfter: exception === null ? -1 : reached };
}
```

`custodyChain.ts`:

```ts
import type { ShipmentEvent } from "@/features/shipments/types";

export interface CustodySegment {
  holderKey: string;
  riderId: string | null;
  userId: string | null;
  branchId: string | null;
  from: string;
  to: string | null;
  eventCount: number;
}

// A rider holds a parcel personally; otherwise the branch holds it (whichever staff member
// scanned it). Events with neither keep the previous holder.
function holderOf(e: ShipmentEvent): string | null {
  if (e.riderId) return `rider:${e.riderId}`;
  if (e.branchId) return `branch:${e.branchId}`;
  return null;
}

function sorted(events: ShipmentEvent[]): ShipmentEvent[] {
  return [...events].sort((a, b) => a.timestamp.localeCompare(b.timestamp) || a.id.localeCompare(b.id));
}

export function buildCustodyChain(events: ShipmentEvent[]): CustodySegment[] {
  const chain: CustodySegment[] = [];
  for (const e of sorted(events)) {
    const key = holderOf(e) ?? chain[chain.length - 1]?.holderKey ?? "unknown";
    const current = chain[chain.length - 1];
    if (current && current.holderKey === key) {
      current.eventCount += 1;
      current.userId = e.userId ?? current.userId;
      continue;
    }
    if (current) current.to = e.timestamp;
    chain.push({ holderKey: key, riderId: e.riderId, userId: e.userId, branchId: e.branchId, from: e.timestamp, to: null, eventCount: 1 });
  }
  return chain;
}

export function custodySince(events: ShipmentEvent[]): string | null {
  const chain = buildCustodyChain(events);
  return chain.length ? chain[chain.length - 1].from : null;
}

export function formatDuration(fromIso: string, toIso: string | null, now: Date = new Date()): string {
  const end = toIso ? new Date(toIso).getTime() : now.getTime();
  const minutes = Math.max(0, Math.floor((end - new Date(fromIso).getTime()) / 60000));
  const days = Math.floor(minutes / 1440);
  const hours = Math.floor((minutes % 1440) / 60);
  const mins = minutes % 60;
  if (days > 0) return `${days} d ${hours} h`;
  if (hours > 0) return `${hours} h ${mins} m`;
  return `${mins} m`;
}
```

`eventLabels.ts`:

```ts
const TITLES: Record<string, string> = {
  Created: "Booked",
  PickupAssigned: "Pickup assigned",
  PickedUp: "Picked up",
  ReceivedAtOrigin: "Received at origin hub",
  Sorted: "Sorted",
  Dispatched: "Dispatched",
  ReceivedAtDestination: "Received at destination",
  OutForDelivery: "Out for delivery",
  DeliveryFailed: "Delivery attempt failed",
  Delivered: "Delivered",
  Cancelled: "Cancelled",
  NdrCreated: "NDR opened",
  RtoInitiated: "Return initiated",
  RtoInTransit: "Return in transit",
  RtoAtOrigin: "Return at origin",
  RtoDelivered: "Returned to merchant",
};

export function eventTitle(eventType: string): string {
  if (TITLES[eventType]) return TITLES[eventType];
  const words = eventType.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}
```

`usePeopleNames.ts` — first make `useAdminUsers` accept `enabled` (backwards compatible) by changing its signature in `src/web/src/features/admin/hooks/useAdminUsers.ts` to:

```ts
export function useAdminUsers(params: { page?: number; pageSize?: number; enabled?: boolean } = {}) {
  const { page, pageSize, enabled = true } = params;
```

and adding `enabled,` to its `useQuery({...})` options. Make the same change to `useRiders` (`export function useRiders(options: { enabled?: boolean } = {})`, `enabled: options.enabled ?? true`). Then:

```ts
"use client";

import { usePermission } from "@/features/auth/hooks/usePermission";
import { useAdminUsers } from "@/features/admin/hooks/useAdminUsers";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useRiders } from "@/features/riders/hooks/useRiders";

// Names for ids on events/custody. Only fetches lookups the viewer may read (user.read,
// rider.read) — otherwise generic "Staff"/"Rider" labels. Server-side names are a follow-up.
export function usePeopleNames() {
  const canReadUsers = usePermission("user.read");
  const canReadRiders = usePermission("rider.read");
  const { data: users } = useAdminUsers({ pageSize: 200, enabled: canReadUsers });
  const { data: riders } = useRiders({ enabled: canReadRiders });
  const { data: branches } = useBranches();

  const userById = new Map((users?.items ?? []).map((u) => [u.id, u.fullName]));
  const riderById = new Map((riders ?? []).map((r) => [r.id, r]));

  return {
    branchName: (id: string | null) => (id ? branches?.find((b) => b.id === id)?.name ?? "Unknown branch" : "—"),
    userName: (id: string | null) => (id ? userById.get(id) ?? "Staff" : "—"),
    riderName: (id: string | null) => {
      if (!id) return "—";
      const rider = riderById.get(id);
      return (rider && userById.get(rider.userId)) ?? "Rider";
    },
  };
}
```

Update the `useRiders` mock call expectation accordingly if you add assertions; existing `useRiders()` callers keep working (default `{}`).

- [ ] **Step 4: Run tests**

Run: `npx vitest run src/features/shipments src/features/admin src/features/riders`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/shipments src/web/src/features/admin/hooks/useAdminUsers.ts src/web/src/features/riders/hooks/useRiders.ts
git commit -m "feat(web): shipment progress, custody chain and name resolution helpers (F15 R2)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Shipment detail Sheet redesign

**Files:**
- Create: `src/web/src/features/shipments/components/detail/ProgressStepper.tsx`
- Create: `src/web/src/features/shipments/components/detail/RightNowCard.tsx`
- Create: `src/web/src/features/shipments/components/detail/TimelineTab.tsx`
- Create: `src/web/src/features/shipments/components/detail/CustodyTab.tsx`
- Create: `src/web/src/features/shipments/components/detail/PartiesTab.tsx`
- Modify: `src/web/src/features/shipments/components/ShipmentDetailSheet.tsx` (full rewrite)
- Create: `src/web/src/features/shipments/components/ShipmentDetailSheet.test.tsx`

**Interfaces:**
- Consumes: Task 5 (`buildProgress`, `buildCustodyChain`, `custodySince`, `formatDuration`, `eventTitle`, `usePeopleNames`), Task 3 (`useShipmentEvents`), `SheetHeaderBlock`, `ComingSoonSlot`, `ShipmentNdrRtoLinks`, `useReceiveAtOrigin`.
- Produces: `ProgressStepper({ status }: { status: string })`; `RightNowCard({ shipment, since }: { shipment: Shipment; since: string | null })`; `TimelineTab({ events }: { events: ShipmentEvent[] })`; `CustodyTab({ events }: { events: ShipmentEvent[] })`; `PartiesTab({ shipment }: { shipment: Shipment })`.

- [ ] **Step 1: Write the failing Sheet test**

`src/web/src/features/shipments/components/ShipmentDetailSheet.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, within } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";

vi.mock("@/features/shipments/store/shipmentPanelStore", () => ({
  useShipmentPanelStore: (selector: (s: object) => unknown) => selector({ panel: { mode: "detail", awb: "QA-1" }, close: vi.fn() }),
}));
const useShipment = vi.fn();
vi.mock("@/features/shipments/hooks/useShipment", () => ({ useShipment: () => useShipment() }));
const useShipmentEvents = vi.fn();
vi.mock("@/features/shipments/hooks/useShipmentEvents", () => ({ useShipmentEvents: () => useShipmentEvents() }));
vi.mock("@/features/shipments/hooks/useMerchants", () => ({ useMerchants: () => ({ data: [{ id: "m1", displayName: "Everest Organics" }] }) }));
vi.mock("@/features/shipments/hooks/useServiceTypes", () => ({ useServiceTypes: () => ({ data: [{ id: "s1", name: "Express" }] }) }));
vi.mock("@/features/shipments/hooks/useReceiveAtOrigin", () => ({ useReceiveAtOrigin: () => ({ mutate: vi.fn(), isPending: false }) }));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => p === "shipment.scan" }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({
    branchName: (id: string | null) => (id ? { b1: "Kathmandu Hub", b2: "Lalitpur Branch" }[id] ?? "Unknown branch" : "—"),
    userName: (id: string | null) => (id ? "Staff" : "—"),
    riderName: (id: string | null) => (id ? "Rider" : "—"),
  }),
}));
vi.mock("@/features/shipments/components/ShipmentNdrRtoLinks", () => ({ ShipmentNdrRtoLinks: () => <div>ndr-rto-links</div> }));

import { ShipmentDetailSheet } from "./ShipmentDetailSheet";

const shipment = {
  id: "x1", awb: "QA-1", merchantId: "m1", originBranchId: "b1", destinationBranchId: "b2", currentBranchId: "b2",
  currentCustodianUserId: null, currentCustodianRiderId: "r1", currentStatus: "OutForDelivery", serviceTypeId: "s1",
  weightKg: 1.2, lengthCm: null, widthCm: null, heightCm: null, declaredValue: 3000, codAmount: 2500, wantsInsurance: false,
  deliveryCharge: 150, insuranceCharge: 0, otherCharges: 0, totalCharge: 150, recipientName: "Sita Shrestha",
  recipientPhone: "9841", recipientAddressLine: "Jawalakhel-4", senderName: "Everest", senderPhone: "01", senderAddressLine: "Thamel",
  items: [{ id: "i1", description: "Tea", quantity: 2, unitValue: 500 }], createdAt: "2026-10-05T05:00:00Z", lastEventAt: "2026-10-05T08:00:00Z",
};
const events = [
  { id: "e1", eventType: "Created", previousStatus: null, newStatus: "Created", branchId: "b1", userId: "u1", riderId: null, timestamp: "2026-10-05T05:00:00Z", notes: null },
  { id: "e2", eventType: "OutForDelivery", previousStatus: "AtDestinationBranch", newStatus: "OutForDelivery", branchId: "b2", userId: "u2", riderId: "r1", timestamp: "2026-10-05T08:00:00Z", notes: "run 7" },
];

describe("ShipmentDetailSheet", () => {
  beforeEach(() => {
    useShipment.mockReturnValue({ data: shipment, isLoading: false, isError: false, refetch: vi.fn() });
    useShipmentEvents.mockReturnValue({ data: events, isLoading: false, isError: false, refetch: vi.fn() });
  });

  it("renders header facts, the right-now card and the stepper", () => {
    renderWithIntl(<ShipmentDetailSheet />);
    expect(screen.getByText("AWB · EXPRESS · COD")).toBeInTheDocument();
    const card = screen.getByRole("region", { name: "Current custody" });
    expect(within(card).getByText("Rider")).toBeInTheDocument();
    // Owning branch and destination are both Lalitpur for an out-for-delivery parcel.
    expect(within(card).getAllByText("Lalitpur Branch")).toHaveLength(2);
    const current = screen.getByRole("list", { name: "Progress" }).querySelector("[aria-current='step']");
    expect(current).toHaveTextContent("Out for delivery");
  });

  it("shows the timeline newest first with notes", () => {
    renderWithIntl(<ShipmentDetailSheet />);
    const items = within(screen.getByRole("list", { name: "Timeline" })).getAllByRole("listitem");
    expect(items[0]).toHaveTextContent("Out for delivery");
    expect(items[0]).toHaveTextContent("run 7");
    expect(items[1]).toHaveTextContent("Booked");
  });

  it("renders COD, Attempts and Audit as needs-backend slots", () => {
    renderWithIntl(<ShipmentDetailSheet />);
    for (const name of ["COD", "Attempts", "Audit"]) {
      fireEvent.mouseDown(screen.getByRole("tab", { name }));
      expect(screen.getAllByText("Needs backend").length).toBeGreaterThan(0);
    }
  });

  it("isolates an events error to the timeline", () => {
    useShipmentEvents.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    renderWithIntl(<ShipmentDetailSheet />);
    expect(screen.getByText("Couldn't load the history")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Current custody" })).toBeInTheDocument();
  });

  it("explains a 404 as not found or out of scope", () => {
    useShipment.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    renderWithIntl(<ShipmentDetailSheet />);
    expect(screen.getByText("Shipment not found or not in your branches")).toBeInTheDocument();
  });

  it("offers receive-at-origin only for picked-up shipments", () => {
    useShipment.mockReturnValue({ data: { ...shipment, currentStatus: "PickedUp" }, isLoading: false, isError: false, refetch: vi.fn() });
    renderWithIntl(<ShipmentDetailSheet />);
    expect(screen.getByRole("button", { name: "Receive at origin" })).toBeInTheDocument();
    expect(screen.getByText("Status changes only through scans and rider actions")).toBeInTheDocument();
  });
});
```

`renderWithIntl` lives at `src/web/src/test/renderWithIntl.tsx` (import path `@/test/renderWithIntl`; adjust to a relative import if the alias does not resolve).

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/features/shipments/components/ShipmentDetailSheet.test.tsx`
Expected: FAIL.

- [ ] **Step 3: Implement the detail components**

`detail/ProgressStepper.tsx`:

```tsx
import { Check, AlertTriangle, Undo2, X } from "lucide-react";
import { cn } from "@/lib/utils";
import { buildProgress } from "@/features/shipments/lib/progress";

const EXCEPTION_LABEL = { ndr: "NDR", rto: "RTO", cancelled: "Cancelled" } as const;
const EXCEPTION_ICON = { ndr: AlertTriangle, rto: Undo2, cancelled: X } as const;

export function ProgressStepper({ status }: { status: string }) {
  const { steps, exception, exceptionAfter } = buildProgress(status);
  return (
    <ol aria-label="Progress" className="flex items-start gap-1">
      {steps.map((step, i) => (
        <li key={step.key} aria-current={step.state === "current" ? "step" : undefined} className="flex min-w-0 flex-1 flex-col items-center gap-1.5">
          <span
            className={cn(
              "flex h-[22px] w-[22px] items-center justify-center rounded-full border-2 text-[11px]",
              step.state === "complete" && "border-status-delivered bg-status-delivered text-white",
              step.state === "current" && "border-status-out-for-delivery bg-status-out-for-delivery",
              step.state === "upcoming" && "border-border bg-card"
            )}
          >
            {step.state === "complete" && <Check className="h-3 w-3" aria-hidden="true" />}
          </span>
          <span className={cn("text-center text-[10.5px] leading-tight", step.state === "upcoming" ? "text-muted-foreground" : "text-foreground")}>
            {step.label}
            <span className="sr-only">{` (${step.state})`}</span>
          </span>
          {exception && i === exceptionAfter && (
            <span className="flex items-center gap-1 rounded bg-destructive/10 px-1.5 text-[10.5px] font-medium text-destructive">
              {(() => {
                const Icon = EXCEPTION_ICON[exception];
                return <Icon className="h-3 w-3" aria-hidden="true" />;
              })()}
              {EXCEPTION_LABEL[exception]}
            </span>
          )}
        </li>
      ))}
    </ol>
  );
}
```

`detail/RightNowCard.tsx`:

```tsx
import { MapPin } from "lucide-react";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { formatDuration } from "@/features/shipments/lib/custodyChain";
import type { Shipment } from "@/features/shipments/types";

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="text-[11.5px] text-muted-foreground">{label}</span>
      <span className="text-[13.5px] font-semibold text-foreground">{value}</span>
    </div>
  );
}

export function RightNowCard({ shipment, since }: { shipment: Shipment; since: string | null }) {
  const names = usePeopleNames();
  const custodian = shipment.currentCustodianRiderId
    ? names.riderName(shipment.currentCustodianRiderId)
    : shipment.currentCustodianUserId
      ? names.userName(shipment.currentCustodianUserId)
      : "—";
  return (
    <section aria-label="Current custody" className="space-y-3 rounded-xl border border-primary/25 bg-primary/5 p-4">
      <h3 className="flex items-center gap-2 font-display text-sm font-semibold text-primary">
        <MapPin className="h-4 w-4" aria-hidden="true" />
        Right now
      </h3>
      <div className="grid grid-cols-2 gap-x-4 gap-y-3">
        <Fact label="Custodian" value={custodian} />
        <Fact label="Owning branch" value={shipment.currentBranchId ? names.branchName(shipment.currentBranchId) : "In transit"} />
        <Fact
          label="In custody since"
          value={since ? `${new Date(since).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })} · ${formatDuration(since, null)}` : "—"}
        />
        <Fact label="Destination branch" value={names.branchName(shipment.destinationBranchId)} />
      </div>
    </section>
  );
}
```

`detail/TimelineTab.tsx`:

```tsx
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { eventTitle } from "@/features/shipments/lib/eventLabels";
import { resolveStatusConfig } from "@/components/status-badge/statusConfig";
import type { ShipmentEvent } from "@/features/shipments/types";

export function TimelineTab({ events }: { events: ShipmentEvent[] }) {
  const names = usePeopleNames();
  const newestFirst = [...events].sort((a, b) => b.timestamp.localeCompare(a.timestamp) || b.id.localeCompare(a.id));
  if (newestFirst.length === 0) return <p className="py-6 text-center text-sm text-muted-foreground">No events recorded yet.</p>;
  return (
    <ol aria-label="Timeline" className="flex flex-col">
      {newestFirst.map((e) => {
        const actor = e.riderId ? names.riderName(e.riderId) : e.userId ? names.userName(e.userId) : "System";
        return (
          <li key={e.id} className="flex gap-3.5">
            <div className="flex w-3.5 shrink-0 flex-col items-center">
              <span className="mt-1 h-2.5 w-2.5 rounded-full" style={{ backgroundColor: `hsl(var(--${resolveStatusConfig(e.newStatus).token}))` }} aria-hidden="true" />
              <span className="w-px flex-1 bg-border" aria-hidden="true" />
            </div>
            <div className="flex flex-1 flex-col gap-0.5 pb-3.5">
              <div className="flex justify-between gap-3">
                <span className="text-[13.5px] font-semibold">{eventTitle(e.eventType)}</span>
                <time dateTime={e.timestamp} className="whitespace-nowrap text-xs text-muted-foreground">
                  {new Date(e.timestamp).toLocaleString()}
                </time>
              </div>
              <span className="text-[12.5px] text-foreground/80">{e.branchId ? names.branchName(e.branchId) : "—"}</span>
              <span className="text-xs text-muted-foreground">{actor}</span>
              {e.notes && <span className="text-xs text-muted-foreground">{e.notes}</span>}
            </div>
          </li>
        );
      })}
    </ol>
  );
}
```

Check `resolveStatusConfig`'s export and signature in `src/web/src/components/status-badge/statusConfig.ts` (it is used by `StatusBadge` as `resolveStatusConfig(status, namespace)`); if it is not exported, export it — no behaviour change.

`detail/CustodyTab.tsx`:

```tsx
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { buildCustodyChain, formatDuration } from "@/features/shipments/lib/custodyChain";
import type { ShipmentEvent } from "@/features/shipments/types";

export function CustodyTab({ events }: { events: ShipmentEvent[] }) {
  const names = usePeopleNames();
  const chain = buildCustodyChain(events).reverse();
  if (chain.length === 0) return <p className="py-6 text-center text-sm text-muted-foreground">No custody records yet.</p>;
  return (
    <ol aria-label="Custody chain" className="divide-y divide-border rounded-xl border border-border bg-card">
      {chain.map((segment) => (
        <li key={`${segment.holderKey}-${segment.from}`} className="flex flex-col gap-0.5 px-4 py-3">
          <div className="flex justify-between gap-3">
            <span className="text-[13.5px] font-semibold">
              {segment.riderId ? names.riderName(segment.riderId) : names.branchName(segment.branchId)}
            </span>
            <span className="text-xs tabular-nums text-muted-foreground">{formatDuration(segment.from, segment.to)}</span>
          </div>
          <span className="text-xs text-muted-foreground">
            {new Date(segment.from).toLocaleString()} → {segment.to ? new Date(segment.to).toLocaleString() : "now"}
          </span>
          {segment.riderId && segment.branchId && <span className="text-xs text-muted-foreground">from {names.branchName(segment.branchId)}</span>}
        </li>
      ))}
    </ol>
  );
}
```

`detail/PartiesTab.tsx`:

```tsx
import type { Shipment } from "@/features/shipments/types";

const npr = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function PartiesTab({ shipment }: { shipment: Shipment }) {
  const dims = shipment.lengthCm && shipment.widthCm && shipment.heightCm ? `${shipment.lengthCm} × ${shipment.widthCm} × ${shipment.heightCm} cm` : "—";
  return (
    <div className="space-y-6">
      <div className="grid grid-cols-2 gap-4">
        <Field label="Recipient" value={`${shipment.recipientName} (${shipment.recipientPhone})`} />
        <Field label="Recipient address" value={shipment.recipientAddressLine} />
        <Field label="Sender" value={`${shipment.senderName} (${shipment.senderPhone})`} />
        <Field label="Sender address" value={shipment.senderAddressLine} />
        <Field label="Weight" value={`${shipment.weightKg} kg`} />
        <Field label="Dimensions" value={dims} />
      </div>
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
        <Field label="Declared value" value={npr.format(shipment.declaredValue)} />
        <Field label="COD amount" value={npr.format(shipment.codAmount)} />
        <Field label="Delivery charge" value={npr.format(shipment.deliveryCharge)} />
        <Field label="Insurance charge" value={npr.format(shipment.insuranceCharge)} />
        <Field label="Other charges" value={npr.format(shipment.otherCharges)} />
        <Field label="Total charge" value={npr.format(shipment.totalCharge)} />
      </div>
      <div className="space-y-2">
        <span className="text-xs text-muted-foreground">Items</span>
        <ul className="space-y-1">
          {shipment.items.map((item) => (
            <li key={item.id} className="flex justify-between text-sm">
              <span>{item.description} × {item.quantity}</span>
              <span>{npr.format(item.unitValue)}</span>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Rewrite `ShipmentDetailSheet.tsx`**

```tsx
"use client";

import { Button } from "@/components/ui/button";
import { Sheet, SheetContent } from "@/components/ui/sheet";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { ErrorState } from "@/components/states/ErrorState";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useShipment } from "@/features/shipments/hooks/useShipment";
import { useShipmentEvents } from "@/features/shipments/hooks/useShipmentEvents";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { useReceiveAtOrigin } from "@/features/shipments/hooks/useReceiveAtOrigin";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import { ShipmentNdrRtoLinks } from "@/features/shipments/components/ShipmentNdrRtoLinks";
import { custodySince } from "@/features/shipments/lib/custodyChain";
import { ProgressStepper } from "./detail/ProgressStepper";
import { RightNowCard } from "./detail/RightNowCard";
import { TimelineTab } from "./detail/TimelineTab";
import { CustodyTab } from "./detail/CustodyTab";
import { PartiesTab } from "./detail/PartiesTab";

const rs = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });

export function ShipmentDetailSheet() {
  const panel = useShipmentPanelStore((s) => s.panel);
  const close = useShipmentPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const awb = panel.mode === "detail" ? panel.awb : "";

  const { data: shipment, isLoading, isError, refetch } = useShipment(awb);
  const events = useShipmentEvents(awb);
  const { data: merchants } = useMerchants();
  const { data: serviceTypes } = useServiceTypes();
  const canScan = usePermission("shipment.scan");
  const receiveAtOrigin = useReceiveAtOrigin(shipment?.id ?? "");

  const serviceName = shipment ? serviceTypes?.find((s) => s.id === shipment.serviceTypeId)?.name ?? "Service" : "";
  const merchantName = shipment ? merchants?.find((m) => m.id === shipment.merchantId)?.displayName ?? "—" : "—";

  const historyError = (
    <ErrorState title="Couldn't load the history" description="The shipment's events could not be fetched." onRetry={() => events.refetch()} />
  );

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="flex w-full flex-col gap-0 p-0 sm:max-w-[580px]">
        <div className="flex-1 space-y-5 overflow-y-auto p-6">
          {isLoading && <DetailSkeleton fieldRows={5} />}
          {isError && (
            <ErrorState
              title="Shipment not found or not in your branches"
              description="Check the AWB, or ask an admin for access to the branch that holds it."
              onRetry={() => refetch()}
            />
          )}
          {shipment && (
            <>
              <SheetHeaderBlock
                eyebrow={`AWB · ${serviceName.toUpperCase()} · ${shipment.codAmount > 0 ? "COD" : "PREPAID"}`}
                title={shipment.awb}
                mono
                status={<StatusBadge status={shipment.currentStatus} />}
                facts={[
                  { label: "Merchant", value: merchantName },
                  { label: "COD", value: shipment.codAmount > 0 ? `Rs ${rs.format(shipment.codAmount)}` : "—" },
                  { label: "Weight", value: `${shipment.weightKg} kg` },
                  { label: "Charge", value: `Rs ${rs.format(shipment.totalCharge)}` },
                ]}
              />
              <RightNowCard shipment={shipment} since={events.data ? custodySince(events.data) : null} />
              <ProgressStepper status={shipment.currentStatus} />
              {(shipment.currentStatus === "NdrCreated" || shipment.currentStatus.startsWith("Rto")) && (
                <ShipmentNdrRtoLinks shipmentId={shipment.id} />
              )}
              <Tabs defaultValue="timeline">
                <TabsList className="w-full justify-start overflow-x-auto">
                  <TabsTrigger value="timeline">Timeline</TabsTrigger>
                  <TabsTrigger value="custody">Custody chain</TabsTrigger>
                  <TabsTrigger value="cod">COD</TabsTrigger>
                  <TabsTrigger value="attempts">Attempts</TabsTrigger>
                  <TabsTrigger value="parties">Parties</TabsTrigger>
                  <TabsTrigger value="audit">Audit</TabsTrigger>
                </TabsList>
                <TabsContent value="timeline" className="pt-4">
                  {events.isLoading ? <DetailSkeleton fieldRows={4} /> : events.isError ? historyError : <TimelineTab events={events.data ?? []} />}
                </TabsContent>
                <TabsContent value="custody" className="pt-4">
                  {events.isLoading ? <DetailSkeleton fieldRows={3} /> : events.isError ? historyError : <CustodyTab events={events.data ?? []} />}
                </TabsContent>
                <TabsContent value="cod" className="pt-4">
                  <ComingSoonSlot title="COD for this shipment" description="Collection, rider deposit and settlement status for this parcel." />
                </TabsContent>
                <TabsContent value="attempts" className="pt-4">
                  <ComingSoonSlot title="Delivery attempts" description="Each attempt with reason, rider, time and next action." />
                </TabsContent>
                <TabsContent value="parties" className="pt-4">
                  <PartiesTab shipment={shipment} />
                </TabsContent>
                <TabsContent value="audit" className="pt-4">
                  <ComingSoonSlot title="Audit trail" description="Edits and corrections to this shipment with before/after values." />
                </TabsContent>
              </Tabs>
            </>
          )}
        </div>
        {shipment && (
          <div className="flex items-center gap-2.5 border-t border-border bg-surface-sunken px-6 py-3.5">
            <span className="flex-1 text-[11.5px] text-muted-foreground">Status changes only through scans and rider actions</span>
            {canScan && shipment.currentStatus === "PickedUp" && (
              <Button size="sm" disabled={receiveAtOrigin.isPending} onClick={() => receiveAtOrigin.mutate()}>
                {receiveAtOrigin.isPending ? "Receiving…" : "Receive at origin"}
              </Button>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
```

`SheetHeaderBlock` renders a `SheetTitle`, satisfying Radix's dialog-title requirement once the shipment loads; while loading/erroring, add `<SheetTitle className="sr-only">Shipment</SheetTitle>` (import `SheetTitle`) inside the scroll container when `!shipment`.

- [ ] **Step 5: Run tests**

Run: `npx vitest run src/features/shipments`
Expected: PASS. Fix only selector details in the test if Radix markup differs (e.g. stepper `aria-current` query) — do not weaken what is asserted.

- [ ] **Step 6: Type-check, lint, commit**

Run: `npx tsc --noEmit` and `npm run lint`.

```bash
git add src/web/src/features/shipments src/web/src/components/status-badge/statusConfig.ts
git commit -m "feat(web): shipment sheet with custody card, stepper, timeline and custody chain (F15 R2)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Dashboard model (pure)

**Files:**
- Create: `src/web/src/features/reports/lib/dashboardModel.ts`, `dashboardModel.test.ts`

**Interfaces:**
- Consumes: `PipelineStage` (`@/components/page/PipelineStrip`), `OrgDashboardSummary`, `CodFinancialSummary` (`@/features/reports/types`), `sumStatuses` (Task 3).
- Produces: `buildPipelineStages(counts: Record<string, number> | undefined, branchPerformance: Record<string, BranchPerformance> | undefined): PipelineStage[]`; `codSegments(f: CodFinancialSummary): { key: "settled" | "collected" | "uncollected"; label: string; value: number; token: string }[]`; `formatRate(v: number | null): string`; `formatTimeSpan(v: string | null): string`; `openNdrCount(byStatus: Record<string, number> | undefined): number`; `type DashboardRange = { preset: RangePreset | "custom"; from: string; to: string }`; `rangeLabel(r: DashboardRange): string`; `rangeToApi(r: DashboardRange, now?: Date): { from?: string; to?: string }`.

- [ ] **Step 1: Write the failing test**

```ts
import { describe, expect, it } from "vitest";
import { buildPipelineStages, codSegments, formatRate, formatTimeSpan, openNdrCount, rangeLabel, rangeToApi } from "./dashboardModel";

describe("dashboardModel", () => {
  it("buckets current WIP into pipeline stages with tab links", () => {
    const stages = buildPipelineStages(
      { Created: 2, PickupAssigned: 1, PickedUp: 4, AtOriginBranch: 3, Sorted: 2, InTransit: 7, AtDestinationBranch: 5, OutForDelivery: 6, Delivered: 99 },
      { b1: { deliverySuccesses: 10 } as never, b2: { deliverySuccesses: 5 } as never }
    );
    expect(stages.map((s) => [s.label, s.count])).toEqual([
      ["Booked", 3], ["Picked up", 4], ["At origin", 5], ["In transit", 7], ["At destination", 5], ["Out for delivery", 6], ["Delivered", 15],
    ]);
    expect(stages[0].href).toBe("/shipments?status=Created&status=PickupAssigned");
    expect(stages[3].href).toBe("/shipments?tab=in-transit");
    expect(stages[6].href).toBeUndefined();
    expect(stages[6].caption).toBe("in range");
  });

  it("handles missing data as zero", () => {
    expect(buildPipelineStages(undefined, undefined).every((s) => s.count === 0)).toBe(true);
  });

  it("splits COD into non-negative segments", () => {
    const segs = codSegments({ expected: 1000, collected: 700, pending: 0, settled: 900, merchantPayable: 0 });
    expect(segs.map((s) => [s.key, s.value])).toEqual([["settled", 900], ["collected", 0], ["uncollected", 300]]);
  });

  it("formats rates and .NET TimeSpans", () => {
    expect(formatRate(0.914)).toBe("91.4%");
    expect(formatRate(null)).toBe("—");
    expect(formatTimeSpan("1.19:12:00")).toBe("1.8 days");
    expect(formatTimeSpan("05:30:00.1234")).toBe("5.5 h");
    expect(formatTimeSpan(null)).toBe("—");
    expect(formatTimeSpan("garbage")).toBe("—");
  });

  it("counts non-resolved NDRs", () => {
    expect(openNdrCount({ Open: 2, Contacting: 1, Resolved: 9 })).toBe(3);
    expect(openNdrCount(undefined)).toBe(0);
  });

  it("labels and converts ranges", () => {
    const now = new Date(2026, 9, 5, 10);
    expect(rangeLabel({ preset: "7d", from: "", to: "" })).toBe("Last 7 days");
    expect(rangeToApi({ preset: "today", from: "", to: "" }, now)).toEqual({ from: "2026-10-05T00:00:00Z", to: "2026-10-05T23:59:59Z" });
    expect(rangeToApi({ preset: "custom", from: "2026-10-01", to: "" }, now)).toEqual({ from: "2026-10-01T00:00:00Z", to: undefined });
    expect(rangeLabel({ preset: "custom", from: "2026-10-01", to: "2026-10-03" })).toBe("2026-10-01 – 2026-10-03");
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/features/reports/lib`
Expected: FAIL.

- [ ] **Step 3: Implement**

```ts
import type { PipelineStage } from "@/components/page/PipelineStrip";
import { presetDays, toUtcRange, type RangePreset } from "@/lib/dates/rangePresets";
import { sumStatuses } from "@/features/shipments/lib/statusGroups";
import type { BranchPerformance, CodFinancialSummary } from "@/features/reports/types";

const STAGES: { key: string; label: string; statuses: string[]; token: string; href: string }[] = [
  { key: "booked", label: "Booked", statuses: ["Created", "PickupAssigned"], token: "status-created", href: "/shipments?status=Created&status=PickupAssigned" },
  { key: "picked-up", label: "Picked up", statuses: ["PickedUp"], token: "status-in-transit", href: "/shipments?status=PickedUp" },
  { key: "origin", label: "At origin", statuses: ["AtOriginBranch", "Sorted"], token: "status-in-transit", href: "/shipments?status=AtOriginBranch&status=Sorted" },
  { key: "in-transit", label: "In transit", statuses: ["InTransit"], token: "status-in-transit", href: "/shipments?tab=in-transit" },
  { key: "destination", label: "At destination", statuses: ["AtDestinationBranch"], token: "status-in-transit", href: "/shipments?status=AtDestinationBranch" },
  { key: "out-for-delivery", label: "Out for delivery", statuses: ["OutForDelivery"], token: "status-out-for-delivery", href: "/shipments?tab=out-for-delivery" },
];

export function buildPipelineStages(
  counts: Record<string, number> | undefined,
  branchPerformance: Record<string, BranchPerformance> | undefined
): PipelineStage[] {
  const delivered = Object.values(branchPerformance ?? {}).reduce((t, b) => t + (b.deliverySuccesses ?? 0), 0);
  return [
    ...STAGES.map((s) => ({ key: s.key, label: s.label, count: sumStatuses(counts, s.statuses), token: s.token, href: s.href })),
    { key: "delivered", label: "Delivered", count: delivered, token: "status-delivered", caption: "in range" },
  ];
}

export function codSegments(f: CodFinancialSummary) {
  return [
    { key: "settled" as const, label: "Settled", value: Math.max(0, f.settled), token: "status-delivered" },
    { key: "collected" as const, label: "Collected, not settled", value: Math.max(0, f.collected - f.settled), token: "status-out-for-delivery" },
    { key: "uncollected" as const, label: "Not yet collected", value: Math.max(0, f.expected - f.collected), token: "status-created" },
  ];
}

export function formatRate(v: number | null): string {
  return v === null ? "—" : `${(v * 100).toFixed(1)}%`;
}

// .NET TimeSpan JSON: "[d.]hh:mm:ss[.fffffff]".
export function formatTimeSpan(v: string | null): string {
  if (v === null) return "—";
  const m = v.match(/^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})(?:\.\d+)?$/);
  if (!m) return "—";
  const hours = (m[1] ? Number(m[1]) * 24 : 0) + Number(m[2]) + Number(m[3]) / 60;
  return hours >= 24 ? `${(hours / 24).toFixed(1)} days` : `${hours.toFixed(1)} h`;
}

export function openNdrCount(byStatus: Record<string, number> | undefined): number {
  return Object.entries(byStatus ?? {}).reduce((t, [status, n]) => (status === "Resolved" ? t : t + n), 0);
}

export type DashboardRange = { preset: RangePreset | "custom"; from: string; to: string };

const PRESET_LABEL: Record<RangePreset, string> = { today: "Today", "7d": "Last 7 days", "30d": "Last 30 days" };

export function rangeLabel(r: DashboardRange): string {
  if (r.preset !== "custom") return PRESET_LABEL[r.preset];
  if (r.from && r.to) return `${r.from} – ${r.to}`;
  return r.from ? `Since ${r.from}` : r.to ? `Until ${r.to}` : "All time";
}

export function rangeToApi(r: DashboardRange, now: Date = new Date()): { from?: string; to?: string } {
  const days = r.preset === "custom" ? { from: r.from, to: r.to } : presetDays(r.preset, now);
  return toUtcRange(days.from, days.to);
}
```

- [ ] **Step 4: Run tests, commit**

Run: `npx vitest run src/features/reports/lib` → PASS.

```bash
git add src/web/src/features/reports/lib
git commit -m "feat(web): dashboard model for pipeline, COD split, rates and ranges (F15 R2)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Dashboard page ("Operations overview")

**Files:**
- Create: `src/web/src/features/reports/components/dashboard/DashboardRangeControl.tsx`
- Create: `src/web/src/features/reports/components/dashboard/NeedsActionCard.tsx`
- Create: `src/web/src/features/reports/components/dashboard/CodRangeCard.tsx`
- Create: `src/web/src/features/reports/components/dashboard/BranchPerformanceTable.tsx`
- Create: `src/web/src/features/reports/components/dashboard/RiderPerformanceTable.tsx`
- Modify: `src/web/src/app/(staff)/dashboard/page.tsx` (full rewrite)
- Create: `src/web/src/app/(staff)/dashboard/page.test.tsx`
- Delete: `features/reports/components/{QuickStatsStrip,ShipmentFunnel,DeliveryRateHero,RateTiles,BranchPerformanceChart,RiderPerformanceChart,FinancialSummary,DashboardHeader}.tsx` and their `.test.tsx` files — each only after `grep -rn "<Name>" src` shows no remaining importer.

**Interfaces:**
- Consumes: Task 7 model; `useOrgDashboard({from,to})`, `useShipmentsSummary()`, `useNdrsSummary()`, `useReturnShipmentsSummary()`, `useManifestsSummary()`, `useBranches()`, `usePeopleNames()` (Task 5), `usePermission`, `useShipmentPanelStore`, `ExportReportsCard`, R1 primitives.
- Produces: `DashboardRangeControl({ value, onChange }: { value: DashboardRange; onChange(r: DashboardRange): void })`; `NeedsActionCard()`; `CodRangeCard({ financial }: { financial: CodFinancialSummary })`; `BranchPerformanceTable({ data }: { data: Record<string, BranchPerformance> })`; `RiderPerformanceTable({ data }: { data: RiderPerformance[] })`.

- [ ] **Step 1: Write the failing page test**

`src/web/src/app/(staff)/dashboard/page.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, within } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";

const useOrgDashboard = vi.fn();
vi.mock("@/features/reports/hooks/useOrgDashboard", () => ({ useOrgDashboard: (p: unknown) => useOrgDashboard(p) }));
vi.mock("@/features/shipments/hooks/useShipmentsSummary", () => ({
  useShipmentsSummary: () => ({ data: { Created: 2, InTransit: 7, OutForDelivery: 6 }, isLoading: false, isError: false }),
}));
const ndr = vi.fn();
vi.mock("@/features/ndr/hooks/useNdrsSummary", () => ({ useNdrsSummary: () => ndr() }));
vi.mock("@/features/ndr/hooks/useReturnShipmentsSummary", () => ({
  useReturnShipmentsSummary: () => ({ data: { byRtoStage: {}, merchantConfirmed: 0, pendingConfirmation: 4 }, isLoading: false, isError: false }),
}));
vi.mock("@/features/manifests/hooks/useManifestsSummary", () => ({ useManifestsSummary: () => ({ data: { InTransit: 2 }, isLoading: false, isError: false }) }));
vi.mock("@/features/branches/hooks/useBranches", () => ({ useBranches: () => ({ data: [{ id: "b1", name: "Kathmandu Hub", code: "KTM" }] }) }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ riderName: () => "Rider", userName: () => "Staff", branchName: () => "Kathmandu Hub" }),
}));
let perms = new Set(["report.read", "ndr.read", "rto.read", "manifest.read", "cod.read", "shipment.create"]);
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => perms.has(p) }));
vi.mock("@/features/shipments/store/shipmentPanelStore", () => ({
  useShipmentPanelStore: (selector: (s: object) => unknown) => selector({ openCreate: vi.fn() }),
}));
vi.mock("@/features/reports/components/ExportReportsCard", () => ({ ExportReportsCard: () => <div>export-card</div> }));

import DashboardPage from "./page";

const summary = {
  todaysShipmentsByStatus: {},
  financial: { expected: 1000, collected: 700, pending: 300, settled: 500, merchantPayable: 450 },
  branchPerformance: {
    b1: { shipmentVolume: 40, deliveryAttempts: 20, deliverySuccesses: 18, deliverySuccessRate: 0.9, ndrCount: 2, ndrRate: 0.1, rtoCount: 1, rtoRate: 0.05 },
  },
  riderPerformance: [{ riderId: "r1", attempts: 10, successes: 9, successRate: 0.9 }],
  overallDeliverySuccessRate: 0.914,
  overallNdrRate: 0.052,
  overallRtoRate: null,
  averageDeliveryTime: "1.19:12:00",
};

describe("DashboardPage", () => {
  beforeEach(() => {
    perms = new Set(["report.read", "ndr.read", "rto.read", "manifest.read", "cod.read", "shipment.create"]);
    useOrgDashboard.mockReset().mockReturnValue({ data: summary, isLoading: false, isError: false, refetch: vi.fn() });
    ndr.mockReturnValue({ data: { Open: 3, Resolved: 9 }, isLoading: false, isError: false });
  });

  it("renders the pipeline from current WIP plus delivered in range", () => {
    renderWithIntl(<DashboardPage />);
    const pipeline = screen.getByRole("list", { name: "Parcel pipeline" });
    expect(within(pipeline).getByRole("link", { name: /In transit\s*7/ })).toHaveAttribute("href", "/shipments?tab=in-transit");
    expect(within(pipeline).getByText("18")).toBeInTheDocument();
  });

  it("renders KPI tiles without deltas", () => {
    renderWithIntl(<DashboardPage />);
    expect(screen.getByText("91.4%")).toBeInTheDocument();
    expect(screen.getByText("1.8 days")).toBeInTheDocument();
    expect(document.querySelector("[data-tone]")).toBeNull();
  });

  it("shows needs-action rows the user may see and a slot for the rest", () => {
    renderWithIntl(<DashboardPage />);
    const card = screen.getByRole("region", { name: "Needs action" });
    expect(within(card).getByRole("link", { name: /3\s*Open NDRs/ })).toHaveAttribute("href", "/ndrs");
    expect(within(card).getByRole("link", { name: /4\s*RTOs awaiting merchant sign-off/ })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Hourly flow" })).toHaveAttribute("data-coming-soon");
  });

  it("hides a needs-action row without its permission", () => {
    perms.delete("ndr.read");
    renderWithIntl(<DashboardPage />);
    expect(screen.queryByText("Open NDRs")).toBeNull();
  });

  it("keeps the page when one summary endpoint fails", () => {
    ndr.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    renderWithIntl(<DashboardPage />);
    expect(screen.getByRole("list", { name: "Parcel pipeline" })).toBeInTheDocument();
    expect(screen.getByText("Branch performance")).toBeInTheDocument();
  });

  it("switches range and sends UTC-anchored bounds", () => {
    renderWithIntl(<DashboardPage />);
    fireEvent.mouseDown(screen.getByRole("tab", { name: "7 days" }));
    const last = useOrgDashboard.mock.calls.at(-1)![0] as { from: string; to: string };
    expect(last.from).toMatch(/^\d{4}-\d{2}-\d{2}T00:00:00Z$/);
    expect(last.to).toMatch(/^\d{4}-\d{2}-\d{2}T23:59:59Z$/);
  });

  it("shows an error state when the org dashboard fails but keeps the pipeline", () => {
    useOrgDashboard.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    renderWithIntl(<DashboardPage />);
    expect(screen.getByText("Couldn't load performance data")).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Parcel pipeline" })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run "src/app/(staff)/dashboard"`
Expected: FAIL.

- [ ] **Step 3: Implement the dashboard components**

`DashboardRangeControl.tsx`:

```tsx
"use client";

import { DatePicker } from "@/components/ui/date-picker";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import type { DashboardRange } from "@/features/reports/lib/dashboardModel";

const OPTIONS: { value: DashboardRange["preset"]; label: string }[] = [
  { value: "today", label: "Today" },
  { value: "7d", label: "7 days" },
  { value: "30d", label: "30 days" },
  { value: "custom", label: "Custom" },
];

export function DashboardRangeControl({ value, onChange }: { value: DashboardRange; onChange: (r: DashboardRange) => void }) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <Tabs value={value.preset} onValueChange={(preset) => onChange({ preset: preset as DashboardRange["preset"], from: "", to: "" })}>
        <TabsList aria-label="Date range">
          {OPTIONS.map((o) => (
            <TabsTrigger key={o.value} value={o.value}>
              {o.label}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>
      {value.preset === "custom" && (
        <>
          <DatePicker id="dashboard-from" aria-label="From date" value={value.from} onChange={(from) => onChange({ ...value, from })} clearable />
          <DatePicker id="dashboard-to" aria-label="To date" value={value.to} onChange={(to) => onChange({ ...value, to })} clearable />
        </>
      )}
    </div>
  );
}
```

`NeedsActionCard.tsx`:

```tsx
"use client";

import Link from "next/link";
import { ChevronRight } from "lucide-react";
import { SectionCard } from "@/components/page/SectionCard";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { cn } from "@/lib/utils";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useNdrsSummary } from "@/features/ndr/hooks/useNdrsSummary";
import { useReturnShipmentsSummary } from "@/features/ndr/hooks/useReturnShipmentsSummary";
import { useManifestsSummary } from "@/features/manifests/hooks/useManifestsSummary";
import { openNdrCount } from "@/features/reports/lib/dashboardModel";

interface Row {
  key: string;
  label: string;
  description: string;
  href: string;
  count: number | undefined;
  visible: boolean;
}

export function NeedsActionCard() {
  const canNdr = usePermission("ndr.read");
  const canRto = usePermission("rto.read");
  const canManifest = usePermission("manifest.read");
  const ndrs = useNdrsSummary();
  const rtos = useReturnShipmentsSummary();
  const manifests = useManifestsSummary();

  const rows: Row[] = [
    { key: "ndr", label: "Open NDRs", description: "Delivery failed, awaiting resolution", href: "/ndrs", count: ndrs.data ? openNdrCount(ndrs.data) : undefined, visible: canNdr && !ndrs.isError },
    { key: "rto", label: "RTOs awaiting merchant sign-off", description: "Returned to origin, not confirmed", href: "/return-shipments", count: rtos.data?.pendingConfirmation, visible: canRto && !rtos.isError },
    { key: "manifests", label: "Manifests in transit", description: "Dispatched and on the road", href: "/manifests", count: manifests.data?.InTransit ?? (manifests.data ? 0 : undefined), visible: canManifest && !manifests.isError },
  ];

  return (
    <section aria-label="Needs action">
      <SectionCard title="Needs action" className="h-full">
        <ul className="flex flex-col gap-1">
          {rows
            .filter((r) => r.visible)
            .map((r) => (
              <li key={r.key}>
                <Link href={r.href} className="flex items-center gap-3 rounded-lg px-2 py-2.5 hover:bg-accent">
                  <span className={cn("min-w-9 text-center font-display text-lg font-semibold tabular-nums", r.count === 0 && "text-muted-foreground")}>
                    {r.count === undefined ? "…" : r.count}
                  </span>
                  <span className="flex flex-1 flex-col">
                    <span className={cn("text-[13.5px] font-medium", r.count === 0 && "text-muted-foreground")}>{r.label}</span>
                    <span className="text-xs text-muted-foreground">{r.description}</span>
                  </span>
                  <ChevronRight className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
                </Link>
              </li>
            ))}
        </ul>
        <ComingSoonSlot
          className="mt-3 p-4"
          title="More exceptions"
          description="Manifests with missing parcels, riders holding cash past 24 h, and express parcels at SLA risk."
        />
      </SectionCard>
    </section>
  );
}
```

`CodRangeCard.tsx`:

```tsx
"use client";

import Link from "next/link";
import { ReceiptText } from "lucide-react";
import { SectionCard } from "@/components/page/SectionCard";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { codSegments } from "@/features/reports/lib/dashboardModel";
import type { CodFinancialSummary } from "@/features/reports/types";

const npr = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR", maximumFractionDigits: 0 });

export function CodRangeCard({ financial, rangeLabel }: { financial: CodFinancialSummary; rangeLabel: string }) {
  const canCod = usePermission("cod.read");
  const segments = codSegments(financial);
  const total = segments.reduce((t, s) => t + s.value, 0);
  return (
    <SectionCard title="COD" description={rangeLabel} action={<span className="text-xs text-muted-foreground">Expected <b className="text-foreground">{npr.format(financial.expected)}</b></span>}>
      <div className="flex h-2.5 gap-0.5 overflow-hidden rounded-full bg-muted" aria-hidden="true">
        {total > 0 && segments.map((s) => (
          <span key={s.key} style={{ width: `${(s.value / total) * 100}%`, backgroundColor: `hsl(var(--${s.token}))` }} />
        ))}
      </div>
      <dl className="mt-3 space-y-1.5">
        {segments.map((s) => (
          <div key={s.key} className="flex items-center justify-between text-[13px]">
            <dt className="flex items-center gap-2 text-muted-foreground">
              <span className="h-2 w-2 rounded-full" style={{ backgroundColor: `hsl(var(--${s.token}))` }} aria-hidden="true" />
              {s.label}
            </dt>
            <dd className="font-medium tabular-nums">{npr.format(s.value)}</dd>
          </div>
        ))}
      </dl>
      <div className="mt-4 flex items-center gap-3 rounded-lg bg-surface-sunken p-3">
        <ReceiptText className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
        <span className="flex flex-1 flex-col">
          <span className="text-xs text-muted-foreground">Merchant payable</span>
          <span className="text-[15px] font-semibold tabular-nums">{npr.format(financial.merchantPayable)}</span>
        </span>
        {canCod && (
          <Link href="/cod" className="text-[13px] font-medium text-primary hover:underline">
            Review
          </Link>
        )}
      </div>
      <ComingSoonSlot className="mt-3 p-4" title="Rider-held vs deposited" description="How much collected cash riders still hold versus deposited but unverified." />
    </SectionCard>
  );
}
```

`BranchPerformanceTable.tsx`:

```tsx
"use client";

import { SectionCard } from "@/components/page/SectionCard";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { formatRate } from "@/features/reports/lib/dashboardModel";
import type { BranchPerformance } from "@/features/reports/types";

export function BranchPerformanceTable({ data }: { data: Record<string, BranchPerformance> }) {
  const { data: branches } = useBranches();
  const rows = Object.entries(data).sort(([, a], [, b]) => b.shipmentVolume - a.shipmentVolume);
  return (
    <SectionCard title="Branch performance">
      {rows.length === 0 ? (
        <p className="py-6 text-center text-sm text-muted-foreground">No branch activity in this range.</p>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Branch</TableHead>
                <TableHead className="text-right">Volume</TableHead>
                <TableHead className="text-right">Delivered</TableHead>
                <TableHead>Success rate</TableHead>
                <TableHead className="text-right">NDR</TableHead>
                <TableHead className="text-right">RTO</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map(([id, b]) => {
                const branch = branches?.find((x) => x.id === id);
                const pct = b.deliverySuccessRate === null ? 0 : Math.round(b.deliverySuccessRate * 100);
                return (
                  <TableRow key={id}>
                    <TableCell>
                      <div className="flex flex-col">
                        <span className="font-medium">{branch?.name ?? "Unknown branch"}</span>
                        <span className="font-mono text-[11px] text-muted-foreground">{branch?.code ?? ""}</span>
                      </div>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{b.shipmentVolume}</TableCell>
                    <TableCell className="text-right tabular-nums">{b.deliverySuccesses}</TableCell>
                    <TableCell>
                      <div className="flex items-center gap-2">
                        <span className="h-1.5 w-20 rounded-full bg-muted" aria-hidden="true">
                          <span className="block h-1.5 rounded-full bg-status-delivered" style={{ width: `${pct}%` }} />
                        </span>
                        <span className="text-xs tabular-nums">{formatRate(b.deliverySuccessRate)}</span>
                      </div>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{b.ndrCount}</TableCell>
                    <TableCell className="text-right tabular-nums">{b.rtoCount}</TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}
    </SectionCard>
  );
}
```

`RiderPerformanceTable.tsx`:

```tsx
"use client";

import { SectionCard } from "@/components/page/SectionCard";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { formatRate } from "@/features/reports/lib/dashboardModel";
import type { RiderPerformance } from "@/features/reports/types";

export function RiderPerformanceTable({ data }: { data: RiderPerformance[] }) {
  const names = usePeopleNames();
  const top = [...data].sort((a, b) => b.successes - a.successes).slice(0, 5);
  return (
    <SectionCard title="Rider performance" description="Top 5 by deliveries">
      {top.length === 0 ? (
        <p className="py-6 text-center text-sm text-muted-foreground">No rider activity in this range.</p>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Rider</TableHead>
                <TableHead className="text-right">Attempts</TableHead>
                <TableHead className="text-right">Delivered</TableHead>
                <TableHead className="text-right">Success rate</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {top.map((r) => {
                const name = names.riderName(r.riderId);
                return (
                  <TableRow key={r.riderId}>
                    <TableCell>{name === "Rider" ? `Rider ${r.riderId.slice(0, 6)}` : name}</TableCell>
                    <TableCell className="text-right tabular-nums">{r.attempts}</TableCell>
                    <TableCell className="text-right tabular-nums">{r.successes}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatRate(r.successRate)}</TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}
    </SectionCard>
  );
}
```

- [ ] **Step 4: Rewrite `app/(staff)/dashboard/page.tsx`**

```tsx
"use client";

import { useState } from "react";
import { Download, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { Skeleton } from "@/components/ui/skeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { PageHeader } from "@/components/page/PageHeader";
import { PipelineStrip } from "@/components/page/PipelineStrip";
import { KpiTile } from "@/components/page/KpiTile";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useOrgDashboard } from "@/features/reports/hooks/useOrgDashboard";
import { useShipmentsSummary } from "@/features/shipments/hooks/useShipmentsSummary";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import { ExportReportsCard } from "@/features/reports/components/ExportReportsCard";
import { DashboardRangeControl } from "@/features/reports/components/dashboard/DashboardRangeControl";
import { NeedsActionCard } from "@/features/reports/components/dashboard/NeedsActionCard";
import { CodRangeCard } from "@/features/reports/components/dashboard/CodRangeCard";
import { BranchPerformanceTable } from "@/features/reports/components/dashboard/BranchPerformanceTable";
import { RiderPerformanceTable } from "@/features/reports/components/dashboard/RiderPerformanceTable";
import {
  buildPipelineStages, formatRate, formatTimeSpan, rangeLabel, rangeToApi, type DashboardRange,
} from "@/features/reports/lib/dashboardModel";

export default function DashboardPage() {
  const [range, setRange] = useState<DashboardRange>({ preset: "30d", from: "", to: "" });
  const org = useOrgDashboard(rangeToApi(range));
  const summary = useShipmentsSummary();
  const { data: branches } = useBranches();
  const canCreate = usePermission("shipment.create");
  const openCreate = useShipmentPanelStore((s) => s.openCreate);
  const label = rangeLabel(range);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Operations overview"
        description={branches ? `${branches.length} branches in scope` : undefined}
        actions={
          <>
            <DashboardRangeControl value={range} onChange={setRange} />
            <Popover>
              <PopoverTrigger asChild>
                <Button variant="outline" className="gap-1.5">
                  <Download className="h-4 w-4" aria-hidden="true" />
                  Export
                </Button>
              </PopoverTrigger>
              <PopoverContent align="end" className="w-[min(92vw,420px)] p-0">
                <ExportReportsCard />
              </PopoverContent>
            </Popover>
            {canCreate && (
              <Button className="gap-1.5" onClick={() => openCreate()}>
                <Plus className="h-4 w-4" aria-hidden="true" />
                Book shipment
              </Button>
            )}
          </>
        }
      />

      <section className="space-y-3">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h2 className="font-display text-base font-semibold">Parcel pipeline</h2>
          <span className="text-xs text-muted-foreground">Parcels currently in each stage · delivered {label.toLowerCase()}</span>
        </div>
        {summary.isError ? (
          <ErrorState title="Couldn't load the pipeline" description="Current status counts are unavailable." />
        ) : (
          <PipelineStrip ariaLabel="Parcel pipeline" stages={buildPipelineStages(summary.data, org.data?.branchPerformance)} />
        )}
      </section>

      {org.isLoading && (
        <div role="status" aria-live="polite" className="grid grid-cols-2 gap-4 lg:grid-cols-4">
          <span className="sr-only">Loading</span>
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}
        </div>
      )}
      {!org.isLoading && (org.isError || !org.data) && (
        <ErrorState title="Couldn't load performance data" description="Rates, COD and branch performance are unavailable." onRetry={() => org.refetch()} />
      )}
      {org.data && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <KpiTile label="Delivery success rate" value={formatRate(org.data.overallDeliverySuccessRate)} note={label} />
          <KpiTile label="NDR rate" value={formatRate(org.data.overallNdrRate)} note={label} />
          <KpiTile label="RTO rate" value={formatRate(org.data.overallRtoRate)} note={label} />
          <KpiTile label="Avg. booking to delivery" value={formatTimeSpan(org.data.averageDeliveryTime)} note={label} />
        </div>
      )}

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        <ComingSoonSlot title="Hourly flow" description="Booked vs delivered parcels by hour of the day." minHeight={260} />
        <NeedsActionCard />
      </div>

      {org.data && (
        <>
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]">
            <CodRangeCard financial={org.data.financial} rangeLabel={label} />
            <BranchPerformanceTable data={org.data.branchPerformance} />
          </div>
          <RiderPerformanceTable data={org.data.riderPerformance} />
        </>
      )}
    </div>
  );
}
```

Check `ExportReportsCard` renders acceptably inside a popover (it is a `Card`); if its card border doubles up visually, pass nothing and accept it — do not refactor it in this task.

- [ ] **Step 5: Run tests**

Run: `npx vitest run "src/app/(staff)/dashboard" src/features/reports`
Expected: PASS.

- [ ] **Step 6: Remove the replaced components**

For each of `QuickStatsStrip`, `ShipmentFunnel`, `DeliveryRateHero`, `RateTiles`, `BranchPerformanceChart`, `RiderPerformanceChart`, `FinancialSummary`, `DashboardHeader`: run `grep -rn "<Name>" src/web/src --include=*.tsx --include=*.ts`; if only the component's own file and test match, `git rm` both. `features/merchant-dashboard/components/MerchantDashboardStats.test.tsx` and `app/merchant/layout.tsx` showed up in an earlier name grep — check whether they import any of these (they likely matched a different identifier); keep any component still imported.

- [ ] **Step 7: Full web checks and commit**

Run: `npx vitest run`, `npx tsc --noEmit` (≤ 8 pre-existing errors), `npm run lint`.

```bash
git add -A src/web/src/features/reports "src/web/src/app/(staff)/dashboard"
git commit -m "feat(web): operations overview dashboard with pipeline, needs-action and COD split (F15 R2)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Shared `BarcodeScanner` with continuous mode

**Files:**
- Move: `src/web/src/features/rider-app/components/BarcodeScanner.tsx` → `src/web/src/components/scanner/BarcodeScanner.tsx`
- Move: `src/web/src/features/rider-app/components/BarcodeScanner.test.tsx` → `src/web/src/components/scanner/BarcodeScanner.test.tsx`
- Create: `src/web/src/components/scanner/dedupe.ts`, `dedupe.test.ts`
- Modify: `src/web/src/app/rider/scan/page.tsx` (import path only)

**Interfaces:**
- Produces: `BarcodeScanner({ onDetect, continuous = false, manualEntry = true }: { onDetect(awb: string): void; continuous?: boolean; manualEntry?: boolean })`.
- Produces: `createDeduper(windowMs: number, now?: () => number): (code: string) => boolean` — returns `true` when the code should be emitted; `SCAN_DEDUPE_MS = 3000`.

- [ ] **Step 1: Move files with git and fix the import**

```bash
git mv src/web/src/features/rider-app/components/BarcodeScanner.tsx src/web/src/components/scanner/BarcodeScanner.tsx
git mv src/web/src/features/rider-app/components/BarcodeScanner.test.tsx src/web/src/components/scanner/BarcodeScanner.test.tsx
```

In `src/web/src/app/rider/scan/page.tsx` change the import to `import { BarcodeScanner } from "@/components/scanner/BarcodeScanner";`. Update any relative imports/mocks inside the moved test (`grep -n "rider-app" src/web/src/components/scanner/BarcodeScanner.test.tsx`). Also `grep -rn "rider-app/components/BarcodeScanner" src/web/src` and fix every hit.

Run: `npx vitest run src/components/scanner src/app/rider` → PASS (behaviour unchanged).

- [ ] **Step 2: Write the failing dedupe test**

`src/web/src/components/scanner/dedupe.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { createDeduper } from "./dedupe";

describe("createDeduper", () => {
  it("suppresses the same code inside the window and allows it after", () => {
    let t = 0;
    const accept = createDeduper(3000, () => t);
    expect(accept("A")).toBe(true);
    t = 1000;
    expect(accept("A")).toBe(false);
    t = 2999;
    expect(accept("A")).toBe(false);
    t = 4001; // window restarts from the last *seen* frame, so a held barcode stays suppressed
    expect(accept("A")).toBe(false);
    t = 7002;
    expect(accept("A")).toBe(true);
  });

  it("lets a different code through immediately", () => {
    let t = 0;
    const accept = createDeduper(3000, () => t);
    expect(accept("A")).toBe(true);
    t = 10;
    expect(accept("B")).toBe(true);
    t = 20;
    expect(accept("A")).toBe(true);
  });
});
```

- [ ] **Step 3: Run to verify it fails, then implement**

Run: `npx vitest run src/components/scanner/dedupe.test.ts` → FAIL.

`src/web/src/components/scanner/dedupe.ts`:

```ts
export const SCAN_DEDUPE_MS = 3000;

// A barcode held in front of the camera is decoded every frame. Emit a code once, then
// suppress it while it keeps being seen within `windowMs` of the previous sighting.
export function createDeduper(windowMs: number, now: () => number = () => Date.now()) {
  let lastCode: string | null = null;
  let lastSeen = -Infinity;
  return (code: string): boolean => {
    const t = now();
    const duplicate = code === lastCode && t - lastSeen < windowMs;
    lastCode = code;
    lastSeen = t;
    return !duplicate;
  };
}
```

Run again → PASS.

- [ ] **Step 4: Add continuous + manualEntry props**

In `src/web/src/components/scanner/BarcodeScanner.tsx`:

1. Change the signature to:

```tsx
export function BarcodeScanner({
  onDetect,
  continuous = false,
  manualEntry = true,
}: {
  onDetect: (awb: string) => void;
  continuous?: boolean;
  manualEntry?: boolean;
}) {
```

2. Add `import { createDeduper, SCAN_DEDUPE_MS } from "./dedupe";` and, next to `onDetectRef`, `const continuousRef = useRef(continuous); continuousRef.current = continuous;` and `const dedupeRef = useRef(createDeduper(SCAN_DEDUPE_MS));`.

3. Replace both detection branches' bodies so that in continuous mode the stream keeps running:

```tsx
              if (results[0]?.rawValue) {
                const code = results[0].rawValue.trim().toUpperCase();
                if (continuousRef.current) {
                  if (dedupeRef.current(code)) onDetectRef.current(code);
                } else {
                  onDetectRef.current(code);
                  // One physical scan, one detection: stop the camera (single-shot rider flow).
                  stopScanning();
                  if (!cancelled) setScanning(false);
                  return;
                }
              }
```

and for the zxing branch:

```tsx
          const controls = await reader.decodeFromVideoElement(videoRef.current, (result) => {
            if (!result || cancelled) return;
            const code = result.getText().trim().toUpperCase();
            if (continuousRef.current) {
              if (dedupeRef.current(code)) onDetectRef.current(code);
              return;
            }
            onDetectRef.current(code);
            controls.stop();
            stopScanning();
            setScanning(false);
          });
```

4. Add a Stop button while scanning in continuous mode, and render the manual-entry block only when `manualEntry`:

```tsx
        {scanning && continuous && (
          <Button type="button" variant="secondary" size="sm" className="absolute right-2 top-2" onClick={() => setScanning(false)}>
            Stop camera
          </Button>
        )}
```

(placed inside the `relative aspect-square` container), change the idle button label to `{continuous ? "Start camera" : "Start Scanning"}`, and wrap the existing manual entry `<div className="space-y-2">…</div>` in `{manualEntry && (…)}`. For the station, also change the container class to `aspect-video` when `continuous` (`className={cn("relative w-full overflow-hidden rounded-lg bg-black", continuous ? "aspect-video" : "aspect-square")}`, importing `cn` from `@/lib/utils`).

- [ ] **Step 5: Add a continuous-mode component test**

Append to `src/web/src/components/scanner/BarcodeScanner.test.tsx` (reuse that file's existing `getUserMedia`/`BarcodeDetector` mocking helpers — read the file first and follow its setup exactly; the test below assumes a `mockDetector(sequence: string[])` style helper — if the file has none, create one at the top that installs `window.BarcodeDetector` whose `detect` returns the next value of `sequence` each call and `[]` afterwards, and stubs `navigator.mediaDevices.getUserMedia` plus `HTMLMediaElement.prototype.play`):

```tsx
  it("keeps scanning in continuous mode and suppresses a held barcode", async () => {
    mockDetector(["QA-1", "QA-1", "QA-1", "QA-2"]);
    const onDetect = vi.fn();
    render(<BarcodeScanner onDetect={onDetect} continuous manualEntry={false} />);
    fireEvent.click(screen.getByRole("button", { name: "Start camera" }));
    await waitFor(() => expect(onDetect).toHaveBeenCalledWith("QA-2"));
    expect(onDetect.mock.calls.map((c) => c[0])).toEqual(["QA-1", "QA-2"]);
    expect(screen.getByRole("button", { name: "Stop camera" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Enter AWB manually")).toBeNull();
  });
```

Run: `npx vitest run src/components/scanner src/app/rider` → PASS (old single-shot tests unchanged).

- [ ] **Step 6: Commit**

```bash
git add -A src/web/src/components/scanner src/web/src/features/rider-app src/web/src/app/rider
git commit -m "feat(web): shared barcode scanner with continuous mode and dedupe (F15 R2)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Scan session reducer, error mapping and mode registry

**Files:**
- Create: `src/web/src/features/scanning/scanSessionReducer.ts`, `scanSessionReducer.test.ts`
- Create: `src/web/src/features/scanning/scanErrors.ts`, `scanErrors.test.ts`
- Create: `src/web/src/features/scanning/modes.ts`, `modes.test.ts`
- Create: `src/web/src/features/scanning/useScanSession.ts`, `useScanSession.test.tsx`

**Interfaces:**
- Consumes: `clientFetch`, `SessionExpiredError` (`@/lib/api/clientFetch`), `manifestScanErrorMessage`, `Manifest`, `Rider`, `Shipment` types.
- Produces (`scanSessionReducer.ts`): `type ScanModeId = "receive" | "sort" | "dispatch" | "handover" | "rto"`; `interface ScanLogEntry { id: number; at: string; awb: string; modeId: ScanModeId; ok: boolean; text: string }`; `interface ScanSessionState { inFlight: string | null; queued: string | null; last: ScanLogEntry | null; log: ScanLogEntry[]; nextId: number }`; `INITIAL_SCAN_SESSION`; `type ScanSessionAction = { type: "requested"; awb: string } | { type: "settled"; awb: string; modeId: ScanModeId; ok: boolean; text: string; at: string }`; `scanSessionReducer(state, action)`; `MAX_LOG = 200`.
- Produces (`scanErrors.ts`): `scanErrorMessage(error: unknown): string`; constants `NOT_FOUND_MESSAGE`, `NETWORK_MESSAGE`.
- Produces (`modes.ts`): `type ScanContext = { kind: "none" } | { kind: "manifest"; manifest: Manifest } | { kind: "rider"; rider: Rider }`; `type ScanOutcome = { ok: true; awb: string; summary: string } | { ok: false; awb: string; message: string }`; `interface ScanMode { id: ScanModeId; label: string; title(ctx: ScanContext): string; context: "none" | "optional-manifest" | "manifest" | "rider"; isAvailable(can: (p: string) => boolean): boolean; requiresContext(can: (p: string) => boolean): boolean; manifestFilter?(m: Manifest, inScope: (branchId: string) => boolean): boolean; submit?(awb: string, ctx: ScanContext): Promise<ScanOutcome> }`; `SCAN_MODES: ScanMode[]`; `getScanMode(id: string | null, can): ScanMode | undefined` (first available when id invalid/unavailable).
- Produces (`useScanSession.ts`): `useScanSession(mode: ScanMode | undefined, context: ScanContext, onSettled?: (outcome: ScanOutcome) => void): { state: ScanSessionState; request(awb: string): void }`.

- [ ] **Step 1: Write failing tests**

`scanSessionReducer.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { INITIAL_SCAN_SESSION, MAX_LOG, scanSessionReducer, type ScanSessionState } from "./scanSessionReducer";

const settle = (s: ScanSessionState, awb: string, ok = true) =>
  scanSessionReducer(s, { type: "settled", awb, modeId: "dispatch", ok, text: ok ? "Accepted" : "Nope", at: "2026-10-05T10:00:00Z" });

describe("scanSessionReducer", () => {
  it("runs the first request immediately", () => {
    const s = scanSessionReducer(INITIAL_SCAN_SESSION, { type: "requested", awb: "A" });
    expect(s.inFlight).toBe("A");
    expect(s.queued).toBeNull();
  });

  it("queues at most one request while busy, latest wins", () => {
    let s = scanSessionReducer(INITIAL_SCAN_SESSION, { type: "requested", awb: "A" });
    s = scanSessionReducer(s, { type: "requested", awb: "B" });
    s = scanSessionReducer(s, { type: "requested", awb: "C" });
    expect(s.inFlight).toBe("A");
    expect(s.queued).toBe("C");
  });

  it("ignores a request for the code already in flight or queued", () => {
    let s = scanSessionReducer(INITIAL_SCAN_SESSION, { type: "requested", awb: "A" });
    s = scanSessionReducer(s, { type: "requested", awb: "A" });
    expect(s.queued).toBeNull();
  });

  it("settling logs newest-first, sets last and promotes the queued code", () => {
    let s = scanSessionReducer(INITIAL_SCAN_SESSION, { type: "requested", awb: "A" });
    s = scanSessionReducer(s, { type: "requested", awb: "B" });
    s = settle(s, "A");
    expect(s.inFlight).toBe("B");
    expect(s.queued).toBeNull();
    expect(s.last).toMatchObject({ awb: "A", ok: true });
    s = settle(s, "B", false);
    expect(s.inFlight).toBeNull();
    expect(s.log.map((e) => e.awb)).toEqual(["B", "A"]);
    expect(new Set(s.log.map((e) => e.id)).size).toBe(2);
  });

  it("caps the log", () => {
    let s = INITIAL_SCAN_SESSION;
    for (let i = 0; i < MAX_LOG + 5; i++) {
      s = scanSessionReducer(s, { type: "requested", awb: `A${i}` });
      s = settle(s, `A${i}`);
    }
    expect(s.log).toHaveLength(MAX_LOG);
    expect(s.log[0].awb).toBe(`A${MAX_LOG + 4}`);
  });
});
```

`scanErrors.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { SessionExpiredError } from "@/lib/api/clientFetch";
import { NETWORK_MESSAGE, NOT_FOUND_MESSAGE, scanErrorMessage } from "./scanErrors";

describe("scanErrorMessage", () => {
  it("maps manifest reason codes", () => {
    expect(scanErrorMessage(new Error(JSON.stringify({ error: { reason: "NOT_ON_MANIFEST" } })))).toBe("This shipment isn't on this manifest.");
  });
  it("uses a server error message when present", () => {
    expect(scanErrorMessage(new Error(JSON.stringify({ error: { code: "X", message: "Shipment must be AtDestinationBranch" } })))).toBe(
      "Shipment must be AtDestinationBranch"
    );
  });
  it("maps a 404 to not-found-in-your-branches", () => {
    expect(scanErrorMessage(new Error("Request to /api/shipments/QA failed with 404"))).toBe(NOT_FOUND_MESSAGE);
  });
  it("maps fetch TypeErrors to a network message", () => {
    expect(scanErrorMessage(new TypeError("Failed to fetch"))).toBe(NETWORK_MESSAGE);
  });
  it("maps session expiry", () => {
    expect(scanErrorMessage(new SessionExpiredError())).toMatch(/session expired/i);
  });
  it("falls back to a generic message", () => {
    expect(scanErrorMessage("weird")).toMatch(/couldn't record/i);
  });
});
```

`modes.test.ts`:

```ts
import { beforeEach, describe, expect, it, vi } from "vitest";

const clientFetch = vi.fn();
vi.mock("@/lib/api/clientFetch", async (orig) => ({ ...(await orig<object>()), clientFetch: (...a: unknown[]) => clientFetch(...a) }));

import { SCAN_MODES, getScanMode, type ScanContext } from "./modes";
import type { Manifest } from "@/features/manifests/types";

const manifest = { id: "m1", code: "MNF-1", fromBranchId: "a", toBranchId: "b", purpose: "Forward", status: "Draft", shipmentCount: 0 } as Manifest;
const can = (granted: string[]) => (p: string) => granted.includes(p);
const mode = (id: string) => SCAN_MODES.find((m) => m.id === id)!;

describe("scan modes", () => {
  beforeEach(() => clientFetch.mockReset());

  it("receive with a manifest posts receive-scan", async () => {
    clientFetch.mockResolvedValue({ awb: "QA-1" });
    const ctx: ScanContext = { kind: "manifest", manifest: { ...manifest, status: "InTransit" } };
    const outcome = await mode("receive").submit!("QA-1", ctx);
    expect(clientFetch).toHaveBeenCalledWith("/api/manifests/m1/receive-scan", expect.objectContaining({ method: "POST", body: JSON.stringify({ awb: "QA-1" }) }));
    expect(outcome.ok).toBe(true);
  });

  it("receive without a manifest looks up then receives at origin", async () => {
    clientFetch.mockResolvedValueOnce({ id: "s1", awb: "QA-1", recipientName: "Sita", codAmount: 990 }).mockResolvedValueOnce({});
    const outcome = await mode("receive").submit!("QA-1", { kind: "none" });
    expect(clientFetch.mock.calls[0][0]).toBe("/api/shipments/QA-1");
    expect(clientFetch.mock.calls[1][0]).toBe("/api/shipments/s1/receive-origin");
    expect(outcome).toMatchObject({ ok: true, summary: expect.stringContaining("Sita") });
  });

  it("dispatch and rto post to the manifest scan endpoints", async () => {
    clientFetch.mockResolvedValue({});
    await mode("dispatch").submit!("QA-1", { kind: "manifest", manifest });
    await mode("rto").submit!("QA-1", { kind: "manifest", manifest: { ...manifest, purpose: "Return" } });
    expect(clientFetch.mock.calls.map((c) => c[0])).toEqual(["/api/manifests/m1/scan", "/api/manifests/m1/rto-scan"]);
  });

  it("handover looks up the shipment then assigns a delivery task", async () => {
    clientFetch.mockResolvedValueOnce({ id: "s1", awb: "QA-1", recipientName: "Sita", codAmount: 0 }).mockResolvedValueOnce({ id: "t1" });
    await mode("handover").submit!("QA-1", { kind: "rider", rider: { id: "r1" } as never });
    expect(clientFetch.mock.calls[1]).toEqual([
      "/api/riders/tasks",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ riderId: "r1", shipmentId: "s1", type: "Delivery" }) }),
    ]);
  });

  it("returns a rejected outcome instead of throwing", async () => {
    clientFetch.mockRejectedValue(new Error(JSON.stringify({ error: { reason: "NOT_ON_MANIFEST" } })));
    const outcome = await mode("dispatch").submit!("QA-9", { kind: "manifest", manifest });
    expect(outcome).toEqual({ ok: false, awb: "QA-9", message: "This shipment isn't on this manifest." });
  });

  it("sort has no submit", () => {
    expect(mode("sort").submit).toBeUndefined();
  });

  it("availability mirrors endpoint permissions", () => {
    expect(mode("receive").isAvailable(can(["manifest.receive"]))).toBe(true);
    expect(mode("receive").requiresContext(can(["manifest.receive"]))).toBe(true);
    expect(mode("receive").requiresContext(can(["shipment.scan"]))).toBe(false);
    expect(mode("dispatch").isAvailable(can(["manifest.create"]))).toBe(false);
    expect(mode("dispatch").isAvailable(can(["manifest.create", "manifest.read"]))).toBe(true);
    expect(mode("handover").isAvailable(can(["delivery.assign"]))).toBe(false);
    expect(mode("handover").isAvailable(can(["delivery.assign", "rider.read"]))).toBe(true);
  });

  it("getScanMode falls back to the first available mode", () => {
    expect(getScanMode("dispatch", can(["shipment.scan"]))?.id).toBe("receive");
    expect(getScanMode("bogus", can(["manifest.create", "manifest.read"]))?.id).toBe("sort");
    expect(getScanMode(null, can([]))).toBeUndefined();
  });

  it("manifest filters pick eligible manifests in scope", () => {
    const inScope = (id: string) => id === "b" || id === "a";
    expect(mode("receive").manifestFilter!({ ...manifest, status: "InTransit" }, inScope)).toBe(true);
    expect(mode("receive").manifestFilter!({ ...manifest, status: "Draft" }, inScope)).toBe(false);
    expect(mode("dispatch").manifestFilter!(manifest, inScope)).toBe(true);
    expect(mode("rto").manifestFilter!(manifest, inScope)).toBe(false);
  });
});
```

`useScanSession.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { act, renderHook, waitFor } from "@testing-library/react";
import { useScanSession } from "./useScanSession";
import type { ScanMode, ScanOutcome } from "./modes";

function deferred() {
  let resolve!: (o: ScanOutcome) => void;
  const promise = new Promise<ScanOutcome>((r) => (resolve = r));
  return { promise, resolve };
}

describe("useScanSession", () => {
  it("serialises submissions: one in flight, then the queued one", async () => {
    const first = deferred();
    const second = deferred();
    const submit = vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    const mode = { id: "dispatch", submit } as unknown as ScanMode;
    const { result } = renderHook(() => useScanSession(mode, { kind: "none" }));

    act(() => result.current.request("A"));
    act(() => result.current.request("B"));
    expect(submit).toHaveBeenCalledTimes(1);

    await act(async () => first.resolve({ ok: true, awb: "A", summary: "ok" }));
    await waitFor(() => expect(submit).toHaveBeenCalledTimes(2));
    expect(submit.mock.calls[1][0]).toBe("B");

    await act(async () => second.resolve({ ok: false, awb: "B", message: "no" }));
    expect(result.current.state.log.map((e) => [e.awb, e.ok])).toEqual([["B", false], ["A", true]]);
  });

  it("ignores requests when the mode has no submit", () => {
    const { result } = renderHook(() => useScanSession({ id: "sort" } as ScanMode, { kind: "none" }));
    act(() => result.current.request("A"));
    expect(result.current.state.inFlight).toBeNull();
  });
});
```

- [ ] **Step 2: Run to verify they fail**

Run: `npx vitest run src/features/scanning` → FAIL (modules missing).

- [ ] **Step 3: Implement the reducer and error mapping**

`scanSessionReducer.ts`:

```ts
export type ScanModeId = "receive" | "sort" | "dispatch" | "handover" | "rto";

export interface ScanLogEntry {
  id: number;
  at: string;
  awb: string;
  modeId: ScanModeId;
  ok: boolean;
  text: string;
}

export interface ScanSessionState {
  inFlight: string | null;
  queued: string | null;
  last: ScanLogEntry | null;
  log: ScanLogEntry[];
  nextId: number;
}

export type ScanSessionAction =
  | { type: "requested"; awb: string }
  | { type: "settled"; awb: string; modeId: ScanModeId; ok: boolean; text: string; at: string };

export const MAX_LOG = 200;

export const INITIAL_SCAN_SESSION: ScanSessionState = { inFlight: null, queued: null, last: null, log: [], nextId: 1 };

// Mode and context live in the page (URL / component state); this reducer only owns the
// submission queue and the session log (a refinement of spec §6.5 — keeps mode in one place).
export function scanSessionReducer(state: ScanSessionState, action: ScanSessionAction): ScanSessionState {
  switch (action.type) {
    case "requested":
      if (action.awb === state.inFlight || action.awb === state.queued) return state;
      if (state.inFlight === null) return { ...state, inFlight: action.awb };
      return { ...state, queued: action.awb };
    case "settled": {
      const entry: ScanLogEntry = { id: state.nextId, at: action.at, awb: action.awb, modeId: action.modeId, ok: action.ok, text: action.text };
      return {
        inFlight: state.queued,
        queued: null,
        last: entry,
        log: [entry, ...state.log].slice(0, MAX_LOG),
        nextId: state.nextId + 1,
      };
    }
  }
}
```

`scanErrors.ts`:

```ts
import { SessionExpiredError } from "@/lib/api/clientFetch";
import { manifestScanErrorMessage } from "@/features/manifests/lib/manifestErrorMessages";

export const NOT_FOUND_MESSAGE = "AWB not found in your branches.";
export const NETWORK_MESSAGE = "Network error — not recorded, scan again.";
const GENERIC_MESSAGE = "Couldn't record this scan. Check the AWB and try again.";

export function scanErrorMessage(error: unknown): string {
  if (error instanceof SessionExpiredError) return "Your session expired — sign in again.";
  if (error instanceof TypeError) return NETWORK_MESSAGE;
  const message = error instanceof Error ? error.message : "";
  if (/failed with 404$/.test(message)) return NOT_FOUND_MESSAGE;
  try {
    const parsed = JSON.parse(message);
    const reason = parsed?.error?.reason;
    if (typeof reason === "string") return manifestScanErrorMessage(reason);
    const serverMessage = parsed?.error?.message;
    if (typeof serverMessage === "string" && serverMessage) return serverMessage;
  } catch {
    // not JSON
  }
  return GENERIC_MESSAGE;
}
```

- [ ] **Step 4: Implement the mode registry**

`modes.ts`:

```ts
import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest } from "@/features/manifests/types";
import type { Rider } from "@/features/riders/types";
import type { Shipment } from "@/features/shipments/types";
import type { ScanModeId } from "./scanSessionReducer";
import { scanErrorMessage } from "./scanErrors";

export type ScanContext = { kind: "none" } | { kind: "manifest"; manifest: Manifest } | { kind: "rider"; rider: Rider };
export type ScanOutcome = { ok: true; awb: string; summary: string } | { ok: false; awb: string; message: string };
type Can = (permission: string) => boolean;

export interface ScanMode {
  id: ScanModeId;
  label: string;
  title(ctx: ScanContext): string;
  context: "none" | "optional-manifest" | "manifest" | "rider";
  isAvailable(can: Can): boolean;
  requiresContext(can: Can): boolean;
  manifestFilter?(m: Manifest, inScope: (branchId: string) => boolean): boolean;
  submit?(awb: string, ctx: ScanContext): Promise<ScanOutcome>;
}

const rs = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });
const post = (body: unknown): RequestInit => ({ method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });

function describe(s: Pick<Shipment, "recipientName" | "codAmount">): string {
  return `${s.recipientName} · ${s.codAmount > 0 ? `COD Rs ${rs.format(s.codAmount)}` : "Prepaid"}`;
}

async function run(awb: string, op: () => Promise<string>): Promise<ScanOutcome> {
  try {
    return { ok: true, awb, summary: await op() };
  } catch (error) {
    return { ok: false, awb, message: scanErrorMessage(error) };
  }
}

const lookup = (awb: string) => clientFetch<Shipment>(`/api/shipments/${encodeURIComponent(awb)}`);
const manifestOf = (ctx: ScanContext) => (ctx.kind === "manifest" ? ctx.manifest : null);

export const SCAN_MODES: ScanMode[] = [
  {
    id: "receive",
    label: "Receive",
    title: (ctx) => (ctx.kind === "manifest" ? "Receive manifest" : "Receive at origin"),
    context: "optional-manifest",
    isAvailable: (can) => can("shipment.scan") || can("manifest.receive"),
    requiresContext: (can) => !can("shipment.scan"),
    manifestFilter: (m, inScope) => m.status === "InTransit" && inScope(m.toBranchId),
    submit: (awb, ctx) =>
      run(awb, async () => {
        const manifest = manifestOf(ctx);
        if (manifest) {
          await clientFetch(`/api/manifests/${manifest.id}/receive-scan`, post({ awb }));
          return `Received on ${manifest.code}`;
        }
        const shipment = await lookup(awb);
        await clientFetch(`/api/shipments/${shipment.id}/receive-origin`, { method: "POST" });
        return `Received at origin · ${describe(shipment)}`;
      }),
  },
  {
    id: "sort",
    label: "Sort",
    title: () => "Sort",
    context: "none",
    isAvailable: (can) => can("shipment.scan") || can("manifest.create") || can("manifest.receive") || can("delivery.assign"),
    requiresContext: () => false,
  },
  {
    id: "dispatch",
    label: "Dispatch",
    title: () => "Dispatch",
    context: "manifest",
    isAvailable: (can) => can("manifest.create") && can("manifest.read"),
    requiresContext: () => true,
    manifestFilter: (m, inScope) => m.status === "Draft" && m.purpose === "Forward" && inScope(m.fromBranchId),
    submit: (awb, ctx) =>
      run(awb, async () => {
        const manifest = manifestOf(ctx)!;
        await clientFetch(`/api/manifests/${manifest.id}/scan`, post({ awb }));
        return `Added to ${manifest.code}`;
      }),
  },
  {
    id: "handover",
    label: "Rider handover",
    title: () => "Rider handover",
    context: "rider",
    isAvailable: (can) => can("delivery.assign") && can("rider.read"),
    requiresContext: () => true,
    submit: (awb, ctx) =>
      run(awb, async () => {
        if (ctx.kind !== "rider") throw new Error("No rider selected");
        const shipment = await lookup(awb);
        await clientFetch(`/api/riders/tasks`, post({ riderId: ctx.rider.id, shipmentId: shipment.id, type: "Delivery" }));
        return `Out for delivery · ${describe(shipment)}`;
      }),
  },
  {
    id: "rto",
    label: "RTO",
    title: () => "RTO",
    context: "manifest",
    isAvailable: (can) => can("manifest.create") && can("manifest.read"),
    requiresContext: () => true,
    manifestFilter: (m, inScope) => m.status === "Draft" && m.purpose === "Return" && inScope(m.fromBranchId),
    submit: (awb, ctx) =>
      run(awb, async () => {
        const manifest = manifestOf(ctx)!;
        await clientFetch(`/api/manifests/${manifest.id}/rto-scan`, post({ awb }));
        return `Added to return manifest ${manifest.code}`;
      }),
  },
];

export function getScanMode(id: string | null, can: Can): ScanMode | undefined {
  const available = SCAN_MODES.filter((m) => m.isAvailable(can));
  return available.find((m) => m.id === id) ?? available[0];
}
```

Confirm `Rider` is exported from `@/features/riders/types` and `/api/riders/tasks` proxy exists (`src/web/src/app/api/riders/tasks/route.ts`) — both exist on master.

- [ ] **Step 5: Implement `useScanSession`**

```ts
"use client";

import { useCallback, useEffect, useReducer, useRef } from "react";
import { INITIAL_SCAN_SESSION, scanSessionReducer } from "./scanSessionReducer";
import type { ScanContext, ScanMode, ScanOutcome } from "./modes";

// Runs at most one submission at a time. Mode/context/onSettled are read through refs so a
// re-render never restarts or duplicates an in-flight write.
export function useScanSession(mode: ScanMode | undefined, context: ScanContext, onSettled?: (outcome: ScanOutcome) => void) {
  const [state, dispatch] = useReducer(scanSessionReducer, INITIAL_SCAN_SESSION);
  const modeRef = useRef(mode);
  const contextRef = useRef(context);
  const onSettledRef = useRef(onSettled);
  modeRef.current = mode;
  contextRef.current = context;
  onSettledRef.current = onSettled;

  useEffect(() => {
    const awb = state.inFlight;
    const active = modeRef.current;
    if (!awb || !active?.submit) return;
    void active.submit(awb, contextRef.current).then((outcome) => {
      dispatch({
        type: "settled",
        awb,
        modeId: active.id,
        ok: outcome.ok,
        text: outcome.ok ? outcome.summary : outcome.message,
        at: new Date().toISOString(),
      });
      onSettledRef.current?.(outcome);
    });
  }, [state.inFlight, state.nextId]);

  const request = useCallback((awb: string) => {
    if (!modeRef.current?.submit) return;
    const trimmed = awb.trim().toUpperCase();
    if (trimmed) dispatch({ type: "requested", awb: trimmed });
  }, []);

  return { state, request };
}
```

Note the effect dependency on `state.nextId`: when the queued code equals the just-settled code's successor, `inFlight` may be set to the same string as before (e.g. A settled, A re-queued after dedupe window) — `nextId` changes on every settle, so the effect still fires.

- [ ] **Step 6: Run tests, commit**

Run: `npx vitest run src/features/scanning` → PASS.

```bash
git add src/web/src/features/scanning
git commit -m "feat(web): scan session queue, error mapping and scan mode registry (F15 R2)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Scan station panels

**Files:**
- Create: `src/web/src/features/scanning/components/ScanResultCard.tsx`
- Create: `src/web/src/features/scanning/components/ScanLog.tsx`
- Create: `src/web/src/features/scanning/components/ManualScanInput.tsx`
- Create: `src/web/src/features/scanning/components/ReconciliationPanel.tsx`
- Create: `src/web/src/features/scanning/components/ScanContextPicker.tsx`
- Create: `src/web/src/features/scanning/beep.ts`
- Create: `src/web/src/features/scanning/components/panels.test.tsx`

**Interfaces:**
- Consumes: Task 10 (`ScanLogEntry`, `ScanMode`, `ScanContext`), `useManifests(branchId?, status?)`, `useMissingShipments(id, enabled)`, `useManifest(id)`, `useCloseManifest(id)`, `useRiders()`, `usePeopleNames()`, `usePermission`.
- Produces: `ScanResultCard({ entry }: { entry: ScanLogEntry | null })`; `ScanLog({ entries }: { entries: ScanLogEntry[] })`; `ManualScanInput({ disabled, onSubmit }: { disabled: boolean; onSubmit(awb: string): void })`; `ReconciliationPanel({ manifest }: { manifest: Manifest })`; `ScanContextPicker({ mode, value, onChange, inScope }: { mode: ScanMode; value: ScanContext; onChange(ctx: ScanContext): void; inScope(branchId: string): boolean })`; `playScanTone(ok: boolean): void`.

- [ ] **Step 1: Write the failing panel tests**

`panels.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";

const missing = vi.fn();
vi.mock("@/features/manifests/hooks/useMissingShipments", () => ({ useMissingShipments: () => missing() }));
const closeMutate = vi.fn();
vi.mock("@/features/manifests/hooks/useCloseManifest", () => ({ useCloseManifest: () => ({ mutate: closeMutate, isPending: false }) }));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: () => true }));

import { ScanResultCard } from "./ScanResultCard";
import { ScanLog } from "./ScanLog";
import { ManualScanInput } from "./ManualScanInput";
import { ReconciliationPanel } from "./ReconciliationPanel";

const entry = (ok: boolean) => ({ id: 1, at: "2026-10-05T05:06:41Z", awb: "QA-388", modeId: "receive" as const, ok, text: ok ? "Received on MNF-1" : "This shipment isn't on this manifest." });

describe("scan panels", () => {
  beforeEach(() => {
    missing.mockReturnValue({ data: Array.from({ length: 7 }, (_, i) => ({ shipmentId: `s${i}`, awb: `QA-${i}`, scannedAt: "" })), isLoading: false, isError: false });
    closeMutate.mockReset();
  });

  it("announces accepted and rejected results", () => {
    const { rerender } = render(<ScanResultCard entry={entry(true)} />);
    expect(screen.getByRole("status")).toHaveTextContent("Accepted");
    expect(screen.getByRole("status")).toHaveTextContent("QA-388");
    rerender(<ScanResultCard entry={entry(false)} />);
    expect(screen.getByRole("status")).toHaveTextContent("Rejected");
    expect(screen.getByRole("status")).toHaveTextContent("isn't on this manifest");
  });

  it("lists the session log with a count", () => {
    render(<ScanLog entries={[entry(true), { ...entry(false), id: 2 }]} />);
    expect(screen.getByText("2 scans this session")).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(2);
  });

  it("manual input submits uppercased on Enter, clears on Escape, and respects disabled", () => {
    const onSubmit = vi.fn();
    const { rerender } = render(<ManualScanInput disabled={false} onSubmit={onSubmit} />);
    const input = screen.getByRole("textbox", { name: /scan or type awb/i });
    fireEvent.change(input, { target: { value: " qa-1 " } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onSubmit).toHaveBeenCalledWith("QA-1");
    expect(input).toHaveValue("");
    fireEvent.change(input, { target: { value: "x" } });
    fireEvent.keyDown(input, { key: "Escape" });
    expect(input).toHaveValue("");
    rerender(<ManualScanInput disabled onSubmit={onSubmit} />);
    expect(screen.getByRole("textbox", { name: /scan or type awb/i })).toBeDisabled();
  });

  it("reconciles a manifest: verified/expected, first five pending, show all, close", () => {
    renderWithIntl(<ReconciliationPanel manifest={{ id: "m1", code: "MNF-1", shipmentCount: 10, status: "InTransit" } as never} />);
    expect(screen.getByText("3")).toBeInTheDocument();
    expect(screen.getByText("/ 10 verified")).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(5);
    fireEvent.click(screen.getByRole("button", { name: "Show all 7" }));
    expect(screen.getAllByRole("listitem")).toHaveLength(7);
    fireEvent.click(screen.getByRole("button", { name: "Close manifest" }));
    fireEvent.click(screen.getByRole("button", { name: "Close and flag 7 missing" }));
    expect(closeMutate).toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/features/scanning/components` → FAIL.

- [ ] **Step 3: Implement the panels**

`beep.ts`:

```ts
// Short confirmation tone; silently does nothing where WebAudio is unavailable.
export function playScanTone(ok: boolean): void {
  try {
    const Ctx = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
    if (!Ctx) return;
    const ctx = new Ctx();
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.frequency.value = ok ? 880 : 220;
    gain.gain.value = 0.08;
    osc.connect(gain).connect(ctx.destination);
    osc.start();
    osc.stop(ctx.currentTime + (ok ? 0.08 : 0.25));
    osc.onended = () => void ctx.close();
  } catch {
    // audio is best-effort
  }
}
```

`ScanResultCard.tsx`:

```tsx
import { Check, X } from "lucide-react";
import { cn } from "@/lib/utils";
import type { ScanLogEntry } from "@/features/scanning/scanSessionReducer";

export function ScanResultCard({ entry }: { entry: ScanLogEntry | null }) {
  return (
    <div
      role="status"
      aria-live="polite"
      className={cn(
        "flex min-h-[96px] items-center gap-4 rounded-xl border p-5",
        !entry && "border-dashed border-border bg-card text-muted-foreground",
        entry?.ok && "border-status-delivered/40 bg-status-delivered/10",
        entry && !entry.ok && "border-destructive/40 bg-destructive/10"
      )}
    >
      {!entry ? (
        <span className="text-sm">Scan a parcel to see the result here.</span>
      ) : (
        <>
          <span className={cn("flex h-12 w-12 shrink-0 items-center justify-center rounded-xl text-white", entry.ok ? "bg-status-delivered" : "bg-destructive")}>
            {entry.ok ? <Check className="h-6 w-6" aria-hidden="true" /> : <X className="h-6 w-6" aria-hidden="true" />}
          </span>
          <div className="flex min-w-0 flex-col gap-1">
            <span className={cn("text-[13px] font-semibold", entry.ok ? "text-status-delivered" : "text-destructive")}>
              {entry.ok ? "Accepted" : "Rejected"}
            </span>
            <span className="truncate font-mono text-xl font-medium sm:text-2xl">{entry.awb}</span>
            <span className="text-[12.5px] text-foreground/80">{entry.text}</span>
          </div>
        </>
      )}
    </div>
  );
}
```

`ScanLog.tsx`:

```tsx
import { cn } from "@/lib/utils";
import type { ScanLogEntry } from "@/features/scanning/scanSessionReducer";

export function ScanLog({ entries }: { entries: ScanLogEntry[] }) {
  return (
    <section aria-label="Scan log" className="overflow-hidden rounded-xl border border-border bg-card">
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <h2 className="font-display text-[15px] font-semibold">Scan log</h2>
        <span className="text-xs text-muted-foreground">{entries.length} scans this session</span>
      </div>
      {entries.length === 0 ? (
        <p className="px-4 py-6 text-center text-sm text-muted-foreground">No scans yet.</p>
      ) : (
        <ol className="max-h-[420px] overflow-y-auto">
          {entries.map((e) => (
            <li key={e.id} className="grid grid-cols-[72px_minmax(0,1fr)] items-center gap-x-3 border-b border-border/60 px-4 py-2.5 text-[13px] sm:grid-cols-[80px_minmax(0,1.2fr)_minmax(0,2fr)]">
              <time dateTime={e.at} className="font-mono text-xs text-muted-foreground">
                {new Date(e.at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" })}
              </time>
              <span className="font-mono font-medium">{e.awb}</span>
              <span className={cn("col-span-2 flex items-center gap-1.5 text-xs sm:col-span-1", e.ok ? "text-status-delivered" : "text-destructive")}>
                <span className={cn("h-1.5 w-1.5 shrink-0 rounded-full", e.ok ? "bg-status-delivered" : "bg-destructive")} aria-hidden="true" />
                {e.text}
              </span>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
```

`ManualScanInput.tsx`:

```tsx
"use client";

import { forwardRef, useState } from "react";
import { ScanLine } from "lucide-react";
import { cn } from "@/lib/utils";

// Always-available input: hand-typed AWBs and USB/Bluetooth keyboard-wedge scanners (which
// "type" the code and press Enter).
export const ManualScanInput = forwardRef<HTMLInputElement, { disabled: boolean; onSubmit: (awb: string) => void }>(
  function ManualScanInput({ disabled, onSubmit }, ref) {
    const [value, setValue] = useState("");
    return (
      <label
        className={cn(
          "flex h-16 items-center gap-3 rounded-xl border-2 bg-card px-4 transition-colors",
          disabled ? "border-border opacity-60" : "border-primary ring-4 ring-primary/10"
        )}
      >
        <ScanLine className="h-6 w-6 shrink-0 text-primary" aria-hidden="true" />
        <input
          ref={ref}
          type="text"
          aria-label="Scan or type AWB"
          placeholder="Scan barcode / QR or type AWB…"
          autoComplete="off"
          spellCheck={false}
          disabled={disabled}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              const awb = value.trim().toUpperCase();
              if (awb) onSubmit(awb);
              setValue("");
            } else if (e.key === "Escape") {
              setValue("");
            }
          }}
          className="min-w-0 flex-1 bg-transparent font-mono text-lg outline-none placeholder:text-muted-foreground sm:text-xl"
        />
        <span className="hidden flex-col items-end text-[11.5px] text-muted-foreground sm:flex">
          <span>Enter submits · Esc clears</span>
        </span>
      </label>
    );
  }
);
```

`ReconciliationPanel.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle, AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { Skeleton } from "@/components/ui/skeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useMissingShipments } from "@/features/manifests/hooks/useMissingShipments";
import { useCloseManifest } from "@/features/manifests/hooks/useCloseManifest";
import type { Manifest } from "@/features/manifests/types";

export function ReconciliationPanel({ manifest }: { manifest: Manifest }) {
  const missing = useMissingShipments(manifest.id, true);
  const close = useCloseManifest(manifest.id);
  const canClose = usePermission("manifest.close");
  const [showAll, setShowAll] = useState(false);

  const pending = missing.data ?? [];
  const verified = Math.max(0, manifest.shipmentCount - pending.length);
  const share = manifest.shipmentCount ? verified / manifest.shipmentCount : 0;
  const visible = showAll ? pending : pending.slice(0, 5);

  return (
    <section aria-label="Manifest reconciliation" className="space-y-4 rounded-xl border border-border bg-card p-5">
      <h2 className="font-display text-[15px] font-semibold">Manifest reconciliation</h2>
      {missing.isLoading && <Skeleton className="h-24 w-full" />}
      {missing.isError && <ErrorState title="Couldn't load missing parcels" description="Reconciliation is unavailable right now." onRetry={() => missing.refetch()} />}
      {missing.data && (
        <>
          <div className="flex items-baseline gap-2">
            <span className="text-4xl font-semibold tabular-nums tracking-tight">{verified}</span>
            <span className="text-base text-muted-foreground">/ {manifest.shipmentCount} verified</span>
          </div>
          <div className="h-2.5 overflow-hidden rounded-full bg-muted" aria-hidden="true">
            <div className="h-full rounded-full bg-status-delivered" style={{ width: `${Math.round(share * 100)}%` }} />
          </div>
          <div className="space-y-2">
            <div className="flex items-baseline justify-between">
              <h3 className="text-sm font-semibold">Still expected</h3>
              <span className="text-xs text-muted-foreground">{pending.length} parcels</span>
            </div>
            <ul className="text-[12.5px]">
              {visible.map((p) => (
                <li key={p.shipmentId} className="border-b border-dashed border-border py-1.5 font-mono">
                  {p.awb}
                </li>
              ))}
            </ul>
            {!showAll && pending.length > 5 && (
              <Button type="button" variant="link" className="h-auto p-0 text-[12.5px]" onClick={() => setShowAll(true)}>
                {`Show all ${pending.length}`}
              </Button>
            )}
          </div>
          {canClose && (
            <AlertDialog>
              <AlertDialogTrigger asChild>
                <Button className="w-full" disabled={close.isPending}>Close manifest</Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Close {manifest.code}?</AlertDialogTitle>
                  <AlertDialogDescription>
                    {pending.length > 0
                      ? `${pending.length} parcels have not been scanned and will be recorded as missing.`
                      : "Every parcel on this manifest has been verified."}
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Keep scanning</AlertDialogCancel>
                  <AlertDialogAction onClick={() => close.mutate()}>
                    {pending.length > 0 ? `Close and flag ${pending.length} missing` : "Close manifest"}
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          )}
        </>
      )}
    </section>
  );
}
```

Before finalising the dialog copy, open `src/web/src/features/manifests/components/ManifestReceivedSection.tsx` and reuse its existing close-confirmation wording if it has one (spec: "existing confirm dialog copy"); update the test's button name to match.

`ScanContextPicker.tsx`:

```tsx
"use client";

import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useManifests } from "@/features/manifests/hooks/useManifests";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { usePermission } from "@/features/auth/hooks/usePermission";
import type { ScanContext, ScanMode } from "@/features/scanning/modes";

export function ScanContextPicker({
  mode,
  value,
  onChange,
  inScope,
}: {
  mode: ScanMode;
  value: ScanContext;
  onChange: (ctx: ScanContext) => void;
  inScope: (branchId: string) => boolean;
}) {
  const wantsManifest = mode.context === "manifest" || mode.context === "optional-manifest";
  // Both permission hooks are called unconditionally (rules of hooks).
  const canReadManifests = usePermission("manifest.read");
  const canReceive = usePermission("manifest.receive");
  const canManifests = canReadManifests && (mode.context !== "optional-manifest" || canReceive);
  const manifests = useManifests();
  const riders = useRiders({ enabled: mode.context === "rider" });
  const names = usePeopleNames();

  if (wantsManifest && canManifests) {
    const options = (manifests.data ?? []).filter((m) => mode.manifestFilter?.(m, inScope));
    const selected = value.kind === "manifest" ? value.manifest.id : "";
    return (
      <Select
        value={selected || (mode.context === "optional-manifest" ? "none" : "")}
        onValueChange={(id) => {
          const m = options.find((o) => o.id === id);
          onChange(m ? { kind: "manifest", manifest: m } : { kind: "none" });
        }}
      >
        <SelectTrigger className="w-full sm:w-96" aria-label="Manifest">
          <SelectValue placeholder={manifests.isLoading ? "Loading manifests…" : "Choose a manifest"} />
        </SelectTrigger>
        <SelectContent>
          {mode.context === "optional-manifest" && <SelectItem value="none">No manifest — receive at origin</SelectItem>}
          {options.map((m) => (
            <SelectItem key={m.id} value={m.id}>
              {`${m.code} · ${names.branchName(m.fromBranchId)} → ${names.branchName(m.toBranchId)}`}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    );
  }

  if (mode.context === "rider") {
    const active = (riders.data ?? []).filter((r) => r.isActive && inScope(r.baseBranchId));
    return (
      <Select
        value={value.kind === "rider" ? value.rider.id : ""}
        onValueChange={(id) => {
          const rider = active.find((r) => r.id === id);
          if (rider) onChange({ kind: "rider", rider });
        }}
      >
        <SelectTrigger className="w-full sm:w-80" aria-label="Rider">
          <SelectValue placeholder={riders.isLoading ? "Loading riders…" : "Choose a rider"} />
        </SelectTrigger>
        <SelectContent>
          {active.map((r) => {
            const name = names.riderName(r.id);
            return (
              <SelectItem key={r.id} value={r.id}>
                {name === "Rider" ? `Rider · ${r.licenseNumber}` : name}
              </SelectItem>
            );
          })}
        </SelectContent>
      </Select>
    );
  }

  return null;
}
```

- [ ] **Step 4: Run tests, commit**

Run: `npx vitest run src/features/scanning` → PASS; `npm run lint` clean.

```bash
git add src/web/src/features/scanning
git commit -m "feat(web): scan station panels — result card, log, manual input, reconciliation, context picker (F15 R2)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: `/scan` page, nav item and i18n

**Files:**
- Create: `src/web/src/features/scanning/components/ScanStation.tsx`, `ScanStation.test.tsx`
- Create: `src/web/src/app/(staff)/scan/page.tsx`
- Modify: `src/web/src/lib/navigation/navItems.ts`
- Modify: `src/web/messages/en.json`, `src/web/messages/ne.json`
- Modify (if they enumerate nav items): `src/web/src/components/layout/app-sidebar.test.tsx` / `nav-main.test.tsx` / `CommandPalette` tests

**Interfaces:**
- Consumes: everything from Tasks 9–11, `useSession` (`@/features/auth/hooks/useSession` → `{ data: MeResponse }` with `permissions`, `hasAllBranchesScope`, `branches: { branchId }[]`), `useManifestPanelStore`, `useQueryClient`.
- Produces: nav item `{ href: "/scan", labelKey: "scan", permission: ["shipment.scan", "manifest.create", "manifest.receive", "delivery.assign"], icon: ScanLine, group: "operations" }` placed right after Dashboard; `nav.scan` = "Scan station" / "स्क्यान स्टेसन".

- [ ] **Step 1: Write the failing station test**

`ScanStation.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderWithIntl } from "@/test/renderWithIntl";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/scan",
  useSearchParams: () => new URLSearchParams(search),
}));
let permissions: string[] = [];
vi.mock("@/features/auth/hooks/useSession", () => ({
  useSession: () => ({ data: { permissions, hasAllBranchesScope: true, branches: [] } }),
}));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => permissions.includes(p) }));
vi.mock("@/components/scanner/BarcodeScanner", () => ({ BarcodeScanner: () => <div>camera</div> }));
vi.mock("@/features/scanning/components/ScanContextPicker", () => ({ ScanContextPicker: () => <div>context-picker</div> }));
vi.mock("@/features/scanning/components/ReconciliationPanel", () => ({ ReconciliationPanel: () => <div>reconciliation</div> }));
vi.mock("@/features/scanning/beep", () => ({ playScanTone: vi.fn() }));
// Mocked so lookup queries (branches/users/riders) never consume the clientFetch responses
// queued for the scan calls below.
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ branchName: () => "Branch", riderName: () => "Rider", userName: () => "Staff" }),
}));
const clientFetch = vi.fn();
vi.mock("@/lib/api/clientFetch", async (orig) => ({ ...(await orig<object>()), clientFetch: (...a: unknown[]) => clientFetch(...a) }));

import { ScanStation } from "./ScanStation";

function renderStation() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithIntl(
    <QueryClientProvider client={qc}>
      <ScanStation />
    </QueryClientProvider>
  );
}

describe("ScanStation", () => {
  beforeEach(() => {
    search = "";
    replace.mockReset();
    clientFetch.mockReset();
    permissions = ["shipment.scan"];
  });

  it("shows only modes the user may use", () => {
    renderStation();
    expect(screen.getByRole("tab", { name: "Receive" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Sort" })).toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Dispatch" })).toBeNull();
    expect(screen.queryByRole("tab", { name: "Rider handover" })).toBeNull();
  });

  it("receives at origin from the manual input without a manifest", async () => {
    clientFetch.mockResolvedValueOnce({ id: "s1", awb: "QA-1", recipientName: "Sita", codAmount: 0 }).mockResolvedValueOnce({});
    renderStation();
    const input = screen.getByRole("textbox", { name: /scan or type awb/i });
    fireEvent.change(input, { target: { value: "qa-1" } });
    fireEvent.keyDown(input, { key: "Enter" });
    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("Accepted"));
    expect(clientFetch.mock.calls[1][0]).toBe("/api/shipments/s1/receive-origin");
    expect(screen.getByText("1 scans this session")).toBeInTheDocument();
  });

  it("blocks scanning until required context is chosen", () => {
    permissions = ["manifest.create", "manifest.read"];
    search = "mode=dispatch";
    renderStation();
    expect(screen.getByRole("textbox", { name: /scan or type awb/i })).toBeDisabled();
    expect(screen.getByText("Choose a manifest to start scanning.")).toBeInTheDocument();
  });

  it("sort mode is a needs-backend slot", () => {
    search = "mode=sort";
    renderStation();
    expect(screen.getByRole("region", { name: "Sorting" })).toHaveAttribute("data-coming-soon");
    expect(screen.queryByRole("textbox", { name: /scan or type awb/i })).toBeNull();
  });

  it("switching mode writes it to the URL", () => {
    permissions = ["shipment.scan", "manifest.create", "manifest.read"];
    renderStation();
    fireEvent.mouseDown(screen.getByRole("tab", { name: "Dispatch" }));
    expect(replace).toHaveBeenCalledWith("/scan?mode=dispatch", { scroll: false });
  });

  it("shows a no-access message with no scan permissions", () => {
    permissions = [];
    renderStation();
    expect(screen.getByText("You don't have permission to scan parcels.")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/features/scanning/components/ScanStation.test.tsx` → FAIL.

- [ ] **Step 3: Implement `ScanStation` and the route**

`ScanStation.tsx`:

```tsx
"use client";

import { useEffect, useRef, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useQueryClient } from "@tanstack/react-query";
import { Volume2, VolumeX } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { PageHeader } from "@/components/page/PageHeader";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { EmptyState } from "@/components/states/EmptyState";
import { BarcodeScanner } from "@/components/scanner/BarcodeScanner";
import { useSession } from "@/features/auth/hooks/useSession";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { SCAN_MODES, getScanMode, type ScanContext, type ScanOutcome } from "@/features/scanning/modes";
import { useScanSession } from "@/features/scanning/useScanSession";
import { playScanTone } from "@/features/scanning/beep";
import { ManualScanInput } from "./ManualScanInput";
import { ScanResultCard } from "./ScanResultCard";
import { ScanLog } from "./ScanLog";
import { ScanContextPicker } from "./ScanContextPicker";
import { ReconciliationPanel } from "./ReconciliationPanel";

const SOUND_KEY = "scan-station-sound";

function readSound(): boolean {
  try {
    return localStorage.getItem(SOUND_KEY) !== "off";
  } catch {
    return true;
  }
}

export function ScanStation() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const queryClient = useQueryClient();
  const { data: session } = useSession();
  const names = usePeopleNames();
  const openManifest = useManifestPanelStore((s) => s.openDetail);

  const granted = session?.permissions ?? [];
  const can = (p: string) => granted.includes(p);
  const inScope = (branchId: string) => Boolean(session?.hasAllBranchesScope || session?.branches.some((b) => b.branchId === branchId));
  const mode = getScanMode(searchParams.get("mode"), can);

  const [context, setContext] = useState<ScanContext>({ kind: "none" });
  const [sound, setSound] = useState(true);
  const inputRef = useRef<HTMLInputElement>(null);
  useEffect(() => setSound(readSound()), []);
  useEffect(() => setContext({ kind: "none" }), [mode?.id]);

  const soundRef = useRef(sound);
  soundRef.current = sound;
  const onSettled = (outcome: ScanOutcome) => {
    if (soundRef.current) playScanTone(outcome.ok);
    if (outcome.ok) {
      void queryClient.invalidateQueries({ queryKey: ["manifests"] });
      void queryClient.invalidateQueries({ queryKey: ["shipments"] });
    }
    inputRef.current?.focus();
  };
  const { state, request } = useScanSession(mode, context, onSettled);

  if (!mode) {
    return <EmptyState title="You don't have permission to scan parcels." description="Ask an admin for scan, manifest or delivery-assign access." />;
  }

  const missingContext = mode.requiresContext(can) && context.kind === "none";
  const canScan = Boolean(mode.submit) && !missingContext;
  const manifest = context.kind === "manifest" ? context.manifest : null;

  function setMode(id: string) {
    router.replace(`${pathname}?mode=${id}`, { scroll: false });
  }
  function toggleSound() {
    const next = !sound;
    setSound(next);
    try {
      localStorage.setItem(SOUND_KEY, next ? "on" : "off");
    } catch {
      // preference is best-effort
    }
  }

  const chips: string[] = manifest
    ? [manifest.code, `${names.branchName(manifest.fromBranchId)} → ${names.branchName(manifest.toBranchId)}`, ...(manifest.vehiclePlate ? [`Vehicle ${manifest.vehiclePlate}`] : [])]
    : context.kind === "rider"
      ? [names.riderName(context.rider.id)]
      : [];

  return (
    <div className="space-y-5">
      <PageHeader
        title={mode.title(context)}
        badge={chips.length > 0 && (
          <span className="flex flex-wrap gap-1.5">
            {chips.map((c) => (
              <span key={c} className="rounded-md border border-border bg-card px-2.5 py-1 text-xs">{c}</span>
            ))}
          </span>
        )}
        actions={
          <>
            <Tabs value={mode.id} onValueChange={setMode}>
              <TabsList aria-label="Scan mode" className="flex-wrap">
                {SCAN_MODES.filter((m) => m.isAvailable(can)).map((m) => (
                  <TabsTrigger key={m.id} value={m.id}>{m.label}</TabsTrigger>
                ))}
              </TabsList>
            </Tabs>
            <Button type="button" variant="ghost" size="icon" aria-label={sound ? "Mute scan sounds" : "Unmute scan sounds"} aria-pressed={sound} onClick={toggleSound}>
              {sound ? <Volume2 className="h-4 w-4" aria-hidden="true" /> : <VolumeX className="h-4 w-4" aria-hidden="true" />}
            </Button>
          </>
        }
      />

      {!mode.submit ? (
        <ComingSoonSlot
          title="Sorting"
          description="Sorting happens when parcels are scanned onto a draft manifest — standalone sort and bins need backend."
          minHeight={240}
        />
      ) : (
        <>
          {mode.context !== "none" && (
            <div className="flex flex-col gap-1.5">
              <ScanContextPicker mode={mode} value={context} onChange={setContext} inScope={inScope} />
              {missingContext && (
                <p className="text-[13px] text-muted-foreground">
                  {mode.context === "rider" ? "Choose a rider to start scanning." : "Choose a manifest to start scanning."}
                </p>
              )}
            </div>
          )}
          <div className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(0,1fr)_380px]">
            <div className="flex min-w-0 flex-col gap-4">
              {canScan && <BarcodeScanner continuous manualEntry={false} onDetect={request} />}
              <ManualScanInput ref={inputRef} disabled={!canScan} onSubmit={request} />
              <ScanResultCard entry={state.last} />
              <ScanLog entries={state.log} />
            </div>
            <div className="flex flex-col gap-4">
              {mode.id === "receive" && manifest && <ReconciliationPanel manifest={manifest} />}
              {(mode.id === "dispatch" || mode.id === "rto") && manifest && (
                <section aria-label="Manifest" className="space-y-2 rounded-xl border border-border bg-card p-5">
                  <h2 className="font-display text-[15px] font-semibold">{manifest.code}</h2>
                  <p className="text-[13px] text-muted-foreground">To {names.branchName(manifest.toBranchId)}</p>
                  <p className="text-2xl font-semibold tabular-nums">{manifest.shipmentCount} <span className="text-sm font-normal text-muted-foreground">scanned</span></p>
                  <Button type="button" variant="outline" size="sm" onClick={() => openManifest(manifest.id)}>Open manifest</Button>
                </section>
              )}
              {mode.id === "handover" && context.kind === "rider" && (
                <section aria-label="Rider" className="space-y-1 rounded-xl border border-border bg-card p-5">
                  <h2 className="font-display text-[15px] font-semibold">{names.riderName(context.rider.id)}</h2>
                  <p className="text-[13px] text-muted-foreground">Base branch · {names.branchName(context.rider.baseBranchId)}</p>
                </section>
              )}
              {(mode.id === "receive" || mode.id === "dispatch") && (
                <ComingSoonSlot title="Sort bins" description="Which bin each parcel goes into, with live bin counts." />
              )}
            </div>
          </div>
        </>
      )}
    </div>
  );
}
```

The manifest's `shipmentCount` in the selected context is a snapshot; refresh it by deriving `manifest` from `useManifests()` data by id instead of the stored object if the reviewer flags staleness — acceptable either way since the reconciliation panel refetches `missing-shipments` on invalidation.

`src/web/src/app/(staff)/scan/page.tsx`:

```tsx
import { Suspense } from "react";
import { ScanStation } from "@/features/scanning/components/ScanStation";

export default function ScanPage() {
  return (
    <Suspense>
      <ScanStation />
    </Suspense>
  );
}
```

- [ ] **Step 4: Nav item and messages**

In `src/web/src/lib/navigation/navItems.ts`: add `ScanLine` to the lucide import; extend `NavLabelKey` with `| "scan"`; insert after the Dashboard entry:

```ts
  { href: "/scan", labelKey: "scan", permission: ["shipment.scan", "manifest.create", "manifest.receive", "delivery.assign"], icon: ScanLine, group: "operations" },
```

In `src/web/messages/en.json` under `"nav"` add `"scan": "Scan station",`; in `src/web/messages/ne.json` under `"nav"` add `"scan": "स्क्यान स्टेसन",`.

- [ ] **Step 5: Run the full web suite**

Run: `npx vitest run`
Expected: PASS. If nav/command-palette tests enumerate exact item lists, add the Scan station entry to their expectations (do not loosen assertions). Then `npx tsc --noEmit` (≤ 8 pre-existing), `npm run lint`, `npm run build`.

- [ ] **Step 6: Commit**

```bash
git add -A src/web/src/features/scanning "src/web/src/app/(staff)/scan" src/web/src/lib/navigation src/web/messages src/web/src/components/layout
git commit -m "feat(web): camera-first scan station at /scan with nav entry (F15 R2)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 13: Documentation

**Files:**
- Modify: `docs/API-SPEC.md`, `docs/RBAC.md`, `docs/CLARIFICATIONS.md`, `docs/FRONTEND-ROADMAP.md`, `DESIGN.md`

- [ ] **Step 1: API-SPEC.md**

Find the `GET /api/v1/shipments` section (`grep -n "GET /api/v1/shipments" docs/API-SPEC.md`). Add a query-parameter table identical to spec §3.1 (`status` repeatable, `q` ≥ 2 chars, `merchantId`, `serviceTypeId`, `branchId`, `createdFrom`, `createdTo`; 400 `SHIPMENT_VALIDATION_FAILED`), note the new `lastEventAt` response field, and add a new section:

```markdown
### GET /api/v1/shipments/{awb}/events

Staff only (`shipment.read`). Merchant callers → 403. Not found, other tenant, or outside the
caller's branch scope → 404. Returns the shipment's events oldest first:
`id, eventType, previousStatus, newStatus, branchId, userId, riderId, timestamp, notes`.
`metadata`, `latitude` and `longitude` are never returned.
```

Also add to `GET /api/v1/shipments/{awb}`: "Staff callers outside the shipment's branch scope (origin, destination or current branch) receive 404."

- [ ] **Step 2: RBAC.md and CLARIFICATIONS.md**

In `docs/RBAC.md`'s branch-scope section add: "Single-shipment reads (`GET /shipments/{awb}`, `/events`) apply the same origin/destination/current-branch predicate as the list; out-of-scope → 404 (F15 R2)."

Append to `docs/CLARIFICATIONS.md`:

```markdown
## 25. Branch scope on single-shipment reads (2026-10-05)

`GET /api/v1/shipments/{awb}` previously checked tenant and merchant ownership but not staff
branch scope, so a branch-restricted user could open any AWB in the organization. From F15 R2,
staff single reads (and the new `/{awb}/events`) require the shipment's origin, destination or
current branch to be in the caller's `shipment.read` scope — the same predicate the list uses.
Out-of-scope returns 404, not 403, so AWB existence is not disclosed. Internal scan lookups
(`GetScanInfoByAwbAsync`) are unchanged; scan and transition endpoints keep their own
branch-access checks.
```

- [ ] **Step 3: FRONTEND-ROADMAP.md and DESIGN.md**

In `docs/FRONTEND-ROADMAP.md` Phase F15 table, set R2's status to `Done — spec docs/superpowers/specs/2026-10-05-f15-r2-staff-operations-design.md` and add below the table a "**Backend follow-ups from R2:**" list copied from spec §10 (11 items).

In `DESIGN.md` add short entries: **Scan station** (camera-first continuous scanning with manual/wedge input always available; one result card with `aria-live`; session log; mode switcher hides unavailable modes), **Progress stepper** (7 forward steps, text labels always present, exception marker for NDR/RTO/Cancelled — never a fake Delivered), **"Right now" custody card** (cobalt-tint section at the top of the shipment Sheet). Keep wording consistent with the file's existing rule style.

- [ ] **Step 4: Commit**

```bash
git add docs DESIGN.md
git commit -m "docs: F15 R2 API, RBAC, clarification #25, roadmap and design notes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Self-Review Notes (plan author)

- Spec coverage: §3.1 → Task 1; §3.2/§3.3 → Task 2; §4 → Tasks 7–8; §5.1 → Tasks 3–4; §5.2 → Tasks 5–6; §6 → Tasks 9–12; §8 tests are embedded per task; §9 → Task 13.
- Deliberate refinements of the spec, all recorded in task text: 400 code is `SHIPMENT_VALIDATION_FAILED` (module convention) rather than `VALIDATION_ERROR`; scan mode lives in the URL and context in component state, with the reducer owning only queue + log; `FinancialSummary` is also removed (dashboard-only); rider display names come from `user.read` (riders carry no name of their own).
