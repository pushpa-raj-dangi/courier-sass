using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

public class TenantIsolationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TenantIsolationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}"[..12];
        var request = new RegisterOrganizationRequest(
            $"Org {suffix}", $"org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    [Fact]
    public async Task GetUsers_OrgAOwner_NeverSeesOrgBUsers()
    {
        var client = _factory.CreateClient();
        var orgA = await RegisterOrgAsync(client, "a");
        var orgB = await RegisterOrgAsync(client, "b");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgA.AccessToken);
        var response = await client.GetAsync("/api/v1/users");
        response.EnsureSuccessStatusCode();

        var users = await response.Content.ReadFromJsonAsync<List<UserListItem>>();
        Assert.Single(users!);
        Assert.Equal(orgA.UserId, users![0].Id);
        Assert.DoesNotContain(users, u => u.Id == orgB.UserId);
    }
}

file record UserListItem(Guid Id, string Email, string FullName, bool IsActive);
