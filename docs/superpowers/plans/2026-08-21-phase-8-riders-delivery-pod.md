# Phase 8 — Riders, Assignments, Delivery, Delivery Attempts, POD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a rider a real, scan-driven pickup/delivery workflow — assignment, custody, failed-attempt recording, and photo/signature/OTP proof of delivery — replacing the Phase 6 placeholder endpoints that let any staff caller move a shipment through pickup/delivery with a raw `AssignedUserId`.

**Architecture:** A new `Riders` module (mirroring `Manifests`' four-layer shape) owns `Rider, Vehicle, DeliveryRun, DeliveryTask, DeliveryAttempt, DeliveryProof`. It never touches `Shipment`'s `DbSet` directly — it drives real status transitions through five new methods added to `Shipments`' existing `IShipmentTransitionService` (the exact cross-module pattern `Manifests` established in Phase 7), performing the `BRANCH-OPERATIONS.md` §1 five-check validation itself before calling in. A rider is a real `Identity.User` (per `DOMAIN-MODEL.md`), so a new narrow `IUserProvisioningService` in `Identity.Application` lets `Riders` create that `User` + its role/branch grant without touching Identity's tables directly — this also closes a real gap discovered while reading the code: `PickupRider`/`DeliveryRider` are never actually seeded into any organization today, and no endpoint anywhere creates a second staff user at all (only the org-registration owner exists).

**Tech Stack:** ASP.NET Core minimal APIs, EF Core / PostgreSQL, xUnit + EF InMemory (unit) / real Postgres via `WebApplicationFactory` (API tests) — same as every prior phase.

**Spec:** `docs/ROADMAP.md` (Phase 8 section), `docs/DOMAIN-MODEL.md` ("Riders & Delivery"), `CLAUDE.md` §17–19, `docs/SHIPMENT-LIFECYCLE.md`, `docs/BRANCH-OPERATIONS.md` §4, `docs/RBAC.md`. This session's bounded brainstorm (single `Riders` module, `RiderAssignment` dropped, `DeliveryRun` auto-created/reused, `OtpVerified` is a client-reported flag) is the approved design basis — no separate spec file, consistent with every prior phase in this project.

## Global Constraints

- Never trust a client-supplied `TenantId`/`BranchId`/price/COD amount (CLAUDE.md §33) — every write here resolves `OrganizationId` from `ICurrentTenantProvider`, never from the request body.
- Permission checks are permission-based, never role-name-based (CLAUDE.md §6) — reuse the exact catalog keys already seeded (`rider.read/create/assign`, `delivery.assign/attempt/complete`, `shipment.scan`); do not invent new permission keys unless a genuine gap is found (this plan finds one gap — see Task 4).
- Every real status transition writes exactly one append-only `ShipmentEvent`; a rejected scan writes none (CLAUDE.md §11/§39, `BRANCH-OPERATIONS.md` §1).
- `Riders` must never call `_dbContext.Set<Shipment>()`/`Set<ShipmentEvent>()` directly — only through `IShipmentTransitionService`, mirroring `Manifests`.
- Grep-check every new read method against the Phase 4/5 cross-tenant lesson already in project memory: a child table with no own `OrganizationId` is safe only when every accessor filters through its parent's id first.
- No RiderAssignment entity (dropped per this session's brainstorm) — `Rider.BaseBranchId` is the sole branch scope for now.
- No server-side OTP generate/verify flow this phase — `DeliveryProof.OtpVerified` is a client-reported boolean, same trust level as the uploaded signature/photo keys.
- Stop at `DELIVERY_FAILED` — do not build `NDR_CREATED`, the reattempt loop, or RTO (Phase 9's job).

---

## Task 1: Shipments groundwork — `DeliveryFailed`, rider custody, `RiderId` on events

**Files:**
- Modify: `src/Modules/Shipments/Domain/ShipmentStatus.cs`
- Modify: `src/Modules/Shipments/Domain/ShipmentEventType.cs`
- Modify: `src/Modules/Shipments/Application/IShipmentTransitionService.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentDtos.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentService.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentService.Reads.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentService.Transitions.cs`
- Test: `tests/Shipments.Tests/ShipmentServiceTransitionTests.cs`

**Interfaces:**
- Produces: `ShipmentStatus.DeliveryFailed`, `ShipmentEventType.DeliveryFailed`; `ShipmentScanInfo.CurrentCustodianRiderId` (new field, position 7); `ShipmentResponse.CurrentCustodianRiderId` (new field, position 7); private `TransitionAsync(..., Guid? riderId = null)` overload behavior — later tasks (Task 2) call this with a real rider id.

- [ ] **Step 1: Add the two new enum members**

`src/Modules/Shipments/Domain/ShipmentStatus.cs` — add `DeliveryFailed` after `OutForDelivery`:

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
    Delivered,
    Cancelled
}
```

`src/Modules/Shipments/Domain/ShipmentEventType.cs` — add `DeliveryFailed` after `OutForDelivery`:

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
    Delivered,
    Cancelled
}
```

- [ ] **Step 2: Extend `ShipmentScanInfo` and `ShipmentResponse` with rider custody**

In `src/Modules/Shipments/Application/IShipmentTransitionService.cs`, replace the `ShipmentScanInfo` record:

```csharp
// Read-only projection for the Manifests module's five-check scan validation
// (BRANCH-OPERATIONS.md §1) — deliberately narrower than ShipmentResponse: only the fields a scan
// check needs (state + custody), not pricing/recipient/sender data Manifests has no reason to see.
// CurrentCustodianRiderId (Phase 8) lets Riders perform its own check 5 (custody consistency) the
// same way Manifests already does with CurrentBranchId — Riders is the only caller that reads it.
public record ShipmentScanInfo(
    Guid ShipmentId,
    Guid OrganizationId,
    string Awb,
    ShipmentStatus CurrentStatus,
    Guid? CurrentBranchId,
    Guid OriginBranchId,
    Guid DestinationBranchId,
    Guid? CurrentCustodianRiderId);
```

In `src/Modules/Shipments/Application/ShipmentDtos.cs`, add `CurrentCustodianRiderId` to `ShipmentResponse` right after `CurrentCustodianUserId`:

```csharp
public record ShipmentResponse(
    Guid Id,
    string Awb,
    Guid MerchantId,
    Guid OriginBranchId,
    Guid DestinationBranchId,
    Guid? CurrentBranchId,
    Guid? CurrentCustodianUserId,
    Guid? CurrentCustodianRiderId,
    ShipmentStatus CurrentStatus,
    Guid ServiceTypeId,
    decimal WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    decimal DeclaredValue,
    decimal CodAmount,
    bool WantsInsurance,
    decimal DeliveryCharge,
    decimal InsuranceCharge,
    decimal OtherCharges,
    decimal TotalCharge,
    string RecipientName,
    string RecipientPhone,
    string RecipientAddressLine,
    string SenderName,
    string SenderPhone,
    string SenderAddressLine,
    List<ShipmentItemResponse> Items);
```

Leave `AssignPickupRequest`/`AssignDeliveryRequest`/`DeliverShipmentRequest` in place for now — Task 2 removes them once nothing references them.

- [ ] **Step 3: Update `ToResponse`/`ToScanInfo` mappings**

In `src/Modules/Shipments/Application/ShipmentService.cs`, update the `ToResponse` call site:

```csharp
    private static ShipmentResponse ToResponse(Shipment shipment, List<ShipmentItem> items) => new(
        shipment.Id, shipment.Awb, shipment.MerchantId, shipment.OriginBranchId, shipment.DestinationBranchId,
        shipment.CurrentBranchId, shipment.CurrentCustodianUserId, shipment.CurrentCustodianRiderId, shipment.CurrentStatus, shipment.ServiceTypeId,
        shipment.WeightKg, shipment.LengthCm, shipment.WidthCm, shipment.HeightCm,
        shipment.DeclaredValue, shipment.CodAmount, shipment.WantsInsurance,
        shipment.DeliveryCharge, shipment.InsuranceCharge, shipment.OtherCharges, shipment.TotalCharge,
        shipment.RecipientName, shipment.RecipientPhone, shipment.RecipientAddressLine,
        shipment.SenderName, shipment.SenderPhone, shipment.SenderAddressLine,
        items.Select(i => new ShipmentItemResponse(i.Id, i.Description, i.Quantity, i.UnitValue)).ToList());
```

In `src/Modules/Shipments/Application/ShipmentService.Reads.cs`, update `ToScanInfo`:

```csharp
    private static ShipmentScanInfo ToScanInfo(Shipment shipment) => new(
        shipment.Id, shipment.OrganizationId, shipment.Awb, shipment.CurrentStatus,
        shipment.CurrentBranchId, shipment.OriginBranchId, shipment.DestinationBranchId,
        shipment.CurrentCustodianRiderId);
```

- [ ] **Step 4: Thread an optional `riderId` through `TransitionAsync`**

In `src/Modules/Shipments/Application/ShipmentService.Transitions.cs`, change the private helper's signature and body (only the signature line and the `ShipmentEvent` construction change — everything else in the method is unchanged):

```csharp
    private async Task<ShipmentResponse?> TransitionAsync(
        Guid shipmentId, ShipmentStatus expectedFrom, ShipmentStatus to, ShipmentEventType eventType,
        Action<Shipment> applyCustody, string auditAction, ClaimsPrincipal actingUser, CancellationToken cancellationToken,
        string? notes = null, Guid? riderId = null)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }

        if (shipment.CurrentStatus != expectedFrom)
        {
            throw new ShipmentInvalidTransitionException(shipmentId, shipment.CurrentStatus, to);
        }

        var previousStatus = shipment.CurrentStatus;
        var eventBranchId = shipment.CurrentBranchId;
        shipment.CurrentStatus = to;
        applyCustody(shipment);

        _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            EventType = eventType,
            PreviousStatus = previousStatus,
            NewStatus = to,
            BranchId = eventBranchId,
            UserId = actingUser.GetUserId(),
            RiderId = riderId,
            Timestamp = DateTimeOffset.UtcNow,
            Notes = notes
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ShipmentInvalidTransitionException(shipmentId, expectedFrom, to);
        }
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), auditAction, "Shipment", shipmentId.ToString()), cancellationToken);

        return await LoadResponseAsync(shipmentId, cancellationToken);
    }
```

- [ ] **Step 5: Build to confirm the module still compiles (test files will be fixed in Task 2)**

Run: `dotnet build src/Modules/Shipments/Shipments.csproj`
Expected: Build succeeds (nothing outside this project references the changed members yet except `ShipmentService.Transitions.cs`'s own callers, which still pass the same positional args since `riderId` defaults to `null`).

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Shipments/Domain/ShipmentStatus.cs src/Modules/Shipments/Domain/ShipmentEventType.cs src/Modules/Shipments/Application/IShipmentTransitionService.cs src/Modules/Shipments/Application/ShipmentDtos.cs src/Modules/Shipments/Application/ShipmentService.cs src/Modules/Shipments/Application/ShipmentService.Reads.cs src/Modules/Shipments/Application/ShipmentService.Transitions.cs
git commit -m "feat(shipments): add DeliveryFailed status and rider custody fields for Phase 8"
```

---

## Task 2: Shipments — rider-facing transition methods; retire the Phase 6 placeholder pickup/delivery endpoints

This is one task (not split further) because removing `AssignPickupAsync`/`PickupScanAsync`/`AssignDeliveryAsync`/`DeliverAsync` breaks compilation everywhere they're still called — the removal and every caller fix must land together for the repo to build at any intermediate point.

**Files:**
- Modify: `src/Modules/Shipments/Application/IShipmentTransitionService.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentDtos.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentService.Transitions.cs`
- Modify: `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs`
- Modify: `tests/Shipments.Tests/ShipmentServiceTransitionTests.cs`
- Modify: `tests/Api.Tests/ShipmentsEndpointsTests.cs`
- Modify: `tests/Api.Tests/ManifestsEndpointsTests.cs`

**Interfaces:**
- Consumes: `TransitionAsync(..., Guid? riderId = null)` from Task 1; `ShipmentScanInfo.CurrentCustodianRiderId` from Task 1.
- Produces (new `IShipmentTransitionService` members Task 9's `RiderService` calls): `AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken)`, `RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken)`, `AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken)`, `CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken)`, `FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken)` — all return `Task<ShipmentResponse?>`, all `null` iff shipment not found, all perform ONLY the state-machine check (BRANCH-OPERATIONS §1 check 3) — callers (Riders) must do checks 1/2/4/5 themselves first, exactly like `SortAsync`/`ReceiveAtDestinationAsync` already document.

- [ ] **Step 1: Design decision note — why `RiderDelivery` has no separate scan step**

Add this comment to the top of `ShipmentService.Transitions.cs` (above the class, inside the `namespace` block) — it resolves an ambiguity between `BRANCH-OPERATIONS.md` §4 (which names `RiderDelivery` as if it were its own scan action) and `SHIPMENT-LIFECYCLE.md`'s transition table (which shows exactly ONE arrow, "delivery rider assignment + out-for-delivery scan", into `OUT_FOR_DELIVERY` — never two):

```csharp
// This plan's resolved design decision: RiderDelivery is NOT a second call after assignment the
// way RiderPickup is (assign -> PICKUP_ASSIGNED, then a separate pickup scan -> PICKED_UP).
// SHIPMENT-LIFECYCLE.md's transition table has exactly one arrow into OUT_FOR_DELIVERY ("delivery
// rider assignment + out-for-delivery scan"), matching Phase 6's AssignDeliveryAsync having no
// sibling scan endpoint at all. So AssignDeliveryToRiderAsync below IS the RiderDelivery scan —
// assigning a delivery task to a rider and scanning it out for delivery are the same action.
// Pickup keeps its two-step shape (an explicit PICKUP_ASSIGNED status exists for dispatch boards
// to show "assigned but not yet physically collected"); delivery has no equivalent named status
// between "assigned" and "out for delivery", so there's nothing for a second step to represent.
```

- [ ] **Step 2: Add the five rider-facing methods**

Append to `src/Modules/Shipments/Application/ShipmentService.Transitions.cs`, replacing `AssignPickupAsync`, `PickupScanAsync`'s branch-check body stays but delivery methods below are new — first, **delete** `AssignPickupAsync`, `AssignDeliveryAsync`, and `DeliverAsync` entirely (search for these three method bodies and remove them), then **change** `PickupScanAsync` to accept and check a rider, then **add** the four new methods. The full resulting file:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

// This plan's resolved design decision: RiderDelivery is NOT a second call after assignment the
// way RiderPickup is (assign -> PICKUP_ASSIGNED, then a separate pickup scan -> PICKED_UP).
// SHIPMENT-LIFECYCLE.md's transition table has exactly one arrow into OUT_FOR_DELIVERY ("delivery
// rider assignment + out-for-delivery scan"), matching Phase 6's AssignDeliveryAsync having no
// sibling scan endpoint at all. So AssignDeliveryToRiderAsync below IS the RiderDelivery scan —
// assigning a delivery task to a rider and scanning it out for delivery are the same action.
// Pickup keeps its two-step shape (an explicit PICKUP_ASSIGNED status exists for dispatch boards
// to show "assigned but not yet physically collected"); delivery has no equivalent named status
// between "assigned" and "out for delivery", so there's nothing for a second step to represent.
public partial class ShipmentService
{
    private async Task<ShipmentResponse?> TransitionAsync(
        Guid shipmentId, ShipmentStatus expectedFrom, ShipmentStatus to, ShipmentEventType eventType,
        Action<Shipment> applyCustody, string auditAction, ClaimsPrincipal actingUser, CancellationToken cancellationToken,
        string? notes = null, Guid? riderId = null)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }

        if (shipment.CurrentStatus != expectedFrom)
        {
            throw new ShipmentInvalidTransitionException(shipmentId, shipment.CurrentStatus, to);
        }

        var previousStatus = shipment.CurrentStatus;
        var eventBranchId = shipment.CurrentBranchId;
        shipment.CurrentStatus = to;
        applyCustody(shipment);

        _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            EventType = eventType,
            PreviousStatus = previousStatus,
            NewStatus = to,
            BranchId = eventBranchId,
            UserId = actingUser.GetUserId(),
            RiderId = riderId,
            Timestamp = DateTimeOffset.UtcNow,
            Notes = notes
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ShipmentInvalidTransitionException(shipmentId, expectedFrom, to);
        }
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), auditAction, "Shipment", shipmentId.ToString()), cancellationToken);

        return await LoadResponseAsync(shipmentId, cancellationToken);
    }

    private async Task CheckBranchAccessAsync(ClaimsPrincipal actingUser, string permission, Guid branchId, CancellationToken cancellationToken)
    {
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, permission, cancellationToken);
        if (accessible is not null && !accessible.Contains(branchId))
        {
            throw new ShipmentValidationException($"Branch is outside your branch access for permission '{permission}'.");
        }
    }

    // Callers of every method below are Riders.Application.RiderService, which performs
    // BRANCH-OPERATIONS.md §1 checks 1 (permission), 2 (branch access), 4 (expected operation), and
    // 5 (custody consistency) itself before ever reaching here — exactly the same division of
    // responsibility Phase 7 established between ManifestService and these Sort/Receive methods.
    // Only check 3 (shipment state, via TransitionAsync's expectedFrom guard) happens here.

    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.Created, ShipmentStatus.PickupAssigned, ShipmentEventType.PickupAssigned,
            s => s.CurrentCustodianRiderId = riderId, "shipment.pickup_assigned", actingUser, cancellationToken, riderId: riderId);

    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.PickupAssigned, ShipmentStatus.PickedUp, ShipmentEventType.PickedUp,
            _ => { }, "shipment.picked_up", actingUser, cancellationToken, riderId: riderId);

    public async Task<ShipmentResponse?> ReceiveAtOriginAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        await CheckBranchAccessAsync(actingUser, "shipment.scan", shipment.OriginBranchId, cancellationToken);

        return await TransitionAsync(shipmentId, ShipmentStatus.PickedUp, ShipmentStatus.AtOriginBranch, ShipmentEventType.ReceivedAtOrigin,
            s => { s.CurrentBranchId = s.OriginBranchId; s.CurrentCustodianUserId = null; s.CurrentCustodianRiderId = null; }, "shipment.received_at_origin", actingUser, cancellationToken);
    }

    // Branch-scope/permission enforcement for this operation lives one layer up in
    // Manifests.Application.ManifestService.ScanAsync (BRANCH-OPERATIONS.md §1 checks 1/2), which
    // is the only caller of this method as of Phase 7.
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.AtOriginBranch, ShipmentStatus.Sorted, ShipmentEventType.Sorted,
            _ => { }, "shipment.sorted", actingUser, cancellationToken);

    // Used exclusively by Manifests.Application.ManifestService.DispatchAsync — see Phase 7's
    // reasoning in this same file's history for why this stages N shipments into one SaveChangesAsync.
    public async Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var shipments = await _dbContext.Set<Shipment>().Where(s => shipmentIds.Contains(s.Id)).ToListAsync(cancellationToken);
        var actingUserId = actingUser.GetUserId();
        var now = DateTimeOffset.UtcNow;

        foreach (var shipment in shipments)
        {
            if (shipment.CurrentStatus != ShipmentStatus.Sorted)
            {
                throw new ShipmentInvalidTransitionException(shipment.Id, shipment.CurrentStatus, ShipmentStatus.InTransit);
            }
            var previousStatus = shipment.CurrentStatus;
            var eventBranchId = shipment.CurrentBranchId;
            shipment.CurrentStatus = ShipmentStatus.InTransit;
            shipment.CurrentBranchId = null;

            _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                EventType = ShipmentEventType.Dispatched,
                PreviousStatus = previousStatus,
                NewStatus = ShipmentStatus.InTransit,
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
            throw new ShipmentValidationException("One or more shipments on this manifest were modified concurrently; dispatch was not applied.");
        }
    }

    // Same reasoning as SortAsync above: Manifests.Application.ManifestService.ReceiveScanAsync is
    // the only caller as of Phase 7.
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.InTransit, ShipmentStatus.AtDestinationBranch, ShipmentEventType.ReceivedAtDestination,
            s => s.CurrentBranchId = s.DestinationBranchId, "shipment.received_at_destination", actingUser, cancellationToken);

    // IS the RiderDelivery scan — see this file's top-of-namespace comment for why there's no
    // separate second "delivery scan" step the way pickup has one.
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.AtDestinationBranch, ShipmentStatus.OutForDelivery, ShipmentEventType.OutForDelivery,
            s => { s.CurrentCustodianRiderId = riderId; s.CurrentBranchId = null; }, "shipment.out_for_delivery", actingUser, cancellationToken, riderId: riderId);

    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.OutForDelivery, ShipmentStatus.Delivered, ShipmentEventType.Delivered,
            s => s.CurrentCustodianRiderId = null, "shipment.delivered", actingUser, cancellationToken,
            notes: $"Delivered to: {recipientName}.", riderId: riderId);

    // Custody deliberately stays with the rider (CurrentCustodianRiderId is NOT cleared) — the
    // parcel is still in the rider's physical possession after a failed attempt, it hasn't gone
    // back to a branch. Phase 9's NDR/reattempt/RTO flow is what eventually moves custody again.
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
        TransitionAsync(shipmentId, ShipmentStatus.OutForDelivery, ShipmentStatus.DeliveryFailed, ShipmentEventType.DeliveryFailed,
            _ => { }, "shipment.delivery_failed", actingUser, cancellationToken, riderId: riderId);

    public async Task<ShipmentResponse?> CancelAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        if (actingUser.GetMerchantId() is Guid callerMerchantId && shipment.MerchantId != callerMerchantId)
        {
            return null;
        }

        if (shipment.CurrentStatus != ShipmentStatus.Created && shipment.CurrentStatus != ShipmentStatus.PickupAssigned)
        {
            throw new ShipmentInvalidTransitionException(shipmentId, shipment.CurrentStatus, ShipmentStatus.Cancelled);
        }

        var previousStatus = shipment.CurrentStatus;
        var eventBranchId = shipment.CurrentBranchId;
        shipment.CurrentStatus = ShipmentStatus.Cancelled;
        shipment.CurrentCustodianUserId = null;
        shipment.CurrentCustodianRiderId = null;
        shipment.CurrentBranchId = null;

        _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            EventType = ShipmentEventType.Cancelled,
            PreviousStatus = previousStatus,
            NewStatus = ShipmentStatus.Cancelled,
            BranchId = eventBranchId,
            UserId = actingUser.GetUserId(),
            Timestamp = DateTimeOffset.UtcNow
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ShipmentInvalidTransitionException(shipmentId, previousStatus, ShipmentStatus.Cancelled);
        }
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "shipment.cancelled", "Shipment", shipmentId.ToString()), cancellationToken);

        return await LoadResponseAsync(shipmentId, cancellationToken);
    }
}
```

- [ ] **Step 3: Update `IShipmentTransitionService`**

Replace `src/Modules/Shipments/Application/IShipmentTransitionService.cs` in full:

```csharp
using System.Security.Claims;
using Courier.Shipments.Domain;

namespace Courier.Shipments.Application;

// Read-only projection for the Manifests module's five-check scan validation
// (BRANCH-OPERATIONS.md §1) — deliberately narrower than ShipmentResponse: only the fields a scan
// check needs (state + custody), not pricing/recipient/sender data Manifests has no reason to see.
// CurrentCustodianRiderId (Phase 8) lets Riders perform its own check 5 (custody consistency) the
// same way Manifests already does with CurrentBranchId — Riders is the only caller that reads it.
public record ShipmentScanInfo(
    Guid ShipmentId,
    Guid OrganizationId,
    string Awb,
    ShipmentStatus CurrentStatus,
    Guid? CurrentBranchId,
    Guid OriginBranchId,
    Guid DestinationBranchId,
    Guid? CurrentCustodianRiderId);

// Lets Manifests and Riders drive real shipment-status transitions without touching Shipment's
// DbSet directly. Both callers perform the permission + branch-access + expected-operation +
// custody checks themselves (BRANCH-OPERATIONS.md §1 checks 1/2/4/5) before calling these — the
// methods below only enforce the state-machine check (#3, via the underlying TransitionAsync's
// expectedFrom guard).
public interface IShipmentTransitionService
{
    Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default);
    Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);

    // Phase 8 — driven by Riders.Application.RiderService.
    Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Remove `AssignPickupRequest`/`AssignDeliveryRequest`/`DeliverShipmentRequest` from `ShipmentDtos.cs`**

In `src/Modules/Shipments/Application/ShipmentDtos.cs`, delete this line entirely (nothing else in the file changes):

```csharp
public record AssignPickupRequest(Guid AssignedUserId);
public record AssignDeliveryRequest(Guid AssignedUserId);
public record DeliverShipmentRequest(string RecipientName, string? Notes);
```

- [ ] **Step 5: Remove the four retired routes from `ShipmentsEndpoints.cs`**

In `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs`, delete these four route registrations:

```csharp
        group.MapPost("/{id:guid}/pickup-assign", async (Guid id, AssignPickupRequest request, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.AssignPickupAsync(id, request, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("shipment.assign");

        group.MapPost("/{id:guid}/pickup-scan", async (Guid id, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.PickupScanAsync(id, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("shipment.scan");
```

and

```csharp
        group.MapPost("/{id:guid}/out-for-delivery", async (Guid id, AssignDeliveryRequest request, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.AssignDeliveryAsync(id, request, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("delivery.assign");

        group.MapPost("/{id:guid}/deliver", async (Guid id, DeliverShipmentRequest request, ShipmentService service, HttpContext httpContext) =>
            await HandleTransitionAsync(() => service.DeliverAsync(id, request, httpContext.User, httpContext.RequestAborted), id))
            .RequirePermission("delivery.complete");
```

Leave `receive-origin` and `cancel` in place (unchanged this phase). The file's `HandleTransitionAsync` helper and `HasReadAccessAsync` are untouched.

- [ ] **Step 6: Fix `tests/Shipments.Tests/ShipmentServiceTransitionTests.cs`**

Replace the file's pickup/delivery-dependent tests. The fakes (`FixedTenantProvider`, `NoopAuditLogger`, `FakeBranchLookup`, `AlwaysTrueGeographyLookup`, `AlwaysTrueMerchantLookup`, `AllBranchesAuthorizationChecker`, `NoBranchesAuthorizationChecker`, `FixedOrganizationLookup`), `TestDbContext`, `CreateContext`, `ActingUser`, `CreateService`, and `SeedCreatedShipmentAsync` are unchanged — only the six test methods below the class-level fixtures change. Replace from `[Fact] public async Task FullHappyPath_...` through the end of `DeliverAsync_CallerDeniedBranchAccess_ThrowsShipmentValidationException` (i.e. replace the five tests `FullHappyPath_CreatedToDelivered_WalksEveryTransitionWithCorrectEventsAndCustody`, `DeliverAsync_WritesRecipientNameAndNotesOntoDeliveredEvent`, `PickupScanAsync_CallerDeniedBranchAccess_ThrowsShipmentValidationException`, `DeliverAsync_CallerDeniedBranchAccess_ThrowsShipmentValidationException`, and update `PickupScanAsync_FromCreatedStatus_ThrowsInvalidTransition`/`CancelAsync_FromPickedUp_ThrowsInvalidTransition`'s bodies) with:

```csharp
    [Fact]
    public async Task FullHappyPath_CreatedToDelivered_WalksEveryTransitionWithCorrectEventsAndCustody()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, originBranchId, destinationBranchId) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        var riderId = Guid.NewGuid();

        var afterAssign = await service.AssignPickupToRiderAsync(shipmentId, riderId, actingUser);
        Assert.Equal(ShipmentStatus.PickupAssigned, afterAssign!.CurrentStatus);
        Assert.Equal(riderId, afterAssign.CurrentCustodianRiderId);

        var afterScan = await service.RiderPickupScanAsync(shipmentId, riderId, actingUser);
        Assert.Equal(ShipmentStatus.PickedUp, afterScan!.CurrentStatus);
        Assert.Equal(riderId, afterScan.CurrentCustodianRiderId);

        var afterReceiveOrigin = await service.ReceiveAtOriginAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.AtOriginBranch, afterReceiveOrigin!.CurrentStatus);
        Assert.Equal(originBranchId, afterReceiveOrigin.CurrentBranchId);
        Assert.Null(afterReceiveOrigin.CurrentCustodianRiderId);

        var afterSort = await service.SortAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.Sorted, afterSort!.CurrentStatus);
        Assert.Equal(originBranchId, afterSort.CurrentBranchId);

        await service.DispatchManyAsync(new[] { shipmentId }, actingUser);
        var afterDispatch = await dbContext.Set<Shipment>().FirstAsync(s => s.Id == shipmentId);
        Assert.Equal(ShipmentStatus.InTransit, afterDispatch.CurrentStatus);
        Assert.Null(afterDispatch.CurrentBranchId);

        var afterReceiveDest = await service.ReceiveAtDestinationAsync(shipmentId, actingUser);
        Assert.Equal(ShipmentStatus.AtDestinationBranch, afterReceiveDest!.CurrentStatus);
        Assert.Equal(destinationBranchId, afterReceiveDest.CurrentBranchId);

        var afterOutForDelivery = await service.AssignDeliveryToRiderAsync(shipmentId, riderId, actingUser);
        Assert.Equal(ShipmentStatus.OutForDelivery, afterOutForDelivery!.CurrentStatus);
        Assert.Equal(riderId, afterOutForDelivery.CurrentCustodianRiderId);
        Assert.Null(afterOutForDelivery.CurrentBranchId);

        var afterDeliver = await service.CompleteDeliveryAsync(shipmentId, riderId, "Recipient", actingUser);
        Assert.Equal(ShipmentStatus.Delivered, afterDeliver!.CurrentStatus);
        Assert.Null(afterDeliver.CurrentCustodianRiderId);
        Assert.Null(afterDeliver.CurrentBranchId);

        var events = await dbContext.Set<ShipmentEvent>().Where(e => e.ShipmentId == shipmentId).OrderBy(e => e.Timestamp).ToListAsync();
        Assert.Equal(9, events.Count);
        Assert.Equal(new[]
        {
            ShipmentEventType.Created, ShipmentEventType.PickupAssigned, ShipmentEventType.PickedUp,
            ShipmentEventType.ReceivedAtOrigin, ShipmentEventType.Sorted, ShipmentEventType.Dispatched,
            ShipmentEventType.ReceivedAtDestination, ShipmentEventType.OutForDelivery, ShipmentEventType.Delivered
        }, events.Select(e => e.EventType));
        Assert.All(events.Where(e => e.EventType is ShipmentEventType.PickupAssigned or ShipmentEventType.PickedUp or ShipmentEventType.OutForDelivery or ShipmentEventType.Delivered),
            e => Assert.Equal(riderId, e.RiderId));

        var dispatchedEvent = events.Single(e => e.EventType == ShipmentEventType.Dispatched);
        Assert.Equal(originBranchId, dispatchedEvent.BranchId);
        var outForDeliveryEvent = events.Single(e => e.EventType == ShipmentEventType.OutForDelivery);
        Assert.Equal(destinationBranchId, outForDeliveryEvent.BranchId);
    }

    [Fact]
    public async Task FailDeliveryAsync_FromOutForDelivery_TransitionsToDeliveryFailedAndKeepsRiderCustody()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        var riderId = Guid.NewGuid();
        await service.AssignPickupToRiderAsync(shipmentId, riderId, actingUser);
        await service.RiderPickupScanAsync(shipmentId, riderId, actingUser);
        await service.ReceiveAtOriginAsync(shipmentId, actingUser);
        await service.SortAsync(shipmentId, actingUser);
        await service.DispatchManyAsync(new[] { shipmentId }, actingUser);
        await service.ReceiveAtDestinationAsync(shipmentId, actingUser);
        await service.AssignDeliveryToRiderAsync(shipmentId, riderId, actingUser);

        var result = await service.FailDeliveryAsync(shipmentId, riderId, actingUser);

        Assert.Equal(ShipmentStatus.DeliveryFailed, result!.CurrentStatus);
        // Regression: custody must NOT be cleared on failure — the parcel is still physically with
        // the rider, it hasn't gone back to a branch (this file's Task 2 design note).
        Assert.Equal(riderId, result.CurrentCustodianRiderId);
    }

    [Fact]
    public async Task PickupScanAsync_CallerDeniedBranchAccess_IsEnforcedByRidersModuleNotShipments()
    {
        // Regression guard for the Phase 7/8 division of responsibility: RiderPickupScanAsync no
        // longer performs its own branch-access check (Riders.Application.RiderService does, before
        // ever calling in) — proven here by showing a "denied" IBranchAuthorizationChecker has NO
        // effect on this method, unlike the old PickupScanAsync it replaced.
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        var riderId = Guid.NewGuid();
        await service.AssignPickupToRiderAsync(shipmentId, riderId, actingUser);

        var deniedService = CreateService(dbContext, organizationId, new(), new NoBranchesAuthorizationChecker());
        var result = await deniedService.RiderPickupScanAsync(shipmentId, riderId, actingUser);

        Assert.Equal(ShipmentStatus.PickedUp, result!.CurrentStatus);
    }

    [Fact]
    public async Task PickupScanAsync_FromCreatedStatus_ThrowsInvalidTransition()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);

        var ex = await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => service.RiderPickupScanAsync(shipmentId, Guid.NewGuid(), ActingUser(Guid.NewGuid())));
        Assert.Equal(ShipmentStatus.Created, ex.From);
        Assert.Equal(ShipmentStatus.PickedUp, ex.To);
    }

    [Fact]
    public async Task CancelAsync_FromCreated_Succeeds()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, originBranchId, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);

        var result = await service.CancelAsync(shipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.Cancelled, result!.CurrentStatus);

        var cancelledEvent = await dbContext.Set<ShipmentEvent>()
            .Where(e => e.ShipmentId == shipmentId && e.EventType == ShipmentEventType.Cancelled)
            .SingleAsync();
        Assert.Equal(originBranchId, cancelledEvent.BranchId);
    }

    [Fact]
    public async Task CancelAsync_FromPickedUp_ThrowsInvalidTransition()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var (service, shipmentId, _, _) = await SeedCreatedShipmentAsync(dbContext, organizationId);
        var actingUser = ActingUser(Guid.NewGuid());
        await service.AssignPickupToRiderAsync(shipmentId, Guid.NewGuid(), actingUser);
        await service.RiderPickupScanAsync(shipmentId, Guid.NewGuid(), actingUser);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => service.CancelAsync(shipmentId, actingUser));
    }
}
```

(The closing `}` above closes the `ShipmentServiceTransitionTests` class — make sure no duplicate closing brace remains from the original file.)

- [ ] **Step 7: Run the Shipments unit tests**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj`
Expected: All tests pass, including the six above.

- [ ] **Step 8: Fix `tests/Api.Tests/ShipmentsEndpointsTests.cs`**

Three tests in this file call the retired endpoints. `FullHappyPath_CreatedToDelivered_Succeeds` and `Deliver_ConcurrentRequestsForSameShipment_ExactlyOneSucceeds` are superseded by Task 13's Riders-module API tests (which exercise the identical lifecycle through real rider endpoints, including the concurrency race against `CompleteDeliveryAsync`) — delete both. `Deliver_FromCreatedStatus_Returns400WithInvalidTransitionDetails` tested `POST /shipments/{id}/deliver`, which no longer exists — delete it too (Task 13 covers the equivalent "wrong state" case against the new endpoint). Remove all three `[Fact]` methods (`FullHappyPath_CreatedToDelivered_Succeeds`, `Deliver_FromCreatedStatus_Returns400WithInvalidTransitionDetails`, `Deliver_ConcurrentRequestsForSameShipment_ExactlyOneSucceeds`) and their doc comments from the file; leave every other test (pricing snapshot, tenant isolation, validation tests, etc.) untouched.

- [ ] **Step 9: Fix `tests/Api.Tests/ManifestsEndpointsTests.cs`'s origin-branch helper**

`CreateShipmentAtOriginBranchAsync` walks a shipment to `AtOriginBranch` using the now-deleted `pickup-assign`/`pickup-scan` endpoints. Since `Manifests`' own tests only need a shipment sitting at `AtOriginBranch` (not a real rider), replace the two dead calls with direct `ShipmentService` transition calls via a test-only staff path — the simplest fix that keeps this file decoupled from `Riders` (which Task 13, not this task, owns end-to-end): keep booking through the real `POST /shipments` HTTP call, then call the still-`internal`-visible-only-via-HTTP... no internal access exists across test projects, so instead assign a rider inline. Replace the method:

```csharp
    // Walks a freshly booked shipment through Riders' real pickup flow up to AtOriginBranch — the
    // point at which this phase's /manifests/{id}/scan takes over. Uses a throwaway rider created
    // solely to satisfy the now-real (Phase 8) pickup-assignment requirement; Manifests' own tests
    // don't otherwise care which rider performed the pickup.
    private async Task<ShipmentResponse> CreateShipmentAtOriginBranchAsync(HttpClient client, Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId)
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        var riderResponse = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Pickup Rider", null, $"LIC-{Guid.NewGuid():N}"[..12], originBranchId, RiderRoleType.PickupRider));
        var rider = await riderResponse.Content.ReadFromJsonAsync<RiderResponse>();

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(rider!.Id, shipment!.Id, DeliveryTaskType.Pickup));
        await client.PostAsJsonAsync($"/api/v1/riders/{rider.Id}/scan/pickup", new RiderScanRequest(shipment.Awb));
        var afterOrigin = await (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { })).Content.ReadFromJsonAsync<ShipmentResponse>();
        return afterOrigin!;
    }
```

Add `using Courier.Riders.Application;` and `using Courier.Riders.Domain;` to the file's `using` block. This step's code will not compile until the full `Riders` module (Domain through Presentation, Tasks 4–11) exists, so **defer literally applying this step's file edit until Task 11 Step 5**, which performs this exact edit as part of making the whole solution build again. This step exists here only to document the target end-state; do not check its checkbox until Task 11 Step 5 is done.

- [ ] **Step 10: Commit (Shipments-only changes; Steps 1–8)**

```bash
git add src/Modules/Shipments/Application/IShipmentTransitionService.cs src/Modules/Shipments/Application/ShipmentDtos.cs src/Modules/Shipments/Application/ShipmentService.Transitions.cs src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs tests/Shipments.Tests/ShipmentServiceTransitionTests.cs tests/Api.Tests/ShipmentsEndpointsTests.cs
git commit -m "feat(shipments): retire placeholder pickup/delivery endpoints for real rider transitions"
```

Do not commit `ManifestsEndpointsTests.cs` yet — it doesn't compile until Task 9.

---

## Task 3: Identity — seed `PickupRider`/`DeliveryRider` roles; add `IUserProvisioningService`

Closes the gap found while reading the code: `RegisterOrganizationAsync` only ever seeds `OrganizationOwner`, and nothing anywhere creates a second staff `User`. `Rider.UserId` (Task 5) needs both to exist.

**Files:**
- Modify: `src/Modules/Identity/Application/AuthService.cs`
- Create: `src/Modules/Identity/Application/IUserProvisioningService.cs`
- Create: `src/Modules/Identity/Application/UserProvisioningService.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Identity.Tests/UserProvisioningServiceTests.cs`

**Interfaces:**
- Produces: `IUserProvisioningService.ProvisionStaffUserAsync(ProvisionStaffUserRequest, Guid organizationId, Guid grantedByUserId, CancellationToken)` returning the new `User`'s `Guid` id — Task 8's `RiderService.CreateRiderAsync` consumes this. Two new seeded role names per org: `"PickupRider"` (permission: `shipment.scan`), `"DeliveryRider"` (permissions: `delivery.attempt`, `delivery.complete`, `cod.collect`, `shipment.scan`), both `BranchScope.SpecificBranches`.

- [ ] **Step 1: Seed the two rider roles in `RegisterOrganizationAsync`**

In `src/Modules/Identity/Application/AuthService.cs`, insert this block immediately after the existing `ownerRole`-seeding `foreach` loop (right before `var owner = new User`):

```csharp
        // Phase 8: PickupRider/DeliveryRider are seeded here (not lazily on first rider creation)
        // so they exist from org registration onward, matching how OrganizationOwner already works
        // — RiderService (Riders module) looks them up by name via IUserProvisioningService and
        // never creates roles itself. Permission sets per RBAC.md §4 (DeliveryRider is explicit
        // there) and CLARIFICATIONS.md's new Phase 8 entry (PickupRider — not itemized in RBAC.md,
        // resolved to shipment.scan only, since a pickup rider's whole job is the pickup scan).
        var deliveryRiderPermissionKeys = new[] { "delivery.attempt", "delivery.complete", "cod.collect", "shipment.scan" };
        var deliveryRiderRole = new Role
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Name = "DeliveryRider",
            IsSystemDefault = true
        };
        _dbContext.Set<Role>().Add(deliveryRiderRole);
        foreach (var permission in allPermissions.Where(p => deliveryRiderPermissionKeys.Contains(p.Key)))
        {
            deliveryRiderRole.Permissions.Add(permission);
        }

        var pickupRiderRole = new Role
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Name = "PickupRider",
            IsSystemDefault = true
        };
        _dbContext.Set<Role>().Add(pickupRiderRole);
        foreach (var permission in allPermissions.Where(p => p.Key == "shipment.scan"))
        {
            pickupRiderRole.Permissions.Add(permission);
        }

```

- [ ] **Step 2: Add `IUserProvisioningService`**

Create `src/Modules/Identity/Application/IUserProvisioningService.cs`:

```csharp
namespace Courier.Identity.Application;

public record ProvisionStaffUserRequest(string Email, string Password, string FullName, string? Phone, string RoleName, Guid BranchId);

// Creates a new staff User + role/branch grant without letting a calling module (Riders, Task 8)
// touch Identity's DbSet<User>/DbSet<Role>/DbSet<UserRole>/DbSet<UserBranch> directly — the same
// narrow-interface discipline as IBranchLookupService (Phase 5) and IShipmentTransitionService
// (Phase 7). Deliberately NOT a general "create any user with any role" endpoint (that remains
// deferred, per this plan's YAGNI scoping) — RoleName must already be a role seeded for this
// organization (RegisterOrganizationAsync seeds OrganizationOwner, PickupRider, DeliveryRider).
public interface IUserProvisioningService
{
    Task<Guid> ProvisionStaffUserAsync(ProvisionStaffUserRequest request, Guid organizationId, Guid grantedByUserId, CancellationToken cancellationToken = default);
}

public class UserProvisioningException : Exception
{
    public UserProvisioningException(string message) : base(message) { }
}
```

- [ ] **Step 3: Implement it**

Create `src/Modules/Identity/Application/UserProvisioningService.cs`:

```csharp
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class UserProvisioningService : IUserProvisioningService
{
    private readonly DbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;

    public UserProvisioningService(DbContext dbContext, IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    public async Task<Guid> ProvisionStaffUserAsync(ProvisionStaffUserRequest request, Guid organizationId, Guid grantedByUserId, CancellationToken cancellationToken = default)
    {
        var email = request.Email.ToLowerInvariant();
        // Same platform-wide uniqueness check as RegisterOrganizationAsync — bypasses the tenant
        // query filter deliberately, since a duplicate email in ANY org must be rejected (User.Email
        // has no per-org uniqueness, it's globally unique for login lookup).
        var emailTaken = await _dbContext.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Email == email, cancellationToken);
        if (emailTaken)
        {
            throw new UserProvisioningException("Email is already registered.");
        }

        var role = await _dbContext.Set<Role>()
            .FirstOrDefaultAsync(r => r.OrganizationId == organizationId && r.Name == request.RoleName, cancellationToken)
            ?? throw new UserProvisioningException($"Role '{request.RoleName}' is not a seeded role for this organization.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            FullName = request.FullName,
            Phone = request.Phone
        };
        _dbContext.Set<User>().Add(user);

        var now = DateTimeOffset.UtcNow;
        _dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RoleId = role.Id,
            OrganizationId = organizationId,
            BranchScope = BranchScope.SpecificBranches,
            AssignedAt = now,
            AssignedByUserId = grantedByUserId
        });
        // Without this, IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync would return an
        // empty set for this user (SpecificBranches scope, zero UserBranch rows) — every
        // branch-scoped permission check the new rider needs would fail immediately.
        _dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            BranchId = request.BranchId,
            OrganizationId = organizationId,
            GrantedAt = now,
            GrantedByUserId = grantedByUserId
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return user.Id;
    }
}
```

- [ ] **Step 4: Wire into `Program.cs`**

In `src/Api/Program.cs`, add after the existing `builder.Services.AddScoped<AuthService>();` line:

```csharp
builder.Services.AddScoped<IUserProvisioningService, UserProvisioningService>();
```

- [ ] **Step 5: Write the failing test**

Create `tests/Identity.Tests/UserProvisioningServiceTests.cs`:

```csharp
using Courier.Identity.Application;
using Courier.Identity.Infrastructure;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Identity.Tests;

file class NullTenantProvider : ICurrentTenantProvider
{
    public Guid? OrganizationId => null;
}

public class UserProvisioningServiceTests
{
    private static AppDbContext CreateContext() => TestDbContextFactory.Create(new NullTenantProvider());

    private static AuthService CreateAuthService(AppDbContext dbContext)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            SigningKey = "identity-tests-signing-key-at-least-32-bytes!",
            Issuer = "courier-tests",
            Audience = "courier-tests"
        });
        return new AuthService(dbContext, new PasswordHasherAdapter(), new JwtTokenService(jwtOptions), new NoopAuditLogger());
    }

    private class NoopAuditLogger : Courier.Audit.Application.IAuditLogger
    {
        public Task LogAsync(Courier.Audit.Application.AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task<(Guid OrganizationId, Guid OwnerUserId)> RegisterOrgAsync(AppDbContext dbContext)
    {
        var authService = CreateAuthService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var result = await authService.RegisterOrganizationAsync(new RegisterOrganizationRequest(
            $"Test Org {suffix}", $"test-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person"));
        return (result.OrganizationId, result.UserId);
    }

    [Fact]
    public async Task RegisterOrganizationAsync_SeedsPickupRiderAndDeliveryRiderRoles()
    {
        await using var dbContext = CreateContext();
        var (organizationId, _) = await RegisterOrgAsync(dbContext);

        var roleNames = dbContext.Set<Courier.Identity.Domain.Role>().IgnoreQueryFilters()
            .Where(r => r.OrganizationId == organizationId)
            .Select(r => r.Name)
            .ToList();

        Assert.Contains("PickupRider", roleNames);
        Assert.Contains("DeliveryRider", roleNames);
    }

    [Fact]
    public async Task ProvisionStaffUserAsync_ValidRole_CreatesUserWithRoleAndBranchGrant()
    {
        await using var dbContext = CreateContext();
        var (organizationId, ownerUserId) = await RegisterOrgAsync(dbContext);
        var branchId = Guid.NewGuid();
        var service = new UserProvisioningService(dbContext, new PasswordHasherAdapter());
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var userId = await service.ProvisionStaffUserAsync(
            new ProvisionStaffUserRequest($"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, "DeliveryRider", branchId),
            organizationId, ownerUserId);

        var userBranch = dbContext.Set<Courier.Identity.Domain.UserBranch>().IgnoreQueryFilters().Single(ub => ub.UserId == userId);
        Assert.Equal(branchId, userBranch.BranchId);
        var userRole = dbContext.Set<Courier.Identity.Domain.UserRole>().IgnoreQueryFilters().Single(ur => ur.UserId == userId);
        Assert.Equal(Courier.Identity.Domain.BranchScope.SpecificBranches, userRole.BranchScope);
    }

    [Fact]
    public async Task ProvisionStaffUserAsync_UnknownRoleName_ThrowsUserProvisioningException()
    {
        await using var dbContext = CreateContext();
        var (organizationId, ownerUserId) = await RegisterOrgAsync(dbContext);
        var service = new UserProvisioningService(dbContext, new PasswordHasherAdapter());
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await Assert.ThrowsAsync<UserProvisioningException>(() => service.ProvisionStaffUserAsync(
            new ProvisionStaffUserRequest($"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, "NoSuchRole", Guid.NewGuid()),
            organizationId, ownerUserId));
    }

    [Fact]
    public async Task ProvisionStaffUserAsync_DuplicateEmail_ThrowsUserProvisioningException()
    {
        await using var dbContext = CreateContext();
        var (organizationId, ownerUserId) = await RegisterOrgAsync(dbContext);
        var service = new UserProvisioningService(dbContext, new PasswordHasherAdapter());
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"rider-{suffix}@example.com";
        await service.ProvisionStaffUserAsync(
            new ProvisionStaffUserRequest(email, "P@ssw0rd123!", "Test Rider", null, "DeliveryRider", Guid.NewGuid()),
            organizationId, ownerUserId);

        await Assert.ThrowsAsync<UserProvisioningException>(() => service.ProvisionStaffUserAsync(
            new ProvisionStaffUserRequest(email, "P@ssw0rd123!", "Another Rider", null, "PickupRider", Guid.NewGuid()),
            organizationId, ownerUserId));
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj`
Expected: All four new tests pass (requires the shared `courier-postgres-1` container running on port 5434 — do not `docker compose up` a second one).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Identity/Application/AuthService.cs src/Modules/Identity/Application/IUserProvisioningService.cs src/Modules/Identity/Application/UserProvisioningService.cs src/Api/Program.cs tests/Identity.Tests/UserProvisioningServiceTests.cs
git commit -m "feat(identity): seed PickupRider/DeliveryRider roles and add staff user provisioning"
```

---

## Task 4: Riders module scaffold — project file and Domain layer

**Files:**
- Create: `src/Modules/Riders/Riders.csproj`
- Create: `src/Modules/Riders/Domain/RiderRoleType.cs`
- Create: `src/Modules/Riders/Domain/Rider.cs`
- Create: `src/Modules/Riders/Domain/Vehicle.cs`
- Create: `src/Modules/Riders/Domain/DeliveryRunStatus.cs`
- Create: `src/Modules/Riders/Domain/DeliveryRun.cs`
- Create: `src/Modules/Riders/Domain/DeliveryTaskType.cs`
- Create: `src/Modules/Riders/Domain/DeliveryTaskStatus.cs`
- Create: `src/Modules/Riders/Domain/DeliveryTask.cs`
- Create: `src/Modules/Riders/Domain/DeliveryAttemptReason.cs`
- Create: `src/Modules/Riders/Domain/DeliveryAttempt.cs`
- Create: `src/Modules/Riders/Domain/DeliveryProof.cs`

**Interfaces:**
- Produces: every Domain type below, consumed by Task 5 (Infrastructure), Task 6 (`AppDbContext`), and Task 7–10 (Application).

- [ ] **Step 1: Create the project file**

`src/Modules/Riders/Riders.csproj` — mirrors `Manifests.csproj`'s shape, referencing `Branches` (validate `BaseBranchId` via `IBranchLookupService`), `Identity` (`IUserProvisioningService` — same precedent as `Shipments` already referencing `Identity`), and `Shipments` (`IShipmentTransitionService`):

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
    <ProjectReference Include="..\Identity\Identity.csproj" />
    <ProjectReference Include="..\Shipments\Shipments.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Rider and its role-type enum**

`src/Modules/Riders/Domain/RiderRoleType.cs`:

```csharp
namespace Courier.Riders.Domain;

// Maps 1:1 onto the Identity.Role names seeded by AuthService.RegisterOrganizationAsync
// ("PickupRider"/"DeliveryRider", Task 3) — RiderService.CreateRiderAsync converts this enum to
// the matching role name string when calling IUserProvisioningService.
public enum RiderRoleType
{
    PickupRider,
    DeliveryRider
}
```

`src/Modules/Riders/Domain/Rider.cs`:

```csharp
namespace Courier.Riders.Domain;

// UserId links to a real Identity.User holding the PickupRider/DeliveryRider role (DOMAIN-MODEL.md:
// "a rider is also a User with the PickupRider/DeliveryRider role") — created together with this
// row by RiderService.CreateRiderAsync via Identity.Application.IUserProvisioningService, never
// referencing Identity's DbSet directly (Riders has no project reference to Identity's
// Infrastructure/Persistence, only its Application-layer interface).
// No RiderAssignment entity this phase (this plan's resolved design decision) — BaseBranchId is
// the sole branch scope; a real re-assignment feature is deferred until a concrete need appears.
public class Rider
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public Guid BaseBranchId { get; set; }
    public required string LicenseNumber { get; set; }
    public RiderRoleType RoleType { get; set; }
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 3: Vehicle**

`src/Modules/Riders/Domain/Vehicle.cs`:

```csharp
namespace Courier.Riders.Domain;

// No dedicated vehicle.* permission (this plan's resolved design decision) — Vehicle is fleet
// metadata owned by the same rider.create/rider.read permissions Rider itself uses; a full fleet
// module with its own permission surface is deferred until a concrete need appears.
public class Vehicle
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? RiderId { get; set; }
    public required string Type { get; set; }
    public required string PlateNumber { get; set; }
    public decimal? Capacity { get; set; }
}
```

- [ ] **Step 4: DeliveryRun and its status enum**

`src/Modules/Riders/Domain/DeliveryRunStatus.cs`:

```csharp
namespace Courier.Riders.Domain;

public enum DeliveryRunStatus
{
    Active,
    Completed
}
```

`src/Modules/Riders/Domain/DeliveryRun.cs`:

```csharp
namespace Courier.Riders.Domain;

// Auto-created/reused (this plan's resolved design decision, no explicit "start my run" endpoint):
// RiderService.Tasks.cs finds-or-creates today's Active run for (RiderId, BranchId) the first time
// a task is assigned to that rider for that branch that day. No own OrganizationId — tenant
// isolation flows transitively through RiderId -> Rider.OrganizationId, same pattern as
// ManifestShipment -> Manifest.OrganizationId (Phase 7).
public class DeliveryRun
{
    public Guid Id { get; set; }
    public Guid RiderId { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly Date { get; set; }
    public DeliveryRunStatus Status { get; set; }
}
```

- [ ] **Step 5: DeliveryTask and its type/status enums**

`src/Modules/Riders/Domain/DeliveryTaskType.cs`:

```csharp
namespace Courier.Riders.Domain;

public enum DeliveryTaskType
{
    Pickup,
    Delivery
}
```

`src/Modules/Riders/Domain/DeliveryTaskStatus.cs`:

```csharp
namespace Courier.Riders.Domain;

public enum DeliveryTaskStatus
{
    Pending,
    Completed,
    Failed
}
```

`src/Modules/Riders/Domain/DeliveryTask.cs`:

```csharp
namespace Courier.Riders.Domain;

// A Pickup task completes when RiderService.RiderPickupScanAsync succeeds (Task 8). A Delivery
// task completes/fails when the eventual DeliveryAttempt resolves (Task 9) — assigning a delivery
// task already performs the AtDestinationBranch -> OutForDelivery scan in the same call (see
// ShipmentService.Transitions.cs's design note, Task 2), so "assigned" and "in progress" are the
// same state for a delivery task; there's no separate scan step left to move it out of Pending.
// No own OrganizationId — flows transitively through DeliveryRunId -> DeliveryRun -> Rider.OrganizationId.
public class DeliveryTask
{
    public Guid Id { get; set; }
    public Guid DeliveryRunId { get; set; }
    public Guid ShipmentId { get; set; }
    public DeliveryTaskType Type { get; set; }
    public int Sequence { get; set; }
    public DeliveryTaskStatus Status { get; set; }
}
```

- [ ] **Step 6: DeliveryAttempt and its reason enum**

`src/Modules/Riders/Domain/DeliveryAttemptReason.cs`:

```csharp
namespace Courier.Riders.Domain;

// Exact list from CLAUDE.md §19.
public enum DeliveryAttemptReason
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

`src/Modules/Riders/Domain/DeliveryAttempt.cs`:

```csharp
namespace Courier.Riders.Domain;

// One row per real-world attempt, success or failure (DOMAIN-MODEL.md: "Shipment 1─* DeliveryAttempt
// 1─(0..1)─ DeliveryProof" — a DeliveryProof exists iff Succeeded). Succeeded is an additive field
// beyond DOMAIN-MODEL.md's original field sketch (which only lists Reason) — needed to represent
// both outcomes in one table without a nullable-Reason-means-success convention that would be easy
// to get backwards; Reason is null iff Succeeded is true. NextAction is a plain string, not a
// controlled enum (this plan's resolved design decision) — Phase 9's NDR workflow is what actually
// automates "what happens next", this phase just records what the rider wrote down.
// Own OrganizationId (unlike DeliveryRun/DeliveryTask): this is a durable, independently-queryable
// audit/financial-adjacent record CLAUDE.md §19/§28 treats seriously enough to list on its own,
// and a future rider-performance report (Phase 13) plausibly queries it without going through a
// specific Shipment first.
public class DeliveryAttempt
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid RiderId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public bool Succeeded { get; set; }
    public DeliveryAttemptReason? Reason { get; set; }
    public string? Notes { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool CustomerContacted { get; set; }
    public string? NextAction { get; set; }
}
```

- [ ] **Step 7: DeliveryProof**

`src/Modules/Riders/Domain/DeliveryProof.cs`:

```csharp
namespace Courier.Riders.Domain;

// Immutable after creation except through CorrectedAt/CorrectionReason (CLAUDE.md §18) — no
// endpoint in this phase writes those two fields (a real correction workflow is deferred, same as
// this phase deliberately not building NDR/RTO); they exist now so a later phase adding correction
// needs no migration, mirroring how Shipment.CurrentCustodianRiderId was added in Phase 6 before
// Rider existed. OtpVerified is a client-reported boolean this phase (this plan's resolved design
// decision) — no server-side OTP generate/verify flow exists yet (that needs Phase 11's
// notification pipeline to actually deliver a code). Own OrganizationId for the same
// independently-queryable-audit-record reasoning as DeliveryAttempt above.
public class DeliveryProof
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid DeliveryAttemptId { get; set; }
    public Guid RiderId { get; set; }
    public bool OtpVerified { get; set; }
    public string? SignatureStorageKey { get; set; }
    public string? PhotoStorageKey { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public required string RecipientName { get; set; }
    public DateTimeOffset? CorrectedAt { get; set; }
    public string? CorrectionReason { get; set; }
}
```

- [ ] **Step 8: Build the (currently empty-of-logic) project to confirm the scaffold compiles**

Run: `dotnet build src/Modules/Riders/Riders.csproj`
Expected: Build succeeds.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Riders/Riders.csproj src/Modules/Riders/Domain/
git commit -m "feat(riders): scaffold Riders module and domain entities"
```

---

## Task 5: Riders Infrastructure — EF configurations

**Files:**
- Create: `src/Modules/Riders/Infrastructure/RiderConfiguration.cs`
- Create: `src/Modules/Riders/Infrastructure/VehicleConfiguration.cs`
- Create: `src/Modules/Riders/Infrastructure/DeliveryRunConfiguration.cs`
- Create: `src/Modules/Riders/Infrastructure/DeliveryTaskConfiguration.cs`
- Create: `src/Modules/Riders/Infrastructure/DeliveryAttemptConfiguration.cs`
- Create: `src/Modules/Riders/Infrastructure/DeliveryProofConfiguration.cs`

**Interfaces:**
- Produces: six `IEntityTypeConfiguration<T>` classes Task 6's `AppDbContext.OnModelCreating` applies.

- [ ] **Step 1: Rider**

`src/Modules/Riders/Infrastructure/RiderConfiguration.cs`:

```csharp
using Courier.Branches.Domain;
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Riders.Infrastructure;

public class RiderConfiguration : IEntityTypeConfiguration<Rider>
{
    public void Configure(EntityTypeBuilder<Rider> builder)
    {
        builder.ToTable("Riders");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.LicenseNumber).HasMaxLength(50).IsRequired();

        builder.HasIndex(r => r.OrganizationId);
        // One Rider per User — a User can't simultaneously be two different riders.
        builder.HasIndex(r => r.UserId).IsUnique();

        builder.HasOne<Branch>().WithMany().HasForeignKey(r => r.BaseBranchId).OnDelete(DeleteBehavior.Restrict);

        // No FK to Organization/User here: Organization FK wiring is centralized in
        // Persistence/AppDbContext.OnModelCreating (Task 6), same as every other module. UserId is
        // left unconstrained at the DB level for the same reason Shipment.CurrentCustodianUserId is
        // (Phase 6) — nothing in this phase queries through it as a join, only stores it.
    }
}
```

- [ ] **Step 2: Vehicle**

`src/Modules/Riders/Infrastructure/VehicleConfiguration.cs`:

```csharp
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Riders.Infrastructure;

public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicles");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Type).HasMaxLength(50).IsRequired();
        builder.Property(v => v.PlateNumber).HasMaxLength(30).IsRequired();
        builder.Property(v => v.Capacity).HasPrecision(10, 2);

        builder.HasIndex(v => v.OrganizationId);
        builder.HasIndex(v => v.RiderId);

        builder.HasOne<Rider>().WithMany().HasForeignKey(v => v.RiderId).OnDelete(DeleteBehavior.SetNull);
    }
}
```

- [ ] **Step 3: DeliveryRun**

`src/Modules/Riders/Infrastructure/DeliveryRunConfiguration.cs`:

```csharp
using Courier.Branches.Domain;
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Riders.Infrastructure;

public class DeliveryRunConfiguration : IEntityTypeConfiguration<DeliveryRun>
{
    public void Configure(EntityTypeBuilder<DeliveryRun> builder)
    {
        builder.ToTable("DeliveryRuns");
        builder.HasKey(r => r.Id);

        // The exact index RiderService.Tasks.cs's find-or-create query (Task 8) relies on: "does an
        // Active run already exist for (RiderId, BranchId, Date)".
        builder.HasIndex(r => new { r.RiderId, r.BranchId, r.Date });

        builder.HasOne<Rider>().WithMany().HasForeignKey(r => r.RiderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Branch>().WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);

        // No OrganizationId of its own — tenant isolation flows transitively through
        // RiderId -> Rider.OrganizationId (Task 6's query filter on Rider already covers it).
    }
}
```

- [ ] **Step 4: DeliveryTask**

`src/Modules/Riders/Infrastructure/DeliveryTaskConfiguration.cs`:

```csharp
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Riders.Infrastructure;

public class DeliveryTaskConfiguration : IEntityTypeConfiguration<DeliveryTask>
{
    public void Configure(EntityTypeBuilder<DeliveryTask> builder)
    {
        builder.ToTable("DeliveryTasks");
        builder.HasKey(t => t.Id);

        builder.HasIndex(t => t.DeliveryRunId);
        // Not unique: a shipment can get a second DeliveryTask on a later run once Phase 9 adds a
        // reattempt loop — only "one row per (DeliveryRunId, ShipmentId, Type)" needs to hold in
        // practice, which the natural assignment path already guarantees this phase.
        builder.HasIndex(t => t.ShipmentId);

        builder.HasOne<DeliveryRun>().WithMany().HasForeignKey(t => t.DeliveryRunId).OnDelete(DeleteBehavior.Cascade);

        // No FK to Shipment: even though Riders has a project reference to Shipments (for
        // IShipmentTransitionService), the same precedent as ManifestShipment.ShipmentId (Phase 7)
        // and Shipment.CurrentCustodianUserId (Phase 6) applies — cross-module id fields are left
        // unconstrained at the DB level in this codebase rather than every referencing module
        // adding a real FK into another module's table. No own OrganizationId — flows transitively
        // through DeliveryRunId -> DeliveryRun -> RiderId -> Rider.OrganizationId.
    }
}
```

- [ ] **Step 5: DeliveryAttempt**

`src/Modules/Riders/Infrastructure/DeliveryAttemptConfiguration.cs`:

```csharp
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Riders.Infrastructure;

public class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder)
    {
        builder.ToTable("DeliveryAttempts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Notes).HasMaxLength(1000);
        builder.Property(a => a.NextAction).HasMaxLength(500);
        builder.Property(a => a.Latitude).HasPrecision(9, 6);
        builder.Property(a => a.Longitude).HasPrecision(9, 6);

        builder.HasIndex(a => a.OrganizationId);
        builder.HasIndex(a => a.ShipmentId);
        builder.HasIndex(a => a.RiderId);

        builder.HasOne<Rider>().WithMany().HasForeignKey(a => a.RiderId).OnDelete(DeleteBehavior.Restrict);

        // No FK to Shipment — same cross-module reasoning as DeliveryTaskConfiguration above.
    }
}
```

- [ ] **Step 6: DeliveryProof**

`src/Modules/Riders/Infrastructure/DeliveryProofConfiguration.cs`:

```csharp
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Riders.Infrastructure;

public class DeliveryProofConfiguration : IEntityTypeConfiguration<DeliveryProof>
{
    public void Configure(EntityTypeBuilder<DeliveryProof> builder)
    {
        builder.ToTable("DeliveryProofs");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.RecipientName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.SignatureStorageKey).HasMaxLength(500);
        builder.Property(p => p.PhotoStorageKey).HasMaxLength(500);
        builder.Property(p => p.CorrectionReason).HasMaxLength(500);
        builder.Property(p => p.Latitude).HasPrecision(9, 6);
        builder.Property(p => p.Longitude).HasPrecision(9, 6);

        builder.HasIndex(p => p.OrganizationId);
        builder.HasIndex(p => p.ShipmentId);
        // One proof per attempt.
        builder.HasIndex(p => p.DeliveryAttemptId).IsUnique();

        builder.HasOne<Rider>().WithMany().HasForeignKey(p => p.RiderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryAttempt>().WithMany().HasForeignKey(p => p.DeliveryAttemptId).OnDelete(DeleteBehavior.Cascade);

        // No FK to Shipment — same cross-module reasoning as DeliveryTaskConfiguration above.
    }
}
```

- [ ] **Step 7: Build**

Run: `dotnet build src/Modules/Riders/Riders.csproj`
Expected: Build succeeds.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Riders/Infrastructure/
git commit -m "feat(riders): add EF Core configurations for Riders module entities"
```

---

## Task 6: Persistence wiring — `AppDbContext`, `Api.csproj`, EF migration

**Files:**
- Modify: `src/Persistence/AppDbContext.cs`
- Modify: `src/Api/Api.csproj`
- Create (generated): `src/Persistence/Migrations/*_AddRiders.cs` and `.Designer.cs`, updated `AppDbContextModelSnapshot.cs`

**Interfaces:**
- Produces: `AppDbContext.Riders/Vehicles/DeliveryRuns/DeliveryTasks/DeliveryAttempts/DeliveryProofs` `DbSet<T>` properties Task 7–10's `RiderService` methods use via `_dbContext.Set<T>()`.

- [ ] **Step 1: Add usings and `DbSet`s**

In `src/Persistence/AppDbContext.cs`, add after the `Manifests`/`Shipments` usings:

```csharp
using Courier.Riders.Domain;
using Courier.Riders.Infrastructure;
```

Add after the existing `DbSet<ShipmentTransfer>` line:

```csharp
    public DbSet<Rider> Riders => Set<Rider>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<DeliveryRun> DeliveryRuns => Set<DeliveryRun>();
    public DbSet<DeliveryTask> DeliveryTasks => Set<DeliveryTask>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<DeliveryProof> DeliveryProofs => Set<DeliveryProof>();
```

- [ ] **Step 2: Apply the six configurations**

Add after the existing `modelBuilder.ApplyConfiguration(new ShipmentTransferConfiguration());` line:

```csharp
        modelBuilder.ApplyConfiguration(new RiderConfiguration());
        modelBuilder.ApplyConfiguration(new VehicleConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryRunConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryTaskConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryProofConfiguration());
```

- [ ] **Step 3: Centralized Organization FK wiring for the four entities that carry their own `OrganizationId`**

Add after the existing `modelBuilder.Entity<Manifest>().HasOne<Organization>()...` block:

```csharp
        // Same reasoning for Rider/Vehicle/DeliveryAttempt/DeliveryProof.OrganizationId: Riders has
        // no project reference to Identity's Infrastructure/Persistence layer (only Identity's
        // Application-layer IUserProvisioningService interface).
        modelBuilder.Entity<Rider>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Vehicle>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(v => v.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DeliveryAttempt>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DeliveryProof>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(p => p.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 4: Tenant query filters**

Add after the existing `modelBuilder.Entity<Manifest>().HasQueryFilter(...)` line:

```csharp
        modelBuilder.Entity<Rider>().HasQueryFilter(r => _currentTenantProvider.OrganizationId != null && r.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Vehicle>().HasQueryFilter(v => _currentTenantProvider.OrganizationId != null && v.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<DeliveryAttempt>().HasQueryFilter(a => _currentTenantProvider.OrganizationId != null && a.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<DeliveryProof>().HasQueryFilter(p => _currentTenantProvider.OrganizationId != null && p.OrganizationId == _currentTenantProvider.OrganizationId);
```

Add this comment at the bottom of the method, alongside the existing "No tenant filter on..." comment block:

```csharp
        // No tenant filter on DeliveryRun/DeliveryTask: neither carries its own OrganizationId —
        // tenant isolation flows transitively through RiderId -> Rider.OrganizationId (which does
        // have a filter, above). Every query against these two tables in Riders.Application scopes
        // through a Rider the caller's tenant already owns.
```

- [ ] **Step 5: Add the project reference to `Api.csproj`**

In `src/Api/Api.csproj`, add after the existing `<ProjectReference Include="..\Modules\Manifests\Manifests.csproj" />` line:

```xml
    <ProjectReference Include="..\Modules\Riders\Riders.csproj" />
```

- [ ] **Step 6: Build to confirm the model wires up**

Run: `dotnet build src/Api/Api.csproj`
Expected: Build succeeds.

- [ ] **Step 7: Generate the migration**

Run: `dotnet ef migrations add AddRiders --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: A new migration file is created under `src/Persistence/Migrations/` creating `Riders`, `Vehicles`, `DeliveryRuns`, `DeliveryTasks`, `DeliveryAttempts`, `DeliveryProofs` tables with the FKs/indexes from Task 5, and `AppDbContextModelSnapshot.cs` is updated. Confirm by reading the generated migration file that no unrelated tables changed.

- [ ] **Step 8: Apply the migration against the shared local Postgres**

Run: `docker exec courier-postgres-1 pg_isready -U courier` to confirm the shared container (port 5434) is up first — do not `docker compose up` a second one. Then either let the API's Development-mode auto-migrate handle it on next run, or apply directly:
`dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: Migration applies cleanly with no errors.

- [ ] **Step 9: Commit**

```bash
git add src/Persistence/AppDbContext.cs src/Api/Api.csproj src/Persistence/Migrations/
git commit -m "feat(riders): wire Riders module into AppDbContext with migration"
```

---

## Task 7: Riders Application — DTOs, exceptions, `RiderService` core (`CreateRiderAsync`/`CreateVehicleAsync`)

**Files:**
- Create: `src/Modules/Riders/Application/RiderDtos.cs`
- Create: `src/Modules/Riders/Application/RiderService.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Riders.Tests/RiderServiceCreationTests.cs`
- Create: `tests/Riders.Tests/Riders.Tests.csproj`

**Interfaces:**
- Consumes: `IUserProvisioningService.ProvisionStaffUserAsync` (Task 3), `IBranchLookupService.GetZoneInfoAsync` (existing), `IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync` (existing).
- Produces: `RiderService.CreateRiderAsync(CreateRiderRequest, ClaimsPrincipal, CancellationToken) -> Task<RiderResponse>`, `RiderService.CreateVehicleAsync(CreateVehicleRequest, ClaimsPrincipal, CancellationToken) -> Task<VehicleResponse>` — Task 11's Presentation layer and Task 13's API tests call these. `RiderResponse`/`VehicleResponse`/`RiderValidationException`/`RiderNotFoundException` are consumed by every later Riders task.

- [ ] **Step 1: Create the test project**

`tests/Riders.Tests/Riders.Tests.csproj` — mirrors `tests/Manifests.Tests/Manifests.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\Modules\Riders\Riders.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: DTOs and exceptions**

`src/Modules/Riders/Application/RiderDtos.cs`:

```csharp
using Courier.Riders.Domain;

namespace Courier.Riders.Application;

public record CreateRiderRequest(string Email, string Password, string FullName, string? Phone, string LicenseNumber, Guid BaseBranchId, RiderRoleType RoleType);

public record RiderResponse(Guid Id, Guid UserId, Guid BaseBranchId, string LicenseNumber, RiderRoleType RoleType, bool IsActive);

public record CreateVehicleRequest(Guid? RiderId, string Type, string PlateNumber, decimal? Capacity);

public record VehicleResponse(Guid Id, Guid? RiderId, string Type, string PlateNumber, decimal? Capacity);

public record AssignDeliveryTaskRequest(Guid RiderId, Guid ShipmentId, DeliveryTaskType Type);

public record DeliveryTaskResponse(Guid Id, Guid DeliveryRunId, Guid ShipmentId, DeliveryTaskType Type, int Sequence, DeliveryTaskStatus Status);

public record RiderScanRequest(string Awb);

public record RecordFailedAttemptRequest(Guid ShipmentId, DeliveryAttemptReason Reason, string? Notes, decimal? Latitude, decimal? Longitude, bool CustomerContacted, string? NextAction);

public record UploadPodFileRequest(); // marker only — actual upload is a raw request body, see Task 11

public record UploadPodFileResponse(string StorageKey);

public enum PodFileKind { Signature, Photo }

public record CompleteDeliveryRequest(Guid ShipmentId, string RecipientName, bool OtpVerified, string? SignatureStorageKey, string? PhotoStorageKey, decimal? Latitude, decimal? Longitude);

public record DeliveryAttemptResponse(Guid Id, Guid ShipmentId, Guid RiderId, int AttemptNumber, bool Succeeded, DeliveryAttemptReason? Reason, string? NextAction);

public class RiderValidationException : Exception
{
    public RiderValidationException(string message) : base(message) { }
}

public class RiderNotFoundException : Exception
{
    public RiderNotFoundException(Guid riderId) : base($"Rider {riderId} was not found.")
    {
        RiderId = riderId;
    }

    public Guid RiderId { get; }
}
```

- [ ] **Step 3: `RiderService` core**

`src/Modules/Riders/Application/RiderService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IUserProvisioningService _userProvisioningService;
    private readonly IBranchLookupService _branchLookupService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IObjectStorage _objectStorage;

    public RiderService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IUserProvisioningService userProvisioningService,
        IBranchLookupService branchLookupService,
        IBranchAuthorizationChecker branchAuthorizationChecker,
        IShipmentTransitionService shipmentTransitionService,
        IObjectStorage objectStorage)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _userProvisioningService = userProvisioningService;
        _branchLookupService = branchLookupService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _shipmentTransitionService = shipmentTransitionService;
        _objectStorage = objectStorage;
    }

    public async Task<RiderResponse> CreateRiderAsync(CreateRiderRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RiderValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new RiderValidationException("Acting user could not be resolved.");

        if (await _branchLookupService.GetZoneInfoAsync(request.BaseBranchId, cancellationToken) is null)
        {
            throw new RiderValidationException("BaseBranchId does not refer to a branch in this organization.");
        }
        await CheckBranchAccessAsync(actingUser, "rider.create", request.BaseBranchId, cancellationToken);

        // Maps 1:1 onto the role names AuthService.RegisterOrganizationAsync seeds (Task 3).
        var roleName = request.RoleType == RiderRoleType.PickupRider ? "PickupRider" : "DeliveryRider";

        Guid userId;
        try
        {
            userId = await _userProvisioningService.ProvisionStaffUserAsync(
                new ProvisionStaffUserRequest(request.Email, request.Password, request.FullName, request.Phone, roleName, request.BaseBranchId),
                organizationId, actingUserId, cancellationToken);
        }
        catch (UserProvisioningException ex)
        {
            throw new RiderValidationException(ex.Message);
        }

        var rider = new Rider
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            BaseBranchId = request.BaseBranchId,
            LicenseNumber = request.LicenseNumber,
            RoleType = request.RoleType,
            IsActive = true
        };
        _dbContext.Set<Rider>().Add(rider);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "rider.created", "Rider", rider.Id.ToString()), cancellationToken);
        return ToResponse(rider);
    }

    public async Task<VehicleResponse> CreateVehicleAsync(CreateVehicleRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RiderValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new RiderValidationException("Acting user could not be resolved.");

        if (request.RiderId is Guid riderId)
        {
            var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken)
                ?? throw new RiderValidationException("RiderId does not refer to a rider in this organization.");
            await CheckBranchAccessAsync(actingUser, "rider.create", rider.BaseBranchId, cancellationToken);
        }

        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            RiderId = request.RiderId,
            Type = request.Type,
            PlateNumber = request.PlateNumber,
            Capacity = request.Capacity
        };
        _dbContext.Set<Vehicle>().Add(vehicle);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "vehicle.created", "Vehicle", vehicle.Id.ToString()), cancellationToken);
        return ToVehicleResponse(vehicle);
    }

    private async Task CheckBranchAccessAsync(ClaimsPrincipal actingUser, string permission, Guid branchId, CancellationToken cancellationToken)
    {
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, permission, cancellationToken);
        if (accessible is not null && !accessible.Contains(branchId))
        {
            throw new RiderValidationException($"Branch is outside your branch access for permission '{permission}'.");
        }
    }

    private static RiderResponse ToResponse(Rider rider) => new(
        rider.Id, rider.UserId, rider.BaseBranchId, rider.LicenseNumber, rider.RoleType, rider.IsActive);

    private static VehicleResponse ToVehicleResponse(Vehicle vehicle) => new(
        vehicle.Id, vehicle.RiderId, vehicle.Type, vehicle.PlateNumber, vehicle.Capacity);
}
```

- [ ] **Step 4: Wire `RiderService` into `Program.cs`**

In `src/Api/Program.cs`, add `using Courier.Riders.Application;` to the usings, and after the existing `builder.Services.AddScoped<ManifestService>();` line:

```csharp
builder.Services.AddScoped<RiderService>();
```

- [ ] **Step 5: Write the failing unit test**

Create `tests/Riders.Tests/RiderServiceCreationTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Riders.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Riders.Tests;

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

file class FakeUserProvisioningService : IUserProvisioningService
{
    public List<ProvisionStaffUserRequest> Calls { get; } = new();
    public bool ThrowOnNextCall { get; set; }

    public Task<Guid> ProvisionStaffUserAsync(ProvisionStaffUserRequest request, Guid organizationId, Guid grantedByUserId, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextCall)
        {
            throw new UserProvisioningException("Email is already registered.");
        }
        Calls.Add(request);
        return Task.FromResult(Guid.NewGuid());
    }
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
}

file class NullObjectStorage : IObjectStorage
{
    public Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class RiderServiceCreationTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RiderConfiguration());
            modelBuilder.ApplyConfiguration(new VehicleConfiguration());
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

    private static (RiderService Service, FakeUserProvisioningService Provisioning) CreateService(TestDbContext dbContext, Guid organizationId, Guid knownBranchId)
    {
        var provisioning = new FakeUserProvisioningService();
        var service = new RiderService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), provisioning,
            new FakeBranchLookup(knownBranchId), new AllBranchesAuthorizationChecker(),
            new NotSupportedShipmentTransitionService(), new NullObjectStorage());
        return (service, provisioning);
    }

    [Fact]
    public async Task CreateRiderAsync_ValidRequest_ProvisionsUserAndCreatesRider()
    {
        await using var dbContext = CreateContext();
        var branchId = Guid.NewGuid();
        var (service, provisioning) = CreateService(dbContext, Guid.NewGuid(), branchId);

        var result = await service.CreateRiderAsync(
            new CreateRiderRequest("rider@example.com", "P@ssw0rd123!", "Test Rider", null, "LIC-001", branchId, RiderRoleType.DeliveryRider),
            ActingUser(Guid.NewGuid()));

        Assert.Equal(branchId, result.BaseBranchId);
        Assert.Equal(RiderRoleType.DeliveryRider, result.RoleType);
        Assert.True(result.IsActive);
        var call = Assert.Single(provisioning.Calls);
        Assert.Equal("DeliveryRider", call.RoleName);
        Assert.Equal(branchId, call.BranchId);
    }

    [Fact]
    public async Task CreateRiderAsync_UnknownBranch_ThrowsRiderValidationException()
    {
        await using var dbContext = CreateContext();
        var (service, _) = CreateService(dbContext, Guid.NewGuid(), Guid.NewGuid());

        await Assert.ThrowsAsync<RiderValidationException>(() => service.CreateRiderAsync(
            new CreateRiderRequest("rider@example.com", "P@ssw0rd123!", "Test Rider", null, "LIC-001", Guid.NewGuid(), RiderRoleType.PickupRider),
            ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateRiderAsync_DuplicateEmail_ThrowsRiderValidationException()
    {
        await using var dbContext = CreateContext();
        var branchId = Guid.NewGuid();
        var (service, provisioning) = CreateService(dbContext, Guid.NewGuid(), branchId);
        provisioning.ThrowOnNextCall = true;

        await Assert.ThrowsAsync<RiderValidationException>(() => service.CreateRiderAsync(
            new CreateRiderRequest("dup@example.com", "P@ssw0rd123!", "Test Rider", null, "LIC-001", branchId, RiderRoleType.PickupRider),
            ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateVehicleAsync_WithoutRider_Succeeds()
    {
        await using var dbContext = CreateContext();
        var (service, _) = CreateService(dbContext, Guid.NewGuid(), Guid.NewGuid());

        var result = await service.CreateVehicleAsync(
            new CreateVehicleRequest(null, "Motorcycle", "BA 2 KHA 1234", 50m), ActingUser(Guid.NewGuid()));

        Assert.Null(result.RiderId);
        Assert.Equal("BA 2 KHA 1234", result.PlateNumber);
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj`
Expected: All four tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Riders/Application/RiderDtos.cs src/Modules/Riders/Application/RiderService.cs src/Api/Program.cs tests/Riders.Tests/
git commit -m "feat(riders): add RiderService creation methods for riders and vehicles"
```

---

## Task 8: Riders Application — task assignment, auto-run reuse, pickup scan

**Files:**
- Create: `src/Modules/Riders/Application/RiderService.Tasks.cs`
- Test: `tests/Riders.Tests/RiderServiceTasksTests.cs`

**Interfaces:**
- Consumes: `IShipmentTransitionService.GetScanInfoAsync/GetScanInfoByAwbAsync/AssignPickupToRiderAsync/RiderPickupScanAsync/AssignDeliveryToRiderAsync` (Task 2).
- Produces: `RiderService.AssignTaskAsync(AssignDeliveryTaskRequest, ClaimsPrincipal, CancellationToken) -> Task<DeliveryTaskResponse>`, `RiderService.RiderPickupScanAsync(Guid riderId, RiderScanRequest, ClaimsPrincipal, CancellationToken) -> Task<ShipmentResponse>` — Task 11's Presentation and Task 13's tests call both.

- [ ] **Step 1: Write `RiderService.Tasks.cs`**

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
    public async Task<DeliveryTaskResponse> AssignTaskAsync(AssignDeliveryTaskRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RiderValidationException("No active organization context.");

        var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == request.RiderId, cancellationToken)
            ?? throw new RiderNotFoundException(request.RiderId);
        if (!rider.IsActive)
        {
            throw new RiderValidationException("Rider is not active.");
        }

        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(request.ShipmentId, cancellationToken)
            ?? throw new RiderValidationException("Shipment was not found.");

        // Pickup tasks are assigned/branch-scoped against the shipment's origin (the rider must be
        // able to operate at the branch it's leaving from); delivery tasks against the destination
        // (where the rider must physically be to take it out for delivery).
        var branchId = request.Type == DeliveryTaskType.Pickup ? scanInfo.OriginBranchId : scanInfo.DestinationBranchId;
        // Check 1 (permission) enforced at the route via .RequirePermission("delivery.assign");
        // check 2 (branch access) here.
        await CheckBranchAccessAsync(actingUser, "delivery.assign", branchId, cancellationToken);

        // Checks 3 (state) + 5 (custody) — check 4 (expected operation) collapses into check 3 here,
        // same reasoning as ManifestService.ScanAsync (Phase 7): the only thing this endpoint does
        // for each task type IS that exact transition.
        if (request.Type == DeliveryTaskType.Pickup)
        {
            if (scanInfo.CurrentStatus != ShipmentStatus.Created)
            {
                throw new RiderValidationException($"Shipment must be Created to assign a pickup task, was {scanInfo.CurrentStatus}.");
            }
        }
        else
        {
            if (scanInfo.CurrentStatus != ShipmentStatus.AtDestinationBranch)
            {
                throw new RiderValidationException($"Shipment must be AtDestinationBranch to assign a delivery task, was {scanInfo.CurrentStatus}.");
            }
            if (scanInfo.CurrentBranchId != branchId)
            {
                throw new RiderValidationException("Shipment's current branch does not match this rider's base branch.");
            }
        }

        // Perform the real transition FIRST — only persist DeliveryRun/DeliveryTask bookkeeping once
        // it succeeds, mirroring ManifestService.ScanAsync's "transition, then record" ordering so a
        // failed transition never leaves an orphan task row behind.
        if (request.Type == DeliveryTaskType.Pickup)
        {
            await _shipmentTransitionService.AssignPickupToRiderAsync(request.ShipmentId, rider.Id, actingUser, cancellationToken);
        }
        else
        {
            await _shipmentTransitionService.AssignDeliveryToRiderAsync(request.ShipmentId, rider.Id, actingUser, cancellationToken);
        }

        // Find-or-create today's Active run for (RiderId, BranchId) — this plan's resolved design
        // decision: no explicit "start my run" endpoint.
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var run = await _dbContext.Set<DeliveryRun>().FirstOrDefaultAsync(
            r => r.RiderId == rider.Id && r.BranchId == branchId && r.Date == today && r.Status == DeliveryRunStatus.Active, cancellationToken);
        if (run is null)
        {
            run = new DeliveryRun { Id = Guid.NewGuid(), RiderId = rider.Id, BranchId = branchId, Date = today, Status = DeliveryRunStatus.Active };
            _dbContext.Set<DeliveryRun>().Add(run);
        }

        var maxSequence = await _dbContext.Set<DeliveryTask>()
            .Where(t => t.DeliveryRunId == run.Id)
            .Select(t => (int?)t.Sequence)
            .MaxAsync(cancellationToken);

        var task = new DeliveryTask
        {
            Id = Guid.NewGuid(),
            DeliveryRunId = run.Id,
            ShipmentId = request.ShipmentId,
            Type = request.Type,
            Sequence = (maxSequence ?? 0) + 1,
            Status = DeliveryTaskStatus.Pending
        };
        _dbContext.Set<DeliveryTask>().Add(task);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "delivery_task.assigned", "DeliveryTask", task.Id.ToString()), cancellationToken);
        return ToTaskResponse(task);
    }

    public async Task<ShipmentResponse> RiderPickupScanAsync(Guid riderId, RiderScanRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        _ = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken)
            ?? throw new RiderNotFoundException(riderId);

        var scanInfo = await _shipmentTransitionService.GetScanInfoByAwbAsync(request.Awb, cancellationToken)
            ?? throw new RiderValidationException($"No shipment found for AWB '{request.Awb}'.");

        // Check 1 (permission) enforced at the route via .RequirePermission("shipment.scan"); check
        // 2 (branch access) here.
        await CheckBranchAccessAsync(actingUser, "shipment.scan", scanInfo.OriginBranchId, cancellationToken);

        // Checks 3+4 collapse: the only operation this endpoint performs IS the pickup scan.
        if (scanInfo.CurrentStatus != ShipmentStatus.PickupAssigned)
        {
            throw new RiderValidationException($"Shipment must be PickupAssigned to scan for pickup, was {scanInfo.CurrentStatus}.");
        }
        // Check 5 (custody consistency): must be assigned to THIS rider, not just any rider.
        if (scanInfo.CurrentCustodianRiderId != riderId)
        {
            throw new RiderValidationException("Shipment is not assigned to this rider.");
        }

        var result = await _shipmentTransitionService.RiderPickupScanAsync(scanInfo.ShipmentId, riderId, actingUser, cancellationToken);

        var task = await _dbContext.Set<DeliveryTask>()
            .FirstOrDefaultAsync(t => t.ShipmentId == scanInfo.ShipmentId && t.Type == DeliveryTaskType.Pickup && t.Status == DeliveryTaskStatus.Pending, cancellationToken);
        if (task is not null)
        {
            task.Status = DeliveryTaskStatus.Completed;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _auditLogger.LogAsync(new AuditEntry(scanInfo.OrganizationId, actingUser.GetUserId(), "rider.pickup_scanned", "Shipment", scanInfo.ShipmentId.ToString()), cancellationToken);
        return result!;
    }

    private static DeliveryTaskResponse ToTaskResponse(DeliveryTask task) => new(
        task.Id, task.DeliveryRunId, task.ShipmentId, task.Type, task.Sequence, task.Status);
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Riders.Tests/RiderServiceTasksTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Riders.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Riders.Tests;

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

// In-memory stand-in for Shipments' real ShipmentService — mirrors Manifests.Tests'
// ScanningFakeShipmentTransitionService (Phase 7).
file class FakeShipmentTransitionService : IShipmentTransitionService
{
    public Dictionary<Guid, ShipmentScanInfo> ShipmentsById { get; } = new();
    public List<(Guid ShipmentId, Guid RiderId)> AssignPickupCalls { get; } = new();
    public List<(Guid ShipmentId, Guid RiderId)> PickupScanCalls { get; } = new();
    public List<(Guid ShipmentId, Guid RiderId)> AssignDeliveryCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.Values.FirstOrDefault(s => s.Awb == awb));
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        AssignPickupCalls.Add((shipmentId, riderId));
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.PickupAssigned, CurrentCustodianRiderId = riderId };
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        PickupScanCalls.Add((shipmentId, riderId));
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.PickedUp };
        return Task.FromResult<ShipmentResponse?>(new ShipmentResponse(
            shipmentId, info.Awb, Guid.NewGuid(), info.OriginBranchId, info.DestinationBranchId, null, null, riderId,
            ShipmentStatus.PickedUp, Guid.NewGuid(), 1m, null, null, null, 100m, 0m, false, 100m, 0m, 0m, 100m,
            "Recipient", "9800000000", "Address", "Sender", "9800000001", "Address", new List<ShipmentItemResponse>()));
    }

    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        AssignDeliveryCalls.Add((shipmentId, riderId));
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.OutForDelivery, CurrentCustodianRiderId = riderId, CurrentBranchId = null };
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NotSupportedUserProvisioningService : IUserProvisioningService
{
    public Task<Guid> ProvisionStaffUserAsync(ProvisionStaffUserRequest request, Guid organizationId, Guid grantedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NotSupportedBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NotSupportedObjectStorage : IObjectStorage
{
    public Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class RiderServiceTasksTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RiderConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryRunConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryTaskConfiguration());
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

    private static async Task<(RiderService Service, FakeShipmentTransitionService Shipments, Guid RiderId)> SeedRiderAsync(TestDbContext dbContext, Guid organizationId, Guid branchId)
    {
        var rider = new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = Guid.NewGuid(), BaseBranchId = branchId, LicenseNumber = "LIC-001", RoleType = RiderRoleType.PickupRider, IsActive = true };
        dbContext.Set<Rider>().Add(rider);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        var service = new RiderService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), new AllBranchesAuthorizationChecker(), shipments, new NotSupportedObjectStorage());
        return (service, shipments, rider.Id);
    }

    private static ShipmentScanInfo CreatedInfo(Guid originBranchId, Guid destinationBranchId, string awb = "AWB001") => new(
        Guid.NewGuid(), Guid.NewGuid(), awb, ShipmentStatus.Created, null, originBranchId, destinationBranchId, null);

    [Fact]
    public async Task AssignTaskAsync_TwoPickupTasksSameRiderSameDay_ReuseSameRunWithIncrementingSequence()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var (service, shipments, riderId) = await SeedRiderAsync(dbContext, organizationId, branchId);
        var info1 = CreatedInfo(branchId, Guid.NewGuid(), "AWB001");
        var info2 = CreatedInfo(branchId, Guid.NewGuid(), "AWB002");
        shipments.ShipmentsById[info1.ShipmentId] = info1;
        shipments.ShipmentsById[info2.ShipmentId] = info2;

        var task1 = await service.AssignTaskAsync(new AssignDeliveryTaskRequest(riderId, info1.ShipmentId, DeliveryTaskType.Pickup), ActingUser(Guid.NewGuid()));
        var task2 = await service.AssignTaskAsync(new AssignDeliveryTaskRequest(riderId, info2.ShipmentId, DeliveryTaskType.Pickup), ActingUser(Guid.NewGuid()));

        Assert.Equal(task1.DeliveryRunId, task2.DeliveryRunId);
        Assert.Equal(1, task1.Sequence);
        Assert.Equal(2, task2.Sequence);
        var runs = await dbContext.Set<DeliveryRun>().ToListAsync();
        Assert.Single(runs);
        Assert.Equal(2, shipments.AssignPickupCalls.Count);
    }

    [Fact]
    public async Task AssignTaskAsync_ShipmentNotCreated_ThrowsRiderValidationException()
    {
        await using var dbContext = CreateContext();
        var branchId = Guid.NewGuid();
        var (service, shipments, riderId) = await SeedRiderAsync(dbContext, Guid.NewGuid(), branchId);
        var info = CreatedInfo(branchId, Guid.NewGuid()) with { CurrentStatus = ShipmentStatus.PickedUp };
        shipments.ShipmentsById[info.ShipmentId] = info;

        await Assert.ThrowsAsync<RiderValidationException>(() => service.AssignTaskAsync(
            new AssignDeliveryTaskRequest(riderId, info.ShipmentId, DeliveryTaskType.Pickup), ActingUser(Guid.NewGuid())));
        Assert.Empty(shipments.AssignPickupCalls);
    }

    [Fact]
    public async Task RiderPickupScanAsync_AssignedToDifferentRider_ThrowsRiderValidationException()
    {
        await using var dbContext = CreateContext();
        var branchId = Guid.NewGuid();
        var (service, shipments, riderId) = await SeedRiderAsync(dbContext, Guid.NewGuid(), branchId);
        var info = CreatedInfo(branchId, Guid.NewGuid()) with { CurrentStatus = ShipmentStatus.PickupAssigned, CurrentCustodianRiderId = Guid.NewGuid() };
        shipments.ShipmentsById[info.ShipmentId] = info;

        var ex = await Assert.ThrowsAsync<RiderValidationException>(() => service.RiderPickupScanAsync(
            riderId, new RiderScanRequest(info.Awb), ActingUser(Guid.NewGuid())));
        Assert.Contains("not assigned to this rider", ex.Message);
    }

    [Fact]
    public async Task RiderPickupScanAsync_ValidScan_CompletesPickupTask()
    {
        await using var dbContext = CreateContext();
        var branchId = Guid.NewGuid();
        var (service, shipments, riderId) = await SeedRiderAsync(dbContext, Guid.NewGuid(), branchId);
        var info = CreatedInfo(branchId, Guid.NewGuid());
        shipments.ShipmentsById[info.ShipmentId] = info;
        await service.AssignTaskAsync(new AssignDeliveryTaskRequest(riderId, info.ShipmentId, DeliveryTaskType.Pickup), ActingUser(Guid.NewGuid()));

        await service.RiderPickupScanAsync(riderId, new RiderScanRequest(info.Awb), ActingUser(Guid.NewGuid()));

        var task = await dbContext.Set<DeliveryTask>().SingleAsync(t => t.ShipmentId == info.ShipmentId);
        Assert.Equal(DeliveryTaskStatus.Completed, task.Status);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj`
Expected: All tests pass (the earlier `RiderServiceCreationTests.cs` tests still pass too).

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Riders/Application/RiderService.Tasks.cs tests/Riders.Tests/RiderServiceTasksTests.cs
git commit -m "feat(riders): add delivery task assignment with auto-reused runs and pickup scan"
```

---

## Task 9: Riders Application — failed attempts, POD file upload, delivery completion

**Files:**
- Create: `src/Modules/Riders/Application/RiderService.Attempts.cs`
- Test: `tests/Riders.Tests/RiderServiceAttemptsTests.cs`

**Interfaces:**
- Consumes: `IShipmentTransitionService.FailDeliveryAsync/CompleteDeliveryAsync` (Task 2), `IObjectStorage.UploadAsync` (existing, `SharedKernel`).
- Produces: `RiderService.RecordFailedAttemptAsync(Guid riderId, RecordFailedAttemptRequest, ClaimsPrincipal, CancellationToken) -> Task<DeliveryAttemptResponse>`, `RiderService.UploadPodFileAsync(Guid riderId, PodFileKind, Stream, string contentType, ClaimsPrincipal, CancellationToken) -> Task<UploadPodFileResponse>`, `RiderService.CompleteDeliveryAsync(Guid riderId, CompleteDeliveryRequest, ClaimsPrincipal, CancellationToken) -> Task<DeliveryAttemptResponse>` — all consumed by Task 11's Presentation and Task 13's tests.

- [ ] **Step 1: Write `RiderService.Attempts.cs`**

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
    public async Task<DeliveryAttemptResponse> RecordFailedAttemptAsync(Guid riderId, RecordFailedAttemptRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RiderValidationException("No active organization context.");
        _ = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken)
            ?? throw new RiderNotFoundException(riderId);

        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(request.ShipmentId, cancellationToken)
            ?? throw new RiderValidationException("Shipment was not found.");

        // Check 1 (permission) enforced at the route via .RequirePermission("delivery.attempt");
        // check 2 (branch access) here.
        await CheckBranchAccessAsync(actingUser, "delivery.attempt", scanInfo.DestinationBranchId, cancellationToken);

        if (scanInfo.CurrentStatus != ShipmentStatus.OutForDelivery)
        {
            throw new RiderValidationException($"Shipment must be OutForDelivery to record a delivery attempt, was {scanInfo.CurrentStatus}.");
        }
        if (scanInfo.CurrentCustodianRiderId != riderId)
        {
            throw new RiderValidationException("Shipment is not assigned to this rider.");
        }

        var attemptNumber = await _dbContext.Set<DeliveryAttempt>().CountAsync(a => a.ShipmentId == request.ShipmentId, cancellationToken) + 1;

        // Transition first, then persist the attempt record — same "transition, then record"
        // ordering as AssignTaskAsync (Task 8).
        await _shipmentTransitionService.FailDeliveryAsync(request.ShipmentId, riderId, actingUser, cancellationToken);

        var attempt = new DeliveryAttempt
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ShipmentId = request.ShipmentId,
            RiderId = riderId,
            AttemptNumber = attemptNumber,
            AttemptedAt = DateTimeOffset.UtcNow,
            Succeeded = false,
            Reason = request.Reason,
            Notes = request.Notes,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            CustomerContacted = request.CustomerContacted,
            NextAction = request.NextAction
        };
        _dbContext.Set<DeliveryAttempt>().Add(attempt);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var task = await _dbContext.Set<DeliveryTask>()
            .FirstOrDefaultAsync(t => t.ShipmentId == request.ShipmentId && t.Type == DeliveryTaskType.Delivery && t.Status == DeliveryTaskStatus.Pending, cancellationToken);
        if (task is not null)
        {
            task.Status = DeliveryTaskStatus.Failed;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "delivery.attempt_failed", "DeliveryAttempt", attempt.Id.ToString()), cancellationToken);
        return ToAttemptResponse(attempt);
    }

    public async Task<UploadPodFileResponse> UploadPodFileAsync(Guid riderId, PodFileKind kind, Stream content, string contentType, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        _ = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken)
            ?? throw new RiderNotFoundException(riderId);

        var storageKey = await _objectStorage.UploadAsync($"pod/{kind}/{riderId}", content, contentType, cancellationToken);
        return new UploadPodFileResponse(storageKey);
    }

    public async Task<DeliveryAttemptResponse> CompleteDeliveryAsync(Guid riderId, CompleteDeliveryRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RiderValidationException("No active organization context.");
        _ = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken)
            ?? throw new RiderNotFoundException(riderId);

        var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(request.ShipmentId, cancellationToken)
            ?? throw new RiderValidationException("Shipment was not found.");

        // Check 1 (permission) enforced at the route via .RequirePermission("delivery.complete");
        // check 2 (branch access) here.
        await CheckBranchAccessAsync(actingUser, "delivery.complete", scanInfo.DestinationBranchId, cancellationToken);

        if (scanInfo.CurrentStatus != ShipmentStatus.OutForDelivery)
        {
            throw new RiderValidationException($"Shipment must be OutForDelivery to complete delivery, was {scanInfo.CurrentStatus}.");
        }
        if (scanInfo.CurrentCustodianRiderId != riderId)
        {
            throw new RiderValidationException("Shipment is not assigned to this rider.");
        }

        var attemptNumber = await _dbContext.Set<DeliveryAttempt>().CountAsync(a => a.ShipmentId == request.ShipmentId, cancellationToken) + 1;

        await _shipmentTransitionService.CompleteDeliveryAsync(request.ShipmentId, riderId, request.RecipientName, actingUser, cancellationToken);

        var attempt = new DeliveryAttempt
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ShipmentId = request.ShipmentId,
            RiderId = riderId,
            AttemptNumber = attemptNumber,
            AttemptedAt = DateTimeOffset.UtcNow,
            Succeeded = true,
            Reason = null,
            CustomerContacted = true
        };
        _dbContext.Set<DeliveryAttempt>().Add(attempt);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var proof = new DeliveryProof
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ShipmentId = request.ShipmentId,
            DeliveryAttemptId = attempt.Id,
            RiderId = riderId,
            OtpVerified = request.OtpVerified,
            SignatureStorageKey = request.SignatureStorageKey,
            PhotoStorageKey = request.PhotoStorageKey,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Timestamp = DateTimeOffset.UtcNow,
            RecipientName = request.RecipientName
        };
        _dbContext.Set<DeliveryProof>().Add(proof);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var task = await _dbContext.Set<DeliveryTask>()
            .FirstOrDefaultAsync(t => t.ShipmentId == request.ShipmentId && t.Type == DeliveryTaskType.Delivery && t.Status == DeliveryTaskStatus.Pending, cancellationToken);
        if (task is not null)
        {
            task.Status = DeliveryTaskStatus.Completed;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "delivery.completed", "DeliveryProof", proof.Id.ToString()), cancellationToken);
        return ToAttemptResponse(attempt);
    }

    private static DeliveryAttemptResponse ToAttemptResponse(DeliveryAttempt attempt) => new(
        attempt.Id, attempt.ShipmentId, attempt.RiderId, attempt.AttemptNumber, attempt.Succeeded, attempt.Reason, attempt.NextAction);
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Riders.Tests/RiderServiceAttemptsTests.cs` — reuses the same file-scoped fakes pattern as `RiderServiceTasksTests.cs` (each test file defines its own, per this project's established convention):

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Riders.Infrastructure;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Riders.Tests;

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
    public List<Guid> FailCalls { get; } = new();
    public List<Guid> CompleteCalls { get; } = new();

    public Task<ShipmentScanInfo?> GetScanInfoAsync(Guid shipmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ShipmentsById.TryGetValue(shipmentId, out var info) ? info : null);
    public Task<ShipmentScanInfo?> GetScanInfoByAwbAsync(string awb, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DispatchManyAsync(IReadOnlyCollection<Guid> shipmentIds, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveAtDestinationAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignPickupToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> RiderPickupScanAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> AssignDeliveryToRiderAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ShipmentResponse?> CompleteDeliveryAsync(Guid shipmentId, Guid riderId, string recipientName, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        CompleteCalls.Add(shipmentId);
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.Delivered, CurrentCustodianRiderId = null };
        return Task.FromResult<ShipmentResponse?>(null);
    }

    public Task<ShipmentResponse?> FailDeliveryAsync(Guid shipmentId, Guid riderId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        FailCalls.Add(shipmentId);
        var info = ShipmentsById[shipmentId];
        ShipmentsById[shipmentId] = info with { CurrentStatus = ShipmentStatus.DeliveryFailed };
        return Task.FromResult<ShipmentResponse?>(null);
    }
}

file class NotSupportedUserProvisioningService : IUserProvisioningService
{
    public Task<Guid> ProvisionStaffUserAsync(ProvisionStaffUserRequest request, Guid organizationId, Guid grantedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NotSupportedBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class RecordingObjectStorage : IObjectStorage
{
    public List<string> UploadedKeyPrefixes { get; } = new();
    public Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        UploadedKeyPrefixes.Add(keyPrefix);
        return Task.FromResult($"{keyPrefix}/{Guid.NewGuid():N}");
    }
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class RiderServiceAttemptsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RiderConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryTaskConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryRunConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryAttemptConfiguration());
            modelBuilder.ApplyConfiguration(new DeliveryProofConfiguration());
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

    private static async Task<(RiderService Service, FakeShipmentTransitionService Shipments, RecordingObjectStorage Storage, Guid RiderId, Guid OrganizationId)> SeedAsync(TestDbContext dbContext)
    {
        var organizationId = Guid.NewGuid();
        var rider = new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = Guid.NewGuid(), BaseBranchId = Guid.NewGuid(), LicenseNumber = "LIC-001", RoleType = RiderRoleType.DeliveryRider, IsActive = true };
        dbContext.Set<Rider>().Add(rider);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        var storage = new RecordingObjectStorage();
        var service = new RiderService(
            dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), new AllBranchesAuthorizationChecker(), shipments, storage);
        return (service, shipments, storage, rider.Id, organizationId);
    }

    private static ShipmentScanInfo OutForDeliveryInfo(Guid riderId, string awb = "AWB001") => new(
        Guid.NewGuid(), Guid.NewGuid(), awb, ShipmentStatus.OutForDelivery, null, Guid.NewGuid(), Guid.NewGuid(), riderId);

    [Fact]
    public async Task RecordFailedAttemptAsync_ValidRequest_CreatesFailedAttemptAndFailsTask()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, _, riderId, _) = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(riderId);
        shipments.ShipmentsById[info.ShipmentId] = info;
        dbContext.Set<DeliveryTask>().Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = Guid.NewGuid(), ShipmentId = info.ShipmentId, Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending });
        await dbContext.SaveChangesAsync();

        var result = await service.RecordFailedAttemptAsync(riderId,
            new RecordFailedAttemptRequest(info.ShipmentId, DeliveryAttemptReason.CustomerUnavailable, "Not home", null, null, true, "Retry tomorrow"),
            ActingUser(Guid.NewGuid()));

        Assert.False(result.Succeeded);
        Assert.Equal(DeliveryAttemptReason.CustomerUnavailable, result.Reason);
        Assert.Equal(1, result.AttemptNumber);
        Assert.Single(shipments.FailCalls);
        var task = await dbContext.Set<DeliveryTask>().SingleAsync(t => t.ShipmentId == info.ShipmentId);
        Assert.Equal(DeliveryTaskStatus.Failed, task.Status);
    }

    [Fact]
    public async Task RecordFailedAttemptAsync_ShipmentNotAssignedToThisRider_ThrowsRiderValidationException()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, _, riderId, _) = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(Guid.NewGuid());
        shipments.ShipmentsById[info.ShipmentId] = info;

        await Assert.ThrowsAsync<RiderValidationException>(() => service.RecordFailedAttemptAsync(riderId,
            new RecordFailedAttemptRequest(info.ShipmentId, DeliveryAttemptReason.Other, null, null, null, false, null),
            ActingUser(Guid.NewGuid())));
        Assert.Empty(shipments.FailCalls);
    }

    [Fact]
    public async Task CompleteDeliveryAsync_ValidRequest_CreatesProofAndCompletesTask()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, storage, riderId, _) = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(riderId);
        shipments.ShipmentsById[info.ShipmentId] = info;
        dbContext.Set<DeliveryTask>().Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = Guid.NewGuid(), ShipmentId = info.ShipmentId, Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending });
        await dbContext.SaveChangesAsync();
        var signatureKey = (await service.UploadPodFileAsync(riderId, PodFileKind.Signature, new MemoryStream(new byte[] { 1, 2, 3 }), "image/png", ActingUser(Guid.NewGuid()))).StorageKey;

        var result = await service.CompleteDeliveryAsync(riderId,
            new CompleteDeliveryRequest(info.ShipmentId, "Jane Doe", true, signatureKey, null, null, null),
            ActingUser(Guid.NewGuid()));

        Assert.True(result.Succeeded);
        Assert.Null(result.Reason);
        Assert.Single(shipments.CompleteCalls);
        Assert.Single(storage.UploadedKeyPrefixes);
        var proof = await dbContext.Set<DeliveryProof>().SingleAsync(p => p.ShipmentId == info.ShipmentId);
        Assert.True(proof.OtpVerified);
        Assert.Equal(signatureKey, proof.SignatureStorageKey);
        Assert.Equal("Jane Doe", proof.RecipientName);
        var task = await dbContext.Set<DeliveryTask>().SingleAsync(t => t.ShipmentId == info.ShipmentId);
        Assert.Equal(DeliveryTaskStatus.Completed, task.Status);
    }

    [Fact]
    public async Task RecordFailedAttemptAsync_SecondAttemptOnSameShipment_IncrementsAttemptNumber()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, _, riderId, organizationId) = await SeedAsync(dbContext);
        var info = OutForDeliveryInfo(riderId);
        shipments.ShipmentsById[info.ShipmentId] = info;
        // Simulate a prior attempt already on record (a real second OutForDelivery attempt needs
        // Phase 9's reattempt loop, out of this phase's scope — this proves the counting logic
        // alone, independent of the state machine).
        dbContext.Set<DeliveryAttempt>().Add(new DeliveryAttempt { Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = info.ShipmentId, RiderId = riderId, AttemptNumber = 1, AttemptedAt = DateTimeOffset.UtcNow, Succeeded = false, Reason = DeliveryAttemptReason.Other, CustomerContacted = false });
        await dbContext.SaveChangesAsync();

        var result = await service.RecordFailedAttemptAsync(riderId,
            new RecordFailedAttemptRequest(info.ShipmentId, DeliveryAttemptReason.WrongAddress, null, null, null, false, null),
            ActingUser(Guid.NewGuid()));

        Assert.Equal(2, result.AttemptNumber);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Riders/Application/RiderService.Attempts.cs tests/Riders.Tests/RiderServiceAttemptsTests.cs
git commit -m "feat(riders): add failed-attempt recording, POD upload, and delivery completion"
```

---

## Task 10: Riders Application — reads (`GetByIdAsync`/`ListAsync`/`ListVehiclesAsync`)

**Files:**
- Create: `src/Modules/Riders/Application/RiderService.Reads.cs`
- Test: `tests/Riders.Tests/RiderServiceReadsTests.cs`

**Interfaces:**
- Produces: `RiderService.GetByIdAsync(Guid, CancellationToken) -> Task<RiderResponse?>`, `RiderService.ListAsync(ClaimsPrincipal, CancellationToken) -> Task<List<RiderResponse>>`, `RiderService.ListVehiclesAsync(ClaimsPrincipal, CancellationToken) -> Task<List<VehicleResponse>>` — Task 11's Presentation calls all three.

- [ ] **Step 1: Write `RiderService.Reads.cs`**

```csharp
using System.Security.Claims;
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
    public async Task<RiderResponse?> GetByIdAsync(Guid riderId, CancellationToken cancellationToken = default)
    {
        var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == riderId, cancellationToken);
        return rider is null ? null : ToResponse(rider);
    }

    // Branch-scoped the same way ShipmentService.ListAsync (Phase 6) and BranchService.ListAsync
    // (Phase 2) already are: null from GetAccessibleBranchIdsAsync means AllBranches (no filter);
    // a non-null set restricts to riders based at one of those branches.
    public async Task<List<RiderResponse>> ListAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Rider>().AsQueryable();
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "rider.read", cancellationToken);
        if (accessibleBranchIds is not null)
        {
            var accessibleBranchIdsArray = accessibleBranchIds.ToArray();
            query = query.Where(r => accessibleBranchIdsArray.Contains(r.BaseBranchId));
        }

        return await query
            .Select(r => new RiderResponse(r.Id, r.UserId, r.BaseBranchId, r.LicenseNumber, r.RoleType, r.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<VehicleResponse>> ListVehiclesAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<Vehicle>()
            .Select(v => new VehicleResponse(v.Id, v.RiderId, v.Type, v.PlateNumber, v.Capacity))
            .ToListAsync(cancellationToken);
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Riders.Tests/RiderServiceReadsTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Riders.Infrastructure;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Riders.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class RestrictedBranchAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly HashSet<Guid> _accessibleBranchIds;
    public RestrictedBranchAuthorizationChecker(params Guid[] accessibleBranchIds) => _accessibleBranchIds = accessibleBranchIds.ToHashSet();
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(_accessibleBranchIds);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessibleBranchIds.Contains(branchId));
}

file class NotSupportedUserProvisioningService : IUserProvisioningService
{
    public Task<Guid> ProvisionStaffUserAsync(ProvisionStaffUserRequest request, Guid organizationId, Guid grantedByUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

file class NotSupportedBranchLookup : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
}

file class NotSupportedObjectStorage : IObjectStorage
{
    public Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public class RiderServiceReadsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RiderConfiguration());
            modelBuilder.ApplyConfiguration(new VehicleConfiguration());
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

    private static RiderService CreateService(TestDbContext dbContext, Guid organizationId, IBranchAuthorizationChecker checker) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new NotSupportedUserProvisioningService(),
            new NotSupportedBranchLookup(), checker, new NotSupportedShipmentTransitionService(), new NotSupportedObjectStorage());

    [Fact]
    public async Task ListAsync_RestrictedToOneBranch_OnlyReturnsRidersAtThatBranch()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var visibleBranchId = Guid.NewGuid();
        var hiddenBranchId = Guid.NewGuid();
        dbContext.Set<Rider>().Add(new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = Guid.NewGuid(), BaseBranchId = visibleBranchId, LicenseNumber = "LIC-1", RoleType = RiderRoleType.PickupRider, IsActive = true });
        dbContext.Set<Rider>().Add(new Rider { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = Guid.NewGuid(), BaseBranchId = hiddenBranchId, LicenseNumber = "LIC-2", RoleType = RiderRoleType.PickupRider, IsActive = true });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId, new RestrictedBranchAuthorizationChecker(visibleBranchId));

        var result = await service.ListAsync(ActingUser(Guid.NewGuid()));

        var rider = Assert.Single(result);
        Assert.Equal(visibleBranchId, rider.BaseBranchId);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid(), new RestrictedBranchAuthorizationChecker());

        var result = await service.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Riders/Application/RiderService.Reads.cs tests/Riders.Tests/RiderServiceReadsTests.cs
git commit -m "feat(riders): add branch-scoped rider and vehicle listing"
```

---

## Task 11: Riders Presentation — HTTP endpoints, `Program.cs` wiring, fix remaining broken test

**Files:**
- Create: `src/Modules/Riders/Presentation/RidersEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Modify: `tests/Api.Tests/ManifestsEndpointsTests.cs`

**Interfaces:**
- Consumes: every `RiderService` method from Tasks 7–10.
- Produces: the full `/api/v1/riders`, `/api/v1/vehicles` HTTP surface Task 12's API tests exercise.

- [ ] **Step 1: Write `RidersEndpoints.cs`**

```csharp
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Riders.Presentation;

public static class RidersEndpoints
{
    // Allowlisted content types for POD signature/photo uploads — same rationale and limit as
    // Merchants' KYC upload (Phase 4): a scanned signature or a delivery photo is typically JPEG/
    // PNG (signatures may also be uploaded as PDF from some capture devices).
    private static readonly string[] AllowedContentTypes = ["application/pdf", "image/jpeg", "image/png"];
    private const long MaxUploadSizeBytes = 10 * 1024 * 1024;

    public static void MapRidersEndpoints(this IEndpointRouteBuilder app)
    {
        var riders = app.MapGroup("/api/v1/riders").RequireAuthorization();
        var vehicles = app.MapGroup("/api/v1/vehicles").RequireAuthorization();

        riders.MapPost("/", async (CreateRiderRequest request, RiderService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CreateRiderAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/riders/{result.Id}", result);
            }
            catch (RiderValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "RIDER_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("rider.create");

        riders.MapGet("/", async (RiderService service, HttpContext httpContext) =>
            Results.Ok(await service.ListAsync(httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("rider.read");

        riders.MapGet("/{id:guid}", async (Guid id, RiderService service, HttpContext httpContext) =>
        {
            var result = await service.GetByIdAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("rider.read");

        vehicles.MapPost("/", async (CreateVehicleRequest request, RiderService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CreateVehicleAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/vehicles/{result.Id}", result);
            }
            catch (RiderValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "RIDER_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("rider.create");

        vehicles.MapGet("/", async (RiderService service, HttpContext httpContext) =>
            Results.Ok(await service.ListVehiclesAsync(httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("rider.read");

        riders.MapPost("/tasks", async (AssignDeliveryTaskRequest request, RiderService service, HttpContext httpContext) =>
            await HandleAsync(() => service.AssignTaskAsync(request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("delivery.assign");

        riders.MapPost("/{id:guid}/scan/pickup", async (Guid id, RiderScanRequest request, RiderService service, HttpContext httpContext) =>
            await HandleAsync(() => service.RiderPickupScanAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("shipment.scan");

        riders.MapPost("/{id:guid}/delivery-attempts/fail", async (Guid id, RecordFailedAttemptRequest request, RiderService service, HttpContext httpContext) =>
            await HandleAsync(() => service.RecordFailedAttemptAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("delivery.attempt");

        // Raw request body upload (not multipart/form-data) — same shape as
        // Merchants' KYC document upload (Phase 4): kind on the query string, bytes as the body.
        // Called once per file (signature, then separately photo) before delivery-attempts/complete,
        // whose request body references the returned StorageKey.
        riders.MapPost("/{id:guid}/pod-uploads", async (Guid id, PodFileKind kind, HttpContext httpContext, RiderService service) =>
        {
            var contentType = httpContext.Request.ContentType;
            if (contentType is null || !AllowedContentTypes.Any(allowed => contentType.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.BadRequest(new { error = new { code = "UNSUPPORTED_CONTENT_TYPE", message = "Only application/pdf, image/jpeg, and image/png are supported." } });
            }
            if (httpContext.Request.ContentLength is long contentLength && contentLength > MaxUploadSizeBytes)
            {
                return Results.BadRequest(new { error = new { code = "FILE_TOO_LARGE", message = "POD files must be 10MB or smaller." } });
            }

            try
            {
                var result = await service.UploadPodFileAsync(id, kind, httpContext.Request.Body, contentType, httpContext.User, httpContext.RequestAborted);
                return Results.Ok(result);
            }
            catch (RiderNotFoundException)
            {
                return Results.NotFound();
            }
        }).RequirePermission("delivery.complete");

        riders.MapPost("/{id:guid}/delivery-attempts/complete", async (Guid id, CompleteDeliveryRequest request, RiderService service, HttpContext httpContext) =>
            await HandleAsync(() => service.CompleteDeliveryAsync(id, request, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("delivery.complete");
    }

    private static async Task<IResult> HandleAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (RiderNotFoundException)
        {
            return Results.NotFound();
        }
        catch (RiderValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "RIDER_VALIDATION_FAILED", message = ex.Message } });
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

- [ ] **Step 2: Wire into `Program.cs`**

In `src/Api/Program.cs`, add `using Courier.Riders.Presentation;` to the usings, and after `app.MapManifestsEndpoints();`:

```csharp
app.MapRidersEndpoints();
```

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build src/Api/Api.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Apply Task 2 Step 9's deferred fix to `ManifestsEndpointsTests.cs`**

Apply exactly the edit documented in Task 2 Step 9: replace `CreateShipmentAtOriginBranchAsync` with the version that creates a throwaway `PickupRider` and drives the real pickup flow, and add the two `using` statements. Do not re-derive the edit — copy it verbatim from Task 2 Step 9.

- [ ] **Step 5: Full solution build and test run**

Run: `dotnet build`
Expected: Build succeeds with zero errors across every project.

Run: `dotnet test`
Expected: Every test project passes, including `Manifests.Tests`, `Shipments.Tests`, `Identity.Tests`, `Riders.Tests`, and `Api.Tests` (the Postgres-backed `Api.Tests`/`Identity.Tests` projects require `courier-postgres-1` running on port 5434).

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Riders/Presentation/RidersEndpoints.cs src/Api/Program.cs tests/Api.Tests/ManifestsEndpointsTests.cs
git commit -m "feat(riders): add Riders HTTP endpoints and wire into the API host"
```

---

## Task 12: API tests — full success/failure lifecycle, tenant isolation, permission scope

**Files:**
- Create: `tests/Api.Tests/RidersEndpointsTests.cs`

**Interfaces:**
- Consumes: the full `/api/v1/riders`, `/api/v1/vehicles`, `/api/v1/manifests`, `/api/v1/shipments` HTTP surface.

- [ ] **Step 1: Write the test file**

Create `tests/Api.Tests/RidersEndpointsTests.cs` — helper methods are copied (not shared) from `ManifestsEndpointsTests.cs`, matching this project's established per-file-helper convention:

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
using Xunit;

namespace Courier.Api.Tests;

public class RidersEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public RidersEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Riders Org {suffix}", $"riders-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
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

    // Walks a shipment from Created all the way to AtDestinationBranch via the real pickup-rider
    // flow (Task 8) and Manifests' real scan/dispatch/receive flow (Phase 7) — the exact hand-off
    // point Riders' delivery-side flow (Task 9) takes over from.
    private async Task<ShipmentResponse> BookAndMoveToDestinationAsync(
        HttpClient client, Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, Guid pickupRiderId)
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

        return (await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>())!;
    }

    [Fact]
    public async Task FullSuccessPath_PickupToDelivered_MovesCustodyThroughRiderAndCapturesPod()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "success");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var afterOrigin = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await afterOrigin.Content.ReadFromJsonAsync<ShipmentResponse>();

        var assignPickupResponse = await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        Assert.Equal(HttpStatusCode.OK, assignPickupResponse.StatusCode);
        var afterAssignPickup = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.PickupAssigned, afterAssignPickup!.CurrentStatus);
        Assert.Equal(pickupRiderId, afterAssignPickup.CurrentCustodianRiderId);

        var scanResponse = await client.PostAsJsonAsync($"/api/v1/riders/{pickupRiderId}/scan/pickup", new RiderScanRequest(shipment.Awb));
        Assert.Equal(HttpStatusCode.OK, scanResponse.StatusCode);
        var afterScan = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.PickedUp, afterScan!.CurrentStatus);

        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { });
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));
        var atDestination = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.AtDestinationBranch, atDestination!.CurrentStatus);

        var assignDeliveryResponse = await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));
        Assert.Equal(HttpStatusCode.OK, assignDeliveryResponse.StatusCode);
        var afterAssignDelivery = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.OutForDelivery, afterAssignDelivery!.CurrentStatus);
        Assert.Equal(deliveryRiderId, afterAssignDelivery.CurrentCustodianRiderId);

        var signatureContent = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
        signatureContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var uploadResponse = await client.PostAsync($"/api/v1/riders/{deliveryRiderId}/pod-uploads?kind=Signature", signatureContent);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadPodFileResponse>();

        var completeResponse = await client.PostAsJsonAsync($"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete",
            new CompleteDeliveryRequest(shipment.Id, "Recipient One", true, uploadResult!.StorageKey, null, null, null));
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var attempt = await completeResponse.Content.ReadFromJsonAsync<DeliveryAttemptResponse>();
        Assert.True(attempt!.Succeeded);

        var delivered = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Delivered, delivered!.CurrentStatus);
        Assert.Null(delivered.CurrentCustodianRiderId);
    }

    [Fact]
    public async Task FullFailurePath_OutForDeliveryToDeliveryFailed_KeepsRiderCustodyAndRecordsAttempt()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "failure");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var shipment = await BookAndMoveToDestinationAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId);
        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));

        var failResponse = await client.PostAsJsonAsync($"/api/v1/riders/{deliveryRiderId}/delivery-attempts/fail",
            new RecordFailedAttemptRequest(shipment.Id, DeliveryAttemptReason.CustomerUnavailable, "No one home", null, null, true, "Retry tomorrow"));
        Assert.Equal(HttpStatusCode.OK, failResponse.StatusCode);
        var attempt = await failResponse.Content.ReadFromJsonAsync<DeliveryAttemptResponse>();
        Assert.False(attempt!.Succeeded);
        Assert.Equal(DeliveryAttemptReason.CustomerUnavailable, attempt.Reason);

        var afterFail = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.DeliveryFailed, afterFail!.CurrentStatus);
        // Custody stays with the rider — the parcel is still physically with them (Task 2's design note).
        Assert.Equal(deliveryRiderId, afterFail.CurrentCustodianRiderId);
    }

    [Fact]
    public async Task Riders_OrgBCannotReadOrgAsRider()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "iso-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(clientA);
        var riderId = await CreateRiderAsync(clientA, originBranchId, RiderRoleType.PickupRider);

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "iso-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var response = await clientB.GetAsync($"/api/v1/riders/{riderId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PickupRider_CannotAssignDeliveryTasks_Returns403()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "perm-scope");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var suffix = Guid.NewGuid().ToString("N");
        var riderEmail = $"rider-{suffix}@example.com";
        await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            riderEmail, "P@ssw0rd123!", "Pickup Only Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));
        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();

        var riderClient = _factory.CreateClient();
        var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmail, "P@ssw0rd123!"));
        var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);

        // PickupRider holds only shipment.scan (Task 3) — delivery.assign is DeliveryRider-only,
        // proving the branch-scoped role split is enforced at the HTTP layer, not just the
        // permission catalog.
        var response = await riderClient.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(Guid.NewGuid(), shipment!.Id, DeliveryTaskType.Delivery));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostRiders_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            "x@example.com", "P@ssw0rd123!", "X", null, "LIC-X", Guid.NewGuid(), RiderRoleType.PickupRider));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test`
Expected: Every test project passes, including all five new tests in this file.

- [ ] **Step 3: Commit**

```bash
git add tests/Api.Tests/RidersEndpointsTests.cs
git commit -m "test(riders): add end-to-end success/failure lifecycle, tenant isolation, and permission-scope tests"
```

---

## Task 13: Documentation — `CLARIFICATIONS.md`, `ROADMAP.md`, `DOMAIN-MODEL.md`, `RBAC.md`

Per this project's Cross-Phase Rule (`ROADMAP.md`'s final section): every ambiguity resolved during this phase gets written down before the phase is considered done.

**Files:**
- Modify: `docs/CLARIFICATIONS.md`
- Modify: `docs/ROADMAP.md`
- Modify: `docs/DOMAIN-MODEL.md`
- Modify: `docs/RBAC.md`

- [ ] **Step 1: Add `CLARIFICATIONS.md` entry #8**

Insert before the `## Summary of schema additions implied by this document` heading:

```markdown
## 8. Phase 8 gaps: PickupRider's permission set, RiderDelivery's scan step, and staff user provisioning

**Where:** §6/§17-19 (Rider domain), `RBAC.md` §4, `BRANCH-OPERATIONS.md` §4

**Ambiguity 1:** `RBAC.md` §4 spells out `DeliveryRider`'s permission set explicitly but only
describes `PickupRider` as "Pickup task permissions" — no concrete list.

**Resolution 1:** `PickupRider` holds `shipment.scan` only. A pickup rider's entire job this phase
is the pickup scan (`RiderPickupScanAsync`); nothing else in the v1 permission catalog is
pickup-specific. `DeliveryRider` holds `delivery.attempt`, `delivery.complete`, `cod.collect`,
`shipment.scan` exactly as `RBAC.md` §4 already specified. Both roles are seeded per-organization
at registration (`AuthService.RegisterOrganizationAsync`), same as `OrganizationOwner`.

**Ambiguity 2:** `BRANCH-OPERATIONS.md` §4 names `RiderDelivery` as if it were its own scan action
distinct from assignment, but `SHIPMENT-LIFECYCLE.md`'s transition table has exactly one arrow into
`OUT_FOR_DELIVERY` ("delivery rider assignment + out-for-delivery scan") — never two, unlike pickup
which has two (`PICKUP_ASSIGNED` is a real, separately-observable status).

**Resolution 2:** `RiderDelivery` is not a second call after assignment. Assigning a delivery task
to a rider (`RiderService.AssignTaskAsync` with `Type = Delivery`) performs the
`AtDestinationBranch -> OutForDelivery` transition in the same call — that assignment call IS the
`RiderDelivery` scan. `RiderPickup` keeps its two-step shape (assign, then a separate physical scan)
because `PICKUP_ASSIGNED` is a real status a dispatch board needs to show; there's no equivalent
named status between "delivery assigned" and "out for delivery" for a second step to represent.

**Ambiguity 3:** `DOMAIN-MODEL.md`'s `Rider.UserId` requires "a User with the PickupRider/
DeliveryRider role", but nothing in Phases 1-7 ever creates a second staff `User` in an
organization — `AuthService.RegisterOrganizationAsync` only creates the org-registration owner, and
no endpoint anywhere invites/provisions additional staff.

**Resolution 3:** Phase 8 adds a narrow `Identity.Application.IUserProvisioningService` (used only
by `Riders.Application.RiderService.CreateRiderAsync`) that creates a `User` + `UserRole`
(`BranchScope.SpecificBranches`) + `UserBranch` grant for one of the organization's already-seeded
role names. This is deliberately NOT a general "invite any staff member with any role" feature — a
real user-management/invite UI (covering `BranchManager`, `Dispatcher`, `SortingStaff`, etc.) is
still deferred, tracked as a known gap for whichever future phase needs it.
```

- [ ] **Step 2: Update `ROADMAP.md`'s Phase 8 section**

Replace the Phase 8 section (currently ending at "or complete delivery with OTP/signature/photo/GPS POD.") by appending a `*Status*` paragraph immediately after the existing `*Exit:*` line, matching every prior phase's format:

```markdown
*Status (2026-08-21): Complete.* A new `Riders` module implements `Rider, Vehicle, DeliveryRun,
DeliveryTask, DeliveryAttempt, DeliveryProof` per `DOMAIN-MODEL.md` — with `RiderAssignment` dropped
from the original sketch (`Rider.BaseBranchId` is the sole branch scope; approved during
brainstorming, `CLARIFICATIONS.md` #8 records the two other Phase 8 design ambiguities it resolved).
A rider is a real `Identity.User` holding the `PickupRider`/`DeliveryRider` role — both roles are now
seeded per-organization at registration alongside `OrganizationOwner`, and a new narrow
`IUserProvisioningService` in `Identity.Application` (Task 3) closes the gap that nothing before this
phase could create a second staff user at all. `RiderPickup`/`RiderDelivery` are the real scan
operations `BRANCH-OPERATIONS.md` §4 earmarked for this module, implemented via five new methods on
`Shipments`' `IShipmentTransitionService` (`AssignPickupToRiderAsync`, `RiderPickupScanAsync`,
`AssignDeliveryToRiderAsync`, `CompleteDeliveryAsync`, `FailDeliveryAsync`) — `Riders` performs the
full `BRANCH-OPERATIONS.md` §1 five-check validation itself before calling in, mirroring the
`Manifests`/`Shipments` division of responsibility Phase 7 established. This retires the Phase 6
placeholder `pickup-assign`/`pickup-scan`/`out-for-delivery`/`deliver` endpoints entirely — a
shipment can no longer move through pickup or delivery outside a real rider. `DeliveryRun` is
auto-created/reused (found-or-created per rider+branch+day the first time a task is assigned, no
explicit "start my run" endpoint) and `DeliveryTask.Sequence` increments within it, proven by
`tests/Riders.Tests/RiderServiceTasksTests.cs`'s
`AssignTaskAsync_TwoPickupTasksSameRiderSameDay_ReuseSameRunWithIncrementingSequence`. A failed
delivery attempt transitions the shipment to the newly-added `DeliveryFailed` status and
deliberately leaves custody (`CurrentCustodianRiderId`) on the rider rather than clearing it — the
parcel is still physically with them until Phase 9's NDR/reattempt/RTO flow moves it again; this
phase stops exactly at `DELIVERY_FAILED` and does not build `NDR_CREATED` or the reattempt loop.
`DeliveryProof.OtpVerified` is a client-reported boolean this phase (no server-side OTP generate/
verify flow — that needs Phase 11's real SMS delivery to be meaningful); signature/photo uploads
reuse the exact `IObjectStorage`/raw-body-upload pattern Phase 4 built for merchant KYC documents.
The full success path (pickup scan through POD capture to `DELIVERED`) and the full failure path
(assignment through a failed attempt to `DELIVERY_FAILED`, proving custody stays with the rider) are
each proven end-to-end over real HTTP in `tests/Api.Tests/RidersEndpointsTests.cs`, alongside tenant
isolation and a permission-scope test proving a `PickupRider` cannot call a `DeliveryRider`-only
endpoint. `Rider`/`Vehicle`/`DeliveryAttempt`/`DeliveryProof` carry their own `OrganizationId`;
`DeliveryRun`/`DeliveryTask` do not — isolation flows transitively through
`RiderId -> Rider.OrganizationId`, the same pattern `ManifestShipment`/`ShipmentItem` already
established. Deferred to later phases: `NDR`/`RTO` (Phase 9), real COD collection linkage (Phase
10), real OTP delivery (Phase 11), and a general staff user-invite feature beyond the two rider
roles this phase needed.
```

- [ ] **Step 3: Update `DOMAIN-MODEL.md`'s "Riders & Delivery" section**

Replace the existing block (from `## Riders & Delivery` through the `DeliveryProof` closing brace) with:

```markdown
## Riders & Delivery — `OrganizationId` present on Rider/Vehicle/DeliveryAttempt/DeliveryProof only

```text
Rider { Id, OrganizationId, UserId (FK — a rider is also a User with the
        PickupRider/DeliveryRider role), BaseBranchId, LicenseNumber, RoleType, IsActive }
        # No RiderAssignment entity (Phase 8's resolved design decision) — BaseBranchId is the
        # sole branch scope.

Vehicle { Id, OrganizationId, RiderId (nullable), Type, PlateNumber, Capacity }

DeliveryRun { Id, RiderId, BranchId, Date, Status (Active|Completed) }
             # No own OrganizationId — flows transitively through RiderId -> Rider.OrganizationId.
             # Auto-created/reused per (RiderId, BranchId, Date) when the first task of the day is
             # assigned; no explicit "start my run" endpoint (Phase 8).

DeliveryTask { Id, DeliveryRunId, ShipmentId, Type (Pickup|Delivery), Sequence,
               Status (Pending|Completed|Failed) }
             # No own OrganizationId — flows transitively through DeliveryRunId -> DeliveryRun ->
             # RiderId -> Rider.OrganizationId.

DeliveryAttempt { Id, OrganizationId, ShipmentId, RiderId, AttemptNumber, AttemptedAt, Succeeded,
                   Reason (CustomerUnavailable|PhoneUnreachable|WrongAddress|
                   IncompleteAddress|CustomerRefused|CustomerRequestedReschedule|
                   CashUnavailable|DamagedParcel|Other — null iff Succeeded),
                   Notes, Latitude, Longitude, CustomerContacted, NextAction (plain string) }
                 # Succeeded is additive beyond this document's original sketch — one row per real
                 # attempt (success or failure), disambiguated by this flag rather than a
                 # nullable-Reason convention (Phase 8).

DeliveryProof { Id, OrganizationId, ShipmentId, DeliveryAttemptId, RiderId, OtpVerified,
                SignatureStorageKey, PhotoStorageKey, Latitude, Longitude, Timestamp,
                RecipientName, CorrectedAt (nullable), CorrectionReason (nullable) }
                # Immutable after successful capture except via the correction fields above, which
                # never overwrite the original capture. OtpVerified is a client-reported boolean —
                # no server-side OTP generate/verify flow exists yet (Phase 8; needs Phase 11's real
                # SMS delivery to be meaningful). No correction endpoint exists yet either — the
                # columns exist now so a later phase adding one needs no migration.
```
```

- [ ] **Step 4: Update `RBAC.md`**

In the "Default Roles" table (§2), update the `PickupRider` row's Notes column from "Pickup task permissions" to "`shipment.scan` only — Phase 8". In §4's "Illustrative Role → Permission Coverage" list, add after the existing `DeliveryRider` bullet:

```markdown
* **PickupRider**: `shipment.scan` (rider pickup scan only), branch-scoped to their base branch —
  the full permission set, not merely illustrative (`CLARIFICATIONS.md` #8).
```

- [ ] **Step 5: Commit**

```bash
git add docs/CLARIFICATIONS.md docs/ROADMAP.md docs/DOMAIN-MODEL.md docs/RBAC.md
git commit -m "docs: mark Phase 8 exit criteria met and record its resolved design ambiguities"
```

---

## Self-Review

**Spec coverage:** `Rider`/`Vehicle`/`RiderAssignment`(dropped, documented)/`DeliveryRun`/`DeliveryTask`/`DeliveryAttempt`/`DeliveryProof` (Tasks 4-6) · rider assignment (Task 8) · failed attempt with a `CLAUDE.md` §19 reason (Task 9) · OTP/signature/photo/GPS POD completion (Task 9) · real `RiderPickup`/`RiderDelivery` scan operations replacing the Phase 6 placeholders (Tasks 1-2, 8) · tenant isolation + branch-scope tests (Task 12) · docs updated (Task 13). Every `ROADMAP.md` Phase 8 exit-criteria clause has a task.

**Placeholder scan:** no TBD/TODO markers; the one intentionally-deferred edit (Task 2 Step 9 → applied in Task 11 Step 4) is explicit about why and exactly where it's picked back up, not a vague "later."

**Type consistency:** `RiderResponse`, `VehicleResponse`, `DeliveryTaskResponse`, `DeliveryAttemptResponse`, `UploadPodFileResponse` field shapes are identical everywhere they're constructed (Task 7/8/9) and consumed (Task 11 Presentation, Task 12 tests). `IShipmentTransitionService`'s five new method signatures (Task 2) match exactly between the interface, `ShipmentService`'s implementation, and every fake in Tasks 7-10's tests. `ShipmentScanInfo`/`ShipmentResponse`'s new `CurrentCustodianRiderId` field is threaded consistently through Task 1's changes and every later task that constructs or asserts on one.
