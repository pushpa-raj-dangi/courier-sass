using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Courier.Api.Tests;

// Proves a normal tenant JWT (org_id claim, no is_super_admin claim) is rejected by
// RequireSuperAdmin-gated routes and vice versa — a genuine cross-track auth test, not just a
// happy-path login test. There is no SuperAdmin-only endpoint to test against until Task 9, so
// this task's own test only covers the login/refresh round-trip itself; the cross-track
// rejection test moves to Task 9 once a real RequireSuperAdmin-gated route exists to test it on.
//
// Uses the same WebApplicationFactory<Program> fixture TenantIsolationTests.cs/
// OrganizationSuspensionTests.cs already use (there is no separate "ApiTestFactory" type in this
// project), which runs as Development (confirmed by other tests in this project depending on the
// Development-only migration block in Program.cs to have already run against the real Postgres).
public class PlatformAuthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlatformAuthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_SeededSuperAdminCorrectPassword_Succeeds()
    {
        // The bootstrap block (Program.cs) seeds superadmin@courier.local from
        // appsettings.Development.json when the test host starts with no existing SuperAdmin.
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "superadmin@courier.local", password = "dev-only-superadmin-password-change-me" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
