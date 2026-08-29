# Frontend Phase F4 (Manifests) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the Manifests feature area — a manifest list, a Sheet-based create flow, and a
Sheet-based detail view driving the full lifecycle (Draft → Sealed → InTransit → Received →
Closed) via manual AWB scanning — plus the one necessary backend addition (a list endpoint) that
Manifests uniquely lacks.

**Architecture:** Mirrors Phases F2/F3 exactly: BFF route handlers proxy the real API, TanStack
Query hooks call them, a feature-scoped Zustand store drives two globally-mounted Sheets
(create/detail) per `docs/ARCHITECTURE.md` §9a.

**Tech Stack:** ASP.NET Core/EF Core (Task 2 only), Next.js 14 App Router, TypeScript, TanStack
Query 5, React Hook Form + Zod v4, Zustand, shadcn/ui (`Sheet`, `Select`, `Input`, `Label`,
`Button`, `Card`), the existing `DataTable`, `StatusBadge`, and `useBranches` (reused verbatim).

**Spec:** `docs/superpowers/specs/2026-08-29-frontend-phase-f4-manifests-design.md`

## Global Constraints

- Detail and create UI is a right-side `Sheet` driven by a Zustand store, never a route or
  `next/link` — `docs/ARCHITECTURE.md` §9a, no exceptions.
- BFF routes are thin proxies: `apiFetch` server-side, `NextResponse.json(body, { status:
  response.status })` on success, `NextResponse.json(null, { status: 401 })` on a caught
  `NOT_AUTHENTICATED` error — matching every existing route in `app/api/*` exactly.
- Zod v4 syntax: `{ error: "..." }`, not v3's `required_error`/`invalid_type_error`.
- Manifest status colors are namespaced (`Manifest.Draft`, not `Draft`) in `statusConfig.ts` to
  avoid silently overwriting `ShipmentStatus`'s own `InTransit` entry — `StatusBadge` takes an
  optional `namespace` prop, defaulting to none, so every existing shipment-status callsite is
  unaffected.
- Run `cd src/web && npm test` and `npm run build` after every frontend task; `dotnet test
  tests/Manifests.Tests/Manifests.Tests.csproj` and the relevant `Api.Tests` filter after Task 2.

---

### Task 1: `CLARIFICATIONS.md` entry

**Files:**
- Modify: `docs/CLARIFICATIONS.md`

**Interfaces:** None (pure documentation).

- [ ] **Step 1: Add entry §15**

Insert immediately before the `## Summary of schema additions implied by this document` section
(after the existing `## 14.` entry):

```markdown
## 15. Frontend Phase F4: Manifests had no list endpoint; manual-AWB scanning only this phase

**Gap — no manifest list/search endpoint.** `ManifestsEndpoints.cs` exposed only `GET /{id:guid}`
(by internal id, known only right after creation) and `GET /{id:guid}/missing-shipments` — no way
to browse manifests at all, unlike every other entity (Branches, Shipments). This uniquely blocked
a real chunk of Frontend Phase F4's own scope: the destination branch's staff has no way to *find*
an inbound manifest to receive, since receiving naturally happens at a different branch/session
than creation.

**Resolution: added, not deferred.** `GET /api/v1/manifests?branchId=&status=` — a plain
(non-paginated) list filtered to manifests where the caller's accessible branches (for
`manifest.read`) include `FromBranchId` or `ToBranchId`. Framed as closing a foundational
inconsistency every other entity already had, not new business feature work.

**Deferred, not silently dropped: camera-based barcode/QR scanning.** This phase's scan
inputs (build-manifest scan, receive-scan) are manual AWB text entry only. Unblocks with a
scanning library choice and camera-permission UX — no backend dependency, purely a frontend
addition whenever a future phase picks it up.

**Also found, not fixed (out of scope for this task):** running `validate_palette.js` against the
existing shipment-status palette plus this phase's two new manifest tokens surfaced that two
*pre-existing* tokens (`--status-delivered` and `--status-created`) already sit below the
normal-vision adjacent-pair floor (ΔE 11.2, light mode) against each other — a debt that predates
this phase and isn't something either of this phase's two new tokens (`--status-sealed`,
`--status-received`) touches or worsens. Worth a dedicated pass revisiting the whole shipment
status palette at some point; not blocking anything today since every status always ships with a
visible text label, never color alone.
```

- [ ] **Step 2: Commit**

```bash
git add docs/CLARIFICATIONS.md
git commit -m "docs: record Frontend Phase F4's manifest-list backend addition and scope decisions"
```

---

### Task 2: Backend — `GET /api/v1/manifests` list endpoint

**Files:**
- Modify: `src/Modules/Manifests/Application/ManifestService.Reads.cs`
- Modify: `src/Modules/Manifests/Presentation/ManifestsEndpoints.cs`
- Test: `tests/Manifests.Tests/ManifestServiceListTests.cs` (new file)
- Test: `tests/Api.Tests/ManifestsEndpointsTests.cs` (add two test methods + one helper to the
  existing file — it already has `RegisterOrgAsync`/`SeedBranchesAsync`/`ValidBranchRequest`
  helpers this task's tests reuse directly; a separate new test file would only duplicate them)

**Interfaces:**
- Produces: `ManifestService.ListAsync(Guid? branchId, ManifestStatus? status, ClaimsPrincipal
  actingUser, CancellationToken): Task<List<ManifestResponse>>`; `GET /api/v1/manifests` (query
  params `branchId`, `status`), permission `manifest.read`.

- [ ] **Step 1: Write the failing unit test**

Create `tests/Manifests.Tests/ManifestServiceListTests.cs`. This mirrors
`ManifestServiceCreationTests.cs`'s exact fixture pattern (`file class` fakes, `TestDbContext`,
`CreateContext()`, `ActingUser()`) — copied here since `file class` types are file-scoped and
can't be reused across files:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Manifests.Application;
using Courier.Manifests.Domain;
using Courier.Manifests.Infrastructure;
using Courier.Shipments.Application;
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
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

file class AllBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(null);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

file class RestrictedBranchesAuthorizationChecker : IBranchAuthorizationChecker
{
    private readonly IReadOnlySet<Guid> _accessible;
    public RestrictedBranchesAuthorizationChecker(params Guid[] accessibleBranchIds) => _accessible = accessibleBranchIds.ToHashSet();
    public Task<IReadOnlySet<Guid>?> GetAccessibleBranchIdsAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>?>(_accessible);
    public Task<bool> HasAccessAsync(ClaimsPrincipal user, string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_accessible.Contains(branchId));
}

file class StubShipmentTransitionService : IShipmentTransitionService
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

public class ManifestServiceListTests
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

    private static async Task<ManifestResponse> CreateManifestAsync(TestDbContext dbContext, Guid organizationId, Guid fromBranchId, Guid toBranchId)
    {
        var service = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(),
            new StubShipmentTransitionService(), new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));
        return await service.CreateManifestAsync(new CreateManifestRequest(fromBranchId, toBranchId), ActingUser(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListAsync_ExcludesManifestsOutsideCallersAccessibleBranches()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var accessibleBranchId1 = Guid.NewGuid();
        var accessibleBranchId2 = Guid.NewGuid();
        var inaccessibleBranchId1 = Guid.NewGuid();
        var inaccessibleBranchId2 = Guid.NewGuid();

        var accessibleManifest = await CreateManifestAsync(dbContext, organizationId, accessibleBranchId1, accessibleBranchId2);
        await CreateManifestAsync(dbContext, organizationId, inaccessibleBranchId1, inaccessibleBranchId2);

        var listService = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(),
            new StubShipmentTransitionService(), new RestrictedBranchesAuthorizationChecker(accessibleBranchId1, accessibleBranchId2),
            new FakeBranchLookup(accessibleBranchId1, accessibleBranchId2));

        var result = await listService.ListAsync(null, null, ActingUser(Guid.NewGuid()));

        var resultIds = result.Select(m => m.Id).ToList();
        Assert.Contains(accessibleManifest.Id, resultIds);
        Assert.Single(result);
    }

    [Fact]
    public async Task ListAsync_FiltersByStatus()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var fromBranchId = Guid.NewGuid();
        var toBranchId = Guid.NewGuid();

        var draftManifest = await CreateManifestAsync(dbContext, organizationId, fromBranchId, toBranchId);
        var sealedManifest = await CreateManifestAsync(dbContext, organizationId, fromBranchId, toBranchId);
        var sealedEntity = await dbContext.Set<Manifest>().FirstAsync(m => m.Id == sealedManifest.Id);
        sealedEntity.Status = ManifestStatus.Sealed;
        await dbContext.SaveChangesAsync();

        var listService = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(),
            new StubShipmentTransitionService(), new AllBranchesAuthorizationChecker(), new FakeBranchLookup(fromBranchId, toBranchId));

        var result = await listService.ListAsync(null, ManifestStatus.Sealed, ActingUser(Guid.NewGuid()));

        var resultIds = result.Select(m => m.Id).ToList();
        Assert.Contains(sealedManifest.Id, resultIds);
        Assert.DoesNotContain(draftManifest.Id, resultIds);
    }

    [Fact]
    public async Task ListAsync_FiltersByBranchId()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var branchC = Guid.NewGuid();
        var branchD = Guid.NewGuid();

        var manifestAB = await CreateManifestAsync(dbContext, organizationId, branchA, branchB);
        await CreateManifestAsync(dbContext, organizationId, branchC, branchD);

        var listService = new ManifestService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(),
            new StubShipmentTransitionService(), new AllBranchesAuthorizationChecker(), new FakeBranchLookup(branchA, branchB));

        var result = await listService.ListAsync(branchA, null, ActingUser(Guid.NewGuid()));

        var resultIds = result.Select(m => m.Id).ToList();
        Assert.Contains(manifestAB.Id, resultIds);
        Assert.Single(result);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Manifests.Tests/Manifests.Tests.csproj --filter ManifestServiceListTests`
Expected: FAIL — `ListAsync` does not exist yet.

- [ ] **Step 3: Implement `ListAsync`**

Add to `src/Modules/Manifests/Application/ManifestService.Reads.cs`:

```csharp
    public async Task<List<ManifestResponse>> ListAsync(Guid? branchId, ManifestStatus? status, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Manifest>().AsQueryable();

        // Scope to branches the caller can access for manifest.read. null means "no filter
        // applies" (BranchScope.AllBranches) per IBranchAuthorizationChecker's documented
        // contract — a non-null set restricts to manifests touching any of those branches as
        // sender or receiver, mirroring ShipmentService.Reads.cs's ListAsync's established shape.
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "manifest.read", cancellationToken);
        if (accessibleBranchIds is not null)
        {
            var accessibleBranchIdsArray = accessibleBranchIds.ToArray();
            query = query.Where(m =>
                accessibleBranchIdsArray.Contains(m.FromBranchId) ||
                accessibleBranchIdsArray.Contains(m.ToBranchId));
        }

        if (branchId is Guid requestedBranchId)
        {
            query = query.Where(m => m.FromBranchId == requestedBranchId || m.ToBranchId == requestedBranchId);
        }
        if (status is ManifestStatus requestedStatus)
        {
            query = query.Where(m => m.Status == requestedStatus);
        }

        var manifests = await query.OrderByDescending(m => m.CreatedAt).ToListAsync(cancellationToken);
        return manifests.Select(ToResponse).ToList();
    }
```

Add `using System.Security.Claims;` and `using Courier.SharedKernel;` to this file's usings if not
already present (check the file's current using list — `ManifestService.cs` already has both, this
partial-class file may not).

`ToResponse` is a private static method already defined in `ManifestService.cs` — this partial
class file already has access to it.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Manifests.Tests/Manifests.Tests.csproj --filter ManifestServiceListTests`
Expected: PASS.

- [ ] **Step 5: Write the failing integration tests**

Add to `tests/Api.Tests/ManifestsEndpointsTests.cs` (inside the existing `ManifestsEndpointsTests`
class, alongside its other `[Fact]` methods). This needs a scoped-second-user helper the file
doesn't have yet — add it as a private method on the class, copied from
`BranchAuthorizationEndpointTests.cs`'s `SeedScopedUserTokenAsync` (same pattern: no
grant/user-management endpoint exists yet, so the scoped user's role/scope/branch grants are
seeded straight against `AppDbContext`):

```csharp
    private async Task<string> SeedScopedUserTokenAsync(
        Guid organizationId, Guid ownerUserId, string permissionKey, params Guid[] grantedBranchIds)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var permission = await dbContext.Set<Permission>().FirstAsync(p => p.Key == permissionKey);
        var role = new Role
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = $"ScopedRole-{permissionKey}-{Guid.NewGuid():N}",
            IsSystemDefault = false
        };
        role.Permissions.Add(permission);
        dbContext.Set<Role>().Add(role);

        var scopedUserId = Guid.NewGuid();
        var email = $"scoped-{scopedUserId:N}@example.com";
        dbContext.Set<User>().Add(new User
        {
            Id = scopedUserId,
            OrganizationId = organizationId,
            Email = email,
            PasswordHash = "not-used-in-this-test",
            FullName = "Scoped User"
        });
        dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = scopedUserId,
            RoleId = role.Id,
            OrganizationId = organizationId,
            BranchScope = BranchScope.SpecificBranches,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = ownerUserId
        });
        foreach (var branchId in grantedBranchIds)
        {
            dbContext.Set<UserBranch>().Add(new UserBranch
            {
                Id = Guid.NewGuid(),
                UserId = scopedUserId,
                BranchId = branchId,
                OrganizationId = organizationId,
                GrantedAt = DateTimeOffset.UtcNow,
                GrantedByUserId = ownerUserId
            });
        }
        await dbContext.SaveChangesAsync();

        return jwtTokenService.IssueAccessToken(scopedUserId, organizationId, email);
    }

    [Fact]
    public async Task GetManifests_OwnerWithAllBranchesScope_SeesManifestItCreated()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client, "list-owner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var (fromBranchId, toBranchId, _) = await SeedBranchesAsync(client);
        var created = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId)))
            .Content.ReadFromJsonAsync<ManifestResponse>();

        var response = await client.GetAsync("/api/v1/manifests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var manifests = await response.Content.ReadFromJsonAsync<List<ManifestResponse>>();
        Assert.Contains(manifests!, m => m.Id == created!.Id);
    }

    [Fact]
    public async Task GetManifests_UserWithNoAccessToEitherBranch_DoesNotSeeManifest()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client, "list-scoped");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var (fromBranchId, toBranchId, _) = await SeedBranchesAsync(client);
        var created = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId)))
            .Content.ReadFromJsonAsync<ManifestResponse>();
        var (unrelatedBranchId, _, _) = await SeedBranchesAsync(client);

        var scopedToken = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "manifest.read", unrelatedBranchId);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopedToken);

        var response = await scopedClient.GetAsync("/api/v1/manifests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var manifests = await response.Content.ReadFromJsonAsync<List<ManifestResponse>>();
        Assert.DoesNotContain(manifests!, m => m.Id == created!.Id);
    }
```

Add the following usings to the top of `tests/Api.Tests/ManifestsEndpointsTests.cs` if not already
present: `Courier.Identity.Domain`, `Courier.Persistence`, `Microsoft.EntityFrameworkCore`,
`Microsoft.Extensions.DependencyInjection`.

- [ ] **Step 6: Run the test to verify it fails**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~GetManifests"`
Expected: FAIL — the endpoint doesn't exist yet (404).

- [ ] **Step 7: Implement the endpoint**

Add to `src/Modules/Manifests/Presentation/ManifestsEndpoints.cs`, inside `MapManifestsEndpoints`,
alongside the existing routes:

```csharp
        group.MapGet("/", async (Guid? branchId, ManifestStatus? status, ManifestService service, HttpContext httpContext) =>
            Results.Ok(await service.ListAsync(branchId, status, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("manifest.read");
```

Add `using Courier.Manifests.Domain;` to this file's usings for `ManifestStatus`.

- [ ] **Step 8: Run the test to verify it passes**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~GetManifests"`
Expected: PASS.

- [ ] **Step 9: Run the full Manifests.Tests and relevant Api.Tests suites**

Run: `dotnet test tests/Manifests.Tests/Manifests.Tests.csproj && dotnet test
tests/Api.Tests/Api.Tests.csproj --filter FullyQualifiedName~Manifest`
Expected: all PASS.

- [ ] **Step 10: Rebuild and restart the API container**

```bash
docker compose -f docker-compose.yml build api
docker compose -f docker-compose.yml up -d api
```

- [ ] **Step 11: Commit**

```bash
git add src/Modules/Manifests/Application/ManifestService.Reads.cs src/Modules/Manifests/Presentation/ManifestsEndpoints.cs tests/Manifests.Tests/ManifestServiceListTests.cs tests/Api.Tests/ManifestsEndpointsTests.cs
git commit -m "feat(manifests): add GET /api/v1/manifests list endpoint"
```

---

### Task 3: Manifest status colors — `statusConfig.ts` namespacing + new tokens

**Files:**
- Modify: `src/web/src/app/globals.css`
- Modify: `src/web/src/components/status-badge/statusConfig.ts`
- Modify: `src/web/src/components/status-badge/StatusBadge.tsx`
- Test: `src/web/src/components/status-badge/StatusBadge.test.tsx` (extend existing file)

**Interfaces:**
- Produces: `resolveStatusConfig(status, namespace?)`; `<StatusBadge status namespace? />`. Tasks 9
  (list page) and 10 (detail sheet) consume the `namespace="Manifest"` form.

- [ ] **Step 1: Add the two new CSS tokens**

In `src/web/src/app/globals.css`, add to the `:root` block (light mode), alongside the existing
`--status-*` tokens:

```css
  --status-sealed: 221.0 65.0% 45.0%;
  --status-sealed-foreground: 229.1 20.0% 10.8%;

  --status-received: 195.0 90.0% 50.0%;
  --status-received-foreground: 229.1 20.0% 10.8%;
```

Add to the dark-mode block:

```css
  --status-sealed: 221.0 70.0% 62.0%;
  --status-sealed-foreground: 230.0 23.1% 10.2%;

  --status-received: 195.0 85.0% 60.0%;
  --status-received-foreground: 230.0 23.1% 10.2%;
```

These two values were chosen and validated via the `dataviz` skill against the full existing
status palette (`node scripts/validate_palette.js` — both `--mode light` and `--mode dark`,
`--pairs all`): neither introduces a new worst-case adjacent-pair failure: the worst pair in both
modes after adding them is an unrelated pre-existing pair (`--status-delivered`↔`--status-created`
in light mode, `--status-rto`↔`--status-delivered`/`--status-created` in dark mode) — see Task 1's
CLARIFICATIONS entry for that pre-existing, out-of-scope finding.

- [ ] **Step 2: Namespace `statusConfig.ts` and add the five manifest entries**

Modify `src/web/src/components/status-badge/statusConfig.ts` — change `resolveStatusConfig` to
accept an optional namespace, and add the five `Manifest.*` keys:

```typescript
export const STATUS_CONFIG: Record<string, StatusConfigEntry> = {
  Created: { label: "Created", token: "status-created" },
  PickupAssigned: { label: "Created", token: "status-created" },
  PickedUp: { label: "Picked Up", token: "status-in-transit" },
  AtOriginBranch: { label: "In Transit", token: "status-in-transit" },
  Sorted: { label: "In Transit", token: "status-in-transit" },
  InTransit: { label: "In Transit", token: "status-in-transit" },
  AtDestinationBranch: { label: "In Transit", token: "status-in-transit" },
  OutForDelivery: { label: "Out for Delivery", token: "status-out-for-delivery" },
  Delivered: { label: "Delivered", token: "status-delivered" },
  DeliveryFailed: { label: "NDR", token: "status-ndr" },
  NdrCreated: { label: "NDR", token: "status-ndr" },
  RtoInitiated: { label: "RTO Initiated", token: "status-rto" },
  RtoInTransit: { label: "RTO In Transit", token: "status-rto" },
  RtoAtOrigin: { label: "RTO At Origin", token: "status-rto" },
  RtoDelivered: { label: "RTO Delivered", token: "status-rto" },
  Cancelled: { label: "Cancelled", token: "status-cancelled" },
  // Manifest statuses — namespaced ("Manifest.X") since ManifestStatus and ShipmentStatus both
  // have an "InTransit" member; without namespacing, a flat key would let one silently overwrite
  // the other in this Record. Draft/Closed deliberately reuse an existing shipment-status token
  // (same real-world meaning: "just created, nothing actioned yet" / "done") rather than minting
  // new colors for concepts this palette already has a color for.
  "Manifest.Draft": { label: "Draft", token: "status-created" },
  "Manifest.Sealed": { label: "Sealed", token: "status-sealed" },
  "Manifest.InTransit": { label: "In Transit", token: "status-in-transit" },
  "Manifest.Received": { label: "Received", token: "status-received" },
  "Manifest.Closed": { label: "Closed", token: "status-delivered" },
};

export function resolveStatusConfig(status: string, namespace?: string): StatusConfigEntry {
  const key = namespace ? `${namespace}.${status}` : status;
  return STATUS_CONFIG[key] ?? { label: status, token: "status-cancelled" };
}
```

- [ ] **Step 3: Add `namespace` to `StatusBadge`**

Modify `src/web/src/components/status-badge/StatusBadge.tsx`:

```typescript
import { Badge } from "@/components/ui/badge";
import { resolveStatusConfig } from "@/components/status-badge/statusConfig";

export function StatusBadge({ status, namespace }: { status: string; namespace?: string }) {
  const config = resolveStatusConfig(status, namespace);
  return (
    <Badge variant="secondary" className="gap-1.5">
      <span
        className="h-2 w-2 rounded-full"
        style={{ backgroundColor: `hsl(var(--${config.token}))` }}
        aria-hidden="true"
      />
      {config.label}
    </Badge>
  );
}
```

- [ ] **Step 4: Write the failing test for namespaced resolution**

Append to `src/web/src/components/status-badge/StatusBadge.test.tsx` (read the existing file first
to match its established test style exactly):

```typescript
it("resolves a namespaced manifest status distinctly from the shipment status of the same name", () => {
  render(<StatusBadge status="InTransit" />);
  expect(screen.getByText("In Transit")).toBeInTheDocument();
});

it("resolves Manifest.InTransit via the namespace prop", () => {
  render(<StatusBadge status="InTransit" namespace="Manifest" />);
  expect(screen.getByText("In Transit")).toBeInTheDocument();
});

it("resolves a manifest-only status like Sealed via the namespace prop", () => {
  render(<StatusBadge status="Sealed" namespace="Manifest" />);
  expect(screen.getByText("Sealed")).toBeInTheDocument();
});
```

- [ ] **Step 5: Run the test to verify it fails, then passes after Steps 2-3**

Run: `cd src/web && npm test -- StatusBadge.test.tsx`
Expected: FAIL before Steps 2-3 (namespace prop doesn't exist), PASS after.

- [ ] **Step 6: Run the full test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: both PASS — every existing `StatusBadge`/`statusConfig` consumer (Branches, Shipments)
is unaffected since they never pass `namespace`.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/app/globals.css src/web/src/components/status-badge
git commit -m "feat(web): add manifest status colors and namespace StatusBadge to avoid the InTransit key collision"
```

---

### Task 4: Manifest types, BFF routes, and hooks (list/detail/create)

**Files:**
- Create: `src/web/src/features/manifests/types.ts`
- Create: `src/web/src/app/api/manifests/route.ts`
- Create: `src/web/src/app/api/manifests/route.test.ts`
- Create: `src/web/src/app/api/manifests/[id]/route.ts`
- Create: `src/web/src/app/api/manifests/[id]/route.test.ts`
- Create: `src/web/src/features/manifests/hooks/useManifests.ts`
- Create: `src/web/src/features/manifests/hooks/useManifest.ts`
- Create: `src/web/src/features/manifests/hooks/useCreateManifest.ts`

**Interfaces:**
- Produces: `Manifest`, `CreateManifestInput` types; `useManifests(branchId?, status?):
  UseQueryResult<Manifest[]>`, `useManifest(id: string): UseQueryResult<Manifest>` (guarded
  `enabled: id !== ""`), `useCreateManifest(): UseMutationResult<Manifest, unknown,
  CreateManifestInput>`.

- [ ] **Step 1: Write `types.ts`**

Create `src/web/src/features/manifests/types.ts`:

```typescript
export type ManifestPurpose = "Forward" | "Return";
export type ManifestStatus = "Draft" | "Sealed" | "InTransit" | "Received" | "Closed";

export interface Manifest {
  id: string;
  code: string;
  fromBranchId: string;
  toBranchId: string;
  purpose: ManifestPurpose;
  vehiclePlate: string | null;
  driverId: string | null;
  status: ManifestStatus;
  shipmentCount: number;
  createdAt: string;
}

export interface CreateManifestInput {
  fromBranchId: string;
  toBranchId: string;
  purpose: ManifestPurpose;
}
```

- [ ] **Step 2: Write the failing test for the list/create BFF route**

Create `src/web/src/app/api/manifests/route.test.ts`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET, POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/manifests", () => {
  it("passes branchId/status through to the backend and proxies the response", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    } as Response);

    const request = new NextRequest("http://localhost/api/manifests?branchId=b1&status=Draft");
    const response = await GET(request);

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/manifests?branchId=b1&status=Draft");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const request = new NextRequest("http://localhost/api/manifests");
    const response = await GET(request);

    expect(response.status).toBe(401);
  });
});

describe("POST /api/manifests", () => {
  it("proxies the create request body to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => ({ id: "m1", code: "MNF-20260829-001" }),
    } as Response);

    const request = new NextRequest("http://localhost/api/manifests", {
      method: "POST",
      body: JSON.stringify({ fromBranchId: "b1", toBranchId: "b2", purpose: "Forward" }),
    });
    const response = await POST(request);

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/manifests",
      expect.objectContaining({ method: "POST" })
    );
    expect(response.status).toBe(201);
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/web && npm test -- manifests/route.test.ts`
Expected: FAIL — `./route` does not exist yet.

- [ ] **Step 4: Implement the list/create BFF route**

Create `src/web/src/app/api/manifests/route.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/manifests${query}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/manifests", {
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

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/web && npm test -- manifests/route.test.ts`
Expected: PASS.

- [ ] **Step 6: Write the failing test for the detail BFF route**

Create `src/web/src/app/api/manifests/[id]/route.test.ts`:

```typescript
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/manifests/[id]", () => {
  it("proxies the backend response for the given id", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "m1" }),
    } as Response);

    const response = await GET({} as Request, { params: { id: "m1" } });

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/manifests/m1");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET({} as Request, { params: { id: "m1" } });

    expect(response.status).toBe(401);
  });
});
```

- [ ] **Step 7: Run the test to verify it fails**

Run: `cd src/web && npm test 2>&1 | grep -A5 "manifests/\[id\]"`
Expected: FAIL — `./route` does not exist yet.

- [ ] **Step 8: Implement the detail BFF route**

Create `src/web/src/app/api/manifests/[id]/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/manifests/${params.id}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 9: Run the full test suite**

Run: `cd src/web && npm test`
Expected: PASS.

- [ ] **Step 10: Write the hooks (no dedicated test — bare TanStack Query wrappers, matching
  established precedent for pure passthrough hooks)**

Create `src/web/src/features/manifests/hooks/useManifests.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest } from "@/features/manifests/types";

export function useManifests(branchId?: string, status?: string) {
  const params = new URLSearchParams();
  if (branchId) params.set("branchId", branchId);
  if (status) params.set("status", status);
  const query = params.toString();

  return useQuery({
    queryKey: ["manifests", branchId ?? null, status ?? null],
    queryFn: () => clientFetch<Manifest[]>(`/api/manifests${query ? `?${query}` : ""}`),
  });
}
```

Create `src/web/src/features/manifests/hooks/useManifest.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest } from "@/features/manifests/types";

export function useManifest(id: string) {
  return useQuery({
    queryKey: ["manifests", id],
    queryFn: () => clientFetch<Manifest>(`/api/manifests/${id}`),
    enabled: id !== "",
  });
}
```

Create `src/web/src/features/manifests/hooks/useCreateManifest.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest, CreateManifestInput } from "@/features/manifests/types";

export function useCreateManifest() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateManifestInput) =>
      clientFetch<Manifest>("/api/manifests", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["manifests"] });
    },
  });
}
```

- [ ] **Step 11: Run the full test suite**

Run: `cd src/web && npm test`
Expected: PASS.

- [ ] **Step 12: Commit**

```bash
git add src/web/src/features/manifests/types.ts src/web/src/app/api/manifests src/web/src/features/manifests/hooks
git commit -m "feat(web): add manifest types, BFF routes, and list/detail/create hooks"
```

---

### Task 5: Manifest action BFF routes and hooks (scan/seal/dispatch/receive/close/missing-shipments)

**Files:**
- Modify: `src/web/src/features/manifests/types.ts`
- Create: `src/web/src/app/api/manifests/[id]/scan/route.ts`
- Create: `src/web/src/app/api/manifests/[id]/rto-scan/route.ts`
- Create: `src/web/src/app/api/manifests/[id]/seal/route.ts`
- Create: `src/web/src/app/api/manifests/[id]/dispatch/route.ts`
- Create: `src/web/src/app/api/manifests/[id]/receive-scan/route.ts`
- Create: `src/web/src/app/api/manifests/[id]/close/route.ts`
- Create: `src/web/src/app/api/manifests/[id]/missing-shipments/route.ts`
- Create: `src/web/src/features/manifests/hooks/useScanManifestShipment.ts`
- Create: `src/web/src/features/manifests/hooks/useSealManifest.ts`
- Create: `src/web/src/features/manifests/hooks/useDispatchManifest.ts`
- Create: `src/web/src/features/manifests/hooks/useReceiveScanManifestShipment.ts`
- Create: `src/web/src/features/manifests/hooks/useCloseManifest.ts`
- Create: `src/web/src/features/manifests/hooks/useMissingShipments.ts`
- Test: `src/web/src/features/manifests/lib/manifestErrorMessages.test.ts`
- Create: `src/web/src/features/manifests/lib/manifestErrorMessages.ts`

**Interfaces:**
- Consumes: nothing new.
- Produces: `ManifestShipmentScan`, `MissingShipment`, `AppliedCharge`-analog types; six mutation
  hooks plus `useMissingShipments(id)`; `manifestScanErrorMessage(reasonCode: string): string`.
  Task 10 (`ManifestDetailSheet`) consumes all of these.

- [ ] **Step 1: Extend `types.ts`**

Append to `src/web/src/features/manifests/types.ts`:

```typescript
export interface ManifestShipmentScan {
  id: string;
  shipmentId: string;
  awb: string;
  scannedAt: string;
  receivedAt: string | null;
}

export interface MissingShipment {
  shipmentId: string;
  awb: string;
  scannedAt: string;
}

export interface DispatchManifestInput {
  vehiclePlate: string;
  driverId?: string;
}
```

- [ ] **Step 2: BFF routes for `scan`/`rto-scan`/`seal`/`dispatch`/`receive-scan`/`close` (no
  dedicated tests — thin POST passthroughs, matching established precedent for this shape)**

Create `src/web/src/app/api/manifests/[id]/scan/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/manifests/${params.id}/scan`, {
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

Create `src/web/src/app/api/manifests/[id]/rto-scan/route.ts` (identical, `/rto-scan` path):

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/manifests/${params.id}/rto-scan`, {
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

Create `src/web/src/app/api/manifests/[id]/seal/route.ts` (no request body):

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/manifests/${params.id}/seal`, { method: "POST" });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/app/api/manifests/[id]/dispatch/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/manifests/${params.id}/dispatch`, {
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

Create `src/web/src/app/api/manifests/[id]/receive-scan/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/manifests/${params.id}/receive-scan`, {
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

Create `src/web/src/app/api/manifests/[id]/close/route.ts` (no request body):

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/manifests/${params.id}/close`, { method: "POST" });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/app/api/manifests/[id]/missing-shipments/route.ts` (GET):

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/manifests/${params.id}/missing-shipments`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 3: Hooks**

Create `src/web/src/features/manifests/hooks/useScanManifestShipment.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ManifestShipmentScan } from "@/features/manifests/types";

export function useScanManifestShipment(manifestId: string, purpose: "Forward" | "Return") {
  const queryClient = useQueryClient();
  const path = purpose === "Return" ? "rto-scan" : "scan";
  return useMutation({
    mutationFn: (awb: string) =>
      clientFetch<ManifestShipmentScan>(`/api/manifests/${manifestId}/${path}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ awb }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["manifests", manifestId] });
    },
  });
}
```

Create `src/web/src/features/manifests/hooks/useSealManifest.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest } from "@/features/manifests/types";

export function useSealManifest(manifestId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => clientFetch<Manifest>(`/api/manifests/${manifestId}/seal`, { method: "POST" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["manifests", manifestId] });
    },
  });
}
```

Create `src/web/src/features/manifests/hooks/useDispatchManifest.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest, DispatchManifestInput } from "@/features/manifests/types";

export function useDispatchManifest(manifestId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: DispatchManifestInput) =>
      clientFetch<Manifest>(`/api/manifests/${manifestId}/dispatch`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["manifests", manifestId] });
    },
  });
}
```

Create `src/web/src/features/manifests/hooks/useReceiveScanManifestShipment.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ManifestShipmentScan } from "@/features/manifests/types";

export function useReceiveScanManifestShipment(manifestId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (awb: string) =>
      clientFetch<ManifestShipmentScan>(`/api/manifests/${manifestId}/receive-scan`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ awb }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["manifests", manifestId] });
    },
  });
}
```

Create `src/web/src/features/manifests/hooks/useCloseManifest.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Manifest } from "@/features/manifests/types";

export function useCloseManifest(manifestId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => clientFetch<Manifest>(`/api/manifests/${manifestId}/close`, { method: "POST" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["manifests", manifestId] });
    },
  });
}
```

Create `src/web/src/features/manifests/hooks/useMissingShipments.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MissingShipment } from "@/features/manifests/types";

export function useMissingShipments(manifestId: string, enabled: boolean) {
  return useQuery({
    queryKey: ["manifests", manifestId, "missing-shipments"],
    queryFn: () => clientFetch<MissingShipment[]>(`/api/manifests/${manifestId}/missing-shipments`),
    enabled: enabled && manifestId !== "",
  });
}
```

- [ ] **Step 4: Write the failing test for the error-reason-code mapping**

Create `src/web/src/features/manifests/lib/manifestErrorMessages.test.ts`:

```typescript
import { describe, expect, it } from "vitest";
import { manifestScanErrorMessage } from "./manifestErrorMessages";

describe("manifestScanErrorMessage", () => {
  it("maps a known reason code to a plain-language message", () => {
    expect(manifestScanErrorMessage("SHIPMENT_NOT_FOUND")).toBe(
      "No shipment matches that AWB."
    );
    expect(manifestScanErrorMessage("ALREADY_RECEIVED")).toBe(
      "This shipment was already received on this manifest."
    );
  });

  it("falls back to a generic message for an unrecognized reason code", () => {
    expect(manifestScanErrorMessage("SOME_FUTURE_CODE")).toBe(
      "Couldn't scan this shipment. Check the AWB and try again."
    );
  });
});
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `cd src/web && npm test -- manifestErrorMessages.test.ts`
Expected: FAIL — `./manifestErrorMessages` does not exist yet.

- [ ] **Step 6: Implement the mapping**

Create `src/web/src/features/manifests/lib/manifestErrorMessages.ts`:

```typescript
// Maps ManifestScanRejectedException's ReasonCode (backend) to a plain-language message — the
// raw code is never shown to the user. Reasons per BRANCH-OPERATIONS.md §1's five-check scan
// validation, carried through Manifests' ManifestDtos.cs.
const REASON_MESSAGES: Record<string, string> = {
  SHIPMENT_NOT_FOUND: "No shipment matches that AWB.",
  WRONG_STATE: "This shipment isn't in a state that allows this scan.",
  WRONG_CUSTODY: "This shipment isn't currently in this branch's custody.",
  MANIFEST_NOT_OPEN: "This manifest is no longer open for scanning.",
  MANIFEST_NOT_RECEIVING: "This manifest isn't ready to receive shipments yet.",
  NOT_ON_MANIFEST: "This shipment isn't on this manifest.",
  ALREADY_RECEIVED: "This shipment was already received on this manifest.",
};

export function manifestScanErrorMessage(reasonCode: string): string {
  return REASON_MESSAGES[reasonCode] ?? "Couldn't scan this shipment. Check the AWB and try again.";
}
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `cd src/web && npm test -- manifestErrorMessages.test.ts`
Expected: PASS.

- [ ] **Step 8: Run the full test suite**

Run: `cd src/web && npm test`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/web/src/features/manifests/types.ts "src/web/src/app/api/manifests/[id]" src/web/src/features/manifests/hooks src/web/src/features/manifests/lib
git commit -m "feat(web): add manifest action BFF routes, hooks, and scan-error message mapping"
```

---

### Task 6: `manifestPanelStore` and `useRiders`

**Files:**
- Create: `src/web/src/features/manifests/store/manifestPanelStore.ts`
- Test: `src/web/src/features/manifests/store/manifestPanelStore.test.ts`
- Create: `src/web/src/app/api/riders/route.ts`
- Create: `src/web/src/features/manifests/hooks/useRiders.ts`

**Interfaces:**
- Produces: `useManifestPanelStore` — `{ panel: { mode: "closed" } | { mode: "create" } |
  { mode: "detail"; id: string }, openCreate(), openDetail(id), close() }`.
  `useRiders(): UseQueryResult<Rider[]>` (Driver picker source for Task 8's dispatch form).

- [ ] **Step 1: Write the failing store test**

Create `src/web/src/features/manifests/store/manifestPanelStore.test.ts`:

```typescript
import { describe, expect, it, beforeEach } from "vitest";
import { useManifestPanelStore } from "./manifestPanelStore";

describe("useManifestPanelStore", () => {
  beforeEach(() => {
    useManifestPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useManifestPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate switches to the create panel", () => {
    useManifestPanelStore.getState().openCreate();
    expect(useManifestPanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("openDetail switches to the detail panel with the given id", () => {
    useManifestPanelStore.getState().openDetail("m1");
    expect(useManifestPanelStore.getState().panel).toEqual({ mode: "detail", id: "m1" });
  });

  it("close returns to closed from either panel", () => {
    useManifestPanelStore.getState().openDetail("m1");
    useManifestPanelStore.getState().close();
    expect(useManifestPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- manifestPanelStore.test.ts`
Expected: FAIL — `./manifestPanelStore` does not exist yet.

- [ ] **Step 3: Implement the store**

Create `src/web/src/features/manifests/store/manifestPanelStore.ts`:

```typescript
import { create } from "zustand";

type ManifestPanelState =
  | { mode: "closed" }
  | { mode: "create" }
  | { mode: "detail"; id: string };

interface ManifestPanelStore {
  panel: ManifestPanelState;
  openCreate: () => void;
  openDetail: (id: string) => void;
  close: () => void;
}

// Global so any component in the (staff) tree can open the manifest sheet without routing to a
// separate page (docs/ARCHITECTURE.md §9a). Keyed by the manifest's Guid id, not Code — there is
// no lookup-by-code endpoint, only GET /{id}.
export const useManifestPanelStore = create<ManifestPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  openDetail: (id) => set({ panel: { mode: "detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- manifestPanelStore.test.ts`
Expected: PASS.

- [ ] **Step 5: Add the riders BFF route and hook (no dedicated tests — thin GET passthrough and
  bare TanStack Query wrapper, matching established precedent)**

Create `src/web/src/app/api/riders/route.ts`:

```typescript
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
```

Create `src/web/src/features/manifests/hooks/useRiders.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export interface Rider {
  id: string;
  licenseNumber: string;
  isActive: boolean;
}

export function useRiders() {
  return useQuery({
    queryKey: ["riders"],
    queryFn: () => clientFetch<Rider[]>("/api/riders"),
  });
}
```

`Rider` deliberately omits `userId`/`baseBranchId`/`roleType` — `RiderResponse` has no display
name, so the Driver picker labels each option by `licenseNumber` (the one human-meaningful
identifier a rider carries in this domain); `roleType` is an unconverted enum (serializes as a raw
integer, same latent bug class documented for `ServiceTypeResponse.Code` in Phase F3) and isn't
needed for this picker, so it's never fetched into the frontend type at all.

- [ ] **Step 6: Run the full test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: both PASS.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/manifests/store src/web/src/app/api/riders src/web/src/features/manifests/hooks/useRiders.ts
git commit -m "feat(web): add manifestPanelStore and a Riders hook for the manifest dispatch form's Driver picker"
```

---

### Task 7: Manifest list page

**Files:**
- Create: `src/web/src/app/(staff)/manifests/page.tsx`

**Interfaces:**
- Consumes: `useManifests` (Task 4), `useManifestPanelStore` (Task 6), `useBranches` (Branches
  feature), `usePermission`, `DataTable`, `StatusBadge` (with `namespace="Manifest"`).
- Produces: the `/manifests` route.

- [ ] **Step 1: Implement the list page**

Create `src/web/src/app/(staff)/manifests/page.tsx`:

```typescript
"use client";

import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useUiStore } from "@/lib/store/uiStore";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useManifests } from "@/features/manifests/hooks/useManifests";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";
import type { Manifest } from "@/features/manifests/types";

export default function ManifestsPage() {
  const activeBranchId = useUiStore((s) => s.activeBranchId);
  const { data, isLoading, isError, refetch } = useManifests(activeBranchId ?? undefined);
  const { data: branches } = useBranches();
  const canCreate = usePermission("manifest.create");
  const openCreate = useManifestPanelStore((s) => s.openCreate);
  const openDetail = useManifestPanelStore((s) => s.openDetail);

  const branchName = (id: string) => branches?.find((b) => b.id === id)?.name ?? "—";

  const columns: ColumnDef<Manifest>[] = [
    {
      key: "code",
      header: "Code",
      render: (m) => (
        <button
          type="button"
          onClick={() => openDetail(m.id)}
          className="font-medium text-foreground hover:underline"
        >
          {m.code}
        </button>
      ),
    },
    { key: "fromBranch", header: "From Branch", render: (m) => branchName(m.fromBranchId) },
    { key: "toBranch", header: "To Branch", render: (m) => branchName(m.toBranchId) },
    { key: "purpose", header: "Purpose", render: (m) => m.purpose },
    {
      key: "status",
      header: "Status",
      render: (m) => <StatusBadge status={m.status} namespace="Manifest" />,
    },
    { key: "shipmentCount", header: "Shipments", render: (m) => String(m.shipmentCount) },
    {
      key: "createdAt",
      header: "Created At",
      render: (m) => new Date(m.createdAt).toLocaleDateString(),
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
        title="Couldn't load manifests"
        description="Something went wrong fetching the manifest list."
        onRetry={() => refetch()}
      />
    );
  }

  const pagedData: PagedResult<Manifest> | undefined = data && {
    items: data,
    page: 1,
    pageSize: data.length,
    totalCount: data.length,
    totalPages: 1,
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Manifests</h1>
        {canCreate && (
          <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            New Manifest
          </Button>
        )}
      </div>
      <DataTable
        columns={columns}
        data={pagedData}
        isLoading={false}
        emptyTitle="No manifests yet"
        emptyDescription="Create a manifest to start building an outgoing transfer."
        onPageChange={() => {}}
        rowKey={(m) => m.id}
      />
    </div>
  );
}
```

- [ ] **Step 2: Run the full test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: both PASS — the new `/manifests` route appears in the build's route list.

- [ ] **Step 3: Commit**

```bash
git add "src/web/src/app/(staff)/manifests"
git commit -m "feat(web): add manifest list page"
```

---

### Task 8: `ManifestCreateSheet`

**Files:**
- Create: `src/web/src/features/manifests/schema.ts`
- Create: `src/web/src/features/manifests/components/ManifestCreateSheet.tsx`
- Test: `src/web/src/features/manifests/components/ManifestCreateSheet.test.tsx`

**Interfaces:**
- Consumes: `useCreateManifest`, `useBranches`, `useManifestPanelStore`.
- Produces: `<ManifestCreateSheet />`, mounted globally in Task 11.

- [ ] **Step 1: Write the schema**

Create `src/web/src/features/manifests/schema.ts`:

```typescript
import { z } from "zod";

export const createManifestSchema = z
  .object({
    fromBranchId: z.string({ error: "From branch is required." }).min(1, "From branch is required."),
    toBranchId: z.string({ error: "To branch is required." }).min(1, "To branch is required."),
    purpose: z.enum(["Forward", "Return"]),
  })
  .refine((values) => values.fromBranchId !== values.toBranchId, {
    message: "From and To branch must be different.",
    path: ["toBranchId"],
  });

export type CreateManifestFormValues = z.infer<typeof createManifestSchema>;
```

- [ ] **Step 2: Write the failing test**

Create `src/web/src/features/manifests/components/ManifestCreateSheet.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ManifestCreateSheet } from "./ManifestCreateSheet";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ManifestCreateSheet />
    </QueryClientProvider>
  );
}

describe("ManifestCreateSheet", () => {
  it("is not visible when the store is closed", () => {
    useManifestPanelStore.setState({ panel: { mode: "closed" } });
    renderWithProviders();
    expect(screen.queryByText("New Manifest")).not.toBeInTheDocument();
  });

  it("is visible with its fields when the store is open", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([
      { id: "b1", name: "Kathmandu Hub" },
    ]);
    useManifestPanelStore.setState({ panel: { mode: "create" } });
    renderWithProviders();

    await waitFor(() => expect(screen.getByText("New Manifest")).toBeInTheDocument());
    expect(screen.getByLabelText(/from branch/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/to branch/i)).toBeInTheDocument();
  });
});

describe("createManifestSchema", () => {
  it("rejects a From/To branch pair that's the same branch", async () => {
    const { createManifestSchema } = await import("@/features/manifests/schema");
    const result = createManifestSchema.safeParse({
      fromBranchId: "b1",
      toBranchId: "b1",
      purpose: "Forward",
    });
    expect(result.success).toBe(false);
  });

  it("accepts a valid From/To branch pair", async () => {
    const { createManifestSchema } = await import("@/features/manifests/schema");
    const result = createManifestSchema.safeParse({
      fromBranchId: "b1",
      toBranchId: "b2",
      purpose: "Forward",
    });
    expect(result.success).toBe(true);
  });
});
```

The same-branch rejection is tested directly against the Zod schema, not by driving it through a
rendered Radix `Select` — this project's established `GeographyPicker.test.tsx` precedent found
that Radix `SelectItem`s only mount via Portal when the dropdown is open, making that interaction
brittle to drive in a unit test; testing the `refine()` logic directly is both more reliable and
exercises the exact validation rule.

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/web && npm test -- ManifestCreateSheet.test.tsx`
Expected: FAIL — `./ManifestCreateSheet` does not exist yet.

- [ ] **Step 4: Implement the create Sheet**

Create `src/web/src/features/manifests/components/ManifestCreateSheet.tsx`:

```typescript
"use client";

import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { createManifestSchema, type CreateManifestFormValues } from "@/features/manifests/schema";
import { useCreateManifest } from "@/features/manifests/hooks/useCreateManifest";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";

export function ManifestCreateSheet() {
  const panel = useManifestPanelStore((s) => s.panel);
  const close = useManifestPanelStore((s) => s.close);
  const openDetail = useManifestPanelStore((s) => s.openDetail);
  const isOpen = panel.mode === "create";

  const createManifest = useCreateManifest();
  const { data: branches } = useBranches();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    handleSubmit,
    control,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateManifestFormValues>({
    resolver: zodResolver(createManifestSchema),
    defaultValues: { purpose: "Forward" },
  });

  async function onSubmit(values: CreateManifestFormValues) {
    setServerError(null);
    try {
      const created = await createManifest.mutateAsync(values);
      reset();
      openDetail(created.id);
    } catch {
      setServerError("Couldn't create the manifest. Check the fields and try again.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>New Manifest</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="fromBranchId">From Branch</Label>
            <Controller
              name="fromBranchId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="fromBranchId" aria-label="From branch">
                    <SelectValue placeholder="Select origin branch" />
                  </SelectTrigger>
                  <SelectContent>
                    {branches?.map((b) => (
                      <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.fromBranchId && <p className="text-sm text-destructive">{errors.fromBranchId.message}</p>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="toBranchId">To Branch</Label>
            <Controller
              name="toBranchId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="toBranchId" aria-label="To branch">
                    <SelectValue placeholder="Select destination branch" />
                  </SelectTrigger>
                  <SelectContent>
                    {branches?.map((b) => (
                      <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.toBranchId && <p className="text-sm text-destructive">{errors.toBranchId.message}</p>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="purpose">Purpose</Label>
            <Controller
              name="purpose"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="purpose" aria-label="Purpose">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Forward">Forward</SelectItem>
                    <SelectItem value="Return">Return</SelectItem>
                  </SelectContent>
                </Select>
              )}
            />
          </div>
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Manifest"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/web && npm test -- ManifestCreateSheet.test.tsx`
Expected: PASS.

- [ ] **Step 6: Run the full test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: both PASS.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/manifests/schema.ts src/web/src/features/manifests/components/ManifestCreateSheet.tsx src/web/src/features/manifests/components/ManifestCreateSheet.test.tsx
git commit -m "feat(web): add manifest create Sheet"
```

---

### Task 9: `ManifestDetailSheet` — Draft and Sealed sections

**Files:**
- Create: `src/web/src/features/manifests/components/ManifestDetailSheet.tsx`
- Create: `src/web/src/features/manifests/components/ManifestDraftSection.tsx`
- Create: `src/web/src/features/manifests/components/ManifestSealedSection.tsx`
- Test: `src/web/src/features/manifests/components/ManifestDraftSection.test.tsx`

**Interfaces:**
- Consumes: `useManifest`, `useManifestPanelStore` (Task 6), `useScanManifestShipment`,
  `useSealManifest`, `useDispatchManifest` (Task 5), `useRiders` (Task 6),
  `manifestScanErrorMessage` (Task 5).
- Produces: `<ManifestDetailSheet />` (header + status-driven section switch; Received/Closed
  sections added in Task 10), `<ManifestDraftSection manifest onScanned />`,
  `<ManifestSealedSection manifest />`.

- [ ] **Step 1: Write the failing test for `ManifestDraftSection`**

Create `src/web/src/features/manifests/components/ManifestDraftSection.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ManifestDraftSection } from "./ManifestDraftSection";
import * as clientFetchModule from "@/lib/api/clientFetch";
import type { Manifest } from "@/features/manifests/types";

const manifest: Manifest = {
  id: "m1",
  code: "MNF-20260829-001",
  fromBranchId: "b1",
  toBranchId: "b2",
  purpose: "Forward",
  vehiclePlate: null,
  driverId: null,
  status: "Draft",
  shipmentCount: 0,
  createdAt: "2026-08-29T00:00:00Z",
};

function renderWithProviders(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("ManifestDraftSection", () => {
  it("appends a successfully scanned AWB to the session list and clears the input", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue({
      id: "ms1",
      shipmentId: "s1",
      awb: "AWB123",
      scannedAt: "2026-08-29T00:00:00Z",
      receivedAt: null,
    });
    const user = userEvent.setup();
    renderWithProviders(<ManifestDraftSection manifest={manifest} />);

    const input = screen.getByLabelText(/scan awb/i);
    await user.type(input, "AWB123{Enter}");

    await waitFor(() => expect(screen.getByText("AWB123")).toBeInTheDocument());
    expect(input).toHaveValue("");
  });

  it("shows a plain-language error when the scan is rejected", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockRejectedValue(
      new Error(JSON.stringify({ error: { code: "MANIFEST_SCAN_REJECTED", reason: "SHIPMENT_NOT_FOUND" } }))
    );
    const user = userEvent.setup();
    renderWithProviders(<ManifestDraftSection manifest={manifest} />);

    const input = screen.getByLabelText(/scan awb/i);
    await user.type(input, "BADAWB{Enter}");

    await waitFor(() => expect(screen.getByText(/no shipment matches that awb/i)).toBeInTheDocument());
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- ManifestDraftSection.test.tsx`
Expected: FAIL — `./ManifestDraftSection` does not exist yet.

- [ ] **Step 3: Implement `ManifestDraftSection`**

Create `src/web/src/features/manifests/components/ManifestDraftSection.tsx`:

```typescript
"use client";

import { useState } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useScanManifestShipment } from "@/features/manifests/hooks/useScanManifestShipment";
import { useSealManifest } from "@/features/manifests/hooks/useSealManifest";
import { manifestScanErrorMessage } from "@/features/manifests/lib/manifestErrorMessages";
import type { Manifest, ManifestShipmentScan } from "@/features/manifests/types";

function parseScanError(error: unknown): string {
  try {
    const parsed = JSON.parse((error as Error).message);
    const reason = parsed?.error?.reason;
    if (typeof reason === "string") {
      return manifestScanErrorMessage(reason);
    }
  } catch {
    // fall through to generic message
  }
  return "Couldn't scan this shipment. Check the AWB and try again.";
}

export function ManifestDraftSection({ manifest }: { manifest: Manifest }) {
  const [awbInput, setAwbInput] = useState("");
  const [scanned, setScanned] = useState<ManifestShipmentScan[]>([]);
  const [scanError, setScanError] = useState<string | null>(null);
  const scanShipment = useScanManifestShipment(manifest.id, manifest.purpose);
  const sealManifest = useSealManifest(manifest.id);

  async function handleScan() {
    if (!awbInput.trim()) return;
    setScanError(null);
    try {
      const result = await scanShipment.mutateAsync(awbInput.trim());
      setScanned((prev) => [result, ...prev]);
      setAwbInput("");
    } catch (error) {
      setScanError(parseScanError(error));
    }
  }

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="scan-awb">Scan AWB</Label>
        <Input
          id="scan-awb"
          aria-label="Scan AWB"
          value={awbInput}
          onChange={(e) => setAwbInput(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              handleScan();
            }
          }}
          placeholder="Scan or type an AWB, then press Enter"
        />
        {scanError && <p className="text-sm text-destructive">{scanError}</p>}
      </div>
      {scanned.length > 0 && (
        <ul className="space-y-1 text-sm">
          {scanned.map((s) => (
            <li key={s.id}>{s.awb}</li>
          ))}
        </ul>
      )}
      <Button
        type="button"
        disabled={manifest.shipmentCount === 0 || sealManifest.isPending}
        onClick={() => sealManifest.mutate()}
      >
        {sealManifest.isPending ? <Spinner className="h-4 w-4" /> : "Seal Manifest"}
      </Button>
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- ManifestDraftSection.test.tsx`
Expected: PASS.

- [ ] **Step 5: Implement `ManifestSealedSection` (no dedicated test — a straightforward form,
  matching the density of this project's other simple mutation-trigger forms)**

Create `src/web/src/features/manifests/components/ManifestSealedSection.tsx`:

```typescript
"use client";

import { useState } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useDispatchManifest } from "@/features/manifests/hooks/useDispatchManifest";
import { useRiders } from "@/features/manifests/hooks/useRiders";
import type { Manifest } from "@/features/manifests/types";

export function ManifestSealedSection({ manifest }: { manifest: Manifest }) {
  const [vehiclePlate, setVehiclePlate] = useState("");
  const [driverId, setDriverId] = useState<string | undefined>(undefined);
  const [error, setError] = useState<string | null>(null);
  const { data: riders } = useRiders();
  const dispatchManifest = useDispatchManifest(manifest.id);

  async function handleDispatch() {
    if (!vehiclePlate.trim()) {
      setError("Vehicle plate is required.");
      return;
    }
    setError(null);
    try {
      await dispatchManifest.mutateAsync({ vehiclePlate: vehiclePlate.trim(), driverId });
    } catch {
      setError("Couldn't dispatch this manifest. Check the fields and try again.");
    }
  }

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="vehiclePlate">Vehicle Plate</Label>
        <Input id="vehiclePlate" value={vehiclePlate} onChange={(e) => setVehiclePlate(e.target.value)} />
      </div>
      <div className="space-y-2">
        <Label htmlFor="driverId">Driver</Label>
        <Select value={driverId} onValueChange={setDriverId}>
          <SelectTrigger id="driverId" aria-label="Driver">
            <SelectValue placeholder="Select driver (optional)" />
          </SelectTrigger>
          <SelectContent>
            {riders?.map((r) => (
              <SelectItem key={r.id} value={r.id}>{r.licenseNumber}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      {error && <p className="text-sm text-destructive">{error}</p>}
      <Button type="button" disabled={dispatchManifest.isPending} onClick={handleDispatch}>
        {dispatchManifest.isPending ? <Spinner className="h-4 w-4" /> : "Dispatch"}
      </Button>
    </div>
  );
}
```

- [ ] **Step 6: Implement `ManifestDetailSheet`'s skeleton (Draft/Sealed sections wired; InTransit/
  Received/Closed added in Task 10)**

Create `src/web/src/features/manifests/components/ManifestDetailSheet.tsx`:

```typescript
"use client";

import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useManifest } from "@/features/manifests/hooks/useManifest";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";
import { ManifestDraftSection } from "@/features/manifests/components/ManifestDraftSection";
import { ManifestSealedSection } from "@/features/manifests/components/ManifestSealedSection";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function ManifestDetailSheet() {
  const panel = useManifestPanelStore((s) => s.panel);
  const close = useManifestPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";

  const { data: manifest, isLoading, isError, refetch } = useManifest(id);
  const { data: branches } = useBranches();
  const branchName = (branchId: string) => branches?.find((b) => b.id === branchId)?.name ?? "—";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{manifest?.code ?? "Manifest"}</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && (
            <ErrorState
              title="Couldn't load this manifest"
              description="It may not exist, or you may not have access to it."
              onRetry={() => refetch()}
            />
          )}
          {manifest && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Status</span>
                  <StatusBadge status={manifest.status} namespace="Manifest" />
                </div>
                <Field label="Purpose" value={manifest.purpose} />
                <Field label="From Branch" value={branchName(manifest.fromBranchId)} />
                <Field label="To Branch" value={branchName(manifest.toBranchId)} />
                <Field label="Shipment Count" value={String(manifest.shipmentCount)} />
                <Field label="Created At" value={new Date(manifest.createdAt).toLocaleString()} />
              </div>

              {manifest.status === "Draft" && <ManifestDraftSection manifest={manifest} />}
              {manifest.status === "Sealed" && <ManifestSealedSection manifest={manifest} />}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 7: Run the full test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: both PASS.

- [ ] **Step 8: Commit**

```bash
git add src/web/src/features/manifests/components/ManifestDetailSheet.tsx src/web/src/features/manifests/components/ManifestDraftSection.tsx src/web/src/features/manifests/components/ManifestDraftSection.test.tsx src/web/src/features/manifests/components/ManifestSealedSection.tsx
git commit -m "feat(web): add manifest detail Sheet with Draft (scan/seal) and Sealed (dispatch) sections"
```

---

### Task 10: `ManifestDetailSheet` — InTransit, Received, and Closed sections

**Files:**
- Create: `src/web/src/features/manifests/components/ManifestInTransitSection.tsx`
- Create: `src/web/src/features/manifests/components/ManifestReceivedSection.tsx`
- Modify: `src/web/src/features/manifests/components/ManifestDetailSheet.tsx`
- Test: `src/web/src/features/manifests/components/ManifestReceivedSection.test.tsx`

**Interfaces:**
- Consumes: `useReceiveScanManifestShipment`, `useMissingShipments`, `useCloseManifest` (Task 5).
- Produces: `<ManifestInTransitSection manifest />`, `<ManifestReceivedSection manifest />`, wired
  into `ManifestDetailSheet`.

- [ ] **Step 1: Implement `ManifestInTransitSection` (no dedicated test — the same
  scan-input-plus-session-list shape as `ManifestDraftSection`, already covered by that test;
  writing an equivalent duplicate test here would only re-verify identical logic)**

Create `src/web/src/features/manifests/components/ManifestInTransitSection.tsx`:

```typescript
"use client";

import { useState } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useReceiveScanManifestShipment } from "@/features/manifests/hooks/useReceiveScanManifestShipment";
import { manifestScanErrorMessage } from "@/features/manifests/lib/manifestErrorMessages";
import type { Manifest, ManifestShipmentScan } from "@/features/manifests/types";

function parseScanError(error: unknown): string {
  try {
    const parsed = JSON.parse((error as Error).message);
    const reason = parsed?.error?.reason;
    if (typeof reason === "string") {
      return manifestScanErrorMessage(reason);
    }
  } catch {
    // fall through to generic message
  }
  return "Couldn't scan this shipment. Check the AWB and try again.";
}

export function ManifestInTransitSection({ manifest }: { manifest: Manifest }) {
  const [awbInput, setAwbInput] = useState("");
  const [scanned, setScanned] = useState<ManifestShipmentScan[]>([]);
  const [scanError, setScanError] = useState<string | null>(null);
  const receiveScan = useReceiveScanManifestShipment(manifest.id);

  async function handleScan() {
    if (!awbInput.trim()) return;
    setScanError(null);
    try {
      const result = await receiveScan.mutateAsync(awbInput.trim());
      setScanned((prev) => [result, ...prev]);
      setAwbInput("");
    } catch (error) {
      setScanError(parseScanError(error));
    }
  }

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="receive-scan-awb">Receive Scan AWB</Label>
        <Input
          id="receive-scan-awb"
          aria-label="Receive scan AWB"
          value={awbInput}
          onChange={(e) => setAwbInput(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              handleScan();
            }
          }}
          placeholder="Scan or type an AWB, then press Enter"
        />
        {scanError && <p className="text-sm text-destructive">{scanError}</p>}
      </div>
      {scanned.length > 0 && (
        <ul className="space-y-1 text-sm">
          {scanned.map((s) => (
            <li key={s.id}>{s.awb}</li>
          ))}
        </ul>
      )}
      <p className="text-xs text-muted-foreground">
        This manifest moves to Received once every scanned shipment has been received, or you can
        close it with missing shipments flagged.
      </p>
    </div>
  );
}
```

- [ ] **Step 2: Write the failing test for `ManifestReceivedSection`**

Create `src/web/src/features/manifests/components/ManifestReceivedSection.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ManifestReceivedSection } from "./ManifestReceivedSection";
import * as clientFetchModule from "@/lib/api/clientFetch";
import type { Manifest } from "@/features/manifests/types";

const manifest: Manifest = {
  id: "m1",
  code: "MNF-20260829-001",
  fromBranchId: "b1",
  toBranchId: "b2",
  purpose: "Forward",
  vehiclePlate: "BA 2 KHA 1234",
  driverId: null,
  status: "Received",
  shipmentCount: 2,
  createdAt: "2026-08-29T00:00:00Z",
};

function renderWithProviders() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ManifestReceivedSection manifest={manifest} />
    </QueryClientProvider>
  );
}

describe("ManifestReceivedSection", () => {
  it("shows the missing-shipments list from the server", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([
      { shipmentId: "s1", awb: "AWB999", scannedAt: "2026-08-29T00:00:00Z" },
    ]);
    renderWithProviders();

    await waitFor(() => expect(screen.getByText("AWB999")).toBeInTheDocument());
  });

  it("shows no missing shipments when the list is empty", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([]);
    renderWithProviders();

    await waitFor(() => expect(screen.getByText(/no missing shipments/i)).toBeInTheDocument());
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/web && npm test -- ManifestReceivedSection.test.tsx`
Expected: FAIL — `./ManifestReceivedSection` does not exist yet.

- [ ] **Step 4: Implement `ManifestReceivedSection`**

Create `src/web/src/features/manifests/components/ManifestReceivedSection.tsx`:

```typescript
"use client";

import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
import { useMissingShipments } from "@/features/manifests/hooks/useMissingShipments";
import { useCloseManifest } from "@/features/manifests/hooks/useCloseManifest";
import type { Manifest } from "@/features/manifests/types";

export function ManifestReceivedSection({ manifest }: { manifest: Manifest }) {
  const { data: missing, isLoading } = useMissingShipments(manifest.id, true);
  const closeManifest = useCloseManifest(manifest.id);

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <span className="text-xs text-muted-foreground">Missing Shipments</span>
        {isLoading && <Spinner className="h-4 w-4" />}
        {!isLoading && missing && missing.length === 0 && (
          <p className="text-sm text-muted-foreground">No missing shipments.</p>
        )}
        {!isLoading && missing && missing.length > 0 && (
          <ul className="space-y-1 text-sm">
            {missing.map((m) => (
              <li key={m.shipmentId} className="text-destructive">{m.awb}</li>
            ))}
          </ul>
        )}
      </div>
      <Button type="button" disabled={closeManifest.isPending} onClick={() => closeManifest.mutate()}>
        {closeManifest.isPending ? <Spinner className="h-4 w-4" /> : "Close Manifest"}
      </Button>
    </div>
  );
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/web && npm test -- ManifestReceivedSection.test.tsx`
Expected: PASS.

- [ ] **Step 6: Wire both new sections plus a Closed read-only note into `ManifestDetailSheet`**

Modify `src/web/src/features/manifests/components/ManifestDetailSheet.tsx` — add the two new
imports and status branches, plus a terminal note for Closed:

```typescript
import { ManifestInTransitSection } from "@/features/manifests/components/ManifestInTransitSection";
import { ManifestReceivedSection } from "@/features/manifests/components/ManifestReceivedSection";
```

Find this exact block (the end of Task 9's `manifest &&` fragment):

```typescript
              {manifest.status === "Draft" && <ManifestDraftSection manifest={manifest} />}
              {manifest.status === "Sealed" && <ManifestSealedSection manifest={manifest} />}
            </>
          )}
```

Replace it with:

```typescript
              {manifest.status === "Draft" && <ManifestDraftSection manifest={manifest} />}
              {manifest.status === "Sealed" && <ManifestSealedSection manifest={manifest} />}
              {manifest.status === "InTransit" && <ManifestInTransitSection manifest={manifest} />}
              {manifest.status === "Received" && <ManifestReceivedSection manifest={manifest} />}
              {manifest.status === "Closed" && (
                <p className="text-sm text-muted-foreground">
                  This manifest is closed. No further action is possible.
                </p>
              )}
            </>
          )}
```

- [ ] **Step 7: Run the full test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: both PASS.

- [ ] **Step 8: Commit**

```bash
git add src/web/src/features/manifests/components/ManifestInTransitSection.tsx src/web/src/features/manifests/components/ManifestReceivedSection.tsx src/web/src/features/manifests/components/ManifestReceivedSection.test.tsx src/web/src/features/manifests/components/ManifestDetailSheet.tsx
git commit -m "feat(web): add manifest detail Sheet's InTransit (receive-scan), Received (missing-shipments + close), and Closed sections"
```

---

### Task 11: Wire into staff layout; final verification; close out Phase F4

**Files:**
- Modify: `src/web/src/app/(staff)/layout.tsx`
- Modify: `docs/FRONTEND-ROADMAP.md`

**Interfaces:**
- Consumes: everything from Tasks 1-10.
- Produces: the complete Phase F4 feature — this task's exit criteria is the phase's exit
  criteria.

- [ ] **Step 1: Mount the manifest Sheets globally**

Modify `src/web/src/app/(staff)/layout.tsx`:

```typescript
import type { ReactNode } from "react";
import { AppShell } from "@/components/layout/AppShell";
import { BranchCreateSheet } from "@/features/branches/components/BranchCreateSheet";
import { BranchDetailSheet } from "@/features/branches/components/BranchDetailSheet";
import { ShipmentCreateSheet } from "@/features/shipments/components/ShipmentCreateSheet";
import { ShipmentDetailSheet } from "@/features/shipments/components/ShipmentDetailSheet";
import { ManifestCreateSheet } from "@/features/manifests/components/ManifestCreateSheet";
import { ManifestDetailSheet } from "@/features/manifests/components/ManifestDetailSheet";

export default function StaffLayout({ children }: { children: ReactNode }) {
  return (
    <AppShell>
      {children}
      <BranchCreateSheet />
      <BranchDetailSheet />
      <ShipmentCreateSheet />
      <ShipmentDetailSheet />
      <ManifestCreateSheet />
      <ManifestDetailSheet />
    </AppShell>
  );
}
```

- [ ] **Step 2: Run the full frontend test suite**

Run: `cd src/web && npm test`
Expected: PASS — every test added across Tasks 1-11.

- [ ] **Step 3: Run the frontend build**

Run: `cd src/web && npm run build`
Expected: build succeeds with no type errors; `/manifests` appears in the route list.

- [ ] **Step 4: Run the full backend test suite one more time**

Run: `dotnet test`
Expected: PASS except the known pre-existing flaky
`NotificationsAndWebhooksEndToEndTests.WebhookSubscription_...` test.

- [ ] **Step 5: Manual end-to-end verification**

Start Postgres/API (reusing the shared containers, API rebuilt per Task 2) and the frontend dev
server. In a browser, as a registered org owner with `hasAllBranchesScope: true`:

1. Navigate to `/manifests`. Confirm the list renders (empty state on a fresh org) and
   "New Manifest" is visible.
2. Click "New Manifest". Confirm it opens as an instant right-side Sheet. Pick From/To branch
   (create a second branch via the API directly first if the org only has one — a manifest needs
   two distinct branches), Purpose "Forward". Submit; confirm it switches straight to the detail
   Sheet, status "Draft".
3. Create at least one shipment first via `/shipments` (needed to have a real AWB to scan) whose
   origin branch matches the manifest's From Branch and is in a scannable state (per
   `BRANCH-OPERATIONS.md` §1's five-check scan validation — check what state a freshly-created
   shipment is actually in and whether it needs a `receive-origin` scan first before it can be
   manifest-scanned; adjust the manual steps here based on what the real validation actually
   requires, don't assume). Scan that AWB in the Draft section; confirm it appears in the
   session-scanned list and the header's Shipment Count increments after refetch.
4. Click "Seal Manifest"; confirm status moves to "Sealed" and the dispatch form appears.
5. Fill Vehicle Plate (and Driver if any riders exist), click "Dispatch"; confirm status moves
   straight to "InTransit" (no separate "Dispatched" status, per the domain design).
6. In the InTransit section, receive-scan the same AWB; confirm it appears in that section's
   session-scanned list.
7. Manually trigger the manifest's move to "Received" the way the real operational flow does
   (check whether `receive-scan` itself transitions the manifest status once every scanned
   shipment has been received, or whether a separate action is needed — verify against
   `ManifestService.Transitions.cs`'s actual logic rather than assuming). Once Received, confirm
   the missing-shipments panel renders (empty, since the one shipment was received) and "Close
   Manifest" is visible.
8. Click "Close Manifest"; confirm status moves to "Closed" and the read-only terminal note
   appears.
9. Navigate back to `/manifests`; confirm the manifest's row shows "Closed" via `StatusBadge`
   with the correct new color (not falling through to the generic gray default), and that the
   Purpose/From/To/Shipment Count columns are all correct.
10. Confirm a user without branch access to either side of the manifest does not see it in the
    list (the real point of Task 2's backend addition) — verify via the API directly with a
    second, unrelated branch-scoped user if convenient, rather than only trusting the unit test.

- [ ] **Step 6: Update the roadmap status**

In `docs/FRONTEND-ROADMAP.md`, replace Phase F4's `*Status: Not started.*` line with a completion
note in the same style as Phases F0-F3's, written only after Step 5's manual verification actually
passes — including the backend list-endpoint addition, the manual-AWB-only scanning decision, and
any real bugs Step 5 surfaces (following this session's established pattern of writing up exactly
what manual E2E verification found, not just declaring success).

- [ ] **Step 7: Commit**

```bash
git add "src/web/src/app/(staff)/layout.tsx" docs/FRONTEND-ROADMAP.md
git commit -m "feat(web): wire manifest sheets into staff layout; close out Phase F4"
```
