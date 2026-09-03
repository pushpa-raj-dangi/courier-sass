# Frontend Phase F10 — Merchant Portal — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a fully separate merchant-facing portal in `src/web` — booking, tracking, COD settlements/invoices, KYC, staff management, and API key management — on top of the backend's existing Phase 12 merchant JWT/API-key track, closing four real backend gaps found during design.

**Architecture:** A parallel `(merchant)` Next.js route group with its own cookies, BFF proxy routes, session hooks, and app shell — never sharing staff session code. One new `features/merchant-*` folder per screen (auth, shipments, pickups, cod, kyc, staff, api-keys, dashboard), each following this codebase's established Sheet-over-pages + Zustand panel store + TanStack Query hook conventions. Backend changes are four narrow, additive fix areas to the existing `Merchants`/`Pricing`/`Branches`/`Shipments` modules — no new modules, no new entities.

**Tech Stack:** ASP.NET Core minimal APIs / EF Core / PostgreSQL (backend), Next.js / TypeScript / Tailwind / shadcn-ui / TanStack Query / React Hook Form / Zod / Zustand (frontend), xUnit (backend tests), Vitest (frontend tests).

**Spec:** `docs/superpowers/specs/2026-09-03-frontend-phase-f10-merchant-portal-design.md`

## Global Constraints

- Every tenant-owned query must respect the ambient `AppDbContext` tenant query filter; a `BackgroundService`/non-HTTP context needs an explicit `organizationId` filter or a deliberately commented `.IgnoreQueryFilters()` instead (not applicable to this phase's synchronous HTTP-only endpoints, but never introduce one).
- Every new enum that persists as a raw integer must get `[JsonConverter(typeof(JsonStringEnumConverter))]` at the point it's introduced — this phase adds no new enums, but Task 1 fixes three pre-existing ones missing it (`KycDocumentType`, `MerchantUserRole`, `PickupRequestStatus`).
- Never trust a client-supplied `TenantId`/`BranchId`/`MerchantId`/price/COD amount — verify server-side (CLAUDE.md §33). Task 3 fixes exactly this class of gap on the price-quote route.
- Detail/create UI is always a right-side shadcn `Sheet` driven by a Zustand store, never a routed page (`docs/ARCHITECTURE.md` §9a).
- Every visible component traces back to a shadcn/ui primitive or block — no hand-rolled HTML form/interaction primitives (date inputs, checkboxes, tabs, pagination all use their shadcn equivalents).
- New cookies, BFF routes, and session code for the merchant track are fully separate from the staff track's — no shared cookie names, no shared `session.ts`/`apiFetch.ts` files.
- Currency formatting throughout uses `new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" })`, matching every existing money display in this codebase.
- Commit after every task's tests pass — small, reviewable commits, one per task.

---

## Task 1: Fix `KycDocumentType`/`MerchantUserRole`/`PickupRequestStatus` enum JSON serialization; record the gap in `CLARIFICATIONS.md`

**Files:**
- Modify: `src/Modules/Merchants/Domain/KycDocumentType.cs`
- Modify: `src/Modules/Merchants/Domain/MerchantUserRole.cs`
- Modify: `src/Modules/Shipments/Domain/PickupRequestStatus.cs`
- Modify: `docs/CLARIFICATIONS.md`
- Test: `tests/Merchants.Tests/KycDocumentTypeSerializationTests.cs` (new)
- Test: `tests/Merchants.Tests/MerchantUserRoleSerializationTests.cs` (new)
- Test: `tests/Shipments.Tests/PickupRequestStatusSerializationTests.cs` (new)

**Interfaces:**
- Produces: `KycDocumentType`/`MerchantUserRole`/`PickupRequestStatus` now serialize/deserialize as their member names (`"CitizenshipDocument"`, `"Owner"`, `"Requested"`, etc.) instead of raw integers — every later task's `MerchantMeResponse.Role`, `MerchantStaffResponse.Role`, the KYC upload query-string parameter, and Task 10's `PickupRequestResponse.Status` badge depend on this.

- [ ] **Step 1: Write the failing serialization tests**

```csharp
// tests/Merchants.Tests/KycDocumentTypeSerializationTests.cs
using System.Text.Json;
using Courier.Merchants.Domain;
using Xunit;

namespace Courier.Merchants.Tests;

public class KycDocumentTypeSerializationTests
{
    [Fact]
    public void SerializesAsMemberName_NotRawInteger()
    {
        var json = JsonSerializer.Serialize(KycDocumentType.PanCard);

        Assert.Equal("\"PanCard\"", json);
    }

    [Fact]
    public void RoundTrips_FromMemberNameString()
    {
        var value = JsonSerializer.Deserialize<KycDocumentType>("\"BusinessRegistrationCertificate\"");

        Assert.Equal(KycDocumentType.BusinessRegistrationCertificate, value);
    }
}
```

```csharp
// tests/Merchants.Tests/MerchantUserRoleSerializationTests.cs
using System.Text.Json;
using Courier.Merchants.Domain;
using Xunit;

namespace Courier.Merchants.Tests;

public class MerchantUserRoleSerializationTests
{
    [Fact]
    public void SerializesAsMemberName_NotRawInteger()
    {
        var json = JsonSerializer.Serialize(MerchantUserRole.Staff);

        Assert.Equal("\"Staff\"", json);
    }

    [Fact]
    public void RoundTrips_FromMemberNameString()
    {
        var value = JsonSerializer.Deserialize<MerchantUserRole>("\"Owner\"");

        Assert.Equal(MerchantUserRole.Owner, value);
    }
}
```

Also add, following the same shape, in a new file:

```csharp
// tests/Shipments.Tests/PickupRequestStatusSerializationTests.cs
using System.Text.Json;
using Courier.Shipments.Domain;
using Xunit;

namespace Courier.Shipments.Tests;

public class PickupRequestStatusSerializationTests
{
    [Fact]
    public void SerializesAsMemberName_NotRawInteger()
    {
        var json = JsonSerializer.Serialize(PickupRequestStatus.Scheduled);

        Assert.Equal("\"Scheduled\"", json);
    }

    [Fact]
    public void RoundTrips_FromMemberNameString()
    {
        var value = JsonSerializer.Deserialize<PickupRequestStatus>("\"Completed\"");

        Assert.Equal(PickupRequestStatus.Completed, value);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter "KycDocumentTypeSerializationTests|MerchantUserRoleSerializationTests" && dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter "PickupRequestStatusSerializationTests"`
Expected: FAIL — all `SerializesAsMemberName_NotRawInteger` tests get raw integers (`"2"`/`"1"`/`"1"`) instead of the quoted name.

- [ ] **Step 3: Add the converter attribute to all three enums**

```csharp
// src/Modules/Merchants/Domain/KycDocumentType.cs
using System.Text.Json.Serialization;

namespace Courier.Merchants.Domain;

// See Branches.Domain.BranchType for why this is a per-enum attribute rather than a global
// converter. Same fix as every prior phase's own enum-serialization gap (BranchType F2,
// ShipmentStatus F3, ManifestPurpose/ManifestStatus F4, 5 Riders enums F5, 3 Ndr enums F6,
// 3 Cod enums F7) — KycStatus in this same folder already had it; this one didn't.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KycDocumentType
{
    CitizenshipDocument,
    PanCard,
    BusinessRegistrationCertificate,
    BankAccountProof,
    Other
}
```

```csharp
// src/Modules/Merchants/Domain/MerchantUserRole.cs
using System.Text.Json.Serialization;

namespace Courier.Merchants.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MerchantUserRole
{
    Owner,
    Staff
}
```

```csharp
// src/Modules/Shipments/Domain/PickupRequestStatus.cs
using System.Text.Json.Serialization;

namespace Courier.Shipments.Domain;

// Same recurring gap — this enum was never revisited since Phase 12 introduced it, and no
// frontend phase touched Pickups until now (Frontend Phase F10's merchant-pickups screen is the
// first UI to render PickupRequestResponse.Status).
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PickupRequestStatus
{
    Requested,
    Scheduled,
    Completed,
    Cancelled
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter "KycDocumentTypeSerializationTests|MerchantUserRoleSerializationTests" && dotnet test tests/Shipments.Tests/Shipments.Tests.csproj --filter "PickupRequestStatusSerializationTests"`
Expected: PASS

- [ ] **Step 5: Run the full backend test suite to confirm no regression**

Run: `dotnet test`
Expected: PASS, all green — nothing in this codebase asserted the old raw-integer shape for any of the three enums.

- [ ] **Step 6: Add the `docs/CLARIFICATIONS.md` entry**

Append a new numbered entry (the file's last entry is `## 18.` — this becomes `## 19.`):

```markdown
## 19. Frontend Phase F10: three more enums needed `JsonStringEnumConverter`, plus three new merchant-reachable read routes

Same recurring bug class hit in every prior phase (`BranchType` F2, `ShipmentStatus` F3,
`ManifestPurpose`/`ManifestStatus` F4, 5 Riders enums F5, 3 Ndr enums F6, 3 Cod enums F7):
`KycDocumentType`, `MerchantUserRole`, and `PickupRequestStatus` had no
`[JsonConverter(typeof(JsonStringEnumConverter))]`. `PickupRequestStatus` dates back to Phase 12
but was never caught until this phase's merchant-pickups screen became the first UI anywhere to
render `PickupRequestResponse.Status`. Fixed directly on all three enums.

Building the Merchant Portal frontend also required three backend reads that had no
merchant-reachable path at all, found by direct inspection before any frontend code was written:

1. No `GET /api/v1/merchant/auth/me` existed — the frontend session hook needs one call
   to learn who is logged in, their role, and their resolved permission set, mirroring staff's
   own `GET /api/v1/auth/me`. Added.
2. `GET /api/v1/branches`, while nominally reachable by a merchant JWT (staff and merchant
   tokens both validate under the shared "Bearer" scheme), always returned `403` for a merchant
   caller — `RequirePermissionFilter` resolves the generic `"sub"` claim against staff
   `UserRole`/`RolePermission` tables, where no matching row exists for a merchant user id.
   Added a dedicated `GET /api/v1/merchant/branches` (org-scoped, no accessible-branch-id
   narrowing — merchants have no branch concept) instead of touching the staff route.
3. `POST /api/v1/pricing/quote` and `GET /api/v1/service-types` were both staff-only
   (`GetUserId() is null → Unauthorized`, `.RequirePermission("pricing.read")`) — the merchant
   booking form needs both for its live quote preview and Service Type picker. Extended both to
   the same dual-auth pattern `ShipmentsEndpoints` already uses, gated by a new
   `MerchantPermissionCatalog.PricingRead = "merchant.pricing.read"` (Owner + Staff). Also fixed
   a real latent risk this widening would otherwise have introduced: `PricingService.QuoteAsync`
   trusted the client-supplied `MerchantId` unmodified (used to resolve a `MerchantRateCard`
   discount) — a merchant caller could have read another merchant's negotiated rate by passing
   their id. The endpoint now forces `MerchantId` to the caller's own id for any merchant-JWT/
   API-key caller, mirroring `CreateShipmentRequest`'s existing "server overrides the
   client-supplied value" doc comment.

Deferred, not fixed this phase (recorded here so they aren't silently dropped):
- No update-role/deactivate endpoint on `MerchantStaffEndpoints` (only invite + list exist).
- No public tracking-event timeline for a shipment anywhere in the backend — affects the staff
  app too, not merchant-specific; `ShipmentResponse` has no embedded event history and no
  `/events` route exists.
- `MerchantAddress` has no Presentation endpoint at all — a merchant cannot save/reuse a sender
  address; the booking form's sender fields are entered manually every time.
- No merchant-facing webhook-subscription UI, though the backend (`WebhooksEndpoints`,
  `MerchantPermissionCatalog.WebhooksManage`) already fully supports it — out of this phase's
  named scope (booking/tracking/COD/invoices/API keys).
```

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Merchants/Domain/KycDocumentType.cs src/Modules/Merchants/Domain/MerchantUserRole.cs src/Modules/Shipments/Domain/PickupRequestStatus.cs tests/Merchants.Tests/KycDocumentTypeSerializationTests.cs tests/Merchants.Tests/MerchantUserRoleSerializationTests.cs tests/Shipments.Tests/PickupRequestStatusSerializationTests.cs docs/CLARIFICATIONS.md
git commit -m "fix: add JsonStringEnumConverter to KycDocumentType/MerchantUserRole/PickupRequestStatus"
```

---

## Task 2: `GET /api/v1/merchant/auth/me`

**Files:**
- Modify: `src/Modules/Merchants/Application/MerchantAuthDtos.cs`
- Modify: `src/Modules/Merchants/Application/MerchantAuthService.cs`
- Modify: `src/Modules/Merchants/Presentation/MerchantAuthEndpoints.cs`
- Test: `tests/Merchants.Tests/MerchantAuthServiceTests.cs` (add methods)
- Test: `tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs` (add methods)

**Interfaces:**
- Consumes: `MerchantUser`, `Merchant` (`Courier.Merchants.Domain`), `MerchantPermissionCatalog.RolePermissions` (`Courier.Merchants.Application`) — all pre-existing.
- Produces: `MerchantMeResponse(Guid MerchantUserId, Guid MerchantId, Guid OrganizationId, string MerchantDisplayName, string Email, string FullName, MerchantUserRole Role, IReadOnlyList<string> Permissions, KycStatus KycStatus)` and `MerchantAuthService.GetMeAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) : Task<MerchantMeResponse?>` — Task 5 (frontend session plumbing) calls `GET /api/v1/merchant/auth/me` and expects exactly this JSON shape (camelCase field names via the app's default `JsonSerializerOptions`).

- [ ] **Step 1: Write the failing unit tests**

Add to `tests/Merchants.Tests/MerchantAuthServiceTests.cs` (inside the existing `MerchantAuthServiceTests` class, after `RefreshTokenAsync_RotatesToken`):

```csharp
    [Fact]
    public async Task GetMeAsync_ActiveOwner_ReturnsProfileAndOwnerPermissions()
    {
        await using var dbContext = CreateContext();
        var owner = await SeedOwnerAsync(dbContext, "me-owner@example.com", "P@ssw0rd123!");
        var service = CreateService(dbContext);
        var principal = PrincipalFor(owner.Id);

        var result = await service.GetMeAsync(principal);

        Assert.NotNull(result);
        Assert.Equal(owner.Id, result!.MerchantUserId);
        Assert.Equal(owner.MerchantId, result.MerchantId);
        Assert.Equal(owner.OrganizationId, result.OrganizationId);
        Assert.Equal(owner.Email, result.Email);
        Assert.Equal(owner.FullName, result.FullName);
        Assert.Equal(MerchantUserRole.Owner, result.Role);
        Assert.Equal(MerchantPermissionCatalog.RolePermissions[MerchantUserRole.Owner].Count, result.Permissions.Count);
        Assert.Contains(MerchantPermissionCatalog.StaffManage, result.Permissions);
    }

    [Fact]
    public async Task GetMeAsync_InactiveMerchantUser_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var owner = await SeedOwnerAsync(dbContext, "me-inactive@example.com", "P@ssw0rd123!");
        owner.IsActive = false;
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var result = await service.GetMeAsync(PrincipalFor(owner.Id));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetMeAsync_ActiveUserUnderInactiveMerchant_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var owner = await SeedOwnerAsync(dbContext, "me-inactive-merchant@example.com", "P@ssw0rd123!", merchantIsActive: false);
        var service = CreateService(dbContext);

        var result = await service.GetMeAsync(PrincipalFor(owner.Id));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetMeAsync_UnknownMerchantUserId_ReturnsNull()
    {
        await using var dbContext = CreateContext();
        var service = CreateService(dbContext);

        var result = await service.GetMeAsync(PrincipalFor(Guid.NewGuid()));

        Assert.Null(result);
    }

    private static System.Security.Claims.ClaimsPrincipal PrincipalFor(Guid merchantUserId)
    {
        var identity = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("sub", merchantUserId.ToString())
        }, "TestAuth");
        return new System.Security.Claims.ClaimsPrincipal(identity);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter "GetMeAsync"`
Expected: FAIL with a compile error — `MerchantAuthService.GetMeAsync` does not exist yet.

- [ ] **Step 3: Add `MerchantMeResponse` to `MerchantAuthDtos.cs`**

```csharp
// src/Modules/Merchants/Application/MerchantAuthDtos.cs
using Courier.Merchants.Domain;

namespace Courier.Merchants.Application;

public record MerchantLoginRequest(string Email, string Password);
public record MerchantRefreshRequest(string RefreshToken);
public record MerchantAuthResult(string AccessToken, string RefreshToken, Guid MerchantUserId, Guid MerchantId, Guid OrganizationId);

// Mirrors Identity.Application.AuthDtos.MeResponse's shape and purpose for the merchant track —
// the one call the frontend session hook needs to know who is logged in, their role, and their
// resolved permission set (added Frontend Phase F10).
public record MerchantMeResponse(
    Guid MerchantUserId, Guid MerchantId, Guid OrganizationId,
    string MerchantDisplayName, string Email, string FullName,
    MerchantUserRole Role, IReadOnlyList<string> Permissions, KycStatus KycStatus);

public class MerchantAuthenticationFailedException : Exception
{
    public MerchantAuthenticationFailedException(string message) : base(message) { }
}
```

- [ ] **Step 4: Add `GetMeAsync` to `MerchantAuthService`**

Add `using System.Security.Claims;` and `using Courier.SharedKernel;` to the top of `src/Modules/Merchants/Application/MerchantAuthService.cs` (for `ClaimsPrincipal` and the `GetUserId()` extension), then add this method to the `MerchantAuthService` class, after `RefreshTokenAsync`:

```csharp
    // Merchant JWT `sub` claim carries the MerchantUserId, same accessor the login/refresh path
    // already relies on via GetUserId(). IgnoreQueryFilters() for the same reason LoginAsync's
    // own lookups use it: no tenant-scoped DbContext filter is active for a merchant-JWT
    // caller. Returns null (never throws) for every rejection reason — same non-disclosure
    // intent as LoginAsync's single generic failure message, though this route's caller is
    // already authenticated so there's no email-enumeration risk; null just means "not a valid
    // session," mapped to 401 by the endpoint.
    public async Task<MerchantMeResponse?> GetMeAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var merchantUserId = user.GetUserId();
        if (merchantUserId is null)
        {
            return null;
        }

        var merchantUser = await _dbContext.Set<MerchantUser>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(mu => mu.Id == merchantUserId && mu.IsActive, cancellationToken);
        if (merchantUser is null)
        {
            return null;
        }

        var merchant = await _dbContext.Set<Merchant>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == merchantUser.MerchantId, cancellationToken);
        if (merchant is null || !merchant.IsActive)
        {
            return null;
        }

        var permissions = MerchantPermissionCatalog.RolePermissions[merchantUser.Role].ToList();
        return new MerchantMeResponse(
            merchantUser.Id, merchantUser.MerchantId, merchantUser.OrganizationId,
            merchant.DisplayName, merchantUser.Email, merchantUser.FullName,
            merchantUser.Role, permissions, merchant.KycStatus);
    }
```

- [ ] **Step 5: Run the unit tests to verify they pass**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter "GetMeAsync"`
Expected: PASS

- [ ] **Step 6: Add the `/me` route**

Add to `src/Modules/Merchants/Presentation/MerchantAuthEndpoints.cs`, inside `MapMerchantAuthEndpoints`, after the `/refresh` route:

```csharp
        // Bare RequireAuthorization() defaults to the app's default scheme ("Bearer" —
        // Program.cs's AddAuthentication(JwtBearerDefaults.AuthenticationScheme)), which both
        // staff and merchant JWTs validate under (shared signing config) but an ApiKey-scheme
        // credential never does — this alone is the JWT-only self-escalation carve-out, same
        // bar ApiKeyEndpoints' three routes already enforce, no separate filter needed. No
        // MerchantPermissionCatalog check either: /me only requires being a valid, active
        // merchant user, not any specific permission.
        group.MapGet("/me", async (HttpContext httpContext, MerchantAuthService authService, CancellationToken cancellationToken) =>
        {
            var result = await authService.GetMeAsync(httpContext.User, cancellationToken);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        }).RequireAuthorization();
```

- [ ] **Step 7: Write the failing integration test**

Add to `tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs` (check the existing file's fixture/helper names first — reuse its established `RegisterOrganizationAsync`/merchant-seeding helpers rather than duplicating them):

```csharp
    [Fact]
    public async Task GetMe_ValidOwnerToken_ReturnsProfileWithOwnerPermissions()
    {
        var (accessToken, _, _) = await LoginAsSeededOwnerAsync();

        var response = await Client.GetAsync("/api/v1/merchant/auth/me", accessToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Owner", body.GetProperty("role").GetString());
        Assert.True(body.GetProperty("permissions").GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetMe_NoToken_ReturnsUnauthorized()
    {
        var response = await Client.GetAsync("/api/v1/merchant/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_StaffToken_ReturnsUnauthorized()
    {
        var staffAccessToken = await LoginAsSeededStaffUserAsync();

        var response = await Client.GetAsync("/api/v1/merchant/auth/me", staffAccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
```

If `LoginAsSeededOwnerAsync`/`LoginAsSeededStaffUserAsync`/the `Client.GetAsync(path, token)` overload don't already exist in this test file's helper region, add them following the exact pattern the file's other tests already use for registering an org, seeding a merchant/owner, and attaching a bearer token — read the full file first (it already has merchant-login and staff-registration flows from Phase 12/F9) rather than reinventing the helper shape.

- [ ] **Step 8: Run the integration tests to verify they fail, then pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "GetMe"`
Expected: FAIL first (route doesn't exist / 404), then PASS after Step 6's route is in place (it already is by this point — this step just confirms end-to-end).

- [ ] **Step 9: Run the full backend test suite**

Run: `dotnet test`
Expected: PASS, all green.

- [ ] **Step 10: Commit**

```bash
git add src/Modules/Merchants/Application/MerchantAuthDtos.cs src/Modules/Merchants/Application/MerchantAuthService.cs src/Modules/Merchants/Presentation/MerchantAuthEndpoints.cs tests/Merchants.Tests/MerchantAuthServiceTests.cs tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs
git commit -m "feat(merchants): add GET /api/v1/merchant/auth/me"
```

---

## Task 3: Merchant-reachable price quote and service-type reference data

**Files:**
- Modify: `src/Modules/Merchants/Application/MerchantPermissionCatalog.cs`
- Modify: `src/Modules/Pricing/Presentation/PricingEndpoints.cs`
- Modify: `src/Modules/Pricing/Pricing.csproj`
- Test: `tests/Merchants.Tests/MerchantPermissionCatalogTests.cs` (modify)
- Test: `tests/Api.Tests/PricingEndpointsTests.cs` or equivalent (new methods — check for an existing pricing integration test file first via `Glob "tests/Api.Tests/*Pricing*"`; create `tests/Api.Tests/MerchantPricingEndpointsTests.cs` if none exists)

**Interfaces:**
- Consumes: `IMerchantAuthorizationChecker` (`Courier.SharedKernel`), `IPermissionChecker` (`Courier.SharedKernel`), `AuthSchemes` (`Courier.SharedKernel`) — all pre-existing. `MerchantPermissionCatalog` (`Courier.Merchants.Application`) — new `Pricing.csproj` reference required, same as `Shipments.csproj`/`Cod.csproj` already have.
- Produces: `MerchantPermissionCatalog.PricingRead = "merchant.pricing.read"`, granted to both `OwnerPermissions` and `StaffPermissions` — Task 9 (merchant-shipments data layer) depends on this string when it wires `useMerchantShipmentQuote`/`useMerchantServiceTypes` against the now-dual-auth routes.

- [ ] **Step 1: Write the failing permission-catalog test updates**

Modify `tests/Merchants.Tests/MerchantPermissionCatalogTests.cs`:

```csharp
    [Fact]
    public void Owner_HoldsAllFourteenPermissions()
    {
        var ownerPermissions = MerchantPermissionCatalog.RolePermissions[MerchantUserRole.Owner];

        // 14: Phase 12 added ApiKeysManage as Owner-only, three pickup permissions
        // (PickupsCreate/Read/Cancel) shared with Staff, CodSettlementsRead shared with Staff,
        // and Phase F10 added PricingRead shared with Staff (both merchant roles book shipments
        // and need a live quote preview).
        Assert.Equal(14, ownerPermissions.Count);
        Assert.Contains(MerchantPermissionCatalog.ProfileRead, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.ProfileUpdate, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.StaffManage, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.KycUpload, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.WebhooksManage, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.ApiKeysManage, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.PickupsCreate, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.PickupsRead, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.PickupsCancel, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.CodSettlementsRead, ownerPermissions);
        Assert.Contains(MerchantPermissionCatalog.PricingRead, ownerPermissions);
    }
```

Rename the old `Owner_HoldsAllThirteenPermissions` test to `Owner_HoldsAllFourteenPermissions` (replace it, don't add a duplicate) and add `Assert.Contains(MerchantPermissionCatalog.PricingRead, staffPermissions);` to the existing `Staff_LacksStaffManageAndProfileUpdateAndWebhooksManageAndApiKeysManage` test.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter "MerchantPermissionCatalogTests"`
Expected: FAIL — `MerchantPermissionCatalog.PricingRead` doesn't exist yet (compile error).

- [ ] **Step 3: Add `PricingRead` to `MerchantPermissionCatalog`**

```csharp
// src/Modules/Merchants/Application/MerchantPermissionCatalog.cs — add after CodSettlementsRead
    // Both merchant roles book shipments and need the same live price-quote preview the staff
    // ShipmentCreateSheet already has (Frontend Phase F10) — same Owner+Staff tier as
    // ShipmentRead/CodSettlementsRead, not an Owner-only management permission.
    public const string PricingRead = "merchant.pricing.read";

    private static readonly string[] OwnerPermissions =
    {
        ProfileRead, ProfileUpdate, StaffManage, KycUpload, ShipmentCreate, ShipmentRead, ShipmentCancel, WebhooksManage, ApiKeysManage, PickupsCreate, PickupsRead, PickupsCancel, CodSettlementsRead, PricingRead
    };

    private static readonly string[] StaffPermissions =
    {
        ProfileRead, KycUpload, ShipmentCreate, ShipmentRead, ShipmentCancel, PickupsCreate, PickupsRead, PickupsCancel, CodSettlementsRead, PricingRead
    };
```

- [ ] **Step 4: Run the permission-catalog tests to verify they pass**

Run: `dotnet test tests/Merchants.Tests/Merchants.Tests.csproj --filter "MerchantPermissionCatalogTests"`
Expected: PASS

- [ ] **Step 5: Add the `Merchants` project reference to `Pricing.csproj`**

```xml
<!-- src/Modules/Pricing/Pricing.csproj — add alongside the existing ProjectReference items -->
    <ProjectReference Include="..\Merchants\Merchants.csproj" />
```

- [ ] **Step 6: Rewrite the `/service-types` route and the `/quote` route for dual-auth**

Replace the `/service-types` mapping inside `MapPricingEndpoints` (`src/Modules/Pricing/Presentation/PricingEndpoints.cs`):

```csharp
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Pricing.Presentation;

public static class PricingEndpoints
{
    public static void MapPricingEndpoints(this IEndpointRouteBuilder app)
    {
        var referenceGroup = app.MapGroup("/api/v1")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Bearer, AuthSchemes.ApiKey)
                .RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true))
            .RequireApiKeyRateLimit();

        // Dual-auth: staff via "pricing.read", or merchant JWT/API-key via
        // MerchantPermissionCatalog.PricingRead — same manual per-credential-type branching
        // ShipmentsEndpoints already establishes, since no existing filter covers "either token
        // kind, different permission string per kind." ServiceType has no OrganizationId at all
        // (global reference data) so this widening carries no tenant-isolation risk.
        referenceGroup.MapGet("/service-types", async (ReferenceDataService referenceDataService, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker, CancellationToken cancellationToken) =>
        {
            if (!await HasPricingReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker))
            {
                return httpContext.User.GetMerchantId() is null && httpContext.User.GetUserId() is null ? Results.Unauthorized() : Results.Forbid();
            }
            return Results.Ok(await referenceDataService.ListServiceTypesAsync(cancellationToken));
        }).WithTags("Price Management");

        referenceGroup.MapGet("/weight-slabs", async (ReferenceDataService referenceDataService, CancellationToken cancellationToken) =>
            Results.Ok(await referenceDataService.ListWeightSlabsAsync(cancellationToken))).RequirePermission("pricing.read").WithTags("Price Management");
```

Leave the rest of `MapPricingEndpoints` (rate-card management routes) untouched until the `quoteGroup` block — replace that block:

```csharp
        var quoteGroup = app.MapGroup("/api/v1/pricing")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(AuthSchemes.Bearer, AuthSchemes.ApiKey)
                .RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true))
            .RequireApiKeyRateLimit()
            .WithTags("Price Quote");

        quoteGroup.MapPost("/quote", async (PriceQuoteRequest request, PricingService pricingService, HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker) =>
        {
            if (!await HasPricingReadAccessAsync(httpContext, permissionChecker, merchantAuthorizationChecker))
            {
                return httpContext.User.GetMerchantId() is null && httpContext.User.GetUserId() is null ? Results.Unauthorized() : Results.Forbid();
            }

            // A merchant caller's client-supplied MerchantId is never trusted — forced to the
            // caller's own id regardless of what the request body said, same principle
            // CreateShipmentRequest's own doc comment establishes for booking. Without this, a
            // merchant JWT could pass another merchant's id and read their MerchantRateCard
            // discount through the quote response.
            if (httpContext.User.GetMerchantId() is Guid callerMerchantId)
            {
                request = request with { MerchantId = callerMerchantId };
            }

            try
            {
                var result = await pricingService.QuoteAsync(request, httpContext.RequestAborted);
                return Results.Ok(result);
            }
            catch (PricingValidationException ex)
            {
                return Results.BadRequest(new { error = new { code = "PRICING_VALIDATION_FAILED", message = ex.Message } });
            }
        });
    }

    // Shared by /service-types (reference data a quote preview needs) and /quote itself — staff
    // via "pricing.read", merchant JWT/API-key via MerchantPermissionCatalog.PricingRead.
    private static async Task<bool> HasPricingReadAccessAsync(HttpContext httpContext, IPermissionChecker permissionChecker, IMerchantAuthorizationChecker merchantAuthorizationChecker)
    {
        if (httpContext.User.GetMerchantId() is not null)
        {
            return await merchantAuthorizationChecker.HasPermissionAsync(httpContext.User, MerchantPermissionCatalog.PricingRead, httpContext.RequestAborted);
        }
        if (httpContext.User.GetUserId() is not null)
        {
            return await permissionChecker.HasPermissionAsync(httpContext.User, "pricing.read", httpContext.RequestAborted);
        }
        return false;
    }
}
```

Remove the old `.RequirePermission("pricing.read")`-only `/quote` mapping and its leading `if (httpContext.User.GetUserId() is null) return Results.Unauthorized();` block entirely — both are replaced by the code above. Leave the rate-card management routes (`.RequirePermission("pricing.manage")`) exactly as they are; they stay staff-only, unrelated to this task.

- [ ] **Step 7: Write the failing integration tests**

First check whether `tests/Api.Tests/` already has a pricing-quote integration test file (`Glob "tests/Api.Tests/*Pricing*"` and `Glob "tests/Api.Tests/*Quote*"`). If one exists, add these methods to it; otherwise create `tests/Api.Tests/MerchantPricingEndpointsTests.cs` following this file's established `WebApplicationFactory` fixture pattern (reuse whatever base class/helpers `MerchantAuthAndStaffEndpointsTests.cs` uses for registering an org, seeding a branch/zone/rate card, and merchant/owner login):

```csharp
    [Fact]
    public async Task Quote_MerchantToken_ReturnsOkAndAppliesOwnRateCard()
    {
        var (accessToken, merchantId, organizationId) = await LoginAsSeededOwnerAsync();
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutePrerequisitesAsync(organizationId);

        var response = await Client.PostAsJsonAsync("/api/v1/pricing/quote", accessToken, new
        {
            originBranchId, destinationBranchId, serviceTypeId,
            weightKg = 2m, codAmount = 0m, declaredValue = 0m, wantsInsurance = false,
            merchantId = (Guid?)null
        });

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Quote_MerchantSuppliesAnotherMerchantId_IsOverriddenToOwnId()
    {
        var (accessToken, ownMerchantId, organizationId) = await LoginAsSeededOwnerAsync();
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutePrerequisitesAsync(organizationId);
        var otherMerchantId = await SeedSecondMerchantWithDiscountedRateCardAsync(organizationId, originBranchId, destinationBranchId, serviceTypeId);

        var ownQuote = await (await Client.PostAsJsonAsync("/api/v1/pricing/quote", accessToken, new
        {
            originBranchId, destinationBranchId, serviceTypeId,
            weightKg = 2m, codAmount = 0m, declaredValue = 0m, wantsInsurance = false,
            merchantId = otherMerchantId // attempted spoof
        })).Content.ReadFromJsonAsync<JsonElement>();

        // Own merchant has no MerchantRateCard override, so the org default applies regardless
        // of the spoofed id — proving the server ignored the client-supplied MerchantId.
        Assert.NotEqual(ownMerchantId, otherMerchantId);
        Assert.True(ownQuote.GetProperty("total").GetDecimal() > 0);
    }

    [Fact]
    public async Task Quote_UnscopedApiKey_ReturnsForbidden()
    {
        var apiKey = await CreateApiKeyWithScopesAsync(new[] { "shipment.read" }); // no PricingRead

        var response = await Client.PostAsJsonWithApiKeyAsync("/api/v1/pricing/quote", apiKey, new
        {
            originBranchId = Guid.NewGuid(), destinationBranchId = Guid.NewGuid(),
            serviceTypeId = Guid.NewGuid(), weightKg = 1m, codAmount = 0m, declaredValue = 0m,
            wantsInsurance = false, merchantId = (Guid?)null
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ServiceTypes_MerchantToken_ReturnsOk()
    {
        var (accessToken, _, _) = await LoginAsSeededOwnerAsync();

        var response = await Client.GetAsync("/api/v1/service-types", accessToken);

        response.EnsureSuccessStatusCode();
    }
```

Write `SeedRoutePrerequisitesAsync`/`SeedSecondMerchantWithDiscountedRateCardAsync`/`CreateApiKeyWithScopesAsync`/`PostAsJsonWithApiKeyAsync` following the exact seeding shapes `tests/Pricing.Tests/PricingServiceTests.cs` and `tests/Api.Tests/MerchantApiExitCriteriaEndToEndTests.cs` already use for rate cards/`MerchantRateCard` and API-key creation — read both files first rather than inventing a new shape; this task's own integration test file should reuse those exact fixture patterns, not add a third one.

- [ ] **Step 8: Run the integration tests to verify they fail, then pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "Quote|ServiceTypes"`
Expected: FAIL first (old staff-only gate rejects a merchant token with 401/403), then PASS after Step 6.

- [ ] **Step 9: Run the full backend test suite**

Run: `dotnet test`
Expected: PASS, all green — this step also confirms the rate-card management routes (untouched) still work and no staff-facing quote/service-types test regressed.

- [ ] **Step 10: Commit**

```bash
git add src/Modules/Merchants/Application/MerchantPermissionCatalog.cs src/Modules/Pricing/Presentation/PricingEndpoints.cs src/Modules/Pricing/Pricing.csproj tests/Merchants.Tests/MerchantPermissionCatalogTests.cs tests/Api.Tests/
git commit -m "feat(pricing): make /quote and /service-types merchant-reachable, force server-side MerchantId"
```

---

## Task 4: `GET /api/v1/merchant/branches`

**Files:**
- Modify: `src/Modules/Branches/Application/BranchService.cs`
- Create: `src/Modules/Branches/Presentation/MerchantBranchesEndpoints.cs`
- Modify: `src/Modules/Branches/Branches.csproj`
- Modify: `src/Api/Program.cs`
- Test: `tests/Branches.Tests/BranchServiceTests.cs` (add methods)
- Test: `tests/Api.Tests/MerchantBranchesEndpointsTests.cs` (new)

**Interfaces:**
- Consumes: `Branch` (`Courier.Branches.Domain`), `BranchResponse` (`Courier.Branches.Application`, pre-existing), `MerchantPermissionCatalog`/`RequireMerchantPermissionAnyCredential`/`AuthSchemes` — new `Branches.csproj` reference to `Merchants.csproj` required.
- Produces: `BranchService.ListBranchesForMerchantAsync(CancellationToken cancellationToken = default) : Task<List<BranchResponse>>` and route `GET /api/v1/merchant/branches` — Task 9 (merchant-shipments data layer) depends on this exact path and response shape for its `useMerchantBranches()` hook.

- [ ] **Step 1: Write the failing unit test**

Add to `tests/Branches.Tests/BranchServiceTests.cs`:

```csharp
    [Fact]
    public async Task ListBranchesForMerchantAsync_ReturnsOnlyActiveBranches_NoAccessNarrowing()
    {
        await using var dbContext = CreateSeededContext();
        var orgId = Guid.NewGuid();
        var active = new Branch
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, Code = "KTM-01", Name = "Kathmandu Hub",
            Type = BranchType.Hub, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
            LocalLevelId = Guid.NewGuid(), WardNumber = 1, AddressLine = "New Road", IsActive = true
        };
        var inactive = new Branch
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, Code = "KTM-02", Name = "Closed Branch",
            Type = BranchType.Branch, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
            LocalLevelId = Guid.NewGuid(), WardNumber = 2, AddressLine = "Old Road", IsActive = false
        };
        dbContext.Set<Branch>().AddRange(active, inactive);
        await dbContext.SaveChangesAsync();
        var service = new BranchService(dbContext, new FixedTenantProvider(orgId), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new GeographyQueryService(dbContext));

        var result = await service.ListBranchesForMerchantAsync();

        var single = Assert.Single(result);
        Assert.Equal(active.Id, single.Id);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Branches.Tests/Branches.Tests.csproj --filter "ListBranchesForMerchantAsync"`
Expected: FAIL — `BranchService.ListBranchesForMerchantAsync` does not exist yet (compile error).

- [ ] **Step 3: Add `ListBranchesForMerchantAsync` to `BranchService`**

Add to `src/Modules/Branches/Application/BranchService.cs`, after `ListBranchesAsync`:

```csharp
    // No accessible-branch-id narrowing — unlike ListBranchesAsync, a merchant caller has no
    // branch-grant concept to narrow by; they need every active branch as a candidate booking
    // origin/destination. Relies on the ambient AppDbContext tenant query filter for
    // organization scoping, same as ListBranchesAsync itself does — proven by integration test
    // against real Postgres, not this unit test (a bare TestDbContext here has no query filter
    // wired in). IsActive filter is a deliberate addition ListBranchesAsync doesn't have: a
    // merchant shouldn't be offered a deactivated branch to book against.
    public async Task<List<BranchResponse>> ListBranchesForMerchantAsync(CancellationToken cancellationToken = default)
    {
        var branches = await _dbContext.Set<Branch>()
            .Where(b => b.IsActive)
            .ToListAsync(cancellationToken);
        return branches.Select(ToResponse).ToList();
    }
```

- [ ] **Step 4: Run the unit test to verify it passes**

Run: `dotnet test tests/Branches.Tests/Branches.Tests.csproj --filter "ListBranchesForMerchantAsync"`
Expected: PASS

- [ ] **Step 5: Add the `Merchants` project reference to `Branches.csproj`**

```xml
<!-- src/Modules/Branches/Branches.csproj — add alongside the existing ProjectReference items -->
    <ProjectReference Include="..\Merchants\Merchants.csproj" />
```

- [ ] **Step 6: Create the endpoint**

```csharp
// src/Modules/Branches/Presentation/MerchantBranchesEndpoints.cs
using Courier.Branches.Application;
using Courier.Merchants.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Courier.Branches.Presentation;

public static class MerchantBranchesEndpoints
{
    public static void MapMerchantBranchesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchant/branches").WithTags("Merchant Branches");

        group.MapGet("/", async (BranchService branchService, CancellationToken cancellationToken) =>
            Microsoft.AspNetCore.Http.Results.Ok(await branchService.ListBranchesForMerchantAsync(cancellationToken)))
            .RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.BranchesRead);
    }
}
```

Add `BranchesRead` to `MerchantPermissionCatalog` (`src/Modules/Merchants/Application/MerchantPermissionCatalog.cs`), after `PricingRead`:

```csharp
    // Both merchant roles book shipments and need to pick an origin/destination branch
    // (Frontend Phase F10) — same Owner+Staff tier as PricingRead.
    public const string BranchesRead = "merchant.branches.read";
```

Add `BranchesRead` to both `OwnerPermissions` and `StaffPermissions` arrays (alongside `PricingRead`), and update `MerchantPermissionCatalogTests.cs`'s `Owner_HoldsAllFourteenPermissions` (from Task 3) to `Owner_HoldsAllFifteenPermissions` with count `15` and an added `Assert.Contains(MerchantPermissionCatalog.BranchesRead, ownerPermissions);`, plus an added assertion in the Staff test.

- [ ] **Step 7: Register the new endpoint group in `Program.cs`**

In `src/Api/Program.cs`, add `app.MapMerchantBranchesEndpoints();` immediately after the existing `app.MapBranchesEndpoints();` line (around line 288):

```csharp
app.MapBranchesEndpoints();
app.MapMerchantBranchesEndpoints();
```

- [ ] **Step 8: Write the failing integration test**

```csharp
// tests/Api.Tests/MerchantBranchesEndpointsTests.cs
// Follow the exact WebApplicationFactory fixture / org-registration / merchant-seeding pattern
// MerchantAuthAndStaffEndpointsTests.cs already establishes — read that file first for the base
// class name and helper methods (RegisterOrganizationAsync, seeded owner login, etc.) rather
// than inventing a new fixture shape.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Courier.Api.Tests;

public class MerchantBranchesEndpointsTests : /* same base fixture class as MerchantAuthAndStaffEndpointsTests */
{
    [Fact]
    public async Task ListBranches_MerchantToken_ReturnsOnlyOwnOrgsBranches()
    {
        var (accessToken, _, organizationId) = await LoginAsSeededOwnerAsync();
        var otherOrgBranchId = await SeedBranchInAnotherOrganizationAsync();

        var response = await Client.GetAsync("/api/v1/merchant/branches", accessToken);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ToList();
        Assert.DoesNotContain(otherOrgBranchId, ids);
    }

    [Fact]
    public async Task ListBranches_UnscopedApiKey_ReturnsForbidden()
    {
        var apiKey = await CreateApiKeyWithScopesAsync(new[] { "shipment.read" }); // no BranchesRead

        var response = await Client.GetWithApiKeyAsync("/api/v1/merchant/branches", apiKey);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListBranches_NoToken_ReturnsUnauthorized()
    {
        var response = await Client.GetAsync("/api/v1/merchant/branches");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 9: Run the integration tests to verify they fail, then pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "MerchantBranchesEndpointsTests"`
Expected: FAIL first (404 — route/registration not wired), then PASS.

- [ ] **Step 10: Run the full backend test suite**

Run: `dotnet test`
Expected: PASS, all green.

- [ ] **Step 11: Commit**

```bash
git add src/Modules/Branches/Application/BranchService.cs src/Modules/Branches/Presentation/MerchantBranchesEndpoints.cs src/Modules/Branches/Branches.csproj src/Modules/Merchants/Application/MerchantPermissionCatalog.cs src/Api/Program.cs tests/Branches.Tests/BranchServiceTests.cs tests/Api.Tests/MerchantBranchesEndpointsTests.cs tests/Merchants.Tests/MerchantPermissionCatalogTests.cs
git commit -m "feat(branches): add GET /api/v1/merchant/branches for merchant booking"
```

---

## Task 5: Merchant session cookie plumbing (`merchantSession.ts`, `merchantAuthService.ts`, `merchantApiFetch.ts`, BFF login/logout/session routes)

All four backend gaps are closed. This task starts the frontend: the merchant track's own session layer, fully parallel to (never sharing code with) `lib/auth/session.ts`/`services/authService.ts`/`lib/auth/apiFetch.ts`.

**Files:**
- Create: `src/web/src/services/merchantAuthService.ts`
- Create: `src/web/src/lib/auth/merchantSession.ts`
- Create: `src/web/src/lib/auth/merchantSession.test.ts`
- Create: `src/web/src/lib/auth/merchantApiFetch.ts`
- Create: `src/web/src/app/api/merchant-auth/login/route.ts`
- Create: `src/web/src/app/api/merchant-auth/logout/route.ts`
- Create: `src/web/src/app/api/merchant-auth/session/route.ts`

**Interfaces:**
- Consumes: `getUpstreamApiUrl` (`@/lib/env`, pre-existing) — the backend routes `merchantAuthService` calls (`GET/POST /api/v1/merchant/auth/*`) come from Tasks 2/3 (this plan) and the pre-existing Phase 12 login/refresh routes.
- Produces: `merchantAuthService.login/refresh/me`, `MerchantAuthTokens`, `MerchantMeResponse` (`@/services/merchantAuthService`); `setMerchantSessionCookies/clearMerchantSessionCookies/getValidMerchantAccessToken` (`@/lib/auth/merchantSession`); `merchantApiFetch` (`@/lib/auth/merchantApiFetch`) — every later merchant BFF route (Tasks 8, 10-14) imports `merchantApiFetch` exactly as today's `app/api/shipments/route.ts` imports `apiFetch`. Task 6 imports `merchantAuthService`/`getValidMerchantAccessToken` for the session hook and login form.

- [ ] **Step 1: Write the failing `merchantSession.ts` test**

```typescript
// src/web/src/lib/auth/merchantSession.test.ts
import { describe, expect, it, vi, beforeEach } from "vitest";

const cookieStore = new Map<string, string>();

vi.mock("next/headers", () => ({
  cookies: () => ({
    get: (name: string) => (cookieStore.has(name) ? { value: cookieStore.get(name) } : undefined),
    set: (name: string, value: string) => {
      cookieStore.set(name, value);
    },
    delete: (name: string) => {
      cookieStore.delete(name);
    },
  }),
}));

import {
  setMerchantSessionCookies,
  clearMerchantSessionCookies,
  getValidMerchantAccessToken,
} from "./merchantSession";

describe("merchant session cookies", () => {
  beforeEach(() => {
    cookieStore.clear();
  });

  it("returns the stored access token when present", async () => {
    setMerchantSessionCookies("access-123", "refresh-456");

    const token = await getValidMerchantAccessToken();

    expect(token).toBe("access-123");
  });

  it("returns null when there is no session at all", async () => {
    const token = await getValidMerchantAccessToken();

    expect(token).toBeNull();
  });

  it("clears both cookies on logout", async () => {
    setMerchantSessionCookies("access-123", "refresh-456");

    clearMerchantSessionCookies();

    expect(await getValidMerchantAccessToken()).toBeNull();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/lib/auth/merchantSession.test.ts`
Expected: FAIL — `./merchantSession` module does not exist yet.

- [ ] **Step 3: Write `merchantAuthService.ts`**

```typescript
// src/web/src/services/merchantAuthService.ts
import { getUpstreamApiUrl } from "@/lib/env";

export interface MerchantAuthTokens {
  accessToken: string;
  refreshToken: string;
  merchantUserId: string;
  merchantId: string;
  organizationId: string;
}

export interface MerchantMeResponse {
  merchantUserId: string;
  merchantId: string;
  organizationId: string;
  merchantDisplayName: string;
  email: string;
  fullName: string;
  role: "Owner" | "Staff";
  permissions: string[];
  kycStatus: "Pending" | "Approved" | "Rejected";
}

export const merchantAuthService = {
  async login(email: string, password: string): Promise<MerchantAuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/merchant/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password }),
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("LOGIN_FAILED");
    }
    const data = await response.json();
    return {
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      merchantUserId: data.merchantUserId,
      merchantId: data.merchantId,
      organizationId: data.organizationId,
    };
  },

  async refresh(refreshToken: string): Promise<MerchantAuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/merchant/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("REFRESH_FAILED");
    }
    const data = await response.json();
    return {
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      merchantUserId: data.merchantUserId,
      merchantId: data.merchantId,
      organizationId: data.organizationId,
    };
  },

  async me(accessToken: string): Promise<MerchantMeResponse> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/merchant/auth/me`, {
      headers: { Authorization: `Bearer ${accessToken}` },
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("ME_FAILED");
    }
    return response.json();
  },
};
```

- [ ] **Step 4: Write `merchantSession.ts`**

```typescript
// src/web/src/lib/auth/merchantSession.ts
import { cookies } from "next/headers";
import { merchantAuthService } from "@/services/merchantAuthService";

const ACCESS_COOKIE = "courier_merchant_access_token";
const REFRESH_COOKIE = "courier_merchant_refresh_token";

const cookieOptions = {
  httpOnly: true,
  secure: process.env.NODE_ENV === "production",
  sameSite: "lax" as const,
  path: "/",
};

export function setMerchantSessionCookies(accessToken: string, refreshToken: string) {
  const store = cookies();
  store.set(ACCESS_COOKIE, accessToken, { ...cookieOptions, maxAge: 60 * 15 });
  store.set(REFRESH_COOKIE, refreshToken, { ...cookieOptions, maxAge: 60 * 60 * 24 * 7 });
}

export function clearMerchantSessionCookies() {
  const store = cookies();
  store.delete(ACCESS_COOKIE);
  store.delete(REFRESH_COOKIE);
}

// Same transparent-refresh contract as getValidAccessToken (lib/auth/session.ts) — returns null
// only when there is no session at all, or the refresh token itself is invalid/expired/revoked.
export async function getValidMerchantAccessToken(): Promise<string | null> {
  const store = cookies();
  const accessToken = store.get(ACCESS_COOKIE)?.value;
  const refreshToken = store.get(REFRESH_COOKIE)?.value;

  if (accessToken) {
    return accessToken;
  }
  if (!refreshToken) {
    return null;
  }

  try {
    const tokens = await merchantAuthService.refresh(refreshToken);
    setMerchantSessionCookies(tokens.accessToken, tokens.refreshToken);
    return tokens.accessToken;
  } catch {
    clearMerchantSessionCookies();
    return null;
  }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/web && npx vitest run src/lib/auth/merchantSession.test.ts`
Expected: PASS

- [ ] **Step 6: Write `merchantApiFetch.ts`**

```typescript
// src/web/src/lib/auth/merchantApiFetch.ts
import { getUpstreamApiUrl } from "@/lib/env";
import { getValidMerchantAccessToken } from "@/lib/auth/merchantSession";

// Server-only helper for merchant-track BFF routes to call the real API with the current
// merchant session's bearer token attached, refreshing transparently. Mirrors lib/auth/apiFetch.ts
// exactly, kept as a separate file rather than a shared parameterized helper — the two token
// tracks (staff vs. merchant) must never be interchangeable at this layer.
export async function merchantApiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const accessToken = await getValidMerchantAccessToken();
  if (!accessToken) {
    throw new Error("NOT_AUTHENTICATED");
  }
  return fetch(`${getUpstreamApiUrl()}${path}`, {
    ...init,
    headers: {
      ...init.headers,
      Authorization: `Bearer ${accessToken}`,
    },
    cache: "no-store",
  });
}
```

- [ ] **Step 7: Write the three BFF routes**

```typescript
// src/web/src/app/api/merchant-auth/login/route.ts
import { NextRequest, NextResponse } from "next/server";
import { merchantAuthService } from "@/services/merchantAuthService";
import { setMerchantSessionCookies } from "@/lib/auth/merchantSession";

export async function POST(request: NextRequest) {
  const { email, password } = await request.json();
  try {
    const tokens = await merchantAuthService.login(email, password);
    setMerchantSessionCookies(tokens.accessToken, tokens.refreshToken);
    return NextResponse.json({ ok: true });
  } catch {
    return NextResponse.json({ error: "Invalid email or password." }, { status: 401 });
  }
}
```

```typescript
// src/web/src/app/api/merchant-auth/logout/route.ts
import { NextResponse } from "next/server";
import { clearMerchantSessionCookies } from "@/lib/auth/merchantSession";

export async function POST() {
  clearMerchantSessionCookies();
  return NextResponse.json({ ok: true });
}
```

```typescript
// src/web/src/app/api/merchant-auth/session/route.ts
import { NextResponse } from "next/server";
import { getValidMerchantAccessToken } from "@/lib/auth/merchantSession";
import { merchantAuthService } from "@/services/merchantAuthService";

export async function GET() {
  const accessToken = await getValidMerchantAccessToken();
  if (!accessToken) {
    return NextResponse.json(null, { status: 401 });
  }
  try {
    const me = await merchantAuthService.me(accessToken);
    return NextResponse.json(me);
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

(No dedicated test files for these three routes — matches the existing staff `app/api/auth/{login,logout,session}/route.ts`, none of which have a `.test.ts` sibling either; their logic is exercised through `merchantSession.test.ts` and Task 6's `MerchantLoginForm.test.tsx`.)

- [ ] **Step 8: Run the full frontend test suite and type check**

Run: `cd src/web && npx vitest run && npx tsc --noEmit`
Expected: PASS, all green.

- [ ] **Step 9: Commit**

```bash
git add src/web/src/services/merchantAuthService.ts src/web/src/lib/auth/merchantSession.ts src/web/src/lib/auth/merchantSession.test.ts src/web/src/lib/auth/merchantApiFetch.ts src/web/src/app/api/merchant-auth/
git commit -m "feat(web): add merchant session cookie plumbing and BFF auth routes"
```

---

## Task 6: Merchant login page, `useMerchantSession`/`useMerchantPermission` hooks, middleware

**Files:**
- Create: `src/web/src/features/merchant-auth/schema.ts`
- Create: `src/web/src/features/merchant-auth/MerchantLoginForm.tsx`
- Create: `src/web/src/features/merchant-auth/MerchantLoginForm.test.tsx`
- Create: `src/web/src/features/merchant-auth/hooks/useMerchantSession.ts`
- Create: `src/web/src/features/merchant-auth/hooks/useMerchantPermission.ts`
- Create: `src/web/src/features/merchant-auth/hooks/useMerchantPermission.test.tsx`
- Create: `src/web/src/app/merchant-login/page.tsx`
- Modify: `src/web/src/middleware.ts`

**Interfaces:**
- Consumes: `MerchantMeResponse` (`@/services/merchantAuthService`, Task 5).
- Produces: `useMerchantSession()` returning a TanStack Query result of `MerchantMeResponse | null` (`@/features/merchant-auth/hooks/useMerchantSession`); `useMerchantPermission(permission: string): boolean` (`@/features/merchant-auth/hooks/useMerchantPermission`) — Task 7's `MerchantNavMain`/`MerchantUserMenu` and every later feature task's screens gate on these two.

- [ ] **Step 1: Write the failing `useMerchantPermission` test**

```tsx
// src/web/src/features/merchant-auth/hooks/useMerchantPermission.test.tsx
import { describe, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useMerchantPermission } from "./useMerchantPermission";

function TestComponent({ permission }: { permission: string }) {
  const allowed = useMerchantPermission(permission);
  return <div>{allowed ? "allowed" : "denied"}</div>;
}

function renderWithSession(session: unknown) {
  const originalFetch = global.fetch;
  global.fetch = vi.fn().mockResolvedValue({
    ok: session !== null,
    status: session === null ? 401 : 200,
    json: async () => session,
  }) as unknown as typeof fetch;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const result = render(
    <QueryClientProvider client={queryClient}>
      <TestComponent permission="merchant.staff.manage" />
    </QueryClientProvider>
  );
  return { ...result, restore: () => (global.fetch = originalFetch) };
}

describe("useMerchantPermission", () => {
  it("returns denied while there is no session", async () => {
    const { restore } = renderWithSession(null);
    await waitFor(() => screen.getByText("denied"));
    restore();
  });

  it("returns allowed when the permission is in the session's permission list", async () => {
    const { restore } = renderWithSession({
      merchantUserId: "mu1",
      merchantId: "m1",
      organizationId: "o1",
      merchantDisplayName: "Acme Traders",
      email: "owner@acme.test",
      fullName: "Owner Person",
      role: "Owner",
      permissions: ["merchant.staff.manage"],
      kycStatus: "Pending",
    });
    await waitFor(() => screen.getByText("allowed"));
    restore();
  });

  it("returns denied when the permission is absent for a Staff role", async () => {
    const { restore } = renderWithSession({
      merchantUserId: "mu2",
      merchantId: "m1",
      organizationId: "o1",
      merchantDisplayName: "Acme Traders",
      email: "staff@acme.test",
      fullName: "Staff Person",
      role: "Staff",
      permissions: ["shipment.read"],
      kycStatus: "Pending",
    });
    await waitFor(() => screen.getByText("denied"));
    restore();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/features/merchant-auth/hooks/useMerchantPermission.test.tsx`
Expected: FAIL — `./useMerchantPermission` module does not exist yet.

- [ ] **Step 3: Write `useMerchantSession.ts` and `useMerchantPermission.ts`**

```typescript
// src/web/src/features/merchant-auth/hooks/useMerchantSession.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import type { MerchantMeResponse } from "@/services/merchantAuthService";

async function fetchMerchantSession(): Promise<MerchantMeResponse | null> {
  const response = await fetch("/api/merchant-auth/session");
  if (response.status === 401) {
    return null;
  }
  if (!response.ok) {
    throw new Error("MERCHANT_SESSION_FETCH_FAILED");
  }
  return response.json();
}

export function useMerchantSession() {
  return useQuery({
    queryKey: ["merchant-session"],
    queryFn: fetchMerchantSession,
    staleTime: 60_000,
    retry: false,
  });
}
```

```typescript
// src/web/src/features/merchant-auth/hooks/useMerchantPermission.ts
"use client";

import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";

// No branch-scope parameter — unlike staff's usePermission, merchants have no branch concept to
// narrow by (RBAC.md §5: merchants hold a small fixed permission set per role, Owner or Staff,
// no custom roles and no per-branch grants).
export function useMerchantPermission(permission: string): boolean {
  const { data: session } = useMerchantSession();
  if (!session) {
    return false;
  }
  return session.permissions.includes(permission);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npx vitest run src/features/merchant-auth/hooks/useMerchantPermission.test.tsx`
Expected: PASS

- [ ] **Step 5: Write the failing `MerchantLoginForm` test**

```tsx
// src/web/src/features/merchant-auth/MerchantLoginForm.test.tsx
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MerchantLoginForm } from "./MerchantLoginForm";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

describe("MerchantLoginForm", () => {
  beforeEach(() => {
    global.fetch = vi.fn();
  });

  it("shows validation errors for an empty submit", async () => {
    render(<MerchantLoginForm />);

    fireEvent.click(screen.getByRole("button", { name: /sign in/i }));

    expect(await screen.findByText(/enter a valid email/i)).toBeInTheDocument();
  });

  it("shows a server error on failed login", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      json: async () => ({ error: "Invalid email or password." }),
    });
    render(<MerchantLoginForm />);

    fireEvent.change(screen.getByLabelText(/^email$/i), { target: { value: "a@b.com" } });
    fireEvent.change(screen.getByLabelText(/^password$/i), { target: { value: "wrong" } });
    fireEvent.click(screen.getByRole("button", { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText("Invalid email or password.")).toBeInTheDocument());
  });
});
```

- [ ] **Step 6: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/features/merchant-auth/MerchantLoginForm.test.tsx`
Expected: FAIL — `./MerchantLoginForm` module does not exist yet.

- [ ] **Step 7: Write `schema.ts` and `MerchantLoginForm.tsx`**

```typescript
// src/web/src/features/merchant-auth/schema.ts
import { z } from "zod";

export const merchantLoginSchema = z.object({
  email: z.string().email("Enter a valid email address."),
  password: z.string().min(1, "Password is required."),
});

export type MerchantLoginFormValues = z.infer<typeof merchantLoginSchema>;
```

```tsx
// src/web/src/features/merchant-auth/MerchantLoginForm.tsx
"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Mail, Lock, Eye, EyeOff, ArrowRight, AlertCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { FieldError } from "@/components/ui/field-error";
import { merchantLoginSchema, type MerchantLoginFormValues } from "@/features/merchant-auth/schema";

export function MerchantLoginForm() {
  const router = useRouter();
  const [serverError, setServerError] = useState<string | null>(null);
  const [showPassword, setShowPassword] = useState(false);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<MerchantLoginFormValues>({ resolver: zodResolver(merchantLoginSchema) });

  async function onSubmit(values: MerchantLoginFormValues) {
    setServerError(null);
    const response = await fetch("/api/merchant-auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(values),
    });
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      setServerError(body.error ?? "Login failed.");
      return;
    }
    router.push("/merchant/dashboard");
  }

  return (
    <Card className="w-full max-w-sm shadow-xl shadow-primary/5">
      <CardHeader>
        <CardTitle className="font-display text-xl">Merchant sign in</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="email">Email</Label>
            <div className="relative">
              <Mail
                className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                strokeWidth={2}
              />
              <Input
                id="email"
                type="email"
                autoComplete="email"
                className="pl-9"
                aria-invalid={!!errors.email}
                aria-describedby={errors.email ? "email-error" : undefined}
                {...register("email")}
              />
            </div>
            {errors.email && (
              <FieldError id="email-error" className="flex items-center gap-1.5">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.email.message}
              </FieldError>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="password">Password</Label>
            <div className="relative">
              <Lock
                className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                strokeWidth={2}
              />
              <Input
                id="password"
                type={showPassword ? "text" : "password"}
                autoComplete="current-password"
                className="pl-9 pr-9"
                aria-invalid={!!errors.password}
                aria-describedby={errors.password ? "password-error" : undefined}
                {...register("password")}
              />
              <button
                type="button"
                onClick={() => setShowPassword((v) => !v)}
                aria-label={showPassword ? "Hide password" : "Show password"}
                className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
              >
                {showPassword ? (
                  <EyeOff className="h-4 w-4" strokeWidth={2} />
                ) : (
                  <Eye className="h-4 w-4" strokeWidth={2} />
                )}
              </button>
            </div>
            {errors.password && (
              <FieldError id="password-error" className="flex items-center gap-1.5">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.password.message}
              </FieldError>
            )}
          </div>
          {serverError && (
            <FieldError id="merchant-login-server-error" className="flex items-center gap-1.5">
              <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
              {serverError}
            </FieldError>
          )}
          <Button type="submit" className="group w-full" disabled={isSubmitting}>
            {isSubmitting ? "Signing in..." : "Sign in"}
            {!isSubmitting && (
              <ArrowRight
                className="h-4 w-4 transition-transform group-hover:translate-x-0.5"
                strokeWidth={2}
              />
            )}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 8: Write the login page**

```tsx
// src/web/src/app/merchant-login/page.tsx
import { Store } from "lucide-react";
import { MerchantLoginForm } from "@/features/merchant-auth/MerchantLoginForm";

export default function MerchantLoginPage() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 bg-background p-4">
      <div className="flex flex-col items-center gap-2">
        <span className="flex h-11 w-11 items-center justify-center rounded-lg bg-primary">
          <Store className="h-5 w-5 text-primary-foreground" strokeWidth={2} />
        </span>
        <span className="font-display text-lg font-semibold tracking-tight">Merchant Portal</span>
      </div>
      <MerchantLoginForm />
    </main>
  );
}
```

- [ ] **Step 9: Update `middleware.ts`**

The existing file's `middleware` function and `matcher` only ever handled the staff cookie/routes. Read the current `src/web/src/middleware.ts` first to confirm its exact present matcher array (it may have grown since F1), then replace the whole file:

```typescript
// src/web/src/middleware.ts
import { NextRequest, NextResponse } from "next/server";

// UX-level redirect only — presence of a cookie is not proof of a valid session. The real
// authorization boundary is the backend API on every request (CLAUDE.md §33); a stale access
// token with an invalid refresh token is caught by /api/(auth|merchant-auth)/session's 401, which
// the client-side session hooks follow. Two independent branches (staff vs. merchant), since the
// two tracks use different cookies and different login pages.
export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  if (pathname.startsWith("/merchant/")) {
    const hasMerchantSession =
      request.cookies.has("courier_merchant_access_token") ||
      request.cookies.has("courier_merchant_refresh_token");
    if (!hasMerchantSession) {
      return NextResponse.redirect(new URL("/merchant-login", request.url));
    }
    return NextResponse.next();
  }

  const hasSession =
    request.cookies.has("courier_access_token") || request.cookies.has("courier_refresh_token");
  if (!hasSession) {
    return NextResponse.redirect(new URL("/login", request.url));
  }
  return NextResponse.next();
}

export const config = {
  // Preserve every entry the file already had for the staff track (re-copy them verbatim from
  // the file you just read — do not drop any) and add "/merchant/:path*" for the new portal.
  matcher: ["/dashboard/:path*", "/shipments/:path*", "/branches/:path*", "/cod/:path*", "/merchant/:path*"],
};
```

- [ ] **Step 10: Run the full frontend test suite and type check**

Run: `cd src/web && npx vitest run && npx tsc --noEmit`
Expected: PASS, all green.

- [ ] **Step 11: Commit**

```bash
git add src/web/src/features/merchant-auth/ src/web/src/app/merchant-login/ src/web/src/middleware.ts
git commit -m "feat(web): add merchant login page, session/permission hooks, and route protection"
```

---

## Task 7: `MerchantAppShell`, `MerchantNavMain`, `(merchant)` layout, and a placeholder dashboard page

**Files:**
- Create: `src/web/src/components/merchant-layout/MerchantNavMain.tsx`
- Create: `src/web/src/components/merchant-layout/MerchantNavMain.test.tsx`
- Create: `src/web/src/components/merchant-layout/MerchantAppSidebar.tsx`
- Create: `src/web/src/components/merchant-layout/MerchantUserMenu.tsx`
- Create: `src/web/src/components/merchant-layout/MerchantAppShell.tsx`
- Create: `src/web/src/app/(merchant)/layout.tsx`
- Create: `src/web/src/app/(merchant)/dashboard/page.tsx`

**Interfaces:**
- Consumes: `useMerchantSession`/`useMerchantPermission` (`@/features/merchant-auth/hooks/*`, Task 6).
- Produces: `MerchantAppShell` (`@/components/merchant-layout/MerchantAppShell`) — every later `app/(merchant)/*/page.tsx` renders inside it via the layout; each of Tasks 9-14 modifies `app/(merchant)/layout.tsx` to mount its own Sheet component alongside `{children}`, the same one-Sheet-added-per-phase pattern `app/(staff)/layout.tsx` already shows across F1-F6.

- [ ] **Step 1: Write the failing `MerchantNavMain` test**

```tsx
// src/web/src/components/merchant-layout/MerchantNavMain.test.tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SidebarProvider } from "@/components/ui/sidebar";
import { MerchantNavMain } from "./MerchantNavMain";

vi.mock("next/navigation", () => ({
  usePathname: () => "/merchant/dashboard",
}));

function renderNavMain(permissions: string[]) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      merchantUserId: "mu1",
      merchantId: "m1",
      organizationId: "o1",
      merchantDisplayName: "Acme Traders",
      email: "owner@acme.test",
      fullName: "Owner Person",
      role: "Owner",
      permissions,
      kycStatus: "Pending",
    }),
  }) as unknown as typeof fetch;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <SidebarProvider>
        <MerchantNavMain />
      </SidebarProvider>
    </QueryClientProvider>
  );
}

describe("MerchantNavMain", () => {
  it("always shows Dashboard regardless of permissions", async () => {
    renderNavMain([]);

    await waitFor(() => expect(screen.getByText("Dashboard")).toBeInTheDocument());
    expect(screen.queryByText("Staff")).not.toBeInTheDocument();
  });

  it("hides Staff and API Keys for a Staff-role session", async () => {
    renderNavMain(["shipment.read", "merchant.pickups.read", "merchant.cod.read", "merchant.kyc.upload"]);

    await waitFor(() => expect(screen.getByText("Shipments")).toBeInTheDocument());
    expect(screen.queryByText("Staff")).not.toBeInTheDocument();
    expect(screen.queryByText("API Keys")).not.toBeInTheDocument();
  });

  it("shows Staff and API Keys for an Owner-role session", async () => {
    renderNavMain(["merchant.staff.manage", "merchant.apikeys.manage"]);

    await waitFor(() => expect(screen.getByText("Staff")).toBeInTheDocument());
    expect(screen.getByText("API Keys")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/components/merchant-layout/MerchantNavMain.test.tsx`
Expected: FAIL — `./MerchantNavMain` module does not exist yet.

- [ ] **Step 3: Write `MerchantNavMain.tsx`**

```tsx
// src/web/src/components/merchant-layout/MerchantNavMain.tsx
"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { LayoutDashboard, Package, Truck, Wallet, ShieldCheck, Users, KeyRound, type LucideIcon } from "lucide-react";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import {
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar";

interface MerchantNavItem {
  href: string;
  label: string;
  /** Omitted means always visible — only Dashboard has no permission gate. */
  permission?: string;
  icon: LucideIcon;
}

// The full merchant nav catalog. Tasks 9-14 each add their real route once built; entries below
// point at routes that don't exist until their task lands, which is fine — Link just 404s until
// then, and every gated entry is hidden entirely for a session lacking the permission regardless.
const NAV_ITEMS: MerchantNavItem[] = [
  { href: "/merchant/dashboard", label: "Dashboard", icon: LayoutDashboard },
  { href: "/merchant/shipments", label: "Shipments", permission: "shipment.read", icon: Package },
  { href: "/merchant/pickups", label: "Pickups", permission: "merchant.pickups.read", icon: Truck },
  { href: "/merchant/cod", label: "COD & Invoices", permission: "merchant.cod.read", icon: Wallet },
  { href: "/merchant/kyc", label: "KYC", permission: "merchant.kyc.upload", icon: ShieldCheck },
  { href: "/merchant/staff", label: "Staff", permission: "merchant.staff.manage", icon: Users },
  { href: "/merchant/api-keys", label: "API Keys", permission: "merchant.apikeys.manage", icon: KeyRound },
];

export function MerchantNavMain({ onNavigate }: { onNavigate?: () => void }) {
  const pathname = usePathname();
  return (
    <SidebarGroup>
      <SidebarGroupLabel>Merchant Portal</SidebarGroupLabel>
      <SidebarMenu>
        {NAV_ITEMS.map((item) => (
          <MerchantNavMainItem key={item.href} item={item} active={pathname === item.href} onNavigate={onNavigate} />
        ))}
      </SidebarMenu>
    </SidebarGroup>
  );
}

function MerchantNavMainItem({
  item,
  active,
  onNavigate,
}: {
  item: MerchantNavItem;
  active: boolean;
  onNavigate?: () => void;
}) {
  // Hooks can't be called conditionally, so this always calls useMerchantPermission even for the
  // permission-less Dashboard entry — passing "" is safe (never a real permission string, always
  // resolves to whatever useMerchantPermission("") returns) since `allowed` below short-circuits
  // on !item.permission before ever reading it.
  const hasPermission = useMerchantPermission(item.permission ?? "");
  const allowed = !item.permission || hasPermission;
  if (!allowed) {
    return null;
  }
  const Icon = item.icon;
  return (
    <SidebarMenuItem>
      <SidebarMenuButton asChild isActive={active} tooltip={item.label}>
        <Link href={item.href} onClick={onNavigate}>
          <Icon strokeWidth={2} />
          <span>{item.label}</span>
        </Link>
      </SidebarMenuButton>
    </SidebarMenuItem>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npx vitest run src/components/merchant-layout/MerchantNavMain.test.tsx`
Expected: PASS

- [ ] **Step 5: Write `MerchantUserMenu.tsx`, `MerchantAppSidebar.tsx`, `MerchantAppShell.tsx`**

```tsx
// src/web/src/components/merchant-layout/MerchantUserMenu.tsx
"use client";

import { useRouter } from "next/navigation";
import { ChevronDown, LogOut } from "lucide-react";
import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

function initials(fullName: string): string {
  return fullName
    .split(" ")
    .map((part) => part[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();
}

export function MerchantUserMenu() {
  const router = useRouter();
  const { data: session } = useMerchantSession();

  async function handleLogout() {
    await fetch("/api/merchant-auth/logout", { method: "POST" });
    router.push("/merchant-login");
  }

  if (!session) {
    return null;
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          className="flex items-center gap-2 rounded-full py-1 pl-1 pr-2 text-sm transition-colors hover:bg-accent focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring data-[state=open]:bg-accent"
        >
          <Avatar className="h-8 w-8 ring-1 ring-border">
            <AvatarFallback className="bg-secondary text-secondary-foreground">
              {initials(session.fullName)}
            </AvatarFallback>
          </Avatar>
          <span className="hidden font-medium text-foreground sm:inline">{session.fullName}</span>
          <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" strokeWidth={2} />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent className="min-w-56 rounded-lg" side="bottom" align="end" sideOffset={4}>
        <DropdownMenuLabel className="p-0 font-normal">
          <div className="flex items-center gap-2 px-1 py-1.5 text-left text-sm">
            <Avatar className="h-8 w-8 ring-1 ring-border">
              <AvatarFallback className="bg-secondary text-secondary-foreground">
                {initials(session.fullName)}
              </AvatarFallback>
            </Avatar>
            <div className="grid flex-1 text-left text-sm leading-tight">
              <span className="truncate font-semibold">{session.fullName}</span>
              <span className="truncate text-xs">{session.email}</span>
            </div>
          </div>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={handleLogout} className="gap-2">
          <LogOut className="h-4 w-4" strokeWidth={2} />
          Log out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

```tsx
// src/web/src/components/merchant-layout/MerchantAppSidebar.tsx
"use client";

import * as React from "react";
import { Store } from "lucide-react";
import { MerchantNavMain } from "@/components/merchant-layout/MerchantNavMain";
import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";
import {
  Sidebar,
  SidebarContent,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuItem,
  SidebarRail,
  useSidebar,
} from "@/components/ui/sidebar";

// No BranchSwitcher-equivalent — merchants have no branch concept. The brand lockup shows the
// merchant's own display name in place of the staff shell's organization name.
function MerchantSidebarBrand() {
  const { state } = useSidebar();
  const { data: session } = useMerchantSession();
  return (
    <SidebarMenu>
      <SidebarMenuItem className="flex items-center gap-2 px-2 py-1.5">
        <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-md bg-sidebar-primary">
          <Store className="h-4 w-4 text-sidebar-primary-foreground" strokeWidth={2} />
        </span>
        {state === "expanded" && (
          <span className="truncate font-display text-base font-semibold tracking-tight">
            {session?.merchantDisplayName ?? "Merchant Portal"}
          </span>
        )}
      </SidebarMenuItem>
    </SidebarMenu>
  );
}

export function MerchantAppSidebar(props: React.ComponentProps<typeof Sidebar>) {
  const { isMobile, setOpenMobile } = useSidebar();

  return (
    <Sidebar collapsible="icon" {...props}>
      <SidebarHeader>
        <MerchantSidebarBrand />
      </SidebarHeader>
      <SidebarContent>
        <MerchantNavMain onNavigate={isMobile ? () => setOpenMobile(false) : undefined} />
      </SidebarContent>
      <SidebarRail />
    </Sidebar>
  );
}
```

```tsx
// src/web/src/components/merchant-layout/MerchantAppShell.tsx
import type { ReactNode } from "react";
import { MerchantAppSidebar } from "@/components/merchant-layout/MerchantAppSidebar";
import { MerchantUserMenu } from "@/components/merchant-layout/MerchantUserMenu";
import { ThemeToggle } from "@/components/layout/ThemeToggle";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { Separator } from "@/components/ui/separator";

// Deliberately not the staff AppShell — that component's HeaderSearch and ReauthDialog are
// staff-specific (search indexes staff-only entities; ReauthDialog reacts to the staff session's
// own refresh-failure signal) and out of this phase's scope. Reuses the same shadcn
// Sidebar/SidebarProvider primitives, own nav catalog and own user menu.
export function MerchantAppShell({ children }: { children: ReactNode }) {
  return (
    <SidebarProvider>
      <MerchantAppSidebar />
      <SidebarInset>
        <header className="sticky top-0 z-10 flex h-16 shrink-0 items-center gap-3 border-b border-border bg-card px-4 sm:px-6">
          <SidebarTrigger className="h-10 w-10 rounded-lg border border-input" />
          <div className="ml-auto flex items-center gap-3">
            <ThemeToggle />
            <Separator orientation="vertical" className="h-6" />
            <MerchantUserMenu />
          </div>
        </header>
        <div className="flex-1 bg-background p-6">{children}</div>
      </SidebarInset>
    </SidebarProvider>
  );
}
```

- [ ] **Step 6: Write the `(merchant)` layout and placeholder dashboard page**

```tsx
// src/web/src/app/(merchant)/layout.tsx
import type { ReactNode } from "react";
import { MerchantAppShell } from "@/components/merchant-layout/MerchantAppShell";

// Tasks 9-14 each mount their own Sheet component here, alongside {children} — same
// one-Sheet-added-per-phase pattern app/(staff)/layout.tsx shows across F1-F6.
export default function MerchantLayout({ children }: { children: ReactNode }) {
  return <MerchantAppShell>{children}</MerchantAppShell>;
}
```

```tsx
// src/web/src/app/(merchant)/dashboard/page.tsx
"use client";

import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";

// Real stat tiles land in Task 15, once the Shipments (Task 8) and COD (Task 11) data-layer
// hooks this dashboard aggregates over actually exist — a greeting-only placeholder here keeps
// this task's own deliverable (the shell + nav + route protection) independently testable.
export default function MerchantDashboardPage() {
  const { data: session } = useMerchantSession();
  return (
    <div>
      <h1 className="font-display text-2xl font-semibold tracking-tight text-foreground">
        {session ? `Welcome back, ${session.fullName}` : "Dashboard"}
      </h1>
      <p className="mt-1 text-sm text-muted-foreground">{session?.merchantDisplayName}</p>
    </div>
  );
}
```

- [ ] **Step 7: Run the full frontend test suite, lint, and type check**

Run: `cd src/web && npx vitest run && npx tsc --noEmit && npx next lint`
Expected: PASS, all green.

- [ ] **Step 8: Commit**

```bash
git add src/web/src/components/merchant-layout/ "src/web/src/app/(merchant)/"
git commit -m "feat(web): add MerchantAppShell, merchant nav, and a placeholder dashboard"
```

---

## Task 8: `merchant-shipments` data layer — types, schema, BFF routes, hooks, panel store

**Files:**
- Create: `src/web/src/features/merchant-shipments/types.ts`
- Create: `src/web/src/features/merchant-shipments/schema.ts`
- Create: `src/web/src/features/merchant-shipments/hooks/useMerchantShipments.ts`
- Create: `src/web/src/features/merchant-shipments/hooks/useMerchantShipment.ts`
- Create: `src/web/src/features/merchant-shipments/hooks/useCreateMerchantShipment.ts`
- Create: `src/web/src/features/merchant-shipments/hooks/useCancelMerchantShipment.ts`
- Create: `src/web/src/features/merchant-shipments/hooks/useMerchantShipmentQuote.ts`
- Create: `src/web/src/features/merchant-shipments/hooks/useMerchantShipmentQuote.test.tsx`
- Create: `src/web/src/features/merchant-shipments/hooks/useMerchantBranches.ts`
- Create: `src/web/src/features/merchant-shipments/hooks/useMerchantServiceTypes.ts`
- Create: `src/web/src/features/merchant-shipments/store/merchantShipmentPanelStore.ts`
- Create: `src/web/src/features/merchant-shipments/store/merchantShipmentPanelStore.test.ts`
- Create: `src/web/src/app/api/merchant/shipments/route.ts`
- Create: `src/web/src/app/api/merchant/shipments/route.test.ts`
- Create: `src/web/src/app/api/merchant/shipments/[awb]/route.ts`
- Create: `src/web/src/app/api/merchant/shipments/[awb]/route.test.ts`
- Create: `src/web/src/app/api/merchant/shipments/[id]/cancel/route.ts`
- Create: `src/web/src/app/api/merchant/shipments/[id]/cancel/route.test.ts`
- Create: `src/web/src/app/api/merchant/pricing/quote/route.ts`
- Create: `src/web/src/app/api/merchant/branches/route.ts`
- Create: `src/web/src/app/api/merchant/service-types/route.ts`

**Interfaces:**
- Consumes: `merchantApiFetch` (`@/lib/auth/merchantApiFetch`, Task 5); `Shipment`, `ShipmentItem`, `ServiceType`, `QuoteResponse` (`@/features/shipments/types`, pre-existing — reused directly, not redefined, since the JSON shapes are identical); `Branch` (`@/features/branches/types`, pre-existing); `PagedResult` (`@/components/data-table/DataTable`, pre-existing).
- Produces: `useMerchantShipments(page, pageSize)`, `useMerchantShipment(awb)`, `useCreateMerchantShipment()`, `useCancelMerchantShipment()`, `useMerchantShipmentQuote(input)`, `useMerchantBranches()`, `useMerchantServiceTypes()`, `createMerchantShipmentSchema`/`CreateMerchantShipmentFormValues`, `useMerchantShipmentPanelStore` — all consumed by Task 9's list page, booking Sheet, and detail Sheet.

- [ ] **Step 1: Write the failing panel store test**

```typescript
// src/web/src/features/merchant-shipments/store/merchantShipmentPanelStore.test.ts
import { describe, expect, it, beforeEach } from "vitest";
import { useMerchantShipmentPanelStore } from "./merchantShipmentPanelStore";

describe("useMerchantShipmentPanelStore", () => {
  beforeEach(() => {
    useMerchantShipmentPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useMerchantShipmentPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate switches to the create panel", () => {
    useMerchantShipmentPanelStore.getState().openCreate();
    expect(useMerchantShipmentPanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("openDetail switches to the detail panel with the given awb", () => {
    useMerchantShipmentPanelStore.getState().openDetail("AWB123");
    expect(useMerchantShipmentPanelStore.getState().panel).toEqual({ mode: "detail", awb: "AWB123" });
  });

  it("close returns to closed from either panel", () => {
    useMerchantShipmentPanelStore.getState().openDetail("AWB123");
    useMerchantShipmentPanelStore.getState().close();
    expect(useMerchantShipmentPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/features/merchant-shipments/store/merchantShipmentPanelStore.test.ts`
Expected: FAIL — module does not exist yet.

- [ ] **Step 3: Write `merchantShipmentPanelStore.ts`**

```typescript
// src/web/src/features/merchant-shipments/store/merchantShipmentPanelStore.ts
import { create } from "zustand";

type MerchantShipmentPanelState =
  | { mode: "closed" }
  | { mode: "create" }
  | { mode: "detail"; awb: string };

interface MerchantShipmentPanelStore {
  panel: MerchantShipmentPanelState;
  openCreate: () => void;
  openDetail: (awb: string) => void;
  close: () => void;
}

export const useMerchantShipmentPanelStore = create<MerchantShipmentPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  openDetail: (awb) => set({ panel: { mode: "detail", awb } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npx vitest run src/features/merchant-shipments/store/merchantShipmentPanelStore.test.ts`
Expected: PASS

- [ ] **Step 5: Write `types.ts` and `schema.ts`**

```typescript
// src/web/src/features/merchant-shipments/types.ts
export type { Shipment, ShipmentItem, ServiceType, QuoteResponse } from "@/features/shipments/types";
export type { Branch } from "@/features/branches/types";

export interface CreateMerchantShipmentItemInput {
  description: string;
  quantity: number;
  unitValue: number;
}
```

```typescript
// src/web/src/features/merchant-shipments/schema.ts
import { z } from "zod";

const itemSchema = z.object({
  description: z.string().min(1, "Description is required."),
  quantity: z.number({ error: "Quantity is required." }).positive("Quantity must be positive."),
  unitValue: z.number({ error: "Unit value is required." }).nonnegative("Unit value cannot be negative."),
});

// No merchantId field — unlike staff's createShipmentSchema, the backend forces MerchantId to
// the caller's own id for a merchant-JWT/API-key caller (ShipmentDtos.cs's own doc comment), so
// there is nothing for this form to collect or submit for it.
export const createMerchantShipmentSchema = z.object({
  originBranchId: z.string({ error: "Origin branch is required." }).min(1, "Origin branch is required."),
  destinationBranchId: z.string({ error: "Destination branch is required." }).min(1, "Destination branch is required."),
  serviceTypeId: z.string({ error: "Service type is required." }).min(1, "Service type is required."),
  weightKg: z.number({ error: "Weight is required." }).positive("Weight must be positive."),
  lengthCm: z.number().positive().optional(),
  widthCm: z.number().positive().optional(),
  heightCm: z.number().positive().optional(),
  codAmount: z.number({ error: "COD amount is required." }).nonnegative("COD amount cannot be negative."),
  wantsInsurance: z.boolean(),
  recipientName: z.string().min(1, "Recipient name is required."),
  recipientPhone: z.string().min(1, "Recipient phone is required."),
  recipientProvinceId: z.string({ error: "Recipient province is required." }).min(1, "Recipient province is required."),
  recipientDistrictId: z.string({ error: "Recipient district is required." }).min(1, "Recipient district is required."),
  recipientLocalLevelId: z.string({ error: "Recipient local level is required." }).min(1, "Recipient local level is required."),
  recipientWardNumber: z.number({ error: "Recipient ward number is required." }).positive(),
  recipientAddressLine: z.string().min(1, "Recipient address is required."),
  senderName: z.string().min(1, "Sender name is required."),
  senderPhone: z.string().min(1, "Sender phone is required."),
  senderProvinceId: z.string({ error: "Sender province is required." }).min(1, "Sender province is required."),
  senderDistrictId: z.string({ error: "Sender district is required." }).min(1, "Sender district is required."),
  senderLocalLevelId: z.string({ error: "Sender local level is required." }).min(1, "Sender local level is required."),
  senderWardNumber: z.number({ error: "Sender ward number is required." }).positive(),
  senderAddressLine: z.string().min(1, "Sender address is required."),
  items: z.array(itemSchema).min(1, "Add at least one item."),
});

export type CreateMerchantShipmentFormValues = z.infer<typeof createMerchantShipmentSchema>;
```

- [ ] **Step 6: Write the failing `useMerchantShipmentQuote` test**

```tsx
// src/web/src/features/merchant-shipments/hooks/useMerchantShipmentQuote.test.tsx
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useMerchantShipmentQuote } from "./useMerchantShipmentQuote";
import * as clientFetchModule from "@/lib/api/clientFetch";

function wrapper({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useMerchantShipmentQuote", () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("does not fetch when input is null", () => {
    const spy = vi.spyOn(clientFetchModule, "clientFetch");
    renderHook(() => useMerchantShipmentQuote(null), { wrapper });
    expect(spy).not.toHaveBeenCalled();
  });

  it("fetches a quote after the debounce delay, always sending merchantId: null", async () => {
    const spy = vi
      .spyOn(clientFetchModule, "clientFetch")
      .mockResolvedValue({ baseRate: 100, appliedCharges: [], discountPercent: 0, subtotal: 100, total: 100 });

    const input = {
      originBranchId: "b1",
      destinationBranchId: "b2",
      weightKg: 2,
      serviceTypeId: "st1",
      codAmount: 0,
      declaredValue: 500,
      wantsInsurance: false,
    };
    renderHook(() => useMerchantShipmentQuote(input), { wrapper });

    expect(spy).not.toHaveBeenCalled();
    vi.advanceTimersByTime(500);
    await waitFor(() =>
      expect(spy).toHaveBeenCalledWith(
        "/api/merchant/pricing/quote",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ ...input, merchantId: null }),
        })
      )
    );
  });
});
```

- [ ] **Step 7: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/features/merchant-shipments/hooks/useMerchantShipmentQuote.test.tsx`
Expected: FAIL — module does not exist yet.

- [ ] **Step 8: Write all seven hooks**

```typescript
// src/web/src/features/merchant-shipments/hooks/useMerchantShipments.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Shipment } from "@/features/merchant-shipments/types";

export function useMerchantShipments(page: number, pageSize: number) {
  return useQuery({
    queryKey: ["merchant-shipments", page, pageSize],
    queryFn: () =>
      clientFetch<PagedResult<Shipment>>(`/api/merchant/shipments?page=${page}&pageSize=${pageSize}`),
  });
}
```

```typescript
// src/web/src/features/merchant-shipments/hooks/useMerchantShipment.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Shipment } from "@/features/merchant-shipments/types";

export function useMerchantShipment(awb: string) {
  return useQuery({
    queryKey: ["merchant-shipments", awb],
    queryFn: () => clientFetch<Shipment>(`/api/merchant/shipments/${awb}`),
    enabled: awb !== "",
  });
}
```

```typescript
// src/web/src/features/merchant-shipments/hooks/useCreateMerchantShipment.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Shipment } from "@/features/merchant-shipments/types";
import type { CreateMerchantShipmentFormValues } from "@/features/merchant-shipments/schema";

export function useCreateMerchantShipment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateMerchantShipmentFormValues) =>
      clientFetch<Shipment>("/api/merchant/shipments", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-shipments"] });
    },
  });
}
```

```typescript
// src/web/src/features/merchant-shipments/hooks/useCancelMerchantShipment.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Shipment } from "@/features/merchant-shipments/types";

export function useCancelMerchantShipment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      clientFetch<Shipment>(`/api/merchant/shipments/${id}/cancel`, { method: "POST" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-shipments"] });
    },
  });
}
```

```typescript
// src/web/src/features/merchant-shipments/hooks/useMerchantShipmentQuote.ts
"use client";

import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { QuoteResponse } from "@/features/merchant-shipments/types";

export interface MerchantQuoteInput {
  originBranchId: string;
  destinationBranchId: string;
  weightKg: number;
  serviceTypeId: string;
  codAmount: number;
  declaredValue: number;
  wantsInsurance: boolean;
}

export function useMerchantShipmentQuote(input: MerchantQuoteInput | null) {
  const [debounced, setDebounced] = useState<MerchantQuoteInput | null>(null);

  useEffect(() => {
    const timeout = setTimeout(() => setDebounced(input), 500);
    return () => clearTimeout(timeout);
  }, [input]);

  return useQuery({
    queryKey: ["merchant-shipment-quote", debounced],
    queryFn: () =>
      clientFetch<QuoteResponse>("/api/merchant/pricing/quote", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        // merchantId is always sent as null — the backend forces it to the caller's own
        // merchant id regardless of what's sent here (Task 3), so there's nothing meaningful to
        // supply from a merchant-track caller.
        body: JSON.stringify({ ...debounced, merchantId: null }),
      }),
    enabled: debounced !== null,
  });
}
```

```typescript
// src/web/src/features/merchant-shipments/hooks/useMerchantBranches.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Branch } from "@/features/merchant-shipments/types";

export function useMerchantBranches() {
  return useQuery({
    queryKey: ["merchant-branches"],
    queryFn: () => clientFetch<Branch[]>("/api/merchant/branches"),
  });
}
```

```typescript
// src/web/src/features/merchant-shipments/hooks/useMerchantServiceTypes.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ServiceType } from "@/features/merchant-shipments/types";

export function useMerchantServiceTypes() {
  return useQuery({
    queryKey: ["merchant-service-types"],
    queryFn: () => clientFetch<ServiceType[]>("/api/merchant/service-types"),
  });
}
```

- [ ] **Step 9: Run the quote hook test to verify it passes**

Run: `cd src/web && npx vitest run src/features/merchant-shipments/hooks/useMerchantShipmentQuote.test.tsx`
Expected: PASS

- [ ] **Step 10: Write the failing BFF route tests**

```typescript
// src/web/src/app/api/merchant/shipments/route.test.ts
import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/merchantApiFetch", () => ({
  merchantApiFetch: vi.fn(),
}));

import { GET, POST } from "./route";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

describe("GET /api/merchant/shipments", () => {
  it("passes page/pageSize through to the backend and proxies the response", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }),
    } as Response);

    const request = new NextRequest("http://localhost/api/merchant/shipments?page=2&pageSize=10");
    const response = await GET(request);

    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/shipments?page=2&pageSize=10");
    expect(response.status).toBe(200);
  });

  it("returns 401 when merchantApiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(merchantApiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const request = new NextRequest("http://localhost/api/merchant/shipments");
    const response = await GET(request);

    expect(response.status).toBe(401);
  });
});

describe("POST /api/merchant/shipments", () => {
  it("proxies the create request body to the backend", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => ({ id: "s1", awb: "AWB123" }),
    } as Response);

    const request = new NextRequest("http://localhost/api/merchant/shipments", {
      method: "POST",
      body: JSON.stringify({ weightKg: 2 }),
    });
    const response = await POST(request);

    expect(merchantApiFetch).toHaveBeenCalledWith(
      "/api/v1/shipments",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ weightKg: 2 }) })
    );
    expect(response.status).toBe(201);
  });
});
```

```typescript
// src/web/src/app/api/merchant/shipments/[awb]/route.test.ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/merchantApiFetch", () => ({
  merchantApiFetch: vi.fn(),
}));

import { GET } from "./route";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

describe("GET /api/merchant/shipments/[awb]", () => {
  it("proxies the detail request for the given awb", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "s1", awb: "AWB123" }),
    } as Response);

    const response = await GET(new Request("http://localhost/api/merchant/shipments/AWB123"), {
      params: { awb: "AWB123" },
    });

    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/shipments/AWB123");
    expect(response.status).toBe(200);
  });
});
```

```typescript
// src/web/src/app/api/merchant/shipments/[id]/cancel/route.test.ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/merchantApiFetch", () => ({
  merchantApiFetch: vi.fn(),
}));

import { POST } from "./route";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

describe("POST /api/merchant/shipments/[id]/cancel", () => {
  it("proxies the cancel action for the given id", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "s1", currentStatus: "Cancelled" }),
    } as Response);

    const response = await POST(new Request("http://localhost/api/merchant/shipments/s1/cancel"), {
      params: { id: "s1" },
    });

    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/shipments/s1/cancel", { method: "POST" });
    expect(response.status).toBe(200);
  });
});
```

- [ ] **Step 11: Run the route tests to verify they fail**

Run: `cd src/web && npx vitest run src/app/api/merchant/shipments`
Expected: FAIL — none of the six route files exist yet.

- [ ] **Step 12: Write the six BFF route files**

```typescript
// src/web/src/app/api/merchant/shipments/route.ts
import { NextRequest, NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await merchantApiFetch(`/api/v1/shipments${query}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await merchantApiFetch("/api/v1/shipments", {
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

```typescript
// src/web/src/app/api/merchant/shipments/[awb]/route.ts
import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET(_request: Request, { params }: { params: { awb: string } }) {
  try {
    const response = await merchantApiFetch(`/api/v1/shipments/${params.awb}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```typescript
// src/web/src/app/api/merchant/shipments/[id]/cancel/route.ts
import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function POST(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await merchantApiFetch(`/api/v1/shipments/${params.id}/cancel`, { method: "POST" });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```typescript
// src/web/src/app/api/merchant/pricing/quote/route.ts
import { NextRequest, NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await merchantApiFetch("/api/v1/pricing/quote", {
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

```typescript
// src/web/src/app/api/merchant/branches/route.ts
import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET() {
  try {
    const response = await merchantApiFetch("/api/v1/merchant/branches");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```typescript
// src/web/src/app/api/merchant/service-types/route.ts
import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET() {
  try {
    const response = await merchantApiFetch("/api/v1/service-types");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 13: Run all new tests, then the full frontend suite and type check**

Run: `cd src/web && npx vitest run src/app/api/merchant/shipments src/features/merchant-shipments && npx vitest run && npx tsc --noEmit`
Expected: PASS, all green.

- [ ] **Step 14: Commit**

```bash
git add src/web/src/features/merchant-shipments/ src/web/src/app/api/merchant/shipments/ src/web/src/app/api/merchant/pricing/ src/web/src/app/api/merchant/branches/ src/web/src/app/api/merchant/service-types/
git commit -m "feat(web): add merchant-shipments data layer (hooks, BFF routes, panel store)"
```

---

## Task 9: `merchant-shipments` UI — list page, booking Sheet, detail Sheet

**Files:**
- Create: `src/web/src/features/merchant-shipments/components/MerchantItemsField.tsx`
- Create: `src/web/src/features/merchant-shipments/components/MerchantShipmentCreateSheet.tsx`
- Create: `src/web/src/features/merchant-shipments/components/MerchantShipmentDetailSheet.tsx`
- Create: `src/web/src/app/(merchant)/shipments/page.tsx`
- Modify: `src/web/src/app/(merchant)/layout.tsx`

**Interfaces:**
- Consumes: everything from Task 8 (`useMerchantShipments`, `useMerchantShipment`, `useCreateMerchantShipment`, `useCancelMerchantShipment`, `useMerchantShipmentQuote`, `useMerchantBranches`, `useMerchantServiceTypes`, `createMerchantShipmentSchema`, `useMerchantShipmentPanelStore`); `useMerchantPermission` (Task 6); `GeographyPicker` (`@/features/branches/GeographyPicker`, pre-existing, shared as-is — no merchant-specific variant needed, it already accepts an `idPrefix` prop for multi-instance use); `StatusBadge` (`@/components/status-badge/StatusBadge`, pre-existing); `DataTable`/`ColumnDef` (`@/components/data-table/DataTable`, pre-existing).
- Produces: `/merchant/shipments` route, matching `MerchantNavMain`'s `Shipments` entry from Task 7.

No new automated tests this task — matches this codebase's established precedent that Sheet components and list pages (`ShipmentCreateSheet.tsx`, `ShipmentDetailSheet.tsx`, `app/(staff)/shipments/page.tsx`) have no dedicated `.test.tsx` files; this class of component is verified by Task 16's manual E2E pass instead. All data-fetching/mutation logic underneath these components is already covered by Task 8's hook tests.

- [ ] **Step 1: Write `MerchantItemsField.tsx`**

A self-contained duplicate of `features/shipments/components/ItemsField.tsx`, typed against this feature's own form values instead of reaching into `features/shipments/schema` — keeps `merchant-shipments` a self-contained module per this codebase's modular-feature-folder convention.

```tsx
// src/web/src/features/merchant-shipments/components/MerchantItemsField.tsx
"use client";

import { useFieldArray, useFormContext } from "react-hook-form";
import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { FieldError } from "@/components/ui/field-error";
import type { CreateMerchantShipmentFormValues } from "@/features/merchant-shipments/schema";

export function MerchantItemsField() {
  const { control, register, formState } = useFormContext<CreateMerchantShipmentFormValues>();
  const { fields, append, remove } = useFieldArray({ control, name: "items" });

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <Label>Items</Label>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="gap-1.5"
          onClick={() => append({ description: "", quantity: 1, unitValue: 0 })}
        >
          <Plus className="h-3.5 w-3.5" strokeWidth={2} />
          Add Item
        </Button>
      </div>
      {fields.map((field, index) => (
        <div key={field.id} className="grid grid-cols-[1fr_auto_auto_auto] items-end gap-2">
          <div className="space-y-1">
            <Label htmlFor={`items.${index}.description`}>Description</Label>
            <Input id={`items.${index}.description`} {...register(`items.${index}.description`)} />
          </div>
          <div className="w-20 space-y-1">
            <Label htmlFor={`items.${index}.quantity`}>Qty</Label>
            <Input
              id={`items.${index}.quantity`}
              type="number"
              {...register(`items.${index}.quantity`, { valueAsNumber: true })}
            />
          </div>
          <div className="w-28 space-y-1">
            <Label htmlFor={`items.${index}.unitValue`}>Unit Value</Label>
            <Input
              id={`items.${index}.unitValue`}
              type="number"
              {...register(`items.${index}.unitValue`, { valueAsNumber: true })}
            />
          </div>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            aria-label="Remove item"
            onClick={() => remove(index)}
          >
            <Trash2 className="h-4 w-4" strokeWidth={2} />
          </Button>
        </div>
      ))}
      {formState.errors.items?.message && (
        <FieldError id="merchant-items-error">{formState.errors.items.message}</FieldError>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Write `MerchantShipmentCreateSheet.tsx`**

```tsx
// src/web/src/features/merchant-shipments/components/MerchantShipmentCreateSheet.tsx
"use client";

import { useState } from "react";
import { useForm, Controller, FormProvider } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { GeographyPicker, type GeographyValue } from "@/features/branches/GeographyPicker";
import { useMerchantBranches } from "@/features/merchant-shipments/hooks/useMerchantBranches";
import { useMerchantServiceTypes } from "@/features/merchant-shipments/hooks/useMerchantServiceTypes";
import { useMerchantShipmentQuote } from "@/features/merchant-shipments/hooks/useMerchantShipmentQuote";
import { useCreateMerchantShipment } from "@/features/merchant-shipments/hooks/useCreateMerchantShipment";
import { useMerchantShipmentPanelStore } from "@/features/merchant-shipments/store/merchantShipmentPanelStore";
import { createMerchantShipmentSchema, type CreateMerchantShipmentFormValues } from "@/features/merchant-shipments/schema";
import { MerchantItemsField } from "@/features/merchant-shipments/components/MerchantItemsField";

const currencyFormatter = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

export function MerchantShipmentCreateSheet() {
  const panel = useMerchantShipmentPanelStore((s) => s.panel);
  const close = useMerchantShipmentPanelStore((s) => s.close);
  const openDetail = useMerchantShipmentPanelStore((s) => s.openDetail);
  const isOpen = panel.mode === "create";

  const createShipment = useCreateMerchantShipment();
  const { data: branches } = useMerchantBranches();
  const { data: serviceTypes } = useMerchantServiceTypes();
  const [serverError, setServerError] = useState<string | null>(null);

  const methods = useForm<CreateMerchantShipmentFormValues>({
    resolver: zodResolver(createMerchantShipmentSchema),
    defaultValues: { items: [{ description: "", quantity: 1, unitValue: 0 }], wantsInsurance: false },
  });
  const { register, handleSubmit, control, watch, setValue, reset, formState: { errors, isSubmitting } } = methods;

  const recipientGeography: GeographyValue = {
    provinceId: watch("recipientProvinceId") ?? null,
    districtId: watch("recipientDistrictId") ?? null,
    localLevelId: watch("recipientLocalLevelId") ?? null,
    wardNumber: watch("recipientWardNumber") ?? null,
  };
  const senderGeography: GeographyValue = {
    provinceId: watch("senderProvinceId") ?? null,
    districtId: watch("senderDistrictId") ?? null,
    localLevelId: watch("senderLocalLevelId") ?? null,
    wardNumber: watch("senderWardNumber") ?? null,
  };

  const [originBranchId, destinationBranchId, weightKg, serviceTypeId, codAmount, wantsInsurance, items] =
    watch(["originBranchId", "destinationBranchId", "weightKg", "serviceTypeId", "codAmount", "wantsInsurance", "items"]);

  const declaredValue = (items ?? []).reduce((sum, item) => sum + (item.quantity || 0) * (item.unitValue || 0), 0);

  const quoteInput =
    originBranchId && destinationBranchId && weightKg && serviceTypeId
      ? {
          originBranchId,
          destinationBranchId,
          weightKg,
          serviceTypeId,
          codAmount: codAmount ?? 0,
          declaredValue,
          wantsInsurance: wantsInsurance ?? false,
        }
      : null;
  const { data: quote, isFetching: isQuoting } = useMerchantShipmentQuote(quoteInput);

  async function onSubmit(values: CreateMerchantShipmentFormValues) {
    setServerError(null);
    try {
      const created = await createShipment.mutateAsync(values);
      reset();
      openDetail(created.awb);
    } catch {
      setServerError("Couldn't create the shipment. Check the fields and try again.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-2xl">
        <SheetHeader>
          <SheetTitle>Book a Shipment</SheetTitle>
        </SheetHeader>
        <FormProvider {...methods}>
          <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-6" noValidate>
            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Route</h3>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="serviceTypeId">Service Type</Label>
                  <Controller
                    name="serviceTypeId"
                    control={control}
                    render={({ field }) => (
                      <Select value={field.value} onValueChange={field.onChange}>
                        <SelectTrigger
                          id="serviceTypeId"
                          aria-label="Service type"
                          aria-invalid={!!errors.serviceTypeId}
                          aria-describedby={errors.serviceTypeId ? "serviceTypeId-error" : undefined}
                        >
                          <SelectValue placeholder="Select service type" />
                        </SelectTrigger>
                        <SelectContent>
                          {serviceTypes?.map((s) => (
                            <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    )}
                  />
                  {errors.serviceTypeId && <FieldError id="serviceTypeId-error">{errors.serviceTypeId.message}</FieldError>}
                </div>
                <div />
                <div className="space-y-2">
                  <Label htmlFor="originBranchId">Origin Branch</Label>
                  <Controller
                    name="originBranchId"
                    control={control}
                    render={({ field }) => (
                      <Select value={field.value} onValueChange={field.onChange}>
                        <SelectTrigger
                          id="originBranchId"
                          aria-label="Origin branch"
                          aria-invalid={!!errors.originBranchId}
                          aria-describedby={errors.originBranchId ? "originBranchId-error" : undefined}
                        >
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
                  {errors.originBranchId && <FieldError id="originBranchId-error">{errors.originBranchId.message}</FieldError>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="destinationBranchId">Destination Branch</Label>
                  <Controller
                    name="destinationBranchId"
                    control={control}
                    render={({ field }) => (
                      <Select value={field.value} onValueChange={field.onChange}>
                        <SelectTrigger
                          id="destinationBranchId"
                          aria-label="Destination branch"
                          aria-invalid={!!errors.destinationBranchId}
                          aria-describedby={errors.destinationBranchId ? "destinationBranchId-error" : undefined}
                        >
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
                  {errors.destinationBranchId && <FieldError id="destinationBranchId-error">{errors.destinationBranchId.message}</FieldError>}
                </div>
              </div>
            </section>

            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Package</h3>
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
                <div className="space-y-2">
                  <Label htmlFor="weightKg">Weight (kg)</Label>
                  <Input
                    id="weightKg"
                    type="number"
                    step="0.1"
                    aria-invalid={!!errors.weightKg}
                    aria-describedby={errors.weightKg ? "weightKg-error" : undefined}
                    {...register("weightKg", { valueAsNumber: true })}
                  />
                  {errors.weightKg && <FieldError id="weightKg-error">{errors.weightKg.message}</FieldError>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="lengthCm">Length (cm)</Label>
                  <Input
                    id="lengthCm"
                    type="number"
                    {...register("lengthCm", { setValueAs: (v) => (v === "" ? undefined : Number(v)) })}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="widthCm">Width (cm)</Label>
                  <Input
                    id="widthCm"
                    type="number"
                    {...register("widthCm", { setValueAs: (v) => (v === "" ? undefined : Number(v)) })}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="heightCm">Height (cm)</Label>
                  <Input
                    id="heightCm"
                    type="number"
                    {...register("heightCm", { setValueAs: (v) => (v === "" ? undefined : Number(v)) })}
                  />
                </div>
              </div>
              <MerchantItemsField />
            </section>

            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Money</h3>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="codAmount">COD Amount</Label>
                  <Input
                    id="codAmount"
                    type="number"
                    aria-invalid={!!errors.codAmount}
                    aria-describedby={errors.codAmount ? "codAmount-error" : undefined}
                    {...register("codAmount", { valueAsNumber: true })}
                  />
                  {errors.codAmount && <FieldError id="codAmount-error">{errors.codAmount.message}</FieldError>}
                </div>
                <div className="flex items-center gap-2 pt-6">
                  <Controller
                    name="wantsInsurance"
                    control={control}
                    render={({ field }) => (
                      <Checkbox id="wantsInsurance" checked={field.value} onCheckedChange={field.onChange} />
                    )}
                  />
                  <Label htmlFor="wantsInsurance">Wants Insurance</Label>
                </div>
              </div>

              {/* Read-only, server-computed estimate — never editable, per CLAUDE.md §33 */}
              <div className="rounded-lg border border-border bg-muted/50 p-4">
                <p className="mb-2 text-xs text-muted-foreground">Estimated Price (recalculates as you fill in the route and package)</p>
                {isQuoting && <Spinner className="h-4 w-4" />}
                {quote && !isQuoting && (
                  <p className="font-sans text-lg font-semibold text-foreground">
                    {currencyFormatter.format(quote.total)}
                  </p>
                )}
                {!quote && !isQuoting && <p className="text-sm text-muted-foreground">—</p>}
              </div>
            </section>

            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Recipient</h3>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="recipientName">Name</Label>
                  <Input
                    id="recipientName"
                    aria-invalid={!!errors.recipientName}
                    aria-describedby={errors.recipientName ? "recipientName-error" : undefined}
                    {...register("recipientName")}
                  />
                  {errors.recipientName && <FieldError id="recipientName-error">{errors.recipientName.message}</FieldError>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="recipientPhone">Phone</Label>
                  <Input
                    id="recipientPhone"
                    aria-invalid={!!errors.recipientPhone}
                    aria-describedby={errors.recipientPhone ? "recipientPhone-error" : undefined}
                    {...register("recipientPhone")}
                  />
                  {errors.recipientPhone && <FieldError id="recipientPhone-error">{errors.recipientPhone.message}</FieldError>}
                </div>
              </div>
              <GeographyPicker
                idPrefix="recipient"
                value={recipientGeography}
                onChange={(next) => {
                  setValue("recipientProvinceId", next.provinceId ?? "");
                  setValue("recipientDistrictId", next.districtId ?? "");
                  setValue("recipientLocalLevelId", next.localLevelId ?? "");
                  setValue("recipientWardNumber", next.wardNumber ?? undefined as never);
                }}
              />
              <div className="space-y-2">
                <Label htmlFor="recipientAddressLine">Address</Label>
                <Input
                  id="recipientAddressLine"
                  aria-invalid={!!errors.recipientAddressLine}
                  aria-describedby={errors.recipientAddressLine ? "recipientAddressLine-error" : undefined}
                  {...register("recipientAddressLine")}
                />
                {errors.recipientAddressLine && <FieldError id="recipientAddressLine-error">{errors.recipientAddressLine.message}</FieldError>}
              </div>
            </section>

            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Sender</h3>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="senderName">Name</Label>
                  <Input
                    id="senderName"
                    aria-invalid={!!errors.senderName}
                    aria-describedby={errors.senderName ? "senderName-error" : undefined}
                    {...register("senderName")}
                  />
                  {errors.senderName && <FieldError id="senderName-error">{errors.senderName.message}</FieldError>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="senderPhone">Phone</Label>
                  <Input
                    id="senderPhone"
                    aria-invalid={!!errors.senderPhone}
                    aria-describedby={errors.senderPhone ? "senderPhone-error" : undefined}
                    {...register("senderPhone")}
                  />
                  {errors.senderPhone && <FieldError id="senderPhone-error">{errors.senderPhone.message}</FieldError>}
                </div>
              </div>
              <GeographyPicker
                idPrefix="sender"
                value={senderGeography}
                onChange={(next) => {
                  setValue("senderProvinceId", next.provinceId ?? "");
                  setValue("senderDistrictId", next.districtId ?? "");
                  setValue("senderLocalLevelId", next.localLevelId ?? "");
                  setValue("senderWardNumber", next.wardNumber ?? undefined as never);
                }}
              />
              <div className="space-y-2">
                <Label htmlFor="senderAddressLine">Address</Label>
                <Input
                  id="senderAddressLine"
                  aria-invalid={!!errors.senderAddressLine}
                  aria-describedby={errors.senderAddressLine ? "senderAddressLine-error" : undefined}
                  {...register("senderAddressLine")}
                />
                {errors.senderAddressLine && <FieldError id="senderAddressLine-error">{errors.senderAddressLine.message}</FieldError>}
              </div>
            </section>

            {serverError && <FieldError id="merchant-shipment-server-error">{serverError}</FieldError>}
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? <Spinner className="h-4 w-4" /> : "Book Shipment"}
            </Button>
          </form>
        </FormProvider>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 3: Write `MerchantShipmentDetailSheet.tsx`**

```tsx
// src/web/src/features/merchant-shipments/components/MerchantShipmentDetailSheet.tsx
"use client";

import { useState } from "react";
import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { FieldError } from "@/components/ui/field-error";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useMerchantBranches } from "@/features/merchant-shipments/hooks/useMerchantBranches";
import { useMerchantServiceTypes } from "@/features/merchant-shipments/hooks/useMerchantServiceTypes";
import { useMerchantShipment } from "@/features/merchant-shipments/hooks/useMerchantShipment";
import { useCancelMerchantShipment } from "@/features/merchant-shipments/hooks/useCancelMerchantShipment";
import { useMerchantShipmentPanelStore } from "@/features/merchant-shipments/store/merchantShipmentPanelStore";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";

const currencyFormatter = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

// Fidelity matches the staff ShipmentDetailSheet exactly (current status + route/package/money/
// items fields) — this is also this phase's "tracking" screen. Neither ShipmentResponse nor any
// endpoint exposes a ShipmentEvent timeline today, so there is no richer event-by-event history
// to show here either; that gap affects the staff app too and is out of this phase's scope
// (recorded in docs/CLARIFICATIONS.md entry #19).
export function MerchantShipmentDetailSheet() {
  const panel = useMerchantShipmentPanelStore((s) => s.panel);
  const close = useMerchantShipmentPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const awb = panel.mode === "detail" ? panel.awb : "";

  const { data: shipment, isLoading, isError, refetch } = useMerchantShipment(awb);
  const { data: branches } = useMerchantBranches();
  const { data: serviceTypes } = useMerchantServiceTypes();
  const cancelShipment = useCancelMerchantShipment();
  const canCancel = useMerchantPermission("shipment.cancel");
  const [cancelError, setCancelError] = useState<string | null>(null);

  const branchName = (id: string) => branches?.find((b) => b.id === id)?.name ?? "—";
  const serviceTypeName = (id: string) => serviceTypes?.find((s) => s.id === id)?.name ?? "—";

  async function handleCancel() {
    if (!shipment) return;
    setCancelError(null);
    try {
      await cancelShipment.mutateAsync(shipment.id);
    } catch {
      setCancelError("Couldn't cancel this shipment. It may already be past a cancellable stage.");
    }
  }

  const isTerminal = shipment?.currentStatus === "Delivered" || shipment?.currentStatus === "Cancelled";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{shipment?.awb ?? "Shipment"}</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && (
            <ErrorState
              title="Couldn't load this shipment"
              description="It may not exist, or you may not have access to it."
              onRetry={() => refetch()}
            />
          )}
          {shipment && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Status</span>
                  <StatusBadge status={shipment.currentStatus} />
                </div>
                <Field label="Service Type" value={serviceTypeName(shipment.serviceTypeId)} />
                <Field label="Created At" value={new Date(shipment.createdAt).toLocaleString()} />
              </div>

              <div className="grid grid-cols-2 gap-4">
                <Field label="Origin Branch" value={branchName(shipment.originBranchId)} />
                <Field label="Destination Branch" value={branchName(shipment.destinationBranchId)} />
                <Field label="Weight" value={`${shipment.weightKg} kg`} />
                <Field
                  label="Dimensions"
                  value={
                    shipment.lengthCm && shipment.widthCm && shipment.heightCm
                      ? `${shipment.lengthCm} x ${shipment.widthCm} x ${shipment.heightCm} cm`
                      : "—"
                  }
                />
              </div>

              <div className="grid grid-cols-2 gap-4">
                <Field label="Recipient" value={`${shipment.recipientName} (${shipment.recipientPhone})`} />
                <Field label="Recipient Address" value={shipment.recipientAddressLine} />
                <Field label="Sender" value={`${shipment.senderName} (${shipment.senderPhone})`} />
                <Field label="Sender Address" value={shipment.senderAddressLine} />
              </div>

              <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
                <Field label="Declared Value" value={currencyFormatter.format(shipment.declaredValue)} />
                <Field label="COD Amount" value={currencyFormatter.format(shipment.codAmount)} />
                <Field label="Delivery Charge" value={currencyFormatter.format(shipment.deliveryCharge)} />
                <Field label="Insurance Charge" value={currencyFormatter.format(shipment.insuranceCharge)} />
                <Field label="Other Charges" value={currencyFormatter.format(shipment.otherCharges)} />
                <Field label="Total Charge" value={currencyFormatter.format(shipment.totalCharge)} />
              </div>

              <div className="space-y-2">
                <span className="text-xs text-muted-foreground">Items</span>
                <ul className="space-y-1">
                  {shipment.items.map((item) => (
                    <li key={item.id} className="flex justify-between text-sm">
                      <span>{item.description} × {item.quantity}</span>
                      <span>{currencyFormatter.format(item.unitValue)}</span>
                    </li>
                  ))}
                </ul>
              </div>

              {canCancel && !isTerminal && (
                <div className="space-y-2 border-t border-border pt-4">
                  <Button
                    type="button"
                    variant="destructive"
                    onClick={handleCancel}
                    disabled={cancelShipment.isPending}
                  >
                    {cancelShipment.isPending ? <Spinner className="h-4 w-4" /> : "Cancel Shipment"}
                  </Button>
                  {cancelError && <FieldError id="merchant-cancel-error">{cancelError}</FieldError>}
                </div>
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 4: Write the list page**

```tsx
// src/web/src/app/(merchant)/shipments/page.tsx
"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import { useMerchantShipments } from "@/features/merchant-shipments/hooks/useMerchantShipments";
import { useMerchantServiceTypes } from "@/features/merchant-shipments/hooks/useMerchantServiceTypes";
import { useMerchantShipmentPanelStore } from "@/features/merchant-shipments/store/merchantShipmentPanelStore";
import type { Shipment } from "@/features/merchant-shipments/types";

const currencyFormatter = new Intl.NumberFormat("en-NP", {
  style: "currency",
  currency: "NPR",
  notation: "compact",
  maximumFractionDigits: 1,
});

export default function MerchantShipmentsPage() {
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const { data, isLoading, isError, refetch } = useMerchantShipments(page, pageSize);
  const { data: serviceTypes } = useMerchantServiceTypes();
  const canCreate = useMerchantPermission("shipment.create");
  const openCreate = useMerchantShipmentPanelStore((s) => s.openCreate);
  const openDetail = useMerchantShipmentPanelStore((s) => s.openDetail);

  const serviceTypeName = (id: string) => serviceTypes?.find((s) => s.id === id)?.name ?? "—";

  const columns: ColumnDef<Shipment>[] = [
    {
      key: "awb",
      header: "AWB",
      render: (s) => (
        <button
          type="button"
          onClick={() => openDetail(s.awb)}
          className="font-medium text-foreground hover:underline"
        >
          {s.awb}
        </button>
      ),
    },
    { key: "recipient", header: "Recipient", render: (s) => s.recipientName },
    { key: "status", header: "Status", render: (s) => <StatusBadge status={s.currentStatus} /> },
    { key: "serviceType", header: "Service Type", render: (s) => serviceTypeName(s.serviceTypeId) },
    { key: "cod", header: "COD Amount", render: (s) => currencyFormatter.format(s.codAmount) },
    {
      key: "createdAt",
      header: "Created At",
      render: (s) => new Date(s.createdAt).toLocaleDateString(),
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
        title="Couldn't load shipments"
        description="Something went wrong fetching your shipment list."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Shipments</h1>
        {canCreate && (
          <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            Book a Shipment
          </Button>
        )}
      </div>
      <DataTable
        columns={columns}
        data={data}
        isLoading={false}
        emptyTitle="No shipments yet"
        emptyDescription="Book your first shipment to get started."
        onPageChange={setPage}
        rowKey={(s) => s.id}
      />
    </div>
  );
}
```

- [ ] **Step 5: Mount the two Sheets in the `(merchant)` layout**

```tsx
// src/web/src/app/(merchant)/layout.tsx
import type { ReactNode } from "react";
import { MerchantAppShell } from "@/components/merchant-layout/MerchantAppShell";
import { MerchantShipmentCreateSheet } from "@/features/merchant-shipments/components/MerchantShipmentCreateSheet";
import { MerchantShipmentDetailSheet } from "@/features/merchant-shipments/components/MerchantShipmentDetailSheet";

export default function MerchantLayout({ children }: { children: ReactNode }) {
  return (
    <MerchantAppShell>
      {children}
      <MerchantShipmentCreateSheet />
      <MerchantShipmentDetailSheet />
    </MerchantAppShell>
  );
}
```

- [ ] **Step 6: Run the full frontend test suite, lint, and type check**

Run: `cd src/web && npx vitest run && npx tsc --noEmit && npx next lint`
Expected: PASS, all green.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/merchant-shipments/components/ "src/web/src/app/(merchant)/shipments/" "src/web/src/app/(merchant)/layout.tsx"
git commit -m "feat(web): add merchant shipment booking, tracking, and cancel UI"
```

---

## Task 10: `merchant-pickups` — data layer and UI

**Files:**
- Create: `src/web/src/features/merchant-pickups/types.ts`
- Create: `src/web/src/features/merchant-pickups/schema.ts`
- Create: `src/web/src/features/merchant-pickups/hooks/useMerchantPickups.ts`
- Create: `src/web/src/features/merchant-pickups/hooks/useCreateMerchantPickup.ts`
- Create: `src/web/src/features/merchant-pickups/hooks/useCancelMerchantPickup.ts`
- Create: `src/web/src/features/merchant-pickups/store/merchantPickupPanelStore.ts`
- Create: `src/web/src/features/merchant-pickups/store/merchantPickupPanelStore.test.ts`
- Create: `src/web/src/features/merchant-pickups/components/MerchantPickupCreateSheet.tsx`
- Create: `src/web/src/app/api/merchant/pickups/route.ts`
- Create: `src/web/src/app/api/merchant/pickups/[id]/cancel/route.ts`
- Create: `src/web/src/app/(merchant)/pickups/page.tsx`
- Modify: `src/web/src/app/(merchant)/layout.tsx`

**Interfaces:**
- Consumes: `useMerchantShipments` (`@/features/merchant-shipments/hooks/useMerchantShipments`, Task 8, to source the "not yet picked up" AWB picker); `useMerchantPermission` (Task 6); `merchantApiFetch` (Task 5); `DatePicker` (`@/components/ui/date-picker`, pre-existing); `StatusBadge` (pre-existing, `namespace="PickupRequest"` — new token mapping added in this task's Step 6).
- Produces: `/merchant/pickups` route, matching `MerchantNavMain`'s `Pickups` entry.

- [ ] **Step 1: Write the failing panel store test**

```typescript
// src/web/src/features/merchant-pickups/store/merchantPickupPanelStore.test.ts
import { describe, expect, it, beforeEach } from "vitest";
import { useMerchantPickupPanelStore } from "./merchantPickupPanelStore";

describe("useMerchantPickupPanelStore", () => {
  beforeEach(() => {
    useMerchantPickupPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useMerchantPickupPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate switches to the create panel", () => {
    useMerchantPickupPanelStore.getState().openCreate();
    expect(useMerchantPickupPanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("close returns to closed", () => {
    useMerchantPickupPanelStore.getState().openCreate();
    useMerchantPickupPanelStore.getState().close();
    expect(useMerchantPickupPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/features/merchant-pickups/store/merchantPickupPanelStore.test.ts`
Expected: FAIL — module does not exist yet.

- [ ] **Step 3: Write the store, types, and schema**

```typescript
// src/web/src/features/merchant-pickups/store/merchantPickupPanelStore.ts
import { create } from "zustand";

// No "detail" mode — unlike shipments, a pickup request's full state fits in the list row plus
// a cancel action; nothing warrants its own detail Sheet.
type MerchantPickupPanelState = { mode: "closed" } | { mode: "create" };

interface MerchantPickupPanelStore {
  panel: MerchantPickupPanelState;
  openCreate: () => void;
  close: () => void;
}

export const useMerchantPickupPanelStore = create<MerchantPickupPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

```typescript
// src/web/src/features/merchant-pickups/types.ts
export interface PickupRequest {
  id: string;
  merchantId: string;
  branchId: string;
  shipmentIds: string[];
  requestedForDate: string;
  notes: string | null;
  status: "Requested" | "Scheduled" | "Completed" | "Cancelled";
  createdAt: string;
  updatedAt: string;
}
```

```typescript
// src/web/src/features/merchant-pickups/schema.ts
import { z } from "zod";

export const createMerchantPickupSchema = z.object({
  shipmentIds: z.array(z.string()).min(1, "Select at least one shipment."),
  requestedForDate: z.string().min(1, "Requested date is required."),
  notes: z.string().optional(),
});

export type CreateMerchantPickupFormValues = z.infer<typeof createMerchantPickupSchema>;
```

- [ ] **Step 4: Run the store test to verify it passes**

Run: `cd src/web && npx vitest run src/features/merchant-pickups/store/merchantPickupPanelStore.test.ts`
Expected: PASS

- [ ] **Step 5: Write the three hooks and two BFF routes**

```typescript
// src/web/src/features/merchant-pickups/hooks/useMerchantPickups.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PickupRequest } from "@/features/merchant-pickups/types";

// Not paginated — GET /api/v1/pickups returns a plain List<PickupRequestResponse>, matching
// every other small merchant-scoped list this phase touches (staff, api-keys, cod settlements).
export function useMerchantPickups() {
  return useQuery({
    queryKey: ["merchant-pickups"],
    queryFn: () => clientFetch<PickupRequest[]>("/api/merchant/pickups"),
  });
}
```

```typescript
// src/web/src/features/merchant-pickups/hooks/useCreateMerchantPickup.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PickupRequest } from "@/features/merchant-pickups/types";
import type { CreateMerchantPickupFormValues } from "@/features/merchant-pickups/schema";

export function useCreateMerchantPickup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateMerchantPickupFormValues) =>
      clientFetch<PickupRequest>("/api/merchant/pickups", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-pickups"] });
    },
  });
}
```

```typescript
// src/web/src/features/merchant-pickups/hooks/useCancelMerchantPickup.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PickupRequest } from "@/features/merchant-pickups/types";

export function useCancelMerchantPickup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      clientFetch<PickupRequest>(`/api/merchant/pickups/${id}/cancel`, { method: "POST" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-pickups"] });
    },
  });
}
```

```typescript
// src/web/src/app/api/merchant/pickups/route.ts
import { NextRequest, NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET() {
  try {
    const response = await merchantApiFetch("/api/v1/pickups");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await merchantApiFetch("/api/v1/pickups", {
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

```typescript
// src/web/src/app/api/merchant/pickups/[id]/cancel/route.ts
import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function POST(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await merchantApiFetch(`/api/v1/pickups/${params.id}/cancel`, { method: "POST" });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 6: Add a `PickupRequest.*` status badge mapping**

In `src/web/src/components/status-badge/statusConfig.ts`, add to `STATUS_CONFIG`:

```typescript
  // PickupRequestStatus — namespaced ("PickupRequest.X") for the same collision-avoidance
  // reason every other namespace here is (a flat key risks colliding with another enum's same
  // member name; PickupRequestStatus has no direct name clash today, but every multi-enum
  // status set in this file is namespaced on principle, not only when a clash is proven).
  "PickupRequest.Requested": { label: "Requested", token: "status-created" },
  "PickupRequest.Scheduled": { label: "Scheduled", token: "status-sealed" },
  "PickupRequest.Completed": { label: "Completed", token: "status-delivered" },
  "PickupRequest.Cancelled": { label: "Cancelled", token: "status-cancelled" },
```

- [ ] **Step 7: Write `MerchantPickupCreateSheet.tsx` and the list page**

```tsx
// src/web/src/features/merchant-pickups/components/MerchantPickupCreateSheet.tsx
"use client";

import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Textarea } from "@/components/ui/textarea";
import { DatePicker } from "@/components/ui/date-picker";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useMerchantShipments } from "@/features/merchant-shipments/hooks/useMerchantShipments";
import { useCreateMerchantPickup } from "@/features/merchant-pickups/hooks/useCreateMerchantPickup";
import { useMerchantPickupPanelStore } from "@/features/merchant-pickups/store/merchantPickupPanelStore";
import { createMerchantPickupSchema, type CreateMerchantPickupFormValues } from "@/features/merchant-pickups/schema";

export function MerchantPickupCreateSheet() {
  const panel = useMerchantPickupPanelStore((s) => s.panel);
  const close = useMerchantPickupPanelStore((s) => s.close);
  const isOpen = panel.mode === "create";

  const createPickup = useCreateMerchantPickup();
  // First page is enough to populate the picker — a merchant with more than 20 freshly-booked,
  // not-yet-picked-up shipments waiting at once is a scale this phase doesn't need to solve for.
  const { data: shipments } = useMerchantShipments(1, 20);
  const bookedShipments = (shipments?.items ?? []).filter((s) => s.currentStatus === "Created");
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    control,
    watch,
    setValue,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateMerchantPickupFormValues>({
    resolver: zodResolver(createMerchantPickupSchema),
    defaultValues: { shipmentIds: [] },
  });

  const selectedIds = watch("shipmentIds");

  function toggleShipment(id: string, checked: boolean) {
    if (checked) {
      setValue("shipmentIds", [...selectedIds, id]);
    } else {
      setValue("shipmentIds", selectedIds.filter((existing) => existing !== id));
    }
  }

  async function onSubmit(values: CreateMerchantPickupFormValues) {
    setServerError(null);
    try {
      await createPickup.mutateAsync(values);
      reset({ shipmentIds: [] });
      close();
    } catch {
      setServerError("Couldn't create the pickup request. Check the fields and try again.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Request a Pickup</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-6" noValidate>
          <div className="space-y-2">
            <Label>Shipments</Label>
            <div className="max-h-64 space-y-2 overflow-y-auto rounded-lg border border-border p-3">
              {bookedShipments.length === 0 && (
                <p className="text-sm text-muted-foreground">No freshly booked shipments waiting for pickup.</p>
              )}
              {bookedShipments.map((s) => (
                <div key={s.id} className="flex items-center gap-2">
                  <Checkbox
                    id={`pickup-shipment-${s.id}`}
                    checked={selectedIds.includes(s.id)}
                    onCheckedChange={(checked) => toggleShipment(s.id, checked === true)}
                  />
                  <Label htmlFor={`pickup-shipment-${s.id}`} className="font-normal">
                    {s.awb} — {s.recipientName}
                  </Label>
                </div>
              ))}
            </div>
            {errors.shipmentIds && <FieldError id="shipmentIds-error">{errors.shipmentIds.message}</FieldError>}
          </div>

          <div className="space-y-2">
            <Label htmlFor="requestedForDate">Requested Date</Label>
            <Controller
              name="requestedForDate"
              control={control}
              render={({ field }) => (
                <DatePicker
                  id="requestedForDate"
                  aria-label="Requested date"
                  value={field.value}
                  onChange={field.onChange}
                  aria-invalid={!!errors.requestedForDate}
                  aria-describedby={errors.requestedForDate ? "requestedForDate-error" : undefined}
                />
              )}
            />
            {errors.requestedForDate && <FieldError id="requestedForDate-error">{errors.requestedForDate.message}</FieldError>}
          </div>

          <div className="space-y-2">
            <Label htmlFor="notes">Notes</Label>
            <Textarea id="notes" {...register("notes")} />
          </div>

          {serverError && <FieldError id="merchant-pickup-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Request Pickup"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
```

```tsx
// src/web/src/app/(merchant)/pickups/page.tsx
"use client";

import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { EmptyState } from "@/components/states/EmptyState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import { useMerchantPickups } from "@/features/merchant-pickups/hooks/useMerchantPickups";
import { useCancelMerchantPickup } from "@/features/merchant-pickups/hooks/useCancelMerchantPickup";
import { useMerchantPickupPanelStore } from "@/features/merchant-pickups/store/merchantPickupPanelStore";

export default function MerchantPickupsPage() {
  const { data: pickups, isLoading, isError, refetch } = useMerchantPickups();
  const canCreate = useMerchantPermission("merchant.pickups.create");
  const canCancel = useMerchantPermission("merchant.pickups.cancel");
  const openCreate = useMerchantPickupPanelStore((s) => s.openCreate);
  const cancelPickup = useCancelMerchantPickup();

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
        title="Couldn't load pickup requests"
        description="Something went wrong fetching your pickup requests."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Pickups</h1>
        {canCreate && (
          <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            Request Pickup
          </Button>
        )}
      </div>
      {!pickups || pickups.length === 0 ? (
        <EmptyState title="No pickup requests yet" description="Request a pickup once you have shipments ready." />
      ) : (
        <div className="overflow-hidden rounded-xl border border-border bg-muted/40">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Shipments</TableHead>
                <TableHead>Requested Date</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Notes</TableHead>
                {canCancel && <TableHead />}
              </TableRow>
            </TableHeader>
            <TableBody>
              {pickups.map((p) => (
                <TableRow key={p.id}>
                  <TableCell>{p.shipmentIds.length} shipment{p.shipmentIds.length === 1 ? "" : "s"}</TableCell>
                  <TableCell>{p.requestedForDate}</TableCell>
                  <TableCell><StatusBadge status={p.status} namespace="PickupRequest" /></TableCell>
                  <TableCell>{p.notes ?? "—"}</TableCell>
                  {canCancel && (
                    <TableCell>
                      {p.status === "Requested" && (
                        <Button
                          variant="ghost"
                          size="sm"
                          disabled={cancelPickup.isPending}
                          onClick={() => cancelPickup.mutate(p.id)}
                        >
                          Cancel
                        </Button>
                      )}
                    </TableCell>
                  )}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 8: Mount the create Sheet in the `(merchant)` layout**

```tsx
// src/web/src/app/(merchant)/layout.tsx — add alongside the shipment Sheets from Task 9
import { MerchantPickupCreateSheet } from "@/features/merchant-pickups/components/MerchantPickupCreateSheet";
// ...
      <MerchantShipmentCreateSheet />
      <MerchantShipmentDetailSheet />
      <MerchantPickupCreateSheet />
```

- [ ] **Step 9: Run the full frontend test suite, lint, and type check**

Run: `cd src/web && npx vitest run && npx tsc --noEmit && npx next lint`
Expected: PASS, all green.

- [ ] **Step 10: Commit**

```bash
git add src/web/src/features/merchant-pickups/ src/web/src/app/api/merchant/pickups/ "src/web/src/app/(merchant)/pickups/" "src/web/src/app/(merchant)/layout.tsx" src/web/src/components/status-badge/statusConfig.ts
git commit -m "feat(web): add merchant pickup request UI"
```

---

## Task 11: `merchant-cod` — settlements list, invoice detail Sheet with print and CSV export

**Files:**
- Create: `src/web/src/features/merchant-cod/types.ts`
- Create: `src/web/src/features/merchant-cod/hooks/useMerchantSettlements.ts`
- Create: `src/web/src/features/merchant-cod/hooks/useMerchantSettlement.ts`
- Create: `src/web/src/features/merchant-cod/lib/toCsv.ts`
- Create: `src/web/src/features/merchant-cod/lib/toCsv.test.ts`
- Create: `src/web/src/features/merchant-cod/store/merchantSettlementPanelStore.ts`
- Create: `src/web/src/features/merchant-cod/store/merchantSettlementPanelStore.test.ts`
- Create: `src/web/src/features/merchant-cod/components/MerchantSettlementDetailSheet.tsx`
- Create: `src/web/src/app/api/merchant/cod/settlements/route.ts`
- Create: `src/web/src/app/api/merchant/cod/settlements/[id]/route.ts`
- Create: `src/web/src/app/(merchant)/cod/page.tsx`
- Modify: `src/web/src/app/(merchant)/layout.tsx`

**Interfaces:**
- Consumes: `MerchantSettlement`/`MerchantSettlementItem`/`MerchantSettlementStatus` (`@/features/cod/types`, pre-existing from Frontend Phase F7 — reused directly, not redefined, since `GET /api/v1/cod/settlements` and the staff `/merchant-settlements` routes share the exact same `MerchantSettlementResponse` shape); `merchantApiFetch` (Task 5); the existing `"MerchantSettlement.Created"`/`"MerchantSettlement.Settled"` `STATUS_CONFIG` entries (`@/components/status-badge/statusConfig`, already added in Frontend Phase F7 — no new badge mapping needed this task).
- Produces: `toCsv(headers, rows): string` (`@/features/merchant-cod/lib/toCsv`) — the client-side equivalent of the backend's `Reports.Infrastructure.CsvWriter.EscapeField`, since this feature has no backend export endpoint of its own (per the design spec's resolved decision to treat `MerchantSettlement` itself as the invoice). `/merchant/cod` route, matching `MerchantNavMain`'s `COD & Invoices` entry.

- [ ] **Step 1: Write the failing `toCsv` test**

```typescript
// src/web/src/features/merchant-cod/lib/toCsv.test.ts
import { describe, expect, it } from "vitest";
import { toCsv } from "./toCsv";

describe("toCsv", () => {
  it("joins headers and rows with CRLF, comma-separated", () => {
    const csv = toCsv(["Name", "Amount"], [["Acme", "100"]]);
    expect(csv).toBe("Name,Amount\r\nAcme,100\r\n");
  });

  it("quotes a field containing a comma", () => {
    const csv = toCsv(["Name"], [["Acme, Inc."]]);
    expect(csv.split("\r\n")[1]).toBe('"Acme, Inc."');
  });

  it("escapes an internal double quote by doubling it, then wraps in quotes", () => {
    const csv = toCsv(["Name"], [['Say "hi"']]);
    expect(csv.split("\r\n")[1]).toBe('"Say ""hi"""');
  });

  it("prefixes a leading = with a single quote to prevent formula injection in Excel", () => {
    const csv = toCsv(["Name"], [["=SUM(A1:A9)"]]);
    expect(csv.split("\r\n")[1]).toBe("'=SUM(A1:A9)");
  });

  it("prefixes a leading @, +, or - the same way", () => {
    expect(toCsv(["N"], [["@cmd"]]).split("\r\n")[1]).toBe("'@cmd");
    expect(toCsv(["N"], [["+cmd"]]).split("\r\n")[1]).toBe("'+cmd");
    expect(toCsv(["N"], [["-cmd"]]).split("\r\n")[1]).toBe("'-cmd");
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/features/merchant-cod/lib/toCsv.test.ts`
Expected: FAIL — module does not exist yet.

- [ ] **Step 3: Write `toCsv.ts`**

```typescript
// src/web/src/features/merchant-cod/lib/toCsv.ts
// Mirrors src/Modules/Reports/Infrastructure/CsvWriter.EscapeField exactly (RFC 4180 quoting
// plus a formula-injection guard for Excel/LibreOffice) — this feature has no backend CSV export
// endpoint of its own (design spec's resolved decision: MerchantSettlement itself is the
// invoice), so the same escaping discipline is reimplemented here against already-fetched JSON.
function escapeField(field: string): string {
  let value = field;
  if (value.length > 0 && "=+-@\t\r".includes(value[0])) {
    value = "'" + value;
  }
  if (value.includes(",") || value.includes('"') || value.includes("\n") || value.includes("\r")) {
    return `"${value.replace(/"/g, '""')}"`;
  }
  return value;
}

export function toCsv(headers: string[], rows: string[][]): string {
  const lines = [headers, ...rows].map((row) => row.map(escapeField).join(","));
  return lines.join("\r\n") + "\r\n";
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npx vitest run src/features/merchant-cod/lib/toCsv.test.ts`
Expected: PASS

- [ ] **Step 5: Write the failing panel store test**

```typescript
// src/web/src/features/merchant-cod/store/merchantSettlementPanelStore.test.ts
import { describe, expect, it, beforeEach } from "vitest";
import { useMerchantSettlementPanelStore } from "./merchantSettlementPanelStore";

describe("useMerchantSettlementPanelStore", () => {
  beforeEach(() => {
    useMerchantSettlementPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useMerchantSettlementPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openDetail switches to the detail panel with the given id", () => {
    useMerchantSettlementPanelStore.getState().openDetail("s1");
    expect(useMerchantSettlementPanelStore.getState().panel).toEqual({ mode: "detail", id: "s1" });
  });

  it("close returns to closed", () => {
    useMerchantSettlementPanelStore.getState().openDetail("s1");
    useMerchantSettlementPanelStore.getState().close();
    expect(useMerchantSettlementPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

- [ ] **Step 6: Run the test to verify it fails, then write the store, types, hooks, and BFF routes**

Run: `cd src/web && npx vitest run src/features/merchant-cod/store/merchantSettlementPanelStore.test.ts` — Expected: FAIL (module doesn't exist).

```typescript
// src/web/src/features/merchant-cod/store/merchantSettlementPanelStore.ts
import { create } from "zustand";

type MerchantSettlementPanelState = { mode: "closed" } | { mode: "detail"; id: string };

interface MerchantSettlementPanelStore {
  panel: MerchantSettlementPanelState;
  openDetail: (id: string) => void;
  close: () => void;
}

// No create mode — MerchantSettlement rows are born by the courier's own staff-side COD
// workflow (Frontend Phase F7), never by a merchant action. This portal is read-only over them.
export const useMerchantSettlementPanelStore = create<MerchantSettlementPanelStore>((set) => ({
  panel: { mode: "closed" },
  openDetail: (id) => set({ panel: { mode: "detail", id } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

```typescript
// src/web/src/features/merchant-cod/types.ts
export type { MerchantSettlement, MerchantSettlementItem, MerchantSettlementStatus } from "@/features/cod/types";
```

```typescript
// src/web/src/features/merchant-cod/hooks/useMerchantSettlements.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantSettlement } from "@/features/merchant-cod/types";

export function useMerchantSettlements() {
  return useQuery({
    queryKey: ["merchant-cod-settlements"],
    queryFn: () => clientFetch<MerchantSettlement[]>("/api/merchant/cod/settlements"),
  });
}
```

```typescript
// src/web/src/features/merchant-cod/hooks/useMerchantSettlement.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantSettlement } from "@/features/merchant-cod/types";

export function useMerchantSettlement(id: string) {
  return useQuery({
    queryKey: ["merchant-cod-settlements", id],
    queryFn: () => clientFetch<MerchantSettlement>(`/api/merchant/cod/settlements/${id}`),
    enabled: id !== "",
  });
}
```

```typescript
// src/web/src/app/api/merchant/cod/settlements/route.ts
import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET() {
  try {
    const response = await merchantApiFetch("/api/v1/cod/settlements");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```typescript
// src/web/src/app/api/merchant/cod/settlements/[id]/route.ts
import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await merchantApiFetch(`/api/v1/cod/settlements/${params.id}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Run: `cd src/web && npx vitest run src/features/merchant-cod/store/merchantSettlementPanelStore.test.ts` — Expected: PASS.

- [ ] **Step 7: Write `MerchantSettlementDetailSheet.tsx` and the list page**

```tsx
// src/web/src/features/merchant-cod/components/MerchantSettlementDetailSheet.tsx
"use client";

import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useMerchantSettlement } from "@/features/merchant-cod/hooks/useMerchantSettlement";
import { useMerchantSettlementPanelStore } from "@/features/merchant-cod/store/merchantSettlementPanelStore";
import { toCsv } from "@/features/merchant-cod/lib/toCsv";

const currencyFormatter = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

function downloadCsv(filename: string, csv: string) {
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}

// Doubles as the invoice view per the design spec's resolved decision — no separate Invoice
// entity/entity exists, so this Sheet's own printable layout plus a CSV export of the already-
// fetched settlement is the whole "invoice" feature.
export function MerchantSettlementDetailSheet() {
  const panel = useMerchantSettlementPanelStore((s) => s.panel);
  const close = useMerchantSettlementPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";

  const { data: settlement, isLoading, isError, refetch } = useMerchantSettlement(id);

  function handleDownloadCsv() {
    if (!settlement) return;
    const csv = toCsv(
      ["Shipment ID", "COD Amount", "Courier Fee", "Other Deductions", "Net Payable"],
      settlement.items.map((item) => [
        item.shipmentId,
        item.codAmount.toFixed(2),
        item.courierFee.toFixed(2),
        item.otherDeductions.toFixed(2),
        item.netPayable.toFixed(2),
      ])
    );
    downloadCsv(`settlement-${settlement.id}.csv`, csv);
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl print:max-w-none print:overflow-visible">
        <style>{`
          @media print {
            .merchant-invoice-no-print { display: none !important; }
          }
        `}</style>
        <SheetHeader className="merchant-invoice-no-print">
          <SheetTitle>Settlement Invoice</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && (
            <ErrorState
              title="Couldn't load this settlement"
              description="It may not exist, or you may not have access to it."
              onRetry={() => refetch()}
            />
          )}
          {settlement && (
            <>
              <div className="flex gap-2 merchant-invoice-no-print">
                <Button variant="outline" size="sm" onClick={() => window.print()}>
                  Print
                </Button>
                <Button variant="outline" size="sm" onClick={handleDownloadCsv}>
                  Download CSV
                </Button>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Status</span>
                  <StatusBadge status={settlement.status} namespace="MerchantSettlement" />
                </div>
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Period</span>
                  <span className="text-sm font-medium text-foreground">
                    {new Date(settlement.periodFrom).toLocaleDateString()} – {new Date(settlement.periodTo).toLocaleDateString()}
                  </span>
                </div>
                {settlement.settledAt && (
                  <div className="flex flex-col gap-1">
                    <span className="text-xs text-muted-foreground">Settled At</span>
                    <span className="text-sm font-medium text-foreground">{new Date(settlement.settledAt).toLocaleString()}</span>
                  </div>
                )}
              </div>

              <div className="space-y-2">
                <span className="text-xs text-muted-foreground">Line Items</span>
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-border text-left text-xs text-muted-foreground">
                      <th className="py-2">Shipment</th>
                      <th className="py-2 text-right">COD Amount</th>
                      <th className="py-2 text-right">Courier Fee</th>
                      <th className="py-2 text-right">Net Payable</th>
                    </tr>
                  </thead>
                  <tbody>
                    {settlement.items.map((item) => (
                      <tr key={item.id} className="border-b border-border/50">
                        <td className="py-2">{item.shipmentId}</td>
                        <td className="py-2 text-right">{currencyFormatter.format(item.codAmount)}</td>
                        <td className="py-2 text-right">{currencyFormatter.format(item.courierFee)}</td>
                        <td className="py-2 text-right">{currencyFormatter.format(item.netPayable)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="flex items-center justify-between border-t border-border pt-4">
                <span className="text-sm font-semibold text-foreground">Total Payable</span>
                <span className="text-lg font-semibold text-foreground">{currencyFormatter.format(settlement.totalPayable)}</span>
              </div>
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

```tsx
// src/web/src/app/(merchant)/cod/page.tsx
"use client";

import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { EmptyState } from "@/components/states/EmptyState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useMerchantSettlements } from "@/features/merchant-cod/hooks/useMerchantSettlements";
import { useMerchantSettlementPanelStore } from "@/features/merchant-cod/store/merchantSettlementPanelStore";

const currencyFormatter = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

export default function MerchantCodPage() {
  const { data: settlements, isLoading, isError, refetch } = useMerchantSettlements();
  const openDetail = useMerchantSettlementPanelStore((s) => s.openDetail);

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
        title="Couldn't load COD settlements"
        description="Something went wrong fetching your settlements."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">COD & Invoices</h1>
      {!settlements || settlements.length === 0 ? (
        <EmptyState title="No settlements yet" description="Settlements appear here once your courier issues one for a period." />
      ) : (
        <div className="overflow-hidden rounded-xl border border-border bg-muted/40">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Period</TableHead>
                <TableHead>Total Payable</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Settled At</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {settlements.map((s) => (
                <TableRow key={s.id} className="cursor-pointer" onClick={() => openDetail(s.id)}>
                  <TableCell>
                    {new Date(s.periodFrom).toLocaleDateString()} – {new Date(s.periodTo).toLocaleDateString()}
                  </TableCell>
                  <TableCell>{currencyFormatter.format(s.totalPayable)}</TableCell>
                  <TableCell><StatusBadge status={s.status} namespace="MerchantSettlement" /></TableCell>
                  <TableCell>{s.settledAt ? new Date(s.settledAt).toLocaleDateString() : "—"}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 8: Mount the detail Sheet in the `(merchant)` layout**

```tsx
// src/web/src/app/(merchant)/layout.tsx — add alongside the Sheets from Tasks 9-10
import { MerchantSettlementDetailSheet } from "@/features/merchant-cod/components/MerchantSettlementDetailSheet";
// ...
      <MerchantPickupCreateSheet />
      <MerchantSettlementDetailSheet />
```

- [ ] **Step 9: Run the full frontend test suite, lint, and type check**

Run: `cd src/web && npx vitest run && npx tsc --noEmit && npx next lint`
Expected: PASS, all green.

- [ ] **Step 10: Commit**

```bash
git add src/web/src/features/merchant-cod/ src/web/src/app/api/merchant/cod/ "src/web/src/app/(merchant)/cod/" "src/web/src/app/(merchant)/layout.tsx"
git commit -m "feat(web): add merchant COD settlements list and printable/exportable invoice Sheet"
```

---

## Task 12: `merchant-kyc` — document list and upload Sheet

**Files:**
- Create: `src/web/src/features/merchant-kyc/types.ts`
- Create: `src/web/src/features/merchant-kyc/hooks/useMerchantKycDocuments.ts`
- Create: `src/web/src/features/merchant-kyc/hooks/useUploadMerchantKycDocument.ts`
- Create: `src/web/src/features/merchant-kyc/store/merchantKycPanelStore.ts`
- Create: `src/web/src/features/merchant-kyc/store/merchantKycPanelStore.test.ts`
- Create: `src/web/src/features/merchant-kyc/components/MerchantKycUploadSheet.tsx`
- Create: `src/web/src/app/api/merchant/kyc-documents/route.ts`
- Create: `src/web/src/app/(merchant)/kyc/page.tsx`
- Modify: `src/web/src/app/(merchant)/layout.tsx`
- Modify: `src/web/src/components/status-badge/statusConfig.ts`

**Interfaces:**
- Consumes: `merchantApiFetch` (Task 5) — the raw-body upload proxy pattern mirrors `app/api/riders/[id]/pod-uploads/route.ts` exactly (`request.arrayBuffer()` forwarded with the original `Content-Type` header, not JSON).
- Produces: `/merchant/kyc` route, matching `MerchantNavMain`'s `KYC` entry.

- [ ] **Step 1: Write the failing panel store test**

```typescript
// src/web/src/features/merchant-kyc/store/merchantKycPanelStore.test.ts
import { describe, expect, it, beforeEach } from "vitest";
import { useMerchantKycPanelStore } from "./merchantKycPanelStore";

describe("useMerchantKycPanelStore", () => {
  beforeEach(() => {
    useMerchantKycPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useMerchantKycPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openUpload switches to the upload panel", () => {
    useMerchantKycPanelStore.getState().openUpload();
    expect(useMerchantKycPanelStore.getState().panel).toEqual({ mode: "upload" });
  });

  it("close returns to closed", () => {
    useMerchantKycPanelStore.getState().openUpload();
    useMerchantKycPanelStore.getState().close();
    expect(useMerchantKycPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails, then write the store, types, hooks, and BFF route**

Run: `cd src/web && npx vitest run src/features/merchant-kyc/store/merchantKycPanelStore.test.ts` — Expected: FAIL (module doesn't exist).

```typescript
// src/web/src/features/merchant-kyc/store/merchantKycPanelStore.ts
import { create } from "zustand";

type MerchantKycPanelState = { mode: "closed" } | { mode: "upload" };

interface MerchantKycPanelStore {
  panel: MerchantKycPanelState;
  openUpload: () => void;
  close: () => void;
}

export const useMerchantKycPanelStore = create<MerchantKycPanelStore>((set) => ({
  panel: { mode: "closed" },
  openUpload: () => set({ panel: { mode: "upload" } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

```typescript
// src/web/src/features/merchant-kyc/types.ts
export type KycDocumentType =
  | "CitizenshipDocument"
  | "PanCard"
  | "BusinessRegistrationCertificate"
  | "BankAccountProof"
  | "Other";
export type KycStatus = "Pending" | "Approved" | "Rejected";

export interface MerchantKycDocument {
  id: string;
  documentType: KycDocumentType;
  status: KycStatus;
  reviewedByUserId: string | null;
}
```

```typescript
// src/web/src/features/merchant-kyc/hooks/useMerchantKycDocuments.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantKycDocument } from "@/features/merchant-kyc/types";

export function useMerchantKycDocuments() {
  return useQuery({
    queryKey: ["merchant-kyc-documents"],
    queryFn: () => clientFetch<MerchantKycDocument[]>("/api/merchant/kyc-documents"),
  });
}
```

```typescript
// src/web/src/features/merchant-kyc/hooks/useUploadMerchantKycDocument.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantKycDocument, KycDocumentType } from "@/features/merchant-kyc/types";

export function useUploadMerchantKycDocument() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ documentType, file }: { documentType: KycDocumentType; file: File }) =>
      clientFetch<MerchantKycDocument>(`/api/merchant/kyc-documents?documentType=${documentType}`, {
        method: "POST",
        headers: { "Content-Type": file.type || "application/octet-stream" },
        body: file,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-kyc-documents"] });
    },
  });
}
```

```typescript
// src/web/src/app/api/merchant/kyc-documents/route.ts
import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET() {
  try {
    const response = await merchantApiFetch("/api/v1/merchant/kyc-documents");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

// Raw request body upload, not multipart/form-data — mirrors the backend's own contract
// (MerchantKycEndpoints.cs's doc comment) and app/api/riders/[id]/pod-uploads/route.ts's
// established proxy pattern exactly: forward the original Content-Type header and raw bytes.
export async function POST(request: Request) {
  const { searchParams } = new URL(request.url);
  const documentType = searchParams.get("documentType") ?? "";
  const contentType = request.headers.get("content-type") ?? "application/octet-stream";
  const bodyBuffer = await request.arrayBuffer();
  try {
    const response = await merchantApiFetch(`/api/v1/merchant/kyc-documents?documentType=${documentType}`, {
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

Run: `cd src/web && npx vitest run src/features/merchant-kyc/store/merchantKycPanelStore.test.ts` — Expected: PASS.

- [ ] **Step 3: Add a `Kyc.*` status badge mapping**

In `src/web/src/components/status-badge/statusConfig.ts`, add to `STATUS_CONFIG`:

```typescript
  // KycStatus — first UI anywhere to render this enum (Frontend Phase F10). Reuses existing
  // tokens: Pending reads as "just submitted, nothing decided yet" (same as Created), Approved
  // as a positive terminal state (same as Delivered), Rejected as a negative terminal state
  // (same as Cancelled).
  "Kyc.Pending": { label: "Pending", token: "status-created" },
  "Kyc.Approved": { label: "Approved", token: "status-delivered" },
  "Kyc.Rejected": { label: "Rejected", token: "status-cancelled" },
```

- [ ] **Step 4: Write `MerchantKycUploadSheet.tsx` and the list page**

```tsx
// src/web/src/features/merchant-kyc/components/MerchantKycUploadSheet.tsx
"use client";

import { useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useUploadMerchantKycDocument } from "@/features/merchant-kyc/hooks/useUploadMerchantKycDocument";
import { useMerchantKycPanelStore } from "@/features/merchant-kyc/store/merchantKycPanelStore";
import type { KycDocumentType } from "@/features/merchant-kyc/types";

const DOCUMENT_TYPE_OPTIONS: { value: KycDocumentType; label: string }[] = [
  { value: "CitizenshipDocument", label: "Citizenship Document" },
  { value: "PanCard", label: "PAN Card" },
  { value: "BusinessRegistrationCertificate", label: "Business Registration Certificate" },
  { value: "BankAccountProof", label: "Bank Account Proof" },
  { value: "Other", label: "Other" },
];

// Mirrors MerchantKycEndpoints.cs's own AllowedContentTypes/MaxUploadSizeBytes exactly — a
// client-side pre-check for a fast error, not a substitute for the backend's own enforcement.
const ALLOWED_CONTENT_TYPES = ["application/pdf", "image/jpeg", "image/png"];
const MAX_UPLOAD_BYTES = 10 * 1024 * 1024;

export function MerchantKycUploadSheet() {
  const panel = useMerchantKycPanelStore((s) => s.panel);
  const close = useMerchantKycPanelStore((s) => s.close);
  const isOpen = panel.mode === "upload";

  const upload = useUploadMerchantKycDocument();
  const [documentType, setDocumentType] = useState<KycDocumentType>("CitizenshipDocument");
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    if (!file) {
      setError("Choose a file to upload.");
      return;
    }
    if (!ALLOWED_CONTENT_TYPES.includes(file.type)) {
      setError("Only PDF, JPEG, or PNG files are supported.");
      return;
    }
    if (file.size > MAX_UPLOAD_BYTES) {
      setError("Files must be 10MB or smaller.");
      return;
    }
    try {
      await upload.mutateAsync({ documentType, file });
      setFile(null);
      close();
    } catch {
      setError("Couldn't upload this document. Try again.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Upload KYC Document</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit} className="mt-6 space-y-6" noValidate>
          <div className="space-y-2">
            <Label htmlFor="documentType">Document Type</Label>
            <Select value={documentType} onValueChange={(v) => setDocumentType(v as KycDocumentType)}>
              <SelectTrigger id="documentType" aria-label="Document type">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {DOCUMENT_TYPE_OPTIONS.map((o) => (
                  <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-2">
            <Label htmlFor="kyc-file">File</Label>
            <Input
              id="kyc-file"
              type="file"
              accept="application/pdf,image/jpeg,image/png"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
            <p className="text-xs text-muted-foreground">PDF, JPEG, or PNG. Max 10MB.</p>
          </div>
          {error && <FieldError id="kyc-upload-error">{error}</FieldError>}
          <Button type="submit" disabled={upload.isPending}>
            {upload.isPending ? <Spinner className="h-4 w-4" /> : "Upload"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
```

```tsx
// src/web/src/app/(merchant)/kyc/page.tsx
"use client";

import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { EmptyState } from "@/components/states/EmptyState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useMerchantKycDocuments } from "@/features/merchant-kyc/hooks/useMerchantKycDocuments";
import { useMerchantKycPanelStore } from "@/features/merchant-kyc/store/merchantKycPanelStore";

const DOCUMENT_TYPE_LABEL: Record<string, string> = {
  CitizenshipDocument: "Citizenship Document",
  PanCard: "PAN Card",
  BusinessRegistrationCertificate: "Business Registration Certificate",
  BankAccountProof: "Bank Account Proof",
  Other: "Other",
};

export default function MerchantKycPage() {
  const { data: documents, isLoading, isError, refetch } = useMerchantKycDocuments();
  const openUpload = useMerchantKycPanelStore((s) => s.openUpload);

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
        title="Couldn't load KYC documents"
        description="Something went wrong fetching your documents."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">KYC</h1>
        <Button size="sm" className="gap-1.5" onClick={() => openUpload()}>
          <Plus className="h-4 w-4" strokeWidth={2} />
          Upload Document
        </Button>
      </div>
      {!documents || documents.length === 0 ? (
        <EmptyState title="No documents uploaded" description="Upload your KYC documents to complete verification." />
      ) : (
        <div className="overflow-hidden rounded-xl border border-border bg-muted/40">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Document Type</TableHead>
                <TableHead>Status</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {documents.map((d) => (
                <TableRow key={d.id}>
                  <TableCell>{DOCUMENT_TYPE_LABEL[d.documentType] ?? d.documentType}</TableCell>
                  <TableCell><StatusBadge status={d.status} namespace="Kyc" /></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 5: Mount the upload Sheet in the `(merchant)` layout**

```tsx
// src/web/src/app/(merchant)/layout.tsx — add alongside the Sheets from Tasks 9-11
import { MerchantKycUploadSheet } from "@/features/merchant-kyc/components/MerchantKycUploadSheet";
// ...
      <MerchantSettlementDetailSheet />
      <MerchantKycUploadSheet />
```

- [ ] **Step 6: Run the full frontend test suite, lint, and type check**

Run: `cd src/web && npx vitest run && npx tsc --noEmit && npx next lint`
Expected: PASS, all green.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/merchant-kyc/ src/web/src/app/api/merchant/kyc-documents/ "src/web/src/app/(merchant)/kyc/" "src/web/src/app/(merchant)/layout.tsx" src/web/src/components/status-badge/statusConfig.ts
git commit -m "feat(web): add merchant KYC document list and upload UI"
```

---

## Task 13: `merchant-staff` — Owner-only staff list and invite Sheet

**Files:**
- Create: `src/web/src/features/merchant-staff/types.ts`
- Create: `src/web/src/features/merchant-staff/schema.ts`
- Create: `src/web/src/features/merchant-staff/hooks/useMerchantStaff.ts`
- Create: `src/web/src/features/merchant-staff/hooks/useInviteMerchantStaff.ts`
- Create: `src/web/src/features/merchant-staff/store/merchantStaffPanelStore.ts`
- Create: `src/web/src/features/merchant-staff/store/merchantStaffPanelStore.test.ts`
- Create: `src/web/src/features/merchant-staff/components/MerchantStaffInviteSheet.tsx`
- Create: `src/web/src/app/api/merchant/staff/route.ts`
- Create: `src/web/src/app/(merchant)/staff/page.tsx`
- Modify: `src/web/src/app/(merchant)/layout.tsx`

**Interfaces:**
- Consumes: `merchantApiFetch` (Task 5), `useMerchantPermission` (Task 6, gated on `"merchant.staff.manage"`).
- Produces: `/merchant/staff` route, matching `MerchantNavMain`'s Owner-only `Staff` entry. No edit/deactivate UI — `MerchantStaffEndpoints` has no such route (recorded as deferred in `docs/CLARIFICATIONS.md` entry #19).

- [ ] **Step 1: Write the failing panel store test**

```typescript
// src/web/src/features/merchant-staff/store/merchantStaffPanelStore.test.ts
import { describe, expect, it, beforeEach } from "vitest";
import { useMerchantStaffPanelStore } from "./merchantStaffPanelStore";

describe("useMerchantStaffPanelStore", () => {
  beforeEach(() => {
    useMerchantStaffPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useMerchantStaffPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate switches to the create panel", () => {
    useMerchantStaffPanelStore.getState().openCreate();
    expect(useMerchantStaffPanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("close returns to closed", () => {
    useMerchantStaffPanelStore.getState().openCreate();
    useMerchantStaffPanelStore.getState().close();
    expect(useMerchantStaffPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails, then write the store, types, schema, hooks, and BFF route**

Run: `cd src/web && npx vitest run src/features/merchant-staff/store/merchantStaffPanelStore.test.ts` — Expected: FAIL (module doesn't exist).

```typescript
// src/web/src/features/merchant-staff/store/merchantStaffPanelStore.ts
import { create } from "zustand";

type MerchantStaffPanelState = { mode: "closed" } | { mode: "create" };

interface MerchantStaffPanelStore {
  panel: MerchantStaffPanelState;
  openCreate: () => void;
  close: () => void;
}

export const useMerchantStaffPanelStore = create<MerchantStaffPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

```typescript
// src/web/src/features/merchant-staff/types.ts
export interface MerchantStaffMember {
  id: string;
  email: string;
  fullName: string;
  role: "Owner" | "Staff";
  isActive: boolean;
}
```

```typescript
// src/web/src/features/merchant-staff/schema.ts
import { z } from "zod";

export const inviteMerchantStaffSchema = z.object({
  email: z.string().email("Enter a valid email address."),
  password: z.string().min(8, "Password must be at least 8 characters."),
  fullName: z.string().min(1, "Full name is required."),
});

export type InviteMerchantStaffFormValues = z.infer<typeof inviteMerchantStaffSchema>;
```

```typescript
// src/web/src/features/merchant-staff/hooks/useMerchantStaff.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantStaffMember } from "@/features/merchant-staff/types";

export function useMerchantStaff() {
  return useQuery({
    queryKey: ["merchant-staff"],
    queryFn: () => clientFetch<MerchantStaffMember[]>("/api/merchant/staff"),
  });
}
```

```typescript
// src/web/src/features/merchant-staff/hooks/useInviteMerchantStaff.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantStaffMember } from "@/features/merchant-staff/types";
import type { InviteMerchantStaffFormValues } from "@/features/merchant-staff/schema";

export function useInviteMerchantStaff() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: InviteMerchantStaffFormValues) =>
      clientFetch<MerchantStaffMember>("/api/merchant/staff", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-staff"] });
    },
  });
}
```

```typescript
// src/web/src/app/api/merchant/staff/route.ts
import { NextRequest, NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET() {
  try {
    const response = await merchantApiFetch("/api/v1/merchant/staff");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await merchantApiFetch("/api/v1/merchant/staff", {
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

Run: `cd src/web && npx vitest run src/features/merchant-staff/store/merchantStaffPanelStore.test.ts` — Expected: PASS.

- [ ] **Step 3: Write `MerchantStaffInviteSheet.tsx` and the list page**

```tsx
// src/web/src/features/merchant-staff/components/MerchantStaffInviteSheet.tsx
"use client";

import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useInviteMerchantStaff } from "@/features/merchant-staff/hooks/useInviteMerchantStaff";
import { useMerchantStaffPanelStore } from "@/features/merchant-staff/store/merchantStaffPanelStore";
import { inviteMerchantStaffSchema, type InviteMerchantStaffFormValues } from "@/features/merchant-staff/schema";

export function MerchantStaffInviteSheet() {
  const panel = useMerchantStaffPanelStore((s) => s.panel);
  const close = useMerchantStaffPanelStore((s) => s.close);
  const isOpen = panel.mode === "create";

  const inviteStaff = useInviteMerchantStaff();
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<InviteMerchantStaffFormValues>({ resolver: zodResolver(inviteMerchantStaffSchema) });

  async function onSubmit(values: InviteMerchantStaffFormValues) {
    setServerError(null);
    try {
      await inviteStaff.mutateAsync(values);
      reset();
      close();
    } catch {
      setServerError("Couldn't invite this staff member. The email may already be in use.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Invite Staff</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-6" noValidate>
          <div className="space-y-2">
            <Label htmlFor="fullName">Full Name</Label>
            <Input
              id="fullName"
              aria-invalid={!!errors.fullName}
              aria-describedby={errors.fullName ? "fullName-error" : undefined}
              {...register("fullName")}
            />
            {errors.fullName && <FieldError id="fullName-error">{errors.fullName.message}</FieldError>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="email">Email</Label>
            <Input
              id="email"
              type="email"
              aria-invalid={!!errors.email}
              aria-describedby={errors.email ? "email-error" : undefined}
              {...register("email")}
            />
            {errors.email && <FieldError id="email-error">{errors.email.message}</FieldError>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="password">Temporary Password</Label>
            <Input
              id="password"
              type="password"
              aria-invalid={!!errors.password}
              aria-describedby={errors.password ? "password-error" : undefined}
              {...register("password")}
            />
            {errors.password && <FieldError id="password-error">{errors.password.message}</FieldError>}
          </div>
          {serverError && <FieldError id="merchant-staff-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Invite Staff Member"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
```

```tsx
// src/web/src/app/(merchant)/staff/page.tsx
"use client";

import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { EmptyState } from "@/components/states/EmptyState";
import { Badge } from "@/components/ui/badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useMerchantStaff } from "@/features/merchant-staff/hooks/useMerchantStaff";
import { useMerchantStaffPanelStore } from "@/features/merchant-staff/store/merchantStaffPanelStore";

export default function MerchantStaffPage() {
  const { data: staff, isLoading, isError, refetch } = useMerchantStaff();
  const openCreate = useMerchantStaffPanelStore((s) => s.openCreate);

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
        title="Couldn't load staff"
        description="Something went wrong fetching your staff list."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Staff</h1>
        <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
          <Plus className="h-4 w-4" strokeWidth={2} />
          Invite Staff
        </Button>
      </div>
      {!staff || staff.length === 0 ? (
        <EmptyState title="No staff members yet" description="Invite your team to help manage shipments." />
      ) : (
        <div className="overflow-hidden rounded-xl border border-border bg-muted/40">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Email</TableHead>
                <TableHead>Role</TableHead>
                <TableHead>Status</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {staff.map((s) => (
                <TableRow key={s.id}>
                  <TableCell>{s.fullName}</TableCell>
                  <TableCell>{s.email}</TableCell>
                  <TableCell>{s.role}</TableCell>
                  <TableCell>
                    <Badge variant={s.isActive ? "secondary" : "outline"}>{s.isActive ? "Active" : "Inactive"}</Badge>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Mount the invite Sheet in the `(merchant)` layout**

```tsx
// src/web/src/app/(merchant)/layout.tsx — add alongside the Sheets from Tasks 9-12
import { MerchantStaffInviteSheet } from "@/features/merchant-staff/components/MerchantStaffInviteSheet";
// ...
      <MerchantKycUploadSheet />
      <MerchantStaffInviteSheet />
```

- [ ] **Step 5: Run the full frontend test suite, lint, and type check**

Run: `cd src/web && npx vitest run && npx tsc --noEmit && npx next lint`
Expected: PASS, all green.

- [ ] **Step 6: Commit**

```bash
git add src/web/src/features/merchant-staff/ src/web/src/app/api/merchant/staff/ "src/web/src/app/(merchant)/staff/" "src/web/src/app/(merchant)/layout.tsx"
git commit -m "feat(web): add merchant staff list and invite UI (Owner-only)"
```

---

## Task 14: `merchant-api-keys` — Owner-only key list, create with one-time reveal, revoke

**Files:**
- Create: `src/web/src/features/merchant-api-keys/types.ts`
- Create: `src/web/src/features/merchant-api-keys/hooks/useMerchantApiKeys.ts`
- Create: `src/web/src/features/merchant-api-keys/hooks/useCreateMerchantApiKey.ts`
- Create: `src/web/src/features/merchant-api-keys/hooks/useRevokeMerchantApiKey.ts`
- Create: `src/web/src/features/merchant-api-keys/store/merchantApiKeyPanelStore.ts`
- Create: `src/web/src/features/merchant-api-keys/store/merchantApiKeyPanelStore.test.ts`
- Create: `src/web/src/features/merchant-api-keys/components/MerchantApiKeyCreateSheet.tsx`
- Create: `src/web/src/app/api/merchant/api-keys/route.ts`
- Create: `src/web/src/app/api/merchant/api-keys/[id]/route.ts`
- Create: `src/web/src/app/(merchant)/api-keys/page.tsx`
- Modify: `src/web/src/app/(merchant)/layout.tsx`

**Interfaces:**
- Consumes: `merchantApiFetch` (Task 5), `useMerchantPermission` (Task 6, gated on `"merchant.apikeys.manage"`).
- Produces: `/merchant/api-keys` route, matching `MerchantNavMain`'s Owner-only `API Keys` entry.

- [ ] **Step 1: Write the failing panel store test**

```typescript
// src/web/src/features/merchant-api-keys/store/merchantApiKeyPanelStore.test.ts
import { describe, expect, it, beforeEach } from "vitest";
import { useMerchantApiKeyPanelStore } from "./merchantApiKeyPanelStore";

describe("useMerchantApiKeyPanelStore", () => {
  beforeEach(() => {
    useMerchantApiKeyPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useMerchantApiKeyPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate switches to the create panel", () => {
    useMerchantApiKeyPanelStore.getState().openCreate();
    expect(useMerchantApiKeyPanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("close returns to closed", () => {
    useMerchantApiKeyPanelStore.getState().openCreate();
    useMerchantApiKeyPanelStore.getState().close();
    expect(useMerchantApiKeyPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails, then write the store, types, hooks, and BFF routes**

Run: `cd src/web && npx vitest run src/features/merchant-api-keys/store/merchantApiKeyPanelStore.test.ts` — Expected: FAIL (module doesn't exist).

```typescript
// src/web/src/features/merchant-api-keys/store/merchantApiKeyPanelStore.ts
import { create } from "zustand";

type MerchantApiKeyPanelState = { mode: "closed" } | { mode: "create" };

interface MerchantApiKeyPanelStore {
  panel: MerchantApiKeyPanelState;
  openCreate: () => void;
  close: () => void;
}

export const useMerchantApiKeyPanelStore = create<MerchantApiKeyPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

```typescript
// src/web/src/features/merchant-api-keys/types.ts
export interface ApiKey {
  id: string;
  prefix: string;
  scopes: string[];
  isActive: boolean;
  createdAt: string;
  revokedAt: string | null;
  lastUsedAt: string | null;
}

// PlaintextKey appears here only — the one-time reveal, immediately after POST. Never
// reconstructable from ApiKey or any later read (mirrors ApiKeyCreatedResponse's own doc
// comment).
export interface ApiKeyCreated {
  id: string;
  plaintextKey: string;
  prefix: string;
  scopes: string[];
  createdAt: string;
}
```

```typescript
// src/web/src/features/merchant-api-keys/hooks/useMerchantApiKeys.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ApiKey } from "@/features/merchant-api-keys/types";

export function useMerchantApiKeys() {
  return useQuery({
    queryKey: ["merchant-api-keys"],
    queryFn: () => clientFetch<ApiKey[]>("/api/merchant/api-keys"),
  });
}
```

```typescript
// src/web/src/features/merchant-api-keys/hooks/useCreateMerchantApiKey.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ApiKeyCreated } from "@/features/merchant-api-keys/types";

export function useCreateMerchantApiKey() {
  const queryClient = useQueryClient();
  return useMutation({
    // null scopes means "default to every permission my role grants" (CreateApiKeyRequest's own
    // doc comment) — there is no way to request zero scopes, matching the backend contract.
    mutationFn: (scopes: string[] | null) =>
      clientFetch<ApiKeyCreated>("/api/merchant/api-keys", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ scopes }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-api-keys"] });
    },
  });
}
```

```typescript
// src/web/src/features/merchant-api-keys/hooks/useRevokeMerchantApiKey.ts
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ApiKey } from "@/features/merchant-api-keys/types";

export function useRevokeMerchantApiKey() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      clientFetch<ApiKey>(`/api/merchant/api-keys/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-api-keys"] });
    },
  });
}
```

```typescript
// src/web/src/app/api/merchant/api-keys/route.ts
import { NextRequest, NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET() {
  try {
    const response = await merchantApiFetch("/api/v1/merchant/api-keys");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await merchantApiFetch("/api/v1/merchant/api-keys", {
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

```typescript
// src/web/src/app/api/merchant/api-keys/[id]/route.ts
import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function DELETE(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await merchantApiFetch(`/api/v1/merchant/api-keys/${params.id}`, { method: "DELETE" });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Run: `cd src/web && npx vitest run src/features/merchant-api-keys/store/merchantApiKeyPanelStore.test.ts` — Expected: PASS.

- [ ] **Step 3: Write `MerchantApiKeyCreateSheet.tsx` and the list page**

```tsx
// src/web/src/features/merchant-api-keys/components/MerchantApiKeyCreateSheet.tsx
"use client";

import { useState } from "react";
import { Check, Copy } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useCreateMerchantApiKey } from "@/features/merchant-api-keys/hooks/useCreateMerchantApiKey";
import { useMerchantApiKeyPanelStore } from "@/features/merchant-api-keys/store/merchantApiKeyPanelStore";
import type { ApiKeyCreated } from "@/features/merchant-api-keys/types";

// Every MerchantPermissionCatalog constant except ApiKeysManage — an issued key can never carry
// that scope itself (server-side refuses it unconditionally, ApiKeyService.CreateApiKeyAsync's
// own doc comment), so it's excluded from the picker rather than offered and silently dropped.
const AVAILABLE_SCOPES = [
  "merchant.profile.read",
  "merchant.profile.update",
  "merchant.staff.manage",
  "merchant.kyc.upload",
  "shipment.create",
  "shipment.read",
  "shipment.cancel",
  "merchant.webhooks.manage",
  "merchant.pickups.create",
  "merchant.pickups.read",
  "merchant.pickups.cancel",
  "merchant.cod.read",
  "merchant.pricing.read",
  "merchant.branches.read",
];

export function MerchantApiKeyCreateSheet() {
  const panel = useMerchantApiKeyPanelStore((s) => s.panel);
  const close = useMerchantApiKeyPanelStore((s) => s.close);
  const isOpen = panel.mode === "create";

  const createKey = useCreateMerchantApiKey();
  const [selectedScopes, setSelectedScopes] = useState<string[]>([]);
  const [createdKey, setCreatedKey] = useState<ApiKeyCreated | null>(null);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function toggleScope(scope: string, checked: boolean) {
    setSelectedScopes((prev) => (checked ? [...prev, scope] : prev.filter((s) => s !== scope)));
  }

  async function handleCreate() {
    setError(null);
    try {
      const result = await createKey.mutateAsync(selectedScopes.length > 0 ? selectedScopes : null);
      setCreatedKey(result);
    } catch {
      setError("Couldn't create an API key. Try again.");
    }
  }

  function handleClose(open: boolean) {
    if (!open) {
      close();
      setCreatedKey(null);
      setSelectedScopes([]);
      setCopied(false);
    }
  }

  async function handleCopy() {
    if (!createdKey) return;
    await navigator.clipboard.writeText(createdKey.plaintextKey);
    setCopied(true);
  }

  return (
    <Sheet open={isOpen} onOpenChange={handleClose}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Create API Key</SheetTitle>
        </SheetHeader>

        {!createdKey ? (
          <div className="mt-6 space-y-6">
            <div className="space-y-2">
              <Label>Scopes</Label>
              <p className="text-xs text-muted-foreground">
                Leave every scope unchecked to grant every permission your role holds. Select
                specific scopes for a narrower key.
              </p>
              <div className="max-h-64 space-y-2 overflow-y-auto rounded-lg border border-border p-3">
                {AVAILABLE_SCOPES.map((scope) => (
                  <div key={scope} className="flex items-center gap-2">
                    <Checkbox
                      id={`scope-${scope}`}
                      checked={selectedScopes.includes(scope)}
                      onCheckedChange={(checked) => toggleScope(scope, checked === true)}
                    />
                    <Label htmlFor={`scope-${scope}`} className="font-mono text-xs font-normal">
                      {scope}
                    </Label>
                  </div>
                ))}
              </div>
            </div>
            {error && <FieldError id="merchant-api-key-error">{error}</FieldError>}
            <Button type="button" onClick={handleCreate} disabled={createKey.isPending}>
              {createKey.isPending ? <Spinner className="h-4 w-4" /> : "Create API Key"}
            </Button>
          </div>
        ) : (
          <div className="mt-6 space-y-4">
            <div className="rounded-lg border border-destructive/40 bg-destructive/5 p-4">
              <p className="text-sm font-semibold text-foreground">Copy this key now</p>
              <p className="mt-1 text-xs text-muted-foreground">
                You won&apos;t be able to see this key again after closing this panel.
              </p>
              <div className="mt-3 flex items-center gap-2 rounded-md border border-border bg-background p-2">
                <code className="flex-1 overflow-x-auto text-xs">{createdKey.plaintextKey}</code>
                <Button type="button" variant="outline" size="icon" onClick={handleCopy} aria-label="Copy key">
                  {copied ? <Check className="h-4 w-4" strokeWidth={2} /> : <Copy className="h-4 w-4" strokeWidth={2} />}
                </Button>
              </div>
            </div>
            <Button type="button" variant="outline" onClick={() => handleClose(false)}>
              Done
            </Button>
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
```

```tsx
// src/web/src/app/(merchant)/api-keys/page.tsx
"use client";

import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { EmptyState } from "@/components/states/EmptyState";
import { Badge } from "@/components/ui/badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useMerchantApiKeys } from "@/features/merchant-api-keys/hooks/useMerchantApiKeys";
import { useRevokeMerchantApiKey } from "@/features/merchant-api-keys/hooks/useRevokeMerchantApiKey";
import { useMerchantApiKeyPanelStore } from "@/features/merchant-api-keys/store/merchantApiKeyPanelStore";

export default function MerchantApiKeysPage() {
  const { data: keys, isLoading, isError, refetch } = useMerchantApiKeys();
  const openCreate = useMerchantApiKeyPanelStore((s) => s.openCreate);
  const revokeKey = useRevokeMerchantApiKey();

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
        title="Couldn't load API keys"
        description="Something went wrong fetching your API keys."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">API Keys</h1>
        <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
          <Plus className="h-4 w-4" strokeWidth={2} />
          Create API Key
        </Button>
      </div>
      {!keys || keys.length === 0 ? (
        <EmptyState title="No API keys yet" description="Create a key to integrate with the Merchant API." />
      ) : (
        <div className="overflow-hidden rounded-xl border border-border bg-muted/40">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Prefix</TableHead>
                <TableHead>Scopes</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Created At</TableHead>
                <TableHead>Last Used</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {keys.map((k) => (
                <TableRow key={k.id}>
                  <TableCell className="font-mono text-xs">{k.prefix}</TableCell>
                  <TableCell className="text-xs">{k.scopes.length} scope{k.scopes.length === 1 ? "" : "s"}</TableCell>
                  <TableCell>
                    <Badge variant={k.isActive ? "secondary" : "outline"}>{k.isActive ? "Active" : "Revoked"}</Badge>
                  </TableCell>
                  <TableCell>{new Date(k.createdAt).toLocaleDateString()}</TableCell>
                  <TableCell>{k.lastUsedAt ? new Date(k.lastUsedAt).toLocaleDateString() : "Never"}</TableCell>
                  <TableCell>
                    {k.isActive && (
                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={revokeKey.isPending}
                        onClick={() => revokeKey.mutate(k.id)}
                      >
                        Revoke
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Mount the create Sheet in the `(merchant)` layout**

```tsx
// src/web/src/app/(merchant)/layout.tsx — add alongside the Sheets from Tasks 9-13
import { MerchantApiKeyCreateSheet } from "@/features/merchant-api-keys/components/MerchantApiKeyCreateSheet";
// ...
      <MerchantStaffInviteSheet />
      <MerchantApiKeyCreateSheet />
```

- [ ] **Step 5: Run the full frontend test suite, lint, and type check**

Run: `cd src/web && npx vitest run && npx tsc --noEmit && npx next lint`
Expected: PASS, all green.

- [ ] **Step 6: Commit**

```bash
git add src/web/src/features/merchant-api-keys/ src/web/src/app/api/merchant/api-keys/ "src/web/src/app/(merchant)/api-keys/" "src/web/src/app/(merchant)/layout.tsx"
git commit -m "feat(web): add merchant API key management UI with one-time secret reveal"
```

---

## Task 15: Merchant dashboard stat tiles

**Files:**
- Create: `src/web/src/features/merchant-dashboard/components/MerchantDashboardStats.tsx`
- Create: `src/web/src/features/merchant-dashboard/components/MerchantDashboardStats.test.tsx`
- Modify: `src/web/src/app/(merchant)/dashboard/page.tsx`

**Interfaces:**
- Consumes: `useMerchantShipments` (Task 8), `useMerchantSettlements` (Task 11) — this task could only land after both existed, which is why it was deferred past Task 7's placeholder dashboard.
- Produces: the real `/merchant/dashboard` content, replacing Task 7's greeting-only placeholder.

- [ ] **Step 1: Write the failing test**

```tsx
// src/web/src/features/merchant-dashboard/components/MerchantDashboardStats.test.tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MerchantDashboardStats } from "./MerchantDashboardStats";

vi.mock("@/features/merchant-shipments/hooks/useMerchantShipments", () => ({
  useMerchantShipments: () => ({
    data: {
      items: [
        { id: "s1", currentStatus: "Created" },
        { id: "s2", currentStatus: "Created" },
        { id: "s3", currentStatus: "InTransit" },
        { id: "s4", currentStatus: "OutForDelivery" },
        { id: "s5", currentStatus: "Delivered" },
      ],
      page: 1, pageSize: 100, totalCount: 5, totalPages: 1,
    },
  }),
}));

vi.mock("@/features/merchant-cod/hooks/useMerchantSettlements", () => ({
  useMerchantSettlements: () => ({
    data: [
      { id: "st1", status: "Created", totalPayable: 1500 },
      { id: "st2", status: "Settled", totalPayable: 3000 },
    ],
  }),
}));

function renderWithClient() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MerchantDashboardStats />
    </QueryClientProvider>
  );
}

describe("MerchantDashboardStats", () => {
  it("counts shipments by status group", async () => {
    renderWithClient();

    await waitFor(() => expect(screen.getByText("2")).toBeInTheDocument()); // Created
    expect(screen.getByText("1")).toBeInTheDocument(); // In Transit (single InTransit row)
  });

  it("sums COD pending and settled from settlements", async () => {
    renderWithClient();

    await waitFor(() => expect(screen.getByText("Rs. 1,500.00")).toBeInTheDocument());
    expect(screen.getByText("Rs. 3,000.00")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run src/features/merchant-dashboard/components/MerchantDashboardStats.test.tsx`
Expected: FAIL — module does not exist yet. (If the exact `Intl.NumberFormat("en-NP", ...)` currency-symbol text differs from `"Rs. 1,500.00"` in the test environment's ICU data, adjust the two currency assertions to match whatever `currencyFormatter.format(1500)` actually renders — check with a quick `console.log` in the test file if the first run's diff shows a different string, then fix the assertion, not the formatter.)

- [ ] **Step 3: Write `MerchantDashboardStats.tsx`**

```tsx
// src/web/src/features/merchant-dashboard/components/MerchantDashboardStats.tsx
"use client";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useMerchantShipments } from "@/features/merchant-shipments/hooks/useMerchantShipments";
import { useMerchantSettlements } from "@/features/merchant-cod/hooks/useMerchantSettlements";

const currencyFormatter = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

function StatTile({ label, value }: { label: string; value: string }) {
  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-xs font-medium text-muted-foreground">{label}</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="font-display text-2xl font-semibold text-foreground">{value}</p>
      </CardContent>
    </Card>
  );
}

// Small client-side aggregation over already-fetched list data (first 100 shipments, every
// settlement) — no new backend dashboard-aggregation endpoint, matching the design spec's
// resolved "no new backend read needed" decision for this screen.
export function MerchantDashboardStats() {
  const { data: shipments } = useMerchantShipments(1, 100);
  const { data: settlements } = useMerchantSettlements();

  const items = shipments?.items ?? [];
  const created = items.filter((s) => s.currentStatus === "Created").length;
  const inTransit = items.filter((s) =>
    ["PickedUp", "AtOriginBranch", "Sorted", "InTransit", "AtDestinationBranch"].includes(s.currentStatus)
  ).length;
  const outForDelivery = items.filter((s) => s.currentStatus === "OutForDelivery").length;
  const delivered = items.filter((s) => s.currentStatus === "Delivered").length;

  const codPending = (settlements ?? [])
    .filter((s) => s.status === "Created")
    .reduce((sum, s) => sum + s.totalPayable, 0);
  const codSettled = (settlements ?? [])
    .filter((s) => s.status === "Settled")
    .reduce((sum, s) => sum + s.totalPayable, 0);

  return (
    <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
      <StatTile label="Created" value={String(created)} />
      <StatTile label="In Transit" value={String(inTransit)} />
      <StatTile label="Out for Delivery" value={String(outForDelivery)} />
      <StatTile label="Delivered" value={String(delivered)} />
      <StatTile label="COD Pending" value={currencyFormatter.format(codPending)} />
      <StatTile label="COD Settled" value={currencyFormatter.format(codSettled)} />
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npx vitest run src/features/merchant-dashboard/components/MerchantDashboardStats.test.tsx`
Expected: PASS

- [ ] **Step 5: Wire it into the dashboard page**

```tsx
// src/web/src/app/(merchant)/dashboard/page.tsx
"use client";

import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";
import { MerchantDashboardStats } from "@/features/merchant-dashboard/components/MerchantDashboardStats";

export default function MerchantDashboardPage() {
  const { data: session } = useMerchantSession();
  return (
    <div className="space-y-6">
      <div>
        <h1 className="font-display text-2xl font-semibold tracking-tight text-foreground">
          {session ? `Welcome back, ${session.fullName}` : "Dashboard"}
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">{session?.merchantDisplayName}</p>
      </div>
      <MerchantDashboardStats />
    </div>
  );
}
```

- [ ] **Step 6: Run the full frontend test suite, lint, and type check**

Run: `cd src/web && npx vitest run && npx tsc --noEmit && npx next lint`
Expected: PASS, all green.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/merchant-dashboard/ "src/web/src/app/(merchant)/dashboard/page.tsx"
git commit -m "feat(web): add merchant dashboard stat tiles"
```

---

## Task 16: Manual E2E verification and `FRONTEND-ROADMAP.md` completion notes

**Files:**
- Modify: `docs/FRONTEND-ROADMAP.md`

**Interfaces:** None — this task is verification and documentation only, no new production code.

Every prior frontend phase's own manual E2E pass against the real API/Postgres (not mocks) found real bugs invisible to unit tests alone. Run both servers live (`dotnet run` from `src/Api`, `npm run dev` from `src/web`), driven through the real Next.js BFF proxy layer with real cookies — not `clientFetch`/`apiFetch` mocks.

- [ ] **Step 1: Seed prerequisites via the API**

Register a fresh organization (`POST /api/v1/auth/register`), then via the staff track seed: a branch with an assigned delivery zone (`POST /api/v1/branches`, `PUT /api/v1/branches/{id}/delivery-zone`), a `ServiceType`/rate card/zone rate (existing seed data or `POST /api/v1/rate-cards`), a `Merchant` with an Owner (`POST /api/v1/merchants`, per `MerchantsEndpoints`), and a rider. Use the exact request shapes `MerchantAuthAndStaffEndpointsTests.cs`/`MerchantApiExitCriteriaEndToEndTests.cs` already use — these are proven-correct fixtures, not shapes to reinvent.

- [ ] **Step 2: Merchant login and session**

Navigate to `/merchant-login`, log in as the seeded Owner. Confirm redirect to `/merchant/dashboard`, the sidebar shows the merchant's display name (not an organization name), and `MerchantNavMain` shows every entry (Owner holds every permission). Confirm `/merchant/staff` and `/merchant/api-keys` are visible.

- [ ] **Step 3: Book a shipment with a live quote preview**

Open the Shipments Sheet, select origin/destination branch (confirm the picker is populated — proves `GET /api/v1/merchant/branches` works end-to-end) and a service type (proves the dual-auth `/service-types` route). Fill in weight, recipient/sender geography and items. Confirm the estimated price tile updates after the 500ms debounce (proves the dual-auth `/pricing/quote` route and the server-side `MerchantId` override — no client-visible difference expected here, but confirm the quote is nonzero and matches the seeded rate card). Submit. Confirm the detail Sheet opens on the new AWB with `Status: Created`.

- [ ] **Step 4: Raise and cancel a pickup request**

Open the Pickups Sheet, confirm the just-booked shipment appears in the picker (its status is still `Created`). Select it, pick a requested date, submit. Confirm the new row appears with `Status: Requested`. Cancel it via the row action; confirm it flips to `Cancelled` and the Cancel button disappears.

- [ ] **Step 5: Drive the shipment to delivery with COD via the staff side**

Book a second shipment through the merchant portal with `codAmount > 0`. Using the staff app (`/shipments`, `/manifests`, `/riders`, `/my-deliveries` — the existing F3-F5 flows), assign it to a rider, pickup-scan it, receive it at origin, move it through a manifest to the destination branch, assign a delivery task, and complete delivery with a collected COD amount. Confirm back in the merchant portal that `/merchant/shipments`'s detail Sheet shows `Status: Delivered` and the Cancel button is gone (terminal status).

- [ ] **Step 6: Settle COD and verify the invoice view**

From the staff `/cod` page (F7), create a rider settlement, branch settlement, finance verification, then a `MerchantSettlement` for the period covering the delivered shipment, and settle it. Back in the merchant portal's `/merchant/cod`, confirm the settlement appears with `Status: Settled` and the correct `Total Payable`. Open its detail Sheet: confirm the line item shows the correct COD amount, courier fee, and net payable; click Print (confirm the browser print preview hides the Print/Download buttons and nav chrome via the `merchant-invoice-no-print` class); click Download CSV and open the downloaded file, confirming the header row and the line item's figures match what's on screen.

- [ ] **Step 7: KYC upload**

Open `/merchant/kyc`, upload a small PDF or JPEG as a `CitizenshipDocument`. Confirm it appears in the list with `Status: Pending` and the correct document type label. Attempt uploading a `.txt` file; confirm the client-side rejection message appears before any request is sent (check the Network tab shows no request fired).

- [ ] **Step 8: Staff invite and Staff-role nav gating**

Open `/merchant/staff`, invite a new Staff-role user with a distinct email. Log out, log back in as that new Staff user. Confirm `MerchantNavMain` now hides `Staff` and `API Keys` (Staff role lacks `merchant.staff.manage`/`merchant.apikeys.manage`) but still shows Shipments/Pickups/COD & Invoices/KYC. Confirm navigating directly to `/merchant/staff`'s URL still loads the page shell but the list call fails (403 surfaces as the page's `ErrorState`) — proving the gate is enforced server-side, not just hidden client-side, per `CLAUDE.md` §33.

- [ ] **Step 9: API key lifecycle**

Log back in as the Owner. Open `/merchant/api-keys`, create a key with no scopes selected (defaults to every Owner permission). Confirm the plaintext reveal callout appears exactly once; close the Sheet and reopen it — confirm the plaintext is gone and cannot be recovered from the list (`ApiKey.plaintextKey` is never in `GET /api/v1/merchant/api-keys`'s response). Revoke the key from the list; confirm its badge flips to "Revoked" and the Revoke button disappears.

- [ ] **Step 10: Fix any real bugs found**

If Steps 2-9 surface any real defect (not a pre-existing, already-documented gap like the missing tracking-event timeline or `MerchantAddress` endpoint), fix it directly, add a regression test if the bug class is testable, and commit as its own `fix:` commit before proceeding — matching every prior phase's practice of fixing real E2E-discovered bugs immediately rather than deferring them.

- [ ] **Step 11: Update `docs/FRONTEND-ROADMAP.md`**

Find this roadmap's F10 entry (currently under "Later (not yet planned in detail)") and replace it with a completed entry, following the exact shape F5-F9's own completed entries use in the same file — entry text, exit-criteria confirmation, and a completion note summarizing what shipped and what was deliberately deferred (mirroring this task's Steps 2-9 and the four backend gaps from Task 1-4). Example shape (adapt the specifics to what actually shipped/what bugs Step 10 found):

```markdown
## Phase F10 — Merchant Portal

Booking, tracking, COD settlements/invoices, KYC, staff management, and API key management for
merchants — the original backend Phase 12b scope, on top of Phase 12's merchant JWT/API-key
track.

*Entry:* F9 done. *Exit:* a merchant can log in through a session fully separate from staff,
book a shipment with a live price preview, track it through delivery, raise and cancel pickup
requests, view and export their COD settlements as invoices, upload and track KYC documents,
invite staff (Owner only), and manage API keys (Owner only).

*Status: Done (<date>).* [fill in with the actual completion summary, backend gaps closed,
and any real bugs Step 10 found — matching the level of detail F5-F9's own entries in this file
already show.]
```

- [ ] **Step 12: Commit**

```bash
git add docs/FRONTEND-ROADMAP.md
git commit -m "docs: mark Frontend Phase F10 (Merchant Portal) complete"
```

---

## Final Step: Whole-branch review

Once all 16 tasks are committed, request a whole-branch review against this plan and the spec at `docs/superpowers/specs/2026-09-03-frontend-phase-f10-merchant-portal-design.md` before merging — matching every prior phase's practice of a final review pass catching cross-task defects invisible to any single task's own review. Pay particular attention to:

- **Cross-tenant/cross-merchant isolation** (this project's most recurring bug class, hit in nearly every backend phase): `BranchService.ListBranchesForMerchantAsync` (Task 4) relying correctly on the ambient `AppDbContext` tenant filter with no accidental `IgnoreQueryFilters()`; the `PricingEndpoints` quote route (Task 3) actually overriding `MerchantId` on every code path, not just the one this plan's snippet shows; `MerchantAuthService.GetMeAsync` (Task 2) never leaking another merchant's data even for a crafted `sub` claim.
- **The JWT-only self-escalation carve-out**: confirm `GET /api/v1/merchant/auth/me` (Task 2, bare `.RequireAuthorization()`) is genuinely unreachable by an `ApiKey`-scheme credential, not just untested — same bar `ApiKeyEndpoints`' three routes already enforce.
- **Every merchant BFF route** (`app/api/merchant/**/route.ts`, Tasks 8, 10-14) using `merchantApiFetch`, never `apiFetch` — a single wrong import would silently leak the staff session's bearer token onto a merchant-facing route or vice versa.
- **The `(merchant)/layout.tsx` incremental edits across Tasks 9-15** not having collided (each task only appends its own Sheet, never reorders or drops another task's).
- **The `toCsv` formula-injection guard** (Task 11) actually being exercised by the invoice download in a real browser, not just its unit test — confirm a shipment/merchant name containing a leading `=` renders literally in the downloaded CSV when opened in a spreadsheet application, not as a formula.
- **Every new `STATUS_CONFIG` entry** (`PickupRequest.*` Task 10, `Kyc.*` Task 12) namespaced correctly with no collision against an existing flat key, and passing the `dataviz` skill's adjacent-pair contrast floor for its own small set (2-4 states each, unlikely to fail but not yet formally validated by any task above — run the validator once during this review pass).
