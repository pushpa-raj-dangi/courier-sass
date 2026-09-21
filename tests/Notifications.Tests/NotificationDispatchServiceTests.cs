using Courier.Merchants.Application;
using Courier.Notifications.Application;
using Courier.Notifications.Domain;
using Courier.Notifications.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Notifications.Tests;

file class FakeMerchantContactLookupService : IMerchantContactLookupService
{
    public string? ContactEmail { get; set; }
    public Task<string?> GetContactEmailAsync(Guid organizationId, Guid merchantId, CancellationToken cancellationToken = default) => Task.FromResult(ContactEmail);
}

file class FakeOrganizationLocaleLookup : IOrganizationLocaleLookup
{
    public string Locale { get; set; } = "en";
    public Task<string> GetDefaultLocaleAsync(Guid organizationId, CancellationToken cancellationToken = default) => Task.FromResult(Locale);
}

file class RecordingEmailChannel : INotificationChannel
{
    public List<(string RecipientAddress, string Body)> Sent { get; } = new();
    public bool ShouldThrow { get; set; }
    public NotificationChannel Channel => NotificationChannel.Email;
    public Task<string> SendAsync(string recipientAddress, string body, CancellationToken cancellationToken = default)
    {
        if (ShouldThrow)
        {
            throw new NotificationDeliveryException("simulated SMTP failure");
        }
        Sent.Add((recipientAddress, body));
        return Task.FromResult("ok");
    }
}

public class NotificationDispatchServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new NotificationTemplateConfiguration());
            modelBuilder.ApplyConfiguration(new NotificationLogConfiguration());
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task DispatchEventAsync_WithResolvableRecipient_SendsRenderedDefaultTemplateAndLogsSent()
    {
        await using var dbContext = CreateContext();
        var merchantLookup = new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" };
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext, merchantLookup, new FakeOrganizationLocaleLookup(), new INotificationChannel[] { emailChannel });
        var organizationId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var payload = System.Text.Json.JsonSerializer.Serialize(new { ShipmentId = shipmentId, MerchantId = Guid.NewGuid(), Awb = "ABC123" });

        await service.DispatchEventAsync(organizationId, "shipment.created", payload);

        Assert.Single(emailChannel.Sent, s => s.RecipientAddress == "shipper@example.com" && s.Body.Contains("ABC123"));
        var log = await dbContext.Set<NotificationLog>().SingleAsync();
        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Equal("shipper@example.com", log.RecipientAddress);
    }

    [Fact]
    public async Task DispatchEventAsync_NoContactEmail_LogsNoRecipientAndSendsNothing()
    {
        await using var dbContext = CreateContext();
        var merchantLookup = new FakeMerchantContactLookupService { ContactEmail = null };
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext, merchantLookup, new FakeOrganizationLocaleLookup(), new INotificationChannel[] { emailChannel });
        var payload = System.Text.Json.JsonSerializer.Serialize(new { ShipmentId = Guid.NewGuid(), MerchantId = Guid.NewGuid(), Awb = "ABC123" });

        await service.DispatchEventAsync(Guid.NewGuid(), "shipment.created", payload);

        Assert.Empty(emailChannel.Sent);
        var log = await dbContext.Set<NotificationLog>().SingleAsync();
        Assert.Equal(NotificationStatus.NoRecipient, log.Status);
        Assert.Null(log.RecipientAddress);
    }

    [Fact]
    public async Task DispatchEventAsync_ChannelThrows_LogsFailed()
    {
        await using var dbContext = CreateContext();
        var merchantLookup = new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" };
        var emailChannel = new RecordingEmailChannel { ShouldThrow = true };
        var service = new NotificationDispatchService(dbContext, merchantLookup, new FakeOrganizationLocaleLookup(), new INotificationChannel[] { emailChannel });
        var payload = System.Text.Json.JsonSerializer.Serialize(new { ShipmentId = Guid.NewGuid(), MerchantId = Guid.NewGuid(), Awb = "ABC123" });

        await service.DispatchEventAsync(Guid.NewGuid(), "shipment.created", payload);

        var log = await dbContext.Set<NotificationLog>().SingleAsync();
        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Contains("simulated SMTP failure", log.ProviderResponse);
    }

    [Fact]
    public async Task DispatchEventAsync_OrgSpecificActiveTemplateExists_UsesItInsteadOfDefault()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        dbContext.Set<NotificationTemplate>().Add(new NotificationTemplate
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, EventType = "shipment.created",
            Channel = NotificationChannel.Email, Body = "Custom body for {Awb}", IsActive = true
        });
        await dbContext.SaveChangesAsync();
        var merchantLookup = new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" };
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext, merchantLookup, new FakeOrganizationLocaleLookup(), new INotificationChannel[] { emailChannel });
        var payload = System.Text.Json.JsonSerializer.Serialize(new { ShipmentId = Guid.NewGuid(), MerchantId = Guid.NewGuid(), Awb = "XYZ789" });

        await service.DispatchEventAsync(organizationId, "shipment.created", payload);

        Assert.Single(emailChannel.Sent, s => s.Body == "Custom body for XYZ789");
    }

    [Fact]
    public async Task DispatchEventAsync_DifferentOrganizationsTemplate_NotUsedAcrossTenants()
    {
        await using var dbContext = CreateContext();
        var otherOrgId = Guid.NewGuid();
        dbContext.Set<NotificationTemplate>().Add(new NotificationTemplate
        {
            Id = Guid.NewGuid(), OrganizationId = otherOrgId, EventType = "shipment.created",
            Channel = NotificationChannel.Email, Body = "Should never be used", IsActive = true
        });
        await dbContext.SaveChangesAsync();
        var merchantLookup = new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" };
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext, merchantLookup, new FakeOrganizationLocaleLookup(), new INotificationChannel[] { emailChannel });
        var payload = System.Text.Json.JsonSerializer.Serialize(new { ShipmentId = Guid.NewGuid(), MerchantId = Guid.NewGuid(), Awb = "ABC123" });

        await service.DispatchEventAsync(Guid.NewGuid(), "shipment.created", payload);

        Assert.Single(emailChannel.Sent, s => s.Body.Contains("ABC123") && !s.Body.Contains("Should never be used"));
    }

    [Fact]
    public async Task DispatchEventAsync_PayloadHasRecipientEmail_SendsToItDirectly_NoMerchantLookup()
    {
        await using var dbContext = CreateContext();
        var merchantLookup = new FakeMerchantContactLookupService { ContactEmail = null }; // proves this isn't consulted
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext, merchantLookup, new FakeOrganizationLocaleLookup(), new INotificationChannel[] { emailChannel });
        var payload = System.Text.Json.JsonSerializer.Serialize(new { RecipientEmail = "invitee@example.com", FullName = "New Person", OrganizationName = "Acme Courier", InviteUrl = "https://app.example.com/accept-invite?token=abc" });

        await service.DispatchEventAsync(Guid.NewGuid(), "user.invited", payload);

        Assert.Single(emailChannel.Sent, s => s.RecipientAddress == "invitee@example.com" && s.Body.Contains("Acme Courier") && s.Body.Contains("https://app.example.com/accept-invite?token=abc"));
    }

    [Fact]
    public async Task DispatchEventAsync_RecipientEmailPresent_IgnoresAbsentMerchantId()
    {
        await using var dbContext = CreateContext();
        var merchantLookup = new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" };
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext, merchantLookup, new FakeOrganizationLocaleLookup(), new INotificationChannel[] { emailChannel });
        // No MerchantId at all in this payload — must not throw NotificationValidationException.
        var payload = System.Text.Json.JsonSerializer.Serialize(new { RecipientEmail = "invitee@example.com", FullName = "New Person", OrganizationName = "Acme Courier", InviteUrl = "https://app.example.com/accept-invite?token=abc" });

        await service.DispatchEventAsync(Guid.NewGuid(), "user.invited", payload);

        Assert.Single(emailChannel.Sent, s => s.RecipientAddress == "invitee@example.com");
    }

    [Fact]
    public async Task DispatchEventAsync_NepaliTenant_SendsNepaliDefault()
    {
        await using var dbContext = CreateContext();
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext,
            new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" },
            new FakeOrganizationLocaleLookup { Locale = "ne" },
            new INotificationChannel[] { emailChannel });
        var payload = System.Text.Json.JsonSerializer.Serialize(new { MerchantId = Guid.NewGuid(), Awb = "AWB123" });

        await service.DispatchEventAsync(Guid.NewGuid(), "shipment.created", payload);

        NotificationDefaultTemplates.TryGetDefault("ne", "shipment.created", out var neBody);
        Assert.Single(emailChannel.Sent);
        Assert.Equal(neBody.Replace("{Awb}", "AWB123"), emailChannel.Sent[0].Body);
    }

    [Fact]
    public async Task DispatchEventAsync_NepaliTenantWithNepaliOverride_UsesOverride()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        dbContext.Set<NotificationTemplate>().Add(new NotificationTemplate
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, EventType = "shipment.created",
            Channel = NotificationChannel.Email, Locale = "ne", Body = "NE custom {Awb}", IsActive = true
        });
        await dbContext.SaveChangesAsync();
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext,
            new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" },
            new FakeOrganizationLocaleLookup { Locale = "ne" },
            new INotificationChannel[] { emailChannel });

        await service.DispatchEventAsync(organizationId, "shipment.created",
            System.Text.Json.JsonSerializer.Serialize(new { MerchantId = Guid.NewGuid(), Awb = "A1" }));

        Assert.Equal("NE custom A1", emailChannel.Sent.Single().Body);
    }

    [Fact]
    public async Task DispatchEventAsync_OtherOrganizationsOverride_IsNotUsed()
    {
        await using var dbContext = CreateContext();
        dbContext.Set<NotificationTemplate>().Add(new NotificationTemplate
        {
            Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), EventType = "shipment.created",
            Channel = NotificationChannel.Email, Locale = "en", Body = "LEAK {Awb}", IsActive = true
        });
        await dbContext.SaveChangesAsync();
        var emailChannel = new RecordingEmailChannel();
        var service = new NotificationDispatchService(dbContext,
            new FakeMerchantContactLookupService { ContactEmail = "shipper@example.com" },
            new FakeOrganizationLocaleLookup(),
            new INotificationChannel[] { emailChannel });

        await service.DispatchEventAsync(Guid.NewGuid(), "shipment.created",
            System.Text.Json.JsonSerializer.Serialize(new { MerchantId = Guid.NewGuid(), Awb = "A1" }));

        Assert.DoesNotContain("LEAK", emailChannel.Sent.Single().Body);
    }
}
