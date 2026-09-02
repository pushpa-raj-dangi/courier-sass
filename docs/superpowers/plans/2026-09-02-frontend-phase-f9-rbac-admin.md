# Frontend Phase F9 — RBAC/Admin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an `OrganizationAdmin` create a custom role with a chosen permission subset, invite a new user by email into a role, and grant that user temporary branch access — entirely through the staff back-office UI, closing the much larger backend gap this phase found (Identity had almost no Role/Permission/UserBranch/invite management endpoints at all).

**Architecture:** 13 backend tasks build out `src/Modules/Identity/`'s missing management surface (Role CRUD, a grouped Permission catalog endpoint, UserBranch grant/list/revoke, paginated/enriched user list, deactivate/reactivate, an email-based invite flow backed by a new `UserInvite` entity, and a small additive extension to `NotificationDispatchService` so it can resolve a recipient directly from the event payload instead of only via `MerchantId`), then 6 frontend tasks build `src/web/src/features/admin/` following the exact Sheet-over-pages/Zustand-panel-store/BFF-passthrough conventions F2–F8 already established.

**Tech Stack:** ASP.NET Core / EF Core / xUnit (backend); Next.js 14 App Router, TanStack Query, Zustand, React Hook Form + Zod, Vitest + Testing Library (frontend). No new npm/NuGet packages.

**Spec:** `docs/superpowers/specs/2026-09-02-frontend-phase-f9-rbac-admin-design.md`

## Global Constraints

- Detail/create UI is always a right-side `Sheet` driven by a Zustand store, never a routed page (`docs/ARCHITECTURE.md` §9a), **except** the accept-invite page (Task 19), which is unauthenticated and outside the `(staff)` layout entirely — the one legitimate exception, matching how `/login` already works.
- Price, permissions, and branch scope are never trusted from the frontend — every new screen only reads/writes through the existing permission-checked backend endpoints (CLAUDE.md §33).
- Every new screen gets loading/error/empty states using the existing `LoadingState`/`ErrorState`/`EmptyState`/`Spinner` primitives — no ad hoc spinners.
- `BranchScope` serializes as a PascalCase string once Task 1 lands — every later task's TypeScript types use string literal unions, never numbers.
- `role.manage` gates every Role/Permission read and write. `user.read` gates the Users list and a user's own detail view (including their branch-grant list). `user.create` gates inviting a user. `user.update` gates deactivate/reactivate and branch-grant add/remove. Gate with the existing `usePermission(permission)` hook on the frontend and `RequirePermission(...)` on the backend, same as every other feature.
- Every mutating operation in `RoleService`/`UserManagementService` writes an `IAuditLogger` entry (CLAUDE.md §28 explicitly lists role changes, permission changes, and user changes as auditable) — action strings: `role.created`, `role.updated`, `role.deleted`, `user.invited`, `user.deactivated`, `user.reactivated`, `user.branch_granted`, `user.branch_revoked`.
- No new nav route exists for `/admin` yet — Task 16 adds it to `NAV_ITEMS` in `src/web/src/components/nav-main.tsx`, gated on `role.manage` OR `user.read` (visible if the user holds either).
- Backend test command: `dotnet test tests/Identity.Tests/Identity.Tests.csproj` (unit, EF InMemory for Tasks 1/2/4/5, real Postgres via `TestDbContextFactory` for Task 6+ where branch/user rows need real FKs — see each task) and `dotnet test tests/Api.Tests/Api.Tests.csproj --filter <TestClassName>` (integration, needs the local Postgres container on port 5434 per `[[Courier Local Postgres]]` — already running, do not start a second one). Frontend: `cd src/web && npm test` (Vitest) and `npm run build` (production build/type-check) after every frontend task.
- Migration command template (used once, in Task 9 — every other backend task changes no schema):

```bash
dotnet ef migrations add <Name> --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj
dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj --connection "Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password"
```

---

## Task 1: `BranchScope` JSON converter; record the whole phase's backend gaps in CLARIFICATIONS.md

**Files:**
- Modify: `src/Modules/Identity/Domain/BranchScope.cs`
- Modify: `docs/CLARIFICATIONS.md`
- Test: `tests/Identity.Tests/BranchScopeSerializationTests.cs`

**Interfaces:**
- Produces: `BranchScope` now carries `[JsonConverter(typeof(JsonStringEnumConverter))]`, matching `BranchType`/`ShipmentStatus`/`ManifestPurpose`/`ManifestStatus`/the 5 Riders enums/the 3 Ndr enums/the 3 Cod enums' existing fix. Every later task's `UserRoleResponse`-shaped DTOs serialize `BranchScope` as a string, never a number.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using Courier.Identity.Domain;
using Xunit;

namespace Courier.Identity.Tests;

public class BranchScopeSerializationTests
{
    [Fact]
    public void BranchScope_SerializesAsString() =>
        Assert.Equal("\"SpecificBranches\"", JsonSerializer.Serialize(BranchScope.SpecificBranches));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter BranchScopeSerializationTests`
Expected: FAIL — gets `"0"` instead of `"SpecificBranches"`.

- [ ] **Step 3: Add the converter**

`src/Modules/Identity/Domain/BranchScope.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Courier.Identity.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BranchScope
{
    SpecificBranches,
    AllBranches
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter BranchScopeSerializationTests`
Expected: PASS

- [ ] **Step 5: Record this phase's backend gaps in CLARIFICATIONS.md**

Append a new entry after entry 17 (`## 17. Frontend Phase F7: ...`), before the `## Summary of schema additions implied by this document` section:

```markdown
## 18. Frontend Phase F9: `Identity` had almost no Role/Permission/UserBranch/invite management surface at all

**Where:** `Identity.Domain.BranchScope`, `Identity.Application.AuthService`/`RoleService`/`UserManagementService`, `Identity.Presentation.RolesEndpoints`/`UsersEndpoints`/`AuthEndpoints`, `Notifications.Application.NotificationDispatchService`

**Gap 1 — no `JsonStringEnumConverter` on `BranchScope`.** Same latent bug class hit and fixed in
every prior phase (`BranchType` F2, `ShipmentStatus` F3, `ManifestPurpose`/`ManifestStatus` F4, 5
Riders enums F5, 3 Ndr enums F6, 3 Cod enums F7). Fixed proactively here.

**Gap 2 — no Role CRUD, no Permission catalog read endpoint, no RolePermission assignment
endpoint.** The `Role`/`Permission`/`UserRole` domain model has carried the shape needed for all
of this since Phase 1 (`Role.OrganizationId` non-null already distinguishes a custom org role from
a `null`-owned platform role, `Role.IsSystemDefault` already distinguishes a seeded role), but
zero HTTP surface existed to create, edit, delete, or even list a role, or to see what permissions
exist to assign. **Resolution:** `GET /api/v1/permissions` (grouped catalog), `GET/POST/PUT/DELETE
/api/v1/roles[/{id}]`, all `role.manage`-gated (Tasks 3–5).

**Gap 3 — only 3 of CLAUDE.md §6's 17 default roles were ever seeded** (`OrganizationOwner`,
`DeliveryRider`, `PickupRider`, added incrementally across Phases 1 and 8). Any org wanting e.g. a
`FinanceManager` or `BranchManager` role had no way to get one short of hand-rolling a custom role
through this phase's own new Role UI. **Resolution:** 11 more org-scoped roles from CLAUDE.md §6
seeded at registration (`OrganizationAdmin`, `OperationsManager`, `BranchManager`, `Dispatcher`,
`SortingStaff`, `CounterStaff`, `FinanceManager`, `FinanceStaff`, `CustomerSupport`, `Auditor`,
`HubStaff` — 14 seeded roles total), permission sets grounded in `docs/RBAC.md` §4's illustrative
coverage table where it names one explicitly, reasoned from the same table's role descriptions
otherwise (Task 2). `SuperAdmin` (platform-level, `OrganizationId = null`) and `Merchant`/
`MerchantStaff` (the existing `MerchantUser`/API-key auth track, `docs/RBAC.md` §5, a wholly
separate identity space) are deliberately excluded — no platform-admin UI exists or is planned
this phase, and merchant-side roles belong to Frontend Phase F10's merchant portal, not this one.

**Gap 4 — no `UserBranch` grant/list/revoke endpoint at all**, despite
`BranchAuthorizationChecker.GetAccessibleBranchIdsAsync` (`Infrastructure/
BranchAuthorizationChecker.cs`) already reading and correctly enforcing `UserBranch.ExpiresAt` —
confirmed by the pre-existing `HasAccessAsync_ExpiredGrant_IsRejected`/
`HasAccessAsync_ExpiredAndValidGrants_FiltersPerRow` tests in
`tests/Identity.Tests/BranchAuthorizationCheckerTests.cs`, which needed no changes this phase. The
enforcement was real; only the management surface to create/see/revoke a grant was missing.
**Resolution:** `GET/POST /api/v1/users/{id}/branches`, `DELETE /api/v1/users/{id}/branches/
{branchId}`, `user.read`/`user.update`-gated (Task 6).

**Gap 5 — `GET /api/v1/users` had no pagination and returned only `{Id, Email, FullName,
IsActive}`, with no role or branch-grant visibility.** Its missing explicit `OrganizationId`
filter is *not* a cross-tenant bug — `Persistence/AppDbContext.cs` already registers a global
`HasQueryFilter` on `User` scoped to the ambient tenant (line 328) — but the endpoint was
otherwise unusable for an admin screen. **Resolution:** pagination + `RoleName`/`BranchCount`
enrichment (Task 7).

**Gap 6 — no user deactivate/reactivate endpoint**, despite `AuthService.LoginAsync`/
`RefreshTokenAsync` already correctly rejecting an inactive `User.IsActive == false` account — the
enforcement existed, the toggle didn't. **Resolution:** `PATCH /api/v1/users/{id}/deactivate` /
`/reactivate`, `user.update`-gated (Task 8).

**Gap 7 — no user-invite flow of any kind**, and the one adjacent mechanism
(`IUserProvisioningService.ProvisionStaffUserAsync`) is deliberately narrow (an upfront plaintext
password, a role name that must already be seeded, a single branch — built for Riders' internal
self-service rider creation, not a general admin invite). **Resolution:** a new `UserInvite`
entity (Task 9) plus `POST /api/v1/users/invite` (Task 11) and `POST /api/v1/auth/accept-invite`
(Task 12), following the email-invite design decision recorded in the phase's design spec.

**Gap 8 (discovered building Gap 7's resolution) — `NotificationDispatchService.
DispatchEventAsync` could only resolve a recipient by reading `MerchantId` from the event payload
and looking it up via `IMerchantContactLookupService`; there was no path for an event whose
payload already carries its own recipient address**, which the `user.invited` event needs (the
invited user is not a merchant). **Resolution:** an additive `RecipientEmail` payload-property
check before the existing `MerchantId` branch (Task 10) — existing `MerchantId`-keyed events are
untouched.

Discovered planning Frontend Phase F9 (`docs/FRONTEND-ROADMAP.md`,
`docs/superpowers/specs/2026-09-02-frontend-phase-f9-rbac-admin-design.md`). Implemented as
Tasks 1–13 of the Frontend Phase F9 plan.

---
```

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Identity/Domain/BranchScope.cs docs/CLARIFICATIONS.md tests/Identity.Tests/BranchScopeSerializationTests.cs
git commit -m "fix(identity): serialize BranchScope as a string; record Frontend Phase F9's backend gaps"
```

---

## Task 2: Seed the 11 additional CLAUDE.md §6 roles at registration

**Files:**
- Create: `src/Modules/Identity/Application/AdditionalRoleSeeds.cs`
- Modify: `src/Modules/Identity/Application/AuthService.cs`
- Test: `tests/Identity.Tests/AuthServiceTests.cs`

**Interfaces:**
- Consumes: `PermissionCatalog.All` (existing).
- Produces: `AdditionalRoleSeeds.ByRoleName` — `IReadOnlyDictionary<string, string[]>` mapping each of the 11 new role names to its permission-key set. `RegisterOrganizationAsync` now seeds 14 roles total (the existing `OrganizationOwner`/`DeliveryRider`/`PickupRider` plus these 11), all `IsSystemDefault = true`.

- [ ] **Step 1: Write the failing test**

Add to `tests/Identity.Tests/AuthServiceTests.cs`, after `RegisterOrganizationAsync_ThenLoginAsync_Succeeds`:

```csharp
    [Fact]
    public async Task RegisterOrganizationAsync_Seeds14Roles_WithExpectedPermissionSets()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Test Org {suffix}", $"test-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");

        var result = await service.RegisterOrganizationAsync(request);

        var roles = await dbContext.Set<Role>()
            .Where(r => r.OrganizationId == result.OrganizationId)
            .Include(r => r.Permissions)
            .ToListAsync();
        Assert.Equal(14, roles.Count);
        Assert.All(roles, r => Assert.True(r.IsSystemDefault));

        var financeManager = roles.Single(r => r.Name == "FinanceManager");
        Assert.Contains(financeManager.Permissions, p => p.Key == "cod.settle");
        Assert.Contains(financeManager.Permissions, p => p.Key == "cod.adjust");

        var financeStaff = roles.Single(r => r.Name == "FinanceStaff");
        Assert.DoesNotContain(financeStaff.Permissions, p => p.Key == "cod.adjust");

        var auditor = roles.Single(r => r.Name == "Auditor");
        Assert.All(auditor.Permissions, p => Assert.True(p.Key.EndsWith(".read") || p.Key == "audit.read"));

        var organizationAdmin = roles.Single(r => r.Name == "OrganizationAdmin");
        Assert.DoesNotContain(organizationAdmin.Permissions, p => p.Key == "org.billing.manage");
        Assert.DoesNotContain(organizationAdmin.Permissions, p => p.Key == "org.ownership.transfer");
        Assert.DoesNotContain(organizationAdmin.Permissions, p => p.Key == "org.delete");
    }
```

Add `using Microsoft.EntityFrameworkCore;` to the top of the file if not already present (it is — `AuthServiceTests.cs` doesn't currently reference EF Core directly, so check and add it).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter RegisterOrganizationAsync_Seeds14Roles_WithExpectedPermissionSets`
Expected: FAIL — only 3 roles exist.

- [ ] **Step 3: Write `AdditionalRoleSeeds`**

`src/Modules/Identity/Application/AdditionalRoleSeeds.cs`:

```csharp
namespace Courier.Identity.Application;

// The 11 CLAUDE.md §6 default roles not already seeded by RegisterOrganizationAsync
// (OrganizationOwner, DeliveryRider, PickupRider predate this phase). Permission sets are
// grounded in docs/RBAC.md §4's illustrative coverage table where it names a role explicitly
// (BranchManager, FinanceManager/FinanceStaff, Auditor), reasoned from that table's role
// descriptions and CLAUDE.md §6's own role names otherwise. SuperAdmin (platform-level, no
// OrganizationId) and Merchant/MerchantStaff (docs/RBAC.md §5's separate MerchantUser identity
// track) are deliberately excluded — see CLARIFICATIONS.md #18.
public static class AdditionalRoleSeeds
{
    public static readonly IReadOnlyDictionary<string, string[]> ByRoleName = new Dictionary<string, string[]>
    {
        // "Same as Owner minus billing/ownership/delete" — docs/RBAC.md §2.
        ["OrganizationAdmin"] = PermissionCatalog.All
            .Select(p => p.Key)
            .Except(new[] { "org.billing.manage", "org.ownership.transfer", "org.delete" })
            .ToArray(),
        ["OperationsManager"] = new[]
        {
            "shipment.create", "shipment.read", "shipment.update", "shipment.cancel", "shipment.assign",
            "shipment.scan", "shipment.sort", "shipment.transfer", "delivery.assign",
            "manifest.read", "manifest.create", "manifest.seal", "manifest.dispatch", "manifest.receive", "manifest.close",
            "rider.read", "rider.assign", "ndr.read", "ndr.manage", "ndr.resolve", "rto.read", "rto.manage",
            "branch.read", "zone.read", "report.read"
        },
        // docs/RBAC.md §4's exact BranchManager example.
        ["BranchManager"] = new[]
        {
            "shipment.read", "shipment.assign", "shipment.scan",
            "manifest.read", "manifest.create", "manifest.seal", "manifest.dispatch", "manifest.receive", "manifest.close",
            "delivery.assign", "ndr.manage", "ndr.read", "cod.reconcile", "cod.read", "branch.read"
        },
        ["Dispatcher"] = new[]
        {
            "manifest.read", "manifest.create", "manifest.seal", "manifest.dispatch", "manifest.receive", "manifest.close",
            "shipment.transfer", "shipment.read", "branch.read"
        },
        ["SortingStaff"] = new[] { "shipment.scan", "shipment.sort", "manifest.read" },
        ["CounterStaff"] = new[] { "shipment.create", "shipment.read", "shipment.update", "pricing.read" },
        // docs/RBAC.md §4: "cod.* including cod.adjust and cod.settle, report.read/export".
        ["FinanceManager"] = new[] { "cod.read", "cod.collect", "cod.reconcile", "cod.settle", "cod.adjust", "report.read", "report.export" },
        // docs/RBAC.md §4: "COD reconciliation, no adjustment approval" — settle also excluded,
        // an approval-adjacent action, matching FinanceManager/FinanceStaff's stated split.
        ["FinanceStaff"] = new[] { "cod.read", "cod.reconcile" },
        ["CustomerSupport"] = new[] { "shipment.read", "ndr.read", "ndr.resolve" },
        // docs/RBAC.md §4: "*.read + audit.read across the org, no write permissions anywhere."
        ["Auditor"] = PermissionCatalog.All
            .Select(p => p.Key)
            .Where(k => k.EndsWith(".read") || k == "audit.read")
            .ToArray(),
        ["HubStaff"] = new[]
        {
            "manifest.read", "manifest.create", "manifest.seal", "manifest.dispatch", "manifest.receive", "manifest.close",
            "shipment.scan", "shipment.sort", "branch.read"
        },
    };
}
```

- [ ] **Step 4: Wire the seed loop into `RegisterOrganizationAsync`**

In `src/Modules/Identity/Application/AuthService.cs`, insert immediately after the `pickupRiderRole` block (after the `foreach (var permission in allPermissions.Where(p => p.Key == "shipment.scan")) { pickupRiderRole.Permissions.Add(permission); }` loop, before the `var owner = new User { ... }` line):

```csharp
        // The 11 CLAUDE.md §6 roles not already seeded above (Frontend Phase F9,
        // CLARIFICATIONS.md #18) — same "load once, join tracked Permission rows" pattern as
        // OrganizationOwner/DeliveryRider/PickupRider.
        foreach (var (roleName, permissionKeys) in AdditionalRoleSeeds.ByRoleName)
        {
            var role = new Role
            {
                Id = Guid.NewGuid(),
                OrganizationId = organization.Id,
                Name = roleName,
                IsSystemDefault = true
            };
            _dbContext.Set<Role>().Add(role);
            foreach (var permission in allPermissions.Where(p => permissionKeys.Contains(p.Key)))
            {
                role.Permissions.Add(permission);
            }
        }

```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter AuthServiceTests`
Expected: PASS (every existing `AuthServiceTests` test plus the new one — registration/login/refresh flows are unaffected by additional roles existing).

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Identity/Application/AdditionalRoleSeeds.cs src/Modules/Identity/Application/AuthService.cs tests/Identity.Tests/AuthServiceTests.cs
git commit -m "feat(identity): seed the 11 remaining CLAUDE.md role names at registration"
```

---

## Task 3: `GET /api/v1/permissions` — grouped permission catalog

**Files:**
- Create: `src/Modules/Identity/Presentation/RolesEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Identity.Tests/RolesEndpointsGroupingTests.cs`

**Interfaces:**
- Consumes: `PermissionCatalog.All` (existing).
- Produces: `RolesEndpoints.GroupPermissions(IReadOnlyList<(string Key, string Description)> all)` — a pure static method returning `List<PermissionGroupResponse>`, grouped by the token before each key's first `.`, groups sorted alphabetically, permissions within a group in catalog order. `GET /api/v1/permissions`, `role.manage`-gated, returns this grouped shape directly. This file also declares `MapRolesEndpoints(this IEndpointRouteBuilder app)`, the extension method later tasks add routes to — mapped (but nearly empty) starting this task so `Program.cs` only needs one new line for this whole phase.

- [ ] **Step 1: Write the failing test**

```csharp
using Courier.Identity.Application;
using Courier.Identity.Presentation;
using Xunit;

namespace Courier.Identity.Tests;

public class RolesEndpointsGroupingTests
{
    [Fact]
    public void GroupPermissions_GroupsByPrefixBeforeFirstDot_SortedAlphabetically()
    {
        var groups = RolesEndpoints.GroupPermissions(PermissionCatalog.All);

        Assert.Equal(groups.Select(g => g.Group), groups.Select(g => g.Group).OrderBy(g => g, StringComparer.Ordinal));

        var shipmentGroup = groups.Single(g => g.Group == "shipment");
        Assert.Contains(shipmentGroup.Permissions, p => p.Key == "shipment.create");
        Assert.Contains(shipmentGroup.Permissions, p => p.Key == "shipment.transfer");
        Assert.DoesNotContain(shipmentGroup.Permissions, p => p.Key == "delivery.assign");

        // "org.billing.manage" has two dots — the group is the token before the FIRST dot.
        var orgGroup = groups.Single(g => g.Group == "org");
        Assert.Contains(orgGroup.Permissions, p => p.Key == "org.billing.manage");

        var totalPermissions = groups.Sum(g => g.Permissions.Count);
        Assert.Equal(PermissionCatalog.All.Count, totalPermissions);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter RolesEndpointsGroupingTests`
Expected: FAIL — `RolesEndpoints` doesn't exist yet (compile error).

- [ ] **Step 3: Write `RolesEndpoints.cs`**

`src/Modules/Identity/Presentation/RolesEndpoints.cs`:

```csharp
using Courier.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Identity.Presentation;

public record PermissionItem(string Key, string Description);
public record PermissionGroupResponse(string Group, IReadOnlyList<PermissionItem> Permissions);

public static class RolesEndpoints
{
    public static void MapRolesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Roles Management");

        group.MapGet("/permissions", () =>
            Results.Ok(GroupPermissions(PermissionCatalog.All)))
            .RequirePermission("role.manage");
    }

    // Public and static: no DbContext dependency (the catalog is a static in-process list, not a
    // table any caller filters), which makes this trivially unit-testable without a database.
    public static List<PermissionGroupResponse> GroupPermissions(IReadOnlyList<(string Key, string Description)> all)
    {
        return all
            .GroupBy(p => p.Key.Split('.', 2)[0])
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PermissionGroupResponse(g.Key, g.Select(p => new PermissionItem(p.Key, p.Description)).ToList()))
            .ToList();
    }
}
```

- [ ] **Step 4: Register the endpoint mapping**

In `src/Api/Program.cs`, insert `app.MapRolesEndpoints();` immediately after the existing `app.MapUsersEndpoints();` line.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter RolesEndpointsGroupingTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Identity/Presentation/RolesEndpoints.cs src/Api/Program.cs tests/Identity.Tests/RolesEndpointsGroupingTests.cs
git commit -m "feat(identity): add GET /api/v1/permissions grouped catalog endpoint"
```

---

## Task 4: `RoleService` — `GET /api/v1/roles`, `POST /api/v1/roles`

**Files:**
- Create: `src/Modules/Identity/Application/RoleDtos.cs`
- Create: `src/Modules/Identity/Application/RoleService.cs`
- Modify: `src/Modules/Identity/Presentation/RolesEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Identity.Tests/RoleServiceListCreateTests.cs`

**Interfaces:**
- Consumes: `ICurrentTenantProvider.OrganizationId`, `IAuditLogger.LogAsync`, `Role`/`Permission`/`UserRole` (existing domain types), `RolesEndpoints.GroupPermissions` (unused here — noted for Task 5's reuse).
- Produces: `RoleResponse(Guid Id, string Name, bool IsSystemDefault, IReadOnlyList<string> PermissionKeys)`, `CreateRoleRequest(string Name, IReadOnlyList<string> PermissionKeys)`, `RoleValidationException`. `RoleService.ListRolesAsync(CancellationToken)` returning `Task<IReadOnlyList<RoleResponse>>`. `RoleService.CreateRoleAsync(CreateRoleRequest, Guid actingUserId, CancellationToken)` returning `Task<RoleResponse>`. `GET /api/v1/roles`, `POST /api/v1/roles`, both `role.manage`-gated.

- [ ] **Step 1: Write the failing tests**

```csharp
using Courier.Audit.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Identity.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

public class RoleServiceListCreateTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RoleConfiguration());
            modelBuilder.ApplyConfiguration(new PermissionConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static RoleService CreateService(TestDbContext dbContext, Guid organizationId) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger());

    private static async Task<Role> SeedRoleAsync(TestDbContext dbContext, Guid organizationId, string name, bool isSystemDefault, params string[] permissionKeys)
    {
        var permissions = await dbContext.Set<Permission>().Where(p => permissionKeys.Contains(p.Key)).ToListAsync();
        var role = new Role { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = name, IsSystemDefault = isSystemDefault };
        foreach (var permission in permissions) role.Permissions.Add(permission);
        dbContext.Set<Role>().Add(role);
        await dbContext.SaveChangesAsync();
        return role;
    }

    [Fact]
    public async Task ListRolesAsync_ExcludesForeignOrganization()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var foreignOrganizationId = Guid.NewGuid();
        var own = await SeedRoleAsync(dbContext, organizationId, "OwnRole", true, "shipment.read");
        await SeedRoleAsync(dbContext, foreignOrganizationId, "ForeignRole", true, "shipment.read");
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListRolesAsync();

        var single = Assert.Single(result);
        Assert.Equal(own.Id, single.Id);
    }

    [Fact]
    public async Task ListRolesAsync_IncludesPermissionKeys()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        await SeedRoleAsync(dbContext, organizationId, "Dispatcher", true, "manifest.read", "manifest.create");
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListRolesAsync();

        var role = Assert.Single(result);
        Assert.Equal(new[] { "manifest.create", "manifest.read" }, role.PermissionKeys.OrderBy(k => k));
    }

    [Fact]
    public async Task CreateRoleAsync_ValidPermissionKeys_CreatesNonSystemDefaultRole()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);

        var result = await service.CreateRoleAsync(
            new CreateRoleRequest("CustomAuditor", new[] { "audit.read", "shipment.read" }), Guid.NewGuid());

        Assert.False(result.IsSystemDefault);
        Assert.Equal(new[] { "audit.read", "shipment.read" }, result.PermissionKeys.OrderBy(k => k));
        var persisted = await dbContext.Set<Role>().Include(r => r.Permissions).SingleAsync(r => r.Id == result.Id);
        Assert.Equal(organizationId, persisted.OrganizationId);
        Assert.False(persisted.IsSystemDefault);
    }

    [Fact]
    public async Task CreateRoleAsync_UnknownPermissionKey_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<RoleValidationException>(() =>
            service.CreateRoleAsync(new CreateRoleRequest("Bad", new[] { "not.a.real.permission" }), Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateRoleAsync_EmptyName_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<RoleValidationException>(() =>
            service.CreateRoleAsync(new CreateRoleRequest("", new[] { "shipment.read" }), Guid.NewGuid()));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter RoleServiceListCreateTests`
Expected: FAIL — `RoleService` doesn't exist yet (compile error).

- [ ] **Step 3: Write `RoleDtos.cs`**

`src/Modules/Identity/Application/RoleDtos.cs`:

```csharp
namespace Courier.Identity.Application;

public record RoleResponse(Guid Id, string Name, bool IsSystemDefault, IReadOnlyList<string> PermissionKeys);
public record CreateRoleRequest(string Name, IReadOnlyList<string> PermissionKeys);
public record UpdateRoleRequest(IReadOnlyList<string> PermissionKeys);

public class RoleValidationException : Exception
{
    public RoleValidationException(string message) : base(message) { }
}
```

- [ ] **Step 4: Write `RoleService.cs`**

`src/Modules/Identity/Application/RoleService.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class RoleService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;

    public RoleService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
    }

    // Role already carries a tenant HasQueryFilter (AppDbContext.cs), so Set<Role>() alone is
    // already org-scoped — this matches every other read in this module that relies on the
    // ambient filter rather than re-stating an explicit OrganizationId == ... clause.
    public async Task<IReadOnlyList<RoleResponse>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _dbContext.Set<Role>().Include(r => r.Permissions).ToListAsync(cancellationToken);
        return roles.Select(ToResponse).ToList();
    }

    public async Task<RoleResponse> CreateRoleAsync(CreateRoleRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RoleValidationException("No active organization context.");

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new RoleValidationException("Role name is required.");
        }

        var permissions = await _dbContext.Set<Permission>()
            .Where(p => request.PermissionKeys.Contains(p.Key))
            .ToListAsync(cancellationToken);
        if (permissions.Count != request.PermissionKeys.Distinct().Count())
        {
            var unknown = request.PermissionKeys.Except(permissions.Select(p => p.Key));
            throw new RoleValidationException($"Unknown permission key(s): {string.Join(", ", unknown)}");
        }

        var role = new Role
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            IsSystemDefault = false
        };
        foreach (var permission in permissions)
        {
            role.Permissions.Add(permission);
        }
        _dbContext.Set<Role>().Add(role);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "role.created", "Role", role.Id.ToString(),
            After: string.Join(",", permissions.Select(p => p.Key))), cancellationToken);

        return ToResponse(role);
    }

    private static RoleResponse ToResponse(Role role) =>
        new(role.Id, role.Name, role.IsSystemDefault, role.Permissions.Select(p => p.Key).ToList());
}
```

- [ ] **Step 5: Wire the endpoints**

In `src/Modules/Identity/Presentation/RolesEndpoints.cs`, add `using Courier.SharedKernel;` and insert immediately before the existing `group.MapGet("/permissions", ...)` line:

```csharp
        group.MapGet("/roles", async (RoleService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListRolesAsync(cancellationToken)))
            .RequirePermission("role.manage");

        group.MapPost("/roles", async (CreateRoleRequest request, RoleService service, HttpContext httpContext) =>
        {
            try
            {
                var result = await service.CreateRoleAsync(request, httpContext.User.GetUserId()!.Value, httpContext.RequestAborted);
                return Results.Ok(result);
            }
            catch (RoleValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "ROLE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("role.manage");

```

- [ ] **Step 6: Register `RoleService` in DI**

In `src/Api/Program.cs`, insert `builder.Services.AddScoped<RoleService>();` immediately after the existing `builder.Services.AddScoped<IUserProvisioningService, UserProvisioningService>();` line.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter RoleServiceListCreateTests`
Expected: PASS (5 tests)

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Identity/Application/RoleDtos.cs src/Modules/Identity/Application/RoleService.cs src/Modules/Identity/Presentation/RolesEndpoints.cs src/Api/Program.cs tests/Identity.Tests/RoleServiceListCreateTests.cs
git commit -m "feat(identity): add GET/POST /api/v1/roles"
```

---

## Task 5: `RoleService` — `PUT /api/v1/roles/{id}`, `DELETE /api/v1/roles/{id}`

**Files:**
- Modify: `src/Modules/Identity/Application/RoleService.cs`
- Modify: `src/Modules/Identity/Presentation/RolesEndpoints.cs`
- Test: `tests/Identity.Tests/RoleServiceUpdateDeleteTests.cs`

**Interfaces:**
- Consumes: `RoleValidationException`, `UpdateRoleRequest` (Task 4), `UserRole` (existing domain type).
- Produces: `RoleService.UpdateRoleAsync(Guid id, UpdateRoleRequest, Guid actingUserId, CancellationToken)` returning `Task<RoleResponse>` — throws `RoleValidationException` if the role doesn't exist, is `IsSystemDefault`, or a requested permission key is unknown. `RoleService.DeleteRoleAsync(Guid id, Guid actingUserId, CancellationToken)` returning `Task` — throws `RoleValidationException` if the role doesn't exist, is `IsSystemDefault`, or any `UserRole` still references it. `PUT /api/v1/roles/{id}`, `DELETE /api/v1/roles/{id}`, both `role.manage`-gated.

- [ ] **Step 1: Write the failing tests**

```csharp
using Courier.Audit.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Identity.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

public class RoleServiceUpdateDeleteTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new RoleConfiguration());
            modelBuilder.ApplyConfiguration(new PermissionConfiguration());
            modelBuilder.ApplyConfiguration(new UserRoleConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static RoleService CreateService(TestDbContext dbContext, Guid organizationId) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger());

    private static async Task<Role> SeedRoleAsync(TestDbContext dbContext, Guid organizationId, bool isSystemDefault, params string[] permissionKeys)
    {
        var permissions = await dbContext.Set<Permission>().Where(p => permissionKeys.Contains(p.Key)).ToListAsync();
        var role = new Role { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "TestRole", IsSystemDefault = isSystemDefault };
        foreach (var permission in permissions) role.Permissions.Add(permission);
        dbContext.Set<Role>().Add(role);
        await dbContext.SaveChangesAsync();
        return role;
    }

    [Fact]
    public async Task UpdateRoleAsync_ReplacesPermissionKeysWholesale()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var role = await SeedRoleAsync(dbContext, organizationId, false, "shipment.read");
        var service = CreateService(dbContext, organizationId);

        var result = await service.UpdateRoleAsync(role.Id, new UpdateRoleRequest(new[] { "cod.read", "cod.reconcile" }), Guid.NewGuid());

        Assert.Equal(new[] { "cod.read", "cod.reconcile" }, result.PermissionKeys.OrderBy(k => k));
        var persisted = await dbContext.Set<Role>().Include(r => r.Permissions).SingleAsync(r => r.Id == role.Id);
        Assert.DoesNotContain(persisted.Permissions, p => p.Key == "shipment.read");
    }

    [Fact]
    public async Task UpdateRoleAsync_SystemDefaultRole_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var role = await SeedRoleAsync(dbContext, organizationId, isSystemDefault: true, "shipment.read");
        var service = CreateService(dbContext, organizationId);

        await Assert.ThrowsAsync<RoleValidationException>(() =>
            service.UpdateRoleAsync(role.Id, new UpdateRoleRequest(new[] { "cod.read" }), Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteRoleAsync_CustomUnassignedRole_Removes()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var role = await SeedRoleAsync(dbContext, organizationId, false, "shipment.read");
        var service = CreateService(dbContext, organizationId);

        await service.DeleteRoleAsync(role.Id, Guid.NewGuid());

        Assert.False(await dbContext.Set<Role>().AnyAsync(r => r.Id == role.Id));
    }

    [Fact]
    public async Task DeleteRoleAsync_SystemDefaultRole_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var role = await SeedRoleAsync(dbContext, organizationId, isSystemDefault: true, "shipment.read");
        var service = CreateService(dbContext, organizationId);

        await Assert.ThrowsAsync<RoleValidationException>(() => service.DeleteRoleAsync(role.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteRoleAsync_StillAssignedToAUser_Throws()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var role = await SeedRoleAsync(dbContext, organizationId, false, "shipment.read");
        dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RoleId = role.Id, OrganizationId = organizationId,
            BranchScope = BranchScope.AllBranches, AssignedAt = DateTimeOffset.UtcNow, AssignedByUserId = Guid.NewGuid()
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId);

        await Assert.ThrowsAsync<RoleValidationException>(() => service.DeleteRoleAsync(role.Id, Guid.NewGuid()));
        Assert.True(await dbContext.Set<Role>().AnyAsync(r => r.Id == role.Id));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter RoleServiceUpdateDeleteTests`
Expected: FAIL — `RoleService` has no `UpdateRoleAsync`/`DeleteRoleAsync` members (compile error).

- [ ] **Step 3: Implement `UpdateRoleAsync` and `DeleteRoleAsync`**

Add to `src/Modules/Identity/Application/RoleService.cs`, after `CreateRoleAsync`:

```csharp
    public async Task<RoleResponse> UpdateRoleAsync(Guid id, UpdateRoleRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RoleValidationException("No active organization context.");

        var role = await _dbContext.Set<Role>().Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new RoleValidationException("Role not found.");
        if (role.IsSystemDefault)
        {
            throw new RoleValidationException("System-default roles cannot be edited.");
        }

        var permissions = await _dbContext.Set<Permission>()
            .Where(p => request.PermissionKeys.Contains(p.Key))
            .ToListAsync(cancellationToken);
        if (permissions.Count != request.PermissionKeys.Distinct().Count())
        {
            var unknown = request.PermissionKeys.Except(permissions.Select(p => p.Key));
            throw new RoleValidationException($"Unknown permission key(s): {string.Join(", ", unknown)}");
        }

        role.Permissions.Clear();
        foreach (var permission in permissions)
        {
            role.Permissions.Add(permission);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "role.updated", "Role", role.Id.ToString(),
            After: string.Join(",", permissions.Select(p => p.Key))), cancellationToken);

        return ToResponse(role);
    }

    public async Task DeleteRoleAsync(Guid id, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new RoleValidationException("No active organization context.");

        var role = await _dbContext.Set<Role>().FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new RoleValidationException("Role not found.");
        if (role.IsSystemDefault)
        {
            throw new RoleValidationException("System-default roles cannot be deleted.");
        }
        var stillAssigned = await _dbContext.Set<UserRole>().AnyAsync(ur => ur.RoleId == id, cancellationToken);
        if (stillAssigned)
        {
            throw new RoleValidationException("This role is still assigned to at least one user. Reassign or remove those users first.");
        }

        _dbContext.Set<Role>().Remove(role);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "role.deleted", "Role", id.ToString()), cancellationToken);
    }
```

- [ ] **Step 4: Wire the endpoints**

In `src/Modules/Identity/Presentation/RolesEndpoints.cs`, insert immediately after the `group.MapPost("/roles", ...)` block:

```csharp
        group.MapPut("/roles/{id:guid}", async (Guid id, UpdateRoleRequest request, RoleService service, HttpContext httpContext) =>
        {
            try
            {
                return Results.Ok(await service.UpdateRoleAsync(id, request, httpContext.User.GetUserId()!.Value, httpContext.RequestAborted));
            }
            catch (RoleValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "ROLE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("role.manage");

        group.MapDelete("/roles/{id:guid}", async (Guid id, RoleService service, HttpContext httpContext) =>
        {
            try
            {
                await service.DeleteRoleAsync(id, httpContext.User.GetUserId()!.Value, httpContext.RequestAborted);
                return Results.NoContent();
            }
            catch (RoleValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "ROLE_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("role.manage");

```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter RoleServiceUpdateDeleteTests`
Expected: PASS (5 tests)

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Identity/Application/RoleService.cs src/Modules/Identity/Presentation/RolesEndpoints.cs tests/Identity.Tests/RoleServiceUpdateDeleteTests.cs
git commit -m "feat(identity): add PUT/DELETE /api/v1/roles/{id} with system-default and still-assigned guards"
```

---

## Task 6: `UserManagementService` — `UserBranch` grant/list/revoke

**Files:**
- Create: `src/Modules/Identity/Application/UserManagementDtos.cs`
- Create: `src/Modules/Identity/Application/UserManagementService.cs`
- Modify: `src/Modules/Identity/Presentation/UsersEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Identity.Tests/UserManagementServiceBranchGrantsTests.cs`

**Interfaces:**
- Consumes: `IBranchLookupService.GetNamesByIdsAsync` (existing, `Courier.Branches.Application`), `UserBranch` (existing domain type, already carries `ExpiresAt`).
- Produces: `UserBranchGrantResponse(Guid BranchId, string BranchName, DateTimeOffset GrantedAt, DateTimeOffset? ExpiresAt)`, `GrantBranchRequest(Guid BranchId, DateTimeOffset? ExpiresAt)`, `UserManagementException`. `UserManagementService.ListUserBranchGrantsAsync(Guid userId, CancellationToken)` returning `Task<IReadOnlyList<UserBranchGrantResponse>>`. `UserManagementService.GrantBranchAsync(Guid userId, GrantBranchRequest, Guid actingUserId, CancellationToken)` returning `Task<UserBranchGrantResponse>` — throws `UserManagementException` on a duplicate active grant for the same branch. `UserManagementService.RevokeBranchAsync(Guid userId, Guid branchId, Guid actingUserId, CancellationToken)` returning `Task`. `GET/POST /api/v1/users/{id}/branches` (`user.read`/`user.update`), `DELETE /api/v1/users/{id}/branches/{branchId}` (`user.update`).

- [ ] **Step 1: Write the failing tests**

```csharp
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Identity.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class StubBranchLookupService : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(branchIds.ToDictionary(id => id, id => $"Branch {id:N}"[..16]));
}

public class UserManagementServiceBranchGrantsTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserBranch>(b =>
            {
                b.HasKey(ub => ub.Id);
            });
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static UserManagementService CreateService(TestDbContext dbContext, Guid organizationId) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new StubBranchLookupService());

    [Fact]
    public async Task GrantBranchAsync_ThenListUserBranchGrantsAsync_ReturnsTheGrant()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);

        var granted = await service.GrantBranchAsync(userId, new GrantBranchRequest(branchId, null), Guid.NewGuid());
        Assert.Equal(branchId, granted.BranchId);
        Assert.Null(granted.ExpiresAt);

        var grants = await service.ListUserBranchGrantsAsync(userId);
        var single = Assert.Single(grants);
        Assert.Equal(branchId, single.BranchId);
    }

    [Fact]
    public async Task GrantBranchAsync_WithExpiresAt_PersistsIt()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);

        var granted = await service.GrantBranchAsync(Guid.NewGuid(), new GrantBranchRequest(Guid.NewGuid(), expiresAt), Guid.NewGuid());

        Assert.Equal(expiresAt, granted.ExpiresAt);
    }

    [Fact]
    public async Task GrantBranchAsync_DuplicateActiveGrantForSameBranch_Throws()
    {
        await using var dbContext = CreateContext();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var service = CreateService(dbContext, Guid.NewGuid());
        await service.GrantBranchAsync(userId, new GrantBranchRequest(branchId, null), Guid.NewGuid());

        await Assert.ThrowsAsync<UserManagementException>(() =>
            service.GrantBranchAsync(userId, new GrantBranchRequest(branchId, null), Guid.NewGuid()));
    }

    [Fact]
    public async Task GrantBranchAsync_PriorGrantForSameBranchAlreadyExpired_AllowsNewGrant()
    {
        await using var dbContext = CreateContext();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(), UserId = userId, BranchId = branchId, OrganizationId = organizationId,
            GrantedAt = DateTimeOffset.UtcNow.AddDays(-10), ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1), GrantedByUserId = userId
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId);

        var granted = await service.GrantBranchAsync(userId, new GrantBranchRequest(branchId, null), Guid.NewGuid());

        Assert.Equal(branchId, granted.BranchId);
    }

    [Fact]
    public async Task RevokeBranchAsync_RemovesTheGrant()
    {
        await using var dbContext = CreateContext();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var service = CreateService(dbContext, Guid.NewGuid());
        await service.GrantBranchAsync(userId, new GrantBranchRequest(branchId, null), Guid.NewGuid());

        await service.RevokeBranchAsync(userId, branchId, Guid.NewGuid());

        var grants = await service.ListUserBranchGrantsAsync(userId);
        Assert.Empty(grants);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter UserManagementServiceBranchGrantsTests`
Expected: FAIL — `UserManagementService` doesn't exist yet (compile error).

- [ ] **Step 3: Write `UserManagementDtos.cs`**

`src/Modules/Identity/Application/UserManagementDtos.cs`:

```csharp
namespace Courier.Identity.Application;

public record UserBranchGrantResponse(Guid BranchId, string BranchName, DateTimeOffset GrantedAt, DateTimeOffset? ExpiresAt);
public record GrantBranchRequest(Guid BranchId, DateTimeOffset? ExpiresAt);

public class UserManagementException : Exception
{
    public UserManagementException(string message) : base(message) { }
}
```

- [ ] **Step 4: Write `UserManagementService.cs`**

`src/Modules/Identity/Application/UserManagementService.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Application;

public class UserManagementService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IBranchLookupService _branchLookupService;

    public UserManagementService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger, IBranchLookupService branchLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchLookupService = branchLookupService;
    }

    public async Task<IReadOnlyList<UserBranchGrantResponse>> ListUserBranchGrantsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var grants = await _dbContext.Set<UserBranch>().Where(ub => ub.UserId == userId).ToListAsync(cancellationToken);
        var branchIds = grants.Select(g => g.BranchId).ToHashSet();
        var names = await _branchLookupService.GetNamesByIdsAsync(branchIds, cancellationToken);
        return grants
            .Select(g => new UserBranchGrantResponse(g.BranchId, names.TryGetValue(g.BranchId, out var name) ? name : "Unknown Branch", g.GrantedAt, g.ExpiresAt))
            .ToList();
    }

    public async Task<UserBranchGrantResponse> GrantBranchAsync(Guid userId, GrantBranchRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new UserManagementException("No active organization context.");

        var now = DateTimeOffset.UtcNow;
        var hasActiveGrant = await _dbContext.Set<UserBranch>()
            .AnyAsync(ub => ub.UserId == userId && ub.BranchId == request.BranchId && (ub.ExpiresAt == null || ub.ExpiresAt > now), cancellationToken);
        if (hasActiveGrant)
        {
            throw new UserManagementException("This user already has an active grant for this branch.");
        }

        var grant = new UserBranch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BranchId = request.BranchId,
            OrganizationId = organizationId,
            GrantedAt = now,
            ExpiresAt = request.ExpiresAt,
            GrantedByUserId = actingUserId
        };
        _dbContext.Set<UserBranch>().Add(grant);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "user.branch_granted", "UserBranch", grant.Id.ToString(),
            After: $"UserId={userId},BranchId={request.BranchId},ExpiresAt={request.ExpiresAt}"), cancellationToken);

        var names = await _branchLookupService.GetNamesByIdsAsync(new HashSet<Guid> { request.BranchId }, cancellationToken);
        return new UserBranchGrantResponse(request.BranchId, names.TryGetValue(request.BranchId, out var name) ? name : "Unknown Branch", grant.GrantedAt, grant.ExpiresAt);
    }

    public async Task RevokeBranchAsync(Guid userId, Guid branchId, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new UserManagementException("No active organization context.");

        var grants = await _dbContext.Set<UserBranch>().Where(ub => ub.UserId == userId && ub.BranchId == branchId).ToListAsync(cancellationToken);
        if (grants.Count == 0)
        {
            throw new UserManagementException("No grant found for this user and branch.");
        }
        _dbContext.Set<UserBranch>().RemoveRange(grants);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "user.branch_revoked", "UserBranch", $"{userId}:{branchId}"), cancellationToken);
    }
}
```

- [ ] **Step 5: Wire the endpoints**

In `src/Modules/Identity/Presentation/UsersEndpoints.cs`, add `using Courier.Identity.Application;` and insert immediately before the closing brace of `MapUsersEndpoints`:

```csharp
        app.MapGet("/api/v1/users/{id:guid}/branches", async (Guid id, UserManagementService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListUserBranchGrantsAsync(id, cancellationToken)))
            .RequirePermission("user.read")
            .WithTags("Users Management");

        app.MapPost("/api/v1/users/{id:guid}/branches", async (Guid id, GrantBranchRequest request, UserManagementService service, HttpContext httpContext) =>
        {
            try
            {
                return Results.Ok(await service.GrantBranchAsync(id, request, httpContext.User.GetUserId()!.Value, httpContext.RequestAborted));
            }
            catch (UserManagementException ex)
            {
                return Results.BadRequest(new { error = new { code = "USER_MANAGEMENT_FAILED", message = ex.Message } });
            }
        }).RequirePermission("user.update")
        .WithTags("Users Management");

        app.MapDelete("/api/v1/users/{id:guid}/branches/{branchId:guid}", async (Guid id, Guid branchId, UserManagementService service, HttpContext httpContext) =>
        {
            try
            {
                await service.RevokeBranchAsync(id, branchId, httpContext.User.GetUserId()!.Value, httpContext.RequestAborted);
                return Results.NoContent();
            }
            catch (UserManagementException ex)
            {
                return Results.BadRequest(new { error = new { code = "USER_MANAGEMENT_FAILED", message = ex.Message } });
            }
        }).RequirePermission("user.update")
        .WithTags("Users Management");
```

- [ ] **Step 6: Register `UserManagementService` in DI**

In `src/Api/Program.cs`, insert `builder.Services.AddScoped<UserManagementService>();` immediately after the `builder.Services.AddScoped<RoleService>();` line added in Task 4.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter UserManagementServiceBranchGrantsTests`
Expected: PASS (5 tests)

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Identity/Application/UserManagementDtos.cs src/Modules/Identity/Application/UserManagementService.cs src/Modules/Identity/Presentation/UsersEndpoints.cs src/Api/Program.cs tests/Identity.Tests/UserManagementServiceBranchGrantsTests.cs
git commit -m "feat(identity): add UserBranch grant/list/revoke endpoints"
```

---

## Task 7: Paginate and enrich `GET /api/v1/users`

**Files:**
- Modify: `src/Modules/Identity/Application/UserManagementDtos.cs`
- Modify: `src/Modules/Identity/Application/UserManagementService.cs`
- Modify: `src/Modules/Identity/Presentation/UsersEndpoints.cs`
- Test: `tests/Identity.Tests/UserManagementServiceListUsersTests.cs`

**Interfaces:**
- Consumes: `PagedResult<T>`/`PaginationDefaults.Normalize` (existing `Courier.SharedKernel`), `UserRole`, `Role` (existing domain types).
- Produces: `UserListItem(Guid Id, string Email, string FullName, bool IsActive, string? RoleName, int BranchCount)` (replaces the old flat record in `UsersEndpoints.cs`). `UserManagementService.ListUsersAsync(int? page, int? pageSize, CancellationToken)` returning `Task<PagedResult<UserListItem>>`. `GET /api/v1/users?page=&pageSize=` now returns this paged, enriched shape — `user.read`-gated, unchanged.

- [ ] **Step 1: Write the failing tests**

```csharp
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Identity.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class StubBranchLookupService : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

public class UserManagementServiceListUsersTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => ur.Id);
            modelBuilder.Entity<UserBranch>().HasKey(ub => ub.Id);
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static UserManagementService CreateService(TestDbContext dbContext, Guid organizationId) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new StubBranchLookupService());

    private static async Task<User> SeedUserAsync(TestDbContext dbContext, Guid organizationId, string? roleName = null, int branchGrantCount = 0)
    {
        var user = new User { Id = Guid.NewGuid(), OrganizationId = organizationId, Email = $"{Guid.NewGuid():N}@example.com", PasswordHash = "x", FullName = "Test User" };
        dbContext.Set<User>().Add(user);

        if (roleName is not null)
        {
            var role = new Role { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = roleName, IsSystemDefault = true };
            dbContext.Set<Role>().Add(role);
            dbContext.Set<UserRole>().Add(new UserRole
            {
                Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id, OrganizationId = organizationId,
                BranchScope = BranchScope.AllBranches, AssignedAt = DateTimeOffset.UtcNow, AssignedByUserId = user.Id
            });
        }
        for (var i = 0; i < branchGrantCount; i++)
        {
            dbContext.Set<UserBranch>().Add(new UserBranch
            {
                Id = Guid.NewGuid(), UserId = user.Id, BranchId = Guid.NewGuid(), OrganizationId = organizationId,
                GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
            });
        }
        await dbContext.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task ListUsersAsync_IncludesRoleNameAndBranchCount()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var user = await SeedUserAsync(dbContext, organizationId, "BranchManager", branchGrantCount: 2);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListUsersAsync(null, null);

        var item = Assert.Single(result.Items);
        Assert.Equal(user.Id, item.Id);
        Assert.Equal("BranchManager", item.RoleName);
        Assert.Equal(2, item.BranchCount);
    }

    [Fact]
    public async Task ListUsersAsync_NoRole_RoleNameIsNull()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        await SeedUserAsync(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListUsersAsync(null, null);

        Assert.Null(Assert.Single(result.Items).RoleName);
    }

    [Fact]
    public async Task ListUsersAsync_Paginates()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        for (var i = 0; i < 5; i++) await SeedUserAsync(dbContext, organizationId);
        var service = CreateService(dbContext, organizationId);

        var result = await service.ListUsersAsync(1, 2);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter UserManagementServiceListUsersTests`
Expected: FAIL — `UserManagementService` has no `ListUsersAsync` member (compile error).

- [ ] **Step 3: Add `UserListItem` to the DTOs file**

Add to `src/Modules/Identity/Application/UserManagementDtos.cs`:

```csharp
public record UserListItem(Guid Id, string Email, string FullName, bool IsActive, string? RoleName, int BranchCount);
```

- [ ] **Step 4: Implement `ListUsersAsync`**

Add to `src/Modules/Identity/Application/UserManagementService.cs`, after the constructor:

```csharp
    // User/Role/UserRole/UserBranch already carry the ambient tenant HasQueryFilter
    // (AppDbContext.cs), so no explicit OrganizationId clause is needed here — matches
    // RoleService.ListRolesAsync's same reasoning.
    public async Task<PagedResult<UserListItem>> ListUsersAsync(int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await _dbContext.Set<User>().CountAsync(cancellationToken);
        var users = await _dbContext.Set<User>()
            .OrderBy(u => u.Email)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var userIds = users.Select(u => u.Id).ToArray();
        var roleNamesByUser = await _dbContext.Set<UserRole>()
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(_dbContext.Set<Role>(), ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(cancellationToken);
        var branchCountsByUser = await _dbContext.Set<UserBranch>()
            .Where(ub => userIds.Contains(ub.UserId))
            .GroupBy(ub => ub.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var items = users.Select(u => new UserListItem(
            u.Id, u.Email, u.FullName, u.IsActive,
            string.Join(", ", roleNamesByUser.Where(r => r.UserId == u.Id).Select(r => r.Name)) is { Length: > 0 } names ? names : null,
            branchCountsByUser.FirstOrDefault(b => b.UserId == u.Id)?.Count ?? 0)).ToList();

        return new PagedResult<UserListItem>(items, normalizedPage, normalizedPageSize, totalCount);
    }
```

- [ ] **Step 5: Replace the old `/api/v1/users` handler**

In `src/Modules/Identity/Presentation/UsersEndpoints.cs`, delete the existing `public record UserListItem(...)` line (superseded by the `Application` layer's version added in Step 3) and replace the whole `MapGet("/api/v1/users", ...)` block with:

```csharp
        app.MapGet("/api/v1/users", async (int? page, int? pageSize, UserManagementService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListUsersAsync(page, pageSize, cancellationToken)))
        .RequirePermission("user.read")
        .WithTags("Users Management");
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter UserManagementServiceListUsersTests`
Expected: PASS (3 tests)

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Identity/Application/UserManagementDtos.cs src/Modules/Identity/Application/UserManagementService.cs src/Modules/Identity/Presentation/UsersEndpoints.cs tests/Identity.Tests/UserManagementServiceListUsersTests.cs
git commit -m "feat(identity): paginate GET /api/v1/users and enrich with RoleName/BranchCount"
```

---

## Task 8: `PATCH /api/v1/users/{id}/deactivate` / `/reactivate`

**Files:**
- Modify: `src/Modules/Identity/Application/UserManagementService.cs`
- Modify: `src/Modules/Identity/Presentation/UsersEndpoints.cs`
- Test: `tests/Identity.Tests/UserManagementServiceDeactivateTests.cs`

**Interfaces:**
- Consumes: `User.IsActive` (existing field — `AuthService.LoginAsync`/`RefreshTokenAsync` already reject an inactive user, unchanged by this task).
- Produces: `UserManagementService.DeactivateUserAsync(Guid userId, Guid actingUserId, CancellationToken)` returning `Task`. `UserManagementService.ReactivateUserAsync(Guid userId, Guid actingUserId, CancellationToken)` returning `Task`. `PATCH /api/v1/users/{id}/deactivate`, `PATCH /api/v1/users/{id}/reactivate`, both `user.update`-gated.

- [ ] **Step 1: Write the failing tests**

```csharp
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Identity.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class StubBranchLookupService : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

public class UserManagementServiceDeactivateTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasKey(u => u.Id);
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static UserManagementService CreateService(TestDbContext dbContext, Guid organizationId) =>
        new(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new StubBranchLookupService());

    [Fact]
    public async Task DeactivateUserAsync_SetsIsActiveFalse()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var user = new User { Id = Guid.NewGuid(), OrganizationId = organizationId, Email = "x@example.com", PasswordHash = "x", FullName = "X", IsActive = true };
        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId);

        await service.DeactivateUserAsync(user.Id, Guid.NewGuid());

        Assert.False((await dbContext.Set<User>().SingleAsync(u => u.Id == user.Id)).IsActive);
    }

    [Fact]
    public async Task ReactivateUserAsync_SetsIsActiveTrue()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var user = new User { Id = Guid.NewGuid(), OrganizationId = organizationId, Email = "x@example.com", PasswordHash = "x", FullName = "X", IsActive = false };
        dbContext.Set<User>().Add(user);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, organizationId);

        await service.ReactivateUserAsync(user.Id, Guid.NewGuid());

        Assert.True((await dbContext.Set<User>().SingleAsync(u => u.Id == user.Id)).IsActive);
    }

    [Fact]
    public async Task DeactivateUserAsync_UnknownUser_Throws()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext, Guid.NewGuid());

        await Assert.ThrowsAsync<UserManagementException>(() => service.DeactivateUserAsync(Guid.NewGuid(), Guid.NewGuid()));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter UserManagementServiceDeactivateTests`
Expected: FAIL — `UserManagementService` has no `DeactivateUserAsync`/`ReactivateUserAsync` members (compile error).

- [ ] **Step 3: Implement `DeactivateUserAsync` and `ReactivateUserAsync`**

Add to `src/Modules/Identity/Application/UserManagementService.cs`, after `RevokeBranchAsync`:

```csharp
    public Task DeactivateUserAsync(Guid userId, Guid actingUserId, CancellationToken cancellationToken = default) =>
        SetActiveAsync(userId, isActive: false, "user.deactivated", actingUserId, cancellationToken);

    public Task ReactivateUserAsync(Guid userId, Guid actingUserId, CancellationToken cancellationToken = default) =>
        SetActiveAsync(userId, isActive: true, "user.reactivated", actingUserId, cancellationToken);

    private async Task SetActiveAsync(Guid userId, bool isActive, string auditAction, Guid actingUserId, CancellationToken cancellationToken)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new UserManagementException("No active organization context.");

        var user = await _dbContext.Set<User>().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new UserManagementException("User not found.");
        var before = user.IsActive;
        user.IsActive = isActive;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, auditAction, "User", userId.ToString(),
            Before: before.ToString(), After: isActive.ToString()), cancellationToken);
    }
```

- [ ] **Step 4: Wire the endpoints**

In `src/Modules/Identity/Presentation/UsersEndpoints.cs`, insert immediately before the closing brace of `MapUsersEndpoints`:

```csharp
        app.MapPatch("/api/v1/users/{id:guid}/deactivate", async (Guid id, UserManagementService service, HttpContext httpContext) =>
        {
            try
            {
                await service.DeactivateUserAsync(id, httpContext.User.GetUserId()!.Value, httpContext.RequestAborted);
                return Results.NoContent();
            }
            catch (UserManagementException ex)
            {
                return Results.BadRequest(new { error = new { code = "USER_MANAGEMENT_FAILED", message = ex.Message } });
            }
        }).RequirePermission("user.update")
        .WithTags("Users Management");

        app.MapPatch("/api/v1/users/{id:guid}/reactivate", async (Guid id, UserManagementService service, HttpContext httpContext) =>
        {
            try
            {
                await service.ReactivateUserAsync(id, httpContext.User.GetUserId()!.Value, httpContext.RequestAborted);
                return Results.NoContent();
            }
            catch (UserManagementException ex)
            {
                return Results.BadRequest(new { error = new { code = "USER_MANAGEMENT_FAILED", message = ex.Message } });
            }
        }).RequirePermission("user.update")
        .WithTags("Users Management");
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter UserManagementServiceDeactivateTests`
Expected: PASS (3 tests)

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Identity/Application/UserManagementService.cs src/Modules/Identity/Presentation/UsersEndpoints.cs tests/Identity.Tests/UserManagementServiceDeactivateTests.cs
git commit -m "feat(identity): add user deactivate/reactivate endpoints"
```

---

## Task 9: `UserInvite` domain entity, EF configuration, and migration

**Files:**
- Create: `src/Modules/Identity/Domain/UserInvite.cs`
- Create: `src/Modules/Identity/Infrastructure/UserInviteConfiguration.cs`
- Modify: `src/Persistence/AppDbContext.cs`
- Create: (generated) `src/Persistence/Migrations/<timestamp>_AddUserInvites.cs` and `.Designer.cs`
- Test: `tests/Identity.Tests/UserInviteConfigurationTests.cs`

**Interfaces:**
- Produces: `UserInvite { Guid Id, Guid UserId, string TokenHash, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt }` — mirrors `RefreshToken`'s plaintext-token/hashed-storage shape exactly. `AppDbContext.UserInvites` (`DbSet<UserInvite>`). No tenant `HasQueryFilter` on `UserInvite` — it carries no own `OrganizationId`; tenant isolation flows transitively through `UserId -> User.OrganizationId`, the same "no cached parent copy" precedent as `WebhookDelivery`/`ZoneRate`/`DeliveryRun` (documented inline, matching those entities' own `AppDbContext.cs` comments).

- [ ] **Step 1: Write the failing test**

```csharp
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Identity.Tests;

public class UserInviteConfigurationTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new UserInviteConfiguration());
        }
    }

    [Fact]
    public async Task UserInvite_RoundTripsThroughEfCore()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();

        var invite = new UserInvite
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), TokenHash = "hashed-token-value",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7), AcceptedAt = null
        };
        dbContext.Set<UserInvite>().Add(invite);
        await dbContext.SaveChangesAsync();

        var persisted = await dbContext.Set<UserInvite>().SingleAsync(i => i.Id == invite.Id);
        Assert.Equal("hashed-token-value", persisted.TokenHash);
        Assert.Null(persisted.AcceptedAt);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter UserInviteConfigurationTests`
Expected: FAIL — `UserInvite`/`UserInviteConfiguration` don't exist yet (compile error).

- [ ] **Step 3: Write the domain entity**

`src/Modules/Identity/Domain/UserInvite.cs`:

```csharp
namespace Courier.Identity.Domain;

// Mirrors RefreshToken's shape: a random plaintext token is emailed to the invitee and never
// stored; only its SHA-256 hash lives here. UserId points at a User row already created (with an
// unusable placeholder PasswordHash) at invite time — see UserManagementService.InviteUserAsync.
public class UserInvite
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}
```

- [ ] **Step 4: Write the EF configuration**

`src/Modules/Identity/Infrastructure/UserInviteConfiguration.cs`:

```csharp
using Courier.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Identity.Infrastructure;

public class UserInviteConfiguration : IEntityTypeConfiguration<UserInvite>
{
    public void Configure(EntityTypeBuilder<UserInvite> builder)
    {
        builder.ToTable("UserInvites");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.TokenHash).IsRequired();
        builder.HasIndex(i => i.TokenHash).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 5: Register the `DbSet` and configuration in `AppDbContext`**

In `src/Persistence/AppDbContext.cs`, add `public DbSet<UserInvite> UserInvites => Set<UserInvite>();` immediately after the existing `public DbSet<UserBranch> UserBranches => Set<UserBranch>();` line, and add `modelBuilder.ApplyConfiguration(new UserInviteConfiguration());` immediately after the existing `modelBuilder.ApplyConfiguration(new UserBranchConfiguration());` line. Add a comment directly above the `ApplyConfiguration` line matching the established "no tenant filter, isolation flows transitively" precedent already used for `WebhookDelivery`/`ZoneRate`:

```csharp
        // No tenant filter on UserInvite: no own OrganizationId — tenant isolation flows
        // transitively through UserId -> User.OrganizationId (which does have a filter, above).
        modelBuilder.ApplyConfiguration(new UserInviteConfiguration());
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter UserInviteConfigurationTests`
Expected: PASS

- [ ] **Step 7: Generate and apply the migration**

```bash
dotnet ef migrations add AddUserInvites --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj
dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj --connection "Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password"
```

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Identity/Domain/UserInvite.cs src/Modules/Identity/Infrastructure/UserInviteConfiguration.cs src/Persistence/AppDbContext.cs src/Persistence/Migrations tests/Identity.Tests/UserInviteConfigurationTests.cs
git commit -m "feat(identity): add UserInvite entity and migration"
```

---

## Task 10: `NotificationDispatchService` — resolve a recipient directly from a `RecipientEmail` payload property

**Files:**
- Modify: `src/Modules/Notifications/Application/NotificationDispatchService.cs`
- Modify: `src/Modules/Notifications/Application/NotificationDefaultTemplates.cs`
- Test: `tests/Notifications.Tests/NotificationDispatchServiceTests.cs`

**Interfaces:**
- Produces: `DispatchEventAsync` now checks the payload for a top-level `RecipientEmail` string property *before* the existing `MerchantId` branch; if present, it's used directly (no `IMerchantContactLookupService` call) and a missing/empty `MerchantId` is no longer an error for such payloads. `NotificationDefaultTemplates.Email` gains `["user.invited"]`. Existing `MerchantId`-keyed events (`shipment.created` etc.) are completely unaffected — this is purely additive.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Notifications.Tests/NotificationDispatchServiceTests.cs`, after `DispatchEventAsync_DifferentOrganizationsTemplate_NotUsedAcrossTenants`:

```csharp
    [Fact]
    public async Task DispatchEventAsync_PayloadHasRecipientEmail_SendsToItDirectly_NoMerchantLookup()
    {
        await using var dbContext = CreateContext();
        var merchantLookup = new FakeMerchantContactLookupService { ContactEmail = null }; // proves this isn't consulted
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext, merchantLookup, new INotificationChannel[] { emailChannel });
        var payload = System.Text.Json.JsonSerializer.Serialize(new { RecipientEmail = "invitee@example.com", FullName = "New Person", OrganizationName = "Acme Courier", InviteUrl = "https://app.example.com/accept-invite?token=abc" });

        await service.DispatchEventAsync(Guid.NewGuid(), "user.invited", payload);

        Assert.Single(emailChannel.Sent, s => s.RecipientAddress == "invitee@example.com" && s.Body.Contains("Acme Courier") && s.Body.Contains("https://app.example.com/accept-invite?token=abc"));
    }

    [Fact]
    public async Task DispatchEventAsync_RecipientEmailPresent_IgnoresAbsentMerchantId()
    {
        await using var dbContext = CreateContext();
        var merchantLookup = new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" };
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext, merchantLookup, new INotificationChannel[] { emailChannel });
        // No MerchantId at all in this payload — must not throw NotificationValidationException.
        var payload = System.Text.Json.JsonSerializer.Serialize(new { RecipientEmail = "invitee@example.com", FullName = "New Person", OrganizationName = "Acme Courier", InviteUrl = "https://app.example.com/accept-invite?token=abc" });

        await service.DispatchEventAsync(Guid.NewGuid(), "user.invited", payload);

        Assert.Single(emailChannel.Sent, s => s.RecipientAddress == "invitee@example.com");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Notifications.Tests/Notifications.Tests.csproj --filter DispatchEventAsync_PayloadHasRecipientEmail_SendsToItDirectly_NoMerchantLookup`
Expected: FAIL — no `user.invited` default template exists (returns early, nothing sent) and no `MerchantId` in the payload throws `NotificationValidationException` before that.

- [ ] **Step 3: Add the `user.invited` default template**

In `src/Modules/Notifications/Application/NotificationDefaultTemplates.cs`, add to the `Email` dictionary:

```csharp
        ["user.invited"] = "You've been invited to join {OrganizationName} on Courier. Set your password: {InviteUrl}"
```

- [ ] **Step 4: Add the `RecipientEmail` branch to `DispatchEventAsync`**

In `src/Modules/Notifications/Application/NotificationDispatchService.cs`, replace the block that reads `MerchantId` and throws on its absence — from `var payload = JsonDocument.Parse(payloadJson).RootElement;` through the `recipientEmail` lookup and its `NoRecipient` early-return — with:

```csharp
        var payload = JsonDocument.Parse(payloadJson).RootElement;

        // Two ways an event can name its recipient: a self-contained RecipientEmail (user.invited
        // — the invitee isn't a merchant, there's nothing to look up) or, for every other existing
        // event, a MerchantId this method resolves via IMerchantContactLookupService as before.
        // Checked first and independently so a RecipientEmail payload never needs a MerchantId at
        // all, and an existing MerchantId-keyed payload is completely unaffected by this branch.
        string? recipientEmail;
        if (payload.TryGetProperty("RecipientEmail", out var recipientEmailProperty) && recipientEmailProperty.ValueKind == JsonValueKind.String)
        {
            recipientEmail = recipientEmailProperty.GetString();
        }
        else
        {
            if (!payload.TryGetProperty("MerchantId", out var merchantIdProperty) || !merchantIdProperty.TryGetGuid(out var merchantId))
            {
                throw new NotificationValidationException($"Event '{eventType}' payload has no RecipientEmail or MerchantId — cannot resolve a recipient.");
            }
            recipientEmail = await _merchantContactLookupService.GetContactEmailAsync(organizationId, merchantId, cancellationToken);
        }
```

Then, in the template-resolution block immediately below (the `var template = await _dbContext.Set<NotificationTemplate>()...` / `var body = Render(...)` lines), remove the now-duplicate `var recipientEmail = await _merchantContactLookupService.GetContactEmailAsync(...)` line that previously followed it (its logic moved into the branch above), leaving the `if (recipientEmail is null) { ... return; }` / `var emailChannel = ...` / `try { ... }` block that follows untouched.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Notifications.Tests/Notifications.Tests.csproj --filter NotificationDispatchServiceTests`
Expected: PASS — every existing test plus the 2 new ones (existing `MerchantId`-only tests prove the branch didn't regress that path).

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Notifications/Application/NotificationDispatchService.cs src/Modules/Notifications/Application/NotificationDefaultTemplates.cs tests/Notifications.Tests/NotificationDispatchServiceTests.cs
git commit -m "feat(notifications): resolve a recipient directly from a RecipientEmail payload property"
```

---

## Task 11: `POST /api/v1/users/invite`

**Files:**
- Modify: `src/Modules/Identity/Application/UserManagementDtos.cs`
- Modify: `src/Modules/Identity/Application/UserManagementService.cs`
- Modify: `src/Modules/Identity/Presentation/UsersEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Modify: `src/Api/appsettings.Development.json`
- Test: `tests/Identity.Tests/UserManagementServiceInviteTests.cs`

**Interfaces:**
- Consumes: `IPasswordHasher.Hash` (existing), `IOutboxWriter.WriteAsync` (existing, `Courier.Events.Application`), `UserInvite` (Task 9), `Organization` (existing domain type).
- Produces: `InviteUserRequest(string Email, string FullName, string? Phone, Guid RoleId, Guid BranchId, DateTimeOffset? ExpiresAt)`, `InviteUserResponse(Guid UserId, string Email)`. `UserManagementService.InviteUserAsync(InviteUserRequest, Guid actingUserId, CancellationToken)` returning `Task<InviteUserResponse>` — throws `UserManagementException` on a duplicate email or an unknown/foreign `RoleId`. `POST /api/v1/users/invite`, `user.create`-gated. Writes a `user.invited` outbox event (Task 10's `RecipientEmail` branch consumes it).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Events.Application;
using Courier.Events.Domain;
using Courier.Events.Infrastructure;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Courier.Identity.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class StubBranchLookupService : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(new BranchZoneInfo(branchId, null));
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

public class UserManagementServiceInviteTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => ur.Id);
            modelBuilder.Entity<UserBranch>().HasKey(ub => ub.Id);
            modelBuilder.ApplyConfiguration(new UserInviteConfiguration());
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

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Frontend:BaseUrl"] = "https://app.test" }).Build();

    private static UserManagementService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new StubBranchLookupService(),
        new PasswordHasherAdapter(), new OutboxWriterService(dbContext), CreateConfiguration());

    private static async Task<(Guid OrganizationId, Guid RoleId)> SeedOrgAndRoleAsync(TestDbContext dbContext)
    {
        var organizationId = Guid.NewGuid();
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = "Acme Courier", Slug = $"acme-{organizationId:N}" });
        var role = new Role { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = "BranchManager", IsSystemDefault = true };
        dbContext.Set<Role>().Add(role);
        await dbContext.SaveChangesAsync();
        return (organizationId, role.Id);
    }

    [Fact]
    public async Task InviteUserAsync_CreatesUserRoleBranchGrantAndInviteRow()
    {
        await using var dbContext = CreateContext();
        var (organizationId, roleId) = await SeedOrgAndRoleAsync(dbContext);
        var branchId = Guid.NewGuid();
        var service = CreateService(dbContext, organizationId);

        var result = await service.InviteUserAsync(
            new InviteUserRequest("invitee@example.com", "New Person", null, roleId, branchId, null), Guid.NewGuid());

        var user = await dbContext.Set<User>().SingleAsync(u => u.Id == result.UserId);
        Assert.True(user.IsActive);
        Assert.False(new PasswordHasherAdapter().Verify("anything", user.PasswordHash)); // unusable placeholder hash
        Assert.True(await dbContext.Set<UserRole>().AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == roleId));
        Assert.True(await dbContext.Set<UserBranch>().AnyAsync(ub => ub.UserId == user.Id && ub.BranchId == branchId));
        Assert.True(await dbContext.Set<UserInvite>().AnyAsync(i => i.UserId == user.Id && i.AcceptedAt == null));
    }

    [Fact]
    public async Task InviteUserAsync_WritesOutboxEventWithRecipientEmailAndInviteUrl()
    {
        await using var dbContext = CreateContext();
        var (organizationId, roleId) = await SeedOrgAndRoleAsync(dbContext);
        var service = CreateService(dbContext, organizationId);

        await service.InviteUserAsync(new InviteUserRequest("invitee@example.com", "New Person", null, roleId, Guid.NewGuid(), null), Guid.NewGuid());

        var message = await dbContext.Set<OutboxMessage>().SingleAsync(m => m.EventType == "user.invited");
        var payload = JsonDocument.Parse(message.Payload).RootElement;
        Assert.Equal("invitee@example.com", payload.GetProperty("RecipientEmail").GetString());
        Assert.Equal("Acme Courier", payload.GetProperty("OrganizationName").GetString());
        Assert.StartsWith("https://app.test/accept-invite?token=", payload.GetProperty("InviteUrl").GetString());
    }

    [Fact]
    public async Task InviteUserAsync_DuplicateEmail_Throws()
    {
        await using var dbContext = CreateContext();
        var (organizationId, roleId) = await SeedOrgAndRoleAsync(dbContext);
        var service = CreateService(dbContext, organizationId);
        await service.InviteUserAsync(new InviteUserRequest("dup@example.com", "First", null, roleId, Guid.NewGuid(), null), Guid.NewGuid());

        await Assert.ThrowsAsync<UserManagementException>(() =>
            service.InviteUserAsync(new InviteUserRequest("dup@example.com", "Second", null, roleId, Guid.NewGuid(), null), Guid.NewGuid()));
    }

    [Fact]
    public async Task InviteUserAsync_UnknownRoleId_Throws()
    {
        await using var dbContext = CreateContext();
        var (organizationId, _) = await SeedOrgAndRoleAsync(dbContext);
        var service = CreateService(dbContext, organizationId);

        await Assert.ThrowsAsync<UserManagementException>(() =>
            service.InviteUserAsync(new InviteUserRequest("x@example.com", "X", null, Guid.NewGuid(), Guid.NewGuid(), null), Guid.NewGuid()));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter UserManagementServiceInviteTests`
Expected: FAIL — `UserManagementService` has no `InviteUserAsync` member and no matching constructor (compile error).

- [ ] **Step 3: Add the invite DTOs**

Add to `src/Modules/Identity/Application/UserManagementDtos.cs`:

```csharp
public record InviteUserRequest(string Email, string FullName, string? Phone, Guid RoleId, Guid BranchId, DateTimeOffset? ExpiresAt);
public record InviteUserResponse(Guid UserId, string Email);
```

- [ ] **Step 4: Extend `UserManagementService`'s constructor and add `InviteUserAsync`**

In `src/Modules/Identity/Application/UserManagementService.cs`, add `using System.Security.Cryptography;`, `using Courier.Events.Application;`, `using Microsoft.Extensions.Configuration;`, three new fields, and extend the constructor:

```csharp
    private readonly IPasswordHasher _passwordHasher;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IConfiguration _configuration;

    public UserManagementService(
        DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger, IBranchLookupService branchLookupService,
        IPasswordHasher passwordHasher, IOutboxWriter outboxWriter, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _branchLookupService = branchLookupService;
        _passwordHasher = passwordHasher;
        _outboxWriter = outboxWriter;
        _configuration = configuration;
    }
```

Then add, after `SetActiveAsync`:

```csharp
    public async Task<InviteUserResponse> InviteUserAsync(InviteUserRequest request, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new UserManagementException("No active organization context.");

        var email = request.Email.ToLowerInvariant();
        // Same platform-wide uniqueness check as AuthService.RegisterOrganizationAsync/
        // UserProvisioningService.ProvisionStaffUserAsync — User.Email has no per-org uniqueness.
        var emailTaken = await _dbContext.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Email == email, cancellationToken);
        if (emailTaken)
        {
            throw new UserManagementException("Email is already registered.");
        }

        var role = await _dbContext.Set<Role>().FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken)
            ?? throw new UserManagementException("Role not found.");

        // Unusable on purpose (same DummyPasswordHash technique AuthService already establishes
        // for its own timing-safety hash) — this user cannot log in until AcceptInviteAsync
        // (Task 12) sets a real one.
        var user = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = email,
            PasswordHash = _passwordHasher.Hash(Guid.NewGuid().ToString()),
            FullName = request.FullName,
            Phone = request.Phone,
            IsActive = true
        };
        _dbContext.Set<User>().Add(user);

        var now = DateTimeOffset.UtcNow;
        _dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id, OrganizationId = organizationId,
            BranchScope = BranchScope.SpecificBranches, AssignedAt = now, AssignedByUserId = actingUserId
        });
        _dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(), UserId = user.Id, BranchId = request.BranchId, OrganizationId = organizationId,
            GrantedAt = now, ExpiresAt = request.ExpiresAt, GrantedByUserId = actingUserId
        });

        var plainTextToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _dbContext.Set<UserInvite>().Add(new UserInvite
        {
            Id = Guid.NewGuid(), UserId = user.Id, TokenHash = HashToken(plainTextToken),
            ExpiresAt = now.AddDays(7), AcceptedAt = null
        });

        var organizationName = await _dbContext.Set<Organization>().Where(o => o.Id == organizationId).Select(o => o.Name).FirstAsync(cancellationToken);
        var baseUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:3000";
        var inviteUrl = $"{baseUrl}/accept-invite?token={Uri.EscapeDataString(plainTextToken)}";
        await _outboxWriter.WriteAsync(organizationId, "user.invited", new
        {
            RecipientEmail = request.Email,
            FullName = request.FullName,
            OrganizationName = organizationName,
            InviteUrl = inviteUrl
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "user.invited", "User", user.Id.ToString(),
            After: $"Email={request.Email},RoleId={request.RoleId}"), cancellationToken);

        return new InviteUserResponse(user.Id, request.Email);
    }

    private static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
```

- [ ] **Step 5: Wire the endpoint**

In `src/Modules/Identity/Presentation/UsersEndpoints.cs`, insert immediately before the closing brace of `MapUsersEndpoints`:

```csharp
        app.MapPost("/api/v1/users/invite", async (InviteUserRequest request, UserManagementService service, HttpContext httpContext) =>
        {
            try
            {
                return Results.Ok(await service.InviteUserAsync(request, httpContext.User.GetUserId()!.Value, httpContext.RequestAborted));
            }
            catch (UserManagementException ex)
            {
                return Results.BadRequest(new { error = new { code = "USER_MANAGEMENT_FAILED", message = ex.Message } });
            }
        }).RequirePermission("user.create")
        .WithTags("Users Management");
```

- [ ] **Step 6: Add the `Frontend:BaseUrl` setting**

In `src/Api/appsettings.Development.json`, add a new top-level section:

```json
  "Frontend": {
    "BaseUrl": "http://localhost:3000"
  },
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter UserManagementServiceInviteTests`
Expected: PASS (4 tests)

- [ ] **Step 8: Fix the constructor-signature ripple in Tasks 6/7/8's test files**

`UserManagementService`'s constructor grew from 4 to 7 parameters this task. Update each of the following three files' `CreateService` helper the same way — add `using Microsoft.Extensions.Configuration;` and `using Courier.Events.Application;` (for `OutboxWriterService`) to the file's imports, and change the `new(...)` call to pass the 3 new arguments:

`tests/Identity.Tests/UserManagementServiceBranchGrantsTests.cs`:

```csharp
    private static UserManagementService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new StubBranchLookupService(),
        new PasswordHasherAdapter(), new OutboxWriterService(dbContext), new ConfigurationBuilder().Build());
```

`tests/Identity.Tests/UserManagementServiceListUsersTests.cs`:

```csharp
    private static UserManagementService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new StubBranchLookupService(),
        new PasswordHasherAdapter(), new OutboxWriterService(dbContext), new ConfigurationBuilder().Build());
```

`tests/Identity.Tests/UserManagementServiceDeactivateTests.cs`:

```csharp
    private static UserManagementService CreateService(TestDbContext dbContext, Guid organizationId) => new(
        dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new StubBranchLookupService(),
        new PasswordHasherAdapter(), new OutboxWriterService(dbContext), new ConfigurationBuilder().Build());
```

None of these three files' tests exercise `InviteUserAsync`, so the `OutboxWriterService`/`ConfigurationBuilder` instances are never actually invoked (`OutboxMessage` doesn't need to be part of any of these files' `TestDbContext` models) — they exist purely to satisfy the widened constructor signature.

- [ ] **Step 9: Run the full `Identity.Tests` suite to confirm the ripple fix**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj`
Expected: PASS — every test project-wide, including Tasks 6/7/8's own tests unchanged in behavior.

- [ ] **Step 10: Commit**

```bash
git add src/Modules/Identity/Application/UserManagementDtos.cs src/Modules/Identity/Application/UserManagementService.cs src/Modules/Identity/Presentation/UsersEndpoints.cs src/Api/Program.cs src/Api/appsettings.Development.json tests/Identity.Tests/UserManagementServiceInviteTests.cs tests/Identity.Tests/UserManagementServiceBranchGrantsTests.cs tests/Identity.Tests/UserManagementServiceListUsersTests.cs tests/Identity.Tests/UserManagementServiceDeactivateTests.cs
git commit -m "feat(identity): add POST /api/v1/users/invite"
```

---

## Task 12: `POST /api/v1/auth/accept-invite`

**Files:**
- Modify: `src/Modules/Identity/Application/AuthDtos.cs`
- Modify: `src/Modules/Identity/Application/AuthService.cs`
- Modify: `src/Modules/Identity/Presentation/AuthEndpoints.cs`
- Test: `tests/Identity.Tests/AuthServiceAcceptInviteTests.cs`

**Interfaces:**
- Consumes: `UserInvite` (Task 9), `IssueRefreshTokenAsync`/`HashToken` (existing private members of `AuthService`, reused directly — same class, no duplication needed).
- Produces: `AcceptInviteRequest(string Token, string Password)`. `AuthService.AcceptInviteAsync(AcceptInviteRequest, CancellationToken)` returning `Task<AuthResult>` — same return shape as `RegisterOrganizationAsync`/`LoginAsync`, throws `AuthenticationFailedException` for an invalid/expired/already-accepted token or an empty password. `POST /api/v1/auth/accept-invite`, unauthenticated (same class as `/register`/`/login`).

- [ ] **Step 1: Write the failing tests**

`tests/Identity.Tests/AuthServiceAcceptInviteTests.cs`:

```csharp
using System.Security.Cryptography;
using Courier.Audit.Application;
using Courier.Branches.Application;
using Courier.Identity.Application;
using Courier.Identity.Domain;
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

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class NullBranchLookupService : IBranchLookupService
{
    public Task<BranchZoneInfo?> GetZoneInfoAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        Task.FromResult<BranchZoneInfo?>(null);
    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}

public class AuthServiceAcceptInviteTests
{
    private static AppDbContext CreateContext() => TestDbContextFactory.Create(new NullTenantProvider());

    private static AuthService CreateService(AppDbContext dbContext)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            SigningKey = "identity-tests-signing-key-at-least-32-bytes!",
            Issuer = "courier-tests",
            Audience = "courier-tests"
        });
        return new AuthService(dbContext, new PasswordHasherAdapter(), new JwtTokenService(jwtOptions), new NoopAuditLogger(), new NullBranchLookupService());
    }

    private static async Task<(Guid OrganizationId, Guid UserId, string PlainTextToken)> SeedInvitedUserAsync(AppDbContext dbContext, DateTimeOffset? expiresAt = null, DateTimeOffset? acceptedAt = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var organizationId = Guid.NewGuid();
        dbContext.Set<Organization>().Add(new Organization { Id = organizationId, Name = $"Org {suffix}", Slug = $"org-{suffix}" });
        var user = new User
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Email = $"invitee-{suffix}@example.com",
            PasswordHash = new PasswordHasherAdapter().Hash(Guid.NewGuid().ToString()), FullName = "Invitee Person", IsActive = true
        };
        dbContext.Set<User>().Add(user);
        var plainTextToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plainTextToken)));
        dbContext.Set<UserInvite>().Add(new UserInvite
        {
            Id = Guid.NewGuid(), UserId = user.Id, TokenHash = tokenHash,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(7), AcceptedAt = acceptedAt
        });
        await dbContext.SaveChangesAsync();
        return (organizationId, user.Id, plainTextToken);
    }

    [Fact]
    public async Task AcceptInviteAsync_ValidToken_SetsPasswordAndReturnsAuthResult()
    {
        await using var dbContext = CreateContext();
        var (organizationId, userId, plainTextToken) = await SeedInvitedUserAsync(dbContext);
        var service = CreateService(dbContext);

        var result = await service.AcceptInviteAsync(new AcceptInviteRequest(plainTextToken, "N3wP@ssword!"));

        Assert.Equal(userId, result.UserId);
        Assert.Equal(organizationId, result.OrganizationId);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));

        // The new password now actually works via the normal login path.
        var loginResult = await service.LoginAsync(new LoginRequest((await dbContext.Set<User>().FindAsync(userId))!.Email, "N3wP@ssword!"));
        Assert.Equal(userId, loginResult.UserId);
    }

    [Fact]
    public async Task AcceptInviteAsync_ExpiredInvite_Throws()
    {
        await using var dbContext = CreateContext();
        var (_, _, plainTextToken) = await SeedInvitedUserAsync(dbContext, expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => service.AcceptInviteAsync(new AcceptInviteRequest(plainTextToken, "N3wP@ssword!")));
    }

    [Fact]
    public async Task AcceptInviteAsync_AlreadyAccepted_Throws()
    {
        await using var dbContext = CreateContext();
        var (_, _, plainTextToken) = await SeedInvitedUserAsync(dbContext, acceptedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => service.AcceptInviteAsync(new AcceptInviteRequest(plainTextToken, "N3wP@ssword!")));
    }

    [Fact]
    public async Task AcceptInviteAsync_WrongToken_Throws()
    {
        await using var dbContext = CreateContext();
        await SeedInvitedUserAsync(dbContext);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            service.AcceptInviteAsync(new AcceptInviteRequest(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), "N3wP@ssword!")));
    }

    [Fact]
    public async Task AcceptInviteAsync_EmptyPassword_Throws()
    {
        await using var dbContext = CreateContext();
        var (_, _, plainTextToken) = await SeedInvitedUserAsync(dbContext);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => service.AcceptInviteAsync(new AcceptInviteRequest(plainTextToken, "")));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter AuthServiceAcceptInviteTests`
Expected: FAIL — `AuthService` has no `AcceptInviteAsync` member and `AcceptInviteRequest` doesn't exist (compile error).

- [ ] **Step 3: Add `AcceptInviteRequest` to `AuthDtos.cs`**

Add to `src/Modules/Identity/Application/AuthDtos.cs`:

```csharp
public record AcceptInviteRequest(string Token, string Password);
```

- [ ] **Step 4: Implement `AcceptInviteAsync`**

Add to `src/Modules/Identity/Application/AuthService.cs`, after `RefreshTokenAsync`:

```csharp
    public async Task<AuthResult> AcceptInviteAsync(AcceptInviteRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new AuthenticationFailedException("Password is required.");
        }

        var hash = HashToken(request.Token);
        var invite = await _dbContext.Set<UserInvite>().FirstOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        if (invite is null || invite.AcceptedAt is not null || invite.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new AuthenticationFailedException("This invite link is invalid or has expired.");
        }

        // No tenant is known yet at this pre-authentication endpoint — same reasoning as
        // RegisterOrganizationAsync's/LoginAsync's own IgnoreQueryFilters() reads.
        var user = await _dbContext.Set<User>().IgnoreQueryFilters().FirstAsync(u => u.Id == invite.UserId, cancellationToken);
        user.PasswordHash = _passwordHasher.Hash(request.Password);
        invite.AcceptedAt = DateTimeOffset.UtcNow;

        var refreshToken = await IssueRefreshTokenAsync(user.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(user.OrganizationId, user.Id, "user.invite_accepted", "User", user.Id.ToString()), cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(user.Id, user.OrganizationId!.Value, user.Email);
        return new AuthResult(accessToken, refreshToken.PlainText, user.Id, user.OrganizationId.Value);
    }
```

- [ ] **Step 5: Wire the endpoint**

In `src/Modules/Identity/Presentation/AuthEndpoints.cs`, insert immediately before the existing `group.MapGet("/me", ...)` block:

```csharp
        group.MapPost("/accept-invite", async (AcceptInviteRequest request, AuthService authService) =>
        {
            try
            {
                var result = await authService.AcceptInviteAsync(request);
                return Results.Ok(result);
            }
            catch (AuthenticationFailedException ex)
            {
                return Results.BadRequest(new { error = new { code = "ACCEPT_INVITE_FAILED", message = ex.Message } });
            }
        });

```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Identity.Tests/Identity.Tests.csproj --filter AuthServiceAcceptInviteTests`
Expected: PASS (5 tests)

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Identity/Application/AuthDtos.cs src/Modules/Identity/Application/AuthService.cs src/Modules/Identity/Presentation/AuthEndpoints.cs tests/Identity.Tests/AuthServiceAcceptInviteTests.cs
git commit -m "feat(identity): add POST /api/v1/auth/accept-invite"
```

---

## Task 13: Integration tests for every new endpoint (tenant/branch scoping, through the real HTTP pipeline)

**Files:**
- Modify: `tests/Api.Tests/UsersEndpointsTests.cs`
- Create: `tests/Api.Tests/RolesEndpointsTests.cs`

**Interfaces:**
- Consumes: everything Tasks 1–12 produced. No new production interfaces — this task proves Tasks 3–12's endpoints work end-to-end through the real ASP.NET Core pipeline and real Postgres, including tenant/branch scoping, which the in-memory unit tests in those tasks cannot fully prove (their `TestDbContext`s have no `AppDbContext`-level global query filter to accidentally rely on) — the same reasoning as F7 Task 6.

- [ ] **Step 1: Fix the pre-existing test broken by Task 7's response-shape change**

`GetUsers_OwnerWithUserReadPermission_Returns200WithSelf` in `tests/Api.Tests/UsersEndpointsTests.cs` currently deserializes `List<UserListItem>` — Task 7 changed `GET /api/v1/users` to return a `PagedResult<UserListItem>`. Replace the file's bottom `file record UserListItem(...)` line and the test body:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Identity.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class UsersEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UsersEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Users Org {suffix}", $"users-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task GetUsers_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_OwnerWithUserReadPermission_Returns200WithSelf()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<UserListItem>>();
        var self = Assert.Single(page!.Items);
        Assert.Equal(auth.UserId, self.Id);
        Assert.Equal("OrganizationOwner", self.RoleName);
    }
}

file record UserListItem(Guid Id, string Email, string FullName, bool IsActive, string? RoleName, int BranchCount);
```

- [ ] **Step 2: Run the fixed test to verify it passes against Task 7's real change**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter GetUsers_OwnerWithUserReadPermission_Returns200WithSelf`
Expected: PASS

- [ ] **Step 3: Write the new integration test file**

`tests/Api.Tests/RolesEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Events.Domain;
using Courier.Identity.Application;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

public class RolesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RolesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"{label} Org {suffix}", $"{label}-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task GetPermissions_ReturnsGroupedCatalogIncludingShipmentGroup()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "perm");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/permissions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var groups = await response.Content.ReadFromJsonAsync<List<PermissionGroupResponse>>();
        var shipmentGroup = groups!.Single(g => g.Group == "shipment");
        Assert.Contains(shipmentGroup.Permissions, p => p.Key == "shipment.create");
    }

    [Fact]
    public async Task CreateRole_ThenList_ThenUpdate_ThenDelete_FullLifecycle()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "rolelifecycle");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var created = await (await client.PostAsJsonAsync("/api/v1/roles",
            new CreateRoleRequest("CustomSupport", new[] { "shipment.read", "ndr.read" }))).Content.ReadFromJsonAsync<RoleResponse>();
        Assert.False(created!.IsSystemDefault);

        var listResponse = await client.GetAsync("/api/v1/roles");
        var roles = await listResponse.Content.ReadFromJsonAsync<List<RoleResponse>>();
        Assert.Contains(roles!, r => r.Id == created.Id);
        Assert.True(roles!.Count >= 15); // 14 seeded + this one

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/roles/{created.Id}", new UpdateRoleRequest(new[] { "cod.read" }));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<RoleResponse>();
        Assert.Equal(new[] { "cod.read" }, updated!.PermissionKeys);

        var deleteResponse = await client.DeleteAsync($"/api/v1/roles/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        var afterDelete = await (await client.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        Assert.DoesNotContain(afterDelete!, r => r.Id == created.Id);
    }

    [Fact]
    public async Task DeleteRole_SystemDefault_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "roledeletedefault");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var roles = await (await client.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        var ownerRole = roles!.Single(r => r.Name == "OrganizationOwner");

        var response = await client.DeleteAsync($"/api/v1/roles/{ownerRole.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListRoles_DoesNotLeakAnotherOrganizationsCustomRole()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "roletenantA");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var createdInA = await (await clientA.PostAsJsonAsync("/api/v1/roles",
            new CreateRoleRequest("OnlyInOrgA", new[] { "shipment.read" }))).Content.ReadFromJsonAsync<RoleResponse>();

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "roletenantB");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var rolesInB = await (await clientB.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        Assert.DoesNotContain(rolesInB!, r => r.Id == createdInA!.Id);
    }

    [Fact]
    public async Task GrantBranch_ThenList_ThenRevoke_FullLifecycle()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "grantlifecycle");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var branchId = await CreateBranchAsync(client);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);

        var granted = await (await client.PostAsJsonAsync($"/api/v1/users/{org.UserId}/branches",
            new GrantBranchRequest(branchId, expiresAt))).Content.ReadFromJsonAsync<UserBranchGrantResponse>();
        Assert.Equal(branchId, granted!.BranchId);

        var grants = await (await client.GetAsync($"/api/v1/users/{org.UserId}/branches")).Content.ReadFromJsonAsync<List<UserBranchGrantResponse>>();
        Assert.Contains(grants!, g => g.BranchId == branchId);

        var revokeResponse = await client.DeleteAsync($"/api/v1/users/{org.UserId}/branches/{branchId}");
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);
        var afterRevoke = await (await client.GetAsync($"/api/v1/users/{org.UserId}/branches")).Content.ReadFromJsonAsync<List<UserBranchGrantResponse>>();
        Assert.DoesNotContain(afterRevoke!, g => g.BranchId == branchId);
    }

    [Fact]
    public async Task InviteUser_ThenAcceptInvite_ThenLoginWithNewPassword_FullLifecycle()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "invitelifecycle");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var roles = await (await client.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        var branchManagerRole = roles!.Single(r => r.Name == "BranchManager");
        var branchId = await CreateBranchAsync(client);
        var email = $"invitee-{Guid.NewGuid():N}@example.com";

        var inviteResponse = await client.PostAsJsonAsync("/api/v1/users/invite",
            new InviteUserRequest(email, "Invited Person", null, branchManagerRole.Id, branchId, null));
        Assert.Equal(HttpStatusCode.OK, inviteResponse.StatusCode);

        // Read the plaintext invite token back out of the persisted OutboxMessage's InviteUrl —
        // never exposed over HTTP (only its SHA-256 hash is stored), but this row IS the same
        // artifact NotificationDispatchService (Task 10) would consume in production, so reading
        // it here proves the full real chain rather than reaching into AuthService internals.
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outboxMessage = await dbContext.Set<OutboxMessage>()
            .Where(m => m.EventType == "user.invited")
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync(m => m.Payload.Contains(email));
        var payload = JsonDocument.Parse(outboxMessage.Payload).RootElement;
        var inviteUrl = payload.GetProperty("InviteUrl").GetString()!;
        var token = Uri.UnescapeDataString(inviteUrl.Split("token=")[1]);

        var acceptResponse = await client.PostAsJsonAsync("/api/v1/auth/accept-invite", new AcceptInviteRequest(token, "N3wP@ssword!"));
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);
        var acceptResult = await acceptResponse.Content.ReadFromJsonAsync<AuthResult>();

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "N3wP@ssword!"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        Assert.Equal(acceptResult!.UserId, loginResult!.UserId);
    }

    [Fact]
    public async Task DeactivateUser_ThenLoginFails_ThenReactivate_LoginSucceeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "deactivatelifecycle");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var roles = await (await client.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        var counterStaffRole = roles!.Single(r => r.Name == "CounterStaff");
        var branchId = await CreateBranchAsync(client);
        var email = $"staff-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/v1/users/invite", new InviteUserRequest(email, "Staff Person", null, counterStaffRole.Id, branchId, null));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invitedUser = await dbContext.Set<Courier.Identity.Domain.User>().IgnoreQueryFilters().SingleAsync(u => u.Email == email);
        var invite = await dbContext.Set<Courier.Identity.Domain.UserInvite>().SingleAsync(i => i.UserId == invitedUser.Id);
        var outboxMessage = await dbContext.Set<OutboxMessage>().Where(m => m.EventType == "user.invited").OrderByDescending(m => m.CreatedAt).FirstAsync(m => m.Payload.Contains(email));
        var token = Uri.UnescapeDataString(JsonDocument.Parse(outboxMessage.Payload).RootElement.GetProperty("InviteUrl").GetString()!.Split("token=")[1]);
        await client.PostAsJsonAsync("/api/v1/auth/accept-invite", new AcceptInviteRequest(token, "St@ffPassword1"));

        var deactivateResponse = await client.PatchAsync($"/api/v1/users/{invitedUser.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivateResponse.StatusCode);
        var loginAfterDeactivate = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "St@ffPassword1"));
        Assert.Equal(HttpStatusCode.BadRequest, loginAfterDeactivate.StatusCode);

        var reactivateResponse = await client.PatchAsync($"/api/v1/users/{invitedUser.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, reactivateResponse.StatusCode);
        var loginAfterReactivate = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "St@ffPassword1"));
        Assert.Equal(HttpStatusCode.OK, loginAfterReactivate.StatusCode);
    }

    // Minimal real Branch, matching the shape SeedBranchesAsync-style helpers in the other
    // *EndpointsTests.cs files already use — this file only needs one branch, not an
    // origin/destination pair.
    private static async Task<Guid> CreateBranchAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var response = await client.PostAsJsonAsync("/api/v1/branches", new
        {
            code = $"BR{suffix}",
            name = $"Branch {suffix}",
            type = "Branch",
            provinceId = GeographyIds.ProvinceId("Bagmati"),
            districtId = GeographyIds.DistrictId("Bagmati", "Kathmandu"),
            localLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City"),
            wardNumber = 1,
            addressLine = "Somewhere in Kathmandu"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }
}
```

Add `using Courier.Geography.Infrastructure;` to the top of the file for `GeographyIds` (same namespace `tests/Identity.Tests/BranchAuthorizationCheckerTests.cs` already imports it from).

- [ ] **Step 4: Run all new and fixed tests**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "FullyQualifiedName~RolesEndpointsTests|FullyQualifiedName~UsersEndpointsTests"`
Expected: PASS — all 9 tests (2 from `UsersEndpointsTests` + 7 from `RolesEndpointsTests`).

- [ ] **Step 5: Run the full backend test suite**

Run: `dotnet test`
Expected: PASS — every test project green, no regressions in any earlier phase's coverage.

- [ ] **Step 6: Commit**

```bash
git add tests/Api.Tests/UsersEndpointsTests.cs tests/Api.Tests/RolesEndpointsTests.cs
git commit -m "test(api): add integration coverage for Frontend Phase F9's new Identity endpoints"
```

---

## Task 14: `admin` feature types, read-only BFF routes, and read hooks

**Files:**
- Create: `src/web/src/features/admin/types.ts`
- Create: `src/web/src/app/api/permissions/route.ts`
- Create: `src/web/src/app/api/roles/route.ts`
- Create: `src/web/src/app/api/roles/route.test.ts`
- Create: `src/web/src/app/api/users/route.ts`
- Create: `src/web/src/app/api/users/route.test.ts`
- Create: `src/web/src/app/api/users/[id]/branches/route.ts`
- Create: `src/web/src/features/admin/hooks/usePermissionCatalog.ts`
- Create: `src/web/src/features/admin/hooks/useRoles.ts`
- Create: `src/web/src/features/admin/hooks/useAdminUsers.ts`
- Create: `src/web/src/features/admin/hooks/useUserBranchGrants.ts`

**Interfaces:**
- Consumes: `apiFetch` (`@/lib/auth/apiFetch`), `clientFetch` (`@/lib/api/clientFetch`), `PagedResult<T>` (`@/components/data-table/DataTable`).
- Produces: `PermissionItem`, `PermissionGroup`, `Role`, `AdminUser`, `UserBranchGrant` types. `usePermissionCatalog()`, `useRoles()`, `useAdminUsers(params)`, `useUserBranchGrants(userId)`.

- [ ] **Step 1: Write the types**

`src/web/src/features/admin/types.ts`:

```typescript
export interface PermissionItem {
  key: string;
  description: string;
}

export interface PermissionGroup {
  group: string;
  permissions: PermissionItem[];
}

export interface Role {
  id: string;
  name: string;
  isSystemDefault: boolean;
  permissionKeys: string[];
}

export interface CreateRoleInput {
  name: string;
  permissionKeys: string[];
}

export interface UpdateRoleInput {
  permissionKeys: string[];
}

export interface AdminUser {
  id: string;
  email: string;
  fullName: string;
  isActive: boolean;
  roleName: string | null;
  branchCount: number;
}

export interface InviteUserInput {
  email: string;
  fullName: string;
  phone?: string;
  roleId: string;
  branchId: string;
  expiresAt?: string;
}

export interface UserBranchGrant {
  branchId: string;
  branchName: string;
  grantedAt: string;
  expiresAt: string | null;
}

export interface GrantBranchInput {
  branchId: string;
  expiresAt?: string;
}
```

- [ ] **Step 2: Add the read-only BFF routes**

`src/web/src/app/api/permissions/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/permissions");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/roles/route.ts` (Task 15 adds a `POST` export to this same file):

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/roles");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/users/route.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/users${query}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/users/[id]/branches/route.ts` (Task 15 adds a `POST` export to this same file):

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/users/${params.id}/branches`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 3: Write BFF route tests for the two query-string-free GETs worth pinning**

`src/web/src/app/api/roles/route.test.ts`:

```typescript
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/roles", () => {
  it("proxies to the backend roles endpoint", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    } as Response);

    const response = await GET();

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/roles");
    expect(response.status).toBe(200);
  });
});
```

`src/web/src/app/api/users/route.test.ts`:

```typescript
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";
import { NextRequest } from "next/server";

describe("GET /api/users", () => {
  it("proxies the query string to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }),
    } as Response);

    const request = new NextRequest("http://localhost/api/users?page=2");
    const response = await GET(request);

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/users?page=2");
    expect(response.status).toBe(200);
  });
});
```

- [ ] **Step 4: Run the new BFF tests**

Run: `cd src/web && npm test -- roles/route.test.ts users/route.test.ts`
Expected: PASS (2 tests)

- [ ] **Step 5: Write the read hooks**

`src/web/src/features/admin/hooks/usePermissionCatalog.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PermissionGroup } from "@/features/admin/types";

export function usePermissionCatalog() {
  return useQuery({
    queryKey: ["admin", "permissions"],
    queryFn: () => clientFetch<PermissionGroup[]>("/api/permissions"),
  });
}
```

`src/web/src/features/admin/hooks/useRoles.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Role } from "@/features/admin/types";

export function useRoles() {
  return useQuery({
    queryKey: ["admin", "roles"],
    queryFn: () => clientFetch<Role[]>("/api/roles"),
  });
}
```

`src/web/src/features/admin/hooks/useAdminUsers.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { AdminUser } from "@/features/admin/types";

export function useAdminUsers(params: { page?: number; pageSize?: number } = {}) {
  const { page, pageSize } = params;
  const query = new URLSearchParams();
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["admin", "users", page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<AdminUser>>(`/api/users${search ? `?${search}` : ""}`),
  });
}
```

`src/web/src/features/admin/hooks/useUserBranchGrants.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { UserBranchGrant } from "@/features/admin/types";

export function useUserBranchGrants(userId: string) {
  return useQuery({
    queryKey: ["admin", "users", userId, "branches"],
    queryFn: () => clientFetch<UserBranchGrant[]>(`/api/users/${userId}/branches`),
    enabled: userId.length > 0,
  });
}
```

- [ ] **Step 6: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS — no existing tests broken, no type errors.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/admin/types.ts src/web/src/app/api/permissions src/web/src/app/api/roles src/web/src/app/api/users src/web/src/features/admin/hooks
git commit -m "feat(admin-frontend): add types, read-only BFF routes, and read hooks"
```

---

## Task 15: Mutation BFF routes, mutation hooks, and `adminErrorMessage`

**Files:**
- Modify: `src/web/src/app/api/roles/route.ts`
- Create: `src/web/src/app/api/roles/[id]/route.ts`
- Create: `src/web/src/app/api/users/invite/route.ts`
- Create: `src/web/src/app/api/users/[id]/deactivate/route.ts`
- Create: `src/web/src/app/api/users/[id]/reactivate/route.ts`
- Modify: `src/web/src/app/api/users/[id]/branches/route.ts`
- Create: `src/web/src/app/api/users/[id]/branches/[branchId]/route.ts`
- Create: `src/web/src/features/admin/lib/adminErrorMessage.ts`
- Create: `src/web/src/features/admin/hooks/useCreateRole.ts`
- Create: `src/web/src/features/admin/hooks/useUpdateRole.ts`
- Create: `src/web/src/features/admin/hooks/useDeleteRole.ts`
- Create: `src/web/src/features/admin/hooks/useInviteUser.ts`
- Create: `src/web/src/features/admin/hooks/useSetUserActive.ts`
- Create: `src/web/src/features/admin/hooks/useGrantBranch.ts`
- Create: `src/web/src/features/admin/hooks/useRevokeBranch.ts`

**Interfaces:**
- Consumes: `Role`, `CreateRoleInput`, `UpdateRoleInput`, `AdminUser`, `InviteUserInput`, `UserBranchGrant`, `GrantBranchInput` (Task 14).
- Produces: `adminErrorMessage(error: unknown): string`. `useCreateRole()`, `useUpdateRole(id)`, `useDeleteRole()`, `useInviteUser()`, `useSetUserActive(userId)` (one hook covering both deactivate/reactivate, since both are the identical shape modulo URL segment), `useGrantBranch(userId)`, `useRevokeBranch(userId)`.

- [ ] **Step 1: Add the mutation BFF routes**

`src/web/src/app/api/roles/route.ts` — add a `POST` export alongside the existing `GET`:

```typescript
export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/roles", {
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

(Add `NextRequest` to the existing `next/server` import on this file's first line.)

`src/web/src/app/api/roles/[id]/route.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function PUT(request: NextRequest, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/roles/${params.id}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function DELETE(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/roles/${params.id}`, { method: "DELETE" });
    if (response.status === 204) {
      return new NextResponse(null, { status: 204 });
    }
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/users/invite/route.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/users/invite", {
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

`src/web/src/app/api/users/[id]/deactivate/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function PATCH(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/users/${params.id}/deactivate`, { method: "PATCH" });
    return new NextResponse(null, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/users/[id]/reactivate/route.ts` (identical except the upstream path):

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function PATCH(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/users/${params.id}/reactivate`, { method: "PATCH" });
    return new NextResponse(null, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/users/[id]/branches/route.ts` — add a `POST` export alongside the existing `GET`:

```typescript
export async function POST(request: Request, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/users/${params.id}/branches`, {
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

`src/web/src/app/api/users/[id]/branches/[branchId]/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function DELETE(_request: Request, { params }: { params: { id: string; branchId: string } }) {
  try {
    const response = await apiFetch(`/api/v1/users/${params.id}/branches/${params.branchId}`, { method: "DELETE" });
    return new NextResponse(null, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 2: Write `adminErrorMessage`**

`src/web/src/features/admin/lib/adminErrorMessage.ts` (mirrors `codErrorMessage.ts` exactly — every new endpoint's error body is `{ error: { code, message } }`):

```typescript
export function adminErrorMessage(error: unknown): string {
  try {
    const parsed = JSON.parse((error as Error).message);
    const message = parsed?.error?.message;
    if (typeof message === "string") {
      return message;
    }
  } catch {
    // fall through to generic message
  }
  return "Something went wrong. Check the fields and try again.";
}
```

- [ ] **Step 3: Write the mutation hooks**

`src/web/src/features/admin/hooks/useCreateRole.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Role, CreateRoleInput } from "@/features/admin/types";

export function useCreateRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateRoleInput) =>
      clientFetch<Role>("/api/roles", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["admin", "roles"] }),
  });
}
```

`src/web/src/features/admin/hooks/useUpdateRole.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Role, UpdateRoleInput } from "@/features/admin/types";

export function useUpdateRole(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: UpdateRoleInput) =>
      clientFetch<Role>(`/api/roles/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["admin", "roles"] }),
  });
}
```

`src/web/src/features/admin/hooks/useDeleteRole.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useDeleteRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => clientFetch<void>(`/api/roles/${id}`, { method: "DELETE" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["admin", "roles"] }),
  });
}
```

`src/web/src/features/admin/hooks/useInviteUser.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { InviteUserInput } from "@/features/admin/types";

export function useInviteUser() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: InviteUserInput) =>
      clientFetch<{ userId: string; email: string }>("/api/users/invite", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["admin", "users"] }),
  });
}
```

`src/web/src/features/admin/hooks/useSetUserActive.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useSetUserActive(userId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (active: boolean) =>
      clientFetch<void>(`/api/users/${userId}/${active ? "reactivate" : "deactivate"}`, { method: "PATCH" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["admin", "users"] }),
  });
}
```

`src/web/src/features/admin/hooks/useGrantBranch.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { UserBranchGrant, GrantBranchInput } from "@/features/admin/types";

export function useGrantBranch(userId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: GrantBranchInput) =>
      clientFetch<UserBranchGrant>(`/api/users/${userId}/branches`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["admin", "users", userId, "branches"] }),
  });
}
```

`src/web/src/features/admin/hooks/useRevokeBranch.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useRevokeBranch(userId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (branchId: string) => clientFetch<void>(`/api/users/${userId}/branches/${branchId}`, { method: "DELETE" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["admin", "users", userId, "branches"] }),
  });
}
```

- [ ] **Step 4: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS — no existing tests broken, no type errors.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/app/api/roles src/web/src/app/api/users src/web/src/features/admin/lib src/web/src/features/admin/hooks
git commit -m "feat(admin-frontend): add mutation BFF routes, mutation hooks, and adminErrorMessage"
```

---

## Task 16: `adminPanelStore`, the `/admin` page shell with tab switcher, nav entry, and the Roles tab

**Files:**
- Create: `src/web/src/features/admin/store/adminPanelStore.ts`
- Create: `src/web/src/features/admin/store/adminPanelStore.test.ts`
- Create: `src/web/src/features/admin/components/RolesTab.tsx`
- Create: `src/web/src/features/admin/components/RoleCreateSheet.tsx`
- Create: `src/web/src/features/admin/components/RoleDetailSheet.tsx`
- Create: `src/web/src/app/(staff)/admin/page.tsx`
- Modify: `src/web/src/components/nav-main.tsx`

**Interfaces:**
- Consumes: `Role`, `PermissionGroup`, `CreateRoleInput`, `UpdateRoleInput` (Task 14), `useRoles`, `usePermissionCatalog` (Task 14), `useCreateRole`, `useUpdateRole`, `useDeleteRole` (Task 15), `adminErrorMessage` (Task 15), `usePermission` (`@/features/auth/hooks/usePermission`), `DataTable`/`ColumnDef` (`@/components/data-table/DataTable`).
- Produces: `useAdminPanelStore` — `{ panel: {mode:"closed"} | {mode:"role-create"} | {mode:"role-detail",id} | {mode:"user-invite"} | {mode:"user-detail",id}, openRoleCreate, openRoleDetail, openUserInvite, openUserDetail, close }`. The `/admin` route and its sidebar entry.

- [ ] **Step 1: Write the failing store test**

`src/web/src/features/admin/store/adminPanelStore.test.ts`:

```typescript
import { describe, expect, it } from "vitest";
import { useAdminPanelStore } from "./adminPanelStore";

describe("useAdminPanelStore", () => {
  it("starts closed", () => {
    expect(useAdminPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openRoleDetail sets mode and id, close resets to closed", () => {
    useAdminPanelStore.getState().openRoleDetail("role-1");
    expect(useAdminPanelStore.getState().panel).toEqual({ mode: "role-detail", id: "role-1" });
    useAdminPanelStore.getState().close();
    expect(useAdminPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openUserInvite sets mode with no id", () => {
    useAdminPanelStore.getState().openUserInvite();
    expect(useAdminPanelStore.getState().panel).toEqual({ mode: "user-invite" });
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npm test -- adminPanelStore.test.ts`
Expected: FAIL — `./adminPanelStore` doesn't exist yet.

- [ ] **Step 3: Write `adminPanelStore.ts`**

`src/web/src/features/admin/store/adminPanelStore.ts`:

```typescript
import { create } from "zustand";

type AdminPanelState =
  | { mode: "closed" }
  | { mode: "role-create" }
  | { mode: "role-detail"; id: string }
  | { mode: "user-invite" }
  | { mode: "user-detail"; id: string };

interface AdminPanelStore {
  panel: AdminPanelState;
  openRoleCreate: () => void;
  openRoleDetail: (id: string) => void;
  openUserInvite: () => void;
  openUserDetail: (id: string) => void;
  close: () => void;
}

// One shared store for every /admin sheet (docs/ARCHITECTURE.md §9a), mirroring codPanelStore's
// discriminated-union-across-all-sheet-kinds shape — only one sheet is ever open at a time on
// this single-page, tabbed UI.
export const useAdminPanelStore = create<AdminPanelStore>((set) => ({
  panel: { mode: "closed" },
  openRoleCreate: () => set({ panel: { mode: "role-create" } }),
  openRoleDetail: (id) => set({ panel: { mode: "role-detail", id } }),
  openUserInvite: () => set({ panel: { mode: "user-invite" } }),
  openUserDetail: (id) => set({ panel: { mode: "user-detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/web && npm test -- adminPanelStore.test.ts`
Expected: PASS (3 tests)

- [ ] **Step 5: Write `RolesTab.tsx`**

`src/web/src/features/admin/components/RolesTab.tsx`:

```tsx
"use client";

import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useRoles } from "@/features/admin/hooks/useRoles";
import { useAdminPanelStore } from "@/features/admin/store/adminPanelStore";
import { usePermission } from "@/features/auth/hooks/usePermission";
import type { Role } from "@/features/admin/types";

export function RolesTab() {
  const { data: roles, isLoading } = useRoles();
  const openCreate = useAdminPanelStore((s) => s.openRoleCreate);
  const openDetail = useAdminPanelStore((s) => s.openRoleDetail);
  const canManage = usePermission("role.manage");

  const columns: ColumnDef<Role>[] = [
    {
      key: "name",
      header: "Name",
      render: (r) => (
        <button type="button" onClick={() => openDetail(r.id)} className="font-medium text-foreground hover:underline">
          {r.name}
        </button>
      ),
    },
    { key: "permissionCount", header: "Permissions", render: (r) => r.permissionKeys.length },
    {
      key: "kind",
      header: "Kind",
      render: (r) => <Badge variant={r.isSystemDefault ? "secondary" : "outline"}>{r.isSystemDefault ? "System" : "Custom"}</Badge>,
    },
  ];

  return (
    <div className="space-y-4">
      {canManage && (
        <div className="flex justify-end">
          <Button type="button" onClick={openCreate}>New Role</Button>
        </div>
      )}
      <DataTable
        columns={columns}
        data={roles ? { items: roles, page: 1, pageSize: roles.length || 1, totalCount: roles.length, totalPages: 1 } : undefined}
        isLoading={isLoading}
        emptyTitle="No roles"
        emptyDescription="Custom roles created for this organization will show up here."
        onPageChange={() => {}}
        rowKey={(r) => r.id}
      />
    </div>
  );
}
```

- [ ] **Step 6: Write `RoleCreateSheet.tsx`**

`src/web/src/features/admin/components/RoleCreateSheet.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { usePermissionCatalog } from "@/features/admin/hooks/usePermissionCatalog";
import { useCreateRole } from "@/features/admin/hooks/useCreateRole";
import { useAdminPanelStore } from "@/features/admin/store/adminPanelStore";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";

export function RoleCreateSheet() {
  const panel = useAdminPanelStore((s) => s.panel);
  const close = useAdminPanelStore((s) => s.close);
  const openDetail = useAdminPanelStore((s) => s.openRoleDetail);
  const isOpen = panel.mode === "role-create";

  const { data: groups } = usePermissionCatalog();
  const createRole = useCreateRole();
  const [name, setName] = useState("");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [serverError, setServerError] = useState<string | null>(null);

  function toggle(key: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });
  }

  function reset() {
    setName("");
    setSelected(new Set());
    setServerError(null);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setServerError(null);
    try {
      const created = await createRole.mutateAsync({ name, permissionKeys: Array.from(selected) });
      reset();
      openDetail(created.id);
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { reset(); close(); } }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>New Role</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="role-name">Name</Label>
            <Input id="role-name" value={name} onChange={(e) => setName(e.target.value)} required />
          </div>
          <div className="space-y-3">
            <Label>Permissions</Label>
            {groups?.map((group) => (
              <fieldset key={group.group} className="rounded-md border border-border p-3">
                <legend className="px-1 text-sm font-medium capitalize text-foreground">{group.group}</legend>
                <div className="grid grid-cols-1 gap-1.5 sm:grid-cols-2">
                  {group.permissions.map((p) => (
                    <label key={p.key} className="flex items-center gap-2 text-sm text-muted-foreground">
                      <input type="checkbox" checked={selected.has(p.key)} onChange={() => toggle(p.key)} />
                      {p.description}
                    </label>
                  ))}
                </div>
              </fieldset>
            ))}
          </div>
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="submit" disabled={createRole.isPending || name.length === 0}>
            {createRole.isPending ? <Spinner className="h-4 w-4" /> : "Create Role"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 7: Write `RoleDetailSheet.tsx`**

`src/web/src/features/admin/components/RoleDetailSheet.tsx`:

```tsx
"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { usePermissionCatalog } from "@/features/admin/hooks/usePermissionCatalog";
import { useRoles } from "@/features/admin/hooks/useRoles";
import { useUpdateRole } from "@/features/admin/hooks/useUpdateRole";
import { useDeleteRole } from "@/features/admin/hooks/useDeleteRole";
import { useAdminPanelStore } from "@/features/admin/store/adminPanelStore";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";

export function RoleDetailSheet() {
  const panel = useAdminPanelStore((s) => s.panel);
  const close = useAdminPanelStore((s) => s.close);
  const isOpen = panel.mode === "role-detail";
  const id = panel.mode === "role-detail" ? panel.id : "";

  const { data: roles } = useRoles();
  const role = roles?.find((r) => r.id === id);
  const { data: groups } = usePermissionCatalog();
  const updateRole = useUpdateRole(id);
  const deleteRole = useDeleteRole();
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [serverError, setServerError] = useState<string | null>(null);

  useEffect(() => {
    if (role) setSelected(new Set(role.permissionKeys));
  }, [role]);

  function toggle(key: string) {
    if (role?.isSystemDefault) return;
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });
  }

  async function handleSave() {
    setServerError(null);
    try {
      await updateRole.mutateAsync({ permissionKeys: Array.from(selected) });
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  async function handleDelete() {
    setServerError(null);
    try {
      await deleteRole.mutateAsync(id);
      close();
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle className="flex items-center gap-2">
            {role?.name ?? "Role"}
            {role && <Badge variant={role.isSystemDefault ? "secondary" : "outline"}>{role.isSystemDefault ? "System" : "Custom"}</Badge>}
          </SheetTitle>
        </SheetHeader>
        {!role && (
          <div className="flex items-center justify-center p-12">
            <Spinner className="h-6 w-6" />
          </div>
        )}
        {role && (
          <div className="mt-6 space-y-4">
            {role.isSystemDefault && (
              <p className="text-sm text-muted-foreground">System-default roles cannot be edited or deleted.</p>
            )}
            <div className="space-y-3">
              {groups?.map((group) => (
                <fieldset key={group.group} className="rounded-md border border-border p-3">
                  <legend className="px-1 text-sm font-medium capitalize text-foreground">{group.group}</legend>
                  <div className="grid grid-cols-1 gap-1.5 sm:grid-cols-2">
                    {group.permissions.map((p) => (
                      <label key={p.key} className="flex items-center gap-2 text-sm text-muted-foreground">
                        <input
                          type="checkbox"
                          checked={selected.has(p.key)}
                          disabled={role.isSystemDefault}
                          onChange={() => toggle(p.key)}
                        />
                        {p.description}
                      </label>
                    ))}
                  </div>
                </fieldset>
              ))}
            </div>
            {serverError && <p className="text-sm text-destructive">{serverError}</p>}
            {!role.isSystemDefault && (
              <div className="flex gap-2">
                <Button type="button" onClick={handleSave} disabled={updateRole.isPending}>
                  {updateRole.isPending ? <Spinner className="h-4 w-4" /> : "Save Changes"}
                </Button>
                <Button type="button" variant="destructive" onClick={handleDelete} disabled={deleteRole.isPending}>
                  Delete Role
                </Button>
              </div>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 8: Write the `/admin` page shell**

`src/web/src/app/(staff)/admin/page.tsx`:

```tsx
"use client";

import { useState } from "react";
import { RolesTab } from "@/features/admin/components/RolesTab";
import { RoleCreateSheet } from "@/features/admin/components/RoleCreateSheet";
import { RoleDetailSheet } from "@/features/admin/components/RoleDetailSheet";

// Plain button-row tab switcher, matching /cod's own precedent (no Tabs primitive in this repo).
// Task 17 appends the "users" tab entry, its `activeTab === "users"` branch, and its own Sheets
// at the bottom — this file grows additively, never restructured, the same way CodPage did.
const TABS = [
  { key: "roles", label: "Roles" },
] as const;

type TabKey = (typeof TABS)[number]["key"];

export default function AdminPage() {
  const [activeTab, setActiveTab] = useState<TabKey>("roles");

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">Admin</h1>
      <div className="flex gap-1 border-b border-border" role="tablist">
        {TABS.map((tab) => (
          <button
            key={tab.key}
            type="button"
            role="tab"
            aria-selected={activeTab === tab.key}
            onClick={() => setActiveTab(tab.key)}
            className={`px-3 py-2 text-sm font-medium transition-colors ${
              activeTab === tab.key
                ? "border-b-2 border-foreground text-foreground"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === "roles" && <RolesTab />}

      <RoleCreateSheet />
      <RoleDetailSheet />
    </div>
  );
}
```

- [ ] **Step 9: Add the nav entry**

In `src/web/src/components/nav-main.tsx`, add `ShieldCheck` to the `lucide-react` import list and insert a new entry at the end of `NAV_ITEMS`:

```typescript
  { href: "/admin", label: "Admin", permission: "role.manage", icon: ShieldCheck },
```

`usePermission` alone can't express "either permission" — leave this entry gated on `role.manage` for now and widen it to `role.manage` OR `user.read` in Task 17, once the Users tab (which only needs `user.read`) actually exists to justify a user without `role.manage` seeing the link at all.

- [ ] **Step 10: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS — no existing tests broken, no type errors.

- [ ] **Step 11: Commit**

```bash
git add src/web/src/features/admin/store src/web/src/features/admin/components/RolesTab.tsx src/web/src/features/admin/components/RoleCreateSheet.tsx src/web/src/features/admin/components/RoleDetailSheet.tsx "src/web/src/app/(staff)/admin" src/web/src/components/nav-main.tsx
git commit -m "feat(admin-frontend): add /admin page shell and the Roles tab"
```

---

## Task 17: The Users tab (invite, deactivate/reactivate, branch grants)

**Files:**
- Create: `src/web/src/features/admin/components/UsersTab.tsx`
- Create: `src/web/src/features/admin/components/UserInviteSheet.tsx`
- Create: `src/web/src/features/admin/components/UserDetailSheet.tsx`
- Create: `src/web/src/features/admin/components/UserBranchGrants.tsx`
- Modify: `src/web/src/app/(staff)/admin/page.tsx`
- Modify: `src/web/src/components/nav-main.tsx`

**Interfaces:**
- Consumes: `AdminUser`, `InviteUserInput`, `UserBranchGrant`, `GrantBranchInput` (Task 14), `useAdminUsers`, `useUserBranchGrants`, `useRoles` (Task 14/16), `useInviteUser`, `useSetUserActive`, `useGrantBranch`, `useRevokeBranch` (Task 15), `adminErrorMessage` (Task 15), `useBranches` (existing, `@/features/branches/hooks/useBranches`).
- Produces: no new hooks/types — pure UI composition of Task 14/15/16's surface, plus a widened `NavItem.permission: string | string[]` on the existing nav item type.

- [ ] **Step 1: Write `UsersTab.tsx`**

`src/web/src/features/admin/components/UsersTab.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useAdminUsers } from "@/features/admin/hooks/useAdminUsers";
import { useAdminPanelStore } from "@/features/admin/store/adminPanelStore";
import { usePermission } from "@/features/auth/hooks/usePermission";
import type { AdminUser } from "@/features/admin/types";

export function UsersTab() {
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const { data, isLoading } = useAdminUsers({ page, pageSize });
  const openDetail = useAdminPanelStore((s) => s.openUserDetail);
  const openInvite = useAdminPanelStore((s) => s.openUserInvite);
  const canInvite = usePermission("user.create");

  const columns: ColumnDef<AdminUser>[] = [
    {
      key: "email",
      header: "Email",
      render: (u) => (
        <button type="button" onClick={() => openDetail(u.id)} className="font-medium text-foreground hover:underline">
          {u.email}
        </button>
      ),
    },
    { key: "fullName", header: "Name", render: (u) => u.fullName },
    { key: "role", header: "Role", render: (u) => u.roleName ?? "—" },
    { key: "branches", header: "Branches", render: (u) => u.branchCount },
    {
      key: "status",
      header: "Status",
      render: (u) => <Badge variant={u.isActive ? "default" : "secondary"}>{u.isActive ? "Active" : "Inactive"}</Badge>,
    },
  ];

  return (
    <div className="space-y-4">
      {canInvite && (
        <div className="flex justify-end">
          <Button type="button" onClick={openInvite}>Invite User</Button>
        </div>
      )}
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No users"
        emptyDescription="Users invited into this organization will show up here."
        onPageChange={setPage}
        rowKey={(u) => u.id}
      />
    </div>
  );
}
```

- [ ] **Step 2: Write `UserInviteSheet.tsx`**

`src/web/src/features/admin/components/UserInviteSheet.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useRoles } from "@/features/admin/hooks/useRoles";
import { useInviteUser } from "@/features/admin/hooks/useInviteUser";
import { useAdminPanelStore } from "@/features/admin/store/adminPanelStore";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";
import { useBranches } from "@/features/branches/hooks/useBranches";

export function UserInviteSheet() {
  const panel = useAdminPanelStore((s) => s.panel);
  const close = useAdminPanelStore((s) => s.close);
  const isOpen = panel.mode === "user-invite";

  const { data: roles } = useRoles();
  const { data: branches } = useBranches();
  const inviteUser = useInviteUser();

  const [email, setEmail] = useState("");
  const [fullName, setFullName] = useState("");
  const [phone, setPhone] = useState("");
  const [roleId, setRoleId] = useState("");
  const [branchId, setBranchId] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  const [serverError, setServerError] = useState<string | null>(null);

  function reset() {
    setEmail(""); setFullName(""); setPhone(""); setRoleId(""); setBranchId(""); setExpiresAt(""); setServerError(null);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setServerError(null);
    try {
      // Anchored to explicit UTC end-of-day, same fix Frontend Phase F7 needed for
      // MerchantSettlementCreateSheet — a bare "YYYY-MM-DD" 500s on Npgsql's timestamptz writer.
      await inviteUser.mutateAsync({
        email, fullName, phone: phone || undefined, roleId, branchId,
        expiresAt: expiresAt ? `${expiresAt}T23:59:59Z` : undefined,
      });
      reset();
      close();
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { reset(); close(); } }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Invite User</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="invite-email">Email</Label>
            <Input id="invite-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
          </div>
          <div className="space-y-2">
            <Label htmlFor="invite-name">Full Name</Label>
            <Input id="invite-name" value={fullName} onChange={(e) => setFullName(e.target.value)} required />
          </div>
          <div className="space-y-2">
            <Label htmlFor="invite-phone">Phone</Label>
            <Input id="invite-phone" value={phone} onChange={(e) => setPhone(e.target.value)} />
          </div>
          <div className="space-y-2">
            <Label htmlFor="invite-role">Role</Label>
            <Select value={roleId} onValueChange={setRoleId}>
              <SelectTrigger id="invite-role" aria-label="Role">
                <SelectValue placeholder="Select role" />
              </SelectTrigger>
              <SelectContent>
                {roles?.map((r) => (
                  <SelectItem key={r.id} value={r.id}>{r.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-2">
            <Label htmlFor="invite-branch">Branch</Label>
            <Select value={branchId} onValueChange={setBranchId}>
              <SelectTrigger id="invite-branch" aria-label="Branch">
                <SelectValue placeholder="Select branch" />
              </SelectTrigger>
              <SelectContent>
                {branches?.map((b) => (
                  <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-2">
            <Label htmlFor="invite-expires">Branch Access Expires (optional)</Label>
            <Input id="invite-expires" type="date" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} />
          </div>
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="submit" disabled={inviteUser.isPending || !email || !fullName || !roleId || !branchId}>
            {inviteUser.isPending ? <Spinner className="h-4 w-4" /> : "Send Invite"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 3: Write `UserBranchGrants.tsx`**

`src/web/src/features/admin/components/UserBranchGrants.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { EmptyState } from "@/components/states/EmptyState";
import { useUserBranchGrants } from "@/features/admin/hooks/useUserBranchGrants";
import { useGrantBranch } from "@/features/admin/hooks/useGrantBranch";
import { useRevokeBranch } from "@/features/admin/hooks/useRevokeBranch";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { usePermission } from "@/features/auth/hooks/usePermission";

export function UserBranchGrants({ userId }: { userId: string }) {
  const { data: grants, isLoading } = useUserBranchGrants(userId);
  const { data: branches } = useBranches();
  const grantBranch = useGrantBranch(userId);
  const revokeBranch = useRevokeBranch(userId);
  const canUpdate = usePermission("user.update");

  const [adding, setAdding] = useState(false);
  const [branchId, setBranchId] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleAdd() {
    setServerError(null);
    try {
      await grantBranch.mutateAsync({ branchId, expiresAt: expiresAt ? `${expiresAt}T23:59:59Z` : undefined });
      setAdding(false);
      setBranchId("");
      setExpiresAt("");
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  async function handleRevoke(revokeBranchId: string) {
    setServerError(null);
    try {
      await revokeBranch.mutateAsync(revokeBranchId);
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  if (isLoading) {
    return <Spinner className="h-5 w-5" />;
  }

  return (
    <div className="space-y-3">
      <Label>Branch Access</Label>
      {(!grants || grants.length === 0) && !adding && <EmptyState title="No branch grants" description="This user has no branch access yet." />}
      <div className="space-y-2">
        {grants?.map((g) => (
          <div key={g.branchId} className="flex items-center justify-between rounded-md border border-border p-3 text-sm">
            <div>
              <p className="font-medium text-foreground">{g.branchName}</p>
              <p className="text-muted-foreground">{g.expiresAt ? `Expires ${new Date(g.expiresAt).toLocaleDateString()}` : "No expiry"}</p>
            </div>
            {canUpdate && (
              <Button type="button" size="sm" variant="outline" onClick={() => handleRevoke(g.branchId)} disabled={revokeBranch.isPending}>
                Revoke
              </Button>
            )}
          </div>
        ))}
      </div>
      {canUpdate && !adding && (
        <Button type="button" size="sm" variant="outline" onClick={() => setAdding(true)}>Add Grant</Button>
      )}
      {adding && (
        <div className="space-y-2 rounded-md border border-border p-3">
          <Select value={branchId} onValueChange={setBranchId}>
            <SelectTrigger aria-label="Branch">
              <SelectValue placeholder="Select branch" />
            </SelectTrigger>
            <SelectContent>
              {branches?.map((b) => (
                <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Input type="date" aria-label="Expires at (optional)" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} />
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <div className="flex gap-2">
            <Button type="button" size="sm" disabled={!branchId || grantBranch.isPending} onClick={handleAdd}>
              {grantBranch.isPending ? <Spinner className="h-4 w-4" /> : "Grant"}
            </Button>
            <Button type="button" size="sm" variant="outline" onClick={() => setAdding(false)}>Cancel</Button>
          </div>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Write `UserDetailSheet.tsx`**

`src/web/src/features/admin/components/UserDetailSheet.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useAdminUsers } from "@/features/admin/hooks/useAdminUsers";
import { useSetUserActive } from "@/features/admin/hooks/useSetUserActive";
import { useAdminPanelStore } from "@/features/admin/store/adminPanelStore";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { UserBranchGrants } from "@/features/admin/components/UserBranchGrants";

export function UserDetailSheet() {
  const panel = useAdminPanelStore((s) => s.panel);
  const close = useAdminPanelStore((s) => s.close);
  const isOpen = panel.mode === "user-detail";
  const id = panel.mode === "user-detail" ? panel.id : "";

  // Reuses the already-loaded list page rather than a dedicated GET /users/{id} — no such
  // endpoint exists (Task 7 only paginated the list), and the tab is almost always open with the
  // list already fetched when a row is clicked.
  const { data } = useAdminUsers({ pageSize: 1000 });
  const user = data?.items.find((u) => u.id === id);
  const setActive = useSetUserActive(id);
  const canUpdate = usePermission("user.update");
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleToggleActive() {
    setServerError(null);
    try {
      await setActive.mutateAsync(!user!.isActive);
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle className="flex items-center gap-2">
            {user?.fullName ?? "User"}
            {user && <Badge variant={user.isActive ? "default" : "secondary"}>{user.isActive ? "Active" : "Inactive"}</Badge>}
          </SheetTitle>
        </SheetHeader>
        {!user && (
          <div className="flex items-center justify-center p-12">
            <Spinner className="h-6 w-6" />
          </div>
        )}
        {user && (
          <div className="mt-6 space-y-6">
            <div className="grid grid-cols-2 gap-4 text-sm">
              <div>
                <p className="text-xs text-muted-foreground">Email</p>
                <p className="font-medium text-foreground">{user.email}</p>
              </div>
              <div>
                <p className="text-xs text-muted-foreground">Role</p>
                <p className="font-medium text-foreground">{user.roleName ?? "—"}</p>
              </div>
            </div>
            {serverError && <p className="text-sm text-destructive">{serverError}</p>}
            {canUpdate && (
              <Button type="button" variant={user.isActive ? "destructive" : "default"} disabled={setActive.isPending} onClick={handleToggleActive}>
                {setActive.isPending ? <Spinner className="h-4 w-4" /> : user.isActive ? "Deactivate" : "Reactivate"}
              </Button>
            )}
            <UserBranchGrants userId={user.id} />
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 5: Wire the Users tab into the `/admin` page shell**

In `src/web/src/app/(staff)/admin/page.tsx`, add the imports (`UsersTab`, `UserInviteSheet`, `UserDetailSheet`), append `{ key: "users", label: "Users" } as const` to the `TABS` array (removing the now-outdated single-entry comment about Task 17), add `{activeTab === "users" && <UsersTab />}` immediately after the `roles` branch, and add `<UserInviteSheet />` and `<UserDetailSheet />` immediately after `<RoleDetailSheet />`.

- [ ] **Step 6: Widen the nav entry to `role.manage` OR `user.read`**

`usePermission` checks exactly one permission string. In `src/web/src/components/nav-main.tsx`, change `NavItem.permission` from `string` to `string | string[]`, and in `NavMainItem`, replace the single `usePermission(item.permission)` call with logic that treats an array as "any": since hooks can't be called conditionally or in a loop, call `usePermission` for each of up to the two known permissions this app ever needs simultaneously and OR them for the `/admin` entry specifically — the simplest correct fix given only one nav item needs this:

```typescript
function NavMainItem({ item, active, onNavigate }: { item: NavItem; active: boolean; onNavigate?: () => void }) {
  const permissions = Array.isArray(item.permission) ? item.permission : [item.permission];
  const hasFirst = usePermission(permissions[0]);
  const hasSecond = usePermission(permissions[1] ?? permissions[0]);
  const allowed = hasFirst || hasSecond;
  if (!allowed) {
    return null;
  }
  const Icon = item.icon;
  return (
```

(This calls `usePermission` twice unconditionally for every nav item — a no-op second check against the same permission for every single-permission item, which is cheap and keeps the Hooks called-unconditionally rule intact; only the `/admin` entry actually uses two distinct values.) Update the `/admin` entry added in Task 16 to:

```typescript
  { href: "/admin", label: "Admin", permission: ["role.manage", "user.read"], icon: ShieldCheck },
```

- [ ] **Step 7: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS — no existing tests broken, no type errors.

- [ ] **Step 8: Commit**

```bash
git add src/web/src/features/admin/components/UsersTab.tsx src/web/src/features/admin/components/UserInviteSheet.tsx src/web/src/features/admin/components/UserDetailSheet.tsx src/web/src/features/admin/components/UserBranchGrants.tsx "src/web/src/app/(staff)/admin/page.tsx" src/web/src/components/nav-main.tsx
git commit -m "feat(admin-frontend): add the Users tab (invite, deactivate/reactivate, branch grants)"
```

---

## Task 18: The public `/accept-invite` page

**Files:**
- Modify: `src/web/src/services/authService.ts`
- Create: `src/web/src/app/api/auth/accept-invite/route.ts`
- Modify: `src/web/src/features/auth/schema.ts`
- Create: `src/web/src/features/auth/AcceptInviteForm.tsx`
- Create: `src/web/src/features/auth/AcceptInviteForm.test.tsx`
- Create: `src/web/src/app/accept-invite/page.tsx`

**Interfaces:**
- Consumes: `setSessionCookies` (existing, `@/lib/auth/session`), `getUpstreamApiUrl` (existing, `@/lib/env`).
- Produces: `authService.acceptInvite(token, password)`, `acceptInviteSchema`/`AcceptInviteFormValues`, `AcceptInviteForm`. The unauthenticated route `/accept-invite?token=...`, outside the `(staff)` layout — the one deliberate exception to this phase's Sheet-over-pages convention, matching how `/login` already works.

- [ ] **Step 1: Add `acceptInvite` to `authService`**

In `src/web/src/services/authService.ts`, add to the `authService` object, after `login`:

```typescript
  async acceptInvite(token: string, password: string): Promise<AuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/auth/accept-invite`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ token, password }),
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("ACCEPT_INVITE_FAILED");
    }
    const data = await response.json();
    return {
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      userId: data.userId,
      organizationId: data.organizationId,
    };
  },
```

- [ ] **Step 2: Write the BFF route**

`src/web/src/app/api/auth/accept-invite/route.ts` (mirrors `app/api/auth/login/route.ts` exactly — unauthenticated, sets the session cookie on success so accepting an invite lands the user signed in, matching `AcceptInviteAsync`'s backend contract of returning a full `AuthResult`):

```typescript
import { NextRequest, NextResponse } from "next/server";
import { authService } from "@/services/authService";
import { setSessionCookies } from "@/lib/auth/session";

export async function POST(request: NextRequest) {
  const { token, password } = await request.json();
  try {
    const tokens = await authService.acceptInvite(token, password);
    setSessionCookies(tokens.accessToken, tokens.refreshToken);
    return NextResponse.json({ ok: true });
  } catch {
    return NextResponse.json({ error: "This invite link is invalid or has expired." }, { status: 400 });
  }
}
```

- [ ] **Step 3: Add the form schema**

In `src/web/src/features/auth/schema.ts`, add:

```typescript
export const acceptInviteSchema = z
  .object({
    password: z.string().min(8, "Password must be at least 8 characters."),
    confirmPassword: z.string().min(1, "Confirm your password."),
  })
  .refine((values) => values.password === values.confirmPassword, {
    message: "Passwords don't match.",
    path: ["confirmPassword"],
  });

export type AcceptInviteFormValues = z.infer<typeof acceptInviteSchema>;
```

- [ ] **Step 4: Write `AcceptInviteForm.tsx`**

`src/web/src/features/auth/AcceptInviteForm.tsx`:

```tsx
"use client";

import { useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { AlertCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { acceptInviteSchema, type AcceptInviteFormValues } from "@/features/auth/schema";

export function AcceptInviteForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const token = searchParams.get("token");
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<AcceptInviteFormValues>({ resolver: zodResolver(acceptInviteSchema) });

  async function onSubmit(values: AcceptInviteFormValues) {
    setServerError(null);
    const response = await fetch("/api/auth/accept-invite", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ token, password: values.password }),
    });
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      setServerError(body.error ?? "Something went wrong.");
      return;
    }
    router.push("/dashboard");
  }

  if (!token) {
    return (
      <Card className="w-full max-w-sm shadow-xl shadow-primary/5">
        <CardContent className="pt-6">
          <p className="flex items-center gap-1.5 text-sm text-destructive">
            <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
            This invite link is missing its token. Check the link in your email and try again.
          </p>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card className="w-full max-w-sm shadow-xl shadow-primary/5">
      <CardHeader>
        <CardTitle className="font-display text-xl">Set your password</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="password">Password</Label>
            <Input id="password" type="password" autoComplete="new-password" {...register("password")} />
            {errors.password && (
              <p className="flex items-center gap-1.5 text-sm text-destructive">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.password.message}
              </p>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="confirmPassword">Confirm Password</Label>
            <Input id="confirmPassword" type="password" autoComplete="new-password" {...register("confirmPassword")} />
            {errors.confirmPassword && (
              <p className="flex items-center gap-1.5 text-sm text-destructive">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.confirmPassword.message}
              </p>
            )}
          </div>
          {serverError && (
            <p className="flex items-center gap-1.5 text-sm text-destructive">
              <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
              {serverError}
            </p>
          )}
          <Button type="submit" className="w-full" disabled={isSubmitting}>
            {isSubmitting ? "Setting password..." : "Set Password & Sign In"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 5: Write `AcceptInviteForm.test.tsx`**

`src/web/src/features/auth/AcceptInviteForm.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const pushMock = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
  useSearchParams: () => new URLSearchParams("token=abc123"),
}));

import { AcceptInviteForm } from "./AcceptInviteForm";

describe("AcceptInviteForm", () => {
  it("submits the token and password, then redirects to /dashboard on success", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ ok: true }) });
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    render(<AcceptInviteForm />);
    await user.type(screen.getByLabelText("Password"), "N3wP@ssword!");
    await user.type(screen.getByLabelText("Confirm Password"), "N3wP@ssword!");
    await user.click(screen.getByRole("button", { name: /set password/i }));

    await waitFor(() => expect(pushMock).toHaveBeenCalledWith("/dashboard"));
    expect(fetchMock).toHaveBeenCalledWith("/api/auth/accept-invite", expect.objectContaining({
      body: JSON.stringify({ token: "abc123", password: "N3wP@ssword!" }),
    }));
  });

  it("shows a validation error when passwords don't match", async () => {
    const user = userEvent.setup();
    render(<AcceptInviteForm />);

    await user.type(screen.getByLabelText("Password"), "N3wP@ssword!");
    await user.type(screen.getByLabelText("Confirm Password"), "Different1!");
    await user.click(screen.getByRole("button", { name: /set password/i }));

    expect(await screen.findByText("Passwords don't match.")).toBeInTheDocument();
  });
});
```

- [ ] **Step 6: Write the page**

`src/web/src/app/accept-invite/page.tsx`:

```tsx
import { Suspense } from "react";
import { Package } from "lucide-react";
import { AcceptInviteForm } from "@/features/auth/AcceptInviteForm";

export default function AcceptInvitePage() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 bg-background p-4">
      <div className="flex flex-col items-center gap-2">
        <span className="flex h-11 w-11 items-center justify-center rounded-lg bg-primary">
          <Package className="h-5 w-5 text-primary-foreground" strokeWidth={2} />
        </span>
        <span className="font-display text-lg font-semibold tracking-tight">Courier Platform</span>
      </div>
      <Suspense>
        <AcceptInviteForm />
      </Suspense>
    </main>
  );
}
```

(`useSearchParams` requires a `Suspense` boundary in the App Router — without it, `npm run build` fails with a static-generation error on this route.)

- [ ] **Step 7: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS — no existing tests broken, no type errors, `/accept-invite` builds successfully.

- [ ] **Step 8: Commit**

```bash
git add src/web/src/services/authService.ts src/web/src/app/api/auth/accept-invite src/web/src/features/auth/schema.ts src/web/src/features/auth/AcceptInviteForm.tsx src/web/src/features/auth/AcceptInviteForm.test.tsx src/web/src/app/accept-invite
git commit -m "feat(admin-frontend): add the public /accept-invite page"
```

---

## Final Step: Whole-Branch Review

After all 18 tasks are complete and committed, per this project's established pattern (every prior frontend phase's plan ends this way):

1. Run the full backend suite (`dotnet test`) and the full frontend suite (`cd src/web && npm test && npm run build`) one more time from a clean state.
2. Dispatch a whole-branch review (not scoped to any single task's diff) checking specifically for:
   - Tenant/branch isolation on every new endpoint (this project's most-repeated bug class).
   - Any backend enum this phase touches still missing `JsonStringEnumConverter`.
   - The `UserInvite`/`InviteUserAsync`/`AcceptInviteAsync` chain for any window where a half-created invite (e.g. `SaveChangesAsync` succeeds but the outbox write logic was skipped) could leave a `User` row with no way to ever set a real password.
   - Whether `RoleService`/`UserManagementService`'s audit entries (Global Constraints) actually landed on every mutating path, not just the ones a fix wave remembered.
3. Apply a single fix wave for anything the review finds, then a scoped re-review of just the fixed files.
4. Manual E2E verification against the real API/Postgres (this project's standing practice — every prior phase's own pass found real bugs invisible to unit tests alone): register a fresh org, create a custom role with a chosen permission subset via the UI, invite a user by email, retrieve the invite email from the dev `mailhog` container (Phase 11's existing SMTP sandbox) or read the `OutboxMessage` row directly, accept the invite and confirm login works with the new password, grant that user a second branch with a short expiry and confirm access to that branch's data disappears once it passes, deactivate the user and confirm their refresh token stops working.
5. Update `docs/FRONTEND-ROADMAP.md`'s Phase F9 entry with completion notes (status, what shipped, any real bugs found and fixed during E2E — matching every prior phase's own completion-note format).
6. Merge per `superpowers:finishing-a-development-branch`.
