# Audit Log Viewer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give tenant users with the `audit.read` permission a searchable, filterable, date-sorted, paginated Audit Logs page for their own organization, and give SuperAdmins an equivalent unrestricted cross-tenant view in Platform.

**Architecture:** `AuditLog` and its write path (`IAuditLogger`, ~57 call sites) already exist and are untouched by this plan. This plan adds only the read side: two new `IAuditLogReader` query methods (tenant-scoped `SearchAsync`, cross-tenant `SearchAcrossOrganizationsAsync`), a tenant endpoint gated by the already-seeded `audit.read` permission, a Platform endpoint gated by `RequireSuperAdmin()`, and two frontend feature areas that mirror the existing `platform-invoices`/`platform-organizations` list-page pattern exactly (search → debounce → `DataTable` → row-click-opens-Sheet-via-Zustand-store).

**Tech Stack:** ASP.NET Core minimal APIs, EF Core (InMemory for unit tests, Postgres in integration/API tests via `WebApplicationFactory`), xUnit; Next.js, TypeScript, TanStack Query, Zustand, shadcn/ui, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-09-11-audit-log-viewer-design.md`

## Global Constraints

* Tenant visibility is gated by the single existing `audit.read` permission only — no per-domain permission matrix (a user either sees their whole org's audit trail or none of it).
* v1 has no branch-scoped filtering. `AuditLog` has no `BranchId` column; visibility is permission + organization only. This is a known, accepted limitation, not a bug to work around in this plan.
* Sort is always `Timestamp` descending (tie-broken by `Id` descending for stability). No sort-direction toggle in v1.
* Detail views are always a right-side `Sheet` driven by a Zustand store holding the already-fetched row data directly — never a separate route, and never a second network request to fetch what the list already returned.
* Use shadcn/ui components (`Input`, `DatePicker`, `Sheet`, `Badge`, the shared `DataTable`) — never raw HTML form/table primitives.
* Every list/search backend method must scope by tenant explicitly in the `Where` clause (via `IgnoreQueryFilters()` + an explicit id/id-set check), never rely on the ambient EF Core query filter alone — matches the existing `ListForOrganizationAsync` convention.

---

### Task 1: Extend `IAuditLogReader` with paginated search

**Files:**
- Modify: `src/Modules/Audit/Application/IAuditLogReader.cs`
- Modify: `src/Modules/Audit/Infrastructure/AuditLogger.cs`
- Modify: `src/Modules/Audit/Audit.csproj`
- Test: `tests/Audit.Tests/AuditLoggerSearchTests.cs`

**Interfaces:**
- Consumes: `AuditLog` domain entity (`src/Modules/Audit/Domain/AuditLog.cs`, unchanged), `PagedResult<T>`/`PaginationDefaults` from `Courier.SharedKernel`.
- Produces: `AuditLogRecord` (now carrying `OrganizationId`, `Before`, `After`, `IpAddress`, `UserAgent` in addition to its existing fields), `AuditLogSearchFilter`, `IAuditLogReader.SearchAsync(Guid? organizationId, AuditLogSearchFilter filter, int? page, int? pageSize, CancellationToken)`, `IAuditLogReader.SearchAcrossOrganizationsAsync(IReadOnlySet<Guid>? organizationIds, AuditLogSearchFilter filter, int? page, int? pageSize, CancellationToken)` — both consumed by Task 2 (tenant endpoint) and Task 3 (Platform service).

- [ ] **Step 1: Write the failing tests**

Create `tests/Audit.Tests/AuditLoggerSearchTests.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Audit.Domain;
using Courier.Audit.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Audit.Tests;

file class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
    }
}

file sealed class NullHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get => null; set { } }
}

public class AuditLoggerSearchTests
{
    private static (AuditLogger Logger, TestDbContext DbContext) CreateLogger()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new TestDbContext(options);
        return (new AuditLogger(dbContext, new NullHttpContextAccessor()), dbContext);
    }

    private static readonly AuditLogSearchFilter NoFilter = new(null, null, null, null, null, null, null);

    [Fact]
    public async Task SearchAsync_ScopesToTheGivenOrganizationOnly()
    {
        var (logger, _) = CreateLogger();
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        await logger.LogAsync(new AuditEntry(orgA, null, "shipment.created", "Shipment", "1"));
        await logger.LogAsync(new AuditEntry(orgB, null, "shipment.created", "Shipment", "2"));

        var result = await logger.SearchAsync(orgA, NoFilter, 1, 25);

        Assert.Equal(1, result.TotalCount);
        Assert.All(result.Items, r => Assert.Equal(orgA, r.OrganizationId));
    }

    [Fact]
    public async Task SearchAsync_SearchTerm_MatchesActionEntityTypeOrEntityIdCaseInsensitively()
    {
        var (logger, _) = CreateLogger();
        var orgId = Guid.NewGuid();
        await logger.LogAsync(new AuditEntry(orgId, null, "shipment.created", "Shipment", "AWB-1"));
        await logger.LogAsync(new AuditEntry(orgId, null, "cod.collected", "CodCollection", "COD-1"));

        var result = await logger.SearchAsync(orgId, NoFilter with { Search = "awb" }, 1, 25);

        Assert.Single(result.Items);
        Assert.Equal("shipment.created", result.Items[0].Action);
    }

    [Fact]
    public async Task SearchAsync_EntityTypeFilter_MatchesExactlyNotSubstring()
    {
        var (logger, _) = CreateLogger();
        var orgId = Guid.NewGuid();
        await logger.LogAsync(new AuditEntry(orgId, null, "shipment.created", "Shipment", "1"));
        await logger.LogAsync(new AuditEntry(orgId, null, "return_shipment.confirmed", "ReturnShipment", "2"));

        var result = await logger.SearchAsync(orgId, NoFilter with { EntityType = "Shipment" }, 1, 25);

        Assert.Single(result.Items);
        Assert.Equal("Shipment", result.Items[0].EntityType);
    }

    [Fact]
    public async Task SearchAsync_DateRangeFilter_ExcludesEventsOutsideRange()
    {
        var (logger, dbContext) = CreateLogger();
        var orgId = Guid.NewGuid();
        await logger.LogAsync(new AuditEntry(orgId, null, "shipment.created", "Shipment", "1"));
        var row = await dbContext.Set<AuditLog>().SingleAsync();
        row.Timestamp = DateTimeOffset.UtcNow.AddDays(-10);
        await dbContext.SaveChangesAsync();

        var result = await logger.SearchAsync(orgId, NoFilter with { DateFrom = DateTimeOffset.UtcNow.AddDays(-1) }, 1, 25);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SearchAsync_SortsNewestFirstAndPaginates()
    {
        var (logger, dbContext) = CreateLogger();
        var orgId = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            await logger.LogAsync(new AuditEntry(orgId, null, $"action.{i}", "Shipment", i.ToString()));
        }
        // Explicit, strictly increasing timestamps — LogAsync's own DateTimeOffset.UtcNow calls
        // can land in the same tick on a fast test machine, which would make "newest first"
        // ambiguous between action.0/1/2.
        var rows = await dbContext.Set<AuditLog>().OrderBy(a => a.Action).ToListAsync();
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10 + i);
        }
        await dbContext.SaveChangesAsync();

        var page1 = await logger.SearchAsync(orgId, NoFilter, 1, 2);
        var page2 = await logger.SearchAsync(orgId, NoFilter, 2, 2);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.Equal("action.2", page1.Items[0].Action);
        Assert.Equal("action.1", page1.Items[1].Action);
        Assert.Equal("action.0", page2.Items[0].Action);
    }

    [Fact]
    public async Task SearchAcrossOrganizationsAsync_NullOrganizationIds_ReturnsEveryOrganizationsEvents()
    {
        var (logger, _) = CreateLogger();
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        await logger.LogAsync(new AuditEntry(orgA, null, "shipment.created", "Shipment", "1"));
        await logger.LogAsync(new AuditEntry(orgB, null, "shipment.created", "Shipment", "2"));

        var result = await logger.SearchAcrossOrganizationsAsync(null, NoFilter, 1, 25);

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task SearchAcrossOrganizationsAsync_WithOrganizationIdSet_RestrictsToThoseOrganizationsOnly()
    {
        var (logger, _) = CreateLogger();
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        await logger.LogAsync(new AuditEntry(orgA, null, "shipment.created", "Shipment", "1"));
        await logger.LogAsync(new AuditEntry(orgB, null, "shipment.created", "Shipment", "2"));

        var result = await logger.SearchAcrossOrganizationsAsync(new HashSet<Guid> { orgA }, NoFilter, 1, 25);

        Assert.Single(result.Items);
        Assert.Equal(orgA, result.Items[0].OrganizationId);
    }

    [Fact]
    public async Task SearchAcrossOrganizationsAsync_WithEmptyOrganizationIdSet_ReturnsNothing()
    {
        var (logger, _) = CreateLogger();
        var orgId = Guid.NewGuid();
        await logger.LogAsync(new AuditEntry(orgId, null, "shipment.created", "Shipment", "1"));

        var result = await logger.SearchAcrossOrganizationsAsync(new HashSet<Guid>(), NoFilter, 1, 25);

        Assert.Empty(result.Items);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Audit.Tests/Audit.Tests.csproj --filter AuditLoggerSearchTests`
Expected: build error — `AuditLogger` has no `SearchAsync`/`SearchAcrossOrganizationsAsync` members, and `AuditLogSearchFilter` does not exist yet.

- [ ] **Step 3: Add the SharedKernel project reference**

Edit `src/Modules/Audit/Audit.csproj` — add a `ProjectReference` item group (this module currently has none):

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\SharedKernel\SharedKernel.csproj" />
  </ItemGroup>
```

- [ ] **Step 4: Extend `IAuditLogReader`**

Replace the contents of `src/Modules/Audit/Application/IAuditLogReader.cs`:

```csharp
using Courier.SharedKernel;

namespace Courier.Audit.Application;

public record AuditLogRecord(
    Guid Id, Guid? OrganizationId, string Action, string EntityType, string EntityId,
    Guid? UserId, Guid? ImpersonatedByUserId, bool IsCrossTenantAction, DateTimeOffset Timestamp,
    string? Before, string? After, string? IpAddress, string? UserAgent);

public record AuditLogSearchFilter(
    string? Search, string? Action, string? EntityType, string? EntityId,
    Guid? UserId, DateTimeOffset? DateFrom, DateTimeOffset? DateTo);

public interface IAuditLogReader
{
    // Cross-tenant read for Platform's PlatformDashboardService (SuperAdmin org detail
    // drill-down) — always called with ICurrentTenantProvider.OrganizationId == null. The
    // implementation must IgnoreQueryFilters(); the explicit organizationId parameter in the
    // Where clause is the real, correct scope, not the ambient tenant filter.
    Task<List<AuditLogRecord>> ListForOrganizationAsync(Guid organizationId, int limit, CancellationToken cancellationToken = default);

    // Tenant-facing paginated/filterable read (Audit/Presentation's GET /api/v1/audit-logs).
    // organizationId is passed explicitly by the caller (the ambient tenant's own id) rather than
    // relying on AppDbContext's HasQueryFilter — same belt-and-suspenders reasoning as
    // ListForOrganizationAsync above.
    Task<PagedResult<AuditLogRecord>> SearchAsync(Guid? organizationId, AuditLogSearchFilter filter, int? page, int? pageSize, CancellationToken cancellationToken = default);

    // Platform-facing cross-tenant read. organizationIds == null means no organization
    // restriction (every org); a non-null set (including an empty one) restricts to exactly
    // those organizations — used when Platform's caller narrowed by an org-name search.
    Task<PagedResult<AuditLogRecord>> SearchAcrossOrganizationsAsync(IReadOnlySet<Guid>? organizationIds, AuditLogSearchFilter filter, int? page, int? pageSize, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Implement the new methods on `AuditLogger`**

Replace the contents of `src/Modules/Audit/Infrastructure/AuditLogger.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Audit.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Courier.Audit.Infrastructure;

public class AuditLogger : IAuditLogger, IAuditLogReader
{
    private readonly DbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditLogger(DbContext dbContext, IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        // Null in every context with no ambient HTTP request (a Hangfire job, or a unit test
        // that never set up an HttpContext) — impersonation only ever happens over a real
        // request, so null is the correct, expected value there.
        var impersonatedByClaim = _httpContextAccessor.HttpContext?.User.FindFirst("impersonated_by")?.Value;
        var impersonatedByUserId = Guid.TryParse(impersonatedByClaim, out var parsed) ? parsed : (Guid?)null;

        _dbContext.Set<AuditLog>().Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = entry.OrganizationId,
            UserId = entry.UserId,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Before = entry.Before,
            After = entry.After,
            IpAddress = entry.IpAddress,
            UserAgent = entry.UserAgent,
            Timestamp = DateTimeOffset.UtcNow,
            IsCrossTenantAction = entry.IsCrossTenantAction,
            ImpersonatedByUserId = impersonatedByUserId
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // IgnoreQueryFilters() here is deliberate and safe for the same reason as every other
    // Platform-facing cross-org read in this plan — Platform's ambient tenant is always null, and
    // the explicit organizationId Where clause is the real, correct scope, not the bypassed
    // ambient filter.
    public async Task<List<AuditLogRecord>> ListForOrganizationAsync(Guid organizationId, int limit, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<AuditLog>().IgnoreQueryFilters()
            .Where(a => a.OrganizationId == organizationId)
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .Select(a => new AuditLogRecord(a.Id, a.OrganizationId, a.Action, a.EntityType, a.EntityId, a.UserId, a.ImpersonatedByUserId, a.IsCrossTenantAction, a.Timestamp, a.Before, a.After, a.IpAddress, a.UserAgent))
            .ToListAsync(cancellationToken);
    }

    public Task<PagedResult<AuditLogRecord>> SearchAsync(Guid? organizationId, AuditLogSearchFilter filter, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<AuditLog>().IgnoreQueryFilters().AsQueryable();
        if (organizationId is Guid orgId)
        {
            query = query.Where(a => a.OrganizationId == orgId);
        }
        return PageAsync(ApplyFilters(query, filter), page, pageSize, cancellationToken);
    }

    public Task<PagedResult<AuditLogRecord>> SearchAcrossOrganizationsAsync(IReadOnlySet<Guid>? organizationIds, AuditLogSearchFilter filter, int? page, int? pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<AuditLog>().IgnoreQueryFilters().AsQueryable();
        if (organizationIds is not null)
        {
            query = query.Where(a => a.OrganizationId != null && organizationIds.Contains(a.OrganizationId.Value));
        }
        return PageAsync(ApplyFilters(query, filter), page, pageSize, cancellationToken);
    }

    private static IQueryable<AuditLog> ApplyFilters(IQueryable<AuditLog> query, AuditLogSearchFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // Case-insensitive via ToLower() on both sides, matching
            // OrganizationAdminService.ListAsync's existing search — this module has no
            // reference to Npgsql.EntityFrameworkCore.PostgreSQL (only provider-agnostic EF
            // Core), so EF.Functions.ILike isn't available here either.
            var term = filter.Search.ToLower();
            query = query.Where(a => a.Action.ToLower().Contains(term) || a.EntityType.ToLower().Contains(term) || a.EntityId.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(a => a.Action == filter.Action);
        }
        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            query = query.Where(a => a.EntityType == filter.EntityType);
        }
        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            query = query.Where(a => a.EntityId == filter.EntityId);
        }
        if (filter.UserId is Guid userId)
        {
            query = query.Where(a => a.UserId == userId);
        }
        if (filter.DateFrom is DateTimeOffset dateFrom)
        {
            query = query.Where(a => a.Timestamp >= dateFrom);
        }
        if (filter.DateTo is DateTimeOffset dateTo)
        {
            query = query.Where(a => a.Timestamp <= dateTo);
        }
        return query;
    }

    private static async Task<PagedResult<AuditLogRecord>> PageAsync(IQueryable<AuditLog> query, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedPageSize) = PaginationDefaults.Normalize(page, pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            // Inlined rather than a shared helper method: EF Core cannot translate a call to an
            // external method inside Select() to SQL against the real Postgres provider (only
            // against the InMemory provider these unit tests run on), so the same constructor
            // call as ListForOrganizationAsync above is repeated here rather than factored out.
            .Select(a => new AuditLogRecord(a.Id, a.OrganizationId, a.Action, a.EntityType, a.EntityId, a.UserId, a.ImpersonatedByUserId, a.IsCrossTenantAction, a.Timestamp, a.Before, a.After, a.IpAddress, a.UserAgent))
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditLogRecord>(items, normalizedPage, normalizedPageSize, totalCount);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Audit.Tests/Audit.Tests.csproj`
Expected: PASS (all tests in the project, including the pre-existing `AuditLoggerTests`).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Audit/Application/IAuditLogReader.cs src/Modules/Audit/Infrastructure/AuditLogger.cs src/Modules/Audit/Audit.csproj tests/Audit.Tests/AuditLoggerSearchTests.cs
git commit -m "feat(audit): add paginated, filterable search to IAuditLogReader

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: Tenant `GET /api/v1/audit-logs` endpoint

**Files:**
- Create: `src/Modules/Audit/Presentation/AuditEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Api.Tests/AuditEndpointsTests.cs`

**Interfaces:**
- Consumes: `IAuditLogReader.SearchAsync` (Task 1), `RequirePermission`/`ICurrentTenantProvider` from `Courier.SharedKernel` (existing).
- Produces: `GET /api/v1/audit-logs` — consumed by Task 5's Next.js proxy route.

- [ ] **Step 1: Write the failing tests**

Create `tests/Api.Tests/AuditEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Audit.Application;
using Courier.Events.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Identity.Presentation;
using Courier.Persistence;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

public class AuditEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuditEndpointsTests(WebApplicationFactory<Program> factory)
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

    [Fact]
    public async Task GetAuditLogs_AsOwner_ReturnsEventsRecordedSinceRegistration()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "auditowner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        await CreateBranchAsync(client);

        var response = await client.GetAsync("/api/v1/audit-logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogRecord>>();
        Assert.Contains(result!.Items, r => r.Action == "organization.registered");
        Assert.Contains(result.Items, r => r.Action == "branch.created");
    }

    [Fact]
    public async Task GetAuditLogs_OrgBCannotSeeOrgAsEvents()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "auditisoa");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        await CreateBranchAsync(clientA);

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "auditisob");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var response = await clientB.GetAsync("/api/v1/audit-logs");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogRecord>>();

        Assert.All(result!.Items, r => Assert.Equal(orgB.OrganizationId, r.OrganizationId));
    }

    [Fact]
    public async Task GetAuditLogs_SearchFilter_MatchesActionOrEntityTypeCaseInsensitively()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "auditsearch");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        await CreateBranchAsync(client);

        var response = await client.GetAsync("/api/v1/audit-logs?search=BRANCH");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogRecord>>();

        Assert.NotEmpty(result!.Items);
        Assert.All(result.Items, r => Assert.True(
            r.Action.Contains("branch", StringComparison.OrdinalIgnoreCase) ||
            r.EntityType.Contains("branch", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GetAuditLogs_Paginates_ReturnsDisjointPages()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "auditpage");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        for (var i = 0; i < 3; i++)
        {
            await CreateBranchAsync(client);
        }

        var page1 = await (await client.GetAsync("/api/v1/audit-logs?page=1&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<AuditLogRecord>>();
        var page2 = await (await client.GetAsync("/api/v1/audit-logs?page=2&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<AuditLogRecord>>();

        Assert.True(page1!.TotalCount >= 4); // organization.registered + 3 branch.created
        var page1Ids = page1.Items.Select(r => r.Id).ToHashSet();
        var page2Ids = page2!.Items.Select(r => r.Id).ToHashSet();
        Assert.Empty(page1Ids.Intersect(page2Ids));
    }

    [Fact]
    public async Task GetAuditLogs_RoleWithoutAuditReadPermission_Returns403()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "auditdenied");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var branchId = await CreateBranchAsync(client);

        var role = await (await client.PostAsJsonAsync("/api/v1/roles",
            new CreateRoleRequest("NoAuditAccess", new[] { "shipment.read" }))).Content.ReadFromJsonAsync<RoleResponse>();
        var email = $"limited-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/v1/users/invite", new InviteUserRequest(email, "Limited Person", null, role!.Id, branchId, null));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outboxMessage = await dbContext.Set<OutboxMessage>().IgnoreQueryFilters()
            .Where(m => m.EventType == "user.invited")
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync(m => m.Payload.Contains(email));
        var inviteUrl = JsonDocument.Parse(outboxMessage.Payload).RootElement.GetProperty("InviteUrl").GetString()!;
        var token = Uri.UnescapeDataString(inviteUrl.Split("token=")[1]);
        await client.PostAsJsonAsync("/api/v1/auth/accept-invite", new AcceptInviteRequest(token, "L1m1tedP@ss!"));
        var loginResult = await (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "L1m1tedP@ss!"))).Content.ReadFromJsonAsync<AuthResult>();

        var limitedClient = _factory.CreateClient();
        limitedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult!.AccessToken);
        var response = await limitedClient.GetAsync("/api/v1/audit-logs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditLogs_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/audit-logs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter AuditEndpointsTests`
Expected: FAIL — every request returns 404 (route `/api/v1/audit-logs` does not exist yet).

- [ ] **Step 3: Create the endpoint**

Create `src/Modules/Audit/Presentation/AuditEndpoints.cs`:

```csharp
using Courier.Audit.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Audit.Presentation;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/audit-logs")
            .WithTags("Audit")
            .RequireAuthorization();

        group.MapGet("/", async (
            string? search, string? action, string? entityType, string? entityId, Guid? userId,
            DateTimeOffset? dateFrom, DateTimeOffset? dateTo, int? page, int? pageSize,
            IAuditLogReader reader, ICurrentTenantProvider tenant, HttpContext httpContext) =>
        {
            // Defense in depth: RequirePermission below already requires audit.read, which no
            // SuperAdmin-only caller (no Role, hence no permissions at all) can ever hold — but
            // if OrganizationId were ever null here regardless, passing it through unchanged
            // would turn SearchAsync into an unfiltered cross-tenant query. Fail closed instead.
            if (tenant.OrganizationId is null)
            {
                return Results.Forbid();
            }

            var filter = new AuditLogSearchFilter(search, action, entityType, entityId, userId, dateFrom, dateTo);
            var result = await reader.SearchAsync(tenant.OrganizationId, filter, page, pageSize, httpContext.RequestAborted);
            return Results.Ok(result);
        }).RequirePermission("audit.read");
    }
}
```

- [ ] **Step 4: Wire it up in `Program.cs`**

Edit `src/Api/Program.cs` — add the using near the other module Presentation usings (alphabetically, before `Courier.Billing.Application;`):

```diff
+using Courier.Audit.Presentation;
 using Courier.Billing.Application;
```

Add the map call near the other tenant endpoint maps (right after `app.MapReportsEndpoints();`):

```diff
 app.MapReportsEndpoints();
+app.MapAuditEndpoints();
 app.MapWebhooksEndpoints();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter AuditEndpointsTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Audit/Presentation/AuditEndpoints.cs src/Api/Program.cs tests/Api.Tests/AuditEndpointsTests.cs
git commit -m "feat(audit): add tenant GET /api/v1/audit-logs endpoint

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Platform `GET /api/v1/platform/audit-logs` endpoint

**Files:**
- Create: `src/Modules/Platform/Application/PlatformAuditService.cs`
- Create: `src/Modules/Platform/Presentation/PlatformAuditEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Test: `tests/Api.Tests/PlatformAuditEndpointsTests.cs`

**Interfaces:**
- Consumes: `IAuditLogReader.SearchAcrossOrganizationsAsync` (Task 1), `IOrganizationLookupService.ListAsync`/`GetAsync` (existing, `Courier.Identity.Application`), `RequireSuperAdmin` (existing, `Courier.SharedKernel`).
- Produces: `AuditLogRecordWithOrgName`, `PlatformAuditService.SearchAsync(...)`, `GET /api/v1/platform/audit-logs` — consumed by Task 9's Next.js proxy route.

- [ ] **Step 1: Write the failing tests**

Create `tests/Api.Tests/PlatformAuditEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Identity.Application;
using Courier.Platform.Application;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class PlatformAuditEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformAuditEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<string> LoginAsSuperAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
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
    public async Task GetPlatformAuditLogs_TenantJwtNotSuperAdmin_Returns403()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "pauditnsa");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/platform/audit-logs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPlatformAuditLogs_AsSuperAdmin_SeesEventsAcrossOrganizations()
    {
        var client = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(client, "pauditcrossa");
        var orgB = await RegisterOrgAsync(_factory.CreateClient(), "pauditcrossb");

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var response = await client.GetAsync("/api/v1/platform/audit-logs?search=organization.registered");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogRecordWithOrgName>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(result!.Items, r => r.OrganizationId == orgA.OrganizationId);
        Assert.Contains(result.Items, r => r.OrganizationId == orgB.OrganizationId);
    }

    [Fact]
    public async Task GetPlatformAuditLogs_FilteredByOrganizationId_ReturnsOnlyThatOrganizationsEvents()
    {
        var client = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(client, "pauditfiltera");
        await RegisterOrgAsync(_factory.CreateClient(), "pauditfilterb");

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var response = await client.GetAsync($"/api/v1/platform/audit-logs?organizationId={orgA.OrganizationId}");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogRecordWithOrgName>>();

        Assert.NotEmpty(result!.Items);
        Assert.All(result.Items, r => Assert.Equal(orgA.OrganizationId, r.OrganizationId));
    }

    [Fact]
    public async Task GetPlatformAuditLogs_FilteredByOrganizationSearch_ResolvesMatchingOrganizationNameToItsEventsOnly()
    {
        var client = _factory.CreateClient();
        var uniqueLabel = $"pauditnamesearch{Guid.NewGuid():N}"[..24];
        var matching = await RegisterOrgAsync(client, uniqueLabel);
        var nonMatching = await RegisterOrgAsync(_factory.CreateClient(), "pauditnamesearchother");

        var superAdminToken = await LoginAsSuperAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var response = await client.GetAsync($"/api/v1/platform/audit-logs?organizationSearch={uniqueLabel}");
        var result = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogRecordWithOrgName>>();

        Assert.NotEmpty(result!.Items);
        Assert.All(result.Items, r => Assert.Equal(matching.OrganizationId, r.OrganizationId));
        Assert.DoesNotContain(result.Items, r => r.OrganizationId == nonMatching.OrganizationId);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter PlatformAuditEndpointsTests`
Expected: FAIL — build error (`PlatformAuditService`/`AuditLogRecordWithOrgName` don't exist) and/or 404s.

- [ ] **Step 3: Create `PlatformAuditService`**

Create `src/Modules/Platform/Application/PlatformAuditService.cs`:

```csharp
using Courier.Audit.Application;
using Courier.Identity.Application;
using Courier.SharedKernel;

namespace Courier.Platform.Application;

public record AuditLogRecordWithOrgName(
    Guid Id, Guid? OrganizationId, string? OrganizationName, string Action, string EntityType, string EntityId,
    Guid? UserId, Guid? ImpersonatedByUserId, bool IsCrossTenantAction, DateTimeOffset Timestamp,
    string? Before, string? After, string? IpAddress, string? UserAgent);

public class PlatformAuditService
{
    private readonly IAuditLogReader _auditLogReader;
    private readonly IOrganizationLookupService _organizationLookupService;

    public PlatformAuditService(IAuditLogReader auditLogReader, IOrganizationLookupService organizationLookupService)
    {
        _auditLogReader = auditLogReader;
        _organizationLookupService = organizationLookupService;
    }

    public async Task<PagedResult<AuditLogRecordWithOrgName>> SearchAsync(
        Guid? organizationId, string? organizationSearch, string? search, string? action, string? entityType,
        string? entityId, Guid? userId, DateTimeOffset? dateFrom, DateTimeOffset? dateTo, int? page, int? pageSize,
        CancellationToken cancellationToken = default)
    {
        // organizationId (an exact match, for a future direct link e.g. from the org detail
        // Sheet) takes precedence over organizationSearch (a name substring) — an exact link
        // should never be silently widened by a stale search box value.
        IReadOnlySet<Guid>? organizationIds = null;
        if (organizationId is Guid exactOrgId)
        {
            organizationIds = new HashSet<Guid> { exactOrgId };
        }
        else if (!string.IsNullOrWhiteSpace(organizationSearch))
        {
            // Bounded the same way PlatformDashboardService.ListAllInvoicesAsync's own org-name
            // search already is: a name search realistically matches a handful of orgs, not
            // hundreds, so capping at MaxPageSize matches is a documented limitation, not a bug.
            var matches = await _organizationLookupService.ListAsync(organizationSearch, null, null, 1, PaginationDefaults.MaxPageSize, cancellationToken);
            organizationIds = matches.Items.Select(o => o.Id).ToHashSet();
        }

        var filter = new AuditLogSearchFilter(search, action, entityType, entityId, userId, dateFrom, dateTo);
        var result = await _auditLogReader.SearchAcrossOrganizationsAsync(organizationIds, filter, page, pageSize, cancellationToken);

        // Bounded per-page organization-name lookup, same "N+1 is fine once bounded by
        // pagination" reasoning as PlatformDashboardService.ListAllInvoicesAsync — capped at
        // pageSize (max 200) lookups, deduplicated within the page via nameById.
        var nameById = new Dictionary<Guid, string>();
        var withNames = new List<AuditLogRecordWithOrgName>();
        foreach (var record in result.Items)
        {
            string? organizationName = null;
            if (record.OrganizationId is Guid orgId)
            {
                if (!nameById.TryGetValue(orgId, out organizationName!))
                {
                    var org = await _organizationLookupService.GetAsync(orgId, cancellationToken);
                    organizationName = org?.Name ?? "Unknown";
                    nameById[orgId] = organizationName;
                }
            }

            withNames.Add(new AuditLogRecordWithOrgName(
                record.Id, record.OrganizationId, organizationName, record.Action, record.EntityType, record.EntityId,
                record.UserId, record.ImpersonatedByUserId, record.IsCrossTenantAction, record.Timestamp,
                record.Before, record.After, record.IpAddress, record.UserAgent));
        }

        return new PagedResult<AuditLogRecordWithOrgName>(withNames, result.Page, result.PageSize, result.TotalCount);
    }
}
```

- [ ] **Step 4: Create the endpoint**

Create `src/Modules/Platform/Presentation/PlatformAuditEndpoints.cs`:

```csharp
using Courier.Platform.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Courier.Platform.Presentation;

public static class PlatformAuditEndpoints
{
    public static void MapPlatformAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/v1/platform/audit-logs").WithTags("Platform Audit")
            .MapGet("/", async (
                Guid? organizationId, string? organizationSearch, string? search, string? action, string? entityType,
                string? entityId, Guid? userId, DateTimeOffset? dateFrom, DateTimeOffset? dateTo, int? page, int? pageSize,
                PlatformAuditService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.SearchAsync(organizationId, organizationSearch, search, action, entityType, entityId, userId, dateFrom, dateTo, page, pageSize, cancellationToken)))
            .RequireSuperAdmin();
    }
}
```

- [ ] **Step 5: Wire it up in `Program.cs`**

Register the service near the other Platform services in `src/Api/Program.cs`:

```diff
 builder.Services.AddScoped<PlatformDashboardService>();
+builder.Services.AddScoped<PlatformAuditService>();
```

Map the endpoint near the other Platform maps:

```diff
 app.MapPlatformImpersonationEndpoints();
+app.MapPlatformAuditEndpoints();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter PlatformAuditEndpointsTests`
Expected: PASS.

- [ ] **Step 7: Full backend regression check**

Run: `dotnet build Courier.slnx && dotnet test Courier.slnx`
Expected: build succeeds, all tests pass (confirms Task 1's `AuditLogRecord` constructor-shape change didn't break any other caller).

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Platform/Application/PlatformAuditService.cs src/Modules/Platform/Presentation/PlatformAuditEndpoints.cs src/Api/Program.cs tests/Api.Tests/PlatformAuditEndpointsTests.cs
git commit -m "feat(platform): add cross-tenant GET /api/v1/platform/audit-logs endpoint

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: Nav entry gated by `audit.read`

**Files:**
- Modify: `src/web/src/lib/navigation/navItems.ts`
- Modify: `src/web/src/components/nav-main.test.tsx`

**Interfaces:**
- Consumes: `usePermission` (existing, `src/web/src/features/auth/hooks/usePermission.ts`), `NavMain`'s existing per-item permission check (`src/web/src/components/nav-main.tsx`, unchanged).
- Produces: a `/audit-logs` nav item — the route it links to is created in Task 8.

- [ ] **Step 1: Write the failing test**

Add to `src/web/src/components/nav-main.test.tsx` (inside the existing `describe("NavMain", ...)` block, after the "Return Shipments" test):

```tsx
  it("shows the Audit Logs entry when the session has audit.read", async () => {
    renderNavMain(["report.read", "audit.read"]);

    await waitFor(() => expect(screen.getByText("Audit Logs")).toBeInTheDocument());
  });

  it("hides the Audit Logs entry when the session lacks audit.read", async () => {
    renderNavMain(["report.read"]);

    await waitFor(() => expect(screen.getByText("Dashboard")).toBeInTheDocument());
    expect(screen.queryByText("Audit Logs")).not.toBeInTheDocument();
  });
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- nav-main`
Expected: FAIL — "Audit Logs" text never renders (no such nav item exists yet).

- [ ] **Step 3: Add the nav item**

Edit `src/web/src/lib/navigation/navItems.ts`:

```diff
 import {
   LayoutDashboard,
   Package,
   Building2,
   Wallet,
   Truck,
   Bike,
   ClipboardCheck,
   AlertTriangle,
   Undo2,
   ShieldCheck,
   CreditCard,
+  History,
   type LucideIcon,
 } from "lucide-react";
```

```diff
   { href: "/cod", label: "COD", permission: "cod.read", icon: Wallet },
   { href: "/billing", label: "Billing", permission: "org.billing.manage", icon: CreditCard },
+  { href: "/audit-logs", label: "Audit Logs", permission: "audit.read", icon: History },
   { href: "/admin", label: "Admin", permission: ["role.manage", "user.read"], icon: ShieldCheck },
 ];
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- nav-main`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/lib/navigation/navItems.ts src/web/src/components/nav-main.test.tsx
git commit -m "feat(web): add Audit Logs nav item gated by audit.read

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: Tenant Next.js proxy route

**Files:**
- Create: `src/web/src/app/api/audit-logs/route.ts`
- Create: `src/web/src/app/api/audit-logs/route.test.ts`

**Interfaces:**
- Consumes: `apiFetch` (existing, `src/web/src/lib/auth/apiFetch.ts`).
- Produces: `GET /api/audit-logs` (Next.js route) — consumed by Task 6's `useAuditLogs` hook.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/app/api/audit-logs/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";
import { NextRequest } from "next/server";

describe("GET /api/audit-logs", () => {
  it("proxies the query string to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 }),
    } as Response);

    const request = new NextRequest("http://localhost/api/audit-logs?search=shipment");
    const response = await GET(request);

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/audit-logs?search=shipment");
    expect(response.status).toBe(200);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- api/audit-logs`
Expected: FAIL — `./route` does not exist yet.

- [ ] **Step 3: Create the route**

Create `src/web/src/app/api/audit-logs/route.ts`:

```ts
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/audit-logs${query}`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- api/audit-logs`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/app/api/audit-logs/route.ts src/web/src/app/api/audit-logs/route.test.ts
git commit -m "feat(web): add tenant audit-logs proxy route

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 6: Tenant `useAuditLogs` hook and types

**Files:**
- Create: `src/web/src/features/audit-logs/types.ts`
- Create: `src/web/src/features/audit-logs/hooks/useAuditLogs.ts`
- Create: `src/web/src/features/audit-logs/hooks/useAuditLogs.test.tsx`

**Interfaces:**
- Consumes: `clientFetch` (existing), `PagedResult<T>` type (existing, `@/components/data-table/DataTable`), the `GET /api/audit-logs` route (Task 5).
- Produces: `AuditLogRecord` type, `useAuditLogs(search, entityType, dateFrom, dateTo, page, pageSize)` — consumed by Task 7 (detail Sheet types it against) and Task 8 (page).

- [ ] **Step 1: Write the failing tests**

Create `src/web/src/features/audit-logs/hooks/useAuditLogs.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useAuditLogs } from "./useAuditLogs";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const pagedResult = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 };

describe("useAuditLogs", () => {
  it("builds the query string from page/pageSize with no filters set", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => useAuditLogs("", "", "", "", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/audit-logs?page=1&pageSize=25", undefined);
  });

  it("includes search and entityType in the query string when set", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => useAuditLogs("shipment.created", "Shipment", "", "", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/audit-logs?page=1&pageSize=25&search=shipment.created&entityType=Shipment", undefined);
  });

  it("expands date-only dateFrom/dateTo into UTC day-boundary timestamps", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => useAuditLogs("", "", "2026-09-01", "2026-09-05", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith(
      "/api/audit-logs?page=1&pageSize=25&dateFrom=2026-09-01T00%3A00%3A00Z&dateTo=2026-09-05T23%3A59%3A59Z",
      undefined
    );
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/web && npm test -- useAuditLogs`
Expected: FAIL — `./useAuditLogs` does not exist yet.

- [ ] **Step 3: Create the types and hook**

Create `src/web/src/features/audit-logs/types.ts`:

```ts
export interface AuditLogRecord {
  id: string;
  organizationId: string | null;
  action: string;
  entityType: string;
  entityId: string;
  userId: string | null;
  impersonatedByUserId: string | null;
  isCrossTenantAction: boolean;
  timestamp: string;
  before: string | null;
  after: string | null;
  ipAddress: string | null;
  userAgent: string | null;
}
```

Create `src/web/src/features/audit-logs/hooks/useAuditLogs.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { AuditLogRecord } from "@/features/audit-logs/types";

export function useAuditLogs(search: string, entityType: string, dateFrom: string, dateTo: string, page: number, pageSize: number) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (search) {
    params.set("search", search);
  }
  if (entityType) {
    params.set("entityType", entityType);
  }
  if (dateFrom) {
    // dateFrom/dateTo come from DatePicker as "yyyy-MM-dd" — expanded to UTC day boundaries so
    // the backend's inclusive DateTimeOffset range covers the whole selected day rather than
    // excluding most of the end date.
    params.set("dateFrom", `${dateFrom}T00:00:00Z`);
  }
  if (dateTo) {
    params.set("dateTo", `${dateTo}T23:59:59Z`);
  }

  return useQuery({
    queryKey: ["audit-logs", "list", search, entityType, dateFrom, dateTo, page, pageSize],
    queryFn: () => clientFetch<PagedResult<AuditLogRecord>>(`/api/audit-logs?${params.toString()}`),
  });
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src/web && npm test -- useAuditLogs`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/audit-logs/types.ts src/web/src/features/audit-logs/hooks/useAuditLogs.ts src/web/src/features/audit-logs/hooks/useAuditLogs.test.tsx
git commit -m "feat(web): add useAuditLogs hook and types

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 7: Tenant panel store and detail Sheet

**Files:**
- Create: `src/web/src/features/audit-logs/store/auditLogPanelStore.ts`
- Create: `src/web/src/features/audit-logs/AuditLogDetailSheet.tsx`
- Create: `src/web/src/features/audit-logs/AuditLogDetailSheet.test.tsx`

**Interfaces:**
- Consumes: `AuditLogRecord` type (Task 6).
- Produces: `useAuditLogPanelStore` (`openDetail(record)`/`close()`/`panel`), `<AuditLogDetailSheet />` — both consumed by Task 8's page.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/features/audit-logs/AuditLogDetailSheet.test.tsx`:

```tsx
import { describe, expect, it, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { AuditLogDetailSheet } from "./AuditLogDetailSheet";
import { useAuditLogPanelStore } from "./store/auditLogPanelStore";
import type { AuditLogRecord } from "./types";

const record: AuditLogRecord = {
  id: "log-1",
  organizationId: "org-1",
  action: "shipment.created",
  entityType: "Shipment",
  entityId: "ship-1",
  userId: "user-1",
  impersonatedByUserId: null,
  isCrossTenantAction: false,
  timestamp: "2026-09-01T10:00:00Z",
  before: null,
  after: "{\"status\":\"Created\"}",
  ipAddress: "203.0.113.1",
  userAgent: "Mozilla/5.0",
};

describe("AuditLogDetailSheet", () => {
  beforeEach(() => {
    useAuditLogPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("renders nothing open when the panel is closed", () => {
    render(<AuditLogDetailSheet />);

    expect(screen.queryByText("shipment.created")).not.toBeInTheDocument();
  });

  it("shows the full record when a row is opened", () => {
    useAuditLogPanelStore.getState().openDetail(record);
    render(<AuditLogDetailSheet />);

    expect(screen.getByText("shipment.created")).toBeInTheDocument();
    expect(screen.getByText("Shipment")).toBeInTheDocument();
    expect(screen.getByText("203.0.113.1")).toBeInTheDocument();
    expect(screen.getByText("{\"status\":\"Created\"}")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- AuditLogDetailSheet`
Expected: FAIL — `./AuditLogDetailSheet` and `./store/auditLogPanelStore` don't exist yet.

- [ ] **Step 3: Create the store**

Create `src/web/src/features/audit-logs/store/auditLogPanelStore.ts`:

```ts
import { create } from "zustand";
import type { AuditLogRecord } from "@/features/audit-logs/types";

type AuditLogPanelState = { mode: "closed" } | { mode: "detail"; record: AuditLogRecord };

interface AuditLogPanelStore {
  panel: AuditLogPanelState;
  openDetail: (record: AuditLogRecord) => void;
  close: () => void;
}

// Global so any row in the audit log table can open the detail Sheet without routing to a
// separate page (CLAUDE.md convention: detail views are always a right-side Sheet). The full
// record is already present in the list response the table already fetched — no extra request
// is made to show it.
export const useAuditLogPanelStore = create<AuditLogPanelStore>((set) => ({
  panel: { mode: "closed" },
  openDetail: (record) => set({ panel: { mode: "detail", record } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

- [ ] **Step 4: Create the detail Sheet**

Create `src/web/src/features/audit-logs/AuditLogDetailSheet.tsx`:

```tsx
"use client";

import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Badge } from "@/components/ui/badge";
import { useAuditLogPanelStore } from "@/features/audit-logs/store/auditLogPanelStore";

export function AuditLogDetailSheet() {
  const panel = useAuditLogPanelStore((s) => s.panel);
  const close = useAuditLogPanelStore((s) => s.close);
  const record = panel.mode === "detail" ? panel.record : null;

  return (
    <Sheet open={panel.mode === "detail"} onOpenChange={(open) => !open && close()}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{record?.action ?? "Audit event"}</SheetTitle>
        </SheetHeader>
        {record && (
          <div className="space-y-4 p-4 text-sm">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant="secondary">{record.entityType}</Badge>
              <span className="text-muted-foreground">{record.entityId}</span>
              {record.impersonatedByUserId && <Badge variant="outline">via impersonation</Badge>}
            </div>
            <dl className="space-y-2">
              <div>
                <dt className="text-muted-foreground">Timestamp</dt>
                <dd>{new Date(record.timestamp).toLocaleString()}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">Actor</dt>
                <dd>{record.userId ?? "System"}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">IP address</dt>
                <dd>{record.ipAddress ?? "—"}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">User agent</dt>
                <dd className="break-all">{record.userAgent ?? "—"}</dd>
              </div>
            </dl>
            {record.before && (
              <div>
                <dt className="mb-1 text-muted-foreground">Before</dt>
                <pre className="overflow-x-auto rounded-md bg-muted p-2 text-xs">{record.before}</pre>
              </div>
            )}
            {record.after && (
              <div>
                <dt className="mb-1 text-muted-foreground">After</dt>
                <pre className="overflow-x-auto rounded-md bg-muted p-2 text-xs">{record.after}</pre>
              </div>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/web && npm test -- AuditLogDetailSheet`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/web/src/features/audit-logs/store/auditLogPanelStore.ts src/web/src/features/audit-logs/AuditLogDetailSheet.tsx src/web/src/features/audit-logs/AuditLogDetailSheet.test.tsx
git commit -m "feat(web): add audit log detail Sheet and panel store

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 8: Tenant `/audit-logs` page

**Files:**
- Create: `src/web/src/app/(staff)/audit-logs/page.tsx`
- Create: `src/web/src/app/(staff)/audit-logs/page.test.tsx`

**Interfaces:**
- Consumes: `useAuditLogs` (Task 6), `useAuditLogPanelStore`/`AuditLogDetailSheet` (Task 7), `DataTable`/`useDebouncedValue`/`DatePicker` (existing).
- Produces: the `/audit-logs` page the Task 4 nav item links to.

- [ ] **Step 1: Write the failing tests**

Create `src/web/src/app/(staff)/audit-logs/page.test.tsx`:

```tsx
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

const records = [
  {
    id: "log-1",
    organizationId: "org-1",
    action: "shipment.created",
    entityType: "Shipment",
    entityId: "ship-1",
    userId: "user-1",
    impersonatedByUserId: null,
    isCrossTenantAction: false,
    timestamp: "2026-09-01T10:00:00Z",
    before: null,
    after: null,
    ipAddress: null,
    userAgent: null,
  },
  {
    id: "log-2",
    organizationId: "org-1",
    action: "branch.created",
    entityType: "Branch",
    entityId: "branch-1",
    userId: "user-1",
    impersonatedByUserId: null,
    isCrossTenantAction: false,
    timestamp: "2026-08-30T09:00:00Z",
    before: null,
    after: null,
    ipAddress: null,
    userAgent: null,
  },
];
const pagedResult = { items: records, page: 1, pageSize: 25, totalCount: 2, totalPages: 1 };

const useAuditLogs = vi.fn(() => ({ data: pagedResult, isLoading: false, isError: false, refetch: vi.fn() }));
vi.mock("@/features/audit-logs/hooks/useAuditLogs", () => ({
  useAuditLogs: (...args: unknown[]) => useAuditLogs(...(args as [])),
}));

import AuditLogsPage from "./page";

describe("AuditLogsPage", () => {
  beforeEach(() => {
    useAuditLogs.mockClear();
  });

  it("renders the audit log list with action and entity columns", () => {
    render(<AuditLogsPage />);

    expect(screen.getByText("shipment.created")).toBeInTheDocument();
    expect(screen.getByText("branch.created")).toBeInTheDocument();
  });

  it("passes the debounced search text to the hook after typing", async () => {
    render(<AuditLogsPage />);
    useAuditLogs.mockClear();

    fireEvent.change(screen.getByLabelText("Search audit logs"), { target: { value: "shipment" } });

    await waitFor(() => {
      expect(useAuditLogs).toHaveBeenCalledWith("shipment", "", "", "", 1, 25);
    }, { timeout: 1000 });
  });

  it("passes the entity type filter to the hook", async () => {
    render(<AuditLogsPage />);
    useAuditLogs.mockClear();

    fireEvent.change(screen.getByLabelText("Filter by entity type"), { target: { value: "Branch" } });

    await waitFor(() => {
      expect(useAuditLogs).toHaveBeenCalledWith("", "Branch", "", "", 1, 25);
    }, { timeout: 1000 });
  });

  it("passes the selected page to the hook via DataTable's onPageChange", async () => {
    useAuditLogs.mockReturnValue({
      data: { items: records, page: 1, pageSize: 1, totalCount: 2, totalPages: 2 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    });
    render(<AuditLogsPage />);
    useAuditLogs.mockClear();

    fireEvent.click(screen.getByText("2"));

    await waitFor(() => {
      expect(useAuditLogs).toHaveBeenCalledWith("", "", "", "", 2, 25);
    });
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/web && npm test -- "audit-logs/page"`
Expected: FAIL — `./page` does not exist yet.

- [ ] **Step 3: Create the page**

Create `src/web/src/app/(staff)/audit-logs/page.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Spinner } from "@/components/ui/spinner";
import { Input } from "@/components/ui/input";
import { DatePicker } from "@/components/ui/date-picker";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useAuditLogs } from "@/features/audit-logs/hooks/useAuditLogs";
import { useAuditLogPanelStore } from "@/features/audit-logs/store/auditLogPanelStore";
import { AuditLogDetailSheet } from "@/features/audit-logs/AuditLogDetailSheet";
import { useDebouncedValue } from "@/lib/hooks/useDebouncedValue";
import type { AuditLogRecord } from "@/features/audit-logs/types";

const PAGE_SIZE = 25;

export default function AuditLogsPage() {
  const [searchInput, setSearchInput] = useState("");
  const [entityTypeInput, setEntityTypeInput] = useState("");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [page, setPage] = useState(1);
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const debouncedEntityType = useDebouncedValue(entityTypeInput, 300);
  const { data, isLoading, isError, refetch } = useAuditLogs(debouncedSearch, debouncedEntityType, dateFrom, dateTo, page, PAGE_SIZE);
  const openDetail = useAuditLogPanelStore((s) => s.openDetail);

  const columns: ColumnDef<AuditLogRecord>[] = [
    {
      key: "timestamp",
      header: "Date",
      render: (r) => (
        <button type="button" onClick={() => openDetail(r)} className="font-medium text-foreground hover:underline">
          {new Date(r.timestamp).toLocaleString()}
        </button>
      ),
    },
    { key: "action", header: "Action", render: (r) => r.action },
    { key: "entity", header: "Entity", render: (r) => `${r.entityType} (${r.entityId})` },
    { key: "actor", header: "Actor", render: (r) => r.userId ?? "System" },
  ];

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load audit logs"
        description="Something went wrong fetching the audit log."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4 p-6">
      <h1 className="font-display text-xl font-semibold">Audit Logs</h1>
      <div className="flex flex-wrap items-center gap-2">
        <Input
          value={searchInput}
          onChange={(e) => {
            setSearchInput(e.target.value);
            setPage(1);
          }}
          placeholder="Search by action, entity type, or entity ID"
          className="max-w-sm"
          aria-label="Search audit logs"
        />
        <Input
          value={entityTypeInput}
          onChange={(e) => {
            setEntityTypeInput(e.target.value);
            setPage(1);
          }}
          placeholder="Entity type"
          className="max-w-40"
          aria-label="Filter by entity type"
        />
        <DatePicker
          value={dateFrom}
          onChange={(v) => {
            setDateFrom(v);
            setPage(1);
          }}
          placeholder="From date"
          clearable
          className="w-40"
          aria-label="Filter from date"
        />
        <DatePicker
          value={dateTo}
          onChange={(v) => {
            setDateTo(v);
            setPage(1);
          }}
          placeholder="To date"
          clearable
          className="w-40"
          aria-label="Filter to date"
        />
      </div>
      {isLoading ? (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      ) : (
        <DataTable
          columns={columns}
          data={data}
          isLoading={false}
          emptyTitle="No audit events found"
          emptyDescription="Try a different search term, entity type, or date range."
          onPageChange={setPage}
          rowKey={(r) => r.id}
        />
      )}
      <AuditLogDetailSheet />
    </div>
  );
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src/web && npm test -- "audit-logs/page"`
Expected: PASS.

- [ ] **Step 5: Manual verification against the real backend**

Run: `cd src/web && npm run dev`, log in as an `OrganizationOwner` (who has every permission, including `audit.read`), navigate to `/audit-logs` via the sidebar, confirm the list shows real events (e.g. `organization.registered`), type into the search box and confirm the list narrows, click a row and confirm the detail Sheet opens with timestamp/actor/IP/user agent, then paginate if there are enough rows.

- [ ] **Step 6: Full frontend regression check**

Run: `cd src/web && npx tsc --noEmit && npm run lint && npm test`
Expected: no type errors, no lint errors, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add "src/web/src/app/(staff)/audit-logs/page.tsx" "src/web/src/app/(staff)/audit-logs/page.test.tsx"
git commit -m "feat(web): add tenant Audit Logs page

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 9: Platform Next.js proxy route

**Files:**
- Create: `src/web/src/app/api/platform/audit-logs/route.ts`
- Create: `src/web/src/app/api/platform/audit-logs/route.test.ts`

**Interfaces:**
- Consumes: `platformApiFetch` (existing, `src/web/src/lib/auth/platformApiFetch.ts`).
- Produces: `GET /api/platform/audit-logs` (Next.js route) — consumed by Task 10's `usePlatformAuditLogs` hook.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/app/api/platform/audit-logs/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/platformApiFetch", () => ({
  platformApiFetch: vi.fn(),
}));

import { GET } from "./route";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";
import { NextRequest } from "next/server";

describe("GET /api/platform/audit-logs", () => {
  it("proxies the query string to the backend", async () => {
    vi.mocked(platformApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 }),
    } as Response);

    const request = new NextRequest("http://localhost/api/platform/audit-logs?organizationSearch=kathmandu");
    const response = await GET(request);

    expect(platformApiFetch).toHaveBeenCalledWith("/api/v1/platform/audit-logs?organizationSearch=kathmandu");
    expect(response.status).toBe(200);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- platform/audit-logs`
Expected: FAIL — `./route` does not exist yet.

- [ ] **Step 3: Create the route**

Create `src/web/src/app/api/platform/audit-logs/route.ts`:

```ts
import { NextRequest, NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await platformApiFetch(`/api/v1/platform/audit-logs${query}`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- platform/audit-logs`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/app/api/platform/audit-logs/route.ts src/web/src/app/api/platform/audit-logs/route.test.ts
git commit -m "feat(web): add platform audit-logs proxy route

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 10: Platform `usePlatformAuditLogs` hook, store, and detail Sheet

**Files:**
- Create: `src/web/src/features/platform-audit-logs/types.ts`
- Create: `src/web/src/features/platform-audit-logs/hooks/usePlatformAuditLogs.ts`
- Create: `src/web/src/features/platform-audit-logs/hooks/usePlatformAuditLogs.test.tsx`
- Create: `src/web/src/features/platform-audit-logs/store/platformAuditLogPanelStore.ts`
- Create: `src/web/src/features/platform-audit-logs/PlatformAuditLogDetailSheet.tsx`

**Interfaces:**
- Consumes: `clientFetch`, `PagedResult<T>` (existing), the `GET /api/platform/audit-logs` route (Task 9).
- Produces: `AuditLogRecord` (platform variant, with `organizationName`), `usePlatformAuditLogs(...)`, `usePlatformAuditLogPanelStore`, `<PlatformAuditLogDetailSheet />` — all consumed by Task 11's page.

- [ ] **Step 1: Write the failing test for the hook**

Create `src/web/src/features/platform-audit-logs/hooks/usePlatformAuditLogs.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { usePlatformAuditLogs } from "./usePlatformAuditLogs";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

const pagedResult = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 };

describe("usePlatformAuditLogs", () => {
  it("builds the query string from page/pageSize with no filters set", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => usePlatformAuditLogs("", "", "", "", "", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/audit-logs?page=1&pageSize=25", undefined);
  });

  it("includes organizationSearch and search in the query string when set", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => pagedResult });

    const { result } = renderHook(() => usePlatformAuditLogs("kathmandu", "shipment.created", "", "", "", 1, 25), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith("/api/platform/audit-logs?page=1&pageSize=25&organizationSearch=kathmandu&search=shipment.created", undefined);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- usePlatformAuditLogs`
Expected: FAIL — `./usePlatformAuditLogs` does not exist yet.

- [ ] **Step 3: Create the types and hook**

Create `src/web/src/features/platform-audit-logs/types.ts`:

```ts
export interface AuditLogRecord {
  id: string;
  organizationId: string | null;
  organizationName: string | null;
  action: string;
  entityType: string;
  entityId: string;
  userId: string | null;
  impersonatedByUserId: string | null;
  isCrossTenantAction: boolean;
  timestamp: string;
  before: string | null;
  after: string | null;
  ipAddress: string | null;
  userAgent: string | null;
}
```

Create `src/web/src/features/platform-audit-logs/hooks/usePlatformAuditLogs.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { AuditLogRecord } from "@/features/platform-audit-logs/types";

export function usePlatformAuditLogs(
  organizationSearch: string, search: string, entityType: string, dateFrom: string, dateTo: string, page: number, pageSize: number
) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (organizationSearch) {
    params.set("organizationSearch", organizationSearch);
  }
  if (search) {
    params.set("search", search);
  }
  if (entityType) {
    params.set("entityType", entityType);
  }
  if (dateFrom) {
    params.set("dateFrom", `${dateFrom}T00:00:00Z`);
  }
  if (dateTo) {
    params.set("dateTo", `${dateTo}T23:59:59Z`);
  }

  return useQuery({
    queryKey: ["platform-audit-logs", "list", organizationSearch, search, entityType, dateFrom, dateTo, page, pageSize],
    queryFn: () => clientFetch<PagedResult<AuditLogRecord>>(`/api/platform/audit-logs?${params.toString()}`),
  });
}
```

- [ ] **Step 4: Run the hook test to verify it passes**

Run: `cd src/web && npm test -- usePlatformAuditLogs`
Expected: PASS.

- [ ] **Step 5: Write the failing test for the detail Sheet**

Create `src/web/src/features/platform-audit-logs/PlatformAuditLogDetailSheet.test.tsx`:

```tsx
import { describe, expect, it, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { PlatformAuditLogDetailSheet } from "./PlatformAuditLogDetailSheet";
import { usePlatformAuditLogPanelStore } from "./store/platformAuditLogPanelStore";
import type { AuditLogRecord } from "./types";

const record: AuditLogRecord = {
  id: "log-1",
  organizationId: "org-1",
  organizationName: "Kathmandu Couriers",
  action: "organization.suspended",
  entityType: "Organization",
  entityId: "org-1",
  userId: null,
  impersonatedByUserId: null,
  isCrossTenantAction: true,
  timestamp: "2026-09-01T10:00:00Z",
  before: null,
  after: null,
  ipAddress: "203.0.113.1",
  userAgent: null,
};

describe("PlatformAuditLogDetailSheet", () => {
  beforeEach(() => {
    usePlatformAuditLogPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("shows the organization name alongside the record when opened", () => {
    usePlatformAuditLogPanelStore.getState().openDetail(record);
    render(<PlatformAuditLogDetailSheet />);

    expect(screen.getByText("organization.suspended")).toBeInTheDocument();
    expect(screen.getByText("Kathmandu Couriers")).toBeInTheDocument();
  });
});
```

- [ ] **Step 6: Run the test to verify it fails**

Run: `cd src/web && npm test -- PlatformAuditLogDetailSheet`
Expected: FAIL — `./PlatformAuditLogDetailSheet` and `./store/platformAuditLogPanelStore` don't exist yet.

- [ ] **Step 7: Create the store and detail Sheet**

Create `src/web/src/features/platform-audit-logs/store/platformAuditLogPanelStore.ts`:

```ts
import { create } from "zustand";
import type { AuditLogRecord } from "@/features/platform-audit-logs/types";

type PlatformAuditLogPanelState = { mode: "closed" } | { mode: "detail"; record: AuditLogRecord };

interface PlatformAuditLogPanelStore {
  panel: PlatformAuditLogPanelState;
  openDetail: (record: AuditLogRecord) => void;
  close: () => void;
}

export const usePlatformAuditLogPanelStore = create<PlatformAuditLogPanelStore>((set) => ({
  panel: { mode: "closed" },
  openDetail: (record) => set({ panel: { mode: "detail", record } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

Create `src/web/src/features/platform-audit-logs/PlatformAuditLogDetailSheet.tsx`:

```tsx
"use client";

import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Badge } from "@/components/ui/badge";
import { usePlatformAuditLogPanelStore } from "@/features/platform-audit-logs/store/platformAuditLogPanelStore";

export function PlatformAuditLogDetailSheet() {
  const panel = usePlatformAuditLogPanelStore((s) => s.panel);
  const close = usePlatformAuditLogPanelStore((s) => s.close);
  const record = panel.mode === "detail" ? panel.record : null;

  return (
    <Sheet open={panel.mode === "detail"} onOpenChange={(open) => !open && close()}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{record?.action ?? "Audit event"}</SheetTitle>
        </SheetHeader>
        {record && (
          <div className="space-y-4 p-4 text-sm">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant="secondary">{record.entityType}</Badge>
              <span className="text-muted-foreground">{record.entityId}</span>
              {record.isCrossTenantAction && <Badge variant="outline">Platform action</Badge>}
            </div>
            <dl className="space-y-2">
              <div>
                <dt className="text-muted-foreground">Organization</dt>
                <dd>{record.organizationName ?? "—"}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">Timestamp</dt>
                <dd>{new Date(record.timestamp).toLocaleString()}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">Actor</dt>
                <dd>{record.userId ?? "System"}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">IP address</dt>
                <dd>{record.ipAddress ?? "—"}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">User agent</dt>
                <dd className="break-all">{record.userAgent ?? "—"}</dd>
              </div>
            </dl>
            {record.before && (
              <div>
                <dt className="mb-1 text-muted-foreground">Before</dt>
                <pre className="overflow-x-auto rounded-md bg-muted p-2 text-xs">{record.before}</pre>
              </div>
            )}
            {record.after && (
              <div>
                <dt className="mb-1 text-muted-foreground">After</dt>
                <pre className="overflow-x-auto rounded-md bg-muted p-2 text-xs">{record.after}</pre>
              </div>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 8: Run the test to verify it passes**

Run: `cd src/web && npm test -- PlatformAuditLogDetailSheet`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/web/src/features/platform-audit-logs
git commit -m "feat(web): add platform audit log hook, store, and detail Sheet

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 11: Platform `/audit-logs` page and nav link

**Files:**
- Create: `src/web/src/app/platform/audit-logs/page.tsx`
- Create: `src/web/src/app/platform/audit-logs/page.test.tsx`
- Modify: `src/web/src/features/platform/PlatformNav.tsx`
- Modify: `src/web/src/features/platform/PlatformNav.test.tsx`

**Interfaces:**
- Consumes: `usePlatformAuditLogs`/`usePlatformAuditLogPanelStore`/`PlatformAuditLogDetailSheet` (Task 10), `DataTable`/`useDebouncedValue`/`DatePicker` (existing).
- Produces: the `/platform/audit-logs` page, and the "Audit Logs" link in `PlatformNav`.

- [ ] **Step 1: Write the failing tests**

Update `src/web/src/features/platform/PlatformNav.test.tsx` — replace the "renders all four platform nav links" test:

```diff
-  it("renders all four platform nav links", () => {
+  it("renders all five platform nav links", () => {
     render(<PlatformNav />);

     expect(screen.getByText("Dashboard")).toBeInTheDocument();
     expect(screen.getByText("Organizations")).toBeInTheDocument();
     expect(screen.getByText("Plans")).toBeInTheDocument();
     expect(screen.getByText("Invoices")).toBeInTheDocument();
+    expect(screen.getByText("Audit Logs")).toBeInTheDocument();
   });
```

Create `src/web/src/app/platform/audit-logs/page.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

const records = [
  {
    id: "log-1",
    organizationId: "org-1",
    organizationName: "Kathmandu Couriers",
    action: "shipment.created",
    entityType: "Shipment",
    entityId: "ship-1",
    userId: "user-1",
    impersonatedByUserId: null,
    isCrossTenantAction: false,
    timestamp: "2026-09-01T10:00:00Z",
    before: null,
    after: null,
    ipAddress: null,
    userAgent: null,
  },
];
const pagedResult = { items: records, page: 1, pageSize: 25, totalCount: 1, totalPages: 1 };

const usePlatformAuditLogs = vi.fn(() => ({ data: pagedResult, isLoading: false, isError: false, refetch: vi.fn() }));
vi.mock("@/features/platform-audit-logs/hooks/usePlatformAuditLogs", () => ({
  usePlatformAuditLogs: (...args: unknown[]) => usePlatformAuditLogs(...(args as [])),
}));

import PlatformAuditLogsPage from "./page";

describe("PlatformAuditLogsPage", () => {
  it("renders the audit log list with the organization name column", () => {
    render(<PlatformAuditLogsPage />);

    expect(screen.getByText("shipment.created")).toBeInTheDocument();
    expect(screen.getByText("Kathmandu Couriers")).toBeInTheDocument();
  });

  it("passes the debounced organization search to the hook after typing", async () => {
    render(<PlatformAuditLogsPage />);
    usePlatformAuditLogs.mockClear();

    fireEvent.change(screen.getByLabelText("Search by organization name"), { target: { value: "kathmandu" } });

    await waitFor(() => {
      expect(usePlatformAuditLogs).toHaveBeenCalledWith("kathmandu", "", "", "", "", 1, 25);
    }, { timeout: 1000 });
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/web && npm test -- "platform/audit-logs" PlatformNav`
Expected: FAIL — `./page` doesn't exist, and `PlatformNav.test.tsx`'s updated assertion fails since the link doesn't exist yet.

- [ ] **Step 3: Add the nav link**

Edit `src/web/src/features/platform/PlatformNav.tsx`:

```diff
 const links = [
   { href: "/platform/dashboard", label: "Dashboard" },
   { href: "/platform/organizations", label: "Organizations" },
   { href: "/platform/plans", label: "Plans" },
   { href: "/platform/invoices", label: "Invoices" },
+  { href: "/platform/audit-logs", label: "Audit Logs" },
 ];
```

- [ ] **Step 4: Create the page**

Create `src/web/src/app/platform/audit-logs/page.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Spinner } from "@/components/ui/spinner";
import { Input } from "@/components/ui/input";
import { DatePicker } from "@/components/ui/date-picker";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePlatformAuditLogs } from "@/features/platform-audit-logs/hooks/usePlatformAuditLogs";
import { usePlatformAuditLogPanelStore } from "@/features/platform-audit-logs/store/platformAuditLogPanelStore";
import { PlatformAuditLogDetailSheet } from "@/features/platform-audit-logs/PlatformAuditLogDetailSheet";
import { useDebouncedValue } from "@/lib/hooks/useDebouncedValue";
import type { AuditLogRecord } from "@/features/platform-audit-logs/types";

const PAGE_SIZE = 25;

export default function PlatformAuditLogsPage() {
  const [organizationSearchInput, setOrganizationSearchInput] = useState("");
  const [searchInput, setSearchInput] = useState("");
  const [entityTypeInput, setEntityTypeInput] = useState("");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [page, setPage] = useState(1);
  const debouncedOrganizationSearch = useDebouncedValue(organizationSearchInput, 300);
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const debouncedEntityType = useDebouncedValue(entityTypeInput, 300);
  const { data, isLoading, isError, refetch } = usePlatformAuditLogs(
    debouncedOrganizationSearch, debouncedSearch, debouncedEntityType, dateFrom, dateTo, page, PAGE_SIZE
  );
  const openDetail = usePlatformAuditLogPanelStore((s) => s.openDetail);

  const columns: ColumnDef<AuditLogRecord>[] = [
    {
      key: "timestamp",
      header: "Date",
      render: (r) => (
        <button type="button" onClick={() => openDetail(r)} className="font-medium text-foreground hover:underline">
          {new Date(r.timestamp).toLocaleString()}
        </button>
      ),
    },
    { key: "organization", header: "Organization", render: (r) => r.organizationName ?? "—" },
    { key: "action", header: "Action", render: (r) => r.action },
    { key: "entity", header: "Entity", render: (r) => `${r.entityType} (${r.entityId})` },
    { key: "actor", header: "Actor", render: (r) => r.userId ?? "System" },
  ];

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load audit logs"
        description="Something went wrong fetching the audit log."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4 p-6">
      <h1 className="font-display text-xl font-semibold">Audit Logs</h1>
      <div className="flex flex-wrap items-center gap-2">
        <Input
          value={organizationSearchInput}
          onChange={(e) => {
            setOrganizationSearchInput(e.target.value);
            setPage(1);
          }}
          placeholder="Search by organization name"
          className="max-w-sm"
          aria-label="Search by organization name"
        />
        <Input
          value={searchInput}
          onChange={(e) => {
            setSearchInput(e.target.value);
            setPage(1);
          }}
          placeholder="Search by action, entity type, or entity ID"
          className="max-w-sm"
          aria-label="Search audit logs"
        />
        <Input
          value={entityTypeInput}
          onChange={(e) => {
            setEntityTypeInput(e.target.value);
            setPage(1);
          }}
          placeholder="Entity type"
          className="max-w-40"
          aria-label="Filter by entity type"
        />
        <DatePicker
          value={dateFrom}
          onChange={(v) => {
            setDateFrom(v);
            setPage(1);
          }}
          placeholder="From date"
          clearable
          className="w-40"
          aria-label="Filter from date"
        />
        <DatePicker
          value={dateTo}
          onChange={(v) => {
            setDateTo(v);
            setPage(1);
          }}
          placeholder="To date"
          clearable
          className="w-40"
          aria-label="Filter to date"
        />
      </div>
      {isLoading ? (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      ) : (
        <DataTable
          columns={columns}
          data={data}
          isLoading={false}
          emptyTitle="No audit events found"
          emptyDescription="Try a different search term, entity type, or date range."
          onPageChange={setPage}
          rowKey={(r) => r.id}
        />
      )}
      <PlatformAuditLogDetailSheet />
    </div>
  );
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd src/web && npm test -- "platform/audit-logs" PlatformNav`
Expected: PASS.

- [ ] **Step 6: Manual verification against the real backend**

Run: `cd src/web && npm run dev`, log in as SuperAdmin, navigate to `/platform/audit-logs`, confirm events from multiple organizations appear with their organization names, type an organization name into the search box and confirm it narrows to that org's events only, and confirm the nav highlights "Audit Logs" as active.

- [ ] **Step 7: Full frontend regression check**

Run: `cd src/web && npx tsc --noEmit && npm run lint && npm test`
Expected: no type errors, no lint errors, all tests pass.

- [ ] **Step 8: Commit**

```bash
git add "src/web/src/app/platform/audit-logs" src/web/src/features/platform/PlatformNav.tsx src/web/src/features/platform/PlatformNav.test.tsx
git commit -m "feat(platform): add cross-tenant Audit Logs page and nav link

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
