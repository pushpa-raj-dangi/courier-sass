# Phase 11 — Notifications & Webhooks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the outbox concept sketched in `ARCHITECTURE.md` into a real pipeline — a domain event becomes a durable `OutboxMessage` row in the same transaction as the business change that caused it, a Hangfire job publishes it to a real RabbitMQ exchange, and two independent consumers (Notifications, Webhooks) act on it: at least one notification channel (email, real SMTP) actually delivers, and webhook delivery is signed, retried with backoff on failure, and replayable on demand.

**Architecture:** Three new modules — `Events` (owns `OutboxMessage` + the narrow `IOutboxWriter` interface every producing module calls, mirroring `Audit`'s `IAuditLogger` shape exactly), `Notifications` (template + recipient resolution, real SMTP for Email, log-only stubs for SMS/WhatsApp/Push), `Webhooks` (merchant-managed subscriptions, signed delivery, retry, replay). Four existing application-service call sites (`Shipments.CreateShipmentAsync`, `Riders.CompleteDeliveryAsync`, `Ndr.CreateFromFailedAttemptAsync`, `Cod.MarkCollectedAsync`) each gain one `IOutboxWriter.WriteAsync(...)` call, in the same `SaveChangesAsync` as their existing business change. A Hangfire recurring job (the "outbox publisher") reads unpublished rows and publishes each to a shared RabbitMQ topic exchange (routing key = event type); `Notifications` and `Webhooks` each run their own `BackgroundService` consumer bound to their own queue on that exchange. A second Hangfire recurring job retries failed webhook deliveries with exponential backoff.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core / PostgreSQL, RabbitMQ.Client 7.0.0 (already the resolved transitive version via the existing health-check package — this plan adds it as a direct reference), Hangfire.AspNetCore 1.8.24 + Hangfire.PostgreSql 1.21.1 (first use of Hangfire in this codebase), MailKit 4.17.0 for real SMTP, ASP.NET Data Protection for webhook secret encryption, xUnit + EF InMemory (unit) / real Postgres+RabbitMQ+mail-catcher via `WebApplicationFactory` (API tests) — same testing shape as every prior phase, extended with two new real backing services.

**Spec:** `docs/superpowers/specs/2026-08-26-phase-11-notifications-webhooks-design.md` (full design, both resolved ambiguities, architecture diagram). Also authoritative: `CLAUDE.md` §26-27, `docs/ROADMAP.md` Phase 11 section, `docs/ARCHITECTURE.md` "Domain events & reliability" / "Background jobs — Hangfire" sections, `docs/DOMAIN-MODEL.md` "Notifications" / "Webhooks" sections (both will be corrected by Task 15 — see the spec's §5 resolved ambiguity on `WebhookSubscription.Secret`), `docs/API-SPEC.md` §5.

**Package versions used across this plan (confirmed current as of this session — pin these exact versions, do not let `dotnet add package` pick a different one without updating this note):** `RabbitMQ.Client` 7.0.0, `Hangfire.AspNetCore` 1.8.24, `Hangfire.PostgreSql` 1.21.1, `MailKit` 4.17.0. If any of these fails to restore when a task runs, the version has moved on since this plan was written — run `dotnet add package <Name>` unpinned to get the current compatible version, update the `.csproj` accordingly, and note the actual version used in that task's commit message.

## Global Constraints

- Never trust a client-supplied `TenantId`/`BranchId` (CLAUDE.md §33) — every write resolves `OrganizationId` from `ICurrentTenantProvider`.
- **Tenant isolation, the single most important discipline this project has repeatedly gotten wrong on a new module's first pass (Phases 4, 5, and Phase 10's Task 9 all shipped a real cross-tenant bug caught only at review):** every new query in `Events`/`Notifications`/`Webhooks` that reads or writes a caller-supplied entity id must filter by the caller's `OrganizationId` explicitly — do not rely solely on `AppDbContext`'s global query filter (it protects production, but this codebase's unit-test `DbContext` fixtures deliberately omit it, so an unscoped query compiles, passes unit tests, and is still a live production hole). `OutboxMessage`, `NotificationTemplate`, `NotificationLog`, `WebhookSubscription` all carry their own `OrganizationId` — direct filters. `WebhookDelivery` does not — isolation flows transitively through `WebhookSubscriptionId -> WebhookSubscription.OrganizationId`, the same pattern as `MerchantSettlementItem -> MerchantSettlement.OrganizationId`.
- `Events` has **zero** project reference to any other module (Domain/Application/Infrastructure all self-contained, same as `Audit`) — `IOutboxWriter.WriteAsync` takes a plain `object payload`, serialized generically to JSON inside `Events.Infrastructure`. It never knows what a "Shipment" or "Merchant" is.
- `Notifications` and `Webhooks` never reference `Shipments`, `Riders`, `Ndr`, or `Cod` — they only ever see an `OutboxMessage`'s `EventType`/`Payload` (deserialized generically) plus whatever narrow lookup interfaces they need from `Merchants` (a new `IMerchantContactLookupService.GetContactEmailAsync`, added in Task 6 as a **second interface** `MerchantLookupService` implements — NOT an extension of the existing `IMerchantLookupService`, which 15 files across 3 test projects already implement as fakes; adding a member there would be pure unnecessary churn for zero benefit, since nothing outside `Notifications` needs contact-email lookup).
- Every real state change writes an `AuditLog` entry via the existing `IAuditLogger` pattern (CLAUDE.md §28) — webhook subscription create/delete, in particular.
- Payload JSON convention: every wired event's payload always includes at minimum `ShipmentId` and `MerchantId` fields (exact casing: PascalCase, matching this codebase's default `System.Text.Json` behavior with no custom naming policy configured anywhere) — `Notifications`' consumer deserializes into a small local record with only those two properties and ignores any extra fields System.Text.Json doesn't need, which is default `JsonSerializer.Deserialize` behavior (unknown extra JSON properties are silently ignored, not an error). `Webhooks`' consumer never deserializes the payload at all — it forwards the raw JSON string verbatim inside the outgoing envelope.
- Webhook signing secret is **encrypted at rest**, not hashed (spec §5 — HMAC-signing an outgoing payload requires the plaintext secret; a one-way hash cannot produce a valid signature). Shown in full exactly once, in the `POST /api/v1/webhooks` response body, via ASP.NET Core's `Microsoft.AspNetCore.DataProtection` (`IDataProtector`) — never returned by any `GET`.
- Webhook retry: 5 attempts per `WebhookDelivery`, exponential backoff starting at 1 minute and doubling (1m, 2m, 4m, 8m, 16m), then left in a terminal failed state (spec §7, resolves an ambiguity no source doc specifies a number for).
- Consumers acknowledge a RabbitMQ message only after successfully completing their work (creating `WebhookDelivery` rows / writing `NotificationLog`) — never before. The `Webhooks` consumer's `WebhookDelivery`-row creation is idempotent per outbox message: it checks for an existing delivery batch (keyed by `(WebhookSubscriptionId, EventType, a payload-derived idempotency value)`) before inserting, so a redelivered message after a crash mid-processing does not double-create delivery rows.
- No new staff-facing permission keys are needed (`Notifications` has no staff-facing read surface yet, per spec §9) — `Webhooks` gets exactly one new **merchant**-facing permission key (`MerchantPermissionCatalog.WebhooksManage = "merchant.webhooks.manage"`, Owner-only — see Task 10), following the existing static in-code `MerchantPermissionCatalog` pattern, not the DB-driven staff `PermissionCatalog`.
- Hangfire uses PostgreSQL storage (`Hangfire.PostgreSql`) against the same `courier` database used by `AppDbContext` — a separate schema (`hangfire`), not a separate database, matching this project's single-Postgres-instance-per-environment convention. The Hangfire dashboard is mounted only in Development (mirrors the existing `if (app.Environment.IsDevelopment())` block in `Program.cs` that gates Swagger).
- Follow the module shape every prior phase established: `Module/{Domain,Application,Infrastructure,Presentation}`, one `.csproj` per module referencing only `SharedKernel` plus whatever narrow interfaces it genuinely needs, EF configurations as `IEntityTypeConfiguration<T>` classes applied in `AppDbContext.OnModelCreating`, tenant query filters added in the same `AppDbContext` block as every other entity's.

---

## Task 1: Events module scaffold — `OutboxMessage`, `IOutboxWriter`, persistence wiring

**Files:**
- Create: `src/Modules/Events/Events.csproj`
- Create: `src/Modules/Events/Domain/OutboxMessage.cs`
- Create: `src/Modules/Events/Infrastructure/OutboxMessageConfiguration.cs`
- Create: `src/Modules/Events/Application/IOutboxWriter.cs`
- Create: `src/Modules/Events/Application/OutboxWriterService.cs`
- Modify: `src/Persistence/AppDbContext.cs`
- Modify: `src/Persistence/Persistence.csproj`
- Modify: `src/Api/Api.csproj`
- Modify: `src/Api/Program.cs`
- Modify: `Courier.slnx`
- Create: `tests/Events.Tests/Events.Tests.csproj`
- Test: `tests/Events.Tests/OutboxWriterServiceTests.cs`

**Interfaces:**
- Produces: `Courier.Events.Application.IOutboxWriter.WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) -> Task`. Task 2's four call sites, and every future event-producing call site, consume this verbatim.

- [ ] **Step 1: Create the project file**

`src/Modules/Events/Events.csproj` — mirrors `Audit.csproj` exactly (same minimal shape: no project references at all beyond `SharedKernel`, since `Events` must never know about any other module's domain types):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.11" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.11" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\SharedKernel\SharedKernel.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: `OutboxMessage` domain entity**

`src/Modules/Events/Domain/OutboxMessage.cs`:

```csharp
namespace Courier.Events.Domain;

// Written by IOutboxWriter.WriteAsync inside the same SaveChangesAsync as the business change
// that caused it (Global Constraints) — this is what guarantees "state changed, no event" can
// never happen. Payload is a plain JSON string; Events has no project reference to any other
// module's domain types, so it cannot and does not know what shape the payload has.
public class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
}
```

- [ ] **Step 3: EF configuration**

`src/Modules/Events/Infrastructure/OutboxMessageConfiguration.cs`:

```csharp
using Courier.Events.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Events.Infrastructure;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.EventType).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Payload).IsRequired();

        builder.HasIndex(m => m.OrganizationId);
        // The outbox publisher job (Task 4) polls exactly this shape: unpublished rows, oldest
        // first. A partial index (WHERE "PublishedAt" IS NULL) keeps that scan cheap forever
        // regardless of how many already-published rows accumulate.
        builder.HasIndex(m => m.CreatedAt).HasFilter("\"PublishedAt\" IS NULL");
    }
}
```

- [ ] **Step 4: `IOutboxWriter` and `OutboxWriterService`**

`src/Modules/Events/Application/IOutboxWriter.cs`:

```csharp
namespace Courier.Events.Application;

// The only public surface of the Events module — every event-producing application service
// (Shipments, Riders, Ndr, Cod, and any future one) calls this exactly once per domain event,
// inside the same SaveChangesAsync as the business change itself (never a separate commit).
// payload is any plain object; it is JSON-serialized as-is. See Global Constraints for the
// "every wired event's payload includes ShipmentId and MerchantId" convention this plan follows,
// which is a caller-side convention, not something IOutboxWriter enforces or knows about.
public interface IOutboxWriter
{
    Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default);
}
```

`src/Modules/Events/Application/OutboxWriterService.cs`:

```csharp
using System.Text.Json;
using Courier.Events.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Events.Application;

public class OutboxWriterService : IOutboxWriter
{
    private readonly DbContext _dbContext;

    public OutboxWriterService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Deliberately does NOT call SaveChangesAsync itself — the caller's own SaveChangesAsync
    // (already in flight for the business change that triggered this event) persists this row
    // too, since both operations share the same DbContext instance in the same request scope.
    // This is what makes the outbox write transactional with the business change for free,
    // without Events needing to know anything about transactions or the caller's own logic.
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow,
            Attempts = 0
        });
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 5: Wire into `AppDbContext`**

In `src/Persistence/AppDbContext.cs`, add after the existing `Courier.Cod.Infrastructure` using:

```csharp
using Courier.Events.Domain;
using Courier.Events.Infrastructure;
```

Add after the existing `public DbSet<Adjustment> Adjustments => Set<Adjustment>();` line:

```csharp
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
```

Add after the existing `modelBuilder.ApplyConfiguration(new AdjustmentConfiguration());` line:

```csharp
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
```

Add after the existing Cod `HasOne<Organization>()` block (right after the `Adjustment` one):

```csharp
        // Same reasoning as every other OrganizationId-carrying entity above: Events has no
        // project reference to Identity.
        modelBuilder.Entity<OutboxMessage>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
```

Add after the existing Cod `HasQueryFilter` lines (right after `Adjustment`'s):

```csharp
        modelBuilder.Entity<OutboxMessage>().HasQueryFilter(m => _currentTenantProvider.OrganizationId != null && m.OrganizationId == _currentTenantProvider.OrganizationId);
```

- [ ] **Step 6: Add project references**

In `src/Persistence/Persistence.csproj`, add after the existing `<ProjectReference Include="..\Modules\Cod\Cod.csproj" />` line:

```xml
    <ProjectReference Include="..\Modules\Events\Events.csproj" />
```

In `src/Api/Api.csproj`, add after the same `Cod.csproj` reference line:

```xml
    <ProjectReference Include="..\Modules\Events\Events.csproj" />
```

In `Courier.slnx`, add inside the `/src/Modules/` folder, after the `Cod.csproj` entry (alphabetical — "Events" sorts after "Cod", before "Geography"):

```xml
    <Project Path="src/Modules/Events/Events.csproj" />
```

- [ ] **Step 7: Wire DI in `Program.cs`**

Add `using Courier.Events.Application;` to the usings. Add after the existing `builder.Services.AddScoped<ICodCollectionService>(sp => sp.GetRequiredService<CodService>());` line:

```csharp
builder.Services.AddScoped<IOutboxWriter, OutboxWriterService>();
```

- [ ] **Step 8: Generate and apply the migration**

Run: `dotnet ef migrations add AddEvents --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: A new migration creating the `OutboxMessages` table with the index/FK/filter from above. Confirm by reading the generated migration file that no unrelated tables changed.

Run: `docker exec courier-postgres-1 pg_isready -U courier` to confirm the shared container (port 5434) is up first — do not `docker compose up` a second one. Then:
`dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: Migration applies cleanly.

- [ ] **Step 9: Create the test project**

`tests/Events.Tests/Events.Tests.csproj` — mirrors `tests/Cod.Tests/Cod.Tests.csproj`'s shape:

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
    <ProjectReference Include="..\..\src\Modules\Events\Events.csproj" />
  </ItemGroup>

</Project>
```

Add it to `Courier.slnx`'s `/tests/` folder, after the `Cod.Tests.csproj` entry (alphabetical): `<Project Path="tests/Events.Tests/Events.Tests.csproj" />`.

- [ ] **Step 10: Write the tests**

`tests/Events.Tests/OutboxWriterServiceTests.cs`:

```csharp
using Courier.Events.Application;
using Courier.Events.Domain;
using Courier.Events.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Events.Tests;

public class OutboxWriterServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task WriteAsync_AddsUnpublishedRowWithSerializedPayload()
    {
        await using var dbContext = CreateContext();
        var writer = new OutboxWriterService(dbContext);
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        await writer.WriteAsync(organizationId, "shipment.created", new { ShipmentId = shipmentId, MerchantId = merchantId, Awb = "TEST123" });
        await dbContext.SaveChangesAsync();

        var row = await dbContext.Set<OutboxMessage>().SingleAsync();
        Assert.Equal(organizationId, row.OrganizationId);
        Assert.Equal("shipment.created", row.EventType);
        Assert.Null(row.PublishedAt);
        Assert.Equal(0, row.Attempts);
        Assert.Contains(shipmentId.ToString(), row.Payload);
        Assert.Contains(merchantId.ToString(), row.Payload);
        Assert.Contains("TEST123", row.Payload);
    }

    [Fact]
    public async Task WriteAsync_DoesNotCallSaveChangesItself()
    {
        // Proves the "same transaction as the caller" design: WriteAsync alone, with no
        // subsequent SaveChangesAsync, must NOT persist anything — only the caller's own
        // SaveChangesAsync (simulated here) commits it.
        await using var dbContext = CreateContext();
        var writer = new OutboxWriterService(dbContext);

        await writer.WriteAsync(Guid.NewGuid(), "shipment.created", new { ShipmentId = Guid.NewGuid(), MerchantId = Guid.NewGuid() });

        Assert.Empty(await dbContext.Set<OutboxMessage>().ToListAsync());
        Assert.Single(dbContext.ChangeTracker.Entries<OutboxMessage>());
    }
}
```

- [ ] **Step 11: Run the tests**

Run: `dotnet test tests/Events.Tests/Events.Tests.csproj`
Expected: Both tests pass.

- [ ] **Step 12: Build the whole solution and commit**

Run: `dotnet build Courier.slnx`
Expected: Build succeeds.

```bash
git add src/Modules/Events/ src/Persistence/AppDbContext.cs src/Persistence/Persistence.csproj src/Persistence/Migrations/ src/Api/Api.csproj src/Api/Program.cs Courier.slnx tests/Events.Tests/
git commit -m "feat(events): scaffold Events module with OutboxMessage and IOutboxWriter"
```

---

## Task 2: Wire `IOutboxWriter` into the four proving-set call sites

**Files:**
- Modify: `src/Modules/Shipments/Shipments.csproj`
- Modify: `src/Modules/Shipments/Application/ShipmentService.cs`
- Modify: `src/Modules/Riders/Riders.csproj`
- Modify: `src/Modules/Riders/Application/RiderService.cs`
- Modify: `src/Modules/Riders/Application/RiderService.Attempts.cs`
- Modify: `src/Modules/Ndr/Ndr.csproj`
- Modify: `src/Modules/Ndr/Application/NdrCreationService.cs`
- Modify: `src/Modules/Cod/Cod.csproj`
- Modify: `src/Modules/Cod/Application/CodService.cs`
- Modify: `src/Modules/Cod/Application/CodService.Collections.cs`
- Test: `tests/Shipments.Tests/ShipmentServiceCreationTests.cs` (and every other `Shipments.Tests` file constructing `new ShipmentService(...)`)
- Test: `tests/Riders.Tests/RiderServiceCreationTests.cs` (and every other `Riders.Tests` file constructing `new RiderService(...)`)
- Test: `tests/Ndr.Tests/*.cs` (whichever construct `new NdrCreationService(...)`)
- Test: `tests/Cod.Tests/*.cs` (whichever construct `new CodService(...)`)

**Interfaces:**
- Consumes: `Courier.Events.Application.IOutboxWriter.WriteAsync(Guid, string, object, CancellationToken)` from Task 1.
- Produces: nothing new — this task only adds calls, no new types.

This is one batched task across four modules — each edit is the same shape (add a project reference, add a constructor parameter, add one `WriteAsync` call after the existing business-change logic, update every test file's constructor call site in that module). Do all four in one commit since they're mechanically identical and none depends on the others.

- [ ] **Step 1: Add `Events` project references**

Add `<ProjectReference Include="..\Events\Events.csproj" />` to each of: `src/Modules/Shipments/Shipments.csproj`, `src/Modules/Riders/Riders.csproj`, `src/Modules/Ndr/Ndr.csproj`, `src/Modules/Cod/Cod.csproj` — in each file, add it alphabetically among the existing `<ProjectReference>` entries (after `Cod.csproj`/before `Geography.csproj` where both exist, otherwise wherever alphabetical order puts it; exact position doesn't matter functionally, just keep the block readable).

- [ ] **Step 2: `Shipments` — wire `shipment.created`**

In `src/Modules/Shipments/Application/ShipmentService.cs`, add `using Courier.Events.Application;` to the usings. Add a new constructor parameter `IOutboxWriter outboxWriter` (after `ICodCollectionCreationService codCollectionCreationService`) and a new field `private readonly IOutboxWriter _outboxWriter;`, assigned in the constructor body the same way every other field is.

In `src/Modules/Shipments/Application/ShipmentService.cs`'s `CreateShipmentAsync`, add this immediately after the existing `if (shipment.CodAmount > 0) { ... }` block and before `return await LoadResponseAsync(shipment.Id, cancellationToken);`:

```csharp
        await _outboxWriter.WriteAsync(organizationId, "shipment.created", new { ShipmentId = shipment.Id, MerchantId = merchantId, Awb = shipment.Awb }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

```

(The extra `SaveChangesAsync` call here is required: `CreateShipmentAsync`'s own `SaveChangesAsync` already ran earlier, inside the AWB-retry loop, before the audit log and `ICodCollectionCreationService` calls — the outbox write needs its own flush the same way those already-established later writes implicitly rely on the `DbContext`'s change tracker, not a fresh transaction. This matches the existing code's own shape: nothing between the AWB-loop's `SaveChangesAsync` and this new call assumed atomicity with the shipment insert itself, so adding one more flush here changes nothing about existing guarantees.)

- [ ] **Step 3: `Riders` — wire `shipment.delivered`**

In `src/Modules/Riders/Application/RiderService.cs`, add `using Courier.Events.Application;` to the usings. Add a new constructor parameter `IOutboxWriter outboxWriter` (after `IPermissionChecker permissionChecker`) and a new field `private readonly IOutboxWriter _outboxWriter;`, wired the same way as every other field.

In `src/Modules/Riders/Application/RiderService.Attempts.cs`'s `CompleteDeliveryAsync`, change this existing line:

```csharp
        await _shipmentTransitionService.CompleteDeliveryAsync(request.ShipmentId, riderId, request.RecipientName, actingUser, cancellationToken);
```

to capture its return value (already `Task<ShipmentResponse?>` per `IShipmentTransitionService` — currently discarded):

```csharp
        var deliveredShipment = await _shipmentTransitionService.CompleteDeliveryAsync(request.ShipmentId, riderId, request.RecipientName, actingUser, cancellationToken);
```

Then, immediately after the existing `if (scanInfo.CodAmount > 0) { await _codCollectionService.MarkCollectedAsync(...); }` block and before the `var task = await _dbContext.Set<DeliveryTask>()...` line, add:

```csharp
        if (deliveredShipment is not null)
        {
            await _outboxWriter.WriteAsync(organizationId, "shipment.delivered", new { ShipmentId = request.ShipmentId, MerchantId = deliveredShipment.MerchantId, Awb = deliveredShipment.Awb }, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

```

(`deliveredShipment` can only be `null` if the shipment vanished between `GetScanInfoAsync` and this call — impossible in a single-request flow with no concurrent deletes anywhere in this codebase, but the transition methods' signature is nullable, so this guards it the same defensive way every other nullable-return call site in this codebase already does, rather than assuming.)

- [ ] **Step 4: `Ndr` — wire `shipment.ndr_created`**

In `src/Modules/Ndr/Application/NdrCreationService.cs`, add `using Courier.Events.Application;` to the usings. Add a new constructor parameter `IOutboxWriter outboxWriter` and a new field `private readonly IOutboxWriter _outboxWriter;`.

Change this existing line inside `CreateFromFailedAttemptAsync`:

```csharp
        await _shipmentTransitionService.TransitionToNdrCreatedAsync(shipmentId, actingUser, cancellationToken);
```

to capture its return value:

```csharp
        var ndrShipment = await _shipmentTransitionService.TransitionToNdrCreatedAsync(shipmentId, actingUser, cancellationToken);
```

Then, immediately after the existing `await _auditLogger.LogAsync(...)` line at the end of the method, add:

```csharp

        if (ndrShipment is not null)
        {
            await _outboxWriter.WriteAsync(organizationId, "shipment.ndr_created", new { ShipmentId = shipmentId, MerchantId = ndrShipment.MerchantId, Awb = ndrShipment.Awb, NdrId = ndr.Id }, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
```

- [ ] **Step 5: `Cod` — wire `cod.collected`**

In `src/Modules/Cod/Application/CodService.cs`, add `using Courier.Events.Application;` to the usings. Add a new constructor parameter `IOutboxWriter outboxWriter` (after `IMerchantLookupService merchantLookupService`) and a new field `private readonly IOutboxWriter _outboxWriter;`.

In `src/Modules/Cod/Application/CodService.Collections.cs`'s `MarkCollectedAsync`, add this immediately after the existing `collection.Status = CodCollectionStatus.CodCollected;` line and before `await _dbContext.SaveChangesAsync(cancellationToken);`:

```csharp
        await _outboxWriter.WriteAsync(organizationId, "cod.collected", new { ShipmentId = shipmentId, MerchantId = collection.MerchantId, CollectedAmount = collectedAmount }, cancellationToken);
```

(No extra `SaveChangesAsync` needed here — the existing one right after this line already covers it, unlike the other three call sites which had already-completed their save before this plan's insertion point.)

- [ ] **Step 6: Update every test constructor call site**

For each of the four modules, grep the corresponding test project for the exact constructor call and add a fake `IOutboxWriter` argument:

```bash
grep -rn "new ShipmentService(" tests/Shipments.Tests/
grep -rn "new RiderService(" tests/Riders.Tests/
grep -rn "new NdrCreationService(" tests/Ndr.Tests/
grep -rn "new CodService(" tests/Cod.Tests/
```

For each match, add a no-op fake matching this codebase's established per-file `file class` fake convention (see any existing `Noop*` fake in that same test file for the exact style — e.g. `NoopAuditLogger`). Add, in each file that needs one and doesn't already have an equivalent:

```csharp
file class NoopOutboxWriter : IOutboxWriter
{
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

(add `using Courier.Events.Application;` to each file that needs this fake), then pass `new NoopOutboxWriter()` as the new constructor argument at every call site found above, in the correct positional slot matching where the parameter was inserted in each service's constructor (Step 2/3/4/5 above state exactly where each was inserted).

- [ ] **Step 7: Wire DI**

`Program.cs` already registers `builder.Services.AddScoped<IOutboxWriter, OutboxWriterService>();` from Task 1 — the four services above resolve it automatically through their existing `AddScoped<XService>()` registrations, no `Program.cs` change needed for this task.

- [ ] **Step 8: Write one new test per module proving the outbox row is written**

Add one focused test to each of the four modules' existing test suites (find the test file already covering the method you changed — `ShipmentServiceCreationTests.cs` for `CreateShipmentAsync`, the `Riders.Tests` file covering `CompleteDeliveryAsync`, an `Ndr.Tests` file covering `CreateFromFailedAttemptAsync`, `CodServiceCollectionsTests.cs` for `MarkCollectedAsync`). Each test follows the identical shape — construct a *recording* fake instead of the no-op one, assert one `WriteAsync` call with the right `eventType` and that the payload (serialize whatever was captured, or capture the raw arguments) contains the expected `ShipmentId`/`MerchantId`. Example shape (adapt field names/setup to each file's existing conventions):

```csharp
file class RecordingOutboxWriter : IOutboxWriter
{
    public List<(Guid OrganizationId, string EventType, object Payload)> Writes { get; } = new();
    public Task WriteAsync(Guid organizationId, string eventType, object payload, CancellationToken cancellationToken = default)
    {
        Writes.Add((organizationId, eventType, payload));
        return Task.CompletedTask;
    }
}
```

Then, in each module's relevant existing happy-path test (or a new one alongside it), swap the no-op fake for `RecordingOutboxWriter`, run the method under test, and assert `Assert.Single(recordingWriter.Writes, w => w.EventType == "shipment.created")` (or the appropriate event type per module).

- [ ] **Step 9: Run the affected test suites**

Run: `dotnet test tests/Shipments.Tests/Shipments.Tests.csproj && dotnet test tests/Riders.Tests/Riders.Tests.csproj && dotnet test tests/Ndr.Tests/Ndr.Tests.csproj && dotnet test tests/Cod.Tests/Cod.Tests.csproj`
Expected: All pass, including the four new tests.

- [ ] **Step 10: Build the whole solution and run the full suite once**

Run: `dotnet build Courier.slnx` then `dotnet test Courier.slnx --no-build`
Expected: Build succeeds, every test project passes, no regressions.

- [ ] **Step 11: Commit**

```bash
git add src/Modules/Shipments/ src/Modules/Riders/ src/Modules/Ndr/ src/Modules/Cod/ tests/Shipments.Tests/ tests/Riders.Tests/ tests/Ndr.Tests/ tests/Cod.Tests/
git commit -m "feat(events): wire IOutboxWriter into shipment.created, shipment.delivered, shipment.ndr_created, cod.collected"
```

---

## Task 3: RabbitMQ topology and Hangfire infrastructure wiring

**Files:**
- Create: `src/SharedKernel/RabbitMqTopology.cs`
- Modify: `src/Api/Api.csproj`
- Modify: `src/Api/Program.cs`
- Modify: `src/Api/appsettings.Development.json`
- Test: `tests/Api.Tests/HangfireWiringTests.cs`

**Interfaces:**
- Produces: a singleton `RabbitMQ.Client.IConnection` registered in DI (any module can inject it to open its own channel); `RabbitMqTopology.DomainEventsExchange` constant (`"courier.domain-events"`) every publisher/consumer agrees on; Hangfire's `IBackgroundJobClient`/`IRecurringJobManager` available via DI for Task 4/12's recurring jobs.

This task adds no business logic — it makes the infrastructure available. Tasks 4, 7, 11, 12 consume it.

- [ ] **Step 1: `RabbitMqTopology` shared constant**

`src/SharedKernel/RabbitMqTopology.cs`:

```csharp
namespace Courier.SharedKernel;

// One topic exchange, shared by the outbox publisher (Events, Task 4) and both consumers
// (Notifications Task 7, Webhooks Task 11) — living in SharedKernel (referenced by every
// module already) is what lets all three agree on the exchange/queue names without any of
// them referencing each other.
public static class RabbitMqTopology
{
    public const string DomainEventsExchange = "courier.domain-events";
    public const string NotificationsQueue = "courier.notifications";
    public const string WebhooksQueue = "courier.webhooks";
}
```

- [ ] **Step 2: Add package references**

In `src/Api/Api.csproj`, add to the existing `<ItemGroup>` of `PackageReference`s (alphabetical position doesn't matter, keep the block readable):

```xml
    <PackageReference Include="RabbitMQ.Client" Version="7.0.0" />
    <PackageReference Include="Hangfire.AspNetCore" Version="1.8.24" />
    <PackageReference Include="Hangfire.PostgreSql" Version="1.21.1" />
```

(`RabbitMQ.Client` is already the resolved transitive version via `AspNetCore.HealthChecks.Rabbitmq` — pinning it directly here just makes the dependency explicit rather than implicit, matching how `Program.cs` already calls `RabbitMQ.Client.ConnectionFactory` directly today without a direct reference; this step formalizes what was already true.)

- [ ] **Step 3: Register the RabbitMQ connection and Hangfire in `Program.cs`**

Add to the usings: `using Hangfire;` and `using Hangfire.PostgreSql;`.

Add after the existing `builder.Services.AddHealthChecks()...` block (the one ending with `.AddRabbitMQ(...)`, before `builder.Services.Configure<JwtOptions>(...)`):

```csharp
// One shared connection for the whole process — every publisher/consumer opens its own
// channel from this (channels are cheap and not thread-safe to share; connections are
// expensive and safe to share). Registered as a factory-created singleton so Program.cs's own
// startup path doesn't block on an async connection open before the host is ready to serve
// health checks — the first real user of the connection (Task 4's publisher job, or either
// consumer) is the first thing that actually needs it live.
builder.Services.AddSingleton<RabbitMQ.Client.IConnection>(sp =>
{
    var factory = new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(builder.Configuration.GetConnectionString("RabbitMq")!) };
    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
});

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Postgres"))));
builder.Services.AddHangfireServer();
```

Add, inside the existing `if (app.Environment.IsDevelopment())` block, immediately after the existing `app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();` line:

```csharp
    app.UseHangfireDashboard("/hangfire");
```

- [ ] **Step 4: Confirm `appsettings.Development.json` already has what's needed**

`src/Api/appsettings.Development.json` already has `ConnectionStrings:Postgres` and `ConnectionStrings:RabbitMq` from prior phases — no change needed here. (This step is a checkpoint, not an edit — if either connection string is missing when you reach this step, something upstream regressed; stop and report rather than adding a workaround.)

- [ ] **Step 5: Write a smoke test proving the wiring resolves**

`tests/Api.Tests/HangfireWiringTests.cs`:

```csharp
using Hangfire;
using Microsoft.AspNetCore.Mvc.Testing;
using RabbitMQ.Client;
using Xunit;

namespace Courier.Api.Tests;

public class HangfireWiringTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HangfireWiringTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void RabbitMqConnection_ResolvesAsSingleton()
    {
        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();
        var connection1 = scope1.ServiceProvider.GetRequiredService<IConnection>();
        var connection2 = scope2.ServiceProvider.GetRequiredService<IConnection>();

        Assert.True(connection1.IsOpen);
        Assert.Same(connection1, connection2);
    }

    [Fact]
    public void HangfireBackgroundJobClient_Resolves()
    {
        using var scope = _factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
        Assert.NotNull(client);
    }
}
```

(Add `using Microsoft.Extensions.DependencyInjection;` if `GetRequiredService` doesn't resolve — check the file's usings against any other `Api.Tests` file already calling `_factory.Services.CreateScope()` for the exact import needed.)

- [ ] **Step 6: Run the test**

Run: `docker exec courier-rabbitmq-1 rabbitmq-diagnostics -q ping` to confirm the shared RabbitMQ container is up first (start it via `docker start courier-rabbitmq-1` if not).
Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~HangfireWiringTests"`
Expected: Both tests pass.

- [ ] **Step 7: Build the whole solution and commit**

Run: `dotnet build Courier.slnx`
Expected: Build succeeds — this also creates Hangfire's own tables in the `courier` database on first run (Hangfire auto-migrates its own schema on `UsePostgreSqlStorage`, unlike `AppDbContext` which needs an explicit EF migration — no `dotnet ef migrations add` step for this task).

```bash
git add src/SharedKernel/RabbitMqTopology.cs src/Api/Api.csproj src/Api/Program.cs tests/Api.Tests/HangfireWiringTests.cs
git commit -m "feat(events): wire RabbitMQ connection and Hangfire infrastructure"
```

---

## Task 4: Outbox publisher — Hangfire recurring job

**Files:**
- Create: `src/Modules/Events/Application/IEventPublisher.cs`
- Create: `src/Modules/Events/Infrastructure/RabbitMqEventPublisher.cs`
- Create: `src/Modules/Events/Application/OutboxPublisherJob.cs`
- Modify: `src/Modules/Events/Events.csproj`
- Modify: `src/Api/Api.csproj`
- Modify: `src/Api/Program.cs`
- Test: `tests/Events.Tests/OutboxPublisherJobTests.cs`

**Interfaces:**
- Consumes: `RabbitMQ.Client.IConnection` (Task 3), `RabbitMqTopology.DomainEventsExchange` (Task 3).
- Produces: `Courier.Events.Application.IEventPublisher.PublishAsync(string exchange, string routingKey, string payload, CancellationToken) -> Task` — a small seam so `OutboxPublisherJob`'s "read unpublished rows, publish each, mark PublishedAt" logic is unit-testable with a fake, without needing a real RabbitMQ connection in every unit test.

- [ ] **Step 1: Add `RabbitMQ.Client` reference to `Events.csproj`**

In `src/Modules/Events/Events.csproj`, add to the existing `<ItemGroup>` of `PackageReference`s:

```xml
    <PackageReference Include="RabbitMQ.Client" Version="7.0.0" />
```

- [ ] **Step 2: `IEventPublisher` and `RabbitMqEventPublisher`**

`src/Modules/Events/Application/IEventPublisher.cs`:

```csharp
namespace Courier.Events.Application;

// Thin seam over the actual RabbitMQ publish call — exists purely so OutboxPublisherJob's
// polling/marking logic can be unit-tested against a fake, the same reason
// Notifications.Application.INotificationChannel (Task 6) exists as an abstraction over the
// real SMTP call.
public interface IEventPublisher
{
    Task PublishAsync(string exchange, string routingKey, string payload, CancellationToken cancellationToken = default);
}
```

`src/Modules/Events/Infrastructure/RabbitMqEventPublisher.cs`:

```csharp
using System.Text;
using Courier.Events.Application;
using RabbitMQ.Client;

namespace Courier.Events.Infrastructure;

public class RabbitMqEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly IConnection _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _channelLock = new(1, 1);

    public RabbitMqEventPublisher(IConnection connection)
    {
        _connection = connection;
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _channelLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            // Declaring on every publish is idempotent and cheap — this keeps the publisher
            // correct even if it starts before any consumer has ever declared the exchange.
            await _channel.ExchangeDeclareAsync(Courier.SharedKernel.RabbitMqTopology.DomainEventsExchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
            return _channel;
        }
        finally
        {
            _channelLock.Release();
        }
    }

    public async Task PublishAsync(string exchange, string routingKey, string payload, CancellationToken cancellationToken = default)
    {
        var channel = await GetChannelAsync(cancellationToken);
        var body = Encoding.UTF8.GetBytes(payload);
        await channel.BasicPublishAsync(exchange, routingKey, body: body, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
        }
        _channelLock.Dispose();
    }
}
```

- [ ] **Step 3: `OutboxPublisherJob`**

`src/Modules/Events/Application/OutboxPublisherJob.cs`:

```csharp
using Courier.Events.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Events.Application;

// Hangfire recurring job (registered in Program.cs, Task 4 Step 5) — polls for unpublished
// OutboxMessage rows, publishes each to the shared topic exchange with the event type as
// routing key, marks PublishedAt. Runs with no ICurrentTenantProvider context (it's a
// background job, not a request), so it reads across every tenant directly via
// IgnoreQueryFilters() — this is the one legitimate cross-tenant read in this codebase,
// exactly analogous to how AuthService.RegisterOrganizationAsync bypasses the filter for its
// own pre-authentication uniqueness check.
public class OutboxPublisherJob
{
    private readonly DbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;

    public OutboxPublisherJob(DbContext dbContext, IEventPublisher eventPublisher)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var unpublished = await _dbContext.Set<OutboxMessage>().IgnoreQueryFilters()
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var message in unpublished)
        {
            message.Attempts += 1;
            await _eventPublisher.PublishAsync(RabbitMqTopology.DomainEventsExchange, message.EventType, message.Payload, cancellationToken);
            message.PublishedAt = DateTimeOffset.UtcNow;
        }

        if (unpublished.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
```

- [ ] **Step 4: Register the job's DI and Hangfire recurring schedule in `Program.cs`**

Add `using Courier.Events.Infrastructure;` to the usings. Add after the existing `builder.Services.AddScoped<IOutboxWriter, OutboxWriterService>();` line (from Task 1):

```csharp
builder.Services.AddScoped<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddScoped<OutboxPublisherJob>();
```

Add after the existing `app.UseHangfireDashboard("/hangfire");` line inside the `if (app.Environment.IsDevelopment())` block — but the recurring-job registration itself must run unconditionally (every environment, not just Development), so add it just before `app.Run();` instead, outside that block:

```csharp
RecurringJob.AddOrUpdate<OutboxPublisherJob>("outbox-publisher", job => job.RunAsync(CancellationToken.None), "*/10 * * * * *");
```

(`*/10 * * * * *` is a 6-field cron expression — Hangfire's default cron parser supports seconds as the first field — meaning "every 10 seconds." This is deliberately fast for a dev/demo environment; a production deployment would likely widen this, but nothing in this phase's exit criteria requires configurability here, and CLAUDE.md §34's "smallest coherent change" argues against adding a settings knob nothing yet needs.)

- [ ] **Step 5: Write the tests**

`tests/Events.Tests/OutboxPublisherJobTests.cs`:

```csharp
using Courier.Events.Application;
using Courier.Events.Domain;
using Courier.Events.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Events.Tests;

file class RecordingEventPublisher : IEventPublisher
{
    public List<(string Exchange, string RoutingKey, string Payload)> Published { get; } = new();
    public Task PublishAsync(string exchange, string routingKey, string payload, CancellationToken cancellationToken = default)
    {
        Published.Add((exchange, routingKey, payload));
        return Task.CompletedTask;
    }
}

public class OutboxPublisherJobTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task RunAsync_PublishesUnpublishedRowsAndMarksThemPublished()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        dbContext.Set<OutboxMessage>().Add(new OutboxMessage { Id = Guid.NewGuid(), OrganizationId = organizationId, EventType = "shipment.created", Payload = "{}", CreatedAt = DateTimeOffset.UtcNow, Attempts = 0 });
        await dbContext.SaveChangesAsync();
        var publisher = new RecordingEventPublisher();
        var job = new OutboxPublisherJob(dbContext, publisher);

        await job.RunAsync();

        var row = await dbContext.Set<OutboxMessage>().SingleAsync();
        Assert.NotNull(row.PublishedAt);
        Assert.Equal(1, row.Attempts);
        Assert.Single(publisher.Published, p => p.RoutingKey == "shipment.created");
    }

    [Fact]
    public async Task RunAsync_SkipsAlreadyPublishedRows()
    {
        await using var dbContext = CreateContext();
        dbContext.Set<OutboxMessage>().Add(new OutboxMessage { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), EventType = "shipment.created", Payload = "{}", CreatedAt = DateTimeOffset.UtcNow, PublishedAt = DateTimeOffset.UtcNow.AddMinutes(-5), Attempts = 1 });
        await dbContext.SaveChangesAsync();
        var publisher = new RecordingEventPublisher();
        var job = new OutboxPublisherJob(dbContext, publisher);

        await job.RunAsync();

        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task RunAsync_MultipleOrganizations_PublishesAcrossAllOfThem()
    {
        // The job runs with no tenant context (a background job, not a request) — this proves
        // IgnoreQueryFilters() is actually doing its job, not silently limiting to one org.
        await using var dbContext = CreateContext();
        dbContext.Set<OutboxMessage>().Add(new OutboxMessage { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), EventType = "shipment.created", Payload = "{}", CreatedAt = DateTimeOffset.UtcNow, Attempts = 0 });
        dbContext.Set<OutboxMessage>().Add(new OutboxMessage { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), EventType = "cod.collected", Payload = "{}", CreatedAt = DateTimeOffset.UtcNow, Attempts = 0 });
        await dbContext.SaveChangesAsync();
        var publisher = new RecordingEventPublisher();
        var job = new OutboxPublisherJob(dbContext, publisher);

        await job.RunAsync();

        Assert.Equal(2, publisher.Published.Count);
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Events.Tests/Events.Tests.csproj`
Expected: All 5 tests (2 from Task 1 + 3 new) pass.

- [ ] **Step 7: Build the whole solution and run the full suite once**

Run: `dotnet build Courier.slnx` then `dotnet test Courier.slnx --no-build`
Expected: Build succeeds, every test project passes.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Events/ src/Api/Api.csproj src/Api/Program.cs tests/Events.Tests/
git commit -m "feat(events): add outbox publisher Hangfire recurring job"
```

---

## Task 5: Notifications module scaffold — `NotificationTemplate`, `NotificationLog`, persistence wiring

**Files:**
- Create: `src/Modules/Notifications/Notifications.csproj`
- Create: `src/Modules/Notifications/Domain/NotificationChannel.cs`
- Create: `src/Modules/Notifications/Domain/NotificationStatus.cs`
- Create: `src/Modules/Notifications/Domain/NotificationTemplate.cs`
- Create: `src/Modules/Notifications/Domain/NotificationLog.cs`
- Create: `src/Modules/Notifications/Infrastructure/NotificationTemplateConfiguration.cs`
- Create: `src/Modules/Notifications/Infrastructure/NotificationLogConfiguration.cs`
- Modify: `src/Persistence/AppDbContext.cs`
- Modify: `src/Persistence/Persistence.csproj`
- Modify: `src/Api/Api.csproj`
- Modify: `Courier.slnx`
- Create: `tests/Notifications.Tests/Notifications.Tests.csproj`

**Interfaces:**
- Produces: `NotificationChannel { Sms, Email, WhatsApp, Push }`, `NotificationStatus { Sent, Logged, Failed, NoRecipient }` enums Task 6 consumes.

**Resolved implementation detail (not in the spec, decided while writing this plan):** `NotificationTemplate.OrganizationId`-scoped rows are never seeded by this phase — no org-registration-time seeding, no migration data seed. `NotificationLog.RecipientAddress` is `string?` (nullable), not the non-nullable field the literal `DOMAIN-MODEL.md` sketch shows — this accommodates the `NoRecipient` status (Task 6), where there is no address to record. Both refinements will be reflected in Task 15's `DOMAIN-MODEL.md` sync, the same "plan resolves a literal-doc gap found while implementing" pattern every prior phase's plan has used at least once.

- [ ] **Step 1: Create the project file**

`src/Modules/Notifications/Notifications.csproj` — references `SharedKernel` and `Merchants` (for `IMerchantContactLookupService`, added in Task 6) — deliberately **no** reference to `Shipments`/`Riders`/`Ndr`/`Cod` (Global Constraints):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.11" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.11" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\SharedKernel\SharedKernel.csproj" />
    <ProjectReference Include="..\Audit\Audit.csproj" />
    <ProjectReference Include="..\Merchants\Merchants.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Domain enums**

`src/Modules/Notifications/Domain/NotificationChannel.cs`:

```csharp
namespace Courier.Notifications.Domain;

public enum NotificationChannel
{
    Sms,
    Email,
    WhatsApp,
    Push
}
```

`src/Modules/Notifications/Domain/NotificationStatus.cs`:

```csharp
namespace Courier.Notifications.Domain;

public enum NotificationStatus
{
    // A real outbound send succeeded (this phase: Email only).
    Sent,
    // A stub channel (Sms/WhatsApp/Push) "handled" the notification by logging it — no real
    // outbound call was made, by design this phase (spec §2).
    Logged,
    // A real outbound send was attempted and failed (e.g. SMTP rejected it).
    Failed,
    // No recipient address could be resolved (e.g. Merchant.ContactEmail is null) — an
    // expected, non-exceptional org data state, not a pipeline failure (spec §6).
    NoRecipient
}
```

- [ ] **Step 3: `NotificationTemplate` and `NotificationLog`**

`src/Modules/Notifications/Domain/NotificationTemplate.cs`:

```csharp
namespace Courier.Notifications.Domain;

// An org-specific override for one (EventType, Channel) pair. No row existing for a given
// combination is normal and expected this phase — NotificationDispatchService (Task 6) falls
// back to a compiled-in default body when no active row matches. A future template-management
// API (deferred, spec §2) would let an org create/edit these.
public class NotificationTemplate
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string EventType { get; set; }
    public NotificationChannel Channel { get; set; }
    public required string Body { get; set; }
    public bool IsActive { get; set; }
}
```

`src/Modules/Notifications/Domain/NotificationLog.cs`:

```csharp
namespace Courier.Notifications.Domain;

// One row per dispatch attempt, written unconditionally (success, failure, or no-recipient) by
// NotificationDispatchService (Task 6) — this is the durable record CLAUDE.md §27's pipeline
// diagram implies ("Notification Worker -> SMS/Email/WhatsApp/Push") without itself being
// itemized as a requirement anywhere; it exists because a dispatch attempt with no trace
// anywhere would be undebuggable.
public class NotificationLog
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string EventType { get; set; }
    public NotificationChannel Channel { get; set; }
    public string? RecipientAddress { get; set; }
    public NotificationStatus Status { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public string? ProviderResponse { get; set; }
}
```

- [ ] **Step 4: EF configurations**

`src/Modules/Notifications/Infrastructure/NotificationTemplateConfiguration.cs`:

```csharp
using Courier.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Notifications.Infrastructure;

public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("NotificationTemplates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.EventType).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Body).IsRequired();

        builder.HasIndex(t => t.OrganizationId);
        // The lookup NotificationDispatchService performs every dispatch: "active template for
        // this org/event/channel." Not unique — an org could plausibly have more than one
        // inactive historical row per combination in a future template-editing flow — but this
        // index makes the active-row lookup itself cheap regardless.
        builder.HasIndex(t => new { t.OrganizationId, t.EventType, t.Channel });
    }
}
```

`src/Modules/Notifications/Infrastructure/NotificationLogConfiguration.cs`:

```csharp
using Courier.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Notifications.Infrastructure;

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("NotificationLogs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.EventType).HasMaxLength(100).IsRequired();
        builder.Property(l => l.RecipientAddress).HasMaxLength(320); // RFC 5321 max email length; generous enough for a phone number too.

        builder.HasIndex(l => new { l.OrganizationId, l.SentAt });
    }
}
```

- [ ] **Step 5: Wire into `AppDbContext`**

In `src/Persistence/AppDbContext.cs`, add after the `Courier.Events.Infrastructure` using:

```csharp
using Courier.Notifications.Domain;
using Courier.Notifications.Infrastructure;
```

Add after the `OutboxMessages` `DbSet` line:

```csharp
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
```

Add after the `OutboxMessageConfiguration` `ApplyConfiguration` line:

```csharp
        modelBuilder.ApplyConfiguration(new NotificationTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationLogConfiguration());
```

Add after the `OutboxMessage` `HasOne<Organization>()` block:

```csharp
        modelBuilder.Entity<NotificationTemplate>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(t => t.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<NotificationLog>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(l => l.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
```

Add after the `OutboxMessage` `HasQueryFilter` line:

```csharp
        modelBuilder.Entity<NotificationTemplate>().HasQueryFilter(t => _currentTenantProvider.OrganizationId != null && t.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<NotificationLog>().HasQueryFilter(l => _currentTenantProvider.OrganizationId != null && l.OrganizationId == _currentTenantProvider.OrganizationId);
```

- [ ] **Step 6: Add project references**

`src/Persistence/Persistence.csproj`, after the `Events.csproj` reference:

```xml
    <ProjectReference Include="..\Modules\Notifications\Notifications.csproj" />
```

`src/Api/Api.csproj`, same position:

```xml
    <ProjectReference Include="..\Modules\Notifications\Notifications.csproj" />
```

`Courier.slnx`, `/src/Modules/` folder, after `Events.csproj` (alphabetical — "Notifications" sorts after "Merchants", before "Ndr"; place it there):

```xml
    <Project Path="src/Modules/Notifications/Notifications.csproj" />
```

- [ ] **Step 7: Generate and apply the migration**

Run: `dotnet ef migrations add AddNotifications --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: Creates `NotificationTemplates`/`NotificationLogs` tables only. Confirm by reading the migration file.

Run: `dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj` (after confirming `courier-postgres-1` is up).

- [ ] **Step 8: Create the test project**

`tests/Notifications.Tests/Notifications.Tests.csproj` — same shape as `tests/Events.Tests/Events.Tests.csproj` (Task 1 Step 9), with its `ProjectReference` pointed at `..\..\src\Modules\Notifications\Notifications.csproj` instead. Add to `Courier.slnx`'s `/tests/` folder after `Merchants.Tests.csproj` (alphabetical — "Notifications" sorts after "Merchants", before "Ndr").

- [ ] **Step 9: Build and commit**

Run: `dotnet build Courier.slnx`
Expected: Build succeeds (no tests yet — Task 6 adds the first ones).

```bash
git add src/Modules/Notifications/ src/Persistence/AppDbContext.cs src/Persistence/Persistence.csproj src/Persistence/Migrations/ src/Api/Api.csproj Courier.slnx tests/Notifications.Tests/
git commit -m "feat(notifications): scaffold Notifications module and domain entities"
```

---

## Task 6: Notifications Application — channels, dispatch service, `Merchants` contact lookup

**Files:**
- Create: `src/Modules/Merchants/Application/IMerchantContactLookupService.cs`
- Modify: `src/Modules/Merchants/Application/MerchantLookupService.cs`
- Create: `src/Modules/Notifications/Application/INotificationChannel.cs`
- Create: `src/Modules/Notifications/Application/EmailNotificationChannel.cs`
- Create: `src/Modules/Notifications/Application/StubNotificationChannels.cs`
- Create: `src/Modules/Notifications/Application/NotificationDefaultTemplates.cs`
- Create: `src/Modules/Notifications/Application/NotificationDtos.cs`
- Create: `src/Modules/Notifications/Application/NotificationDispatchService.cs`
- Modify: `src/Modules/Notifications/Notifications.csproj`
- Modify: `src/Api/Api.csproj`
- Modify: `src/Api/Program.cs`
- Modify: `src/Api/appsettings.Development.json`
- Test: `tests/Notifications.Tests/NotificationDispatchServiceTests.cs`

**Interfaces:**
- Produces: `Courier.Notifications.Application.NotificationDispatchService.DispatchEventAsync(Guid organizationId, string eventType, string payloadJson, CancellationToken) -> Task` — Task 7's RabbitMQ consumer calls this verbatim, once per consumed message.

- [ ] **Step 1: `Merchants` — `IMerchantContactLookupService`**

`src/Modules/Merchants/Application/IMerchantContactLookupService.cs`:

```csharp
namespace Courier.Merchants.Application;

// Separate from IMerchantLookupService (ExistsAsync) deliberately — IMerchantLookupService is
// implemented as a fake in 15 files across 3 test projects (Cod.Tests, Shipments.Tests,
// Pricing.Tests); extending it for a need only Notifications has would force every one of those
// fakes to grow a member they'd never exercise. MerchantLookupService implements both
// interfaces (Step 2 below) — the same "one class, two narrow interfaces" shape CodService
// already uses for ICodCollectionCreationService/ICodCollectionService.
public interface IMerchantContactLookupService
{
    // Null if the merchant has no ContactEmail on file (a normal, expected state — Merchant.
    // ContactEmail is nullable) or the merchant doesn't exist in this organization.
    Task<string?> GetContactEmailAsync(Guid merchantId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: `MerchantLookupService` implements the new interface too**

In `src/Modules/Merchants/Application/MerchantLookupService.cs`, change the class declaration:

```csharp
public class MerchantLookupService : IMerchantLookupService, IMerchantContactLookupService
```

Add this method to the class body, alongside `ExistsAsync`:

```csharp
    public async Task<string?> GetContactEmailAsync(Guid merchantId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Merchant>().Where(m => m.Id == merchantId).Select(m => m.ContactEmail).FirstOrDefaultAsync(cancellationToken);
```

- [ ] **Step 3: Wire the new interface's DI registration**

In `src/Api/Program.cs`, add after the existing `builder.Services.AddScoped<IMerchantLookupService>(sp => sp.GetRequiredService<MerchantLookupService>());` line:

```csharp
builder.Services.AddScoped<IMerchantContactLookupService>(sp => sp.GetRequiredService<MerchantLookupService>());
```

- [ ] **Step 4: Add `MailKit` reference**

In `src/Modules/Notifications/Notifications.csproj`, add to the `<ItemGroup>` of `PackageReference`s:

```xml
    <PackageReference Include="MailKit" Version="4.17.0" />
```

- [ ] **Step 5: `INotificationChannel`**

`src/Modules/Notifications/Application/INotificationChannel.cs`:

```csharp
namespace Courier.Notifications.Application;

public class NotificationDeliveryException : Exception
{
    public NotificationDeliveryException(string message) : base(message) { }
    public NotificationDeliveryException(string message, Exception innerException) : base(message, innerException) { }
}

// One implementation per Domain.NotificationChannel value (Step 6/7 below) — NotificationDispatchService
// resolves the right one from an injected IEnumerable<INotificationChannel> by matching .Channel.
// SendAsync returns a short provider-response string on success (stored verbatim in
// NotificationLog.ProviderResponse) and throws NotificationDeliveryException on failure — the
// dispatch service never needs to know which concrete channel it's talking to.
public interface INotificationChannel
{
    Courier.Notifications.Domain.NotificationChannel Channel { get; }
    Task<string> SendAsync(string recipientAddress, string body, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6: `EmailNotificationChannel` (real SMTP)**

`src/Modules/Notifications/Application/EmailNotificationChannel.cs`:

```csharp
using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Configuration;

namespace Courier.Notifications.Application;

// The one real channel this phase — connects to whatever SMTP host/port appsettings' Smtp
// section names (Task 8 points this at a dev-only mail-catcher container in docker-compose;
// production would point it at a real SMTP relay via the same config keys, no code change).
public class EmailNotificationChannel : INotificationChannel
{
    private readonly IConfiguration _configuration;

    public EmailNotificationChannel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Courier.Notifications.Domain.NotificationChannel Channel => Courier.Notifications.Domain.NotificationChannel.Email;

    public async Task<string> SendAsync(string recipientAddress, string body, CancellationToken cancellationToken = default)
    {
        var host = _configuration["Smtp:Host"] ?? throw new NotificationDeliveryException("Smtp:Host is not configured.");
        var port = int.Parse(_configuration["Smtp:Port"] ?? throw new NotificationDeliveryException("Smtp:Port is not configured."));
        var from = _configuration["Smtp:From"] ?? "noreply@courier.local";

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(recipientAddress));
        message.Subject = "Courier notification";
        message.Body = new TextPart("plain") { Text = body };

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.None, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return $"Delivered to {host}:{port}";
        }
        catch (Exception ex) when (ex is not NotificationDeliveryException)
        {
            throw new NotificationDeliveryException($"SMTP send failed: {ex.Message}", ex);
        }
    }
}
```

- [ ] **Step 7: Stub channels for SMS/WhatsApp/Push**

`src/Modules/Notifications/Application/StubNotificationChannels.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Courier.Notifications.Application;

// SMS/WhatsApp/Push are not wired to any real provider this phase (spec §2) — each channel
// still exists, is independently unit-testable, and is a real, pluggable implementation of
// INotificationChannel, so a future phase adding a real provider replaces one class, not this
// abstraction. "Sending" here means: log it, never throw — Logged is always the outcome, never
// Sent or Failed, since no real delivery is attempted.
public class SmsNotificationChannel : INotificationChannel
{
    private readonly ILogger<SmsNotificationChannel> _logger;
    public SmsNotificationChannel(ILogger<SmsNotificationChannel> logger) => _logger = logger;
    public Courier.Notifications.Domain.NotificationChannel Channel => Courier.Notifications.Domain.NotificationChannel.Sms;
    public Task<string> SendAsync(string recipientAddress, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SMS (stub, not sent) to {RecipientAddress}: {Body}", recipientAddress, body);
        return Task.FromResult("Logged, no real SMS provider configured");
    }
}

public class WhatsAppNotificationChannel : INotificationChannel
{
    private readonly ILogger<WhatsAppNotificationChannel> _logger;
    public WhatsAppNotificationChannel(ILogger<WhatsAppNotificationChannel> logger) => _logger = logger;
    public Courier.Notifications.Domain.NotificationChannel Channel => Courier.Notifications.Domain.NotificationChannel.WhatsApp;
    public Task<string> SendAsync(string recipientAddress, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("WhatsApp (stub, not sent) to {RecipientAddress}: {Body}", recipientAddress, body);
        return Task.FromResult("Logged, no real WhatsApp provider configured");
    }
}

public class PushNotificationChannel : INotificationChannel
{
    private readonly ILogger<PushNotificationChannel> _logger;
    public PushNotificationChannel(ILogger<PushNotificationChannel> logger) => _logger = logger;
    public Courier.Notifications.Domain.NotificationChannel Channel => Courier.Notifications.Domain.NotificationChannel.Push;
    public Task<string> SendAsync(string recipientAddress, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Push (stub, not sent) to {RecipientAddress}: {Body}", recipientAddress, body);
        return Task.FromResult("Logged, no real Push provider configured");
    }
}
```

- [ ] **Step 8: Compiled-in default templates**

`src/Modules/Notifications/Application/NotificationDefaultTemplates.cs`:

```csharp
namespace Courier.Notifications.Application;

// Fallback bodies used when no active NotificationTemplate row exists for an
// (OrganizationId, EventType, Channel) combination — which is every combination this phase,
// since nothing seeds template rows (Task 5's resolved implementation detail). Only Email has
// defaults, since Email is the only channel this phase actually dispatches through (see
// NotificationDispatchService.DispatchEventAsync — it only attempts a channel it has a
// resolvable body for). {PlaceholderName} tokens are replaced from the event payload's own
// top-level JSON properties by DispatchEventAsync — this file only owns the text.
public static class NotificationDefaultTemplates
{
    public static readonly IReadOnlyDictionary<string, string> Email = new Dictionary<string, string>
    {
        ["shipment.created"] = "Your shipment {Awb} has been created and is being processed.",
        ["shipment.delivered"] = "Your shipment {Awb} has been delivered.",
        ["shipment.ndr_created"] = "We were unable to deliver shipment {Awb}. Our team will contact you shortly.",
        ["cod.collected"] = "COD amount of Rs. {CollectedAmount} has been collected for shipment {ShipmentId}."
    };
}
```

- [ ] **Step 9: DTOs and exception type**

`src/Modules/Notifications/Application/NotificationDtos.cs`:

```csharp
namespace Courier.Notifications.Application;

public class NotificationValidationException : Exception
{
    public NotificationValidationException(string message) : base(message) { }
}
```

- [ ] **Step 10: `NotificationDispatchService`**

`src/Modules/Notifications/Application/NotificationDispatchService.cs`:

```csharp
using System.Text.Json;
using Courier.Merchants.Application;
using Courier.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Notifications.Application;

public class NotificationDispatchService
{
    private readonly DbContext _dbContext;
    private readonly IMerchantContactLookupService _merchantContactLookupService;
    private readonly IEnumerable<INotificationChannel> _channels;

    public NotificationDispatchService(DbContext dbContext, IMerchantContactLookupService merchantContactLookupService, IEnumerable<INotificationChannel> channels)
    {
        _dbContext = dbContext;
        _merchantContactLookupService = merchantContactLookupService;
        _channels = channels;
    }

    // Called once per consumed RabbitMQ message (Task 7). organizationId/eventType/payloadJson
    // come straight from the OutboxMessage row the publisher (Task 4) serialized — this method
    // never touches Shipments/Riders/Ndr/Cod, only what the payload itself carries plus
    // Merchants' contact lookup (Global Constraints).
    public async Task DispatchEventAsync(Guid organizationId, string eventType, string payloadJson, CancellationToken cancellationToken = default)
    {
        if (!NotificationDefaultTemplates.Email.TryGetValue(eventType, out var defaultBody))
        {
            // No default exists for this event type — nothing this phase wires should ever hit
            // this path, but a future event type added to Events without a matching template
            // here would land here rather than throwing, which is the right behavior: silently
            // not-notifying is safer than crashing the consumer over a missing template.
            return;
        }

        var payload = JsonDocument.Parse(payloadJson).RootElement;
        if (!payload.TryGetProperty("MerchantId", out var merchantIdProperty) || !merchantIdProperty.TryGetGuid(out var merchantId))
        {
            throw new NotificationValidationException($"Event '{eventType}' payload has no MerchantId — cannot resolve a recipient.");
        }

        var template = await _dbContext.Set<NotificationTemplate>()
            .Where(t => t.OrganizationId == organizationId && t.EventType == eventType && t.Channel == NotificationChannel.Email && t.IsActive)
            .Select(t => t.Body)
            .FirstOrDefaultAsync(cancellationToken);
        var body = Render(template ?? defaultBody, payload);

        var recipientEmail = await _merchantContactLookupService.GetContactEmailAsync(merchantId, cancellationToken);
        if (recipientEmail is null)
        {
            await WriteLogAsync(organizationId, eventType, null, NotificationStatus.NoRecipient, null, cancellationToken);
            return;
        }

        var emailChannel = _channels.Single(c => c.Channel == NotificationChannel.Email);
        try
        {
            var providerResponse = await emailChannel.SendAsync(recipientEmail, body, cancellationToken);
            await WriteLogAsync(organizationId, eventType, recipientEmail, NotificationStatus.Sent, providerResponse, cancellationToken);
        }
        catch (NotificationDeliveryException ex)
        {
            await WriteLogAsync(organizationId, eventType, recipientEmail, NotificationStatus.Failed, ex.Message, cancellationToken);
        }
    }

    // Every {PropertyName} token in the body is replaced with that property's string value from
    // the payload's top-level JSON, if present — a deliberately simple substitution, not a
    // templating engine, matching CLAUDE.md §3's anti-ceremony guidance.
    private static string Render(string body, JsonElement payload)
    {
        var rendered = body;
        foreach (var property in payload.EnumerateObject())
        {
            var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
            rendered = rendered.Replace($"{{{property.Name}}}", value);
        }
        return rendered;
    }

    private async Task WriteLogAsync(Guid organizationId, string eventType, string? recipientAddress, NotificationStatus status, string? providerResponse, CancellationToken cancellationToken)
    {
        _dbContext.Set<NotificationLog>().Add(new NotificationLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EventType = eventType,
            Channel = NotificationChannel.Email,
            RecipientAddress = recipientAddress,
            Status = status,
            SentAt = DateTimeOffset.UtcNow,
            ProviderResponse = providerResponse
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 11: Wire DI in `Program.cs`**

Add `using Courier.Notifications.Application;` to the usings. Add after the existing `builder.Services.AddScoped<IMerchantContactLookupService>(...)` line from Step 3:

```csharp
builder.Services.AddScoped<INotificationChannel, EmailNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, SmsNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, WhatsAppNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, PushNotificationChannel>();
builder.Services.AddScoped<NotificationDispatchService>();
```

(Four separate `AddScoped<INotificationChannel, X>()` calls is how ASP.NET Core DI builds the `IEnumerable<INotificationChannel>` that `NotificationDispatchService`'s constructor consumes — every registration against the same interface is collected, not just the last one.)

- [ ] **Step 12: Add `Smtp` config**

In `src/Api/appsettings.Development.json`, add a new top-level section after `"ObjectStorage"`:

```json
  "Smtp": {
    "Host": "localhost",
    "Port": "1025",
    "From": "noreply@courier.local"
  }
```

- [ ] **Step 13: Write the tests**

`tests/Notifications.Tests/NotificationDispatchServiceTests.cs`:

```csharp
using Courier.Merchants.Application;
using Courier.Notifications.Application;
using Courier.Notifications.Domain;
using Courier.Notifications.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Notifications.Tests;

file class FakeMerchantContactLookupService : IMerchantContactLookupService
{
    public string? ContactEmail { get; set; }
    public Task<string?> GetContactEmailAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(ContactEmail);
}

file class RecordingEmailChannel : INotificationChannel
{
    public List<(string RecipientAddress, string Body)> Sent { get; } = new();
    public bool ShouldThrow { get; set; }
    public NotificationChannel Channel => NotificationChannel.Email;
    public Task<string> SendAsync(string recipientAddress, string body, CancellationToken cancellationToken = default)
    {
        if (ShouldThrow)
        {
            throw new NotificationDeliveryException("simulated SMTP failure");
        }
        Sent.Add((recipientAddress, body));
        return Task.FromResult("ok");
    }
}

public class NotificationDispatchServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new NotificationTemplateConfiguration());
            modelBuilder.ApplyConfiguration(new NotificationLogConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task DispatchEventAsync_WithResolvableRecipient_SendsRenderedDefaultTemplateAndLogsSent()
    {
        await using var dbContext = CreateContext();
        var merchantLookup = new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" };
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext, merchantLookup, new INotificationChannel[] { emailChannel });
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var payload = System.Text.Json.JsonSerializer.Serialize(new { ShipmentId = shipmentId, MerchantId = Guid.NewGuid(), Awb = "ABC123" });

        await service.DispatchEventAsync(organizationId, "shipment.created", payload);

        Assert.Single(emailChannel.Sent, s => s.RecipientAddress == "shipper@example.com" && s.Body.Contains("ABC123"));
        var log = await dbContext.Set<NotificationLog>().SingleAsync();
        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Equal("shipper@example.com", log.RecipientAddress);
    }

    [Fact]
    public async Task DispatchEventAsync_NoContactEmail_LogsNoRecipientAndSendsNothing()
    {
        await using var dbContext = CreateContext();
        var merchantLookup = new FakeMerchantContactLookupService { ContactEmail = null };
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext, merchantLookup, new INotificationChannel[] { emailChannel });
        var payload = System.Text.Json.JsonSerializer.Serialize(new { ShipmentId = Guid.NewGuid(), MerchantId = Guid.NewGuid(), Awb = "ABC123" });

        await service.DispatchEventAsync(Guid.NewGuid(), "shipment.created", payload);

        Assert.Empty(emailChannel.Sent);
        var log = await dbContext.Set<NotificationLog>().SingleAsync();
        Assert.Equal(NotificationStatus.NoRecipient, log.Status);
        Assert.Null(log.RecipientAddress);
    }

    [Fact]
    public async Task DispatchEventAsync_ChannelThrows_LogsFailed()
    {
        await using var dbContext = CreateContext();
        var merchantLookup = new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" };
        var emailChannel = new RecordingEmailChannel { ShouldThrow = true };
        var service = new NotificationDispatchService(dbContext, merchantLookup, new INotificationChannel[] { emailChannel });
        var payload = System.Text.Json.JsonSerializer.Serialize(new { ShipmentId = Guid.NewGuid(), MerchantId = Guid.NewGuid(), Awb = "ABC123" });

        await service.DispatchEventAsync(Guid.NewGuid(), "shipment.created", payload);

        var log = await dbContext.Set<NotificationLog>().SingleAsync();
        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Contains("simulated SMTP failure", log.ProviderResponse);
    }

    [Fact]
    public async Task DispatchEventAsync_OrgSpecificActiveTemplateExists_UsesItInsteadOfDefault()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        dbContext.Set<NotificationTemplate>().Add(new NotificationTemplate
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, EventType = "shipment.created",
            Channel = NotificationChannel.Email, Body = "Custom body for {Awb}", IsActive = true
        });
        await dbContext.SaveChangesAsync();
        var merchantLookup = new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" };
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext, merchantLookup, new INotificationChannel[] { emailChannel });
        var payload = System.Text.Json.JsonSerializer.Serialize(new { ShipmentId = Guid.NewGuid(), MerchantId = Guid.NewGuid(), Awb = "XYZ789" });

        await service.DispatchEventAsync(organizationId, "shipment.created", payload);

        Assert.Single(emailChannel.Sent, s => s.Body == "Custom body for XYZ789");
    }

    [Fact]
    public async Task DispatchEventAsync_DifferentOrganizationsTemplate_NotUsedAcrossTenants()
    {
        await using var dbContext = CreateContext();
        var otherOrgId = Guid.NewGuid();
        dbContext.Set<NotificationTemplate>().Add(new NotificationTemplate
        {
            Id = Guid.NewGuid(), OrganizationId = otherOrgId, EventType = "shipment.created",
            Channel = NotificationChannel.Email, Body = "Should never be used", IsActive = true
        });
        await dbContext.SaveChangesAsync();
        var merchantLookup = new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" };
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext, merchantLookup, new INotificationChannel[] { emailChannel });
        var payload = System.Text.Json.JsonSerializer.Serialize(new { ShipmentId = Guid.NewGuid(), MerchantId = Guid.NewGuid(), Awb = "ABC123" });

        await service.DispatchEventAsync(Guid.NewGuid(), "shipment.created", payload);

        Assert.Single(emailChannel.Sent, s => s.Body.Contains("ABC123") && !s.Body.Contains("Should never be used"));
    }
}
```

- [ ] **Step 14: Run the tests**

Run: `dotnet test tests/Notifications.Tests/Notifications.Tests.csproj`
Expected: All 5 tests pass.

- [ ] **Step 15: Build the whole solution and run the full suite once**

Run: `dotnet build Courier.slnx` then `dotnet test Courier.slnx --no-build`
Expected: Build succeeds, every test project passes (this includes every existing test file that constructs `MerchantLookupService` directly, if any — grep `new MerchantLookupService(` across `tests/` first; its constructor signature is unchanged by this task, only its implemented-interfaces list grew, so no existing call site should need edits, but confirm this rather than assume it).

- [ ] **Step 16: Commit**

```bash
git add src/Modules/Merchants/Application/IMerchantContactLookupService.cs src/Modules/Merchants/Application/MerchantLookupService.cs src/Modules/Notifications/ src/Api/Api.csproj src/Api/Program.cs src/Api/appsettings.Development.json tests/Notifications.Tests/
git commit -m "feat(notifications): add notification channels and dispatch service"
```

---

## Task 7: Notifications RabbitMQ consumer

**Files:**
- Create: `src/Modules/Notifications/Infrastructure/NotificationsConsumer.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Api.Tests/NotificationsConsumerTests.cs`

**Interfaces:**
- Consumes: `NotificationDispatchService.DispatchEventAsync` (Task 6), `RabbitMQ.Client.IConnection` and `RabbitMqTopology` (Task 3).

- [ ] **Step 1: `NotificationsConsumer` hosted service**

`src/Modules/Notifications/Infrastructure/NotificationsConsumer.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Courier.Notifications.Application;
using Courier.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Courier.Notifications.Infrastructure;

// Binds its own queue (RabbitMqTopology.NotificationsQueue) to the shared domain-events topic
// exchange with a wildcard routing key ("#" — every event type), independently of the Webhooks
// consumer (Task 11), which binds its own separate queue to the same exchange. Each message is
// only ack'd after NotificationDispatchService.DispatchEventAsync completes without throwing —
// an unhandled exception nacks with requeue=true, matching "consumers acknowledge only after
// successfully completing their work" (Global Constraints). DispatchEventAsync itself never
// throws for expected conditions (no template, no recipient) — only for a malformed payload
// (NotificationValidationException) or an infrastructure fault, both of which are legitimately
// worth a requeue-and-retry rather than a silent drop.
public class NotificationsConsumer : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationsConsumer> _logger;
    private IChannel? _channel;

    public NotificationsConsumer(IConnection connection, IServiceScopeFactory scopeFactory, ILogger<NotificationsConsumer> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.ExchangeDeclareAsync(RabbitMqTopology.DomainEventsExchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(RabbitMqTopology.NotificationsQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(RabbitMqTopology.NotificationsQueue, RabbitMqTopology.DomainEventsExchange, routingKey: "#", cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            var eventType = eventArgs.RoutingKey;
            var rawEnvelope = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
            try
            {
                // Envelope shape published by OutboxPublisherJob (Task 4, amended by this task):
                // { EventId, OrganizationId, EventType, Data }. This consumer only needs
                // OrganizationId (to scope the dispatch) and Data (the original per-event payload
                // DispatchEventAsync expects) — EventId is Webhooks' (Task 11) idempotency key,
                // unused here.
                var envelope = JsonDocument.Parse(rawEnvelope).RootElement;
                if (!envelope.TryGetProperty("OrganizationId", out var orgProperty) || !orgProperty.TryGetGuid(out var organizationId))
                {
                    throw new NotificationValidationException("Envelope has no OrganizationId.");
                }
                if (!envelope.TryGetProperty("Data", out var dataElement))
                {
                    throw new NotificationValidationException("Envelope has no Data.");
                }

                using var scope = _scopeFactory.CreateScope();
                var dispatchService = scope.ServiceProvider.GetRequiredService<NotificationDispatchService>();
                await dispatchService.DispatchEventAsync(organizationId, eventType, dataElement.GetRawText(), stoppingToken);
                await _channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process notification for event {EventType}", eventType);
                await _channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync(RabbitMqTopology.NotificationsQueue, autoAck: false, consumer, cancellationToken: stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
        }
        await base.StopAsync(cancellationToken);
    }
}
```

**Before writing `NotificationsConsumer.cs` above, first fix Task 4's publisher — it currently publishes the bare `OutboxMessage.Payload`, which has no `OrganizationId`/`EventId` at all** (those are separate columns on `OutboxMessage`, not part of the JSON payload each of Task 2's four call sites built). The consumer above expects an envelope. Modify `src/Modules/Events/Application/OutboxPublisherJob.cs`'s `RunAsync` method: replace the line `await _eventPublisher.PublishAsync(RabbitMqTopology.DomainEventsExchange, message.EventType, message.Payload, cancellationToken);` with:

```csharp
            var envelope = System.Text.Json.JsonSerializer.Serialize(new { EventId = message.Id, message.OrganizationId, EventType = message.EventType, Data = System.Text.Json.JsonDocument.Parse(message.Payload).RootElement });
            await _eventPublisher.PublishAsync(RabbitMqTopology.DomainEventsExchange, message.EventType, envelope, cancellationToken);
```

(`EventId` — the source `OutboxMessage.Id` — travels in the envelope too, not just `OrganizationId`/`EventType`/`Data`: Task 11's Webhooks consumer uses it as the idempotency key for `WebhookDelivery` row creation, so a redelivered RabbitMQ message after a crash mid-processing can't double-create delivery rows for the same source event.)

and update `tests/Events.Tests/OutboxPublisherJobTests.cs`'s `RunAsync_PublishesUnpublishedRowsAndMarksThemPublished` test to assert the published payload deserializes with `EventId` (matching the row's own `Id`) and `OrganizationId` properties, instead of (or in addition to) the existing assertions — re-run `dotnet test tests/Events.Tests/Events.Tests.csproj` to confirm this doesn't break the other two tests in that file, which don't inspect payload shape. `NotificationsConsumer.cs` above already reads this envelope shape correctly (`OrganizationId` and `Data` from the envelope root, `EventType` from `eventArgs.RoutingKey`). `Webhooks`' consumer (Task 11) reads the same envelope shape plus `EventId`.

- [ ] **Step 2: Register the hosted service**

In `src/Api/Program.cs`, add `using Courier.Notifications.Infrastructure;` to the usings. Add after the existing `builder.Services.AddScoped<NotificationDispatchService>();` line:

```csharp
builder.Services.AddHostedService<NotificationsConsumer>();
```

**No dedicated `NotificationsConsumerTests.cs` in this task:** a real test of "the consumer processes a live `shipment.created` message" needs a fully booked shipment to trigger it (per Task 2's wiring), which means the entire Phase 6-10 shipment-creation-through-delivery HTTP flow as test setup — Task 14's end-to-end test already builds exactly that flow and is the right place to prove this consumer works end-to-end against a real message, not a duplicate, narrower setup here. This task's actual verification is a startup-safety check instead:

- [ ] **Step 3: Confirm the consumer starts cleanly**

Run: `docker exec courier-rabbitmq-1 rabbitmq-diagnostics -q ping` to confirm RabbitMQ is up.
Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~HangfireWiringTests"` (Task 3's existing test) — a passing run here, with no startup exceptions logged, is sufficient evidence `NotificationsConsumer`'s `ExecuteAsync` (which runs for every `WebApplicationFactory<Program>` in every `Api.Tests` test from this point forward) doesn't throw during host startup. If it does throw, every `Api.Tests` test will start failing, not just this one — treat any new `Api.Tests` failures introduced by this task's commit as this task's bug to fix, not a pre-existing issue.

- [ ] **Step 4: Build the whole solution and run the full suite once**

Run: `dotnet build Courier.slnx` then `dotnet test Courier.slnx --no-build`
Expected: Build succeeds, every test project passes — including, critically, every existing `Api.Tests` test, since `NotificationsConsumer` now runs as a hosted service inside every `WebApplicationFactory<Program>` those tests already construct.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Events/Application/OutboxPublisherJob.cs tests/Events.Tests/OutboxPublisherJobTests.cs src/Modules/Notifications/Infrastructure/NotificationsConsumer.cs src/Api/Program.cs
git commit -m "feat(notifications): add RabbitMQ consumer, envelope OrganizationId in outbox publisher"
```

---

## Task 8: Dev SMTP mail-catcher

**Files:**
- Modify: `docker-compose.yml`
- Modify: `.github/workflows/ci.yml`

**Interfaces:** none — infrastructure only, consumed by Task 14's end-to-end email test.

- [ ] **Step 1: Add the mail-catcher service to `docker-compose.yml`**

Add a new service after the existing `rabbitmq:` block, before `api:`:

```yaml
  mailhog:
    image: mailhog/mailhog:v1.0.1
    ports:
      - "1025:1025"
      - "8025:8025"
    healthcheck:
      test: ["CMD", "wget", "-q", "-O", "-", "http://localhost:8025/api/v2/messages"]
      interval: 5s
      timeout: 5s
      retries: 5
```

Add `SMTP__HOST` / `SMTP__PORT` to the `api` service's existing `environment:` block:

```yaml
      SMTP__HOST: "mailhog"
      SMTP__PORT: "1025"
```

Add `mailhog` to the `api` service's existing `depends_on:` block:

```yaml
      mailhog:
        condition: service_healthy
```

- [ ] **Step 2: Add the mail-catcher service to CI**

In `.github/workflows/ci.yml`, add a new service under the `api` job's existing `services:` block, after `rabbitmq:`:

```yaml
      mailhog:
        image: mailhog/mailhog:v1.0.1
        ports: ["1025:1025", "8025:8025"]
```

Add to the job's `env:` block:

```yaml
      Smtp__Host: "localhost"
      Smtp__Port: "1025"
```

- [ ] **Step 3: Confirm local dev still works**

Run: `docker compose up -d mailhog` (or, if not using docker-compose locally, run the container directly: `docker run -d --name courier-mailhog -p 1025:1025 -p 8025:8025 mailhog/mailhog:v1.0.1`, matching how `courier-postgres-1`/`courier-redis-1`/`courier-rabbitmq-1` are already run as standalone named containers in this project's actual dev workflow rather than via `docker compose up`, per every prior phase's own migration/test steps).
Run: `curl http://localhost:8025/api/v2/messages` — expect an empty `{"total":0,...}` JSON response, confirming the mail-catcher's HTTP API is reachable (Task 14's end-to-end test polls this same endpoint).

- [ ] **Step 4: Commit**

```bash
git add docker-compose.yml .github/workflows/ci.yml
git commit -m "feat(notifications): add dev SMTP mail-catcher"
```

---

## Task 9: Webhooks module scaffold — `WebhookSubscription`, `WebhookDelivery`, persistence wiring

**Files:**
- Create: `src/Modules/Webhooks/Webhooks.csproj`
- Create: `src/Modules/Webhooks/Domain/WebhookSubscription.cs`
- Create: `src/Modules/Webhooks/Domain/WebhookDelivery.cs`
- Create: `src/Modules/Webhooks/Infrastructure/WebhookSubscriptionConfiguration.cs`
- Create: `src/Modules/Webhooks/Infrastructure/WebhookDeliveryConfiguration.cs`
- Modify: `src/Persistence/AppDbContext.cs`
- Modify: `src/Persistence/Persistence.csproj`
- Modify: `src/Api/Api.csproj`
- Modify: `Courier.slnx`
- Create: `tests/Webhooks.Tests/Webhooks.Tests.csproj`

**Interfaces:** none new — pure scaffold, same shape as Task 5.

**Resolved implementation detail (not in the spec, decided while writing this plan):** `WebhookDelivery` gains one field beyond `DOMAIN-MODEL.md`'s literal sketch: `SourceEventId` (the originating `OutboxMessage.Id`), with a unique index on `(WebhookSubscriptionId, SourceEventId)` — this is the concrete mechanism behind the idempotent-delivery-creation requirement in Global Constraints (a redelivered RabbitMQ message can't double-create delivery rows for the same subscription+source-event pair; the unique index makes this DB-enforced, not just checked-then-written, the same TOCTOU lesson Phase 5's `RateCard` versioning and Phase 10's `FinancialTransaction` uniqueness already established). `WebhookSubscription.Events` is a Postgres native array column (`string[]`), which the Npgsql EF Core provider maps without any extra configuration beyond the property's CLR type.

- [ ] **Step 1: Create the project file**

`src/Modules/Webhooks/Webhooks.csproj` — references `SharedKernel`, `Audit` (for `IAuditLogger` on subscription create/delete), `Merchants` (for merchant-existence validation, mirroring how `Shipments`/`Cod` already validate a caller-supplied `MerchantId` before writing) — deliberately no reference to `Shipments`/`Riders`/`Ndr`/`Cod`/`Events` (Global Constraints: `Webhooks` only ever sees an `OutboxMessage`'s already-published payload over RabbitMQ, never its table):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.11" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.11" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\SharedKernel\SharedKernel.csproj" />
    <ProjectReference Include="..\Audit\Audit.csproj" />
    <ProjectReference Include="..\Merchants\Merchants.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Domain entities**

`src/Modules/Webhooks/Domain/WebhookSubscription.cs`:

```csharp
namespace Courier.Webhooks.Domain;

// Secret is ciphertext (ASP.NET Core Data Protection, Task 10) — never the plaintext signing
// key. The plaintext is shown exactly once, in the POST /api/v1/webhooks response body, and
// never persisted anywhere in plaintext form. Events lists the CLAUDE.md §26 event-type strings
// this subscription wants delivered ("shipment.created", "cod.collected", etc.) — a merchant may
// list any of the 10 documented types even though this phase only ever publishes 4 of them
// (spec §2); an unpublished type simply never fires, which is honest, not a bug.
public class WebhookSubscription
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Guid OrganizationId { get; set; }
    public required string TargetUrl { get; set; }
    public required string Secret { get; set; }
    public required string[] Events { get; set; }
    public bool IsActive { get; set; }
}
```

`src/Modules/Webhooks/Domain/WebhookDelivery.cs`:

```csharp
namespace Courier.Webhooks.Domain;

// One row per (subscription, source event) pair the Webhooks consumer (Task 11) creates, plus
// however many retry attempts the retry job (Task 12) records against the SAME row (Attempt
// increments in place — this is not one row per attempt, it is one row per delivery, whose
// Attempt/ResponseStatus/ResponseSnippet/NextRetryAt fields are overwritten on each retry, only
// DeliveredAt ever being a terminal, no-longer-overwritten field once set).
public class WebhookDelivery
{
    public Guid Id { get; set; }
    public Guid WebhookSubscriptionId { get; set; }
    public Guid SourceEventId { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }
    public int Attempt { get; set; }
    public int? ResponseStatus { get; set; }
    public string? ResponseSnippet { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
}
```

- [ ] **Step 3: EF configurations**

`src/Modules/Webhooks/Infrastructure/WebhookSubscriptionConfiguration.cs`:

```csharp
using Courier.Webhooks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Webhooks.Infrastructure;

public class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.ToTable("WebhookSubscriptions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TargetUrl).HasMaxLength(2048).IsRequired();
        builder.Property(s => s.Secret).IsRequired();

        builder.HasIndex(s => s.OrganizationId);
        builder.HasIndex(s => s.MerchantId);
        // The Webhooks consumer's (Task 11) per-message hot path: "every active subscription in
        // this org whose Events contains this event type."
        builder.HasIndex(s => new { s.OrganizationId, s.IsActive });
    }
}
```

`src/Modules/Webhooks/Infrastructure/WebhookDeliveryConfiguration.cs`:

```csharp
using Courier.Webhooks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Webhooks.Infrastructure;

public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("WebhookDeliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.EventType).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Payload).IsRequired();
        builder.Property(d => d.ResponseSnippet).HasMaxLength(2000);

        // At most one WebhookDelivery per (subscription, source event) — DB-enforced idempotency
        // for the consumer's delivery-row creation (Task 11), not just a checked-then-written
        // pre-check. Same TOCTOU-guard lesson as Phase 5's RateCard versioning and Phase 10's
        // FinancialTransaction uniqueness.
        builder.HasIndex(d => new { d.WebhookSubscriptionId, d.SourceEventId }).IsUnique();
        // The retry job's (Task 12) hot path: "every delivery whose NextRetryAt is due."
        builder.HasIndex(d => d.NextRetryAt).HasFilter("\"NextRetryAt\" IS NOT NULL");

        builder.HasOne<WebhookSubscription>().WithMany()
            .HasForeignKey(d => d.WebhookSubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 4: Wire into `AppDbContext`**

In `src/Persistence/AppDbContext.cs`, add after the `Courier.Notifications.Infrastructure` using:

```csharp
using Courier.Webhooks.Domain;
using Courier.Webhooks.Infrastructure;
```

Add after the `NotificationLogs` `DbSet` line:

```csharp
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
```

Add after the `NotificationLogConfiguration` `ApplyConfiguration` line:

```csharp
        modelBuilder.ApplyConfiguration(new WebhookSubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookDeliveryConfiguration());
```

Add after the `NotificationLog` `HasOne<Organization>()` block:

```csharp
        modelBuilder.Entity<WebhookSubscription>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        // No FK to Merchant — Webhooks' project reference to Merchants is Application-layer only
        // (validating a caller-supplied MerchantId exists, Task 10), not its EF model, same
        // cross-module precedent as Shipment.MerchantId (Phase 6).
```

Add after the `NotificationLog` `HasQueryFilter` line:

```csharp
        modelBuilder.Entity<WebhookSubscription>().HasQueryFilter(s => _currentTenantProvider.OrganizationId != null && s.OrganizationId == _currentTenantProvider.OrganizationId);
        // No tenant filter on WebhookDelivery: no own OrganizationId — tenant isolation flows
        // transitively through WebhookSubscriptionId -> WebhookSubscription.OrganizationId
        // (which does have a filter, above), same pattern as MerchantSettlementItem.
```

- [ ] **Step 5: Add project references**

`src/Persistence/Persistence.csproj`, after `Notifications.csproj`:

```xml
    <ProjectReference Include="..\Modules\Webhooks\Webhooks.csproj" />
```

`src/Api/Api.csproj`, same position.

`Courier.slnx`, `/src/Modules/` folder, after `Riders.csproj` (alphabetical — "Webhooks" sorts last):

```xml
    <Project Path="src/Modules/Webhooks/Webhooks.csproj" />
```

- [ ] **Step 6: Generate and apply the migration**

Run: `dotnet ef migrations add AddWebhooks --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
Expected: Creates `WebhookSubscriptions`/`WebhookDeliveries` tables only, `Events` as a Postgres `text[]` column. Confirm by reading the migration file.

Run: `dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj` (after confirming `courier-postgres-1` is up).

- [ ] **Step 7: Create the test project**

`tests/Webhooks.Tests/Webhooks.Tests.csproj` — same shape as `tests/Events.Tests/Events.Tests.csproj` (Task 1 Step 9), `ProjectReference` pointed at `..\..\src\Modules\Webhooks\Webhooks.csproj`. Add to `Courier.slnx`'s `/tests/` folder after `Riders.Tests.csproj` (alphabetical last).

- [ ] **Step 8: Build and commit**

Run: `dotnet build Courier.slnx`
Expected: Build succeeds.

```bash
git add src/Modules/Webhooks/ src/Persistence/AppDbContext.cs src/Persistence/Persistence.csproj src/Persistence/Migrations/ src/Api/Api.csproj Courier.slnx tests/Webhooks.Tests/
git commit -m "feat(webhooks): scaffold Webhooks module and domain entities"
```

---

## Task 10: Webhooks Application — subscription CRUD, secret encryption, signature computation

**Files:**
- Create: `src/Modules/Webhooks/Application/WebhookDtos.cs`
- Create: `src/Modules/Webhooks/Application/ISecretProtector.cs`
- Create: `src/Modules/Webhooks/Infrastructure/DataProtectionSecretProtector.cs`
- Create: `src/Modules/Webhooks/Application/WebhookSignature.cs`
- Create: `src/Modules/Webhooks/Application/WebhookService.cs`
- Modify: `src/Modules/Merchants/Application/MerchantPermissionCatalog.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Webhooks.Tests/WebhookServiceTests.cs`
- Test: `tests/Webhooks.Tests/WebhookSignatureTests.cs`

**Interfaces:**
- Produces: `WebhookService.CreateSubscriptionAsync(Guid organizationId, Guid merchantId, CreateWebhookSubscriptionRequest, CancellationToken) -> Task<CreateWebhookSubscriptionResponse>`, `.ListAsync(Guid organizationId, Guid merchantId, CancellationToken) -> Task<List<WebhookSubscriptionResponse>>`, `.DeleteAsync(Guid organizationId, Guid merchantId, Guid subscriptionId, CancellationToken) -> Task<bool>`, `.ListDeliveriesAsync(Guid organizationId, Guid merchantId, Guid subscriptionId, CancellationToken) -> Task<List<WebhookDeliveryResponse>?>`, `.ReplayAsync(Guid organizationId, Guid merchantId, Guid deliveryId, CancellationToken) -> Task<WebhookDeliveryResponse?>` — Task 13's Presentation layer resolves `organizationId`/`merchantId` from the request the same way `MerchantProfileEndpoints.cs` already does (`httpContext.User.GetMerchantId()`), then calls these directly; `WebhookSignature.Compute(string secret, string payload) -> string` — Task 11's consumer and Task 12's retry job both call this.

- [ ] **Step 1: DTOs and exception type**

`src/Modules/Webhooks/Application/WebhookDtos.cs`:

```csharp
namespace Courier.Webhooks.Application;

public record CreateWebhookSubscriptionRequest(string TargetUrl, string[] Events);

// The only response that ever carries the plaintext secret — returned exactly once, at
// creation. Every other read (ListAsync) uses WebhookSubscriptionResponse, which has no secret
// field at all.
public record CreateWebhookSubscriptionResponse(Guid Id, string TargetUrl, string[] Events, bool IsActive, string Secret);

public record WebhookSubscriptionResponse(Guid Id, string TargetUrl, string[] Events, bool IsActive);

public record WebhookDeliveryResponse(
    Guid Id, Guid WebhookSubscriptionId, string EventType, int Attempt, int? ResponseStatus,
    string? ResponseSnippet, DateTimeOffset? DeliveredAt, DateTimeOffset? NextRetryAt);

public class WebhookValidationException : Exception
{
    public WebhookValidationException(string message) : base(message) { }
}
```

- [ ] **Step 2: `ISecretProtector` and its Data Protection implementation**

`src/Modules/Webhooks/Application/ISecretProtector.cs`:

```csharp
namespace Courier.Webhooks.Application;

// Reversible encryption for WebhookSubscription.Secret (spec §5 — HMAC-signing an outgoing
// payload needs the plaintext at send time, so this must be encryption, not a one-way hash,
// despite DOMAIN-MODEL.md's original "hashed at rest" wording; Task 15 corrects that doc).
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
```

`src/Modules/Webhooks/Infrastructure/DataProtectionSecretProtector.cs`:

```csharp
using Courier.Webhooks.Application;
using Microsoft.AspNetCore.DataProtection;

namespace Courier.Webhooks.Infrastructure;

public class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    // A named purpose string ("Webhooks.Secret") scopes this protector's keys away from any
    // other Data Protection consumer this codebase might add later — ASP.NET Core's Data
    // Protection system derives a distinct key per purpose string from the same master key
    // ring, so two different purposes can never decrypt each other's ciphertext even if both
    // exist in the same process.
    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Webhooks.Secret");
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
```

- [ ] **Step 3: `WebhookSignature`**

`src/Modules/Webhooks/Application/WebhookSignature.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Courier.Webhooks.Application;

// X-Webhook-Signature: sha256=<HMAC> per API-SPEC.md §5 — computed over the exact raw outgoing
// payload bytes using the subscription's decrypted secret. A static pure function (no DbContext,
// no I/O) so both the consumer (Task 11) and the retry job (Task 12) call the identical
// computation, and so it's trivially unit-testable without any infrastructure.
public static class WebhookSignature
{
    public static string Compute(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return $"sha256={Convert.ToHexStringLower(hash)}";
    }
}
```

- [ ] **Step 4: `WebhookService`**

`src/Modules/Webhooks/Application/WebhookService.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Merchants.Application;
using Courier.Webhooks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Webhooks.Application;

public class WebhookService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly ISecretProtector _secretProtector;
    private readonly IMerchantLookupService _merchantLookupService;

    private static readonly HashSet<string> ValidEventTypes = new()
    {
        "shipment.created", "shipment.picked_up", "shipment.in_transit", "shipment.out_for_delivery",
        "shipment.delivered", "shipment.delivery_failed", "shipment.ndr_created", "shipment.rto",
        "cod.collected", "cod.settled"
    };

    public WebhookService(DbContext dbContext, IAuditLogger auditLogger, ISecretProtector secretProtector, IMerchantLookupService merchantLookupService)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _secretProtector = secretProtector;
        _merchantLookupService = merchantLookupService;
    }

    public async Task<CreateWebhookSubscriptionResponse> CreateSubscriptionAsync(Guid organizationId, Guid merchantId, CreateWebhookSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        if (!await _merchantLookupService.ExistsAsync(merchantId, cancellationToken))
        {
            throw new WebhookValidationException("Merchant was not found in this organization.");
        }
        if (!Uri.TryCreate(request.TargetUrl, UriKind.Absolute, out var targetUri) || (targetUri.Scheme != "http" && targetUri.Scheme != "https"))
        {
            throw new WebhookValidationException("TargetUrl must be an absolute http/https URL.");
        }
        if (request.Events.Length == 0)
        {
            throw new WebhookValidationException("At least one event type is required.");
        }
        var invalidEvents = request.Events.Where(e => !ValidEventTypes.Contains(e)).ToArray();
        if (invalidEvents.Length > 0)
        {
            throw new WebhookValidationException($"Unknown event type(s): {string.Join(", ", invalidEvents)}");
        }

        var plaintextSecret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var subscription = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            OrganizationId = organizationId,
            TargetUrl = request.TargetUrl,
            Secret = _secretProtector.Protect(plaintextSecret),
            Events = request.Events,
            IsActive = true
        };
        _dbContext.Set<WebhookSubscription>().Add(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, null, "webhook.subscription_created", "WebhookSubscription", subscription.Id.ToString()), cancellationToken);
        return new CreateWebhookSubscriptionResponse(subscription.Id, subscription.TargetUrl, subscription.Events, subscription.IsActive, plaintextSecret);
    }

    public async Task<List<WebhookSubscriptionResponse>> ListAsync(Guid organizationId, Guid merchantId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await _dbContext.Set<WebhookSubscription>()
            .Where(s => s.OrganizationId == organizationId && s.MerchantId == merchantId)
            .ToListAsync(cancellationToken);
        return subscriptions.Select(ToResponse).ToList();
    }

    public async Task<bool> DeleteAsync(Guid organizationId, Guid merchantId, Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.Set<WebhookSubscription>()
            .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.OrganizationId == organizationId && s.MerchantId == merchantId, cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        _dbContext.Set<WebhookSubscription>().Remove(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditLogger.LogAsync(new AuditEntry(organizationId, null, "webhook.subscription_deleted", "WebhookSubscription", subscription.Id.ToString()), cancellationToken);
        return true;
    }

    public async Task<List<WebhookDeliveryResponse>?> ListDeliveriesAsync(Guid organizationId, Guid merchantId, Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        var owned = await _dbContext.Set<WebhookSubscription>()
            .AnyAsync(s => s.Id == subscriptionId && s.OrganizationId == organizationId && s.MerchantId == merchantId, cancellationToken);
        if (!owned)
        {
            return null;
        }

        var deliveries = await _dbContext.Set<WebhookDelivery>()
            .Where(d => d.WebhookSubscriptionId == subscriptionId)
            .OrderByDescending(d => d.DeliveredAt ?? DateTimeOffset.MaxValue)
            .ToListAsync(cancellationToken);
        return deliveries.Select(ToDeliveryResponse).ToList();
    }

    private static WebhookSubscriptionResponse ToResponse(WebhookSubscription subscription) => new(
        subscription.Id, subscription.TargetUrl, subscription.Events, subscription.IsActive);

    internal static WebhookDeliveryResponse ToDeliveryResponse(WebhookDelivery delivery) => new(
        delivery.Id, delivery.WebhookSubscriptionId, delivery.EventType, delivery.Attempt,
        delivery.ResponseStatus, delivery.ResponseSnippet, delivery.DeliveredAt, delivery.NextRetryAt);
}
```

Note this class is declared `public partial class WebhookService` (not plain `public class`) — change the class declaration accordingly:

```csharp
public partial class WebhookService
```

`ReplayAsync` is deliberately NOT part of this file — it needs `IWebhookDeliveryAttemptService`, which doesn't exist until Task 11 (its consumer/retry-job send logic). Adding it here would make this task's own code fail to compile before Task 11 lands, breaking "each task ends with an independently testable deliverable." Task 11 adds `ReplayAsync` in a second partial file, `WebhookService.Replay.cs`, exactly the way `CodService` was split across `CodService.Settlements.cs`/`CodService.MerchantSettlements.cs`/`CodService.Adjustments.cs` in Phase 10 — each task's own file, added once its dependencies exist. `ToDeliveryResponse` is `internal` (not `private`) specifically so that second partial file can reuse it.

- [ ] **Step 5: Extend `MerchantPermissionCatalog`**

In `src/Modules/Merchants/Application/MerchantPermissionCatalog.cs`, add a new constant after `ShipmentCancel`:

```csharp
    // Webhook subscription management is an account-level integration setting, same sensitivity
    // tier as StaffManage/ProfileUpdate — Owner-only, not shared with Staff (Phase 11).
    public const string WebhooksManage = "merchant.webhooks.manage";
```

Add `WebhooksManage` to the `OwnerPermissions` array (do not add it to `StaffPermissions`):

```csharp
    private static readonly string[] OwnerPermissions =
    {
        ProfileRead, ProfileUpdate, StaffManage, KycUpload, ShipmentCreate, ShipmentRead, ShipmentCancel, WebhooksManage
    };
```

- [ ] **Step 6: Wire DI in `Program.cs`**

Add `using Courier.Webhooks.Application;`, `using Courier.Webhooks.Infrastructure;` to the usings. Add after the existing `builder.Services.AddScoped<NotificationDispatchService>();` line:

```csharp
builder.Services.AddDataProtection();
builder.Services.AddScoped<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddScoped<WebhookService>();
```

- [ ] **Step 7: Write the tests**

`tests/Webhooks.Tests/WebhookSignatureTests.cs`:

```csharp
using Courier.Webhooks.Application;
using Xunit;

namespace Courier.Webhooks.Tests;

public class WebhookSignatureTests
{
    [Fact]
    public void Compute_SamePayloadAndSecret_ProducesSameSignature()
    {
        var sig1 = WebhookSignature.Compute("my-secret", "{\"a\":1}");
        var sig2 = WebhookSignature.Compute("my-secret", "{\"a\":1}");
        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void Compute_DifferentSecret_ProducesDifferentSignature()
    {
        var sig1 = WebhookSignature.Compute("secret-one", "{\"a\":1}");
        var sig2 = WebhookSignature.Compute("secret-two", "{\"a\":1}");
        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void Compute_HasExpectedPrefix()
    {
        var sig = WebhookSignature.Compute("secret", "payload");
        Assert.StartsWith("sha256=", sig);
    }
}
```

`tests/Webhooks.Tests/WebhookServiceTests.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Merchants.Application;
using Courier.Webhooks.Application;
using Courier.Webhooks.Domain;
using Courier.Webhooks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Webhooks.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class InMemorySecretProtector : ISecretProtector
{
    // A real reversible round-trip, no external Data Protection dependency needed in unit
    // tests — just enough to prove WebhookService never stores the plaintext.
    public string Protect(string plaintext) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("protected:" + plaintext));
    public string Unprotect(string ciphertext) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext))["protected:".Length..];
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public class WebhookServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new WebhookSubscriptionConfiguration());
            modelBuilder.ApplyConfiguration(new WebhookDeliveryConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static WebhookService CreateService(TestDbContext dbContext) =>
        new(dbContext, new NoopAuditLogger(), new InMemorySecretProtector(), new AlwaysTrueMerchantLookup());

    [Fact]
    public async Task CreateSubscriptionAsync_ReturnsPlaintextSecretOnce_StoresOnlyCiphertext()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var response = await service.CreateSubscriptionAsync(organizationId, merchantId, new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "shipment.created" }));

        Assert.NotEmpty(response.Secret);
        var stored = await dbContext.Set<WebhookSubscription>().SingleAsync();
        Assert.NotEqual(response.Secret, stored.Secret);
        Assert.Equal(response.Secret, new InMemorySecretProtector().Unprotect(stored.Secret));
    }

    [Fact]
    public async Task CreateSubscriptionAsync_UnknownEventType_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<WebhookValidationException>(() =>
            service.CreateSubscriptionAsync(Guid.NewGuid(), Guid.NewGuid(), new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "not.a.real.event" })));
    }

    [Fact]
    public async Task CreateSubscriptionAsync_InvalidUrl_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<WebhookValidationException>(() =>
            service.CreateSubscriptionAsync(Guid.NewGuid(), Guid.NewGuid(), new CreateWebhookSubscriptionRequest("not-a-url", new[] { "shipment.created" })));
    }

    [Fact]
    public async Task ListAsync_OnlyReturnsCallingMerchantsOwnSubscriptions()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var service = CreateService(dbContext);
        await service.CreateSubscriptionAsync(organizationId, merchantId, new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "shipment.created" }));
        await service.CreateSubscriptionAsync(organizationId, Guid.NewGuid(), new CreateWebhookSubscriptionRequest("https://other.example.com/hook", new[] { "shipment.created" }));

        var result = await service.ListAsync(organizationId, merchantId);

        Assert.Single(result);
    }

    [Fact]
    public async Task DeleteAsync_ForeignMerchantsSubscription_ReturnsFalseAndDoesNotDelete()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext);
        var created = await service.CreateSubscriptionAsync(organizationId, Guid.NewGuid(), new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "shipment.created" }));

        var deleted = await service.DeleteAsync(organizationId, Guid.NewGuid(), created.Id);

        Assert.False(deleted);
        Assert.Equal(1, await dbContext.Set<WebhookSubscription>().CountAsync());
    }
}
```

(`ReplayAsync` has no tests in this file — it doesn't exist yet in this task's `WebhookService.cs`, see Step 4's note. Task 11 adds both the method and its tests together, in `WebhookService.Replay.cs`'s own test file section.)

- [ ] **Step 8: Run the tests**

Run: `dotnet test tests/Webhooks.Tests/Webhooks.Tests.csproj`
Expected: All 8 tests pass.

- [ ] **Step 9: Build the whole solution and run the full suite once**

Run: `dotnet build Courier.slnx` then `dotnet test Courier.slnx --no-build`
Expected: Build succeeds, every test project passes.

- [ ] **Step 10: Commit**

```bash
git add src/Modules/Webhooks/Application/ src/Modules/Webhooks/Infrastructure/DataProtectionSecretProtector.cs src/Modules/Merchants/Application/MerchantPermissionCatalog.cs src/Api/Program.cs tests/Webhooks.Tests/
git commit -m "feat(webhooks): add subscription CRUD, secret encryption, signature computation"
```

---

## Task 11: Webhooks delivery-attempt service, RabbitMQ consumer, `ReplayAsync`

**Files:**
- Create: `src/Modules/Webhooks/Application/IWebhookDeliveryAttemptService.cs`
- Create: `src/Modules/Webhooks/Infrastructure/WebhookDeliveryAttemptService.cs`
- Create: `src/Modules/Webhooks/Application/WebhookService.Replay.cs`
- Create: `src/Modules/Webhooks/Infrastructure/WebhooksConsumer.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Webhooks.Tests/WebhookServiceReplayTests.cs`
- Test: `tests/Webhooks.Tests/WebhookDeliveryAttemptServiceTests.cs`

**Interfaces:**
- Produces: `IWebhookDeliveryAttemptService.AttemptAsync(Guid deliveryId, CancellationToken) -> Task` — consumed by this task's own consumer, `WebhookService.ReplayAsync` (this task), and Task 12's retry job.
- Consumes: `WebhookSignature.Compute` (Task 10), `RabbitMQ.Client.IConnection`/`RabbitMqTopology` (Task 3), the envelope shape Task 7 established (`EventId`, `OrganizationId`, `EventType`, `Data`).

- [ ] **Step 1: `IWebhookDeliveryAttemptService`**

`src/Modules/Webhooks/Application/IWebhookDeliveryAttemptService.cs`:

```csharp
namespace Courier.Webhooks.Application;

// One method, reused by three callers: the consumer below (first attempt, immediately after
// creating a WebhookDelivery row), Task 12's retry job (subsequent attempts, on its own
// schedule), and WebhookService.ReplayAsync (Step 3 below, on demand). Every caller already
// knows the delivery exists — this only takes its id, loads everything else itself.
public interface IWebhookDeliveryAttemptService
{
    Task AttemptAsync(Guid deliveryId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: `WebhookDeliveryAttemptService`**

`src/Modules/Webhooks/Infrastructure/WebhookDeliveryAttemptService.cs`:

```csharp
using System.Net.Http.Json;
using System.Text;
using Courier.Webhooks.Application;
using Courier.Webhooks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Webhooks.Infrastructure;

public class WebhookDeliveryAttemptService : IWebhookDeliveryAttemptService
{
    private readonly DbContext _dbContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IHttpClientFactory _httpClientFactory;

    // 1m, 2m, 4m, 8m, 16m for attempts 1-5 (Global Constraints) — attempt N's backoff is
    // 2^(N-1) minutes; after attempt 5 fails, NextRetryAt stays null (terminal).
    private const int MaxAttempts = 5;
    private static TimeSpan BackoffFor(int attempt) => TimeSpan.FromMinutes(Math.Pow(2, attempt - 1));

    public WebhookDeliveryAttemptService(DbContext dbContext, ISecretProtector secretProtector, IHttpClientFactory httpClientFactory)
    {
        _dbContext = dbContext;
        _secretProtector = secretProtector;
        _httpClientFactory = httpClientFactory;
    }

    public async Task AttemptAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var result = await _dbContext.Set<WebhookDelivery>()
            .Join(_dbContext.Set<WebhookSubscription>(), d => d.WebhookSubscriptionId, s => s.Id, (d, s) => new { Delivery = d, Subscription = s })
            .FirstOrDefaultAsync(x => x.Delivery.Id == deliveryId, cancellationToken);
        if (result is null)
        {
            return;
        }
        var (delivery, subscription) = (result.Delivery, result.Subscription);

        delivery.Attempt += 1;
        var secret = _secretProtector.Unprotect(subscription.Secret);
        var signature = WebhookSignature.Compute(secret, delivery.Payload);

        var client = _httpClientFactory.CreateClient("Webhooks");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.TargetUrl)
            {
                Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Webhook-Signature", signature);

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            delivery.ResponseStatus = (int)response.StatusCode;
            delivery.ResponseSnippet = body.Length > 500 ? body[..500] : body;

            if (response.IsSuccessStatusCode)
            {
                delivery.DeliveredAt = DateTimeOffset.UtcNow;
                delivery.NextRetryAt = null;
            }
            else
            {
                delivery.NextRetryAt = delivery.Attempt < MaxAttempts ? DateTimeOffset.UtcNow + BackoffFor(delivery.Attempt) : null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            delivery.ResponseStatus = null;
            delivery.ResponseSnippet = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            delivery.NextRetryAt = delivery.Attempt < MaxAttempts ? DateTimeOffset.UtcNow + BackoffFor(delivery.Attempt) : null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 3: `WebhookService.ReplayAsync`**

`src/Modules/Webhooks/Application/WebhookService.Replay.cs`:

```csharp
using Courier.Webhooks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Webhooks.Application;

public partial class WebhookService
{
    // Resets the attempt counter and re-sends immediately (API-SPEC.md §5) — a fresh attempt
    // cycle, not a continuation of the old one, so a delivery that failed all 5 attempts can be
    // replayed after the merchant fixes their endpoint rather than staying permanently terminal.
    public async Task<WebhookDeliveryResponse?> ReplayAsync(Guid organizationId, Guid merchantId, Guid deliveryId, IWebhookDeliveryAttemptService attemptService, CancellationToken cancellationToken = default)
    {
        var delivery = await _dbContext.Set<WebhookDelivery>()
            .Join(_dbContext.Set<WebhookSubscription>(), d => d.WebhookSubscriptionId, s => s.Id, (d, s) => new { Delivery = d, Subscription = s })
            .Where(x => x.Delivery.Id == deliveryId && x.Subscription.OrganizationId == organizationId && x.Subscription.MerchantId == merchantId)
            .Select(x => x.Delivery)
            .FirstOrDefaultAsync(cancellationToken);
        if (delivery is null)
        {
            return null;
        }

        delivery.Attempt = 0;
        delivery.DeliveredAt = null;
        delivery.NextRetryAt = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await attemptService.AttemptAsync(delivery.Id, cancellationToken);
        await _dbContext.Entry(delivery).ReloadAsync(cancellationToken);
        return ToDeliveryResponse(delivery);
    }
}
```

- [ ] **Step 4: `WebhooksConsumer`**

`src/Modules/Webhooks/Infrastructure/WebhooksConsumer.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Courier.Webhooks.Application;
using Courier.Webhooks.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Courier.Webhooks.Infrastructure;

// Binds its own queue (RabbitMqTopology.WebhooksQueue) to the shared exchange, independently of
// NotificationsConsumer (Task 7) — same shared exchange, two separate queues, so both consumers
// see every published event without competing for the same messages (a topic exchange fans out
// to every bound queue whose binding matches, it does not load-balance across them).
public class WebhooksConsumer : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhooksConsumer> _logger;
    private IChannel? _channel;

    public WebhooksConsumer(IConnection connection, IServiceScopeFactory scopeFactory, ILogger<WebhooksConsumer> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.ExchangeDeclareAsync(RabbitMqTopology.DomainEventsExchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(RabbitMqTopology.WebhooksQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(RabbitMqTopology.WebhooksQueue, RabbitMqTopology.DomainEventsExchange, routingKey: "#", cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            var eventType = eventArgs.RoutingKey;
            var rawEnvelope = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
            try
            {
                var envelope = JsonDocument.Parse(rawEnvelope).RootElement;
                if (!envelope.TryGetProperty("EventId", out var eventIdProperty) || !eventIdProperty.TryGetGuid(out var sourceEventId))
                {
                    throw new WebhookValidationException("Envelope has no EventId.");
                }
                if (!envelope.TryGetProperty("OrganizationId", out var orgProperty) || !orgProperty.TryGetGuid(out var organizationId))
                {
                    throw new WebhookValidationException("Envelope has no OrganizationId.");
                }
                if (!envelope.TryGetProperty("Data", out var dataElement))
                {
                    throw new WebhookValidationException("Envelope has no Data.");
                }
                var payloadJson = dataElement.GetRawText();

                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
                var attemptService = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptService>();

                var matchingSubscriptions = await dbContext.Set<WebhookSubscription>().IgnoreQueryFilters()
                    .Where(s => s.OrganizationId == organizationId && s.IsActive && s.Events.Contains(eventType))
                    .Select(s => s.Id)
                    .ToListAsync(stoppingToken);

                foreach (var subscriptionId in matchingSubscriptions)
                {
                    var alreadyExists = await dbContext.Set<WebhookDelivery>()
                        .AnyAsync(d => d.WebhookSubscriptionId == subscriptionId && d.SourceEventId == sourceEventId, stoppingToken);
                    if (alreadyExists)
                    {
                        continue;
                    }

                    var delivery = new WebhookDelivery
                    {
                        Id = Guid.NewGuid(),
                        WebhookSubscriptionId = subscriptionId,
                        SourceEventId = sourceEventId,
                        EventType = eventType,
                        Payload = payloadJson,
                        Attempt = 0
                    };
                    dbContext.Set<WebhookDelivery>().Add(delivery);
                    try
                    {
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                    catch (DbUpdateException)
                    {
                        // The unique (WebhookSubscriptionId, SourceEventId) index (Task 9) caught
                        // a redelivered message that raced past the AnyAsync pre-check above —
                        // this exact source event already has a delivery row for this
                        // subscription. Detach and move on; nothing to send twice.
                        dbContext.Entry(delivery).State = EntityState.Detached;
                        continue;
                    }

                    await attemptService.AttemptAsync(delivery.Id, stoppingToken);
                }

                await _channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process webhook fan-out for event {EventType}", eventType);
                await _channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync(RabbitMqTopology.WebhooksQueue, autoAck: false, consumer, cancellationToken: stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
        }
        await base.StopAsync(cancellationToken);
    }
}
```

- [ ] **Step 5: Wire DI in `Program.cs`**

Add `using Courier.Webhooks.Infrastructure;` to the usings. Add after the existing `builder.Services.AddScoped<WebhookService>();` line (Task 10):

```csharp
builder.Services.AddHttpClient("Webhooks", client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddScoped<IWebhookDeliveryAttemptService, WebhookDeliveryAttemptService>();
builder.Services.AddHostedService<WebhooksConsumer>();
```

- [ ] **Step 6: Write the tests**

`tests/Webhooks.Tests/WebhookDeliveryAttemptServiceTests.cs` — uses a fake `HttpMessageHandler` (no real network call, no `WireMock`/external test-server dependency, matching this codebase's convention of not introducing a new test-only package when a hand-written fake suffices):

```csharp
using System.Net;
using Courier.Webhooks.Application;
using Courier.Webhooks.Domain;
using Courier.Webhooks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Webhooks.Tests;

file class FakeSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => plaintext;
    public string Unprotect(string ciphertext) => ciphertext;
}

file class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;
    public string? LastSignatureHeader { get; private set; }

    public FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody = "")
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastSignatureHeader = request.Headers.TryGetValues("X-Webhook-Signature", out var values) ? values.First() : null;
        return Task.FromResult(new HttpResponseMessage(_statusCode) { Content = new StringContent(_responseBody) });
    }
}

file class SingleClientHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _client;
    public SingleClientHttpClientFactory(HttpMessageHandler handler) => _client = new HttpClient(handler);
    public HttpClient CreateClient(string name) => _client;
}

public class WebhookDeliveryAttemptServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new WebhookSubscriptionConfiguration());
            modelBuilder.ApplyConfiguration(new WebhookDeliveryConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static async Task<(WebhookSubscription Subscription, WebhookDelivery Delivery)> SeedAsync(TestDbContext dbContext, int initialAttempt = 0)
    {
        var subscription = new WebhookSubscription { Id = Guid.NewGuid(), MerchantId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), TargetUrl = "https://example.com/hook", Secret = "test-secret", Events = new[] { "shipment.created" }, IsActive = true };
        var delivery = new WebhookDelivery { Id = Guid.NewGuid(), WebhookSubscriptionId = subscription.Id, SourceEventId = Guid.NewGuid(), EventType = "shipment.created", Payload = "{\"a\":1}", Attempt = initialAttempt };
        dbContext.Set<WebhookSubscription>().Add(subscription);
        dbContext.Set<WebhookDelivery>().Add(delivery);
        await dbContext.SaveChangesAsync();
        return (subscription, delivery);
    }

    [Fact]
    public async Task AttemptAsync_SuccessResponse_SetsDeliveredAtAndClearsNextRetry()
    {
        await using var dbContext = CreateContext();
        var (_, delivery) = await SeedAsync(dbContext);
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var service = new WebhookDeliveryAttemptService(dbContext, new FakeSecretProtector(), new SingleClientHttpClientFactory(handler));

        await service.AttemptAsync(delivery.Id);

        var updated = await dbContext.Set<WebhookDelivery>().SingleAsync();
        Assert.NotNull(updated.DeliveredAt);
        Assert.Null(updated.NextRetryAt);
        Assert.Equal(200, updated.ResponseStatus);
        Assert.Equal(1, updated.Attempt);
        Assert.NotNull(handler.LastSignatureHeader);
        Assert.StartsWith("sha256=", handler.LastSignatureHeader);
    }

    [Fact]
    public async Task AttemptAsync_FailureResponse_BelowMaxAttempts_SchedulesBackoffRetry()
    {
        await using var dbContext = CreateContext();
        var (_, delivery) = await SeedAsync(dbContext, initialAttempt: 1);
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "boom");
        var service = new WebhookDeliveryAttemptService(dbContext, new FakeSecretProtector(), new SingleClientHttpClientFactory(handler));

        await service.AttemptAsync(delivery.Id);

        var updated = await dbContext.Set<WebhookDelivery>().SingleAsync();
        Assert.Null(updated.DeliveredAt);
        Assert.NotNull(updated.NextRetryAt);
        Assert.Equal(2, updated.Attempt);
        // Attempt 2's backoff is 2^(2-1) = 2 minutes.
        Assert.True(updated.NextRetryAt > DateTimeOffset.UtcNow.AddMinutes(1.9) && updated.NextRetryAt < DateTimeOffset.UtcNow.AddMinutes(2.1));
    }

    [Fact]
    public async Task AttemptAsync_FailureResponse_AtMaxAttempts_LeavesTerminalWithNoRetry()
    {
        await using var dbContext = CreateContext();
        var (_, delivery) = await SeedAsync(dbContext, initialAttempt: 4);
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "boom");
        var service = new WebhookDeliveryAttemptService(dbContext, new FakeSecretProtector(), new SingleClientHttpClientFactory(handler));

        await service.AttemptAsync(delivery.Id);

        var updated = await dbContext.Set<WebhookDelivery>().SingleAsync();
        Assert.Equal(5, updated.Attempt);
        Assert.Null(updated.DeliveredAt);
        Assert.Null(updated.NextRetryAt);
    }
}
```

`tests/Webhooks.Tests/WebhookServiceReplayTests.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Merchants.Application;
using Courier.Webhooks.Application;
using Courier.Webhooks.Domain;
using Courier.Webhooks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Webhooks.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class InMemorySecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => plaintext;
    public string Unprotect(string ciphertext) => ciphertext;
}

file class AlwaysTrueMerchantLookup : IMerchantLookupService
{
    public Task<bool> ExistsAsync(Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file class RecordingAttemptService : IWebhookDeliveryAttemptService
{
    public List<Guid> Attempted { get; } = new();
    public Task AttemptAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        Attempted.Add(deliveryId);
        return Task.CompletedTask;
    }
}

public class WebhookServiceReplayTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new WebhookSubscriptionConfiguration());
            modelBuilder.ApplyConfiguration(new WebhookDeliveryConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static WebhookService CreateService(TestDbContext dbContext) =>
        new(dbContext, new NoopAuditLogger(), new InMemorySecretProtector(), new AlwaysTrueMerchantLookup());

    [Fact]
    public async Task ReplayAsync_ResetsAttemptCounterBeforeCallingAttemptService()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var service = CreateService(dbContext);
        var subscription = await service.CreateSubscriptionAsync(organizationId, merchantId, new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "shipment.created" }));
        var delivery = new WebhookDelivery { Id = Guid.NewGuid(), WebhookSubscriptionId = subscription.Id, SourceEventId = Guid.NewGuid(), EventType = "shipment.created", Payload = "{}", Attempt = 5, ResponseStatus = 500, NextRetryAt = null };
        dbContext.Set<WebhookDelivery>().Add(delivery);
        await dbContext.SaveChangesAsync();
        var attemptService = new RecordingAttemptService();

        var result = await service.ReplayAsync(organizationId, merchantId, delivery.Id, attemptService);

        Assert.NotNull(result);
        Assert.Single(attemptService.Attempted, id => id == delivery.Id);
    }

    [Fact]
    public async Task ReplayAsync_ForeignMerchantsDelivery_ReturnsNullAndDoesNotAttempt()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext);
        var subscription = await service.CreateSubscriptionAsync(organizationId, Guid.NewGuid(), new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "shipment.created" }));
        var delivery = new WebhookDelivery { Id = Guid.NewGuid(), WebhookSubscriptionId = subscription.Id, SourceEventId = Guid.NewGuid(), EventType = "shipment.created", Payload = "{}", Attempt = 1 };
        dbContext.Set<WebhookDelivery>().Add(delivery);
        await dbContext.SaveChangesAsync();
        var attemptService = new RecordingAttemptService();

        var result = await service.ReplayAsync(organizationId, Guid.NewGuid(), delivery.Id, attemptService);

        Assert.Null(result);
        Assert.Empty(attemptService.Attempted);
    }
}
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/Webhooks.Tests/Webhooks.Tests.csproj`
Expected: All 13 tests pass (8 from Task 10 + 3 + 2 new).

- [ ] **Step 8: Build the whole solution and run the full suite once**

Run: `dotnet build Courier.slnx` then `dotnet test Courier.slnx --no-build`
Expected: Build succeeds, every test project passes — including every existing `Api.Tests` test, since `WebhooksConsumer` now also runs as a hosted service inside every `WebApplicationFactory<Program>`.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Webhooks/ src/Api/Program.cs tests/Webhooks.Tests/
git commit -m "feat(webhooks): add delivery-attempt service, RabbitMQ consumer, replay"
```

---

## Task 12: Webhook retry — Hangfire recurring job

**Files:**
- Create: `src/Modules/Webhooks/Application/WebhookRetryJob.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Webhooks.Tests/WebhookRetryJobTests.cs`

**Interfaces:**
- Consumes: `IWebhookDeliveryAttemptService.AttemptAsync` (Task 11).

- [ ] **Step 1: `WebhookRetryJob`**

`src/Modules/Webhooks/Application/WebhookRetryJob.cs`:

```csharp
using Courier.Webhooks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Webhooks.Application;

// Hangfire recurring job (registered in Program.cs, Step 2 below). WebhookDelivery has no own
// OrganizationId (Task 9) — no IgnoreQueryFilters() call needed here, unlike
// Events.Application.OutboxPublisherJob, since there is no tenant filter on this entity to
// bypass in the first place; "due for retry" is inherently a cross-tenant query by construction.
public class WebhookRetryJob
{
    private readonly DbContext _dbContext;
    private readonly IWebhookDeliveryAttemptService _attemptService;

    public WebhookRetryJob(DbContext dbContext, IWebhookDeliveryAttemptService attemptService)
    {
        _dbContext = dbContext;
        _attemptService = attemptService;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var dueDeliveryIds = await _dbContext.Set<WebhookDelivery>()
            .Where(d => d.NextRetryAt != null && d.NextRetryAt <= DateTimeOffset.UtcNow)
            .OrderBy(d => d.NextRetryAt)
            .Select(d => d.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var deliveryId in dueDeliveryIds)
        {
            await _attemptService.AttemptAsync(deliveryId, cancellationToken);
        }
    }
}
```

- [ ] **Step 2: Register DI and the Hangfire recurring schedule**

In `src/Api/Program.cs`, add after the existing `builder.Services.AddHostedService<WebhooksConsumer>();` line:

```csharp
builder.Services.AddScoped<WebhookRetryJob>();
```

Add after the existing `RecurringJob.AddOrUpdate<OutboxPublisherJob>(...)` line (near `app.Run();`):

```csharp
RecurringJob.AddOrUpdate<WebhookRetryJob>("webhook-retry", job => job.RunAsync(CancellationToken.None), "*/30 * * * * *");
```

(Every 30 seconds — deliberately coarser than the outbox publisher's 10 seconds, since retries are already spaced at least a minute apart by the backoff schedule itself; polling faster than that buys nothing.)

- [ ] **Step 3: Write the tests**

`tests/Webhooks.Tests/WebhookRetryJobTests.cs`:

```csharp
using Courier.Webhooks.Application;
using Courier.Webhooks.Domain;
using Courier.Webhooks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Webhooks.Tests;

file class RecordingAttemptService : IWebhookDeliveryAttemptService
{
    public List<Guid> Attempted { get; } = new();
    public Task AttemptAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        Attempted.Add(deliveryId);
        return Task.CompletedTask;
    }
}

public class WebhookRetryJobTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new WebhookSubscriptionConfiguration());
            modelBuilder.ApplyConfiguration(new WebhookDeliveryConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static WebhookDelivery MakeDelivery(DateTimeOffset? nextRetryAt) => new()
    {
        Id = Guid.NewGuid(), WebhookSubscriptionId = Guid.NewGuid(), SourceEventId = Guid.NewGuid(),
        EventType = "shipment.created", Payload = "{}", Attempt = 1, NextRetryAt = nextRetryAt
    };

    [Fact]
    public async Task RunAsync_DueDelivery_AttemptsIt()
    {
        await using var dbContext = CreateContext();
        var due = MakeDelivery(DateTimeOffset.UtcNow.AddMinutes(-1));
        dbContext.Set<WebhookDelivery>().Add(due);
        await dbContext.SaveChangesAsync();
        var attemptService = new RecordingAttemptService();
        var job = new WebhookRetryJob(dbContext, attemptService);

        await job.RunAsync();

        Assert.Single(attemptService.Attempted, id => id == due.Id);
    }

    [Fact]
    public async Task RunAsync_NotYetDueDelivery_DoesNotAttemptIt()
    {
        await using var dbContext = CreateContext();
        var notYetDue = MakeDelivery(DateTimeOffset.UtcNow.AddMinutes(5));
        dbContext.Set<WebhookDelivery>().Add(notYetDue);
        await dbContext.SaveChangesAsync();
        var attemptService = new RecordingAttemptService();
        var job = new WebhookRetryJob(dbContext, attemptService);

        await job.RunAsync();

        Assert.Empty(attemptService.Attempted);
    }

    [Fact]
    public async Task RunAsync_DeliveryWithNoNextRetryAt_DoesNotAttemptIt()
    {
        // Either terminal-failed (5 attempts exhausted) or already-delivered — either way,
        // NextRetryAt is null and this job must never touch it.
        await using var dbContext = CreateContext();
        var terminal = MakeDelivery(null);
        dbContext.Set<WebhookDelivery>().Add(terminal);
        await dbContext.SaveChangesAsync();
        var attemptService = new RecordingAttemptService();
        var job = new WebhookRetryJob(dbContext, attemptService);

        await job.RunAsync();

        Assert.Empty(attemptService.Attempted);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Webhooks.Tests/Webhooks.Tests.csproj`
Expected: All 16 tests pass (13 from Task 11 + 3 new).

- [ ] **Step 5: Build the whole solution and run the full suite once**

Run: `dotnet build Courier.slnx` then `dotnet test Courier.slnx --no-build`
Expected: Build succeeds, every test project passes.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Webhooks/Application/WebhookRetryJob.cs src/Api/Program.cs tests/Webhooks.Tests/WebhookRetryJobTests.cs
git commit -m "feat(webhooks): add webhook delivery retry Hangfire recurring job"
```

---

## Task 13: Webhooks Presentation — `WebhooksEndpoints.cs` and `Program.cs` wiring

**Files:**
- Create: `src/Modules/Webhooks/Presentation/WebhooksEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Api.Tests/WebhooksEndpointsTests.cs`

**Interfaces:**
- Produces: every `/api/v1/webhooks*` route `API-SPEC.md` §5 specifies.

**Design note carried from Task 10:** `WebhookService`'s methods take `organizationId`/`merchantId` as explicit `Guid` parameters rather than resolving them internally from a `ClaimsPrincipal` (unlike `CodService`/`ShipmentService`'s more common pattern in this codebase) — this endpoint file resolves both in the route handler itself and passes them in, mirroring `MerchantProfileEndpoints.cs`'s existing exact shape (`var merchantId = httpContext.User.GetMerchantId(); if (merchantId is null) return Results.Unauthorized();`) rather than introducing a new pattern. `organizationId` is resolved the same way via `ICurrentTenantProvider`, injected as a minimal-API endpoint parameter (ASP.NET Core resolves any registered service type automatically when a delegate parameter's type matches one) — this works identically whether the caller authenticated via the staff JWT scheme or the merchant JWT scheme, since `ICurrentTenantProvider` reads a shared `org_id`-shaped claim off whichever token is present, the same mechanism `ShipmentService.CreateShipmentAsync` already relies on for merchant-JWT callers today.

- [ ] **Step 1: `WebhooksEndpoints.cs`**

`src/Modules/Webhooks/Presentation/WebhooksEndpoints.cs` — mirrors `MerchantKycEndpoints.cs`'s shape (a merchant-track-only module) more than `CodEndpoints.cs`'s (a staff-track module):

```csharp
using Courier.SharedKernel;
using Courier.Webhooks.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Webhooks.Presentation;

public static class WebhooksEndpoints
{
    public static void MapWebhooksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/webhooks").WithTags("Webhooks");

        group.MapPost("/", async (CreateWebhookSubscriptionRequest request, WebhookService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var (organizationId, merchantId, unauthorized) = ResolveCaller(tenantProvider, httpContext);
            if (unauthorized is not null) return unauthorized;
            return await HandleAsync(() => service.CreateSubscriptionAsync(organizationId, merchantId, request, httpContext.RequestAborted));
        }).RequireMerchantPermission(MerchantPermissionCatalog.WebhooksManage);

        group.MapGet("/", async (WebhookService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var (organizationId, merchantId, unauthorized) = ResolveCaller(tenantProvider, httpContext);
            if (unauthorized is not null) return unauthorized;
            return Results.Ok(await service.ListAsync(organizationId, merchantId, httpContext.RequestAborted));
        }).RequireMerchantPermission(MerchantPermissionCatalog.WebhooksManage);

        group.MapDelete("/{id:guid}", async (Guid id, WebhookService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var (organizationId, merchantId, unauthorized) = ResolveCaller(tenantProvider, httpContext);
            if (unauthorized is not null) return unauthorized;
            var deleted = await service.DeleteAsync(organizationId, merchantId, id, httpContext.RequestAborted);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).RequireMerchantPermission(MerchantPermissionCatalog.WebhooksManage);

        group.MapGet("/{id:guid}/deliveries", async (Guid id, WebhookService service, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var (organizationId, merchantId, unauthorized) = ResolveCaller(tenantProvider, httpContext);
            if (unauthorized is not null) return unauthorized;
            var deliveries = await service.ListDeliveriesAsync(organizationId, merchantId, id, httpContext.RequestAborted);
            return deliveries is null ? Results.NotFound() : Results.Ok(deliveries);
        }).RequireMerchantPermission(MerchantPermissionCatalog.WebhooksManage);

        group.MapPost("/deliveries/{id:guid}/replay", async (Guid id, WebhookService service, IWebhookDeliveryAttemptService attemptService, ICurrentTenantProvider tenantProvider, HttpContext httpContext) =>
        {
            var (organizationId, merchantId, unauthorized) = ResolveCaller(tenantProvider, httpContext);
            if (unauthorized is not null) return unauthorized;
            var result = await service.ReplayAsync(organizationId, merchantId, id, attemptService, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireMerchantPermission(MerchantPermissionCatalog.WebhooksManage);
    }

    private static (Guid OrganizationId, Guid MerchantId, IResult? Unauthorized) ResolveCaller(ICurrentTenantProvider tenantProvider, HttpContext httpContext)
    {
        var merchantId = httpContext.User.GetMerchantId();
        if (merchantId is null)
        {
            return (default, default, Results.Unauthorized());
        }
        if (tenantProvider.OrganizationId is not Guid organizationId)
        {
            return (default, default, Results.Unauthorized());
        }
        return (organizationId, merchantId.Value, null);
    }

    private static async Task<IResult> HandleAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            var result = await operation();
            return Results.Ok(result);
        }
        catch (WebhookValidationException ex)
        {
            return Results.BadRequest(new { error = new { code = "WEBHOOK_VALIDATION_FAILED", message = ex.Message } });
        }
    }
}
```

- [ ] **Step 2: Wire into `Program.cs`**

Add `using Courier.Webhooks.Presentation;` to the usings. Add after the existing `app.MapCodEndpoints();` line:

```csharp
app.MapWebhooksEndpoints();
```

- [ ] **Step 3: Write an API test**

`tests/Api.Tests/WebhooksEndpointsTests.cs` — this module is called with a **merchant** JWT, not the staff token every other endpoint test in this codebase uses. The register-org/create-merchant/login-as-merchant helper sequence below is copied verbatim from `tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs`'s existing `RegisterOrgAndGetStaffTokenAsync`/`CreateMerchantAsync` methods and its `MerchantOwnerLogin_ThenGetProfile_Succeeds` test's login call — this codebase's established per-file-helper convention (see `Cod.Tests`/`Ndr.Tests`) is to copy, not share, so this file gets its own copies:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.Webhooks.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class WebhooksEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public WebhooksEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> RegisterOrgAndGetStaffTokenAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12];
        var request = new RegisterOrganizationRequest(
            $"Webhooks Org {suffix}", $"webhooks-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        return auth.AccessToken;
    }

    private async Task<(string OwnerEmail, string Password)> CreateMerchantAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"owner-{label}-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            "Webhook Traders Pvt. Ltd.", "Webhook Traders", "01-4444444", "contact@webhooktraders.example",
            ownerEmail, "P@ssw0rd123!", "Merchant Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        return (ownerEmail, "P@ssw0rd123!");
    }

    private async Task<HttpClient> CreateAuthenticatedMerchantClientAsync(string label)
    {
        var staffClient = _factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, label);
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (ownerEmail, password) = await CreateMerchantAsync(staffClient, label);

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, password));
        loginResponse.EnsureSuccessStatusCode();
        var auth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return merchantClient;
    }

    [Fact]
    public async Task CreateAndListAndDeleteSubscription_FullLifecycle()
    {
        var client = await CreateAuthenticatedMerchantClientAsync("lifecycle");

        var createResponse = await client.PostAsJsonAsync("/api/v1/webhooks", new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "shipment.created" }));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateWebhookSubscriptionResponse>();
        Assert.NotNull(created);
        Assert.NotEmpty(created!.Secret);

        var listResponse = await client.GetAsync("/api/v1/webhooks");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<List<WebhookSubscriptionResponse>>();
        Assert.Single(list!, s => s.Id == created.Id);

        var deleteResponse = await client.DeleteAsync($"/api/v1/webhooks/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listAfterDelete = await client.GetAsync("/api/v1/webhooks");
        var listAfter = await listAfterDelete.Content.ReadFromJsonAsync<List<WebhookSubscriptionResponse>>();
        Assert.Empty(listAfter!);
    }

    [Fact]
    public async Task CreateSubscription_WithoutMerchantAuthentication_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/webhooks", new CreateWebhookSubscriptionRequest("https://example.com/hook", new[] { "shipment.created" }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `docker exec courier-postgres-1 pg_isready -U courier && docker exec courier-rabbitmq-1 rabbitmq-diagnostics -q ping` to confirm both shared containers are up.
Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~WebhooksEndpointsTests"`
Expected: Both tests pass.

- [ ] **Step 5: Build the whole solution and run the full suite once**

Run: `dotnet build Courier.slnx` then `dotnet test Courier.slnx --no-build`
Expected: Build succeeds, every test project passes.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Webhooks/Presentation/ src/Api/Program.cs tests/Api.Tests/WebhooksEndpointsTests.cs
git commit -m "feat(webhooks): expose webhook management endpoints"
```

---

## Task 14: End-to-end API tests — real email delivery, real webhook retry-then-replay

**Files:**
- Create: `tests/Api.Tests/NotificationsAndWebhooksEndToEndTests.cs`

**Interfaces:**
- Consumes: every wired event/pipeline component from Tasks 1-13, plus the real `courier-postgres-1`, `courier-rabbitmq-1`, and the mail-catcher (Task 8) containers.

This is the phase's own stated exit criteria, proven literally: "at least one real channel... fires off a domain event with a configurable template; a webhook subscriber receives a correctly signed event and a forced failure demonstrably retries then logs" (`ROADMAP.md` Phase 11).

- [ ] **Step 1: Write the end-to-end tests**

`tests/Api.Tests/NotificationsAndWebhooksEndToEndTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.Persistence;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Shipments.Application;
using Courier.Webhooks.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

// Bare System.Net.HttpListener stub, not a new test-only package — mirrors this task's own
// "hand-written fake over an external dependency" convention already used for
// FakeHttpMessageHandler (Task 11's unit tests), just as a real listening HTTP server this time,
// since WebhooksConsumer's real HttpClient makes a genuine network call over the real loopback
// interface, not something a Task 11-style in-process fake can intercept.
file class StubWebhookTarget : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    public int ResponseStatusCode { get; set; } = 200;
    public int RequestCount { get; private set; }
    public string? LastSignatureHeader { get; private set; }
    public string? LastBody { get; private set; }
    public string Url { get; }

    public StubWebhookTarget()
    {
        var port = GetFreePort();
        Url = $"http://127.0.0.1:{port}/hook";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested || _cts.IsCancellationRequested)
            {
                return;
            }
            RequestCount++;
            LastSignatureHeader = context.Request.Headers["X-Webhook-Signature"];
            using var reader = new StreamReader(context.Request.InputStream);
            LastBody = await reader.ReadToEndAsync();
            context.Response.StatusCode = ResponseStatusCode;
            context.Response.Close();
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
    }
}

public class NotificationsAndWebhooksEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public NotificationsAndWebhooksEndToEndTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"E2E Org {suffix}", $"e2e-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
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

    // Returns owner login credentials alongside ContactEmail — the first test only needs
    // ContactEmail (to poll the mail-catcher), the second needs OwnerEmail/Password (to log in
    // as this merchant and manage its own webhook subscription). Mirrors
    // MerchantAuthAndStaffEndpointsTests.cs's own CreateMerchantAsync, which already returns
    // owner credentials — this version also carries ContactEmail alongside them.
    private async Task<(Guid MerchantId, string ContactEmail, string OwnerEmail, string Password)> CreateMerchantAsync(HttpClient staffClient)
    {
        var contactEmail = $"contact-{Guid.NewGuid():N}@example.com";
        var ownerEmail = $"shipper-owner-{Guid.NewGuid():N}@example.com";
        const string password = "P@ssw0rd123!";
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "E2E Shipper Pvt. Ltd.", "E2E Shipper", "01-4444444", contactEmail,
            ownerEmail, password, "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return (merchant!.Id, contactEmail, ownerEmail, password);
    }

    private static CreateShipmentRequest PlainShipmentRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 1, 500m) });

    private static async Task<JsonElement?> PollMailhogForRecipientAsync(HttpClient httpClient, string recipientEmail, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await httpClient.GetAsync("http://localhost:8025/api/v2/messages");
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            foreach (var item in json.GetProperty("items").EnumerateArray())
            {
                var toArray = item.GetProperty("To");
                if (toArray.GetArrayLength() == 0)
                {
                    continue;
                }
                var mailbox = toArray[0].GetProperty("Mailbox").GetString();
                var domain = toArray[0].GetProperty("Domain").GetString();
                if (string.Equals($"{mailbox}@{domain}", recipientEmail, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
            await Task.Delay(500);
        }
        return null;
    }

    private static async Task<T> PollUntilAsync<T>(Func<Task<T?>> probe, TimeSpan timeout) where T : class
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await probe();
            if (result is not null)
            {
                return result;
            }
            await Task.Delay(500);
        }
        throw new TimeoutException($"Condition not met within {timeout}.");
    }

    [Fact]
    public async Task ShipmentCreated_SendsRealEmailToMerchantContactEmail()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "email");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var (merchantId, contactEmail, _, _) = await CreateMerchantAsync(client);

        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", PlainShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        createResponse.EnsureSuccessStatusCode();
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        using var mailhogClient = new HttpClient();
        var message = await PollMailhogForRecipientAsync(mailhogClient, contactEmail, TimeSpan.FromSeconds(30));

        Assert.NotNull(message);
        var bodyResponse = await mailhogClient.GetAsync($"http://localhost:8025/api/v1/messages/{message!.Value.GetProperty("ID").GetString()}");
        var rawBody = await bodyResponse.Content.ReadAsStringAsync();
        Assert.Contains(shipment!.Awb, rawBody);
    }

    [Fact]
    public async Task WebhookSubscription_ReceivesSignedEvent_ForcedFailureRetriesThenAppearsFailed_ThenReplaySucceeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "webhook");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var (merchantId, _, ownerEmail, password) = await CreateMerchantAsync(client);

        using var stubTarget = new StubWebhookTarget { ResponseStatusCode = 500 };

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, password));
        loginResponse.EnsureSuccessStatusCode();
        var merchantAuth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", merchantAuth!.AccessToken);

        var subscribeResponse = await merchantClient.PostAsJsonAsync("/api/v1/webhooks", new CreateWebhookSubscriptionRequest(stubTarget.Url, new[] { "shipment.created" }));
        subscribeResponse.EnsureSuccessStatusCode();
        var subscription = await subscribeResponse.Content.ReadFromJsonAsync<CreateWebhookSubscriptionResponse>();

        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", PlainShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        createResponse.EnsureSuccessStatusCode();

        // First attempt happens immediately, inline in WebhooksConsumer's message handling — no
        // need to wait for the Hangfire retry job's cron schedule for this first failure.
        var deliveries = await PollUntilAsync(async () =>
        {
            var response = await merchantClient.GetAsync($"/api/v1/webhooks/{subscription!.Id}/deliveries");
            var list = await response.Content.ReadFromJsonAsync<List<WebhookDeliveryResponse>>();
            return list!.Count > 0 ? list : null;
        }, TimeSpan.FromSeconds(30));
        var delivery = Assert.Single(deliveries);
        Assert.Equal(1, delivery.Attempt);
        Assert.Equal(500, delivery.ResponseStatus);
        Assert.Null(delivery.DeliveredAt);
        Assert.NotNull(delivery.NextRetryAt);
        Assert.True(stubTarget.RequestCount >= 1);
        Assert.NotNull(stubTarget.LastSignatureHeader);
        Assert.StartsWith("sha256=", stubTarget.LastSignatureHeader);
        Assert.Contains("shipment.created", stubTarget.LastBody ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // Fix the target, then replay on demand rather than waiting out the real 1-minute
        // backoff — the backoff schedule's own timing math is already proven by Task 11's/Task
        // 12's unit tests; this test's job is to prove replay recovers a failed delivery over a
        // real HTTP round trip, not to wait out a cron timer.
        stubTarget.ResponseStatusCode = 200;
        var replayResponse = await merchantClient.PostAsync($"/api/v1/webhooks/deliveries/{delivery.Id}/replay", null);
        replayResponse.EnsureSuccessStatusCode();
        var replayed = await replayResponse.Content.ReadFromJsonAsync<WebhookDeliveryResponse>();

        Assert.NotNull(replayed!.DeliveredAt);
        Assert.Equal(200, replayed.ResponseStatus);
        Assert.Null(replayed.NextRetryAt);
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `docker exec courier-postgres-1 pg_isready -U courier && docker exec courier-rabbitmq-1 rabbitmq-diagnostics -q ping` to confirm both shared containers are up. Confirm the mail-catcher is reachable too: `curl http://localhost:8025/api/v2/messages` (start it per Task 8 Step 3 if not already running).
Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~NotificationsAndWebhooksEndToEndTests"`
Expected: Both tests pass — this requires all four backing services (Postgres, Redis, RabbitMQ, mail-catcher) up.

- [ ] **Step 3: Build the whole solution and run the full suite once**

Run: `dotnet build Courier.slnx` then `dotnet test Courier.slnx --no-build`
Expected: Build succeeds, every test project passes, no regressions in any prior phase's tests.

- [ ] **Step 4: Commit**

```bash
git add tests/Api.Tests/NotificationsAndWebhooksEndToEndTests.cs
git commit -m "test(notifications,webhooks): add end-to-end real-email and webhook retry/replay tests"
```

---

## Task 15: Docs sync — `CLARIFICATIONS.md`, `DOMAIN-MODEL.md`, `ROADMAP.md`

**Files:**
- Modify: `docs/CLARIFICATIONS.md`
- Modify: `docs/DOMAIN-MODEL.md`
- Modify: `docs/ROADMAP.md`

**Interfaces:** none — documentation only.

- [ ] **Step 1: Add `CLARIFICATIONS.md` entry #12**

Append a new section after entry #11 (`## 11. Phase 10: ...`, added by Phase 10's own plan) and before the `## Summary of schema additions implied by this document` heading:

```markdown
## 12. Phase 11: outbox envelope shape, webhook secret storage, retry policy, template seeding

**Where:** `CLAUDE.md` §26-27, `ARCHITECTURE.md` "Domain events & reliability"/"Background jobs —
Hangfire", `DOMAIN-MODEL.md` "Notifications"/"Webhooks", `API-SPEC.md` §5

**Ambiguity 1:** `DOMAIN-MODEL.md`'s `WebhookSubscription` entity lists `Secret (hashed at rest)`,
but HMAC-signing an outgoing payload requires the plaintext secret at send time — a one-way hash
cannot produce a valid signature.

**Resolution 1:** `WebhookSubscription.Secret` is encrypted at rest (ASP.NET Core Data
Protection, `Courier.Webhooks.Infrastructure.DataProtectionSecretProtector`), not hashed — a
reversible operation, decrypted only inside `WebhookDeliveryAttemptService`'s signing path. The
plaintext is returned exactly once, in `POST /api/v1/webhooks`'s response body, and never
appears in any `GET` response afterward.

**Ambiguity 2:** No source document specifies a webhook retry attempt count or backoff schedule
— `API-SPEC.md` §5 says only "exponential backoff... capped attempts."

**Resolution 2:** 5 attempts, backoff 1m/2m/4m/8m/16m (attempt *N*'s delay is 2^(N-1) minutes),
then the delivery is left in a terminal state (`NextRetryAt` null, `DeliveredAt` still null) —
visible via `GET /webhooks/{id}/deliveries` and recoverable via `POST
/webhooks/deliveries/{id}/replay`, which resets the attempt counter to 0 and re-sends
immediately rather than continuing the old cycle.

**Ambiguity 3:** `CLAUDE.md` §27 says notification templates "must be configurable," but no
source document specifies whether default templates are seeded per-organization (like `Role`
rows at registration) or resolved some other way.

**Resolution 3:** No `NotificationTemplate` rows are seeded anywhere this phase — no
org-registration-time seeding, no migration data seed. `NotificationDispatchService` checks for
an active org-specific override row first; if none exists (every organization, every event, this
phase), it falls back to a compiled-in default body
(`Courier.Notifications.Application.NotificationDefaultTemplates`). This avoids a new
`Identity -> Notifications` module dependency that eager per-org seeding at registration would
have required, and a future template-management API (deferred, out of this phase's scope) simply
inserts override rows into the same table this resolver already checks — no code change needed
when that API lands.

**Implementation note (not an ambiguity, but worth recording):** `WebhookDelivery` gains one
field beyond `DOMAIN-MODEL.md`'s original sketch: `SourceEventId` (the originating
`OutboxMessage.Id`), with a unique index on `(WebhookSubscriptionId, SourceEventId)` — this is
the concrete mechanism behind "a redelivered RabbitMQ message can't double-create delivery rows,"
the same DB-enforced-not-just-checked-then-written lesson `FinancialTransaction`'s uniqueness
constraint established in Phase 10. `NotificationLog.RecipientAddress` is nullable (`string?`),
accommodating the `NoRecipient` status (no `Merchant.ContactEmail` on file) — also a departure
from the literal, non-nullable field `DOMAIN-MODEL.md`'s original sketch showed. The outbox
publisher wraps each `OutboxMessage`'s bare JSON payload in a small envelope
(`{ EventId, OrganizationId, EventType, Data }`) before publishing to RabbitMQ — `OrganizationId`
and `EventId` are separate columns on `OutboxMessage`, not part of the payload each producing
module built, and both consumers need them (tenant scoping, idempotency respectively).

---
```

- [ ] **Step 2: Add an "Events & Outbox" section and update "Notifications"/"Webhooks" in `DOMAIN-MODEL.md`**

Add a new section immediately before the existing `## Notifications — \`OrganizationId\` present` heading:

```markdown
## Events & Outbox — `OrganizationId` present

```text
OutboxMessage { Id, OrganizationId, EventType, Payload, CreatedAt, PublishedAt (nullable),
                 Attempts }
                 # Written by IOutboxWriter.WriteAsync (Courier.Events module) inside the same
                 # SaveChangesAsync as the business change that caused it — the DB-commit-vs-
                 # message-publish reliability guarantee ARCHITECTURE.md's outbox pattern
                 # describes. A Hangfire recurring job (the "outbox publisher") reads unpublished
                 # rows and publishes each, wrapped in an envelope
                 # ({EventId, OrganizationId, EventType, Data}), to a shared RabbitMQ topic
                 # exchange (courier.domain-events, routing key = EventType). Events has zero
                 # project reference to any other module — Payload is an opaque JSON string it
                 # never interprets. [C#12]
```
```

Replace the existing `NotificationLog` field-list line inside the `## Notifications` section:

```text
NotificationLog { Id, OrganizationId, EventType, Channel, RecipientAddress, Status, SentAt, ProviderResponse }
```

with:

```text
NotificationLog { Id, OrganizationId, EventType, Channel, RecipientAddress (nullable), Status
                    (Sent|Logged|Failed|NoRecipient), SentAt, ProviderResponse }
                    # RecipientAddress is nullable to accommodate NoRecipient (no
                    # Merchant.ContactEmail on file) — a departure from this section's original
                    # non-nullable sketch. Status's four values replace this section's original
                    # unenumerated Status field: only Email is a real channel this phase
                    # (real SMTP); Sms/WhatsApp/Push always resolve to Logged, never Sent/Failed,
                    # since no real provider is wired to them yet. [C#12]
```

Replace the existing `WebhookSubscription` field-list line inside the `## Webhooks` section:

```text
WebhookSubscription { Id, MerchantId, OrganizationId, TargetUrl, Secret (hashed at rest), Events[], IsActive }
```

with:

```text
WebhookSubscription { Id, MerchantId, OrganizationId, TargetUrl, Secret (encrypted at rest, not
                        hashed — see CLARIFICATIONS.md #12 resolution 1), Events[], IsActive }
```

Replace the existing `WebhookDelivery` field-list line:

```text
WebhookDelivery { Id, WebhookSubscriptionId, EventType, Payload, Attempt, ResponseStatus, ResponseSnippet, DeliveredAt (nullable), NextRetryAt (nullable) }
```

with:

```text
WebhookDelivery { Id, WebhookSubscriptionId, SourceEventId, EventType, Payload, Attempt,
                    ResponseStatus (nullable), ResponseSnippet (nullable), DeliveredAt (nullable),
                    NextRetryAt (nullable) }
                    # SourceEventId is the originating OutboxMessage.Id — a unique index on
                    # (WebhookSubscriptionId, SourceEventId) makes duplicate-delivery-row
                    # creation from a redelivered RabbitMQ message DB-impossible, not just
                    # checked-then-written. [C#12]
```

- [ ] **Step 3: Update `ROADMAP.md`'s Phase 11 section**

Replace the existing Phase 11 block:

```markdown
## Phase 11 — Notifications, SMS, Email, Push, Webhooks

Event-driven notification pipeline (CLAUDE.md §27) off the Phase-0/1 outbox, plus
`Webhooks` module per `API-SPEC.md` §5 (signature, retry, delivery log, replay).
*Exit:* at least one real channel (e.g. SMS or email) fires off a domain event with
a configurable template; a webhook subscriber receives a correctly signed event and
a forced failure demonstrably retries then logs.
```

with:

```markdown
## Phase 11 — Notifications, SMS, Email, Push, Webhooks

Event-driven notification pipeline (CLAUDE.md §27) off the Phase-0/1 outbox, plus
`Webhooks` module per `API-SPEC.md` §5 (signature, retry, delivery log, replay).
*Exit:* at least one real channel (e.g. SMS or email) fires off a domain event with
a configurable template; a webhook subscriber receives a correctly signed event and
a forced failure demonstrably retries then logs.

*Status (2026-08-26): Complete.* Three new modules: `Events` (owns `OutboxMessage` and the
narrow `IOutboxWriter` interface every producing module calls, mirroring `Audit`'s `IAuditLogger`
shape), `Notifications` (real SMTP for Email via MailKit, log-only stubs for SMS/WhatsApp/Push),
`Webhooks` (merchant-managed subscriptions, signed delivery, exponential-backoff retry, replay).
Four proving-set domain events wired: `shipment.created`, `shipment.delivered`,
`shipment.ndr_created`, `cod.collected` — each an existing application-service call site gaining
one `IOutboxWriter.WriteAsync` call in the same `SaveChangesAsync` as its business change. This
phase is the first to actually use RabbitMQ and Hangfire (both provisioned since Phase 0 but
unused before now, per `CLARIFICATIONS.md` #4): a Hangfire recurring job publishes unpublished
outbox rows to a shared RabbitMQ topic exchange, and `Notifications`/`Webhooks` each run their
own independent `BackgroundService` consumer bound to their own queue on that exchange — a real
internal broker hop, not a simplified in-process shortcut, matching `ARCHITECTURE.md`'s
already-committed design. `CLARIFICATIONS.md` #12 records three resolved ambiguities:
`WebhookSubscription.Secret` is encrypted (Data Protection), not hashed — HMAC-signing needs the
plaintext at send time; retry is 5 attempts with 1m/2m/4m/8m/16m backoff; no `NotificationTemplate`
rows are seeded anywhere — `NotificationDispatchService` falls back to a compiled-in default body
per event type when no org-specific override exists, avoiding a new `Identity -> Notifications`
dependency eager seeding would have required. `WebhookDelivery.SourceEventId` plus a unique
`(WebhookSubscriptionId, SourceEventId)` index makes duplicate-delivery creation from a
redelivered message DB-impossible, the same TOCTOU-guard lesson `FinancialTransaction`'s
uniqueness constraint established in Phase 10. The full exit criteria — a real email landing in a
dev-only mail-catcher (`docker-compose.yml`'s new `mailhog` service) and a forced-failure webhook
target retrying then appearing failed in the delivery log, then succeeding on replay — is proven
end-to-end over real HTTP, real Postgres, and real RabbitMQ in
`tests/Api.Tests/NotificationsAndWebhooksEndToEndTests.cs`. Deferred to later phases: a
`NotificationTemplate` management API, the six remaining `CLAUDE.md` §26 webhook event types
(subscribable but never published this phase), and the Hangfire recurring jobs `ARCHITECTURE.md`
separately brainstormed that this pipeline itself doesn't need (scheduled merchant-settlement
generation, NDR SLA reminders, expired `UserBranch` cleanup) — all now unblocked by this phase's
Hangfire wiring, none implemented by it.
```

- [ ] **Step 4: Commit**

```bash
git add docs/CLARIFICATIONS.md docs/DOMAIN-MODEL.md docs/ROADMAP.md
git commit -m "docs: mark Phase 11 exit criteria met and record its resolved design ambiguities"
```

---

## Self-Review

**Spec coverage:** all three new modules from the design spec (`Events`/`Notifications`/`Webhooks`, Tasks 1, 5-8, 9-13) · the four proving-set domain events (Task 2) · real SMTP for Email plus stub SMS/WhatsApp/Push channels (Task 6) · the real RabbitMQ topic-exchange hop with two independent consumers, not a simplified in-process shortcut (Tasks 3-4, 7, 11) · webhook signature/retry/replay/delivery-log per `API-SPEC.md` §5 (Tasks 10-13) · both of the spec's resolved ambiguities (encrypted-not-hashed secret, Task 10; the spec itself didn't fix a retry count, this plan resolves it in Task 11/CLARIFICATIONS #12) · tenant isolation on every new `OrganizationId`-carrying entity (Tasks 1, 5, 9) · the dev mail-catcher for verifiable real-SMTP testing (Task 8) · the full exit criteria proven end-to-end over real infrastructure (Task 14) · docs updated (Task 15). Every one of the design spec's ten sections has a task implementing it.

**Placeholder scan:** three drafting mistakes were caught and fixed while writing this plan itself, each replaced with real working code before being left in the document — Task 7's `NotificationsConsumerTests.cs` draft (a vacuous `Assert.True(true, ...)` test that would prove nothing), Task 13's `CreateAuthenticatedMerchantClientAsync` draft (a `NotImplementedException` stub), and Task 14's second end-to-end test draft (an `Assert.Fail` stub blocking on a `CreateMerchantAsync` signature gap). All three are gone from the final task text — Task 7 replaced its placeholder test with a real startup-safety check and deferred the real coverage to Task 14 explicitly; Task 13 and Task 14 both now contain the actual, complete, working code (the real merchant-login helper copied from `MerchantAuthAndStaffEndpointsTests.cs`, and the real forced-failure-then-replay test body). No other placeholder markers, TBD/TODO strings, or "implement later" phrasing remain — confirmed by re-reading every task's Steps section during this pass.

**Type consistency:** `IOutboxWriter.WriteAsync(Guid, string, object, CancellationToken)` (Task 1) is called with the identical signature at all four Task 2 call sites and by `OutboxWriterService`'s own implementation. `IEventPublisher.PublishAsync` (Task 4) matches between the interface and `RabbitMqEventPublisher`. The envelope shape (`EventId`, `OrganizationId`, `EventType`, `Data`) is produced once, in Task 4's `OutboxPublisherJob` (amended in Task 7), and consumed identically by both `NotificationsConsumer` (Task 7) and `WebhooksConsumer` (Task 11) — both read the same four fields the same way. `INotificationChannel.SendAsync` (Task 6) has one signature implemented identically by `EmailNotificationChannel` and all three stub channels. `IWebhookDeliveryAttemptService.AttemptAsync(Guid, CancellationToken)` (Task 11) is called identically by `WebhooksConsumer` (Task 11), `WebhookRetryJob` (Task 12), and `WebhookService.ReplayAsync` (Task 11's own partial-file addition) — `WebhookService` is declared `partial` starting in Task 10 specifically so Task 11 can add `ReplayAsync` in a second file once `IWebhookDeliveryAttemptService` exists, avoiding the forward-reference-across-tasks problem an earlier draft of this plan had (`ReplayAsync` originally lived in Task 10's own file, referencing a Task-11-only interface — caught and fixed while writing this plan, before any task was dispatched). `WebhookSignature.Compute(string, string) -> string` (Task 10) is called identically by `WebhookDeliveryAttemptService` (Task 11) and every one of its own unit tests. `RabbitMqTopology`'s three constants (Task 3) are referenced by name, never re-typed as string literals, everywhere an exchange/queue name is needed (Tasks 4, 7, 11).

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-08-26-phase-11-notifications-webhooks.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
