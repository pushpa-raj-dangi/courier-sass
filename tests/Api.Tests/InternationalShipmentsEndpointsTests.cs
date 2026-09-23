using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.International.Application;
using Courier.Merchants.Application;
using Courier.Persistence;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
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

    // Local shape for deserializing just Id/CurrentStatus from a full ShipmentResponse without
    // pulling in every field — mirrors the narrow-projection pattern other *EndpointsTests files
    // use when they only need to assert on a couple of fields from a larger response.
    private record ShipmentResponseForTest(Guid Id, string CurrentStatus);
}
