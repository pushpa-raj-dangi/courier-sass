using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Billing.Application;
using Courier.Billing.Domain;
using Courier.Identity.Application;
using Courier.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Courier.Api.Tests;

public class TrialEnforcementMiddlewareTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TrialEnforcementMiddlewareTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new RegisterOrganizationRequest(
            $"{label} Org {suffix}", $"{label}-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    private async Task ExpireTrialAsync(Guid organizationId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await dbContext.Set<Subscription>().IgnoreQueryFilters().SingleAsync(s => s.OrganizationId == organizationId);
        subscription.Status = SubscriptionStatus.TrialExpired;
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task PostBranch_TrialExpired_Returns402()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "trialblock");
        await ExpireTrialAsync(org.OrganizationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.PostAsJsonAsync("/api/v1/branches", new { });

        Assert.Equal((HttpStatusCode)402, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("TRIAL_EXPIRED", body);
    }

    [Fact]
    public async Task GetRequest_TrialExpired_StillSucceeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "trialblockread");
        await ExpireTrialAsync(org.OrganizationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/branches");

        Assert.NotEqual((HttpStatusCode)402, response.StatusCode);
    }

    [Fact]
    public async Task PostBillingPlan_TrialExpired_IsExemptAndSucceeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "trialblockexempt");
        await ExpireTrialAsync(org.OrganizationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        Guid planId;
        using (var scope = _factory.Services.CreateScope())
        {
            var planService = scope.ServiceProvider.GetRequiredService<PlanService>();
            var plan = await planService.CreatePlanAsync(new CreatePlanRequest("Escape", $"escape-{Guid.NewGuid():N}", 500m, null, null, null, false), Guid.NewGuid());
            planId = plan.Id;
        }

        var response = await client.PostAsJsonAsync("/api/v1/billing/plan", new { PlanId = planId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostBranch_StillWithinTrial_Succeeds()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "trialactive");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);

        var response = await client.GetAsync("/api/v1/branches");

        Assert.NotEqual((HttpStatusCode)402, response.StatusCode);
    }
}
