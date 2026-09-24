using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Audit.Application;
using Courier.Audit.Domain;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.International.Application;
using Courier.Merchants.Application;
using Courier.Persistence;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.SharedKernel;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

public class InternationalShipmentsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public InternationalShipmentsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Intl Org {suffix}", $"intl-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private static CreateBranchRequest ValidBranchRequest(string code) => new(
        code, $"Branch {code}", BranchType.Hub, KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null, null);

    private async Task<Guid> CreateBranchAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest(code));
        response.EnsureSuccessStatusCode();
        var branch = await response.Content.ReadFromJsonAsync<BranchResponse>();
        return branch!.Id;
    }

    private async Task<Guid> CreateMerchantAsync(HttpClient staffClient)
    {
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Shipper Pvt. Ltd.", "Shipper", "01-4444444", "shipper@example.com",
            $"shipper-owner-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        response.EnsureSuccessStatusCode();
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return merchant!.Id;
    }

    // Country rows are platform-level singletons shared by the whole test suite and never reset
    // between runs against the persistent courier-postgres-1 container (see
    // CountriesGeographyEndpointTests.cs) — this marks the ISO code serviced (an idempotent,
    // monotonic change) rather than asserting anything about its prior state.
    private async Task MarkCountryServicedAsync(string isoCode)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var country = await dbContext.Set<Country>().IgnoreQueryFilters().SingleAsync(c => c.IsoCode == isoCode);
        country.IsServiced = true;
        await dbContext.SaveChangesAsync();
    }

    private async Task<Guid> GetStandardServiceTypeIdAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/service-types");
        var serviceTypes = await response.Content.ReadFromJsonAsync<List<ServiceTypeResponse>>();
        return serviceTypes!.Single(s => s.Code == ServiceTypeCode.Standard).Id;
    }

    private static object OutboundBody(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, string recipientCountryCode) => new
    {
        merchantId,
        direction = "Outbound",
        originBranchId,
        destinationBranchId,
        serviceTypeId,
        weightKg = 1m,
        lengthCm = (decimal?)null,
        widthCm = (decimal?)null,
        heightCm = (decimal?)null,
        recipientName = "Foreign Recipient",
        recipientPhone = "9998887776",
        recipientProvinceId = (Guid?)null,
        recipientDistrictId = (Guid?)null,
        recipientLocalLevelId = (Guid?)null,
        recipientWardNumber = (int?)null,
        recipientAddressLine = "221B Baker Street",
        recipientCity = "London",
        recipientRegion = (string?)null,
        recipientPostalCode = "NW16XE",
        recipientCountryCode,
        senderName = "Local Sender",
        senderPhone = "9811111111",
        senderProvinceId = KathmanduProvinceId,
        senderDistrictId = KathmanduDistrictId,
        senderLocalLevelId = KathmanduLocalLevelId,
        senderWardNumber = KathmanduWardNumber,
        senderAddressLine = "Ward 1 Line",
        senderCity = (string?)null,
        senderRegion = (string?)null,
        senderPostalCode = (string?)null,
        senderCountryCode = (string?)null,
        provisionalTotalChargeAmount = 5000m,
        provisionalTotalChargeCurrency = "NPR",
        items = new[] { new { description = "Sample goods", quantity = 1, unitValue = 5000m } }
    };

    [Fact]
    public async Task CreateInternationalShipment_ServicedCountry_SameOriginAndDestination_ReachesCustomsPrepared()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "same-branch");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        await MarkCountryServicedAsync("GB");

        var branchId = await CreateBranchAsync(client, $"GW-{Guid.NewGuid():N}"[..10]);
        var merchantId = await CreateMerchantAsync(client);
        var serviceTypeId = await GetStandardServiceTypeIdAsync(client);

        var createResponse = await client.PostAsJsonAsync("/api/v1/international/shipments", OutboundBody(merchantId, branchId, branchId, serviceTypeId, "GB"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<InternationalShipmentDetailResponse>();
        Assert.Equal(ShipmentStatus.Created, created!.Shipment.CurrentStatus);
        Assert.False(created.Leg.CustomsCompleted);

        var itemId = created.Shipment.Items.Single().Id;

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var shipment = await dbContext.Set<Shipment>().IgnoreQueryFilters().SingleAsync(s => s.Id == created.Shipment.Id);
            shipment.CurrentStatus = ShipmentStatus.Sorted;
            await dbContext.SaveChangesAsync();
        }

        var customsResponse = await client.PatchAsJsonAsync($"/api/v1/international/shipments/{created.Shipment.Id}/customs", new
        {
            contentsDescription = "Sample goods for testing",
            countryOfOrigin = "NP",
            purpose = "Sample",
            incoterm = "Ddu",
            exportLicenceReference = (string?)null,
            items = new[] { new { shipmentItemId = itemId, hsCode = "HS1234", customsValueAmount = 5000m, customsValueCurrency = "NPR" } }
        });
        Assert.Equal(HttpStatusCode.OK, customsResponse.StatusCode);

        var prepareResponse = await client.PostAsync($"/api/v1/international/shipments/{created.Shipment.Id}/prepare-customs", null);
        Assert.Equal(HttpStatusCode.OK, prepareResponse.StatusCode);
        var prepared = await prepareResponse.Content.ReadFromJsonAsync<ShipmentResponseForTest>();
        Assert.Equal("CustomsPrepared", prepared!.CurrentStatus);

        var getResponse = await client.GetAsync($"/api/v1/international/shipments/{created.Shipment.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task CreateInternationalShipment_UnservicedCountry_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "unserviced");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        // Deliberately NOT calling MarkCountryServicedAsync — TV (Tuvalu) is a country no other
        // test in the suite ever marks serviced (unlike GB, IN, MV, QA, which other test files
        // already toggle serviced), so it reliably stays IsServiced = false across runs of the
        // shared, never-reset courier-postgres-1 database.
        var branchId = await CreateBranchAsync(client, $"UN-{Guid.NewGuid():N}"[..10]);
        var merchantId = await CreateMerchantAsync(client);
        var serviceTypeId = await GetStandardServiceTypeIdAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/international/shipments", OutboundBody(merchantId, branchId, branchId, serviceTypeId, "TV"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateInternationalShipment_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "no-auth");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        await MarkCountryServicedAsync("GB");

        // Proves the route is authentication-gated at all: a permission-stripped-but-authenticated
        // scenario (e.g. a CounterStaff user, which Task 12 does not grant international.create)
        // would need a reusable "assign role X to a fresh user" helper, which does not yet exist
        // in ManifestsEndpointsTests.cs (confirmed before writing this file) — building one is out
        // of scope for this task. That authorization rule is covered at the service layer by
        // Task 10's unit tests instead.
        var anonymousClient = _factory.CreateClient();
        var branchId = await CreateBranchAsync(client, $"P-{Guid.NewGuid():N}"[..10]);
        var merchantId = await CreateMerchantAsync(client);
        var serviceTypeId = await GetStandardServiceTypeIdAsync(client);

        var response = await anonymousClient.PostAsJsonAsync("/api/v1/international/shipments", OutboundBody(merchantId, branchId, branchId, serviceTypeId, "GB"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetInternationalShipment_CrossTenant_Returns404()
    {
        var clientA = _factory.CreateClient();
        var authA = await RegisterOrgAsync(clientA, "tenant-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authA.AccessToken);
        await MarkCountryServicedAsync("GB");
        var branchId = await CreateBranchAsync(clientA, $"TA-{Guid.NewGuid():N}"[..10]);
        var merchantId = await CreateMerchantAsync(clientA);
        var serviceTypeId = await GetStandardServiceTypeIdAsync(clientA);
        var createResponse = await clientA.PostAsJsonAsync("/api/v1/international/shipments", OutboundBody(merchantId, branchId, branchId, serviceTypeId, "GB"));
        var created = await createResponse.Content.ReadFromJsonAsync<InternationalShipmentDetailResponse>();

        var clientB = _factory.CreateClient();
        var authB = await RegisterOrgAsync(clientB, "tenant-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authB.AccessToken);

        var response = await clientB.GetAsync($"/api/v1/international/shipments/{created!.Shipment.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Final-review fix wave helpers --------------------------------------------------------

    private static object CustomsBody(Guid itemId, string currency = "NPR", string contents = "Sample goods for testing") => new
    {
        contentsDescription = contents,
        countryOfOrigin = "NP",
        purpose = "Sample",
        incoterm = "Ddu",
        exportLicenceReference = (string?)null,
        items = new[] { new { shipmentItemId = itemId, hsCode = "HS1234", customsValueAmount = 5000m, customsValueCurrency = currency } }
    };

    private sealed record OrgContext(HttpClient Client, AuthResult Auth, Guid BranchId, Guid MerchantId, Guid ServiceTypeId);

    private async Task<OrgContext> SetUpOrgAsync(string label)
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, label);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        await MarkCountryServicedAsync("GB");
        var branchId = await CreateBranchAsync(client, $"F-{Guid.NewGuid():N}"[..10]);
        var merchantId = await CreateMerchantAsync(client);
        var serviceTypeId = await GetStandardServiceTypeIdAsync(client);
        return new OrgContext(client, auth, branchId, merchantId, serviceTypeId);
    }

    private async Task<InternationalShipmentDetailResponse> CreateOutboundAsync(OrgContext org, Guid? originBranchId = null, Guid? destinationBranchId = null)
    {
        var response = await org.Client.PostAsJsonAsync("/api/v1/international/shipments",
            OutboundBody(org.MerchantId, originBranchId ?? org.BranchId, destinationBranchId ?? org.BranchId, org.ServiceTypeId, "GB"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InternationalShipmentDetailResponse>())!;
    }

    // Same shortcut the happy-path test above uses: the physical pickup/receive/sort/manifest scans
    // that would normally move a shipment to Sorted/AtDestinationBranch are other modules' routes
    // (covered by their own suites), so the shipment row is positioned directly.
    private async Task SetShipmentStateAsync(Guid shipmentId, ShipmentStatus status, Guid? currentBranchId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var shipment = await dbContext.Set<Shipment>().IgnoreQueryFilters().SingleAsync(s => s.Id == shipmentId);
        shipment.CurrentStatus = status;
        shipment.CurrentBranchId = currentBranchId;
        await dbContext.SaveChangesAsync();
    }

    private async Task<int> CountOrgShipmentsAsync(Guid organizationId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.Set<Shipment>().IgnoreQueryFilters().CountAsync(s => s.OrganizationId == organizationId);
    }

    // Same direct-seeding approach as BranchAuthorizationEndpointTests.SeedScopedUserTokenAsync
    // (no invite-user-with-grants endpoint to drive this through): one custom role holding exactly
    // permissionKeys, assigned with SpecificBranches scope to grantedBranchIds — or AllBranches
    // when grantedBranchIds is null.
    private async Task<HttpClient> SeedUserClientAsync(AuthResult owner, string[] permissionKeys, params Guid[]? grantedBranchIds)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var permissions = await dbContext.Set<Permission>().Where(p => permissionKeys.Contains(p.Key)).ToListAsync();
        Assert.Equal(permissionKeys.Length, permissions.Count);
        var role = new Role { Id = Guid.NewGuid(), OrganizationId = owner.OrganizationId, Name = $"IntlTestRole-{Guid.NewGuid():N}", IsSystemDefault = false };
        foreach (var permission in permissions)
        {
            role.Permissions.Add(permission);
        }
        dbContext.Set<Role>().Add(role);

        var userId = Guid.NewGuid();
        var email = $"intl-user-{userId:N}@example.com";
        dbContext.Set<User>().Add(new User { Id = userId, OrganizationId = owner.OrganizationId, Email = email, PasswordHash = "not-used-in-this-test", FullName = "Intl Test User" });
        dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(), UserId = userId, RoleId = role.Id, OrganizationId = owner.OrganizationId,
            BranchScope = grantedBranchIds is null ? BranchScope.AllBranches : BranchScope.SpecificBranches,
            AssignedAt = DateTimeOffset.UtcNow, AssignedByUserId = owner.UserId
        });
        foreach (var branchId in grantedBranchIds ?? Array.Empty<Guid>())
        {
            dbContext.Set<UserBranch>().Add(new UserBranch
            {
                Id = Guid.NewGuid(), UserId = userId, BranchId = branchId, OrganizationId = owner.OrganizationId,
                GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = owner.UserId
            });
        }
        await dbContext.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtTokenService.IssueAccessToken(userId, owner.OrganizationId, email));
        return client;
    }

    private static readonly string[] AllIntlPermissions = { "international.create", "international.read", "international.manage_customs" };

    // ---- C1: atomic create -------------------------------------------------------------------

    [Fact]
    public async Task CreateInternationalShipment_InvalidCurrency_Returns400AndPersistsNoShipment()
    {
        var org = await SetUpOrgAsync("bad-currency");
        var body = OutboundBody(org.MerchantId, org.BranchId, org.BranchId, org.ServiceTypeId, "GB");
        var json = System.Text.Json.JsonSerializer.SerializeToNode(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        json["provisionalTotalChargeCurrency"] = "RUPEES";

        var response = await org.Client.PostAsJsonAsync("/api/v1/international/shipments", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INTERNATIONAL_SHIPMENT_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountOrgShipmentsAsync(org.Auth.OrganizationId));
    }

    [Fact]
    public async Task CreateInternationalShipment_DomesticDirection_Returns400AndPersistsNoShipment()
    {
        var org = await SetUpOrgAsync("domestic-direction");
        var json = System.Text.Json.JsonSerializer.SerializeToNode(
            OutboundBody(org.MerchantId, org.BranchId, org.BranchId, org.ServiceTypeId, "GB"),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        json["direction"] = "Domestic";

        var response = await org.Client.PostAsJsonAsync("/api/v1/international/shipments", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountOrgShipmentsAsync(org.Auth.OrganizationId));
    }

    // Proves the whole create (gateway shell + InternationalLeg + audit) is one transaction: the
    // fake gateway durably writes a marker row through the shared DbContext (exactly as the real
    // shell's two SaveChangesAsync calls do), then returns a ShipmentId that doesn't exist, so the
    // InternationalLeg insert trips the InternationalLeg -> Shipment FK. The marker must be gone.
    [Fact]
    public async Task CreateAsync_FailureAfterShellWrite_RollsBackTheShellWrite()
    {
        var org = await SetUpOrgAsync("rollback");
        var marker = Guid.NewGuid().ToString();

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
            var service = new InternationalShipmentService(
                dbContext, new FixedTenantProvider(org.Auth.OrganizationId), auditLogger,
                new ShellWritingGateway(auditLogger, org.Auth.OrganizationId, marker),
                scope.ServiceProvider.GetRequiredService<ICountryLookupService>(),
                scope.ServiceProvider.GetRequiredService<IShipmentItemLookupService>(),
                scope.ServiceProvider.GetRequiredService<IShipmentTransitionService>(),
                scope.ServiceProvider.GetRequiredService<IBranchAuthorizationChecker>());
            var request = new CreateInternationalShipmentRequest(
                org.MerchantId, ShipmentDirection.Outbound, org.BranchId, org.BranchId, org.ServiceTypeId, 1m, null, null, null,
                "Foreign Recipient", "9998887776", null, null, null, null, "221B Baker Street", "London", null, "NW16XE", "GB",
                "Local Sender", "9811111111", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Ward 1 Line",
                null, null, null, null, 5000m, "NPR", new List<CreateShipmentItemRequest> { new("Sample goods", 1, 5000m) });
            var actingUser = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim("sub", org.Auth.UserId.ToString()) }, "test"));

            await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(request, actingUser));
        }

        using (var verifyScope = _factory.Services.CreateScope())
        {
            var dbContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await dbContext.Set<AuditLog>().IgnoreQueryFilters().AnyAsync(a => a.EntityId == marker));
        }
    }

    // The AWB collision-retry loop inside CreateShipmentShellAsync now runs inside the outer
    // transaction CreateAsync opens. A unique-violation inside an open Postgres transaction aborts
    // it unless rolled back to a savepoint — EF Core creates one automatically around each
    // SaveChangesAsync when a transaction is already active, so a same-org concurrent burst must
    // still all succeed with distinct AWBs.
    [Fact]
    public async Task CreateInternationalShipment_ConcurrentRequestsForSameOrg_AllSucceedWithDistinctAwbs()
    {
        const int concurrency = 10;
        var org = await SetUpOrgAsync("intl-awb-race");

        var responses = await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ =>
            org.Client.PostAsJsonAsync("/api/v1/international/shipments", OutboundBody(org.MerchantId, org.BranchId, org.BranchId, org.ServiceTypeId, "GB"))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var created = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<InternationalShipmentDetailResponse>()));
        Assert.Equal(concurrency, created.Select(c => c!.Shipment.Awb).Distinct().Count());
        Assert.Equal(concurrency, await CountOrgShipmentsAsync(org.Auth.OrganizationId));
    }

    // ---- C2: tenant validation of merchant/branches -------------------------------------------

    [Fact]
    public async Task CreateInternationalShipment_CrossTenantMerchant_Returns400AndPersistsNothing()
    {
        var orgA = await SetUpOrgAsync("xt-merchant-a");
        var orgB = await SetUpOrgAsync("xt-merchant-b");

        var response = await orgA.Client.PostAsJsonAsync("/api/v1/international/shipments",
            OutboundBody(orgB.MerchantId, orgA.BranchId, orgA.BranchId, orgA.ServiceTypeId, "GB"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SHIPMENT_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountOrgShipmentsAsync(orgA.Auth.OrganizationId));
    }

    [Fact]
    public async Task CreateInternationalShipment_CrossTenantBranch_Returns400AndPersistsNothing()
    {
        var orgA = await SetUpOrgAsync("xt-branch-a");
        var orgB = await SetUpOrgAsync("xt-branch-b");

        var originResponse = await orgA.Client.PostAsJsonAsync("/api/v1/international/shipments",
            OutboundBody(orgA.MerchantId, orgB.BranchId, orgA.BranchId, orgA.ServiceTypeId, "GB"));
        var destinationResponse = await orgA.Client.PostAsJsonAsync("/api/v1/international/shipments",
            OutboundBody(orgA.MerchantId, orgA.BranchId, orgB.BranchId, orgA.ServiceTypeId, "GB"));

        Assert.Equal(HttpStatusCode.BadRequest, originResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, destinationResponse.StatusCode);
        Assert.Equal(0, await CountOrgShipmentsAsync(orgA.Auth.OrganizationId));
    }

    // ---- I6: cross-tenant PATCH / prepare-customs -----------------------------------------------

    [Fact]
    public async Task PatchCustomsAndPrepareCustoms_CrossTenant_Return404()
    {
        var orgA = await SetUpOrgAsync("xt-customs-a");
        var created = await CreateOutboundAsync(orgA);
        var itemId = created.Shipment.Items.Single().Id;
        var orgB = await SetUpOrgAsync("xt-customs-b");

        var patch = await orgB.Client.PatchAsJsonAsync($"/api/v1/international/shipments/{created.Shipment.Id}/customs", CustomsBody(itemId));
        var prepare = await orgB.Client.PostAsync($"/api/v1/international/shipments/{created.Shipment.Id}/prepare-customs", null);

        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, prepare.StatusCode);
    }

    // ---- I6: 403 for a missing permission ---------------------------------------------------

    [Fact]
    public async Task AllRoutes_MissingPermission_Return403()
    {
        var org = await SetUpOrgAsync("perm-403");
        var created = await CreateOutboundAsync(org);
        var itemId = created.Shipment.Items.Single().Id;
        // Holds an unrelated permission only (all branches), so every international.* route must 403.
        var client = await SeedUserClientAsync(org.Auth, new[] { "shipment.read" }, null);

        var post = await client.PostAsJsonAsync("/api/v1/international/shipments", OutboundBody(org.MerchantId, org.BranchId, org.BranchId, org.ServiceTypeId, "GB"));
        var get = await client.GetAsync($"/api/v1/international/shipments/{created.Shipment.Id}");
        var patch = await client.PatchAsJsonAsync($"/api/v1/international/shipments/{created.Shipment.Id}/customs", CustomsBody(itemId));
        var prepare = await client.PostAsync($"/api/v1/international/shipments/{created.Shipment.Id}/prepare-customs", null);

        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, prepare.StatusCode);
    }

    // ---- I1: branch scope -------------------------------------------------------------------

    [Fact]
    public async Task CreateInternationalShipment_OriginOutsideUsersBranches_Returns400()
    {
        var org = await SetUpOrgAsync("scope-create");
        var grantedBranchId = await CreateBranchAsync(org.Client, $"G-{Guid.NewGuid():N}"[..10]);
        var client = await SeedUserClientAsync(org.Auth, AllIntlPermissions, grantedBranchId);

        var outside = await client.PostAsJsonAsync("/api/v1/international/shipments", OutboundBody(org.MerchantId, org.BranchId, org.BranchId, org.ServiceTypeId, "GB"));
        var inside = await client.PostAsJsonAsync("/api/v1/international/shipments", OutboundBody(org.MerchantId, grantedBranchId, grantedBranchId, org.ServiceTypeId, "GB"));

        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
        Assert.Contains("outside your branch access", await outside.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, inside.StatusCode);
    }

    [Fact]
    public async Task GetPatchPrepare_ShipmentNotTouchingUsersBranches_Return404()
    {
        var org = await SetUpOrgAsync("scope-invisible");
        var created = await CreateOutboundAsync(org);
        var itemId = created.Shipment.Items.Single().Id;
        await SetShipmentStateAsync(created.Shipment.Id, ShipmentStatus.Sorted, org.BranchId);
        var otherBranchId = await CreateBranchAsync(org.Client, $"O-{Guid.NewGuid():N}"[..10]);
        var client = await SeedUserClientAsync(org.Auth, AllIntlPermissions, otherBranchId);

        var get = await client.GetAsync($"/api/v1/international/shipments/{created.Shipment.Id}");
        var patch = await client.PatchAsJsonAsync($"/api/v1/international/shipments/{created.Shipment.Id}/customs", CustomsBody(itemId));
        var prepare = await client.PostAsync($"/api/v1/international/shipments/{created.Shipment.Id}/prepare-customs", null);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, prepare.StatusCode);

        // And the owner (all branches) can still see it — the 404s above are scope, not absence.
        Assert.Equal(HttpStatusCode.OK, (await org.Client.GetAsync($"/api/v1/international/shipments/{created.Shipment.Id}")).StatusCode);
    }

    [Fact]
    public async Task PrepareCustoms_UserSeesShipmentViaOriginButItIsAtAnotherBranch_Returns400()
    {
        var org = await SetUpOrgAsync("scope-prepare");
        var destinationBranchId = await CreateBranchAsync(org.Client, $"D-{Guid.NewGuid():N}"[..10]);
        var created = await CreateOutboundAsync(org, org.BranchId, destinationBranchId);
        var itemId = created.Shipment.Items.Single().Id;
        Assert.Equal(HttpStatusCode.OK, (await org.Client.PatchAsJsonAsync($"/api/v1/international/shipments/{created.Shipment.Id}/customs", CustomsBody(itemId))).StatusCode);
        await SetShipmentStateAsync(created.Shipment.Id, ShipmentStatus.AtDestinationBranch, destinationBranchId);
        // Origin-branch user: may read the shipment (it touches their branch) but it physically sits
        // at the destination branch, so they cannot perform the customs-prepared operation.
        var client = await SeedUserClientAsync(org.Auth, AllIntlPermissions, org.BranchId);

        var get = await client.GetAsync($"/api/v1/international/shipments/{created.Shipment.Id}");
        var prepare = await client.PostAsync($"/api/v1/international/shipments/{created.Shipment.Id}/prepare-customs", null);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, prepare.StatusCode);
        Assert.Contains("outside your branch access", await prepare.Content.ReadAsStringAsync());
    }

    // ---- I6: different-branch path over HTTP; I2 GET includes customs items; I5 frozen after ---

    [Fact]
    public async Task CreateInternationalShipment_DifferentOriginAndDestination_ReachesCustomsPreparedOnlyFromAtDestinationBranch()
    {
        var org = await SetUpOrgAsync("diff-branch");
        var destinationBranchId = await CreateBranchAsync(org.Client, $"DB-{Guid.NewGuid():N}"[..10]);
        var created = await CreateOutboundAsync(org, org.BranchId, destinationBranchId);
        var itemId = created.Shipment.Items.Single().Id;
        var id = created.Shipment.Id;

        Assert.Equal(HttpStatusCode.OK, (await org.Client.PatchAsJsonAsync($"/api/v1/international/shipments/{id}/customs", CustomsBody(itemId))).StatusCode);

        // Sorted at origin is NOT the right state when origin != destination.
        await SetShipmentStateAsync(id, ShipmentStatus.Sorted, org.BranchId);
        var fromSorted = await org.Client.PostAsync($"/api/v1/international/shipments/{id}/prepare-customs", null);
        Assert.Equal(HttpStatusCode.BadRequest, fromSorted.StatusCode);
        Assert.Contains("SHIPMENT_INVALID_TRANSITION", await fromSorted.Content.ReadAsStringAsync());

        await SetShipmentStateAsync(id, ShipmentStatus.AtDestinationBranch, destinationBranchId);
        var prepare = await org.Client.PostAsync($"/api/v1/international/shipments/{id}/prepare-customs", null);
        Assert.Equal(HttpStatusCode.OK, prepare.StatusCode);
        Assert.Equal("CustomsPrepared", (await prepare.Content.ReadFromJsonAsync<ShipmentResponseForTest>())!.CurrentStatus);

        var get = await org.Client.GetAsync($"/api/v1/international/shipments/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var detail = (await get.Content.ReadFromJsonAsync<InternationalShipmentDetailResponse>())!;
        Assert.True(detail.Leg.CustomsCompleted);
        var customsItem = Assert.Single(detail.Leg.CustomsItems);
        Assert.Equal(itemId, customsItem.ShipmentItemId);
        Assert.Equal("HS1234", customsItem.HsCode);

        // I5: the declaration is frozen once CustomsPrepared.
        var lateEdit = await org.Client.PatchAsJsonAsync($"/api/v1/international/shipments/{id}/customs", CustomsBody(itemId, contents: "Changed after prepare"));
        Assert.Equal(HttpStatusCode.BadRequest, lateEdit.StatusCode);
        Assert.Contains("INTERNATIONAL_SHIPMENT_VALIDATION_FAILED", await lateEdit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PatchCustoms_InvalidCurrency_Returns400()
    {
        var org = await SetUpOrgAsync("customs-currency");
        var created = await CreateOutboundAsync(org);

        var response = await org.Client.PatchAsJsonAsync($"/api/v1/international/shipments/{created.Shipment.Id}/customs",
            CustomsBody(created.Shipment.Items.Single().Id, currency: "RUPEES"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INTERNATIONAL_SHIPMENT_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    private sealed class FixedTenantProvider : ICurrentTenantProvider
    {
        public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
        public Guid? OrganizationId { get; }
    }

    private sealed class ShellWritingGateway : IShipmentInternationalGateway
    {
        private readonly IAuditLogger _auditLogger;
        private readonly Guid _organizationId;
        private readonly string _marker;

        public ShellWritingGateway(IAuditLogger auditLogger, Guid organizationId, string marker)
        {
            _auditLogger = auditLogger;
            _organizationId = organizationId;
            _marker = marker;
        }

        public async Task<ShipmentResponse> CreateShipmentShellAsync(CreateInternationalShipmentShellRequest request, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
        {
            await _auditLogger.LogAsync(new AuditEntry(_organizationId, null, "test.shell_written", "Test", _marker), cancellationToken);
            return new ShipmentResponse(
                Guid.NewGuid(), "AWB-NOPE", request.MerchantId, request.OriginBranchId, request.DestinationBranchId, request.OriginBranchId,
                null, null, ShipmentStatus.Created, request.ServiceTypeId, request.WeightKg, null, null, null, 0m, 0m, false, 0m, 0m, 0m, 0m,
                request.RecipientName, request.RecipientPhone, request.RecipientAddressLine, request.SenderName, request.SenderPhone, request.SenderAddressLine,
                new List<ShipmentItemResponse>(), DateTimeOffset.UtcNow);
        }

        public Task<ShipmentResponse?> TransitionToCustomsPreparedAsync(Guid shipmentId, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CarrierStatusApplyResult> ApplyCarrierStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, DateTimeOffset occurredAt, CarrierEventReference reference, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ShipmentResponse?> OverrideInternationalStatusAsync(Guid shipmentId, ShipmentStatus targetStatus, string reason, System.Security.Claims.ClaimsPrincipal actingUser, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    // Local shape for deserializing just Id/CurrentStatus from a full ShipmentResponse without
    // pulling in every field — mirrors the narrow-projection pattern other *EndpointsTests files
    // use when they only need to assert on a couple of fields from a larger response.
    private record ShipmentResponseForTest(Guid Id, string CurrentStatus);
}
