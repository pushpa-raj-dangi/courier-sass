using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class BranchesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BranchesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"Branches Org {suffix}", $"branches-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task PostBranches_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "KTM-HUB", "Kathmandu Hub", BranchType.Hub, "New Road, Kathmandu", null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateThenGet_OwnerWithAllBranchesScope_Succeeds()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "KTM-HUB", "Kathmandu Hub", BranchType.Hub, "New Road, Kathmandu", "01-4444444", "09:00-18:00", 27.7m, 85.3m, null));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.NotNull(created);

        var getResponse = await client.GetAsync($"/api/v1/branches/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<BranchResponse>();
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal("KTM-HUB", fetched.Code);
    }

    [Fact]
    public async Task CreateBranch_DuplicateCode_Returns400()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var request = new CreateBranchRequest("DUP-01", "First", BranchType.Branch, "Somewhere", null, null, null, null, null);
        await client.PostAsJsonAsync("/api/v1/branches", request);

        var response = await client.PostAsJsonAsync("/api/v1/branches", request with { Name = "Second" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBranch_NonExistent_Returns404()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync($"/api/v1/branches/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListBranches_ReturnsCreatedBranch()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        await client.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(
            "LST-01", "List Test Branch", BranchType.Branch, "Somewhere", null, null, null, null, null));

        var response = await client.GetAsync("/api/v1/branches");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var branches = await response.Content.ReadFromJsonAsync<List<BranchResponse>>();
        Assert.Contains(branches!, b => b.Code == "LST-01");
    }
}
