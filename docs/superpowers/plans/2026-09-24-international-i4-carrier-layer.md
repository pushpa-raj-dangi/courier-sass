# International I4 — Carrier Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carry international shipments past `CustomsPrepared` / `Created` through partner carriers — booking, handoff and receive scans, carrier tracking ingest (webhook, polling, manual entry) applied through the state machine — with a manual carrier adapter and full, carrier-agnostic infrastructure.

**Architecture:** Everything carrier-related lives in the `International` module (`CarrierAccount`, `CarrierBooking`, `CarrierTrackingEvent`, `ICarrierProvider` port + registry, `ManualCarrierProvider`, services, Hangfire jobs, endpoints). `Shipments` stays the sole owner of the state machine, custody and `ShipmentEvent`s; `International` drives it only through new `IShipmentInternationalGateway` methods. Ingest is store-then-process: every source inserts deduplicated `CarrierTrackingEvent`s, one processor applies them per tenant through a background tenant scope.

**Tech Stack:** ASP.NET Core (.NET 10), EF Core 10 + Npgsql/PostgreSQL, Hangfire 1.8, ASP.NET Data Protection, `Microsoft.Extensions.Http.Resilience` (Polly v8), xUnit, EF InMemory (unit tests), `WebApplicationFactory` (API tests).

**Spec:** `docs/superpowers/specs/2026-09-24-international-i4-carrier-layer-design.md` (parent: `docs/superpowers/specs/2026-09-21-international-support-design.md`).

## Global Constraints

- Seven new `ShipmentStatus` values and seven matching `ShipmentEventType` values, appended in this exact order after `CustomsPrepared`: `HandedToCarrier`, `InInternationalTransit`, `DeliveredAbroad`, `CustomsHold`, `InternationalException`, `CustomsClearance`, `ReceivedFromCarrier`. Never reorder existing values (persisted as integers).
- Every new status is reachable only when `Scope == International`.
- Allowed transitions are exactly the spec §3.2/§3.3 tables (encoded once, in `InternationalTransitionRules`). Carrier events never move a shipment backwards; an event whose target equals the current status is a no-op.
- `International` never calls `Set<Shipment>()`/`Set<ShipmentItem>()`/`Set<ShipmentEvent>()`; it goes through `IShipmentInternationalGateway`, `IShipmentTransitionService`, `IShipmentItemLookupService`.
- New tables (`CarrierAccounts`, `CarrierBookings`, `CarrierTrackingEvents`) are tenant-owned: `OrganizationId`, FK to `Organization` (Restrict), standard tenant query filter. Migrations are additive only; never edit an existing migration.
- Secrets (carrier credentials, webhook secret) are encrypted with Data Protection (purposes `International.CarrierAccount.Credentials`, `International.CarrierAccount.WebhookSecret`), never returned by the API (except the one-time webhook secret in create/rotate responses) and never logged.
- Webhook tenant is always taken from the resolved `CarrierAccount`, never from the payload. Signature comparison is constant-time.
- New permissions: `international.carrier.manage` (OrganizationOwner/Admin), `international.book` (OperationsManager, HubStaff), `international.tracking.manage` (OperationsManager). `international.handoff`/`international.receive` are now granted to OperationsManager and HubStaff.
- Error codes: `INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` 400, `CARRIER_ACCOUNT_VALIDATION_FAILED` 400, `SHIPMENT_VALIDATION_FAILED` 400, `SHIPMENT_INVALID_TRANSITION` 400, `CARRIER_BOOKING_FAILED` 502, `CARRIER_UNAVAILABLE` 503. Invisible/other-tenant resources → 404.
- Every validation runs before any carrier call or database write.
- Resilience defaults: attempt timeout 10 s; 3 retries, exponential backoff with jitter; circuit breaker failure ratio 0.5 over 30 s, minimum throughput 10, break 30 s; total timeout 30 s.
- Commit trailer on every commit: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Test infra: shared containers `courier-postgres-1` (port 5434), `courier-redis-1`, `courier-rabbitmq-1`, `courier-mailhog-1` must already be running — never start a second Postgres. Connection string: `Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password`. Migrations: `dotnet ef migrations add <Name> --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj` then `dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`.

## Plan-level rulings (deviations from the spec text, decided while planning)

- **No outbox write on international transitions.** Spec §5.8 says transitions get the outbox "for free"; the existing `TransitionAsync` writes no outbox row (only `CreateShipmentAsync`/shell creation do). Merchant-facing webhook event types are I7 anyway. Task 16 corrects the spec sentence.
- **`Processing` processing status.** Spec §4.3 lists `Pending | Applied | Ignored | Rejected | Unmatched`. The processor atomically claims events (`Pending → Processing`) so Hangfire runs and direct invocations never double-apply; stuck claims older than 5 minutes are reclaimed. Adds `Processing`, `ProcessingAttempts`, `ClaimedAt`.
- **No separate `deactivate` route.** Spec §5.10 lists both `update (display name, active, polling)` and `deactivate`; `PUT` covers `IsActive` (including reactivation), so only `PUT` is built.
- **`MappedShipmentStatus` stored as a string** (e.g. `"CustomsHold"`) so `International.Domain` does not depend on `Shipments.Domain`; `CarrierBooking.Direction` uses International's own `CarrierBookingDirection` enum for the same reason.
- **Book idempotency is enforced twice:** the existing `IdempotencyMiddleware` (staff partition, same as rider actions) and a DB-unique `(OrganizationId, IdempotencyKey)` on `CarrierBookings`. Replaying a key whose booking `Failed` returns the same 502.

## Review Focus

1. **Out-of-order carrier events in one delivery** (a webhook body listing `DELIVERED` before `TRANSIT`) — the processor must order by `OccurredAt` so the shipment still reaches `DeliveredAbroad`. Test: Task 11 `Processor_AppliesEventsInOccurredAtOrder_EvenWhenInsertedOutOfOrder`.
2. **The same carrier AWB used by two tenants' accounts** — an event must only ever match the booking on its own account. Test: Task 11 `Applier_MatchesBookingByAccountAndAwb_NotByAwbAlone`.
3. **A late event for a cancelled booking** — must end `Unmatched`, never applied. Test: Task 11 `Applier_EventForCancelledBooking_IsUnmatched`.
4. **Malformed webhook JSON with a valid signature** — 400 `CARRIER_WEBHOOK_INVALID_PAYLOAD`, nothing stored, never 500. Test: Task 13 `ReceiveWebhook_ValidSignatureMalformedBody_ReturnsInvalidPayload`.
5. **The same carrier event arriving from both webhook and poll** — stored once. Test: Task 11 `InsertAsync_DuplicateEventIdAcrossSources_StoredOnce`.

---

## File Structure

| File | Responsibility |
|---|---|
| `tests/Api.Tests/NonParallelCollection.cs` (new) | xUnit collection that runs after all parallel collections |
| `src/SharedKernel/TenantScopeOverride.cs` (new) | Scoped holder for a background job's organization |
| `src/SharedKernel/ITenantScopeFactory.cs` (new) | `ITenantScopeFactory` + `TenantScopeFactory` |
| `src/SharedKernel/HttpContextCurrentTenantProvider.cs` | Consults `TenantScopeOverride` first |
| `src/Modules/Shipments/Domain/ShipmentStatus.cs`, `ShipmentEventType.cs` | + 7 values each |
| `src/Modules/Shipments/Domain/InternationalTransitionRules.cs` (new) | The §3.2/§3.3 transition table |
| `src/Modules/Shipments/Application/PublicTrackingStatusMapper.cs` | New status mappings |
| `src/Modules/Shipments/Application/IShipmentTransitionService.cs` | `ShipmentScanInfo.Direction` |
| `src/Modules/Shipments/Application/IShipmentInternationalGateway.cs` | + 4 lifecycle methods, `CarrierEventReference`, `CarrierHandoffReference`, `CarrierStatusApplyResult` |
| `src/Modules/Shipments/Application/ShipmentService.InternationalLifecycle.cs` (new) | Gateway lifecycle implementation |
| `src/Modules/Shipments/Application/ShipmentService.Transitions.cs` | `SortAsync` accepts inbound `ReceivedFromCarrier` |
| `src/Modules/Manifests/Application/ManifestService.Scanning.cs` | Sort scan accepts inbound `ReceivedFromCarrier` |
| `src/Modules/International/Domain/Carrier*.cs` (new) | Entities + enums |
| `src/Modules/International/Infrastructure/Carrier*Configuration.cs` (new) | EF configuration |
| `src/Persistence/AppDbContext.cs` | DbSets, configs, Organization FKs, query filters |
| `src/Modules/Identity/Application/PermissionCatalog.cs`, `AdditionalRoleSeeds.cs` | Permissions + grants |
| `src/Modules/International/Application/Carriers/*.cs` (new) | Port, DTOs, registry, exceptions, masker, secret-protector interface, tracking window |
| `src/Modules/International/Infrastructure/Carriers/*.cs` (new) | `ManualCarrierProvider`, `DataProtectionCarrierSecretProtector`, `CarrierHttpClientExtensions`, `HangfireCarrierJobScheduler` |
| `src/Modules/International/Application/CarrierAccountService.cs` (new) | Account CRUD, secrets |
| `src/Modules/International/Application/CarrierBookingService.cs` (new) | Book, cancel, link inbound |
| `src/Modules/International/Application/CarrierTrackingEventWriter.cs` (new) | Deduplicated insert |
| `src/Modules/International/Application/CarrierWebhookService.cs` (new) | Webhook verification + ingest |
| `src/Modules/International/Application/ManualCarrierEventService.cs` (new) | Staff-entered carrier events |
| `src/Modules/International/Application/CarrierEventApplier.cs` (new) | Applies one event in a tenant scope |
| `src/Modules/International/Application/CarrierEventProcessorJob.cs` (new) | Claims + dispatches per tenant |
| `src/Modules/International/Application/CarrierTrackingPollJob.cs` (new) | Polling |
| `src/Modules/International/Application/InternationalCarrierLifecycleService.cs` (new) | Handoff, receive, override |
| `src/Modules/International/Application/CarrierDtos.cs` (new) | Request/response records |
| `src/Modules/International/Presentation/InternationalEndpointErrors.cs` (new) | Shared exception → result mapping |
| `src/Modules/International/Presentation/CarrierAccountEndpoints.cs`, `CarrierWebhookEndpoints.cs` (new), `InternationalEndpoints.cs` | Routes |
| `src/Api/Middleware/IdempotencyMiddleware.cs` | Book route |
| `src/Api/Program.cs` | DI + recurring jobs + endpoint mapping |
| `tests/Api.Tests/International/InternationalApiTestBase.cs` (new) | Shared API test helpers (moved from the I3 test class) |
| `tests/Api.Tests/International/FakeHttpCarrier.cs` (new) | Test-only HTTP carrier + stub handler |

---

### Task 1: Run the JWT-rotation test outside the parallel test run

`PlatformSettingsJwtRotationTests` rotates the real shared `Jwt.SigningKey` and broadcasts it over Redis, which invalidates every concurrently running test's tokens (bursts of 401s in unrelated tests). xUnit 2 runs a collection with `DisableParallelization = true` only after all parallel collections finish.

**Files:**
- Create: `tests/Api.Tests/NonParallelCollection.cs`
- Modify: `tests/Api.Tests/PlatformSettingsJwtRotationTests.cs:24`

**Interfaces:**
- Produces: `NonParallelCollection.Name` (`"NonParallel"`) for any future test that mutates process-wide shared state.

- [ ] **Step 1: Create the collection definition**

```csharp
using Xunit;

namespace Courier.Api.Tests;

// Test classes in this collection run only after every parallel collection has finished (xUnit 2
// schedules DisableParallelization collections last, one at a time). Use it for tests that mutate
// process-wide shared state other tests depend on — e.g. PlatformSettingsJwtRotationTests rotates
// the real Jwt.SigningKey and broadcasts it over Redis, invalidating every concurrently running
// test's access tokens.
[CollectionDefinition(Name, DisableParallelization = true)]
public class NonParallelCollection
{
    public const string Name = "NonParallel";
}
```

- [ ] **Step 2: Put the rotation test in it**

In `tests/Api.Tests/PlatformSettingsJwtRotationTests.cs`, add the attribute directly above the class declaration:

```csharp
[Collection(NonParallelCollection.Name)]
public class PlatformSettingsJwtRotationTests : IClassFixture<WebApplicationFactory<Program>>
```

- [ ] **Step 3: Run the rotation test alone**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~PlatformSettingsJwtRotationTests"`
Expected: PASS.

- [ ] **Step 4: Run the full Api.Tests suite twice**

Run: `dotnet test tests/Api.Tests` (twice)
Expected: both runs pass. If a failure appears, rerun that test alone; record in the task report any test that fails in the full run but passes alone, with its name and error (this is evidence for later tasks, not a blocker).

- [ ] **Step 5: Commit**

```bash
git add tests/Api.Tests/NonParallelCollection.cs tests/Api.Tests/PlatformSettingsJwtRotationTests.cs
git commit -m "test(api): run JWT signing-key rotation test outside the parallel run

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Background tenant scope

Background jobs have no `HttpContext`, so `ICurrentTenantProvider.OrganizationId` is null and every tenant-filtered query returns nothing. The carrier processor and poller must run tenant-filtered services (`ShipmentService`, International services) as a specific organization.

**Files:**
- Create: `src/SharedKernel/TenantScopeOverride.cs`
- Create: `src/SharedKernel/ITenantScopeFactory.cs`
- Modify: `src/SharedKernel/HttpContextCurrentTenantProvider.cs`
- Modify: `src/Api/Program.cs:154`
- Test: `tests/SharedKernel.Tests/TenantScopeFactoryTests.cs`

**Interfaces:**
- Produces: `public interface ITenantScopeFactory { AsyncServiceScope CreateScope(Guid organizationId); }` (singleton). Resolving `ICurrentTenantProvider` inside the returned scope yields `organizationId`.
- Produces: `public sealed class TenantScopeOverride { public Guid? OrganizationId { get; set; } }` (scoped).

- [ ] **Step 1: Write the failing test**

```csharp
using Courier.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class TenantScopeFactoryTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddScoped<TenantScopeOverride>();
        services.AddScoped<ICurrentTenantProvider, HttpContextCurrentTenantProvider>();
        services.AddSingleton<ITenantScopeFactory, TenantScopeFactory>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CreateScope_ProviderInThatScopeReturnsTheGivenOrganization()
    {
        await using var provider = BuildProvider();
        var factory = provider.GetRequiredService<ITenantScopeFactory>();
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();

        await using var scopeA = factory.CreateScope(organizationA);
        await using var scopeB = factory.CreateScope(organizationB);

        Assert.Equal(organizationA, scopeA.ServiceProvider.GetRequiredService<ICurrentTenantProvider>().OrganizationId);
        Assert.Equal(organizationB, scopeB.ServiceProvider.GetRequiredService<ICurrentTenantProvider>().OrganizationId);
    }

    [Fact]
    public async Task PlainScope_WithoutHttpContextOrOverride_HasNoOrganization()
    {
        await using var provider = BuildProvider();

        await using var scope = provider.CreateAsyncScope();

        Assert.Null(scope.ServiceProvider.GetRequiredService<ICurrentTenantProvider>().OrganizationId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SharedKernel.Tests --filter "FullyQualifiedName~TenantScopeFactoryTests"`
Expected: FAIL — compile error, `TenantScopeOverride`/`ITenantScopeFactory` do not exist. (If `ServiceCollection`/`AddHttpContextAccessor` don't resolve, add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` to `tests/SharedKernel.Tests/SharedKernel.Tests.csproj`.)

- [ ] **Step 3: Implement**

`src/SharedKernel/TenantScopeOverride.cs`:

```csharp
namespace Courier.SharedKernel;

// Scoped. Set only by TenantScopeFactory.CreateScope, for background jobs (no HttpContext) that
// must run tenant-filtered application services as one specific organization. No request path
// ever sets it, so HTTP requests keep resolving the organization from the caller's own JWT.
public sealed class TenantScopeOverride
{
    public Guid? OrganizationId { get; set; }
}
```

`src/SharedKernel/ITenantScopeFactory.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Courier.SharedKernel;

public interface ITenantScopeFactory
{
    // A new DI scope whose ICurrentTenantProvider (and therefore every tenant query filter on
    // the scope's AppDbContext) resolves to organizationId. The caller disposes it.
    AsyncServiceScope CreateScope(Guid organizationId);
}

public sealed class TenantScopeFactory : ITenantScopeFactory
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public TenantScopeFactory(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    public AsyncServiceScope CreateScope(Guid organizationId)
    {
        var scope = _serviceScopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantScopeOverride>().OrganizationId = organizationId;
        return scope;
    }
}
```

Replace `src/SharedKernel/HttpContextCurrentTenantProvider.cs` with:

```csharp
using Microsoft.AspNetCore.Http;

namespace Courier.SharedKernel;

public class HttpContextCurrentTenantProvider : ICurrentTenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TenantScopeOverride _tenantScopeOverride;

    public HttpContextCurrentTenantProvider(IHttpContextAccessor httpContextAccessor, TenantScopeOverride tenantScopeOverride)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantScopeOverride = tenantScopeOverride;
    }

    public Guid? OrganizationId
    {
        get
        {
            // A background tenant scope (TenantScopeFactory) wins; it is never set on a request path.
            if (_tenantScopeOverride.OrganizationId is Guid overridden)
            {
                return overridden;
            }
            var claim = _httpContextAccessor.HttpContext?.User.FindFirst("org_id")?.Value;
            return Guid.TryParse(claim, out var id) ? id : null;
        }
    }
}
```

In `src/Api/Program.cs`, replace the line `builder.Services.AddScoped<ICurrentTenantProvider, HttpContextCurrentTenantProvider>();` with:

```csharp
builder.Services.AddScoped<TenantScopeOverride>();
builder.Services.AddScoped<ICurrentTenantProvider, HttpContextCurrentTenantProvider>();
builder.Services.AddSingleton<ITenantScopeFactory, TenantScopeFactory>();
```

Then search the whole repository for other constructions of `HttpContextCurrentTenantProvider` (`grep -rn "new HttpContextCurrentTenantProvider" --include=*.cs src tests`) and pass `new TenantScopeOverride()` as the new second argument at each one found.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SharedKernel.Tests --filter "FullyQualifiedName~TenantScopeFactoryTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Build and run the API smoke**

Run: `dotnet build Courier.slnx` — Expected: 0 errors.
Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~AuthEndpointsTests|FullyQualifiedName~InternationalShipmentsEndpointsTests"` — Expected: PASS (request-path tenant resolution unchanged).

- [ ] **Step 6: Commit**

```bash
git add src/SharedKernel/TenantScopeOverride.cs src/SharedKernel/ITenantScopeFactory.cs src/SharedKernel/HttpContextCurrentTenantProvider.cs src/Api/Program.cs tests/SharedKernel.Tests/
git commit -m "feat(sharedkernel): add background tenant scope for tenant-filtered jobs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: International statuses, event types, public tracking and scan-info direction

**Files:**
- Modify: `src/Modules/Shipments/Domain/ShipmentStatus.cs`
- Modify: `src/Modules/Shipments/Domain/ShipmentEventType.cs`
- Modify: `src/Modules/Shipments/Application/PublicTrackingStatusMapper.cs`
- Modify: `src/Modules/Shipments/Application/IShipmentTransitionService.cs:11-26` (`ShipmentScanInfo`)
- Modify: `src/Modules/Shipments/Application/ShipmentService.Reads.cs:90-93` (`ToScanInfo`)
- Modify: `tests/International.Tests/InternationalShipmentServiceTests.cs:87-96` (`FixedShipmentTransitionService`)
- Test: `tests/Shipments.Tests/PublicTrackingStatusMapperTests.cs`, `tests/Shipments.Tests/ShipmentServiceInternationalTests.cs`

**Interfaces:**
- Produces: `ShipmentStatus.HandedToCarrier`, `.InInternationalTransit`, `.DeliveredAbroad`, `.CustomsHold`, `.InternationalException`, `.CustomsClearance`, `.ReceivedFromCarrier` and `ShipmentEventType` values with the same names.
- Produces: `ShipmentScanInfo.Direction` (`ShipmentDirection`, trailing optional parameter, default `ShipmentDirection.Domestic`).
- Produces: `FixedShipmentTransitionService.Direction` (settable, default `ShipmentDirection.Outbound`) in International.Tests.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Shipments.Tests/PublicTrackingStatusMapperTests.cs` (inside the existing test class):

```csharp
    [Theory]
    [InlineData(ShipmentStatus.HandedToCarrier, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.InInternationalTransit, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.CustomsClearance, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.ReceivedFromCarrier, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.CustomsHold, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.InternationalException, PublicTrackingStatus.InTransit)]
    [InlineData(ShipmentStatus.DeliveredAbroad, PublicTrackingStatus.Delivered)]
    public void Map_InternationalCarrierStatuses_HidesExceptionsBehindInTransit(ShipmentStatus status, PublicTrackingStatus expected)
    {
        Assert.Equal(expected, PublicTrackingStatusMapper.Map(status));
    }
```

Append to `tests/Shipments.Tests/ShipmentServiceInternationalTests.cs` (inside `ShipmentServiceInternationalTests`):

```csharp
    [Fact]
    public async Task GetScanInfoAsync_ReturnsShipmentDirection()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var created = await service.CreateShipmentShellAsync(
            OutboundRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), serviceTypeId), ActingUser(Guid.NewGuid()));

        var info = await service.GetScanInfoAsync(created.Id);

        Assert.Equal(ShipmentDirection.Outbound, info!.Direction);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Shipments.Tests --filter "FullyQualifiedName~PublicTrackingStatusMapperTests|FullyQualifiedName~GetScanInfoAsync_ReturnsShipmentDirection"`
Expected: FAIL — compile errors (`ShipmentStatus.HandedToCarrier` and `ShipmentScanInfo.Direction` do not exist).

- [ ] **Step 3: Implement**

In `ShipmentStatus.cs`, replace the final `CustomsPrepared` line of the enum with:

```csharp
    CustomsPrepared,
    // I4 — international carrier lifecycle. Append-only (persisted ordinals).
    HandedToCarrier,
    InInternationalTransit,
    DeliveredAbroad,
    CustomsHold,
    InternationalException,
    CustomsClearance,
    ReceivedFromCarrier
```

In `ShipmentEventType.cs`, replace the final `CustomsPrepared` line with the same block (same names, same comment).

In `PublicTrackingStatusMapper.cs`, replace the `AtOriginBranch or ... CustomsPrepared => PublicTrackingStatus.InTransit,` arm and the `Delivered` arm with:

```csharp
        ShipmentStatus.AtOriginBranch or ShipmentStatus.Sorted or ShipmentStatus.InTransit or ShipmentStatus.CustomsPrepared
            or ShipmentStatus.HandedToCarrier or ShipmentStatus.InInternationalTransit or ShipmentStatus.CustomsClearance
            or ShipmentStatus.ReceivedFromCarrier
            // Exceptions are internal (I4 spec §3.7) — the public sees the parcel as still moving.
            or ShipmentStatus.CustomsHold or ShipmentStatus.InternationalException
            => PublicTrackingStatus.InTransit,
```

and

```csharp
        ShipmentStatus.Delivered or ShipmentStatus.DeliveredAbroad => PublicTrackingStatus.Delivered,
```

In `IShipmentTransitionService.cs`, change the end of the `ShipmentScanInfo` record from `decimal CodAmount = 0m);` to:

```csharp
    decimal CodAmount = 0m,
    // I4 — trailing optional for the same reason as CodAmount: existing positional test call
    // sites keep compiling. Read by ManifestService.ScanAsync (inbound ReceivedFromCarrier sort)
    // and International.Application (outbound/inbound carrier operations).
    ShipmentDirection Direction = ShipmentDirection.Domestic);
```

In `ShipmentService.Reads.cs`, open `ToScanInfo` and append `shipment.Direction` as the last constructor argument (after the existing `CodAmount` argument).

In `tests/International.Tests/InternationalShipmentServiceTests.cs`, add to `FixedShipmentTransitionService` a property and pass it through:

```csharp
    public ShipmentDirection Direction { get; set; } = ShipmentDirection.Outbound;
```

and change its `GetScanInfoAsync` body to:

```csharp
        Task.FromResult(Exists ? new ShipmentScanInfo(shipmentId, Guid.NewGuid(), "AWB-INTL-1", Status, CurrentBranchId ?? OriginBranchId, OriginBranchId, DestinationBranchId, null, 0m, Direction) : null);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Shipments.Tests` — Expected: PASS (all, including `Map_EveryDefinedShipmentStatus_HasAMapping`).
Run: `dotnet test tests/International.Tests` — Expected: PASS.

- [ ] **Step 5: Build**

Run: `dotnet build Courier.slnx` — Expected: 0 errors. (No migration: enums are stored as integers.)

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Shipments/ tests/Shipments.Tests/ tests/International.Tests/InternationalShipmentServiceTests.cs
git commit -m "feat(shipments): add international carrier statuses, public mapping and scan-info direction

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Transition rules, carrier-driven transitions and staff overrides

**Files:**
- Create: `src/Modules/Shipments/Domain/InternationalTransitionRules.cs`
- Modify: `src/Modules/Shipments/Application/IShipmentInternationalGateway.cs`
- Create: `src/Modules/Shipments/Application/ShipmentService.InternationalLifecycle.cs`
- Modify: `tests/International.Tests/InternationalShipmentServiceTests.cs` (`RecordingGateway`)
- Test: `tests/Shipments.Tests/InternationalTransitionRulesTests.cs`, `tests/Shipments.Tests/ShipmentServiceInternationalTests.cs`

**Interfaces:**
- Produces (Shipments.Domain):
  - `public enum InternationalTransitionTrigger { Carrier, StaffOverride }`
  - `public static class InternationalTransitionRules { static bool IsAllowed(ShipmentDirection direction, ShipmentStatus from, ShipmentStatus to, InternationalTransitionTrigger trigger); static ShipmentEventType EventTypeFor(ShipmentStatus to); }`
- Produces (Shipments.Application, on `IShipmentInternationalGateway`):
  - `public record CarrierEventReference(Guid CarrierAccountId, string CarrierAwb, string CarrierEventId, string CarrierStatusCode, string? Location);`
  - `public enum CarrierStatusApplyResult { Applied, AlreadyAtStatus, NotFound }`
  - `Task<CarrierStatusApplyResult> ApplyCarrierStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, DateTimeOffset occurredAt, CarrierEventReference reference, CancellationToken cancellationToken = default);` — throws `ShipmentInvalidTransitionException` for a disallowed transition, `ShipmentValidationException` for a non-international shipment.
  - `Task<ShipmentResponse?> OverrideInternationalStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, string reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);` — null when not found.
- Produces (private, used by Task 5): `StageInternationalTransition(...)`, `SaveInternationalTransitionAsync(...)` in `ShipmentService.InternationalLifecycle.cs`.

- [ ] **Step 1: Write the failing rules tests**

`tests/Shipments.Tests/InternationalTransitionRulesTests.cs`:

```csharp
using Courier.Shipments.Domain;
using Xunit;

namespace Courier.Shipments.Tests;

public class InternationalTransitionRulesTests
{
    [Theory]
    [InlineData(ShipmentStatus.HandedToCarrier, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentStatus.InInternationalTransit, ShipmentStatus.DeliveredAbroad)]
    [InlineData(ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsHold)]
    [InlineData(ShipmentStatus.InInternationalTransit, ShipmentStatus.InternationalException)]
    [InlineData(ShipmentStatus.CustomsHold, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentStatus.CustomsHold, ShipmentStatus.InternationalException)]
    [InlineData(ShipmentStatus.InternationalException, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentStatus.InternationalException, ShipmentStatus.DeliveredAbroad)]
    public void Outbound_CarrierTransitions_AreAllowed(ShipmentStatus from, ShipmentStatus to)
    {
        Assert.True(InternationalTransitionRules.IsAllowed(ShipmentDirection.Outbound, from, to, InternationalTransitionTrigger.Carrier));
    }

    [Theory]
    [InlineData(ShipmentStatus.Created, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsClearance)]
    [InlineData(ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsHold)]
    [InlineData(ShipmentStatus.InInternationalTransit, ShipmentStatus.InternationalException)]
    [InlineData(ShipmentStatus.CustomsClearance, ShipmentStatus.CustomsHold)]
    [InlineData(ShipmentStatus.CustomsHold, ShipmentStatus.CustomsClearance)]
    [InlineData(ShipmentStatus.CustomsHold, ShipmentStatus.InternationalException)]
    [InlineData(ShipmentStatus.InternationalException, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentStatus.InternationalException, ShipmentStatus.CustomsClearance)]
    public void Inbound_CarrierTransitions_AreAllowed(ShipmentStatus from, ShipmentStatus to)
    {
        Assert.True(InternationalTransitionRules.IsAllowed(ShipmentDirection.Inbound, from, to, InternationalTransitionTrigger.Carrier));
    }

    [Theory]
    // Backwards / skipping / wrong-direction moves the carrier must never make.
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.DeliveredAbroad, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.HandedToCarrier, ShipmentStatus.DeliveredAbroad)]
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.CustomsPrepared, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsClearance)]
    [InlineData(ShipmentDirection.Inbound, ShipmentStatus.CustomsClearance, ShipmentStatus.InInternationalTransit)]
    [InlineData(ShipmentDirection.Inbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.DeliveredAbroad)]
    [InlineData(ShipmentDirection.Inbound, ShipmentStatus.Created, ShipmentStatus.CustomsClearance)]
    [InlineData(ShipmentDirection.Inbound, ShipmentStatus.CustomsClearance, ShipmentStatus.ReceivedFromCarrier)]
    [InlineData(ShipmentDirection.Domestic, ShipmentStatus.Created, ShipmentStatus.InInternationalTransit)]
    public void DisallowedCarrierTransitions_AreRejected(ShipmentDirection direction, ShipmentStatus from, ShipmentStatus to)
    {
        Assert.False(InternationalTransitionRules.IsAllowed(direction, from, to, InternationalTransitionTrigger.Carrier));
    }

    [Theory]
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.CustomsHold, ShipmentStatus.InInternationalTransit, true)]
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.InternationalException, ShipmentStatus.DeliveredAbroad, true)]
    [InlineData(ShipmentDirection.Inbound, ShipmentStatus.CustomsHold, ShipmentStatus.CustomsClearance, true)]
    [InlineData(ShipmentDirection.Inbound, ShipmentStatus.InternationalException, ShipmentStatus.InInternationalTransit, true)]
    // Overrides are only exits from the two exception statuses.
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsHold, false)]
    [InlineData(ShipmentDirection.Outbound, ShipmentStatus.HandedToCarrier, ShipmentStatus.InInternationalTransit, false)]
    [InlineData(ShipmentDirection.Inbound, ShipmentStatus.Created, ShipmentStatus.InInternationalTransit, false)]
    public void StaffOverride_OnlyExitsExceptionStatuses(ShipmentDirection direction, ShipmentStatus from, ShipmentStatus to, bool expected)
    {
        Assert.Equal(expected, InternationalTransitionRules.IsAllowed(direction, from, to, InternationalTransitionTrigger.StaffOverride));
    }

    [Fact]
    public void EventTypeFor_MapsEachCarrierStatusToItsSameNamedEventType()
    {
        Assert.Equal(ShipmentEventType.CustomsHold, InternationalTransitionRules.EventTypeFor(ShipmentStatus.CustomsHold));
        Assert.Equal(ShipmentEventType.DeliveredAbroad, InternationalTransitionRules.EventTypeFor(ShipmentStatus.DeliveredAbroad));
        Assert.Throws<ArgumentOutOfRangeException>(() => InternationalTransitionRules.EventTypeFor(ShipmentStatus.Delivered));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Shipments.Tests --filter "FullyQualifiedName~InternationalTransitionRulesTests"`
Expected: FAIL — `InternationalTransitionRules` does not exist.

- [ ] **Step 3: Implement the rules**

`src/Modules/Shipments/Domain/InternationalTransitionRules.cs`:

```csharp
namespace Courier.Shipments.Domain;

public enum InternationalTransitionTrigger
{
    Carrier,
    StaffOverride
}

// The I4 spec §3.2 (outbound) / §3.3 (inbound) transition tables — the single place these moves
// are defined. Handoff (CustomsPrepared -> HandedToCarrier) and receive (-> ReceivedFromCarrier)
// are scan operations with their own gateway methods, so they are deliberately not in this table.
public static class InternationalTransitionRules
{
    private sealed record Rule(ShipmentDirection Direction, ShipmentStatus From, ShipmentStatus To, bool StaffOverridable);

    private static readonly Rule[] Rules =
    {
        // Outbound
        new(ShipmentDirection.Outbound, ShipmentStatus.HandedToCarrier, ShipmentStatus.InInternationalTransit, false),
        new(ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.DeliveredAbroad, false),
        new(ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsHold, false),
        new(ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.InternationalException, false),
        new(ShipmentDirection.Outbound, ShipmentStatus.CustomsHold, ShipmentStatus.InInternationalTransit, true),
        new(ShipmentDirection.Outbound, ShipmentStatus.CustomsHold, ShipmentStatus.InternationalException, true),
        new(ShipmentDirection.Outbound, ShipmentStatus.InternationalException, ShipmentStatus.InInternationalTransit, true),
        new(ShipmentDirection.Outbound, ShipmentStatus.InternationalException, ShipmentStatus.DeliveredAbroad, true),
        // Inbound
        new(ShipmentDirection.Inbound, ShipmentStatus.Created, ShipmentStatus.InInternationalTransit, false),
        new(ShipmentDirection.Inbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsClearance, false),
        new(ShipmentDirection.Inbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsHold, false),
        new(ShipmentDirection.Inbound, ShipmentStatus.InInternationalTransit, ShipmentStatus.InternationalException, false),
        new(ShipmentDirection.Inbound, ShipmentStatus.CustomsClearance, ShipmentStatus.CustomsHold, false),
        new(ShipmentDirection.Inbound, ShipmentStatus.CustomsHold, ShipmentStatus.CustomsClearance, true),
        new(ShipmentDirection.Inbound, ShipmentStatus.CustomsHold, ShipmentStatus.InternationalException, true),
        new(ShipmentDirection.Inbound, ShipmentStatus.InternationalException, ShipmentStatus.InInternationalTransit, true),
        new(ShipmentDirection.Inbound, ShipmentStatus.InternationalException, ShipmentStatus.CustomsClearance, true),
    };

    // Carriers may make every listed move; staff overrides only the exits from CustomsHold and
    // InternationalException (the rows marked StaffOverridable).
    public static bool IsAllowed(ShipmentDirection direction, ShipmentStatus from, ShipmentStatus to, InternationalTransitionTrigger trigger) =>
        Rules.Any(r => r.Direction == direction && r.From == from && r.To == to
            && (trigger == InternationalTransitionTrigger.Carrier || r.StaffOverridable));

    public static ShipmentEventType EventTypeFor(ShipmentStatus to) => to switch
    {
        ShipmentStatus.HandedToCarrier => ShipmentEventType.HandedToCarrier,
        ShipmentStatus.InInternationalTransit => ShipmentEventType.InInternationalTransit,
        ShipmentStatus.DeliveredAbroad => ShipmentEventType.DeliveredAbroad,
        ShipmentStatus.CustomsHold => ShipmentEventType.CustomsHold,
        ShipmentStatus.InternationalException => ShipmentEventType.InternationalException,
        ShipmentStatus.CustomsClearance => ShipmentEventType.CustomsClearance,
        ShipmentStatus.ReceivedFromCarrier => ShipmentEventType.ReceivedFromCarrier,
        _ => throw new ArgumentOutOfRangeException(nameof(to), to, "Not an international carrier-lifecycle status.")
    };
}
```

Run: `dotnet test tests/Shipments.Tests --filter "FullyQualifiedName~InternationalTransitionRulesTests"` — Expected: PASS.

- [ ] **Step 4: Write the failing gateway tests**

Append inside `ShipmentServiceInternationalTests` in `tests/Shipments.Tests/ShipmentServiceInternationalTests.cs`:

```csharp
    private static CreateInternationalShipmentShellRequest InboundRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        merchantId, ShipmentDirection.Inbound, originBranchId, destinationBranchId, serviceTypeId, 1m, null, null, null,
        "Local Recipient", "9811111111", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "Ward 1 Line",
        null, null, null, null,
        "Foreign Sender", "9998887776", null, null, null, null, "221B Baker Street", "London", null, "NW16XE", "GB",
        5000m, "NPR", new List<CreateShipmentItemRequest> { new("Sample goods", 1, 5000m) });

    // Creates an international shipment through the real gateway, then positions it at `status`.
    private static async Task<Guid> CreateAtStatusAsync(
        TestDbContext dbContext, ShipmentService service, Guid serviceTypeId, ShipmentDirection direction, ShipmentStatus status,
        Guid? originBranchId = null, Guid? destinationBranchId = null, Guid? currentBranchId = null)
    {
        var origin = originBranchId ?? Guid.NewGuid();
        var destination = destinationBranchId ?? Guid.NewGuid();
        var request = direction == ShipmentDirection.Outbound
            ? OutboundRequest(Guid.NewGuid(), origin, destination, serviceTypeId)
            : InboundRequest(Guid.NewGuid(), origin, destination, serviceTypeId);
        var created = await service.CreateShipmentShellAsync(request, ActingUser(Guid.NewGuid()));
        var shipment = await dbContext.Set<Shipment>().SingleAsync(s => s.Id == created.Id);
        shipment.CurrentStatus = status;
        shipment.CurrentBranchId = currentBranchId;
        await dbContext.SaveChangesAsync();
        return created.Id;
    }

    private static CarrierEventReference CarrierRef(string code = "IN_TRANSIT") =>
        new(Guid.NewGuid(), "CARRIER-AWB-1", $"evt-{Guid.NewGuid():N}", code, "Frankfurt");

    [Fact]
    public async Task ApplyCarrierStatusAsync_AllowedTransition_AppliesWithCarrierTimestampAndNoUser()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.HandedToCarrier);
        var occurredAt = DateTimeOffset.UtcNow.AddHours(-3);

        var result = await service.ApplyCarrierStatusAsync(shipmentId, ShipmentStatus.InInternationalTransit, occurredAt, CarrierRef());

        Assert.Equal(CarrierStatusApplyResult.Applied, result);
        var shipment = await dbContext.Set<Shipment>().SingleAsync(s => s.Id == shipmentId);
        Assert.Equal(ShipmentStatus.InInternationalTransit, shipment.CurrentStatus);
        var evt = await dbContext.Set<ShipmentEvent>().SingleAsync(e => e.ShipmentId == shipmentId && e.EventType == ShipmentEventType.InInternationalTransit);
        Assert.Null(evt.UserId);
        Assert.Equal(occurredAt, evt.Timestamp);
        Assert.Equal(ShipmentStatus.HandedToCarrier, evt.PreviousStatus);
        Assert.Contains("\"source\":\"Carrier\"", evt.Metadata);
        Assert.Contains("CARRIER-AWB-1", evt.Metadata);
    }

    [Fact]
    public async Task ApplyCarrierStatusAsync_AlreadyAtTarget_IsNoOp()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit);
        var eventsBefore = await dbContext.Set<ShipmentEvent>().CountAsync(e => e.ShipmentId == shipmentId);

        var result = await service.ApplyCarrierStatusAsync(shipmentId, ShipmentStatus.InInternationalTransit, DateTimeOffset.UtcNow, CarrierRef());

        Assert.Equal(CarrierStatusApplyResult.AlreadyAtStatus, result);
        Assert.Equal(eventsBefore, await dbContext.Set<ShipmentEvent>().CountAsync(e => e.ShipmentId == shipmentId));
    }

    [Fact]
    public async Task ApplyCarrierStatusAsync_BackwardsMove_ThrowsInvalidTransition()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.DeliveredAbroad);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() =>
            service.ApplyCarrierStatusAsync(shipmentId, ShipmentStatus.InInternationalTransit, DateTimeOffset.UtcNow, CarrierRef()));
    }

    [Fact]
    public async Task ApplyCarrierStatusAsync_UnknownShipment_ReturnsNotFound()
    {
        var (dbContext, organizationId, _) = await SeedAsync();
        await using var __ = dbContext;
        var service = CreateService(dbContext, organizationId);

        var result = await service.ApplyCarrierStatusAsync(Guid.NewGuid(), ShipmentStatus.InInternationalTransit, DateTimeOffset.UtcNow, CarrierRef());

        Assert.Equal(CarrierStatusApplyResult.NotFound, result);
    }

    [Fact]
    public async Task OverrideInternationalStatusAsync_FromCustomsHold_AppliesWithStaffUserAndReason()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.CustomsHold);
        var staffUserId = Guid.NewGuid();

        var result = await service.OverrideInternationalStatusAsync(shipmentId, ShipmentStatus.InInternationalTransit, "  Released by customs officer  ", ActingUser(staffUserId));

        Assert.Equal(ShipmentStatus.InInternationalTransit, result!.CurrentStatus);
        var evt = await dbContext.Set<ShipmentEvent>().SingleAsync(e => e.ShipmentId == shipmentId && e.EventType == ShipmentEventType.InInternationalTransit);
        Assert.Equal(staffUserId, evt.UserId);
        Assert.Equal("Released by customs officer", evt.Notes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OverrideInternationalStatusAsync_BlankReason_ThrowsValidation(string reason)
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.CustomsHold);

        var ex = await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            service.OverrideInternationalStatusAsync(shipmentId, ShipmentStatus.InInternationalTransit, reason, ActingUser(Guid.NewGuid())));
        Assert.IsNotType<ShipmentInvalidTransitionException>(ex);
    }

    [Fact]
    public async Task OverrideInternationalStatusAsync_NotAnExceptionExit_ThrowsInvalidTransition()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() =>
            service.OverrideInternationalStatusAsync(shipmentId, ShipmentStatus.DeliveredAbroad, "Carrier confirmed by phone", ActingUser(Guid.NewGuid())));
    }
```

- [ ] **Step 5: Run to verify failure**

Run: `dotnet test tests/Shipments.Tests --filter "FullyQualifiedName~ShipmentServiceInternationalTests"`
Expected: FAIL — `ApplyCarrierStatusAsync`, `CarrierEventReference` etc. do not exist.

- [ ] **Step 6: Implement the gateway additions**

Append to `src/Modules/Shipments/Application/IShipmentInternationalGateway.cs` (after the `CreateInternationalShipmentShellRequest` record, before the interface):

```csharp
// Identifies the carrier tracking event that drove a transition; serialized into the
// ShipmentEvent's Metadata so the timeline can show which carrier reported it.
public record CarrierEventReference(Guid CarrierAccountId, string CarrierAwb, string CarrierEventId, string CarrierStatusCode, string? Location);

public enum CarrierStatusApplyResult
{
    Applied,
    AlreadyAtStatus,
    NotFound
}
```

and add to the interface body:

```csharp
    // I4 — carrier-driven (no user). Only InternationalTransitionRules' Carrier moves are
    // allowed; throws ShipmentInvalidTransitionException otherwise, ShipmentValidationException
    // for a non-international shipment.
    Task<CarrierStatusApplyResult> ApplyCarrierStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, DateTimeOffset occurredAt, CarrierEventReference reference, CancellationToken cancellationToken = default);

    // I4 — staff exit from CustomsHold/InternationalException, with a required reason.
    Task<ShipmentResponse?> OverrideInternationalStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, string reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
```

Create `src/Modules/Shipments/Application/ShipmentService.InternationalLifecycle.cs`:

```csharp
using System.Security.Claims;
using System.Text.Json;
using Courier.Audit.Application;
using Courier.SharedKernel;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Shipments.Application;

// I4 international carrier lifecycle. Unlike TransitionAsync (one fixed expectedFrom, always a
// user, always "now"), these moves are validated against InternationalTransitionRules, may have
// no user (carrier-driven), carry the carrier's own timestamp, and a scan may record two events in
// one save (inbound receive at the destination branch). Same xmin concurrency handling.
public partial class ShipmentService
{
    private const int MaxOverrideReasonLength = 2000; // ShipmentEvent.Notes column limit

    public async Task<CarrierStatusApplyResult> ApplyCarrierStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, DateTimeOffset occurredAt, CarrierEventReference reference, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return CarrierStatusApplyResult.NotFound;
        }
        if (shipment.Scope != ShipmentScope.International)
        {
            throw new ShipmentValidationException("Carrier status updates apply only to international shipments.");
        }
        if (shipment.CurrentStatus == targetStatus)
        {
            return CarrierStatusApplyResult.AlreadyAtStatus;
        }
        if (!InternationalTransitionRules.IsAllowed(shipment.Direction, shipment.CurrentStatus, targetStatus, InternationalTransitionTrigger.Carrier))
        {
            throw new ShipmentInvalidTransitionException(shipmentId, shipment.CurrentStatus, targetStatus);
        }

        var previousStatus = shipment.CurrentStatus;
        var metadata = JsonSerializer.Serialize(new
        {
            source = "Carrier",
            carrierAccountId = reference.CarrierAccountId,
            carrierAwb = reference.CarrierAwb,
            carrierEventId = reference.CarrierEventId,
            carrierStatusCode = reference.CarrierStatusCode,
            location = reference.Location
        });
        StageInternationalTransition(shipment, targetStatus, InternationalTransitionRules.EventTypeFor(targetStatus),
            userId: null, occurredAt, notes: null, metadata, _ => { });
        await SaveInternationalTransitionAsync(shipmentId, previousStatus, targetStatus, cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, null, "shipment.carrier_status_applied", "Shipment", shipmentId.ToString(),
            Before: previousStatus.ToString(), After: targetStatus.ToString()), cancellationToken);
        return CarrierStatusApplyResult.Applied;
    }

    public async Task<ShipmentResponse?> OverrideInternationalStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, string reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        var trimmedReason = reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length == 0)
        {
            throw new ShipmentValidationException("A reason is required for a status override.");
        }
        if (trimmedReason.Length > MaxOverrideReasonLength)
        {
            throw new ShipmentValidationException($"Reason must be at most {MaxOverrideReasonLength} characters.");
        }

        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        if (shipment.Scope != ShipmentScope.International)
        {
            throw new ShipmentValidationException("Status overrides apply only to international shipments.");
        }
        if (!InternationalTransitionRules.IsAllowed(shipment.Direction, shipment.CurrentStatus, targetStatus, InternationalTransitionTrigger.StaffOverride))
        {
            throw new ShipmentInvalidTransitionException(shipmentId, shipment.CurrentStatus, targetStatus);
        }

        var previousStatus = shipment.CurrentStatus;
        StageInternationalTransition(shipment, targetStatus, InternationalTransitionRules.EventTypeFor(targetStatus),
            actingUser.GetUserId(), DateTimeOffset.UtcNow, trimmedReason, JsonSerializer.Serialize(new { source = "StaffOverride" }), _ => { });
        await SaveInternationalTransitionAsync(shipmentId, previousStatus, targetStatus, cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "shipment.international_status_overridden", "Shipment", shipmentId.ToString(),
            Before: previousStatus.ToString(), After: JsonSerializer.Serialize(new { status = targetStatus.ToString(), reason = trimmedReason })), cancellationToken);
        return await LoadResponseAsync(shipmentId, cancellationToken);
    }

    // Mutates the tracked shipment and adds its immutable ShipmentEvent; nothing is saved here so
    // a scan can stage two moves (receive + arrive) into one SaveChangesAsync.
    private void StageInternationalTransition(
        Shipment shipment, ShipmentStatus to, ShipmentEventType eventType, Guid? userId, DateTimeOffset timestamp,
        string? notes, string? metadata, Action<Shipment> applyCustody, Guid? eventBranchIdOverride = null)
    {
        var previousStatus = shipment.CurrentStatus;
        var eventBranchId = eventBranchIdOverride ?? shipment.CurrentBranchId;
        shipment.CurrentStatus = to;
        applyCustody(shipment);

        _dbContext.Set<ShipmentEvent>().Add(new ShipmentEvent
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipment.Id,
            EventType = eventType,
            PreviousStatus = previousStatus,
            NewStatus = to,
            BranchId = eventBranchId,
            UserId = userId,
            Timestamp = timestamp,
            Notes = notes,
            Metadata = metadata
        });
    }

    private async Task SaveInternationalTransitionAsync(Guid shipmentId, ShipmentStatus from, ShipmentStatus to, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Same race handling as TransitionAsync: another request moved the shipment first.
            throw new ShipmentInvalidTransitionException(shipmentId, from, to);
        }
    }
}
```

In `tests/International.Tests/InternationalShipmentServiceTests.cs`, add to `RecordingGateway` (these stubs are replaced with recording versions in Task 5 and Task 13):

```csharp
    public Task<CarrierStatusApplyResult> ApplyCarrierStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, DateTimeOffset occurredAt, CarrierEventReference reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> OverrideInternationalStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, string reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Shipments.Tests` — Expected: PASS.
Run: `dotnet test tests/International.Tests` — Expected: PASS.
Run: `dotnet build Courier.slnx` — Expected: 0 errors (fix any other `IShipmentInternationalGateway` implementer the build reports by adding the same `NotSupportedException` stubs).

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Shipments/ tests/Shipments.Tests/ tests/International.Tests/
git commit -m "feat(shipments): add international transition rules, carrier-driven transitions and staff overrides

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Handoff and receive scans, inbound sort

**Files:**
- Modify: `src/Modules/Shipments/Application/IShipmentInternationalGateway.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentService.InternationalLifecycle.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentService.Transitions.cs:115-117` (`SortAsync`)
- Modify: `src/Modules/Manifests/Application/ManifestService.Scanning.cs:41-47`
- Modify: `tests/International.Tests/InternationalShipmentServiceTests.cs` (`RecordingGateway`)
- Test: `tests/Shipments.Tests/ShipmentServiceInternationalTests.cs`, `tests/Manifests.Tests/ManifestServiceScanningTests.cs`

**Interfaces:**
- Consumes: Task 4's `StageInternationalTransition`, `SaveInternationalTransitionAsync`.
- Produces (on `IShipmentInternationalGateway`):
  - `public record CarrierHandoffReference(Guid CarrierAccountId, string CarrierAwb);`
  - `Task<ShipmentResponse?> HandToCarrierAsync(Guid shipmentId, CarrierHandoffReference reference, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);` — requires `international.handoff` branch access to `CurrentBranchId` (else `ShipmentValidationException`), outbound, `CustomsPrepared`; clears branch and custodians.
  - `Task<ShipmentResponse?> ReceiveFromCarrierAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);` — requires `international.receive` branch access to `OriginBranchId`, inbound, from `InInternationalTransit` or `CustomsClearance`; sets branch = origin, custodian = scanning user; when origin == destination also moves to `AtDestinationBranch` in the same save.

- [ ] **Step 1: Write the failing Shipments tests**

Append inside `ShipmentServiceInternationalTests`:

```csharp
    [Fact]
    public async Task HandToCarrierAsync_FromCustomsPrepared_ClearsCustodyAndRecordsCarrier()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var branchId = Guid.NewGuid();
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.CustomsPrepared, branchId, branchId, branchId);
        var accountId = Guid.NewGuid();

        var result = await service.HandToCarrierAsync(shipmentId, new CarrierHandoffReference(accountId, "DHL123"), ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.HandedToCarrier, result!.CurrentStatus);
        Assert.Null(result.CurrentBranchId);
        Assert.Null(result.CurrentCustodianUserId);
        var evt = await dbContext.Set<ShipmentEvent>().SingleAsync(e => e.ShipmentId == shipmentId && e.EventType == ShipmentEventType.HandedToCarrier);
        Assert.Equal(branchId, evt.BranchId);
        Assert.Contains("DHL123", evt.Metadata);
        Assert.Contains(accountId.ToString(), evt.Metadata);
    }

    [Fact]
    public async Task HandToCarrierAsync_WrongState_ThrowsInvalidTransition()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var branchId = Guid.NewGuid();
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.Sorted, branchId, branchId, branchId);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() =>
            service.HandToCarrierAsync(shipmentId, new CarrierHandoffReference(Guid.NewGuid(), "DHL123"), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task HandToCarrierAsync_InboundShipment_ThrowsValidation()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var branchId = Guid.NewGuid();
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Inbound, ShipmentStatus.CustomsPrepared, branchId, branchId, branchId);

        var ex = await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            service.HandToCarrierAsync(shipmentId, new CarrierHandoffReference(Guid.NewGuid(), "DHL123"), ActingUser(Guid.NewGuid())));
        Assert.IsNotType<ShipmentInvalidTransitionException>(ex);
    }

    [Fact]
    public async Task HandToCarrierAsync_UserWithoutAccessToCurrentBranch_ThrowsValidation()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var branchId = Guid.NewGuid();
        var shipmentId = await CreateAtStatusAsync(dbContext, CreateService(dbContext, organizationId), serviceTypeId,
            ShipmentDirection.Outbound, ShipmentStatus.CustomsPrepared, branchId, branchId, branchId);
        var restricted = CreateService(dbContext, organizationId, branchAuthorizationChecker: new SpecificBranchesAuthorizationChecker(Guid.NewGuid()));

        await Assert.ThrowsAsync<ShipmentValidationException>(() =>
            restricted.HandToCarrierAsync(shipmentId, new CarrierHandoffReference(Guid.NewGuid(), "DHL123"), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ReceiveFromCarrierAsync_OriginDiffersFromDestination_StopsAtReceivedFromCarrier()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var gatewayBranchId = Guid.NewGuid();
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Inbound, ShipmentStatus.CustomsClearance, gatewayBranchId, Guid.NewGuid(), null);
        var userId = Guid.NewGuid();

        var result = await service.ReceiveFromCarrierAsync(shipmentId, ActingUser(userId));

        Assert.Equal(ShipmentStatus.ReceivedFromCarrier, result!.CurrentStatus);
        Assert.Equal(gatewayBranchId, result.CurrentBranchId);
        Assert.Equal(userId, result.CurrentCustodianUserId);
        var evt = await dbContext.Set<ShipmentEvent>().SingleAsync(e => e.ShipmentId == shipmentId && e.EventType == ShipmentEventType.ReceivedFromCarrier);
        Assert.Equal(gatewayBranchId, evt.BranchId);
    }

    [Fact]
    public async Task ReceiveFromCarrierAsync_OriginEqualsDestination_RecordsTwoEventsAndArrives()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var branchId = Guid.NewGuid();
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Inbound, ShipmentStatus.InInternationalTransit, branchId, branchId, null);

        var result = await service.ReceiveFromCarrierAsync(shipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.AtDestinationBranch, result!.CurrentStatus);
        var events = await dbContext.Set<ShipmentEvent>().Where(e => e.ShipmentId == shipmentId).OrderBy(e => e.Timestamp).ToListAsync();
        Assert.Contains(events, e => e.EventType == ShipmentEventType.ReceivedFromCarrier && e.NewStatus == ShipmentStatus.ReceivedFromCarrier);
        Assert.Contains(events, e => e.EventType == ShipmentEventType.ReceivedAtDestination
            && e.PreviousStatus == ShipmentStatus.ReceivedFromCarrier && e.NewStatus == ShipmentStatus.AtDestinationBranch);
    }

    [Fact]
    public async Task ReceiveFromCarrierAsync_UserWithoutAccessToGatewayBranch_ThrowsValidation()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var shipmentId = await CreateAtStatusAsync(dbContext, CreateService(dbContext, organizationId), serviceTypeId,
            ShipmentDirection.Inbound, ShipmentStatus.CustomsClearance);
        var restricted = CreateService(dbContext, organizationId, branchAuthorizationChecker: new SpecificBranchesAuthorizationChecker(Guid.NewGuid()));

        await Assert.ThrowsAsync<ShipmentValidationException>(() => restricted.ReceiveFromCarrierAsync(shipmentId, ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task ReceiveFromCarrierAsync_FromCreated_ThrowsInvalidTransition()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Inbound, ShipmentStatus.Created);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => service.ReceiveFromCarrierAsync(shipmentId, ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task SortAsync_InboundReceivedFromCarrier_Sorts()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var branchId = Guid.NewGuid();
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Inbound, ShipmentStatus.ReceivedFromCarrier, branchId, Guid.NewGuid(), branchId);

        var result = await service.SortAsync(shipmentId, ActingUser(Guid.NewGuid()));

        Assert.Equal(ShipmentStatus.Sorted, result!.CurrentStatus);
    }

    [Fact]
    public async Task SortAsync_OutboundAtReceivedFromCarrier_ThrowsInvalidTransition()
    {
        var (dbContext, organizationId, serviceTypeId) = await SeedAsync();
        await using var _ = dbContext;
        var service = CreateService(dbContext, organizationId);
        var shipmentId = await CreateAtStatusAsync(dbContext, service, serviceTypeId, ShipmentDirection.Outbound, ShipmentStatus.ReceivedFromCarrier);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => service.SortAsync(shipmentId, ActingUser(Guid.NewGuid())));
    }
```

- [ ] **Step 2: Write the failing Manifests tests**

Append inside the test class in `tests/Manifests.Tests/ManifestServiceScanningTests.cs` (it already has `SeedDraftManifestAsync`, `AtOriginInfo`, `ActingUser`, and a fake exposing `ShipmentsById`/`SortCalls`):

```csharp
    [Fact]
    public async Task ScanAsync_InboundInternationalReceivedFromCarrier_IsSorted()
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, fromBranchId, toBranchId) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());
        var info = AtOriginInfo(fromBranchId, toBranchId) with { CurrentStatus = ShipmentStatus.ReceivedFromCarrier, Direction = ShipmentDirection.Inbound };
        shipments.ShipmentsById[info.ShipmentId] = info;

        var result = await service.ScanAsync(manifestId, new ScanShipmentRequest(info.Awb), ActingUser(Guid.NewGuid()));

        Assert.Equal(info.ShipmentId, result.ShipmentId);
        Assert.Contains(info.ShipmentId, shipments.SortCalls);
    }

    [Theory]
    [InlineData(ShipmentDirection.Outbound)]
    [InlineData(ShipmentDirection.Domestic)]
    public async Task ScanAsync_ReceivedFromCarrierButNotInbound_ThrowsWrongState(ShipmentDirection direction)
    {
        await using var dbContext = CreateContext();
        var (service, shipments, manifestId, fromBranchId, toBranchId) = await SeedDraftManifestAsync(dbContext, Guid.NewGuid());
        var info = AtOriginInfo(fromBranchId, toBranchId) with { CurrentStatus = ShipmentStatus.ReceivedFromCarrier, Direction = direction };
        shipments.ShipmentsById[info.ShipmentId] = info;

        var ex = await Assert.ThrowsAsync<ManifestScanRejectedException>(() =>
            service.ScanAsync(manifestId, new ScanShipmentRequest(info.Awb), ActingUser(Guid.NewGuid())));
        Assert.Equal("WRONG_STATE", ex.ReasonCode);
    }
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/Shipments.Tests --filter "FullyQualifiedName~ShipmentServiceInternationalTests"` — Expected: FAIL (compile: `HandToCarrierAsync` missing).
Run: `dotnet test tests/Manifests.Tests --filter "FullyQualifiedName~ManifestServiceScanningTests"` — Expected: FAIL (`ScanAsync_InboundInternationalReceivedFromCarrier_IsSorted` throws `WRONG_STATE`).

- [ ] **Step 4: Implement**

Add to `IShipmentInternationalGateway.cs` (next to `CarrierEventReference`):

```csharp
public record CarrierHandoffReference(Guid CarrierAccountId, string CarrierAwb);
```

and to the interface:

```csharp
    // I4 — handoff scan: CustomsPrepared -> HandedToCarrier; our custody ends.
    Task<ShipmentResponse?> HandToCarrierAsync(Guid shipmentId, CarrierHandoffReference reference, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);

    // I4 — receive scan at the inbound gateway (OriginBranchId): -> ReceivedFromCarrier; custody
    // resumes. When the gateway is also the destination, arrives (AtDestinationBranch) in the same save.
    Task<ShipmentResponse?> ReceiveFromCarrierAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
```

Add to `ShipmentService.InternationalLifecycle.cs` (inside the class):

```csharp
    public async Task<ShipmentResponse?> HandToCarrierAsync(Guid shipmentId, CarrierHandoffReference reference, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        if (shipment.Scope != ShipmentScope.International || shipment.Direction != ShipmentDirection.Outbound)
        {
            throw new ShipmentValidationException("Only outbound international shipments are handed to a carrier.");
        }
        // The operator must be at the branch physically holding the parcel (same rule as
        // TransitionToCustomsPreparedAsync / scans).
        var accessible = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "international.handoff", cancellationToken);
        if (accessible is not null && (shipment.CurrentBranchId is not Guid currentBranchId || !accessible.Contains(currentBranchId)))
        {
            throw new ShipmentValidationException("Branch is outside your branch access for permission 'international.handoff'.");
        }
        if (shipment.CurrentStatus != ShipmentStatus.CustomsPrepared)
        {
            throw new ShipmentInvalidTransitionException(shipmentId, shipment.CurrentStatus, ShipmentStatus.HandedToCarrier);
        }

        var metadata = JsonSerializer.Serialize(new { source = "Scan", carrierAccountId = reference.CarrierAccountId, carrierAwb = reference.CarrierAwb });
        StageInternationalTransition(shipment, ShipmentStatus.HandedToCarrier, ShipmentEventType.HandedToCarrier,
            actingUser.GetUserId(), DateTimeOffset.UtcNow, notes: null, metadata,
            s => { s.CurrentBranchId = null; s.CurrentCustodianUserId = null; s.CurrentCustodianRiderId = null; });
        await SaveInternationalTransitionAsync(shipmentId, ShipmentStatus.CustomsPrepared, ShipmentStatus.HandedToCarrier, cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "shipment.handed_to_carrier", "Shipment", shipmentId.ToString()), cancellationToken);
        return await LoadResponseAsync(shipmentId, cancellationToken);
    }

    public async Task<ShipmentResponse?> ReceiveFromCarrierAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new ShipmentValidationException("No active organization context.");

        var shipment = await _dbContext.Set<Shipment>().FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return null;
        }
        if (shipment.Scope != ShipmentScope.International || shipment.Direction != ShipmentDirection.Inbound)
        {
            throw new ShipmentValidationException("Only inbound international shipments are received from a carrier.");
        }
        await CheckBranchAccessAsync(actingUser, "international.receive", shipment.OriginBranchId, cancellationToken);
        var previousStatus = shipment.CurrentStatus;
        if (previousStatus is not (ShipmentStatus.InInternationalTransit or ShipmentStatus.CustomsClearance))
        {
            throw new ShipmentInvalidTransitionException(shipmentId, previousStatus, ShipmentStatus.ReceivedFromCarrier);
        }

        var userId = actingUser.GetUserId();
        var receivedAt = DateTimeOffset.UtcNow;
        StageInternationalTransition(shipment, ShipmentStatus.ReceivedFromCarrier, ShipmentEventType.ReceivedFromCarrier,
            userId, receivedAt, notes: null, JsonSerializer.Serialize(new { source = "Scan" }),
            s => { s.CurrentBranchId = s.OriginBranchId; s.CurrentCustodianUserId = userId; s.CurrentCustodianRiderId = null; },
            eventBranchIdOverride: shipment.OriginBranchId);

        var finalStatus = ShipmentStatus.ReceivedFromCarrier;
        if (shipment.OriginBranchId == shipment.DestinationBranchId)
        {
            // Gateway is also the delivery branch: the parcel has arrived — two immutable events,
            // one scan, one save (I4 spec §3.3). The second event is 1 ms later so timelines order it.
            StageInternationalTransition(shipment, ShipmentStatus.AtDestinationBranch, ShipmentEventType.ReceivedAtDestination,
                userId, receivedAt.AddMilliseconds(1), notes: null, JsonSerializer.Serialize(new { source = "Scan" }), _ => { });
            finalStatus = ShipmentStatus.AtDestinationBranch;
        }
        await SaveInternationalTransitionAsync(shipmentId, previousStatus, finalStatus, cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, userId, "shipment.received_from_carrier", "Shipment", shipmentId.ToString()), cancellationToken);
        return await LoadResponseAsync(shipmentId, cancellationToken);
    }
```

In `ShipmentService.Transitions.cs`, replace the `SortAsync` expression-bodied method (and keep its comment above) with:

```csharp
    public async Task<ShipmentResponse?> SortAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        // I4: an inbound international parcel received from the carrier at the gateway branch
        // enters the domestic flow here instead of at AtOriginBranch.
        var current = await _dbContext.Set<Shipment>().Where(s => s.Id == shipmentId)
            .Select(s => new { s.CurrentStatus, s.Direction }).FirstOrDefaultAsync(cancellationToken);
        var expectedFrom = current is { Direction: ShipmentDirection.Inbound, CurrentStatus: ShipmentStatus.ReceivedFromCarrier }
            ? ShipmentStatus.ReceivedFromCarrier
            : ShipmentStatus.AtOriginBranch;
        return await TransitionAsync(shipmentId, expectedFrom, ShipmentStatus.Sorted, ShipmentEventType.Sorted,
            _ => { }, "shipment.sorted", actingUser, cancellationToken);
    }
```

In `ManifestService.Scanning.cs`, replace the check-3/4 comment and `if` block (lines 41-47) with:

```csharp
        // Check 3 (shipment state) + check 4 (expected operation) collapse into one check here: the
        // only operation this endpoint performs IS the -> Sorted transition, from AtOriginBranch, or
        // (I4) from ReceivedFromCarrier for an inbound international parcel at its gateway branch.
        var sortable = scanInfo.CurrentStatus == ShipmentStatus.AtOriginBranch
            || (scanInfo.Direction == ShipmentDirection.Inbound && scanInfo.CurrentStatus == ShipmentStatus.ReceivedFromCarrier);
        if (!sortable)
        {
            throw new ManifestScanRejectedException("WRONG_STATE", $"Shipment must be AtOriginBranch (or ReceivedFromCarrier for inbound international) to be added to a manifest, was {scanInfo.CurrentStatus}.");
        }
```

In `tests/International.Tests/InternationalShipmentServiceTests.cs`, add to `RecordingGateway`:

```csharp
    public Task<ShipmentResponse?> HandToCarrierAsync(Guid shipmentId, CarrierHandoffReference reference, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ShipmentResponse?> ReceiveFromCarrierAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) => throw new NotSupportedException();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Shipments.Tests` — Expected: PASS.
Run: `dotnet test tests/Manifests.Tests` — Expected: PASS.
Run: `dotnet test tests/International.Tests` — Expected: PASS.
Run: `dotnet build Courier.slnx` — Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Shipments/ src/Modules/Manifests/ tests/Shipments.Tests/ tests/Manifests.Tests/ tests/International.Tests/
git commit -m "feat(shipments): add carrier handoff and receive scans, inbound sort from ReceivedFromCarrier

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 6: Carrier entities, persistence and the shared API test base

**Files:**
- Create: `src/Modules/International/Domain/CarrierAccount.cs`, `CarrierBooking.cs`, `CarrierTrackingEvent.cs`, `CarrierEnums.cs`
- Create: `src/Modules/International/Infrastructure/CarrierAccountConfiguration.cs`, `CarrierBookingConfiguration.cs`, `CarrierTrackingEventConfiguration.cs`
- Modify: `src/Persistence/AppDbContext.cs` (DbSets ~line 70, `ApplyConfiguration` ~line 152, Organization FKs ~line 324, query filters ~line 395)
- Create: `src/Persistence/Migrations/<timestamp>_AddCarrierLayer.cs` (generated)
- Create: `tests/Api.Tests/International/InternationalApiTestBase.cs`
- Modify: `tests/Api.Tests/InternationalShipmentsEndpointsTests.cs` (inherit the base, delete the moved helpers)
- Test: `tests/Api.Tests/International/CarrierPersistenceTests.cs`

**Interfaces:**
- Produces (International.Domain):
  - `enum CarrierBookingDirection { Outbound, Inbound }`, `enum CarrierBookingStatus { Pending, Booked, Failed, Cancelled }`, `enum CarrierEventSource { Webhook, Poll, Manual }`, `enum CarrierEventProcessingStatus { Pending, Processing, Applied, Ignored, Rejected, Unmatched }`
  - `CarrierAccount { Id, OrganizationId, CarrierCode, DisplayName, IsActive, EncryptedCredentials?, EncryptedWebhookSecret?, WebhookPublicId, PollingEnabled, CreatedAt, UpdatedAt }`
  - `CarrierBooking { Id, OrganizationId, InternationalLegId, CarrierAccountId, Direction, CarrierAwb?, ServiceLevel?, LabelStorageKey?, Status, FailureReason?, RawResponse?, IdempotencyKey, BookedAt?, CancelledAt?, CustodyStartedAt?, CreatedAt, UpdatedAt }`
  - `CarrierTrackingEvent { Id, OrganizationId, CarrierAccountId, CarrierBookingId?, CarrierAwb, CarrierEventId, CarrierStatusCode, Description?, Location?, OccurredAt, Source, RawPayload?, EnteredByUserId?, ReceivedAt, ProcessingStatus, ProcessingNote?, MappedShipmentStatus?, ProcessingAttempts, ClaimedAt?, ProcessedAt? }`
- Produces (Api.Tests): `public abstract class InternationalApiTestBase` with protected members `Factory`, `KathmanduProvinceId/DistrictId/LocalLevelId/WardNumber`, `RegisterOrgAsync`, `ValidBranchRequest`, `CreateBranchAsync`, `CreateMerchantAsync`, `MarkCountryServicedAsync`, `GetStandardServiceTypeIdAsync`, `OutboundBody`, `InboundBody`, `CustomsBody`, `OrgContext`, `SetUpOrgAsync`, `CreateOutboundAsync`, `CreateInboundAsync`, `SetShipmentStateAsync`, `SeedUserClientAsync`, `PrepareOutboundAtCustomsPreparedAsync`.

- [ ] **Step 1: Create the domain types**

`src/Modules/International/Domain/CarrierEnums.cs`:

```csharp
namespace Courier.International.Domain;

// International's own direction enum (not Shipments.Domain.ShipmentDirection) so this module's
// domain does not depend on another module's domain. Mapped in International.Application.
public enum CarrierBookingDirection
{
    Outbound,
    Inbound
}

public enum CarrierBookingStatus
{
    Pending,
    Booked,
    Failed,
    Cancelled
}

public enum CarrierEventSource
{
    Webhook,
    Poll,
    Manual
}

// Processing is the processor's atomic claim (plan ruling) — an event is only ever applied by
// the one run that moved it Pending -> Processing.
public enum CarrierEventProcessingStatus
{
    Pending,
    Processing,
    Applied,
    Ignored,
    Rejected,
    Unmatched
}
```

`src/Modules/International/Domain/CarrierAccount.cs`:

```csharp
namespace Courier.International.Domain;

// A tenant's configured account with one partner carrier (I4 spec §4.1). Several accounts per
// carrier are allowed. Secrets are Data-Protection ciphertext, never plaintext; the manual carrier
// has none. Never deleted once a booking references it — deactivated instead.
public class CarrierAccount
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string CarrierCode { get; set; }
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public string? EncryptedCredentials { get; set; }
    public string? EncryptedWebhookSecret { get; set; }
    // Random, unguessable, platform-unique — the only identifier in the public webhook URL.
    public required string WebhookPublicId { get; set; }
    public bool PollingEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

`src/Modules/International/Domain/CarrierBooking.cs`:

```csharp
namespace Courier.International.Domain;

// At most one live (Pending/Booked) booking per InternationalLeg; Failed/Cancelled rows stay as
// history (I4 spec §4.2). Outbound bookings are made through the carrier; inbound bookings are
// links to the foreign carrier's existing AWB (created Booked directly).
public class CarrierBooking
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid InternationalLegId { get; set; }
    public Guid CarrierAccountId { get; set; }
    public CarrierBookingDirection Direction { get; set; }
    public string? CarrierAwb { get; set; }
    public string? ServiceLevel { get; set; }
    public string? LabelStorageKey { get; set; }
    public CarrierBookingStatus Status { get; set; }
    public string? FailureReason { get; set; }
    // Valid JSON (jsonb) with known secret values masked — see CarrierPayloadSanitizer.
    public string? RawResponse { get; set; }
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset? BookedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    // Set at the handoff scan: from here the carrier is the parcel's custodian (I4 spec §3.6).
    public DateTimeOffset? CustodyStartedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

`src/Modules/International/Domain/CarrierTrackingEvent.cs`:

```csharp
namespace Courier.International.Domain;

// Append-only record of one carrier-reported tracking event, from any source (I4 spec §4.3).
// Deduplicated by (CarrierAccountId, CarrierEventId). After insert only the processing columns
// (CarrierBookingId, ProcessingStatus, ProcessingNote, MappedShipmentStatus, ProcessingAttempts,
// ClaimedAt, ProcessedAt) ever change.
public class CarrierTrackingEvent
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CarrierAccountId { get; set; }
    public Guid? CarrierBookingId { get; set; }
    public required string CarrierAwb { get; set; }
    public required string CarrierEventId { get; set; }
    public required string CarrierStatusCode { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public CarrierEventSource Source { get; set; }
    public string? RawPayload { get; set; }
    public Guid? EnteredByUserId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public CarrierEventProcessingStatus ProcessingStatus { get; set; }
    public string? ProcessingNote { get; set; }
    // ShipmentStatus name (e.g. "CustomsHold") — a string so International.Domain stays
    // independent of Shipments.Domain.
    public string? MappedShipmentStatus { get; set; }
    public int ProcessingAttempts { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
```

- [ ] **Step 2: Create the EF configurations**

`src/Modules/International/Infrastructure/CarrierAccountConfiguration.cs`:

```csharp
using Courier.International.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.International.Infrastructure;

public class CarrierAccountConfiguration : IEntityTypeConfiguration<CarrierAccount>
{
    public void Configure(EntityTypeBuilder<CarrierAccount> builder)
    {
        builder.ToTable("CarrierAccounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.CarrierCode).HasMaxLength(50).IsRequired();
        builder.Property(a => a.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.WebhookPublicId).HasMaxLength(64).IsRequired();
        builder.Property(a => a.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(a => a.OrganizationId);
        // Platform-unique: the anonymous webhook route resolves the account (and so the tenant) by it.
        builder.HasIndex(a => a.WebhookPublicId).IsUnique();
    }
}
```

`src/Modules/International/Infrastructure/CarrierBookingConfiguration.cs`:

```csharp
using Courier.International.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.International.Infrastructure;

public class CarrierBookingConfiguration : IEntityTypeConfiguration<CarrierBooking>
{
    public void Configure(EntityTypeBuilder<CarrierBooking> builder)
    {
        builder.ToTable("CarrierBookings");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.CarrierAwb).HasMaxLength(100);
        builder.Property(b => b.ServiceLevel).HasMaxLength(100);
        builder.Property(b => b.LabelStorageKey).HasMaxLength(500);
        builder.Property(b => b.FailureReason).HasMaxLength(1000);
        builder.Property(b => b.RawResponse).HasColumnType("jsonb");
        builder.Property(b => b.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(b => b.CreatedAt).HasDefaultValueSql("now()");

        builder.HasOne<InternationalLeg>().WithMany().HasForeignKey(b => b.InternationalLegId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CarrierAccount>().WithMany().HasForeignKey(b => b.CarrierAccountId).OnDelete(DeleteBehavior.Restrict);

        // At most one live booking (Pending = 0, Booked = 1) per leg; Failed/Cancelled are history.
        builder.HasIndex(b => b.InternationalLegId).IsUnique().HasFilter("\"Status\" IN (0, 1)");
        // Ingest resolves an event to exactly one Booked booking by (account, carrier AWB).
        builder.HasIndex(b => new { b.CarrierAccountId, b.CarrierAwb }).IsUnique().HasFilter("\"CarrierAwb\" IS NOT NULL AND \"Status\" = 1");
        builder.HasIndex(b => new { b.OrganizationId, b.IdempotencyKey }).IsUnique();
    }
}
```

`src/Modules/International/Infrastructure/CarrierTrackingEventConfiguration.cs`:

```csharp
using Courier.International.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.International.Infrastructure;

public class CarrierTrackingEventConfiguration : IEntityTypeConfiguration<CarrierTrackingEvent>
{
    public void Configure(EntityTypeBuilder<CarrierTrackingEvent> builder)
    {
        builder.ToTable("CarrierTrackingEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.CarrierAwb).HasMaxLength(100).IsRequired();
        builder.Property(e => e.CarrierEventId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.CarrierStatusCode).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.Location).HasMaxLength(300);
        builder.Property(e => e.RawPayload).HasColumnType("jsonb");
        builder.Property(e => e.ProcessingNote).HasMaxLength(1000);
        builder.Property(e => e.MappedShipmentStatus).HasMaxLength(50);
        builder.Property(e => e.ReceivedAt).HasDefaultValueSql("now()");

        builder.HasOne<CarrierAccount>().WithMany().HasForeignKey(e => e.CarrierAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CarrierBooking>().WithMany().HasForeignKey(e => e.CarrierBookingId).OnDelete(DeleteBehavior.Restrict);

        // Deduplication across every source (webhook, poll, manual).
        builder.HasIndex(e => new { e.CarrierAccountId, e.CarrierEventId }).IsUnique();
        builder.HasIndex(e => new { e.ProcessingStatus, e.OccurredAt });
        builder.HasIndex(e => e.CarrierBookingId);
        builder.HasIndex(e => new { e.CarrierAccountId, e.CarrierAwb });
    }
}
```

- [ ] **Step 3: Wire into `AppDbContext`**

In `src/Persistence/AppDbContext.cs`:

After `public DbSet<CustomsItem> CustomsItems => Set<CustomsItem>();` add:

```csharp
    public DbSet<CarrierAccount> CarrierAccounts => Set<CarrierAccount>();
    public DbSet<CarrierBooking> CarrierBookings => Set<CarrierBooking>();
    public DbSet<CarrierTrackingEvent> CarrierTrackingEvents => Set<CarrierTrackingEvent>();
```

After `modelBuilder.ApplyConfiguration(new CustomsItemConfiguration());` add:

```csharp
        modelBuilder.ApplyConfiguration(new CarrierAccountConfiguration());
        modelBuilder.ApplyConfiguration(new CarrierBookingConfiguration());
        modelBuilder.ApplyConfiguration(new CarrierTrackingEventConfiguration());
```

After the `InternationalLeg.ShipmentId -> Shipments` FK block add:

```csharp
        // Same reasoning for the three I4 carrier tables: International has no project reference
        // to Identity.
        modelBuilder.Entity<CarrierAccount>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CarrierBooking>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(b => b.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CarrierTrackingEvent>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
```

After the `InternationalLeg` `HasQueryFilter` line add:

```csharp
        modelBuilder.Entity<CarrierAccount>().HasQueryFilter(a => _currentTenantProvider.OrganizationId != null && a.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<CarrierBooking>().HasQueryFilter(b => _currentTenantProvider.OrganizationId != null && b.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<CarrierTrackingEvent>().HasQueryFilter(e => _currentTenantProvider.OrganizationId != null && e.OrganizationId == _currentTenantProvider.OrganizationId);
```

Run: `dotnet build Courier.slnx` — Expected: 0 errors.

- [ ] **Step 4: Generate, inspect and apply the migration**

Run: `dotnet ef migrations add AddCarrierLayer --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`

Inspect the generated `*_AddCarrierLayer.cs`: it must only `CreateTable` `CarrierAccounts`, `CarrierBookings`, `CarrierTrackingEvents` and create their indexes/FKs — including the two filtered unique indexes with the exact filters `"Status" IN (0, 1)` and `"CarrierAwb" IS NOT NULL AND "Status" = 1`. It must not alter any existing table. If it does, stop and report.

Run: `dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj` — Expected: applied.

- [ ] **Step 5: Move the I3 API test helpers into a shared base class**

Create `tests/Api.Tests/International/InternationalApiTestBase.cs` by **moving** (not copying) these members out of `tests/Api.Tests/InternationalShipmentsEndpointsTests.cs`, changing `private` to `protected` (except `OrgContext`, which becomes `public sealed record` — a test-only record that Task 15's nested harness returns publicly) and `_factory` to `Factory`: the four `Kathmandu*` fields, `RegisterOrgAsync`, `ValidBranchRequest`, `CreateBranchAsync`, `CreateMerchantAsync`, `MarkCountryServicedAsync`, `GetStandardServiceTypeIdAsync`, `OutboundBody`, `CustomsBody`, `OrgContext`, `SetUpOrgAsync`, `CreateOutboundAsync`, `SetShipmentStateAsync`, `SeedUserClientAsync`. Keep their bodies exactly as they are. The file starts:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Domain;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.International.Application;
using Courier.Merchants.Application;
using Courier.Persistence;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests.International;

// Shared helpers for every international API test class (I3 endpoints, I4 carrier layer). Derived
// classes still declare IClassFixture<WebApplicationFactory<Program>> themselves and pass the
// factory (or a WithWebHostBuilder-derived one) to this constructor.
public abstract class InternationalApiTestBase
{
    protected InternationalApiTestBase(WebApplicationFactory<Program> factory)
    {
        Factory = factory;
    }

    protected WebApplicationFactory<Program> Factory { get; }

    // ... moved members go here ...
}
```

Add these new helpers to the base (below the moved ones):

```csharp
    protected static object InboundBody(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, string senderCountryCode) => new
    {
        merchantId,
        direction = "Inbound",
        originBranchId,
        destinationBranchId,
        serviceTypeId,
        weightKg = 1m,
        lengthCm = (decimal?)null,
        widthCm = (decimal?)null,
        heightCm = (decimal?)null,
        recipientName = "Local Recipient",
        recipientPhone = "9811111111",
        recipientProvinceId = KathmanduProvinceId,
        recipientDistrictId = KathmanduDistrictId,
        recipientLocalLevelId = KathmanduLocalLevelId,
        recipientWardNumber = KathmanduWardNumber,
        recipientAddressLine = "Ward 1 Line",
        recipientCity = (string?)null,
        recipientRegion = (string?)null,
        recipientPostalCode = (string?)null,
        recipientCountryCode = (string?)null,
        senderName = "Foreign Sender",
        senderPhone = "9998887776",
        senderProvinceId = (Guid?)null,
        senderDistrictId = (Guid?)null,
        senderLocalLevelId = (Guid?)null,
        senderWardNumber = (int?)null,
        senderAddressLine = "221B Baker Street",
        senderCity = "London",
        senderRegion = (string?)null,
        senderPostalCode = "NW16XE",
        senderCountryCode,
        provisionalTotalChargeAmount = 5000m,
        provisionalTotalChargeCurrency = "NPR",
        items = new[] { new { description = "Sample goods", quantity = 1, unitValue = 5000m } }
    };

    protected async Task<InternationalShipmentDetailResponse> CreateInboundAsync(OrgContext org, Guid? originBranchId = null, Guid? destinationBranchId = null)
    {
        var response = await org.Client.PostAsJsonAsync("/api/v1/international/shipments",
            InboundBody(org.MerchantId, originBranchId ?? org.BranchId, destinationBranchId ?? org.BranchId, org.ServiceTypeId, "GB"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InternationalShipmentDetailResponse>())!;
    }

    // Outbound shipment at the org's single branch, customs completed and prepared — the state a
    // carrier booking starts from. (Sorted is positioned directly, as in the I3 tests.)
    protected async Task<InternationalShipmentDetailResponse> PrepareOutboundAtCustomsPreparedAsync(OrgContext org)
    {
        var created = await CreateOutboundAsync(org);
        await SetShipmentStateAsync(created.Shipment.Id, ShipmentStatus.Sorted, org.BranchId);
        var customs = await org.Client.PatchAsJsonAsync($"/api/v1/international/shipments/{created.Shipment.Id}/customs",
            CustomsBody(created.Shipment.Items.Single().Id));
        Assert.Equal(HttpStatusCode.OK, customs.StatusCode);
        var prepare = await org.Client.PostAsync($"/api/v1/international/shipments/{created.Shipment.Id}/prepare-customs", null);
        Assert.Equal(HttpStatusCode.OK, prepare.StatusCode);
        return created;
    }
```

Then change `InternationalShipmentsEndpointsTests` to:

```csharp
public class InternationalShipmentsEndpointsTests : InternationalApiTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public InternationalShipmentsEndpointsTests(WebApplicationFactory<Program> factory) : base(factory)
    {
        _factory = factory;
    }
```

add `using Courier.Api.Tests.International;`, delete the moved members from it, and leave every test method body unchanged. Keep `AllIntlPermissions`, `CountOrgShipmentsAsync`, `ShipmentResponseForTest` and any other member not listed above in the I3 class.

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~InternationalShipmentsEndpointsTests"` — Expected: PASS, same test count as before the move.

- [ ] **Step 6: Write the persistence tests**

`tests/Api.Tests/International/CarrierPersistenceTests.cs`:

```csharp
using Courier.International.Domain;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierPersistenceTests : InternationalApiTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public CarrierPersistenceTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private async Task<(Guid OrganizationId, Guid LegId, Guid AccountId)> SeedAccountAndLegAsync(string label)
    {
        var org = await SetUpOrgAsync(label);
        var created = await CreateOutboundAsync(org);
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var legId = await dbContext.Set<InternationalLeg>().IgnoreQueryFilters()
            .Where(l => l.ShipmentId == created.Shipment.Id).Select(l => l.Id).SingleAsync();
        var account = new CarrierAccount
        {
            Id = Guid.NewGuid(), OrganizationId = org.Auth.OrganizationId, CarrierCode = "MANUAL", DisplayName = "Manual",
            WebhookPublicId = Guid.NewGuid().ToString("N"), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<CarrierAccount>().Add(account);
        await dbContext.SaveChangesAsync();
        return (org.Auth.OrganizationId, legId, account.Id);
    }

    private static CarrierBooking Booking(Guid organizationId, Guid legId, Guid accountId, CarrierBookingStatus status, string? awb = null) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = organizationId, InternationalLegId = legId, CarrierAccountId = accountId,
        Direction = CarrierBookingDirection.Outbound, Status = status, CarrierAwb = awb, IdempotencyKey = Guid.NewGuid().ToString("N"),
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task SecondLiveBookingForSameLeg_IsRejectedByTheDatabase_ButHistoryIsAllowed()
    {
        var (organizationId, legId, accountId) = await SeedAccountAndLegAsync("live-booking");
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        dbContext.Set<CarrierBooking>().Add(Booking(organizationId, legId, accountId, CarrierBookingStatus.Failed));
        dbContext.Set<CarrierBooking>().Add(Booking(organizationId, legId, accountId, CarrierBookingStatus.Booked, "AWB-1"));
        await dbContext.SaveChangesAsync();

        dbContext.Set<CarrierBooking>().Add(Booking(organizationId, legId, accountId, CarrierBookingStatus.Pending));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateCarrierEventIdForSameAccount_IsRejectedByTheDatabase()
    {
        var (organizationId, _, accountId) = await SeedAccountAndLegAsync("dup-event");
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        CarrierTrackingEvent Event() => new()
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, CarrierAccountId = accountId, CarrierAwb = "AWB-1",
            CarrierEventId = "evt-1", CarrierStatusCode = "IN_TRANSIT", OccurredAt = DateTimeOffset.UtcNow,
            Source = CarrierEventSource.Manual, ReceivedAt = DateTimeOffset.UtcNow
        };

        dbContext.Set<CarrierTrackingEvent>().Add(Event());
        await dbContext.SaveChangesAsync();
        dbContext.Set<CarrierTrackingEvent>().Add(Event());

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task CarrierAccounts_AreTenantFiltered()
    {
        var (organizationA, _, accountId) = await SeedAccountAndLegAsync("filter-a");
        var (organizationB, _, _) = await SeedAccountAndLegAsync("filter-b");
        var tenantScopes = Factory.Services.GetRequiredService<ITenantScopeFactory>();

        await using (var scopeA = tenantScopes.CreateScope(organizationA))
        {
            var visible = await scopeA.ServiceProvider.GetRequiredService<AppDbContext>().Set<CarrierAccount>().Select(a => a.Id).ToListAsync();
            Assert.Equal(new[] { accountId }, visible);
        }
        await using (var scopeB = tenantScopes.CreateScope(organizationB))
        {
            Assert.False(await scopeB.ServiceProvider.GetRequiredService<AppDbContext>().Set<CarrierAccount>().AnyAsync(a => a.Id == accountId));
        }
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CarrierPersistenceTests"` — Expected: PASS (3 tests).
Run: `dotnet build Courier.slnx` — Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/International/Domain/ src/Modules/International/Infrastructure/ src/Persistence/ tests/Api.Tests/
git commit -m "feat(international): add carrier account, booking and tracking event persistence

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Carrier permissions and role grants

**Files:**
- Modify: `src/Modules/Identity/Application/PermissionCatalog.cs`
- Modify: `src/Modules/Identity/Application/AdditionalRoleSeeds.cs` (`OperationsManager`, `HubStaff`)
- Create: `src/Persistence/Migrations/<timestamp>_AddCarrierPermissions.cs` (generated)
- Test: `tests/Identity.Tests/InternationalCarrierPermissionTests.cs`

**Interfaces:**
- Produces: permission keys `international.carrier.manage`, `international.book`, `international.tracking.manage`.

- [ ] **Step 1: Write the failing test**

```csharp
using Courier.Identity.Application;
using Xunit;

namespace Courier.Identity.Tests;

public class InternationalCarrierPermissionTests
{
    [Theory]
    [InlineData("international.carrier.manage")]
    [InlineData("international.book")]
    [InlineData("international.tracking.manage")]
    public void Catalog_ContainsCarrierPermission(string key)
    {
        Assert.Contains(PermissionCatalog.All, p => p.Key == key);
    }

    [Fact]
    public void OperationsManager_HoldsEveryCarrierOperationPermission()
    {
        var granted = AdditionalRoleSeeds.ByRoleName["OperationsManager"];
        Assert.Contains("international.handoff", granted);
        Assert.Contains("international.receive", granted);
        Assert.Contains("international.book", granted);
        Assert.Contains("international.tracking.manage", granted);
        Assert.DoesNotContain("international.carrier.manage", granted);
    }

    [Fact]
    public void HubStaff_CanBookAndScanButNotOverrideOrManageAccounts()
    {
        var granted = AdditionalRoleSeeds.ByRoleName["HubStaff"];
        Assert.Contains("international.handoff", granted);
        Assert.Contains("international.receive", granted);
        Assert.Contains("international.book", granted);
        Assert.DoesNotContain("international.tracking.manage", granted);
        Assert.DoesNotContain("international.carrier.manage", granted);
    }

    [Fact]
    public void OrganizationAdmin_CanManageCarrierAccounts()
    {
        Assert.Contains("international.carrier.manage", AdditionalRoleSeeds.ByRoleName["OrganizationAdmin"]);
    }

    [Fact]
    public void EverySeededPermission_ExistsInTheCatalog()
    {
        var catalog = PermissionCatalog.All.Select(p => p.Key).ToHashSet();
        foreach (var (role, keys) in AdditionalRoleSeeds.ByRoleName)
        {
            foreach (var key in keys)
            {
                Assert.True(catalog.Contains(key), $"{role} is granted '{key}', which is not in PermissionCatalog.");
            }
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Identity.Tests --filter "FullyQualifiedName~InternationalCarrierPermissionTests"`
Expected: FAIL (the new keys and grants are missing).

- [ ] **Step 3: Implement**

In `PermissionCatalog.cs`, after `("international.receive", "Receive international shipments from a carrier"),` add:

```csharp
        ("international.carrier.manage", "Manage international carrier accounts and credentials"),
        ("international.book", "Book, cancel and link international carrier bookings"),
        ("international.tracking.manage", "Record carrier tracking events and override international exception statuses"),
```

In `AdditionalRoleSeeds.cs`, change the `OperationsManager` array's `"international.create", "international.read", "international.manage_customs"` line to:

```csharp
            "international.create", "international.read", "international.manage_customs",
            "international.handoff", "international.receive", "international.book", "international.tracking.manage"
```

and the `HubStaff` array's same line to:

```csharp
            "international.create", "international.read", "international.manage_customs",
            "international.handoff", "international.receive", "international.book"
```

(`OrganizationAdmin` already takes `PermissionCatalog.All` minus billing/ownership/delete; `OrganizationOwner` receives every `Permission` row at registration — no change needed.)

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Identity.Tests` — Expected: PASS.

- [ ] **Step 5: Generate, inspect and apply the migration**

Run: `dotnet ef migrations add AddCarrierPermissions --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`

`PermissionConfiguration` seeds `PermissionCatalog.All` through `HasData`, so EF generates `InsertData` for exactly the three new `Permissions` rows (ids = `PermissionCatalog.IdFor(key)`) and matching `DeleteData` in `Down`. Inspect: nothing else may change. Then:

Run: `dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj` — Expected: applied.

(Existing tenants' already-seeded roles do not receive the new grants — role seeds apply at registration; there is no backfill precedent in this codebase. New organizations get them. Documented in Task 16.)

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Identity/ src/Persistence/ tests/Identity.Tests/
git commit -m "feat(identity): add international carrier permissions and grant carrier operations

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Carrier port, registry, manual carrier and secret handling

**Files:**
- Create: `src/Modules/International/Application/Carriers/CarrierContracts.cs`
- Create: `src/Modules/International/Application/Carriers/ICarrierProvider.cs`
- Create: `src/Modules/International/Application/Carriers/CarrierProviderRegistry.cs`
- Create: `src/Modules/International/Application/Carriers/ICarrierSecretProtector.cs`
- Create: `src/Modules/International/Application/Carriers/CarrierPayloadSanitizer.cs`
- Create: `src/Modules/International/Infrastructure/Carriers/ManualCarrierProvider.cs`
- Create: `src/Modules/International/Infrastructure/Carriers/DataProtectionCarrierSecretProtector.cs`
- Modify: `src/Api/Program.cs` (DI, next to the existing International registrations ~line 253)
- Test: `tests/International.Tests/Carriers/ManualCarrierProviderTests.cs`, `CarrierProviderRegistryTests.cs`, `CarrierPayloadSanitizerTests.cs`, `DataProtectionCarrierSecretProtectorTests.cs`

**Interfaces:**
- Produces (namespace `Courier.International.Application.Carriers`):
  - `record CarrierCapabilities(bool SupportsWebhooks, bool SupportsPolling, bool RequiresManualAwb, bool SupportsLabels)`
  - `sealed record CarrierCredentials(IReadOnlyDictionary<string, string> Values)` with `static Empty`, `SecretValues`, a non-leaking `ToString()`
  - `record CarrierBookingRequest(Guid ShipmentId, string PlatformAwb, string? ServiceLevel, string? ManualCarrierAwb, decimal WeightKg)`
  - `record CarrierLabel(byte[] Content, string ContentType)`
  - `record CarrierBookingResult(string CarrierAwb, string? ServiceLevel, CarrierLabel? Label, string? RawResponse)`
  - `record CarrierTrackingUpdate(string CarrierAwb, string CarrierEventId, string CarrierStatusCode, string? Description, string? Location, DateTimeOffset OccurredAt, string? RawPayload)`
  - `class CarrierBookingException(string message) : Exception` — the carrier rejected the request
  - `class CarrierWebhookPayloadException(string message) : Exception` — unparseable webhook body
  - `interface ICarrierProvider` (members below)
  - `interface ICarrierProviderRegistry { ICarrierProvider? Find(string carrierCode); IReadOnlyCollection<string> RegisteredCodes { get; } }` + `CarrierProviderRegistry`
  - `interface ICarrierSecretProtector { string ProtectCredentials(CarrierCredentials credentials); CarrierCredentials UnprotectCredentials(string? ciphertext); string ProtectWebhookSecret(string secret); string UnprotectWebhookSecret(string ciphertext); }`
  - `static class CarrierPayloadSanitizer { const string Mask = "***"; static string MaskSecrets(string text, IEnumerable<string> secretValues); static string? ToStoredJson(string? raw, IEnumerable<string> secretValues); }`
- Produces (namespace `Courier.International.Infrastructure.Carriers`): `ManualCarrierProvider` (`Code = "MANUAL"`), `DataProtectionCarrierSecretProtector`.

- [ ] **Step 1: Write the failing tests**

`tests/International.Tests/Carriers/ManualCarrierProviderTests.cs`:

```csharp
using Courier.International.Application.Carriers;
using Courier.International.Infrastructure.Carriers;
using Courier.Shipments.Domain;

namespace Courier.International.Tests.Carriers;

public class ManualCarrierProviderTests
{
    private readonly ManualCarrierProvider _provider = new();

    [Theory]
    [InlineData("IN_TRANSIT", ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit)]
    [InlineData("IN_TRANSIT", ShipmentDirection.Inbound, ShipmentStatus.InInternationalTransit)]
    [InlineData("IN_CUSTOMS", ShipmentDirection.Inbound, ShipmentStatus.CustomsClearance)]
    [InlineData("CLEARED", ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit)]
    [InlineData("CLEARED", ShipmentDirection.Inbound, ShipmentStatus.CustomsClearance)]
    [InlineData("CUSTOMS_HOLD", ShipmentDirection.Outbound, ShipmentStatus.CustomsHold)]
    [InlineData("CUSTOMS_HOLD", ShipmentDirection.Inbound, ShipmentStatus.CustomsHold)]
    [InlineData("EXCEPTION", ShipmentDirection.Outbound, ShipmentStatus.InternationalException)]
    [InlineData("EXCEPTION", ShipmentDirection.Inbound, ShipmentStatus.InternationalException)]
    [InlineData("DELIVERED", ShipmentDirection.Outbound, ShipmentStatus.DeliveredAbroad)]
    [InlineData("in_transit", ShipmentDirection.Outbound, ShipmentStatus.InInternationalTransit)]
    public void MapStatus_KnownCode_MapsPerDirection(string code, ShipmentDirection direction, ShipmentStatus expected)
    {
        Assert.Equal(expected, _provider.MapStatus(code, direction));
    }

    [Theory]
    [InlineData("IN_CUSTOMS", ShipmentDirection.Outbound)]
    [InlineData("DELIVERED", ShipmentDirection.Inbound)]
    [InlineData("ARRIVED", ShipmentDirection.Outbound)]
    [InlineData("ARRIVED", ShipmentDirection.Inbound)]
    [InlineData("NOT_A_CODE", ShipmentDirection.Outbound)]
    public void MapStatus_InformationalOrUnknownCode_ReturnsNull(string code, ShipmentDirection direction)
    {
        Assert.Null(_provider.MapStatus(code, direction));
    }

    [Theory]
    [InlineData("ARRIVED", true)]
    [InlineData("delivered", true)]
    [InlineData("NOT_A_CODE", false)]
    public void IsKnownStatusCode_ReflectsTheVocabulary(string code, bool expected)
    {
        Assert.Equal(expected, _provider.IsKnownStatusCode(code));
    }

    [Fact]
    public async Task BookAsync_UsesTheStaffSuppliedAwb()
    {
        var result = await _provider.BookAsync(new CarrierBookingRequest(Guid.NewGuid(), "KTM-1", "EXPRESS", "  MAN-778  ", 1m), CarrierCredentials.Empty, "key-1", CancellationToken.None);

        Assert.Equal("MAN-778", result.CarrierAwb);
        Assert.Equal("EXPRESS", result.ServiceLevel);
        Assert.Null(result.Label);
    }

    [Fact]
    public async Task BookAsync_WithoutAwb_ThrowsBookingException()
    {
        await Assert.ThrowsAsync<CarrierBookingException>(() =>
            _provider.BookAsync(new CarrierBookingRequest(Guid.NewGuid(), "KTM-1", null, null, 1m), CarrierCredentials.Empty, "key-1", CancellationToken.None));
    }

    [Fact]
    public void Capabilities_ManualAwbOnly()
    {
        Assert.Equal(new CarrierCapabilities(SupportsWebhooks: false, SupportsPolling: false, RequiresManualAwb: true, SupportsLabels: false), _provider.Capabilities);
    }
}
```

`tests/International.Tests/Carriers/CarrierProviderRegistryTests.cs`:

```csharp
using Courier.International.Application.Carriers;
using Courier.International.Infrastructure.Carriers;

namespace Courier.International.Tests.Carriers;

public class CarrierProviderRegistryTests
{
    [Fact]
    public void Find_IsCaseInsensitive_AndUnknownIsNull()
    {
        var registry = new CarrierProviderRegistry(new ICarrierProvider[] { new ManualCarrierProvider() });

        Assert.IsType<ManualCarrierProvider>(registry.Find("manual"));
        Assert.Null(registry.Find("DHL_EXPRESS"));
        Assert.Equal(new[] { "MANUAL" }, registry.RegisteredCodes);
    }

    [Fact]
    public void DuplicateCarrierCodes_Throw()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new CarrierProviderRegistry(new ICarrierProvider[] { new ManualCarrierProvider(), new ManualCarrierProvider() }));
    }
}
```

`tests/International.Tests/Carriers/CarrierPayloadSanitizerTests.cs`:

```csharp
using System.Text.Json;
using Courier.International.Application.Carriers;

namespace Courier.International.Tests.Carriers;

public class CarrierPayloadSanitizerTests
{
    [Fact]
    public void ToStoredJson_MasksSecretValuesAndKeepsValidJson()
    {
        var stored = CarrierPayloadSanitizer.ToStoredJson("{\"apiKey\":\"sk_live_12345\",\"awb\":\"A1\"}", new[] { "sk_live_12345" });

        Assert.Equal("{\"apiKey\":\"***\",\"awb\":\"A1\"}", stored);
        using var _ = JsonDocument.Parse(stored!);
    }

    [Fact]
    public void ToStoredJson_NonJsonText_IsStoredAsAJsonString()
    {
        var stored = CarrierPayloadSanitizer.ToStoredJson("Bad Gateway from sk_live_12345", new[] { "sk_live_12345" });

        Assert.Equal("\"Bad Gateway from ***\"", stored);
    }

    [Fact]
    public void ToStoredJson_Null_IsNull()
    {
        Assert.Null(CarrierPayloadSanitizer.ToStoredJson(null, Array.Empty<string>()));
    }
}
```

`tests/International.Tests/Carriers/DataProtectionCarrierSecretProtectorTests.cs`:

```csharp
using Courier.International.Application.Carriers;
using Courier.International.Infrastructure.Carriers;
using Microsoft.AspNetCore.DataProtection;

namespace Courier.International.Tests.Carriers;

public class DataProtectionCarrierSecretProtectorTests
{
    private readonly DataProtectionCarrierSecretProtector _protector = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Credentials_RoundTrip_AndCiphertextDoesNotContainPlaintext()
    {
        var credentials = new CarrierCredentials(new Dictionary<string, string> { ["apiKey"] = "sk_live_12345", ["account"] = "ACC-9" });

        var ciphertext = _protector.ProtectCredentials(credentials);
        var roundTripped = _protector.UnprotectCredentials(ciphertext);

        Assert.DoesNotContain("sk_live_12345", ciphertext);
        Assert.Equal("sk_live_12345", roundTripped.Values["apiKey"]);
        Assert.Equal("ACC-9", roundTripped.Values["account"]);
    }

    [Fact]
    public void UnprotectCredentials_Null_IsEmpty()
    {
        Assert.Empty(_protector.UnprotectCredentials(null).Values);
    }

    [Fact]
    public void WebhookSecret_UsesADifferentPurposeThanCredentials()
    {
        var ciphertext = _protector.ProtectWebhookSecret("whsec_abc");

        Assert.Equal("whsec_abc", _protector.UnprotectWebhookSecret(ciphertext));
        Assert.ThrowsAny<Exception>(() => _protector.UnprotectCredentials(ciphertext));
    }

    [Fact]
    public void CredentialsToString_DoesNotLeakValues()
    {
        var credentials = new CarrierCredentials(new Dictionary<string, string> { ["apiKey"] = "sk_live_12345" });

        Assert.DoesNotContain("sk_live_12345", credentials.ToString());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/International.Tests --filter "FullyQualifiedName~Carriers"`
Expected: FAIL — compile errors, the types do not exist.

- [ ] **Step 3: Implement the Application contracts**

`src/Modules/International/Application/Carriers/CarrierContracts.cs`:

```csharp
namespace Courier.International.Application.Carriers;

public record CarrierCapabilities(bool SupportsWebhooks, bool SupportsPolling, bool RequiresManualAwb, bool SupportsLabels);

// Decrypted carrier credentials. Held only for the duration of one provider call; ToString never
// prints values (records print every member by default), so accidental logging cannot leak them.
public sealed record CarrierCredentials(IReadOnlyDictionary<string, string> Values)
{
    public static CarrierCredentials Empty { get; } = new(new Dictionary<string, string>());

    // Values long enough to be worth masking in stored carrier payloads.
    public IEnumerable<string> SecretValues => Values.Values.Where(v => v.Length >= 4);

    public override string ToString() => $"CarrierCredentials({Values.Count} values)";
}

// Platform-side booking input. I6 (DHL) extends this with the addresses/customs it needs.
public record CarrierBookingRequest(Guid ShipmentId, string PlatformAwb, string? ServiceLevel, string? ManualCarrierAwb, decimal WeightKg);

public record CarrierLabel(byte[] Content, string ContentType);

public record CarrierBookingResult(string CarrierAwb, string? ServiceLevel, CarrierLabel? Label, string? RawResponse);

public record CarrierTrackingUpdate(
    string CarrierAwb,
    string CarrierEventId,
    string CarrierStatusCode,
    string? Description,
    string? Location,
    DateTimeOffset OccurredAt,
    string? RawPayload);

// The carrier understood the request and refused it (bad data, unknown AWB, account problem).
public class CarrierBookingException : Exception
{
    public CarrierBookingException(string message) : base(message) { }
}

// A webhook body the provider could not parse (after its signature verified).
public class CarrierWebhookPayloadException : Exception
{
    public CarrierWebhookPayloadException(string message) : base(message) { }
}
```

`src/Modules/International/Application/Carriers/ICarrierProvider.cs`:

```csharp
using Courier.Shipments.Domain;

namespace Courier.International.Application.Carriers;

// The carrier port (I4 spec §5.1). Adapters live in International.Infrastructure and use platform
// types only; network adapters are registered through AddCarrierHttpClient so they get the shared
// resilience pipeline. GetRatesAsync is added in I5.
public interface ICarrierProvider
{
    string CarrierCode { get; }
    CarrierCapabilities Capabilities { get; }

    Task<CarrierBookingResult> BookAsync(CarrierBookingRequest request, CarrierCredentials credentials, string idempotencyKey, CancellationToken cancellationToken);
    Task CancelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken);
    Task<CarrierLabel?> GetLabelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken);
    Task<IReadOnlyList<CarrierTrackingUpdate>> GetTrackingAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken);

    // headers are case-insensitive. Must compare signatures in constant time.
    bool VerifyWebhook(IReadOnlyDictionary<string, string> headers, byte[] rawBody, string webhookSecret);
    // Throws CarrierWebhookPayloadException for an unparseable body.
    IReadOnlyList<CarrierTrackingUpdate> ParseWebhook(byte[] rawBody);

    // Whether staff may enter this code manually (manual carrier events, I4 spec §5.5).
    bool IsKnownStatusCode(string carrierStatusCode);
    // null = informational only (the event is kept, shown on the timeline, never applied).
    ShipmentStatus? MapStatus(string carrierStatusCode, ShipmentDirection direction);
}
```

`src/Modules/International/Application/Carriers/CarrierProviderRegistry.cs`:

```csharp
namespace Courier.International.Application.Carriers;

public interface ICarrierProviderRegistry
{
    ICarrierProvider? Find(string carrierCode);
    IReadOnlyCollection<string> RegisteredCodes { get; }
}

// Scoped: providers built on typed HttpClients are transient, so the registry is rebuilt per scope.
public sealed class CarrierProviderRegistry : ICarrierProviderRegistry
{
    private readonly Dictionary<string, ICarrierProvider> _providers;

    public CarrierProviderRegistry(IEnumerable<ICarrierProvider> providers)
    {
        _providers = new Dictionary<string, ICarrierProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            if (!_providers.TryAdd(provider.CarrierCode, provider))
            {
                throw new InvalidOperationException($"Two carrier providers are registered for code '{provider.CarrierCode}'.");
            }
        }
    }

    public ICarrierProvider? Find(string carrierCode) => _providers.GetValueOrDefault(carrierCode);

    public IReadOnlyCollection<string> RegisteredCodes => _providers.Keys.ToList();
}
```

`src/Modules/International/Application/Carriers/ICarrierSecretProtector.cs`:

```csharp
namespace Courier.International.Application.Carriers;

public interface ICarrierSecretProtector
{
    string ProtectCredentials(CarrierCredentials credentials);
    // null ciphertext (no credentials configured, e.g. the manual carrier) -> CarrierCredentials.Empty.
    CarrierCredentials UnprotectCredentials(string? ciphertext);
    string ProtectWebhookSecret(string secret);
    string UnprotectWebhookSecret(string ciphertext);
}
```

`src/Modules/International/Application/Carriers/CarrierPayloadSanitizer.cs`:

```csharp
using System.Text.Json;

namespace Courier.International.Application.Carriers;

// Carrier responses/payloads are stored (jsonb) for support and audit, with any known secret value
// replaced — a carrier echoing our API key must never land in the database in plaintext.
public static class CarrierPayloadSanitizer
{
    public const string Mask = "***";

    public static string MaskSecrets(string text, IEnumerable<string> secretValues)
    {
        foreach (var secret in secretValues.Where(s => !string.IsNullOrEmpty(s)).OrderByDescending(s => s.Length))
        {
            text = text.Replace(secret, Mask, StringComparison.Ordinal);
        }
        return text;
    }

    // Always valid JSON (the columns are jsonb): JSON input is kept as JSON, anything else is
    // stored as a JSON string.
    public static string? ToStoredJson(string? raw, IEnumerable<string> secretValues)
    {
        if (raw is null)
        {
            return null;
        }
        var masked = MaskSecrets(raw, secretValues);
        try
        {
            using var _ = JsonDocument.Parse(masked);
            return masked;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(masked);
        }
    }
}
```

- [ ] **Step 4: Implement the Infrastructure adapters**

`src/Modules/International/Infrastructure/Carriers/ManualCarrierProvider.cs`:

```csharp
using Courier.International.Application.Carriers;
using Courier.Shipments.Domain;

namespace Courier.International.Infrastructure.Carriers;

// Staff record the carrier's AWB at booking and enter tracking events by hand (I4 spec §5.2).
// No network calls, no labels, no webhooks, no polling.
public sealed class ManualCarrierProvider : ICarrierProvider
{
    public const string Code = "MANUAL";

    // (outbound target, inbound target); null = informational for that direction.
    private static readonly Dictionary<string, (ShipmentStatus? Outbound, ShipmentStatus? Inbound)> Vocabulary =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["IN_TRANSIT"] = (ShipmentStatus.InInternationalTransit, ShipmentStatus.InInternationalTransit),
            ["IN_CUSTOMS"] = (null, ShipmentStatus.CustomsClearance),
            ["CLEARED"] = (ShipmentStatus.InInternationalTransit, ShipmentStatus.CustomsClearance),
            ["CUSTOMS_HOLD"] = (ShipmentStatus.CustomsHold, ShipmentStatus.CustomsHold),
            ["EXCEPTION"] = (ShipmentStatus.InternationalException, ShipmentStatus.InternationalException),
            // Inbound delivery is our own last mile, not a carrier event.
            ["DELIVERED"] = (ShipmentStatus.DeliveredAbroad, null),
            // Arrival at our gateway is the receive scan, not a carrier event.
            ["ARRIVED"] = (null, null)
        };

    public string CarrierCode => Code;

    public CarrierCapabilities Capabilities { get; } = new(SupportsWebhooks: false, SupportsPolling: false, RequiresManualAwb: true, SupportsLabels: false);

    public Task<CarrierBookingResult> BookAsync(CarrierBookingRequest request, CarrierCredentials credentials, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ManualCarrierAwb))
        {
            throw new CarrierBookingException("A carrier AWB is required for the manual carrier.");
        }
        return Task.FromResult(new CarrierBookingResult(request.ManualCarrierAwb.Trim(), request.ServiceLevel, null, null));
    }

    public Task CancelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<CarrierLabel?> GetLabelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken) =>
        Task.FromResult<CarrierLabel?>(null);

    public Task<IReadOnlyList<CarrierTrackingUpdate>> GetTrackingAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The manual carrier does not support polling.");

    public bool VerifyWebhook(IReadOnlyDictionary<string, string> headers, byte[] rawBody, string webhookSecret) => false;

    public IReadOnlyList<CarrierTrackingUpdate> ParseWebhook(byte[] rawBody) =>
        throw new NotSupportedException("The manual carrier does not support webhooks.");

    public bool IsKnownStatusCode(string carrierStatusCode) => Vocabulary.ContainsKey(carrierStatusCode);

    public ShipmentStatus? MapStatus(string carrierStatusCode, ShipmentDirection direction)
    {
        if (!Vocabulary.TryGetValue(carrierStatusCode, out var targets))
        {
            return null;
        }
        return direction == ShipmentDirection.Outbound ? targets.Outbound : targets.Inbound;
    }
}
```

`src/Modules/International/Infrastructure/Carriers/DataProtectionCarrierSecretProtector.cs`:

```csharp
using System.Text.Json;
using Courier.International.Application.Carriers;
using Microsoft.AspNetCore.DataProtection;

namespace Courier.International.Infrastructure.Carriers;

// Two purpose strings — Data Protection derives a distinct key per purpose, so credential
// ciphertext and webhook-secret ciphertext can never be decrypted as each other.
public sealed class DataProtectionCarrierSecretProtector : ICarrierSecretProtector
{
    private readonly IDataProtector _credentialsProtector;
    private readonly IDataProtector _webhookSecretProtector;

    public DataProtectionCarrierSecretProtector(IDataProtectionProvider provider)
    {
        _credentialsProtector = provider.CreateProtector("International.CarrierAccount.Credentials");
        _webhookSecretProtector = provider.CreateProtector("International.CarrierAccount.WebhookSecret");
    }

    public string ProtectCredentials(CarrierCredentials credentials) =>
        _credentialsProtector.Protect(JsonSerializer.Serialize(credentials.Values));

    public CarrierCredentials UnprotectCredentials(string? ciphertext)
    {
        if (ciphertext is null)
        {
            return CarrierCredentials.Empty;
        }
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(_credentialsProtector.Unprotect(ciphertext))
            ?? new Dictionary<string, string>();
        return new CarrierCredentials(values);
    }

    public string ProtectWebhookSecret(string secret) => _webhookSecretProtector.Protect(secret);

    public string UnprotectWebhookSecret(string ciphertext) => _webhookSecretProtector.Unprotect(ciphertext);
}
```

In `src/Api/Program.cs`, after `builder.Services.AddScoped<Courier.International.Application.InternationalShipmentQueryService>();` add:

```csharp
builder.Services.AddScoped<Courier.International.Application.Carriers.ICarrierProvider, Courier.International.Infrastructure.Carriers.ManualCarrierProvider>();
builder.Services.AddScoped<Courier.International.Application.Carriers.ICarrierProviderRegistry, Courier.International.Application.Carriers.CarrierProviderRegistry>();
builder.Services.AddScoped<Courier.International.Application.Carriers.ICarrierSecretProtector, Courier.International.Infrastructure.Carriers.DataProtectionCarrierSecretProtector>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/International.Tests` — Expected: PASS.
Run: `dotnet build Courier.slnx` — Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/International/ src/Api/Program.cs tests/International.Tests/
git commit -m "feat(international): add carrier port, registry, manual carrier and secret protection

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Carrier HTTP resilience pipeline

**Files:**
- Modify: `src/Modules/International/International.csproj` (package `Microsoft.Extensions.Http.Resilience`)
- Create: `src/Modules/International/Infrastructure/Carriers/CarrierHttpClientExtensions.cs`
- Create: `src/Modules/International/Application/Carriers/CarrierCallFailures.cs`
- Test: `tests/International.Tests/Carriers/CarrierHttpClientExtensionsTests.cs`

**Interfaces:**
- Produces: `public sealed class CarrierResilienceOptions { TimeSpan AttemptTimeout = 10s; TimeSpan TotalTimeout = 30s; int MaxRetryAttempts = 3; TimeSpan RetryBaseDelay = 1s; double FailureRatio = 0.5; TimeSpan SamplingDuration = 30s; int MinimumThroughput = 10; TimeSpan BreakDuration = 30s; }`
- Produces: `public static IHttpClientBuilder AddCarrierHttpClient<TClient, TImplementation>(this IServiceCollection services, Action<CarrierResilienceOptions>? configure = null)` — every network carrier adapter (I6 DHL, the test fake) registers through this, then as an `ICarrierProvider`.
- Produces: `public static class CarrierCallFailures { static bool IsCarrierUnavailable(Exception ex); static bool IsCarrierFailure(Exception ex); }` — used by Task 11 to map provider exceptions to 503/502.

- [ ] **Step 1: Add the package**

Run: `dotnet add src/Modules/International/International.csproj package Microsoft.Extensions.Http.Resilience`
Use the latest stable version compatible with `net10.0`; record the version in the task report. (Justification: first outbound HTTP integration that needs retry, circuit breaking and timeouts; this is Microsoft's supported Polly v8 integration for `HttpClient`.)

- [ ] **Step 2: Write the failing tests**

`tests/International.Tests/Carriers/CarrierHttpClientExtensionsTests.cs`:

```csharp
using System.Net;
using Courier.International.Application.Carriers;
using Courier.International.Infrastructure.Carriers;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;

namespace Courier.International.Tests.Carriers;

public class CarrierHttpClientExtensionsTests
{
    public sealed class ProbeClient
    {
        private readonly HttpClient _http;
        public ProbeClient(HttpClient http) => _http = http;
        public Task<HttpResponseMessage> PingAsync() => _http.GetAsync("http://carrier.test/ping");
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _script;
        private readonly HttpStatusCode _fallback;
        public int Calls { get; private set; }

        public ScriptedHandler(HttpStatusCode fallback, params HttpStatusCode[] script)
        {
            _fallback = fallback;
            _script = new Queue<HttpStatusCode>(script);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(_script.Count > 0 ? _script.Dequeue() : _fallback));
        }
    }

    private static ProbeClient BuildClient(ScriptedHandler handler, Action<CarrierResilienceOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCarrierHttpClient<ProbeClient, ProbeClient>(configure).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<ProbeClient>();
    }

    [Fact]
    public async Task TransientServerError_IsRetried_ThenSucceeds()
    {
        var handler = new ScriptedHandler(HttpStatusCode.OK, HttpStatusCode.InternalServerError);
        var client = BuildClient(handler, o => o.RetryBaseDelay = TimeSpan.Zero);

        var response = await client.PingAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ClientError_IsNotRetried()
    {
        var handler = new ScriptedHandler(HttpStatusCode.BadRequest);
        var client = BuildClient(handler, o => o.RetryBaseDelay = TimeSpan.Zero);

        var response = await client.PingAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RepeatedFailures_OpenTheCircuit_AndLaterCallsNeverReachTheCarrier()
    {
        var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable);
        var client = BuildClient(handler, o =>
        {
            o.RetryBaseDelay = TimeSpan.Zero;
            o.MaxRetryAttempts = 1;
            o.MinimumThroughput = 2;
            o.SamplingDuration = TimeSpan.FromSeconds(30);
            o.BreakDuration = TimeSpan.FromSeconds(30);
        });

        BrokenCircuitException? broken = null;
        for (var i = 0; i < 5 && broken is null; i++)
        {
            try
            {
                await client.PingAsync();
            }
            catch (BrokenCircuitException ex)
            {
                broken = ex;
            }
        }

        Assert.NotNull(broken);
        Assert.True(CarrierCallFailures.IsCarrierUnavailable(broken));
        var callsWhenOpen = handler.Calls;
        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.PingAsync());
        Assert.Equal(callsWhenOpen, handler.Calls);
    }

    [Fact]
    public void Failures_AreClassified()
    {
        Assert.True(CarrierCallFailures.IsCarrierFailure(new HttpRequestException("down")));
        Assert.True(CarrierCallFailures.IsCarrierFailure(new CarrierBookingException("rejected")));
        Assert.True(CarrierCallFailures.IsCarrierFailure(new TaskCanceledException("timeout")));
        Assert.False(CarrierCallFailures.IsCarrierFailure(new InvalidOperationException("bug")));
        Assert.False(CarrierCallFailures.IsCarrierUnavailable(new HttpRequestException("down")));
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/International.Tests --filter "FullyQualifiedName~CarrierHttpClientExtensionsTests"`
Expected: FAIL — `AddCarrierHttpClient`/`CarrierCallFailures` do not exist.

- [ ] **Step 4: Implement**

`src/Modules/International/Infrastructure/Carriers/CarrierHttpClientExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Courier.International.Infrastructure.Carriers;

// Defaults are the I4 spec §5.9 values; tests shrink them.
public sealed class CarrierResilienceOptions
{
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);
    public double FailureRatio { get; set; } = 0.5;
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);
    public int MinimumThroughput { get; set; } = 10;
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

public static class CarrierHttpClientExtensions
{
    public const string PipelineName = "carrier";

    // Registers a typed HttpClient for a carrier adapter with the standard carrier pipeline:
    // total timeout -> retry (5xx/408/429/HttpRequestException/timeouts, exponential + jitter) ->
    // circuit breaker -> per-attempt timeout. Booking calls are safe to retry only because
    // adapters send our idempotency key to the carrier.
    public static IHttpClientBuilder AddCarrierHttpClient<TClient, TImplementation>(this IServiceCollection services, Action<CarrierResilienceOptions>? configure = null)
        where TClient : class
        where TImplementation : class, TClient
    {
        var options = new CarrierResilienceOptions();
        configure?.Invoke(options);

        var builder = services.AddHttpClient<TClient, TImplementation>();
        builder.AddResilienceHandler(PipelineName, pipeline =>
        {
            pipeline.AddTimeout(options.TotalTimeout);
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.RetryBaseDelay
            });
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = options.FailureRatio,
                SamplingDuration = options.SamplingDuration,
                MinimumThroughput = options.MinimumThroughput,
                BreakDuration = options.BreakDuration
            });
            pipeline.AddTimeout(options.AttemptTimeout);
        });
        return builder;
    }
}
```

`src/Modules/International/Application/Carriers/CarrierCallFailures.cs`:

```csharp
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Courier.International.Application.Carriers;

// How a provider-call exception maps to the API: open circuit -> 503 CARRIER_UNAVAILABLE; the
// carrier refusing, erroring or timing out -> 502 CARRIER_BOOKING_FAILED. Anything else is a bug
// and propagates (500).
public static class CarrierCallFailures
{
    public static bool IsCarrierUnavailable(Exception ex) => ex is BrokenCircuitException;

    public static bool IsCarrierFailure(Exception ex) =>
        ex is CarrierBookingException or HttpRequestException or TimeoutRejectedException or TaskCanceledException;
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/International.Tests --filter "FullyQualifiedName~CarrierHttpClientExtensionsTests"` — Expected: PASS (4 tests).
Run: `dotnet build Courier.slnx` — Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/International/ tests/International.Tests/
git commit -m "feat(international): add carrier HTTP resilience pipeline

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 10: Carrier account management

**Files:**
- Create: `src/Modules/International/Application/CarrierDtos.cs`
- Create: `src/Modules/International/Application/CarrierAccountService.cs`
- Create: `src/Modules/International/Presentation/InternationalEndpointErrors.cs`
- Create: `src/Modules/International/Presentation/CarrierAccountEndpoints.cs`
- Modify: `src/Api/Program.cs` (DI + `app.MapCarrierAccountEndpoints();` next to `app.MapInternationalEndpoints();`)
- Create: `tests/International.Tests/CarrierTestSupport.cs`
- Test: `tests/International.Tests/CarrierAccountServiceTests.cs`, `tests/Api.Tests/International/CarrierAccountEndpointsTests.cs`

**Interfaces:**
- Consumes: Task 8 `ICarrierProviderRegistry`, `ICarrierSecretProtector`, `CarrierCredentials`.
- Produces (International.Application):
  - Exceptions: `CarrierAccountValidationException(string)` (→400), `CarrierBookingFailedException(string)` (→502), `CarrierUnavailableException(string)` (→503).
  - `record CreateCarrierAccountRequest(string CarrierCode, string DisplayName, Dictionary<string, string>? Credentials, bool PollingEnabled, bool GenerateWebhookSecret)`
  - `record UpdateCarrierAccountRequest(string DisplayName, bool IsActive, bool PollingEnabled)`
  - `record ReplaceCarrierCredentialsRequest(Dictionary<string, string> Credentials)`
  - `record CarrierAccountResponse(Guid Id, string CarrierCode, string DisplayName, bool IsActive, bool PollingEnabled, bool HasCredentials, bool HasWebhookSecret, string WebhookPath, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)`
  - `record CarrierAccountSecretResponse(CarrierAccountResponse Account, string? WebhookSecret)`
  - `CarrierAccountService`: `ListAsync`, `GetAsync(Guid)`, `CreateAsync(request, actingUser)`, `UpdateAsync(Guid, request, actingUser)`, `ReplaceCredentialsAsync(Guid, request, actingUser)`, `RotateWebhookSecretAsync(Guid, actingUser)`.
- Produces (International.Presentation): `internal static class InternationalEndpointErrors { static Task<IResult> ExecuteAsync(Func<Task<IResult>> action); }` — every I4 route handler wraps its body in it.
- Produces (International.Tests): `CarrierTestDbContext`, `CarrierTestData`, `TestCarrierProvider`, `RecordingJobScheduler`, `InMemoryObjectStorage` (later tasks extend `RecordingJobScheduler` usage).

- [ ] **Step 1: Create the unit-test support file**

`tests/International.Tests/CarrierTestSupport.cs`:

```csharp
using System.Text.Json;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.International.Infrastructure;
using Courier.SharedKernel;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Tests;

internal sealed class CarrierTestDbContext : DbContext
{
    public CarrierTestDbContext(DbContextOptions<CarrierTestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new InternationalLegConfiguration());
        modelBuilder.ApplyConfiguration(new CustomsItemConfiguration());
        modelBuilder.ApplyConfiguration(new CarrierAccountConfiguration());
        modelBuilder.ApplyConfiguration(new CarrierBookingConfiguration());
        modelBuilder.ApplyConfiguration(new CarrierTrackingEventConfiguration());
    }
}

internal static class CarrierTestData
{
    public static CarrierTestDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<CarrierTestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new CarrierTestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    public static async Task<CarrierAccount> SeedAccountAsync(DbContext dbContext, Guid organizationId, string carrierCode,
        bool isActive = true, bool pollingEnabled = false, string? encryptedWebhookSecret = null, string? encryptedCredentials = null)
    {
        var account = new CarrierAccount
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, CarrierCode = carrierCode, DisplayName = carrierCode,
            IsActive = isActive, PollingEnabled = pollingEnabled, EncryptedWebhookSecret = encryptedWebhookSecret,
            EncryptedCredentials = encryptedCredentials, WebhookPublicId = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<CarrierAccount>().Add(account);
        await dbContext.SaveChangesAsync();
        return account;
    }

    public static async Task<InternationalLeg> SeedLegAsync(DbContext dbContext, Guid organizationId, Guid shipmentId)
    {
        var leg = new InternationalLeg
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, ShipmentId = shipmentId, ProvisionalTotalChargeCurrency = "NPR",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<InternationalLeg>().Add(leg);
        await dbContext.SaveChangesAsync();
        return leg;
    }

    public static async Task<CarrierBooking> SeedBookingAsync(DbContext dbContext, Guid organizationId, Guid legId, Guid accountId,
        CarrierBookingStatus status, string? carrierAwb, CarrierBookingDirection direction = CarrierBookingDirection.Outbound)
    {
        var booking = new CarrierBooking
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, InternationalLegId = legId, CarrierAccountId = accountId,
            Direction = direction, Status = status, CarrierAwb = carrierAwb, IdempotencyKey = Guid.NewGuid().ToString("N"),
            BookedAt = status == CarrierBookingStatus.Booked ? DateTimeOffset.UtcNow : null,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<CarrierBooking>().Add(booking);
        await dbContext.SaveChangesAsync();
        return booking;
    }

    public static async Task<CarrierTrackingEvent> SeedEventAsync(DbContext dbContext, Guid organizationId, Guid accountId, string carrierAwb,
        string statusCode, DateTimeOffset occurredAt, CarrierEventProcessingStatus processingStatus = CarrierEventProcessingStatus.Processing)
    {
        var evt = new CarrierTrackingEvent
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, CarrierAccountId = accountId, CarrierAwb = carrierAwb,
            CarrierEventId = $"evt-{Guid.NewGuid():N}", CarrierStatusCode = statusCode, OccurredAt = occurredAt,
            Source = CarrierEventSource.Webhook, ReceivedAt = DateTimeOffset.UtcNow, ProcessingStatus = processingStatus
        };
        dbContext.Set<CarrierTrackingEvent>().Add(evt);
        await dbContext.SaveChangesAsync();
        return evt;
    }
}

// Configurable in-memory carrier for unit tests. Webhook "signature" = the X-Test-Signature
// header equal to the secret; webhook body = a JSON array of CarrierTrackingUpdate (web casing).
internal sealed class TestCarrierProvider : ICarrierProvider
{
    public const string SignatureHeader = "X-Test-Signature";

    private static readonly Dictionary<string, (ShipmentStatus? Outbound, ShipmentStatus? Inbound)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TRANSIT"] = (ShipmentStatus.InInternationalTransit, ShipmentStatus.InInternationalTransit),
        ["HOLD"] = (ShipmentStatus.CustomsHold, ShipmentStatus.CustomsHold),
        ["CLEARANCE"] = (null, ShipmentStatus.CustomsClearance),
        ["DELIVERED"] = (ShipmentStatus.DeliveredAbroad, null),
        ["INFO"] = (null, null)
    };

    public TestCarrierProvider(string code = "TESTCARRIER", bool webhooks = true, bool polling = true, bool manualAwb = false)
    {
        CarrierCode = code;
        Capabilities = new CarrierCapabilities(webhooks, polling, manualAwb, SupportsLabels: true);
    }

    public string CarrierCode { get; }
    public CarrierCapabilities Capabilities { get; }
    public Func<CarrierBookingRequest, CarrierBookingResult> Book { get; set; } =
        request => new CarrierBookingResult($"TC-{request.PlatformAwb}", request.ServiceLevel, null, "{\"ok\":true}");
    public Exception? BookException { get; set; }
    public int BookCalls { get; private set; }
    public List<string> CancelledAwbs { get; } = new();
    public List<CarrierTrackingUpdate> TrackingUpdates { get; } = new();
    public Exception? TrackingException { get; set; }

    public Task<CarrierBookingResult> BookAsync(CarrierBookingRequest request, CarrierCredentials credentials, string idempotencyKey, CancellationToken cancellationToken)
    {
        BookCalls++;
        if (BookException is not null)
        {
            throw BookException;
        }
        return Task.FromResult(Book(request));
    }

    public Task CancelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken)
    {
        CancelledAwbs.Add(carrierAwb);
        return Task.CompletedTask;
    }

    public Task<CarrierLabel?> GetLabelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken) => Task.FromResult<CarrierLabel?>(null);

    public Task<IReadOnlyList<CarrierTrackingUpdate>> GetTrackingAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken)
    {
        if (TrackingException is not null)
        {
            throw TrackingException;
        }
        return Task.FromResult<IReadOnlyList<CarrierTrackingUpdate>>(TrackingUpdates.Where(u => u.CarrierAwb == carrierAwb).ToList());
    }

    public bool VerifyWebhook(IReadOnlyDictionary<string, string> headers, byte[] rawBody, string webhookSecret) =>
        headers.TryGetValue(SignatureHeader, out var signature) && signature == webhookSecret;

    public IReadOnlyList<CarrierTrackingUpdate> ParseWebhook(byte[] rawBody)
    {
        try
        {
            return JsonSerializer.Deserialize<List<CarrierTrackingUpdate>>(rawBody, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new CarrierWebhookPayloadException("Empty webhook body.");
        }
        catch (JsonException ex)
        {
            throw new CarrierWebhookPayloadException(ex.Message);
        }
    }

    public bool IsKnownStatusCode(string carrierStatusCode) => Map.ContainsKey(carrierStatusCode);

    public ShipmentStatus? MapStatus(string carrierStatusCode, ShipmentDirection direction) =>
        Map.TryGetValue(carrierStatusCode, out var targets) ? (direction == ShipmentDirection.Outbound ? targets.Outbound : targets.Inbound) : null;
}

internal sealed class InMemoryObjectStorage : IObjectStorage
{
    public Dictionary<string, (byte[] Content, string ContentType)> Objects { get; } = new();

    public async Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var key = $"{keyPrefix}/{Guid.NewGuid():N}";
        Objects[key] = (buffer.ToArray(), contentType);
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new MemoryStream(Objects[key].Content));
}
```

(`RecordingJobScheduler` is added in Task 11, when `ICarrierJobScheduler` exists.)

- [ ] **Step 2: Write the failing service tests**

`tests/International.Tests/CarrierAccountServiceTests.cs`:

```csharp
using System.Security.Claims;
using Courier.International.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.International.Infrastructure.Carriers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Tests;

public class CarrierAccountServiceTests
{
    private sealed record Harness(CarrierTestDbContext DbContext, Guid OrganizationId, CarrierAccountService Service, RecordingAuditLogger Audit, ICarrierSecretProtector Protector);

    private static Harness CreateHarness()
    {
        var dbContext = CarrierTestData.NewContext();
        var organizationId = Guid.NewGuid();
        var audit = new RecordingAuditLogger();
        var protector = new DataProtectionCarrierSecretProtector(new EphemeralDataProtectionProvider());
        var registry = new CarrierProviderRegistry(new ICarrierProvider[] { new ManualCarrierProvider(), new TestCarrierProvider() });
        var service = new CarrierAccountService(dbContext, new FixedTenantProvider(organizationId), audit, registry, protector);
        return new Harness(dbContext, organizationId, service, audit, protector);
    }

    private static ClaimsPrincipal ActingUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    [Fact]
    public async Task CreateAsync_WithCredentialsAndWebhookSecret_EncryptsBothAndReturnsSecretOnce()
    {
        var h = CreateHarness();

        var result = await h.Service.CreateAsync(new CreateCarrierAccountRequest("testcarrier", "  Test Carrier  ",
            new Dictionary<string, string> { ["apiKey"] = "sk_live_12345" }, PollingEnabled: true, GenerateWebhookSecret: true), ActingUser());

        Assert.Equal("TESTCARRIER", result.Account.CarrierCode);
        Assert.Equal("Test Carrier", result.Account.DisplayName);
        Assert.True(result.Account.HasCredentials);
        Assert.True(result.Account.HasWebhookSecret);
        Assert.False(string.IsNullOrWhiteSpace(result.WebhookSecret));
        Assert.StartsWith("/api/v1/carrier-webhooks/", result.Account.WebhookPath);

        var stored = await h.DbContext.Set<CarrierAccount>().SingleAsync();
        Assert.Equal(h.OrganizationId, stored.OrganizationId);
        Assert.DoesNotContain("sk_live_12345", stored.EncryptedCredentials);
        Assert.Equal("sk_live_12345", h.Protector.UnprotectCredentials(stored.EncryptedCredentials).Values["apiKey"]);
        Assert.Equal(result.WebhookSecret, h.Protector.UnprotectWebhookSecret(stored.EncryptedWebhookSecret!));
        Assert.Contains(h.Audit.Entries, e => e.Action == "carrier_account.created" && !(e.After ?? "").Contains("sk_live_12345"));
    }

    [Fact]
    public async Task CreateAsync_ManualCarrier_HasNoSecret()
    {
        var h = CreateHarness();

        var result = await h.Service.CreateAsync(new CreateCarrierAccountRequest("MANUAL", "Manual", null, false, false), ActingUser());

        Assert.Null(result.WebhookSecret);
        Assert.False(result.Account.HasCredentials);
        Assert.False(result.Account.HasWebhookSecret);
    }

    [Theory]
    [InlineData("DHL_EXPRESS", "Name", false, false)]  // not registered
    [InlineData("MANUAL", "", false, false)]           // blank name
    [InlineData("MANUAL", "Manual", true, false)]      // manual cannot poll
    [InlineData("MANUAL", "Manual", false, true)]      // manual has no webhooks
    public async Task CreateAsync_Invalid_ThrowsAccountValidation(string code, string name, bool polling, bool webhookSecret)
    {
        var h = CreateHarness();

        await Assert.ThrowsAsync<CarrierAccountValidationException>(() =>
            h.Service.CreateAsync(new CreateCarrierAccountRequest(code, name, null, polling, webhookSecret), ActingUser()));
        Assert.Empty(h.DbContext.Set<CarrierAccount>());
    }

    [Fact]
    public async Task CreateAsync_BlankCredentialValue_ThrowsAccountValidation()
    {
        var h = CreateHarness();

        await Assert.ThrowsAsync<CarrierAccountValidationException>(() =>
            h.Service.CreateAsync(new CreateCarrierAccountRequest("TESTCARRIER", "Test", new Dictionary<string, string> { ["apiKey"] = " " }, false, false), ActingUser()));
    }

    [Fact]
    public async Task UpdateAsync_DeactivatesAndRenames()
    {
        var h = CreateHarness();
        var created = await h.Service.CreateAsync(new CreateCarrierAccountRequest("MANUAL", "Manual", null, false, false), ActingUser());

        var updated = await h.Service.UpdateAsync(created.Account.Id, new UpdateCarrierAccountRequest("Manual (old)", IsActive: false, PollingEnabled: false), ActingUser());

        Assert.False(updated!.IsActive);
        Assert.Equal("Manual (old)", updated.DisplayName);
    }

    [Fact]
    public async Task RotateWebhookSecretAsync_ReplacesTheSecret()
    {
        var h = CreateHarness();
        var created = await h.Service.CreateAsync(new CreateCarrierAccountRequest("TESTCARRIER", "Test", null, false, true), ActingUser());

        var rotated = await h.Service.RotateWebhookSecretAsync(created.Account.Id, ActingUser());

        Assert.NotEqual(created.WebhookSecret, rotated!.WebhookSecret);
        var stored = await h.DbContext.Set<CarrierAccount>().SingleAsync();
        Assert.Equal(rotated.WebhookSecret, h.Protector.UnprotectWebhookSecret(stored.EncryptedWebhookSecret!));
    }

    [Fact]
    public async Task RotateWebhookSecretAsync_ManualCarrier_ThrowsAccountValidation()
    {
        var h = CreateHarness();
        var created = await h.Service.CreateAsync(new CreateCarrierAccountRequest("MANUAL", "Manual", null, false, false), ActingUser());

        await Assert.ThrowsAsync<CarrierAccountValidationException>(() => h.Service.RotateWebhookSecretAsync(created.Account.Id, ActingUser()));
    }

    [Fact]
    public async Task UnknownAccount_ReturnsNull()
    {
        var h = CreateHarness();

        Assert.Null(await h.Service.GetAsync(Guid.NewGuid()));
        Assert.Null(await h.Service.UpdateAsync(Guid.NewGuid(), new UpdateCarrierAccountRequest("X", true, false), ActingUser()));
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/International.Tests --filter "FullyQualifiedName~CarrierAccountServiceTests"`
Expected: FAIL — `CarrierAccountService` etc. do not exist.

- [ ] **Step 4: Implement DTOs and the service**

`src/Modules/International/Application/CarrierDtos.cs`:

```csharp
namespace Courier.International.Application;

public class CarrierAccountValidationException : Exception
{
    public CarrierAccountValidationException(string message) : base(message) { }
}

// The carrier refused, errored or timed out on a booking/cancel call (502). The message is the
// sanitized reason, safe to return to staff.
public class CarrierBookingFailedException : Exception
{
    public CarrierBookingFailedException(string message) : base(message) { }
}

// The carrier's circuit breaker is open (503) — try again later.
public class CarrierUnavailableException : Exception
{
    public CarrierUnavailableException(string message) : base(message) { }
}

public record CreateCarrierAccountRequest(string CarrierCode, string DisplayName, Dictionary<string, string>? Credentials, bool PollingEnabled, bool GenerateWebhookSecret);

public record UpdateCarrierAccountRequest(string DisplayName, bool IsActive, bool PollingEnabled);

public record ReplaceCarrierCredentialsRequest(Dictionary<string, string> Credentials);

// Never carries secret values — only whether they are configured.
public record CarrierAccountResponse(
    Guid Id, string CarrierCode, string DisplayName, bool IsActive, bool PollingEnabled,
    bool HasCredentials, bool HasWebhookSecret, string WebhookPath, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

// WebhookSecret is present only in the create/rotate response that generated it — shown once.
public record CarrierAccountSecretResponse(CarrierAccountResponse Account, string? WebhookSecret);
```

`src/Modules/International/Application/CarrierAccountService.cs`:

```csharp
using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Courier.Audit.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Application;

public class CarrierAccountService
{
    private const int MaxDisplayNameLength = 200;
    private const int MaxCredentialEntries = 20;
    private const int MaxCredentialKeyLength = 100;
    private const int MaxCredentialValueLength = 2000;

    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly ICarrierProviderRegistry _registry;
    private readonly ICarrierSecretProtector _secretProtector;

    public CarrierAccountService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger,
        ICarrierProviderRegistry registry, ICarrierSecretProtector secretProtector)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _registry = registry;
        _secretProtector = secretProtector;
    }

    public async Task<IReadOnlyList<CarrierAccountResponse>> ListAsync(CancellationToken cancellationToken = default) =>
        (await _dbContext.Set<CarrierAccount>().OrderBy(a => a.DisplayName).ToListAsync(cancellationToken)).Select(ToResponse).ToList();

    public async Task<CarrierAccountResponse?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.Set<CarrierAccount>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return account is null ? null : ToResponse(account);
    }

    public async Task<CarrierAccountSecretResponse> CreateAsync(CreateCarrierAccountRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new CarrierAccountValidationException("No active organization context.");

        var provider = _registry.Find(request.CarrierCode?.Trim() ?? string.Empty)
            ?? throw new CarrierAccountValidationException($"Unknown carrier code '{request.CarrierCode}'. Registered: {string.Join(", ", _registry.RegisteredCodes)}.");
        var displayName = ValidateDisplayName(request.DisplayName);
        ValidateCapabilities(provider, request.PollingEnabled, request.GenerateWebhookSecret);
        var credentials = request.Credentials is null ? null : ValidateCredentials(request.Credentials);

        string? webhookSecret = request.GenerateWebhookSecret ? NewSecret() : null;
        var now = DateTimeOffset.UtcNow;
        var account = new CarrierAccount
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CarrierCode = provider.CarrierCode,
            DisplayName = displayName,
            IsActive = true,
            PollingEnabled = request.PollingEnabled,
            EncryptedCredentials = credentials is null ? null : _secretProtector.ProtectCredentials(credentials),
            EncryptedWebhookSecret = webhookSecret is null ? null : _secretProtector.ProtectWebhookSecret(webhookSecret),
            WebhookPublicId = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24)),
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.Set<CarrierAccount>().Add(account);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = ToResponse(account);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "carrier_account.created", "CarrierAccount", account.Id.ToString(),
            After: JsonSerializer.Serialize(response)), cancellationToken);
        return new CarrierAccountSecretResponse(response, webhookSecret);
    }

    public async Task<CarrierAccountResponse?> UpdateAsync(Guid id, UpdateCarrierAccountRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.Set<CarrierAccount>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return null;
        }
        var provider = _registry.Find(account.CarrierCode)
            ?? throw new CarrierAccountValidationException($"Carrier '{account.CarrierCode}' is no longer registered.");
        var displayName = ValidateDisplayName(request.DisplayName);
        ValidateCapabilities(provider, request.PollingEnabled, generateWebhookSecret: false);

        var before = JsonSerializer.Serialize(ToResponse(account));
        account.DisplayName = displayName;
        account.IsActive = request.IsActive;
        account.PollingEnabled = request.PollingEnabled;
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = ToResponse(account);
        await _auditLogger.LogAsync(new AuditEntry(account.OrganizationId, actingUser.GetUserId(), "carrier_account.updated", "CarrierAccount", account.Id.ToString(),
            Before: before, After: JsonSerializer.Serialize(response)), cancellationToken);
        return response;
    }

    public async Task<CarrierAccountResponse?> ReplaceCredentialsAsync(Guid id, ReplaceCarrierCredentialsRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.Set<CarrierAccount>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return null;
        }
        var credentials = ValidateCredentials(request.Credentials ?? new Dictionary<string, string>());
        account.EncryptedCredentials = credentials.Values.Count == 0 ? null : _secretProtector.ProtectCredentials(credentials);
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Audited without values — only which keys were set.
        await _auditLogger.LogAsync(new AuditEntry(account.OrganizationId, actingUser.GetUserId(), "carrier_account.credentials_replaced", "CarrierAccount", account.Id.ToString(),
            After: JsonSerializer.Serialize(new { keys = credentials.Values.Keys.OrderBy(k => k).ToList() })), cancellationToken);
        return ToResponse(account);
    }

    public async Task<CarrierAccountSecretResponse?> RotateWebhookSecretAsync(Guid id, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var account = await _dbContext.Set<CarrierAccount>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return null;
        }
        var provider = _registry.Find(account.CarrierCode)
            ?? throw new CarrierAccountValidationException($"Carrier '{account.CarrierCode}' is no longer registered.");
        if (!provider.Capabilities.SupportsWebhooks)
        {
            throw new CarrierAccountValidationException($"Carrier '{account.CarrierCode}' does not support webhooks.");
        }

        var secret = NewSecret();
        account.EncryptedWebhookSecret = _secretProtector.ProtectWebhookSecret(secret);
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(account.OrganizationId, actingUser.GetUserId(), "carrier_account.webhook_secret_rotated", "CarrierAccount", account.Id.ToString()), cancellationToken);
        return new CarrierAccountSecretResponse(ToResponse(account), secret);
    }

    public static CarrierAccountResponse ToResponse(CarrierAccount account) => new(
        account.Id, account.CarrierCode, account.DisplayName, account.IsActive, account.PollingEnabled,
        account.EncryptedCredentials is not null, account.EncryptedWebhookSecret is not null,
        $"/api/v1/carrier-webhooks/{account.WebhookPublicId}", account.CreatedAt, account.UpdatedAt);

    private static string NewSecret() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static string ValidateDisplayName(string? displayName)
    {
        var trimmed = displayName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxDisplayNameLength)
        {
            throw new CarrierAccountValidationException($"Display name is required and must be at most {MaxDisplayNameLength} characters.");
        }
        return trimmed;
    }

    private static void ValidateCapabilities(ICarrierProvider provider, bool pollingEnabled, bool generateWebhookSecret)
    {
        if (pollingEnabled && !provider.Capabilities.SupportsPolling)
        {
            throw new CarrierAccountValidationException($"Carrier '{provider.CarrierCode}' does not support polling.");
        }
        if (generateWebhookSecret && !provider.Capabilities.SupportsWebhooks)
        {
            throw new CarrierAccountValidationException($"Carrier '{provider.CarrierCode}' does not support webhooks.");
        }
    }

    private static CarrierCredentials ValidateCredentials(Dictionary<string, string> credentials)
    {
        if (credentials.Count > MaxCredentialEntries)
        {
            throw new CarrierAccountValidationException($"At most {MaxCredentialEntries} credential entries are allowed.");
        }
        foreach (var (key, value) in credentials)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > MaxCredentialKeyLength)
            {
                throw new CarrierAccountValidationException($"Credential names are required and must be at most {MaxCredentialKeyLength} characters.");
            }
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxCredentialValueLength)
            {
                throw new CarrierAccountValidationException($"Credential '{key}' must have a value of at most {MaxCredentialValueLength} characters.");
            }
        }
        return new CarrierCredentials(new Dictionary<string, string>(credentials));
    }
}
```

Run: `dotnet test tests/International.Tests --filter "FullyQualifiedName~CarrierAccountServiceTests"` — Expected: PASS.

- [ ] **Step 5: Implement the endpoints**

`src/Modules/International/Presentation/InternationalEndpointErrors.cs`:

```csharp
using Courier.International.Application;
using Courier.Shipments.Application;
using Microsoft.AspNetCore.Http;

namespace Courier.International.Presentation;

// One exception -> error-contract mapping for every I4 route (spec §7). Order matters:
// ShipmentInvalidTransitionException derives from ShipmentValidationException.
internal static class InternationalEndpointErrors
{
    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (InternationalShipmentValidationException ex)
        {
            return Error(StatusCodes.Status400BadRequest, "INTERNATIONAL_SHIPMENT_VALIDATION_FAILED", ex.Message);
        }
        catch (CarrierAccountValidationException ex)
        {
            return Error(StatusCodes.Status400BadRequest, "CARRIER_ACCOUNT_VALIDATION_FAILED", ex.Message);
        }
        catch (CarrierBookingFailedException ex)
        {
            return Error(StatusCodes.Status502BadGateway, "CARRIER_BOOKING_FAILED", ex.Message);
        }
        catch (CarrierUnavailableException ex)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "CARRIER_UNAVAILABLE", ex.Message);
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
            return Error(StatusCodes.Status400BadRequest, "SHIPMENT_VALIDATION_FAILED", ex.Message);
        }
    }

    public static IResult Error(int statusCode, string code, string message) =>
        Results.Json(new { error = new { code, message } }, statusCode: statusCode);
}
```

`src/Modules/International/Presentation/CarrierAccountEndpoints.cs`:

```csharp
using Courier.International.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.International.Presentation;

public static class CarrierAccountEndpoints
{
    public static void MapCarrierAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/international/carrier-accounts")
            .WithTags("International Carrier Accounts")
            .RequireAuthorization();

        group.MapGet("/", async (CarrierAccountService service, HttpContext httpContext) =>
            Results.Ok(await service.ListAsync(httpContext.RequestAborted)))
            .RequirePermission("international.carrier.manage");

        group.MapGet("/{id:guid}", async (Guid id, CarrierAccountService service, HttpContext httpContext) =>
        {
            var result = await service.GetAsync(id, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("international.carrier.manage");

        group.MapPost("/", (CreateCarrierAccountRequest request, CarrierAccountService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.CreateAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/international/carrier-accounts/{result.Account.Id}", result);
            })).RequirePermission("international.carrier.manage");

        group.MapPut("/{id:guid}", (Guid id, UpdateCarrierAccountRequest request, CarrierAccountService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.UpdateAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.carrier.manage");

        group.MapPost("/{id:guid}/credentials", (Guid id, ReplaceCarrierCredentialsRequest request, CarrierAccountService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.ReplaceCredentialsAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.carrier.manage");

        group.MapPost("/{id:guid}/webhook-secret/rotate", (Guid id, CarrierAccountService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.RotateWebhookSecretAsync(id, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.carrier.manage");
    }
}
```

In `src/Api/Program.cs`: register `builder.Services.AddScoped<Courier.International.Application.CarrierAccountService>();` with the other International services, and add `app.MapCarrierAccountEndpoints();` directly after `app.MapInternationalEndpoints();`.

- [ ] **Step 6: Write the API tests**

`tests/Api.Tests/International/CarrierAccountEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.International.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierAccountEndpointsTests : InternationalApiTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public CarrierAccountEndpointsTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private static object ManualAccountBody(Dictionary<string, string>? credentials = null) => new
    {
        carrierCode = "MANUAL",
        displayName = "Manual carrier",
        credentials,
        pollingEnabled = false,
        generateWebhookSecret = false
    };

    [Fact]
    public async Task CreateListUpdate_ManualAccount_NeverReturnsCredentialValues()
    {
        var org = await SetUpOrgAsync("acct-crud");

        var create = await org.Client.PostAsJsonAsync("/api/v1/international/carrier-accounts",
            ManualAccountBody(new Dictionary<string, string> { ["contact"] = "ops-desk-secret-9" }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var createBody = await create.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ops-desk-secret-9", createBody);
        var created = JsonSerializer.Deserialize<CarrierAccountSecretResponse>(createBody, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(created.Account.HasCredentials);
        Assert.Null(created.WebhookSecret);

        var list = await org.Client.GetFromJsonAsync<List<CarrierAccountResponse>>("/api/v1/international/carrier-accounts");
        Assert.Single(list!, a => a.Id == created.Account.Id);

        var update = await org.Client.PutAsJsonAsync($"/api/v1/international/carrier-accounts/{created.Account.Id}",
            new { displayName = "Manual (retired)", isActive = false, pollingEnabled = false });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.False((await update.Content.ReadFromJsonAsync<CarrierAccountResponse>())!.IsActive);
    }

    [Fact]
    public async Task Create_UnknownCarrierCode_Returns400WithAccountValidationCode()
    {
        var org = await SetUpOrgAsync("acct-unknown");

        var response = await org.Client.PostAsJsonAsync("/api/v1/international/carrier-accounts",
            new { carrierCode = "NOPE", displayName = "X", credentials = (object?)null, pollingEnabled = false, generateWebhookSecret = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CARRIER_ACCOUNT_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OtherTenantsAccount_Returns404()
    {
        var owner = await SetUpOrgAsync("acct-owner");
        var other = await SetUpOrgAsync("acct-other");
        var create = await owner.Client.PostAsJsonAsync("/api/v1/international/carrier-accounts", ManualAccountBody());
        var created = (await create.Content.ReadFromJsonAsync<CarrierAccountSecretResponse>())!;

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/v1/international/carrier-accounts/{created.Account.Id}")).StatusCode);
        var update = await other.Client.PutAsJsonAsync($"/api/v1/international/carrier-accounts/{created.Account.Id}",
            new { displayName = "Hijacked", isActive = false, pollingEnabled = false });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
    }

    [Fact]
    public async Task WithoutCarrierManagePermission_Returns403()
    {
        var org = await SetUpOrgAsync("acct-403");
        var client = await SeedUserClientAsync(org.Auth, new[] { "international.read" }, null);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/international/carrier-accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/international/carrier-accounts", ManualAccountBody())).StatusCode);
    }
}
```

- [ ] **Step 7: Run tests**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CarrierAccountEndpointsTests"` — Expected: PASS (4 tests).
Run: `dotnet build Courier.slnx` — Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/International/ src/Api/Program.cs tests/International.Tests/ tests/Api.Tests/International/
git commit -m "feat(international): add carrier account management with encrypted secrets

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Tracking event storage, processing and polling

**Files:**
- Create: `src/Modules/International/Application/Carriers/ICarrierJobScheduler.cs`
- Create: `src/Modules/International/Application/Carriers/CarrierTrackingWindow.cs`
- Create: `src/Modules/International/Application/CarrierTrackingEventWriter.cs`
- Create: `src/Modules/International/Application/CarrierEventApplier.cs`
- Create: `src/Modules/International/Application/CarrierEventProcessorJob.cs`
- Create: `src/Modules/International/Application/CarrierBookingPoller.cs`
- Create: `src/Modules/International/Application/CarrierTrackingPollJob.cs`
- Create: `src/Modules/International/Infrastructure/Carriers/HangfireCarrierJobScheduler.cs`
- Modify: `src/Modules/International/International.csproj` (package `Hangfire.Core` 1.8.24, same version as `Platform.csproj`)
- Modify: `src/Api/Program.cs` (DI + two recurring jobs)
- Modify: `tests/International.Tests/CarrierTestSupport.cs` (+ `RecordingJobScheduler`), `tests/International.Tests/InternationalShipmentServiceTests.cs` (`RecordingGateway.ApplyCarrierStatusAsync`)
- Test: `tests/International.Tests/CarrierTrackingEventWriterTests.cs`, `CarrierEventApplierTests.cs`, `CarrierBookingPollerTests.cs`; `tests/Api.Tests/International/CarrierEventProcessingTests.cs`

**Interfaces:**
- Consumes: Task 2 `ITenantScopeFactory`; Task 4 `IShipmentInternationalGateway.ApplyCarrierStatusAsync`; Task 8 contracts; Task 3 `ShipmentScanInfo.Direction`.
- Produces:
  - `interface ICarrierJobScheduler { void EnqueueProcessing(); void EnqueuePoll(Guid organizationId, Guid bookingId); }`
  - `static class CarrierTrackingWindow { static bool IsTracked(ShipmentDirection direction, ShipmentStatus status); }`
  - `CarrierTrackingEventWriter.InsertAsync(CarrierAccount account, IReadOnlyList<CarrierTrackingUpdate> updates, CarrierEventSource source, Guid? enteredByUserId, IEnumerable<string> secretValues, CancellationToken ct) : Task<int>` (count inserted; tenant-independent)
  - `CarrierEventApplier.ApplyAsync(Guid eventId, CancellationToken ct)` — runs inside a tenant scope; only acts on an event in `Processing`.
  - `CarrierEventProcessorJob.RunAsync(CancellationToken ct = default)` (Hangfire recurring `carrier-event-processor`, every 30 s)
  - `record CarrierPollResult(bool Polled, int Inserted)`; `CarrierBookingPoller.PollAsync(Guid bookingId, CancellationToken ct) : Task<CarrierPollResult>` — runs inside a tenant scope.
  - `CarrierTrackingPollJob.RunAsync(CancellationToken ct = default)` (Hangfire recurring `carrier-tracking-poll`, every 15 min) and `PollBookingAsync(Guid organizationId, Guid bookingId, CancellationToken ct = default)`.

- [ ] **Step 1: Add the Hangfire.Core package and the small contracts**

Run: `dotnet add src/Modules/International/International.csproj package Hangfire.Core --version 1.8.24`

`src/Modules/International/Application/Carriers/ICarrierJobScheduler.cs`:

```csharp
namespace Courier.International.Application.Carriers;

// Fire-and-forget background work, so request paths (webhook, manual entry, inbound link) never
// wait on processing or a carrier poll. Implemented with Hangfire in Infrastructure.
public interface ICarrierJobScheduler
{
    void EnqueueProcessing();
    void EnqueuePoll(Guid organizationId, Guid bookingId);
}
```

`src/Modules/International/Application/Carriers/CarrierTrackingWindow.cs`:

```csharp
using Courier.Shipments.Domain;

namespace Courier.International.Application.Carriers;

// The statuses in which a shipment is in the carrier's hands (or, inbound, not yet received by
// us), so carrier tracking is still meaningful and polling is worth doing.
public static class CarrierTrackingWindow
{
    public static bool IsTracked(ShipmentDirection direction, ShipmentStatus status) => direction switch
    {
        ShipmentDirection.Outbound => status is ShipmentStatus.HandedToCarrier or ShipmentStatus.InInternationalTransit
            or ShipmentStatus.CustomsHold or ShipmentStatus.InternationalException,
        ShipmentDirection.Inbound => status is ShipmentStatus.Created or ShipmentStatus.InInternationalTransit
            or ShipmentStatus.CustomsClearance or ShipmentStatus.CustomsHold or ShipmentStatus.InternationalException,
        _ => false
    };
}
```

`src/Modules/International/Infrastructure/Carriers/HangfireCarrierJobScheduler.cs`:

```csharp
using Courier.International.Application;
using Courier.International.Application.Carriers;
using Hangfire;

namespace Courier.International.Infrastructure.Carriers;

public sealed class HangfireCarrierJobScheduler : ICarrierJobScheduler
{
    private readonly IBackgroundJobClient _backgroundJobClient;

    public HangfireCarrierJobScheduler(IBackgroundJobClient backgroundJobClient)
    {
        _backgroundJobClient = backgroundJobClient;
    }

    public void EnqueueProcessing() =>
        _backgroundJobClient.Enqueue<CarrierEventProcessorJob>(job => job.RunAsync(CancellationToken.None));

    public void EnqueuePoll(Guid organizationId, Guid bookingId) =>
        _backgroundJobClient.Enqueue<CarrierTrackingPollJob>(job => job.PollBookingAsync(organizationId, bookingId, CancellationToken.None));
}
```

Append to `tests/International.Tests/CarrierTestSupport.cs`:

```csharp
internal sealed class RecordingJobScheduler : ICarrierJobScheduler
{
    public int ProcessingEnqueued { get; private set; }
    public List<(Guid OrganizationId, Guid BookingId)> Polls { get; } = new();
    public void EnqueueProcessing() => ProcessingEnqueued++;
    public void EnqueuePoll(Guid organizationId, Guid bookingId) => Polls.Add((organizationId, bookingId));
}
```

In `tests/International.Tests/InternationalShipmentServiceTests.cs`, replace `RecordingGateway.ApplyCarrierStatusAsync`'s `NotSupportedException` stub with:

```csharp
    public List<(Guid ShipmentId, ShipmentStatus Target, DateTimeOffset OccurredAt, CarrierEventReference Reference)> CarrierApplies { get; } = new();
    public CarrierStatusApplyResult ApplyResult { get; set; } = CarrierStatusApplyResult.Applied;
    public Exception? ApplyException { get; set; }

    public Task<CarrierStatusApplyResult> ApplyCarrierStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, DateTimeOffset occurredAt, CarrierEventReference reference, CancellationToken cancellationToken = default)
    {
        CarrierApplies.Add((shipmentId, targetStatus, occurredAt, reference));
        if (ApplyException is not null)
        {
            throw ApplyException;
        }
        return Task.FromResult(ApplyResult);
    }
```

- [ ] **Step 2: Write the failing unit tests**

`tests/International.Tests/CarrierTrackingEventWriterTests.cs`:

```csharp
using Courier.International.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Courier.International.Tests;

public class CarrierTrackingEventWriterTests
{
    private static CarrierTrackingUpdate Update(string eventId, string code = "TRANSIT", string awb = "AWB-1", string? description = null) =>
        new(awb, eventId, code, description, "Frankfurt", DateTimeOffset.UtcNow.AddHours(-1), "{\"k\":\"v\"}");

    [Fact]
    public async Task InsertAsync_StoresEventsUnderTheAccountsOrganizationAsPending()
    {
        await using var dbContext = CarrierTestData.NewContext();
        var organizationId = Guid.NewGuid();
        var account = await CarrierTestData.SeedAccountAsync(dbContext, organizationId, "TESTCARRIER");
        var writer = new CarrierTrackingEventWriter(dbContext, NullLogger<CarrierTrackingEventWriter>.Instance);

        var inserted = await writer.InsertAsync(account, new[] { Update("e1"), Update("e2") }, CarrierEventSource.Webhook, null, Array.Empty<string>(), CancellationToken.None);

        Assert.Equal(2, inserted);
        var stored = await dbContext.Set<CarrierTrackingEvent>().ToListAsync();
        Assert.All(stored, e =>
        {
            Assert.Equal(organizationId, e.OrganizationId);
            Assert.Equal(account.Id, e.CarrierAccountId);
            Assert.Equal(CarrierEventProcessingStatus.Pending, e.ProcessingStatus);
            Assert.Equal(CarrierEventSource.Webhook, e.Source);
        });
    }

    [Fact]
    public async Task InsertAsync_DuplicateEventIdAcrossSources_StoredOnce()
    {
        await using var dbContext = CarrierTestData.NewContext();
        var account = await CarrierTestData.SeedAccountAsync(dbContext, Guid.NewGuid(), "TESTCARRIER");
        var writer = new CarrierTrackingEventWriter(dbContext, NullLogger<CarrierTrackingEventWriter>.Instance);

        await writer.InsertAsync(account, new[] { Update("e1") }, CarrierEventSource.Webhook, null, Array.Empty<string>(), CancellationToken.None);
        var second = await writer.InsertAsync(account, new[] { Update("e1"), Update("e1") }, CarrierEventSource.Poll, null, Array.Empty<string>(), CancellationToken.None);

        Assert.Equal(0, second);
        Assert.Equal(1, await dbContext.Set<CarrierTrackingEvent>().CountAsync());
    }

    [Fact]
    public async Task InsertAsync_SkipsUnstorableUpdates_TruncatesLongText_AndMasksSecrets()
    {
        await using var dbContext = CarrierTestData.NewContext();
        var account = await CarrierTestData.SeedAccountAsync(dbContext, Guid.NewGuid(), "TESTCARRIER");
        var writer = new CarrierTrackingEventWriter(dbContext, NullLogger<CarrierTrackingEventWriter>.Instance);
        var withSecret = new CarrierTrackingUpdate("AWB-1", "e3", "TRANSIT", null, null, DateTimeOffset.UtcNow, "{\"token\":\"sk_live_12345\"}");

        var inserted = await writer.InsertAsync(account, new[]
        {
            Update("e1", awb: ""),                              // no AWB — skipped
            Update(new string('x', 201)),                       // event id too long — skipped
            Update("e2", description: new string('d', 1500)),   // description truncated
            withSecret
        }, CarrierEventSource.Webhook, null, new[] { "sk_live_12345" }, CancellationToken.None);

        Assert.Equal(2, inserted);
        var e2 = await dbContext.Set<CarrierTrackingEvent>().SingleAsync(e => e.CarrierEventId == "e2");
        Assert.Equal(1000, e2.Description!.Length);
        var e3 = await dbContext.Set<CarrierTrackingEvent>().SingleAsync(e => e.CarrierEventId == "e3");
        Assert.DoesNotContain("sk_live_12345", e3.RawPayload);
    }
}
```

`tests/International.Tests/CarrierEventApplierTests.cs`:

```csharp
using Courier.International.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Tests;

public class CarrierEventApplierTests
{
    private sealed record Harness(CarrierTestDbContext DbContext, Guid OrganizationId, CarrierAccount Account, RecordingGateway Gateway, CarrierEventApplier Applier);

    private static async Task<Harness> CreateHarnessAsync()
    {
        var dbContext = CarrierTestData.NewContext();
        var organizationId = Guid.NewGuid();
        var account = await CarrierTestData.SeedAccountAsync(dbContext, organizationId, "TESTCARRIER");
        var gateway = new RecordingGateway();
        var registry = new CarrierProviderRegistry(new ICarrierProvider[] { new TestCarrierProvider() });
        return new Harness(dbContext, organizationId, account, gateway, new CarrierEventApplier(dbContext, registry, gateway));
    }

    private static async Task<(CarrierBooking Booking, Guid ShipmentId)> SeedBookedAsync(Harness h, string awb,
        CarrierBookingDirection direction = CarrierBookingDirection.Outbound, CarrierBookingStatus status = CarrierBookingStatus.Booked, Guid? accountId = null)
    {
        var shipmentId = Guid.NewGuid();
        var leg = await CarrierTestData.SeedLegAsync(h.DbContext, h.OrganizationId, shipmentId);
        var booking = await CarrierTestData.SeedBookingAsync(h.DbContext, h.OrganizationId, leg.Id, accountId ?? h.Account.Id, status, awb, direction);
        return (booking, shipmentId);
    }

    private static async Task<CarrierTrackingEvent> ReloadAsync(Harness h, Guid eventId)
    {
        h.DbContext.ChangeTracker.Clear();
        return await h.DbContext.Set<CarrierTrackingEvent>().SingleAsync(e => e.Id == eventId);
    }

    [Fact]
    public async Task MatchedMappedEvent_IsAppliedThroughTheGateway()
    {
        var h = await CreateHarnessAsync();
        var (booking, shipmentId) = await SeedBookedAsync(h, "AWB-1");
        var occurredAt = DateTimeOffset.UtcNow.AddHours(-2);
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-1", "TRANSIT", occurredAt);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        var call = Assert.Single(h.Gateway.CarrierApplies);
        Assert.Equal(shipmentId, call.ShipmentId);
        Assert.Equal(ShipmentStatus.InInternationalTransit, call.Target);
        Assert.Equal(occurredAt, call.OccurredAt);
        Assert.Equal(evt.CarrierEventId, call.Reference.CarrierEventId);
        var stored = await ReloadAsync(h, evt.Id);
        Assert.Equal(CarrierEventProcessingStatus.Applied, stored.ProcessingStatus);
        Assert.Equal(booking.Id, stored.CarrierBookingId);
        Assert.Equal("InInternationalTransit", stored.MappedShipmentStatus);
        Assert.NotNull(stored.ProcessedAt);
        Assert.Null(stored.ClaimedAt);
    }

    [Fact]
    public async Task Applier_MatchesBookingByAccountAndAwb_NotByAwbAlone()
    {
        var h = await CreateHarnessAsync();
        var otherAccount = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        await SeedBookedAsync(h, "SAME-AWB", accountId: otherAccount.Id);
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "SAME-AWB", "TRANSIT", DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        Assert.Empty(h.Gateway.CarrierApplies);
        Assert.Equal(CarrierEventProcessingStatus.Unmatched, (await ReloadAsync(h, evt.Id)).ProcessingStatus);
    }

    [Fact]
    public async Task Applier_EventForCancelledBooking_IsUnmatched()
    {
        var h = await CreateHarnessAsync();
        await SeedBookedAsync(h, "AWB-1", status: CarrierBookingStatus.Cancelled);
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-1", "TRANSIT", DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        Assert.Empty(h.Gateway.CarrierApplies);
        Assert.Equal(CarrierEventProcessingStatus.Unmatched, (await ReloadAsync(h, evt.Id)).ProcessingStatus);
    }

    [Fact]
    public async Task InformationalCode_IsIgnoredWithoutCallingTheGateway()
    {
        var h = await CreateHarnessAsync();
        await SeedBookedAsync(h, "AWB-1");
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-1", "INFO", DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        Assert.Empty(h.Gateway.CarrierApplies);
        Assert.Equal(CarrierEventProcessingStatus.Ignored, (await ReloadAsync(h, evt.Id)).ProcessingStatus);
    }

    [Fact]
    public async Task InboundBooking_MapsWithInboundDirection()
    {
        var h = await CreateHarnessAsync();
        await SeedBookedAsync(h, "AWB-IN", CarrierBookingDirection.Inbound);
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-IN", "CLEARANCE", DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        Assert.Equal(ShipmentStatus.CustomsClearance, Assert.Single(h.Gateway.CarrierApplies).Target);
    }

    [Fact]
    public async Task AlreadyAtStatus_IsIgnored()
    {
        var h = await CreateHarnessAsync();
        await SeedBookedAsync(h, "AWB-1");
        h.Gateway.ApplyResult = CarrierStatusApplyResult.AlreadyAtStatus;
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-1", "TRANSIT", DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        Assert.Equal(CarrierEventProcessingStatus.Ignored, (await ReloadAsync(h, evt.Id)).ProcessingStatus);
    }

    [Fact]
    public async Task InvalidTransition_IsRejectedWithTheReason()
    {
        var h = await CreateHarnessAsync();
        await SeedBookedAsync(h, "AWB-1");
        h.Gateway.ApplyException = new ShipmentInvalidTransitionException(Guid.NewGuid(), ShipmentStatus.CustomsPrepared, ShipmentStatus.InInternationalTransit);
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-1", "TRANSIT", DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        var stored = await ReloadAsync(h, evt.Id);
        Assert.Equal(CarrierEventProcessingStatus.Rejected, stored.ProcessingStatus);
        Assert.Contains("Cannot transition", stored.ProcessingNote);
        Assert.Equal("InInternationalTransit", stored.MappedShipmentStatus);
    }

    [Fact]
    public async Task UnexpectedFailure_ReturnsToPending_UntilTheAttemptCap()
    {
        var h = await CreateHarnessAsync();
        await SeedBookedAsync(h, "AWB-1");
        h.Gateway.ApplyException = new InvalidOperationException("database hiccup");
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-1", "TRANSIT", DateTimeOffset.UtcNow);

        for (var attempt = 1; attempt <= CarrierEventApplier.MaxAttempts; attempt++)
        {
            var current = await ReloadAsync(h, evt.Id);
            current.ProcessingStatus = CarrierEventProcessingStatus.Processing;
            await h.DbContext.SaveChangesAsync();

            await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

            var stored = await ReloadAsync(h, evt.Id);
            Assert.Equal(attempt, stored.ProcessingAttempts);
            Assert.Equal(attempt < CarrierEventApplier.MaxAttempts ? CarrierEventProcessingStatus.Pending : CarrierEventProcessingStatus.Rejected, stored.ProcessingStatus);
        }
    }

    [Fact]
    public async Task EventNotClaimed_IsLeftAlone()
    {
        var h = await CreateHarnessAsync();
        await SeedBookedAsync(h, "AWB-1");
        var evt = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, h.Account.Id, "AWB-1", "TRANSIT", DateTimeOffset.UtcNow, CarrierEventProcessingStatus.Pending);

        await h.Applier.ApplyAsync(evt.Id, CancellationToken.None);

        Assert.Empty(h.Gateway.CarrierApplies);
        Assert.Equal(CarrierEventProcessingStatus.Pending, (await ReloadAsync(h, evt.Id)).ProcessingStatus);
    }
}
```

`tests/International.Tests/CarrierBookingPollerTests.cs`:

```csharp
using Courier.International.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.International.Infrastructure.Carriers;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;

namespace Courier.International.Tests;

public class CarrierBookingPollerTests
{
    private sealed record Harness(CarrierTestDbContext DbContext, Guid OrganizationId, TestCarrierProvider Provider, FixedShipmentTransitionService Shipments, CarrierBookingPoller Poller);

    private static Harness CreateHarness()
    {
        var dbContext = CarrierTestData.NewContext();
        var provider = new TestCarrierProvider();
        var shipments = new FixedShipmentTransitionService { Status = ShipmentStatus.HandedToCarrier, Direction = ShipmentDirection.Outbound };
        var poller = new CarrierBookingPoller(dbContext, shipments, new CarrierProviderRegistry(new ICarrierProvider[] { provider }),
            new DataProtectionCarrierSecretProtector(new EphemeralDataProtectionProvider()),
            new CarrierTrackingEventWriter(dbContext, NullLogger<CarrierTrackingEventWriter>.Instance), NullLogger<CarrierBookingPoller>.Instance);
        return new Harness(dbContext, Guid.NewGuid(), provider, shipments, poller);
    }

    private static async Task<CarrierBooking> SeedAsync(Harness h, bool pollingEnabled = true, bool accountActive = true)
    {
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER", accountActive, pollingEnabled);
        var leg = await CarrierTestData.SeedLegAsync(h.DbContext, h.OrganizationId, Guid.NewGuid());
        return await CarrierTestData.SeedBookingAsync(h.DbContext, h.OrganizationId, leg.Id, account.Id, CarrierBookingStatus.Booked, "AWB-1");
    }

    [Fact]
    public async Task PollAsync_TrackedShipment_StoresTheCarriersEvents()
    {
        var h = CreateHarness();
        var booking = await SeedAsync(h);
        h.Provider.TrackingUpdates.Add(new CarrierTrackingUpdate("AWB-1", "p1", "TRANSIT", null, null, DateTimeOffset.UtcNow, null));

        var result = await h.Poller.PollAsync(booking.Id, CancellationToken.None);

        Assert.Equal(new CarrierPollResult(Polled: true, Inserted: 1), result);
        Assert.Equal(CarrierEventSource.Poll, (await h.DbContext.Set<CarrierTrackingEvent>().SingleAsync()).Source);
    }

    [Theory]
    [InlineData(ShipmentStatus.CustomsPrepared)]
    [InlineData(ShipmentStatus.DeliveredAbroad)]
    public async Task PollAsync_ShipmentOutsideTheTrackingWindow_IsSkipped(ShipmentStatus status)
    {
        var h = CreateHarness();
        var booking = await SeedAsync(h);
        h.Shipments.Status = status;

        Assert.Equal(new CarrierPollResult(false, 0), await h.Poller.PollAsync(booking.Id, CancellationToken.None));
    }

    [Fact]
    public async Task PollAsync_PollingDisabledOrInactiveAccount_IsSkipped()
    {
        var h = CreateHarness();
        var disabled = await SeedAsync(h, pollingEnabled: false);
        var inactive = await SeedAsync(h, accountActive: false);

        Assert.False((await h.Poller.PollAsync(disabled.Id, CancellationToken.None)).Polled);
        Assert.False((await h.Poller.PollAsync(inactive.Id, CancellationToken.None)).Polled);
    }

    [Fact]
    public async Task PollAsync_CarrierUnavailable_IsSwallowedAndNothingStored()
    {
        var h = CreateHarness();
        var booking = await SeedAsync(h);
        h.Provider.TrackingException = new BrokenCircuitException("open");

        var result = await h.Poller.PollAsync(booking.Id, CancellationToken.None);

        Assert.Equal(new CarrierPollResult(true, 0), result);
        Assert.Empty(h.DbContext.Set<CarrierTrackingEvent>());
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/International.Tests --filter "FullyQualifiedName~CarrierTrackingEventWriterTests|FullyQualifiedName~CarrierEventApplierTests|FullyQualifiedName~CarrierBookingPollerTests"`
Expected: FAIL — the writer/applier/poller types do not exist.

- [ ] **Step 4: Implement the writer**

`src/Modules/International/Application/CarrierTrackingEventWriter.cs`:

```csharp
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courier.International.Application;

// The single insert path for carrier tracking events from every source (webhook, poll, manual).
// Deduplicates by (CarrierAccountId, CarrierEventId) — within the batch, against stored rows, and
// against a concurrent insert racing on the unique index.
public class CarrierTrackingEventWriter
{
    private const int MaxAwbLength = 100;
    private const int MaxEventIdLength = 200;
    private const int MaxStatusCodeLength = 100;
    private const int MaxDescriptionLength = 1000;
    private const int MaxLocationLength = 300;

    private readonly DbContext _dbContext;
    private readonly ILogger<CarrierTrackingEventWriter> _logger;

    public CarrierTrackingEventWriter(DbContext dbContext, ILogger<CarrierTrackingEventWriter> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<int> InsertAsync(CarrierAccount account, IReadOnlyList<CarrierTrackingUpdate> updates, CarrierEventSource source,
        Guid? enteredByUserId, IEnumerable<string> secretValues, CancellationToken cancellationToken)
    {
        var storable = new List<CarrierTrackingUpdate>();
        foreach (var update in updates)
        {
            if (IsStorable(update))
            {
                storable.Add(update);
            }
            else
            {
                _logger.LogWarning("Skipped an unstorable carrier tracking update for carrier account {CarrierAccountId}.", account.Id);
            }
        }
        var distinct = storable.GroupBy(u => u.CarrierEventId).Select(g => g.First()).ToList();
        if (distinct.Count == 0)
        {
            return 0;
        }

        // Deliberate IgnoreQueryFilters: the webhook path has no tenant context. The account was
        // already resolved and every row here is scoped to it by CarrierAccountId.
        var eventIds = distinct.Select(u => u.CarrierEventId).ToList();
        var existing = (await _dbContext.Set<CarrierTrackingEvent>().IgnoreQueryFilters()
            .Where(e => e.CarrierAccountId == account.Id && eventIds.Contains(e.CarrierEventId))
            .Select(e => e.CarrierEventId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var secrets = secretValues.ToList();
        var now = DateTimeOffset.UtcNow;
        var toInsert = distinct.Where(u => !existing.Contains(u.CarrierEventId)).Select(u => new CarrierTrackingEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = account.OrganizationId,
            CarrierAccountId = account.Id,
            CarrierAwb = u.CarrierAwb.Trim(),
            CarrierEventId = u.CarrierEventId,
            CarrierStatusCode = u.CarrierStatusCode.Trim(),
            Description = Truncate(u.Description, MaxDescriptionLength),
            Location = Truncate(u.Location, MaxLocationLength),
            OccurredAt = u.OccurredAt,
            Source = source,
            RawPayload = CarrierPayloadSanitizer.ToStoredJson(u.RawPayload, secrets),
            EnteredByUserId = enteredByUserId,
            ReceivedAt = now,
            ProcessingStatus = CarrierEventProcessingStatus.Pending
        }).ToList();
        if (toInsert.Count == 0)
        {
            return 0;
        }

        _dbContext.Set<CarrierTrackingEvent>().AddRange(toInsert);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return toInsert.Count;
        }
        catch (DbUpdateException)
        {
            // Another source inserted one of these first — fall back to one row at a time.
            foreach (var row in toInsert)
            {
                _dbContext.Entry(row).State = EntityState.Detached;
            }
            var inserted = 0;
            foreach (var row in toInsert)
            {
                _dbContext.Set<CarrierTrackingEvent>().Add(row);
                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    inserted++;
                }
                catch (DbUpdateException)
                {
                    _dbContext.Entry(row).State = EntityState.Detached;
                }
            }
            return inserted;
        }
    }

    private static bool IsStorable(CarrierTrackingUpdate update) =>
        !string.IsNullOrWhiteSpace(update.CarrierAwb) && update.CarrierAwb.Trim().Length <= MaxAwbLength
        && !string.IsNullOrWhiteSpace(update.CarrierEventId) && update.CarrierEventId.Length <= MaxEventIdLength
        && !string.IsNullOrWhiteSpace(update.CarrierStatusCode) && update.CarrierStatusCode.Trim().Length <= MaxStatusCodeLength;

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null : value.Length <= maxLength ? value : value[..maxLength];
}
```

- [ ] **Step 5: Implement the applier**

`src/Modules/International/Application/CarrierEventApplier.cs`:

```csharp
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Application;

// Applies one claimed (Processing) carrier tracking event. Must run inside a tenant scope for the
// event's organization (CarrierEventProcessorJob creates it) — the gateway's Shipment lookup and
// every query here are tenant-filtered.
public class CarrierEventApplier
{
    public const int MaxAttempts = 5;
    private const int MaxNoteLength = 1000;

    private readonly DbContext _dbContext;
    private readonly ICarrierProviderRegistry _registry;
    private readonly IShipmentInternationalGateway _gateway;

    public CarrierEventApplier(DbContext dbContext, ICarrierProviderRegistry registry, IShipmentInternationalGateway gateway)
    {
        _dbContext = dbContext;
        _registry = registry;
        _gateway = gateway;
    }

    private sealed record Outcome(CarrierEventProcessingStatus Status, string? Note, Guid? BookingId = null, string? MappedStatus = null);

    public async Task ApplyAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var evt = await _dbContext.Set<CarrierTrackingEvent>().FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        if (evt is null || evt.ProcessingStatus != CarrierEventProcessingStatus.Processing)
        {
            return;
        }

        Outcome outcome;
        try
        {
            outcome = await EvaluateAndApplyAsync(evt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unexpected (not a state-machine rejection) — retried by a later run up to MaxAttempts.
            outcome = new Outcome(CarrierEventProcessingStatus.Pending, $"{ex.GetType().Name}: {ex.Message}");
        }

        // A failed gateway save leaves its modified Shipment tracked; never let it be re-saved.
        _dbContext.ChangeTracker.Clear();
        var stored = await _dbContext.Set<CarrierTrackingEvent>().FirstAsync(e => e.Id == eventId, cancellationToken);
        stored.ProcessingAttempts += 1;
        stored.CarrierBookingId = outcome.BookingId ?? stored.CarrierBookingId;
        stored.MappedShipmentStatus = outcome.MappedStatus ?? stored.MappedShipmentStatus;
        stored.ClaimedAt = null;

        var status = outcome.Status;
        var note = outcome.Note;
        if (status == CarrierEventProcessingStatus.Pending && stored.ProcessingAttempts >= MaxAttempts)
        {
            status = CarrierEventProcessingStatus.Rejected;
            note = $"Gave up after {stored.ProcessingAttempts} attempts. Last error: {note}";
        }
        stored.ProcessingStatus = status;
        stored.ProcessingNote = note is null ? null : note.Length <= MaxNoteLength ? note : note[..MaxNoteLength];
        stored.ProcessedAt = status == CarrierEventProcessingStatus.Pending ? null : DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Outcome> EvaluateAndApplyAsync(CarrierTrackingEvent evt, CancellationToken cancellationToken)
    {
        var account = await _dbContext.Set<CarrierAccount>().FirstOrDefaultAsync(a => a.Id == evt.CarrierAccountId, cancellationToken);
        if (account is null)
        {
            return new Outcome(CarrierEventProcessingStatus.Rejected, "Carrier account not found.");
        }
        var provider = _registry.Find(account.CarrierCode);
        if (provider is null)
        {
            return new Outcome(CarrierEventProcessingStatus.Rejected, $"No carrier provider is registered for '{account.CarrierCode}'.");
        }

        // Matched by account AND carrier AWB, only against a live Booked booking — never by AWB alone.
        var booking = await _dbContext.Set<CarrierBooking>().FirstOrDefaultAsync(b =>
            b.CarrierAccountId == evt.CarrierAccountId && b.CarrierAwb == evt.CarrierAwb && b.Status == CarrierBookingStatus.Booked, cancellationToken);
        if (booking is null)
        {
            return new Outcome(CarrierEventProcessingStatus.Unmatched, "No booked carrier booking matches this carrier AWB.");
        }
        var leg = await _dbContext.Set<InternationalLeg>().FirstOrDefaultAsync(l => l.Id == booking.InternationalLegId, cancellationToken);
        if (leg is null)
        {
            return new Outcome(CarrierEventProcessingStatus.Rejected, "International leg not found.", booking.Id);
        }

        var direction = booking.Direction == CarrierBookingDirection.Outbound ? ShipmentDirection.Outbound : ShipmentDirection.Inbound;
        var target = provider.MapStatus(evt.CarrierStatusCode, direction);
        if (target is null)
        {
            return new Outcome(CarrierEventProcessingStatus.Ignored, $"Informational carrier status '{evt.CarrierStatusCode}'.", booking.Id);
        }
        var mapped = target.Value.ToString();

        try
        {
            var result = await _gateway.ApplyCarrierStatusAsync(leg.ShipmentId, target.Value, evt.OccurredAt,
                new CarrierEventReference(account.Id, evt.CarrierAwb, evt.CarrierEventId, evt.CarrierStatusCode, evt.Location), cancellationToken);
            return result switch
            {
                CarrierStatusApplyResult.Applied => new Outcome(CarrierEventProcessingStatus.Applied, null, booking.Id, mapped),
                CarrierStatusApplyResult.AlreadyAtStatus => new Outcome(CarrierEventProcessingStatus.Ignored, $"Shipment was already {mapped}.", booking.Id, mapped),
                _ => new Outcome(CarrierEventProcessingStatus.Rejected, "Shipment not found.", booking.Id, mapped)
            };
        }
        catch (ShipmentValidationException ex)
        {
            // Includes ShipmentInvalidTransitionException: a stale/out-of-order/impossible event.
            return new Outcome(CarrierEventProcessingStatus.Rejected, ex.Message, booking.Id, mapped);
        }
    }
}
```

- [ ] **Step 6: Implement the poller and the two jobs**

`src/Modules/International/Application/CarrierBookingPoller.cs`:

```csharp
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courier.International.Application;

public record CarrierPollResult(bool Polled, int Inserted);

// Polls one booking. Runs inside a tenant scope (CarrierTrackingPollJob).
public class CarrierBookingPoller
{
    private readonly DbContext _dbContext;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly ICarrierProviderRegistry _registry;
    private readonly ICarrierSecretProtector _secretProtector;
    private readonly CarrierTrackingEventWriter _writer;
    private readonly ILogger<CarrierBookingPoller> _logger;

    public CarrierBookingPoller(DbContext dbContext, IShipmentTransitionService shipmentTransitionService, ICarrierProviderRegistry registry,
        ICarrierSecretProtector secretProtector, CarrierTrackingEventWriter writer, ILogger<CarrierBookingPoller> logger)
    {
        _dbContext = dbContext;
        _shipmentTransitionService = shipmentTransitionService;
        _registry = registry;
        _secretProtector = secretProtector;
        _writer = writer;
        _logger = logger;
    }

    public async Task<CarrierPollResult> PollAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await _dbContext.Set<CarrierBooking>().FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
        if (booking is null || booking.Status != CarrierBookingStatus.Booked || booking.CarrierAwb is null)
        {
            return new CarrierPollResult(false, 0);
        }
        var account = await _dbContext.Set<CarrierAccount>().FirstOrDefaultAsync(a => a.Id == booking.CarrierAccountId, cancellationToken);
        var provider = account is null ? null : _registry.Find(account.CarrierCode);
        if (account is null || !account.IsActive || !account.PollingEnabled || provider is null || !provider.Capabilities.SupportsPolling)
        {
            return new CarrierPollResult(false, 0);
        }
        var leg = await _dbContext.Set<InternationalLeg>().FirstOrDefaultAsync(l => l.Id == booking.InternationalLegId, cancellationToken);
        var scan = leg is null ? null : await _shipmentTransitionService.GetScanInfoAsync(leg.ShipmentId, cancellationToken);
        if (scan is null || !CarrierTrackingWindow.IsTracked(scan.Direction, scan.CurrentStatus))
        {
            return new CarrierPollResult(false, 0);
        }

        var credentials = _secretProtector.UnprotectCredentials(account.EncryptedCredentials);
        IReadOnlyList<CarrierTrackingUpdate> updates;
        try
        {
            updates = await provider.GetTrackingAsync(booking.CarrierAwb, credentials, cancellationToken);
        }
        catch (Exception ex) when (CarrierCallFailures.IsCarrierUnavailable(ex) || CarrierCallFailures.IsCarrierFailure(ex))
        {
            _logger.LogWarning("Polling carrier booking {CarrierBookingId} failed: {Reason}", booking.Id, ex.GetType().Name);
            return new CarrierPollResult(true, 0);
        }
        var inserted = await _writer.InsertAsync(account, updates, CarrierEventSource.Poll, null, credentials.SecretValues, cancellationToken);
        return new CarrierPollResult(true, inserted);
    }
}
```

`src/Modules/International/Application/CarrierEventProcessorJob.cs`:

```csharp
using Courier.International.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Courier.International.Application;

// Hangfire recurring job "carrier-event-processor" (every 30 s) and enqueued after every insert.
// Claims each Pending event atomically (Pending -> Processing), so concurrent runs — recurring,
// enqueued, or a test invoking RunAsync directly — never apply the same event twice. Each
// organization's events are applied inside that organization's tenant scope.
public class CarrierEventProcessorJob
{
    public const int BatchSize = 200;
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(5);

    private readonly DbContext _dbContext;
    private readonly ITenantScopeFactory _tenantScopeFactory;
    private readonly ILogger<CarrierEventProcessorJob> _logger;

    public CarrierEventProcessorJob(DbContext dbContext, ITenantScopeFactory tenantScopeFactory, ILogger<CarrierEventProcessorJob> logger)
    {
        _dbContext = dbContext;
        _tenantScopeFactory = tenantScopeFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Deliberate IgnoreQueryFilters (same as OutboxPublisherJob): this job runs without a
        // tenant and reads across tenants only to find work; all application of events happens in
        // per-organization tenant scopes below.
        var staleBefore = DateTimeOffset.UtcNow - StaleClaimAge;
        await _dbContext.Set<CarrierTrackingEvent>().IgnoreQueryFilters()
            .Where(e => e.ProcessingStatus == CarrierEventProcessingStatus.Processing && e.ClaimedAt < staleBefore)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.ProcessingStatus, CarrierEventProcessingStatus.Pending)
                .SetProperty(e => e.ClaimedAt, (DateTimeOffset?)null), cancellationToken);

        var candidates = await _dbContext.Set<CarrierTrackingEvent>().IgnoreQueryFilters()
            .Where(e => e.ProcessingStatus == CarrierEventProcessingStatus.Pending)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.ReceivedAt)
            .Take(BatchSize)
            .Select(e => new { e.Id, e.OrganizationId })
            .ToListAsync(cancellationToken);

        foreach (var organization in candidates.GroupBy(c => c.OrganizationId))
        {
            await using var scope = _tenantScopeFactory.CreateScope(organization.Key);
            var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            var applier = scope.ServiceProvider.GetRequiredService<CarrierEventApplier>();
            foreach (var candidate in organization) // still OccurredAt order
            {
                var claimedAt = DateTimeOffset.UtcNow;
                var claimed = await dbContext.Set<CarrierTrackingEvent>()
                    .Where(e => e.Id == candidate.Id && e.ProcessingStatus == CarrierEventProcessingStatus.Pending)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.ProcessingStatus, CarrierEventProcessingStatus.Processing)
                        .SetProperty(e => e.ClaimedAt, claimedAt), cancellationToken);
                if (claimed == 0)
                {
                    continue; // another run owns it
                }
                try
                {
                    await applier.ApplyAsync(candidate.Id, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Left in Processing; reclaimed after StaleClaimAge.
                    _logger.LogError(ex, "Applying carrier tracking event {CarrierTrackingEventId} failed.", candidate.Id);
                }
            }
        }
    }
}
```

`src/Modules/International/Application/CarrierTrackingPollJob.cs`:

```csharp
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Courier.International.Application;

// Hangfire recurring job "carrier-tracking-poll" (every 15 min); PollBookingAsync is enqueued when
// an inbound booking is linked on a polling account.
public class CarrierTrackingPollJob
{
    public const int MaxBookingsPerAccountPerRun = 50;
    private static readonly TimeSpan MaxBookingAge = TimeSpan.FromDays(60);

    private readonly DbContext _dbContext;
    private readonly ITenantScopeFactory _tenantScopeFactory;
    private readonly ICarrierJobScheduler _scheduler;

    public CarrierTrackingPollJob(DbContext dbContext, ITenantScopeFactory tenantScopeFactory, ICarrierJobScheduler scheduler)
    {
        _dbContext = dbContext;
        _tenantScopeFactory = tenantScopeFactory;
        _scheduler = scheduler;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Deliberate IgnoreQueryFilters: finds work across tenants; polling itself runs per tenant.
        var createdAfter = DateTimeOffset.UtcNow - MaxBookingAge;
        var targets = await (
            from booking in _dbContext.Set<CarrierBooking>().IgnoreQueryFilters()
            join account in _dbContext.Set<CarrierAccount>().IgnoreQueryFilters() on booking.CarrierAccountId equals account.Id
            where booking.Status == CarrierBookingStatus.Booked && booking.CreatedAt > createdAfter
                && account.IsActive && account.PollingEnabled
            orderby booking.CreatedAt
            select new { booking.Id, booking.OrganizationId, booking.CarrierAccountId })
            .ToListAsync(cancellationToken);

        var anyInserted = false;
        foreach (var organization in targets.GroupBy(t => t.OrganizationId))
        {
            await using var scope = _tenantScopeFactory.CreateScope(organization.Key);
            var poller = scope.ServiceProvider.GetRequiredService<CarrierBookingPoller>();
            foreach (var account in organization.GroupBy(t => t.CarrierAccountId))
            {
                var polled = 0;
                foreach (var target in account)
                {
                    if (polled >= MaxBookingsPerAccountPerRun)
                    {
                        break;
                    }
                    var result = await poller.PollAsync(target.Id, cancellationToken);
                    polled += result.Polled ? 1 : 0;
                    anyInserted |= result.Inserted > 0;
                }
            }
        }
        if (anyInserted)
        {
            _scheduler.EnqueueProcessing();
        }
    }

    public async Task PollBookingAsync(Guid organizationId, Guid bookingId, CancellationToken cancellationToken = default)
    {
        await using var scope = _tenantScopeFactory.CreateScope(organizationId);
        var result = await scope.ServiceProvider.GetRequiredService<CarrierBookingPoller>().PollAsync(bookingId, cancellationToken);
        if (result.Inserted > 0)
        {
            _scheduler.EnqueueProcessing();
        }
    }
}
```

In `src/Api/Program.cs`, with the International registrations add:

```csharp
builder.Services.AddScoped<Courier.International.Application.Carriers.ICarrierJobScheduler, Courier.International.Infrastructure.Carriers.HangfireCarrierJobScheduler>();
builder.Services.AddScoped<Courier.International.Application.CarrierTrackingEventWriter>();
builder.Services.AddScoped<Courier.International.Application.CarrierEventApplier>();
builder.Services.AddScoped<Courier.International.Application.CarrierEventProcessorJob>();
builder.Services.AddScoped<Courier.International.Application.CarrierBookingPoller>();
builder.Services.AddScoped<Courier.International.Application.CarrierTrackingPollJob>();
```

and after the existing `RecurringJob.AddOrUpdate<...RiderLocationPingRetentionJob>(...)` line add:

```csharp
RecurringJob.AddOrUpdate<Courier.International.Application.CarrierEventProcessorJob>("carrier-event-processor", job => job.RunAsync(CancellationToken.None), "*/30 * * * * *");
RecurringJob.AddOrUpdate<Courier.International.Application.CarrierTrackingPollJob>("carrier-tracking-poll", job => job.RunAsync(CancellationToken.None), "*/15 * * * *");
```

- [ ] **Step 7: Run the unit tests**

Run: `dotnet test tests/International.Tests` — Expected: PASS.
Run: `dotnet build Courier.slnx` — Expected: 0 errors.

- [ ] **Step 8: Write the processing integration tests (real Postgres, real Hangfire in the host)**

First add two helpers to `tests/Api.Tests/International/InternationalApiTestBase.cs` (Tasks 13–15 reuse them); add `using Courier.International.Application;` and `using Courier.International.Domain;` to that file:

```csharp
    // Runs the processor directly, then waits until none of this carrier account's events is
    // still Pending/Processing (the host's own Hangfire server may claim some first — the atomic
    // claim makes that safe).
    protected async Task ProcessCarrierEventsAsync(Guid carrierAccountId)
    {
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<CarrierEventProcessorJob>().RunAsync();
        }
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            using var scope = Factory.Services.CreateScope();
            var open = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<CarrierTrackingEvent>().IgnoreQueryFilters()
                .CountAsync(e => e.CarrierAccountId == carrierAccountId
                    && (e.ProcessingStatus == CarrierEventProcessingStatus.Pending || e.ProcessingStatus == CarrierEventProcessingStatus.Processing));
            if (open == 0)
            {
                return;
            }
            Assert.True(DateTime.UtcNow < deadline, "Carrier events were not processed in time.");
            await Task.Delay(200);
        }
    }

    protected async Task<ShipmentStatus> ShipmentStatusAsync(Guid shipmentId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Shipment>().IgnoreQueryFilters()
            .Where(s => s.Id == shipmentId).Select(s => s.CurrentStatus).SingleAsync();
    }
```

Then create `tests/Api.Tests/International/CarrierEventProcessingTests.cs`:

```csharp
using Courier.International.Application;
using Courier.International.Domain;
using Courier.Persistence;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierEventProcessingTests : InternationalApiTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public CarrierEventProcessingTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private async Task<(Guid OrganizationId, Guid ShipmentId, Guid AccountId)> SeedHandedOverAsync(string label, string carrierAwb)
    {
        var org = await SetUpOrgAsync(label);
        var created = await CreateOutboundAsync(org);
        await SetShipmentStateAsync(created.Shipment.Id, ShipmentStatus.HandedToCarrier, null);
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var legId = await dbContext.Set<InternationalLeg>().IgnoreQueryFilters().Where(l => l.ShipmentId == created.Shipment.Id).Select(l => l.Id).SingleAsync();
        var account = new CarrierAccount
        {
            Id = Guid.NewGuid(), OrganizationId = org.Auth.OrganizationId, CarrierCode = "MANUAL", DisplayName = "Manual",
            WebhookPublicId = Guid.NewGuid().ToString("N"), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Set<CarrierAccount>().Add(account);
        dbContext.Set<CarrierBooking>().Add(new CarrierBooking
        {
            Id = Guid.NewGuid(), OrganizationId = org.Auth.OrganizationId, InternationalLegId = legId, CarrierAccountId = account.Id,
            Direction = CarrierBookingDirection.Outbound, Status = CarrierBookingStatus.Booked, CarrierAwb = carrierAwb,
            IdempotencyKey = Guid.NewGuid().ToString("N"), BookedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        return (org.Auth.OrganizationId, created.Shipment.Id, account.Id);
    }

    private async Task<Guid> InsertPendingEventAsync(Guid organizationId, Guid accountId, string carrierAwb, string code, DateTimeOffset occurredAt)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = new CarrierTrackingEvent
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, CarrierAccountId = accountId, CarrierAwb = carrierAwb,
            CarrierEventId = $"evt-{Guid.NewGuid():N}", CarrierStatusCode = code, OccurredAt = occurredAt,
            Source = CarrierEventSource.Manual, ReceivedAt = DateTimeOffset.UtcNow, ProcessingStatus = CarrierEventProcessingStatus.Pending
        };
        dbContext.Set<CarrierTrackingEvent>().Add(evt);
        await dbContext.SaveChangesAsync();
        return evt.Id;
    }

    private async Task<CarrierTrackingEvent> EventAsync(Guid eventId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<CarrierTrackingEvent>().IgnoreQueryFilters().SingleAsync(e => e.Id == eventId);
    }

    [Fact]
    public async Task Processor_AppliesEventsInOccurredAtOrder_EvenWhenInsertedOutOfOrder()
    {
        var (organizationId, shipmentId, accountId) = await SeedHandedOverAsync("order", "ORD-AWB");
        var now = DateTimeOffset.UtcNow;
        var delivered = await InsertPendingEventAsync(organizationId, accountId, "ORD-AWB", "DELIVERED", now.AddMinutes(-1));
        var transit = await InsertPendingEventAsync(organizationId, accountId, "ORD-AWB", "IN_TRANSIT", now.AddHours(-3));

        await ProcessCarrierEventsAsync(accountId);

        Assert.Equal(ShipmentStatus.DeliveredAbroad, await ShipmentStatusAsync(shipmentId));
        Assert.Equal(CarrierEventProcessingStatus.Applied, (await EventAsync(transit)).ProcessingStatus);
        Assert.Equal(CarrierEventProcessingStatus.Applied, (await EventAsync(delivered)).ProcessingStatus);
    }

    [Fact]
    public async Task Processor_AppliesEachTenantsEventsToItsOwnShipment()
    {
        var a = await SeedHandedOverAsync("tenant-a", "SAME-AWB-1");
        var b = await SeedHandedOverAsync("tenant-b", "SAME-AWB-1");
        var eventA = await InsertPendingEventAsync(a.OrganizationId, a.AccountId, "SAME-AWB-1", "IN_TRANSIT", DateTimeOffset.UtcNow);

        await ProcessCarrierEventsAsync(a.AccountId);

        Assert.Equal(ShipmentStatus.InInternationalTransit, await ShipmentStatusAsync(a.ShipmentId));
        Assert.Equal(ShipmentStatus.HandedToCarrier, await ShipmentStatusAsync(b.ShipmentId));

        var eventB = await InsertPendingEventAsync(b.OrganizationId, b.AccountId, "SAME-AWB-1", "CUSTOMS_HOLD", DateTimeOffset.UtcNow);
        await ProcessCarrierEventsAsync(b.AccountId);
        Assert.Equal(CarrierEventProcessingStatus.Rejected, (await EventAsync(eventB)).ProcessingStatus); // HandedToCarrier -> CustomsHold is not allowed
        Assert.Equal(ShipmentStatus.InInternationalTransit, await ShipmentStatusAsync(a.ShipmentId));
    }

    [Fact]
    public async Task Processor_InvalidTransition_IsRejectedAndShipmentUnchanged()
    {
        var (organizationId, shipmentId, accountId) = await SeedHandedOverAsync("invalid", "INV-AWB");
        var evt = await InsertPendingEventAsync(organizationId, accountId, "INV-AWB", "DELIVERED", DateTimeOffset.UtcNow);

        await ProcessCarrierEventsAsync(accountId);

        var stored = await EventAsync(evt);
        Assert.Equal(CarrierEventProcessingStatus.Rejected, stored.ProcessingStatus);
        Assert.Contains("Cannot transition", stored.ProcessingNote);
        Assert.Equal(ShipmentStatus.HandedToCarrier, await ShipmentStatusAsync(shipmentId));
    }

    [Fact]
    public async Task Processor_AppliedTransition_WritesACarrierShipmentEvent()
    {
        var (organizationId, shipmentId, accountId) = await SeedHandedOverAsync("event", "EVT-AWB");
        var occurredAt = DateTimeOffset.UtcNow.AddHours(-1);
        var evt = await InsertPendingEventAsync(organizationId, accountId, "EVT-AWB", "IN_TRANSIT", occurredAt);

        await ProcessCarrierEventsAsync(accountId);

        using var scope = Factory.Services.CreateScope();
        var shipmentEvent = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<ShipmentEvent>()
            .SingleAsync(e => e.ShipmentId == shipmentId && e.EventType == ShipmentEventType.InInternationalTransit);
        Assert.Null(shipmentEvent.UserId);
        Assert.Equal(occurredAt.ToUnixTimeMilliseconds(), shipmentEvent.Timestamp.ToUnixTimeMilliseconds());
    }
}
```

- [ ] **Step 9: Run the integration tests**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CarrierEventProcessingTests"` — Expected: PASS (4 tests).

- [ ] **Step 10: Commit**

```bash
git add src/Modules/International/ src/Api/Program.cs tests/International.Tests/ tests/Api.Tests/International/
git commit -m "feat(international): store, process and poll carrier tracking events per tenant

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Carrier booking — book, cancel, link inbound

**Files:**
- Modify: `src/Modules/International/Application/CarrierDtos.cs`
- Create: `src/Modules/International/Application/CarrierBookingService.cs`
- Modify: `src/Modules/International/Presentation/InternationalEndpoints.cs` (three routes)
- Modify: `src/Api/Middleware/IdempotencyMiddleware.cs` (book route)
- Modify: `src/Api/Program.cs` (DI)
- Test: `tests/International.Tests/CarrierBookingServiceTests.cs`, `tests/Api.Tests/International/CarrierBookingEndpointsTests.cs`

**Interfaces:**
- Consumes: Task 3 `ShipmentScanInfo.Direction`; Task 8 contracts; Task 9 `CarrierCallFailures`; Task 10 exceptions; Task 11 `ICarrierJobScheduler`; I3 `InternationalShipmentAccess.IsVisibleAsync`.
- Produces:
  - `record BookCarrierRequest(Guid CarrierAccountId, string? ServiceLevel, string? CarrierAwb)`
  - `record LinkInboundBookingRequest(Guid CarrierAccountId, string CarrierAwb, string? ServiceLevel)`
  - `record CarrierBookingResponse(Guid Id, Guid CarrierAccountId, string CarrierCode, string CarrierAccountName, string Direction, string Status, string? CarrierAwb, string? ServiceLevel, bool HasLabel, string? FailureReason, DateTimeOffset? BookedAt, DateTimeOffset? CancelledAt, DateTimeOffset? CustodyStartedAt, DateTimeOffset CreatedAt)`
  - `static CarrierBookingResponse CarrierBookingService.ToResponse(CarrierBooking booking, CarrierAccount account)` (reused by the read model, Task 14)
  - `CarrierBookingService.BookAsync(Guid shipmentId, BookCarrierRequest request, string? idempotencyKey, ClaimsPrincipal actingUser, CancellationToken ct) : Task<CarrierBookingResponse?>`
  - `CarrierBookingService.CancelAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken ct) : Task<CarrierBookingResponse?>`
  - `CarrierBookingService.LinkInboundAsync(Guid shipmentId, LinkInboundBookingRequest request, ClaimsPrincipal actingUser, CancellationToken ct) : Task<CarrierBookingResponse?>`
  - Routes: `POST /api/v1/international/shipments/{id}/book` (`international.book`, `Idempotency-Key` header required), `POST .../booking/cancel` (`international.book`), `POST .../inbound-booking` (`international.book`).

- [ ] **Step 1: Write the failing service tests**

`tests/International.Tests/CarrierBookingServiceTests.cs`:

```csharp
using System.Security.Claims;
using Courier.International.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.International.Infrastructure.Carriers;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;

namespace Courier.International.Tests;

public class CarrierBookingServiceTests
{
    private sealed class Harness
    {
        public required CarrierTestDbContext DbContext { get; init; }
        public required Guid OrganizationId { get; init; }
        public required FixedShipmentTransitionService Shipments { get; init; }
        public required TestCarrierProvider Provider { get; init; }
        public required InMemoryObjectStorage Storage { get; init; }
        public required RecordingJobScheduler Scheduler { get; init; }
        public required RecordingAuditLogger Audit { get; init; }
        public required CarrierBookingService Service { get; init; }
        public required Guid ShipmentId { get; init; }
        public required InternationalLeg Leg { get; init; }
    }

    private static async Task<Harness> CreateHarnessAsync(IReadOnlySet<Guid>? accessibleBranchIds = null)
    {
        var dbContext = CarrierTestData.NewContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var leg = await CarrierTestData.SeedLegAsync(dbContext, organizationId, shipmentId);
        var shipments = new FixedShipmentTransitionService { Status = ShipmentStatus.CustomsPrepared, Direction = ShipmentDirection.Outbound };
        var provider = new TestCarrierProvider();
        var storage = new InMemoryObjectStorage();
        var scheduler = new RecordingJobScheduler();
        var audit = new RecordingAuditLogger();
        var service = new CarrierBookingService(dbContext, new FixedTenantProvider(organizationId), audit, shipments,
            new FixedBranchAuthorizationChecker(accessibleBranchIds),
            new CarrierProviderRegistry(new ICarrierProvider[] { provider, new ManualCarrierProvider() }),
            new DataProtectionCarrierSecretProtector(new EphemeralDataProtectionProvider()), storage, scheduler, NullLogger<CarrierBookingService>.Instance);
        return new Harness
        {
            DbContext = dbContext, OrganizationId = organizationId, Shipments = shipments, Provider = provider, Storage = storage,
            Scheduler = scheduler, Audit = audit, Service = service, ShipmentId = shipmentId, Leg = leg
        };
    }

    private static ClaimsPrincipal ActingUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    [Fact]
    public async Task BookAsync_Success_PersistsBookedBookingAndStoresLabel()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        h.Provider.Book = request => new CarrierBookingResult("TC-777", "EXPRESS", new CarrierLabel(new byte[] { 1, 2, 3 }, "application/pdf"), "{\"awb\":\"TC-777\"}");

        var result = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, "EXPRESS", null), "key-1", ActingUser(), CancellationToken.None);

        Assert.Equal("Booked", result!.Status);
        Assert.Equal("TC-777", result.CarrierAwb);
        Assert.True(result.HasLabel);
        var stored = await h.DbContext.Set<CarrierBooking>().SingleAsync();
        Assert.Equal(h.Leg.Id, stored.InternationalLegId);
        Assert.Equal(CarrierBookingDirection.Outbound, stored.Direction);
        Assert.Equal("application/pdf", stored.LabelContentType);
        Assert.Equal(new byte[] { 1, 2, 3 }, h.Storage.Objects[stored.LabelStorageKey!].Content);
        Assert.Contains(h.Audit.Entries, e => e.Action == "carrier_booking.booked");
    }

    [Fact]
    public async Task BookAsync_SameIdempotencyKey_ReturnsTheSameBookingWithoutCallingTheCarrierAgain()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");

        var first = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None);
        var second = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None);

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(1, h.Provider.BookCalls);
    }

    [Fact]
    public async Task BookAsync_ManualCarrier_RequiresTheCarrierAwb()
    {
        var h = await CreateHarnessAsync();
        var manual = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "MANUAL");

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(manual.Id, null, null), "key-1", ActingUser(), CancellationToken.None));
        var booked = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(manual.Id, null, "MAN-1"), "key-2", ActingUser(), CancellationToken.None);
        Assert.Equal("MAN-1", booked!.CarrierAwb);
    }

    [Fact]
    public async Task BookAsync_CarrierAwbForANetworkCarrier_IsRejected()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, "X-1"), "key-1", ActingUser(), CancellationToken.None));
        Assert.Equal(0, h.Provider.BookCalls);
    }

    [Theory]
    [InlineData(ShipmentStatus.Sorted, ShipmentDirection.Outbound)]
    [InlineData(ShipmentStatus.HandedToCarrier, ShipmentDirection.Outbound)]
    [InlineData(ShipmentStatus.CustomsPrepared, ShipmentDirection.Inbound)]
    public async Task BookAsync_WrongStatusOrDirection_IsRejectedBeforeAnyWrite(ShipmentStatus status, ShipmentDirection direction)
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        h.Shipments.Status = status;
        h.Shipments.Direction = direction;

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None));
        Assert.Empty(h.DbContext.Set<CarrierBooking>());
        Assert.Equal(0, h.Provider.BookCalls);
    }

    [Fact]
    public async Task BookAsync_InactiveOrUnknownAccount_IsRejected()
    {
        var h = await CreateHarnessAsync();
        var inactive = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER", isActive: false);

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(inactive.Id, null, null), "key-1", ActingUser(), CancellationToken.None));
        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(Guid.NewGuid(), null, null), "key-2", ActingUser(), CancellationToken.None));
    }

    [Fact]
    public async Task BookAsync_ExistingLiveBooking_IsRejected()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None);

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-2", ActingUser(), CancellationToken.None));
    }

    [Fact]
    public async Task BookAsync_CarrierRejects_MarksFailedAndThrowsBookingFailed_AndReplayReturnsTheSameFailure()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        h.Provider.BookException = new CarrierBookingException("Destination postcode invalid");

        var ex = await Assert.ThrowsAsync<CarrierBookingFailedException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None));
        Assert.Contains("Destination postcode invalid", ex.Message);
        var stored = await h.DbContext.Set<CarrierBooking>().SingleAsync();
        Assert.Equal(CarrierBookingStatus.Failed, stored.Status);

        await Assert.ThrowsAsync<CarrierBookingFailedException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None));
        Assert.Equal(1, h.Provider.BookCalls);

        h.Provider.BookException = null;
        var retried = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-2", ActingUser(), CancellationToken.None);
        Assert.Equal("Booked", retried!.Status);
    }

    [Fact]
    public async Task BookAsync_CircuitOpen_MarksFailedAndThrowsUnavailable()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        h.Provider.BookException = new BrokenCircuitException("open");

        await Assert.ThrowsAsync<CarrierUnavailableException>(() =>
            h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None));
        Assert.Equal(CarrierBookingStatus.Failed, (await h.DbContext.Set<CarrierBooking>().SingleAsync()).Status);
    }

    [Fact]
    public async Task BookAsync_NotVisibleToTheCaller_ReturnsNull()
    {
        var h = await CreateHarnessAsync(new HashSet<Guid> { Guid.NewGuid() });
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");

        Assert.Null(await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None));
        Assert.Equal(0, h.Provider.BookCalls);
    }

    [Fact]
    public async Task CancelAsync_BeforeHandoff_CancelsAtTheCarrier()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        var booked = await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None);

        var cancelled = await h.Service.CancelAsync(h.ShipmentId, ActingUser(), CancellationToken.None);

        Assert.Equal("Cancelled", cancelled!.Status);
        Assert.Contains(booked!.CarrierAwb, h.Provider.CancelledAwbs);
    }

    [Fact]
    public async Task CancelAsync_AfterHandoff_IsRejected()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");
        await h.Service.BookAsync(h.ShipmentId, new BookCarrierRequest(account.Id, null, null), "key-1", ActingUser(), CancellationToken.None);
        h.Shipments.Status = ShipmentStatus.HandedToCarrier;

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() => h.Service.CancelAsync(h.ShipmentId, ActingUser(), CancellationToken.None));
        Assert.Empty(h.Provider.CancelledAwbs);
    }

    [Fact]
    public async Task LinkInboundAsync_CreatesBookedLink_SchedulesAPoll_AndRequeuesUnmatchedEvents()
    {
        var h = await CreateHarnessAsync();
        h.Shipments.Status = ShipmentStatus.Created;
        h.Shipments.Direction = ShipmentDirection.Inbound;
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER", pollingEnabled: true);
        var early = await CarrierTestData.SeedEventAsync(h.DbContext, h.OrganizationId, account.Id, "FOREIGN-1", "TRANSIT", DateTimeOffset.UtcNow, CarrierEventProcessingStatus.Unmatched);

        var linked = await h.Service.LinkInboundAsync(h.ShipmentId, new LinkInboundBookingRequest(account.Id, " FOREIGN-1 ", null), ActingUser(), CancellationToken.None);

        Assert.Equal("Booked", linked!.Status);
        Assert.Equal("Inbound", linked.Direction);
        Assert.Equal("FOREIGN-1", linked.CarrierAwb);
        Assert.Equal(0, h.Provider.BookCalls);
        Assert.Contains((h.OrganizationId, linked.Id), h.Scheduler.Polls);
        h.DbContext.ChangeTracker.Clear();
        Assert.Equal(CarrierEventProcessingStatus.Pending, (await h.DbContext.Set<CarrierTrackingEvent>().SingleAsync(e => e.Id == early.Id)).ProcessingStatus);
        Assert.True(h.Scheduler.ProcessingEnqueued > 0);
    }

    [Fact]
    public async Task LinkInboundAsync_OutboundShipment_IsRejected()
    {
        var h = await CreateHarnessAsync();
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "TESTCARRIER");

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.LinkInboundAsync(h.ShipmentId, new LinkInboundBookingRequest(account.Id, "X-1", null), ActingUser(), CancellationToken.None));
    }
}
```

This test uses `CarrierBooking.LabelContentType` — add it in Step 3.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/International.Tests --filter "FullyQualifiedName~CarrierBookingServiceTests"` — Expected: FAIL (types missing).

- [ ] **Step 3: Add `LabelContentType` to `CarrierBooking`**

In `src/Modules/International/Domain/CarrierBooking.cs`, after `LabelStorageKey` add `public string? LabelContentType { get; set; }`. In `CarrierBookingConfiguration`, add `builder.Property(b => b.LabelContentType).HasMaxLength(100);`. Then:

Run: `dotnet ef migrations add AddCarrierBookingLabelContentType --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj` — inspect: a single `AddColumn` on `CarrierBookings`.
Run: `dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`.

- [ ] **Step 4: Implement DTOs and the service**

Append to `src/Modules/International/Application/CarrierDtos.cs`:

```csharp
public record BookCarrierRequest(Guid CarrierAccountId, string? ServiceLevel, string? CarrierAwb);

public record LinkInboundBookingRequest(Guid CarrierAccountId, string CarrierAwb, string? ServiceLevel);

public record CarrierBookingResponse(
    Guid Id, Guid CarrierAccountId, string CarrierCode, string CarrierAccountName, string Direction, string Status,
    string? CarrierAwb, string? ServiceLevel, bool HasLabel, string? FailureReason,
    DateTimeOffset? BookedAt, DateTimeOffset? CancelledAt, DateTimeOffset? CustodyStartedAt, DateTimeOffset CreatedAt);
```

`src/Modules/International/Application/CarrierBookingService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courier.International.Application;

public class CarrierBookingService
{
    private const int MaxIdempotencyKeyLength = 200;
    private const int MaxCarrierAwbLength = 100;
    private const int MaxServiceLevelLength = 100;
    private const int MaxFailureReasonLength = 1000;

    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly ICarrierProviderRegistry _registry;
    private readonly ICarrierSecretProtector _secretProtector;
    private readonly IObjectStorage _objectStorage;
    private readonly ICarrierJobScheduler _scheduler;
    private readonly ILogger<CarrierBookingService> _logger;

    public CarrierBookingService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger,
        IShipmentTransitionService shipmentTransitionService, IBranchAuthorizationChecker branchAuthorizationChecker,
        ICarrierProviderRegistry registry, ICarrierSecretProtector secretProtector, IObjectStorage objectStorage,
        ICarrierJobScheduler scheduler, ILogger<CarrierBookingService> logger)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _shipmentTransitionService = shipmentTransitionService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _registry = registry;
        _secretProtector = secretProtector;
        _objectStorage = objectStorage;
        _scheduler = scheduler;
        _logger = logger;
    }

    public async Task<CarrierBookingResponse?> BookAsync(Guid shipmentId, BookCarrierRequest request, string? idempotencyKey, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganization();
        var key = idempotencyKey?.Trim() ?? string.Empty;
        if (key.Length == 0 || key.Length > MaxIdempotencyKeyLength)
        {
            throw new InternationalShipmentValidationException($"An Idempotency-Key header of at most {MaxIdempotencyKeyLength} characters is required.");
        }

        var target = await LoadVisibleAsync(shipmentId, actingUser, "international.book", cancellationToken);
        if (target is null)
        {
            return null;
        }
        var (leg, scan) = target.Value;

        // Idempotent replay: same key -> same outcome, the carrier is never called twice.
        var replay = await _dbContext.Set<CarrierBooking>().FirstOrDefaultAsync(b => b.IdempotencyKey == key, cancellationToken);
        if (replay is not null)
        {
            if (replay.InternationalLegId != leg.Id || replay.CarrierAccountId != request.CarrierAccountId)
            {
                throw new InternationalShipmentValidationException("This Idempotency-Key was already used for a different booking.");
            }
            return replay.Status switch
            {
                CarrierBookingStatus.Failed => throw new CarrierBookingFailedException(replay.FailureReason ?? "Carrier booking failed."),
                CarrierBookingStatus.Pending => throw new InternationalShipmentValidationException("This booking is still in progress."),
                _ => await ToResponseAsync(replay, cancellationToken)
            };
        }

        if (scan.Direction != ShipmentDirection.Outbound)
        {
            throw new InternationalShipmentValidationException("Only outbound shipments are booked with a carrier; link an inbound carrier booking instead.");
        }
        if (scan.CurrentStatus != ShipmentStatus.CustomsPrepared)
        {
            throw new InternationalShipmentValidationException($"Shipment must be CustomsPrepared to book a carrier, was {scan.CurrentStatus}.");
        }
        await EnsureNoLiveBookingAsync(leg.Id, cancellationToken);
        var (account, provider) = await RequireActiveAccountAsync(request.CarrierAccountId, cancellationToken);

        var serviceLevel = NormalizeOptional(request.ServiceLevel, MaxServiceLevelLength, "Service level");
        string? manualAwb = null;
        if (provider.Capabilities.RequiresManualAwb)
        {
            manualAwb = NormalizeRequired(request.CarrierAwb, MaxCarrierAwbLength, "Carrier AWB");
            await EnsureCarrierAwbFreeAsync(account.Id, manualAwb, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.CarrierAwb))
        {
            throw new InternationalShipmentValidationException($"Carrier '{account.CarrierCode}' assigns its own AWB; do not send one.");
        }

        var shipmentDetail = await _shipmentTransitionService.GetResponseAsync(shipmentId, cancellationToken)
            ?? throw new InternationalShipmentValidationException("Shipment not found.");

        var now = DateTimeOffset.UtcNow;
        var booking = new CarrierBooking
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            InternationalLegId = leg.Id,
            CarrierAccountId = account.Id,
            Direction = CarrierBookingDirection.Outbound,
            ServiceLevel = serviceLevel,
            Status = CarrierBookingStatus.Pending,
            IdempotencyKey = key,
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.Set<CarrierBooking>().Add(booking);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race on the one-live-booking-per-leg or idempotency-key unique index.
            throw new InternationalShipmentValidationException("A booking for this shipment is already in progress or booked.");
        }

        var credentials = _secretProtector.UnprotectCredentials(account.EncryptedCredentials);
        CarrierBookingResult result;
        try
        {
            result = await provider.BookAsync(new CarrierBookingRequest(shipmentId, scan.Awb, serviceLevel, manualAwb, shipmentDetail.WeightKg), credentials, key, cancellationToken);
        }
        catch (Exception ex) when (CarrierCallFailures.IsCarrierUnavailable(ex))
        {
            await MarkFailedAsync(booking, "Carrier is temporarily unavailable.", actingUser);
            throw new CarrierUnavailableException($"Carrier '{account.CarrierCode}' is temporarily unavailable. Try again later.");
        }
        catch (Exception ex) when (CarrierCallFailures.IsCarrierFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            var reason = CarrierPayloadSanitizer.MaskSecrets(ex.Message, credentials.SecretValues);
            await MarkFailedAsync(booking, reason, actingUser);
            throw new CarrierBookingFailedException(Truncate(reason, MaxFailureReasonLength));
        }

        // The carrier has booked: persist with CancellationToken.None so a client disconnect can
        // never lose a real carrier booking.
        booking.Status = CarrierBookingStatus.Booked;
        booking.CarrierAwb = Truncate(result.CarrierAwb.Trim(), MaxCarrierAwbLength);
        booking.ServiceLevel = NormalizeOptionalOrNull(result.ServiceLevel, MaxServiceLevelLength) ?? serviceLevel;
        booking.RawResponse = CarrierPayloadSanitizer.ToStoredJson(result.RawResponse, credentials.SecretValues);
        booking.BookedAt = DateTimeOffset.UtcNow;
        booking.UpdatedAt = booking.BookedAt.Value;
        if (result.Label is not null)
        {
            using var content = new MemoryStream(result.Label.Content);
            booking.LabelStorageKey = await _objectStorage.UploadAsync($"carrier-labels/{organizationId}/{booking.Id}", content, result.Label.ContentType, CancellationToken.None);
            booking.LabelContentType = result.Label.ContentType;
        }
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        await RequeueUnmatchedAsync(account.Id, booking.CarrierAwb, CancellationToken.None);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "carrier_booking.booked", "CarrierBooking", booking.Id.ToString(),
            After: booking.CarrierAwb), CancellationToken.None);
        return ToResponse(booking, account);
    }

    public async Task<CarrierBookingResponse?> CancelAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganization();
        var target = await LoadVisibleAsync(shipmentId, actingUser, "international.book", cancellationToken);
        if (target is null)
        {
            return null;
        }
        var (leg, scan) = target.Value;
        var booking = await _dbContext.Set<CarrierBooking>().FirstOrDefaultAsync(b => b.InternationalLegId == leg.Id && b.Status == CarrierBookingStatus.Booked, cancellationToken)
            ?? throw new InternationalShipmentValidationException("There is no booked carrier booking to cancel.");
        if (booking.Direction != CarrierBookingDirection.Outbound || scan.CurrentStatus != ShipmentStatus.CustomsPrepared)
        {
            throw new InternationalShipmentValidationException("A carrier booking can only be cancelled before the parcel is handed to the carrier.");
        }
        var account = await _dbContext.Set<CarrierAccount>().FirstAsync(a => a.Id == booking.CarrierAccountId, cancellationToken);
        var provider = _registry.Find(account.CarrierCode)
            ?? throw new InternationalShipmentValidationException($"Carrier '{account.CarrierCode}' is no longer registered.");

        var credentials = _secretProtector.UnprotectCredentials(account.EncryptedCredentials);
        try
        {
            await provider.CancelAsync(booking.CarrierAwb!, credentials, cancellationToken);
        }
        catch (Exception ex) when (CarrierCallFailures.IsCarrierUnavailable(ex))
        {
            throw new CarrierUnavailableException($"Carrier '{account.CarrierCode}' is temporarily unavailable. Try again later.");
        }
        catch (Exception ex) when (CarrierCallFailures.IsCarrierFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            throw new CarrierBookingFailedException(Truncate(CarrierPayloadSanitizer.MaskSecrets(ex.Message, credentials.SecretValues), MaxFailureReasonLength));
        }

        booking.Status = CarrierBookingStatus.Cancelled;
        booking.CancelledAt = DateTimeOffset.UtcNow;
        booking.UpdatedAt = booking.CancelledAt.Value;
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "carrier_booking.cancelled", "CarrierBooking", booking.Id.ToString()), CancellationToken.None);
        return ToResponse(booking, account);
    }

    public async Task<CarrierBookingResponse?> LinkInboundAsync(Guid shipmentId, LinkInboundBookingRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganization();
        var target = await LoadVisibleAsync(shipmentId, actingUser, "international.book", cancellationToken);
        if (target is null)
        {
            return null;
        }
        var (leg, scan) = target.Value;
        if (scan.Direction != ShipmentDirection.Inbound)
        {
            throw new InternationalShipmentValidationException("Only inbound shipments can be linked to an existing carrier booking.");
        }
        if (scan.CurrentStatus != ShipmentStatus.Created)
        {
            throw new InternationalShipmentValidationException($"An inbound carrier booking can only be linked while the shipment is Created, was {scan.CurrentStatus}.");
        }
        await EnsureNoLiveBookingAsync(leg.Id, cancellationToken);
        var (account, provider) = await RequireActiveAccountAsync(request.CarrierAccountId, cancellationToken);
        var carrierAwb = NormalizeRequired(request.CarrierAwb, MaxCarrierAwbLength, "Carrier AWB");
        await EnsureCarrierAwbFreeAsync(account.Id, carrierAwb, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var booking = new CarrierBooking
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            InternationalLegId = leg.Id,
            CarrierAccountId = account.Id,
            Direction = CarrierBookingDirection.Inbound,
            CarrierAwb = carrierAwb,
            ServiceLevel = NormalizeOptional(request.ServiceLevel, MaxServiceLevelLength, "Service level"),
            Status = CarrierBookingStatus.Booked,
            IdempotencyKey = $"inbound-link:{Guid.NewGuid():N}",
            BookedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.Set<CarrierBooking>().Add(booking);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new InternationalShipmentValidationException("This shipment already has a carrier booking, or this carrier AWB is already linked.");
        }
        await RequeueUnmatchedAsync(account.Id, carrierAwb, cancellationToken);
        if (account.PollingEnabled && provider.Capabilities.SupportsPolling)
        {
            _scheduler.EnqueuePoll(organizationId, booking.Id);
        }

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "carrier_booking.inbound_linked", "CarrierBooking", booking.Id.ToString(),
            After: carrierAwb), cancellationToken);
        return ToResponse(booking, account);
    }

    public static CarrierBookingResponse ToResponse(CarrierBooking booking, CarrierAccount account) => new(
        booking.Id, account.Id, account.CarrierCode, account.DisplayName, booking.Direction.ToString(), booking.Status.ToString(),
        booking.CarrierAwb, booking.ServiceLevel, booking.LabelStorageKey is not null, booking.FailureReason,
        booking.BookedAt, booking.CancelledAt, booking.CustodyStartedAt, booking.CreatedAt);

    private async Task<CarrierBookingResponse> ToResponseAsync(CarrierBooking booking, CancellationToken cancellationToken) =>
        ToResponse(booking, await _dbContext.Set<CarrierAccount>().FirstAsync(a => a.Id == booking.CarrierAccountId, cancellationToken));

    private Guid RequireOrganization() =>
        _currentTenantProvider.OrganizationId ?? throw new InternationalShipmentValidationException("No active organization context.");

    // null = not found / not in tenant / not visible to the caller (-> 404).
    private async Task<(InternationalLeg Leg, ShipmentScanInfo Scan)?> LoadVisibleAsync(Guid shipmentId, ClaimsPrincipal actingUser, string permission, CancellationToken cancellationToken)
    {
        var leg = await _dbContext.Set<InternationalLeg>().FirstOrDefaultAsync(l => l.ShipmentId == shipmentId, cancellationToken);
        if (leg is null)
        {
            return null;
        }
        var scan = await _shipmentTransitionService.GetScanInfoAsync(shipmentId, cancellationToken);
        if (scan is null || !await InternationalShipmentAccess.IsVisibleAsync(_branchAuthorizationChecker, actingUser, permission,
                scan.OriginBranchId, scan.DestinationBranchId, scan.CurrentBranchId, cancellationToken))
        {
            return null;
        }
        return (leg, scan);
    }

    private async Task EnsureNoLiveBookingAsync(Guid legId, CancellationToken cancellationToken)
    {
        if (await _dbContext.Set<CarrierBooking>().AnyAsync(b => b.InternationalLegId == legId
                && (b.Status == CarrierBookingStatus.Pending || b.Status == CarrierBookingStatus.Booked), cancellationToken))
        {
            throw new InternationalShipmentValidationException("This shipment already has a carrier booking. Cancel it first.");
        }
    }

    private async Task EnsureCarrierAwbFreeAsync(Guid accountId, string carrierAwb, CancellationToken cancellationToken)
    {
        if (await _dbContext.Set<CarrierBooking>().AnyAsync(b => b.CarrierAccountId == accountId && b.CarrierAwb == carrierAwb
                && b.Status == CarrierBookingStatus.Booked, cancellationToken))
        {
            throw new InternationalShipmentValidationException("This carrier AWB is already linked to another shipment on this carrier account.");
        }
    }

    private async Task<(CarrierAccount Account, ICarrierProvider Provider)> RequireActiveAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _dbContext.Set<CarrierAccount>().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null || !account.IsActive)
        {
            throw new InternationalShipmentValidationException("Carrier account not found or inactive.");
        }
        var provider = _registry.Find(account.CarrierCode)
            ?? throw new InternationalShipmentValidationException($"Carrier '{account.CarrierCode}' is not registered.");
        return (account, provider);
    }

    // Events that arrived before this booking existed (e.g. an early webhook) get another chance.
    private async Task RequeueUnmatchedAsync(Guid accountId, string carrierAwb, CancellationToken cancellationToken)
    {
        var unmatched = await _dbContext.Set<CarrierTrackingEvent>()
            .Where(e => e.CarrierAccountId == accountId && e.CarrierAwb == carrierAwb && e.ProcessingStatus == CarrierEventProcessingStatus.Unmatched)
            .ToListAsync(cancellationToken);
        if (unmatched.Count == 0)
        {
            return;
        }
        foreach (var evt in unmatched)
        {
            evt.ProcessingStatus = CarrierEventProcessingStatus.Pending;
            evt.ProcessingNote = null;
            evt.ProcessedAt = null;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        _scheduler.EnqueueProcessing();
    }

    private async Task MarkFailedAsync(CarrierBooking booking, string reason, ClaimsPrincipal actingUser)
    {
        booking.Status = CarrierBookingStatus.Failed;
        booking.FailureReason = Truncate(reason, MaxFailureReasonLength);
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        _logger.LogWarning("Carrier booking {CarrierBookingId} failed.", booking.Id);
        await _auditLogger.LogAsync(new AuditEntry(booking.OrganizationId, actingUser.GetUserId(), "carrier_booking.failed", "CarrierBooking", booking.Id.ToString(),
            After: booking.FailureReason), CancellationToken.None);
    }

    private static string NormalizeRequired(string? value, int maxLength, string field)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
        {
            throw new InternationalShipmentValidationException($"{field} is required and must be at most {maxLength} characters.");
        }
        return trimmed;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }
        if (trimmed.Length > maxLength)
        {
            throw new InternationalShipmentValidationException($"{field} must be at most {maxLength} characters.");
        }
        return trimmed;
    }

    private static string? NormalizeOptionalOrNull(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), maxLength);

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
```

Run: `dotnet test tests/International.Tests --filter "FullyQualifiedName~CarrierBookingServiceTests"` — Expected: PASS.

- [ ] **Step 5: Routes, idempotency middleware, DI**

In `src/Modules/International/Presentation/InternationalEndpoints.cs`, inside `MapInternationalEndpoints` after the `prepare-customs` route, add:

```csharp
        group.MapPost("/{id:guid}/book", (Guid id, BookCarrierRequest request, CarrierBookingService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.BookAsync(id, request, httpContext.Request.Headers["Idempotency-Key"].ToString(), httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.book");

        group.MapPost("/{id:guid}/booking/cancel", (Guid id, CarrierBookingService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.CancelAsync(id, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.book");

        group.MapPost("/{id:guid}/inbound-booking", (Guid id, LinkInboundBookingRequest request, CarrierBookingService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.LinkInboundAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.book");
```

In `src/Api/Middleware/IdempotencyMiddleware.cs`, add a pattern next to the rider patterns:

```csharp
    private static readonly Regex InternationalBookPattern = new(@"^/api/v1/international/shipments/[0-9a-fA-F-]{36}/book$", RegexOptions.Compiled);
```

and extend the second `if` in `ClassifyRoute` to include it (staff-user partition — `RouteKind.RiderAction` partitions by the staff user id, which is what booking needs):

```csharp
        // RiderAction = partitioned per staff user ("staff:{userId}"); also used for carrier booking.
        if (RiderScanPickupPattern.IsMatch(path) || RiderDeliveryFailPattern.IsMatch(path) ||
            RiderDeliveryCompletePattern.IsMatch(path) || RiderPodUploadsPattern.IsMatch(path) ||
            InternationalBookPattern.IsMatch(path))
```

In `src/Api/Program.cs`, register `builder.Services.AddScoped<Courier.International.Application.CarrierBookingService>();`.

- [ ] **Step 6: Write the API tests**

`tests/Api.Tests/International/CarrierBookingEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Courier.International.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierBookingEndpointsTests : InternationalApiTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public CarrierBookingEndpointsTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private static async Task<Guid> CreateManualAccountAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/international/carrier-accounts",
            new { carrierCode = "MANUAL", displayName = "Manual", credentials = (object?)null, pollingEnabled = false, generateWebhookSecret = false });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CarrierAccountSecretResponse>())!.Account.Id;
    }

    private static Task<HttpResponseMessage> BookAsync(HttpClient client, Guid shipmentId, Guid accountId, string carrierAwb, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/international/shipments/{shipmentId}/book")
        {
            Content = JsonContent.Create(new { carrierAccountId = accountId, serviceLevel = (string?)null, carrierAwb })
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Book_Replay_Cancel_Rebook_ManualCarrier()
    {
        var org = await SetUpOrgAsync("book-flow");
        var accountId = await CreateManualAccountAsync(org.Client);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        var key = Guid.NewGuid().ToString("N");

        var first = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-100", key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var booked = (await first.Content.ReadFromJsonAsync<CarrierBookingResponse>())!;
        Assert.Equal("Booked", booked.Status);
        Assert.Equal("MAN-100", booked.CarrierAwb);

        var replay = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-100", key);
        Assert.Equal(booked.Id, (await replay.Content.ReadFromJsonAsync<CarrierBookingResponse>())!.Id);

        var cancel = await org.Client.PostAsync($"/api/v1/international/shipments/{shipment.Shipment.Id}/booking/cancel", null);
        Assert.Equal("Cancelled", (await cancel.Content.ReadFromJsonAsync<CarrierBookingResponse>())!.Status);

        var rebook = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-101", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, rebook.StatusCode);
    }

    [Fact]
    public async Task Book_WithoutIdempotencyKey_Returns400()
    {
        var org = await SetUpOrgAsync("book-nokey");
        var accountId = await CreateManualAccountAsync(org.Client);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);

        var response = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-1", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("IDEMPOTENCY_KEY_REQUIRED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Book_BeforeCustomsPrepared_Returns400()
    {
        var org = await SetUpOrgAsync("book-early");
        var accountId = await CreateManualAccountAsync(org.Client);
        var shipment = await CreateOutboundAsync(org);

        var response = await BookAsync(org.Client, shipment.Shipment.Id, accountId, "MAN-1", Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INTERNATIONAL_SHIPMENT_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LinkInbound_OnCreatedInboundShipment_Succeeds_AndOnOutbound_Returns400()
    {
        var org = await SetUpOrgAsync("link");
        var accountId = await CreateManualAccountAsync(org.Client);
        var inbound = await CreateInboundAsync(org);
        var outbound = await CreateOutboundAsync(org);

        var link = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{inbound.Shipment.Id}/inbound-booking",
            new { carrierAccountId = accountId, carrierAwb = "FOREIGN-1", serviceLevel = (string?)null });
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        Assert.Equal("Inbound", (await link.Content.ReadFromJsonAsync<CarrierBookingResponse>())!.Direction);

        var wrong = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{outbound.Shipment.Id}/inbound-booking",
            new { carrierAccountId = accountId, carrierAwb = "FOREIGN-2", serviceLevel = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
    }

    [Fact]
    public async Task OtherTenantsShipment_Returns404_AndMissingPermission_Returns403()
    {
        var owner = await SetUpOrgAsync("book-owner");
        var other = await SetUpOrgAsync("book-other");
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(owner);
        var otherAccount = await CreateManualAccountAsync(other.Client);

        var crossTenant = await BookAsync(other.Client, shipment.Shipment.Id, otherAccount, "MAN-1", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        var readOnly = await SeedUserClientAsync(owner.Auth, new[] { "international.read" }, null);
        var forbidden = await BookAsync(readOnly, shipment.Shipment.Id, Guid.NewGuid(), "MAN-1", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task UserScopedToAnotherBranch_Returns404()
    {
        var org = await SetUpOrgAsync("book-scope");
        var accountId = await CreateManualAccountAsync(org.Client);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        var otherBranch = await CreateBranchAsync(org.Client, $"OB-{Guid.NewGuid():N}"[..10]);
        var scoped = await SeedUserClientAsync(org.Auth, new[] { "international.book" }, otherBranch);

        var response = await BookAsync(scoped, shipment.Shipment.Id, accountId, "MAN-1", Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 7: Run tests**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CarrierBookingEndpointsTests"` — Expected: PASS (6 tests).
Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~Idempotency"` — Expected: PASS (existing idempotency behavior unchanged).
Run: `dotnet build Courier.slnx` — Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/International/ src/Api/ src/Persistence/ tests/International.Tests/ tests/Api.Tests/International/
git commit -m "feat(international): add idempotent carrier booking, cancellation and inbound linking

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---
### Task 13: Carrier webhooks and manual carrier events

**Files:**
- Modify: `src/Modules/International/Application/CarrierDtos.cs`
- Create: `src/Modules/International/Application/CarrierWebhookService.cs`
- Create: `src/Modules/International/Application/ManualCarrierEventService.cs`
- Create: `src/Modules/International/Presentation/CarrierWebhookEndpoints.cs`
- Modify: `src/Modules/International/Presentation/InternationalEndpoints.cs` (manual events route)
- Modify: `src/Api/Program.cs` (DI + `app.MapCarrierWebhookEndpoints();`)
- Test: `tests/International.Tests/CarrierWebhookServiceTests.cs`, `ManualCarrierEventServiceTests.cs`; `tests/Api.Tests/International/CarrierEventEndpointsTests.cs`

**Interfaces:**
- Consumes: Task 11 `CarrierTrackingEventWriter`, `ICarrierJobScheduler`; Task 8 contracts; Task 10 `InternationalEndpointErrors`.
- Produces:
  - `record ManualCarrierEventRequest(string StatusCode, DateTimeOffset OccurredAt, string? Location, string? Description)`
  - `record CarrierTrackingEventResponse(Guid Id, string Source, string CarrierAwb, string CarrierStatusCode, string? Description, string? Location, DateTimeOffset OccurredAt, DateTimeOffset ReceivedAt, string ProcessingStatus, string? ProcessingNote, string? MappedShipmentStatus)` with `static CarrierTrackingEventResponse From(CarrierTrackingEvent e)`
  - `enum CarrierWebhookOutcome { Accepted, NotFound, InvalidSignature, InvalidPayload }`; `CarrierWebhookService.ReceiveAsync(string webhookPublicId, IReadOnlyDictionary<string, string> headers, byte[] body, CancellationToken ct) : Task<CarrierWebhookOutcome>`
  - `ManualCarrierEventService.RecordAsync(Guid shipmentId, ManualCarrierEventRequest request, ClaimsPrincipal actingUser, CancellationToken ct) : Task<CarrierTrackingEventResponse?>`
  - Routes: anonymous `POST /api/v1/carrier-webhooks/{webhookPublicId}` (202 / 401 / 404 / 400 `CARRIER_WEBHOOK_INVALID_PAYLOAD` / 413); `POST /api/v1/international/shipments/{id}/carrier-events` (`international.tracking.manage`, 202).

- [ ] **Step 1: Write the failing unit tests**

`tests/International.Tests/CarrierWebhookServiceTests.cs`:

```csharp
using System.Text.Json;
using Courier.International.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.International.Infrastructure.Carriers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Courier.International.Tests;

public class CarrierWebhookServiceTests
{
    private const string Secret = "whsec_test_secret";

    private sealed record Harness(CarrierTestDbContext DbContext, CarrierWebhookService Service, RecordingJobScheduler Scheduler, ICarrierSecretProtector Protector);

    private static Harness CreateHarness()
    {
        var dbContext = CarrierTestData.NewContext();
        var protector = new DataProtectionCarrierSecretProtector(new EphemeralDataProtectionProvider());
        var scheduler = new RecordingJobScheduler();
        var registry = new CarrierProviderRegistry(new ICarrierProvider[] { new TestCarrierProvider(), new ManualCarrierProvider() });
        var service = new CarrierWebhookService(dbContext, registry, protector,
            new CarrierTrackingEventWriter(dbContext, NullLogger<CarrierTrackingEventWriter>.Instance), scheduler, NullLogger<CarrierWebhookService>.Instance);
        return new Harness(dbContext, service, scheduler, protector);
    }

    private static Task<CarrierAccount> SeedWebhookAccountAsync(Harness h, Guid organizationId, bool isActive = true) =>
        CarrierTestData.SeedAccountAsync(h.DbContext, organizationId, "TESTCARRIER", isActive, encryptedWebhookSecret: h.Protector.ProtectWebhookSecret(Secret));

    private static Dictionary<string, string> SignedHeaders(string secret = Secret) =>
        new(StringComparer.OrdinalIgnoreCase) { [TestCarrierProvider.SignatureHeader] = secret };

    private static byte[] Body(params CarrierTrackingUpdate[] updates) =>
        JsonSerializer.SerializeToUtf8Bytes(updates, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static CarrierTrackingUpdate Update(string eventId) =>
        new("AWB-1", eventId, "TRANSIT", null, "Dubai", DateTimeOffset.UtcNow.AddMinutes(-5), null);

    [Fact]
    public async Task ReceiveAsync_ValidSignature_StoresEventsUnderTheAccountsTenant_AndEnqueuesProcessing()
    {
        var h = CreateHarness();
        var organizationId = Guid.NewGuid();
        var account = await SeedWebhookAccountAsync(h, organizationId);

        var outcome = await h.Service.ReceiveAsync(account.WebhookPublicId, SignedHeaders(), Body(Update("w1"), Update("w2")), CancellationToken.None);

        Assert.Equal(CarrierWebhookOutcome.Accepted, outcome);
        var stored = await h.DbContext.Set<CarrierTrackingEvent>().ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, e => Assert.Equal(organizationId, e.OrganizationId));
        Assert.Equal(1, h.Scheduler.ProcessingEnqueued);
    }

    [Fact]
    public async Task ReceiveAsync_DuplicateDelivery_IsStoredOnce()
    {
        var h = CreateHarness();
        var account = await SeedWebhookAccountAsync(h, Guid.NewGuid());

        await h.Service.ReceiveAsync(account.WebhookPublicId, SignedHeaders(), Body(Update("w1")), CancellationToken.None);
        var again = await h.Service.ReceiveAsync(account.WebhookPublicId, SignedHeaders(), Body(Update("w1")), CancellationToken.None);

        Assert.Equal(CarrierWebhookOutcome.Accepted, again);
        Assert.Equal(1, await h.DbContext.Set<CarrierTrackingEvent>().CountAsync());
    }

    [Fact]
    public async Task ReceiveAsync_BadSignature_StoresNothing()
    {
        var h = CreateHarness();
        var account = await SeedWebhookAccountAsync(h, Guid.NewGuid());

        var outcome = await h.Service.ReceiveAsync(account.WebhookPublicId, SignedHeaders("wrong"), Body(Update("w1")), CancellationToken.None);

        Assert.Equal(CarrierWebhookOutcome.InvalidSignature, outcome);
        Assert.Empty(h.DbContext.Set<CarrierTrackingEvent>());
    }

    [Fact]
    public async Task ReceiveWebhook_ValidSignatureMalformedBody_ReturnsInvalidPayload()
    {
        var h = CreateHarness();
        var account = await SeedWebhookAccountAsync(h, Guid.NewGuid());

        var outcome = await h.Service.ReceiveAsync(account.WebhookPublicId, SignedHeaders(), "{not json"u8.ToArray(), CancellationToken.None);

        Assert.Equal(CarrierWebhookOutcome.InvalidPayload, outcome);
        Assert.Empty(h.DbContext.Set<CarrierTrackingEvent>());
    }

    [Fact]
    public async Task ReceiveAsync_UnknownInactiveOrWebhooklessAccount_IsNotFound()
    {
        var h = CreateHarness();
        var inactive = await SeedWebhookAccountAsync(h, Guid.NewGuid(), isActive: false);
        var manual = await CarrierTestData.SeedAccountAsync(h.DbContext, Guid.NewGuid(), "MANUAL");

        Assert.Equal(CarrierWebhookOutcome.NotFound, await h.Service.ReceiveAsync("no-such-id", SignedHeaders(), Body(Update("w1")), CancellationToken.None));
        Assert.Equal(CarrierWebhookOutcome.NotFound, await h.Service.ReceiveAsync(inactive.WebhookPublicId, SignedHeaders(), Body(Update("w1")), CancellationToken.None));
        Assert.Equal(CarrierWebhookOutcome.NotFound, await h.Service.ReceiveAsync(manual.WebhookPublicId, SignedHeaders(), Body(Update("w1")), CancellationToken.None));
    }
}
```

`tests/International.Tests/ManualCarrierEventServiceTests.cs`:

```csharp
using System.Security.Claims;
using Courier.International.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.International.Infrastructure.Carriers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Courier.International.Tests;

public class ManualCarrierEventServiceTests
{
    private sealed class Harness
    {
        public required CarrierTestDbContext DbContext { get; init; }
        public required Guid OrganizationId { get; init; }
        public required Guid ShipmentId { get; init; }
        public required InternationalLeg Leg { get; init; }
        public required RecordingJobScheduler Scheduler { get; init; }
        public required RecordingAuditLogger Audit { get; init; }
        public required ManualCarrierEventService Service { get; init; }
    }

    private static async Task<Harness> CreateHarnessAsync(IReadOnlySet<Guid>? accessibleBranchIds = null)
    {
        var dbContext = CarrierTestData.NewContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var leg = await CarrierTestData.SeedLegAsync(dbContext, organizationId, shipmentId);
        var scheduler = new RecordingJobScheduler();
        var audit = new RecordingAuditLogger();
        var service = new ManualCarrierEventService(dbContext, new FixedTenantProvider(organizationId), audit, new FixedShipmentTransitionService(),
            new FixedBranchAuthorizationChecker(accessibleBranchIds), new CarrierProviderRegistry(new ICarrierProvider[] { new ManualCarrierProvider() }),
            new CarrierTrackingEventWriter(dbContext, NullLogger<CarrierTrackingEventWriter>.Instance), scheduler);
        return new Harness { DbContext = dbContext, OrganizationId = organizationId, ShipmentId = shipmentId, Leg = leg, Scheduler = scheduler, Audit = audit, Service = service };
    }

    private static async Task BookManualAsync(Harness h)
    {
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "MANUAL");
        await CarrierTestData.SeedBookingAsync(h.DbContext, h.OrganizationId, h.Leg.Id, account.Id, CarrierBookingStatus.Booked, "MAN-1");
    }

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));

    [Fact]
    public async Task RecordAsync_KnownCode_StoresAManualPendingEvent()
    {
        var h = await CreateHarnessAsync();
        await BookManualAsync(h);
        var userId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow.AddHours(-1);

        var result = await h.Service.RecordAsync(h.ShipmentId, new ManualCarrierEventRequest(" in_transit ", occurredAt, "Delhi", "Departed hub"), ActingUser(userId), CancellationToken.None);

        Assert.Equal("Pending", result!.ProcessingStatus);
        Assert.Equal("IN_TRANSIT", result.CarrierStatusCode);
        var stored = await h.DbContext.Set<CarrierTrackingEvent>().SingleAsync();
        Assert.Equal(CarrierEventSource.Manual, stored.Source);
        Assert.Equal(userId, stored.EnteredByUserId);
        Assert.Equal("MAN-1", stored.CarrierAwb);
        Assert.Equal(1, h.Scheduler.ProcessingEnqueued);
        Assert.Contains(h.Audit.Entries, e => e.Action == "carrier_event.manual_recorded");
    }

    [Fact]
    public async Task RecordAsync_UnknownCode_IsRejected()
    {
        var h = await CreateHarnessAsync();
        await BookManualAsync(h);

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.RecordAsync(h.ShipmentId, new ManualCarrierEventRequest("TELEPORTED", DateTimeOffset.UtcNow, null, null), ActingUser(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_FutureOccurredAt_IsRejected()
    {
        var h = await CreateHarnessAsync();
        await BookManualAsync(h);

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.RecordAsync(h.ShipmentId, new ManualCarrierEventRequest("IN_TRANSIT", DateTimeOffset.UtcNow.AddHours(1), null, null), ActingUser(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_WithoutABookedBooking_IsRejected()
    {
        var h = await CreateHarnessAsync();

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() =>
            h.Service.RecordAsync(h.ShipmentId, new ManualCarrierEventRequest("IN_TRANSIT", DateTimeOffset.UtcNow, null, null), ActingUser(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_NotVisible_ReturnsNull()
    {
        var h = await CreateHarnessAsync(new HashSet<Guid> { Guid.NewGuid() });
        await BookManualAsync(h);

        Assert.Null(await h.Service.RecordAsync(h.ShipmentId, new ManualCarrierEventRequest("IN_TRANSIT", DateTimeOffset.UtcNow, null, null), ActingUser(Guid.NewGuid()), CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/International.Tests --filter "FullyQualifiedName~CarrierWebhookServiceTests|FullyQualifiedName~ManualCarrierEventServiceTests"`
Expected: FAIL (types missing).

- [ ] **Step 3: Implement**

Append to `src/Modules/International/Application/CarrierDtos.cs`:

```csharp
public record ManualCarrierEventRequest(string StatusCode, DateTimeOffset OccurredAt, string? Location, string? Description);

// RawPayload is deliberately not exposed.
public record CarrierTrackingEventResponse(
    Guid Id, string Source, string CarrierAwb, string CarrierStatusCode, string? Description, string? Location,
    DateTimeOffset OccurredAt, DateTimeOffset ReceivedAt, string ProcessingStatus, string? ProcessingNote, string? MappedShipmentStatus)
{
    public static CarrierTrackingEventResponse From(Domain.CarrierTrackingEvent e) => new(
        e.Id, e.Source.ToString(), e.CarrierAwb, e.CarrierStatusCode, e.Description, e.Location,
        e.OccurredAt, e.ReceivedAt, e.ProcessingStatus.ToString(), e.ProcessingNote, e.MappedShipmentStatus);
}
```

`src/Modules/International/Application/CarrierWebhookService.cs`:

```csharp
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courier.International.Application;

public enum CarrierWebhookOutcome
{
    Accepted,
    NotFound,
    InvalidSignature,
    InvalidPayload
}

// Anonymous ingest: the webhook is authenticated only by the per-account signature, and the
// tenant comes only from the resolved account — never from the payload (I4 spec §5.5, §8).
public class CarrierWebhookService
{
    private readonly DbContext _dbContext;
    private readonly ICarrierProviderRegistry _registry;
    private readonly ICarrierSecretProtector _secretProtector;
    private readonly CarrierTrackingEventWriter _writer;
    private readonly ICarrierJobScheduler _scheduler;
    private readonly ILogger<CarrierWebhookService> _logger;

    public CarrierWebhookService(DbContext dbContext, ICarrierProviderRegistry registry, ICarrierSecretProtector secretProtector,
        CarrierTrackingEventWriter writer, ICarrierJobScheduler scheduler, ILogger<CarrierWebhookService> logger)
    {
        _dbContext = dbContext;
        _registry = registry;
        _secretProtector = secretProtector;
        _writer = writer;
        _scheduler = scheduler;
        _logger = logger;
    }

    public async Task<CarrierWebhookOutcome> ReceiveAsync(string webhookPublicId, IReadOnlyDictionary<string, string> headers, byte[] body, CancellationToken cancellationToken)
    {
        // Deliberate IgnoreQueryFilters: an anonymous request has no tenant; this single lookup by
        // the unguessable, platform-unique public id is what establishes it.
        var account = await _dbContext.Set<CarrierAccount>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.WebhookPublicId == webhookPublicId, cancellationToken);
        if (account is null || !account.IsActive || account.EncryptedWebhookSecret is null)
        {
            return CarrierWebhookOutcome.NotFound;
        }
        var provider = _registry.Find(account.CarrierCode);
        if (provider is null || !provider.Capabilities.SupportsWebhooks)
        {
            return CarrierWebhookOutcome.NotFound;
        }

        var webhookSecret = _secretProtector.UnprotectWebhookSecret(account.EncryptedWebhookSecret);
        if (!provider.VerifyWebhook(headers, body, webhookSecret))
        {
            _logger.LogWarning("Rejected a carrier webhook with an invalid signature for carrier account {CarrierAccountId}.", account.Id);
            return CarrierWebhookOutcome.InvalidSignature;
        }

        IReadOnlyList<CarrierTrackingUpdate> updates;
        try
        {
            updates = provider.ParseWebhook(body);
        }
        catch (CarrierWebhookPayloadException)
        {
            _logger.LogWarning("Rejected an unparseable carrier webhook for carrier account {CarrierAccountId}.", account.Id);
            return CarrierWebhookOutcome.InvalidPayload;
        }

        var credentials = _secretProtector.UnprotectCredentials(account.EncryptedCredentials);
        var inserted = await _writer.InsertAsync(account, updates, CarrierEventSource.Webhook, null,
            credentials.SecretValues.Append(webhookSecret), cancellationToken);
        if (inserted > 0)
        {
            _scheduler.EnqueueProcessing();
        }
        return CarrierWebhookOutcome.Accepted;
    }
}
```

`src/Modules/International/Application/ManualCarrierEventService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Application;

// Staff-entered carrier events (e.g. from a phone call or the carrier's own portal) — the manual
// carrier's only tracking source, available for any carrier as a fallback (I4 spec §5.5).
public class ManualCarrierEventService
{
    private const int MaxLocationLength = 300;
    private const int MaxDescriptionLength = 1000;
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;
    private readonly ICarrierProviderRegistry _registry;
    private readonly CarrierTrackingEventWriter _writer;
    private readonly ICarrierJobScheduler _scheduler;

    public ManualCarrierEventService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger,
        IShipmentTransitionService shipmentTransitionService, IBranchAuthorizationChecker branchAuthorizationChecker,
        ICarrierProviderRegistry registry, CarrierTrackingEventWriter writer, ICarrierJobScheduler scheduler)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _shipmentTransitionService = shipmentTransitionService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
        _registry = registry;
        _writer = writer;
        _scheduler = scheduler;
    }

    public async Task<CarrierTrackingEventResponse?> RecordAsync(Guid shipmentId, ManualCarrierEventRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new InternationalShipmentValidationException("No active organization context.");

        var leg = await _dbContext.Set<InternationalLeg>().FirstOrDefaultAsync(l => l.ShipmentId == shipmentId, cancellationToken);
        if (leg is null)
        {
            return null;
        }
        var scan = await _shipmentTransitionService.GetScanInfoAsync(shipmentId, cancellationToken);
        if (scan is null || !await InternationalShipmentAccess.IsVisibleAsync(_branchAuthorizationChecker, actingUser, "international.tracking.manage",
                scan.OriginBranchId, scan.DestinationBranchId, scan.CurrentBranchId, cancellationToken))
        {
            return null;
        }

        var booking = await _dbContext.Set<CarrierBooking>().FirstOrDefaultAsync(b => b.InternationalLegId == leg.Id && b.Status == CarrierBookingStatus.Booked, cancellationToken)
            ?? throw new InternationalShipmentValidationException("A booked carrier booking is required before recording carrier events.");
        var account = await _dbContext.Set<CarrierAccount>().FirstAsync(a => a.Id == booking.CarrierAccountId, cancellationToken);
        var provider = _registry.Find(account.CarrierCode)
            ?? throw new InternationalShipmentValidationException($"Carrier '{account.CarrierCode}' is not registered.");

        var statusCode = request.StatusCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (statusCode.Length == 0 || !provider.IsKnownStatusCode(statusCode))
        {
            throw new InternationalShipmentValidationException($"Unknown status code '{request.StatusCode}' for carrier '{account.CarrierCode}'.");
        }
        if (request.OccurredAt > DateTimeOffset.UtcNow + AllowedClockSkew)
        {
            throw new InternationalShipmentValidationException("OccurredAt cannot be in the future.");
        }
        var location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (location?.Length > MaxLocationLength || description?.Length > MaxDescriptionLength)
        {
            throw new InternationalShipmentValidationException($"Location must be at most {MaxLocationLength} and description at most {MaxDescriptionLength} characters.");
        }

        var eventId = $"manual-{Guid.NewGuid():N}";
        await _writer.InsertAsync(account,
            new[] { new CarrierTrackingUpdate(booking.CarrierAwb!, eventId, statusCode, description, location, request.OccurredAt, null) },
            CarrierEventSource.Manual, actingUser.GetUserId(), Array.Empty<string>(), cancellationToken);
        var stored = await _dbContext.Set<CarrierTrackingEvent>().FirstAsync(e => e.CarrierAccountId == account.Id && e.CarrierEventId == eventId, cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "carrier_event.manual_recorded", "CarrierTrackingEvent", stored.Id.ToString(),
            After: statusCode), cancellationToken);
        _scheduler.EnqueueProcessing();
        return CarrierTrackingEventResponse.From(stored);
    }
}
```

`src/Modules/International/Presentation/CarrierWebhookEndpoints.cs`:

```csharp
using Courier.International.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.International.Presentation;

public static class CarrierWebhookEndpoints
{
    public const int MaxBodyBytes = 1_048_576;

    public static void MapCarrierWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous by design — authenticated by the carrier account's webhook signature only.
        app.MapPost("/api/v1/carrier-webhooks/{webhookPublicId}", async (string webhookPublicId, HttpContext httpContext, CarrierWebhookService service) =>
        {
            var body = await ReadCappedBodyAsync(httpContext.Request, httpContext.RequestAborted);
            if (body is null)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }
            var headers = httpContext.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            var outcome = await service.ReceiveAsync(webhookPublicId, headers, body, httpContext.RequestAborted);
            return outcome switch
            {
                CarrierWebhookOutcome.Accepted => Results.Accepted(),
                CarrierWebhookOutcome.InvalidSignature => Results.Unauthorized(),
                CarrierWebhookOutcome.InvalidPayload => InternationalEndpointErrors.Error(StatusCodes.Status400BadRequest,
                    "CARRIER_WEBHOOK_INVALID_PAYLOAD", "The webhook body could not be parsed."),
                _ => Results.NotFound()
            };
        })
        .AllowAnonymous()
        .RequireIpRateLimit()
        .WithTags("Carrier Webhooks");
    }

    // null = larger than MaxBodyBytes (declared or actual).
    private static async Task<byte[]?> ReadCappedBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxBodyBytes)
        {
            return null;
        }
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
```

In `InternationalEndpoints.cs`, add after the `inbound-booking` route:

```csharp
        group.MapPost("/{id:guid}/carrier-events", (Guid id, ManualCarrierEventRequest request, ManualCarrierEventService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.RecordAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Accepted($"/api/v1/international/shipments/{id}", result);
            })).RequirePermission("international.tracking.manage");
```

In `Program.cs`: register `CarrierWebhookService` and `ManualCarrierEventService` (scoped), and add `app.MapCarrierWebhookEndpoints();` after `app.MapCarrierAccountEndpoints();`.

- [ ] **Step 4: Run the unit tests**

Run: `dotnet test tests/International.Tests` — Expected: PASS.

- [ ] **Step 5: Write the API tests**

`tests/Api.Tests/International/CarrierEventEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Courier.International.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierEventEndpointsTests : InternationalApiTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public CarrierEventEndpointsTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private async Task<(OrgContext Org, Guid ShipmentId, Guid AccountId)> BookedAndHandedOverAsync(string label)
    {
        var org = await SetUpOrgAsync(label);
        var account = await org.Client.PostAsJsonAsync("/api/v1/international/carrier-accounts",
            new { carrierCode = "MANUAL", displayName = "Manual", credentials = (object?)null, pollingEnabled = false, generateWebhookSecret = false });
        var accountId = (await account.Content.ReadFromJsonAsync<CarrierAccountSecretResponse>())!.Account.Id;
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        var book = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/international/shipments/{shipment.Shipment.Id}/book")
        {
            Content = JsonContent.Create(new { carrierAccountId = accountId, serviceLevel = (string?)null, carrierAwb = $"MAN-{Guid.NewGuid():N}"[..20] })
        };
        book.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, (await org.Client.SendAsync(book)).StatusCode);
        // The handoff route arrives in Task 14; position the shipment directly here.
        await SetShipmentStateAsync(shipment.Shipment.Id, ShipmentStatus.HandedToCarrier, null);
        return (org, shipment.Shipment.Id, accountId);
    }

    [Fact]
    public async Task ManualEvent_IsAcceptedAndProcessedIntoTheShipmentStatus()
    {
        var (org, shipmentId, accountId) = await BookedAndHandedOverAsync("manual-evt");

        var response = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{shipmentId}/carrier-events",
            new { statusCode = "IN_TRANSIT", occurredAt = DateTimeOffset.UtcNow.AddMinutes(-10), location = "Delhi", description = "Departed" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Pending", (await response.Content.ReadFromJsonAsync<CarrierTrackingEventResponse>())!.ProcessingStatus);

        await ProcessCarrierEventsAsync(accountId);

        Assert.Equal(ShipmentStatus.InInternationalTransit, await ShipmentStatusAsync(shipmentId));
    }

    [Fact]
    public async Task ManualEvent_UnknownCode_Returns400()
    {
        var (org, shipmentId, _) = await BookedAndHandedOverAsync("manual-bad");

        var response = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{shipmentId}/carrier-events",
            new { statusCode = "TELEPORTED", occurredAt = DateTimeOffset.UtcNow, location = (string?)null, description = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INTERNATIONAL_SHIPMENT_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ManualEvent_OtherTenant_Returns404_AndWithoutPermission_Returns403()
    {
        var (org, shipmentId, _) = await BookedAndHandedOverAsync("manual-owner");
        var other = await SetUpOrgAsync("manual-other");
        var body = new { statusCode = "IN_TRANSIT", occurredAt = DateTimeOffset.UtcNow, location = (string?)null, description = (string?)null };

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"/api/v1/international/shipments/{shipmentId}/carrier-events", body)).StatusCode);
        var hubStaffLike = await SeedUserClientAsync(org.Auth, new[] { "international.read", "international.book" }, null);
        Assert.Equal(HttpStatusCode.Forbidden, (await hubStaffLike.PostAsJsonAsync($"/api/v1/international/shipments/{shipmentId}/carrier-events", body)).StatusCode);
    }

    [Fact]
    public async Task Webhook_UnknownPublicId_Returns404_WithoutAuthentication()
    {
        var anonymous = Factory.CreateClient();

        var response = await anonymous.PostAsync($"/api/v1/carrier-webhooks/{Guid.NewGuid():N}", new StringContent("[]"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CarrierEventEndpointsTests"` — Expected: PASS (4 tests).
Run: `dotnet build Courier.slnx` — Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/International/ src/Api/Program.cs tests/International.Tests/ tests/Api.Tests/International/
git commit -m "feat(international): add signed carrier webhooks and manual carrier events

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 14: Handoff, receive and override routes; booking and timeline in the read model

**Files:**
- Create: `src/Modules/International/Application/InternationalCarrierLifecycleService.cs`
- Modify: `src/Modules/International/Application/CarrierDtos.cs` (`StatusOverrideRequest`)
- Modify: `src/Modules/International/Application/InternationalDtos.cs:77` (`InternationalShipmentDetailResponse`)
- Modify: `src/Modules/International/Application/InternationalShipmentQueryService.cs`
- Modify: `src/Modules/International/Application/CarrierBookingService.cs` (+ `GetLabelAsync`)
- Modify: `src/Modules/International/Presentation/InternationalEndpoints.cs` (4 routes)
- Modify: `src/Api/Program.cs` (DI)
- Modify: `tests/International.Tests/InternationalShipmentServiceTests.cs` (`RecordingGateway` handoff/receive/override; `TestDbContext` carrier configurations)
- Test: `tests/International.Tests/InternationalCarrierLifecycleServiceTests.cs`; `tests/Api.Tests/International/CarrierLifecycleEndpointsTests.cs`

**Interfaces:**
- Consumes: Task 5 `HandToCarrierAsync`, `ReceiveFromCarrierAsync`; Task 4 `OverrideInternationalStatusAsync`; Task 12 `CarrierBookingService.ToResponse`; Task 13 `CarrierTrackingEventResponse.From`.
- Produces:
  - `record StatusOverrideRequest(ShipmentStatus TargetStatus, string Reason)`
  - `InternationalShipmentDetailResponse(ShipmentResponse Shipment, InternationalLegResponse Leg, CarrierBookingResponse? Booking = null, IReadOnlyList<CarrierBookingResponse>? BookingHistory = null, IReadOnlyList<CarrierTrackingEventResponse>? CarrierEvents = null)`
  - `InternationalCarrierLifecycleService.HandoffAsync / ReceiveAsync / OverrideStatusAsync(Guid shipmentId, ..., ClaimsPrincipal actingUser, CancellationToken ct) : Task<ShipmentResponse?>`
  - `CarrierBookingService.GetLabelAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken ct) : Task<(Stream Content, string ContentType)?>`
  - Routes: `POST .../{id}/handoff` (`international.handoff`), `POST .../{id}/receive` (`international.receive`), `POST .../{id}/status-override` (`international.tracking.manage`), `GET .../{id}/booking/label` (`international.read`).

- [ ] **Step 1: Update the International.Tests fakes**

In `tests/International.Tests/InternationalShipmentServiceTests.cs`:

Add to `TestDbContext.OnModelCreating` (the I3 query service now also reads carrier tables):

```csharp
            modelBuilder.ApplyConfiguration(new CarrierAccountConfiguration());
            modelBuilder.ApplyConfiguration(new CarrierBookingConfiguration());
            modelBuilder.ApplyConfiguration(new CarrierTrackingEventConfiguration());
```

Replace `RecordingGateway`'s `HandToCarrierAsync`, `ReceiveFromCarrierAsync` and `OverrideInternationalStatusAsync` stubs with:

```csharp
    public List<(Guid ShipmentId, CarrierHandoffReference Reference)> Handoffs { get; } = new();
    public List<Guid> Receives { get; } = new();
    public List<(Guid ShipmentId, ShipmentStatus Target, string Reason)> Overrides { get; } = new();
    public Exception? LifecycleException { get; set; }

    public Task<ShipmentResponse?> HandToCarrierAsync(Guid shipmentId, CarrierHandoffReference reference, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        Handoffs.Add((shipmentId, reference));
        return LifecycleResult(shipmentId, ShipmentStatus.HandedToCarrier);
    }

    public Task<ShipmentResponse?> ReceiveFromCarrierAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        Receives.Add(shipmentId);
        return LifecycleResult(shipmentId, ShipmentStatus.ReceivedFromCarrier);
    }

    public Task<ShipmentResponse?> OverrideInternationalStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, string reason, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        Overrides.Add((shipmentId, targetStatus, reason));
        return LifecycleResult(shipmentId, targetStatus);
    }

    private Task<ShipmentResponse?> LifecycleResult(Guid shipmentId, ShipmentStatus status)
    {
        if (LifecycleException is not null)
        {
            throw LifecycleException;
        }
        return Task.FromResult<ShipmentResponse?>(new ShipmentResponse(
            shipmentId, "AWB-INTL-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, null, status, Guid.NewGuid(),
            1m, null, null, null, 5000m, 0m, false, 5000m, 0m, 0m, 5000m,
            "R", "1", "L", "S", "2", "L", new List<ShipmentItemResponse>(), DateTimeOffset.UtcNow));
    }
```

- [ ] **Step 2: Write the failing unit tests**

`tests/International.Tests/InternationalCarrierLifecycleServiceTests.cs`:

```csharp
using System.Security.Claims;
using Courier.International.Application;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Tests;

public class InternationalCarrierLifecycleServiceTests
{
    private sealed class Harness
    {
        public required CarrierTestDbContext DbContext { get; init; }
        public required Guid OrganizationId { get; init; }
        public required Guid ShipmentId { get; init; }
        public required InternationalLeg Leg { get; init; }
        public required RecordingGateway Gateway { get; init; }
        public required RecordingAuditLogger Audit { get; init; }
        public required InternationalCarrierLifecycleService Service { get; init; }
    }

    private static async Task<Harness> CreateHarnessAsync(IReadOnlySet<Guid>? accessibleBranchIds = null)
    {
        var dbContext = CarrierTestData.NewContext();
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var leg = await CarrierTestData.SeedLegAsync(dbContext, organizationId, shipmentId);
        var gateway = new RecordingGateway();
        var audit = new RecordingAuditLogger();
        var service = new InternationalCarrierLifecycleService(dbContext, new FixedTenantProvider(organizationId), audit, gateway,
            new FixedShipmentTransitionService(), new FixedBranchAuthorizationChecker(accessibleBranchIds));
        return new Harness { DbContext = dbContext, OrganizationId = organizationId, ShipmentId = shipmentId, Leg = leg, Gateway = gateway, Audit = audit, Service = service };
    }

    private static ClaimsPrincipal ActingUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    private static async Task<CarrierBooking> BookAsync(Harness h, CarrierBookingDirection direction = CarrierBookingDirection.Outbound)
    {
        var account = await CarrierTestData.SeedAccountAsync(h.DbContext, h.OrganizationId, "MANUAL");
        return await CarrierTestData.SeedBookingAsync(h.DbContext, h.OrganizationId, h.Leg.Id, account.Id, CarrierBookingStatus.Booked, "MAN-9", direction);
    }

    [Fact]
    public async Task HandoffAsync_WithBookedBooking_CallsGatewayAndStartsCarrierCustody()
    {
        var h = await CreateHarnessAsync();
        var booking = await BookAsync(h);

        var result = await h.Service.HandoffAsync(h.ShipmentId, ActingUser(), CancellationToken.None);

        Assert.Equal(ShipmentStatus.HandedToCarrier, result!.CurrentStatus);
        var call = Assert.Single(h.Gateway.Handoffs);
        Assert.Equal(new CarrierHandoffReference(booking.CarrierAccountId, "MAN-9"), call.Reference);
        h.DbContext.ChangeTracker.Clear();
        Assert.NotNull((await h.DbContext.Set<CarrierBooking>().SingleAsync()).CustodyStartedAt);
        Assert.Contains(h.Audit.Entries, e => e.Action == "carrier_booking.custody_started");
    }

    [Fact]
    public async Task HandoffAsync_WithoutBooking_IsRejectedBeforeTheGateway()
    {
        var h = await CreateHarnessAsync();

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() => h.Service.HandoffAsync(h.ShipmentId, ActingUser(), CancellationToken.None));
        Assert.Empty(h.Gateway.Handoffs);
    }

    [Fact]
    public async Task HandoffAsync_GatewayRejects_CustodyIsNotStarted()
    {
        var h = await CreateHarnessAsync();
        await BookAsync(h);
        h.Gateway.LifecycleException = new ShipmentInvalidTransitionException(h.ShipmentId, ShipmentStatus.Sorted, ShipmentStatus.HandedToCarrier);

        await Assert.ThrowsAsync<ShipmentInvalidTransitionException>(() => h.Service.HandoffAsync(h.ShipmentId, ActingUser(), CancellationToken.None));
        h.DbContext.ChangeTracker.Clear();
        Assert.Null((await h.DbContext.Set<CarrierBooking>().SingleAsync()).CustodyStartedAt);
    }

    [Fact]
    public async Task ReceiveAsync_RequiresALinkedInboundBooking()
    {
        var h = await CreateHarnessAsync();

        await Assert.ThrowsAsync<InternationalShipmentValidationException>(() => h.Service.ReceiveAsync(h.ShipmentId, ActingUser(), CancellationToken.None));

        await BookAsync(h, CarrierBookingDirection.Inbound);
        var result = await h.Service.ReceiveAsync(h.ShipmentId, ActingUser(), CancellationToken.None);
        Assert.Equal(ShipmentStatus.ReceivedFromCarrier, result!.CurrentStatus);
    }

    [Fact]
    public async Task OverrideStatusAsync_PassesTargetAndReasonToTheGateway()
    {
        var h = await CreateHarnessAsync();

        await h.Service.OverrideStatusAsync(h.ShipmentId, new StatusOverrideRequest(ShipmentStatus.InInternationalTransit, "Released"), ActingUser(), CancellationToken.None);

        Assert.Equal((h.ShipmentId, ShipmentStatus.InInternationalTransit, "Released"), Assert.Single(h.Gateway.Overrides));
    }

    [Fact]
    public async Task NotVisible_ReturnsNullWithoutCallingTheGateway()
    {
        var h = await CreateHarnessAsync(new HashSet<Guid> { Guid.NewGuid() });
        await BookAsync(h);

        Assert.Null(await h.Service.HandoffAsync(h.ShipmentId, ActingUser(), CancellationToken.None));
        Assert.Null(await h.Service.OverrideStatusAsync(h.ShipmentId, new StatusOverrideRequest(ShipmentStatus.InInternationalTransit, "x"), ActingUser(), CancellationToken.None));
        Assert.Empty(h.Gateway.Handoffs);
        Assert.Empty(h.Gateway.Overrides);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/International.Tests --filter "FullyQualifiedName~InternationalCarrierLifecycleServiceTests"` — Expected: FAIL (types missing).

- [ ] **Step 4: Implement the lifecycle service**

Append to `CarrierDtos.cs`:

```csharp
public record StatusOverrideRequest(Courier.Shipments.Domain.ShipmentStatus TargetStatus, string Reason);
```

`src/Modules/International/Application/InternationalCarrierLifecycleService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.International.Domain;
using Courier.Shipments.Application;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Application;

// Handoff / receive scans and staff overrides. Visibility (404) is checked here with the same
// origin/destination/current rule as every international route; the gateway then enforces the
// stricter "operator is at the right branch" rule (400) and the state machine.
public class InternationalCarrierLifecycleService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IShipmentInternationalGateway _gateway;
    private readonly IShipmentTransitionService _shipmentTransitionService;
    private readonly IBranchAuthorizationChecker _branchAuthorizationChecker;

    public InternationalCarrierLifecycleService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger,
        IShipmentInternationalGateway gateway, IShipmentTransitionService shipmentTransitionService, IBranchAuthorizationChecker branchAuthorizationChecker)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _gateway = gateway;
        _shipmentTransitionService = shipmentTransitionService;
        _branchAuthorizationChecker = branchAuthorizationChecker;
    }

    public async Task<ShipmentResponse?> HandoffAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganization();
        var leg = await LoadVisibleLegAsync(shipmentId, actingUser, "international.handoff", cancellationToken);
        if (leg is null)
        {
            return null;
        }
        var booking = await LiveBookedAsync(leg.Id, CarrierBookingDirection.Outbound, cancellationToken)
            ?? throw new InternationalShipmentValidationException("A booked outbound carrier booking is required before handoff.");

        // The shipment transition and the booking's custody start commit together.
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational())
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }
        try
        {
            var result = await _gateway.HandToCarrierAsync(shipmentId, new CarrierHandoffReference(booking.CarrierAccountId, booking.CarrierAwb!), actingUser, cancellationToken);
            if (result is null)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                return null;
            }
            booking.CustodyStartedAt = DateTimeOffset.UtcNow;
            booking.UpdatedAt = booking.CustodyStartedAt.Value;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUser.GetUserId(), "carrier_booking.custody_started", "CarrierBooking", booking.Id.ToString()), cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return result;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<ShipmentResponse?> ReceiveAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        RequireOrganization();
        var leg = await LoadVisibleLegAsync(shipmentId, actingUser, "international.receive", cancellationToken);
        if (leg is null)
        {
            return null;
        }
        _ = await LiveBookedAsync(leg.Id, CarrierBookingDirection.Inbound, cancellationToken)
            ?? throw new InternationalShipmentValidationException("Link the inbound carrier booking before receiving the parcel.");
        return await _gateway.ReceiveFromCarrierAsync(shipmentId, actingUser, cancellationToken);
    }

    public async Task<ShipmentResponse?> OverrideStatusAsync(Guid shipmentId, StatusOverrideRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        RequireOrganization();
        var leg = await LoadVisibleLegAsync(shipmentId, actingUser, "international.tracking.manage", cancellationToken);
        if (leg is null)
        {
            return null;
        }
        return await _gateway.OverrideInternationalStatusAsync(shipmentId, request.TargetStatus, request.Reason ?? string.Empty, actingUser, cancellationToken);
    }

    private Guid RequireOrganization() =>
        _currentTenantProvider.OrganizationId ?? throw new InternationalShipmentValidationException("No active organization context.");

    private async Task<InternationalLeg?> LoadVisibleLegAsync(Guid shipmentId, ClaimsPrincipal actingUser, string permission, CancellationToken cancellationToken)
    {
        var leg = await _dbContext.Set<InternationalLeg>().FirstOrDefaultAsync(l => l.ShipmentId == shipmentId, cancellationToken);
        if (leg is null)
        {
            return null;
        }
        var scan = await _shipmentTransitionService.GetScanInfoAsync(shipmentId, cancellationToken);
        if (scan is null || !await InternationalShipmentAccess.IsVisibleAsync(_branchAuthorizationChecker, actingUser, permission,
                scan.OriginBranchId, scan.DestinationBranchId, scan.CurrentBranchId, cancellationToken))
        {
            return null;
        }
        return leg;
    }

    private Task<CarrierBooking?> LiveBookedAsync(Guid legId, CarrierBookingDirection direction, CancellationToken cancellationToken) =>
        _dbContext.Set<CarrierBooking>().FirstOrDefaultAsync(b => b.InternationalLegId == legId && b.Status == CarrierBookingStatus.Booked && b.Direction == direction, cancellationToken);
}
```

Run: `dotnet test tests/International.Tests --filter "FullyQualifiedName~InternationalCarrierLifecycleServiceTests"` — Expected: PASS.

- [ ] **Step 5: Extend the read model and add the label download**

In `InternationalDtos.cs`, replace the `InternationalShipmentDetailResponse` record with:

```csharp
// I4 adds the carrier booking (live one + history) and the carrier event timeline as trailing
// optionals so every existing (shipment, leg) construction keeps compiling.
public record InternationalShipmentDetailResponse(
    ShipmentResponse Shipment,
    InternationalLegResponse Leg,
    CarrierBookingResponse? Booking = null,
    IReadOnlyList<CarrierBookingResponse>? BookingHistory = null,
    IReadOnlyList<CarrierTrackingEventResponse>? CarrierEvents = null);
```

In `InternationalShipmentQueryService.GetAsync`, replace the final `return new InternationalShipmentDetailResponse(shipment, await InternationalShipmentAccess.ToLegResponseAsync(_dbContext, leg, cancellationToken));` with:

```csharp
        var legResponse = await InternationalShipmentAccess.ToLegResponseAsync(_dbContext, leg, cancellationToken);
        var bookings = await _dbContext.Set<CarrierBooking>().Where(b => b.InternationalLegId == leg.Id)
            .OrderByDescending(b => b.CreatedAt).ToListAsync(cancellationToken);
        if (bookings.Count == 0)
        {
            return new InternationalShipmentDetailResponse(shipment, legResponse, null, Array.Empty<CarrierBookingResponse>(), Array.Empty<CarrierTrackingEventResponse>());
        }

        var accountIds = bookings.Select(b => b.CarrierAccountId).Distinct().ToList();
        var accounts = await _dbContext.Set<CarrierAccount>().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);
        var bookingResponses = bookings.Select(b => CarrierBookingService.ToResponse(b, accounts[b.CarrierAccountId])).ToList();
        var live = bookings.FirstOrDefault(b => b.Status is CarrierBookingStatus.Pending or CarrierBookingStatus.Booked);

        // Every event reported for any of this shipment's (account, carrier AWB) pairs — including
        // ones still Pending/Unmatched, so staff see carrier activity before it is applied.
        var pairs = bookings.Where(b => b.CarrierAwb is not null).Select(b => (b.CarrierAccountId, b.CarrierAwb!)).ToHashSet();
        var awbs = pairs.Select(p => p.Item2).Distinct().ToList();
        var events = (await _dbContext.Set<CarrierTrackingEvent>()
                .Where(e => accountIds.Contains(e.CarrierAccountId) && awbs.Contains(e.CarrierAwb))
                .OrderBy(e => e.OccurredAt)
                .ToListAsync(cancellationToken))
            .Where(e => pairs.Contains((e.CarrierAccountId, e.CarrierAwb)))
            .Select(CarrierTrackingEventResponse.From)
            .ToList();

        return new InternationalShipmentDetailResponse(shipment, legResponse,
            live is null ? null : CarrierBookingService.ToResponse(live, accounts[live.CarrierAccountId]), bookingResponses, events);
```

Add to `CarrierBookingService`:

```csharp
    // The live booking's stored label; null when not visible, no live booking, or no label.
    public async Task<(Stream Content, string ContentType)?> GetLabelAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var target = await LoadVisibleAsync(shipmentId, actingUser, "international.read", cancellationToken);
        if (target is null)
        {
            return null;
        }
        var booking = await _dbContext.Set<CarrierBooking>().FirstOrDefaultAsync(b => b.InternationalLegId == target.Value.Leg.Id
            && b.Status == CarrierBookingStatus.Booked && b.LabelStorageKey != null, cancellationToken);
        if (booking is null)
        {
            return null;
        }
        return (await _objectStorage.OpenReadAsync(booking.LabelStorageKey!, cancellationToken), booking.LabelContentType ?? "application/octet-stream");
    }
```

- [ ] **Step 6: Routes and DI**

In `InternationalEndpoints.cs`, add after the `carrier-events` route:

```csharp
        group.MapPost("/{id:guid}/handoff", (Guid id, InternationalCarrierLifecycleService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.HandoffAsync(id, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.handoff");

        group.MapPost("/{id:guid}/receive", (Guid id, InternationalCarrierLifecycleService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.ReceiveAsync(id, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.receive");

        group.MapPost("/{id:guid}/status-override", (Guid id, StatusOverrideRequest request, InternationalCarrierLifecycleService service, HttpContext httpContext) =>
            InternationalEndpointErrors.ExecuteAsync(async () =>
            {
                var result = await service.OverrideStatusAsync(id, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            })).RequirePermission("international.tracking.manage");

        group.MapGet("/{id:guid}/booking/label", async (Guid id, CarrierBookingService service, HttpContext httpContext) =>
        {
            var label = await service.GetLabelAsync(id, httpContext.User, httpContext.RequestAborted);
            return label is null ? Results.NotFound() : Results.File(label.Value.Content, label.Value.ContentType);
        }).RequirePermission("international.read");
```

In `Program.cs`, register `InternationalCarrierLifecycleService` (scoped).

- [ ] **Step 7: Write the API tests (manual carrier, real stack)**

`tests/Api.Tests/International/CarrierLifecycleEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Courier.International.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierLifecycleEndpointsTests : InternationalApiTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public CarrierLifecycleEndpointsTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private sealed record ShipmentResponseForTest(Guid Id, string CurrentStatus, Guid? CurrentBranchId);

    private static async Task<Guid> ManualAccountAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/international/carrier-accounts",
            new { carrierCode = "MANUAL", displayName = "Manual", credentials = (object?)null, pollingEnabled = false, generateWebhookSecret = false });
        return (await response.Content.ReadFromJsonAsync<CarrierAccountSecretResponse>())!.Account.Id;
    }

    private static async Task BookManualAsync(HttpClient client, Guid shipmentId, Guid accountId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/international/shipments/{shipmentId}/book")
        {
            Content = JsonContent.Create(new { carrierAccountId = accountId, serviceLevel = (string?)null, carrierAwb = $"MAN-{Guid.NewGuid():N}"[..20] })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    private static Task<HttpResponseMessage> ManualEventAsync(HttpClient client, Guid shipmentId, string code) =>
        client.PostAsJsonAsync($"/api/v1/international/shipments/{shipmentId}/carrier-events",
            new { statusCode = code, occurredAt = DateTimeOffset.UtcNow.AddSeconds(-5), location = (string?)null, description = (string?)null });

    [Fact]
    public async Task Outbound_Book_Handoff_Hold_Override_Deliver()
    {
        var org = await SetUpOrgAsync("life-out");
        var accountId = await ManualAccountAsync(org.Client);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        var id = shipment.Shipment.Id;
        await BookManualAsync(org.Client, id, accountId);

        var handoff = await org.Client.PostAsync($"/api/v1/international/shipments/{id}/handoff", null);
        Assert.Equal(HttpStatusCode.OK, handoff.StatusCode);
        var handed = (await handoff.Content.ReadFromJsonAsync<ShipmentResponseForTest>())!;
        Assert.Equal("HandedToCarrier", handed.CurrentStatus);
        Assert.Null(handed.CurrentBranchId);

        Assert.Equal(HttpStatusCode.Accepted, (await ManualEventAsync(org.Client, id, "IN_TRANSIT")).StatusCode);
        await ProcessCarrierEventsAsync(accountId);
        Assert.Equal(HttpStatusCode.Accepted, (await ManualEventAsync(org.Client, id, "CUSTOMS_HOLD")).StatusCode);
        await ProcessCarrierEventsAsync(accountId);
        Assert.Equal(ShipmentStatus.CustomsHold, await ShipmentStatusAsync(id));

        var noReason = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{id}/status-override", new { targetStatus = "InInternationalTransit", reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var badTarget = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{id}/status-override", new { targetStatus = "Delivered", reason = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, badTarget.StatusCode);
        var released = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{id}/status-override", new { targetStatus = "InInternationalTransit", reason = "Customs released after inspection" });
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);

        Assert.Equal(HttpStatusCode.Accepted, (await ManualEventAsync(org.Client, id, "DELIVERED")).StatusCode);
        await ProcessCarrierEventsAsync(accountId);
        Assert.Equal(ShipmentStatus.DeliveredAbroad, await ShipmentStatusAsync(id));

        var detail = await org.Client.GetFromJsonAsync<InternationalShipmentDetailResponse>($"/api/v1/international/shipments/{id}");
        Assert.NotNull(detail!.Booking!.CustodyStartedAt);
        Assert.Single(detail.BookingHistory!);
        Assert.Equal(3, detail.CarrierEvents!.Count(e => e.ProcessingStatus == "Applied"));
    }

    [Fact]
    public async Task Handoff_WithoutBooking_Returns400()
    {
        var org = await SetUpOrgAsync("life-nobook");
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);

        var response = await org.Client.PostAsync($"/api/v1/international/shipments/{shipment.Shipment.Id}/handoff", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Handoff_ByUserNotAtTheCurrentBranch_Returns400()
    {
        var org = await SetUpOrgAsync("life-branch");
        var accountId = await ManualAccountAsync(org.Client);
        var gatewayBranch = await CreateBranchAsync(org.Client, $"GW-{Guid.NewGuid():N}"[..10]);
        var shipment = await CreateOutboundAsync(org, org.BranchId, gatewayBranch);
        await SetShipmentStateAsync(shipment.Shipment.Id, ShipmentStatus.AtDestinationBranch, gatewayBranch);
        Assert.Equal(HttpStatusCode.OK, (await org.Client.PatchAsJsonAsync($"/api/v1/international/shipments/{shipment.Shipment.Id}/customs",
            CustomsBody(shipment.Shipment.Items.Single().Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await org.Client.PostAsync($"/api/v1/international/shipments/{shipment.Shipment.Id}/prepare-customs", null)).StatusCode);
        await BookManualAsync(org.Client, shipment.Shipment.Id, accountId);
        var originOnly = await SeedUserClientAsync(org.Auth, new[] { "international.handoff" }, org.BranchId);

        var response = await originOnly.PostAsync($"/api/v1/international/shipments/{shipment.Shipment.Id}/handoff", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SHIPMENT_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Inbound_Link_Transit_Customs_Receive_ArrivesAtTheSameBranch()
    {
        var org = await SetUpOrgAsync("life-in");
        var accountId = await ManualAccountAsync(org.Client);
        var inbound = await CreateInboundAsync(org);
        var id = inbound.Shipment.Id;
        Assert.Equal(HttpStatusCode.OK, (await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{id}/inbound-booking",
            new { carrierAccountId = accountId, carrierAwb = $"IN-{Guid.NewGuid():N}"[..20], serviceLevel = (string?)null })).StatusCode);

        Assert.Equal(HttpStatusCode.Accepted, (await ManualEventAsync(org.Client, id, "IN_TRANSIT")).StatusCode);
        await ProcessCarrierEventsAsync(accountId);
        Assert.Equal(HttpStatusCode.Accepted, (await ManualEventAsync(org.Client, id, "IN_CUSTOMS")).StatusCode);
        await ProcessCarrierEventsAsync(accountId);
        Assert.Equal(ShipmentStatus.CustomsClearance, await ShipmentStatusAsync(id));

        var receive = await org.Client.PostAsync($"/api/v1/international/shipments/{id}/receive", null);
        Assert.Equal(HttpStatusCode.OK, receive.StatusCode);
        Assert.Equal("AtDestinationBranch", (await receive.Content.ReadFromJsonAsync<ShipmentResponseForTest>())!.CurrentStatus);
    }

    [Fact]
    public async Task LifecycleRoutes_OtherTenant_Return404_AndMissingPermission_Returns403()
    {
        var owner = await SetUpOrgAsync("life-owner");
        var other = await SetUpOrgAsync("life-other");
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(owner);
        var id = shipment.Shipment.Id;

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync($"/api/v1/international/shipments/{id}/handoff", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync($"/api/v1/international/shipments/{id}/receive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"/api/v1/international/shipments/{id}/status-override",
            new { targetStatus = "InInternationalTransit", reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/v1/international/shipments/{id}/booking/label")).StatusCode);

        var readOnly = await SeedUserClientAsync(owner.Auth, new[] { "international.read" }, null);
        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostAsync($"/api/v1/international/shipments/{id}/handoff", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostAsJsonAsync($"/api/v1/international/shipments/{id}/status-override",
            new { targetStatus = "InInternationalTransit", reason = "x" })).StatusCode);
    }
}
```

- [ ] **Step 8: Run tests**

Run: `dotnet test tests/International.Tests` — Expected: PASS (including the I3 query tests, now with carrier configurations).
Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~International"` — Expected: PASS.
Run: `dotnet build Courier.slnx` — Expected: 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/International/ src/Api/Program.cs tests/International.Tests/ tests/Api.Tests/International/
git commit -m "feat(international): add handoff, receive and override routes with booking timeline

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 15: Fake HTTP carrier and end-to-end carrier tests

A test-only carrier that talks real HTTP (through the production resilience pipeline) to an in-process stub, so webhook signatures, polling, booking over HTTP, carrier-side idempotency, labels and the circuit breaker are exercised end to end.

**Files:**
- Create: `tests/Api.Tests/International/FakeHttpCarrier.cs`
- Test: `tests/Api.Tests/International/CarrierHttpIntegrationTests.cs`

**Interfaces:**
- Consumes: Task 9 `AddCarrierHttpClient`; every I4 route.
- Produces (test-only): `FakeCarrierBackend`, `FakeCarrierHttpHandler`, `FakeHttpCarrierProvider` (`Code = "FAKE_HTTP"`, `SignatureHeader = "X-Fake-Signature"`, `static string Sign(byte[] body, string secret)`), `FakeCarrierAppFixture`.

- [ ] **Step 1: Create the fake carrier**

`tests/Api.Tests/International/FakeHttpCarrier.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Courier.International.Application.Carriers;
using Courier.International.Infrastructure.Carriers;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Courier.Api.Tests.International;

// Carrier-side state shared by the stub handler and the tests.
public sealed class FakeCarrierBackend
{
    private int _bookCalls;
    private int _nextAwb;

    public int BookCalls => _bookCalls;
    public HttpStatusCode? FailBookingWith { get; set; }
    public ConcurrentDictionary<string, string> AwbByIdempotencyKey { get; } = new();
    public ConcurrentDictionary<string, List<FakeTrackingEvent>> TrackingByAwb { get; } = new();
    public ConcurrentBag<string> Cancelled { get; } = new();

    public int RecordBookCall() => Interlocked.Increment(ref _bookCalls);
    public string NewAwb() => $"FAKE{Interlocked.Increment(ref _nextAwb):D6}{Random.Shared.Next(1000, 9999)}";
}

public sealed record FakeTrackingEvent(string CarrierAwb, string EventId, string StatusCode, string? Description, string? Location, DateTimeOffset OccurredAt);

// In-process stand-in for the carrier's HTTP API.
public sealed class FakeCarrierHttpHandler : HttpMessageHandler
{
    private readonly FakeCarrierBackend _backend;

    public FakeCarrierHttpHandler(FakeCarrierBackend backend) => _backend = backend;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.Trim('/');
        if (request.Method == HttpMethod.Post && path == "book")
        {
            _backend.RecordBookCall();
            if (_backend.FailBookingWith is HttpStatusCode failure)
            {
                return Task.FromResult(new HttpResponseMessage(failure) { Content = new StringContent("{\"error\":\"carrier says no\"}") });
            }
            var key = request.Headers.GetValues("Idempotency-Key").Single();
            var awb = _backend.AwbByIdempotencyKey.GetOrAdd(key, _ => _backend.NewAwb());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { carrierAwb = awb, serviceLevel = "EXPRESS", labelBase64 = Convert.ToBase64String("%PDF-fake-label"u8.ToArray()) })
            });
        }
        if (request.Method == HttpMethod.Post && path.StartsWith("cancel/"))
        {
            _backend.Cancelled.Add(path["cancel/".Length..]);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
        if (request.Method == HttpMethod.Get && path.StartsWith("tracking/"))
        {
            var events = _backend.TrackingByAwb.GetValueOrDefault(path["tracking/".Length..]) ?? new List<FakeTrackingEvent>();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(events) });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

public sealed class FakeHttpCarrierProvider : ICarrierProvider
{
    public const string Code = "FAKE_HTTP";
    public const string SignatureHeader = "X-Fake-Signature";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public FakeHttpCarrierProvider(HttpClient http)
    {
        _http = http;
        _http.BaseAddress ??= new Uri("http://fake-carrier.test/");
    }

    public string CarrierCode => Code;
    public CarrierCapabilities Capabilities { get; } = new(SupportsWebhooks: true, SupportsPolling: true, RequiresManualAwb: false, SupportsLabels: true);

    public static string Sign(byte[] body, string secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    public async Task<CarrierBookingResult> BookAsync(CarrierBookingRequest request, CarrierCredentials credentials, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "book") { Content = JsonContent.Create(new { request.PlatformAwb, request.ServiceLevel, request.WeightKg }) };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await _http.SendAsync(message, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new CarrierBookingException($"Fake carrier returned {(int)response.StatusCode}.");
        }
        var body = JsonSerializer.Deserialize<BookBody>(raw, Json)!;
        return new CarrierBookingResult(body.CarrierAwb, body.ServiceLevel,
            body.LabelBase64 is null ? null : new CarrierLabel(Convert.FromBase64String(body.LabelBase64), "application/pdf"), raw);
    }

    public async Task CancelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync($"cancel/{carrierAwb}", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new CarrierBookingException($"Fake carrier returned {(int)response.StatusCode}.");
        }
    }

    public Task<CarrierLabel?> GetLabelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken) => Task.FromResult<CarrierLabel?>(null);

    public async Task<IReadOnlyList<CarrierTrackingUpdate>> GetTrackingAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken cancellationToken)
    {
        var events = await _http.GetFromJsonAsync<List<FakeTrackingEvent>>($"tracking/{carrierAwb}", Json, cancellationToken) ?? new();
        return events.Select(ToUpdate).ToList();
    }

    public bool VerifyWebhook(IReadOnlyDictionary<string, string> headers, byte[] rawBody, string webhookSecret)
    {
        if (!headers.TryGetValue(SignatureHeader, out var provided))
        {
            return false;
        }
        var expected = Encoding.ASCII.GetBytes(Sign(rawBody, webhookSecret));
        var actual = Encoding.ASCII.GetBytes(provided.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public IReadOnlyList<CarrierTrackingUpdate> ParseWebhook(byte[] rawBody)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<FakeTrackingEvent>>(rawBody, Json) ?? new()).Select(ToUpdate).ToList();
        }
        catch (JsonException ex)
        {
            throw new CarrierWebhookPayloadException(ex.Message);
        }
    }

    public bool IsKnownStatusCode(string carrierStatusCode) => carrierStatusCode.ToUpperInvariant() is "TRANSIT" or "HOLD" or "CLEARANCE" or "DELIVERED";

    public ShipmentStatus? MapStatus(string carrierStatusCode, ShipmentDirection direction) => (carrierStatusCode.ToUpperInvariant(), direction) switch
    {
        ("TRANSIT", _) => ShipmentStatus.InInternationalTransit,
        ("HOLD", _) => ShipmentStatus.CustomsHold,
        ("CLEARANCE", ShipmentDirection.Inbound) => ShipmentStatus.CustomsClearance,
        ("DELIVERED", ShipmentDirection.Outbound) => ShipmentStatus.DeliveredAbroad,
        _ => null
    };

    private static CarrierTrackingUpdate ToUpdate(FakeTrackingEvent e) =>
        new(e.CarrierAwb, e.EventId, e.StatusCode, e.Description, e.Location, e.OccurredAt, JsonSerializer.Serialize(e, Json));

    private sealed record BookBody(string CarrierAwb, string? ServiceLevel, string? LabelBase64);
}

// One app instance per test class (booting per test would start a Hangfire server per test).
public sealed class FakeCarrierAppFixture : IDisposable
{
    public FakeCarrierAppFixture()
    {
        Backend = new FakeCarrierBackend();
        Factory = CreateFactory(Backend, o => o.RetryBaseDelay = TimeSpan.Zero);
    }

    public FakeCarrierBackend Backend { get; }
    public WebApplicationFactory<Program> Factory { get; }

    public static WebApplicationFactory<Program> CreateFactory(FakeCarrierBackend backend, Action<CarrierResilienceOptions> configure) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(backend);
            services.AddCarrierHttpClient<FakeHttpCarrierProvider, FakeHttpCarrierProvider>(configure)
                .ConfigurePrimaryHttpMessageHandler(sp => new FakeCarrierHttpHandler(sp.GetRequiredService<FakeCarrierBackend>()));
            services.AddScoped<ICarrierProvider>(sp => sp.GetRequiredService<FakeHttpCarrierProvider>());
        }));

    public void Dispose() => Factory.Dispose();
}
```

- [ ] **Step 2: Write the end-to-end tests**

`tests/Api.Tests/International/CarrierHttpIntegrationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.International.Application;
using Courier.International.Domain;
using Courier.Persistence;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests.International;

public class CarrierHttpIntegrationTests : InternationalApiTestBase, IClassFixture<FakeCarrierAppFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly FakeCarrierBackend _backend;

    public CarrierHttpIntegrationTests(FakeCarrierAppFixture fixture) : base(fixture.Factory)
    {
        _backend = fixture.Backend;
        _backend.FailBookingWith = null;
    }

    private static async Task<CarrierAccountSecretResponse> FakeAccountAsync(HttpClient client, bool polling)
    {
        var response = await client.PostAsJsonAsync("/api/v1/international/carrier-accounts", new
        {
            carrierCode = FakeHttpCarrierProvider.Code, displayName = "Fake HTTP",
            credentials = new Dictionary<string, string> { ["apiKey"] = "fake-api-key-123" },
            pollingEnabled = polling, generateWebhookSecret = true
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CarrierAccountSecretResponse>())!;
    }

    private static Task<HttpResponseMessage> BookAsync(HttpClient client, Guid shipmentId, Guid accountId, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/international/shipments/{shipmentId}/book")
        {
            Content = JsonContent.Create(new { carrierAccountId = accountId, serviceLevel = "EXPRESS", carrierAwb = (string?)null })
        };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PostWebhookAsync(string webhookPath, string secret, params FakeTrackingEvent[] events)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(events, Json);
        var request = new HttpRequestMessage(HttpMethod.Post, webhookPath) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add(FakeHttpCarrierProvider.SignatureHeader, FakeHttpCarrierProvider.Sign(body, secret));
        return Factory.CreateClient().SendAsync(request);
    }

    private async Task<int> EventCountAsync(Guid accountId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<CarrierTrackingEvent>().IgnoreQueryFilters().CountAsync(e => e.CarrierAccountId == accountId);
    }

    [Fact]
    public async Task Outbound_BookOverHttp_Label_Handoff_SignedWebhooks_DeliverTheShipment()
    {
        var org = await SetUpOrgAsync("http-out");
        var account = await FakeAccountAsync(org.Client, polling: false);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        var id = shipment.Shipment.Id;

        var book = await BookAsync(org.Client, id, account.Account.Id, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, book.StatusCode);
        var booking = (await book.Content.ReadFromJsonAsync<CarrierBookingResponse>())!;
        Assert.StartsWith("FAKE", booking.CarrierAwb);
        Assert.True(booking.HasLabel);

        var label = await org.Client.GetAsync($"/api/v1/international/shipments/{id}/booking/label");
        Assert.Equal(HttpStatusCode.OK, label.StatusCode);
        Assert.Equal("%PDF-fake-label", await label.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await org.Client.PostAsync($"/api/v1/international/shipments/{id}/handoff", null)).StatusCode);

        var now = DateTimeOffset.UtcNow;
        var events = new[]
        {
            new FakeTrackingEvent(booking.CarrierAwb!, $"e-{Guid.NewGuid():N}", "DELIVERED", "Delivered", "London", now.AddMinutes(-1)),
            new FakeTrackingEvent(booking.CarrierAwb!, $"e-{Guid.NewGuid():N}", "TRANSIT", "Departed", "Kathmandu", now.AddHours(-5))
        };
        Assert.Equal(HttpStatusCode.Accepted, (await PostWebhookAsync(account.Account.WebhookPath, account.WebhookSecret!, events)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await PostWebhookAsync(account.Account.WebhookPath, account.WebhookSecret!, events)).StatusCode);
        Assert.Equal(2, await EventCountAsync(account.Account.Id));

        await ProcessCarrierEventsAsync(account.Account.Id);

        Assert.Equal(ShipmentStatus.DeliveredAbroad, await ShipmentStatusAsync(id));
    }

    [Fact]
    public async Task Webhook_BadSignature_Returns401_AndMalformedBody_Returns400_StoringNothing()
    {
        var org = await SetUpOrgAsync("http-sig");
        var account = await FakeAccountAsync(org.Client, polling: false);
        var evt = new FakeTrackingEvent("FAKE-X", "e-1", "TRANSIT", null, null, DateTimeOffset.UtcNow);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostWebhookAsync(account.Account.WebhookPath, "not-the-secret", evt)).StatusCode);

        var malformed = "{broken"u8.ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, account.Account.WebhookPath) { Content = new ByteArrayContent(malformed) };
        request.Headers.Add(FakeHttpCarrierProvider.SignatureHeader, FakeHttpCarrierProvider.Sign(malformed, account.WebhookSecret!));
        var response = await Factory.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CARRIER_WEBHOOK_INVALID_PAYLOAD", await response.Content.ReadAsStringAsync());

        Assert.Equal(0, await EventCountAsync(account.Account.Id));
    }

    [Fact]
    public async Task Booking_CarrierRejects_Returns502_ShipmentStaysCustomsPrepared_ThenANewKeyBooks()
    {
        var org = await SetUpOrgAsync("http-reject");
        var account = await FakeAccountAsync(org.Client, polling: false);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        _backend.FailBookingWith = HttpStatusCode.UnprocessableEntity;

        var rejected = await BookAsync(org.Client, shipment.Shipment.Id, account.Account.Id, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.BadGateway, rejected.StatusCode);
        var rejectedBody = await rejected.Content.ReadAsStringAsync();
        Assert.Contains("CARRIER_BOOKING_FAILED", rejectedBody);
        Assert.DoesNotContain("fake-api-key-123", rejectedBody);
        Assert.Equal(ShipmentStatus.CustomsPrepared, await ShipmentStatusAsync(shipment.Shipment.Id));

        _backend.FailBookingWith = null;
        Assert.Equal(HttpStatusCode.OK, (await BookAsync(org.Client, shipment.Shipment.Id, account.Account.Id, Guid.NewGuid().ToString("N"))).StatusCode);
    }

    [Fact]
    public async Task Booking_SameKeyTwice_CallsTheCarrierOnce()
    {
        var org = await SetUpOrgAsync("http-idem");
        var account = await FakeAccountAsync(org.Client, polling: false);
        var shipment = await PrepareOutboundAtCustomsPreparedAsync(org);
        var key = Guid.NewGuid().ToString("N");
        var callsBefore = _backend.BookCalls;

        var first = await (await BookAsync(org.Client, shipment.Shipment.Id, account.Account.Id, key)).Content.ReadFromJsonAsync<CarrierBookingResponse>();
        var second = await (await BookAsync(org.Client, shipment.Shipment.Id, account.Account.Id, key)).Content.ReadFromJsonAsync<CarrierBookingResponse>();

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(callsBefore + 1, _backend.BookCalls);
    }

    [Fact]
    public async Task Inbound_LinkThenPoll_ReachesCustomsClearance_ThenReceive()
    {
        var org = await SetUpOrgAsync("http-in");
        var account = await FakeAccountAsync(org.Client, polling: true);
        var inbound = await CreateInboundAsync(org);
        var foreignAwb = $"DHLX{Guid.NewGuid():N}"[..16];
        _backend.TrackingByAwb[foreignAwb] = new List<FakeTrackingEvent>
        {
            new(foreignAwb, $"p-{Guid.NewGuid():N}", "TRANSIT", "Departed origin", "Frankfurt", DateTimeOffset.UtcNow.AddHours(-10)),
            new(foreignAwb, $"p-{Guid.NewGuid():N}", "CLEARANCE", "At customs", "Kathmandu", DateTimeOffset.UtcNow.AddHours(-1))
        };

        var link = await org.Client.PostAsJsonAsync($"/api/v1/international/shipments/{inbound.Shipment.Id}/inbound-booking",
            new { carrierAccountId = account.Account.Id, carrierAwb = foreignAwb, serviceLevel = (string?)null });
        var booking = (await link.Content.ReadFromJsonAsync<CarrierBookingResponse>())!;

        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<CarrierTrackingPollJob>().PollBookingAsync(org.Auth.OrganizationId, booking.Id);
        }
        await ProcessCarrierEventsAsync(account.Account.Id);
        Assert.Equal(ShipmentStatus.CustomsClearance, await ShipmentStatusAsync(inbound.Shipment.Id));

        var receive = await org.Client.PostAsync($"/api/v1/international/shipments/{inbound.Shipment.Id}/receive", null);
        Assert.Equal(HttpStatusCode.OK, receive.StatusCode);
        Assert.Equal(ShipmentStatus.AtDestinationBranch, await ShipmentStatusAsync(inbound.Shipment.Id));
    }

    [Fact]
    public async Task RepeatedCarrierFailures_OpenTheCircuit_AndBookingReturns503()
    {
        var backend = new FakeCarrierBackend { FailBookingWith = HttpStatusCode.ServiceUnavailable };
        using var factory = FakeCarrierAppFixture.CreateFactory(backend, o =>
        {
            o.RetryBaseDelay = TimeSpan.Zero;
            o.MaxRetryAttempts = 1;
            o.MinimumThroughput = 2;
            o.SamplingDuration = TimeSpan.FromSeconds(60);
            o.BreakDuration = TimeSpan.FromSeconds(60);
        });
        var harness = new CircuitHarness(factory);
        var org = await harness.SetUpAsync("http-circuit");
        var account = await FakeAccountAsync(org.Client, polling: false);
        var shipment = await harness.PrepareAsync(org);

        HttpStatusCode last = default;
        for (var attempt = 0; attempt < 5 && last != HttpStatusCode.ServiceUnavailable; attempt++)
        {
            last = (await BookAsync(org.Client, shipment.Shipment.Id, account.Account.Id, Guid.NewGuid().ToString("N"))).StatusCode;
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, last);
    }

    // Reuses the base-class helpers against the circuit test's own factory.
    private sealed class CircuitHarness : InternationalApiTestBase
    {
        public CircuitHarness(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) : base(factory) { }
        public Task<OrgContext> SetUpAsync(string label) => SetUpOrgAsync(label);
        public Task<InternationalShipmentDetailResponse> PrepareAsync(OrgContext org) => PrepareOutboundAtCustomsPreparedAsync(org);
    }
}
```

(`OrgContext` is `public` in the base class — Task 6 — so the nested harness can return it.)

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~CarrierHttpIntegrationTests"` — Expected: PASS (6 tests). Run it 3 times; all runs must pass.

- [ ] **Step 4: Run the whole International surface and the full suites**

Run: `dotnet test tests/International.Tests`, `dotnet test tests/Shipments.Tests`, `dotnet test tests/Manifests.Tests`, `dotnet test tests/Identity.Tests`, `dotnet test tests/SharedKernel.Tests` — Expected: PASS.
Run: `dotnet test tests/Api.Tests` — Expected: PASS. Rerun any failing test alone and record in the report whether it passes alone (with Task 1 in place, failures should be rare).

- [ ] **Step 5: Commit**

```bash
git add tests/Api.Tests/International/
git commit -m "test(api): exercise carrier webhooks, polling, booking and circuit breaker over HTTP

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 16: Documentation

**Files:**
- Modify: `docs/INTERNATIONAL.md` (status table I4 → Implemented; new "Carrier layer (I4)" section)
- Modify: `docs/SHIPMENT-LIFECYCLE.md` (seven statuses, transition tables, custody)
- Modify: `docs/DOMAIN-MODEL.md` (`CarrierAccount`, `CarrierBooking`, `CarrierTrackingEvent`)
- Modify: `docs/API-SPEC.md` (carrier-account, booking, lifecycle, carrier-events, label and webhook routes with permissions and error codes)
- Modify: `docs/RBAC.md` (three new permissions, OperationsManager/HubStaff grants)
- Modify: `docs/superpowers/specs/2026-09-24-international-i4-carrier-layer-design.md` (the plan-level rulings)

- [ ] **Step 1: Read each doc in full before editing, and match its conventions.**

- [ ] **Step 2: Document what the code actually does** — verify every statement against the source files, not this plan. Cover at least:
  - the transition tables (from `InternationalTransitionRules`), handoff/receive custody, the same-branch inbound two-event receive, the inbound sort from `ReceivedFromCarrier`;
  - store-then-process ingest, the atomic `Pending → Processing` claim, stale-claim reclaim (5 minutes), the 5-attempt retry cap, `Unmatched` re-queue on booking/link;
  - the background tenant scope (`ITenantScopeFactory`) and which jobs use it;
  - webhook authentication (per-account signature, public id, tenant from account), 1 MB cap, IP rate limit;
  - secret handling (Data Protection purposes, never returned except the one-time rotate/create secret, masking of stored payloads);
  - resilience defaults and error-code mapping (502 vs 503);
  - Known limitations: no real carrier adapter yet (DHL is I6); no pricing/`GetRatesAsync` (I5); no frontend/merchant API/merchant webhook events (I7); international returns not supported; existing tenants' roles do not receive the new permissions (role seeds apply at registration); a carrier event arriving before the handoff scan is `Rejected` and is not re-evaluated; polling covers bookings created in the last 60 days.
- [ ] **Step 3: Record the plan-level rulings in the spec** — append a "Rulings made during planning" section to the I4 design spec listing the five rulings from this plan's header, and correct §5.8's claim that transitions write an outbox row.
- [ ] **Step 4: Commit**

```bash
git add docs/
git commit -m "docs: document international I4 carrier layer

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
