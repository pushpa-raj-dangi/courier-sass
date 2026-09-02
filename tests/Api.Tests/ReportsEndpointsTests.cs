using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Courier.Api.Tests;

public class ReportsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid ProvinceId = GeographyIds.ProvinceId("Bagmati");
    private static readonly Guid DistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    private static readonly Guid LocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");

    public ReportsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, Guid BranchId)> RegisterOrgWithBranchAsync(string label)
    {
        var client = _factory.CreateClient();
        // Deviation from brief: the brief's `$"{label}-{Guid.NewGuid():N}"[..12]` truncates away
        // all (or nearly all) of the random suffix once `label` reaches ~11+ chars — several
        // labels below are longer than that, so every run produced the exact same slug/email and
        // collided with rows left over from a prior test run against the real, persistent
        // Postgres instance this project uses (no DB reset between runs). Matches this codebase's
        // established convention elsewhere (see BranchesEndpointsTests.cs) of slicing only the
        // GUID portion, keeping the label intact and the random suffix always 8 hex chars.
        var suffix = $"{label}-{Guid.NewGuid().ToString("N")[..8]}";
        var registerRequest = new RegisterOrganizationRequest(
            $"Reports Org {suffix}", $"reports-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResponse.EnsureSuccessStatusCode();
        var auth = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var branchRequest = new
        {
            Code = "HUB1", Name = "Hub One", Type = 1 /* Hub */, ProvinceId, DistrictId, LocalLevelId,
            WardNumber = 1, AddressLine = "Test Address", Phone = (string?)null, OperatingHours = (string?)null,
            Latitude = (decimal?)null, Longitude = (decimal?)null, ParentBranchId = (Guid?)null
        };
        var branchResponse = await client.PostAsJsonAsync("/api/v1/branches", branchRequest);
        branchResponse.EnsureSuccessStatusCode();
        var branch = await branchResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var branchId = branch.GetProperty("id").GetGuid();

        return (client, branchId);
    }

    [Fact]
    public async Task OrgDashboard_OwnerCaller_ReturnsOk()
    {
        var (client, _) = await RegisterOrgWithBranchAsync("org-dash");

        var response = await client.GetAsync("/api/v1/reports/dashboard/organization");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BranchDashboard_AccessibleBranch_ReturnsOk()
    {
        var (client, branchId) = await RegisterOrgWithBranchAsync("branch-dash");

        var response = await client.GetAsync($"/api/v1/reports/dashboard/branch/{branchId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BranchDashboard_BranchFromAnotherOrganization_ReturnsReportsValidationFailed()
    {
        var (_, otherOrgBranchId) = await RegisterOrgWithBranchAsync("branch-dash-other");
        var (client, _) = await RegisterOrgWithBranchAsync("branch-dash-caller");

        var response = await client.GetAsync($"/api/v1/reports/dashboard/branch/{otherOrgBranchId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("REPORTS_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExportShipments_ReturnsCsvWithHeaderRow()
    {
        var (client, _) = await RegisterOrgWithBranchAsync("export-shipments");

        var response = await client.GetAsync("/api/v1/reports/export/shipments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Awb,Status,OriginBranch,DestinationBranch,MerchantName,CodAmount,TotalCharge,CreatedAt,DeliveredAt", body);
    }

    [Fact]
    public async Task ExportShipments_SetsContentDispositionAttachmentWithCsvFilename()
    {
        var (client, _) = await RegisterOrgWithBranchAsync("export-ship-disp");

        var response = await client.GetAsync("/api/v1/reports/export/shipments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition!.DispositionType);
        Assert.StartsWith("shipments-", disposition.FileName?.Trim('"'));
        Assert.EndsWith(".csv", disposition.FileName?.Trim('"'));
    }

    [Fact]
    public async Task ExportShipments_BranchFromAnotherOrganization_ReturnsReportsValidationFailed()
    {
        // AllBranches-scope caller (an OrganizationOwner) must not be able to use a branchId query
        // param belonging to a different organization as the export's row filter — mirrors
        // BranchDashboard_BranchFromAnotherOrganization_ReturnsReportsValidationFailed, but for
        // ReportExportService.ResolveBranchScopeAsync instead of BranchDashboardService.GetAsync.
        var (_, otherOrgBranchId) = await RegisterOrgWithBranchAsync("export-ship-other");
        var (client, _) = await RegisterOrgWithBranchAsync("export-ship-caller");

        var response = await client.GetAsync($"/api/v1/reports/export/shipments?branchId={otherOrgBranchId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("REPORTS_VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExportCodSummary_ReturnsCsvWithHeaderRow()
    {
        var (client, _) = await RegisterOrgWithBranchAsync("export-cod");

        var response = await client.GetAsync("/api/v1/reports/export/cod-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Awb,MerchantName,BranchName,ExpectedAmount,CollectedAmount,Status,CollectedAt", body);
    }

    [Fact]
    public async Task ExportCodSummary_SetsContentDispositionAttachmentWithCsvFilename()
    {
        var (client, _) = await RegisterOrgWithBranchAsync("export-cod-disp");

        var response = await client.GetAsync("/api/v1/reports/export/cod-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition!.DispositionType);
        Assert.StartsWith("cod-summary-", disposition.FileName?.Trim('"'));
        Assert.EndsWith(".csv", disposition.FileName?.Trim('"'));
    }

    [Fact]
    public async Task Reports_UnauthenticatedCaller_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/reports/dashboard/organization");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
