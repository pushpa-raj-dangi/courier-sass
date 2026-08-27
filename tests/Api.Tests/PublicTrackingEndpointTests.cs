using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Manifests.Application;
using Courier.Merchants.Application;
using Courier.Persistence;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Courier.Riders.Application;
using Courier.Riders.Domain;
using Courier.Shipments.Application;
using Courier.Shipments.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

// Covers Task 12: GET /track/{awb} — the genuinely anonymous public tracking endpoint (CLAUDE.md
// §24). Seeding/scan-transition helpers mirror ShipmentsEndpointsTests.cs and
// RidersEndpointsTests.cs's BookAndMoveToDestinationAsync exactly, since this endpoint needs the
// same staff-side scan flow those files already drive to move a shipment through real state.
public class PublicTrackingEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    private static readonly JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };

    public PublicTrackingEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        // ShipmentService.GenerateAwbAsync derives its AWB prefix from the first 6 alphanumeric
        // characters of the org slug — a slug that starts with a shared literal like
        // "tracking-org-" would give every org in this file the SAME AWB prefix, and since each
        // org's per-org sequence independently starts at 000001, their first shipments would all
        // collide on the exact same AWB (uniqueness is only enforced per (OrganizationId, Awb),
        // not globally — see ShipmentConfiguration.cs). PublicTrackingService intentionally has no
        // OrganizationId to filter by (it's anonymous), so an AWB collision across orgs would make
        // it return the wrong org's shipment. Using a pure random hex slug (no shared literal
        // prefix) keeps every org's AWB prefix effectively unique across this file's tests.
        var slug = Guid.NewGuid().ToString("N");
        var request = new RegisterOrganizationRequest(
            $"Tracking Org {suffix}", slug, $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
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

    private async Task<(Guid OriginBranchId, Guid DestinationBranchId, Guid ServiceTypeId)> SeedRoutableBranchesAsync(HttpClient client)
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
        merchantId, originBranchId, destinationBranchId, serviceTypeId, 0.7m, null, null, null, 500m, false,
        "Recipient One", "9800000000", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Recipient Address",
        "Sender One", "9800000001", KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "Sender Address",
        new List<CreateShipmentItemRequest> { new("Item A", 2, 500m) });

    private async Task<Guid> CreateRiderAsync(HttpClient client, Guid branchId, RiderRoleType roleType)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/v1/riders", new CreateRiderRequest(
            $"rider-{suffix}@example.com", "P@ssw0rd123!", "Test Rider", null, $"LIC-{suffix[..10]}", branchId, roleType));
        var rider = await response.Content.ReadFromJsonAsync<RiderResponse>();
        return rider!.Id;
    }

    // Walks a shipment Created -> PickupAssigned -> PickedUp -> AtOriginBranch -> Sorted ->
    // InTransit -> AtDestinationBranch via the real pickup-rider flow (Task 8) and manifest
    // scan/seal/dispatch/receive flow (Phase 7) — same sequence as
    // RidersEndpointsTests.BookAndMoveToDestinationAsync. Deliberately walks through the manifest
    // scan (Sorted) and dispatch (InTransit) steps too, not just receive-origin, because
    // AtOriginBranch, Sorted, and InTransit all map to the SAME public status
    // (PublicTrackingStatus.InTransit) — this is the real-world path that exercises consecutive
    // event collapsing, not merely the mapper's pure function.
    private async Task<ShipmentResponse> BookAndMoveToDestinationAsync(
        HttpClient client, Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, Guid pickupRiderId)
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId));
        var shipment = await createResponse.Content.ReadFromJsonAsync<ShipmentResponse>();

        await client.PostAsJsonAsync("/api/v1/riders/tasks", new AssignDeliveryTaskRequest(pickupRiderId, shipment!.Id, DeliveryTaskType.Pickup));
        await client.PostAsJsonAsync($"/api/v1/riders/{pickupRiderId}/scan/pickup", new RiderScanRequest(shipment.Awb));
        await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/receive-origin", new { });

        var manifest = await (await client.PostAsJsonAsync("/api/v1/manifests", new CreateManifestRequest(originBranchId, destinationBranchId))).Content.ReadFromJsonAsync<ManifestResponse>();
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest!.Id}/scan", new ScanShipmentRequest(shipment.Awb));
        await client.PostAsync($"/api/v1/manifests/{manifest.Id}/seal", null);
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/dispatch", new DispatchManifestRequest("BA 2 KHA 1234", null));
        await client.PostAsJsonAsync($"/api/v1/manifests/{manifest.Id}/receive-scan", new ScanShipmentRequest(shipment.Awb));

        return (await (await client.GetAsync($"/api/v1/shipments/{shipment.Awb}")).Content.ReadFromJsonAsync<ShipmentResponse>())!;
    }

    [Fact]
    public async Task GetTrack_FreshlyCreatedShipment_NoAuthHeaderAtAll_Returns200WithReceivedStatus()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "fresh");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        var merchantId = await CreateMerchantAsync(staffClient);
        var shipment = await (await staffClient.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantId, originBranchId, destinationBranchId, serviceTypeId)))
            .Content.ReadFromJsonAsync<ShipmentResponse>();

        // A genuinely anonymous client — no Authorization header, no X-Api-Key, nothing.
        var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync($"/track/{shipment!.Awb}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var tracking = JsonSerializer.Deserialize<PublicTrackingResponse>(body, CaseInsensitiveJson);
        Assert.Equal(shipment.Awb, tracking!.Awb);
        Assert.Equal(PublicTrackingStatus.Received, tracking.CurrentStatus);
        var onlyEvent = Assert.Single(tracking.Events);
        Assert.Equal(PublicTrackingStatus.Received, onlyEvent.Status);
    }

    [Fact]
    public async Task GetTrack_AfterRealScanTransitionsToDestination_StatusAdvancesAndCollapsesConsecutiveInTransitEvents()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "advance");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        var merchantId = await CreateMerchantAsync(staffClient);
        var pickupRiderId = await CreateRiderAsync(staffClient, originBranchId, RiderRoleType.PickupRider);

        var shipment = await BookAndMoveToDestinationAsync(staffClient, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId);
        Assert.Equal(Courier.Shipments.Domain.ShipmentStatus.AtDestinationBranch, shipment.CurrentStatus);

        var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync($"/track/{shipment.Awb}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var tracking = JsonSerializer.Deserialize<PublicTrackingResponse>(body, CaseInsensitiveJson);

        Assert.Equal(PublicTrackingStatus.AtDestinationBranch, tracking!.CurrentStatus);

        // Internally the shipment passed through Created, PickupAssigned, PickedUp, AtOriginBranch,
        // Sorted, InTransit (dispatch), AtDestinationBranch — 7 ShipmentEvent rows. Publicly that
        // must collapse to exactly 4 monotonic entries: Received, PickedUp, InTransit,
        // AtDestinationBranch. AtOriginBranch/Sorted/InTransit collapsing into ONE InTransit row is
        // the specific behavior under test here — a naive 1:1 mapping would show 6 rows instead.
        var expectedSequence = new[]
        {
            PublicTrackingStatus.Received,
            PublicTrackingStatus.PickedUp,
            PublicTrackingStatus.InTransit,
            PublicTrackingStatus.AtDestinationBranch
        };
        Assert.Equal(expectedSequence, tracking.Events.Select(e => e.Status));

        // Monotonic: the sequence of public statuses must never regress to an earlier index in the
        // canonical lifecycle ordering above.
        var order = expectedSequence.Select((s, i) => (s, i)).ToDictionary(x => x.s, x => x.i);
        var indices = tracking.Events.Select(e => order[e.Status]).ToList();
        Assert.Equal(indices.OrderBy(i => i), indices);
    }

    [Fact]
    public async Task GetTrack_UnknownAwb_Returns404()
    {
        var anonClient = _factory.CreateClient();

        var response = await anonClient.GetAsync($"/track/UNKNOWN-AWB-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Proves PublicTrackingService.GetByAwbAsync's fail-closed fix: AWB uniqueness is only
    // enforced per-(OrganizationId, Awb) (ShipmentConfiguration.cs), not globally, so two
    // different organizations' shipments CAN legitimately share the same Awb string (e.g. same
    // org-slug-derived 6-char prefix, same UTC day — see ShipmentService.GenerateAwbAsync). Every
    // other shipment read path in this codebase goes through the tenant query filter and never
    // notices this; /track/{awb} is the first anonymous, zero-tenant-scope path, so it must not
    // arbitrarily pick one org's row when more than one matches — it must refuse to guess.
    // The collision is forced directly via AppDbContext (mutating one real shipment's Awb to
    // match another's) rather than trying to engineer a real GenerateAwbAsync collision, which
    // would require controlling org slugs and exact timing.
    [Fact]
    public async Task GetTrack_AwbCollisionAcrossOrganizations_ReturnsNotFoundRatherThanEitherOrgsData()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "collide-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (originA, destA, serviceTypeA) = await SeedRoutableBranchesAsync(clientA);
        var merchantA = await CreateMerchantAsync(clientA);
        var shipmentA = await (await clientA.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantA, originA, destA, serviceTypeA)))
            .Content.ReadFromJsonAsync<ShipmentResponse>();

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "collide-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);
        var (originB, destB, serviceTypeB) = await SeedRoutableBranchesAsync(clientB);
        var merchantB = await CreateMerchantAsync(clientB);
        var shipmentB = await (await clientB.PostAsJsonAsync("/api/v1/shipments", ValidShipmentRequest(merchantB, originB, destB, serviceTypeB)))
            .Content.ReadFromJsonAsync<ShipmentResponse>();

        Assert.NotEqual(shipmentA!.Awb, shipmentB!.Awb);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var trackedB = await dbContext.Set<Shipment>().IgnoreQueryFilters().FirstAsync(s => s.Id == shipmentB.Id);
            trackedB.Awb = shipmentA.Awb;
            await dbContext.SaveChangesAsync();
        }

        var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync($"/track/{shipmentA.Awb}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTrack_ResponseBody_NeverContainsInternalOrFinancialFieldsAtWireLevel()
    {
        var staffClient = _factory.CreateClient();
        var org = await RegisterOrgAsync(staffClient, "excluded");
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(staffClient);
        var merchantId = await CreateMerchantAsync(staffClient);
        var pickupRiderId = await CreateRiderAsync(staffClient, originBranchId, RiderRoleType.PickupRider);

        // Use the full scan-driven shipment (real BranchId/UserId/RiderId on its ShipmentEvent
        // rows, and a nonzero CodAmount/DeclaredValue/DeliveryCharge on the Shipment itself) so
        // this test is a genuine check that the service never leaks those columns onto the wire —
        // not a vacuous pass because the fields all happened to be null/zero anyway.
        var shipment = await BookAndMoveToDestinationAsync(staffClient, merchantId, originBranchId, destinationBranchId, serviceTypeId, pickupRiderId);
        Assert.True(shipment.CodAmount > 0);
        Assert.True(shipment.DeclaredValue > 0);
        Assert.True(shipment.DeliveryCharge > 0);

        var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync($"/track/{shipment.Awb}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var lowerBody = body.ToLowerInvariant();

        string[] forbiddenFields =
        {
            "branchid", "userid", "riderid", "latitude", "longitude",
            "notes", "metadata", "codamount", "deliverycharge", "declaredvalue"
        };
        foreach (var field in forbiddenFields)
        {
            Assert.DoesNotContain(field, lowerBody);
        }
    }

    // Sets a fixed, test-unique RemoteIpAddress before IpRateLimitFilter runs, purely so this
    // test's Redis rate-limit bucket (partitioned by IP, per Task 6's IpRateLimitFilter) can never
    // collide with any other test in this class or process — TestServer leaves
    // Connection.RemoteIpAddress unset by default, which would otherwise put every anonymous
    // request in this whole file into the same "ip:unknown" bucket. This does not touch
    // IpRateLimitFilter or add any X-Forwarded-For handling to production code; it only fakes the
    // raw socket-level address the filter already trusts, the same way a real client on a distinct
    // IP would land in its own bucket.
    private sealed class FixedRemoteIpStartupFilter : IStartupFilter
    {
        private readonly System.Net.IPAddress _ip;
        public FixedRemoteIpStartupFilter(System.Net.IPAddress ip) => _ip = ip;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = _ip;
                await nextMiddleware();
            });
            next(app);
        };
    }

    private WebApplicationFactory<Program> CreateFactoryWithLowLimitAndFixedIp(int limit, System.Net.IPAddress ip) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:PublicTrackingRequestsPerMinute"] = limit.ToString()
            }));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new FixedRemoteIpStartupFilter(ip)));
        });

    [Fact]
    public async Task GetTrack_ExceedsConfiguredIpLimit_ThirdRequestReturns429WithRetryAfter()
    {
        var testIp = System.Net.IPAddress.Parse($"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}");
        using var scopedFactory = CreateFactoryWithLowLimitAndFixedIp(2, testIp);
        var client = scopedFactory.CreateClient();
        var awb = $"RATE-LIMIT-{Guid.NewGuid():N}";

        var first = await client.GetAsync($"/track/{awb}");
        var second = await client.GetAsync($"/track/{awb}");
        var third = await client.GetAsync($"/track/{awb}");

        // The AWB doesn't exist, so the first two are 404s — the rate limiter runs regardless of
        // whether the handler finds a shipment, so this still proves the limiter trips on request 3.
        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);

        Assert.True(third.Headers.TryGetValues("Retry-After", out var retryAfterValues), "Expected a Retry-After header on the 429 response.");
        var retryAfter = Assert.Single(retryAfterValues!);
        Assert.True(int.TryParse(retryAfter, out var retryAfterSeconds), $"Retry-After value '{retryAfter}' was not numeric.");
        Assert.True(retryAfterSeconds > 0);

        var body = await third.Content.ReadAsStringAsync();
        Assert.Contains("RATE_LIMIT_EXCEEDED", body);
    }
}
