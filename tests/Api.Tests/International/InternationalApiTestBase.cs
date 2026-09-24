using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Courier.Branches.Application;
using Courier.Branches.Domain;
using Courier.Geography.Domain;
using Courier.Geography.Infrastructure;
using Courier.Identity.Application;
using Courier.Identity.Domain;
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

namespace Courier.Api.Tests.International;

// Shared helpers for every international API test class (I3 endpoints, I4 carrier layer). Derived
// classes still declare IClassFixture<WebApplicationFactory<Program>> themselves and pass the
// factory (or a WithWebHostBuilder-derived one) to this constructor.
public abstract class InternationalApiTestBase
{
    protected InternationalApiTestBase(WebApplicationFactory<Program> factory)
    {
        Factory = factory;
    }

    protected WebApplicationFactory<Program> Factory { get; }

    protected static readonly Guid KathmanduProvinceId = GeographyIds.ProvinceId("Bagmati");
    protected static readonly Guid KathmanduDistrictId = GeographyIds.DistrictId("Bagmati", "Kathmandu");
    protected static readonly Guid KathmanduLocalLevelId = GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City");
    protected const int KathmanduWardNumber = 1;

    protected async Task<AuthResult> RegisterOrgAsync(HttpClient client, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var request = new RegisterOrganizationRequest(
            $"Intl Org {suffix}", $"intl-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    protected static CreateBranchRequest ValidBranchRequest(string code) => new(
        code, $"Branch {code}", BranchType.Hub, KathmanduProvinceId, KathmanduDistrictId, KathmanduLocalLevelId, KathmanduWardNumber,
        "New Road, Kathmandu", null, null, null, null, null, null);

    protected async Task<Guid> CreateBranchAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest(code));
        response.EnsureSuccessStatusCode();
        var branch = await response.Content.ReadFromJsonAsync<BranchResponse>();
        return branch!.Id;
    }

    protected async Task<Guid> CreateMerchantAsync(HttpClient staffClient)
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
    protected async Task MarkCountryServicedAsync(string isoCode)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var country = await dbContext.Set<Country>().IgnoreQueryFilters().SingleAsync(c => c.IsoCode == isoCode);
        country.IsServiced = true;
        await dbContext.SaveChangesAsync();
    }

    protected async Task<Guid> GetStandardServiceTypeIdAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/service-types");
        var serviceTypes = await response.Content.ReadFromJsonAsync<List<ServiceTypeResponse>>();
        return serviceTypes!.Single(s => s.Code == ServiceTypeCode.Standard).Id;
    }

    protected static object OutboundBody(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, string recipientCountryCode) => new
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

    protected static object InboundBody(Guid merchantId, Guid originBranchId, Guid destinationBranchId, Guid serviceTypeId, string senderCountryCode) => new
    {
        merchantId,
        direction = "Inbound",
        originBranchId,
        destinationBranchId,
        serviceTypeId,
        weightKg = 1m,
        lengthCm = (decimal?)null,
        widthCm = (decimal?)null,
        heightCm = (decimal?)null,
        recipientName = "Local Recipient",
        recipientPhone = "9811111111",
        recipientProvinceId = KathmanduProvinceId,
        recipientDistrictId = KathmanduDistrictId,
        recipientLocalLevelId = KathmanduLocalLevelId,
        recipientWardNumber = KathmanduWardNumber,
        recipientAddressLine = "Ward 1 Line",
        recipientCity = (string?)null,
        recipientRegion = (string?)null,
        recipientPostalCode = (string?)null,
        recipientCountryCode = (string?)null,
        senderName = "Foreign Sender",
        senderPhone = "9998887776",
        senderProvinceId = (Guid?)null,
        senderDistrictId = (Guid?)null,
        senderLocalLevelId = (Guid?)null,
        senderWardNumber = (int?)null,
        senderAddressLine = "221B Baker Street",
        senderCity = "London",
        senderRegion = (string?)null,
        senderPostalCode = "NW16XE",
        senderCountryCode,
        provisionalTotalChargeAmount = 5000m,
        provisionalTotalChargeCurrency = "NPR",
        items = new[] { new { description = "Sample goods", quantity = 1, unitValue = 5000m } }
    };

    protected static object CustomsBody(Guid itemId, string currency = "NPR", string contents = "Sample goods for testing") => new
    {
        contentsDescription = contents,
        countryOfOrigin = "NP",
        purpose = "Sample",
        incoterm = "Ddu",
        exportLicenceReference = (string?)null,
        items = new[] { new { shipmentItemId = itemId, hsCode = "HS1234", customsValueAmount = 5000m, customsValueCurrency = currency } }
    };

    public sealed record OrgContext(HttpClient Client, AuthResult Auth, Guid BranchId, Guid MerchantId, Guid ServiceTypeId);

    protected async Task<OrgContext> SetUpOrgAsync(string label)
    {
        var client = Factory.CreateClient();
        var auth = await RegisterOrgAsync(client, label);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        await MarkCountryServicedAsync("GB");
        var branchId = await CreateBranchAsync(client, $"F-{Guid.NewGuid():N}"[..10]);
        var merchantId = await CreateMerchantAsync(client);
        var serviceTypeId = await GetStandardServiceTypeIdAsync(client);
        return new OrgContext(client, auth, branchId, merchantId, serviceTypeId);
    }

    protected async Task<InternationalShipmentDetailResponse> CreateOutboundAsync(OrgContext org, Guid? originBranchId = null, Guid? destinationBranchId = null)
    {
        var response = await org.Client.PostAsJsonAsync("/api/v1/international/shipments",
            OutboundBody(org.MerchantId, originBranchId ?? org.BranchId, destinationBranchId ?? org.BranchId, org.ServiceTypeId, "GB"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InternationalShipmentDetailResponse>())!;
    }

    protected async Task<InternationalShipmentDetailResponse> CreateInboundAsync(OrgContext org, Guid? originBranchId = null, Guid? destinationBranchId = null)
    {
        var response = await org.Client.PostAsJsonAsync("/api/v1/international/shipments",
            InboundBody(org.MerchantId, originBranchId ?? org.BranchId, destinationBranchId ?? org.BranchId, org.ServiceTypeId, "GB"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InternationalShipmentDetailResponse>())!;
    }

    // Same shortcut the happy-path test above uses: the physical pickup/receive/sort/manifest scans
    // that would normally move a shipment to Sorted/AtDestinationBranch are other modules' routes
    // (covered by their own suites), so the shipment row is positioned directly.
    protected async Task SetShipmentStateAsync(Guid shipmentId, ShipmentStatus status, Guid? currentBranchId)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var shipment = await dbContext.Set<Shipment>().IgnoreQueryFilters().SingleAsync(s => s.Id == shipmentId);
        shipment.CurrentStatus = status;
        shipment.CurrentBranchId = currentBranchId;
        await dbContext.SaveChangesAsync();
    }

    // Same direct-seeding approach as BranchAuthorizationEndpointTests.SeedScopedUserTokenAsync
    // (no invite-user-with-grants endpoint to drive this through): one custom role holding exactly
    // permissionKeys, assigned with SpecificBranches scope to grantedBranchIds — or AllBranches
    // when grantedBranchIds is null.
    protected async Task<HttpClient> SeedUserClientAsync(AuthResult owner, string[] permissionKeys, params Guid[]? grantedBranchIds)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var permissions = await dbContext.Set<Permission>().Where(p => permissionKeys.Contains(p.Key)).ToListAsync();
        Assert.Equal(permissionKeys.Length, permissions.Count);
        var role = new Role { Id = Guid.NewGuid(), OrganizationId = owner.OrganizationId, Name = $"IntlTestRole-{Guid.NewGuid():N}", IsSystemDefault = false };
        foreach (var permission in permissions)
        {
            role.Permissions.Add(permission);
        }
        dbContext.Set<Role>().Add(role);

        var userId = Guid.NewGuid();
        var email = $"intl-user-{userId:N}@example.com";
        dbContext.Set<User>().Add(new User { Id = userId, OrganizationId = owner.OrganizationId, Email = email, PasswordHash = "not-used-in-this-test", FullName = "Intl Test User" });
        dbContext.Set<UserRole>().Add(new UserRole
        {
            Id = Guid.NewGuid(), UserId = userId, RoleId = role.Id, OrganizationId = owner.OrganizationId,
            BranchScope = grantedBranchIds is null ? BranchScope.AllBranches : BranchScope.SpecificBranches,
            AssignedAt = DateTimeOffset.UtcNow, AssignedByUserId = owner.UserId
        });
        foreach (var branchId in grantedBranchIds ?? Array.Empty<Guid>())
        {
            dbContext.Set<UserBranch>().Add(new UserBranch
            {
                Id = Guid.NewGuid(), UserId = userId, BranchId = branchId, OrganizationId = owner.OrganizationId,
                GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = owner.UserId
            });
        }
        await dbContext.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtTokenService.IssueAccessToken(userId, owner.OrganizationId, email));
        return client;
    }

    // Outbound shipment at the org's single branch, customs completed and prepared — the state a
    // carrier booking starts from. (Sorted is positioned directly, as in the I3 tests.)
    protected async Task<InternationalShipmentDetailResponse> PrepareOutboundAtCustomsPreparedAsync(OrgContext org)
    {
        var created = await CreateOutboundAsync(org);
        await SetShipmentStateAsync(created.Shipment.Id, ShipmentStatus.Sorted, org.BranchId);
        var customs = await org.Client.PatchAsJsonAsync($"/api/v1/international/shipments/{created.Shipment.Id}/customs",
            CustomsBody(created.Shipment.Items.Single().Id));
        Assert.Equal(HttpStatusCode.OK, customs.StatusCode);
        var prepare = await org.Client.PostAsync($"/api/v1/international/shipments/{created.Shipment.Id}/prepare-customs", null);
        Assert.Equal(HttpStatusCode.OK, prepare.StatusCode);
        return created;
    }
}
