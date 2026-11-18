using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Merchants.Application;
using Courier.Persistence;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

// Covers Task 10: POST /api/v1/pickups (merchant-track only), GET /api/v1/pickups (dual-purpose:
// merchant sees own via PickupsRead, staff sees branch-scoped via pickup.read), and
// POST /api/v1/pickups/{id}/cancel (merchant-track only). Seeding helpers mirror
// ShipmentsEndpointsTests.cs; the API-key fixture mirrors ApiKeyAuthenticationTests.cs; the
// branch-restricted staff seeding mirrors BranchAuthorizationEndpointTests.cs's
// SeedScopedUserTokenAsync (no grant/user-management endpoint exists yet, so the scoped user's
// role/UserRole/UserBranch rows are persisted straight against AppDbContext).
public class PickupsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public PickupsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Pickups Org {suffix}", $"pickups-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private static CreateBranchRequest ValidBranchRequest(string code, Guid? deliveryZoneId = null) => new(
        code, $"Branch {code}", BranchType.Hub, KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null, deliveryZoneId);

    private static List<Guid> KathmanduWard1Areas() => new()
    {
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
    };

    private async Task<(Guid OriginBranchId, Guid DestinationBranchId, Guid ServiceTypeId)> SeedRoutableBranchesAsync(HttpClient client)
    {
        var originZoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Origin-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var originZone = await originZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        var destZoneResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Dest-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var destZone = await destZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();

        var originBranchResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"OZ-{Guid.NewGuid():N}"[..10], originZone!.Id));
        var originBranch = await originBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();
        var destBranchResponse = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"DZ-{Guid.NewGuid():N}"[..10], destZone!.Id));
        var destBranch = await destBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var serviceTypesResponse = await client.GetAsync("/api/v1/service-types");
        var serviceTypes = await serviceTypesResponse.Content.ReadFromJsonAsync<List<ServiceTypeResponse>>();
        var standard = serviceTypes!.Single(s => s.Code == ServiceTypeCode.Standard);

        return (originBranch!.Id, destBranch!.Id, standard.Id);
    }

    private async Task SeedRateCardAsync(HttpClient client, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, decimal baseRate)
    {
        var originBranch = (await (await client.GetAsync($"/api/v1/branches/{originBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var destBranch = (await (await client.GetAsync($"/api/v1/branches/{destinationBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var weightSlabs = await (await client.GetAsync("/api/v1/weight-slabs")).Content.ReadFromJsonAsync<List<WeightSlabResponse>>();
        var slab = weightSlabs!.Single(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m);

        var rateCardResponse = await client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest($"Card-{Guid.NewGuid():N}", DateTimeOffset.UtcNow));
        rateCardResponse.EnsureSuccessStatusCode();
        var rateCard = await rateCardResponse.Content.ReadFromJsonAsync<RateCardResponse>();
        var zoneRateResponse = await client.PostAsJsonAsync($"/api/v1/rate-cards/{rateCard!.Id}/zone-rates",
            new CreateZoneRateRequest(originBranch.DeliveryZoneId!.Value, destBranch.DeliveryZoneId!.Value, serviceTypeId, slab.Id, baseRate));
        zoneRateResponse.EnsureSuccessStatusCode();
    }

    private static CreateShipmentRequest ValidShipmentRequest(Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        null, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 2, 500m) });

    // Merchant-track POSTs to /api/v1/shipments and /api/v1/pickups are gated by
    // IdempotencyMiddleware (Task 7) — every such POST in this file must carry an
    // Idempotency-Key header or the middleware short-circuits with 400 before the request ever
    // reaches the handler under test. (POST /api/v1/pickups/{id}/cancel is NOT on that allow-list
    // — only the literal "/api/v1/shipments", "/api/v1/pickups" paths and the shipment cancel
    // regex are — so cancel calls below use plain PostAsync.)
    private static async Task<HttpResponseMessage> PostIdempotentAsync<T>(HttpClient client, string url, T body, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private async Task<(string OwnerEmail, string Password)> CreateMerchantWithLoginAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"pickup-owner-{label}-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            $"Pickup Shipper {label} Pvt. Ltd.", $"Pickup Shipper {label}", "01-4444444", $"pickup-shipper-{label}@example.com",
            ownerEmail, "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, $"Address {label}");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        return (ownerEmail, "P@ssw0rd123!");
    }

    private async Task<HttpClient> LoginMerchantAsync(string ownerEmail, string password)
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, password));
        loginResponse.EnsureSuccessStatusCode();
        var auth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return client;
    }

    private async Task<Guid> CreateShipmentAsync(HttpClient merchantClient, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, string idempotencyKey)
    {
        var response = await PostIdempotentAsync(merchantClient, "/api/v1/shipments", ValidShipmentRequest(originBranchId, destinationBranchId, serviceTypeId), idempotencyKey);
        response.EnsureSuccessStatusCode();
        var shipment = await response.Content.ReadFromJsonAsync<ShipmentResponse>();
        return shipment!.Id;
    }

    [Fact]
    public async Task CreatePickupRequest_SharedOriginBranch_Returns201WithSharedBranchId()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "create-ok");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var (ownerEmail, password) = await CreateMerchantWithLoginAsync(staffClient, "create-ok");
        var merchantClient = await LoginMerchantAsync(ownerEmail, password);

        var shipment1Id = await CreateShipmentAsync(merchantClient, originBranchId, destinationBranchId, serviceTypeId, "create-ok-ship-1");
        var shipment2Id = await CreateShipmentAsync(merchantClient, originBranchId, destinationBranchId, serviceTypeId, "create-ok-ship-2");

        var createRequest = new CreatePickupRequestRequest(new[] { shipment1Id, shipment2Id }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), "Please pick up by 5pm");
        var response = await PostIdempotentAsync(merchantClient, "/api/v1/pickups", createRequest, "create-ok-pickup-1");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var pickup = await response.Content.ReadFromJsonAsync<PickupRequestResponse>();
        Assert.Equal(originBranchId, pickup!.BranchId);
        Assert.Equal(2, pickup.ShipmentIds.Length);
        Assert.Equal(PickupRequestStatus.Requested, pickup.Status);
    }

    [Fact]
    public async Task CreatePickupRequest_ForeignMerchantShipment_Returns400()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "create-foreign");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);

        var (ownerEmailA, passwordA) = await CreateMerchantWithLoginAsync(staffClient, "create-foreign-a");
        var merchantAClient = await LoginMerchantAsync(ownerEmailA, passwordA);
        var (ownerEmailB, passwordB) = await CreateMerchantWithLoginAsync(staffClient, "create-foreign-b");
        var merchantBClient = await LoginMerchantAsync(ownerEmailB, passwordB);

        var shipmentBId = await CreateShipmentAsync(merchantBClient, originBranchId, destinationBranchId, serviceTypeId, "create-foreign-ship-b1");

        var createRequest = new CreatePickupRequestRequest(new[] { shipmentBId }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), null);
        var response = await PostIdempotentAsync(merchantAClient, "/api/v1/pickups", createRequest, "create-foreign-pickup-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("SHIPMENT_VALIDATION_FAILED", body);
    }

    [Fact]
    public async Task ListPickups_Merchant_SeesOnlyOwnRequests_StaffWithOrgWideAccess_SeesBoth()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "list-scope");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);

        var (ownerEmailA, passwordA) = await CreateMerchantWithLoginAsync(staffClient, "list-scope-a");
        var merchantAClient = await LoginMerchantAsync(ownerEmailA, passwordA);
        var (ownerEmailB, passwordB) = await CreateMerchantWithLoginAsync(staffClient, "list-scope-b");
        var merchantBClient = await LoginMerchantAsync(ownerEmailB, passwordB);

        var shipmentAId = await CreateShipmentAsync(merchantAClient, originBranchId, destinationBranchId, serviceTypeId, "list-scope-ship-a1");
        var shipmentBId = await CreateShipmentAsync(merchantBClient, originBranchId, destinationBranchId, serviceTypeId, "list-scope-ship-b1");

        var createA = await PostIdempotentAsync(merchantAClient, "/api/v1/pickups",
            new CreatePickupRequestRequest(new[] { shipmentAId }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), "A's pickup"), "list-scope-pickup-a1");
        createA.EnsureSuccessStatusCode();
        var pickupA = await createA.Content.ReadFromJsonAsync<PickupRequestResponse>();

        var createB = await PostIdempotentAsync(merchantBClient, "/api/v1/pickups",
            new CreatePickupRequestRequest(new[] { shipmentBId }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), "B's pickup"), "list-scope-pickup-b1");
        createB.EnsureSuccessStatusCode();
        var pickupB = await createB.Content.ReadFromJsonAsync<PickupRequestResponse>();

        // Merchant A sees only its own request.
        var listAsA = await merchantAClient.GetAsync("/api/v1/pickups");
        Assert.Equal(HttpStatusCode.OK, listAsA.StatusCode);
        var pickupsAsA = await listAsA.Content.ReadFromJsonAsync<List<PickupRequestResponse>>();
        Assert.Single(pickupsAsA!);
        Assert.Equal(pickupA!.Id, pickupsAsA![0].Id);

        // Merchant B sees only its own request — proves merchant-scoping, not just "the caller's
        // request happens to be first."
        var listAsB = await merchantBClient.GetAsync("/api/v1/pickups");
        var pickupsAsB = await listAsB.Content.ReadFromJsonAsync<List<PickupRequestResponse>>();
        Assert.Single(pickupsAsB!);
        Assert.Equal(pickupB!.Id, pickupsAsB![0].Id);

        // Staff (org owner, AllBranches scope via OrganizationOwner) sees both, branch-scoped
        // rather than merchant-scoped — the org-wide-access half of the dual-purpose GET.
        var listAsStaff = await staffClient.GetAsync("/api/v1/pickups");
        Assert.Equal(HttpStatusCode.OK, listAsStaff.StatusCode);
        var pickupsAsStaff = await listAsStaff.Content.ReadFromJsonAsync<List<PickupRequestResponse>>();
        Assert.Contains(pickupsAsStaff!, p => p.Id == pickupA.Id);
        Assert.Contains(pickupsAsStaff!, p => p.Id == pickupB.Id);
    }

    // Direct-seeding approach for a branch-RESTRICTED staff user, mirroring
    // BranchAuthorizationEndpointTests.SeedScopedUserTokenAsync: no grant/user-management endpoint
    // exists yet this phase, so the scoped user's role, UserRole scope and UserBranch grants are
    // persisted straight against AppDbContext.
    private async Task<(string Token, Guid UserId)> SeedScopedUserTokenAsync(
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

        return (jwtTokenService.IssueAccessToken(scopedUserId, organizationId, email), scopedUserId);
    }

    private async Task GrantBranchToUserAsync(Guid organizationId, Guid userId, Guid branchId, Guid grantedByUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Set<UserBranch>().Add(new UserBranch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BranchId = branchId,
            OrganizationId = organizationId,
            GrantedAt = DateTimeOffset.UtcNow,
            GrantedByUserId = grantedByUserId
        });
        await dbContext.SaveChangesAsync();
    }

    // Proves the staff half of the dual-purpose GET is genuinely BRANCH-scoped, not merely
    // permission-gated: a staff user holding pickup.read but granted only an unrelated branch must
    // NOT see a pickup request whose BranchId is the (different) origin branch; granting that same
    // user the actual origin branch must then make it visible. Complements the org-wide-access
    // assertion in ListPickups_Merchant_SeesOnlyOwnRequests_StaffWithOrgWideAccess_SeesBoth above,
    // which alone would pass identically even if branch scoping were entirely broken (an
    // AllBranches-scoped checker returns everything regardless).
    [Fact]
    public async Task ListPickups_BranchRestrictedStaff_OnlySeesRequestsForGrantedBranch()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "branch-scope");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);

        // An unrelated branch, never used as anyone's origin, to grant the scoped user instead of
        // the real origin branch.
        var unrelatedZoneResponse = await staffClient.PostAsJsonAsync("/api/v1/delivery-zones", new CreateDeliveryZoneRequest($"Unrelated-{Guid.NewGuid():N}", KathmanduWard1Areas()));
        var unrelatedZone = await unrelatedZoneResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        var unrelatedBranchResponse = await staffClient.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"UB-{Guid.NewGuid():N}"[..10], unrelatedZone!.Id));
        var unrelatedBranch = await unrelatedBranchResponse.Content.ReadFromJsonAsync<BranchResponse>();

        var (ownerEmail, password) = await CreateMerchantWithLoginAsync(staffClient, "branch-scope");
        var merchantClient = await LoginMerchantAsync(ownerEmail, password);
        var shipmentId = await CreateShipmentAsync(merchantClient, originBranchId, destinationBranchId, serviceTypeId, "branch-scope-ship-1");
        var createResponse = await PostIdempotentAsync(merchantClient, "/api/v1/pickups",
            new CreatePickupRequestRequest(new[] { shipmentId }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), null), "branch-scope-pickup-1");
        createResponse.EnsureSuccessStatusCode();
        var pickup = await createResponse.Content.ReadFromJsonAsync<PickupRequestResponse>();

        var (scopedToken, scopedUserId) = await SeedScopedUserTokenAsync(org.OrganizationId, org.UserId, "pickup.read", unrelatedBranch!.Id);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopedToken);

        // Granted only the unrelated branch: the request (BranchId == originBranchId) must not
        // be visible yet, even though this user genuinely holds pickup.read.
        var listBeforeGrant = await scopedClient.GetAsync("/api/v1/pickups");
        Assert.Equal(HttpStatusCode.OK, listBeforeGrant.StatusCode);
        var pickupsBeforeGrant = await listBeforeGrant.Content.ReadFromJsonAsync<List<PickupRequestResponse>>();
        Assert.DoesNotContain(pickupsBeforeGrant!, p => p.Id == pickup!.Id);

        // Granting the actual origin branch makes it visible — isolates branch scoping as the
        // deciding factor, not the permission check (which passed both times).
        await GrantBranchToUserAsync(org.OrganizationId, scopedUserId, originBranchId, org.UserId);

        var listAfterGrant = await scopedClient.GetAsync("/api/v1/pickups");
        Assert.Equal(HttpStatusCode.OK, listAfterGrant.StatusCode);
        var pickupsAfterGrant = await listAfterGrant.Content.ReadFromJsonAsync<List<PickupRequestResponse>>();
        var visible = Assert.Single(pickupsAfterGrant!, p => p.Id == pickup!.Id);
        Assert.Equal(shipmentId, Assert.Single(visible.Shipments).Id);
    }

    private async Task<ShipmentResponse> CreateShipmentResponseAsync(HttpClient merchantClient, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, string idempotencyKey)
    {
        var response = await PostIdempotentAsync(merchantClient, "/api/v1/shipments", ValidShipmentRequest(originBranchId, destinationBranchId, serviceTypeId), idempotencyKey);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShipmentResponse>())!;
    }

    [Fact]
    public async Task ListPickups_ReturnsBranchNameStageAndShipments()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "list-enrich");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var (ownerEmail, password) = await CreateMerchantWithLoginAsync(staffClient, "list-enrich");
        var merchantClient = await LoginMerchantAsync(ownerEmail, password);
        var s1 = await CreateShipmentResponseAsync(merchantClient, originBranchId, destinationBranchId, serviceTypeId, "list-enrich-ship-1");
        var s2 = await CreateShipmentResponseAsync(merchantClient, originBranchId, destinationBranchId, serviceTypeId, "list-enrich-ship-2");
        var createResponse = await PostIdempotentAsync(merchantClient, "/api/v1/pickups",
            new CreatePickupRequestRequest(new[] { s1.Id, s2.Id }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), null), "list-enrich-pickup-1");
        createResponse.EnsureSuccessStatusCode();

        var list = await merchantClient.GetFromJsonAsync<List<PickupRequestResponse>>("/api/v1/pickups");

        var item = Assert.Single(list!);
        Assert.StartsWith("Branch ", item.BranchName);
        Assert.Equal(PickupStage.Requested, item.Stage);
        Assert.Equal(new[] { s1.Awb, s2.Awb }, item.Shipments.Select(s => s.Awb).ToArray());
    }

    [Fact]
    public async Task ListPickups_SecondMerchantSameOrg_SeesNoneOfFirstMerchantsParcels()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "list-isolation");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var (ownerEmailA, passwordA) = await CreateMerchantWithLoginAsync(staffClient, "list-isolation-a");
        var merchantAClient = await LoginMerchantAsync(ownerEmailA, passwordA);
        var (ownerEmailB, passwordB) = await CreateMerchantWithLoginAsync(staffClient, "list-isolation-b");
        var merchantBClient = await LoginMerchantAsync(ownerEmailB, passwordB);
        var shipmentA = await CreateShipmentResponseAsync(merchantAClient, originBranchId, destinationBranchId, serviceTypeId, "list-isolation-ship-a");
        var createResponse = await PostIdempotentAsync(merchantAClient, "/api/v1/pickups",
            new CreatePickupRequestRequest(new[] { shipmentA.Id }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), null), "list-isolation-pickup-a");
        createResponse.EnsureSuccessStatusCode();

        var response = await merchantBClient.GetAsync("/api/v1/pickups");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(shipmentA.Awb, body);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<PickupRequestResponse>>())!);
    }

    [Fact]
    public async Task CancelPickupRequest_Own_Returns200Cancelled()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "cancel-own");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var (ownerEmail, password) = await CreateMerchantWithLoginAsync(staffClient, "cancel-own");
        var merchantClient = await LoginMerchantAsync(ownerEmail, password);

        var shipmentId = await CreateShipmentAsync(merchantClient, originBranchId, destinationBranchId, serviceTypeId, "cancel-own-ship-1");
        var createResponse = await PostIdempotentAsync(merchantClient, "/api/v1/pickups",
            new CreatePickupRequestRequest(new[] { shipmentId }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), null), "cancel-own-pickup-1");
        createResponse.EnsureSuccessStatusCode();
        var pickup = await createResponse.Content.ReadFromJsonAsync<PickupRequestResponse>();

        var cancelResponse = await merchantClient.PostAsync($"/api/v1/pickups/{pickup!.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<PickupRequestResponse>();
        Assert.Equal(PickupRequestStatus.Cancelled, cancelled!.Status);
    }

    [Fact]
    public async Task CancelPickupRequest_ForeignMerchant_Returns404()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "cancel-foreign");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);

        var (ownerEmailA, passwordA) = await CreateMerchantWithLoginAsync(staffClient, "cancel-foreign-a");
        var merchantAClient = await LoginMerchantAsync(ownerEmailA, passwordA);
        var (ownerEmailB, passwordB) = await CreateMerchantWithLoginAsync(staffClient, "cancel-foreign-b");
        var merchantBClient = await LoginMerchantAsync(ownerEmailB, passwordB);

        var shipmentAId = await CreateShipmentAsync(merchantAClient, originBranchId, destinationBranchId, serviceTypeId, "cancel-foreign-ship-a1");
        var createResponse = await PostIdempotentAsync(merchantAClient, "/api/v1/pickups",
            new CreatePickupRequestRequest(new[] { shipmentAId }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), null), "cancel-foreign-pickup-a1");
        createResponse.EnsureSuccessStatusCode();
        var pickupA = await createResponse.Content.ReadFromJsonAsync<PickupRequestResponse>();

        var cancelAsB = await merchantBClient.PostAsync($"/api/v1/pickups/{pickupA!.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, cancelAsB.StatusCode);
    }

    [Fact]
    public async Task ApiKeyCaller_ScopedForPickupsCreate_CanCreatePickup_Returns201()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "apikey-create");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var (ownerEmail, password) = await CreateMerchantWithLoginAsync(staffClient, "apikey-create");
        var merchantClient = await LoginMerchantAsync(ownerEmail, password);

        var shipmentId = await CreateShipmentAsync(merchantClient, originBranchId, destinationBranchId, serviceTypeId, "apikey-create-ship-1");

        var createKeyResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/api-keys",
            new CreateApiKeyRequest(new[] { MerchantPermissionCatalog.PickupsCreate }));
        createKeyResponse.EnsureSuccessStatusCode();
        var apiKey = await createKeyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var apiKeyClient = _factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey!.PlaintextKey);

        var createRequest = new CreatePickupRequestRequest(new[] { shipmentId }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), "Via API key");
        var response = await PostIdempotentAsync(apiKeyClient, "/api/v1/pickups", createRequest, "apikey-create-pickup-1");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var pickup = await response.Content.ReadFromJsonAsync<PickupRequestResponse>();
        Assert.Equal(originBranchId, pickup!.BranchId);
    }
}
