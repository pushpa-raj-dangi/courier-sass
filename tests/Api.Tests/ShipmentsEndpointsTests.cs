using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Events.Domain;
using Courier.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class ShipmentsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public ShipmentsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Shipments Org {suffix}", $"shipments-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
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

    private static CreateShipmentRequest ValidShipmentRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 2, 500m) });

    // Merchant-track callers (merchant JWT or API key) are gated by IdempotencyMiddleware (Task 7)
    // on POST /api/v1/shipments and POST /api/v1/shipments/{id}/cancel — every merchant-client POST
    // in this file must carry an Idempotency-Key header or the middleware short-circuits with 400
    // before the request ever reaches the handler under test.
    private static async Task<HttpResponseMessage> PostIdempotentAsync<T>(HttpClient client, string url, T body, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostIdempotentAsync(HttpClient client, string url, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private async Task<Guid> CreateMerchantAsync(HttpClient staffClient)
    {
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Shipper Pvt. Ltd.", "Shipper", "01-4444444", "shipper@example.com",
            $"shipper-owner-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return merchant!.Id;
    }

    [Fact]
    public async Task PricingSnapshot_RateCardSupersededAfterBooking_ShipmentTotalChargeUnchanged()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "price-lock");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 150m);
        var merchantId = await CreateMerchantAsync(client);

        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(150m, shipment!.TotalCharge);

        // Supersede the RateCard with a new, higher rate — same route/service/weight.
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 999m);

        // Prove the new rate card is actually live for NEW bookings — without this, the test below
        // (re-fetching the already-booked shipment and expecting 150m) would pass identically
        // whether pricing is genuinely locked at booking OR the supersede call above silently
        // no-op'd, since neither case would change the already-booked shipment's price.
        var secondShipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(999m, secondShipment!.TotalCharge);

        var reloaded = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(150m, reloaded!.TotalCharge);
    }

    [Fact]
    public async Task CreateShipment_SetsCreatedAtCloseToNow()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "created-at");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(client);

        var before = DateTimeOffset.UtcNow;
        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(shipment!.CreatedAt, before, after);
    }

    [Fact]
    public async Task List_Paginates_ReturnsDisjointPagesInNewestFirstOrder()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "list-page");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(client);

        var booked = new List<ShipmentResponse>();
        for (var i = 0; i < 3; i++)
        {
            var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();
            booked.Add(shipment!);
        }

        var page1 = await (await client.GetAsync("/api/v1/shipments?page=1&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<ShipmentResponse>>();
        var page2 = await (await client.GetAsync("/api/v1/shipments?page=2&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<ShipmentResponse>>();

        Assert.Equal(3, page1!.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2!.Items);

        var page1Ids = page1.Items.Select(s => s.Id).ToHashSet();
        var page2Ids = page2.Items.Select(s => s.Id).ToHashSet();
        Assert.Empty(page1Ids.Intersect(page2Ids));
        Assert.Equal(booked.Select(s => s.Id).ToHashSet(), page1Ids.Union(page2Ids).ToHashSet());

        var expectedNewestFirst = booked.OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id).Select(s => s.Id).ToList();
        Assert.Equal(expectedNewestFirst.Take(2), page1.Items.Select(s => s.Id));
        Assert.Equal(expectedNewestFirst.Skip(2), page2.Items.Select(s => s.Id));
    }

    [Fact]
    public async Task Shipments_OrgBCannotReadOrgAsShipment()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "iso-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(clientA);
        await SeedRateCardAsync(clientA, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(clientA);
        var shipment = await (await clientA.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "iso-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var response = await clientB.GetAsync($"/api/v1/shipments/{shipment!.Awb}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Returns (OwnerEmail, Password) for a freshly created merchant, mirroring
    // tests/Api.Tests/MerchantAuthAndStaffEndpointsTests.cs's CreateMerchantAsync exactly — that
    // file's helper captures the owner email at creation time rather than trying to re-derive it
    // later, since CreateMerchantRequest's owner email is caller-supplied, not server-generated.
    private async Task<(string OwnerEmail, string Password)> CreateMerchantWithLoginAsync(HttpClient staffClient, string label)
    {
        var ownerEmail = $"shipper-owner-{label}-{Guid.NewGuid():N}@example.com";
        var request = new CreateMerchantRequest(
            $"Shipper {label} Pvt. Ltd.", $"Shipper {label}", "01-4444444", $"shipper-{label}@example.com",
            ownerEmail, "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, $"Address {label}");
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", request);
        response.EnsureSuccessStatusCode();
        return (ownerEmail, "P@ssw0rd123!");
    }

    [Fact]
    public async Task Merchant_CanCreateAndCancelOwnShipment_ButNotAnotherMerchants()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "merchant-flow");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        await SeedRateCardAsync(staffClient, originBranchId, destinationBranchId, serviceTypeId, 100m);

        var (ownerEmailA, passwordA) = await CreateMerchantWithLoginAsync(staffClient, "a");
        var merchantAClient = _factory.CreateClient();
        var merchantALoginResponse = await merchantAClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmailA, passwordA));
        Assert.Equal(HttpStatusCode.OK, merchantALoginResponse.StatusCode);
        var merchantAAuth = await merchantALoginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantAClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", merchantAAuth!.AccessToken);

        // Merchant JWT callers never supply MerchantId in the body — it's forced server-side from
        // the token's merchant_id claim, so Guid.Empty here proves that (a real value would only
        // matter if the server trusted it, which it must not per CLAUDE.md §33).
        var createResponse = await PostIdempotentAsync(merchantAClient, "/api/v1/shipments", ValidShipmentRequest(Guid.Empty, originBranchId, destinationBranchId, serviceTypeId), "merchant-flow-create-a1");
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var shipmentA = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(merchantAAuth.MerchantId, shipmentA!.MerchantId);

        var cancelResponse = await PostIdempotentAsync(merchantAClient, $"/api/v1/shipments/{shipmentA.Id}/cancel", "merchant-flow-cancel-a1");
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Cancelled, cancelled!.CurrentStatus);

        // A second merchant, in the same org, must not be able to read or cancel merchant A's
        // shipment — this is the merchant-scoping axis, distinct from the tenant axis proven by
        // Shipments_OrgBCannotReadOrgAsShipment above (same org here, different merchant).
        var (ownerEmailB, passwordB) = await CreateMerchantWithLoginAsync(staffClient, "b");
        var merchantBClient = _factory.CreateClient();
        var merchantBLoginResponse = await merchantBClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmailB, passwordB));
        var merchantBAuth = await merchantBLoginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", merchantBAuth!.AccessToken);

        // shipmentA is already Cancelled at this point, so re-cancelling from B would 400 even if
        // isolation were broken — create a second, still-cancellable shipment for A to make this
        // a real isolation check rather than a coincidental transition-guard pass.
        var secondCreateResponse = await PostIdempotentAsync(merchantAClient, "/api/v1/shipments", ValidShipmentRequest(Guid.Empty, originBranchId, destinationBranchId, serviceTypeId), "merchant-flow-create-a2");
        var shipmentA2 = await secondCreateResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        var readAsB = await merchantBClient.GetAsync($"/api/v1/shipments/{shipmentA2!.Awb}");
        Assert.Equal(HttpStatusCode.NotFound, readAsB.StatusCode);

        var cancelAsB = await PostIdempotentAsync(merchantBClient, $"/api/v1/shipments/{shipmentA2.Id}/cancel", "merchant-flow-cancel-b1");
        Assert.Equal(HttpStatusCode.NotFound, cancelAsB.StatusCode);
    }

    [Fact]
    public async Task PostShipments_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Proves ShipmentService.CreateShipmentAsync's AWB collision-retry (insert-and-catch-
    // DbUpdateException-retry, ShipmentService.cs) actually survives real concurrency — EF's
    // InMemory provider used by Shipments.Tests' unit tests doesn't enforce the unique
    // (OrganizationId, Awb) index, so only a real-Postgres, real-concurrent-request test like this
    // one exercises the retry path at all. Mirrors Phase 5's analogous
    // CreateRateCard_ConcurrentRequestsFor... precedent for the RateCard versioning race.
    [Fact]
    public async Task CreateShipment_ConcurrentRequestsForSameOrg_BothSucceedWithDistinctAwbs()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "awb-race");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(client);

        var request1 = ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId);
        var request2 = ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId);

        var task1 = client.PostAsJsonAsync("/api/v1/shipments", request1);
        var task2 = client.PostAsJsonAsync("/api/v1/shipments", request2);
        var responses = await Task.WhenAll(task1, task2);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var shipment1 = await responses[0].Content.ReadFromJsonAsync<ShipmentResponse>();
        var shipment2 = await responses[1].Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.NotEqual(shipment1!.Awb, shipment2!.Awb);
    }

    // The 2-way test above only ever needs one retry to resolve, which already passed even with
    // the AWB collision-retry loop's original 3-attempt budget. This reproduces the real bug found
    // seeding 2000 demo shipments at concurrency 16: 20 requests racing the same (OrganizationId,
    // day-prefix) can collide with each other repeatedly, not just with one winner, and the
    // original 3-attempt budget with no backoff between retries exhausted for several of them
    // (raw 500s). maxAwbAttempts=8 plus jittered backoff (ShipmentService.cs) fixes it.
    [Fact]
    public async Task CreateShipment_ManyConcurrentRequestsForSameOrg_AllSucceedWithDistinctAwbs()
    {
        const int concurrency = 20;
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "awb-race-many");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(client);

        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId)));
        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var shipments = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<ShipmentResponse>()));
        Assert.Equal(concurrency, shipments.Select(s => s!.Awb).Distinct().Count());
    }

    [Fact]
    public async Task GetShipmentsSummary_ReturnsStatusCounts_NotSwallowedByAwbRoute()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "summary");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/shipments/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        Assert.NotNull(body);
    }

    [Fact]
    public async Task GetShipmentsSummary_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/shipments/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/shipments?status=Bogus")]
    [InlineData("/api/v1/shipments?q=a")]
    [InlineData("/api/v1/shipments?createdFrom=2026-10-05T00:00:00Z&createdTo=2026-10-01T00:00:00Z")]
    public async Task List_InvalidFilter_Returns400WithValidationCode(string url)
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "list-bad-filter");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SHIPMENT_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task List_NonUtcOffsetCreatedFrom_Returns200()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "list-offset");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/shipments?createdFrom=2026-10-05T00:00:00%2B05:45");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task List_RepeatedStatusParam_FiltersAndReturnsLastEventAt()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "list-status");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(client);
        await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));

        var created = await (await client.GetAsync("/api/v1/shipments?status=Created&status=PickupAssigned")).Content.ReadFromJsonAsync<PagedResult<ShipmentResponse>>();
        var inTransit = await (await client.GetAsync("/api/v1/shipments?status=InTransit")).Content.ReadFromJsonAsync<PagedResult<ShipmentResponse>>();

        Assert.Single(created!.Items);
        Assert.NotNull(created.Items[0].LastEventAt);
        Assert.Empty(inTransit!.Items);
    }

    private async Task<HttpClient> CreateBranchScopedStaffClientAsync(HttpClient ownerClient, Guid branchId, string roleName)
    {
        var roles = await (await ownerClient.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        var role = roles!.Single(r => r.Name == roleName);

        var staffEmail = $"staff-{Guid.NewGuid():N}@example.com";
        (await ownerClient.PostAsJsonAsync("/api/v1/users/invite", new InviteUserRequest(staffEmail, "Staff Person", null, role.Id, branchId, null))).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outboxMessage = await dbContext.Set<OutboxMessage>().IgnoreQueryFilters()
            .Where(m => m.EventType == "user.invited")
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync(m => m.Payload.Contains(staffEmail));
        var payload = JsonDocument.Parse(outboxMessage.Payload).RootElement;
        var inviteUrl = payload.GetProperty("InviteUrl").GetString()!;
        var token = Uri.UnescapeDataString(inviteUrl.Split("token=")[1]);
        (await ownerClient.PostAsJsonAsync("/api/v1/auth/accept-invite", new AcceptInviteRequest(token, "St@ffPassword1"))).EnsureSuccessStatusCode();

        var staffClient = _factory.CreateClient();
        var loginResponse = await staffClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(staffEmail, "St@ffPassword1"));
        loginResponse.EnsureSuccessStatusCode();
        var staffAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffAuth!.AccessToken);
        return staffClient;
    }

    private async Task<(HttpClient Owner, ShipmentResponse Shipment, Guid OriginBranchId)> BookOneAsync(string label)
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, label);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantId = await CreateMerchantAsync(client);
        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();
        return (client, shipment!, originBranchId);
    }

    [Fact]
    public async Task GetByAwb_StaffScopedToUnrelatedBranch_Returns404_ScopedToOrigin_Returns200()
    {
        var (owner, shipment, originBranchId) = await BookOneAsync("scope-read");
        var otherBranch = await (await owner.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest($"XB-{Guid.NewGuid():N}"[..10]))).Content.ReadFromJsonAsync<BranchResponse>();

        var outsider = await CreateBranchScopedStaffClientAsync(owner, otherBranch!.Id, "BranchManager");
        var insider = await CreateBranchScopedStaffClientAsync(owner, originBranchId, "BranchManager");

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/shipments/{shipment.Awb}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/shipments/{shipment.Awb}/events")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await insider.GetAsync($"/api/v1/shipments/{shipment.Awb}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await insider.GetAsync($"/api/v1/shipments/{shipment.Awb}/events")).StatusCode);
    }

    [Fact]
    public async Task GetEvents_OwnerSeesCreatedEvent_WithoutMetadataOrCoordinates()
    {
        var (owner, shipment, _) = await BookOneAsync("events-owner");

        var response = await owner.GetAsync($"/api/v1/shipments/{shipment.Awb}/events");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"eventType\":\"Created\"", json);
        Assert.DoesNotContain("metadata", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("latitude", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEvents_OtherTenant_Returns404()
    {
        var (_, shipment, _) = await BookOneAsync("events-tenant-a");
        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "events-tenant-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/v1/shipments/{shipment.Awb}/events")).StatusCode);
    }

    [Fact]
    public async Task GetEvents_MerchantCaller_Returns403()
    {
        var (owner, shipment, _) = await BookOneAsync("events-merchant");
        var (ownerEmail, password) = await CreateMerchantWithLoginAsync(owner, "events");
        var merchantClient = _factory.CreateClient();
        var login = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = await login.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await merchantClient.GetAsync($"/api/v1/shipments/{shipment.Awb}/events")).StatusCode);
    }
}
