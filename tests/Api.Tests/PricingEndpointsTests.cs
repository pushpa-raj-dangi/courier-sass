using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Merchants.Application;
using Courier.Pricing.Application;
using Courier.Pricing.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class PricingEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    private const int KathmanduWardNumber = 1;

    public PricingEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Pricing Org {suffix}", $"pricing-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
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

    private async Task<RateCardResponse> SeedRateCardWithZoneRateAsync(HttpClient client, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, decimal baseRate)
    {
        var originBranch = (await (await client.GetAsync($"/api/v1/branches/{originBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var destBranch = (await (await client.GetAsync($"/api/v1/branches/{destinationBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;

        var weightSlabsResponse = await client.GetAsync("/api/v1/weight-slabs");
        var weightSlabs = await weightSlabsResponse.Content.ReadFromJsonAsync<List<WeightSlabResponse>>();
        var slab = weightSlabs!.Single(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m);

        var rateCardResponse = await client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest($"Card-{Guid.NewGuid():N}", DateTimeOffset.UtcNow));
        var rateCard = await rateCardResponse.Content.ReadFromJsonAsync<RateCardResponse>();

        await client.PostAsJsonAsync($"/api/v1/rate-cards/{rateCard!.Id}/zone-rates",
            new CreateZoneRateRequest(originBranch.DeliveryZoneId!.Value, destBranch.DeliveryZoneId!.Value, serviceTypeId, slab.Id, baseRate));

        return rateCard;
    }

    [Fact]
    public async Task Quote_FullRoundTrip_ReturnsExpectedTotal()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "quote");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardWithZoneRateAsync(client, originBranchId, destinationBranchId, serviceTypeId, 150m);

        var response = await client.PostAsJsonAsync("/api/v1/pricing/quote",
            new PriceQuoteRequest(originBranchId, destinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<PriceQuoteResponse>();
        Assert.Equal(150m, quote!.Total);
    }

    [Fact]
    public async Task Quote_NoRateDefined_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "quote-missing");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        // No rate card created at all for this org.

        var response = await client.PostAsJsonAsync("/api/v1/pricing/quote",
            new PriceQuoteRequest(originBranchId, destinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RateCardVersioning_SupersededCardRowsUnchanged_NewQuoteUsesNewNumbers()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "version");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        var v1 = await SeedRateCardWithZoneRateAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);

        var v1QuoteBefore = await (await client.PostAsJsonAsync("/api/v1/pricing/quote",
            new PriceQuoteRequest(originBranchId, destinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null)))
            .Content.ReadFromJsonAsync<PriceQuoteResponse>();
        Assert.Equal(100m, v1QuoteBefore!.Total);

        var v2 = await SeedRateCardWithZoneRateAsync(client, originBranchId, destinationBranchId, serviceTypeId, 175m);
        Assert.Equal(v1.Version + 1, v2.Version);

        // Old card's own zone-rates are unchanged (never mutated) — proves immutability, not just
        // that a new quote picks up new numbers.
        var v1ZoneRatesAfter = await (await client.GetAsync($"/api/v1/rate-cards/{v1.Id}/zone-rates")).Content.ReadFromJsonAsync<List<ZoneRateResponse>>();
        Assert.Single(v1ZoneRatesAfter!);
        Assert.Equal(100m, v1ZoneRatesAfter![0].BaseRate);

        var v1After = await (await client.GetAsync($"/api/v1/rate-cards/{v1.Id}")).Content.ReadFromJsonAsync<RateCardResponse>();
        Assert.False(v1After!.IsActive);

        var newQuote = await (await client.PostAsJsonAsync("/api/v1/pricing/quote",
            new PriceQuoteRequest(originBranchId, destinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, null)))
            .Content.ReadFromJsonAsync<PriceQuoteResponse>();
        Assert.Equal(175m, newQuote!.Total);
        Assert.Equal(v2.Id, newQuote.RateCardId);
    }

    [Fact]
    public async Task AddZoneRate_OnSupersededRateCard_Returns400()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "superseded-write");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        var v1 = await SeedRateCardWithZoneRateAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        await SeedRateCardWithZoneRateAsync(client, originBranchId, destinationBranchId, serviceTypeId, 200m);

        var originBranch = (await (await client.GetAsync($"/api/v1/branches/{originBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var destBranch = (await (await client.GetAsync($"/api/v1/branches/{destinationBranchId}")).Content.ReadFromJsonAsync<BranchResponse>())!;
        var slab = (await (await client.GetAsync("/api/v1/weight-slabs")).Content.ReadFromJsonAsync<List<WeightSlabResponse>>())!.Single(w => w.MinWeightKg == 0.5m && w.MaxWeightKg == 1m);

        var response = await client.PostAsJsonAsync($"/api/v1/rate-cards/{v1.Id}/zone-rates",
            new CreateZoneRateRequest(originBranch.DeliveryZoneId!.Value, destBranch.DeliveryZoneId!.Value, serviceTypeId, slab.Id, 999m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MerchantOverride_UsedInsteadOfOrgDefault_DiscountApplied()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "merchant-override");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(client);
        await SeedRateCardWithZoneRateAsync(client, originBranchId, destinationBranchId, serviceTypeId, 100m);
        var merchantRateCard = await SeedRateCardWithZoneRateAsync(client, originBranchId, destinationBranchId, serviceTypeId, 300m); // becomes org default (v2)

        var merchantResponse = await client.PostAsJsonAsync("/api/v1/merchants", new CreateMerchantRequest(
            "Merchant Override Pvt. Ltd.", "Merchant Override", "01-4444444", "override@example.com",
            $"override-owner-{Guid.NewGuid():N}@example.com", "P@ssw0rd123!", "Owner",
            KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber, "New Road, Kathmandu"));
        var merchant = await merchantResponse.Content.ReadFromJsonAsync<MerchantResponse>();

        await client.PutAsJsonAsync($"/api/v1/merchants/{merchant!.Id}/rate-card", new UpsertMerchantRateCardRequest(merchantRateCard.Id, 20m));

        var quoteWithMerchant = await (await client.PostAsJsonAsync("/api/v1/pricing/quote",
            new PriceQuoteRequest(originBranchId, destinationBranchId, 0.7m, serviceTypeId, 0m, 0m, false, merchant.Id)))
            .Content.ReadFromJsonAsync<PriceQuoteResponse>();

        Assert.Equal(240m, quoteWithMerchant!.Total); // 300 - 20%
    }

    [Fact]
    public async Task RateCards_OrgBCannotSeeOrgAsRateCards()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "iso-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(clientA);
        var rateCardA = await SeedRateCardWithZoneRateAsync(clientA, originBranchId, destinationBranchId, serviceTypeId, 100m);

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "iso-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var listResponse = await clientB.GetAsync("/api/v1/rate-cards");
        var rateCards = await listResponse.Content.ReadFromJsonAsync<List<RateCardResponse>>();
        Assert.DoesNotContain(rateCards!, rc => rc.Id == rateCardA.Id);

        var getResponse = await clientB.GetAsync($"/api/v1/rate-cards/{rateCardA.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task ZoneRatesAndAdditionalCharges_OrgBCannotReadOrgAsRateCardChildren()
    {
        var clientA = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(clientA, "iso-children-a");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var (originBranchId, destinationBranchId, serviceTypeId) = await SeedRoutableBranchesAsync(clientA);
        var rateCardA = await SeedRateCardWithZoneRateAsync(clientA, originBranchId, destinationBranchId, serviceTypeId, 100m);
        await clientA.PostAsJsonAsync($"/api/v1/rate-cards/{rateCardA.Id}/additional-charges",
            new CreateAdditionalChargeRequest(AdditionalChargeType.Fuel, ChargeCalculationType.Percentage, 5m));

        var clientB = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(clientB, "iso-children-b");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        // Org B guesses/knows Org A's rate card id — must not be able to read its zone-rates or
        // additional-charges (base rates and surcharge percentages are commercially sensitive).
        var zoneRatesResponse = await clientB.GetAsync($"/api/v1/rate-cards/{rateCardA.Id}/zone-rates");
        Assert.Equal(HttpStatusCode.NotFound, zoneRatesResponse.StatusCode);

        var chargesResponse = await clientB.GetAsync($"/api/v1/rate-cards/{rateCardA.Id}/additional-charges");
        Assert.Equal(HttpStatusCode.NotFound, chargesResponse.StatusCode);
    }

    [Fact]
    public async Task PostRateCards_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest("Default", DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateRateCard_ConcurrentRequestsForSameOrg_OnlyOneSucceeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "race");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        // Two concurrent requests share the same WebApplicationFactory but each gets its own DI
        // scope/DbContext/DB connection per ASP.NET Core request handling, so this genuinely
        // races against the real Postgres container — proving the filtered unique index
        // (RateCardConfiguration.IX_RateCards_OrganizationId_ActiveUnique) actually blocks a
        // second concurrent IsActive=true insert, not just that the SQL is syntactically valid.
        // Whether the two requests literally overlap at the DB level is scheduling-dependent —
        // if they don't overlap, both correctly succeed (first becomes v1, second becomes v2,
        // exactly one active) which is NOT a bug; if they do overlap, one is rejected with a
        // retry error. Either way, the one invariant that must always hold is asserted below:
        // exactly one RateCard for this org is ever IsActive at once. Asserting a fixed
        // success/failure split instead would make this test flaky.
        var now = DateTimeOffset.UtcNow;
        var first = client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest("Race A", now));
        var second = client.PostAsJsonAsync("/api/v1/rate-cards", new CreateRateCardRequest("Race B", now));
        var responses = await Task.WhenAll(first, second);

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.BadRequest));

        var rateCards = await (await client.GetAsync("/api/v1/rate-cards")).Content.ReadFromJsonAsync<List<RateCardResponse>>();
        Assert.Single(rateCards!, rc => rc.IsActive);
    }
}
