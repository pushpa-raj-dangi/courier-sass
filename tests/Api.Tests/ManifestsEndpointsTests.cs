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
using Courier.Persistence;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

public class ManifestsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public ManifestsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Manifests Org {suffix}", $"manifests-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private static CreateBranchRequest ValidBranchRequest(string code) => new(
        code, $"Branch {code}", BranchType.Hub, KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null, null);

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

    private static List<Guid> KathmanduWard1Areas() => new()
    {
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
    };

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

    // Walks a freshly booked shipment through Riders' real pickup flow up to AtOriginBranch — the
    // point at which this phase's /manifests/{id}/scan takes over. Uses a throwaway rider created
    // solely to satisfy the now-real (Phase 8) pickup-assignment requirement; Manifests' own tests
    // don't otherwise care which rider performed the pickup.
    private async Task<ShipmentResponse> CreateShipmentAtOriginBranchAsync(HttpClient client, Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId)
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        var riderResponse = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Pickup Rider", null, $"LIC-{Guid.NewGuid():N}"[..12], originBranchId, RiderRoleType.PickupRider));
        var rider = await riderResponse.Content.ReadFromJsonAsync<RiderResponse>();

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(rider!.Id, shipment!.Id, DeliveryTaskType.Pickup));
        using (var scanRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/riders/{rider.Id}/scan/pickup")
        { Content = JsonContent.Create(new RiderScanRequest(shipment.Awb)) })
        {
            scanRequest.Headers.Add("Idempotency-Key", $"scan-{Guid.NewGuid():N}");
            await client.SendAsync(scanRequest);
        }
        var afterOrigin = await (await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { })).Content.ReadFromJsonAsync<ShipmentResponse>();
        return afterOrigin!;
    }

    [Fact]
    public async Task FullManifestLifecycle_DraftToClosed_MovesShipmentThroughCustodyAndSurfacesMissingShipment()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "happy");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (fromBranchId, toBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);

        var received = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);
        var missing = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);

        var manifestResponse = await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId));
        Assert.Equal(HttpStatusCode.Created, manifestResponse.StatusCode);
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<ManifestResponse>();
        Assert.Equal(ManifestStatus.Draft, manifest!.Status);

        var scanReceivedResponse = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/scan", new ScanShipmentRequest(received.Awb));
        Assert.Equal(HttpStatusCode.OK, scanReceivedResponse.StatusCode);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/scan", new ScanShipmentRequest(missing.Awb));

        var afterScanShipment = await (await client.GetAsync($"/api/v1/shipments/{received.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.Sorted, afterScanShipment!.CurrentStatus);

        var sealResponse = await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        var sealed_ = await sealResponse.Content.ReadFromJsonAsync<ManifestResponse>();
        Assert.Equal(ManifestStatus.Sealed, sealed_!.Status);
        Assert.Equal(2, sealed_.ShipmentCount);

        var dispatchResponse = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        var dispatched = await dispatchResponse.Content.ReadFromJsonAsync<ManifestResponse>();
        Assert.Equal(ManifestStatus.InTransit, dispatched!.Status);
        Assert.Equal("BA 2 KHA 1234", dispatched.VehiclePlate);

        var afterDispatchShipment = await (await client.GetAsync($"/api/v1/shipments/{received.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.InTransit, afterDispatchShipment!.CurrentStatus);

        var receiveScanResponse = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(received.Awb));
        Assert.Equal(HttpStatusCode.OK, receiveScanResponse.StatusCode);

        var afterReceiveShipment = await (await client.GetAsync($"/api/v1/shipments/{received.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.AtDestinationBranch, afterReceiveShipment!.CurrentStatus);
        Assert.Equal(toBranchId, afterReceiveShipment.CurrentBranchId);

        var closeResponse = await client.PostAsync($"/api/v1/manifests/{manifest.Id}/close", null);
        var closed = await closeResponse.Content.ReadFromJsonAsync<ManifestResponse>();
        Assert.Equal(ManifestStatus.Closed, closed!.Status);
        Assert.Equal(1, closed.ReceivedCount);
        Assert.Equal(2, closed.ShipmentCount);

        var missingResponse = await client.GetAsync($"/api/v1/manifests/{manifest.Id}/missing-shipments");
        var missingList = await missingResponse.Content.ReadFromJsonAsync<List<MissingShipmentResponse>>();
        var missingEntry = Assert.Single(missingList!);
        Assert.Equal(missing.Awb, missingEntry.Awb);

        // The missing shipment was dispatched along with the rest of the manifest (dispatch is a
        // bulk action that doesn't know yet which shipments will fail to be receive-scanned) — its
        // last known status is InTransit, not Sorted, since it genuinely left the origin branch
        // without ever being confirmed to arrive.
        var afterCloseMissingShipment = await (await client.GetAsync($"/api/v1/shipments/{missing.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>();
        Assert.Equal(ShipmentStatus.InTransit, afterCloseMissingShipment!.CurrentStatus);
    }

    [Fact]
    public async Task Scan_ShipmentStillCreated_Returns400WithWrongStateReason()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "wrong-state");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (fromBranchId, toBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var shipment = await (await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, fromBranchId, toBranchId, serviceTypeId))).Content.ReadFromJsonAsync<ShipmentResponse>();
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();

        var response = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment!.Awb));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("MANIFEST_SCAN_REJECTED", body);
        Assert.Contains("WRONG_STATE", body);
    }

    [Fact]
    public async Task ReceiveScan_ShipmentNotOnThisManifest_Returns400WithNotOnManifestReason()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "not-on-manifest");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (fromBranchId, toBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var otherShipment = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();

        // otherShipment was never scanned onto this manifest, so it's still AtOriginBranch — the
        // manifest itself is still Draft, which fails MANIFEST_NOT_RECEIVING before NOT_ON_MANIFEST
        // would ever be reached, so exercise the check with a manifest that IS receiving instead.
        var scannedShipment = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(scannedShipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));

        var response = await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(otherShipment.Awb));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("MANIFEST_SCAN_REJECTED", body);
        Assert.Contains("NOT_ON_MANIFEST", body);
    }

    [Fact]
    public async Task Manifests_OrgBCannotReadOrgAsManifest()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "iso-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (fromBranchId, toBranchId, _) = await SeedBranchesAsync(clientA);
        var manifest = await (await clientA.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "iso-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var response = await clientB.GetAsync($"/api/v1/manifests/{manifest!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    [Fact]
    public async Task PostManifests_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetManifestsSummary_ReturnsStatusCounts()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "summary");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/manifests/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        Assert.NotNull(body);
    }

    [Fact]
    public async Task GetManifestsSummary_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/manifests/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ManifestSingleReads_UserWithNoAccessToEitherBranch_Return404()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client, "single-scope");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var (fromBranchId, toBranchId, _) = await SeedBranchesAsync(client);
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId)))
            .Content.ReadFromJsonAsync<ManifestResponse>();
        var (unrelatedBranchId, _, _) = await SeedBranchesAsync(client);

        var scopedToken = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "manifest.read", unrelatedBranchId);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopedToken);

        foreach (var path in new[] { "", "/missing-shipments", "/shipments" })
        {
            var response = await scopedClient.GetAsync($"/api/v1/manifests/{manifest!.Id}{path}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task ManifestSingleReads_UserScopedToDestinationBranch_Return200()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client, "single-dest");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var (fromBranchId, toBranchId, _) = await SeedBranchesAsync(client);
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId)))
            .Content.ReadFromJsonAsync<ManifestResponse>();

        var scopedToken = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "manifest.read", toBranchId);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scopedToken);

        var response = await scopedClient.GetAsync($"/api/v1/manifests/{manifest!.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetManifestShipments_ReturnsEveryScannedParcelWithItsAwb()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "parcels");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (fromBranchId, toBranchId, serviceTypeId) = await SeedBranchesAsync(client);
        var merchantId = await CreateMerchantAsync(client);
        var first = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);
        var second = await CreateShipmentAtOriginBranchAsync(client, merchantId, fromBranchId, toBranchId, serviceTypeId);
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId)))
            .Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(first.Awb));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/scan", new ScanShipmentRequest(second.Awb));

        var response = await client.GetAsync($"/api/v1/manifests/{manifest.Id}/shipments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parcels = await response.Content.ReadFromJsonAsync<List<ManifestShipmentResponse>>();
        Assert.Equal(new[] { first.Awb, second.Awb }, parcels!.Select(p => p.Awb).ToArray());
        Assert.All(parcels!, p => Assert.Null(p.ReceivedAt));
    }

    [Fact]
    public async Task GetManifestShipments_WithoutManifestRead_Returns403()
    {
        var client = _factory.CreateClient();
        var owner = await RegisterOrgAsync(client, "parcels-403");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var (fromBranchId, toBranchId, _) = await SeedBranchesAsync(client);
        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId)))
            .Content.ReadFromJsonAsync<ManifestResponse>();
        var token = await SeedScopedUserTokenAsync(owner.OrganizationId, owner.UserId, "shipment.read", fromBranchId);
        var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await scopedClient.GetAsync($"/api/v1/manifests/{manifest!.Id}/shipments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetManifestShipments_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/manifests/{Guid.NewGuid()}/shipments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetManifests_DirectionFilter_SplitsIncomingAndOutgoingForABranch()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "direction");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (fromBranchId, toBranchId, _) = await SeedBranchesAsync(client);
        var forward = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(fromBranchId, toBranchId)))
            .Content.ReadFromJsonAsync<ManifestResponse>();
        var back = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(toBranchId, fromBranchId)))
            .Content.ReadFromJsonAsync<ManifestResponse>();

        var incoming = await (await client.GetAsync($"/api/v1/manifests?branchId={toBranchId}&direction=incoming"))
            .Content.ReadFromJsonAsync<List<ManifestResponse>>();
        var outgoing = await (await client.GetAsync($"/api/v1/manifests?branchId={toBranchId}&direction=outgoing"))
            .Content.ReadFromJsonAsync<List<ManifestResponse>>();

        Assert.Contains(incoming!, m => m.Id == forward!.Id);
        Assert.DoesNotContain(incoming!, m => m.Id == back!.Id);
        Assert.Contains(outgoing!, m => m.Id == back!.Id);
        Assert.DoesNotContain(outgoing!, m => m.Id == forward!.Id);
        Assert.All(incoming!, m => Assert.Equal(0, m.ReceivedCount));
    }

    [Fact]
    public async Task GetManifests_InvalidDirection_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "direction-400");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/manifests?direction=sideways");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("MANIFEST_VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
    }
}
