using Courier.Audit.Application;
using Courier.Notifications.Application;
using Courier.Notifications.Domain;
using Courier.Notifications.Infrastructure;
using Courier.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Notifications.Tests;

file class FixedTenantProvider : ICurrentTenantProvider
{
    public FixedTenantProvider(Guid organizationId) => OrganizationId = organizationId;
    public Guid? OrganizationId { get; }
}

file class FakeAuditLogger : IAuditLogger
{
    public List<AuditEntry> Entries { get; } = new();
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; }
}

public class NotificationTemplateServiceTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new NotificationTemplateConfiguration());
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
    public async Task UpsertAsync_UnknownEventType_ThrowsValidationException()
    {
        await using var dbContext = CreateContext();
        var service = new NotificationTemplateService(dbContext, new FixedTenantProvider(Guid.NewGuid()), new FakeAuditLogger());

        await Assert.ThrowsAsync<NotificationValidationException>(() =>
            service.UpsertAsync(Guid.NewGuid(), "not.a.real.event", NotificationChannel.Email, "Body", true));
    }

    [Fact]
    public async Task UpsertAsync_NoExistingRow_InsertsOneRowAndWritesAuditEntry()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var auditLogger = new FakeAuditLogger();
        var service = new NotificationTemplateService(dbContext, new FixedTenantProvider(organizationId), auditLogger);

        var result = await service.UpsertAsync(Guid.NewGuid(), "shipment.created", NotificationChannel.Email, "Custom body {Awb}", true);

        Assert.Equal("Custom body {Awb}", result.Body);
        Assert.Single(dbContext.Set<NotificationTemplate>().Where(t => t.OrganizationId == organizationId));
        Assert.Contains(auditLogger.Entries, e => e.Action == "notification_template.upserted");
    }

    [Fact]
    public async Task UpsertAsync_ExistingRowForSameEventAndChannel_UpdatesInPlaceRatherThanInserting()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = new NotificationTemplateService(dbContext, new FixedTenantProvider(organizationId), new FakeAuditLogger());
        await service.UpsertAsync(Guid.NewGuid(), "shipment.created", NotificationChannel.Email, "First body", true);

        var result = await service.UpsertAsync(Guid.NewGuid(), "shipment.created", NotificationChannel.Email, "Second body", false);

        Assert.Equal("Second body", result.Body);
        Assert.False(result.IsActive);
        Assert.Single(dbContext.Set<NotificationTemplate>().Where(t => t.OrganizationId == organizationId));
    }

    [Fact]
    public async Task UpsertAsync_ExistingInactiveRowForSameEventAndChannel_UpdatesThatRowRatherThanInsertingASecondOne()
    {
        // Proves the lookup finds an existing row REGARDLESS of IsActive (brief's explicit
        // constraint) — the previous test's existing row is always active when it's overwritten,
        // so it alone wouldn't catch a lookup that filtered on IsActive == true and therefore
        // missed an inactive existing row, inserting a duplicate instead of updating it.
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var service = new NotificationTemplateService(dbContext, new FixedTenantProvider(organizationId), new FakeAuditLogger());
        await service.UpsertAsync(Guid.NewGuid(), "shipment.created", NotificationChannel.Email, "First body", false);

        var result = await service.UpsertAsync(Guid.NewGuid(), "shipment.created", NotificationChannel.Email, "Second body", true);

        Assert.Equal("Second body", result.Body);
        Assert.True(result.IsActive);
        Assert.Single(dbContext.Set<NotificationTemplate>().Where(t => t.OrganizationId == organizationId));
    }

    [Fact]
    public async Task ListAsync_ScopesToCurrentOrganization()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        dbContext.Set<NotificationTemplate>().Add(new NotificationTemplate { Id = Guid.NewGuid(), OrganizationId = otherOrganizationId, EventType = "shipment.created", Channel = NotificationChannel.Email, Body = "Other org's body", IsActive = true });
        await dbContext.SaveChangesAsync();
        var service = new NotificationTemplateService(dbContext, new FixedTenantProvider(organizationId), new FakeAuditLogger());
        await service.UpsertAsync(Guid.NewGuid(), "shipment.delivered", NotificationChannel.Email, "This org's body", true);

        var result = await service.ListAsync();

        var single = Assert.Single(result);
        Assert.Equal("This org's body", single.Body);
    }
}
