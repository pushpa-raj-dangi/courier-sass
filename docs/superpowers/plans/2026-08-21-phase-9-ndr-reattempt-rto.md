# Phase 9 — NDR, Reattempt, RTO Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a failed delivery attempt a real, auditable resolution path — automatic NDR creation, a staff-driven contact/resolve workflow, a reattempt loop back into delivery, and a full RTO (return-to-origin) path that reuses the existing Manifest transport mechanism for its physical return leg.

**Architecture:** A new `Ndr` module (mirroring `Riders`'s single-module shape) owns `NDR` and `ReturnShipment` together — they share one lifecycle (NDR resolution is RTO's only trigger, per `CLARIFICATIONS.md` #4). NDR creation is triggered automatically: `Riders.RecordFailedAttemptAsync` calls a new narrow `INdrCreationService` right after `FailDeliveryAsync`, in the same request. The reattempt loop and RTO-initiation both drive real `Shipment` transitions through six new `IShipmentTransitionService` methods, following the exact `Manifests`/`Riders` cross-module discipline established in Phases 7–8. RTO's physical return leg reuses `Manifest` as-is — a `Manifest.Purpose` field (`Forward`/`Return`) tells the existing scan/dispatch/receive endpoints which pair of shipment transitions to drive.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core / PostgreSQL, xUnit + EF InMemory (unit) / real Postgres via `WebApplicationFactory` (API tests) — same as every prior phase.

**Spec:** `docs/ROADMAP.md` (Phase 9 section), `docs/DOMAIN-MODEL.md` ("NDR"/"RTO" sections), `CLAUDE.md` §20-21, `docs/SHIPMENT-LIFECYCLE.md`, `docs/CLARIFICATIONS.md` #4. This session's bounded brainstorm (single `Ndr` module, NDR auto-created synchronously from `Riders`, AutoRTO is a selectable resolution outcome only — no scheduled trigger, RTO transport reuses `Manifest` via a new `Purpose` field) is the approved design basis — no separate spec file, consistent with every prior phase in this project.

## Global Constraints

- Never trust a client-supplied `TenantId`/`BranchId` (CLAUDE.md §33) — every write resolves `OrganizationId` from `ICurrentTenantProvider`.
- Permission checks are permission-based (CLAUDE.md §6) — reuse the already-seeded `ndr.read`, `ndr.manage`, `ndr.resolve`, `rto.read`, `rto.manage` keys; no new permission keys needed this phase.
- Every real status transition writes exactly one append-only `ShipmentEvent`; a rejected scan writes none (CLAUDE.md §11/§39).
- `Ndr` must never call `_dbContext.Set<Shipment>()`/`Set<ShipmentEvent>()` directly — only through `IShipmentTransitionService`, mirroring `Riders`/`Manifests`.
- `CLARIFICATIONS.md` #4 is binding: `DELIVERY_FAILED → RTO_INITIATED` must never exist as a direct transition anywhere in the codebase — RTO is only reachable via `NDR_CREATED` and an explicit NDR resolution.
- `NDR`/`ReturnShipment` both carry their own `OrganizationId` (DOMAIN-MODEL.md annotates both sections `OrganizationId present`) — direct query filters, not transitive ones.
- Every fake `IShipmentTransitionService` implementation across `Manifests.Tests` and `Riders.Tests` must be updated wherever this plan adds interface members — this was a real gap missed mid-plan in Phase 8; Task 1 below enumerates every file that needs it.
- `CreateManifestRequest` gains `Purpose` as a **trailing optional parameter defaulting to `Forward`** — never make it required. A required parameter would break all 17 existing `CreateManifestRequest(...)` call sites across `Manifests.Tests`/`Riders.Tests`/`Api.Tests` for no reason; every one of them means "Forward" today.

---

## Task 1: Shipments groundwork — five new statuses, six new transition methods, and every fake this breaks

This is one task (not split further) for the same reason Phase 8's Task 2 was one task: removing/adding `IShipmentTransitionService` members breaks compilation everywhere a fake implements it, and the fix must land in the same commit as the interface change for the repo to build at any intermediate point.

**Files:**
- Modify: `src/Modules/Shipments/Domain/ShipmentStatus.cs`
- Modify: `src/Modules/Shipments/Domain/ShipmentEventType.cs`
- Modify: `src/Modules/Shipments/Application/IShipmentTransitionService.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentService.Transitions.cs`
- Test: `tests/Shipments.Tests/ShipmentServiceNdrRtoTransitionTests.cs` (new)
- Modify: `tests/Manifests.Tests/ManifestServiceCreationTests.cs`
- Modify: `tests/Manifests.Tests/ManifestServiceScanningTests.cs`
- Modify: `tests/Manifests.Tests/ManifestServiceTransitionsTests.cs`
- Modify: `tests/Riders.Tests/RiderServiceCreationTests.cs`
- Modify: `tests/Riders.Tests/RiderServiceTasksTests.cs`
- Modify: `tests/Riders.Tests/RiderServiceAttemptsTests.cs`
- Modify: `tests/Riders.Tests/RiderServiceReadsTests.cs`

**Interfaces:**
- Produces (new `IShipmentTransitionService` members later tasks' `Ndr`/`Manifests` code calls): `TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken) -> Task<ShipmentResponse?>`, `ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken) -> Task<ShipmentResponse?>`, `InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken) -> Task<ShipmentResponse?>`, `DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken) -> Task`, `ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken) -> Task<ShipmentResponse?>`, `ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken) -> Task<ShipmentResponse?>`.

- [ ] **Step 1: Add the five new enum members**

`src/Modules/Shipments/Domain/ShipmentStatus.cs` — replace in full:

```csharp
namespace Courier.Shipments.Domain;

public enum ShipmentStatus
{
    Created,
    PickupAssigned,
    PickedUp,
    AtOriginBranch,
    Sorted,
    InTransit,
    AtDestinationBranch,
    OutForDelivery,
    DeliveryFailed,
    NdrCreated,
    RtoInitiated,
    RtoInTransit,
    RtoAtOrigin,
    RtoDelivered,
    Delivered,
    Cancelled
}
```

`src/Modules/Shipments/Domain/ShipmentEventType.cs` — replace in full:

```csharp
namespace Courier.Shipments.Domain;

public enum ShipmentEventType
{
    Created,
    PickupAssigned,
    PickedUp,
    ReceivedAtOrigin,
    Sorted,
    Dispatched,
    ReceivedAtDestination,
    OutForDelivery,
    DeliveryFailed,
    NdrCreated,
    RtoInitiated,
    RtoInTransit,
    RtoAtOrigin,
    RtoDelivered,
    Delivered,
    Cancelled
}
```

- [ ] **Step 2: Add the six new `IShipmentTransitionService` methods**

Append to the interface in `src/Modules/Shipments/Application/IShipmentTransitionService.cs` (only the interface body changes — `ShipmentScanInfo` is unchanged this phase):

```csharp
    // Phase 8 — driven by Riders.Application.RiderService.
    Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);

    // Phase 9 — driven by Ndr.Application (NdrCreationService, NdrService) and Manifests.Application
    // (ManifestService, for the RTO transport leg only).
    Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
```

Also update the interface's doc comment (directly above `public interface IShipmentTransitionService`) by appending one sentence: `Phase 9 adds the NDR/RTO transition surface, driven by the new Ndr module and by Manifests for the RTO transport leg only.`

- [ ] **Step 3: Implement the six methods**

Append to `src/Modules/Shipments/Application/ShipmentService.Transitions.cs`, immediately before the closing `}` of the `ShipmentService` class (after `FailDeliveryAsync`, before `CancelAsync`):

```csharp
    // Callers: Ndr.Application.NdrCreationService (this one only), immediately after
    // Riders.Application.RiderService.RecordFailedAttemptAsync calls FailDeliveryAsync above, in
    // the same request — SHIPMENT-LIFECYCLE.md's diagram shows DELIVERY_FAILED -> NDR_CREATED as an
    // automatic system step, not a separate manual action.
    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.DeliveryFailed, ShipmentStatus.NdrCreated, ShipmentEventType.NdrCreated,
            _ => { }, "shipment.ndr_created", actingUser, cancellationToken);

    // Caller: Ndr.Application.NdrService.ResolveAsync, Resolution=Reattempt only. Goes straight to
    // OUT_FOR_DELIVERY (CLARIFICATIONS.md #4 / SHIPMENT-LIFECYCLE.md's diagram show no intermediate
    // status between NDR_CREATED and a reattempt). Custody is deliberately left untouched —
    // CurrentCustodianRiderId already holds the same rider from the original failed attempt
    // (FailDeliveryAsync never cleared it), so the parcel's physical custody chain stays accurate
    // without this method needing to know which rider that was.
    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.NdrCreated, ShipmentStatus.OutForDelivery, ShipmentEventType.OutForDelivery,
            _ => { }, "shipment.reattempt_scheduled", actingUser, cancellationToken);

    // Caller: Ndr.Application.NdrService.ResolveAsync, Resolution=Rto or AutoRto. RTO is a
    // back-office/staff action (NDR resolution), not something the rider does in the field, so
    // custody moves OFF the rider and onto the destination branch — the branch that attempted
    // delivery and is now the one that will process the physical return. This is also what makes
    // Manifests.Application.ManifestService.ScanRtoAsync's custody check (CurrentBranchId ==
    // Return-manifest's FromBranchId) work the same shape as the forward-path ScanAsync's.
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.NdrCreated, ShipmentStatus.RtoInitiated, ShipmentEventType.RtoInitiated,
            s => { s.CurrentCustodianRiderId = null; s.CurrentBranchId = s.DestinationBranchId; }, "shipment.rto_initiated", actingUser, cancellationToken);

    // Caller: Manifests.Application.ManifestService.DispatchAsync, Return-purpose manifests only —
    // mirrors DispatchManyAsync's bulk-transaction shape exactly, just for the RTO status pair.
    public async Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipments = await _dbContext.Set<Shipment>().Where(s => shipmentIds.Contains(s.Id)).ToListAsync(cancellationToken);
        var actingUserId = actingUser.GetUserId();
        var now = DateTimeOffset.UtcNow;

        foreach (var shipment in shipments)
        {
            if (shipment.CurrentStatus != ShipmentStatus.RtoInitiated)
            {
                throw new ShipmentInvalidTransitionException(shipment.Id, shipment.CurrentStatus, ShipmentStatus.RtoInTransit);
            }
            var previousStatus = shipment.CurrentStatus;
            var eventBranchId = shipment.CurrentBranchId;
            shipment.CurrentStatus = ShipmentStatus.RtoInTransit;
            shipment.CurrentBranchId = null;

            _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                EventType = ShipmentEventType.RtoInTransit,
                PreviousStatus = previousStatus,
                NewStatus = ShipmentStatus.RtoInTransit,
                BranchId = eventBranchId,
                UserId = actingUserId,
                Timestamp = now
            });
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ShipmentValidationException("One or more shipments on this return manifest were modified concurrently; dispatch was not applied.");
        }
    }

    // Caller: Manifests.Application.ManifestService.ReceiveScanAsync, Return-purpose manifests only.
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.RtoInTransit, ShipmentStatus.RtoAtOrigin, ShipmentEventType.RtoAtOrigin,
            s => s.CurrentBranchId = s.OriginBranchId, "shipment.rto_at_origin", actingUser, cancellationToken);

    // Caller: Ndr.Application.ReturnShipmentService.ConfirmAsync. Terminal.
    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.RtoAtOrigin, ShipmentStatus.RtoDelivered, ShipmentEventType.RtoDelivered,
            _ => { }, "shipment.rto_delivered", actingUser, cancellationToken);

```

- [ ] **Step 4: Write failing unit tests proving the new methods and the CLARIFICATIONS.md #4 guarantee**

Create `tests/Shipments.Tests/ShipmentServiceNdrRtoTransitionTests.cs` — reuses the exact fixtures/fakes/`CreateService`/`SeedCreatedShipmentAsync` helpers already defined in `tests/Shipments.Tests/ShipmentServiceTransitionTests.cs` (copy those file-scoped fakes and the `TestDbContext`/`CreateContext`/`ActingUser`/`CreateService`/`SeedCreatedShipmentAsync` helpers verbatim — this project's established per-file-helper convention, same as every other `*Tests.cs` pair in this codebase):

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
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
}

file class AlwaysTrueGeographyLookup : IGeographyLookupService
{
    public Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> DeliveryZoneExistsAsync(Guid zoneId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
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

public class ShipmentServiceNdrRtoTransitionTests
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
            new FixedOrganizationLookup("testorg"));
    }

    private static async Task<(ShipmentService Service, Guid ShipmentId, Guid OriginBranchId, Guid DestinationBranchId)> SeedShipmentAtStatusAsync(
        TestDbContext dbContext, Guid organizationId, ShipmentStatus targetStatus)
    {
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var originZoneId = Guid.NewGuid();
        var destinationZoneId = Guid.NewGuid();
        var serviceTypeId = dbContext.Set<ServiceType>().First(s => s.Code == ServiceTypeCode.Standard).Id;
        var weightSlabId = dbContext.Set<WeightSlab>().First(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m).Id;

        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Test Org", Slug = "testorg" });
        var rateCard = new RateCard { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "Default", EffectiveFrom = DateTimeOffset.UtcNow, Version = 1, IsActive = true };
        dbContext.Set<RateCard>().Add(rateCard);
        dbContext.Set<ZoneRate>().Add(new ZoneRate { Id = Guid.NewGuid(), RateCardId = rateCard.Id, OriginZoneId = originZoneId, DestinationZoneId = destinationZoneId, ServiceTypeId = serviceTypeId, WeightSlabId = weightSlabId, BaseRate = 100m });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, organizationId, new() { [originBranchId] = originZoneId, [destinationBranchId] = destinationZoneId });
        var request = new CreateShipmentRequest(
            Guid.NewGuid(), originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
            "Recipient", "9800000000", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Recipient Address",
            "Sender", "9800000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Sender Address",
            new List<CreateShipmentItemRequest> { new("Item", 1, 100m) });
        var created = await service.CreateShipmentAsync(request, ActingUser(Guid.NewGuid()));
        var actingUser = ActingUser(Guid.NewGuid());
        var riderId = Guid.NewGuid();

        // Walk the shipment through the exact same real transition methods every earlier phase's
        // tests use, up to (but not including) targetStatus.
        if (targetStatus == ShipmentStatus.Created) return (service, created.Id, originBranchId, destinationBranchId);
        await service.AssignPickupToRiderAsync(created.Id, riderId, actingUser);
        if (targetStatus == ShipmentStatus.PickupAssigned) return (service, created.Id, originBranchId, destinationBranchId);
        await service.RiderPickupScanAsync(created.Id, riderId, actingUser);
        await service.ReceiveAtOriginAsync(created.Id, actingUser);
        await service.SortAsync(created.Id, actingUser);
        await service.DispatchManyAsync(new[] { created.Id }, actingUser);
        await service.ReceiveAtDestinationAsync(created.Id, actingUser);
        await service.AssignDeliveryToRiderAsync(created.Id, riderId, actingUser);
        if (targetStatus == ShipmentStatus.OutForDelivery) return (service, created.Id, originBranchId, destinationBranchId);
        await service.FailDeliveryAsync(created.Id, riderId, actingUser);
        if (targetStatus == ShipmentStatus.DeliveryFailed) return (service, created.Id, originBranchId, destinationBranchId);
        await service.TransitionToNdrCreatedAsync(created.Id, actingUser);
        if (targetStatus == ShipmentStatus.NdrCreated) return (service, created.Id, originBranchId, destinationBranchId);
        await service.InitiateRtoAsync(created.Id, actingUser);
        if (targetStatus == ShipmentStatus.RtoInitiated) return (service, created.Id, originBranchId, destinationBranchId);
        await service.DispatchManyRtoAsync(new[] { created.Id }, actingUser);
        if (targetStatus == ShipmentStatus.RtoInTransit) return (service, created.Id, originBranchId, destinationBranchId);
        await service.ReceiveRtoAtOriginAsync(created.Id, actingUser);
        return (service, created.Id, originBranchId, destinationBranchId);
    }

    [Fact]
    public async Task TransitionToNdrCreatedAsync_FromDeliveryFailed_Succeeds()
    {
        await using var dbContext = CreateContext();
        var (service, shipmentId, _, _) = await SeedShipmentAtStatusAsync(dbContext, Guid.NewGuid(), ShipmentStatus.DeliveryFailed);

        var result = await service.TransitionToNdrCreatedAsync(shipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.NdrCreated, result!.CurrentStatus);
    }

    [Fact]
    public async Task ReattemptDeliveryAsync_FromNdrCreated_TransitionsToOutForDeliveryWithoutTouchingCustody()
    {
        await using var dbContext = CreateContext();
        var (service, shipmentId, _, _) = await SeedShipmentAtStatusAsync(dbContext, Guid.NewGuid(), ShipmentStatus.NdrCreated);
        var beforeCustody = (await dbContext.Set<Shipment>().SingleAsync(s => s.Id == shipmentId)).CurrentCustodianRiderId;

        var result = await service.ReattemptDeliveryAsync(shipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.OutForDelivery, result!.CurrentStatus);
        Assert.Equal(beforeCustody, result.CurrentCustodianRiderId);
        Assert.NotNull(beforeCustody);
    }

    [Fact]
    public async Task InitiateRtoAsync_FromNdrCreated_MovesCustodyToDestinationBranch()
    {
        await using var dbContext = CreateContext();
        var (service, shipmentId, _, destinationBranchId) = await SeedShipmentAtStatusAsync(dbContext, Guid.NewGuid(), ShipmentStatus.NdrCreated);

        var result = await service.InitiateRtoAsync(shipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.RtoInitiated, result!.CurrentStatus);
        Assert.Null(result.CurrentCustodianRiderId);
        Assert.Equal(destinationBranchId, result.CurrentBranchId);
    }

    [Fact]
    public async Task InitiateRtoAsync_FromDeliveryFailed_ThrowsInvalidTransition()
    {
        // Regression test for CLARIFICATIONS.md #4: RTO_INITIATED must only ever be reachable from
        // NDR_CREATED, never directly from DELIVERY_FAILED.
        await using var dbContext = CreateContext();
        var (service, shipmentId, _, _) = await SeedShipmentAtStatusAsync(dbContext, Guid.NewGuid(), ShipmentStatus.DeliveryFailed);

        var ex = await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => service.InitiateRtoAsync(shipmentId, ActingUser(Guid.NewGuid())));
        Assert.Equal(ShipmentStatus.DeliveryFailed, ex.From);
        Assert.Equal(ShipmentStatus.RtoInitiated, ex.To);
    }

    [Fact]
    public async Task FullRtoPath_NdrCreatedToRtoDelivered_WalksEveryTransition()
    {
        await using var dbContext = CreateContext();
        var (service, shipmentId, originBranchId, destinationBranchId) = await SeedShipmentAtStatusAsync(dbContext, Guid.NewGuid(), ShipmentStatus.NdrCreated);
        var actingUser = ActingUser(Guid.NewGuid());

        var afterInitiate = await service.InitiateRtoAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.RtoInitiated, afterInitiate!.CurrentStatus);
        Assert.Equal(destinationBranchId, afterInitiate.CurrentBranchId);

        await service.DispatchManyRtoAsync(new[] { shipmentId }, actingUser);
        var afterDispatch = await dbContext.Set<Shipment>().SingleAsync(s => s.Id == shipmentId);
        Assert.Equal(ShipmentStatus.RtoInTransit, afterDispatch.CurrentStatus);
        Assert.Null(afterDispatch.CurrentBranchId);

        var afterReceive = await service.ReceiveRtoAtOriginAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.RtoAtOrigin, afterReceive!.CurrentStatus);
        Assert.Equal(originBranchId, afterReceive.CurrentBranchId);

        var afterConfirm = await service.ConfirmRtoDeliveredAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.RtoDelivered, afterConfirm!.CurrentStatus);

        var events = await dbContext.Set<ShipmentEvent>().Where(e => e.ShipmentId == shipmentId).OrderBy(e => e.Timestamp).Select(e => e.EventType).ToListAsync();
        Assert.Equal(new[]
        {
            ShipmentEventType.Created, ShipmentEventType.PickupAssigned, ShipmentEventType.PickedUp, ShipmentEventType.ReceivedAtOrigin,
            ShipmentEventType.Sorted, ShipmentEventType.Dispatched, ShipmentEventType.ReceivedAtDestination, ShipmentEventType.OutForDelivery,
            ShipmentEventType.DeliveryFailed, ShipmentEventType.NdrCreated, ShipmentEventType.RtoInitiated, ShipmentEventType.RtoInTransit,
            ShipmentEventType.RtoAtOrigin, ShipmentEventType.RtoDelivered
        }, events);
    }
}
```

- [ ] **Step 5: Run the new tests to verify they fail to compile / fail to run** (the interface members don't exist yet in `ShipmentService` — wait, Step 3 already added them; run now to see them pass)

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter "FullyQualifiedName~ShipmentServiceNdrRtoTransitionTests"`
Expected: All 5 tests pass (the implementation from Step 3 already exists).

- [ ] **Step 6: Update every fake `IShipmentTransitionService` implementation across `Manifests.Tests` and `Riders.Tests`**

Each of the seven fakes below needs the same six new stub methods appended, all throwing `NotSupportedException` (none of these existing test scenarios exercise NDR/RTO transitions — that's this plan's later `Ndr.Tests`' job):

```csharp
    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
```

Add this block immediately after the existing `FailDeliveryAsync` stub/impl line, immediately before the fake class's closing `}`, in each of these seven classes:

- `tests/Manifests.Tests/ManifestServiceCreationTests.cs` — `StubShipmentTransitionService`
- `tests/Manifests.Tests/ManifestServiceScanningTests.cs` — `ScanningFakeShipmentTransitionService`
- `tests/Manifests.Tests/ManifestServiceTransitionsTests.cs` — `TransitionsFakeShipmentTransitionService`
- `tests/Riders.Tests/RiderServiceCreationTests.cs` — `NotSupportedShipmentTransitionService`
- `tests/Riders.Tests/RiderServiceTasksTests.cs` — `TasksFakeShipmentTransitionService`
- `tests/Riders.Tests/RiderServiceAttemptsTests.cs` — `AttemptsFakeShipmentTransitionService`
- `tests/Riders.Tests/RiderServiceReadsTests.cs` — `NotSupportedShipmentTransitionService`

- [ ] **Step 7: Build the whole solution and run every existing test project to confirm nothing regressed**

Run: `dotnet build`
Expected: Build succeeds with zero errors.

Run: `dotnet test`
Expected: Every test project passes — `Shipments.Tests` now has 5 more tests than before this task; `Manifests.Tests`/`Riders.Tests`/`Api.Tests` counts are unchanged (only new stub methods were added, no behavior changed).

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Shipments/Domain/ShipmentStatus.cs src/Modules/Shipments/Domain/ShipmentEventType.cs src/Modules/Shipments/Application/IShipmentTransitionService.cs src/Modules/Shipments/Application/ShipmentService.Transitions.cs tests/Shipments.Tests/ShipmentServiceNdrRtoTransitionTests.cs tests/Manifests.Tests/ManifestServiceCreationTests.cs tests/Manifests.Tests/ManifestServiceScanningTests.cs tests/Manifests.Tests/ManifestServiceTransitionsTests.cs tests/Riders.Tests/RiderServiceCreationTests.cs tests/Riders.Tests/RiderServiceTasksTests.cs tests/Riders.Tests/RiderServiceAttemptsTests.cs tests/Riders.Tests/RiderServiceReadsTests.cs
git commit -m "feat(shipments): add NDR/RTO statuses and six new rider/staff-driven transition methods"
```

---

## Task 2: Ndr module scaffold — project file and Domain layer

**Files:**
- Create: `src/Modules/Ndr/Ndr.csproj`
- Create: `src/Modules/Ndr/Domain/NdrStatus.cs`
- Create: `src/Modules/Ndr/Domain/NdrResolution.cs`
- Create: `src/Modules/Ndr/Domain/NdrReason.cs`
- Create: `src/Modules/Ndr/Domain/Ndr.cs`
- Create: `src/Modules/Ndr/Domain/ReturnShipment.cs`

**Interfaces:**
- Produces: every Domain type below, consumed by Task 3 (Infrastructure), Task 4 (`AppDbContext`), and Tasks 5–8/11 (Application/Presentation).

- [ ] **Step 1: Create the project file**

`src/Modules/Ndr/Ndr.csproj` — mirrors `Riders.csproj`'s shape. References `Branches` (validate branch ids via `IBranchLookupService`, same as `Manifests`/`Riders`) and `Shipments` (`IShipmentTransitionService`) — deliberately **no** reference to `Identity` (no user provisioning happens in this module) and **no** reference to `Riders` (see `NdrReason` below):

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
    <ProjectReference Include="..\Audit\Audit.csproj" />
    <ProjectReference Include="..\Branches\Branches.csproj" />
    <ProjectReference Include="..\Shipments\Shipments.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: `NdrStatus`, `NdrResolution`, `NdrReason`**

`src/Modules/Ndr/Domain/NdrStatus.cs`:

```csharp
namespace Courier.Ndr.Domain;

// Exact list from CLAUDE.md §20. Legal forward moves (enforced in NdrService.UpdateAsync, Task 7):
// Open -> Contacting -> CustomerConfirmed -> {ReattemptScheduled | RtoRequested} -> Resolved.
// No backward or skipped moves.
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

`src/Modules/Ndr/Domain/NdrResolution.cs`:

```csharp
namespace Courier.Ndr.Domain;

// Exact list from DOMAIN-MODEL.md's NDR entity. AutoRTO is a selectable resolution outcome only
// this phase (this plan's resolved design decision) — no scheduled/cron trigger exists to set it
// automatically; a human or a future automated caller must still call POST /ndrs/{id}/resolve.
public enum NdrResolution
{
    Reattempt,
    Rto,
    AutoRto,
    Other
}
```

`src/Modules/Ndr/Domain/NdrReason.cs`:

```csharp
namespace Courier.Ndr.Domain;

// Deliberately duplicated from Riders.Domain.DeliveryAttemptReason (identical 9 values, CLAUDE.md
// §19) rather than referenced — avoids Ndr.csproj needing a project reference to Riders.csproj
// purely for one enum's type, mirroring how Riders.Domain.DeliveryAttemptReason was itself already
// a fresh copy of CLAUDE.md §19's list, not shared from anywhere else. Riders.Application.
// RiderService converts its own DeliveryAttemptReason to this one via an explicit switch
// expression (Task 6), not a raw int cast — so a future divergence between the two lists fails
// loudly at compile time instead of silently miscasting.
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

- [ ] **Step 3: `Ndr` entity**

`src/Modules/Ndr/Domain/Ndr.cs`:

```csharp
namespace Courier.Ndr.Domain;

// Auto-created by Ndr.Application.NdrCreationService.CreateFromFailedAttemptAsync, called from
// Riders.Application.RiderService.RecordFailedAttemptAsync immediately after FailDeliveryAsync
// (Task 6) — SHIPMENT-LIFECYCLE.md's diagram shows DELIVERY_FAILED -> NDR_CREATED as an automatic
// system step, not a separate manual action.
public class Ndr
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ShipmentId { get; set; }
    public NdrReason Reason { get; set; }
    public Guid? AssignedStaffUserId { get; set; }
    public NdrStatus Status { get; set; }
    public string? CustomerResponse { get; set; }
    public DateTimeOffset? FollowUpAt { get; set; }
    public string? NextAction { get; set; }
    public NdrResolution? Resolution { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
```

- [ ] **Step 4: `ReturnShipment` entity**

`src/Modules/Ndr/Domain/ReturnShipment.cs`:

```csharp
namespace Courier.Ndr.Domain;

// Created by Ndr.Application.NdrService.ResolveAsync when Resolution is Rto or AutoRto (Task 7).
// Deliberately does NOT store its own Status/CurrentBranchId/ReceivedAt columns, unlike
// DOMAIN-MODEL.md's original field sketch — the underlying Shipment (via OriginalShipmentId) is
// already the single source of truth for both (CurrentStatus walks RtoInitiated -> RtoInTransit ->
// RtoAtOrigin -> RtoDelivered, CurrentBranchId tracks the same physical custody Manifests already
// updates during the return leg). A second, independently-updated copy of either would need
// Manifests to gain a project reference to Ndr purely to keep it in sync — this plan avoids that
// cross-module coupling entirely by having ReturnShipmentService (Task 11) read the live values
// from IShipmentTransitionService.GetScanInfoAsync(OriginalShipmentId) instead, matching how
// Manifests.Application.ManifestService.GetMissingShipmentsAsync already enriches its own response
// with live shipment data the same way (CLAUDE.md §12: a status change alone is never treated as
// proof of physical transfer, and neither is a cached copy of one). MerchantConfirmedAt is the one
// exception — it's a genuine Ndr-module-owned fact (when staff confirmed the merchant accepted the
// return), set directly by ReturnShipmentService.ConfirmAsync with no cross-module coordination
// needed.
public class ReturnShipment
{
    public Guid Id { get; set; }
    public Guid OriginalShipmentId { get; set; }
    public Guid OrganizationId { get; set; }
    public required string ReturnAwb { get; set; }
    public required string RtoReason { get; set; }
    public Guid OriginBranchId { get; set; }
    public Guid DestinationBranchId { get; set; }
    public DateTimeOffset? MerchantConfirmedAt { get; set; }
}
```

- [ ] **Step 5: Build the (currently empty-of-logic) project to confirm the scaffold compiles**

Run: `dotnet build src/Modules/Ndr/Ndr.csproj`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Ndr/Ndr.csproj src/Modules/Ndr/Domain/
git commit -m "feat(ndr): scaffold Ndr module and domain entities"
```

---

## Task 3: Ndr Infrastructure — EF configurations

**Files:**
- Create: `src/Modules/Ndr/Infrastructure/NdrConfiguration.cs`
- Create: `src/Modules/Ndr/Infrastructure/ReturnShipmentConfiguration.cs`

**Interfaces:**
- Produces: two `IEntityTypeConfiguration<T>` classes Task 4's `AppDbContext.OnModelCreating` applies.

- [ ] **Step 1: `Ndr`**

`src/Modules/Ndr/Infrastructure/NdrConfiguration.cs`:

```csharp
using Courier.Ndr.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Ndr.Infrastructure;

public class NdrConfiguration : IEntityTypeConfiguration<Ndr>
{
    public void Configure(EntityTypeBuilder<Ndr> builder)
    {
        builder.ToTable("Ndrs");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.CustomerResponse).HasMaxLength(1000);
        builder.Property(n => n.NextAction).HasMaxLength(500);

        builder.HasIndex(n => n.OrganizationId);
        builder.HasIndex(n => n.ShipmentId);
        // Not unique: a shipment could in principle accumulate more than one NDR over its lifetime
        // (a second failed attempt after some future phase's successful reattempt would create a
        // second one) — only "no second Open NDR for the same shipment while one is already
        // unresolved" needs to hold, enforced in NdrCreationService.CreateFromFailedAttemptAsync
        // (Task 5), not a DB constraint.

        // No FK to Shipment: Ndr has a project reference to Shipments only for
        // IShipmentTransitionService (Application layer), not its EF model — same cross-module
        // precedent as ManifestShipment.ShipmentId (Phase 7) and DeliveryTask.ShipmentId (Phase 8).
    }
}
```

- [ ] **Step 2: `ReturnShipment`**

`src/Modules/Ndr/Infrastructure/ReturnShipmentConfiguration.cs`:

```csharp
using Courier.Branches.Domain;
using Courier.Ndr.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Ndr.Infrastructure;

public class ReturnShipmentConfiguration : IEntityTypeConfiguration<ReturnShipment>
{
    public void Configure(EntityTypeBuilder<ReturnShipment> builder)
    {
        builder.ToTable("ReturnShipments");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ReturnAwb).HasMaxLength(30).IsRequired();
        builder.Property(r => r.RtoReason).HasMaxLength(500).IsRequired();

        builder.HasIndex(r => r.OrganizationId);
        // A shipment can only ever reach RTO_INITIATED once (there is no transition back from
        // RtoInitiated/RtoInTransit/RtoAtOrigin/RtoDelivered to NdrCreated), so at most one
        // ReturnShipment per original shipment ever exists.
        builder.HasIndex(r => r.OriginalShipmentId).IsUnique();
        // ReturnAwb is unique per org, same shape as Shipment.Awb/Manifest.Code — also the index
        // GenerateReturnAwbAsync's collision-retry relies on catching a violation of.
        builder.HasIndex(r => new { r.OrganizationId, r.ReturnAwb }).IsUnique();

        builder.HasOne<Branch>().WithMany().HasForeignKey(r => r.OriginBranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(r => r.DestinationBranchId).OnDelete(DeleteBehavior.Restrict);

        // No FK to Shipment (OriginalShipmentId) — same cross-module reasoning as NdrConfiguration
        // above. No FK to Organization here: wired centrally in
        // Persistence/AppDbContext.OnModelCreating (Task 4), same as every other module.
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Modules/Ndr/Ndr.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Ndr/Infrastructure/
git commit -m "feat(ndr): add EF Core configurations for Ndr module entities"
```

---

## Task 4: Persistence wiring — `AppDbContext`, `Persistence.csproj`, `Api.csproj`, `Courier.slnx`, EF migration

**Files:**
- Modify: `src/Persistence/AppDbContext.cs`
- Modify: `src/Persistence/Persistence.csproj`
- Modify: `src/Api/Api.csproj`
- Modify: `Courier.slnx`
- Create (generated): `src/Persistence/Migrations/*_AddNdr.cs` and `.Designer.cs`, updated `AppDbContextModelSnapshot.cs`

**Interfaces:**
- Produces: `AppDbContext.Ndrs/ReturnShipments` `DbSet<T>` properties Tasks 5–7/11's `Ndr.Application` services use via `_dbContext.Set<T>()`.

- [ ] **Step 1: Add usings and `DbSet`s**

In `src/Persistence/AppDbContext.cs`, add after the `Riders`-module usings:

```csharp
using Courier.Ndr.Domain;
using Courier.Ndr.Infrastructure;
```

Add after the existing `DbSet<DeliveryProof> DeliveryProofs` line:

```csharp
    public DbSet<Ndr> Ndrs => Set<Ndr>();
    public DbSet<ReturnShipment> ReturnShipments => Set<ReturnShipment>();
```

- [ ] **Step 2: Apply the two configurations**

Add after the existing `modelBuilder.ApplyConfiguration(new DeliveryProofConfiguration());` line:

```csharp
        modelBuilder.ApplyConfiguration(new NdrConfiguration());
        modelBuilder.ApplyConfiguration(new ReturnShipmentConfiguration());
```

- [ ] **Step 3: Centralized Organization FK wiring**

Add after the existing `Rider`/`Vehicle`/`DeliveryAttempt`/`DeliveryProof` `HasOne<Organization>()` block:

```csharp
        // Same reasoning for Ndr/ReturnShipment.OrganizationId: Ndr has no project reference to
        // Identity.
        modelBuilder.Entity<Ndr>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(n => n.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ReturnShipment>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 4: Tenant query filters**

Add after the existing `Rider`/`Vehicle`/`DeliveryAttempt`/`DeliveryProof` `HasQueryFilter` lines:

```csharp
        modelBuilder.Entity<Ndr>().HasQueryFilter(n => _currentTenantProvider.OrganizationId != null && n.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<ReturnShipment>().HasQueryFilter(r => _currentTenantProvider.OrganizationId != null && r.OrganizationId == _currentTenantProvider.OrganizationId);
```

- [ ] **Step 5: Add project references**

In `src/Persistence/Persistence.csproj`, add after the existing `<ProjectReference Include="..\Modules\Riders\Riders.csproj" />` line:

```xml
    <ProjectReference Include="..\Modules\Ndr\Ndr.csproj" />
```

In `src/Api/Api.csproj`, add after the same `Riders.csproj` reference line:

```xml
    <ProjectReference Include="..\Modules\Ndr\Ndr.csproj" />
```

In `Courier.slnx`, add inside the `/src/Modules/` folder, after the `Riders.csproj` entry:

```xml
    <Project Path="src/Modules/Ndr/Ndr.csproj" />
```

- [ ] **Step 6: Build to confirm the model wires up**

Run: `dotnet build src/Api/Api.csproj`
Expected: Build succeeds.

- [ ] **Step 7: Generate the migration**

Run: `dotnet ef migrations add AddNdr --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: A new migration file is created under `src/Persistence/Migrations/` creating `Ndrs` and `ReturnShipments` tables with the FKs/indexes from Task 3, and `AppDbContextModelSnapshot.cs` is updated. Confirm by reading the generated migration file that no unrelated tables changed.

- [ ] **Step 8: Apply the migration against the shared local Postgres**

Run: `docker exec courier-postgres-1 pg_isready -U courier` to confirm the shared container (port 5434) is up first — do not `docker compose up` a second one. Then:
`dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: Migration applies cleanly with no errors.

- [ ] **Step 9: Commit**

```bash
git add src/Persistence/AppDbContext.cs src/Persistence/Persistence.csproj src/Api/Api.csproj Courier.slnx src/Persistence/Migrations/
git commit -m "feat(ndr): wire Ndr module into AppDbContext with migration"
```

---

## Task 5: Ndr Application — `INdrCreationService`

**Files:**
- Create: `src/Modules/Ndr/Application/INdrCreationService.cs`
- Create: `src/Modules/Ndr/Application/NdrCreationService.cs`
- Modify: `src/Api/Program.cs`
- Create: `tests/Ndr.Tests/Ndr.Tests.csproj`
- Test: `tests/Ndr.Tests/NdrCreationServiceTests.cs`

**Interfaces:**
- Consumes: `IShipmentTransitionService.TransitionToNdrCreatedAsync` (Task 1).
- Produces: `INdrCreationService.CreateFromFailedAttemptAsync(Guid shipmentId, Guid organizationId, NdrReason reason, ClaimsPrincipal actingUser, CancellationToken) -> Task` — Task 6's `RiderService.RecordFailedAttemptAsync` consumes this (the one and only caller this phase).

- [ ] **Step 1: Create the test project**

`tests/Ndr.Tests/Ndr.Tests.csproj` — mirrors `tests/Riders.Tests/Riders.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\Modules\Ndr\Ndr.csproj" />
  </ItemGroup>

</Project>
```

Add it to `Courier.slnx`'s `/tests/` folder, after the `Riders.Tests.csproj` entry: `<Project Path="tests/Ndr.Tests/Ndr.Tests.csproj" />`.

- [ ] **Step 2: `INdrCreationService`**

`src/Modules/Ndr/Application/INdrCreationService.cs`:

```csharp
using System.Security.Claims;
using Courier.Ndr.Domain;

namespace Courier.Ndr.Application;

// Called exactly once this phase: Riders.Application.RiderService.RecordFailedAttemptAsync, right
// after Shipments.Application.IShipmentTransitionService.FailDeliveryAsync, in the same request —
// the narrow cross-module-write interface pattern established by IUserProvisioningService (Phase
// 8) and IShipmentTransitionService (Phase 7) itself.
public interface INdrCreationService
{
    Task CreateFromFailedAttemptAsync(Guid shipmentId, Guid organizationId, NdrReason reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Implement it**

`src/Modules/Ndr/Application/NdrCreationService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Ndr.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public class NdrCreationService : INdrCreationService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly IShipmentTransitionService _shipmentTransitionService;

    public NdrCreationService(DbContext dbContext, IAuditLogger auditLogger, IShipmentTransitionService shipmentTransitionService)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _shipmentTransitionService = shipmentTransitionService;
    }

    public async Task CreateFromFailedAttemptAsync(Guid shipmentId, Guid organizationId, NdrReason reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        // Transition first, then persist the NDR record — same "transition, then record" ordering
        // RiderService.AssignTaskAsync (Phase 8) already established for cross-module writes.
        await _shipmentTransitionService.TransitionToNdrCreatedAsync(shipmentId, actingUser, cancellationToken);

        var ndr = new Ndr
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ShipmentId = shipmentId,
            Reason = reason,
            Status = NdrStatus.Open
        };
        _dbContext.Set<Ndr>().Add(ndr);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "ndr.created", "Ndr", ndr.Id.ToString()), cancellationToken);
    }
}
```

- [ ] **Step 4: Wire into `Program.cs`**

In `src/Api/Program.cs`, add `using Courier.Ndr.Application;` to the usings, and after the existing `builder.Services.AddScoped<RiderService>();` line:

```csharp
builder.Services.AddScoped<INdrCreationService, NdrCreationService>();
```

(`RiderService` is registered before `INdrCreationService` here even though `RiderService` will consume it starting in Task 6 — DI resolves constructor dependencies at request time, not registration time, so registration order between two `AddScoped` calls never matters in ASP.NET Core's container.)

- [ ] **Step 5: Write the failing test**

Create `tests/Ndr.Tests/NdrCreationServiceTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.Ndr.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Ndr.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

// Not file-scoped: needs its call-recording state inspected by name from test methods, and (unlike
// this project's usual "returned from a tuple" reason) simply keeping every fake in this small file
// consistently non-file-scoped avoids a second, inconsistent rule to remember.
class RecordingShipmentTransitionService : IShipmentTransitionService
{
    public List<Guid> TransitionToNdrCreatedCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        TransitionToNdrCreatedCalls.Add(shipmentId);
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class NdrCreationServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new NdrConfiguration());
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

    [Fact]
    public async Task CreateFromFailedAttemptAsync_TransitionsShipmentThenCreatesOpenNdr()
    {
        await using var dbContext = CreateContext();
        var shipments = new RecordingShipmentTransitionService();
        var service = new NdrCreationService(dbContext, new NoopAuditLogger(), shipments);
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();

        await service.CreateFromFailedAttemptAsync(shipmentId, organizationId, NdrReason.CustomerUnavailable, ActingUser(Guid.NewGuid()));

        Assert.Single(shipments.TransitionToNdrCreatedCalls, shipmentId);
        var ndr = await dbContext.Set<Ndr>().SingleAsync(n => n.ShipmentId == shipmentId);
        Assert.Equal(organizationId, ndr.OrganizationId);
        Assert.Equal(NdrReason.CustomerUnavailable, ndr.Reason);
        Assert.Equal(NdrStatus.Open, ndr.Status);
    }
}
```

- [ ] **Step 6: Run the test**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj`
Expected: The one test passes.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Ndr/Application/INdrCreationService.cs src/Modules/Ndr/Application/NdrCreationService.cs src/Api/Program.cs Courier.slnx tests/Ndr.Tests/
git commit -m "feat(ndr): add NdrCreationService, auto-creating an Open NDR from a failed delivery attempt"
```

---

## Task 6: Wire `Riders` → `Ndr` — auto-create the NDR from a failed delivery attempt

Adding a constructor parameter to `RiderService` breaks every existing `new RiderService(...)` call site — four test files. This task fixes all of them in the same commit, same reasoning as Task 1.

**Files:**
- Modify: `src/Modules/Riders/Riders.csproj`
- Modify: `src/Modules/Riders/Application/RiderService.cs`
- Modify: `src/Modules/Riders/Application/RiderService.Attempts.cs`
- Modify: `tests/Riders.Tests/RiderServiceCreationTests.cs`
- Modify: `tests/Riders.Tests/RiderServiceTasksTests.cs`
- Modify: `tests/Riders.Tests/RiderServiceReadsTests.cs`
- Modify: `tests/Riders.Tests/RiderServiceAttemptsTests.cs`

**Interfaces:**
- Consumes: `INdrCreationService.CreateFromFailedAttemptAsync` (Task 5).

- [ ] **Step 1: Add the project reference**

In `src/Modules/Riders/Riders.csproj`, add after the existing `<ProjectReference Include="..\Shipments\Shipments.csproj" />` line:

```xml
    <ProjectReference Include="..\..\Modules\Ndr\Ndr.csproj" />
```

- [ ] **Step 2: Add the dependency to `RiderService`'s constructor**

In `src/Modules/Riders/Application/RiderService.cs`, add `using Courier.Ndr.Application;` to the usings. Add a new field after `_objectStorage`:

```csharp
    private readonly INdrCreationService _ndrCreationService;
```

Add `INdrCreationService ndrCreationService` as the constructor's last parameter, and `_ndrCreationService = ndrCreationService;` as the last assignment in the constructor body — the full constructor becomes:

```csharp
    public RiderService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IUserProvisioningService userProvisioningService,
        IBranchLookupService branchLookupService,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IShipmentTransitionService shipmentTransitionService,
        IObjectStorage objectStorage,
        INdrCreationService ndrCreationService)
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
    }
```

- [ ] **Step 3: Call it from `RecordFailedAttemptAsync`**

In `src/Modules/Riders/Application/RiderService.Attempts.cs`, add `using Courier.Ndr.Domain;` to the usings. Insert the new call immediately after the existing `await _shipmentTransitionService.FailDeliveryAsync(request.ShipmentId, riderId, actingUser, cancellationToken);` line and before `var attempt = new DeliveryAttempt`:

```csharp
        // Transition first, then persist the attempt record — same "transition, then record"
        // ordering as AssignTaskAsync (Task 8).
        await _shipmentTransitionService.FailDeliveryAsync(request.ShipmentId, riderId, actingUser, cancellationToken);

        // Phase 9: DELIVERY_FAILED -> NDR_CREATED happens automatically, in the same request, right
        // after the failure transition above — SHIPMENT-LIFECYCLE.md's diagram shows this as a
        // system step, not a separate manual action.
        await _ndrCreationService.CreateFromFailedAttemptAsync(request.ShipmentId, organizationId, ToNdrReason(request.Reason), actingUser, cancellationToken);

        var attempt = new DeliveryAttempt
```

Add the reason-conversion helper as a new private static method at the bottom of the class, right before the closing `}`:

```csharp
    // Explicit switch, not a raw int cast — Riders.Domain.DeliveryAttemptReason and
    // Ndr.Domain.NdrReason are deliberately separate, identically-valued enums (see NdrReason.cs's
    // doc comment); a switch makes a future divergence between the two lists a compile error
    // instead of a silent miscast.
    private static NdrReason ToNdrReason(DeliveryAttemptReason reason) => reason switch
    {
        DeliveryAttemptReason.CustomerUnavailable => NdrReason.CustomerUnavailable,
        DeliveryAttemptReason.PhoneUnreachable => NdrReason.PhoneUnreachable,
        DeliveryAttemptReason.WrongAddress => NdrReason.WrongAddress,
        DeliveryAttemptReason.IncompleteAddress => NdrReason.IncompleteAddress,
        DeliveryAttemptReason.CustomerRefused => NdrReason.CustomerRefused,
        DeliveryAttemptReason.CustomerRequestedReschedule => NdrReason.CustomerRequestedReschedule,
        DeliveryAttemptReason.CashUnavailable => NdrReason.CashUnavailable,
        DeliveryAttemptReason.DamagedParcel => NdrReason.DamagedParcel,
        DeliveryAttemptReason.Other => NdrReason.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };
```

- [ ] **Step 4: Fix the three `RiderService` fakes that never exercise `RecordFailedAttemptAsync`**

In each of `tests/Riders.Tests/RiderServiceCreationTests.cs`, `tests/Riders.Tests/RiderServiceTasksTests.cs`, `tests/Riders.Tests/RiderServiceReadsTests.cs`: add `using Courier.Ndr.Application;` and `using Courier.Ndr.Domain;` to the usings, add this file-scoped fake (none of these three files' tests ever reach `RecordFailedAttemptAsync`, so a throwing stub is safe):

```csharp
file class NotSupportedNdrCreationService : INdrCreationService
{
    public Task CreateFromFailedAttemptAsync(Guid shipmentId, Guid organizationId, NdrReason reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
```

Then add `new NotSupportedNdrCreationService()` as the last argument to each file's `RiderService` construction:
- `RiderServiceCreationTests.cs`'s `CreateService` helper: `new RiderService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), provisioning, new FakeBranchLookup(knownBranchId), new AllBranchesAuthorizationChecker(), new NotSupportedShipmentTransitionService(), new NullObjectStorage(), new NotSupportedNdrCreationService());`
- `RiderServiceTasksTests.cs`'s `SeedRiderAsync` helper: `new RiderService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(), new NotSupportedBranchLookup(), new AllBranchesAuthorizationChecker(), shipments, new NotSupportedObjectStorage(), new NotSupportedNdrCreationService());`
- `RiderServiceReadsTests.cs`'s `CreateService` helper: `new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(), new NotSupportedBranchLookup(), checker, new NotSupportedShipmentTransitionService(), new NotSupportedObjectStorage(), new NotSupportedNdrCreationService());`

- [ ] **Step 5: Fix `RiderServiceAttemptsTests.cs` with a real recording fake and new assertions**

This file's tests DO exercise `RecordFailedAttemptAsync`, so its fake must actually record the call (a throwing stub would turn every existing passing test into a failure). Add `using Courier.Ndr.Application;` and `using Courier.Ndr.Domain;` to the usings. Add this fake (not file-scoped — returned from `SeedAsync`'s tuple, same reasoning as `AttemptsFakeShipmentTransitionService`):

```csharp
// Not file-scoped: returned from SeedAsync's tuple.
class RecordingNdrCreationService : INdrCreationService
{
    public List<(Guid ShipmentId, Guid OrganizationId, NdrReason Reason)> Calls { get; } = new();
    public Task CreateFromFailedAttemptAsync(Guid shipmentId, Guid organizationId, NdrReason reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        Calls.Add((shipmentId, organizationId, reason));
        return Task.CompletedTask;
    }
}
```

Change `SeedAsync`'s signature and body to construct and return it:

```csharp
    private static async Task<(RiderService Service, AttemptsFakeShipmentTransitionService Shipments, RecordingObjectStorage Storage, RecordingNdrCreationService Ndr, Guid RiderId, Guid OrganizationId)> SeedAsync(TestDbContext dbContext)
    {
        var organizationId = Guid.NewGuid();
        var rider = new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = Guid.NewGuid(), BaseBranchId = Guid.NewGuid(), LicenseNumber = "LIC-001", RoleType = RiderRoleType.DeliveryRider, IsActive = true };
        dbContext.Set<Rider>().Add(rider);
        await dbContext.SaveChangesAsync();

        var shipments = new AttemptsFakeShipmentTransitionService();
        var storage = new RecordingObjectStorage();
        var ndr = new RecordingNdrCreationService();
        var service = new RiderService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), new AllBranchesAuthorizationChecker(), shipments, storage, ndr);
        return (service, shipments, storage, ndr, rider.Id, organizationId);
    }
```

Update every call site destructuring this tuple to add the new `ndr` element — the four existing test methods (`RecordFailedAttemptAsync_ValidRequest_CreatesFailedAttemptAndFailsTask`, `RecordFailedAttemptAsync_ShipmentNotAssignedToThisRider_ThrowsRiderValidationException`, `CompleteDeliveryAsync_ValidRequest_CreatesProofAndCompletesTask`, `RecordFailedAttemptAsync_SecondAttemptOnSameShipment_IncrementsAttemptNumber`) each start with `var (service, shipments, _, riderId, _) = await SeedAsync(dbContext);` or similar — change every one to `var (service, shipments, _, _, riderId, _) = await SeedAsync(dbContext);` (inserting one more `_` for the new `Ndr` tuple element; `CompleteDeliveryAsync_ValidRequest_CreatesProofAndCompletesTask` additionally destructures `storage`, so its line becomes `var (service, shipments, storage, _, riderId, _) = await SeedAsync(dbContext);`).

Add two new tests:

```csharp
    [Fact]
    public async Task RecordFailedAttemptAsync_ValidRequest_CreatesOpenNdrWithMappedReason()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, _, ndr, riderId, organizationId) = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(riderId);
        shipments.ShipmentsById[info.ShipmentId] = info;

        await service.RecordFailedAttemptAsync(riderId,
            new RecordFailedAttemptRequest(info.ShipmentId, DeliveryAttemptReason.WrongAddress, null, null, null, false, null),
            ActingUser(Guid.NewGuid()));

        var call = Assert.Single(ndr.Calls);
        Assert.Equal(info.ShipmentId, call.ShipmentId);
        Assert.Equal(organizationId, call.OrganizationId);
        Assert.Equal(NdrReason.WrongAddress, call.Reason);
    }

    [Fact]
    public async Task CompleteDeliveryAsync_ValidRequest_NeverCreatesAnNdr()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, _, ndr, riderId, _) = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(riderId);
        shipments.ShipmentsById[info.ShipmentId] = info;
        dbContext.Set<DeliveryTask>().Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = Guid.NewGuid(), ShipmentId = info.ShipmentId, Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending });
        await dbContext.SaveChangesAsync();

        await service.CompleteDeliveryAsync(riderId,
            new CompleteDeliveryRequest(info.ShipmentId, "Jane Doe", true, null, null, null, null),
            ActingUser(Guid.NewGuid()));

        Assert.Empty(ndr.Calls);
    }
```

- [ ] **Step 6: Run the whole solution's build and tests**

Run: `dotnet build`
Expected: Build succeeds.

Run: `dotnet test`
Expected: Every test project passes — `Riders.Tests` gains 2 new tests.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Riders/Riders.csproj src/Modules/Riders/Application/RiderService.cs src/Modules/Riders/Application/RiderService.Attempts.cs tests/Riders.Tests/RiderServiceCreationTests.cs tests/Riders.Tests/RiderServiceTasksTests.cs tests/Riders.Tests/RiderServiceReadsTests.cs tests/Riders.Tests/RiderServiceAttemptsTests.cs
git commit -m "feat(riders): auto-create an NDR immediately after every failed delivery attempt"
```

---

## Task 7: Ndr Application — `NdrService` (update/resolve workflow, `ReturnShipment` creation, NDR reads)

`NdrService` is the single service class for the whole `Ndr` module, covering both `NDR` and `ReturnShipment` operations — mirrors `RiderService` covering `Rider`/`Vehicle`/`DeliveryTask`/`DeliveryAttempt`/`DeliveryProof` together in one partial class across several files.

**Files:**
- Create: `src/Modules/Ndr/Application/NdrDtos.cs`
- Create: `src/Modules/Ndr/Application/NdrService.cs`
- Create: `src/Modules/Ndr/Application/NdrService.Resolve.cs`
- Create: `src/Modules/Ndr/Application/NdrService.Reads.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Ndr.Tests/NdrServiceUpdateTests.cs`
- Test: `tests/Ndr.Tests/NdrServiceResolveTests.cs`

**Interfaces:**
- Consumes: `IShipmentTransitionService.GetScanInfoAsync/ReattemptDeliveryAsync/InitiateRtoAsync` (Task 1).
- Produces: `NdrService.UpdateAsync(Guid ndrId, UpdateNdrRequest, ClaimsPrincipal, CancellationToken) -> Task<NdrResponse?>`, `NdrService.ResolveAsync(Guid ndrId, ResolveNdrRequest, ClaimsPrincipal, CancellationToken) -> Task<NdrResponse?>`, `NdrService.GetByIdAsync(Guid, CancellationToken) -> Task<NdrResponse?>`, `NdrService.ListAsync(NdrStatus?, CancellationToken) -> Task<List<NdrResponse>>` — Task 8's Presentation layer and Task 12's tests call all four.

- [ ] **Step 1: DTOs and exceptions**

`src/Modules/Ndr/Application/NdrDtos.cs`:

```csharp
using Courier.Ndr.Domain;

namespace Courier.Ndr.Application;

public record UpdateNdrRequest(NdrStatus Status, string? CustomerResponse, string? NextAction, DateTimeOffset? FollowUpAt);

public record ResolveNdrRequest(NdrResolution Resolution, string? RtoReason);

public record NdrResponse(
    Guid Id, Guid ShipmentId, NdrReason Reason, Guid? AssignedStaffUserId, NdrStatus Status,
    string? CustomerResponse, DateTimeOffset? FollowUpAt, string? NextAction, NdrResolution? Resolution, DateTimeOffset? ResolvedAt);

public record ReturnShipmentResponse(
    Guid Id, Guid OriginalShipmentId, string ReturnAwb, string RtoReason, Guid OriginBranchId, Guid DestinationBranchId,
    ShipmentStatus CurrentStatus, Guid? CurrentBranchId, DateTimeOffset? MerchantConfirmedAt);

public class NdrValidationException : Exception
{
    public NdrValidationException(string message) : base(message) { }
}

public class NdrNotFoundException : Exception
{
    public NdrNotFoundException(Guid ndrId) : base($"NDR {ndrId} was not found.")
    {
        NdrId = ndrId;
    }

    public Guid NdrId { get; }
}
```

`ReturnShipmentResponse` needs `Courier.Shipments.Domain.ShipmentStatus` — add `using Courier.Shipments.Domain;` to the top of this file too.

- [ ] **Step 2: `NdrService` core — constructor, `UpdateAsync`, shared helpers**

`src/Modules/Ndr/Application/NdrService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Ndr.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public partial class NdrService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public NdrService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IShipmentTransitionService shipmentTransitionService,
        IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _shipmentTransitionService = shipmentTransitionService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    // Legal forward moves only (CLAUDE.md §20's status list, this plan's resolved design decision):
    // Open -> Contacting -> CustomerConfirmed -> {ReattemptScheduled | RtoRequested}. No backward or
    // skipped moves, and Resolved is never a legal target here — only ResolveAsync sets it, as the
    // side effect of a real resolution action, never as a plain status edit.
    private static bool IsLegalForwardMove(NdrStatus from, NdrStatus to) => (from, to) switch
    {
        (NdrStatus.Open, NdrStatus.Contacting) => true,
        (NdrStatus.Contacting, NdrStatus.CustomerConfirmed) => true,
        (NdrStatus.CustomerConfirmed, NdrStatus.ReattemptScheduled) => true,
        (NdrStatus.CustomerConfirmed, NdrStatus.RtoRequested) => true,
        _ => false
    };

    public async Task<NdrResponse?> UpdateAsync(Guid ndrId, UpdateNdrRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new NdrValidationException("No active organization context.");

        var ndr = await _dbContext.Set<Ndr>().FirstOrDefaultAsync(n => n.Id == ndrId, cancellationToken);
        if (ndr is null)
        {
            return null;
        }

        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(ndr.ShipmentId, cancellationToken)
            ?? throw new NdrValidationException("The NDR's shipment was not found.");
        // Check 1 (permission) enforced at the route via .RequirePermission("ndr.manage"); check 2
        // (branch access) here, scoped to the shipment's destination branch — the branch that
        // attempted delivery and owns following up on the customer.
        await CheckBranchAccessAsync(actingUser, "ndr.manage", scanInfo.DestinationBranchId, cancellationToken);

        if (!IsLegalForwardMove(ndr.Status, request.Status))
        {
            throw new NdrValidationException($"Cannot move NDR from {ndr.Status} to {request.Status}.");
        }

        ndr.Status = request.Status;
        if (request.CustomerResponse is not null)
        {
            ndr.CustomerResponse = request.CustomerResponse;
        }
        if (request.NextAction is not null)
        {
            ndr.NextAction = request.NextAction;
        }
        if (request.FollowUpAt is not null)
        {
            ndr.FollowUpAt = request.FollowUpAt;
        }
        ndr.AssignedStaffUserId ??= actingUser.GetUserId();

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "ndr.updated", "Ndr", ndr.Id.ToString()), cancellationToken);
        return ToResponse(ndr);
    }

    private async Task CheckBranchAccessAsync(ClaimsPrincipal actingUser, string permission, Guid branchId, CancellationToken cancellationToken)
    {
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, permission, cancellationToken);
        if (accessible is not null && !accessible.Contains(branchId))
        {
            throw new NdrValidationException($"Branch is outside your branch access for permission '{permission}'.");
        }
    }

    private static NdrResponse ToResponse(Ndr ndr) => new(
        ndr.Id, ndr.ShipmentId, ndr.Reason, ndr.AssignedStaffUserId, ndr.Status,
        ndr.CustomerResponse, ndr.FollowUpAt, ndr.NextAction, ndr.Resolution, ndr.ResolvedAt);
}
```

- [ ] **Step 3: `ResolveAsync`**

`src/Modules/Ndr/Application/NdrService.Resolve.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Ndr.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public partial class NdrService
{
    public async Task<NdrResponse?> ResolveAsync(Guid ndrId, ResolveNdrRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new NdrValidationException("No active organization context.");

        var ndr = await _dbContext.Set<Ndr>().FirstOrDefaultAsync(n => n.Id == ndrId, cancellationToken);
        if (ndr is null)
        {
            return null;
        }
        if (ndr.Status == NdrStatus.Resolved)
        {
            throw new NdrValidationException("This NDR is already resolved.");
        }

        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(ndr.ShipmentId, cancellationToken)
            ?? throw new NdrValidationException("The NDR's shipment was not found.");
        // Check 1 (permission) enforced at the route via .RequirePermission("ndr.resolve"); check 2
        // (branch access) here.
        await CheckBranchAccessAsync(actingUser, "ndr.resolve", scanInfo.DestinationBranchId, cancellationToken);

        switch (request.Resolution)
        {
            case NdrResolution.Reattempt:
                if (ndr.Status != NdrStatus.ReattemptScheduled)
                {
                    throw new NdrValidationException($"NDR must be ReattemptScheduled to resolve with Reattempt, was {ndr.Status}.");
                }
                await _shipmentTransitionService.ReattemptDeliveryAsync(ndr.ShipmentId, actingUser, cancellationToken);
                break;

            case NdrResolution.Rto:
            case NdrResolution.AutoRto:
                if (ndr.Status != NdrStatus.RtoRequested)
                {
                    throw new NdrValidationException($"NDR must be RtoRequested to resolve with {request.Resolution}, was {ndr.Status}.");
                }
                if (string.IsNullOrWhiteSpace(request.RtoReason))
                {
                    throw new NdrValidationException("RtoReason is required when resolving with Rto or AutoRto.");
                }
                await _shipmentTransitionService.InitiateRtoAsync(ndr.ShipmentId, actingUser, cancellationToken);
                await CreateReturnShipmentAsync(ndr.ShipmentId, organizationId, scanInfo.OriginBranchId, scanInfo.DestinationBranchId, request.RtoReason, cancellationToken);
                break;

            case NdrResolution.Other:
                // Allowed from any non-Resolved status — a catch-all for outcomes CLAUDE.md §20's
                // status/resolution lists don't otherwise name (e.g. the shipment was lost, or a
                // merchant-side cancellation makes the NDR moot). No shipment-side transition.
                break;

            default:
                throw new NdrValidationException("Unknown resolution.");
        }

        ndr.Status = NdrStatus.Resolved;
        ndr.Resolution = request.Resolution;
        ndr.ResolvedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), $"ndr.resolved_{request.Resolution.ToString().ToLowerInvariant()}", "Ndr", ndr.Id.ToString()), cancellationToken);
        return ToResponse(ndr);
    }

    // Note the swap: the ORIGINAL shipment's DestinationBranchId (where the failed delivery was
    // attempted) becomes the return leg's OriginBranchId, and the original's OriginBranchId becomes
    // the return leg's destination — the parcel is physically headed back the way it came.
    private async Task CreateReturnShipmentAsync(Guid originalShipmentId, Guid organizationId, Guid originalOriginBranchId, Guid originalDestinationBranchId, string rtoReason, CancellationToken cancellationToken)
    {
        // Insert-and-catch-DbUpdateException-retry on the unique (OrganizationId, ReturnAwb) index —
        // same race-handling shape as Shipment.Awb/Manifest.Code generation (Phases 6-7).
        const int maxAwbAttempts = 3;
        for (var attempt = 1; attempt <= maxAwbAttempts; attempt++)
        {
            var returnAwb = await GenerateReturnAwbAsync(organizationId, cancellationToken);
            var returnShipment = new ReturnShipment
            {
                Id = Guid.NewGuid(),
                OriginalShipmentId = originalShipmentId,
                OrganizationId = organizationId,
                ReturnAwb = returnAwb,
                RtoReason = rtoReason,
                OriginBranchId = originalDestinationBranchId,
                DestinationBranchId = originalOriginBranchId
            };
            _dbContext.Set<ReturnShipment>().Add(returnShipment);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException) when (attempt < maxAwbAttempts)
            {
                _dbContext.Entry(returnShipment).State = EntityState.Detached;
            }
        }
    }

    private async Task<string> GenerateReturnAwbAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var datePart = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var awbPrefix = $"RTO-{datePart}-";

        var existingAwbs = await _dbContext.Set<ReturnShipment>()
            .Where(r => r.OrganizationId == organizationId && r.ReturnAwb.StartsWith(awbPrefix))
            .Select(r => r.ReturnAwb)
            .ToListAsync(cancellationToken);
        var nextSequence = existingAwbs.Count == 0
            ? 1
            : existingAwbs.Select(a => int.Parse(a[^3..])).Max() + 1;

        return $"{awbPrefix}{nextSequence:D3}";
    }
}
```

- [ ] **Step 4: NDR reads**

`src/Modules/Ndr/Application/NdrService.Reads.cs`:

```csharp
using Courier.Ndr.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public partial class NdrService
{
    public async Task<NdrResponse?> GetByIdAsync(Guid ndrId, CancellationToken cancellationToken = default)
    {
        var ndr = await _dbContext.Set<Ndr>().FirstOrDefaultAsync(n => n.Id == ndrId, cancellationToken);
        return ndr is null ? null : ToResponse(ndr);
    }

    public async Task<List<NdrResponse>> ListAsync(NdrStatus? status, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Ndr>().AsQueryable();
        if (status is not null)
        {
            query = query.Where(n => n.Status == status);
        }

        return await query
            .Select(n => new NdrResponse(n.Id, n.ShipmentId, n.Reason, n.AssignedStaffUserId, n.Status, n.CustomerResponse, n.FollowUpAt, n.NextAction, n.Resolution, n.ResolvedAt))
            .ToListAsync(cancellationToken);
    }
}
```

- [ ] **Step 5: Wire into `Program.cs`**

In `src/Api/Program.cs`, add `using Courier.Ndr.Application;` (already added in Task 5 — confirm, don't duplicate) and after `builder.Services.AddScoped<INdrCreationService, NdrCreationService>();`:

```csharp
builder.Services.AddScoped<NdrService>();
```

- [ ] **Step 6: Write the failing tests**

Create `tests/Ndr.Tests/NdrServiceUpdateTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.Ndr.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Ndr.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class FakeShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class NdrServiceUpdateTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new NdrConfiguration());
            modelBuilder.ApplyConfiguration(new ReturnShipmentConfiguration());
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

    private static ShipmentScanInfo Info(Guid shipmentId, Guid originBranchId, Guid destinationBranchId, ShipmentStatus status = ShipmentStatus.NdrCreated) => new(
        shipmentId, Guid.NewGuid(), "AWB001", status, null, originBranchId, destinationBranchId, null);

    private static async Task<(Ndr.Domain.Ndr Entity, NdrService Service, FakeShipmentTransitionService Shipments)> SeedOpenNdrAsync(TestDbContext dbContext, Guid organizationId, ShipmentScanInfo info)
    {
        var ndr = new Ndr.Domain.Ndr { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = info.ShipmentId, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open };
        dbContext.Set<Ndr.Domain.Ndr>().Add(ndr);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[info.ShipmentId] = info;
        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());
        return (ndr, service, shipments);
    }

    [Fact]
    public async Task UpdateAsync_OpenToContacting_Succeeds()
    {
        await using var dbContext = CreateContext();
        var info = Info(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (ndr, service, _) = await SeedOpenNdrAsync(dbContext, Guid.NewGuid(), info);

        var result = await service.UpdateAsync(ndr.Id, new UpdateNdrRequest(NdrStatus.Contacting, "No answer", null, null), ActingUser(Guid.NewGuid()));

        Assert.Equal(NdrStatus.Contacting, result!.Status);
        Assert.Equal("No answer", result.CustomerResponse);
    }

    [Fact]
    public async Task UpdateAsync_SkippedMove_ThrowsNdrValidationException()
    {
        await using var dbContext = CreateContext();
        var info = Info(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (ndr, service, _) = await SeedOpenNdrAsync(dbContext, Guid.NewGuid(), info);

        await Assert.ThrowsAsync<NdrValidationException>(() => service.UpdateAsync(ndr.Id, new UpdateNdrRequest(NdrStatus.CustomerConfirmed, null, null, null), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task UpdateAsync_BackwardMove_ThrowsNdrValidationException()
    {
        await using var dbContext = CreateContext();
        var info = Info(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (ndr, service, _) = await SeedOpenNdrAsync(dbContext, Guid.NewGuid(), info);
        await service.UpdateAsync(ndr.Id, new UpdateNdrRequest(NdrStatus.Contacting, null, null, null), ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<NdrValidationException>(() => service.UpdateAsync(ndr.Id, new UpdateNdrRequest(NdrStatus.Open, null, null, null), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = new NdrService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new FakeShipmentTransitionService(), new AllBranchesAuthorizationChecker());

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateNdrRequest(NdrStatus.Contacting, null, null, null), ActingUser(Guid.NewGuid()));

        Assert.Null(result);
    }
}
```

Create `tests/Ndr.Tests/NdrServiceResolveTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.Ndr.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Ndr.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class RecordingShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public List<Guid> ReattemptCalls { get; } = new();
    public List<Guid> InitiateRtoCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        ReattemptCalls.Add(shipmentId);
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        InitiateRtoCalls.Add(shipmentId);
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class NdrServiceResolveTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new NdrConfiguration());
            modelBuilder.ApplyConfiguration(new ReturnShipmentConfiguration());
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

    private static async Task<(Ndr.Domain.Ndr Entity, NdrService Service, RecordingShipmentTransitionService Shipments, Guid OriginBranchId, Guid DestinationBranchId)> SeedNdrAsync(
        TestDbContext dbContext, Guid organizationId, NdrStatus status)
    {
        var shipmentId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var ndr = new Ndr.Domain.Ndr { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = shipmentId, Reason = NdrReason.CustomerUnavailable, Status = status };
        dbContext.Set<Ndr.Domain.Ndr>().Add(ndr);
        await dbContext.SaveChangesAsync();

        var shipments = new RecordingShipmentTransitionService();
        shipments.ShipmentsById[shipmentId] = new ShipmentScanInfo(shipmentId, organizationId, "AWB001", ShipmentStatus.NdrCreated, null, originBranchId, destinationBranchId, null);
        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());
        return (ndr, service, shipments, originBranchId, destinationBranchId);
    }

    [Fact]
    public async Task ResolveAsync_ReattemptFromReattemptScheduled_TransitionsShipmentAndResolvesNdr()
    {
        await using var dbContext = CreateContext();
        var (ndr, service, shipments, _, _) = await SeedNdrAsync(dbContext, Guid.NewGuid(), NdrStatus.ReattemptScheduled);

        var result = await service.ResolveAsync(ndr.Id, new ResolveNdrRequest(NdrResolution.Reattempt, null), ActingUser(Guid.NewGuid()));

        Assert.Equal(NdrStatus.Resolved, result!.Status);
        Assert.Equal(NdrResolution.Reattempt, result.Resolution);
        Assert.NotNull(result.ResolvedAt);
        Assert.Single(shipments.ReattemptCalls, ndr.ShipmentId);
    }

    [Fact]
    public async Task ResolveAsync_ReattemptWhenNotReattemptScheduled_ThrowsNdrValidationException()
    {
        await using var dbContext = CreateContext();
        var (ndr, service, _, _, _) = await SeedNdrAsync(dbContext, Guid.NewGuid(), NdrStatus.Open);

        await Assert.ThrowsAsync<NdrValidationException>(() => service.ResolveAsync(ndr.Id, new ResolveNdrRequest(NdrResolution.Reattempt, null), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ResolveAsync_RtoFromRtoRequested_CreatesReturnShipmentWithSwappedBranches()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (ndr, service, shipments, originBranchId, destinationBranchId) = await SeedNdrAsync(dbContext, organizationId, NdrStatus.RtoRequested);

        var result = await service.ResolveAsync(ndr.Id, new ResolveNdrRequest(NdrResolution.Rto, "Customer refused after three contacts"), ActingUser(Guid.NewGuid()));

        Assert.Equal(NdrResolution.Rto, result!.Resolution);
        Assert.Single(shipments.InitiateRtoCalls, ndr.ShipmentId);
        var returnShipment = await dbContext.Set<Ndr.Domain.ReturnShipment>().SingleAsync(r => r.OriginalShipmentId == ndr.ShipmentId);
        Assert.Equal(destinationBranchId, returnShipment.OriginBranchId);
        Assert.Equal(originBranchId, returnShipment.DestinationBranchId);
        Assert.StartsWith("RTO-", returnShipment.ReturnAwb);
        Assert.Equal("Customer refused after three contacts", returnShipment.RtoReason);
    }

    [Fact]
    public async Task ResolveAsync_RtoWithoutReason_ThrowsNdrValidationException()
    {
        await using var dbContext = CreateContext();
        var (ndr, service, _, _, _) = await SeedNdrAsync(dbContext, Guid.NewGuid(), NdrStatus.RtoRequested);

        await Assert.ThrowsAsync<NdrValidationException>(() => service.ResolveAsync(ndr.Id, new ResolveNdrRequest(NdrResolution.Rto, null), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ResolveAsync_AlreadyResolved_ThrowsNdrValidationException()
    {
        await using var dbContext = CreateContext();
        var (ndr, service, _, _, _) = await SeedNdrAsync(dbContext, Guid.NewGuid(), NdrStatus.ReattemptScheduled);
        await service.ResolveAsync(ndr.Id, new ResolveNdrRequest(NdrResolution.Reattempt, null), ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<NdrValidationException>(() => service.ResolveAsync(ndr.Id, new ResolveNdrRequest(NdrResolution.Reattempt, null), ActingUser(Guid.NewGuid())));
    }
}
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj`
Expected: All 10 tests (5 update + 5 resolve) plus the earlier `NdrCreationServiceTests` pass — 11 total.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Ndr/Application/NdrDtos.cs src/Modules/Ndr/Application/NdrService.cs src/Modules/Ndr/Application/NdrService.Resolve.cs src/Modules/Ndr/Application/NdrService.Reads.cs src/Api/Program.cs tests/Ndr.Tests/NdrServiceUpdateTests.cs tests/Ndr.Tests/NdrServiceResolveTests.cs
git commit -m "feat(ndr): add NDR update/resolve workflow, creating a ReturnShipment on Rto/AutoRto resolution"
```

---

## Task 8: Ndr Presentation — NDR HTTP endpoints

**Files:**
- Create: `src/Modules/Ndr/Presentation/NdrEndpoints.cs`
- Modify: `src/Api/Program.cs`

**Interfaces:**
- Consumes: `NdrService.UpdateAsync/ResolveAsync/GetByIdAsync/ListAsync` (Task 7).
- Produces: the `/api/v1/ndrs` HTTP surface Task 12's API tests exercise.

- [ ] **Step 1: Write `NdrEndpoints.cs`**

```csharp
using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Ndr.Presentation;

public static class NdrEndpoints
{
    public static void MapNdrEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ndrs").RequireAuthorization();

        group.MapGet("/", async (NdrStatus? status, NdrService service, HttpContext httpContext) =>
            Results.Ok(await service.ListAsync(status, httpContext.RequestAborted)))
            .RequirePermission("ndr.read");

        group.MapGet("/{id:guid}", async (Guid id, NdrService service, HttpContext httpContext) =>
        {
            var result = await service.GetByIdAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("ndr.read");

        group.MapPost("/{id:guid}/update", async (Guid id, UpdateNdrRequest request, NdrService service, HttpContext httpContext) =>
            await HandleAsync(() => service.UpdateAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("ndr.manage");

        group.MapPost("/{id:guid}/resolve", async (Guid id, ResolveNdrRequest request, NdrService service, HttpContext httpContext) =>
            await HandleAsync(() => service.ResolveAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("ndr.resolve");
    }

    private static async Task<IResult> HandleAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            var result = await operation();
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (NdrValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "NDR_VALIDATION_FAILED", message = ex.Message } });
        }
        catch (ShipmentInvalidTransitionException ex)
        {
            return Results.BadRequest(new
            {
                error = new
                {
                    code = "SHIPMENT_INVALID_TRANSITION",
                    message = ex.Message,
                    details = new { shipmentId = ex.ShipmentId, from = ex.From.ToString(), to = ex.To.ToString() }
                }
            });
        }
        catch (ShipmentValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "SHIPMENT_VALIDATION_FAILED", message = ex.Message } });
        }
    }
}
```

Note: `HandleAsync<T>`'s `result is null ? Results.NotFound()` branch is unreachable for `UpdateAsync`/`ResolveAsync` when called through this route (the route's `{id:guid}` always resolves an existing `NdrService` call whose `null` return already means "not found" — same shape as `Riders`' identical `HandleAsync<T>` helper). This mirrors `RidersEndpoints.HandleAsync<T>` exactly.

- [ ] **Step 2: Wire into `Program.cs`**

In `src/Api/Program.cs`, add `using Courier.Ndr.Presentation;` to the usings, and after `app.MapRidersEndpoints();`:

```csharp
app.MapNdrEndpoints();
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Api/Api.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Ndr/Presentation/NdrEndpoints.cs src/Api/Program.cs
git commit -m "feat(ndr): add NDR HTTP endpoints"
```

---

## Task 9: Manifests — add `ManifestPurpose` (`Forward`/`Return`), guard the existing forward-only endpoints

**Files:**
- Create: `src/Modules/Manifests/Domain/ManifestPurpose.cs`
- Modify: `src/Modules/Manifests/Domain/Manifest.cs`
- Modify: `src/Modules/Manifests/Application/ManifestDtos.cs`
- Modify: `src/Modules/Manifests/Application/ManifestService.cs`
- Modify: `src/Modules/Manifests/Application/ManifestService.Scanning.cs`
- Modify: `src/Modules/Manifests/Infrastructure/ManifestConfiguration.cs`
- Test: `tests/Manifests.Tests/ManifestServicePurposeTests.cs` (new)
- Migration: generated

**Interfaces:**
- Produces: `Manifest.Purpose` (`ManifestPurpose`, defaults to `Forward`), `CreateManifestRequest`'s new trailing optional `Purpose` parameter (defaults to `Forward` — every one of the 17 existing `CreateManifestRequest(...)` call sites across this codebase is unaffected), `ManifestResponse.Purpose`. Task 10 consumes `Manifest.Purpose` to branch `DispatchAsync`/`ReceiveScanAsync` and to add the new `rto-scan` endpoint.

- [ ] **Step 1: `ManifestPurpose` enum**

`src/Modules/Manifests/Domain/ManifestPurpose.cs`:

```csharp
namespace Courier.Manifests.Domain;

// Forward is the default (see ManifestConfiguration's HasDefaultValue and
// CreateManifestRequest's default parameter) — every manifest before Phase 9 was implicitly this.
public enum ManifestPurpose
{
    Forward,
    Return
}
```

- [ ] **Step 2: Add the field to `Manifest`**

In `src/Modules/Manifests/Domain/Manifest.cs`, add after `public Guid ToBranchId { get; set; }`:

```csharp
    public ManifestPurpose Purpose { get; set; }
```

- [ ] **Step 3: `CreateManifestRequest`/`ManifestResponse` DTO changes**

In `src/Modules/Manifests/Application/ManifestDtos.cs`, change:

```csharp
public record CreateManifestRequest(Guid FromBranchId, Guid ToBranchId, ManifestPurpose Purpose = ManifestPurpose.Forward);
```

and

```csharp
public record ManifestResponse(
    Guid Id,
    string Code,
    Guid FromBranchId,
    Guid ToBranchId,
    ManifestPurpose Purpose,
    string? VehiclePlate,
    Guid? DriverId,
    ManifestStatus Status,
    int ShipmentCount,
    DateTimeOffset CreatedAt);
```

Add a new reason code constant is not needed — reuse the existing `ManifestScanRejectedException(reasonCode, message)` shape with a new literal reason code string, `"MANIFEST_WRONG_PURPOSE"`, at the call site in Step 5 (no DTO change needed for this).

- [ ] **Step 4: Set `Purpose` at creation**

In `src/Modules/Manifests/Application/ManifestService.cs`'s `CreateManifestAsync`, add `Purpose = request.Purpose,` to the `Manifest` object initializer (right after `Code = code,`):

```csharp
            manifest = new Manifest
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Code = code,
                Purpose = request.Purpose,
                FromBranchId = request.FromBranchId,
                ToBranchId = request.ToBranchId,
                Status = ManifestStatus.Draft,
                ShipmentCount = 0,
                CreatedByUserId = actingUserId,
                CreatedAt = DateTimeOffset.UtcNow
            };
```

Update `ToResponse` (same file) to include it:

```csharp
    private static ManifestResponse ToResponse(Manifest manifest) => new(
        manifest.Id, manifest.Code, manifest.FromBranchId, manifest.ToBranchId, manifest.Purpose,
        manifest.VehiclePlate, manifest.DriverId, manifest.Status, manifest.ShipmentCount, manifest.CreatedAt);
```

- [ ] **Step 5: Guard the existing forward-only `ScanAsync`**

In `src/Modules/Manifests/Application/ManifestService.Scanning.cs`'s `ScanAsync`, insert this check immediately after the existing `CheckBranchAccessAsync(actingUser, "manifest.create", manifest.FromBranchId, cancellationToken);` line and before `if (manifest.Status != ManifestStatus.Draft)`:

```csharp
        if (manifest.Purpose != ManifestPurpose.Forward)
        {
            throw new ManifestScanRejectedException("MANIFEST_WRONG_PURPOSE", "This manifest is a Return manifest — use /rto-scan instead of /scan.");
        }
```

Add `using Courier.Manifests.Domain;` if not already present in the file (it already is, for `ManifestStatus`/`Manifest` — no change needed).

- [ ] **Step 6: EF configuration default value**

In `src/Modules/Manifests/Infrastructure/ManifestConfiguration.cs`, add after the existing `builder.Property(m => m.VehiclePlate).HasMaxLength(30);` line:

```csharp
        // Explicit default so the generated migration backfills every pre-Phase-9 Manifest row as
        // Forward (the only purpose that existed before this column did) without needing a manual
        // data migration.
        builder.Property(m => m.Purpose).HasDefaultValue(ManifestPurpose.Forward);
```

- [ ] **Step 7: Write the failing test**

Create `tests/Manifests.Tests/ManifestServicePurposeTests.cs` — reuses the exact file-scoped fakes and `SeedDraftManifestAsync`-style helper pattern from `ManifestServiceScanningTests.cs` (copy `FixedTenantProvider`, `NoopAuditLogger`, `FakeBranchLookup`, `AllBranchesAuthorizationChecker`, and the non-file-scoped `ScanningFakeShipmentTransitionService` verbatim — reusing the exact class already defined in that file is not possible across files without a shared fixtures file, which this project's established per-file-helper convention deliberately avoids, so copy it):

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Manifests.Application;
using Courier.Manifests.Domain;
using Courier.Manifests.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Manifests.Tests;

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
    private readonly HashSet<Guid> _knownBranchIds;
    public FakeBranchLookup(params Guid[] knownBranchIds) => _knownBranchIds = knownBranchIds.ToHashSet();
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_knownBranchIds.Contains(branchId) ? new BranchZoneInfo(branchId, null) : null);
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class NotSupportedShipmentTransitionService : IShipmentTransitionService
{
    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class ManifestServicePurposeTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new ManifestConfiguration());
            modelBuilder.ApplyConfiguration(new ManifestShipmentConfiguration());
            modelBuilder.ApplyConfiguration(new ShipmentTransferConfiguration());
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

    [Fact]
    public async Task CreateManifestAsync_NoPurposeSpecified_DefaultsToForward()
    {
        await using var dbContext = CreateContext();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var service = new ManifestService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(),
            new NotSupportedShipmentTransitionService(), new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));

        var result = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));

        Assert.Equal(ManifestPurpose.Forward, result.Purpose);
    }

    [Fact]
    public async Task ScanAsync_OnReturnPurposeManifest_ThrowsWithWrongPurposeReasonCode()
    {
        await using var dbContext = CreateContext();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();
        var service = new ManifestService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(),
            new NotSupportedShipmentTransitionService(), new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));
        var manifest = await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId, ManifestPurpose.Return), ActingUser(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ScanAsync(manifest.Id, new ScanShipmentRequest("AWB001"), ActingUser(Guid.NewGuid())));
        Assert.Equal("MANIFEST_WRONG_PURPOSE", ex.ReasonCode);
    }
}
```

- [ ] **Step 8: Run the tests, then run every `Manifests.Tests` test to confirm no regression**

Run: `dotnet test tests/Manifests.Tests/Manifests.Tests.csproj`
Expected: All tests pass, including the two new ones — every pre-existing `Manifests.Tests` test still passes unchanged (they all construct `CreateManifestRequest` with two positional args, which now defaults `Purpose` to `Forward`, identical behavior to before this task).

- [ ] **Step 9: Generate and apply the migration**

Run: `dotnet ef migrations add AddManifestPurpose --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: A migration adding the `Purpose` column to `Manifests` with a default value, no other table changes.

Run: `dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: Applies cleanly.

- [ ] **Step 10: Commit**

```bash
git add src/Modules/Manifests/Domain/ManifestPurpose.cs src/Modules/Manifests/Domain/Manifest.cs src/Modules/Manifests/Application/ManifestDtos.cs src/Modules/Manifests/Application/ManifestService.cs src/Modules/Manifests/Application/ManifestService.Scanning.cs src/Modules/Manifests/Infrastructure/ManifestConfiguration.cs tests/Manifests.Tests/ManifestServicePurposeTests.cs src/Persistence/Migrations/
git commit -m "feat(manifests): add ManifestPurpose (Forward/Return), guard forward-only scan against Return manifests"
```

---

## Task 10: Manifests — RTO transport (`rto-scan`, and `Purpose`-branched dispatch/receive)

**Files:**
- Modify: `src/Modules/Manifests/Application/ManifestService.Scanning.cs`
- Modify: `src/Modules/Manifests/Application/ManifestService.Transitions.cs`
- Modify: `src/Modules/Manifests/Presentation/ManifestsEndpoints.cs`
- Modify: `tests/Manifests.Tests/ManifestServicePurposeTests.cs`

**Interfaces:**
- Consumes: `IShipmentTransitionService.DispatchManyRtoAsync/ReceiveRtoAtOriginAsync` (Task 1).
- Produces: `ManifestService.ScanRtoAsync(Guid manifestId, ScanShipmentRequest, ClaimsPrincipal, CancellationToken) -> Task<ManifestShipmentResponse>` — Task 12's end-to-end RTO test calls it via `POST /manifests/{id}/rto-scan`.

- [ ] **Step 1: Add `ScanRtoAsync`**

Append to `src/Modules/Manifests/Application/ManifestService.Scanning.cs`, immediately after the existing `ScanAsync` method, before `SealAsync`:

```csharp
    // The RTO counterpart to ScanAsync above. Unlike the forward path (which sorts
    // AtOriginBranch -> Sorted as part of adding to the manifest), RTO_INITIATED has no
    // resting-status equivalent to move into here — the shipment stays RtoInitiated until Dispatch
    // moves every shipment on this manifest to RtoInTransit at once (this plan's resolved design
    // decision, CLARIFICATIONS.md's new Phase 9 entry).
    public async Task<ManifestShipmentResponse> ScanRtoAsync(Guid manifestId, ScanShipmentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ManifestValidationException("No active organization context.");
        if (string.IsNullOrWhiteSpace(request.Awb))
        {
            throw new ManifestValidationException("Awb is required.");
        }

        var manifest = await _dbContext.Set<Manifest>().FirstOrDefaultAsync(m => m.Id == manifestId, cancellationToken)
            ?? throw new ManifestNotFoundException(manifestId);

        await CheckBranchAccessAsync(actingUser, "manifest.create", manifest.FromBranchId, cancellationToken);

        if (manifest.Purpose != ManifestPurpose.Return)
        {
            throw new ManifestScanRejectedException("MANIFEST_WRONG_PURPOSE", "This manifest is a Forward manifest — use /scan instead of /rto-scan.");
        }
        if (manifest.Status != ManifestStatus.Draft)
        {
            throw new ManifestScanRejectedException("MANIFEST_NOT_OPEN", $"Manifest is {manifest.Status}, not open for adding shipments.");
        }

        var scanInfo = await _shipmentTransitionService.GetScanInfoByAwbAsync(request.Awb, cancellationToken)
            ?? throw new ManifestScanRejectedException("SHIPMENT_NOT_FOUND", $"No shipment found for AWB '{request.Awb}'.");

        if (scanInfo.CurrentStatus != ShipmentStatus.RtoInitiated)
        {
            throw new ManifestScanRejectedException("WRONG_STATE", $"Shipment must be RtoInitiated to add to a return manifest, was {scanInfo.CurrentStatus}.");
        }
        if (scanInfo.CurrentBranchId != manifest.FromBranchId)
        {
            throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's current branch does not match this return manifest's FromBranchId.");
        }

        var manifestShipment = new ManifestShipment
        {
            Id = Guid.NewGuid(),
            ManifestId = manifestId,
            ShipmentId = scanInfo.ShipmentId,
            ScannedAt = DateTimeOffset.UtcNow,
            ReceivedAt = null
        };
        _dbContext.Set<ManifestShipment>().Add(manifestShipment);
        manifest.ShipmentCount += 1;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "manifest.rto_shipment_scanned", "Manifest", manifestId.ToString()), cancellationToken);
        return new ManifestShipmentResponse(manifestShipment.Id, scanInfo.ShipmentId, scanInfo.Awb, manifestShipment.ScannedAt, manifestShipment.ReceivedAt);
    }
```

- [ ] **Step 2: Branch `DispatchAsync` on `Purpose`**

In `src/Modules/Manifests/Application/ManifestService.Transitions.cs`'s `DispatchAsync`, replace the single line `await _shipmentTransitionService.DispatchManyAsync(shipmentIds, actingUser, cancellationToken);` with:

```csharp
        if (manifest.Purpose == ManifestPurpose.Forward)
        {
            // This plan's resolved design decision #2 (from Phase 7, still true): goes straight to
            // InTransit, no separate persisted "Dispatched" resting state.
            await _shipmentTransitionService.DispatchManyAsync(shipmentIds, actingUser, cancellationToken);
        }
        else
        {
            await _shipmentTransitionService.DispatchManyRtoAsync(shipmentIds, actingUser, cancellationToken);
        }
```

(Delete the old comment line directly above the replaced call — its content now lives inside the `Forward` branch above.)

- [ ] **Step 3: Branch `ReceiveScanAsync` on `Purpose`**

In the same file's `ReceiveScanAsync`, replace this block:

```csharp
        if (scanInfo.CurrentStatus != ShipmentStatus.InTransit)
        {
            throw new ManifestScanRejectedException("WRONG_STATE", $"Shipment must be InTransit to receive, was {scanInfo.CurrentStatus}.");
        }
        // Check 5 (custody consistency): an in-transit shipment has no current branch, and its
        // declared destination must match where this manifest is actually headed.
        if (scanInfo.CurrentBranchId is not null)
        {
            throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's custody is inconsistent with being in transit.");
        }
        if (scanInfo.DestinationBranchId != manifest.ToBranchId)
        {
            throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's destination branch does not match this manifest's ToBranchId.");
        }

        await _shipmentTransitionService.ReceiveAtDestinationAsync(scanInfo.ShipmentId, actingUser, cancellationToken);
```

with:

```csharp
        if (manifest.Purpose == ManifestPurpose.Forward)
        {
            if (scanInfo.CurrentStatus != ShipmentStatus.InTransit)
            {
                throw new ManifestScanRejectedException("WRONG_STATE", $"Shipment must be InTransit to receive, was {scanInfo.CurrentStatus}.");
            }
            // Check 5 (custody consistency): an in-transit shipment has no current branch, and its
            // declared destination must match where this manifest is actually headed.
            if (scanInfo.CurrentBranchId is not null)
            {
                throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's custody is inconsistent with being in transit.");
            }
            if (scanInfo.DestinationBranchId != manifest.ToBranchId)
            {
                throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's destination branch does not match this manifest's ToBranchId.");
            }

            await _shipmentTransitionService.ReceiveAtDestinationAsync(scanInfo.ShipmentId, actingUser, cancellationToken);
        }
        else
        {
            if (scanInfo.CurrentStatus != ShipmentStatus.RtoInTransit)
            {
                throw new ManifestScanRejectedException("WRONG_STATE", $"Shipment must be RtoInTransit to receive, was {scanInfo.CurrentStatus}.");
            }
            if (scanInfo.CurrentBranchId is not null)
            {
                throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's custody is inconsistent with being in transit.");
            }
            // For a return leg, ShipmentScanInfo.OriginBranchId still names the ORIGINAL shipment's
            // origin branch — that's exactly where this return manifest is headed (manifest.ToBranchId).
            if (scanInfo.OriginBranchId != manifest.ToBranchId)
            {
                throw new ManifestScanRejectedException("WRONG_CUSTODY", "Shipment's original origin branch does not match this return manifest's ToBranchId.");
            }

            await _shipmentTransitionService.ReceiveRtoAtOriginAsync(scanInfo.ShipmentId, actingUser, cancellationToken);
        }
```

The rest of `ReceiveScanAsync` (setting `manifestShipment.ReceivedAt`, moving `manifest.Status` to `Received`, adding the `ShipmentTransfer` row, the audit log call) is unchanged — none of it is purpose-specific.

- [ ] **Step 4: Add the `rto-scan` route**

In `src/Modules/Manifests/Presentation/ManifestsEndpoints.cs`, add after the existing `/{id:guid}/scan` route registration:

```csharp
        group.MapPost("/{id:guid}/rto-scan", async (Guid id, ScanShipmentRequest request, ManifestService service, HttpContext httpContext) =>
            await HandleScanAsync(() => service.ScanRtoAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("manifest.create");
```

- [ ] **Step 5: Write failing tests for the RTO dispatch/receive branch, and a full Draft→Closed RTO manifest lifecycle**

Append to `tests/Manifests.Tests/ManifestServicePurposeTests.cs` (reuse the file's existing fakes — extend `NotSupportedShipmentTransitionService` isn't suitable here since these tests need to observe real state transitions; add a small recording fake instead, following the same non-file-scoped-when-returned-from-a-helper rule):

```csharp
// Not file-scoped: returned from SeedReturnManifestWithOneShipmentAsync's tuple.
class RtoRecordingShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public List<Guid> DispatchManyRtoCalls { get; } = new();
    public List<Guid> ReceiveRtoCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.Values.FirstOrDefault(s => s.Awb == awb));
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        foreach (var id in shipmentIds)
        {
            DispatchManyRtoCalls.Add(id);
            var info = ShipmentsById[id];
            ShipmentsById[id] = info with { CurrentStatus = ShipmentStatus.RtoInTransit, CurrentBranchId = null };
        }
        return Task.CompletedTask;
    }

    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        ReceiveRtoCalls.Add(shipmentId);
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.RtoAtOrigin, CurrentBranchId = info.OriginBranchId };
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
```

Add these test methods to `ManifestServicePurposeTests`:

```csharp
    private static async Task<(ManifestService Service, RtoRecordingShipmentTransitionService Shipments, Guid ManifestId, Guid OriginalOriginBranchId, Guid OriginalDestinationBranchId)> SeedReturnManifestWithOneShipmentAsync(TestDbContext dbContext, Guid organizationId)
    {
        // Return-leg branch naming: the manifest travels FROM the original destination branch TO
        // the original origin branch (NdrService.CreateReturnShipmentAsync, Task 7, swaps them the
        // same way).
        var originalOriginBranchId = Guid.NewGuid();
        var originalDestinationBranchId = Guid.NewGuid();
        var shipments = new RtoRecordingShipmentTransitionService();
        var service = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(),
            shipments, new AllBranchesAuthorizationChecker(), new FakeBranchLookup(originalOriginBranchId, originalDestinationBranchId));

        var manifest = await service.CreateManifestAsync(new CreateManifestRequest(originalDestinationBranchId, originalOriginBranchId, ManifestPurpose.Return), ActingUser(Guid.NewGuid()));

        var shipmentId = Guid.NewGuid();
        shipments.ShipmentsById[shipmentId] = new ShipmentScanInfo(shipmentId, organizationId, "RTOAWB001", ShipmentStatus.RtoInitiated, originalDestinationBranchId, originalOriginBranchId, originalDestinationBranchId, null);

        return (service, shipments, manifest.Id, originalOriginBranchId, originalDestinationBranchId);
    }

    [Fact]
    public async Task FullReturnManifestLifecycle_DraftToClosed_MovesShipmentThroughRtoStatuses()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, originalOriginBranchId, _) = await SeedReturnManifestWithOneShipmentAsync(dbContext, Guid.NewGuid());
        var shipmentId = shipments.ShipmentsById.Keys.Single();

        var scanResult = await service.ScanRtoAsync(manifestId, new ScanShipmentRequest("RTOAWB001"), ActingUser(Guid.NewGuid()));
        Assert.Equal(shipmentId, scanResult.ShipmentId);
        Assert.Equal(ShipmentStatus.RtoInitiated, shipments.ShipmentsById[shipmentId].CurrentStatus); // no status change on scan itself

        await service.SealAsync(manifestId, ActingUser(Guid.NewGuid()));
        var dispatched = await service.DispatchAsync(manifestId, new DispatchManifestRequest("BA 2 KHA 5678", null), ActingUser(Guid.NewGuid()));
        Assert.Equal(ManifestStatus.InTransit, dispatched.Status);
        Assert.Single(shipments.DispatchManyRtoCalls, shipmentId);
        Assert.Equal(ShipmentStatus.RtoInTransit, shipments.ShipmentsById[shipmentId].CurrentStatus);

        var received = await service.ReceiveScanAsync(manifestId, new ScanShipmentRequest("RTOAWB001"), ActingUser(Guid.NewGuid()));
        Assert.NotNull(received.ReceivedAt);
        Assert.Single(shipments.ReceiveRtoCalls, shipmentId);
        Assert.Equal(ShipmentStatus.RtoAtOrigin, shipments.ShipmentsById[shipmentId].CurrentStatus);
        Assert.Equal(originalOriginBranchId, shipments.ShipmentsById[shipmentId].CurrentBranchId);

        var closed = await service.CloseAsync(manifestId, ActingUser(Guid.NewGuid()));
        Assert.Equal(ManifestStatus.Closed, closed.Status);
    }
```

- [ ] **Step 6: Build and run the whole solution**

Run: `dotnet build`
Expected: Build succeeds.

Run: `dotnet test`
Expected: Every test project passes — `Manifests.Tests` gains 3 more tests than Task 9 left it with.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Manifests/Application/ManifestService.Scanning.cs src/Modules/Manifests/Application/ManifestService.Transitions.cs src/Modules/Manifests/Presentation/ManifestsEndpoints.cs tests/Manifests.Tests/ManifestServicePurposeTests.cs
git commit -m "feat(manifests): drive the RTO return leg through Purpose-branched dispatch/receive-scan and a new rto-scan endpoint"
```

---

## Task 11: Ndr Application/Presentation — `ReturnShipment` reads and merchant confirmation

**Files:**
- Create: `src/Modules/Ndr/Application/NdrService.ReturnShipments.cs`
- Modify: `src/Modules/Ndr/Presentation/NdrEndpoints.cs`
- Test: `tests/Ndr.Tests/NdrServiceReturnShipmentTests.cs`

**Interfaces:**
- Consumes: `IShipmentTransitionService.GetScanInfoAsync/ConfirmRtoDeliveredAsync` (Task 1).
- Produces: `NdrService.GetReturnShipmentByIdAsync(Guid, CancellationToken) -> Task<ReturnShipmentResponse?>`, `NdrService.ListReturnShipmentsAsync(CancellationToken) -> Task<List<ReturnShipmentResponse>>`, `NdrService.ConfirmReturnShipmentAsync(Guid, ClaimsPrincipal, CancellationToken) -> Task<ReturnShipmentResponse?>` — Task 12's end-to-end test calls all three via HTTP.

- [ ] **Step 1: `ReturnShipment` reads and confirmation**

`src/Modules/Ndr/Application/NdrService.ReturnShipments.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Ndr.Domain;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public partial class NdrService
{
    public async Task<ReturnShipmentResponse?> GetReturnShipmentByIdAsync(Guid returnShipmentId, CancellationToken cancellationToken = default)
    {
        var returnShipment = await _dbContext.Set<ReturnShipment>().FirstOrDefaultAsync(r => r.Id == returnShipmentId, cancellationToken);
        return returnShipment is null ? null : await ToReturnShipmentResponseAsync(returnShipment, cancellationToken);
    }

    public async Task<List<ReturnShipmentResponse>> ListReturnShipmentsAsync(CancellationToken cancellationToken = default)
    {
        var returnShipments = await _dbContext.Set<ReturnShipment>().ToListAsync(cancellationToken);
        var responses = new List<ReturnShipmentResponse>();
        foreach (var returnShipment in returnShipments)
        {
            responses.Add(await ToReturnShipmentResponseAsync(returnShipment, cancellationToken));
        }
        return responses;
    }

    public async Task<ReturnShipmentResponse?> ConfirmReturnShipmentAsync(Guid returnShipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new NdrValidationException("No active organization context.");

        var returnShipment = await _dbContext.Set<ReturnShipment>().FirstOrDefaultAsync(r => r.Id == returnShipmentId, cancellationToken);
        if (returnShipment is null)
        {
            return null;
        }
        if (returnShipment.MerchantConfirmedAt is not null)
        {
            throw new NdrValidationException("This return shipment has already been confirmed.");
        }

        // Check 1 (permission) enforced at the route via .RequirePermission("rto.manage"); check 2
        // (branch access) here, scoped to DestinationBranchId — the ORIGINAL shipment's origin
        // branch, where the parcel now physically sits after RTO_AT_ORIGIN. The underlying
        // ConfirmRtoDeliveredAsync call below enforces the state check (shipment must actually be
        // RtoAtOrigin) itself, via its own TransitionAsync guard — no need to duplicate that here.
        await CheckBranchAccessAsync(actingUser, "rto.manage", returnShipment.DestinationBranchId, cancellationToken);

        await _shipmentTransitionService.ConfirmRtoDeliveredAsync(returnShipment.OriginalShipmentId, actingUser, cancellationToken);
        returnShipment.MerchantConfirmedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "return_shipment.confirmed", "ReturnShipment", returnShipment.Id.ToString()), cancellationToken);
        return await ToReturnShipmentResponseAsync(returnShipment, cancellationToken);
    }

    // Enriches with live status/branch straight from Shipments rather than a stored, driftable
    // copy — see ReturnShipment.cs's doc comment (Task 2) for why no Status/CurrentBranchId column
    // exists on this entity at all.
    private async Task<ReturnShipmentResponse> ToReturnShipmentResponseAsync(ReturnShipment returnShipment, CancellationToken cancellationToken)
    {
        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(returnShipment.OriginalShipmentId, cancellationToken);
        return new ReturnShipmentResponse(
            returnShipment.Id, returnShipment.OriginalShipmentId, returnShipment.ReturnAwb, returnShipment.RtoReason,
            returnShipment.OriginBranchId, returnShipment.DestinationBranchId,
            scanInfo?.CurrentStatus ?? ShipmentStatus.RtoInitiated, scanInfo?.CurrentBranchId, returnShipment.MerchantConfirmedAt);
    }
}
```

- [ ] **Step 2: Add the `/return-shipments` routes**

In `src/Modules/Ndr/Presentation/NdrEndpoints.cs`, add a second route group and its mappings inside `MapNdrEndpoints` (same file, same static class — mirrors `RidersEndpoints.cs` mapping both `/riders` and `/vehicles` from one file), right after the existing `ndrs` group's four route registrations, before the closing `}` of `MapNdrEndpoints`:

```csharp
        var returnShipments = app.MapGroup("/api/v1/return-shipments").RequireAuthorization();

        returnShipments.MapGet("/", async (NdrService service, HttpContext httpContext) =>
            Results.Ok(await service.ListReturnShipmentsAsync(httpContext.RequestAborted)))
            .RequirePermission("rto.read");

        returnShipments.MapGet("/{id:guid}", async (Guid id, NdrService service, HttpContext httpContext) =>
        {
            var result = await service.GetReturnShipmentByIdAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("rto.read");

        returnShipments.MapPost("/{id:guid}/confirm", async (Guid id, NdrService service, HttpContext httpContext) =>
            await HandleAsync(() => service.ConfirmReturnShipmentAsync(id, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("rto.manage");
```

- [ ] **Step 3: Write the failing tests**

Create `tests/Ndr.Tests/NdrServiceReturnShipmentTests.cs` — reuses the same file-scoped fakes as `NdrServiceResolveTests.cs` (`FixedTenantProvider`, `NoopAuditLogger`, `AllBranchesAuthorizationChecker`) plus a small recording `IShipmentTransitionService`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.Ndr.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Ndr.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class FakeShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public List<Guid> ConfirmCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> TransitionToNdrCreatedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReattemptDeliveryAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> InitiateRtoAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyRtoAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveRtoAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ShipmentResponse?> ConfirmRtoDeliveredAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        ConfirmCalls.Add(shipmentId);
        if (ShipmentsById.TryGetValue(shipmentId, out var info))
        {
            ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.RtoDelivered };
        }
        return Task.FromResult<ShipmentResponse?>(null);
    }
}

public class NdrServiceReturnShipmentTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new NdrConfiguration());
            modelBuilder.ApplyConfiguration(new ReturnShipmentConfiguration());
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

    private static async Task<(ReturnShipment Entity, NdrService Service, FakeShipmentTransitionService Shipments)> SeedReturnShipmentAsync(TestDbContext dbContext, Guid organizationId, ShipmentStatus originalShipmentStatus)
    {
        var originalShipmentId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();
        var returnShipment = new ReturnShipment
        {
            Id = Guid.NewGuid(), OriginalShipmentId = originalShipmentId, OrganizationId = organizationId,
            ReturnAwb = "RTO-20260821-001", RtoReason = "Customer refused",
            OriginBranchId = destinationBranchId, DestinationBranchId = originBranchId
        };
        dbContext.Set<ReturnShipment>().Add(returnShipment);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[originalShipmentId] = new ShipmentScanInfo(originalShipmentId, organizationId, "AWB001", originalShipmentStatus, originBranchId, originBranchId, destinationBranchId, null);
        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());
        return (returnShipment, service, shipments);
    }

    [Fact]
    public async Task GetReturnShipmentByIdAsync_ReflectsLiveShipmentStatus()
    {
        await using var dbContext = CreateContext();
        var (returnShipment, service, _) = await SeedReturnShipmentAsync(dbContext, Guid.NewGuid(), ShipmentStatus.RtoAtOrigin);

        var result = await service.GetReturnShipmentByIdAsync(returnShipment.Id);

        Assert.Equal(ShipmentStatus.RtoAtOrigin, result!.CurrentStatus);
        Assert.Equal(returnShipment.ReturnAwb, result.ReturnAwb);
    }

    [Fact]
    public async Task ConfirmReturnShipmentAsync_FromRtoAtOrigin_SetsMerchantConfirmedAtAndTransitionsShipment()
    {
        await using var dbContext = CreateContext();
        var (returnShipment, service, shipments) = await SeedReturnShipmentAsync(dbContext, Guid.NewGuid(), ShipmentStatus.RtoAtOrigin);

        var result = await service.ConfirmReturnShipmentAsync(returnShipment.Id, ActingUser(Guid.NewGuid()));

        Assert.NotNull(result!.MerchantConfirmedAt);
        Assert.Single(shipments.ConfirmCalls, returnShipment.OriginalShipmentId);
        Assert.Equal(ShipmentStatus.RtoDelivered, result.CurrentStatus);
    }

    [Fact]
    public async Task ConfirmReturnShipmentAsync_AlreadyConfirmed_ThrowsNdrValidationException()
    {
        await using var dbContext = CreateContext();
        var (returnShipment, service, _) = await SeedReturnShipmentAsync(dbContext, Guid.NewGuid(), ShipmentStatus.RtoAtOrigin);
        await service.ConfirmReturnShipmentAsync(returnShipment.Id, ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<NdrValidationException>(() => service.ConfirmReturnShipmentAsync(returnShipment.Id, ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task GetReturnShipmentByIdAsync_UnknownId_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = new NdrService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new FakeShipmentTransitionService(), new AllBranchesAuthorizationChecker());

        Assert.Null(await service.GetReturnShipmentByIdAsync(Guid.NewGuid()));
    }
}
```

- [ ] **Step 4: Run the tests, then build the whole solution**

Run: `dotnet test tests/Ndr.Tests/Ndr.Tests.csproj`
Expected: All tests pass (15 total across the module: 1 creation + 5 update + 5 resolve + 4 return-shipment).

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Ndr/Application/NdrService.ReturnShipments.cs src/Modules/Ndr/Presentation/NdrEndpoints.cs tests/Ndr.Tests/NdrServiceReturnShipmentTests.cs
git commit -m "feat(ndr): add ReturnShipment reads and merchant-confirmation endpoint"
```

---

## Task 12: API tests — full RTO success path, full reattempt path, tenant isolation

**Files:**
- Create: `tests/Api.Tests/NdrEndpointsTests.cs`

**Interfaces:**
- Consumes: the full `/api/v1/ndrs`, `/api/v1/return-shipments`, `/api/v1/riders`, `/api/v1/manifests`, `/api/v1/shipments` HTTP surface.

- [ ] **Step 1: Write the test file**

Create `tests/Api.Tests/NdrEndpointsTests.cs` — helper methods are copied (not shared) from `RidersEndpointsTests.cs`, matching this project's established per-file-helper convention:

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
using Courier.Manifests.Domain;
using Courier.Merchants.Application;
using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class NdrEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public NdrEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Ndr Org {suffix}", $"ndr-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private static CreateBranchRequest ValidBranchRequest(string code) => new(
        code, $"Branch {code}", BranchType.Hub, KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null, null);

    private static List<Guid> KathmanduWard1Areas() => new()
    {
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
    };

    private async Task<(Guid FromBranchId, Guid ToBranchId, Guid ServiceTypeId)> SeedBranchesAsync(HttpClient client)
    {
        var fromBranchResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"FR-{Guid.NewGuid():N}"[..10]));
        var fromBranch = await fromBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();
        var toBranchResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"TO-{Guid.NewGuid():N}"[..10]));
        var toBranch = await toBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var originZoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Origin-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var originZone = await originZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        var destZoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Dest-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var destZone = await destZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        await client.PutAsJsonAsync($"/api/v1/branches/{fromBranch!.Id}/delivery-zone", new AssignDeliveryZoneRequest(originZone!.Id));
        await client.PutAsJsonAsync($"/api/v1/branches/{toBranch!.Id}/delivery-zone", new AssignDeliveryZoneRequest(destZone!.Id));

        var serviceTypesResponse = await client.GetAsync("/api/v1/service-types");
        var serviceTypes = await serviceTypesResponse.Content.ReadFromJsonAsync<List<ServiceTypeResponse>>();
        var standard = serviceTypes!.Single(s => s.Code == ServiceTypeCode.Standard);

        var weightSlabs = await (await client.GetAsync("/api/v1/weight-slabs")).Content.ReadFromJsonAsync<List<WeightSlabResponse>>();
        var slab = weightSlabs!.Single(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m);
        var rateCardResponse = await client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest($"Card-{Guid.NewGuid():N}", DateTimeOffset.UtcNow));
        var rateCard = await rateCardResponse.Content.ReadFromJsonAsync<RateCardResponse>();
        await client.PostAsJsonAsync($"/api/v1/rate-cards/{rateCard!.Id}/zone-rates",
            new CreateZoneRateRequest(originZone.Id, destZone.Id, standard.Id, slab.Id, 100m));

        return (fromBranch.Id, toBranch.Id, standard.Id);
    }

    private async Task<Guid> CreateMerchantAsync(HttpClient staffClient)
    {
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Shipper Pvt. Ltd.", "Shipper", "01-4444444", "shipper@example.com",
            $"shipper-owner-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return merchant!.Id;
    }

    private static CreateShipmentRequest ValidShipmentRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 1, 500m) });

    private async Task<Guid> CreateRiderAsync(HttpClient client, Guid branchId, RiderRoleType roleType)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, $"LIC-{suffix[..10]}", branchId, roleType));
        var rider = await response.Content.ReadFromJsonAsync<RiderResponse>();
        return rider!.Id;
    }

    // Books a shipment and walks it all the way to a failed delivery attempt — pickup (Phase 8),
    // sort/dispatch/receive via Manifests (Phase 7), assign+scan out for delivery (Phase 8), then a
    // failed attempt, which auto-creates the NDR this whole test file exercises (Phase 9, Task 6).
    private async Task<ShipmentResponse> BookAndFailDeliveryAsync(
        HttpClient client, Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, Guid pickupRiderId, Guid deliveryRiderId)
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        await client.PostAsJsonAsync($"/api/v1/riders/{pickupRiderId}/scan/pickup", new RiderScanRequest(shipment.Awb));
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { });

        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));
        await client.PostAsJsonAsync($"/api/v1/riders/{deliveryRiderId}/delivery-attempts/fail",
            new RecordFailedAttemptRequest(shipment.Id, DeliveryAttemptReason.CustomerUnavailable, "No one home", null, null, true, "Try tomorrow"));

        return (await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>())!;
    }

    private async Task<NdrResponse> GetOpenNdrForShipmentAsync(HttpClient client, Guid shipmentId)
    {
        var ndrs = await (await client.GetAsync("/api/v1/ndrs")).Content.ReadFromJsonAsync<List<NdrResponse>>();
        return ndrs!.Single(n => n.ShipmentId == shipmentId);
    }

    [Fact]
    public async Task FullRtoPath_FailedAttemptToRtoDelivered_MovesThroughEveryStatusViaManifests()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "rto-success");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var shipment = await BookAndFailDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);
        Assert.Equal(ShipmentStatus.NdrCreated, shipment.CurrentStatus);

        var ndr = await GetOpenNdrForShipmentAsync(client, shipment.Id);
        Assert.Equal(NdrStatus.Open, ndr.Status);

        await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/update", new UpdateNdrRequest(NdrStatus.Contacting, "Phone rang out", null, null));
        await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/update", new UpdateNdrRequest(NdrStatus.CustomerConfirmed, "Customer confirmed refusal", null, null));
        var afterRtoRequested = await (await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/update", new UpdateNdrRequest(NdrStatus.RtoRequested, null, "Return to merchant", null))).Content.ReadFromJsonAsync<NdrResponse>();
        Assert.Equal(NdrStatus.RtoRequested, afterRtoRequested!.Status);

        var resolveResponse = await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/resolve", new ResolveNdrRequest(NdrResolution.Rto, "Customer refused after three contacts"));
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
        var resolved = await resolveResponse.Content.ReadFromJsonAsync<NdrResponse>();
        Assert.Equal(NdrStatus.Resolved, resolved!.Status);

        var afterInitiate = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.RtoInitiated, afterInitiate!.CurrentStatus);
        Assert.Equal(destinationBranchId, afterInitiate.CurrentBranchId);

        var returnShipments = await (await client.GetAsync("/api/v1/return-shipments")).Content.ReadFromJsonAsync<List<ReturnShipmentResponse>>();
        var returnShipment = returnShipments!.Single(r => r.OriginalShipmentId == shipment.Id);
        Assert.StartsWith("RTO-", returnShipment.ReturnAwb);
        Assert.Equal(destinationBranchId, returnShipment.OriginBranchId);
        Assert.Equal(originBranchId, returnShipment.DestinationBranchId);

        // The return leg is a real Manifest, just Purpose=Return and traveling the opposite
        // direction — scanned by the ORIGINAL shipment's own Awb (ReturnAwb is ReturnShipment's own
        // administrative tracking number, not what Manifests scans against).
        var returnManifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(destinationBranchId, originBranchId, ManifestPurpose.Return))).Content.ReadFromJsonAsync<ManifestResponse>();
        Assert.Equal(ManifestPurpose.Return, returnManifest!.Purpose);

        var rtoScanResponse = await client.PostAsJsonAsync($"/api/v1/manifests/{returnManifest.Id}/rto-scan", new ScanShipmentRequest(shipment.Awb));
        Assert.Equal(HttpStatusCode.OK, rtoScanResponse.StatusCode);
        await client.PostAsync($"/api/v1/manifests/{returnManifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{returnManifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 5678", null));

        var afterRtoDispatch = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.RtoInTransit, afterRtoDispatch!.CurrentStatus);

        await client.PostAsJsonAsync($"/api/v1/manifests/{returnManifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));
        var afterRtoReceive = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.RtoAtOrigin, afterRtoReceive!.CurrentStatus);
        Assert.Equal(originBranchId, afterRtoReceive.CurrentBranchId);

        var confirmResponse = await client.PostAsync($"/api/v1/return-shipments/{returnShipment.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmed = await confirmResponse.Content.ReadFromJsonAsync<ReturnShipmentResponse>();
        Assert.NotNull(confirmed!.MerchantConfirmedAt);
        Assert.Equal(ShipmentStatus.RtoDelivered, confirmed.CurrentStatus);

        var final = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.RtoDelivered, final!.CurrentStatus);
    }

    [Fact]
    public async Task FullReattemptPath_FailedAttemptBackToOutForDeliveryThenDelivered_AttemptNumberReachesTwo()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "reattempt");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var shipment = await BookAndFailDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);
        var ndr = await GetOpenNdrForShipmentAsync(client, shipment.Id);

        await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/update", new UpdateNdrRequest(NdrStatus.Contacting, null, null, null));
        await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/update", new UpdateNdrRequest(NdrStatus.CustomerConfirmed, null, null, null));
        await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/update", new UpdateNdrRequest(NdrStatus.ReattemptScheduled, null, "Redeliver tomorrow morning", DateTimeOffset.UtcNow.AddDays(1)));

        var resolveResponse = await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/resolve", new ResolveNdrRequest(NdrResolution.Reattempt, null));
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var afterReattempt = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.OutForDelivery, afterReattempt!.CurrentStatus);
        Assert.Equal(deliveryRiderId, afterReattempt.CurrentCustodianRiderId);

        var completeResponse = await client.PostAsJsonAsync($"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete",
            new CompleteDeliveryRequest(shipment.Id, "Recipient One", true, null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var attempt = await completeResponse.Content.ReadFromJsonAsync<DeliveryAttemptResponse>();
        Assert.True(attempt!.Succeeded);
        Assert.Equal(2, attempt.AttemptNumber);

        var final = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Delivered, final!.CurrentStatus);
    }

    [Fact]
    public async Task Ndrs_OrgBCannotReadOrgAsNdr()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "iso-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(clientA);
        var merchantId = await CreateMerchantAsync(clientA);
        var pickupRiderId = await CreateRiderAsync(clientA, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(clientA, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndFailDeliveryAsync(clientA, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);
        var ndr = await GetOpenNdrForShipmentAsync(clientA, shipment.Id);

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "iso-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var response = await clientB.GetAsync($"/api/v1/ndrs/{ndr.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetNdrs_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/ndrs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test`
Expected: Every test project passes, including all four new tests in this file.

- [ ] **Step 3: Commit**

```bash
git add tests/Api.Tests/NdrEndpointsTests.cs
git commit -m "test(ndr): add end-to-end RTO and reattempt lifecycle tests, tenant isolation"
```

---

## Task 13: Documentation — `CLARIFICATIONS.md`, `ROADMAP.md`, `DOMAIN-MODEL.md`

Per this project's Cross-Phase Rule (`ROADMAP.md`'s final section): every ambiguity resolved during this phase gets written down before the phase is considered done.

**Files:**
- Modify: `docs/CLARIFICATIONS.md`
- Modify: `docs/ROADMAP.md`
- Modify: `docs/DOMAIN-MODEL.md`

- [ ] **Step 1: Add `CLARIFICATIONS.md` entry #9**

Insert before the `## Summary of schema additions implied by this document` heading:

```markdown
## 9. Phase 9: RTO transport reuse, `ReturnShipment`'s missing status column, and `NdrReason` duplication

**Where:** §13/§14 (Scanning/Manifests), §20/§21 (NDR/RTO), `BRANCH-OPERATIONS.md` §1

**Ambiguity 1:** `BRANCH-OPERATIONS.md` §1 lists `RTO` as one of the seven scan operation types
alongside `Receive, Sort, Dispatch, Transfer, RiderPickup, RiderDelivery` — implying RTO needs its
own transport mechanism — but `CLAUDE.md` §14/`BRANCH-OPERATIONS.md` §2 already define `Manifest`
as the one branch-to-branch transport primitive, and building a second one for RTO alone would
duplicate everything `Manifest` already does (missing-shipment detection, vehicle/driver capture,
seal/dispatch/receive/close lifecycle).

**Resolution 1:** `Manifest` gains a `Purpose` field (`Forward | Return`, defaults to `Forward`).
A `Return`-purpose manifest is created exactly like a forward one, just with `FromBranchId`/
`ToBranchId` pointing the opposite direction (from the branch that attempted delivery back toward
the branch the parcel originated at). The existing `/scan`, `/dispatch`, `/receive-scan` endpoints
branch on `Purpose` to call the RTO-specific pair of `IShipmentTransitionService` methods
(`DispatchManyRtoAsync`/`ReceiveRtoAtOriginAsync`) instead of the forward pair; a new `/rto-scan`
endpoint (mirroring `/scan`) adds a shipment to a Return manifest without any status transition of
its own — unlike the forward path's sort-scan, `RTO_INITIATED` has no resting-status equivalent to
move into, so the shipment stays `RtoInitiated` until the whole manifest dispatches at once.
`/seal`/`/close` need no branching — neither one touches shipment-specific state.

**Ambiguity 2:** `DOMAIN-MODEL.md`'s original `ReturnShipment` sketch included `Status` and
`ReceivedAt` columns, but the underlying `Shipment` (via `OriginalShipmentId`) is already the
single source of truth for both once `RtoInitiated`/`RtoInTransit`/`RtoAtOrigin`/`RtoDelivered`
exist as real `ShipmentStatus` values — a second, independently-updated copy would need `Manifests`
to gain a project reference to `Ndr` purely to keep it in sync (`Manifests`' RTO receive-scan is
what actually knows when a return arrives).

**Resolution 2:** `ReturnShipment` does not store `Status`/`CurrentBranchId`/`ReceivedAt`.
`Ndr.Application.NdrService`'s `ReturnShipmentResponse` reads both live from
`IShipmentTransitionService.GetScanInfoAsync(OriginalShipmentId)` at request time instead — the
same "no cached copy of custody" principle CLAUDE.md §12 already states for `Shipment` itself,
applied one level up. `MerchantConfirmedAt` is the one exception: it is a genuine
`Ndr`-module-owned fact (when staff confirmed the merchant accepted the return) with no
cross-module coordination needed, so it stays a real stored column.

**Ambiguity 3:** `NDR.Reason` needs `DOMAIN-MODEL.md`'s "from `DeliveryAttempt.Reason`" list —
identical to `Riders.Domain.DeliveryAttemptReason` (Phase 8) — but `Ndr` has no natural reason to
depend on `Riders` (NDR resolution and RTO have nothing to do with rider assignment/tasks), and
`Riders` already depends on `Ndr` (for `INdrCreationService`) — a reference the other way would be
circular.

**Resolution 3:** `Ndr.Domain.NdrReason` is a fresh, identically-valued copy of
`Riders.Domain.DeliveryAttemptReason`, not a shared reference — the same choice
`DeliveryAttemptReason` itself already made relative to `CLAUDE.md` §19 in Phase 8.
`Riders.Application.RiderService` converts between the two via an explicit `switch` expression
(not a raw enum-to-enum cast), so a future divergence between the two lists fails at compile time
instead of silently miscasting.

**Ambiguity 4:** `CLAUDE.md` §20 describes an "auto-return after N failed attempts" policy for
`AutoRTO`, but no `N` is specified anywhere and no scheduled-job infrastructure exists yet in this
codebase (Hangfire is in the tech stack per `CLAUDE.md` §1 but nothing uses it before Phase 11).

**Resolution 4:** `NdrResolution.AutoRto` is a selectable resolution outcome only this phase —
identical in every way to `NdrResolution.Rto` except the audit trail records which one a caller
chose. No scheduled trigger exists to set it automatically; a human or a future automated caller
(once Phase 11's background-job infrastructure lands and product defines `N`) must still call
`POST /ndrs/{id}/resolve`. This still satisfies `CLARIFICATIONS.md` #4's single-auditable-trigger-
point requirement — `AutoRTO` was never required to be automatic, only to remain a distinct,
recorded resolution reason from an ordinary staff-initiated `Rto`.
```

- [ ] **Step 2: Update `ROADMAP.md`'s Phase 9 section**

Append a `*Status*` paragraph immediately after the existing `*Exit:*` line, matching every prior phase's format:

```markdown
*Status (2026-08-21): Complete.* A new `Ndr` module implements `NDR`/`ReturnShipment` per
`DOMAIN-MODEL.md`, with `CLARIFICATIONS.md` #9 recording three deliberate simplifications
(`ReturnShipment` has no stored status/branch/received-at columns — it reads them live from the
underlying `Shipment`; `NdrReason` is a fresh copy of `Riders.Domain.DeliveryAttemptReason`, not a
shared reference; `AutoRto` is a selectable resolution outcome only, no scheduled trigger exists).
`DELIVERY_FAILED -> NDR_CREATED` happens automatically: `Riders.Application.RiderService.
RecordFailedAttemptAsync` (Phase 8) calls a new narrow `INdrCreationService` in the same request,
immediately after `FailDeliveryAsync` — proven by `tests/Riders.Tests/RiderServiceAttemptsTests.cs`.
NDR's own workflow (`POST /ndrs/{id}/update` walking `Open -> Contacting -> CustomerConfirmed ->
{ReattemptScheduled|RtoRequested}`, `POST /ndrs/{id}/resolve` performing the real side effect) and
the reattempt loop (`NDR_CREATED -> OUT_FOR_DELIVERY` directly, custody untouched, no new
`DeliveryTask` created — a deliberate scope simplification) are exercised end-to-end in
`tests/Api.Tests/NdrEndpointsTests.cs`. `CLARIFICATIONS.md` #4's single-gateway-into-RTO guarantee
(no direct `DELIVERY_FAILED -> RTO_INITIATED` transition anywhere) is proven by
`tests/Shipments.Tests/ShipmentServiceNdrRtoTransitionTests.cs`'s
`InitiateRtoAsync_FromDeliveryFailed_ThrowsInvalidTransition`. RTO's physical return leg reuses
`Manifest` entirely — a new `Manifest.Purpose` field (`Forward|Return`, `CLARIFICATIONS.md` #9)
lets the existing scan/dispatch/receive-scan endpoints drive the RTO status pair
(`RtoInitiated -> RtoInTransit -> RtoAtOrigin`) through a new `/rto-scan` endpoint and
`Purpose`-branched dispatch/receive, proven by `tests/Manifests.Tests/ManifestServicePurposeTests.cs`
and the full `tests/Api.Tests/NdrEndpointsTests.cs`'s
`FullRtoPath_FailedAttemptToRtoDelivered_MovesThroughEveryStatusViaManifests` test (book -> pickup
-> sort/dispatch/receive -> out for delivery -> failed attempt -> NDR workflow -> `Rto` resolution
-> return manifest -> `RTO_DELIVERED`, terminal, over real HTTP end to end). `NDR`/`ReturnShipment`
are both tenant-isolated with their own `OrganizationId` (not transitive), proven by
`Ndrs_OrgBCannotReadOrgAsNdr`. Deferred to later phases: `CODCollection`/rider cash settlement
(Phase 10), a scheduled `AutoRTO` trigger (needs Phase 11's background-job infrastructure and a
product-defined attempt threshold), and a real `DeliveryTask` row for reattempts (the existing
`DeliveryAttempt` audit trail via `AttemptNumber` was judged sufficient for this phase's scope).
```

- [ ] **Step 3: Update `DOMAIN-MODEL.md`'s "NDR" and "RTO" sections**

Replace the existing `## NDR` block with:

```markdown
## NDR — `OrganizationId` present

```text
Ndr { Id, OrganizationId, ShipmentId, Reason (Ndr.Domain.NdrReason — a fresh copy of
      Riders.Domain.DeliveryAttemptReason's 9 values, not a shared reference; see
      CLARIFICATIONS.md #9), AssignedStaffUserId (nullable — set to whichever staff user first
      calls /update), Status (Open|Contacting|CustomerConfirmed|
      ReattemptScheduled|RtoRequested|Resolved), CustomerResponse, FollowUpAt,
      NextAction, Resolution (Reattempt|Rto|AutoRto|Other), ResolvedAt }         [C#4] [C#9]
      # Auto-created by Ndr.Application.NdrCreationService.CreateFromFailedAttemptAsync,
      # called from Riders.Application.RiderService.RecordFailedAttemptAsync immediately after
      # FailDeliveryAsync, in the same request (Phase 9) — DELIVERY_FAILED -> NDR_CREATED is a
      # system step, never a separate manual action.
```
```

Replace the existing `## RTO` block with:

```markdown
## RTO — `OrganizationId` present

```text
ReturnShipment { Id, OriginalShipmentId, OrganizationId, ReturnAwb,
                  RtoReason, OriginBranchId, DestinationBranchId,
                  MerchantConfirmedAt (nullable) }         [C#9]
                  # No Status/CurrentBranchId/ReceivedAt columns (unlike this section's original
                  # sketch) — both are read live from the underlying Shipment (via
                  # OriginalShipmentId) instead of cached here, CLARIFICATIONS.md #9. OriginBranchId
                  # is the ORIGINAL shipment's DestinationBranchId (the return leg starts where
                  # delivery was attempted); DestinationBranchId is the ORIGINAL shipment's
                  # OriginBranchId (the return leg ends where the parcel came from) — the swap
                  # happens once, at creation, in Ndr.Application.NdrService.ResolveAsync.
```
```

- [ ] **Step 4: Commit**

```bash
git add docs/CLARIFICATIONS.md docs/ROADMAP.md docs/DOMAIN-MODEL.md
git commit -m "docs: mark Phase 9 exit criteria met and record its resolved design ambiguities"
```

---

## Self-Review

**Spec coverage:** `NDR`/`ReturnShipment` entities (Tasks 2-3) · automatic `DELIVERY_FAILED -> NDR_CREATED` (Tasks 5-6) · NDR contact/status workflow (Task 7) · reattempt loop back to `OUT_FOR_DELIVERY` (Task 7, proven end-to-end in Task 12) · full RTO path `RTO_INITIATED -> RTO_DELIVERED` via real Manifest transport (Tasks 9-11, proven end-to-end in Task 12) · `CLARIFICATIONS.md` #4's no-direct-shortcut guarantee (regression test in Task 1) · tenant isolation (Task 12) · docs updated (Task 13). Every `ROADMAP.md` Phase 9 exit-criteria clause has a task and a proving test.

**Placeholder scan:** no TBD/TODO markers; every design decision left open by the source docs (RTO transport mechanism, `ReturnShipment`'s status columns, `NdrReason`'s type, AutoRTO's scope) is resolved explicitly in this plan's Architecture section and Task 13's `CLARIFICATIONS.md` entry, not deferred.

**Type consistency:** `IShipmentTransitionService`'s six new method signatures (Task 1) match exactly between the interface, `ShipmentService`'s implementation, and every one of the seven existing fakes plus the four new `Ndr.Tests`/`Manifests.Tests` fakes that implement it across Tasks 1, 5, 7, 9-11. `NdrResponse`/`ReturnShipmentResponse` field shapes are identical everywhere constructed (`NdrService.cs`/`.Resolve.cs`/`.Reads.cs`/`.ReturnShipments.cs`) and consumed (Task 8/11 Presentation, Task 12 tests). `CreateManifestRequest`'s new `Purpose` parameter is a trailing default everywhere — no existing call site needed updating, confirmed by name against all 17 pre-existing call sites plus the new ones this plan adds.
