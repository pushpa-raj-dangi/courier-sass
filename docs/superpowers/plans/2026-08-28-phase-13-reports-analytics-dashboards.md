# Phase 13 — Reports, Analytics, Advanced Dashboards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a backend-only `Reports` module exposing an org dashboard, a branch dashboard, and two CSV exports, computed live from real data across six existing modules, with correct tenant/branch scoping.

**Architecture:** New `Reports` module (no owned entities) composes five new narrow cross-module lookup interfaces — one per owning module (`Shipments`, `Manifests`, `Riders`, `Ndr`, `Cod`), each implemented by a plain class in that module's own `Application` layer, following the exact pattern `IBranchLookupService`/`IMerchantLookupService` already established. `Reports.Application`'s `OrgDashboardService`/`BranchDashboardService`/`ReportExportService` call these interfaces via DI and assemble responses; `Reports.Presentation.ReportsEndpoints` exposes four `GET` routes gated by `report.read`/`report.export`.

**Tech Stack:** ASP.NET Core Minimal APIs, EF Core 10 (Npgsql), xUnit, real Postgres for lookup-service tests (query-filter/tenant-isolation correctness), `WebApplicationFactory` for API-level tests.

**Spec:** `docs/superpowers/specs/2026-08-28-phase-13-reports-analytics-dashboards-design.md`

## Global Constraints

- No lookup interface method takes `organizationId` — every query relies on `AppDbContext`'s ambient tenant filter (directly, or transitively through `Rider` for `DeliveryTask`/`DeliveryRun`, which carry no filter of their own).
- Branch-scoped methods take a caller-verified `Guid branchId` or `IReadOnlySet<Guid>? branchIds` (`null` = every branch in the org) — never re-derive branch access inside a lookup implementation.
- Cross-module lookup interfaces are implemented by a **plain class in the owning module's `Application` layer** (e.g. `Manifests.Application.ManifestReportingLookupService`), registered `AddScoped<IInterface, ConcreteClass>()` — not a new partial-class file on an existing service, not `Infrastructure`.
- `Reports` owns no entities — no migration, no `Persistence.csproj` change, anywhere in this plan.
- Branch-access-denied on the branch dashboard throws `Reports.Application.ReportsValidationException`, mapped to `400 REPORTS_VALIDATION_FAILED` — never `403`, matching `ManifestService`/`NdrService`/`RiderService`/`CodService`'s existing `CheckBranchAccessAsync` convention exactly.
- Invalid `from`/`to` throws `Reports.Application.InvalidDateRangeException`, mapped to `400 INVALID_DATE_RANGE`.
- Use `_dbContext.Set<T>()` in every new query (this codebase's established convention — services hold a generic `DbContext`, not the concrete `AppDbContext`), never the typed `AppDbContext.Foo` properties.
- Real-Postgres lookup-service tests use `GeographyIds`/`PricingIds`' deterministic seeded reference rows (`GeographyIds.ProvinceId("Bagmati")`, `GeographyIds.DistrictId("Bagmati", "Kathmandu")`, `GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City")`, `PricingIds.ServiceTypeId(ServiceTypeCode.Standard)`) rather than creating new geography/service-type rows per test.

---

## Task 1: Scaffold the `Reports` module and `Reports.Tests` project

**Files:**
- Create: `src/Modules/Reports/Reports.csproj`
- Create: `src/Modules/Reports/Application/ReportsDtos.cs`
- Create: `tests/Reports.Tests/Reports.Tests.csproj`
- Create: `tests/Reports.Tests/ReportsValidationExceptionTests.cs`
- Modify: `Courier.slnx`

**Interfaces:**
- Produces: `Courier.Reports.Application.ReportsValidationException`, `Courier.Reports.Application.InvalidDateRangeException` — both plain `Exception` subclasses with a single `string message` constructor, consumed by every later task's error handling.

- [ ] **Step 1: Create `Reports.csproj`**

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

  <ItemGroup>
    <ProjectReference Include="..\..\SharedKernel\SharedKernel.csproj" />
    <ProjectReference Include="..\Branches\Branches.csproj" />
    <ProjectReference Include="..\Merchants\Merchants.csproj" />
    <ProjectReference Include="..\Shipments\Shipments.csproj" />
    <ProjectReference Include="..\Manifests\Manifests.csproj" />
    <ProjectReference Include="..\Riders\Riders.csproj" />
    <ProjectReference Include="..\Ndr\Ndr.csproj" />
    <ProjectReference Include="..\Cod\Cod.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add `ReportsDtos.cs`**

```csharp
namespace Courier.Reports.Application;

public sealed class ReportsValidationException : Exception
{
    public ReportsValidationException(string message) : base(message)
    {
    }
}

public sealed class InvalidDateRangeException : Exception
{
    public InvalidDateRangeException(string message) : base(message)
    {
    }
}
```

- [ ] **Step 3: Create `Reports.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.11" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Modules\Reports\Reports.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Write a trivial smoke test**

```csharp
using Courier.Reports.Application;

namespace Courier.Reports.Tests;

public class ReportsValidationExceptionTests
{
    [Fact]
    public void ReportsValidationException_CarriesMessage()
    {
        var ex = new ReportsValidationException("Branch is outside your branch access for permission 'report.read'.");
        Assert.Equal("Branch is outside your branch access for permission 'report.read'.", ex.Message);
    }

    [Fact]
    public void InvalidDateRangeException_CarriesMessage()
    {
        var ex = new InvalidDateRangeException("'from' must not be after 'to'.");
        Assert.Equal("'from' must not be after 'to'.", ex.Message);
    }
}
```

- [ ] **Step 5: Add both projects to `Courier.slnx`**

Insert `<Project Path="src/Modules/Reports/Reports.csproj" />` alphabetically among the other `src/Modules/*` entries (after `Pricing`, before `Riders`), and `<Project Path="tests/Reports.Tests/Reports.Tests.csproj" />` alphabetically among the `tests/*` entries (after `Pricing.Tests`, before `Riders.Tests`).

- [ ] **Step 6: Build and run**

Run: `dotnet build Courier.slnx`
Expected: succeeds, no errors.

Run: `dotnet test tests/Reports.Tests/Reports.Tests.csproj`
Expected: 2 passed.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Reports tests/Reports.Tests Courier.slnx
git commit -m "feat(reports): scaffold Reports module and test project"
```

---

## Task 2: Add bulk name-lookup methods to `IBranchLookupService` and `IMerchantLookupService`

**Files:**
- Modify: `src/Modules/Branches/Application/IBranchLookupService.cs`
- Modify: `src/Modules/Branches/Application/BranchService.cs`
- Modify: `src/Modules/Merchants/Application/IMerchantLookupService.cs`
- Modify: `src/Modules/Merchants/Application/MerchantLookupService.cs`
- Test: `tests/Branches.Tests/BranchServiceReportingTests.cs`
- Test: `tests/Merchants.Tests/MerchantLookupServiceReportingTests.cs`

**Interfaces:**
- Produces: `IBranchLookupService.GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken)` → `Task<IReadOnlyDictionary<Guid, string>>`; `IMerchantLookupService.GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken)` → `Task<IReadOnlyDictionary<Guid, string>>`. Both consumed by Task 9's `ReportExportService`.

- [ ] **Step 1: Write failing tests for both new methods**

`tests/Branches.Tests/BranchServiceReportingTests.cs` — check the existing `Branches.Tests` project for its DbContext/tenant-provider test setup convention (likely EF InMemory with a fake `ICurrentTenantProvider`) and follow it exactly; the two tests are:

```csharp
[Fact]
public async Task GetNamesByIdsAsync_ReturnsNameForEachRequestedBranch()
{
    // Arrange: seed two Branch rows for the current org via the InMemory context.
    // Act: call service.GetNamesByIdsAsync(new HashSet<Guid> { branch1.Id, branch2.Id });
    // Assert: dictionary has 2 entries, keyed by Id, valued by Name.
}

[Fact]
public async Task GetNamesByIdsAsync_EmptySet_ReturnsEmptyDictionary()
{
    // Act: call with an empty HashSet<Guid>.
    // Assert: result.Count == 0, and no DB round trip is required for correctness (behavior, not
    // an implementation detail — just assert the empty result).
}
```

`tests/Merchants.Tests/MerchantLookupServiceReportingTests.cs` — same shape against `GetDisplayNamesByIdsAsync`, asserting the returned values equal each seeded `Merchant.DisplayName`.

- [ ] **Step 2: Run tests, confirm they fail to compile** (methods don't exist yet)

Run: `dotnet test tests/Branches.Tests/Branches.Tests.csproj tests/Merchants.Tests/Merchants.Tests.csproj`
Expected: build error, `GetNamesByIdsAsync`/`GetDisplayNamesByIdsAsync` not found.

- [ ] **Step 3: Add the interface methods**

`IBranchLookupService.cs` — add alongside the existing `GetZoneInfoAsync`:

```csharp
Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default);
```

`IMerchantLookupService.cs` — add alongside `ExistsAsync`:

```csharp
Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement both methods**

In `BranchService.cs`, add:

```csharp
public async Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default)
{
    if (branchIds.Count == 0)
    {
        return new Dictionary<Guid, string>();
    }
    var idArray = branchIds.ToArray();
    var rows = await _dbContext.Set<Branch>()
        .Where(b => idArray.Contains(b.Id))
        .Select(b => new { b.Id, b.Name })
        .ToListAsync(cancellationToken);
    return rows.ToDictionary(r => r.Id, r => r.Name);
}
```

In `MerchantLookupService.cs`, add:

```csharp
public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default)
{
    if (merchantIds.Count == 0)
    {
        return new Dictionary<Guid, string>();
    }
    var idArray = merchantIds.ToArray();
    var rows = await _dbContext.Set<Merchant>()
        .Where(m => idArray.Contains(m.Id))
        .Select(m => new { m.Id, m.DisplayName })
        .ToListAsync(cancellationToken);
    return rows.ToDictionary(r => r.Id, r => r.DisplayName);
}
```

- [ ] **Step 5: Run tests, confirm they pass**

Run: `dotnet test tests/Branches.Tests/Branches.Tests.csproj tests/Merchants.Tests/Merchants.Tests.csproj`
Expected: all passed, including every pre-existing test in both projects (no regression).

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Branches/Application/IBranchLookupService.cs src/Modules/Branches/Application/BranchService.cs src/Modules/Merchants/Application/IMerchantLookupService.cs src/Modules/Merchants/Application/MerchantLookupService.cs tests/Branches.Tests/BranchServiceReportingTests.cs tests/Merchants.Tests/MerchantLookupServiceReportingTests.cs
git commit -m "feat(reports): add bulk name-lookup methods to IBranchLookupService and IMerchantLookupService"
```

---

## Task 3: `IShipmentReportingLookupService`

**Files:**
- Create: `src/Modules/Shipments/Application/IShipmentReportingLookupService.cs`
- Create: `src/Modules/Shipments/Application/ShipmentReportingLookupService.cs`
- Modify: `tests/Shipments.Tests/Shipments.Tests.csproj` (add `Persistence.csproj` reference + `Npgsql.EntityFrameworkCore.PostgreSQL` package)
- Create: `tests/Shipments.Tests/TestDbContextFactory.cs`
- Create: `tests/Shipments.Tests/ShipmentReportingLookupServiceTests.cs`

**Interfaces:**
- Consumes: `Courier.Geography.Infrastructure.GeographyIds`, `Courier.Pricing.Infrastructure.PricingIds`, `Courier.Pricing.Domain.ServiceTypeCode` (test-only, for seeded reference rows).
- Produces (consumed by Task 8's `OrgDashboardService`, Task 9's `ReportExportService`):
  - `GetCohortStatusCountsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken)` → `Task<IReadOnlyDictionary<ShipmentStatus, int>>`
  - `GetBranchShipmentCountsAsync(Guid branchId, CancellationToken)` → `Task<BranchShipmentCounts>` (`record BranchShipmentCounts(int Outgoing, int PendingSorting, int PendingDispatch)`)
  - `GetAverageDeliveryTimeAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken)` → `Task<TimeSpan?>`
  - `GetShipmentIdsByDestinationBranchAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken)` → `Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>`
  - `GetShipmentExportRowsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken)` → `Task<IReadOnlyList<ShipmentExportRow>>` (`record ShipmentExportRow(string Awb, ShipmentStatus Status, Guid OriginBranchId, Guid DestinationBranchId, Guid MerchantId, decimal CodAmount, decimal TotalCharge, DateTimeOffset CreatedAt, DateTimeOffset? DeliveredAt)`)
  - `GetAwbsByShipmentIdsAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken)` → `Task<IReadOnlyDictionary<Guid, string>>`

- [ ] **Step 1: Add the `Persistence` reference and Npgsql package to `Shipments.Tests.csproj`**

Add inside the existing `<ItemGroup>` with `PackageReference`s:

```xml
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
```

Add inside the existing `<ItemGroup>` with `ProjectReference`s:

```xml
<ProjectReference Include="..\..\src\Persistence\Persistence.csproj" />
```

- [ ] **Step 2: Add `TestDbContextFactory.cs`**

Copied from `tests/Identity.Tests/TestDbContextFactory.cs` verbatim except the namespace:

```csharp
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Courier.Shipments.Tests;

internal static class TestDbContextFactory
{
    private static readonly Lazy<bool> SchemaReady = new(EnsureSchema, LazyThreadSafetyMode.ExecutionAndPublication);

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

- [ ] **Step 3: Write the failing tests**

```csharp
using Courier.Geography.Infrastructure;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Tests;

public class ShipmentReportingLookupServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId { get; init; }
    }

    private static readonly Guid ProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private static readonly Guid ServiceTypeId = PricingIds.ServiceTypeId(ServiceTypeCode.Standard);

    private static async Task<(Guid OrgId, Guid OriginBranchId, Guid DestBranchId, Guid MerchantId)> SeedOrgAsync(AppDbContext dbContext)
    {
        var orgId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var destBranchId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        dbContext.Add(new Courier.Identity.Domain.Organization { Id = orgId, Name = $"Org {orgId}", Slug = $"org-{orgId:N}" });
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = originBranchId, OrganizationId = orgId, Code = "ORG", Name = "Origin Branch",
            Type = Courier.Branches.Domain.BranchType.Hub, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = destBranchId, OrganizationId = orgId, Code = "DST", Name = "Destination Branch",
            Type = Courier.Branches.Domain.BranchType.Branch, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        dbContext.Add(new Courier.Merchants.Domain.Merchant
        {
            Id = merchantId, OrganizationId = orgId, LegalName = "Test Merchant Pvt Ltd", DisplayName = "Test Merchant"
        });
        await dbContext.SaveChangesAsync();
        return (orgId, originBranchId, destBranchId, merchantId);
    }

    private static Shipment BuildShipment(Guid orgId, Guid originBranchId, Guid destBranchId, Guid merchantId, ShipmentStatus status, Guid? currentBranchId = null) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = orgId,
        Awb = $"AWB{Guid.NewGuid():N}"[..12],
        MerchantId = merchantId,
        OriginBranchId = originBranchId,
        DestinationBranchId = destBranchId,
        CurrentBranchId = currentBranchId,
        CurrentStatus = status,
        ServiceTypeId = ServiceTypeId,
        WeightKg = 1m,
        DeclaredValue = 100m,
        CodAmount = 500m,
        WantsInsurance = false,
        DeliveryCharge = 100m,
        InsuranceCharge = 0m,
        OtherCharges = 0m,
        TotalCharge = 100m,
        RecipientName = "Recipient", RecipientPhone = "9800000000",
        RecipientProvinceId = ProvinceId, RecipientDistrictId = DistrictId, RecipientLocalLevelId = LocalLevelId,
        RecipientWardNumber = 1, RecipientAddressLine = "Recipient Address",
        SenderName = "Sender", SenderPhone = "9800000001",
        SenderProvinceId = ProvinceId, SenderDistrictId = DistrictId, SenderLocalLevelId = LocalLevelId,
        SenderWardNumber = 1, SenderAddressLine = "Sender Address"
    };

    [Fact]
    public async Task GetCohortStatusCountsAsync_CountsOnlyShipmentsCreatedInRange_ByCurrentStatus()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var inRange = BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Delivered);
        var outOfRange = BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Delivered);
        dbContext.Add(inRange);
        dbContext.Add(outOfRange);

        var now = DateTimeOffset.UtcNow;
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = inRange.Id, EventType = ShipmentEventType.Created, NewStatus = ShipmentStatus.Created, Timestamp = now });
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = outOfRange.Id, EventType = ShipmentEventType.Created, NewStatus = ShipmentStatus.Created, Timestamp = now.AddDays(-5) });
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetCohortStatusCountsAsync(null, now.AddHours(-1), now.AddHours(1));

        Assert.Equal(1, result[ShipmentStatus.Delivered]);
    }

    [Fact]
    public async Task GetCohortStatusCountsAsync_ExcludesAnotherOrganizationsShipments()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgAId, originA, destA, merchantA) = await SeedOrgAsync(dbContext);
        var (orgBId, originB, destB, merchantB) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgAId;

        var shipmentA = BuildShipment(orgAId, originA, destA, merchantA, ShipmentStatus.Created);
        var shipmentB = BuildShipment(orgBId, originB, destB, merchantB, ShipmentStatus.Created);
        dbContext.Add(shipmentA);
        dbContext.Add(shipmentB);

        var now = DateTimeOffset.UtcNow;
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = shipmentA.Id, EventType = ShipmentEventType.Created, NewStatus = ShipmentStatus.Created, Timestamp = now });
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = shipmentB.Id, EventType = ShipmentEventType.Created, NewStatus = ShipmentStatus.Created, Timestamp = now });
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetCohortStatusCountsAsync(null, now.AddHours(-1), now.AddHours(1));

        Assert.Equal(1, result[ShipmentStatus.Created]);
    }

    [Fact]
    public async Task GetBranchShipmentCountsAsync_BucketsByCurrentStatusAtBranch()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        dbContext.Add(BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.AtOriginBranch, originBranchId));
        dbContext.Add(BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Sorted, originBranchId));
        dbContext.Add(BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Sorted, originBranchId));
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetBranchShipmentCountsAsync(originBranchId);

        Assert.Equal(1, result.PendingSorting);
        Assert.Equal(2, result.PendingDispatch);
        Assert.Equal(3, result.Outgoing);
    }

    [Fact]
    public async Task GetAverageDeliveryTimeAsync_AveragesCreatedToDeliveredDuration()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var shipment = BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Delivered);
        dbContext.Add(shipment);
        var created = DateTimeOffset.UtcNow.AddHours(-3);
        var delivered = created.AddHours(2);
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = shipment.Id, EventType = ShipmentEventType.Created, NewStatus = ShipmentStatus.Created, Timestamp = created });
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = shipment.Id, EventType = ShipmentEventType.Delivered, NewStatus = ShipmentStatus.Delivered, Timestamp = delivered });
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetAverageDeliveryTimeAsync(null, created.AddHours(-1), delivered.AddHours(1));

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromHours(2), result!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetShipmentIdsByDestinationBranchAsync_BucketsByDestinationBranch_RespectsBranchFilter()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var shipment = BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.InTransit);
        dbContext.Add(shipment);
        var now = DateTimeOffset.UtcNow;
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = shipment.Id, EventType = ShipmentEventType.Created, NewStatus = ShipmentStatus.Created, Timestamp = now });
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);

        var allBranches = await service.GetShipmentIdsByDestinationBranchAsync(null, now.AddHours(-1), now.AddHours(1));
        Assert.Contains(shipment.Id, allBranches[destBranchId]);

        var restricted = await service.GetShipmentIdsByDestinationBranchAsync(new HashSet<Guid> { originBranchId }, now.AddHours(-1), now.AddHours(1));
        Assert.False(restricted.ContainsKey(destBranchId));
    }

    [Fact]
    public async Task GetShipmentExportRowsAsync_ReturnsRowWithDeliveredAtNullWhenNotYetDelivered()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var shipment = BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.InTransit);
        dbContext.Add(shipment);
        var now = DateTimeOffset.UtcNow;
        dbContext.Add(new ShipmentEvent { Id = Guid.NewGuid(), ShipmentId = shipment.Id, EventType = ShipmentEventType.Created, NewStatus = ShipmentStatus.Created, Timestamp = now });
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var rows = await service.GetShipmentExportRowsAsync(null, now.AddHours(-1), now.AddHours(1));

        var row = Assert.Single(rows.Where(r => r.Awb == shipment.Awb));
        Assert.Null(row.DeliveredAt);
        Assert.Equal(shipment.MerchantId, row.MerchantId);
    }

    [Fact]
    public async Task GetAwbsByShipmentIdsAsync_ReturnsAwbPerId()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var shipment = BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Created);
        dbContext.Add(shipment);
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetAwbsByShipmentIdsAsync(new HashSet<Guid> { shipment.Id });

        Assert.Equal(shipment.Awb, result[shipment.Id]);
    }
}
```

- [ ] **Step 4: Run tests, confirm they fail to compile**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj`
Expected: build error — `IShipmentReportingLookupService`/`ShipmentReportingLookupService` don't exist yet.

- [ ] **Step 5: Add the interface**

```csharp
using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

public record BranchShipmentCounts(int Outgoing, int PendingSorting, int PendingDispatch);

public record ShipmentExportRow(
    string Awb, ShipmentStatus Status, Guid OriginBranchId, Guid DestinationBranchId, Guid MerchantId,
    decimal CodAmount, decimal TotalCharge, DateTimeOffset CreatedAt, DateTimeOffset? DeliveredAt);

// Consumed by Courier.Reports.Application — no method takes organizationId; every query relies on
// AppDbContext's ambient tenant filter (Shipment/ShipmentEvent both carry their own OrganizationId).
public interface IShipmentReportingLookupService
{
    Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCohortStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<BranchShipmentCounts> GetBranchShipmentCountsAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<TimeSpan?> GetAverageDeliveryTimeAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetShipmentIdsByDestinationBranchAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShipmentExportRow>> GetShipmentExportRowsAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(
        IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6: Implement the service**

```csharp
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

public class ShipmentReportingLookupService : IShipmentReportingLookupService
{
    private readonly DbContext _dbContext;

    public ShipmentReportingLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCohortStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var createdShipmentIds = _dbContext.Set<ShipmentEvent>()
            .Where(e => e.EventType == ShipmentEventType.Created && e.Timestamp >= from && e.Timestamp < to)
            .Select(e => e.ShipmentId);

        var query = _dbContext.Set<Shipment>().Where(s => createdShipmentIds.Contains(s.Id));
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(s => branchIdArray.Contains(s.OriginBranchId));
        }

        var counts = await query
            .GroupBy(s => s.CurrentStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(x => x.Status, x => x.Count);
    }

    public async Task<BranchShipmentCounts> GetBranchShipmentCountsAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var pendingSorting = await _dbContext.Set<Shipment>()
            .CountAsync(s => s.CurrentBranchId == branchId && s.CurrentStatus == ShipmentStatus.AtOriginBranch, cancellationToken);
        var pendingDispatch = await _dbContext.Set<Shipment>()
            .CountAsync(s => s.CurrentBranchId == branchId && s.CurrentStatus == ShipmentStatus.Sorted, cancellationToken);
        return new BranchShipmentCounts(pendingSorting + pendingDispatch, pendingSorting, pendingDispatch);
    }

    public async Task<TimeSpan?> GetAverageDeliveryTimeAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var deliveredEvents = _dbContext.Set<ShipmentEvent>()
            .Where(e => e.EventType == ShipmentEventType.Delivered && e.Timestamp >= from && e.Timestamp < to);
        var createdEvents = _dbContext.Set<ShipmentEvent>().Where(e => e.EventType == ShipmentEventType.Created);

        var query =
            from delivered in deliveredEvents
            join created in createdEvents on delivered.ShipmentId equals created.ShipmentId
            join shipment in _dbContext.Set<Shipment>() on delivered.ShipmentId equals shipment.Id
            select new { shipment.OriginBranchId, DeliveredAt = delivered.Timestamp, CreatedAt = created.Timestamp };

        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(x => branchIdArray.Contains(x.OriginBranchId));
        }

        var durationTicks = await query.Select(x => (double)(x.DeliveredAt - x.CreatedAt).Ticks).ToListAsync(cancellationToken);
        return durationTicks.Count == 0 ? null : TimeSpan.FromTicks((long)durationTicks.Average());
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetShipmentIdsByDestinationBranchAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var createdShipmentIds = _dbContext.Set<ShipmentEvent>()
            .Where(e => e.EventType == ShipmentEventType.Created && e.Timestamp >= from && e.Timestamp < to)
            .Select(e => e.ShipmentId);

        var query = _dbContext.Set<Shipment>().Where(s => createdShipmentIds.Contains(s.Id));
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(s => branchIdArray.Contains(s.DestinationBranchId));
        }

        var rows = await query.Select(s => new { s.DestinationBranchId, s.Id }).ToListAsync(cancellationToken);
        return rows
            .GroupBy(r => r.DestinationBranchId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(r => r.Id).ToList());
    }

    public async Task<IReadOnlyList<ShipmentExportRow>> GetShipmentExportRowsAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var createdEvents = _dbContext.Set<ShipmentEvent>()
            .Where(e => e.EventType == ShipmentEventType.Created && e.Timestamp >= from && e.Timestamp < to);

        var query =
            from created in createdEvents
            join shipment in _dbContext.Set<Shipment>() on created.ShipmentId equals shipment.Id
            select new { Shipment = shipment, CreatedAt = created.Timestamp };

        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(x => branchIdArray.Contains(x.Shipment.OriginBranchId));
        }

        var rows = await query.ToListAsync(cancellationToken);
        var shipmentIds = rows.Select(r => r.Shipment.Id).ToList();
        var deliveredAtByShipmentId = await _dbContext.Set<ShipmentEvent>()
            .Where(e => e.EventType == ShipmentEventType.Delivered && shipmentIds.Contains(e.ShipmentId))
            .ToDictionaryAsync(e => e.ShipmentId, e => e.Timestamp, cancellationToken);

        return rows.Select(r => new ShipmentExportRow(
            r.Shipment.Awb, r.Shipment.CurrentStatus, r.Shipment.OriginBranchId, r.Shipment.DestinationBranchId,
            r.Shipment.MerchantId, r.Shipment.CodAmount, r.Shipment.TotalCharge, r.CreatedAt,
            deliveredAtByShipmentId.TryGetValue(r.Shipment.Id, out var deliveredAt) ? deliveredAt : null)).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(
        IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default)
    {
        if (shipmentIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }
        var idArray = shipmentIds.ToArray();
        var rows = await _dbContext.Set<Shipment>()
            .Where(s => idArray.Contains(s.Id))
            .Select(s => new { s.Id, s.Awb })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Id, r => r.Awb);
    }
}
```

- [ ] **Step 7: Run tests, confirm they pass**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj`
Expected: all passed. Requires the shared `courier-postgres-1` container (port 5434) to be running — check with `docker exec courier-postgres-1 pg_isready -U courier` first; do not `docker compose up` a second Postgres instance.

- [ ] **Step 8: Commit**

```bash
git add tests/Shipments.Tests/Shipments.Tests.csproj tests/Shipments.Tests/TestDbContextFactory.cs tests/Shipments.Tests/ShipmentReportingLookupServiceTests.cs src/Modules/Shipments/Application/IShipmentReportingLookupService.cs src/Modules/Shipments/Application/ShipmentReportingLookupService.cs
git commit -m "feat(reports): add IShipmentReportingLookupService"
```

---

## Task 4: `IManifestReportingLookupService`

**Files:**
- Create: `src/Modules/Manifests/Application/IManifestReportingLookupService.cs`
- Create: `src/Modules/Manifests/Application/ManifestReportingLookupService.cs`
- Modify: `tests/Manifests.Tests/Manifests.Tests.csproj` (add `Persistence.csproj` reference + Npgsql package, same as Task 3 Step 1)
- Create: `tests/Manifests.Tests/TestDbContextFactory.cs` (identical template to Task 3 Step 2, namespace `Courier.Manifests.Tests`)
- Create: `tests/Manifests.Tests/ManifestReportingLookupServiceTests.cs`

**Interfaces:**
- Produces (consumed by Task 8's `BranchDashboardService`):
  - `GetBranchManifestCountsAsync(Guid branchId, CancellationToken)` → `Task<BranchManifestCounts>` (`record BranchManifestCounts(int InTransitTowardBranch, int RecentlyClosed)`)
  - `GetOpenMissingShipmentExceptionCountAsync(Guid branchId, CancellationToken)` → `Task<int>`

- [ ] **Step 1: Csproj + `TestDbContextFactory.cs`**

Same edits as Task 3 Steps 1–2, applied to `tests/Manifests.Tests/`, namespace `Courier.Manifests.Tests`.

- [ ] **Step 2: Write the failing tests**

```csharp
using Courier.Geography.Infrastructure;
using Courier.Manifests.Application;
using Courier.Manifests.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Manifests.Tests;

public class ManifestReportingLookupServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId { get; init; }
    }

    private static readonly Guid ProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");

    private static async Task<(Guid OrgId, Guid FromBranchId, Guid ToBranchId)> SeedOrgAsync(Courier.Persistence.AppDbContext dbContext)
    {
        var orgId = Guid.NewGuid();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        dbContext.Add(new Courier.Identity.Domain.Organization { Id = orgId, Name = $"Org {orgId}", Slug = $"org-{orgId:N}" });
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = fromBranchId, OrganizationId = orgId, Code = "FRM", Name = "From Branch",
            Type = Courier.Branches.Domain.BranchType.Hub, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = toBranchId, OrganizationId = orgId, Code = "TO", Name = "To Branch",
            Type = Courier.Branches.Domain.BranchType.Branch, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        await dbContext.SaveChangesAsync();
        return (orgId, fromBranchId, toBranchId);
    }

    [Fact]
    public async Task GetBranchManifestCountsAsync_CountsInTransitAndRecentlyClosed()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, fromBranchId, toBranchId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var inTransit = new Manifest { Id = Guid.NewGuid(), OrganizationId = orgId, Code = "MNF001", FromBranchId = fromBranchId, ToBranchId = toBranchId, Purpose = ManifestPurpose.Forward, Status = ManifestStatus.InTransit, ShipmentCount = 1, CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var closedRecent = new Manifest { Id = Guid.NewGuid(), OrganizationId = orgId, Code = "MNF002", FromBranchId = fromBranchId, ToBranchId = toBranchId, Purpose = ManifestPurpose.Forward, Status = ManifestStatus.Closed, ShipmentCount = 1, CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        dbContext.Add(inTransit);
        dbContext.Add(closedRecent);
        dbContext.Add(new ManifestShipment { Id = Guid.NewGuid(), ManifestId = closedRecent.Id, ShipmentId = Guid.NewGuid(), ScannedAt = DateTimeOffset.UtcNow.AddDays(-2), ReceivedAt = DateTimeOffset.UtcNow.AddHours(-1) });
        await dbContext.SaveChangesAsync();

        var service = new ManifestReportingLookupService(dbContext);
        var result = await service.GetBranchManifestCountsAsync(toBranchId);

        Assert.Equal(1, result.InTransitTowardBranch);
        Assert.Equal(1, result.RecentlyClosed);
    }

    [Fact]
    public async Task GetBranchManifestCountsAsync_ExcludesAnotherOrganizationsManifests()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgAId, fromA, toA) = await SeedOrgAsync(dbContext);
        var (orgBId, fromB, toB) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgAId;

        dbContext.Add(new Manifest { Id = Guid.NewGuid(), OrganizationId = orgAId, Code = "A001", FromBranchId = fromA, ToBranchId = toA, Purpose = ManifestPurpose.Forward, Status = ManifestStatus.InTransit, ShipmentCount = 1, CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow });
        dbContext.Add(new Manifest { Id = Guid.NewGuid(), OrganizationId = orgBId, Code = "B001", FromBranchId = fromB, ToBranchId = toA, Purpose = ManifestPurpose.Forward, Status = ManifestStatus.InTransit, ShipmentCount = 1, CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow });
        await dbContext.SaveChangesAsync();

        var service = new ManifestReportingLookupService(dbContext);
        var result = await service.GetBranchManifestCountsAsync(toA);

        Assert.Equal(1, result.InTransitTowardBranch);
    }

    [Fact]
    public async Task GetOpenMissingShipmentExceptionCountAsync_CountsUnreceivedRowsOnClosedManifests()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, fromBranchId, toBranchId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var closed = new Manifest { Id = Guid.NewGuid(), OrganizationId = orgId, Code = "MNF003", FromBranchId = fromBranchId, ToBranchId = toBranchId, Purpose = ManifestPurpose.Forward, Status = ManifestStatus.Closed, ShipmentCount = 2, CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Add(closed);
        dbContext.Add(new ManifestShipment { Id = Guid.NewGuid(), ManifestId = closed.Id, ShipmentId = Guid.NewGuid(), ScannedAt = DateTimeOffset.UtcNow, ReceivedAt = DateTimeOffset.UtcNow });
        dbContext.Add(new ManifestShipment { Id = Guid.NewGuid(), ManifestId = closed.Id, ShipmentId = Guid.NewGuid(), ScannedAt = DateTimeOffset.UtcNow, ReceivedAt = null });
        await dbContext.SaveChangesAsync();

        var service = new ManifestReportingLookupService(dbContext);
        var count = await service.GetOpenMissingShipmentExceptionCountAsync(toBranchId);

        Assert.Equal(1, count);
    }
}
```

- [ ] **Step 3: Run, confirm compile failure**

Run: `dotnet test tests/Manifests.Tests/Manifests.Tests.csproj`
Expected: build error — types don't exist yet.

- [ ] **Step 4: Add the interface**

```csharp
namespace Courier.Manifests.Application;

public record BranchManifestCounts(int InTransitTowardBranch, int RecentlyClosed);

public interface IManifestReportingLookupService
{
    Task<BranchManifestCounts> GetBranchManifestCountsAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<int> GetOpenMissingShipmentExceptionCountAsync(Guid branchId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Implement the service**

```csharp
using Courier.Manifests.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Manifests.Application;

public class ManifestReportingLookupService : IManifestReportingLookupService
{
    private readonly DbContext _dbContext;

    public ManifestReportingLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BranchManifestCounts> GetBranchManifestCountsAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var inTransitTowardBranch = await _dbContext.Set<Manifest>()
            .CountAsync(m => m.ToBranchId == branchId && m.Status == ManifestStatus.InTransit, cancellationToken);

        // No ClosedAt column exists on Manifest — the latest ManifestShipment.ReceivedAt across a
        // Closed manifest's rows is used as a proxy for "when it was closed" (this plan's resolved
        // design decision, since receiving is what drives a manifest to Closed in the first place).
        var recentCutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var recentlyClosed = await _dbContext.Set<Manifest>()
            .Where(m => m.ToBranchId == branchId && m.Status == ManifestStatus.Closed)
            .Where(m => _dbContext.Set<ManifestShipment>()
                .Any(ms => ms.ManifestId == m.Id && ms.ReceivedAt != null && ms.ReceivedAt >= recentCutoff))
            .CountAsync(cancellationToken);

        return new BranchManifestCounts(inTransitTowardBranch, recentlyClosed);
    }

    public async Task<int> GetOpenMissingShipmentExceptionCountAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var closedManifestIds = _dbContext.Set<Manifest>()
            .Where(m => m.ToBranchId == branchId && m.Status == ManifestStatus.Closed)
            .Select(m => m.Id);

        return await _dbContext.Set<ManifestShipment>()
            .CountAsync(ms => closedManifestIds.Contains(ms.ManifestId) && ms.ReceivedAt == null, cancellationToken);
    }
}
```

- [ ] **Step 6: Run tests, confirm they pass**

Run: `dotnet test tests/Manifests.Tests/Manifests.Tests.csproj`
Expected: all passed.

- [ ] **Step 7: Commit**

```bash
git add tests/Manifests.Tests/Manifests.Tests.csproj tests/Manifests.Tests/TestDbContextFactory.cs tests/Manifests.Tests/ManifestReportingLookupServiceTests.cs src/Modules/Manifests/Application/IManifestReportingLookupService.cs src/Modules/Manifests/Application/ManifestReportingLookupService.cs
git commit -m "feat(reports): add IManifestReportingLookupService"
```

---

## Task 5: `IRiderReportingLookupService`

**Files:**
- Create: `src/Modules/Riders/Application/IRiderReportingLookupService.cs`
- Create: `src/Modules/Riders/Application/RiderReportingLookupService.cs`
- Modify: `tests/Riders.Tests/Riders.Tests.csproj` (Persistence reference + Npgsql package)
- Create: `tests/Riders.Tests/TestDbContextFactory.cs` (namespace `Courier.Riders.Tests`)
- Create: `tests/Riders.Tests/RiderReportingLookupServiceTests.cs`

**Interfaces:**
- Produces (consumed by Task 8):
  - `GetBranchRiderTaskLoadAsync(Guid branchId, CancellationToken)` → `Task<IReadOnlyList<RiderTaskLoad>>` (`record RiderTaskLoad(Guid RiderId, int OpenTaskCount)`)
  - `GetSuccessRateByBranchAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken)` → `Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>>` (`record DeliverySuccessStats(int Attempts, int Successes)`)
  - `GetSuccessRateByRiderAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken)` → `Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>>`

- [ ] **Step 1: Csproj + `TestDbContextFactory.cs`**

Same edits as Task 3 Steps 1–2, applied to `tests/Riders.Tests/`, namespace `Courier.Riders.Tests`.

- [ ] **Step 2: Write the failing tests**

```csharp
using Courier.Geography.Infrastructure;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Tests;

public class RiderReportingLookupServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId { get; init; }
    }

    private static readonly Guid ProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");

    private static async Task<(Guid OrgId, Guid BranchId)> SeedOrgAsync(Courier.Persistence.AppDbContext dbContext)
    {
        var orgId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        dbContext.Add(new Courier.Identity.Domain.Organization { Id = orgId, Name = $"Org {orgId}", Slug = $"org-{orgId:N}" });
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = branchId, OrganizationId = orgId, Code = "BR1", Name = "Branch One",
            Type = Courier.Branches.Domain.BranchType.Branch, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        await dbContext.SaveChangesAsync();
        return (orgId, branchId);
    }

    private static Rider BuildRider(Guid orgId, Guid branchId) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = orgId, UserId = Guid.NewGuid(), BaseBranchId = branchId,
        LicenseNumber = $"LIC{Guid.NewGuid():N}"[..10], RoleType = RiderRoleType.DeliveryRider, IsActive = true
    };

    [Fact]
    public async Task GetBranchRiderTaskLoadAsync_CountsOnlyPendingTasks_ForActiveRidersAtBranch()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var rider = BuildRider(orgId, branchId);
        dbContext.Add(rider);
        var run = new DeliveryRun { Id = Guid.NewGuid(), RiderId = rider.Id, BranchId = branchId, Date = DateOnly.FromDateTime(DateTime.UtcNow), Status = DeliveryRunStatus.Active };
        dbContext.Add(run);
        dbContext.Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = Guid.NewGuid(), Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending });
        dbContext.Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = Guid.NewGuid(), Type = DeliveryTaskType.Delivery, Sequence = 2, Status = DeliveryTaskStatus.Completed });
        await dbContext.SaveChangesAsync();

        var service = new RiderReportingLookupService(dbContext);
        var result = await service.GetBranchRiderTaskLoadAsync(branchId);

        var load = Assert.Single(result);
        Assert.Equal(rider.Id, load.RiderId);
        Assert.Equal(1, load.OpenTaskCount);
    }

    [Fact]
    public async Task GetBranchRiderTaskLoadAsync_ExcludesAnotherOrganizationsRiders()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgAId, branchA) = await SeedOrgAsync(dbContext);
        var (orgBId, branchB) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgAId;

        dbContext.Add(BuildRider(orgAId, branchA));
        dbContext.Add(BuildRider(orgBId, branchA)); // same branch id would never really collide across orgs, but exercise the filter directly
        await dbContext.SaveChangesAsync();

        var service = new RiderReportingLookupService(dbContext);
        var result = await service.GetBranchRiderTaskLoadAsync(branchA);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetSuccessRateByBranchAsync_ComputesAttemptsAndSuccessesInRange()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var rider = BuildRider(orgId, branchId);
        dbContext.Add(rider);
        var now = DateTimeOffset.UtcNow;
        dbContext.Add(new DeliveryAttempt { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = Guid.NewGuid(), RiderId = rider.Id, AttemptNumber = 1, AttemptedAt = now, Succeeded = true, CustomerContacted = true });
        dbContext.Add(new DeliveryAttempt { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = Guid.NewGuid(), RiderId = rider.Id, AttemptNumber = 1, AttemptedAt = now, Succeeded = false, CustomerContacted = true });
        await dbContext.SaveChangesAsync();

        var service = new RiderReportingLookupService(dbContext);
        var result = await service.GetSuccessRateByBranchAsync(null, now.AddHours(-1), now.AddHours(1));

        Assert.Equal(2, result[branchId].Attempts);
        Assert.Equal(1, result[branchId].Successes);
    }

    [Fact]
    public async Task GetSuccessRateByRiderAsync_BucketsPerRiderId()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var rider = BuildRider(orgId, branchId);
        dbContext.Add(rider);
        var now = DateTimeOffset.UtcNow;
        dbContext.Add(new DeliveryAttempt { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = Guid.NewGuid(), RiderId = rider.Id, AttemptNumber = 1, AttemptedAt = now, Succeeded = true, CustomerContacted = true });
        await dbContext.SaveChangesAsync();

        var service = new RiderReportingLookupService(dbContext);
        var result = await service.GetSuccessRateByRiderAsync(null, now.AddHours(-1), now.AddHours(1));

        Assert.Equal(1, result[rider.Id].Attempts);
        Assert.Equal(1, result[rider.Id].Successes);
    }
}
```

- [ ] **Step 3: Run, confirm compile failure**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj`
Expected: build error.

- [ ] **Step 4: Add the interface**

```csharp
namespace Courier.Riders.Application;

public record RiderTaskLoad(Guid RiderId, int OpenTaskCount);
public record DeliverySuccessStats(int Attempts, int Successes);

// DeliveryTask/DeliveryRun carry no OrganizationId of their own — every implementation of this
// interface must query them only through a join to Rider, never DbSet<DeliveryTask>()/
// DbSet<DeliveryRun>() standalone.
public interface IRiderReportingLookupService
{
    Task<IReadOnlyList<RiderTaskLoad>> GetBranchRiderTaskLoadAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>> GetSuccessRateByBranchAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>> GetSuccessRateByRiderAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Implement the service**

```csharp
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public class RiderReportingLookupService : IRiderReportingLookupService
{
    private readonly DbContext _dbContext;

    public RiderReportingLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<RiderTaskLoad>> GetBranchRiderTaskLoadAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        // Rider is ambient-filtered; this list is therefore already tenant-scoped before the
        // DeliveryTask/DeliveryRun join below ever runs.
        var riderIds = await _dbContext.Set<Rider>()
            .Where(r => r.BaseBranchId == branchId && r.IsActive)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var openTaskCounts = await (
            from task in _dbContext.Set<DeliveryTask>()
            join run in _dbContext.Set<DeliveryRun>() on task.DeliveryRunId equals run.Id
            where task.Status == DeliveryTaskStatus.Pending && riderIds.Contains(run.RiderId)
            group task by run.RiderId into g
            select new { RiderId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var countsByRider = openTaskCounts.ToDictionary(x => x.RiderId, x => x.Count);
        return riderIds.Select(riderId => new RiderTaskLoad(riderId, countsByRider.GetValueOrDefault(riderId))).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>> GetSuccessRateByBranchAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var query =
            from attempt in _dbContext.Set<DeliveryAttempt>()
            join rider in _dbContext.Set<Rider>() on attempt.RiderId equals rider.Id
            where attempt.AttemptedAt >= from && attempt.AttemptedAt < to
            select new { rider.BaseBranchId, attempt.Succeeded };

        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(x => branchIdArray.Contains(x.BaseBranchId));
        }

        var grouped = await query
            .GroupBy(x => x.BaseBranchId)
            .Select(g => new { BranchId = g.Key, Attempts = g.Count(), Successes = g.Count(x => x.Succeeded) })
            .ToListAsync(cancellationToken);

        return grouped.ToDictionary(g => g.BranchId, g => new DeliverySuccessStats(g.Attempts, g.Successes));
    }

    public async Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>> GetSuccessRateByRiderAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var query =
            from attempt in _dbContext.Set<DeliveryAttempt>()
            join rider in _dbContext.Set<Rider>() on attempt.RiderId equals rider.Id
            where attempt.AttemptedAt >= from && attempt.AttemptedAt < to
            select new { RiderId = rider.Id, rider.BaseBranchId, attempt.Succeeded };

        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(x => branchIdArray.Contains(x.BaseBranchId));
        }

        var grouped = await query
            .GroupBy(x => x.RiderId)
            .Select(g => new { RiderId = g.Key, Attempts = g.Count(), Successes = g.Count(x => x.Succeeded) })
            .ToListAsync(cancellationToken);

        return grouped.ToDictionary(g => g.RiderId, g => new DeliverySuccessStats(g.Attempts, g.Successes));
    }
}
```

- [ ] **Step 6: Run tests, confirm they pass**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj`
Expected: all passed.

- [ ] **Step 7: Commit**

```bash
git add tests/Riders.Tests/Riders.Tests.csproj tests/Riders.Tests/TestDbContextFactory.cs tests/Riders.Tests/RiderReportingLookupServiceTests.cs src/Modules/Riders/Application/IRiderReportingLookupService.cs src/Modules/Riders/Application/RiderReportingLookupService.cs
git commit -m "feat(reports): add IRiderReportingLookupService"
```

---

## Task 6: `INdrReportingLookupService`

**Files:**
- Create: `src/Modules/Ndr/Application/INdrReportingLookupService.cs`
- Create: `src/Modules/Ndr/Application/NdrReportingLookupService.cs`
- Modify: `tests/Ndr.Tests/Ndr.Tests.csproj` (Persistence reference + Npgsql package)
- Create: `tests/Ndr.Tests/TestDbContextFactory.cs` (namespace `Courier.Ndr.Tests`)
- Create: `tests/Ndr.Tests/NdrReportingLookupServiceTests.cs`

**Interfaces:**
- Consumes: `Courier.Shipments.Application.IShipmentTransitionService.GetScanInfoAsync` (existing, from Phase 7/9 — `Ndr.csproj` already references `Shipments.csproj`).
- Produces (consumed by Task 8):
  - `GetOpenNdrCountForBranchAsync(Guid branchId, CancellationToken)` → `Task<int>`
  - `GetShipmentIdsWithAnyNdrAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken)` → `Task<IReadOnlySet<Guid>>`
  - `GetShipmentIdsWithRtoAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken)` → `Task<IReadOnlySet<Guid>>`

- [ ] **Step 1: Csproj + `TestDbContextFactory.cs`**

Same edits as Task 3 Steps 1–2, applied to `tests/Ndr.Tests/`, namespace `Courier.Ndr.Tests`.

- [ ] **Step 2: Write the failing tests**

For `GetOpenNdrCountForBranchAsync`, the implementation depends on `IShipmentTransitionService.GetScanInfoAsync`, so the test needs a real `Shipment` row too (the same `SeedOrgAsync`/`BuildShipment` shape as Task 3's tests, trimmed to what NDR needs) — reuse `Courier.Shipments.Application.ShipmentService` isn't required, a plain `Shipment` insert plus the real `ShipmentReportingLookupService`... no: `GetScanInfoAsync` is implemented by `Shipments.Application.ShipmentService`, not by the reporting lookup service. Construct a `ShipmentService` directly (it has several constructor dependencies — for this narrow test, only `GetScanInfoAsync` is exercised, which only touches `_dbContext`) or, simpler and matching this module's own `NdrService.Reads.cs` precedent, use a minimal **fake** `IShipmentTransitionService` that reads the same `_dbContext` directly:

```csharp
using Courier.Geography.Infrastructure;
using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.SharedKernel;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Tests;

public class NdrReportingLookupServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId { get; init; }
    }

    // Only GetScanInfoAsync is exercised by NdrReportingLookupService — this fake reads the same
    // shared DbContext directly rather than pulling in the full ShipmentService dependency graph,
    // matching how Manifests.Tests/Ndr.Tests' own fixtures already fake this interface per-file.
    private sealed class DbBackedShipmentTransitionServiceFake : IShipmentTransitionService
    {
        private readonly DbContext _dbContext;
        public DbBackedShipmentTransitionServiceFake(DbContext dbContext) => _dbContext = dbContext;

        public async Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default)
        {
            var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
            return shipment is null ? null : new ShipmentScanInfo(shipment.Id, shipment.OrganizationId, shipment.Awb, shipment.CurrentStatus, shipment.CurrentBranchId, shipment.OriginBranchId, shipment.DestinationBranchId, shipment.CurrentCustodianRiderId);
        }

        public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> SortAsync(Guid shipmentId, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static readonly Guid ProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private static readonly Guid ServiceTypeId = Courier.Pricing.Infrastructure.PricingIds.ServiceTypeId(Courier.Pricing.Domain.ServiceTypeCode.Standard);

    private static async Task<(Guid OrgId, Guid BranchId, Guid MerchantId)> SeedOrgAsync(Courier.Persistence.AppDbContext dbContext)
    {
        var orgId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        dbContext.Add(new Courier.Identity.Domain.Organization { Id = orgId, Name = $"Org {orgId}", Slug = $"org-{orgId:N}" });
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = branchId, OrganizationId = orgId, Code = "BR1", Name = "Branch One",
            Type = Courier.Branches.Domain.BranchType.Branch, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        dbContext.Add(new Courier.Merchants.Domain.Merchant { Id = merchantId, OrganizationId = orgId, LegalName = "Test Merchant Pvt Ltd", DisplayName = "Test Merchant" });
        await dbContext.SaveChangesAsync();
        return (orgId, branchId, merchantId);
    }

    private static Shipment BuildShipment(Guid orgId, Guid branchId, Guid merchantId) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = orgId, Awb = $"AWB{Guid.NewGuid():N}"[..12], MerchantId = merchantId,
        OriginBranchId = branchId, DestinationBranchId = branchId, CurrentStatus = ShipmentStatus.NdrCreated,
        ServiceTypeId = ServiceTypeId, WeightKg = 1m, DeclaredValue = 100m, CodAmount = 500m, WantsInsurance = false,
        DeliveryCharge = 100m, InsuranceCharge = 0m, OtherCharges = 0m, TotalCharge = 100m,
        RecipientName = "R", RecipientPhone = "9800000000", RecipientProvinceId = ProvinceId, RecipientDistrictId = DistrictId,
        RecipientLocalLevelId = LocalLevelId, RecipientWardNumber = 1, RecipientAddressLine = "Addr",
        SenderName = "S", SenderPhone = "9800000001", SenderProvinceId = ProvinceId, SenderDistrictId = DistrictId,
        SenderLocalLevelId = LocalLevelId, SenderWardNumber = 1, SenderAddressLine = "Addr"
    };

    [Fact]
    public async Task GetOpenNdrCountForBranchAsync_CountsOnlyUnresolvedNdrsAtThatBranch()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var shipment = BuildShipment(orgId, branchId, merchantId);
        dbContext.Add(shipment);
        dbContext.Add(new NDR { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = shipment.Id, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open });
        var resolvedShipment = BuildShipment(orgId, branchId, merchantId);
        dbContext.Add(resolvedShipment);
        dbContext.Add(new NDR { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = resolvedShipment.Id, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Resolved, Resolution = NdrResolution.Reattempt, ResolvedAt = DateTimeOffset.UtcNow });
        await dbContext.SaveChangesAsync();

        var service = new NdrReportingLookupService(dbContext, new DbBackedShipmentTransitionServiceFake(dbContext));
        var count = await service.GetOpenNdrCountForBranchAsync(branchId);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetShipmentIdsWithAnyNdrAsync_ReturnsOnlyIdsWithAnNdrRow()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var withNdr = BuildShipment(orgId, branchId, merchantId);
        var withoutNdr = BuildShipment(orgId, branchId, merchantId);
        dbContext.Add(withNdr);
        dbContext.Add(withoutNdr);
        dbContext.Add(new NDR { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = withNdr.Id, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open });
        await dbContext.SaveChangesAsync();

        var service = new NdrReportingLookupService(dbContext, new DbBackedShipmentTransitionServiceFake(dbContext));
        var result = await service.GetShipmentIdsWithAnyNdrAsync(new HashSet<Guid> { withNdr.Id, withoutNdr.Id });

        Assert.Contains(withNdr.Id, result);
        Assert.DoesNotContain(withoutNdr.Id, result);
    }

    [Fact]
    public async Task GetShipmentIdsWithRtoAsync_ReturnsOnlyOriginalShipmentIdsWithAReturnShipment()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var withRto = BuildShipment(orgId, branchId, merchantId);
        dbContext.Add(withRto);
        dbContext.Add(new ReturnShipment { Id = Guid.NewGuid(), OrganizationId = orgId, OriginalShipmentId = withRto.Id, ReturnAwb = "RTN000001", RtoReason = "CustomerRefused", OriginBranchId = branchId, DestinationBranchId = branchId });
        await dbContext.SaveChangesAsync();

        var service = new NdrReportingLookupService(dbContext, new DbBackedShipmentTransitionServiceFake(dbContext));
        var result = await service.GetShipmentIdsWithRtoAsync(new HashSet<Guid> { withRto.Id });

        Assert.Contains(withRto.Id, result);
    }

    [Fact]
    public async Task GetShipmentIdsWithAnyNdrAsync_EmptyInput_ReturnsEmptySet()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var service = new NdrReportingLookupService(dbContext, new DbBackedShipmentTransitionServiceFake(dbContext));

        var result = await service.GetShipmentIdsWithAnyNdrAsync(new HashSet<Guid>());

        Assert.Empty(result);
    }
}
```

- [ ] **Step 3: Run, confirm compile failure**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj`
Expected: build error.

- [ ] **Step 4: Add the interface**

```csharp
namespace Courier.Ndr.Application;

public interface INdrReportingLookupService
{
    Task<int> GetOpenNdrCountForBranchAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetShipmentIdsWithAnyNdrAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetShipmentIdsWithRtoAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Implement the service**

```csharp
using Courier.Ndr.Domain;
using Courier.Shipments.Application;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public class NdrReportingLookupService : INdrReportingLookupService
{
    private readonly DbContext _dbContext;
    private readonly IShipmentTransitionService _shipmentTransitionService;

    public NdrReportingLookupService(DbContext dbContext, IShipmentTransitionService shipmentTransitionService)
    {
        _dbContext = dbContext;
        _shipmentTransitionService = shipmentTransitionService;
    }

    // Reuses NdrService.ListAsync's established per-row GetScanInfoAsync branch-resolution
    // pattern (Phase 9) — NDR carries no branch column of its own.
    public async Task<int> GetOpenNdrCountForBranchAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var openNdrShipmentIds = await _dbContext.Set<NDR>()
            .Where(n => n.Status != NdrStatus.Resolved)
            .Select(n => n.ShipmentId)
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var shipmentId in openNdrShipmentIds)
        {
            var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(shipmentId, cancellationToken);
            if (scanInfo is not null && scanInfo.DestinationBranchId == branchId)
            {
                count++;
            }
        }
        return count;
    }

    public async Task<IReadOnlySet<Guid>> GetShipmentIdsWithAnyNdrAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default)
    {
        if (shipmentIds.Count == 0)
        {
            return new HashSet<Guid>();
        }
        var idArray = shipmentIds.ToArray();
        var matched = await _dbContext.Set<NDR>()
            .Where(n => idArray.Contains(n.ShipmentId))
            .Select(n => n.ShipmentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return matched.ToHashSet();
    }

    public async Task<IReadOnlySet<Guid>> GetShipmentIdsWithRtoAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default)
    {
        if (shipmentIds.Count == 0)
        {
            return new HashSet<Guid>();
        }
        var idArray = shipmentIds.ToArray();
        var matched = await _dbContext.Set<ReturnShipment>()
            .Where(r => idArray.Contains(r.OriginalShipmentId))
            .Select(r => r.OriginalShipmentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return matched.ToHashSet();
    }
}
```

- [ ] **Step 6: Run tests, confirm they pass**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj`
Expected: all passed.

- [ ] **Step 7: Commit**

```bash
git add tests/Ndr.Tests/Ndr.Tests.csproj tests/Ndr.Tests/TestDbContextFactory.cs tests/Ndr.Tests/NdrReportingLookupServiceTests.cs src/Modules/Ndr/Application/INdrReportingLookupService.cs src/Modules/Ndr/Application/NdrReportingLookupService.cs
git commit -m "feat(reports): add INdrReportingLookupService"
```

---

## Task 7: `ICodReportingLookupService`

**Files:**
- Create: `src/Modules/Cod/Application/ICodReportingLookupService.cs`
- Create: `src/Modules/Cod/Application/CodReportingLookupService.cs`
- Modify: `tests/Cod.Tests/Cod.Tests.csproj` (Persistence reference + Npgsql package)
- Create: `tests/Cod.Tests/TestDbContextFactory.cs` (namespace `Courier.Cod.Tests`)
- Create: `tests/Cod.Tests/CodReportingLookupServiceTests.cs`

**Interfaces:**
- Produces (consumed by Task 8's `OrgDashboardService`/`BranchDashboardService`, Task 9's `ReportExportService`):
  - `GetOrgFinancialSummaryAsync(IReadOnlySet<Guid>? branchIds, CancellationToken)` → `Task<CodFinancialSummary>` (`record CodFinancialSummary(decimal Expected, decimal Collected, decimal Pending, decimal Settled, decimal MerchantPayable)`)
  - `GetBranchPendingFinanceVerificationTotalAsync(Guid branchId, CancellationToken)` → `Task<decimal>`
  - `GetCodExportRowsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken)` → `Task<IReadOnlyList<CodExportRow>>` (`record CodExportRow(Guid ShipmentId, Guid MerchantId, Guid BranchId, decimal ExpectedAmount, decimal? CollectedAmount, CodCollectionStatus Status, DateTimeOffset? CollectedAt)`)

- [ ] **Step 1: Csproj + `TestDbContextFactory.cs`**

Same edits as Task 3 Steps 1–2, applied to `tests/Cod.Tests/`, namespace `Courier.Cod.Tests`.

- [ ] **Step 2: Write the failing tests**

```csharp
using Courier.Cod.Application;
using Courier.Cod.Domain;
using Courier.Geography.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Tests;

public class CodReportingLookupServiceTests
{
    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public Guid? OrganizationId { get; init; }
    }

    private static readonly Guid ProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");

    private static async Task<(Guid OrgId, Guid BranchId, Guid MerchantId)> SeedOrgAsync(Courier.Persistence.AppDbContext dbContext)
    {
        var orgId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        dbContext.Add(new Courier.Identity.Domain.Organization { Id = orgId, Name = $"Org {orgId}", Slug = $"org-{orgId:N}" });
        dbContext.Add(new Courier.Branches.Domain.Branch
        {
            Id = branchId, OrganizationId = orgId, Code = "BR1", Name = "Branch One",
            Type = Courier.Branches.Domain.BranchType.Branch, ProvinceId = ProvinceId, DistrictId = DistrictId,
            LocalLevelId = LocalLevelId, WardNumber = 1, AddressLine = "Test Address"
        });
        dbContext.Add(new Courier.Merchants.Domain.Merchant { Id = merchantId, OrganizationId = orgId, LegalName = "Test Merchant Pvt Ltd", DisplayName = "Test Merchant" });
        await dbContext.SaveChangesAsync();
        return (orgId, branchId, merchantId);
    }

    [Fact]
    public async Task GetOrgFinancialSummaryAsync_ComputesExpectedCollectedPendingSettledMerchantPayable()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        dbContext.Add(new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 500m, ShipmentTotalCharge = 100m, Status = CodCollectionStatus.CodExpected });
        dbContext.Add(new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 300m, ShipmentTotalCharge = 100m, CollectedAmount = 300m, Status = CodCollectionStatus.Settled });
        var settlement = new MerchantSettlement { Id = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, PeriodFrom = DateTimeOffset.UtcNow.AddDays(-7), PeriodTo = DateTimeOffset.UtcNow, TotalPayable = 250m, Status = MerchantSettlementStatus.Settled, SettledAt = DateTimeOffset.UtcNow };
        var pendingSettlement = new MerchantSettlement { Id = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, PeriodFrom = DateTimeOffset.UtcNow.AddDays(-7), PeriodTo = DateTimeOffset.UtcNow, TotalPayable = 90m, Status = MerchantSettlementStatus.Created };
        dbContext.Add(settlement);
        dbContext.Add(pendingSettlement);
        await dbContext.SaveChangesAsync();

        var service = new CodReportingLookupService(dbContext);
        var result = await service.GetOrgFinancialSummaryAsync(null);

        Assert.Equal(500m, result.Expected);
        Assert.Equal(300m, result.Collected);
        Assert.Equal(50m, result.Pending);
        Assert.Equal(250m, result.Settled);
        Assert.Equal(90m, result.MerchantPayable);
    }

    [Fact]
    public async Task GetOrgFinancialSummaryAsync_ExcludesAnotherOrganizationsCollections()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgAId, branchA, merchantA) = await SeedOrgAsync(dbContext);
        var (orgBId, branchB, merchantB) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgAId;

        dbContext.Add(new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgAId, MerchantId = merchantA, BranchId = branchA, ExpectedAmount = 500m, ShipmentTotalCharge = 100m, Status = CodCollectionStatus.CodExpected });
        dbContext.Add(new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgBId, MerchantId = merchantB, BranchId = branchB, ExpectedAmount = 9000m, ShipmentTotalCharge = 100m, Status = CodCollectionStatus.CodExpected });
        await dbContext.SaveChangesAsync();

        var service = new CodReportingLookupService(dbContext);
        var result = await service.GetOrgFinancialSummaryAsync(null);

        Assert.Equal(500m, result.Expected);
    }

    [Fact]
    public async Task GetBranchPendingFinanceVerificationTotalAsync_SumsRiderDepositedAndBranchReconciled()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        dbContext.Add(new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 200m, ShipmentTotalCharge = 50m, CollectedAmount = 200m, Status = CodCollectionStatus.RiderDeposited });
        dbContext.Add(new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 150m, ShipmentTotalCharge = 50m, CollectedAmount = 150m, Status = CodCollectionStatus.BranchReconciled });
        dbContext.Add(new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 999m, ShipmentTotalCharge = 50m, CollectedAmount = 999m, Status = CodCollectionStatus.Settled });
        await dbContext.SaveChangesAsync();

        var service = new CodReportingLookupService(dbContext);
        var total = await service.GetBranchPendingFinanceVerificationTotalAsync(branchId);

        Assert.Equal(350m, total);
    }

    [Fact]
    public async Task GetCodExportRowsAsync_IncludesUncollectedRows_ExcludesRowsCollectedOutsideRange()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var now = DateTimeOffset.UtcNow;
        var uncollected = new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 400m, ShipmentTotalCharge = 50m, Status = CodCollectionStatus.CodExpected };
        var collectedInRange = new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 400m, ShipmentTotalCharge = 50m, CollectedAmount = 400m, CollectedAt = now, Status = CodCollectionStatus.CodCollected };
        var collectedOutOfRange = new CODCollection { Id = Guid.NewGuid(), ShipmentId = Guid.NewGuid(), OrganizationId = orgId, MerchantId = merchantId, BranchId = branchId, ExpectedAmount = 400m, ShipmentTotalCharge = 50m, CollectedAmount = 400m, CollectedAt = now.AddDays(-10), Status = CodCollectionStatus.CodCollected };
        dbContext.Add(uncollected);
        dbContext.Add(collectedInRange);
        dbContext.Add(collectedOutOfRange);
        await dbContext.SaveChangesAsync();

        var service = new CodReportingLookupService(dbContext);
        var rows = await service.GetCodExportRowsAsync(null, now.AddHours(-1), now.AddHours(1));

        var shipmentIds = rows.Select(r => r.ShipmentId).ToHashSet();
        Assert.Contains(uncollected.ShipmentId, shipmentIds);
        Assert.Contains(collectedInRange.ShipmentId, shipmentIds);
        Assert.DoesNotContain(collectedOutOfRange.ShipmentId, shipmentIds);
    }
}
```

- [ ] **Step 3: Run, confirm compile failure**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj`
Expected: build error.

- [ ] **Step 4: Add the interface**

```csharp
using Courier.Cod.Domain;

namespace Courier.Cod.Application;

public record CodFinancialSummary(decimal Expected, decimal Collected, decimal Pending, decimal Settled, decimal MerchantPayable);

public record CodExportRow(Guid ShipmentId, Guid MerchantId, Guid BranchId, decimal ExpectedAmount, decimal? CollectedAmount, CodCollectionStatus Status, DateTimeOffset? CollectedAt);

public interface ICodReportingLookupService
{
    Task<CodFinancialSummary> GetOrgFinancialSummaryAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);

    Task<decimal> GetBranchPendingFinanceVerificationTotalAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CodExportRow>> GetCodExportRowsAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Implement the service**

```csharp
using Courier.Cod.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Cod.Application;

public class CodReportingLookupService : ICodReportingLookupService
{
    private readonly DbContext _dbContext;

    public CodReportingLookupService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CodFinancialSummary> GetOrgFinancialSummaryAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<CODCollection>().AsQueryable();
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(c => branchIdArray.Contains(c.BranchId));
        }

        var expected = await query.Where(c => c.Status != CodCollectionStatus.Settled).SumAsync(c => c.ExpectedAmount, cancellationToken);
        var collected = await query.Where(c => c.Status != CodCollectionStatus.CodExpected).SumAsync(c => c.CollectedAmount ?? 0m, cancellationToken);

        // MerchantSettlement has no BranchId of its own (it spans every branch that delivered for
        // a merchant) — a branch-restricted caller still sees the org-wide settled/payable totals
        // here, the same "no branch concept" shape CodService.ListMerchantSettlementsAsync already
        // has for its own reads.
        var settlements = _dbContext.Set<MerchantSettlement>().AsQueryable();
        var settled = await settlements.Where(s => s.Status == MerchantSettlementStatus.Settled).SumAsync(s => s.TotalPayable, cancellationToken);
        var merchantPayable = await settlements.Where(s => s.Status == MerchantSettlementStatus.Created).SumAsync(s => s.TotalPayable, cancellationToken);

        return new CodFinancialSummary(expected, collected, collected - settled, settled, merchantPayable);
    }

    public async Task<decimal> GetBranchPendingFinanceVerificationTotalAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var statuses = new[] { CodCollectionStatus.RiderDeposited, CodCollectionStatus.BranchReconciled };
        return await _dbContext.Set<CODCollection>()
            .Where(c => c.BranchId == branchId && statuses.Contains(c.Status))
            .SumAsync(c => c.CollectedAmount ?? 0m, cancellationToken);
    }

    // Mirrors CodService.Reads.cs's GetReconciliationSummaryAsync date-filter convention exactly:
    // a null CollectedAt (not yet collected) is never excluded by the range, since an uncollected
    // COD hasn't happened yet and so can't be said to fall outside any date range.
    public async Task<IReadOnlyList<CodExportRow>> GetCodExportRowsAsync(
        IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<CODCollection>().AsQueryable();
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(c => branchIdArray.Contains(c.BranchId));
        }
        query = query
            .Where(c => c.CollectedAt == null || c.CollectedAt >= from)
            .Where(c => c.CollectedAt == null || c.CollectedAt <= to);

        var rows = await query
            .Select(c => new CodExportRow(c.ShipmentId, c.MerchantId, c.BranchId, c.ExpectedAmount, c.CollectedAmount, c.Status, c.CollectedAt))
            .ToListAsync(cancellationToken);
        return rows;
    }
}
```

- [ ] **Step 6: Run tests, confirm they pass**

Run: `dotnet test tests/Cod.Tests/Cod.Tests.csproj`
Expected: all passed.

- [ ] **Step 7: Commit**

```bash
git add tests/Cod.Tests/Cod.Tests.csproj tests/Cod.Tests/TestDbContextFactory.cs tests/Cod.Tests/CodReportingLookupServiceTests.cs src/Modules/Cod/Application/ICodReportingLookupService.cs src/Modules/Cod/Application/CodReportingLookupService.cs
git commit -m "feat(reports): add ICodReportingLookupService"
```

---

## Task 8: `OrgDashboardService` and `BranchDashboardService`

**Files:**
- Modify: `src/Modules/Reports/Application/ReportsDtos.cs` (add dashboard DTOs and `ReportsDateRange`)
- Create: `src/Modules/Reports/Application/OrgDashboardService.cs`
- Create: `src/Modules/Reports/Application/BranchDashboardService.cs`
- Create: `tests/Reports.Tests/ReportsDateRangeTests.cs`
- Create: `tests/Reports.Tests/OrgDashboardServiceTests.cs`
- Create: `tests/Reports.Tests/BranchDashboardServiceTests.cs`
- Create: `tests/Reports.Tests/Fakes.cs` (in-memory fakes of the five lookup interfaces + `IBranchAuthorizationChecker`, for pure composition/math tests — these two services' own logic is what's under test here, not the lookup implementations, which Tasks 3–7 already tested against real Postgres)

**Interfaces:**
- Consumes: all five interfaces from Tasks 3–7, `Courier.SharedKernel.IBranchAuthorizationChecker`.
- Produces (consumed by Task 9's `ReportsEndpoints`): `OrgDashboardService.GetAsync(DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken)` → `Task<OrgDashboardResponse>`; `BranchDashboardService.GetAsync(Guid branchId, ClaimsPrincipal actingUser, CancellationToken)` → `Task<BranchDashboardResponse>`.

- [ ] **Step 1: Write the failing test for `ReportsDateRange`**

```csharp
using Courier.Reports.Application;

namespace Courier.Reports.Tests;

public class ReportsDateRangeTests
{
    [Fact]
    public void Resolve_BothNull_DefaultsToTrailing30Days()
    {
        var (from, to) = ReportsDateRange.Resolve(null, null);
        Assert.True((to - from) > TimeSpan.FromDays(29) && (to - from) < TimeSpan.FromDays(31));
    }

    [Fact]
    public void Resolve_FromAfterTo_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidDateRangeException>(() => ReportsDateRange.Resolve(now, now.AddDays(-1)));
    }

    [Fact]
    public void Resolve_RangeOverOneYear_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidDateRangeException>(() => ReportsDateRange.Resolve(now.AddDays(-400), now));
    }

    [Fact]
    public void Resolve_ValidExplicitRange_ReturnsItUnchanged()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-10);
        var to = DateTimeOffset.UtcNow;
        var (resolvedFrom, resolvedTo) = ReportsDateRange.Resolve(from, to);
        Assert.Equal(from, resolvedFrom);
        Assert.Equal(to, resolvedTo);
    }
}
```

Run: `dotnet test tests/Reports.Tests/Reports.Tests.csproj` — expect a build error (`ReportsDateRange` doesn't exist).

- [ ] **Step 2: Add `ReportsDateRange` and the dashboard DTOs to `ReportsDtos.cs`**

`ReportsDtos.cs` uses a file-scoped `namespace` declaration (Task 1) — C# allows only one per file, and `using` directives must precede it — so this is a **full-file replacement**, not an append: add the four new `using`s at the top, above the existing `namespace Courier.Reports.Application;` line, and add the four new types below the two existing exception classes (which stay exactly as Task 1 left them). The complete resulting file:

```csharp
using Courier.Cod.Application;
using Courier.Manifests.Application;
using Courier.Riders.Application;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;

namespace Courier.Reports.Application;

public sealed class ReportsValidationException : Exception
{
    public ReportsValidationException(string message) : base(message)
    {
    }
}

public sealed class InvalidDateRangeException : Exception
{
    public InvalidDateRangeException(string message) : base(message)
    {
    }
}

public static class ReportsDateRange
{
    public static (DateTimeOffset From, DateTimeOffset To) Resolve(DateTimeOffset? from, DateTimeOffset? to)
    {
        var resolvedTo = to ?? DateTimeOffset.UtcNow;
        var resolvedFrom = from ?? resolvedTo.AddDays(-30);
        if (resolvedFrom > resolvedTo)
        {
            throw new InvalidDateRangeException("'from' must not be after 'to'.");
        }
        if (resolvedTo - resolvedFrom > TimeSpan.FromDays(366))
        {
            throw new InvalidDateRangeException("Date range must not exceed 1 year.");
        }
        return (resolvedFrom, resolvedTo);
    }
}

public record BranchPerformance(
    int ShipmentVolume, int DeliveryAttempts, int DeliverySuccesses, double? DeliverySuccessRate,
    int NdrCount, double? NdrRate, int RtoCount, double? RtoRate);

public record RiderPerformance(Guid RiderId, int Attempts, int Successes, double? SuccessRate);

public record OrgDashboardResponse(
    IReadOnlyDictionary<ShipmentStatus, int> TodaysShipmentsByStatus,
    CodFinancialSummary Financial,
    IReadOnlyDictionary<Guid, BranchPerformance> BranchPerformance,
    IReadOnlyList<RiderPerformance> RiderPerformance,
    double? OverallDeliverySuccessRate,
    double? OverallNdrRate,
    double? OverallRtoRate,
    TimeSpan? AverageDeliveryTime);

public record BranchDashboardResponse(
    BranchShipmentCounts ShipmentCounts,
    BranchManifestCounts ManifestCounts,
    int MissingShipmentExceptions,
    IReadOnlyList<RiderTaskLoad> Riders,
    DeliverySuccessStats TodaysDeliveries,
    decimal CodPendingFinanceVerification,
    int OpenNdrCount);
```

- [ ] **Step 3: Run, confirm `ReportsDateRangeTests` passes**

Run: `dotnet test tests/Reports.Tests/Reports.Tests.csproj --filter ReportsDateRangeTests`
Expected: 4 passed.

- [ ] **Step 4: Add `Fakes.cs`**

```csharp
using System.Security.Claims;
using Courier.Cod.Application;
using Courier.Manifests.Application;
using Courier.Ndr.Application;
using Courier.Riders.Application;
using Courier.SharedKernel;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;

namespace Courier.Reports.Tests;

internal sealed class FakeBranchAuthorizationChecker : IBranchAuthorizationChecker
{
    public IReadOnlySet<Guid>? AccessibleBranchIds { get; set; }

    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(AccessibleBranchIds);

    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(AccessibleBranchIds is null || AccessibleBranchIds.Contains(branchId));
}

internal sealed class FakeShipmentReportingLookupService : IShipmentReportingLookupService
{
    public IReadOnlyDictionary<ShipmentStatus, int> CohortStatusCounts { get; set; } = new Dictionary<ShipmentStatus, int>();
    public BranchShipmentCounts BranchShipmentCounts { get; set; } = new(0, 0, 0);
    public TimeSpan? AverageDeliveryTime { get; set; }
    public IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> ShipmentIdsByDestinationBranch { get; set; } = new Dictionary<Guid, IReadOnlyList<Guid>>();
    public IReadOnlyList<ShipmentExportRow> ExportRows { get; set; } = new List<ShipmentExportRow>();
    public IReadOnlyDictionary<Guid, string> Awbs { get; set; } = new Dictionary<Guid, string>();

    public Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCohortStatusCountsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(CohortStatusCounts);
    public Task<BranchShipmentCounts> GetBranchShipmentCountsAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(BranchShipmentCounts);
    public Task<TimeSpan?> GetAverageDeliveryTimeAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(AverageDeliveryTime);
    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetShipmentIdsByDestinationBranchAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(ShipmentIdsByDestinationBranch);
    public Task<IReadOnlyList<ShipmentExportRow>> GetShipmentExportRowsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(ExportRows);
    public Task<IReadOnlyDictionary<Guid, string>> GetAwbsByShipmentIdsAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default) => Task.FromResult(Awbs);
}

internal sealed class FakeManifestReportingLookupService : IManifestReportingLookupService
{
    public BranchManifestCounts ManifestCounts { get; set; } = new(0, 0);
    public int MissingShipmentExceptionCount { get; set; }

    public Task<BranchManifestCounts> GetBranchManifestCountsAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(ManifestCounts);
    public Task<int> GetOpenMissingShipmentExceptionCountAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(MissingShipmentExceptionCount);
}

internal sealed class FakeRiderReportingLookupService : IRiderReportingLookupService
{
    public IReadOnlyList<RiderTaskLoad> RiderTaskLoad { get; set; } = new List<RiderTaskLoad>();
    public IReadOnlyDictionary<Guid, DeliverySuccessStats> SuccessRateByBranch { get; set; } = new Dictionary<Guid, DeliverySuccessStats>();
    public IReadOnlyDictionary<Guid, DeliverySuccessStats> SuccessRateByRider { get; set; } = new Dictionary<Guid, DeliverySuccessStats>();

    public Task<IReadOnlyList<RiderTaskLoad>> GetBranchRiderTaskLoadAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(RiderTaskLoad);
    public Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>> GetSuccessRateByBranchAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(SuccessRateByBranch);
    public Task<IReadOnlyDictionary<Guid, DeliverySuccessStats>> GetSuccessRateByRiderAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(SuccessRateByRider);
}

internal sealed class FakeNdrReportingLookupService : INdrReportingLookupService
{
    public int OpenNdrCountForBranch { get; set; }
    public IReadOnlySet<Guid> ShipmentIdsWithAnyNdr { get; set; } = new HashSet<Guid>();
    public IReadOnlySet<Guid> ShipmentIdsWithRto { get; set; } = new HashSet<Guid>();

    public Task<int> GetOpenNdrCountForBranchAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(OpenNdrCountForBranch);
    public Task<IReadOnlySet<Guid>> GetShipmentIdsWithAnyNdrAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default) => Task.FromResult(ShipmentIdsWithAnyNdr);
    public Task<IReadOnlySet<Guid>> GetShipmentIdsWithRtoAsync(IReadOnlySet<Guid> shipmentIds, CancellationToken cancellationToken = default) => Task.FromResult(ShipmentIdsWithRto);
}

internal sealed class FakeCodReportingLookupService : ICodReportingLookupService
{
    public CodFinancialSummary FinancialSummary { get; set; } = new(0, 0, 0, 0, 0);
    public decimal BranchPendingFinanceVerificationTotal { get; set; }
    public IReadOnlyList<CodExportRow> ExportRows { get; set; } = new List<CodExportRow>();

    public Task<CodFinancialSummary> GetOrgFinancialSummaryAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default) => Task.FromResult(FinancialSummary);
    public Task<decimal> GetBranchPendingFinanceVerificationTotalAsync(Guid branchId, CancellationToken cancellationToken = default) => Task.FromResult(BranchPendingFinanceVerificationTotal);
    public Task<IReadOnlyList<CodExportRow>> GetCodExportRowsAsync(IReadOnlySet<Guid>? branchIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) => Task.FromResult(ExportRows);
}
```

- [ ] **Step 5: Write the failing `OrgDashboardService` tests**

```csharp
using System.Security.Claims;
using Courier.Cod.Application;
using Courier.Reports.Application;
using Courier.Riders.Application;
using Courier.Shipments.Domain;

namespace Courier.Reports.Tests;

public class OrgDashboardServiceTests
{
    private static ClaimsPrincipal AnyUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    [Fact]
    public async Task GetAsync_ComputesOverallRatesFromSummedNumeratorsAndDenominators_NotAveragedPerBranchRates()
    {
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var shipmentsA = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };   // 2 shipments, branch A
        var shipmentsB = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToList(); // 8 shipments, branch B
        var shipmentIdsByBranch = new Dictionary<Guid, IReadOnlyList<Guid>>
        {
            [branchA] = shipmentsA,
            [branchB] = shipmentsB
        };

        // Branch A: 1/2 NDR (50%). Branch B: 1/8 NDR (12.5%). A naive average would give 31.25%;
        // the correct pooled rate is 2/10 = 20%.
        var ndrShipmentIds = new HashSet<Guid> { shipmentsA[0], shipmentsB[0] };

        var shipmentLookup = new FakeShipmentReportingLookupService
        {
            CohortStatusCounts = new Dictionary<ShipmentStatus, int> { [ShipmentStatus.Created] = 3 },
            ShipmentIdsByDestinationBranch = shipmentIdsByBranch,
            AverageDeliveryTime = TimeSpan.FromHours(4)
        };
        var ndrLookup = new FakeNdrReportingLookupService { ShipmentIdsWithAnyNdr = ndrShipmentIds, ShipmentIdsWithRto = new HashSet<Guid>() };
        var riderLookup = new FakeRiderReportingLookupService
        {
            SuccessRateByBranch = new Dictionary<Guid, DeliverySuccessStats>
            {
                [branchA] = new DeliverySuccessStats(2, 1),
                [branchB] = new DeliverySuccessStats(8, 8)
            },
            SuccessRateByRider = new Dictionary<Guid, DeliverySuccessStats>()
        };
        var codLookup = new FakeCodReportingLookupService { FinancialSummary = new CodFinancialSummary(1000m, 800m, 200m, 600m, 100m) };
        var branchChecker = new FakeBranchAuthorizationChecker { AccessibleBranchIds = null };

        var service = new OrgDashboardService(shipmentLookup, codLookup, riderLookup, ndrLookup, branchChecker);
        var result = await service.GetAsync(null, null, AnyUser());

        Assert.Equal(0.2, result.OverallNdrRate);
        Assert.Equal(9.0 / 10.0, result.OverallDeliverySuccessRate);
        Assert.Equal(TimeSpan.FromHours(4), result.AverageDeliveryTime);
        Assert.Equal(1000m, result.Financial.Expected);
    }

    [Fact]
    public async Task GetAsync_BranchWithNoShipments_RateIsNullNotDivisionByZero()
    {
        var branchWithNoVolume = Guid.NewGuid();
        var shipmentLookup = new FakeShipmentReportingLookupService
        {
            ShipmentIdsByDestinationBranch = new Dictionary<Guid, IReadOnlyList<Guid>> { [branchWithNoVolume] = new List<Guid>() }
        };
        var service = new OrgDashboardService(
            shipmentLookup, new FakeCodReportingLookupService(), new FakeRiderReportingLookupService(),
            new FakeNdrReportingLookupService(), new FakeBranchAuthorizationChecker { AccessibleBranchIds = null });

        var result = await service.GetAsync(null, null, AnyUser());

        Assert.Null(result.BranchPerformance[branchWithNoVolume].NdrRate);
        Assert.Null(result.BranchPerformance[branchWithNoVolume].RtoRate);
    }
}
```

- [ ] **Step 6: Run, confirm compile failure**

Run: `dotnet test tests/Reports.Tests/Reports.Tests.csproj`
Expected: build error — `OrgDashboardService` doesn't exist.

- [ ] **Step 7: Implement `OrgDashboardService`**

```csharp
using System.Security.Claims;
using Courier.Cod.Application;
using Courier.Ndr.Application;
using Courier.Riders.Application;
using Courier.SharedKernel;
using Courier.Shipments.Application;

namespace Courier.Reports.Application;

public class OrgDashboardService
{
    private readonly IShipmentReportingLookupService _shipmentLookup;
    private readonly ICodReportingLookupService _codLookup;
    private readonly IRiderReportingLookupService _riderLookup;
    private readonly INdrReportingLookupService _ndrLookup;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public OrgDashboardService(
        IShipmentReportingLookupService shipmentLookup,
        ICodReportingLookupService codLookup,
        IRiderReportingLookupService riderLookup,
        INdrReportingLookupService ndrLookup,
        IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _shipmentLookup = shipmentLookup;
        _codLookup = codLookup;
        _riderLookup = riderLookup;
        _ndrLookup = ndrLookup;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<OrgDashboardResponse> GetAsync(DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var (rangeFrom, rangeTo) = ReportsDateRange.Resolve(from, to);
        var branchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "report.read", cancellationToken);

        var todayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var todayEnd = todayStart.AddDays(1);

        var todaysShipmentsTask = _shipmentLookup.GetCohortStatusCountsAsync(branchIds, todayStart, todayEnd, cancellationToken);
        var financialTask = _codLookup.GetOrgFinancialSummaryAsync(branchIds, cancellationToken);
        var successByBranchTask = _riderLookup.GetSuccessRateByBranchAsync(branchIds, rangeFrom, rangeTo, cancellationToken);
        var successByRiderTask = _riderLookup.GetSuccessRateByRiderAsync(branchIds, rangeFrom, rangeTo, cancellationToken);
        var avgDeliveryTimeTask = _shipmentLookup.GetAverageDeliveryTimeAsync(branchIds, rangeFrom, rangeTo, cancellationToken);
        var shipmentIdsByBranchTask = _shipmentLookup.GetShipmentIdsByDestinationBranchAsync(branchIds, rangeFrom, rangeTo, cancellationToken);

        await Task.WhenAll(todaysShipmentsTask, financialTask, successByBranchTask, successByRiderTask, avgDeliveryTimeTask, shipmentIdsByBranchTask);

        var shipmentIdsByBranch = shipmentIdsByBranchTask.Result;
        var allShipmentIds = shipmentIdsByBranch.Values.SelectMany(ids => ids).ToHashSet();
        var ndrShipmentIds = await _ndrLookup.GetShipmentIdsWithAnyNdrAsync(allShipmentIds, cancellationToken);
        var rtoShipmentIds = await _ndrLookup.GetShipmentIdsWithRtoAsync(allShipmentIds, cancellationToken);

        var successByBranch = successByBranchTask.Result;
        var branchPerformance = new Dictionary<Guid, BranchPerformance>();
        foreach (var (branchId, shipmentIds) in shipmentIdsByBranch)
        {
            var volume = shipmentIds.Count;
            var ndrCount = shipmentIds.Count(id => ndrShipmentIds.Contains(id));
            var rtoCount = shipmentIds.Count(id => rtoShipmentIds.Contains(id));
            successByBranch.TryGetValue(branchId, out var successStats);
            var attempts = successStats?.Attempts ?? 0;
            var successes = successStats?.Successes ?? 0;

            branchPerformance[branchId] = new BranchPerformance(
                volume, attempts, successes,
                attempts > 0 ? (double)successes / attempts : null,
                ndrCount, volume > 0 ? (double)ndrCount / volume : null,
                rtoCount, volume > 0 ? (double)rtoCount / volume : null);
        }

        var riderPerformance = successByRiderTask.Result
            .Select(kv => new RiderPerformance(kv.Key, kv.Value.Attempts, kv.Value.Successes, kv.Value.Attempts > 0 ? (double)kv.Value.Successes / kv.Value.Attempts : null))
            .ToList();

        var totalAttempts = successByBranch.Values.Sum(s => s.Attempts);
        var totalSuccesses = successByBranch.Values.Sum(s => s.Successes);
        var totalShipments = allShipmentIds.Count;
        var totalNdr = ndrShipmentIds.Count(id => allShipmentIds.Contains(id));
        var totalRto = rtoShipmentIds.Count(id => allShipmentIds.Contains(id));

        return new OrgDashboardResponse(
            todaysShipmentsTask.Result,
            financialTask.Result,
            branchPerformance,
            riderPerformance,
            totalAttempts > 0 ? (double)totalSuccesses / totalAttempts : null,
            totalShipments > 0 ? (double)totalNdr / totalShipments : null,
            totalShipments > 0 ? (double)totalRto / totalShipments : null,
            avgDeliveryTimeTask.Result);
    }
}
```

- [ ] **Step 8: Run, confirm `OrgDashboardServiceTests` passes**

Run: `dotnet test tests/Reports.Tests/Reports.Tests.csproj --filter OrgDashboardServiceTests`
Expected: 2 passed.

- [ ] **Step 9: Write the failing `BranchDashboardService` tests**

```csharp
using System.Security.Claims;
using Courier.Manifests.Application;
using Courier.Reports.Application;
using Courier.Riders.Application;

namespace Courier.Reports.Tests;

public class BranchDashboardServiceTests
{
    private static ClaimsPrincipal AnyUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    [Fact]
    public async Task GetAsync_AccessibleBranch_ReturnsComposedResponse()
    {
        var branchId = Guid.NewGuid();
        var shipmentLookup = new FakeShipmentReportingLookupService { BranchShipmentCounts = new Courier.Shipments.Application.BranchShipmentCounts(5, 2, 3) };
        var manifestLookup = new FakeManifestReportingLookupService { ManifestCounts = new BranchManifestCounts(1, 2), MissingShipmentExceptionCount = 1 };
        var riderLookup = new FakeRiderReportingLookupService
        {
            RiderTaskLoad = new List<RiderTaskLoad> { new(Guid.NewGuid(), 3) },
            SuccessRateByBranch = new Dictionary<Guid, DeliverySuccessStats> { [branchId] = new(4, 3) }
        };
        var ndrLookup = new FakeNdrReportingLookupService { OpenNdrCountForBranch = 2 };
        var codLookup = new FakeCodReportingLookupService { BranchPendingFinanceVerificationTotal = 750m };
        var branchChecker = new FakeBranchAuthorizationChecker { AccessibleBranchIds = new HashSet<Guid> { branchId } };

        var service = new BranchDashboardService(shipmentLookup, manifestLookup, riderLookup, ndrLookup, codLookup, branchChecker);
        var result = await service.GetAsync(branchId, AnyUser());

        Assert.Equal(5, result.ShipmentCounts.Outgoing);
        Assert.Equal(1, result.MissingShipmentExceptions);
        Assert.Single(result.Riders);
        Assert.Equal(4, result.TodaysDeliveries.Attempts);
        Assert.Equal(750m, result.CodPendingFinanceVerification);
        Assert.Equal(2, result.OpenNdrCount);
    }

    [Fact]
    public async Task GetAsync_InaccessibleBranch_ThrowsReportsValidationException()
    {
        var branchId = Guid.NewGuid();
        var branchChecker = new FakeBranchAuthorizationChecker { AccessibleBranchIds = new HashSet<Guid> { Guid.NewGuid() } };
        var service = new BranchDashboardService(
            new FakeShipmentReportingLookupService(), new FakeManifestReportingLookupService(), new FakeRiderReportingLookupService(),
            new FakeNdrReportingLookupService(), new FakeCodReportingLookupService(), branchChecker);

        await Assert.ThrowsAsync<ReportsValidationException>(() => service.GetAsync(branchId, AnyUser()));
    }
}
```

- [ ] **Step 10: Run, confirm compile failure**

Run: `dotnet test tests/Reports.Tests/Reports.Tests.csproj`
Expected: build error — `BranchDashboardService` doesn't exist.

- [ ] **Step 11: Implement `BranchDashboardService`**

```csharp
using System.Security.Claims;
using Courier.Cod.Application;
using Courier.Manifests.Application;
using Courier.Ndr.Application;
using Courier.Riders.Application;
using Courier.SharedKernel;
using Courier.Shipments.Application;

namespace Courier.Reports.Application;

public class BranchDashboardService
{
    private readonly IShipmentReportingLookupService _shipmentLookup;
    private readonly IManifestReportingLookupService _manifestLookup;
    private readonly IRiderReportingLookupService _riderLookup;
    private readonly INdrReportingLookupService _ndrLookup;
    private readonly ICodReportingLookupService _codLookup;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public BranchDashboardService(
        IShipmentReportingLookupService shipmentLookup,
        IManifestReportingLookupService manifestLookup,
        IRiderReportingLookupService riderLookup,
        INdrReportingLookupService ndrLookup,
        ICodReportingLookupService codLookup,
        IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _shipmentLookup = shipmentLookup;
        _manifestLookup = manifestLookup;
        _riderLookup = riderLookup;
        _ndrLookup = ndrLookup;
        _codLookup = codLookup;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<BranchDashboardResponse> GetAsync(Guid branchId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        // Matches ManifestService/NdrService/RiderService/CodService's established
        // CheckBranchAccessAsync convention exactly: 400 REPORTS_VALIDATION_FAILED, not 403.
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "report.read", cancellationToken);
        if (accessibleBranchIds is not null && !accessibleBranchIds.Contains(branchId))
        {
            throw new ReportsValidationException("Branch is outside your branch access for permission 'report.read'.");
        }

        var todayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var todayEnd = todayStart.AddDays(1);
        var singleBranch = new HashSet<Guid> { branchId };

        var shipmentCountsTask = _shipmentLookup.GetBranchShipmentCountsAsync(branchId, cancellationToken);
        var manifestCountsTask = _manifestLookup.GetBranchManifestCountsAsync(branchId, cancellationToken);
        var missingExceptionsTask = _manifestLookup.GetOpenMissingShipmentExceptionCountAsync(branchId, cancellationToken);
        var riderLoadTask = _riderLookup.GetBranchRiderTaskLoadAsync(branchId, cancellationToken);
        var deliveriesTask = _riderLookup.GetSuccessRateByBranchAsync(singleBranch, todayStart, todayEnd, cancellationToken);
        var codPendingTask = _codLookup.GetBranchPendingFinanceVerificationTotalAsync(branchId, cancellationToken);
        var openNdrTask = _ndrLookup.GetOpenNdrCountForBranchAsync(branchId, cancellationToken);

        await Task.WhenAll(shipmentCountsTask, manifestCountsTask, missingExceptionsTask, riderLoadTask, deliveriesTask, codPendingTask, openNdrTask);

        deliveriesTask.Result.TryGetValue(branchId, out var todaysDeliveries);

        return new BranchDashboardResponse(
            shipmentCountsTask.Result,
            manifestCountsTask.Result,
            missingExceptionsTask.Result,
            riderLoadTask.Result,
            todaysDeliveries ?? new DeliverySuccessStats(0, 0),
            codPendingTask.Result,
            openNdrTask.Result);
    }
}
```

- [ ] **Step 12: Run all Reports.Tests, confirm everything passes**

Run: `dotnet test tests/Reports.Tests/Reports.Tests.csproj`
Expected: all passed.

- [ ] **Step 13: Commit**

```bash
git add src/Modules/Reports/Application/ReportsDtos.cs src/Modules/Reports/Application/OrgDashboardService.cs src/Modules/Reports/Application/BranchDashboardService.cs tests/Reports.Tests/ReportsDateRangeTests.cs tests/Reports.Tests/Fakes.cs tests/Reports.Tests/OrgDashboardServiceTests.cs tests/Reports.Tests/BranchDashboardServiceTests.cs
git commit -m "feat(reports): add OrgDashboardService and BranchDashboardService"
```

---

## Task 9: CSV writer, `ReportExportService`, dashboard/export endpoints, and app wiring

**Files:**
- Create: `src/Modules/Reports/Infrastructure/CsvWriter.cs`
- Create: `src/Modules/Reports/Application/ReportExportService.cs`
- Create: `src/Modules/Reports/Presentation/ReportsEndpoints.cs`
- Create: `tests/Reports.Tests/CsvWriterTests.cs`
- Modify: `src/Api/Api.csproj` (add `Reports.csproj` reference)
- Modify: `src/Api/Program.cs` (register the 5 lookup services + 3 Reports application services, map the new endpoints)
- Create: `tests/Api.Tests/ReportsEndpointsTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 3–8, plus Task 2's `GetNamesByIdsAsync`/`GetDisplayNamesByIdsAsync`.
- Produces: `Reports.Presentation.ReportsEndpoints.MapReportsEndpoints(this IEndpointRouteBuilder app)`, mapped from `Program.cs`.

- [ ] **Step 1: Write the failing `CsvWriter` test**

```csharp
using Courier.Reports.Infrastructure;

namespace Courier.Reports.Tests;

public class CsvWriterTests
{
    [Fact]
    public void Write_EscapesCommaQuoteAndNewline()
    {
        var csv = CsvWriter.Write(
            new[] { "Name", "Note" },
            new[] { (IReadOnlyList<string?>)new[] { "Acme, Inc.", "Said \"hello\"\nagain" } });

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Name,Note", lines[0]);
        Assert.Equal("\"Acme, Inc.\",\"Said \"\"hello\"\"\nagain\"", lines[1]);
    }

    [Fact]
    public void Write_NullField_RendersAsEmptyString()
    {
        var csv = CsvWriter.Write(new[] { "A" }, new[] { (IReadOnlyList<string?>)new string?[] { null } });
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("", lines[1]);
    }

    [Fact]
    public void Write_PlainFields_NoQuoting()
    {
        var csv = CsvWriter.Write(new[] { "Awb" }, new[] { (IReadOnlyList<string?>)new[] { "AWB123456" } });
        Assert.Contains("AWB123456", csv);
        Assert.DoesNotContain("\"AWB123456\"", csv);
    }
}
```

Run: `dotnet test tests/Reports.Tests/Reports.Tests.csproj --filter CsvWriterTests`
Expected: build error (`CsvWriter` doesn't exist).

- [ ] **Step 2: Implement `CsvWriter`**

```csharp
using System.Text;

namespace Courier.Reports.Infrastructure;

public static class CsvWriter
{
    public static string Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', headers.Select(EscapeField))).Append("\r\n");
        foreach (var row in rows)
        {
            sb.Append(string.Join(',', row.Select(EscapeField))).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string EscapeField(string? field)
    {
        field ??= string.Empty;
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
        return field;
    }
}
```

- [ ] **Step 3: Run, confirm `CsvWriterTests` passes**

Run: `dotnet test tests/Reports.Tests/Reports.Tests.csproj --filter CsvWriterTests`
Expected: 3 passed.

- [ ] **Step 4: Implement `ReportExportService`** (no dedicated unit test — its two methods are thin orchestration over already-tested lookups/`CsvWriter`; proven end-to-end by Step 10's API tests, matching how this codebase treats thin composition layers elsewhere, e.g. `NdrEndpoints` itself has no dedicated unit test file)

```csharp
using System.Globalization;
using System.Security.Claims;
using Courier.Merchants.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Reports.Infrastructure;
using Courier.SharedKernel;
using Courier.Shipments.Application;

namespace Courier.Reports.Application;

public class ReportExportService
{
    private readonly IShipmentReportingLookupService _shipmentLookup;
    private readonly ICodReportingLookupService _codLookup;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IMerchantLookupService _merchantLookupService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public ReportExportService(
        IShipmentReportingLookupService shipmentLookup,
        ICodReportingLookupService codLookup,
        IBranchLookupService branchLookupService,
        IMerchantLookupService merchantLookupService,
        IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _shipmentLookup = shipmentLookup;
        _codLookup = codLookup;
        _branchLookupService = branchLookupService;
        _merchantLookupService = merchantLookupService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<string> GetShipmentsCsvAsync(Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var (rangeFrom, rangeTo) = ReportsDateRange.Resolve(from, to);
        var branchIds = await ResolveBranchScopeAsync(branchId, actingUser, cancellationToken);

        var rows = await _shipmentLookup.GetShipmentExportRowsAsync(branchIds, rangeFrom, rangeTo, cancellationToken);

        var branchNameIds = rows.SelectMany(r => new[] { r.OriginBranchId, r.DestinationBranchId }).ToHashSet();
        var merchantIds = rows.Select(r => r.MerchantId).ToHashSet();
        var branchNames = await _branchLookupService.GetNamesByIdsAsync(branchNameIds, cancellationToken);
        var merchantNames = await _merchantLookupService.GetDisplayNamesByIdsAsync(merchantIds, cancellationToken);

        var headers = new[] { "Awb", "Status", "OriginBranch", "DestinationBranch", "MerchantName", "CodAmount", "TotalCharge", "CreatedAt", "DeliveredAt" };
        var csvRows = rows.Select(r => (IReadOnlyList<string?>)new[]
        {
            r.Awb,
            r.Status.ToString(),
            branchNames.GetValueOrDefault(r.OriginBranchId, string.Empty),
            branchNames.GetValueOrDefault(r.DestinationBranchId, string.Empty),
            merchantNames.GetValueOrDefault(r.MerchantId, string.Empty),
            r.CodAmount.ToString(CultureInfo.InvariantCulture),
            r.TotalCharge.ToString(CultureInfo.InvariantCulture),
            r.CreatedAt.ToString("O"),
            r.DeliveredAt?.ToString("O")
        });

        return CsvWriter.Write(headers, csvRows);
    }

    public async Task<string> GetCodSummaryCsvAsync(Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var (rangeFrom, rangeTo) = ReportsDateRange.Resolve(from, to);
        var branchIds = await ResolveBranchScopeAsync(branchId, actingUser, cancellationToken);

        var rows = await _codLookup.GetCodExportRowsAsync(branchIds, rangeFrom, rangeTo, cancellationToken);

        var shipmentIds = rows.Select(r => r.ShipmentId).ToHashSet();
        var branchIdsForNames = rows.Select(r => r.BranchId).ToHashSet();
        var merchantIds = rows.Select(r => r.MerchantId).ToHashSet();
        var awbs = await _shipmentLookup.GetAwbsByShipmentIdsAsync(shipmentIds, cancellationToken);
        var branchNames = await _branchLookupService.GetNamesByIdsAsync(branchIdsForNames, cancellationToken);
        var merchantNames = await _merchantLookupService.GetDisplayNamesByIdsAsync(merchantIds, cancellationToken);

        var headers = new[] { "Awb", "MerchantName", "BranchName", "ExpectedAmount", "CollectedAmount", "Status", "CollectedAt" };
        var csvRows = rows.Select(r => (IReadOnlyList<string?>)new[]
        {
            awbs.GetValueOrDefault(r.ShipmentId, string.Empty),
            merchantNames.GetValueOrDefault(r.MerchantId, string.Empty),
            branchNames.GetValueOrDefault(r.BranchId, string.Empty),
            r.ExpectedAmount.ToString(CultureInfo.InvariantCulture),
            r.CollectedAmount?.ToString(CultureInfo.InvariantCulture),
            r.Status.ToString(),
            r.CollectedAt?.ToString("O")
        });

        return CsvWriter.Write(headers, csvRows);
    }

    private async Task<IReadOnlySet<Guid>?> ResolveBranchScopeAsync(Guid? requestedBranchId, ClaimsPrincipal actingUser, CancellationToken cancellationToken)
    {
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "report.export", cancellationToken);
        if (requestedBranchId is not Guid branchId)
        {
            return accessible;
        }
        if (accessible is not null && !accessible.Contains(branchId))
        {
            throw new ReportsValidationException("Branch is outside your branch access for permission 'report.export'.");
        }
        return new HashSet<Guid> { branchId };
    }
}
```

- [ ] **Step 5: Implement `ReportsEndpoints`**

```csharp
using Courier.Reports.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Reports.Presentation;

public static class ReportsEndpoints
{
    public static void MapReportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reports")
            .WithTags("Reports")
            .RequireAuthorization();

        group.MapGet("/dashboard/organization", async (DateTimeOffset? from, DateTimeOffset? to, OrgDashboardService service, HttpContext httpContext) =>
            await HandleAsync(() => service.GetAsync(from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.read");

        group.MapGet("/dashboard/branch/{branchId:guid}", async (Guid branchId, BranchDashboardService service, HttpContext httpContext) =>
            await HandleAsync(() => service.GetAsync(branchId, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.read");

        group.MapGet("/export/shipments", async (Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ReportExportService service, HttpContext httpContext) =>
            await HandleCsvAsync(() => service.GetShipmentsCsvAsync(branchId, from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.export");

        group.MapGet("/export/cod-summary", async (Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ReportExportService service, HttpContext httpContext) =>
            await HandleCsvAsync(() => service.GetCodSummaryCsvAsync(branchId, from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.export");
    }

    private static async Task<IResult> HandleAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
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
}
```

- [ ] **Step 6: Add the `Reports.csproj` reference to `Api.csproj`**

Insert `<ProjectReference Include="..\Modules\Reports\Reports.csproj" />` alongside the other module references (after `Pricing`, before `Shipments`, matching `Courier.slnx`'s ordering from Task 1).

- [ ] **Step 7: Wire DI registrations and endpoint mapping into `Program.cs`**

Add `using` statements near the top (alongside the existing `Courier.Cod.*`/`Courier.Ndr.*` usings):

```csharp
using Courier.Reports.Application;
using Courier.Reports.Presentation;
```

Add service registrations after the existing `builder.Services.AddScoped<NdrService>();` line:

```csharp
builder.Services.AddScoped<IShipmentReportingLookupService, ShipmentReportingLookupService>();
builder.Services.AddScoped<IManifestReportingLookupService, ManifestReportingLookupService>();
builder.Services.AddScoped<IRiderReportingLookupService, RiderReportingLookupService>();
builder.Services.AddScoped<INdrReportingLookupService, NdrReportingLookupService>();
builder.Services.AddScoped<ICodReportingLookupService, CodReportingLookupService>();
builder.Services.AddScoped<OrgDashboardService>();
builder.Services.AddScoped<BranchDashboardService>();
builder.Services.AddScoped<ReportExportService>();
```

(`ShipmentReportingLookupService`/`ManifestReportingLookupService`/`RiderReportingLookupService`/`NdrReportingLookupService`/`CodReportingLookupService` are already visible via each module's existing `Courier.*.Application` using directive already present in `Program.cs`.)

Add the endpoint mapping after `app.MapCodEndpoints();`:

```csharp
app.MapReportsEndpoints();
```

- [ ] **Step 8: Build the whole solution**

Run: `dotnet build Courier.slnx`
Expected: succeeds.

- [ ] **Step 9: Write the failing `Api.Tests` for the four endpoints**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Courier.Api.Tests;

public class ReportsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid ProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");

    public ReportsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, Guid BranchId)> RegisterOrgWithBranchAsync(string label)
    {
        var client = _factory.CreateClient();
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12];
        var registerRequest = new RegisterOrganizationRequest(
            $"Reports Org {suffix}", $"reports-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var auth = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var branchRequest = new
        {
            Code = "HUB1", Name = "Hub One", Type = 1 /* Hub */, ProvinceId, DistrictId, LocalLevelId,
            WardNumber = 1, AddressLine = "Test Address", Phone = (string?)null, OperatingHours = (string?)null,
            Latitude = (decimal?)null, Longitude = (decimal?)null, ParentBranchId = (Guid?)null
        };
        var branchResponse = await client.PostAsJsonAsync("/api/v1/branches", branchRequest);
        branchResponse.EnsureSuccessStatusCode();
        var branch = await branchResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var branchId = branch.GetProperty("id").GetGuid();

        return (client, branchId);
    }

    [Fact]
    public async Task OrgDashboard_OwnerCaller_ReturnsOk()
    {
        var (client, _) = await RegisterOrgWithBranchAsync("org-dash");

        var response = await client.GetAsync("/api/v1/reports/dashboard/organization");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BranchDashboard_AccessibleBranch_ReturnsOk()
    {
        var (client, branchId) = await RegisterOrgWithBranchAsync("branch-dash");

        var response = await client.GetAsync($"/api/v1/reports/dashboard/branch/{branchId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BranchDashboard_BranchFromAnotherOrganization_ReturnsReportsValidationFailed()
    {
        var (_, otherOrgBranchId) = await RegisterOrgWithBranchAsync("branch-dash-other");
        var (client, _) = await RegisterOrgWithBranchAsync("branch-dash-caller");

        var response = await client.GetAsync($"/api/v1/reports/dashboard/branch/{otherOrgBranchId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("REPORTS_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExportShipments_ReturnsCsvWithHeaderRow()
    {
        var (client, _) = await RegisterOrgWithBranchAsync("export-shipments");

        var response = await client.GetAsync("/api/v1/reports/export/shipments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Awb,Status,OriginBranch,DestinationBranch,MerchantName,CodAmount,TotalCharge,CreatedAt,DeliveredAt", body);
    }

    [Fact]
    public async Task ExportCodSummary_ReturnsCsvWithHeaderRow()
    {
        var (client, _) = await RegisterOrgWithBranchAsync("export-cod");

        var response = await client.GetAsync("/api/v1/reports/export/cod-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Awb,MerchantName,BranchName,ExpectedAmount,CollectedAmount,Status,CollectedAt", body);
    }

    [Fact]
    public async Task Reports_UnauthenticatedCaller_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/reports/dashboard/organization");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 10: Run, confirm they fail** (endpoints not mapped yet if any earlier step was skipped) then **pass** once Steps 6–7 are in place

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter ReportsEndpointsTests`
Expected: all passed. Requires `courier-postgres-1`, `courier-redis-1`, `courier-rabbitmq-1` all running (`docker start courier-postgres-1 courier-redis-1 courier-rabbitmq-1` if any are stopped) — `Api.Tests`' `HealthReadyTests`-adjacent startup path needs all three.

- [ ] **Step 11: Run the full test suite**

Run: `dotnet test Courier.slnx`
Expected: all passed, no regressions in any pre-existing test project.

- [ ] **Step 12: Commit**

```bash
git add src/Modules/Reports/Infrastructure/CsvWriter.cs src/Modules/Reports/Application/ReportExportService.cs src/Modules/Reports/Presentation/ReportsEndpoints.cs tests/Reports.Tests/CsvWriterTests.cs src/Api/Api.csproj src/Api/Program.cs tests/Api.Tests/ReportsEndpointsTests.cs
git commit -m "feat(reports): add CSV export, wire Reports endpoints and DI into the app"
```

---

## Task 10: Exit-criteria end-to-end proof and docs sync

**Files:**
- Create: `tests/Api.Tests/ReportsExitCriteriaEndToEndTests.cs`
- Modify: `docs/ROADMAP.md` (Phase 13 status)

**Interfaces:** None new — this task only adds one proof test and updates docs.

- [ ] **Step 1: Write the end-to-end exit-criteria test**

Proves ROADMAP.md's literal exit criterion — "dashboards reflect real data from all prior phases with correct branch/role scoping" — by driving one real shipment through the same staff-JWT lifecycle sequence `tests/Api.Tests/MerchantApiExitCriteriaEndToEndTests.cs` (Phase 12) already proved end-to-end (branch/zone/rate-card/rider setup → create → pickup → hub transfer via manifest → delivery), reusing that file's exact route names/request DTOs, then asserting the org dashboard's `todaysShipmentsByStatus.Delivered` count reflects it and the branch dashboard's shipment/manifest counts are real (not zeroed) for the branch that handled it:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Manifests.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Courier.Api.Tests;

// Mirrors MerchantApiExitCriteriaEndToEndTests.cs's setup/lifecycle helpers exactly (Phase 12) —
// same branch/zone/rate-card/rider seeding, same pickup->manifest->delivery route sequence, staff
// JWT throughout (no merchant/API-key indirection needed here, since Reports itself is staff-only).
public class ReportsExitCriteriaEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public ReportsExitCriteriaEndToEndTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static CreateBranchRequest ValidBranchRequest(string code, Guid? deliveryZoneId = null) => new(
        code, $"Branch {code}", BranchType.Hub, KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null, deliveryZoneId);

    private static List<Guid> KathmanduWard1Areas() => new()
    {
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
    };

    private async Task<(Guid OriginBranchId, Guid DestinationBranchId, Guid ServiceTypeId)> SeedRoutableBranchesAsync(HttpClient client)
    {
        var originZone = await (await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Origin-{Guid.NewGuid():N}", KathmanduWard1Areas()))).Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        var destZone = await (await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Dest-{Guid.NewGuid():N}", KathmanduWard1Areas()))).Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        var originBranch = await (await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"OZ-{Guid.NewGuid():N}"[..10], originZone!.Id))).Content.ReadFromJsonAsync<BranchResponse>();
        var destBranch = await (await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"DZ-{Guid.NewGuid():N}"[..10], destZone!.Id))).Content.ReadFromJsonAsync<BranchResponse>();
        var serviceTypes = await (await client.GetAsync("/api/v1/service-types")).Content.ReadFromJsonAsync<List<ServiceTypeResponse>>();
        var standard = serviceTypes!.Single(s => s.Code == ServiceTypeCode.Standard);
        return (originBranch!.Id, destBranch!.Id, standard.Id);
    }

    private async Task SeedRateCardAsync(HttpClient client, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, decimal baseRate)
    {
        var originBranch = (await (await client.GetAsync($"/api/v1/branches/{originBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var destBranch = (await (await client.GetAsync($"/api/v1/branches/{destinationBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var weightSlabs = await (await client.GetAsync("/api/v1/weight-slabs")).Content.ReadFromJsonAsync<List<WeightSlabResponse>>();
        var slab = weightSlabs!.Single(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m);
        var rateCard = await (await client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest($"Card-{Guid.NewGuid():N}", DateTimeOffset.UtcNow))).Content.ReadFromJsonAsync<RateCardResponse>();
        await client.PostAsJsonAsync($"/api/v1/rate-cards/{rateCard!.Id}/zone-rates",
            new CreateZoneRateRequest(originBranch.DeliveryZoneId!.Value, destBranch.DeliveryZoneId!.Value, serviceTypeId, slab.Id, baseRate));
    }

    private async Task<Guid> CreateRiderAsync(HttpClient client, Guid branchId, RiderRoleType roleType)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var rider = await (await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, $"LIC-{suffix[..10]}", branchId, roleType)))
            .Content.ReadFromJsonAsync<RiderResponse>();
        return rider!.Id;
    }

    [Fact]
    public async Task OrgAndBranchDashboards_ReflectARealDeliveredShipment_NotHardcodedData()
    {
        var client = _factory.CreateClient();
        var suffix = $"exit-{Guid.NewGuid():N}";
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterOrganizationRequest(
            $"Reports Exit Org {suffix}", $"reports-exit-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person"));
        registerResponse.EnsureSuccessStatusCode();
        var auth = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        const decimal codAmount = 750m;
        var createShipmentRequest = new CreateShipmentRequest(
            null, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, codAmount, false,
            "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
            "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
            new List<CreateShipmentItemRequest> { new("Item A", 1, 500m) });
        var created = await (await client.PostAsJsonAsync("/api/v1/shipments", createShipmentRequest)).Content.ReadFromJsonAsync<ShipmentResponse>();

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, created!.Id, DeliveryTaskType.Pickup));
        await client.PostAsJsonAsync($"/api/v1/riders/{pickupRiderId}/scan/pickup", new RiderScanRequest(created.Awb));
        await client.PostAsJsonAsync($"/api/v1/shipments/{created.Id}/receive-origin", new { });

        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(created.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(created.Awb));

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, created.Id, DeliveryTaskType.Delivery));
        var completeResponse = await client.PostAsJsonAsync($"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete",
            new CompleteDeliveryRequest(created.Id, "Recipient One", true, null, null, null, null, codAmount));
        completeResponse.EnsureSuccessStatusCode();
        var delivered = await (await client.GetAsync($"/api/v1/shipments/{created.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Delivered, delivered!.CurrentStatus);

        var orgDashboardResponse = await client.GetAsync("/api/v1/reports/dashboard/organization");
        Assert.Equal(HttpStatusCode.OK, orgDashboardResponse.StatusCode);
        var orgDashboard = await orgDashboardResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(orgDashboard.GetProperty("todaysShipmentsByStatus").TryGetProperty("Delivered", out var deliveredCount));
        Assert.True(deliveredCount.GetInt32() >= 1);
        Assert.True(orgDashboard.GetProperty("financial").GetProperty("expected").GetDecimal() >= codAmount);

        var branchDashboardResponse = await client.GetAsync($"/api/v1/reports/dashboard/branch/{destinationBranchId}");
        Assert.Equal(HttpStatusCode.OK, branchDashboardResponse.StatusCode);
        var branchDashboard = await branchDashboardResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(branchDashboard.GetProperty("manifestCounts").GetProperty("recentlyClosed").GetInt32() >= 0);
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter ReportsExitCriteriaEndToEndTests`
Expected: passed.

- [ ] **Step 3: Update `docs/ROADMAP.md`'s Phase 13 entry**

Replace:

```
## Phase 13 — Reports, Analytics, Advanced Dashboards

Org dashboard (CLAUDE.md §29) and branch dashboard (`BRANCH-OPERATIONS.md` §3) fully
built out, `report.export`.
*Exit:* dashboards reflect real data from all prior phases with correct branch/role
scoping, no hardcoded/demo data paths left in.
```

with:

```
## Phase 13 — Reports, Analytics, Advanced Dashboards

Org dashboard (CLAUDE.md §29) and branch dashboard (`BRANCH-OPERATIONS.md` §3) fully
built out, `report.export`.
*Exit:* dashboards reflect real data from all prior phases with correct branch/role
scoping, no hardcoded/demo data paths left in.

*Status: Complete — backend slice only.* New `Reports` module (no owned entities):
`GET /api/v1/reports/dashboard/organization`, `GET /api/v1/reports/dashboard/branch/{branchId}`,
`GET /api/v1/reports/export/shipments`, `GET /api/v1/reports/export/cod-summary`, gated by
`report.read`/`report.export`. Five new cross-module lookup interfaces
(`IShipmentReportingLookupService`, `IManifestReportingLookupService`,
`IRiderReportingLookupService`, `INdrReportingLookupService`, `ICodReportingLookupService`), each
implemented in its owning module's `Application` layer, following the `IBranchLookupService`
pattern Phase 5 established. All figures computed live (no caching/materialization). The staff
dashboard frontend — like the Phase 12b merchant portal — remains deferred; `src/web` is untouched.
See `docs/superpowers/specs/2026-08-28-phase-13-reports-analytics-dashboards-design.md` for the
full design, including the resolved formulas for COD Expected/Collected/Pending/Settled/Merchant
Payable and NDR Rate/RTO Rate (neither CLAUDE.md §29 nor COD-LIFECYCLE.md/NDR's own entities define
these precisely).
```

- [ ] **Step 4: Run the full suite one final time**

Run: `dotnet test Courier.slnx`
Expected: all passed.

- [ ] **Step 5: Commit**

```bash
git add tests/Api.Tests/ReportsExitCriteriaEndToEndTests.cs docs/ROADMAP.md
git commit -m "test(api): add end-to-end proof that Reports satisfies Phase 13's exit criteria; sync ROADMAP.md"
```

---

## Post-Plan: Final Whole-Branch Review

Per this project's established pipeline (see `feedback-courier-sdd-workflow` in project memory): after all 10 tasks are merged in the worktree, run one whole-branch review on the most capable available model before merging to `master`, specifically checking (per this plan's own recurring-bug-class note in Global Constraints):

1. Every new lookup method in Tasks 3–7 is grep-checked against tables lacking their own `OrganizationId` (`DeliveryTask`/`DeliveryRun`) to confirm no accidental standalone `DbSet` query slipped in.
2. Every integer-persisted enum touched by this plan (`ShipmentStatus`, `ShipmentEventType`, `NdrStatus`, `ManifestStatus`, `CodCollectionStatus`) — this plan adds no new members to any of them, so there is nothing to append-vs-insert here, but confirm no task accidentally did.
3. `BranchDashboardService`/`OrgDashboardService`'s division-by-zero guards (`attempts > 0 ? ... : null`, `volume > 0 ? ... : null`) are exercised by a real test, not just present in code.
4. The two export endpoints never leak another organization's data even when `branchId` is a guessed/leaked id from a different org — `ResolveBranchScopeAsync`'s `accessible is not null && !accessible.Contains(branchId)` check is the load-bearing line; confirm a test exercises it directly (Task 9's `BranchDashboard_BranchFromAnotherOrganization_ReturnsReportsValidationFailed` covers the dashboard path — confirm an equivalent exists for at least one export endpoint, adding one in the fix wave if not).
