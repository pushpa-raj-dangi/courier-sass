# Frontend Phase F5 — Riders + Delivery + POD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the staff back-office UI for rider management, task assignment, rider self-service delivery, and POD capture, closing the 3 real backend gaps found during design (no run/task read endpoint, no rider-self-lookup path, 5 unconverted enums).

**Architecture:** 4 backend tasks add the minimum new read surface (`GET /riders/me`, `GET /riders/me/tasks/today`, a narrow `IShipmentTaskLookupService`) and fix enum serialization; 10 frontend tasks build `src/web/src/features/riders/` following the exact conventions F2–F4 already established (Sheet-over-pages via Zustand panel stores, TanStack Query hooks per endpoint, Next.js Route Handlers as a thin BFF proxy).

**Tech Stack:** ASP.NET Core / EF Core / xUnit (backend); Next.js 14 App Router, TanStack Query, React Hook Form + Zod, Zustand, Vitest + Testing Library (frontend). No new npm/NuGet packages.

**Spec:** `docs/superpowers/specs/2026-08-30-frontend-phase-f5-riders-delivery-pod-design.md`

## Global Constraints

- No new library without justification (CLAUDE.md §34) — the signature pad is hand-built `<canvas>`, no signature/drawing package.
- Detail/create UI is always a right-side `Sheet` driven by a Zustand store, never a routed page (`docs/ARCHITECTURE.md` §9a).
- Price, COD amount, permissions, and branch scope are never trusted from the frontend — every new screen only reads/writes through the existing permission-checked backend endpoints (CLAUDE.md §33).
- Every new screen gets loading/error/empty states using the existing `LoadingState`/`ErrorState`/`EmptyState`/`Spinner` primitives — no ad hoc spinners.
- Backend test command: `dotnet test tests/<Module>.Tests/<Module>.Tests.csproj`. Frontend: `cd src/web && npm test` (Vitest) and `npm run build` (production build/type-check) after every frontend task.
- All 5 Riders enums serialize as PascalCase strings once Task 1 lands (`"Pickup"`, not `0`) — every later task's TypeScript types use string literal unions, never numbers.

---

## Task 1: Fix Riders enum JSON serialization

**Files:**
- Modify: `src/Modules/Riders/Domain/DeliveryTaskType.cs`
- Modify: `src/Modules/Riders/Domain/DeliveryTaskStatus.cs`
- Modify: `src/Modules/Riders/Domain/DeliveryRunStatus.cs`
- Modify: `src/Modules/Riders/Domain/RiderRoleType.cs`
- Modify: `src/Modules/Riders/Domain/DeliveryAttemptReason.cs`
- Test: `tests/Riders.Tests/RiderEnumSerializationTests.cs`

**Interfaces:**
- Produces: all 5 enums now carry `[JsonConverter(typeof(JsonStringEnumConverter))]`, matching `ManifestStatus`/`ShipmentStatus`'s existing fix. Every later task's DTOs using these enums serialize as strings.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using Courier.Riders.Domain;
using Xunit;

namespace Courier.Riders.Tests;

public class RiderEnumSerializationTests
{
    [Fact]
    public void DeliveryTaskType_SerializesAsString() =>
        Assert.Equal("\"Pickup\"", JsonSerializer.Serialize(DeliveryTaskType.Pickup));

    [Fact]
    public void DeliveryTaskStatus_SerializesAsString() =>
        Assert.Equal("\"Pending\"", JsonSerializer.Serialize(DeliveryTaskStatus.Pending));

    [Fact]
    public void DeliveryRunStatus_SerializesAsString() =>
        Assert.Equal("\"Active\"", JsonSerializer.Serialize(DeliveryRunStatus.Active));

    [Fact]
    public void RiderRoleType_SerializesAsString() =>
        Assert.Equal("\"PickupRider\"", JsonSerializer.Serialize(RiderRoleType.PickupRider));

    [Fact]
    public void DeliveryAttemptReason_SerializesAsString() =>
        Assert.Equal("\"CustomerUnavailable\"", JsonSerializer.Serialize(DeliveryAttemptReason.CustomerUnavailable));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj --filter RiderEnumSerializationTests`
Expected: FAIL — each assertion gets `"0"`/`"1"` etc. instead of the quoted name.

- [ ] **Step 3: Add the converter to each enum**

For each of the 5 files, add the `using` and attribute. Example (`DeliveryTaskType.cs`):

```csharp
using System.Text.Json.Serialization;

namespace Courier.Riders.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeliveryTaskType
{
    Pickup,
    Delivery
}
```

Apply the identical `using System.Text.Json.Serialization;` + `[JsonConverter(typeof(JsonStringEnumConverter))]` pair to `DeliveryTaskStatus.cs`, `DeliveryRunStatus.cs`, `RiderRoleType.cs`, and `DeliveryAttemptReason.cs`, keeping each file's existing doc comments and member lists unchanged.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj --filter RiderEnumSerializationTests`
Expected: PASS (5/5)

- [ ] **Step 5: Run the full Riders test suite to check for regressions, then commit**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj`
Expected: PASS (all tests)

```bash
git add src/Modules/Riders/Domain tests/Riders.Tests/RiderEnumSerializationTests.cs
git commit -m "fix(riders): serialize all 5 Riders enums as strings, not raw integers"
```

---

## Task 2: Add IShipmentTaskLookupService

**Files:**
- Create: `src/Modules/Shipments/Application/IShipmentTaskLookupService.cs`
- Create: `src/Modules/Shipments/Application/ShipmentService.TaskLookup.cs`
- Modify: `src/Api/Program.cs:143` (add DI registration)
- Test: `tests/Shipments.Tests/ShipmentServiceTaskLookupTests.cs`

**Interfaces:**
- Produces: `IShipmentTaskLookupService.GetTaskInfoAsync(Guid shipmentId, CancellationToken)` → `ShipmentTaskInfo?` record `(string Awb, string RecipientName, string RecipientPhone, string RecipientAddressLine, int RecipientWardNumber, decimal CodAmount)`. Task 4 consumes this from `RiderService`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Cod.Application;
using Courier.Events.Application;
using Courier.Geography.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Pricing.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.Shipments.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Shipments.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FakeBranchLookup : IBranchLookupService
{
    private readonly Dictionary<Guid, Guid?> _zonesByBranch;
    public FakeBranchLookup(Dictionary<Guid, Guid?> zonesByBranch) => _zonesByBranch = zonesByBranch;
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_zonesByBranch.TryGetValue(branchId, out var zoneId) ? new BranchZoneInfo(branchId, zoneId) : null);
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class AlwaysTrueGeographyLookup : IGeographyLookupService
{
    public Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesByIdsAsync(IReadOnlySet<Guid> merchantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class FixedOrganizationLookup : IOrganizationLookupService
{
    private readonly string? _slug;
    public FixedOrganizationLookup(string? slug) => _slug = slug;
    public Task<string?> GetSlugAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(_slug);
}

file class NoopCodCollectionCreationService : ICodCollectionCreationService
{
    public Task CreateExpectedAsync(Guid shipmentId, Guid organizationId, Guid merchantId, Guid branchId, decimal expectedAmount, decimal shipmentTotalCharge, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class ShipmentServiceTaskLookupTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
            modelBuilder.ApplyConfiguration(new ServiceTypeConfiguration());
            modelBuilder.ApplyConfiguration(new WeightSlabConfiguration());
            modelBuilder.ApplyConfiguration(new RateCardConfiguration());
            modelBuilder.ApplyConfiguration(new ZoneRateConfiguration());
            modelBuilder.ApplyConfiguration(new AdditionalChargeConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantRateCardConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentItemConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentEventConfiguration());
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

    private static ShipmentService CreateService(TestDbContext dbContext, Guid organizationId, Dictionary<Guid, Guid?> zonesByBranch)
    {
        var branchLookup = new FakeBranchLookup(zonesByBranch);
        var pricingService = new PricingService(dbContext, new FixedTenantProvider(organizationId), branchLookup, new AlwaysTrueMerchantLookup());
        return new ShipmentService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), branchLookup,
            new AlwaysTrueGeographyLookup(), new AlwaysTrueMerchantLookup(), pricingService, new AllBranchesAuthorizationChecker(),
            new FixedOrganizationLookup("testorg"), new NoopCodCollectionCreationService(), new NoopOutboxWriter());
    }

    private static async Task<(ShipmentService Service, Guid ShipmentId, string Awb)> SeedCreatedShipmentAsync(TestDbContext dbContext, Guid organizationId)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;

        if (!dbContext.Set<Organization>().Any(o => o.Id == organizationId))
        {
            dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Test Org", Slug = "testorg" });
        }
        var rateCard = dbContext.Set<RateCard>().FirstOrDefault(r => r.OrganizationId == organizationId);
        if (rateCard is null)
        {
            rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
            dbContext.Set<RateCard>().Add(rateCard);
        }
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = weightSlabId, BaseRate = 100m });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, organizationId, new() { [originBranchId] = originZoneId, [destinationBranchId] = destinationZoneId });
        var request = new CreateShipmentRequest(
            Guid.NewGuid(), originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 500m, false,
            "Recipient", "9800000000", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Recipient Address",
            "Sender", "9800000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Sender Address",
            new List<CreateShipmentItemRequest> { new("Item", 1, 100m) });
        var created = await service.CreateShipmentAsync(request, ActingUser(Guid.NewGuid()));

        return (service, created.Id, created.Awb);
    }

    [Fact]
    public async Task GetTaskInfoAsync_ExistingShipment_ReturnsRecipientAndCodInfo()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, awb) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        IShipmentTaskLookupService lookup = service;

        var info = await lookup.GetTaskInfoAsync(shipmentId);

        Assert.NotNull(info);
        Assert.Equal(awb, info!.Awb);
        Assert.Equal("Recipient", info.RecipientName);
        Assert.Equal("9800000000", info.RecipientPhone);
        Assert.Equal("Recipient Address", info.RecipientAddressLine);
        Assert.Equal(1, info.RecipientWardNumber);
        Assert.Equal(500m, info.CodAmount);
    }

    [Fact]
    public async Task GetTaskInfoAsync_UnknownShipment_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), new());
        IShipmentTaskLookupService lookup = service;

        Assert.Null(await lookup.GetTaskInfoAsync(Guid.NewGuid()));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter ShipmentServiceTaskLookupTests`
Expected: FAIL to compile — `IShipmentTaskLookupService` doesn't exist yet.

- [ ] **Step 3: Create the interface**

```csharp
namespace Courier.Shipments.Application;

public record ShipmentTaskInfo(
    string Awb, string RecipientName, string RecipientPhone,
    string RecipientAddressLine, int RecipientWardNumber, decimal CodAmount);

// Narrow read-only projection for Riders' "my tasks today" view (Phase F5 frontend) — mirrors
// IBranchLookupService's cross-module lookup pattern so Riders never touches Shipments' DbSet
// directly. Deliberately separate from ShipmentScanInfo: that record is for scan-check validation
// (state + custody), this one is for rendering a task card (recipient contact/address + COD).
public interface IShipmentTaskLookupService
{
    Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement it on ShipmentService**

```csharp
using Microsoft.EntityFrameworkCore;
using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

public partial class ShipmentService : IShipmentTaskLookupService
{
    public async Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        return shipment is null ? null : new ShipmentTaskInfo(
            shipment.Awb, shipment.RecipientName, shipment.RecipientPhone,
            shipment.RecipientAddressLine, shipment.RecipientWardNumber, shipment.CodAmount);
    }
}
```

- [ ] **Step 5: Register in DI**

In `src/Api/Program.cs`, immediately after the existing line
`builder.Services.AddScoped<IShipmentTransitionService>(sp => sp.GetRequiredService<ShipmentService>());` (line 143), add:

```csharp
builder.Services.AddScoped<IShipmentTaskLookupService>(sp => sp.GetRequiredService<ShipmentService>());
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter ShipmentServiceTaskLookupTests`
Expected: PASS (2/2)

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Shipments/Application/IShipmentTaskLookupService.cs src/Modules/Shipments/Application/ShipmentService.TaskLookup.cs src/Api/Program.cs tests/Shipments.Tests/ShipmentServiceTaskLookupTests.cs
git commit -m "feat(shipments): add IShipmentTaskLookupService for Riders' task enrichment"
```

---

## Task 3: Add GET /api/v1/riders/me

**Files:**
- Modify: `src/Modules/Riders/Application/RiderService.Reads.cs`
- Modify: `src/Modules/Riders/Presentation/RidersEndpoints.cs`
- Modify: `tests/Riders.Tests/RiderServiceReadsTests.cs`
- Modify: `tests/Api.Tests/RidersEndpointsTests.cs`

**Interfaces:**
- Consumes: nothing new (uses existing `_dbContext`, `actingUser.GetUserId()` extension from `Courier.SharedKernel`).
- Produces: `RiderService.GetMyRiderAsync(ClaimsPrincipal, CancellationToken)` → `RiderResponse?`. No new constructor dependency.

- [ ] **Step 1: Write the failing unit tests**

In `tests/Riders.Tests/RiderServiceReadsTests.cs`, add inside `RiderServiceReadsTests`:

```csharp
    [Fact]
    public async Task GetMyRiderAsync_UserHasRiderRecord_ReturnsIt()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var rider = new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = userId, BaseBranchId = Guid.NewGuid(), LicenseNumber = "LIC-3", RoleType = RiderRoleType.DeliveryRider, IsActive = true };
        dbContext.Set<Rider>().Add(rider);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker());

        var result = await service.GetMyRiderAsync(ActingUser(userId));

        Assert.NotNull(result);
        Assert.Equal(rider.Id, result!.Id);
    }

    [Fact]
    public async Task GetMyRiderAsync_UserHasNoRiderRecord_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), new RestrictedBranchAuthorizationChecker());

        var result = await service.GetMyRiderAsync(ActingUser(Guid.NewGuid()));

        Assert.Null(result);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj --filter RiderServiceReadsTests`
Expected: FAIL to compile — `GetMyRiderAsync` doesn't exist yet.

- [ ] **Step 3: Implement GetMyRiderAsync**

In `src/Modules/Riders/Application/RiderService.Reads.cs`, add `using Courier.SharedKernel;` to the using list (it currently has `System.Security.Claims`, `Courier.Riders.Domain`, `Microsoft.EntityFrameworkCore` only), then add this method inside the `partial class RiderService`:

```csharp
    // Self-scoped by design (Phase F5's rider self-service model) — a rider holds no permission
    // that would let them call ListAsync/GetByIdAsync to find their own Rider.Id, so this is the
    // only path from a logged-in rider's UserId to their Rider record.
    public async Task<RiderResponse?> GetMyRiderAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var userId = actingUser.GetUserId();
        if (userId is null)
        {
            return null;
        }
        var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);
        return rider is null ? null : ToResponse(rider);
    }
```

- [ ] **Step 4: Add the endpoint**

In `src/Modules/Riders/Presentation/RidersEndpoints.cs`, insert this immediately before the existing `riders.MapGet("/{id:guid}", ...)` mapping (no `.RequirePermission(...)` — authenticated only, same shape as `GET /api/v1/auth/me`):

```csharp
        riders.MapGet("/me", async (RiderService service, HttpContext httpContext) =>
        {
            var result = await service.GetMyRiderAsync(httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

```

- [ ] **Step 5: Run unit tests to verify they pass**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj --filter RiderServiceReadsTests`
Expected: PASS (all tests in this file)

- [ ] **Step 6: Write the failing integration tests**

In `tests/Api.Tests/RidersEndpointsTests.cs`, add inside `RidersEndpointsTests`:

```csharp
    [Fact]
    public async Task GetMyRider_LoggedInAsRider_ReturnsOwnRecord()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "me-rider");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(client);
        var suffix = Guid.NewGuid().ToString("N");
        var riderEmail = $"rider-{suffix}@example.com";
        var createResponse = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            riderEmail, "P@ssw0rd123!", "Self Service Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));
        var createdRider = await createResponse.Content.ReadFromJsonAsync<RiderResponse>();

        var riderClient = _factory.CreateClient();
        var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmail, "P@ssw0rd123!"));
        var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);

        var response = await riderClient.GetAsync("/api/v1/riders/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RiderResponse>();
        Assert.Equal(createdRider!.Id, result!.Id);
    }

    [Fact]
    public async Task GetMyRider_LoggedInAsOwnerNotRider_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "me-nonrider");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/riders/me");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
```

- [ ] **Step 7: Run integration tests to verify they pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter RidersEndpointsTests`
Expected: PASS (all tests in this file)

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Riders/Application/RiderService.Reads.cs src/Modules/Riders/Presentation/RidersEndpoints.cs tests/Riders.Tests/RiderServiceReadsTests.cs tests/Api.Tests/RidersEndpointsTests.cs
git commit -m "feat(riders): add GET /api/v1/riders/me for rider self-lookup"
```

---

## Task 4: Add GET /api/v1/riders/me/tasks/today

**Files:**
- Modify: `src/Modules/Riders/Application/RiderDtos.cs`
- Modify: `src/Modules/Riders/Application/RiderService.cs`
- Modify: `src/Modules/Riders/Application/RiderService.Reads.cs`
- Modify: `src/Modules/Riders/Presentation/RidersEndpoints.cs`
- Modify: `tests/Riders.Tests/RiderServiceReadsTests.cs`
- Modify: `tests/Riders.Tests/RiderServiceAttemptsTests.cs`
- Modify: `tests/Riders.Tests/RiderServiceCreationTests.cs`
- Modify: `tests/Riders.Tests/RiderServiceTasksTests.cs`
- Modify: `tests/Api.Tests/RidersEndpointsTests.cs`

**Interfaces:**
- Consumes: `IShipmentTaskLookupService.GetTaskInfoAsync` (Task 2).
- Produces: `RiderService.GetMyTasksTodayAsync(ClaimsPrincipal, CancellationToken)` → `MyTasksTodayResponse(Guid? DeliveryRunId, List<MyTaskResponse> Tasks)`. `RiderService`'s constructor gains a 13th parameter, `IShipmentTaskLookupService shipmentTaskLookupService`, appended last — every existing `new RiderService(...)` call site in the 4 test files must append one more argument.

- [ ] **Step 1: Add the new DTOs**

In `src/Modules/Riders/Application/RiderDtos.cs`, add at the end of the file:

```csharp
public record MyTaskResponse(Guid TaskId, Guid ShipmentId, DeliveryTaskType Type, int Sequence, string Awb, string RecipientName, string RecipientPhone, string RecipientAddressLine, int RecipientWardNumber, decimal CodAmount);

public record MyTasksTodayResponse(Guid? DeliveryRunId, List<MyTaskResponse> Tasks);
```

- [ ] **Step 2: Wire the new constructor dependency**

In `src/Modules/Riders/Application/RiderService.cs`, add the field and constructor parameter (append last, after `outboxWriter`):

```csharp
    private readonly IOutboxWriter _outboxWriter;
    private readonly IShipmentTaskLookupService _shipmentTaskLookupService;

    public RiderService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IUserProvisioningService userProvisioningService,
        IBranchLookupService branchLookupService,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IShipmentTransitionService shipmentTransitionService,
        IObjectStorage objectStorage,
        INdrCreationService ndrCreationService,
        ICodCollectionService codCollectionService,
        IPermissionChecker permissionChecker,
        IOutboxWriter outboxWriter,
        IShipmentTaskLookupService shipmentTaskLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _userProvisioningService = userProvisioningService;
        _branchLookupService = branchLookupService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _shipmentTransitionService = shipmentTransitionService;
        _objectStorage = objectStorage;
        _ndrCreationService = ndrCreationService;
        _codCollectionService = codCollectionService;
        _permissionChecker = permissionChecker;
        _outboxWriter = outboxWriter;
        _shipmentTaskLookupService = shipmentTaskLookupService;
    }
```

(Replace the existing field list and constructor entirely — this is the same file shown in the design investigation, just with the one new field/parameter/assignment added at the end of each block.)

- [ ] **Step 3: Fix the 3 test files that don't yet need real task-lookup behavior**

In `tests/Riders.Tests/RiderServiceCreationTests.cs`, add this file-scoped class near the other `file class` fakes (e.g. right after `NullObjectStorage`):

```csharp
file class NotSupportedShipmentTaskLookupService : IShipmentTaskLookupService
{
    public Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
```

Then change its `CreateService` helper's `new RiderService(...)` call from:

```csharp
        var service = new RiderService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), provisioning,
            new FakeBranchLookup(knownBranchId), new AllBranchesAuthorizationChecker(),
            new NotSupportedShipmentTransitionService(), new NullObjectStorage(), new NotSupportedNdrCreationService(),
            new NoopCodCollectionService(), new AlwaysTruePermissionChecker(), new NoopOutboxWriter());
```

to:

```csharp
        var service = new RiderService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), provisioning,
            new FakeBranchLookup(knownBranchId), new AllBranchesAuthorizationChecker(),
            new NotSupportedShipmentTransitionService(), new NullObjectStorage(), new NotSupportedNdrCreationService(),
            new NoopCodCollectionService(), new AlwaysTruePermissionChecker(), new NoopOutboxWriter(), new NotSupportedShipmentTaskLookupService());
```

Repeat the identical `file class NotSupportedShipmentTaskLookupService` addition in `tests/Riders.Tests/RiderServiceAttemptsTests.cs` and `tests/Riders.Tests/RiderServiceTasksTests.cs` (each file needs its own copy — `file class` is file-scoped, this matches the existing one-fake-set-per-file convention).

In `tests/Riders.Tests/RiderServiceAttemptsTests.cs`, change:

```csharp
        var service = new RiderService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), new AllBranchesAuthorizationChecker(), shipments, storage, ndr, cod, permissionChecker, outboxWriter);
```

to:

```csharp
        var service = new RiderService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), new AllBranchesAuthorizationChecker(), shipments, storage, ndr, cod, permissionChecker, outboxWriter, new NotSupportedShipmentTaskLookupService());
```

In `tests/Riders.Tests/RiderServiceTasksTests.cs`, change:

```csharp
        var service = new RiderService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), new AllBranchesAuthorizationChecker(), shipments, new NotSupportedObjectStorage(), new NotSupportedNdrCreationService(),
            new NoopCodCollectionService(), new AlwaysTruePermissionChecker(), new NoopOutboxWriter());
```

to:

```csharp
        var service = new RiderService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), new AllBranchesAuthorizationChecker(), shipments, new NotSupportedObjectStorage(), new NotSupportedNdrCreationService(),
            new NoopCodCollectionService(), new AlwaysTruePermissionChecker(), new NoopOutboxWriter(), new NotSupportedShipmentTaskLookupService());
```

- [ ] **Step 4: Run the full Riders suite to confirm it still compiles and passes**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj`
Expected: PASS (all tests) — this proves the constructor change didn't break Tasks 1–3's tests.

- [ ] **Step 5: Write the failing new tests in RiderServiceReadsTests.cs**

Add this file-scoped fake near the top of `tests/Riders.Tests/RiderServiceReadsTests.cs` (alongside `NotSupportedBranchLookup` etc.):

```csharp
file class FakeShipmentTaskLookupService : IShipmentTaskLookupService
{
    private readonly Dictionary<Guid, ShipmentTaskInfo> _infoByShipmentId;
    public FakeShipmentTaskLookupService(Dictionary<Guid, ShipmentTaskInfo> infoByShipmentId) => _infoByShipmentId = infoByShipmentId;
    public Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_infoByShipmentId.TryGetValue(shipmentId, out var info) ? info : null);
}

file class NotSupportedShipmentTaskLookupService : IShipmentTaskLookupService
{
    public Task<ShipmentTaskInfo?> GetTaskInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
```

Change the file's `CreateService` helper to accept an optional lookup (defaulting to the not-supported fake, so Tasks 1–3's existing tests and calls need no edits):

```csharp
    private static RiderService CreateService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker checker, IShipmentTaskLookupService? shipmentTaskLookup = null) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), checker, new NotSupportedShipmentTransitionService(), new NotSupportedObjectStorage(), new NotSupportedNdrCreationService(),
            new NoopCodCollectionService(), new AlwaysTruePermissionChecker(), new NoopOutboxWriter(), shipmentTaskLookup ?? new NotSupportedShipmentTaskLookupService());
```

Add these 3 tests inside `RiderServiceReadsTests`:

```csharp
    [Fact]
    public async Task GetMyTasksTodayAsync_ActiveRunWithPendingTask_ReturnsEnrichedTask()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var rider = new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = userId, BaseBranchId = branchId, LicenseNumber = "LIC-4", RoleType = RiderRoleType.DeliveryRider, IsActive = true };
        dbContext.Set<Rider>().Add(rider);
        var run = new DeliveryRun { Id = Guid.NewGuid(), RiderId = rider.Id, BranchId = branchId, Date = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date), Status = DeliveryRunStatus.Active };
        dbContext.Set<DeliveryRun>().Add(run);
        var shipmentId = Guid.NewGuid();
        var task = new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = shipmentId, Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending };
        dbContext.Set<DeliveryTask>().Add(task);
        await dbContext.SaveChangesAsync();
        var taskInfo = new ShipmentTaskInfo("AWB001", "Recipient One", "9800000000", "Recipient Address", 3, 500m);
        var lookup = new FakeShipmentTaskLookupService(new() { [shipmentId] = taskInfo });
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(), lookup);

        var result = await service.GetMyTasksTodayAsync(ActingUser(userId));

        Assert.Equal(run.Id, result.DeliveryRunId);
        var only = Assert.Single(result.Tasks);
        Assert.Equal(task.Id, only.TaskId);
        Assert.Equal("AWB001", only.Awb);
        Assert.Equal(500m, only.CodAmount);
    }

    [Fact]
    public async Task GetMyTasksTodayAsync_CompletedTaskExcluded_OnlyPendingReturned()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var rider = new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = userId, BaseBranchId = branchId, LicenseNumber = "LIC-5", RoleType = RiderRoleType.DeliveryRider, IsActive = true };
        dbContext.Set<Rider>().Add(rider);
        var run = new DeliveryRun { Id = Guid.NewGuid(), RiderId = rider.Id, BranchId = branchId, Date = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date), Status = DeliveryRunStatus.Active };
        dbContext.Set<DeliveryRun>().Add(run);
        dbContext.Set<DeliveryTask>().Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = Guid.NewGuid(), Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Completed });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(), new FakeShipmentTaskLookupService(new()));

        var result = await service.GetMyTasksTodayAsync(ActingUser(userId));

        Assert.Equal(run.Id, result.DeliveryRunId);
        Assert.Empty(result.Tasks);
    }

    [Fact]
    public async Task GetMyTasksTodayAsync_NoActiveRunToday_ReturnsNullRunAndEmptyTasks()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var rider = new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = userId, BaseBranchId = Guid.NewGuid(), LicenseNumber = "LIC-6", RoleType = RiderRoleType.DeliveryRider, IsActive = true };
        dbContext.Set<Rider>().Add(rider);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(), new FakeShipmentTaskLookupService(new()));

        var result = await service.GetMyTasksTodayAsync(ActingUser(userId));

        Assert.Null(result.DeliveryRunId);
        Assert.Empty(result.Tasks);
    }
```

- [ ] **Step 6: Run tests to verify they fail**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj --filter RiderServiceReadsTests`
Expected: FAIL to compile — `GetMyTasksTodayAsync` doesn't exist yet.

- [ ] **Step 7: Implement GetMyTasksTodayAsync**

In `src/Modules/Riders/Application/RiderService.Reads.cs`, add this method after `GetMyRiderAsync`:

```csharp
    // Sequential awaits, not Task.WhenAll — EF Core's DbContext is not thread-safe, and Phase 13
    // hit a real InvalidOperationException from exactly this shape of concurrent-await-on-one-
    // DbContext mistake.
    public async Task<MyTasksTodayResponse> GetMyTasksTodayAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var userId = actingUser.GetUserId();
        if (userId is null)
        {
            return new MyTasksTodayResponse(null, new List<MyTaskResponse>());
        }
        var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);
        if (rider is null)
        {
            return new MyTasksTodayResponse(null, new List<MyTaskResponse>());
        }

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var run = await _dbContext.Set<DeliveryRun>().FirstOrDefaultAsync(
            r => r.RiderId == rider.Id && r.Date == today && r.Status == DeliveryRunStatus.Active, cancellationToken);
        if (run is null)
        {
            return new MyTasksTodayResponse(null, new List<MyTaskResponse>());
        }

        var pendingTasks = await _dbContext.Set<DeliveryTask>()
            .Where(t => t.DeliveryRunId == run.Id && t.Status == DeliveryTaskStatus.Pending)
            .OrderBy(t => t.Sequence)
            .ToListAsync(cancellationToken);

        var results = new List<MyTaskResponse>();
        foreach (var task in pendingTasks)
        {
            var info = await _shipmentTaskLookupService.GetTaskInfoAsync(task.ShipmentId, cancellationToken);
            if (info is null)
            {
                continue;
            }
            results.Add(new MyTaskResponse(task.Id, task.ShipmentId, task.Type, task.Sequence, info.Awb, info.RecipientName, info.RecipientPhone, info.RecipientAddressLine, info.RecipientWardNumber, info.CodAmount));
        }
        return new MyTasksTodayResponse(run.Id, results);
    }
```

- [ ] **Step 8: Add the endpoint**

In `src/Modules/Riders/Presentation/RidersEndpoints.cs`, add this right after the `/me` mapping from Task 3 (still before `/{id:guid}`):

```csharp
        riders.MapGet("/me/tasks/today", async (RiderService service, HttpContext httpContext) =>
            Results.Ok(await service.GetMyTasksTodayAsync(httpContext.User, httpContext.RequestAborted)));

```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj`
Expected: PASS (all tests)

- [ ] **Step 10: Write and run the integration tests**

Add to `tests/Api.Tests/RidersEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task GetMyTasksToday_AssignedDeliveryTask_ReturnsEnrichedPendingTask()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "my-tasks");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var suffix = Guid.NewGuid().ToString("N");
        var deliveryRiderEmail = $"rider-{suffix}@example.com";
        var deliveryRiderResponse = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            deliveryRiderEmail, "P@ssw0rd123!", "Delivery Rider", null, $"LIC-{suffix[..10]}", destinationBranchId, RiderRoleType.DeliveryRider));
        var deliveryRider = await deliveryRiderResponse.Content.ReadFromJsonAsync<RiderResponse>();

        var shipment = await BookAndMoveToDestinationAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId);
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRider!.Id, shipment.Id, DeliveryTaskType.Delivery));

        var riderClient = _factory.CreateClient();
        var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(deliveryRiderEmail, "P@ssw0rd123!"));
        var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);

        var response = await riderClient.GetAsync("/api/v1/riders/me/tasks/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MyTasksTodayResponse>();
        var task = Assert.Single(result!.Tasks);
        Assert.Equal(shipment.Awb, task.Awb);
        Assert.Equal(DeliveryTaskType.Delivery, task.Type);
    }

    [Fact]
    public async Task GetMyTasksToday_TwoRidersSameOrg_EachSeesOnlyOwnTasks()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "my-tasks-isolation");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);

        var suffixA = Guid.NewGuid().ToString("N");
        var riderA = await (await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-a-{suffixA}@example.com", "P@ssw0rd123!", "Rider A", null, $"LIC-{suffixA[..10]}", destinationBranchId, RiderRoleType.DeliveryRider))).Content.ReadFromJsonAsync<RiderResponse>();
        var suffixB = Guid.NewGuid().ToString("N");
        var riderBEmail = $"rider-b-{suffixB}@example.com";
        await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            riderBEmail, "P@ssw0rd123!", "Rider B", null, $"LIC-{suffixB[..10]}", destinationBranchId, RiderRoleType.DeliveryRider));

        var shipment = await BookAndMoveToDestinationAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId);
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(riderA!.Id, shipment.Id, DeliveryTaskType.Delivery));

        var riderBClient = _factory.CreateClient();
        var loginB = await riderBClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderBEmail, "P@ssw0rd123!"));
        var authB = await loginB.Content.ReadFromJsonAsync<AuthResult>();
        riderBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authB!.AccessToken);

        var response = await riderBClient.GetAsync("/api/v1/riders/me/tasks/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<MyTasksTodayResponse>();
        Assert.Null(result!.DeliveryRunId);
        Assert.Empty(result.Tasks);
    }
```

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter RidersEndpointsTests`
Expected: PASS (all tests in this file)

- [ ] **Step 11: Run the full backend test suite once, to confirm no cross-module regressions**

Run: `dotnet test`
Expected: PASS (all test projects)

- [ ] **Step 12: Commit**

```bash
git add src/Modules/Riders tests/Riders.Tests tests/Api.Tests/RidersEndpointsTests.cs
git commit -m "feat(riders): add GET /api/v1/riders/me/tasks/today for rider self-service"
```

---

## Task 5: Riders feature foundation (types, hooks, BFF routes)

**Files:**
- Create: `src/web/src/features/riders/types.ts`
- Create: `src/web/src/features/riders/hooks/useRiders.ts`
- Create: `src/web/src/features/riders/hooks/useCreateRider.ts`
- Create: `src/web/src/features/riders/hooks/useVehicles.ts`
- Create: `src/web/src/features/riders/hooks/useCreateVehicle.ts`
- Modify: `src/web/src/app/api/riders/route.ts`
- Create: `src/web/src/app/api/riders/route.test.ts`
- Create: `src/web/src/app/api/vehicles/route.ts`
- Create: `src/web/src/app/api/vehicles/route.test.ts`
- Modify: `src/web/src/features/manifests/components/ManifestSealedSection.tsx`
- Delete: `src/web/src/features/manifests/hooks/useRiders.ts`

**Interfaces:**
- Produces: `Rider`, `CreateRiderInput`, `Vehicle`, `CreateVehicleInput` types; `useRiders()`, `useCreateRider()`, `useVehicles()`, `useCreateVehicle()` hooks — all consumed by Tasks 6–8.

- [ ] **Step 1: Write the failing BFF route tests**

Create `src/web/src/app/api/riders/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET, POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/riders", () => {
  it("proxies to the backend and returns the response", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    } as Response);

    const response = await GET();

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders");
    expect(response.status).toBe(200);
  });
});

describe("POST /api/riders", () => {
  it("proxies the create request body to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => ({ id: "r1", licenseNumber: "LIC-1" }),
    } as Response);

    const request = new Request("http://localhost/api/riders", {
      method: "POST",
      body: JSON.stringify({ email: "a@example.com" }),
    });
    const response = await POST(request);

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/riders",
      expect.objectContaining({ method: "POST" })
    );
    expect(response.status).toBe(201);
  });
});
```

Create `src/web/src/app/api/vehicles/route.test.ts` (identical shape, `/api/vehicles` / `/api/v1/vehicles`):

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET, POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/vehicles", () => {
  it("proxies to the backend and returns the response", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    } as Response);

    const response = await GET();

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/vehicles");
    expect(response.status).toBe(200);
  });
});

describe("POST /api/vehicles", () => {
  it("proxies the create request body to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => ({ id: "v1", plateNumber: "BA 2 KHA 1234" }),
    } as Response);

    const request = new Request("http://localhost/api/vehicles", {
      method: "POST",
      body: JSON.stringify({ type: "Motorcycle", plateNumber: "BA 2 KHA 1234" }),
    });
    const response = await POST(request);

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/vehicles",
      expect.objectContaining({ method: "POST" })
    );
    expect(response.status).toBe(201);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd src/web && npm test -- api/riders/route.test.ts api/vehicles/route.test.ts`
Expected: FAIL — `route.ts` has no `POST` export yet (riders) or doesn't exist yet (vehicles).

- [ ] **Step 3: Update the riders BFF route and create the vehicles BFF route**

Replace `src/web/src/app/api/riders/route.ts` entirely with:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/riders");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: Request) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/riders", {
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

Create `src/web/src/app/api/vehicles/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/vehicles");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: Request) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/vehicles", {
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

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd src/web && npm test -- api/riders/route.test.ts api/vehicles/route.test.ts`
Expected: PASS (4/4)

- [ ] **Step 5: Add the feature types and hooks**

Create `src/web/src/features/riders/types.ts`:

```ts
export type RiderRoleType = "PickupRider" | "DeliveryRider";

export interface Rider {
  id: string;
  userId: string;
  baseBranchId: string;
  licenseNumber: string;
  roleType: RiderRoleType;
  isActive: boolean;
}

export interface CreateRiderInput {
  email: string;
  password: string;
  fullName: string;
  phone?: string;
  licenseNumber: string;
  baseBranchId: string;
  roleType: RiderRoleType;
}

export interface Vehicle {
  id: string;
  riderId: string | null;
  type: string;
  plateNumber: string;
  capacity: number | null;
}

export interface CreateVehicleInput {
  riderId?: string;
  type: string;
  plateNumber: string;
  capacity?: number;
}
```

Create `src/web/src/features/riders/hooks/useRiders.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Rider } from "@/features/riders/types";

export function useRiders() {
  return useQuery({
    queryKey: ["riders"],
    queryFn: () => clientFetch<Rider[]>("/api/riders"),
  });
}
```

Create `src/web/src/features/riders/hooks/useCreateRider.ts`:

```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Rider, CreateRiderInput } from "@/features/riders/types";

export function useCreateRider() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateRiderInput) =>
      clientFetch<Rider>("/api/riders", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["riders"] });
    },
  });
}
```

Create `src/web/src/features/riders/hooks/useVehicles.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Vehicle } from "@/features/riders/types";

export function useVehicles() {
  return useQuery({
    queryKey: ["vehicles"],
    queryFn: () => clientFetch<Vehicle[]>("/api/vehicles"),
  });
}
```

Create `src/web/src/features/riders/hooks/useCreateVehicle.ts`:

```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Vehicle, CreateVehicleInput } from "@/features/riders/types";

export function useCreateVehicle() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateVehicleInput) =>
      clientFetch<Vehicle>("/api/vehicles", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["vehicles"] });
    },
  });
}
```

- [ ] **Step 6: Consolidate Manifests' local useRiders onto the new canonical one**

In `src/web/src/features/manifests/components/ManifestSealedSection.tsx`, change:

```ts
import { useRiders } from "@/features/manifests/hooks/useRiders";
```

to:

```ts
import { useRiders } from "@/features/riders/hooks/useRiders";
```

The rest of the component is unchanged — the new `Rider` type is a superset (`r.id`/`r.licenseNumber` both still exist) of the old minimal local one.

Delete `src/web/src/features/manifests/hooks/useRiders.ts` (now unused — it was the only consumer).

- [ ] **Step 7: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds (no other file imported the deleted `manifests/hooks/useRiders.ts`)

- [ ] **Step 8: Commit**

```bash
git add src/web/src/features/riders src/web/src/app/api/riders/route.ts src/web/src/app/api/riders/route.test.ts src/web/src/app/api/vehicles src/web/src/features/manifests/components/ManifestSealedSection.tsx
git rm src/web/src/features/manifests/hooks/useRiders.ts
git commit -m "feat(web): add Riders feature foundation; consolidate manifests' local useRiders"
```

---

## Task 6: Riders list page, RiderCreateSheet, sidebar nav entry

**Files:**
- Create: `src/web/src/features/riders/store/riderPanelStore.ts`
- Create: `src/web/src/features/riders/store/riderPanelStore.test.ts`
- Create: `src/web/src/features/riders/schema.ts`
- Create: `src/web/src/features/riders/schema.test.ts`
- Create: `src/web/src/features/riders/components/RiderCreateSheet.tsx`
- Create: `src/web/src/features/riders/components/RiderCreateSheet.test.tsx`
- Create: `src/web/src/app/(staff)/riders/page.tsx`
- Modify: `src/web/src/app/(staff)/layout.tsx`
- Modify: `src/web/src/components/nav-main.tsx`
- Modify: `src/web/src/components/nav-main.test.tsx`

**Interfaces:**
- Consumes: `useRiders`, `useCreateRider` (Task 5), `useBranches` (existing).
- Produces: `useRiderPanelStore` with modes `closed | create-rider` — Task 7 extends this with `create-vehicle`, Task 8 with `assign-task`.

- [ ] **Step 1: Write the failing store test**

Create `src/web/src/features/riders/store/riderPanelStore.test.ts`:

```ts
import { describe, expect, it, beforeEach } from "vitest";
import { useRiderPanelStore } from "./riderPanelStore";

describe("useRiderPanelStore", () => {
  beforeEach(() => {
    useRiderPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useRiderPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreateRider switches to the create-rider panel", () => {
    useRiderPanelStore.getState().openCreateRider();
    expect(useRiderPanelStore.getState().panel).toEqual({ mode: "create-rider" });
  });

  it("close returns to closed", () => {
    useRiderPanelStore.getState().openCreateRider();
    useRiderPanelStore.getState().close();
    expect(useRiderPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npm test -- riderPanelStore.test.ts`
Expected: FAIL — module doesn't exist yet.

- [ ] **Step 3: Create the store**

```ts
import { create } from "zustand";

type RiderPanelState =
  | { mode: "closed" }
  | { mode: "create-rider" };

interface RiderPanelStore {
  panel: RiderPanelState;
  openCreateRider: () => void;
  close: () => void;
}

export const useRiderPanelStore = create<RiderPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreateRider: () => set({ panel: { mode: "create-rider" } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/web && npm test -- riderPanelStore.test.ts`
Expected: PASS (3/3)

- [ ] **Step 5: Write the failing schema test**

Create `src/web/src/features/riders/schema.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { createRiderSchema } from "./schema";

describe("createRiderSchema", () => {
  it("accepts a valid rider", () => {
    const result = createRiderSchema.safeParse({
      email: "rider@example.com",
      password: "P@ssw0rd123!",
      fullName: "Test Rider",
      licenseNumber: "LIC-001",
      baseBranchId: "b1",
      roleType: "DeliveryRider",
    });
    expect(result.success).toBe(true);
  });

  it("rejects an invalid email", () => {
    const result = createRiderSchema.safeParse({
      email: "not-an-email",
      password: "P@ssw0rd123!",
      fullName: "Test Rider",
      licenseNumber: "LIC-001",
      baseBranchId: "b1",
      roleType: "DeliveryRider",
    });
    expect(result.success).toBe(false);
  });

  it("rejects a missing base branch", () => {
    const result = createRiderSchema.safeParse({
      email: "rider@example.com",
      password: "P@ssw0rd123!",
      fullName: "Test Rider",
      licenseNumber: "LIC-001",
      baseBranchId: "",
      roleType: "DeliveryRider",
    });
    expect(result.success).toBe(false);
  });
});
```

- [ ] **Step 6: Run test to verify it fails**

Run: `cd src/web && npm test -- riders/schema.test.ts`
Expected: FAIL — module doesn't exist yet.

- [ ] **Step 7: Create the schema**

```ts
import { z } from "zod";

export const createRiderSchema = z.object({
  email: z.string().email("Enter a valid email."),
  password: z.string().min(8, "Password must be at least 8 characters."),
  fullName: z.string().min(1, "Full name is required."),
  phone: z.string().optional(),
  licenseNumber: z.string().min(1, "License number is required."),
  baseBranchId: z.string({ error: "Base branch is required." }).min(1, "Base branch is required."),
  roleType: z.enum(["PickupRider", "DeliveryRider"]),
});

export type CreateRiderFormValues = z.infer<typeof createRiderSchema>;
```

- [ ] **Step 8: Run test to verify it passes**

Run: `cd src/web && npm test -- riders/schema.test.ts`
Expected: PASS (3/3)

- [ ] **Step 9: Write the failing RiderCreateSheet test**

Create `src/web/src/features/riders/components/RiderCreateSheet.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RiderCreateSheet } from "./RiderCreateSheet";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <RiderCreateSheet />
    </QueryClientProvider>
  );
}

describe("RiderCreateSheet", () => {
  it("is not visible when the store is closed", () => {
    useRiderPanelStore.setState({ panel: { mode: "closed" } });
    renderWithProviders();
    expect(screen.queryByText("New Rider")).not.toBeInTheDocument();
  });

  it("is visible with its fields when the store is open", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([
      { id: "b1", code: "KTM", name: "Kathmandu Hub", type: "Hub", isActive: true },
    ]);
    useRiderPanelStore.setState({ panel: { mode: "create-rider" } });
    renderWithProviders();

    await waitFor(() => expect(screen.getByText("New Rider")).toBeInTheDocument());
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/license number/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/base branch/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/role/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 10: Run test to verify it fails**

Run: `cd src/web && npm test -- RiderCreateSheet.test.tsx`
Expected: FAIL — component doesn't exist yet.

- [ ] **Step 11: Create RiderCreateSheet**

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
import { createRiderSchema, type CreateRiderFormValues } from "@/features/riders/schema";
import { useCreateRider } from "@/features/riders/hooks/useCreateRider";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import { useBranches } from "@/features/branches/hooks/useBranches";

const ROLE_TYPES = ["PickupRider", "DeliveryRider"] as const;

export function RiderCreateSheet() {
  const panel = useRiderPanelStore((s) => s.panel);
  const close = useRiderPanelStore((s) => s.close);
  const isOpen = panel.mode === "create-rider";

  const createRider = useCreateRider();
  const { data: branches } = useBranches();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateRiderFormValues>({ resolver: zodResolver(createRiderSchema) });

  async function onSubmit(values: CreateRiderFormValues) {
    setServerError(null);
    try {
      await createRider.mutateAsync(values);
      reset();
      close();
    } catch {
      setServerError("Couldn't create the rider. Check the fields and try again.");
    }
  }

  return (
    <Sheet
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) close();
      }}
    >
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>New Rider</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="fullName">Full Name</Label>
            <Input id="fullName" {...register("fullName")} />
            {errors.fullName && <p className="text-sm text-destructive">{errors.fullName.message}</p>}
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="email">Email</Label>
              <Input id="email" type="email" {...register("email")} />
              {errors.email && <p className="text-sm text-destructive">{errors.email.message}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="password">Password</Label>
              <Input id="password" type="password" {...register("password")} />
              {errors.password && <p className="text-sm text-destructive">{errors.password.message}</p>}
            </div>
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="phone">Phone</Label>
              <Input id="phone" {...register("phone")} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="licenseNumber">License Number</Label>
              <Input id="licenseNumber" {...register("licenseNumber")} />
              {errors.licenseNumber && <p className="text-sm text-destructive">{errors.licenseNumber.message}</p>}
            </div>
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="baseBranchId">Base Branch</Label>
              <Controller
                name="baseBranchId"
                control={control}
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="baseBranchId" aria-label="Base branch">
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
              {errors.baseBranchId && <p className="text-sm text-destructive">{errors.baseBranchId.message}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="roleType">Role</Label>
              <Controller
                name="roleType"
                control={control}
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="roleType" aria-label="Role">
                      <SelectValue placeholder="Select role" />
                    </SelectTrigger>
                    <SelectContent>
                      {ROLE_TYPES.map((t) => (
                        <SelectItem key={t} value={t}>{t}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
              {errors.roleType && <p className="text-sm text-destructive">{errors.roleType.message}</p>}
            </div>
          </div>
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Rider"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 12: Run test to verify it passes**

Run: `cd src/web && npm test -- RiderCreateSheet.test.tsx`
Expected: PASS (2/2)

- [ ] **Step 13: Create the Riders list page**

```tsx
"use client";

import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import type { Rider } from "@/features/riders/types";

export default function RidersPage() {
  const { data, isLoading, isError, refetch } = useRiders();
  const canCreate = usePermission("rider.create");
  const openCreateRider = useRiderPanelStore((s) => s.openCreateRider);

  const columns: ColumnDef<Rider>[] = [
    { key: "licenseNumber", header: "License Number", render: (r) => r.licenseNumber },
    { key: "roleType", header: "Role", render: (r) => r.roleType },
    { key: "active", header: "Active", render: (r) => (r.isActive ? "Yes" : "No") },
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
        title="Couldn't load riders"
        description="Something went wrong fetching the rider list."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Riders</h1>
        {canCreate && (
          <Button size="sm" className="gap-1.5" onClick={() => openCreateRider()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            New Rider
          </Button>
        )}
      </div>
      <DataTable
        columns={columns}
        data={
          data && {
            items: data,
            page: 1,
            pageSize: data.length,
            totalCount: data.length,
            totalPages: 1,
          }
        }
        isLoading={false}
        emptyTitle="No riders yet"
        emptyDescription="Create your first rider to start assigning pickups and deliveries."
        onPageChange={() => {}}
        rowKey={(r) => r.id}
      />
    </div>
  );
}
```

- [ ] **Step 14: Mount the Sheet and add the nav entry**

In `src/web/src/app/(staff)/layout.tsx`, add the import and mount alongside the existing Sheets:

```tsx
import { RiderCreateSheet } from "@/features/riders/components/RiderCreateSheet";
```

```tsx
      <ManifestDetailSheet />
      <RiderCreateSheet />
```

In `src/web/src/components/nav-main.tsx`, add `Bike` to the `lucide-react` import and a new entry to `NAV_ITEMS` (after the Manifests entry):

```ts
import { LayoutDashboard, Package, Building2, Wallet, Truck, Bike, type LucideIcon } from "lucide-react";
```

```ts
  { href: "/manifests", label: "Manifests", permission: "manifest.read", icon: Truck },
  { href: "/riders", label: "Riders", permission: "rider.read", icon: Bike },
```

- [ ] **Step 15: Extend the nav-main test**

Add to `src/web/src/components/nav-main.test.tsx`, inside the `describe("NavMain", ...)` block:

```ts
  it("shows the Riders entry when the session has rider.read", async () => {
    renderNavMain(["report.read", "rider.read"]);

    await waitFor(() => expect(screen.getByText("Riders")).toBeInTheDocument());
  });
```

- [ ] **Step 16: Run the full frontend suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds

- [ ] **Step 17: Commit**

```bash
git add src/web/src/features/riders src/web/src/app/\(staff\)/riders src/web/src/app/\(staff\)/layout.tsx src/web/src/components/nav-main.tsx src/web/src/components/nav-main.test.tsx
git commit -m "feat(web): add Riders list page, create Sheet, and sidebar nav entry"
```

---

## Task 7: Vehicles section on the Riders page

**Files:**
- Modify: `src/web/src/features/riders/store/riderPanelStore.ts`
- Modify: `src/web/src/features/riders/store/riderPanelStore.test.ts`
- Create: `src/web/src/features/riders/schema.ts` — already exists (Task 6); extend it
- Create: `src/web/src/features/riders/components/VehicleCreateSheet.tsx`
- Create: `src/web/src/features/riders/components/VehicleCreateSheet.test.tsx`
- Modify: `src/web/src/app/(staff)/riders/page.tsx`
- Modify: `src/web/src/app/(staff)/layout.tsx`

**Interfaces:**
- Consumes: `useVehicles`, `useCreateVehicle`, `useRiders` (Task 5).
- Produces: `useRiderPanelStore` gains `create-vehicle` mode.

- [ ] **Step 1: Extend the store test, watch it fail, then extend the store**

Add to `riderPanelStore.test.ts`:

```ts
  it("openCreateVehicle switches to the create-vehicle panel", () => {
    useRiderPanelStore.getState().openCreateVehicle();
    expect(useRiderPanelStore.getState().panel).toEqual({ mode: "create-vehicle" });
  });
```

Run: `cd src/web && npm test -- riderPanelStore.test.ts` — expect FAIL (`openCreateVehicle` doesn't exist).

Update `riderPanelStore.ts` to:

```ts
import { create } from "zustand";

type RiderPanelState =
  | { mode: "closed" }
  | { mode: "create-rider" }
  | { mode: "create-vehicle" };

interface RiderPanelStore {
  panel: RiderPanelState;
  openCreateRider: () => void;
  openCreateVehicle: () => void;
  close: () => void;
}

export const useRiderPanelStore = create<RiderPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreateRider: () => set({ panel: { mode: "create-rider" } }),
  openCreateVehicle: () => set({ panel: { mode: "create-vehicle" } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

Run: `cd src/web && npm test -- riderPanelStore.test.ts` — expect PASS (4/4).

- [ ] **Step 2: Add the vehicle schema, watch a quick sanity test fail, then add it**

Add to `src/web/src/features/riders/schema.test.ts`:

```ts
import { createVehicleSchema } from "./schema";

describe("createVehicleSchema", () => {
  it("accepts a valid vehicle", () => {
    const result = createVehicleSchema.safeParse({ type: "Motorcycle", plateNumber: "BA 2 KHA 1234" });
    expect(result.success).toBe(true);
  });

  it("rejects a missing plate number", () => {
    const result = createVehicleSchema.safeParse({ type: "Motorcycle", plateNumber: "" });
    expect(result.success).toBe(false);
  });
});
```

Run: `cd src/web && npm test -- riders/schema.test.ts` — expect FAIL (`createVehicleSchema` doesn't exist).

Add to `src/web/src/features/riders/schema.ts`:

```ts
export const createVehicleSchema = z.object({
  type: z.string().min(1, "Vehicle type is required."),
  plateNumber: z.string().min(1, "Plate number is required."),
  capacity: z.number().positive("Capacity must be positive.").optional(),
  riderId: z.string().optional(),
});

export type CreateVehicleFormValues = z.infer<typeof createVehicleSchema>;
```

Run: `cd src/web && npm test -- riders/schema.test.ts` — expect PASS (5/5, including Task 6's 3 rider-schema tests).

- [ ] **Step 3: Write the failing VehicleCreateSheet test**

Create `src/web/src/features/riders/components/VehicleCreateSheet.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { VehicleCreateSheet } from "./VehicleCreateSheet";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <VehicleCreateSheet />
    </QueryClientProvider>
  );
}

describe("VehicleCreateSheet", () => {
  it("is not visible when the store is closed", () => {
    useRiderPanelStore.setState({ panel: { mode: "closed" } });
    renderWithProviders();
    expect(screen.queryByText("New Vehicle")).not.toBeInTheDocument();
  });

  it("is visible with its fields when the store is open", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([]);
    useRiderPanelStore.setState({ panel: { mode: "create-vehicle" } });
    renderWithProviders();

    await waitFor(() => expect(screen.getByText("New Vehicle")).toBeInTheDocument());
    expect(screen.getByLabelText(/plate number/i)).toBeInTheDocument();
  });
});
```

Run: `cd src/web && npm test -- VehicleCreateSheet.test.tsx` — expect FAIL (component doesn't exist).

- [ ] **Step 4: Create VehicleCreateSheet**

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
import { createVehicleSchema, type CreateVehicleFormValues } from "@/features/riders/schema";
import { useCreateVehicle } from "@/features/riders/hooks/useCreateVehicle";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";

export function VehicleCreateSheet() {
  const panel = useRiderPanelStore((s) => s.panel);
  const close = useRiderPanelStore((s) => s.close);
  const isOpen = panel.mode === "create-vehicle";

  const createVehicle = useCreateVehicle();
  const { data: riders } = useRiders();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateVehicleFormValues>({ resolver: zodResolver(createVehicleSchema) });

  async function onSubmit(values: CreateVehicleFormValues) {
    setServerError(null);
    try {
      await createVehicle.mutateAsync(values);
      reset();
      close();
    } catch {
      setServerError("Couldn't create the vehicle. Check the fields and try again.");
    }
  }

  return (
    <Sheet
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) close();
      }}
    >
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>New Vehicle</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="type">Type</Label>
              <Input id="type" placeholder="Motorcycle" {...register("type")} />
              {errors.type && <p className="text-sm text-destructive">{errors.type.message}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="plateNumber">Plate Number</Label>
              <Input id="plateNumber" {...register("plateNumber")} />
              {errors.plateNumber && <p className="text-sm text-destructive">{errors.plateNumber.message}</p>}
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="riderId">Assigned Rider</Label>
            <Controller
              name="riderId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="riderId" aria-label="Assigned rider">
                    <SelectValue placeholder="None" />
                  </SelectTrigger>
                  <SelectContent>
                    {riders?.map((r) => (
                      <SelectItem key={r.id} value={r.id}>{r.licenseNumber}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </div>
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Vehicle"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd src/web && npm test -- VehicleCreateSheet.test.tsx`
Expected: PASS (2/2)

- [ ] **Step 6: Add a Vehicles table + "New Vehicle" button to the Riders page**

In `src/web/src/app/(staff)/riders/page.tsx`, add the vehicles table below the riders `DataTable`:

```tsx
import { useVehicles } from "@/features/riders/hooks/useVehicles";
import type { Vehicle } from "@/features/riders/types";
```

```tsx
  const { data: vehicles, isLoading: vehiclesLoading } = useVehicles();
  const openCreateVehicle = useRiderPanelStore((s) => s.openCreateVehicle);

  const vehicleColumns: ColumnDef<Vehicle>[] = [
    { key: "plateNumber", header: "Plate Number", render: (v) => v.plateNumber },
    { key: "type", header: "Type", render: (v) => v.type },
    { key: "riderId", header: "Assigned Rider", render: (v) => v.riderId ?? "Unassigned" },
  ];
```

Add this block after the riders `<DataTable ... />` (still inside the returned `<div className="space-y-4">`):

```tsx
      <div className="flex items-center justify-between pt-4">
        <h2 className="font-display text-lg font-semibold">Vehicles</h2>
        {canCreate && (
          <Button size="sm" variant="outline" className="gap-1.5" onClick={() => openCreateVehicle()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            New Vehicle
          </Button>
        )}
      </div>
      <DataTable
        columns={vehicleColumns}
        data={
          vehicles && {
            items: vehicles,
            page: 1,
            pageSize: vehicles.length,
            totalCount: vehicles.length,
            totalPages: 1,
          }
        }
        isLoading={vehiclesLoading}
        emptyTitle="No vehicles yet"
        emptyDescription="Add a vehicle to assign it to a rider."
        onPageChange={() => {}}
        rowKey={(v) => v.id}
      />
```

- [ ] **Step 7: Mount the new Sheet**

In `src/web/src/app/(staff)/layout.tsx`:

```tsx
import { VehicleCreateSheet } from "@/features/riders/components/VehicleCreateSheet";
```

```tsx
      <RiderCreateSheet />
      <VehicleCreateSheet />
```

- [ ] **Step 8: Run the full frontend suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds

- [ ] **Step 9: Commit**

```bash
git add src/web/src/features/riders src/web/src/app/\(staff\)/riders/page.tsx src/web/src/app/\(staff\)/layout.tsx
git commit -m "feat(web): add Vehicles section and create Sheet to the Riders page"
```

---

## Task 8: Assign Task flow (AWB lookup + rider picker)

**Files:**
- Modify: `src/web/src/features/riders/store/riderPanelStore.ts`
- Modify: `src/web/src/features/riders/store/riderPanelStore.test.ts`
- Modify: `src/web/src/features/riders/types.ts`
- Create: `src/web/src/features/riders/hooks/useAssignTask.ts`
- Create: `src/web/src/app/api/riders/tasks/route.ts`
- Create: `src/web/src/app/api/riders/tasks/route.test.ts`
- Create: `src/web/src/features/riders/components/AssignTaskSheet.tsx`
- Create: `src/web/src/features/riders/components/AssignTaskSheet.test.tsx`
- Modify: `src/web/src/app/(staff)/riders/page.tsx`
- Modify: `src/web/src/app/(staff)/layout.tsx`

**Interfaces:**
- Consumes: `useShipment(awb)` (existing, `@/features/shipments/hooks/useShipment`), `useRiders` (Task 5).
- Produces: `useRiderPanelStore` gains `assign-task` mode; `AssignDeliveryTaskInput` type; `useAssignTask()` mutation hook.

- [ ] **Step 1: Extend the store, test-first**

Add to `riderPanelStore.test.ts`:

```ts
  it("openAssignTask switches to the assign-task panel", () => {
    useRiderPanelStore.getState().openAssignTask();
    expect(useRiderPanelStore.getState().panel).toEqual({ mode: "assign-task" });
  });
```

Run: `cd src/web && npm test -- riderPanelStore.test.ts` — expect FAIL.

Update `riderPanelStore.ts`:

```ts
import { create } from "zustand";

type RiderPanelState =
  | { mode: "closed" }
  | { mode: "create-rider" }
  | { mode: "create-vehicle" }
  | { mode: "assign-task" };

interface RiderPanelStore {
  panel: RiderPanelState;
  openCreateRider: () => void;
  openCreateVehicle: () => void;
  openAssignTask: () => void;
  close: () => void;
}

export const useRiderPanelStore = create<RiderPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreateRider: () => set({ panel: { mode: "create-rider" } }),
  openCreateVehicle: () => set({ panel: { mode: "create-vehicle" } }),
  openAssignTask: () => set({ panel: { mode: "assign-task" } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

Run: `cd src/web && npm test -- riderPanelStore.test.ts` — expect PASS (5/5).

- [ ] **Step 2: Add the assign-task type and BFF route, test-first**

Add to `src/web/src/features/riders/types.ts`:

```ts
export type DeliveryTaskType = "Pickup" | "Delivery";

export interface AssignDeliveryTaskInput {
  riderId: string;
  shipmentId: string;
  type: DeliveryTaskType;
}
```

Create `src/web/src/app/api/riders/tasks/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("POST /api/riders/tasks", () => {
  it("proxies the assignment request to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "t1" }),
    } as Response);

    const request = new Request("http://localhost/api/riders/tasks", {
      method: "POST",
      body: JSON.stringify({ riderId: "r1", shipmentId: "s1", type: "Pickup" }),
    });
    const response = await POST(request);

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/riders/tasks",
      expect.objectContaining({ method: "POST" })
    );
    expect(response.status).toBe(200);
  });
});
```

Run: `cd src/web && npm test -- riders/tasks/route.test.ts` — expect FAIL (route doesn't exist).

Create `src/web/src/app/api/riders/tasks/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/riders/tasks", {
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

Run: `cd src/web && npm test -- riders/tasks/route.test.ts` — expect PASS (1/1).

Create `src/web/src/features/riders/hooks/useAssignTask.ts`:

```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { AssignDeliveryTaskInput } from "@/features/riders/types";

export function useAssignTask() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: AssignDeliveryTaskInput) =>
      clientFetch<{ id: string }>("/api/riders/tasks", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["shipments"] });
    },
  });
}
```

- [ ] **Step 3: Write the failing AssignTaskSheet test**

Create `src/web/src/features/riders/components/AssignTaskSheet.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import userEvent from "@testing-library/user-event";
import { AssignTaskSheet } from "./AssignTaskSheet";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <AssignTaskSheet />
    </QueryClientProvider>
  );
}

describe("AssignTaskSheet", () => {
  it("is not visible when the store is closed", () => {
    useRiderPanelStore.setState({ panel: { mode: "closed" } });
    renderWithProviders();
    expect(screen.queryByText("Assign Task")).not.toBeInTheDocument();
  });

  it("looks up a shipment by AWB and shows an eligible rider picker", async () => {
    const user = userEvent.setup();
    vi.spyOn(clientFetchModule, "clientFetch").mockImplementation(async (path: string) => {
      if (path.startsWith("/api/shipments/")) {
        return {
          id: "s1", awb: "AWB001", currentStatus: "Created",
          originBranchId: "b1", destinationBranchId: "b2",
          recipientName: "Recipient One", codAmount: 0,
        } as never;
      }
      if (path === "/api/riders") {
        return [{ id: "r1", baseBranchId: "b1", licenseNumber: "LIC-1", roleType: "PickupRider", isActive: true }] as never;
      }
      return [] as never;
    });
    useRiderPanelStore.setState({ panel: { mode: "assign-task" } });
    renderWithProviders();

    await user.type(screen.getByLabelText(/awb/i), "AWB001{enter}");

    await waitFor(() => expect(screen.getByText(/Recipient One/)).toBeInTheDocument());
    expect(screen.getByLabelText(/rider/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 4: Run test to verify it fails**

Run: `cd src/web && npm test -- AssignTaskSheet.test.tsx`
Expected: FAIL — component doesn't exist yet.

- [ ] **Step 5: Create AssignTaskSheet**

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useShipment } from "@/features/shipments/hooks/useShipment";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { useAssignTask } from "@/features/riders/hooks/useAssignTask";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import type { DeliveryTaskType } from "@/features/riders/types";

function inferTaskType(status: string): DeliveryTaskType | null {
  if (status === "Created") return "Pickup";
  if (status === "AtDestinationBranch") return "Delivery";
  return null;
}

export function AssignTaskSheet() {
  const panel = useRiderPanelStore((s) => s.panel);
  const close = useRiderPanelStore((s) => s.close);
  const isOpen = panel.mode === "assign-task";

  const [awbInput, setAwbInput] = useState("");
  const [lookupAwb, setLookupAwb] = useState("");
  const [riderId, setRiderId] = useState<string | undefined>(undefined);
  const [serverError, setServerError] = useState<string | null>(null);

  const { data: shipment, isFetching, isError } = useShipment(lookupAwb);
  const { data: riders } = useRiders();
  const assignTask = useAssignTask();

  const taskType = shipment ? inferTaskType(shipment.currentStatus) : null;
  const relevantBranchId = taskType === "Pickup" ? shipment?.originBranchId : shipment?.destinationBranchId;
  const eligibleRiders = riders?.filter((r) => r.isActive && r.baseBranchId === relevantBranchId);

  function reset() {
    setAwbInput("");
    setLookupAwb("");
    setRiderId(undefined);
    setServerError(null);
  }

  async function handleAssign() {
    if (!shipment || !taskType || !riderId) return;
    setServerError(null);
    try {
      await assignTask.mutateAsync({ riderId, shipmentId: shipment.id, type: taskType });
      reset();
      close();
    } catch {
      setServerError("Couldn't assign this task. Check the fields and try again.");
    }
  }

  return (
    <Sheet
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) {
          reset();
          close();
        }
      }}
    >
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Assign Task</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-4">
          <div className="space-y-2">
            <Label htmlFor="assign-awb">AWB</Label>
            <Input
              id="assign-awb"
              aria-label="AWB"
              value={awbInput}
              onChange={(e) => setAwbInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  setLookupAwb(awbInput.trim());
                }
              }}
              placeholder="Scan or type an AWB, then press Enter"
            />
            {isFetching && <Spinner className="h-4 w-4" />}
            {isError && <p className="text-sm text-destructive">No shipment found for that AWB.</p>}
          </div>

          {shipment && (
            <div className="rounded-md border border-border p-3 text-sm">
              <p className="font-medium text-foreground">{shipment.recipientName}</p>
              <p className="text-muted-foreground">AWB {shipment.awb} &middot; {shipment.currentStatus}</p>
              {!taskType && (
                <p className="mt-2 text-destructive">
                  Shipment must be Created or AtDestinationBranch to assign a task, is currently {shipment.currentStatus}.
                </p>
              )}
            </div>
          )}

          {shipment && taskType && (
            <div className="space-y-2">
              <Label htmlFor="assign-rider">Rider</Label>
              <Select value={riderId} onValueChange={setRiderId}>
                <SelectTrigger id="assign-rider" aria-label="Rider">
                  <SelectValue placeholder="Select rider" />
                </SelectTrigger>
                <SelectContent>
                  {eligibleRiders?.map((r) => (
                    <SelectItem key={r.id} value={r.id}>{r.licenseNumber}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}

          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="button" disabled={!shipment || !taskType || !riderId || assignTask.isPending} onClick={handleAssign}>
            {assignTask.isPending ? <Spinner className="h-4 w-4" /> : `Assign ${taskType ?? "Task"}`}
          </Button>
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `cd src/web && npm test -- AssignTaskSheet.test.tsx`
Expected: PASS (2/2)

- [ ] **Step 7: Wire the "Assign Task" button into the Riders page and mount the Sheet**

In `src/web/src/app/(staff)/riders/page.tsx`, add next to the "New Rider" button:

```tsx
  const canAssign = usePermission("delivery.assign");
  const openAssignTask = useRiderPanelStore((s) => s.openAssignTask);
```

```tsx
        {canAssign && (
          <Button size="sm" variant="outline" className="gap-1.5" onClick={() => openAssignTask()}>
            Assign Task
          </Button>
        )}
```

In `src/web/src/app/(staff)/layout.tsx`:

```tsx
import { AssignTaskSheet } from "@/features/riders/components/AssignTaskSheet";
```

```tsx
      <VehicleCreateSheet />
      <AssignTaskSheet />
```

- [ ] **Step 8: Run the full frontend suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds

- [ ] **Step 9: Commit**

```bash
git add src/web/src/features/riders src/web/src/app/api/riders/tasks src/web/src/app/\(staff\)/riders/page.tsx src/web/src/app/\(staff\)/layout.tsx
git commit -m "feat(web): add Assign Task flow via AWB lookup and rider picker"
```

---

## Task 9: My Deliveries page (rider self-service)

**Files:**
- Modify: `src/web/src/features/riders/types.ts`
- Create: `src/web/src/app/api/riders/me/route.ts`
- Create: `src/web/src/app/api/riders/me/route.test.ts`
- Create: `src/web/src/app/api/riders/me/tasks/today/route.ts`
- Create: `src/web/src/app/api/riders/me/tasks/today/route.test.ts`
- Create: `src/web/src/features/riders/hooks/useMyRider.ts`
- Create: `src/web/src/features/riders/hooks/useMyTasksToday.ts`
- Create: `src/web/src/app/(staff)/my-deliveries/page.tsx`
- Modify: `src/web/src/components/nav-main.tsx`
- Modify: `src/web/src/components/nav-main.test.tsx`

**Interfaces:**
- Produces: `MyTask`, `MyTasksToday` types; `useMyRider()`, `useMyTasksToday()` hooks — consumed by Tasks 10–13.

- [ ] **Step 1: Write the failing BFF route tests**

Create `src/web/src/app/api/riders/me/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/riders/me", () => {
  it("proxies to the backend and returns the response", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "r1" }),
    } as Response);

    const response = await GET();

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/me");
    expect(response.status).toBe(200);
  });

  it("returns 404 when the backend returns 404", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: false,
      status: 404,
      json: async () => null,
    } as Response);

    const response = await GET();

    expect(response.status).toBe(404);
  });
});
```

Create `src/web/src/app/api/riders/me/tasks/today/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/riders/me/tasks/today", () => {
  it("proxies to the backend and returns the response", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ deliveryRunId: null, tasks: [] }),
    } as Response);

    const response = await GET();

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/me/tasks/today");
    expect(response.status).toBe(200);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd src/web && npm test -- riders/me/route.test.ts riders/me/tasks/today/route.test.ts`
Expected: FAIL — neither route exists yet.

- [ ] **Step 3: Create both BFF routes**

Create `src/web/src/app/api/riders/me/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/riders/me");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/app/api/riders/me/tasks/today/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/riders/me/tasks/today");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd src/web && npm test -- riders/me/route.test.ts riders/me/tasks/today/route.test.ts`
Expected: PASS (3/3)

- [ ] **Step 5: Add the types and hooks**

Add to `src/web/src/features/riders/types.ts`:

```ts
export interface MyTask {
  taskId: string;
  shipmentId: string;
  type: DeliveryTaskType;
  sequence: number;
  awb: string;
  recipientName: string;
  recipientPhone: string;
  recipientAddressLine: string;
  recipientWardNumber: number;
  codAmount: number;
}

export interface MyTasksToday {
  deliveryRunId: string | null;
  tasks: MyTask[];
}
```

Create `src/web/src/features/riders/hooks/useMyRider.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Rider } from "@/features/riders/types";

export function useMyRider() {
  return useQuery({
    queryKey: ["riders", "me"],
    queryFn: () => clientFetch<Rider>("/api/riders/me"),
    retry: false,
  });
}
```

Create `src/web/src/features/riders/hooks/useMyTasksToday.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MyTasksToday } from "@/features/riders/types";

export function useMyTasksToday(enabled: boolean) {
  return useQuery({
    queryKey: ["riders", "me", "tasks", "today"],
    queryFn: () => clientFetch<MyTasksToday>("/api/riders/me/tasks/today"),
    enabled,
  });
}
```

- [ ] **Step 6: Create the My Deliveries page**

```tsx
"use client";

import { EmptyState } from "@/components/states/EmptyState";
import { ErrorState } from "@/components/states/ErrorState";
import { Spinner } from "@/components/ui/spinner";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useMyRider } from "@/features/riders/hooks/useMyRider";
import { useMyTasksToday } from "@/features/riders/hooks/useMyTasksToday";
import type { MyTask } from "@/features/riders/types";

function TaskCard({ task }: { task: MyTask }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between text-base">
          <span>{task.type} &middot; {task.awb}</span>
          {task.codAmount > 0 && <Badge variant="outline">COD Rs. {task.codAmount}</Badge>}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-1 text-sm">
        <p className="font-medium text-foreground">{task.recipientName}</p>
        <p className="text-muted-foreground">{task.recipientPhone}</p>
        <p className="text-muted-foreground">{task.recipientAddressLine}, Ward {task.recipientWardNumber}</p>
      </CardContent>
    </Card>
  );
}

export default function MyDeliveriesPage() {
  const { data: rider, isLoading: riderLoading, isError: riderError } = useMyRider();
  const { data: tasksToday, isLoading: tasksLoading, isError: tasksError, refetch } = useMyTasksToday(!!rider);

  if (riderLoading || (rider && tasksLoading)) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (riderError) {
    return (
      <EmptyState
        title="No rider profile"
        description="This account isn't linked to a rider record, so there's nothing to show here."
      />
    );
  }

  if (tasksError) {
    return (
      <ErrorState
        title="Couldn't load today's deliveries"
        description="Something went wrong fetching your assigned tasks."
        onRetry={() => refetch()}
      />
    );
  }

  const tasks = tasksToday?.tasks ?? [];

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">My Deliveries</h1>
      {tasks.length === 0 ? (
        <EmptyState
          title="No deliveries assigned today"
          description="Pickup and delivery tasks assigned to you today will show up here."
        />
      ) : (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          {tasks.map((task) => (
            <TaskCard key={task.taskId} task={task} />
          ))}
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 7: Add the sidebar nav entry**

In `src/web/src/components/nav-main.tsx`, add `ClipboardCheck` to the `lucide-react` import and a new entry after Riders:

```ts
import { LayoutDashboard, Package, Building2, Wallet, Truck, Bike, ClipboardCheck, type LucideIcon } from "lucide-react";
```

```ts
  { href: "/riders", label: "Riders", permission: "rider.read", icon: Bike },
  { href: "/my-deliveries", label: "My Deliveries", permission: "delivery.attempt", icon: ClipboardCheck },
```

Add to `src/web/src/components/nav-main.test.tsx`:

```ts
  it("shows My Deliveries when the session has delivery.attempt", async () => {
    renderNavMain(["report.read", "delivery.attempt"]);

    await waitFor(() => expect(screen.getByText("My Deliveries")).toBeInTheDocument());
  });
```

- [ ] **Step 8: Run the full frontend suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds

- [ ] **Step 9: Commit**

```bash
git add src/web/src/features/riders src/web/src/app/api/riders/me src/web/src/app/\(staff\)/my-deliveries src/web/src/components/nav-main.tsx src/web/src/components/nav-main.test.tsx
git commit -m "feat(web): add My Deliveries page for rider self-service"
```

---

## Task 10: Pickup task "Scan pickup" action

**Files:**
- Create: `src/web/src/app/api/riders/[id]/scan/pickup/route.ts`
- Create: `src/web/src/app/api/riders/[id]/scan/pickup/route.test.ts`
- Create: `src/web/src/features/riders/hooks/useRiderPickupScan.ts`
- Create: `src/web/src/features/riders/components/PickupScanControl.tsx`
- Create: `src/web/src/features/riders/components/PickupScanControl.test.tsx`
- Modify: `src/web/src/app/(staff)/my-deliveries/page.tsx`

**Interfaces:**
- Consumes: `useMyRider` (Task 9) for the rider id the `{id}`-scoped endpoint needs.
- Produces: `useRiderPickupScan(riderId)` mutation hook; `PickupScanControl` component rendered on `Pickup`-type task cards.

- [ ] **Step 1: Write the failing BFF route test**

Create `src/web/src/app/api/riders/[id]/scan/pickup/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("POST /api/riders/[id]/scan/pickup", () => {
  it("proxies the scan request to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "s1", currentStatus: "PickedUp" }),
    } as Response);

    const request = new Request("http://localhost/api/riders/r1/scan/pickup", {
      method: "POST",
      body: JSON.stringify({ awb: "AWB001" }),
    });
    const response = await POST(request, { params: { id: "r1" } });

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/riders/r1/scan/pickup",
      expect.objectContaining({ method: "POST" })
    );
    expect(response.status).toBe(200);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npm test -- riders/\[id\]/scan/pickup/route.test.ts`
Expected: FAIL — route doesn't exist yet.

- [ ] **Step 3: Create the BFF route**

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/riders/${params.id}/scan/pickup`, {
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

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/web && npm test -- riders/\[id\]/scan/pickup/route.test.ts`
Expected: PASS (1/1)

- [ ] **Step 5: Add the hook**

Create `src/web/src/features/riders/hooks/useRiderPickupScan.ts`:

```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useRiderPickupScan(riderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (awb: string) =>
      clientFetch<{ id: string; currentStatus: string }>(`/api/riders/${riderId}/scan/pickup`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ awb }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["riders", "me", "tasks", "today"] });
    },
  });
}
```

- [ ] **Step 6: Write the failing PickupScanControl test**

Create `src/web/src/features/riders/components/PickupScanControl.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PickupScanControl } from "./PickupScanControl";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders(riderId: string, awb: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <PickupScanControl riderId={riderId} awb={awb} />
    </QueryClientProvider>
  );
}

describe("PickupScanControl", () => {
  it("submits the AWB and shows a confirmation on success", async () => {
    const user = userEvent.setup();
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ id: "s1", currentStatus: "PickedUp" });
    renderWithProviders("r1", "AWB001");

    await user.click(screen.getByRole("button", { name: /scan pickup/i }));

    await waitFor(() => expect(screen.getByText(/picked up/i)).toBeInTheDocument());
    expect(clientFetchModule.clientFetch).toHaveBeenCalledWith(
      "/api/riders/r1/scan/pickup",
      expect.objectContaining({ method: "POST" })
    );
  });
});
```

- [ ] **Step 7: Run test to verify it fails**

Run: `cd src/web && npm test -- PickupScanControl.test.tsx`
Expected: FAIL — component doesn't exist yet.

- [ ] **Step 8: Create PickupScanControl**

```tsx
"use client";

import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useRiderPickupScan } from "@/features/riders/hooks/useRiderPickupScan";

export function PickupScanControl({ riderId, awb }: { riderId: string; awb: string }) {
  const scanPickup = useRiderPickupScan(riderId);

  if (scanPickup.isSuccess) {
    return <p className="text-sm text-muted-foreground">Picked up.</p>;
  }

  return (
    <div className="space-y-1">
      <Button type="button" size="sm" disabled={scanPickup.isPending} onClick={() => scanPickup.mutate(awb)}>
        {scanPickup.isPending ? <Spinner className="h-4 w-4" /> : "Scan Pickup"}
      </Button>
      {scanPickup.isError && <p className="text-sm text-destructive">Couldn't confirm pickup. Try again.</p>}
    </div>
  );
}
```

- [ ] **Step 9: Run test to verify it passes**

Run: `cd src/web && npm test -- PickupScanControl.test.tsx`
Expected: PASS (1/1)

- [ ] **Step 10: Wire PickupScanControl into My Deliveries' task cards**

In `src/web/src/app/(staff)/my-deliveries/page.tsx`, add the import:

```tsx
import { PickupScanControl } from "@/features/riders/components/PickupScanControl";
import { useMyRider } from "@/features/riders/hooks/useMyRider";
```

Update `TaskCard` to accept the rider id and render the control for `Pickup` tasks:

```tsx
function TaskCard({ task, riderId }: { task: MyTask; riderId: string }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between text-base">
          <span>{task.type} &middot; {task.awb}</span>
          {task.codAmount > 0 && <Badge variant="outline">COD Rs. {task.codAmount}</Badge>}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-1 text-sm">
        <p className="font-medium text-foreground">{task.recipientName}</p>
        <p className="text-muted-foreground">{task.recipientPhone}</p>
        <p className="text-muted-foreground">{task.recipientAddressLine}, Ward {task.recipientWardNumber}</p>
        <div className="pt-2">
          {task.type === "Pickup" && <PickupScanControl riderId={riderId} awb={task.awb} />}
        </div>
      </CardContent>
    </Card>
  );
}
```

And update the map call in `MyDeliveriesPage` to pass `riderId`:

```tsx
          {tasks.map((task) => (
            <TaskCard key={task.taskId} task={task} riderId={rider!.id} />
          ))}
```

- [ ] **Step 11: Run the full frontend suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds

- [ ] **Step 12: Commit**

```bash
git add src/web/src/app/api/riders/\[id\]/scan src/web/src/features/riders/hooks/useRiderPickupScan.ts src/web/src/features/riders/components/PickupScanControl.tsx src/web/src/features/riders/components/PickupScanControl.test.tsx src/web/src/app/\(staff\)/my-deliveries/page.tsx
git commit -m "feat(web): add pickup-scan action to My Deliveries pickup tasks"
```

---

## Task 11: Record Attempt Sheet shell (success/failure branching)

**Files:**
- Create: `src/web/src/components/ui/textarea.tsx`
- Create: `src/web/src/features/riders/store/myDeliveriesPanelStore.ts`
- Create: `src/web/src/features/riders/store/myDeliveriesPanelStore.test.ts`
- Create: `src/web/src/features/riders/components/RecordAttemptSheet.tsx`
- Create: `src/web/src/features/riders/components/RecordAttemptSheet.test.tsx`
- Modify: `src/web/src/app/(staff)/my-deliveries/page.tsx`
- Modify: `src/web/src/app/(staff)/layout.tsx`

**Interfaces:**
- Produces: `useMyDeliveriesPanelStore` with modes `closed | { mode: "record-attempt"; task: MyTask }`; `RecordAttemptSheet` renders a success/failure toggle only in this task — Task 12 fills in the failure branch's submission, Task 13 fills in the success/POD branch's submission.

- [ ] **Step 1: Add the Textarea primitive**

No test needed — this is a direct visual port of `Input`'s styling to a `<textarea>`, matching how `ReauthDialog` (Phase F1) was hand-authored when the shadcn CLI couldn't be used non-interactively.

Create `src/web/src/components/ui/textarea.tsx`:

```tsx
import * as React from "react"

import { cn } from "@/lib/utils"

const Textarea = React.forwardRef<HTMLTextAreaElement, React.ComponentProps<"textarea">>(
  ({ className, ...props }, ref) => {
    return (
      <textarea
        className={cn(
          "flex min-h-[80px] w-full rounded-md border border-input bg-transparent px-3 py-2 text-base shadow-sm transition-colors placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 md:text-sm",
          className
        )}
        ref={ref}
        {...props}
      />
    )
  }
)
Textarea.displayName = "Textarea"

export { Textarea }
```

- [ ] **Step 2: Write the failing store test**

Create `src/web/src/features/riders/store/myDeliveriesPanelStore.test.ts`:

```ts
import { describe, expect, it, beforeEach } from "vitest";
import { useMyDeliveriesPanelStore } from "./myDeliveriesPanelStore";
import type { MyTask } from "@/features/riders/types";

const task: MyTask = {
  taskId: "t1", shipmentId: "s1", type: "Delivery", sequence: 1,
  awb: "AWB001", recipientName: "Recipient One", recipientPhone: "9800000000",
  recipientAddressLine: "Address", recipientWardNumber: 1, codAmount: 0,
};

describe("useMyDeliveriesPanelStore", () => {
  beforeEach(() => {
    useMyDeliveriesPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useMyDeliveriesPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openRecordAttempt switches to the record-attempt panel with the given task", () => {
    useMyDeliveriesPanelStore.getState().openRecordAttempt(task);
    expect(useMyDeliveriesPanelStore.getState().panel).toEqual({ mode: "record-attempt", task });
  });

  it("close returns to closed", () => {
    useMyDeliveriesPanelStore.getState().openRecordAttempt(task);
    useMyDeliveriesPanelStore.getState().close();
    expect(useMyDeliveriesPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

- [ ] **Step 3: Run test to verify it fails**

Run: `cd src/web && npm test -- myDeliveriesPanelStore.test.ts`
Expected: FAIL — module doesn't exist yet.

- [ ] **Step 4: Create the store**

```ts
import { create } from "zustand";
import type { MyTask } from "@/features/riders/types";

type MyDeliveriesPanelState =
  | { mode: "closed" }
  | { mode: "record-attempt"; task: MyTask };

interface MyDeliveriesPanelStore {
  panel: MyDeliveriesPanelState;
  openRecordAttempt: (task: MyTask) => void;
  close: () => void;
}

export const useMyDeliveriesPanelStore = create<MyDeliveriesPanelStore>((set) => ({
  panel: { mode: "closed" },
  openRecordAttempt: (task) => set({ panel: { mode: "record-attempt", task } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd src/web && npm test -- myDeliveriesPanelStore.test.ts`
Expected: PASS (3/3)

- [ ] **Step 5b: Add the DeliveryAttemptReason type**

Add to `src/web/src/features/riders/types.ts`:

```ts
export type DeliveryAttemptReason =
  | "CustomerUnavailable" | "PhoneUnreachable" | "WrongAddress" | "IncompleteAddress"
  | "CustomerRefused" | "CustomerRequestedReschedule" | "CashUnavailable" | "DamagedParcel" | "Other";
```

- [ ] **Step 6: Write the failing RecordAttemptSheet shell test**

Create `src/web/src/features/riders/components/RecordAttemptSheet.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RecordAttemptSheet } from "./RecordAttemptSheet";
import { useMyDeliveriesPanelStore } from "@/features/riders/store/myDeliveriesPanelStore";
import type { MyTask } from "@/features/riders/types";

const task: MyTask = {
  taskId: "t1", shipmentId: "s1", type: "Delivery", sequence: 1,
  awb: "AWB001", recipientName: "Recipient One", recipientPhone: "9800000000",
  recipientAddressLine: "Address", recipientWardNumber: 1, codAmount: 0,
};

function renderWithProviders(riderId = "r1") {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <RecordAttemptSheet riderId={riderId} />
    </QueryClientProvider>
  );
}

describe("RecordAttemptSheet", () => {
  it("is not visible when the store is closed", () => {
    useMyDeliveriesPanelStore.setState({ panel: { mode: "closed" } });
    renderWithProviders();
    expect(screen.queryByText("Record Delivery Attempt")).not.toBeInTheDocument();
  });

  it("defaults to the success branch and shows POD fields", () => {
    useMyDeliveriesPanelStore.setState({ panel: { mode: "record-attempt", task } });
    renderWithProviders();

    expect(screen.getByText("Record Delivery Attempt")).toBeInTheDocument();
    expect(screen.getByLabelText(/recipient name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/customer verified via otp/i)).toBeInTheDocument();
  });

  it("switches to the failure branch and shows the reason select", async () => {
    const user = userEvent.setup();
    useMyDeliveriesPanelStore.setState({ panel: { mode: "record-attempt", task } });
    renderWithProviders();

    await user.click(screen.getByRole("button", { name: /failed/i }));

    expect(screen.getByLabelText(/reason/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/recipient name/i)).not.toBeInTheDocument();
  });

  it("shows the COD field only when the task has a COD amount", () => {
    useMyDeliveriesPanelStore.setState({
      panel: { mode: "record-attempt", task: { ...task, codAmount: 500 } },
    });
    renderWithProviders();

    expect(screen.getByLabelText(/collected cod amount/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 7: Run test to verify it fails**

Run: `cd src/web && npm test -- RecordAttemptSheet.test.tsx`
Expected: FAIL — component doesn't exist yet.

- [ ] **Step 8: Create the RecordAttemptSheet shell**

This step wires only the success/failure toggle and its field sets — Task 12 replaces the failure branch's `TODO` submit with the real mutation, Task 13 replaces the success branch's.

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Textarea } from "@/components/ui/textarea";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { useMyDeliveriesPanelStore } from "@/features/riders/store/myDeliveriesPanelStore";
import type { DeliveryAttemptReason } from "@/features/riders/types";

const FAILURE_REASONS: DeliveryAttemptReason[] = [
  "CustomerUnavailable", "PhoneUnreachable", "WrongAddress", "IncompleteAddress",
  "CustomerRefused", "CustomerRequestedReschedule", "CashUnavailable", "DamagedParcel", "Other",
];

export function RecordAttemptSheet({ riderId }: { riderId: string }) {
  const panel = useMyDeliveriesPanelStore((s) => s.panel);
  const close = useMyDeliveriesPanelStore((s) => s.close);
  const isOpen = panel.mode === "record-attempt";
  const task = panel.mode === "record-attempt" ? panel.task : null;

  const [outcome, setOutcome] = useState<"success" | "failure">("success");
  const [reason, setReason] = useState<DeliveryAttemptReason | undefined>(undefined);
  const [notes, setNotes] = useState("");
  const [recipientName, setRecipientName] = useState("");
  const [otpVerified, setOtpVerified] = useState(false);
  const [collectedCodAmount, setCollectedCodAmount] = useState("");

  function reset() {
    setOutcome("success");
    setReason(undefined);
    setNotes("");
    setRecipientName("");
    setOtpVerified(false);
    setCollectedCodAmount("");
  }

  return (
    <Sheet
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) {
          reset();
          close();
        }
      }}
    >
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Record Delivery Attempt</SheetTitle>
        </SheetHeader>
        {task && (
          <div className="mt-6 space-y-4">
            <div className="flex gap-2">
              <Button
                type="button"
                variant={outcome === "success" ? "default" : "outline"}
                size="sm"
                onClick={() => setOutcome("success")}
              >
                Delivered
              </Button>
              <Button
                type="button"
                variant={outcome === "failure" ? "default" : "outline"}
                size="sm"
                onClick={() => setOutcome("failure")}
              >
                Failed
              </Button>
            </div>

            {outcome === "failure" && (
              <div className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="attempt-reason">Reason</Label>
                  <Select value={reason} onValueChange={(v) => setReason(v as DeliveryAttemptReason)}>
                    <SelectTrigger id="attempt-reason" aria-label="Reason">
                      <SelectValue placeholder="Select reason" />
                    </SelectTrigger>
                    <SelectContent>
                      {FAILURE_REASONS.map((r) => (
                        <SelectItem key={r} value={r}>{r}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="attempt-notes">Notes</Label>
                  <Textarea id="attempt-notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
                </div>
              </div>
            )}

            {outcome === "success" && (
              <div className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="recipient-name">Recipient Name</Label>
                  <Input id="recipient-name" value={recipientName} onChange={(e) => setRecipientName(e.target.value)} />
                </div>
                <div className="flex items-center gap-2">
                  <Checkbox id="otp-verified" checked={otpVerified} onCheckedChange={(v) => setOtpVerified(v === true)} />
                  <Label htmlFor="otp-verified">Customer verified via OTP</Label>
                </div>
                {task.codAmount > 0 && (
                  <div className="space-y-2">
                    <Label htmlFor="collected-cod">Collected COD Amount</Label>
                    <Input
                      id="collected-cod"
                      type="number"
                      value={collectedCodAmount}
                      onChange={(e) => setCollectedCodAmount(e.target.value)}
                      placeholder={String(task.codAmount)}
                    />
                  </div>
                )}
              </div>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 9: Run test to verify it passes**

Run: `cd src/web && npm test -- RecordAttemptSheet.test.tsx`
Expected: PASS (4/4)

- [ ] **Step 10: Wire a "Record attempt" button into Delivery task cards and mount the Sheet**

In `src/web/src/app/(staff)/my-deliveries/page.tsx`, add the import and store hook:

```tsx
import { Button } from "@/components/ui/button";
import { useMyDeliveriesPanelStore } from "@/features/riders/store/myDeliveriesPanelStore";
```

Update `TaskCard`'s body to open the sheet for `Delivery` tasks:

```tsx
        <div className="pt-2">
          {task.type === "Pickup" && <PickupScanControl riderId={riderId} awb={task.awb} />}
          {task.type === "Delivery" && (
            <Button type="button" size="sm" onClick={() => useMyDeliveriesPanelStore.getState().openRecordAttempt(task)}>
              Record Attempt
            </Button>
          )}
        </div>
```

In `src/web/src/app/(staff)/layout.tsx`:

```tsx
import { RecordAttemptSheet } from "@/features/riders/components/RecordAttemptSheet";
```

Mounting `RecordAttemptSheet` needs the current user's own rider id, which only `/my-deliveries` has loaded — so mount it directly in the page instead of the shared layout (the only Sheet in this codebase that isn't layout-mounted, because every other Sheet's data comes from route params or a list already on the page, while this one needs the rider's own id resolved via `useMyRider`). In `src/web/src/app/(staff)/my-deliveries/page.tsx`, render it inside `MyDeliveriesPage`'s returned JSX, after the task grid:

```tsx
      {rider && <RecordAttemptSheet riderId={rider.id} />}
```

- [ ] **Step 11: Run the full frontend suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds

- [ ] **Step 12: Commit**

```bash
git add src/web/src/components/ui/textarea.tsx src/web/src/features/riders/store/myDeliveriesPanelStore.ts src/web/src/features/riders/store/myDeliveriesPanelStore.test.ts src/web/src/features/riders/components/RecordAttemptSheet.tsx src/web/src/features/riders/components/RecordAttemptSheet.test.tsx src/web/src/app/\(staff\)/my-deliveries/page.tsx
git commit -m "feat(web): add Record Attempt Sheet shell with success/failure branching"
```

---

## Task 12: Failure branch submission (record failed attempt)

**Files:**
- Modify: `src/web/src/features/riders/types.ts`
- Create: `src/web/src/app/api/riders/[id]/delivery-attempts/fail/route.ts`
- Create: `src/web/src/app/api/riders/[id]/delivery-attempts/fail/route.test.ts`
- Create: `src/web/src/features/riders/hooks/useRecordFailedAttempt.ts`
- Modify: `src/web/src/features/riders/components/RecordAttemptSheet.tsx`
- Modify: `src/web/src/features/riders/components/RecordAttemptSheet.test.tsx`

**Interfaces:**
- Produces: `RecordFailedAttemptInput` type; `useRecordFailedAttempt(riderId)` mutation hook, wired to the failure branch's submit button added in this task.

- [ ] **Step 1: Add the type and write the failing BFF route test**

Add to `src/web/src/features/riders/types.ts`:

```ts
export interface RecordFailedAttemptInput {
  shipmentId: string;
  reason: DeliveryAttemptReason;
  notes?: string;
  latitude?: number;
  longitude?: number;
  customerContacted: boolean;
  nextAction?: string;
}

export interface DeliveryAttemptResult {
  id: string;
  shipmentId: string;
  riderId: string;
  attemptNumber: number;
  succeeded: boolean;
  reason: DeliveryAttemptReason | null;
  nextAction: string | null;
}
```

Create `src/web/src/app/api/riders/[id]/delivery-attempts/fail/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("POST /api/riders/[id]/delivery-attempts/fail", () => {
  it("proxies the failed-attempt request to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "a1", succeeded: false }),
    } as Response);

    const request = new Request("http://localhost/api/riders/r1/delivery-attempts/fail", {
      method: "POST",
      body: JSON.stringify({ shipmentId: "s1", reason: "CustomerUnavailable", customerContacted: true }),
    });
    const response = await POST(request, { params: { id: "r1" } });

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/riders/r1/delivery-attempts/fail",
      expect.objectContaining({ method: "POST" })
    );
    expect(response.status).toBe(200);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npm test -- delivery-attempts/fail/route.test.ts`
Expected: FAIL — route doesn't exist yet.

- [ ] **Step 3: Create the BFF route and hook**

Create `src/web/src/app/api/riders/[id]/delivery-attempts/fail/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/riders/${params.id}/delivery-attempts/fail`, {
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

Create `src/web/src/features/riders/hooks/useRecordFailedAttempt.ts`:

```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { DeliveryAttemptResult, RecordFailedAttemptInput } from "@/features/riders/types";

export function useRecordFailedAttempt(riderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: RecordFailedAttemptInput) =>
      clientFetch<DeliveryAttemptResult>(`/api/riders/${riderId}/delivery-attempts/fail`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["riders", "me", "tasks", "today"] });
    },
  });
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/web && npm test -- delivery-attempts/fail/route.test.ts`
Expected: PASS (1/1)

- [ ] **Step 5: Extend the RecordAttemptSheet test for the failure submit**

Add to `RecordAttemptSheet.test.tsx`:

```tsx
  it("submits a failed attempt with the selected reason and closes on success", async () => {
    const user = userEvent.setup();
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ id: "a1", succeeded: false });
    useMyDeliveriesPanelStore.setState({ panel: { mode: "record-attempt", task } });
    renderWithProviders();

    await user.click(screen.getByRole("button", { name: /failed/i }));
    await user.selectOptions(screen.getByLabelText(/reason/i), "CustomerUnavailable");
    await user.click(screen.getByRole("button", { name: /submit/i }));

    await waitFor(() => expect(clientFetchModule.clientFetch).toHaveBeenCalledWith(
      "/api/riders/r1/delivery-attempts/fail",
      expect.objectContaining({ method: "POST" })
    ));
  });
```

Add the needed imports at the top of the test file:

```tsx
import { waitFor } from "@testing-library/react";
import * as clientFetchModule from "@/lib/api/clientFetch";
```

(Add these alongside the existing `render, screen` import from `@testing-library/react` — combine into one import statement rather than two separate ones for the same module.)

- [ ] **Step 6: Run test to verify it fails**

Run: `cd src/web && npm test -- RecordAttemptSheet.test.tsx`
Expected: FAIL — no submit button wired to the failure mutation yet.

- [ ] **Step 7: Wire the failure branch's submit button**

In `src/web/src/features/riders/components/RecordAttemptSheet.tsx`, add the import and hook:

```tsx
import { useRecordFailedAttempt } from "@/features/riders/hooks/useRecordFailedAttempt";
```

```tsx
  const recordFailedAttempt = useRecordFailedAttempt(riderId);
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleSubmitFailure() {
    if (!task || !reason) return;
    setServerError(null);
    try {
      await recordFailedAttempt.mutateAsync({
        shipmentId: task.shipmentId,
        reason,
        notes: notes || undefined,
        customerContacted: true,
      });
      reset();
      close();
    } catch {
      setServerError("Couldn't record this attempt. Check the fields and try again.");
    }
  }
```

Add the submit button and error message at the end of the `outcome === "failure"` block:

```tsx
                <div className="space-y-2">
                  <Label htmlFor="attempt-notes">Notes</Label>
                  <Textarea id="attempt-notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
                </div>
                {serverError && <p className="text-sm text-destructive">{serverError}</p>}
                <Button type="button" disabled={!reason || recordFailedAttempt.isPending} onClick={handleSubmitFailure}>
                  {recordFailedAttempt.isPending ? <Spinner className="h-4 w-4" /> : "Submit"}
                </Button>
```

Add the `Spinner` import alongside the existing `Button`/`Input` imports:

```tsx
import { Spinner } from "@/components/ui/spinner";
```

- [ ] **Step 8: Run test to verify it passes**

Run: `cd src/web && npm test -- RecordAttemptSheet.test.tsx`
Expected: PASS (5/5)

- [ ] **Step 9: Run the full frontend suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds

- [ ] **Step 10: Commit**

```bash
git add src/web/src/features/riders src/web/src/app/api/riders/\[id\]/delivery-attempts/fail
git commit -m "feat(web): wire failed-delivery-attempt submission into Record Attempt Sheet"
```

---

## Task 13: POD capture (signature, photo, OTP, GPS, COD) and completion submission

**Files:**
- Modify: `src/web/src/features/riders/types.ts`
- Create: `src/web/src/app/api/riders/[id]/pod-uploads/route.ts`
- Create: `src/web/src/app/api/riders/[id]/pod-uploads/route.test.ts`
- Create: `src/web/src/app/api/riders/[id]/delivery-attempts/complete/route.ts`
- Create: `src/web/src/app/api/riders/[id]/delivery-attempts/complete/route.test.ts`
- Create: `src/web/src/features/riders/hooks/usePodUpload.ts`
- Create: `src/web/src/features/riders/hooks/useCompleteDelivery.ts`
- Create: `src/web/src/features/riders/components/SignaturePad.tsx`
- Create: `src/web/src/features/riders/components/SignaturePad.test.tsx`
- Modify: `src/web/src/features/riders/components/RecordAttemptSheet.tsx`
- Modify: `src/web/src/features/riders/components/RecordAttemptSheet.test.tsx`

**Interfaces:**
- Produces: `CompleteDeliveryInput`, `UploadPodFileResult` types; `usePodUpload(riderId)`, `useCompleteDelivery(riderId)` hooks; `SignaturePad` component — this is the phase's final task, closing the loop back to `GetMyTasksTodayAsync` (Task 4) via query invalidation.

- [ ] **Step 1: Add the types**

Add to `src/web/src/features/riders/types.ts`:

```ts
export interface UploadPodFileResult {
  storageKey: string;
}

export interface CompleteDeliveryInput {
  shipmentId: string;
  recipientName: string;
  otpVerified: boolean;
  signatureStorageKey?: string | null;
  photoStorageKey?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  collectedCodAmount?: number | null;
}
```

- [ ] **Step 2: Write the failing BFF route tests**

Create `src/web/src/app/api/riders/[id]/pod-uploads/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("POST /api/riders/[id]/pod-uploads", () => {
  it("forwards the raw body and content-type, with kind as a query param", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ storageKey: "pod/Signature/r1/abc" }),
    } as Response);

    const request = new Request("http://localhost/api/riders/r1/pod-uploads?kind=Signature", {
      method: "POST",
      headers: { "Content-Type": "image/png" },
      body: new Uint8Array([1, 2, 3, 4]),
    });
    const response = await POST(request, { params: { id: "r1" } });

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/riders/r1/pod-uploads?kind=Signature",
      expect.objectContaining({ method: "POST", headers: { "Content-Type": "image/png" } })
    );
    expect(response.status).toBe(200);
  });
});
```

Create `src/web/src/app/api/riders/[id]/delivery-attempts/complete/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("POST /api/riders/[id]/delivery-attempts/complete", () => {
  it("proxies the completion request to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "a1", succeeded: true }),
    } as Response);

    const request = new Request("http://localhost/api/riders/r1/delivery-attempts/complete", {
      method: "POST",
      body: JSON.stringify({ shipmentId: "s1", recipientName: "Recipient One", otpVerified: true }),
    });
    const response = await POST(request, { params: { id: "r1" } });

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/riders/r1/delivery-attempts/complete",
      expect.objectContaining({ method: "POST" })
    );
    expect(response.status).toBe(200);
  });
});
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `cd src/web && npm test -- pod-uploads/route.test.ts delivery-attempts/complete/route.test.ts`
Expected: FAIL — neither route exists yet.

- [ ] **Step 4: Create both BFF routes**

Create `src/web/src/app/api/riders/[id]/pod-uploads/route.ts`. Unlike every other BFF route so far, this one forwards a raw binary body (a signature PNG or a photo), not JSON — `request.arrayBuffer()` reads the raw bytes, and the incoming `Content-Type` header (set by the client to the actual image mime type) is forwarded unchanged so the backend's content-type allowlist check sees the real type:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request, { params }: { params: { id: string } }) {
  const { searchParams } = new URL(request.url);
  const kind = searchParams.get("kind") ?? "";
  const contentType = request.headers.get("content-type") ?? "application/octet-stream";
  const bodyBuffer = await request.arrayBuffer();
  try {
    const response = await apiFetch(`/api/v1/riders/${params.id}/pod-uploads?kind=${kind}`, {
      method: "POST",
      headers: { "Content-Type": contentType },
      body: bodyBuffer,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/app/api/riders/[id]/delivery-attempts/complete/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/riders/${params.id}/delivery-attempts/complete`, {
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

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd src/web && npm test -- pod-uploads/route.test.ts delivery-attempts/complete/route.test.ts`
Expected: PASS (2/2)

- [ ] **Step 6: Add the hooks**

Create `src/web/src/features/riders/hooks/usePodUpload.ts`:

```ts
"use client";

import { useMutation } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { UploadPodFileResult } from "@/features/riders/types";

export function usePodUpload(riderId: string) {
  return useMutation({
    mutationFn: ({ kind, blob }: { kind: "Signature" | "Photo"; blob: Blob }) =>
      clientFetch<UploadPodFileResult>(`/api/riders/${riderId}/pod-uploads?kind=${kind}`, {
        method: "POST",
        headers: { "Content-Type": blob.type || "application/octet-stream" },
        body: blob,
      }),
  });
}
```

Create `src/web/src/features/riders/hooks/useCompleteDelivery.ts`:

```ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CompleteDeliveryInput, DeliveryAttemptResult } from "@/features/riders/types";

export function useCompleteDelivery(riderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CompleteDeliveryInput) =>
      clientFetch<DeliveryAttemptResult>(`/api/riders/${riderId}/delivery-attempts/complete`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["riders", "me", "tasks", "today"] });
    },
  });
}
```

- [ ] **Step 7: Write the failing SignaturePad test**

Create `src/web/src/features/riders/components/SignaturePad.test.tsx`:

```tsx
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { SignaturePad } from "./SignaturePad";

describe("SignaturePad", () => {
  beforeEach(() => {
    HTMLCanvasElement.prototype.getContext = vi.fn().mockReturnValue({
      beginPath: vi.fn(),
      moveTo: vi.fn(),
      lineTo: vi.fn(),
      stroke: vi.fn(),
      clearRect: vi.fn(),
    }) as unknown as typeof HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.toBlob = vi.fn(function (callback: BlobCallback) {
      callback(new Blob(["x"], { type: "image/png" }));
    });
    HTMLCanvasElement.prototype.getBoundingClientRect = vi.fn().mockReturnValue({ left: 0, top: 0 } as DOMRect);
  });

  it("calls onChange with a blob after drawing", () => {
    const onChange = vi.fn();
    render(<SignaturePad onChange={onChange} />);
    const canvas = screen.getByRole("img", { name: /signature pad/i });

    fireEvent.pointerDown(canvas, { clientX: 10, clientY: 10 });
    fireEvent.pointerMove(canvas, { clientX: 20, clientY: 20 });
    fireEvent.pointerUp(canvas);

    expect(onChange).toHaveBeenCalledWith(expect.any(Blob));
  });

  it("clear resets the canvas and calls onChange with null", () => {
    const onChange = vi.fn();
    render(<SignaturePad onChange={onChange} />);
    const canvas = screen.getByRole("img", { name: /signature pad/i });
    fireEvent.pointerDown(canvas, { clientX: 10, clientY: 10 });
    fireEvent.pointerMove(canvas, { clientX: 20, clientY: 20 });
    fireEvent.pointerUp(canvas);

    fireEvent.click(screen.getByRole("button", { name: /clear/i }));

    expect(onChange).toHaveBeenLastCalledWith(null);
  });
});
```

- [ ] **Step 8: Run test to verify it fails**

Run: `cd src/web && npm test -- SignaturePad.test.tsx`
Expected: FAIL — component doesn't exist yet.

- [ ] **Step 9: Create SignaturePad**

```tsx
"use client";

import { useRef, useState, type PointerEvent } from "react";
import { Button } from "@/components/ui/button";

export function SignaturePad({ onChange }: { onChange: (blob: Blob | null) => void }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const drawing = useRef(false);
  const [hasDrawn, setHasDrawn] = useState(false);

  function getContext() {
    return canvasRef.current?.getContext("2d") ?? null;
  }

  function pointerDown(e: PointerEvent<HTMLCanvasElement>) {
    const ctx = getContext();
    if (!ctx || !canvasRef.current) return;
    drawing.current = true;
    const rect = canvasRef.current.getBoundingClientRect();
    ctx.beginPath();
    ctx.moveTo(e.clientX - rect.left, e.clientY - rect.top);
  }

  function pointerMove(e: PointerEvent<HTMLCanvasElement>) {
    if (!drawing.current) return;
    const ctx = getContext();
    if (!ctx || !canvasRef.current) return;
    const rect = canvasRef.current.getBoundingClientRect();
    ctx.lineTo(e.clientX - rect.left, e.clientY - rect.top);
    ctx.stroke();
    setHasDrawn(true);
  }

  function pointerUp() {
    if (!drawing.current) return;
    drawing.current = false;
    canvasRef.current?.toBlob((blob) => onChange(blob), "image/png");
  }

  function clear() {
    const ctx = getContext();
    if (ctx && canvasRef.current) {
      ctx.clearRect(0, 0, canvasRef.current.width, canvasRef.current.height);
    }
    setHasDrawn(false);
    onChange(null);
  }

  return (
    <div className="space-y-2">
      <canvas
        ref={canvasRef}
        role="img"
        aria-label="Signature pad"
        width={320}
        height={120}
        className="touch-none rounded-md border border-input bg-background"
        onPointerDown={pointerDown}
        onPointerMove={pointerMove}
        onPointerUp={pointerUp}
        onPointerLeave={pointerUp}
      />
      <Button type="button" variant="outline" size="sm" disabled={!hasDrawn} onClick={clear}>
        Clear
      </Button>
    </div>
  );
}
```

- [ ] **Step 10: Run test to verify it passes**

Run: `cd src/web && npm test -- SignaturePad.test.tsx`
Expected: PASS (2/2)

- [ ] **Step 11: Extend the RecordAttemptSheet test for the success submit**

Add to `RecordAttemptSheet.test.tsx`:

```tsx
  it("submits a completed delivery with recipient name and OTP flag", async () => {
    const user = userEvent.setup();
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ id: "a1", succeeded: true });
    useMyDeliveriesPanelStore.setState({ panel: { mode: "record-attempt", task } });
    renderWithProviders();

    await user.type(screen.getByLabelText(/recipient name/i), "Recipient One");
    await user.click(screen.getByLabelText(/customer verified via otp/i));
    await user.click(screen.getByRole("button", { name: /^submit$/i }));

    await waitFor(() => expect(clientFetchModule.clientFetch).toHaveBeenCalledWith(
      "/api/riders/r1/delivery-attempts/complete",
      expect.objectContaining({ method: "POST" })
    ));
  });

  it("requires a collected COD amount before submitting when the task has COD", async () => {
    const user = userEvent.setup();
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({ id: "a1", succeeded: true });
    useMyDeliveriesPanelStore.setState({ panel: { mode: "record-attempt", task: { ...task, codAmount: 500 } } });
    renderWithProviders();

    await user.type(screen.getByLabelText(/recipient name/i), "Recipient One");

    expect(screen.getByRole("button", { name: /^submit$/i })).toBeDisabled();
  });
```

- [ ] **Step 12: Run test to verify it fails**

Run: `cd src/web && npm test -- RecordAttemptSheet.test.tsx`
Expected: FAIL — no success submit button wired yet.

- [ ] **Step 13: Wire the success/POD branch's submit**

In `src/web/src/features/riders/components/RecordAttemptSheet.tsx`, add the imports:

```tsx
import { SignaturePad } from "@/features/riders/components/SignaturePad";
import { usePodUpload } from "@/features/riders/hooks/usePodUpload";
import { useCompleteDelivery } from "@/features/riders/hooks/useCompleteDelivery";
```

Add state for the signature/photo blobs and GPS, plus the two mutations:

```tsx
  const [signatureBlob, setSignatureBlob] = useState<Blob | null>(null);
  const [photoFile, setPhotoFile] = useState<File | null>(null);
  const [latitude, setLatitude] = useState<number | null>(null);
  const [longitude, setLongitude] = useState<number | null>(null);
  const podUpload = usePodUpload(riderId);
  const completeDelivery = useCompleteDelivery(riderId);
```

Add a location-capture helper alongside the other handlers:

```tsx
  function captureLocation() {
    if (!("geolocation" in navigator)) return;
    navigator.geolocation.getCurrentPosition(
      (position) => {
        setLatitude(position.coords.latitude);
        setLongitude(position.coords.longitude);
      },
      () => {
        // Permission denied or unavailable — GPS is optional, submission proceeds without it.
      }
    );
  }

  const codRequired = task ? task.codAmount > 0 : false;
  const codInvalid = codRequired && (collectedCodAmount === "" || Number(collectedCodAmount) < 0);
  const canSubmitSuccess = recipientName.trim().length > 0 && !codInvalid;

  async function handleSubmitSuccess() {
    if (!task || !canSubmitSuccess) return;
    setServerError(null);
    try {
      const signatureResult = signatureBlob ? await podUpload.mutateAsync({ kind: "Signature", blob: signatureBlob }) : null;
      const photoResult = photoFile ? await podUpload.mutateAsync({ kind: "Photo", blob: photoFile }) : null;
      await completeDelivery.mutateAsync({
        shipmentId: task.shipmentId,
        recipientName: recipientName.trim(),
        otpVerified,
        signatureStorageKey: signatureResult?.storageKey ?? null,
        photoStorageKey: photoResult?.storageKey ?? null,
        latitude,
        longitude,
        collectedCodAmount: codRequired ? Number(collectedCodAmount) : null,
      });
      reset();
      close();
    } catch {
      setServerError("Couldn't complete this delivery. Check the fields and try again.");
    }
  }
```

Reset the new state in `reset()`:

```tsx
  function reset() {
    setOutcome("success");
    setReason(undefined);
    setNotes("");
    setRecipientName("");
    setOtpVerified(false);
    setCollectedCodAmount("");
    setSignatureBlob(null);
    setPhotoFile(null);
    setLatitude(null);
    setLongitude(null);
  }
```

Replace the `outcome === "success"` block's contents to add the signature pad, photo input, GPS button, and submit button:

```tsx
            {outcome === "success" && (
              <div className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="recipient-name">Recipient Name</Label>
                  <Input id="recipient-name" value={recipientName} onChange={(e) => setRecipientName(e.target.value)} />
                </div>
                <div className="flex items-center gap-2">
                  <Checkbox id="otp-verified" checked={otpVerified} onCheckedChange={(v) => setOtpVerified(v === true)} />
                  <Label htmlFor="otp-verified">Customer verified via OTP</Label>
                </div>
                <div className="space-y-2">
                  <Label>Signature</Label>
                  <SignaturePad onChange={setSignatureBlob} />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="pod-photo">Photo</Label>
                  <input
                    id="pod-photo"
                    type="file"
                    accept="image/*"
                    capture="environment"
                    onChange={(e) => setPhotoFile(e.target.files?.[0] ?? null)}
                  />
                </div>
                <Button type="button" variant="outline" size="sm" onClick={captureLocation}>
                  {latitude !== null ? "Location captured" : "Capture current location"}
                </Button>
                {task.codAmount > 0 && (
                  <div className="space-y-2">
                    <Label htmlFor="collected-cod">Collected COD Amount</Label>
                    <Input
                      id="collected-cod"
                      type="number"
                      value={collectedCodAmount}
                      onChange={(e) => setCollectedCodAmount(e.target.value)}
                      placeholder={String(task.codAmount)}
                    />
                  </div>
                )}
                {serverError && <p className="text-sm text-destructive">{serverError}</p>}
                <Button
                  type="button"
                  disabled={!canSubmitSuccess || podUpload.isPending || completeDelivery.isPending}
                  onClick={handleSubmitSuccess}
                >
                  {podUpload.isPending || completeDelivery.isPending ? <Spinner className="h-4 w-4" /> : "Submit"}
                </Button>
              </div>
            )}
```

- [ ] **Step 14: Run test to verify it passes**

Run: `cd src/web && npm test -- RecordAttemptSheet.test.tsx`
Expected: PASS (7/7)

- [ ] **Step 15: Run the full frontend suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds

- [ ] **Step 16: Commit**

```bash
git add src/web/src/features/riders src/web/src/app/api/riders/\[id\]/pod-uploads src/web/src/app/api/riders/\[id\]/delivery-attempts/complete
git commit -m "feat(web): add POD capture (signature/photo/OTP/GPS) and delivery completion"
```

---

## Task 14: Update FRONTEND-ROADMAP.md and run full verification

**Files:**
- Modify: `docs/FRONTEND-ROADMAP.md`

**Interfaces:**
- None — documentation and final verification only.

- [ ] **Step 1: Run the full backend test suite**

Run: `dotnet test`
Expected: PASS (all test projects, no regressions from Tasks 1–4)

- [ ] **Step 2: Run the full frontend test suite and production build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS, build succeeds

- [ ] **Step 3: Manual E2E verification against the real API/Postgres**

Per this project's established pattern (every prior phase found real bugs invisible to unit tests — F2's `BranchType`, F3's `ShipmentStatus`/BFF path/`GeographyPicker` ids, F4's `ManifestPurpose`/`ManifestStatus`/stale query keys), before marking this phase done, run through the full flow live: create a rider, assign a pickup task via AWB lookup, log in as that rider, scan the pickup, move the shipment to `AtDestinationBranch` (reuse the existing Manifests flow), assign a delivery task to a second rider, log in as them, record a failed attempt (verify it becomes `NdrCreated`), assign the retry, and complete a delivery with a drawn signature, a photo, and OTP checked — including a COD shipment, to verify the collected-amount path. Fix anything found, following this project's "found and fixed" writeup convention in the roadmap update below.

- [ ] **Step 4: Update the roadmap**

In `docs/FRONTEND-ROADMAP.md`, change:

```
*Status: Not started.*

## Phase F6 — NDR + RTO
```

to a completion write-up in the same style as F2–F4's entries — status line, what shipped, what was found and fixed during manual E2E verification (fill in the actual findings from Step 3; do not leave a placeholder), and any deliberate scope notes (no dispatcher overview board, no run-history view, AWB-lookup-based assignment — all per the design spec's resolved decisions), followed by the existing `## Phase F6 — NDR + RTO` heading unchanged.

- [ ] **Step 5: Commit**

```bash
git add docs/FRONTEND-ROADMAP.md
git commit -m "docs: mark Frontend Phase F5 (Riders + Delivery + POD) complete"
```
