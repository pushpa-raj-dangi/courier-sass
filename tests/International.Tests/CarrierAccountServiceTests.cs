using System.Security.Claims;
using Courier.International.Application;
using Courier.International.Application.Carriers;
using Courier.International.Domain;
using Courier.International.Infrastructure.Carriers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Courier.International.Tests;

public class CarrierAccountServiceTests
{
    private sealed record Harness(CarrierTestDbContext DbContext, Guid OrganizationId, CarrierAccountService Service, RecordingAuditLogger Audit, ICarrierSecretProtector Protector);

    private static Harness CreateHarness()
    {
        var dbContext = CarrierTestData.NewContext();
        var organizationId = Guid.NewGuid();
        var audit = new RecordingAuditLogger();
        var protector = new DataProtectionCarrierSecretProtector(new EphemeralDataProtectionProvider());
        var registry = new CarrierProviderRegistry(new ICarrierProvider[] { new ManualCarrierProvider(), new TestCarrierProvider() });
        var service = new CarrierAccountService(dbContext, new FixedTenantProvider(organizationId), audit, registry, protector);
        return new Harness(dbContext, organizationId, service, audit, protector);
    }

    private static ClaimsPrincipal ActingUser() => new(new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test"));

    [Fact]
    public async Task CreateAsync_WithCredentialsAndWebhookSecret_EncryptsBothAndReturnsSecretOnce()
    {
        var h = CreateHarness();

        var result = await h.Service.CreateAsync(new CreateCarrierAccountRequest("testcarrier", "  Test Carrier  ",
            new Dictionary<string, string> { ["apiKey"] = "sk_live_12345" }, PollingEnabled: true, GenerateWebhookSecret: true), ActingUser());

        Assert.Equal("TESTCARRIER", result.Account.CarrierCode);
        Assert.Equal("Test Carrier", result.Account.DisplayName);
        Assert.True(result.Account.HasCredentials);
        Assert.True(result.Account.HasWebhookSecret);
        Assert.False(string.IsNullOrWhiteSpace(result.WebhookSecret));
        Assert.StartsWith("/api/v1/carrier-webhooks/", result.Account.WebhookPath);

        var stored = await h.DbContext.Set<CarrierAccount>().SingleAsync();
        Assert.Equal(h.OrganizationId, stored.OrganizationId);
        Assert.DoesNotContain("sk_live_12345", stored.EncryptedCredentials);
        Assert.Equal("sk_live_12345", h.Protector.UnprotectCredentials(stored.EncryptedCredentials).Values["apiKey"]);
        Assert.Equal(result.WebhookSecret, h.Protector.UnprotectWebhookSecret(stored.EncryptedWebhookSecret!));
        Assert.Contains(h.Audit.Entries, e => e.Action == "carrier_account.created" && !(e.After ?? "").Contains("sk_live_12345"));
    }

    [Fact]
    public async Task CreateAsync_ManualCarrier_HasNoSecret()
    {
        var h = CreateHarness();

        var result = await h.Service.CreateAsync(new CreateCarrierAccountRequest("MANUAL", "Manual", null, false, false), ActingUser());

        Assert.Null(result.WebhookSecret);
        Assert.False(result.Account.HasCredentials);
        Assert.False(result.Account.HasWebhookSecret);
    }

    [Theory]
    [InlineData("DHL_EXPRESS", "Name", false, false)]  // not registered
    [InlineData("MANUAL", "", false, false)]           // blank name
    [InlineData("MANUAL", "Manual", true, false)]      // manual cannot poll
    [InlineData("MANUAL", "Manual", false, true)]      // manual has no webhooks
    public async Task CreateAsync_Invalid_ThrowsAccountValidation(string code, string name, bool polling, bool webhookSecret)
    {
        var h = CreateHarness();

        await Assert.ThrowsAsync<CarrierAccountValidationException>(() =>
            h.Service.CreateAsync(new CreateCarrierAccountRequest(code, name, null, polling, webhookSecret), ActingUser()));
        Assert.Empty(h.DbContext.Set<CarrierAccount>());
    }

    [Fact]
    public async Task CreateAsync_BlankCredentialValue_ThrowsAccountValidation()
    {
        var h = CreateHarness();

        await Assert.ThrowsAsync<CarrierAccountValidationException>(() =>
            h.Service.CreateAsync(new CreateCarrierAccountRequest("TESTCARRIER", "Test", new Dictionary<string, string> { ["apiKey"] = " " }, false, false), ActingUser()));
    }

    [Fact]
    public async Task UpdateAsync_DeactivatesAndRenames()
    {
        var h = CreateHarness();
        var created = await h.Service.CreateAsync(new CreateCarrierAccountRequest("MANUAL", "Manual", null, false, false), ActingUser());

        var updated = await h.Service.UpdateAsync(created.Account.Id, new UpdateCarrierAccountRequest("Manual (old)", IsActive: false, PollingEnabled: false), ActingUser());

        Assert.False(updated!.IsActive);
        Assert.Equal("Manual (old)", updated.DisplayName);
    }

    [Fact]
    public async Task RotateWebhookSecretAsync_ReplacesTheSecret()
    {
        var h = CreateHarness();
        var created = await h.Service.CreateAsync(new CreateCarrierAccountRequest("TESTCARRIER", "Test", null, false, true), ActingUser());

        var rotated = await h.Service.RotateWebhookSecretAsync(created.Account.Id, ActingUser());

        Assert.NotEqual(created.WebhookSecret, rotated!.WebhookSecret);
        var stored = await h.DbContext.Set<CarrierAccount>().SingleAsync();
        Assert.Equal(rotated.WebhookSecret, h.Protector.UnprotectWebhookSecret(stored.EncryptedWebhookSecret!));
    }

    [Fact]
    public async Task RotateWebhookSecretAsync_ManualCarrier_ThrowsAccountValidation()
    {
        var h = CreateHarness();
        var created = await h.Service.CreateAsync(new CreateCarrierAccountRequest("MANUAL", "Manual", null, false, false), ActingUser());

        await Assert.ThrowsAsync<CarrierAccountValidationException>(() => h.Service.RotateWebhookSecretAsync(created.Account.Id, ActingUser()));
    }

    [Fact]
    public async Task UnknownAccount_ReturnsNull()
    {
        var h = CreateHarness();

        Assert.Null(await h.Service.GetAsync(Guid.NewGuid()));
        Assert.Null(await h.Service.UpdateAsync(Guid.NewGuid(), new UpdateCarrierAccountRequest("X", true, false), ActingUser()));
    }
}
