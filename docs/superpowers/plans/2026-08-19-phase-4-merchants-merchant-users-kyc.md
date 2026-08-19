# Phase 4 — Merchants, Merchant Users, KYC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `docs/ROADMAP.md` Phase 4 — a `Merchant` account can be onboarded by org
staff (creating the merchant, its owner `MerchantUser`, and a default pickup address in one
call), the owner can log in and invite `MerchantStaff`, both roles hold correctly scoped
permissions, and KYC documents can be uploaded by the merchant and reviewed by org staff.

**Architecture:** A new `Merchants` module (`src/Modules/Merchants/Merchants.csproj`) owns
`Merchant`, `MerchantUser`, `MerchantAddress`, `MerchantKycDocument`, `MerchantRefreshToken`.
The dependency runs `Merchants → Geography` only (for `MerchantAddress`'s FKs and validating a
new merchant's address chain via `IGeographyLookupService`, exactly like `Branches`'ades
`BranchService` does) — `Merchants` has **no** project reference to `Identity`, and `Identity`
has none to `Merchants`. Merchant authorization is a fully separate track from staff RBAC
(`RBAC.md` §5: "Merchants are not staff users"): `MerchantUser.Role` is a 2-value enum
(`Owner`/`Staff`), checked against a small hardcoded `MerchantPermissionCatalog` — not a reuse of
staff's `Role`/`Permission`/`UserRole` tables, since merchants cannot define custom roles in v1.
This means `Merchants` cannot reuse `Identity`'s `IJwtTokenService`/`IPasswordHasher`/`JwtOptions`
either (they live in `Identity.Application`, and referencing them would require the
cross-module reference this design deliberately avoids) — `Merchants` gets its own small
`IMerchantJwtTokenService`/`IMerchantPasswordHasher`/`MerchantJwtOptions`, each ~15-20 lines,
duplicating (not reusing) the equivalent Identity types. This is a deliberate trade: a little
duplication over a cross-module coupling that would break the module graph's intended shape.

Both token types are signed with the **same** `"Jwt"` config section (signing key/issuer/audience)
so a single `AddJwtBearer()` registration in `Program.cs` validates both structurally — they
differ only in which claims they carry. A merchant token carries `sub` (=`MerchantUser.Id`),
`org_id`, `merchant_id`, `email`. A staff token carries `sub` (=`User.Id`), `org_id`, `email` —
no `merchant_id`. This is what keeps the two tracks safely separated without extra plumbing:
`IPermissionChecker` (staff) resolves `sub` against the `Users` table and finds nothing for a
merchant token (safe deny); `IMerchantAuthorizationChecker` resolves `sub` against `MerchantUsers`
and finds nothing for a staff token (safe deny). Reusing the `org_id` claim name (not a new one)
means `ICurrentTenantProvider`/`AppDbContext`'s existing tenant query filters work unchanged for
merchant sessions too — no code in `Persistence` needs to know two kinds of token exist.

`MerchantAddress` and `MerchantKycDocument` don't have a natural org-scoped root themselves (they
hang off `Merchant`, not `Organization`) — rather than requiring every query to join through
`Merchant` first to enforce tenant isolation (an easy pattern to forget and silently leak across
tenants), they carry their own denormalized `OrganizationId` (set once from `Merchant.OrganizationId`
at creation, never updated after) with their own EF Core global query filter, mirroring
`CLARIFICATIONS.md` #7's identical reasoning for `ApiKey`'s denormalized `OrganizationId` — just
applied here without that entry's DB-trigger requirement, since nothing in this phase updates
`OrganizationId` after creation (the drift `CLARIFICATIONS.md` #7 guards against can't occur).

KYC document bytes go through a new `IObjectStorage` interface (`SharedKernel`, alongside
`ICurrentTenantProvider`/`IPermissionChecker` — the existing precedent for cross-module shared
interfaces) with a local-filesystem implementation. Production S3/Azure targets swap the DI
registration behind the same interface later (`ARCHITECTURE.md` §5) — not built this phase.

**Scope deliberately excluded from this phase** (see brainstorming discussion, approved
2026-08-19): `ApiKey` (no consumer until the Merchant API, `ROADMAP.md` Phase 12); a
`PUT /api/v1/merchants/{id}` update endpoint (the `merchant.update` permission already exists in
the catalog from Phase 1 but has no endpoint yet — same "permission exists ahead of its endpoint"
state `branch.update` has been in since Phase 2); `MerchantAddress` CRUD beyond the one address
created at onboarding; auto-aggregating `MerchantKycDocument.Status` into `Merchant.KycStatus`.

**Tech Stack:** Same as Phase 1-3 — EF Core 10 + Npgsql, xUnit + `Microsoft.EntityFrameworkCore.InMemory`
for unit tests, `Microsoft.AspNetCore.Mvc.Testing` for API-level integration tests against the
real Postgres container from `docker-compose.yml`.

**Spec:** `docs/ROADMAP.md` (Phase 4), `docs/DOMAIN-MODEL.md` (Merchants section),
`docs/RBAC.md` §5, `docs/CLARIFICATIONS.md` #7 (denormalized-`OrganizationId` precedent),
`docs/ARCHITECTURE.md` §2-3 §5, CLAUDE.md §15.

## Global Constraints

- `Merchants` module has no project reference to `Identity`, and `Identity` has none to
  `Merchants`. The only cross-module dependency is `Merchants → Geography`. If a future task
  seems to need the reverse, that is a signal the design needs to change, not a signal to add
  the reference.
- `MerchantUser.Role` is a 2-value enum (`Owner`/`Staff`) checked against the static
  `MerchantPermissionCatalog`, never the staff `Role`/`Permission`/`UserRole` tables. No
  `if (user.Role == "Admin")`-shaped checks anywhere — merchant endpoints use
  `RequireMerchantPermission("...")`, staff endpoints use the existing `RequirePermission("...")`.
- Merchant JWTs and staff JWTs share the same signing key/issuer/audience (`"Jwt"` config
  section) but are issued by separate services (`IMerchantJwtTokenService` vs `IJwtTokenService`)
  with different claim shapes. A merchant token always carries `merchant_id`; a staff token never
  does. Never make `IPermissionChecker` or `IMerchantAuthorizationChecker` accept the other
  token's shape — each fails closed (returns false/denies) on a token it wasn't built for.
- `Merchant`/`MerchantUser`/`MerchantAddress`/`MerchantKycDocument` all carry `OrganizationId`
  and get the identical fail-closed `HasQueryFilter(x => _currentTenantProvider.OrganizationId
  != null && x.OrganizationId == _currentTenantProvider.OrganizationId)` pattern established in
  Phase 1-3. `MerchantAddress`/`MerchantKycDocument`'s `OrganizationId` is denormalized from
  their parent `Merchant` at creation time — set once, never updated, per `CLARIFICATIONS.md` #7's
  precedent. No tenant filter on `MerchantRefreshToken` (pre-auth lookups, mirrors `RefreshToken`).
- `MerchantOnboardingService.CreateMerchantAsync` must reject any
  `ProvinceId/DistrictId/LocalLevelId/WardNumber` combination that doesn't nest correctly,
  validated server-side via `IGeographyLookupService.IsValidHierarchyAsync` — same rule Phase 3
  established for `Branch`, never trusted from the client (CLAUDE.md §33).
- `MerchantUser.Email` is globally unique across the whole platform (not just per-merchant),
  mirroring `User.Email` — login resolves a `MerchantUser` by email alone using
  `.IgnoreQueryFilters()` pre-auth, exactly like `AuthService.LoginAsync`/`RegisterOrganizationAsync`.
- Commit after every task using conventional prefixes (`feat:`, `chore:`, `refactor:`, `test:`).
- Every integration test in this plan that touches `AppDbContext` requires the shared
  `courier-postgres-1` container (`docker exec courier-postgres-1 pg_isready -U courier`) — do
  **not** `docker compose up` a second Postgres; reuse the existing one on port 5434.

---

### Task 1: Scaffold Merchants Module and Wire Project References

**Files:**
- Create: `src/Modules/Merchants/Merchants.csproj`
- Create: `tests/Merchants.Tests/Merchants.Tests.csproj`
- Modify: `Courier.slnx`
- Modify: `src/Persistence/Persistence.csproj` (add reference to Merchants)
- Modify: `src/Api/Api.csproj` (add reference to Merchants)

**Interfaces:**
- Consumes: nothing (first task).
- Produces: an empty-but-building `Merchants` project wired into the solution with the reference
  graph `SharedKernel, Audit, Geography ← Merchants`, `..., Merchants ← Persistence`,
  `..., Merchants ← Api`. No cycles — `Merchants` does not reference `Identity`; `Identity` does
  not reference `Merchants`. `Api.Tests` gets transitive access to `Merchants` types through its
  existing `Api.csproj` reference, so it needs no direct reference added. Later tasks add real
  code.

- [ ] **Step 1: Create the two projects**

```bash
dotnet new classlib -n Merchants -o src/Modules/Merchants
dotnet new xunit -n Merchants.Tests -o tests/Merchants.Tests
dotnet sln Courier.slnx add src/Modules/Merchants/Merchants.csproj tests/Merchants.Tests/Merchants.Tests.csproj
```

Delete the template `src/Modules/Merchants/Class1.cs` and `tests/Merchants.Tests/UnitTest1.cs` —
both projects start empty.

- [ ] **Step 2: Add the ASP.NET Core framework reference to Merchants.csproj**

`Merchants.Presentation` (added in a later task) needs `IEndpointRouteBuilder`,
`RouteHandlerBuilder`, `HttpContext`. Add to `src/Modules/Merchants/Merchants.csproj`, inside a
sibling `<ItemGroup>`:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

- [ ] **Step 3: Add EF Core packages**

```bash
dotnet add src/Modules/Merchants/Merchants.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Modules/Merchants/Merchants.csproj package Microsoft.EntityFrameworkCore.Relational
dotnet add tests/Merchants.Tests/Merchants.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory
```

- [ ] **Step 4: Wire project references**

```bash
dotnet add src/Modules/Merchants/Merchants.csproj reference src/SharedKernel/SharedKernel.csproj
dotnet add src/Modules/Merchants/Merchants.csproj reference src/Modules/Audit/Audit.csproj
dotnet add src/Modules/Merchants/Merchants.csproj reference src/Modules/Geography/Geography.csproj
dotnet add src/Persistence/Persistence.csproj reference src/Modules/Merchants/Merchants.csproj
dotnet add src/Api/Api.csproj reference src/Modules/Merchants/Merchants.csproj
dotnet add tests/Merchants.Tests/Merchants.Tests.csproj reference src/Modules/Merchants/Merchants.csproj
```

- [ ] **Step 5: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (pre-existing NU1510/CS8602 warnings from
earlier phases are fine; no new ones).

- [ ] **Step 6: Commit**

```bash
git add Courier.slnx src/Modules/Merchants tests/Merchants.Tests src/Persistence/Persistence.csproj src/Api/Api.csproj
git commit -m "chore: scaffold Merchants module project and wire references"
```

---

### Task 2: SharedKernel — Object Storage and Merchant Authorization Abstractions

**Files:**
- Create: `src/SharedKernel/IObjectStorage.cs`
- Create: `src/SharedKernel/LocalFileSystemObjectStorage.cs`
- Create: `src/SharedKernel/IMerchantAuthorizationChecker.cs`
- Create: `src/SharedKernel/RequireMerchantPermissionFilter.cs`
- Modify: `src/SharedKernel/ClaimsPrincipalExtensions.cs`
- Test: `tests/SharedKernel.Tests/LocalFileSystemObjectStorageTests.cs`
- Test: `tests/SharedKernel.Tests/RequireMerchantPermissionFilterTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `IObjectStorage.{UploadAsync,OpenReadAsync}` — Task 7's `MerchantKycService`
  consumes this. `IMerchantAuthorizationChecker.HasPermissionAsync` — Task 4's
  `MerchantAuthorizationChecker` implements it, Task 8's endpoints consume it via
  `RequireMerchantPermission`. `ClaimsPrincipalExtensions.GetMerchantId(this ClaimsPrincipal)` —
  Tasks 6-8 consume it.

- [ ] **Step 1: Write the object storage interface and local filesystem implementation**

`src/SharedKernel/IObjectStorage.cs`:

```csharp
namespace Courier.SharedKernel;

public interface IObjectStorage
{
    // Stores content under a key derived from keyPrefix (implementations append a unique
    // suffix), returning the final storage key callers must persist to retrieve it later.
    Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);
}
```

`src/SharedKernel/LocalFileSystemObjectStorage.cs`:

```csharp
namespace Courier.SharedKernel;

// Dev-appropriate IObjectStorage backed by the local filesystem. Production targets S3/Azure
// Blob behind this same interface (ARCHITECTURE.md §5) — swapping the DI registration in
// Program.cs is the only change a real implementation needs.
public class LocalFileSystemObjectStorage : IObjectStorage
{
    private readonly string _rootPath;

    public LocalFileSystemObjectStorage(string rootPath)
    {
        _rootPath = rootPath;
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var key = $"{keyPrefix}/{Guid.NewGuid():N}";
        var path = Path.Combine(_rootPath, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var fileStream = File.Create(path);
        await content.CopyToAsync(fileStream, cancellationToken);

        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_rootPath, key.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No object found for key '{key}'.", path);
        }
        return Task.FromResult<Stream>(File.OpenRead(path));
    }
}
```

- [ ] **Step 2: Write the failing test for LocalFileSystemObjectStorage**

`tests/SharedKernel.Tests/LocalFileSystemObjectStorageTests.cs`:

```csharp
using Courier.SharedKernel;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class LocalFileSystemObjectStorageTests
{
    private static LocalFileSystemObjectStorage CreateStorage(out string rootPath)
    {
        rootPath = Path.Combine(Path.GetTempPath(), "courier-storage-tests", Guid.NewGuid().ToString("N"));
        return new LocalFileSystemObjectStorage(rootPath);
    }

    [Fact]
    public async Task UploadAsync_ThenOpenReadAsync_RoundTripsContent()
    {
        var storage = CreateStorage(out var rootPath);
        try
        {
            var originalBytes = "hello kyc document"u8.ToArray();
            using var uploadStream = new MemoryStream(originalBytes);

            var key = await storage.UploadAsync("kyc/merchant-1", uploadStream, "text/plain");

            Assert.StartsWith("kyc/merchant-1/", key);

            await using var readStream = await storage.OpenReadAsync(key);
            using var reader = new MemoryStream();
            await readStream.CopyToAsync(reader);

            Assert.Equal(originalBytes, reader.ToArray());
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UploadAsync_TwiceWithSamePrefix_ProducesDistinctKeys()
    {
        var storage = CreateStorage(out var rootPath);
        try
        {
            using var stream1 = new MemoryStream("a"u8.ToArray());
            using var stream2 = new MemoryStream("b"u8.ToArray());

            var key1 = await storage.UploadAsync("kyc/merchant-1", stream1, "text/plain");
            var key2 = await storage.UploadAsync("kyc/merchant-1", stream2, "text/plain");

            Assert.NotEqual(key1, key2);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OpenReadAsync_UnknownKey_ThrowsFileNotFoundException()
    {
        var storage = CreateStorage(out var rootPath);
        try
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() => storage.OpenReadAsync("kyc/merchant-1/does-not-exist"));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they pass**

Run: `dotnet test tests/SharedKernel.Tests/SharedKernel.Tests.csproj --filter LocalFileSystemObjectStorageTests`
Expected: 3 tests pass.

- [ ] **Step 4: Write the merchant authorization abstractions**

`src/SharedKernel/IMerchantAuthorizationChecker.cs`:

```csharp
using System.Security.Claims;

namespace Courier.SharedKernel;

public interface IMerchantAuthorizationChecker
{
    Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default);
}
```

`src/SharedKernel/RequireMerchantPermissionFilter.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Courier.SharedKernel;

public class RequireMerchantPermissionFilter : IEndpointFilter
{
    private readonly string _permission;

    public RequireMerchantPermissionFilter(string permission)
    {
        _permission = permission;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var checker = httpContext.RequestServices.GetRequiredService<IMerchantAuthorizationChecker>();

        var hasPermission = await checker.HasPermissionAsync(httpContext.User, _permission, httpContext.RequestAborted);
        if (!hasPermission)
        {
            return Results.Forbid();
        }

        return await next(context);
    }
}

public static class MerchantPermissionEndpointExtensions
{
    public static RouteHandlerBuilder RequireMerchantPermission(this RouteHandlerBuilder builder, string permission)
    {
        return builder
            .RequireAuthorization()
            .AddEndpointFilter(new RequireMerchantPermissionFilter(permission));
    }
}
```

- [ ] **Step 5: Add GetMerchantId to ClaimsPrincipalExtensions**

Replace the full contents of `src/SharedKernel/ClaimsPrincipalExtensions.cs`:

```csharp
using System.Security.Claims;

namespace Courier.SharedKernel;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirst("sub")?.Value, out var id) ? id : null;

    public static Guid? GetMerchantId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirst("merchant_id")?.Value, out var id) ? id : null;
}
```

- [ ] **Step 6: Write the failing test for RequireMerchantPermissionFilter**

Read `tests/SharedKernel.Tests/RequirePermissionFilterTests.cs` first — mirror its exact
structure (fake `IMerchantAuthorizationChecker` instead of `IPermissionChecker`, same
allow/deny assertions against `InvokeAsync`).

`tests/SharedKernel.Tests/RequireMerchantPermissionFilterTests.cs` — this mirrors
`tests/SharedKernel.Tests/RequirePermissionFilterTests.cs` exactly (same
`DefaultEndpointFilterInvocationContext(httpContext)` construction, same
`IStatusCodeHttpResult`/`ForbidHttpResult` assertions from `Microsoft.AspNetCore.Http.HttpResults`),
substituting `IMerchantAuthorizationChecker`/`RequireMerchantPermissionFilter` for their staff
equivalents:

```csharp
using System.Security.Claims;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.SharedKernel.Tests;

public class RequireMerchantPermissionFilterTests
{
    private sealed class FakeMerchantAuthorizationChecker : IMerchantAuthorizationChecker
    {
        private readonly bool _result;
        public FakeMerchantAuthorizationChecker(bool result) => _result = result;

        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private static EndpointFilterInvocationContext BuildContext(bool authenticated, IMerchantAuthorizationChecker checker)
    {
        var services = new ServiceCollection();
        services.AddSingleton(checker);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (authenticated)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "TestAuth"));
        }
        return new DefaultEndpointFilterInvocationContext(httpContext);
    }

    [Fact]
    public async Task InvokeAsync_MerchantUserHasPermission_CallsNext()
    {
        var filter = new RequireMerchantPermissionFilter("merchant.profile.read");
        var context = BuildContext(authenticated: true, new FakeMerchantAuthorizationChecker(true));
        var nextCalled = false;

        var result = await filter.InvokeAsync(context, _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        Assert.True(nextCalled);
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
    }

    [Fact]
    public async Task InvokeAsync_MerchantUserLacksPermission_ReturnsForbid()
    {
        var filter = new RequireMerchantPermissionFilter("merchant.staff.manage");
        var context = BuildContext(authenticated: true, new FakeMerchantAuthorizationChecker(false));
        var nextCalled = false;

        var result = await filter.InvokeAsync(context, _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); });

        Assert.False(nextCalled);
        Assert.IsType<ForbidHttpResult>(result);
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/SharedKernel.Tests/SharedKernel.Tests.csproj`
Expected: all tests pass (existing `RequirePermissionFilterTests`/`RequireBranchScopedPermissionFilterTests` plus the new ones).

- [ ] **Step 8: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (no new warnings).

- [ ] **Step 9: Commit**

```bash
git add src/SharedKernel tests/SharedKernel.Tests
git commit -m "feat: add object storage and merchant authorization abstractions to SharedKernel"
```

---

### Task 3: Merchants Domain, EF Configuration, and Permission Catalog

**Files:**
- Create: `src/Modules/Merchants/Domain/MerchantUserRole.cs`
- Create: `src/Modules/Merchants/Domain/KycStatus.cs`
- Create: `src/Modules/Merchants/Domain/KycDocumentType.cs`
- Create: `src/Modules/Merchants/Domain/Merchant.cs`
- Create: `src/Modules/Merchants/Domain/MerchantUser.cs`
- Create: `src/Modules/Merchants/Domain/MerchantAddress.cs`
- Create: `src/Modules/Merchants/Domain/MerchantKycDocument.cs`
- Create: `src/Modules/Merchants/Domain/MerchantRefreshToken.cs`
- Create: `src/Modules/Merchants/Infrastructure/MerchantConfiguration.cs`
- Create: `src/Modules/Merchants/Infrastructure/MerchantUserConfiguration.cs`
- Create: `src/Modules/Merchants/Infrastructure/MerchantAddressConfiguration.cs`
- Create: `src/Modules/Merchants/Infrastructure/MerchantKycDocumentConfiguration.cs`
- Create: `src/Modules/Merchants/Infrastructure/MerchantRefreshTokenConfiguration.cs`
- Create: `src/Modules/Merchants/Application/MerchantPermissionCatalog.cs`
- Test: `tests/Merchants.Tests/MerchantPermissionCatalogTests.cs`

**Interfaces:**
- Consumes: `Province/District/LocalLevel` from `Geography.Domain` (Phase 3, already merged).
- Produces: all 5 domain entities + their EF configurations; `MerchantPermissionCatalog.{ProfileRead,
  ProfileUpdate,StaffManage,KycUpload,RolePermissions}` — Task 4's `MerchantAuthorizationChecker`
  and Task 8's endpoints both consume `RolePermissions`/the permission-key constants.

- [ ] **Step 1: Write the domain entities**

`src/Modules/Merchants/Domain/MerchantUserRole.cs`:

```csharp
namespace Courier.Merchants.Domain;

public enum MerchantUserRole
{
    Owner,
    Staff
}
```

`src/Modules/Merchants/Domain/KycStatus.cs`:

```csharp
namespace Courier.Merchants.Domain;

public enum KycStatus
{
    Pending,
    Approved,
    Rejected
}
```

`src/Modules/Merchants/Domain/KycDocumentType.cs`:

```csharp
namespace Courier.Merchants.Domain;

public enum KycDocumentType
{
    CitizenshipDocument,
    PanCard,
    BusinessRegistrationCertificate,
    BankAccountProof,
    Other
}
```

`src/Modules/Merchants/Domain/Merchant.cs`:

```csharp
namespace Courier.Merchants.Domain;

public class Merchant
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string LegalName { get; set; }
    public required string DisplayName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public KycStatus KycStatus { get; set; } = KycStatus.Pending;
    public bool IsActive { get; set; } = true;
}
```

`src/Modules/Merchants/Domain/MerchantUser.cs`:

```csharp
namespace Courier.Merchants.Domain;

public class MerchantUser
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string FullName { get; set; }
    public MerchantUserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
}
```

`src/Modules/Merchants/Domain/MerchantAddress.cs`:

```csharp
namespace Courier.Merchants.Domain;

public class MerchantAddress
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }

    // Denormalized from Merchant.OrganizationId at creation time, never updated after — lets
    // this entity carry its own tenant query filter directly instead of requiring every query
    // to join through Merchant first. Mirrors CLARIFICATIONS.md #7's identical reasoning for
    // ApiKey's denormalized OrganizationId.
    public Guid OrganizationId { get; set; }

    public required string Label { get; set; }
    public required Guid ProvinceId { get; set; }
    public required Guid DistrictId { get; set; }
    public required Guid LocalLevelId { get; set; }
    public required int WardNumber { get; set; }
    public required string AddressLine { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool IsDefaultPickup { get; set; }
}
```

`src/Modules/Merchants/Domain/MerchantKycDocument.cs`:

```csharp
namespace Courier.Merchants.Domain;

public class MerchantKycDocument
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }

    // Denormalized from Merchant.OrganizationId — same reasoning as MerchantAddress.OrganizationId.
    public Guid OrganizationId { get; set; }

    public KycDocumentType DocumentType { get; set; }
    public required string StorageKey { get; set; }
    public KycStatus Status { get; set; } = KycStatus.Pending;

    // No FK to a staff User: Merchants has no project reference to Identity. Resolved
    // server-side from the reviewing staff member's ClaimsPrincipal at review time, same
    // reasoning as Branch/DeliveryZone's acting-user ids.
    public Guid? ReviewedByUserId { get; set; }
}
```

`src/Modules/Merchants/Domain/MerchantRefreshToken.cs`:

```csharp
namespace Courier.Merchants.Domain;

public class MerchantRefreshToken
{
    public Guid Id { get; set; }
    public Guid MerchantUserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
}
```

- [ ] **Step 2: Write the EF Core configurations**

`src/Modules/Merchants/Infrastructure/MerchantConfiguration.cs`:

```csharp
using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Merchants.Infrastructure;

public class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.ToTable("Merchants");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(m => m.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(m => m.ContactPhone).HasMaxLength(30);
        builder.Property(m => m.ContactEmail).HasMaxLength(200);

        // Every tenant-filtered query hits this column.
        builder.HasIndex(m => m.OrganizationId);

        // No FK to Organization here: Merchants has no project reference to Identity. That FK
        // is configured centrally in Persistence/AppDbContext.OnModelCreating instead, mirroring
        // Branch.OrganizationId/DeliveryZone.OrganizationId's identical reasoning.
    }
}
```

`src/Modules/Merchants/Infrastructure/MerchantUserConfiguration.cs`:

```csharp
using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Merchants.Infrastructure;

public class MerchantUserConfiguration : IEntityTypeConfiguration<MerchantUser>
{
    public void Configure(EntityTypeBuilder<MerchantUser> builder)
    {
        builder.ToTable("MerchantUsers");
        builder.HasKey(mu => mu.Id);
        builder.Property(mu => mu.Email).HasMaxLength(200).IsRequired();
        builder.Property(mu => mu.PasswordHash).IsRequired();
        builder.Property(mu => mu.FullName).HasMaxLength(200).IsRequired();

        // Globally unique across the platform (not just per-merchant), mirroring User.Email —
        // login resolves a MerchantUser by email alone, same pattern as staff login.
        builder.HasIndex(mu => mu.Email).IsUnique();

        builder.HasIndex(mu => mu.MerchantId);
        builder.HasIndex(mu => mu.OrganizationId);

        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(mu => mu.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

`src/Modules/Merchants/Infrastructure/MerchantAddressConfiguration.cs`:

```csharp
using Courier.Geography.Domain;
using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Merchants.Infrastructure;

public class MerchantAddressConfiguration : IEntityTypeConfiguration<MerchantAddress>
{
    public void Configure(EntityTypeBuilder<MerchantAddress> builder)
    {
        builder.ToTable("MerchantAddresses");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Label).HasMaxLength(100).IsRequired();
        builder.Property(a => a.AddressLine).HasMaxLength(500).IsRequired();

        builder.HasIndex(a => a.OrganizationId);
        builder.HasIndex(a => a.MerchantId);
        builder.HasIndex(a => a.ProvinceId);
        builder.HasIndex(a => a.DistrictId);
        builder.HasIndex(a => a.LocalLevelId);

        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(a => a.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Geography FKs: Merchants has a project reference to Geography (Task 1), so these are
        // configured directly here, mirroring Branches' BranchConfiguration.
        builder.HasOne<Province>()
            .WithMany()
            .HasForeignKey(a => a.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<District>()
            .WithMany()
            .HasForeignKey(a => a.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalLevel>()
            .WithMany()
            .HasForeignKey(a => a.LocalLevelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

`src/Modules/Merchants/Infrastructure/MerchantKycDocumentConfiguration.cs`:

```csharp
using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Merchants.Infrastructure;

public class MerchantKycDocumentConfiguration : IEntityTypeConfiguration<MerchantKycDocument>
{
    public void Configure(EntityTypeBuilder<MerchantKycDocument> builder)
    {
        builder.ToTable("MerchantKycDocuments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.StorageKey).HasMaxLength(500).IsRequired();

        builder.HasIndex(d => d.OrganizationId);
        builder.HasIndex(d => d.MerchantId);

        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(d => d.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

`src/Modules/Merchants/Infrastructure/MerchantRefreshTokenConfiguration.cs`:

```csharp
using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Courier.Merchants.Infrastructure;

public class MerchantRefreshTokenConfiguration : IEntityTypeConfiguration<MerchantRefreshToken>
{
    public void Configure(EntityTypeBuilder<MerchantRefreshToken> builder)
    {
        builder.ToTable("MerchantRefreshTokens");
        builder.HasKey(rt => rt.Id);
        builder.Property(rt => rt.TokenHash).IsRequired();
        builder.HasIndex(rt => rt.TokenHash).IsUnique();

        // Tokens follow the merchant user: deleting a MerchantUser should take their refresh
        // tokens with it rather than leaving credentials that resolve to a missing account.
        // Mirrors RefreshTokenConfiguration's identical Cascade choice for User/RefreshToken.
        builder.HasOne<MerchantUser>()
            .WithMany()
            .HasForeignKey(rt => rt.MerchantUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 3: Write the merchant permission catalog**

`src/Modules/Merchants/Application/MerchantPermissionCatalog.cs`:

```csharp
using Courier.Merchants.Domain;

namespace Courier.Merchants.Application;

// Merchant-facing permissions are a small, fixed catalog — unlike staff Roles, merchants cannot
// define custom roles in v1 (RBAC.md §5). Owner holds every merchant permission; Staff holds a
// fixed subset with no staff-management visibility.
public static class MerchantPermissionCatalog
{
    public const string ProfileRead = "merchant.profile.read";
    public const string ProfileUpdate = "merchant.profile.update";
    public const string StaffManage = "merchant.staff.manage";
    public const string KycUpload = "merchant.kyc.upload";

    private static readonly string[] OwnerPermissions =
    {
        ProfileRead, ProfileUpdate, StaffManage, KycUpload
    };

    private static readonly string[] StaffPermissions =
    {
        ProfileRead, KycUpload
    };

    public static readonly IReadOnlyDictionary<MerchantUserRole, IReadOnlySet<string>> RolePermissions =
        new Dictionary<MerchantUserRole, IReadOnlySet<string>>
        {
            [MerchantUserRole.Owner] = OwnerPermissions.ToHashSet(),
            [MerchantUserRole.Staff] = StaffPermissions.ToHashSet()
        };
}
```

- [ ] **Step 4: Write the failing test**

`tests/Merchants.Tests/MerchantPermissionCatalogTests.cs`:

```csharp
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Xunit;

namespace Courier.Merchants.Tests;

public class MerchantPermissionCatalogTests
{
    [Fact]
    public void Owner_HoldsAllFourPermissions()
    {
        var ownerPermissions = MerchantPermissionCatalog.RolePermissions[MerchantUserRole.Owner];

        Assert.Equal(4, ownerPermissions.Count);
        Assert.Contains(MerchantPermissionCatalog.ProfileRead, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.ProfileUpdate, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.StaffManage, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.KycUpload, ownerPermissions);
    }

    [Fact]
    public void Staff_LacksStaffManageAndProfileUpdate()
    {
        var staffPermissions = MerchantPermissionCatalog.RolePermissions[MerchantUserRole.Staff];

        Assert.Contains(MerchantPermissionCatalog.ProfileRead, staffPermissions);
        Assert.Contains(MerchantPermissionCatalog.KycUpload, staffPermissions);
        Assert.DoesNotContain(MerchantPermissionCatalog.StaffManage, staffPermissions);
        Assert.DoesNotContain(MerchantPermissionCatalog.ProfileUpdate, staffPermissions);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj`
Expected: 2 tests pass.

- [ ] **Step 6: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (no new warnings).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Merchants/Domain src/Modules/Merchants/Infrastructure src/Modules/Merchants/Application/MerchantPermissionCatalog.cs tests/Merchants.Tests/MerchantPermissionCatalogTests.cs
git commit -m "feat: add Merchants domain entities, EF configurations, and permission catalog"
```

---

### Task 4: Merchants Infrastructure — Authorization Checker, Password Hasher, JWT Issuance

**Files:**
- Create: `src/Modules/Merchants/Application/IMerchantPasswordHasher.cs`
- Create: `src/Modules/Merchants/Application/MerchantJwtOptions.cs`
- Create: `src/Modules/Merchants/Application/IMerchantJwtTokenService.cs`
- Create: `src/Modules/Merchants/Infrastructure/MerchantAuthorizationChecker.cs`
- Create: `src/Modules/Merchants/Infrastructure/MerchantPasswordHasherAdapter.cs`
- Create: `src/Modules/Merchants/Infrastructure/MerchantJwtTokenService.cs`
- Test: `tests/Merchants.Tests/MerchantAuthorizationCheckerTests.cs`
- Test: `tests/Merchants.Tests/MerchantJwtTokenServiceTests.cs`

**Interfaces:**
- Consumes: `MerchantUser`/`MerchantUserConfiguration` (Task 3), `MerchantPermissionCatalog`
  (Task 3), `IMerchantAuthorizationChecker` (Task 2).
- Produces: `MerchantAuthorizationChecker` (implements `IMerchantAuthorizationChecker`) — Task 8's
  DI wiring registers it. `IMerchantPasswordHasher.{Hash,Verify}` — Task 5/6/7 consume it.
  `IMerchantJwtTokenService.IssueAccessToken(Guid merchantUserId, Guid merchantId, Guid
  organizationId, string email)` — Task 5 consumes this exact signature.

- [ ] **Step 1: Write IMerchantPasswordHasher**

`src/Modules/Merchants/Application/IMerchantPasswordHasher.cs`:

```csharp
namespace Courier.Merchants.Application;

public interface IMerchantPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
```

- [ ] **Step 2: Write MerchantPasswordHasherAdapter**

`src/Modules/Merchants/Infrastructure/MerchantPasswordHasherAdapter.cs`:

```csharp
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Microsoft.AspNetCore.Identity;

namespace Courier.Merchants.Infrastructure;

public class MerchantPasswordHasherAdapter : IMerchantPasswordHasher
{
    private readonly PasswordHasher<MerchantUser> _inner = new();

    public string Hash(string password) => _inner.HashPassword(null!, password);

    public bool Verify(string password, string hash)
        => _inner.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;
}
```

- [ ] **Step 3: Write MerchantJwtOptions and IMerchantJwtTokenService**

`src/Modules/Merchants/Application/MerchantJwtOptions.cs`:

```csharp
namespace Courier.Merchants.Application;

public class MerchantJwtOptions
{
    public required string SigningKey { get; set; }
    public required string Issuer { get; set; }
    public required string Audience { get; set; }
    public int AccessTokenMinutes { get; set; } = 15;
}
```

`src/Modules/Merchants/Application/IMerchantJwtTokenService.cs`:

```csharp
namespace Courier.Merchants.Application;

public interface IMerchantJwtTokenService
{
    string IssueAccessToken(Guid merchantUserId, Guid merchantId, Guid organizationId, string email);
}
```

- [ ] **Step 4: Write MerchantJwtTokenService**

`src/Modules/Merchants/Infrastructure/MerchantJwtTokenService.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Courier.Merchants.Application;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Courier.Merchants.Infrastructure;

public class MerchantJwtTokenService : IMerchantJwtTokenService
{
    private readonly MerchantJwtOptions _options;

    public MerchantJwtTokenService(IOptions<MerchantJwtOptions> options)
    {
        _options = options.Value;
    }

    public string IssueAccessToken(Guid merchantUserId, Guid merchantId, Guid organizationId, string email)
    {
        var claims = new[]
        {
            new Claim("sub", merchantUserId.ToString()),
            new Claim("org_id", organizationId.ToString()),
            new Claim("merchant_id", merchantId.ToString()),
            new Claim("email", email)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 5: Write the failing test for MerchantJwtTokenService**

`tests/Merchants.Tests/MerchantJwtTokenServiceTests.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using Courier.Merchants.Application;
using Courier.Merchants.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Merchants.Tests;

public class MerchantJwtTokenServiceTests
{
    private static MerchantJwtTokenService CreateService() => new(Options.Create(new MerchantJwtOptions
    {
        SigningKey = "merchant-tests-signing-key-at-least-32-bytes!",
        Issuer = "courier-tests",
        Audience = "courier-tests"
    }));

    [Fact]
    public void IssueAccessToken_EncodesMerchantIdAndOrgIdClaims()
    {
        var service = CreateService();
        var merchantUserId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();

        var token = service.IssueAccessToken(merchantUserId, merchantId, organizationId, "owner@example.com");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(merchantUserId.ToString(), jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal(merchantId.ToString(), jwt.Claims.Single(c => c.Type == "merchant_id").Value);
        Assert.Equal(organizationId.ToString(), jwt.Claims.Single(c => c.Type == "org_id").Value);
        Assert.Equal("owner@example.com", jwt.Claims.Single(c => c.Type == "email").Value);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter MerchantJwtTokenServiceTests`
Expected: 1 test passes.

- [ ] **Step 7: Write the failing test for MerchantAuthorizationChecker**

`tests/Merchants.Tests/MerchantAuthorizationCheckerTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Merchants.Tests;

public class MerchantAuthorizationCheckerTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantUserConfiguration());
        }
    }

    private static TestDbContext CreateContext() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static ClaimsPrincipal PrincipalFor(Guid userId) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", userId.ToString())
    }, "test"));

    private static MerchantUser NewMerchantUser(Guid id, MerchantUserRole role, bool isActive = true) => new()
    {
        Id = id,
        MerchantId = Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        Email = $"{id:N}@example.com",
        PasswordHash = "not-used-in-this-test",
        FullName = "Test Merchant User",
        Role = role,
        IsActive = isActive
    };

    [Fact]
    public async Task HasPermissionAsync_OwnerRole_HoldsStaffManage()
    {
        await using var dbContext = CreateContext();
        var ownerId = Guid.NewGuid();
        dbContext.Set<MerchantUser>().Add(NewMerchantUser(ownerId, MerchantUserRole.Owner));
        await dbContext.SaveChangesAsync();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(PrincipalFor(ownerId), "merchant.staff.manage");

        Assert.True(result);
    }

    [Fact]
    public async Task HasPermissionAsync_StaffRole_LacksStaffManage()
    {
        await using var dbContext = CreateContext();
        var staffId = Guid.NewGuid();
        dbContext.Set<MerchantUser>().Add(NewMerchantUser(staffId, MerchantUserRole.Staff));
        await dbContext.SaveChangesAsync();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(PrincipalFor(staffId), "merchant.staff.manage");

        Assert.False(result);
    }

    [Fact]
    public async Task HasPermissionAsync_InactiveMerchantUser_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        var ownerId = Guid.NewGuid();
        dbContext.Set<MerchantUser>().Add(NewMerchantUser(ownerId, MerchantUserRole.Owner, isActive: false));
        await dbContext.SaveChangesAsync();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(PrincipalFor(ownerId), "merchant.profile.read");

        Assert.False(result);
    }

    [Fact]
    public async Task HasPermissionAsync_UnknownUser_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        var checker = new MerchantAuthorizationChecker(dbContext);

        var result = await checker.HasPermissionAsync(PrincipalFor(Guid.NewGuid()), "merchant.profile.read");

        Assert.False(result);
    }

    [Fact]
    public async Task HasPermissionAsync_UnauthenticatedPrincipal_ReturnsFalse()
    {
        await using var dbContext = CreateContext();
        var checker = new MerchantAuthorizationChecker(dbContext);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await checker.HasPermissionAsync(anonymous, "merchant.profile.read");

        Assert.False(result);
    }
}
```

- [ ] **Step 8: Run the tests to verify they fail**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter MerchantAuthorizationCheckerTests`
Expected: FAIL with "MerchantAuthorizationChecker could not be found" (type does not exist yet).

- [ ] **Step 9: Implement MerchantAuthorizationChecker**

`src/Modules/Merchants/Infrastructure/MerchantAuthorizationChecker.cs`:

```csharp
using System.Security.Claims;
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Infrastructure;

public class MerchantAuthorizationChecker : IMerchantAuthorizationChecker
{
    private readonly DbContext _dbContext;

    public MerchantAuthorizationChecker(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default)
    {
        var merchantUserId = user.GetUserId();
        if (merchantUserId is null)
        {
            return false;
        }

        var merchantUser = await _dbContext.Set<MerchantUser>()
            .FirstOrDefaultAsync(mu => mu.Id == merchantUserId && mu.IsActive, cancellationToken);
        if (merchantUser is null)
        {
            return false;
        }

        return MerchantPermissionCatalog.RolePermissions[merchantUser.Role].Contains(permission);
    }
}
```

- [ ] **Step 10: Run the tests to verify they pass**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj`
Expected: all tests pass (2 catalog + 1 JWT + 5 authorization checker = 8 total).

- [ ] **Step 11: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (no new warnings).

- [ ] **Step 12: Commit**

```bash
git add src/Modules/Merchants/Application src/Modules/Merchants/Infrastructure tests/Merchants.Tests
git commit -m "feat: add merchant authorization checker, password hasher, and JWT issuance"
```

---

### Task 5: Merchants Application — MerchantAuthService (Login, Refresh)

**Files:**
- Create: `src/Modules/Merchants/Application/MerchantAuthDtos.cs`
- Create: `src/Modules/Merchants/Application/MerchantAuthService.cs`
- Test: `tests/Merchants.Tests/MerchantAuthServiceTests.cs`

**Interfaces:**
- Consumes: `IMerchantPasswordHasher`, `IMerchantJwtTokenService` (Task 4), `MerchantUser`,
  `MerchantRefreshToken` (Task 3).
- Produces: `MerchantAuthService.{LoginAsync,RefreshTokenAsync}` — Task 8's
  `MerchantAuthEndpoints` consumes these exact method names/signatures.
  `MerchantAuthResult(string AccessToken, string RefreshToken, Guid MerchantUserId, Guid
  MerchantId, Guid OrganizationId)` — the response shape Task 8's endpoints and Task 10's
  Api.Tests both consume.

- [ ] **Step 1: Write the DTOs and exception**

`src/Modules/Merchants/Application/MerchantAuthDtos.cs`:

```csharp
namespace Courier.Merchants.Application;

public record MerchantLoginRequest(string Email, string Password);
public record MerchantRefreshRequest(string RefreshToken);
public record MerchantAuthResult(string AccessToken, string RefreshToken, Guid MerchantUserId, Guid MerchantId, Guid OrganizationId);

public class MerchantAuthenticationFailedException : Exception
{
    public MerchantAuthenticationFailedException(string message) : base(message) { }
}
```

- [ ] **Step 2: Write the failing test**

`tests/Merchants.Tests/MerchantAuthServiceTests.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courier.Merchants.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class MerchantAuthServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantUserConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantRefreshTokenConfiguration());
        }
    }

    private static TestDbContext CreateContext() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static MerchantAuthService CreateService(TestDbContext dbContext)
    {
        var jwtOptions = Options.Create(new MerchantJwtOptions
        {
            SigningKey = "merchant-tests-signing-key-at-least-32-bytes!",
            Issuer = "courier-tests",
            Audience = "courier-tests"
        });
        return new MerchantAuthService(dbContext, new MerchantPasswordHasherAdapter(), new MerchantJwtTokenService(jwtOptions), new NoopAuditLogger());
    }

    private static async Task<MerchantUser> SeedOwnerAsync(TestDbContext dbContext, string email, string password)
    {
        var owner = new MerchantUser
        {
            Id = Guid.NewGuid(),
            MerchantId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            Email = email,
            PasswordHash = new MerchantPasswordHasherAdapter().Hash(password),
            FullName = "Owner Person",
            Role = MerchantUserRole.Owner,
            IsActive = true
        };
        dbContext.Set<MerchantUser>().Add(owner);
        await dbContext.SaveChangesAsync();
        return owner;
    }

    [Fact]
    public async Task LoginAsync_CorrectPassword_ReturnsTokensForMerchantUser()
    {
        await using var dbContext = CreateContext();
        var owner = await SeedOwnerAsync(dbContext, "owner@example.com", "P@ssw0rd123!");
        var service = CreateService(dbContext);

        var result = await service.LoginAsync(new MerchantLoginRequest("owner@example.com", "P@ssw0rd123!"));

        Assert.Equal(owner.Id, result.MerchantUserId);
        Assert.Equal(owner.MerchantId, result.MerchantId);
        Assert.Equal(owner.OrganizationId, result.OrganizationId);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsMerchantAuthenticationFailed()
    {
        await using var dbContext = CreateContext();
        await SeedOwnerAsync(dbContext, "owner2@example.com", "P@ssw0rd123!");
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<MerchantAuthenticationFailedException>(
            () => service.LoginAsync(new MerchantLoginRequest("owner2@example.com", "wrong-password")));
    }

    [Fact]
    public async Task LoginAsync_UnknownEmail_ThrowsMerchantAuthenticationFailed()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<MerchantAuthenticationFailedException>(
            () => service.LoginAsync(new MerchantLoginRequest("nobody@example.com", "whatever")));
    }

    [Fact]
    public async Task LoginAsync_InactiveMerchantUser_ThrowsMerchantAuthenticationFailed()
    {
        await using var dbContext = CreateContext();
        var owner = await SeedOwnerAsync(dbContext, "inactive@example.com", "P@ssw0rd123!");
        owner.IsActive = false;
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<MerchantAuthenticationFailedException>(
            () => service.LoginAsync(new MerchantLoginRequest("inactive@example.com", "P@ssw0rd123!")));
    }

    [Fact]
    public async Task RefreshTokenAsync_RotatesToken()
    {
        await using var dbContext = CreateContext();
        await SeedOwnerAsync(dbContext, "refresh@example.com", "P@ssw0rd123!");
        var service = CreateService(dbContext);
        var loginResult = await service.LoginAsync(new MerchantLoginRequest("refresh@example.com", "P@ssw0rd123!"));

        var refreshed = await service.RefreshTokenAsync(new MerchantRefreshRequest(loginResult.RefreshToken));

        Assert.Equal(loginResult.MerchantUserId, refreshed.MerchantUserId);
        Assert.NotEqual(loginResult.RefreshToken, refreshed.RefreshToken);

        await Assert.ThrowsAsync<MerchantAuthenticationFailedException>(
            () => service.RefreshTokenAsync(new MerchantRefreshRequest(loginResult.RefreshToken)));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter MerchantAuthServiceTests`
Expected: FAIL with "MerchantAuthService could not be found" (type does not exist yet).

- [ ] **Step 4: Implement MerchantAuthService**

`src/Modules/Merchants/Application/MerchantAuthService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Courier.Audit.Application;
using Courier.Merchants.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class MerchantAuthService
{
    private readonly DbContext _dbContext;
    private readonly IMerchantPasswordHasher _passwordHasher;
    private readonly IMerchantJwtTokenService _jwtTokenService;
    private readonly IAuditLogger _auditLogger;

    // Computed once, same timing-safety reasoning as Identity.Application.AuthService's
    // DummyPasswordHash: keeps the unknown-email and wrong-password paths costing the same
    // single hash-verify, avoiding a timing side-channel that would let an attacker enumerate
    // registered merchant-user emails.
    private static readonly string DummyPasswordHash =
        new Courier.Merchants.Infrastructure.MerchantPasswordHasherAdapter().Hash("dummy-password-for-timing-safety");

    public MerchantAuthService(DbContext dbContext, IMerchantPasswordHasher passwordHasher, IMerchantJwtTokenService jwtTokenService, IAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _auditLogger = auditLogger;
    }

    public async Task<MerchantAuthResult> LoginAsync(MerchantLoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.ToLowerInvariant();
        var merchantUser = await _dbContext.Set<MerchantUser>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(mu => mu.Email == email, cancellationToken);

        var passwordValid = _passwordHasher.Verify(request.Password, merchantUser?.PasswordHash ?? DummyPasswordHash);

        if (merchantUser is null || !passwordValid || !merchantUser.IsActive)
        {
            throw new MerchantAuthenticationFailedException("Invalid email or password.");
        }

        var refreshToken = await IssueRefreshTokenAsync(merchantUser.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(merchantUser.OrganizationId, merchantUser.Id, "merchant_user.login", "MerchantUser", merchantUser.Id.ToString()), cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(merchantUser.Id, merchantUser.MerchantId, merchantUser.OrganizationId, merchantUser.Email);
        return new MerchantAuthResult(accessToken, refreshToken.PlainText, merchantUser.Id, merchantUser.MerchantId, merchantUser.OrganizationId);
    }

    public async Task<MerchantAuthResult> RefreshTokenAsync(MerchantRefreshRequest request, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(request.RefreshToken);
        var existing = await _dbContext.Set<MerchantRefreshToken>()
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash, cancellationToken);

        if (existing is null || existing.RevokedAt is not null || existing.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new MerchantAuthenticationFailedException("Refresh token is invalid or expired.");
        }

        var merchantUser = await _dbContext.Set<MerchantUser>().IgnoreQueryFilters()
            .FirstAsync(mu => mu.Id == existing.MerchantUserId, cancellationToken);

        if (!merchantUser.IsActive)
        {
            throw new MerchantAuthenticationFailedException("Refresh token is invalid or expired.");
        }

        var newRefreshToken = await IssueRefreshTokenAsync(merchantUser.Id, cancellationToken);
        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenId = newRefreshToken.Entity.Id;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtTokenService.IssueAccessToken(merchantUser.Id, merchantUser.MerchantId, merchantUser.OrganizationId, merchantUser.Email);
        return new MerchantAuthResult(accessToken, newRefreshToken.PlainText, merchantUser.Id, merchantUser.MerchantId, merchantUser.OrganizationId);
    }

    private async Task<(MerchantRefreshToken Entity, string PlainText)> IssueRefreshTokenAsync(Guid merchantUserId, CancellationToken cancellationToken)
    {
        var plainText = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var entity = new MerchantRefreshToken
        {
            Id = Guid.NewGuid(),
            MerchantUserId = merchantUserId,
            TokenHash = HashToken(plainText),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        _dbContext.Set<MerchantRefreshToken>().Add(entity);
        await Task.CompletedTask;
        return (entity, plainText);
    }

    private static string HashToken(string token)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj`
Expected: all tests pass (8 from Tasks 3-4 + 5 new = 13 total).

- [ ] **Step 6: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (no new warnings).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Merchants/Application/MerchantAuthDtos.cs src/Modules/Merchants/Application/MerchantAuthService.cs tests/Merchants.Tests/MerchantAuthServiceTests.cs
git commit -m "feat: add MerchantAuthService for merchant-user login and refresh"
```

---

### Task 6: Merchants Application — MerchantOnboardingService

**Files:**
- Create: `src/Modules/Merchants/Application/MerchantOnboardingDtos.cs`
- Create: `src/Modules/Merchants/Application/MerchantOnboardingService.cs`
- Test: `tests/Merchants.Tests/MerchantOnboardingServiceTests.cs`

**Interfaces:**
- Consumes: `IGeographyLookupService.IsValidHierarchyAsync` (Phase 3, `Courier.Geography.Application`),
  `ICurrentTenantProvider` (`SharedKernel`), `IMerchantPasswordHasher` (Task 4).
- Produces: `MerchantOnboardingService.{CreateMerchantAsync,GetMerchantAsync,ListMerchantsAsync}` —
  Task 8's `MerchantsEndpoints`/`MerchantProfileEndpoints` consume these exact method
  names/signatures. `MerchantResponse(Guid Id, string LegalName, string DisplayName, string?
  ContactPhone, string? ContactEmail, KycStatus KycStatus, bool IsActive)` — the response shape
  Task 8's endpoints and Task 10's Api.Tests both consume.

- [ ] **Step 1: Write the DTOs and exception**

`src/Modules/Merchants/Application/MerchantOnboardingDtos.cs`:

```csharp
using Courier.Merchants.Domain;

namespace Courier.Merchants.Application;

public record CreateMerchantRequest(
    string LegalName,
    string DisplayName,
    string? ContactPhone,
    string? ContactEmail,
    string OwnerEmail,
    string OwnerPassword,
    string OwnerFullName,
    Guid ProvinceId,
    Guid DistrictId,
    Guid LocalLevelId,
    int WardNumber,
    string AddressLine);

public record MerchantResponse(
    Guid Id,
    string LegalName,
    string DisplayName,
    string? ContactPhone,
    string? ContactEmail,
    KycStatus KycStatus,
    bool IsActive);

public class MerchantValidationException : Exception
{
    public MerchantValidationException(string message) : base(message) { }
}
```

- [ ] **Step 2: Write the failing test**

`tests/Merchants.Tests/MerchantOnboardingServiceTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Merchants.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

// A stub, not the real Geography module — MerchantOnboardingServiceTests exercises this
// service's own validation-ordering and persistence logic in isolation. Real geography data
// (via the actual GeographyQueryService against seeded Kathmandu rows) is exercised end-to-end
// in Task 10's Api.Tests, mirroring how Branch's own geography validation was unit-tested with
// a stub and proven for real only at the Api.Tests layer in Phase 3.
file class StubGeographyLookupService : IGeographyLookupService
{
    private readonly bool _result;
    public StubGeographyLookupService(bool result) => _result = result;
    public Task<bool> IsValidHierarchyAsync(Guid provinceId, Guid districtId, Guid localLevelId, int wardNumber, CancellationToken cancellationToken = default)
        => Task.FromResult(_result);
}

public class MerchantOnboardingServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantUserConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantAddressConfiguration());
        }
    }

    private static TestDbContext CreateContext() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static ClaimsPrincipal ActingUser(Guid userId) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", userId.ToString())
    }, "test"));

    private static CreateMerchantRequest ValidRequest(string ownerEmail) => new(
        "Acme Traders Pvt. Ltd.", "Acme Traders", "01-4444444", "contact@acme.example",
        ownerEmail, "P@ssw0rd123!", "Owner Person",
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "New Road, Kathmandu");

    [Fact]
    public async Task CreateMerchantAsync_ValidGeography_PersistsMerchantOwnerAndAddress()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = new MerchantOnboardingService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new MerchantPasswordHasherAdapter(), new StubGeographyLookupService(true));

        var result = await service.CreateMerchantAsync(ValidRequest("owner1@example.com"), ActingUser(Guid.NewGuid()));

        Assert.Equal("Acme Traders Pvt. Ltd.", result.LegalName);
        Assert.Equal(KycStatus.Pending, result.KycStatus);
        Assert.True(result.IsActive);

        var owner = await dbContext.Set<MerchantUser>().SingleAsync(mu => mu.MerchantId == result.Id);
        Assert.Equal(MerchantUserRole.Owner, owner.Role);
        Assert.Equal("owner1@example.com", owner.Email);

        var address = await dbContext.Set<MerchantAddress>().SingleAsync(a => a.MerchantId == result.Id);
        Assert.True(address.IsDefaultPickup);
    }

    [Fact]
    public async Task CreateMerchantAsync_InvalidGeography_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var service = new MerchantOnboardingService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new MerchantPasswordHasherAdapter(), new StubGeographyLookupService(false));

        await Assert.ThrowsAsync<MerchantValidationException>(
            () => service.CreateMerchantAsync(ValidRequest("owner2@example.com"), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task CreateMerchantAsync_DuplicateOwnerEmail_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var service = new MerchantOnboardingService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new MerchantPasswordHasherAdapter(), new StubGeographyLookupService(true));
        await service.CreateMerchantAsync(ValidRequest("dup@example.com"), ActingUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<MerchantValidationException>(
            () => service.CreateMerchantAsync(ValidRequest("dup@example.com"), ActingUser(Guid.NewGuid())));
    }

    [Fact]
    public async Task GetMerchantAsync_ThenListMerchantsAsync_ReturnsCreatedMerchant()
    {
        await using var dbContext = CreateContext();
        var service = new MerchantOnboardingService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new MerchantPasswordHasherAdapter(), new StubGeographyLookupService(true));
        var created = await service.CreateMerchantAsync(ValidRequest("owner3@example.com"), ActingUser(Guid.NewGuid()));

        var fetched = await service.GetMerchantAsync(created.Id);
        Assert.Equal(created.Id, fetched!.Id);

        var listed = await service.ListMerchantsAsync();
        Assert.Contains(listed, m => m.Id == created.Id);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter MerchantOnboardingServiceTests`
Expected: FAIL with "MerchantOnboardingService could not be found" (type does not exist yet).

- [ ] **Step 4: Implement MerchantOnboardingService**

`src/Modules/Merchants/Application/MerchantOnboardingService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Geography.Application;
using Courier.Merchants.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class MerchantOnboardingService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IMerchantPasswordHasher _passwordHasher;
    private readonly IGeographyLookupService _geographyLookupService;

    public MerchantOnboardingService(
        DbContext dbContext,
        ICurrentTenantProvider currentTenantProvider,
        IAuditLogger auditLogger,
        IMerchantPasswordHasher passwordHasher,
        IGeographyLookupService geographyLookupService)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _passwordHasher = passwordHasher;
        _geographyLookupService = geographyLookupService;
    }

    public async Task<MerchantResponse> CreateMerchantAsync(CreateMerchantRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new MerchantValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new MerchantValidationException("Acting user could not be resolved.");

        var ownerEmail = request.OwnerEmail.ToLowerInvariant();
        var emailTaken = await _dbContext.Set<MerchantUser>().IgnoreQueryFilters()
            .AnyAsync(mu => mu.Email == ownerEmail, cancellationToken);
        if (emailTaken)
        {
            throw new MerchantValidationException($"Email '{ownerEmail}' is already registered to a merchant account.");
        }

        var validGeography = await _geographyLookupService.IsValidHierarchyAsync(
            request.ProvinceId, request.DistrictId, request.LocalLevelId, request.WardNumber, cancellationToken);
        if (!validGeography)
        {
            throw new MerchantValidationException("Province/District/LocalLevel/WardNumber do not form a valid geography chain.");
        }

        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            LegalName = request.LegalName,
            DisplayName = request.DisplayName,
            ContactPhone = request.ContactPhone,
            ContactEmail = request.ContactEmail,
            KycStatus = KycStatus.Pending,
            IsActive = true
        };
        _dbContext.Set<Merchant>().Add(merchant);

        var owner = new MerchantUser
        {
            Id = Guid.NewGuid(),
            MerchantId = merchant.Id,
            OrganizationId = organizationId,
            Email = ownerEmail,
            PasswordHash = _passwordHasher.Hash(request.OwnerPassword),
            FullName = request.OwnerFullName,
            Role = MerchantUserRole.Owner,
            IsActive = true
        };
        _dbContext.Set<MerchantUser>().Add(owner);

        var address = new MerchantAddress
        {
            Id = Guid.NewGuid(),
            MerchantId = merchant.Id,
            OrganizationId = organizationId,
            Label = "Pickup",
            ProvinceId = request.ProvinceId,
            DistrictId = request.DistrictId,
            LocalLevelId = request.LocalLevelId,
            WardNumber = request.WardNumber,
            AddressLine = request.AddressLine,
            IsDefaultPickup = true
        };
        _dbContext.Set<MerchantAddress>().Add(address);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "merchant.created", "Merchant", merchant.Id.ToString()), cancellationToken);

        return ToResponse(merchant);
    }

    public async Task<MerchantResponse?> GetMerchantAsync(Guid merchantId, CancellationToken cancellationToken = default)
    {
        var merchant = await _dbContext.Set<Merchant>().FirstOrDefaultAsync(m => m.Id == merchantId, cancellationToken);
        return merchant is null ? null : ToResponse(merchant);
    }

    public async Task<List<MerchantResponse>> ListMerchantsAsync(CancellationToken cancellationToken = default)
    {
        var merchants = await _dbContext.Set<Merchant>().ToListAsync(cancellationToken);
        return merchants.Select(ToResponse).ToList();
    }

    private static MerchantResponse ToResponse(Merchant merchant) => new(
        merchant.Id, merchant.LegalName, merchant.DisplayName, merchant.ContactPhone, merchant.ContactEmail,
        merchant.KycStatus, merchant.IsActive);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj`
Expected: all tests pass (13 from Tasks 3-5 + 4 new = 17 total).

- [ ] **Step 6: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (no new warnings).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Merchants/Application/MerchantOnboardingDtos.cs src/Modules/Merchants/Application/MerchantOnboardingService.cs tests/Merchants.Tests/MerchantOnboardingServiceTests.cs
git commit -m "feat: add MerchantOnboardingService for merchant creation with geography validation"
```

---

### Task 7: Merchants Application — MerchantStaffService and MerchantKycService

**Files:**
- Create: `src/Modules/Merchants/Application/MerchantStaffDtos.cs`
- Create: `src/Modules/Merchants/Application/MerchantStaffService.cs`
- Create: `src/Modules/Merchants/Application/MerchantKycDtos.cs`
- Create: `src/Modules/Merchants/Application/MerchantKycService.cs`
- Test: `tests/Merchants.Tests/MerchantStaffServiceTests.cs`
- Test: `tests/Merchants.Tests/MerchantKycServiceTests.cs`

**Interfaces:**
- Consumes: `ClaimsPrincipalExtensions.GetMerchantId` (Task 2), `IObjectStorage` (Task 2),
  `IMerchantPasswordHasher` (Task 4).
- Produces: `MerchantStaffService.{InviteStaffAsync,ListStaffAsync}`,
  `MerchantKycService.{UploadDocumentAsync,ListDocumentsAsync,ReviewDocumentAsync}` — Task 8's
  endpoints consume these exact method names/signatures.

- [ ] **Step 1: Write MerchantStaffDtos and MerchantStaffService**

`src/Modules/Merchants/Application/MerchantStaffDtos.cs`:

```csharp
using Courier.Merchants.Domain;

namespace Courier.Merchants.Application;

public record InviteMerchantStaffRequest(string Email, string Password, string FullName);
public record MerchantStaffResponse(Guid Id, string Email, string FullName, MerchantUserRole Role, bool IsActive);
```

`src/Modules/Merchants/Application/MerchantStaffService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Merchants.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class MerchantStaffService
{
    private readonly DbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly IMerchantPasswordHasher _passwordHasher;

    public MerchantStaffService(DbContext dbContext, IAuditLogger auditLogger, IMerchantPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _passwordHasher = passwordHasher;
    }

    public async Task<MerchantStaffResponse> InviteStaffAsync(InviteMerchantStaffRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = actingUser.GetMerchantId()
            ?? throw new MerchantValidationException("Acting merchant could not be resolved.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new MerchantValidationException("Acting user could not be resolved.");

        var actingMerchantUser = await _dbContext.Set<MerchantUser>()
            .FirstOrDefaultAsync(mu => mu.Id == actingUserId, cancellationToken)
            ?? throw new MerchantValidationException("Acting merchant user could not be resolved.");

        var email = request.Email.ToLowerInvariant();
        var emailTaken = await _dbContext.Set<MerchantUser>().IgnoreQueryFilters()
            .AnyAsync(mu => mu.Email == email, cancellationToken);
        if (emailTaken)
        {
            throw new MerchantValidationException($"Email '{email}' is already registered to a merchant account.");
        }

        var staff = new MerchantUser
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            OrganizationId = actingMerchantUser.OrganizationId,
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            FullName = request.FullName,
            Role = MerchantUserRole.Staff,
            IsActive = true
        };
        _dbContext.Set<MerchantUser>().Add(staff);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(actingMerchantUser.OrganizationId, actingUserId, "merchant_staff.invited", "MerchantUser", staff.Id.ToString()), cancellationToken);

        return ToResponse(staff);
    }

    public async Task<List<MerchantStaffResponse>> ListStaffAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var merchantId = user.GetMerchantId()
            ?? throw new MerchantValidationException("Acting merchant could not be resolved.");

        var staff = await _dbContext.Set<MerchantUser>()
            .Where(mu => mu.MerchantId == merchantId)
            .ToListAsync(cancellationToken);
        return staff.Select(ToResponse).ToList();
    }

    private static MerchantStaffResponse ToResponse(MerchantUser merchantUser) => new(
        merchantUser.Id, merchantUser.Email, merchantUser.FullName, merchantUser.Role, merchantUser.IsActive);
}
```

- [ ] **Step 2: Write the failing test for MerchantStaffService**

`tests/Merchants.Tests/MerchantStaffServiceTests.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Merchants.Tests;

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class MerchantStaffServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantUserConfiguration());
        }
    }

    private static TestDbContext CreateContext() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static ClaimsPrincipal OwnerPrincipal(Guid userId, Guid merchantId) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", userId.ToString()),
        new Claim("merchant_id", merchantId.ToString())
    }, "test"));

    private static async Task<MerchantUser> SeedOwnerAsync(TestDbContext dbContext, Guid merchantId, Guid organizationId)
    {
        var owner = new MerchantUser
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            OrganizationId = organizationId,
            Email = $"owner-{Guid.NewGuid():N}@example.com",
            PasswordHash = "not-used",
            FullName = "Owner Person",
            Role = MerchantUserRole.Owner,
            IsActive = true
        };
        dbContext.Set<MerchantUser>().Add(owner);
        await dbContext.SaveChangesAsync();
        return owner;
    }

    [Fact]
    public async Task InviteStaffAsync_ValidRequest_CreatesStaffUnderSameMerchantAndOrg()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, organizationId);
        var service = new MerchantStaffService(dbContext, new NoopAuditLogger(), new MerchantPasswordHasherAdapter());

        var result = await service.InviteStaffAsync(
            new InviteMerchantStaffRequest("staff@example.com", "P@ssw0rd123!", "Staff Person"),
            OwnerPrincipal(owner.Id, merchantId));

        Assert.Equal(MerchantUserRole.Staff, result.Role);
        var staffRow = await dbContext.Set<MerchantUser>().SingleAsync(mu => mu.Id == result.Id);
        Assert.Equal(merchantId, staffRow.MerchantId);
        Assert.Equal(organizationId, staffRow.OrganizationId);
    }

    [Fact]
    public async Task InviteStaffAsync_DuplicateEmail_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        var service = new MerchantStaffService(dbContext, new NoopAuditLogger(), new MerchantPasswordHasherAdapter());
        await service.InviteStaffAsync(new InviteMerchantStaffRequest("dup-staff@example.com", "P@ssw0rd123!", "Staff Person"), OwnerPrincipal(owner.Id, merchantId));

        await Assert.ThrowsAsync<MerchantValidationException>(() =>
            service.InviteStaffAsync(new InviteMerchantStaffRequest("dup-staff@example.com", "P@ssw0rd123!", "Second Staff"), OwnerPrincipal(owner.Id, merchantId)));
    }

    [Fact]
    public async Task ListStaffAsync_ReturnsOnlyStaffForThatMerchant()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var owner = await SeedOwnerAsync(dbContext, merchantId, Guid.NewGuid());
        await SeedOwnerAsync(dbContext, otherMerchantId, Guid.NewGuid());
        var service = new MerchantStaffService(dbContext, new NoopAuditLogger(), new MerchantPasswordHasherAdapter());
        await service.InviteStaffAsync(new InviteMerchantStaffRequest("listed-staff@example.com", "P@ssw0rd123!", "Staff Person"), OwnerPrincipal(owner.Id, merchantId));

        var listed = await service.ListStaffAsync(OwnerPrincipal(owner.Id, merchantId));

        Assert.Contains(listed, mu => mu.Email == "listed-staff@example.com");
        Assert.DoesNotContain(listed, mu => mu.Email != "listed-staff@example.com" && mu.Role == MerchantUserRole.Owner && mu.Id != owner.Id);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter MerchantStaffServiceTests`
Expected: FAIL with "MerchantStaffService could not be found" (type does not exist yet — it was
already written in Step 1 above per this task's non-strict-TDD bundling; run this to confirm the
test file itself compiles and exercises real assertions, then proceed).

Note: Step 1 already wrote `MerchantStaffService.cs`, so this task's TDD loop is: write the DTOs
+ service (Step 1), write the test (Step 2), then run tests (this step) — they should already
pass since the implementation exists. If any fail, fix the implementation, not the test.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter MerchantStaffServiceTests`
Expected: all 3 tests pass.

- [ ] **Step 5: Write MerchantKycDtos and MerchantKycService**

`src/Modules/Merchants/Application/MerchantKycDtos.cs`:

```csharp
using Courier.Merchants.Domain;

namespace Courier.Merchants.Application;

public record MerchantKycDocumentResponse(Guid Id, KycDocumentType DocumentType, KycStatus Status, Guid? ReviewedByUserId);
public record ReviewKycDocumentRequest(KycStatus Status);
```

`src/Modules/Merchants/Application/MerchantKycService.cs`:

```csharp
using System.Security.Claims;
using Courier.Audit.Application;
using Courier.Merchants.Domain;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Merchants.Application;

public class MerchantKycService
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenantProvider _currentTenantProvider;
    private readonly IAuditLogger _auditLogger;
    private readonly IObjectStorage _objectStorage;

    public MerchantKycService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger, IObjectStorage objectStorage)
    {
        _dbContext = dbContext;
        _currentTenantProvider = currentTenantProvider;
        _auditLogger = auditLogger;
        _objectStorage = objectStorage;
    }

    public async Task<MerchantKycDocumentResponse> UploadDocumentAsync(
        KycDocumentType documentType, Stream content, string contentType, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var merchantId = actingUser.GetMerchantId()
            ?? throw new MerchantValidationException("Acting merchant could not be resolved.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new MerchantValidationException("Acting user could not be resolved.");

        var merchantUser = await _dbContext.Set<MerchantUser>().FirstAsync(mu => mu.Id == actingUserId, cancellationToken);

        var storageKey = await _objectStorage.UploadAsync($"kyc/{merchantId}", content, contentType, cancellationToken);

        var document = new MerchantKycDocument
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            OrganizationId = merchantUser.OrganizationId,
            DocumentType = documentType,
            StorageKey = storageKey,
            Status = KycStatus.Pending
        };
        _dbContext.Set<MerchantKycDocument>().Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(merchantUser.OrganizationId, actingUserId, "merchant_kyc.uploaded", "MerchantKycDocument", document.Id.ToString()), cancellationToken);

        return ToResponse(document);
    }

    public async Task<List<MerchantKycDocumentResponse>> ListDocumentsAsync(Guid merchantId, CancellationToken cancellationToken = default)
    {
        var documents = await _dbContext.Set<MerchantKycDocument>()
            .Where(d => d.MerchantId == merchantId)
            .ToListAsync(cancellationToken);
        return documents.Select(ToResponse).ToList();
    }

    public async Task<MerchantKycDocumentResponse?> ReviewDocumentAsync(
        Guid merchantId, Guid documentId, ReviewKycDocumentRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var organizationId = _currentTenantProvider.OrganizationId
            ?? throw new MerchantValidationException("No active organization context.");
        var actingUserId = actingUser.GetUserId()
            ?? throw new MerchantValidationException("Acting user could not be resolved.");

        // Set<MerchantKycDocument>() carries its own OrganizationId query filter (denormalized
        // from Merchant.OrganizationId at upload time, see MerchantKycDocument.cs) — a
        // merchantId/documentId pair belonging to a different org simply won't be found here,
        // no separate "look up Merchant first" step needed.
        var document = await _dbContext.Set<MerchantKycDocument>()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.MerchantId == merchantId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        document.Status = request.Status;
        document.ReviewedByUserId = actingUserId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(new AuditEntry(organizationId, actingUserId, "merchant_kyc.reviewed", "MerchantKycDocument", document.Id.ToString()), cancellationToken);

        return ToResponse(document);
    }

    private static MerchantKycDocumentResponse ToResponse(MerchantKycDocument document) => new(
        document.Id, document.DocumentType, document.Status, document.ReviewedByUserId);
}
```

- [ ] **Step 6: Write the failing test for MerchantKycService**

`tests/Merchants.Tests/MerchantKycServiceTests.cs`:

```csharp
using System.Security.Claims;
using System.Text;
using Courier.Audit.Application;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Merchants.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class NoopAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file class InMemoryObjectStorage : IObjectStorage
{
    public Task<string> UploadAsync(string keyPrefix, Stream content, string contentType, CancellationToken cancellationToken = default)
        => Task.FromResult($"{keyPrefix}/{Guid.NewGuid():N}");

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream>(new MemoryStream());
}

public class MerchantKycServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MerchantUserConfiguration());
            modelBuilder.ApplyConfiguration(new MerchantKycDocumentConfiguration());
        }
    }

    private static TestDbContext CreateContext() => new(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static ClaimsPrincipal MerchantPrincipal(Guid userId, Guid merchantId) => new(new ClaimsIdentity(new[]
    {
        new Claim("sub", userId.ToString()),
        new Claim("merchant_id", merchantId.ToString())
    }, "test"));

    private static async Task<MerchantUser> SeedMerchantUserAsync(TestDbContext dbContext, Guid merchantId, Guid organizationId)
    {
        var user = new MerchantUser
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            OrganizationId = organizationId,
            Email = $"user-{Guid.NewGuid():N}@example.com",
            PasswordHash = "not-used",
            FullName = "Merchant User",
            Role = MerchantUserRole.Owner,
            IsActive = true
        };
        dbContext.Set<MerchantUser>().Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task UploadDocumentAsync_PersistsPendingDocumentUnderCorrectMerchant()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var user = await SeedMerchantUserAsync(dbContext, merchantId, organizationId);
        var service = new MerchantKycService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new InMemoryObjectStorage());
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("fake pan card bytes"));

        var result = await service.UploadDocumentAsync(KycDocumentType.PanCard, content, "application/pdf", MerchantPrincipal(user.Id, merchantId));

        Assert.Equal(KycStatus.Pending, result.Status);
        Assert.Null(result.ReviewedByUserId);
        var row = await dbContext.Set<MerchantKycDocument>().SingleAsync(d => d.Id == result.Id);
        Assert.Equal(merchantId, row.MerchantId);
        Assert.Equal(organizationId, row.OrganizationId);
    }

    [Fact]
    public async Task ReviewDocumentAsync_ApprovesAndRecordsReviewer()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var user = await SeedMerchantUserAsync(dbContext, merchantId, organizationId);
        var uploadService = new MerchantKycService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new InMemoryObjectStorage());
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("fake citizenship bytes"));
        var uploaded = await uploadService.UploadDocumentAsync(KycDocumentType.CitizenshipDocument, content, "application/pdf", MerchantPrincipal(user.Id, merchantId));

        var reviewerId = Guid.NewGuid();
        var reviewed = await uploadService.ReviewDocumentAsync(
            merchantId, uploaded.Id, new ReviewKycDocumentRequest(KycStatus.Approved), MerchantPrincipal(reviewerId, merchantId));

        Assert.NotNull(reviewed);
        Assert.Equal(KycStatus.Approved, reviewed!.Status);
        Assert.Equal(reviewerId, reviewed.ReviewedByUserId);
    }

    [Fact]
    public async Task ReviewDocumentAsync_WrongOrganization_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var merchantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var user = await SeedMerchantUserAsync(dbContext, merchantId, organizationId);
        var uploadService = new MerchantKycService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new InMemoryObjectStorage());
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("fake bytes"));
        var uploaded = await uploadService.UploadDocumentAsync(KycDocumentType.Other, content, "application/pdf", MerchantPrincipal(user.Id, merchantId));

        var wrongOrgService = new MerchantKycService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new NoopAuditLogger(), new InMemoryObjectStorage());
        var result = await wrongOrgService.ReviewDocumentAsync(
            merchantId, uploaded.Id, new ReviewKycDocumentRequest(KycStatus.Approved), MerchantPrincipal(Guid.NewGuid(), merchantId));

        Assert.Null(result);
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj`
Expected: all tests pass (17 from Tasks 3-6 + 3 staff + 3 kyc = 23 total).

- [ ] **Step 8: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (no new warnings).

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Merchants/Application/MerchantStaffDtos.cs src/Modules/Merchants/Application/MerchantStaffService.cs src/Modules/Merchants/Application/MerchantKycDtos.cs src/Modules/Merchants/Application/MerchantKycService.cs tests/Merchants.Tests/MerchantStaffServiceTests.cs tests/Merchants.Tests/MerchantKycServiceTests.cs
git commit -m "feat: add MerchantStaffService and MerchantKycService"
```

---

### Task 8: Merchants Presentation — Endpoints, DI Wiring, Configuration

**Files:**
- Create: `src/Modules/Merchants/Presentation/MerchantsEndpoints.cs`
- Create: `src/Modules/Merchants/Presentation/MerchantAuthEndpoints.cs`
- Create: `src/Modules/Merchants/Presentation/MerchantProfileEndpoints.cs`
- Create: `src/Modules/Merchants/Presentation/MerchantStaffEndpoints.cs`
- Create: `src/Modules/Merchants/Presentation/MerchantKycEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Modify: `src/Api/appsettings.Development.json`

**Interfaces:**
- Consumes: `MerchantOnboardingService`, `MerchantAuthService`, `MerchantStaffService`,
  `MerchantKycService` (Tasks 5-7), `IMerchantAuthorizationChecker`, `IObjectStorage`,
  `RequireMerchantPermission` (Task 2/4).
- Produces: `POST/GET /api/v1/merchants`, `GET /api/v1/merchants/{id}`,
  `POST /api/v1/merchants/{merchantId}/kyc-documents/{documentId}/review`,
  `POST /api/v1/merchant/auth/{login,refresh}`, `GET /api/v1/merchant/profile`,
  `POST/GET /api/v1/merchant/staff`, `POST/GET /api/v1/merchant/kyc-documents` — Task 10's
  Api.Tests exercise every one of these end-to-end.

- [ ] **Step 1: Write MerchantsEndpoints (org-staff-facing)**

`src/Modules/Merchants/Presentation/MerchantsEndpoints.cs`:

```csharp
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Merchants.Presentation;

public static class MerchantsEndpoints
{
    public static void MapMerchantsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchants");

        group.MapPost("/", async (CreateMerchantRequest request, MerchantOnboardingService onboardingService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await onboardingService.CreateMerchantAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/merchants/{result.Id}", result);
            }
            catch (MerchantValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "MERCHANT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("merchant.create");

        group.MapGet("/", async (MerchantOnboardingService onboardingService, CancellationToken cancellationToken) =>
            Results.Ok(await onboardingService.ListMerchantsAsync(cancellationToken))).RequirePermission("merchant.read");

        group.MapGet("/{id:guid}", async (Guid id, MerchantOnboardingService onboardingService, CancellationToken cancellationToken) =>
        {
            var merchant = await onboardingService.GetMerchantAsync(id, cancellationToken);
            return merchant is null ? Results.NotFound() : Results.Ok(merchant);
        }).RequirePermission("merchant.read");

        group.MapPost("/{merchantId:guid}/kyc-documents/{documentId:guid}/review", async (
            Guid merchantId, Guid documentId, ReviewKycDocumentRequest request, MerchantKycService kycService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await kycService.ReviewDocumentAsync(merchantId, documentId, request, httpContext.User, httpContext.RequestAborted);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (MerchantValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "MERCHANT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequirePermission("merchant.kyc.review");
    }
}
```

- [ ] **Step 2: Write MerchantAuthEndpoints (merchant self-service login)**

`src/Modules/Merchants/Presentation/MerchantAuthEndpoints.cs`:

```csharp
using Courier.Merchants.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Merchants.Presentation;

public static class MerchantAuthEndpoints
{
    public static void MapMerchantAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchant/auth");

        group.MapPost("/login", async (MerchantLoginRequest request, MerchantAuthService authService) =>
        {
            try
            {
                var result = await authService.LoginAsync(request);
                return Results.Ok(result);
            }
            catch (MerchantAuthenticationFailedException ex)
            {
                return Results.Json(new { error = new { code = "LOGIN_FAILED", message = ex.Message } }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        group.MapPost("/refresh", async (MerchantRefreshRequest request, MerchantAuthService authService) =>
        {
            try
            {
                var result = await authService.RefreshTokenAsync(request);
                return Results.Ok(result);
            }
            catch (MerchantAuthenticationFailedException ex)
            {
                return Results.Json(new { error = new { code = "REFRESH_FAILED", message = ex.Message } }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });
    }
}
```

- [ ] **Step 3: Write MerchantProfileEndpoints**

`src/Modules/Merchants/Presentation/MerchantProfileEndpoints.cs`:

```csharp
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Merchants.Presentation;

public static class MerchantProfileEndpoints
{
    public static void MapMerchantProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchant/profile");

        group.MapGet("/", async (HttpContext httpContext, MerchantOnboardingService onboardingService, CancellationToken cancellationToken) =>
        {
            var merchantId = httpContext.User.GetMerchantId();
            if (merchantId is null)
            {
                return Results.Unauthorized();
            }
            var merchant = await onboardingService.GetMerchantAsync(merchantId.Value, cancellationToken);
            return merchant is null ? Results.NotFound() : Results.Ok(merchant);
        }).RequireMerchantPermission("merchant.profile.read");
    }
}
```

- [ ] **Step 4: Write MerchantStaffEndpoints**

`src/Modules/Merchants/Presentation/MerchantStaffEndpoints.cs`:

```csharp
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Merchants.Presentation;

public static class MerchantStaffEndpoints
{
    public static void MapMerchantStaffEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchant/staff");

        group.MapPost("/", async (InviteMerchantStaffRequest request, MerchantStaffService staffService, HttpContext httpContext) =>
        {
            try
            {
                var result = await staffService.InviteStaffAsync(request, httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/merchant/staff/{result.Id}", result);
            }
            catch (MerchantValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "MERCHANT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireMerchantPermission("merchant.staff.manage");

        group.MapGet("/", async (MerchantStaffService staffService, HttpContext httpContext, CancellationToken cancellationToken) =>
            Results.Ok(await staffService.ListStaffAsync(httpContext.User, cancellationToken))).RequireMerchantPermission("merchant.staff.manage");
    }
}
```

- [ ] **Step 5: Write MerchantKycEndpoints**

`src/Modules/Merchants/Presentation/MerchantKycEndpoints.cs`:

```csharp
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Merchants.Presentation;

public static class MerchantKycEndpoints
{
    public static void MapMerchantKycEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchant/kyc-documents");

        // Raw request body upload (not multipart/form-data): the client sends the file's bytes
        // directly as the request body with documentType on the query string. This sidesteps
        // IFormFile/antiforgery concerns entirely for a single-file upload, which is all this
        // phase needs.
        group.MapPost("/", async (KycDocumentType documentType, HttpContext httpContext, MerchantKycService kycService) =>
        {
            try
            {
                var result = await kycService.UploadDocumentAsync(
                    documentType, httpContext.Request.Body, httpContext.Request.ContentType ?? "application/octet-stream",
                    httpContext.User, httpContext.RequestAborted);
                return Results.Created($"/api/v1/merchant/kyc-documents/{result.Id}", result);
            }
            catch (MerchantValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "MERCHANT_VALIDATION_FAILED", message = ex.Message } });
            }
        }).RequireMerchantPermission("merchant.kyc.upload");

        group.MapGet("/", async (HttpContext httpContext, MerchantKycService kycService, CancellationToken cancellationToken) =>
        {
            var merchantId = httpContext.User.GetMerchantId();
            if (merchantId is null)
            {
                return Results.Unauthorized();
            }
            return Results.Ok(await kycService.ListDocumentsAsync(merchantId.Value, cancellationToken));
        }).RequireMerchantPermission("merchant.kyc.upload");
    }
}
```

- [ ] **Step 6: Wire DI registrations, endpoint mappings, and configuration in Program.cs**

In `src/Api/Program.cs`, add to the `using` list (keep alphabetical among the existing `Courier.*`
usings):

```csharp
using Courier.Merchants.Application;
using Courier.Merchants.Infrastructure;
using Courier.Merchants.Presentation;
```

After the existing `builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));`
line, add:

```csharp
builder.Services.Configure<MerchantJwtOptions>(builder.Configuration.GetSection("Jwt"));
```

After the existing `builder.Services.AddScoped<DeliveryZoneService>();` line, add:

```csharp
builder.Services.AddScoped<IMerchantPasswordHasher, MerchantPasswordHasherAdapter>();
builder.Services.AddScoped<IMerchantJwtTokenService, MerchantJwtTokenService>();
builder.Services.AddScoped<IMerchantAuthorizationChecker, MerchantAuthorizationChecker>();
builder.Services.AddScoped<MerchantAuthService>();
builder.Services.AddScoped<MerchantOnboardingService>();
builder.Services.AddScoped<MerchantStaffService>();
builder.Services.AddScoped<MerchantKycService>();
builder.Services.AddSingleton<IObjectStorage>(_ =>
    new LocalFileSystemObjectStorage(builder.Configuration["ObjectStorage:LocalRootPath"] ?? Path.Combine(AppContext.BaseDirectory, "storage")));
```

After the existing `app.MapDeliveryZonesEndpoints();` line, add:

```csharp
app.MapMerchantsEndpoints();
app.MapMerchantAuthEndpoints();
app.MapMerchantProfileEndpoints();
app.MapMerchantStaffEndpoints();
app.MapMerchantKycEndpoints();
```

- [ ] **Step 7: Add ObjectStorage configuration**

In `src/Api/appsettings.Development.json`, after the existing `"Jwt": { ... }` block (as a
sibling top-level key), add:

```json
  "ObjectStorage": {
    "LocalRootPath": "./storage/kyc-documents"
  }
```

- [ ] **Step 8: Verify the solution builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (no new warnings).

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Merchants/Presentation src/Api/Program.cs src/Api/appsettings.Development.json
git commit -m "feat: add Merchants endpoints, DI wiring, and object storage configuration"
```

---

### Task 9: Persistence — Wire Merchants into AppDbContext, Add Migration

**Files:**
- Modify: `src/Persistence/AppDbContext.cs`
- Create: `src/Persistence/Migrations/` (generated)

**Interfaces:**
- Consumes: all Merchants configurations (Task 3).
- Produces: `AppDbContext.{Merchants,MerchantUsers,MerchantAddresses,MerchantKycDocuments,
  MerchantRefreshTokens}`, a migration creating all five tables plus their FKs/indexes/query
  filters. Task 10's Api.Tests query through this context.

- [ ] **Step 1: Update AppDbContext**

Replace the full contents of `src/Persistence/AppDbContext.cs`:

```csharp
using Courier.Audit.Domain;
using Courier.Audit.Infrastructure;
using Courier.Branches.Domain;
using Courier.Branches.Infrastructure;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Courier.Merchants.Domain;
using Courier.Merchants.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Courier.Persistence;

public class AppDbContext : DbContext
{
    private readonly ICurrentTenantProvider _currentTenantProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantProvider currentTenantProvider)
        : base(options)
    {
        _currentTenantProvider = currentTenantProvider;
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();
    public DbSet<Province> Provinces => Set<Province>();
    public DbSet<District> Districts => Set<District>();
    public DbSet<LocalLevel> LocalLevels => Set<LocalLevel>();
    public DbSet<Ward> Wards => Set<Ward>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<DeliveryZone> DeliveryZones => Set<DeliveryZone>();
    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<MerchantUser> MerchantUsers => Set<MerchantUser>();
    public DbSet<MerchantAddress> MerchantAddresses => Set<MerchantAddress>();
    public DbSet<MerchantKycDocument> MerchantKycDocuments => Set<MerchantKycDocument>();
    public DbSet<MerchantRefreshToken> MerchantRefreshTokens => Set<MerchantRefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new PermissionConfiguration());
        modelBuilder.ApplyConfiguration(new UserRoleConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new BranchConfiguration());
        modelBuilder.ApplyConfiguration(new UserBranchConfiguration());
        modelBuilder.ApplyConfiguration(new ProvinceConfiguration());
        modelBuilder.ApplyConfiguration(new DistrictConfiguration());
        modelBuilder.ApplyConfiguration(new LocalLevelConfiguration());
        modelBuilder.ApplyConfiguration(new WardConfiguration());
        modelBuilder.ApplyConfiguration(new AreaConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryZoneConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantUserConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantAddressConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantKycDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new MerchantRefreshTokenConfiguration());

        // Branch.OrganizationId -> Organizations FK lives here rather than in
        // BranchConfiguration (Branches module) because Branches deliberately has no project
        // reference to Identity — the dependency runs the other way (Identity -> Branches, for
        // UserBranch's FK to Branch and IBranchHierarchyService). Persistence already
        // references both modules, so it's the one place that can wire this specific
        // constraint without introducing a project cycle.
        modelBuilder.Entity<Branch>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(b => b.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning for DeliveryZone.OrganizationId: Geography has no project reference to
        // Identity.
        modelBuilder.Entity<DeliveryZone>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(z => z.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same reasoning for all four Merchants entities' OrganizationId: Merchants has no
        // project reference to Identity either.
        modelBuilder.Entity<Merchant>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MerchantUser>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(mu => mu.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MerchantAddress>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MerchantKycDocument>().HasOne<Organization>()
            .WithMany()
            .HasForeignKey(d => d.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Tenant filters fail CLOSED: the explicit `OrganizationId != null` guard stops EF Core's
        // null-semantics translation from degrading these into `WHERE "OrganizationId" IS NULL`
        // when no tenant is resolved, which would otherwise match org-less rows (planned for
        // future platform/SuperAdmin accounts) instead of matching nothing.
        modelBuilder.Entity<User>().HasQueryFilter(u => _currentTenantProvider.OrganizationId != null && u.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Role>().HasQueryFilter(r => _currentTenantProvider.OrganizationId != null && r.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<UserRole>().HasQueryFilter(ur => _currentTenantProvider.OrganizationId != null && ur.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(a => _currentTenantProvider.OrganizationId != null && a.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Branch>().HasQueryFilter(b => _currentTenantProvider.OrganizationId != null && b.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<UserBranch>().HasQueryFilter(ub => _currentTenantProvider.OrganizationId != null && ub.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<DeliveryZone>().HasQueryFilter(z => _currentTenantProvider.OrganizationId != null && z.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<Merchant>().HasQueryFilter(m => _currentTenantProvider.OrganizationId != null && m.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<MerchantUser>().HasQueryFilter(mu => _currentTenantProvider.OrganizationId != null && mu.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<MerchantAddress>().HasQueryFilter(a => _currentTenantProvider.OrganizationId != null && a.OrganizationId == _currentTenantProvider.OrganizationId);
        modelBuilder.Entity<MerchantKycDocument>().HasQueryFilter(d => _currentTenantProvider.OrganizationId != null && d.OrganizationId == _currentTenantProvider.OrganizationId);

        // Defensive: nothing queries Organizations with a tenant context today (registration only
        // inserts), but without this a future `Organizations.ToListAsync()` would enumerate every
        // tenant on the platform.
        modelBuilder.Entity<Organization>().HasQueryFilter(o => _currentTenantProvider.OrganizationId != null && o.Id == _currentTenantProvider.OrganizationId);

        // No tenant filter on RefreshToken/MerchantRefreshToken: their lookups happen before the
        // caller's tenant is known (see AuthService.RefreshTokenAsync / MerchantAuthService.
        // RefreshTokenAsync), and rows are keyed by an unguessable SHA-256 token hash rather than
        // being enumerable. Filtering it would break the refresh flow outright.

        // No tenant filter on Province/District/LocalLevel/Ward/Area: shared reference data,
        // identical across every tenant, per ARCHITECTURE.md §2's Geography module description.
    }
}
```

- [ ] **Step 2: Ensure Postgres is running, then generate and apply the migration**

```bash
docker exec courier-postgres-1 pg_isready -U courier
```

If it's not already running (this project reuses the shared container across worktrees — do not
`docker compose up` a second one):

```bash
docker compose up -d postgres
```

Then:

```bash
dotnet ef migrations add AddMerchants --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj
dotnet ef database update --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj --connection "Host=localhost;Port=5434;Database=courier;Username=courier;Password=courier_dev_password"
```

Expected: a new migration file appears under `src/Persistence/Migrations/`; `database update`
reports it applied `AddMerchants` with no errors.

- [ ] **Step 3: Verify the new tables exist**

```bash
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"Merchants\""
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"MerchantUsers\""
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"MerchantAddresses\""
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"MerchantKycDocuments\""
docker exec courier-postgres-1 psql -U courier -d courier -c "\d \"MerchantRefreshTokens\""
```

Expected: each command prints a column listing (not "Did not find any relation"). Note: no
`-it` flags — a non-interactive shell can't allocate the pseudo-TTY they request.

- [ ] **Step 4: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (no new warnings).

- [ ] **Step 5: Commit**

```bash
git add src/Persistence
git commit -m "feat: wire Merchants into AppDbContext with migration"
```

---

### Task 10: Api.Tests — End-to-End Merchant Onboarding, Auth, Staff, and KYC Tests

**Files:**
- Create: `tests/Api.Tests/MerchantsEndpointsTests.cs`
- Create: `tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs`
- Create: `tests/Api.Tests/MerchantKycEndpointsTests.cs`

**Interfaces:**
- Consumes: `MerchantsEndpoints`/`MerchantAuthEndpoints`/`MerchantProfileEndpoints`/
  `MerchantStaffEndpoints`/`MerchantKycEndpoints` (Task 8), `GeographyIds` (Phase 3).
- Produces: end-to-end proof of Phase 4's exit criterion — a merchant can be onboarded, its
  owner and staff can log in with correctly scoped permissions (staff cannot invite staff),
  tenant isolation holds for `Merchant` and cross-org KYC review, and geography validation is
  enforced on merchant creation exactly like it is on branch creation.

- [ ] **Step 1: Write MerchantsEndpointsTests**

`tests/Api.Tests/MerchantsEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class MerchantsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public MerchantsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12];
        var request = new RegisterOrganizationRequest(
            $"Merchant Org {suffix}", $"merchant-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private static CreateMerchantRequest ValidMerchantRequest(string ownerEmail) => new(
        "Acme Traders Pvt. Ltd.", "Acme Traders", "01-4444444", "contact@acme.example",
        ownerEmail, "P@ssw0rd123!", "Merchant Owner",
        KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");

    [Fact]
    public async Task PostMerchants_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/merchants", ValidMerchantRequest("noauth@example.com"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateThenGet_StaffWithOwnerRole_Succeeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "a");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/merchants", ValidMerchantRequest("merchant-owner-a@example.com"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<MerchantResponse>();
        Assert.NotNull(created);
        Assert.Equal("Acme Traders", created!.DisplayName);

        var getResponse = await client.GetAsync($"/api/v1/merchants/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task CreateMerchant_MismatchedGeographyChain_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "b");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var pokharaLocalLevelId = GeographyIds.LocalLevelId("Gandaki", "Kaski", "Pokhara Metropolitan City");
        var request = ValidMerchantRequest("bad-geo@example.com") with { LocalLevelId = pokharaLocalLevelId };

        var response = await client.PostAsJsonAsync("/api/v1/merchants", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("valid geography chain", body);
    }

    [Fact]
    public async Task ListMerchants_OrgBCannotSeeOrgAsMerchant()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "c");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var createResponse = await clientA.PostAsJsonAsync("/api/v1/merchants", ValidMerchantRequest("merchant-owner-c@example.com"));
        var created = await createResponse.Content.ReadFromJsonAsync<MerchantResponse>();

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "d");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var getResponse = await clientB.GetAsync($"/api/v1/merchants/{created!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var listResponse = await clientB.GetAsync("/api/v1/merchants");
        var orgBMerchants = await listResponse.Content.ReadFromJsonAsync<List<MerchantResponse>>();
        Assert.DoesNotContain(orgBMerchants!, m => m.Id == created.Id);
    }
}
```

- [ ] **Step 2: Write MerchantAuthAndStaffEndpointsTests**

`tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class MerchantAuthAndStaffEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public MerchantAuthAndStaffEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> RegisterOrgAndGetStaffTokenAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12];
        var request = new RegisterOrganizationRequest(
            $"Merchant Org {suffix}", $"merchant-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        return auth.AccessToken;
    }

    private async Task<(string OwnerEmail, string Password)> CreateMerchantAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"owner-{label}-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            "Acme Traders Pvt. Ltd.", "Acme Traders", "01-4444444", "contact@acme.example",
            ownerEmail, "P@ssw0rd123!", "Merchant Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        return (ownerEmail, "P@ssw0rd123!");
    }

    [Fact]
    public async Task MerchantOwnerLogin_ThenGetProfile_Succeeds()
    {
        var staffClient = _factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, "a");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (ownerEmail, password) = await CreateMerchantAsync(staffClient, "a");

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var auth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();

        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        var profileResponse = await merchantClient.GetAsync("/api/v1/merchant/profile");
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        var profile = await profileResponse.Content.ReadFromJsonAsync<MerchantResponse>();
        Assert.Equal("Acme Traders", profile!.DisplayName);
    }

    [Fact]
    public async Task MerchantOwnerLogin_WrongPassword_Returns401()
    {
        var staffClient = _factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, "b");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (ownerEmail, _) = await CreateMerchantAsync(staffClient, "b");

        var merchantClient = _factory.CreateClient();
        var response = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Owner_InvitesStaff_StaffCanLoginAndReadProfileButNotInviteOthers()
    {
        var staffClient = _factory.CreateClient();
        var staffToken = await RegisterOrgAndGetStaffTokenAsync(staffClient, "c");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var (ownerEmail, ownerPassword) = await CreateMerchantAsync(staffClient, "c");

        var ownerClient = _factory.CreateClient();
        var ownerLogin = await ownerClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, ownerPassword));
        var ownerAuth = await ownerLogin.Content.ReadFromJsonAsync<MerchantAuthResult>();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth!.AccessToken);

        var staffEmail = $"merchant-staff-{Guid.NewGuid():N}@example.com";
        var inviteResponse = await ownerClient.PostAsJsonAsync("/api/v1/merchant/staff",
            new InviteMerchantStaffRequest(staffEmail, "P@ssw0rd123!", "Staff Person"));
        Assert.Equal(HttpStatusCode.Created, inviteResponse.StatusCode);

        var staffLoginClient = _factory.CreateClient();
        var staffLogin = await staffLoginClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(staffEmail, "P@ssw0rd123!"));
        var staffAuth = await staffLogin.Content.ReadFromJsonAsync<MerchantAuthResult>();
        staffLoginClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffAuth!.AccessToken);

        var staffProfileResponse = await staffLoginClient.GetAsync("/api/v1/merchant/profile");
        Assert.Equal(HttpStatusCode.OK, staffProfileResponse.StatusCode);

        var staffInviteAttempt = await staffLoginClient.PostAsJsonAsync("/api/v1/merchant/staff",
            new InviteMerchantStaffRequest($"blocked-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Blocked"));
        Assert.Equal(HttpStatusCode.Forbidden, staffInviteAttempt.StatusCode);
    }
}
```

- [ ] **Step 3: Write MerchantKycEndpointsTests**

`tests/Api.Tests/MerchantKycEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class MerchantKycEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public MerchantKycEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> RegisterStaffClientAsync(string label)
    {
        var client = _factory.CreateClient();
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12];
        var request = new RegisterOrganizationRequest(
            $"Merchant Org {suffix}", $"merchant-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private async Task<(Guid MerchantId, HttpClient MerchantClient)> CreateMerchantAndLoginAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"owner-{label}-{Guid.NewGuid():N}@example.com";
        var createRequest = new CreateMerchantRequest(
            "Acme Traders Pvt. Ltd.", "Acme Traders", "01-4444444", "contact@acme.example",
            ownerEmail, "P@ssw0rd123!", "Merchant Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu");
        var createResponse = await staffClient.PostAsJsonAsync("/api/v1/merchants", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<MerchantResponse>();

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, "P@ssw0rd123!"));
        var auth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        return (created!.Id, merchantClient);
    }

    [Fact]
    public async Task UploadThenReview_ApprovesDocument()
    {
        var staffClient = await RegisterStaffClientAsync("a");
        var (merchantId, merchantClient) = await CreateMerchantAndLoginAsync(staffClient, "a");

        var uploadContent = new ByteArrayContent(Encoding.UTF8.GetBytes("fake pan card bytes"));
        uploadContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var uploadResponse = await merchantClient.PostAsync($"/api/v1/merchant/kyc-documents?documentType={KycDocumentType.PanCard}", uploadContent);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<MerchantKycDocumentResponse>();
        Assert.Equal(KycStatus.Pending, uploaded!.Status);

        var listResponse = await merchantClient.GetAsync("/api/v1/merchant/kyc-documents");
        var documents = await listResponse.Content.ReadFromJsonAsync<List<MerchantKycDocumentResponse>>();
        Assert.Contains(documents!, d => d.Id == uploaded.Id);

        var reviewResponse = await staffClient.PostAsJsonAsync(
            $"/api/v1/merchants/{merchantId}/kyc-documents/{uploaded.Id}/review", new ReviewKycDocumentRequest(KycStatus.Approved));
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        var reviewed = await reviewResponse.Content.ReadFromJsonAsync<MerchantKycDocumentResponse>();
        Assert.Equal(KycStatus.Approved, reviewed!.Status);
        Assert.NotNull(reviewed.ReviewedByUserId);
    }

    [Fact]
    public async Task Review_FromDifferentOrganization_Returns404()
    {
        var staffClientA = await RegisterStaffClientAsync("b");
        var (merchantId, merchantClient) = await CreateMerchantAndLoginAsync(staffClientA, "b");
        var uploadContent = new ByteArrayContent(Encoding.UTF8.GetBytes("fake bytes"));
        uploadContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var uploadResponse = await merchantClient.PostAsync($"/api/v1/merchant/kyc-documents?documentType={KycDocumentType.Other}", uploadContent);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<MerchantKycDocumentResponse>();

        var staffClientB = await RegisterStaffClientAsync("c");

        var reviewResponse = await staffClientB.PostAsJsonAsync(
            $"/api/v1/merchants/{merchantId}/kyc-documents/{uploaded!.Id}/review", new ReviewKycDocumentRequest(KycStatus.Approved));

        Assert.Equal(HttpStatusCode.NotFound, reviewResponse.StatusCode);
    }
}
```

- [ ] **Step 4: Run the full test suite**

```bash
dotnet test Courier.slnx
```

Expected: all tests pass, including the 9 new tests in this task. The one known pre-existing
environmental failure (`HealthReadyTests`, Redis/RabbitMQ not running in this environment) is
unrelated to this phase and is not a regression — see the Phase 3 plan/ledger for the same
finding, still true here since nothing in this phase touches health checks.

- [ ] **Step 5: Verify the solution still builds**

Run: `dotnet build Courier.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (no new warnings).

- [ ] **Step 6: Commit**

```bash
git add tests/Api.Tests/MerchantsEndpointsTests.cs tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs tests/Api.Tests/MerchantKycEndpointsTests.cs
git commit -m "test: add end-to-end merchant onboarding, auth, staff, and KYC endpoint tests"
```

---

### Task 11: Update ROADMAP.md Status

**Files:**
- Modify: `docs/ROADMAP.md`

**Interfaces:**
- Consumes: nothing.
- Produces: an updated Phase 4 status note, mirroring Phase 1-3's, so Phase 5's "Entry" check has
  a clear record of what's actually done.

- [ ] **Step 1: Add the status note**

In `docs/ROADMAP.md`, immediately after the existing Phase 4 paragraph:

```text
## Phase 4 — Merchants, Merchant Users, KYC, Merchant Settings

`Merchant`, `MerchantUser`, KYC document handling (via the storage abstraction),
merchant-scoped authorization (`RBAC.md` §5).
*Exit:* a merchant account can be onboarded and its staff can log in with
correctly scoped permissions.
```

add:

```text

*Status (2026-08-19): Complete.* A new `Merchants` module implements `Merchant, MerchantUser,
MerchantAddress, MerchantKycDocument, MerchantRefreshToken` per `DOMAIN-MODEL.md`. Merchant
authorization is a separate track from staff RBAC (`RBAC.md` §5): `MerchantUser.Role` is a
2-value enum (`Owner`/`Staff`) checked against a small `MerchantPermissionCatalog`, with its own
`IMerchantAuthorizationChecker`/`RequireMerchantPermission` filter and its own
`IMerchantJwtTokenService` issuing tokens that share the staff signing key/issuer/audience but
carry a distinct `merchant_id` claim. Org staff onboard a merchant in one call
(`POST /api/v1/merchants`, `merchant.create`) — creating the `Merchant`, its owner `MerchantUser`,
and a default pickup address, with the address's geography chain validated server-side via
`IGeographyLookupService` exactly like Phase 3's `Branch` creation
(`tests/Api.Tests/MerchantsEndpointsTests.cs`'s `CreateMerchant_MismatchedGeographyChain_Returns400`).
The owner can log in (`POST /api/v1/merchant/auth/login`), invite `MerchantStaff`
(`POST /api/v1/merchant/staff`, owner-only — proven end-to-end in
`tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs`), and both roles can upload KYC documents
(`POST /api/v1/merchant/kyc-documents`, raw-body upload against a new `IObjectStorage`
local-filesystem implementation in `SharedKernel`) for org staff to review
(`POST /api/v1/merchants/{id}/kyc-documents/{docId}/review`, `merchant.kyc.review`). `Merchant`,
`MerchantUser`, `MerchantAddress`, and `MerchantKycDocument` are all tenant-isolated
(`tests/Api.Tests/MerchantsEndpointsTests.cs`'s `ListMerchants_OrgBCannotSeeOrgAsMerchant`,
`MerchantKycEndpointsTests.cs`'s `Review_FromDifferentOrganization_Returns404`). Deferred to later
phases per this phase's plan: `ApiKey`/Merchant API (Phase 12), a merchant-update endpoint,
`MerchantAddress` CRUD beyond the one address created at onboarding.
```

- [ ] **Step 2: Commit**

```bash
git add docs/ROADMAP.md
git commit -m "docs: mark Phase 4 exit criteria met"
```

---

## Plan Self-Review Notes

- **Spec coverage:** `Merchant`, `MerchantUser`, KYC document handling via a storage abstraction,
  merchant-scoped authorization (Tasks 3-9) — all of `ROADMAP.md` Phase 4's stated scope is
  covered. The exit criterion ("a merchant account can be onboarded and its staff can log in with
  correctly scoped permissions") is directly proven by Task 10's
  `Owner_InvitesStaff_StaffCanLoginAndReadProfileButNotInviteOthers` test, which exercises both
  halves (onboarding → login; correctly scoped — staff denied an owner-only action).
- **Scope-exclusion decisions:** `ApiKey`, a merchant-update endpoint, and `MerchantAddress` CRUD
  beyond onboarding are explicitly out of scope per the approved 2026-08-19 brainstorming
  decisions — documented in this plan's Architecture section and Task 11's status note so it
  reads as a deliberate choice, not a gap, when Phase 5+ is planned.
- **Module boundary:** `Merchants → Geography` only, verified against every task — Task 1 wires
  the reference, Task 3/6's `MerchantAddress`/geography-validation code are the only consumers of
  it, and no task adds a `Merchants → Identity` reference (Tasks 4-5's independent
  `IMerchantPasswordHasher`/`IMerchantJwtTokenService`/`MerchantJwtOptions` are what make this
  possible, at the cost of ~50 lines of deliberate duplication against their Identity
  equivalents — documented and justified in the Architecture section).
- **Type/signature consistency:** `IMerchantJwtTokenService.IssueAccessToken(Guid, Guid, Guid,
  string)` has the identical parameter order (`merchantUserId, merchantId, organizationId,
  email`) everywhere it's defined (Task 4), implemented (Task 4), and called (Task 5).
  `MerchantAuthResult`/`MerchantResponse`/`MerchantKycDocumentResponse` field shapes are
  identical across their defining task and every consuming task (5→8→10, 6→8→10, 7→8→10).
  `ClaimsPrincipalExtensions.GetMerchantId` (Task 2) is consumed with the identical signature in
  Tasks 6-8.
- **Tenant isolation:** every org-scoped Merchants entity (`Merchant`, `MerchantUser`,
  `MerchantAddress`, `MerchantKycDocument`) gets the identical fail-closed query filter pattern
  in Task 9, and Task 10 proves it end-to-end for both `Merchant` listing and cross-org KYC
  review — the two paths most likely to leak across tenants if the denormalized `OrganizationId`
  design (Task 3) were implemented inconsistently.
