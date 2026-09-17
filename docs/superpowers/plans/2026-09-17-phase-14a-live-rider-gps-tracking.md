# Phase 14a — Live Rider GPS Tracking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship foreground-only live rider GPS tracking: a rider's position reports automatically while the `/rider` PWA is open, a branch-scoped "Live Map" shows every active rider at a branch, and a delivery run's breadcrumb trail can be pulled for audit/dispute resolution — plus a bundled RBAC fix giving `BranchManager`/`Dispatcher` the `rider.read`/`rider.location.read` grants they were missing.

**Architecture:** One new entity (`RiderLocationPing`, no own `OrganizationId` — isolation flows transitively through `RiderId -> Rider.OrganizationId`, same pattern as `DeliveryTask`), three new endpoints on the existing `Riders` module's route group, a Hangfire retention job, and a corresponding frontend slice (BFF proxy routes, a rider-side reporting hook using plain foreground `watchPosition`, and a staff-side Leaflet map page). No real-time push channel, no native wrapper, no customer-facing tracking change — all confirmed out of scope in the design spec.

**Tech Stack:** ASP.NET Core (Minimal APIs) + EF Core + PostgreSQL (backend, `Courier.Riders` module), Next.js App Router + TanStack Query + Leaflet/react-leaflet (frontend, new dependency).

**Spec:** `docs/superpowers/specs/2026-09-17-phase-14a-live-rider-gps-tracking-design.md`

## Global Constraints

- Foreground-only tracking: the rider hook must stop reporting the instant the tab is hidden and must never queue/replay a ping made while offline (spec §3, §5.1).
- No new permission folded into `rider.read` — `rider.location.read` is separate (spec §7).
- `BranchManager` and `Dispatcher` gain `rider.read` + `rider.location.read`; `HubStaff` deliberately does not (spec §4.2).
- All new endpoints live inside the existing `/api/v1/riders` route group — no new `/api/v1/branches/...` or `/api/v1/delivery-runs/...` groups (spec §4.3, corrected during planning to match this codebase's actual route-nesting convention).
- `RiderLocationPing` has no own `OrganizationId`/query filter — any lookup by a raw `DeliveryRunId` route param must re-resolve the owning `Rider` through a tenant-filtered query before trusting it, treating "no matching Rider" as 404 (spec §4.3).
- Retention: a daily Hangfire job deletes pings older than 30 days (spec §4.4).
- Riders are identified in the UI by `LicenseNumber`, matching every other rider-identifying surface in this app (`Rider` has no display name of its own).
- Decimal, not double/float, for `Latitude`/`Longitude`, with `HasPrecision(9, 6)` — matches `DeliveryAttempt`/`DeliveryProof`.

---

## Task 1: `RiderLocationPing` domain entity, EF configuration, and migration

**Files:**
- Create: `src/Modules/Riders/Domain/RiderLocationPing.cs`
- Create: `src/Modules/Riders/Infrastructure/RiderLocationPingConfiguration.cs`
- Modify: `src/Persistence/AppDbContext.cs` (add `DbSet` + `ApplyConfiguration` + no query filter — matches `DeliveryRun`/`DeliveryTask`, which also have none)
- Create: `src/Persistence/Migrations/<timestamp>_AddRiderLocationPings.cs` (generated)
- Test: `tests/Api.Tests/RidersEndpointsTests.cs` (schema proof folded into Task 3's integration test — this task has no standalone test, matching how `DeliveryTask`'s own migration in Phase 8 had no dedicated schema test)

**Interfaces:**
- Produces: `Courier.Riders.Domain.RiderLocationPing { Guid Id; Guid RiderId; Guid? DeliveryRunId; decimal Latitude; decimal Longitude; decimal? AccuracyMeters; DateTimeOffset RecordedAt; }`

- [ ] **Step 1: Create the domain entity**

```csharp
// src/Modules/Riders/Domain/RiderLocationPing.cs
namespace Courier.Riders.Domain;

// No own OrganizationId — tenant isolation flows transitively through
// RiderId -> Rider.OrganizationId, the same pattern DeliveryTask already established (via
// DeliveryRunId -> DeliveryRun -> RiderId). DeliveryRunId is nullable: a rider can have the PWA
// open with no active run yet (before their first task of the day is assigned).
// RecordedAt is client-reported (the device's own GPS timestamp) — trusted only for ordering
// within a run's breadcrumb, never for anything security- or billing-relevant, matching this
// codebase's posture on other client-reported fields (e.g. DeliveryProof.OtpVerified).
public class RiderLocationPing
{
    public Guid Id { get; set; }
    public Guid RiderId { get; set; }
    public Guid? DeliveryRunId { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal? AccuracyMeters { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

```csharp
// src/Modules/Riders/Infrastructure/RiderLocationPingConfiguration.cs
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Riders.Infrastructure;

public class RiderLocationPingConfiguration : IEntityTypeConfiguration<RiderLocationPing>
{
    public void Configure(EntityTypeBuilder<RiderLocationPing> builder)
    {
        builder.ToTable("RiderLocationPings");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Latitude).HasPrecision(9, 6);
        builder.Property(p => p.Longitude).HasPrecision(9, 6);
        builder.Property(p => p.AccuracyMeters).HasPrecision(9, 2);

        // Covers RiderService.Location.cs's per-rider "latest ping" lookup (Task 3).
        builder.HasIndex(p => new { p.RiderId, p.RecordedAt });
        // Covers the breadcrumb-by-run read (Task 3) and the retention job's age-based delete (Task 4).
        builder.HasIndex(p => p.DeliveryRunId);
        builder.HasIndex(p => p.RecordedAt);

        builder.HasOne<Rider>().WithMany().HasForeignKey(p => p.RiderId).OnDelete(DeleteBehavior.Cascade);
        // SetNull, not Cascade: DeliveryRun rows are never deleted anywhere in this codebase today,
        // but a ping losing its run association (rather than being deleted with it) is the correct
        // semantic if that ever changes — the ping itself is still a real position fact.
        builder.HasOne<DeliveryRun>().WithMany().HasForeignKey(p => p.DeliveryRunId).OnDelete(DeleteBehavior.SetNull);

        // No OrganizationId of its own — tenant isolation flows transitively through
        // RiderId -> Rider.OrganizationId (Rider's own HasQueryFilter already covers it for every
        // lookup that resolves Rider first, per this plan's Global Constraints).
    }
}
```

- [ ] **Step 3: Wire into `AppDbContext`**

In `src/Persistence/AppDbContext.cs`, add the `DbSet` next to the other `Riders` sets (near line 85):

```csharp
    public DbSet<RiderLocationPing> RiderLocationPings => Set<RiderLocationPing>();
```

Add the `ApplyConfiguration` call next to `DeliveryProofConfiguration` (near line 161):

```csharp
        modelBuilder.ApplyConfiguration(new RiderLocationPingConfiguration());
```

Do **not** add a `HasQueryFilter` for `RiderLocationPing` — it has no own `OrganizationId`, matching `DeliveryRun`/`DeliveryTask`.

- [ ] **Step 4: Generate the migration**

Run: `dotnet ef migrations add AddRiderLocationPings --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`

Expected: a new `src/Persistence/Migrations/<timestamp>_AddRiderLocationPings.cs` creating the `RiderLocationPings` table with the three indexes and two foreign keys above, and `AppDbContextModelSnapshot.cs` updated to match.

- [ ] **Step 5: Build**

Run: `dotnet build Courier.slnx`
Expected: build succeeds with no errors.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Riders/Domain/RiderLocationPing.cs src/Modules/Riders/Infrastructure/RiderLocationPingConfiguration.cs src/Persistence/AppDbContext.cs src/Persistence/Migrations/
git commit -m "feat(riders): add RiderLocationPing entity and migration"
```

---

## Task 2: `rider.location.read` permission + `BranchManager`/`Dispatcher` RBAC fix

**Files:**
- Modify: `src/Modules/Identity/Application/PermissionCatalog.cs`
- Modify: `src/Modules/Identity/Application/AdditionalRoleSeeds.cs`
- Create: `src/Persistence/Migrations/<timestamp>_AddRiderLocationReadPermission.cs` (generated)
- Test: `tests/Api.Tests/RidersEndpointsTests.cs` (permission-enforcement tests land in Task 4, which is the first task that actually calls an endpoint gated by this permission)

**Interfaces:**
- Produces: permission key `"rider.location.read"` usable in `.RequirePermission("rider.location.read")` (Tasks 4 and 5).

- [ ] **Step 1: Add the permission to the catalog**

In `src/Modules/Identity/Application/PermissionCatalog.cs`, add directly after the `rider.assign` row (around line 51):

```csharp
        ("rider.location.read", "Read rider live location and breadcrumb history"),
```

- [ ] **Step 2: Grant it to `BranchManager` and `Dispatcher`, alongside the pre-existing `rider.read` gap fix**

In `src/Modules/Identity/Application/AdditionalRoleSeeds.cs`, update the `BranchManager` array (around line 28-33):

```csharp
        // docs/RBAC.md §4's exact BranchManager example, plus a deliberate RBAC fix (this design's
        // spec §4.2): BranchManager never held rider.read at all before this — it's the realistic
        // day-to-day user of the new Live Map, so it gains rider.read and rider.location.read
        // together rather than being left out.
        ["BranchManager"] = new[]
        {
            "shipment.read", "shipment.assign", "shipment.scan",
            "manifest.read", "manifest.create", "manifest.seal", "manifest.dispatch", "manifest.receive", "manifest.close",
            "delivery.assign", "ndr.manage", "ndr.read", "cod.reconcile", "cod.read", "branch.read",
            "rider.read", "rider.location.read"
        },
```

And the `Dispatcher` array (around line 34-38):

```csharp
        // Same rider.read/rider.location.read RBAC fix as BranchManager above — Dispatcher runs
        // day-to-day dispatch and had no visibility into riders at all before this.
        ["Dispatcher"] = new[]
        {
            "manifest.read", "manifest.create", "manifest.seal", "manifest.dispatch", "manifest.receive", "manifest.close",
            "shipment.transfer", "shipment.read", "branch.read",
            "rider.read", "rider.location.read"
        },
```

This is a pure code change — `AdditionalRoleSeeds.ByRoleName` is applied at org-registration time by `AuthService.RegisterOrganizationAsync` (not an EF `HasData` seed), so it needs no migration and only affects newly-registered organizations, consistent with how this catalog has always worked.

- [ ] **Step 3: Generate the migration for the new permission row**

`PermissionCatalog.All` feeds `PermissionConfiguration`'s `HasData` seed (`src/Modules/Identity/Infrastructure/PermissionConfiguration.cs`), so adding a new key requires its own migration — the same requirement Phase 7 hit the moment `manifest.read` was added.

Run: `dotnet ef migrations add AddRiderLocationReadPermission --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`

Expected: a new migration inserting exactly one `Permissions` row (`rider.location.read`), and `AppDbContextModelSnapshot.cs` updated.

- [ ] **Step 4: Build**

Run: `dotnet build Courier.slnx`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Identity/Application/PermissionCatalog.cs src/Modules/Identity/Application/AdditionalRoleSeeds.cs src/Persistence/Migrations/
git commit -m "feat(identity): add rider.location.read permission and grant BranchManager/Dispatcher rider access"
```

---

## Task 3: `POST /api/v1/riders/me/location` — rider self-reporting endpoint

**Files:**
- Modify: `src/Modules/Riders/Application/RiderDtos.cs` (add `LocationPingRequest`)
- Create: `src/Modules/Riders/Application/RiderService.Location.cs`
- Modify: `src/Modules/Riders/Presentation/RidersEndpoints.cs`
- Test: `tests/Api.Tests/RidersEndpointsTests.cs`

**Interfaces:**
- Consumes: `Courier.Riders.Domain.RiderLocationPing` (Task 1); `RiderService`'s existing `_dbContext`, and `ClaimsPrincipal.GetUserId()` extension (`Courier.SharedKernel`, already used by `GetMyRiderAsync`).
- Produces: `public record LocationPingRequest(decimal Latitude, decimal Longitude, decimal? AccuracyMeters, DateTimeOffset RecordedAt);` and `RiderService.RecordLocationPingAsync(LocationPingRequest, ClaimsPrincipal, CancellationToken) : Task<bool>` (`true` = recorded, `false` = caller has no linked `Rider` — mirrors `GetMyRiderAsync`'s null-means-404 idiom). Task 4/5 add sibling methods to the same `RiderService.Location.cs` file.

- [ ] **Step 1: Add the request DTO**

In `src/Modules/Riders/Application/RiderDtos.cs`, add after the existing `RiderScanRequest` (around line 17):

```csharp
public record LocationPingRequest(decimal Latitude, decimal Longitude, decimal? AccuracyMeters, DateTimeOffset RecordedAt);
```

- [ ] **Step 2: Write the failing integration test**

In `tests/Api.Tests/RidersEndpointsTests.cs`, add:

```csharp
[Fact]
public async Task PostLocation_LoggedInAsRider_RecordsPingAndReturns204()
{
    var client = _factory.CreateClient();
    var org = await RegisterOrgAsync(client, "location-ping");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
    var (originBranchId, _, _) = await SeedBranchesAsync(client);
    var suffix = Guid.NewGuid().ToString("N");
    var riderEmail = $"rider-{suffix}@example.com";
    await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
        riderEmail, "P@ssw0rd123!", "Location Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));

    var riderClient = _factory.CreateClient();
    var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmail, "P@ssw0rd123!"));
    var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
    riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);

    var response = await riderClient.PostAsJsonAsync("/api/v1/riders/me/location",
        new LocationPingRequest(27.7172m, 85.3240m, 12.5m, DateTimeOffset.UtcNow));

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
}

[Fact]
public async Task PostLocation_Unauthenticated_Returns401()
{
    var client = _factory.CreateClient();

    var response = await client.PostAsJsonAsync("/api/v1/riders/me/location",
        new LocationPingRequest(27.7172m, 85.3240m, null, DateTimeOffset.UtcNow));

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "PostLocation_LoggedInAsRider_RecordsPingAndReturns204|PostLocation_Unauthenticated_Returns401"`
Expected: `PostLocation_LoggedInAsRider_RecordsPingAndReturns204` fails with a 404 (route doesn't exist yet); `PostLocation_Unauthenticated_Returns401` passes trivially (any unmapped route returns 404, not 401 — note this in the run, it's expected to also fail until the route exists and `RequireAuthorization()` applies).

- [ ] **Step 4: Implement `RiderService.Location.cs`**

```csharp
// src/Modules/Riders/Application/RiderService.Location.cs
using System.Security.Claims;
using Courier.Riders.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

public partial class RiderService
{
    // Self-scoped, same idiom as GetMyRiderAsync: false means "no Rider linked to this JWT",
    // mapped to 404 at the route (Step 5) rather than a thrown exception, since this isn't
    // invalid input — it's the same "not found" shape GetMyRiderAsync already established.
    public async Task<bool> RecordLocationPingAsync(LocationPingRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var userId = actingUser.GetUserId();
        if (userId is null)
        {
            return false;
        }
        var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);
        if (rider is null)
        {
            return false;
        }

        // Same find-today's-Active-run lookup GetMyTasksTodayAsync already uses — a ping made
        // before the rider's first task of the day is assigned simply has no run yet.
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);
        var activeRun = await _dbContext.Set<DeliveryRun>().FirstOrDefaultAsync(
            r => r.RiderId == rider.Id && r.Date == today && r.Status == DeliveryRunStatus.Active, cancellationToken);

        var ping = new RiderLocationPing
        {
            Id = Guid.NewGuid(),
            RiderId = rider.Id,
            DeliveryRunId = activeRun?.Id,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            AccuracyMeters = request.AccuracyMeters,
            RecordedAt = request.RecordedAt
        };
        _dbContext.Set<RiderLocationPing>().Add(ping);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
```

- [ ] **Step 5: Map the endpoint**

In `src/Modules/Riders/Presentation/RidersEndpoints.cs`, add directly after the `riders.MapGet("/me/tasks/today", ...)` mapping (around line 59):

```csharp
        riders.MapPost("/me/location", async (LocationPingRequest request, RiderService service, HttpContext httpContext) =>
        {
            var recorded = await service.RecordLocationPingAsync(request, httpContext.User, httpContext.RequestAborted);
            return recorded ? Results.NoContent() : Results.NotFound();
        });
```

No `.RequirePermission(...)` call — matches `/me` and `/me/tasks/today` immediately above it, which authorize "is this JWT's subject a rider" rather than a permission grant. The group-level `.RequireAuthorization()` still applies, so an unauthenticated caller gets 401.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "PostLocation_LoggedInAsRider_RecordsPingAndReturns204|PostLocation_Unauthenticated_Returns401"`
Expected: both PASS.

- [ ] **Step 7: Build**

Run: `dotnet build Courier.slnx`
Expected: build succeeds.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Riders/Application/RiderDtos.cs src/Modules/Riders/Application/RiderService.Location.cs src/Modules/Riders/Presentation/RidersEndpoints.cs tests/Api.Tests/RidersEndpointsTests.cs
git commit -m "feat(riders): add POST /api/v1/riders/me/location self-reporting endpoint"
```

---

## Task 4: `GET /api/v1/riders/branches/{branchId}/locations` — branch-scoped live positions

**Files:**
- Modify: `src/Modules/Riders/Application/RiderDtos.cs` (add `RiderLocationResponse`)
- Modify: `src/Modules/Riders/Application/RiderService.Location.cs`
- Modify: `src/Modules/Riders/Presentation/RidersEndpoints.cs`
- Test: `tests/Api.Tests/RidersEndpointsTests.cs`

**Interfaces:**
- Consumes: `RiderService.RecordLocationPingAsync` (Task 3, for seeding a ping in tests); `CheckBranchAccessAsync(ClaimsPrincipal, string permission, Guid branchId, CancellationToken)` (existing private helper in `RiderService.cs`); the `rider.location.read` permission (Task 2).
- Produces: `public record RiderLocationResponse(Guid RiderId, decimal Latitude, decimal Longitude, decimal? AccuracyMeters, DateTimeOffset RecordedAt);` and `RiderService.GetBranchLocationsAsync(Guid branchId, ClaimsPrincipal, CancellationToken) : Task<List<RiderLocationResponse>>`. Task 5 reuses `RiderLocationResponse`.

- [ ] **Step 1: Add the response DTO**

In `src/Modules/Riders/Application/RiderDtos.cs`, add next to `LocationPingRequest`:

```csharp
public record RiderLocationResponse(Guid RiderId, decimal Latitude, decimal Longitude, decimal? AccuracyMeters, DateTimeOffset RecordedAt);
```

- [ ] **Step 2: Write the failing integration tests**

Add to `tests/Api.Tests/RidersEndpointsTests.cs`:

```csharp
[Fact]
public async Task GetBranchLocations_AfterPing_ReturnsLatestPositionForActiveRider()
{
    var client = _factory.CreateClient();
    var org = await RegisterOrgAsync(client, "branch-locations");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
    var (originBranchId, _, _) = await SeedBranchesAsync(client);
    var suffix = Guid.NewGuid().ToString("N");
    var riderEmail = $"rider-{suffix}@example.com";
    var createResponse = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
        riderEmail, "P@ssw0rd123!", "Branch Map Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));
    var createdRider = await createResponse.Content.ReadFromJsonAsync<RiderResponse>();

    var riderClient = _factory.CreateClient();
    var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmail, "P@ssw0rd123!"));
    var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
    riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);
    await riderClient.PostAsJsonAsync("/api/v1/riders/me/location", new LocationPingRequest(27.7172m, 85.3240m, 10m, DateTimeOffset.UtcNow));

    var response = await client.GetAsync($"/api/v1/riders/branches/{originBranchId}/locations");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var locations = await response.Content.ReadFromJsonAsync<List<RiderLocationResponse>>();
    var location = Assert.Single(locations!, l => l.RiderId == createdRider!.Id);
    Assert.Equal(27.7172m, location.Latitude);
}

[Fact]
public async Task GetBranchLocations_OrgBBranch_Returns400()
{
    var clientA = _factory.CreateClient();
    var orgA = await RegisterOrgAsync(clientA, "branch-locations-iso-a");
    clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
    var (branchIdA, _, _) = await SeedBranchesAsync(clientA);

    var clientB = _factory.CreateClient();
    var orgB = await RegisterOrgAsync(clientB, "branch-locations-iso-b");
    clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

    var response = await clientB.GetAsync($"/api/v1/riders/branches/{branchIdA}/locations");

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task GetBranchLocations_RoleWithoutPermission_Returns403()
{
    var client = _factory.CreateClient();
    var org = await RegisterOrgAsync(client, "branch-locations-perm");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
    var (originBranchId, _, _) = await SeedBranchesAsync(client);
    // SortingStaff holds neither rider.read nor rider.location.read (AdditionalRoleSeeds.cs) —
    // the negative case for the RBAC fix in Task 2.
    var token = await InviteAcceptAndLoginForBranchAsync(client, "SortingStaff", originBranchId);
    var sortingStaffClient = _factory.CreateClient();
    sortingStaffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var response = await sortingStaffClient.GetAsync($"/api/v1/riders/branches/{originBranchId}/locations");

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}

[Fact]
public async Task GetBranchLocations_BranchManager_NowHasAccess()
{
    // Proves Task 2's RBAC fix actually took effect end-to-end, not just declared in the seed array.
    var client = _factory.CreateClient();
    var org = await RegisterOrgAsync(client, "branch-locations-bm");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
    var (originBranchId, _, _) = await SeedBranchesAsync(client);
    var token = await InviteAcceptAndLoginForBranchAsync(client, "BranchManager", originBranchId);
    var branchManagerClient = _factory.CreateClient();
    branchManagerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var response = await branchManagerClient.GetAsync($"/api/v1/riders/branches/{originBranchId}/locations");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}

// Invites, accepts, and logs in a user under roleName scoped to the given branchId — same
// invite/accept-invite/login chain as OrganizationSettingsEndpointsTests.InviteAcceptAndLoginAsync,
// adapted to take an explicit branchId (that test's version always creates a fresh branch, but
// branch-scope tests here need the invited user scoped to the SAME branch as the rider/location
// under test).
private async Task<string> InviteAcceptAndLoginForBranchAsync(HttpClient ownerClient, string roleName, Guid branchId)
{
    var roles = await (await ownerClient.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
    var role = roles!.Single(r => r.Name == roleName);
    var email = $"staff-{Guid.NewGuid():N}@example.com";
    var password = "St@ffPassword1";

    var inviteResponse = await ownerClient.PostAsJsonAsync("/api/v1/users/invite",
        new InviteUserRequest(email, "Staff Person", null, role.Id, branchId, null));
    inviteResponse.EnsureSuccessStatusCode();

    using var scope = _factory.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var outboxMessage = await dbContext.Set<OutboxMessage>().IgnoreQueryFilters()
        .Where(m => m.EventType == "user.invited")
        .OrderByDescending(m => m.CreatedAt)
        .FirstAsync(m => m.Payload.Contains(email));
    var payload = System.Text.Json.JsonDocument.Parse(outboxMessage.Payload).RootElement;
    var inviteUrl = payload.GetProperty("InviteUrl").GetString()!;
    var token = Uri.UnescapeDataString(inviteUrl.Split("token=")[1]);

    var freshClient = _factory.CreateClient();
    var acceptResponse = await freshClient.PostAsJsonAsync("/api/v1/auth/accept-invite", new AcceptInviteRequest(token, password));
    acceptResponse.EnsureSuccessStatusCode();
    var acceptResult = await acceptResponse.Content.ReadFromJsonAsync<AuthResult>();
    return acceptResult!.AccessToken;
}
```

Add these `using` directives to the top of `tests/Api.Tests/RidersEndpointsTests.cs` (not already present in this file): `Courier.Events.Domain`, `Courier.Identity.Presentation`, `Courier.Persistence`, `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.DependencyInjection`.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "GetBranchLocations_AfterPing_ReturnsLatestPositionForActiveRider|GetBranchLocations_OrgBBranch_Returns400|GetBranchLocations_RoleWithoutPermission_Returns403|GetBranchLocations_BranchManager_NowHasAccess"`
Expected: all FAIL (route doesn't exist yet — 404s).

- [ ] **Step 4: Implement `GetBranchLocationsAsync`**

Append to `src/Modules/Riders/Application/RiderService.Location.cs`:

```csharp
    // Per-rider latest-ping lookup — deliberately NOT a GroupBy: EF Core/Npgsql does not reliably
    // translate "GroupBy then OrderBy().First() per group" to SQL (CodService.Reads.cs hit the
    // same class of problem and resolved it by materializing a bounded set client-side; here the
    // bounded set is "riders at one branch," a small dispatch-sized number, so N small indexed
    // per-rider queries is simpler and provably correct). Sequential awaits on the shared
    // DbContext, same reasoning as GetMyTasksTodayAsync.
    public async Task<List<RiderLocationResponse>> GetBranchLocationsAsync(Guid branchId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        await CheckBranchAccessAsync(actingUser, "rider.location.read", branchId, cancellationToken);

        // Scoped by BaseBranchId, matching RiderReportingLookupService.GetBranchRiderTaskLoadAsync's
        // established convention for "riders at this branch" — not the active run's branch.
        var riderIds = await _dbContext.Set<Rider>().AsNoTracking()
            .Where(r => r.BaseBranchId == branchId && r.IsActive)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var results = new List<RiderLocationResponse>();
        foreach (var riderId in riderIds)
        {
            var latest = await _dbContext.Set<RiderLocationPing>().AsNoTracking()
                .Where(p => p.RiderId == riderId)
                .OrderByDescending(p => p.RecordedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (latest is not null)
            {
                results.Add(new RiderLocationResponse(riderId, latest.Latitude, latest.Longitude, latest.AccuracyMeters, latest.RecordedAt));
            }
        }
        return results;
    }
```

- [ ] **Step 5: Map the endpoint**

In `src/Modules/Riders/Presentation/RidersEndpoints.cs`, add after the `/me/location` mapping from Task 3:

```csharp
        riders.MapGet("/branches/{branchId:guid}/locations", async (Guid branchId, RiderService service, HttpContext httpContext) =>
            await HandleAsync(() => service.GetBranchLocationsAsync(branchId, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("rider.location.read");
```

This reuses the existing `HandleAsync` wrapper (already in this file) so a `CheckBranchAccessAsync` rejection (thrown as `RiderValidationException`) maps to 400, matching every other branch-scope-outside-access case in this codebase (e.g. `ReportsEndpointsTests.BranchDashboard_BranchFromAnotherOrganization_ReturnsReportsValidationFailed`).

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "GetBranchLocations_AfterPing_ReturnsLatestPositionForActiveRider|GetBranchLocations_OrgBBranch_Returns400|GetBranchLocations_RoleWithoutPermission_Returns403|GetBranchLocations_BranchManager_NowHasAccess"`
Expected: all PASS.

- [ ] **Step 7: Build**

Run: `dotnet build Courier.slnx`
Expected: build succeeds.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Riders/Application/RiderDtos.cs src/Modules/Riders/Application/RiderService.Location.cs src/Modules/Riders/Presentation/RidersEndpoints.cs tests/Api.Tests/RidersEndpointsTests.cs
git commit -m "feat(riders): add GET /api/v1/riders/branches/{branchId}/locations live map endpoint"
```

---

## Task 5: `GET /api/v1/riders/runs/{runId}/breadcrumbs` — per-run breadcrumb trail

**Files:**
- Modify: `src/Modules/Riders/Application/RiderService.Location.cs`
- Modify: `src/Modules/Riders/Presentation/RidersEndpoints.cs`
- Test: `tests/Api.Tests/RidersEndpointsTests.cs`

**Interfaces:**
- Consumes: `RiderLocationResponse` (Task 4); `CheckBranchAccessAsync` (existing helper).
- Produces: `RiderService.GetRunBreadcrumbsAsync(Guid runId, ClaimsPrincipal, CancellationToken) : Task<List<RiderLocationResponse>?>` (`null` = run not found, or its owning `Rider` isn't visible under the caller's tenant — both map to 404).

- [ ] **Step 1: Write the failing integration tests**

Add to `tests/Api.Tests/RidersEndpointsTests.cs`:

```csharp
[Fact]
public async Task GetRunBreadcrumbs_TwoPingsSameRun_ReturnsBothOrderedByTime()
{
    var client = _factory.CreateClient();
    var org = await RegisterOrgAsync(client, "breadcrumbs");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
    var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
    var merchantId = await CreateMerchantAsync(client);
    var suffix = Guid.NewGuid().ToString("N");
    var riderEmail = $"rider-{suffix}@example.com";
    await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
        riderEmail, "P@ssw0rd123!", "Breadcrumb Rider", null, $"LIC-{suffix[..10]}", originBranchId, RiderRoleType.PickupRider));
    var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();

    var riderClient = _factory.CreateClient();
    var loginResponse = await riderClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmail, "P@ssw0rd123!"));
    var riderAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
    riderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuth!.AccessToken);

    // AssignTaskAsync (Task 8, Phase 8) auto-creates today's DeliveryRun — the only way to get a
    // real DeliveryRunId to attach pings to, matching how every other Riders test reaches a run.
    var riderRecord = await (await riderClient.GetAsync("/api/v1/riders/me")).Content.ReadFromJsonAsync<RiderResponse>();
    await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(riderRecord!.Id, shipment!.Id, DeliveryTaskType.Pickup));
    var tasksToday = await (await riderClient.GetAsync("/api/v1/riders/me/tasks/today")).Content.ReadFromJsonAsync<MyTasksTodayResponse>();
    var runId = tasksToday!.DeliveryRunId!.Value;

    await riderClient.PostAsJsonAsync("/api/v1/riders/me/location", new LocationPingRequest(27.70m, 85.30m, null, DateTimeOffset.UtcNow.AddMinutes(-2)));
    await riderClient.PostAsJsonAsync("/api/v1/riders/me/location", new LocationPingRequest(27.72m, 85.32m, null, DateTimeOffset.UtcNow));

    var response = await client.GetAsync($"/api/v1/riders/runs/{runId}/breadcrumbs");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var breadcrumbs = await response.Content.ReadFromJsonAsync<List<RiderLocationResponse>>();
    Assert.Equal(2, breadcrumbs!.Count);
    Assert.Equal(27.70m, breadcrumbs[0].Latitude);
    Assert.Equal(27.72m, breadcrumbs[1].Latitude);
}

[Fact]
public async Task GetRunBreadcrumbs_RunFromAnotherOrganization_Returns404()
{
    var clientA = _factory.CreateClient();
    var orgA = await RegisterOrgAsync(clientA, "breadcrumbs-iso-a");
    clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
    var (originBranchIdA, destinationBranchIdA, serviceTypeIdA) = await SeedBranchesAsync(clientA);
    var merchantIdA = await CreateMerchantAsync(clientA);
    var suffixA = Guid.NewGuid().ToString("N");
    var riderEmailA = $"rider-{suffixA}@example.com";
    await clientA.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
        riderEmailA, "P@ssw0rd123!", "Org A Rider", null, $"LIC-{suffixA[..10]}", originBranchIdA, RiderRoleType.PickupRider));
    var shipmentA = await (await clientA.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantIdA, originBranchIdA, destinationBranchIdA, serviceTypeIdA))).Content.ReadFromJsonAsync<ShipmentResponse>();
    var riderClientA = _factory.CreateClient();
    var loginA = await riderClientA.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(riderEmailA, "P@ssw0rd123!"));
    var riderAuthA = await loginA.Content.ReadFromJsonAsync<AuthResult>();
    riderClientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", riderAuthA!.AccessToken);
    var riderRecordA = await (await riderClientA.GetAsync("/api/v1/riders/me")).Content.ReadFromJsonAsync<RiderResponse>();
    await clientA.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(riderRecordA!.Id, shipmentA!.Id, DeliveryTaskType.Pickup));
    var tasksTodayA = await (await riderClientA.GetAsync("/api/v1/riders/me/tasks/today")).Content.ReadFromJsonAsync<MyTasksTodayResponse>();
    var runIdA = tasksTodayA!.DeliveryRunId!.Value;

    var clientB = _factory.CreateClient();
    var orgB = await RegisterOrgAsync(clientB, "breadcrumbs-iso-b");
    clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

    var response = await clientB.GetAsync($"/api/v1/riders/runs/{runIdA}/breadcrumbs");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}

[Fact]
public async Task GetRunBreadcrumbs_UnknownRunId_Returns404()
{
    var client = _factory.CreateClient();
    var org = await RegisterOrgAsync(client, "breadcrumbs-unknown");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

    var response = await client.GetAsync($"/api/v1/riders/runs/{Guid.NewGuid()}/breadcrumbs");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "GetRunBreadcrumbs_TwoPingsSameRun_ReturnsBothOrderedByTime|GetRunBreadcrumbs_RunFromAnotherOrganization_Returns404|GetRunBreadcrumbs_UnknownRunId_Returns404"`
Expected: all FAIL (route doesn't exist yet).

- [ ] **Step 3: Implement `GetRunBreadcrumbsAsync`**

Append to `src/Modules/Riders/Application/RiderService.Location.cs`:

```csharp
    // DeliveryRun carries no own OrganizationId/query filter (Task 1's design note), so a raw
    // lookup by runId is NOT tenant-safe on its own — the owning Rider must be re-resolved through
    // a tenant-filtered query before trusting run.BranchId, the same way GetMyTasksTodayAsync
    // resolves Rider before ever touching DeliveryRun/DeliveryTask. Returning null for "run exists
    // but its Rider isn't visible in this tenant" (rather than a distinct error) never leaks that
    // a run with this id exists in another organization.
    public async Task<List<RiderLocationResponse>?> GetRunBreadcrumbsAsync(Guid runId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var run = await _dbContext.Set<DeliveryRun>().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return null;
        }
        var rider = await _dbContext.Set<Rider>().FirstOrDefaultAsync(r => r.Id == run.RiderId, cancellationToken);
        if (rider is null)
        {
            return null;
        }

        await CheckBranchAccessAsync(actingUser, "rider.location.read", run.BranchId, cancellationToken);

        var pings = await _dbContext.Set<RiderLocationPing>().AsNoTracking()
            .Where(p => p.DeliveryRunId == runId)
            .OrderBy(p => p.RecordedAt)
            .ToListAsync(cancellationToken);

        return pings.Select(p => new RiderLocationResponse(p.RiderId, p.Latitude, p.Longitude, p.AccuracyMeters, p.RecordedAt)).ToList();
    }
```

- [ ] **Step 4: Map the endpoint**

In `src/Modules/Riders/Presentation/RidersEndpoints.cs`, add after the `/branches/{branchId:guid}/locations` mapping from Task 4:

```csharp
        riders.MapGet("/runs/{runId:guid}/breadcrumbs", async (Guid runId, RiderService service, HttpContext httpContext) =>
        {
            var result = await service.GetRunBreadcrumbsAsync(runId, httpContext.User, httpContext.RequestAborted);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequirePermission("rider.location.read");
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "GetRunBreadcrumbs_TwoPingsSameRun_ReturnsBothOrderedByTime|GetRunBreadcrumbs_RunFromAnotherOrganization_Returns404|GetRunBreadcrumbs_UnknownRunId_Returns404"`
Expected: all PASS.

- [ ] **Step 6: Build**

Run: `dotnet build Courier.slnx`
Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Riders/Application/RiderService.Location.cs src/Modules/Riders/Presentation/RidersEndpoints.cs tests/Api.Tests/RidersEndpointsTests.cs
git commit -m "feat(riders): add GET /api/v1/riders/runs/{runId}/breadcrumbs endpoint"
```

---

## Task 6: Retention — daily Hangfire job pruning old pings

**Files:**
- Create: `src/Modules/Riders/Application/RiderLocationPingRetentionJob.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Riders.Tests/RiderLocationPingRetentionJobTests.cs`

**Interfaces:**
- Consumes: `Courier.Riders.Domain.RiderLocationPing` (Task 1).
- Produces: `Courier.Riders.Application.RiderLocationPingRetentionJob.RunAsync(CancellationToken) : Task`.

- [ ] **Step 1: Write the failing unit tests**

```csharp
// tests/Riders.Tests/RiderLocationPingRetentionJobTests.cs
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Riders.Tests;

public class RiderLocationPingRetentionJobTests
{
    private sealed class NullTenantProvider : ICurrentTenantProvider { public Guid? OrganizationId => null; }

    [Fact]
    public async Task RunAsync_PingOlderThan30Days_IsDeleted()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var riderId = Guid.NewGuid();
        var oldPing = new RiderLocationPing { Id = Guid.NewGuid(), RiderId = riderId, Latitude = 27.7m, Longitude = 85.3m, RecordedAt = DateTimeOffset.UtcNow.AddDays(-31) };
        dbContext.Set<RiderLocationPing>().Add(oldPing);
        await dbContext.SaveChangesAsync();
        var job = new RiderLocationPingRetentionJob(dbContext);

        await job.RunAsync();

        var remaining = dbContext.Set<RiderLocationPing>().Where(p => p.Id == oldPing.Id).ToList();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task RunAsync_PingWithinRetentionWindow_IsKept()
    {
        using var dbContext = TestDbContextFactory.Create(new NullTenantProvider());
        var riderId = Guid.NewGuid();
        var recentPing = new RiderLocationPing { Id = Guid.NewGuid(), RiderId = riderId, Latitude = 27.7m, Longitude = 85.3m, RecordedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        dbContext.Set<RiderLocationPing>().Add(recentPing);
        await dbContext.SaveChangesAsync();
        var job = new RiderLocationPingRetentionJob(dbContext);

        await job.RunAsync();

        var remaining = dbContext.Set<RiderLocationPing>().Where(p => p.Id == recentPing.Id).ToList();
        Assert.Single(remaining);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj --filter RiderLocationPingRetentionJobTests`
Expected: FAIL with a compile error (`RiderLocationPingRetentionJob` doesn't exist yet).

- [ ] **Step 3: Implement the job**

```csharp
// src/Modules/Riders/Application/RiderLocationPingRetentionJob.cs
using Courier.Riders.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Riders.Application;

// Hangfire recurring job, same shape as Billing's TrialExpiryJob (no ambient tenant). Unlike that
// job, RiderLocationPing has no query filter to bypass at all (Task 1's design note), so no
// IgnoreQueryFilters() call is needed here — this simply deletes globally by age across every org.
public class RiderLocationPingRetentionJob
{
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(30);

    private readonly DbContext _dbContext;

    public RiderLocationPingRetentionJob(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - RetentionWindow;
        var expiredPings = await _dbContext.Set<RiderLocationPing>()
            .Where(p => p.RecordedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (expiredPings.Count == 0)
        {
            return;
        }

        _dbContext.Set<RiderLocationPing>().RemoveRange(expiredPings);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Riders.Tests/Riders.Tests.csproj --filter RiderLocationPingRetentionJobTests`
Expected: both PASS.

- [ ] **Step 5: Register the job in `Program.cs`**

In `src/Api/Program.cs`, add near the other `AddScoped<...Job>()` registrations (near line 187):

```csharp
builder.Services.AddScoped<Courier.Riders.Application.RiderLocationPingRetentionJob>();
```

And near the other `RecurringJob.AddOrUpdate` calls (near line 446), running once daily at an off-peak hour distinct from the existing billing jobs:

```csharp
RecurringJob.AddOrUpdate<Courier.Riders.Application.RiderLocationPingRetentionJob>("rider-location-ping-retention", job => job.RunAsync(CancellationToken.None), "0 4 * * *");
```

- [ ] **Step 6: Build**

Run: `dotnet build Courier.slnx`
Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Riders/Application/RiderLocationPingRetentionJob.cs src/Api/Program.cs tests/Riders.Tests/RiderLocationPingRetentionJobTests.cs
git commit -m "feat(riders): add daily retention job for RiderLocationPing rows older than 30 days"
```

---

## Task 7: Frontend BFF proxy routes

**Files:**
- Create: `src/web/src/app/api/riders/me/location/route.ts`
- Create: `src/web/src/app/api/riders/branches/[branchId]/locations/route.ts`
- Create: `src/web/src/app/api/riders/runs/[runId]/breadcrumbs/route.ts`
- Test: `src/web/src/app/api/riders/me/location/route.test.ts`
- Test: `src/web/src/app/api/riders/branches/[branchId]/locations/route.test.ts`
- Test: `src/web/src/app/api/riders/runs/[runId]/breadcrumbs/route.test.ts`

**Interfaces:**
- Consumes: `apiFetch` (`@/lib/auth/apiFetch`, existing).
- Produces: `/api/riders/me/location` (POST), `/api/riders/branches/{branchId}/locations` (GET), `/api/riders/runs/{runId}/breadcrumbs` (GET) — the exact paths Tasks 8 and 9's frontend code call.

- [ ] **Step 1: Write the failing route tests**

```typescript
// src/web/src/app/api/riders/me/location/route.test.ts
import { describe, expect, it, vi } from "vitest";
import { POST } from "./route";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

describe("POST /api/riders/me/location", () => {
  it("forwards the ping to the backend and returns its status with no body", async () => {
    const { apiFetch } = await import("@/lib/auth/apiFetch");
    vi.mocked(apiFetch).mockResolvedValue(new Response(null, { status: 204 }));

    const request = new Request("http://localhost/api/riders/me/location", {
      method: "POST",
      body: JSON.stringify({ latitude: 27.7, longitude: 85.3, accuracyMeters: null, recordedAt: "2026-09-17T00:00:00Z" }),
    });
    const response = await POST(request);

    expect(response.status).toBe(204);
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/me/location", expect.objectContaining({ method: "POST" }));
  });

  it("returns 401 when apiFetch throws", async () => {
    const { apiFetch } = await import("@/lib/auth/apiFetch");
    vi.mocked(apiFetch).mockRejectedValue(new Error("no session"));

    const request = new Request("http://localhost/api/riders/me/location", { method: "POST", body: "{}" });
    const response = await POST(request);

    expect(response.status).toBe(401);
  });
});
```

```typescript
// src/web/src/app/api/riders/branches/[branchId]/locations/route.test.ts
import { describe, expect, it, vi } from "vitest";
import { GET } from "./route";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

describe("GET /api/riders/branches/[branchId]/locations", () => {
  it("forwards to the backend branch-locations endpoint", async () => {
    const { apiFetch } = await import("@/lib/auth/apiFetch");
    vi.mocked(apiFetch).mockResolvedValue(
      new Response(JSON.stringify([{ riderId: "r1", latitude: 27.7, longitude: 85.3, accuracyMeters: null, recordedAt: "2026-09-17T00:00:00Z" }]), { status: 200 }),
    );

    const response = await GET(new Request("http://localhost/api/riders/branches/b1/locations"), { params: { branchId: "b1" } });

    expect(response.status).toBe(200);
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/branches/b1/locations");
    const body = await response.json();
    expect(body[0].riderId).toBe("r1");
  });
});
```

```typescript
// src/web/src/app/api/riders/runs/[runId]/breadcrumbs/route.test.ts
import { describe, expect, it, vi } from "vitest";
import { GET } from "./route";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

describe("GET /api/riders/runs/[runId]/breadcrumbs", () => {
  it("forwards to the backend breadcrumbs endpoint", async () => {
    const { apiFetch } = await import("@/lib/auth/apiFetch");
    vi.mocked(apiFetch).mockResolvedValue(new Response(JSON.stringify([]), { status: 200 }));

    const response = await GET(new Request("http://localhost/api/riders/runs/run1/breadcrumbs"), { params: { runId: "run1" } });

    expect(response.status).toBe(200);
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/runs/run1/breadcrumbs");
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npm test -- src/app/api/riders/me/location/route.test.ts src/app/api/riders/branches/route.test.ts src/app/api/riders/runs/route.test.ts` (run from `src/web`)
Expected: FAIL — the `route.ts` files don't exist yet.

- [ ] **Step 3: Implement the POST proxy route**

```typescript
// src/web/src/app/api/riders/me/location/route.ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/riders/me/location", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    return new NextResponse(null, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 4: Implement the branch-locations GET proxy route**

```typescript
// src/web/src/app/api/riders/branches/[branchId]/locations/route.ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { branchId: string } }) {
  try {
    const response = await apiFetch(`/api/v1/riders/branches/${params.branchId}/locations`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 5: Implement the breadcrumbs GET proxy route**

```typescript
// src/web/src/app/api/riders/runs/[runId]/breadcrumbs/route.ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { runId: string } }) {
  try {
    const response = await apiFetch(`/api/v1/riders/runs/${params.runId}/breadcrumbs`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `npm test -- src/app/api/riders/me/location/route.test.ts src/app/api/riders/branches/route.test.ts src/app/api/riders/runs/route.test.ts` (run from `src/web`)
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/app/api/riders/me/location/ src/web/src/app/api/riders/branches/ src/web/src/app/api/riders/runs/
git commit -m "feat(web): add BFF proxy routes for rider location reporting and reads"
```

---

## Task 8: Rider PWA — foreground location-reporting hook

**Files:**
- Create: `src/web/src/features/rider-app/location/useLocationReporting.ts`
- Test: `src/web/src/features/rider-app/location/useLocationReporting.test.ts`
- Modify: `src/web/src/app/rider/layout.tsx`

**Interfaces:**
- Consumes: `/api/riders/me/location` (Task 7).
- Produces: `useLocationReporting(): void` — a side-effect-only hook, mounted once in the rider PWA's layout so it runs across every rider screen.

- [ ] **Step 1: Write the failing test**

```typescript
// src/web/src/features/rider-app/location/useLocationReporting.test.ts
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { renderHook } from "@testing-library/react";
import { useLocationReporting } from "./useLocationReporting";

describe("useLocationReporting", () => {
  let watchPositionMock: ReturnType<typeof vi.fn>;
  let clearWatchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    watchPositionMock = vi.fn().mockReturnValue(1);
    clearWatchMock = vi.fn();
    Object.defineProperty(global.navigator, "geolocation", {
      value: { watchPosition: watchPositionMock, clearWatch: clearWatchMock },
      configurable: true,
    });
    Object.defineProperty(document, "visibilityState", { value: "visible", configurable: true });
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("starts watching position when mounted with the tab visible", () => {
    renderHook(() => useLocationReporting());

    expect(watchPositionMock).toHaveBeenCalledTimes(1);
  });

  it("stops watching when the tab becomes hidden and restarts when visible again", () => {
    renderHook(() => useLocationReporting());
    expect(watchPositionMock).toHaveBeenCalledTimes(1);

    Object.defineProperty(document, "visibilityState", { value: "hidden", configurable: true });
    document.dispatchEvent(new Event("visibilitychange"));
    expect(clearWatchMock).toHaveBeenCalledWith(1);

    Object.defineProperty(document, "visibilityState", { value: "visible", configurable: true });
    document.dispatchEvent(new Event("visibilitychange"));
    expect(watchPositionMock).toHaveBeenCalledTimes(2);
  });

  it("posts a ping to the BFF route when a position arrives", () => {
    renderHook(() => useLocationReporting());
    const onPosition = watchPositionMock.mock.calls[0][0] as (position: GeolocationPosition) => void;

    onPosition({
      coords: { latitude: 27.7, longitude: 85.3, accuracy: 10 } as GeolocationCoordinates,
      timestamp: Date.now(),
    } as GeolocationPosition);

    expect(global.fetch).toHaveBeenCalledWith(
      "/api/riders/me/location",
      expect.objectContaining({ method: "POST" }),
    );
  });

  it("stops watching on unmount", () => {
    const { unmount } = renderHook(() => useLocationReporting());
    unmount();

    expect(clearWatchMock).toHaveBeenCalledWith(1);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `src/web`): `npm test -- src/features/rider-app/location/useLocationReporting.test.ts`
Expected: FAIL — `useLocationReporting.ts` doesn't exist yet.

- [ ] **Step 3: Implement the hook**

```typescript
// src/web/src/features/rider-app/location/useLocationReporting.ts
"use client";

import { useEffect, useRef } from "react";

const REPORT_INTERVAL_MS = 20_000;

// Foreground-only by construction (spec §3, §5.1): watchPosition only runs while the tab is
// visible, and a ping made while offline is simply dropped, never queued — a stale replayed
// position would show a wrong "current" location, which is worse than a missing one. Uses plain
// fetch (not clientFetch/useMutation): this is a fire-and-forget side effect with no UI state to
// react to, and clientFetch's unconditional response.json() call would throw on the backend's
// 204 No Content response.
export function useLocationReporting() {
  const watchIdRef = useRef<number | null>(null);
  const lastSentAtRef = useRef(0);

  useEffect(() => {
    function sendPing(position: GeolocationPosition) {
      const now = Date.now();
      if (now - lastSentAtRef.current < REPORT_INTERVAL_MS) {
        return;
      }
      lastSentAtRef.current = now;
      fetch("/api/riders/me/location", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          latitude: position.coords.latitude,
          longitude: position.coords.longitude,
          accuracyMeters: position.coords.accuracy,
          recordedAt: new Date(position.timestamp).toISOString(),
        }),
      }).catch(() => {
        // A dropped ping just means a slightly staler position — never surfaced to the rider.
      });
    }

    function startWatch() {
      if (watchIdRef.current !== null || typeof navigator === "undefined" || !("geolocation" in navigator)) {
        return;
      }
      watchIdRef.current = navigator.geolocation.watchPosition(sendPing, () => {}, {
        enableHighAccuracy: false,
        maximumAge: 15_000,
      });
    }

    function stopWatch() {
      if (watchIdRef.current !== null) {
        navigator.geolocation.clearWatch(watchIdRef.current);
        watchIdRef.current = null;
      }
    }

    function handleVisibilityChange() {
      if (document.visibilityState === "visible") {
        startWatch();
      } else {
        stopWatch();
      }
    }

    if (document.visibilityState === "visible") {
      startWatch();
    }
    document.addEventListener("visibilitychange", handleVisibilityChange);
    return () => {
      document.removeEventListener("visibilitychange", handleVisibilityChange);
      stopWatch();
    };
  }, []);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run (from `src/web`): `npm test -- src/features/rider-app/location/useLocationReporting.test.ts`
Expected: all PASS.

- [ ] **Step 5: Mount the hook in the rider layout**

Read `src/web/src/app/rider/layout.tsx` first to see its current structure, then add a single line calling `useLocationReporting()` inside the layout component's body (mark the layout `"use client"` if it isn't already — Phase F12's layout already registers a service worker client-side, so it should already be a client component).

- [ ] **Step 6: Build**

Run (from `src/web`): `npm run build`
Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/rider-app/location/ src/web/src/app/rider/layout.tsx
git commit -m "feat(web): report rider location in the foreground from the rider PWA"
```

---

## Task 9: Staff web — Live Map page

**Files:**
- Create: `src/web/src/features/rider-tracking/hooks/useRiderLocations.ts`
- Create: `src/web/src/features/rider-tracking/components/LiveMap.tsx`
- Create: `src/web/src/app/(staff)/riders/live-map/page.tsx`
- Modify: `src/web/src/lib/navigation/navItems.ts`
- Modify: `src/web/package.json` (new dependencies)
- Test: `src/web/src/features/rider-tracking/hooks/useRiderLocations.test.tsx`

**Interfaces:**
- Consumes: `/api/riders/branches/{branchId}/locations` (Task 7), `useUiStore().activeBranchId` (existing, `@/lib/store/uiStore`), `useRiders()` (existing, `@/features/riders/hooks/useRiders`, for `licenseNumber` lookup by rider id), `usePermission` (existing, `@/features/auth/hooks/usePermission`), `ErrorState`/`EmptyState` (existing, `@/components/states/`).
- Produces: `useRiderLocations(branchId: string | undefined)` (TanStack Query hook), `<LiveMap locations={...} riderLicenseById={...} />`, the `/riders/live-map` route, and a new `NAV_ITEMS` entry gated on `rider.location.read`.

- [ ] **Step 1: Install the new dependencies**

Run (from `src/web`): `npm install leaflet react-leaflet && npm install -D @types/leaflet`
Expected: `package.json`/`package-lock.json` updated with `leaflet`, `react-leaflet`, `@types/leaflet`.

- [ ] **Step 2: Write the failing hook test**

```typescript
// src/web/src/features/rider-tracking/hooks/useRiderLocations.test.tsx
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useRiderLocations } from "./useRiderLocations";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useRiderLocations", () => {
  it("fetches locations for the given branch", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [{ riderId: "r1", latitude: 27.7, longitude: 85.3, accuracyMeters: 10, recordedAt: "2026-09-17T00:00:00Z" }],
    });

    const { result } = renderHook(() => useRiderLocations("branch-1"), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.[0].riderId).toBe("r1");
    expect(global.fetch).toHaveBeenCalledWith("/api/riders/branches/branch-1/locations", undefined);
  });

  it("does not fetch when branchId is undefined", () => {
    global.fetch = vi.fn();

    renderHook(() => useRiderLocations(undefined), { wrapper });

    expect(global.fetch).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run (from `src/web`): `npm test -- src/features/rider-tracking/hooks/useRiderLocations.test.tsx`
Expected: FAIL — the hook doesn't exist yet.

- [ ] **Step 4: Implement the query hook**

```typescript
// src/web/src/features/rider-tracking/hooks/useRiderLocations.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export interface RiderLocation {
  riderId: string;
  latitude: number;
  longitude: number;
  accuracyMeters: number | null;
  recordedAt: string;
}

const POLL_INTERVAL_MS = 20_000;

export function useRiderLocations(branchId: string | undefined) {
  return useQuery({
    queryKey: ["riders", "locations", branchId],
    queryFn: () => clientFetch<RiderLocation[]>(`/api/riders/branches/${branchId}/locations`),
    enabled: branchId !== undefined,
    refetchInterval: POLL_INTERVAL_MS,
  });
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run (from `src/web`): `npm test -- src/features/rider-tracking/hooks/useRiderLocations.test.tsx`
Expected: both PASS.

- [ ] **Step 6: Implement the map component**

Leaflet touches `window` at import time, so it must never be imported during SSR — this component is loaded via `next/dynamic` with `ssr: false` from the page (Step 7), not imported directly anywhere else. Leaflet's default marker icon also breaks under bundlers unless explicitly reconfigured (a well-known Leaflet/webpack issue) — worked around here with CDN-hosted icon URLs rather than bundling image assets.

```typescript
// src/web/src/features/rider-tracking/components/LiveMap.tsx
"use client";

import { useEffect } from "react";
import { MapContainer, TileLayer, Marker, Popup } from "react-leaflet";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import type { RiderLocation } from "@/features/rider-tracking/hooks/useRiderLocations";

const DEFAULT_CENTER: [number, number] = [27.7172, 85.324]; // Kathmandu — used only when no rider has reported yet.

function minutesAgo(recordedAt: string): number {
  return Math.max(0, Math.round((Date.now() - new Date(recordedAt).getTime()) / 60_000));
}

export function LiveMap({ locations, riderLicenseById }: { locations: RiderLocation[]; riderLicenseById: Map<string, string> }) {
  useEffect(() => {
    // Leaflet's default marker icon references image paths relative to the installed package,
    // which bundlers rewrite incorrectly — fixed once here with CDN URLs, the standard workaround.
    L.Icon.Default.mergeOptions({
      iconRetinaUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png",
      iconUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png",
      shadowUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png",
    });
  }, []);

  const center: [number, number] = locations.length > 0 ? [locations[0].latitude, locations[0].longitude] : DEFAULT_CENTER;

  return (
    <MapContainer center={center} zoom={13} className="h-[500px] w-full rounded-md">
      <TileLayer
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
      />
      {locations.map((location) => (
        <Marker key={location.riderId} position={[location.latitude, location.longitude]}>
          <Popup>
            <div>{riderLicenseById.get(location.riderId) ?? "Unknown rider"}</div>
            <div>Last seen {minutesAgo(location.recordedAt)}m ago</div>
          </Popup>
        </Marker>
      ))}
    </MapContainer>
  );
}
```

- [ ] **Step 7: Implement the page and add the nav entry**

```typescript
// src/web/src/app/(staff)/riders/live-map/page.tsx
"use client";

import dynamic from "next/dynamic";
import { EmptyState } from "@/components/states/EmptyState";
import { ErrorState } from "@/components/states/ErrorState";
import { LoadingState } from "@/components/states/LoadingState";
import { useUiStore } from "@/lib/store/uiStore";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { useRiderLocations } from "@/features/rider-tracking/hooks/useRiderLocations";

const LiveMap = dynamic(() => import("@/features/rider-tracking/components/LiveMap").then((mod) => mod.LiveMap), { ssr: false });

export default function LiveMapPage() {
  const activeBranchId = useUiStore((s) => s.activeBranchId);
  const { data: locations, isLoading, isError, refetch } = useRiderLocations(activeBranchId ?? undefined);
  const { data: riders } = useRiders();

  const riderLicenseById = new Map((riders ?? []).map((r) => [r.id, r.licenseNumber]));

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">Live Map</h1>
      {!activeBranchId ? (
        <EmptyState title="Select a branch" description="Choose a branch from the switcher to see its active riders." />
      ) : isError ? (
        <ErrorState title="Couldn't load rider locations" description="Something went wrong fetching live positions." onRetry={() => refetch()} />
      ) : isLoading ? (
        <LoadingState />
      ) : locations && locations.length > 0 ? (
        <LiveMap locations={locations} riderLicenseById={riderLicenseById} />
      ) : (
        <EmptyState title="No active riders" description="No riders at this branch have reported a position yet." />
      )}
    </div>
  );
}
```

`EmptyState` takes `title`/`description` (optional `action`/`icon`), `LoadingState` takes no required props, and `ErrorState` takes `title`/`description`/`onRetry` — all confirmed against `src/web/src/components/states/*.tsx` and matched exactly above.

In `src/web/src/lib/navigation/navItems.ts`, add one entry after the existing `Riders` row:

```typescript
  { href: "/riders/live-map", label: "Live Map", permission: "rider.location.read", icon: MapPin },
```

Add `MapPin` to the `lucide-react` import at the top of the file alongside the other icons already imported there (`Bike`, `Truck`, etc.).

- [ ] **Step 8: Build**

Run (from `src/web`): `npm run build`
Expected: build succeeds — the `ssr: false` dynamic import must not error during the production build's static analysis.

- [ ] **Step 9: Manual verification**

Start the stack (`docker compose up -d` from the repo root if not already running, then the API and `npm run dev` from `src/web`). Log in as an org owner, create a rider and a branch, open `/rider/tasks` on a second browser/device (or the same browser with location permission granted) to let the foreground hook post a real ping, then open `/riders/live-map` as staff and confirm a pin appears with the correct "last seen" age. This is the one step in this plan that isn't automatable — the spec (§6) calls it out explicitly as manual E2E.

- [ ] **Step 10: Commit**

```bash
git add src/web/src/features/rider-tracking/ src/web/src/app/\(staff\)/riders/live-map/ src/web/src/lib/navigation/navItems.ts src/web/package.json src/web/package-lock.json
git commit -m "feat(web): add branch-scoped Live Map page for rider tracking"
```
