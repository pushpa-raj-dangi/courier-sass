using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Application;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class DeliveryZonesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly List<Guid> KathmanduWard1Areas = new()
    {
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area A"),
        GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 1, "Area B"),
    };

    public DeliveryZonesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12];
        var request = new RegisterOrganizationRequest(
            $"Zone Org {suffix}", $"zone-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task CreateThenGet_Succeeds()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "a");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones",
            new CreateDeliveryZoneRequest("Kathmandu Ward 1", KathmanduWard1Areas));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();

        var getResponse = await client.GetAsync($"/api/v1/delivery-zones/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        Assert.Equal("Kathmandu Ward 1", fetched!.Name);
        Assert.Equal(2, fetched.CoveredAreaIds.Count);
    }

    [Fact]
    public async Task CreateDeliveryZone_UnknownAreaId_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "b");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/delivery-zones",
            new CreateDeliveryZoneRequest("Bad Zone", new List<Guid> { Guid.NewGuid() }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDeliveryZone_ChangesNameAndCoverage()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "c");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var createResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones",
            new CreateDeliveryZoneRequest("Original", KathmanduWard1Areas));
        var created = await createResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();

        var ward2Areas = new List<Guid> { GeographyIds.AreaId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", 2, "Area A") };
        var updateResponse = await client.PutAsJsonAsync($"/api/v1/delivery-zones/{created!.Id}",
            new UpdateDeliveryZoneRequest("Renamed", ward2Areas));

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();
        Assert.Equal("Renamed", updated!.Name);
        Assert.Equal(ward2Areas, updated.CoveredAreaIds);
    }

    [Fact]
    public async Task GetDeliveryZone_OrgBCannotSeeOrgAsZone()
    {
        var client = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(client, "d");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var createResponse = await client.PostAsJsonAsync("/api/v1/delivery-zones",
            new CreateDeliveryZoneRequest("Org A Zone", KathmanduWard1Areas));
        var created = await createResponse.Content.ReadFromJsonAsync<DeliveryZoneResponse>();

        var orgBClient = _factory.CreateClient();
        var orgB = await RegisterOrgAsync(orgBClient, "e");
        orgBClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgB.AccessToken);

        var getResponse = await orgBClient.GetAsync($"/api/v1/delivery-zones/{created!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var listResponse = await orgBClient.GetAsync("/api/v1/delivery-zones");
        var orgBZones = await listResponse.Content.ReadFromJsonAsync<List<DeliveryZoneResponse>>();
        Assert.DoesNotContain(orgBZones!, z => z.Id == created.Id);
    }
}
