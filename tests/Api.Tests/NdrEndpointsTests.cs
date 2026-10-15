using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Identity.Domain;
using Courier.Manifests.Application;
using Courier.Manifests.Domain;
using Courier.Merchants.Application;
using Courier.Ndr.Application;
using Courier.Ndr.Domain;
using Courier.Persistence;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

public class NdrEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public NdrEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Ndr Org {suffix}", $"ndr-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
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

    private async Task<Guid> CreateMerchantAsync(HttpClient staffClient)
    {
        var response = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Shipper Pvt. Ltd.", "Shipper", "01-4444444", "shipper@example.com",
            $"shipper-owner-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        var merchant = await response.Content.ReadFromJsonAsync<MerchantResponse>();
        return merchant!.Id;
    }

    private static CreateShipmentRequest ValidShipmentRequest(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId) => new(
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 0m, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 1, 500m) });

    private async Task<Guid> CreateRiderAsync(HttpClient client, Guid branchId, RiderRoleType roleType)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, $"LIC-{suffix[..10]}", branchId, roleType));
        var rider = await response.Content.ReadFromJsonAsync<RiderResponse>();
        return rider!.Id;
    }

    // Books a shipment and walks it all the way to a failed delivery attempt — pickup (Phase 8),
    // sort/dispatch/receive via Manifests (Phase 7), assign+scan out for delivery (Phase 8), then a
    // failed attempt, which auto-creates the NDR this whole test file exercises (Phase 9, Task 6).
    private async Task<ShipmentResponse> BookAndFailDeliveryAsync(
        HttpClient client, Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, Guid pickupRiderId, Guid deliveryRiderId)
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        using (var scanRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{pickupRiderId}/scan/pickup")
        { Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) })
        {
            scanRequest.Headers.Add("Idempotency-Key", $"scan-{Guid.NewGuid():N}");
            await client.SendAsync(scanRequest);
        }
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { });

        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(deliveryRiderId, shipment.Id, DeliveryTaskType.Delivery));
        using var failRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/fail")
        { Content = JsonContent.Create(new RecordFailedAttemptRequest(shipment.Id, DeliveryAttemptReason.CustomerUnavailable, "No one home", null, null, true, "Try tomorrow")) };
        failRequest.Headers.Add("Idempotency-Key", $"fail-{Guid.NewGuid():N}");
        await client.SendAsync(failRequest);

        return (await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>())!;
    }

    private async Task<NdrResponse> GetOpenNdrForShipmentAsync(HttpClient client, Guid shipmentId)
    {
        var ndrs = await (await client.GetAsync($"/api/v1/ndrs?shipmentId={shipmentId}")).Content.ReadFromJsonAsync<PagedResult<NdrResponse>>();
        return ndrs!.Items.Single();
    }

    [Fact]
    public async Task List_Paginates_ReturnsDisjointPagesInNewestFirstOrder()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "ndr-list-page");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var failed = new List<ShipmentResponse>();
        for (var i = 0; i < 3; i++)
        {
            var shipment = await BookAndFailDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);
            failed.Add(shipment);
        }

        var page1 = await (await client.GetAsync("/api/v1/ndrs?page=1&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<NdrResponse>>();
        var page2 = await (await client.GetAsync("/api/v1/ndrs?page=2&pageSize=2")).Content.ReadFromJsonAsync<PagedResult<NdrResponse>>();

        Assert.Equal(3, page1!.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2!.Items);

        var page1ShipmentIds = page1.Items.Select(n => n.ShipmentId).ToHashSet();
        var page2ShipmentIds = page2.Items.Select(n => n.ShipmentId).ToHashSet();
        Assert.Empty(page1ShipmentIds.Intersect(page2ShipmentIds));
        Assert.Equal(failed.Select(s => s.Id).ToHashSet(), page1ShipmentIds.Union(page2ShipmentIds).ToHashSet());
    }

    [Fact]
    public async Task FullRtoPath_FailedAttemptToRtoDelivered_MovesThroughEveryStatusViaManifests()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "rto-success");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var shipment = await BookAndFailDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);
        Assert.Equal(ShipmentStatus.NdrCreated, shipment.CurrentStatus);

        var ndr = await GetOpenNdrForShipmentAsync(client, shipment.Id);
        Assert.Equal(NdrStatus.Open, ndr.Status);

        await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/update", new UpdateNdrRequest(NdrStatus.Contacting, "Phone rang out", null, null));
        await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/update", new UpdateNdrRequest(NdrStatus.CustomerConfirmed, "Customer confirmed refusal", null, null));
        var afterRtoRequested = await (await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/update", new UpdateNdrRequest(NdrStatus.RtoRequested, null, "Return to merchant", null))).Content.ReadFromJsonAsync<NdrResponse>();
        Assert.Equal(NdrStatus.RtoRequested, afterRtoRequested!.Status);

        var resolveResponse = await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/resolve", new ResolveNdrRequest(NdrResolution.Rto, "Customer refused after three contacts"));
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
        var resolved = await resolveResponse.Content.ReadFromJsonAsync<NdrResponse>();
        Assert.Equal(NdrStatus.Resolved, resolved!.Status);

        var afterInitiate = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.RtoInitiated, afterInitiate!.CurrentStatus);
        Assert.Equal(destinationBranchId, afterInitiate.CurrentBranchId);

        var returnShipments = await (await client.GetAsync($"/api/v1/return-shipments?originalShipmentId={shipment.Id}")).Content.ReadFromJsonAsync<List<ReturnShipmentResponse>>();
        var returnShipment = Assert.Single(returnShipments!);
        Assert.Equal(shipment.Awb, returnShipment.OriginalAwb);
        Assert.StartsWith("RTO-", returnShipment.ReturnAwb);
        Assert.Equal(destinationBranchId, returnShipment.OriginBranchId);
        Assert.Equal(originBranchId, returnShipment.DestinationBranchId);

        // The return leg is a real Manifest, just Purpose=Return and traveling the opposite
        // direction — scanned by the ORIGINAL shipment's own Awb (ReturnAwb is ReturnShipment's own
        // administrative tracking number, not what Manifests scans against).
        var returnManifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(destinationBranchId, originBranchId, ManifestPurpose.Return))).Content.ReadFromJsonAsync<ManifestResponse>();
        Assert.Equal(ManifestPurpose.Return, returnManifest!.Purpose);

        var rtoScanResponse = await client.PostAsJsonAsync($"/api/v1/manifests/{returnManifest.Id}/rto-scan", new ScanShipmentRequest(shipment.Awb));
        Assert.Equal(HttpStatusCode.OK, rtoScanResponse.StatusCode);
        await client.PostAsync($"/api/v1/manifests/{returnManifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{returnManifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 5678", null));

        var afterRtoDispatch = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.RtoInTransit, afterRtoDispatch!.CurrentStatus);

        await client.PostAsJsonAsync($"/api/v1/manifests/{returnManifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));
        var afterRtoReceive = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.RtoAtOrigin, afterRtoReceive!.CurrentStatus);
        Assert.Equal(originBranchId, afterRtoReceive.CurrentBranchId);

        var confirmResponse = await client.PostAsync($"/api/v1/return-shipments/{returnShipment.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmed = await confirmResponse.Content.ReadFromJsonAsync<ReturnShipmentResponse>();
        Assert.NotNull(confirmed!.MerchantConfirmedAt);
        Assert.Equal(ShipmentStatus.RtoDelivered, confirmed.CurrentStatus);

        var final = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.RtoDelivered, final!.CurrentStatus);
    }

    [Fact]
    public async Task FullReattemptPath_FailedAttemptBackToOutForDeliveryThenDelivered_AttemptNumberReachesTwo()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "reattempt");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);

        var shipment = await BookAndFailDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);
        var ndr = await GetOpenNdrForShipmentAsync(client, shipment.Id);

        await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/update", new UpdateNdrRequest(NdrStatus.Contacting, null, null, null));
        await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/update", new UpdateNdrRequest(NdrStatus.CustomerConfirmed, null, null, null));
        await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/update", new UpdateNdrRequest(NdrStatus.ReattemptScheduled, null, "Redeliver tomorrow morning", DateTimeOffset.UtcNow.AddDays(1)));

        var resolveResponse = await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/resolve", new ResolveNdrRequest(NdrResolution.Reattempt, null));
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        var afterReattempt = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.OutForDelivery, afterReattempt!.CurrentStatus);
        Assert.Equal(deliveryRiderId, afterReattempt.CurrentCustodianRiderId);

        using var completeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{deliveryRiderId}/delivery-attempts/complete")
        { Content = JsonContent.Create(new CompleteDeliveryRequest(shipment.Id, "Recipient One", true, null, null, null, null)) };
        completeRequest.Headers.Add("Idempotency-Key", $"complete-{Guid.NewGuid():N}");
        var completeResponse = await client.SendAsync(completeRequest);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var attempt = await completeResponse.Content.ReadFromJsonAsync<DeliveryAttemptResponse>();
        Assert.True(attempt!.Succeeded);
        Assert.Equal(2, attempt.AttemptNumber);

        var final = await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Delivered, final!.CurrentStatus);
    }

    [Fact]
    public async Task Ndrs_OrgBCannotReadOrgAsNdr()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "iso-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(clientA);
        var merchantId = await CreateMerchantAsync(clientA);
        var pickupRiderId = await CreateRiderAsync(clientA, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(clientA, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndFailDeliveryAsync(clientA, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);
        var ndr = await GetOpenNdrForShipmentAsync(clientA, shipment.Id);

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "iso-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var response = await clientB.GetAsync($"/api/v1/ndrs/{ndr.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetNdrs_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/ndrs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetNdrsSummary_ReturnsStatusCounts()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "summary");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/ndrs/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        Assert.NotNull(body);
    }

    [Fact]
    public async Task GetNdrsSummary_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/ndrs/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetReturnShipmentsSummary_ReturnsEmptySummaryForNewOrg()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "summary");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/return-shipments/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReturnShipmentSummary>();
        Assert.Equal(0, body!.MerchantConfirmed);
        Assert.Equal(0, body.PendingConfirmation);
    }

    [Fact]
    public async Task GetReturnShipmentsSummary_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/return-shipments/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

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

    private async Task<HttpClient> CreateMerchantClientAsync(HttpClient staffClient)
    {
        var ownerEmail = $"ndr-merchant-{Guid.NewGuid():N}@example.com";
        var createResponse = await staffClient.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Attempts Shipper Pvt. Ltd.", "Attempts Shipper", "01-4444444", "attempts-shipper@example.com",
            ownerEmail, "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        createResponse.EnsureSuccessStatusCode();

        var merchantClient = _factory.CreateClient();
        var loginResponse = await merchantClient.PostAsJsonAsync("/api/v1/merchant/auth/login", new MerchantLoginRequest(ownerEmail, "P@ssw0rd123!"));
        loginResponse.EnsureSuccessStatusCode();
        var auth = await loginResponse.Content.ReadFromJsonAsync<MerchantAuthResult>();
        merchantClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return merchantClient;
    }

    [Fact]
    public async Task GetDeliveryAttempts_AfterAFailedAttempt_ReturnsIt()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "attempts");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndFailDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);

        var response = await client.GetAsync($"/api/v1/shipments/{shipment.Id}/delivery-attempts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeliveryAttemptListResponse>();
        var attempt = Assert.Single(body!.Items);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.False(attempt.Succeeded);
        Assert.Equal(DeliveryAttemptReason.CustomerUnavailable, attempt.Reason);
        Assert.Equal("No one home", attempt.Notes);
        Assert.True(attempt.CustomerContacted);
        Assert.Equal("Try tomorrow", attempt.NextAction);
        Assert.Equal(deliveryRiderId, attempt.RiderId);
        Assert.DoesNotContain("latitude", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDeliveryAttempts_UserOutsideBranchScope_Returns404()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client, "attempts-scope");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndFailDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);
        var (unrelatedBranchId, _, _) = await SeedBranchesAsync(client);
        var token = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "ndr.read", unrelatedBranchId);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await scopedClient.GetAsync($"/api/v1/shipments/{shipment.Id}/delivery-attempts");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDeliveryAttempts_WithoutNdrRead_Returns403()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client, "attempts-403");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var (originBranchId, _, _) = await SeedBranchesAsync(client);
        var token = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "shipment.read", originBranchId);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await scopedClient.GetAsync($"/api/v1/shipments/{Guid.NewGuid()}/delivery-attempts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDeliveryAttempts_MerchantCaller_Returns403()
    {
        // Merchant tokens lack ndr.read, so RequirePermission returns 403 before the handler's merchant guard runs (the guard is defence in depth).
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "attempts-merchant");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        await SeedBranchesAsync(client);
        var merchantClient = await CreateMerchantClientAsync(client);

        var response = await merchantClient.GetAsync($"/api/v1/shipments/{Guid.NewGuid()}/delivery-attempts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDeliveryAttempts_UnknownShipment_Returns404()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "attempts-404");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync($"/api/v1/shipments/{Guid.NewGuid()}/delivery-attempts");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDeliveryAttempts_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/shipments/{Guid.NewGuid()}/delivery-attempts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetNdrs_AssignedAndReasonFilters_RoundTrip()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "ndr-filters");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var pickupRiderId = await CreateRiderAsync(client, originBranchId, RiderRoleType.PickupRider);
        var deliveryRiderId = await CreateRiderAsync(client, destinationBranchId, RiderRoleType.DeliveryRider);
        var shipment = await BookAndFailDeliveryAsync(client, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId, deliveryRiderId);
        var ndr = await GetOpenNdrForShipmentAsync(client, shipment.Id);

        async Task<PagedResult<NdrResponse>> ListAsync(string query) =>
            (await (await client.GetAsync($"/api/v1/ndrs?{query}")).Content.ReadFromJsonAsync<PagedResult<NdrResponse>>())!;

        Assert.Contains((await ListAsync("assigned=unassigned")).Items, n => n.Id == ndr.Id);
        Assert.DoesNotContain((await ListAsync("assigned=me")).Items, n => n.Id == ndr.Id);

        // Any follow-up update makes the caller the owner (NdrService.UpdateAsync).
        var update = await client.PostAsJsonAsync($"/api/v1/ndrs/{ndr.Id}/update", new UpdateNdrRequest(NdrStatus.Contacting, "Called once", null, null));
        update.EnsureSuccessStatusCode();

        Assert.Contains((await ListAsync("assigned=me")).Items, n => n.Id == ndr.Id);
        Assert.DoesNotContain((await ListAsync("assigned=unassigned")).Items, n => n.Id == ndr.Id);
        Assert.Contains((await ListAsync("reason=CustomerUnavailable")).Items, n => n.Id == ndr.Id);
        Assert.DoesNotContain((await ListAsync("reason=WrongAddress")).Items, n => n.Id == ndr.Id);
    }

    [Fact]
    public async Task GetNdrs_InvalidFilters_Return400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "ndr-filters-400");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var badAssigned = await client.GetAsync("/api/v1/ndrs?assigned=everyone");
        var badReason = await client.GetAsync("/api/v1/ndrs?reason=Bogus");

        Assert.Equal(HttpStatusCode.BadRequest, badAssigned.StatusCode);
        Assert.Contains("NDR_VALIDATION_FAILED", await badAssigned.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, badReason.StatusCode);
    }
}
