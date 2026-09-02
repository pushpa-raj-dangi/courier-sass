using Courier.Identity.Domain;
using Courier.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Identity.Tests;

public class UserInviteConfigurationTests
{
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new UserInviteConfiguration());
        }
    }

    [Fact]
    public async Task UserInvite_RoundTripsThroughEfCore()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var dbContext = new TestDbContext(options);
        dbContext.Database.EnsureCreated();

        var invite = new UserInvite
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), TokenHash = "hashed-token-value",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7), AcceptedAt = null
        };
        dbContext.Set<UserInvite>().Add(invite);
        await dbContext.SaveChangesAsync();

        var persisted = await dbContext.Set<UserInvite>().SingleAsync(i => i.Id == invite.Id);
        Assert.Equal("hashed-token-value", persisted.TokenHash);
        Assert.Null(persisted.AcceptedAt);
    }
}
