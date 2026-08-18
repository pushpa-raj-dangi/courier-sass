using Courier.Audit.Application;
using Courier.Audit.Domain;
using Courier.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Audit.Tests;

file class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
    }
}

public class AuditLoggerTests
{
    [Fact]
    public async Task LogAsync_PersistsAuditLogRow()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new TestDbContext(options);
        var logger = new AuditLogger(dbContext);
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await logger.LogAsync(new AuditEntry(orgId, userId, "user.login", "User", userId.ToString()));

        var saved = await dbContext.Set<AuditLog>().SingleAsync();
        Assert.Equal("user.login", saved.Action);
        Assert.Equal(orgId, saved.OrganizationId);
        Assert.Equal(userId, saved.UserId);
        Assert.True(saved.Timestamp > DateTimeOffset.UtcNow.AddMinutes(-1));
    }
}
